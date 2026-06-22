using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Rendering;
using Unity.Mathematics;
using UnityEngine;

public enum DebugDisplay
{
    SPBox,
    AllNeighbours,
    Pair,
    Velocity,
    Force
}

namespace SimulationLogic
{
    public class RenderDataBuilder : MonoBehaviour
    {
        [Header("Render settings")]
        [SerializeField] private int Offset;
        public int offset { get; private set; }

        [Header("References")]
        [SerializeField] private SimulationManager manager;
        [SerializeField] private InitializeParticles spawn;
        [SerializeField] private RenderManager renderManager;

        [Header("Debug settings")]
        [SerializeField] private int trackParticle = -1;
        [SerializeField] private int trackPair1 = -1, trackPair2 = -1;
        [SerializeField] private DebugDisplay particleDebugDisplay;
        [SerializeField] private bool bodyDebug;
        [SerializeField] private DebugDisplay bodyDebugDisplay;

        private Simulation simulation;

        private float2[] renderPositions;
        private float2[] renderVelocities;
        // private float2[] renderBodyPositions;

        private float[] densitiesSquares;
        private float[] densitiesMap;

        // Debug buffers
        private List<int> greenParticles = new();
        private List<int> yellowParticles = new();

        // WARNING: Needs to be called after Simulation's SetScene
        public void Init(Simulation sim)
        {
            if (CheckDependencies(sim))
                return;

            simulation = sim;
            (int, int) numSamplePoints = renderManager.InitAll(sim.maxParticles, spawn.GetBoundSize());

            renderPositions = new float2[sim.maxParticles];
            renderVelocities = new float2[sim.maxParticles];
            densitiesSquares = new float[numSamplePoints.Item1];
            densitiesMap = new float[numSamplePoints.Item2];

            if (CheckSimSettings() && simulation.includeBody)
                    renderManager.InitBody(manager.settings[0].body.position, manager.settings[0].body.radius, Color.antiqueWhite);

            else
                Debug.LogWarning("RenderDataBuilder: cannot init body particle — manager settings are missing");

            if (simulation.useParticlesAsBorder)
                renderManager.InitBorderParticles(sim._borderParticles.ForEach(p => p.position));
        }

        public void Draw()
        {
            if (simulation == null)
            {
                Debug.LogError("RenderDataBuilder: Draw called before Init — simulation is null");
                return;
            }

            if (simulation.count == 0)
            {
                Debug.LogWarning("Render data builder: drawing 0 particles");
                return;
            }

            if (renderManager.renderType == RenderType.Particles)
                DrawParticles();

            else if (renderManager.renderType == RenderType.MarchingSquares)
                DrawMarchingSquares();

            else if (renderManager.renderType == RenderType.DensityMap)
                DrawDensityMap();
        }

        #region Particles
        private void DrawParticles()
        {
            HandleKeyInputs();
            greenParticles.Clear();
            yellowParticles.Clear();
            HighlghtParticlesForDebug();

            if (manager != null && manager.settings != null && manager.settings.Length > 0 && manager.settings[0].includeBody)
                HighlighParticlesBodyForDebug();

            if (simulation.maxParticles != renderPositions.Length)
            {
                if (simulation.maxParticles > renderPositions.Length)
                    renderManager.InitParticles(simulation.maxParticles - renderPositions.Length);

                renderPositions = new float2[simulation.maxParticles];
                renderVelocities = new float2[simulation.maxParticles];
            }

            SetPositions(simulation._particles.AsSpan(0, simulation.count));
            SetVelocities(simulation._particles.AsSpan(0, simulation.count));

            renderManager.DrawParticles(
                renderPositions,
                renderVelocities,
                simulation.count,
                greenParticles,
                yellowParticles);

            if (simulation.includeBody)
                renderManager.DrawCustomParticle(simulation.body.position);

            if (simulation.useParticlesAsBorder)
                renderManager.DrawBorderParticles();
        }

        private void HighlghtParticlesForDebug()
        {
            if (trackParticle != -1)
            {
                if (trackParticle >= simulation.count || trackParticle < 0)
                {
                    Debug.LogError($"Render data builder: tracked particle index is out of range. Track particle: {trackParticle}, number of particles: {simulation.count}");
                    return;
                }

                HighlightSingle();
            }

            else if (trackPair1 != -1 ^ trackPair2 != -1)
            {
                var tracked = trackPair1 == -1 ? trackPair2 : trackPair1;

                if (tracked >= simulation.count || tracked < 0)
                {
                    Debug.LogError($"Render data builder: tracked particle is out of range. Track particle: {tracked}, number of particles: {simulation.count}");
                    return;
                }

                yellowParticles.Add(simulation._sparse[tracked]);
            }
            
            else if (trackPair1 != -1 && trackPair2 != -1)
            {
                if (trackPair1 >= simulation.count || trackPair1 < 0)
                {
                    Debug.LogError($"Render data builder: particle 1 from the track pair is out of range. Track particle: {trackPair1}, number of particles: {simulation.count}");
                    return;
                }

                if (trackPair1 >= simulation.count || trackPair2 < 0)
                {
                    Debug.LogError($"Render data builder: particle 1 from the track pair is out of range. Track particle: {trackPair2}, number of particles: {simulation.count}");
                    return;
                }

                if (particleDebugDisplay != DebugDisplay.Pair)
                    return;

                HighlightPair();
            }
        }

