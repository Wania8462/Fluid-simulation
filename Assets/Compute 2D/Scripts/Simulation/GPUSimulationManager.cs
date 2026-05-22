using System;
using System.Collections.Generic;
using System.Text;
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
    [SerializeField] public float particleRadius;
    [SerializeField] private int targetFrameRate;
    [SerializeField] private bool useRealDeltaTime;

    [Header("References")]
    [SerializeField] private ComputeShader compute;
    [SerializeField] private Spawn2DParticles spawn;
    [SerializeField] private ParticleRender render;
    private SPValues SP;

    [HideInInspector] public int numParticles;

    private Dictionary<string, int> KernelIDs;

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

    private List<GameObject> squares = new();

    private void Start()
    {
        Setup();
    }

    private void Update()
    {
        SP.Draw(Color.green);

        if (Input.GetKeyDown(KeyCode.R))
            Setup();

        if (Input.GetKeyDown(KeyCode.Space))
            paused = !paused;

        if (!paused || Input.GetKeyDown(KeyCode.RightArrow))
            SimulationStep();

        if (Input.GetKeyDown(KeyCode.W))
        {
            foreach (var square in squares)
                Destroy(square);

            var neighbourIndices = GetNeighboursIndicesDebug(GetMousePos());
            Debug.Log(neighbourIndices.Count);
            var neighbourPositons = GetNeighbourPositionsDebug(neighbourIndices);
            DrawPointsDebug(neighbourPositons);
        }

        var neighbours = GetNeighboursIndicesDebug(100);
        render.DrawParticles(neighbours);
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
        KernelIDs = ComputeHelper.GetKernels(compute);
        Application.targetFrameRate = targetFrameRate;
        float2 boundingBoxSize = spawn.GetBoundingBoxSize();
        SP = new(
            new(-boundingBoxSize.x / 2, -boundingBoxSize.y / 2),
            new(boundingBoxSize.x / 2, boundingBoxSize.y / 2),
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
#if UNITY_EDITOR
    readonly int2[] offsets2D = new int2[]
    {
        new(-1, 1),
        new(0, 1),
        new(1, 1),
        new(-1, 0),
        new(0, 0),
        new(1, 0),
        new(-1, -1),
        new(0, -1),
        new(1, -1),
    };

    private void TempNighboursTest()
    {
        float2[] positions = ComputeHelper.GetBuffer<float2>(buffers["Positions"]);
        int[,] grid = ComputeHelper.GetTextureAs2DArr<int>(textures["Grid"]);
        int[,] neighbours = ComputeHelper.GetTextureAs2DArr<int>(textures["Neighbours"]);
        int[] cellsLength = ComputeHelper.GetBuffer<int>(buffers["CellsLength"]);
        int[] neighboursLength = ComputeHelper.GetBuffer<int>(buffers["NeighboursLength"]);

        // Init SP
        for (int i = 0; i < neighboursLength.Length; i++)
        {
            int cellIndex = GetCellIndex(positions[i]);

            if (cellsLength[cellIndex] < maxParticlesPerCell)
            {
                grid[cellIndex, cellsLength[cellIndex]] = i;
                cellsLength[cellIndex]++;
            }
        }

        // Set neighbours
        for (int i = 0; i < neighboursLength.Length; i++)
        {
            int2 cellPosition = GetCellPosition(positions[i]);
            int cellIndex = cellPosition.x + cellPosition.y * SP.columns;

            for (int j = 0; j < 9; j++)
            {
                int2 neighbourPos = cellPosition + offsets2D[j];
                if (neighbourPos.x < 0 || neighbourPos.y < 0) continue;
                int neighbourIndex = neighbourPos.x + neighbourPos.y * SP.columns;

                for (int k = 0; k < cellsLength[neighbourIndex]; k++)
                {
                    neighbours[i, neighboursLength[i]] = grid[neighbourIndex, k];
                    neighboursLength[i]++;
                }
            }
        }

        StringBuilder sb = new("Neighbours: ");
        for (int i = 0; i < neighboursLength[100]; i++)
            sb.Append($"{neighbours[100, i]}, ");

        sb.Remove(sb.Length - 3, sb.Length - 1);
        Debug.Log(sb);
        sb.Clear();

        sb.Append("Positions: ");
        for (int i = 0; i < neighboursLength[100]; i++)
            sb.Append($"{positions[neighbours[100, i]]}, ");

        sb.Remove(sb.Length - 2, sb.Length);
        Debug.Log(sb);
    }

    int2 GetCellPosition(float2 position)
    {
        float2 scaled = (position - SP.offset) / SP.length;
        return new int2((int)Mathf.Clamp(scaled.x, 0, SP.columns - 1),
                     (int)Mathf.Clamp(scaled.y, 0, SP.rows - 1));
    }

    int GetCellIndex(float2 position)
    {
        int2 cellPosition = GetCellPosition(position);
        return cellPosition.x + cellPosition.y * SP.columns;
    }

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
        var neighboursLength = ComputeHelper.GetBuffer<int>(buffers["NeighboursLength"], index);
        var neighboursIndices = ComputeHelper.GetTextureStripe<int>(textures["Neighbours"], index);

        if (neighboursLength < neighboursIndices.Count)
            neighboursIndices.RemoveRange(neighboursLength + 1, neighboursIndices.Count - neighboursLength - 1);

        return neighboursIndices;
    }


    private List<float2> GetNeighbourPositionsDebug(List<int> indices)
    {
        var positions = ComputeHelper.GetBuffer<float2>(buffers["Positions"]);
        var result = new List<float2>();

        for (int i = 0; i < indices.Count; i++)
            result.Add(positions[i]);

        return result;
    }

    private List<int> GetNeighboursIndicesDebug(Vector4 position) => GetNeighboursIndicesDebug(new float2(position.x, position.y));

    private void DrawSPGradient()
    {
        // Instantiate(sprite, new Vector3(-boundingBoxSize.x / 2, -boundingBoxSize.y / 2), quaternion.identity);
        // Instantiate(sprite, new Vector3(boundingBoxSize.x / 2, boundingBoxSize.y / 2), quaternion.identity);
        var prevScale = sprite.transform.localScale;
        sprite.transform.localScale = new Vector3(SP.length, SP.length, 1);
        for (int i = 0; i < SP.columns; i++)
            for (int j = 0; j < SP.rows; j++)
            {
                var pos = new Vector3(SP.offset.x + (SP.length / 2), SP.offset.y + (SP.length / 2));
                pos.x += SP.length * i;
                pos.y += SP.length * j;
                var square = Instantiate(sprite, pos, quaternion.identity);
                square.GetComponent<SpriteRenderer>().color = new Color(0, (float)i / SP.columns, (float)j / SP.rows);
            }

        sprite.transform.localScale = prevScale;
    }

    private void DrawPointsDebug(List<float2> positions)
    {
        foreach (var pos in positions)
            squares.Add(Instantiate(sprite, new Vector3(pos.x, pos.y), quaternion.identity));
    }
#endif
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

    public void Draw(Color color)
    {
        for (var i = 0; i <= columns; i++)
        {
            Vector3 start = new(offset.x + (length * i), -offset.y, 0);
            Vector3 end = new(offset.x + (length * i), offset.y, 0);
            Debug.DrawLine(start, end, color);
        }

        for (var i = 0; i <= rows; i++)
        {
            Vector3 start = new(-offset.x, offset.y + (length * i), 0);
            Vector3 end = new(offset.x, offset.y + (length * i), 0);
            Debug.DrawLine(start, end, color);
        }
    }
}