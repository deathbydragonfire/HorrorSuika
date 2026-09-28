using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Places the level-info box across the top in portrait and down the left side in landscape,
/// and insets the gameplay camera so the container stays visible in the remaining region.
/// </summary>
[DefaultExecutionOrder(-50)]
public class LevelInfoPanelLayout : MonoBehaviour
{
    [Header("Layout")]
    [SerializeField] private RectTransform contentRoot;
    [SerializeField] private LayoutElement infoElement;
    [SerializeField] private LayoutElement objectiveElement;
    [SerializeField] private LayoutElement buttonElement;
    [SerializeField] private RectTransform edgeAccent;

    [Header("Scene")]
    [SerializeField] private Camera gameplayCamera;
    [SerializeField] private PortraitCameraFitter cameraFitter;

    [SerializeField] private float portraitHeight = 320f;
    [SerializeField] private float landscapeWidth = 420f;
    [SerializeField] private float ticketRowHeight = 96f;

    private RectTransform panelRect;
    private int cachedScreenWidth = -1;
    private int cachedScreenHeight = -1;
    private int cachedRowCount = -1;
    private int cachedChromeKey = -1;

    private void Awake()
    {
        panelRect = (RectTransform)transform;
    }

    private void OnEnable()
    {
        Apply();
    }

    private void Update()
    {
        int rowsKey = Mathf.RoundToInt(TicketRowsHeight());
        int chromeKey = ObjectiveChromeKey();
        if (Screen.width == cachedScreenWidth && Screen.height == cachedScreenHeight && rowsKey == cachedRowCount && chromeKey == cachedChromeKey)
        {
            return;
        }

        if (!Apply())
        {
            return;
        }

        cachedScreenWidth = Screen.width;
        cachedScreenHeight = Screen.height;
        cachedRowCount = rowsKey;
        cachedChromeKey = chromeKey;
    }

    /// <summary>Rebuilds anchors, the inner layout, and the camera inset for the current screen.</summary>
    public bool Apply()
    {
        if (panelRect == null)
        {
            panelRect = (RectTransform)transform;
        }

        int width = Screen.width;
        int height = Screen.height;
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        bool portrait = height >= width;
        if (portrait)
        {
            ApplyPortrait();
        }
        else
        {
            ApplyLandscape();
        }

        Canvas.ForceUpdateCanvases();
        if (!ApplyCameraViewport())
        {
            return false;
        }

        if (cameraFitter != null)
        {
            cameraFitter.Refit();
        }

        return true;
    }

    private void ApplyPortrait()
    {
        panelRect.anchorMin = new Vector2(0f, 1f);
        panelRect.anchorMax = new Vector2(1f, 1f);
        panelRect.pivot = new Vector2(0.5f, 1f);
        panelRect.anchoredPosition = Vector2.zero;
        float rowsHeight = TicketRowsHeight();
        float chrome = ObjectiveChromeHeight();
        float infoHeight = InfoSectionHeight();
        float height = Mathf.Max(portraitHeight, 196f + infoHeight + rowsHeight + chrome);
        panelRect.sizeDelta = new Vector2(0f, height);

        ConfigureGroup(EnsureGroup(horizontal: false), new RectOffset(28, 28, 22, 22), 10f, TextAnchor.UpperCenter, expandWidth: true, expandHeight: false);
        SetSectionFlex(infoElement, width: 1f, height: 0f, preferredWidth: -1f, preferredHeight: infoHeight);
        SetSectionFlex(objectiveElement, width: 1f, height: 0f, preferredWidth: -1f, preferredHeight: rowsHeight + chrome);
        SetSectionFlex(buttonElement, width: 1f, height: 0f, preferredWidth: -1f, preferredHeight: 72f);
        AlignButtons(TextAnchor.MiddleCenter);
        PlaceAccent(bottom: true);
    }

