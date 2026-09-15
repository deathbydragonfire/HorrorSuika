using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

/// <summary>
/// Owns one <see cref="ObjectPool{T}"/> per tier and is the only place items are created or returned.
/// </summary>
public class MergeItemPool : MonoBehaviour
{
    private const int DefaultPoolCapacity = 12;
    private const int MaxPoolSize = 128;

    private readonly Dictionary<int, ObjectPool<MergeItem>> poolsByTier = new Dictionary<int, ObjectPool<MergeItem>>();
    private readonly List<MergeItem> activeItems = new List<MergeItem>();
    private readonly Dictionary<MergeItem, int> tierByItem = new Dictionary<MergeItem, int>();

    private MergeItemTierTable tierTable;
    private GameObject itemPrefab;
    private Transform itemRoot;

    /// <summary>All items currently alive in the container.</summary>
    public IReadOnlyList<MergeItem> ActiveItems => activeItems;

    /// <summary>Number of items currently alive in the container.</summary>
    public int ActiveCount => activeItems.Count;

    /// <summary>Supplies the pool with the tier data, item prefab, and hierarchy parent.</summary>
    public void Configure(MergeItemTierTable table, GameObject prefab, Transform root)
    {
        tierTable = table;
        itemPrefab = prefab;
        itemRoot = root != null ? root : transform;
    }

    /// <summary>Takes an item of the requested tier from its pool and places it in the world.</summary>
    public MergeItem Spawn(int tierIndex, Vector3 position, bool startKinematic)
    {
        if (tierTable == null || itemPrefab == null)
        {
            Debug.LogWarning($"{nameof(MergeItemPool)}: Configure has not been called.", this);
            return null;
        }

        if (tierTable.GetTier(tierIndex) == null)
        {
            Debug.LogWarning($"{nameof(MergeItemPool)}: no tier at index {tierIndex}.", this);
            return null;
        }

        MergeItem item = GetPool(tierIndex).Get();
        item.transform.SetPositionAndRotation(new Vector3(position.x, position.y, 0f), Quaternion.identity);
        item.Initialize(tierTable, tierIndex, startKinematic);
        item.gameObject.SetActive(true);

        activeItems.Add(item);
        tierByItem[item] = tierIndex;
        return item;
    }

    /// <summary>Returns an item to its pool. Safe to call with an already-despawned item.</summary>
    public void Despawn(MergeItem item)
    {
        if (item == null || !tierByItem.TryGetValue(item, out int tierIndex))
        {
            return;
        }

        tierByItem.Remove(item);
        activeItems.Remove(item);
        GetPool(tierIndex).Release(item);
    }

    /// <summary>Returns every active item to its pool; used by restart.</summary>
    public void DespawnAll()
    {
        for (int i = activeItems.Count - 1; i >= 0; i--)
        {
            MergeItem item = activeItems[i];
            if (item == null)
            {
                activeItems.RemoveAt(i);
                continue;
            }

            if (tierByItem.TryGetValue(item, out int tierIndex))
            {
                tierByItem.Remove(item);
                GetPool(tierIndex).Release(item);
            }

            activeItems.RemoveAt(i);
        }
    }

    /// <summary>Counts the active items currently representing the given tier.</summary>
    public int CountActiveOfTier(int tierIndex)
    {
        int count = 0;
        for (int i = 0; i < activeItems.Count; i++)
        {
            MergeItem item = activeItems[i];
            if (item != null && !item.IsConsumed && item.TierIndex == tierIndex)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Pre-creates instances of the spawnable tiers so the first drops never instantiate mid-frame.</summary>
    public void Prewarm(int instancesPerTier)
    {
        Prewarm(instancesPerTier, tierTable != null ? tierTable.MaxSpawnableTierCount : 0);
    }

    /// <summary>
    /// Pre-creates instances for an explicit number of tiers, because a level's spawnable tier count
    /// rather than the table's is the authority on how many tiers can appear.
    /// </summary>
    public void Prewarm(int instancesPerTier, int tierCount)
    {
        if (tierTable == null || itemPrefab == null || instancesPerTier <= 0 || tierCount <= 0)
        {
            return;
        }

        List<MergeItem> warmed = new List<MergeItem>(instancesPerTier);
        for (int tierIndex = 0; tierIndex < tierCount; tierIndex++)
        {
            ObjectPool<MergeItem> pool = GetPool(tierIndex);
            warmed.Clear();
            for (int i = 0; i < instancesPerTier; i++)
            {
                warmed.Add(pool.Get());
            }

            for (int i = 0; i < warmed.Count; i++)
            {
                pool.Release(warmed[i]);
            }
        }
    }

    private ObjectPool<MergeItem> GetPool(int tierIndex)
    {
        if (poolsByTier.TryGetValue(tierIndex, out ObjectPool<MergeItem> pool))
        {
            return pool;
        }

        pool = new ObjectPool<MergeItem>(
            createFunc: CreateItem,
            actionOnGet: OnGetItem,
            actionOnRelease: OnReleaseItem,
            actionOnDestroy: OnDestroyItem,
            collectionCheck: false,
            defaultCapacity: DefaultPoolCapacity,
            maxSize: MaxPoolSize);

        poolsByTier.Add(tierIndex, pool);
        return pool;
    }

    private MergeItem CreateItem()
    {
        GameObject instance = Instantiate(itemPrefab, itemRoot);
        instance.SetActive(false);
        MergeItem item = instance.GetComponent<MergeItem>();
        if (item == null)
        {
            Debug.LogError($"{nameof(MergeItemPool)}: the item prefab has no {nameof(MergeItem)} component.", this);
        }

        return item;
    }

    private static void OnGetItem(MergeItem item)
    {
        // Initialize and activation happen in Spawn once the position is known.
    }

    private static void OnReleaseItem(MergeItem item)
    {
        if (item == null)
        {
            return;
        }

        item.ResetForPool();
        item.gameObject.SetActive(false);
    }

    private static void OnDestroyItem(MergeItem item)
    {
        if (item != null)
        {
            Destroy(item.gameObject);
        }
    }

    private void OnDestroy()
    {
        foreach (KeyValuePair<int, ObjectPool<MergeItem>> entry in poolsByTier)
        {
            entry.Value.Clear();
        }

        poolsByTier.Clear();
        activeItems.Clear();
        tierByItem.Clear();
    }
}
