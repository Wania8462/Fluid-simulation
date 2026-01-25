using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;

[BurstCompile]
public class GpuFluidSimulation : MonoBehaviour, IFluidSimulation
{
    private ComputeShader compute;
    private SpawnParticles spawn;
    private ParticleDisplay display;

    // Buffers
    public ComputeBuffer pointsBuffer;
    private ComputeBuffer velocitiesBuffer;
    private ComputeBuffer densitiesBuffer;
    private ComputeBuffer debugBuffer;

    //Kernel IDs
    private const int ExternalForcesKernelID = 0;
    private const int CalcDensitiesKernelID = 1;
    private const int UpdatePositionsKernelID = 2;
    private const int ResolveBoundariesKernelID = 3;

    private const int floatSize = 8;
    private const int float4Size = 16;

    // Precompiled values
    public float3 realHalfBoundSize;
    private float smoothRad2;
    private float poly6Denom;
    private int threadGroups;

    public GpuFluidSimulation(ComputeShader compute, SpawnParticles spawn, ParticleDisplay display)
    {
        this.compute = compute;
        this.spawn = spawn;
        this.display = display;
    }

    public void InitializeConstants(
        int numParticles,
        float3 particleSize,
        float mass,
        float gravity,
        float smoothingRadius,
        float collisionDamp,
        float restDensity,
        float stiffness
    )
    {
        Precompute(numParticles, smoothingRadius);

        // Create buffers
        pointsBuffer = new ComputeBuffer(numParticles, float4Size);
        velocitiesBuffer = new ComputeBuffer(numParticles, float4Size);
        densitiesBuffer = new ComputeBuffer(numParticles, floatSize);
        debugBuffer = new ComputeBuffer(4, float4Size);

        // Assign the buffers to methods
        SetBuffers(ExternalForcesKernelID);
        SetBuffers(CalcDensitiesKernelID);
        SetBuffers(UpdatePositionsKernelID);
        SetBuffers(ResolveBoundariesKernelID);

        // Set constants
        compute.SetInt("numParticles", numParticles);
        compute.SetFloat("mass", mass);
        compute.SetFloat("gravity", gravity);
        compute.SetFloat("smoothingRadius", smoothingRadius);
        compute.SetFloat("smoothingRadius2", smoothRad2);
        compute.SetVector("realHalfBoundSize", new Vector4(realHalfBoundSize.x, realHalfBoundSize.y, realHalfBoundSize.z, 0));
        compute.SetFloat("collisionDamp", collisionDamp);
        compute.SetFloat("poly6Denom", poly6Denom);
        compute.SetFloat("restDensity", restDensity);
        compute.SetFloat("stiffness", stiffness);

        display.Setup();
    }

    public void InitializeStartingPoints()
    {
        var points = spawn.GetSpawnPositions();
        var velocities = spawn.GetSpawnVelocities();
        
        // Convert to float4
        float4[] points4 = new float4[points.Length];
        float4[] velocities4 = new float4[velocities.Length];
        for (int i = 0; i < points.Length; i++)
        {
            points4[i] = new float4(points[i].x, points[i].y, points[i].z, 0);
            velocities4[i] = new float4(velocities[i].x, velocities[i].y, velocities[i].z, 0);
        }

        // Set initial data in the buffers
        pointsBuffer.SetData(points4);
        velocitiesBuffer.SetData(velocities4);
    }

    public void CalculateStep(float deltaTime)
    {
        compute.SetFloat("deltaTime", deltaTime);

        compute.Dispatch(ExternalForcesKernelID, threadGroups, 1, 1);
        compute.Dispatch(ResolveBoundariesKernelID, threadGroups, 1, 1);
        compute.Dispatch(UpdatePositionsKernelID, threadGroups, 1, 1);
    }

    private void SetBuffers(int kernelID)
    {
        compute.SetBuffer(kernelID, "Points", pointsBuffer);
        compute.SetBuffer(kernelID, "Velocities", velocitiesBuffer);
        compute.SetBuffer(kernelID, "Densities", densitiesBuffer);
        compute.SetBuffer(kernelID, "Debug", debugBuffer);
    }

    public void OnDestroy()
    {
        if (pointsBuffer != null) pointsBuffer.Release();
        if (velocitiesBuffer != null) velocitiesBuffer.Release();
        if (densitiesBuffer != null) densitiesBuffer.Release();
        if (debugBuffer != null) debugBuffer.Release();
    }

    private void Precompute(int numPoints, float smoothingRadius)
    {
        realHalfBoundSize = spawn.boundSize / 2 - display.particleSize / 2;
        smoothRad2 = smoothingRadius * smoothingRadius;
        poly6Denom = 64 * Mathf.PI * Mathf.Pow(smoothingRadius, 9);
        threadGroups = Mathf.CeilToInt(numPoints / 256f);
    }
}