using UnityEngine.SceneManagement;

/// <summary>
/// Scene names and the two load calls, so no scene-name string is duplicated in view code.
/// </summary>
public static class SceneFlow
{
    /// <summary>Name of the gameplay scene.</summary>
    public const string GameSceneName = "Game";

    /// <summary>Name of the level-select scene.</summary>
    public const string LevelSelectSceneName = "LevelSelect";

    /// <summary>Loads the gameplay scene.</summary>
    public static void LoadGame()
    {
        SceneManager.LoadScene(GameSceneName);
    }

    /// <summary>Loads the level-select scene.</summary>
    public static void LoadLevelSelect()
    {
        SceneManager.LoadScene(LevelSelectSceneName);
    }
}
