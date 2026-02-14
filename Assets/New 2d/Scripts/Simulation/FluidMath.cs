using System.Runtime.CompilerServices;
using Unity.Mathematics;
using UnityEngine;

namespace SimulationLogic
{
    public static class FluidMath
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float2 PressureDisplacement(float deltaTime, float relativeDistance, float pseudoPressure, float nearPseudoPressure, float2 unitVector)
        {
            return deltaTime * deltaTime *
                (pseudoPressure * (1 - relativeDistance) + nearPseudoPressure * (1 - relativeDistance) * (1 - relativeDistance)) *
                unitVector;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float2 ViscosityImpulse(float deltaTime, float highViscosity, float lowViscosity, float relativeDistance, float inwardVelocity, float2 unitVector)
        {
            return deltaTime *
                (1 - relativeDistance) *
                (highViscosity * inwardVelocity + (lowViscosity * inwardVelocity)) *
                unitVector;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float StretchSpring(float deltaTime, float plasticity, float magnitude, float restLength, float deformation)
        {
            return deltaTime * plasticity * (magnitude - restLength - deformation);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float CompressSpring(float deltaTime, float plasticity, float magnitude, float restLength, float deformation)
        {
            return deltaTime * plasticity * (restLength - deformation - magnitude);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float2 DisplacementBySpring(float deltaTime, float springStiffness, float springRestLength, float interactionRadius, float magnitude, float2 unitVector)
        {
            return deltaTime * deltaTime *
                springStiffness *
                (1 - (springRestLength / interactionRadius)) *
                (springRestLength - magnitude) *
                unitVector;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float2 UnitVector(float2 initialVector, float2 finalVector)
        {
            return (finalVector - initialVector) / Distance(initialVector, finalVector);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float2 UnitVector(float2 initialVector, float2 finalVector, float distance)
        {
            return (finalVector - initialVector) / distance;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Poly6Kernel(float dist, float smoothingRadius)
        {
            return 315 *
                Pow3(Mathf.Abs(smoothingRadius * smoothingRadius - dist * dist)) /
                (64 * Mathf.PI * Pow9(smoothingRadius));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float QuadraticSpikyKernel(float relativeDistance) => (1 - relativeDistance) * (1 - relativeDistance);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float CubicSpikyKernel(float relativeDistance) => Pow3(1 - relativeDistance);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Distance(float2 p1, float2 p2) => Mathf.Sqrt((p2.x - p1.x) * (p2.x - p1.x) + (p2.y - p1.y) * (p2.y - p1.y));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float DistanceSq(float2 p1, float2 p2) => (p2.x - p1.x) * (p2.x - p1.x) + (p2.y - p1.y) * (p2.y - p1.y);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Pow3(float x)
        {
            return x * x * x;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Pow9(float x)
        {
            float y = x;
            y *= y;
            y *= y;
            y *= y;
            y *= x;
            return y;
        }
    }
}