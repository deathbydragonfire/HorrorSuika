using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One authored level: victory objectives, spawn-pool rules, failure limits, and the container shape.
/// The unit of play.
/// </summary>
[CreateAssetMenu(menuName = "Merge Drop/Level Definition", fileName = "Level_00")]
public class LevelDefinition : ScriptableObject
{
    private const float MinimumVictorySettleTimeout = 0.5f;

    [Tooltip("Stable save key, independent of the level's position in the sequence.")]
    [SerializeField] private string levelId = "level_00";

    [Tooltip("Name shown in the HUD and on the level-select tile.")]
    [SerializeField] private string displayName = "Level";

    [Tooltip("Optional tier table replacing the GameController default for this level.")]
    [SerializeField] private MergeItemTierTable tierTable;

    [Tooltip("Baked container prefab carrying a PlayfieldShape component.")]
    [SerializeField] private GameObject shapePrefab;

    [Tooltip("Curve asset the level author edits. Editor-time only; runtime reads ShapePrefab.")]
    [SerializeField] private PlayfieldShapeDefinition shapeDefinition;

    [Tooltip("Every condition that must hold for the level to be won.")]
    [SerializeField] private List<LevelObjective> objectives = new List<LevelObjective>();

    [Tooltip("How many tiers are droppable at the start. 0 falls back to the tier table's value.")]
    [SerializeField] private int initialSpawnableTierCount;

    [Tooltip("Ceiling on how many tiers merges can unlock for dropping. 0 falls back to the tier table's value.")]
    [SerializeField] private int maxSpawnableTierCount;

    [Tooltip("Maximum drops allowed. 0 means unlimited.")]
    [SerializeField] private int dropLimit;

    [Tooltip("Time allowed in seconds. 0 means unlimited.")]
    [SerializeField] private float timeLimitSeconds;

    [Tooltip("Seed for the drop sequence. 0 means non-deterministic.")]
    [SerializeField] private int randomSeed;

    [Tooltip("How long the board is allowed to keep settling after the last objective completes.")]
    [SerializeField] private float victorySettleTimeout = 3f;

    /// <summary>Stable save key for this level.</summary>
    public string LevelId => levelId;

    /// <summary>Name shown in the HUD and on the level-select tile.</summary>
    public string DisplayName => displayName;

    /// <summary>Tier table override; null means use the controller's default.</summary>
    public MergeItemTierTable TierTableOverride => tierTable;

    /// <summary>Baked container prefab for this level.</summary>
    public GameObject ShapePrefab => shapePrefab;

    /// <summary>Curve asset this level was authored from; unused at runtime.</summary>
    public PlayfieldShapeDefinition ShapeDefinition => shapeDefinition;

    /// <summary>Every condition that must hold for the level to be won.</summary>
    public IReadOnlyList<LevelObjective> Objectives => objectives;

    /// <summary>Maximum drops allowed; 0 means unlimited.</summary>
    public int DropLimit => Mathf.Max(0, dropLimit);

    /// <summary>Time allowed in seconds; 0 means unlimited.</summary>
    public float TimeLimitSeconds => Mathf.Max(0f, timeLimitSeconds);

    /// <summary>Seed for the drop sequence; 0 means non-deterministic.</summary>
    public int RandomSeed => randomSeed;

    /// <summary>How long the board may keep settling after the last objective completes.</summary>
    public float VictorySettleTimeout => Mathf.Max(MinimumVictorySettleTimeout, victorySettleTimeout);

    /// <summary>Authored starting spawnable-tier count; 0 means use the resolved table.</summary>
    public int InitialSpawnableTierCount => Mathf.Max(0, initialSpawnableTierCount);

    /// <summary>Authored spawnable-tier ceiling; 0 means use the resolved table.</summary>
    public int MaxSpawnableTierCount => Mathf.Max(0, maxSpawnableTierCount);

    /// <summary>Resolves the table this level runs on, falling back to the supplied default.</summary>
    public MergeItemTierTable ResolveTierTable(MergeItemTierTable fallback)
    {
        return tierTable != null ? tierTable : fallback;
    }

    /// <summary>How many tiers are droppable at the start, clamped against the resolved table.</summary>
    public int GetInitialSpawnableTierCount(MergeItemTierTable resolvedTable)
    {
        if (initialSpawnableTierCount <= 0)
        {
            return resolvedTable != null ? resolvedTable.InitialSpawnableTierCount : 1;
        }

        return Mathf.Clamp(initialSpawnableTierCount, 1, GetMaxSpawnableTierCount(resolvedTable));
    }

    /// <summary>Ceiling on how many tiers merges can unlock, clamped against the resolved table.</summary>
    public int GetMaxSpawnableTierCount(MergeItemTierTable resolvedTable)
    {
        int tierCount = resolvedTable != null ? resolvedTable.MaxTierIndex + 1 : 1;
        if (maxSpawnableTierCount <= 0)
        {
            return resolvedTable != null ? resolvedTable.MaxSpawnableTierCount : 1;
        }

        return Mathf.Clamp(maxSpawnableTierCount, 1, Mathf.Max(1, tierCount));
    }

    private void OnValidate()
    {
        dropLimit = Mathf.Max(0, dropLimit);
        timeLimitSeconds = Mathf.Max(0f, timeLimitSeconds);
        victorySettleTimeout = Mathf.Max(MinimumVictorySettleTimeout, victorySettleTimeout);
        initialSpawnableTierCount = Mathf.Max(0, initialSpawnableTierCount);
        maxSpawnableTierCount = Mathf.Max(0, maxSpawnableTierCount);

        if (string.IsNullOrWhiteSpace(levelId))
        {
            Debug.LogWarning($"{name}: LevelId is blank; saved progress cannot be keyed to this level.", this);
        }

        if (objectives == null || objectives.Count == 0)
        {
            Debug.LogWarning($"{name}: has no objectives and can therefore never be won.", this);
        }
        else
        {
            int maxTierIndex = tierTable != null ? tierTable.MaxTierIndex : int.MaxValue - 1;
            for (int i = 0; i < objectives.Count; i++)
            {
                objectives[i]?.ClampValues(maxTierIndex);
            }
        }

        if (shapePrefab != null && shapePrefab.GetComponent<PlayfieldShape>() == null)
        {
            Debug.LogWarning($"{name}: shape prefab '{shapePrefab.name}' has no {nameof(PlayfieldShape)} component.", this);
        }
    }
}