        private void HighlightSingle()
        {
            yellowParticles.Add(simulation._sparse[trackParticle]);

            if (particleDebugDisplay == DebugDisplay.SPBox)
            {
                foreach (var id in simulation.GetParticlesSPNeighbours(trackParticle))
                    greenParticles.Add(simulation._sparse[id]);

                var lineThickness = 0.2f;
                var SPBox = simulation.GetParticleSPDimentions(trackParticle);

                renderManager.DrawLine(SPBox[0], SPBox[1], lineThickness, Color.white);
                renderManager.DrawLine(SPBox[0], SPBox[2], lineThickness, Color.white);
                renderManager.DrawLine(SPBox[1], SPBox[3], lineThickness, Color.white);
                renderManager.DrawLine(SPBox[2], SPBox[3], lineThickness, Color.white);
            }

            else if (particleDebugDisplay == DebugDisplay.AllNeighbours)
            {
                var trackPosition = simulation._particles[simulation._sparse[trackParticle]].position;
                var lineThickness = 0.1f;
                var neighbours = simulation.GetNeighbourParticles(trackParticle);
                foreach (var id in neighbours)
                    greenParticles.Add(simulation._sparse[id]);

                var neighbourPoss = new float2[neighbours.Length];

                for (int i = 0; i < neighbours.Length; i++)
                    neighbourPoss[i] = simulation._particles[simulation._sparse[neighbours[i]]].position;

                renderManager.DrawLines(trackPosition, neighbourPoss, lineThickness, Color.white);
            }

            else if (particleDebugDisplay == DebugDisplay.Velocity)
            {
                var trackPosition = simulation._particles[simulation._sparse[trackParticle]].position;
                var trackVelocity = simulation._particles[simulation._sparse[trackParticle]].velocity;
                var lineThickness = 0.5f;
                var predictedPos = trackPosition + trackVelocity;
                renderManager.DrawLine(trackPosition, predictedPos, lineThickness, Color.white);
            }

            else if (particleDebugDisplay == DebugDisplay.Force)
            {
                Debug.LogError("Render data builder: force isn't supported now");
                // var trackPosition = simulation._particles[simulation._sparse[trackParticle]].position;
                // var lineThickness = 0.5f;
                // var force = (simulation._velocities[trackParticle] - simulation._prevVelocities[trackParticle]) * 5;
                // var forceEnd = trackPosition + force;
                // renderManager.DrawLine(trackPosition, forceEnd, lineThickness, Color.white);
            }
        }

        private void HighlightPair()
        {
            yellowParticles.Add(simulation._sparse[trackPair1]);
            greenParticles.Add(simulation._sparse[trackPair2]);
            var firstPos = simulation._particles[simulation._sparse[trackPair1]].position;
            var secondPos = simulation._particles[simulation._sparse[trackPair2]].position;
            renderManager.DrawLine(firstPos, secondPos, width: 0.1f, Color.white);
        }

        private void HighlighParticlesBodyForDebug()
        {
            if (!bodyDebug) return;

            if (bodyDebugDisplay == DebugDisplay.SPBox)
            {
                foreach (var id in simulation.GetBodySPNeighbours())
                    greenParticles.Add(simulation._sparse[id]);

                var lineThickness = 0.2f;
                var SPBox = simulation.GetBodySPDimentions();

                renderManager.DrawLine(SPBox[0], SPBox[1], lineThickness, Color.white);
                renderManager.DrawLine(SPBox[0], SPBox[2], lineThickness, Color.white);
                renderManager.DrawLine(SPBox[1], SPBox[3], lineThickness, Color.white);
                renderManager.DrawLine(SPBox[2], SPBox[3], lineThickness, Color.white);
            }

            else if (bodyDebugDisplay == DebugDisplay.AllNeighbours)
            {
                foreach (var id in simulation.GetBodyNeighbours())
                    greenParticles.Add(simulation._sparse[id]);
            }

            else if (bodyDebugDisplay == DebugDisplay.Velocity)
            {
                var lineThickness = 0.5f;
                var predictedPos = simulation.body.position + simulation.body.velocity;
                renderManager.DrawLine(simulation.body.position, predictedPos, lineThickness, Color.white);
            }

            else if (bodyDebugDisplay == DebugDisplay.Force)
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

            else if (Input.GetKeyDown(KeyCode.T))
                particleDebugDisplay = DebugDisplay.Pair;

            else if (Input.GetKeyDown(KeyCode.V))
                particleDebugDisplay = DebugDisplay.Velocity;

            else if (Input.GetKeyDown(KeyCode.F))
                particleDebugDisplay = DebugDisplay.Force;

            else if (Input.GetKeyDown(KeyCode.W))
                trackParticle = GetClosestParticleToMouse();

            else if (Input.GetMouseButtonDown(2))
            {
                if (trackPair1 == -1)
                    trackPair1 = GetClosestParticleToMouse();

                else
                    trackPair2 = GetClosestParticleToMouse();
            }

            else if (Input.GetKeyDown(KeyCode.P))
            {
                trackParticle = -1;
                trackPair1 = -1;
                trackPair2 = -1;
            }

            if (particleDebugDisplay == DebugDisplay.Pair)
                trackParticle = -1;

            else
            {
                trackPair1 = -1;
                trackPair2 = -1;
            }
        }

