#define PI 3.14159265359

float Distance(float2 p1, float2 p2);
float Pow3(float x);
float Pow9(float x);

float2 PressureDisplacement(float deltaTime, float relativeDistance, float pseudoPressure, float nearPseudoPressure, float2 unitVector)
{
    float invRelativeDist = 1.0 - relativeDistance;
    return deltaTime * deltaTime *
        (pseudoPressure * invRelativeDist +
        nearPseudoPressure * invRelativeDist * invRelativeDist) *
        unitVector;
}

float2 ViscosityImpulse(float deltaTime, float highViscosity, float lowViscosity, float relativeDistance, float inwardVelocity, float2 unitVector)
{
    return deltaTime *
        (1.0 - relativeDistance) *
        (highViscosity * inwardVelocity + (lowViscosity * inwardVelocity)) *
        unitVector;
}

float StretchSpring(float deltaTime, float plasticity, float magnitude, float restLength, float deformation)
{
    return deltaTime * plasticity * (magnitude - restLength - deformation);
}

float CompressSpring(float deltaTime, float plasticity, float magnitude, float restLength, float deformation)
{
    return deltaTime * plasticity * (restLength - deformation - magnitude);
}

float2 DisplacementBySpring(float deltaTime, float springStiffness, float springRestLength, float interactionRadius, float magnitude, float2 unitVector)
{
    return deltaTime * deltaTime *
        springStiffness *
        (1.0 - (springRestLength / interactionRadius)) *
        (springRestLength - magnitude) *
        unitVector;
}

float2 UnitVector(float2 initialVector, float2 finalVector)
{
    return (finalVector - initialVector) / Distance(initialVector, finalVector);
}

float2 UnitVector(float2 initialVector, float2 finalVector, float distance)
{
    return (finalVector - initialVector) / distance;
}

float Poly6Kernel(float dist, float smoothingRadius)
{
    return 315.0 *
        Pow3(abs(smoothingRadius * smoothingRadius - dist * dist)) /
        (64.0 * PI * Pow9(smoothingRadius));
}

float QuadraticSpikyKernel(float relativeDistance)
{
    return (1.0 - relativeDistance) * (1.0 - relativeDistance);
}

float CubicSpikyKernel(float relativeDistance)
{
    return Pow3(1.0 - relativeDistance);
}

float Distance(float2 p1, float2 p2)
{
    return sqrt((p2.x - p1.x) * (p2.x - p1.x) + (p2.y - p1.y) * (p2.y - p1.y));
}

float DistanceSq(float2 p1, float2 p2)
{
    return (p2.x - p1.x) * (p2.x - p1.x) + (p2.y - p1.y) * (p2.y - p1.y);
}

float Pow3(float x)
{
    return x * x * x;
}

float Pow9(float x)
{
    float y = x;
    y *= y;
    y *= y;
    y *= y;
    y *= x;
    return y;
}