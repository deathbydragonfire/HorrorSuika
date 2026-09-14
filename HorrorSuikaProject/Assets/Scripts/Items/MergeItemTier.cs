using System;
using UnityEngine;

/// <summary>
/// Serializable definition of a single tier on the merge ladder.
/// Holds every per-tier physical and visual value so art and balance live in data, not code.
/// </summary>
[Serializable]
public class MergeItemTier
{
    [SerializeField] private string displayName = "Tier 01";
    [SerializeField] private float radius = 0.2f;
    [SerializeField] private float mass = 0.5f;
    [SerializeField] private int mergeScore = 1;
    [SerializeField] private Color placeholderColor = Color.white;
    [SerializeField] private Mesh overrideMesh;
    [SerializeField] private Material overrideMaterial;

    /// <summary>Free-text label for the tier. Numbered by default so art direction imposes no rename.</summary>
    public string DisplayName => displayName;

    /// <summary>World-space radius of the item; also drives uniform scale.</summary>
    public float Radius => radius;

    /// <summary>Rigidbody mass applied to an item of this tier.</summary>
    public float Mass => mass;

    /// <summary>Points awarded when two items of this tier merge.</summary>
    public int MergeScore => mergeScore;

    /// <summary>Colour used when no override material is supplied.</summary>
    public Color PlaceholderColor => placeholderColor;

    /// <summary>Optional mesh replacing the built-in sphere.</summary>
    public Mesh OverrideMesh => overrideMesh;

    /// <summary>Optional material replacing the placeholder colour.</summary>
    public Material OverrideMaterial => overrideMaterial;

    /// <summary>Clamps invalid authored values so the asset stays editable instead of throwing.</summary>
    public void ClampValues(float minimumRadius, float minimumMass)
    {
        radius = Mathf.Max(minimumRadius, radius);
        mass = Mathf.Max(minimumMass, mass);
        mergeScore = Mathf.Max(0, mergeScore);
    }
}
