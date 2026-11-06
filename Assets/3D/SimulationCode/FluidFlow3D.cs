using Unity.Mathematics;
using UnityEngine;

public class FluidFlow3D : MonoBehaviour
{
    [Header("Source")]
    [Tooltip("Centre of the reservoir the fluid pours out of. Put it above the scene, out of frame")]
    [SerializeField] private float3 sourceCenter = new(0, 250, 0);
    [Tooltip("Wall to wall size of the reservoir")]
    [SerializeField] private float3 sourceSize = new(80, 200, 80);
    [Tooltip("Radius of the hole in the reservoir's floor. Below about the interaction radius the fluid bridges over it and the flow stops")]
    [SerializeField] private float nozzleRadius = 15;

    [Header("Fill")]
    [Tooltip("How many particles the simulation runs with. They all start stacked in the reservoir")]
    [SerializeField] private int particleCount = 60000;
    [SerializeField] private float spacing = 3;
    [SerializeField] private bool useJitter = true;
    [SerializeField] private float jitterStrength = 0.2f;

    [Header("Drain")]
    [Tooltip("Particles that fall below this height are returned to the reservoir")]
    [SerializeField] private float drainHeight = -200;
    [Tooltip("Height of the slab across the top of the reservoir that returning particles reappear in")]
    [SerializeField] private float respawnSlabHeight = 20;

    private float3 HalfSize => sourceSize / 2;

    public float Spacing => spacing;

    public float4[] InitializePositions()
    {
        float4[] positions = new float4[math.max(particleCount, 0)];
        if (positions.Length == 0) return positions;

        float3 low = sourceCenter - HalfSize + spacing / 2;
        int columns = math.max(1, (int)(sourceSize.x / spacing));
        int layers = math.max(1, (int)(sourceSize.z / spacing));
        float jitter = useJitter ? jitterStrength : 0;

        for (int i = 0; i < positions.Length; i++)
        {
            // Fill x then z, then stack layers upwards
            int3 cell = new(i % columns, i / (columns * layers), i / columns % layers);
            float3 jitterOffset = UnityEngine.Random.insideUnitSphere * jitter;
            float3 position = low + (float3)cell * spacing + jitterOffset;
            positions[i] = new float4(position, 0);
        }

        return positions;
    }

    public void SetComputeSettings(ComputeShader compute)
    {
        float3 halfSize = HalfSize;
        compute.SetVector("reservoirCenter", new Vector4(sourceCenter.x, sourceCenter.y, sourceCenter.z));
        compute.SetVector("reservoirHalfSize", new Vector4(halfSize.x, halfSize.y, halfSize.z));
        compute.SetFloat("nozzleRadius", nozzleRadius);
        compute.SetFloat("drainHeight", drainHeight);
        compute.SetFloat("respawnSlabHeight", respawnSlabHeight);
    }

    public void SetRecycleSeed(ComputeShader compute, int seed) => compute.SetInt("recycleSeed", seed);

    public void Validate(Tank tank, float interactionRadius, float particleRadius)
    {
        float3 halfSize = HalfSize;

        if (math.any(halfSize <= particleRadius))
            Debug.LogError($"Fluid flow: sourceSize is {sourceSize}, which is thinner than a particle on at least one axis, so the reservoir has no inside");

        if (math.any(math.abs(sourceCenter) + halfSize > tank.innerHalfSize))
            Debug.LogWarning($"Fluid flow: the reservoir reaches outside the tank (half size {tank.innerHalfSize}), so its walls and the tank walls will fight over the same particles");

        if (nozzleRadius < interactionRadius)
            Debug.LogWarning($"Fluid flow: nozzleRadius is {nozzleRadius}, below interactionRadius {interactionRadius}. The fluid will bridge over the hole and the flow will stop");

        if (nozzleRadius >= math.cmin(halfSize.xz))
            Debug.LogWarning($"Fluid flow: nozzleRadius {nozzleRadius} is as wide as the reservoir, so there is no floor left to hold the fluid");

        float floorHeight = sourceCenter.y - halfSize.y;
        if (drainHeight >= floorHeight)
            Debug.LogError($"Fluid flow: drainHeight {drainHeight} is at or above the reservoir floor ({floorHeight}), so particles are drained before they can fall");

        if (drainHeight <= -tank.innerHalfSize.y)
            Debug.LogWarning($"Fluid flow: drainHeight {drainHeight} is at or below the tank floor ({-tank.innerHalfSize.y}), so particles pile up on the floor instead of draining");

        int perLayer = math.max(1, (int)(sourceSize.x / spacing)) * math.max(1, (int)(sourceSize.z / spacing));
        float fillHeight = math.ceil((float)particleCount / perLayer) * spacing;
        if (fillHeight > sourceSize.y)
            Debug.LogWarning($"Fluid flow: {particleCount} particles at spacing {spacing} stack {fillHeight} high, past the reservoir's {sourceSize.y}. Widen or heighten it, or spawn fewer");

        if (respawnSlabHeight <= 0 || respawnSlabHeight > sourceSize.y)
            Debug.LogWarning($"Fluid flow: respawnSlabHeight is {respawnSlabHeight}, which doesn't fit in a reservoir {sourceSize.y} tall");
    }

    private void OnDrawGizmosSelected()
    {
        float3 halfSize = HalfSize;

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(sourceCenter, sourceSize);

        Gizmos.color = Color.yellow;
        float3 floorCentre = new(sourceCenter.x, sourceCenter.y - halfSize.y, sourceCenter.z);
        float3 previous = floorCentre + new float3(nozzleRadius, 0, 0);
        for (int i = 1; i <= 32; i++)
        {
            float angle = i * 2 * math.PI / 32;
            float3 point = floorCentre + new float3(math.cos(angle), 0, math.sin(angle)) * nozzleRadius;
            Gizmos.DrawLine(previous, point);
            previous = point;
        }

        Gizmos.color = Color.red;
        Gizmos.DrawWireCube(new float3(sourceCenter.x, drainHeight, sourceCenter.z), new float3(sourceSize.x, 0, sourceSize.z));
    }
}
