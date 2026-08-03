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

public enum BoundaryShape
{
    Square,
    Circle
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

    [Header("Density")]
    public float stiffness;
    public float nearStiffness;
    public float restDensity;

    [Header("Springs")]
    public float plasticity;
    public float highViscosity;
    public float lowViscosity;

    [Header("Boundary body")]
    public BoundaryShape shape;
    public float sampleDensity;
    public float bodyRadius;
    public float2 bodyPosition;
    public float bodyRotationRad;
    public float boundaryFriction;
    public float boundaryBodyMass;
}

public class GPUSimulationManager : MonoBehaviour
{
    [Header("Simulation settings")]
    [SerializeField] private bool paused;
    [SerializeField] private SimulationSettings settings;
    [SerializeField] private int maxParticlesPerCell;
    [SerializeField] private int maxSpringsPerParticle;
    [SerializeField] public float particleRadius;
    [SerializeField] private int targetFrameRate;
    [SerializeField] private bool useRealDeltaTime;
    [SerializeField] private float fakeFramerate;
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
    [HideInInspector] public int numBoundaryParticles;
    public float InteractionRadius => settings.interactionRadius;

    private Dictionary<string, int> KernelIDs;
    public Dictionary<string, ComputeBuffer> Buffers = new();

    private float2[] boundaryPositions;
    private float2 boundaryRestCenter;

    private int3 threadGropus;
    private int3 gridThreadGropus;
    private int3 boundaryThreadGropus;

    private int clock;

    private readonly int debugLength = 100;

    private float _maxForce = 0f;
    private float _maxDisplacement = 0f;
    private int frameTotal = 0;
    private int frames = 0;

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

        if (renderingType == RenderingType.Particles)
        {
            render.DrawParticles();
            render.DrawBoundaryParticles();
        }

        else if (renderingType == RenderingType.DensityMap)
            densityMap.Draw();

