using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace SimulationLogic
{
    public class SpatialPartitioning
    {
        public float2 offset;
        public float length;
        public int columns;
        public int rows;
        public List<int>[] grid;

        private readonly (int, int)[] neighbours = {
        (-1, -1),
        (-1, 0),
        (-1, 1),
        (0, -1),
        (0, 0),
        (0, 1),
        (1, -1),
        (1, 0),
        (1, 1)};

        public SpatialPartitioning(float2 bottomLeft, float2 topRight, float length)
        {
            if (length <= 0)
                Debug.LogError($"SpatialPartitioning: length must be > 0, got {length}");

            this.length = length;
            offset = bottomLeft;
            var width = topRight.x - bottomLeft.x;
            var height = topRight.y - bottomLeft.y;

            if (width <= 0 || height <= 0)
                Debug.LogWarning($"SpatialPartitioning: grid dimensions are non-positive (width={width}, height={height}), likely caused by a degenerate bounding box");

            columns = (int)(width / length);
            rows = (int)(height / length);

            if (width % length != 0) columns++;
            if (height % length != 0) rows++;

            if (columns == 0 || rows == 0)
                Debug.LogWarning($"SpatialPartitioning: grid has zero cells (columns={columns}, rows={rows}), neighbour queries will return nothing");

            grid = new List<int>[columns * rows];

            for (var i = 0; i < grid.Length; i++)
                grid[i] = new List<int>();
        }

        public void Init(ReadOnlySpan<Particle> particles)
        {
            foreach (var list in grid)
                list.Clear();

            foreach(var particle in particles)
                grid[GetGridIndex(particle.position)].Add(particle.ID);
        }

        public void Init(ReadOnlySpan<BorderParticle> particles)
        {
            foreach (var list in grid)
                list.Clear();

            for (int i = 0; i < particles.Length; i++)
                grid[GetGridIndex(particles[i].position)].Add(i);
        }

        public List<int> GetNeighbours(float2 position)
        {
            List<int> result = new();
            var scaled = (position - offset) / length;
            var (gridX, gridY) = ((int)scaled.x, (int)scaled.y);

            foreach (var (offsetX, offsetY) in neighbours)
            {
                var nX = gridX + offsetX;
                var nY = gridY + offsetY;
                if (nX < 0 || nX >= columns || nY < 0 || nY >= rows) continue;

                var index = nX + nY * columns;
                result.AddRange(grid[index]);
            }

            return result;
        }

        public void GetNeighbours(float2 position, List<int> list)
        {
            list.Clear();
            var scaled = (position - offset) / length;
            var (gridX, gridY) = ((int)scaled.x, (int)scaled.y);

            foreach (var (offsetX, offsetY) in neighbours)
            {
                var nX = gridX + offsetX;
                var nY = gridY + offsetY;
                if (nX < 0 || nX >= columns || nY < 0 || nY >= rows) continue;

                var index = nX + nY * columns;
                list.AddRange(grid[index]);
            }
        }

        // Returns 75 neighbours on average
        public void GetNeighbours(float2 position, RefList<int> list)
        {
            list.Clear();
            var scaled = (position - offset) / length;
            var (gridX, gridY) = ((int)scaled.x, (int)scaled.y);

            foreach (var (offsetX, offsetY) in neighbours)
            {
                var nX = gridX + offsetX;
                var nY = gridY + offsetY;
                if (nX < 0 || nX >= columns || nY < 0 || nY >= rows) continue;

                var index = nX + nY * columns;
                list.AddRange(grid[index]);
            }
        }

#if UNITY_EDITOR
        public float2[] GetNeighboursDimentions(float2 position)
        {
            var result = new float2[4];
            var scaled = (position - offset) / length;
            var (gridX, gridY) = ((int)scaled.x, (int)scaled.y);

            result[0] = new float2(offset.x + (gridX - 1) * length, offset.y + (gridY - 1) * length);
            result[1] = new float2(offset.x + (gridX + 2) * length, offset.y + (gridY - 1) * length);
            result[2] = new float2(offset.x + (gridX - 1) * length, offset.y + (gridY + 2) * length);
            result[3] = new float2(offset.x + (gridX + 2) * length, offset.y + (gridY + 2) * length);

            return result;
        }
#endif

        private int GetGridIndex(float2 pos)
        {
            var scaled = (pos - offset) / length;
            var (gridX, gridY) = ((int)Math.Clamp(scaled.x, 0, columns - 1),
                                        (int)Math.Clamp(scaled.y, 0, rows - 1));
            return gridX + gridY * columns;
        }
    }
}