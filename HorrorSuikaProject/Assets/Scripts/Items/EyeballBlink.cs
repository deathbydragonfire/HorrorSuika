using UnityEngine;

/// <summary>
/// Blinks the Human Eyeball eyelid blend shape on a timed interval with random variance.
/// </summary>
[DisallowMultipleComponent]
public class EyeballBlink : MonoBehaviour
{
    private const string DefaultBlendShapeName = "eye open";
    private const float MinimumWaitSeconds = 0.1f;
    private const float ClosedPhase = 0.45f;

    [SerializeField, Tooltip("Eyelid renderer with the blink blend shape. Leave empty to find one on this object or its children.")]
    private SkinnedMeshRenderer eyelidRenderer;

    [SerializeField, Tooltip("Blend shape that is fully open at 100 and closed at 0.")]
    private string blendShapeName = DefaultBlendShapeName;

    [SerializeField, Min(0.1f), Tooltip("Average seconds between blinks.")]
    private float blinkInterval = 4f;

    [SerializeField, Min(0f), Tooltip("Random seconds added or subtracted from Blink Interval. The wait is clamped so it never goes below 0.1s.")]
    private float intervalVariance = 1.5f;

    [SerializeField, Min(0.04f), Tooltip("Seconds for one close-and-open blink.")]
    private float blinkDuration = 0.12f;

    private int blendShapeIndex = -1;
    private float waitRemaining;
    private float blinkElapsed;
    private bool blinking;

    /// <summary>Eyelid skinned mesh used for blinking. Resolved from children when unassigned.</summary>
    public SkinnedMeshRenderer EyelidRenderer
    {
        get
        {
            ResolveEyelid();
            return eyelidRenderer;
        }
    }

    /// <summary>Average seconds between blinks.</summary>
    public float BlinkInterval
    {
        get => blinkInterval;
        set => blinkInterval = Mathf.Max(0.1f, value);
    }

    /// <summary>Random plus-or-minus offset applied to <see cref="BlinkInterval"/>.</summary>
    public float IntervalVariance
    {
        get => intervalVariance;
        set => intervalVariance = Mathf.Max(0f, value);
    }

    private void Awake()
    {
        ResolveEyelid();
    }

    private void OnEnable()
    {
        ResolveEyelid();
        ApplyOpen();
        blinking = false;
        ScheduleNextBlink();
    }

    private void OnDisable()
    {
        ApplyOpen();
        blinking = false;
    }

    private void Update()
    {
        if (blendShapeIndex < 0 || eyelidRenderer == null)
        {
            return;
        }

        if (blinking)
        {
            AdvanceBlink();
            return;
        }

        waitRemaining -= Time.deltaTime;
        if (waitRemaining <= 0f)
        {
            blinking = true;
            blinkElapsed = 0f;
        }
    }

    private void AdvanceBlink()
    {
        blinkElapsed += Time.deltaTime;
        float duration = Mathf.Max(0.04f, blinkDuration);
        float t = Mathf.Clamp01(blinkElapsed / duration);
        float closeAmount;
        if (t < ClosedPhase)
        {
            closeAmount = t / ClosedPhase;
        }
        else
        {
            closeAmount = 1f - ((t - ClosedPhase) / (1f - ClosedPhase));
        }

        closeAmount = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(closeAmount));
        eyelidRenderer.SetBlendShapeWeight(blendShapeIndex, Mathf.Lerp(100f, 0f, closeAmount));

        if (t >= 1f)
        {
            ApplyOpen();
            blinking = false;
            ScheduleNextBlink();
        }
    }

    private void ScheduleNextBlink()
    {
        float wait = blinkInterval + Random.Range(-intervalVariance, intervalVariance);
        waitRemaining = Mathf.Max(MinimumWaitSeconds, wait);
    }

    private void ApplyOpen()
    {
        if (eyelidRenderer != null && blendShapeIndex >= 0)
        {
            eyelidRenderer.SetBlendShapeWeight(blendShapeIndex, 100f);
        }
    }

    private void ResolveEyelid()
    {
        if (eyelidRenderer == null)
        {
            eyelidRenderer = GetComponentInChildren<SkinnedMeshRenderer>(true);
        }

        blendShapeIndex = -1;
        if (eyelidRenderer == null || eyelidRenderer.sharedMesh == null)
        {
            return;
        }

        Mesh mesh = eyelidRenderer.sharedMesh;
        string targetName = string.IsNullOrEmpty(blendShapeName) ? DefaultBlendShapeName : blendShapeName;
        for (int i = 0; i < mesh.blendShapeCount; i++)
        {
            if (mesh.GetBlendShapeName(i) == targetName)
            {
                blendShapeIndex = i;
                return;
            }
        }
    }

    private void OnValidate()
    {
        blinkInterval = Mathf.Max(0.1f, blinkInterval);
        intervalVariance = Mathf.Max(0f, intervalVariance);
        blinkDuration = Mathf.Max(0.04f, blinkDuration);
        if (string.IsNullOrEmpty(blendShapeName))
        {
            blendShapeName = DefaultBlendShapeName;
        }
    }
}
