using UnityEngine;

/// <summary>
/// Facade over the container geometry so the dropper, death line, and camera never disagree.
/// Reads the installed <see cref="PlayfieldShape"/> when a level supplies one and falls back to its
/// own serialized rectangle otherwise.
/// </summary>
public class PlayfieldBounds : MonoBehaviour
{
    private const float MinimumClampWidth = 0.05f;

    [Tooltip("Fallback interior width used when no baked shape is installed.")]
    [SerializeField] private float innerWidth = 5.5f;

    [Tooltip("Fallback world Y of the container floor surface.")]
    [SerializeField] private float floorY;

    [Tooltip("Fallback world Y of the overflow line.")]
    [SerializeField] private float deathLineY = 6f;

    [Tooltip("Fallback world Y the held item hovers at.")]
    [SerializeField] private float dropY = 7f;

    [Tooltip("Red bar shown at the death line. Its width is the interior opening at that height.")]
    [SerializeField] private Transform deathLineVisual;

    private PlayfieldShape shape;

    /// <summary>The installed baked shape, or null when running on the serialized fallback rectangle.</summary>
    public PlayfieldShape Shape => shape;

    /// <summary>Half the usable interior width of the container; exists to size the camera frame.</summary>
    public float InnerHalfWidth => shape != null ? shape.InteriorHalfWidth : innerWidth * 0.5f;

    /// <summary>World Y of the container floor surface.</summary>
    public float FloorY => shape != null ? shape.FloorY : floorY;

    /// <summary>World Y of the overflow line.</summary>
    public float DeathLineY => shape != null ? shape.DeathLineY : deathLineY;

    /// <summary>World Y the held item hovers at.</summary>
    public float DropY => shape != null ? shape.DropY : dropY;

    /// <summary>Installs (or clears, when null) the baked shape every geometry query then delegates to.</summary>
    public void SetShape(PlayfieldShape activeShape)
    {
        shape = activeShape;
        if (shape != null)
        {
            shape.RefreshWorldOutline();
        }

        RefreshDeathLineVisual();
    }

    /// <summary>
    /// Places the red death-line bar at <paramref name="y"/> and stretches it from <paramref name="minX"/>
    /// to <paramref name="maxX"/>, the interior opening between the walls.
    /// </summary>
    public void PlaceDeathLine(float y, float minX, float maxX)
    {
        if (deathLineVisual == null)
        {
            return;
        }

        float width = Mathf.Max(0.02f, maxX - minX);
        float centerX = (minX + maxX) * 0.5f;
        Transform parent = deathLineVisual.parent;
        float parentScaleX = parent != null ? Mathf.Abs(parent.lossyScale.x) : 1f;
        if (parentScaleX < 0.0001f)
        {
            parentScaleX = 1f;
        }

        float localWidth = width / parentScaleX;
        Vector3 position = deathLineVisual.position;
        Vector3 scale = deathLineVisual.localScale;
        if (Mathf.Approximately(position.x, centerX)
            && Mathf.Approximately(position.y, y)
            && Mathf.Approximately(scale.x, localWidth))
        {
            return;
        }

        position.x = centerX;
        position.y = y;
        deathLineVisual.position = position;
        scale.x = localWidth;
        deathLineVisual.localScale = scale;
    }

    /// <summary>Resizes the red bar from the installed shape, or the fallback rectangle when none is installed.</summary>
    public void RefreshDeathLineVisual()
    {
        float y = DeathLineY;
        if (shape != null && shape.TryGetInteriorSpanAtY(y, out float minX, out float maxX))
        {
            PlaceDeathLine(y, minX, maxX);
            return;
        }

        PlaceDeathLine(y, -innerWidth * 0.5f, innerWidth * 0.5f);
    }

    /// <summary>Clamps a requested drop X so an item of the given radius stays fully inside the walls.</summary>
    public float ClampDropX(float requestedX, float itemRadius)
    {
        if (shape != null)
        {
            return shape.ClampX(requestedX, DropY, itemRadius);
        }

        float limit = Mathf.Max(MinimumClampWidth, InnerHalfWidth - itemRadius);
        return Mathf.Clamp(requestedX, -limit, limit);
    }

    /// <summary>Builds the hover position for the held item at the given X.</summary>
    public Vector3 GetDropPosition(float x)
    {
        return new Vector3(x, DropY, 0f);
    }

    private void OnEnable()
    {
        if (shape == null)
        {
            PlayfieldShape installed = GetComponentInChildren<PlayfieldShape>(true);
            if (installed != null)
            {
                shape = installed;
                shape.RefreshWorldOutline();
            }
        }

        RefreshDeathLineVisual();
    }

    private void OnDrawGizmos()
    {
        if (shape == null)
        {
            return;
        }

        float floor = FloorY;
        float drop = DropY;

        if (TryGetSpan(floor, out float floorMin, out float floorMax))
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(new Vector3(floorMin, floor, 0f), new Vector3(floorMax, floor, 0f));
        }

        if (deathLineVisual == null && TryGetSpan(DeathLineY, out float deathMin, out float deathMax))
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(new Vector3(deathMin, DeathLineY, 0f), new Vector3(deathMax, DeathLineY, 0f));
        }

        if (TryGetSpan(drop, out float dropMin, out float dropMax))
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(new Vector3(dropMin, drop, 0f), new Vector3(dropMax, drop, 0f));
        }
    }

    private bool TryGetSpan(float y, out float minX, out float maxX)
    {
        if (shape != null && shape.TryGetInteriorSpanAtY(y, out minX, out maxX))
        {
            return true;
        }

        minX = -innerWidth * 0.5f;
        maxX = innerWidth * 0.5f;
        return shape == null;
    }
}
