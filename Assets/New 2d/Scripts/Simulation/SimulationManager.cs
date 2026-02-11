using System;
using Rendering;
using UnityEngine;

namespace SimulationLogic
{
    // [Serializable]
    // public struct Body
    // {
    //     public float radius;
    //     public Vector2 position;
    //     public float density;
    //     public int densityResolution;
    //     public int densityRadius;
    //     private float friction;
    //     
    //     [HideInInspector] public Vector2 prevPosition;
    //     [HideInInspector] public Vector2 rotation;
    //     [HideInInspector] public Vector2 prevRotation;
    //     [HideInInspector] public Vector2 velocity;
    //     [HideInInspector] public Vector2[] densityPoints;
    // }
    
    [Serializable]
    public struct SimulationSettings
    {
        [Header("Simulation settings")] 
        [SerializeField] private float particleSize;
        [SerializeField] private float interactionRadius;
        [SerializeField] private float gravity;
        [SerializeField] private float mouseAttractiveness;
        [SerializeField] private float mouseRadius;

        [Header("Body settings")]
        [SerializeField] private Body body;

        [Header("Density")] 
        [SerializeField] private float stiffness;
        [SerializeField] private float nearStiffness;
        [SerializeField] private float restDensity;

        [Header("Springs")] 
        [SerializeField] private float springInteractionRadius;
        [SerializeField] private float springRadius;
        [SerializeField] private float springStiffness;
        [SerializeField] private float springDeformationLimit;
        [SerializeField] private float plasticity;
        [SerializeField] private float highViscosity;
        [SerializeField] private float lowViscosity;
    }
    
    public class SimulationManager : MonoBehaviour
    {
        [SerializeField] private bool pause;
        
        private SimulationSettings settings;
        
        [Header("References")] 
        [SerializeField] private SpawnParticles spawn;
        [SerializeField] private Render render;

        private void Start()
        {
            Application.targetFrameRate = 120;
        }
    }
}
