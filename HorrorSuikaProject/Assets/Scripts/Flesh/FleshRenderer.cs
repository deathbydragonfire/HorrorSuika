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
/// The field is analytic: each instance contributes a sphere described by two vectors, so there is
/// no volume texture to bind and no rotation to upload.
/// </summary>
[DefaultExecutionOrder(100)]
[ExecuteAlways]
public class FleshRenderer : MonoBehaviour
{
    /// <summary>Hard instance ceiling. Must match MAX_FLESH_INSTANCES in FleshSDF.hlsl.</summary>
    public const int MaxInstances = 48;

    private const float MinimumProxyMargin = 0.01f;
    private const float ProxyEpsilonMargin = 8f;
    private const int MinRaymarchSteps = 8;
    private const int MaxRaymarchStepCeiling = 128;
    private const float MaxPulseDeltaSeconds = 0.1f;

    private static readonly List<FleshVisualComponent> Registered = new List<FleshVisualComponent>(MaxInstances);

    private static readonly int SphereId = Shader.PropertyToID("_FleshSphere");
    private static readonly int ColorId = Shader.PropertyToID("_FleshColor");
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

    private float pulseTime;
    private float lastPulseSampleTime;
    private bool hasPulseSampleTime;

    private bool warnedInstanceOverflow;

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
    }

    private void UpdateFleshData()
    {
        EnsureBuffers();
        AdvancePulseClock();

        // A Vector4 array upload bypasses the automatic gamma-to-linear conversion that
        // Material.color performs, so the colour space has to be applied here by hand.
        bool linearColorSpace = QualitySettings.activeColorSpace == ColorSpace.Linear;

        int count = 0;
        Vector3 clusterMin = Vector3.positiveInfinity;
        Vector3 clusterMax = Vector3.negativeInfinity;

        for (int i = 0; i < Registered.Count; i++)
        {
            FleshVisualComponent component = Registered[i];
            if (component == null || !component.isActiveAndEnabled)
            {
                continue;
            }

            if (count >= MaxInstances)
            {
                if (!warnedInstanceOverflow)
                {
                    warnedInstanceOverflow = true;
                    Debug.LogWarning($"{name}: more than {MaxInstances} flesh instances are active; extras are not rendered.", this);
                }

                break;
            }

            Vector3 worldPosition = component.transform.position;
            float sphereRadius = pulseEnabled ? component.GetPulsedSphereRadius(pulseTime) : component.SphereRadius;
            float blendRadius = component.BlendRadius;
            Color surfaceColor = linearColorSpace ? component.SurfaceColor.linear : component.SurfaceColor;

            sphereData[count] = new Vector4(worldPosition.x, worldPosition.y, worldPosition.z, sphereRadius);
            colorData[count] = new Vector4(surfaceColor.r, surfaceColor.g, surfaceColor.b, blendRadius);

            float bound = sphereRadius + blendRadius;
            Vector3 radius = new Vector3(bound, bound, bound);
            clusterMin = Vector3.Min(clusterMin, worldPosition - radius);
            clusterMax = Vector3.Max(clusterMax, worldPosition + radius);
            count++;
        }

        if (count == 0)
        {
            Shader.SetGlobalInt(CountId, 0);
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
        Shader.SetGlobalVector(BoundsMinId, new Vector4(clusterMin.x, clusterMin.y, clusterMin.z, 0f));
        Shader.SetGlobalVector(BoundsMaxId, new Vector4(clusterMax.x, clusterMax.y, clusterMax.z, 0f));
        Shader.SetGlobalInt(CountId, count);
        Shader.SetGlobalInt(MaxStepsId, Mathf.Clamp(maxRaymarchSteps, MinRaymarchSteps, MaxRaymarchStepCeiling));
        Shader.SetGlobalFloat(SurfaceEpsilonId, Mathf.Max(surfaceEpsilon, 1e-5f));
        Shader.SetGlobalInt(DebugModeId, (int)debugMode);

        UpdateProxy(clusterMin, clusterMax);
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
            return;
        }

        float delta = Mathf.Clamp(now - lastPulseSampleTime, 0f, MaxPulseDeltaSeconds);
        lastPulseSampleTime = now;
        pulseTime += delta * Mathf.Max(pulseTimeScale, 0f);
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
