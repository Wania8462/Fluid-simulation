using System;
using System.Threading.Tasks;
using Rendering;
using UnityEngine;
using UnityEngine.UI;
using System.Reflection;

namespace SimulationLogic
{
    [Serializable]
    public struct Body
    {
        public float radius;
        public Vector2 position;
        public float density;
        public int densityResolution;
        public int densityRadius;
        private float friction;
        
        [HideInInspector] public Vector2 prevPosition;
        [HideInInspector] public Vector2 rotation;
        [HideInInspector] public Vector2 prevRotation;
        [HideInInspector] public Vector2 velocity;
        [HideInInspector] public Vector2[] densityPoints;
    }
    
    [Serializable]
    public class SimulationSettings
    {
        [Header("Simulation settings")] 
        public float particleSize;
        public float interactionRadius;
        public float gravity;
        public float mouseAttractiveness;
        public float mouseRadius;

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
    }
    
    public class SimulationManager : MonoBehaviour
    {
        [Header("Manager settings")]
        [SerializeField] private bool pause = true;
        [SerializeField] private bool twoSimulations;
        [SerializeField] private int offset;
        [SerializeField] private SimulationSettings settings;
        private SimulationSettings secondSettings;
        
        [Header("References")] 
        [SerializeField] private SpawnParticles spawn;
        [SerializeField] private Render render;
        [SerializeField] private InputField inputField;
        
        private Simulation[] simulations;
        private Vector2[] renderPositions;
        private Vector2[] renderVelocities;
        
        private Vector2 mousePos;

        private void Start()
        {
            Application.targetFrameRate = 120;
            SetScene();
            Invoke(nameof(Unpause), 0.5f);
        }

        private void Unpause()
        {
            pause = false;   
        }

        private void Update()
        {
            if (!inputField.isFocused)
            {
                if (Input.GetKeyDown(KeyCode.R))
                    SetScene();

                if (Input.GetKeyDown(KeyCode.Space))
                    pause = !pause;
            }

            if (Input.GetKeyDown(KeyCode.Return))
            {
                if (!twoSimulations) return;
                var command = inputField.text.Split(' ');
                var field = typeof(SimulationSettings).GetField(command[0]);

                if (field != null)
                {
                    if (!twoSimulations)
                    {
                        field.SetValue(settings, float.Parse(command[1]));
                        simulations[0].SettingsParser(settings);
                    }

                    else
                    {
                        field.SetValue(secondSettings, float.Parse(command[1]));
                        simulations[0].SettingsParser(secondSettings);
                    }
                }

                else
                {
                    Debug.LogWarning($"No field with name {command[0]} is found");
                }
            }

            if (!pause && !Input.GetKeyDown(KeyCode.RightArrow))
            {
                foreach (var simulation in simulations)
                {
                    mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
                    mousePos.x = mousePos.x < 0 ? mousePos.x + offset :  mousePos.x -  offset;
                    simulation.SimulationStep(mousePos);
                }
            }
            
            RenderParticles();
        }

        private void RenderParticles()
        {
            if (twoSimulations)
            {
                Parallel.For(0, simulations[0]._positions.Length, i =>
                {
                    renderPositions[i].x = simulations[0]._positions[i].x - offset;
                    renderPositions[i].y = simulations[0]._positions[i].y;
                    renderVelocities[i] = simulations[0]._velocities[i];
                });
                
                Parallel.For(0, simulations[1]._positions.Length, i =>
                {
                    renderPositions[i + simulations[1]._positions.Length].x = simulations[0]._positions[i].x + offset;
                    renderPositions[i + simulations[1]._positions.Length].y = simulations[0]._positions[i].y;
                    renderVelocities[i + simulations[1]._positions.Length] = simulations[1]._velocities[i];
                });
                
                render.DrawParticles(renderPositions, renderVelocities);
            }

            else
                render.DrawParticles(simulations[0]._positions, simulations[0]._velocities);
        }

        private void SetScene()
        {
            Camera.main.orthographicSize = offset;
            render.DeleteParticles();
            
            if (!twoSimulations)
            {
                simulations = new Simulation[1];
                simulations[0] = new Simulation(settings, spawn);
            }
            
            else
            {
                secondSettings = settings;
                simulations = new Simulation[2];
                simulations[0] = new Simulation(settings, spawn);
                simulations[1] = new Simulation(secondSettings, spawn);
            }
            
            foreach (var simulation in simulations)
            {
                simulation.SettingsParser(settings);
                simulation.SetScene();
                
                render.InitializeParticles(simulation._positions, settings.particleSize);
            }

            renderPositions = new Vector2[simulations[0]._positions.Length * 2];
            renderVelocities = new Vector2[simulations[0]._positions.Length * 2];
        }
    }
}