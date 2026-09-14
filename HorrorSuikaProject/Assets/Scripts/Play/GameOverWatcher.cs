using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Loses the game when a settled item stays above the death line past a grace period.
/// </summary>
public class GameOverWatcher : MonoBehaviour
{
    private const float GraceSeconds = 2f;
    private const float GraceDecayMultiplier = 2f;

    private PlayfieldBounds bounds;
    private MergeItemPool itemPool;
    private ItemDropper itemDropper;

    private float graceTimer;
    private bool isActive;
    private bool hasTriggered;

    /// <summary>Raised once when the overflow grace period runs out.</summary>
    public event Action GameOverTriggered;

    /// <summary>Accumulated overflow time, for a HUD warning.</summary>
    public float CurrentGrace => graceTimer;

    /// <summary>Total overflow time allowed before the run ends.</summary>
    public float GraceLimit => GraceSeconds;

    /// <summary>Injects the geometry, item source, and dropper whose held item must be ignored.</summary>
    public void Configure(PlayfieldBounds playfieldBounds, MergeItemPool pool, ItemDropper dropper)
    {
        bounds = playfieldBounds;
        itemPool = pool;
        itemDropper = dropper;
    }

    /// <summary>Starts or stops watching, resetting the grace timer either way.</summary>
    public void SetActive(bool active)
    {
        isActive = active;
        graceTimer = 0f;
        hasTriggered = false;
    }

    private void FixedUpdate()
    {
        if (!isActive || hasTriggered || bounds == null || itemPool == null)
        {
            return;
        }

        if (HasViolatingItem())
        {
            graceTimer += Time.fixedDeltaTime;
        }
        else
        {
            graceTimer = Mathf.Max(0f, graceTimer - (Time.fixedDeltaTime * GraceDecayMultiplier));
        }

        if (graceTimer < GraceSeconds)
        {
            return;
        }

        hasTriggered = true;
        isActive = false;
        GameOverTriggered?.Invoke();
    }

    private bool HasViolatingItem()
    {
        IReadOnlyList<MergeItem> items = itemPool.ActiveItems;
        MergeItem held = itemDropper != null ? itemDropper.HeldItem : null;
        float deathLineY = bounds.DeathLineY;

        for (int i = 0; i < items.Count; i++)
        {
            MergeItem item = items[i];
            if (item == null || item == held || item.IsConsumed || item.IsSettling)
            {
                continue;
            }

            if (item.transform.position.y - item.Radius > deathLineY)
            {
                return true;
            }
        }

        return false;
    }
}
