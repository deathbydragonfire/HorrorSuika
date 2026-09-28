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

    /// <summary>
    /// Highest sequence index recorded when a level is won. The level list does not read this;
    /// a level opens only after the previous one has been completed.
    /// </summary>
    public static int HighestUnlockedIndex => Mathf.Max(0, PlayerPrefs.GetInt(HighestUnlockedKey, 0));

    /// <summary>
    /// True when this sequence index may be played. The first level is always open. Every later
    /// level stays locked until the one immediately before it has been completed.
    /// </summary>
    public static bool IsUnlocked(LevelSequence sequence, int levelIndex)
    {
        if (levelIndex <= 0)
        {
            return true;
        }

        if (sequence == null)
        {
            return false;
        }

        LevelDefinition previous = sequence.GetLevel(levelIndex - 1);
        return previous != null && IsCompleted(previous.LevelId);
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

#if UNITY_EDITOR
    /// <summary>Editor play sessions start from a fresh save. Builds keep the player's progress.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ResetProgressOnEditorPlay()
    {
        PlayerPrefs.DeleteAll();
        PlayerPrefs.Save();
    }
#endif

    private static bool IsValidId(string levelId)
    {
        return !string.IsNullOrWhiteSpace(levelId);
    }
}
