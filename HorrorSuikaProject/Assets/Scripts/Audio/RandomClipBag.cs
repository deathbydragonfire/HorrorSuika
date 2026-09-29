using System;
using UnityEngine;

/// <summary>
/// Shuffle-bag clip picker: every clip plays once per cycle and the same clip never plays twice
/// in a row, even across a reshuffle. Pure random repeats are what make SFX sound cheap.
/// </summary>
[Serializable]
public class RandomClipBag
{
    [SerializeField] private AudioClip[] clips = Array.Empty<AudioClip>();

    private int[] order;
    private int cursor;
    private int lastIndex = -1;

    /// <summary>True when the bag has nothing to play.</summary>
    public bool IsEmpty => clips == null || clips.Length == 0;

    /// <summary>The next clip in the shuffled cycle, or null when empty.</summary>
    public AudioClip Next()
    {
        if (IsEmpty)
        {
            return null;
        }

        for (int attempt = 0; attempt < clips.Length; attempt++)
        {
            if (order == null || order.Length != clips.Length || cursor >= order.Length)
            {
                Reshuffle();
            }

            int index = order[cursor++];
            lastIndex = index;
            if (clips[index] != null)
            {
                return clips[index];
            }
        }

        return null;
    }

    private void Reshuffle()
    {
        if (order == null || order.Length != clips.Length)
        {
            order = new int[clips.Length];
        }

        for (int i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }

        for (int i = order.Length - 1; i > 0; i--)
        {
            int swap = UnityEngine.Random.Range(0, i + 1);
            (order[i], order[swap]) = (order[swap], order[i]);
        }

        // The new cycle must not open with the clip that closed the last one.
        if (order.Length > 1 && order[0] == lastIndex)
        {
            int swap = UnityEngine.Random.Range(1, order.Length);
            (order[0], order[swap]) = (order[swap], order[0]);
        }

        cursor = 0;
    }
}
