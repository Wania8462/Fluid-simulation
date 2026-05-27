using Unity.Mathematics;
using UnityEngine;

public class RenderDensityMap : MonoBehaviour
{
    [SerializeField] private int resolution;
    [SerializeField] private float densityLimit;
    [SerializeField] private ComputeShader compute;
    [SerializeField] private Material material;

    private int numCells;
    private int2 gridDimensions;
    private float2 cellSize;

    private ComputeBuffer cellPositionsBuffer;
    private ComputeBuffer densityMapBuffer;
    private ComputeBuffer colorsBuffer;
    private GraphicsBuffer commandBuf;
    private GraphicsBuffer.IndirectDrawIndexedArgs[] commandData;
    private Mesh mesh;
    private RenderParams rp;

    private int3 threadGroups;
    private int calcDensitiesKernel;
    private int calcColorsKernel;

    public void Setup(GPUSimulationManager sim, float2 bounds)
    {
        gridDimensions = new(
            (int)(resolution * (bounds.x / bounds.y)),
            resolution
        );
        numCells = gridDimensions.x * gridDimensions.y;
        cellSize = new(bounds.x / gridDimensions.x, bounds.y / gridDimensions.y);

        mesh = mesh == null ? MeshGenerator.Rectangle(cellSize.x, cellSize.y) : mesh;

        ComputeHelper.Release(commandBuf);
        commandBuf = ComputeHelper.CreateCommandBuffer();
        commandData = ComputeHelper.CreateCommandData(mesh, numCells);
        commandBuf.SetData(commandData);
        rp = ComputeHelper.CreateRenderParams(material);

        ComputeHelper.Release(cellPositionsBuffer);
        cellPositionsBuffer = ComputeHelper.CreateStructuredBufferWithData(GenerateCellPositions(bounds));

        ComputeHelper.Release(densityMapBuffer);
        densityMapBuffer = ComputeHelper.CreateStructuredBuffer<float>(numCells);

        ComputeHelper.Release(colorsBuffer);
        colorsBuffer = ComputeHelper.CreateStructuredBuffer<float4>(numCells);

        calcDensitiesKernel = compute.FindKernel("CalculateDensities");
        calcColorsKernel = compute.FindKernel("CalculateColors");
        threadGroups = compute.GetThreadGroups(calcDensitiesKernel, numCells);

        compute.SetBuffer(calcDensitiesKernel, "Positions", sim.Buffers["Positions"]);
        compute.SetBuffer(calcDensitiesKernel, "CellPositions", cellPositionsBuffer);
        compute.SetBuffer(calcDensitiesKernel, "DensityMap", densityMapBuffer);
        compute.SetBuffer(calcColorsKernel, "DensityMap", densityMapBuffer);
        compute.SetBuffer(calcColorsKernel, "Colors", colorsBuffer);

        compute.SetInt("numCells", numCells);
        compute.SetInt("numParticles", sim.numParticles);
        compute.SetFloat("interactionRadius", sim.InteractionRadius);
        compute.SetFloat("interactionRadiusSq", sim.InteractionRadius * sim.InteractionRadius);
        compute.SetFloat("densityLimit", densityLimit);

        rp.matProps.SetBuffer("CellPositions", cellPositionsBuffer);
        rp.matProps.SetBuffer("Colors", colorsBuffer);
    }

    public void Draw()
    {
        compute.Dispatch(calcDensitiesKernel, threadGroups);
        compute.Dispatch(calcColorsKernel, threadGroups);
        Graphics.RenderMeshIndirect(rp, mesh, commandBuf, commandCount: 1);
    }

    private float2[] GenerateCellPositions(float2 bounds)
    {
        float2 topLeft = new(-bounds.x / 2, bounds.y / 2);
        float halfW = cellSize.x / 2;
        float halfH = cellSize.y / 2;

        var positions = new float2[numCells];
        for (int i = 0; i < gridDimensions.y; i++)
            for (int j = 0; j < gridDimensions.x; j++)
                positions[gridDimensions.x * i + j] = new(
                    topLeft.x + halfW + j * cellSize.x,
                    topLeft.y - halfH - i * cellSize.y
                );
        return positions;
    }

    private void ReleaseBuffers()
    {
        ComputeHelper.Release(commandBuf);
        ComputeHelper.Release(cellPositionsBuffer);
        ComputeHelper.Release(densityMapBuffer);
        ComputeHelper.Release(colorsBuffer);
    }

    private void OnDestroy()
    {
        ReleaseBuffers();
        Destroy(mesh);
    }

#if UNITY_EDITOR
    private void OnEnable()  => UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += ReleaseBuffers;
    private void OnDisable() => UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= ReleaseBuffers;
#endif
}
