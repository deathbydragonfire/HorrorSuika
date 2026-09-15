using System.Collections.Generic;
using Bezi;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor-only entry points that let a playfield shape be baked without opening the authoring tool.
/// </summary>
public static class PlayfieldShapeBakeActions
{
    [BeziAction("Bakes a PlayfieldShapeDefinition asset into a watertight collision/render mesh asset and a container prefab. Pass the definition's asset path; prefab and mesh paths default to Assets/Prefabs/Shapes and Assets/Models/Shapes.")]
    public static string BakePlayfieldShape(string definitionAssetPath, string prefabPath = null, string meshPath = null)
    {
        PlayfieldShapeDefinition definition = AssetDatabase.LoadAssetAtPath<PlayfieldShapeDefinition>(definitionAssetPath);
        if (definition == null)
        {
            throw new System.ArgumentException($"No PlayfieldShapeDefinition at '{definitionAssetPath}'.");
        }

        string resolvedPrefabPath = string.IsNullOrEmpty(prefabPath)
            ? PlayfieldShapeBaker.GetDefaultPrefabPath(definition)
            : prefabPath;
        string resolvedMeshPath = string.IsNullOrEmpty(meshPath)
            ? PlayfieldShapeBaker.GetDefaultMeshPath(definition)
            : meshPath;

        GameObject prefab = PlayfieldShapeBaker.Bake(definition, resolvedPrefabPath, resolvedMeshPath);
        if (prefab == null)
        {
            throw new System.InvalidOperationException($"Baking '{definition.name}' failed; see the console for the reason.");
        }

        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(resolvedMeshPath);
        int triangleCount = mesh != null ? mesh.triangles.Length / 3 : 0;
        return $"Baked '{definition.name}' -> {resolvedPrefabPath} ({mesh?.vertexCount ?? 0} verts, {triangleCount} tris).";
    }

    [BeziAction("Reports the validation state of a PlayfieldShapeDefinition (errors and warnings the baker would raise) plus the vertex and triangle count the bake would produce. probeAboveFloor excludes the region right above the floor, where the span naturally closes, from the narrowest-neck measurement.", IsReadOnly = true)]
    public static string InspectPlayfieldShape(string definitionAssetPath, float probeAboveFloor = 1.5f)
    {
        PlayfieldShapeDefinition definition = AssetDatabase.LoadAssetAtPath<PlayfieldShapeDefinition>(definitionAssetPath);
        if (definition == null)
        {
            throw new System.ArgumentException($"No PlayfieldShapeDefinition at '{definitionAssetPath}'.");
        }

        List<string> errors = new List<string>();
        List<string> warnings = new List<string>();
        definition.Validate(errors, warnings);

        Vector2[] inner = definition.BuildSampledOutline();
        Vector2[] outer = PlayfieldShapeGeometry.BuildOuterOutline(inner, definition.WallThickness);
        if (PlayfieldShapeGeometry.HasSelfIntersection(outer))
        {
            errors.Add("The outer wall self-intersects at a concave corner; reduce the wall thickness or soften the corner.");
        }

        float floorY = float.MaxValue;
        for (int i = 0; i < inner.Length; i++)
        {
            floorY = Mathf.Min(floorY, inner[i].y);
        }

        float probeFloor = floorY + probeAboveFloor;
        float narrowest = float.MaxValue;
        float narrowestY = 0f;
        for (int i = 0; i < inner.Length; i++)
        {
            float y = inner[i].y;
            if (y < probeFloor || !TryMeasureSpan(inner, y, out float width))
            {
                continue;
            }

            if (width < narrowest)
            {
                narrowest = width;
                narrowestY = y;
            }
        }

        return string.Join("\n", new[]
        {
            $"{definition.name}: {inner.Length} outline points, {PlayfieldShapeGeometry.CountVertices(inner.Length)} verts, {PlayfieldShapeGeometry.CountTriangles(inner.Length)} tris, floorY {floorY:0.###}.",
            $"Narrowest interior span above y {probeFloor:0.###}: {(narrowest == float.MaxValue ? 0f : narrowest):0.###} at y {narrowestY:0.###}.",
            errors.Count == 0 ? "Errors: none." : $"Errors: {string.Join(" | ", errors)}",
            warnings.Count == 0 ? "Warnings: none." : $"Warnings: {string.Join(" | ", warnings)}"
        });
    }

    private static bool TryMeasureSpan(Vector2[] outline, float y, out float width)
    {
        width = 0f;
        bool foundLeft = false;
        bool foundRight = false;
        float minX = 0f;
        float maxX = 0f;

        for (int i = 0; i < outline.Length - 1; i++)
        {
            Vector2 a = outline[i];
            Vector2 b = outline[i + 1];
            if (!((a.y <= y && b.y > y) || (b.y <= y && a.y > y)))
            {
                continue;
            }

            float t = Mathf.Approximately(b.y, a.y) ? 0f : (y - a.y) / (b.y - a.y);
            float x = Mathf.LerpUnclamped(a.x, b.x, t);
            if (x <= 0f)
            {
                if (!foundLeft || x > minX)
                {
                    minX = x;
                    foundLeft = true;
                }
            }
            else if (!foundRight || x < maxX)
            {
                maxX = x;
                foundRight = true;
            }
        }

        if (!foundLeft || !foundRight)
        {
            return false;
        }

        width = maxX - minX;
        return true;
    }
}
