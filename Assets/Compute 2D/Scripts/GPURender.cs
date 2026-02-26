using System;
using UnityEngine;

public class GPURender : MonoBehaviour
{
    [SerializeField] private GPUSimulationManager sim;
    [SerializeField] private Material material;
    private Mesh mesh;

    GraphicsBuffer commandBuf;
    GraphicsBuffer.IndirectDrawIndexedArgs[] commandData;
    const int commandCount = 1;

    public void Setup()
    {
        commandBuf = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, commandCount, GraphicsBuffer.IndirectDrawIndexedArgs.size);
        commandData = new GraphicsBuffer.IndirectDrawIndexedArgs[commandCount];
        mesh = Circle(0.5f, 10);
    }

    public void DrawParticles()
    {
        RenderParams rp = new(material)
        {
            worldBounds = new Bounds(Vector3.zero, 10000 * Vector3.one),
            matProps = new MaterialPropertyBlock()
        };

        rp.matProps.SetBuffer("Positions", sim.buffers["Positions"]);
        commandData[0].indexCountPerInstance = mesh.GetIndexCount(0);
        commandData[0].instanceCount = (uint)sim.numParticles;
        commandBuf.SetData(commandData);
        Graphics.RenderMeshIndirect(rp, mesh, commandBuf, commandCount);
    }

    void OnDestroy()
    {
        commandBuf?.Release();
        commandBuf = null;
    }

    static Mesh Circle(float radius, int resolution)
    {
        Vector3[] verticies = new Vector3[4 * resolution + 1];
        int[] triangles = new int[resolution * 12];

        verticies[0] = new(0, 0);

        for (int i = 1; i < verticies.Length; i++)
        {
            verticies[i] = new((float)(radius * Math.Cos(Math.PI * (i - 1) / resolution / 2)),
                               (float)(radius * Math.Sin(Math.PI * (i - 1) / resolution / 2)),
                               0);
        }

        int v = 1;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            triangles[i] = 0;
            triangles[i + 1] = v + 1;
            triangles[i + 2] = v;
            v += 1;
        }

        triangles[^2] = 1;

        Mesh mesh = new()
        {
            vertices = verticies,
            triangles = triangles
        };

        return mesh;
    }
}