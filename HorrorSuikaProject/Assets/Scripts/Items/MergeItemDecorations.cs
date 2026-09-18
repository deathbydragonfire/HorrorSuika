using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One decoration instance's world facing, captured before a merge despawns its host item.
/// </summary>
public readonly struct MergeItemDecorationPlacement
{
    public readonly int DefinitionIndex;
    public readonly Vector3 WorldDirection;

    public MergeItemDecorationPlacement(int definitionIndex, Vector3 worldDirection)
    {
        DefinitionIndex = definitionIndex;
        WorldDirection = worldDirection;
    }
}

/// <summary>
/// All decorations on a merge item, captured before the item is despawned.
/// </summary>
public readonly struct MergeItemDecorationLayout
{
    public readonly MergeItemDecorationPlacement[] Placements;

    public MergeItemDecorationLayout(MergeItemDecorationPlacement[] placements)
    {
        Placements = placements ?? System.Array.Empty<MergeItemDecorationPlacement>();
    }

    /// <summary>True when the source item had no decorations.</summary>
    public bool IsEmpty => Placements == null || Placements.Length == 0;
}

/// <summary>
/// Places authored decorations on merge items and carries both parents' instances onto a merge result.
/// </summary>
[DisallowMultipleComponent]
public class MergeItemDecorations : MonoBehaviour
{
    private const float LocalSurfaceRadius = MergeItem.ColliderBaseRadius;
    private const int SeparationIterations = 18;
    private const int ScatterAttemptsPerInstance = 48;
    private const float MinimumFacingDot = 0.55f;
    private const float SeparationPadding = 1.3f;

    private readonly List<List<GameObject>> pools = new List<List<GameObject>>(8);
    private readonly List<ActiveDecoration> spawned = new List<ActiveDecoration>(16);
    private readonly List<int> pendingDefinitionIndices = new List<int>(16);
    private readonly List<Vector3> pendingDirections = new List<Vector3>(16);
    private readonly List<float> pendingRadii = new List<float>(16);

    private Transform decorationRoot;
    private MergeItem mergeItem;
    private MeshRenderer hostRenderer;
    private MergeItemTierTable tierTable;

    private struct ActiveDecoration
    {
        public int DefinitionIndex;
        public Transform Transform;
    }

    /// <summary>Active decoration count currently attached to this item.</summary>
    public int ActiveCount => spawned.Count;

    private void Awake()
    {
        mergeItem = GetComponent<MergeItem>();
        hostRenderer = GetComponent<MeshRenderer>();
    }

    /// <summary>
    /// Rolls drop chances for each authored decoration. Merges still inherit whatever both parents
    /// already had instead of rolling again.
    /// </summary>
    public void PopulateForNewSpawn(MergeItemTierTable table, int tierIndex)
    {
        tierTable = table;
        Clear();
        if (table == null)
        {
            return;
        }

        IReadOnlyList<MergeItemDecorationDefinition> definitions = table.Decorations;
        Vector3 facing = ResolveFacingLocal();
        pendingDefinitionIndices.Clear();
        pendingDirections.Clear();
        pendingRadii.Clear();

        for (int i = 0; i < definitions.Count; i++)
        {
            MergeItemDecorationDefinition definition = definitions[i];
            if (definition == null || !definition.CanDropOnTier(tierIndex))
            {
                continue;
            }

            if (Random.value >= definition.DropChance)
            {
                continue;
            }

            pendingDefinitionIndices.Add(i);
            pendingDirections.Add(facing);
            pendingRadii.Add(definition.WorldRadius);
        }

        if (pendingDefinitionIndices.Count == 0)
        {
            return;
        }

        ScatterOrganic(pendingDirections, pendingRadii, facing);
        RelaxDirections(pendingDirections, pendingRadii, facing);
        ApplyPending();
    }

    /// <summary>Snapshots each decoration's world facing so a merge can rebuild them on a new item.</summary>
    public MergeItemDecorationLayout CaptureLayout()
    {
        var placements = new MergeItemDecorationPlacement[spawned.Count];
        for (int i = 0; i < spawned.Count; i++)
        {
            Transform decoration = spawned[i].Transform;
            Vector3 worldDir = ResolveFacingWorld();
            if (decoration != null)
            {
                Vector3 offset = decoration.position - transform.position;
                if (offset.sqrMagnitude > 0.0001f)
                {
                    worldDir = offset.normalized;
                }
            }

            placements[i] = new MergeItemDecorationPlacement(spawned[i].DefinitionIndex, worldDir);
        }

        return new MergeItemDecorationLayout(placements);
    }

