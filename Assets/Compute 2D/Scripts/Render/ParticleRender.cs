using System;
using System.Collections.Generic;
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

    private int3 theradGroups;

    const int CalculateColorsKernelID = 0;

    public void Setup(GPUSimulationManager sim)
    {
        mesh = mesh == null ? MeshGenerator.Circle(sim.particleRadius, particleQuality) : mesh;
        commandBuf ??= ComputeHelper.CreateCommandBuffer();
        commandData ??= ComputeHelper.CreateCommandData(mesh, sim.numParticles);
        commandBuf.SetData(commandData);
        rp = ComputeHelper.CreateRenderParams(material);

        theradGroups = compute.GetThreadGroups(0, sim.numParticles);
        compute.SetBuffer(CalculateColorsKernelID, "Velocities", sim.buffers["Velocities"]);

        colorsBuffer?.Release();
        colorsBuffer = ComputeHelper.CreateStructuredBufferWithData(GetDefaultColors(sim.numParticles));
        compute.SetBuffer(CalculateColorsKernelID, "Colors", colorsBuffer);

        rp.matProps.SetBuffer("Positions", sim.buffers["Positions"]);
        rp.matProps.SetBuffer("Colors", colorsBuffer);
    }

    public void DrawParticles(List<int> indices)
    {
        compute.Dispatch(CalculateColorsKernelID, theradGroups);
        ColorParticles(indices);
        Graphics.RenderMeshIndirect(rp, mesh, commandBuf, commandCount: 1);
    }

    private void ColorParticles(List<int> indices)
    {
        // ik that its inefficient
        var colors = ComputeHelper.GetBuffer<float4>(colorsBuffer);

        for (int i = 0; i < indices.Count; i++)
            colors[indices[i]] = new float4(0, 1, 0, 1);

        colorsBuffer.SetData(colors);
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
