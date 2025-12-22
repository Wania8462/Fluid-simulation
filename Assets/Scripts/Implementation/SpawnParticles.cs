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

    [HideInInspector] public float3 boundSize;

    private Simulation sim;

    void Start()
    {
        sim = gameObject.GetComponent<Simulation>();

        boundSize = new(axisLength * 2 + 20, axisLength * 2 + 20, axisLength * 2 + 20);

        sim.points = new float3[(int)Mathf.Pow(axisLength, 3)];
        sim.velocities = new float3[(int)Mathf.Pow(axisLength, 3)];

        CreateList();
    }

    private void CreateList()
    {
        for (int i = centre.x; i < axisLength; i++)
            for (int j = centre.y; j < axisLength; j++)
                for (int k = centre.z; k < axisLength; k++)
                {
                    int index = i * axisLength * axisLength + j * axisLength + k;

                    sim.points[index].x = (i - axisLength / 2) * spacing + UnityEngine.Random.insideUnitSphere.x * jitterStrength;
                    sim.points[index].y = (j - axisLength / 2) * spacing + UnityEngine.Random.insideUnitSphere.y * jitterStrength;
                    sim.points[index].z = (k - axisLength / 2) * spacing + UnityEngine.Random.insideUnitSphere.z * jitterStrength;
                }
    }
}