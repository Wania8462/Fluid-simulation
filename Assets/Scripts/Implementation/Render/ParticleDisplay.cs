using NUnit.Framework.Constraints;
using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;

[BurstCompile]
public class ParticleDisplay : MonoBehaviour
{
    [Header("Render settings")]
    public float3 particleSize;
    public uint offset;
    [SerializeField] private Color color;

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

        mesh = MeshGenerator.Cube();
        bounds = new(Vector3.zero, Vector3.one * drawingBoundary);

        uint[] args = new uint[]
        {
            mesh.GetIndexCount(subMeshIndex),
            (uint)spawn.pointsAmount,
            mesh.GetIndexStart(subMeshIndex),
            mesh.GetBaseVertex(subMeshIndex),
            offset
        };

        argsBuffer = new (
            numberOfElements, 
            args.Length * sizeof(uint), 
            ComputeBufferType.IndirectArguments
        );

        argsBuffer.SetData(args);
    }

    void LateUpdate()
    {
        if(mat != null && mesh != null)
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