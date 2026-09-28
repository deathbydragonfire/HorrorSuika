using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Sparse body-hair coat on a merge blob. Visual only: no collider. <see cref="Hairiness"/> chooses
/// how many strands show. Stroke width stays constant, and a value below the minimum is stored as bare.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(220)]
public class MergeItemHair : MonoBehaviour
{
    private const int StrandCount = 150;
    private const int SegmentsPerStrand = 3;
    private const float SphereRadius = MergeItem.ColliderBaseRadius;
    private const float SurfacePadding = 0.008f;
    private const float MinLength = 0.12f;
    private const float MaxLength = 0.28f;
    private const float StrandWidth = 0.008f;
    private const float BaldEpsilon = 0.001f;
    private const float DefaultMinimumHairiness = 0.2f;

    private static readonly int HairinessId = Shader.PropertyToID("_Hairiness");
    private static Mesh sharedMesh;

    [SerializeField, Tooltip("Shared hair material. Hairiness is applied per blob, not by duplicating this asset.")]
    private Material hairMaterial;

    [SerializeField, Range(0f, 1f), Tooltip("Fraction of strands shown. 0% is bare. Stroke width does not change. Values under the tier table minimum are stored as 0%.")]
    private float hairiness;

    private float minimumHairiness = DefaultMinimumHairiness;

    private Transform hairRoot;
    private MeshRenderer hairRenderer;
    private FleshVisualComponent flesh;
    private MaterialPropertyBlock propertyBlock;

    /// <summary>Fraction of strands shown, from 0 (bare) to 1 (every strand). Below the minimum this is 0.</summary>
    public float Hairiness => hairiness;

    /// <summary>Rolls a coat for a newly dropped blob. Merges do not call this.</summary>
    public void RollForNewSpawn(MergeItemTierTable table)
    {
        ApplyMinimum(table);
        if (table == null || Random.value >= table.HairChance)
        {
            SetHairiness(0f);
            return;
        }

        float min = table.MinRolledHairiness;
        float max = Mathf.Max(min, table.MaxRolledHairiness);
        SetHairiness(Random.Range(min, max));
    }

    /// <summary>
    /// Shows this fraction of the strands, spread evenly. A value under the minimum is stored as bare.
    /// Merges raise any non-bare pair up to the minimum before calling this.
    /// </summary>
    public void SetHairiness(float value)
    {
        hairiness = Sanitize(value);
        ApplyVisual(assignSpin: true);
    }

    /// <summary>Hides the coat so the blob can return to the pool.</summary>
    public void Clear()
    {
        hairiness = 0f;
        if (hairRenderer != null)
        {
            hairRenderer.enabled = false;
        }

        enabled = false;
    }

    private void LateUpdate()
    {
        if (hairRoot == null || hairRenderer == null || !hairRenderer.enabled || flesh == null)
        {
            return;
        }

        float authored = flesh.SphereRadius;
        if (authored <= 0.0001f)
        {
            hairRoot.localScale = Vector3.one;
            return;
        }

        // The flesh pulse changes the rendered radius without touching the collider transform.
        float pulsed = flesh.GetPulsedSphereRadius(FleshRenderer.SharedPulseTime);
        float factor = pulsed / authored;
        hairRoot.localScale = new Vector3(factor, factor, factor);
    }

    private void OnValidate()
    {
        hairiness = Sanitize(hairiness);
        if (hairRoot != null)
        {
            ApplyVisual(assignSpin: false);
        }
    }

    private void ApplyMinimum(MergeItemTierTable table)
    {
        minimumHairiness = table != null ? table.MinimumHairiness : DefaultMinimumHairiness;
    }

    private float Sanitize(float value)
    {
        value = Mathf.Clamp01(value);
        if (value < minimumHairiness)
        {
            return 0f;
        }

        return value;
    }

    private void ApplyVisual(bool assignSpin)
    {
        EnsureVisual();
        if (hairRenderer == null)
        {
            return;
        }

        bool show = hairiness > BaldEpsilon;
        hairRenderer.enabled = show;
        enabled = show;
        if (!show)
        {
            return;
        }

        if (propertyBlock == null)
        {
            propertyBlock = new MaterialPropertyBlock();
        }

        hairRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetFloat(HairinessId, hairiness);
        hairRenderer.SetPropertyBlock(propertyBlock);

        if (assignSpin)
        {
            // A small twist so blobs are not identical. A full spin turns the downward growth into a halo.
            hairRoot.localRotation = Quaternion.Euler(0f, 0f, Random.Range(-18f, 18f));
        }
    }

