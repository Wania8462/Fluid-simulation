using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;

// Shape ids, matching ShapeCube and ShapeSphere in RigidBodyMath3D.hlsl
public enum BodyShape3D
{
    Cube = 0,
    Sphere = 1
}

[Serializable]
public struct RigidBodySettings3D
{
    public BodyShape3D shape;
    [Tooltip("Sphere radius, or half the edge length of a cube")]
    public float bodyRadius;
    [Tooltip("Spacing between the boundary particles sampled on the surface")]
    public float sampleDensity;
    public float3 bodyPosition;
    [Tooltip("Euler angles in degrees")]
    public float3 bodyRotation;
    public float boundaryBodyMass;
}

// Mirrors RigidBodyProperties in RigidBodyMath3D.hlsl
[StructLayout(LayoutKind.Sequential)]
public struct RigidBodyProperties3D
{
    public float4 size;
    public float4 centerOfMass;
    public float4 invInertia;
    public uint shape;
    public uint start;
    public uint count;
    public float invMass;
}

// Mirrors RigidBodyState in RigidBodyMath3D.hlsl
[StructLayout(LayoutKind.Sequential)]
public struct RigidBodyState3D
{
    public float4 position;
    public float4 rotation;
    public float4 prevPosition;
    public float4 prevRotation;
    public float4 velocity;
    public float4 angularVelocity;
}

// CPU-side contents of the rigid body buffers, one entry per simulated body
public class RigidBodyData3D
{
    public RigidBodySettings3D[] settings;
    public int[] inspectorIndices;
    public RigidBodyProperties3D[] properties;
    public RigidBodyState3D[] states;
    public float4[] boundaryPositions;
    public float4[] boundaryRestPositions;
    public uint[] boundaryBodyIndices;

    public int NumBodies => properties.Length;
    public int NumBoundaryParticles => boundaryPositions.Length;
}

// Adding a shape: a BodyShape3D value, a sampler in Spawn3DParticles, cases in SampleSurface, SetMassProperties and
// SignedDistance below, and a signed distance with a mask term in RigidBodyMath3D.hlsl
public static class RigidBodies3D
{
    public const int MaxBodies = 10;

    // Threads per body in the strided reduction kernels, matching BodyReductionThreads in RigidBodyMath3D.hlsl
    public const int ReductionThreads = 64;

    // float4s per reduction slot, matching CouplingSlotSize and ContactSlotSize in RigidBodyKernels3D.hlsl
    public const int CouplingSlotSize = 2;
    public const int ContactSlotSize = 8;

    public static RigidBodyData3D Build(RigidBodySettings3D[] inspectorBodies, Spawn3DParticles spawn, float interactionRadius)
    {
        List<RigidBodySettings3D> settings = new();
        List<int> inspectorIndices = new();
        List<RigidBodyProperties3D> properties = new();
        List<RigidBodyState3D> states = new();
        List<float4> positions = new();
        List<float4> restPositions = new();
        List<uint> bodyIndices = new();

        int inspectorCount = inspectorBodies?.Length ?? 0;
        for (int i = 0; i < inspectorCount; i++)
        {
            RigidBodySettings3D body = inspectorBodies[i];

            if (settings.Count == MaxBodies)
            {
                Debug.LogError($"Rigid bodies: at most {MaxBodies} bodies are supported, ignoring body {i} and the ones after it");
                break;
            }

            if (body.bodyRadius <= 0 || body.sampleDensity <= 0)
            {
                Debug.LogError($"Rigid bodies: body {i} needs bodyRadius and sampleDensity above 0 (got {body.bodyRadius} and {body.sampleDensity}), ignoring it");
                continue;
            }

            if (body.sampleDensity >= interactionRadius)
                Debug.LogWarning($"Rigid bodies: body {i} has sampleDensity {body.sampleDensity} >= interactionRadius {interactionRadius}, its boundary particles won't see each other and their volumes will be infinite");

            float3[] samples = SampleSurface(body, spawn);
            quaternion rotation = GetRotation(body);

            RigidBodyProperties3D bodyProperties = new()
            {
                size = new float4(new float3(body.bodyRadius), 0),
                // Uniform cubes and spheres balance at their centre
                centerOfMass = float4.zero,
                shape = (uint)body.shape,
                start = (uint)positions.Count,
                count = (uint)samples.Length
            };
            SetMassProperties(ref bodyProperties, body);

            float4 centerOfMass = new(body.bodyPosition + math.mul(rotation, bodyProperties.centerOfMass.xyz), 0);
            states.Add(new RigidBodyState3D
            {
                position = centerOfMass,
                rotation = rotation.value,
                prevPosition = centerOfMass,
                prevRotation = rotation.value
            });

            foreach (float3 sample in samples)
            {
                float3 rest = sample - bodyProperties.centerOfMass.xyz;
                restPositions.Add(new float4(rest, 0));
                positions.Add(new float4(centerOfMass.xyz + math.mul(rotation, rest), 0));
                bodyIndices.Add((uint)settings.Count);
            }

            settings.Add(body);
            inspectorIndices.Add(i);
            properties.Add(bodyProperties);
        }

        RigidBodyData3D data = new()
        {
            settings = settings.ToArray(),
            inspectorIndices = inspectorIndices.ToArray(),
            properties = properties.ToArray(),
            states = states.ToArray(),
            boundaryPositions = positions.ToArray(),
            boundaryRestPositions = restPositions.ToArray(),
            boundaryBodyIndices = bodyIndices.ToArray()
        };

        WarnAboutOverlaps(data);
        return data;
    }

