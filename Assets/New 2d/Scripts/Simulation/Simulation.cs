using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Rendering;
using UnityEngine.UI;

namespace SimulationLogic
{
    [Serializable]
    struct Body
    {
        public float radius;
        public Vector2 position;
        public Vector2 prevPosition;
        public Vector2 rotation;
        public Vector2 prevRotation;
        public Vector2 velocity;
    }

    public class Simulation : MonoBehaviour
    {
        [Header("Simulation settings")]
        
        [SerializeField] private bool pause;
        [SerializeField] private float particleSize;
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
        
        [SerializeField] private float springInteractionRadius;
        [SerializeField] private float springRadius;
        [SerializeField] private float springStiffness;
        [SerializeField] private float springDeformationLimit;
        [SerializeField] private float plasticity;
        [SerializeField] private float highViscosity;
        [SerializeField] private float lowViscosity;

        [Header("References")] 
        
        [SerializeField] private SpawnParticles spawn;
        [SerializeField] private Render render;
        
        private SpatialPartitioning particleSP;
        private SpatialPartitioning bodySP;

        private Vector2[] _prevPositions;
        private Vector2[] _positions;
        private Vector2[] _velocities;
        private float[] _densities;
        private float[] _nearDensities;
        private ConcurrentDictionary<(int, int), float> _springs = new(); // (i, j), rest length

        private Vector2 realHalfBoundSize;
        private Vector2 realHalfBoundSizeBody;
        private float dt;
        private List<int>[] neighbours;

        private bool initialFramePassed;

        private void Start()
        {
            Application.targetFrameRate = 120;
            Invoke(nameof(StartSimulation), 0.5f);
        }

        private void StartSimulation()
        {
            SetScene();
            initialFramePassed = true;
        }

        private void Update()
        {
            if (!initialFramePassed) return;
            
            if (Input.GetKeyDown(KeyCode.R))
                SetScene();

            if (Input.GetKeyDown(KeyCode.RightArrow))
                SimulationStep();

            if (Input.GetKeyDown(KeyCode.Space))
                pause = !pause;

            if (!pause)
                SimulationStep();
            
        }

        public void SimulationStep()
        {
            // dt = Time.deltaTime;
            dt = 1 / 60f;
            particleSP.Init(_positions);

            var watch = System.Diagnostics.Stopwatch.StartNew();
            Parallel.For(0, neighbours.Length, i =>
            {
                particleSP.GetNeighbours(_positions[i],  neighbours[i]);
            });
            watch.Stop();
            if (watch.ElapsedMilliseconds > 5)
                Debug.Log(watch.ElapsedMilliseconds);
            
            ExternalForces();
            ApplyViscosity();

            // Advance to predicted position
            Parallel.For(0, _positions.Length, i =>
            {
                _prevPositions[i] = _positions[i];
                _positions[i] += dt * _velocities[i];
            });

            AdjustSprings();
            SpringDisplacements();

            DoubleDensityRelaxation();
            // ResolveCollisions();

            AttractToMouse();
            ResolveBoundaries();

            // Change in position to calculate velocity 
            Parallel.For(0, _positions.Length, i =>
            {
                _velocities[i] = (_positions[i] - _prevPositions[i]) / dt;
            });

            // Render
            DrawDebugGrid(Color.green);
            DrawDebugSquare(Vector3.zero, realHalfBoundSize, Color.red);

            render.DrawParticles(_positions, _velocities);
            // render.UpdateBodyPosition(body.position, 0);
        }

        public void ExternalForces()
        {
            Parallel.For(0, _positions.Length, i => { _velocities[i].y += dt * gravity; });
        }

        public void DoubleDensityRelaxation()
        {
            Parallel.For(0, _positions.Length, i =>
            {
                _densities[i] = 0;
                _nearDensities[i] = 0;

                for (var j = 0; j < neighbours[i].Count; j++)
                {
                    var mag = FluidMath.Distance(_positions[i], _positions[neighbours[i][j]]);
                    var q = mag / interactionRadius;
                    if (!(q <= 1) || q == 0) continue;

                    _densities[i] += FluidMath.QuadraticSpikyKernel(q);
                    _nearDensities[i] += FluidMath.CubicSpikyKernel(q);
                }

                var pressure = stiffness * (_densities[i] - restDensity);
                var nearPressure = nearStiffness * _nearDensities[i];
                Vector2 deltaX = new(0, 0);

                for (var j = 0; j < neighbours[i].Count; j++)
                {
                    var mag = FluidMath.Distance(_positions[i], _positions[neighbours[i][j]]);
                    var q = mag / interactionRadius;
                    if (!(q <= 1) || q == 0) continue;

                    var r = (_positions[neighbours[i][j]] - _positions[i]) / mag;
                    var displacement = FluidMath.PressureDisplacement(
                        dt,
                        q,
                        pressure,
                        nearPressure,
                        r);

                    _positions[neighbours[i][j]] += displacement / 2;
                    deltaX -= displacement / 2;
                }

                _positions[i] += deltaX;
            });
        }

