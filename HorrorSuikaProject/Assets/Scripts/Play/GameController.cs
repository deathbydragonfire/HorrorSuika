using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Owns the run state machine, runs the active level, and wires every system together.
/// The only integration point.
/// </summary>
public class GameController : MonoBehaviour
{
    /// <summary>High-level run state; the HUD reads only from here.</summary>
    public enum GameState
    {
        Ready,
        Playing,
        VictoryPending,
        Victory,
        GameOver
    }

    /// <summary>Why a level was lost.</summary>
    public enum LevelFailureReason
    {
        Overflow,
        OutOfDrops,
        TimeExpired
    }

    private const string GameplayMapName = "Gameplay";
    private const string RestartActionName = "Restart";
    private const int TargetFrameRate = 60;
    private const int PrewarmInstancesPerTier = 12;
    private const int FallbackRandomSeed = 0;
    private const float SettleVelocityThreshold = 0.05f;
    private const float DropLimitSettleDelay = 1.5f;

    [Header("Data")]
    [SerializeField] private MergeItemTierTable tierTable;
    [SerializeField] private GameObject itemPrefab;
    [SerializeField] private InputActionAsset controlsAsset;

    [Header("Level")]
    [Tooltip("Level played when no sequence or selection resolves one. The only level reference Phase 2 needs.")]
    [SerializeField] private LevelDefinition startingLevel;

    [Tooltip("Ordered level list used for progression and Next Level.")]
    [SerializeField] private LevelSequence levelSequence;

    [Tooltip("Handoff asset carrying the level chosen in the level-select scene.")]
    [SerializeField] private LevelSelectionState levelSelection;

    [Header("Systems")]
    [SerializeField] private MergeItemPool itemPool;
    [SerializeField] private MergeCoordinator mergeCoordinator;
    [SerializeField] private ItemDropper itemDropper;
    [SerializeField] private NextItemQueue nextItemQueue;
    [SerializeField] private GameOverWatcher gameOverWatcher;
    [SerializeField] private ScoreController scoreController;
    [SerializeField] private LevelObjectiveTracker objectiveTracker;
    [SerializeField] private PlayfieldEscapeGuard escapeGuard;

    [Header("Scene")]
    [SerializeField] private PlayfieldBounds playfieldBounds;
    [SerializeField] private PlayfieldShapeHost shapeHost;
    [SerializeField] private PortraitCameraFitter cameraFitter;
    [SerializeField] private Camera gameplayCamera;
    [SerializeField] private Transform itemRoot;
    [SerializeField] private GameHudView hudView;

    private InputAction restartAction;
    private Coroutine settleRoutine;
    private MergeItemTierTable activeTierTable;
    private int dropCount;
    private float levelTimeRemaining;
    private bool dropLimitReached;
    private float dropLimitSettleTimer;

    /// <summary>Raised whenever the run state changes.</summary>
    public event Action<GameState> StateChanged;

    /// <summary>Current run state.</summary>
    public GameState State { get; private set; } = GameState.Ready;

    /// <summary>Why the last run was lost; only meaningful in <see cref="GameState.GameOver"/>.</summary>
    public LevelFailureReason FailureReason { get; private set; } = LevelFailureReason.Overflow;

    /// <summary>The level currently loaded.</summary>
    public LevelDefinition CurrentLevel { get; private set; }

    /// <summary>Sequence index of the current level, or -1 when it is not part of the sequence.</summary>
    public int CurrentLevelIndex { get; private set; } = -1;

    /// <summary>Tier table the current level runs on.</summary>
    public MergeItemTierTable ActiveTierTable => activeTierTable != null ? activeTierTable : tierTable;

    /// <summary>Drops left before the level's drop limit is hit; -1 when unlimited.</summary>
    public int DropsRemaining => CurrentLevel != null && CurrentLevel.DropLimit > 0
        ? Mathf.Max(0, CurrentLevel.DropLimit - dropCount)
        : -1;

    /// <summary>Seconds left on the level's time limit; -1 when unlimited.</summary>
    public float TimeRemaining => CurrentLevel != null && CurrentLevel.TimeLimitSeconds > 0f
        ? Mathf.Max(0f, levelTimeRemaining)
        : -1f;

