using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Authored curve describing one container shape. Editor-time input to the shape baker;
/// never read at runtime, where <see cref="PlayfieldShape"/> carries the baked result instead.
/// </summary>
[CreateAssetMenu(menuName = "Merge Drop/Playfield Shape", fileName = "Shape_00")]
public class PlayfieldShapeDefinition : ScriptableObject
{
    /// <summary>Fewest control points that can describe a container.</summary>
    public const int MinimumControlPointCount = 3;

    private const int MinSubdivisions = 0;
    private const int MaxSubdivisions = 32;
    private const float MinSegmentLength = 0.02f;
    private const float MinWallThickness = 0.02f;
    private const float MinExtrudeDepth = 0.05f;
    private const float CatmullRomAlpha = 0.5f;
    private const float LengthEpsilon = 1e-5f;

    [Tooltip("Interior boundary control points in the XY plane, ordered left rim -> down the left wall -> across the floor -> up the right wall -> right rim.")]
    [SerializeField]
    private List<Vector2> outlinePoints = new List<Vector2>
    {
        new Vector2(-2.75f, 7.5f),
        new Vector2(-2.75f, 0f),
        new Vector2(0f, 0f),
        new Vector2(2.75f, 0f),
        new Vector2(2.75f, 7.5f)
    };

    [Tooltip("Catmull-Rom samples inserted between each pair of control points before arc-length re-sampling.")]
    [SerializeField] private int subdivisionsPerSegment = 8;

    [Tooltip("Target edge length after uniform arc-length re-sampling. Smaller means denser, smoother collision.")]
    [SerializeField] private float maxSegmentLength = 0.2f;

    [Tooltip("Outward extrusion in the XY plane that turns the interior outline into a solid ring.")]
    [SerializeField] private float wallThickness = 0.25f;

    [Tooltip("Thickness of the container along Z.")]
    [SerializeField] private float extrudeDepth = 1f;

    [Tooltip("World Y of the overflow line for this shape.")]
    [SerializeField] private float deathLineY = 6f;

    [Tooltip("World Y the held item hovers at for this shape.")]
    [SerializeField] private float dropY = 7f;

    /// <summary>Interior boundary control points, in authoring order.</summary>
    public IReadOnlyList<Vector2> OutlinePoints => outlinePoints;

    /// <summary>Mutable control point list, for the authoring tool only.</summary>
    public List<Vector2> EditableOutlinePoints => outlinePoints;

    /// <summary>Catmull-Rom samples inserted between control points.</summary>
    public int SubdivisionsPerSegment => Mathf.Clamp(subdivisionsPerSegment, MinSubdivisions, MaxSubdivisions);

    /// <summary>Target edge length after arc-length re-sampling.</summary>
    public float MaxSegmentLength => Mathf.Max(MinSegmentLength, maxSegmentLength);

    /// <summary>Outward XY extrusion that gives the shape solid walls.</summary>
    public float WallThickness => Mathf.Max(MinWallThickness, wallThickness);

    /// <summary>Container thickness along Z.</summary>
    public float ExtrudeDepth => Mathf.Max(MinExtrudeDepth, extrudeDepth);

    /// <summary>World Y of the overflow line.</summary>
    public float DeathLineY => deathLineY;

    /// <summary>World Y the held item hovers at.</summary>
    public float DropY => dropY;

    /// <summary>
    /// Smooths the control points with a Catmull-Rom spline (endpoints duplicated so the first and
    /// last control points are interpolated) and re-samples the result to uniform arc length so
    /// tessellation density is even regardless of control point spacing.
    /// </summary>
    public Vector2[] BuildSampledOutline()
    {
        int count = outlinePoints != null ? outlinePoints.Count : 0;
        if (count == 0)
        {
            return new Vector2[0];
        }

        if (count == 1)
        {
            return new[] { outlinePoints[0] };
        }

        // Each control-point interval is smoothed and re-sampled on its own so the authored control
        // points stay exactly on the outline; a single global re-sample would chamfer sharp corners.
        List<Vector2> result = new List<Vector2>(count * (SubdivisionsPerSegment + 2));
        List<Vector2> scratch = new List<Vector2>(SubdivisionsPerSegment + 2);

        for (int i = 0; i < count - 1; i++)
        {
            BuildSmoothedInterval(i, scratch);
            Vector2[] resampled = ResampleUniform(scratch, MaxSegmentLength);
            if (resampled.Length == 0)
            {
                continue;
            }

            if (result.Count == 0)
            {
                result.Add(resampled[0]);
            }

            for (int j = 1; j < resampled.Length; j++)
            {
                result.Add(resampled[j]);
            }
        }

        return result.ToArray();
    }

