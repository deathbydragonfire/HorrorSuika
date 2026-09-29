using UnityEngine;

/// <summary>
/// Organic shape that writhes in the fog behind the eyes: vessels, entrails, a mouth. It sways and
/// twists on slow noise, swells with the heartbeat, and can drive one blend shape, such as a mouth
/// that slowly gapes open and shut.
/// </summary>
[DisallowMultipleComponent]
public class BackgroundLurker : BackgroundApparition
{
    [Header("Writhing")]
    [SerializeField, Tooltip("Degrees of slow sway around each axis.")]
    private Vector3 swayDegrees = new Vector3(14f, 14f, 22f);

    [SerializeField, Min(0f), Tooltip("Speed of the sway noise.")]
    private float swaySpeed = 0.18f;

    [SerializeField, Tooltip("Degrees per second of constant twist about the view axis, randomised between x and y. Direction is random.")]
    private Vector2 spinSpeed = new Vector2(2f, 7f);

    [SerializeField, Range(0f, 0.3f), Tooltip("Fraction the shape swells on each heartbeat.")]
    private float beatSwell = 0.05f;

    [SerializeField, Min(0f), Tooltip("World units of slow drift over its lifetime, as a fraction of its radius.")]
    private float drift = 0.4f;

    [Header("Blend Shape (optional)")]
    [SerializeField, Tooltip("Skinned mesh to animate. Leave empty to use the first one found.")]
    private SkinnedMeshRenderer shapeRenderer;

    [SerializeField, Tooltip("Blend shape to animate. Leave empty to skip.")]
    private string blendShapeName = "";

    [SerializeField, Tooltip("Weight at rest (for the CubeOrigins mouth, 100 on 'mouth close' is shut).")]
    private float restWeight = 100f;

    [SerializeField, Tooltip("Weight at the widest gape.")]
    private float gapeWeight = 0f;

    [SerializeField, Tooltip("Seconds between gapes, randomised between x and y.")]
    private Vector2 gapeInterval = new Vector2(2.5f, 6f);

    [SerializeField, Min(0.1f), Tooltip("Seconds for one slow open-and-close.")]
    private float gapeDuration = 2.4f;

    private Quaternion baseRotation;
    private float noiseSeed;
    private float spin;
    private float spinAngle;
    private Vector3 driftDirection;
    private float age;
    private int blendShapeIndex = -1;
    private float gapeTimer;
    private float gapeElapsed = -1f;

    protected override void Awake()
    {
        base.Awake();
        if (shapeRenderer == null)
        {
            shapeRenderer = GetComponentInChildren<SkinnedMeshRenderer>(true);
        }

        if (shapeRenderer != null && shapeRenderer.sharedMesh != null && !string.IsNullOrEmpty(blendShapeName))
        {
            blendShapeIndex = shapeRenderer.sharedMesh.GetBlendShapeIndex(blendShapeName);
        }
    }

    protected override void OnBegin()
    {
        baseRotation = transform.rotation;
        noiseSeed = Random.value * 100f;
        spin = Random.Range(spinSpeed.x, spinSpeed.y) * (Random.value < 0.5f ? -1f : 1f);
        spinAngle = 0f;
        age = 0f;
        Vector2 dir = Random.insideUnitCircle.normalized;
        Transform view = ViewCamera != null ? ViewCamera.transform : transform;
        driftDirection = view.right * dir.x + view.up * dir.y;
        gapeElapsed = -1f;
        gapeTimer = Random.Range(gapeInterval.x, gapeInterval.y) * 0.5f;
        SetWeight(restWeight);
    }

    protected override void Update()
    {
        base.Update();
        if (!IsAlive)
        {
            return;
        }

        float dt = Time.deltaTime;
        age += dt;
        spinAngle += spin * dt;

        float t = age * swaySpeed;
        var sway = new Vector3(
            (Mathf.PerlinNoise(noiseSeed, t) - 0.5f) * 2f * swayDegrees.x,
            (Mathf.PerlinNoise(t, noiseSeed + 3.1f) - 0.5f) * 2f * swayDegrees.y,
            (Mathf.PerlinNoise(noiseSeed + 7.7f, t) - 0.5f) * 2f * swayDegrees.z + spinAngle);
        transform.rotation = baseRotation * Quaternion.Euler(sway);

        float beat = Background != null ? Background.Beat : 0f;
        transform.localScale *= 1f + beat * beatSwell;

        UpdateGape(dt);
    }

    protected override Vector3 GetMotionOffset()
    {
        return driftDirection * (age * 0.05f * drift * WorldRadius);
    }

    private void UpdateGape(float dt)
    {
        if (blendShapeIndex < 0)
        {
            return;
        }

        float surge = Background != null ? Background.Surge : 0f;
        float presenceGate = Mathf.SmoothStep(0f, 1f, Presence);

        if (gapeElapsed >= 0f)
        {
            gapeElapsed += dt;
            float g = Mathf.Clamp01(gapeElapsed / gapeDuration);
            float open = Mathf.Sin(g * Mathf.PI);
            open = Mathf.Max(open * open * (3f - 2f * open), surge);
            SetWeight(Mathf.Lerp(restWeight, gapeWeight, open * presenceGate));
            if (g >= 1f)
            {
                gapeElapsed = -1f;
                gapeTimer = Random.Range(gapeInterval.x, gapeInterval.y);
            }

            return;
        }

        SetWeight(Mathf.Lerp(restWeight, gapeWeight, surge * presenceGate));
        gapeTimer -= dt;
        if (gapeTimer <= 0f && IsLingering)
        {
            gapeElapsed = 0f;
            PlaySound(ApparitionSound.Open);
        }
    }

    private void SetWeight(float weight)
    {
        if (shapeRenderer != null && blendShapeIndex >= 0)
        {
            shapeRenderer.SetBlendShapeWeight(blendShapeIndex, weight);
        }
    }
}
