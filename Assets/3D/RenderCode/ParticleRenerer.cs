using Unity.Mathematics;
using UnityEngine;

enum ParticleColor3D
{
    SolidBlue,
    BlueToRed,
    BlueToWhite
}

public class ParticleRenerer : MonoBehaviour
{
    [SerializeField] private int particleQuality = 5;
    [SerializeField] private ParticleColor3D color = ParticleColor3D.BlueToWhite;
    [SerializeField] private float maxSpeed = 100;
    [SerializeField] private ComputeShader compute;
    [SerializeField] private Material material;

    private Mesh mesh;

    private ComputeBuffer colorsBuffer;
    private GraphicsBuffer commandBuf;
    private GraphicsBuffer.IndirectDrawIndexedArgs[] commandData;
    private RenderParams rp;

    private int3 threadGroups;

    const int CalculateColorsKernelID = 0;

    public void Setup(SimulationManager sim)
    {
        mesh = mesh == null ? MeshGenerator.Circle(sim.particleRadius, particleQuality) : mesh;
        ComputeHelper.Release(commandBuf);
        commandBuf = ComputeHelper.CreateCommandBuffer();
        commandData = ComputeHelper.CreateCommandData(mesh, sim.numParticles);
        commandBuf.SetData(commandData);
        rp = ComputeHelper.CreateRenderParams(material);

        threadGroups = compute.GetThreadGroups(CalculateColorsKernelID, sim.numParticles);
        compute.SetBuffer(CalculateColorsKernelID, "Velocities", sim.Buffers["Velocities"]);
        compute.SetInt("numParticles", sim.numParticles);

        ComputeHelper.Release(colorsBuffer);
        colorsBuffer = ComputeHelper.CreateStructuredBufferWithData(GetDefaultColors(sim.numParticles));
        compute.SetBuffer(CalculateColorsKernelID, "Colors", colorsBuffer);
        compute.SetBool("solidBlue", color == ParticleColor3D.SolidBlue);
        compute.SetBool("blueToRed", color == ParticleColor3D.BlueToRed);
        compute.SetBool("blueToWhite", color == ParticleColor3D.BlueToWhite);
        compute.SetFloat("maxSpeed", maxSpeed);

        rp.matProps.SetBuffer("Positions", sim.Buffers["Positions"]);
        rp.matProps.SetBuffer("Colors", colorsBuffer);
    }

    public void DrawParticles()
    {
        compute.Dispatch(CalculateColorsKernelID, threadGroups);
        Graphics.RenderMeshIndirect(rp, mesh, commandBuf, commandCount: 1);
    }

    private float4[] GetDefaultColors(int length)
    {
        float4[] defaultColors = new float4[length];

        for (int i = 0; i < length; i++)
            defaultColors[i] = new float4(1, 0, 1, 1);

        return defaultColors;
    }

    private void ReleaseBuffers()
    {
        ComputeHelper.Release(commandBuf);
        ComputeHelper.Release(colorsBuffer);
    }

    private void OnDestroy() => ReleaseBuffers();

#if UNITY_EDITOR
    private void OnEnable()  => UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += ReleaseBuffers;
    private void OnDisable() => UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= ReleaseBuffers;
#endif
}
