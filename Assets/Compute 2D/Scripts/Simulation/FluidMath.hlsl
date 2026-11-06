#define PI 3.14159265359

float Distance(float2 p1, float2 p2);
float Pow3(float x);
float Pow9(float x);

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

// Used by the density map and marching squares renderers
float QuadraticSpikyKernel(float relativeDistance)
{
    return (1.0 - relativeDistance) * (1.0 - relativeDistance);
}

// 2D Poly6 density kernel, factor = 4 / (π h⁸)
float Poly6(float distanceSq, float radiusSq, float factor)
{
    return factor * Pow3(max(radiusSq - distanceSq, 0.0));
}

// 2D spiky kernel gradient with respect to the particle, pointing towards the neighbour. factor = 30 / (π h⁵)
float2 SpikyGradient(float distance, float radius, float factor, float2 towardsNeighbour)
{
    float overlap = radius - distance;
    return factor * overlap * overlap * towardsNeighbour;
}

// Density a wall adds as if fluid at rest filled the space beyond it: 2D Poly6 integrated over that half plane,
// 128 / (35π) times the integral of (1 - u²)^(7/2) from x to 1, done with u = sin θ.
// x is the distance to the wall over the radius in [0, 1]: 0.5 at the wall, 0 at x = 1
float2 WallDensity(float2 x)
{
    float2 t = asin(x);
    return (35.0 * (PI / 2.0 - t) - 28.0 * sin(2.0 * t) - 7.0 * sin(4.0 * t) -
        4.0 / 3.0 * sin(6.0 * t) - 1.0 / 8.0 * sin(8.0 * t)) / (35.0 * PI);
}

// How fast WallDensity grows as the particle moves towards the wall
float2 WallDensityGradient(float2 x, float radius)
{
    float2 y = 1.0 - x * x;
    return 128.0 / (35.0 * PI * radius) * y * y * y * sqrt(y);
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