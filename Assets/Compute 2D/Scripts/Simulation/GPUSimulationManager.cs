using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

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
    [SerializeField] private bool paused;
    [SerializeField] private SimulationSettings settings;
    [SerializeField] public float particleRadius;
    [SerializeField] private int targetFrameRate;
    [SerializeField] private bool useRealDeltaTime;

    [SerializeField] private ParticleRender render;
    [SerializeField] private Spawn2DParticles spawn;
    [SerializeField] private ComputeShader compute;

    [HideInInspector] public int numParticles;

    public readonly Dictionary<string, ComputeBuffer> buffers =
    new()
    {
        { "Positions", null },
        { "PrevPositions", null },
        { "ForceBuffersX", null },
        { "ForceBuffersY", null },
        { "Velocities", null },
        { "Densities", null },
        { "NearDensities", null },
        { "Springs", null },
        { "SpatialHashes", null },
        { "SpatialHashesScratch", null },
        { "SortRanges", null },
        { "SortCounters", null },
        { "DebugFloat", null },
        { "DebugInt", null }
    };

    private readonly Dictionary<string, int> KernelIDs =
    new()
    {
        { "ExternalForces", 0 },
        { "ClearForceBuffers", 1 },
        { "DoubleDensityRelaxation", 2 },
        { "ApplyForceBuffers", 3 },
        { "ApplyForceBuffersToVelocities", 4 },
        { "ApplyViscosity", 5 },
        { "AdjustSprings", 6 },
        { "SpringDisplacements", 7 },
        { "ResolveBoundaries", 8 },
        { "AttractToMouse", 9 },
        { "AdvancePredictedPositions", 10 },
        { "CalculateVelocity", 11 },
        { "UpdateSpatialHash", 12 },
        { "SortHashes", 13 },
        { "CopySpatialHashes", 14 }
    };

    public ThreadGroups threadGropus;
    private const int float2Size = 8;
    private const int int2Size = 8;
    private const int int4Size = 16;
    private const int sortCounterLength = 2;
    private const float fakeDT = 1 / 60f;

    private readonly int debugLength = 100;
    private readonly uint[] sortCounterReadback = new uint[sortCounterLength];

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

        render.DrawParticles();
    }

    private void SimulationStep()
    {
        float dt = useRealDeltaTime ? Time.deltaTime : fakeDT;
        compute.SetFloat("dt", dt);

        Dispatch(KernelIDs["ExternalForces"]);

        Dispatch(KernelIDs["ClearForceBuffers"]);
        Dispatch(KernelIDs["ApplyViscosity"]);
        Dispatch(KernelIDs["ApplyForceBuffersToVelocities"]);

        Dispatch(KernelIDs["AdvancePredictedPositions"]);

        Dispatch(KernelIDs["AdjustSprings"]);
        Dispatch(KernelIDs["ClearForceBuffers"]);
        Dispatch(KernelIDs["SpringDisplacements"]);
        Dispatch(KernelIDs["ApplyForceBuffers"]);

        Dispatch(KernelIDs["ClearForceBuffers"]);
        Dispatch(KernelIDs["DoubleDensityRelaxation"]);
        Dispatch(KernelIDs["ApplyForceBuffers"]);

        if (Input.GetMouseButton(0))
        {
            compute.SetVector("mousePosition", GetMousePos());
            Dispatch(KernelIDs["AttractToMouse"]);
        }

        Dispatch(KernelIDs["ResolveBoundaries"]);
        Dispatch(KernelIDs["CalculateVelocity"]);
    }

    private void OnValidate()
    {
        if (buffers["Positions"] != null)
        {
            Application.targetFrameRate = targetFrameRate;
            UpdateComputeSettings();
        }
    }

    private void Setup()
    {
        Application.targetFrameRate = targetFrameRate;
        numParticles = spawn.GetNumberOfParticles();

        ReleaseBuffers();
        CreateBuffers();
        SetBuffers();
        SetComputeSettings();
        SetInitialBufferData();
        threadGropus = new ThreadGroups(compute, numParticles);
        Camera.main.orthographicSize = spawn.GetRealHalfBoundSize(0).y + 2;

        render.Setup(this);
    }

    #region Buffer helpers
    private void SetInitialBufferData()
    {
        buffers["Positions"].SetData(spawn.InitializePositions());
        buffers["PrevPositions"].SetData(spawn.InitializePreviousPositions());
        buffers["ForceBuffersX"].SetData(spawn.InitializeForceBuffers());
        buffers["ForceBuffersY"].SetData(spawn.InitializeForceBuffers());
        buffers["Velocities"].SetData(spawn.InitializeVelocities());
        buffers["Densities"].SetData(spawn.InitializeDensities());
        buffers["NearDensities"].SetData(spawn.InitializeNearDensities());
        buffers["Springs"].SetData(spawn.InitializeSprings());
        buffers["SpatialHashes"].SetData(new int2[numParticles]);
        buffers["SpatialHashesScratch"].SetData(new int2[numParticles]);
        buffers["SortRanges"].SetData(new int4[numParticles * 2]);
        buffers["SortCounters"].SetData(new uint[sortCounterLength]);

        buffers["DebugFloat"].SetData(new float[debugLength]);
        buffers["DebugInt"].SetData(new int[debugLength]);
    }

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
        compute.SetInt("activeSortRangeCount", 0);
        compute.SetInt("sortCurrentRangeBase", 0);
        compute.SetInt("sortNextRangeBase", numParticles);
        compute.SetInt("sortSourceIsSpatialHashes", 1);
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
        buffers["ForceBuffersX"] = new ComputeBuffer(numParticles, sizeof(int));
        buffers["ForceBuffersY"] = new ComputeBuffer(numParticles, sizeof(int));
        buffers["Velocities"] = new ComputeBuffer(numParticles, float2Size);
        buffers["Densities"] = new ComputeBuffer(numParticles, sizeof(float));
        buffers["NearDensities"] = new ComputeBuffer(numParticles, sizeof(float));
        buffers["Springs"] = new ComputeBuffer(spawn.GetSpringsLength(), sizeof(float));
        buffers["SpatialHashes"] = new ComputeBuffer(numParticles, int2Size);
        buffers["SpatialHashesScratch"] = new ComputeBuffer(numParticles, int2Size);
        buffers["SortRanges"] = new ComputeBuffer(numParticles * 2, int4Size);
        buffers["SortCounters"] = new ComputeBuffer(sortCounterLength, sizeof(uint));

        buffers["DebugFloat"] = new ComputeBuffer(debugLength, sizeof(float));
        buffers["DebugInt"] = new ComputeBuffer(debugLength, sizeof(int));
    }

    private void Dispatch(int kernelID)
    {
        compute.Dispatch(kernelID, threadGropus.x, threadGropus.y, threadGropus.z);
    }

    private void Dispatch(int kernelID, int groupsX)
    {
        compute.Dispatch(kernelID, groupsX, 1, 1);
    }

    // This is kept separate from SimulationStep because it requires CPU-side
    // coordination between GPU partition passes to advance the quicksort ranges.
    private void UpdateAndSortSpatialHashes()
    {
        if (numParticles < 2)
            return;

        Dispatch(KernelIDs["UpdateSpatialHash"]);

        buffers["SortRanges"].SetData(new[] { new int4(0, numParticles, 1, 0) }, 0, 0, 1);

        int activeRangeCount = 1;
        int currentRangeBase = 0;
        int nextRangeBase = numParticles;
        bool sourceIsSpatialHashes = true;
        uint[] clearedSortCounters = new uint[sortCounterLength];

        while (true)
        {
            buffers["SortCounters"].SetData(clearedSortCounters);
            compute.SetInt("activeSortRangeCount", activeRangeCount);
            compute.SetInt("sortCurrentRangeBase", currentRangeBase);
            compute.SetInt("sortNextRangeBase", nextRangeBase);
            compute.SetInt("sortSourceIsSpatialHashes", sourceIsSpatialHashes ? 1 : 0);

            Dispatch(KernelIDs["SortHashes"], activeRangeCount);
            buffers["SortCounters"].GetData(sortCounterReadback);

            if (sortCounterReadback[1] == 0)
                break;

            activeRangeCount = (int)sortCounterReadback[0];
            (currentRangeBase, nextRangeBase) = (nextRangeBase, currentRangeBase);
            sourceIsSpatialHashes = !sourceIsSpatialHashes;
        }

        if (sourceIsSpatialHashes)
            Dispatch(KernelIDs["CopySpatialHashes"]);
    }

    private void ReleaseBuffers()
    {
        foreach (var buffer in buffers)
            buffer.Value?.Release();
    }

    private void OnDestroy()
    {
        ReleaseBuffers();
    }
    #endregion

    private Vector4 GetMousePos() => new(
        Camera.main.ScreenToWorldPoint(Input.mousePosition).x,
        Camera.main.ScreenToWorldPoint(Input.mousePosition).y
    );

    #region Debug
    private void GetDebugFloat()
    {
        AsyncGPUReadback.Request(
            buffers["DebugFloat"],
            request =>
            {
                if (!request.hasError)
                {
                    var data = request.GetData<float>();
                    Debug.Log($"Add 2: Value init: ({data[0]}, {data[1]})");
                    Debug.Log($"Read 6: Scaled read value: ({data[4]}, {data[5]})");
                }

                else
                    Debug.Log("GPU simulation: Debug error");
            }
        );
    }

    private void GetDebugInt()
    {
        AsyncGPUReadback.Request(
            buffers["DebugInt"],
            request =>
            {
                if (!request.hasError)
                {
                    var data = request.GetData<int>();
                    Debug.Log($"Add 1: Force buffer init: ({data[0]}, {data[1]})");
                    Debug.Log($"Add 3: Value scaled: ({data[2]}, {data[3]})");
                    Debug.Log($"Add 4: Add result ({data[4]}, {data[5]})");
                    Debug.Log($"Read 5: Read value: ({data[6]}, {data[7]})");
                }

                else
                    Debug.Log("GPU simulation: Debug error");
            }
        );
    }
    #endregion
}

public class ThreadGroups
{
    public int x;
    public int y;
    public int z;

    public ThreadGroups(ComputeShader compute, int numParticles)
    {
        GetThreadGroups(compute, numParticles);
    }

    public void GetThreadGroups(ComputeShader compute, int numParticles)
    {
        compute.GetKernelThreadGroupSizes(0, out uint xt, out uint yt, out uint zt);
        x = (int)math.ceil((float)numParticles / xt);
        y = 1;
        z = 1;
    }
}
