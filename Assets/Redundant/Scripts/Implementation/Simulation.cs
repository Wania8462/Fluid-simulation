using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

public struct HashLookup // If there is odd amount of particles buffer size won't match
{
    public int hash;
    public uint particleIndex;
}

[BurstCompile]
public class Simulation : MonoBehaviour
{
    [Header("Simulation settings")]
    [SerializeField] private bool useGPU;
    [SerializeField] private float gravity;
    [SerializeField] private float collisionDamp;
    [SerializeField] private float mass;
    [SerializeField] private float smoothingRadius;
    [SerializeField] private float restDensity;
    [SerializeField] private float stiffness;

    [Header("References")]
    [SerializeField] private ComputeShader compute;
    [SerializeField] private SpawnParticles spawn;
    [SerializeField] private ParticleDisplay display;
    [SerializeField] private CpuFluidSimulation cpuFluidSimulation;
    [SerializeField] private GpuFluidSimulation gpuFluidSimulation;

    private IFluidSimulation fluidSimulation;

    void Start()
    {
        //fluidSimulation = new GpuFluidSimulation(compute, spawn, display);
        // fluidSimulation = new CpuFluidSimulation(spawn);
        if(useGPU)
            fluidSimulation = gpuFluidSimulation;

        else
            fluidSimulation = cpuFluidSimulation;
            
        fluidSimulation.InitializeConstants(
            spawn.numParticles,
            display.particleSize,
            mass,
            gravity,
            smoothingRadius,
            collisionDamp,
            restDensity,
            stiffness
        );
        fluidSimulation.InitializeStartingPoints();
    }

    void Update()
    {
        fluidSimulation.CalculateStep(Time.deltaTime);
        DrawDebugCube(Vector3.zero, spawn.boundSize / 2 - display.particleSize / 2, Color.red);
        // GetDebugValues();
    }

    // private void GetDebugValues()
    // {
    //     if (Input.GetKeyDown(KeyCode.Space))
    //     {
    //         AsyncGPUReadback.Request(
    //             debugBuffer,
    //             request =>
    //             {
    //                 if (!request.hasError)
    //                 {
    //                     var data = request.GetData<float4>();
    //                     Debug.Log(data[0]);
    //                 }
    //             }
    //         );
    //     }
    // }

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
        Debug.DrawLine(p0, p1, color);
        Debug.DrawLine(p1, p2, color);
        Debug.DrawLine(p2, p3, color);
        Debug.DrawLine(p3, p0, color);

        // Top
        Debug.DrawLine(p4, p5, color);
        Debug.DrawLine(p5, p6, color);
        Debug.DrawLine(p6, p7, color);
        Debug.DrawLine(p7, p4, color);

        // Sides
        Debug.DrawLine(p0, p4, color);
        Debug.DrawLine(p1, p5, color);
        Debug.DrawLine(p2, p6, color);
        Debug.DrawLine(p3, p7, color);
    }
}