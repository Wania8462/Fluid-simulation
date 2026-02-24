using System;
using UnityEngine;

public class GPURender : MonoBehaviour
{
    [Header("Render settings")]
    [SerializeField] private int resolution;
    public uint offset;
    [SerializeField] private Color color;

    [Header("References")]
    [SerializeField] private Material mat;
    [SerializeField] private GPUSimulationManager sim;
    [SerializeField] private Spawn2DParticles spawn;

    private ComputeBuffer argsBuffer;
    private Bounds bounds;
    private Mesh mesh;
    private const int subMeshIndex = 0;
    private const int drawingBoundary = 10000;
    private const int numberOfElements = 1;

    public void Setup()
    {
        mat.SetBuffer("Points", sim.buffers["Positions"]);
        mat.SetColor("_Color", color);

        mesh = Sphere(sim.particleRadius, resolution);

        bounds = new(Vector3.zero, Vector3.one * drawingBoundary);
        uint[] args = new uint[]
        {
            mesh.GetIndexCount(subMeshIndex),
            (uint)spawn.GetNumberOfParticles(),
            mesh.GetIndexStart(subMeshIndex),
            mesh.GetBaseVertex(subMeshIndex),
            offset
        };

        argsBuffer = new(
            args.Length,
            sizeof(uint),
            ComputeBufferType.IndirectArguments
        );

        argsBuffer.SetData(args);
    }

    public void DrawParticles()
    {
        if (mat != null && mesh != null)
        {
            Graphics.DrawMeshInstancedIndirect(
                mesh,
                subMeshIndex,
                mat,
                bounds,
                argsBuffer
            );
        }
    }

    void OnDestroy()
    {
        argsBuffer?.Release();
    }

    public static Mesh Sphere(float radius, int resolution)
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