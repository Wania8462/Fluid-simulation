using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Rendering;
using SimulationLogic;
using Unity.Mathematics;
using UnityEngine;

public enum DebugDisplay
{
    SPBox,
    AllNeighbours,
    Velocity,
    Force
}

public enum RenderType
{
    Particles,
    MarchingSquares,
    DensityMap
}

public class RenderDataBuilder : MonoBehaviour
{
    [Header("Render settings")]
    [SerializeField] private RenderType renderType;
    [SerializeField] private int Offset;
    public int offset { get; private set; }

    [Header("References")]
    [SerializeField] private SimulationManager manager;
    [SerializeField] private SpawnParticles spawn;
    [SerializeField] private RenderParticles renderParticles;
    [SerializeField] private RenderMarchingSquares renderSquares;
    [SerializeField] private RenderDensityMap renderDensityMap;

    [Header("Debug settings")]
    [SerializeField] private int trackParticle = -1;
    [SerializeField] private DebugDisplay particleDebugDisplay;
    [SerializeField] private bool bodyDebug;
    [SerializeField] private DebugDisplay bodyDebugDisplay;

    private Simulation simulation;

    private float2[] renderPositions;
    private float2[] renderVelocities;
    private float2[] renderBodyPositions;

    private float[] densities;
    private float[] densitiesMap;

    // Debug buffers
    private List<int> greenParticles = new();
    private List<int> yellowParticles = new();

    public void Init(Simulation sim)
    {
        simulation = sim;
        Camera.main.orthographicSize = manager.twoSim ? offset : Camera.main.orthographicSize;

        renderParticles.DeleteAllTypesOfParticles();
        renderParticles.InitParticles(simulation._positions);
        renderParticles.InitCustomParticle(manager.settings[0].body.position, manager.settings[0].body.radius, Color.antiqueWhite);

        renderSquares.Init(spawn.GetBoundSize());
        densities = new float[renderSquares.edges.Length];

        renderDensityMap.Init(spawn.GetBoundSize());
        densitiesMap = new float[renderDensityMap.cells.Length];
    }

    public void Draw()
    {
        if (renderType == RenderType.Particles)
            DrawParticles();

        else if (renderType == RenderType.MarchingSquares)
            DrawMarchingSquares();

        else if (renderType == RenderType.DensityMap)
            DrawDensityMap();
    }

    #region Particles
    private void DrawParticles()
    {
        HandleKeyInputs();
        greenParticles.Clear();
        yellowParticles.Clear();
        HighlghtParticlesForDebug();

        if (manager.settings[0].includeBody)
            HighlighParticlesBodyForDebug();

        renderParticles.DrawParticles(simulation._positions, simulation._velocities, greenParticles, yellowParticles);
    }

    private void HighlghtParticlesForDebug()
    {
        if (trackParticle == -1) return;

        if (trackParticle >= simulation._positions.Length || trackParticle < 0)
        {
            Debug.LogError($"Simulation manager: tracked particle index is out of range. Number of particles: {simulation._positions.Length}");
            return;
        }

        yellowParticles.Add(trackParticle);

        if (particleDebugDisplay == DebugDisplay.SPBox)
        {
            greenParticles.AddRange(simulation.GetParticlesSPNeighbours(trackParticle));

            var lineThickness = 0.2f;
            var SPBox = simulation.GetParticleSPDimentions(trackParticle);

            renderParticles.DrawLine(SPBox[0], SPBox[1], lineThickness, Color.white);
            renderParticles.DrawLine(SPBox[0], SPBox[2], lineThickness, Color.white);
            renderParticles.DrawLine(SPBox[1], SPBox[3], lineThickness, Color.white);
            renderParticles.DrawLine(SPBox[2], SPBox[3], lineThickness, Color.white);
        }

        else if (particleDebugDisplay == DebugDisplay.AllNeighbours)
        {
            var lineThickness = 0.1f;
            var neighbours = simulation.GetNeighbourParticles(trackParticle);
            greenParticles.AddRange(neighbours);

            var neighbourPoss = new float2[neighbours.Length];
            for (int i = 0; i < neighbours.Length; i++)
                neighbourPoss[i] = simulation._positions[neighbours[i]];

            renderParticles.DrawLines(simulation._positions[trackParticle], neighbourPoss, lineThickness);
        }

        else if (particleDebugDisplay == DebugDisplay.Velocity)
        {
            var lineThickness = 0.5f;
            var predictedPos = simulation._positions[trackParticle] + simulation._velocities[trackParticle];
            renderParticles.DrawLine(simulation._positions[trackParticle], predictedPos, lineThickness, Color.white);
        }

        else if (particleDebugDisplay == DebugDisplay.Force)
        {
            var lineThickness = 0.5f;
            var force = (simulation._velocities[trackParticle] - simulation._prevVelocities[trackParticle]) * 5;
            var forceEnd = simulation._positions[trackParticle] + force;
            renderParticles.DrawLine(simulation._positions[trackParticle], forceEnd, lineThickness, Color.white);
        }
    }

