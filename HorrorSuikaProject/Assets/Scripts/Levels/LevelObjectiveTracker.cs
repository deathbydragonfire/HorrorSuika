using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Evaluates the active level's objectives and announces progress and completion. Polls in
/// FixedUpdate like <see cref="GameOverWatcher"/>. Completion latches until a merge during the
/// victory wait drops an objective, which revokes it so play can continue.
/// </summary>
public class LevelObjectiveTracker : MonoBehaviour
{
    /// <summary>Snapshot of one objective's state, as the HUD consumes it.</summary>
    public readonly struct ObjectiveProgress
    {
        public readonly int Index;
        public readonly int Current;
        public readonly int Required;
        public readonly bool IsComplete;
        public readonly string Label;
        public readonly bool IsOnOne;
        public readonly string[] Parts;
        public readonly bool[] PartMet;

        public ObjectiveProgress(
            int index,
            int current,
            int required,
            bool isComplete,
            string label,
            bool isOnOne,
            string[] parts,
            bool[] partMet)
        {
            Index = index;
            Current = current;
            Required = required;
            IsComplete = isComplete;
            Label = label;
            IsOnOne = isOnOne;
            Parts = parts;
            PartMet = partMet;
        }
    }

    private readonly List<ObjectiveProgress> progress = new List<ObjectiveProgress>();
    private readonly List<int> cumulativeCountsByTier = new List<int>();
    private readonly List<int> cumulativeHairyCountsByTier = new List<int>();
    private readonly List<int> sameSphereCounts = new List<int>();
    private readonly HashSet<int> reportedBadTierIndices = new HashSet<int>();
    private readonly HashSet<int> reportedBadDecorationIndices = new HashSet<int>();
    private readonly List<bool> scratchMarks = new List<bool>();
    private readonly List<bool> bestMarks = new List<bool>();
    private bool reportedEmptyDecorationObjective;

    private MergeItemPool itemPool;
    private MergeCoordinator mergeCoordinator;
    private ItemDropper itemDropper;
    private ScoreController scoreController;

    private LevelDefinition level;
    private MergeItemTierTable tierTable;

    private bool isActive;
    private bool hasCompleted;

    /// <summary>Raised for each objective whose current value or completion state changed.</summary>
    public event Action<ObjectiveProgress> ProgressChanged;

    /// <summary>Raised exactly once, when every objective is complete at the same time.</summary>
    public event Action AllObjectivesComplete;

    /// <summary>Current state of every objective, in authoring order.</summary>
    public IReadOnlyList<ObjectiveProgress> Progress => progress;

    /// <summary>Injects the systems the objectives are measured against.</summary>
    public void Configure(MergeItemPool pool, MergeCoordinator coordinator, ItemDropper dropper, ScoreController score)
    {
        Unsubscribe();

        itemPool = pool;
        mergeCoordinator = coordinator;
        itemDropper = dropper;
        scoreController = score;

        if (mergeCoordinator != null)
        {
            mergeCoordinator.MergePerformed += OnMergePerformed;
        }

        if (itemDropper != null)
        {
            itemDropper.ItemDropped += OnItemDropped;
        }
    }

    /// <summary>Rebuilds the progress list for a level and clears every cumulative counter.</summary>
    public void SetLevel(LevelDefinition activeLevel, MergeItemTierTable table)
    {
        level = activeLevel;
        tierTable = table;
        isActive = false;
        hasCompleted = false;
        reportedBadTierIndices.Clear();
        reportedBadDecorationIndices.Clear();
        reportedEmptyDecorationObjective = false;

        int tierCount = table != null ? table.MaxTierIndex + 1 : 0;
        cumulativeCountsByTier.Clear();
        cumulativeHairyCountsByTier.Clear();
        for (int i = 0; i < tierCount; i++)
        {
            cumulativeCountsByTier.Add(0);
            cumulativeHairyCountsByTier.Add(0);
        }

        sameSphereCounts.Clear();
        progress.Clear();
        IReadOnlyList<LevelObjective> objectives = level != null ? level.Objectives : null;
        int objectiveCount = objectives != null ? objectives.Count : 0;
        for (int i = 0; i < objectiveCount; i++)
        {
            LevelObjective objective = objectives[i];
            int required = objective != null ? objective.RequiredCount : 1;
            string label = objective != null ? objective.BuildTicketCategory(table) : "(missing objective)";
            bool isOnOne = objective != null && objective.IsOnOneGoal;
            string[] parts = objective != null ? objective.BuildOnOneParts(table) : null;
            progress.Add(new ObjectiveProgress(i, 0, required, false, label, isOnOne, parts, null));
            sameSphereCounts.Add(0);
            ProgressChanged?.Invoke(progress[i]);
        }
    }

