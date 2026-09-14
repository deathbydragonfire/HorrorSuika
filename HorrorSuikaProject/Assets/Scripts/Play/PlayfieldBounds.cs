using UnityEngine;

/// <summary>
/// Authoritative source for container geometry so the dropper, death line, and walls never disagree.
/// </summary>
public class PlayfieldBounds : MonoBehaviour
{
    private const float MinimumClampWidth = 0.05f;

    [SerializeField] private float innerWidth = 5.5f;
    [SerializeField] private float floorY;
    [SerializeField] private float deathLineY = 6f;
    [SerializeField] private float dropY = 7f;

    /// <summary>Half the usable interior width of the container.</summary>
    public float InnerHalfWidth => innerWidth * 0.5f;

    /// <summary>World Y of the container floor surface.</summary>
    public float FloorY => floorY;

    /// <summary>World Y of the overflow line.</summary>
    public float DeathLineY => deathLineY;

    /// <summary>World Y the held item hovers at.</summary>
    public float DropY => dropY;

    /// <summary>Clamps a requested drop X so an item of the given radius stays fully inside the walls.</summary>
    public float ClampDropX(float requestedX, float itemRadius)
    {
        float limit = Mathf.Max(MinimumClampWidth, InnerHalfWidth - itemRadius);
        return Mathf.Clamp(requestedX, -limit, limit);
    }

    /// <summary>Builds the hover position for the held item at the given X.</summary>
    public Vector3 GetDropPosition(float x)
    {
        return new Vector3(x, dropY, 0f);
    }

    private void OnDrawGizmos()
    {
        float halfWidth = InnerHalfWidth;

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(new Vector3(-halfWidth, floorY, 0f), new Vector3(halfWidth, floorY, 0f));
        Gizmos.DrawLine(new Vector3(-halfWidth, floorY, 0f), new Vector3(-halfWidth, dropY, 0f));
        Gizmos.DrawLine(new Vector3(halfWidth, floorY, 0f), new Vector3(halfWidth, dropY, 0f));

        Gizmos.color = Color.red;
        Gizmos.DrawLine(new Vector3(-halfWidth, deathLineY, 0f), new Vector3(halfWidth, deathLineY, 0f));

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(new Vector3(-halfWidth, dropY, 0f), new Vector3(halfWidth, dropY, 0f));
    }
}
