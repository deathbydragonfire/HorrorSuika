using UnityEngine;

/// <summary>
/// Passive per-object emitter of implicit-surface data. Holds no physics state and never moves its
/// transform: it only tells <see cref="FleshRenderer"/> that a flesh sphere should be rendered here.
/// </summary>
[DisallowMultipleComponent]
[ExecuteAlways]
public class FleshVisualComponent : MonoBehaviour
{
    private const float MinimumBlendRadius = 0.001f;
    private const float NonUniformScaleEpsilon = 0.01f;

    [SerializeField, Tooltip("Radius in local space, multiplied by the transform's uniform scale to get the world radius. 0.5 matches Unity's primitive sphere, whose diameter equals the scale.")]
    private float localRadius = 0.5f;

    [SerializeField, Tooltip("World-space smooth-union radius used when merging with neighbouring flesh.")]
    private float blendRadius = 0.15f;

    [SerializeField, Tooltip("Treat Blend Radius as a fraction of this object's uniform scale instead of an absolute world distance. Use this when instance sizes vary widely.")]
    private bool blendRadiusRelativeToScale;

    [SerializeField, Tooltip("Surface colour of this instance. Merged necks average the colours of the instances that form them. Driven from the tier at runtime.")]
    private Color surfaceColor = new Color(0.72f, 0.28f, 0.28f, 1f);

    [SerializeField, Range(0f, 1f), Tooltip("Reserved for the wet specular pass; serialized now, unused by the MVP shader.")]
    private float wetness = 0.5f;

    [SerializeField, Tooltip("Disable any MeshRenderer on this GameObject while the flesh visual is active.")]
    private bool hideSourceRenderer = true;

    private MeshRenderer cachedMeshRenderer;
    private bool restoreMeshRendererOnDisable;

    /// <summary>Local-space sphere radius, before the transform's uniform scale is applied.</summary>
    public float LocalRadius
    {
        get => localRadius;
        set => localRadius = Mathf.Max(value, 0f);
    }

    /// <summary>World-space smooth-union radius, resolved against the transform scale when configured as relative.</summary>
    public float BlendRadius
    {
        get
        {
            float resolved = blendRadiusRelativeToScale ? blendRadius * UniformScale : blendRadius;
            return Mathf.Max(resolved, MinimumBlendRadius);
        }
        set => blendRadius = Mathf.Max(value, MinimumBlendRadius);
    }

    /// <summary>Surface colour of this instance, averaged with neighbours across a merged neck.</summary>
    public Color SurfaceColor
    {
        get => surfaceColor;
        set => surfaceColor = value;
    }

    /// <summary>Reserved wetness parameter for the later material pass.</summary>
    public float Wetness
    {
        get => wetness;
        set => wetness = Mathf.Clamp01(value);
    }

    /// <summary>True when the source MeshRenderer should be hidden while the flesh visual is active.</summary>
    public bool HideSourceRenderer => hideSourceRenderer;

    /// <summary>Largest uniform scale component of the transform. Flesh instances must be uniformly scaled.</summary>
    public float UniformScale
    {
        get
        {
            Vector3 scale = transform.lossyScale;
            return Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
        }
    }

    /// <summary>World-space radius of the flesh sphere this instance contributes.</summary>
    public float SphereRadius => localRadius * UniformScale;

    /// <summary>Conservative world-space bounding radius including the blend skirt.</summary>
    public float VisualRadius => SphereRadius + BlendRadius;

    private void OnEnable()
    {
        if (hideSourceRenderer)
        {
            if (cachedMeshRenderer == null)
            {
                TryGetComponent(out cachedMeshRenderer);
            }

            if (cachedMeshRenderer != null && cachedMeshRenderer.enabled)
            {
                cachedMeshRenderer.enabled = false;
                restoreMeshRendererOnDisable = true;
            }
        }

        FleshRenderer.Register(this);
    }

    private void OnDisable()
    {
        FleshRenderer.Unregister(this);

        if (restoreMeshRendererOnDisable && cachedMeshRenderer != null)
        {
            cachedMeshRenderer.enabled = true;
        }

        restoreMeshRendererOnDisable = false;
    }

    private void OnValidate()
    {
        localRadius = Mathf.Max(localRadius, 0f);
        blendRadius = Mathf.Max(blendRadius, MinimumBlendRadius);
        wetness = Mathf.Clamp01(wetness);

        Vector3 scale = transform.lossyScale;
        float maxComponent = Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
        float minComponent = Mathf.Min(Mathf.Abs(scale.x), Mathf.Min(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
        if (maxComponent - minComponent > NonUniformScaleEpsilon * Mathf.Max(maxComponent, 1f))
        {
            Debug.LogWarning($"{name}: FleshVisualComponent requires a uniform scale; non-uniform scale breaks sphere tracing.", this);
        }
    }
}
