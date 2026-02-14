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

        public float2[] InitializeBoundaryPositions(float borderDensity)
        {
            var lenX = (int)(boundingBoxSize.x * borderDensity);
            var lenY = (int)(boundingBoxSize.y * borderDensity);
            var pos = new float2[lenX * 2 + lenY * 2];
            var topLeft = new float2(-(boundingBoxSize.x / 2), boundingBoxSize.y / 2);

            var dx = boundingBoxSize.x / lenX;
            var dy = boundingBoxSize.y / lenY;

            for (var i = 0; i < pos.Length; i++)
            {
                if (i < lenX)
                    pos[i] = new float2(topLeft.x + dx * i, topLeft.y);
                
                else if (i < lenX * 2)
                    pos[i] = new float2(topLeft.x + dx * (i - lenX), topLeft.y - boundingBoxSize.y);
                
                else if (i < lenX * 2 + lenY)
                    pos[i] = new float2(topLeft.x, topLeft.y - dy * (i - lenX * 2));
                
                else
                    pos[i] = new float2(topLeft.x + boundingBoxSize.x, topLeft.y - dy * (i - (lenX * 2 + lenY)));
            }
            
            return pos;
        }

        public float2[] InitializePreviousPositions() => new float2[(int)Math.Pow(particleSquareLength, 2)];

        public float2[] InitializeVelocities() => new float2[(int)Math.Pow(particleSquareLength, 2)];

        public float[] InitializeDensities() => new float[(int)Math.Pow(particleSquareLength, 2)];
        public float[] InitializeNearDensities() => new float[(int)Math.Pow(particleSquareLength, 2)];

        public float2 GetRealHalfBoundSize(float radius) => new(boundingBoxSize.x / 2 - radius, boundingBoxSize.y / 2 - radius);
    }
}