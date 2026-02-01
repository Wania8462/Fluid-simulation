using System.Runtime.CompilerServices;
using UnityEngine;

public static class FluidMath
{
    public static Vector2 PressureDisplacement(float deltaTime, float relativeDistance, float pseudoPressure, float nearPseudoPressure, Vector2 unitVector)
    {
        return deltaTime * deltaTime *
               (pseudoPressure * (1 - relativeDistance) + nearPseudoPressure * Mathf.Pow(1 - relativeDistance, 2)) *
               unitVector;
    }

    public static Vector2 ViscosityImpulse(float deltaTime, float highViscosity, float lowViscosity, float relativeDistance, float inwardVelocity, Vector2 unitVector)
    {
        return deltaTime *
               (1 - relativeDistance) *
               (highViscosity * inwardVelocity + (lowViscosity * Mathf.Pow(inwardVelocity, 2))) *
               unitVector;
    }

    public static float StretchSpring(float deltaTime, float plasticity, float magnitude, float restLength, float deformation)
    {
        return deltaTime * plasticity * (magnitude - restLength - deformation);
    }

    public static float CompressSpring(float deltaTime, float plasticity, float magnitude, float restLength, float deformation)
    {
        return deltaTime * plasticity * (restLength - deformation - magnitude);
    }

    public static Vector2 DisplacementBySpring(float deltaTime, float springStiffness, float springRestLength, float interactionRadius, float magnitude, Vector2 unitVector)
    {
        return deltaTime * deltaTime *
               springStiffness *
               (1 - (springRestLength / interactionRadius)) *
               (springRestLength - magnitude) *
               unitVector;
    }

    public static Vector2 UnitVector(Vector2 initialVector, Vector2 finalVector)
    {
        return (finalVector - initialVector) / Distance(initialVector, finalVector);
    }

    public static float Poly6Kernel(float dist, float smoothingRadius)
    {
        return 315 * 
               Mathf.Pow(Mathf.Abs(Mathf.Pow(smoothingRadius, 2) - Mathf.Pow(dist, 2)), 3) / 
               (64 * Mathf.PI * Mathf.Pow(smoothingRadius, 9));
    }

    public static float QuadraticSpikyKernel(float relativeDistance) => Mathf.Pow(1 - relativeDistance, 2);

    public static float CubicSpikyKernel(float relativeDistance) => Mathf.Pow(1 - relativeDistance, 3);
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Distance(Vector2 p1, Vector2 p2) => Mathf.Sqrt(Mathf.Pow(p2.x - p1.x, 2) + Mathf.Pow(p2.y - p1.y, 2));
}