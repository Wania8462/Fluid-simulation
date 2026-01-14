using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;

[BurstCompile]
public class Simulation : MonoBehaviour
{
    [Header("Simulation settings")]
    [SerializeField] private float gravity;
    [SerializeField] private float collisionDamp;
    [SerializeField] private float mass;
    [SerializeField] private float smoothingRadius;

    [Header("References")]
    [SerializeField] private ComputeShader compute;
    [SerializeField] private SpawnParticles spawn;
    [SerializeField] private ParticleDisplay display;

    // Buffers
    public ComputeBuffer pointsBuffer;
    private ComputeBuffer velocitiesBuffer;
    private ComputeBuffer densitiesBuffer;

    [HideInInspector] public float3[] points { get; private set; }
    [HideInInspector] public float3[] velocities { get; private set; }

    //Kernel IDs
    private const int ExternalForcesKernelID = 0;
    private const int CalcDensitiesKernelID = 1;

    private const int floatSize = 8;
    private const int float3Size = 24;

    // Precompiled values
    private float3 realHalfBoundSize;
    private float smoothRad2;
    private float poly6KernDenom;
    private int threadGroups;

    void Start()
    {
        // Get references
        spawn = gameObject.GetComponent<SpawnParticles>();

        points = spawn.GetSpawnPositions();
        velocities = spawn.GetSpawnVelocities();

        Precompute();

        // Set buffers
        pointsBuffer = new(points.Length, float3Size);
        velocitiesBuffer = new(points.Length, float3Size);
        densitiesBuffer = new(points.Length, floatSize);

        // Set initial data in the buffers
        pointsBuffer.SetData(points);
        velocitiesBuffer.SetData(velocities);

        // Set buffers into shader
        // todo: make method for prettier setting
        compute.SetBuffer(ExternalForcesKernelID, "Points", pointsBuffer);
        compute.SetBuffer(ExternalForcesKernelID, "Velocities", velocitiesBuffer);
        compute.SetBuffer(CalcDensitiesKernelID, "Densities", densitiesBuffer);

        compute.SetInt("numParticles", points.Length);
        compute.SetFloat("mass", mass);
        compute.SetFloat("gravity", gravity);
        compute.SetFloat("smoothingRadius", smoothingRadius);
        compute.SetFloat("smoothingRadius2", smoothRad2);

        display.Setup();
    }

    void Update()
    {
        SimulationFrame();
        ResolveCollisions();
    }
    
    private void SimulationFrame()
    {
        compute.Dispatch(ExternalForcesKernelID, threadGroups, 1, 1);
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
        realHalfBoundSize = spawn.boundSize / 2 - display.particleSize / 2;
        smoothRad2 = smoothingRadius * smoothingRadius;
        poly6KernDenom = 64 * Mathf.PI * Mathf.Pow(smoothingRadius, 9);
        threadGroups = Mathf.CeilToInt(points.Length / 256f);
    }

    void OnDestroy()
    {
        pointsBuffer.Release();
        velocitiesBuffer.Release();
        densitiesBuffer.Release();
    }
}