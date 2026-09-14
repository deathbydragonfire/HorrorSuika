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

    /// <summary>Supplies the tier table and the random seed (0 means non-deterministic).</summary>
    public void Configure(MergeItemTierTable table, int randomSeed)
    {
        tierTable = table;
        seed = randomSeed;
        Reset();
    }

    /// <summary>
    /// Makes a tier droppable because a merge just produced it; every lower tier is implicitly
    /// unlocked too. Capped by the table's MaxSpawnableTierCount, and a no-op when already unlocked.
    /// </summary>
    public bool UnlockTier(int tierIndex)
    {
        if (tierTable == null)
        {
            return false;
        }

        int requestedCount = Mathf.Min(tierIndex + 1, tierTable.MaxSpawnableTierCount);
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

        unlockedTierCount = tierTable != null ? tierTable.InitialSpawnableTierCount : 1;
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
