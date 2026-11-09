static const uint ShapeCube = 0;
static const uint ShapeSphere = 1;

#define BodyReductionThreads 64

// Max rotation (radians) a single correction may apply, keeps solves stable
static const float MaxCorrectionAngle = 0.2f;

struct RigidBodyProperties
{
    float4 size;
    float4 centerOfMass;
    float4 invInertia;
    uint shape;
    uint start;
    uint count;
    float invMass;
};

struct RigidBodyState
{
    float4 position;
    float4 rotation;
    float4 prevPosition;
    float4 prevRotation;
    float4 velocity;
    float4 angularVelocity;
};

float4 QuaternionMultiply(float4 a, float4 b)
{
    return float4(a.w * b.xyz + b.w * a.xyz + cross(a.xyz, b.xyz), a.w * b.w - dot(a.xyz, b.xyz));
}

float4 QuaternionConjugate(float4 q)
{
    return float4(-q.xyz, q.w);
}

float3 QuaternionRotate(float4 q, float3 v)
{
    float3 t = 2.0 * cross(q.xyz, v);
    return v + q.w * t + cross(q.xyz, t);
}

float3 QuaternionRotateInverse(float4 q, float3 v)
{
    return QuaternionRotate(QuaternionConjugate(q), v);
}

// First-order quaternion update: q += 0.5 * (w, 0) * q
float4 IntegrateRotation(float4 q, float3 rotationVector)
{
    return normalize(q + 0.5 * QuaternionMultiply(float4(rotationVector, 0), q));
}

float3 ClampRotation(float3 rotationVector, float maxAngle)
{
    return rotationVector * min(1.0, maxAngle / max(length(rotationVector), 1e-9));
}

float3x3 InverseInertiaWorld(float4 rotation, float3 invInertia)
{
    float3 axisX = QuaternionRotate(rotation, float3(1, 0, 0));
    float3 axisY = QuaternionRotate(rotation, float3(0, 1, 0));
    float3 axisZ = QuaternionRotate(rotation, float3(0, 0, 1));

    float3x3 rotationMatrix = float3x3(axisX.x, axisY.x, axisZ.x,
                                       axisX.y, axisY.y, axisZ.y,
                                       axisX.z, axisY.z, axisZ.z);
    float3x3 scaled = float3x3(rotationMatrix[0] * invInertia,
                               rotationMatrix[1] * invInertia,
                               rotationMatrix[2] * invInertia);

    return mul(scaled, transpose(rotationMatrix));
}

// Effective inverse mass of a body at offset r along direction (XPBD)
float GeneralizedInverseMass(float invMass, float3x3 invInertiaWorld, float3 r, float3 direction)
{
    float3 rn = cross(r, direction);
    return invMass + dot(rn, mul(invInertiaWorld, rn));
}

// How far the body point now at worldPosition moved since the previous step
float3 BodyPointDisplacement(RigidBodyState state, float3 worldPosition)
{
    float3 bodyOffset = QuaternionRotateInverse(state.rotation, worldPosition - state.position.xyz);
    float3 previousPosition = state.prevPosition.xyz + QuaternionRotate(state.prevRotation, bodyOffset);
    return worldPosition - previousPosition;
}

float3 SignNotZero(float3 v)
{
    return step(0.0, v) * 2.0 - 1.0;
}

float SphereSignedDistance(float3 localPosition, float radius, out float3 localNormal)
{
    float len = length(localPosition);
    localNormal = localPosition / max(len, 1e-9);
    return len - radius;
}

float BoxSignedDistance(float3 localPosition, float3 halfExtents, out float3 localNormal)
{
    float3 q = abs(localPosition) - halfExtents;
    float3 outside = max(q, 0.0);
    float outsideLength = length(outside);

    // Inside the box the normal points along the axis of the nearest face
    float isX = step(q.y, q.x) * step(q.z, q.x);
    float isY = (1.0 - isX) * step(q.z, q.y);
    float3 insideNormal = float3(isX, isY, (1.0 - isX) * (1.0 - isY));
    float3 outsideNormal = outside / max(outsideLength, 1e-9);

    float isOutside = step(1e-9, outsideLength);
    localNormal = lerp(insideNormal, outsideNormal, isOutside) * SignNotZero(localPosition);
    return outsideLength + min(max(q.x, max(q.y, q.z)), 0.0);
}

float BodySignedDistance(RigidBodyProperties properties, RigidBodyState state, float3 worldPosition, out float3 worldNormal)
{
    float3 local = QuaternionRotateInverse(state.rotation, worldPosition - state.position.xyz) + properties.centerOfMass.xyz;

    float3 cubeNormal;
    float cubeDistance = BoxSignedDistance(local, properties.size.xyz, cubeNormal);

    float3 sphereNormal;
    float sphereDistance = SphereSignedDistance(local, properties.size.x, sphereNormal);

    // Evaluate both shapes and select branchlessly
    float isCube = properties.shape == ShapeCube;
    float isSphere = properties.shape == ShapeSphere;

    worldNormal = QuaternionRotate(state.rotation, cubeNormal * isCube + sphereNormal * isSphere);
    return cubeDistance * isCube + sphereDistance * isSphere;
}
