using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Ordered tier ladder plus the global merge and decoration rules. The single asset where art and balance get retuned.
/// </summary>
[CreateAssetMenu(menuName = "Merge Drop/Item Tier Table", fileName = "DefaultItemTiers")]
public class MergeItemTierTable : ScriptableObject
{
    private const float MinimumRadius = 0.01f;
    private const float MinimumMass = 0.01f;

    [SerializeField] private List<MergeItemTier> tiers = new List<MergeItemTier>();
    [SerializeField] private int initialSpawnableTierCount = 2;
    [SerializeField] private int maxSpawnableTierCount = 5;
    [SerializeField] private int topTierPopScore = 100;
    [SerializeField] private bool topTierMergePops = true;

    [Header("Decorations")]
    [SerializeField, Tooltip("Prefabs that can appear on dropped blobs. Each entry has its own drop chance, tier filter, and merge inheritance.")]
    private List<MergeItemDecorationDefinition> decorations = new List<MergeItemDecorationDefinition>();

    [Header("Pulse")]
    [SerializeField, Tooltip("Cosmetic breathing applied to every flesh blob. Does not affect colliders or merge radii.")]
    private FleshPulseSettings pulse = new FleshPulseSettings();

    /// <summary>All tiers, lowest first.</summary>
    public IReadOnlyList<MergeItemTier> Tiers => tiers;

    /// <summary>How many tiers are droppable at the start of a run, before any merge unlocks more.</summary>
    public int InitialSpawnableTierCount => Mathf.Clamp(initialSpawnableTierCount, 1, MaxSpawnableTierCount);

    /// <summary>Ceiling on how many tiers merges can ever unlock for dropping.</summary>
    public int MaxSpawnableTierCount => Mathf.Clamp(maxSpawnableTierCount, 1, Mathf.Max(1, tiers.Count));

    /// <summary>Index of the highest tier on the ladder.</summary>
    public int MaxTierIndex => tiers.Count - 1;

    /// <summary>Score awarded when two max-tier items annihilate.</summary>
    public int TopTierPopScore => topTierPopScore;

    /// <summary>When true two top-tier items pop instead of producing a further tier.</summary>
    public bool TopTierMergePops => topTierMergePops;

    /// <summary>Global decoration prefabs that can appear on blobs, independent of any one tier's look.</summary>
    public IReadOnlyList<MergeItemDecorationDefinition> Decorations => decorations;

    /// <summary>Cosmetic pulse applied to every flesh blob on this table.</summary>
    public FleshPulseSettings Pulse => pulse;

    /// <summary>Returns the tier definition at the index, or null when out of range.</summary>
    public MergeItemTier GetTier(int tierIndex)
    {
        if (tierIndex < 0 || tierIndex >= tiers.Count)
        {
            return null;
        }

        return tiers[tierIndex];
    }

    /// <summary>Resolves the tier produced by merging two items of the given tier.</summary>
    public bool TryGetNextTier(int tierIndex, out int nextTierIndex)
    {
        nextTierIndex = tierIndex + 1;
        return nextTierIndex >= 0 && nextTierIndex <= MaxTierIndex;
    }

    private void OnValidate()
    {
        if (tiers == null || tiers.Count == 0)
        {
            Debug.LogWarning($"{name}: tier list is empty; the dropper has nothing to spawn.", this);
            return;
        }

        if (maxSpawnableTierCount > tiers.Count)
        {
            Debug.LogWarning($"{name}: MaxSpawnableTierCount ({maxSpawnableTierCount}) exceeds tier count ({tiers.Count}); clamping.", this);
            maxSpawnableTierCount = tiers.Count;
        }

        maxSpawnableTierCount = Mathf.Max(1, maxSpawnableTierCount);

        if (initialSpawnableTierCount > maxSpawnableTierCount)
        {
            Debug.LogWarning($"{name}: InitialSpawnableTierCount ({initialSpawnableTierCount}) exceeds MaxSpawnableTierCount ({maxSpawnableTierCount}); clamping.", this);
            initialSpawnableTierCount = maxSpawnableTierCount;
        }

        initialSpawnableTierCount = Mathf.Max(1, initialSpawnableTierCount);
        topTierPopScore = Mathf.Max(0, topTierPopScore);

        for (int i = 0; i < tiers.Count; i++)
        {
            MergeItemTier tier = tiers[i];
            if (tier == null)
            {
                continue;
            }

            if (tier.Radius <= 0f)
            {
                Debug.LogWarning($"{name}: tier {i} has a non-positive radius; clamping.", this);
            }

            tier.ClampValues(MinimumRadius, MinimumMass);
        }

        if (decorations == null)
        {
            decorations = new List<MergeItemDecorationDefinition>();
        }

        for (int i = 0; i < decorations.Count; i++)
        {
            MergeItemDecorationDefinition decoration = decorations[i];
            if (decoration == null)
            {
                continue;
            }

            decoration.ClampValues();
        }

        if (pulse == null)
        {
            pulse = new FleshPulseSettings();
        }

        pulse.ClampValues();
    }
}
