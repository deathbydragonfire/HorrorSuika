using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Title menu. Play continues the first unfinished campaign level, Level Select opens the grid,
/// and Classic Mode starts the endless rectangle level.
/// </summary>
public class StartMenuView : MonoBehaviour
{
    [Tooltip("Ordered campaign used to find the next unfinished level.")]
    [SerializeField] private LevelSequence levelSequence;

    [Tooltip("Handoff asset the chosen level is written to before the game scene loads.")]
    [SerializeField] private LevelSelectionState levelSelection;

    [Tooltip("Endless rectangle level. Kept out of the campaign sequence.")]
    [SerializeField] private LevelDefinition classicLevel;

    [SerializeField] private Button playButton;
    [SerializeField] private Button levelSelectButton;
    [SerializeField] private Button classicButton;

    private void Awake()
    {
        Bind(playButton, OnPlayClicked);
        Bind(levelSelectButton, OnLevelSelectClicked);
        Bind(classicButton, OnClassicClicked);
    }

    private void OnDestroy()
    {
        Unbind(playButton, OnPlayClicked);
        Unbind(levelSelectButton, OnLevelSelectClicked);
        Unbind(classicButton, OnClassicClicked);
    }

    private void OnPlayClicked()
    {
        if (levelSelection == null)
        {
            SceneFlow.LoadGame();
            return;
        }

        levelSelection.Clear();
        LevelDefinition next = levelSelection.ResolveOrDefault(levelSequence, out int index);
        if (next != null)
        {
            levelSelection.Select(next, index);
        }

        SceneFlow.LoadGame();
    }

    private void OnLevelSelectClicked()
    {
        if (levelSelection != null)
        {
            levelSelection.Clear();
        }

        SceneFlow.LoadLevelSelect();
    }

    private void OnClassicClicked()
    {
        if (classicLevel == null)
        {
            Debug.LogWarning($"{nameof(StartMenuView)}: classic level is missing.", this);
            return;
        }

        if (levelSelection != null)
        {
            levelSelection.Select(classicLevel, -1);
        }

        SceneFlow.LoadGame();
    }

    private static void Bind(Button button, UnityAction action)
    {
        if (button == null)
        {
            return;
        }

        button.onClick.RemoveListener(action);
        button.onClick.AddListener(action);
    }

    private static void Unbind(Button button, UnityAction action)
    {
        if (button == null)
        {
            return;
        }

        button.onClick.RemoveListener(action);
    }
}
