using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// World-space facing directions of eyeballs on a merge item, captured before the item is despawned.
/// </summary>
public readonly struct MergeItemEyeballLayout
{
    public readonly Vector3[] WorldDirections;

    public MergeItemEyeballLayout(Vector3[] worldDirections)
    {
        WorldDirections = worldDirections ?? System.Array.Empty<Vector3>();
    }

    /// <summary>True when the source item had no eyeballs.</summary>
    public bool IsEmpty => WorldDirections == null || WorldDirections.Length == 0;
}

/// <summary>
/// Spawns decoration eyeballs on the camera-facing side of a merge item and carries both parents'
/// eyes onto the merged result.
/// </summary>
[DisallowMultipleComponent]
public class MergeItemEyeballs : MonoBehaviour
{
    private const float LocalSurfaceRadius = MergeItem.ColliderBaseRadius;
    private const int PlacementAttemptsPerEye = 64;
    private const int SeparationIterations = 18;
    private const float HemisphereEdgeDot = 0.02f;
    private const float SeparationPadding = 1.3f;
    private const float RandomRollDegrees = 22f;

    [SerializeField, Tooltip("Eyeball decoration placed on each merge item.")]
    private GameObject eyeballPrefab;

    [SerializeField, Tooltip("World-space eyeball radius used to keep placements from stacking.")]
    private float eyeballWorldRadius = 0.14f;

    private readonly List<GameObject> pool = new List<GameObject>(16);
    private readonly List<Transform> spawned = new List<Transform>(16);
    private Transform eyeRoot;
    private MergeItem mergeItem;
    private MeshRenderer hostRenderer;

    /// <summary>Active eyeball count currently attached to this item.</summary>
    public int ActiveCount => spawned.Count;

    private void Awake()
    {
        mergeItem = GetComponent<MergeItem>();
        hostRenderer = GetComponent<MeshRenderer>();
    }

    /// <summary>Places one eyeball per tier step anywhere on the camera-facing hemisphere.</summary>
    public void PopulateForNewSpawn(int tierIndex)
    {
        int count = Mathf.Max(1, tierIndex + 1);
        Vector3 facing = ResolveFacingLocal();
        var directions = new List<Vector3>(count);
        for (int i = 0; i < count; i++)
        {
            directions.Add(GenerateHemisphereDirection(facing, directions));
        }

        RelaxDirections(directions, facing, true);
        ApplyDirections(directions);
    }

    /// <summary>Snapshots each eyeball's world facing so a merge can rebuild them on a new item.</summary>
    public MergeItemEyeballLayout CaptureLayout()
    {
        var directions = new Vector3[spawned.Count];
        for (int i = 0; i < spawned.Count; i++)
        {
            Transform eye = spawned[i];
            if (eye == null)
            {
                directions[i] = Vector3.back;
                continue;
            }

            Vector3 worldDir = eye.position - transform.position;
            directions[i] = worldDir.sqrMagnitude > 0.0001f ? worldDir.normalized : ResolveFacingWorld();
        }

        return new MergeItemEyeballLayout(directions);
    }

    /// <summary>Rebuilds this item's eyeballs from both merge parents.</summary>
    public void ApplyInherited(MergeItemEyeballLayout first, MergeItemEyeballLayout second)
    {
        var localDirections = new List<Vector3>(16);
        AppendWorldDirections(first, localDirections);
        AppendWorldDirections(second, localDirections);
        if (localDirections.Count == 0)
        {
            PopulateForNewSpawn(mergeItem != null ? mergeItem.TierIndex : 0);
            return;
        }

        RelaxDirections(localDirections, ResolveFacingLocal(), false);
        ApplyDirections(localDirections);
    }

    /// <summary>Hides every spawned eyeball so the merge item can return to its pool.</summary>
    public void Clear()
    {
        for (int i = 0; i < pool.Count; i++)
        {
            if (pool[i] != null)
            {
                pool[i].SetActive(false);
            }
        }

        spawned.Clear();
    }

