using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Horror styling for a button. Bone-white text on a dark panel with a crimson edge; the label
/// breathes with the backdrop heartbeat and twitches now and then. Hovering or selecting it
/// brightens it, swells it slightly, makes the label tremble, and bleeds down the button: the
/// drips land on the button below (found automatically), pool on its edge, and spill over.
/// Disabled buttons dim and never bleed. Drives its own visuals, so set the Button transition to None.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Button))]
public class HorrorMenuButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
{
    private static readonly int AspectId = Shader.PropertyToID("_Aspect");
    private static readonly int SurfaceYId = Shader.PropertyToID("_SurfaceY");
    private static readonly int FaceId = Shader.PropertyToID("_Face");
    private static readonly int CornerRadiusId = Shader.PropertyToID("_CornerRadius");

    [Header("Parts")]
    [SerializeField] private TMP_Text label;
    [SerializeField] private Graphic panel;
    [SerializeField, Tooltip("Optional outline effect around the panel.")]
    private Outline edge;
    [SerializeField, Tooltip("Optional edge drawn as its own Graphic, such as a dashed ticket outline.")]
    private Graphic edgeGraphic;

    [Header("Idle")]
    [SerializeField] private Color idleText = new Color(0.86f, 0.8f, 0.72f, 1f);
    [SerializeField] private Color idlePanel = new Color(0.07f, 0.02f, 0.025f, 0.88f);
    [SerializeField] private Color idleEdge = new Color(0.42f, 0.04f, 0.06f, 0.85f);

    [Header("Hover")]
    [SerializeField] private Color hoverText = new Color(1f, 0.95f, 0.88f, 1f);
    [SerializeField] private Color hoverPanel = new Color(0.26f, 0.02f, 0.04f, 0.94f);
    [SerializeField] private Color hoverEdge = new Color(0.9f, 0.08f, 0.1f, 1f);
    [SerializeField, Min(1f), Tooltip("Keep small: the blood's collision line is measured at rest size.")]
    private float hoverScale = 1.02f;
    [SerializeField, Min(0f), Tooltip("Pixels the label trembles while hovered.")]
    private float hoverTremble = 1.6f;

    [Header("Disabled")]
    [SerializeField] private Color disabledText = new Color(0.4f, 0.36f, 0.33f, 0.8f);
    [SerializeField] private Color disabledPanel = new Color(0.04f, 0.015f, 0.018f, 0.8f);

    [Header("Blood")]
    [SerializeField, Tooltip("Optional Graphic using the UIBloodDrip material. Blood runs down it while hovered.")]
    private Graphic bloodDrip;

    [SerializeField, Tooltip("Let drips land on the button directly below (found automatically), pool on its edge, and spill over. Without one below, drips fall freely.")]
    private bool bloodSurfaceBelow = true;

    [SerializeField, Min(0.1f), Tooltip("Seconds for the blood to run, pool, and spill all the way.")]
    private float bloodRunSeconds = 8f;

    [SerializeField, Min(0.1f), Tooltip("Seconds for the blood to fade after the pointer leaves.")]
    private float bloodFadeSeconds = 0.45f;

    [Header("Unease")]
    [SerializeField, Range(0f, 1f), Tooltip("How much the label brightens on each heartbeat.")]
    private float beatPulse = 0.15f;

    [SerializeField, Tooltip("Seconds between idle twitches, randomised between x and y.")]
    private Vector2 twitchInterval = new Vector2(4f, 10f);

    [SerializeField, Min(0f), Tooltip("Pixels an idle twitch jolts the label.")]
    private float twitchPixels = 3f;

    [SerializeField, Min(0.1f), Tooltip("How quickly the hover look fades in and out.")]
    private float fadeSpeed = 10f;

    private const float TrembleStep = 0.045f;
    private const float TwitchLength = 0.12f;
    private const float MinimumSurfaceOverlap = 0.4f;

    private Button button;
    private HorrorBackground background;
    private RectTransform labelRect;
    private RectTransform bloodRect;
    private Material bloodMaterial;
    private Vector2 labelRestPosition;
    private Vector3 restScale;
    private bool pointerOver;
    private bool selected;
    private float hover;
    private float trembleTimer;
    private Vector2 trembleOffset;
    private float twitchTimer;
    private float twitchTime = -1f;
    private Vector2 twitchDirection;
    private float bloodSeed;
    private float bloodRun;
    private float bloodVisible;
    private bool bloodLands;

