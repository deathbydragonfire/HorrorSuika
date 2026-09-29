using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Something that surfaces out of the black fog behind the playfield, lingers, and sinks back.
/// Owns the shared lifecycle: it starts deep and fully shadowed, pushes toward the camera while the
/// ShadowEmerge shader peels the darkness off its front, then retreats the same way.
/// Subclasses add the behaviour while it is out (eyes look and blink, lurkers writhe).
/// </summary>
public abstract class BackgroundApparition : MonoBehaviour
{
    private enum Phase
    {
        Idle,
        Emerging,
        Lingering,
        Retreating
    }

    private static readonly List<BackgroundApparition> ActiveList = new List<BackgroundApparition>(16);

    private static readonly int EmergeId = Shader.PropertyToID("_Emerge");
    private static readonly int EyeCenterId = Shader.PropertyToID("_EyeCenter");
    private static readonly int HueId = Shader.PropertyToID("_Hue");

    [Header("Shadow Emergence")]
    [SerializeField, Tooltip("Seconds to surface out of the fog, randomised between x and y.")]
    private Vector2 emergeDuration = new Vector2(2.6f, 4f);

    [SerializeField, Tooltip("Seconds spent fully out before sinking back, randomised between x and y.")]
    private Vector2 lingerDuration = new Vector2(6f, 13f);

    [SerializeField, Tooltip("Seconds to sink back into the fog, randomised between x and y.")]
    private Vector2 retreatDuration = new Vector2(2.2f, 3.4f);

    [SerializeField, Min(0f), Tooltip("World units behind the rest position where it starts, deep in the fog.")]
    private float emergeDepth = 9f;

    [SerializeField, Range(0.1f, 1f), Tooltip("Scale multiplier while deep in the fog. It swells to full size as it surfaces.")]
    private float submergedScale = 0.7f;

    [SerializeField, Min(0f), Tooltip("World units it rises while surfacing, so it drifts up out of the murk instead of sliding straight at the camera.")]
    private float riseDistance = 0.35f;

    [SerializeField, Range(0f, 1f), Tooltip("How strongly this carves a shadow pool into the backdrop.")]
    private float socketStrength = 1f;

    private readonly List<Renderer> renderers = new List<Renderer>(4);
    private MaterialPropertyBlock propertyBlock;
    private Action<BackgroundApparition> finished;
    private Phase phase = Phase.Idle;
    private float phaseTime;
    private float phaseLength;
    private float emergeLength;
    private float lingerLength;
    private float retreatLength;
    private float baseRadius = 0.15f;
    private float fullScale = 1f;
    private float hue;

    /// <summary>Every apparition currently in the scene, in spawn order.</summary>
    public static IReadOnlyList<BackgroundApparition> Active => ActiveList;

    /// <summary>0 while fully swallowed by fog, 1 once fully out.</summary>
    public float Presence { get; private set; }

    /// <summary>Presence weighted by how strongly this one carves the backdrop.</summary>
    public float SocketPresence => Presence * socketStrength;

    /// <summary>World radius at full size, used for spacing and the backdrop shadow pool.</summary>
    public float WorldRadius => baseRadius * fullScale;

    /// <summary>True between <see cref="Begin"/> and the end of the retreat.</summary>
    public bool IsAlive => phase != Phase.Idle;

    /// <summary>True once the apparition is fully out and before it starts to retreat.</summary>
    protected bool IsLingering => phase == Phase.Lingering;

    /// <summary>Backdrop driving heartbeat and surges. Null when none is in the scene.</summary>
    protected HorrorBackground Background { get; private set; }

    /// <summary>Camera the apparition faces.</summary>
    protected Camera ViewCamera { get; private set; }

    /// <summary>Position it settles at once fully out, before any subclass motion.</summary>
    public Vector3 RestPosition { get; private set; }

    /// <summary>Radius of the unscaled prefab. Valid after Awake.</summary>
    public float BaseRadius => baseRadius;

