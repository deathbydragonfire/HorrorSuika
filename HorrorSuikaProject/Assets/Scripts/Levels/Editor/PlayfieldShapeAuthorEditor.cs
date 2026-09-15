using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The shape authoring tool: scene-view curve editing with undo, mirror-X, live geometry and fit
/// preview, validation gating, and one-click Bake / Bake and Install.
/// </summary>
[CustomEditor(typeof(PlayfieldShapeAuthor))]
public class PlayfieldShapeAuthorEditor : Editor
{
    private const float HandleSizeFactor = 0.05f;
    private const float SegmentPickDistance = 0.35f;
    private const float LabelOffset = 0.35f;
    private const int FitCircleSegments = 48;

    private static readonly Color InnerOutlineColor = new Color(0.3f, 0.9f, 1f);
    private static readonly Color OuterOutlineColor = new Color(0.4f, 0.5f, 0.7f);
    private static readonly Color DeathLineColor = new Color(1f, 0.25f, 0.25f);
    private static readonly Color DropLineColor = new Color(1f, 0.9f, 0.3f);
    private static readonly Color FitCircleOkColor = new Color(0.4f, 1f, 0.5f);
    private static readonly Color FitCircleBadColor = new Color(1f, 0.35f, 0.2f);

    private readonly List<string> errors = new List<string>();
    private readonly List<string> warnings = new List<string>();

    private SerializedProperty definitionProperty;
    private SerializedProperty mirrorProperty;
    private SerializedProperty bakeTargetProperty;
    private SerializedProperty hostProperty;
    private SerializedProperty tierTableProperty;

    private Editor definitionEditor;

    private void OnEnable()
    {
        definitionProperty = serializedObject.FindProperty("definition");
        mirrorProperty = serializedObject.FindProperty("mirrorX");
        bakeTargetProperty = serializedObject.FindProperty("bakeTargetPrefab");
        hostProperty = serializedObject.FindProperty("shapeHost");
        tierTableProperty = serializedObject.FindProperty("tierTable");
    }

