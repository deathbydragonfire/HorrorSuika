using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Reads GameController, ScoreController, and LevelObjectiveTracker events and drives the level
/// info panel, objective checklist, and the game-over and victory panels.
/// Never talks to physics directly.
/// </summary>
public class GameHudView : MonoBehaviour
{
    [Header("Run")]
    [SerializeField] private TextMeshProUGUI scoreLabel;

    [Header("Level")]
    [SerializeField] private TextMeshProUGUI levelNameLabel;
    [SerializeField] private TextMeshProUGUI limitLabel;
    [SerializeField] private Transform objectiveListRoot;
    [SerializeField] private Transform onOneListRoot;
    [SerializeField] private Transform quotaListRoot;
    [SerializeField] private GameObject objectiveSectionRule;
    [SerializeField] private GameObject orderFormTitle;
    [SerializeField] private GameObject atOnceLabel;
    [SerializeField] private LevelObjectiveRowView objectiveRowPrefab;
    [SerializeField] private Button resetLevelButton;
    [SerializeField] private Button homeButton;

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

    private ScoreController scoreController;
    private GameController gameController;
    private LevelObjectiveTracker objectiveTracker;

    /// <summary>Injects the data and systems the HUD reflects.</summary>
    public void Configure(
        ScoreController score,
        GameController game,
        LevelObjectiveTracker tracker)
    {
        Unsubscribe();

        scoreController = score;
        gameController = game;
        objectiveTracker = tracker;

        if (scoreController != null)
        {
            scoreController.ScoreChanged += OnScoreChanged;
            OnScoreChanged(scoreController.Score);
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

        BindButton(resetLevelButton, OnRestartClicked);
        BindButton(homeButton, OnLevelSelectClicked);
        BindButton(restartButton, OnRestartClicked);
        BindButton(retryButton, OnRestartClicked);
        BindButton(nextLevelButton, OnNextLevelClicked);
        BindButton(levelSelectButton, OnLevelSelectClicked);
        BindButton(gameOverLevelSelectButton, OnLevelSelectClicked);

        UpdateLevelLabel();
        UpdateScoreVisibility();
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
            gameOverLabel.text = LevelRequiresScore()
                ? $"{BuildFailureHeadline()}\nScore {finalScore}"
                : BuildFailureHeadline();
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
            victoryLabel.text = LevelRequiresScore()
                ? $"Level Complete\nScore {finalScore}"
                : "Level Complete";
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
        int onOneCount = 0;
        int quotaCount = 0;
        for (int i = 0; i < progress.Count; i++)
        {
            LevelObjectiveTracker.ObjectiveProgress entry = progress[i];
            bool onOne = entry.IsOnOne;
            Transform parent = onOne ? onOneListRoot : quotaListRoot;
            if (parent == null)
            {
                parent = objectiveListRoot;
            }

            if (parent == null)
            {
                continue;
            }

            LevelObjectiveRowView row = Instantiate(objectiveRowPrefab, parent);
            if (!onOne)
            {
                row.SetStripe(quotaCount);
                quotaCount++;
            }
            else
            {
                onOneCount++;
            }

            row.Bind(entry);
            objectiveRows.Add(row);
        }

        ApplyGoalSectionVisibility(onOneCount, quotaCount, GlobalGoalsAreAtOnce());
    }

    private void ApplyGoalSectionVisibility(int onOneCount, int quotaCount, bool atOnce)
    {
        bool showOnOne = onOneCount > 0;
        bool showQuota = quotaCount > 0;
        SetActiveIfDifferent(onOneListRoot != null ? onOneListRoot.gameObject : null, showOnOne);
        SetActiveIfDifferent(quotaListRoot != null ? quotaListRoot.gameObject : null, showQuota);
        SetActiveIfDifferent(objectiveSectionRule, showOnOne && showQuota);

        bool showAtOnce = showQuota && atOnce;
        SetActiveIfDifferent(atOnceLabel, showAtOnce);
        if (showAtOnce)
        {
            SetSectionLabel(atOnceLabel, "At Once");
        }
    }

    private static void SetActiveIfDifferent(GameObject gameObject, bool active)
    {
        if (gameObject != null && gameObject.activeSelf != active)
        {
            gameObject.SetActive(active);
        }
    }

    private bool GlobalGoalsAreAtOnce()
    {
        LevelDefinition level = gameController != null ? gameController.CurrentLevel : null;
        return GoalsAreAtOnce(level);
    }

    private static bool GoalsAreAtOnce(LevelDefinition level)
    {
        IReadOnlyList<LevelObjective> objectives = level != null ? level.Objectives : null;
        if (objectives == null)
        {
            return false;
        }

        bool sawGlobal = false;
        for (int i = 0; i < objectives.Count; i++)
        {
            LevelObjective objective = objectives[i];
            if (objective == null || objective.IsOnOneGoal || objective.ObjectiveType == LevelObjectiveType.ScoreAtLeast)
            {
                continue;
            }

            sawGlobal = true;
            if (objective.CountMode != ObjectiveCountMode.Simultaneous)
            {
                return false;
            }
        }

        return sawGlobal;
    }

#if UNITY_EDITOR
    private const string GoalPreviewName = "GoalPreview";

    /// <summary>Shows the level's goal lines on the ticket while the editor is not playing.</summary>
    public void ShowEditorGoalPreview(LevelDefinition level, MergeItemTierTable table)
    {
        if (Application.isPlaying || objectiveRowPrefab == null)
        {
            return;
        }

        ClearEditorGoalPreview();

        IReadOnlyList<LevelObjective> objectives = level != null ? level.Objectives : null;
        int count = objectives != null ? objectives.Count : 0;
        int onOneCount = 0;
        int quotaCount = 0;
        for (int i = 0; i < count; i++)
        {
            LevelObjective objective = objectives[i];
            if (objective == null)
            {
                continue;
            }

            bool onOne = objective.IsOnOneGoal;
            Transform parent = onOne ? onOneListRoot : quotaListRoot;
            if (parent == null)
            {
                parent = objectiveListRoot;
            }

            if (parent == null)
            {
                continue;
            }

            string[] parts = objective.BuildOnOneParts(table);
            bool[] partMet = parts != null ? new bool[parts.Length] : null;
            var progress = new LevelObjectiveTracker.ObjectiveProgress(
                i,
                0,
                objective.RequiredCount,
                false,
                objective.BuildTicketCategory(table),
                onOne,
                parts,
                partMet);

            LevelObjectiveRowView row = Instantiate(objectiveRowPrefab, parent);
            row.gameObject.name = GoalPreviewName;
            if (!onOne)
            {
                row.SetStripe(quotaCount);
                quotaCount++;
            }
            else
            {
                onOneCount++;
            }

            row.Bind(progress);
            MarkGoalPreview(row.gameObject);
        }

        ApplyGoalSectionVisibility(onOneCount, quotaCount, GoalsAreAtOnce(level));
    }

    /// <summary>Removes editor-only goal lines from the ticket.</summary>
    public void ClearEditorGoalPreview()
    {
        if (Application.isPlaying)
        {
            return;
        }

        DestroyGoalPreviews(objectiveListRoot);
        if (onOneListRoot != null && !IsUnder(onOneListRoot, objectiveListRoot))
        {
            DestroyGoalPreviews(onOneListRoot);
        }

        if (quotaListRoot != null && !IsUnder(quotaListRoot, objectiveListRoot))
        {
            DestroyGoalPreviews(quotaListRoot);
        }
    }

    /// <summary>How many editor goal lines are currently on the ticket.</summary>
    public int EditorGoalPreviewCount()
    {
        int count = CountGoalPreviews(objectiveListRoot);
        if (onOneListRoot != null && !IsUnder(onOneListRoot, objectiveListRoot))
        {
            count += CountGoalPreviews(onOneListRoot);
        }

        if (quotaListRoot != null && !IsUnder(quotaListRoot, objectiveListRoot))
        {
            count += CountGoalPreviews(quotaListRoot);
        }

        return count;
    }

    private static bool IsUnder(Transform child, Transform parent)
    {
        return parent != null && child.IsChildOf(parent);
    }

    private static void MarkGoalPreview(GameObject gameObject)
    {
        Transform[] transforms = gameObject.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            transforms[i].gameObject.hideFlags = HideFlags.DontSaveInEditor | HideFlags.HideInHierarchy | HideFlags.NotEditable;
        }
    }

