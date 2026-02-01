using System.Collections.Generic;
using UnityEngine;

public class SpatialPartitioning
{
    public Vector2 offset;
    public float length;
    public int columns;
    public int rows;
    private List<int>[] grid;

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
        columns = (int)((-topLeft.x + bottomRight.x) / length) + 1;
        rows = (int)((-topLeft.y + bottomRight.y) / length) + 1;
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
            grid[index].Add(i);
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
        var (gridX, gridY) = ((int)scaled.x, (int)scaled.y);
        return gridX + gridY * columns;
    }
}