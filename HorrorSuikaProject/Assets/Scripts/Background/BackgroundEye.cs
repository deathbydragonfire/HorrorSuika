using UnityEngine;

/// <summary>
/// A background eye. Its lids stay shut while it is deep in the fog, crack open as it surfaces,
/// hesitate, then open fully and snap onto the player. While out it darts between saccades,
/// sometimes tracks the pointer, and blinks. During a surge every eye locks on and stops blinking.
/// </summary>
[DisallowMultipleComponent]
public class BackgroundEye : BackgroundApparition
{
    private const string DefaultBlendShapeName = "eye open";
    private const float LidOpenWeight = 100f;

    [Header("Rig")]
    [SerializeField, Tooltip("Inner globe that rotates to look. The lid stays fixed.")]
    private Transform globe;

    [SerializeField, Tooltip("Eyelid skinned mesh with the open/close blend shape.")]
    private SkinnedMeshRenderer eyelid;

    [SerializeField, Tooltip("Blend shape that is fully open at 100 and closed at 0.")]
    private string blendShapeName = DefaultBlendShapeName;

    [SerializeField, Tooltip("Iris direction in the globe's local space. The CubeOrigins eyeballs look along -Y.")]
    private Vector3 localLookAxis = Vector3.down;

    [Header("Waking")]
    [SerializeField, Range(0f, 1f), Tooltip("Point in the surfacing where the lids first crack open.")]
    private float lidCrackStart = 0.38f;

    [SerializeField, Range(0f, 1f), Tooltip("How far the lids open on the first crack before hesitating.")]
    private float lidCrackAmount = 0.28f;

    [SerializeField, Range(0f, 1f), Tooltip("Point in the surfacing where the lids open the rest of the way.")]
    private float lidFullOpenStart = 0.7f;

    [Header("Gaze")]
    [SerializeField, Range(1f, 55f), Tooltip("Maximum degrees the iris turns left or right.")]
    private float maxYaw = 40f;

    [SerializeField, Range(1f, 45f), Tooltip("Maximum degrees the iris turns up or down.")]
    private float maxPitch = 20f;

    [SerializeField, Tooltip("Seconds the gaze holds between darts, randomised between x and y.")]
    private Vector2 saccadeHold = new Vector2(0.35f, 2.2f);

    [SerializeField, Min(1f), Tooltip("How fast the iris snaps to a new target. Real saccades are very fast.")]
    private float saccadeSpeed = 24f;

    [SerializeField, Range(0f, 1f), Tooltip("Chance each new gaze target is the player's pointer.")]
    private float pointerLookChance = 0.4f;

    [SerializeField, Min(0f), Tooltip("Degrees of constant fine tremor, so the eye never looks mechanical.")]
    private float tremor = 0.7f;

    [Header("Blinking")]
    [SerializeField, Tooltip("Seconds between blinks, randomised between x and y.")]
    private Vector2 blinkInterval = new Vector2(1.8f, 5.5f);

    [SerializeField, Min(0.05f), Tooltip("Seconds for one close-and-open blink. Slightly slow reads as wrong.")]
    private float blinkDuration = 0.2f;

    [SerializeField, Range(0f, 1f), Tooltip("Chance a blink is immediately followed by a second one.")]
    private float doubleBlinkChance = 0.22f;

    [Header("Unease")]
    [SerializeField, Range(0f, 1f), Tooltip("Chance the eye stays open, staring, as it sinks back into the fog.")]
    private float stareOnRetreatChance = 0.5f;

    [SerializeField, Tooltip("Seconds between small jolts of the whole eye, randomised between x and y.")]
    private Vector2 twitchInterval = new Vector2(3f, 9f);

    [SerializeField, Min(0f), Tooltip("World units a twitch displaces the eye, as a fraction of its radius.")]
    private float twitchStrength = 0.08f;

