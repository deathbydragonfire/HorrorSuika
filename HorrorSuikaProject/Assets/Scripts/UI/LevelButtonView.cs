using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One level tile: number, lock state, completion tick, and best score.
/// </summary>
public class LevelButtonView : MonoBehaviour
{
    private static readonly Color UnlockedColor = new Color(0.9f, 0.9f, 0.93f);
    private static readonly Color LockedColor = new Color(0.28f, 0.28f, 0.32f);

    [SerializeField] private Button button;
    [SerializeField] private Image background;
    [SerializeField] private TextMeshProUGUI titleLabel;
    [SerializeField] private TextMeshProUGUI bestScoreLabel;
    [SerializeField] private GameObject completionTick;
    [SerializeField] private GameObject lockIcon;

    private LevelDefinition level;
    private int levelIndex = -1;
    private Action<LevelDefinition, int> selectedCallback;

    /// <summary>Fills the tile from a level, its index, and its unlock state.</summary>
    public void Bind(LevelDefinition boundLevel, int index, bool unlocked, Action<LevelDefinition, int> onSelected)
    {
        level = boundLevel;
        levelIndex = index;
        selectedCallback = onSelected;

        bool completed = boundLevel != null && LevelProgressStore.IsCompleted(boundLevel.LevelId);
        int bestScore = boundLevel != null ? LevelProgressStore.GetBestScore(boundLevel.LevelId) : 0;

        if (titleLabel != null)
        {
            titleLabel.text = boundLevel != null ? boundLevel.DisplayName : $"{index + 1}";
        }

        if (bestScoreLabel != null)
        {
            bestScoreLabel.text = bestScore > 0 ? $"Best {bestScore}" : string.Empty;
        }

        if (completionTick != null)
        {
            completionTick.SetActive(completed);
        }

        if (lockIcon != null)
        {
            lockIcon.SetActive(!unlocked);
        }

        if (background != null)
        {
            background.color = unlocked ? UnlockedColor : LockedColor;
        }

        if (button != null)
        {
            button.interactable = unlocked && boundLevel != null;
            button.onClick.RemoveListener(OnClicked);
            button.onClick.AddListener(OnClicked);
        }
    }

    private void OnClicked()
    {
        selectedCallback?.Invoke(level, levelIndex);
    }
}