    private static void DestroyGoalPreviews(Transform root)
    {
        if (root == null)
        {
            return;
        }

        LevelObjectiveRowView[] rows = root.GetComponentsInChildren<LevelObjectiveRowView>(true);
        for (int i = rows.Length - 1; i >= 0; i--)
        {
            LevelObjectiveRowView row = rows[i];
            if (row != null && row.gameObject.name == GoalPreviewName)
            {
                DestroyImmediate(row.gameObject);
            }
        }
    }

    private static int CountGoalPreviews(Transform root)
    {
        if (root == null)
        {
            return 0;
        }

        int count = 0;
        LevelObjectiveRowView[] rows = root.GetComponentsInChildren<LevelObjectiveRowView>(true);
        for (int i = 0; i < rows.Length; i++)
        {
            if (rows[i] != null && rows[i].gameObject.name == GoalPreviewName)
            {
                count++;
            }
        }

        return count;
    }
#endif

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
        int index = gameController != null ? gameController.CurrentLevelIndex : -1;
        levelNameLabel.fontSize = 36f;
        levelNameLabel.fontStyle = FontStyles.Bold;
        if (orderFormTitle != null)
        {
            orderFormTitle.SetActive(true);
            SetSectionLabel(orderFormTitle, "Order form");
        }

        if (index >= 0)
        {
            levelNameLabel.text = $"Level {index + 1}";
        }
        else
        {
            levelNameLabel.text = level != null ? level.DisplayName : string.Empty;
        }

