using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What kind of condition an objective measures.
/// </summary>
public enum LevelObjectiveType
{
    TierCount,
    ScoreAtLeast,
    SameSphereDecorations
}

/// <summary>
/// One decoration that a same-sphere objective requires on the sphere.
/// </summary>
[Serializable]
public class LevelObjectiveDecoration
{
    [Tooltip("Index into the tier table's decoration list.")]
    [SerializeField] private int decorationIndex;

    [Tooltip("How many instances of this decoration the sphere must carry.")]
    [SerializeField] private int requiredCount = 1;

    /// <summary>Index into the tier table's decoration list.</summary>
    public int DecorationIndex => decorationIndex;

    /// <summary>How many instances of this decoration the sphere must carry.</summary>
    public int RequiredCount => requiredCount;

    /// <summary>Clamps authored values so the asset stays editable instead of throwing at runtime.</summary>
    public void ClampValues(int maxDecorationIndex)
    {
        decorationIndex = Mathf.Clamp(decorationIndex, 0, Mathf.Max(0, maxDecorationIndex));
        requiredCount = Mathf.Max(1, requiredCount);
    }
}

/// <summary>
/// Whether a tier-count objective measures items coexisting on the board or items ever produced.
/// </summary>
public enum ObjectiveCountMode
{
    Simultaneous,
    Cumulative
}

/// <summary>
/// Serializable description of one victory condition. Deliberately flat data rather than a
/// <c>[SerializeReference]</c> polymorphic graph so every level asset stays authorable from scripts;
/// extensibility lives on the evaluation side, in <see cref="LevelObjectiveTracker"/>.
/// </summary>
[Serializable]
public class LevelObjective
{
    [Tooltip("Which condition this objective measures.")]
    [SerializeField] private LevelObjectiveType objectiveType = LevelObjectiveType.TierCount;

    [Tooltip("Count items coexisting on the board right now, or every item of that tier ever produced.")]
    [SerializeField] private ObjectiveCountMode countMode = ObjectiveCountMode.Cumulative;

    [Tooltip("Tier the objective counts. Ignored for ScoreAtLeast.")]
    [SerializeField] private int tierIndex = 2;

    [Tooltip("How many items, points, or matching spheres are required.")]
    [SerializeField] private int requiredCount = 1;

    [Tooltip("Decorations that must all be present on one sphere. Used by SameSphereDecorations.")]
    [SerializeField] private List<LevelObjectiveDecoration> requiredDecorations = new List<LevelObjectiveDecoration>();

    [Tooltip("Optional HUD text replacing the generated label.")]
    [SerializeField] private string descriptionOverride = string.Empty;

    /// <summary>Which condition this objective measures.</summary>
    public LevelObjectiveType ObjectiveType => objectiveType;

    /// <summary>Simultaneous or cumulative counting. Meaningless for <see cref="LevelObjectiveType.ScoreAtLeast"/>.</summary>
    public ObjectiveCountMode CountMode => countMode;

    /// <summary>Tier the objective counts.</summary>
    public int TierIndex => tierIndex;

    /// <summary>Required item count, score, or number of matching spheres.</summary>
    public int RequiredCount => requiredCount;

    /// <summary>Decorations that must share one sphere. Empty unless the type is <see cref="LevelObjectiveType.SameSphereDecorations"/>.</summary>
    public IReadOnlyList<LevelObjectiveDecoration> RequiredDecorations => requiredDecorations;

    /// <summary>Optional HUD text replacing the generated label.</summary>
    public string DescriptionOverride => descriptionOverride;

    /// <summary>Clamps authored values so the asset stays editable instead of throwing at runtime.</summary>
    public void ClampValues(int maxTierIndex, int maxDecorationIndex)
    {
        tierIndex = Mathf.Clamp(tierIndex, 0, Mathf.Max(0, maxTierIndex));
        requiredCount = Mathf.Max(1, requiredCount);

        if (requiredDecorations == null)
        {
            requiredDecorations = new List<LevelObjectiveDecoration>();
            return;
        }

        for (int i = 0; i < requiredDecorations.Count; i++)
        {
            requiredDecorations[i]?.ClampValues(maxDecorationIndex);
        }
    }

    /// <summary>Builds the HUD label for this objective, preferring the authored override.</summary>
    public string BuildLabel(MergeItemTierTable table)
    {
        if (!string.IsNullOrWhiteSpace(descriptionOverride))
        {
            return descriptionOverride;
        }

        if (objectiveType == LevelObjectiveType.ScoreAtLeast)
        {
            // Score is inherently cumulative, so the count mode is never printed for it.
            return $"Score {requiredCount}";
        }

        if (objectiveType == LevelObjectiveType.SameSphereDecorations)
        {
            return BuildSameSphereLabel(table);
        }

        MergeItemTier tier = table != null ? table.GetTier(tierIndex) : null;
        string tierName = tier != null ? tier.DisplayName : $"Tier {tierIndex + 1:00}";
        string modeSuffix = countMode == ObjectiveCountMode.Simultaneous ? " at once" : string.Empty;
        return $"{requiredCount}x {tierName}{modeSuffix}";
    }

    private string BuildSameSphereLabel(MergeItemTierTable table)
    {
        if (requiredDecorations == null || requiredDecorations.Count == 0)
        {
            return "Decorations on one sphere";
        }

        string features = string.Empty;
        for (int i = 0; i < requiredDecorations.Count; i++)
        {
            LevelObjectiveDecoration decoration = requiredDecorations[i];
            if (features.Length > 0)
            {
                features += ", ";
            }

            int index = decoration != null ? decoration.DecorationIndex : -1;
            int count = decoration != null ? Mathf.Max(1, decoration.RequiredCount) : 1;
            string name = ResolveDecorationName(table, index);
            features += count > 1 ? $"{count}x {name}" : name;
        }

        if (requiredCount <= 1)
        {
            return $"{features} merged onto one sphere";
        }

        return $"{requiredCount} merges with {features} on the result";
    }

    private static string ResolveDecorationName(MergeItemTierTable table, int decorationIndex)
    {
        if (table == null || decorationIndex < 0 || decorationIndex >= table.Decorations.Count)
        {
            return $"Decoration {decorationIndex + 1}";
        }

        MergeItemDecorationDefinition decoration = table.Decorations[decorationIndex];
        if (decoration == null || string.IsNullOrWhiteSpace(decoration.DisplayName))
        {
            return $"Decoration {decorationIndex + 1}";
        }

        return decoration.DisplayName;
    }
}
