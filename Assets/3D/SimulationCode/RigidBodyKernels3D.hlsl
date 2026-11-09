// Included from Simulation.compute, which declares all buffers, uniforms and kernel pragmas
#define CouplingSlotSize 2

// Contacts are grouped per wall axis (x, y, z) plus one group for other bodies.
// Each group's slot holds push (w = contact count), friction, torque and approach
#define ContactGroups 4
#define ContactSlotSize 16

void SetBoundaryForceBuffer(uint index, float4 value)
{
    BoundaryForceBuffersX[index] = (int)(value.x * FloatScale);
    BoundaryForceBuffersY[index] = (int)(value.y * FloatScale);
    BoundaryForceBuffersZ[index] = (int)(value.z * FloatScale);
}

void AtomicAddBoundaryForceBuffer(uint index, float4 value)
{
    InterlockedAdd(BoundaryForceBuffersX[index], (int)(value.x * FloatScale));
    InterlockedAdd(BoundaryForceBuffersY[index], (int)(value.y * FloatScale));
    InterlockedAdd(BoundaryForceBuffersZ[index], (int)(value.z * FloatScale));
}

float3 ReadBoundaryForceBuffer(uint index)
{
    return float3((float)BoundaryForceBuffersX[index],
                  (float)BoundaryForceBuffersY[index],
                  (float)BoundaryForceBuffersZ[index]) / FloatScale;
}

void AddBoundaryDensity(float4 position, uint3 cellPosition, inout float density, inout float4 gradient)
{
    for (uint i = 0; i < 27; i++)
    {
        int3 cellOffset = int3(i % 3, (i / 3) % 3, i / 9) - 1;
        uint3 neighbourPos = cellPosition + cellOffset;
        if (neighbourPos.x >= columns || neighbourPos.y >= rows || neighbourPos.z >= layers) continue;
        uint cell = GetCellIndex(neighbourPos);

        for (uint j = BoundaryCellStartRO[cell]; j < BoundaryCellStartRO[cell + 1]; j++)
        {
            uint boundary = BoundarySortedIndicesRO[j];
            float4 boundaryPosition = BoundaryPositionsRO[boundary];
            float magSq = DistanceSq(position, boundaryPosition);
            float mag = sqrt(magSq);

            // Branchless mask: 0 when the particles coincide or are out of range
            float w = step(1e-12, magSq) * step(magSq, interactionRadiusSq);
            float volume = BoundaryVolumesRO[boundary];
            float4 r = (boundaryPosition - position) / max(mag, 1e-9);

            density += w * volume * Poly6(magSq, interactionRadiusSq, poly6Factor);
            gradient += w * volume * SpikyGradient(mag, interactionRadius, spikyGradFactor, r);
        }
    }
}

void AddBoundaryCorrection(float4 position, uint3 cellPosition, float lambda, inout float4 delta)
{
    for (uint i = 0; i < 27; i++)
    {
        int3 cellOffset = int3(i % 3, (i / 3) % 3, i / 9) - 1;
        uint3 neighbourPos = cellPosition + cellOffset;
        if (neighbourPos.x >= columns || neighbourPos.y >= rows || neighbourPos.z >= layers) continue;
        uint cell = GetCellIndex(neighbourPos);

        for (uint j = BoundaryCellStartRO[cell]; j < BoundaryCellStartRO[cell + 1]; j++)
        {
            uint boundary = BoundarySortedIndicesRO[j];
            float4 boundaryPosition = BoundaryPositionsRO[boundary];
            float magSq = DistanceSq(position, boundaryPosition);
            float mag = sqrt(magSq);

            float w = step(1e-12, magSq) * step(magSq, interactionRadiusSq);
            float4 r = (boundaryPosition - position) / max(mag, 1e-9);
            float4 correction = w * lambda * BoundaryVolumesRO[boundary] *
                SpikyGradient(mag, interactionRadius, spikyGradFactor, r);

            delta += correction;
            // The reaction is summed into force and torque on the body by SumCouplingForces
            AtomicAddBoundaryForceBuffer(boundary, -correction);
        }
    }
}

