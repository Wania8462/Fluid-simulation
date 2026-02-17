using System;
using Unity.Mathematics;
using UnityEngine;

namespace SimulationLogic
{
    public class SpawnParticles : MonoBehaviour
    {
        [Header("Spawn settings")]
        [SerializeField] private int particleSquareLength = 50;
        [SerializeField] private float spacing = 2;
        [SerializeField] private bool useJitter = true;
        [SerializeField] private float jitterStrength = 0.2f;
        [SerializeField] private float2 boundingBoxSizeOffset = new float2(160, 80);

        public float2 boundingBoxSize;

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

            boundingBoxSize = new float2(particleSquareLength + boundingBoxSizeOffset.x * 2, particleSquareLength + boundingBoxSizeOffset.y * 2); ;
            return pos;
        }

        public float2[] InitializeBoundaryPositions(float borderDensity, int layers, float layerOffset)
        {
            // For now layers don't account for the cornders so some fluid particles might escape
            
            // Number of particles on each side
            var lenX = (int)(boundingBoxSize.x * borderDensity);
            var lenY = (int)(boundingBoxSize.y * borderDensity);
            // Total number of particles
            var pos = new float2[lenX * 2 + lenY * 2 * layers];
            var topLeft = new float2(-(boundingBoxSize.x / 2), boundingBoxSize.y / 2);

            // Distance between particles
            var dx = boundingBoxSize.x / lenX;
            var dy = boundingBoxSize.y / lenY;

            for (int i = 0; i < layers; i++)
            {
                for (var j = 0; j < pos.Length; j++)
                {
                    // Top
                    if (j < lenX)
                        pos[j] = new float2(topLeft.x + dx * j, topLeft.y - (layerOffset * i));

                    // Bottom
                    else if (j < lenX * 2)
                        pos[j] = new float2(topLeft.x + dx * (j - lenX), topLeft.y - boundingBoxSize.y + (layerOffset * i));

                    // Left
                    else if (j < lenX * 2 + lenY)
                        pos[j] = new float2(topLeft.x + (layerOffset * i), topLeft.y - dy * (j - lenX * 2));

                    // Right
                    else
                        pos[j] = new float2(topLeft.x + boundingBoxSize.x - (layerOffset * i), topLeft.y - dy * (j - (lenX * 2 + lenY)));
                }
            }

            return pos;
        }

        public float2[] InitializePreviousPositions() => new float2[(int)Math.Pow(particleSquareLength, 2)];

        public float2[] InitializeVelocities() => new float2[(int)Math.Pow(particleSquareLength, 2)];

        public float[] InitializeDensities() => new float[(int)Math.Pow(particleSquareLength, 2)];

        public float[] InitializeNearDensities() => new float[(int)Math.Pow(particleSquareLength, 2)];

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

        public float2 GetRealHalfBoundSize(float radius) => new(boundingBoxSize.x / 2 - radius, boundingBoxSize.y / 2 - radius);
    }
}