    private void OnDisable()
    {
        if (definitionEditor != null)
        {
            DestroyImmediate(definitionEditor);
            definitionEditor = null;
        }
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUILayout.PropertyField(definitionProperty);
        EditorGUILayout.PropertyField(mirrorProperty);
        EditorGUILayout.PropertyField(bakeTargetProperty);
        EditorGUILayout.PropertyField(hostProperty);
        EditorGUILayout.PropertyField(tierTableProperty);
        serializedObject.ApplyModifiedProperties();

        PlayfieldShapeAuthor author = (PlayfieldShapeAuthor)target;
        PlayfieldShapeDefinition definition = author.Definition;
        if (definition == null)
        {
            EditorGUILayout.HelpBox("Assign a PlayfieldShapeDefinition to start authoring.", MessageType.Info);
            return;
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Curve", EditorStyles.boldLabel);
        DrawDefinitionInspector(definition);

        EditorGUILayout.Space();
        DrawValidation(author, definition);

        EditorGUILayout.Space();
        DrawBakeButtons(author, definition);
    }

    private void DrawDefinitionInspector(PlayfieldShapeDefinition definition)
    {
        if (definitionEditor == null || definitionEditor.target != definition)
        {
            if (definitionEditor != null)
            {
                DestroyImmediate(definitionEditor);
            }

            definitionEditor = CreateEditor(definition);
        }

        definitionEditor.OnInspectorGUI();
    }

    private void DrawValidation(PlayfieldShapeAuthor author, PlayfieldShapeDefinition definition)
    {
        CollectValidation(author, definition, out int vertexCount, out int triangleCount);

        EditorGUILayout.LabelField("Bake Cost", $"{vertexCount} vertices, {triangleCount} triangles");

        for (int i = 0; i < errors.Count; i++)
        {
            EditorGUILayout.HelpBox(errors[i], MessageType.Error);
        }

        for (int i = 0; i < warnings.Count; i++)
        {
            EditorGUILayout.HelpBox(warnings[i], MessageType.Warning);
        }

        if (errors.Count == 0 && warnings.Count == 0)
        {
            EditorGUILayout.HelpBox("Shape is valid and ready to bake.", MessageType.Info);
        }
    }

    private void CollectValidation(
        PlayfieldShapeAuthor author,
        PlayfieldShapeDefinition definition,
        out int vertexCount,
        out int triangleCount)
    {
        definition.Validate(errors, warnings);

        Vector2[] inner = definition.BuildSampledOutline();
        vertexCount = PlayfieldShapeGeometry.CountVertices(inner.Length);
        triangleCount = PlayfieldShapeGeometry.CountTriangles(inner.Length);

        if (inner.Length < PlayfieldShapeDefinition.MinimumControlPointCount)
        {
            return;
        }

        Vector2[] outer = PlayfieldShapeGeometry.BuildOuterOutline(inner, definition.WallThickness);
        if (PlayfieldShapeGeometry.HasSelfIntersection(outer))
        {
            errors.Add(
                $"The outer wall self-intersects: wall thickness ({definition.WallThickness:0.###}) exceeds the " +
                "radius of curvature at a concave corner and would emit inverted, non-watertight triangles.");
        }

        float largestRadius = GetLargestTierRadius(author);
        if (largestRadius <= 0f)
        {
            return;
        }

        if (definition.WallThickness < largestRadius)
        {
            warnings.Add(
                $"Wall thickness ({definition.WallThickness:0.###}) is below the largest tier radius " +
                $"({largestRadius:0.###}); a thin wall invites tunnelling.");
        }

        if (TryFindNarrowestNeck(inner, largestRadius * 2f, out float neckWidth, out float neckY)
            && neckWidth < largestRadius * 2f)
        {
            errors.Add(
                $"The interior necks down to {neckWidth:0.###} at y {neckY:0.###}, narrower than the largest " +
                $"tier's diameter ({largestRadius * 2f:0.###}); the top-tier item cannot pass.");
        }
    }

    private void DrawBakeButtons(PlayfieldShapeAuthor author, PlayfieldShapeDefinition definition)
    {
        using (new EditorGUI.DisabledScope(errors.Count > 0))
        {
            if (GUILayout.Button("Bake"))
            {
                Bake(author, definition);
            }

            using (new EditorGUI.DisabledScope(author.ShapeHost == null))
            {
                if (GUILayout.Button("Bake and Install"))
                {
                    GameObject prefab = Bake(author, definition);
                    if (prefab != null && author.ShapeHost != null)
                    {
                        Undo.RegisterFullObjectHierarchyUndo(author.ShapeHost.gameObject, "Install Playfield Shape");
                        author.ShapeHost.Apply(prefab);
                        EditorSceneManagerMarkDirty(author);
                    }
                }
            }
        }

        if (author.ShapeHost == null)
        {
            EditorGUILayout.HelpBox($"Assign a {nameof(PlayfieldShapeHost)} to enable Bake and Install.", MessageType.Info);
        }
    }

    private static void EditorSceneManagerMarkDirty(PlayfieldShapeAuthor author)
    {
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(author.gameObject.scene);
    }

    private static GameObject Bake(PlayfieldShapeAuthor author, PlayfieldShapeDefinition definition)
    {
        string prefabPath = author.BakeTargetPrefab != null
            ? AssetDatabase.GetAssetPath(author.BakeTargetPrefab)
            : PlayfieldShapeBaker.GetDefaultPrefabPath(definition);

        if (string.IsNullOrEmpty(prefabPath))
        {
            prefabPath = PlayfieldShapeBaker.GetDefaultPrefabPath(definition);
        }

        WarnWhenPrefabIsReferencedByALevel(prefabPath);
        return PlayfieldShapeBaker.Bake(definition, prefabPath, PlayfieldShapeBaker.GetDefaultMeshPath(definition));
    }

    private static void WarnWhenPrefabIsReferencedByALevel(string prefabPath)
    {
        string[] levelGuids = AssetDatabase.FindAssets("t:LevelDefinition");
        for (int i = 0; i < levelGuids.Length; i++)
        {
            string levelPath = AssetDatabase.GUIDToAssetPath(levelGuids[i]);
            string[] dependencies = AssetDatabase.GetDependencies(levelPath, false);
            for (int j = 0; j < dependencies.Length; j++)
            {
                if (dependencies[j] != prefabPath)
                {
                    continue;
                }

                Debug.LogWarning(
                    $"'{prefabPath}' is referenced by the level '{System.IO.Path.GetFileNameWithoutExtension(levelPath)}'; " +
                    "re-baking changes that level's playfield.");
                return;
            }
        }
    }

    private void OnSceneGUI()
    {
        PlayfieldShapeAuthor author = (PlayfieldShapeAuthor)target;
        PlayfieldShapeDefinition definition = author.Definition;
        if (definition == null)
        {
            return;
        }

        List<Vector2> points = definition.EditableOutlinePoints;
        if (points == null)
        {
            return;
        }

        DrawPreview(author, definition);
        HandleSegmentInsertion(definition, points, author.MirrorX);
        DrawPointHandles(definition, points, author.MirrorX);
    }

    private void DrawPreview(PlayfieldShapeAuthor author, PlayfieldShapeDefinition definition)
    {
        Vector2[] inner = definition.BuildSampledOutline();
        if (inner.Length < 2)
        {
            return;
        }

        Vector2[] outer = PlayfieldShapeGeometry.BuildOuterOutline(inner, definition.WallThickness);

        DrawPolyline(inner, InnerOutlineColor);
        DrawPolyline(outer, OuterOutlineColor);

        DrawSpanLine(inner, definition.DeathLineY, DeathLineColor, "Death");
        DrawSpanLine(inner, definition.DropY, DropLineColor, "Drop");

        float largestRadius = GetLargestTierRadius(author);
        if (largestRadius > 0f && TryFindNarrowestNeck(inner, largestRadius * 2f, out float neckWidth, out float neckY))
        {
            bool fits = neckWidth >= largestRadius * 2f;
            Handles.color = fits ? FitCircleOkColor : FitCircleBadColor;
            DrawCircle(new Vector3(0f, neckY, 0f), largestRadius);
            Handles.Label(
                new Vector3(largestRadius + LabelOffset, neckY, 0f),
                $"top tier d {largestRadius * 2f:0.##} / neck {neckWidth:0.##}");
        }

        Handles.color = Color.white;
        Handles.Label(
            new Vector3(0f, definition.DropY + LabelOffset, 0f),
            $"{inner.Length} pts | {PlayfieldShapeGeometry.CountVertices(inner.Length)} verts | " +
            $"{PlayfieldShapeGeometry.CountTriangles(inner.Length)} tris");
    }

    private static void DrawPolyline(Vector2[] polyline, Color color)
    {
        Handles.color = color;
        for (int i = 0; i < polyline.Length - 1; i++)
        {
            Handles.DrawLine(
                new Vector3(polyline[i].x, polyline[i].y, 0f),
                new Vector3(polyline[i + 1].x, polyline[i + 1].y, 0f));
        }
    }

    private static void DrawSpanLine(Vector2[] inner, float y, Color color, string label)
    {
        if (!TryMeasureSpan(inner, y, out float minX, out float maxX))
        {
            return;
        }

        Handles.color = color;
        Handles.DrawLine(new Vector3(minX, y, 0f), new Vector3(maxX, y, 0f));
        Handles.Label(new Vector3(maxX + LabelOffset, y, 0f), $"{label} {y:0.##}");
    }

    private static void DrawCircle(Vector3 center, float radius)
    {
        Vector3 previous = center + new Vector3(radius, 0f, 0f);
        for (int i = 1; i <= FitCircleSegments; i++)
        {
            float angle = (i / (float)FitCircleSegments) * Mathf.PI * 2f;
            Vector3 current = center + new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f);
            Handles.DrawLine(previous, current);
            previous = current;
        }
    }

