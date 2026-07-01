using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Rendering;
using Unity.Mathematics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace SimulationLogic
{
    public class Particle
    {
        public float2 position;
        public float2 prevPosition;
        public float2 velocity;
        public float2 forceBuffer;
        public float density;
        public float nearDensity;
        public RefList<int> neighbours;
    }

    public class FluidParticle : Particle
    {
        public int ID;
        public RefList<int> springsNeighbours;
        public RefList<int> boundaryNeighbours;

        public FluidParticle()
        {
            ID = -1;
            neighbours = new RefList<int>();
            springsNeighbours = new RefList<int>();
            boundaryNeighbours = new RefList<int>();
        }

        public FluidParticle(float2 position) : this()
        {
            this.position = position;
        }
    }

    public class BoundaryParticle : Particle
    {
        public float volume;

        public BoundaryParticle(float2 position)
        {
            this.position = position;
            neighbours = new RefList<int>();
        }

        public BoundaryParticle(float2 position, float volume)
        {
            this.position = position;
            this.volume = volume;
            neighbours = new RefList<int>();
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
        public bool includeBody { get; private set; }

        // Bodies
        public Body body; // maybe change to prop later
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
        private InitializeParticles initParticles;
        private Boundaries boundaries;

        // Spatial partitioning grids
        private SpatialPartitioning particleSP;
        private SpatialPartitioning springsSP;
        private SpatialPartitioning bodySP;
        private SpatialPartitioning boundarySP;

        // Buffers
        // Fluid particles
        public int maxParticles { get; private set; }
        public FluidParticle[] _particles { get; private set; }
        public SparseArray _sparse { get; private set; }
        public int count { get; private set; }
        private RefList<int> _freeIDs;
        public RefList<BoundaryParticle> _boundaryParticles { get; private set; }
        private ConcurrentDictionary<(int, int), float> _springs;

        // Spatial partitioning buffers
        private List<int> bodyNeighbours;
        private ThreadLocal<List<int>> densityNeighbours;

        // Miscellaneous
        public float2 realHalfBoundSize; // temporarily public
        private float2 realHalfBoundSizeBody;
        private const float particleRadius = 0.5f;
        private static readonly object lockObject = new();
        private float timer;
        private float dt;

        // RENDER MANAGER ONLY FOR DEBUG DONT PUSH ON MAIN
        private RenderManager render;

        public Simulation(SimulationSettings settings, InitializeParticles spawn, RenderManager renderManager)
        {
            initParticles = spawn;
            render = renderManager;
            SetSettings(settings);
        }

        #region Simulation

        public void SimulationStep(float2 mousePos, float deltatime)
        {
            if (deltatime <= 0)
            {
                Debug.LogWarning($"Simulation: deltatime is {deltatime}, skipping step to avoid division by zero");
                return;
            }

            CheckArraysLength();
            dt = deltatime;

            if (flow)
            {
                timer += dt;

                if (timer >= initParticles.spawnInterval)
                {
                    timer -= initParticles.spawnInterval;
                    Watcher.ExecuteWithTimer("2. SpawnFlowParticles", SpawnFlowParticles);
                }
            }

            Watcher.ExecuteWithTimer("3. Init", InitSpatialPartitioning);
            Watcher.ExecuteWithTimer("4. GetNeighbours", SetNeighbours);

            Watcher.ExecuteWithTimer("5. ExternalForces", ExternalForces);
            Watcher.ExecuteWithTimer("6. ApplyViscosity", ApplyViscosity);

            Watcher.ExecuteWithTimer("7. Advance predicted pos", () =>
            {
                for (int i = 0; i < count; i++)
                {
                    _particles[i].prevPosition = _particles[i].position;
                    _particles[i].position += dt * _particles[i].velocity;
                }
            });

            Watcher.ExecuteWithTimer("8. Adjust springs", AdjustSprings);
            Watcher.ExecuteWithTimer("9. Spring displacements", SpringDisplacements);

            Watcher.ExecuteWithTimer("10. DoubleDensityRelaxation", DoubleDensityRelaxation);

            AttractToMouse(mousePos);
            Watcher.ExecuteWithTimer("11. Resolve boundaries", ResolveBoundariesStep);

            Watcher.ExecuteWithTimer("12. Calculate velocity", () =>
            {
                for (int i = 0; i < count; i++)
                    _particles[i].velocity = (_particles[i].position - _particles[i].prevPosition) / dt;
            });

            if (flow)
                Watcher.ExecuteWithTimer("13. Init", ResolveFlow);
        }

        private void ExternalForces()
        {
            for (int i = 0; i < count; i++)
                _particles[i].velocity.y += dt * gravity;
        }

        private void DoubleDensityRelaxation()
        {
            ClearForceBuffers();

            Parallel.For(0, count, i =>
            {
                var p = _particles[i];
                p.density = 0;
                p.nearDensity = 0;

                foreach (var n in p.neighbours)
                {
                    // todo: try distance squared and compute q just once
                    // maybe need mass
                    float mag = FluidMath.Distance(p.position, _particles[_sparse[n]].position);
                    if (mag == 0 || mag > interactionRadius) continue;
                    float q = mag / interactionRadius;

                    p.density += FluidMath.QuadraticSpikyKernel(q);
                    p.nearDensity += FluidMath.CubicSpikyKernel(q);
                }

                foreach (var b in p.boundaryNeighbours)
                {
                    float mag = FluidMath.Distance(p.position, _boundaryParticles[b].position);
                    if (mag == 0 || mag > interactionRadius) continue;
                    float q = mag / interactionRadius;

                    // can be precomputed
                    float psi = restDensity * _boundaryParticles[b].volume;
                    p.density += psi * FluidMath.QuadraticSpikyKernel(q);
                    p.nearDensity += psi * FluidMath.CubicSpikyKernel(q);
                }

                float pressure = stiffness * (p.density - restDensity);
                float nearPressure = nearStiffness * p.nearDensity;

                foreach (var n in p.neighbours)
                {
                    float mag = FluidMath.Distance(p.position, _particles[_sparse[n]].position);
                    if (mag == 0 || mag > interactionRadius) continue;
                    float q = mag / interactionRadius;

                    float2 r = FluidMath.UnitVector(p.position, _particles[_sparse[n]].position, mag);
                    float2 displacement = FluidMath.PressureDisplacement(
                        dt,
                        q,
                        pressure,
                        nearPressure,
                        r);

                    lock (lockObject) { _particles[_sparse[n]].forceBuffer += displacement / 2; }
                    lock (lockObject) { p.forceBuffer -= displacement / 2; }
                }

                // Boundary particle displacement (Akinci et al. 2012, eqs. 9-10).
                // Uses the fluid particle's own pressure, scaled by the boundary weight Psi.
                // The boundary stays fixed, so the fluid particle absorbs the full
                // displacement instead of the symmetric half used between two fluids.
                foreach (var b in p.boundaryNeighbours)
                {
                    float mag = FluidMath.Distance(p.position, _boundaryParticles[b].position);
                    if (mag == 0 || mag > interactionRadius) continue;
                    float q = mag / interactionRadius;

                    float2 r = FluidMath.UnitVector(p.position, _boundaryParticles[b].position, mag);
                    float2 displacement = FluidMath.PressureDisplacement(
                        dt,
                        q,
                        pressure,
                        nearPressure,
                        r);

                    float psi = restDensity * _boundaryParticles[b].volume;
                    lock (lockObject) { p.forceBuffer -= psi * displacement; }
                }
            });

            ApplyForceBuffers();
        }

        private void ApplyViscosity()
        {
            Parallel.For(0, count, i =>
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
            Parallel.For(0, count, i =>
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
                                _springs.TryRemove((p.ID, n), out _);
                            continue;
                    }

                    if (!_springs.TryGetValue((p.ID, n), out var restLength))
                    {
                        _springs.TryAdd((p.ID, n), springRadius);
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

                if (i >= maxParticles || j >= maxParticles)
                {
                    Debug.LogWarning($"Simulation: spring references out-of-range particle IDs ({i}, {j}), skipping");
                    return;
                }

                var p = _particles[_sparse[i]];
                var n = _particles[_sparse[j]];

                if (p.ID != i || n.ID != j)
                {
                    Debug.LogWarning($"Simulation: stale spring entry ({i}, {j}) — particle IDs no longer match, spring will be skipped");
                    return;
                }

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
            body.velocity.y += dt * -20;

            var force = float2.zero;
            var collisionRad = body.radius + particleRadius;

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

                force += dt * normalVelocity / 5;
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

        private void AttractToMouse(float2 mousePos)
        {
            if (Input.GetMouseButton(0))
            {
                Parallel.For(0, count, i =>
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

        private void InitSpatialPartitioning()
        {
            // todo: evaluate if I need 3 seperate SP
            particleSP.Init(_particles.AsSpan(0, count));
            bodySP.Init(_particles.AsSpan(0, count));
            springsSP.Init(_particles.AsSpan(0, count));
            boundarySP.Init(_boundaryParticles.AsSpan(0, _boundaryParticles.Count));
        }

        private void SetNeighbours()
        {
            // todo: try using regular for and batching
            Parallel.For(0, count, i =>
            {
                particleSP.GetNeighbours(_particles[i].position, _particles[i].neighbours);
            });

            Parallel.For(0, count, i =>
            {
                springsSP.GetNeighbours(_particles[i].position, _particles[i].springsNeighbours);
            });

            bodySP.GetNeighbours(body.position, bodyNeighbours);

            Parallel.For(0, count, i =>
            {
                boundarySP.GetNeighbours(_particles[i].position, _particles[i].boundaryNeighbours);
            });
        }

        private void ClearNeighbours()
        {
            foreach (var particle in _particles)
            {
                particle.neighbours.Clear();
                particle.springsNeighbours.Clear();
                particle.boundaryNeighbours.Clear();
            }

            bodyNeighbours.Clear();
        }

        #endregion
        #region Flow
        private void SpawnFlowParticles()
        {
            if (maxParticles < count + initParticles.spawnPerFlowRow) return;

            float startPosX = -((initParticles.spawnPerFlowRow - 1) * initParticles.flowSpacing / 2f);
            float posY = initParticles.GetRealHalfBoundSize(particleRadius).y;

            for (int i = 0; i < initParticles.spawnPerFlowRow; i++)
                AddParticle(new float2(startPosX + (i * initParticles.flowSpacing), posY));
        }

        private void ResolveFlow()
        {
            for (int i = count - 1; i >= 0; i--)
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
            realHalfBoundSize = initParticles.GetRealHalfBoundSize(particleRadius);
            realHalfBoundSizeBody = initParticles.GetRealHalfBoundSize(body.radius);
            // quadraticSpikyKernelVolume = math.PI * (interactionRadius * interactionRadius) / 6;
            // cubicSpikyKernelVolume = math.PI * (interactionRadius * interactionRadius) / 10;

            if (realHalfBoundSize.x <= 0 || realHalfBoundSize.y <= 0)
                Debug.LogWarning($"Simulation: realHalfBoundSize is {realHalfBoundSize}, bounding box is degenerate — particles may escape or behave incorrectly");

            particleSP = new SpatialPartitioning(-realHalfBoundSize, realHalfBoundSize, interactionRadius);
            springsSP = new SpatialPartitioning(-realHalfBoundSize, realHalfBoundSize, springInteractionRadius + 0.5f);
            bodySP = new SpatialPartitioning(-realHalfBoundSize, realHalfBoundSize, body.radius + particleRadius);
            boundarySP = new SpatialPartitioning(-realHalfBoundSize, realHalfBoundSize, interactionRadius);

            bodyNeighbours = new List<int>();

            // Buffers
            EnsureArrays(maxParticles);

            if (!flow)
            {
                var positions = initParticles.InitPositions();
                for (int i = 0; i < positions.Count; i++)
                    AddParticle(positions[i]);
            }

            densityNeighbours?.Dispose();
            densityNeighbours = new ThreadLocal<List<int>>(() => new List<int>());

            boundaries = new Boundaries(_particles, count, particleRadius, collisionDamp, realHalfBoundSizeBody);

            // Test boundaries make it look better before pushing to main
            _boundaryParticles = new();
            int nbBoundaryParticles = 100;
            var boundPos = initParticles.InitCircleOutlinePositions(10, nbBoundaryParticles, new(60, -80));
            // var boundPos = initParticles.InitBorderPositions();
            for (int i = 0; i < nbBoundaryParticles; i++)
                _boundaryParticles.Add(new(boundPos[i]));

            boundarySP.Init(_boundaryParticles.AsSpan());
            Parallel.For(0, nbBoundaryParticles, i =>
            {
                boundarySP.GetNeighbours(_boundaryParticles[i].position, _boundaryParticles[i].neighbours);
            });

            CalculateBoundaryParticleVolumes();
        }

        private void CalculateBoundaryParticleVolumes()
        {
            for (int i = 0; i < _boundaryParticles.Count; i++)
            {
                float delta = 0f;

                foreach (int n in _boundaryParticles[i].neighbours)
                {
                    float relativeDistance = FluidMath.Distance(_boundaryParticles[i].position, _boundaryParticles[n].position) / interactionRadius;
                    if (relativeDistance == 0 || relativeDistance > 1f) continue;

                    delta += FluidMath.QuadraticSpikyKernel(relativeDistance);
                }

                _boundaryParticles[i].volume = 1 / delta;
            }
        }

        public void SetSettings(SimulationSettings settings)
        {
            UpdateSettings(settings);
            body = settings.body;
        }

        public void UpdateSettings(SimulationSettings settings)
        {
            if (settings.interactionRadius <= 0)
                Debug.LogWarning($"Simulation: interactionRadius is {settings.interactionRadius}, will cause division by zero in density and viscosity calculations");

            if (settings.springInteractionRadius <= 0)
                Debug.LogWarning($"Simulation: springInteractionRadius is {settings.springInteractionRadius}, will cause division by zero in spring calculations");

            interactionRadius = settings.interactionRadius;
            gravity = settings.gravity;
            mouseAttractiveness = settings.mouseAttractiveness;
            mouseRadius = settings.mouseRadius;
            collisionDamp = settings.collisionDamping;
            flow = settings.flow;

            var newMax = GetMaxParticles(settings.maxParticles);
            HandleParticleArrSize(newMax);
            maxParticles = newMax;

            includeBody = settings.includeBody;

            body.radius = settings.body.radius;
            body.density = settings.body.density;
            body.densityResolution = settings.body.densityResolution;
            body.densityRadius = settings.body.densityRadius;
            body.upthrustStrength = settings.body.upthrustStrength;
            body.friction = settings.body.friction;

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
        #endregion
        #region ParticleArraysHandler
        private int GetMaxParticles(int newMax)
        {
            if (!flow)
                return initParticles.InitPositions().Count;

            else
            {
                if (newMax == -1)
                    return 1_000_000;

                if (newMax < count)
                    Debug.LogWarning($"Simulation: new max particles is smaller than the number of current particles");

                return newMax;
            }
        }

        private void EnsureArrays(int len)
        {
            if (len <= 0)
                Debug.LogWarning($"Simulation: EnsureArrays called with len={len}, arrays will be empty");

            _springs ??= new();
            _freeIDs ??= new RefList<int>();
            bodyNeighbours = new();

            if (_sparse == null)
            {
                _sparse = new SparseArray(len);
                _sparse.Fill(-1);
            }

            if (_particles == null)
            {
                _particles = new FluidParticle[len];
                Array.Fill(_particles, new FluidParticle());
            }
        }

        private void HandleParticleArrSize(int newMax)
        {
            if (newMax == maxParticles)
                return;

            if (newMax < maxParticles)
            {
                Debug.LogError("Simulation can't set max number of particles to lower than before");
                return;
            }

            if (newMax <= 0)
            {
                Debug.LogError($"Simulation: HandleParticleArrSize called with newMax={newMax}, aborting resize");
                return;
            }

            EnsureArrays(newMax);
            _springs.Clear();
            ClearNeighbours();

            FluidParticle[] newParticles = new FluidParticle[newMax];
            SparseArray newSparse = new(newMax);

            if (newMax > maxParticles)
            {
                Array.Copy(_particles, newParticles, maxParticles);
                _sparse.CopyTo(newSparse, maxParticles);

                for (int i = maxParticles; i < newMax; i++)
                {
                    newSparse[i] = -1;
                    newParticles[i] = new FluidParticle();
                    _freeIDs.Add(i);
                }
            }

            _particles = newParticles;
            _sparse = newSparse;
        }

        private void AddParticle(float2 pos)
        {
            if (count == maxParticles)
            {
                Debug.LogError("Simulation: adding when max number of particles reached");
                return;
            }

            var id = _freeIDs.Last();
            _freeIDs.RemoveLast();

            var p = _particles[count];
            p.ID = id;
            p.position = pos;
            p.prevPosition = pos;

            _sparse[id] = count;
            count++;
        }

        private void RemoveParticle(int id)
        {
            if (count == 0)
            {
                Debug.LogError("Simulation: RemoveParticle called when there are no particles");
                return;
            }

            if (id < 0 || id >= maxParticles)
            {
                Debug.LogError($"Simulation: RemoveParticle called with out-of-range id={id} (maxParticles={maxParticles})");
                return;
            }

            try
            {
                int i = _sparse[id];

                foreach (var key in _springs.Keys)
                {
                    if (key.Item1 == id || key.Item2 == id)
                        _springs.TryRemove(key, out _);
                }

                (_particles[count - 1], _particles[i]) = (_particles[i], _particles[count - 1]);
                _sparse[id] = -1;
                _sparse[_particles[i].ID] = i;

                _particles[count - 1] = ClearParticleExceptPos(_particles[count - 1]);
                _freeIDs.Add(id);
                count--;
            }
            catch
            {
                Debug.LogError("Simulation: trying to delete particle that doesn't exist");
            }
        }
        #endregion
        #region Helpers
        private void ResolveBoundariesStep() => boundaries.ResolveBoundaries(ref body, realHalfBoundSize);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ClearForceBuffers()
        {
            for (int i = 0; i < count; i++)
                _particles[i].forceBuffer = new(0, 0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ApplyForceBuffers()
        {
            for (int i = 0; i < count; i++)
                _particles[i].position += _particles[i].forceBuffer;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private FluidParticle ClearParticleExceptPos(FluidParticle particle)
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
            if (particleSP == null)
            {
                Debug.LogError("Simulation: GetDensity called before SetScene — particleSP is not initialized");
                return 0f;
            }

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

        private void CheckArraysLength()
        {
            if (maxParticles != _sparse.Length)
                Debug.LogWarning($"Simulation: number of max particles doesn't equal the length of sparse. Max particles: {maxParticles}, sparse: {_sparse.Length}");

            if (count != maxParticles - _freeIDs.Count)
                Debug.LogWarning($"Simulation: number of particles doesn't equal number of taken IDs. Num particles: {count}, taken IDs: {maxParticles - _freeIDs.Count}");
        }
        #endregion
        #region Debug
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

        private void LogPositions(Particle[] particles, string message)
        {
            StringBuilder sb = new();
            foreach (var p in particles)
                sb.Append($"({p.position.x}, {p.position.y}) + ");

            Debug.Log(message + ": " + sb.ToString());
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