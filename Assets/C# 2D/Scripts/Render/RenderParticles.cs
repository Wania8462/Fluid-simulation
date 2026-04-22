using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Mathematics;

namespace Rendering
{
    struct ParticlesBuffer
    {
        public Mesh mesh;
        public List<Matrix4x4> matrices;
        public List<Vector4> colorsBuffer;
        public MaterialPropertyBlock mpb;
    }

    public class RenderParticles : MonoBehaviour
    {
        [SerializeField] private int resolution;
        [SerializeField] private int bodyResolution;
        [SerializeField] private float redThreshold;
        [SerializeField] private Material mat;

        private ParticlesBuffer fluidBuffer;
        private ParticlesBuffer borderBuffer;
        private ParticlesBuffer customBuffer;
        
        private readonly Vector4[] colorsBatch = new Vector4[batchSize];
        private readonly Matrix4x4[] matricesBatch = new Matrix4x4[batchSize];

        private const int batchSize = 1023;
        private const int parallelForBatchSize = 512;
        private const float particleRadius = 0.5f;
        private const int submeshIndex = 0;
        private readonly int colors = Shader.PropertyToID("_Color");
        private readonly Vector3 scale = Vector3.one;

        private readonly List<Matrix4x4> identityMatrixList = new();
        private Material topMatertial;
        private MaterialPropertyBlock lineMpb;
        private Mesh lineMesh;

        # region Fluid particles
        public void InitParticles(int maxNumParticles = 100_000)
        {
            fluidBuffer.matrices ??= new();
            fluidBuffer.colorsBuffer ??= new();
            fluidBuffer.mesh = fluidBuffer.mesh != null ? fluidBuffer.mesh : MeshGenerator.Circle(particleRadius, resolution);
            fluidBuffer.mpb ??= new MaterialPropertyBlock();
            fluidBuffer.mpb.SetVectorArray(colors, new Vector4[batchSize]);

            for (int i = 0; i < maxNumParticles; i++)
            {
                fluidBuffer.matrices.Add(Matrix4x4.TRS(
                    new(0, 0),
                    Quaternion.identity,
                    scale
                ));
                fluidBuffer.colorsBuffer.Add(new Vector4());
            }
        }

        public void InitParticles(float2[] positions, int count = -1)
        {
            fluidBuffer.matrices ??= new();
            fluidBuffer.colorsBuffer ??= new();
            fluidBuffer.mesh = fluidBuffer.mesh != null ? fluidBuffer.mesh : MeshGenerator.Circle(particleRadius, resolution);
            fluidBuffer.mpb ??= new MaterialPropertyBlock();
            fluidBuffer.mpb.SetVectorArray(colors, new Vector4[batchSize]);
            var length = count == -1 ? positions.Length : count;

            for (int i = 0; i < length; i++)
            {
                fluidBuffer.matrices.Add(Matrix4x4.TRS(
                    new(positions[i].x, positions[i].y),
                    Quaternion.identity,
                    scale
                ));
                fluidBuffer.colorsBuffer.Add(new Vector4());
            }
        }

        public void DrawParticles(float2[] positions, float2[] velocities, int count = -1, List<int> highlightGreen = null, List<int> highlightYellow = null)
        {
            if (positions.Length != velocities.Length)
                Debug.LogWarning("Render particles: length of positions is different to length of velocities");

            var length = positions.Length;
            if (count != -1) length = count;

            UpdateParticleBuffers(positions, velocities, length);
            ApplyHighlight(highlightGreen, Color.green);
            ApplyHighlight(highlightYellow, Color.yellow);
            DrawParticleBatches(length);
        }

        public void DrawParticles(float2[] positions, float2[] velocities, int count = -1, List<int> highlightGreen = null, int highlightYellow = -1)
        {
            if (positions.Length != velocities.Length)
                Debug.LogWarning("Render particles: length of positions is different to length of velocities");

            var length = positions.Length;
            if (count != -1) length = count;

            UpdateParticleBuffers(positions, velocities, length);
            ApplyHighlight(highlightGreen, Color.green);
            ApplyHighlight(highlightYellow, Color.yellow);
            DrawParticleBatches(length);
        }

