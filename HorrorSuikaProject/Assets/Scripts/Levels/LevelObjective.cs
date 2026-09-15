using System;
using UnityEngine;

/// <summary>
/// What kind of condition an objective measures.
/// </summary>
public enum LevelObjectiveType
{
    TierCount,
    ScoreAtLeast
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

    [Tooltip("How many items (or how many points, for ScoreAtLeast) are required.")]
    [SerializeField] private int requiredCount = 1;

    [Tooltip("Optional HUD text replacing the generated label.")]
    [SerializeField] private string descriptionOverride = string.Empty;

    /// <summary>Which condition this objective measures.</summary>
    public LevelObjectiveType ObjectiveType => objectiveType;

    /// <summary>Simultaneous or cumulative counting. Meaningless for <see cref="LevelObjectiveType.ScoreAtLeast"/>.</summary>
    public ObjectiveCountMode CountMode => countMode;

    /// <summary>Tier the objective counts.</summary>
    public int TierIndex => tierIndex;

    /// <summary>Required item count, or required score.</summary>
    public int RequiredCount => requiredCount;

    /// <summary>Optional HUD text replacing the generated label.</summary>
    public string DescriptionOverride => descriptionOverride;

    /// <summary>Clamps authored values so the asset stays editable instead of throwing at runtime.</summary>
    public void ClampValues(int maxTierIndex)
    {
        tierIndex = Mathf.Clamp(tierIndex, 0, Mathf.Max(0, maxTierIndex));
        requiredCount = Mathf.Max(1, requiredCount);
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

        MergeItemTier tier = table != null ? table.GetTier(tierIndex) : null;
        string tierName = tier != null ? tier.DisplayName : $"Tier {tierIndex + 1:00}";
        string modeSuffix = countMode == ObjectiveCountMode.Simultaneous ? " at once" : string.Empty;
        return $"{requiredCount}x {tierName}{modeSuffix}";
    }
}