    private int blendShapeIndex = -1;
    private Quaternion restLocalRotation;
    private float lidOpen;
    private float yaw;
    private float pitch;
    private float targetYaw;
    private float targetPitch;
    private bool trackingPointer;
    private float saccadeTimer;
    private float blinkTimer;
    private float blinkElapsed = -1f;
    private bool pendingDoubleBlink;
    private bool stareOnRetreat;
    private float retreatStartLid;
    private float twitchTimer;
    private float twitchTime = -1f;
    private Vector3 twitchDirection;
    private float noiseSeed;
    private BackgroundEye leader;

    protected override void Awake()
    {
        base.Awake();
        ResolveRig();
        if (globe != null)
        {
            restLocalRotation = globe.localRotation;
        }
    }

    /// <summary>
    /// Slaves this eye's gaze, blinks, and waking to <paramref name="source"/>, so the pair reads as
    /// one face in the dark. Call right after both have begun.
    /// </summary>
    public void FollowGaze(BackgroundEye source)
    {
        leader = source != this ? source : null;
        if (leader == null)
        {
            return;
        }

        MatchTimeline(leader);
        noiseSeed = leader.noiseSeed;
        stareOnRetreat = leader.stareOnRetreat;
        yaw = targetYaw = leader.targetYaw;
        pitch = targetPitch = leader.targetPitch;
    }

    protected override void OnBegin()
    {
        leader = null;
        stareOnRetreat = Random.value < stareOnRetreatChance;
        noiseSeed = Random.value * 100f;
        lidOpen = 0f;
        blinkElapsed = -1f;
        pendingDoubleBlink = false;
        trackingPointer = false;

        // Starts groggy, gaze sagging off to one side.
        yaw = targetYaw = Random.Range(-maxYaw, maxYaw) * 0.7f;
        pitch = targetPitch = -maxPitch * Random.Range(0.4f, 0.9f);
        ScheduleBlink();
        twitchTimer = Random.Range(twitchInterval.x, twitchInterval.y);
        ApplyLid(0f);
        ApplyGaze(0f);
    }

    protected override void OnEmerging(float t, float dt)
    {
        float previousLid = lidOpen;
        lidOpen = EvaluateWakingLid(t);
        if (previousLid <= 0f && lidOpen > 0f)
        {
            PlaySound(ApparitionSound.Open);
        }

        // Wakes, and the first thing it does is find you.
        if (t > lidFullOpenStart + 0.1f && !trackingPointer)
        {
            trackingPointer = true;
            saccadeTimer = Random.Range(saccadeHold.x, saccadeHold.y) + 0.8f;
        }

        CopyLeaderGaze();
        UpdateGaze(dt);
        ApplyLid(lidOpen);
    }

    protected override void OnLingering(float dt)
    {
        lidOpen = 1f;
        float surge = Background != null ? Background.Surge : 0f;
        bool locked = surge > 0.25f;

        if (HasLeader)
        {
            CopyLeaderGaze();
            blinkElapsed = leader.blinkElapsed;
        }
        else if (locked)
        {
            trackingPointer = true;
            saccadeTimer = 0.5f;
            blinkElapsed = -1f;
        }
        else
        {
            saccadeTimer -= dt;
            if (saccadeTimer <= 0f)
            {
                PickSaccade();
            }

            UpdateBlink(dt);
        }

        UpdateTwitch(dt);
        UpdateGaze(dt, locked ? 2.5f : 1f);
        ApplyLid(lidOpen * (1f - BlinkClosure()));
    }

    protected override void OnRetreatStarted()
    {
        retreatStartLid = lidOpen * (1f - BlinkClosure());
        blinkElapsed = -1f;
        if (stareOnRetreat)
        {
            trackingPointer = true;
        }
    }

