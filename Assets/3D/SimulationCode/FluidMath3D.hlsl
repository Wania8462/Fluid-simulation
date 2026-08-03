#define PI 3.14159265359

float Distance(float4 p1, float4 p2);
float DistanceSq(float4 p1, float4 p2);
float Pow3(float x);

// Positions and velocities are float4 with w = 0 (16 byte GPU alignment),
// only xyz carries simulation data

float4 PressureDisplacement(float deltaTime, float relativeDistance, float pseudoPressure, float nearPseudoPressure, float4 unitVector)
{
    float invRelativeDist = 1.0 - relativeDistance;
    return deltaTime * deltaTime *
        (pseudoPressure * invRelativeDist +
        nearPseudoPressure * invRelativeDist * invRelativeDist) *
        unitVector;
}

float4 ViscosityImpulse(float deltaTime, float highViscosity, float lowViscosity, float relativeDistance, float inwardVelocity, float4 unitVector)
{
    return deltaTime *
        (1.0 - relativeDistance) *
        (highViscosity * inwardVelocity + (lowViscosity * inwardVelocity)) *
        unitVector;
}

float4 UnitVector(float4 initialVector, float4 finalVector)
{
    return (finalVector - initialVector) / Distance(initialVector, finalVector);
}

float4 UnitVector(float4 initialVector, float4 finalVector, float distance)
{
    return (finalVector - initialVector) / distance;
}

float QuadraticSpikyKernel(float relativeDistance)
{
    return (1.0 - relativeDistance) * (1.0 - relativeDistance);
}

float CubicSpikyKernel(float relativeDistance)
{
    return Pow3(1.0 - relativeDistance);
}

float DistanceSq(float4 p1, float4 p2)
{
    float3 d = p2.xyz - p1.xyz;
    return dot(d, d);
}

float Distance(float4 p1, float4 p2)
{
    return sqrt(DistanceSq(p1, p2));
}

float Pow3(float x)
{
    return x * x * x;
}
