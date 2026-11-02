// Continuous flow kernels, included at the end of Simulation.compute, which declares every buffer, uniform and
// helper they use. Only dispatched when the simulation has a FluidFlow3D.
//
// The flow is a closed loop at a constant particle count: a reservoir of water sits above the scene and drains
// through a hole in its floor, and whatever falls past drainHeight is put back into the top of the reservoir.
// Nothing is created or destroyed, so numParticles stays fixed and the renderers keep their sizes.

// Three decorrelated floats in [0, 1) from a particle index and a per-dispatch seed. A hash rather than a
// lattice slot because the slab the particles land in sits above the reservoir's surface, where they fall
// through open air and their spacing doesn't matter
float3 Hash3(uint index, uint seed)
{
    uint h = index * 747796405u + seed * 2891336453u;
    h ^= h >> 16;
    h *= 2246822519u;
    h ^= h >> 13;
    h *= 3266489917u;

    // Three different shifts of the same avalanche, scaled into [0, 1)
    uint3 bits = uint3(h, h * 2654435761u, h * 40503u);
    return float3(bits >> 8) / 16777216.0;
}

// The drain and the pool in one pass: a particle below drainHeight reappears in a slab across the top of the
// reservoir, stopped, and falls back in.
//
// Two things about where this is dispatched from are load bearing. It runs before AdvancePredictedPositions,
// which is what writes PrevPositions, because CalculateVelocity is (Positions - PrevPositions) / dt and a
// teleport against a stale PrevPositions would come out as an enormous velocity. And it only runs on a step
// that rebuilds the grid, because on the other steps the particle would sit in the cell it was drained from,
// invisible to its new neighbours and they to it, and drop straight back through the reservoir floor
[numthreads(64,1,1)]
void RecycleParticles (uint3 id : SV_DispatchThreadID)
{
    if (id.x >= numParticles) return;
    if (Positions[id.x].y > drainHeight) return;

    float3 inner = reservoirHalfSize.xyz - particleRadius;
    float3 low = reservoirCenter.xyz - inner;
    float3 high = reservoirCenter.xyz + inner;
    low.y = high.y - respawnSlabHeight;

    Positions[id.x] = float4(lerp(low, high, Hash3(id.x, recycleSeed)), 0);
    Velocities[id.x] = 0;
}

// The reservoir's walls and its floor, as a positional clamp with no geometry and no boundary particles behind
// it. The interior counterpart of ResolveBoundaries, dispatched right after it for the same reason: the
// correction lands before CalculateVelocity, so being stopped by a wall turns into the loss of velocity itself
[numthreads(64,1,1)]
void ResolveReservoir (uint3 id : SV_DispatchThreadID)
{
    if (id.x >= numParticles) return;

    float3 position = Positions[id.x].xyz;
    float3 offsetFromCentre = position - reservoirCenter.xyz;

    // Not this reservoir's fluid: the falling stream, and everything that has already left through the nozzle
    if (any(abs(offsetFromCentre) > reservoirHalfSize.xyz)) return;

    // Where a particle centre touches a wall, so the shell this clamps within is one particle thick, as it is
    // for the tank walls
    float3 inner = reservoirHalfSize.xyz - particleRadius;

    if (abs(offsetFromCentre.x) >= inner.x)
        position.x = reservoirCenter.x + (inner.x - collisionDamp) * sign(offsetFromCentre.x);

    if (abs(offsetFromCentre.z) >= inner.z)
        position.z = reservoirCenter.z + (inner.z - collisionDamp) * sign(offsetFromCentre.z);

    // The floor holds everything except what is over the nozzle. There is no ceiling: the top is open, which
    // is what lets the recycled particles rain in
    if (offsetFromCentre.y <= -inner.y && length(offsetFromCentre.xz) > nozzleRadius)
        position.y = reservoirCenter.y - inner.y + collisionDamp;

    Positions[id.x] = float4(position, 0);
}
