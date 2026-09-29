using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One ticket line. A board-wide count is a name plus a quantity. A single-sphere goal is one
/// line per feature, with X or Check on the right, under the On One heading.
/// </summary>
public class LevelObjectiveRowView : MonoBehaviour
{
    private const float QuotaRowHeight = 96f;
    private const float FeatureLineHeight = 72f;

    // Bone ink on the dark, blood-stained ticket. Stripes are a faint crimson wash.
    private static readonly Color Ink = new Color(0.86f, 0.8f, 0.72f, 1f);
    private static readonly Color Paper = new Color(0.07f, 0.025f, 0.03f, 0f);
    private static readonly Color Stripe = new Color(0.3f, 0.03f, 0.05f, 0.35f);

    [SerializeField] private TextMeshProUGUI labelText;
    [SerializeField] private TextMeshProUGUI counterText;
    [SerializeField] private Image rowBackground;

    private readonly List<TextMeshProUGUI> featureLabels = new List<TextMeshProUGUI>();
    private readonly List<TextMeshProUGUI> featureStatuses = new List<TextMeshProUGUI>();
    private int stripeIndex;

    /// <summary>Index of the objective this row reflects.</summary>
    public int ObjectiveIndex { get; private set; } = -1;

    /// <summary>Stripe slot among the board-wide rows. Ignored for single-sphere goals.</summary>
    public void SetStripe(int index)
    {
        stripeIndex = index;
    }

    /// <summary>Applies an objective snapshot to the row.</summary>
    public void Bind(LevelObjectiveTracker.ObjectiveProgress progress)
    {
        ObjectiveIndex = progress.Index;
        if (progress.IsOnOne)
        {
            BindOnOne(progress);
            return;
        }

        BindQuota(progress);
    }

    private void BindQuota(LevelObjectiveTracker.ObjectiveProgress progress)
    {
        UseSideBySide();
        SetRowHeight(QuotaRowHeight);
        SetPrefabTextsActive(true);
        EnsureFeatureLines(0);

        if (labelText != null)
        {
            labelText.text = progress.Label;
            labelText.color = Ink;
            labelText.fontSize = 32f;
            labelText.fontStyle = FontStyles.Bold;
            labelText.alignment = TextAlignmentOptions.MidlineLeft;
            ShowFullWords(labelText);
            SetPreferredHeight(labelText, -1f);
        }

        if (counterText != null)
        {
            int shown = Mathf.Min(Mathf.Max(0, progress.Current), Mathf.Max(0, progress.Required));
            counterText.gameObject.SetActive(true);
            counterText.text = $"{shown}/{Mathf.Max(0, progress.Required)}";
            counterText.color = Ink;
            counterText.fontSize = 36f;
            counterText.fontStyle = FontStyles.Bold;
            counterText.alignment = TextAlignmentOptions.MidlineRight;
            SetPreferredHeight(counterText, -1f);
            SetPreferredWidth(counterText, 128f, 96f);
        }

        if (rowBackground != null)
        {
            rowBackground.color = stripeIndex % 2 == 1 ? Stripe : Paper;
        }
    }

    private void BindOnOne(LevelObjectiveTracker.ObjectiveProgress progress)
    {
        string[] parts = progress.Parts;
        int lines = parts != null && parts.Length > 0 ? parts.Length : 1;
        UseStacked();
        SetPrefabTextsActive(false);
        EnsureFeatureLines(lines);
        SetRowHeight(lines * FeatureLineHeight);

        for (int i = 0; i < lines; i++)
        {
            string name = parts != null && i < parts.Length ? parts[i] : "On One";
            bool met = progress.PartMet != null && i < progress.PartMet.Length && progress.PartMet[i];
            featureLabels[i].text = name;
            featureStatuses[i].text = met ? "Check" : "X";
        }

        if (rowBackground != null)
        {
            rowBackground.color = Paper;
        }
    }

    private void SetPrefabTextsActive(bool active)
    {
        if (labelText != null)
        {
            labelText.gameObject.SetActive(active);
        }

        if (counterText != null)
        {
            counterText.gameObject.SetActive(active);
        }
    }

    private void EnsureFeatureLines(int count)
    {
        while (featureLabels.Count < count)
        {
            CreateFeatureLine();
        }

        for (int i = 0; i < featureLabels.Count; i++)
        {
            featureLabels[i].transform.parent.gameObject.SetActive(i < count);
        }
    }