    private void ApplyLandscape()
    {
        panelRect.anchorMin = new Vector2(0f, 0f);
        panelRect.anchorMax = new Vector2(0f, 1f);
        panelRect.pivot = new Vector2(0f, 0.5f);
        panelRect.anchoredPosition = Vector2.zero;
        panelRect.sizeDelta = new Vector2(landscapeWidth, 0f);

        ConfigureGroup(EnsureGroup(horizontal: false), new RectOffset(22, 22, 28, 28), 10f, TextAnchor.UpperCenter, expandWidth: true, expandHeight: false);
        SetSectionFlex(infoElement, width: 1f, height: 0f, preferredWidth: -1f, preferredHeight: InfoSectionHeight());
        SetSectionFlex(objectiveElement, width: 1f, height: 1f, preferredWidth: -1f, preferredHeight: ticketRowHeight);
        SetSectionFlex(buttonElement, width: 1f, height: 0f, preferredWidth: -1f, preferredHeight: 72f);
        AlignButtons(TextAnchor.LowerCenter);
        PlaceAccent(bottom: false);
    }

    private float TicketRowsHeight()
    {
        if (objectiveElement == null || !objectiveElement.gameObject.activeInHierarchy)
        {
            return 0f;
        }

        float score = ScoreRowHeight();
        LevelObjectiveRowView[] rows = objectiveElement.GetComponentsInChildren<LevelObjectiveRowView>(false);
        if (rows.Length == 0)
        {
            return score > 0f ? score : ticketRowHeight;
        }

        float total = 0f;
        for (int i = 0; i < rows.Length; i++)
        {
            LayoutElement element = rows[i].GetComponent<LayoutElement>();
            total += element != null && element.preferredHeight > 0f ? element.preferredHeight : ticketRowHeight;
        }

        return total + score;
    }

    private float ScoreRowHeight()
    {
        if (objectiveElement == null)
        {
            return 0f;
        }

        Transform score = objectiveElement.transform.Find("ScoreLabel");
        if (score == null || !score.gameObject.activeInHierarchy)
        {
            return 0f;
        }

        LayoutElement element = score.GetComponent<LayoutElement>();
        if (element != null && element.preferredHeight > 0f)
        {
            return element.preferredHeight;
        }

        return ticketRowHeight;
    }

    private float ObjectiveSplitHeight()
    {
        if (objectiveElement == null)
        {
            return 0f;
        }

        Transform split = objectiveElement.transform.Find("ObjectiveSplit");
        if (split == null || !split.gameObject.activeSelf)
        {
            return 0f;
        }

        return 12f;
    }

    private float InfoSectionHeight()
    {
        return 108f;
    }

    private float ObjectiveChromeHeight()
    {
        if (objectiveElement == null)
        {
            return 0f;
        }

        return ObjectiveSplitHeight()
            + ActiveChromeHeight("OnOneList/OnOne", objectiveElement.transform)
            + ActiveChromeHeight("QuotaList/AtOnce", objectiveElement.transform);
    }

    private int ObjectiveChromeKey()
    {
        if (objectiveElement == null)
        {
            return 0;
        }

        int key = 0;
        Transform onOne = objectiveElement.transform.Find("OnOneList/OnOne");
        if (onOne != null && onOne.gameObject.activeInHierarchy)
        {
            key |= 1;
        }

        Transform atOnce = objectiveElement.transform.Find("QuotaList/AtOnce");
        if (atOnce != null && atOnce.gameObject.activeInHierarchy)
        {
            key |= 2;
        }

        Transform split = objectiveElement.transform.Find("ObjectiveSplit");
        if (split != null && split.gameObject.activeSelf)
        {
            key |= 4;
        }

        return key;
    }

    private static float ActiveChromeHeight(string childPath, Transform root)
    {
        if (root == null)
        {
            return 0f;
        }

        Transform child = root.Find(childPath);
        if (child == null || !child.gameObject.activeInHierarchy)
        {
            return 0f;
        }

        LayoutElement element = child.GetComponent<LayoutElement>();
        if (element != null && element.preferredHeight > 0f)
        {
            return element.preferredHeight;
        }

        return 32f;
    }

    private HorizontalOrVerticalLayoutGroup EnsureGroup(bool horizontal)
    {
        if (contentRoot == null)
        {
            return null;
        }

        HorizontalOrVerticalLayoutGroup current = contentRoot.GetComponent<HorizontalOrVerticalLayoutGroup>();
        bool matches = horizontal ? current is HorizontalLayoutGroup : current is VerticalLayoutGroup;
        if (current != null && matches)
        {
            return current;
        }

        // DestroyImmediate so the replacement can be added in the same call. LayoutGroup
        // disallows more than one of itself on a transform.
        if (current != null)
        {
            DestroyImmediate(current);
        }

        if (horizontal)
        {
            return contentRoot.gameObject.AddComponent<HorizontalLayoutGroup>();
        }

        return contentRoot.gameObject.AddComponent<VerticalLayoutGroup>();
    }