        #region Springs

        public void ApplyViscosity()
        {
            Parallel.For(0, _positions.Length, i =>
            {
                for(var j = 0; j < neighbours[i].Count; j++)
                {
                    if (i >= neighbours[i][j]) continue;
                    var mag = FluidMath.Distance(_positions[i], _positions[neighbours[i][j]]);
                    if (mag > springRadius || mag == 0) continue;

                    var q = mag / springRadius;

                    var r = (_positions[neighbours[i][j]] - _positions[i]) / mag;
                    var inwardVelocity = Vector2.Dot(_velocities[i] - _velocities[neighbours[i][j]], r);
                    if (!(inwardVelocity > 0)) continue;

                    var impulse = FluidMath.ViscosityImpulse(dt, 
                        highViscosity, 
                        lowViscosity, 
                        q, 
                        inwardVelocity, 
                        r);

                    _velocities[i] -= impulse / 2;
                    _velocities[neighbours[i][j]] += impulse / 2;
                }
            });
        }

        public void AdjustSprings()
        {
            var keysToRemove = new ConcurrentBag<(int, int)>();
            Parallel.For(0, _positions.Length, i =>
            {
                for(var j = 0; j < neighbours[i].Count; j++)
                {
                    if (i >= neighbours[i][j]) continue;

                    var mag = FluidMath.Distance(_positions[i], _positions[neighbours[i][j]]);
                    var q = mag / springInteractionRadius;
                    switch (q)
                    {
                        case 0:
                            continue;
                        case > 1:
                            keysToRemove.Add((i, neighbours[i][j]));
                            continue;
                    }

                    if (!_springs.ContainsKey((i, neighbours[i][j])))
                        _springs.TryAdd((i, neighbours[i][j]), springRadius);

                    if (_springs.TryGetValue((i, neighbours[i][j]), out var restLength))
                    {
                        var deformation = springDeformationLimit * restLength;

                        if (mag > restLength + deformation)
                            _springs[(i, neighbours[i][j])] += FluidMath.StretchSpring(dt, 
                                plasticity, 
                                mag, 
                                restLength, 
                                deformation);

                        else if (mag < restLength + deformation)
                            _springs[(i, neighbours[i][j])] -= FluidMath.CompressSpring(dt, 
                                plasticity, 
                                mag, 
                                restLength, 
                                deformation);
                    }

                    else
                        Debug.LogError($"Simulation: Couldn't get the spring with key: {i}, {neighbours[i][j]}");
                }
            });
            
            Parallel.ForEach(keysToRemove, i => { _springs.TryRemove(i, out _); });
        }

        public void SpringDisplacements()
        {
            var springArray = _springs.ToArray();

            Parallel.For(0, springArray.Length, k =>
            {
                var i = springArray[k].Key.Item1;
                var j = springArray[k].Key.Item2;

                var mag = FluidMath.Distance(_positions[i], _positions[j]);
                if (mag == 0) return;

                var r = FluidMath.UnitVector(_positions[i], _positions[j], mag);
                var displacement = FluidMath.DisplacementBySpring(dt, 
                    springStiffness, 
                    springArray[k].Value,
                    springRadius, 
                    mag, 
                    r);

                _positions[i] -= displacement / 2;
                _positions[j] += displacement / 2;
            });
        }

        #endregion

        // var tangentVelocity = relativeVelocity - normalVelocity;
        // var impulse = normalVelocity - (friction * tangentVelocity);
        
        public void ResolveCollisions()
        {
            body.prevPosition = body.position;
            body.position += dt * body.velocity;

            body.velocity.y += dt * gravity;

            var force = Vector2.zero;
            var collisionRadiusSq = (body.radius + particleSize) * (body.radius + particleSize);
            var minDistance = body.radius + particleSize;

            Parallel.For(0, _positions.Length, i =>
            {
                var distSq = FluidMath.DistanceSq(body.position, _positions[i]);
                if (!(distSq < collisionRadiusSq)) return;

                var relativeVelocity = _velocities[i] - body.velocity;
                var normalVector = FluidMath.UnitVector(body.position, 
                    _positions[i], 
                    Mathf.Sqrt(distSq));
                var normalVelocity = Vector2.Dot(relativeVelocity, normalVector) * normalVector;

                force += dt * normalVelocity;
            });

            body.velocity += force;
            body.position += dt * body.velocity;

            Parallel.For(0, _positions.Length, i =>
            {
                var distSq = FluidMath.DistanceSq(body.position, _positions[i]);
                if (!(distSq < collisionRadiusSq)) return;

                var relativeVelocity = _velocities[i] - body.velocity;
                var normalVector = FluidMath.UnitVector(body.position, 
                    _positions[i], 
                    Mathf.Sqrt(distSq));
                var normalVelocity = Vector2.Dot(relativeVelocity, normalVector) * normalVector;

                _positions[i] -= dt * normalVelocity;

                distSq = FluidMath.DistanceSq(body.position, _positions[i]);
                if (!(distSq < collisionRadiusSq)) return;

                var unitVector = FluidMath.UnitVector(body.position, 
                    _positions[i], 
                    MathF.Sqrt(distSq));
                _positions[i] = body.position + unitVector * minDistance;
            });
        }

