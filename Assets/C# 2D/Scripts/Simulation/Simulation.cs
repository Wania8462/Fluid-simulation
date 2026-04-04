using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.Mathematics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace SimulationLogic
{
    public class Simulation
    {
        // General settings
        private float interactionRadius;
        private float gravity;
        private float mouseAttractiveness;
        private float mouseRadius;
        private float collisionDamp;
        public bool flow { get; private set; }
        private float spawnInterval;
        private bool useParticlesAsBorder;
        private bool includeBody;

        // Bodies
        public Body body; // maybe change to prop later
        private float friction;

        // Density
        private float stiffness;
        private float nearStiffness;
        private float borderStiffness;
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
        // Fluid particles
        private int maxParticles;
        public FlexibleArray<float2> _positions { get; private set; }
        public FlexibleArray<float2> _velocities { get; private set; }
        private FlexibleArray<float2> _forceBuffers;
        private FlexibleArray<float2> _prevPositions;
#if UNITY_EDITOR
        public FlexibleArray<float2> _prevVelocities { get; private set; }
#endif
        private FlexibleArray<float> _densities;
        private FlexibleArray<float> _nearDensities;

        private ConcurrentDictionary<(int, int), float> _springs = new(); // (i, j), rest length
        private List<(int, int)> springKeysToRemap = new();

        // Border particles
        // public float2[] _borderPositions { get; private set; }
        // private FlexibleArray<float> _borderDensities;

        // Spatial partitioning buffers
        private FlexibleArray<List<int>> neighbours;
        private FlexibleArray<List<int>> springsNeighbours;
        private List<int> bodyNeighbours;
        private ThreadLocal<List<int>> densityNeighbours;

        // Miscellaneous
        private float2 realHalfBoundSize;
        private float2 realHalfBoundSizeBody;
        private const float particleRadius = 0.5f;
        private float timer;
        private float dt;

        public Simulation(SimulationSettings settings, SpawnParticles spawn)
        {
            this.spawn = spawn;
            SetSettings(settings);
        }

        #region Simulation

        public void SimulationStep(float2 mousePos, float deltatime, float realDeltaTime)
        {
            CheckBufferLengths();
            dt = deltatime;

            if (flow)
            {
                timer += dt;

                if (timer >= spawnInterval)
                {
                    timer -= spawnInterval;
                    SpawnFlowParticles();
                }
            }

            Watcher.ExecuteWithTimer("2. Init", () =>
            {
                particleSP.Init(_positions);
                bodySP.Init(_positions);
                springsSP.Init(_positions);
            });

            Watcher.ExecuteWithTimer("3. GetNeighbours", () =>
            {
                Parallel.For(0, neighbours.Count, i => { particleSP.GetNeighbours(_positions[i], neighbours[i]); });
                Parallel.For(0, springsNeighbours.Count, i => { springsSP.GetNeighbours(_positions[i], springsNeighbours[i]); });
                bodySP.GetNeighbours(body.position, bodyNeighbours);
            });

            Watcher.ExecuteWithTimer("4. ExternalForces", ExternalForces);
            Watcher.ExecuteWithTimer("5. ApplyViscosity", ApplyViscosity);

            Watcher.ExecuteWithTimer("6. Advance predicted pos", () =>
            {
                // Advance to predicted position
                Parallel.For(0, _positions.Count, i =>
                {
                    _prevPositions[i] = _positions[i];
                    _positions[i] += dt * _velocities[i];
                });
            });

            Watcher.ExecuteWithTimer("7. Adjust springs", AdjustSprings);
            Watcher.ExecuteWithTimer("8. Spring displacements", SpringDisplacements);

            Watcher.ExecuteWithTimer("9. DoubleDensityRelaxation", DoubleDensityRelaxation);

            if (includeBody)
            {
                Watcher.ExecuteWithTimer("10. Resolve collisions", ResolveCollisions);
                // Watcher.ExecuteWithTimer("11. Upthrust", Upthrust);
            }

            AttractToMouse(mousePos);
            Watcher.ExecuteWithTimer("11. Resolve boundaries", ResolveBoundaries);

            Watcher.ExecuteWithTimer("12. Calculate velocity", () =>
            {
                Parallel.For(0, _positions.Count, i =>
                {
                    _prevVelocities[i] = _velocities[i];
                    _velocities[i] = (_positions[i] - _prevPositions[i]) / dt;
                });
            });

            if (flow)
                ResolveFlow();

            // DrawDebugGrid(Color.green, particleSP);
        }

        public void ExternalForces()
        {
            for (int i = 0; i < _positions.Count; i++)
                _velocities[i].y += dt * gravity;
        }

        public void DoubleDensityRelaxation()
        {
            for (int i = 0; i < _positions.Count; i++)
                _forceBuffers[i] = new(0, 0);

            ParallelOptions options = new();
            options.MaxDegreeOfParallelism = 20;

            Parallel.For(0, _positions.Count, options, i =>
            {
                _densities[i] = 0;
                _nearDensities[i] = 0;

                foreach (var j in neighbours[i])
                {
                    // try distance squared
                    var mag = FluidMath.Distance(_positions[i], _positions[j]);
                    if (mag == 0 || mag > interactionRadius) continue;
                    var q = mag / interactionRadius;

                    _densities[i] += FluidMath.QuadraticSpikyKernel(q);
                    _nearDensities[i] += FluidMath.CubicSpikyKernel(q);
                }

                // if (useParticlesAsBorder)
                // {
                //     _borderDensities[i] = 0;

                //     foreach (var j in _borderPositions)
                //     {
                //         var mag = FluidMath.Distance(_positions[i], j);
                //         if (mag == 0 || mag > interactionRadius) continue;
                //         var q = mag / interactionRadius;
                //         _borderDensities[i] += FluidMath.QuadraticSpikyKernel(q);
                //     }
                // }

                var pressure = stiffness * (_densities[i] - restDensity);
                var nearPressure = nearStiffness * _nearDensities[i];
                // var borderPressure = borderStiffness * _borderDensities[i];

                foreach (var j in neighbours[i])
                {
                    var mag = FluidMath.Distance(_positions[i], _positions[j]);
                    if (mag == 0 || mag > interactionRadius) continue;
                    var q = mag / interactionRadius;

                    var r = FluidMath.UnitVector(_positions[i], _positions[j], mag);
                    var displacement = FluidMath.PressureDisplacement(
                        dt,
                        q,
                        pressure,
                        nearPressure,
                        r);

                    _forceBuffers[j] += displacement / 2;
                    _forceBuffers[i] -= displacement / 2;
                }

                // if (useParticlesAsBorder)
                // {
                //     foreach (var j in _borderPositions)
                //     {
                //         var mag = FluidMath.Distance(_positions[i], j);
                //         if (mag == 0 || mag > interactionRadius) continue;
                //         var q = mag / interactionRadius;
                //         var r = FluidMath.UnitVector(_positions[i], j, mag);
                //         _positions[i] -= dt * dt * borderPressure * (1 - q) * r;
                //     }
                // }
            });

            for (int i = 0; i < _positions.Count; i++)
                _positions[i] += _forceBuffers[i];
        }

        public void ApplyViscosity()
        {
            Parallel.For(0, _positions.Count, i =>
            {
                foreach (var j in neighbours[i])
                {
                    if (i >= j) continue;
                    var mag = FluidMath.Distance(_positions[i], _positions[j]);
                    if (mag > interactionRadius || mag == 0) continue;

                    var q = mag / interactionRadius;
                    var r = (_positions[j] - _positions[i]) / mag;
                    var inwardVelocity = math.dot(_velocities[i] - _velocities[j], r);
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

        public void AdjustSprings()
        {
            // foreach (var key in _springs.Keys)
            // {
            //     if (FluidMath.Distance(_positions[key.Item1], _positions[key.Item2]) > springInteractionRadius)
            //         _springs.TryRemove(key, out _);
            // }

            Parallel.For(0, _positions.Count, i =>
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
                            _springs.TryRemove((i, j), out _);
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

            // for (int i = 0; i < _positions.Count; i++)
            // {
            //     foreach (var spring in _springs)
            //     {
            //         int a = spring.Key.Item1, b = spring.Key.Item2;
            //         if (a != i && b != i) continue;

            //         int other = a == i ? b : a;
            //         var dist = FluidMath.Distance(_positions[i], _positions[other]);
            //         if (dist > springInteractionRadius)
            //             Debug.LogWarning($"AdjustSprings: particle {i} has spring to {other} but dist={dist:F2} > springInteractionRadius={springInteractionRadius}");
            //     }
            // }
        }

        public void SpringDisplacements()
        {
            var springArray = _springs.ToArray(); // todo find a better way

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

        public void ResolveCollisions()
        {
            body.prevPosition = body.position;
            body.position += dt * body.velocity;
            body.velocity.y += dt * -2;

            var force = float2.zero;
            var collisionRad = body.radius + particleRadius;

            var debug = new ConcurrentBag<int>();

            Parallel.ForEach(bodyNeighbours, n =>
            {
                var dist = FluidMath.Distance(body.position, _positions[n]);
                if (!(dist <= collisionRad)) return;
                debug.Add(n);

                // try using absolute value
                var relativeVelocity = _velocities[n] - body.velocity;
                var normalVector = FluidMath.UnitVector(body.position,
                    _positions[n],
                    dist);
                var normalVelocity = math.dot(relativeVelocity, normalVector) * normalVector;

                force += dt * normalVelocity;
            });

            // try interpreting "modify" differently
            body.velocity += force;
            body.position += dt * body.velocity;

            Parallel.ForEach(bodyNeighbours, n =>
            {
                var dist = FluidMath.Distance(body.position, _positions[n]);
                if (!(dist <= collisionRad)) return;

                var relativeVelocity = _velocities[n] - body.velocity;
                var normalVector = FluidMath.UnitVector(body.position,
                    _positions[n],
                    dist);
                var normalVelocity = math.dot(relativeVelocity, normalVector) * normalVector;

                // try adding
                _positions[n] -= dt * normalVelocity;

                dist = FluidMath.Distance(body.position, _positions[n]);
                if (!(dist < collisionRad)) return;

                var unitVector = FluidMath.UnitVector(body.position,
                    _positions[n],
                    dist);
                _positions[n] = body.position + unitVector * collisionRad;
            });
        }

        private void Upthrust()
        {
            var submerged = 0f;
            var tempRadiusSq = body.densityRadius * body.densityRadius;
            var upUnitVector = new float2(0, 1);


            Parallel.For(0, body.densityResolution, i =>
            {
                var neighbours = springsSP.GetNeighbours(body.densityPoints[i]); // temporary maybe

                foreach (var n in neighbours)
                {
                    var magSq = FluidMath.DistanceSq(body.densityPoints[i], _positions[i]);
                    if (magSq < tempRadiusSq)
                    {
                        submerged += 1 / body.densityResolution;
                        break;
                    }
                }
            });

            var force = body.upthrustStrength * submerged * (body.density - restDensity);
            body.position += dt * dt * force * upUnitVector;
        }

        public void ResolveBoundaries()
        {
            // Particles
            // Not sure if parallel for is faster for large number of particles
            for (int i = 0; i < _positions.Count; i++)
            {
                if (Math.Abs(_positions[i].x) >= realHalfBoundSize.x)
                {
                    var sign = Math.Sign(_positions[i].x);
                    _positions[i].x = realHalfBoundSize.x * sign;
                    _positions[i].x += -sign * collisionDamp;
                }

                if (Math.Abs(_positions[i].y) >= realHalfBoundSize.y)
                {
                    var sign = Math.Sign(_positions[i].y);
                    _positions[i].y = realHalfBoundSize.y * sign;
                    _positions[i].y += -sign * collisionDamp;
                }
            }

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

        public void AttractToMouse(float2 mousePos)
        {
            if (Input.GetMouseButton(0))
            {
                Parallel.For(0, _positions.Count, i =>
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
                body.velocity = float2.zero;
            }
        }

        #endregion
        #region Flow
        private void SpawnFlowParticles()
        {
            if (maxParticles < _positions.Count + spawn.spawnPerFlowRow) return;

            float startPosX = -(spawn.spawnPerFlowRow * spawn.flowSpacing);
            float posY = spawn.GetRealHalfBoundSize(particleRadius).y;

            for (int i = 0; i < spawn.spawnPerFlowRow; i++)
                AddParticle(new float2(startPosX + (i * spawn.flowSpacing), posY));
        }

        private void ResolveFlow()
        {
            for (int i = _positions.Count - 1; i >= 0; i--)
            {
                if (_positions[i].y < -(realHalfBoundSize.y - 0.1f))
                    RemoveParticle(i);
            }
        }
        #endregion
        #region Miscellaneous

        public void SetScene()
        {
            // Precomputing values
            realHalfBoundSize = spawn.GetRealHalfBoundSize(particleRadius);
            realHalfBoundSizeBody = spawn.GetRealHalfBoundSize(body.radius);

            particleSP = new SpatialPartitioning(-realHalfBoundSize, realHalfBoundSize, interactionRadius * 2);
            springsSP = new SpatialPartitioning(-realHalfBoundSize, realHalfBoundSize, springInteractionRadius);
            bodySP = new SpatialPartitioning(-realHalfBoundSize, realHalfBoundSize, body.radius + particleRadius);

            bodyNeighbours = new List<int>();

            // Buffers
            if (!flow)
            {
                _positions = spawn.InitializePositions();
                _prevPositions = spawn.InitializePreviousPositions();
                _velocities = spawn.InitializeVelocities();
#if UNITY_EDITOR
                _prevVelocities = spawn.InitializeVelocities();
#endif
                _forceBuffers = spawn.InitializeForcesBuffer();
                _densities = spawn.InitializeDensities();
                _nearDensities = spawn.InitializeNearDensities();
                _springs.Clear();
                // _borderDensities = spawn.InitializeBoundaryDensities();
                // _borderPositions = spawn.InitializeBoundaryPositions();

                neighbours = new FlexibleArray<List<int>>(_positions.Count);
                Parallel.For(0, neighbours.Count, i => { neighbours[i] = new List<int>(); });
                springsNeighbours = new FlexibleArray<List<int>>(_positions.Count);
                Parallel.For(0, springsNeighbours.Count, i => { springsNeighbours[i] = new List<int>(); });
            }

            else
            {
                _positions = new();
                _prevPositions = new();
                _velocities = new();
#if UNITY_EDITOR
                _prevVelocities = new();
#endif
                _forceBuffers = new();
                _densities = new();
                _nearDensities = new();
                _springs.Clear();

                neighbours = new FlexibleArray<List<int>>();
                Parallel.For(0, neighbours.Count, i => { neighbours[i] = new List<int>(); });
                springsNeighbours = new FlexibleArray<List<int>>();
                Parallel.For(0, springsNeighbours.Count, i => { springsNeighbours[i] = new List<int>(); });
            }

            densityNeighbours?.Dispose();
            densityNeighbours = new ThreadLocal<List<int>>(() => new List<int>());

            // In development
            // body.densityPoints = spawn.InitializeBodyDensityPoints(body.densityResolution, body.radius);
        }

        public void SetSettings(SimulationSettings settings)
        {
            UpdateSettings(settings);
            body = settings.body;
        }

        public void UpdateSettings(SimulationSettings settings)
        {
            interactionRadius = settings.interactionRadius;
            gravity = settings.gravity;
            mouseAttractiveness = settings.mouseAttractiveness;
            mouseRadius = settings.mouseRadius;
            collisionDamp = settings.collisionDamping;
            flow = settings.flow;
            maxParticles = settings.maxParticles == -1 ? spawn.InitializePositions().Count : settings.maxParticles;
            spawnInterval = settings.spawnInterval;
            includeBody = settings.includeBody;
            useParticlesAsBorder = settings.useParticlesAsBorder;

            body.radius = settings.body.radius;
            body.density = settings.body.density;
            body.densityResolution = settings.body.densityResolution;
            body.densityRadius = settings.body.densityRadius;
            body.upthrustStrength = settings.body.upthrustStrength;
            body.friction = settings.body.friction;

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

        public float GetDensity(float2 position)
        {
            densityNeighbours ??= new ThreadLocal<List<int>>(() => new List<int>());
            var neighbours = densityNeighbours.Value;
            particleSP.GetNeighbours(position, neighbours);
            var density = 0f;

            foreach (var n in neighbours)
            {
                var mag = FluidMath.Distance(position, _positions[n]);
                if (mag == 0 || mag > interactionRadius) continue;
                var q = mag / interactionRadius;

                density += FluidMath.QuadraticSpikyKernel(q);
            }

            return density;
        }

        private void AddParticle(float2 pos)
        {
            _positions.Add(pos);
            _prevPositions.Add(pos);

            var zero = new float2(0, 0);
            _velocities.Add(zero);
#if UNITY_EDITOR
            _prevVelocities.Add(zero);
#endif
            _forceBuffers.Add(zero);
            _densities.Add(0);
            _nearDensities.Add(0);

            // todo: chnage the list bc of the GC
            neighbours.Add(new List<int>());
            springsNeighbours.Add(new List<int>());
        }

        private void RemoveParticle(int index)
        {
            _positions.RemoveAt(index);
            _prevPositions.RemoveAt(index);

            var zero = new float2(0, 0);
            _velocities.RemoveAt(index);
#if UNITY_EDITOR
            _prevVelocities.RemoveAt(index);
#endif
            _forceBuffers.RemoveAt(index);
            _densities.RemoveAt(index);
            _nearDensities.RemoveAt(index);

            neighbours.RemoveAt(index);
            springsNeighbours.RemoveAt(index);

            springKeysToRemap.Clear();
            foreach (var key in _springs.Keys)
            {
                if (key.Item1 == index || key.Item2 == index || key.Item1 > index || key.Item2 > index)
                    springKeysToRemap.Add(key);
            }

            foreach (var key in springKeysToRemap)
            {
                if (key.Item1 == index || key.Item2 == index)
                    _springs.TryRemove(key, out _);
            }

            foreach (var key in springKeysToRemap)
            {
                if (key.Item1 == index || key.Item2 == index) continue;

                _springs.TryRemove(key, out var restLength);
                var i = key.Item1 > index ? key.Item1 - 1 : key.Item1;
                var j = key.Item2 > index ? key.Item2 - 1 : key.Item2;
                _springs[(i, j)] = restLength;
            }
        }

        private void CheckBufferLengths()
        {
            var equal = new[] {
                _positions.Count,
                _prevPositions.Count,
                _velocities.Count,
                _forceBuffers.Count,
                _densities.Count,
                _nearDensities.Count }.Distinct().Count() == 1;

            if (!equal)
                Debug.LogError("Simulation: the buffer arrays don't have the same length");
        }

        public float GetDensity(Vector3 position) => GetDensity(new float2(position.x, position.y));

        #endregion
        #region Debug purpoused

        public float2[] GetBodySPDimentions() => bodySP.GetNeighboursDimentions(body.position);

        public int[] GetBodySPNeighbours() => bodySP.GetNeighbours(body.position).ToArray();

        public int[] GetBodyNeighbours()
        {
            List<int> indices = new();
            var neighbours = bodySP.GetNeighbours(body.position);
            var collisionRad = body.radius + particleRadius;

            foreach (int n in neighbours)
            {
                var dist = FluidMath.Distance(body.position, _positions[n]);
                if (dist <= collisionRad)
                    indices.Add(n);
            }

            return indices.ToArray();
        }

        public float2[] GetParticleSPDimentions(int particleIndex) => particleSP.GetNeighboursDimentions(_positions[particleIndex]);

        public int[] GetParticlesSPNeighbours(int particleIndex) => particleSP.GetNeighbours(_positions[particleIndex]).ToArray();

        public int[] GetNeighbourParticles(int particleIndex)
        {
            List<int> indices = new();
            var neighbours = particleSP.GetNeighbours(_positions[particleIndex]);

            foreach (int n in neighbours)
            {
                var dist = FluidMath.Distance(_positions[particleIndex], _positions[n]);
                if (dist <= interactionRadius && n != particleIndex)
                    indices.Add(n);
            }

            return indices.ToArray();
        }

        public int[] GetNeighbourParticles(float2 pos)
        {
            List<int> indices = new();
            var neighbours = particleSP.GetNeighbours(pos);

            foreach (int n in neighbours)
            {
                var dist = FluidMath.Distance(pos, _positions[n]);
                if (dist <= interactionRadius)
                    indices.Add(n);
            }

            return indices.ToArray();
        }

        public float2[] GetNeighbourParticlesPositions(float2 pos)
        {
            List<float2> indices = new();
            var neighbours = particleSP.GetNeighbours(pos);

            foreach (int n in neighbours)
            {
                var dist = FluidMath.Distance(pos, _positions[n]);
                if (dist <= interactionRadius)
                    indices.Add(_positions[n]);
            }

            return indices.ToArray();
        }

        // Draws the boundary box in scene
        public void DrawDebugSquare(Vector3 center, float2 halfSize, Color color)
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

        // Draws the spatial partitioning grid in scene
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

public class FlexibleArray<T> : IEnumerable<T>
{
    private T[] _items;
    private int _size;

    public int Count => _size;
    public int Capacity => _items.Length;

    private const int defaultSize = 4;

    public FlexibleArray(int size = 0)
    {
        if (size == 0)
            _items = new T[defaultSize];

        else
            _items = new T[size];

        _size = size;
    }

    public ref T this[int index]
    {
        get
        {
            if (index >= _size)
                throw new IndexOutOfRangeException();

            return ref _items[index];
        }
    }

    public void Add()
    {
        if (_size == _items.Length)
            Resize(_items.Length * 2);
    }

    public void Add(T item)
    {
        if (_size == _items.Length)
            Resize(_items.Length * 2);

        _items[_size++] = item;
    }

    public void RemoveLast()
    {
        if (_size == 0)
            throw new InvalidOperationException();

        _size--;
        _items[_size] = default!;
    }

    public void RemoveAt(int index)
    {
        if ((uint)index >= (uint)_size)
            throw new IndexOutOfRangeException();

        _size--;

        if (index < _size)
            Array.Copy(_items, index + 1, _items, index, _size - index);

        _items[_size] = default!;
    }

    public void Clear()
    {
        Array.Clear(_items, 0, _size);
        _size = 0;
    }

    public void EnsureCapacity(int capacity)
    {
        if (capacity > _items.Length)
            Resize(capacity);
    }

    public IEnumerator<T> GetEnumerator()
    {
        for (int i = 0; i < _size; i++)
            yield return _items[i];
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    private void Resize(int newCapacity)
    {
        T[] newArr = new T[newCapacity];
        Array.Copy(_items, newArr, _size);
        _items = newArr;
    }
}