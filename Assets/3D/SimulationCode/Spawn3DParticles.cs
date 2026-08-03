using Unity.Mathematics;
using UnityEngine;

public class Spawn3DParticles : MonoBehaviour
{
    [Header("Spawn settings")]
    [SerializeField] private int particleCubeLength = 20;
    [SerializeField] private float spacing = 2;
    [SerializeField] private bool useJitter = true;
    [SerializeField] private float jitterStrength = 0.2f;
    [SerializeField] private float3 boundingBoxSizeOffset = new(40, 40, 40);
    private float3 boundingBoxSize;

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

        boundingBoxSize = new float3(particleCubeLength) + boundingBoxSizeOffset * 2;

        if (boundingBoxSize.x == 0 || boundingBoxSize.y == 0 || boundingBoxSize.z == 0)
            Debug.LogWarning($"Bounding box size is {boundingBoxSize}");

        return pos;
    }

    public float3 GetRealHalfBoundSize(float radius)
    {
        return GetBoundingBoxSize() / 2 - radius;
    }

    public float3 GetBoundingBoxSize()
    {
        if (boundingBoxSize.x == 0 || boundingBoxSize.y == 0 || boundingBoxSize.z == 0)
            boundingBoxSize = new float3(particleCubeLength) + boundingBoxSizeOffset * 2;

        return boundingBoxSize;
    }
}
