using System;
using UnityEngine;

namespace SimulationLogic
{
    public class SpawnParticles : MonoBehaviour
    {
        [Header("Spawn settings")]
        [SerializeField] private int particleSquareLength;
        [SerializeField] private float spacing;
        [SerializeField] private float jitterStrength;
        [SerializeField] private Vector2 boundingBoxSizeOffset;

        [Header("References")]
        [SerializeField] private Transform cam;

        public Vector2 boundingBoxSize;
        private const float particleRadius = 0.5f;

        public Vector2[] InitializePositions()
        {
            int len = particleSquareLength;
            Vector2[] pos = new Vector2[(int)Math.Pow(len, 2)];

            for (int i = 0; i < len; i++)
            {
                for (int j = 0; j < len; j++)
                {
                    pos[i * len + j] = new(i * spacing + (UnityEngine.Random.insideUnitSphere.x * jitterStrength) - len + 1,
                                          j * spacing + (UnityEngine.Random.insideUnitSphere.y * jitterStrength) - len + 1);
                }
            }

            boundingBoxSize = new(particleSquareLength + boundingBoxSizeOffset.x, particleSquareLength + boundingBoxSizeOffset.y);
            Camera.main.orthographicSize = 0.5f * particleSquareLength + 32;
            return pos;
        }

        public Vector2[] InitializePreviousPositions() => new Vector2[(int)Math.Pow(particleSquareLength, 2)];

        public Vector2[] InitializeVelocities() => new Vector2[(int)Math.Pow(particleSquareLength, 2)];

        public float[] InitializeDensities() => new float[(int)Math.Pow(particleSquareLength, 2)];
        public float[] InitializeNearDensities() => new float[(int)Math.Pow(particleSquareLength, 2)];

        public Vector2 GetRealHalfBoundSize() => new(boundingBoxSize.x / 2 - particleRadius, boundingBoxSize.y / 2 - particleRadius);
    }
}