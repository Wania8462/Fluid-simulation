using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Mathematics;

namespace Rendering
{
    public class Render : MonoBehaviour
    {
        [SerializeField] private int resolution;
        [SerializeField] private Material mat;

        private List<Matrix4x4> matrices = new();
        private List<Vector4> colorsBuffer = new();
        private MaterialPropertyBlock mpb;

        private List<Matrix4x4> customMatrices = new();
        private List<Vector4> customColorsBuffer = new();
        private MaterialPropertyBlock customMpb;

        private Mesh mesh;
        private const int batchSize = 1024;
        private readonly int colors = Shader.PropertyToID("_Colors");
        private readonly Vector3 scale = Vector3.one;

        public void InitializeParticles(float2[] positions, float radius)
        {
            mesh ??= MeshGenerator.Sphere(radius, resolution);
            mpb ??= new MaterialPropertyBlock();

            foreach (var pos in positions)
            {
                matrices.Add(Matrix4x4.TRS(
                    new(pos.x, pos.y),
                    Quaternion.identity,
                    scale
                ));
                colorsBuffer.Add(new Vector4());
            }
        }

        public void DrawParticles(float2[] positions, float2[] velocities)
        {
            Parallel.For(0, positions.Length, i =>
            {
                matrices[i] = Matrix4x4.Translate(new(positions[i].x, positions[i].y));
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

        public void InitCustomParticle(float2 position, float radius, Color color)
        {
            mesh ??= MeshGenerator.Sphere(radius, resolution);

            customMatrices.Add(Matrix4x4.TRS(
                new(position.x, position.y),
                Quaternion.identity,
                new(radius, radius, 1)
            ));

            customMpb ??= new MaterialPropertyBlock();
            customMpb.SetColor(colors, color);
        }

        public void DrawCustomParticles()
        {
            if (customMatrices.Count <= batchSize)
            {
                Graphics.DrawMeshInstanced(
                    mesh,
                    0,
                    mat,
                    customMatrices,
                    customMpb
                );
            }

            else
                Debug.LogError($"Too many custom particles: {customMatrices.Count}");
        }

        public void DeleteCustomParticles()
        {
            customMpb = null;
            customMatrices.Clear();
            customColorsBuffer.Clear();
        }

        public void DeleteParticles()
        {
            mpb = null;
            matrices.Clear();
            colorsBuffer.Clear();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float4 GetColorVector(Vector2 velocity)
        {
            var color = Mathf.Clamp01((Mathf.Abs(velocity.x) + Mathf.Abs(velocity.y)) / 40f);
            return new float4(color,
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float4 ColorToVector(Color color) => new(color.r, color.g, color.b, color.a);
    }
}