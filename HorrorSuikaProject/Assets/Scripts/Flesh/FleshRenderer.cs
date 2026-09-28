using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Fragment output selector for bringing up and debugging the raymarcher.</summary>
public enum FleshDebugMode
{
    Shaded = 0,
    Distance = 1,
    Normal = 2,
    StepCount = 3,
    LocalPosition = 4,
    InstanceId = 5
}

/// <summary>
/// Single scene-level owner of the flesh raymarch pass. Packs every registered
/// <see cref="FleshVisualComponent"/> into fixed-length global shader arrays (WebGL2 has no
/// StructuredBuffer in fragment shaders) and sizes the proxy volume the shader is rasterized on.
///
/// The field is analytic: each instance contributes a sphere described by its centre, radius,
/// colour, blend radius, and tier. Same-tier spheres are packed contiguously so the shader can
/// smooth-union inside a tier and hard-union across tiers. There is no volume texture to bind
/// and no rotation to upload.
/// </summary>
[DefaultExecutionOrder(100)]
[ExecuteAlways]
public class FleshRenderer : MonoBehaviour
{
    /// <summary>Hard instance ceiling. Must match MAX_FLESH_INSTANCES in FleshSDF.hlsl.</summary>
    public const int MaxInstances = 48;

    /// <summary>Hard cavity ceiling for mouths and eye sockets. Must match MAX_FLESH_CAVITIES in FleshSDF.hlsl.</summary>
    public const int MaxCavities = 64;

    private const float MinimumProxyMargin = 0.01f;
    private const float ProxyEpsilonMargin = 8f;
    private const int MinRaymarchSteps = 8;
    private const int MaxRaymarchStepCeiling = 128;
    private const float MaxPulseDeltaSeconds = 0.1f;

    private static readonly List<FleshVisualComponent> Registered = new List<FleshVisualComponent>(MaxInstances);
    private static readonly List<FleshMouthCavity> Cavities = new List<FleshMouthCavity>(MaxCavities);

    private static readonly int SphereId = Shader.PropertyToID("_FleshSphere");
    private static readonly int ColorId = Shader.PropertyToID("_FleshColor");
    private static readonly int TierId = Shader.PropertyToID("_FleshTier");
    private static readonly int CavityCenterId = Shader.PropertyToID("_FleshCavityCenter");
    private static readonly int CavityAxisId = Shader.PropertyToID("_FleshCavityAxis");
    private static readonly int CavityCountId = Shader.PropertyToID("_FleshCavityCount");
    private static readonly int BoundsMinId = Shader.PropertyToID("_FleshBoundsMin");
    private static readonly int BoundsMaxId = Shader.PropertyToID("_FleshBoundsMax");
    private static readonly int CountId = Shader.PropertyToID("_FleshCount");
    private static readonly int MaxStepsId = Shader.PropertyToID("_FleshMaxSteps");
    private static readonly int SurfaceEpsilonId = Shader.PropertyToID("_FleshSurfaceEpsilon");
    private static readonly int DebugModeId = Shader.PropertyToID("_FleshDebugMode");

    [Header("Proxy Volume")]
    [SerializeField, Tooltip("Unit-cube GameObject the raymarch material is rasterized on. Driven every frame.")]
    private Transform proxyTransform;

    [SerializeField, Tooltip("Renderer on the proxy cube; disabled while there are no flesh instances.")]
    private MeshRenderer proxyRenderer;

    [Header("Raymarch Tuning")]
    [SerializeField, Range(MinRaymarchSteps, MaxRaymarchStepCeiling), Tooltip("Sphere-tracing step ceiling per pixel. Cost scales with steps times instances. The analytic field is exact, so far fewer steps are needed than a baked one.")]
    private int maxRaymarchSteps = 32;

    [SerializeField, Tooltip("World-space distance at which the march counts as a surface hit.")]
    private float surfaceEpsilon = 0.002f;

    [SerializeField, Tooltip("Fragment output selector. Bring up Distance and StepCount before Shaded.")]
    private FleshDebugMode debugMode = FleshDebugMode.Shaded;