void ApplyBoundaryFriction(uint index, float4 position, float4 velocity, uint3 cellPosition)
{
    for (uint i = 0; i < 27; i++)
    {
        int3 cellOffset = int3(i % 3, (i / 3) % 3, i / 9) - 1;
        uint3 neighbourPos = cellPosition + cellOffset;
        if (neighbourPos.x >= columns || neighbourPos.y >= rows || neighbourPos.z >= layers) continue;
        uint cell = GetCellIndex(neighbourPos);

        for (uint j = BoundaryCellStartRO[cell]; j < BoundaryCellStartRO[cell + 1]; j++)
        {
            uint boundary = BoundarySortedIndicesRO[j];
            float4 boundaryPosition = BoundaryPositionsRO[boundary];
            float magSq = DistanceSq(position, boundaryPosition);
            float mag = sqrt(magSq);
            float q = mag / interactionRadius;

            float4 r = (boundaryPosition - position) / max(mag, 1e-9);
            float inwardVelocity = dot(velocity - BoundaryVelocitiesRO[boundary], r);

            float w = step(1e-12, magSq) * step(q, 1.0) * step(0.0, inwardVelocity);
            float4 impulse = ViscosityImpulse(dt, boundaryFriction, 0.0, q, inwardVelocity, r);

            AtomicAddForceBuffer(index, -w * impulse);
        }
    }
}

[numthreads(64,1,1)]
void ClearBoundaryGrid (uint3 id : SV_DispatchThreadID)
{
    if (id.x > numCells) return;

    BoundaryCellStart[id.x] = 0;
}

[numthreads(64,1,1)]
void CountBoundaryParticles (uint3 id : SV_DispatchThreadID)
{
    if (id.x >= numBoundaryParticles) return;

    uint cellIndex = GetCellIndex(GetCellPosition(BoundaryPositions[id.x]));
    InterlockedAdd(BoundaryCellStart[cellIndex + 1], 1);
}

[numthreads(64,1,1)]
void BoundaryScanStep (uint3 id : SV_DispatchThreadID)
{
    if (id.x > numCells) return;

    uint previous = max((int)id.x - (int)scanStride, 0);
    uint mask = id.x >= scanStride;

    if (scanFlip == 0)
        ScanTemp[id.x] = BoundaryCellStart[id.x] + BoundaryCellStart[previous] * mask;
    else
        BoundaryCellStart[id.x] = ScanTemp[id.x] + ScanTemp[previous] * mask;
}

[numthreads(64,1,1)]
void ResetBoundaryCursor (uint3 id : SV_DispatchThreadID)
{
    if (id.x > numCells) return;

    CellCursor[id.x] = BoundaryCellStart[id.x];
}

[numthreads(64,1,1)]
void ScatterBoundary (uint3 id : SV_DispatchThreadID)
{
    if (id.x >= numBoundaryParticles) return;

    uint cellIndex = GetCellIndex(GetCellPosition(BoundaryPositions[id.x]));
    uint destination;
    InterlockedAdd(CellCursor[cellIndex], 1, destination);

    BoundarySortedIndices[destination] = id.x;
}

[numthreads(64,1,1)]
void CalculateBoundaryVolumes (uint3 id : SV_DispatchThreadID)
{
    if (id.x >= numBoundaryParticles) return;

    float4 position = BoundaryPositions[id.x];
    uint body = BoundaryBodyIndices[id.x];
    uint3 cellPosition = GetCellPosition(position);
    float delta = 0;

    for (uint i = 0; i < 27; i++)
    {
        int3 cellOffset = int3(i % 3, (i / 3) % 3, i / 9) - 1;
        uint3 neighbourPos = cellPosition + cellOffset;
        if (neighbourPos.x >= columns || neighbourPos.y >= rows || neighbourPos.z >= layers) continue;
        uint cell = GetCellIndex(neighbourPos);

        for (uint j = BoundaryCellStart[cell]; j < BoundaryCellStart[cell + 1]; j++)
        {
            uint neighbour = BoundarySortedIndices[j];
            float magSq = DistanceSq(position, BoundaryPositions[neighbour]);

            float w = BoundaryBodyIndices[neighbour] == body;
            delta += w * Poly6(magSq, interactionRadiusSq, poly6Factor);
        }
    }

    // An isolated sample would give 1/0, and inf * 0 masks turn into NaN that spreads through the fluid
    BoundaryVolumes[id.x] = delta > 0 ? 1.0 / delta : 0.0;
}

