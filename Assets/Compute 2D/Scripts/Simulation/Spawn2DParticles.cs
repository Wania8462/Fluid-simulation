using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class Spawn2DParticles : MonoBehaviour
{
    [Header("Spawn settings")]
    [SerializeField] private int particleSquareLength = 50;
    [SerializeField] private float spacing = 2;
    [SerializeField] private bool useJitter = true;
    [SerializeField] private float jitterStrength = 0.2f;
    [SerializeField] private float2 boundingBoxSizeOffset = new(160, 80);
    private float2 boundingBoxSize;

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

        boundingBoxSize = new float2(particleSquareLength + boundingBoxSizeOffset.x * 2, particleSquareLength + boundingBoxSizeOffset.y * 2);

        if (boundingBoxSize.x == 0 || boundingBoxSize.x == 0)
            Debug.LogWarning($"Bounding box size is {boundingBoxSize}");

        return pos;
    }

    public float2[] InitializePreviousPositions() => new float2[particleSquareLength * particleSquareLength];
    public int[] InitializeForceBuffers() => new int[particleSquareLength * particleSquareLength];

    public float2[] InitializeVelocities() => new float2[particleSquareLength * particleSquareLength];

    public float[] InitializeDensities() => new float[particleSquareLength * particleSquareLength];

    public float[] InitializeNearDensities() => new float[particleSquareLength * particleSquareLength];

    public float[] InitializeSprings()
    {
        int particleCount = GetNumberOfParticles();
        return new float[checked(particleCount * particleCount)];
    }

    public float2[] InitCircleOutlinePositions(float radius, float sampleDensity, float2 position)
    {
        float arcLen = ArcLength(radius, sampleDensity);
        float theta = AngleFromArcLength(radius, arcLen);
        int nbOfParticles = (int)Mathf.Ceil(2 * math.PI / theta);

        if (nbOfParticles < 4)
            Debug.LogWarning("Spawn particles: not enough particles for the circle outline");

        float2[] positions = new float2[nbOfParticles];
        for (int i = 0; i < nbOfParticles; i++)
        {
            positions[i] = new(radius * Mathf.Cos(i * theta) + position.x,
                               radius * Mathf.Sin(i * theta) + position.y);
        }

        return positions;
    }

    public float2[] InitRectangleOutlinePositions(float width, float height, float samplingDensity, float2 position, float rotation = 0)
    {
        int lenX = Mathf.Max(1, Mathf.CeilToInt(width / samplingDensity));
        int lenY = Mathf.Max(1, Mathf.CeilToInt(height / samplingDensity));
        float spacingX = width / lenX;
        float spacingY = height / lenY;

        List<float2> positions = new();
        float2 topLeft = new(-width / 2, height / 2);

        for (int i = 0; i < lenX; i++)
            positions.Add(new(topLeft.x + i * spacingX, topLeft.y));

        for (int i = 0; i < lenY; i++)
            positions.Add(new(topLeft.x + width, topLeft.y - i * spacingY));

        for (int i = 0; i < lenX; i++)
            positions.Add(new(topLeft.x + width - i * spacingX, topLeft.y - height));

        for (int i = 0; i < lenY; i++)
            positions.Add(new(topLeft.x, topLeft.y - height + i * spacingY));

        float cos = Mathf.Cos(rotation);
        float sin = Mathf.Sin(rotation);

        float2[] result = new float2[positions.Count];
        for (int i = 0; i < positions.Count; i++)
        {
            float2 local = positions[i];
            result[i] = new(position.x + local.x * cos - local.y * sin,
                            position.y + local.x * sin + local.y * cos);
        }

        return result;
    }

    private static float ArcLength(float radius, float chordLength)
    {
        float angle = Mathf.Acos((2 * radius * radius - (chordLength * chordLength)) / (2 * radius * radius));
        return 2 * angle * radius;
    }

    private static float AngleFromArcLength(float radius, float arcLength) => arcLength / (2 * radius);

    public float2 GetRealHalfBoundSize(float radius)
    {
        if (boundingBoxSize.x == 0 || boundingBoxSize.y == 0)
            boundingBoxSize = new float2(particleSquareLength + boundingBoxSizeOffset.x * 2, particleSquareLength + boundingBoxSizeOffset.y * 2);

        return new(boundingBoxSize.x / 2 - radius, boundingBoxSize.y / 2 - radius);
    }

    public float2 GetBoundingBoxSize()
    {
        if (boundingBoxSize.x == 0 || boundingBoxSize.y == 0)
            boundingBoxSize = new float2(particleSquareLength + boundingBoxSizeOffset.x * 2, particleSquareLength + boundingBoxSizeOffset.y * 2);

        return boundingBoxSize;
    }
}
