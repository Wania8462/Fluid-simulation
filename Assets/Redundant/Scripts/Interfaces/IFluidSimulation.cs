using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

public interface IFluidSimulation
{
    void InitializeConstants(
        int numParticles,
        float3 particleSize,
        float mass,
        float gravity,
        float smoothingRadius,
        float collisionDamp,
        float restDensity,
        float stiffness
    );

    void InitializeStartingPoints();

    void CalculateStep(float deltaTime);
    // void StartSimulationStep(float deltaTime);
    // void CalculateExternalForces();
    // void ResolveBoundaries();
    // void UpdatePositions();
    }