    private static void ConfigureGroup(
        HorizontalOrVerticalLayoutGroup group,
        RectOffset padding,
        float spacing,
        TextAnchor alignment,
        bool expandWidth,
        bool expandHeight)
    {
        if (group == null)
        {
            return;
        }

        group.padding = padding;
        group.spacing = spacing;
        group.childAlignment = alignment;
        group.childControlWidth = true;
        group.childControlHeight = true;
        group.childForceExpandWidth = expandWidth;
        group.childForceExpandHeight = expandHeight;
        group.childScaleWidth = false;
        group.childScaleHeight = false;
    }

    private void AlignButtons(TextAnchor alignment)
    {
        if (buttonElement == null)
        {
            return;
        }

        HorizontalOrVerticalLayoutGroup group = buttonElement.GetComponent<HorizontalOrVerticalLayoutGroup>();
        if (group != null)
        {
            group.childAlignment = alignment;
        }
    }

    private static void SetSectionFlex(LayoutElement element, float width, float height, float preferredWidth, float preferredHeight)
    {
        if (element == null)
        {
            return;
        }

        element.flexibleWidth = width;
        element.flexibleHeight = height;
        element.preferredWidth = preferredWidth;
        element.preferredHeight = preferredHeight;
    }

    private void PlaceAccent(bool bottom)
    {
        if (edgeAccent == null)
        {
            return;
        }

        if (bottom)
        {
            edgeAccent.anchorMin = new Vector2(0f, 0f);
            edgeAccent.anchorMax = new Vector2(1f, 0f);
            edgeAccent.pivot = new Vector2(0.5f, 0f);
            edgeAccent.sizeDelta = new Vector2(0f, 4f);
        }
        else
        {
            edgeAccent.anchorMin = new Vector2(1f, 0f);
            edgeAccent.anchorMax = new Vector2(1f, 1f);
            edgeAccent.pivot = new Vector2(1f, 0.5f);
            edgeAccent.sizeDelta = new Vector2(4f, 0f);
        }

        edgeAccent.anchoredPosition = Vector2.zero;
    }

    private bool ApplyCameraViewport()
    {
        if (gameplayCamera == null)
        {
            return true;
        }

        if (!IsCanvasScaleReady())
        {
            return false;
        }

        Vector3[] corners = new Vector3[4];
        panelRect.GetWorldCorners(corners);
        float screenWidth = Screen.width;
        float screenHeight = Screen.height;
        if (screenWidth <= 0f || screenHeight <= 0f)
        {
            return false;
        }

        float left = corners[0].x / screenWidth;
        float right = corners[2].x / screenWidth;
        float bottom = corners[0].y / screenHeight;
        float top = corners[1].y / screenHeight;

        // A collapsed read means the canvas has not laid out yet.
        if (top <= bottom || right <= left)
        {
            return false;
        }

        bool portrait = screenHeight >= screenWidth;
        Rect viewport;
        if (portrait)
        {
            float playHeight = Mathf.Clamp01(bottom);
            viewport = new Rect(0f, 0f, 1f, Mathf.Max(0.2f, playHeight));
        }
        else
        {
            float playX = Mathf.Clamp01(right);
            viewport = new Rect(playX, 0f, Mathf.Max(0.2f, 1f - playX), 1f);
        }

        gameplayCamera.rect = viewport;
        return true;
    }

    private bool IsCanvasScaleReady()
    {
        Canvas canvas = panelRect.GetComponentInParent<Canvas>();
        if (canvas == null)
        {
            return false;
        }

        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler == null || scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize)
        {
            return true;
        }

        float widthScale = Screen.width / scaler.referenceResolution.x;
        float heightScale = Screen.height / scaler.referenceResolution.y;
        float logWidth = Mathf.Log(Mathf.Max(widthScale, 0.0001f), 2f);
        float logHeight = Mathf.Log(Mathf.Max(heightScale, 0.0001f), 2f);
        float expected = Mathf.Pow(2f, Mathf.Lerp(logWidth, logHeight, scaler.matchWidthOrHeight));
        return Mathf.Abs(canvas.scaleFactor - expected) <= 0.02f;
    }
}
