using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;

/// <summary>
/// Switches the Game view between a portrait and a landscape resolution so the level-info
/// panel can be checked in both orientations from the main toolbar.
/// </summary>
[InitializeOnLoad]
public static class LevelInfoPreviewToggle
{
    private const string ToolbarPath = "Horror Suika/Orientation";
    private const string MenuPath = "Tools/Horror Suika/Preview Portrait";
    private const int PortraitWidth = 1080;
    private const int PortraitHeight = 1920;
    private const int LandscapeWidth = 1920;
    private const int LandscapeHeight = 1080;
    private const double ApplyTimeoutSeconds = 2.0;

    private static readonly Assembly EditorAssembly = typeof(Editor).Assembly;
    private static readonly Type GameViewType = EditorAssembly.GetType("UnityEditor.GameView");
    private static readonly Type SizesType = EditorAssembly.GetType("UnityEditor.GameViewSizes");
    private static readonly Type SizeType = EditorAssembly.GetType("UnityEditor.GameViewSize");
    private static readonly Type SizeEnum = EditorAssembly.GetType("UnityEditor.GameViewSizeType");

    private static int cachedSizeIndex = int.MinValue;
    private static int pendingWidth;
    private static int pendingHeight;
    private static double applyUntil;

    static LevelInfoPreviewToggle()
    {
        EditorApplication.update += RefreshToolbarWhenSizeChanges;
    }

    /// <summary>True when the Game view's selected resolution is taller than it is wide.</summary>
    public static bool IsPortraitPreview()
    {
        Vector2Int size = GetCurrentSize();
        return size.y > size.x && size.x > 0;
    }

    [MainToolbarElement(ToolbarPath, defaultDockPosition = MainToolbarDockPosition.Right)]
    private static MainToolbarElement CreateOrientationToggle()
    {
        bool portrait = IsPortraitPreview();
        string label = portrait ? "Portrait" : "Landscape";
        var content = new MainToolbarContent(label, "Switch the Game view between portrait and landscape.");
        return new MainToolbarToggle(content, portrait, OnToolbarToggle);
    }

    [MenuItem(MenuPath)]
    private static void ToggleFromMenu()
    {
        SetPortrait(!IsPortraitPreview());
    }

    [MenuItem(MenuPath, true)]
    private static bool ToggleFromMenuValidate()
    {
        Menu.SetChecked(MenuPath, IsPortraitPreview());
        return true;
    }

    private static void OnToolbarToggle(bool portrait)
    {
        SetPortrait(portrait);
    }

    private static void SetPortrait(bool portrait)
    {
        pendingWidth = portrait ? PortraitWidth : LandscapeWidth;
        pendingHeight = portrait ? PortraitHeight : LandscapeHeight;
        if (!TrySelectResolution(pendingWidth, pendingHeight))
        {
            Debug.LogWarning("Level info preview could not change the Game view resolution.");
            return;
        }

        cachedSizeIndex = int.MinValue;
        applyUntil = EditorApplication.timeSinceStartup + ApplyTimeoutSeconds;
        EditorApplication.update -= ApplyOpenLayouts;
        EditorApplication.update += ApplyOpenLayouts;
        EditorApplication.delayCall += RefreshToolbar;
    }

    private static void RefreshToolbar()
    {
        MainToolbar.Refresh(ToolbarPath);
    }

    private static void RefreshToolbarWhenSizeChanges()
    {
        EditorWindow gameView = GetGameView(create: false);
        if (gameView == null)
        {
            return;
        }

        int index = ReadSelectedIndex(gameView);
        if (index == cachedSizeIndex)
        {
            return;
        }

        cachedSizeIndex = index;
        MainToolbar.Refresh(ToolbarPath);
    }