    private void Awake()
    {
        Application.targetFrameRate = TargetFrameRate;
        Screen.sleepTimeout = SleepTimeout.NeverSleep;

        ResolveLevel();
        activeTierTable = CurrentLevel != null ? CurrentLevel.ResolveTierTable(tierTable) : tierTable;

        itemPool.Configure(activeTierTable, itemPrefab, itemRoot);
        mergeCoordinator.Configure(activeTierTable, itemPool);
        nextItemQueue.Configure(activeTierTable, ResolveRandomSeed(), ResolveInitialTierCount(), ResolveMaxTierCount());
        itemDropper.Configure(activeTierTable, itemPool, playfieldBounds, nextItemQueue, gameplayCamera);
        gameOverWatcher.Configure(playfieldBounds, itemPool, itemDropper);

        if (shapeHost != null)
        {
            shapeHost.Configure(null, playfieldBounds, cameraFitter);
        }

        if (escapeGuard != null)
        {
            escapeGuard.Configure(playfieldBounds, itemPool);
            escapeGuard.ItemEscaped += OnItemEscaped;
        }

        if (objectiveTracker != null)
        {
            objectiveTracker.Configure(itemPool, mergeCoordinator, itemDropper, scoreController);
            objectiveTracker.AllObjectivesComplete += OnAllObjectivesComplete;
        }

        if (cameraFitter != null)
        {
            cameraFitter.Configure(playfieldBounds);
        }

        if (hudView != null)
        {
            hudView.Configure(activeTierTable, nextItemQueue, scoreController, this, objectiveTracker);
        }

        mergeCoordinator.MergePerformed += OnMergePerformed;
        mergeCoordinator.TopTierPopped += OnTopTierPopped;
        gameOverWatcher.GameOverTriggered += OnGameOverTriggered;
        itemDropper.ItemDropped += OnItemDropped;

        SetUpRestartAction();
        itemPool.Prewarm(PrewarmInstancesPerTier, ResolveMaxTierCount());
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
        itemDropper.ItemDropped -= OnItemDropped;

        if (objectiveTracker != null)
        {
            objectiveTracker.AllObjectivesComplete -= OnAllObjectivesComplete;
        }

        if (escapeGuard != null)
        {
            escapeGuard.ItemEscaped -= OnItemEscaped;
        }

        if (restartAction != null)
        {
            restartAction.performed -= OnRestartPerformed;
        }
    }

    /// <summary>Applies the current level, resets every system, and begins a fresh run.</summary>
    public void StartGame()
    {
        activeTierTable = CurrentLevel != null ? CurrentLevel.ResolveTierTable(tierTable) : tierTable;

        if (shapeHost != null && CurrentLevel != null && CurrentLevel.ShapePrefab != null)
        {
            shapeHost.Apply(CurrentLevel.ShapePrefab);
        }

        nextItemQueue.Configure(activeTierTable, ResolveRandomSeed(), ResolveInitialTierCount(), ResolveMaxTierCount());

        if (objectiveTracker != null)
        {
            objectiveTracker.SetLevel(CurrentLevel, activeTierTable);
        }

        dropCount = 0;
        dropLimitReached = false;
        dropLimitSettleTimer = 0f;
        levelTimeRemaining = CurrentLevel != null ? CurrentLevel.TimeLimitSeconds : 0f;

        scoreController.Reset();
        nextItemQueue.Reset();
        gameOverWatcher.SetActive(true);

        if (escapeGuard != null)
        {
            escapeGuard.SetActive(true);
        }

        itemDropper.SetInputEnabled(true);
        itemDropper.PrepareNext();

        if (objectiveTracker != null)
        {
            objectiveTracker.SetActive(true);
        }

        SetState(GameState.Playing);
    }

    /// <summary>Clears the board and starts a new run without reloading the scene.</summary>
    public void Restart()
    {
        StopSettleRoutine();
        itemDropper.SetInputEnabled(false);
        itemDropper.ClearHeldItem();
        mergeCoordinator.ClearQueue();
        itemPool.DespawnAll();
        StartGame();
    }

    /// <summary>Swaps in a different level and restarts the run on it.</summary>
    public void LoadLevel(LevelDefinition level, int index)
    {
        if (level == null)
        {
            Debug.LogWarning($"{nameof(GameController)}.{nameof(LoadLevel)}: null level ignored.", this);
            return;
        }

        StopSettleRoutine();
        CurrentLevel = level;
        CurrentLevelIndex = index >= 0 ? index : (levelSequence != null ? levelSequence.IndexOf(level) : -1);

        itemDropper.SetInputEnabled(false);
        itemDropper.ClearHeldItem();
        mergeCoordinator.ClearQueue();
        itemPool.DespawnAll();
        StartGame();
    }

    /// <summary>Loads the level after the current one, if the sequence has one. Returns false otherwise.</summary>
    public bool TryAdvanceToNextLevel()
    {
        if (levelSequence == null || !levelSequence.TryGetNext(CurrentLevel, out LevelDefinition next, out int nextIndex))
        {
            return false;
        }

        LoadLevel(next, nextIndex);
        return true;
    }

    /// <summary>True when a following level exists, without loading it.</summary>
    public bool HasNextLevel()
    {
        return levelSequence != null && levelSequence.TryGetNext(CurrentLevel, out _, out _);
    }

    /// <summary>Returns to the level-select scene.</summary>
    public void ReturnToLevelSelect()
    {
        if (levelSelection != null)
        {
            levelSelection.Clear();
        }

        mergeCoordinator.ClearQueue();
        SceneFlow.LoadLevelSelect();
    }

    private void Update()
    {
        if (State != GameState.Playing)
        {
            return;
        }

        TickTimeLimit();
        TickDropLimit();
    }

