using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace SimulationLogic
{
    public class Simulation
    {
        // General settings
        private bool pause;
        private float particleSize;
        private float interactionRadius;
        private float gravity;
        private float mouseAttractiveness;
        private float mouseRadius;

        // Bodies
        private Body body;
        private float friction;

        // Density
        private float stiffness;
        private float nearStiffness;
        private float restDensity;

        // Springs
        private float springInteractionRadius;
        private float springRadius;
        private float springStiffness;
        private float springDeformationLimit;
        private float plasticity;
        private float highViscosity;
        private float lowViscosity;

        // References
        private SpawnParticles spawn;

        // Spatial partitioning grids
        private SpatialPartitioning particleSP;
        private SpatialPartitioning springsSP;
        private SpatialPartitioning bodySP;

        // Buffers
        public Vector2[] _positions;
        public Vector2[] _velocities;
        private Vector2[] _prevPositions;
        private float[] _densities;
        private float[] _nearDensities;
        private ConcurrentDictionary<(int, int), float> _springs = new(); // (i, j), rest length

        // Spatial partitioning buffers
        private List<int>[] neighbours;
        private List<int>[] springsNeighbours;
        private List<int> bodyNeighbours;

        // Miscellaneous
        private Vector2 realHalfBoundSize;
        private Vector2 realHalfBoundSizeBody;
        private float dt;

        // Debug
        private List<int> debugParticles = new();

        public Simulation(SimulationSettings settings, SpawnParticles spawn)
        {
            SettingsParser(settings);
            this.spawn = spawn;
        }

        public void SimulationStep(Vector2 mousePos)
        {
            dt = Time.deltaTime;
            // dt = 1 / 60f;

            Watcher.ExecuteWithTimer("2. Init", () =>
            {
                particleSP.Init(_positions);
                bodySP.Init(_positions);
                springsSP.Init(_positions);
            });

            Watcher.ExecuteWithTimer("3. GetNeighbours", () =>
            {
                Parallel.For(0, neighbours.Length, i => { particleSP.GetNeighbours(_positions[i], neighbours[i]); });
                Parallel.For(0, springsNeighbours.Length, i => { springsSP.GetNeighbours(_positions[i], springsNeighbours[i]); });
                bodySP.GetNeighbours(body.position, bodyNeighbours);
            });

            Watcher.ExecuteWithTimer("4. ExternalForces", ExternalForces);
            Watcher.ExecuteWithTimer("5. ApplyViscosity", ApplyViscosity);

            // Advance to predicted position
            Parallel.For(0, _positions.Length, i =>
            {
                _prevPositions[i] = _positions[i];
                _positions[i] += dt * _velocities[i];
            });

            Watcher.ExecuteWithTimer("6. Springs", () =>
            {
                AdjustSprings();
                SpringDisplacements();
            });

            Watcher.ExecuteWithTimer("7. DoubleDensityRelaxation", DoubleDensityRelaxation);
            Watcher.ExecuteWithTimer("8. Resolve collisions", ResolveCollisions);

            AttractToMouse(mousePos);
            Watcher.ExecuteWithTimer("9. Resolve boundaries", ResolveBoundaries);

            Parallel.For(0, _positions.Length, i => { _velocities[i] = (_positions[i] - _prevPositions[i]) / dt; });
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

                foreach (var j in neighbours[i])
                {
                    var mag = FluidMath.Distance(_positions[i], _positions[j]);
                    var q = mag / interactionRadius;
                    if (!(q <= 1) || q == 0) continue;

                    _densities[i] += FluidMath.QuadraticSpikyKernel(q);
                    _nearDensities[i] += FluidMath.CubicSpikyKernel(q);
                }

                var pressure = stiffness * (_densities[i] - restDensity);
                var nearPressure = nearStiffness * _nearDensities[i];
                Vector2 deltaX = new(0, 0);

                foreach (var j in neighbours[i])
                {
                    var mag = FluidMath.Distance(_positions[i], _positions[j]);
                    var q = mag / interactionRadius;
                    if (!(q <= 1) || q == 0) continue;

                    var r = (_positions[j] - _positions[i]) / mag;
                    var displacement = FluidMath.PressureDisplacement(
                        dt,
                        q,
                        pressure,
                        nearPressure,
                        r);

                    _positions[j] += displacement / 2;
                    deltaX -= displacement / 2;
                }

                _positions[i] += deltaX;
            });
        }

        public void ApplyViscosity()
        {
            Parallel.For(0, _positions.Length, i =>
            {
                foreach (var j in neighbours[i])
                {
                    if (i >= j) continue;
                    var mag = FluidMath.Distance(_positions[i], _positions[j]);
                    if (mag > springRadius || mag == 0) continue;

                    var q = mag / springRadius;

                    var r = (_positions[j] - _positions[i]) / mag;
                    var inwardVelocity = Vector2.Dot(_velocities[i] - _velocities[j], r);
                    if (!(inwardVelocity > 0)) continue;

                    var impulse = FluidMath.ViscosityImpulse(dt,
                        highViscosity,
                        lowViscosity,
                        q,
                        inwardVelocity,
                        r);

                    _velocities[i] -= impulse / 2;
                    _velocities[j] += impulse / 2;
                }
            });
        }

        #region Springs

        public void AdjustSprings()
        {
            var keysToRemove = new ConcurrentBag<(int, int)>();
            Parallel.For(0, _positions.Length, i =>
            {
                foreach (var j in springsNeighbours[i])
                {
                    if (i >= j) continue;

                    var mag = FluidMath.Distance(_positions[i], _positions[j]);
                    var q = mag / springInteractionRadius;
                    switch (q)
                    {
                        case 0:
                            continue;
                        case > 1:
                            keysToRemove.Add((i, j));
                            continue;
                    }

                    if (!_springs.ContainsKey((i, j)))
                        _springs.TryAdd((i, j), springRadius);

                    if (_springs.TryGetValue((i, j), out var restLength))
                    {
                        var deformation = springDeformationLimit * restLength;

                        if (mag > restLength + deformation)
                            _springs[(i, j)] += FluidMath.StretchSpring(dt,
                                plasticity,
                                mag,
                                restLength,
                                deformation);

                        else if (mag < restLength + deformation)
                            _springs[(i, j)] -= FluidMath.CompressSpring(dt,
                                plasticity,
                                mag,
                                restLength,
                                deformation);
                    }

                    else
                        Debug.LogError($"Simulation: Couldn't get the spring with key: {i}, {j}");
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

        public void ResolveCollisions()
        {
            body.prevPosition = body.position;
            body.position += dt * body.velocity;
            body.velocity.y += dt * -2;

            var force = Vector2.zero;
            var collisionRadiusSq = (body.radius + particleSize) * (body.radius + particleSize);
            var minDistance = body.radius + particleSize;

            // Parallel.For(0, body.densityPoints.Length, i =>
            // {
            //     
            // });
            debugParticles.Clear();

            Parallel.ForEach(bodyNeighbours, n =>
            {
                var distSq = FluidMath.DistanceSq(body.position, _positions[n]);
                if (!(distSq < collisionRadiusSq)) return;
                debugParticles.Add(n);

                var relativeVelocity = _velocities[n] - body.velocity;
                var normalVector = FluidMath.UnitVector(body.position,
                    _positions[n],
                    Mathf.Sqrt(distSq));
                var normalVelocity = Vector2.Dot(relativeVelocity, normalVector) * normalVector;

                force += dt * normalVelocity;
            });

            body.velocity += force;
            body.position += dt * body.velocity;

            Parallel.ForEach(bodyNeighbours, n =>
            {
                var distSq = FluidMath.DistanceSq(body.position, _positions[n]);
                if (!(distSq < collisionRadiusSq)) return;

                var relativeVelocity = _velocities[n] - body.velocity;
                var normalVector = FluidMath.UnitVector(body.position,
                    _positions[n],
                    Mathf.Sqrt(distSq));
                var normalVelocity = Vector2.Dot(relativeVelocity, normalVector) * normalVector;

                _positions[n] -= dt * normalVelocity;

                distSq = FluidMath.DistanceSq(body.position, _positions[n]);
                if (!(distSq < collisionRadiusSq)) return;

                var unitVector = FluidMath.UnitVector(body.position,
                    _positions[n],
                    MathF.Sqrt(distSq));
                _positions[n] = body.position + unitVector * minDistance;
            });
        }

        public void ResolveBoundaries()
        {
            // Particles
            Parallel.For(0, _positions.Length, i =>
            {
                if (Math.Abs(_positions[i].x) >= realHalfBoundSize.x)
                {
                    _positions[i].x = realHalfBoundSize.x * Math.Sign(_positions[i].x);
                    // _velocities[i].x = 0;
                }

                if (Math.Abs(_positions[i].y) >= realHalfBoundSize.y)
                {
                    _positions[i].y = realHalfBoundSize.y * Math.Sign(_positions[i].y);
                    // _velocities[i].y = 0;
                }
            });

            // Bodies
            if (Math.Abs(body.position.x) >= realHalfBoundSizeBody.x)
            {
                body.position.x = realHalfBoundSizeBody.x * Math.Sign(body.position.x);
                body.velocity.x = 0;
            }

            if (Math.Abs(body.position.y) >= realHalfBoundSizeBody.y)
            {
                body.position.y = realHalfBoundSizeBody.y * Math.Sign(body.position.y);
                body.velocity.y = 0;
            }
        }

        #region Michelsons

        public void AttractToMouse(Vector2 mousePos)
        {
            if (Input.GetMouseButton(0))
            {
                Parallel.For(0, _positions.Length, i =>
                {
                    var dist = FluidMath.Distance(_positions[i], mousePos);
                    if (dist > mouseRadius) return;

                    var unitVector = FluidMath.UnitVector(_positions[i], mousePos, dist);
                    _positions[i] += dt * mouseAttractiveness * unitVector;

                });
            }

            if (Input.GetMouseButton(1))
            {
                body.position = mousePos;
                body.velocity = Vector2.zero;
            }
        }

        public void SetScene()
        {
            // Buffers
            _positions = spawn.InitializePositions();
            _prevPositions = spawn.InitializePreviousPositions();
            _velocities = spawn.InitializeVelocities();
            _densities = spawn.InitializeDensities();
            _nearDensities = spawn.InitializeNearDensities();
            _springs.Clear();

            // Precomputing values
            realHalfBoundSize = spawn.GetRealHalfBoundSize(particleSize);
            realHalfBoundSizeBody = spawn.GetRealHalfBoundSize(body.radius);

            // Spatial partitioning initialization
            particleSP = new SpatialPartitioning(-realHalfBoundSize, realHalfBoundSize, interactionRadius);
            springsSP = new SpatialPartitioning(-realHalfBoundSize, realHalfBoundSize, springInteractionRadius);
            bodySP = new SpatialPartitioning(-realHalfBoundSize, realHalfBoundSize, body.radius + particleSize);

            neighbours = new List<int>[_positions.Length];
            Parallel.For(0, neighbours.Length, i => { neighbours[i] = new List<int>(); });
            springsNeighbours = new List<int>[_positions.Length];
            Parallel.For(0, springsNeighbours.Length, i => { springsNeighbours[i] = new List<int>(); });
            bodyNeighbours = new List<int>();

            // In development
            body.densityPoints = new Vector2[body.densityResolution];
        }

        public void SettingsParser(SimulationSettings settings)
        {
            particleSize = settings.particleSize;
            interactionRadius = settings.interactionRadius;
            gravity = settings.gravity;
            mouseAttractiveness = settings.mouseAttractiveness;
            mouseRadius = settings.mouseRadius;
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

        public void DrawDebugGrid(Color color, SpatialPartitioning sp)
        {
            for (var i = 0; i <= sp.columns; i++)
            {
                Vector3 start = new(sp.offset.x + (sp.length * i), -sp.offset.y, 0);
                Vector3 end = new(sp.offset.x + (sp.length * i), sp.offset.y, 0);
                Debug.DrawLine(start, end, color);
            }

            for (var i = 0; i <= sp.rows; i++)
            {
                Vector3 start = new(-sp.offset.x, sp.offset.y + (sp.length * i), 0);
                Vector3 end = new(sp.offset.x, sp.offset.y + (sp.length * i), 0);
                Debug.DrawLine(start, end, color);
            }
        }

        #endregion
    }
}