using System;
using TMPro;
using UnityEngine;

/// <summary>
/// Scene owner of the psychedelic horror backdrop. Keeps the backdrop and black fog quads filling
/// the camera at any aspect, runs the heartbeat, blackout flickers, and surges, and uploads every
/// live <see cref="BackgroundApparition"/> to the shared shader globals.
/// </summary>
[ExecuteAlways]
[DefaultExecutionOrder(200)]
public class HorrorBackground : MonoBehaviour
{
    /// <summary>Shader array length. Must match HORROR_MAX_EYES in HorrorBackgroundCommon.hlsl.</summary>
    public const int MaxEyes = 8;

    private const float TimeWrap = 4096f;
    private const float LubPhase = 0.06f;
    private const float DubPhase = 0.3f;
    private const float FlickerStep = 0.045f;

    private static readonly int EyesId = Shader.PropertyToID("_HorrorEyes");
    private static readonly int EyeCountId = Shader.PropertyToID("_HorrorEyeCount");
    private static readonly int BeatId = Shader.PropertyToID("_HorrorBeat");
    private static readonly int SurgeId = Shader.PropertyToID("_HorrorSurge");
    private static readonly int FlickerId = Shader.PropertyToID("_HorrorFlicker");
    private static readonly int GlitchId = Shader.PropertyToID("_HorrorGlitch");
    private static readonly int TimeId = Shader.PropertyToID("_HorrorTime");
    private static readonly int ViewId = Shader.PropertyToID("_HorrorView");
    private static readonly int PlayfieldRectId = Shader.PropertyToID("_HorrorPlayfieldRect");
    private static readonly int DepthFogId = Shader.PropertyToID("_HorrorDepthFog");

    private static readonly string[] DefaultWords =
    {
        "FEED IT", "IT SEES YOU", "DON'T BLINK", "HUNGRY", "LET IT IN", "MORE", "WAKE UP", "BEHIND YOU"
    };

    [Header("Layers")]
    [SerializeField, Tooltip("Camera the backdrop fills. Leave empty to use Camera.main.")]
    private Camera targetCamera;

    [SerializeField, Tooltip("Quad with the PsychedelicBackdrop material.")]
    private Transform backdrop;

    [SerializeField, Tooltip("Quad with the BlackFog material, between the apparitions and the playfield.")]
    private Transform fogLayer;

    [SerializeField, Min(1f), Tooltip("Distance from the camera to the backdrop. Must be beyond every apparition.")]
    private float backdropDistance = 48f;

    [SerializeField, Min(0.5f), Tooltip("Distance from the camera to the fog layer. Must be between the playfield and the nearest apparition.")]
    private float fogDistance = 20f;

    [SerializeField, Min(1f), Tooltip("Oversizes both quads so camera shake or refits never expose an edge.")]
    private float overscan = 1.2f;

    [SerializeField, Range(0f, 1f), Tooltip("How much of an apparition's light the black fog eats at the backdrop distance. Sells depth under an orthographic camera.")]
    private float depthDarkening = 0.8f;

    [SerializeField, Range(0f, 1f), Tooltip("Darkening applied even to the nearest apparitions, so nothing in the background is ever fully lit.")]
    private float nearDarkening = 0.35f;

    [Header("Playfield")]
    [SerializeField, Tooltip("Optional. The backdrop calms down inside this area so items stay readable.")]
    private PlayfieldBounds playfield;

    [SerializeField, Min(0f), Tooltip("World units the calm area extends past the container walls.")]
    private float playfieldMargin = 0.25f;

    [Header("Heartbeat")]
    [SerializeField, Tooltip("Resting beats per minute, wandering slowly between x and y.")]
    private Vector2 bpmRange = new Vector2(48f, 68f);

    [SerializeField, Min(0f), Tooltip("Extra beats per minute at the peak of a surge.")]
    private float surgeBpmBoost = 45f;

    [SerializeField, Tooltip("Plays the heartbeat sound through AudioManager in time with the backdrop pulse.")]
    private bool heartbeatSound = true;

    [SerializeField, Range(0f, 0.1f), Tooltip("Seconds the sound fires ahead of the visual peak, covering the clip's attack so the thump lands on the pulse.")]
    private float heartbeatSoundLead = 0.015f;

    [Header("Blackouts")]
    [SerializeField, Tooltip("Seconds between blackout flickers, randomised between x and y.")]
    private Vector2 flickerInterval = new Vector2(9f, 22f);

