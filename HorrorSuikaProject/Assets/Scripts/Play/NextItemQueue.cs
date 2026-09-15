using System;
using UnityEngine;

/// <summary>
/// Produces the current and upcoming droppable tiers, and owns which tiers are unlocked for dropping.
/// Uses <see cref="System.Random"/> so a seed makes runs reproducible without disturbing Unity's
/// global random state.
/// </summary>
public class NextItemQueue : MonoBehaviour
{
    private const int RandomSeedUnset = 0;

    [SerializeField] private int seed = RandomSeedUnset;

    private MergeItemTierTable tierTable;
    private System.Random random;
    private int unlockedTierCount = 1;
    private int initialSpawnableTierOverride;
    private int maxSpawnableTierOverride;

    /// <summary>Raised whenever the current tier, next tier, or unlocked set changes.</summary>
    public event Action Changed;

    /// <summary>Tier that the dropper will spawn now.</summary>
    public int CurrentTier { get; private set; }

    /// <summary>Tier previewed in the HUD.</summary>
    public int NextTier { get; private set; }

    /// <summary>How many tiers are currently droppable; rolls pick from [0, UnlockedTierCount).</summary>
    public int UnlockedTierCount => unlockedTierCount;

    /// <summary>Highest tier index the player is currently allowed to drop.</summary>
    public int HighestUnlockedTierIndex => unlockedTierCount - 1;

    /// <summary>How many tiers are droppable at the start of a run, after any per-level override.</summary>
    public int EffectiveInitialSpawnableTierCount => initialSpawnableTierOverride > 0
        ? Mathf.Min(initialSpawnableTierOverride, EffectiveMaxSpawnableTierCount)
        : tierTable != null ? tierTable.InitialSpawnableTierCount : 1;

    /// <summary>Ceiling on how many tiers merges can unlock, after any per-level override.</summary>
    public int EffectiveMaxSpawnableTierCount => maxSpawnableTierOverride > 0
        ? maxSpawnableTierOverride
        : tierTable != null ? tierTable.MaxSpawnableTierCount : 1;

    /// <summary>Supplies the tier table and the random seed (0 means non-deterministic).</summary>
    public void Configure(MergeItemTierTable table, int randomSeed)
    {
        Configure(table, randomSeed, 0, 0);
    }

    /// <summary>
    /// Supplies the tier table, the random seed, and per-level spawnable-tier counts.
    /// Non-positive overrides fall back to the table's own values.
    /// </summary>
    public void Configure(MergeItemTierTable table, int randomSeed, int initialSpawnableTierCount, int maxSpawnableTierCount)
    {
        tierTable = table;
        seed = randomSeed;
        initialSpawnableTierOverride = Mathf.Max(0, initialSpawnableTierCount);
        maxSpawnableTierOverride = Mathf.Max(0, maxSpawnableTierCount);
        Reset();
    }

    /// <summary>
    /// Makes a tier droppable because a merge just produced it; every lower tier is implicitly
    /// unlocked too. Capped by the effective max spawnable count, and a no-op when already unlocked.
    /// </summary>
    public bool UnlockTier(int tierIndex)
    {
        if (tierTable == null)
        {
            return false;
        }

        int requestedCount = Mathf.Min(tierIndex + 1, EffectiveMaxSpawnableTierCount);
        if (requestedCount <= unlockedTierCount)
        {
            return false;
        }

        unlockedTierCount = requestedCount;
        Changed?.Invoke();
        return true;
    }

    /// <summary>Returns the tier to drop now and rolls a fresh preview.</summary>
    public int Advance()
    {
        int dropped = CurrentTier;
        CurrentTier = NextTier;
        NextTier = RollTier();
        Changed?.Invoke();
        return dropped;
    }

    /// <summary>Re-seeds the generator, relocks every merge-earned tier, and rolls a fresh pair.</summary>
    public void Reset()
    {
        random = seed == RandomSeedUnset
            ? new System.Random(Environment.TickCount)
            : new System.Random(seed);

        unlockedTierCount = tierTable != null ? EffectiveInitialSpawnableTierCount : 1;
        CurrentTier = RollTier();
        NextTier = RollTier();
        Changed?.Invoke();
    }

    private int RollTier()
    {
        if (tierTable == null)
        {
            return 0;
        }

        return random.Next(0, Mathf.Max(1, unlockedTierCount));
    }
}