    protected virtual void Awake()
    {
        GetComponentsInChildren(true, renderers);
        propertyBlock = new MaterialPropertyBlock();
        baseRadius = MeasureBaseRadius();
    }

    protected virtual void OnDisable()
    {
        ActiveList.Remove(this);
    }

    /// <summary>
    /// Starts a new surfacing. The apparition faces <paramref name="viewCamera"/>, rolled by
    /// <paramref name="rollDegrees"/>, and settles at <paramref name="restPosition"/>.
    /// </summary>
    public void Begin(HorrorBackground background, Camera viewCamera, Vector3 restPosition, float scale, float rollDegrees, Action<BackgroundApparition> onFinished)
    {
        Background = background;
        ViewCamera = viewCamera;
        RestPosition = restPosition;
        fullScale = Mathf.Max(0.01f, scale);
        finished = onFinished;
        hue = UnityEngine.Random.value;

        Quaternion facing = viewCamera != null ? viewCamera.transform.rotation : Quaternion.identity;
        transform.rotation = facing * Quaternion.Euler(0f, 0f, rollDegrees);

        gameObject.SetActive(true);
        if (!ActiveList.Contains(this))
        {
            ActiveList.Add(this);
        }

        emergeLength = RandomRange(emergeDuration);
        lingerLength = RandomRange(lingerDuration);
        retreatLength = RandomRange(retreatDuration);
        EnterPhase(Phase.Emerging, emergeLength);
        Presence = 0f;
        OnBegin();
        ApplyTransform(0f);
        ApplyShaderState();
        PlaySound(ApparitionSound.Emerge);
    }

    /// <summary>
    /// Adopts another apparition's durations so the two surface, linger, and sink together.
    /// Call right after both have begun.
    /// </summary>
    public void MatchTimeline(BackgroundApparition source)
    {
        if (source == null || !IsAlive)
        {
            return;
        }

        emergeLength = source.emergeLength;
        lingerLength = source.lingerLength;
        retreatLength = source.retreatLength;
        hue = source.hue;
        EnterPhase(Phase.Emerging, emergeLength);
    }

    /// <summary>Cuts the linger short and starts sinking back into the fog.</summary>
    public void Dismiss()
    {
        if (phase == Phase.Emerging || phase == Phase.Lingering)
        {
            EnterPhase(Phase.Retreating, retreatLength);
            OnRetreatStarted();
            PlaySound(ApparitionSound.Retreat);
        }
    }

    protected virtual void Update()
    {
        if (phase == Phase.Idle)
        {
            return;
        }

        float dt = Time.deltaTime;
        phaseTime += dt;
        float t = phaseLength > 0f ? Mathf.Clamp01(phaseTime / phaseLength) : 1f;

        switch (phase)
        {
            case Phase.Emerging:
                Presence = EaseOutCubic(t);
                OnEmerging(t, dt);
                if (t >= 1f)
                {
                    EnterPhase(Phase.Lingering, lingerLength);
                }
                break;

            case Phase.Lingering:
                Presence = 1f;
                OnLingering(dt);
                if (t >= 1f)
                {
                    EnterPhase(Phase.Retreating, retreatLength);
                    OnRetreatStarted();
                    PlaySound(ApparitionSound.Retreat);
                }
                break;

            case Phase.Retreating:
                Presence = 1f - EaseInCubic(t);
                OnRetreating(t, dt);
                if (t >= 1f)
                {
                    Finish();
                    return;
                }
                break;
        }

        ApplyTransform(Presence);
        ApplyShaderState();
    }

    /// <summary>Called once when a new surfacing starts, before the first frame is drawn.</summary>
    protected virtual void OnBegin() { }

    /// <summary>Called every frame while surfacing. <paramref name="t"/> runs 0 to 1.</summary>
    protected virtual void OnEmerging(float t, float dt) { }

    /// <summary>Called every frame while fully out.</summary>
    protected virtual void OnLingering(float dt) { }

    /// <summary>Called once when the retreat starts.</summary>
    protected virtual void OnRetreatStarted() { }

