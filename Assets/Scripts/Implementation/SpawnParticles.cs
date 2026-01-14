using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;

[BurstCompile]
public class SpawnParticles : MonoBehaviour
{
    [Header("Spawn settings")]
    public int axisLength;
    [SerializeField] private int3 centre;
    [SerializeField] private float spacing;
    [SerializeField] private float jitterStrength;

    [HideInInspector] public int pointsAmount;
    [HideInInspector] public float3 boundSize { get; private set; }

    void Awake()
    {
        boundSize = new(axisLength * 2 + 20, axisLength * 2 + 20, axisLength * 2 + 20);
        pointsAmount = (int)Mathf.Pow(axisLength, 3);
    }

    public float3[] GetSpawnPositions()
    {
        float3[] points = new float3[pointsAmount];

        for (int i = centre.x; i < axisLength; i++)
            for (int j = centre.y; j < axisLength; j++)
                for (int k = centre.z; k < axisLength; k++)
                {
                    int index = i * axisLength * axisLength + j * axisLength + k;

                    points[index].x = (i - axisLength / 2) * spacing + UnityEngine.Random.insideUnitSphere.x * jitterStrength;
                    points[index].y = (j - axisLength / 2) * spacing + UnityEngine.Random.insideUnitSphere.y * jitterStrength;
                    points[index].z = (k - axisLength / 2) * spacing + UnityEngine.Random.insideUnitSphere.z * jitterStrength;
                }

        return points;
    }

    public float3[] GetSpawnVelocities() => new float3[pointsAmount];
}