    protected override void OnRetreating(float t, float dt)
    {
        lidOpen = stareOnRetreat
            ? Mathf.Lerp(retreatStartLid, 1f, Mathf.Clamp01(t * 4f))
            : Mathf.Lerp(retreatStartLid, 0f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.55f)));
        UpdateGaze(dt);
        ApplyLid(lidOpen);
    }

    private bool HasLeader => leader != null && leader.IsAlive;

    protected override bool SuppressSounds => HasLeader;

    private void CopyLeaderGaze()
    {
        if (!HasLeader)
        {
            return;
        }

        trackingPointer = leader.trackingPointer;
        if (!trackingPointer)
        {
            targetYaw = leader.targetYaw;
            targetPitch = leader.targetPitch;
        }
    }

    protected override Vector3 GetMotionOffset()
    {
        if (twitchTime < 0f)
        {
            return Vector3.zero;
        }

        float decay = Mathf.Exp(-twitchTime * 18f);
        return twitchDirection * (Mathf.Sin(twitchTime * 90f) * decay * twitchStrength * WorldRadius);
    }

    /// <summary>Lids: shut, a hesitant crack with a tremble, then fully open.</summary>
    private float EvaluateWakingLid(float t)
    {
        if (t < lidCrackStart)
        {
            return 0f;
        }

        if (t < lidFullOpenStart)
        {
            float crack = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(lidCrackStart, lidCrackStart + 0.12f, t));
            float tremble = (Mathf.PerlinNoise(noiseSeed, t * 40f) - 0.5f) * 0.12f;
            return Mathf.Clamp01(crack * lidCrackAmount + tremble * crack);
        }

        float open = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(lidFullOpenStart, 0.96f, t));
        return Mathf.Lerp(lidCrackAmount, 1f, open);
    }

    private void PickSaccade()
    {
        saccadeTimer = Random.Range(saccadeHold.x, saccadeHold.y);
        trackingPointer = Random.value < pointerLookChance;
        if (trackingPointer)
        {
            return;
        }

        // Mostly small darts, sometimes a hard side-eye to the limit.
        float reach = Random.value < 0.25f ? 1f : Random.Range(0.2f, 0.7f);
        targetYaw = Random.Range(-1f, 1f) * maxYaw * reach;
        targetPitch = Random.Range(-1f, 1f) * maxPitch * reach;
    }

    private void UpdateGaze(float dt, float speedMultiplier = 1f)
    {
        if (trackingPointer)
        {
            AimAtPointer();
        }

        float t = 1f - Mathf.Exp(-saccadeSpeed * speedMultiplier * dt);
        yaw = Mathf.Lerp(yaw, targetYaw, t);
        pitch = Mathf.Lerp(pitch, targetPitch, t);
        ApplyGaze(tremor);
    }

    private void AimAtPointer()
    {
        if (!TryGetPointerWorld(transform.position, out Vector3 pointerWorld))
        {
            return;
        }

        Quaternion frame = GetGazeFrame();
        float lookDepth = 2.5f + WorldRadius * 2f;
        Vector3 toward = (pointerWorld - transform.position) + frame * Vector3.forward * lookDepth;
        Vector3 local = Quaternion.Inverse(frame) * toward;
        float forward = Mathf.Max(local.z, 0.0001f);
        targetYaw = Mathf.Clamp(Mathf.Atan2(local.x, forward) * Mathf.Rad2Deg, -maxYaw, maxYaw);
        targetPitch = Mathf.Clamp(Mathf.Atan2(local.y, Mathf.Sqrt(local.x * local.x + forward * forward)) * Mathf.Rad2Deg, -maxPitch, maxPitch);
    }

    /// <summary>Rest gaze frame: forward points at the camera and up follows the eye's roll.</summary>
    private Quaternion GetGazeFrame()
    {
        Vector3 facing = ViewCamera != null ? -ViewCamera.transform.forward : -Vector3.forward;
        return Quaternion.LookRotation(facing, transform.up);
    }

    private void ApplyGaze(float tremorDegrees)
    {
        if (globe == null)
        {
            return;
        }

        float time = Time.time * 7f;
        float jitterYaw = (Mathf.PerlinNoise(noiseSeed, time) - 0.5f) * 2f * tremorDegrees;
        float jitterPitch = (Mathf.PerlinNoise(time, noiseSeed) - 0.5f) * 2f * tremorDegrees;
        Quaternion frame = GetGazeFrame();
        Vector3 desired = frame * (Quaternion.Euler(-(pitch + jitterPitch), yaw + jitterYaw, 0f) * Vector3.forward);

        Quaternion restWorld = globe.parent != null ? globe.parent.rotation * restLocalRotation : restLocalRotation;
        Vector3 restLook = restWorld * localLookAxis.normalized;
        globe.rotation = Quaternion.FromToRotation(restLook, desired) * restWorld;
    }

    private void UpdateBlink(float dt)
    {
        if (blinkElapsed >= 0f)
        {
            blinkElapsed += dt;
            if (blinkElapsed >= blinkDuration)
            {
                blinkElapsed = -1f;
                if (pendingDoubleBlink)
                {
                    pendingDoubleBlink = false;
                    blinkTimer = 0.09f;
                }
                else
                {
                    ScheduleBlink();
                }
            }

            return;
        }

        blinkTimer -= dt;
        if (blinkTimer <= 0f)
        {
            blinkElapsed = 0f;
            PlaySound(ApparitionSound.Blink);
            pendingDoubleBlink = !pendingDoubleBlink && Random.value < doubleBlinkChance;
        }
    }

    private float BlinkClosure()
    {
        if (blinkElapsed < 0f)
        {
            return 0f;
        }

        float t = Mathf.Clamp01(blinkElapsed / blinkDuration);
        float closed = t < 0.4f ? t / 0.4f : 1f - (t - 0.4f) / 0.6f;
        return Mathf.SmoothStep(0f, 1f, closed);
    }

    private void ScheduleBlink()
    {
        blinkTimer = Random.Range(Mathf.Min(blinkInterval.x, blinkInterval.y), Mathf.Max(blinkInterval.x, blinkInterval.y));
    }

    private void UpdateTwitch(float dt)
    {
        if (twitchTime >= 0f)
        {
            twitchTime += dt;
            if (twitchTime > 0.35f)
            {
                twitchTime = -1f;
            }
        }

        twitchTimer -= dt;
        if (twitchTimer <= 0f)
        {
            twitchTimer = Random.Range(twitchInterval.x, twitchInterval.y);
            twitchTime = 0f;
            Vector2 dir = Random.insideUnitCircle.normalized;
            Transform view = ViewCamera != null ? ViewCamera.transform : transform;
            twitchDirection = view.right * dir.x + view.up * dir.y;
        }
    }

    private void ApplyLid(float open)
    {
        if (eyelid != null && blendShapeIndex >= 0)
        {
            eyelid.SetBlendShapeWeight(blendShapeIndex, Mathf.Clamp01(open) * LidOpenWeight);
        }
    }

    private void ResolveRig()
    {
        if (eyelid == null)
        {
            eyelid = GetComponentInChildren<SkinnedMeshRenderer>(true);
        }

        if (globe == null)
        {
            MeshFilter filter = GetComponentInChildren<MeshFilter>(true);
            globe = filter != null ? filter.transform : null;
        }

        blendShapeIndex = -1;
        if (eyelid == null || eyelid.sharedMesh == null)
        {
            return;
        }

        string target = string.IsNullOrEmpty(blendShapeName) ? DefaultBlendShapeName : blendShapeName;
        blendShapeIndex = eyelid.sharedMesh.GetBlendShapeIndex(target);
    }

    protected override void OnValidate()
    {
        base.OnValidate();
        blinkDuration = Mathf.Max(0.05f, blinkDuration);
        if (string.IsNullOrEmpty(blendShapeName))
        {
            blendShapeName = DefaultBlendShapeName;
        }

        if (localLookAxis.sqrMagnitude < 0.0001f)
        {
            localLookAxis = Vector3.down;
        }
    }
}
