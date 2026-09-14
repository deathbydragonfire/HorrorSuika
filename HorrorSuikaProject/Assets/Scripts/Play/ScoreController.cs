using System;
using UnityEngine;

/// <summary>
/// Accumulates score and exposes change events for the HUD.
/// </summary>
public class ScoreController : MonoBehaviour
{
    /// <summary>Raised whenever the score changes, with the new total.</summary>
    public event Action<int> ScoreChanged;

    /// <summary>Current run score.</summary>
    public int Score { get; private set; }

    /// <summary>Adds points to the run total.</summary>
    public void Add(int amount)
    {
        if (amount == 0)
        {
            return;
        }

        Score = Mathf.Max(0, Score + amount);
        ScoreChanged?.Invoke(Score);
    }

    /// <summary>Zeroes the run total.</summary>
    public void Reset()
    {
        Score = 0;
        ScoreChanged?.Invoke(Score);
    }
}
