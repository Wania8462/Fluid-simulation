using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

enum RenderingType
{
    Particles,
    DensityMap,
    MarchingSquares
}

public struct Spring
{
    public int neighbourIndex;
    public float restLength;
}

[Serializable]
public struct SimulationSettings
{
    [Header("Simulation settings")]
    public float interactionRadius;
    public float gravity;
    public float mouseAttractiveness;
    public float mouseRadius;
    public float collisionDamping;

    // [Header("Body settings")]
    // public Body body;

    [Header("Density")]
    public float stiffness;
    public float nearStiffness;
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
    public GameObject sprite;
    [Header("Simulation settings")]
    [SerializeField] private bool paused;
    [SerializeField] private SimulationSettings settings;
    [SerializeField] private int maxParticlesPerCell;
    [SerializeField] private int maxSpringsPerParticle;
    [SerializeField] public float particleRadius;
    [SerializeField] private int targetFrameRate;
    [SerializeField] private bool useRealDeltaTime;
    [SerializeField] private RenderingType renderingType;

    [Header("References")]
    [SerializeField] private ComputeShader compute;
    [SerializeField] private Spawn2DParticles spawn;
    [SerializeField] private ParticleRender render;
    [SerializeField] private RenderDensityMap densityMap;
    [SerializeField] private RenderMarchingSquares marchingSquares;
    [SerializeField] private Material debugMaterial;
    private RenderDebug renderDebug;
    private SPValues SP;

    [HideInInspector] public int numParticles;
    public float InteractionRadius => settings.interactionRadius;

    private Dictionary<string, int> KernelIDs;
    public Dictionary<string, ComputeBuffer> Buffers = new();

    private int3 threadGropus;
    private int3 gridThreadGropus;

    private readonly int debugLength = 100;
    private const float fakeDT = 1 / 120f;


    private float _maxForce = 0f;

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
            SimulationStep();

        else if (Input.GetKeyDown(KeyCode.PageDown))
        {
            for (int i = 0; i < 10; i++)
                SimulationStep();
        }

        if (renderingType == RenderingType.Particles)
            render.DrawParticles();

        else if (renderingType == RenderingType.DensityMap)
            densityMap.Draw();

        else
            marchingSquares.Draw();
    }

    private void SimulationStep()
    {
        float dt = useRealDeltaTime ? Time.deltaTime : fakeDT;
        compute.SetFloat("dt", dt);

        compute.Dispatch(KernelIDs["ClearGrid"], gridThreadGropus);
        compute.Dispatch(KernelIDs["ClearNeighbours"], threadGropus);
        compute.Dispatch(KernelIDs["InitSpatialPartitoning"], threadGropus);
        compute.Dispatch(KernelIDs["SetNeighbours"], threadGropus);

        compute.Dispatch(KernelIDs["ExternalForces"], threadGropus);

        compute.Dispatch(KernelIDs["ClearForceBuffers"], threadGropus);
        compute.Dispatch(KernelIDs["ApplyViscosity"], threadGropus);
        compute.Dispatch(KernelIDs["ApplyForceBuffersToVelocities"], threadGropus);

        compute.Dispatch(KernelIDs["AdvancePredictedPositions"], threadGropus);

        // compute.Dispatch(KernelIDs["AdjustSprings"], threadGropus);
        // compute.Dispatch(KernelIDs["ClearForceBuffers"], threadGropus);
        // compute.Dispatch(KernelIDs["SpringDisplacements"], threadGropus);
        // compute.Dispatch(KernelIDs["ApplyForceBuffers"], threadGropus);

        compute.Dispatch(KernelIDs["ClearForceBuffers"], threadGropus);
        compute.Dispatch(KernelIDs["DoubleDensityRelaxation"], threadGropus);
        compute.Dispatch(KernelIDs["ApplyForceBuffers"], threadGropus);

        if (Input.GetMouseButton(0))
        {
            compute.SetVector("mousePosition", GetMousePos());
            compute.Dispatch(KernelIDs["AttractToMouse"], threadGropus);
        }

        compute.Dispatch(KernelIDs["ResolveBoundaries"], threadGropus);
        compute.Dispatch(KernelIDs["CalculateVelocity"], threadGropus);
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
        renderDebug = new(debugMaterial);

        float2 boundingBoxSize = spawn.GetBoundingBoxSize();
        SP = new(
            new(-boundingBoxSize.x / 2, -boundingBoxSize.y / 2),
            new(boundingBoxSize.x / 2, boundingBoxSize.y / 2),
            settings.interactionRadius);

        numParticles = spawn.GetNumberOfParticles();

        CreateBuffers();

        SetBuffers();
        SetComputeSettings();

        threadGropus = compute.GetThreadGroups(0, numParticles);
        gridThreadGropus = compute.GetThreadGroups(KernelIDs["ClearGrid"], SP.columns * SP.rows);

        Camera.main.orthographicSize = math.max(spawn.GetRealHalfBoundSize(0).y + 2, spawn.GetRealHalfBoundSize(0).x - 237);
        render.Setup(this);
        densityMap.Setup(this, boundingBoxSize);
        marchingSquares.Setup(this, boundingBoxSize);
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

        compute.SetFloat("springInteractionRadius", settings.springInteractionRadius);
        compute.SetFloat("springRadius", settings.springRadius);
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

        compute.SetInt("numCells", SP.columns * SP.rows);
        compute.SetInt("maxParticlesPerCell", maxParticlesPerCell);
        compute.SetInt("maxSpringsPerParticle", maxSpringsPerParticle);
        compute.SetVector("offset", new(SP.offset.x, SP.offset.y));
        compute.SetFloat("length", SP.length);
        compute.SetInt("columns", SP.columns);
        compute.SetInt("rows", SP.rows);
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
            Debug.LogWarning("GPU simulation manager: there are 0 particles. Creating non-existant buffers.");

        Buffers["Positions"] = ComputeHelper.CreateStructuredBufferWithData(spawn.InitializePositions());
        Buffers["PrevPositions"] = ComputeHelper.CreateStructuredBufferWithData<float2>(numParticles);
        Buffers["ForceBuffersX"] = ComputeHelper.CreateStructuredBufferWithData<int>(numParticles);
        Buffers["ForceBuffersY"] = ComputeHelper.CreateStructuredBufferWithData<int>(numParticles);
        Buffers["Velocities"] = ComputeHelper.CreateStructuredBufferWithData<float2>(numParticles);

        Buffers["Densities"] = ComputeHelper.CreateStructuredBufferWithData<float>(numParticles);
        Buffers["NearDensities"] = ComputeHelper.CreateStructuredBufferWithData<float>(numParticles);

        // Buffers["Springs"] = ComputeHelper.CreateStructuredBufferWithData<Spring>(numParticles * maxSpringsPerParticle);
        // Buffers["SpringLengths"] = ComputeHelper.CreateStructuredBufferWithData<uint>(numParticles);

        Buffers["Grid"] = ComputeHelper.CreateStructuredBufferWithData<uint>(SP.columns * SP.rows * maxParticlesPerCell);
        Buffers["Neighbours"] = ComputeHelper.CreateStructuredBufferWithData<uint>(numParticles * maxParticlesPerCell * 3);
        Buffers["CellsLength"] = ComputeHelper.CreateStructuredBufferWithData<uint>(SP.columns * SP.rows);
        Buffers["NeighboursLength"] = ComputeHelper.CreateStructuredBufferWithData<uint>(numParticles);

        Buffers["DebugFloat"] = ComputeHelper.CreateStructuredBufferWithData<float>(debugLength);
        Buffers["DebugInt"] = ComputeHelper.CreateStructuredBufferWithData<float>(debugLength);
    }

    private void ReleaseBuffers()
    {
        foreach (var buffer in Buffers)
            ComputeHelper.Release(buffer.Value);
    }

    private void OnDestroy()
    {
        ReleaseBuffers();
    }

