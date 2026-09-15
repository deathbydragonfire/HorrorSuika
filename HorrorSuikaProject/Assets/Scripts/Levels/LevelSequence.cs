using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Ordered level list backing both progression and the level-select grid.
/// </summary>
[CreateAssetMenu(menuName = "Merge Drop/Level Sequence", fileName = "MainLevelSequence")]
public class LevelSequence : ScriptableObject
{
    [Tooltip("Levels in play order. The first entry is always unlocked.")]
    [SerializeField] private List<LevelDefinition> levels = new List<LevelDefinition>();

    /// <summary>Levels in play order.</summary>
    public IReadOnlyList<LevelDefinition> Levels => levels;

    /// <summary>How many levels the sequence holds.</summary>
    public int Count => levels != null ? levels.Count : 0;

    /// <summary>Returns the level at the index, or null when out of range.</summary>
    public LevelDefinition GetLevel(int index)
    {
        if (levels == null || index < 0 || index >= levels.Count)
        {
            Debug.LogWarning($"{name}: no level at index {index}.", this);
            return null;
        }

        return levels[index];
    }

    /// <summary>Index of the level in the sequence, or -1 when it is not part of it.</summary>
    public int IndexOf(LevelDefinition level)
    {
        return levels != null && level != null ? levels.IndexOf(level) : -1;
    }

    /// <summary>Resolves the level after the given one, if any.</summary>
    public bool TryGetNext(LevelDefinition current, out LevelDefinition next, out int nextIndex)
    {
        next = null;
        nextIndex = -1;

        int index = IndexOf(current);
        if (index < 0 || index + 1 >= Count)
        {
            return false;
        }

        nextIndex = index + 1;
        next = levels[nextIndex];
        return next != null;
    }

    private void OnValidate()
    {
        if (levels == null)
        {
            return;
        }

        HashSet<string> seenIds = new HashSet<string>();
        for (int i = 0; i < levels.Count; i++)
        {
            LevelDefinition level = levels[i];
            if (level == null)
            {
                Debug.LogWarning($"{name}: entry {i} is null.", this);
                continue;
            }

            if (!seenIds.Add(level.LevelId))
            {
                Debug.LogWarning($"{name}: duplicate LevelId '{level.LevelId}' at entry {i}; saved progress will collide.", this);
            }
        }
    }
}
