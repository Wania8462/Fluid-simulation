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

    [HideInInspector] public float3[] points { get; private set; }
    [HideInInspector] public float3[] velocities { get; private set; }

    //Kernel IDs
    private const int ExternalForcesKernelID = 0;
    private const int CalcDensitiesKernelID = 1;
    private const int UpdatePositionsKernelID = 2;
    private const int ResolveCollisionsKernelID = 3;

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
        pointsBuffer = new(spawn.pointsAmount, float3Size);
        velocitiesBuffer = new(spawn.pointsAmount, float3Size);
        densitiesBuffer = new(spawn.pointsAmount, floatSize);
        debugBuffer = new(4, floatSize);

        // Set initial data in the buffers
        pointsBuffer.SetData(points);
        velocitiesBuffer.SetData(velocities);

        // Assign the buffers to methods
        // todo: make method for prettier setting
        compute.SetBuffer(ExternalForcesKernelID, "Points", pointsBuffer);
        compute.SetBuffer(ExternalForcesKernelID, "Velocities", velocitiesBuffer);

        compute.SetBuffer(CalcDensitiesKernelID, "Densities", densitiesBuffer);

        compute.SetBuffer(UpdatePositionsKernelID, "Points", pointsBuffer);
        compute.SetBuffer(UpdatePositionsKernelID, "Velocities", velocitiesBuffer);
        compute.SetBuffer(UpdatePositionsKernelID, "Debug", debugBuffer);

        compute.SetBuffer(ResolveCollisionsKernelID, "Points", pointsBuffer);
        compute.SetBuffer(ResolveCollisionsKernelID, "Velocities", velocitiesBuffer);
        compute.SetBuffer(ResolveCollisionsKernelID, "Debug", debugBuffer);

        compute.SetInt("numParticles", points.Length);
        compute.SetFloat("mass", mass);
        compute.SetFloat("gravity", gravity);
        compute.SetFloat("smoothingRadius", smoothingRadius);
        compute.SetFloat("smoothingRadius2", smoothRad2);
        compute.SetVector("realHalfBoundSize", new Vector4(realHalfBoundSize.x, realHalfBoundSize.y, realHalfBoundSize.z, 0));
        compute.SetFloat("collisionDamp", collisionDamp);

        display.Setup();
    }

    void Update()
    {
        SimulationFrame();
        Debug();
    }

    private void SimulationFrame()
    {
        compute.SetFloat("deltaTime", Time.deltaTime);

        compute.Dispatch(ExternalForcesKernelID, threadGroups, 1, 1);
        compute.Dispatch(ResolveCollisionsKernelID, threadGroups, 1, 1);
        compute.Dispatch(UpdatePositionsKernelID, threadGroups, 1, 1);
    }

    private void Precompute()
    {
        realHalfBoundSize = spawn.boundSize / 2 - display.particleSize / 2;
        smoothRad2 = smoothingRadius * smoothingRadius;
        poly6KernDenom = 64 * Mathf.PI * Mathf.Pow(smoothingRadius, 9);
        threadGroups = Mathf.CeilToInt(points.Length / 256f);
    }

    private void Debug()
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
                        // Debug.Log(data[i])
                    }
                }
            );
        }
    }

    void OnDestroy()
    {
        pointsBuffer.Release();
        velocitiesBuffer.Release();
        densitiesBuffer.Release();
        debugBuffer.Release();
    }
}