    [SerializeField, Tooltip("Seconds a flicker lasts, randomised between x and y.")]
    private Vector2 flickerDuration = new Vector2(0.2f, 0.65f);

    [Header("Surges")]
    [SerializeField, Tooltip("Seconds between surges, randomised between x and y. Every eye locks onto the player during one.")]
    private Vector2 surgeInterval = new Vector2(24f, 42f);

    [SerializeField, Min(0.05f)] private float surgeRise = 0.6f;
    [SerializeField, Min(0f)] private float surgeHold = 2.4f;
    [SerializeField, Min(0.05f)] private float surgeFall = 2f;

    [Header("Subliminal")]
    [SerializeField, Tooltip("Optional world-space text flashed for a frame or two during blackouts.")]
    private TMP_Text subliminalText;

    [SerializeField, Tooltip("Words picked at random for the subliminal flash.")]
    private string[] subliminalWords = DefaultWords;

    [SerializeField, Range(0f, 1f), Tooltip("Chance a blackout carries a subliminal flash.")]
    private float subliminalChance = 0.55f;

    private readonly Vector4[] eyeData = new Vector4[MaxEyes];
    private float beatPhase;
    private float bpmNoiseSeed;
    private float flickerTimer;
    private float flickerRemaining;
    private float flickerStepTimer;
    private float surgeTimer;
    private float surgeElapsed = -1f;
    private float subliminalRemaining;
    private bool subliminalPending;

    /// <summary>Heartbeat envelope, 0 between beats and near 1 on the "lub".</summary>
    public float Beat { get; private set; }

    /// <summary>Surge envelope 0..1.</summary>
    public float Surge { get; private set; }

    /// <summary>Blackout amount 0..1.</summary>
    public float Flicker { get; private set; }

    /// <summary>Raised when a surge begins, so spawners can burst more apparitions.</summary>
    public event Action SurgeStarted;

    /// <summary>Camera the backdrop is fitted to.</summary>
    public Camera ViewCamera => targetCamera != null ? targetCamera : Camera.main;

    /// <summary>Area kept calm behind the container, in world XY. Zero size when there is no playfield.</summary>
    public Rect PlayfieldRect { get; private set; }

    /// <summary>Half width and half height of the view at <paramref name="distance"/> from the camera.</summary>
    public Vector2 GetViewHalfExtents(float distance)
    {
        Camera cam = ViewCamera;
        if (cam == null)
        {
            return new Vector2(5f, 5f);
        }

        float halfHeight = cam.orthographic
            ? cam.orthographicSize
            : distance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        return new Vector2(halfHeight * cam.aspect, halfHeight);
    }

    /// <summary>0 at the fog layer, 1 at the backdrop: how deep in the dark a point sits.</summary>
    public float GetDepth01(Vector3 worldPosition)
    {
        Camera cam = ViewCamera;
        if (cam == null)
        {
            return 0.5f;
        }

        float distance = Vector3.Dot(worldPosition - cam.transform.position, cam.transform.forward);
        return Mathf.InverseLerp(fogDistance, backdropDistance, distance);
    }

    /// <summary>Starts a surge now, regardless of the timer.</summary>
    public void TriggerSurge()
    {
        surgeElapsed = 0f;
        surgeTimer = RandomRange(surgeInterval);
        SurgeStarted?.Invoke();
    }

    /// <summary>Starts a blackout flicker now, regardless of the timer.</summary>
    public void TriggerFlicker()
    {
        flickerRemaining = RandomRange(flickerDuration);
        flickerTimer = RandomRange(flickerInterval);
        flickerStepTimer = 0f;
        subliminalPending = subliminalText != null && subliminalWords != null && subliminalWords.Length > 0
            && UnityEngine.Random.value < subliminalChance;
    }

    private void OnEnable()
    {
        bpmNoiseSeed = UnityEngine.Random.value * 100f;
        flickerTimer = RandomRange(flickerInterval) * 0.6f;
        surgeTimer = RandomRange(surgeInterval);
        if (playfield == null)
        {
            playfield = FindFirstObjectByType<PlayfieldBounds>();
        }

        HideSubliminal();
    }

    private void OnDisable()
    {
        Shader.SetGlobalFloat(EyeCountId, 0f);
        Shader.SetGlobalFloat(FlickerId, 0f);
        Shader.SetGlobalFloat(GlitchId, 0f);
        Shader.SetGlobalFloat(SurgeId, 0f);
    }

