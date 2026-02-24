using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

[Serializable]
public class SimulationSettings
{
    [Header("Simulation settings")]
    public float interactionRadius;
    public float gravity;
    public float mouseAttractiveness;
    public float mouseRadius;
    public float collisionDamping;
    public bool useParticlesAsBorder;

    // [Header("Body settings")]
    // public Body body;

    [Header("Density")]
    public float stiffness;
    public float nearStiffness;
    public float borderStiffness;
    public float restDensity;

    [Header("Springs")]
    public float springInteractionRadius;
    public float springRadius;
    public float springStiffness;
    public float springDeformationLimit;
    public float plasticity;
    public float highViscosity;
    public float lowViscosity;

    public SimulationSettings() { }

    public SimulationSettings(SimulationSettings settings)
    {
        interactionRadius = settings.interactionRadius;
        gravity = settings.gravity;
        mouseAttractiveness = settings.mouseAttractiveness;
        mouseRadius = settings.mouseRadius;
        collisionDamping = settings.collisionDamping;
        useParticlesAsBorder = settings.useParticlesAsBorder;

        // body = settings.body;

        stiffness = settings.stiffness;
        nearStiffness = settings.nearStiffness;
        borderStiffness = settings.borderStiffness;
        restDensity = settings.restDensity;

        springInteractionRadius = settings.springInteractionRadius;
        springRadius = settings.springRadius;
        springStiffness = settings.springStiffness;
        springDeformationLimit = settings.springDeformationLimit;
        plasticity = settings.plasticity;
        highViscosity = settings.highViscosity;
        lowViscosity = settings.lowViscosity;
    }
}

public class GPUSimulationManager : MonoBehaviour
{
    [SerializeField] private SimulationSettings settings;
    [SerializeField] public float particleRadius;
    [SerializeField] private bool useRealDeltaTime;

    [SerializeField] private GPURender render;
    [SerializeField] private Spawn2DParticles spawn;
    [SerializeField] private ComputeShader compute;

    private int numParticles;

    public readonly Dictionary<string, ComputeBuffer> buffers =
    new()
    {
        { "Positions", null },
        { "PrevPositions", null },
        { "Velocities", null },
        { "Densities", null },
        { "NearDensities", null },
        { "Springs", null },
    };

    private readonly Dictionary<string, int> KernelIDs =
    new()
    {
        { "ExternalForces", 0 },
        { "DoubleDensityRelaxation", 1 },
        { "ApplyViscosity", 2 },
        { "AdjustSprings", 3 },
        { "SpringDisplacements", 4 },
        { "ResolveBoundaries", 5 },
        { "AdvancePredictedPositions", 6 },
        { "CalculateVelocity", 7 }
    };

    private const int threadGropus = 256;
    private const int floatSize = 4;
    private const int float2Size = 8;
    private const float fakeDT = 1 / 60f;

    private void Start()
    {
        numParticles = spawn.GetNumberOfParticles();
        CreateBuffers();
        SetBuffers();
        SetComputeSettings();
        buffers["Positions"].SetData(spawn.InitializePositions());
        buffers["PrevPositions"].SetData(spawn.InitializePositions());
        render.Setup();
    }

    private void Update()
    {
        SimulationStep();
        render.DrawParticles();
    }

    private void SimulationStep()
    {
        float dt = useRealDeltaTime ? Time.deltaTime : fakeDT;
        compute.SetFloat("dt", dt);

        compute.Dispatch(KernelIDs["ExternalForces"], threadGropus, 1, 1);
        compute.Dispatch(KernelIDs["ApplyViscosity"], threadGropus, 1, 1);
        compute.Dispatch(KernelIDs["AdvancePredictedPositions"], threadGropus, 1, 1);
        compute.Dispatch(KernelIDs["AdjustSprings"], threadGropus, 1, 1);
        compute.Dispatch(KernelIDs["SpringDisplacements"], threadGropus, 1, 1);
        compute.Dispatch(KernelIDs["DoubleDensityRelaxation"], threadGropus, 1, 1);
        compute.Dispatch(KernelIDs["ResolveBoundaries"], threadGropus, 1, 1);
        compute.Dispatch(KernelIDs["CalculateVelocity"], threadGropus, 1, 1);
    }

    private void SetComputeSettings()
    {
        compute.SetInt("numParticles", numParticles);
        compute.SetFloat("interactionRadius", settings.interactionRadius);
        compute.SetFloat("gravity", settings.gravity);
        compute.SetFloat("mouseAttractiveness", settings.mouseAttractiveness);
        compute.SetFloat("mouseRadius", settings.mouseRadius);
        compute.SetFloat("collisionDamp", settings.collisionDamping);

        compute.SetFloat("stiffness", settings.stiffness);
        compute.SetFloat("nearStiffness", settings.nearStiffness);
        compute.SetFloat("restDensity", settings.restDensity);

        compute.SetFloat("springInteractionRadius", settings.springInteractionRadius);
        compute.SetFloat("springStiffness", settings.springStiffness);
        compute.SetFloat("springDeformationLimit", settings.springDeformationLimit);
        compute.SetFloat("plasticity", settings.plasticity);
        compute.SetFloat("highViscosity", settings.highViscosity);
        compute.SetFloat("lowViscosity", settings.lowViscosity);

        float2 rhbs = spawn.GetRealHalfBoundSize(particleRadius);
        compute.SetVector("realHalfBoundSize", new Vector4(rhbs.x, rhbs.y));
        compute.SetFloat("particleRadius", particleRadius);
    }

    private void SetBuffers()
    {
        foreach (var kernel in KernelIDs)
            foreach (var buffer in buffers)
                compute.SetBuffer(kernel.Value, buffer.Key, buffer.Value);
    }

    private void CreateBuffers()
    {
        buffers["Positions"] = new ComputeBuffer(numParticles, float2Size);
        buffers["PrevPositions"] = new ComputeBuffer(numParticles, float2Size);
        buffers["Velocities"] = new ComputeBuffer(numParticles, float2Size);
        buffers["Densities"] = new ComputeBuffer(numParticles, floatSize);
        buffers["NearDensities"] = new ComputeBuffer(numParticles, floatSize);
        // Double the size if need more springs
        buffers["Springs"] = new ComputeBuffer(numParticles * 50, floatSize);
    }

    private void OnDestroy()
    {
        foreach (var buffer in buffers)
            buffer.Value?.Release();
    }
}