    private void Awake()
    {
        button = GetComponent<Button>();
        if (label == null)
        {
            label = GetComponentInChildren<TMP_Text>(true);
        }

        if (panel == null)
        {
            panel = GetComponent<Graphic>();
        }

        if (edge == null)
        {
            edge = GetComponent<Outline>();
        }

        if (bloodDrip != null)
        {
            bloodDrip.raycastTarget = false;
            bloodRect = bloodDrip.rectTransform;
            // Own copy so the drip shape can match this button's size.
            if (bloodDrip.material != null)
            {
                bloodMaterial = new Material(bloodDrip.material);
                bloodDrip.material = bloodMaterial;
            }
        }

        bloodSeed = Random.value;
        labelRect = label != null ? label.rectTransform : null;
        labelRestPosition = labelRect != null ? labelRect.anchoredPosition : Vector2.zero;
        restScale = transform.localScale;
    }

    private void OnDestroy()
    {
        if (bloodMaterial != null)
        {
            Destroy(bloodMaterial);
        }
    }

    private void OnEnable()
    {
        background = FindFirstObjectByType<HorrorBackground>();
        twitchTimer = Random.Range(twitchInterval.x, twitchInterval.y);
        pointerOver = false;
        selected = false;
        hover = 0f;
        bloodRun = 0f;
        bloodVisible = 0f;
        Apply();
    }

    private void OnDisable()
    {
        transform.localScale = restScale;
        if (labelRect != null)
        {
            labelRect.anchoredPosition = labelRestPosition;
        }

        if (bloodDrip != null)
        {
            bloodVisible = 0f;
            bloodDrip.color = Color.clear;
        }
    }

    public void OnPointerEnter(PointerEventData eventData) => pointerOver = true;

    public void OnPointerExit(PointerEventData eventData) => pointerOver = false;

    public void OnSelect(BaseEventData eventData) => selected = true;

    public void OnDeselect(BaseEventData eventData) => selected = false;

    private bool Interactable => button == null || button.IsInteractable();

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;
        bool active = Interactable && (pointerOver || selected);
        hover = Mathf.MoveTowards(hover, active ? 1f : 0f, dt * fadeSpeed);

