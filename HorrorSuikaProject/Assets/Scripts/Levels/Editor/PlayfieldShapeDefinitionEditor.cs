using UnityEditor;
using UnityEngine;

/// <summary>
/// Inspector for a playfield curve. Shows spawn height and the death line under their own heading
/// so the level author can edit them without hunting through the raw field names.
/// </summary>
[CustomEditor(typeof(PlayfieldShapeDefinition))]
public class PlayfieldShapeDefinitionEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.LabelField("Heights", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(
            serializedObject.FindProperty("dropY"),
            new GUIContent("Spawn Height", "World Y where the next item appears and hovers before it is dropped."));
        EditorGUILayout.PropertyField(
            serializedObject.FindProperty("deathLineY"),
            new GUIContent("Death Line", "World Y of the overflow line. A sphere that keeps touching this line, or sitting above it, loses the game."));
        EditorGUILayout.HelpBox(
            "Drag the Spawn and Death handles in the Scene view, or type the heights here. Bake the shape before playing so the installed container uses them.",
            MessageType.None);

        EditorGUILayout.Space();
        DrawPropertiesExcluding(serializedObject, "m_Script", "deathLineY", "dropY");

        serializedObject.ApplyModifiedProperties();
    }
}
