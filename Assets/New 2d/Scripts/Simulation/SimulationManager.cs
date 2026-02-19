using System;
using System.Threading.Tasks;
using log4net.Layout;
using Rendering;
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
        private float friction;

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
        [SerializeField] private bool twoSimulations;
        [SerializeField] private int offset;
        [SerializeField] private SimulationSettings[] settings;

        [Header("References")]
        [SerializeField] private SpawnParticles spawn;
        [SerializeField] private Render render;
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
            Application.targetFrameRate = 60;
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
                        simulations[FirstSim].SettingsParser(settings[FirstSim]);
                    }

                    else
                    {
                        field.SetValue(settings[1], float.Parse(command[1]));
                        simulations[SecondSim].SettingsParser(settings[SecondSim]);
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

                    // Logs the time taken for each step every 100 frames
                    if (Watcher.Count % 100 == 0)
                    {
                        Debug.Log(Watcher.Log());
                        Watcher.Reset();
                    }
                }
            }

            DrawParticles();
        }

        private void OnValidate()
        {
            if (simulations == null) return;

            for (var i = 0; i < simulations.Length; i++)
                simulations[i].SettingsParser(settings[i]);

            render.DeleteBorderParticles();
            render.InitBorderParticles(OffsetBorderParticles(simulations[FirstSim]._borderPositions, simulations[SecondSim]._borderPositions));
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

                render.DrawParticles(renderPositions, renderVelocities);
                render.DrawBorderParticles();

                // Offsets the body positions for rendering
                // renderBodyPositions[FirstSim].x = simulations[FirstSim].body.position.x - offset;
                // renderBodyPositions[FirstSim].y = simulations[FirstSim].body.position.y;
                // renderBodyPositions[SecondSim].x = simulations[SecondSim].body.position.x + offset;
                // renderBodyPositions[SecondSim].y = simulations[SecondSim].body.position.y;
                // render.DrawAllCustomParticles(renderBodyPositions);
            }

            else
            {
                render.DrawParticles(simulations[FirstSim]._positions, simulations[FirstSim]._velocities);
                render.DrawBorderParticles();
                // render.DrawCustomParticle(simulations[FirstSim].body.position);
            }
        }

        private void InitSimulationInstances()
        {
            render.DeleteAllTypesOfParticles();

            if (settings == null || settings.Length == 0)
            {
                Debug.LogError("Simulation manager: There are no settings");
                Application.Quit(); // Avoids error spamming
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
                simulations[i].SettingsParser(settings[i]);
                simulations[i].SetScene();

                render.InitParticles(simulations[i]._positions);
                render.InitCustomParticle(settings[i].body.position, settings[i].body.radius, Color.magenta);
            }

            if (!twoSimulations)
                render.InitBorderParticles(simulations[FirstSim]._borderPositions);

            else
                render.InitBorderParticles(OffsetBorderParticles(simulations[FirstSim]._borderPositions, simulations[SecondSim]._borderPositions));

            // Calculates the total number of particles for 2 simulations. Not used if only 1 simulation is active
            renderPositions = new float2[simulations[FirstSim]._positions.Length * 2];
            renderVelocities = new float2[simulations[FirstSim]._positions.Length * 2];
            if (twoSimulations) renderBodyPositions = new float2[2];
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