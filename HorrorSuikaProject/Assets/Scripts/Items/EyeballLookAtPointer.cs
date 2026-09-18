using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

/// <summary>
/// Turns the inner eyeball toward the mouse or touch pointer when it is nearby, keeping the iris
/// inside a forward cone so it never rolls back into the head.
/// </summary>
[DisallowMultipleComponent]
public class EyeballLookAtPointer : MonoBehaviour
{
    private const float MinimumLookRadius = 0.05f;
    private const float MinimumSmoothing = 0.1f;

    [SerializeField, Tooltip("Inner globe to rotate. Leave empty to use the child named 'human eyeball'.")]
    private Transform eyeball;

    [SerializeField, Tooltip("Camera used to convert the pointer into a world point. Leave empty to use Camera.main.")]
    private Camera lookCamera;

    [SerializeField, Tooltip("Iris direction in the eyeball's local space. The Human Eyeball mesh looks along -Y.")]
    private Vector3 localLookAxis = Vector3.down;

    [SerializeField, Min(MinimumLookRadius), Tooltip("World-space radius around the eye. Outside this, the iris returns to rest.")]
    private float lookRadius = 0.525f;

    [SerializeField, Range(1f, 55f), Tooltip("Maximum degrees the iris may turn left or right from rest.")]
    private float maxHorizontalLookAngle = 42f;

    [SerializeField, Range(1f, 45f), FormerlySerializedAs("maxLookAngle"), Tooltip("Maximum degrees the iris may turn up or down from rest.")]
    private float maxVerticalLookAngle = 18f;

    [SerializeField, Min(MinimumSmoothing), Tooltip("How quickly the iris eases toward the pointer.")]
    private float lookSpeed = 10f;

    [SerializeField, Min(MinimumSmoothing), Tooltip("How quickly the iris eases back to facing forward when the pointer leaves.")]
    private float returnSpeed = 5f;

    private Quaternion restLocalRotation;

    private bool restCaptured;

    /// <summary>World-space radius around the eye that attracts the iris.</summary>
    public float LookRadius
    {
        get => lookRadius;
        set => lookRadius = Mathf.Max(MinimumLookRadius, value);
    }

    /// <summary>Maximum degrees left or right from rest.</summary>
    public float MaxHorizontalLookAngle
    {
        get => maxHorizontalLookAngle;
        set => maxHorizontalLookAngle = Mathf.Clamp(value, 1f, 55f);
    }

    /// <summary>Maximum degrees up or down from rest.</summary>
    public float MaxVerticalLookAngle
    {
        get => maxVerticalLookAngle;
        set => maxVerticalLookAngle = Mathf.Clamp(value, 1f, 45f);
    }

    private void Awake()
    {
        ResolveReferences();
        CaptureRestPose();
    }

    private void OnEnable()
    {
        ResolveReferences();
        CaptureRestPose();
        ApplyRestPose();
    }

