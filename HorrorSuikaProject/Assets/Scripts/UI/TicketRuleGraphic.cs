using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Draws a dashed rule: a horizontal stroke, a vertical stroke, or a rounded frame.
/// A gap of zero draws a solid stroke.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class TicketRuleGraphic : MaskableGraphic
{
    public enum RuleKind
    {
        Horizontal,
        Vertical,
        Frame
    }

    [SerializeField] private RuleKind kind = RuleKind.Horizontal;
    [SerializeField] private float thickness = 2.5f;
    [SerializeField] private float dashLength = 12f;
    [SerializeField] private float gapLength = 7f;
    [SerializeField] private float cornerRadius = 18f;
    [SerializeField] private float inset;

    /// <summary>Which stroke this graphic draws.</summary>
    public RuleKind Kind
    {
        get => kind;
        set
        {
            kind = value;
            SetVerticesDirty();
        }
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        raycastTarget = false;
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        thickness = Mathf.Max(0.5f, thickness);
        dashLength = Mathf.Max(1f, dashLength);
        gapLength = Mathf.Max(0f, gapLength);
        cornerRadius = Mathf.Max(0f, cornerRadius);
        inset = Mathf.Max(0f, inset);
    }
#endif

    protected override void OnPopulateMesh(VertexHelper vertexHelper)
    {
        vertexHelper.Clear();
        Rect rect = GetPixelAdjustedRect();
        rect.xMin += inset;
        rect.xMax -= inset;
        rect.yMin += inset;
        rect.yMax -= inset;
        if (rect.width <= 1f || rect.height <= 1f)
        {
            return;
        }

        if (kind == RuleKind.Horizontal)
        {
            DrawStraight(vertexHelper, new Vector2(rect.xMin, rect.center.y), new Vector2(rect.xMax, rect.center.y));
            return;
        }

        if (kind == RuleKind.Vertical)
        {
            DrawStraight(vertexHelper, new Vector2(rect.center.x, rect.yMin), new Vector2(rect.center.x, rect.yMax));
            return;
        }

        DrawFrame(vertexHelper, rect);
    }

    private void DrawFrame(VertexHelper vertexHelper, Rect rect)
    {
        float radius = Mathf.Min(cornerRadius, Mathf.Min(rect.width, rect.height) * 0.5f);
        var points = new List<Vector2>(48);
        if (radius <= 0.5f)
        {
            points.Add(new Vector2(rect.xMin, rect.yMax));
            points.Add(new Vector2(rect.xMax, rect.yMax));
            points.Add(new Vector2(rect.xMax, rect.yMin));
            points.Add(new Vector2(rect.xMin, rect.yMin));
            points.Add(new Vector2(rect.xMin, rect.yMax));
        }
        else
        {
            AddCorner(points, new Vector2(rect.xMax - radius, rect.yMax - radius), radius, 90f, 0f);
            AddCorner(points, new Vector2(rect.xMax - radius, rect.yMin + radius), radius, 0f, -90f);
            AddCorner(points, new Vector2(rect.xMin + radius, rect.yMin + radius), radius, -90f, -180f);
            AddCorner(points, new Vector2(rect.xMin + radius, rect.yMax - radius), radius, 180f, 90f);
            points.Add(points[0]);
        }

        DashAlong(vertexHelper, points);
    }

    private static void AddCorner(List<Vector2> points, Vector2 center, float radius, float startDegrees, float endDegrees)
    {
        const int Steps = 5;
        for (int i = 0; i <= Steps; i++)
        {
            float degrees = Mathf.Lerp(startDegrees, endDegrees, i / (float)Steps);
            float radians = degrees * Mathf.Deg2Rad;
            points.Add(center + new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * radius);
        }
    }

    private void DashAlong(VertexHelper vertexHelper, List<Vector2> points)
    {
        float remaining = dashLength;
        bool drawing = true;
        for (int i = 0; i < points.Count - 1; i++)
        {
            Vector2 from = points[i];
            Vector2 to = points[i + 1];
            float length = Vector2.Distance(from, to);
            if (length <= 0.01f)
            {
                continue;
            }

            Vector2 direction = (to - from) / length;
            float walked = 0f;
            while (walked < length - 0.01f)
            {
                float span = Mathf.Min(remaining, length - walked);
                Vector2 start = from + direction * walked;
                Vector2 end = start + direction * span;
                if (drawing)
                {
                    AddSegment(vertexHelper, start, end);
                }

                walked += span;
                remaining -= span;
                if (remaining <= 0.01f)
                {
                    drawing = !drawing;
                    remaining = drawing ? dashLength : Mathf.Max(gapLength, 0.01f);
                    if (!drawing && gapLength <= 0.01f)
                    {
                        drawing = true;
                        remaining = dashLength;
                    }
                }
            }
        }
    }

    private void DrawStraight(VertexHelper vertexHelper, Vector2 start, Vector2 end)
    {
        var points = new List<Vector2>(2) { start, end };
        DashAlong(vertexHelper, points);
    }

    private void AddSegment(VertexHelper vertexHelper, Vector2 start, Vector2 end)
    {
        Vector2 direction = end - start;
        float length = direction.magnitude;
        if (length < 0.01f)
        {
            return;
        }

        direction /= length;
        Vector2 normal = new Vector2(-direction.y, direction.x) * (thickness * 0.5f);
        int index = vertexHelper.currentVertCount;
        UIVertex vertex = UIVertex.simpleVert;
        vertex.color = color;
        vertex.position = start - normal;
        vertexHelper.AddVert(vertex);
        vertex.position = start + normal;
        vertexHelper.AddVert(vertex);
        vertex.position = end + normal;
        vertexHelper.AddVert(vertex);
        vertex.position = end - normal;
        vertexHelper.AddVert(vertex);
        vertexHelper.AddTriangle(index, index + 1, index + 2);
        vertexHelper.AddTriangle(index, index + 2, index + 3);
    }
}
