using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Reads input from touch, mouse, or keyboard/gamepad, positions the held item above the container,
/// and releases it with a cooldown. Touch and mouse share a single "release to drop" rule.
/// </summary>
public class ItemDropper : MonoBehaviour
{
    /// <summary>Which input family currently owns the aim.</summary>
    public enum AimSource
    {
        Pointer,
        Axis
    }

    private const string GameplayMapName = "Gameplay";
    private const string PointActionName = "Point";
    private const string PointerPressActionName = "PointerPress";
    private const string AimDeltaActionName = "AimDelta";
    private const string MoveDropperActionName = "MoveDropper";
    private const string DropButtonActionName = "DropButton";

    private const float DropperSpeed = 6f;
    private const float DropCooldownSeconds = 0.45f;
    private const float MouseActivationThreshold = 0.01f;
    private const float StickDeadzone = 0.15f;

    [SerializeField] private InputActionAsset controlsAsset;

    private MergeItemTierTable tierTable;
    private MergeItemPool itemPool;
    private PlayfieldBounds bounds;
    private NextItemQueue nextItemQueue;
    private Camera gameplayCamera;

    private InputActionMap gameplayMap;
    private InputAction pointAction;
    private InputAction pointerPressAction;
    private InputAction aimDeltaAction;
    private InputAction moveDropperAction;
    private InputAction dropButtonAction;

    private AimSource aimSource = AimSource.Pointer;
    private bool inputEnabled;
    private bool pointerActive;
    private float aimX;
    private float dropCooldownTimer;

    /// <summary>Raised with the item that was just released to physics.</summary>
    public event Action<MergeItem> ItemDropped;

    /// <summary>The kinematic item currently hovering above the container, if any.</summary>
    public MergeItem HeldItem { get; private set; }

    /// <summary>Injects every dependency the dropper needs.</summary>
    public void Configure(MergeItemTierTable table, MergeItemPool pool, PlayfieldBounds playfieldBounds, NextItemQueue queue, Camera camera)
    {
        tierTable = table;
        itemPool = pool;
        bounds = playfieldBounds;
        nextItemQueue = queue;
        gameplayCamera = camera;

        ResolveActions();
    }

    /// <summary>Spawns the current-tier item as a kinematic held item above the container.</summary>
    public void PrepareNext()
    {
        if (itemPool == null || bounds == null || nextItemQueue == null || tierTable == null)
        {
            return;
        }

        if (HeldItem != null)
        {
            return;
        }

        HeldItem = itemPool.Spawn(nextItemQueue.CurrentTier, bounds.GetDropPosition(aimX), true);
        if (HeldItem == null)
        {
            return;
        }

        ApplyAim(aimX);
    }

    /// <summary>
    /// Enables or disables drop handling; disabling also clears any in-progress pointer drag.
    /// The action map itself stays enabled so the Restart action keeps working on game over.
    /// </summary>
    public void SetInputEnabled(bool enabled)
    {
        inputEnabled = enabled;
        pointerActive = false;
    }

    /// <summary>Despawns the held item without dropping it.</summary>
    public void ClearHeldItem()
    {
        if (HeldItem == null)
        {
            return;
        }

        itemPool.Despawn(HeldItem);
        HeldItem = null;
    }

    private void ResolveActions()
    {
        if (controlsAsset == null)
        {
            Debug.LogError($"{nameof(ItemDropper)}: no {nameof(InputActionAsset)} assigned.", this);
            return;
        }

        if (gameplayMap != null)
        {
            return;
        }

        gameplayMap = controlsAsset.FindActionMap(GameplayMapName, true);
        pointAction = gameplayMap.FindAction(PointActionName, true);
        pointerPressAction = gameplayMap.FindAction(PointerPressActionName, true);
        aimDeltaAction = gameplayMap.FindAction(AimDeltaActionName, true);
        moveDropperAction = gameplayMap.FindAction(MoveDropperActionName, true);
        dropButtonAction = gameplayMap.FindAction(DropButtonActionName, true);

        pointerPressAction.started += OnPointerPressStarted;
        pointerPressAction.canceled += OnPointerPressCanceled;
        dropButtonAction.performed += OnDropButtonPerformed;
    }

