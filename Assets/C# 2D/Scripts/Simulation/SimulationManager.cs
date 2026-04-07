using System;
using Unity.Mathematics;
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
            flow = settings.flow;
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
        public bool flow;
        [SerializeField] private bool twoSimulations;
        // Pretend that it only has public get and don't change outside
        [SerializeField] public SimulationSettings[] settings;
        public bool twoSim { get; private set; }

        [Header("References")]
        [SerializeField] private SpawnParticles spawn;
        [SerializeField] private RenderDataBuilder render;
        [SerializeField] private InputField inputField;

        private const int FirstSim = 0;
        private const int SecondSim = 1;
        private const float fakeDT = 1 / 60f;

        private Simulation[] simulations;

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

            if (!pause || Input.GetKeyDown(KeyCode.RightArrow))
            {
                var dt = realDeltaTime ? Time.deltaTime : fakeDT;

                if (Camera.main == null)
                    Debug.LogError("SimulationManager: Camera.main is null — cannot convert mouse position to world space");

                else if (twoSim)
                {
                    foreach (var simulation in simulations)
                    {
                        mousePos = new(Camera.main.ScreenToWorldPoint(Input.mousePosition).x, Camera.main.ScreenToWorldPoint(Input.mousePosition).y);
                        mousePos.x = mousePos.x < 0 ? mousePos.x + render.offset : mousePos.x - render.offset;
                        simulation.SimulationStep(mousePos, dt);
                    }
                }
                else
                {
                    mousePos = new(Camera.main.ScreenToWorldPoint(Input.mousePosition).x, Camera.main.ScreenToWorldPoint(Input.mousePosition).y);
                    Watcher.ExecuteWithTimer("1. Step", () => { simulations[FirstSim].SimulationStep(mousePos, dt); });
                    LogFrameData();
                }
            }

            render.Draw();
        }

        private void InitSimulationInstances()
        {
            if (twoSim)
            {
                Debug.LogError("Simulation manager: 2 simulations aren't supported");
                UnityEditor.EditorApplication.isPlaying = false;
            }

            if (settings == null || settings.Length == 0)
            {
                Debug.LogError("Simulation manager: There are no settings");
                UnityEditor.EditorApplication.isPlaying = false; // Avoids error spamming
            }

            if (!twoSim)
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
            }

            render.Init(simulations[FirstSim]);
        }

        private void OnValidate()
        {
            twoSim = twoSimulations;
            if (simulations == null) return;

            Application.targetFrameRate = targetFrameRate;
            for (var i = 0; i < simulations.Length; i++)
                simulations[i].UpdateSettings(settings[i]);
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
    }
}