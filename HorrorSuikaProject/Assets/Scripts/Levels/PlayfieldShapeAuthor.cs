using UnityEngine;

/// <summary>
/// Scene-placed anchor for the shape authoring tool. Exists only so the curve can be edited with
/// scene-view handles at true world scale: Unity dispatches <c>OnSceneGUI</c> only for editors of
/// objects present in a scene, so a custom inspector on the asset itself could never receive it.
/// Runtime-inert - it has no Update and ships no geometry.
/// </summary>
[DisallowMultipleComponent]
public class PlayfieldShapeAuthor : MonoBehaviour
{
    [Tooltip("Curve asset this proxy edits.")]
    [SerializeField] private PlayfieldShapeDefinition definition;

    [Tooltip("Edit the left half only and generate the right half by reflection across X = 0.")]
    [SerializeField] private bool mirrorX;

    [Tooltip("Prefab the Bake buttons write to. Leave empty to create one named after the definition.")]
    [SerializeField] private GameObject bakeTargetPrefab;

    [Tooltip("Host in this scene that Bake and Install republishes the new geometry through.")]
    [SerializeField] private PlayfieldShapeHost shapeHost;

    [Tooltip("Tier table used to draw the largest item's circle at the narrowest point of the interior.")]
    [SerializeField] private MergeItemTierTable tierTable;

    /// <summary>Curve asset this proxy edits.</summary>
    public PlayfieldShapeDefinition Definition => definition;

    /// <summary>True when only the left half is editable and the right half is reflected.</summary>
    public bool MirrorX => mirrorX;

    /// <summary>Prefab the Bake buttons write to, or null to derive the path from the definition.</summary>
    public GameObject BakeTargetPrefab => bakeTargetPrefab;

    /// <summary>Host used by Bake and Install.</summary>
    public PlayfieldShapeHost ShapeHost => shapeHost;

    /// <summary>Tier table backing the fit preview.</summary>
    public MergeItemTierTable TierTable => tierTable;
}
