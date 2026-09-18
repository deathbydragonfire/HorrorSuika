using System;
using UnityEngine;

/// <summary>
/// Shared cosmetic pulse for every flesh blob. Authored on the tier table so amplitude, rate, and
/// size falloff live with the rest of the merge-item tuning.
/// </summary>
[Serializable]
public class FleshPulseSettings
{
    private const float MinimumReferenceRadius = 0.01f;

    [SerializeField, Tooltip("Breathe the rendered radius in and out. Purely cosmetic: colliders and gameplay radii are untouched.")]
    private bool enabled = true;

    [SerializeField, Range(0f, 0.35f), Tooltip("Peak radius swing as a fraction of the sphere radius, before the size falloff scales it.")]
    private float amplitude = 0.06f;

    [SerializeField, Min(0f), Tooltip("Pulse rate in cycles per second for an instance whose radius equals Reference Radius.")]
    private float frequency = 0.55f;

    [SerializeField, Min(MinimumReferenceRadius), Tooltip("Sphere radius at which Frequency and Amplitude are taken verbatim. Smaller instances beat faster and deeper, larger ones slower and shallower.")]
    private float referenceRadius = 0.5f;

    [SerializeField, Range(0f, 2f), Tooltip("How strongly size drives the pulse. 0 makes every instance pulse identically; 1 makes frequency and amplitude inversely proportional to radius.")]
    private float sizeFalloff = 0.75f;

    [SerializeField, Range(0f, 1f), Tooltip("Per-instance random spread of the pulse rate, so two items of the same tier never beat in lockstep.")]
    private float frequencyJitter = 0.3f;

    [SerializeField, Range(0f, 1f), Tooltip("Weight of the second harmonic. Above zero the beat stops reading as a clean sine and gains a fleshier double-thump.")]
    private float harmonicWeight = 0.35f;

    [SerializeField, Min(0f), Tooltip("Global multiplier on every instance's pulse rate. 0 stops the pulse mid-beat instead of snapping it back.")]
    private float timeScale = 1f;

    /// <summary>True when the rendered radius should breathe.</summary>
    public bool Enabled => enabled;

    /// <summary>Peak radius swing as a fraction of sphere radius, before size falloff.</summary>
    public float Amplitude => amplitude;

    /// <summary>Pulse rate in cycles per second at the reference radius.</summary>
    public float Frequency => frequency;

    /// <summary>Sphere radius at which frequency and amplitude are taken verbatim.</summary>
    public float ReferenceRadius => referenceRadius;

    /// <summary>How strongly instance size scales the pulse.</summary>
    public float SizeFalloff => sizeFalloff;

    /// <summary>Per-instance random spread of the pulse rate.</summary>
    public float FrequencyJitter => frequencyJitter;

    /// <summary>Weight of the second harmonic in the pulse wave.</summary>
    public float HarmonicWeight => harmonicWeight;

    /// <summary>Global multiplier on the shared pulse clock.</summary>
    public float TimeScale => timeScale;

    /// <summary>Clamps authored values so the asset stays editable instead of throwing.</summary>
    public void ClampValues()
    {
        amplitude = Mathf.Clamp(amplitude, 0f, 0.35f);
        frequency = Mathf.Max(frequency, 0f);
        referenceRadius = Mathf.Max(referenceRadius, MinimumReferenceRadius);
        sizeFalloff = Mathf.Clamp(sizeFalloff, 0f, 2f);
        frequencyJitter = Mathf.Clamp01(frequencyJitter);
        harmonicWeight = Mathf.Clamp01(harmonicWeight);
        timeScale = Mathf.Max(timeScale, 0f);
    }
}
