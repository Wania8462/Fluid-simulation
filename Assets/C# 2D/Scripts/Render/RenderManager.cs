using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Rendering
{
    public enum RenderType
    {
        Particles,
        MarchingSquares,
        DensityMap
    }

    // Basically works as an interface rn
    // todo: add safety checks
    public class RenderManager : MonoBehaviour
    {
        public RenderType renderType;

        [Header("References")]
        [SerializeField] private RenderParticles renderParticles;
        [SerializeField] private RenderMarchingSquares renderMarchingSquares;
        [SerializeField] private RenderDensityMap renderDensityMap;

        private List<float2> persistantParticles = new();

        private void Update()
        {
            for (int i = 0; i < persistantParticles.Count; i++)
                renderParticles.DrawCustomParticle(persistantParticles[i]);
        }

        public (int edges, int cells) InitAll(float2 boundSize, int maxNbParticles, int nbBoundaryParticles = 0)
        {
            InitParticles(maxNbParticles);

            (int edges, int cells) ret = new();
            ret.edges = InitMarchingSqaures(boundSize);
            ret.cells = InitDensityMap(boundSize);

            if (nbBoundaryParticles > 0)
                InitBoundaryParticles(nbBoundaryParticles);

            return ret;
        }

        #region Particles
        /// <summary>
        /// Calling this more than once will append particles rather than reinitializing.
        /// Delete particles first if that is not intended.
        /// </summary>
        public void InitParticles(int maxNbParticles)
        {
            renderParticles.DeleteAllTypesOfParticles();
            renderParticles.InitParticles(maxNbParticles);
        }

        /// <summary>
        /// Calling this more than once will append particles rather than reinitializing.
        /// Delete particles first if that is not intended.
        /// </summary>
        public void InitCustomParticle(float2 position, float radius, Color color)
        {
            renderParticles.DeleteCustomParticles();
            renderParticles.InitCustomParticle(position, radius, color);
        }

        /// <summary>
        /// Calling this more than once will append particles rather than reinitializing.
        /// Delete particles first if that is not intended.
        /// </summary>
        public void InitBoundaryParticles(int nbParticles)
        {
            renderParticles.DeleteBoundaryParticles();
            renderParticles.InitBoundaryParticles(nbParticles);
        }

        public void DrawParticles(float2[] positions, float2[] velocities, int count = -1, List<int> highlightGreen = null, List<int> highlightYellow = null)
        {
            if (positions.Length != velocities.Length)
                Debug.LogWarning("RenderParticles: length of positions is different to length of velocities");

            renderParticles.DrawParticles(positions, velocities, count, highlightGreen, highlightYellow);
        }

        public void DrawParticles(float2[] positions, float2[] velocities, int count = -1, List<int> highlightGreen = null, int highlightYellow = -1)
        {
            if (positions.Length != velocities.Length)
                Debug.LogWarning("RenderParticles: length of positions is different to length of velocities");

            renderParticles.DrawParticles(positions, velocities, count, highlightGreen, highlightYellow);
        }

        public void DrawParticles(float2[] positions, float2[] velocities, int count = -1, int highlightGreen = -1, List<int> highlightYellow = null)
        {
            if (positions.Length != velocities.Length)
                Debug.LogWarning("RenderParticles: length of positions is different to length of velocities");

            renderParticles.DrawParticles(positions, velocities, count, highlightGreen, highlightYellow);
        }

        public void DrawParticles(float2[] positions, float2[] velocities, int count = -1, int highlightGreen = -1, int highlightYellow = -1)
        {
            if (positions.Length != velocities.Length)
                UnityEngine.Debug.LogWarning("RenderParticles: length of positions is different to length of velocities");

            renderParticles.DrawParticles(positions, velocities, count, highlightGreen, highlightYellow);
        }

        public void DrawCustomParticle(float2 position, int index = 0)
        {
            int numOfCustomParticles = renderParticles.customBuffer.matrices.Count;
            if (numOfCustomParticles < index || numOfCustomParticles == 0)
            {
                Debug.LogError("RenderParticles: custom particle is outside of the range");
                return;
            }

            renderParticles.DrawCustomParticle(position, index);
        }

        public void DrawAllCustomParticles(float2[] positions)
        {
            if (renderParticles.customBuffer.matrices.Count == 0)
                Debug.LogWarning("RenderParticles: there are no custom particles to draw");

            renderParticles.DrawAllCustomParticles(positions);
        }

        public void DrawBoundaryParticles(float2[] positions)
        {
            renderParticles.DrawBoundaryParticles(positions);
        }

        public void DrawBoundaryParticles()
        {
            renderParticles.DrawBoundaryParticles();
        }
        
        public void CreatePersistantStaticParticle(float2 position, float radius, Color color)
        {
            renderParticles.InitCustomParticle(position, radius, color);
            persistantParticles.Add(position);
        }
        #endregion

        #region MarchingSquares
        public int InitMarchingSqaures(float2 boundSize)
        {
            renderMarchingSquares.Init(boundSize);
            return renderMarchingSquares.edges.Length;
        }

        public void DrawMarchingSquares(float[] densities)
        {
            if (densities.Length != renderMarchingSquares.edges.Length)
            {
                Debug.LogError($"RenderMarchingSquares: Length of densities != length of edges. Densities: {densities.Length}, edges: {renderMarchingSquares.edges.Length}");
                return;
            }

            renderMarchingSquares.DrawLerp(densities);
        }

        public void DrawMarchingSquaresMidpoint(float[] densities)
        {
            if (densities.Length != renderMarchingSquares.edges.Length)
            {
                Debug.LogError($"RenderMarchingSquares: Length of densities != length of edges. Densities: {densities.Length}, edges: {renderMarchingSquares.edges.Length}");
                return;
            }

            renderMarchingSquares.DrawMidpoints(densities);
        }

        public Vector3[] GetSquaresEdges()
        {
            if (renderMarchingSquares.edges.Length == 0)
                Debug.LogWarning("RenderMarchingSquares: marching squares haven't been initialised");

            return renderMarchingSquares.edges;
        }
        #endregion

        #region DensityMap
        public int InitDensityMap(float2 boundSize)
        {
            renderDensityMap.Init(boundSize);
            return renderDensityMap.cells.Length;
        }

        public void DrawDensityMap(float[] densities)
        {
            if (densities.Length != renderDensityMap.cells.Length)
                Debug.LogError($"RenderDensityMap: Length of densities != length of cells. Densities: {densities.Length}, centres: {renderDensityMap.cells.Length}");

            renderDensityMap.Draw(densities);
        }

        public float2[] GetCells()
        {
            if (renderMarchingSquares.edges.Length == 0)
                Debug.LogWarning("RenderDensityMap: density cells haven't been initialised");

            return renderDensityMap.cells;
        }
        #endregion

        public void InitBody(float2 position, float radius, Color color)
        {
            renderParticles.InitCustomParticle(position, radius, color);
        }

        #region Debug
        public void DrawLine(float2 start, float2 end, float width, Color color)
        {
            if (start.x == float.NaN || start.y == float.NaN)
            {
                Debug.LogError("RenderDebug: start position of the line is NaN");
                return;
            }

            if (end.x == float.NaN || end.y == float.NaN)
            {
                Debug.LogError("RenderDebug: end position of the line is NaN");
                return;
            }

            renderParticles.DrawLine(start, end, width, color);
        }

        public void DrawLines(float2 start, float2[] ends, float width, Color color)
        {
            if (start.x == float.NaN || start.y == float.NaN)
            {
                Debug.LogError("RenderDebug: start position of the line is NaN");
                return;
            }

            renderParticles.DrawLines(start, ends, width, color);
        }

        public void DrawRect(float2 topLeft, float2 bottomRight, float width, Color color)
        {
            if (topLeft.x == float.NaN || topLeft.y == float.NaN)
            {
                Debug.LogError("RenderDebug: topLeft position of the line is NaN");
                return;
            }

            if (bottomRight.x == float.NaN || bottomRight.y == float.NaN)
            {
                Debug.LogError("RenderDebug: bottomRight position of the line is NaN");
                return;
            }
        }
        #endregion
    }
}