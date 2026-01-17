using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

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
    private ComputeBuffer debugBuffer;

    [HideInInspector] public float4[] points { get; private set; }
    [HideInInspector] public float4[] velocities { get; private set; }

    //Kernel IDs
    private const int ExternalForcesKernelID = 0;
    private const int CalcDensitiesKernelID = 1;
    private const int UpdatePositionsKernelID = 2;
    private const int ResolveBoundariesKernelID = 3;

    private const int floatSize = 8;
    private const int float4Size = 16;

    // Precompiled values
    private float3 realHalfBoundSize;
    private float smoothRad2;
    private float poly6Denom;
    private int threadGroups;

    void Start()
    {
        points = spawn.GetSpawnPositions();
        velocities = spawn.GetSpawnVelocities();

        Precompute();

        // Create buffers
        pointsBuffer = new ComputeBuffer(spawn.pointsAmount, float4Size);
        velocitiesBuffer = new ComputeBuffer(spawn.pointsAmount, float4Size);
        densitiesBuffer = new ComputeBuffer(spawn.pointsAmount, floatSize);

        debugBuffer = new ComputeBuffer(4, floatSize);

        // Set initial data in the buffers
        pointsBuffer.SetData(points);
        velocitiesBuffer.SetData(velocities);

        // Assign the buffers to methods
        SetBuffers(ExternalForcesKernelID);
        SetBuffers(CalcDensitiesKernelID);
        SetBuffers(UpdatePositionsKernelID);
        SetBuffers(ResolveBoundariesKernelID);

        // Set constants
        compute.SetInt("numParticles", points.Length);
        compute.SetFloat("mass", mass);
        compute.SetFloat("gravity", gravity);
        compute.SetFloat("smoothingRadius", smoothingRadius);
        compute.SetFloat("smoothingRadius2", smoothRad2);
        compute.SetVector("realHalfBoundSize", new Vector4(realHalfBoundSize.x, realHalfBoundSize.y, realHalfBoundSize.z, 0));
        compute.SetFloat("collisionDamp", collisionDamp);
        compute.SetFloat("poly6Denom", poly6Denom);

        display.Setup();
    }

    void Update()
    {
        SimulationFrame();
        DrawDebugCube(Vector3.zero, realHalfBoundSize, Color.red);
        GetDebugValues();
    }

    private void SimulationFrame()
    {
        compute.SetFloat("deltaTime", Time.deltaTime);

        // compute.Dispatch(CalcDensitiesKernelID, threadGroups, 1, 1);
        compute.Dispatch(ExternalForcesKernelID, threadGroups, 1, 1);
        compute.Dispatch(ResolveBoundariesKernelID, threadGroups, 1, 1);
        compute.Dispatch(UpdatePositionsKernelID, threadGroups, 1, 1);
    }

    private void Precompute()
    {
        realHalfBoundSize = spawn.boundSize / 2 - display.particleSize / 2;
        smoothRad2 = smoothingRadius * smoothingRadius;
        poly6Denom = 64 * Mathf.PI * Mathf.Pow(smoothingRadius, 9);
        threadGroups = Mathf.CeilToInt(points.Length / 256f);
    }

    private void SetBuffers(int kernelID)
    {
        compute.SetBuffer(kernelID, "Points", pointsBuffer);
        compute.SetBuffer(kernelID, "Velocities", velocitiesBuffer);
        compute.SetBuffer(kernelID, "Densities", densitiesBuffer);
        compute.SetBuffer(kernelID, "Debug", debugBuffer);
    }

    private void GetDebugValues()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            AsyncGPUReadback.Request(
                debugBuffer,
                request =>
                {
                    if (!request.hasError)
                    {
                        var data = request.GetData<float>();
                        UnityEngine.Debug.Log(data[0]);
                    }
                }
            );
        }
    }

    private void DrawDebugCube(Vector3 center, Vector3 halfSize, Color color)
    {
        // 8 corners
        Vector3 p0 = center + new Vector3(-halfSize.x, -halfSize.y, -halfSize.z);
        Vector3 p1 = center + new Vector3(halfSize.x, -halfSize.y, -halfSize.z);
        Vector3 p2 = center + new Vector3(halfSize.x, -halfSize.y, halfSize.z);
        Vector3 p3 = center + new Vector3(-halfSize.x, -halfSize.y, halfSize.z);

        Vector3 p4 = center + new Vector3(-halfSize.x, halfSize.y, -halfSize.z);
        Vector3 p5 = center + new Vector3(halfSize.x, halfSize.y, -halfSize.z);
        Vector3 p6 = center + new Vector3(halfSize.x, halfSize.y, halfSize.z);
        Vector3 p7 = center + new Vector3(-halfSize.x, halfSize.y, halfSize.z);

        // Bottom
        UnityEngine.Debug.DrawLine(p0, p1, color);
        UnityEngine.Debug.DrawLine(p1, p2, color);
        UnityEngine.Debug.DrawLine(p2, p3, color);
        UnityEngine.Debug.DrawLine(p3, p0, color);

        // Top
        UnityEngine.Debug.DrawLine(p4, p5, color);
        UnityEngine.Debug.DrawLine(p5, p6, color);
        UnityEngine.Debug.DrawLine(p6, p7, color);
        UnityEngine.Debug.DrawLine(p7, p4, color);

        // Sides
        UnityEngine.Debug.DrawLine(p0, p4, color);
        UnityEngine.Debug.DrawLine(p1, p5, color);
        UnityEngine.Debug.DrawLine(p2, p6, color);
        UnityEngine.Debug.DrawLine(p3, p7, color);
    }

    void OnDestroy()
    {
        pointsBuffer.Release();
        velocitiesBuffer.Release();
        densitiesBuffer.Release();
        debugBuffer.Release();
    }
}