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
/// Whether an objective cares that a blob is hairy. Hair is binary: a coat is present, or the blob is bare.
/// </summary>
public enum ObjectiveHairRequirement
{
    Either,
    Hairy,
    NotHairy
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

    [Tooltip("Tier the objective counts. -1 counts every size. Ignored for ScoreAtLeast and SameSphereDecorations.")]
    [SerializeField] private int tierIndex = 2;

    [Tooltip("How many items, points, or matching spheres are required.")]
    [SerializeField] private int requiredCount = 1;

    [Tooltip("Decorations that must all be present on one sphere. Used by SameSphereDecorations.")]
    [SerializeField] private List<LevelObjectiveDecoration> requiredDecorations = new List<LevelObjectiveDecoration>();

    [Tooltip("Require the counted blob to be hairy, bare, or either. Ignored for ScoreAtLeast.")]
    [SerializeField] private ObjectiveHairRequirement hairRequirement = ObjectiveHairRequirement.Either;

    [Tooltip("Optional HUD text replacing the generated label.")]
    [SerializeField] private string descriptionOverride = string.Empty;

    /// <summary>Which condition this objective measures.</summary>
    public LevelObjectiveType ObjectiveType => objectiveType;

    /// <summary>True when every listed feature has to share one sphere, rather than a board-wide count.</summary>
    public bool IsOnOneGoal => objectiveType == LevelObjectiveType.SameSphereDecorations;

    /// <summary>Simultaneous or cumulative counting. Meaningless for <see cref="LevelObjectiveType.ScoreAtLeast"/>.</summary>
    public ObjectiveCountMode CountMode => countMode;

    /// <summary>Tier the objective counts.</summary>
    public int TierIndex => tierIndex;

    /// <summary>Required item count, score, or number of matching spheres.</summary>
    public int RequiredCount => requiredCount;

    /// <summary>Decorations that must share one sphere. Empty unless the type is <see cref="LevelObjectiveType.SameSphereDecorations"/>.</summary>
    public IReadOnlyList<LevelObjectiveDecoration> RequiredDecorations => requiredDecorations;

    /// <summary>Whether counted blobs must be hairy, bare, or either. Ignored for score objectives.</summary>
    public ObjectiveHairRequirement HairRequirement => hairRequirement;

    /// <summary>Optional HUD text replacing the generated label.</summary>
    public string DescriptionOverride => descriptionOverride;

    /// <summary>Clamps authored values so the asset stays editable instead of throwing at runtime.</summary>
    public void ClampValues(int maxTierIndex, int maxDecorationIndex)
    {
        tierIndex = tierIndex < 0 ? -1 : Mathf.Clamp(tierIndex, 0, Mathf.Max(0, maxTierIndex));
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

    /// <summary>Feature names that must share one sphere. Empty for board-wide counts.</summary>
    public string[] BuildOnOneParts(MergeItemTierTable table)
    {
        if (!IsOnOneGoal)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(descriptionOverride))
        {
            return new[] { descriptionOverride.Trim() };
        }

        var parts = new List<string>();
        if (requiredDecorations != null)
        {
            for (int i = 0; i < requiredDecorations.Count; i++)
            {
                LevelObjectiveDecoration decoration = requiredDecorations[i];
                int index = decoration != null ? decoration.DecorationIndex : -1;
                int count = decoration != null ? Mathf.Max(1, decoration.RequiredCount) : 1;
                string name = ResolveDecorationName(table, index);
                parts.Add(count > 1 ? $"{count} {name}" : name);
            }
        }

        if (hairRequirement == ObjectiveHairRequirement.Hairy)
        {
            parts.Add("Hairy");
        }
        else if (hairRequirement == ObjectiveHairRequirement.NotHairy)
        {
            parts.Add("Bare");
        }

        if (parts.Count == 0)
        {
            parts.Add("Decorations");
        }

        return parts.ToArray();
    }

    /// <summary>
    /// Short ticket category. The required count is drawn separately as the order quantity.
    /// </summary>
    public string BuildTicketCategory(MergeItemTierTable table)
    {
        if (!string.IsNullOrWhiteSpace(descriptionOverride))
        {
            return descriptionOverride.Trim();
        }

        if (objectiveType == LevelObjectiveType.ScoreAtLeast)
        {
            return "Score";
        }

        if (objectiveType == LevelObjectiveType.SameSphereDecorations)
        {
            return BuildSameSphereLabel(table);
        }

        if (tierIndex < 0)
        {
            string subject = hairRequirement == ObjectiveHairRequirement.Hairy
                ? "Hairy"
                : hairRequirement == ObjectiveHairRequirement.NotHairy ? "Bare" : "Any";
            return subject;
        }

        MergeItemTier tier = table != null ? table.GetTier(tierIndex) : null;
        string tierName = tier != null ? tier.DisplayName : $"Tier {tierIndex + 1:00}";
        return tierName + HairLabelSuffix();
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

        if (tierIndex < 0)
        {
            string subject = hairRequirement == ObjectiveHairRequirement.Hairy
                ? "hairy"
                : hairRequirement == ObjectiveHairRequirement.NotHairy ? "not hairy" : "any size";
            return $"{requiredCount}x {subject}";
        }

        MergeItemTier tier = table != null ? table.GetTier(tierIndex) : null;
        string tierName = tier != null ? tier.DisplayName : $"Tier {tierIndex + 1:00}";
        return $"{requiredCount}x {tierName}{HairLabelSuffix()}";
    }

    private string HairLabelSuffix()
    {
        if (hairRequirement == ObjectiveHairRequirement.Hairy)
        {
            return ", hairy";
        }

        if (hairRequirement == ObjectiveHairRequirement.NotHairy)
        {
            return ", not hairy";
        }

        return string.Empty;
    }

    private string BuildSameSphereLabel(MergeItemTierTable table)
    {
        string features = string.Empty;
        if (requiredDecorations != null)
        {
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
        }

        if (hairRequirement == ObjectiveHairRequirement.Hairy || hairRequirement == ObjectiveHairRequirement.NotHairy)
        {
            if (features.Length > 0)
            {
                features += ", ";
            }

            features += hairRequirement == ObjectiveHairRequirement.Hairy ? "hairy" : "not hairy";
        }

        if (features.Length == 0)
        {
            return "Decorations on one sphere";
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
