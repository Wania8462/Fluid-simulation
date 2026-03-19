using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Rendering;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;

#if UNITY_EDITOR
public enum DebugDisplay
{
    SPBox,
    AllNeighbours,
    Velocity,
    Force
}
#endif

public enum RenderType
{
    Particles,
    MarchingSquares
}

namespace SimulationLogic
{
    [Serializable]
    public struct Body
    {
        public float radius;
        public float2 position;
        public float density;
        public int densityResolution;
        public int densityRadius;
        public float upthrustStrength;
        public float friction;

        [HideInInspector] public float2 prevPosition;
        [HideInInspector] public float2 rotation;
        [HideInInspector] public float2 prevRotation;
        [HideInInspector] public float2 velocity;
        [HideInInspector] public float2[] densityPoints;
    }

    [Serializable]
    public class SimulationSettings
    {
        [Header("Simulation settings")]
        public float interactionRadius;
        public float gravity;
        public float mouseAttractiveness;
        public float mouseRadius;
        public float collisionDamping;
        public bool includeBody;
        public bool useParticlesAsBorder;

        [Header("Body settings")]
        public Body body;

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

            body = settings.body;

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

    public class SimulationManager : MonoBehaviour
    {
        [Header("Manager settings")]
        [SerializeField] private bool pause = true;
        [SerializeField] private bool realDeltaTime;
        [SerializeField] private int targetFrameRate;
        [SerializeField] private bool twoSimulations;
        [SerializeField] private RenderType renderType;
        [SerializeField] private int offset;
        [SerializeField] private SimulationSettings[] settings;

#if UNITY_EDITOR
        [Header("Debug settings")]
        [SerializeField] private int trackParticle = -1;
        [SerializeField] private DebugDisplay particleDebugDisplay;
        [SerializeField] private bool bodyDebug;
        [SerializeField] private DebugDisplay bodyDebugDisplay;

        private List<int> greenParticles = new();
        private List<int> yellowParticles = new();
#endif

        [Header("References")]
        [SerializeField] private SpawnParticles spawn;
        [SerializeField] private RenderParticles renderParticles;
        [SerializeField] private RenderMarchingSquares renderSquares;
        [SerializeField] private InputField inputField;

        private const int FirstSim = 0;
        private const int SecondSim = 1;
        private const float fakeDT = 1 / 60f;

        private float dt;

        private Simulation[] simulations;
        private float2[] renderPositions;
        private float2[] renderVelocities;
        private float2[] renderBodyPositions;

        private float2 mousePos;