    private void AppendWorldDirections(MergeItemEyeballLayout layout, List<Vector3> localDirections)
    {
        if (layout.IsEmpty)
        {
            return;
        }

        for (int i = 0; i < layout.WorldDirections.Length; i++)
        {
            Vector3 worldDir = layout.WorldDirections[i];
            Vector3 local = transform.InverseTransformDirection(worldDir);
            if (local.sqrMagnitude < 0.0001f)
            {
                local = ResolveFacingLocal();
            }

            localDirections.Add(local.normalized);
        }
    }

    private void ApplyDirections(List<Vector3> localDirections)
    {
        EnsureEyeRoot();
        EnsurePoolSize(localDirections.Count);
        spawned.Clear();

        float itemScale = Mathf.Max(transform.localScale.x, 0.0001f);
        Vector3 compensatedScale = Vector3.one / itemScale;

        for (int i = 0; i < localDirections.Count; i++)
        {
            GameObject instance = pool[i];
            instance.SetActive(true);
            PlaceEye(instance.transform, localDirections[i].normalized, compensatedScale);
            spawned.Add(instance.transform);
        }

        for (int i = localDirections.Count; i < pool.Count; i++)
        {
            if (pool[i] != null)
            {
                pool[i].SetActive(false);
            }
        }
    }

    private void PlaceEye(Transform eye, Vector3 localDirection, Vector3 compensatedScale)
    {
        eye.localPosition = localDirection * LocalSurfaceRadius;
        eye.localScale = compensatedScale;

        Vector3 up = Vector3.up;
        if (Mathf.Abs(Vector3.Dot(up, localDirection)) > 0.94f)
        {
            up = Vector3.right;
        }

        float roll = Random.Range(-RandomRollDegrees, RandomRollDegrees);
        eye.localRotation = Quaternion.LookRotation(localDirection, up) * Quaternion.AngleAxis(roll, Vector3.forward);
        ApplyHostMaterialToEyelid(eye);
    }

    private void ApplyHostMaterialToEyelid(Transform eye)
    {
        if (hostRenderer == null)
        {
            hostRenderer = GetComponent<MeshRenderer>();
        }

        Material hostMaterial = hostRenderer != null ? hostRenderer.sharedMaterial : null;
        if (hostMaterial == null)
        {
            return;
        }

        SkinnedMeshRenderer eyelid = null;
        EyeballBlink blink = eye.GetComponent<EyeballBlink>();
        if (blink != null)
        {
            eyelid = blink.EyelidRenderer;
        }

        if (eyelid == null)
        {
            eyelid = eye.GetComponentInChildren<SkinnedMeshRenderer>(true);
        }

        if (eyelid != null)
        {
            eyelid.sharedMaterial = hostMaterial;
        }
    }

    private Vector3 GenerateHemisphereDirection(Vector3 facing, List<Vector3> existing)
    {
        float minDot = ComputeMinimumSeparationDot();
        Vector3 best = SampleFacingHemisphere(facing);
        float bestNearestDot = 2f;

        for (int attempt = 0; attempt < PlacementAttemptsPerEye; attempt++)
        {
            Vector3 candidate = SampleFacingHemisphere(facing);
            float nearestDot = -1f;
            for (int i = 0; i < existing.Count; i++)
            {
                nearestDot = Mathf.Max(nearestDot, Vector3.Dot(candidate, existing[i]));
            }

            bool separated = existing.Count == 0 || nearestDot <= minDot;
            if (separated)
            {
                return candidate;
            }

            if (nearestDot < bestNearestDot)
            {
                bestNearestDot = nearestDot;
                best = candidate;
            }
        }

        return best;
    }

