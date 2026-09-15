using UnityEngine;

/// <summary>
/// Static <see cref="PlayerPrefs"/> wrapper for per-level completion and best score.
/// PlayerPrefs is backed by IndexedDB on WebGL and only flushed on <see cref="PlayerPrefs.Save"/>,
/// so every write path calls it explicitly.
/// </summary>
public static class LevelProgressStore
{
    private const string CompletedKeyPrefix = "level.done.";
    private const string BestScoreKeyPrefix = "level.best.";
    private const string HighestUnlockedKey = "level.unlocked";

    /// <summary>True when the level has been won at least once.</summary>
    public static bool IsCompleted(string levelId)
    {
        return IsValidId(levelId) && PlayerPrefs.GetInt(CompletedKeyPrefix + levelId, 0) != 0;
    }

    /// <summary>Highest score recorded for the level, or 0.</summary>
    public static int GetBestScore(string levelId)
    {
        return IsValidId(levelId) ? PlayerPrefs.GetInt(BestScoreKeyPrefix + levelId, 0) : 0;
    }

    /// <summary>Index of the highest level the player has unlocked; index 0 is always unlocked.</summary>
    public static int HighestUnlockedIndex => Mathf.Max(0, PlayerPrefs.GetInt(HighestUnlockedKey, 0));

    /// <summary>True when the level at this sequence index may be played.</summary>
    public static bool IsUnlocked(int levelIndex)
    {
        return levelIndex <= 0 || levelIndex <= HighestUnlockedIndex;
    }

    /// <summary>Records a win: marks completion, raises the best score, and unlocks the next level.</summary>
    public static void MarkCompleted(string levelId, int levelIndex, int score)
    {
        if (!IsValidId(levelId))
        {
            Debug.LogWarning($"{nameof(LevelProgressStore)}: blank level id; progress was not saved.");
            return;
        }

        PlayerPrefs.SetInt(CompletedKeyPrefix + levelId, 1);

        if (score > GetBestScore(levelId))
        {
            PlayerPrefs.SetInt(BestScoreKeyPrefix + levelId, score);
        }

        int unlockedThrough = Mathf.Max(HighestUnlockedIndex, levelIndex + 1);
        PlayerPrefs.SetInt(HighestUnlockedKey, unlockedThrough);
        PlayerPrefs.Save();
    }

    /// <summary>Clears completion and best score for every level in the sequence.</summary>
    public static void ResetProgress(LevelSequence sequence)
    {
        if (sequence != null)
        {
            for (int i = 0; i < sequence.Count; i++)
            {
                LevelDefinition level = sequence.GetLevel(i);
                if (level == null || !IsValidId(level.LevelId))
                {
                    continue;
                }

                PlayerPrefs.DeleteKey(CompletedKeyPrefix + level.LevelId);
                PlayerPrefs.DeleteKey(BestScoreKeyPrefix + level.LevelId);
            }
        }

        PlayerPrefs.SetInt(HighestUnlockedKey, 0);
        PlayerPrefs.Save();
    }

    private static bool IsValidId(string levelId)
    {
        return !string.IsNullOrWhiteSpace(levelId);
    }
}
