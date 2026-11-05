// PCG-style integer hash mapped to three floats in [0, 1)
float3 Hash3(uint index, uint seed)
{
    uint h = index * 747796405u + seed * 2891336453u;
    h ^= h >> 16;
    h *= 2246822519u;
    h ^= h >> 13;
    h *= 3266489917u;

    uint3 bits = uint3(h, h * 2654435761u, h * 40503u);
    return float3(bits >> 8) / 16777216.0;
}

[numthreads(64,1,1)]
void RecycleParticles (uint3 id : SV_DispatchThreadID)
{
    if (id.x >= numParticles) return;
    if (Positions[id.x].y > drainHeight) return;

    float3 inner = reservoirHalfSize.xyz - particleRadius;
    float3 low = reservoirCenter.xyz - inner;
    float3 high = reservoirCenter.xyz + inner;
    // Respawn in a thin slab at the top of the reservoir
    low.y = high.y - respawnSlabHeight;

    Positions[id.x] = float4(lerp(low, high, Hash3(id.x, recycleSeed)), 0);
    Velocities[id.x] = 0;
}

[numthreads(64,1,1)]
void ResolveReservoir (uint3 id : SV_DispatchThreadID)
{
    if (id.x >= numParticles) return;

    float3 position = Positions[id.x].xyz;
    float3 offsetFromCentre = position - reservoirCenter.xyz;

    // Only particles inside the reservoir are confined
    if (any(abs(offsetFromCentre) > reservoirHalfSize.xyz)) return;

    float3 inner = reservoirHalfSize.xyz - particleRadius;

    if (abs(offsetFromCentre.x) >= inner.x)
        position.x = reservoirCenter.x + (inner.x - collisionDamp) * sign(offsetFromCentre.x);

    if (abs(offsetFromCentre.z) >= inner.z)
        position.z = reservoirCenter.z + (inner.z - collisionDamp) * sign(offsetFromCentre.z);

    // The floor is solid except for the nozzle hole
    if (offsetFromCentre.y <= -inner.y && length(offsetFromCentre.xz) > nozzleRadius)
        position.y = reservoirCenter.y - inner.y + collisionDamp;

    Positions[id.x] = float4(position, 0);
}