#if UNITY_EDITOR
    private void OnEnable()
    {
        AssemblyReloadEvents.beforeAssemblyReload += ReleaseBuffers;
    }

    private void OnDisable()
    {
        AssemblyReloadEvents.beforeAssemblyReload -= ReleaseBuffers;
    }
#endif

    #endregion

    private Vector4 GetMousePos() => new(
        Camera.main.ScreenToWorldPoint(Input.mousePosition).x,
        Camera.main.ScreenToWorldPoint(Input.mousePosition).y
    );

    #region Debug
#if UNITY_EDITOR

    private List<int> GetNeighboursIndicesDebug(float2 position)
    {
        float2 scaled = (position - SP.offset) / SP.length;
        int2 cellPosition = new((int)math.clamp(scaled.x, 0, SP.columns - 1),
                                (int)math.clamp(scaled.y, 0, SP.rows - 1));

        int cellIndex = cellPosition.x + cellPosition.y * SP.columns;
        throw new NotImplementedException();
    }

    private List<int> GetNeighboursIndicesDebug(int index)
    {
        var neighboursLength = ComputeHelper.GetBuffer<int>(Buffers["NeighboursLength"], index);
        var allNeighbours = ComputeHelper.GetBuffer<uint>(Buffers["Neighbours"]);

        var neighboursIndices = new List<int>();
        for (int i = 0; i < neighboursLength; i++)
            neighboursIndices.Add((int)allNeighbours[index + i * numParticles]);

        return neighboursIndices;
    }

    private List<int> GetNeighboursIndicesDebug(Vector4 position) => GetNeighboursIndicesDebug(new float2(position.x, position.y));

    private List<float2> GetPositionsDebug(List<int> indices)
    {
        var positions = ComputeHelper.GetBuffer<float2>(Buffers["Positions"]);
        var result = new List<float2>();

        for (int i = 0; i < indices.Count; i++)
            result.Add(positions[i]);

        return result;
    }

    private static string Commify(long n) =>
        n.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);

    private void LogMaxForce()
    {
        const float scale = 1_000_000f;
        int[] forceX = ComputeHelper.GetBuffer<int>(Buffers["ForceBuffersX"]);
        int[] forceY = ComputeHelper.GetBuffer<int>(Buffers["ForceBuffersY"]);

        float max = 0f;
        int maxIndex = 0;
        for (int i = 0; i < numParticles; i++)
        {
            float fx = forceX[i] / scale;
            float fy = forceY[i] / scale;
            float mag = Mathf.Sqrt(fx * fx + fy * fy);
            if (mag > max)
            {
                max = mag;
                maxIndex = i;
            }
        }

        if (max > _maxForce)
        {
            _maxForce = max;
            float percentX = Mathf.Abs(forceX[maxIndex]) / 2147483647f * 100;
            float percentY = Mathf.Abs(forceY[maxIndex]) / 2147483647f * 100;
            float maxPercent = percentX > percentY ? percentX : percentY;
            Debug.Log($"New max force magnitude: {max:F4}. Percent of int used: {maxPercent}% (particle {maxIndex}, raw x={Commify(forceX[maxIndex])}, y={Commify(forceY[maxIndex])})");
        }
    }
#endif
    #endregion
}