using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;

[BurstCompile]
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

    // Precompiled values
    private float smoothRad2;
    private float poly6KernDenom;

    void Start()
    {
        render = gameObject.GetComponent<CreateCubeMesh>();
        spawn = gameObject.GetComponent<SpawnParticles>();

        realHalfBoundSize = spawn.boundSize / 2 - particleSize / 2;
        smoothRad2 = smoothingRadius * smoothingRadius;
        poly6KernDenom = 64 * Mathf.PI * Mathf.Pow(smoothingRadius, 9);
    }

    void Update()
    {
        SimulationFrame();
        ResolveCollisions();
        render.DrawPoints(points, particleSize);
        Density(float3.zero);
    }

    private void SimulationFrame()
    {
        for (int i = 0; i < velocities.Length; i++)
        {
            velocities[i].y += gravity * Time.deltaTime;
            points[i].y += velocities[i].y * Time.deltaTime;
        }
    }

    
    private float Density(float3 pos)
    {
        float density = 0;

        for (int i = 0; i < points.Length; i++)
            density += mass * SmoothingKernelPoly6(Distance2(pos, points[i]));
        
        return density;
    }

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

    private float SmoothingKernelPoly6(float dist2)
    {
        if (dist2 > smoothingRadius)
            return 0;

        return 315 * Mathf.Pow(smoothRad2 - dist2, 3) / poly6KernDenom;
    }

    private float Distance(float3 pos1, float3 pos2)
    {
        float distX = Mathf.Pow(Mathf.Abs(pos1.x - pos2.x), 2);
        float distY = Mathf.Pow(Mathf.Abs(pos1.y - pos2.y), 2);
        float distZ = Mathf.Pow(Mathf.Abs(pos1.z - pos2.z), 2);
        return Mathf.Sqrt(distX + distY + distZ);
    }

    private float Distance2(float3 pos1, float3 pos2)
    {
        float distX = Mathf.Pow(Mathf.Abs(pos1.x - pos2.x), 2);
        float distY = Mathf.Pow(Mathf.Abs(pos1.y - pos2.y), 2);
        float distZ = Mathf.Pow(Mathf.Abs(pos1.z - pos2.z), 2);
        return distX + distY + distZ;
    }
}