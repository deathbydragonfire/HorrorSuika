using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;

/// <summary>
/// Editor toolbar switch for skipping the current level. Starts off, and is not part of the game UI.
/// Turning it on marks the level won and loads the next one.
/// </summary>
[InitializeOnLoad]
public static class DebugNextLevelToggle
{
    private const string ToolbarPath = "Horror Suika/Next Level";
    private const string MenuPath = "Tools/Horror Suika/Next Level";

    private static bool enabled;

    /// <summary>True after the toolbar switch has been turned on this editor session.</summary>
    public static bool Enabled => enabled;

    [MainToolbarElement(ToolbarPath, defaultDockPosition = MainToolbarDockPosition.Right)]
    private static MainToolbarElement CreateToolbarToggle()
    {
        var content = new MainToolbarContent("Next Level", "Mark the current level won and load the next one.");
        return new MainToolbarToggle(content, enabled, OnToolbarToggle);
    }

    [MenuItem(MenuPath)]
    private static void ToggleFromMenu()
    {
        SetEnabled(!enabled);
    }

    [MenuItem(MenuPath, true)]
    private static bool ToggleFromMenuValidate()
    {
        Menu.SetChecked(MenuPath, enabled);
        return true;
    }

    private static void OnToolbarToggle(bool on)
    {
        SetEnabled(on);
    }

    private static void SetEnabled(bool on)
    {
        enabled = on;
        if (on)
        {
            TryAdvance();
        }

        EditorApplication.delayCall += RefreshToolbar;
    }

    private static void TryAdvance()
    {
        if (!EditorApplication.isPlaying)
        {
            return;
        }

        GameController game = Object.FindFirstObjectByType<GameController>();
        if (game == null)
        {
            return;
        }

        game.DebugCompleteAndAdvance();
    }

    private static void RefreshToolbar()
    {
        MainToolbar.Refresh(ToolbarPath);
    }
}