    [Header("Pulse (visual only)")]
    [SerializeField, Tooltip("Master switch for the per-instance breathing layer. Off freezes every instance at its authored radius. Affects rendering only.")]
    private bool pulseEnabled = true;

    [SerializeField, Min(0f), Tooltip("Global multiplier on every instance's pulse rate. 0 stops the pulse mid-beat instead of snapping it back.")]
    private float pulseTimeScale = 1f;

    private Vector4[] sphereData;
    private Vector4[] colorData;
    private float[] tierData;
    private int[] uploadIndices;
    private Vector4[] cavityCenterData;
    private Vector4[] cavityAxisData;

    private float pulseTime;
    private float lastPulseSampleTime;
    private bool hasPulseSampleTime;

    private bool warnedInstanceOverflow;
    private bool warnedCavityOverflow;

    /// <summary>Applies the shared pulse authored on the tier table.</summary>
    public void ApplyPulseSettings(FleshPulseSettings settings)
    {
        if (settings == null)
        {
            return;
        }

        pulseEnabled = settings.Enabled;
        pulseTimeScale = Mathf.Max(settings.TimeScale, 0f);
    }

    /// <summary>Master switch for the cosmetic per-instance pulse.</summary>
    public bool PulseEnabled
    {
        get => pulseEnabled;
        set => pulseEnabled = value;
    }

    /// <summary>Global multiplier on every instance's pulse rate.</summary>
    public float PulseTimeScale
    {
        get => pulseTimeScale;
        set => pulseTimeScale = Mathf.Max(value, 0f);
    }

    /// <summary>Sphere-tracing step ceiling per pixel.</summary>
    public int MaxRaymarchSteps
    {
        get => maxRaymarchSteps;
        set => maxRaymarchSteps = Mathf.Clamp(value, MinRaymarchSteps, MaxRaymarchStepCeiling);
    }

    /// <summary>World-space surface hit threshold.</summary>
    public float SurfaceEpsilon
    {
        get => surfaceEpsilon;
        set => surfaceEpsilon = Mathf.Max(value, 1e-5f);
    }

    /// <summary>
    /// Shared pulse clock in seconds. Visual layers that stick to the breathing surface, such as hair,
    /// sample this after the renderer advances it each frame.
    /// </summary>
    public static float SharedPulseTime { get; private set; }

    /// <summary>Fragment output selector.</summary>
    public FleshDebugMode DebugMode
    {
        get => debugMode;
        set => debugMode = value;
    }

    /// <summary>Adds a flesh visual to the render set. Safe to call before any renderer exists.</summary>
    public static void Register(FleshVisualComponent component)
    {
        if (component == null || Registered.Contains(component))
        {
            return;
        }

        Registered.Add(component);
    }

    /// <summary>Removes a flesh visual from the render set.</summary>
    public static void Unregister(FleshVisualComponent component)
    {
        if (component == null)
        {
            return;
        }

        Registered.Remove(component);
    }

    /// <summary>Adds a mouth hole to the render set. Safe to call before any renderer exists.</summary>
    public static void Register(FleshMouthCavity cavity)
    {
        if (cavity == null || Cavities.Contains(cavity))
        {
            return;
        }

        Cavities.Add(cavity);
    }

    /// <summary>Removes a mouth hole from the render set.</summary>
    public static void Unregister(FleshMouthCavity cavity)
    {
        if (cavity == null)
        {
            return;
        }

        Cavities.Remove(cavity);
    }

    private void Awake()
    {
        EnsureBuffers();
    }

    private void OnEnable()
    {
        hasPulseSampleTime = false;

        if (!Application.isPlaying)
        {
            RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
        }
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;

        Shader.SetGlobalInt(CountId, 0);
        Shader.SetGlobalInt(CavityCountId, 0);

        if (proxyRenderer != null)
        {
            proxyRenderer.enabled = false;
        }
    }

    private void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
    {
        // Edit mode never ticks Update or LateUpdate, so the scene view drives the upload instead.
        UpdateFleshData();
    }

    private void LateUpdate()
    {
        if (Application.isPlaying)
        {
            UpdateFleshData();
        }
    }

