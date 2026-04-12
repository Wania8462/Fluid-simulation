using System;
using Unity.Mathematics;
using UnityEngine;

namespace SimulationLogic
{
    public class InitializeParticles : MonoBehaviour
    {
        [Header("Spawn settings")]
        [SerializeField] private int particleSquareLength = 50;
        [SerializeField] private bool spawnCircle;
        [SerializeField] private float2 spawnOffset;
        [SerializeField] private float spacing = 2;
        [SerializeField] private bool useJitter = true;
        [SerializeField] private float jitterStrength = 0.2f;
        [SerializeField] private float2 boundingBoxSizeOffset = new float2(160, 80);
        public float2 boundingBoxSize;

        [Header("Flow settings")]
        // pretend it has private set
        public int spawnPerFlowRow;
        public float flowSpacing = 2;
        public float spawnInterval;

        [Header("Border settings")]
        public float borderDensity;
        public int layers;
        public float layerOffset;

        private int circleArraySize = -1;

        public RefList<int> GetIDs()
        {
            var numParticles = GetNumOfSpawnParticles();
            var IDs = new RefList<int>();

            for (int i = 0; i < numParticles; i++)
                IDs.Add(i);

            return IDs;
        }

        public RefList<float2> InitPositions()
        {
            if (!spawnCircle)
            {
                int len = particleSquareLength;
                RefList<float2> pos = new(len * len);
                jitterStrength = useJitter ? jitterStrength : 0;

                for (int i = 0; i < len; i++)
                {
                    for (int j = 0; j < len; j++)
                    {
                        pos[i * len + j] = new float2(i * spacing + (UnityEngine.Random.insideUnitSphere.x * jitterStrength) - len + 1 + spawnOffset.x,
                                            j * spacing + (UnityEngine.Random.insideUnitSphere.y * jitterStrength) - len + 1 + spawnOffset.y);
                    }
                }

                boundingBoxSize = new float2(particleSquareLength + boundingBoxSizeOffset.x * 2, particleSquareLength + boundingBoxSizeOffset.y * 2);

                if (boundingBoxSize.x == 0 || boundingBoxSize.y == 0)
                    Debug.LogWarning($"Spawn particles: Bounding box size is {boundingBoxSize}");

                return pos;
            }

            else
            {
                int len = particleSquareLength;
                float radius = len * spacing / 2;
                RefList<float2> positions = new();
                float2 origin = new(0, 0);
                jitterStrength = useJitter ? jitterStrength : 0;

                for (int i = 0; i < len; i++)
                {
                    for (int j = 0; j < len; j++)
                    {
                        float2 pos = new(i * spacing + (UnityEngine.Random.insideUnitSphere.x * jitterStrength) - len + 1,
                                            j * spacing + (UnityEngine.Random.insideUnitSphere.y * jitterStrength) - len + 1);

                        if (FluidMath.Distance(origin, pos) < radius)
                            positions.Add(pos);
                    }
                }

                boundingBoxSize = new float2(particleSquareLength + boundingBoxSizeOffset.x * 2, particleSquareLength + boundingBoxSizeOffset.y * 2);

                if (boundingBoxSize.x == 0 || boundingBoxSize.y == 0)
                    Debug.LogWarning($"Spawn particles: Bounding box size is {boundingBoxSize}");

                circleArraySize = positions.Count;
                return positions;
            }
        }

        public RefList<float2> InitBorderPositions()
        {
            var lenX = (int)(boundingBoxSize.x * borderDensity);
            var lenY = (int)(boundingBoxSize.y * borderDensity);
            var particlesPerLayer = lenX * 2 + lenY * 2;
            var totParticles = particlesPerLayer * layers;
            var pos = new RefList<float2>(totParticles);
            var topLeft = new float2(-(boundingBoxSize.x / 2), boundingBoxSize.y / 2);

            for (int i = 0; i < layers; i++)
            {
                for (var j = 0; j < particlesPerLayer; j++)
                {
                    // Top
                    if (j < lenX)
                        pos[j + i * particlesPerLayer] = new float2(topLeft.x + borderDensity * j, topLeft.y - (layerOffset * i));

                    // Bottom
                    else if (j < lenX * 2)
                        pos[j + i * particlesPerLayer] = new float2(topLeft.x + borderDensity * (j - lenX), topLeft.y - boundingBoxSize.y + (layerOffset * i));

                    // Left
                    else if (j < lenX * 2 + lenY)
                        pos[j + i * particlesPerLayer] = new float2(topLeft.x + (layerOffset * i), topLeft.y - borderDensity * (j - lenX * 2));

                    // Right
                    else
                        pos[j + i * particlesPerLayer] = new float2(topLeft.x + boundingBoxSize.x - (layerOffset * i), topLeft.y - borderDensity * (j - (lenX * 2 + lenY)));
                }
            }

            return pos;
        }

        public RefList<float2> InitBodyDensityPoints(int resolution, float radius)
        {
            RefList<float2> res = new(resolution);

            for (int i = 0; i < resolution; i++)
            {
                res[i] = new float2((float)(radius * Math.Cos(Math.PI * (i - 1) / resolution / 2)),
                                    (float)(radius * Math.Sin(Math.PI * (i - 1) / resolution / 2)));
            }

            return res;
        }

        public float2 GetBoundSize()
        {
            if (boundingBoxSize.x == 0 || boundingBoxSize.y == 0)
                boundingBoxSize = new float2(particleSquareLength + boundingBoxSizeOffset.x * 2, particleSquareLength + boundingBoxSizeOffset.y * 2);

            return boundingBoxSize;
        }

        public float2 GetRealHalfBoundSize(float radius)
        {
            if (boundingBoxSize.x == 0 || boundingBoxSize.y == 0)
                boundingBoxSize = new float2(particleSquareLength + boundingBoxSizeOffset.x * 2, particleSquareLength + boundingBoxSizeOffset.y * 2);

            var result = new float2(boundingBoxSize.x / 2 - radius, boundingBoxSize.y / 2 - radius);

            if (result.x <= 0 || result.y <= 0)
                Debug.LogWarning($"SpawnParticles: GetRealHalfBoundSize result is non-positive ({result}) — radius ({radius}) may be too large for the bounding box ({boundingBoxSize})");

            return result;
        }

        public int GetNumOfSpawnParticles()
        {
            if (!spawnCircle)
                return particleSquareLength * particleSquareLength;

            else
            {
                if (circleArraySize == -1)
                    InitPositions();

                return circleArraySize;
            }
        }

        private RefList<T> GetPropperSizedArray<T>()
        {
            if (!spawnCircle)
                return new RefList<T>(particleSquareLength * particleSquareLength);

            else
            {
                if (circleArraySize == -1)
                    InitPositions();

                return new RefList<T>(circleArraySize);
            }
        }
    }
}