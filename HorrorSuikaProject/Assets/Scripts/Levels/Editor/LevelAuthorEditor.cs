using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

/// <summary>
/// Level authoring inspector: create and edit a <see cref="LevelDefinition"/>, draw its playfield
/// curve in the Scene view, and manage sequence membership.
/// </summary>
[CustomEditor(typeof(LevelAuthor))]
public class LevelAuthorEditor : Editor
{
    private const string LevelsFolder = "Assets/ScriptableObjects/Levels";
    private const string ShapesFolder = "Assets/ScriptableObjects/Shapes";
    private const string ShapeTemplatePath = "Assets/ScriptableObjects/Shapes/Shape_Rect.asset";
    private const string DefaultSequencePath = "Assets/ScriptableObjects/Levels/MainLevelSequence.asset";
    private const string DefaultTierTablePath = "Assets/ScriptableObjects/DefaultItemTiers.asset";

    private readonly List<string> errors = new List<string>();
    private readonly List<string> warnings = new List<string>();

    private SerializedProperty levelProperty;
    private SerializedProperty sequenceProperty;
    private SerializedProperty shapeProperty;
    private SerializedProperty mirrorProperty;
    private SerializedProperty bakeTargetProperty;
    private SerializedProperty hostProperty;
    private SerializedProperty tierTableProperty;

    private SerializedObject levelSerialized;
    private ReorderableList objectiveList;
    private Editor shapeDefinitionEditor;
    private string newLevelName = "New Level";

    private void OnEnable()
    {
        levelProperty = serializedObject.FindProperty("level");
        sequenceProperty = serializedObject.FindProperty("sequence");
        shapeProperty = serializedObject.FindProperty("shapeDefinition");
        mirrorProperty = serializedObject.FindProperty("mirrorX");
        bakeTargetProperty = serializedObject.FindProperty("bakeTargetPrefab");
        hostProperty = serializedObject.FindProperty("shapeHost");
        tierTableProperty = serializedObject.FindProperty("tierTable");
        BindLevelSerialized();
    }

    private void OnDisable()
    {
        DisposeShapeEditor();
        levelSerialized?.Dispose();
        levelSerialized = null;
        objectiveList = null;
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        LevelAuthor author = (LevelAuthor)target;

        DrawCreateSection(author);
        EditorGUILayout.Space();
        DrawLevelIdentity();
        SyncShapeFromLevel();
        serializedObject.ApplyModifiedProperties();

        LevelDefinition level = author.Level;
        if (level == null)
        {
            EditorGUILayout.HelpBox("Create a level or assign an existing Level Definition to begin.", MessageType.Info);
            return;
        }

        BindLevelSerialized();
        levelSerialized.Update();

        EditorGUILayout.Space();
        DrawPassRequirements(author);
        EditorGUILayout.Space();
        DrawLimits();
        EditorGUILayout.Space();
        DrawShapeSection(author);
        EditorGUILayout.Space();
        DrawSequenceSection(author);
        EditorGUILayout.Space();
        DrawPlayFromEditor(author);

        if (levelSerialized.ApplyModifiedProperties())
        {
            EditorUtility.SetDirty(level);
        }
    }

    private void OnSceneGUI()
    {
        LevelAuthor author = (LevelAuthor)target;
        PlayfieldShapeAuthoringGui.DrawSceneGui(author.ShapeDefinition, author.MirrorX, author.TierTable);
    }

    [MenuItem("Merge Drop/Select Level Author")]
    private static void SelectLevelAuthor()
    {
        LevelAuthor author = Object.FindFirstObjectByType<LevelAuthor>(FindObjectsInactive.Include);
        if (author == null)
        {
            Debug.LogWarning("No LevelAuthor in the open scene. Add the LevelAuthor component to a scene object first.");
            return;
        }

        Selection.activeGameObject = author.gameObject;
        if (SceneView.lastActiveSceneView != null)
        {
            SceneView.lastActiveSceneView.FrameSelected();
        }
    }