    private void EnsureVisual()
    {
        if (flesh == null)
        {
            flesh = GetComponent<FleshVisualComponent>();
        }

        if (hairRoot != null)
        {
            return;
        }

        Transform existing = transform.Find("Hair");
        if (existing != null)
        {
            hairRoot = existing;
            hairRenderer = existing.GetComponent<MeshRenderer>();
            return;
        }

        Material material = ResolveMaterial();
        if (material == null)
        {
            return;
        }

        var rootObject = new GameObject("Hair");
        hairRoot = rootObject.transform;
        hairRoot.SetParent(transform, false);
        hairRoot.localPosition = Vector3.zero;
        hairRoot.localRotation = Quaternion.identity;
        hairRoot.localScale = Vector3.one;

        var filter = rootObject.AddComponent<MeshFilter>();
        filter.sharedMesh = SharedMesh();

        hairRenderer = rootObject.AddComponent<MeshRenderer>();
        hairRenderer.sharedMaterial = material;
        hairRenderer.shadowCastingMode = ShadowCastingMode.Off;
        hairRenderer.receiveShadows = false;
        hairRenderer.lightProbeUsage = LightProbeUsage.Off;
        hairRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        hairRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        hairRenderer.enabled = false;
    }

    private Material ResolveMaterial()
    {
        if (hairMaterial != null)
        {
            return hairMaterial;
        }

        Shader shader = Shader.Find("HorrorSuika/HairStrands");
        if (shader == null)
        {
            Debug.LogWarning($"{nameof(MergeItemHair)}: no hair material is assigned and {nameof(Shader.Find)} missed HorrorSuika/HairStrands.", this);
            return null;
        }

        hairMaterial = new Material(shader)
        {
            name = "HairStrands (Runtime)"
        };
        return hairMaterial;
    }

    private static Mesh SharedMesh()
    {
        if (sharedMesh != null)
        {
            return sharedMesh;
        }

        sharedMesh = BuildMesh();
        return sharedMesh;
    }