        UpdateScoreVisibility();
    }

    private static void SetSectionLabel(GameObject label, string text)
    {
        if (label == null)
        {
            return;
        }

        TMP_Text view = label.GetComponent<TMP_Text>();
        if (view != null)
        {
            view.text = text;
        }
    }

    private void UpdateScoreVisibility()
    {
        bool showScore = LevelRequiresScore();
        if (scoreLabel != null && scoreLabel.gameObject.activeSelf != showScore)
        {
            scoreLabel.gameObject.SetActive(showScore);
        }

        UpdateHeaderChrome();
    }

    private bool LevelRequiresScore()
    {
        LevelDefinition level = gameController != null ? gameController.CurrentLevel : null;
        IReadOnlyList<LevelObjective> objectives = level != null ? level.Objectives : null;
        if (objectives == null)
        {
            return false;
        }

        for (int i = 0; i < objectives.Count; i++)
        {
            LevelObjective objective = objectives[i];
            if (objective != null && objective.ObjectiveType == LevelObjectiveType.ScoreAtLeast)
            {
                return true;
            }
        }

        return false;
    }

    private void UpdateHeaderChrome()
    {
        if (levelNameLabel == null)
        {
            return;
        }

        bool scoreOn = scoreLabel != null && scoreLabel.gameObject.activeSelf;
        bool limitOn = limitLabel != null && limitLabel.gameObject.activeSelf;
        bool showRight = scoreOn || limitOn;
        Transform header = levelNameLabel.transform.parent;
        if (header == null)
        {
            return;
        }

        Transform split = header.Find("HeaderSplit");
        if (split != null && split.gameObject.activeSelf != showRight)
        {
            split.gameObject.SetActive(showRight);
        }

        Transform stack = header.Find("ScoreStack");
        if (stack != null && stack.gameObject.activeSelf != showRight)
        {
            stack.gameObject.SetActive(showRight);
        }
    }

    private void UpdateLimitLabel()
    {
        if (limitLabel == null || gameController == null)
        {
            return;
        }

        float timeRemaining = gameController.TimeRemaining;
        int dropsRemaining = gameController.DropsRemaining;
        bool hasLimit = timeRemaining >= 0f || dropsRemaining >= 0;
        if (limitLabel.gameObject.activeSelf != hasLimit)
        {
            limitLabel.gameObject.SetActive(hasLimit);
        }

        UpdateHeaderChrome();
        if (!hasLimit)
        {
            return;
        }

        if (timeRemaining >= 0f)
        {
            int seconds = Mathf.CeilToInt(timeRemaining);
            limitLabel.text = $"{seconds / 60:0}:{seconds % 60:00}";
            return;
        }

        limitLabel.text = $"{dropsRemaining} drops";
    }

    private void OnScoreChanged(int score)
    {
        if (scoreLabel != null)
        {
            scoreLabel.text = score.ToString();
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
