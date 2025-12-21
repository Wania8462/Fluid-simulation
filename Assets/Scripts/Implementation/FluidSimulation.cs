using System;
using NUnit.Framework;
using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;

[RequireComponent(typeof(CreateCubeMesh))]
public class FluidSimulation : MonoBehaviour
{
    [Header("Spawn settings")]
    public int axisLength;
    public float3 particleSize;
    [SerializeField] private int3 startingPosition;
    [SerializeField] private float spacing;
    [SerializeField] private float3 boundSize;
    [SerializeField] private float collisionDamp;
    [SerializeField] private float jitterStrength;

    [Header("Simulation settings")]
    [SerializeField] private float gravity;

    private CreateCubeMesh render;
    private float3[] points;
    private float3[] velocities;
    private float3 halfParticleSize;

    void Start()
    {
        render = gameObject.GetComponent<CreateCubeMesh>();
        points = new float3[(int)Mathf.Pow(axisLength, 3)];
        velocities = new float3[(int)Mathf.Pow(axisLength, 3)];
        halfParticleSize = particleSize / 2;
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
                }
    }

    void Update()
    {
        SimulationStep();
        ResolveCollisions();
        render.DrawPoints(points, particleSize);
    }

    [BurstCompile]
    private void SimulationStep()
    {
        for (int i = 0; i < velocities.Length; i++)
        {
            velocities[i].y += gravity * Time.deltaTime;
            points[i].y += velocities[i].y * Time.deltaTime;
        }
    }

    [BurstCompile]
    private void ResolveCollisions()
    {
        for (int i = 0; i < points.Length; i++)
        {
            if (points[i].x <= halfParticleSize.x)
            {
                points[i].x = halfParticleSize.x;
                velocities[i].x *= -1 * collisionDamp;
            }

            else if (points[i].x >= boundSize.x - halfParticleSize.x)
            {
                points[i].x = boundSize.x - halfParticleSize.x;
                velocities[i].x *= -1 * collisionDamp;
            }

            if (points[i].y <= halfParticleSize.y)
            {
                points[i].y = halfParticleSize.y;
                velocities[i].y *= -1 * collisionDamp;
            }

            else if (points[i].y >= boundSize.y - halfParticleSize.y)
            {
                points[i].y = boundSize.y - halfParticleSize.y;
                velocities[i].y *= -1 * collisionDamp;
            }

            if (points[i].z <= halfParticleSize.z)
            {
                points[i].z = halfParticleSize.z;
                velocities[i].z *= -1 * collisionDamp;
            }

            else if (points[i].z >= boundSize.z - halfParticleSize.z)
            {
                points[i].z = boundSize.z - halfParticleSize.z;
                velocities[i].z *= -1 * collisionDamp;
            }
        }
    }
}
