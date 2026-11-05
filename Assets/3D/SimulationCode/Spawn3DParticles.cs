using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public readonly struct Tank
{
    public readonly float3 size;
    public readonly float3 halfSize;
    public readonly float3 innerHalfSize;

    public Tank(float3 size, float particleRadius)
    {
        this.size = size;
        halfSize = size / 2;
        innerHalfSize = halfSize - particleRadius;
    }
}

public class Spawn3DParticles : MonoBehaviour
{
    [Header("Spawn settings")]
    [SerializeField] private int particleCubeLength = 20;
    [SerializeField] private float spacing = 2;
    [SerializeField] private bool useJitter = true;
    [SerializeField] private float jitterStrength = 0.2f;

    [Header("Tank")]
    [Tooltip("The box the fluid is simulated in, wall to wall, centred on the origin. Nothing else sizes it, so it has to be big enough for the spawn cube, or for a FluidFlow3D's reservoir and drain")]
    [SerializeField] private float3 tankSize = new(100, 100, 100);
    [SerializeField] private Color tankGizmoColor = new(1, 1, 1, 0.4f);

    public int GetNumberOfParticles() => particleCubeLength * particleCubeLength * particleCubeLength;

    public float4[] InitializePositions()
    {
        int len = particleCubeLength;
        float4[] pos = new float4[len * len * len];
        float jitter = useJitter ? jitterStrength : 0;

        for (int i = 0; i < len; i++)
        {
            for (int j = 0; j < len; j++)
            {
                for (int k = 0; k < len; k++)
                {
                    Vector3 jitterOffset = UnityEngine.Random.insideUnitSphere * jitter;
                    pos[(i * len + j) * len + k] = new float4(i * spacing + jitterOffset.x - len + 1,
                                                              j * spacing + jitterOffset.y - len + 1,
                                                              k * spacing + jitterOffset.z - len + 1,
                                                              0);
                }
            }
        }

        return pos;
    }

    public float3[] InitCubeSurfacePositions(float halfSize, float sampleDensity)
    {
        int cellsPerEdge = Mathf.Max(1, Mathf.CeilToInt(2 * halfSize / sampleDensity));
        float latticeSpacing = 2 * halfSize / cellsPerEdge;
        List<float3> positions = new();

        for (int i = 0; i <= cellsPerEdge; i++)
        {
            for (int j = 0; j <= cellsPerEdge; j++)
            {
                for (int k = 0; k <= cellsPerEdge; k++)
                {
                    bool onFace = i == 0 || j == 0 || k == 0 || i == cellsPerEdge || j == cellsPerEdge || k == cellsPerEdge;
                    if (onFace)
                        positions.Add(new float3(i, j, k) * latticeSpacing - halfSize);
                }
            }
        }

        return positions.ToArray();
    }

    public float3[] InitSphereSurfacePositions(float radius, float sampleDensity)
    {
        int count = Mathf.CeilToInt(4 * math.PI * radius * radius / (sampleDensity * sampleDensity));

        if (count < 4)
        {
            Debug.LogWarning("Spawn particles: not enough particles for the sphere surface");
            count = 4;
        }

        // Fibonacci sphere: evenly spaced heights, each rotated by the golden angle
        float goldenAngle = math.PI * (3 - math.sqrt(5));
        float3[] positions = new float3[count];

        for (int i = 0; i < count; i++)
        {
            float y = 1 - 2 * (i + 0.5f) / count;
            float ringRadius = math.sqrt(1 - y * y);
            float angle = goldenAngle * i;
            positions[i] = radius * new float3(math.cos(angle) * ringRadius, y, math.sin(angle) * ringRadius);
        }

        return positions;
    }

    public Tank GetTank(float particleRadius)
    {
        if (math.any(tankSize <= 0))
            Debug.LogWarning($"Spawn particles: tank size is {tankSize}");

        return new Tank(tankSize, particleRadius);
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = tankGizmoColor;
        Gizmos.DrawWireCube(Vector3.zero, tankSize);
    }
}
