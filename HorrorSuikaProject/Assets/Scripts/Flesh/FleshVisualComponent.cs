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

    private const float Tau = Mathf.PI * 2f;
    private const float MinimumPulseReferenceRadius = 0.01f;
    private const float MinimumSizeFactor = 0.25f;
    private const float MaximumSizeFactor = 4f;
    private const float MaximumResolvedAmplitude = 0.35f;
    private const uint PhaseHashMultiplier = 2654435761u;
    private const uint JitterHashMultiplier = 2246822519u;
    private const float HashNormalizer = 1f / 65535f;

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

    [Header("Pulse (visual only)")]
    [SerializeField, Tooltip("Breathe the rendered radius in and out. Purely cosmetic: the collider and every gameplay radius are untouched.")]
    private bool pulseEnabled = true;

    [SerializeField, Range(0f, 0.35f), Tooltip("Peak radius swing as a fraction of the sphere radius, before the size falloff scales it.")]
    private float pulseAmplitude = 0.06f;

    [SerializeField, Min(0f), Tooltip("Pulse rate in cycles per second for an instance whose radius equals Pulse Reference Radius.")]
    private float pulseFrequency = 0.55f;

    [SerializeField, Min(MinimumPulseReferenceRadius), Tooltip("Sphere radius at which Pulse Frequency and Pulse Amplitude are taken verbatim. Smaller instances beat faster and deeper, larger ones slower and shallower.")]
    private float pulseReferenceRadius = 0.5f;

    [SerializeField, Range(0f, 2f), Tooltip("How strongly size drives the pulse. 0 makes every instance pulse identically; 1 makes frequency and amplitude inversely proportional to radius.")]
    private float pulseSizeFalloff = 0.75f;

    [SerializeField, Range(0f, 1f), Tooltip("Per-instance random spread of the pulse rate, so two items of the same tier never beat in lockstep.")]
    private float pulseFrequencyJitter = 0.3f;

    [SerializeField, Range(0f, 1f), Tooltip("Weight of the second harmonic. Above zero the beat stops reading as a clean sine and gains a fleshier double-thump.")]
    private float pulseHarmonicWeight = 0.35f;

    private MeshRenderer cachedMeshRenderer;
    private bool restoreMeshRendererOnDisable;

    private float phaseOffset01;
    private float frequencyJitter01;
    private bool pulseSeedResolved;

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

    /// <summary>Applies the shared pulse authored on the tier table.</summary>
    public void ApplyPulseSettings(FleshPulseSettings settings)
    {
        if (settings == null)
        {
            return;
        }

        pulseEnabled = settings.Enabled;
        pulseAmplitude = settings.Amplitude;
        pulseFrequency = settings.Frequency;
        pulseReferenceRadius = settings.ReferenceRadius;
        pulseSizeFalloff = settings.SizeFalloff;
        pulseFrequencyJitter = settings.FrequencyJitter;
        pulseHarmonicWeight = settings.HarmonicWeight;
    }

    /// <summary>True when this instance breathes its rendered radius.</summary>
    public bool PulseEnabled
    {
        get => pulseEnabled;
        set => pulseEnabled = value;
    }

    /// <summary>
    /// Rendered sphere radius at the given time, with the cosmetic pulse applied. Nothing in the
    /// physics or merge path reads this, so the pulse can never change collision or gameplay.
    /// </summary>
    /// <param name="time">Shared clock in seconds; every instance offsets it by its own phase.</param>
    public float GetPulsedSphereRadius(float time)
    {
        float radius = SphereRadius;
        if (!pulseEnabled || pulseAmplitude <= 0f || radius <= 0f)
        {
            return radius;
        }

        ResolvePulseSeed();

        // Small instances get a higher rate and a deeper swing from the same falloff exponent.
        float sizeRatio = Mathf.Clamp(pulseReferenceRadius / radius, MinimumSizeFactor, MaximumSizeFactor);
        float sizeFactor = Mathf.Pow(sizeRatio, pulseSizeFalloff);

        float jitter = 1f + pulseFrequencyJitter * (frequencyJitter01 * 2f - 1f);
        float frequency = pulseFrequency * sizeFactor * Mathf.Max(jitter, 0f);
        float amplitude = Mathf.Min(pulseAmplitude * sizeFactor, MaximumResolvedAmplitude);

        float phase = (phaseOffset01 + time * frequency) * Tau;
        float fundamental = Mathf.Sin(phase);
        float harmonic = Mathf.Sin(phase * 2f + phaseOffset01 * Tau);
        float wave = Mathf.Lerp(fundamental, harmonic, pulseHarmonicWeight * 0.5f);

        return radius * (1f + amplitude * wave);
    }

    /// <summary>Deterministic per-instance phase and rate spread, stable for the object's lifetime.</summary>
    private void ResolvePulseSeed()
    {
        if (pulseSeedResolved)
        {
            return;
        }

        uint hash = (uint)GetInstanceID() * PhaseHashMultiplier;
        hash ^= hash >> 15;
        phaseOffset01 = (hash & 0xFFFF) * HashNormalizer;

        hash = (hash ^ 0x9E3779B9u) * JitterHashMultiplier;
        hash ^= hash >> 13;
        frequencyJitter01 = (hash & 0xFFFF) * HashNormalizer;

        pulseSeedResolved = true;
    }

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
        pulseAmplitude = Mathf.Max(pulseAmplitude, 0f);
        pulseFrequency = Mathf.Max(pulseFrequency, 0f);
        pulseReferenceRadius = Mathf.Max(pulseReferenceRadius, MinimumPulseReferenceRadius);

        Vector3 scale = transform.lossyScale;
        float maxComponent = Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
        float minComponent = Mathf.Min(Mathf.Abs(scale.x), Mathf.Min(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
        if (maxComponent - minComponent > NonUniformScaleEpsilon * Mathf.Max(maxComponent, 1f))
        {
            Debug.LogWarning($"{name}: FleshVisualComponent requires a uniform scale; non-uniform scale breaks sphere tracing.", this);
        }
    }
}
