using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor-only bake of a <see cref="PlayfieldShapeDefinition"/> into a saved <see cref="Mesh"/> asset
/// plus a container prefab carrying one non-convex <see cref="MeshCollider"/> and one
/// <see cref="MeshRenderer"/>. Baking at edit time keeps prefab references stable and lets PhysX cook
/// the collision mesh once at import instead of on every scene load.
/// </summary>
public static class PlayfieldShapeBaker
{
    /// <summary>Folder every baked collision/render mesh is written to.</summary>
    public const string MeshFolder = "Assets/Models/Shapes";

    /// <summary>Folder every baked container prefab is written to.</summary>
    public const string PrefabFolder = "Assets/Prefabs/Shapes";

    private const string ContainerMaterialPath = "Assets/Materials/Container.mat";
    private const MeshColliderCookingOptions CookingOptions =
        MeshColliderCookingOptions.CookForFasterSimulation
        | MeshColliderCookingOptions.EnableMeshCleaning
        | MeshColliderCookingOptions.WeldColocatedVertices
        | MeshColliderCookingOptions.UseFastMidphase;

    /// <summary>Default mesh asset path for a definition.</summary>
    public static string GetDefaultMeshPath(PlayfieldShapeDefinition definition)
    {
        return $"{MeshFolder}/{definition.name}.asset";
    }

    /// <summary>Default prefab path for a definition.</summary>
    public static string GetDefaultPrefabPath(PlayfieldShapeDefinition definition)
    {
        return $"{PrefabFolder}/{definition.name}.prefab";
    }

    /// <summary>
    /// Bakes the definition. Returns the saved prefab asset, or null when the definition is invalid.
    /// Re-bakes in place when the prefab and mesh already exist so level assets keep their references.
    /// </summary>
    public static GameObject Bake(PlayfieldShapeDefinition definition, string prefabPath, string meshPath)
    {
        if (definition == null)
        {
            Debug.LogError($"{nameof(PlayfieldShapeBaker)}: no definition supplied.");
            return null;
        }

        List<string> errors = new List<string>();
        List<string> warnings = new List<string>();
        if (!definition.Validate(errors, warnings))
        {
            Debug.LogError($"{definition.name}: cannot bake. {string.Join(" ", errors)}", definition);
            return null;
        }

        Vector2[] inner = definition.BuildSampledOutline();
        if (inner.Length < PlayfieldShapeDefinition.MinimumControlPointCount)
        {
            Debug.LogError($"{definition.name}: sampled outline has fewer than {PlayfieldShapeDefinition.MinimumControlPointCount} points.", definition);
            return null;
        }

        Vector2[] outer = PlayfieldShapeGeometry.BuildOuterOutline(inner, definition.WallThickness);
        if (PlayfieldShapeGeometry.HasSelfIntersection(outer))
        {
            Debug.LogError(
                $"{definition.name}: the outer wall self-intersects. Wall thickness ({definition.WallThickness:0.###}) " +
                "exceeds the radius of curvature at a concave corner, which would emit inverted triangles.",
                definition);
            return null;
        }

        PlayfieldShapeGeometry.BuildSolid(inner, outer, definition.ExtrudeDepth, out Vector3[] vertices, out int[] triangles);

        Mesh mesh = WriteMeshAsset(meshPath, definition.name, vertices, triangles);
        GameObject prefab = WritePrefab(definition, prefabPath, mesh, inner);

        AssetDatabase.SaveAssets();
        return prefab;
    }

    private static Mesh WriteMeshAsset(string meshPath, string meshName, Vector3[] vertices, int[] triangles)
    {
        EnsureFolder(Path.GetDirectoryName(meshPath).Replace('\\', '/'));

        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        bool isNew = mesh == null;
        if (isNew)
        {
            mesh = new Mesh();
        }
        else
        {
            mesh.Clear();
        }

        mesh.name = meshName;
        mesh.indexFormat = vertices.Length > ushort.MaxValue
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mesh.Optimize();

        if (isNew)
        {
            AssetDatabase.CreateAsset(mesh, meshPath);
        }
        else
        {
            EditorUtility.SetDirty(mesh);
        }

        return mesh;
    }

    private static GameObject WritePrefab(PlayfieldShapeDefinition definition, string prefabPath, Mesh mesh, Vector2[] inner)
    {
        EnsureFolder(Path.GetDirectoryName(prefabPath).Replace('\\', '/'));

        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        GameObject root = existing != null
            ? (GameObject)PrefabUtility.InstantiatePrefab(existing)
            : new GameObject(Path.GetFileNameWithoutExtension(prefabPath));

        root.name = Path.GetFileNameWithoutExtension(prefabPath);
        root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        root.transform.localScale = Vector3.one;

        MeshFilter filter = GetOrAddComponent<MeshFilter>(root);
        filter.sharedMesh = mesh;

        MeshRenderer renderer = GetOrAddComponent<MeshRenderer>(root);
        Material containerMaterial = AssetDatabase.LoadAssetAtPath<Material>(ContainerMaterialPath);
        if (containerMaterial != null)
        {
            renderer.sharedMaterial = containerMaterial;
        }

        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        MeshCollider collider = GetOrAddComponent<MeshCollider>(root);
        collider.sharedMesh = mesh;
        collider.convex = false;
        collider.isTrigger = false;
        collider.cookingOptions = CookingOptions;

        float floorY = float.MaxValue;
        for (int i = 0; i < inner.Length; i++)
        {
            floorY = Mathf.Min(floorY, inner[i].y);
        }

        PlayfieldShape shape = GetOrAddComponent<PlayfieldShape>(root);
        shape.SetBakedData(inner, definition.DeathLineY, definition.DropY, floorY, mesh.bounds);

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);

        Debug.Log(
            $"Baked '{definition.name}': {inner.Length} outline points, " +
            $"{PlayfieldShapeGeometry.CountVertices(inner.Length)} vertices, " +
            $"{PlayfieldShapeGeometry.CountTriangles(inner.Length)} triangles -> {prefabPath}",
            saved);

        return saved;
    }

    private static T GetOrAddComponent<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        return component != null ? component : target.AddComponent<T>();
    }

    private static void EnsureFolder(string folder)
    {
        if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder))
        {
            return;
        }

        string[] parts = folder.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{current}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }

            current = next;
        }
    }
}
