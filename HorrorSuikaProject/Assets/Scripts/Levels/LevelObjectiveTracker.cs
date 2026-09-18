using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Evaluates the active level's objectives and announces progress and completion. Polls in
/// FixedUpdate like <see cref="GameOverWatcher"/> and latches completion so a win cannot be lost
/// once every condition has held simultaneously for one step.
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

        public ObjectiveProgress(int index, int current, int required, bool isComplete, string label)
        {
            Index = index;
            Current = current;
            Required = required;
            IsComplete = isComplete;
            Label = label;
        }
    }

    private readonly List<ObjectiveProgress> progress = new List<ObjectiveProgress>();
    private readonly List<int> cumulativeCountsByTier = new List<int>();
    private readonly HashSet<int> reportedBadTierIndices = new HashSet<int>();

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

        int tierCount = table != null ? table.MaxTierIndex + 1 : 0;
        cumulativeCountsByTier.Clear();
        for (int i = 0; i < tierCount; i++)
        {
            cumulativeCountsByTier.Add(0);
        }

        progress.Clear();
        IReadOnlyList<LevelObjective> objectives = level != null ? level.Objectives : null;
        int objectiveCount = objectives != null ? objectives.Count : 0;
        for (int i = 0; i < objectiveCount; i++)
        {
            LevelObjective objective = objectives[i];
            int required = objective != null ? objective.RequiredCount : 1;
            string label = objective != null ? objective.BuildLabel(table) : "(missing objective)";
            progress.Add(new ObjectiveProgress(i, 0, required, false, label));
            ProgressChanged?.Invoke(progress[i]);
        }
    }

    /// <summary>Starts or stops evaluating. Never clears the completion latch.</summary>
    public void SetActive(bool active)
    {
        isActive = active;
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

    private void OnMergePerformed(int resultTierIndex, Vector3 position, int awardedScore)
    {
        AddCumulative(resultTierIndex);
    }

    private void OnItemDropped(MergeItem item)
    {
        // Directly-dropped low tiers never pass through MergePerformed, so a cumulative objective on
        // tier 0 or 1 would otherwise never advance.
        if (item != null)
        {
            AddCumulative(item.TierIndex);
        }
    }

    private void AddCumulative(int tierIndex)
    {
        if (tierIndex < 0 || tierIndex >= cumulativeCountsByTier.Count)
        {
            return;
        }

        cumulativeCountsByTier[tierIndex]++;
    }

    private void FixedUpdate()
    {
        if (!isActive || hasCompleted || level == null || progress.Count == 0)
        {
            return;
        }

        IReadOnlyList<LevelObjective> objectives = level.Objectives;
        bool allComplete = true;

        for (int i = 0; i < progress.Count; i++)
        {
            LevelObjective objective = i < objectives.Count ? objectives[i] : null;
            int current = EvaluateObjective(objective);
            int required = progress[i].Required;
            bool isComplete = objective != null && current >= required;

            if (!isComplete)
            {
                allComplete = false;
            }

            if (progress[i].Current == current && progress[i].IsComplete == isComplete)
            {
                continue;
            }

            progress[i] = new ObjectiveProgress(i, current, required, isComplete, progress[i].Label);
            ProgressChanged?.Invoke(progress[i]);
        }

        if (!allComplete)
        {
            return;
        }

        hasCompleted = true;
        isActive = false;
        AllObjectivesComplete?.Invoke();
    }

    private int EvaluateObjective(LevelObjective objective)
    {
        if (objective == null)
        {
            return 0;
        }

        if (objective.ObjectiveType == LevelObjectiveType.ScoreAtLeast)
        {
            return scoreController != null ? scoreController.Score : 0;
        }

        int tierIndex = objective.TierIndex;
        if (tierIndex < 0 || tierIndex >= cumulativeCountsByTier.Count)
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
            return cumulativeCountsByTier[tierIndex];
        }

        return CountSimultaneous(tierIndex);
    }

    private int CountSimultaneous(int tierIndex)
    {
        if (itemPool == null)
        {
            return 0;
        }

        MergeItem held = itemDropper != null ? itemDropper.HeldItem : null;
        return itemPool.CountActiveOfTier(tierIndex, held);
    }
}
