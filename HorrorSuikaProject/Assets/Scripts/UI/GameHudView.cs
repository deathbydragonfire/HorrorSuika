using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Reads GameController, ScoreController, and LevelObjectiveTracker events and drives the score
/// label, next-item preview, objective checklist, and the game-over and victory panels.
/// Never talks to physics directly.
/// </summary>
public class GameHudView : MonoBehaviour
{
    private const float PreviewMaxSize = 140f;
    private const float PreviewSizePerRadiusUnit = 110f;
    private const float PreviewMinSize = 44f;

    [Header("Run")]
    [SerializeField] private TextMeshProUGUI scoreLabel;
    [SerializeField] private Image nextPreviewImage;
    [SerializeField] private TextMeshProUGUI nextPreviewLabel;

    [Header("Level")]
    [SerializeField] private TextMeshProUGUI levelNameLabel;
    [SerializeField] private TextMeshProUGUI limitLabel;
    [SerializeField] private Transform objectiveListRoot;
    [SerializeField] private LevelObjectiveRowView objectiveRowPrefab;

    [Header("Game Over")]
    [SerializeField] private GameObject gameOverPanel;
    [SerializeField] private TextMeshProUGUI gameOverLabel;
    [SerializeField] private Button restartButton;

    [Header("Victory")]
    [SerializeField] private GameObject victoryPanel;
    [SerializeField] private TextMeshProUGUI victoryLabel;
    [SerializeField] private Button nextLevelButton;
    [SerializeField] private Button retryButton;
    [SerializeField] private Button levelSelectButton;

    [Tooltip("Optional Level Select control on the game-over panel so a loss can still return to the menu.")]
    [SerializeField] private Button gameOverLevelSelectButton;

    private readonly List<LevelObjectiveRowView> objectiveRows = new List<LevelObjectiveRowView>();

    private MergeItemTierTable tierTable;
    private NextItemQueue nextItemQueue;
    private ScoreController scoreController;
    private GameController gameController;
    private LevelObjectiveTracker objectiveTracker;

    /// <summary>Injects the data and systems the HUD reflects.</summary>
    public void Configure(
        MergeItemTierTable table,
        NextItemQueue queue,
        ScoreController score,
        GameController game,
        LevelObjectiveTracker tracker)
    {
        Unsubscribe();

        tierTable = table;
        nextItemQueue = queue;
        scoreController = score;
        gameController = game;
        objectiveTracker = tracker;

        if (scoreController != null)
        {
            scoreController.ScoreChanged += OnScoreChanged;
            OnScoreChanged(scoreController.Score);
        }

        if (nextItemQueue != null)
        {
            nextItemQueue.Changed += OnNextItemChanged;
            OnNextItemChanged();
        }

        if (gameController != null)
        {
            gameController.StateChanged += OnStateChanged;
        }

        if (objectiveTracker != null)
        {
            objectiveTracker.ProgressChanged += OnObjectiveProgressChanged;
            RebuildObjectiveRows();
        }

        BindButton(restartButton, OnRestartClicked);
        BindButton(retryButton, OnRestartClicked);
        BindButton(nextLevelButton, OnNextLevelClicked);
        BindButton(levelSelectButton, OnLevelSelectClicked);
        BindButton(gameOverLevelSelectButton, OnLevelSelectClicked);

        UpdateLevelLabel();
        SetGameOverVisible(false, 0);
        SetVictoryVisible(false, 0);
    }

    /// <summary>Shows or hides the game-over panel and its result text.</summary>
    public void SetGameOverVisible(bool visible, int finalScore)
    {
        if (gameOverPanel != null)
        {
            gameOverPanel.SetActive(visible);
        }

        if (visible && gameOverLabel != null)
        {
            gameOverLabel.text = $"{BuildFailureHeadline()}\nScore {finalScore}";
        }
    }

    /// <summary>Shows or hides the victory panel and its result text.</summary>
    public void SetVictoryVisible(bool visible, int finalScore)
    {
        if (victoryPanel != null)
        {
            victoryPanel.SetActive(visible);
        }

        if (visible && victoryLabel != null)
        {
            victoryLabel.text = $"Level Complete\nScore {finalScore}";
        }

        if (nextLevelButton != null)
        {
            nextLevelButton.interactable = gameController != null && gameController.HasNextLevel();
        }
    }

    private void Update()
    {
        UpdateLimitLabel();
    }

    private void OnDestroy()
    {
        Unsubscribe();
    }