    private void LateUpdate()
    {
        float dt = Application.isPlaying ? Time.deltaTime : 0.016f;
        float time = Application.isPlaying ? Time.timeSinceLevelLoad : (float)(Time.realtimeSinceStartupAsDouble % TimeWrap);

        if (Application.isPlaying)
        {
            UpdateSurge(dt);
            UpdateFlicker(dt);
            UpdateSubliminal(dt);
        }

        UpdateHeartbeat(dt, time);
        FitLayers();
        UploadGlobals(time % TimeWrap);
    }

    private void UpdateHeartbeat(float dt, float time)
    {
        float wander = Mathf.PerlinNoise(bpmNoiseSeed, time * 0.03f);
        float bpm = Mathf.Lerp(bpmRange.x, bpmRange.y, wander) + Surge * surgeBpmBoost;
        float previousPhase = beatPhase;
        beatPhase = Mathf.Repeat(beatPhase + dt * bpm / 60f, 1f);

        if (heartbeatSound && Application.isPlaying && dt > 0f)
        {
            float lead = heartbeatSoundLead * bpm / 60f;
            if (CrossedPhase(previousPhase, beatPhase, LubPhase - lead))
            {
                AudioManager.PlayHeartbeat(true, Surge);
            }

            if (CrossedPhase(previousPhase, beatPhase, DubPhase - lead))
            {
                AudioManager.PlayHeartbeat(false, Surge);
            }
        }

        // Lub-dub: a sharp first beat, a softer second one, then silence.
        float lub = Gaussian(beatPhase, LubPhase, 0.045f);
        float dub = Gaussian(beatPhase, DubPhase, 0.06f) * 0.55f;
        Beat = Mathf.Clamp01(lub + dub);
    }

    private void UpdateSurge(float dt)
    {
        if (surgeElapsed < 0f)
        {
            surgeTimer -= dt;
            if (surgeTimer <= 0f)
            {
                TriggerSurge();
            }

            Surge = 0f;
            return;
        }

        surgeElapsed += dt;
        float e = surgeElapsed;
        if (e < surgeRise)
        {
            Surge = Mathf.SmoothStep(0f, 1f, e / surgeRise);
        }
        else if (e < surgeRise + surgeHold)
        {
            Surge = 1f;
        }
        else if (e < surgeRise + surgeHold + surgeFall)
        {
            Surge = 1f - Mathf.SmoothStep(0f, 1f, (e - surgeRise - surgeHold) / surgeFall);
        }
        else
        {
            Surge = 0f;
            surgeElapsed = -1f;
        }
    }

    private void UpdateFlicker(float dt)
    {
        if (flickerRemaining <= 0f)
        {
            Flicker = 0f;
            flickerTimer -= dt;
            if (flickerTimer <= 0f)
            {
                TriggerFlicker();
            }

            return;
        }

        flickerRemaining -= dt;
        flickerStepTimer -= dt;
        if (flickerStepTimer <= 0f)
        {
            flickerStepTimer = FlickerStep;
            float roll = UnityEngine.Random.value;
            Flicker = roll < 0.35f ? 1f : roll < 0.6f ? 0.85f : roll < 0.8f ? 0.3f : 0f;

            if (subliminalPending && Flicker >= 0.85f)
            {
                subliminalPending = false;
                ShowSubliminal();
            }
        }

        if (flickerRemaining <= 0f)
        {
            Flicker = 0f;
        }
    }

