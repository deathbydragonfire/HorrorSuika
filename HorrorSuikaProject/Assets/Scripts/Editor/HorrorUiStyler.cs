using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Applies the horror UI look to the open scene or the level button prefab: dark blood-stained
/// cards, bone-white text (never red on red), crimson rules and edges, the Anton poster font on
/// titles and buttons, and <see cref="HorrorMenuButton"/> with its hover blood. Safe to re-run.
/// </summary>
public static class HorrorUiStyler
{
    private const string FontPath = "Assets/Fonts/Anton Horror SDF.asset";
    private const string BloodMaterialPath = "Assets/Materials/UI/M_UIBloodDrip.mat";
    private const string LevelButtonPrefabPath = "Assets/Prefabs/UI/LevelButton.prefab";

    /// <summary>Sorting for overlay panels (pause, results) so they sit above HUD blood and labels.</summary>
    private const int OverlaySortingOrder = 10;

    public static readonly Color Bone = new Color(0.86f, 0.8f, 0.72f, 1f);
    public static readonly Color BoneDim = new Color(0.62f, 0.56f, 0.5f, 1f);
    public static readonly Color Card = new Color(0.075f, 0.028f, 0.032f, 0.94f);
    public static readonly Color Crimson = new Color(0.55f, 0.06f, 0.08f, 0.9f);
    public static readonly Color BloodText = new Color(1f, 0.24f, 0.2f, 1f);
    public static readonly Color IdlePanel = new Color(0.07f, 0.02f, 0.025f, 0.88f);
    public static readonly Color IdleEdge = new Color(0.42f, 0.04f, 0.06f, 0.85f);

    [MenuItem("Tools/Horror/Apply Spooky UI To Open Scene")]
    public static void StyleOpenScene()
    {
        var scene = EditorSceneManager.GetActiveScene();
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.GetComponent<Canvas>() == null || root.name == "VHS Overlay")
            {
                continue;
            }

