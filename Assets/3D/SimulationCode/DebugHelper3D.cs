using System.Linq;
using Unity.Mathematics;
using UnityEngine;

public static class DebugHelper3D
{
    public static void LogPositions(SimulationManager sim)
    {
        if (!sim.Buffers.TryGetValue("Positions", out ComputeBuffer positions) || positions == null)
        {
            Debug.LogError("Debug helper: there is no Positions buffer, so Setup did not finish", sim);
            return;
        }

        float4[] positionData = ComputeHelper.GetBuffer<float4>(positions);
        if (positionData == null)
        {
            Debug.LogError("Debug helper: the Positions readback failed", sim);
            return;
        }

        sim.Buffers.TryGetValue("Velocities", out ComputeBuffer velocities);
        float4[] velocityData = velocities != null ? ComputeHelper.GetBuffer<float4>(velocities) : null;

        int broken = positionData.Count(p => !math.all(math.isfinite(p.xyz)));
        float3 low = new(float.PositiveInfinity);
        float3 high = new(float.NegativeInfinity);
        float fastest = 0;

        for (int i = 0; i < positionData.Length; i++)
        {
            if (!math.all(math.isfinite(positionData[i].xyz))) continue;

            low = math.min(low, positionData[i].xyz);
            high = math.max(high, positionData[i].xyz);

            if (velocityData != null)
                fastest = math.max(fastest, math.length(velocityData[i].xyz));
        }

        string bounds = broken == positionData.Length ? "none, every position is NaN or infinite" : $"{low} to {high}";

        Debug.Log($"Positions: {positionData.Length} in the buffer, numParticles is {sim.numParticles}, {broken} not finite\n"
                + $"Bounds: {bounds}, tank inner half size {sim.Tank.innerHalfSize}\n"
                + $"Fastest {fastest}, first {positionData[0].xyz}, last {positionData[^1].xyz}", sim);
    }
}