    /// <summary>Called every frame while sinking. <paramref name="t"/> runs 0 to 1.</summary>
    protected virtual void OnRetreating(float t, float dt) { }

    /// <summary>Extra world offset layered on the rest position, such as a twitch or sway.</summary>
    protected virtual Vector3 GetMotionOffset() => Vector3.zero;

    private void ApplyTransform(float presence)
    {
        Vector3 back = ViewCamera != null ? ViewCamera.transform.forward : Vector3.forward;
        Vector3 up = ViewCamera != null ? ViewCamera.transform.up : Vector3.up;
        float submerged = 1f - presence;
        transform.position = RestPosition
            + back * (emergeDepth * submerged)
            - up * (riseDistance * submerged)
            + GetMotionOffset();
        transform.localScale = Vector3.one * (fullScale * Mathf.Lerp(submergedScale, 1f, presence));
    }

    private void ApplyShaderState()
    {
        Vector3 centre = transform.position;
        var centreAndRadius = new Vector4(centre.x, centre.y, centre.z, baseRadius * transform.localScale.x);
        for (int i = 0; i < renderers.Count; i++)
        {
            Renderer target = renderers[i];
            if (target == null)
            {
                continue;
            }

            target.GetPropertyBlock(propertyBlock);
            propertyBlock.SetFloat(EmergeId, Presence);
            propertyBlock.SetVector(EyeCenterId, centreAndRadius);
            propertyBlock.SetFloat(HueId, hue);
            target.SetPropertyBlock(propertyBlock);
        }
    }

    private void EnterPhase(Phase next, float length)
    {
        phase = next;
        phaseTime = 0f;
        phaseLength = length;
    }

    private static float RandomRange(Vector2 range)
    {
        float min = Mathf.Max(0.05f, Mathf.Min(range.x, range.y));
        float max = Mathf.Max(min, Mathf.Max(range.x, range.y));
        return UnityEngine.Random.Range(min, max);
    }

    private void Finish()
    {
        phase = Phase.Idle;
        Presence = 0f;
        ActiveList.Remove(this);
        gameObject.SetActive(false);
        Action<BackgroundApparition> callback = finished;
        finished = null;
        callback?.Invoke(this);
    }

    private float MeasureBaseRadius()
    {
        if (renderers.Count == 0)
        {
            return 0.15f;
        }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Count; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        float scale = Mathf.Max(0.0001f, transform.lossyScale.x);
        return Mathf.Max(bounds.extents.x, bounds.extents.y) / scale;
    }

    /// <summary>Plays a very quiet flesh sound, quieter the deeper this sits in the fog.</summary>
    protected void PlaySound(ApparitionSound sound)
    {
        if (SuppressSounds)
        {
            return;
        }

        float depth = Background != null ? Background.GetDepth01(RestPosition) : 0.5f;
        AudioManager.PlayApparition(sound, RestPosition, depth);
    }

    /// <summary>True when another apparition already voices this one, such as the second eye of a pair.</summary>
    protected virtual bool SuppressSounds => false;

    /// <summary>Screen pointer projected onto the plane through <paramref name="planePoint"/> facing the camera.</summary>
    protected bool TryGetPointerWorld(Vector3 planePoint, out Vector3 pointerWorld)
    {
        pointerWorld = default;
        if (ViewCamera == null || !HorrorPointer.TryGetScreenPosition(out Vector2 screen))
        {
            return false;
        }

        Ray ray = ViewCamera.ScreenPointToRay(screen);
        var plane = new Plane(-ViewCamera.transform.forward, planePoint);
        if (!plane.Raycast(ray, out float enter))
        {
            return false;
        }

        pointerWorld = ray.GetPoint(enter);
        return true;
    }

    protected static float EaseOutCubic(float t)
    {
        float inv = 1f - t;
        return 1f - inv * inv * inv;
    }

    protected static float EaseInCubic(float t)
    {
        return t * t * t;
    }

    protected virtual void OnValidate()
    {
        emergeDepth = Mathf.Max(0f, emergeDepth);
        riseDistance = Mathf.Max(0f, riseDistance);
    }
}
