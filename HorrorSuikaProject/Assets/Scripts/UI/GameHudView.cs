using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Reads GameController and ScoreController events and drives the score label,
/// next-item preview, and game-over panel. Never talks to physics directly.
/// </summary>
public class GameHudView : MonoBehaviour
{
    private const float PreviewMaxSize = 140f;
    private const float PreviewSizePerRadiusUnit = 110f;
    private const float PreviewMinSize = 44f;

    [SerializeField] private TextMeshProUGUI scoreLabel;
    [SerializeField] private Image nextPreviewImage;
    [SerializeField] private TextMeshProUGUI nextPreviewLabel;
    [SerializeField] private GameObject gameOverPanel;
    [SerializeField] private TextMeshProUGUI gameOverLabel;
    [SerializeField] private Button restartButton;

    private MergeItemTierTable tierTable;
    private NextItemQueue nextItemQueue;
    private ScoreController scoreController;
    private GameController gameController;

    /// <summary>Injects the data and systems the HUD reflects.</summary>
    public void Configure(MergeItemTierTable table, NextItemQueue queue, ScoreController score, GameController game)
    {
        Unsubscribe();

        tierTable = table;
        nextItemQueue = queue;
        scoreController = score;
        gameController = game;

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

        if (restartButton != null)
        {
            restartButton.onClick.RemoveListener(OnRestartClicked);
            restartButton.onClick.AddListener(OnRestartClicked);
        }

        SetGameOverVisible(false, 0);
    }

    /// <summary>Shows or hides the game-over panel and its final score text.</summary>
    public void SetGameOverVisible(bool visible, int finalScore)
    {
        if (gameOverPanel != null)
        {
            gameOverPanel.SetActive(visible);
        }

        if (visible && gameOverLabel != null)
        {
            gameOverLabel.text = $"Game Over\nScore {finalScore}";
        }
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
        bool isGameOver = state == GameController.GameState.GameOver;
        SetGameOverVisible(isGameOver, scoreController != null ? scoreController.Score : 0);
    }

    private void OnRestartClicked()
    {
        if (gameController != null)
        {
            gameController.Restart();
        }
    }
}
