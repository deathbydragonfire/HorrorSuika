using UnityEngine;

/// <summary>
/// Scene-placed anchor for authoring a whole level: the playfield curve and every pass requirement.
/// Runtime-inert - it has no Update and ships no geometry. Scene-view handles are dispatched because
/// Unity only sends <c>OnSceneGUI</c> to editors of objects present in a scene.
/// </summary>
[DisallowMultipleComponent]
public class LevelAuthor : MonoBehaviour
{
    [Header("Level")]
    [Tooltip("The level asset being edited. Create one from the inspector or assign an existing asset.")]
    [SerializeField] private LevelDefinition level;

    [Tooltip("Sequence this level can be added to or removed from.")]
    [SerializeField] private LevelSequence sequence;

    [Header("Shape")]
    [Tooltip("Curve asset the scene-view handles edit. Baking writes a prefab onto the level.")]
    [SerializeField] private PlayfieldShapeDefinition shapeDefinition;

    [Tooltip("Edit the left half only and generate the right half by reflection across X = 0.")]
    [SerializeField] private bool mirrorX;

    [Tooltip("Prefab the Bake buttons write to. Leave empty to create one named after the shape.")]
    [SerializeField] private GameObject bakeTargetPrefab;

    [Tooltip("Host in this scene that Bake and Install republishes the new geometry through.")]
    [SerializeField] private PlayfieldShapeHost shapeHost;

    [Tooltip("Tier table used for objective tier names and the neck-fit preview.")]
    [SerializeField] private MergeItemTierTable tierTable;

    /// <summary>The level asset being edited.</summary>
    public LevelDefinition Level => level;

    /// <summary>Sequence this tool adds the level to.</summary>
    public LevelSequence Sequence => sequence;

    /// <summary>Curve asset the scene handles edit.</summary>
    public PlayfieldShapeDefinition ShapeDefinition => shapeDefinition;

    /// <summary>True when only the left half is editable and the right half is reflected.</summary>
    public bool MirrorX => mirrorX;

    /// <summary>Prefab the Bake buttons write to, or null to derive the path from the shape.</summary>
    public GameObject BakeTargetPrefab => bakeTargetPrefab;

    /// <summary>Host used by Bake and Install.</summary>
    public PlayfieldShapeHost ShapeHost => shapeHost;

    /// <summary>Tier table backing objective dropdowns and the fit preview.</summary>
    public MergeItemTierTable TierTable => tierTable;
}
