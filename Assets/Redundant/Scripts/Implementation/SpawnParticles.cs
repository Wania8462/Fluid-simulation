using System;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
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

    [HideInInspector] public int numParticles;
    [HideInInspector] public float3 boundSize { get; private set; }

    private const uint primeOne = 46771;
    private const uint primeTwo = 35863;
    private const uint primeThree = 45887;

    void Awake()
    {
        boundSize = new(axisLength * 2 + 20, axisLength * 2 + 20, axisLength * 2 + 20);
        numParticles = (int)Mathf.Pow(axisLength, 3);
    }

    public float4[] GetSpawnPositions()
    {
        float4[] points = new float4[numParticles];

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

    public float4[] GetSpawnVelocities() => new float4[numParticles];

    public HashLookup[] GetGrid()
    {
        HashLookup[] grid = new HashLookup[numParticles];   

        for (int i = 0; i < numParticles; i++)
        {
            grid[i].hash = 0; // calc hashes?
            grid[i].particleIndex = (uint)i;
        }

        return grid;
    }
}