    private void EnsureBuffers()
    {
        // Checked independently: a domain reload can restore one array and leave a newly added one null.
        if (sphereData == null || sphereData.Length != MaxInstances)
        {
            sphereData = new Vector4[MaxInstances];
        }

        if (colorData == null || colorData.Length != MaxInstances)
        {
            colorData = new Vector4[MaxInstances];
        }

        if (tierData == null || tierData.Length != MaxInstances)
        {
            tierData = new float[MaxInstances];
        }

        if (uploadIndices == null || uploadIndices.Length != MaxInstances)
        {
            uploadIndices = new int[MaxInstances];
        }

        if (cavityCenterData == null || cavityCenterData.Length != MaxCavities)
        {
            cavityCenterData = new Vector4[MaxCavities];
        }

        if (cavityAxisData == null || cavityAxisData.Length != MaxCavities)
        {
            cavityAxisData = new Vector4[MaxCavities];
        }
    }

    /// <summary>
    /// Writes the active registration indices into <see cref="uploadIndices"/>, then sorts them by
    /// tier. The shader walks that order and only smooth-unions a run of equal tiers.
    /// </summary>
    private int CollectActiveIndices()
    {
        int activeCount = 0;
        for (int i = 0; i < Registered.Count; i++)
        {
            FleshVisualComponent component = Registered[i];
            if (component == null || !component.isActiveAndEnabled)
            {
                continue;
            }

            if (activeCount >= MaxInstances)
            {
                if (!warnedInstanceOverflow)
                {
                    warnedInstanceOverflow = true;
                    Debug.LogWarning($"{name}: more than {MaxInstances} flesh instances are active; extras are not rendered.", this);
                }

                break;
            }

            uploadIndices[activeCount] = i;
            activeCount++;
        }

        for (int i = 1; i < activeCount; i++)
        {
            int key = uploadIndices[i];
            int keyTier = Registered[key].TierIndex;
            int j = i - 1;
            while (j >= 0 && Registered[uploadIndices[j]].TierIndex > keyTier)
            {
                uploadIndices[j + 1] = uploadIndices[j];
                j--;
            }

            uploadIndices[j + 1] = key;
        }

        return activeCount;
    }

    private void UpdateFleshData()
    {
        EnsureBuffers();
        AdvancePulseClock();

        // A Vector4 array upload bypasses the automatic gamma-to-linear conversion that
        // Material.color performs, so the colour space has to be applied here by hand.
        bool linearColorSpace = QualitySettings.activeColorSpace == ColorSpace.Linear;

        int count = CollectActiveIndices();
        Vector3 clusterMin = Vector3.positiveInfinity;
        Vector3 clusterMax = Vector3.negativeInfinity;

        for (int i = 0; i < count; i++)
        {
            FleshVisualComponent component = Registered[uploadIndices[i]];
            Vector3 worldPosition = component.transform.position;
            float sphereRadius = pulseEnabled ? component.GetPulsedSphereRadius(pulseTime) : component.SphereRadius;
            float blendRadius = component.BlendRadius;
            Color surfaceColor = linearColorSpace ? component.SurfaceColor.linear : component.SurfaceColor;

            sphereData[i] = new Vector4(worldPosition.x, worldPosition.y, worldPosition.z, sphereRadius);
            colorData[i] = new Vector4(surfaceColor.r, surfaceColor.g, surfaceColor.b, blendRadius);
            tierData[i] = component.TierIndex;

            float bound = sphereRadius + blendRadius;
            Vector3 radius = new Vector3(bound, bound, bound);
            clusterMin = Vector3.Min(clusterMin, worldPosition - radius);
            clusterMax = Vector3.Max(clusterMax, worldPosition + radius);
        }

        if (count == 0)
        {
            Shader.SetGlobalInt(CountId, 0);
            Shader.SetGlobalInt(CavityCountId, 0);
            if (proxyRenderer != null)
            {
                proxyRenderer.enabled = false;
            }

            return;
        }

        float margin = Mathf.Max(surfaceEpsilon * ProxyEpsilonMargin, MinimumProxyMargin);
        clusterMin -= new Vector3(margin, margin, margin);
        clusterMax += new Vector3(margin, margin, margin);

        Shader.SetGlobalVectorArray(SphereId, sphereData);
        Shader.SetGlobalVectorArray(ColorId, colorData);
        Shader.SetGlobalFloatArray(TierId, tierData);
        Shader.SetGlobalVector(BoundsMinId, new Vector4(clusterMin.x, clusterMin.y, clusterMin.z, 0f));
        Shader.SetGlobalVector(BoundsMaxId, new Vector4(clusterMax.x, clusterMax.y, clusterMax.z, 0f));
        Shader.SetGlobalInt(CountId, count);
        Shader.SetGlobalInt(MaxStepsId, Mathf.Clamp(maxRaymarchSteps, MinRaymarchSteps, MaxRaymarchStepCeiling));
        Shader.SetGlobalFloat(SurfaceEpsilonId, Mathf.Max(surfaceEpsilon, 1e-5f));
        Shader.SetGlobalInt(DebugModeId, (int)debugMode);
        UploadCavities();

        UpdateProxy(clusterMin, clusterMax);
    }

