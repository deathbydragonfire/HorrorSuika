using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Fills the level-info ticket with the current level's goal lines while the editor is not playing,
/// so the portrait and landscape preview shows the same words the HUD uses in play.
/// </summary>
[InitializeOnLoad]
public static class LevelInfoGoalPreview
{
    private const double RefreshIntervalSeconds = 0.25;

    private static int appliedSignature = int.MinValue;
    private static int appliedCount = -1;
    private static double nextRefreshTime;

    static LevelInfoGoalPreview()
    {
        EditorApplication.update += Refresh;
        EditorSceneManager.sceneSaving += OnSceneSaving;
    }

    private static void Refresh()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            return;
        }

        if (EditorApplication.timeSinceStartup < nextRefreshTime && appliedSignature != int.MinValue)
        {
            return;
        }

        nextRefreshTime = EditorApplication.timeSinceStartup + RefreshIntervalSeconds;

        GameHudView hud = Object.FindFirstObjectByType<GameHudView>(FindObjectsInactive.Include);
        if (hud == null)
        {
            return;
        }

        ResolvePreviewLevel(out LevelDefinition level, out MergeItemTierTable table);
        int signature = BuildSignature(level, table);
        int expected = CountObjectives(level);
        if (signature == appliedSignature && hud.EditorGoalPreviewCount() == appliedCount && appliedCount == expected)
        {
            return;
        }

        hud.ShowEditorGoalPreview(level, table);
        LevelInfoPanelLayout layout = Object.FindFirstObjectByType<LevelInfoPanelLayout>(FindObjectsInactive.Include);
        if (layout != null)
        {
            layout.Apply();
        }

        Canvas.ForceUpdateCanvases();
        appliedSignature = signature;
        appliedCount = expected;
        EditorApplication.QueuePlayerLoopUpdate();
    }

    private static void OnSceneSaving(UnityEngine.SceneManagement.Scene scene, string path)
    {
        GameHudView hud = Object.FindFirstObjectByType<GameHudView>(FindObjectsInactive.Include);
        if (hud != null)
        {
            hud.ClearEditorGoalPreview();
        }

        appliedSignature = int.MinValue;
        appliedCount = -1;
    }

    private static void ResolvePreviewLevel(out LevelDefinition level, out MergeItemTierTable table)
    {
        level = null;
        table = null;

        LevelAuthor author = Object.FindFirstObjectByType<LevelAuthor>(FindObjectsInactive.Include);
        if (author != null)
        {
            level = author.Level;
            table = author.TierTable;
        }

        if (level != null && level.TierTableOverride != null)
        {
            table = level.TierTableOverride;
        }

        if (level != null)
        {
            return;
        }

        GameController controller = Object.FindFirstObjectByType<GameController>(FindObjectsInactive.Include);
        if (controller == null)
        {
            return;
        }

        SerializedObject serialized = new SerializedObject(controller);
        level = serialized.FindProperty("startingLevel").objectReferenceValue as LevelDefinition;
        table = serialized.FindProperty("tierTable").objectReferenceValue as MergeItemTierTable;
        if (level != null && level.TierTableOverride != null)
        {
            table = level.TierTableOverride;
        }
    }

    private static int CountObjectives(LevelDefinition level)
    {
        if (level == null || level.Objectives == null)
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < level.Objectives.Count; i++)
        {
            if (level.Objectives[i] != null)
            {
                count++;
            }
        }

        return count;
    }

    private static int BuildSignature(LevelDefinition level, MergeItemTierTable table)
    {
        unchecked
        {
            int hash = 17;
            hash = (hash * 31) + (level != null ? level.GetInstanceID() : 0);
            hash = (hash * 31) + (table != null ? table.GetInstanceID() : 0);
            System.Collections.Generic.IReadOnlyList<LevelObjective> objectives = level != null ? level.Objectives : null;
            int count = objectives != null ? objectives.Count : 0;
            hash = (hash * 31) + count;
            for (int i = 0; i < count; i++)
            {
                LevelObjective objective = objectives[i];
                if (objective == null)
                {
                    hash = (hash * 31) + 1;
                    continue;
                }

                hash = (hash * 31) + (int)objective.ObjectiveType;
                hash = (hash * 31) + (int)objective.CountMode;
                hash = (hash * 31) + objective.RequiredCount;
                hash = (hash * 31) + objective.TierIndex;
                hash = (hash * 31) + (int)objective.HairRequirement;
                hash = (hash * 31) + Stable(objective.BuildTicketCategory(table));
                string[] parts = objective.BuildOnOneParts(table);
                if (parts == null)
                {
                    continue;
                }

                hash = (hash * 31) + parts.Length;
                for (int p = 0; p < parts.Length; p++)
                {
                    hash = (hash * 31) + Stable(parts[p]);
                }
            }

            return hash;
        }
    }

    private static int Stable(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return 0;
        }

        unchecked
        {
            int hash = 23;
            for (int i = 0; i < value.Length; i++)
            {
                hash = (hash * 31) + value[i];
            }

            return hash;
        }
    }
}
