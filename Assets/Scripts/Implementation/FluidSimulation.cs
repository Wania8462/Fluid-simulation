using System;
using NUnit.Framework;
using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Timeline;

[RequireComponent(typeof(CreateCubeMesh))]
public class FluidSimulation : MonoBehaviour
{
    [Header("Spawn settings")]
    public int axisLength;
    public float3 particleSize;
    [SerializeField] private int3 centre;
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
    private float3 realHalfBoundSize;

    void Start()
    {
        render = gameObject.GetComponent<CreateCubeMesh>();
        points = new float3[(int)Mathf.Pow(axisLength, 3)];
        velocities = new float3[(int)Mathf.Pow(axisLength, 3)];
        halfParticleSize = particleSize / 2;
        realHalfBoundSize = boundSize / 2 - halfParticleSize;
        CreateList();
    }

    [BurstCompile]
    private void CreateList()
    {
        for (int i = centre.x; i < axisLength; i++)
            for (int j = centre.y; j < axisLength; j++)
                for (int k = centre.z; k < axisLength; k++)
                {
                    int index = i * axisLength * axisLength + j * axisLength + k;

                    points[index].x = (i - axisLength / 2) * spacing + UnityEngine.Random.insideUnitSphere.x * jitterStrength;
                    points[index].y = (j - axisLength / 2) * spacing + UnityEngine.Random.insideUnitSphere.y * jitterStrength;
                    points[index].z = (k - axisLength / 2) * spacing + UnityEngine.Random.insideUnitSphere.z * jitterStrength;
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
            if(Mathf.Abs(points[i].x) >= realHalfBoundSize.x)
            {
                points[i].x = realHalfBoundSize.x * Mathf.Sign(points[i].x);
                velocities[i].x *= -1 * collisionDamp;
            }
            
            if(Mathf.Abs(points[i].y) >= realHalfBoundSize.y)
            {
                points[i].y = realHalfBoundSize.y * Mathf.Sign(points[i].y);
                velocities[i].y *= -1 * collisionDamp;
            }
            
            if(Mathf.Abs(points[i].z) >= realHalfBoundSize.z)
            {
                points[i].z = realHalfBoundSize.z * Mathf.Sign(points[i].z);
                velocities[i].z *= -1 * collisionDamp;
            }
            
        }
    }
}