    private static void ApplyOpenLayouts()
    {
        bool screenReady = Screen.width == pendingWidth && Screen.height == pendingHeight;
        if (!screenReady)
        {
            if (EditorApplication.timeSinceStartup > applyUntil)
            {
                EditorApplication.update -= ApplyOpenLayouts;
            }

            return;
        }

        LevelInfoPanelLayout[] layouts = UnityEngine.Object.FindObjectsByType<LevelInfoPanelLayout>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        bool applied = true;
        for (int i = 0; i < layouts.Length; i++)
        {
            applied = layouts[i].Apply() && applied;
        }

        if (applied || EditorApplication.timeSinceStartup > applyUntil)
        {
            EditorApplication.update -= ApplyOpenLayouts;
        }
    }

    private static bool TrySelectResolution(int width, int height)
    {
        if (GameViewType == null || SizesType == null || SizeType == null || SizeEnum == null)
        {
            return false;
        }

        EditorWindow gameView = GetGameView(create: true);
        object sizes = GetSizesInstance();
        object group = GetCurrentGroup(sizes);
        if (gameView == null || group == null)
        {
            return false;
        }

        int index = FindSizeIndex(group, width, height);
        if (index < 0)
        {
            object fixedResolution = Enum.Parse(SizeEnum, "FixedResolution");
            string label = height > width ? "Portrait" : "Landscape";
            object size = Activator.CreateInstance(SizeType, fixedResolution, width, height, label);
            group.GetType().GetMethod("AddCustomSize").Invoke(group, new object[] { size });
            index = FindSizeIndex(group, width, height);
        }

        if (index < 0)
        {
            return false;
        }

        MethodInfo select = GameViewType.GetMethod(
            "SizeSelectionCallback",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        select.Invoke(gameView, new object[] { index, null });
        gameView.Repaint();
        return true;
    }

    private static Vector2Int GetCurrentSize()
    {
        EditorWindow gameView = GetGameView(create: false);
        if (gameView == null)
        {
            return Vector2Int.zero;
        }

        PropertyInfo current = GameViewType.GetProperty(
            "currentGameViewSize",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        object size = current.GetValue(gameView, null);
        if (size == null)
        {
            return Vector2Int.zero;
        }

        int width = (int)SizeType.GetProperty("width").GetValue(size, null);
        int height = (int)SizeType.GetProperty("height").GetValue(size, null);
        return new Vector2Int(width, height);
    }

    private static int ReadSelectedIndex(EditorWindow gameView)
    {
        PropertyInfo selected = GameViewType.GetProperty(
            "selectedSizeIndex",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return (int)selected.GetValue(gameView, null);
    }

    private static EditorWindow GetGameView(bool create)
    {
        UnityEngine.Object[] views = Resources.FindObjectsOfTypeAll(GameViewType);
        for (int i = 0; i < views.Length; i++)
        {
            if (views[i] is EditorWindow window)
            {
                return window;
            }
        }

        if (!create)
        {
            return null;
        }

        return EditorWindow.GetWindow(GameViewType);
    }

    private static object GetSizesInstance()
    {
        Type singleton = typeof(ScriptableSingleton<>).MakeGenericType(SizesType);
        return singleton.GetProperty("instance").GetValue(null, null);
    }

    private static object GetCurrentGroup(object sizes)
    {
        PropertyInfo groupType = SizesType.GetProperty(
            "currentGroupType",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        object group = groupType.GetValue(sizes, null);
        return SizesType.GetMethod("GetGroup").Invoke(sizes, new object[] { group });
    }

    private static int FindSizeIndex(object group, int width, int height)
    {
        Type groupType = group.GetType();
        int total = (int)groupType.GetMethod("GetTotalCount").Invoke(group, null);
        MethodInfo getSize = groupType.GetMethod("GetGameViewSize");
        for (int i = 0; i < total; i++)
        {
            object size = getSize.Invoke(group, new object[] { i });
            int sizeWidth = (int)SizeType.GetProperty("width").GetValue(size, null);
            int sizeHeight = (int)SizeType.GetProperty("height").GetValue(size, null);
            if (sizeWidth == width && sizeHeight == height)
            {
                return i;
            }
        }

        return -1;
    }
}