[numthreads(64,1,1)]
void ClearBoundaryForceBuffers (uint3 id : SV_DispatchThreadID)
{
    if (id.x >= numBoundaryParticles) return;

    SetBoundaryForceBuffer(id.x, ZeroImpulse);
}

[numthreads(64,1,1)]
void PlaceBoundaryParticles (uint3 id : SV_DispatchThreadID)
{
    if (id.x >= numBoundaryParticles) return;

    RigidBodyState state = BodyStates[BoundaryBodyIndices[id.x]];
    float3 worldOffset = QuaternionRotate(state.rotation, BoundaryRestPositions[id.x].xyz);

    BoundaryPositions[id.x] = float4(state.position.xyz + worldOffset, 0);
    BoundaryVelocities[id.x] = float4(state.velocity.xyz + cross(state.angularVelocity.xyz, worldOffset), 0);
}

[numthreads(1,1,1)]
void BodyAdvancePredictedStates (uint3 id : SV_DispatchThreadID)
{
    if (id.x >= numBodies) return;

    RigidBodyState state = BodyStates[id.x];

    // Static bodies (invMass 0) ignore gravity but still update prev state
    float moves = step(1e-12, BodyProperties[id.x].invMass);

    state.velocity.y += dt * gravity * moves;
    state.prevPosition = state.position;
    state.prevRotation = state.rotation;
    state.position += dt * state.velocity * moves;
    state.rotation = IntegrateRotation(state.rotation, dt * state.angularVelocity.xyz * moves);

    BodyStates[id.x] = state;
}

[numthreads(BodyReductionThreads,1,1)]
void SumCouplingForces (uint3 groupId : SV_GroupID, uint3 threadId : SV_GroupThreadID)
{
    uint body = groupId.x;
    if (body >= numBodies) return;

    RigidBodyProperties properties = BodyProperties[body];
    float3 centerOfMass = BodyStates[body].position.xyz;

    float3 force = 0;
    float3 torque = 0;
    // Strided reduction: thread s sums every BodyReductionThreads-th boundary particle into its own slot
    for (uint i = properties.start + threadId.x; i < properties.start + properties.count; i += BodyReductionThreads)
    {
        float3 push = ReadBoundaryForceBuffer(i);
        force += push;
        torque += cross(BoundaryPositionsRO[i].xyz - centerOfMass, push);
    }

    uint slot = (body * BodyReductionThreads + threadId.x) * CouplingSlotSize;
    BodyCouplingSums[slot] = float4(force, 0);
    BodyCouplingSums[slot + 1] = float4(torque, 0);
}

[numthreads(1,1,1)]
void ApplyCouplingForces (uint3 id : SV_DispatchThreadID)
{
    if (id.x >= numBodies) return;

    RigidBodyProperties properties = BodyProperties[id.x];
    RigidBodyState state = BodyStates[id.x];

    float3 force = 0;
    float3 torque = 0;
    [loop] for (uint s = 0; s < BodyReductionThreads; s++)
    {
        uint slot = (id.x * BodyReductionThreads + s) * CouplingSlotSize;
        force += BodyCouplingSums[slot].xyz;
        torque += BodyCouplingSums[slot + 1].xyz;
    }

    // Weighted by particleMass so momentum is kept
    force *= particleMass;
    torque *= particleMass;

    float3x3 invInertiaWorld = InverseInertiaWorld(state.rotation, properties.invInertia.xyz);
    state.position.xyz += properties.invMass * force;
    state.rotation = IntegrateRotation(state.rotation, ClampRotation(mul(invInertiaWorld, torque), MaxCorrectionAngle));

    BodyStates[id.x] = state;
}

[numthreads(1,1,1)]
void AttractBodiesToMouse (uint3 id : SV_DispatchThreadID)
{
    if (id.x >= numBodies) return;

    RigidBodyState state = BodyStates[id.x];
    float mag = Distance(state.position, mousePosition);

    float inRange = step(mag, mouseRadius) * step(1e-12, BodyProperties[id.x].invMass);

    state.position += inRange * dt * mouseAttractiveness * (mousePosition - state.position) / max(mag, 1e-9);
    BodyStates[id.x] = state;
}

// Stops a particle spacing short of the walls, so fluid gets under a body on the floor
float3 WallPushOut(float3 position)
{
    float3 halfSize = realHalfBoundSize.xyz - restSpacing;
    float3 outside = step(halfSize, abs(position));
    return (sign(position) * halfSize - position) * outside;
}

