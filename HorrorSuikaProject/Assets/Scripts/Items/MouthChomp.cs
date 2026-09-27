using UnityEngine;

/// <summary>
/// Chomps the mouth blend shape on a timed interval with random variance.
/// </summary>
[DisallowMultipleComponent]
public class MouthChomp : MonoBehaviour
{
    private const string DefaultBlendShapeName = "mouth close";
    private const float MinimumWaitSeconds = 0.1f;
    private const float ClosePhase = 0.35f;
    private const float HoldPhase = 0.55f;

    [SerializeField, Tooltip("Mouth renderer with the chomp blend shape. Leave empty to find one on this object or its children.")]
    private SkinnedMeshRenderer mouthRenderer;

    [SerializeField, Tooltip("Blend shape that is fully open at 0 and closed at 100.")]
    private string blendShapeName = DefaultBlendShapeName;

    [SerializeField, Min(0.1f), Tooltip("Average seconds between chomps.")]
    private float chompInterval = 4f;

    [SerializeField, Min(0f), Tooltip("Random seconds added or subtracted from Chomp Interval. The wait is clamped so it never goes below 0.1s.")]
    private float intervalVariance = 1.5f;

    [SerializeField, Min(0.04f), Tooltip("Seconds for one close, hold, and open.")]
    private float chompDuration = 0.4f;

    private int blendShapeIndex = -1;
    private float waitRemaining;
    private float chompElapsed;
    private bool chomping;

    /// <summary>Mouth skinned mesh used for chomping. Resolved from children when unassigned.</summary>
    public SkinnedMeshRenderer MouthRenderer
    {
        get
        {
            ResolveMouth();
            return mouthRenderer;
        }
    }

    /// <summary>Average seconds between chomps.</summary>
    public float ChompInterval
    {
        get => chompInterval;
        set => chompInterval = Mathf.Max(0.1f, value);
    }

    /// <summary>Random plus-or-minus offset applied to <see cref="ChompInterval"/>.</summary>
    public float IntervalVariance
    {
        get => intervalVariance;
        set => intervalVariance = Mathf.Max(0f, value);
    }

    private void Awake()
    {
        ResolveMouth();
    }

    private void OnEnable()
    {
        ResolveMouth();
        ApplyOpen();
        chomping = false;
        ScheduleNextChomp();
    }

    private void OnDisable()
    {
        ApplyOpen();
        chomping = false;
    }

    private void Update()
    {
        if (blendShapeIndex < 0 || mouthRenderer == null)
        {
            return;
        }

        if (chomping)
        {
            AdvanceChomp();
            return;
        }

        waitRemaining -= Time.deltaTime;
        if (waitRemaining <= 0f)
        {
            chomping = true;
            chompElapsed = 0f;
        }
    }

    private void AdvanceChomp()
    {
        chompElapsed += Time.deltaTime;
        float duration = Mathf.Max(0.04f, chompDuration);
        float t = Mathf.Clamp01(chompElapsed / duration);
        float closeAmount;
        if (t < ClosePhase)
        {
            closeAmount = t / ClosePhase;
        }
        else if (t < HoldPhase)
        {
            closeAmount = 1f;
        }
        else
        {
            closeAmount = 1f - ((t - HoldPhase) / (1f - HoldPhase));
        }

        closeAmount = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(closeAmount));
        mouthRenderer.SetBlendShapeWeight(blendShapeIndex, Mathf.Lerp(0f, 100f, closeAmount));

        if (t >= 1f)
        {
            ApplyOpen();
            chomping = false;
            ScheduleNextChomp();
        }
    }

    private void ScheduleNextChomp()
    {
        float wait = chompInterval + Random.Range(-intervalVariance, intervalVariance);
        waitRemaining = Mathf.Max(MinimumWaitSeconds, wait);
    }

    private void ApplyOpen()
    {
        if (mouthRenderer != null && blendShapeIndex >= 0)
        {
            mouthRenderer.SetBlendShapeWeight(blendShapeIndex, 0f);
        }
    }

    private void ResolveMouth()
    {
        if (mouthRenderer == null)
        {
            mouthRenderer = GetComponentInChildren<SkinnedMeshRenderer>(true);
        }

        blendShapeIndex = -1;
        if (mouthRenderer == null || mouthRenderer.sharedMesh == null)
        {
            return;
        }

        Mesh mesh = mouthRenderer.sharedMesh;
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
        chompInterval = Mathf.Max(0.1f, chompInterval);
        intervalVariance = Mathf.Max(0f, intervalVariance);
        chompDuration = Mathf.Max(0.04f, chompDuration);
        if (string.IsNullOrEmpty(blendShapeName))
        {
            blendShapeName = DefaultBlendShapeName;
        }
    }
}
