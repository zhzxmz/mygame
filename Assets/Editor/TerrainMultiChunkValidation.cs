using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Chunk Specification V2 多 Chunk 自动验证。
/// 临时把当前地形切成 2 × 1 × 2 个 Chunk，总范围保持 83 × 20.3 × 55.2。
/// 验证坐标、Density、Mesh、Transform、Dirty 在多 Chunk 情况下成立。
/// 该脚本只创建临时测试对象，不会保存或修改 SampleScene。
/// </summary>
public static class TerrainMultiChunkValidation
{
    // 当前场景的临时测试配置，不是最终 Chunk 设计。
    private const float VoxelSize = 0.1f;
    private const int DensityResolution = 30;
    private const string SampleScenePath = "Assets/Scenes/SampleScene.unity";
    private const string BatchHostScenePath = "Assets/Scenes/Validation_HostTemp.unity";

    // 独立 Chunk 的 Marching Cubes 可能产生极小的边界顶点误差；
    // 精确水密缝合属于后续 Mesh 优化，本轮只验证边界连续性在可接受容差内。
    private const float BoundaryVertexTolerance = 0.01f;

    private static readonly Vector3Int ChunkCount = new Vector3Int(2, 1, 2);
    private static readonly Vector3Int ChunkVoxelSize = new Vector3Int(415, 203, 276);
    private static readonly Vector3 ChunkWorldSize = new Vector3(41.5f, 20.3f, 27.6f);

    private static readonly Vector3Int[] ExpectedChunkCoordinates =
    {
        new Vector3Int(0, 0, 0),
        new Vector3Int(1, 0, 0),
        new Vector3Int(0, 0, 1),
        new Vector3Int(1, 0, 1)
    };

    private static int failures;
    private static readonly List<string> summary = new List<string>();

    // ---------------------------------------------------------------------
    // Menu / Batch entry points
    // ---------------------------------------------------------------------

    [MenuItem("Tools/Terrain/Apply Temporary Multi-Chunk Split")]
    public static void ApplyTemporarySplitToActiveScene()
    {
        if (Application.isPlaying)
        {
            Debug.LogWarning(
                "[TerrainMultiChunkValidation] Apply Temporary Multi-Chunk Split is an editor-only action."
            );
            return;
        }

        DensityTerrain terrain = UnityEngine.Object.FindObjectOfType<DensityTerrain>();
        if (terrain == null)
        {
            Debug.LogError(
                "[TerrainMultiChunkValidation] 当前场景中没有找到 DensityTerrain。"
            );
            return;
        }

        Undo.RecordObject(terrain, "Apply Temporary Multi-Chunk Split");

        terrain.voxelSize = VoxelSize;
        terrain.chunkVoxelSize = ChunkVoxelSize;
        terrain.chunkCount = ChunkCount;
        terrain.chunkStartCoordinate = Vector3Int.zero;
        terrain.Regenerate();

        EditorUtility.SetDirty(terrain);
        EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);

