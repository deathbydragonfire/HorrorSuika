using System;
using UnityEngine;

/// <summary>
/// Runtime behaviour of every pooled item: applies tier data, reports merge candidates, tracks the settle window.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(SphereCollider))]
public class MergeItem : MonoBehaviour
{
    /// <summary>Collider radius baked into the prefab; visual size comes from uniform scale instead.</summary>
    public const float ColliderBaseRadius = 0.5f;

    private const float SettleDelaySeconds = 0.25f;

    [SerializeField] private Rigidbody body;
    [SerializeField] private SphereCollider sphereCollider;
    [SerializeField] private MeshFilter meshFilter;
    [SerializeField] private MeshRenderer meshRenderer;
    [SerializeField] private FleshVisualComponent fleshVisual;
    [SerializeField] private MergeItemDecorations decorations;

    private MergeItemTierTable tierTable;
    private float settleTimer;
    private bool hasSettled;

    /// <summary>Raised the first time the item touches anything after being released.</summary>
    public event Action<MergeItem> Settled;

    /// <summary>Tier index this instance currently represents.</summary>
    public int TierIndex { get; private set; } = -1;

    /// <summary>True once the item has been claimed by a merge this step; every callback bails on it.</summary>
    public bool IsConsumed { get; private set; }

    /// <summary>True while the item is still inside its post-release grace window.</summary>
    public bool IsSettling => settleTimer > 0f;

    /// <summary>World-space radius of the item.</summary>
    public float Radius { get; private set; }

    /// <summary>The item's rigidbody.</summary>
    public Rigidbody Body => body;

    private void Awake()
    {
        CacheComponents();
    }

    private void OnValidate()
    {
        CacheComponents();
    }

    private void CacheComponents()
    {
        if (body == null)
        {
            body = GetComponent<Rigidbody>();
        }

        if (sphereCollider == null)
        {
            sphereCollider = GetComponent<SphereCollider>();
        }

        if (meshFilter == null)
        {
            meshFilter = GetComponent<MeshFilter>();
        }

        if (meshRenderer == null)
        {
            meshRenderer = GetComponent<MeshRenderer>();
        }

        if (fleshVisual == null)
        {
            fleshVisual = GetComponent<FleshVisualComponent>();
        }

        if (decorations == null)
        {
            decorations = GetComponent<MergeItemDecorations>();
        }
    }

    /// <summary>Applies the tier's physical and visual values to this instance.</summary>
    public void Initialize(MergeItemTierTable table, int tierIndex, bool startKinematic)
    {
        MergeItemTier tier = table != null ? table.GetTier(tierIndex) : null;
        if (tier == null)
        {
            Debug.LogWarning($"{nameof(MergeItem)}.{nameof(Initialize)}: tier index {tierIndex} is outside the table.", this);
            return;
        }

        CacheComponents();

        tierTable = table;
        TierIndex = tierIndex;
        Radius = tier.Radius;
        IsConsumed = false;
        hasSettled = false;
        settleTimer = 0f;

        name = $"MergeItem_{tierIndex:00}";
        transform.localScale = Vector3.one * (tier.Radius * 2f);
        sphereCollider.radius = ColliderBaseRadius;

        // A held preview must not touch physics at all: a kinematic body still shoves dynamic
        // ones, which would knock the item that was just dropped off its aim.
        sphereCollider.enabled = !startKinematic;

        if (tier.OverrideMesh != null && meshFilter != null)
        {
            meshFilter.sharedMesh = tier.OverrideMesh;
        }

        // Flesh owns the visible sphere, but the MeshRenderer still holds the tier material so
        // decorations such as eyelids can share it. Avoid meshRenderer.material here: that would
        // instantiate a unique copy per pooled item for a renderer the flesh visual then hides.
        if (fleshVisual != null)
        {
            fleshVisual.SurfaceColor = tier.PlaceholderColor;
            fleshVisual.ApplyPulseSettings(table.Pulse);
        }

        bool fleshOwnsAppearance = fleshVisual != null && fleshVisual.enabled && fleshVisual.HideSourceRenderer;

        if (meshRenderer != null)
        {
            if (tier.OverrideMaterial != null)
            {
                meshRenderer.sharedMaterial = tier.OverrideMaterial;
            }
            else if (!fleshOwnsAppearance)
            {
                meshRenderer.material.color = tier.PlaceholderColor;
            }
        }

        body.mass = tier.Mass;
        body.constraints = RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY;
        body.interpolation = RigidbodyInterpolation.Interpolate;

        // Continuous detection is only valid on a dynamic body, so it is applied in Release.
        body.collisionDetectionMode = CollisionDetectionMode.Discrete;
        body.isKinematic = startKinematic;
        if (!startKinematic)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.collisionDetectionMode = CollisionDetectionMode.Continuous;
        }

        if (decorations != null)
        {
            decorations.PopulateForNewSpawn(tierTable, TierIndex);
        }
    }

    /// <summary>Hands the item over to physics and starts the settle grace window.</summary>
    public void Release()
    {
        sphereCollider.enabled = true;
        body.isKinematic = false;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        // Small items dropped at speed tunnel through the floor with discrete detection.
        body.collisionDetectionMode = CollisionDetectionMode.Continuous;
        settleTimer = SettleDelaySeconds;
        hasSettled = false;
    }

    /// <summary>Flags the item as claimed by a merge so no further callback can use it.</summary>
    public void MarkConsumed()
    {
        IsConsumed = true;
    }

    /// <summary>Clears all runtime state before the item returns to its pool.</summary>
    public void ResetForPool()
    {
        Settled = null;
        IsConsumed = false;
        hasSettled = false;
        settleTimer = 0f;
        TierIndex = -1;

        if (decorations != null)
        {
            decorations.Clear();
        }

        if (sphereCollider != null)
        {
            sphereCollider.enabled = false;
        }

        if (body != null)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.collisionDetectionMode = CollisionDetectionMode.Discrete;
            body.isKinematic = true;
        }
    }

    /// <summary>Rebuilds decorations from both merge parents instead of rolling a fresh drop.</summary>
    public void InheritDecorations(MergeItemDecorationLayout first, MergeItemDecorationLayout second)
    {
        if (decorations != null)
        {
            decorations.ApplyInherited(tierTable, first, second);
        }
    }

    /// <summary>Snapshots this item's decorations before it is despawned for a merge.</summary>
    public MergeItemDecorationLayout CaptureDecorations()
    {
        return decorations != null ? decorations.CaptureLayout() : new MergeItemDecorationLayout(null);
    }


    /// <summary>Applies a starting velocity, used when a merge inherits the momentum of its sources.</summary>
    public void SetVelocity(Vector3 linearVelocity)
    {
        if (body == null || body.isKinematic)
        {
            return;
        }

        body.linearVelocity = linearVelocity;
    }

    private void FixedUpdate()
    {
        if (settleTimer > 0f)
        {
            settleTimer -= Time.fixedDeltaTime;
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (IsConsumed)
        {
            return;
        }

        if (!hasSettled)
        {
            hasSettled = true;
            Settled?.Invoke(this);
        }

        if (collision.rigidbody == null)
        {
            return;
        }

        MergeItem other = collision.rigidbody.GetComponent<MergeItem>();
        if (other == null || other == this || other.IsConsumed || other.TierIndex != TierIndex)
        {
            return;
        }

        MergeCoordinator.Enqueue(this, other, collision.GetContact(0).point);
    }
}
