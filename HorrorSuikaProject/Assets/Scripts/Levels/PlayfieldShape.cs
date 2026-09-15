using UnityEngine;

/// <summary>
/// Baked geometry data on a container prefab root. This is the query surface
/// <see cref="PlayfieldBounds"/> delegates to once a shape is installed.
/// </summary>
public class PlayfieldShape : MonoBehaviour
{
    private const float MinimumSpanWidth = 0.05f;

    [Tooltip("Interior boundary polyline in local space, written by the shape baker.")]
    [SerializeField] private Vector2[] bakedOutline = new Vector2[0];

    [Tooltip("Local Y of the overflow line, written by the shape baker.")]
    [SerializeField] private float deathLineY = 6f;

    [Tooltip("Local Y the held item hovers at, written by the shape baker.")]
    [SerializeField] private float dropY = 7f;

    [Tooltip("Local Y of the lowest interior point, written by the shape baker.")]
    [SerializeField] private float floorY;

    [Tooltip("Local-space bounds of the baked solid, written by the shape baker.")]
    [SerializeField] private Bounds localBounds = new Bounds(Vector3.zero, Vector3.one);

    private Vector2[] worldOutline = new Vector2[0];
    private bool hasWorldCache;

    /// <summary>World Y of the overflow line.</summary>
    public float DeathLineY => transform.TransformPoint(new Vector3(0f, deathLineY, 0f)).y;

    /// <summary>World Y the held item hovers at.</summary>
    public float DropY => transform.TransformPoint(new Vector3(0f, dropY, 0f)).y;

    /// <summary>World Y of the container floor surface.</summary>
    public float FloorY => transform.TransformPoint(new Vector3(0f, floorY, 0f)).y;

    /// <summary>World-space bounds of the baked solid.</summary>
    public Bounds WorldBounds
    {
        get
        {
            Vector3 center = transform.TransformPoint(localBounds.center);
            Vector3 lossyScale = transform.lossyScale;
            Vector3 size = new Vector3(
                localBounds.size.x * Mathf.Abs(lossyScale.x),
                localBounds.size.y * Mathf.Abs(lossyScale.y),
                localBounds.size.z * Mathf.Abs(lossyScale.z));
            return new Bounds(center, size);
        }
    }

    /// <summary>Number of points in the baked interior outline; zero means the shape was never baked.</summary>
    public int OutlinePointCount => bakedOutline != null ? bakedOutline.Length : 0;

    /// <summary>
    /// Largest horizontal distance from the shape centre to the interior boundary. Used only to size
    /// the camera frame, so it must cover the widest part of the interior rather than a single scanline.
    /// </summary>
    public float InteriorHalfWidth
    {
        get
        {
            if (!hasWorldCache)
            {
                RefreshWorldOutline();
            }

            if (worldOutline.Length == 0)
            {
                return WorldBounds.extents.x;
            }

            float centerX = transform.position.x;
            float halfWidth = 0f;
            for (int i = 0; i < worldOutline.Length; i++)
            {
                halfWidth = Mathf.Max(halfWidth, Mathf.Abs(worldOutline[i].x - centerX));
            }

            return halfWidth;
        }
    }

    /// <summary>Writes the baked geometry. Called by the editor baker only.</summary>
    public void SetBakedData(Vector2[] outline, float overflowY, float hoverY, float floorSurfaceY, Bounds bakedLocalBounds)
    {
        bakedOutline = outline;
        deathLineY = overflowY;
        dropY = hoverY;
        floorY = floorSurfaceY;
        localBounds = bakedLocalBounds;
        hasWorldCache = false;
    }

    /// <summary>Rebuilds the cached world-space outline; call after moving or scaling the shape.</summary>
    public void RefreshWorldOutline()
    {
        int count = bakedOutline != null ? bakedOutline.Length : 0;
        if (worldOutline.Length != count)
        {
            worldOutline = new Vector2[count];
        }

        for (int i = 0; i < count; i++)
        {
            Vector3 world = transform.TransformPoint(new Vector3(bakedOutline[i].x, bakedOutline[i].y, 0f));
            worldOutline[i] = new Vector2(world.x, world.y);
        }

        hasWorldCache = true;
    }

    /// <summary>
    /// Finds the interior horizontal span at a world Y, taking the crossing closest to the shape
    /// centre on each side. Allocation-free after the first call.
    /// </summary>
    public bool TryGetInteriorSpanAtY(float y, out float minX, out float maxX)
    {
        minX = 0f;
        maxX = 0f;

        if (!hasWorldCache)
        {
            RefreshWorldOutline();
        }

        if (worldOutline.Length < 2)
        {
            return false;
        }

        float centerX = transform.TransformPoint(new Vector3(0f, 0f, 0f)).x;
        bool foundLeft = false;
        bool foundRight = false;

        for (int i = 0; i < worldOutline.Length - 1; i++)
        {
            Vector2 a = worldOutline[i];
            Vector2 b = worldOutline[i + 1];
            bool spansY = (a.y <= y && b.y > y) || (b.y <= y && a.y > y);
            if (!spansY)
            {
                continue;
            }

            float t = Mathf.Approximately(b.y, a.y) ? 0f : (y - a.y) / (b.y - a.y);
            float crossingX = Mathf.LerpUnclamped(a.x, b.x, t);

            if (crossingX <= centerX)
            {
                if (!foundLeft || crossingX > minX)
                {
                    minX = crossingX;
                    foundLeft = true;
                }
            }
            else if (!foundRight || crossingX < maxX)
            {
                maxX = crossingX;
                foundRight = true;
            }
        }

        Bounds worldBounds = WorldBounds;
        if (!foundLeft)
        {
            minX = worldBounds.min.x;
        }

        if (!foundRight)
        {
            maxX = worldBounds.max.x;
        }

        return foundLeft || foundRight;
    }

    /// <summary>
    /// Clamps a requested X so an item of the given radius fits inside the interior span at that Y.
    /// Returns the span midpoint when the inset span inverts, i.e. the neck is narrower than the item.
    /// </summary>
    public float ClampX(float requestedX, float y, float itemRadius)
    {
        if (!TryGetInteriorSpanAtY(y, out float minX, out float maxX))
        {
            return requestedX;
        }

        float insetMin = minX + itemRadius;
        float insetMax = maxX - itemRadius;
        if (insetMax - insetMin < MinimumSpanWidth)
        {
            return (minX + maxX) * 0.5f;
        }

        return Mathf.Clamp(requestedX, insetMin, insetMax);
    }

    private void Awake()
    {
        RefreshWorldOutline();
    }

    private void OnDrawGizmosSelected()
    {
        if (bakedOutline == null || bakedOutline.Length < 2)
        {
            return;
        }

        Gizmos.color = Color.cyan;
        for (int i = 0; i < bakedOutline.Length - 1; i++)
        {
            Vector3 a = transform.TransformPoint(new Vector3(bakedOutline[i].x, bakedOutline[i].y, 0f));
            Vector3 b = transform.TransformPoint(new Vector3(bakedOutline[i + 1].x, bakedOutline[i + 1].y, 0f));
            Gizmos.DrawLine(a, b);
        }
    }
}
