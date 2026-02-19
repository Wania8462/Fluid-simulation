using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Mathematics;
using System;

namespace Rendering
{
    struct ParticlesBuffer
    {
        public Mesh mesh;
        public List<Matrix4x4> matrices;
        public List<Vector4> colorsBuffer;
        public MaterialPropertyBlock mpb;
    }

    public class Render : MonoBehaviour
    {
        [SerializeField] private int resolution;
        [SerializeField] private int bodyResolution;
        [SerializeField] private Material mat;

        private ParticlesBuffer fluidBuffer;
        private ParticlesBuffer borderBuffer;
        private ParticlesBuffer customBuffer;

        private const int batchSize = 1023;
        private const float particleRadius = 0.5f;
        private const int submeshIndex = 0;
        private readonly int colors = Shader.PropertyToID("_Color");
        private readonly Vector3 scale = Vector3.one;

        # region Fluid particles
        public void InitParticles(float2[] positions)
        {
            fluidBuffer.matrices ??= new();
            fluidBuffer.colorsBuffer ??= new();
            fluidBuffer.mesh ??= MeshGenerator.Sphere(particleRadius, resolution);
            fluidBuffer.mpb ??= new MaterialPropertyBlock();

            foreach (var pos in positions)
            {
                fluidBuffer.matrices.Add(Matrix4x4.TRS(
                    new(pos.x, pos.y),
                    Quaternion.identity,
                    scale
                ));
                fluidBuffer.colorsBuffer.Add(new Vector4());
            }
        }

        public void DrawParticles(float2[] positions, float2[] velocities)
        {
            // todo add error catching
            // todo try using regular for
            // todo try removing colors buffer

            Parallel.For(0, positions.Length, i =>
            {
                fluidBuffer.matrices[i] = Matrix4x4.Translate(new(positions[i].x, positions[i].y));
                fluidBuffer.colorsBuffer[i] = GetColorVector(velocities[i]);
            });

            for (var i = 0; i < fluidBuffer.matrices.Count; i += batchSize)
            {
                var count = Mathf.Min(batchSize, fluidBuffer.matrices.Count - i);
                fluidBuffer.mpb.SetVectorArray(colors, fluidBuffer.colorsBuffer.GetRange(i, count));

                Graphics.DrawMeshInstanced(
                    fluidBuffer.mesh,
                    submeshIndex,
                    mat,
                    fluidBuffer.matrices.GetRange(i, count),
                    fluidBuffer.mpb
                );
            }
        }

        public void DeleteParticles()
        {
            fluidBuffer.mpb = null;
            fluidBuffer.matrices?.Clear();
            fluidBuffer.colorsBuffer?.Clear();
        }
        # endregion

        # region Border particles
        public void InitBorderParticles(float2[] positions)
        {
            borderBuffer.matrices ??= new();
            borderBuffer.colorsBuffer ??= new();
            borderBuffer.mesh ??= MeshGenerator.Sphere(particleRadius, resolution);
            borderBuffer.mpb ??= new MaterialPropertyBlock();
            var grey = ColorToVector(Color.grey);

            foreach (var pos in positions)
            {
                borderBuffer.matrices.Add(Matrix4x4.TRS(
                    new(pos.x, pos.y),
                    Quaternion.identity,
                    scale
                ));
                borderBuffer.colorsBuffer.Add(grey);
            }

            borderBuffer.mpb.SetVectorArray(colors, borderBuffer.colorsBuffer);
        }

        public void DrawBorderParticles()
        {
            for (var i = 0; i < borderBuffer.matrices.Count; i += batchSize)
            {
                Graphics.DrawMeshInstanced(
                    borderBuffer.mesh,
                    submeshIndex,
                    mat,
                    borderBuffer.matrices.GetRange(i, Mathf.Min(batchSize, borderBuffer.matrices.Count - i)),
                    borderBuffer.mpb
                );
            }
        }

        public void DeleteBorderParticles()
        {
            borderBuffer.mpb = null;
            borderBuffer.matrices?.Clear();
            borderBuffer.colorsBuffer?.Clear();
        }
        # endregion

        # region Custom particles
        public void InitCustomParticle(float2 position, float radius, Color color)
        {
            customBuffer.matrices ??= new();
            customBuffer.mesh ??= MeshGenerator.Sphere(particleRadius, bodyResolution);

            customBuffer.matrices.Add(Matrix4x4.TRS(
                new(position.x, position.y),
                Quaternion.identity,
                new(radius * 2, radius * 2, 1)
            ));

            customBuffer.mpb ??= new MaterialPropertyBlock();
            customBuffer.mpb.SetColor(colors, color);
        }

        public void DrawCustomParticle(float2 position, int index = 0)
        {
            customBuffer.matrices[index] = Matrix4x4.TRS(
                new(position.x, position.y),
                Quaternion.identity,
                customBuffer.matrices[index].lossyScale
            );

            Graphics.DrawMeshInstanced(
                customBuffer.mesh,
                submeshIndex,
                mat,
                customBuffer.matrices,
                customBuffer.mpb
            );
        }

        public void DrawAllCustomParticles(float2[] positions)
        {
            if (customBuffer.matrices.Count != positions.Length)
            {
                if (customBuffer.matrices.Count < positions.Length)
                {
                    Debug.LogError($"Render: Trying to draw more custom particles than there is matrices. Positions: {positions.Length}, matrices: {customBuffer.matrices.Count}");
                    return;
                }

                else
                    Debug.LogWarning($"Render: There are more custom matrices than positions. Matrices: {customBuffer.matrices.Count}, positions: {positions.Length}");
            }

            for (int i = 0; i < positions.Length; i++)
                customBuffer.matrices[i] = Matrix4x4.Translate(new(positions[i].x, positions[i].y));

            if (customBuffer.matrices.Count <= batchSize)
            {
                Graphics.DrawMeshInstanced(
                    customBuffer.mesh,
                    submeshIndex,
                    mat,
                    customBuffer.matrices,
                    customBuffer.mpb
                );
            }

            else
                Debug.LogError($"Render: Too many custom particles: {customBuffer.matrices.Count}");
        }

        public void DeleteCustomParticles()
        {
            customBuffer.mpb = null;
            customBuffer.matrices?.Clear();
            customBuffer.colorsBuffer?.Clear();
        }
        # endregion

        public void DeleteAllTypesOfParticles()
        {
            DeleteParticles();
            DeleteBorderParticles();
            DeleteCustomParticles();
        }

        public void DestroyMeshes()
        {
            Destroy(fluidBuffer.mesh);
            Destroy(borderBuffer.mesh);
            Destroy(customBuffer.mesh);
        }

        # region Helpers
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Vector4 ColorToVector(Color color) => new(color.r, color.g, color.b, color.a);
        # endregion
    }
}