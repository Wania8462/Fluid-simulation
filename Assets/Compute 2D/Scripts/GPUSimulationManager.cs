using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

[Serializable]
public struct SimulationSettings
{
    [Header("Simulation settings")]
    public float interactionRadius;
    public float gravity;
    public float mouseAttractiveness;
    public float mouseRadius;
    public float collisionDamping;
    public bool useParticlesAsBorder;

    // [Header("Body settings")]
    // public Body body;

    [Header("Density")]
    public float stiffness;
    public float nearStiffness;
    public float borderStiffness;
    public float restDensity;

    [Header("Springs")]
    public float springInteractionRadius;
    public float springRadius;
    public float springStiffness;
    public float springDeformationLimit;
    public float plasticity;
    public float highViscosity;
    public float lowViscosity;
}

public class GPUSimulationManager : MonoBehaviour
{
    [SerializeField] private bool paused;
    [SerializeField] private SimulationSettings settings;
    [SerializeField] public float particleRadius;
    [SerializeField] private int targetFrameRate;
    [SerializeField] private bool useRealDeltaTime;

    [SerializeField] private GPURender render;
    [SerializeField] private Spawn2DParticles spawn;
    [SerializeField] private ComputeShader compute;

    [HideInInspector] public int numParticles;

    public readonly Dictionary<string, ComputeBuffer> buffers =
    new()
    {
        { "Positions", null },
        { "PrevPositions", null },
        { "Velocities", null },
        { "Densities", null },
        { "NearDensities", null },
        { "Springs", null },
    };

    private readonly Dictionary<string, int> KernelIDs =
    new()
    {
        { "ExternalForces", 0 },
        { "DoubleDensityRelaxation", 1 },
        { "ApplyViscosity", 2 },
        { "AdjustSprings", 3 },
        { "SpringDisplacements", 4 },
        { "ResolveBoundaries", 5 },
        { "AttractToMouse", 6 },
        { "AdvancePredictedPositions", 7 },
        { "CalculateVelocity", 8 }
    };

    public TheradGroups threadGropus;
    private const int floatSize = 4;
    private const int float2Size = 8;
    private const float fakeDT = 1 / 60f;

    private void Start()
    {
        Application.targetFrameRate = targetFrameRate;
        numParticles = spawn.GetNumberOfParticles();

        CreateBuffers();
        SetBuffers();
        SetComputeSettings();
        SetInitialBufferData();
        GetThreadGroups();
        Camera.main.orthographicSize = spawn.GetRealHalfBoundSize(0).y + 2;

        render.Setup();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
            paused = !paused;

        if (!paused || Input.GetKeyDown(KeyCode.RightArrow))
            SimulationStep();

        render.DrawParticles();
    }

    private void SimulationStep()
    {
        float dt = useRealDeltaTime ? Time.deltaTime : fakeDT;
        compute.SetFloat("dt", dt);
        compute.SetVector("mousePosition", new Vector4(Input.mousePosition.x, Input.mousePosition.y));

        Dispatch(KernelIDs["ExternalForces"]);
        Dispatch(KernelIDs["ApplyViscosity"]);
        Dispatch(KernelIDs["AdvancePredictedPositions"]);
        // Dispatch(KernelIDs["AdjustSprings"]);
        // Dispatch(KernelIDs["SpringDisplacements"]);
        Dispatch(KernelIDs["DoubleDensityRelaxation"]);

        // if (Input.GetMouseButton(0))
        //     Dispatch(KernelIDs["AttractToMouse"]);

        Dispatch(KernelIDs["ResolveBoundaries"]);
        Dispatch(KernelIDs["CalculateVelocity"]);
    }

    private void OnValidate()
    {
        // todo add check if the settings have changed and only update if they have
        if (buffers["Positions"] != null)
        {
            UpdateComputeSettings();
        }
    }

    private void SetInitialBufferData()
    {
        buffers["Positions"].SetData(spawn.InitializePositions());
        buffers["PrevPositions"].SetData(spawn.InitializePositions());
        buffers["Velocities"].SetData(spawn.InitializeVelocities());
        buffers["Densities"].SetData(spawn.InitializeDensities());
        buffers["NearDensities"].SetData(spawn.InitializeNearDensities());
        buffers["Springs"].SetData(spawn.InitializeSprings());
    }

    private void UpdateComputeSettings()
    {
        compute.SetFloat("interactionRadius", settings.interactionRadius);
        compute.SetFloat("gravity", settings.gravity);
        compute.SetFloat("mouseAttractiveness", settings.mouseAttractiveness);
        compute.SetFloat("mouseRadius", settings.mouseRadius);
        compute.SetFloat("collisionDamp", settings.collisionDamping);

        compute.SetFloat("stiffness", settings.stiffness);
        compute.SetFloat("nearStiffness", settings.nearStiffness);
        compute.SetFloat("restDensity", settings.restDensity);

        compute.SetFloat("springInteractionRadius", settings.springInteractionRadius);
        compute.SetFloat("springStiffness", settings.springStiffness);
        compute.SetFloat("springDeformationLimit", settings.springDeformationLimit);
        compute.SetFloat("plasticity", settings.plasticity);
        compute.SetFloat("highViscosity", settings.highViscosity);
        compute.SetFloat("lowViscosity", settings.lowViscosity);
    }

    private void SetComputeSettings()
    {
        compute.SetInt("numParticles", numParticles);
        UpdateComputeSettings();
        float2 rhbs = spawn.GetRealHalfBoundSize(particleRadius);
        compute.SetVector("realHalfBoundSize", new Vector4(rhbs.x, rhbs.y));
        compute.SetFloat("particleRadius", particleRadius);
    }

    private void SetBuffers()
    {
        foreach (var kernel in KernelIDs)
            foreach (var buffer in buffers)
                compute.SetBuffer(kernel.Value, buffer.Key, buffer.Value);
    }

    private void CreateBuffers()
    {
        buffers["Positions"] = new ComputeBuffer(numParticles, float2Size);
        buffers["PrevPositions"] = new ComputeBuffer(numParticles, float2Size);
        buffers["Velocities"] = new ComputeBuffer(numParticles, float2Size);
        buffers["Densities"] = new ComputeBuffer(numParticles, floatSize);
        buffers["NearDensities"] = new ComputeBuffer(numParticles, floatSize);
        // Double the size if need more springs
        buffers["Springs"] = new ComputeBuffer(numParticles * 50, floatSize);
    }

    private void Dispatch(int kernelID)
    {
        compute.Dispatch(kernelID, threadGropus.x, threadGropus.y, threadGropus.y);
    }

    private void GetThreadGroups()
    {
        compute.GetKernelThreadGroupSizes(0, out uint x, out uint y, out uint z);
        threadGropus.x = (int)x;
        threadGropus.y = (int)y;
        threadGropus.z = (int)z;
    }

    private void OnDestroy()
    {
        foreach (var buffer in buffers)
        {
            buffer.Value?.Release();
        }
    }
}

public struct TheradGroups
{
    public int x;
    public int y;
    public int z;
}