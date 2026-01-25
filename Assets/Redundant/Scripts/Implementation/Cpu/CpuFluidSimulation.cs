using System;
using Unity.Mathematics;
using UnityEngine;

public class CpuFluidSimulation : MonoBehaviour, IFluidSimulation
{
    public float3 particleSize;
    [SerializeField] private float gravity;
    [SerializeField] private float collisionDamp;
    private float restDensity;
    private float stiffness;
    [SerializeField] private float mass;
    [SerializeField] private float smoothingRadius;

    [SerializeField] private SpawnParticles spawn;

    [HideInInspector] public float3[] points;
    [HideInInspector] public float3[] velocities;
    [HideInInspector] public float[] densities;

    private float3 realHalfBoundSize;

    // Precompiled values
    private float smoothRad2;
    private float poly6KernDenom;
    public int numParticles;


    public CpuFluidSimulation(SpawnParticles spawn)
    {
        this.spawn = spawn;
    }
    public void CalculateStep(float deltaTime)
    {
        ExternalForces();
        ResolveCollisions();

        for(int i = 0; i < points.Length; i++)
            densities[i] = Density(points[i]);
    }

    private void ExternalForces()
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
            density += mass * SmoothingKernelPoly6(Distance(pos, points[i]));

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

    public void InitializeConstants(int numParticles, float3 particleSize, float mass, float gravity, float smoothingRadius, float collisionDamp, float restDensity, float stiffness)
    {
        this.numParticles = numParticles;
        this.particleSize = particleSize;
        this.mass = mass;
        this.gravity = gravity;
        this.smoothingRadius = smoothingRadius;
        this.collisionDamp = collisionDamp;
        this.restDensity = restDensity;
        this.stiffness = stiffness;
    }

    public void InitializeStartingPoints()
    {
        float4[] spawnPoints = spawn.GetSpawnPositions();
        float4[] spawnVelocities = spawn.GetSpawnVelocities();
        
        points = new float3[numParticles];
        velocities = new float3[numParticles];
        densities = new float[numParticles];
        
        for (int i = 0; i < spawnPoints.Length; i++)
        {
            points[i] = spawnPoints[i].xyz;
            velocities[i] = spawnVelocities[i].xyz;
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