using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Unity.Mathematics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace SimulationLogic
{
    public class Particle
    {
        public int ID;
        public float2 position;
        public float2 prevPosition;
        public float2 velocity;
        public float2 forceBuffer;
        public float density;
        public float nearDensity;
        public RefList<int> neighbours;
        public RefList<int> springsNeighbours;

        public Particle()
        {
            neighbours = new RefList<int>();
            springsNeighbours = new RefList<int>();
        }

        public Particle(float2 position) : this()
        {
            this.position = position;
        }
    }

    // Change parallel for to Unity jobs for the best performance
    public class Simulation
    {
        // General settings
        private float interactionRadius;
        private float gravity;
        private float mouseAttractiveness;
        private float mouseRadius;
        private float collisionDamp;
        public bool flow { get; private set; }
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
        public int maxParticles { get; private set; }
        public Particle[] _particles { get; private set; }
        public int[] _sparse { get; private set; }
        public int _count { get; private set; }
        private RefList<int> _freeIDs;
        private ConcurrentDictionary<(int, int), float> _springs;

        // Spatial partitioning buffers
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

        public void SimulationStep(float2 mousePos, float deltatime)
        {
            dt = deltatime;

            if (flow)
            {
                timer += dt;

                if (timer >= spawn.spawnInterval)
                {
                    timer -= spawn.spawnInterval;
                    Watcher.ExecuteWithTimer("2. SpawnFlowParticles", SpawnFlowParticles);
                }
            }

            Watcher.ExecuteWithTimer("3. Init", () =>
            {
                particleSP.Init(_particles.AsSpan(0, _count));
                bodySP.Init(_particles.AsSpan(0, _count));
                springsSP.Init(_particles.AsSpan(0, _count));
            });

            Watcher.ExecuteWithTimer("4. GetNeighbours", () =>
            {
                Parallel.For(0, _count, i =>
                {
                    particleSP.GetNeighbours(_particles[i].position, _particles[i].neighbours);
                });
                Parallel.For(0, _count, i =>
                {
                    springsSP.GetNeighbours(_particles[i].position, _particles[i].springsNeighbours);
                });
                bodySP.GetNeighbours(body.position, bodyNeighbours);
            });

            Watcher.ExecuteWithTimer("5. ExternalForces", ExternalForces);
            Watcher.ExecuteWithTimer("6. ApplyViscosity", ApplyViscosity);

            Watcher.ExecuteWithTimer("7. Advance predicted pos", () =>
            {
                for (int i = 0; i < _count; i++)
                {
                    _particles[i].prevPosition = _particles[i].position;
                    _particles[i].position += dt * _particles[i].velocity;
                }
            });

            Watcher.ExecuteWithTimer("8. Adjust springs", AdjustSprings);
            Watcher.ExecuteWithTimer("9. Spring displacements", SpringDisplacements);

            Watcher.ExecuteWithTimer("10. DoubleDensityRelaxation", DoubleDensityRelaxation);

            // if (includeBody)
            // {
            //     Watcher.ExecuteWithTimer("10. Resolve collisions", ResolveCollisions);
            //     // Watcher.ExecuteWithTimer("11. Upthrust", Upthrust);
            // }

            AttractToMouse(mousePos);
            Watcher.ExecuteWithTimer("11. Resolve boundaries", ResolveBoundaries);

            Watcher.ExecuteWithTimer("12. Calculate velocity", () =>
            {
                for (int i = 0; i < _count; i++)
                    _particles[i].velocity = (_particles[i].position - _particles[i].prevPosition) / dt;
            });

            if (flow)
                Watcher.ExecuteWithTimer("13. Init", ResolveFlow);

            // DrawDebugGrid(Color.green, particleSP);
        }

        private void ExternalForces()
        {
            for (int i = 0; i < _count; i++)
                _particles[i].velocity.y += dt * gravity;
        }

        private void DoubleDensityRelaxation()
        {
            ClearForceBuffers();

            Parallel.For(0, _count, i =>
            {
                var p = _particles[i];
                p.density = 0;
                p.nearDensity = 0;

                foreach (var n in p.neighbours)
                {
                    // try distance squared
                    var mag = FluidMath.Distance(p.position, _particles[_sparse[n]].position);
                    if (mag == 0 || mag > interactionRadius) continue;
                    var q = mag / interactionRadius;

                    p.density += FluidMath.QuadraticSpikyKernel(q);
                    p.nearDensity += FluidMath.CubicSpikyKernel(q);
                }

                var pressure = stiffness * (p.density - restDensity);
                var nearPressure = nearStiffness * p.nearDensity;

                foreach (var n in p.neighbours)
                {
                    var mag = FluidMath.Distance(p.position, _particles[_sparse[n]].position);
                    if (mag == 0 || mag > interactionRadius) continue;
                    var q = mag / interactionRadius;

                    var r = FluidMath.UnitVector(p.position, _particles[_sparse[n]].position, mag);
                    var displacement = FluidMath.PressureDisplacement(
                        dt,
                        q,
                        pressure,
                        nearPressure,
                        r);

                    _particles[_sparse[n]].forceBuffer += displacement / 2; // try lock to see if anything chnages
                    p.forceBuffer -= displacement / 2;
                }
            });

            ApplyForceBuffers();
        }

        private void ApplyViscosity()
        {
            Parallel.For(0, _count, i =>
            {
                var p = _particles[i];
                foreach (var n in p.neighbours)
                {
                    if (p.ID >= n) continue;
                    var mag = FluidMath.Distance(p.position, _particles[_sparse[n]].position);
                    if (mag > interactionRadius || mag == 0) continue;

                    var q = mag / interactionRadius;
                    var r = FluidMath.UnitVector(p.position, _particles[_sparse[n]].position, mag);
                    var inwardVelocity = math.dot(p.velocity - _particles[_sparse[n]].velocity, r);
                    if (!(inwardVelocity > 0)) continue;

                    var impulse = FluidMath.ViscosityImpulse(dt,
                        highViscosity,
                        lowViscosity,
                        q,
                        inwardVelocity,
                        r);

                    p.velocity -= impulse / 2;
                    _particles[_sparse[n]].velocity += impulse / 2;
                }
            });
        }

        private void AdjustSprings()
        {
            Parallel.For(0, _count, i => 
            {
                var p = _particles[i];
                foreach (var n in p.springsNeighbours)
                {
                    if (p.ID >= n) continue;

                    var mag = FluidMath.Distance(p.position, _particles[_sparse[n]].position);
                    var q = mag / springInteractionRadius;
                    switch (q)
                    {
                        case 0:
                            continue;
                        case > 1:
                            if (_springs.ContainsKey((p.ID, n)))
                                _springs[(p.ID, n)] = float.MinValue;
                            continue;
                    }

                    if (!_springs.TryGetValue((p.ID, n), out var restLength))
                    {
                        _springs.TryAdd((p.ID, n), springRadius);
                        restLength = springRadius;
                    }

                    else if (restLength == float.MinValue)
                    {
                        _springs[(p.ID, n)] = springRadius;
                        restLength = springRadius;
                    }

                    var deformation = springDeformationLimit * restLength;

                    if (mag > restLength + deformation)
                        _springs[(p.ID, n)] += FluidMath.StretchSpring(dt,
                            plasticity,
                            mag,
                            restLength,
                            deformation);

                    else if (mag < restLength + deformation)
                        _springs[(p.ID, n)] -= FluidMath.CompressSpring(dt,
                            plasticity,
                            mag,
                            restLength,
                            deformation);
                }
            });
        }

        private void SpringDisplacements()
        {
            Parallel.ForEach(_springs, kvp =>
            {
                var i = kvp.Key.Item1;
                var j = kvp.Key.Item2;

                var p = _particles[_sparse[i]];
                var n = _particles[_sparse[j]];

                if (kvp.Value == float.MinValue) return;

                var mag = FluidMath.Distance(p.position, n.position);
                if (mag == 0) return;

                var r = FluidMath.UnitVector(p.position, n.position, mag);
                var displacement = FluidMath.DisplacementBySpring(dt,
                    springStiffness,
                    kvp.Value,
                    springRadius,
                    mag,
                    r);

                p.position -= displacement / 2;
                n.position += displacement / 2;
            });
        }

        private void ResolveCollisions()
        {
            body.prevPosition = body.position;
            body.position += dt * body.velocity;
            body.velocity.y += dt * -2;

            var force = float2.zero;
            var collisionRad = body.radius + particleRadius;

            Parallel.ForEach(bodyNeighbours, n =>
            {
                var p = _particles[_sparse[n]];
                var dist = FluidMath.Distance(body.position, p.position);
                if (!(dist <= collisionRad)) return;

                // try using absolute value
                var relativeVelocity = p.velocity - body.velocity;
                var normalVector = FluidMath.UnitVector(body.position,
                    p.position,
                    dist);
                var normalVelocity = math.dot(relativeVelocity, normalVector) * normalVector;

                force += dt * normalVelocity;
            });

            // try interpreting "modify" differently
            body.velocity += force;
            body.position += dt * body.velocity;

            Parallel.ForEach(bodyNeighbours, n =>
            {
                var p = _particles[_sparse[n]];
                var dist = FluidMath.Distance(body.position, p.position);
                if (!(dist <= collisionRad)) return;

                var relativeVelocity = p.velocity - body.velocity;
                var normalVector = FluidMath.UnitVector(body.position,
                    p.position,
                    dist);
                var normalVelocity = math.dot(relativeVelocity, normalVector) * normalVector;

                // try adding
                p.position -= dt * normalVelocity;

                dist = FluidMath.Distance(body.position, p.position);
                if (!(dist < collisionRad)) return;

                var unitVector = FluidMath.UnitVector(body.position,
                    p.position,
                    dist);
                p.position = body.position + unitVector * collisionRad;
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
                    var magSq = FluidMath.DistanceSq(body.densityPoints[i], _particles[_sparse[n]].position);
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

        private void ResolveBoundaries()
        {
            // Particles
            for (int i = 0; i < _count; i++)
            {
                var pos = _particles[i].position;
                if (Math.Abs(pos.x) >= realHalfBoundSize.x)
                {
                    var sign = Math.Sign(pos.x);
                    pos.x = realHalfBoundSize.x * sign;
                    pos.x += -sign * collisionDamp;
                    _particles[i].position = pos;
                }

                if (Math.Abs(pos.y) >= realHalfBoundSize.y)
                {
                    var sign = Math.Sign(pos.y);
                    pos.y = realHalfBoundSize.y * sign;
                    pos.y += -sign * collisionDamp;
                    _particles[i].position = pos;
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

        private void AttractToMouse(float2 mousePos)
        {
            if (Input.GetMouseButton(0))
            {
                Parallel.For(0, _count, i =>
                {
                    var pos = _particles[i].position;
                    var dist = FluidMath.Distance(pos, mousePos);
                    if (dist > mouseRadius) return;

                    var unitVector = FluidMath.UnitVector(pos, mousePos, dist);
                    pos += dt * mouseAttractiveness * unitVector;
                    _particles[i].position = pos;
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
            if (maxParticles < _count + spawn.spawnPerFlowRow) return;

            float startPosX = -((spawn.spawnPerFlowRow - 1) * spawn.flowSpacing / 2f);
            float posY = spawn.GetRealHalfBoundSize(particleRadius).y;

            for (int i = 0; i < spawn.spawnPerFlowRow; i++)
                AddParticle(new float2(startPosX + (i * spawn.flowSpacing), posY));
        }

        private void ResolveFlow()
        {
            for (int i = _count - 1; i >= 0; i--)
            {
                if (_particles[i].position.y < -(realHalfBoundSize.y - 0.1f))
                    RemoveParticle(_particles[i].ID);
            }
        }
        #endregion
        #region SetUp

        public void SetScene()
        {
            // Precomputing values
            realHalfBoundSize = spawn.GetRealHalfBoundSize(particleRadius);
            realHalfBoundSizeBody = spawn.GetRealHalfBoundSize(body.radius);

            particleSP = new SpatialPartitioning(-realHalfBoundSize, realHalfBoundSize, interactionRadius);
            springsSP = new SpatialPartitioning(-realHalfBoundSize, realHalfBoundSize, springInteractionRadius);
            bodySP = new SpatialPartitioning(-realHalfBoundSize, realHalfBoundSize, body.radius + particleRadius);

            bodyNeighbours = new List<int>();

            // Buffers
            _springs = new ConcurrentDictionary<(int, int), float>();
            _particles = new Particle[maxParticles];
            _sparse = new int[maxParticles];
            _freeIDs = new RefList<int>(maxParticles);
            _count = 0;

            for (int i = 0; i < maxParticles; i++)
                _particles[i] = new Particle();

            for (int i = maxParticles - 1; i >= 0; i--)
                _freeIDs.Add(i);

            if (!flow)
            {
                var positions = spawn.InitPositions();
                for (int i = 0; i < positions.Count; i++)
                    AddParticle(positions[i]);
            }

            densityNeighbours?.Dispose();
            densityNeighbours = new ThreadLocal<List<int>>(() => new List<int>());
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
            maxParticles = settings.maxParticles == -1 ? spawn.InitPositions().Count : settings.maxParticles;
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
        #endregion
        #region Helpers
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ClearForceBuffers()
        {
            for (int i = 0; i < _count; i++)
                _particles[i].forceBuffer = new(0, 0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ApplyForceBuffers()
        {
            for (int i = 0; i < _count; i++)
                _particles[i].position += _particles[i].forceBuffer;
        }

        private void AddParticle(float2 pos)
        {
            if (_count == maxParticles)
            {
                Debug.LogWarning("Simulation: adding when max number of particles reached");
                return;
            }

            var id = _freeIDs.Last();
            _freeIDs.RemoveLast();

            var p = _particles[_count];
            p.ID = id;
            p.position = pos;
            p.prevPosition = pos;

            _sparse[id] = _count;
            _count++;
        }

        private void RemoveParticle(int id)
        {
            int i = _sparse[id];

            if (i != _count - 1)
            {
                (_particles[_count - 1], _particles[i]) = (_particles[i], _particles[_count - 1]);
                _sparse[_particles[i].ID] = i;
            }

            ClearParticleExceptPos(_particles[_count - 1]);

            _freeIDs.Add(id);
            _count--;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Particle ClearParticleExceptPos(Particle particle)
        {
            particle.velocity = float2.zero;
            particle.forceBuffer = float2.zero;
            particle.density = 0f;
            particle.nearDensity = 0f;
            particle.neighbours.Clear();
            particle.springsNeighbours.Clear();
            return particle;
        }

        public float GetDensity(float2 position)
        {
            densityNeighbours ??= new ThreadLocal<List<int>>(() => new List<int>());
            var neighbours = densityNeighbours.Value;
            particleSP.GetNeighbours(position, neighbours);
            var density = 0f;

            foreach (var n in neighbours)
            {
                var mag = FluidMath.Distance(position, _particles[_sparse[n]].position);
                if (mag == 0 || mag > interactionRadius) continue;
                var q = mag / interactionRadius;

                density += FluidMath.QuadraticSpikyKernel(q);
            }

            return density;
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
                var dist = FluidMath.Distance(body.position, _particles[_sparse[n]].position);
                if (dist <= collisionRad)
                    indices.Add(n);
            }

            return indices.ToArray();
        }

        public float2[] GetParticleSPDimentions(int particleID) => particleSP.GetNeighboursDimentions(_particles[_sparse[particleID]].position);

        public int[] GetParticlesSPNeighbours(int particleID) => particleSP.GetNeighbours(_particles[_sparse[particleID]].position).ToArray();

        public int[] GetNeighbourParticles(int particleID)
        {
            List<int> indices = new();
            var neighbours = particleSP.GetNeighbours(_particles[_sparse[particleID]].position);

            foreach (int n in neighbours)
            {
                var dist = FluidMath.Distance(_particles[_sparse[particleID]].position, _particles[_sparse[n]].position);
                if (dist <= interactionRadius && n != particleID)
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
                var dist = FluidMath.Distance(pos, _particles[_sparse[n]].position);
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
                var dist = FluidMath.Distance(pos, _particles[_sparse[n]].position);
                if (dist <= interactionRadius)
                    indices.Add(_particles[_sparse[n]].position);
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
