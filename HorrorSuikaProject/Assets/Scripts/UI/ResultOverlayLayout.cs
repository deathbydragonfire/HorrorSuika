using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Places the game-over or victory stack in the play area, beside the ticket in landscape and
/// below it in portrait, and keeps the stack inside that area.
/// </summary>
[DisallowMultipleComponent]
public class ResultOverlayLayout : MonoBehaviour
{
    private const float SideMargin = 28f;
    private const float MaxContentWidth = 520f;
    private const float MinContentWidth = 240f;

    [Tooltip("Column of the headline and buttons. The dimmer stays full screen.")]
    [SerializeField] private RectTransform content;

    [Tooltip("Camera whose viewport is the play area. Falls back to the full screen.")]
    [SerializeField] private Camera gameplayCamera;

    private int cachedWidth = -1;
    private int cachedHeight = -1;
    private Rect cachedPlayRect;

    private void OnEnable()
    {
        Apply();
        // Children's layout components enable after this one, so the height measured above can
        // read as zero; invalidating the cache makes LateUpdate measure again before the frame draws.
        cachedWidth = -1;
    }

    private void LateUpdate()
    {
        Rect play = PlayPixels();
        if (Screen.width == cachedWidth && Screen.height == cachedHeight && play == cachedPlayRect)
        {
            return;
        }

        Apply();
    }

    /// <summary>Centers the stack in the current play area and caps it to that area.</summary>
    public void Apply()
    {
        if (content == null)
        {
            return;
        }

        Rect play = PlayPixels();
        RectTransform panel = (RectTransform)transform;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(panel, play.min, null, out Vector2 min)
            || !RectTransformUtility.ScreenPointToLocalPointInRectangle(panel, play.max, null, out Vector2 max))
        {
            return;
        }

        Vector2 area = max - min;
        float width = Mathf.Clamp(area.x - SideMargin * 2f, MinContentWidth, MaxContentWidth);
        content.anchorMin = new Vector2(0.5f, 0.5f);
        content.anchorMax = new Vector2(0.5f, 0.5f);
        content.pivot = new Vector2(0.5f, 0.5f);
        content.anchoredPosition = (min + max) * 0.5f;
        content.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);

        LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        float preferred = LayoutUtility.GetPreferredHeight(content);
        float height = Mathf.Min(Mathf.Max(80f, preferred), Mathf.Max(80f, area.y - SideMargin * 2f));
        content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);

        cachedWidth = Screen.width;
        cachedHeight = Screen.height;
        cachedPlayRect = play;
    }

    private Rect PlayPixels()
    {
        if (gameplayCamera == null)
        {
            return new Rect(0f, 0f, Screen.width, Screen.height);
        }

        return gameplayCamera.pixelRect;
    }
}
