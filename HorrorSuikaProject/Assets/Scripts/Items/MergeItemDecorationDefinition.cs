using System;
using UnityEngine;

/// <summary>
/// One decoration prefab that can appear on merge items. Authored on the tier table so every
/// droppable blob shares the same spawn and merge rules.
/// </summary>
[Serializable]
public class MergeItemDecorationDefinition
{
    [SerializeField, Tooltip("Inspector label only.")]
    private string displayName = "Decoration";

    [SerializeField, Tooltip("Prefab instantiated on the blob. Keep its authored world size; scale is compensated at runtime unless Scale With Item is on.")]
    private GameObject prefab;

    [SerializeField, Range(0f, 1f), Tooltip("Chance this decoration appears when a blob is first dropped. Merges do not roll again; they only keep instances the parents already had.")]
    private float dropChance = 0.25f;

    [SerializeField, Min(0), Tooltip("Lowest tier index that can roll this decoration. 0 is Tier 01, so Tier 06 is index 5.")]
    private int minDropTierIndex;

    [SerializeField, Min(0), Tooltip("Highest tier index that can roll this decoration. 0 is Tier 01.")]
    private int maxDropTierIndex = 0;

    [SerializeField, Tooltip("When true, both merge parents contribute their instances of this decoration to the result.")]
    private bool inheritOnMerge = true;

    [SerializeField, Tooltip("When true, a blob keeps at most one instance. Merging two parents that both have it leaves a single one.")]
    private bool singleInstance;

    [SerializeField, Tooltip("When true, the decoration stays at the center of the camera-facing side. Other decorations are pushed off it.")]
    private bool anchorToCenter;

    [SerializeField, Tooltip("World-space radius used to keep inherited instances from stacking.")]
    private float worldRadius = 0.14f;

    [SerializeField, Tooltip("When false, the decoration keeps its authored world size as the blob grows.")]
    private bool scaleWithItem;

    [SerializeField, Tooltip("When true, the decoration looks toward the camera at rest instead of along the surface normal.")]
    private bool faceCameraAtRest = true;

    [SerializeField, Range(0f, 45f), Tooltip("Random twist around the look axis when placing an instance.")]
    private float randomRollDegrees = 12f;

    [SerializeField, Tooltip("Copies the blob's material onto SkinnedMeshRenderers, used by eyelids that should match the flesh.")]
    private bool applyHostMaterialToSkinnedMeshes;

    /// <summary>Inspector label only.</summary>
    public string DisplayName => displayName;

    /// <summary>Prefab instantiated on the blob.</summary>
    public GameObject Prefab => prefab;

    /// <summary>Chance this decoration appears when a blob is first created at a tier that allows it.</summary>
    public float DropChance => dropChance;

    /// <summary>Lowest tier index that can roll this decoration. 0 is Tier 01.</summary>
    public int MinDropTierIndex => minDropTierIndex;

    /// <summary>Highest tier index that can roll this decoration. 0 is Tier 01.</summary>
    public int MaxDropTierIndex => maxDropTierIndex;

    /// <summary>True when merge results keep both parents' instances of this decoration.</summary>
    public bool InheritOnMerge => inheritOnMerge;

    /// <summary>True when a blob keeps at most one instance of this decoration.</summary>
    public bool SingleInstance => singleInstance;

    /// <summary>True when this decoration stays at the center of the camera-facing side.</summary>
    public bool AnchorToCenter => anchorToCenter;

    /// <summary>World-space radius used to separate inherited instances.</summary>
    public float WorldRadius => worldRadius;

    /// <summary>True when the decoration scales up with the blob.</summary>
    public bool ScaleWithItem => scaleWithItem;

    /// <summary>True when the decoration looks toward the camera at rest.</summary>
    public bool FaceCameraAtRest => faceCameraAtRest;

    /// <summary>Random twist around the look axis, in degrees.</summary>
    public float RandomRollDegrees => randomRollDegrees;

    /// <summary>True when SkinnedMeshRenderers should use the blob's material.</summary>
    public bool ApplyHostMaterialToSkinnedMeshes => applyHostMaterialToSkinnedMeshes;

    /// <summary>True when a newly created blob of this tier may roll the decoration.</summary>
    public bool CanDropOnTier(int tierIndex)
    {
        return prefab != null
            && dropChance > 0f
            && tierIndex >= minDropTierIndex
            && tierIndex <= maxDropTierIndex;
    }

    /// <summary>Clamps authored values so the asset stays editable instead of throwing.</summary>
    public void ClampValues()
    {
        dropChance = Mathf.Clamp01(dropChance);
        minDropTierIndex = Mathf.Max(0, minDropTierIndex);
        maxDropTierIndex = Mathf.Max(minDropTierIndex, maxDropTierIndex);
        worldRadius = Mathf.Max(0.01f, worldRadius);
        randomRollDegrees = Mathf.Clamp(randomRollDegrees, 0f, 45f);
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = prefab != null ? prefab.name : "Decoration";
        }
    }
}
