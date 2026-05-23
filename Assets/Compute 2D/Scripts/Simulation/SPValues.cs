using Unity.Mathematics;
using UnityEngine;

public class SPValues
{
    public float2 offset;
    public float length;
    public int columns;
    public int rows;

    public SPValues(float2 bottomLeft, float2 topRight, float length)
    {
        if (length <= 0)
            Debug.LogError($"SPValues: length must be > 0, got {length}");

        this.length = length;
        offset = bottomLeft;
        var width = topRight.x - bottomLeft.x;
        var height = topRight.y - bottomLeft.y;

        if (width <= 0 || height <= 0)
            Debug.LogWarning($"SPValues: grid dimensions are non-positive (width={width}, height={height}), likely caused by a degenerate bounding box");

        columns = (int)(width / length);
        rows = (int)(height / length);

        if (width % length != 0) columns++;
        if (height % length != 0) rows++;

        if (columns == 0 || rows == 0)
            Debug.LogWarning($"SPValues: grid has zero cells (columns={columns}, rows={rows}), neighbour queries will return nothing");
    }

    public void Draw(Color color)
    {
        for (var i = 0; i <= columns; i++)
        {
            Vector3 start = new(offset.x + (length * i), -offset.y, 0);
            Vector3 end = new(offset.x + (length * i), offset.y, 0);
            Debug.DrawLine(start, end, color);
        }

        for (var i = 0; i <= rows; i++)
        {
            Vector3 start = new(-offset.x, offset.y + (length * i), 0);
            Vector3 end = new(offset.x, offset.y + (length * i), 0);
            Debug.DrawLine(start, end, color);
        }
    }
}