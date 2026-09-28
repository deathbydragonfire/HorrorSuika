using UnityEngine;

/// <summary>
/// Swaps the inner eyeball sphere among authored meshes. The eyelid, blink, and look-at stay on this object,
/// and every option remains the same eye decoration to game logic.
/// </summary>
[DisallowMultipleComponent]
public class EyeballVariant : MonoBehaviour
{
    [System.Serializable]
    public struct SphereOption
    {
        [Tooltip("Inner globe mesh. The eyelid authored on this prefab stays in place.")]
        public Mesh mesh;

        [Tooltip("Material for that globe. The eyelid keeps the blob material applied at placement.")]
        public Material material;
    }

    [SerializeField, Tooltip("Inner globe whose mesh is swapped. Leave empty to use the child named 'human eyeball'.")]
    private MeshFilter sphere;

    [SerializeField, Tooltip("Sphere meshes in roll order. Index 0 is the prefab's default globe.")]
    private SphereOption[] options;

    /// <summary>Index of the sphere currently shown. 0 when no option has been applied.</summary>
    public int CurrentIndex { get; private set; }

    /// <summary>How many sphere meshes this eye can show.</summary>
    public int OptionCount => options != null ? options.Length : 0;

    /// <summary>Picks an option index. Returns 0 when none are authored.</summary>
    public int RollIndex()
    {
        if (options == null || options.Length == 0)
        {
            return 0;
        }

        return Random.Range(0, options.Length);
    }

    /// <summary>Shows the sphere at <paramref name="index"/>, clamped to the authored options.</summary>
    public void Apply(int index)
    {
        ResolveSphere();
        if (sphere == null || options == null || options.Length == 0)
        {
            CurrentIndex = 0;
            return;
        }

        index = Mathf.Clamp(index, 0, options.Length - 1);
        CurrentIndex = index;
        SphereOption option = options[index];
        if (option.mesh != null)
        {
            sphere.sharedMesh = option.mesh;
        }

        if (option.material == null)
        {
            return;
        }

        MeshRenderer renderer = sphere.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = option.material;
        }
    }

    private void ResolveSphere()
    {
        if (sphere != null)
        {
            return;
        }

        Transform named = transform.Find("human eyeball");
        if (named != null)
        {
            sphere = named.GetComponent<MeshFilter>();
        }
    }
}
