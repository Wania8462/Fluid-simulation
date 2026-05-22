using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public enum Colors
{
    Default,
    HighlightMain,
    HighlightSub
}

public class RenderDebug
{
    private Mesh defaultMesh;
    private Material mat;

    private List<Matrix4x4> matrices;
    private MaterialPropertyBlock mpb;

    private readonly int colorProp = Shader.PropertyToID("_Color");
    private const float particleRadius = 0.5f;

    public RenderDebug(Material material)
    {
        defaultMesh = MeshGenerator.Circle(particleRadius, 5);
        mat = material;
        matrices = new() { new() };
        mpb = new();
    }

    public void DrawPartricle(float2 position, Colors color = Colors.Default, float radius = particleRadius)
    {
        Mesh currMesh = defaultMesh;
        if (radius != particleRadius)
            currMesh = MeshGenerator.Circle(particleRadius, 5);

        matrices[0].SetRow(0, new Vector4(position.x, position.y));
        mpb.SetColor(colorProp, GetColor(color));
        DrawMesh(currMesh, matrices[0]);
    }

    private void DrawMesh(Mesh mesh, Matrix4x4 matrix) => Graphics.DrawMesh(mesh, matrix, mat, layer: 10);

    private Color GetColor(Colors color)
    {
        if (color == Colors.Default)
            return Color.blue;

        if (color == Colors.HighlightMain)
            return Color.yellow;

        return Color.green;
    }
}