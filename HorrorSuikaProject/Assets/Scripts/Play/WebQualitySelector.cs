using UnityEngine;

/// <summary>
/// Web builds default to the PC quality level; phone browsers drop back to the Mobile level so the
/// flesh raymarch keeps its frame rate. Must run at runtime because one web build serves both.
/// </summary>
public static class WebQualitySelector
{
    private const string MobileLevelName = "Mobile";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Select()
    {
        if (Application.platform != RuntimePlatform.WebGLPlayer || !Application.isMobilePlatform)
        {
            return;
        }

        int level = System.Array.IndexOf(QualitySettings.names, MobileLevelName);
        if (level >= 0 && level != QualitySettings.GetQualityLevel())
        {
            QualitySettings.SetQualityLevel(level, true);
        }
    }
}
