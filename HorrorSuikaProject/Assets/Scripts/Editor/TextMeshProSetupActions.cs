using System.IO;
using Bezi;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor-only helpers for one-time TextMeshPro project setup.
/// </summary>
public static class TextMeshProSetupActions
{
    private const string EssentialResourcesPackagePath = "Packages/com.unity.ugui/Package Resources/TMP Essential Resources.unitypackage";
    private const string ImportedMarkerPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";

    [BeziAction("Imports the TextMeshPro Essential Resources unitypackage (fonts, shaders, TMP Settings) into Assets so TMP text components can resolve a default font. No-op when already imported.")]
    public static string ImportTextMeshProEssentialResources()
    {
        if (File.Exists(ImportedMarkerPath))
        {
            return "TMP Essential Resources are already imported.";
        }

        string fullPath = Path.GetFullPath(EssentialResourcesPackagePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"TMP Essential Resources package not found at '{EssentialResourcesPackagePath}'.");
        }

        AssetDatabase.ImportPackage(fullPath, false);
        AssetDatabase.Refresh();
        return $"Imported TMP Essential Resources from '{EssentialResourcesPackagePath}'.";
    }
}
