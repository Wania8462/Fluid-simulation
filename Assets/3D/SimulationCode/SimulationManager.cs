using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;

[Serializable]
public struct SimulationSettings3D
{
    [Header("Simulation settings")]
    public float interactionRadius;
    public float gravity;
    public float mouseAttractiveness;
    public float mouseRadius;
    public float collisionDamping;

    [Header("Density")]
    [Tooltip("PBF iterations per step. More keeps deep fluid less compressed")]
    public int solverIterations;
    // The stable range depends on spacing and interactionRadius, retune it if either changes
    [Tooltip("Softens the density constraint. Below about 0.02 (spacing 3, radius 6) the solver overshoots and the fluid blows apart, higher is springier")]
    public float relaxation;
    [Tooltip("Strength k of the s_corr term that keeps particles from clumping, 0.1 in the PBF paper")]
    public float artificialPressure;
    [Tooltip("Sim mass per sim unit³. Only sets the particles' mass, the fluid's packing doesn't change")]
    public float restDensity;

    [Header("Viscosity")]
    public float highViscosity;
    public float lowViscosity;

    [Header("Rigid bodies")]
    public float boundaryFriction;
    public float bodyFriction;
    public RigidBodySettings3D[] bodies;
}

public class SimulationManager : MonoBehaviour
{
    [Header("Simulation settings")]
    [SerializeField] private bool paused;
    [SerializeField] internal SimulationSettings3D settings;
    [SerializeField] public float particleRadius = 0.5f;
    [SerializeField] private int targetFrameRate = 120;
    [SerializeField] private bool useRealDeltaTime;
    [SerializeField] private float fakeFramerate = 120;

    [Header("References")]
    [SerializeField] private ComputeShader compute;
    [SerializeField] private Spawn3DParticles spawn;
    [Tooltip("Optional. With one, the fluid pours out of its reservoir and is recycled at its drain instead of spawning as a block")]
    [SerializeField] private FluidFlow3D flow;
    private SPValues3D SP;

    public bool Running { get; private set; }

    public event Action<SimulationManager> BuffersChanged;
    // Raised every Update, even when paused, since draws must be submitted each frame
    public event Action StepFinished;

    public int numParticles;
    public int numBoundaryParticles;
    public float InteractionRadius => settings.interactionRadius;
    public Tank Tank => spawn.GetTank(particleRadius);

    private Dictionary<string, int> KernelIDs;
    public Dictionary<string, ComputeBuffer> Buffers = new();

    private RigidBodyData3D bodies;
    private int numBodies;

    // Body buffers replaced by AddBody/RemoveBody, released at the start of the next Update
    private readonly List<ComputeBuffer> replacedBuffers = new();

    private int3 threadGroups;
    private int3 gridThreadGroups;
    private int3 boundaryThreadGroups;
    private int numScanSteps;

