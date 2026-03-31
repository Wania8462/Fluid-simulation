using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace SimulationLogic
{
    public class SpawnParticles : MonoBehaviour
    {
        [Header("Spawn settings")]
        [SerializeField] private int particleSquareLength = 50;
        [SerializeField] private bool spawnCircle;
        [SerializeField] private float spacing = 2;
        [SerializeField] private bool useJitter = true;
        [SerializeField] private float jitterStrength = 0.2f;
        [SerializeField] private float2 boundingBoxSizeOffset = new float2(160, 80);
        public float2 boundingBoxSize;

        [Header("Border settings")]
        public float borderDensity;
        public int layers;
        public float layerOffset;

        private int circleArraySize = -1;

        public float2[] InitializePositions()
        {
            if (!spawnCircle)
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

            else
            {
                int len = particleSquareLength;
                float radius = len * spacing / 2;
                List<float2> positions = new();
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

                if (boundingBoxSize.x == 0 || boundingBoxSize.x == 0)
                    Debug.LogWarning($"Bounding box size is {boundingBoxSize}");
            
                circleArraySize = positions.Count;
                return positions.ToArray();
            }
        }

        public float2[] InitializeBoundaryPositions()
        {
            var lenX = (int)(boundingBoxSize.x * borderDensity);
            var lenY = (int)(boundingBoxSize.y * borderDensity);
            var particlesPerLayer = lenX * 2 + lenY * 2;
            var totParticles = particlesPerLayer * layers;
            var pos = new float2[totParticles];
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

        public float2[] InitializePreviousPositions() => GetPropperSizedArray<float2>();

        public float2[] InitializeVelocities() => GetPropperSizedArray<float2>();
        public float2[] InitializeForcesBuffer() => GetPropperSizedArray<float2>();

        public float[] InitializeDensities() => GetPropperSizedArray<float>();

        public float[] InitializeNearDensities() => GetPropperSizedArray<float>();

        public float[] InitializeBoundaryDensities() => GetPropperSizedArray<float>();

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
            
            return new(boundingBoxSize.x / 2 - radius, boundingBoxSize.y / 2 - radius);
        }

        private T[] GetPropperSizedArray<T>()
        {
            if (!spawnCircle)
                return new T[particleSquareLength * particleSquareLength];

            else
            {
                if (circleArraySize == -1)
                    InitializePositions();

                return new T[circleArraySize];
            }
        }
    }
}