using System;
using System.Collections.Generic;
using UnityEngine;

namespace SimulationLogic
{
    public class SpatialPartitioning
    {
        public Vector2 offset;
        public float length;
        public int columns;
        public int rows;
        public List<int>[] grid;

        private (int, int)[] neighbours = {
        (-1, -1),
        (-1, 0),
        (-1, 1),
        (0, -1),
        (0, 0),
        (0, 1),
        (1, -1),
        (1, 0),
        (1, 1)};

        public SpatialPartitioning(Vector2 topLeft, Vector2 bottomRight, float length)
        {
            this.length = length;
            offset = topLeft;
            float width = -topLeft.x + bottomRight.x;
            float height = -topLeft.y + bottomRight.y; 
            columns = (int)(width / length);
            rows = (int)(height / length);

            if (width % length != 0)
                columns++;

            if(height % length != 0)
                rows++;

            grid = new List<int>[columns * rows];

            for (int i = 0; i < grid.Length; i++)
                grid[i] = new List<int>();
        }

        public void Init(Vector2[] positions)
        {
            foreach (List<int> list in grid)
                list.Clear();

            for (int i = 0; i < positions.Length; i++)
            {
                int index = GetGridIndex(positions[i]);

                if (index >= 0 && index < grid.Length)
                    grid[index].Add(i);

                else
                    Debug.LogError($"SP: Index is out of range. Index: {index}. Grid length: {grid.Length}");
            }
        }

        public List<int> GetNeighbours(Vector2 position)
        {
            List<int> result = new();
            Vector2 scaled = (position - offset) / length;
            var (gridX, gridY) = ((int)scaled.x, (int)scaled.y);

            foreach (var (offsetX, offsetY) in neighbours)
            {
                int nX = gridX + offsetX;
                int nY = gridY + offsetY;

                if (nX >= 0 && nX < columns && nY >= 0 && nY < rows)
                {
                    int index = nX + nY * columns;
                    result.AddRange(grid[index]);
                }
            }

            return result;
        }

        private int GetGridIndex(Vector2 pos)
        {
            Vector2 scaled = (pos - offset) / length;
            var (gridX, gridY) = ((int)Math.Clamp(scaled.x, 0, columns - 1), (int)Math.Clamp(scaled.y, 0, rows - 1));
            return gridX + gridY * columns;
        }
    }
}