    private void Start()
    {
        Setup();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.R))
            Setup();

        if (replacedBuffers.Count > 0)
        {
            // Released here because draws queued last frame may still have used them
            foreach (ComputeBuffer buffer in replacedBuffers)
                ComputeHelper.Release(buffer);

            replacedBuffers.Clear();
            BuffersChanged?.Invoke(this);
        }

        if (Input.GetKeyDown(KeyCode.Space))
            paused = !paused;

        if (Input.GetKeyDown(KeyCode.P))
            DebugHelper3D.LogPositions(this);

        if (!paused || Input.GetKeyDown(KeyCode.RightArrow))
            ExecuteStep();

        else if (Input.GetKeyDown(KeyCode.PageDown))
        {
            for (int i = 0; i < 10; i++)
                ExecuteStep();
        }

        UpdateComputeSettings();
        StepFinished?.Invoke();
    }

    private void ExecuteStep()
    {
        Running = true;
        Watcher.ExecuteWithTimer("1. Time step", SimulationStep);
        Running = false;
    }

    private void SimulationStep()
    {
        float dt = useRealDeltaTime ? Time.deltaTime : 1 / fakeFramerate;
        compute.SetFloat("dt", dt);

        if (flow != null)
        {
            // Recycle before AdvancePredictedPositions so the teleport isn't turned into velocity
            flow.SetRecycleSeed(compute, Time.frameCount);
            compute.Dispatch(KernelIDs["RecycleParticles"], threadGroups);
        }

        compute.Dispatch(KernelIDs["ExternalForces"], threadGroups);

        // Viscosity reads the grid built from last step's predicted positions, which the solve has only nudged since
        compute.Dispatch(KernelIDs["ClearForceBuffers"], threadGroups);
        compute.Dispatch(KernelIDs["ApplyViscosity"], threadGroups);
        compute.Dispatch(KernelIDs["ApplyForceBuffersToVelocities"], threadGroups);

        compute.Dispatch(KernelIDs["AdvancePredictedPositions"], threadGroups);

        if (numBodies > 0)
        {
            DispatchPerBody("BodyAdvancePredictedStates");
            compute.Dispatch(KernelIDs["PlaceBoundaryParticles"], boundaryThreadGroups);
        }

        // PBF needs every neighbour of the predicted positions, so the grids are rebuilt every step
        BuildGrid();
        if (numBodies > 0)
            BuildBoundaryGrid();

        // The bodies are moved by the fluid inside every iteration, so they and the fluid settle together
        for (int i = 0; i < settings.solverIterations; i++)
        {
            compute.Dispatch(KernelIDs["CalculateLambdas"], threadGroups);

            if (numBodies > 0)
                compute.Dispatch(KernelIDs["ClearBoundaryForceBuffers"], boundaryThreadGroups);

            compute.Dispatch(KernelIDs["CalculateDeltaPositions"], threadGroups);
            compute.Dispatch(KernelIDs["ApplyDeltaPositions"], threadGroups);
            compute.Dispatch(KernelIDs["ResolveBoundaries"], threadGroups);

            if (flow != null)
                compute.Dispatch(KernelIDs["ResolveReservoir"], threadGroups);

            if (numBodies > 0)
            {
                DispatchPerBody("SumCouplingForces");
                DispatchPerBody("ApplyCouplingForces");
                compute.Dispatch(KernelIDs["PlaceBoundaryParticles"], boundaryThreadGroups);
            }
        }

        if (Input.GetMouseButton(0))
        {
            compute.SetVector("mousePosition", GetMousePos());
            compute.Dispatch(KernelIDs["AttractToMouse"], threadGroups);
        }

        if (numBodies > 0)
        {
            if (Input.GetMouseButton(1))
            {
                compute.SetVector("mousePosition", GetMousePos());
                DispatchPerBody("AttractBodiesToMouse");
            }

            DispatchPerBody("SumContacts");
            DispatchPerBody("ApplyContacts");
            DispatchPerBody("BodyCalculateVelocity");
            compute.Dispatch(KernelIDs["PlaceBoundaryParticles"], boundaryThreadGroups);
            compute.Dispatch(KernelIDs["ResolveBodyCollisions"], threadGroups);
        }

        compute.Dispatch(KernelIDs["ResolveBoundaries"], threadGroups);

        if (flow != null)
            compute.Dispatch(KernelIDs["ResolveReservoir"], threadGroups);

        compute.Dispatch(KernelIDs["CalculateVelocity"], threadGroups);
    }

    // Counting sort: cell c's particles end up in SortedIndices[CellStart[c]..CellStart[c + 1])
    private void BuildGrid()
    {
        compute.Dispatch(KernelIDs["ClearGrid"], gridThreadGroups);
        compute.Dispatch(KernelIDs["CountParticles"], threadGroups);

        for (int i = 0; i < numScanSteps; i++)
        {
            compute.SetInt("scanStride", 1 << i);
            compute.SetInt("scanFlip", i % 2);
            compute.Dispatch(KernelIDs["ScanStep"], gridThreadGroups);
        }

        compute.Dispatch(KernelIDs["ResetCursor"], gridThreadGroups);
        compute.Dispatch(KernelIDs["Scatter"], threadGroups);
    }

    private void BuildBoundaryGrid()
    {
        compute.Dispatch(KernelIDs["ClearBoundaryGrid"], gridThreadGroups);
        compute.Dispatch(KernelIDs["CountBoundaryParticles"], boundaryThreadGroups);

        for (int i = 0; i < numScanSteps; i++)
        {
            compute.SetInt("scanStride", 1 << i);
            compute.SetInt("scanFlip", i % 2);
            compute.Dispatch(KernelIDs["BoundaryScanStep"], gridThreadGroups);
        }

        compute.Dispatch(KernelIDs["ResetBoundaryCursor"], gridThreadGroups);
        compute.Dispatch(KernelIDs["ScatterBoundary"], boundaryThreadGroups);
    }

    private void DispatchPerBody(string kernel) => compute.Dispatch(KernelIDs[kernel], numBodies, 1, 1);

    private void OnValidate()
    {
        if (Buffers.ContainsKey("Positions"))
        {
            if (Buffers["Positions"] != null)
            {
                Application.targetFrameRate = targetFrameRate;
                UpdateComputeSettings();
            }
        }
    }

    private void Setup()
    {
        ReleaseBuffers();

        KernelIDs = ComputeHelper.GetKernels(compute);
        Buffers = ComputeHelper.GetBuffers(compute);

        Application.targetFrameRate = targetFrameRate;

        Tank tank = Tank;
        SP = new(-tank.halfSize, tank.halfSize, settings.interactionRadius);

        bodies = RigidBodies3D.Build(settings.bodies, spawn, settings.interactionRadius, tank);
        numBodies = bodies.NumBodies;
        numBoundaryParticles = bodies.NumBoundaryParticles;

        if (flow != null)
            flow.Validate(tank, settings.interactionRadius, particleRadius);

        float4[] spawned = flow != null ? flow.InitializePositions() : spawn.InitializePositions();

        // Fluid spawned too close to a body would be shot out on the first step
        float4[] positions = RigidBodies3D.RemoveParticlesInsideBodies(spawned, bodies.settings, settings.interactionRadius * 0.5f);
        numParticles = positions.Length;

        CreateBuffers(positions);

        SetBuffers();
        SetComputeSettings();

        threadGroups = compute.GetThreadGroups(0, numParticles);
        gridThreadGroups = compute.GetThreadGroups(KernelIDs["ClearGrid"], SP.NumCells + 1);
        boundaryThreadGroups = compute.GetThreadGroups(0, numBoundaryParticles);

        numScanSteps = 0;
        while ((1 << numScanSteps) < SP.NumCells + 1) numScanSteps++;
        // An even pass count makes the ping-pong end in CellStart; the extra pass is a plain copy
        if (numScanSteps % 2 == 1) numScanSteps++;

        // The first step's viscosity reads the grid before that step builds it
        BuildGrid();

        if (numBodies > 0)
        {
            BuildBoundaryGrid();
            compute.Dispatch(KernelIDs["CalculateBoundaryVolumes"], boundaryThreadGroups);
        }

        BuffersChanged?.Invoke(this);
    }

    internal int AddBody(RigidBodySettings3D body)
    {
        if (settings.bodies.Count(slot => !slot.IsEmpty) >= RigidBodies3D.MaxBodies)
            throw new InvalidOperationException($"At most {RigidBodies3D.MaxBodies} rigid bodies are supported.");

        int id = Array.FindIndex(settings.bodies, slot => slot.IsEmpty);
        if (id < 0)
        {
            id = settings.bodies.Length;
            Array.Resize(ref settings.bodies, id + 1);
        }

        settings.bodies[id] = body;

        if (bodies == null) return id;

        ReadBodyStates();
        RigidBodies3D.AddBody(bodies, body, id, spawn);
        RebuildBodies();
        return id;
    }

    internal void RemoveBody(int id)
    {
        if (id < 0 || id >= settings.bodies.Length || settings.bodies[id].IsEmpty)
            throw new ArgumentException($"There is no rigid body with id {id}.", nameof(id));

        settings.bodies[id] = default;

        if (bodies == null) return;

        ReadBodyStates();
        RigidBodies3D.RemoveBody(bodies, id);
        RebuildBodies();
    }

    // Bodies move on the GPU, so copy their current states back before rebuilding
    private void ReadBodyStates()
    {
        if (numBodies == 0) return;

        RigidBodyState3D[] states = new RigidBodyState3D[numBodies];
        Buffers["BodyStates"].GetData(states);
        bodies.states.Clear();
        bodies.states.AddRange(states);
    }

    private void RebuildBodies()
    {
        List<ComputeBuffer> previous = Buffers.Values.ToList();
        RigidBodies3D.CreateBuffers(Buffers, bodies, SP.NumCells);
        // Except yields each replaced buffer once even though read-only aliases share them
        replacedBuffers.AddRange(previous.Except(Buffers.Values));

        numBodies = bodies.NumBodies;
        numBoundaryParticles = bodies.NumBoundaryParticles;
        boundaryThreadGroups = compute.GetThreadGroups(0, numBoundaryParticles);
        SetBuffers();
        UpdateComputeSettings();

        if (numBodies > 0)
        {
            compute.Dispatch(KernelIDs["PlaceBoundaryParticles"], boundaryThreadGroups);
            BuildBoundaryGrid();
            compute.Dispatch(KernelIDs["CalculateBoundaryVolumes"], boundaryThreadGroups);

            // Push fluid out of new bodies now, before PrevPositions is saved, so it doesn't become velocity
            compute.Dispatch(KernelIDs["ResolveBodyCollisions"], threadGroups);
        }
    }

    #region Buffer helpers
    private void UpdateComputeSettings()
    {
        compute.SetFloat("interactionRadius", settings.interactionRadius);
        compute.SetFloat("interactionRadiusSq", settings.interactionRadius * settings.interactionRadius);
        compute.SetFloat("gravity", settings.gravity);
        compute.SetFloat("mouseAttractiveness", settings.mouseAttractiveness);
        compute.SetFloat("mouseRadius", settings.mouseRadius);
        compute.SetFloat("collisionDamp", settings.collisionDamping);

        if (settings.solverIterations < 1)
            Debug.LogWarning($"Simulation manager: solverIterations is {settings.solverIterations}, nothing will keep the fluid from compressing");

        // The fluid rests at the spawn spacing, which V = spacing³ gives to within about 1% for Poly6 at h = 2 spacings
        float h = settings.interactionRadius;
        float spacing = flow != null ? flow.Spacing : spawn.Spacing;
        float particleVolume = math.pow(spacing, 3);
        compute.SetFloat("particleVolume", particleVolume);
        compute.SetFloat("particleMass", settings.restDensity * particleVolume);
        compute.SetFloat("restSpacing", spacing);
        compute.SetFloat("relaxation", settings.relaxation);
        compute.SetFloat("artificialPressure", settings.artificialPressure);
        compute.SetFloat("poly6Factor", 315 / (64 * math.PI * math.pow(h, 9)));
        compute.SetFloat("spikyGradFactor", 45 / (math.PI * math.pow(h, 6)));

        compute.SetFloat("highViscosity", settings.highViscosity);
        compute.SetFloat("lowViscosity", settings.lowViscosity);

        if (numBodies > 0)
        {
            if (settings.boundaryFriction <= 0)
                Debug.LogWarning($"Simulation manager: boundaryFriction is {settings.boundaryFriction}, fluid particles won't be slowed near the rigid bodies and may tunnel through them");

            RigidBodies3D.UpdateMasses(bodies, settings.bodies, Buffers["BodyProperties"]);
        }

        compute.SetFloat("boundaryFriction", settings.boundaryFriction);
        compute.SetFloat("bodyFriction", settings.bodyFriction);

        compute.SetInt("numBoundaryParticles", numBoundaryParticles);
        compute.SetInt("numBodies", numBodies);

        if (flow != null)
            flow.SetComputeSettings(compute);
    }

    private void SetComputeSettings()
    {
        compute.SetInt("numParticles", numParticles);
        UpdateComputeSettings();

        float3 innerHalfSize = Tank.innerHalfSize;
        compute.SetVector("realHalfBoundSize", new Vector4(innerHalfSize.x, innerHalfSize.y, innerHalfSize.z));
        compute.SetFloat("particleRadius", particleRadius);

        compute.SetInt("numCells", SP.NumCells);
        compute.SetVector("offset", new(SP.offset.x, SP.offset.y, SP.offset.z, 0));
        compute.SetFloat("cellLength", SP.length);
        compute.SetInt("columns", SP.columns);
        compute.SetInt("rows", SP.rows);
        compute.SetInt("layers", SP.layers);
    }

    private void SetBuffers()
    {
        foreach (var kernel in KernelIDs)
            foreach (var buffer in Buffers)
                compute.SetBuffer(kernel.Value, buffer.Key, buffer.Value);
    }

    private void CreateBuffers(float4[] positions)
    {
        if (numParticles == 0)
            Debug.LogWarning("Simulation manager: there are 0 particles. Creating non-existant buffers.");

        if (SP.NumCells <= 0)
            Debug.LogError($"Simulation manager: grid has {SP.NumCells} cells. interactionRadius is too small for the bounding box, so every neighbour query will read out of range.");

        Buffers["Positions"] = ComputeHelper.CreateStructuredBufferWithData(positions);
        Buffers["PrevPositions"] = ComputeHelper.CreateStructuredBufferWithData<float4>(numParticles);
        Buffers["ForceBuffersX"] = ComputeHelper.CreateStructuredBufferWithData<int>(numParticles);
        Buffers["ForceBuffersY"] = ComputeHelper.CreateStructuredBufferWithData<int>(numParticles);
        Buffers["ForceBuffersZ"] = ComputeHelper.CreateStructuredBufferWithData<int>(numParticles);
        Buffers["Velocities"] = ComputeHelper.CreateStructuredBufferWithData<float4>(numParticles);

        Buffers["Densities"] = ComputeHelper.CreateStructuredBufferWithData<float>(numParticles);
        Buffers["Lambdas"] = ComputeHelper.CreateStructuredBufferWithData<float>(numParticles);
        Buffers["DeltaPositions"] = ComputeHelper.CreateStructuredBufferWithData<float4>(numParticles);

        Buffers["CellStart"] = ComputeHelper.CreateStructuredBufferWithData<uint>(SP.NumCells + 1);
        Buffers["ScanTemp"] = ComputeHelper.CreateStructuredBufferWithData<uint>(SP.NumCells + 1);
        Buffers["CellCursor"] = ComputeHelper.CreateStructuredBufferWithData<uint>(SP.NumCells + 1);
        Buffers["SortedIndices"] = ComputeHelper.CreateStructuredBufferWithData<uint>(numParticles);

        // Read-only aliases keep the heavy kernels within the 8 read-write resources a compute shader gets
        Buffers["PositionsRO"] = Buffers["Positions"];
        Buffers["VelocitiesRO"] = Buffers["Velocities"];
        Buffers["LambdasRO"] = Buffers["Lambdas"];
        Buffers["CellStartRO"] = Buffers["CellStart"];
        Buffers["SortedIndicesRO"] = Buffers["SortedIndices"];

        RigidBodies3D.CreateBuffers(Buffers, bodies, SP.NumCells);
    }

    private void ReleaseBuffers()
    {
        foreach (var buffer in Buffers.Values.Concat(replacedBuffers).Distinct())
            ComputeHelper.Release(buffer);

        replacedBuffers.Clear();
    }

    private void OnDestroy()
    {
        ReleaseBuffers();
    }

#if UNITY_EDITOR
    private void OnEnable()
    {
        UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += ReleaseBuffers;
    }

    private void OnDisable()
    {
        UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= ReleaseBuffers;
    }
#endif

    #endregion

    private Vector4 GetMousePos()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        // Mouse interaction happens on the z = 0 plane
        new Plane(Vector3.forward, 0).Raycast(ray, out float enter);
        Vector3 point = ray.GetPoint(enter);
        return new(point.x, point.y, point.z, 0);
    }
}