    /// <summary>Starts or stops evaluating. Never clears the completion latch.</summary>
    public void SetActive(bool active)
    {
        isActive = active;
    }

    /// <summary>True when every objective currently holds, including during a pending victory.</summary>
    public bool AreObjectivesSatisfied()
    {
        if (level == null || progress.Count == 0)
        {
            return false;
        }

        IReadOnlyList<LevelObjective> objectives = level.Objectives;
        for (int i = 0; i < progress.Count; i++)
        {
            LevelObjective objective = objectives != null && i < objectives.Count ? objectives[i] : null;
            int current = EvaluateObjective(objective, i);
            if (objective == null || current < progress[i].Required)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Drops a latched victory so play can continue after a merge changed the board.</summary>
    public void RevokeCompletion()
    {
        hasCompleted = false;
        isActive = true;
    }

    private void OnDestroy()
    {
        Unsubscribe();
    }

    private void Unsubscribe()
    {
        if (mergeCoordinator != null)
        {
            mergeCoordinator.MergePerformed -= OnMergePerformed;
        }

        if (itemDropper != null)
        {
            itemDropper.ItemDropped -= OnItemDropped;
        }
    }

    private void OnMergePerformed(MergeItem result, int resultTierIndex, Vector3 position, int awardedScore)
    {
        AddCumulative(result);
        CountSameSphereMerge(result);
    }

    private void OnItemDropped(MergeItem item)
    {
        // Directly-dropped low tiers never pass through MergePerformed, so a cumulative objective on
        // tier 0 or 1 would otherwise never advance.
        AddCumulative(item);
    }

    private void AddCumulative(MergeItem item)
    {
        if (item == null)
        {
            return;
        }

        int tierIndex = item.TierIndex;
        if (tierIndex < 0 || tierIndex >= cumulativeCountsByTier.Count)
        {
            return;
        }

        cumulativeCountsByTier[tierIndex]++;
        if (item.IsHairy)
        {
            cumulativeHairyCountsByTier[tierIndex]++;
        }
    }

    private void FixedUpdate()
    {
        if (!isActive || level == null || progress.Count == 0)
        {
            return;
        }

        IReadOnlyList<LevelObjective> objectives = level.Objectives;
        bool allComplete = true;

        for (int i = 0; i < progress.Count; i++)
        {
            LevelObjective objective = i < objectives.Count ? objectives[i] : null;
            int current = EvaluateObjective(objective, i);
            int required = progress[i].Required;
            bool isComplete = objective != null && current >= required;

            if (!isComplete)
            {
                allComplete = false;
            }

            WritePartMarks(objective, isComplete);
            bool[] partMet = progress[i].PartMet;
            if (!MarksEqual(partMet, bestMarks))
            {
                partMet = bestMarks.Count > 0 ? bestMarks.ToArray() : null;
            }

            if (progress[i].Current == current && progress[i].IsComplete == isComplete && MarksEqual(progress[i].PartMet, bestMarks))
            {
                continue;
            }

            progress[i] = new ObjectiveProgress(
                i,
                current,
                required,
                isComplete,
                progress[i].Label,
                progress[i].IsOnOne,
                progress[i].Parts,
                partMet);
            ProgressChanged?.Invoke(progress[i]);
        }

        if (!allComplete)
        {
            return;
        }

        if (hasCompleted)
        {
            return;
        }

        hasCompleted = true;
        AllObjectivesComplete?.Invoke();
    }

    private int EvaluateObjective(LevelObjective objective, int index)
    {
        if (objective == null)
        {
            return 0;
        }

        if (objective.ObjectiveType == LevelObjectiveType.ScoreAtLeast)
        {
            return scoreController != null ? scoreController.Score : 0;
        }

        if (objective.ObjectiveType == LevelObjectiveType.SameSphereDecorations)
        {
            return SameSphereCount(objective, index);
        }

        int tierIndex = objective.TierIndex;
        bool anySize = tierIndex < 0;
        if (!anySize && tierIndex >= cumulativeCountsByTier.Count)
        {
            if (reportedBadTierIndices.Add(tierIndex))
            {
                Debug.LogError(
                    $"{nameof(LevelObjectiveTracker)}: objective targets tier {tierIndex}, outside the active table " +
                    $"({cumulativeCountsByTier.Count} tiers). It will stay permanently incomplete.",
                    this);
            }

            return 0;
        }

        if (objective.CountMode == ObjectiveCountMode.Cumulative)
        {
            return CountProduced(tierIndex, objective.HairRequirement);
        }

        return CountSimultaneous(tierIndex, objective.HairRequirement);
    }

    private void WritePartMarks(LevelObjective objective, bool objectiveComplete)
    {
        bestMarks.Clear();
        if (objective == null || !objective.IsOnOneGoal)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(objective.DescriptionOverride) || !HasIndividualFeatures(objective))
        {
            bestMarks.Add(objectiveComplete);
            return;
        }

        int slots = FeatureSlotCount(objective);
        int bestScore = -1;
        IReadOnlyList<MergeItem> items = itemPool != null ? itemPool.ActiveItems : null;
        MergeItem held = itemDropper != null ? itemDropper.HeldItem : null;
        int count = items != null ? items.Count : 0;
        for (int i = 0; i < count; i++)
        {
            MergeItem item = items[i];
            if (item == null || item == held || item.IsConsumed)
            {
                continue;
            }

            int score = FillItemMarks(item, objective, slots);
            if (score <= bestScore)
            {
                continue;
            }

            bestScore = score;
            bestMarks.Clear();
            for (int mark = 0; mark < scratchMarks.Count; mark++)
            {
                bestMarks.Add(scratchMarks[mark]);
            }
        }

        if (bestScore < 0)
        {
            for (int i = 0; i < slots; i++)
            {
                bestMarks.Add(false);
            }
        }
    }

    private static bool HasIndividualFeatures(LevelObjective objective)
    {
        if (objective.RequiredDecorations != null && objective.RequiredDecorations.Count > 0)
        {
            return true;
        }

        return objective.HairRequirement == ObjectiveHairRequirement.Hairy
            || objective.HairRequirement == ObjectiveHairRequirement.NotHairy;
    }

    private static int FeatureSlotCount(LevelObjective objective)
    {
        int slots = objective.RequiredDecorations != null ? objective.RequiredDecorations.Count : 0;
        if (objective.HairRequirement == ObjectiveHairRequirement.Hairy
            || objective.HairRequirement == ObjectiveHairRequirement.NotHairy)
        {
            slots++;
        }

        return Mathf.Max(1, slots);
    }

    private int FillItemMarks(MergeItem item, LevelObjective objective, int slots)
    {
        scratchMarks.Clear();
        int score = 0;
        IReadOnlyList<LevelObjectiveDecoration> required = objective.RequiredDecorations;
        int decorationCount = required != null ? required.Count : 0;
        for (int i = 0; i < decorationCount; i++)
        {
            LevelObjectiveDecoration decoration = required[i];
            int index = decoration != null ? decoration.DecorationIndex : -1;
            int needed = decoration != null ? Mathf.Max(1, decoration.RequiredCount) : 1;
            bool met = index >= 0 && item.CountDecoration(index) >= needed;
            scratchMarks.Add(met);
            if (met)
            {
                score++;
            }
        }

        if (objective.HairRequirement == ObjectiveHairRequirement.Hairy
            || objective.HairRequirement == ObjectiveHairRequirement.NotHairy)
        {
            bool met = MatchesHair(item, objective.HairRequirement);
            scratchMarks.Add(met);
            if (met)
            {
                score++;
            }
        }

        while (scratchMarks.Count < slots)
        {
            scratchMarks.Add(false);
        }

        return score;
    }

    private static bool MarksEqual(bool[] published, List<bool> next)
    {
        int publishedCount = published != null ? published.Length : 0;
        if (publishedCount != next.Count)
        {
            return false;
        }

        for (int i = 0; i < publishedCount; i++)
        {
            if (published[i] != next[i])
            {
                return false;
            }
        }

        return true;
    }

    private int CountProduced(int tierIndex, ObjectiveHairRequirement hairRequirement)
    {
        if (tierIndex < 0)
        {
            int total = 0;
            for (int i = 0; i < cumulativeCountsByTier.Count; i++)
            {
                total += CountProduced(i, hairRequirement);
            }

            return total;
        }

        int produced = cumulativeCountsByTier[tierIndex];
        if (hairRequirement == ObjectiveHairRequirement.Hairy)
        {
            return cumulativeHairyCountsByTier[tierIndex];
        }

        if (hairRequirement == ObjectiveHairRequirement.NotHairy)
        {
            return produced - cumulativeHairyCountsByTier[tierIndex];
        }

        return produced;
    }

    private int CountSimultaneous(int tierIndex, ObjectiveHairRequirement hairRequirement)
    {
        if (itemPool == null)
        {
            return 0;
        }

        MergeItem held = itemDropper != null ? itemDropper.HeldItem : null;
        IReadOnlyList<MergeItem> items = itemPool.ActiveItems;
        int count = 0;
        for (int i = 0; i < items.Count; i++)
        {
            MergeItem item = items[i];
            if (item == null || item == held || item.IsConsumed || (tierIndex >= 0 && item.TierIndex != tierIndex))
            {
                continue;
            }

            if (!MatchesHair(item, hairRequirement))
            {
                continue;
            }

            count++;
        }

        return count;
    }

    private static bool MatchesHair(MergeItem item, ObjectiveHairRequirement hairRequirement)
    {
        if (hairRequirement == ObjectiveHairRequirement.Hairy)
        {
            return item.IsHairy;
        }

        if (hairRequirement == ObjectiveHairRequirement.NotHairy)
        {
            return !item.IsHairy;
        }

        return true;
    }

    private int SameSphereCount(LevelObjective objective, int index)
    {
        IReadOnlyList<LevelObjectiveDecoration> required = objective.RequiredDecorations;
        bool hasDecorations = required != null && required.Count > 0;
        if (!hasDecorations && objective.HairRequirement == ObjectiveHairRequirement.Either)
        {
            if (!reportedEmptyDecorationObjective)
            {
                reportedEmptyDecorationObjective = true;
                Debug.LogError(
                    $"{nameof(LevelObjectiveTracker)}: a same-sphere objective lists no decorations and will stay incomplete.",
                    this);
            }

            return 0;
        }

        if (index < 0 || index >= sameSphereCounts.Count)
        {
            return 0;
        }

        return sameSphereCounts[index];
    }

    private void CountSameSphereMerge(MergeItem result)
    {
        if (!isActive || hasCompleted || result == null || level == null)
        {
            return;
        }

        IReadOnlyList<LevelObjective> objectives = level.Objectives;
        int count = objectives != null ? objectives.Count : 0;
        for (int i = 0; i < count && i < sameSphereCounts.Count; i++)
        {
            LevelObjective objective = objectives[i];
            if (objective == null || objective.ObjectiveType != LevelObjectiveType.SameSphereDecorations)
            {
                continue;
            }

            IReadOnlyList<LevelObjectiveDecoration> required = objective.RequiredDecorations;
            bool hasDecorations = required != null && required.Count > 0;
            if (!hasDecorations && objective.HairRequirement == ObjectiveHairRequirement.Either)
            {
                continue;
            }

            if (hasDecorations && !SphereHasRequiredDecorations(result, required))
            {
                continue;
            }

            if (MatchesHair(result, objective.HairRequirement))
            {
                sameSphereCounts[i]++;
            }
        }
    }

    private bool SphereHasRequiredDecorations(MergeItem item, IReadOnlyList<LevelObjectiveDecoration> required)
    {
        int decorationCount = tierTable != null && tierTable.Decorations != null ? tierTable.Decorations.Count : 0;
        for (int i = 0; i < required.Count; i++)
        {
            LevelObjectiveDecoration decoration = required[i];
            if (decoration == null)
            {
                return false;
            }

            int index = decoration.DecorationIndex;
            if (index < 0 || index >= decorationCount)
            {
                if (reportedBadDecorationIndices.Add(index))
                {
                    Debug.LogError(
                        $"{nameof(LevelObjectiveTracker)}: objective targets decoration {index}, outside the active table " +
                        $"({decorationCount} decorations). It will stay permanently incomplete.",
                        this);
                }

                return false;
            }

            if (item.CountDecoration(index) < decoration.RequiredCount)
            {
                return false;
            }
        }

        return true;
    }
}