    /// <summary>Runs every authoring rule and appends a message per problem found. Returns true when clean.</summary>
    public bool Validate(List<string> errors, List<string> warnings)
    {
        errors?.Clear();
        warnings?.Clear();

        if (outlinePoints == null || outlinePoints.Count < MinimumControlPointCount)
        {
            errors?.Add($"Needs at least {MinimumControlPointCount} control points.");
            return false;
        }

        if (dropY < deathLineY)
        {
            warnings?.Add($"DropY ({dropY:0.##}) is below DeathLineY ({deathLineY:0.##}); the held item spawns already overflowing.");
        }

        Vector2[] sampled = BuildSampledOutline();
        if (sampled.Length < MinimumControlPointCount)
        {
            errors?.Add("The sampled outline collapsed to fewer than 3 points; control points are coincident.");
            return false;
        }

        if (CountCrossingsAtY(sampled, dropY) > 2)
        {
            warnings?.Add($"The outline crosses DropY ({dropY:0.##}) more than twice; the drop span is ambiguous there.");
        }

        return errors == null || errors.Count == 0;
    }

    /// <summary>Counts how many outline segments a horizontal line at the given Y passes through.</summary>
    public static int CountCrossingsAtY(IReadOnlyList<Vector2> outline, float y)
    {
        int crossings = 0;
        for (int i = 0; i < outline.Count - 1; i++)
        {
            Vector2 a = outline[i];
            Vector2 b = outline[i + 1];
            if ((a.y <= y && b.y > y) || (b.y <= y && a.y > y))
            {
                crossings++;
            }
        }

        return crossings;
    }

    private void BuildSmoothedInterval(int index, List<Vector2> result)
    {
        result.Clear();

        int count = outlinePoints.Count;
        Vector2 p0 = outlinePoints[Mathf.Max(0, index - 1)];
        Vector2 p1 = outlinePoints[index];
        Vector2 p2 = outlinePoints[index + 1];
        Vector2 p3 = outlinePoints[Mathf.Min(count - 1, index + 2)];

        int steps = SubdivisionsPerSegment + 1;
        result.Add(p1);
        for (int step = 1; step <= steps; step++)
        {
            result.Add(EvaluateCatmullRom(p0, p1, p2, p3, (float)step / steps));
        }
    }

    private static Vector2 EvaluateCatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
    {
        Vector2 m1 = CatmullRomAlpha * (p2 - p0);
        Vector2 m2 = CatmullRomAlpha * (p3 - p1);
        float t2 = t * t;
        float t3 = t2 * t;

        return ((2f * t3) - (3f * t2) + 1f) * p1
            + (t3 - (2f * t2) + t) * m1
            + ((-2f * t3) + (3f * t2)) * p2
            + (t3 - t2) * m2;
    }

    /// <summary>Re-samples an open polyline so every edge is at most the target length and all edges are equal.</summary>
    public static Vector2[] ResampleUniform(IReadOnlyList<Vector2> polyline, float targetSegmentLength)
    {
        if (polyline == null || polyline.Count < 2)
        {
            return polyline != null && polyline.Count == 1 ? new[] { polyline[0] } : new Vector2[0];
        }

        List<Vector2> cleaned = new List<Vector2>(polyline.Count) { polyline[0] };
        for (int i = 1; i < polyline.Count; i++)
        {
            if ((polyline[i] - cleaned[cleaned.Count - 1]).sqrMagnitude > LengthEpsilon * LengthEpsilon)
            {
                cleaned.Add(polyline[i]);
            }
        }

        if (cleaned.Count < 2)
        {
            return cleaned.ToArray();
        }

        float totalLength = 0f;
        for (int i = 0; i < cleaned.Count - 1; i++)
        {
            totalLength += Vector2.Distance(cleaned[i], cleaned[i + 1]);
        }

        int segmentCount = Mathf.Max(1, Mathf.CeilToInt(totalLength / Mathf.Max(MinSegmentLength, targetSegmentLength)));
        float step = totalLength / segmentCount;

        Vector2[] result = new Vector2[segmentCount + 1];
        result[0] = cleaned[0];
        result[segmentCount] = cleaned[cleaned.Count - 1];

        int sourceIndex = 0;
        float walked = 0f;
        for (int i = 1; i < segmentCount; i++)
        {
            float target = step * i;
            while (sourceIndex < cleaned.Count - 2)
            {
                float segmentLength = Vector2.Distance(cleaned[sourceIndex], cleaned[sourceIndex + 1]);
                if (walked + segmentLength >= target)
                {
                    break;
                }

                walked += segmentLength;
                sourceIndex++;
            }

            float currentLength = Vector2.Distance(cleaned[sourceIndex], cleaned[sourceIndex + 1]);
            float local = currentLength <= LengthEpsilon ? 0f : Mathf.Clamp01((target - walked) / currentLength);
            result[i] = Vector2.LerpUnclamped(cleaned[sourceIndex], cleaned[sourceIndex + 1], local);
        }

        return result;
    }

    private void OnValidate()
    {
        subdivisionsPerSegment = Mathf.Clamp(subdivisionsPerSegment, MinSubdivisions, MaxSubdivisions);
        maxSegmentLength = Mathf.Max(MinSegmentLength, maxSegmentLength);
        wallThickness = Mathf.Max(MinWallThickness, wallThickness);
        extrudeDepth = Mathf.Max(MinExtrudeDepth, extrudeDepth);

        if (outlinePoints == null || outlinePoints.Count < MinimumControlPointCount)
        {
            Debug.LogWarning($"{name}: needs at least {MinimumControlPointCount} outline control points.", this);
        }
    }
}
