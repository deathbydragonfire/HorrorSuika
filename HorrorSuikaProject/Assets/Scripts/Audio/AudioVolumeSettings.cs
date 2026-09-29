using System;
using UnityEngine;

/// <summary>
/// Persisted per-channel volumes. WebGL has no AudioMixer, so every source multiplies by
/// <see cref="GetGain"/> instead of routing through mixer groups.
/// </summary>
public static class AudioVolumeSettings
{
    private const string KeyPrefix = "audio.volume.";
    private const float DefaultVolume = 0.8f;

    private static readonly float[] Cache = { -1f, -1f, -1f };

    /// <summary>Raised with the channel and its new slider value whenever a volume changes.</summary>
    public static event Action<AudioChannel, float> Changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        // Survives a play session when domain reload is disabled.
        Changed = null;
        for (int i = 0; i < Cache.Length; i++)
        {
            Cache[i] = -1f;
        }
    }

    /// <summary>Slider position for a channel, 0..1.</summary>
    public static float Get(AudioChannel channel)
    {
        int index = (int)channel;
        if (Cache[index] < 0f)
        {
            Cache[index] = Mathf.Clamp01(PlayerPrefs.GetFloat(KeyPrefix + channel, DefaultVolume));
        }

        return Cache[index];
    }

    /// <summary>Stores a slider position for a channel. Call <see cref="Save"/> once the drag ends.</summary>
    public static void Set(AudioChannel channel, float value)
    {
        value = Mathf.Clamp01(value);
        if (Mathf.Approximately(Get(channel), value))
        {
            return;
        }

        Cache[(int)channel] = value;
        PlayerPrefs.SetFloat(KeyPrefix + channel, value);
        Changed?.Invoke(channel, value);
    }

    /// <summary>Linear gain for a channel. Squared so the slider feels even across its travel.</summary>
    public static float GetGain(AudioChannel channel)
    {
        float value = Get(channel);
        return value * value;
    }

    /// <summary>Flushes to disk (IndexedDB on WebGL); too slow to call on every drag frame.</summary>
    public static void Save()
    {
        PlayerPrefs.Save();
    }
}