    private void OnDisable()
    {
        ApplyRestPose();
    }

private void LateUpdate()
    {
        if (eyeball == null)
        {
            return;
        }

        Camera cam = lookCamera != null ? lookCamera : Camera.main;
        if (cam == null)
        {
            return;
        }

        Quaternion targetLocal = restLocalRotation;
        float proximity = 0f;
        if (TryGetPointerWorldPoint(cam, out Vector3 pointerWorld))
        {
            float distance = Vector3.Distance(pointerWorld, eyeball.position);
            proximity = 1f - Mathf.Clamp01(distance / lookRadius);
            proximity = Mathf.SmoothStep(0f, 1f, proximity);
            if (proximity > 0f)
            {
                targetLocal = ComputeLookLocalRotation(cam, pointerWorld, proximity);
            }
        }

        float speed = proximity > 0f ? lookSpeed : returnSpeed;
        float t = 1f - Mathf.Exp(-speed * Time.deltaTime);
        eyeball.localRotation = Quaternion.Slerp(eyeball.localRotation, targetLocal, t);
    }

private Quaternion ComputeLookLocalRotation(Camera cam, Vector3 pointerWorld, float proximity)
    {
        Quaternion restWorld = eyeball.parent != null
            ? eyeball.parent.rotation * restLocalRotation
            : restLocalRotation;
        Vector3 restLook = restWorld * localLookAxis.normalized;
        Vector3 desired = pointerWorld - eyeball.position;
        if (desired.sqrMagnitude < 0.0001f)
        {
            return restLocalRotation;
        }

        desired.Normalize();
        desired = ClampLookDirection(restLook, desired, cam.transform.up);
        desired = Vector3.Slerp(restLook, desired, proximity);

        Quaternion worldRotation = Quaternion.FromToRotation(restLook, desired) * restWorld;
        Transform parent = eyeball.parent;
        if (parent == null)
        {
            return worldRotation;
        }

        return Quaternion.Inverse(parent.rotation) * worldRotation;
    }

private Vector3 ClampLookDirection(Vector3 restLook, Vector3 desired, Vector3 worldUp)
    {
        Vector3 up = worldUp;
        if (up.sqrMagnitude < 0.0001f || Mathf.Abs(Vector3.Dot(restLook.normalized, up.normalized)) > 0.98f)
        {
            up = Vector3.up;
        }

        Quaternion restOrient = Quaternion.LookRotation(restLook, up);
        Vector3 local = Quaternion.Inverse(restOrient) * desired;
        float forward = Mathf.Max(local.z, 0.0001f);
        float yaw = Mathf.Atan2(local.x, forward) * Mathf.Rad2Deg;
        float pitch = Mathf.Atan2(local.y, Mathf.Sqrt((local.x * local.x) + (forward * forward))) * Mathf.Rad2Deg;
        yaw = Mathf.Clamp(yaw, -maxHorizontalLookAngle, maxHorizontalLookAngle);
        pitch = Mathf.Clamp(pitch, -maxVerticalLookAngle, maxVerticalLookAngle);
        return restOrient * (Quaternion.Euler(-pitch, yaw, 0f) * Vector3.forward);
    }


    private bool TryGetPointerWorldPoint(Camera cam, out Vector3 pointerWorld)
    {
        pointerWorld = default;
        if (!TryReadPointerScreenPosition(out Vector2 screen))
        {
            return false;
        }

        Ray ray = cam.ScreenPointToRay(screen);
        Vector3 planeNormal = -cam.transform.forward;
        if (Mathf.Abs(Vector3.Dot(planeNormal, ray.direction)) < 0.0001f)
        {
            return false;
        }

        var plane = new Plane(planeNormal, eyeball.position);
        if (!plane.Raycast(ray, out float enter))
        {
            return false;
        }

        pointerWorld = ray.GetPoint(enter);
        return true;
    }

    private static bool TryReadPointerScreenPosition(out Vector2 screen)
    {
        screen = default;
        Pointer pointer = Pointer.current;
        if (pointer != null)
        {
            screen = pointer.position.ReadValue();
            return true;
        }

        Mouse mouse = Mouse.current;
        if (mouse != null)
        {
            screen = mouse.position.ReadValue();
            return true;
        }

        return false;
    }

    private void ResolveReferences()
    {
        if (eyeball == null)
        {
            Transform named = transform.Find("human eyeball");
            eyeball = named != null ? named : FindInnerGlobe();
        }

        if (lookCamera == null)
        {
            lookCamera = Camera.main;
        }

        if (localLookAxis.sqrMagnitude < 0.0001f)
        {
            localLookAxis = Vector3.down;
        }
    }

    private Transform FindInnerGlobe()
    {
        MeshFilter[] filters = GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < filters.Length; i++)
        {
            MeshFilter filter = filters[i];
            if (filter == null || filter.GetComponent<SkinnedMeshRenderer>() != null)
            {
                continue;
            }

            if (filter.transform != transform)
            {
                return filter.transform;
            }
        }

        return null;
    }

    private void CaptureRestPose()
    {
        if (eyeball == null || restCaptured)
        {
            return;
        }

        restLocalRotation = eyeball.localRotation;
        restCaptured = true;
    }

    private void ApplyRestPose()
    {
        if (eyeball != null && restCaptured)
        {
            eyeball.localRotation = restLocalRotation;
        }
    }

    private void OnValidate()
    {
        lookRadius = Mathf.Max(MinimumLookRadius, lookRadius);
        maxHorizontalLookAngle = Mathf.Clamp(maxHorizontalLookAngle, 1f, 55f);
        maxVerticalLookAngle = Mathf.Clamp(maxVerticalLookAngle, 1f, 45f);
        lookSpeed = Mathf.Max(MinimumSmoothing, lookSpeed);
        returnSpeed = Mathf.Max(MinimumSmoothing, returnSpeed);
        if (localLookAxis.sqrMagnitude < 0.0001f)
        {
            localLookAxis = Vector3.down;
        }
    }
}
