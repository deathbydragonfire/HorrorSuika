using UnityEditor;
using UnityEngine;

/// <summary>
/// Bakes uniform Blender FBX node scale into mesh vertices so decoration transforms import at scale 1.
/// Eyeballs and the ear are then shrunk to their gameplay size. The mouth keeps the Scale Factor authored on the importer.
/// </summary>
public sealed class CubeOriginsEyeballImportProcessor : AssetPostprocessor
{
    private const float ScaleBakeEpsilon = 0.001f;
    private const float ImportedVisualScale = 0.15f;

    /// <summary>
    /// The Ear FBX node imports at scale 100. The scene preview uses 20, which is 20% of that node scale.
    /// Baking both leaves the mesh at that size with a transform scale of 1.
    /// </summary>
    private const float EarVisualScale = 0.2f;

    private const float MouthOuterUvMax = 0.70f;

    private void OnPostprocessModel(GameObject root)
    {
        if (IsEyeballModel(assetPath))
        {
            BakeUniformScaleRecursive(root.transform);
            ApplyImportedVisualScale(root.transform, ImportedVisualScale);
            return;
        }

        if (IsEarModel(assetPath))
        {
            BakeUniformScaleRecursive(root.transform);
            ApplyImportedVisualScale(root.transform, EarVisualScale);
            return;
        }

        if (IsMouthModel(assetPath))
        {
            BakeUniformScaleRecursive(root.transform);
            SplitMouthOuterSurface(root.transform);
        }
    }

