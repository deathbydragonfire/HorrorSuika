using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Despawns any item that ends up outside the container so a single tunnelling event cannot strand an
/// item and permanently block a simultaneous objective. Insurance, not a substitute for correct
/// geometry: if this fires in normal play, that shape's wall thickness is too small.
/// </summary>
public class PlayfieldEscapeGuard : MonoBehaviour
{
    private const float EscapeMargin = 1.5f;

    [Tooltip("Bounds facade whose installed shape supplies the containment volume.")]
    [SerializeField] private PlayfieldBounds playfieldBounds;

    [Tooltip("Pool the escaped items are returned to.")]
    [SerializeField] private MergeItemPool itemPool;

    [Tooltip("Start watching immediately on Awake, without waiting for a controller to activate it.")]
    [SerializeField] private bool activateOnAwake = true;

    private readonly List<MergeItem> escapedBuffer = new List<MergeItem>(8);

    private bool isActive;

    /// <summary>Raised for every item removed for leaving the container.</summary>
    public event Action<MergeItem> ItemEscaped;

    /// <summary>Injects the geometry source and the pool escaped items are returned to.</summary>
    public void Configure(PlayfieldBounds bounds, MergeItemPool pool)
    {
        playfieldBounds = bounds;
        itemPool = pool;
    }

    /// <summary>Starts or stops watching.</summary>
    public void SetActive(bool active)
    {
        isActive = active;
    }

    private void Awake()
    {
        isActive = activateOnAwake;
    }

    private void FixedUpdate()
    {
        if (!isActive || playfieldBounds == null || itemPool == null)
        {
            return;
        }

        PlayfieldShape shape = playfieldBounds.Shape;
        if (shape == null)
        {
            return;
        }

        Bounds allowed = shape.WorldBounds;
        allowed.Expand(EscapeMargin * 2f);

        IReadOnlyList<MergeItem> items = itemPool.ActiveItems;
        escapedBuffer.Clear();

        for (int i = 0; i < items.Count; i++)
        {
            MergeItem item = items[i];
            if (item == null || item.IsConsumed)
            {
                continue;
            }

            Vector3 position = item.transform.position;
            if (position.y > allowed.max.y)
            {
                // Above the container is legal: that is where the held item hovers and overflow is judged.
                continue;
            }

            if (!allowed.Contains(new Vector3(position.x, position.y, allowed.center.z)))
            {
                escapedBuffer.Add(item);
            }
        }

        for (int i = 0; i < escapedBuffer.Count; i++)
        {
            MergeItem escaped = escapedBuffer[i];
            itemPool.Despawn(escaped);
            ItemEscaped?.Invoke(escaped);
        }

        escapedBuffer.Clear();
    }
}