    private void CreateFeatureLine()
    {
        TMP_FontAsset font = labelText != null ? labelText.font : null;
        var lineObject = new GameObject("Feature", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        lineObject.transform.SetParent(transform, false);

        HorizontalLayoutGroup row = lineObject.GetComponent<HorizontalLayoutGroup>();
        row.padding = new RectOffset(18, 18, 4, 4);
        row.spacing = 16f;
        row.childAlignment = TextAnchor.MiddleLeft;
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = false;

        LayoutElement lineElement = lineObject.GetComponent<LayoutElement>();
        lineElement.preferredHeight = FeatureLineHeight;
        lineElement.minHeight = FeatureLineHeight;
        lineElement.flexibleWidth = 1f;

        featureLabels.Add(CreateLineText(lineObject.transform, font, TextAlignmentOptions.MidlineLeft, -1f, -1f, 1f));
        featureStatuses.Add(CreateLineText(lineObject.transform, font, TextAlignmentOptions.MidlineRight, 160f, 96f, 0f));
    }

    private static TextMeshProUGUI CreateLineText(
        Transform parent,
        TMP_FontAsset font,
        TextAlignmentOptions alignment,
        float preferredWidth,
        float minimumWidth,
        float flexibleWidth)
    {
        var textObject = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI), typeof(LayoutElement));
        textObject.transform.SetParent(parent, false);
        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        text.font = font;
        text.fontSize = 32f;
        text.fontStyle = FontStyles.Bold;
        text.color = Ink;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;

        LayoutElement element = textObject.GetComponent<LayoutElement>();
        element.preferredWidth = preferredWidth;
        element.minWidth = minimumWidth;
        element.flexibleWidth = flexibleWidth;
        element.preferredHeight = FeatureLineHeight;
        return text;
    }

    private void UseSideBySide()
    {
        VerticalLayoutGroup stacked = GetComponent<VerticalLayoutGroup>();
        if (stacked != null)
        {
            DestroyImmediate(stacked);
        }

        HorizontalLayoutGroup row = GetComponent<HorizontalLayoutGroup>();
        if (row == null)
        {
            row = gameObject.AddComponent<HorizontalLayoutGroup>();
        }

        row.padding = new RectOffset(18, 18, 8, 8);
        row.spacing = 16f;
        row.childAlignment = TextAnchor.MiddleLeft;
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = false;
    }

    private void UseStacked()
    {
        HorizontalLayoutGroup sideBySide = GetComponent<HorizontalLayoutGroup>();
        if (sideBySide != null)
        {
            DestroyImmediate(sideBySide);
        }

        VerticalLayoutGroup stack = GetComponent<VerticalLayoutGroup>();
        if (stack == null)
        {
            stack = gameObject.AddComponent<VerticalLayoutGroup>();
        }

        stack.padding = new RectOffset(0, 0, 0, 0);
        stack.spacing = 0f;
        stack.childAlignment = TextAnchor.UpperLeft;
        stack.childControlWidth = true;
        stack.childControlHeight = true;
        stack.childForceExpandWidth = true;
        stack.childForceExpandHeight = false;
    }

    private void SetRowHeight(float height)
    {
        LayoutElement element = GetComponent<LayoutElement>();
        if (element == null)
        {
            return;
        }

        element.minHeight = height;
        element.preferredHeight = height;
    }

    private static void SetPreferredHeight(TextMeshProUGUI text, float height)
    {
        LayoutElement element = text.GetComponent<LayoutElement>();
        if (element == null)
        {
            return;
        }

        element.minHeight = height;
        element.preferredHeight = height;
    }

    private static void SetPreferredWidth(TextMeshProUGUI text, float preferred, float minimum)
    {
        LayoutElement element = text.GetComponent<LayoutElement>();
        if (element == null)
        {
            return;
        }

        element.preferredWidth = preferred;
        element.minWidth = minimum;
        element.flexibleWidth = preferred < 0f ? 1f : 0f;
    }

    private static void ShowFullWords(TextMeshProUGUI label)
    {
        label.textWrappingMode = TextWrappingModes.Normal;
        label.overflowMode = TextOverflowModes.Overflow;
    }
}
