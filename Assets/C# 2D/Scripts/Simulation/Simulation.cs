using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
        public RefList<int> neighbours;
    }

    public class FluidParticle : Particle
    {
        public int ID;
        public float density;
        public float nearDensity;
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
        public float2 restPosition;
        public float psi;

        public BoundaryParticle(float2 position)
        {
            this.position = position;
            prevPosition = position;
            restPosition = position;
            neighbours = new RefList<int>();
        }

        public BoundaryParticle(float2 position, float volume)
        {
            this.position = position;
            prevPosition = position;
            restPosition = position;
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

        // Boundary object
        private float boundaryFriction;
        private float boundaryObjectMass;

        // References
        private InitializeParticles initParticles;
        private Boundaries boundaries;

        // Spatial partitioning grids
        private SpatialPartitioning particleSP;
        private SpatialPartitioning springsSP;
        private SpatialPartitioning boundarySP;

        // Fluid particles buffers
        public int maxParticles { get; private set; }
        public FluidParticle[] _particles { get; private set; }
        public SparseArray _sparse { get; private set; }
        public int count { get; private set; }
        private RefList<int> _freeIDs;

        // Other buffers
        public RefList<BoundaryParticle> _boundaryParticles { get; private set; }
        private ConcurrentDictionary<(int, int), float> _springs;
        private List<int> bodyNeighbours;
        private ThreadLocal<List<int>> densityNeighbours;

        // Boundary
        private float2 boundaryRestCenter;
        private float2 boundaryCenter;
        private float boundaryAngle;
        private bool deformableBoundaryObject;

        // Miscellaneous
        public float2 realHalfBoundSize;
        private const float particleRadius = 0.5f;
        private static readonly object lockObject = new();
        private float timer;
        private float dt;

        public Simulation(SimulationSettings settings, InitializeParticles spawn)
        {
            initParticles = spawn;
            UpdateSettings(settings);
        }

        #region Simulation

        public void SimulationStep(float2 mousePos, float deltatime)
        {
            dt = deltatime;
            if (CheckDeltaTime()) return;
            CheckArraysLength();

            if (flow) HandleFlow();

            Watcher.ExecuteWithTimer("3. Init", InitSpatialPartitioning);
            Watcher.ExecuteWithTimer("4. GetNeighbours", SetNeighbours);

            Watcher.ExecuteWithTimer("5. ExternalForces", ExternalForces);
            Watcher.ExecuteWithTimer("6. ApplyViscosity", ApplyViscosity);

            Watcher.ExecuteWithTimer("7. Advance predicted pos", AdvancePredictedPositions);

            Watcher.ExecuteWithTimer("8. Adjust springs", AdjustSprings);
            Watcher.ExecuteWithTimer("9. Spring displacements", SpringDisplacements);

            Watcher.ExecuteWithTimer("10. DoubleDensityRelaxation", DoubleDensityRelaxation);

            AttractToMouse(mousePos);

            boundaries.ResolveBoundaries(realHalfBoundSize);
            Watcher.ExecuteWithTimer("11. Resolve boundary particles", ResolveBoundaryObject);

            Watcher.ExecuteWithTimer("12. Calculate velocity", CalculateVelocities);

            if (flow)
                Watcher.ExecuteWithTimer("13. Init", ResolveFlow);
        }

        private void AdvancePredictedPositions()
        {
            ForEachParticle(p =>
            {
                p.prevPosition = p.position;
                p.position += dt * p.velocity;
            });
        }

        private void CalculateVelocities()
        {
            ForEachParticle(p => p.velocity = (p.position - p.prevPosition) / dt);
        }

        private void ExternalForces()
        {
            ForEachParticle(p => p.velocity.y += dt * gravity);
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

                float boundaryPressure = math.max(pressure, 0f);
                float boundaryNearPressure = math.max(nearPressure, 0f);

                foreach (var b in p.boundaryNeighbours)
                {
                    float mag = FluidMath.Distance(p.position, _boundaryParticles[b].position);
                    if (mag == 0 || mag > interactionRadius) continue;
                    float q = mag / interactionRadius;

                    float2 r = FluidMath.UnitVector(p.position, _boundaryParticles[b].position, mag);
                    float2 displacement = FluidMath.PressureDisplacement(
                        dt,
                        q,
                        boundaryPressure,
                        boundaryNearPressure,
                        r);

                    float psi = restDensity * _boundaryParticles[b].volume;
                    lock (lockObject) { p.forceBuffer -= psi * displacement; }
                    lock (lockObject) { _boundaryParticles[b].forceBuffer += psi * displacement; }
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

                foreach (var b in p.boundaryNeighbours)
                {
                    var mag = FluidMath.Distance(p.position, _boundaryParticles[b].position);
                    if (mag > interactionRadius || mag == 0) continue;

                    var q = mag / interactionRadius;
                    var r = FluidMath.UnitVector(p.position, _boundaryParticles[b].position, mag);
                    var inwardVelocity = math.dot(p.velocity - _boundaryParticles[b].velocity, r);
                    if (!(inwardVelocity > 0)) continue;

                    var impulse = FluidMath.ViscosityImpulse(dt,
                        boundaryFriction,
                        0f,
                        q,
                        inwardVelocity,
                        r);

                    p.velocity -= impulse;
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

        private void ResolveBoundaryObject()
        {
            if (_boundaryParticles.Count == 0) return;

            // Maintain the shape
            float angle = FindRotationAngle();
            PlaceBoundaryParticles(angle);

            if (deformableBoundaryObject)
                boundaries.ResolveBoundaryParticleCollisions(realHalfBoundSize, _boundaryParticles);
            else
                RigidContactResolution();
        }

        private float FindRotationAngle()
        {
            int n = _boundaryParticles.Count;
            float2 center = float2.zero;

            for (int i = 0; i < n; i++)
                center += _boundaryParticles[i].position;

            center /= n;

            float cosPart = 0f;
            float sinPart = 0f;
            for (int i = 0; i < n; i++)
            {
                float2 centerDisp = _boundaryParticles[i].position - center;
                float2 restDisp = _boundaryParticles[i].restPosition - boundaryRestCenter;
                cosPart += math.dot(centerDisp, restDisp);
                sinPart += centerDisp.y * restDisp.x - centerDisp.x * restDisp.y;
            }

            boundaryCenter = center;
            return math.atan2(sinPart, cosPart);
        }

        private void PlaceBoundaryParticles(float angle)
        {
            float cos = math.cos(angle);
            float sin = math.sin(angle);
            for (int i = 0; i < _boundaryParticles.Count; i++)
            {
                float2 restDisp = _boundaryParticles[i].restPosition - boundaryRestCenter;
                _boundaryParticles[i].position = boundaryCenter + new float2(cos * restDisp.x - sin * restDisp.y,
                                                                     sin * restDisp.x + cos * restDisp.y);
            }
        }

        private void RigidContactResolution()
        {
            int n = _boundaryParticles.Count;
            if (n == 0) return;

            // Pass 1: mean push-out and contact centroid.
            float2 sumDelta = float2.zero;
            float2 sumContactPos = float2.zero;
            int contacts = 0;
            for (int i = 0; i < n; i++)
            {
                float2 delta = WallPushOut(_boundaryParticles[i].position);
                if (delta.x == 0 && delta.y == 0) continue;

                sumDelta += delta;
                sumContactPos += _boundaryParticles[i].position;
                contacts++;
            }

            if (contacts == 0) return; // no wall contact this frame — nothing to resolve

            float2 translation = sumDelta / contacts;       // Δc: average push-out
            float2 pivot = sumContactPos / contacts;         // P: contact point to rotate about

            // Pass 2: levelling rotation (least-squares fit about the pivot) and inertia about the pivot.
            // Since Σ(posᵢ - P) = 0 over the contacts, the fit reduces to Σ r×Δ / Σ|r|².
            float levelTorque = 0f;
            float levelDenom = 0f;
            float inertiaPivot = 0f; // Σ |posⱼ - P|² over ALL particles, for the gravity torque
            for (int i = 0; i < n; i++)
            {
                float2 r = _boundaryParticles[i].position - pivot;
                inertiaPivot += r.x * r.x + r.y * r.y;

                float2 delta = WallPushOut(_boundaryParticles[i].position);
                if (delta.x == 0 && delta.y == 0) continue;

                levelTorque += r.x * delta.y - r.y * delta.x;
                levelDenom += r.x * r.x + r.y * r.y;
            }

            float levelAngle = levelDenom > 1e-6f ? levelTorque / levelDenom : 0f;

            // Gravity torque about the pivot: τ = (com - P) × (0, n·gravity). Applied as a position-level
            // angle increment (∝ dt²) so the per-particle velocities carry the angular momentum forward,
            // giving a constant angular acceleration τ / I — the object tips faster as its weight falls.
            float2 arm = boundaryCenter - pivot;
            float gravityTorque = arm.x * (n * gravity);
            float gravityAngle = inertiaPivot > 1e-6f ? gravityTorque / inertiaPivot * dt * dt : 0f;

            float deltaAngle = levelAngle + gravityAngle;

            // Apply: rotate the body about the pivot by deltaAngle, then translate by the push-out.
            float cos = math.cos(deltaAngle);
            float sin = math.sin(deltaAngle);
            float2 c = boundaryCenter - pivot;
            boundaryCenter = pivot + new float2(cos * c.x - sin * c.y, sin * c.x + cos * c.y) + translation;
            boundaryAngle += deltaAngle;

            PlaceBoundaryParticles(boundaryAngle);
        }

        // Returns the vector that pushes a point back inside the container walls (zero if already inside).
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float2 WallPushOut(float2 pos)
        {
            float2 delta = float2.zero;

            if (math.abs(pos.x) > realHalfBoundSize.x)
                delta.x = math.sign(pos.x) * realHalfBoundSize.x - pos.x;

            if (math.abs(pos.y) > realHalfBoundSize.y)
                delta.y = math.sign(pos.y) * realHalfBoundSize.y - pos.y;

            return delta;
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
        }

        #endregion
        #region Flow
        private void HandleFlow()
        {
            timer += dt;

            if (timer >= initParticles.spawnInterval)
            {
                timer -= initParticles.spawnInterval;
                Watcher.ExecuteWithTimer("2. SpawnFlowParticles", SpawnFlowParticles);
            }
        }
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

            if (realHalfBoundSize.x <= 0 || realHalfBoundSize.y <= 0)
                Debug.LogWarning($"Simulation: realHalfBoundSize is {realHalfBoundSize}, bounding box is degenerate — particles may escape or behave incorrectly");

            particleSP = new SpatialPartitioning(-realHalfBoundSize, realHalfBoundSize, interactionRadius);
            springsSP = new SpatialPartitioning(-realHalfBoundSize, realHalfBoundSize, springInteractionRadius + 0.5f);
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

            boundaries = new Boundaries(_particles, count, particleRadius, collisionDamp);
            CreateObject();
        }

        // Don't forget to add changable sampling density
        private void CreateObject()
        {
            // Test boundaries make it look better before pushing to main
            _boundaryParticles = new();
            // var boundPos = initParticles.InitCircleOutlinePositions(10, sampleDensity: 1, new(60, -80));
            var boundPos = initParticles.InitRectangleOutlinePositions(20, 20, 1, new(60, -80), math.PI / 5);
            int nbBoundaryParticles = boundPos.Count;

            for (int i = 0; i < nbBoundaryParticles; i++)
                _boundaryParticles.Add(new(boundPos[i]));

            // Rest center of mass of the boundary object, used as the reference for shape matching.
            float2 restCenter = float2.zero;
            for (int i = 0; i < nbBoundaryParticles; i++)
                restCenter += _boundaryParticles[i].restPosition;
            boundaryRestCenter = nbBoundaryParticles > 0 ? restCenter / nbBoundaryParticles : float2.zero;

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

            if (settings.boundaryFriction <= 0)
                Debug.LogWarning($"Simulation: boundaryFriction is {settings.boundaryFriction}, fluid particles won't be slowed near the boundary object and may tunnel through it");

            if (settings.boundaryObjectMass <= 0)
                Debug.LogWarning($"Simulation: boundaryObjectMass is {settings.boundaryObjectMass}, falling back to the neutral response (mass = boundary particle count)");

            deformableBoundaryObject = settings.deformableBoundaryObject;
            boundaryFriction = settings.boundaryFriction;
            boundaryObjectMass = settings.boundaryObjectMass;
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
        private void InitSpatialPartitioning()
        {
            // todo: evaluate if I need 3 seperate SP
            particleSP.Init(_particles.AsSpan(0, count));
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ForEachParticle(Action<Particle> action)
        {
            for (int i = 0; i < count; i++)
                action(_particles[i]);

            for (int i = 0; i < _boundaryParticles.Count; i++)
                action(_boundaryParticles[i]);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ClearForceBuffers() => ForEachParticle(p => p.forceBuffer = new(0, 0));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ApplyForceBuffers()
        {
            for (int i = 0; i < count; i++)
                _particles[i].position += _particles[i].forceBuffer;

            int n = _boundaryParticles.Count;
            if (n == 0) return;

            float invMass = boundaryObjectMass > 0 ? n / boundaryObjectMass : 1f;
            for (int i = 0; i < n; i++)
                _boundaryParticles[i].position += invMass * _boundaryParticles[i].forceBuffer;
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

        private bool CheckDeltaTime()
        {
            if (dt <= 0)
            {
                Debug.LogWarning($"Simulation: deltatime is too small. Deltatime: {dt}");
                return true;
            }

            if (dt <= 0)
            {
                Debug.LogWarning($"Simulation: deltatime is too large. Deltatime: {dt}");
                return true;
            }

            return false;
        }
        #endregion
        #region Debug

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