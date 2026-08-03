using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;

[Serializable]
public struct SimulationSettings3D
{
    [Header("Simulation settings")]
    public float interactionRadius;
    public float gravity;
    public float mouseAttractiveness;
    public float mouseRadius;
    public float collisionDamping;

    [Header("Density")]
    public float stiffness;
    public float nearStiffness;
    public float restDensity;

    [Header("Viscosity")]
    public float highViscosity;
    public float lowViscosity;
}

public class SimulationManager : MonoBehaviour
{
    [Header("Simulation settings")]
    [SerializeField] private bool paused;
    [SerializeField] private SimulationSettings3D settings;
    [SerializeField] private int maxParticlesPerCell = 64;
    [SerializeField] public float particleRadius = 0.5f;
    [SerializeField] private int targetFrameRate = 120;
    [SerializeField] private bool useRealDeltaTime;
    [SerializeField] private float fakeFramerate = 120;

    [Header("References")]
    [SerializeField] private ComputeShader compute;
    [SerializeField] private Spawn3DParticles spawn;
    [SerializeField] private ParticleRenerer render;
    private SPValues3D SP;

    [HideInInspector] public int numParticles;
    public float InteractionRadius => settings.interactionRadius;

    private Dictionary<string, int> KernelIDs;
    public Dictionary<string, ComputeBuffer> Buffers = new();

    private int3 threadGroups;
    private int3 gridThreadGroups;

    private int clock;

