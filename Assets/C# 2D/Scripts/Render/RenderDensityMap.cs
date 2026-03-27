using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Mathematics;
using UnityEngine;

namespace Rendering
{
    public class RenderDensityMap : MonoBehaviour
    {
        [SerializeField] private int resolution;
        [SerializeField] private float densityLimt;
        [SerializeField] private Material mat;
        [HideInInspector] public float2[] cells;
        private float2 bounds;
        private int width, height;
        private float cellWidth, cellHeight;

        private Mesh mesh;
        private List<Matrix4x4> matrices;
        private List<Vector4> colorsBuffer;
        private MaterialPropertyBlock mpb;

        private readonly int colors = Shader.PropertyToID("_Color");
        private const int batchSize = 1023;
        private const int submeshIndex = 0;

        public void Init(float2 simBounds)
        {
            bounds = simBounds;
            SetDimentions();
            mesh = mesh == null ? MeshGenerator.Rectangle(cellWidth, cellHeight) : mesh;
            GenerateMatrices();
            GenerateColorsBuffer();
        }

        public void Draw(float[] densities)
        {
            if (densities.Length != cells.Length)
                Debug.LogError($"RenderDensityMap: Length of densities != length of cells. Densities: {densities.Length}, centres: {cells.Length}");

            for (int i = 0; i < cells.Length; i++)
            {
                if (densities[i] > 0) 
                    colorsBuffer[i] = GetColorVector(densities[i]);
            }

            for (var i = 0; i < matrices.Count; i += batchSize)
            {
                var count = Mathf.Min(batchSize, matrices.Count - i);
                mpb.SetVectorArray(colors, colorsBuffer.GetRange(i, count));

                Graphics.DrawMeshInstanced(
                    mesh,
                    submeshIndex,
                    mat,
                    matrices.GetRange(i, count),
                    mpb
                );
            }
        }

        private void SetDimentions()
        {
            if (bounds.x == 0 || bounds.y == 0)
            {
                Debug.LogError("RenderDensityMap: Bounds are 0");
                return;
            }

            var topLeft = new float2(-bounds.x / 2, bounds.y / 2);
            height = resolution;
            width = (int)(resolution * (bounds.x / bounds.y));

            cellHeight = bounds.y / height;
            cellWidth = bounds.x / width;

            var halfHeight = cellHeight / 2;
            var halfWidth = cellWidth / 2;

            cells = new float2[width * height];
            for (int i = 0; i < height; i++)
            {
                for (int j = 0; j < width; j++)
                {
                    cells[width * i + j] = new float2(
                        topLeft.x + halfWidth + (j * cellWidth),
                        topLeft.y - (halfHeight + (i * cellHeight)));
                }
            }
        }

        private void GenerateMatrices()
        {
            matrices ??= new();
            matrices.Clear();

            foreach (var cell in cells)
            {
                matrices.Add(Matrix4x4.TRS(
                    new(cell.x, cell.y),
                    Quaternion.identity,
                    new(cellWidth, cellHeight)
                ));
            }
        }

        private void GenerateColorsBuffer()
        {
            mpb ??= new();
            colorsBuffer ??= new();
            colorsBuffer.Clear();

            for (int i = 0; i < cells.Length; i++)
                colorsBuffer.Add(new Vector4());
        }
        
        private Vector4 GetColorVector(float density) => new(0, 0, Mathf.Clamp01(density / densityLimt), 1);

        // void OnValidate()
        // {
        //     SetDimentions();
        // }

        void OnDestroy()
        {
            Destroy(mesh);
        }
    }
}