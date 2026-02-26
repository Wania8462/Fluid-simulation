using System;
using Unity.Mathematics;
using UnityEngine;

public class Spawn2DParticles : MonoBehaviour
{
    [Header("Spawn settings")]
    [SerializeField] private int particleSquareLength = 50;
    [SerializeField] private float spacing = 2;
    [SerializeField] private bool useJitter = true;
    [SerializeField] private float jitterStrength = 0.2f;
    [SerializeField] private float2 boundingBoxSizeOffset = new float2(160, 80);
    public float2 boundingBoxSize;

    public int GetNumberOfParticles() => particleSquareLength * particleSquareLength;

    public float2[] InitializePositions()
    {
        int len = particleSquareLength;
        float2[] pos = new float2[len * len];
        jitterStrength = useJitter ? jitterStrength : 0;

        for (int i = 0; i < len; i++)
        {
            for (int j = 0; j < len; j++)
            {
                pos[i * len + j] = new float2(i * spacing + (UnityEngine.Random.insideUnitSphere.x * jitterStrength) - len + 1,
                                      j * spacing + (UnityEngine.Random.insideUnitSphere.y * jitterStrength) - len + 1);
            }
        }

        boundingBoxSize = new float2(particleSquareLength + boundingBoxSizeOffset.x * 2, particleSquareLength + boundingBoxSizeOffset.y * 2); ;
        return pos;
    }

    public float2[] InitializePreviousPositions() => new float2[(int)Math.Pow(particleSquareLength, 2)];

    public float2[] InitializeVelocities() => new float2[(int)Math.Pow(particleSquareLength, 2)];

    public float[] InitializeDensities() => new float[(int)Math.Pow(particleSquareLength, 2)];

    public float[] InitializeNearDensities() => new float[(int)Math.Pow(particleSquareLength, 2)];

    public float[] InitializeBoundaryDensities() => new float[(int)Math.Pow(particleSquareLength, 2)];

    public float2[] InitializeBodyDensityPoints(int resolution, float radius)
    {
        float2[] res = new float2[resolution];

        for (int i = 0; i < resolution; i++)
        {
            res[i] = new float2((float)(radius * Math.Cos(Math.PI * (i - 1) / resolution / 2)),
                                (float)(radius * Math.Sin(Math.PI * (i - 1) / resolution / 2)));
        }

        return res;
    }

    public float2 GetRealHalfBoundSize(float radius) => new(boundingBoxSize.x / 2 - radius, boundingBoxSize.y / 2 - radius);
}
