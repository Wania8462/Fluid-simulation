using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public class Simulation : MonoBehaviour
{
    [Header("Simulation settings")]
    [SerializeField] private bool useSprings;
    [SerializeField] private float interactionRadius;
    [SerializeField] private float gravity;
    [SerializeField] private float mouseAttractiveness;

    [Header("Density")]
    [SerializeField] private float stiffness;
    [SerializeField] private float nearStiffness;
    [SerializeField] private float restDensity;

    [Header("Springs")]
    [SerializeField] private float springStiffness;
    [SerializeField] private float springRestLength;
    [SerializeField] private float springDeformation;
    [SerializeField] private float plasticity;

    [Header("References")]
    [SerializeField] private SpawnParticles spawn;
    [SerializeField] private Render render;

    private Vector2[] _prevPositions;
    private Vector2[] _positions;
    private Vector2[] _velocities;
    private float[] _densities;
    private float[] _nearDensities;
    private Dictionary<(int, int), float> springs = new(); // (i, j), rest length

    private Vector2 realHalfBoundSize;
    private float deltaTime;

    void Start()
    {
        Application.targetFrameRate = 60;
        SetScene();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
            SetScene();

        SimulationStep();
    }

    private void SimulationStep()
    {
        deltaTime = Time.deltaTime;
        ExternalForces();
        // Todo: Apply viscocity

        // Advance to predicted position
        Parallel.For(0, _positions.Length, i =>
        {
            _prevPositions[i] = _positions[i];
            _positions[i] += deltaTime * _velocities[i];
        });

        // AdjustSprings();
        // SpringDisplacements();

        DoubleDensityRelaxation();
        AttractToMouse();
        ResolveCollisions();

        // Change in position to calculate velocity 
        Parallel.For(0, _positions.Length, i =>
        {
            _velocities[i] = (_positions[i] - _prevPositions[i]) / deltaTime;
        });

        // Render
        DrawDebugSquare(Vector3.zero, realHalfBoundSize, Color.red);
        render.UpdatePositions(_positions, _velocities);
    }

    private void ExternalForces()
    {
        Parallel.For(0, _velocities.Length, i =>
        {
            _velocities[i].y += gravity * deltaTime;
        });
    }

    private void DoubleDensityRelaxation()
    {
        Parallel.For(0, _densities.Length, i =>
        {
            _densities[i] = 0;
            _nearDensities[i] = 0;

            for (int j = 0; j < _densities.Length; j++)
            {
                float relativeDist = FluidMath.Distance(_positions[i], _positions[j]) / interactionRadius;

                if (relativeDist <= 1 && relativeDist != 0)
                {
                    _densities[i] += Mathf.Pow(1 - relativeDist, 2);
                    _nearDensities[i] += Mathf.Pow(1 - relativeDist, 3);
                }
            }

            float pseudoPressure = stiffness * (_densities[i] - restDensity);
            float nearPseudoPressure = nearStiffness * _nearDensities[i];
            Vector2 deltaX = new(0, 0);

            for (int j = 0; j < _densities.Length; j++)
            {
                float magnitude = FluidMath.Distance(_positions[i], _positions[j]);
                float relativeDist = magnitude / interactionRadius;

                if (relativeDist <= 1 && relativeDist != 0)
                {
                    Vector2 unitVector = (_positions[j] - _positions[i]) / magnitude;
                    Vector2 displacement = deltaTime * deltaTime * (pseudoPressure * (1 - relativeDist) + nearPseudoPressure * Mathf.Pow(1 - relativeDist, 2)) * unitVector;
                    _positions[j] += displacement / 2;
                    deltaX -= displacement / 2;
                }
            }

            _positions[i] += deltaX;
        });
    }

    private void ApplyViscosity()
    {

    }

    private void AdjustSprings()
    {
        Parallel.For(0, _positions.Length, i =>
        {
            for (int j = 0; j < _positions.Length; j++)
            {
                float relativeDist = FluidMath.Distance(_positions[i], _positions[j]) / interactionRadius;

                if (relativeDist <= 1 && relativeDist != 0)
                {
                    if (!springs.ContainsKey((i, j)))
                        springs.Add((i, j), interactionRadius);

                    float deformation = springDeformation * springs[(i, j)];
                    float magnitude = FluidMath.Distance(_positions[i], _positions[j]);

                    if (magnitude > springs[(i, j)] + deformation)
                        springs[(i, j)] += deltaTime * plasticity * (magnitude - springs[(i, j)] - deformation);

                    else if (magnitude < springs[(i, j)] + deformation)
                        springs[(i, j)] -= deltaTime * plasticity * (springs[(i, j)] - deformation - magnitude);
                }
            }
        });

        Parallel.For(0, _positions.Length, i =>
        {
            if (i < springs.Count())
            {
                if (springs.ElementAt(i).Value > interactionRadius)
                    springs.Remove(springs.ElementAt(i).Key);
            }
        });
    }

    private void SpringDisplacements()
    {
        Parallel.For(0, _positions.Length, i =>
        {
            for (int j = 0; j < _positions.Length; j++)
            {
                float magnitude = FluidMath.Distance(_positions[i], _positions[j]);

                if (magnitude != 0)
                {
                    Vector2 unitVector = (_positions[j] - _positions[i]) / magnitude;
                    Vector2 displacement = deltaTime * deltaTime * springStiffness * (1 - (springRestLength / interactionRadius)) * (springRestLength - magnitude) * unitVector;
                    _positions[i] -= displacement / 2;
                    _positions[j] += displacement / 2;
                }
            }
        });
    }

    private void ResolveCollisions()
    {
        // Todo: add collision damping
        Parallel.For(0, _positions.Length, i =>
        {
            if (Math.Abs(_positions[i].x) >= realHalfBoundSize.x)
                _positions[i].x = realHalfBoundSize.x * Math.Sign(_positions[i].x);

            if (Math.Abs(_positions[i].y) >= realHalfBoundSize.y)
                _positions[i].y = realHalfBoundSize.y * Math.Sign(_positions[i].y);
        });
    }

    private void AttractToMouse()
    {
        if (Input.GetMouseButton(0))
        {
            for (int i = 0; i < _positions.Length; i++)
            {
                Vector2 mousePos = (Vector2)Camera.main.ScreenToWorldPoint(Input.mousePosition);
                Vector2 unitVector = (mousePos - _positions[i]) / FluidMath.Distance(_positions[i], mousePos);
                _positions[i] += deltaTime * mouseAttractiveness * unitVector;
            }
        }
    }

    private void SetScene()
    {
        _positions = spawn.InitializePositions();
        _prevPositions = spawn.InitializePreviousPositions();
        _velocities = spawn.InitializeVelocities();

        _densities = spawn.InitializeDensities();
        _nearDensities = spawn.InitializeNearDensities();

        realHalfBoundSize = spawn.GetRealHalfBoundSize();

        render.DeleteParticles();
        render.CreateParticles(_positions, _velocities);
    }

    private void DrawDebugSquare(Vector3 center, Vector2 halfSize, Color color)
    {
        Vector3 p0 = center + new Vector3(-halfSize.x, -halfSize.y, 0f);
        Vector3 p1 = center + new Vector3(halfSize.x, -halfSize.y, 0f);
        Vector3 p2 = center + new Vector3(halfSize.x, halfSize.y, 0f);
        Vector3 p3 = center + new Vector3(-halfSize.x, halfSize.y, 0f);

        Debug.DrawLine(p0, p1, color);
        Debug.DrawLine(p1, p2, color);
        Debug.DrawLine(p2, p3, color);
        Debug.DrawLine(p3, p0, color);
    }
}