using System.Collections.Generic;
using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;

[BurstCompile]
[RequireComponent(typeof(SpawnParticles))]
public class CreateCubeMesh : MonoBehaviour, ICreateCubeMesh
{
    [Header("References")]
    [SerializeField] private Material mat;
    private SpawnParticles fluidSim;

    private const int batchSize = 1024;
    private List<Matrix4x4> matrices = new();

    private Mesh mesh;

    private void Start()
    {
        fluidSim = gameObject.GetComponent<SpawnParticles>();
        float3 scale = new(1, 1, 1);
        mesh = DrawMesh();

        for (int i = 0; i < fluidSim.axisLength; i++)
            for (int j = 0; j < fluidSim.axisLength; j++)
                for (int k = 0; k < fluidSim.axisLength; k++)
                {
                    matrices.Add(Matrix4x4.TRS(
                        Vector3.one,
                        Quaternion.identity,
                        scale
                    ));
                }

    }

    public void DrawPoints(float3[] points, float3 scale)
    {
        for (int i = 0; i < points.Length; i++)
        {
            matrices[i] = Matrix4x4.TRS(
                new Vector3(points[i].x, points[i].y, points[i].z),
                Quaternion.identity,
                scale
            );
        }

        for (int i = 0; i < matrices.Count; i += batchSize)
        {
            Graphics.DrawMeshInstanced(
                mesh,
                0,
                mat,
                matrices.GetRange(i, Mathf.Min(batchSize, matrices.Count - i))
            );
        }
    }

    public Mesh DrawMesh()
    {
        Vector3[] verticies = new Vector3[8];
        Vector2[] uv = new Vector2[8];

        verticies[(int)Verticies.BottomLeftFront] = new Vector3(-0.5f, -0.5f, -0.5f);
        verticies[(int)Verticies.BottomRightFront] = new Vector3(0.5f, -0.5f, -0.5f);
        verticies[(int)Verticies.TopRightFront] = new Vector3(0.5f, 0.5f, -0.5f);
        verticies[(int)Verticies.TopLeftFront] = new Vector3(-0.5f, 0.5f, -0.5f);
        verticies[(int)Verticies.BottomLeftBack] = new Vector3(-0.5f, -0.5f, 0.5f);
        verticies[(int)Verticies.BottomRightBack] = new Vector3(0.5f, -0.5f, 0.5f);
        verticies[(int)Verticies.TopRightBack] = new Vector3(0.5f, 0.5f, 0.5f);
        verticies[(int)Verticies.TopLeftBack] = new Vector3(-0.5f, 0.5f, 0.5f);

        uv[(int)Verticies.BottomLeftFront] = new Vector3(-0.5f, -0.5f, -0.5f);
        uv[(int)Verticies.BottomRightFront] = new Vector3(0.5f, -0.5f, -0.5f);
        uv[(int)Verticies.TopRightFront] = new Vector3(0.5f, 0.5f, -0.5f);
        uv[(int)Verticies.TopLeftFront] = new Vector3(-0.5f, 0.5f, -0.5f);
        uv[(int)Verticies.BottomLeftBack] = new Vector3(-0.5f, -0.5f, 0.5f);
        uv[(int)Verticies.BottomRightBack] = new Vector3(0.5f, -0.5f, 0.5f);
        uv[(int)Verticies.TopRightBack] = new Vector3(0.5f, 0.5f, 0.5f);
        uv[(int)Verticies.TopLeftBack] = new Vector3(-0.5f, 0.5f, 0.5f);

        int[] triangles = new int[]
        {
            // Back
            0, 1, 2,  0, 2, 3,
            // Front
            4, 6, 5,  4, 7, 6,
            // Left
            0, 3, 7,  0, 7, 4,
            // Right
            1, 5, 6,  1, 6, 2,
            // Top
            3, 2, 6,  3, 6, 7,
            // Bottom
            0, 4, 5,  0, 5, 1
        };

        Mesh mesh = new()
        {
            vertices = verticies,
            uv = uv,
            triangles = triangles
        };

        return mesh;
    }
}