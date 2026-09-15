using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Collects merge candidates raised from collision callbacks and resolves them deterministically
/// once per physics step. Resolving inside the callback causes double-merges and destroy-during-iteration bugs.
/// </summary>
public class MergeCoordinator : MonoBehaviour
{
    private readonly struct PendingMerge
    {
        public readonly MergeItem First;
        public readonly MergeItem Second;
        public readonly Vector3 ContactPoint;

        public PendingMerge(MergeItem first, MergeItem second, Vector3 contactPoint)
        {
            First = first;
            Second = second;
            ContactPoint = contactPoint;
        }
    }

    private static readonly List<PendingMerge> PendingMerges = new List<PendingMerge>(32);

    private readonly List<PendingMerge> drainBuffer = new List<PendingMerge>(32);
    private readonly HashSet<long> handledPairKeys = new HashSet<long>();

    private MergeItemTierTable tierTable;
    private MergeItemPool itemPool;

    /// <summary>Raised after a merge produced a new item: (resultTierIndex, position, awardedScore).</summary>
    public event Action<int, Vector3, int> MergePerformed;

    /// <summary>Raised when two max-tier items annihilate: (position, awardedScore).</summary>
    public event Action<Vector3, int> TopTierPopped;

    /// <summary>Queues a merge candidate pair. Called from collision callbacks only.</summary>
    public static void Enqueue(MergeItem a, MergeItem b, Vector3 contactPoint)
    {
        if (a == null || b == null || a == b || a.IsConsumed || b.IsConsumed)
        {
            return;
        }

        PendingMerges.Add(new PendingMerge(a, b, contactPoint));
    }

    /// <summary>Supplies the tier data and the pool used to create and retire items.</summary>
    public void Configure(MergeItemTierTable table, MergeItemPool pool)
    {
        tierTable = table;
        itemPool = pool;
    }

    /// <summary>Drops every queued candidate; used when the board is reset mid-cascade.</summary>
    public void ClearQueue()
    {
        PendingMerges.Clear();
        drainBuffer.Clear();
        handledPairKeys.Clear();
    }

    private void OnEnable()
    {
        // The pending queue is static, so it survives a scene load; entries referencing items
        // destroyed with the previous scene would otherwise be resolved against a dead pool.
        ClearQueue();
    }

    private void OnDisable()
    {
        ClearQueue();
    }

    private void FixedUpdate()
    {
        if (PendingMerges.Count == 0)
        {
            return;
        }

        drainBuffer.Clear();
        drainBuffer.AddRange(PendingMerges);
        PendingMerges.Clear();
        handledPairKeys.Clear();

        for (int i = 0; i < drainBuffer.Count; i++)
        {
            ResolvePair(drainBuffer[i]);
        }

        drainBuffer.Clear();
    }

    private void ResolvePair(PendingMerge pending)
    {
        MergeItem first = pending.First;
        MergeItem second = pending.Second;

        if (!IsUsable(first) || !IsUsable(second) || first == second)
        {
            return;
        }

        if (first.TierIndex != second.TierIndex)
        {
            return;
        }

        if (!handledPairKeys.Add(BuildPairKey(first, second)))
        {
            return;
        }

        int sourceTier = first.TierIndex;
        Vector3 mergePosition = (first.transform.position + second.transform.position) * 0.5f;
        mergePosition.z = 0f;
        Vector3 mergeVelocity = ComputeMassWeightedVelocity(first, second);

        first.MarkConsumed();
        second.MarkConsumed();
        itemPool.Despawn(first);
        itemPool.Despawn(second);

        bool isTopTier = sourceTier >= tierTable.MaxTierIndex;
        if (isTopTier && tierTable.TopTierMergePops)
        {
            TopTierPopped?.Invoke(mergePosition, tierTable.TopTierPopScore);
            return;
        }

        if (!tierTable.TryGetNextTier(sourceTier, out int resultTier))
        {
            return;
        }

        MergeItem merged = itemPool.Spawn(resultTier, mergePosition, false);
        if (merged == null)
        {
            return;
        }

        merged.Release();
        merged.SetVelocity(mergeVelocity);

        MergeItemTier sourceTierData = tierTable.GetTier(sourceTier);
        int awarded = sourceTierData != null ? sourceTierData.MergeScore : 0;
        MergePerformed?.Invoke(resultTier, mergePosition, awarded);
    }

    private static Vector3 ComputeMassWeightedVelocity(MergeItem first, MergeItem second)
    {
        Rigidbody a = first.Body;
        Rigidbody b = second.Body;
        if (a == null || b == null)
        {
            return Vector3.zero;
        }

        float totalMass = a.mass + b.mass;
        if (totalMass <= Mathf.Epsilon)
        {
            return Vector3.zero;
        }

        Vector3 velocity = ((a.linearVelocity * a.mass) + (b.linearVelocity * b.mass)) / totalMass;
        velocity.z = 0f;
        return velocity;
    }

    private static long BuildPairKey(MergeItem first, MergeItem second)
    {
        int low = first.GetInstanceID();
        int high = second.GetInstanceID();
        if (low > high)
        {
            (low, high) = (high, low);
        }

        return ((long)low << 32) | (uint)high;
    }

    private bool IsUsable(MergeItem item)
    {
        return tierTable != null
            && itemPool != null
            && item != null
            && !item.IsConsumed
            && item.gameObject.activeInHierarchy;
    }
}
