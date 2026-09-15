using UnityEngine;

/// <summary>
/// Owns the container instance for the active level: swaps in a baked shape prefab and republishes
/// the resulting geometry to <see cref="PlayfieldBounds"/> and the camera fitter.
/// </summary>
public class PlayfieldShapeHost : MonoBehaviour
{
    [Tooltip("Parent the shape instance is created under. Defaults to this transform.")]
    [SerializeField] private Transform containerParent;

    [Tooltip("Bounds facade the installed shape is published to.")]
    [SerializeField] private PlayfieldBounds playfieldBounds;

    [Tooltip("Camera fitter re-run after a shape swap so the new geometry stays framed.")]
    [SerializeField] private PortraitCameraFitter cameraFitter;

    [Tooltip("Shape installed on Awake when no level supplies one. Makes the shape pipeline testable on its own.")]
    [SerializeField] private GameObject defaultShapePrefab;

    private GameObject activeInstance;

    /// <summary>The shape currently installed, or null when running on the fallback geometry.</summary>
    public PlayfieldShape ActiveShape { get; private set; }

    /// <summary>Injects the scene references the host publishes geometry to.</summary>
    public void Configure(Transform parent, PlayfieldBounds bounds, PortraitCameraFitter fitter)
    {
        if (parent != null)
        {
            containerParent = parent;
        }

        if (bounds != null)
        {
            playfieldBounds = bounds;
        }

        if (fitter != null)
        {
            cameraFitter = fitter;
        }
    }

    /// <summary>
    /// Destroys the previous container instance and installs the given baked shape prefab.
    /// A null prefab leaves the existing container in place so the game is never left without a floor.
    /// </summary>
    public void Apply(GameObject shapePrefab)
    {
        if (shapePrefab == null)
        {
            Debug.LogWarning($"{nameof(PlayfieldShapeHost)}: no shape prefab supplied; keeping the existing container.", this);
            return;
        }

        if (activeInstance != null)
        {
            DestroyInstance(activeInstance);
            activeInstance = null;
        }

        Transform parent = containerParent != null ? containerParent : transform;

        // Clear every leftover child, not just the tracked instance: the authoring tool installs
        // shapes at edit time, and those instances are saved into the scene.
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            DestroyInstance(parent.GetChild(i).gameObject);
        }

        activeInstance = Instantiate(shapePrefab, parent);
        activeInstance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        activeInstance.transform.localScale = Vector3.one;

        ActiveShape = activeInstance.GetComponent<PlayfieldShape>();
        if (ActiveShape == null)
        {
            Debug.LogError(
                $"{nameof(PlayfieldShapeHost)}: '{shapePrefab.name}' has no {nameof(PlayfieldShape)} component; " +
                $"falling back to the serialized {nameof(PlayfieldBounds)} values.",
                this);
        }

        if (playfieldBounds != null)
        {
            playfieldBounds.SetShape(ActiveShape);
        }

        if (cameraFitter != null && playfieldBounds != null)
        {
            cameraFitter.Configure(playfieldBounds);
        }
    }

    private void Awake()
    {
        if (defaultShapePrefab != null)
        {
            Apply(defaultShapePrefab);
        }
    }

    private static void DestroyInstance(GameObject target)
    {
        if (Application.isPlaying)
        {
            Destroy(target);
        }
        else
        {
            DestroyImmediate(target);
        }
    }
}
