using System;
using UnityEngine;

/// <summary>
/// Single-asset handoff carrying the chosen level across the scene load from level select into the
/// game scene. The runtime fields are <c>[NonSerialized]</c> so choosing a level in the editor never
/// dirties the asset on disk.
/// </summary>
[CreateAssetMenu(menuName = "Merge Drop/Level Selection State", fileName = "LevelSelectionState")]
public class LevelSelectionState : ScriptableObject
{
    [NonSerialized] private LevelDefinition selectedLevel;
    [NonSerialized] private int selectedIndex = -1;

    /// <summary>The level the player picked, or null when the game scene was entered directly.</summary>
    public LevelDefinition SelectedLevel => selectedLevel;

    /// <summary>Sequence index of the picked level, or -1.</summary>
    public int SelectedIndex => selectedIndex;

    /// <summary>Records the player's choice before the game scene loads.</summary>
    public void Select(LevelDefinition level, int index)
    {
        selectedLevel = level;
        selectedIndex = index;
    }

    /// <summary>Forgets the current choice.</summary>
    public void Clear()
    {
        selectedLevel = null;
        selectedIndex = -1;
    }

    /// <summary>
    /// Resolves which level to play: the explicit selection, else the first level the player has not
    /// completed, else the first entry. This is what lets the game scene be pressed Play directly.
    /// </summary>
    public LevelDefinition ResolveOrDefault(LevelSequence sequence)
    {
        return ResolveOrDefault(sequence, out _);
    }

    /// <summary>
    /// Resolves which level to play and the sequence index that goes with it.
    /// </summary>
    public LevelDefinition ResolveOrDefault(LevelSequence sequence, out int resolvedIndex)
    {
        if (selectedLevel != null)
        {
            resolvedIndex = selectedIndex >= 0 ? selectedIndex : (sequence != null ? sequence.IndexOf(selectedLevel) : -1);
            return selectedLevel;
        }

        resolvedIndex = -1;
        if (sequence == null || sequence.Count == 0)
        {
            return null;
        }

        for (int i = 0; i < sequence.Count; i++)
        {
            LevelDefinition level = sequence.Levels[i];
            if (level != null && !LevelProgressStore.IsCompleted(level.LevelId))
            {
                resolvedIndex = i;
                return level;
            }
        }

        resolvedIndex = 0;
        return sequence.Levels[0];
    }
}
