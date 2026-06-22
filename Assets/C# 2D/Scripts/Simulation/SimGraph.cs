using System.Collections.Generic;
using UnityEngine;

public class SimGraph : MonoBehaviour
{
    const int MaxSeries = 3;

    [Header("Graph Series")]
    public Color[] seriesColors = { Color.white, Color.cyan, Color.yellow };

    [Header("X Axis")]
    public bool useMilliseconds = true;
    [Tooltip("Units between each tick label (ms or s depending on toggle)")]
    public float xScale = 100f;
    [Tooltip("World units per tick")]
    public float xZoom = 0.005f;
    public int xTickCount = 5;

    [Header("Y Axis")]
    [Tooltip("World units per value unit")]
    public float yScale = 0.01f;
    [Tooltip("Multiplier for y display size")]
    public float yZoom = 1f;
    public int yTickCount = 5;
    public float yMin = 0f;
    public float yMax = 100f;

    float YS => yScale * yZoom;

    [Header("Position")]
    public Vector2 graphOrigin = Vector2.zero;

    [Header("Axes Appearance")]
    public float axisWidth = 0.04f;
    public float labelOffset = 0.35f;

    [Header("Label Appearance")]
    public int labelFontSize = 24;
    public float labelCharacterSize = 0.1f;

    LineRenderer[] seriesLRs;
    List<Vector3>[] seriesPoints;
    float[] seriesTimes;

    List<TextMesh> xLabels = new List<TextMesh>();
    List<TextMesh> yLabels = new List<TextMesh>();

    void Start()
    {
        int count = Mathf.Clamp(seriesColors.Length, 1, MaxSeries);
        seriesLRs = new LineRenderer[count];
        seriesPoints = new List<Vector3>[count];
        seriesTimes = new float[count];

        for (int i = 0; i < count; i++)
        {
            seriesLRs[i] = NewLineRenderer($"Series {i}", seriesColors[i], axisWidth * 0.5f);
            seriesPoints[i] = new List<Vector3>();
        }

        DrawAxes();
        SpawnAxisLabels();
    }

    public void AddPoint(float value, float deltaTime, int series = 0)
    {
        if (seriesLRs == null || series < 0 || series >= seriesLRs.Length) return;

        seriesTimes[series] += deltaTime;

        float xPos = graphOrigin.x + seriesTimes[series] * 1000f / xScale * xZoom;
        float yPos = graphOrigin.y + (value - yMin) * YS;
        seriesPoints[series].Add(new Vector3(xPos, yPos, 0f));
        seriesLRs[series].positionCount = seriesPoints[series].Count;
        seriesLRs[series].SetPositions(seriesPoints[series].ToArray());
    }

    public float GetCurrentTime(int series = 0)
    {
        if (series < 0 || series >= seriesTimes.Length) return 0f;
        return seriesTimes[series];
    }

    public void Reset()
    {
        for (int i = 0; i < seriesLRs.Length; i++)
        {
            seriesPoints[i].Clear();
            seriesLRs[i].positionCount = 0;
            seriesTimes[i] = 0f;
        }
    }

    // ── Axes ──────────────────────────────────────────────────────

    void DrawAxes()
    {
        float xLen = xTickCount * xZoom;
        float yLen = (yMax - yMin) * YS;

        Vector3 origin = new(graphOrigin.x, graphOrigin.y, 0f);

        var xAxis = NewLineRenderer("X Axis", Color.white, axisWidth);
        xAxis.positionCount = 2;
        xAxis.SetPositions(new Vector3[]
        {
            origin,
            origin + new Vector3(xLen, 0f, 0f)
        });

        var yAxis = NewLineRenderer("Y Axis", Color.white, axisWidth);
        yAxis.positionCount = 2;
        yAxis.SetPositions(new Vector3[]
        {
            origin,
            origin + new Vector3(0f, yLen, 0f)
        });
    }

    // ── Labels ────────────────────────────────────────────────────

    void SpawnAxisLabels()
    {
        float yLen = (yMax - yMin) * YS;

        Vector3 base2d = new(graphOrigin.x, graphOrigin.y, 0f);

        for (int i = 0; i <= yTickCount; i++)
        {
            float t = (float)i / yTickCount;
            float value = Mathf.Lerp(yMin, yMax, t);
            var label = SpawnLabel($"{value:F1}",
                base2d + new Vector3(-labelOffset, t * yLen, 0f));
            yLabels.Add(label);
        }

        for (int i = 0; i <= xTickCount; i++)
        {
            float tickValue = i * xScale;
            string tickText = useMilliseconds ? $"{tickValue:F0}ms" : $"{tickValue / 1000f:F2}s";
            float xPos = i * xZoom;
            var label = SpawnLabel(tickText,
                base2d + new Vector3(xPos, -labelOffset, 0f));
            xLabels.Add(label);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────

    TextMesh SpawnLabel(string text, Vector3 worldPos)
    {
        var go = new GameObject("Label");
        go.transform.SetParent(transform);
        go.transform.position = worldPos;

        var tm = go.AddComponent<TextMesh>();
        tm.text = text;
        tm.fontSize = labelFontSize;
        tm.characterSize = labelCharacterSize;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.color = Color.white;

        return tm;
    }

    LineRenderer NewLineRenderer(string name, Color color, float width)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform);
        var newLr = go.AddComponent<LineRenderer>();
        SetupLineRenderer(newLr, color);
        newLr.startWidth = newLr.endWidth = width;
        newLr.useWorldSpace = true;
        return newLr;
    }

    void SetupLineRenderer(LineRenderer target, Color color)
    {
        // Unlit/Color works in all render pipelines without the purple fallback
        target.material = new Material(Shader.Find("Unlit/Color")) { color = color };
    }
}