    private void Start()
    {
        Setup();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.R))
            Setup();

        if (Input.GetKeyDown(KeyCode.Space))
            paused = !paused;

        if (!paused || Input.GetKeyDown(KeyCode.RightArrow))
            Watcher.ExecuteWithTimer("1. Time step", SimulationStep);

        else if (Input.GetKeyDown(KeyCode.PageDown))
        {
            for (int i = 0; i < 10; i++)
                Watcher.ExecuteWithTimer("1. Time step", SimulationStep);
        }

        render.DrawParticles();
    }

    private void SimulationStep()
    {
        float dt = useRealDeltaTime ? Time.deltaTime : 1 / fakeFramerate;
        compute.SetFloat("dt", dt);

        if (clock % 4 == 0)
        {
            compute.Dispatch(KernelIDs["ClearGrid"], gridThreadGroups);
            compute.Dispatch(KernelIDs["ClearNeighbours"], threadGroups);
            compute.Dispatch(KernelIDs["InitSpatialPartitoning"], threadGroups);
            compute.Dispatch(KernelIDs["SetNeighbours"], threadGroups);
            clock = 0;
        }
        clock++;

        compute.Dispatch(KernelIDs["ExternalForces"], threadGroups);

        compute.Dispatch(KernelIDs["ClearForceBuffers"], threadGroups);
        compute.Dispatch(KernelIDs["ApplyViscosity"], threadGroups);
        compute.Dispatch(KernelIDs["ApplyForceBuffersToVelocities"], threadGroups);

        compute.Dispatch(KernelIDs["AdvancePredictedPositions"], threadGroups);

        for (int i = 0; i < 2; i++)
        {
            compute.Dispatch(KernelIDs["ClearForceBuffers"], threadGroups);
            compute.Dispatch(KernelIDs["DoubleDensityRelaxation"], threadGroups);
            compute.Dispatch(KernelIDs["ApplyForceBuffers"], threadGroups);
        }

        if (Input.GetMouseButton(0))
        {
            compute.SetVector("mousePosition", GetMousePos());
            compute.Dispatch(KernelIDs["AttractToMouse"], threadGroups);
        }

        compute.Dispatch(KernelIDs["ResolveBoundaries"], threadGroups);

        compute.Dispatch(KernelIDs["CalculateVelocity"], threadGroups);
    }

    private void OnValidate()
    {
        if (Buffers.ContainsKey("Positions"))
        {
            if (Buffers["Positions"] != null)
            {
                Application.targetFrameRate = targetFrameRate;
                UpdateComputeSettings();
            }
        }
    }

    private void Setup()
    {
        ReleaseBuffers();

        KernelIDs = ComputeHelper.GetKernels(compute);
        Buffers = ComputeHelper.GetBuffers(compute);

        Application.targetFrameRate = targetFrameRate;

        float3 boundingBoxSize = spawn.GetBoundingBoxSize();
        SP = new(-boundingBoxSize / 2, boundingBoxSize / 2, settings.interactionRadius);

        numParticles = spawn.GetNumberOfParticles();

        CreateBuffers();

        SetBuffers();
        SetComputeSettings();

        threadGroups = compute.GetThreadGroups(0, numParticles);
        gridThreadGroups = compute.GetThreadGroups(KernelIDs["ClearGrid"], SP.NumCells);

        clock = 0;

        render.Setup(this);
    }

    #region Buffer helpers
    private void UpdateComputeSettings()
    {
        compute.SetFloat("interactionRadius", settings.interactionRadius);
        compute.SetFloat("interactionRadiusSq", settings.interactionRadius * settings.interactionRadius);
        compute.SetFloat("gravity", settings.gravity);
        compute.SetFloat("mouseAttractiveness", settings.mouseAttractiveness);
        compute.SetFloat("mouseRadius", settings.mouseRadius);
        compute.SetFloat("collisionDamp", settings.collisionDamping);

        compute.SetFloat("stiffness", settings.stiffness);
        compute.SetFloat("nearStiffness", settings.nearStiffness);
        compute.SetFloat("restDensity", settings.restDensity);

        compute.SetFloat("highViscosity", settings.highViscosity);
        compute.SetFloat("lowViscosity", settings.lowViscosity);
    }

    private void SetComputeSettings()
    {
        compute.SetInt("numParticles", numParticles);
        UpdateComputeSettings();

        float3 rhbs = spawn.GetRealHalfBoundSize(particleRadius);
        compute.SetVector("realHalfBoundSize", new Vector4(rhbs.x, rhbs.y, rhbs.z));
        compute.SetFloat("particleRadius", particleRadius);

        compute.SetInt("numCells", SP.NumCells);
        compute.SetInt("maxParticlesPerCell", maxParticlesPerCell);
        compute.SetVector("offset", new(SP.offset.x, SP.offset.y, SP.offset.z, 0));
        compute.SetFloat("cellLength", SP.length);
        compute.SetInt("columns", SP.columns);
        compute.SetInt("rows", SP.rows);
        compute.SetInt("layers", SP.layers);
    }

    private void SetBuffers()
    {
        foreach (var kernel in KernelIDs)
            foreach (var buffer in Buffers)
                compute.SetBuffer(kernel.Value, buffer.Key, buffer.Value);
    }

    private void CreateBuffers()
    {
        if (numParticles == 0)
            Debug.LogWarning("Simulation manager: there are 0 particles. Creating non-existant buffers.");

        // 3D counts grow cubically: 8M particles overflow the Neighbours size (numParticles * maxParticlesPerCell * 9)
        // past int.MaxValue and the allocations reach gigabytes, which aborts Setup before any buffer is bound
        long neighboursCount = (long)numParticles * maxParticlesPerCell * 9;
        if (neighboursCount > int.MaxValue)
            Debug.LogError($"Simulation manager: Neighbours buffer would need {neighboursCount} elements (> int.MaxValue). Reduce particleCubeLength or maxParticlesPerCell or every kernel will report unset properties.");

        Buffers["Positions"] = ComputeHelper.CreateStructuredBufferWithData(spawn.InitializePositions());
        Buffers["PrevPositions"] = ComputeHelper.CreateStructuredBufferWithData<float4>(numParticles);
        Buffers["ForceBuffersX"] = ComputeHelper.CreateStructuredBufferWithData<int>(numParticles);
        Buffers["ForceBuffersY"] = ComputeHelper.CreateStructuredBufferWithData<int>(numParticles);
        Buffers["ForceBuffersZ"] = ComputeHelper.CreateStructuredBufferWithData<int>(numParticles);
        Buffers["Velocities"] = ComputeHelper.CreateStructuredBufferWithData<float4>(numParticles);

        Buffers["Densities"] = ComputeHelper.CreateStructuredBufferWithData<float>(numParticles);
        Buffers["NearDensities"] = ComputeHelper.CreateStructuredBufferWithData<float>(numParticles);

        Buffers["Grid"] = ComputeHelper.CreateStructuredBufferWithData<uint>(SP.NumCells * maxParticlesPerCell);
        Buffers["Neighbours"] = ComputeHelper.CreateStructuredBufferWithData<uint>(numParticles * maxParticlesPerCell * 9);
        Buffers["CellsLength"] = ComputeHelper.CreateStructuredBufferWithData<uint>(SP.NumCells);
        Buffers["NeighboursLength"] = ComputeHelper.CreateStructuredBufferWithData<uint>(numParticles);

        // Read-only aliases: the same buffers bound under the SRV names that
        // DoubleDensityRelaxation and ApplyViscosity read through (D3D11.0 UAV limit)
        Buffers["PositionsRO"] = Buffers["Positions"];
        Buffers["VelocitiesRO"] = Buffers["Velocities"];
        Buffers["NeighboursRO"] = Buffers["Neighbours"];
        Buffers["NeighboursLengthRO"] = Buffers["NeighboursLength"];
    }

    private void ReleaseBuffers()
    {
        // Distinct: the RO aliases share buffer instances with their RW originals
        foreach (var buffer in Buffers.Values.Distinct())
            ComputeHelper.Release(buffer);
    }

    private void OnDestroy()
    {
        ReleaseBuffers();
    }

#if UNITY_EDITOR
    private void OnEnable()
    {
        UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += ReleaseBuffers;
    }

    private void OnDisable()
    {
        UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= ReleaseBuffers;
    }
#endif

    #endregion

    // The cursor ray intersected with the z = 0 plane, so mouse attraction acts there
    private Vector4 GetMousePos()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        new Plane(Vector3.forward, 0).Raycast(ray, out float enter);
        Vector3 point = ray.GetPoint(enter);
        return new(point.x, point.y, point.z, 0);
    }
}