    private void HighlighParticlesBodyForDebug()
    {
        if (!bodyDebug) return;

        if (bodyDebugDisplay == DebugDisplay.SPBox)
        {
            greenParticles.AddRange(simulation.GetBodySPNeighbours());

            var lineThickness = 0.2f;
            var SPBox = simulation.GetBodySPDimentions();

            renderParticles.DrawLine(SPBox[0], SPBox[1], lineThickness, Color.white);
            renderParticles.DrawLine(SPBox[0], SPBox[2], lineThickness, Color.white);
            renderParticles.DrawLine(SPBox[1], SPBox[3], lineThickness, Color.white);
            renderParticles.DrawLine(SPBox[2], SPBox[3], lineThickness, Color.white);
        }

        else if (bodyDebugDisplay == DebugDisplay.AllNeighbours)
            greenParticles.AddRange(simulation.GetBodyNeighbours());

        else if (particleDebugDisplay == DebugDisplay.Velocity)
        {
            var lineThickness = 0.5f;
            var predictedPos = simulation.body.position + simulation.body.velocity;
            renderParticles.DrawLine(simulation.body.position, predictedPos, lineThickness, Color.white);
        }

        else if (particleDebugDisplay == DebugDisplay.Force)
            Debug.Log("Simulation manager: Body force isn't implemented");
    }

    private void HandleKeyInputs()
    {
        if (!Input.GetKey(KeyCode.LeftShift))
            HandleParticleInputs();

        else
            HandleBodyInputs();
    }

    private void HandleParticleInputs()
    {
        if (Input.GetKeyDown(KeyCode.B))
            particleDebugDisplay = DebugDisplay.SPBox;

        else if (Input.GetKeyDown(KeyCode.A))
            particleDebugDisplay = DebugDisplay.AllNeighbours;

        else if (Input.GetKeyDown(KeyCode.V))
            particleDebugDisplay = DebugDisplay.Velocity;

        else if (Input.GetKeyDown(KeyCode.F))
            particleDebugDisplay = DebugDisplay.Force;

        else if (Input.GetKeyDown(KeyCode.W))
        {
            var pos = new float2(Camera.main.ScreenToWorldPoint(Input.mousePosition).x, Camera.main.ScreenToWorldPoint(Input.mousePosition).y);
            var neighboursIndices = simulation.GetNeighbourParticles(pos);

            if (neighboursIndices.Length > 0)
            {
                var neighboursPos = simulation.GetNeighbourParticlesPositions(pos);
                var magnitudes = neighboursPos.Select(x => FluidMath.Distance(pos, x)).ToArray();
                trackParticle = neighboursIndices[Array.IndexOf(magnitudes, magnitudes.Min())];
            }
        }

        else if (Input.GetKeyDown(KeyCode.P))
            trackParticle = -1;
    }

