using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using UnityEngine;
using System.Xml;

[Serializable]
struct Body
{
    public Vector2 position;
    public float radius;
    public Vector2 prevPosition;
    public Vector2 velocity;
}

public class Simulation : MonoBehaviour
{
    [Header("Simulation settings")]
    [SerializeField] private bool useSprings;
    [SerializeField] private float interactionRadius;
    [SerializeField] private float gravity;
    [SerializeField] private float mouseAttractiveness;

    [Header("Body settings")]
    [SerializeField] private Body body;
    [SerializeField] private float friction;

    [Header("Density")]
    [SerializeField] private float stiffness;
    [SerializeField] private float nearStiffness;
    [SerializeField] private float restDensity;

    [Header("Springs")]
    [SerializeField] private float springStiffness;
    [SerializeField] private float springDeformationLimit;
    [SerializeField] private float plasticity;
    [SerializeField] private float highViscosity;
    [SerializeField] private float lowViscosity;

    [Header("References")]
    [SerializeField] private SpawnParticles spawn;
    [SerializeField] private Render render;

    private Vector2[] _prevPositions;
    private Vector2[] _positions;
    private Vector2[] _velocities;
    private float[] _densities;
    private float[] _nearDensities;
    private ConcurrentDictionary<(int, int), float> _springs = new(); // (i, j), rest length

    private Vector2 realHalfBoundSize;
    private float dt;

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
        dt = Time.deltaTime;
        ExternalForces();
        ApplyViscosity();

        // Advance to predicted position
        Parallel.For(0, _positions.Length, i =>
        {
            _prevPositions[i] = _positions[i];
            _positions[i] += dt * _velocities[i];
        });

        if (useSprings)
        {
            AdjustSprings();
            SpringDisplacements();
        }

        DoubleDensityRelaxation();
        ResolveCollisions();

        AttractToMouse();
        ResolveBoundaries();

        // Change in position to calculate velocity 
        Parallel.For(0, _positions.Length, i =>
        {
            _velocities[i] = (_positions[i] - _prevPositions[i]) / dt;
        });