        else
            marchingSquares.Draw();
    }

    private void SimulationStep()
    {
        float dt = useRealDeltaTime ? Time.deltaTime : 1 / fakeFramerate;
        compute.SetFloat("dt", dt);
        clock++;

        if (clock % 4 == 0)
        {
            compute.Dispatch(KernelIDs["ClearGrid"], gridThreadGropus);
            compute.Dispatch(KernelIDs["ClearNeighbours"], threadGropus);
            compute.Dispatch(KernelIDs["InitSpatialPartitoning"], threadGropus);
            compute.Dispatch(KernelIDs["SetNeighbours"], threadGropus);

            compute.Dispatch(KernelIDs["ClearBoundaryGrid"], gridThreadGropus);
            compute.Dispatch(KernelIDs["InitBoundarySpatialPartitoning"], boundaryThreadGropus);
            compute.Dispatch(KernelIDs["SetBoundaryNeighbours"], threadGropus);
            clock = 0;
        }

        compute.Dispatch(KernelIDs["ExternalForces"], threadGropus);
        compute.Dispatch(KernelIDs["BoundaryExternalForces"], boundaryThreadGropus);

        compute.Dispatch(KernelIDs["ClearForceBuffers"], threadGropus);
        compute.Dispatch(KernelIDs["ApplyViscosity"], threadGropus);
        compute.Dispatch(KernelIDs["ApplyForceBuffersToVelocities"], threadGropus);

        compute.Dispatch(KernelIDs["AdvancePredictedPositions"], threadGropus);
        compute.Dispatch(KernelIDs["BoundaryAdvancePredictedPositions"], boundaryThreadGropus);

        for (int i = 0; i < 2; i++)
        {
            compute.Dispatch(KernelIDs["ClearForceBuffers"], threadGropus);
            compute.Dispatch(KernelIDs["ClearBoundaryForceBuffers"], boundaryThreadGropus);
            compute.Dispatch(KernelIDs["DoubleDensityRelaxation"], threadGropus);
            compute.Dispatch(KernelIDs["ApplyForceBuffers"], threadGropus);
            compute.Dispatch(KernelIDs["ApplyBoundaryForceBuffers"], boundaryThreadGropus);
        }

        if (Input.GetMouseButton(0))
        {
            compute.SetVector("mousePosition", GetMousePos());
            compute.Dispatch(KernelIDs["AttractToMouse"], threadGropus);
        }

        if (Input.GetMouseButton(1))
        {
            compute.SetVector("mousePosition", GetMousePos());
            compute.Dispatch(KernelIDs["AttractToMouseBoundary"], boundaryThreadGropus);
        }

        // Rigid boundary body: maintain the shape, then resolve wall contacts
        compute.Dispatch(KernelIDs["FindRotationAngle"], 1);
        compute.Dispatch(KernelIDs["PlaceBoundaryParticles"], boundaryThreadGropus);
        compute.Dispatch(KernelIDs["RigidContactResolution"], 1);
        compute.Dispatch(KernelIDs["PlaceBoundaryParticles"], boundaryThreadGropus);
        compute.Dispatch(KernelIDs["CancelBoundaryBorderVelocity"], 1);

        // Project fluid particles out of the body, then clamp to the borders last
        string bodyCollisionKernel = settings.shape == BoundaryShape.Square
            ? "ResolveSquareBodyCollision"
            : "ResolveCircleBodyCollision";
        compute.Dispatch(KernelIDs[bodyCollisionKernel], threadGropus);
        compute.Dispatch(KernelIDs["ResolveBoundaries"], threadGropus);

        compute.Dispatch(KernelIDs["CalculateVelocity"], threadGropus);
        compute.Dispatch(KernelIDs["BoundaryCalculateVelocity"], boundaryThreadGropus);
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
            settings.interactionRadius + 1);

        numParticles = spawn.GetNumberOfParticles();

        boundaryPositions = settings.shape == BoundaryShape.Square
            ? spawn.InitRectangleOutlinePositions(
                width: settings.bodyRadius * 2,
                height: settings.bodyRadius * 2,
                settings.sampleDensity,
                settings.bodyPosition,
                settings.bodyRotationRad)
            : spawn.InitCircleOutlinePositions(
                settings.bodyRadius,
                settings.sampleDensity,
                settings.bodyPosition);

        numBoundaryParticles = boundaryPositions.Length;
        boundaryRestCenter = ComputeBodyCenter(boundaryPositions);

        CreateBuffers();

        SetBuffers();
        SetComputeSettings();

        threadGropus = compute.GetThreadGroups(0, numParticles);
        gridThreadGropus = compute.GetThreadGroups(KernelIDs["ClearGrid"], SP.columns * SP.rows);
        boundaryThreadGropus = compute.GetThreadGroups(0, numBoundaryParticles);

        compute.Dispatch(KernelIDs["ClearBoundaryGrid"], gridThreadGropus);
        compute.Dispatch(KernelIDs["InitBoundarySpatialPartitoning"], boundaryThreadGropus);
        compute.Dispatch(KernelIDs["CalculateBoundaryVolumes"], boundaryThreadGropus);

        Camera.main.orthographicSize = math.max(spawn.GetRealHalfBoundSize(0).y + 2, spawn.GetRealHalfBoundSize(0).x - 237);
        render.Setup(this);
        densityMap.Setup(this, boundingBoxSize);
        marchingSquares.Setup(this, boundingBoxSize);
    }

    private float2 ComputeBodyCenter(float2[] positions)
    {
        float2 restCenter = float2.zero;
        for (int i = 0; i < positions.Length; i++)
            restCenter += positions[i];

        return positions.Length > 0 ? restCenter / positions.Length : float2.zero;
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

        compute.SetFloat("plasticity", settings.plasticity);
        compute.SetFloat("highViscosity", settings.highViscosity);
        compute.SetFloat("lowViscosity", settings.lowViscosity);

        if (settings.boundaryFriction <= 0)
            UnityEngine.Debug.LogWarning($"GPU simulation manager: boundaryFriction is {settings.boundaryFriction}, fluid particles won't be slowed near the boundary body and may tunnel through it");

        if (settings.boundaryBodyMass <= 0)
            UnityEngine.Debug.LogWarning($"GPU simulation manager: boundaryBodyMass is {settings.boundaryBodyMass}, falling back to the neutral response (mass = boundary particle count)");

        compute.SetFloat("boundaryFriction", settings.boundaryFriction);
        compute.SetFloat("invBodyMass", settings.boundaryBodyMass > 0 ? numBoundaryParticles / settings.boundaryBodyMass : 1f);
        compute.SetFloat("bodyRadius", settings.bodyRadius);
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
        compute.SetFloat("cellLength", SP.length);
        compute.SetInt("columns", SP.columns);
        compute.SetInt("rows", SP.rows);

        compute.SetInt("numBoundaryParticles", numBoundaryParticles);
        compute.SetVector("boundaryRestCenter", new Vector4(boundaryRestCenter.x, boundaryRestCenter.y));
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

        Buffers["Grid"] = ComputeHelper.CreateStructuredBufferWithData<uint>(SP.columns * SP.rows * maxParticlesPerCell);
        Buffers["Neighbours"] = ComputeHelper.CreateStructuredBufferWithData<uint>(numParticles * maxParticlesPerCell * 3);
        Buffers["CellsLength"] = ComputeHelper.CreateStructuredBufferWithData<uint>(SP.columns * SP.rows);
        Buffers["NeighboursLength"] = ComputeHelper.CreateStructuredBufferWithData<uint>(numParticles);

        Buffers["BoundaryPositions"] = ComputeHelper.CreateStructuredBufferWithData(boundaryPositions);
        Buffers["BoundaryPrevPositions"] = ComputeHelper.CreateStructuredBufferWithData(boundaryPositions);
        Buffers["BoundaryRestPositions"] = ComputeHelper.CreateStructuredBufferWithData(boundaryPositions);
        Buffers["BoundaryVelocities"] = ComputeHelper.CreateStructuredBufferWithData<float2>(numBoundaryParticles);
        Buffers["BoundaryVolumes"] = ComputeHelper.CreateStructuredBufferWithData<float>(numBoundaryParticles);
        Buffers["BoundaryForceBuffersX"] = ComputeHelper.CreateStructuredBufferWithData<int>(numBoundaryParticles);
        Buffers["BoundaryForceBuffersY"] = ComputeHelper.CreateStructuredBufferWithData<int>(numBoundaryParticles);

        Buffers["BoundaryGrid"] = ComputeHelper.CreateStructuredBufferWithData<uint>(SP.columns * SP.rows * maxParticlesPerCell);
        Buffers["BoundaryCellsLength"] = ComputeHelper.CreateStructuredBufferWithData<uint>(SP.columns * SP.rows);
        Buffers["BoundaryNeighbours"] = ComputeHelper.CreateStructuredBufferWithData<uint>(numParticles * maxParticlesPerCell * 9);
        Buffers["BoundaryNeighboursLength"] = ComputeHelper.CreateStructuredBufferWithData<uint>(numParticles);

        Buffers["BodyState"] = ComputeHelper.CreateStructuredBufferWithData<float>(5);

        Buffers["DebugFloat"] = ComputeHelper.CreateStructuredBufferWithData<float>(debugLength);
        Buffers["DebugInt"] = ComputeHelper.CreateStructuredBufferWithData<float>(debugLength);

        // Read-only aliases: the same buffers bound under the SRV names that
        // DoubleDensityRelaxation and ApplyViscosity read through (D3D11.0 UAV limit)
        Buffers["PositionsRO"] = Buffers["Positions"];
        Buffers["VelocitiesRO"] = Buffers["Velocities"];
        Buffers["NeighboursRO"] = Buffers["Neighbours"];
        Buffers["NeighboursLengthRO"] = Buffers["NeighboursLength"];
        Buffers["BoundaryPositionsRO"] = Buffers["BoundaryPositions"];
        Buffers["BoundaryVelocitiesRO"] = Buffers["BoundaryVelocities"];
        Buffers["BoundaryVolumesRO"] = Buffers["BoundaryVolumes"];
        Buffers["BoundaryNeighboursRO"] = Buffers["BoundaryNeighbours"];
        Buffers["BoundaryNeighboursLengthRO"] = Buffers["BoundaryNeighboursLength"];
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
    // Retrieve some dummy data at the end of the function or it will only wait for the calls
    private void LogFrameData()
    {
        if (Watcher.Count % 1000 == 0)
        {
            frameTotal += (int)Watcher.GetTotal();
            frames++;
            UnityEngine.Debug.Log(frameTotal / frames);
        }
    }
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
            UnityEngine.Debug.Log($"New max force magnitude: {max:F4}. Percent of int used: {maxPercent}% (particle {maxIndex}, raw x={Commify(forceX[maxIndex])}, y={Commify(forceY[maxIndex])})");
        }
    }

    private void LogMaxDisplacement()
    {
        float2[] positions = ComputeHelper.GetBuffer<float2>(Buffers["Positions"]);
        float2[] prevPositions = ComputeHelper.GetBuffer<float2>(Buffers["PrevPositions"]);

        float max = 0f;
        int maxIndex = 0;
        float2 maxDisplacement = float2.zero;
        for (int i = 0; i < numParticles; i++)
        {
            float2 displacement = positions[i] - prevPositions[i];
            float largestComponent = math.max(math.abs(displacement.x), math.abs(displacement.y));
            if (largestComponent > max)
            {
                max = largestComponent;
                maxIndex = i;
                maxDisplacement = displacement;
            }
        }

        if (max > _maxDisplacement)
        {
            _maxDisplacement = max;
            UnityEngine.Debug.Log($"New max displacement vector: {maxDisplacement} (particle {maxIndex})");
        }
    }
#endif
    #endregion
}