    private void OnEnable()
    {
        Application.focusChanged += OnApplicationFocusChanged;
    }

    private void OnDisable()
    {
        Application.focusChanged -= OnApplicationFocusChanged;
        pointerActive = false;
    }

    private void OnDestroy()
    {
        if (pointerPressAction != null)
        {
            pointerPressAction.started -= OnPointerPressStarted;
            pointerPressAction.canceled -= OnPointerPressCanceled;
        }

        if (dropButtonAction != null)
        {
            dropButtonAction.performed -= OnDropButtonPerformed;
        }
    }

    private void OnApplicationFocusChanged(bool hasFocus)
    {
        if (!hasFocus)
        {
            // A dropped 'canceled' callback must never release the held item on return.
            pointerActive = false;
        }
    }

    private void Update()
    {
        if (dropCooldownTimer > 0f)
        {
            dropCooldownTimer -= Time.deltaTime;
        }

        if (!inputEnabled || HeldItem == null)
        {
            return;
        }

        UpdateAxisAim();
        UpdatePointerAim();
    }

    private void UpdateAxisAim()
    {
        if (moveDropperAction == null)
        {
            return;
        }

        float axis = moveDropperAction.ReadValue<float>();
        if (Mathf.Abs(axis) <= StickDeadzone)
        {
            return;
        }

        aimSource = AimSource.Axis;
        ApplyAim(aimX + (axis * DropperSpeed * Time.deltaTime));
    }

    private void UpdatePointerAim()
    {
        if (pointAction == null || gameplayCamera == null)
        {
            return;
        }

        bool mouseIsMoving = aimDeltaAction != null
            && aimDeltaAction.ReadValue<Vector2>().sqrMagnitude > MouseActivationThreshold * MouseActivationThreshold;

        if (mouseIsMoving)
        {
            aimSource = AimSource.Pointer;
        }

        if (!pointerActive && !(aimSource == AimSource.Pointer && mouseIsMoving))
        {
            return;
        }

        ApplyAim(ReadPointerWorldX());
    }

    private float ReadPointerWorldX()
    {
        Vector2 screenPosition = pointAction.ReadValue<Vector2>();
        // Orthographic projection returns a meaningless Z, so only X is used.
        return gameplayCamera.ScreenToWorldPoint(new Vector3(screenPosition.x, screenPosition.y, 0f)).x;
    }

    private void ApplyAim(float requestedX)
    {
        if (bounds == null)
        {
            return;
        }

        float radius = HeldItem != null ? HeldItem.Radius : 0f;
        aimX = bounds.ClampDropX(requestedX, radius);

        if (HeldItem != null)
        {
            HeldItem.transform.position = bounds.GetDropPosition(aimX);
        }
    }

    private void OnPointerPressStarted(InputAction.CallbackContext context)
    {
        if (!inputEnabled || HeldItem == null)
        {
            return;
        }

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            // A press that begins on interactive UI (e.g. Restart) never reaches the dropper.
            return;
        }

        pointerActive = true;
        aimSource = AimSource.Pointer;
        ApplyAim(ReadPointerWorldX());
    }

    private void OnPointerPressCanceled(InputAction.CallbackContext context)
    {
        if (!pointerActive)
        {
            return;
        }

        pointerActive = false;
        TryDrop();
    }

    private void OnDropButtonPerformed(InputAction.CallbackContext context)
    {
        aimSource = AimSource.Axis;
        TryDrop();
    }

    private void TryDrop()
    {
        if (!inputEnabled || HeldItem == null || dropCooldownTimer > 0f)
        {
            return;
        }

        MergeItem dropped = HeldItem;
        HeldItem = null;

        dropped.Release();
        dropCooldownTimer = DropCooldownSeconds;

        ItemDropped?.Invoke(dropped);
        nextItemQueue.Advance();
        PrepareNext();
    }
}
