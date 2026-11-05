using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;

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
    [Tooltip("One way coupling: the fluid and the other bodies are blocked by this one, but nothing ever moves it. Its mass is ignored")]
    public bool isStatic;

    public bool IsEmpty => bodyRadius == 0;
}

// Layouts must match the HLSL structs in RigidBodyMath3D.hlsl
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

public class RigidBodyData3D
{
    public List<RigidBodySettings3D> settings = new();
    // Slot of each body in the inspector's bodies array
    public List<int> inspectorIndices = new();
    public List<RigidBodyProperties3D> properties = new();
    public List<RigidBodyState3D> states = new();
    public List<float4> boundaryPositions = new();
    public List<float4> boundaryRestPositions = new();
    public List<uint> boundaryBodyIndices = new();

    public int NumBodies => properties.Count;
    public int NumBoundaryParticles => boundaryPositions.Count;
}

public static class RigidBodies3D
{
    public const int MaxBodies = 10;

    // Must match BodyReductionThreads, CouplingSlotSize and ContactSlotSize in the shaders
    public const int ReductionThreads = 64;

    public const int CouplingSlotSize = 2;
    public const int ContactSlotSize = 16;

    public static RigidBodyData3D Build(RigidBodySettings3D[] inspectorBodies, Spawn3DParticles spawn, float interactionRadius, Tank tank)
    {
        RigidBodyData3D data = new();

        int inspectorCount = inspectorBodies?.Length ?? 0;
        for (int i = 0; i < inspectorCount; i++)
        {
            RigidBodySettings3D body = inspectorBodies[i];

            if (body.IsEmpty)
                continue;

            if (data.NumBodies == MaxBodies)
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

            AddBody(data, body, i, spawn);
        }

        WarnAboutWalls(data, tank.innerHalfSize);
        WarnAboutOverlaps(data);
        return data;
    }

    public static void AddBody(RigidBodyData3D data, RigidBodySettings3D body, int inspectorIndex, Spawn3DParticles spawn)
    {
        uint bodyIndex = (uint)data.NumBodies;
        float3[] samples = SampleSurface(body, spawn);
        quaternion rotation = GetRotation(body);

        RigidBodyProperties3D bodyProperties = new()
        {
            size = new float4(new float3(body.bodyRadius), 0),
            centerOfMass = float4.zero,
            shape = (uint)body.shape,
            start = (uint)data.boundaryPositions.Count,
            count = (uint)samples.Length
        };
        SetMassProperties(ref bodyProperties, body);

        float4 centerOfMass = new(body.bodyPosition + math.mul(rotation, bodyProperties.centerOfMass.xyz), 0);
        data.states.Add(new RigidBodyState3D
        {
            position = centerOfMass,
            rotation = rotation.value,
            prevPosition = centerOfMass,
            prevRotation = rotation.value
        });

        foreach (float3 sample in samples)
        {
            float3 rest = sample - bodyProperties.centerOfMass.xyz;
            data.boundaryRestPositions.Add(new float4(rest, 0));
            data.boundaryPositions.Add(new float4(centerOfMass.xyz + math.mul(rotation, rest), 0));
            data.boundaryBodyIndices.Add(bodyIndex);
        }

        data.settings.Add(body);
        data.inspectorIndices.Add(inspectorIndex);
        data.properties.Add(bodyProperties);
    }

    public static void RemoveBody(RigidBodyData3D data, int slot)
    {
        int index = data.inspectorIndices.IndexOf(slot);
        if (index < 0) return;

        RigidBodyProperties3D removed = data.properties[index];
        int start = (int)removed.start;
        int count = (int)removed.count;
        data.boundaryPositions.RemoveRange(start, count);
        data.boundaryRestPositions.RemoveRange(start, count);
        data.boundaryBodyIndices.RemoveRange(start, count);

        // Later bodies shift down one index
        for (int i = start; i < data.NumBoundaryParticles; i++)
            data.boundaryBodyIndices[i]--;

        data.settings.RemoveAt(index);
        data.inspectorIndices.RemoveAt(index);
        data.properties.RemoveAt(index);
        data.states.RemoveAt(index);

        for (int b = index; b < data.NumBodies; b++)
        {
            RigidBodyProperties3D properties = data.properties[b];
            properties.start -= removed.count;
            data.properties[b] = properties;
        }
    }