    private void HandleBodyInputs()
    {
        if (Input.GetKey(KeyCode.LeftShift) && Input.GetKeyDown(KeyCode.P))
            bodyDebug = !bodyDebug;

        else if (Input.GetKey(KeyCode.LeftShift) && Input.GetKeyDown(KeyCode.B))
            bodyDebugDisplay = DebugDisplay.SPBox;

        else if (Input.GetKey(KeyCode.LeftShift) && Input.GetKeyDown(KeyCode.A))
            bodyDebugDisplay = DebugDisplay.AllNeighbours;

        else if (Input.GetKey(KeyCode.LeftShift) && Input.GetKeyDown(KeyCode.V))
            bodyDebugDisplay = DebugDisplay.Velocity;

        else if (Input.GetKey(KeyCode.LeftShift) && Input.GetKeyDown(KeyCode.F))
            bodyDebugDisplay = DebugDisplay.Force;
    }
    #endregion

    private void DrawMarchingSquares()
    {
        Parallel.For(0, densities.Length, i =>
        {
            densities[i] = simulation.GetDensity(renderSquares.edges[i]);
        });

        renderSquares.DrawLerp(densities);
    }

    private void DrawDensityMap()
    {
        int batchSize = 100;
        int threadCount = (int)math.ceil((float)densitiesMap.Length / batchSize);

        Parallel.For(0, threadCount, threadIndex =>
        {
            int start = threadIndex * batchSize;
            int end = Math.Min(start + batchSize, densitiesMap.Length);

            for (int i = start; i < end; i++)
            {
                densitiesMap[i] = simulation.GetDensity(renderDensityMap.cells[i]);
            }
        });

        renderDensityMap.Draw(densitiesMap);
    }

    private void OnDestroy()
    {
        renderParticles.DestroyMeshes();
        renderSquares.DestroyMeshes();
    }
}

// CODE FOR 2 SIMULATIONS
// INIT
// Calculates the total number of particles for 2 simulations. Not used if only 1 simulation is active
// renderPositions = new float2[simulations[FirstSim]._positions.Length * 2];
// renderVelocities = new float2[simulations[FirstSim]._positions.Length * 2];
// if (twoSim) renderBodyPositions = new float2[2];
// DRAW
// Packs the offseted positions into a single array for rendering along with combining velocities into one array
// Parallel.For(0, simulations[FirstSim]._positions.Length, i =>
// {
//     renderPositions[i].x = simulations[FirstSim]._positions[i].x - offset;
//     renderPositions[i].y = simulations[FirstSim]._positions[i].y;
//     renderVelocities[i] = simulations[FirstSim]._velocities[i];
// });

// Parallel.For(0, simulations[SecondSim]._positions.Length, i =>
// {
//     renderPositions[i + simulations[SecondSim]._positions.Length].x = simulations[SecondSim]._positions[i].x + offset;
//     renderPositions[i + simulations[SecondSim]._positions.Length].y = simulations[SecondSim]._positions[i].y;
//     renderVelocities[i + simulations[SecondSim]._positions.Length] = simulations[SecondSim]._velocities[i];
// });

// renderParticles.DrawParticles(renderPositions, renderVelocities, null, null);

// // Offsets the body positions for rendering
// renderBodyPositions[FirstSim].x = simulations[FirstSim].body.position.x - offset;
// renderBodyPositions[FirstSim].y = simulations[FirstSim].body.position.y;
// renderBodyPositions[SecondSim].x = simulations[SecondSim].body.position.x + offset;
// renderBodyPositions[SecondSim].y = simulations[SecondSim].body.position.y;
// render.DrawAllCustomParticles(renderBodyPositions);

// private float2[] OffsetBorderParticles(float2[] positions1, float2[] positions2)
// {
//     float2[] result = new float2[positions1.Length + positions2.Length];

//     for (int i = 0; i < result.Length; i++)
//     {
//         if (i < positions1.Length)
//         {
//             result[i].x = positions1[i].x - offset;
//             result[i].y = positions1[i].y;
//         }

//         else
//         {
//             result[i].x = positions2[i - positions1.Length].x + offset;
//             result[i].y = positions2[i - positions1.Length].y;
//         }
//     }

//     return result;
// }