    /// <summary>Rebuilds this item's decorations from both merge parents.</summary>
    public void ApplyInherited(MergeItemTierTable table, MergeItemDecorationLayout first, MergeItemDecorationLayout second)
    {
        tierTable = table;
        Clear();
        if (table == null)
        {
            return;
        }

        pendingDefinitionIndices.Clear();
        pendingDirections.Clear();
        pendingRadii.Clear();
        AppendLayout(first);
        AppendLayout(second);
        if (pendingDefinitionIndices.Count == 0)
        {
            return;
        }

        Vector3 facing = ResolveFacingLocal();
        ScatterOrganic(pendingDirections, pendingRadii, facing);
        RelaxDirections(pendingDirections, pendingRadii, facing);
        ApplyPending();
    }

    /// <summary>Hides every spawned decoration so the merge item can return to its pool.</summary>
    public void Clear()
    {
        for (int i = 0; i < pools.Count; i++)
        {
            List<GameObject> pool = pools[i];
            for (int j = 0; j < pool.Count; j++)
            {
                if (pool[j] != null)
                {
                    pool[j].SetActive(false);
                }
            }
        }

        spawned.Clear();
    }

    private void AppendLayout(MergeItemDecorationLayout layout)
    {
        if (layout.IsEmpty)
        {
            return;
        }

        IReadOnlyList<MergeItemDecorationDefinition> definitions = tierTable.Decorations;
        for (int i = 0; i < layout.Placements.Length; i++)
        {
            MergeItemDecorationPlacement placement = layout.Placements[i];
            if (placement.DefinitionIndex < 0 || placement.DefinitionIndex >= definitions.Count)
            {
                continue;
            }

            MergeItemDecorationDefinition definition = definitions[placement.DefinitionIndex];
            if (definition == null || !definition.InheritOnMerge || definition.Prefab == null)
            {
                continue;
            }

            Vector3 local = transform.InverseTransformDirection(placement.WorldDirection);
            if (local.sqrMagnitude < 0.0001f)
            {
                local = ResolveFacingLocal();
            }

            pendingDefinitionIndices.Add(placement.DefinitionIndex);
            pendingDirections.Add(local.normalized);
            pendingRadii.Add(definition.WorldRadius);
        }
    }

    private void ApplyPending()
    {
        EnsureDecorationRoot();
        EnsurePoolCapacity();

        float itemScale = Mathf.Max(transform.localScale.x, 0.0001f);
        Vector3 facing = ResolveFacingLocal();
        IReadOnlyList<MergeItemDecorationDefinition> definitions = tierTable.Decorations;

        for (int i = 0; i < pendingDefinitionIndices.Count; i++)
        {
            int definitionIndex = pendingDefinitionIndices[i];
            MergeItemDecorationDefinition definition = definitions[definitionIndex];
            GameObject instance = Rent(definitionIndex, definition);
            if (instance == null)
            {
                continue;
            }

            Vector3 compensatedScale = definition.ScaleWithItem ? Vector3.one : Vector3.one / itemScale;
            PlaceDecoration(instance.transform, definition, pendingDirections[i].normalized, facing, compensatedScale);
            spawned.Add(new ActiveDecoration
            {
                DefinitionIndex = definitionIndex,
                Transform = instance.transform
            });
        }
    }

    private void PlaceDecoration(
        Transform decoration,
        MergeItemDecorationDefinition definition,
        Vector3 localDirection,
        Vector3 facing,
        Vector3 compensatedScale)
    {
        decoration.localPosition = localDirection * LocalSurfaceRadius;
        decoration.localScale = compensatedScale;

        Vector3 look = definition.FaceCameraAtRest ? facing : localDirection;
        if (look.sqrMagnitude < 0.0001f)
        {
            look = Vector3.back;
        }

        Vector3 up = Vector3.up;
        if (Mathf.Abs(Vector3.Dot(up, look)) > 0.94f)
        {
            up = Vector3.right;
        }

        float rollRange = definition.RandomRollDegrees;
        float roll = rollRange > 0f ? Random.Range(-rollRange, rollRange) : 0f;
        decoration.localRotation = Quaternion.LookRotation(look, up) * Quaternion.AngleAxis(roll, Vector3.forward);

        if (definition.ApplyHostMaterialToSkinnedMeshes)
        {
            ApplyHostMaterialToSkinnedMeshes(decoration);
        }
    }

    private void ApplyHostMaterialToSkinnedMeshes(Transform decoration)
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