        public void ResolveBoundaries()
        {
            // Particles
            Parallel.For(0, _positions.Length, i =>
            {
                if (Math.Abs(_positions[i].x) >= realHalfBoundSize.x)
                    _positions[i].x = realHalfBoundSize.x * Math.Sign(_positions[i].x);

                if (Math.Abs(_positions[i].y) >= realHalfBoundSize.y)
                    _positions[i].y = realHalfBoundSize.y * Math.Sign(_positions[i].y);
            });

            // Bodies
            if (Math.Abs(body.position.x) >= realHalfBoundSizeBody.x)
                body.position.x = realHalfBoundSizeBody.x * Math.Sign(body.position.x);

            if (Math.Abs(body.position.y) >= realHalfBoundSizeBody.y)
                body.position.y = realHalfBoundSizeBody.y * Math.Sign(body.position.y);
        }

        #region Michelsons

        public void AttractToMouse()
        {
            if (Input.GetMouseButton(0))
            {
                var mousePos = (Vector2)Camera.main.ScreenToWorldPoint(Input.mousePosition);

                Parallel.For(0, _positions.Length, i =>
                {
                    var unitVector = FluidMath.UnitVector(_positions[i], mousePos);
                    _positions[i] += dt * mouseAttractiveness * unitVector;
                });
            }

            if (Input.GetMouseButton(1))
            {
                var mousePos = (Vector2)Camera.main.ScreenToWorldPoint(Input.mousePosition);
                body.position = mousePos;
                body.velocity = Vector2.zero;
            }
        }

        public void SetScene()
        {
            _positions = spawn.InitializePositions();
            _prevPositions = spawn.InitializePreviousPositions();
            _velocities = spawn.InitializeVelocities();

            _densities = spawn.InitializeDensities();
            _nearDensities = spawn.InitializeNearDensities();
            _springs.Clear();

            realHalfBoundSize = spawn.GetRealHalfBoundSize(particleSize);
            realHalfBoundSizeBody = spawn.GetRealHalfBoundSize(body.radius);

            particleSP = new SpatialPartitioning(-realHalfBoundSize, realHalfBoundSize, interactionRadius);
            bodySP = new SpatialPartitioning(-realHalfBoundSize, realHalfBoundSize, body.radius + particleSize);
            neighbours = new List<int>[_positions.Length];
            Parallel.For(0, neighbours.Length, i => { neighbours[i] = new List<int>(); });

            render.DeleteParticles();
            render.CreateParticles(_positions, _velocities, particleSize);
            // render.DeleteAllBodies();
            // render.DrawCircle(body.position, body.radius);
        }

        public void DrawDebugSquare(Vector3 center, Vector2 halfSize, Color color)
        {
            var p0 = center + new Vector3(-halfSize.x, -halfSize.y, 0f);
            var p1 = center + new Vector3(halfSize.x, -halfSize.y, 0f);
            var p2 = center + new Vector3(halfSize.x, halfSize.y, 0f);
            var p3 = center + new Vector3(-halfSize.x, halfSize.y, 0f);

            Debug.DrawLine(p0, p1, color);
            Debug.DrawLine(p1, p2, color);
            Debug.DrawLine(p2, p3, color);
            Debug.DrawLine(p3, p0, color);
        }

        public void DrawDebugGrid(Color color)
        {
            for (var i = 0; i <= particleSP.columns; i++)
            {
                Vector3 start = new(particleSP.offset.x + (particleSP.length * i), -particleSP.offset.y, 0);
                Vector3 end = new(particleSP.offset.x + (particleSP.length * i), particleSP.offset.y, 0);
                Debug.DrawLine(start, end, color);
            }

            for (var i = 0; i <= particleSP.rows; i++)
            {
                Vector3 start = new(-particleSP.offset.x, particleSP.offset.y + (particleSP.length * i), 0);
                Vector3 end = new(particleSP.offset.x, particleSP.offset.y + (particleSP.length * i), 0);
                Debug.DrawLine(start, end, color);
            }
        }

        #endregion
    }
}