            StyleCanvas(root.transform);
        }

        EditorSceneManager.MarkSceneDirty(scene);
    }

    [MenuItem("Tools/Horror/Apply Spooky UI To Level Button Prefab")]
    public static void StyleLevelButtonPrefab()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(LevelButtonPrefabPath);
        try
        {
            var button = root.GetComponent<Button>();
            StyleButton(button, false, 0, 0f);
            foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text.name == "TitleLabel")
                {
                    StyleTitle(text, Bone, 1f);
                }
                else if (text.name == "LockLabel")
                {
                    StyleTitle(text, BoneDim, 1f);
                }
                else
                {
                    text.color = BoneDim;
                }
            }

            Transform lockIcon = root.transform.Find("LockIcon");
            if (lockIcon != null && lockIcon.TryGetComponent(out Image lockImage))
            {
                lockImage.color = new Color(0.02f, 0.005f, 0.008f, 0.78f);
            }

            Transform tick = root.transform.Find("CompletionTick");
            if (tick != null && tick.TryGetComponent(out Image tickImage))
            {
                tickImage.color = Crimson;
            }

            PrefabUtility.SaveAsPrefabAsset(root, LevelButtonPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void StyleCanvas(Transform canvasRoot)
    {
        // Overlay panels get their own sorting so HUD blood and labels never draw over them.
        foreach (Transform child in canvasRoot.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == "GameOverPanel" || child.name == "VictoryPanel"
                || (child.name == "Overlay" && child.parent != null && child.parent.name == "PauseMenu"))
            {
                EnsureSortingCanvas(child.gameObject, OverlaySortingOrder, true);
            }
        }

        foreach (Button button in canvasRoot.GetComponentsInChildren<Button>(true))
        {
            // Level tiles live in a scrolling, masked grid: nested canvases would escape the mask.
            if (button.GetComponent<LevelButtonView>() != null)
            {
                continue;
            }

            int layer = SortingBaseFor(button.transform);
            StyleButton(button, true, layer, 0f);
        }

        foreach (TicketRuleGraphic rule in canvasRoot.GetComponentsInChildren<TicketRuleGraphic>(true))
        {
            if (rule.GetComponentInParent<Button>() == null)
            {
                rule.color = Crimson;
                EditorUtility.SetDirty(rule);
            }
        }

        foreach (Image image in canvasRoot.GetComponentsInChildren<Image>(true))
        {
            if (image.GetComponent<Button>() != null)
            {
                continue;
            }

            if (image.sprite != null && image.sprite.name == "TicketCard")
            {
                image.color = Card;
            }
            else if (image.name == "Dimmer")
            {
                image.color = new Color(0.02f, 0.004f, 0.008f, 0.82f);
            }
            else if (image.name == "Background" && image.GetComponentInParent<Slider>() != null)
            {
                image.color = new Color(0.2f, 0.07f, 0.075f, 1f);
            }
            else if (image.name == "Handle")
            {
                image.color = Bone;
            }
            else if (image.name == "Fill")
            {
                image.color = new Color(0.62f, 0.06f, 0.08f, 1f);
            }
            else
            {
                continue;
            }

            EditorUtility.SetDirty(image);
        }

        foreach (TMP_Text text in canvasRoot.GetComponentsInChildren<TMP_Text>(true))
        {
            if (text.GetComponentInParent<Button>() != null)
            {
                continue;
            }

            switch (text.name)
            {
                case "Title":
                case "LevelLabel":
                case "OrderFormTitle":
                    StyleTitle(text, Bone, 1.1f);
                    break;
                case "ResultLabel":
                    bool gameOver = text.transform.parent != null && text.transform.parent.parent != null
                        && text.transform.parent.parent.name == "GameOverPanel";
                    StyleTitle(text, gameOver ? BloodText : Bone, 1.05f);
                    break;
                case "LimitLabel":
                case "OnOne":
                case "AtOnce":
                    text.color = BoneDim;
                    break;
                default:
                    text.color = Bone;
                    break;
            }

            EditorUtility.SetDirty(text);
        }
    }

    private static int SortingBaseFor(Transform button)
    {
        for (Transform t = button; t != null; t = t.parent)
        {
            if (t != button && t.TryGetComponent(out Canvas canvas))
            {
                var serialized = new SerializedObject(canvas);
                if (serialized.FindProperty("m_OverrideSorting").boolValue)
                {
                    return serialized.FindProperty("m_SortingOrder").intValue;
                }
            }
        }

        return 0;
    }

    /// <summary>
    /// Dark panel, crimson edge, Anton bone label, <see cref="HorrorMenuButton"/>. With blood, adds a
    /// drip layer that hangs below the button, sorted above neighbouring buttons but under all labels.
    /// </summary>
    public static void StyleButton(Button button, bool blood, int sortingBase, float hangPixels)
    {
        GameObject go = button.gameObject;
        Undo.RecordObject(button, "Spooky UI");
        button.transition = Selectable.Transition.None;

        TMP_Text label = null;
        foreach (TMP_Text text in go.GetComponentsInChildren<TMP_Text>(true))
        {
            if (text.transform.parent == go.transform)
            {
                label = text;
                break;
            }
        }

        if (label != null && go.GetComponent<LevelButtonView>() == null)
        {
            StyleTitle(label, Bone, 1.12f);
        }

        Graphic edgeGraphic = null;
        Outline outline = null;
        Transform dashed = go.transform.Find("Outline");
        if (dashed != null && dashed.TryGetComponent(out TicketRuleGraphic rule))
        {
            edgeGraphic = rule;
        }
        else
        {
            outline = go.GetComponent<Outline>();
            if (outline == null)
            {
                outline = Undo.AddComponent<Outline>(go);
            }

            outline.effectDistance = new Vector2(2f, -2f);
            outline.useGraphicAlpha = false;
        }

        var style = go.GetComponent<HorrorMenuButton>();
        if (style == null)
        {
            style = Undo.AddComponent<HorrorMenuButton>(go);
        }

        // Write the idle look into the saved objects too, so the editor view and the first
        // frame never show light text on the old light panel.
        var panel = go.GetComponent<Graphic>();
        if (panel != null)
        {
            Undo.RecordObject(panel, "Spooky UI");
            panel.color = IdlePanel;
            EditorUtility.SetDirty(panel);
        }

        if (outline != null)
        {
            outline.effectColor = IdleEdge;
            EditorUtility.SetDirty(outline);
        }

        if (edgeGraphic != null)
        {
            Undo.RecordObject(edgeGraphic, "Spooky UI");
            edgeGraphic.color = IdleEdge;
            EditorUtility.SetDirty(edgeGraphic);
        }

        var so = new SerializedObject(style);
        so.FindProperty("label").objectReferenceValue = label;
        so.FindProperty("panel").objectReferenceValue = panel;
        so.FindProperty("edge").objectReferenceValue = outline;
        so.FindProperty("edgeGraphic").objectReferenceValue = edgeGraphic;
        so.FindProperty("idleText").colorValue = Bone;
        so.FindProperty("idlePanel").colorValue = IdlePanel;
        so.FindProperty("idleEdge").colorValue = IdleEdge;
        so.FindProperty("hoverText").colorValue = new Color(1f, 0.95f, 0.88f, 1f);
        so.FindProperty("hoverPanel").colorValue = new Color(0.26f, 0.02f, 0.04f, 0.94f);
        so.FindProperty("hoverEdge").colorValue = new Color(0.9f, 0.08f, 0.1f, 1f);
        so.FindProperty("beatPulse").floatValue = 0.15f;
        so.FindProperty("hoverScale").floatValue = 1.02f;

        if (blood)
        {
            Graphic drip = EnsureBloodDrip(go, hangPixels);
            so.FindProperty("bloodDrip").objectReferenceValue = drip;
            so.FindProperty("bloodSurfaceBelow").boolValue = true;
            EnsureSortingCanvas(drip.gameObject, sortingBase + 1, false);
            if (label != null)
            {
                EnsureSortingCanvas(label.gameObject, sortingBase + 2, false);
            }
        }

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(go);
    }

    /// <summary>Anton poster font, upper case, spaced, with the red glow material.</summary>
    public static void StyleTitle(TMP_Text text, Color color, float sizeScale)
    {
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        Undo.RecordObject(text, "Spooky UI");
        if (font != null && text.font != font)
        {
            text.font = font;
            text.fontSharedMaterial = font.material;
            text.fontSize *= sizeScale;
            if (text.enableAutoSizing)
            {
                text.fontSizeMax *= sizeScale;
            }
        }

        text.fontStyle = (text.fontStyle & ~FontStyles.LowerCase & ~FontStyles.SmallCaps) | FontStyles.UpperCase;
        text.characterSpacing = Mathf.Max(text.characterSpacing, 8f);
        text.color = color;
        EditorUtility.SetDirty(text);
    }

    private static Graphic EnsureBloodDrip(GameObject button, float hangPixels)
    {
        Transform existing = button.transform.Find("BloodDrip");
        GameObject drip = existing != null ? existing.gameObject : new GameObject("BloodDrip", typeof(RectTransform));
        if (existing == null)
        {
            Undo.RegisterCreatedObjectUndo(drip, "Spooky UI");
            drip.transform.SetParent(button.transform, false);
        }

        drip.transform.SetSiblingIndex(0);
        if (hangPixels <= 0f)
        {
            hangPixels = EstimateHeight(button) * 1.3f;
        }

        var rect = (RectTransform)drip.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = new Vector2(0f, -hangPixels);
        rect.offsetMax = Vector2.zero;

        var raw = drip.GetComponent<RawImage>();
        if (raw == null)
        {
            raw = drip.AddComponent<RawImage>();
        }

        raw.material = AssetDatabase.LoadAssetAtPath<Material>(BloodMaterialPath);
        raw.raycastTarget = false;
        raw.color = Color.clear;
        return raw;
    }

    private static float EstimateHeight(GameObject button)
    {
        if (button.TryGetComponent(out LayoutElement layout) && layout.preferredHeight > 0f)
        {
            return layout.preferredHeight;
        }

        float height = ((RectTransform)button.transform).rect.height;
        return height > 1f ? height : 90f;
    }

    private static void EnsureSortingCanvas(GameObject target, int order, bool raycasts)
    {
        var canvas = target.GetComponent<Canvas>();
        if (canvas == null)
        {
            canvas = Undo.AddComponent<Canvas>(target);
        }

        // Canvas.overrideSorting is silently ignored while the object is inactive (hidden panels
        // such as pause and results), so write the serialized fields directly.
        var serialized = new SerializedObject(canvas);
        serialized.FindProperty("m_OverrideSorting").boolValue = true;
        serialized.FindProperty("m_SortingOrder").intValue = order;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        if (raycasts && target.GetComponent<GraphicRaycaster>() == null)
        {
            Undo.AddComponent<GraphicRaycaster>(target);
        }

        EditorUtility.SetDirty(target);
    }
}