    public static void SetMassProperties(ref RigidBodyProperties3D properties, RigidBodySettings3D body)
    {
        if (body.isStatic)
        {
            properties.invMass = 0;
            properties.invInertia = float4.zero;
            return;
        }

        float mass = body.boundaryBodyMass > 0 ? body.boundaryBodyMass : properties.count;
        float r = body.bodyRadius;
        float inertia = body.shape switch
        {
            // Solid cube: m * edge^2 / 6, solid sphere: 2/5 * m * r^2
            BodyShape3D.Cube => mass * (2 * r) * (2 * r) / 6,
            BodyShape3D.Sphere => 0.4f * mass * r * r,
            _ => throw new ArgumentOutOfRangeException(nameof(body), $"Rigid bodies: no inertia for shape {body.shape}")
        };

        properties.invMass = 1 / mass;
        properties.invInertia = new float4(new float3(1 / inertia), 0);
    }

    public static void UpdateMasses(RigidBodyData3D data, RigidBodySettings3D[] inspectorBodies, ComputeBuffer propertiesBuffer)
    {
        if (data == null || data.NumBodies == 0 || inspectorBodies == null) return;

        for (int b = 0; b < data.NumBodies; b++)
        {
            int index = data.inspectorIndices[b];

            if (index >= inspectorBodies.Length) continue;

            float mass = inspectorBodies[index].boundaryBodyMass;
            if (mass <= 0 && !inspectorBodies[index].isStatic)
                Debug.LogWarning($"Rigid bodies: body {index} has boundaryBodyMass {mass}, falling back to its boundary particle count ({data.properties[b].count})");

            RigidBodySettings3D body = data.settings[b];
            body.boundaryBodyMass = mass;
            body.isStatic = inspectorBodies[index].isStatic;
            data.settings[b] = body;

            RigidBodyProperties3D properties = data.properties[b];
            SetMassProperties(ref properties, body);
            data.properties[b] = properties;
        }

        propertiesBuffer.SetData(data.properties);
    }

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

        buffers["BoundaryPositionsRO"] = buffers["BoundaryPositions"];
        buffers["BoundaryVelocitiesRO"] = buffers["BoundaryVelocities"];
        buffers["BoundaryVolumesRO"] = buffers["BoundaryVolumes"];
        buffers["BoundaryCellStartRO"] = buffers["BoundaryCellStart"];
        buffers["BoundarySortedIndicesRO"] = buffers["BoundarySortedIndices"];
    }

    public static float4[] RemoveParticlesInsideBodies(float4[] positions, List<RigidBodySettings3D> bodies, float margin)
    {
        if (bodies.Count == 0) return positions;

        quaternion[] inverseRotations = new quaternion[bodies.Count];
        for (int b = 0; b < bodies.Count; b++)
            inverseRotations[b] = math.inverse(GetRotation(bodies[b]));

        List<float4> kept = new(positions.Length);
        foreach (float4 position in positions)
        {
            bool inside = false;
            for (int b = 0; b < bodies.Count && !inside; b++)
            {
                float3 local = math.mul(inverseRotations[b], position.xyz - bodies[b].bodyPosition);
                inside = SignedDistance(bodies[b], local) < margin;
            }

            if (!inside)
                kept.Add(position);
        }

        return kept.ToArray();
    }

    private static void WarnAboutWalls(RigidBodyData3D data, float3 innerHalfSize)
    {
        for (int b = 0; b < data.NumBodies; b++)
        {
            RigidBodyProperties3D body = data.properties[b];
            float3 extent = float3.zero;

            for (int i = (int)body.start; i < body.start + body.count; i++)
                extent = math.max(extent, math.abs(data.boundaryPositions[i].xyz));

            if (math.any(extent > innerHalfSize))
                Debug.LogWarning($"Rigid bodies: body {data.inspectorIndices[b]} starts partly outside the tank and will be pushed back in. Its surface has to stay within x ±{innerHalfSize.x}, y ±{innerHalfSize.y}, z ±{innerHalfSize.z}");
        }
    }

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

    private static bool StartsInside(RigidBodyData3D data, int a, int b)
    {
        RigidBodySettings3D other = data.settings[b];
        quaternion inverseRotation = math.inverse(GetRotation(other));
        RigidBodyProperties3D body = data.properties[a];

        for (int i = (int)body.start; i < body.start + body.count; i++)
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

    // Compute buffers can't be empty, so an empty list gets one zeroed element
    private static ComputeBuffer CreateNonEmptyBuffer<T>(List<T> data) where T : struct =>
        data.Count > 0
            ? ComputeHelper.CreateStructuredBufferWithData(data.ToArray())
            : ComputeHelper.CreateStructuredBufferWithData<T>(1);
}