    /// <summary>Packs active mouth holes. The opening stays fixed while a mouth chomps.</summary>
    private void UploadCavities()
    {
        int cavityCount = 0;
        for (int i = 0; i < Cavities.Count; i++)
        {
            FleshMouthCavity cavity = Cavities[i];
            if (cavity == null || !cavity.isActiveAndEnabled)
            {
                continue;
            }

            Vector3 center;
            Vector3 axis;
            float opening;
            float depth;
            if (!cavity.TryGetWorld(out center, out axis, out opening, out depth))
            {
                continue;
            }

            if (cavityCount >= MaxCavities)
            {
                if (!warnedCavityOverflow)
                {
                    warnedCavityOverflow = true;
                    Debug.LogWarning($"{name}: more than {MaxCavities} flesh cavities are active; extras are not cut.", this);
                }

                break;
            }

            cavityCenterData[cavityCount] = new Vector4(center.x, center.y, center.z, opening);
            cavityAxisData[cavityCount] = new Vector4(axis.x, axis.y, axis.z, depth);
            cavityCount++;
        }

        Shader.SetGlobalVectorArray(CavityCenterId, cavityCenterData);
        Shader.SetGlobalVectorArray(CavityAxisId, cavityAxisData);
        Shader.SetGlobalInt(CavityCountId, cavityCount);
    }

    /// <summary>
    /// Integrates the shared pulse clock from an unscaled wall clock so the beat keeps its rate in
    /// edit mode, where neither Update nor Time.deltaTime run, and so changing the time scale bends
    /// the beat instead of teleporting it.
    /// </summary>
    private void AdvancePulseClock()
    {
        float now = Time.realtimeSinceStartup;

        if (!hasPulseSampleTime)
        {
            hasPulseSampleTime = true;
            lastPulseSampleTime = now;
            SharedPulseTime = pulseTime;
            return;
        }

        float delta = Mathf.Clamp(now - lastPulseSampleTime, 0f, MaxPulseDeltaSeconds);
        lastPulseSampleTime = now;
        pulseTime += delta * Mathf.Max(pulseTimeScale, 0f);
        SharedPulseTime = pulseTime;
    }

    private void UpdateProxy(Vector3 clusterMin, Vector3 clusterMax)
    {
        if (proxyTransform == null)
        {
            return;
        }

        proxyTransform.position = (clusterMin + clusterMax) * 0.5f;
        proxyTransform.rotation = Quaternion.identity;
        proxyTransform.localScale = clusterMax - clusterMin;

        if (proxyRenderer != null)
        {
            proxyRenderer.enabled = true;
        }
    }

    private void OnValidate()
    {
        maxRaymarchSteps = Mathf.Clamp(maxRaymarchSteps, MinRaymarchSteps, MaxRaymarchStepCeiling);
        surfaceEpsilon = Mathf.Max(surfaceEpsilon, 1e-5f);
        pulseTimeScale = Mathf.Max(pulseTimeScale, 0f);

        if (proxyRenderer == null && proxyTransform != null)
        {
            proxyTransform.TryGetComponent(out proxyRenderer);
        }
    }
}
