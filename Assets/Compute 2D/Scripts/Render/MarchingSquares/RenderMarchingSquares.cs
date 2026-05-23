using Unity.Mathematics;
using UnityEngine;

public class RenderMarchingSquares : MonoBehaviour
{
    [SerializeField] private int resolution;
    [SerializeField] private float densityThreshold;
    [SerializeField] private Color color = Color.blue;
    [SerializeField] private ComputeShader compute;
    [SerializeField] private Material material;

    private int numCells;
    private int numVertices;
    private int2 gridDimensions;
    private float2 cellSize;

    private ComputeBuffer vertexPositionsBuffer;
    private ComputeBuffer vertexDensitiesBuffer;
    private ComputeBuffer caseCountsBuffer;
    private ComputeBuffer casePositionsBuffer;
    private ComputeBuffer caseDensitiesBuffer;
    private GraphicsBuffer commandBuf;

    private Mesh[] meshes;
    private RenderParams[] rp;

    private int3 vertexThreadGroups;
    private int3 cellThreadGroups;

    private int clearCaseCountsKernel;
    private int calcVertexDensitiesKernel;
    private int calcCasesKernel;
    private int updateIndirectArgsKernel;

    public void Setup(GPUSimulationManager sim, float2 bounds)
    {
        gridDimensions = new(
            (int)(resolution * (bounds.x / bounds.y)),
            resolution
        );
        numCells = gridDimensions.x * gridDimensions.y;
        cellSize = new(bounds.x / gridDimensions.x, bounds.y / gridDimensions.y);
        int numVerticesX = gridDimensions.x + 1;
        int numVerticesY = gridDimensions.y + 1;
        numVertices = numVerticesX * numVerticesY;

        meshes = MeshGenerator.MarchingSquareVariations();

        // Single command buffer holds all 16 cases' indirect args.
        // Stride must equal IndirectDrawIndexedArgs.size (20 bytes).
        commandBuf?.Release();
        commandBuf = new GraphicsBuffer(
            GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Structured,
            16,
            GraphicsBuffer.IndirectDrawIndexedArgs.size
        );

        var initArgs = new GraphicsBuffer.IndirectDrawIndexedArgs[16];
        for (int c = 0; c < 16; c++)
            initArgs[c].indexCountPerInstance = meshes[c].GetIndexCount(0);
        commandBuf.SetData(initArgs);

        // One RenderParams per case, each reads CasePositions at a different offset.
        rp = new RenderParams[16];
        for (int c = 0; c < 16; c++)
        {
            rp[c] = ComputeHelper.CreateRenderParams(material);
            rp[c].matProps.SetColor("_Color", color);
            rp[c].matProps.SetVector("cellSize", new Vector4(cellSize.x, cellSize.y));
            rp[c].matProps.SetInt("caseOffset", c * numCells);
            rp[c].matProps.SetFloat("densityThreshold", densityThreshold);
        }

        vertexPositionsBuffer?.Release();
        vertexPositionsBuffer = ComputeHelper.CreateStructuredBufferWithData(
            GenerateVertexPositions(bounds, numVerticesX, numVerticesY)
        );

        vertexDensitiesBuffer?.Release();
        vertexDensitiesBuffer = ComputeHelper.CreateStructuredBuffer<float>(numVertices);

        caseCountsBuffer?.Release();
        caseCountsBuffer = ComputeHelper.CreateStructuredBuffer<uint>(16);

        casePositionsBuffer?.Release();
        casePositionsBuffer = ComputeHelper.CreateStructuredBuffer<float2>(16 * numCells);

        caseDensitiesBuffer?.Release();
        caseDensitiesBuffer = ComputeHelper.CreateStructuredBuffer<float4>(16 * numCells);

        clearCaseCountsKernel     = compute.FindKernel("ClearCaseCounts");
        calcVertexDensitiesKernel = compute.FindKernel("CalculateVertexDensities");
        calcCasesKernel           = compute.FindKernel("CalculateCases");
        updateIndirectArgsKernel  = compute.FindKernel("UpdateIndirectArgs");

        vertexThreadGroups = compute.GetThreadGroups(calcVertexDensitiesKernel, numVertices);
        cellThreadGroups   = compute.GetThreadGroups(calcCasesKernel, numCells);

        compute.SetBuffer(clearCaseCountsKernel,     "CaseCounts",  caseCountsBuffer);

        compute.SetBuffer(calcVertexDensitiesKernel, "Positions",       sim.Buffers["Positions"]);
        compute.SetBuffer(calcVertexDensitiesKernel, "VertexPositions", vertexPositionsBuffer);
        compute.SetBuffer(calcVertexDensitiesKernel, "VertexDensities", vertexDensitiesBuffer);

        compute.SetBuffer(calcCasesKernel, "VertexPositions",  vertexPositionsBuffer);
        compute.SetBuffer(calcCasesKernel, "VertexDensities", vertexDensitiesBuffer);
        compute.SetBuffer(calcCasesKernel, "CaseCounts",      caseCountsBuffer);
        compute.SetBuffer(calcCasesKernel, "CasePositions",   casePositionsBuffer);
        compute.SetBuffer(calcCasesKernel, "CaseDensities",   caseDensitiesBuffer);

        compute.SetBuffer(updateIndirectArgsKernel, "CaseCounts",   caseCountsBuffer);
        compute.SetBuffer(updateIndirectArgsKernel, "IndirectArgs", commandBuf);

        compute.SetInt("numVertices",      numVertices);
        compute.SetInt("numCells",         numCells);
        compute.SetInt("numParticles",     sim.numParticles);
        compute.SetFloat("interactionRadius",   sim.InteractionRadius);
        compute.SetFloat("interactionRadiusSq", sim.InteractionRadius * sim.InteractionRadius);
        compute.SetFloat("densityThreshold",    densityThreshold);
        compute.SetInt("gridWidth",        gridDimensions.x);
        compute.SetVector("cellSize",      new Vector4(cellSize.x, cellSize.y));

        for (int c = 0; c < 16; c++)
        {
            rp[c].matProps.SetBuffer("CasePositions",  casePositionsBuffer);
            rp[c].matProps.SetBuffer("CaseDensities",  caseDensitiesBuffer);
        }
    }

    public void Draw()
    {
        compute.Dispatch(clearCaseCountsKernel,     1, 1, 1);
        compute.Dispatch(calcVertexDensitiesKernel, vertexThreadGroups);
        compute.Dispatch(calcCasesKernel,           cellThreadGroups);
        compute.Dispatch(updateIndirectArgsKernel,  1, 1, 1);

        for (int c = 1; c < 16; c++)
            Graphics.RenderMeshIndirect(rp[c], meshes[c], commandBuf, commandCount: 1, startCommand: c);
    }

    private float2[] GenerateVertexPositions(float2 bounds, int numX, int numY)
    {
        float2 topLeft = new(-bounds.x / 2, bounds.y / 2);
        var positions = new float2[numX * numY];
        for (int i = 0; i < numY; i++)
            for (int j = 0; j < numX; j++)
                positions[i * numX + j] = new(
                    topLeft.x + j * cellSize.x,
                    topLeft.y - i * cellSize.y
                );
        return positions;
    }

    private void OnDestroy()
    {
        commandBuf?.Release();
        vertexPositionsBuffer?.Release();
        vertexDensitiesBuffer?.Release();
        caseCountsBuffer?.Release();
        casePositionsBuffer?.Release();
        caseDensitiesBuffer?.Release();
        if (meshes != null)
            foreach (var m in meshes)
                if (m != null) Destroy(m);
    }
}