        SkinnedMeshRenderer[] eyelids = decoration.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < eyelids.Length; i++)
        {
            eyelids[i].sharedMaterial = hostMaterial;
        }
    }

    private void RelaxDirections(List<Vector3> directions, List<float> radii, Vector3 facing)
    {
        if (directions.Count < 2)
        {
            return;
        }

        float itemRadius = mergeItem != null ? Mathf.Max(mergeItem.Radius, 0.01f) : 0.2f;
        for (int iter = 0; iter < SeparationIterations; iter++)
        {
            for (int i = 0; i < directions.Count; i++)
            {
                for (int j = i + 1; j < directions.Count; j++)
                {
                    float minDot = ComputeMinimumSeparationDot(itemRadius, radii[i] + radii[j]);
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
                        push = RandomCapTangent(facing);
                    }

                    push.Normalize();
                    float strength = (pairDot - minDot) * 0.55f + 0.08f;
                    directions[i] = ClampToFacingCap((a + push * strength).normalized, facing);
                    directions[j] = ClampToFacingCap((b - push * strength).normalized, facing);
                }
            }
        }
    }

    private void ScatterOrganic(List<Vector3> directions, List<float> radii, Vector3 facing)
    {
        if (directions.Count == 0)
        {
            return;
        }

        if (directions.Count == 1)
        {
            directions[0] = ClampToFacingCap(directions[0], facing);
            return;
        }

        float itemRadius = mergeItem != null ? Mathf.Max(mergeItem.Radius, 0.01f) : 0.2f;
        for (int i = 0; i < directions.Count; i++)
        {
            Vector3 best = SampleFacingCap(facing);
            float bestNearestDot = 2f;

            for (int attempt = 0; attempt < ScatterAttemptsPerInstance; attempt++)
            {
                Vector3 candidate = SampleFacingCap(facing);
                float nearestDot = -1f;
                bool separated = true;
                for (int p = 0; p < i; p++)
                {
                    float pairDot = Vector3.Dot(candidate, directions[p]);
                    nearestDot = Mathf.Max(nearestDot, pairDot);
                    float minDot = ComputeMinimumSeparationDot(itemRadius, radii[i] + radii[p]);
                    if (pairDot > minDot)
                    {
                        separated = false;
                    }
                }

                if (separated)
                {
                    best = candidate;
                    break;
                }

                if (nearestDot < bestNearestDot)
                {
                    bestNearestDot = nearestDot;
                    best = candidate;
                }
            }

            directions[i] = best;
        }
    }

    private static Vector3 SampleFacingCap(Vector3 facing)
    {
        float z = Mathf.Lerp(MinimumFacingDot, 1f, Random.value);
        float azimuth = Random.Range(0f, Mathf.PI * 2f);
        float radial = Mathf.Sqrt(Mathf.Max(0f, 1f - (z * z)));
        Vector3 inFacingSpace = new Vector3(radial * Mathf.Cos(azimuth), radial * Mathf.Sin(azimuth), z);
        return (Quaternion.FromToRotation(Vector3.forward, facing) * inFacingSpace).normalized;
    }

    private static Vector3 RandomCapTangent(Vector3 facing)
    {
        Vector3 tangent = Vector3.Cross(facing, Random.onUnitSphere);
        if (tangent.sqrMagnitude < 0.0001f)
        {
            tangent = Vector3.Cross(facing, Vector3.up);
        }

        if (tangent.sqrMagnitude < 0.0001f)
        {
            tangent = Vector3.right;
        }

        return tangent.normalized;
    }

    private static Vector3 ClampToFacingCap(Vector3 direction, Vector3 facing)
    {
        Vector3 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : facing;
        float facingDot = Vector3.Dot(dir, facing);
        if (facingDot >= MinimumFacingDot)
        {
            return dir;
        }

        Vector3 projected = dir - (facing * facingDot);
        if (projected.sqrMagnitude < 0.0001f)
        {
            return facing;
        }

        float rim = Mathf.Sqrt(Mathf.Max(0f, 1f - (MinimumFacingDot * MinimumFacingDot)));
        return ((projected.normalized * rim) + (facing * MinimumFacingDot)).normalized;
    }

    private static float ComputeMinimumSeparationDot(float itemRadius, float combinedRadii)
    {
        float minChord = combinedRadii * SeparationPadding;
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

    private void EnsureDecorationRoot()
    {
        if (decorationRoot != null)
        {
            return;
        }

        Transform existing = transform.Find("Decorations");
        if (existing != null)
        {
            decorationRoot = existing;
            return;
        }

        var rootObject = new GameObject("Decorations");
        decorationRoot = rootObject.transform;
        decorationRoot.SetParent(transform, false);
        decorationRoot.localPosition = Vector3.zero;
        decorationRoot.localRotation = Quaternion.identity;
        decorationRoot.localScale = Vector3.one;
    }

    private void EnsurePoolCapacity()
    {
        int definitionCount = tierTable != null ? tierTable.Decorations.Count : 0;
        while (pools.Count < definitionCount)
        {
            pools.Add(new List<GameObject>(4));
        }
    }

    private GameObject Rent(int definitionIndex, MergeItemDecorationDefinition definition)
    {
        if (definition == null || definition.Prefab == null)
        {
            return null;
        }

        List<GameObject> pool = pools[definitionIndex];
        for (int i = 0; i < pool.Count; i++)
        {
            GameObject candidate = pool[i];
            if (candidate != null && !candidate.activeSelf)
            {
                candidate.SetActive(true);
                return candidate;
            }
        }

        GameObject instance = Instantiate(definition.Prefab, decorationRoot);
        instance.name = $"{definition.DisplayName}_{pool.Count:00}";
        pool.Add(instance);
        return instance;
    }
}