        private void HandleBodyInputs()
        {
            if (Input.GetKeyDown(KeyCode.P))
                bodyDebug = !bodyDebug;

            else if (Input.GetKeyDown(KeyCode.B))
                bodyDebugDisplay = DebugDisplay.SPBox;

            else if (Input.GetKeyDown(KeyCode.A))
                bodyDebugDisplay = DebugDisplay.AllNeighbours;

            else if (Input.GetKeyDown(KeyCode.V))
                bodyDebugDisplay = DebugDisplay.Velocity;

            else if (Input.GetKeyDown(KeyCode.F))
                bodyDebugDisplay = DebugDisplay.Force;
        }
        #endregion

        private void DrawMarchingSquares()
        {
            Vector3[] edges = renderManager.GetSquaresEdges();
            Parallel.For(0, densitiesSquares.Length, i =>
            {
                densitiesSquares[i] = simulation.GetDensity(edges[i]);
            });

            renderManager.DrawMarchingSquares(densitiesSquares);
        }

        private void DrawDensityMap()
        {
            int batchSize = 100;
            int threadCount = (int)math.ceil((float)densitiesMap.Length / batchSize);
            float2[] cells = renderManager.GetCells();

            Parallel.For(0, threadCount, threadIndex =>
            {
                int start = threadIndex * batchSize;
                int end = Math.Min(start + batchSize, densitiesMap.Length);

                for (int i = start; i < end; i++)
                {
                    densitiesMap[i] = simulation.GetDensity(cells[i]);
                }
            });

            renderManager.DrawDensityMap(densitiesMap);
        }

        public void DrawSquare(float2 topLeft, float2 bottomRight)
        {
            renderManager.DrawRect(topLeft, bottomRight, 50, Color.white);
        }

        private int GetClosestParticleToMouse()
        {
            if (Camera.main == null)
            {
                Debug.LogError("RenderDataBuilder: Camera.main is null — cannot select particle by mouse position");
                return -1;
            }

            var pos = new float2(Camera.main.ScreenToWorldPoint(Input.mousePosition).x, Camera.main.ScreenToWorldPoint(Input.mousePosition).y);
            var neighboursIndices = simulation.GetNeighbourParticles(pos);

            if (neighboursIndices.Length > 0)
            {
                var neighboursPos = simulation.GetNeighbourParticlesPositions(pos);
                var magnitudes = neighboursPos.Select(x => FluidMath.Distance(pos, x)).ToArray();
                return neighboursIndices[Array.IndexOf(magnitudes, magnitudes.Min())];
            }

            else
                return -1;
        }

        private void SetPositions(Span<Particle> particles)
        {
            if (particles.Length > renderPositions.Length)
            {
                Debug.LogWarning($"RenderDataBuilder: particles span ({particles.Length}) exceeds renderPositions buffer ({renderPositions.Length}), clamping");
                particles = particles[..renderPositions.Length];
            }

            for (int i = 0; i < particles.Length; i++)
                renderPositions[i] = particles[i].position;
        }

        private void SetVelocities(Span<Particle> particles)
        {
            if (particles.Length > renderVelocities.Length)
            {
                Debug.LogWarning($"RenderDataBuilder: particles span ({particles.Length}) exceeds renderVelocities buffer ({renderVelocities.Length}), clamping");
                particles = particles[..renderVelocities.Length];
            }

            for (int i = 0; i < particles.Length; i++)
                renderVelocities[i] = particles[i].velocity;
        }

        private bool CheckDependencies(Simulation sim)
        {
            if (sim == null)
            {
                Debug.LogError("RenderDataBuilder: Init called with null simulation");
                return true;
            }

            if (manager == null) Debug.LogError("RenderDataBuilder: manager reference is not assigned");
            if (spawn == null) Debug.LogError("RenderDataBuilder: spawn reference is not assigned");
            if (renderManager == null) Debug.LogError("RenderDataBuilder: renderManager reference is not assigned");

            if (Camera.main == null)
                Debug.LogError("RenderDataBuilder: Camera.main is null — orthographic size will not be set");
            else
                Camera.main.orthographicSize = manager.twoSim ? offset : Camera.main.orthographicSize;

            return false;
        }

        private bool CheckSimSettings() => !(manager == null || manager.settings == null || manager.settings.Length <= 0);
    }
}