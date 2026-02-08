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

        public SpatialPartitioning(Vector2 bottomLeft, Vector2 topRight, float length)
        {
            this.length = length;
            offset = bottomLeft;
            var width = topRight.x - bottomLeft.x;
            var height =  topRight.y - bottomLeft.y; 
            columns = (int)(width / length);
            rows = (int)(height / length);

            if (width % length != 0) columns++;
            if (height % length != 0) rows++;

            grid = new List<int>[columns * rows];

            for (var i = 0; i < grid.Length; i++)
                grid[i] = new List<int>();
        }

        public void Init(Vector2[] positions)
        {
            foreach (var list in grid)
                list.Clear();

            for (var i = 0; i < positions.Length; i++)
                grid[GetGridIndex(positions[i])].Add(i);
        }

        public List<int> GetNeighbours(Vector2 position)
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

        private int GetGridIndex(Vector2 pos)
        {
            var scaled = (pos - offset) / length;
            var (gridX, gridY) = ((int)Math.Clamp(scaled.x, 0, columns - 1), 
                                        (int)Math.Clamp(scaled.y, 0, rows - 1));
            return gridX + gridY * columns;
        }
    }
}