        private void Start()
        {
            Application.targetFrameRate = targetFrameRate;
            Debug.Log(@"Controls: Pause/resume: space, Restart: R, Attract particles to mouse: left hold ↓
            Move body to mouse: right click
            Select particle to track: W, Deactivate debug tracking: P, All neighbours: A, Velocity: V, Force: F
            Activate/deactivate body debug: Shift + P, All neighbours: A, Velocity: V, Force: F");
            InitSimulationInstances();
            Invoke(nameof(Unpause), 0.5f);
        }

        private void Unpause()
        {
            pause = false;
        }

        private void Update()
        {
            // Allows typing
            if (!inputField.isFocused)
            {
                if (Input.GetKeyDown(KeyCode.R))
                    InitSimulationInstances();

                if (Input.GetKeyDown(KeyCode.Space))
                    pause = !pause;
            }

            if (Input.GetKeyDown(KeyCode.Return))
            {
                var command = inputField.text.Split(' ');
                var field = typeof(SimulationSettings).GetField(command[0]);

                if (field != null)
                {
                    if (!twoSimulations)
                    {
                        field.SetValue(settings, float.Parse(command[1]));
                        simulations[FirstSim].SetSettings(settings[FirstSim]);
                    }

                    else
                    {
                        field.SetValue(settings[1], float.Parse(command[1]));
                        simulations[SecondSim].SetSettings(settings[SecondSim]);
                    }
                }

                else
                    Debug.LogWarning($"No field with name {command[0]} is found");
            }

            if (!pause || Input.GetKeyDown(KeyCode.RightArrow))
            {
                dt = realDeltaTime ? Time.deltaTime : fakeDT;

                if (twoSimulations)
                {
                    foreach (var simulation in simulations)
                    {
                        mousePos = new(Camera.main.ScreenToWorldPoint(Input.mousePosition).x, Camera.main.ScreenToWorldPoint(Input.mousePosition).y);
                        mousePos.x = mousePos.x < 0 ? mousePos.x + offset : mousePos.x - offset;
                        simulation.SimulationStep(mousePos, dt);
                    }
                }

                else
                {
                    mousePos = new(Camera.main.ScreenToWorldPoint(Input.mousePosition).x, Camera.main.ScreenToWorldPoint(Input.mousePosition).y);
                    Watcher.ExecuteWithTimer("1. Step", () => { simulations[FirstSim].SimulationStep(mousePos, dt); });
                    // LogFrameData();
                }
            }

            DrawParticles();
        }

        private void DrawParticles()
        {
            if (twoSimulations)
            {
                // Packs the offseted positions into a single array for rendering along with combining velocities into one array
                Parallel.For(0, simulations[FirstSim]._positions.Length, i =>
                {
                    renderPositions[i].x = simulations[FirstSim]._positions[i].x - offset;
                    renderPositions[i].y = simulations[FirstSim]._positions[i].y;
                    renderVelocities[i] = simulations[FirstSim]._velocities[i];
                });

                Parallel.For(0, simulations[SecondSim]._positions.Length, i =>
                {
                    renderPositions[i + simulations[SecondSim]._positions.Length].x = simulations[SecondSim]._positions[i].x + offset;
                    renderPositions[i + simulations[SecondSim]._positions.Length].y = simulations[SecondSim]._positions[i].y;
                    renderVelocities[i + simulations[SecondSim]._positions.Length] = simulations[SecondSim]._velocities[i];
                });

                renderParticles.DrawParticles(renderPositions, renderVelocities, null, null);

                // Offsets the body positions for rendering
                // renderBodyPositions[FirstSim].x = simulations[FirstSim].body.position.x - offset;
                // renderBodyPositions[FirstSim].y = simulations[FirstSim].body.position.y;
                // renderBodyPositions[SecondSim].x = simulations[SecondSim].body.position.x + offset;
                // renderBodyPositions[SecondSim].y = simulations[SecondSim].body.position.y;
                // render.DrawAllCustomParticles(renderBodyPositions);
            }

            else
            {
#if UNITY_STANDALONE
                if (renderType == RenderType.Particles)
                {
                    renderParticles.DrawParticles(simulations[FirstSim]._positions, simulations[FirstSim]._velocities, null, null);
                    if (settings[FirstSim].includeBody)
                        renderParticles.DrawCustomParticle(simulations[FirstSim].body.position);
                }
#endif
#if UNITY_EDITOR
                if (renderType == RenderType.Particles)
                {
                    greenParticles.Clear();
                    yellowParticles.Clear();
                    HighlghtParticlesForDebug();

                    if (settings[FirstSim].includeBody)
                        HighlighParticlesBodyForDebug();

                    renderParticles.DrawParticles(simulations[FirstSim]._positions, simulations[FirstSim]._velocities, greenParticles.ToArray(), yellowParticles.ToArray());
                }

                else if (renderType == RenderType.MarchingSquares)
                {
                    
                }
#endif
            }
        }

        private void InitSimulationInstances()
        {
            renderParticles.DeleteAllTypesOfParticles();

            Camera.main.orthographicSize = twoSimulations ? offset : Camera.main.orthographicSize;

            if (settings == null || settings.Length == 0)
            {
                Debug.LogError("Simulation manager: There are no settings");
                UnityEditor.EditorApplication.isPlaying = false; // Avoids error spamming
            }

            if (renderType == RenderType.MarchingSquares && twoSimulations)
            {
                Debug.LogError("Simulation manager: Can't run 2 simulations with marching squares");
                UnityEditor.EditorApplication.isPlaying = false;
            }

            if (!twoSimulations)
            {
                simulations = new Simulation[1];
                simulations[FirstSim] = new Simulation(settings[FirstSim], spawn);
            }

            else
            {
                simulations = new Simulation[2];

                if (settings.Length == 1)
                {
                    Array.Resize(ref settings, 2);
                    settings[SecondSim] = new SimulationSettings(settings[FirstSim]);
                }

                simulations[FirstSim] = new Simulation(settings[FirstSim], spawn);
                simulations[SecondSim] = new Simulation(settings[SecondSim], spawn);
            }

            for (var i = 0; i < simulations.Length; i++)
            {
                simulations[i].SetSettings(settings[i]);
                simulations[i].SetScene();

                renderParticles.InitParticles(simulations[i]._positions);
                renderParticles.InitCustomParticle(settings[i].body.position, settings[i].body.radius, Color.antiqueWhite);
            }

            renderSquares.Init(spawn.GetBoundSize());

            // Calculates the total number of particles for 2 simulations. Not used if only 1 simulation is active
            renderPositions = new float2[simulations[FirstSim]._positions.Length * 2];
            renderVelocities = new float2[simulations[FirstSim]._positions.Length * 2];
            if (twoSimulations) renderBodyPositions = new float2[2];
        }

        private void OnValidate()
        {
            if (simulations == null) return;

            Application.targetFrameRate = targetFrameRate;
            for (var i = 0; i < simulations.Length; i++)
                simulations[i].UpdateSettings(settings[i]);
        }

#if UNITY_EDITOR
        private void HighlghtParticlesForDebug()
        {
            if (!Input.GetKey(KeyCode.LeftShift) && Input.GetKeyDown(KeyCode.B))
                particleDebugDisplay = DebugDisplay.SPBox;

            else if (!Input.GetKey(KeyCode.LeftShift) && Input.GetKeyDown(KeyCode.A))
                particleDebugDisplay = DebugDisplay.AllNeighbours;

            else if (!Input.GetKey(KeyCode.LeftShift) && Input.GetKeyDown(KeyCode.V))
                particleDebugDisplay = DebugDisplay.Velocity;

            else if (!Input.GetKey(KeyCode.LeftShift) && Input.GetKeyDown(KeyCode.F))
                particleDebugDisplay = DebugDisplay.Force;

            else if (!Input.GetKey(KeyCode.LeftShift) && Input.GetKeyDown(KeyCode.W))
            {
                var pos = new float2(Camera.main.ScreenToWorldPoint(Input.mousePosition).x, Camera.main.ScreenToWorldPoint(Input.mousePosition).y);
                var neighboursIndices = simulations[FirstSim].GetNeighbourParticles(pos);

                if (neighboursIndices.Length > 0)
                {
                    var neighboursPos = simulations[FirstSim].GetNeighbourParticlesPositions(pos);
                    var magnitudes = neighboursPos.Select(x => FluidMath.Distance(pos, x)).ToArray();
                    trackParticle = neighboursIndices[Array.IndexOf(magnitudes, magnitudes.Min())];
                }
            }

            else if (!Input.GetKey(KeyCode.LeftShift) && Input.GetKeyDown(KeyCode.P))
                trackParticle = -1;

            if (trackParticle != -1)
            {
                if (trackParticle >= simulations[FirstSim]._positions.Length || trackParticle < 0)
                {
                    Debug.LogError($"Simulation manager: tracked particle index is out of range. Number of particles: {simulations[FirstSim]._positions.Length}");
                    return;
                }

                yellowParticles.Add(trackParticle);

                if (particleDebugDisplay == DebugDisplay.SPBox)
                {
                    greenParticles.AddRange(simulations[FirstSim].GetParticlesSPNeighbours(trackParticle));

                    var lineThickness = 0.2f;
                    var SPBox = simulations[FirstSim].GetParticleSPDimentions(trackParticle);

                    renderParticles.DrawLine(SPBox[0], SPBox[1], lineThickness, Color.white);
                    renderParticles.DrawLine(SPBox[0], SPBox[2], lineThickness, Color.white);
                    renderParticles.DrawLine(SPBox[1], SPBox[3], lineThickness, Color.white);
                    renderParticles.DrawLine(SPBox[2], SPBox[3], lineThickness, Color.white);
                }

                if (particleDebugDisplay == DebugDisplay.AllNeighbours)
                {
                    var lineThickness = 0.1f;
                    var neighbours = simulations[FirstSim].GetNeighbourParticles(trackParticle);
                    greenParticles.AddRange(neighbours);

                    var neighbourPoss = new float2[neighbours.Length];
                    for (int i = 0; i < neighbours.Length; i++)
                        neighbourPoss[i] = simulations[FirstSim]._positions[neighbours[i]];

                    renderParticles.DrawLines(simulations[FirstSim]._positions[trackParticle], neighbourPoss, lineThickness);
                }

                else if (particleDebugDisplay == DebugDisplay.Velocity)
                {
                    var lineThickness = 0.5f;
                    var predictedPos = simulations[FirstSim]._positions[trackParticle] + simulations[FirstSim]._velocities[trackParticle];
                    renderParticles.DrawLine(simulations[FirstSim]._positions[trackParticle], predictedPos, lineThickness, Color.white);
                }

                else if (particleDebugDisplay == DebugDisplay.Force)
                {
                    var lineThickness = 0.5f;
                    var force = (simulations[FirstSim]._velocities[trackParticle] - simulations[FirstSim]._prevVelocities[trackParticle]) * 5;
                    var forceEnd = simulations[FirstSim]._positions[trackParticle] + force;
                    renderParticles.DrawLine(simulations[FirstSim]._positions[trackParticle], forceEnd, lineThickness, Color.white);
                }
            }
        }

        private void HighlighParticlesBodyForDebug()
        {
            // if (Input.GetKeyDown(KeyCode.B))
            //     Debug.Log("B");

            // if (Input.GetKey(KeyCode.LeftShift) && Input.GetKeyDown(KeyCode.B))
            //     Debug.Log("Shift + B");

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

            if (bodyDebug)
            {
                if (bodyDebugDisplay == DebugDisplay.SPBox)
                {
                    greenParticles.AddRange(simulations[FirstSim].GetBodySPNeighbours());

                    var lineThickness = 0.2f;
                    var SPBox = simulations[FirstSim].GetBodySPDimentions();

                    renderParticles.DrawLine(SPBox[0], SPBox[1], lineThickness, Color.white);
                    renderParticles.DrawLine(SPBox[0], SPBox[2], lineThickness, Color.white);
                    renderParticles.DrawLine(SPBox[1], SPBox[3], lineThickness, Color.white);
                    renderParticles.DrawLine(SPBox[2], SPBox[3], lineThickness, Color.white);
                }

                else if (bodyDebugDisplay == DebugDisplay.AllNeighbours)
                    greenParticles.AddRange(simulations[FirstSim].GetBodyNeighbours());

                else if (particleDebugDisplay == DebugDisplay.Velocity)
                {
                    var lineThickness = 0.5f;
                    var predictedPos = simulations[FirstSim].body.position + simulations[FirstSim].body.velocity;
                    renderParticles.DrawLine(simulations[FirstSim].body.position, predictedPos, lineThickness, Color.white);
                }

                else if (particleDebugDisplay == DebugDisplay.Force)
                    Debug.Log("Simulation manager: Body force isn't implemented");
            }
        }

        private void LogFrameData()
        {
            // Logs the time taken for each step every 100 frames
            if (Watcher.Count % 100 == 0)
            {
                Debug.Log(Watcher.Log());
                Watcher.Reset();
            }
        }
#endif

        private void OnDestroy()
        {
            renderParticles.DestroyMeshes();
        }

        private float2[] OffsetBorderParticles(float2[] positions1, float2[] positions2)
        {
            float2[] result = new float2[positions1.Length + positions2.Length];

            for (int i = 0; i < result.Length; i++)
            {
                if (i < positions1.Length)
                {
                    result[i].x = positions1[i].x - offset;
                    result[i].y = positions1[i].y;
                }

                else
                {
                    result[i].x = positions2[i - positions1.Length].x + offset;
                    result[i].y = positions2[i - positions1.Length].y;
                }
            }

            return result;
        }
    }
}