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
        [SerializeField] private int bodyResolution;
        [SerializeField] private Material mat;

        private List<Matrix4x4> matrices = new();
        private List<Vector4> colorsBuffer = new();
        private MaterialPropertyBlock mpb;

        private List<Matrix4x4> customMatrices = new();
        private List<Vector4> customColorsBuffer = new();
        private MaterialPropertyBlock customMpb;

        private Mesh mesh;
        private Mesh customMesh;
        private const int batchSize = 1024;
        private const float meshRadius = 0.5f;
        private const int submeshIndex = 0;
        private readonly int colors = Shader.PropertyToID("_Color");
        private readonly Vector3 scale = Vector3.one;

        public void InitializeParticles(float2[] positions)
        {
            mesh ??= MeshGenerator.Sphere(meshRadius, resolution);
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
            // todo add error catching
            // todo try using regular for
            Parallel.For(0, positions.Length, i =>
            {
                matrices[i] = Matrix4x4.Translate(new(positions[i].x, positions[i].y));
                colorsBuffer[i] = GetColorVector(velocities[i]);
            });

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

        public void InitCustomParticle(float2 position, float radius, Color color)
        {
            customMesh ??= MeshGenerator.Sphere(meshRadius, bodyResolution);

            customMatrices.Add(Matrix4x4.TRS(
                new(position.x, position.y),
                Quaternion.identity,
                new(radius * 2, radius * 2, 1)
            ));

            customMpb ??= new MaterialPropertyBlock();
            customMpb.SetColor(colors, color);
        }

        public void DrawCustomParticle(float2 position, int index = 0)
        {
            customMatrices[index] = Matrix4x4.TRS(
                new(position.x, position.y),
                Quaternion.identity,
                customMatrices[index].lossyScale
            );

            Graphics.DrawMeshInstanced(
                customMesh,
                submeshIndex,
                mat,
                customMatrices,
                customMpb
            );
        }

        public void DrawCustomParticles(float2[] positions)
        {
            if (customMatrices.Count != positions.Length)
            {
                if (customMatrices.Count < positions.Length)
                {
                    Debug.LogError($"Render: Trying to draw more custom particles than there is matrices. Positions: {positions.Length}, matrices: {customMatrices.Count}");
                    return;
                }

                else
                    Debug.LogWarning($"Render: There are more custom matrices than positions. Matrices: {customMatrices.Count}, positions: {positions.Length}");
            }

            for (int i = 0; i < positions.Length; i++)
                customMatrices[i] = Matrix4x4.Translate(new(positions[i].x, positions[i].y));

            if (customMatrices.Count <= batchSize)
            {
                Graphics.DrawMeshInstanced(
                    mesh,
                    submeshIndex,
                    mat,
                    customMatrices,
                    customMpb
                );
            }

            else
                Debug.LogError($"Render: Too many custom particles: {customMatrices.Count}");
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
    }
}