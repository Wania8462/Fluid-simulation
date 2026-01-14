using NUnit.Framework.Constraints;
using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;

[BurstCompile]
public class ParticleDisplay : MonoBehaviour
{
    [Header("Render settings")]
    public float3 particleSize;
    // todo: add the ability to change color

    [Header("References")]
    [SerializeField] private Material mat;
    [SerializeField] private Simulation sim;
    [SerializeField] private SpawnParticles spawn;

    private ComputeBuffer argsBuffer;
    private Bounds bounds;
    private Mesh mesh;

    public void Setup()
    {
        mat.SetBuffer("Points", sim.pointsBuffer);
        mesh = MeshGenerator.Cube();
        bounds = new(Vector3.zero, Vector3.one * 10000);

        uint[] args = new uint[]
        {
            mesh.GetIndexCount(0),
            (uint)spawn.pointsAmount,
            mesh.GetIndexStart(0),
            mesh.GetBaseVertex(0),
            0 // todo: make this a variable/constatnt
        };

        argsBuffer = new (1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
        argsBuffer.SetData(args);
    }

    void LateUpdate()
    {
        if(mat != null && mesh != null)
        {
            Graphics.DrawMeshInstancedIndirect(
                mesh,
                0,
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