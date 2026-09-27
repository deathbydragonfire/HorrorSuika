using UnityEngine;

/// <summary>
/// Idle mouth motion is a slow breath. <see cref="Chomp"/> plays one bite over that breath:
/// open fully, snap shut, then settle back onto the breath.
/// </summary>
[DisallowMultipleComponent]
public class MouthChomp : MonoBehaviour
{
    private const string DefaultBlendShapeName = "mouth close";
    private const float OpenWeight = 0f;
    private const float ClosedWeight = 100f;
    private const float OpenPhase = 0.28f;
    private const float ChompPhase = 0.55f;
    private const float HoldPhase = 0.68f;

    [SerializeField, Tooltip("Mouth renderer with the chomp blend shape. Leave empty to find one on this object or its children.")]
    private SkinnedMeshRenderer mouthRenderer;

    [SerializeField, Tooltip("Blend shape that is fully open at 0 and closed at 100.")]
    private string blendShapeName = DefaultBlendShapeName;

    [Header("Breath")]
    [SerializeField, Min(0.2f), Tooltip("Seconds for one slow open-and-close breath.")]
    private float breathPeriod = 4f;

    [SerializeField, Range(0f, 100f), Tooltip("Blend shape weight at the open end of a breath. 0 is fully open.")]
    private float breathOpenWeight = 20f;

    [SerializeField, Range(0f, 100f), Tooltip("Blend shape weight at the closed end of a breath. 100 is fully closed.")]
    private float breathClosedWeight = 75f;

    [SerializeField, Range(0f, 0.35f), Tooltip("How far the host sphere's rendered radius swings with the breath, as a fraction of its radius.")]
    private float breathPulseAmplitude = 0.06f;

    [Header("Bite")]
    [SerializeField, Min(0.04f), Tooltip("Seconds for one bite: open fully, chomp shut, and return to the breath.")]
    private float chompDuration = 0.55f;

    private int blendShapeIndex = -1;
    private float breathPhase;
    private float chompElapsed;
    private float biteStartWeight;
    private bool chomping;
    private FleshVisualComponent hostFlesh;

    /// <summary>Mouth skinned mesh used for chomping. Resolved from children when unassigned.</summary>
    public SkinnedMeshRenderer MouthRenderer
    {
        get
        {
            ResolveMouth();
            return mouthRenderer;
        }
    }

    /// <summary>True while a bite is playing over the breath.</summary>
    public bool IsChomping => chomping;

    /// <summary>Plays one bite. Does nothing if a bite is already playing.</summary>
    public void Chomp()
    {
        ResolveMouth();
        if (chomping || blendShapeIndex < 0 || mouthRenderer == null)
        {
            return;
        }

        chomping = true;
        chompElapsed = 0f;
        biteStartWeight = CurrentBreathWeight();
    }

    private void Awake()
    {
        ResolveMouth();
    }

    private void OnEnable()
    {
        ResolveMouth();
        breathPhase = Random.Range(0f, Mathf.PI * 2f);
        chomping = false;
        BindHost();
        ApplyBreath();
    }

    private void Start()
    {
        // OnEnable can run before the nested mouth renderer finishes waking.
        ResolveMouth();
        BindHost();
        ApplyBreath();
    }

    private void OnDisable()
    {
        if (hostFlesh != null)
        {
            hostFlesh.ClearBreathDrive();
        }

        chomping = false;
    }

    private void Update()
    {
        if (blendShapeIndex < 0 || mouthRenderer == null)
        {
            ResolveMouth();
            if (blendShapeIndex < 0 || mouthRenderer == null)
            {
                return;
            }
        }

        breathPhase += Time.deltaTime * (Mathf.PI * 2f) / Mathf.Max(breathPeriod, 0.2f);
        DriveHostPulse();

        if (chomping)
        {
            AdvanceChomp();
            return;
        }

        ApplyBreath();
    }

    private void AdvanceChomp()
    {
        chompElapsed += Time.deltaTime;
        float duration = Mathf.Max(0.04f, chompDuration);
        float t = Mathf.Clamp01(chompElapsed / duration);
        mouthRenderer.SetBlendShapeWeight(blendShapeIndex, BiteWeight(t));

        if (t >= 1f)
        {
            chomping = false;
            ApplyBreath();
        }
    }

    /// <summary>Opens from the current breath, snaps shut, then eases back onto the moving breath.</summary>
    private float BiteWeight(float t)
    {
        if (t < OpenPhase)
        {
            float u = Mathf.SmoothStep(0f, 1f, t / OpenPhase);
            return Mathf.Lerp(biteStartWeight, OpenWeight, u);
        }

        if (t < ChompPhase)
        {
            float u = Mathf.SmoothStep(0f, 1f, (t - OpenPhase) / (ChompPhase - OpenPhase));
            return Mathf.Lerp(OpenWeight, ClosedWeight, u);
        }

        if (t < HoldPhase)
        {
            return ClosedWeight;
        }

        float settle = Mathf.SmoothStep(0f, 1f, (t - HoldPhase) / (1f - HoldPhase));
        return Mathf.Lerp(ClosedWeight, CurrentBreathWeight(), settle);
    }

    private void ApplyBreath()
    {
        mouthRenderer.SetBlendShapeWeight(blendShapeIndex, CurrentBreathWeight());
    }

    /// <summary>+1 is the open, expanded end of the breath. -1 is the closed, contracted end.</summary>
    private float BreathWave => Mathf.Sin(breathPhase);

    private float CurrentBreathWeight()
    {
        float openAmount = BreathWave * 0.5f + 0.5f;
        return Mathf.Lerp(breathClosedWeight, breathOpenWeight, openAmount);
    }

    private void DriveHostPulse()
    {
        if (hostFlesh == null)
        {
            BindHost();
        }

        if (hostFlesh == null)
        {
            return;
        }

        hostFlesh.DriveBreath(BreathWave, breathPulseAmplitude);
    }

    private void BindHost()
    {
        hostFlesh = GetComponentInParent<FleshVisualComponent>();
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
        breathPeriod = Mathf.Max(0.2f, breathPeriod);
        breathOpenWeight = Mathf.Clamp(breathOpenWeight, 0f, 100f);
        breathClosedWeight = Mathf.Clamp(breathClosedWeight, 0f, 100f);
        breathPulseAmplitude = Mathf.Clamp(breathPulseAmplitude, 0f, 0.35f);
        chompDuration = Mathf.Max(0.04f, chompDuration);
        if (string.IsNullOrEmpty(blendShapeName))
        {
            blendShapeName = DefaultBlendShapeName;
        }
    }
}