    private static bool IsEyeballModel(string path)
    {
        string normalized = path.Replace('\\', '/');
        return normalized.IndexOf("/CubeOrigins Models/", System.StringComparison.OrdinalIgnoreCase) >= 0
            && normalized.EndsWith("Eyeball.fbx", System.StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsEarModel(string path)
    {
        string normalized = path.Replace('\\', '/');
        return normalized.IndexOf("/CubeOrigins Models/", System.StringComparison.OrdinalIgnoreCase) >= 0
            && (normalized.EndsWith("/Ear.fbx", System.StringComparison.OrdinalIgnoreCase)
                || normalized.EndsWith("/Ear.blend", System.StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsMouthModel(string path)
    {
        string normalized = path.Replace('\\', '/');
        return normalized.IndexOf("/CubeOrigins Models/", System.StringComparison.OrdinalIgnoreCase) >= 0
            && (normalized.EndsWith("/Mouth.fbx", System.StringComparison.OrdinalIgnoreCase)
                || normalized.EndsWith("/Mouth.blend", System.StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Puts the plain outer mouth on submesh 0 and the lips, teeth, and interior on submesh 1.
    /// Gameplay replaces only submesh 0 with the blob material.
    /// </summary>
    private static void SplitMouthOuterSurface(Transform root)
    {
        SkinnedMeshRenderer[] renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Mesh mesh = renderers[i].sharedMesh;
            if (mesh == null || mesh.uv == null || mesh.uv.Length != mesh.vertexCount)
            {
                continue;
            }

            int[] triangles = mesh.triangles;
            Vector2[] uvs = mesh.uv;
            var outer = new System.Collections.Generic.List<int>(triangles.Length / 2);
            var detail = new System.Collections.Generic.List<int>(triangles.Length / 2);
            for (int t = 0; t < triangles.Length; t += 3)
            {
                bool isOuter = uvs[triangles[t]].x < MouthOuterUvMax
                    && uvs[triangles[t + 1]].x < MouthOuterUvMax
                    && uvs[triangles[t + 2]].x < MouthOuterUvMax;
                var target = isOuter ? outer : detail;
                target.Add(triangles[t]);
                target.Add(triangles[t + 1]);
                target.Add(triangles[t + 2]);
            }

            mesh.subMeshCount = 2;
            mesh.SetTriangles(outer, 0, false);
            mesh.SetTriangles(detail, 1, false);

            Material[] existing = renderers[i].sharedMaterials;
            Material lipMaterial = existing != null && existing.Length > 0 ? existing[0] : null;
            renderers[i].sharedMaterials = new Material[] { lipMaterial, lipMaterial };
        }
    }

    private static void BakeUniformScaleRecursive(Transform transform)
    {
        for (int i = 0; i < transform.childCount; i++)
        {
            BakeUniformScaleRecursive(transform.GetChild(i));
        }

        BakeUniformScale(transform);
    }

    private static void ApplyImportedVisualScale(Transform root, float visualScale)
    {
        ApplyImportedVisualScaleRecursive(root, Vector3.one * visualScale, true);
    }

    private static void ApplyImportedVisualScaleRecursive(Transform transform, Vector3 visualScale, bool isRoot)
    {
        if (!isRoot)
        {
            transform.localPosition = Vector3.Scale(transform.localPosition, visualScale);
        }

        Mesh mesh = GetMesh(transform);
        if (mesh != null)
        {
            ScaleMesh(mesh, visualScale);
            SkinnedMeshRenderer skinned = transform.GetComponent<SkinnedMeshRenderer>();
            if (skinned != null)
            {
                skinned.localBounds = mesh.bounds;
            }
        }

        for (int i = 0; i < transform.childCount; i++)
        {
            ApplyImportedVisualScaleRecursive(transform.GetChild(i), visualScale, false);
        }
    }


    private static void BakeUniformScale(Transform transform)
    {
        Vector3 scale = transform.localScale;
        if (IsApproximatelyOne(scale) || !IsUniform(scale))
        {
            return;
        }

        Mesh mesh = GetMesh(transform);
        if (mesh != null)
        {
            ScaleMesh(mesh, scale);
        }

        transform.localScale = Vector3.one;
    }

    private static bool IsApproximatelyOne(Vector3 scale)
    {
        return Mathf.Abs(scale.x - 1f) <= ScaleBakeEpsilon
            && Mathf.Abs(scale.y - 1f) <= ScaleBakeEpsilon
            && Mathf.Abs(scale.z - 1f) <= ScaleBakeEpsilon;
    }

    private static bool IsUniform(Vector3 scale)
    {
        return Mathf.Abs(scale.x - scale.y) <= ScaleBakeEpsilon
            && Mathf.Abs(scale.y - scale.z) <= ScaleBakeEpsilon;
    }

    private static Mesh GetMesh(Transform transform)
    {
        MeshFilter filter = transform.GetComponent<MeshFilter>();
        if (filter != null && filter.sharedMesh != null)
        {
            return filter.sharedMesh;
        }

        SkinnedMeshRenderer skinned = transform.GetComponent<SkinnedMeshRenderer>();
        return skinned != null ? skinned.sharedMesh : null;
    }

    private static void ScaleMesh(Mesh mesh, Vector3 scale)
    {
        Vector3[] vertices = mesh.vertices;
        for (int i = 0; i < vertices.Length; i++)
        {
            vertices[i] = Vector3.Scale(vertices[i], scale);
        }

        mesh.vertices = vertices;
        if (mesh.blendShapeCount > 0)
        {
            ScaleBlendShapes(mesh, scale);
        }

        mesh.RecalculateBounds();
    }

    private static void ScaleBlendShapes(Mesh mesh, Vector3 scale)
    {
        int shapeCount = mesh.blendShapeCount;
        int vertexCount = mesh.vertexCount;
        string[] names = new string[shapeCount];
        int[] frameCounts = new int[shapeCount];
        Vector3[][][] deltaVertices = new Vector3[shapeCount][][];
        Vector3[][][] deltaNormals = new Vector3[shapeCount][][];
        Vector3[][][] deltaTangents = new Vector3[shapeCount][][];
        float[][] frameWeights = new float[shapeCount][];

        for (int shape = 0; shape < shapeCount; shape++)
        {
            names[shape] = mesh.GetBlendShapeName(shape);
            int frames = mesh.GetBlendShapeFrameCount(shape);
            frameCounts[shape] = frames;
            deltaVertices[shape] = new Vector3[frames][];
            deltaNormals[shape] = new Vector3[frames][];
            deltaTangents[shape] = new Vector3[frames][];
            frameWeights[shape] = new float[frames];
            for (int frame = 0; frame < frames; frame++)
            {
                Vector3[] vertices = new Vector3[vertexCount];
                Vector3[] normals = new Vector3[vertexCount];
                Vector3[] tangents = new Vector3[vertexCount];
                mesh.GetBlendShapeFrameVertices(shape, frame, vertices, normals, tangents);
                for (int i = 0; i < vertices.Length; i++)
                {
                    vertices[i] = Vector3.Scale(vertices[i], scale);
                }

                deltaVertices[shape][frame] = vertices;
                deltaNormals[shape][frame] = normals;
                deltaTangents[shape][frame] = tangents;
                frameWeights[shape][frame] = mesh.GetBlendShapeFrameWeight(shape, frame);
            }
        }

        mesh.ClearBlendShapes();
        for (int shape = 0; shape < shapeCount; shape++)
        {
            for (int frame = 0; frame < frameCounts[shape]; frame++)
            {
                mesh.AddBlendShapeFrame(
                    names[shape],
                    frameWeights[shape][frame],
                    deltaVertices[shape][frame],
                    deltaNormals[shape][frame],
                    deltaTangents[shape][frame]);
            }
        }
    }
}
