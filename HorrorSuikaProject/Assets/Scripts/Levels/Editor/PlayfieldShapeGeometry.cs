using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Editor-only geometry maths shared by the shape baker and the scene-view authoring tool:
/// outward offsetting, self-intersection detection, and watertight solid construction.
/// </summary>
public static class PlayfieldShapeGeometry
{
    private const float MinimumMiterDot = 0.2f;
    private const float IntersectionEpsilon = 1e-6f;

    /// <summary>Outward normal of a segment whose interior lies to the left of the travel direction.</summary>
    public static Vector2 SegmentOutwardNormal(Vector2 from, Vector2 to)
    {
        Vector2 tangent = to - from;
        if (tangent.sqrMagnitude <= IntersectionEpsilon)
        {
            return Vector2.zero;
        }

        tangent.Normalize();
        return new Vector2(tangent.y, -tangent.x);
    }

    /// <summary>
    /// Offsets an interior polyline outward by the wall thickness, mitring at corners so the
    /// resulting ring keeps a constant wall width. Zero-length segments are ignored.
    /// </summary>
    public static Vector2[] BuildOuterOutline(IReadOnlyList<Vector2> inner, float thickness)
    {
        int count = inner.Count;
        Vector2[] outer = new Vector2[count];
        Vector2[] segmentNormals = new Vector2[Mathf.Max(0, count - 1)];

        for (int i = 0; i < count - 1; i++)
        {
            segmentNormals[i] = SegmentOutwardNormal(inner[i], inner[i + 1]);
        }

        for (int i = 0; i < count; i++)
        {
            Vector2 previous = i > 0 ? segmentNormals[i - 1] : Vector2.zero;
            Vector2 next = i < count - 1 ? segmentNormals[i] : Vector2.zero;
            Vector2 blended = previous + next;

            if (blended.sqrMagnitude <= IntersectionEpsilon)
            {
                blended = next.sqrMagnitude > IntersectionEpsilon ? next : previous;
            }

            if (blended.sqrMagnitude <= IntersectionEpsilon)
            {
                outer[i] = inner[i];
                continue;
            }

            blended.Normalize();
            Vector2 reference = next.sqrMagnitude > IntersectionEpsilon ? next : previous;
            float miterScale = 1f / Mathf.Max(MinimumMiterDot, Vector2.Dot(blended, reference));
            outer[i] = inner[i] + (blended * thickness * miterScale);
        }

        return outer;
    }

    /// <summary>
    /// True when any two non-adjacent segments of the polyline cross. On the outer polyline this
    /// means the wall thickness exceeded a concave corner's radius of curvature.
    /// </summary>
    public static bool HasSelfIntersection(IReadOnlyList<Vector2> polyline)
    {
        int segmentCount = polyline.Count - 1;
        for (int i = 0; i < segmentCount; i++)
        {
            for (int j = i + 2; j < segmentCount; j++)
            {
                if (SegmentsIntersect(polyline[i], polyline[i + 1], polyline[j], polyline[j + 1]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool SegmentsIntersect(Vector2 a1, Vector2 a2, Vector2 b1, Vector2 b2)
    {
        Vector2 r = a2 - a1;
        Vector2 s = b2 - b1;
        float denominator = (r.x * s.y) - (r.y * s.x);
        if (Mathf.Abs(denominator) <= IntersectionEpsilon)
        {
            return false;
        }

        Vector2 delta = b1 - a1;
        float t = ((delta.x * s.y) - (delta.y * s.x)) / denominator;
        float u = ((delta.x * r.y) - (delta.y * r.x)) / denominator;
        return t > IntersectionEpsilon && t < 1f - IntersectionEpsilon
            && u > IntersectionEpsilon && u < 1f - IntersectionEpsilon;
    }

    /// <summary>Vertex count of the solid that would be built from an outline of the given length.</summary>
    public static int CountVertices(int outlinePointCount)
    {
        return Mathf.Max(0, outlinePointCount) * 4;
    }

    /// <summary>Triangle count of the solid that would be built from an outline of the given length.</summary>
    public static int CountTriangles(int outlinePointCount)
    {
        int segments = Mathf.Max(0, outlinePointCount - 1);
        return (segments * 8) + 4;
    }

    /// <summary>
    /// Builds a closed, watertight solid from the interior and outer polylines: interior surface,
    /// outer surface, both Z caps, and both rim end caps, all consistently wound with interior
    /// normals facing the play area.
    /// </summary>
    public static void BuildSolid(
        IReadOnlyList<Vector2> inner,
        IReadOnlyList<Vector2> outer,
        float extrudeDepth,
        out Vector3[] vertices,
        out int[] triangles)
    {
        int count = inner.Count;
        float half = extrudeDepth * 0.5f;

        vertices = new Vector3[count * 4];
        for (int i = 0; i < count; i++)
        {
            vertices[InnerFront(i)] = new Vector3(inner[i].x, inner[i].y, -half);
            vertices[InnerBack(i)] = new Vector3(inner[i].x, inner[i].y, half);
            vertices[OuterFront(i, count)] = new Vector3(outer[i].x, outer[i].y, -half);
            vertices[OuterBack(i, count)] = new Vector3(outer[i].x, outer[i].y, half);
        }

        List<int> indices = new List<int>(CountTriangles(count) * 3);

        for (int i = 0; i < count - 1; i++)
        {
            int ifA = InnerFront(i);
            int ibA = InnerBack(i);
            int ifB = InnerFront(i + 1);
            int ibB = InnerBack(i + 1);
            int ofA = OuterFront(i, count);
            int obA = OuterBack(i, count);
            int ofB = OuterFront(i + 1, count);
            int obB = OuterBack(i + 1, count);

            // Interior surface, normals facing the play area.
            AddTriangle(indices, ifA, ibA, ifB);
            AddTriangle(indices, ifB, ibA, ibB);

            // Outer surface, normals facing away from the play area.
            AddTriangle(indices, ofA, ofB, obA);
            AddTriangle(indices, ofB, obB, obA);

            // Front cap at -Z.
            AddTriangle(indices, ifA, ifB, ofA);
            AddTriangle(indices, ifB, ofB, ofA);

            // Back cap at +Z.
            AddTriangle(indices, ibA, obA, ibB);
            AddTriangle(indices, ibB, obA, obB);
        }

        // Rim end caps close the ring at both open ends of the polyline.
        AddTriangle(indices, InnerFront(0), OuterFront(0, count), InnerBack(0));
        AddTriangle(indices, OuterFront(0, count), OuterBack(0, count), InnerBack(0));

        int last = count - 1;
        AddTriangle(indices, InnerFront(last), InnerBack(last), OuterFront(last, count));
        AddTriangle(indices, OuterFront(last, count), InnerBack(last), OuterBack(last, count));

        triangles = indices.ToArray();
    }

    private static void AddTriangle(List<int> indices, int a, int b, int c)
    {
        indices.Add(a);
        indices.Add(b);
        indices.Add(c);
    }

    private static int InnerFront(int index) => index * 2;

    private static int InnerBack(int index) => (index * 2) + 1;

    private static int OuterFront(int index, int count) => (count * 2) + (index * 2);

    private static int OuterBack(int index, int count) => (count * 2) + (index * 2) + 1;
}