    private void DrawPointHandles(PlayfieldShapeDefinition definition, List<Vector2> points, bool mirrorX)
    {
        Event current = Event.current;

        for (int i = 0; i < points.Count; i++)
        {
            Vector3 world = new Vector3(points[i].x, points[i].y, 0f);
            float size = HandleUtility.GetHandleSize(world) * HandleSizeFactor;
            bool isMirrorSlave = mirrorX && IsMirrorSlave(i, points.Count);

            Handles.color = isMirrorSlave ? Color.grey : Color.yellow;

            if (isMirrorSlave)
            {
                Handles.SphereHandleCap(0, world, Quaternion.identity, size * 2f, EventType.Repaint);
                continue;
            }

            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.FreeMoveHandle(world, size * 2f, Vector3.zero, Handles.SphereHandleCap);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(definition, "Move Shape Point");
                SetPoint(points, i, new Vector2(moved.x, moved.y), mirrorX);
                EditorUtility.SetDirty(definition);
            }

            if (current.type == EventType.MouseDown
                && current.button == 0
                && current.shift
                && points.Count > PlayfieldShapeDefinition.MinimumControlPointCount
                && HandleUtility.DistanceToCircle(world, size * 2f) <= 0f)
            {
                Undo.RecordObject(definition, "Delete Shape Point");
                RemovePoint(points, i, mirrorX);
                EditorUtility.SetDirty(definition);
                current.Use();
                return;
            }
        }
    }

    private void HandleSegmentInsertion(PlayfieldShapeDefinition definition, List<Vector2> points, bool mirrorX)
    {
        Event current = Event.current;
        if (current.type != EventType.MouseDown || current.button != 0 || !current.control)
        {
            return;
        }

        Ray ray = HandleUtility.GUIPointToWorldRay(current.mousePosition);
        if (Mathf.Approximately(ray.direction.z, 0f))
        {
            return;
        }

        float t = -ray.origin.z / ray.direction.z;
        Vector3 hit = ray.origin + (ray.direction * t);
        Vector2 clicked = new Vector2(hit.x, hit.y);

        int bestIndex = -1;
        float bestDistance = SegmentPickDistance;
        for (int i = 0; i < points.Count - 1; i++)
        {
            float distance = DistanceToSegment(clicked, points[i], points[i + 1]);
            if (distance >= bestDistance)
            {
                continue;
            }

            bestDistance = distance;
            bestIndex = i;
        }

        if (bestIndex < 0)
        {
            return;
        }

        if (mirrorX && clicked.x > 0f)
        {
            return;
        }

        Undo.RecordObject(definition, "Insert Shape Point");
        InsertPoint(points, bestIndex + 1, clicked, mirrorX);
        EditorUtility.SetDirty(definition);
        current.Use();
    }

    private static void SetPoint(List<Vector2> points, int index, Vector2 value, bool mirrorX)
    {
        if (!mirrorX)
        {
            points[index] = value;
            return;
        }

        int mirrorIndex = points.Count - 1 - index;
        if (mirrorIndex == index)
        {
            points[index] = new Vector2(0f, value.y);
            return;
        }

        value.x = Mathf.Min(0f, value.x);
        points[index] = value;
        points[mirrorIndex] = new Vector2(-value.x, value.y);
    }

    private static void InsertPoint(List<Vector2> points, int index, Vector2 value, bool mirrorX)
    {
        if (!mirrorX)
        {
            points.Insert(index, value);
            return;
        }

        value.x = Mathf.Min(0f, value.x);
        int mirrorIndex = points.Count - index;
        points.Insert(index, value);
        points.Insert(mirrorIndex + 1, new Vector2(-value.x, value.y));
    }

    private static void RemovePoint(List<Vector2> points, int index, bool mirrorX)
    {
        if (!mirrorX)
        {
            points.RemoveAt(index);
            return;
        }

        int mirrorIndex = points.Count - 1 - index;
        if (mirrorIndex == index)
        {
            points.RemoveAt(index);
            return;
        }

        if (points.Count - 2 < PlayfieldShapeDefinition.MinimumControlPointCount)
        {
            return;
        }

        points.RemoveAt(mirrorIndex);
        points.RemoveAt(index);
    }

    private static bool IsMirrorSlave(int index, int count)
    {
        return index > (count - 1) - index;
    }

    private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float lengthSquared = ab.sqrMagnitude;
        if (lengthSquared <= Mathf.Epsilon)
        {
            return Vector2.Distance(point, a);
        }

        float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / lengthSquared);
        return Vector2.Distance(point, a + (ab * t));
    }

    private static float GetLargestTierRadius(PlayfieldShapeAuthor author)
    {
        MergeItemTierTable table = author.TierTable;
        if (table == null || table.Tiers == null)
        {
            return 0f;
        }

        float largest = 0f;
        for (int i = 0; i < table.Tiers.Count; i++)
        {
            MergeItemTier tier = table.Tiers[i];
            if (tier != null)
            {
                largest = Mathf.Max(largest, tier.Radius);
            }
        }

        return largest;
    }

    private static bool TryFindNarrowestNeck(Vector2[] inner, float probeAboveFloor, out float width, out float y)
    {
        width = float.MaxValue;
        y = 0f;

        float floorY = float.MaxValue;
        for (int i = 0; i < inner.Length; i++)
        {
            floorY = Mathf.Min(floorY, inner[i].y);
        }

        // The span naturally closes right above the floor, so that band is excluded: what matters is
        // a neck above a wider region, not the bowl the ball rests in.
        float probeFloor = floorY + probeAboveFloor;
        bool found = false;

        for (int i = 0; i < inner.Length; i++)
        {
            float candidateY = inner[i].y;
            if (candidateY < probeFloor || !TryMeasureSpan(inner, candidateY, out float minX, out float maxX))
            {
                continue;
            }

            float span = maxX - minX;
            if (span >= width)
            {
                continue;
            }

            width = span;
            y = candidateY;
            found = true;
        }

        if (!found)
        {
            width = 0f;
        }

        return found;
    }

    private static bool TryMeasureSpan(Vector2[] outline, float y, out float minX, out float maxX)
    {
        minX = 0f;
        maxX = 0f;
        bool foundLeft = false;
        bool foundRight = false;

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

        return foundLeft && foundRight;
    }
}
