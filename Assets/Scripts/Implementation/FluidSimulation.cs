using System;
using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;

// float3 jitter = UnityEngine.Random.insideUnitSphere

[RequireComponent(typeof(CreateCubeMesh))]
public class FluidSimulation : MonoBehaviour
{
    [Header("Spawn settings")]
    public int axisLength;
    [SerializeField] private int3 startingPosition;
    [SerializeField] private float spacing;
    [SerializeField] private float jitterStrength;

    [Header("Simulation settings")]
    [SerializeField] private float gravityStrength; // use it

    private CreateCubeMesh render;
    private float4[] points;

    void Start()
    {
        render = gameObject.GetComponent<CreateCubeMesh>();
        points = new float4[(int)Mathf.Pow(axisLength, 3)];
        CreateList();
    }

    [BurstCompile]
    private void CreateList()
    {
        for (int i = startingPosition.x; i < axisLength; i++)
            for (int j = startingPosition.y; j < axisLength; j++)
                for (int k = startingPosition.z; k < axisLength; k++)
                {
                    int index = i * axisLength * axisLength + j * axisLength + k;

                    points[index].x = i * spacing + UnityEngine.Random.insideUnitSphere.x * jitterStrength;
                    points[index].y = j * spacing + UnityEngine.Random.insideUnitSphere.y * jitterStrength;
                    points[index].z = k * spacing + UnityEngine.Random.insideUnitSphere.z * jitterStrength;
                    points[index].w = 1;
                }
    }

    void Update()
    {
        render.DrawPoints(points);
    }
}
