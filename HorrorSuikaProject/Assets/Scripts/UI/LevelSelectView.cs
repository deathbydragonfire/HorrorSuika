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

    [Tooltip("Optional button that returns to the title scene.")]
    [SerializeField] private Button backButton;

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
            tile.Bind(level, i, LevelProgressStore.IsUnlocked(levelSequence, i), OnLevelSelected);
            tiles.Add(tile);
        }

        FitButtons();
    }

    private void OnRectTransformDimensionsChange()
    {
        FitButtons();
    }

    /// <summary>
    /// Fills the space under the title with a 3 by 2 grid in landscape and a 2 by 3 grid in portrait.
    /// </summary>
    private void FitButtons()
    {
        if (gridRoot == null || levelSequence == null || levelSequence.Count == 0)
        {
            return;
        }

        GridLayoutGroup grid = gridRoot.GetComponent<GridLayoutGroup>();
        RectTransform gridRect = gridRoot as RectTransform;
        RectTransform viewport = gridRect != null ? gridRect.parent as RectTransform : null;
        if (grid == null || gridRect == null || viewport == null)
        {
            return;
        }

        gridRect.anchorMin = Vector2.zero;
        gridRect.anchorMax = Vector2.one;
        gridRect.pivot = new Vector2(0.5f, 1f);
        gridRect.offsetMin = Vector2.zero;
        gridRect.offsetMax = Vector2.zero;

        float width = viewport.rect.width;
        float height = viewport.rect.height;
        if (width < 1f || height < 1f)
        {
            return;
        }

        bool portrait = Screen.height >= Screen.width;
        int columns = portrait ? 2 : 3;
        int rows = portrait ? 3 : 2;
        float innerWidth = width - grid.padding.left - grid.padding.right;
        float innerHeight = height - grid.padding.top - grid.padding.bottom;
        float cellWidth = (innerWidth - grid.spacing.x * (columns - 1)) / columns;
        float cellHeight = (innerHeight - grid.spacing.y * (rows - 1)) / rows;

        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = columns;
        grid.cellSize = new Vector2(Mathf.Max(1f, cellWidth), Mathf.Max(1f, cellHeight));
        grid.childAlignment = TextAnchor.UpperCenter;
    }

    private void Start()
    {
        if (backButton != null)
        {
            backButton.onClick.RemoveListener(OnBackClicked);
            backButton.onClick.AddListener(OnBackClicked);
        }

        Refresh();
        Canvas.ForceUpdateCanvases();
        FitButtons();
    }

    private void OnLevelSelected(LevelDefinition level, int index)
    {
        if (level == null || !LevelProgressStore.IsUnlocked(levelSequence, index))
        {
            return;
        }

        if (levelSelection != null)
        {
            levelSelection.Select(level, index);
        }

        SceneFlow.LoadGame();
    }

    private void OnBackClicked()
    {
        SceneFlow.LoadStart();
    }
}
