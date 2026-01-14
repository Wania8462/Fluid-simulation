using Unity.Mathematics;
using UnityEngine;

enum Verticies
{
    TopLeftFront = 0,
    TopRightFront = 1,
    BottomRightFront = 2,
    BottomLeftFront = 3,
    TopLeftBack = 4,
    TopRightBack = 5,
    BottomRightBack = 6,
    BottomLeftBack = 7
}

public interface ICreateCubeMesh
{
    public Mesh GetMeshCube();
    public void DrawPoints(float3[] points, float3 scale);
}