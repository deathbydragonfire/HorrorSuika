using UnityEngine;

/// <summary>
/// Subtracts an ellipsoid from the flesh field so a decoration mesh shows through the blob.
/// The decoration root's forward points out of the item. Mouths use a deep opening. Eyes use a
/// shallow socket so the breath cannot swell over the iris.
/// </summary>
[DisallowMultipleComponent]
[ExecuteAlways]
public class FleshMouthCavity : MonoBehaviour
{
    private const float MinimumRadius = 0.02f;

    [SerializeField, Tooltip("Cavity centre in this object's local space. Negative Z is into the blob.")]
    private Vector3 localCenter = new Vector3(0f, 0f, -0.12f);

    [SerializeField, Min(0.01f), Tooltip("Radius of the opening, in this object's local space. Slightly inside the outside of the mouth mesh.")]
    private float openingRadius = 0.4f;

    [SerializeField, Min(0.01f), Tooltip("How far the hole extends along the facing axis, in local space.")]
    private float depthRadius = 0.4f;

    /// <summary>World pose of the hole. The size does not follow the chomp.</summary>
    public bool TryGetWorld(out Vector3 center, out Vector3 axis, out float opening, out float depth)
    {
        opening = openingRadius * OpeningScale;
        depth = depthRadius * DepthScale;
        center = transform.TransformPoint(localCenter);
        axis = transform.forward;
        float axisLength = axis.magnitude;
        if (axisLength < 1e-5f)
        {
            axis = Vector3.forward;
        }
        else
        {
            axis /= axisLength;
        }

        if (opening < MinimumRadius || depth < MinimumRadius)
        {
            return false;
        }

        return true;
    }

    private float OpeningScale
    {
        get
        {
            Vector3 scale = transform.lossyScale;
            return Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
        }
    }

    private float DepthScale
    {
        get => Mathf.Abs(transform.lossyScale.z);
    }

    private void OnEnable()
    {
        FleshRenderer.Register(this);
    }

    private void OnDisable()
    {
        FleshRenderer.Unregister(this);
    }

    private void OnValidate()
    {
        openingRadius = Mathf.Max(openingRadius, 0.01f);
        depthRadius = Mathf.Max(depthRadius, 0.01f);
    }
}
