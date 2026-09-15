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

    private void OnDrawGizmos()
    {
        float halfWidth = InnerHalfWidth;
        float floor = FloorY;
        float death = DeathLineY;
        float drop = DropY;

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(new Vector3(-halfWidth, floor, 0f), new Vector3(halfWidth, floor, 0f));
        Gizmos.DrawLine(new Vector3(-halfWidth, floor, 0f), new Vector3(-halfWidth, drop, 0f));
        Gizmos.DrawLine(new Vector3(halfWidth, floor, 0f), new Vector3(halfWidth, drop, 0f));

        Gizmos.color = Color.red;
        Gizmos.DrawLine(new Vector3(-halfWidth, death, 0f), new Vector3(halfWidth, death, 0f));

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(new Vector3(-halfWidth, drop, 0f), new Vector3(halfWidth, drop, 0f));
    }
}
