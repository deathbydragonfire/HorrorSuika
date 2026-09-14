using System;
using UnityEngine;

/// <summary>
/// Keeps the whole container framed on any portrait viewport and pillarboxes rather than cropping
/// on wider ones. Browser canvas size is unknown at build time, so this must run at runtime.
/// </summary>
[RequireComponent(typeof(Camera))]
public class PortraitCameraFitter : MonoBehaviour
{
    [SerializeField] private float bottomPadding = 0.4f;
    [SerializeField] private float topPadding = 1.6f;
    [SerializeField] private float horizontalPadding = 0.35f;

    private Camera targetCamera;
    private PlayfieldBounds bounds;
    private int cachedScreenWidth = -1;
    private int cachedScreenHeight = -1;

    /// <summary>Raised after a successful refit so world-anchored UI can re-derive its positions.</summary>
    public event Action Refitted;

    /// <summary>World units kept below the container floor.</summary>
    public float BottomPadding => bottomPadding;

    /// <summary>World units kept above the drop line; must clear the largest item's radius.</summary>
    public float TopPadding => topPadding;

    /// <summary>Extra world units kept left and right of the container.</summary>
    public float HorizontalPadding => horizontalPadding;

    /// <summary>Supplies the geometry the camera must keep visible.</summary>
    public void Configure(PlayfieldBounds playfieldBounds)
    {
        bounds = playfieldBounds;
        cachedScreenWidth = -1;
        cachedScreenHeight = -1;
        Fit();
    }

    private void Awake()
    {
        targetCamera = GetComponent<Camera>();
    }

    private void Start()
    {
        Fit();
    }

    private void Update()
    {
        if (Screen.width == cachedScreenWidth && Screen.height == cachedScreenHeight)
        {
            return;
        }

        Fit();
    }

    private void Fit()
    {
        if (bounds == null)
        {
            return;
        }

        if (targetCamera == null)
        {
            targetCamera = GetComponent<Camera>();
        }

        int width = Screen.width;
        int height = Screen.height;
        if (width <= 0 || height <= 0)
        {
            // WebGL can report a zero dimension before the canvas layout settles.
            return;
        }

        float aspect = (float)width / height;
        if (aspect <= 0f)
        {
            return;
        }

        cachedScreenWidth = width;
        cachedScreenHeight = height;

        targetCamera.orthographic = true;

        // The frame must reach the drop line, not just the death line, or the held item is cropped.
        float frameBottomY = bounds.FloorY - bottomPadding;
        float frameTopY = bounds.DropY + topPadding;
        float requiredHalfHeight = (frameTopY - frameBottomY) * 0.5f;
        float requiredHalfWidth = bounds.InnerHalfWidth + horizontalPadding;
        float size = Mathf.Max(requiredHalfHeight, requiredHalfWidth / aspect);
        targetCamera.orthographicSize = size;

        // Anchor the bottom edge so surplus height on tall viewports opens up above the
        // drop line (where the HUD and incoming items live) instead of below the floor.
        Vector3 position = transform.position;
        position.y = frameBottomY + size;
        transform.position = position;

        Refitted?.Invoke();
    }
}
