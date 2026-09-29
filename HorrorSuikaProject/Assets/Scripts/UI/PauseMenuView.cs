using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Pause overlay: Music, Sound FX, and Interface volume sliders plus Resume, Main Menu, and Quit.
/// Opened by the HUD Pause button or the Pause action (Tab, Escape, gamepad Select). The run
/// itself is frozen by <see cref="GameController.SetPaused"/>; this view only shows it.
/// </summary>
public class PauseMenuView : MonoBehaviour
{
    private const string GameplayMapName = "Gameplay";
    private const string PauseActionName = "Pause";

    [SerializeField] private GameController gameController;
    [SerializeField] private InputActionAsset controlsAsset;

    [Tooltip("Dimmer and card. Hidden while the run is live.")]
    [SerializeField] private GameObject overlay;

    [Tooltip("HUD button beside Reset and Home.")]
    [SerializeField] private Button hudPauseButton;

    [SerializeField] private Button resumeButton;
    [SerializeField] private Button mainMenuButton;

    [Tooltip("Hidden in WebGL builds, where a browser tab cannot quit itself.")]
    [SerializeField] private Button quitButton;

    private InputAction pauseAction;

    private void Awake()
    {
        Bind(hudPauseButton, OnHudPauseClicked);
        Bind(resumeButton, OnResumeClicked);
        Bind(mainMenuButton, OnMainMenuClicked);
        Bind(quitButton, OnQuitClicked);

#if UNITY_WEBGL && !UNITY_EDITOR
        if (quitButton != null)
        {
            quitButton.gameObject.SetActive(false);
        }
#endif

        if (overlay != null)
        {
            overlay.SetActive(false);
        }

        if (controlsAsset != null)
        {
            pauseAction = controlsAsset.FindActionMap(GameplayMapName, true).FindAction(PauseActionName, false);
        }

        if (pauseAction == null)
        {
            Debug.LogWarning($"{nameof(PauseMenuView)}: no '{PauseActionName}' action in the {GameplayMapName} map; only the HUD button opens the menu.", this);
        }
    }

    private void OnEnable()
    {
        if (pauseAction != null)
        {
            pauseAction.performed += OnPausePerformed;
        }

        if (gameController != null)
        {
            gameController.PausedChanged += OnPausedChanged;
        }
    }

    private void OnDisable()
    {
        if (pauseAction != null)
        {
            pauseAction.performed -= OnPausePerformed;
        }

        if (gameController != null)
        {
            gameController.PausedChanged -= OnPausedChanged;
        }
    }

    private void OnDestroy()
    {
        Unbind(hudPauseButton, OnHudPauseClicked);
        Unbind(resumeButton, OnResumeClicked);
        Unbind(mainMenuButton, OnMainMenuClicked);
        Unbind(quitButton, OnQuitClicked);
    }

    private void OnPausePerformed(InputAction.CallbackContext context)
    {
        if (gameController != null)
        {
            gameController.SetPaused(!gameController.IsPaused);
        }
    }

    private void OnPausedChanged(bool paused)
    {
        if (overlay != null)
        {
            overlay.SetActive(paused);
        }
    }

    private void OnHudPauseClicked()
    {
        if (gameController != null)
        {
            gameController.SetPaused(true);
        }
    }

    private void OnResumeClicked()
    {
        if (gameController != null)
        {
            gameController.SetPaused(false);
        }
    }

    private void OnMainMenuClicked()
    {
        if (gameController != null)
        {
            gameController.ReturnToStart();
        }
    }

    private void OnQuitClicked()
    {
        AudioVolumeSettings.Save();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
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
        if (button != null)
        {
            button.onClick.RemoveListener(action);
        }
    }
}