        // Render
        DrawDebugSquare(Vector3.zero, realHalfBoundSize, Color.red);
        render.UpdatePositions(_positions, _velocities);
    }

    private void ExternalForces()
    {
        Parallel.For(0, _velocities.Length, i =>
        {
            _velocities[i].y += gravity * dt;
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
                float mag = FluidMath.Distance(_positions[i], _positions[j]);
                float q = mag / interactionRadius;

                if (q <= 1 && q != 0)
                {
                    _densities[i] += FluidMath.QuadraticSpikyKernel(q);
                    _nearDensities[i] += FluidMath.CubicSpikyKernel(q);
                }
            }

            float pressure = stiffness * (_densities[i] - restDensity);
            float nearPressure = nearStiffness * _nearDensities[i];
            Vector2 deltaX = new(0, 0);

            for (int j = 0; j < _densities.Length; j++)
            {
                float mag = FluidMath.Distance(_positions[i], _positions[j]);
                float q = mag / interactionRadius;

                if (q <= 1 && q != 0)
                {
                    Vector2 r = (_positions[j] - _positions[i]) / mag;
                    Vector2 displacement = FluidMath.PressureDisplacement(dt, q, pressure, nearPressure, r);
                    
                    _positions[j] += displacement / 2;
                    deltaX -= displacement / 2;
                }
            }

            _positions[i] += deltaX;
        });
    }

    private void ApplyViscosity()
    {
        Parallel.For(0, _positions.Length, i =>
        {
            for (int j = 0; j < _positions.Length; j++)
            {
                if (i < j)
                {
                    float mag = FluidMath.Distance(_positions[i], _positions[j]);
                    float q = mag / interactionRadius;

                    if (q <= 1 && q != 0)
                    {
                        Vector2 r = (_positions[j] - _positions[i]) / mag;
                        float inwardVelocity = Vector2.Dot(_velocities[i] - _velocities[j], r);

                        if (inwardVelocity > 0)
                        {
                            Vector2 impulse = FluidMath.ViscosityImpulse(dt, highViscosity, lowViscosity, q, inwardVelocity, r);

                            _velocities[i] -= impulse / 2;
                            _velocities[j] += impulse / 2;
                        }
                    }
                }
            }
        });
    }

    private void AdjustSprings()
    {
        Parallel.For(0, _positions.Length, i =>
        {
            for (int j = 0; j < _positions.Length; j++)
            {
                if (i < j)
                {
                    float mag = FluidMath.Distance(_positions[i], _positions[j]);
                    float q = mag / interactionRadius;

                    if (q <= 1 && q != 0)
                    {
                        if (!_springs.ContainsKey((i, j)))
                            _springs.TryAdd((i, j), interactionRadius);

                        if (_springs.TryGetValue((i, j), out float restLength))
                        {
                            float deformation = springDeformationLimit * restLength;

                            if (mag > restLength + deformation)
                                _springs[(i, j)] += FluidMath.StretchSpring(dt, plasticity, mag, restLength, deformation);

                            else if (mag < restLength + deformation)
                                _springs[(i, j)] -= FluidMath.CompressSpring(dt, plasticity, mag, restLength, deformation);
                        }

                        else
                            Debug.LogError("Simulation: Couldn't get the spring with key: " + i + ", " + j);
                    }
                }
            }
        });

        (int, int)[] keysToRemove = _springs.Where(kvp => kvp.Value > interactionRadius)
                                            .Select(kvp => kvp.Key)
                                            .ToArray();

        Parallel.For(0, keysToRemove.Length, i =>
        {
            _springs.TryRemove(keysToRemove[i], out _);
        });
    }

    private void SpringDisplacements()
    {
        KeyValuePair<(int, int), float>[] springArray = _springs.ToArray();

        Parallel.For(0, springArray.Length, k =>
        {
            int i = springArray[k].Key.Item1;
            int j = springArray[k].Key.Item2;

            float mag = FluidMath.Distance(_positions[i], _positions[j]);
            Vector2 r = (_positions[j] - _positions[i]) / mag;
            Vector2 displacement = FluidMath.DisplacementBySpring(dt, springStiffness, springArray[k].Value, interactionRadius, mag, r);

            if (displacement.x > 0 || displacement.x < 0 && displacement.y > 0 || displacement.y < 0)
            {
                _positions[i] -= displacement / 2;
                _positions[j] += displacement / 2;
            }
        });
    }

    private void ResolveCollisions()
    {
        body.prevPosition = body.position;
        body.position += body.velocity;

        // Idk about buffers
        Vector2 force = Vector2.zero;

        Parallel.For(0, _velocities.Length, i =>
        {
            float dist = FluidMath.Distance(body.position, _positions[i]);

            if (dist - body.radius - 0.5f > 0) // 0.5 - radius of the paricle
            {
                Vector2 relativeVelocity = _velocities[i] - body.velocity;
                Vector2 unitVector = FluidMath.UnitVector(body.position, _positions[i]);

                Vector2 normalVelocity = Vector2.Dot(relativeVelocity, unitVector) * unitVector;
                Vector2 tangentVelocity = relativeVelocity - normalVelocity;
                Vector2 impulse = normalVelocity - (friction * tangentVelocity);

                _positions[i] -= impulse * dt;
                force += impulse * dt;
            }
        });

        // Not add?
        body.position += force;
        body.velocity += (body.position - body.prevPosition) / dt;

    }

    private void ResolveBoundaries()
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
                Vector2 r = (mousePos - _positions[i]) / FluidMath.Distance(_positions[i], mousePos);
                _positions[i] += dt * mouseAttractiveness * r;
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
        _springs.Clear();

        realHalfBoundSize = spawn.GetRealHalfBoundSize();

        render.DeleteParticles();
        render.CreateParticles(_positions, _velocities);
        render.DeleteAllBodies();
        render.DrawCircle(body.position, body.radius);
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