    /// <summary>
    /// One coat of camera-facing ribbons over the whole sphere. Each strand stores an even rank,
    /// so a lower hairiness keeps a spread-out subset instead of a bald patch.
    /// </summary>
    private static Mesh BuildMesh()
    {
        int ringCount = SegmentsPerStrand + 1;
        int vertsPerStrand = ringCount * 2;
        int vertCount = StrandCount * vertsPerStrand;
        int indexCount = StrandCount * SegmentsPerStrand * 6;

        var positions = new Vector3[vertCount];
        var normals = new Vector3[vertCount];
        var tangents = new Vector4[vertCount];
        var uvs = new Vector2[vertCount];
        var uv2s = new Vector2[vertCount];
        var colors = new Color[vertCount];
        var indices = new int[indexCount];

        int vertex = 0;
        int index = 0;
        for (int i = 0; i < StrandCount; i++)
        {
            float band = (i + 0.5f) / StrandCount;
            // z uniform from back to front is an even spread over the whole sphere, not one face.
            float z = Mathf.Lerp(-1f, 1f, band);
            z += (Hash01(i, 8) - 0.5f) * 0.04f;
            z = Mathf.Clamp(z, -1f, 1f);
            float ring = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
            float azimuth = i * 2.39996323f + (Hash01(i, 1) - 0.5f) * 0.35f;
            Vector3 normal = new Vector3(Mathf.Cos(azimuth) * ring, Mathf.Sin(azimuth) * ring, z).normalized;

            Vector3 tangent = Vector3.Cross(normal, Mathf.Abs(normal.y) > 0.9f ? Vector3.right : Vector3.up);
            if (tangent.sqrMagnitude < 0.0001f)
            {
                tangent = Vector3.Cross(normal, Vector3.forward);
            }

            tangent.Normalize();
            Vector3 bitangent = Vector3.Cross(normal, tangent);
            // Each strand picks its own direction so one side of the skin is not combed bare.
            float twist = Hash01(i, 2) * Mathf.PI * 2f;
            Vector3 alongSkin = tangent * Mathf.Cos(twist) + bitangent * Mathf.Sin(twist);

            // Almost flat to the skin. Standing hairs turn the blob into fur.
            float lift = Mathf.Lerp(0.05f, 0.18f, Hash01(i, 3));
            Vector3 grow = (alongSkin * (1f - lift) + normal * lift).normalized;

            Vector3 bend = Vector3.Cross(grow, normal);
            if (bend.sqrMagnitude < 0.0001f)
            {
                bend = alongSkin;
            }

            bend.Normalize();
            if (Vector3.Dot(bend, normal) < 0f)
            {
                bend = -bend;
            }

            float lengthRoll = Hash01(i, 4);
            float lengthT = lengthRoll < 0.15f
                ? (lengthRoll / 0.15f) * 0.25f
                : 0.25f + ((lengthRoll - 0.15f) / 0.85f) * 0.75f;
            float length = Mathf.Lerp(MinLength, MaxLength, lengthT);
            if (Hash01(i, 9) > 0.9f)
            {
                length *= 1.2f;
            }

            float bow = Mathf.Lerp(0.05f, 0.16f, Hash01(i, 5)) * length;
            float shade = Mathf.Lerp(0.35f, 1f, Hash01(i, 7));
            // Rank must not use the same golden angle as the placement, or a mid percent
            // keeps one side of the sphere and drops the other. Merges land in that middle.
            float threshold = SpreadRank(i);

            int strandBase = vertex;
            for (int ringIndex = 0; ringIndex < ringCount; ringIndex++)
            {
                float along = ringIndex / (float)SegmentsPerStrand;
                Vector3 deriv = grow * length + bend * (bow * 2f * along);
                if (deriv.sqrMagnitude < 0.0000001f)
                {
                    deriv = grow;
                }

                deriv.Normalize();
                Vector3 center = PointOnStrand(normal, grow, bend, length, bow, along);
                for (int side = 0; side < 2; side++)
                {
                    positions[vertex] = center;
                    normals[vertex] = normal;
                    tangents[vertex] = new Vector4(deriv.x, deriv.y, deriv.z, 1f);
                    uvs[vertex] = new Vector2(side == 0 ? -1f : 1f, along);
                    uv2s[vertex] = new Vector2(threshold, StrandWidth);
                    colors[vertex] = new Color(shade, 0f, 0f, 1f);
                    vertex++;
                }
            }

            for (int segment = 0; segment < SegmentsPerStrand; segment++)
            {
                int a = strandBase + segment * 2;
                int b = a + 1;
                int c = a + 2;
                int d = a + 3;
                indices[index++] = a;
                indices[index++] = c;
                indices[index++] = b;
                indices[index++] = b;
                indices[index++] = c;
                indices[index++] = d;
            }
        }

        var mesh = new Mesh
        {
            name = "MergeItemHair",
            hideFlags = HideFlags.HideAndDontSave
        };
        mesh.SetVertices(positions);
        mesh.SetNormals(normals);
        mesh.SetTangents(tangents);
        mesh.SetUVs(0, uvs);
        mesh.SetUVs(1, uv2s);
        mesh.SetColors(colors);
        mesh.SetIndices(indices, MeshTopology.Triangles, 0, calculateBounds: true);
        mesh.UploadMeshData(true);
        return mesh;
    }

    private static Vector3 PointOnStrand(Vector3 normal, Vector3 grow, Vector3 bend, float length, float bow, float along)
    {
        Vector3 point = normal * (SphereRadius + SurfacePadding) + grow * (length * along) + bend * (bow * along * along);
        float radial = Vector3.Dot(point, normal);
        float minimum = SphereRadius + SurfacePadding * 0.35f;
        if (radial < minimum)
        {
            point += normal * (minimum - radial);
        }

        return point;
    }

    /// <summary>
    /// Bit-reversed index in 0–1. Any prefix of this rank is spread over the whole sphere.
    /// </summary>
    private static float SpreadRank(int index)
    {
        uint bits = (uint)(index + 1);
        bits = (bits << 16) | (bits >> 16);
        bits = ((bits & 0x00FF00FFu) << 8) | ((bits & 0xFF00FF00u) >> 8);
        bits = ((bits & 0x0F0F0F0Fu) << 4) | ((bits & 0xF0F0F0F0u) >> 4);
        bits = ((bits & 0x33333333u) << 2) | ((bits & 0xCCCCCCCCu) >> 2);
        bits = ((bits & 0x55555555u) << 1) | ((bits & 0xAAAAAAAAu) >> 1);
        return bits * (1f / 4294967296f);
    }

    private static float Hash01(int index, int channel)
    {
        unchecked
        {
            uint hash = (uint)(index + 1) * 747796405u + (uint)(channel + 1) * 2891336453u;
            hash = ((hash >> 16) ^ hash) * 73244475u;
            hash = (hash >> 16) ^ hash;
            return (hash & 65535u) / 65535f;
        }
    }
}
