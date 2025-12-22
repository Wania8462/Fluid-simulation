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

    [Header("References")]
    [SerializeField] private ComputeShader compute;
    private CreateCubeMesh render;
    private SpawnParticles spawn;

    // Buffers
    private ComputeBuffer pointsBuffer;
    private ComputeBuffer velocitiesBuffer;

    [HideInInspector] public float3[] points;
    [HideInInspector] public float3[] velocities;

    //Kernel IDs
    private const int externalForcesID = 0;

    private const int float3Size = 24;

    // Precompiled values
    private float3 realHalfBoundSize;
    private float smoothRad2;
    private float poly6KernDenom;
    private int threadGroups;

    void Start()
    {
        // Get references
        render = gameObject.GetComponent<CreateCubeMesh>();
        spawn = gameObject.GetComponent<SpawnParticles>();

        // Set buffers
        pointsBuffer = new (points.Length, float3Size);
        velocitiesBuffer = new(velocities.Length, float3Size);

        // Set data in the buffers
        pointsBuffer.SetData(points);
        velocitiesBuffer.SetData(velocities);

        // Set buffers into shader
        // TODO: make method for prettier setting
        compute.SetBuffer(externalForcesID, "Points", pointsBuffer);
        compute.SetBuffer(externalForcesID, "Velocities", velocitiesBuffer);

        compute.SetInt("numParticles", points.Length);
        compute.SetFloat("gravity", gravity);

        Precompute();
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
        compute.Dispatch(externalForcesID, threadGroups, 1, 1);
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

    private void Precompute()
    {
        realHalfBoundSize = spawn.boundSize / 2 - particleSize / 2;
        smoothRad2 = smoothingRadius * smoothingRadius;
        poly6KernDenom = 64 * Mathf.PI * Mathf.Pow(smoothingRadius, 9);
        threadGroups = Mathf.CeilToInt(points.Length / 256f);
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