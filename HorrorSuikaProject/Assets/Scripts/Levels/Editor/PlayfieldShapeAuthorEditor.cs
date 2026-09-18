using UnityEditor;
using UnityEngine;

/// <summary>
/// The shape authoring tool: scene-view curve editing with undo, mirror-X, live geometry and fit
/// preview, validation gating, and one-click Bake / Bake and Install.
/// </summary>
[CustomEditor(typeof(PlayfieldShapeAuthor))]
public class PlayfieldShapeAuthorEditor : Editor
{
    private readonly System.Collections.Generic.List<string> errors = new System.Collections.Generic.List<string>();
    private readonly System.Collections.Generic.List<string> warnings = new System.Collections.Generic.List<string>();

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
        PlayfieldShapeAuthoringGui.CollectValidation(
            definition,
            author.TierTable,
            errors,
            warnings,
            out int vertexCount,
            out int triangleCount);
        PlayfieldShapeAuthoringGui.DrawValidation(errors, warnings, vertexCount, triangleCount);

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

    private void DrawBakeButtons(PlayfieldShapeAuthor author, PlayfieldShapeDefinition definition)
    {
        using (new EditorGUI.DisabledScope(errors.Count > 0))
        {
            if (GUILayout.Button("Bake"))
            {
                PlayfieldShapeAuthoringGui.Bake(definition, author.BakeTargetPrefab);
            }

            using (new EditorGUI.DisabledScope(author.ShapeHost == null))
            {
                if (GUILayout.Button("Bake and Install"))
                {
                    GameObject prefab = PlayfieldShapeAuthoringGui.Bake(definition, author.BakeTargetPrefab);
                    PlayfieldShapeAuthoringGui.Install(prefab, author.ShapeHost, author);
                }
            }
        }

        if (author.ShapeHost == null)
        {
            EditorGUILayout.HelpBox($"Assign a {nameof(PlayfieldShapeHost)} to enable Bake and Install.", MessageType.Info);
        }
    }

    private void OnSceneGUI()
    {
        PlayfieldShapeAuthor author = (PlayfieldShapeAuthor)target;
        PlayfieldShapeAuthoringGui.DrawSceneGui(author.Definition, author.MirrorX, author.TierTable);
    }
}
