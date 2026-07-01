using System;
using System.Collections.Generic;
using Rendering;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

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
        public bool flow;
        public int maxParticles = -1;
        public bool includeBody;

        [Header("Body settings")]
        public Body body;

        [Header("Density")]
        public float stiffness;
        public float nearStiffness;
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
            flow = settings.flow;
            collisionDamping = settings.collisionDamping;

            body = settings.body;

            stiffness = settings.stiffness;
            nearStiffness = settings.nearStiffness;
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
        [SerializeField] private Text text;
        [Header("Manager settings")]
        [SerializeField] private bool pause = true;
        [SerializeField] private bool realDeltaTime;
        [SerializeField] private int targetFrameRate;
        [SerializeField] private bool twoSimulations;
        // Pretend that it only has public get and don't change outside
        [SerializeField] public SimulationSettings[] settings;
        public bool twoSim { get; private set; }

        [Header("Graph settings")]
        [SerializeField] private bool draw;


        [Header("References")]
        [SerializeField] private InitializeParticles spawn;
        [SerializeField] private RenderDataBuilder render;
        [SerializeField] private InputField inputField;
        [SerializeField] private SimGraph graph;
        [SerializeField] private Text maxVelText;

        private const int FirstSim = 0;
        private const int SecondSim = 1;

        private Simulation[] simulations;

        private float2 mousePos;

        private List<float> buffer = new();

        private float previous = 0;
        private int currStage = 0;
        private bool capturePending = false;
        private readonly List<string> stages = new()
        {
            "Velocity 60fps",
            "Velocity 30fps",
            "Velocity 30fps capped",

            "Displacement 60fps",
            "Displacement 30fps",
            "Displacement 30fps capped",

            "Density 60fps",
            "Density 30fps",
            "Density 30fps capped"
        };
        private int frameTotal = 0;
        private int frames = 0;

        private void Start()
        {
            Debug.Log(Application.persistentDataPath);
            text.text = targetFrameRate.ToString();
            Application.targetFrameRate = targetFrameRate;
            Debug.Log(@"Controls: Pause/resume: space, Restart: R, Attract particles to mouse: left hold ↓
            Move body to mouse: right click
            Select particle to track: W, Deactivate debug tracking: P, All neighbours: A, Velocity: V, Force: F
            Track pair: T, Select one of the particles: scroll wheel, Deactivate: P
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
                {
                    InitSimulationInstances();
                    graph.Reset();
                }

                if (Input.GetKeyDown(KeyCode.Space))
                    pause = !pause;
            }

            HandleFieldInputs();

            if (!pause || Input.GetKeyDown(KeyCode.RightArrow))
            {
                float maxDen = GetMaxDensity(simulations[0]._particles);
                float dt = math.abs(maxDen - previous) > 0.4f ? 1 / 60f : 1 / 40f;
                previous = maxDen;

                if (Camera.main == null)
                    Debug.LogError("SimulationManager: Camera.main is null — cannot convert mouse position to world space");

                else
                {
                    mousePos = new(Camera.main.ScreenToWorldPoint(Input.mousePosition).x, Camera.main.ScreenToWorldPoint(Input.mousePosition).y);
                    Watcher.ExecuteWithTimer("1. Step", () => { simulations[FirstSim].SimulationStep(mousePos, dt); });
                    LogFrameData();
                }
            }

            if (draw)
                render.Draw();
        }

        private void InitSimulationInstances()
        {
            if (twoSim)
            {
                Debug.LogError("Simulation manager: 2 simulations aren't supported");
#if UNITY_EDITOR
                EditorApplication.isPlaying = false;
#endif
                return;
            }

            if (settings == null || settings.Length == 0)
            {
                Debug.LogError("Simulation manager: There are no settings");
#if UNITY_EDITOR
                EditorApplication.isPlaying = false; // Avoids error spamming
#endif
                return;
            }

            if (!twoSim)
            {
                simulations = new Simulation[1];
                simulations[FirstSim] = new Simulation(settings[FirstSim], spawn, render.renderManager); // DO NOT PUSH THIS INTO MAIN
            }

            else
            {
                simulations = new Simulation[2];

                if (settings.Length == 1)
                {
                    Array.Resize(ref settings, 2);
                    settings[SecondSim] = new SimulationSettings(settings[FirstSim]);
                }

                simulations[FirstSim] = new Simulation(settings[FirstSim], spawn, render.renderManager); // DO NOT PUSH THIS INTO MAIN
                simulations[SecondSim] = new Simulation(settings[SecondSim], spawn, render.renderManager); // DO NOT PUSH THIS INTO MAIN
            }

            for (var i = 0; i < simulations.Length; i++)
            {
                simulations[i].SetSettings(settings[i]);
                simulations[i].SetScene();
            }

            render.Init(simulations[FirstSim]);
        }

        private void HandleFieldInputs()
        {
            if (Input.GetKeyDown(KeyCode.Return))
            {
                var command = inputField.text.Split(' ');

                if (command.Length < 2)
                {
                    Debug.LogWarning("SimulationManager: command must have the form '<fieldName> <value>'");
                }
                else
                {
                    var field = typeof(SimulationSettings).GetField(command[0]);

                    if (field != null)
                    {
                        if (!float.TryParse(command[1], out var value))
                        {
                            Debug.LogWarning($"SimulationManager: could not parse '{command[1]}' as a float");
                        }
                        else if (!twoSim)
                        {
                            field.SetValue(settings[FirstSim], value);
                            simulations[FirstSim].UpdateSettings(settings[FirstSim]);
                        }
                        else
                        {
                            field.SetValue(settings[SecondSim], value);
                            simulations[SecondSim].UpdateSettings(settings[SecondSim]);
                        }
                    }
                    else
                        Debug.LogWarning($"SimulationManager: no field with name '{command[0]}' found on SimulationSettings");
                }
            }
        }

        private void OnValidate()
        {
            twoSim = twoSimulations;
            if (simulations == null) return;

            Application.targetFrameRate = targetFrameRate;
            for (var i = 0; i < simulations.Length; i++)
                simulations[i].UpdateSettings(settings[i]);
        }

        #region Log
        private void LogFrameData()
        {
            if (Watcher.Count % 100 == 0)
            {
                // Debug.Log(Watcher.Log());
                // Watcher.Reset();
                frameTotal += (int)Watcher.GetTotal();
                frames++;
                Debug.Log(frameTotal / frames);
            }
        }

        private float HandleGraphs()
        {
            if (graph.GetCurrentTime() > 40)
            {
                if (currStage == stages.Count - 1)
                    EditorApplication.isPlaying = false;

                else if (!capturePending)
                {
                    capturePending = true;
                    StartCoroutine(CaptureAndAdvance());
                }
            }

            FluidParticle[] particles = simulations[0]._particles;
            float maxVel = GetMaxVelocity(particles);

            float dt = stages[currStage].Contains("60") ? 1 / 60f : 1 / 30f;
            dt = stages[currStage].Contains("capped") ? Mathf.Min(1 / maxVel, dt) : dt;

            if (stages[currStage].Contains("Velocity"))
            {
                graph.AddPoint(maxVel, dt, 0);
                graph.AddPoint(GetMeanVelocity(particles), dt, 1);
                graph.AddPoint(GetMedianVelocity(particles), dt, 2);
            }

            else if (stages[currStage].Contains("Displacement"))
            {
                graph.AddPoint(GetMaxDisplacement(particles), dt, 0);
                graph.AddPoint(GetMeanDisplacement(particles), dt, 1);
                graph.AddPoint(GetMedianDisplacement(particles), dt, 2);
            }

            else if (stages[currStage].Contains("Density"))
            {
                graph.AddPoint(GetMaxDensity(particles), dt, 0);
                graph.AddPoint(GetMeanDensity(particles), dt, 1);
                graph.AddPoint(GetMedianDensity(particles), dt, 2);
            }

            return dt;
        }

        private System.Collections.IEnumerator CaptureAndAdvance()
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Application.persistentDataPath + $"/{stages[currStage]}.png", 2);
            yield return new WaitForEndOfFrame();
            currStage++;
            InitSimulationInstances();

            if (stages[currStage].Contains("Displacement"))
            {
                graph.yZoom = 20;
                graph.yMax = 20;
            }

            graph.Reset();
            capturePending = false;
        }

        #region Velcoities
        private float GetMaxVelocity(FluidParticle[] particles)
        {
            float maximum = 0;
            foreach (var particle in particles)
            {
                float mag = FluidMath.Magnitude(particle.velocity);
                if (mag > maximum) maximum = mag;
            }

            return maximum;
        }

        private float GetMeanVelocity(FluidParticle[] particles)
        {
            float total = 0;
            foreach (var particle in particles)
            {
                float mag = FluidMath.Magnitude(particle.velocity);
                total += mag;
            }

            return total / particles.Length;
        }

        private float GetMedianVelocity(FluidParticle[] particles)
        {
            buffer.Clear();
            foreach (var particle in particles)
            {
                float mag = FluidMath.Magnitude(particle.velocity);
                buffer.Add(mag);
            }

            return buffer[particles.Length / 2];
        }

        private float GetStandardDeviationVelocity(FluidParticle[] particles)
        {
            float sum = 0;
            float sumSqares = 0;
            foreach (var particle in particles)
            {
                float mag = FluidMath.Magnitude(particle.velocity);
                sum += mag;
                sumSqares += mag * mag;
            }

            int n = particles.Length;
            float mean = sum / n;
            return Mathf.Sqrt(sumSqares / n - (mean * mean));
        }
        #endregion

        #region Displacements
        private float GetMaxDisplacement(FluidParticle[] particles)
        {
            float maximum = 0;
            foreach (var particle in particles)
            {
                float displacement = FluidMath.Distance(particle.position, particle.prevPosition);
                if (displacement > maximum) maximum = displacement;
            }

            return maximum;
        }

        private float GetMeanDisplacement(FluidParticle[] particles)
        {
            float total = 0;
            foreach (var particle in particles)
            {
                float displacement = FluidMath.Distance(particle.position, particle.prevPosition);
                total += displacement;
            }

            return total / particles.Length;
        }

        private float GetMedianDisplacement(FluidParticle[] particles)
        {
            buffer.Clear();
            foreach (var particle in particles)
            {
                float displacement = FluidMath.Distance(particle.position, particle.prevPosition);
                buffer.Add(displacement);
            }

            return buffer[particles.Length / 2];
        }

        private float GetStandardDeviationDisplacement(FluidParticle[] particles)
        {
            float sum = 0;
            float sumSquares = 0;
            foreach (var particle in particles)
            {
                float displacement = FluidMath.Distance(particle.position, particle.prevPosition);
                sum += displacement;
                sumSquares += displacement * displacement;
            }

            int n = particles.Length;
            float mean = sum / n;
            return Mathf.Sqrt(sumSquares / n - (mean * mean));
        }
        #endregion

        #region Densities
        private float GetMaxDensity(FluidParticle[] particles)
        {
            float maximum = 0;
            foreach (var particle in particles)
            {
                if (particle.density > maximum)
                    maximum = particle.density;
            }

            return maximum;
        }

        private float GetMeanDensity(FluidParticle[] particles)
        {
            float total = 0;
            foreach (var particle in particles)
            {
                total += particle.density;
            }

            return total / particles.Length;
        }

        private float GetMedianDensity(FluidParticle[] particles)
        {
            buffer.Clear();
            foreach (var particle in particles)
            {
                buffer.Add(particle.density);
            }

            return buffer[particles.Length / 2];
        }

        private float GetStandardDeviationDensity(FluidParticle[] particles)
        {
            float sum = 0;
            float sumSquares = 0;
            foreach (var particle in particles)
            {
                sum += particle.density;
                sumSquares += particle.density * particle.density;
            }

            int n = particles.Length;
            float mean = sum / n;
            return Mathf.Sqrt(sumSquares / n - (mean * mean));
        }
        #endregion
        #endregion
    }
}