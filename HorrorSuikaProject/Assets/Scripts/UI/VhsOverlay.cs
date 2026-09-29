using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Game-wide VHS layer over the UI. The 3D view gets its warp and chroma smear from the
/// VHSFullscreen renderer feature; Screen Space Overlay canvases draw after that pass, so this
/// adds scanlines, static, and the tracking band on a top-most canvas that survives scene loads.
/// It also decides where the tape look applies: menus, pause, and result screens get it, while
/// regular gameplay stays clean (only the vignette remains) so the playfield is easy to read.
/// </summary>
[DisallowMultipleComponent]
public class VhsOverlay : MonoBehaviour
{
    private const string MaterialResourcePath = "VHS/M_VhsOverlay";
    private const int TopSortingOrder = 32000;
    private const float SuppressFadeSeconds = 0.3f;

    private static readonly int SuppressId = Shader.PropertyToID("_VhsSuppress");

    private static VhsOverlay instance;

    private RawImage image;
    private GameController gameController;
    private float suppress;

    /// <summary>Overall strength of the UI layer, 0..1.</summary>
    public static float Intensity
    {
        get => instance != null && instance.image != null ? instance.image.color.a : 0f;
        set
        {
            if (instance != null && instance.image != null)
            {
                Color color = instance.image.color;
                color.a = Mathf.Clamp01(value);
                instance.image.color = color;
            }
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (instance != null)
        {
            return;
        }

        Material material = Resources.Load<Material>(MaterialResourcePath);
        if (material == null)
        {
            return;
        }

        var root = new GameObject("VHS Overlay");
        DontDestroyOnLoad(root);
        instance = root.AddComponent<VhsOverlay>();

        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = TopSortingOrder;
        canvas.overrideSorting = true;

        var imageObject = new GameObject("Tape", typeof(RectTransform));
        imageObject.transform.SetParent(root.transform, false);
        var rect = (RectTransform)imageObject.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        instance.image = imageObject.AddComponent<RawImage>();
        instance.image.material = material;
        instance.image.raycastTarget = false;
        instance.image.color = Color.white;
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        gameController = FindFirstObjectByType<GameController>();
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        Shader.SetGlobalFloat(SuppressId, 0f);
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    private void Update()
    {
        float target = IsRegularGameplay() ? 1f : 0f;
        suppress = Mathf.MoveTowards(suppress, target, Time.unscaledDeltaTime / SuppressFadeSeconds);
        Shader.SetGlobalFloat(SuppressId, suppress);
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        gameController = FindFirstObjectByType<GameController>();
    }

    /// <summary>A level is running and nothing is covering it: not paused, not on a result screen.</summary>
    private bool IsRegularGameplay()
    {
        if (gameController == null || gameController.IsPaused)
        {
            return false;
        }

        GameController.GameState state = gameController.State;
        return state == GameController.GameState.Ready
            || state == GameController.GameState.Playing
            || state == GameController.GameState.VictoryPending;
    }
}