    private void RelaxDirections(List<Vector3> directions, Vector3 facing, bool clampToFacingHemisphere)
    {
        if (directions.Count < 2)
        {
            return;
        }

        float minDot = ComputeMinimumSeparationDot();
        for (int iter = 0; iter < SeparationIterations; iter++)
        {
            for (int i = 0; i < directions.Count; i++)
            {
                for (int j = i + 1; j < directions.Count; j++)
                {
                    Vector3 a = directions[i];
                    Vector3 b = directions[j];
                    float pairDot = Vector3.Dot(a, b);
                    if (pairDot <= minDot)
                    {
                        continue;
                    }

                    Vector3 push = a - b;
                    if (push.sqrMagnitude < 0.0001f)
                    {
                        push = Vector3.Cross(a, facing);
                        if (push.sqrMagnitude < 0.0001f)
                        {
                            push = Vector3.Cross(a, Vector3.up);
                        }

                        if (push.sqrMagnitude < 0.0001f)
                        {
                            push = Vector3.right;
                        }
                    }

                    push.Normalize();
                    float strength = (pairDot - minDot) * 0.55f + 0.08f;
                    a = (a + push * strength).normalized;
                    b = (b - push * strength).normalized;
                    if (clampToFacingHemisphere)
                    {
                        a = ClampToHemisphere(a, facing);
                        b = ClampToHemisphere(b, facing);
                    }

                    directions[i] = a;
                    directions[j] = b;
                }
            }
        }
    }

    private static Vector3 SampleFacingHemisphere(Vector3 facing)
    {
        Vector3 sample = Random.onUnitSphere;
        if (Vector3.Dot(sample, facing) < 0f)
        {
            sample = -sample;
        }

        return sample.normalized;
    }

    private static Vector3 ClampToHemisphere(Vector3 direction, Vector3 facing)
    {
        float facingDot = Vector3.Dot(direction, facing);
        if (facingDot >= HemisphereEdgeDot)
        {
            return direction.normalized;
        }

        Vector3 projected = direction - (facing * facingDot);
        if (projected.sqrMagnitude < 0.0001f)
        {
            projected = Vector3.Cross(facing, Vector3.up);
        }

        if (projected.sqrMagnitude < 0.0001f)
        {
            projected = Vector3.Cross(facing, Vector3.right);
        }

        return (projected.normalized + (facing * HemisphereEdgeDot)).normalized;
    }

    private float ComputeMinimumSeparationDot()
    {
        float itemRadius = mergeItem != null ? Mathf.Max(mergeItem.Radius, 0.01f) : 0.2f;
        float minChord = eyeballWorldRadius * 2f * SeparationPadding;
        float sinHalf = (minChord * 0.5f) / itemRadius;
        if (sinHalf >= 0.999f)
        {
            return -0.15f;
        }

        return Mathf.Cos(2f * Mathf.Asin(sinHalf));
    }

    private Vector3 ResolveFacingLocal()
    {
        return transform.InverseTransformDirection(ResolveFacingWorld()).normalized;
    }

    private static Vector3 ResolveFacingWorld()
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            return Vector3.back;
        }

        return -cam.transform.forward;
    }

    private void EnsureEyeRoot()
    {
        if (eyeRoot != null)
        {
            return;
        }

        Transform existing = transform.Find("Eyeballs");
        if (existing != null)
        {
            eyeRoot = existing;
            return;
        }

        var rootObject = new GameObject("Eyeballs");
        eyeRoot = rootObject.transform;
        eyeRoot.SetParent(transform, false);
        eyeRoot.localPosition = Vector3.zero;
        eyeRoot.localRotation = Quaternion.identity;
        eyeRoot.localScale = Vector3.one;
    }

    private void EnsurePoolSize(int count)
    {
        if (eyeballPrefab == null)
        {
            Debug.LogWarning($"{nameof(MergeItemEyeballs)}: no eyeball prefab assigned.", this);
            return;
        }

        while (pool.Count < count)
        {
            GameObject instance = Instantiate(eyeballPrefab, eyeRoot);
            instance.name = $"Eyeball_{pool.Count:00}";
            instance.SetActive(false);
            pool.Add(instance);
        }
    }
}