    private void DrawCreateSection(LevelAuthor author)
    {
        EditorGUILayout.LabelField("Create", EditorStyles.boldLabel);
        newLevelName = EditorGUILayout.TextField("Name", newLevelName);
        using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(newLevelName)))
        {
            if (GUILayout.Button("Create New Level"))
            {
                CreateNewLevel(author, newLevelName.Trim());
            }
        }

        using (new EditorGUI.DisabledScope(author.Level == null))
        {
            if (GUILayout.Button("Duplicate Current Level"))
            {
                DuplicateCurrentLevel(author);
            }
        }
    }

    private void DrawLevelIdentity()
    {
        EditorGUILayout.LabelField("Level", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(levelProperty);
        EditorGUILayout.PropertyField(sequenceProperty);
        EditorGUILayout.PropertyField(tierTableProperty);

        if (levelSerialized == null)
        {
            return;
        }

        EditorGUILayout.PropertyField(levelSerialized.FindProperty("levelId"));
        EditorGUILayout.PropertyField(levelSerialized.FindProperty("displayName"));
        EditorGUILayout.PropertyField(levelSerialized.FindProperty("tierTable"));
    }

    private void DrawPassRequirements(LevelAuthor author)
    {
        EditorGUILayout.LabelField("Pass Requirements", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Every objective must hold at the same time to win. Cumulative counts every item of that tier ever produced. Simultaneous counts items on the board right now (merging one away can regress it).",
            MessageType.None);

        EnsureObjectiveList(author);
        objectiveList.DoLayoutList();

        if (levelSerialized.FindProperty("objectives").arraySize == 0)
        {
            EditorGUILayout.HelpBox("A level with no objectives can never be won.", MessageType.Warning);
        }
    }

    private void DrawLimits()
    {
        EditorGUILayout.LabelField("Spawn and Failure", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(levelSerialized.FindProperty("initialSpawnableTierCount"));
        EditorGUILayout.PropertyField(levelSerialized.FindProperty("maxSpawnableTierCount"));
        EditorGUILayout.PropertyField(levelSerialized.FindProperty("dropLimit"));
        EditorGUILayout.PropertyField(levelSerialized.FindProperty("timeLimitSeconds"));
        EditorGUILayout.PropertyField(levelSerialized.FindProperty("randomSeed"));
        EditorGUILayout.PropertyField(levelSerialized.FindProperty("victorySettleTimeout"));
        EditorGUILayout.HelpBox("Drop limit and time limit of 0 mean unlimited. Spawn counts of 0 fall back to the tier table.", MessageType.None);
    }

    private void DrawShapeSection(LevelAuthor author)
    {
        EditorGUILayout.LabelField("Playfield Shape", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Drag yellow points in the Scene view. Ctrl-click a segment to insert a point. Shift-click a point to delete it. Mirror X authors the left half only.",
            MessageType.None);

        EditorGUILayout.PropertyField(shapeProperty);
        EditorGUILayout.PropertyField(mirrorProperty);
        EditorGUILayout.PropertyField(bakeTargetProperty);
        EditorGUILayout.PropertyField(hostProperty);

        PlayfieldShapeDefinition definition = author.ShapeDefinition;
        if (definition == null)
        {
            EditorGUILayout.HelpBox("Assign or create a Playfield Shape to draw the container curve.", MessageType.Info);
            return;
        }

        DrawShapeDefinitionInspector(definition);

        PlayfieldShapeAuthoringGui.CollectValidation(
            definition,
            author.TierTable,
            errors,
            warnings,
            out int vertexCount,
            out int triangleCount);
        PlayfieldShapeAuthoringGui.DrawValidation(errors, warnings, vertexCount, triangleCount);

        using (new EditorGUI.DisabledScope(errors.Count > 0))
        {
            if (GUILayout.Button("Bake Shape"))
            {
                AssignBakedPrefab(author, PlayfieldShapeAuthoringGui.Bake(definition, author.BakeTargetPrefab));
            }

            using (new EditorGUI.DisabledScope(author.ShapeHost == null))
            {
                if (GUILayout.Button("Bake and Install"))
                {
                    GameObject prefab = PlayfieldShapeAuthoringGui.Bake(definition, author.BakeTargetPrefab);
                    PlayfieldShapeAuthoringGui.Install(prefab, author.ShapeHost, author);
                    AssignBakedPrefab(author, prefab);
                }
            }
        }

        if (author.ShapeHost == null)
        {
            EditorGUILayout.HelpBox($"Assign a {nameof(PlayfieldShapeHost)} to enable Bake and Install.", MessageType.Info);
        }
    }

    private void DrawSequenceSection(LevelAuthor author)
    {
        EditorGUILayout.LabelField("Level Sequence", EditorStyles.boldLabel);
        LevelSequence sequence = author.Sequence;
        LevelDefinition level = author.Level;
        if (sequence == null)
        {
            EditorGUILayout.HelpBox("Assign a Level Sequence to include this level in progression and the select screen.", MessageType.Info);
            return;
        }

        SerializedObject sequenceSo = new SerializedObject(sequence);
        SerializedProperty levelsProperty = sequenceSo.FindProperty("levels");
        int currentIndex = IndexOfLevel(levelsProperty, level);

        if (currentIndex < 0)
        {
            if (GUILayout.Button("Add to Sequence"))
            {
                Undo.RecordObject(sequence, "Add Level to Sequence");
                levelsProperty.arraySize++;
                levelsProperty.GetArrayElementAtIndex(levelsProperty.arraySize - 1).objectReferenceValue = level;
                sequenceSo.ApplyModifiedProperties();
                EditorUtility.SetDirty(sequence);
            }
        }
        else if (GUILayout.Button("Remove from Sequence"))
        {
            Undo.RecordObject(sequence, "Remove Level from Sequence");
            DeleteObjectListElement(levelsProperty, currentIndex);
            sequenceSo.ApplyModifiedProperties();
            EditorUtility.SetDirty(sequence);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Order", EditorStyles.miniBoldLabel);
        for (int i = 0; i < levelsProperty.arraySize; i++)
        {
            LevelDefinition entry = levelsProperty.GetArrayElementAtIndex(i).objectReferenceValue as LevelDefinition;
            using (new EditorGUILayout.HorizontalScope())
            {
                bool isCurrent = entry == level;
                GUIStyle style = isCurrent ? EditorStyles.boldLabel : EditorStyles.label;
                EditorGUILayout.LabelField($"{i + 1}. {(entry != null ? entry.DisplayName : "(missing)")}", style);
                using (new EditorGUI.DisabledScope(i == 0))
                {
                    if (GUILayout.Button("Up", GUILayout.Width(40f)))
                    {
                        levelsProperty.MoveArrayElement(i, i - 1);
                        sequenceSo.ApplyModifiedProperties();
                        EditorUtility.SetDirty(sequence);
                        break;
                    }
                }

                using (new EditorGUI.DisabledScope(i >= levelsProperty.arraySize - 1))
                {
                    if (GUILayout.Button("Down", GUILayout.Width(52f)))
                    {
                        levelsProperty.MoveArrayElement(i, i + 1);
                        sequenceSo.ApplyModifiedProperties();
                        EditorUtility.SetDirty(sequence);
                        break;
                    }
                }
            }
        }

        sequenceSo.Dispose();
    }

    private void DrawPlayFromEditor(LevelAuthor author)
    {
        EditorGUILayout.LabelField("Editor Play", EditorStyles.boldLabel);
        GameController controller = Object.FindFirstObjectByType<GameController>(FindObjectsInactive.Include);
        if (controller == null)
        {
            EditorGUILayout.HelpBox("No GameController in this scene; Play-from-editor wiring is unavailable.", MessageType.Info);
            return;
        }

        if (GUILayout.Button("Set as Play-from-Editor Level"))
        {
            SerializedObject controllerSo = new SerializedObject(controller);
            controllerSo.FindProperty("startingLevel").objectReferenceValue = author.Level;
            controllerSo.ApplyModifiedProperties();
            EditorUtility.SetDirty(controller);
        }
    }

    private void DrawShapeDefinitionInspector(PlayfieldShapeDefinition definition)
    {
        if (shapeDefinitionEditor == null || shapeDefinitionEditor.target != definition)
        {
            DisposeShapeEditor();
            shapeDefinitionEditor = CreateEditor(definition);
        }

        shapeDefinitionEditor.OnInspectorGUI();
    }

    private void EnsureObjectiveList(LevelAuthor author)
    {
        SerializedProperty objectives = levelSerialized.FindProperty("objectives");
        if (objectiveList != null && objectiveList.serializedProperty == objectives)
        {
            return;
        }

        objectiveList = new ReorderableList(levelSerialized, objectives, true, true, true, true)
        {
            drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Objectives"),
            elementHeight = EditorGUIUtility.singleLineHeight * 5f + 16f,
            drawElementCallback = (rect, index, active, focused) => DrawObjectiveElement(author, objectives.GetArrayElementAtIndex(index), rect),
            onAddCallback = list =>
            {
                list.serializedProperty.arraySize++;
                SerializedProperty added = list.serializedProperty.GetArrayElementAtIndex(list.serializedProperty.arraySize - 1);
                added.FindPropertyRelative("objectiveType").enumValueIndex = (int)LevelObjectiveType.TierCount;
                added.FindPropertyRelative("countMode").enumValueIndex = (int)ObjectiveCountMode.Cumulative;
                added.FindPropertyRelative("tierIndex").intValue = 2;
                added.FindPropertyRelative("requiredCount").intValue = 1;
                added.FindPropertyRelative("descriptionOverride").stringValue = string.Empty;
            }
        };
    }

    private void DrawObjectiveElement(LevelAuthor author, SerializedProperty objective, Rect rect)
    {
        float line = EditorGUIUtility.singleLineHeight;
        float y = rect.y + 2f;
        Rect LineRect()
        {
            Rect result = new Rect(rect.x, y, rect.width, line);
            y += line + 2f;
            return result;
        }

        SerializedProperty typeProperty = objective.FindPropertyRelative("objectiveType");
        SerializedProperty modeProperty = objective.FindPropertyRelative("countMode");
        SerializedProperty tierProperty = objective.FindPropertyRelative("tierIndex");
        SerializedProperty requiredProperty = objective.FindPropertyRelative("requiredCount");
        SerializedProperty overrideProperty = objective.FindPropertyRelative("descriptionOverride");

        EditorGUI.PropertyField(LineRect(), typeProperty, new GUIContent("Type"));

        bool isScore = typeProperty.enumValueIndex == (int)LevelObjectiveType.ScoreAtLeast;
        using (new EditorGUI.DisabledScope(isScore))
        {
            EditorGUI.PropertyField(LineRect(), modeProperty, new GUIContent("Count Mode"));
        }

        if (isScore)
        {
            requiredProperty.intValue = Mathf.Max(1, EditorGUI.IntField(LineRect(), "Required Score", requiredProperty.intValue));
        }
        else
        {
            MergeItemTierTable table = ResolveTierTable(author);
            if (table != null && table.Tiers != null && table.Tiers.Count > 0)
            {
                string[] names = new string[table.Tiers.Count];
                int[] values = new int[table.Tiers.Count];
                for (int i = 0; i < table.Tiers.Count; i++)
                {
                    MergeItemTier tier = table.Tiers[i];
                    names[i] = tier != null ? $"{i}: {tier.DisplayName}" : $"{i}: (missing)";
                    values[i] = i;
                }

                tierProperty.intValue = EditorGUI.IntPopup(LineRect(), "Tier", tierProperty.intValue, names, values);
            }
            else
            {
                EditorGUI.PropertyField(LineRect(), tierProperty, new GUIContent("Tier Index"));
            }

            requiredProperty.intValue = Mathf.Max(1, EditorGUI.IntField(LineRect(), "Required Count", requiredProperty.intValue));
        }

        EditorGUI.PropertyField(LineRect(), overrideProperty, new GUIContent("Label Override"));
    }

    private MergeItemTierTable ResolveTierTable(LevelAuthor author)
    {
        if (author.Level != null && author.Level.TierTableOverride != null)
        {
            return author.Level.TierTableOverride;
        }

        return author.TierTable;
    }

    private void BindLevelSerialized()
    {
        LevelDefinition level = levelProperty.objectReferenceValue as LevelDefinition;
        if (level == null)
        {
            levelSerialized?.Dispose();
            levelSerialized = null;
            objectiveList = null;
            return;
        }

        if (levelSerialized == null || levelSerialized.targetObject != level)
        {
            levelSerialized?.Dispose();
            levelSerialized = new SerializedObject(level);
            objectiveList = null;
        }
    }

    private void SyncShapeFromLevel()
    {
        LevelDefinition level = levelProperty.objectReferenceValue as LevelDefinition;
        if (level == null)
        {
            return;
        }

        if (shapeProperty.objectReferenceValue == null && level.ShapeDefinition != null)
        {
            shapeProperty.objectReferenceValue = level.ShapeDefinition;
        }

        if (bakeTargetProperty.objectReferenceValue == null && level.ShapePrefab != null)
        {
            bakeTargetProperty.objectReferenceValue = level.ShapePrefab;
        }
    }

    private void AssignBakedPrefab(LevelAuthor author, GameObject prefab)
    {
        if (prefab == null)
        {
            return;
        }

        serializedObject.Update();
        bakeTargetProperty.objectReferenceValue = prefab;
        serializedObject.ApplyModifiedProperties();

        if (author.Level == null)
        {
            return;
        }

        BindLevelSerialized();
        levelSerialized.Update();
        levelSerialized.FindProperty("shapePrefab").objectReferenceValue = prefab;
        levelSerialized.FindProperty("shapeDefinition").objectReferenceValue = author.ShapeDefinition;
        levelSerialized.ApplyModifiedProperties();
        EditorUtility.SetDirty(author.Level);
    }

    private void CreateNewLevel(LevelAuthor author, string displayName)
    {
        EnsureFolder(LevelsFolder);
        EnsureFolder(ShapesFolder);

        string stem = SanitizeFileStem(displayName);
        string shapePath = AssetDatabase.GenerateUniqueAssetPath($"{ShapesFolder}/Shape_{stem}.asset");
        string levelPath = AssetDatabase.GenerateUniqueAssetPath($"{LevelsFolder}/Level_{stem}.asset");

        PlayfieldShapeDefinition shape = CreateShapeAsset(shapePath, Path.GetFileNameWithoutExtension(shapePath));
        LevelDefinition level = ScriptableObject.CreateInstance<LevelDefinition>();
        AssetDatabase.CreateAsset(level, levelPath);

        SerializedObject created = new SerializedObject(level);
        created.FindProperty("levelId").stringValue = Path.GetFileNameWithoutExtension(levelPath).ToLowerInvariant();
        created.FindProperty("displayName").stringValue = displayName;
        created.FindProperty("shapeDefinition").objectReferenceValue = shape;
        created.FindProperty("initialSpawnableTierCount").intValue = 2;
        created.FindProperty("maxSpawnableTierCount").intValue = 4;
        created.FindProperty("victorySettleTimeout").floatValue = 3f;
        SerializedProperty objectives = created.FindProperty("objectives");
        objectives.arraySize = 1;
        SerializedProperty first = objectives.GetArrayElementAtIndex(0);
        first.FindPropertyRelative("objectiveType").enumValueIndex = (int)LevelObjectiveType.TierCount;
        first.FindPropertyRelative("countMode").enumValueIndex = (int)ObjectiveCountMode.Cumulative;
        first.FindPropertyRelative("tierIndex").intValue = 2;
        first.FindPropertyRelative("requiredCount").intValue = 1;
        created.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(level);

        serializedObject.Update();
        levelProperty.objectReferenceValue = level;
        shapeProperty.objectReferenceValue = shape;
        bakeTargetProperty.objectReferenceValue = null;
        if (sequenceProperty.objectReferenceValue == null)
        {
            sequenceProperty.objectReferenceValue = AssetDatabase.LoadAssetAtPath<LevelSequence>(DefaultSequencePath);
        }

        if (tierTableProperty.objectReferenceValue == null)
        {
            tierTableProperty.objectReferenceValue = AssetDatabase.LoadAssetAtPath<MergeItemTierTable>(DefaultTierTablePath);
        }

        serializedObject.ApplyModifiedProperties();
        AssetDatabase.SaveAssets();
        EditorGUIUtility.PingObject(level);
    }

    private void DuplicateCurrentLevel(LevelAuthor author)
    {
        LevelDefinition sourceLevel = author.Level;
        PlayfieldShapeDefinition sourceShape = author.ShapeDefinition;
        if (sourceLevel == null)
        {
            return;
        }

        string displayName = sourceLevel.DisplayName + " Copy";
        CreateNewLevel(author, displayName);

        PlayfieldShapeDefinition destShape = shapeProperty.objectReferenceValue as PlayfieldShapeDefinition;
        if (sourceShape != null && destShape != null && sourceShape != destShape)
        {
            EditorUtility.CopySerialized(sourceShape, destShape);
            destShape.name = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(destShape));
            EditorUtility.SetDirty(destShape);
        }

        LevelDefinition destLevel = levelProperty.objectReferenceValue as LevelDefinition;
        if (destLevel != null)
        {
            EditorUtility.CopySerialized(sourceLevel, destLevel);
            SerializedObject copy = new SerializedObject(destLevel);
            copy.FindProperty("displayName").stringValue = displayName;
            copy.FindProperty("levelId").stringValue = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(destLevel)).ToLowerInvariant();
            copy.FindProperty("shapeDefinition").objectReferenceValue = destShape;
            copy.FindProperty("shapePrefab").objectReferenceValue = null;
            copy.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(destLevel);
        }

        AssetDatabase.SaveAssets();
    }

    private static PlayfieldShapeDefinition CreateShapeAsset(string path, string assetName)
    {
        PlayfieldShapeDefinition template = AssetDatabase.LoadAssetAtPath<PlayfieldShapeDefinition>(ShapeTemplatePath);
        PlayfieldShapeDefinition shape = template != null
            ? Object.Instantiate(template)
            : ScriptableObject.CreateInstance<PlayfieldShapeDefinition>();
        shape.name = assetName;
        AssetDatabase.CreateAsset(shape, path);
        EditorUtility.SetDirty(shape);
        return shape;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
        {
            return;
        }

        string parent = Path.GetDirectoryName(path)?.Replace("\\", "/");
        string leaf = Path.GetFileName(path);
        if (!string.IsNullOrEmpty(parent) && !string.IsNullOrEmpty(leaf))
        {
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }

    private static string SanitizeFileStem(string value)
    {
        StringBuilder builder = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
            }
            else if (c == ' ' || c == '-' || c == '_')
            {
                builder.Append('_');
            }
        }

        return builder.Length == 0 ? "Level" : builder.ToString();
    }

    private static int IndexOfLevel(SerializedProperty levelsProperty, LevelDefinition level)
    {
        for (int i = 0; i < levelsProperty.arraySize; i++)
        {
            if (levelsProperty.GetArrayElementAtIndex(i).objectReferenceValue == level)
            {
                return i;
            }
        }

        return -1;
    }

    private static void DeleteObjectListElement(SerializedProperty list, int index)
    {
        list.GetArrayElementAtIndex(index).objectReferenceValue = null;
        list.DeleteArrayElementAtIndex(index);
        if (index < list.arraySize && list.GetArrayElementAtIndex(index).objectReferenceValue == null)
        {
            list.DeleteArrayElementAtIndex(index);
        }
    }

    private void DisposeShapeEditor()
    {
        if (shapeDefinitionEditor == null)
        {
            return;
        }

        DestroyImmediate(shapeDefinitionEditor);
        shapeDefinitionEditor = null;
    }
}
