using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using SimulationLogic;

namespace Rendering
{   
    public class RenderMarchingSquares : MonoBehaviour
    {
        [SerializeField] private int quality;
        [SerializeField] private float threashold;

        // Marching squares
        private Mesh[] meshes;
        private float2[] edges;
        private float2[] centres;
        private float scale;

        // Draw buffers
        private List<Matrix4x4> matrices;
        private List<Vector4> colorsBuffer;
        private MaterialPropertyBlock mpb;
        private readonly int colors = Shader.PropertyToID("_Color");

        public void Init(float2 bounds)
        {
            if (quality < 2)
            {
                Debug.LogError("RednerMarchingSquares: Quality must be >= 2");
                Application.Quit();
            }

            meshes = MeshGenerator.MarchingSquareVariations();
            GenerateEdges(bounds);
            GenerateCentres(bounds);
            GenerateDrawBuffers();
        }

        public void Draw()
        {
            for (int i = 0; i < edges.Length; i++)
            {

            }
        }
        
        // private int GetMeshCase(int index)
        // {
            
        // }

        private void GenerateEdges(float2 bounds)
        {
            var topLeft = new float2(-bounds.x / 2, bounds.y / 2);
            var aspectRatio = bounds.x / bounds.y;
            var edgesList = new List<float2>();
            var len = bounds.y / (quality - 1);
            var width = (int)(quality * aspectRatio);

            for (int i = 0; i < quality; i++)
            {
                for (int j = 0; j < width; j++)
                {
                    edgesList.Add(new float2(
                        topLeft.x + (len * j),
                        topLeft.y - (len * i)
                    ));
                }
            }

            edges = edgesList.ToArray();
            scale = len;
        }

        private void GenerateCentres(float2 bounds)
        {
            var centresList = new List<float2>();
            var aspectRatio = bounds.x / bounds.y;
            var width = (int)(quality * aspectRatio);

            for (int i = 0; i < quality - 1; i++)
            {
                for (int j = 0; j < width - 1; j++)
                {
                    centresList.Add(new float2(
                        edges[i * width + j].x + scale / 2,
                        edges[i * width + j].y - scale / 2
                    ));
                }
            }

            centres = centresList.ToArray();
        }
        
        private void GenerateDrawBuffers()
        {
            matrices ??= new();
            colorsBuffer ??= new();
            mpb ??= new MaterialPropertyBlock();

            for (int i = 0; i < centres.Length; i++)
            {
                matrices.Add(Matrix4x4.TRS(
                    new(edges[i].x, edges[i].y),
                    Quaternion.identity,
                    new(scale, scale, 1)
                ));

                colorsBuffer.Add(Color.blue);
            }

            mpb.SetVectorArray(colors, colorsBuffer);
        }
    }
}