        Debug.Log(
            "[TerrainMultiChunkValidation] 已应用临时多 Chunk 配置: " +
            $"ChunkCount={ChunkCount}, ChunkVoxelSize={ChunkVoxelSize}, " +
            $"ChunkWorldSize={ChunkWorldSize}, Total={ChunkWorldSize.x * ChunkCount.x}×" +
            $"{ChunkWorldSize.y * ChunkCount.y}×{ChunkWorldSize.z * ChunkCount.z}"
        );
    }

    [MenuItem("Tools/Terrain/Validate Multi-Chunk Terrain")]
    public static void ValidateFromMenu()
    {
        if (Application.isPlaying)
        {
            Debug.LogWarning(
                "[TerrainMultiChunkValidation] 请先退出 Play Mode，再运行多 Chunk 验证。"
            );
            return;
        }

        // 菜单验证不打开 / 修改 SampleScene；场景配置验证在 batchmode 中包含。
        bool success = ValidateAll(false);
        Debug.Log(
            "[TerrainMultiChunkValidation] Menu validation result = " +
            (success ? "PASS" : "FAIL")
        );
    }

    /// <summary>供 Unity batchmode -executeMethod 调用。</summary>
    public static void RunBatch()
    {
        PrepareBatchHostScene();
        bool success = ValidateAll(true);
        CleanupBatchHostScene();
        EditorApplication.Exit(success ? 0 : 1);
    }

    [MenuItem("Tools/Terrain/Validate Mining V1")]
    public static void ValidateMiningFromMenu()
    {
        if (Application.isPlaying)
        {
            Debug.LogWarning(
                "[TerrainMultiChunkValidation] 请先退出 Play Mode，再运行 Mining V1 验证。"
            );
            return;
        }

        failures = 0;
        summary.Clear();

        try
        {
            ValidateMiningV1();
        }
        catch (Exception exception)
        {
            Check(false, "Mining validation exception: " + exception);
        }

        LogSummary("MINING");
        Debug.Log(
            "[TerrainMultiChunkValidation] Mining validation result = " +
            (failures == 0 ? "PASS" : "FAIL")
        );
    }

    /// <summary>只运行 Mining V1 验证的 batchmode 入口。</summary>
    public static void RunMiningBatch()
    {
        failures = 0;
        summary.Clear();

        try
        {
            ValidateMiningV1();
        }
        catch (Exception exception)
        {
            Check(false, "Mining validation exception: " + exception);
        }

        LogSummary("MINING");
        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    [MenuItem("Tools/Terrain/Validate Chunk Lifecycle")]
    public static void ValidateChunkLifecycleFromMenu()
    {
        if (Application.isPlaying)
        {
            Debug.LogWarning(
                "[TerrainMultiChunkValidation] 请先退出 Play Mode，再运行 Chunk Lifecycle 验证。"
            );
            return;
        }

        failures = 0;
        summary.Clear();

        try
        {
            ValidateChunkLifecycleV1();
        }
        catch (Exception exception)
        {
            Check(false, "Chunk lifecycle validation exception: " + exception);
        }

        LogSummary("CHUNK LIFECYCLE");
        Debug.Log(
            "[TerrainMultiChunkValidation] Chunk lifecycle validation result = " +
            (failures == 0 ? "PASS" : "FAIL")
        );
    }

    /// <summary>只运行 Chunk GameObject / Mesh 生命周期验证的 batchmode 入口。</summary>
    public static void RunLifecycleBatch()
    {
        PrepareBatchHostScene();

        failures = 0;
        summary.Clear();

        try
        {
            ValidateChunkLifecycleV1();
        }
        catch (Exception exception)
        {
            Check(false, "Chunk lifecycle validation exception: " + exception);
        }

        LogSummary("CHUNK LIFECYCLE");
        CleanupBatchHostScene();
        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    [MenuItem("Tools/Terrain/Validate Player Mining V1")]
    public static void ValidatePlayerMiningFromMenu()
    {
        if (Application.isPlaying)
        {
            Debug.LogWarning(
                "[TerrainMultiChunkValidation] 请先退出 Play Mode，再运行 Player Mining V1 验证。"
            );
            return;
        }

        failures = 0;
        summary.Clear();

        try
        {
            ValidatePlayerMiningV1();
        }
        catch (Exception exception)
        {
            Check(false, "Player mining validation exception: " + exception);
        }

        LogSummary("PLAYER MINING");
        Debug.Log(
            "[TerrainMultiChunkValidation] Player Mining validation result = " +
            (failures == 0 ? "PASS" : "FAIL")
        );
    }

    /// <summary>只运行 Player Mining / Targeting V1 验证的 batchmode 入口。</summary>
    public static void RunPlayerMiningBatch()
    {
        failures = 0;
        summary.Clear();

        try
        {
            ValidatePlayerMiningV1();
        }
        catch (Exception exception)
        {
            Check(false, "Player mining validation exception: " + exception);
        }

        LogSummary("PLAYER MINING");
        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    private static void LogSummary(string title)
    {
        Debug.Log(
            "[TerrainMultiChunkValidation] ===== " + title + " SUMMARY =====\n" +
            string.Join("\n", summary) +
            "\nFailures = " + failures +
            "\nResult = " + (failures == 0 ? "PASS" : "FAIL")
        );
    }

    private static void PrepareBatchHostScene()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (!activeScene.IsValid() || !string.IsNullOrEmpty(activeScene.path))
        {
            return;
        }

        if (System.IO.File.Exists(BatchHostScenePath))
        {
            AssetDatabase.DeleteAsset(BatchHostScenePath);
        }

        EditorSceneManager.SaveScene(activeScene, BatchHostScenePath);
    }

    private static void CleanupBatchHostScene()
    {
        if (!System.IO.File.Exists(BatchHostScenePath))
        {
            return;
        }

        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.IsValid() &&
            !string.IsNullOrEmpty(activeScene.path) &&
            activeScene.isDirty)
        {
            EditorSceneManager.SaveScene(activeScene);
        }

        try
        {
            EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single
            );
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                "[TerrainMultiChunkValidation] Failed to reset batch host scene: " +
                exception.Message
            );
        }

        if (System.IO.File.Exists(BatchHostScenePath))
        {
            AssetDatabase.DeleteAsset(BatchHostScenePath);
        }
    }

    // ---------------------------------------------------------------------
    // Validation entry
    // ---------------------------------------------------------------------

    public static bool ValidateAll(bool includeSceneConfiguration)
    {
        failures = 0;
        summary.Clear();

        try
        {
            ValidateCoordinates();
            ValidateAbsoluteChunkStartCoordinate();

            ValidateDensityForMode(DensityGenerationMode.HeightField);
            ValidateDensityForMode(DensityGenerationMode.ThreeDimensional);
            ValidateDensityForMode(DensityGenerationMode.SphereCavity);

            ValidateMeshForMode(DensityGenerationMode.HeightField);
            ValidateMeshForMode(DensityGenerationMode.ThreeDimensional);
            ValidateMeshForMode(DensityGenerationMode.SphereCavity);

            ValidateTransform();
            ValidateDirtyRebuild();
            ValidateMiningV1();
            ValidateChunkLifecycleV1();
            ValidatePlayerMiningV1();

            if (includeSceneConfiguration)
            {
                ValidateSceneConfiguration();
            }
        }
        catch (Exception exception)
        {
            Check(false, "Validation exception: " + exception);
        }

        Debug.Log(
            "[TerrainMultiChunkValidation] ===== SUMMARY =====\n" +
            string.Join("\n", summary) +
            "\nFailures = " + failures +
            "\nResult = " + (failures == 0 ? "PASS" : "FAIL")
        );

        return failures == 0;
    }

    // ---------------------------------------------------------------------
    // 1. Coordinates
    // ---------------------------------------------------------------------

    private static void ValidateCoordinates()
    {
        DensityTerrain terrain = CreateTerrain(
            "Validation_Coordinates",
            DensityGenerationMode.HeightField
        );

        try
        {
            for (int i = 0; i < ExpectedChunkCoordinates.Length; i++)
            {
                Vector3Int coordinate = ExpectedChunkCoordinates[i];
                Vector3 localCenter = GetChunkLocalCenter(coordinate);
                Vector3 worldCenter = terrain.transform.TransformPoint(localCenter);

                Vector3Int expectedVoxel = ChunkSpace.LocalToVoxel(localCenter, VoxelSize);
                Vector3Int expectedLocalVoxel = ChunkSpace.VoxelToLocal(
                    expectedVoxel,
                    ChunkVoxelSize
                );

                Check(
                    terrain.WorldToVoxel(worldCenter) == expectedVoxel,
                    $"Coordinate {coordinate}: WorldToVoxel matches"
                );
                Check(
                    terrain.WorldToChunkCoordinate(worldCenter) == coordinate,
                    $"Coordinate {coordinate}: WorldToChunkCoordinate matches"
                );
                Check(
                    terrain.WorldToLocalVoxel(worldCenter) == expectedLocalVoxel,
                    $"Coordinate {coordinate}: WorldToLocalVoxel matches"
                );

                bool found = terrain.TryGetChunk(worldCenter, out TerrainChunk chunk);
                Check(
                    found && chunk != null && chunk.Coordinate == coordinate,
                    $"Coordinate {coordinate}: TryGetChunk finds containing chunk"
                );

                if (chunk == null) continue;

                Check(
                    chunk.ChunkVoxelSize == ChunkVoxelSize,
                    $"Coordinate {coordinate}: ChunkVoxelSize matches"
                );
                Check(
                    Vector3.Distance(chunk.ChunkWorldSize, ChunkWorldSize) < 0.001f,
                    $"Coordinate {coordinate}: ChunkWorldSize matches"
                );
                Check(
                    chunk.ContainsWorldPosition(worldCenter, terrain.transform),
                    $"Coordinate {coordinate}: Chunk contains its center"
                );
            }

            // 共享边界属于边界之后的 Chunk：local x = chunkWorldSize.x 应映射到 chunk (1,0,0)。
            Vector3 boundaryWorld = terrain.transform.TransformPoint(
                new Vector3(ChunkWorldSize.x, 1f, 1f)
            );
            Check(
                terrain.WorldToChunkCoordinate(boundaryWorld) == new Vector3Int(1, 0, 0),
                "Coordinate boundary X: boundary belongs to positive-side chunk"
            );
            Check(
                terrain.WorldToLocalVoxel(boundaryWorld).x == 0,
                "Coordinate boundary X: boundary maps to local voxel x = 0"
            );

            Vector3 outsideWorld = terrain.transform.TransformPoint(
                new Vector3(ChunkWorldSize.x * ChunkCount.x * 2f, 1f, 1f)
            );
            Check(
                !terrain.TryGetChunk(outsideWorld, out _),
                "Coordinate outside generated range: TryGetChunk returns false"
            );
        }
        finally
        {
            DestroyTerrain(terrain);
        }
    }

    private static void ValidateAbsoluteChunkStartCoordinate()
    {
        Vector3Int start = new Vector3Int(5, 0, 7);
        DensityTerrain terrain = CreateTerrain(
            "Validation_AbsoluteChunkStart",
            DensityGenerationMode.HeightField,
            start
        );

        try
        {
            Vector3Int[] expectedCoordinates =
            {
                start,
                start + new Vector3Int(1, 0, 0),
                start + new Vector3Int(0, 0, 1),
                start + new Vector3Int(1, 0, 1)
            };

            for (int i = 0; i < expectedCoordinates.Length; i++)
            {
                Vector3Int coordinate = expectedCoordinates[i];
                Vector3 localCenter = GetChunkLocalCenter(coordinate);
                Vector3 worldCenter = terrain.transform.TransformPoint(localCenter);

                Check(
                    terrain.WorldToChunkCoordinate(worldCenter) == coordinate,
                    $"Absolute start {start}: WorldToChunkCoordinate {coordinate} matches"
                );
                Check(
                    terrain.TryGetChunk(worldCenter, out TerrainChunk chunk) &&
                    chunk != null &&
                    chunk.Coordinate == coordinate,
                    $"Absolute start {start}: TryGetChunk {coordinate} finds chunk"
                );
            }

            Check(
                !terrain.TryGetChunk(start + new Vector3Int(-1, 0, 0), out _),
                $"Absolute start {start}: previous chunk is not generated"
            );
        }
        finally
        {
            DestroyTerrain(terrain);
        }
    }

    // ---------------------------------------------------------------------
    // 2. Density
    // ---------------------------------------------------------------------
    private static void ValidateDensityForMode(DensityGenerationMode mode)
    {
        DensityTerrain terrain = CreateTerrain(
            "Validation_Density_" + mode,
            mode
        );

        try
        {
            long pairs = 0;
            long positionMismatches = 0;
            long densityMismatches = 0;

            CompareDensityBoundaryX(
                terrain,
                new Vector3Int(0, 0, 0),
                new Vector3Int(1, 0, 0),
                ref pairs,
                ref positionMismatches,
                ref densityMismatches
            );
            CompareDensityBoundaryX(
                terrain,
                new Vector3Int(0, 0, 1),
                new Vector3Int(1, 0, 1),
                ref pairs,
                ref positionMismatches,
                ref densityMismatches
            );
            CompareDensityBoundaryZ(
                terrain,
                new Vector3Int(0, 0, 0),
                new Vector3Int(0, 0, 1),
                ref pairs,
                ref positionMismatches,
                ref densityMismatches
            );
            CompareDensityBoundaryZ(
                terrain,
                new Vector3Int(1, 0, 0),
                new Vector3Int(1, 0, 1),
                ref pairs,
                ref positionMismatches,
                ref densityMismatches
            );

            Check(pairs > 0, $"{mode}: density boundary samples checked");
            Check(
                positionMismatches == 0,
                $"{mode}: density boundary positions match"
            );
            Check(
                densityMismatches == 0,
                $"{mode}: density boundary values match"
            );

            if (mode == DensityGenerationMode.SphereCavity)
            {
                ValidateSphereCavityIsAbsolute(terrain);
            }
        }
        finally
        {
            DestroyTerrain(terrain);
        }
    }

    private static void ValidateSphereCavityIsAbsolute(DensityTerrain terrain)
    {
        if (!terrain.TryGetChunk(new Vector3Int(0, 0, 0), out TerrainChunk chunk0) ||
            !terrain.TryGetChunk(new Vector3Int(1, 0, 0), out TerrainChunk chunk1))
        {
            Check(false, "SphereCavity: chunk 0 / chunk 1 exist");
            return;
        }

        float densityAtCavityCenter = SampleDensityAtLocal(
            chunk0.Density,
            new Vector3(5f, 5f, 5f)
        );
        float densityAtChunk1LocalCenter = SampleDensityAtLocal(
            chunk1.Density,
            new Vector3(5f, 5f, 5f)
        );

        Check(
            densityAtCavityCenter < 0f,
            "SphereCavity multi-chunk: absolute cavity center is air"
        );
        Check(
            densityAtChunk1LocalCenter > 0f,
            "SphereCavity multi-chunk: cavity is not repeated in chunk 1"
        );
    }

    private static void CompareDensityBoundaryX(
        DensityTerrain terrain,
        Vector3Int coordinateA,
        Vector3Int coordinateB,
        ref long pairs,
        ref long positionMismatches,
        ref long densityMismatches)
    {
        if (!terrain.TryGetChunk(coordinateA, out TerrainChunk a) ||
            !terrain.TryGetChunk(coordinateB, out TerrainChunk b))
        {
            Check(false, $"Density X boundary {coordinateA}-{coordinateB}: chunks exist");
            return;
        }

        int ax = a.Density.SampleCountX - 1;

        for (int y = 0; y < a.Density.SampleCountY; y++)
        {
            for (int z = 0; z < a.Density.SampleCountZ; z++)
            {
                pairs++;

                Vector3 localA = a.Density.GetLocalPosition(ax, y, z);
                Vector3 localB = b.Density.GetLocalPosition(0, y, z);

                if (Vector3.Distance(localA, localB) > 0.001f)
                {
                    positionMismatches++;
                }

                if (Mathf.Abs(a.Density[ax, y, z] - b.Density[0, y, z]) > 0.0001f)
                {
                    densityMismatches++;
                }
            }
        }
    }

    private static void CompareDensityBoundaryZ(
        DensityTerrain terrain,
        Vector3Int coordinateA,
        Vector3Int coordinateB,
        ref long pairs,
        ref long positionMismatches,
        ref long densityMismatches)
    {
        if (!terrain.TryGetChunk(coordinateA, out TerrainChunk a) ||
            !terrain.TryGetChunk(coordinateB, out TerrainChunk b))
        {
            Check(false, $"Density Z boundary {coordinateA}-{coordinateB}: chunks exist");
            return;
        }

        int az = a.Density.SampleCountZ - 1;

        for (int x = 0; x < a.Density.SampleCountX; x++)
        {
            for (int y = 0; y < a.Density.SampleCountY; y++)
            {
                pairs++;

                Vector3 localA = a.Density.GetLocalPosition(x, y, az);
                Vector3 localB = b.Density.GetLocalPosition(x, y, 0);

                if (Vector3.Distance(localA, localB) > 0.001f)
                {
                    positionMismatches++;
                }

                if (Mathf.Abs(a.Density[x, y, az] - b.Density[x, y, 0]) > 0.0001f)
                {
                    densityMismatches++;
                }
            }
        }
    }

    // ---------------------------------------------------------------------
    // 3. Mesh
    // ---------------------------------------------------------------------

    private static void ValidateMeshForMode(DensityGenerationMode mode)
    {
        DensityTerrain terrain = CreateTerrain(
            "Validation_Mesh_" + mode,
            mode
        );

        try
        {
            long boundaryVertices = 0;

            for (int i = 0; i < ExpectedChunkCoordinates.Length; i++)
            {
                Vector3Int coordinate = ExpectedChunkCoordinates[i];

                if (!terrain.TryGetChunk(coordinate, out TerrainChunk chunk) ||
                    chunk == null)
                {
                    Check(false, $"{mode} mesh {coordinate}: chunk exists");
                    continue;
                }

                Check(
                    chunk.Mesh != null && chunk.Mesh.vertexCount > 0,
                    $"{mode} mesh {coordinate}: mesh has vertices"
                );

                ValidateChunkLocalVertices(chunk, $"{mode} mesh {coordinate}");

                Transform chunkTransform = FindChunkTransform(terrain, chunk);
                Check(
                    chunkTransform != null,
                    $"{mode} mesh {coordinate}: chunk GameObject exists"
                );

                if (chunkTransform != null)
                {
                    Check(
                        Vector3.Distance(chunkTransform.localPosition, chunk.ChunkOrigin) < 0.001f,
                        $"{mode} mesh {coordinate}: GameObject localPosition = ChunkOrigin"
                    );
                }
            }

            CompareMeshBoundaryX(
                terrain,
                new Vector3Int(0, 0, 0),
                new Vector3Int(1, 0, 0),
                mode,
                ref boundaryVertices
            );
            CompareMeshBoundaryX(
                terrain,
                new Vector3Int(0, 0, 1),
                new Vector3Int(1, 0, 1),
                mode,
                ref boundaryVertices
            );
            CompareMeshBoundaryZ(
                terrain,
                new Vector3Int(0, 0, 0),
                new Vector3Int(0, 0, 1),
                mode,
                ref boundaryVertices
            );
            CompareMeshBoundaryZ(
                terrain,
                new Vector3Int(1, 0, 0),
                new Vector3Int(1, 0, 1),
                mode,
                ref boundaryVertices
            );

            Check(
                boundaryVertices > 0,
                $"{mode}: boundary mesh vertices exist"
            );
        }
        finally
        {
            DestroyTerrain(terrain);
        }
    }

    private static void CompareMeshBoundaryX(
        DensityTerrain terrain,
        Vector3Int coordinateA,
        Vector3Int coordinateB,
        DensityGenerationMode mode,
        ref long boundaryVertices)
    {
        if (!terrain.TryGetChunk(coordinateA, out TerrainChunk a) ||
            !terrain.TryGetChunk(coordinateB, out TerrainChunk b))
        {
            Check(false, $"{mode} mesh X boundary {coordinateA}-{coordinateB}: chunks exist");
            return;
        }

        HashSet<Vector3Int> keysA = CollectBoundaryVertexKeys(
            a,
            0,
            a.ChunkWorldSize.x
        );
        HashSet<Vector3Int> keysB = CollectBoundaryVertexKeys(
            b,
            0,
            0f
        );

        boundaryVertices += keysA.Count + keysB.Count;

        Check(
            keysA.Count > 0 && keysB.Count > 0,
            $"{mode} mesh X boundary {coordinateA}-{coordinateB}: vertices on both sides " +
            $"(A={keysA.Count}, B={keysB.Count})"
        );
        ValidateBoundaryVertexSets(
            $"{mode} mesh X boundary {coordinateA}-{coordinateB}",
            keysA,
            keysB
        );
    }

    private static void CompareMeshBoundaryZ(
        DensityTerrain terrain,
        Vector3Int coordinateA,
        Vector3Int coordinateB,
        DensityGenerationMode mode,
        ref long boundaryVertices)
    {
        if (!terrain.TryGetChunk(coordinateA, out TerrainChunk a) ||
            !terrain.TryGetChunk(coordinateB, out TerrainChunk b))
        {
            Check(false, $"{mode} mesh Z boundary {coordinateA}-{coordinateB}: chunks exist");
            return;
        }

        HashSet<Vector3Int> keysA = CollectBoundaryVertexKeys(
            a,
            2,
            a.ChunkWorldSize.z
        );
        HashSet<Vector3Int> keysB = CollectBoundaryVertexKeys(
            b,
            2,
            0f
        );

        boundaryVertices += keysA.Count + keysB.Count;

        Check(
            keysA.Count > 0 && keysB.Count > 0,
            $"{mode} mesh Z boundary {coordinateA}-{coordinateB}: vertices on both sides " +
            $"(A={keysA.Count}, B={keysB.Count})"
        );
        ValidateBoundaryVertexSets(
            $"{mode} mesh Z boundary {coordinateA}-{coordinateB}",
            keysA,
            keysB
        );
    }

    private static void ValidateBoundaryVertexSets(
        string label,
        HashSet<Vector3Int> keysA,
        HashSet<Vector3Int> keysB)
    {
        if (keysA.Count == 0 || keysB.Count == 0)
        {
            Check(false, label + ": both sides have boundary vertices");
            return;
        }

        float maxAToB = ComputeMaxNearestDistance(keysA, keysB);
        float maxBToA = ComputeMaxNearestDistance(keysB, keysA);
        float maxDeviation = Mathf.Max(maxAToB, maxBToA);

        Check(
            maxDeviation <= BoundaryVertexTolerance,
            $"{label}: boundary vertices continuous " +
            $"(maxDeviation={maxDeviation:F4}, A={keysA.Count}, B={keysB.Count})"
        );
    }

    private static float ComputeMaxNearestDistance(
        HashSet<Vector3Int> source,
        HashSet<Vector3Int> target)
    {
        const float quantizeScale = 1000f;
        float maxDistanceSquared = 0f;

        foreach (Vector3Int key in source)
        {
            float nearestDistanceSquared = float.MaxValue;

            foreach (Vector3Int candidate in target)
            {
                int dx = candidate.x - key.x;
                int dy = candidate.y - key.y;
                int dz = candidate.z - key.z;
                float distanceSquared = dx * dx + dy * dy + dz * dz;

                if (distanceSquared < nearestDistanceSquared)
                {
                    nearestDistanceSquared = distanceSquared;
                }
            }

            if (nearestDistanceSquared > maxDistanceSquared)
            {
                maxDistanceSquared = nearestDistanceSquared;
            }
        }

        return Mathf.Sqrt(maxDistanceSquared) / quantizeScale;
    }

    private static HashSet<Vector3Int> CollectBoundaryVertexKeys(
        TerrainChunk chunk,
        int axis,
        float boundaryLocalCoordinate)
    {
        HashSet<Vector3Int> keys = new HashSet<Vector3Int>();
        if (chunk == null || chunk.Mesh == null) return keys;

        const float positionEpsilon = 0.001f;
        const float quantizeScale = 1000f;

        Vector3[] vertices = chunk.Mesh.vertices;

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 vertex = vertices[i];
            float axisValue = axis == 0
                ? vertex.x
                : axis == 1
                    ? vertex.y
                    : vertex.z;

            if (Mathf.Abs(axisValue - boundaryLocalCoordinate) > positionEpsilon)
            {
                continue;
            }

            Vector3 terrainLocal = chunk.ChunkOrigin + vertex;

            keys.Add(new Vector3Int(
                Mathf.RoundToInt(terrainLocal.x * quantizeScale),
                Mathf.RoundToInt(terrainLocal.y * quantizeScale),
                Mathf.RoundToInt(terrainLocal.z * quantizeScale)
            ));
        }

        return keys;
    }

    private static void ValidateChunkLocalVertices(
        TerrainChunk chunk,
        string label)
    {
        if (chunk == null || chunk.Mesh == null)
        {
            Check(false, label + ": mesh exists for local vertex check");
            return;
        }

        const float epsilon = 0.001f;
        Vector3 size = chunk.ChunkWorldSize;
        Vector3[] vertices = chunk.Mesh.vertices;
        bool allInside = true;

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 vertex = vertices[i];

            if (vertex.x < -epsilon || vertex.y < -epsilon || vertex.z < -epsilon ||
                vertex.x > size.x + epsilon ||
                vertex.y > size.y + epsilon ||
                vertex.z > size.z + epsilon)
            {
                allInside = false;
                break;
            }
        }

        Check(allInside, label + ": vertices are Chunk-local");
    }

    // ---------------------------------------------------------------------
    // 4. Transform
    // ---------------------------------------------------------------------

    private static void ValidateTransform()
    {
        DensityTerrain terrain = CreateTerrain(
            "Validation_Transform",
            DensityGenerationMode.HeightField
        );

        try
        {
            terrain.transform.position = new Vector3(10f, 5f, -3f);
            terrain.transform.rotation = Quaternion.Euler(0f, 35f, 0f);
            terrain.transform.localScale = new Vector3(1.5f, 2f, 0.75f);
            terrain.Regenerate();

            for (int i = 0; i < ExpectedChunkCoordinates.Length; i++)
            {
                Vector3Int coordinate = ExpectedChunkCoordinates[i];
                Vector3 localCenter = GetChunkLocalCenter(coordinate);
                Vector3 worldCenter = terrain.transform.TransformPoint(localCenter);

                bool found = terrain.TryGetChunk(worldCenter, out TerrainChunk chunk);
                Check(
                    found && chunk != null && chunk.Coordinate == coordinate,
                    $"Transform {coordinate}: world → containing chunk"
                );

                if (chunk == null) continue;

                Transform chunkTransform = FindChunkTransform(terrain, chunk);
                if (chunkTransform == null)
                {
                    Check(false, $"Transform {coordinate}: chunk GameObject exists");
                    continue;
                }

                Vector3 expectedWorldOrigin = terrain.transform.TransformPoint(
                    chunk.ChunkOrigin
                );

                Check(
                    Vector3.Distance(chunkTransform.localPosition, chunk.ChunkOrigin) < 0.001f,
                    $"Transform {coordinate}: localPosition = ChunkOrigin"
                );
                Check(
                    Vector3.Distance(chunkTransform.position, expectedWorldOrigin) < 0.001f,
                    $"Transform {coordinate}: world origin follows root Transform"
                );

                MeshRenderer renderer = chunkTransform.GetComponent<MeshRenderer>();
                Check(
                    renderer != null,
                    $"Transform {coordinate}: MeshRenderer exists"
                );

                if (renderer != null && chunk.Mesh != null)
                {
                    Vector3 expectedBoundsCenter = chunkTransform.TransformPoint(
                        chunk.Mesh.bounds.center
                    );
                    Check(
                        Vector3.Distance(renderer.bounds.center, expectedBoundsCenter) < 0.01f,
                        $"Transform {coordinate}: renderer bounds follow chunk transform"
                    );
                }

                ValidateChunkLocalVertices(
                    chunk,
                    $"Transform {coordinate}"
                );
            }

            CheckChunkViews(
                terrain,
                ExpectedChunkCoordinates,
                "Transform"
            );
        }
        finally
        {
            DestroyTerrain(terrain);
        }
    }

    // ---------------------------------------------------------------------
    // 5. Dirty / Rebuild
    // ---------------------------------------------------------------------

    private static void ValidateDirtyRebuild()
    {
        DensityTerrain terrain = CreateTerrain(
            "Validation_DirtyRebuild",
            DensityGenerationMode.HeightField
        );

        try
        {
            Dictionary<Vector3Int, TerrainChunk> chunks =
                new Dictionary<Vector3Int, TerrainChunk>();
            Dictionary<Vector3Int, int> meshIdsBefore =
                new Dictionary<Vector3Int, int>();
            Dictionary<Vector3Int, DensityField> densityRefs =
                new Dictionary<Vector3Int, DensityField>();
            Dictionary<Vector3Int, MaterialField> materialRefs =
                new Dictionary<Vector3Int, MaterialField>();

            for (int i = 0; i < ExpectedChunkCoordinates.Length; i++)
            {
                Vector3Int coordinate = ExpectedChunkCoordinates[i];

                if (!terrain.TryGetChunk(coordinate, out TerrainChunk chunk) ||
                    chunk == null ||
                    chunk.Mesh == null)
                {
                    Check(false, $"DirtyRebuild {coordinate}: chunk exists");
                    continue;
                }

                chunks[coordinate] = chunk;
                meshIdsBefore[coordinate] = chunk.Mesh.GetInstanceID();
                densityRefs[coordinate] = chunk.Density;
                materialRefs[coordinate] = chunk.Material;
            }

            Vector3Int[] dirtyCoordinates =
            {
                new Vector3Int(0, 0, 0),
                new Vector3Int(1, 0, 1)
            };

            Dictionary<Vector3Int, int> sampleX = new Dictionary<Vector3Int, int>();
            Dictionary<Vector3Int, int> sampleY = new Dictionary<Vector3Int, int>();
            Dictionary<Vector3Int, int> sampleZ = new Dictionary<Vector3Int, int>();

            for (int i = 0; i < dirtyCoordinates.Length; i++)
            {
                Vector3Int coordinate = dirtyCoordinates[i];
                if (!chunks.TryGetValue(coordinate, out TerrainChunk chunk))
                {
                    Check(false, $"DirtyRebuild {coordinate}: dirty chunk exists");
                    continue;
                }

                if (!TryFindSolidSample(
                    chunk.Density,
                    out int sx,
                    out int sy,
                    out int sz))
                {
                    Check(false, $"DirtyRebuild {coordinate}: found solid density sample");
                    continue;
                }

                chunk.Density[sx, sy, sz] = -10f;
                chunk.Material[sx, sy, sz] = MaterialType.Air;
                chunk.MarkDirty();

                sampleX[coordinate] = sx;
                sampleY[coordinate] = sy;
                sampleZ[coordinate] = sz;

                Check(
                    chunk.IsDirty,
                    $"DirtyRebuild {coordinate}: marked dirty"
                );
            }

            terrain.RebuildDirtyChunks();

            for (int i = 0; i < ExpectedChunkCoordinates.Length; i++)
            {
                Vector3Int coordinate = ExpectedChunkCoordinates[i];
                if (!chunks.TryGetValue(coordinate, out TerrainChunk chunk))
                {
                    continue;
                }

                bool wasDirty = Array.IndexOf(dirtyCoordinates, coordinate) >= 0;

                Check(
                    !chunk.IsDirty,
                    $"DirtyRebuild {coordinate}: clean after rebuild"
                );
                Check(
                    ReferenceEquals(chunk.Density, densityRefs[coordinate]),
                    $"DirtyRebuild {coordinate}: DensityField reference preserved"
                );
                Check(
                    ReferenceEquals(chunk.Material, materialRefs[coordinate]),
                    $"DirtyRebuild {coordinate}: MaterialField reference preserved"
                );

                if (wasDirty)
                {
                    Check(
                        chunk.Mesh != null &&
                        chunk.Mesh.GetInstanceID() != meshIdsBefore[coordinate],
                        $"DirtyRebuild {coordinate}: mesh rebuilt"
                    );

                    int sx = sampleX[coordinate];
                    int sy = sampleY[coordinate];
                    int sz = sampleZ[coordinate];

                    Check(
                        Mathf.Abs(chunk.Density[sx, sy, sz] + 10f) < 0.0001f,
                        $"DirtyRebuild {coordinate}: modified density preserved"
                    );
                    Check(
                        chunk.Material[sx, sy, sz] == MaterialType.Air,
                        $"DirtyRebuild {coordinate}: modified material preserved"
                    );

                    Transform chunkTransform = FindChunkTransform(terrain, chunk);
                    if (chunkTransform == null)
                    {
                        Check(false, $"DirtyRebuild {coordinate}: chunk GameObject exists");
                    }
                    else
                    {
                        TerrainChunkView view =
                            chunkTransform.GetComponent<TerrainChunkView>();
                        Check(
                            view != null &&
                            view.MeshFilter != null &&
                            view.MeshFilter.sharedMesh == chunk.Mesh,
                            $"DirtyRebuild {coordinate}: MeshFilter synced"
                        );
                        Check(
                            view != null &&
                            view.MeshCollider != null &&
                            view.MeshCollider.sharedMesh == chunk.Mesh,
                            $"DirtyRebuild {coordinate}: MeshCollider synced"
                        );
                    }
                }
                else
                {
                    Check(
                        chunk.Mesh != null &&
                        chunk.Mesh.GetInstanceID() == meshIdsBefore[coordinate],
                        $"DirtyRebuild {coordinate}: untouched mesh preserved"
                    );

                    Transform chunkTransform = FindChunkTransform(terrain, chunk);
                    TerrainChunkView view = chunkTransform != null
                        ? chunkTransform.GetComponent<TerrainChunkView>()
                        : null;
                    Check(
                        view != null &&
                        view.MeshFilter != null &&
                        view.MeshFilter.sharedMesh == chunk.Mesh,
                        $"DirtyRebuild {coordinate}: untouched MeshFilter preserved"
                    );
                    Check(
                        view != null &&
                        view.MeshCollider != null &&
                        view.MeshCollider.sharedMesh == chunk.Mesh,
                        $"DirtyRebuild {coordinate}: untouched MeshCollider preserved"
                    );
                }
            }
        }
        finally
        {
            DestroyTerrain(terrain);
        }
    }

    // ---------------------------------------------------------------------
    // 6. Actual scene configuration
    // ---------------------------------------------------------------------

    private static void ValidateSceneConfiguration()
    {
        if (!System.IO.File.Exists(SampleScenePath))
        {
            Check(false, "Scene config: SampleScene file exists");
            return;
        }

        Scene activeScene = SceneManager.GetActiveScene();
        bool alreadyLoaded = activeScene.IsValid() &&
                             activeScene.path == SampleScenePath;

        Scene scene = alreadyLoaded
            ? activeScene
            : EditorSceneManager.OpenScene(SampleScenePath, OpenSceneMode.Additive);

        try
        {
            DensityTerrain terrain = FindDensityTerrainInScene(scene);
            Check(terrain != null, "Scene config: DensityTerrain found");

            if (terrain == null) return;

            PlayerMiningController playerMining =
                FindComponentInScene<PlayerMiningController>(scene);
            Check(
                playerMining != null,
                "Scene config: PlayerMiningController mounted"
            );

            if (playerMining != null)
            {
                Check(
                    playerMining.targetCamera != null,
                    "Scene config: PlayerMiningController targetCamera assigned"
                );
                Check(
                    playerMining.terrain != null,
                    "Scene config: PlayerMiningController terrain assigned"
                );
                Check(
                    playerMining.brushSize.x > 0f &&
                    playerMining.brushSize.y > 0f &&
                    playerMining.brushSize.z > 0f,
                    "Scene config: PlayerMiningController brushSize positive"
                );
            }

            Check(
                terrain.voxelSize > 0f,
                "Scene config: voxelSize > 0 (temporary value, not hard-coded)"
            );
            Check(
                terrain.chunkVoxelSize.x > 0 &&
                terrain.chunkVoxelSize.y > 0 &&
                terrain.chunkVoxelSize.z > 0,
                "Scene config: chunkVoxelSize positive"
            );
            Check(
                terrain.chunkCount == ChunkCount,
                "Scene config: chunkCount = (2,1,2)"
            );
            Check(
                terrain.chunkStartCoordinate == Vector3Int.zero,
                "Scene config: chunkStartCoordinate = (0,0,0)"
            );

            terrain.Regenerate();

            CheckChunkViews(
                terrain,
                ExpectedChunkCoordinates,
                "Scene config lifecycle"
            );

            for (int i = 0; i < ExpectedChunkCoordinates.Length; i++)
            {
                Vector3Int coordinate = ExpectedChunkCoordinates[i];
                Check(
                    terrain.TryGetChunk(coordinate, out TerrainChunk chunk) &&
                    chunk != null,
                    $"Scene config: chunk {coordinate} generated"
                );
            }

            Check(
                !terrain.TryGetChunk(new Vector3Int(2, 0, 0), out _),
                "Scene config: ungenerated chunk returns false"
            );
        }
        finally
        {
            if (!alreadyLoaded)
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }

    private static DensityTerrain FindDensityTerrainInScene(Scene scene)
    {
        GameObject[] roots = scene.GetRootGameObjects();

        for (int i = 0; i < roots.Length; i++)
        {
            DensityTerrain terrain = roots[i].GetComponentInChildren<DensityTerrain>(true);
            if (terrain != null) return terrain;
        }

        return null;
    }

    private static T FindComponentInScene<T>(Scene scene) where T : Component
    {
        GameObject[] roots = scene.GetRootGameObjects();

        for (int i = 0; i < roots.Length; i++)
        {
            T component = roots[i].GetComponentInChildren<T>(true);
            if (component != null) return component;
        }

        return null;
    }

    // ---------------------------------------------------------------------
    // 7. Mining V1
    // ---------------------------------------------------------------------

    private static void ValidateMiningV1()
    {
        ValidateMiningSingleChunk();
        ValidateMiningCrossChunk();
        ValidateMiningBoundaryTouching();
        ValidateMiningDoesNotRegenerateWorld();
        ValidateMiningWithNonIdentityTransform();
        ValidateMiningMeshResult();
    }

    private static void ValidateMiningSingleChunk()
    {
        RunMiningCase(
            "Mining single chunk",
            new Vector3(10f, 10f, 10f),
            new Vector3(5f, 5f, 5f),
            new[] { new Vector3Int(0, 0, 0) }
        );
    }

    private static void ValidateMiningCrossChunk()
    {
        RunMiningCase(
            "Mining across X boundary",
            new Vector3(41.5f, 10f, 10f),
            new Vector3(5f, 5f, 5f),
            new[] { new Vector3Int(0, 0, 0), new Vector3Int(1, 0, 0) }
        );
        RunMiningCase(
            "Mining across Z boundary",
            new Vector3(10f, 10f, 27.6f),
            new Vector3(5f, 5f, 5f),
            new[] { new Vector3Int(0, 0, 0), new Vector3Int(0, 0, 1) }
        );
        RunMiningCase(
            "Mining across XZ boundary",
            new Vector3(41.5f, 10f, 27.6f),
            new Vector3(5f, 5f, 5f),
            new[]
            {
                new Vector3Int(0, 0, 0),
                new Vector3Int(1, 0, 0),
                new Vector3Int(0, 0, 1),
                new Vector3Int(1, 0, 1)
            }
        );
    }

    private static void ValidateMiningBoundaryTouching()
    {
        RunMiningCase(
            "Mining touching X boundary",
            new Vector3(43.5f, 10f, 10f),
            new Vector3(4f, 4f, 4f),
            new[] { new Vector3Int(0, 0, 0), new Vector3Int(1, 0, 0) }
        );
        RunMiningCase(
            "Mining touching Z boundary",
            new Vector3(10f, 10f, 29.6f),
            new Vector3(4f, 4f, 4f),
            new[] { new Vector3Int(0, 0, 0), new Vector3Int(0, 0, 1) }
        );
        RunMiningCase(
            "Mining touching XZ boundary",
            new Vector3(43.5f, 10f, 29.6f),
            new Vector3(4f, 4f, 4f),
            new[]
            {
                new Vector3Int(0, 0, 0),
                new Vector3Int(1, 0, 0),
                new Vector3Int(0, 0, 1),
                new Vector3Int(1, 0, 1)
            }
        );
    }

    private static void ValidateMiningDoesNotRegenerateWorld()
    {
        DensityTerrain terrain = CreateTerrain(
            "Validation_Mining_NoWorldRegeneration",
            DensityGenerationMode.HeightField
        );

        try
        {
            if (!terrain.TryGetChunk(new Vector3Int(0, 0, 0), out TerrainChunk chunk0) ||
                !terrain.TryGetChunk(new Vector3Int(1, 0, 0), out TerrainChunk chunk1))
            {
                Check(false, "Mining no-regen: chunks exist");
                return;
            }

            DensityField densityRef = chunk0.Density;
            MaterialField materialRef = chunk0.Material;

            Bounds brushBounds = new Bounds(
                new Vector3(10f, 10f, 10f),
                new Vector3(5f, 5f, 5f)
            );

            if (!TryFindSolidSampleOutsideBounds(
                chunk0.Density,
                brushBounds,
                out int sx,
                out int sy,
                out int sz))
            {
                Check(false, "Mining no-regen: found sentinel sample outside brush");
                return;
            }

            const float sentinelDensity = 123.456f;
            chunk0.Density[sx, sy, sz] = sentinelDensity;
            chunk0.Material[sx, sy, sz] = MaterialType.Dirt;

            int chunk1MeshIdBefore = chunk1.Mesh.GetInstanceID();

            terrain.MineCubeLocal(brushBounds.center, brushBounds.size);

            Check(
                ReferenceEquals(chunk0.Density, densityRef),
                "Mining no-regen: DensityField reference preserved"
            );
            Check(
                ReferenceEquals(chunk0.Material, materialRef),
                "Mining no-regen: MaterialField reference preserved"
            );
            Check(
                Mathf.Abs(chunk0.Density[sx, sy, sz] - sentinelDensity) < 0.0001f,
                "Mining no-regen: sentinel density preserved"
            );
            Check(
                chunk0.Material[sx, sy, sz] == MaterialType.Dirt,
                "Mining no-regen: sentinel material preserved"
            );
            Check(
                chunk1.Mesh.GetInstanceID() == chunk1MeshIdBefore,
                "Mining no-regen: unaffected chunk mesh preserved"
            );
        }
        finally
        {
            DestroyTerrain(terrain);
        }
    }

    private static void ValidateMiningWithNonIdentityTransform()
    {
        DensityTerrain terrain = CreateTerrain(
            "Validation_Mining_Transform",
            DensityGenerationMode.HeightField
        );

        try
        {
            terrain.transform.position = new Vector3(10f, 5f, -3f);
            terrain.transform.rotation = Quaternion.Euler(0f, 35f, 0f);
            terrain.transform.localScale = new Vector3(1.5f, 2f, 0.75f);
            terrain.Regenerate();

            if (!terrain.TryGetChunk(new Vector3Int(0, 0, 0), out TerrainChunk chunk))
            {
                Check(false, "Mining transform: chunk exists");
                return;
            }

            Bounds brushBounds = new Bounds(
                new Vector3(10f, 10f, 10f),
                new Vector3(5f, 5f, 5f)
            );

            CaptureChunkData(
                chunk,
                out float[,,] beforeDensity,
                out MaterialType[,,] beforeMaterial
            );
            int meshIdBefore = chunk.Mesh.GetInstanceID();

            int modified = terrain.MineCubeLocal(
                brushBounds.center,
                brushBounds.size
            );

            Check(modified > 0, "Mining transform: local samples modified");
            Check(
                chunk.Mesh.GetInstanceID() != meshIdBefore,
                "Mining transform: mesh rebuilt"
            );
            CheckSamplesInsideAreAir(chunk, brushBounds, "Mining transform");
            CheckSamplesOutsideUnchanged(
                chunk,
                brushBounds,
                beforeDensity,
                beforeMaterial,
                "Mining transform"
            );
            ValidateChunkLocalVertices(chunk, "Mining transform");

            Vector3 worldCenter = terrain.transform.TransformPoint(brushBounds.center);
            Check(
                terrain.TryGetChunk(worldCenter, out TerrainChunk worldChunk) &&
                worldChunk == chunk,
                "Mining transform: local brush maps back to containing chunk"
            );
            Check(
                Mathf.Abs(
                    SampleDensityAtLocal(chunk.Density, brushBounds.center) -
                    TerrainMining.AirDensity
                ) < 0.0001f,
                "Mining transform: brush center density is air"
            );

            CheckChunkViews(
                terrain,
                ExpectedChunkCoordinates,
                "Mining transform lifecycle"
            );
        }
        finally
        {
            DestroyTerrain(terrain);
        }
    }

    private static void ValidateMiningMeshResult()
    {
        DensityTerrain terrain = CreateTerrain(
            "Validation_Mining_MeshResult",
            DensityGenerationMode.HeightField
        );

        try
        {
            Bounds brushBounds = new Bounds(
                new Vector3(10f, 10f, 10f),
                new Vector3(5f, 5f, 5f)
            );

            if (!terrain.TryGetChunk(new Vector3Int(0, 0, 0), out TerrainChunk chunk0) ||
                !terrain.TryGetChunk(new Vector3Int(1, 0, 0), out TerrainChunk chunk1))
            {
                Check(false, "Mining mesh: chunks exist");
                return;
            }

            int chunk0MeshIdBefore = chunk0.Mesh.GetInstanceID();
            int chunk1MeshIdBefore = chunk1.Mesh.GetInstanceID();
            int chunk0VertexCountBefore = chunk0.Mesh.vertexCount;

            terrain.MineCubeLocal(brushBounds.center, brushBounds.size);

            Check(
                chunk0.Mesh != null && chunk0.Mesh.vertexCount > 0,
                "Mining mesh: affected mesh exists"
            );
            Check(
                chunk0.Mesh.GetInstanceID() != chunk0MeshIdBefore,
                "Mining mesh: affected mesh rebuilt"
            );
            Check(
                chunk1.Mesh.GetInstanceID() == chunk1MeshIdBefore,
                "Mining mesh: unaffected mesh preserved"
            );
            ValidateChunkLocalVertices(chunk0, "Mining mesh");
            CheckNoMeshVerticesDeepInside(chunk0, brushBounds, "Mining mesh");
            Check(
                Mathf.Abs(
                    SampleDensityAtLocal(chunk0.Density, brushBounds.center) -
                    TerrainMining.AirDensity
                ) < 0.0001f,
                "Mining mesh: brush center density is air"
            );

            CheckChunkViews(
                terrain,
                ExpectedChunkCoordinates,
                "Mining mesh lifecycle"
            );

            Debug.Log(
                "[TerrainMultiChunkValidation] Mining mesh vertex count " +
                $"{chunk0VertexCountBefore} → {chunk0.Mesh.vertexCount}"
            );
        }
        finally
        {
            DestroyTerrain(terrain);
        }
    }

    private static void RunMiningCase(
        string label,
        Vector3 localCenter,
        Vector3 size,
        Vector3Int[] expectedAffectedCoordinates)
    {
        DensityTerrain terrain = CreateTerrain(
            "Validation_" + label.Replace(" ", "_"),
            DensityGenerationMode.HeightField
        );

        try
        {
            Bounds brushBounds = new Bounds(
                localCenter,
                new Vector3(
                    Mathf.Abs(size.x),
                    Mathf.Abs(size.y),
                    Mathf.Abs(size.z)
                )
            );

            Dictionary<Vector3Int, TerrainChunk> allChunks =
                new Dictionary<Vector3Int, TerrainChunk>();
            Dictionary<Vector3Int, TerrainChunkView> views =
                new Dictionary<Vector3Int, TerrainChunkView>();
            Dictionary<Vector3Int, int> meshIdsBefore =
                new Dictionary<Vector3Int, int>();
            Dictionary<Vector3Int, float[,,]> densitySnapshots =
                new Dictionary<Vector3Int, float[,,]>();
            Dictionary<Vector3Int, MaterialType[,,]> materialSnapshots =
                new Dictionary<Vector3Int, MaterialType[,,]>();

            for (int i = 0; i < ExpectedChunkCoordinates.Length; i++)
            {
                Vector3Int coordinate = ExpectedChunkCoordinates[i];

                if (!terrain.TryGetChunk(coordinate, out TerrainChunk chunk) ||
                    chunk == null)
                {
                    Check(
                        false,
                        label + ": expected chunk " + coordinate + " exists"
                    );
                    continue;
                }

                allChunks[coordinate] = chunk;
                meshIdsBefore[coordinate] = chunk.Mesh.GetInstanceID();

                Transform chunkTransform = FindChunkTransform(terrain, chunk);
                TerrainChunkView view = chunkTransform != null
                    ? chunkTransform.GetComponent<TerrainChunkView>()
                    : null;
                Check(
                    view != null && view.MeshCollider != null,
                    label + ": chunk " + coordinate +
                    " has TerrainChunkView and MeshCollider"
                );
                views[coordinate] = view;

                CaptureChunkData(
                    chunk,
                    out float[,,] densitySnapshot,
                    out MaterialType[,,] materialSnapshot
                );
                densitySnapshots[coordinate] = densitySnapshot;
                materialSnapshots[coordinate] = materialSnapshot;
            }

            List<TerrainChunk> affectedChunks = new List<TerrainChunk>();
            terrain.CollectChunksIntersectingLocalBounds(
                brushBounds,
                affectedChunks
            );

            CheckAffectedChunkSet(
                label + ": affected chunk query",
                affectedChunks,
                expectedAffectedCoordinates
            );

            int modifiedSamples = TerrainMining.MineCube(
                terrain,
                localCenter,
                size,
                rebuild: false
            );

            Check(modifiedSamples > 0, label + ": modified density samples > 0");

            for (int i = 0; i < expectedAffectedCoordinates.Length; i++)
            {
                Vector3Int coordinate = expectedAffectedCoordinates[i];
                if (!allChunks.TryGetValue(coordinate, out TerrainChunk chunk))
                {
                    continue;
                }

                Check(
                    chunk.IsDirty,
                    label + ": chunk " + coordinate + " dirty before rebuild"
                );

                if (views.TryGetValue(coordinate, out TerrainChunkView view) &&
                    view != null)
                {
                    Check(
                        chunk.Mesh.GetInstanceID() == meshIdsBefore[coordinate],
                        label + ": chunk " + coordinate +
                        " mesh not rebuilt before rebuild"
                    );
                    Check(
                        view.MeshFilter != null &&
                        view.MeshFilter.sharedMesh == chunk.Mesh,
                        label + ": chunk " + coordinate +
                        " MeshFilter still references old mesh before rebuild"
                    );
                    Check(
                        view.MeshCollider != null &&
                        view.MeshCollider.sharedMesh == chunk.Mesh,
                        label + ": chunk " + coordinate +
                        " MeshCollider still references old mesh before rebuild"
                    );
                }

                CheckSamplesInsideAreAir(
                    chunk,
                    brushBounds,
                    label + " " + coordinate
                );
                CheckSamplesOutsideUnchanged(
                    chunk,
                    brushBounds,
                    densitySnapshots[coordinate],
                    materialSnapshots[coordinate],
                    label + " " + coordinate
                );
            }

            for (int i = 0; i < ExpectedChunkCoordinates.Length; i++)
            {
                Vector3Int coordinate = ExpectedChunkCoordinates[i];
                if (Array.IndexOf(expectedAffectedCoordinates, coordinate) >= 0)
                {
                    continue;
                }

                if (!allChunks.TryGetValue(coordinate, out TerrainChunk chunk))
                {
                    continue;
                }

                Check(
                    !chunk.IsDirty,
                    label + ": chunk " + coordinate + " not dirty"
                );
                Check(
                    ChunkDataMatches(
                        chunk,
                        densitySnapshots[coordinate],
                        materialSnapshots[coordinate]
                    ),
                    label + ": chunk " + coordinate + " data unchanged"
                );
            }

            terrain.RebuildDirtyChunks();

            for (int i = 0; i < expectedAffectedCoordinates.Length; i++)
            {
                Vector3Int coordinate = expectedAffectedCoordinates[i];
                if (!allChunks.TryGetValue(coordinate, out TerrainChunk chunk))
                {
                    continue;
                }

                Check(
                    !chunk.IsDirty,
                    label + ": chunk " + coordinate + " clean after rebuild"
                );
                Check(
                    chunk.Mesh != null &&
                    chunk.Mesh.GetInstanceID() != meshIdsBefore[coordinate],
                    label + ": chunk " + coordinate + " mesh rebuilt"
                );

                if (views.TryGetValue(coordinate, out TerrainChunkView view) &&
                    view != null)
                {
                    Check(
                        view.MeshFilter != null &&
                        view.MeshFilter.sharedMesh == chunk.Mesh,
                        label + ": chunk " + coordinate +
                        " MeshFilter synced after rebuild"
                    );
                    Check(
                        view.MeshCollider != null &&
                        view.MeshCollider.sharedMesh == chunk.Mesh,
                        label + ": chunk " + coordinate +
                        " MeshCollider synced after rebuild"
                    );
                }
                else
                {
                    Check(
                        false,
                        label + ": chunk " + coordinate +
                        " view exists after rebuild"
                    );
                }

                Transform chunkTransform = FindChunkTransform(terrain, chunk);
                if (chunkTransform == null)
                {
                    Check(
                        false,
                        label + ": chunk " + coordinate + " GameObject exists"
                    );
                }
                else
                {
                    MeshFilter filter = chunkTransform.GetComponent<MeshFilter>();
                    Check(
                        filter != null && filter.sharedMesh == chunk.Mesh,
                        label + ": chunk " + coordinate + " MeshFilter synced"
                    );
                }

                ValidateChunkLocalVertices(chunk, label + " " + coordinate);
                CheckNoMeshVerticesDeepInside(
                    chunk,
                    brushBounds,
                    label + " " + coordinate
                );
            }

            for (int i = 0; i < ExpectedChunkCoordinates.Length; i++)
            {
                Vector3Int coordinate = ExpectedChunkCoordinates[i];
                if (Array.IndexOf(expectedAffectedCoordinates, coordinate) >= 0)
                {
                    continue;
                }

                if (!allChunks.TryGetValue(coordinate, out TerrainChunk chunk))
                {
                    continue;
                }

                Check(
                    chunk.Mesh != null &&
                    chunk.Mesh.GetInstanceID() == meshIdsBefore[coordinate],
                    label + ": chunk " + coordinate + " mesh not rebuilt"
                );

                if (views.TryGetValue(coordinate, out TerrainChunkView view) &&
                    view != null)
                {
                    Check(
                        view.MeshFilter != null &&
                        view.MeshFilter.sharedMesh == chunk.Mesh,
                        label + ": chunk " + coordinate +
                        " MeshFilter unchanged"
                    );
                    Check(
                        view.MeshCollider != null &&
                        view.MeshCollider.sharedMesh == chunk.Mesh,
                        label + ": chunk " + coordinate +
                        " MeshCollider unchanged"
                    );
                }
            }
        }
        finally
        {
            DestroyTerrain(terrain);
        }
    }

    private static void CheckAffectedChunkSet(
        string label,
        List<TerrainChunk> affectedChunks,
        Vector3Int[] expectedCoordinates)
    {
        HashSet<Vector3Int> actual = new HashSet<Vector3Int>();
        for (int i = 0; i < affectedChunks.Count; i++)
        {
            if (affectedChunks[i] != null)
            {
                actual.Add(affectedChunks[i].Coordinate);
            }
        }

        HashSet<Vector3Int> expected = new HashSet<Vector3Int>(expectedCoordinates);

        Check(
            actual.Count == expected.Count && actual.SetEquals(expected),
            label + ": expected " + FormatCoordinates(expected) +
            ", actual " + FormatCoordinates(actual)
        );
    }

    private static string FormatCoordinates(IEnumerable<Vector3Int> coordinates)
    {
        List<string> parts = new List<string>();
        foreach (Vector3Int coordinate in coordinates)
        {
            parts.Add(coordinate.ToString());
        }

        return "[" + string.Join(", ", parts) + "]";
    }

    private static void CaptureChunkData(
        TerrainChunk chunk,
        out float[,,] densitySnapshot,
        out MaterialType[,,] materialSnapshot)
    {
        DensityField density = chunk.Density;
        MaterialField material = chunk.Material;

        densitySnapshot = new float[
            density.SampleCountX,
            density.SampleCountY,
            density.SampleCountZ
        ];
        materialSnapshot = new MaterialType[
            material.SampleCountX,
            material.SampleCountY,
            material.SampleCountZ
        ];

        for (int x = 0; x < density.SampleCountX; x++)
        {
            for (int y = 0; y < density.SampleCountY; y++)
            {
                for (int z = 0; z < density.SampleCountZ; z++)
                {
                    densitySnapshot[x, y, z] = density[x, y, z];
                    materialSnapshot[x, y, z] = material[x, y, z];
                }
            }
        }
    }

    private static bool ChunkDataMatches(
        TerrainChunk chunk,
        float[,,] expectedDensity,
        MaterialType[,,] expectedMaterial)
    {
        DensityField density = chunk.Density;
        MaterialField material = chunk.Material;

        for (int x = 0; x < density.SampleCountX; x++)
        {
            for (int y = 0; y < density.SampleCountY; y++)
            {
                for (int z = 0; z < density.SampleCountZ; z++)
                {
                    if (Mathf.Abs(
                        density[x, y, z] - expectedDensity[x, y, z]
                    ) > 0.0001f)
                    {
                        return false;
                    }

                    if (material[x, y, z] != expectedMaterial[x, y, z])
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private static int CheckSamplesInsideAreAir(
        TerrainChunk chunk,
        Bounds brushBounds,
        string label)
    {
        DensityField density = chunk.Density;
        MaterialField material = chunk.Material;
        int insideCount = 0;
        int mismatches = 0;

        for (int x = 0; x < density.SampleCountX; x++)
        {
            for (int y = 0; y < density.SampleCountY; y++)
            {
                for (int z = 0; z < density.SampleCountZ; z++)
                {
                    Vector3 localPosition = density.GetLocalPosition(x, y, z);
                    if (!ContainsInclusive(brushBounds, localPosition))
                    {
                        continue;
                    }

                    insideCount++;

                    if (Mathf.Abs(density[x, y, z] - TerrainMining.AirDensity) > 0.0001f ||
                        material[x, y, z] != MaterialType.Air)
                    {
                        mismatches++;
                    }
                }
            }
        }

        Check(insideCount > 0, label + ": samples inside brush");
        Check(mismatches == 0, label + ": inside samples are Air");

        return insideCount;
    }

    private static void CheckSamplesOutsideUnchanged(
        TerrainChunk chunk,
        Bounds brushBounds,
        float[,,] beforeDensity,
        MaterialType[,,] beforeMaterial,
        string label)
    {
        DensityField density = chunk.Density;
        MaterialField material = chunk.Material;
        int mismatches = 0;

        for (int x = 0; x < density.SampleCountX; x++)
        {
            for (int y = 0; y < density.SampleCountY; y++)
            {
                for (int z = 0; z < density.SampleCountZ; z++)
                {
                    Vector3 localPosition = density.GetLocalPosition(x, y, z);
                    if (ContainsInclusive(brushBounds, localPosition))
                    {
                        continue;
                    }

                    if (Mathf.Abs(density[x, y, z] - beforeDensity[x, y, z]) > 0.0001f ||
                        material[x, y, z] != beforeMaterial[x, y, z])
                    {
                        mismatches++;
                    }
                }
            }
        }

        Check(mismatches == 0, label + ": outside samples unchanged");
    }

    private static void CheckNoMeshVerticesDeepInside(
        TerrainChunk chunk,
        Bounds brushBounds,
        string label)
    {
        if (chunk == null || chunk.Mesh == null || chunk.Density == null)
        {
            Check(false, label + ": mesh exists for deep-inside check");
            return;
        }

        Vector3 margin = new Vector3(
            chunk.Density.CellSizeX,
            chunk.Density.CellSizeY,
            chunk.Density.CellSizeZ
        );

        Vector3 innerSize = new Vector3(
            brushBounds.size.x - margin.x * 2f,
            brushBounds.size.y - margin.y * 2f,
            brushBounds.size.z - margin.z * 2f
        );

        if (innerSize.x <= 0f || innerSize.y <= 0f || innerSize.z <= 0f)
        {
            return;
        }

        Bounds innerBounds = new Bounds(brushBounds.center, innerSize);
        Vector3[] vertices = chunk.Mesh.vertices;
        int deepVertices = 0;

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 terrainLocal = chunk.ChunkOrigin + vertices[i];
            if (innerBounds.Contains(terrainLocal))
            {
                deepVertices++;
            }
        }

        Check(
            deepVertices == 0,
            label + ": no mesh vertices deep inside mined region"
        );
    }

    private static bool ContainsInclusive(Bounds bounds, Vector3 point)
    {
        return point.x >= bounds.min.x && point.x <= bounds.max.x
            && point.y >= bounds.min.y && point.y <= bounds.max.y
            && point.z >= bounds.min.z && point.z <= bounds.max.z;
    }

    private static bool TryFindSolidSampleOutsideBounds(
        DensityField field,
        Bounds bounds,
        out int x,
        out int y,
        out int z)
    {
        for (x = 0; x < field.SampleCountX; x++)
        {
            for (y = 0; y < field.SampleCountY; y++)
            {
                for (z = 0; z < field.SampleCountZ; z++)
                {
                    if (field[x, y, z] <= 1f) continue;
                    if (ContainsInclusive(bounds, field.GetLocalPosition(x, y, z)))
                    {
                        continue;
                    }

                    return true;
                }
            }
        }

        x = 0;
        y = 0;
        z = 0;
        return false;
    }

    // ---------------------------------------------------------------------
    // 8. Chunk GameObject / Mesh Lifecycle
    // ---------------------------------------------------------------------

    private static void ValidateChunkLifecycleV1()
    {
        ValidateChunkCountsAndMeshSync();
        ValidateMeshColliderNullHandling();
        ValidateChunkStartCoordinateLifecycle();
        ValidateRegenerateDoesNotAccumulate();
        ValidateUntrackedLegacyChunkCleanup();
        ValidateChunkSceneReloadLifecycle();
    }

    private static void ValidateChunkCountsAndMeshSync()
    {
        DensityTerrain terrain = CreateTerrain(
            "Validation_Lifecycle_Count",
            DensityGenerationMode.HeightField
        );

        try
        {
            CheckChunkViews(
                terrain,
                ExpectedChunkCoordinates,
                "Lifecycle count (2,1,2)"
            );
        }
        finally
        {
            DestroyTerrain(terrain);
        }
    }

    private static void ValidateMeshColliderNullHandling()
    {
        DensityTerrain terrain = CreateTerrain(
            "Validation_Lifecycle_ColliderNull",
            DensityGenerationMode.HeightField
        );

        try
        {
            if (!terrain.TryGetChunk(new Vector3Int(0, 0, 0), out TerrainChunk chunk))
            {
                Check(false, "Collider null handling: chunk exists");
                return;
            }

            Transform chunkTransform = FindChunkTransform(terrain, chunk);
            TerrainChunkView view = chunkTransform != null
                ? chunkTransform.GetComponent<TerrainChunkView>()
                : null;

            Check(
                view != null && view.MeshCollider != null,
                "Collider null handling: view/MeshCollider exists"
            );
            if (view == null || view.MeshCollider == null) return;

            view.SetMesh(null);

            Check(
                view.MeshFilter == null || view.MeshFilter.sharedMesh == null,
                "Collider null handling: MeshFilter cleared"
            );
            Check(
                view.MeshCollider.sharedMesh == null,
                "Collider null handling: MeshCollider cleared"
            );

            view.SetMesh(chunk.Mesh);

            Check(
                view.MeshFilter != null &&
                view.MeshFilter.sharedMesh == chunk.Mesh,
                "Collider null handling: MeshFilter restored"
            );
            Check(
                view.MeshCollider.sharedMesh == chunk.Mesh,
                "Collider null handling: MeshCollider restored"
            );
        }
        finally
        {
            DestroyTerrain(terrain);
        }
    }

    private static void ValidateChunkStartCoordinateLifecycle()
    {
        Vector3Int start = new Vector3Int(5, 0, 7);
        Vector3Int[] expected =
        {
            start,
            start + new Vector3Int(1, 0, 0),
            start + new Vector3Int(0, 0, 1),
            start + new Vector3Int(1, 0, 1)
        };

        DensityTerrain terrain = CreateTerrain(
            "Validation_Lifecycle_Start",
            DensityGenerationMode.HeightField,
            start
        );

        try
        {
            CheckChunkViews(
                terrain,
                expected,
                "Lifecycle start (5,0,7)"
            );
        }
        finally
        {
            DestroyTerrain(terrain);
        }
    }

    private static void ValidateRegenerateDoesNotAccumulate()
    {
        DensityTerrain terrain = CreateTerrain(
            "Validation_Lifecycle_Regenerate",
            DensityGenerationMode.HeightField
        );

        try
        {
            for (int i = 0; i < 5; i++)
            {
                terrain.Regenerate();
                CheckChunkViews(
                    terrain,
                    ExpectedChunkCoordinates,
                    "Lifecycle regenerate " + i
                );
            }
        }
        finally
        {
            DestroyTerrain(terrain);
        }
    }

    private static void ValidateUntrackedLegacyChunkCleanup()
    {
        DensityTerrain terrain = CreateTerrain(
            "Validation_Lifecycle_Orphan",
            DensityGenerationMode.HeightField
        );

        try
        {
            GameObject orphan = new GameObject("Chunk_9_9_9");
            orphan.transform.SetParent(terrain.transform, false);
            orphan.AddComponent<MeshFilter>();
            orphan.AddComponent<MeshRenderer>();

            Check(
                CountLegacyChunkObjects(terrain) == 1,
                "Lifecycle orphan: legacy chunk created"
            );

            terrain.Regenerate();

            CheckChunkViews(
                terrain,
                ExpectedChunkCoordinates,
                "Lifecycle orphan: cleanup"
            );
            Check(
                CountLegacyChunkObjects(terrain) == 0,
                "Lifecycle orphan: legacy chunk removed"
            );
        }
        finally
        {
            DestroyTerrain(terrain);
        }
    }

    private static void ValidateChunkSceneReloadLifecycle()
    {
        const string tempScenePath =
            "Assets/Scenes/Validation_ChunkLifecycleTemp.unity";

        if (System.IO.File.Exists(tempScenePath))
        {
            AssetDatabase.DeleteAsset(tempScenePath);
        }

        Scene originalScene = SceneManager.GetActiveScene();
        Scene tempScene = EditorSceneManager.NewScene(
            NewSceneSetup.EmptyScene,
            NewSceneMode.Additive
        );
        SceneManager.SetActiveScene(tempScene);

        DensityTerrain terrain = CreateTerrain(
            "Validation_Lifecycle_SceneReload",
            DensityGenerationMode.HeightField
        );

        try
        {
            Check(
                CountChunkViews(terrain) == ExpectedChunkCoordinates.Length,
                "Lifecycle scene: runtime chunks exist before save"
            );

            // runtime Chunk GameObjects 是 HideFlags.DontSave，不应写入 Scene。
            terrain.gameObject.SetActive(false);
            EditorSceneManager.MarkSceneDirty(tempScene);
            EditorSceneManager.SaveScene(tempScene, tempScenePath);
            EditorSceneManager.CloseScene(tempScene, true);
            SceneManager.SetActiveScene(originalScene);

            string savedSceneText = System.IO.File.ReadAllText(tempScenePath);
            Check(
                !savedSceneText.Contains("m_Name: Chunk_"),
                "Lifecycle scene: saved scene contains no Chunk GameObjects"
            );

            Scene reloaded = EditorSceneManager.OpenScene(
                tempScenePath,
                OpenSceneMode.Additive
            );

            try
            {
                DensityTerrain reloadedTerrain = FindDensityTerrainInScene(reloaded);
                Check(
                    reloadedTerrain != null,
                    "Lifecycle scene: reloaded DensityTerrain found"
                );

                if (reloadedTerrain != null)
                {
                    Check(
                        CountChunkViews(reloadedTerrain) == 0,
                        "Lifecycle scene: no runtime chunks persisted in scene file"
                    );
                    Check(
                        CountLegacyChunkObjects(reloadedTerrain) == 0,
                        "Lifecycle scene: no legacy chunks after reload"
                    );

                    reloadedTerrain.Regenerate();
                    CheckChunkViews(
                        reloadedTerrain,
                        ExpectedChunkCoordinates,
                        "Lifecycle scene: regenerate after reload"
                    );

                    GameObject orphan = new GameObject("Chunk_9_9_9");
                    orphan.transform.SetParent(reloadedTerrain.transform, false);
                    orphan.AddComponent<MeshFilter>();
                    orphan.AddComponent<MeshRenderer>();

                    Check(
                        CountLegacyChunkObjects(reloadedTerrain) == 1,
                        "Lifecycle scene: legacy orphan added after reload"
                    );

                    reloadedTerrain.Regenerate();
                    CheckChunkViews(
                        reloadedTerrain,
                        ExpectedChunkCoordinates,
                        "Lifecycle scene: orphan cleanup after reload"
                    );
                    Check(
                        CountLegacyChunkObjects(reloadedTerrain) == 0,
                        "Lifecycle scene: legacy orphan removed after reload"
                    );
                }
            }
            finally
            {
                EditorSceneManager.CloseScene(reloaded, true);
            }
        }
        finally
        {
            if (System.IO.File.Exists(tempScenePath))
            {
                AssetDatabase.DeleteAsset(tempScenePath);
            }

            if (SceneManager.GetActiveScene() != originalScene)
            {
                SceneManager.SetActiveScene(originalScene);
            }
        }
    }

    private static void CheckChunkViews(
        DensityTerrain terrain,
        Vector3Int[] expectedCoordinates,
        string label)
    {
        if (terrain == null)
        {
            Check(false, label + ": terrain exists");
            return;
        }

        TerrainChunkView[] views =
            terrain.GetComponentsInChildren<TerrainChunkView>(true);

        Check(
            views.Length == expectedCoordinates.Length,
            label + $": runtime chunk count = {views.Length}, expected {expectedCoordinates.Length}"
        );

        HashSet<Vector3Int> expected = new HashSet<Vector3Int>(expectedCoordinates);
        HashSet<Vector3Int> actual = new HashSet<Vector3Int>();
        int missingChunkReferences = 0;
        int meshSyncMismatches = 0;
        int colliderSyncMismatches = 0;

        for (int i = 0; i < views.Length; i++)
        {
            TerrainChunkView view = views[i];
            if (view == null) continue;

            actual.Add(view.Coordinate);

            if (view.Chunk == null ||
                view.MeshFilter == null ||
                view.MeshCollider == null)
            {
                missingChunkReferences++;
                continue;
            }

            if (view.MeshFilter.sharedMesh != view.Chunk.Mesh)
            {
                meshSyncMismatches++;
            }

            if (view.MeshCollider.sharedMesh != view.Chunk.Mesh)
            {
                colliderSyncMismatches++;
            }
        }

        Check(
            actual.SetEquals(expected),
            label + ": coordinates " + FormatCoordinates(actual) +
            " match " + FormatCoordinates(expected)
        );
        Check(
            missingChunkReferences == 0,
            label + ": every view has TerrainChunk, MeshFilter and MeshCollider"
        );
        Check(
            meshSyncMismatches == 0,
            label + ": MeshFilter.sharedMesh == TerrainChunk.Mesh"
        );
        Check(
            colliderSyncMismatches == 0,
            label + ": MeshCollider.sharedMesh == TerrainChunk.Mesh"
        );
        Check(
            CountLegacyChunkObjects(terrain) == 0,
            label + ": no legacy orphan Chunk GameObjects"
        );
        Check(
            CountChunkNamedChildren(terrain) == expectedCoordinates.Length,
            label + ": only current Chunk GameObjects remain"
        );
    }

    private static int CountChunkViews(DensityTerrain terrain)
    {
        if (terrain == null) return 0;

        return terrain.GetComponentsInChildren<TerrainChunkView>(true).Length;
    }

    private static int CountLegacyChunkObjects(DensityTerrain terrain)
    {
        if (terrain == null) return 0;

        int count = 0;

        for (int i = 0; i < terrain.transform.childCount; i++)
        {
            Transform child = terrain.transform.GetChild(i);
            if (child == null) continue;

            GameObject childObject = child.gameObject;
            if (childObject == null) continue;
            if (childObject.GetComponent<TerrainChunkView>() != null) continue;
            if (!IsLegacyChunkObject(childObject)) continue;

            count++;
        }

        return count;
    }

    private static int CountChunkNamedChildren(DensityTerrain terrain)
    {
        if (terrain == null) return 0;

        int count = 0;

        for (int i = 0; i < terrain.transform.childCount; i++)
        {
            Transform child = terrain.transform.GetChild(i);
            if (child == null) continue;
            if (child.name.StartsWith("Chunk_")) count++;
        }

        return count;
    }

    private static bool IsLegacyChunkObject(GameObject gameObject)
    {
        if (gameObject == null) return false;
        if (!gameObject.name.StartsWith("Chunk_")) return false;
        if (gameObject.GetComponent<MeshFilter>() == null) return false;
        if (gameObject.GetComponent<MeshRenderer>() == null) return false;

        return true;
    }

    // ---------------------------------------------------------------------
    // 9. Player Mining / Targeting V1
    // ---------------------------------------------------------------------

    private static void ValidatePlayerMiningV1()
    {
        ValidatePlayerMiningRaycastAndMine();
        ValidatePlayerMiningNonIdentityTransform();
        ValidatePlayerMiningNoHitDoesNotModify();
        ValidatePlayerMiningNonTerrainColliderRejected();
    }

    private static void ValidatePlayerMiningRaycastAndMine()
    {
        DensityTerrain terrain = CreateTerrain(
            "Validation_PlayerMining_Identity",
            DensityGenerationMode.HeightField
        );
        Camera camera = null;
        PlayerMiningController controller = null;

        try
        {
            camera = CreateValidationCamera(
                "Validation_PlayerMining_Camera",
                new Vector3(10f, 40f, 10f),
                Quaternion.Euler(90f, 0f, 0f)
            );
            controller = CreateValidationPlayerMining(
                camera,
                terrain,
                new Vector3(4f, 4f, 4f)
            );

            bool hit = controller.TryGetTerrainHit(
                out RaycastHit rayHit,
                out TerrainChunkView view
            );
            Check(
                hit && view != null,
                "PlayerMining identity: raycast hits Terrain MeshCollider"
            );
            if (!hit || view == null) return;

            Check(
                rayHit.collider != null &&
                rayHit.collider.GetComponentInParent<TerrainChunkView>() != null,
                "PlayerMining identity: hit collider is Terrain MeshCollider"
            );

            Vector3 localHit = terrain.WorldToLocalPosition(rayHit.point);
            Check(
                Mathf.Abs(localHit.x - 10f) < 0.5f &&
                Mathf.Abs(localHit.y - 17.5f) < 0.5f &&
                Mathf.Abs(localHit.z - 10f) < 0.5f,
                "PlayerMining identity: hit point converts to Terrain Local Space"
            );

            TerrainChunk chunk = view.Chunk;
            Check(
                chunk != null &&
                terrain.TryGetChunk(
                    terrain.WorldToChunkCoordinate(rayHit.point),
                    out TerrainChunk hitChunk
                ) &&
                hitChunk == chunk,
                "PlayerMining identity: world hit maps to expected Chunk"
            );
            if (chunk == null) return;

            CaptureChunkData(
                chunk,
                out float[,,] beforeDensity,
                out MaterialType[,,] beforeMaterial
            );
            int meshIdBefore = chunk.Mesh.GetInstanceID();

            TerrainChunk otherChunk = null;
            TerrainChunkView otherView = null;
            int otherMeshIdBefore = 0;

            if (terrain.TryGetChunk(new Vector3Int(1, 0, 0), out otherChunk))
            {
                otherMeshIdBefore = otherChunk.Mesh.GetInstanceID();
                Transform otherTransform = FindChunkTransform(terrain, otherChunk);
                otherView = otherTransform != null
                    ? otherTransform.GetComponent<TerrainChunkView>()
                    : null;
            }

            Bounds brushBounds = new Bounds(localHit, controller.brushSize);

            Check(
                controller.TryMine(),
                "PlayerMining identity: TryMine modifies terrain"
            );

            CheckSamplesInsideAreAir(
                chunk,
                brushBounds,
                "PlayerMining identity"
            );
            CheckSamplesOutsideUnchanged(
                chunk,
                brushBounds,
                beforeDensity,
                beforeMaterial,
                "PlayerMining identity"
            );

            Check(
                chunk.Mesh.GetInstanceID() != meshIdBefore,
                "PlayerMining identity: chunk mesh rebuilt"
            );
            Check(
                view.MeshFilter != null &&
                view.MeshFilter.sharedMesh == chunk.Mesh,
                "PlayerMining identity: MeshFilter synced"
            );
            Check(
                view.MeshCollider != null &&
                view.MeshCollider.sharedMesh == chunk.Mesh,
                "PlayerMining identity: MeshCollider synced"
            );

            if (otherChunk != null)
            {
                Check(
                    otherChunk.Mesh.GetInstanceID() == otherMeshIdBefore,
                    "PlayerMining identity: other chunk mesh unchanged"
                );

                if (otherView != null)
                {
                    Check(
                        otherView.MeshCollider != null &&
                        otherView.MeshCollider.sharedMesh == otherChunk.Mesh,
                        "PlayerMining identity: other chunk collider unchanged"
                    );
                }
            }
        }
        finally
        {
            DestroyValidationObject(controller != null ? controller.gameObject : null);
            DestroyValidationObject(camera != null ? camera.gameObject : null);
            DestroyTerrain(terrain);
        }
    }

    private static void ValidatePlayerMiningNonIdentityTransform()
    {
        DensityTerrain terrain = CreateTerrain(
            "Validation_PlayerMining_Transform",
            DensityGenerationMode.HeightField
        );
        Camera camera = null;
        PlayerMiningController controller = null;

        try
        {
            terrain.transform.position = new Vector3(10f, 5f, -3f);
            terrain.transform.rotation = Quaternion.Euler(0f, 35f, 0f);
            terrain.transform.localScale = new Vector3(1.5f, 2f, 0.75f);
            terrain.Regenerate();

            Vector3 localTarget = new Vector3(10f, 17.5f, 10f);
            Vector3 worldTarget = terrain.transform.TransformPoint(localTarget);
            Vector3 cameraLocal = localTarget + Vector3.up * 20f;
            Vector3 cameraWorld = terrain.transform.TransformPoint(cameraLocal);

            GameObject cameraObject = new GameObject(
                "Validation_PlayerMining_Camera_Transform"
            );
            camera = cameraObject.AddComponent<Camera>();
            cameraObject.transform.position = cameraWorld;
            cameraObject.transform.LookAt(worldTarget);

            controller = CreateValidationPlayerMining(
                camera,
                terrain,
                new Vector3(4f, 4f, 4f)
            );

            bool hit = controller.TryGetTerrainHit(
                out RaycastHit rayHit,
                out TerrainChunkView view
            );
            Check(
                hit && view != null,
                "PlayerMining transform: raycast hits Terrain MeshCollider"
            );
            if (!hit || view == null) return;

            Vector3 localHit = terrain.WorldToLocalPosition(rayHit.point);
            Check(
                Vector3.Distance(localHit, localTarget) < 1f,
                "PlayerMining transform: hit point converts to Terrain Local Space"
            );

            TerrainChunk chunk = view.Chunk;
            Check(
                chunk != null && chunk.Coordinate == new Vector3Int(0, 0, 0),
                "PlayerMining transform: hit maps to expected Chunk"
            );
            if (chunk == null) return;

            CaptureChunkData(
                chunk,
                out float[,,] beforeDensity,
                out MaterialType[,,] beforeMaterial
            );
            int meshIdBefore = chunk.Mesh.GetInstanceID();
            Bounds brushBounds = new Bounds(localHit, controller.brushSize);

            Check(
                controller.TryMine(),
                "PlayerMining transform: TryMine modifies terrain"
            );

            CheckSamplesInsideAreAir(
                chunk,
                brushBounds,
                "PlayerMining transform"
            );
            CheckSamplesOutsideUnchanged(
                chunk,
                brushBounds,
                beforeDensity,
                beforeMaterial,
                "PlayerMining transform"
            );

            Check(
                chunk.Mesh.GetInstanceID() != meshIdBefore,
                "PlayerMining transform: chunk mesh rebuilt"
            );
            Check(
                view.MeshFilter != null &&
                view.MeshFilter.sharedMesh == chunk.Mesh,
                "PlayerMining transform: MeshFilter synced"
            );
            Check(
                view.MeshCollider != null &&
                view.MeshCollider.sharedMesh == chunk.Mesh,
                "PlayerMining transform: MeshCollider synced"
            );
        }
        finally
        {
            DestroyValidationObject(controller != null ? controller.gameObject : null);
            DestroyValidationObject(camera != null ? camera.gameObject : null);
            DestroyTerrain(terrain);
        }
    }

    private static void ValidatePlayerMiningNoHitDoesNotModify()
    {
        DensityTerrain terrain = CreateTerrain(
            "Validation_PlayerMining_NoHit",
            DensityGenerationMode.HeightField
        );
        Camera camera = null;
        PlayerMiningController controller = null;

        try
        {
            camera = CreateValidationCamera(
                "Validation_PlayerMining_NoHit_Camera",
                new Vector3(1000f, 40f, 1000f),
                Quaternion.Euler(90f, 0f, 0f)
            );
            controller = CreateValidationPlayerMining(
                camera,
                terrain,
                new Vector3(4f, 4f, 4f)
            );

            TerrainChunk chunk = null;
            TerrainChunkView view = null;
            if (terrain.TryGetChunk(new Vector3Int(0, 0, 0), out chunk))
            {
                Transform chunkTransform = FindChunkTransform(terrain, chunk);
                view = chunkTransform != null
                    ? chunkTransform.GetComponent<TerrainChunkView>()
                    : null;
            }

            Check(chunk != null, "PlayerMining no-hit: chunk exists");
            if (chunk == null) return;

            CaptureChunkData(
                chunk,
                out float[,,] beforeDensity,
                out MaterialType[,,] beforeMaterial
            );
            int meshIdBefore = chunk.Mesh.GetInstanceID();
            int colliderMeshIdBefore = view != null &&
                                       view.MeshCollider != null &&
                                       view.MeshCollider.sharedMesh != null
                ? view.MeshCollider.sharedMesh.GetInstanceID()
                : 0;

            Check(
                !controller.TryGetTerrainHit(out _, out _),
                "PlayerMining no-hit: raycast does not hit terrain"
            );
            Check(
                !controller.TryMine(),
                "PlayerMining no-hit: TryMine returns false"
            );
            Check(
                chunk.Mesh.GetInstanceID() == meshIdBefore,
                "PlayerMining no-hit: mesh unchanged"
            );
            Check(
                ChunkDataMatches(chunk, beforeDensity, beforeMaterial),
                "PlayerMining no-hit: density/material unchanged"
            );

            if (view != null && view.MeshCollider != null)
            {
                int colliderMeshIdAfter = view.MeshCollider.sharedMesh != null
                    ? view.MeshCollider.sharedMesh.GetInstanceID()
                    : 0;
                Check(
                    colliderMeshIdAfter == colliderMeshIdBefore,
                    "PlayerMining no-hit: collider unchanged"
                );
            }
        }
        finally
        {
            DestroyValidationObject(controller != null ? controller.gameObject : null);
            DestroyValidationObject(camera != null ? camera.gameObject : null);
            DestroyTerrain(terrain);
        }
    }

    private static void ValidatePlayerMiningNonTerrainColliderRejected()
    {
        DensityTerrain terrain = CreateTerrain(
            "Validation_PlayerMining_NonTerrainCollider",
            DensityGenerationMode.HeightField
        );
        Camera camera = null;
        PlayerMiningController controller = null;
        GameObject blocker = null;

        try
        {
            camera = CreateValidationCamera(
                "Validation_PlayerMining_Blocker_Camera",
                new Vector3(10f, 40f, 10f),
                Quaternion.Euler(90f, 0f, 0f)
            );
            controller = CreateValidationPlayerMining(
                camera,
                terrain,
                new Vector3(4f, 4f, 4f)
            );

            blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            blocker.name = "Validation_PlayerMining_Blocker";
            blocker.transform.position = new Vector3(10f, 25f, 10f);
            blocker.transform.localScale = new Vector3(5f, 5f, 5f);

            Physics.SyncTransforms();

            TerrainChunk chunk = null;
            int meshIdBefore = 0;
            if (terrain.TryGetChunk(new Vector3Int(0, 0, 0), out chunk))
            {
                meshIdBefore = chunk.Mesh.GetInstanceID();
            }

            Check(
                !controller.TryGetTerrainHit(out _, out _),
                "PlayerMining blocker: non-terrain collider is rejected"
            );
            Check(
                !controller.TryMine(),
                "PlayerMining blocker: TryMine returns false"
            );

            if (chunk != null)
            {
                Check(
                    chunk.Mesh.GetInstanceID() == meshIdBefore,
                    "PlayerMining blocker: terrain mesh unchanged"
                );
            }
        }
        finally
        {
            DestroyValidationObject(blocker);
            DestroyValidationObject(controller != null ? controller.gameObject : null);
            DestroyValidationObject(camera != null ? camera.gameObject : null);
            DestroyTerrain(terrain);
        }
    }

    private static Camera CreateValidationCamera(
        string name,
        Vector3 position,
        Quaternion rotation)
    {
        GameObject gameObject = new GameObject(name);
        Camera camera = gameObject.AddComponent<Camera>();
        gameObject.transform.position = position;
        gameObject.transform.rotation = rotation;
        return camera;
    }

    private static PlayerMiningController CreateValidationPlayerMining(
        Camera camera,
        DensityTerrain terrain,
        Vector3 brushSize)
    {
        GameObject gameObject = new GameObject(
            "Validation_PlayerMiningController"
        );
        PlayerMiningController controller =
            gameObject.AddComponent<PlayerMiningController>();
        controller.targetCamera = camera;
        controller.terrain = terrain;
        controller.brushSize = brushSize;
        controller.maxDistance = 200f;
        controller.raycastMask = ~0;
        return controller;
    }

    private static void DestroyValidationObject(GameObject gameObject)
    {
        if (gameObject == null) return;
        UnityEngine.Object.DestroyImmediate(gameObject);
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private static DensityTerrain CreateTerrain(
        string name,
        DensityGenerationMode mode,
        Vector3Int chunkStartCoordinate = default(Vector3Int))
    {
        GameObject gameObject = new GameObject(name);
        gameObject.SetActive(false);

        DensityTerrain terrain = gameObject.AddComponent<DensityTerrain>();
        terrain.regenerateInEditor = false;
        terrain.voxelSize = VoxelSize;
        terrain.chunkVoxelSize = ChunkVoxelSize;
        terrain.chunkCount = ChunkCount;
        terrain.chunkStartCoordinate = chunkStartCoordinate;
        terrain.resolution = DensityResolution;
        terrain.densityMode = mode;
        terrain.debugCutaway = false;

        terrain.height = 17.5f;
        terrain.noiseScale = 0.41f;
        terrain.noiseStrength = 3.39f;

        terrain.useNoise = mode != DensityGenerationMode.ThreeDimensional;

        if (mode == DensityGenerationMode.SphereCavity)
        {
            terrain.cavityCenterOffset = new Vector3(5f, 5f, 5f);
            terrain.cavityRadius = 2f;
        }
        else
        {
            terrain.cavityCenterOffset = new Vector3(7.11f, 13.3f, 19.9f);
            terrain.cavityRadius = 6.13f;
        }

        terrain.useLegacyMaterialRules = false;
        terrain.grassDepth = 1.9f;
        terrain.dirtDepth = 0.65f;
        terrain.weatheringStrength = 0.8f;
        terrain.weatheringDepth = 8f;
        terrain.gentleSlopeAngle = 15f;
        terrain.steepSlopeAngle = 45f;
        terrain.gravityStrength = 1f;
        terrain.soilSimulationIterations = 4;
        terrain.grassStrength = 1f;

        terrain.Regenerate();
        gameObject.SetActive(true);

        return terrain;
    }

    private static void DestroyTerrain(DensityTerrain terrain)
    {
        if (terrain == null) return;
        UnityEngine.Object.DestroyImmediate(terrain.gameObject);
    }

    private static Vector3 GetChunkLocalCenter(Vector3Int coordinate)
    {
        return new Vector3(
            (coordinate.x + 0.5f) * ChunkWorldSize.x,
            (coordinate.y + 0.5f) * ChunkWorldSize.y,
            (coordinate.z + 0.5f) * ChunkWorldSize.z
        );
    }

    private static Transform FindChunkTransform(
        DensityTerrain terrain,
        TerrainChunk chunk)
    {
        string name =
            $"Chunk_{chunk.Coordinate.x}_{chunk.Coordinate.y}_{chunk.Coordinate.z}";
        return terrain.transform.Find(name);
    }

    private static float SampleDensityAtLocal(
        DensityField field,
        Vector3 terrainLocalPosition)
    {
        int x = Mathf.Clamp(
            Mathf.RoundToInt((terrainLocalPosition.x - field.Origin.x) / field.CellSizeX),
            0,
            field.SampleCountX - 1
        );
        int y = Mathf.Clamp(
            Mathf.RoundToInt((terrainLocalPosition.y - field.Origin.y) / field.CellSizeY),
            0,
            field.SampleCountY - 1
        );
        int z = Mathf.Clamp(
            Mathf.RoundToInt((terrainLocalPosition.z - field.Origin.z) / field.CellSizeZ),
            0,
            field.SampleCountZ - 1
        );

        return field[x, y, z];
    }

    private static bool TryFindSolidSample(
        DensityField field,
        out int x,
        out int y,
        out int z)
    {
        for (x = 0; x < field.SampleCountX; x++)
        {
            for (y = 0; y < field.SampleCountY; y++)
            {
                for (z = 0; z < field.SampleCountZ; z++)
                {
                    if (field[x, y, z] > 1f)
                    {
                        return true;
                    }
                }
            }
        }

        x = 0;
        y = 0;
        z = 0;
        return false;
    }

    private static void Check(bool condition, string label)
    {
        summary.Add((condition ? "PASS" : "FAIL") + " - " + label);

        if (!condition)
        {
            failures++;
            Debug.LogError("[TerrainMultiChunkValidation] FAIL - " + label);
        }
        else
        {
            Debug.Log("[TerrainMultiChunkValidation] PASS - " + label);
        }
    }
}