    private void Unsubscribe()
    {
        if (scoreController != null)
        {
            scoreController.ScoreChanged -= OnScoreChanged;
        }

        if (nextItemQueue != null)
        {
            nextItemQueue.Changed -= OnNextItemChanged;
        }

        if (gameController != null)
        {
            gameController.StateChanged -= OnStateChanged;
        }

        if (objectiveTracker != null)
        {
            objectiveTracker.ProgressChanged -= OnObjectiveProgressChanged;
        }
    }

    private static void BindButton(Button button, UnityEngine.Events.UnityAction callback)
    {
        if (button == null)
        {
            return;
        }

        button.onClick.RemoveListener(callback);
        button.onClick.AddListener(callback);
    }

    private void RebuildObjectiveRows()
    {
        if (objectiveListRoot == null || objectiveRowPrefab == null || objectiveTracker == null)
        {
            return;
        }

        for (int i = objectiveRows.Count - 1; i >= 0; i--)
        {
            if (objectiveRows[i] != null)
            {
                Destroy(objectiveRows[i].gameObject);
            }
        }

        objectiveRows.Clear();

        IReadOnlyList<LevelObjectiveTracker.ObjectiveProgress> progress = objectiveTracker.Progress;
        for (int i = 0; i < progress.Count; i++)
        {
            LevelObjectiveRowView row = Instantiate(objectiveRowPrefab, objectiveListRoot);
            row.Bind(progress[i]);
            objectiveRows.Add(row);
        }
    }

    private void OnObjectiveProgressChanged(LevelObjectiveTracker.ObjectiveProgress progress)
    {
        if (progress.Index < 0)
        {
            return;
        }

        if (progress.Index >= objectiveRows.Count)
        {
            RebuildObjectiveRows();
            return;
        }

        LevelObjectiveRowView row = objectiveRows[progress.Index];
        if (row != null)
        {
            row.Bind(progress);
        }
    }

    private void UpdateLevelLabel()
    {
        if (levelNameLabel == null)
        {
            return;
        }

        LevelDefinition level = gameController != null ? gameController.CurrentLevel : null;
        levelNameLabel.text = level != null ? level.DisplayName : string.Empty;
    }

    private void UpdateLimitLabel()
    {
        if (limitLabel == null || gameController == null)
        {
            return;
        }

        float timeRemaining = gameController.TimeRemaining;
        if (timeRemaining >= 0f)
        {
            int seconds = Mathf.CeilToInt(timeRemaining);
            limitLabel.text = $"{seconds / 60:0}:{seconds % 60:00}";
            return;
        }

        int dropsRemaining = gameController.DropsRemaining;
        limitLabel.text = dropsRemaining >= 0 ? $"{dropsRemaining} drops" : string.Empty;
    }

    private void OnScoreChanged(int score)
    {
        if (scoreLabel != null)
        {
            scoreLabel.text = score.ToString();
        }
    }

    private void OnNextItemChanged()
    {
        if (tierTable == null || nextItemQueue == null)
        {
            return;
        }

        MergeItemTier tier = tierTable.GetTier(nextItemQueue.NextTier);
        if (tier == null)
        {
            return;
        }

        if (nextPreviewImage != null)
        {
            nextPreviewImage.color = tier.PlaceholderColor;
            float size = Mathf.Clamp(tier.Radius * PreviewSizePerRadiusUnit, PreviewMinSize, PreviewMaxSize);
            nextPreviewImage.rectTransform.sizeDelta = new Vector2(size, size);
        }

        if (nextPreviewLabel != null)
        {
            nextPreviewLabel.text = "NEXT";
        }
    }

    private void OnStateChanged(GameController.GameState state)
    {
        int score = scoreController != null ? scoreController.Score : 0;

        switch (state)
        {
            case GameController.GameState.Victory:
                SetGameOverVisible(false, score);
                SetVictoryVisible(true, score);
                break;
            case GameController.GameState.GameOver:
                SetVictoryVisible(false, score);
                SetGameOverVisible(true, score);
                break;
            default:
                SetGameOverVisible(false, score);
                SetVictoryVisible(false, score);
                break;
        }

        UpdateLevelLabel();
    }

    private string BuildFailureHeadline()
    {
        if (gameController == null)
        {
            return "Game Over";
        }

        switch (gameController.FailureReason)
        {
            case GameController.LevelFailureReason.OutOfDrops:
                return "Out of Drops";
            case GameController.LevelFailureReason.TimeExpired:
                return "Time Up";
            default:
                return "Overflow";
        }
    }

    private void OnRestartClicked()
    {
        if (gameController != null)
        {
            gameController.Restart();
        }
    }

    private void OnNextLevelClicked()
    {
        if (gameController != null)
        {
            gameController.TryAdvanceToNextLevel();
        }
    }

    private void OnLevelSelectClicked()
    {
        if (gameController != null)
        {
            gameController.ReturnToLevelSelect();
        }
    }
}
