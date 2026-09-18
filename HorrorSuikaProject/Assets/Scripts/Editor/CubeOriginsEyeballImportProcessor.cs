using UnityEditor;
using UnityEngine;

/// <summary>
/// Bakes uniform Blender FBX node scale into mesh vertices so eyeball and eyelid transforms import at scale 1.
/// </summary>
public sealed class CubeOriginsEyeballImportProcessor : AssetPostprocessor
{
    private const float ScaleBakeEpsilon = 0.001f;
    private const float ImportedVisualScale = 0.15f;

private void OnPostprocessModel(GameObject root)
    {
        if (!IsEyeballModel(assetPath))
        {
            return;
        }

        BakeUniformScaleRecursive(root.transform);
        ApplyImportedVisualScale(root.transform);
    }

    private static bool IsEyeballModel(string path)
    {
        string normalized = path.Replace('\\', '/');
        return normalized.IndexOf("/CubeOrigins Models/", System.StringComparison.OrdinalIgnoreCase) >= 0
            && normalized.EndsWith("Eyeball.fbx", System.StringComparison.OrdinalIgnoreCase);
    }

    private static void BakeUniformScaleRecursive(Transform transform)
    {
        for (int i = 0; i < transform.childCount; i++)
        {
            BakeUniformScaleRecursive(transform.GetChild(i));
        }

        BakeUniformScale(transform);
    }

private static void ApplyImportedVisualScale(Transform root)
    {
        Vector3 visualScale = Vector3.one * ImportedVisualScale;
        ApplyImportedVisualScaleRecursive(root, visualScale, true);
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