        UpdateTremble(dt);
        UpdateTwitch(dt);
        UpdateBlood(dt, active);
        Apply();
    }

    private void Apply()
    {
        bool interactable = Interactable;
        float beat = background != null ? background.Beat : 0f;
        float pulse = interactable ? 1f + beat * beatPulse * (1f - hover * 0.5f) : 1f;

        if (label != null)
        {
            Color text = interactable ? Color.Lerp(idleText, hoverText, hover) : disabledText;
            text.r = Mathf.Clamp01(text.r * pulse);
            text.g = Mathf.Clamp01(text.g * pulse);
            text.b = Mathf.Clamp01(text.b * pulse);
            label.color = text;
        }

        if (panel != null)
        {
            panel.color = interactable ? Color.Lerp(idlePanel, hoverPanel, hover) : disabledPanel;
        }

        Color edgeColor = interactable ? Color.Lerp(idleEdge, hoverEdge, hover) : idleEdge * 0.5f;
        if (edge != null)
        {
            edge.effectColor = edgeColor;
        }

        if (edgeGraphic != null)
        {
            edgeGraphic.color = edgeColor;
        }

        transform.localScale = restScale * Mathf.Lerp(1f, hoverScale, EaseOut(hover));

        if (labelRect != null)
        {
            Vector2 twitch = interactable ? TwitchOffset() : Vector2.zero;
            labelRect.anchoredPosition = labelRestPosition + trembleOffset * hover + twitch;
        }
    }

    /// <summary>
    /// Blood runs while hovered and holds once fully out. Leaving fades it rather than pulling the
    /// drips back up, and a fresh hover after it has faded bleeds from scratch with a new pattern.
    /// </summary>
    private void UpdateBlood(float dt, bool hovered)
    {
        if (bloodDrip == null)
        {
            return;
        }

        if (hovered)
        {
            if (bloodVisible <= 0.01f)
            {
                bloodRun = 0f;
                bloodSeed = Random.value;
                FitBloodToLayout();
            }

            bloodVisible = Mathf.MoveTowards(bloodVisible, 1f, dt * 8f);
            bloodRun = Mathf.MoveTowards(bloodRun, 1f, dt / bloodRunSeconds);
        }
        else
        {
            bloodVisible = Mathf.MoveTowards(bloodVisible, 0f, dt / bloodFadeSeconds);
        }

        // The shader reads its state from the vertex colour: seed, visibility, surface below, run.
        bloodDrip.color = new Color(bloodSeed, bloodVisible, bloodLands ? 1f : 0f, bloodRun);
    }

    /// <summary>
    /// Matches the drip shape to this button's size and finds the top edge of the button directly
    /// below, if any, for the drips to land on. Runs at the start of each bleed so it follows layout.
    /// </summary>
    private void FitBloodToLayout()
    {
        if (bloodRect == null || bloodMaterial == null)
        {
            return;
        }

        Rect area = bloodRect.rect;
        if (area.height <= 0f || area.width <= 0f)
        {
            return;
        }

        bloodMaterial.SetFloat(AspectId, area.width / area.height);
        FitBloodToShape(area);
        bloodLands = false;
        if (!bloodSurfaceBelow)
        {
            return;
        }

        float panelBottom = LocalBottomOf((RectTransform)transform);
        float bestTop = float.NegativeInfinity;
        Selectable[] all = Selectable.allSelectablesArray;
        var corners = new Vector3[4];
        for (int i = 0; i < all.Length; i++)
        {
            Selectable other = all[i];
            if (other == null || other.gameObject == gameObject || !other.gameObject.activeInHierarchy)
            {
                continue;
            }

            var otherRect = (RectTransform)other.transform;
            otherRect.GetWorldCorners(corners);
            float xMin = float.PositiveInfinity, xMax = float.NegativeInfinity, top = float.NegativeInfinity;
            for (int c = 0; c < 4; c++)
            {
                Vector3 local = bloodRect.InverseTransformPoint(corners[c]);
                xMin = Mathf.Min(xMin, local.x);
                xMax = Mathf.Max(xMax, local.x);
                top = Mathf.Max(top, local.y);
            }

            float overlap = Mathf.Min(xMax, area.xMax) - Mathf.Max(xMin, area.xMin);
            bool below = top <= panelBottom + 0.5f && top > area.yMin;
            if (below && overlap >= area.width * MinimumSurfaceOverlap && top > bestTop)
            {
                bestTop = top;
            }
        }

        if (!float.IsNegativeInfinity(bestTop))
        {
            bloodLands = true;
            bloodMaterial.SetFloat(SurfaceYId, (area.yMax - bestTop) / area.height);
        }
    }

    /// <summary>
    /// Tells the shader the button's face (and rounded corners, from a dashed frame edge) so the
    /// blood stays on the button's shape instead of filling a rectangle.
    /// </summary>
    private void FitBloodToShape(Rect area)
    {
        float inset = 0f;
        float radius = 0f;
        if (edgeGraphic is TicketRuleGraphic frame && frame.Kind == TicketRuleGraphic.RuleKind.Frame)
        {
            inset = frame.Inset;
            radius = frame.CornerRadius;
        }

        float panelHeight = area.yMax - LocalBottomOf((RectTransform)transform);
        float h = area.height;
        bloodMaterial.SetVector(FaceId, new Vector4(inset / h, inset / h, (area.width - inset) / h, (panelHeight - inset) / h));
        bloodMaterial.SetFloat(CornerRadiusId, radius / h);
    }

    private float LocalBottomOf(RectTransform target)
    {
        var corners = new Vector3[4];
        target.GetWorldCorners(corners);
        float bottom = float.PositiveInfinity;
        for (int c = 0; c < 4; c++)
        {
            bottom = Mathf.Min(bottom, bloodRect.InverseTransformPoint(corners[c]).y);
        }

        return bottom;
    }

    private void UpdateTremble(float dt)
    {
        trembleTimer -= dt;
        if (trembleTimer <= 0f)
        {
            trembleTimer = TrembleStep;
            trembleOffset = Random.insideUnitCircle * hoverTremble;
        }
    }

    private void UpdateTwitch(float dt)
    {
        if (twitchTime >= 0f)
        {
            twitchTime += dt;
            if (twitchTime > TwitchLength)
            {
                twitchTime = -1f;
            }
        }

        twitchTimer -= dt;
        if (twitchTimer <= 0f)
        {
            twitchTimer = Random.Range(twitchInterval.x, twitchInterval.y);
            twitchTime = 0f;
            twitchDirection = new Vector2(Random.Range(-1f, 1f), Random.Range(-0.3f, 0.3f)).normalized;
        }
    }

    private Vector2 TwitchOffset()
    {
        if (twitchTime < 0f)
        {
            return Vector2.zero;
        }

        float t = twitchTime / TwitchLength;
        return twitchDirection * (Mathf.Sin(t * Mathf.PI * 3f) * (1f - t) * twitchPixels);
    }

    private static float EaseOut(float t)
    {
        float inv = 1f - t;
        return 1f - inv * inv;
    }
}
