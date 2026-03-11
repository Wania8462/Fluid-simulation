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
#if UNITY_EDITOR
        private readonly List<Matrix4x4> identityMatrixList = new();
        private Material topMatertial;
        private MaterialPropertyBlock lineMpb;
        private Mesh lineMesh;
#endif

        # region Fluid particles
        public void InitParticles(float2[] positions)
        {
            fluidBuffer.matrices ??= new();
            fluidBuffer.colorsBuffer ??= new();
            fluidBuffer.mesh = fluidBuffer.mesh != null ? fluidBuffer.mesh : MeshGenerator.Circle(particleRadius, resolution);
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

        public void DrawParticles(float2[] positions, float2[] velocities, int[] highlightGreen = null, int[] highlightYellow = null)
        {
            Parallel.For(0, positions.Length, i =>
            {
                fluidBuffer.matrices[i] = Matrix4x4.Translate(new(positions[i].x, positions[i].y));
                fluidBuffer.colorsBuffer[i] = GetColorVector(velocities[i]);
            });

            if (highlightGreen != null)
            {
                for (int i = 0; i < highlightGreen.Length; i++)
                    fluidBuffer.colorsBuffer[highlightGreen[i]] = Color.green;
            }

            if (highlightYellow != null)
            {
                for (int i = 0; i < highlightYellow.Length; i++)
                    fluidBuffer.colorsBuffer[highlightYellow[i]] = Color.yellow;
            }

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

        public void DrawParticles(float2[] positions, float2[] velocities, int[] highlightGreen = null, int highlightYellow = -1)
        {
            if (highlightYellow != -1)
                DrawParticles(positions, velocities, highlightGreen, new int[] { highlightYellow });

            else
                DrawParticles(positions, velocities, highlightGreen, null);
        }

        public void DrawParticles(float2[] positions, float2[] velocities, int highlightGreen = -1, int[] highlightYellow = null)
        {
            if (highlightGreen != -1)
                DrawParticles(positions, velocities, new int[] { highlightGreen }, highlightYellow);

            else
                DrawParticles(positions, velocities, null, highlightYellow);
        }

        public void DrawParticles(float2[] positions, float2[] velocities, int highlightGreen = -1, int highlightYellow = -1)
        {
            if (highlightYellow != -1 && highlightYellow != -1)
                DrawParticles(positions, velocities, new int[] { highlightGreen }, new int[] { highlightYellow });

            else if (highlightYellow != -1)
                DrawParticles(positions, velocities, null, new int[] { highlightYellow });

            else if (highlightGreen != -1)
                DrawParticles(positions, velocities, new int[] { highlightGreen }, null);
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
            borderBuffer.mesh ??= MeshGenerator.Circle(particleRadius, resolution);
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
            customBuffer.mesh ??= MeshGenerator.Circle(particleRadius, bodyResolution);

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

        // If needs to be on prod, optimise
        #region Debug
#if UNITY_EDITOR
        void Awake()
        {
            identityMatrixList.Add(Matrix4x4.identity);
            topMatertial = new(mat) { renderQueue = 100_000 };
            lineMpb = new();
            lineMpb.SetColor(colors, new Color(1, 1, 1, 1));
            lineMesh = MeshGenerator.Line(new float2(0, 0), new float2(1, 0), 1f);
        }
        
        public void DrawLine(float2 start, float2 end, float width, Color color)
        {
            float2 dir = end - start;
            float length = math.length(dir);
            if (length < Mathf.Epsilon) return;

            identityMatrixList[0] = Matrix4x4.TRS(
                new Vector3(start.x, start.y),
                Quaternion.Euler(0, 0, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg),
                new Vector3(length, width, 1f));
            lineMpb.SetColor(colors, color);

            Graphics.DrawMeshInstanced(
                lineMesh,
                0,
                topMatertial,
                identityMatrixList,
                lineMpb,
                UnityEngine.Rendering.ShadowCastingMode.Off,
                false
            );
        }

        public void DrawLines(float2 start, float2[] ends, float width)
        {
            for (int i = 0; i < ends.Length; i++)
                DrawLine(start, ends[i], width, Color.white);
        }
#endif
        #endregion

        #region Helpers
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