using Unity.Burst;
using UnityEngine;

enum RenderType
{
    Cube,
    Sphere
}

enum SphereQuality
{
    Low = 4,
    Medium = 8,
    High = 16
}

[BurstCompile]
public class ParticleDisplay : MonoBehaviour
{
    [Header("Render settings")]
    [SerializeField] private RenderType renderType;
    public float particleSize;
    public uint offset;
    [SerializeField] private Color color;

    [Header("Sphere settings")]
    [SerializeField] private SphereQuality quality;

    [Header("References")]
    [SerializeField] private Material mat;
    [SerializeField] private Simulation sim;
    [SerializeField] private SpawnParticles spawn;

    private ComputeBuffer argsBuffer;
    private Bounds bounds;
    private Mesh mesh;
    private const int subMeshIndex = 0;
    private const int drawingBoundary = 10000;
    private const int numberOfElements = 1;

    public void Setup()
    {
        mat.SetBuffer("Points", sim.pointsBuffer);
        mat.SetColor("_Color", color);

        if (renderType == RenderType.Cube)
            mesh = MeshGenerator.Cube(particleSize);

        else if (renderType == RenderType.Sphere)
            mesh = MeshGenerator.Sphere(particleSize, (int)quality, (int)quality);

        bounds = new(Vector3.zero, Vector3.one * drawingBoundary);

        uint[] args = new uint[]
        {
            mesh.GetIndexCount(subMeshIndex),
            (uint)spawn.pointsAmount,
            mesh.GetIndexStart(subMeshIndex),
            mesh.GetBaseVertex(subMeshIndex),
            offset
        };

        argsBuffer = new(
            numberOfElements,
            args.Length * sizeof(uint),
            ComputeBufferType.IndirectArguments
        );

        argsBuffer.SetData(args);
    }

    void LateUpdate()
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
        argsBuffer.Release();
    }
}