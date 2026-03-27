using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Rendering
{
    public class RenderMarchingSquares : MonoBehaviour
    {
        private readonly struct InterpolatedCell
        {
            public readonly Vector3 top;
            public readonly Vector3 right;
            public readonly Vector3 bottom;
            public readonly Vector3 left;

            public InterpolatedCell(Vector3 top, Vector3 right, Vector3 bottom, Vector3 left)
            {
                this.top = top;
                this.right = right;
                this.bottom = bottom;
                this.left = left;
            }
        }

        private readonly struct CellVertices
        {
            public readonly Vector3 topLeft;
            public readonly Vector3 topRight;
            public readonly Vector3 bottomRight;
            public readonly Vector3 bottomLeft;

            public CellVertices(Vector3 topLeft, Vector3 topRight, Vector3 bottomRight, Vector3 bottomLeft)
            {
                this.topLeft = topLeft;
                this.topRight = topRight;
                this.bottomRight = bottomRight;
                this.bottomLeft = bottomLeft;
            }

            public Vector3 Centre => (topLeft + topRight + bottomLeft + bottomRight) * 0.25f;
        }

        private readonly struct CellDensities
        {
            public readonly float topLeft;
            public readonly float topRight;
            public readonly float bottomRight;
            public readonly float bottomLeft;

            public CellDensities(float topLeft, float topRight, float bottomRight, float bottomLeft)
            {
                this.topLeft = topLeft;
                this.topRight = topRight;
                this.bottomRight = bottomRight;
                this.bottomLeft = bottomLeft;
            }

            public float Centre => (topLeft + topRight + bottomRight + bottomLeft) * 0.25f;
        }

        [SerializeField] private int quality;
        [SerializeField] private float threashold;

        [SerializeField] private Material mat;
        [SerializeField] private Material testMat;
        [SerializeField] private GameObject testSquare;
        [SerializeField] private int drawIndex;
        private List<GameObject> testObjects = new();
        private int prevDrawIndex;

        private Mesh[] meshes;
        public Vector3[] edges { get; private set; }
        private int width, height;
        private float2[] centres;
        private float cellWidth, cellHeight;

        private const int batchSize = 1023;
        private const int submeshIndex = 0;
        private List<Matrix4x4> matrices;
        private List<Matrix4x4>[] instancedMatrices;

        private Mesh lerpMesh;
        private List<Vector3> lerpVertices;
        private List<int> lerpTriangles;

        private static readonly int[][] triangleTable =
        {
            Array.Empty<int>(),
            new[] { 7, 0, 4 },
            new[] { 4, 1, 5 },
            new[] { 7, 0, 1, 7, 1, 5 },
            new[] { 5, 3, 6 },
            new[] { 7, 0, 4, 5, 3, 6 },
            new[] { 4, 1, 3, 4, 3, 6 },
            new[] { 7, 0, 1, 7, 1, 3, 7, 3, 6 },
            new[] { 6, 2, 7 },
            new[] { 4, 6, 2, 4, 2, 0 },
            new[] { 4, 1, 5, 6, 2, 7 },
            new[] { 0, 1, 5, 0, 5, 6, 0, 6, 2 },
            new[] { 5, 3, 2, 5, 2, 7 },
            new[] { 0, 4, 5, 0, 5, 3, 0, 3, 2 },
            new[] { 4, 1, 3, 4, 3, 2, 4, 2, 7 },
            new[] { 0, 1, 3, 0, 3, 2 }
        };

        public void Init(float2 bounds)
        {
            if (quality < 2)
            {
                Debug.LogError("RednerMarchingSquares: Quality must be >= 2");
                Application.Quit();
            }

            SetupMeshes();
            InitInstancedMatrices();
            GenerateEdges(bounds);
            GenerateCentres(bounds);
            GenerateMatrices();
        }

        #region Draw
        public void DrawLerp(float[] densities)
        {
            if (!ValidateDensities(densities))
                return;

            lerpVertices.Clear();
            lerpTriangles.Clear();

            for (int y = 0; y < height - 1; y++)
            {
                for (int x = 0; x < width - 1; x++)
                {
                    var edgeIndex = y * width + x;
                    var cellVertices = GetCellVertices(edgeIndex);
                    var cellDensities = GetCellDensities(densities, edgeIndex);
                    var meshIndex = GetMeshIndex(cellDensities);
                    
                    if (meshIndex == 0)
                        continue;

                    var interpolated = GetInterpolatedVetrices(cellVertices, cellDensities);
                    AppendTriangles(meshIndex, cellVertices, interpolated, cellDensities);
                }
            }

            lerpMesh.Clear();
            if (lerpVertices.Count == 0)
                return;

            lerpMesh.SetVertices(lerpVertices);
            lerpMesh.SetTriangles(lerpTriangles, 0);
            lerpMesh.RecalculateBounds();

            Graphics.DrawMesh(lerpMesh, Matrix4x4.identity, mat, gameObject.layer);
        }

        public void DrawMidpoints(float[] densities)
        {
            if (!ValidateDensities(densities))
                return;

            ClearInstancedMatrices();

            for (int y = 0; y < height - 1; y++)
            {
                for (int x = 0; x < width - 1; x++)
                {
                    var edgeIndex = y * width + x;
                    var centreIndex = y * (width - 1) + x;
                    var meshIndex = GetMeshIndex(GetCellDensities(densities, edgeIndex));

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
        #endregion

        #region Setup
        private void GenerateEdges(float2 bounds)
        {
            var topLeft = new Vector3(-bounds.x / 2, bounds.y / 2, 0);
            var aspectRatio = bounds.x / bounds.y;
            var edgesList = new List<Vector3>();

            height = quality;
            width = (int)(quality * aspectRatio);
            cellWidth = bounds.x / (width - 1);
            cellHeight = bounds.y / (height - 1);

            for (int i = 0; i < height; i++)
            {
                for (int j = 0; j < width; j++)
                {
                    edgesList.Add(new Vector3(
                        topLeft.x + (cellWidth * j),
                        topLeft.y - (cellHeight * i),
                        0
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

        private void SetupMeshes()
        {
            meshes = MeshGenerator.MarchingSquareVariations();

            lerpMesh = lerpMesh != null ? lerpMesh : new Mesh();
            lerpMesh.MarkDynamic();
            lerpVertices ??= new();
            lerpTriangles ??= new();
        }
        #endregion

        #region Helpers
        private void AppendTriangles(int meshIndex, CellVertices cellVertices, InterpolatedCell interpolatedCell, CellDensities cellDensities)
        {
            var shouldConnect = false;

            if (meshIndex == 5 || meshIndex == 10)
            {
                var decider = ((cellDensities.topLeft - threashold) * (cellDensities.bottomRight - threashold))
                    - ((cellDensities.topRight - threashold) * (cellDensities.bottomLeft - threashold));

                if (Mathf.Abs(decider) > Mathf.Epsilon)
                    shouldConnect = meshIndex == 5 ? decider > 0 : decider < 0;

                else
                    shouldConnect = cellDensities.Centre >= threashold;
            }

            if (shouldConnect)
            {
                if (meshIndex == 5)
                {
                    AddTriangle(cellVertices.Centre, interpolatedCell.left, cellVertices.topLeft);
                    AddTriangle(cellVertices.Centre, cellVertices.topLeft, interpolatedCell.top);
                    AddTriangle(cellVertices.Centre, interpolatedCell.top, interpolatedCell.right);
                    AddTriangle(cellVertices.Centre, interpolatedCell.right, cellVertices.bottomRight);
                    AddTriangle(cellVertices.Centre, cellVertices.bottomRight, interpolatedCell.bottom);
                    AddTriangle(cellVertices.Centre, interpolatedCell.bottom, interpolatedCell.left);
                }

                else
                {
                    AddTriangle(cellVertices.Centre, interpolatedCell.top, cellVertices.topRight);
                    AddTriangle(cellVertices.Centre, cellVertices.topRight, interpolatedCell.right);
                    AddTriangle(cellVertices.Centre, interpolatedCell.right, interpolatedCell.bottom);
                    AddTriangle(cellVertices.Centre, interpolatedCell.bottom, cellVertices.bottomLeft);
                    AddTriangle(cellVertices.Centre, cellVertices.bottomLeft, interpolatedCell.left);
                    AddTriangle(cellVertices.Centre, interpolatedCell.left, interpolatedCell.top);
                }

                return;
            }

            var triangleIndices = triangleTable[meshIndex];
            var startIndex = lerpVertices.Count;

            for (int i = 0; i < triangleIndices.Length; i++)
            {
                lerpVertices.Add(GetVertex(triangleIndices[i], cellVertices, interpolatedCell));
                lerpTriangles.Add(startIndex + i);
            }
        }

        private int GetMeshIndex(CellDensities cellDensities)
        {
            var meshIndex = 0;

            if (cellDensities.topLeft >= threashold) meshIndex |= 1;
            if (cellDensities.topRight >= threashold) meshIndex |= 2;
            if (cellDensities.bottomRight >= threashold) meshIndex |= 4;
            if (cellDensities.bottomLeft >= threashold) meshIndex |= 8;

            return meshIndex;
        }

        private void AddTriangle(Vector3 a, Vector3 b, Vector3 c)
        {
            var startIndex = lerpVertices.Count;
            lerpVertices.Add(a);
            lerpVertices.Add(b);
            lerpVertices.Add(c);
            lerpTriangles.Add(startIndex);
            lerpTriangles.Add(startIndex + 1);
            lerpTriangles.Add(startIndex + 2);
        }

        private Vector3 Interpolate(Vector3 start, Vector3 end, float startDensity, float endDensity)
        {
            var delta = endDensity - startDensity;
            var t = Mathf.Abs(delta) < Mathf.Epsilon ? 0.5f : Mathf.Clamp01((threashold - startDensity) / delta);
            return Vector3.Lerp(start, end, t);
        }

        private static Vector3 GetVertex(int index, CellVertices cellVertices, InterpolatedCell interpolatedCell) => index switch
            {
                0 => cellVertices.topLeft,
                1 => cellVertices.topRight,
                2 => cellVertices.bottomLeft,
                3 => cellVertices.bottomRight,
                4 => interpolatedCell.top,
                5 => interpolatedCell.right,
                6 => interpolatedCell.bottom,
                7 => interpolatedCell.left,
                _ => Vector3.zero
            };

        private CellVertices GetCellVertices(int edgeIndex) => new(
            edges[edgeIndex],
            edges[edgeIndex + 1],
            edges[edgeIndex + width + 1],
            edges[edgeIndex + width]);

        private CellDensities GetCellDensities(float[] densities, int edgeIndex) => new(
            densities[edgeIndex],
            densities[edgeIndex + 1],
            densities[edgeIndex + width + 1],
            densities[edgeIndex + width]);

        private InterpolatedCell GetInterpolatedVetrices(CellVertices vertices, CellDensities densities) => new(
            Interpolate(vertices.topLeft, vertices.topRight, densities.topLeft, densities.topRight),
            Interpolate(vertices.topRight, vertices.bottomRight, densities.topRight, densities.bottomRight),
            Interpolate(vertices.bottomLeft, vertices.bottomRight, densities.bottomLeft, densities.bottomRight),
            Interpolate(vertices.topLeft, vertices.bottomLeft, densities.topLeft, densities.bottomLeft)
        );

        private bool ValidateDensities(float[] densities)
        {
            if (densities.Length == edges.Length)
                return true;

            Debug.LogError($"RenderMarchingSquares: Length of densities != length of edges. Densities: {densities.Length}, edges: {edges.Length}");
            return false;
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
            Destroy(lerpMesh);
        }
        #endregion

        #region Debug
        private void DrawEdges()
        {
            testSquare.GetComponent<SpriteRenderer>().color = Color.white;
            foreach (var edge in edges)
                Instantiate(testSquare, edge, Quaternion.identity);
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

            testObjects.Add(Instantiate(testSquare, edges[drawIndex + offset], Quaternion.identity));
            testObjects.Add(Instantiate(testSquare, edges[drawIndex + offset + 1], Quaternion.identity));
            testObjects.Add(Instantiate(testSquare, edges[drawIndex + offset + width], Quaternion.identity));
            testObjects.Add(Instantiate(testSquare, edges[drawIndex + offset + width + 1], Quaternion.identity));
        }
        #endregion
    }
}