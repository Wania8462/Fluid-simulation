using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using UnityEngine;

namespace Rendering
{
    public class Render : MonoBehaviour
    {
        [SerializeField] private int resolution;
        [SerializeField] private Material mat;
        
        private List<Matrix4x4> matrices = new();
        private List<Vector4> colorsBuffer;
        private MaterialPropertyBlock mpb;
        
        private Mesh mesh;
        private const int batchSize = 1024;
        private readonly int colors = Shader.PropertyToID("_Colors");
        private readonly Vector3 scale = Vector3.one;
        
        public void InitializeParticles(Vector2[] positions, float radius)
        {
            mesh ??= MeshGenerator.Sphere(radius, resolution);
            mpb ??= new MaterialPropertyBlock();
            colorsBuffer ??= new List<Vector4>();
            

            foreach (var pos in positions)
            {
                matrices.Add(Matrix4x4.TRS(
                    pos,
                    Quaternion.identity,
                    scale
                ));
                colorsBuffer.Add(new Vector4());
            }
        }

        public void DrawParticles(Vector2[] positions, Vector2[] velocities)
        {
            Parallel.For(0, positions.Length, i =>
            {
                matrices[i] = Matrix4x4.Translate(positions[i]);
                colorsBuffer[i] = GetColorVector(velocities[i]);
            });

            for (var i = 0; i < matrices.Count; i += batchSize)
            {
                var count = Mathf.Min(batchSize, matrices.Count - i);
                mpb.SetVectorArray("_Color", colorsBuffer.GetRange(i, count));
                Graphics.DrawMeshInstanced(
                    mesh,
                    0,
                    mat,
                    matrices.GetRange(i, count),
                    mpb
                );
            }
        }

        public void DeleteParticles()
        {
            mesh = null;
            mpb = null;
            matrices.Clear();
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Vector4 GetColorVector(Vector2 velocity)
        {
            var color = Mathf.Clamp01((Mathf.Abs(velocity.x) + Mathf.Abs(velocity.y)) / 40f);
            return new Vector4(color, 
                0, 
                1 - color,
                1);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Color GetColor(Vector2 velocity)
        {
            var color = Mathf.Clamp01((Mathf.Abs(velocity.x) + Mathf.Abs(velocity.y)) / 40f);
            return new Color(color, 
                0, 
                1 - color);
        }
    }
}