void AccumulateContact(inout float4 pushSum, inout float4 frictionSum, inout float4 torqueSum, inout float4 approachSum,
                       float invMass, float3x3 invInertiaWorld, float3 r, float3 normal, float depth,
                       float3 contactDisplacement, float share)
{
    float normalInverseMass = GeneralizedInverseMass(invMass, invInertiaWorld, r, normal);
    // Static bodies are masked out here so their infinite corrections never reach the sums
    float isContact = step(1e-9, depth) * step(1e-12, normalInverseMass);
    float normalLambda = depth / max(normalInverseMass, 1e-12);

    float3 tangentialMotion = contactDisplacement - dot(contactDisplacement, normal) * normal;
    float tangentialLength = length(tangentialMotion);
    float3 tangent = -tangentialMotion / max(tangentialLength, 1e-9);
    float tangentLambda = tangentialLength / max(GeneralizedInverseMass(invMass, invInertiaWorld, r, tangent), 1e-12);
    // Coulomb friction: undo tangential motion, capped by the normal push
    float frictionLambda = min(tangentLambda, bodyFriction * normalLambda);

    float3 push = normalLambda * normal * isContact;
    float3 friction = frictionLambda * tangent * isContact;

    pushSum += float4(push, isContact);
    frictionSum.xyz += friction;
    torqueSum.xyz += cross(r, push + friction);
    approachSum.xyz += normal * min(dot(contactDisplacement, normal), 0.0) * share * isContact;
}

[numthreads(BodyReductionThreads,1,1)]
void SumContacts (uint3 groupId : SV_GroupID, uint3 threadId : SV_GroupThreadID)
{
    uint body = groupId.x;
    if (body >= numBodies) return;

    RigidBodyProperties properties = BodyProperties[body];
    RigidBodyState state = BodyStates[body];
    float3x3 invInertiaWorld = InverseInertiaWorld(state.rotation, properties.invInertia.xyz);

    float4 pushes[ContactGroups];
    float4 frictions[ContactGroups];
    float4 torques[ContactGroups];
    float4 approaches[ContactGroups];
    for (uint g = 0; g < ContactGroups; g++)
    {
        pushes[g] = 0;
        frictions[g] = 0;
        torques[g] = 0;
        approaches[g] = 0;
    }

    for (uint i = properties.start + threadId.x; i < properties.start + properties.count; i += BodyReductionThreads)
    {
        float3 restOffset = BoundaryRestPositions[i].xyz;
        float3 r = QuaternionRotate(state.rotation, restOffset);
        float3 contactPosition = state.position.xyz + r;
        float3 displacement = contactPosition - (state.prevPosition.xyz + QuaternionRotate(state.prevRotation, restOffset));

        // Walls are axis-aligned and static, so each axis is its own constraint and this body takes the full correction
        float3 wallPush = WallPushOut(contactPosition);
        AccumulateContact(pushes[0], frictions[0], torques[0], approaches[0], properties.invMass, invInertiaWorld,
                          r, float3(sign(wallPush.x), 0, 0), abs(wallPush.x), displacement, 1.0);
        AccumulateContact(pushes[1], frictions[1], torques[1], approaches[1], properties.invMass, invInertiaWorld,
                          r, float3(0, sign(wallPush.y), 0), abs(wallPush.y), displacement, 1.0);
        AccumulateContact(pushes[2], frictions[2], torques[2], approaches[2], properties.invMass, invInertiaWorld,
                          r, float3(0, 0, sign(wallPush.z)), abs(wallPush.z), displacement, 1.0);

        float3 bodyPush = 0;
        float3 otherDisplacement = 0;
        float touching = 0;
        float shareSum = 0;
        for (uint other = 0; other < numBodies; other++)
        {
            RigidBodyProperties otherProperties = BodyProperties[other];
            RigidBodyState otherState = BodyStates[other];

            float3 otherNormal;
            float otherDistance = BodySignedDistance(otherProperties, otherState, contactPosition, otherNormal);

            // Ignore pushes toward the other body's centre, which deep overlaps would otherwise produce
            float facing = step(0.0, dot(otherNormal, state.position.xyz - otherState.position.xyz));
            float inside = step(otherDistance, particleRadius) * facing * (other != body);
            // Guarded division: NaN * 0 is still NaN, so masking afterwards would not help
            float share = properties.invMass / max(properties.invMass + otherProperties.invMass, 1e-12);

            bodyPush += otherNormal * (particleRadius - otherDistance) * share * inside;
            otherDisplacement += BodyPointDisplacement(otherState, contactPosition) * inside;
            touching += inside;
            shareSum += share * inside;
        }

        float bodyDepth = length(bodyPush);
        float touchingCount = max(touching, 1.0);
        AccumulateContact(pushes[3], frictions[3], torques[3], approaches[3], properties.invMass, invInertiaWorld,
                          r, bodyPush / max(bodyDepth, 1e-9), bodyDepth, displacement - otherDisplacement / touchingCount,
                          shareSum / touchingCount);
    }

    uint slot = (body * BodyReductionThreads + threadId.x) * ContactSlotSize;
    for (uint c = 0; c < ContactGroups; c++)
    {
        BodyContactSums[slot + c * 4] = pushes[c];
        BodyContactSums[slot + c * 4 + 1] = frictions[c];
        BodyContactSums[slot + c * 4 + 2] = torques[c];
        BodyContactSums[slot + c * 4 + 3] = approaches[c];
    }
}

