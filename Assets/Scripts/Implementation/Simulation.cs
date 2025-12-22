using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;

[RequireComponent(typeof(SpawnParticles), typeof(CreateCubeMesh))]
public class Simulation : MonoBehaviour
{
    [Header("Simulation settings")]
    public float3 particleSize;
    [SerializeField] private float gravity;
    [SerializeField] private float collisionDamp;
    [SerializeField] private float mass;
    [SerializeField] private float smoothingRadius;

    private CreateCubeMesh render;
    private SpawnParticles spawn;

    [HideInInspector] public float3[] points;
    [HideInInspector] public float3[] velocities;

    private float3 realHalfBoundSize;

    void Start()
    {
        render = gameObject.GetComponent<CreateCubeMesh>();
        spawn = gameObject.GetComponent<SpawnParticles>();

        realHalfBoundSize = spawn.boundSize / 2 - particleSize / 2;
    }

    void Update()
    {
        SimulationFrame();
        ResolveCollisions();
        render.DrawPoints(points, particleSize);
    }

    [BurstCompile]
    private void SimulationFrame()
    {
        for (int i = 0; i < velocities.Length; i++)
        {
            velocities[i].y += gravity * Time.deltaTime;
            points[i].y += velocities[i].y * Time.deltaTime;
        }
    }

    [BurstCompile]
    private float Density(float3 pos)
    {
        float density = 0;

        for (int i = 0; i < points.Length; i++)
            density += mass * SmoothingKernelPoly6(Distance(pos, points[i]));
        
        return density;
    }

    [BurstCompile]
    private void ResolveCollisions()
    {
        for (int i = 0; i < points.Length; i++)
        {
            if (Mathf.Abs(points[i].x) >= realHalfBoundSize.x)
            {
                points[i].x = realHalfBoundSize.x * Mathf.Sign(points[i].x);
                velocities[i].x *= -1 * collisionDamp;
            }

            else if (Mathf.Abs(points[i].y) >= realHalfBoundSize.y)
            {
                points[i].y = realHalfBoundSize.y * Mathf.Sign(points[i].y);
                velocities[i].y *= -1 * collisionDamp;
            }

            else if (Mathf.Abs(points[i].z) >= realHalfBoundSize.z)
            {
                points[i].z = realHalfBoundSize.z * Mathf.Sign(points[i].z);
                velocities[i].z *= -1 * collisionDamp;
            }

        }
    }

    [BurstCompile]
    private float SmoothingKernelPoly6(float dist)
    {
        if (dist > smoothingRadius)
            return 0;

        float numerator = 315 * Mathf.Pow(smoothingRadius * smoothingRadius - dist * dist, 3);
        return numerator / 64 * Mathf.PI * Mathf.Pow(smoothingRadius, 9);
    }

    [BurstCompile]
    private float Distance(float3 pos1, float3 pos2)
    {
        float distX = Mathf.Pow(Mathf.Abs(pos1.x - pos2.x), 2);
        float distY = Mathf.Pow(Mathf.Abs(pos1.y - pos2.y), 2);
        float distZ = Mathf.Pow(Mathf.Abs(pos1.z - pos2.z), 2);
        return Mathf.Sqrt(distX + distY + distZ);
    }
}