    // Uniform solids: sphere I = 2/5 m r^2, cube I = 1/6 m a^2 with edge a = 2r. A mass <= 0 falls back to the
    // boundary particle count, like 2D
    public static void SetMassProperties(ref RigidBodyProperties3D properties, RigidBodySettings3D body)
    {
        float mass = body.boundaryBodyMass > 0 ? body.boundaryBodyMass : properties.count;
        float r = body.bodyRadius;
        float inertia = body.shape switch
        {
            BodyShape3D.Cube => mass * (2 * r) * (2 * r) / 6,
            BodyShape3D.Sphere => 0.4f * mass * r * r,
            _ => throw new ArgumentOutOfRangeException(nameof(body), $"Rigid bodies: no inertia for shape {body.shape}")
        };

        properties.invMass = 1 / mass;
        properties.invInertia = new float4(new float3(1 / inertia), 0);
    }

    // Mass is the only body setting applied live; shape, size, sampling and pose need a reset
    public static void UpdateMasses(RigidBodyData3D data, RigidBodySettings3D[] inspectorBodies, ComputeBuffer propertiesBuffer)
    {
        if (data == null || data.NumBodies == 0 || inspectorBodies == null) return;

        for (int b = 0; b < data.NumBodies; b++)
        {
            int index = data.inspectorIndices[b];

            // The array was shortened in the inspector, the remaining bodies update on reset
            if (index >= inspectorBodies.Length) break;

            float mass = inspectorBodies[index].boundaryBodyMass;
            if (mass <= 0)
                Debug.LogWarning($"Rigid bodies: body {index} has boundaryBodyMass {mass}, falling back to its boundary particle count ({data.properties[b].count})");

            data.settings[b].boundaryBodyMass = mass;
            SetMassProperties(ref data.properties[b], data.settings[b]);
        }

        propertiesBuffer.SetData(data.properties);
    }

    // Every buffer gets at least one element so it exists without bodies. BoundaryCellStart always spans the whole
    // grid because DoubleDensityRelaxation indexes it by cell
    public static void CreateBuffers(Dictionary<string, ComputeBuffer> buffers, RigidBodyData3D data, int numCells)
    {
        int boundaryCount = math.max(data.NumBoundaryParticles, 1);
        int slotCount = math.max(data.NumBodies, 1) * ReductionThreads;

        buffers["BoundaryPositions"] = CreateNonEmptyBuffer(data.boundaryPositions);
        buffers["BoundaryVelocities"] = ComputeHelper.CreateStructuredBufferWithData<float4>(boundaryCount);
        buffers["BoundaryRestPositions"] = CreateNonEmptyBuffer(data.boundaryRestPositions);
        buffers["BoundaryVolumes"] = ComputeHelper.CreateStructuredBufferWithData<float>(boundaryCount);
        buffers["BoundaryForceBuffersX"] = ComputeHelper.CreateStructuredBufferWithData<int>(boundaryCount);
        buffers["BoundaryForceBuffersY"] = ComputeHelper.CreateStructuredBufferWithData<int>(boundaryCount);
        buffers["BoundaryForceBuffersZ"] = ComputeHelper.CreateStructuredBufferWithData<int>(boundaryCount);
        buffers["BoundaryBodyIndices"] = CreateNonEmptyBuffer(data.boundaryBodyIndices);
        buffers["BoundaryCellStart"] = ComputeHelper.CreateStructuredBufferWithData<uint>(numCells + 1);
        buffers["BoundarySortedIndices"] = ComputeHelper.CreateStructuredBufferWithData<uint>(boundaryCount);

        buffers["BodyProperties"] = CreateNonEmptyBuffer(data.properties);
        buffers["BodyStates"] = CreateNonEmptyBuffer(data.states);
        buffers["BodyCouplingSums"] = ComputeHelper.CreateStructuredBufferWithData<float4>(slotCount * CouplingSlotSize);
        buffers["BodyContactSums"] = ComputeHelper.CreateStructuredBufferWithData<float4>(slotCount * ContactSlotSize);

        // Read-only aliases share the instances above, see the note in Simulation.compute
        buffers["BoundaryPositionsRO"] = buffers["BoundaryPositions"];
        buffers["BoundaryVelocitiesRO"] = buffers["BoundaryVelocities"];
        buffers["BoundaryVolumesRO"] = buffers["BoundaryVolumes"];
        buffers["BoundaryCellStartRO"] = buffers["BoundaryCellStart"];
        buffers["BoundarySortedIndicesRO"] = buffers["BoundarySortedIndices"];
    }