    private void TickTimeLimit()
    {
        if (CurrentLevel == null || CurrentLevel.TimeLimitSeconds <= 0f)
        {
            return;
        }

        levelTimeRemaining -= Time.deltaTime;
        if (levelTimeRemaining <= 0f)
        {
            levelTimeRemaining = 0f;
            FailLevel(LevelFailureReason.TimeExpired);
        }
    }

    private void TickDropLimit()
    {
        if (!dropLimitReached)
        {
            return;
        }

        // Failing the instant the last drop leaves the hand would rob the player of the cascade it
        // causes, so the check waits for the board to settle. A cascade that wins gets there first
        // because FailLevel no-ops outside Playing.
        dropLimitSettleTimer -= Time.deltaTime;
        if (dropLimitSettleTimer > 0f || !IsBoardAtRest())
        {
            return;
        }

        FailLevel(LevelFailureReason.OutOfDrops);
    }

    private void ResolveLevel()
    {
        if (levelSelection != null)
        {
            LevelDefinition resolved = levelSelection.ResolveOrDefault(levelSequence, out int resolvedIndex);
            if (resolved != null)
            {
                CurrentLevel = resolved;
                CurrentLevelIndex = resolvedIndex;
                return;
            }
        }

        CurrentLevel = startingLevel;
        CurrentLevelIndex = levelSequence != null ? levelSequence.IndexOf(startingLevel) : -1;

        if (CurrentLevel == null)
        {
            Debug.LogWarning($"{nameof(GameController)}: no level resolved; running with the default tier table and shape.", this);
        }
    }

    private int ResolveRandomSeed()
    {
        return CurrentLevel != null ? CurrentLevel.RandomSeed : FallbackRandomSeed;
    }

    private int ResolveInitialTierCount()
    {
        return CurrentLevel != null ? CurrentLevel.GetInitialSpawnableTierCount(activeTierTable) : 0;
    }

    private int ResolveMaxTierCount()
    {
        if (CurrentLevel != null)
        {
            return CurrentLevel.GetMaxSpawnableTierCount(activeTierTable);
        }

        return activeTierTable != null ? activeTierTable.MaxSpawnableTierCount : 1;
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

    private void OnItemDropped(MergeItem item)
    {
        dropCount++;

        if (CurrentLevel == null || CurrentLevel.DropLimit <= 0 || dropCount < CurrentLevel.DropLimit)
        {
            return;
        }

        dropLimitReached = true;
        dropLimitSettleTimer = DropLimitSettleDelay;
    }

    private void OnItemEscaped(MergeItem item)
    {
        Debug.LogWarning($"{nameof(GameController)}: an item left the container and was despawned; check the shape's wall thickness.", this);
    }

    private void OnGameOverTriggered()
    {
        FailLevel(LevelFailureReason.Overflow);
    }

    private void FailLevel(LevelFailureReason reason)
    {
        if (State != GameState.Playing)
        {
            return;
        }

        FailureReason = reason;
        itemDropper.SetInputEnabled(false);
        itemDropper.ClearHeldItem();
        gameOverWatcher.SetActive(false);

        if (objectiveTracker != null)
        {
            objectiveTracker.SetActive(false);
        }

        SetState(GameState.GameOver);
    }

    private void OnAllObjectivesComplete()
    {
        if (State != GameState.Playing)
        {
            return;
        }

        // The watcher is cut immediately: an overflow during the settle window must not steal a won level.
        gameOverWatcher.SetActive(false);

        if (objectiveTracker != null)
        {
            objectiveTracker.SetActive(false);
        }

        itemDropper.SetInputEnabled(false);
        itemDropper.ClearHeldItem();
        SetState(GameState.VictoryPending);

        StopSettleRoutine();
        settleRoutine = StartCoroutine(SettleThenWin());
    }

    private IEnumerator SettleThenWin()
    {
        float timeout = CurrentLevel != null ? CurrentLevel.VictorySettleTimeout : 1f;
        float elapsed = 0f;

        while (elapsed < timeout && !IsBoardAtRest())
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        settleRoutine = null;

        if (CurrentLevel != null)
        {
            LevelProgressStore.MarkCompleted(CurrentLevel.LevelId, CurrentLevelIndex, scoreController.Score);
        }

        SetState(GameState.Victory);
    }

    private bool IsBoardAtRest()
    {
        var items = itemPool.ActiveItems;
        MergeItem held = itemDropper != null ? itemDropper.HeldItem : null;

        for (int i = 0; i < items.Count; i++)
        {
            MergeItem item = items[i];
            if (item == null || item == held || item.IsConsumed)
            {
                continue;
            }

            if (item.IsSettling)
            {
                return false;
            }

            Rigidbody body = item.Body;
            if (body != null && !body.isKinematic && body.linearVelocity.sqrMagnitude > SettleVelocityThreshold * SettleVelocityThreshold)
            {
                return false;
            }
        }

        return true;
    }

    private void StopSettleRoutine()
    {
        if (settleRoutine == null)
        {
            return;
        }

        StopCoroutine(settleRoutine);
        settleRoutine = null;
    }

    private void SetState(GameState state)
    {
        State = state;
        StateChanged?.Invoke(state);
    }
}