        public void DrawParticles(float2[] positions, float2[] velocities, int count = -1, int highlightGreen = -1, List<int> highlightYellow = null)
        {
            if (positions.Length != velocities.Length)
                Debug.LogWarning("Render particles: length of positions is different to length of velocities");

            var length = positions.Length;
            if (count != -1) length = count;

            UpdateParticleBuffers(positions, velocities, length);
            ApplyHighlight(highlightGreen, Color.green);
            ApplyHighlight(highlightYellow, Color.yellow);
            DrawParticleBatches(length);
        }

        public void DrawParticles(float2[] positions, float2[] velocities, int count = -1, int highlightGreen = -1, int highlightYellow = -1)
        {
            if (positions.Length != velocities.Length)
                Debug.LogWarning("Render particles: length of positions is different to length of velocities");

            var length = positions.Length;
            if (count != -1) length = count;

            UpdateParticleBuffers(positions, velocities, length);
            ApplyHighlight(highlightGreen, Color.green);
            ApplyHighlight(highlightYellow, Color.yellow);
            DrawParticleBatches(length);
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
                var count = Mathf.Min(batchSize, borderBuffer.matrices.Count - i);
                borderBuffer.matrices.CopyTo(i, matricesBatch, 0, count);

                Graphics.DrawMeshInstanced(
                    borderBuffer.mesh,
                    submeshIndex,
                    mat,
                    matricesBatch,
                    count,
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
        void Awake()
        {
            identityMatrixList.Add(Matrix4x4.identity);
            topMatertial = new(mat) { renderQueue = 4000 };
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

        public void DrawRect(float2 topLeft, float2 bottomRight, float width, Color color)
        {
            var topRight = new float2(bottomRight.x, topLeft.y);
            var bottomLeft  = new float2(topLeft.x,     bottomRight.y);

            DrawLine(topLeft,    topRight,    width, color); // top
            DrawLine(bottomLeft, bottomRight, width, color); // bottom
            DrawLine(topLeft,    bottomLeft,  width, color); // left
            DrawLine(topRight,   bottomRight, width, color); // right
        }
        #endregion

        #region Helpers
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ParallelFor(int count, System.Action<int> body)
        {
            Parallel.ForEach(Partitioner.Create(0, count, parallelForBatchSize), range =>
            {
                for (int i = range.Item1; i < range.Item2; i++)
                    body(i);
            });
        }

        private void UpdateParticleBuffers(float2[] positions, float2[] velocities, int count)
        {
            ParallelFor(count, i =>
            {
                fluidBuffer.matrices[i] = Matrix4x4.Translate(new(positions[i].x, positions[i].y));
                fluidBuffer.colorsBuffer[i] = GetColorVector(velocities[i]);
            });
        }

        private void ApplyHighlight(List<int> highlightIndices, Color color)
        {
            if (highlightIndices == null)
                return;

            for (int i = 0; i < highlightIndices.Count; i++)
                fluidBuffer.colorsBuffer[highlightIndices[i]] = color;
        }

        private void ApplyHighlight(int highlightIndex, Color color)
        {
            if (highlightIndex == -1)
                return;

            fluidBuffer.colorsBuffer[highlightIndex] = color;
        }

        private void DrawParticleBatches(int numParticles)
        {
            for (var i = 0; i < numParticles; i += batchSize)
            {
                var count = Mathf.Min(batchSize, numParticles - i);
                fluidBuffer.colorsBuffer.CopyTo(i, colorsBatch, 0, count);
                fluidBuffer.matrices.CopyTo(i, matricesBatch, 0, count);
                fluidBuffer.mpb.SetVectorArray(colors, colorsBatch);

                Graphics.DrawMeshInstanced(
                    fluidBuffer.mesh,
                    submeshIndex,
                    mat,
                    matricesBatch,
                    count,
                    fluidBuffer.mpb
                );
            }
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Vector4 GetColorVector(Vector2 velocity)
        {
            var color = Mathf.Clamp01((Mathf.Abs(velocity.x) + Mathf.Abs(velocity.y)) / redThreshold);
            return new Vector4(color,
                0,
                1 - color,
                1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Color GetColor(Vector2 velocity)
        {
            var color = Mathf.Clamp01((Mathf.Abs(velocity.x) + Mathf.Abs(velocity.y)) / redThreshold);
            return new Color(color,
                0,
                1 - color);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Vector4 ColorToVector(Color color) => new(color.r, color.g, color.b, color.a);
        # endregion
    }
}