    // Drops fluid particles closer than margin to a body's surface, so they don't start squeezed against it
    public static float4[] RemoveParticlesInsideBodies(float4[] positions, RigidBodySettings3D[] bodies, float margin)
    {
        if (bodies.Length == 0) return positions;

        quaternion[] inverseRotations = new quaternion[bodies.Length];
        for (int b = 0; b < bodies.Length; b++)
            inverseRotations[b] = math.inverse(GetRotation(bodies[b]));

        List<float4> kept = new(positions.Length);
        foreach (float4 position in positions)
        {
            bool inside = false;
            for (int b = 0; b < bodies.Length && !inside; b++)
            {
                float3 local = math.mul(inverseRotations[b], position.xyz - bodies[b].bodyPosition);
                inside = SignedDistance(bodies[b], local) < margin;
            }

            if (!inside)
                kept.Add(position);
        }

        return kept.ToArray();
    }

    // Bodies that start inside each other can't be pushed apart cleanly, so point them out. Adding an element to the
    // bodies array in the inspector copies the last one, which puts the new body exactly on top of it
    private static void WarnAboutOverlaps(RigidBodyData3D data)
    {
        for (int a = 0; a < data.NumBodies; a++)
        {
            for (int b = a + 1; b < data.NumBodies; b++)
            {
                if (StartsInside(data, a, b) || StartsInside(data, b, a))
                    Debug.LogWarning($"Rigid bodies: bodies {data.inspectorIndices[a]} and {data.inspectorIndices[b]} start inside each other, move them apart");
            }
        }
    }

    // Whether any boundary particle of body a starts inside body b
    private static bool StartsInside(RigidBodyData3D data, int a, int b)
    {
        RigidBodySettings3D other = data.settings[b];
        quaternion inverseRotation = math.inverse(GetRotation(other));
        RigidBodyProperties3D body = data.properties[a];

        for (uint i = body.start; i < body.start + body.count; i++)
        {
            float3 local = math.mul(inverseRotation, data.boundaryPositions[i].xyz - other.bodyPosition);
            if (SignedDistance(other, local) < 0)
                return true;
        }

        return false;
    }

    private static float3[] SampleSurface(RigidBodySettings3D body, Spawn3DParticles spawn) => body.shape switch
    {
        BodyShape3D.Cube => spawn.InitCubeSurfacePositions(body.bodyRadius, body.sampleDensity),
        BodyShape3D.Sphere => spawn.InitSphereSurfacePositions(body.bodyRadius, body.sampleDensity),
        _ => throw new ArgumentOutOfRangeException(nameof(body), $"Rigid bodies: no sampler for shape {body.shape}")
    };

    // CPU versions of the shape signed distances in RigidBodyMath3D.hlsl, for a shape-local position
    private static float SignedDistance(RigidBodySettings3D body, float3 local) => body.shape switch
    {
        BodyShape3D.Cube => BoxSignedDistance(local, new float3(body.bodyRadius)),
        BodyShape3D.Sphere => math.length(local) - body.bodyRadius,
        _ => throw new ArgumentOutOfRangeException(nameof(body), $"Rigid bodies: no signed distance for shape {body.shape}")
    };

    private static float BoxSignedDistance(float3 local, float3 halfExtents)
    {
        float3 q = math.abs(local) - halfExtents;
        return math.length(math.max(q, 0f)) + math.min(math.cmax(q), 0f);
    }

    private static quaternion GetRotation(RigidBodySettings3D body) => quaternion.Euler(math.radians(body.bodyRotation));

    private static ComputeBuffer CreateNonEmptyBuffer<T>(T[] data) where T : struct =>
        data.Length > 0
            ? ComputeHelper.CreateStructuredBufferWithData(data)
            : ComputeHelper.CreateStructuredBufferWithData<T>(1);
}
