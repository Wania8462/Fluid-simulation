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
    [Header("Simulation settings")]
    [SerializeField] private bool paused;
    [SerializeField] private SimulationSettings settings;
    [SerializeField] private int maxParticlesPerCell;
    [SerializeField] public float particleRadius;
    [SerializeField] private int targetFrameRate;
    [SerializeField] private bool useRealDeltaTime;

    [Header("References")]
    [SerializeField] private ComputeShader compute;
    [SerializeField] private Spawn2DParticles spawn;
    [SerializeField] private ParticleRender render;
    private SPValues SP;

    [HideInInspector] public int numParticles;

    private readonly Dictionary<string, int> KernelIDs = new()
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

        { "ClearGrid", 12 },
        { "ClearNeighbours", 13 },
        { "InitSpatialPartitoning", 14 },
        { "SetNeighbours", 15 }
    };

    public readonly Dictionary<string, ComputeBuffer> buffers = new()
    {
        { "Positions", null },
        { "PrevPositions", null },
        { "ForceBuffersX", null },
        { "ForceBuffersY", null },
        { "Velocities", null },
        { "Densities", null },
        { "NearDensities", null },
        { "Springs", null },

        { "CellsLength", null },
        { "NeighboursLength", null },

        { "DebugFloat", null },
        { "DebugInt", null }
    };

    public readonly Dictionary<string, RenderTexture> textures = new()
    {
        { "Grid", null },
        { "Neighbours", null },
    };

    public int3 threadGropus;
    private const int float2Size = 8;
    private const float fakeDT = 1 / 60f;

    private readonly int debugLength = 100;

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

        // todo change it
        int3 groups = compute.GetThreadGroups(KernelIDs["ClearGrid"], SP.columns, SP.rows);
        compute.Dispatch(KernelIDs["ClearGrid"], groups);
        compute.Dispatch(KernelIDs["ClearNeighbours"], threadGropus);
        compute.Dispatch(KernelIDs["InitSpatialPartitoning"], threadGropus);
        compute.Dispatch(KernelIDs["SetNeighbours"], threadGropus);

        compute.Dispatch(KernelIDs["ExternalForces"], threadGropus);

        compute.Dispatch(KernelIDs["ClearForceBuffers"], threadGropus);
        compute.Dispatch(KernelIDs["ApplyViscosity"], threadGropus);
        compute.Dispatch(KernelIDs["ApplyForceBuffersToVelocities"], threadGropus);

        compute.Dispatch(KernelIDs["AdvancePredictedPositions"], threadGropus);

        compute.Dispatch(KernelIDs["AdjustSprings"], threadGropus);
        compute.Dispatch(KernelIDs["ClearForceBuffers"], threadGropus);
        compute.Dispatch(KernelIDs["SpringDisplacements"], threadGropus);
        compute.Dispatch(KernelIDs["ApplyForceBuffers"], threadGropus);

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
        if (buffers["Positions"] != null)
        {
            Application.targetFrameRate = targetFrameRate;
            UpdateComputeSettings();
        }
    }

    private void Setup()
    {
        Application.targetFrameRate = targetFrameRate;
        float2 boundingBoxSize = spawn.GetBoundingBoxSize();
        SP = new(
            new(-boundingBoxSize.x, -boundingBoxSize.y),
            new(boundingBoxSize.x, boundingBoxSize.y),
            settings.interactionRadius);
        numParticles = spawn.GetNumberOfParticles();

        ReleaseBuffers();
        CreateBuffers();
        SetBuffers();
        SetComputeSettings();
        threadGropus = compute.GetThreadGroups(0, numParticles);
        Camera.main.orthographicSize = spawn.GetRealHalfBoundSize(0).y + 2;

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
        compute.SetVector("offset", new(SP.offset.x, SP.offset.y));
        compute.SetFloat("length", SP.length);
        compute.SetInt("columns", SP.columns);
        compute.SetInt("rows", SP.rows);
    }

    private void SetBuffers()
    {
        foreach (var kernel in KernelIDs)
            foreach (var buffer in buffers)
                compute.SetBuffer(kernel.Value, buffer.Key, buffer.Value);

        foreach (var kernel in KernelIDs)
            foreach (var texture in textures)
                compute.SetTexture(kernel.Value, texture.Key, texture.Value);
    }

    private void CreateBuffers()
    {
        if (numParticles == 0)
            Debug.LogWarning("GPU simulation manager: there are 0 particles. Creating non-existant buffers.");

        buffers["Positions"] = ComputeHelper.CreateStructuredBufferWithData(spawn.InitializePositions());
        buffers["PrevPositions"] = ComputeHelper.CreateStructuredBufferWithData<float2>(numParticles);
        buffers["ForceBuffersX"] = ComputeHelper.CreateStructuredBufferWithData<int>(numParticles);
        buffers["ForceBuffersY"] = ComputeHelper.CreateStructuredBufferWithData<int>(numParticles);
        buffers["Velocities"] = ComputeHelper.CreateStructuredBufferWithData<float2>(numParticles);
        buffers["Densities"] = ComputeHelper.CreateStructuredBufferWithData<float>(numParticles);
        buffers["NearDensities"] = ComputeHelper.CreateStructuredBufferWithData<float>(numParticles);
        buffers["Springs"] = ComputeHelper.CreateStructuredBufferWithData(spawn.InitializeSprings());

        textures["Grid"] = ComputeHelper.CreateRenderTexture(SP.columns * SP.rows, maxParticlesPerCell);
        textures["Neighbours"] = ComputeHelper.CreateRenderTexture(numParticles, maxParticlesPerCell * 9);
        buffers["CellsLength"] = ComputeHelper.CreateStructuredBufferWithData<int>(SP.columns * SP.rows);
        buffers["NeighboursLength"] = ComputeHelper.CreateStructuredBufferWithData<int>(numParticles);

        buffers["DebugFloat"] = ComputeHelper.CreateStructuredBufferWithData<float>(debugLength);
        buffers["DebugInt"] = ComputeHelper.CreateStructuredBufferWithData<float>(debugLength);
    }

    private void ReleaseBuffers()
    {
        foreach (var buffer in buffers)
            buffer.Value?.Release();

        foreach (var texture in textures)
            texture.Value?.Release();
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

public class SPValues
{
    public float2 offset;
    public float length;
    public int columns;
    public int rows;

    public SPValues(float2 bottomLeft, float2 topRight, float length)
    {
        if (length <= 0)
            Debug.LogError($"SPValues: length must be > 0, got {length}");

        this.length = length;
        offset = bottomLeft;
        var width = topRight.x - bottomLeft.x;
        var height = topRight.y - bottomLeft.y;

        if (width <= 0 || height <= 0)
            Debug.LogWarning($"SPValues: grid dimensions are non-positive (width={width}, height={height}), likely caused by a degenerate bounding box");

        columns = (int)(width / length);
        rows = (int)(height / length);

        if (width % length != 0) columns++;
        if (height % length != 0) rows++;

        if (columns == 0 || rows == 0)
            Debug.LogWarning($"SPValues: grid has zero cells (columns={columns}, rows={rows}), neighbour queries will return nothing");
    }
}