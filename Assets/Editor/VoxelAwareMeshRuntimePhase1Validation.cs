using System.Collections.Generic;
using TerrainModificationV2;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Voxel-aware Mesh Runtime Phase 1 Validation。
/// 只验证：Raw Base Mesh → Voxel Boundary Pre-cut → ChunkGeometry →
/// Final Mesh Assembly。不接入 Mining / Hidden Face / Collider 行为。
/// </summary>
public static class VoxelAwareMeshRuntimePhase1Validation
{
    private const float Epsilon = 1e-4f;

    private static int failures;
    private static int checks;

    [MenuItem("Tools/Terrain/Validate VoxelAwareMesh Runtime Phase 1")]
    public static void ValidateFromMenu()
    {
        RunAll();
        LogSummary();

        Debug.Log(
            "[VoxelAwareMeshRuntimePhase1Validation] Menu result = " +
            (failures == 0 ? "PASS" : "FAIL")
        );
    }

    public static void RunBatch()
    {
        RunAll();
        LogSummary();
        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    private static void RunAll()
    {
        failures = 0;
        checks = 0;

        CheckSingleChunk();
        CheckMultipleChunks();
        CheckFragmentVoxelOwnershipAndWelding();
        CheckAreaAndMaterialPreservation();
        CheckNegativeCoordinates();
        CheckNonIdentityTransform();
        CheckChunkBoundary();
        CheckRegenerateIdempotence();
        CheckMeshFilterSync();
        CheckFallbackRawPath();
    }

    private static void CheckSingleChunk()
    {
        GameObject terrainObject = CreateTemporaryTerrain(
            new Vector3Int(1, 1, 1),
            Vector3Int.zero,
            true,
            out DensityTerrain terrain
        );

        try
        {
            TerrainChunkView[] views =
                terrainObject.GetComponentsInChildren<TerrainChunkView>(true);

            Check(views.Length == 1, "R1: single chunk generated");

            TerrainChunkView view = views.Length == 1 ? views[0] : null;
            Check(
                view != null && view.Chunk.Geometry != null,
                "R1: single chunk has ChunkGeometry"
            );
            Check(
                view != null && view.Chunk.Geometry.Fragments.Count > 0,
                "R1: single chunk has Voxel-local fragments"
            );
            Check(
                view != null && view.Chunk.Mesh != null &&
                view.Chunk.Mesh.vertexCount > 0,
                "R1: assembled Mesh has vertices"
            );
            Check(
                view != null &&
                view.MeshFilter != null &&
                view.MeshFilter.sharedMesh == view.Chunk.Mesh,
                "R1: MeshFilter references assembled Mesh"
            );

            if (view != null && view.Chunk.Geometry != null)
            {
                float rawArea = RawMeshArea(view.Chunk.RawMesh);
                float assembledArea = RawMeshArea(view.Chunk.Mesh);
                Check(
                    Mathf.Abs(rawArea - assembledArea) <=
                    Mathf.Max(1f, rawArea) * 1e-3f,
                    "R1: assembled area matches raw area"
                );
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    private static void CheckMultipleChunks()
    {
        GameObject terrainObject = CreateTemporaryTerrain(
            new Vector3Int(2, 1, 2),
            Vector3Int.zero,
            true,
            out DensityTerrain terrain
        );

        try
        {
            TerrainChunkView[] views =
                terrainObject.GetComponentsInChildren<TerrainChunkView>(true);

            Check(views.Length == 4, "R2: 2x1x2 terrain generated 4 chunks");

            int withGeometry = 0;
            for (int i = 0; i < views.Length; i++)
            {
                if (views[i].Chunk != null && views[i].Chunk.Geometry != null)
                {
                    withGeometry++;
                }
            }

            Check(
                withGeometry == views.Length,
                "R2: every chunk owns its own ChunkGeometry"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    private static void CheckFragmentVoxelOwnershipAndWelding()
    {
        GameObject terrainObject = CreateTemporaryTerrain(
            new Vector3Int(2, 1, 2),
            Vector3Int.zero,
            true,
            out DensityTerrain terrain
        );

        try
        {
            TerrainChunkView[] views =
                terrainObject.GetComponentsInChildren<TerrainChunkView>(true);

            bool allInside = true;
            bool allWelded = true;

            for (int i = 0; i < views.Length; i++)
            {
                TerrainChunkView view = views[i];
                if (view == null || view.Chunk == null ||
                    view.Chunk.Geometry == null)
                {
                    allInside = false;
                    allWelded = false;
                    continue;
                }

                VoxelAwareMeshPrecut.ChunkGeometry geometry =
                    view.Chunk.Geometry;

                for (int f = 0; f < geometry.Fragments.Count; f++)
                {
                    VoxelAwareMeshPrecut.Fragment fragment =
                        geometry.Fragments[f];

                    for (int p = 0; p < fragment.Polygon.Count; p++)
                    {
                        Vector3 global = fragment.Polygon[p] +
                                         geometry.ChunkOrigin;
                        Vector3 boxMin = new Vector3(
                            fragment.Voxel.x * geometry.VoxelSize,
                            fragment.Voxel.y * geometry.VoxelSize,
                            fragment.Voxel.z * geometry.VoxelSize
                        );
                        Vector3 boxMax = boxMin +
                                         Vector3.one * geometry.VoxelSize;

                        if (global.x < boxMin.x - Epsilon ||
                            global.x > boxMax.x + Epsilon ||
                            global.y < boxMin.y - Epsilon ||
                            global.y > boxMax.y + Epsilon ||
                            global.z < boxMin.z - Epsilon ||
                            global.z > boxMax.z + Epsilon)
                        {
                            allInside = false;
                        }
                    }
                }

                Dictionary<string, bool> keys =
                    new Dictionary<string, bool>();
                for (int p = 0; p < geometry.Pool.Positions.Count; p++)
                {
                    string key = VoxelAwareMeshPrecut.VertexPool.MakeKey(
                        geometry.Pool.Positions[p]
                    );
                    if (keys.ContainsKey(key))
                    {
                        allWelded = false;
                    }
                    else
                    {
                        keys.Add(key, true);
                    }
                }
            }

            Check(allInside, "R3: every fragment is fully inside one Voxel");
            Check(allWelded, "R4: VertexPool has one VertexId per spatial point");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    private static void CheckAreaAndMaterialPreservation()
    {
        GameObject terrainObject = CreateTemporaryTerrain(
            new Vector3Int(2, 1, 2),
            Vector3Int.zero,
            true,
            out DensityTerrain terrain
        );

        try
        {
            TerrainChunkView[] views =
                terrainObject.GetComponentsInChildren<TerrainChunkView>(true);

            float rawArea = 0f;
            float assembledArea = 0f;

            for (int i = 0; i < views.Length; i++)
            {
                if (views[i] == null || views[i].Chunk == null) continue;
                rawArea += RawMeshArea(views[i].Chunk.RawMesh);
                assembledArea += RawMeshArea(views[i].Chunk.Mesh);
            }

            Check(
                Mathf.Abs(rawArea - assembledArea) <=
                Mathf.Max(1f, rawArea) * 1e-3f,
                "R5: aggregate area preserved after Pre-cut"
            );

            bool submeshesPreserved = true;
            for (int i = 0; i < views.Length; i++)
            {
                TerrainChunkView view = views[i];
                if (view == null || view.Chunk == null ||
                    view.Chunk.Geometry == null ||
                    view.Chunk.RawMesh == null)
                {
                    submeshesPreserved = false;
                    continue;
                }

                for (int s = 0; s < 3; s++)
                {
                    float rawSubmeshArea = SubmeshArea(
                        view.Chunk.RawMesh,
                        s
                    );
                    float geometrySubmeshArea = GeometrySubmeshArea(
                        view.Chunk.Geometry,
                        s
                    );

                    if (Mathf.Abs(rawSubmeshArea - geometrySubmeshArea) >
                        Mathf.Max(1f, rawSubmeshArea) * 1e-3f)
                    {
                        submeshesPreserved = false;
                    }
                }
            }

            Check(submeshesPreserved, "R6: material submesh assignment preserved");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    private static void CheckNegativeCoordinates()
    {
        GameObject terrainObject = CreateTemporaryTerrain(
            new Vector3Int(1, 1, 1),
            new Vector3Int(-1, 0, 0),
            true,
            out DensityTerrain terrain
        );

        try
        {
            TerrainChunkView[] views =
                terrainObject.GetComponentsInChildren<TerrainChunkView>(true);

            Check(views.Length == 1, "R7: negative-coordinate chunk generated");

            bool hasNegativeVoxel = false;
            if (views.Length == 1 && views[0].Chunk.Geometry != null)
            {
                VoxelAwareMeshPrecut.ChunkGeometry geometry =
                    views[0].Chunk.Geometry;

                for (int i = 0; i < geometry.Fragments.Count; i++)
                {
                    if (geometry.Fragments[i].Voxel.x < 0)
                    {
                        hasNegativeVoxel = true;
                    }
                }
            }

            Check(hasNegativeVoxel, "R7: negative Voxel coordinates preserved");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    private static void CheckNonIdentityTransform()
    {
        GameObject identityObject = CreateTemporaryTerrain(
            new Vector3Int(1, 1, 1),
            Vector3Int.zero,
            true,
            out DensityTerrain identityTerrain
        );

        GameObject transformedObject = CreateTemporaryTerrain(
            new Vector3Int(1, 1, 1),
            Vector3Int.zero,
            true,
            out DensityTerrain transformedTerrain
        );

        try
        {
            transformedObject.transform.position =
                new Vector3(100f, 50f, -30f);
            transformedObject.transform.rotation =
                Quaternion.Euler(0f, 90f, 0f);
            transformedObject.transform.localScale =
                new Vector3(1.5f, 1f, 2f);
            transformedTerrain.Regenerate();

            TerrainChunkView identityView =
                identityObject.GetComponentInChildren<TerrainChunkView>(true);
            TerrainChunkView transformedView =
                transformedObject.GetComponentInChildren<TerrainChunkView>(true);

            Check(
                identityView != null && transformedView != null,
                "R8: identity / transformed terrain both generated"
            );

            if (identityView != null && transformedView != null)
            {
                Vector3 identityBounds =
                    identityView.Chunk.Mesh.bounds.size;
                Vector3 transformedBounds =
                    transformedView.Chunk.Mesh.bounds.size;

                Check(
                    Vector3.Distance(identityBounds, transformedBounds) <
                    Epsilon * 100f,
                    "R8: Chunk-local Mesh bounds do not depend on Terrain Transform"
                );

                Vector3 expectedWorld =
                    transformedObject.transform.TransformPoint(
                        transformedView.Chunk.ChunkOrigin
                    );
                Check(
                    Vector3.Distance(
                        transformedView.transform.position,
                        expectedWorld) < Epsilon * 100f,
                    "R8: transformed Chunk world origin matches root Transform"
                );
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(identityObject);
            UnityEngine.Object.DestroyImmediate(transformedObject);
        }
    }

    private static void CheckChunkBoundary()
    {
        GameObject terrainObject = CreateTemporaryTerrain(
            new Vector3Int(2, 1, 1),
            Vector3Int.zero,
            true,
            out DensityTerrain terrain
        );

        try
        {
            TerrainChunkView[] views =
                terrainObject.GetComponentsInChildren<TerrainChunkView>(true);

            bool noCrossChunkFragment = true;

            for (int i = 0; i < views.Length; i++)
            {
                TerrainChunkView view = views[i];
                if (view == null || view.Chunk == null ||
                    view.Chunk.Geometry == null)
                {
                    noCrossChunkFragment = false;
                    continue;
                }

                Vector3Int minVoxel = new Vector3Int(
                    view.Chunk.Coordinate.x * view.Chunk.ChunkVoxelSize.x,
                    view.Chunk.Coordinate.y * view.Chunk.ChunkVoxelSize.y,
                    view.Chunk.Coordinate.z * view.Chunk.ChunkVoxelSize.z
                );
                Vector3Int maxVoxel = minVoxel +
                                      view.Chunk.ChunkVoxelSize;

                for (int f = 0; f < view.Chunk.Geometry.Fragments.Count; f++)
                {
                    Vector3Int voxel =
                        view.Chunk.Geometry.Fragments[f].Voxel;

                    if (voxel.x < minVoxel.x || voxel.x >= maxVoxel.x ||
                        voxel.y < minVoxel.y || voxel.y >= maxVoxel.y ||
                        voxel.z < minVoxel.z || voxel.z >= maxVoxel.z)
                    {
                        noCrossChunkFragment = false;
                    }
                }
            }

            Check(noCrossChunkFragment, "R9: no Fragment crosses Chunk boundary");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    private static void CheckRegenerateIdempotence()
    {
        GameObject terrainObject = CreateTemporaryTerrain(
            new Vector3Int(2, 1, 2),
            Vector3Int.zero,
            true,
            out DensityTerrain terrain
        );

        try
        {
            terrain.Regenerate();
            terrain.Regenerate();
            terrain.Regenerate();

            TerrainChunkView[] views =
                terrainObject.GetComponentsInChildren<TerrainChunkView>(true);

            Check(views.Length == 4, "R10: Regenerate keeps 4 Chunk GameObjects");

            bool allGeometry = true;
            for (int i = 0; i < views.Length; i++)
            {
                if (views[i].Chunk == null || views[i].Chunk.Geometry == null)
                {
                    allGeometry = false;
                }
            }

            Check(allGeometry, "R10: Regenerate does not duplicate ChunkGeometry");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    private static void CheckMeshFilterSync()
    {
        GameObject terrainObject = CreateTemporaryTerrain(
            new Vector3Int(2, 1, 2),
            Vector3Int.zero,
            true,
            out DensityTerrain terrain
        );

        try
        {
            TerrainChunkView[] views =
                terrainObject.GetComponentsInChildren<TerrainChunkView>(true);

            bool sync = views.Length > 0;
            for (int i = 0; i < views.Length; i++)
            {
                if (views[i].MeshFilter == null ||
                    views[i].MeshFilter.sharedMesh != views[i].Chunk.Mesh)
                {
                    sync = false;
                }
            }

            Check(sync, "R11: MeshFilter.sharedMesh == TerrainChunk.Mesh");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    private static void CheckFallbackRawPath()
    {
        GameObject terrainObject = CreateTemporaryTerrain(
            new Vector3Int(1, 1, 1),
            Vector3Int.zero,
            false,
            out DensityTerrain terrain
        );

        try
        {
            TerrainChunkView[] views =
                terrainObject.GetComponentsInChildren<TerrainChunkView>(true);

            Check(views.Length == 1, "R12: fallback terrain generated");

            if (views.Length == 1)
            {
                TerrainChunkView view = views[0];
                Check(
                    view.Chunk.Geometry == null,
                    "R12: fallback path does not create ChunkGeometry"
                );
                Check(
                    view.Chunk.Mesh != null && view.Chunk.Mesh.vertexCount > 0,
                    "R12: fallback Raw Mesh still works"
                );
                Check(
                    view.MeshFilter.sharedMesh == view.Chunk.Mesh,
                    "R12: fallback MeshFilter sync works"
                );
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    private static GameObject CreateTemporaryTerrain(
        Vector3Int chunkCount,
        Vector3Int chunkStart,
        bool useVoxelAware,
        out DensityTerrain terrain)
    {
        GameObject terrainObject = new GameObject(
            "VoxelAwareMeshRuntimePhase1Terrain"
        );

        terrainObject.SetActive(false);
        terrain = terrainObject.AddComponent<DensityTerrain>();
        terrainObject.transform.position = Vector3.zero;
        terrainObject.transform.rotation = Quaternion.identity;
        terrainObject.transform.localScale = Vector3.one;

        terrain.voxelSize = 1f;
        terrain.chunkVoxelSize = new Vector3Int(16, 16, 16);
        terrain.chunkCount = chunkCount;
        terrain.chunkStartCoordinate = chunkStart;
        terrain.resolution = 8;
        terrain.densityMode = DensityGenerationMode.HeightField;
        terrain.useNoise = false;
        // 非整数高度避免所有 MC triangle 恰好 coplanar with integer Voxel plane，
        // 从而同时覆盖 ordinary Voxel-local clipping 与 plane-case 分支。
        terrain.height = 7.5f;
        terrain.regenerateInEditor = false;
        terrain.useVoxelAwareMeshPrecut = useVoxelAware;

        terrain.Regenerate();
        return terrainObject;
    }

    private static float RawMeshArea(Mesh mesh)
    {
        if (mesh == null) return 0f;

        float area = 0f;
        Vector3[] vertices = mesh.vertices;

        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            int[] triangles = mesh.GetTriangles(s);
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                area += TriangleArea(
                    vertices[triangles[i]],
                    vertices[triangles[i + 1]],
                    vertices[triangles[i + 2]]
                );
            }
        }

        return area;
    }

    private static float SubmeshArea(Mesh mesh, int submesh)
    {
        if (mesh == null || submesh >= mesh.subMeshCount) return 0f;

        float area = 0f;
        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.GetTriangles(submesh);

        for (int i = 0; i + 2 < triangles.Length; i += 3)
        {
            area += TriangleArea(
                vertices[triangles[i]],
                vertices[triangles[i + 1]],
                vertices[triangles[i + 2]]
            );
        }

        return area;
    }

    private static float GeometrySubmeshArea(
        VoxelAwareMeshPrecut.ChunkGeometry geometry,
        int submesh)
    {
        if (geometry == null) return 0f;

        float area = 0f;
        for (int i = 0; i < geometry.Fragments.Count; i++)
        {
            VoxelAwareMeshPrecut.Fragment fragment =
                geometry.Fragments[i];
            if (!fragment.Active || fragment.Submesh != submesh) continue;
            area += fragment.Area;
        }

        for (int i = 0; i < geometry.FaceFragments.Count; i++)
        {
            VoxelAwareMeshPrecut.FaceFragment fragment =
                geometry.FaceFragments[i];
            if (!fragment.Active || fragment.Submesh != submesh) continue;
            area += fragment.Area;
        }

        return area;
    }

    private static float TriangleArea(Vector3 a, Vector3 b, Vector3 c)
    {
        return Vector3.Cross(b - a, c - a).magnitude * 0.5f;
    }

    private static void Check(bool condition, string message)
    {
        checks++;
        if (condition)
        {
            Debug.Log("PASS - " + message);
        }
        else
        {
            failures++;
            Debug.LogError("FAIL - " + message);
        }
    }

    private static void LogSummary()
    {
        Debug.Log(
            "[VoxelAwareMeshRuntimePhase1Validation] ===== SUMMARY =====\n" +
            "Checks = " + checks + "\n" +
            "Failures = " + failures + "\n" +
            "Result = " + (failures == 0 ? "PASS" : "FAIL")
        );
    }
}