[numthreads(1,1,1)]
void ApplyContacts (uint3 id : SV_DispatchThreadID)
{
    if (id.x >= numBodies) return;

    RigidBodyProperties properties = BodyProperties[id.x];
    RigidBodyState state = BodyStates[id.x];
    float3x3 invInertiaWorld = InverseInertiaWorld(state.rotation, properties.invInertia.xyz);

    float3 pushChange = 0;
    float3 frictionChange = 0;
    float3 approachChange = 0;
    float3 angleChange = 0;
    for (uint g = 0; g < ContactGroups; g++)
    {
        float4 push = 0;
        float3 friction = 0;
        float3 torque = 0;
        float3 approach = 0;
        [loop] for (uint s = 0; s < BodyReductionThreads; s++)
        {
            uint slot = (id.x * BodyReductionThreads + s) * ContactSlotSize + g * 4;
            push += BodyContactSums[slot];
            friction += BodyContactSums[slot + 1].xyz;
            torque += BodyContactSums[slot + 2].xyz;
            approach += BodyContactSums[slot + 3].xyz;
        }

        // Average each group (Jacobi) so many floor contacts don't overshoot or drown out a few wall contacts
        float contacts = max(push.w, 1.0);
        pushChange += properties.invMass * push.xyz / contacts;
        frictionChange += properties.invMass * friction / contacts;
        angleChange += mul(invInertiaWorld, torque) / contacts;
        approachChange += approach / contacts;
    }

    state.position.xyz += pushChange + frictionChange;
    // Moving prevPosition too keeps the push out from turning into velocity; the approach term makes contacts inelastic
    state.prevPosition.xyz += pushChange + approachChange;
    state.rotation = IntegrateRotation(state.rotation, ClampRotation(angleChange, MaxCorrectionAngle));

    BodyStates[id.x] = state;
}

[numthreads(1,1,1)]
void BodyCalculateVelocity (uint3 id : SV_DispatchThreadID)
{
    if (id.x >= numBodies) return;

    RigidBodyState state = BodyStates[id.x];
    state.velocity = (state.position - state.prevPosition) / dt;

    float4 change = QuaternionMultiply(state.rotation, QuaternionConjugate(state.prevRotation));
    // q and -q are the same rotation, flip to w >= 0 to take the short way
    float shortWay = step(0.0, change.w) * 2.0 - 1.0;
    state.angularVelocity = float4(2.0 * shortWay * change.xyz / dt, 0);

    BodyStates[id.x] = state;
}

[numthreads(64,1,1)]
// Hard collision of fluid particles against body surfaces, since soft pressure lets particles slip between boundary samples
void ResolveBodyCollisions (uint3 id : SV_DispatchThreadID)
{
    if (id.x >= numParticles) return;

    float3 position = Positions[id.x].xyz;

    for (uint body = 0; body < numBodies; body++)
    {
        float3 normal;
        float surfaceDistance = BodySignedDistance(BodyProperties[body], BodyStates[body], position, normal);
        float inside = step(surfaceDistance, particleRadius);

        position += normal * (particleRadius + collisionDamp - surfaceDistance) * inside;
    }

    Positions[id.x] = float4(position, 0);
}
