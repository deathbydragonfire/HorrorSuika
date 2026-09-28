using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

/// <summary>
/// Level authoring inspector: switch levels, save and load their curves and goals, draw the
/// playfield in the Scene view, and manage sequence membership.
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
    private LevelDefinition loadedLevel;
    private bool pendingGeometryInstall;
    private string renameText = string.Empty;
    private LevelDefinition renameSource;

    private void OnEnable()
    {
        levelProperty = serializedObject.FindProperty("level");
        sequenceProperty = serializedObject.FindProperty("sequence");
        shapeProperty = serializedObject.FindProperty("shapeDefinition");
        mirrorProperty = serializedObject.FindProperty("mirrorX");
        bakeTargetProperty = serializedObject.FindProperty("bakeTargetPrefab");
        hostProperty = serializedObject.FindProperty("shapeHost");
        tierTableProperty = serializedObject.FindProperty("tierTable");
        loadedLevel = levelProperty.objectReferenceValue as LevelDefinition;
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

        DrawLevelSwitcher(author);
        EditorGUILayout.Space();
        DrawCreateSection(author);
        EditorGUILayout.Space();
        DrawLevelReferences();
        SyncShapeFromLevel();
        serializedObject.ApplyModifiedProperties();
        if (pendingGeometryInstall)
        {
            pendingGeometryInstall = false;
            InstallGeometry(author, author.Level != null ? author.Level.ShapePrefab : null);
        }

        LevelDefinition level = author.Level;
        if (level == null)
        {
            EditorGUILayout.HelpBox("Create a level or assign an existing Level Definition to begin.", MessageType.Info);
            return;
        }

        BindLevelSerialized();
        levelSerialized.Update();
        DrawLevelIdentityFields();

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

    private void DrawLevelSwitcher(LevelAuthor author)
    {
        EditorGUILayout.LabelField("Level", EditorStyles.boldLabel);

        List<LevelDefinition> levels = CollectSwitchableLevels(author.Sequence);
        LevelDefinition shown = levelProperty.objectReferenceValue as LevelDefinition;
        int current = shown != null ? levels.IndexOf(shown) : -1;

        if (levels.Count == 0)
        {
            EditorGUILayout.HelpBox("No level assets found yet. Create one below.", MessageType.Info);
        }
        else
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(current <= 0))
                {
                    if (GUILayout.Button("Previous", GUILayout.Width(76f)))
                    {
                        OpenLevel(author, levels[current - 1]);
                    }
                }

                string[] labels = new string[levels.Count];
                for (int i = 0; i < levels.Count; i++)
                {
                    labels[i] = FormatLevelChoice(levels[i], levels);
                }

                if (current < 0)
                {
                    string[] unassigned = new string[labels.Length + 1];
                    unassigned[0] = shown != null ? shown.DisplayName : "Select a level";
                    System.Array.Copy(labels, 0, unassigned, 1, labels.Length);
                    int picked = EditorGUILayout.Popup(0, unassigned);
                    if (picked > 0)
                    {
                        OpenLevel(author, levels[picked - 1]);
                    }
                }
                else
                {
                    int picked = EditorGUILayout.Popup(current, labels);
                    if (picked != current && picked >= 0 && picked < levels.Count)
                    {
                        OpenLevel(author, levels[picked]);
                    }
                }

                using (new EditorGUI.DisabledScope(current < 0 || current >= levels.Count - 1))
                {
                    if (GUILayout.Button("Next", GUILayout.Width(52f)))
                    {
                        OpenLevel(author, levels[current + 1]);
                    }
                }
            }
        }

        DrawRenameRow(shown);

        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(shown == null))
            {
                if (GUILayout.Button("Save Geometry and Goals"))
                {
                    SaveCurrentLevel(author);
                }

                if (GUILayout.Button("Load Geometry and Goals"))
                {
                    LoadCurrentLevel(author);
                }
            }
        }

        EditorGUILayout.HelpBox(
            "Previous, Next, and the level list load that level's curve and goals. Save bakes the curve into the level and writes its goals to disk. Load restores the curve and goals from the last save.",
            MessageType.None);
    }

    private void DrawRenameRow(LevelDefinition shown)
    {
        if (shown == null)
        {
            return;
        }

        if (renameSource != shown)
        {
            renameSource = shown;
            renameText = shown.DisplayName ?? string.Empty;
        }

        string trimmed = renameText != null ? renameText.Trim() : string.Empty;
        bool unchanged = trimmed == shown.DisplayName;
        using (new EditorGUILayout.HorizontalScope())
        {
            GUI.SetNextControlName("LevelRename");
            renameText = EditorGUILayout.TextField(
                new GUIContent("Name", "Name shown in the level list, the HUD, and level select."),
                renameText);
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(trimmed) || unchanged))
            {
                bool submit = GUILayout.Button("Rename", GUILayout.Width(72f));
                Event current = Event.current;
                bool enter = current.type == EventType.KeyDown
                    && (current.keyCode == KeyCode.Return || current.keyCode == KeyCode.KeypadEnter)
                    && GUI.GetNameOfFocusedControl() == "LevelRename";
                if ((submit || enter) && !string.IsNullOrWhiteSpace(trimmed) && !unchanged)
                {
                    if (enter)
                    {
                        current.Use();
                    }

                    RenameLevel(shown, trimmed);
                }
            }
        }
    }

    private void RenameLevel(LevelDefinition level, string displayName)
    {
        BindLevelSerialized();
        if (levelSerialized == null || levelSerialized.targetObject != level)
        {
            levelSerialized?.Dispose();
            levelSerialized = new SerializedObject(level);
        }

        levelSerialized.Update();
        levelSerialized.FindProperty("displayName").stringValue = displayName;
        levelSerialized.ApplyModifiedProperties();
        EditorUtility.SetDirty(level);
        AssetDatabase.SaveAssets();
        renameText = displayName;
        renameSource = level;
        GUIUtility.ExitGUI();
    }

    private void DrawLevelReferences()
    {
        EditorGUILayout.PropertyField(levelProperty);
        EditorGUILayout.PropertyField(sequenceProperty);
        EditorGUILayout.PropertyField(tierTableProperty);
    }

    private void DrawLevelIdentityFields()
    {
        if (levelSerialized == null)
        {
            return;
        }

        EditorGUILayout.PropertyField(levelSerialized.FindProperty("levelId"));
        EditorGUILayout.PropertyField(levelSerialized.FindProperty("tierTable"));
    }

    private void DrawPassRequirements(LevelAuthor author)
    {
        EditorGUILayout.LabelField("Pass Requirements", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Every objective must hold at the same time to win. Cumulative counts every item of that tier ever produced. Simultaneous counts items on the board right now. Hair can restrict those counts, and a same-sphere objective, to hairy or bare blobs. Any size counts every tier, so a mission can require a number of hairy blobs no matter how large they are. Same-sphere decorations pass only when a merge leaves every listed decoration on the result. A drop cannot complete it, and later merges do not undo it.",
            MessageType.None);

        EnsureObjectiveList(author);
        objectiveList.DoLayoutList();

        if (levelSerialized.FindProperty("objectives").arraySize == 0)
        {
            bool endless = levelSerialized.FindProperty("endless").boolValue;
            EditorGUILayout.HelpBox(
                endless
                    ? "Endless level. There is no win; play continues until overflow or another failure limit."
                    : "A level with no objectives can never be won.",
                endless ? MessageType.Info : MessageType.Warning);
        }
    }

    private void DrawLimits()
    {
        EditorGUILayout.LabelField("Spawn and Failure", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(levelSerialized.FindProperty("endless"));
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
            "Drag yellow points in the Scene view. Ctrl-click a segment to insert a point. Shift-click a point to delete it. Mirror X authors the left half only. Drag the Spawn and Death handles to set those heights, then bake.",
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
                string entryName = entry != null ? entry.DisplayName : "(missing)";
                if (GUILayout.Button(new GUIContent($"{i + 1}. {entryName}", "Load this level's geometry and goals"), style))
                {
                    OpenLevel(author, entry);
                }
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
            elementHeightCallback = GetObjectiveElementHeight,
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
                added.FindPropertyRelative("hairRequirement").enumValueIndex = (int)ObjectiveHairRequirement.Either;
                added.FindPropertyRelative("requiredDecorations").ClearArray();
            }
        };
    }

    private float GetObjectiveElementHeight(int index)
    {
        float line = EditorGUIUtility.singleLineHeight + 2f;
        SerializedProperty objective = objectiveList.serializedProperty.GetArrayElementAtIndex(index);
        int type = objective.FindPropertyRelative("objectiveType").enumValueIndex;
        int rows = 6;
        if (type == (int)LevelObjectiveType.ScoreAtLeast)
        {
            rows = 5;
        }
        else if (type == (int)LevelObjectiveType.SameSphereDecorations)
        {
            int decorationCount = objective.FindPropertyRelative("requiredDecorations").arraySize;
            rows = 5 + decorationCount;
        }

        return (line * rows) + 6f;
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

        if (typeProperty.enumValueIndex == (int)LevelObjectiveType.SameSphereDecorations)
        {
            DrawSameSphereFields(author, objective, requiredProperty, overrideProperty, LineRect);
            return;
        }

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
                string[] names = new string[table.Tiers.Count + 1];
                int[] values = new int[table.Tiers.Count + 1];
                names[0] = "Any size";
                values[0] = -1;
                for (int i = 0; i < table.Tiers.Count; i++)
                {
                    MergeItemTier tier = table.Tiers[i];
                    names[i + 1] = tier != null ? $"{i}: {tier.DisplayName}" : $"{i}: (missing)";
                    values[i + 1] = i;
                }

                tierProperty.intValue = EditorGUI.IntPopup(LineRect(), "Tier", tierProperty.intValue, names, values);
            }
            else
            {
                EditorGUI.PropertyField(LineRect(), tierProperty, new GUIContent("Tier Index"));
            }

            requiredProperty.intValue = Mathf.Max(1, EditorGUI.IntField(LineRect(), "Required Count", requiredProperty.intValue));
        }

        if (!isScore)
        {
            DrawHairPopup(LineRect(), objective);
        }

        EditorGUI.PropertyField(LineRect(), overrideProperty, new GUIContent("Label Override"));
    }

    private void DrawSameSphereFields(
        LevelAuthor author,
        SerializedProperty objective,
        SerializedProperty requiredProperty,
        SerializedProperty overrideProperty,
        System.Func<Rect> lineRect)
    {
        requiredProperty.intValue = Mathf.Max(1, EditorGUI.IntField(lineRect(), "Merges Required", requiredProperty.intValue));
        DrawHairPopup(lineRect(), objective);

        SerializedProperty decorations = objective.FindPropertyRelative("requiredDecorations");
        for (int i = 0; i < decorations.arraySize; i++)
        {
            SerializedProperty entry = decorations.GetArrayElementAtIndex(i);
            Rect row = lineRect();
            const float countWidth = 48f;
            const float removeWidth = 22f;
            Rect popupRect = new Rect(row.x, row.y, row.width - countWidth - removeWidth - 8f, row.height);
            Rect countRect = new Rect(popupRect.xMax + 4f, row.y, countWidth, row.height);
            Rect removeRect = new Rect(countRect.xMax + 4f, row.y, removeWidth, row.height);

            SerializedProperty indexProperty = entry.FindPropertyRelative("decorationIndex");
            SerializedProperty countProperty = entry.FindPropertyRelative("requiredCount");
            DrawDecorationPopup(author, popupRect, indexProperty);
            countProperty.intValue = Mathf.Max(1, EditorGUI.IntField(countRect, countProperty.intValue));
            if (GUI.Button(removeRect, "x"))
            {
                decorations.DeleteArrayElementAtIndex(i);
                break;
            }
        }

        if (GUI.Button(lineRect(), "Add Decoration"))
        {
            int addedIndex = decorations.arraySize;
            decorations.arraySize++;
            SerializedProperty added = decorations.GetArrayElementAtIndex(addedIndex);
            added.FindPropertyRelative("decorationIndex").intValue = 0;
            added.FindPropertyRelative("requiredCount").intValue = 1;
        }

        EditorGUI.PropertyField(lineRect(), overrideProperty, new GUIContent("Label Override"));
    }

    private void DrawDecorationPopup(LevelAuthor author, Rect rect, SerializedProperty indexProperty)
    {
        MergeItemTierTable table = ResolveTierTable(author);
        if (table == null || table.Decorations == null || table.Decorations.Count == 0)
        {
            indexProperty.intValue = Mathf.Max(0, EditorGUI.IntField(rect, "Decoration", indexProperty.intValue));
            return;
        }

        string[] names = new string[table.Decorations.Count];
        int[] values = new int[table.Decorations.Count];
        for (int i = 0; i < table.Decorations.Count; i++)
        {
            MergeItemDecorationDefinition decoration = table.Decorations[i];
            names[i] = decoration != null ? decoration.DisplayName : "(missing)";
            values[i] = i;
        }

        indexProperty.intValue = EditorGUI.IntPopup(rect, indexProperty.intValue, names, values);
    }

    private static readonly string[] HairOptionNames = { "Either", "Hairy", "Not hairy" };

    private static void DrawHairPopup(Rect rect, SerializedProperty objective)
    {
        SerializedProperty hairProperty = objective.FindPropertyRelative("hairRequirement");
        hairProperty.enumValueIndex = EditorGUI.Popup(rect, "Hair", hairProperty.enumValueIndex, HairOptionNames);
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
        if (level == loadedLevel)
        {
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

            return;
        }

        shapeProperty.objectReferenceValue = level != null ? level.ShapeDefinition : null;
        bakeTargetProperty.objectReferenceValue = level != null ? level.ShapePrefab : null;
        loadedLevel = level;
        pendingGeometryInstall = level != null;
        DisposeShapeEditor();
        objectiveList = null;
        if (levelSerialized != null)
        {
            levelSerialized.Dispose();
            levelSerialized = null;
        }
    }

    private void OpenLevel(LevelAuthor author, LevelDefinition level)
    {
        if (level == null || level == loadedLevel)
        {
            return;
        }

        serializedObject.Update();
        levelProperty.objectReferenceValue = level;
        shapeProperty.objectReferenceValue = level.ShapeDefinition;
        bakeTargetProperty.objectReferenceValue = level.ShapePrefab;
        serializedObject.ApplyModifiedProperties();
        FinishLevelSwap(author, level);
        GUIUtility.ExitGUI();
    }

    private void SaveCurrentLevel(LevelAuthor author)
    {
        LevelDefinition level = author.Level;
        if (level == null)
        {
            return;
        }

        BindLevelSerialized();
        levelSerialized.Update();
        levelSerialized.ApplyModifiedProperties();

        PlayfieldShapeDefinition shape = author.ShapeDefinition;
        GameObject baked = null;
        if (shape != null)
        {
            baked = PlayfieldShapeAuthoringGui.Bake(shape, author.BakeTargetPrefab);
            EditorUtility.SetDirty(shape);
        }

        levelSerialized.Update();
        levelSerialized.FindProperty("shapeDefinition").objectReferenceValue = shape;
        if (baked != null)
        {
            levelSerialized.FindProperty("shapePrefab").objectReferenceValue = baked;
        }

        levelSerialized.ApplyModifiedProperties();
        EditorUtility.SetDirty(level);

        serializedObject.Update();
        shapeProperty.objectReferenceValue = shape;
        if (baked != null)
        {
            bakeTargetProperty.objectReferenceValue = baked;
        }

        serializedObject.ApplyModifiedProperties();
        loadedLevel = level;
        AssetDatabase.SaveAssets();

        if (baked != null)
        {
            InstallGeometry(author, baked);
        }
        else if (shape != null)
        {
            Debug.LogWarning($"{level.DisplayName}: goals were saved. The curve has errors, so the playfield geometry was not baked.", level);
        }

        GUIUtility.ExitGUI();
    }

    private void LoadCurrentLevel(LevelAuthor author)
    {
        LevelDefinition level = author.Level;
        if (level == null)
        {
            return;
        }

        if (levelSerialized != null)
        {
            levelSerialized.Dispose();
            levelSerialized = null;
        }

        objectiveList = null;
        RevertAssetToSaved(level);
        RevertAssetToSaved(level.ShapeDefinition);

        serializedObject.Update();
        levelProperty.objectReferenceValue = level;
        shapeProperty.objectReferenceValue = level.ShapeDefinition;
        bakeTargetProperty.objectReferenceValue = level.ShapePrefab;
        serializedObject.ApplyModifiedProperties();
        FinishLevelSwap(author, level);
        GUIUtility.ExitGUI();
    }

    private void FinishLevelSwap(LevelAuthor author, LevelDefinition level)
    {
        loadedLevel = level;
        pendingGeometryInstall = false;
        renameSource = null;
        DisposeShapeEditor();
        objectiveList = null;
        if (levelSerialized != null)
        {
            levelSerialized.Dispose();
            levelSerialized = null;
        }

        BindLevelSerialized();
        InstallGeometry(author, level != null ? level.ShapePrefab : null);
    }

    private static void InstallGeometry(LevelAuthor author, GameObject prefab)
    {
        if (prefab != null && author.ShapeHost != null)
        {
            PlayfieldShapeAuthoringGui.Install(prefab, author.ShapeHost, author);
        }

        SceneView.RepaintAll();
    }

    private static void RevertAssetToSaved(UnityEngine.Object asset)
    {
        if (asset == null)
        {
            return;
        }

        string path = AssetDatabase.GetAssetPath(asset);
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return;
        }

        string directory = Path.GetDirectoryName(path)?.Replace("\\", "/");
        string tempPath = $"{directory}/__level_reload_tmp{Path.GetExtension(path)}";
        if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(tempPath) != null)
        {
            AssetDatabase.DeleteAsset(tempPath);
        }

        File.Copy(path, tempPath, true);
        try
        {
            AssetDatabase.ImportAsset(tempPath, ImportAssetOptions.ForceSynchronousImport);
            UnityEngine.Object fresh = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(tempPath);
            if (fresh == null)
            {
                return;
            }

            Undo.RecordObject(asset, "Load Level Geometry and Goals");
            EditorUtility.CopySerialized(fresh, asset);
            EditorUtility.ClearDirty(asset);
        }
        finally
        {
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(tempPath) != null)
            {
                AssetDatabase.DeleteAsset(tempPath);
            }
            else if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
                string metaPath = tempPath + ".meta";
                if (File.Exists(metaPath))
                {
                    File.Delete(metaPath);
                }
            }
        }
    }

    private static List<LevelDefinition> CollectSwitchableLevels(LevelSequence sequence)
    {
        List<LevelDefinition> levels = new List<LevelDefinition>();
        if (sequence != null)
        {
            for (int i = 0; i < sequence.Count; i++)
            {
                LevelDefinition level = sequence.GetLevel(i);
                if (level != null && !levels.Contains(level))
                {
                    levels.Add(level);
                }
            }
        }

        string[] guids = AssetDatabase.FindAssets("t:LevelDefinition");
        List<LevelDefinition> extras = new List<LevelDefinition>();
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            LevelDefinition level = AssetDatabase.LoadAssetAtPath<LevelDefinition>(path);
            if (level != null && !levels.Contains(level))
            {
                extras.Add(level);
            }
        }

        extras.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, System.StringComparison.OrdinalIgnoreCase));
        levels.AddRange(extras);
        return levels;
    }

    private static string FormatLevelChoice(LevelDefinition level, IReadOnlyList<LevelDefinition> levels)
    {
        if (level == null)
        {
            return "(missing)";
        }

        string display = string.IsNullOrEmpty(level.DisplayName) ? level.name : level.DisplayName;
        int shares = 0;
        for (int i = 0; i < levels.Count; i++)
        {
            LevelDefinition other = levels[i];
            if (other != null && other.DisplayName == level.DisplayName)
            {
                shares++;
            }
        }

        if (shares > 1)
        {
            display += $" ({level.name})";
        }

        return display;
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
        created.FindProperty("victorySettleTimeout").floatValue = 5f;
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
            destLevel.name = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(destLevel));
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