    private void ShowSubliminal()
    {
        Camera cam = ViewCamera;
        if (subliminalText == null || cam == null)
        {
            return;
        }

        float distance = Mathf.Max(0.5f, fogDistance - 1f);
        Vector2 half = GetViewHalfExtents(distance);
        Transform view = cam.transform;
        Vector3 offset = view.right * UnityEngine.Random.Range(-half.x, half.x) * 0.4f
            + view.up * UnityEngine.Random.Range(-half.y, half.y) * 0.5f;
        Transform text = subliminalText.transform;
        text.position = view.position + view.forward * distance + offset;
        text.rotation = view.rotation * Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(-9f, 9f));
        subliminalText.text = subliminalWords[UnityEngine.Random.Range(0, subliminalWords.Length)];
        subliminalText.gameObject.SetActive(true);
        subliminalRemaining = UnityEngine.Random.Range(0.05f, 0.09f);
    }

    private void UpdateSubliminal(float dt)
    {
        if (subliminalRemaining <= 0f)
        {
            return;
        }

        subliminalRemaining -= dt;
        if (subliminalRemaining <= 0f)
        {
            HideSubliminal();
        }
    }

    private void HideSubliminal()
    {
        subliminalRemaining = 0f;
        if (subliminalText != null)
        {
            subliminalText.gameObject.SetActive(false);
        }
    }

    private void FitLayers()
    {
        Camera cam = ViewCamera;
        if (cam == null)
        {
            return;
        }

        FitQuad(cam, backdrop, backdropDistance);
        FitQuad(cam, fogLayer, fogDistance);

        if (playfield != null)
        {
            float halfWidth = playfield.InnerHalfWidth + playfieldMargin;
            float centreX = playfield.transform.position.x;
            float bottom = playfield.FloorY - playfieldMargin;
            float top = playfield.DeathLineY + playfieldMargin;
            PlayfieldRect = Rect.MinMaxRect(centreX - halfWidth, bottom, centreX + halfWidth, top);
        }
        else
        {
            PlayfieldRect = Rect.zero;
        }
    }

    private void FitQuad(Camera cam, Transform quad, float distance)
    {
        if (quad == null)
        {
            return;
        }

        Transform view = cam.transform;
        Vector2 half = GetViewHalfExtents(distance);
        quad.SetPositionAndRotation(view.position + view.forward * distance, view.rotation);
        quad.localScale = new Vector3(half.x * 2f * overscan, half.y * 2f * overscan, 1f);
    }

    private void UploadGlobals(float time)
    {
        int count = 0;
        var active = BackgroundApparition.Active;
        for (int i = 0; i < active.Count && count < MaxEyes; i++)
        {
            BackgroundApparition apparition = active[i];
            if (apparition == null || !apparition.IsAlive)
            {
                continue;
            }

            Vector3 position = apparition.transform.position;
            eyeData[count++] = new Vector4(position.x, position.y, apparition.WorldRadius, apparition.SocketPresence);
        }

        for (int i = count; i < MaxEyes; i++)
        {
            eyeData[i] = Vector4.zero;
        }

        Shader.SetGlobalVectorArray(EyesId, eyeData);
        Shader.SetGlobalFloat(EyeCountId, count);
        Shader.SetGlobalFloat(BeatId, Beat);
        Shader.SetGlobalFloat(SurgeId, Surge);
        Shader.SetGlobalFloat(FlickerId, Flicker);
        Shader.SetGlobalFloat(GlitchId, Flicker > 0.2f ? Mathf.Lerp(0.4f, 1f, Flicker) : Surge * 0.15f);
        Shader.SetGlobalFloat(TimeId, time);
        Shader.SetGlobalVector(DepthFogId, new Vector4(fogDistance, backdropDistance, depthDarkening, nearDarkening));

        Camera cam = ViewCamera;
        if (cam != null)
        {
            Vector2 half = GetViewHalfExtents(backdropDistance);
            Vector3 centre = cam.transform.position;
            Shader.SetGlobalVector(ViewId, new Vector4(centre.x, centre.y, half.y, half.x));
        }

        Rect rect = PlayfieldRect;
        Shader.SetGlobalVector(PlayfieldRectId, new Vector4(rect.xMin, rect.yMin, rect.xMax, rect.yMax));
    }

    /// <summary>True when the phase passed <paramref name="mark"/> this frame, including across the wrap.</summary>
    private static bool CrossedPhase(float previous, float current, float mark)
    {
        mark = Mathf.Repeat(mark, 1f);
        return current >= previous
            ? previous < mark && current >= mark
            : previous < mark || current >= mark;
    }

    private static float Gaussian(float x, float centre, float width)
    {
        float d = (x - centre) / width;
        return Mathf.Exp(-d * d);
    }

    private static float RandomRange(Vector2 range)
    {
        return UnityEngine.Random.Range(Mathf.Min(range.x, range.y), Mathf.Max(range.x, range.y));
    }

    private void OnValidate()
    {
        fogDistance = Mathf.Min(fogDistance, backdropDistance - 1f);
        if (subliminalWords == null || subliminalWords.Length == 0)
        {
            subliminalWords = DefaultWords;
        }
    }
}
