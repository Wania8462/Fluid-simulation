using Unity.Mathematics;
using UnityEngine;

public class ParticleRender : MonoBehaviour
{
    [SerializeField] private int particleQuality;
    [SerializeField] private ComputeShader compute;
    [SerializeField] private Material material;
    
    private Mesh mesh;

    private ComputeBuffer colorsBuffer;
    private GraphicsBuffer commandBuf;
    private GraphicsBuffer.IndirectDrawIndexedArgs[] commandData;
    private RenderParams rp;

    private ThreadGroups theradGroups;

    const int commandCount = 1;
    const int CalculateColorsKernelID = 0;
    const int float4Size = 16;

    public void Setup(GPUSimulationManager sim)
    {
        mesh = mesh == null ? MeshGenerator.Circle(sim.particleRadius, particleQuality) : mesh;
        commandBuf ??= new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, commandCount, GraphicsBuffer.IndirectDrawIndexedArgs.size);
        commandData ??= new GraphicsBuffer.IndirectDrawIndexedArgs[commandCount];

        commandData[0].indexCountPerInstance = mesh.GetIndexCount(0);
        commandData[0].instanceCount = (uint)sim.numParticles;
        commandBuf.SetData(commandData);

        rp = new(material)
        {
            worldBounds = new Bounds(Vector3.zero, 10000 * Vector3.one),
            matProps = new MaterialPropertyBlock()
        };

        theradGroups = new ThreadGroups(compute, sim.numParticles);
        compute.SetBuffer(CalculateColorsKernelID, "Velocities", sim.buffers["Velocities"]);

        colorsBuffer?.Release();
        colorsBuffer = new ComputeBuffer(sim.numParticles, float4Size);
        colorsBuffer.SetData(GetDefaultColors(sim.numParticles));
        compute.SetBuffer(CalculateColorsKernelID, "Colors", colorsBuffer);

        rp.matProps.SetBuffer("Positions", sim.buffers["Positions"]);
        rp.matProps.SetBuffer("Colors", colorsBuffer);
    }

    public void DrawParticles()
    {
        compute.Dispatch(CalculateColorsKernelID, theradGroups.x, theradGroups.y, theradGroups.z);
        Graphics.RenderMeshIndirect(rp, mesh, commandBuf, commandCount);
    }

    private float4[] GetDefaultColors(int length)
    {
        float4[] defaultColors = new float4[length];

        for (int i = 0; i < length; i++)
            defaultColors[i] = new float4(1, 0, 1, 1);

        return defaultColors;
    }

    private void OnDestroy()
    {
        commandBuf?.Release();
        colorsBuffer.Release();
    }
}
