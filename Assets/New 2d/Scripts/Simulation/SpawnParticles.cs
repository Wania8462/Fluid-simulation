using System;
using UnityEngine;

namespace SimulationLogic
{
    public class SpawnParticles : MonoBehaviour
    {
        [Header("Spawn settings")]
        [SerializeField] private int particleSquareLength;
        [SerializeField] private float spacing;
        [SerializeField] private bool useJitter;
        [SerializeField] private float jitterStrength;
        [SerializeField] private Vector2 boundingBoxSizeOffset;

        [Header("References")]
        [SerializeField] private Transform cam;

        public Vector2 boundingBoxSize;

        public Vector2[] InitializePositions()
        {
            int len = particleSquareLength;
            Vector2[] pos = new Vector2[len * len];

            jitterStrength = useJitter ? jitterStrength : 0;
            for (int i = 0; i < len; i++)
            {
                for (int j = 0; j < len; j++)
                {
                    pos[i * len + j] = new Vector2(i * spacing + (UnityEngine.Random.insideUnitSphere.x * jitterStrength) - len + 1,
                                          j * spacing + (UnityEngine.Random.insideUnitSphere.y * jitterStrength) - len + 1);
                }
            }

            boundingBoxSize = new Vector2(particleSquareLength + boundingBoxSizeOffset.x, particleSquareLength + boundingBoxSizeOffset.y); ;
            return pos;
        }

        public Vector2[] InitializeBoundaryPositions(float borderDensity)
        {
            var lenX = (int)(boundingBoxSize.x * borderDensity);
            var lenY = (int)(boundingBoxSize.y * borderDensity);
            var pos = new Vector2[lenX * 2 + lenY * 2];
            var topLeft = new Vector2(-(boundingBoxSize.x / 2), boundingBoxSize.y / 2);

            var dx = boundingBoxSize.x / lenX;
            var dy = boundingBoxSize.y / lenY;

            for (var i = 0; i < pos.Length; i++)
            {
                if (i < lenX)
                    pos[i] = new Vector2(topLeft.x + dx * i, topLeft.y);
                
                else if (i < lenX * 2)
                    pos[i] = new Vector2(topLeft.x + dx * (i - lenX), topLeft.y - boundingBoxSize.y);
                
                else if (i < lenX * 2 + lenY)
                    pos[i] = new Vector2(topLeft.x, topLeft.y - dy * (i - lenX * 2));
                
                else
                    pos[i] = new Vector2(topLeft.x + boundingBoxSize.x, topLeft.y - dy * (i - (lenX * 2 + lenY)));
            }
            
            return pos;
        }

        public Vector2[] InitializePreviousPositions() => new Vector2[(int)Math.Pow(particleSquareLength, 2)];

        public Vector2[] InitializeVelocities() => new Vector2[(int)Math.Pow(particleSquareLength, 2)];

        public float[] InitializeDensities() => new float[(int)Math.Pow(particleSquareLength, 2)];
        public float[] InitializeNearDensities() => new float[(int)Math.Pow(particleSquareLength, 2)];

        public Vector2 GetRealHalfBoundSize(float radius) => new(boundingBoxSize.x / 2 - radius, boundingBoxSize.y / 2 - radius);
    }
}