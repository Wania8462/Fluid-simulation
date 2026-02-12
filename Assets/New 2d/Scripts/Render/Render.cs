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
            
            mpb.SetVectorArray(colors, colorsBuffer);

            for (var i = 0; i < matrices.Count; i += batchSize)
            {
                Graphics.DrawMeshInstanced(
                    mesh,
                    0,
                    mat,
                    matrices.GetRange(i, Mathf.Min(batchSize, matrices.Count - i)),
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
        
        // May be optimized with computing into a variable
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Vector4 GetColorVector(Vector2 velocity)
        {
            return new Vector4(Mathf.Clamp01((Mathf.Abs(velocity.x) + Mathf.Abs(velocity.y)) / 40f), 
                0, 
                1 - Mathf.Clamp01((Mathf.Abs(velocity.x) + Mathf.Abs(velocity.y)) / 40f),
                1);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Color GetColor(Vector2 velocity)
        {
            return new Color(Mathf.Clamp01((Mathf.Abs(velocity.x) + Mathf.Abs(velocity.y)) / 40f), 
                0, 
                1 - Mathf.Clamp01((Mathf.Abs(velocity.x) + Mathf.Abs(velocity.y)) / 40f));
        }
    }
}