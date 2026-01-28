using UnityEngine;

public static class FluidMath
{
    public static float Poly6Kernel(float dist, float smoothingRadius)
    {
        return 315 * Mathf.Pow(Mathf.Abs(Mathf.Pow(smoothingRadius, 2) - Mathf.Pow(dist, 2)), 3) / (64 * Mathf.PI * Mathf.Pow(smoothingRadius, 9));
    }

    public static float Distance(Vector2 p1, Vector2 p2) => Mathf.Sqrt(Mathf.Pow(p2.x - p1.x, 2) + Mathf.Pow(p2.y - p1.y, 2));
}