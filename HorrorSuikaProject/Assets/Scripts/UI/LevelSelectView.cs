using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds the level grid from a <see cref="LevelSequence"/> and saved progress, and launches the
/// game scene with the player's choice.
/// </summary>
public class LevelSelectView : MonoBehaviour
{
    [Tooltip("Levels shown in the grid, in order.")]
    [SerializeField] private LevelSequence levelSequence;

    [Tooltip("Handoff asset the choice is written to before the game scene loads.")]
    [SerializeField] private LevelSelectionState levelSelection;

    [Tooltip("Parent with a GridLayoutGroup that the tiles are created under.")]
    [SerializeField] private Transform gridRoot;

    [Tooltip("Tile prefab instantiated once per level.")]
    [SerializeField] private LevelButtonView levelButtonPrefab;

    [Tooltip("Optional button that wipes saved progress.")]
    [SerializeField] private Button resetProgressButton;

    private readonly List<LevelButtonView> tiles = new List<LevelButtonView>();

    /// <summary>Rebuilds every tile from the sequence and the current saved progress.</summary>
    public void Refresh()
    {
        if (gridRoot == null || levelButtonPrefab == null || levelSequence == null)
        {
            Debug.LogWarning($"{nameof(LevelSelectView)}: grid root, tile prefab, or sequence is missing.", this);
            return;
        }

        for (int i = tiles.Count - 1; i >= 0; i--)
        {
            if (tiles[i] != null)
            {
                Destroy(tiles[i].gameObject);
            }
        }

        tiles.Clear();

        for (int i = 0; i < levelSequence.Count; i++)
        {
            LevelDefinition level = levelSequence.Levels[i];
            LevelButtonView tile = Instantiate(levelButtonPrefab, gridRoot);
            tile.Bind(level, i, LevelProgressStore.IsUnlocked(i), OnLevelSelected);
            tiles.Add(tile);
        }
    }

    private void Start()
    {
        if (resetProgressButton != null)
        {
            resetProgressButton.onClick.RemoveListener(OnResetProgressClicked);
            resetProgressButton.onClick.AddListener(OnResetProgressClicked);
        }

        Refresh();
    }

    private void OnLevelSelected(LevelDefinition level, int index)
    {
        if (level == null)
        {
            return;
        }

        if (levelSelection != null)
        {
            levelSelection.Select(level, index);
        }

        SceneFlow.LoadGame();
    }

    private void OnResetProgressClicked()
    {
        LevelProgressStore.ResetProgress(levelSequence);
        Refresh();
    }
}
