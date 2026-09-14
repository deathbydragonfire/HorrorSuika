using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Owns the Ready/Playing/GameOver state machine and wires every system together. The only integration point.
/// </summary>
public class GameController : MonoBehaviour
{
    /// <summary>High-level run state; the HUD reads only from here.</summary>
    public enum GameState
    {
        Ready,
        Playing,
        GameOver
    }

    private const string GameplayMapName = "Gameplay";
    private const string RestartActionName = "Restart";
    private const int TargetFrameRate = 60;
    private const int PrewarmInstancesPerTier = 12;
    private const int RandomSeed = 0;

    [Header("Data")]
    [SerializeField] private MergeItemTierTable tierTable;
    [SerializeField] private GameObject itemPrefab;
    [SerializeField] private InputActionAsset controlsAsset;

    [Header("Systems")]
    [SerializeField] private MergeItemPool itemPool;
    [SerializeField] private MergeCoordinator mergeCoordinator;
    [SerializeField] private ItemDropper itemDropper;
    [SerializeField] private NextItemQueue nextItemQueue;
    [SerializeField] private GameOverWatcher gameOverWatcher;
    [SerializeField] private ScoreController scoreController;

    [Header("Scene")]
    [SerializeField] private PlayfieldBounds playfieldBounds;
    [SerializeField] private PortraitCameraFitter cameraFitter;
    [SerializeField] private Camera gameplayCamera;
    [SerializeField] private Transform itemRoot;
    [SerializeField] private GameHudView hudView;

    private InputAction restartAction;

    /// <summary>Raised whenever the run state changes.</summary>
    public event Action<GameState> StateChanged;

    /// <summary>Current run state.</summary>
    public GameState State { get; private set; } = GameState.Ready;

    private void Awake()
    {
        Application.targetFrameRate = TargetFrameRate;
        Screen.sleepTimeout = SleepTimeout.NeverSleep;

        itemPool.Configure(tierTable, itemPrefab, itemRoot);
        mergeCoordinator.Configure(tierTable, itemPool);
        nextItemQueue.Configure(tierTable, RandomSeed);
        itemDropper.Configure(tierTable, itemPool, playfieldBounds, nextItemQueue, gameplayCamera);
        gameOverWatcher.Configure(playfieldBounds, itemPool, itemDropper);

        if (cameraFitter != null)
        {
            cameraFitter.Configure(playfieldBounds);
        }

        if (hudView != null)
        {
            hudView.Configure(tierTable, nextItemQueue, scoreController, this);
        }

        mergeCoordinator.MergePerformed += OnMergePerformed;
        mergeCoordinator.TopTierPopped += OnTopTierPopped;
        gameOverWatcher.GameOverTriggered += OnGameOverTriggered;

        SetUpRestartAction();
        itemPool.Prewarm(PrewarmInstancesPerTier);
    }

    private void Start()
    {
        StartGame();
    }

    private void OnDestroy()
    {
        mergeCoordinator.MergePerformed -= OnMergePerformed;
        mergeCoordinator.TopTierPopped -= OnTopTierPopped;
        gameOverWatcher.GameOverTriggered -= OnGameOverTriggered;

        if (restartAction != null)
        {
            restartAction.performed -= OnRestartPerformed;
        }
    }

    /// <summary>Resets every system and begins a fresh run.</summary>
    public void StartGame()
    {
        scoreController.Reset();
        nextItemQueue.Reset();
        gameOverWatcher.SetActive(true);
        itemDropper.SetInputEnabled(true);
        itemDropper.PrepareNext();
        SetState(GameState.Playing);
    }

    /// <summary>Clears the board and starts a new run without reloading the scene.</summary>
    public void Restart()
    {
        itemDropper.SetInputEnabled(false);
        itemDropper.ClearHeldItem();
        MergeCoordinatorClearQueue();
        itemPool.DespawnAll();
        StartGame();
    }

    private void MergeCoordinatorClearQueue()
    {
        if (mergeCoordinator != null)
        {
            mergeCoordinator.ClearQueue();
        }
    }

    private void SetUpRestartAction()
    {
        if (controlsAsset == null)
        {
            return;
        }

        InputActionMap gameplayMap = controlsAsset.FindActionMap(GameplayMapName, true);
        gameplayMap.Enable();

        restartAction = gameplayMap.FindAction(RestartActionName, true);
        restartAction.performed += OnRestartPerformed;
    }

    private void OnRestartPerformed(InputAction.CallbackContext context)
    {
        Restart();
    }

    private void OnMergePerformed(int resultTierIndex, Vector3 position, int awardedScore)
    {
        scoreController.Add(awardedScore);

        // A tier becomes droppable once the player has produced it by merging.
        nextItemQueue.UnlockTier(resultTierIndex);
    }

    private void OnTopTierPopped(Vector3 position, int awardedScore)
    {
        scoreController.Add(awardedScore);
    }

    private void OnGameOverTriggered()
    {
        itemDropper.SetInputEnabled(false);
        itemDropper.ClearHeldItem();
        gameOverWatcher.SetActive(false);
        SetState(GameState.GameOver);
    }

    private void SetState(GameState state)
    {
        State = state;
        StateChanged?.Invoke(state);
    }
}
