using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Rendering
{
    public class RenderMarchingSquares : MonoBehaviour
    {
        [SerializeField] private int quality;
        [SerializeField] private float threashold;

        [SerializeField] private Material mat;
        [SerializeField] private Material testMat;
        [SerializeField] private GameObject testSquare;
        [SerializeField] private int drawIndex;
        private List<GameObject> testObjects = new();
        private int prevDrawIndex;

        // Marching squares
        private Mesh[] meshes;
        public float2[] edges { get; private set; }
        private int width, height;
        private float2[] centres;
        private float cellWidth, cellHeight;

        private const int batchSize = 1023;
        private const int submeshIndex = 0;
        private List<Matrix4x4> matrices;
        private List<Matrix4x4>[] instancedMatrices;

        public void Init(float2 bounds)
        {
            if (quality < 2)
            {
                Debug.LogError("RednerMarchingSquares: Quality must be >= 2");
                Application.Quit();
            }

            meshes = MeshGenerator.MarchingSquareVariations();
            InitInstancedMatrices();
            GenerateEdges(bounds);
            GenerateCentres(bounds);
            GenerateMatrices();
        }

        public void DrawLerp(float[] densities)
        {

        }

        public void DrawMidpoints(float[] densities)
        {
            if (densities.Length != centres.Length)
                Debug.LogError($"RenderMarchingSquares: Length of densities != length of centres. Densities: {densities.Length}, centres: {centres.Length}");

            ClearInstancedMatrices();

            for (int y = 0; y < height - 1; y++)
            {
                for (int x = 0; x < width - 1; x++)
                {
                    var edgeIndex = y * width + x;
                    var centreIndex = y * (width - 1) + x;
                    var meshIndex = 0;

                    if (densities[edgeIndex] >= 4) meshIndex |= 1;
                    if (densities[edgeIndex + 1] >= 4) meshIndex |= 2;
                    if (densities[edgeIndex + width + 1] >= 4) meshIndex |= 4;
                    if (densities[edgeIndex + width] >= 4) meshIndex |= 8;

                    if (meshIndex != 0)
                        instancedMatrices[meshIndex].Add(matrices[centreIndex]);
                }
            }

            for (int i = 1; i < meshes.Length; i++)
            {
                for (var j = 0; j < instancedMatrices[i].Count; j += batchSize)
                {
                    var temp = instancedMatrices[i].GetRange(j, Mathf.Min(instancedMatrices[i].Count - j, batchSize));
                    Graphics.DrawMeshInstanced(
                        meshes[i],
                        submeshIndex,
                        mat,
                        temp);
                }
            }
        }

        private void GenerateEdges(float2 bounds)
        {
            var topLeft = new float2(-bounds.x / 2, bounds.y / 2);
            var aspectRatio = bounds.x / bounds.y;
            var edgesList = new List<float2>();

            height = quality;
            width = (int)(quality * aspectRatio);
            cellWidth = bounds.x / (width - 1);
            cellHeight = bounds.y / (height - 1);

            for (int i = 0; i < height; i++)
            {
                for (int j = 0; j < width; j++)
                {
                    edgesList.Add(new float2(
                        topLeft.x + (cellWidth * j),
                        topLeft.y - (cellHeight * i)
                    ));
                }
            }

            edges = edgesList.ToArray();
        }

        private void GenerateCentres(float2 bounds)
        {
            var centresList = new List<float2>();

            for (int i = 0; i < height - 1; i++)
            {
                for (int j = 0; j < width - 1; j++)
                {
                    centresList.Add(new float2(
                        edges[i * width + j].x + cellWidth / 2,
                        edges[i * width + j].y - cellHeight / 2
                    ));
                }
            }

            centres = centresList.ToArray();
        }

        private void GenerateMatrices()
        {
            matrices ??= new();

            for (int i = 0; i < centres.Length; i++)
            {
                matrices.Add(Matrix4x4.TRS(
                    new(centres[i].x, centres[i].y),
                    Quaternion.identity,
                    new(cellWidth, cellHeight, 1)
                ));
            }
        }

        private void InitInstancedMatrices()
        {
            if (instancedMatrices != null)
                ClearInstancedMatrices();

            else
            {
                instancedMatrices = new List<Matrix4x4>[16];

                for (int i = 0; i < instancedMatrices.Length; i++)
                    instancedMatrices[i] = new();
            }
        }

        private void ClearInstancedMatrices()
        {
            foreach (var list in instancedMatrices)
                list.Clear();
        }

        public void DestroyMeshes()
        {
            if (meshes == null) return;
            for (int i = 0; i < meshes.Length; i++)
                Destroy(meshes[i]);
        }

        #region Debug
        private void DrawEdges()
        {
            testSquare.GetComponent<SpriteRenderer>().color = Color.white;
            foreach (var edge in edges)
                Instantiate(testSquare, new Vector3(edge.x, edge.y), Quaternion.identity);
        }

        private void DrawCentres()
        {
            testSquare.GetComponent<SpriteRenderer>().color = Color.purple;
            foreach (var centre in centres)
                Instantiate(testSquare, new Vector3(centre.x, centre.y), Quaternion.identity);
        }

        private void DrawSquare()
        {
            foreach (var testObject in testObjects)
                Destroy(testObject);

            var offset = Mathf.FloorToInt((float)drawIndex / (width - 1));
            Debug.Log($"Draw Index: {drawIndex}, width: {width - 1}, offset: {offset}");
            testSquare.GetComponent<SpriteRenderer>().color = Color.purple;
            testObjects.Add(Instantiate(testSquare, new Vector3(centres[drawIndex].x, centres[drawIndex].y), Quaternion.identity));
            testSquare.GetComponent<SpriteRenderer>().color = Color.white;

            testObjects.Add(Instantiate(testSquare, new Vector3(edges[drawIndex + offset].x, edges[drawIndex + offset].y), Quaternion.identity));
            testObjects.Add(Instantiate(testSquare, new Vector3(edges[drawIndex + offset + 1].x, edges[drawIndex + offset + 1].y), Quaternion.identity));
            testObjects.Add(Instantiate(testSquare, new Vector3(edges[drawIndex + offset + width].x, edges[drawIndex + offset + width].y), Quaternion.identity));
            testObjects.Add(Instantiate(testSquare, new Vector3(edges[drawIndex + offset + width + 1].x, edges[drawIndex + offset + width + 1].y), Quaternion.identity));
        }
        #endregion
    }
}