using TerrainModificationV2;
using UnityEditor;
using UnityEngine;

/// <summary>
/// TerrainVoxelMeshIntegrationValidation（Step 3D Runtime）。
/// 验证 Model B：
///   F → VoxelField.Set(Air)
///     → Affected Chunk(s)
///     → Base Density + VoxelField
///     → Effective Density
///     → TerrainMeshGenerator
///     → TerrainChunk.Mesh
///     → MeshFilter + MeshCollider
/// 不调用旧 Mining，不修改 Base Density / MaterialField。
/// </summary>
public static class TerrainVoxelMeshIntegrationValidation
{
    private const float Epsilon = 0.00001f;

    private static int failures;
    private static int checks;

    [MenuItem("Tools/Terrain/Validate Terrain Voxel Mesh Integration")]
    public static void ValidateFromMenu()
    {
        ValidateAll();
        LogSummary();

        Debug.Log(
            "[TerrainVoxelMeshIntegrationValidation] Menu validation result = " +
            (failures == 0 ? "PASS" : "FAIL")
        );
    }

    /// <summary>Batchmode 入口：-executeMethod TerrainVoxelMeshIntegrationValidation.RunBatch。</summary>
    public static void RunBatch()
    {
        ValidateAll();
        LogSummary();
        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    private static void ValidateAll()
    {
        failures = 0;
        checks = 0;

        ValidateInitialBaseTerrain();
        ValidateSingleVoxelEdit();
        ValidateAdjacentVoxelEdits();
        ValidateContinuousAndWallEdits();
        ValidateVerticalWallEdit();
        ValidateChunkMappingAndBoundaryRebuild();
        ValidateLegacyIsolationViaController();
    }

    private static void ValidateInitialBaseTerrain()
    {
        GameObject terrainObject = CreateTemporaryTerrain(
            1,
            out DensityTerrain terrain
        );

        try
        {
            TerrainChunkView view = GetOnlyChunkView(terrainObject);
            if (view == null) return;

            DensityField baseField = view.Chunk.Density;
            DensityField effectiveField = terrain.BuildEffectiveDensityField(
                view.Chunk
            );

            Check(
                baseField != null && effectiveField != null,
                "Case 1: Base and Effective DensityField exist"
            );

            if (baseField == null || effectiveField == null)
            {
                return;
            }

            Check(
                terrain.VoxelField.StoredVoxelCount == 0,
                "Case 1: no excavation edits in VoxelField"
            );

            bool allSamplesEqual = true;
            for (int x = 0; x < baseField.SampleCountX; x++)
            {
                for (int y = 0; y < baseField.SampleCountY; y++)
                {
                    for (int z = 0; z < baseField.SampleCountZ; z++)
                    {
                        if (Mathf.Abs(effectiveField[x, y, z] - baseField[x, y, z]) > Epsilon)
                        {
                            allSamplesEqual = false;
                        }
                    }
                }
            }

            Check(
                allSamplesEqual,
                "Case 1: Effective Density == Base Density when VoxelField has no edits"
            );

            Check(
                view.Chunk.Mesh != null && view.Chunk.Mesh.vertexCount > 0,
                "Case 1: initial HeightField Chunk Mesh has geometry"
            );

            CheckViewMeshConsistency(view, "Case 1");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    private static void ValidateSingleVoxelEdit()
    {
        GameObject terrainObject = CreateTemporaryTerrain(
            1,
            out DensityTerrain terrain
        );

        try
        {
            TerrainChunkView view = GetOnlyChunkView(terrainObject);
            if (view == null) return;

            Vector3Int voxel = new Vector3Int(8, 5, 8);
            DensityField baseField = view.Chunk.Density;
            float baseSampleBefore = baseField[8, 5, 8];
            Mesh meshBefore = view.Chunk.Mesh;

            Check(
                terrain.VoxelField.Get(voxel) == VoxelState.Solid,
                "Case 2: Voxel(8,5,8) starts as no-edit"
            );

            terrain.VoxelField.Set(voxel, VoxelState.Air);

            float rebuildStart = Time.realtimeSinceStartup;
            int rebuilt = terrain.RebuildChunksAffectedByVoxel(voxel);
            float elapsedMs = (Time.realtimeSinceStartup - rebuildStart) * 1000f;

            Debug.Log(
                "[TerrainVoxelMeshIntegrationValidation] single voxel rebuild: " +
                "affectedChunks=" + rebuilt +
                " elapsedMs=" + elapsedMs.ToString("F3")
            );

            Check(rebuilt >= 1, "Case 2: at least one affected Chunk was rebuilt");
            Check(
                terrain.VoxelField.IsAir(voxel),
                "Case 2: VoxelField contains the excavation edit"
            );

            DensityField effectiveField = terrain.BuildEffectiveDensityField(
                view.Chunk
            );

            Vector3 voxelMin = terrain.VoxelField.GetVoxelMin(voxel);
            Vector3 voxelMax = terrain.VoxelField.GetVoxelMax(voxel);

            int affectedSamples = 0;
            bool outsidePreserved = true;

            for (int x = 0; x < baseField.SampleCountX; x++)
            {
                for (int y = 0; y < baseField.SampleCountY; y++)
                {
                    for (int z = 0; z < baseField.SampleCountZ; z++)
                    {
                        Vector3 p = baseField.GetLocalPosition(x, y, z);
                        bool insideVoxel =
                            p.x >= voxelMin.x && p.x < voxelMax.x &&
                            p.y >= voxelMin.y && p.y < voxelMax.y &&
                            p.z >= voxelMin.z && p.z < voxelMax.z;

                        if (insideVoxel)
                        {
                            affectedSamples++;
                            if (Mathf.Abs(
                                effectiveField[x, y, z] -
                                VoxelDensityAdapter.AirDensity) > Epsilon)
                            {
                                outsidePreserved = false;
                            }
                        }
                        else if (Mathf.Abs(
                            effectiveField[x, y, z] -
                            baseField[x, y, z]) > Epsilon)
                        {
                            outsidePreserved = false;
                        }
                    }
                }
            }

            Check(
                affectedSamples > 0 && outsidePreserved,
                "Case 2: only samples inside the Air Voxel are overridden to AirDensity"
            );

            Check(
                Mathf.Abs(
                    baseField[8, 5, 8] -
                    baseSampleBefore) < Epsilon,
                "Case 2: Base Density is not mutated"
            );

            Check(
                view.Chunk.Mesh != meshBefore,
                "Case 2: Chunk Mesh was regenerated"
            );

            CheckViewMeshConsistency(view, "Case 2");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    private static void ValidateAdjacentVoxelEdits()
    {
        GameObject terrainObject = CreateTemporaryTerrain(
            1,
            out DensityTerrain terrain
        );

        try
        {
            TerrainChunkView view = GetOnlyChunkView(terrainObject);
            if (view == null) return;

            Vector3Int voxelA = new Vector3Int(8, 5, 8);
            Vector3Int voxelB = new Vector3Int(9, 5, 8);

            terrain.VoxelField.Set(voxelA, VoxelState.Air);
            terrain.VoxelField.Set(voxelB, VoxelState.Air);
            int rebuilt = terrain.RebuildChunksAffectedByVoxel(voxelA);
            rebuilt += terrain.RebuildChunksAffectedByVoxel(voxelB);

            Check(
                rebuilt >= 1 &&
                terrain.VoxelField.IsAir(voxelA) &&
                terrain.VoxelField.IsAir(voxelB),
                "Case 3: adjacent Voxels (8,5,8) and (9,5,8) are both Air"
            );

            DensityField effective = terrain.BuildEffectiveDensityField(
                view.Chunk
            );

            // resolution=16 时 sample 间距为 1m，两个相邻 Air Voxel 之间
            // 的 sample 全部应为 Air，不会留下内部 Solid wall sample。
            bool sharedFaceAir = true;
            int yIndex = Mathf.RoundToInt((5f - effective.Origin.y) / effective.CellSizeY);
            int zIndex = Mathf.RoundToInt((8f - effective.Origin.z) / effective.CellSizeZ);
            int x0 = Mathf.RoundToInt((8f - effective.Origin.x) / effective.CellSizeX);
            int x1 = Mathf.RoundToInt((9f - effective.Origin.x) / effective.CellSizeX);

            if (Mathf.Abs(effective[x0, yIndex, zIndex] - VoxelDensityAdapter.AirDensity) > Epsilon ||
                Mathf.Abs(effective[x1, yIndex, zIndex] - VoxelDensityAdapter.AirDensity) > Epsilon)
            {
                sharedFaceAir = false;
            }

            Check(
                sharedFaceAir,
                "Case 3: no false Solid sample wall remains between adjacent Air Voxels"
            );

            CheckViewMeshConsistency(view, "Case 3");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    private static void ValidateContinuousAndWallEdits()
    {
        GameObject terrainObject = CreateTemporaryTerrain(
            1,
            out DensityTerrain terrain
        );

        try
        {
            TerrainChunkView view = GetOnlyChunkView(terrainObject);
            if (view == null) return;

            Vector3Int[] forwardVoxels =
            {
                new Vector3Int(8, 5, 8),
                new Vector3Int(9, 5, 8),
                new Vector3Int(10, 5, 8),
                new Vector3Int(11, 5, 8)
            };

            for (int i = 0; i < forwardVoxels.Length; i++)
            {
                terrain.VoxelField.Set(forwardVoxels[i], VoxelState.Air);
                terrain.RebuildChunksAffectedByVoxel(forwardVoxels[i]);
            }

            bool forwardAllAir = true;
            for (int i = 0; i < forwardVoxels.Length; i++)
            {
                if (!terrain.VoxelField.IsAir(forwardVoxels[i]))
                {
                    forwardAllAir = false;
                }
            }

            Check(
                forwardAllAir,
                "Case 4: four consecutive forward Voxel edits are all recorded in VoxelField"
            );

            Vector3Int leftWall = new Vector3Int(8, 5, 7);
            Vector3Int rightWall = new Vector3Int(8, 5, 9);
            terrain.VoxelField.Set(leftWall, VoxelState.Air);
            terrain.RebuildChunksAffectedByVoxel(leftWall);
            terrain.VoxelField.Set(rightWall, VoxelState.Air);
            terrain.RebuildChunksAffectedByVoxel(rightWall);

            Check(
                terrain.VoxelField.IsAir(leftWall) &&
                terrain.VoxelField.IsAir(rightWall),
                "Case 5: left and right wall Voxels can be edited independently"
            );

            Check(
                terrain.VoxelField.Get(new Vector3Int(8, 5, 8)) == VoxelState.Air,
                "Case 5: prior tunnel Voxel remains Air after wall edits"
            );

            CheckViewMeshConsistency(view, "Case 5");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    private static void ValidateVerticalWallEdit()
    {
        GameObject terrainObject = CreateTemporaryTerrain(
            1,
            out DensityTerrain terrain
        );

        try
        {
            TerrainChunkView view = GetOnlyChunkView(terrainObject);
            if (view == null) return;

            // HeightField surface 约 y=8；Voxel(8,7,8) 位于垂直墙面的实体一侧。
            Vector3Int voxel = new Vector3Int(8, 7, 8);
            terrain.VoxelField.Set(voxel, VoxelState.Air);
            int rebuilt = terrain.RebuildChunksAffectedByVoxel(voxel);

            Check(rebuilt >= 1, "Case 6: vertical-wall Voxel edit triggers Chunk rebuild");
            Check(terrain.VoxelField.IsAir(voxel), "Case 6: vertical-wall Voxel is Air");

            DensityField effective = terrain.BuildEffectiveDensityField(view.Chunk);
            Vector3 p = new Vector3(8f, 7f, 8f);
            Vector3Int mappedVoxel = terrain.VoxelField.PositionToVoxel(p);
            int x = Mathf.RoundToInt((p.x - effective.Origin.x) / effective.CellSizeX);
            int y = Mathf.RoundToInt((p.y - effective.Origin.y) / effective.CellSizeY);
            int z = Mathf.RoundToInt((p.z - effective.Origin.z) / effective.CellSizeZ);

            Check(
                mappedVoxel == voxel &&
                Mathf.Abs(effective[x, y, z] - VoxelDensityAdapter.AirDensity) < Epsilon,
                "Case 6: Effective Density uses VoxelField membership at the wall sample"
            );

            CheckViewMeshConsistency(view, "Case 6");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    private static void ValidateChunkMappingAndBoundaryRebuild()
    {
        Vector3Int chunkSize = new Vector3Int(16, 16, 16);

        Check(
            ChunkSpace.VoxelToChunk(new Vector3Int(15, 0, 0), chunkSize) ==
            new Vector3Int(0, 0, 0),
            "Case 7 mapping: Voxel 15 -> Chunk 0"
        );
        Check(
            ChunkSpace.VoxelToChunk(new Vector3Int(16, 0, 0), chunkSize) ==
            new Vector3Int(1, 0, 0),
            "Case 7 mapping: Voxel 16 -> Chunk 1"
        );
        Check(
            ChunkSpace.VoxelToChunk(new Vector3Int(31, 0, 0), chunkSize) ==
            new Vector3Int(1, 0, 0),
            "Case 7 mapping: Voxel 31 -> Chunk 1"
        );
        Check(
            ChunkSpace.VoxelToChunk(new Vector3Int(32, 0, 0), chunkSize) ==
            new Vector3Int(2, 0, 0),
            "Case 7 mapping: Voxel 32 -> Chunk 2"
        );
        Check(
            ChunkSpace.VoxelToChunk(new Vector3Int(-1, 0, 0), chunkSize) ==
            new Vector3Int(-1, 0, 0),
            "Case 7 mapping: Voxel -1 -> Chunk -1"
        );
        Check(
            ChunkSpace.VoxelToChunk(new Vector3Int(-16, 0, 0), chunkSize) ==
            new Vector3Int(-1, 0, 0),
            "Case 7 mapping: Voxel -16 -> Chunk -1"
        );
        Check(
            ChunkSpace.VoxelToChunk(new Vector3Int(-17, 0, 0), chunkSize) ==
            new Vector3Int(-2, 0, 0),
            "Case 7 mapping: Voxel -17 -> Chunk -2"
        );

        GameObject terrainObject = CreateTemporaryTerrain(
            2,
            out DensityTerrain terrain
        );

        try
        {
            TerrainChunkView[] views =
                terrainObject.GetComponentsInChildren<TerrainChunkView>(true);

            Check(
                views != null && views.Length == 2,
                "Case 7 runtime: two formal 16m Chunks exist"
            );

            if (views == null || views.Length != 2) return;

            TerrainChunkView chunk0 = FindChunkView(views, new Vector3Int(0, 0, 0));
            TerrainChunkView chunk1 = FindChunkView(views, new Vector3Int(1, 0, 0));

            Mesh mesh0Before = chunk0.Chunk.Mesh;
            Mesh mesh1Before = chunk1.Chunk.Mesh;

            terrain.VoxelField.Set(new Vector3Int(15, 0, 0), VoxelState.Air);
            int rebuilt15 = terrain.RebuildChunksAffectedByVoxel(
                new Vector3Int(15, 0, 0)
            );

            Check(
                rebuilt15 == 1 &&
                chunk0.Chunk.Mesh != mesh0Before &&
                chunk1.Chunk.Mesh == mesh1Before,
                "Case 7 runtime: Voxel 15 rebuilds only Chunk 0"
            );

            terrain.VoxelField.Set(new Vector3Int(16, 0, 0), VoxelState.Air);
            int rebuilt16 = terrain.RebuildChunksAffectedByVoxel(
                new Vector3Int(16, 0, 0)
            );

            Check(
                rebuilt16 == 2 &&
                chunk0.Chunk.Mesh != mesh0Before &&
                chunk1.Chunk.Mesh != mesh1Before,
                "Case 7 runtime: boundary Voxel 16 rebuilds both Chunk 0 and Chunk 1"
            );

            CheckViewMeshConsistency(chunk0, "Case 7 chunk0");
            CheckViewMeshConsistency(chunk1, "Case 7 chunk1");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    private static void ValidateLegacyIsolationViaController()
    {
        GameObject terrainObject = CreateTemporaryTerrain(
            1,
            out DensityTerrain terrain
        );
        GameObject controllerObject = new GameObject(
            "TerrainVoxelMeshIntegrationValidationController"
        );
        controllerObject.SetActive(false);
        PlayerMiningController controller =
            controllerObject.AddComponent<PlayerMiningController>();

        try
        {
            TerrainChunkView view = GetOnlyChunkView(terrainObject);
            if (view == null) return;

            SetControllerSelection(
                controller,
                terrain,
                new Vector3Int(8, 5, 8)
            );

            controller.TryMineVoxel();

            Check(
                terrain.VoxelField.IsAir(new Vector3Int(8, 5, 8)),
                "Case 11: TryMineVoxel writes VoxelField"
            );

            Check(
                terrain.EditLayer.Count == 0,
                "Case 11: TryMineVoxel does not write TerrainEditLayer"
            );

            Check(
                view.Chunk.Density != null,
                "Case 11: Base Density still exists after F"
            );

            CheckViewMeshConsistency(view, "Case 11");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(controllerObject);
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    private static GameObject CreateTemporaryTerrain(
        int chunkCountX,
        out DensityTerrain terrain)
    {
        GameObject terrainObject = new GameObject(
            "TerrainVoxelMeshIntegrationValidationTerrain"
        );

        terrainObject.SetActive(false);
        terrain = terrainObject.AddComponent<DensityTerrain>();
        terrainObject.transform.position = Vector3.zero;
        terrainObject.transform.rotation = Quaternion.identity;
        terrainObject.transform.localScale = Vector3.one;

        // 使用正式契约 VoxelSize=1m / ChunkVoxelSize=16。
        terrain.voxelSize = 1f;
        terrain.chunkVoxelSize = new Vector3Int(16, 16, 16);
        terrain.chunkCount = new Vector3Int(chunkCountX, 1, 1);
        terrain.chunkStartCoordinate = Vector3Int.zero;
        terrain.resolution = 16;
        terrain.densityMode = DensityGenerationMode.HeightField;
        terrain.useNoise = false;
        terrain.height = 8f;
        terrain.regenerateInEditor = false;
        terrain.Regenerate();

        return terrainObject;
    }

    private static TerrainChunkView GetOnlyChunkView(GameObject terrainObject)
    {
        TerrainChunkView[] views =
            terrainObject.GetComponentsInChildren<TerrainChunkView>(true);

        Check(
            views != null && views.Length == 1,
            "Temporary terrain has exactly one Chunk"
        );

        return views != null && views.Length == 1 ? views[0] : null;
    }

    private static TerrainChunkView FindChunkView(
        TerrainChunkView[] views,
        Vector3Int coordinate)
    {
        for (int i = 0; i < views.Length; i++)
        {
            if (views[i].Coordinate == coordinate)
            {
                return views[i];
            }
        }

        return null;
    }

    private static void CheckViewMeshConsistency(
        TerrainChunkView view,
        string label)
    {
        if (view == null || view.Chunk == null)
        {
            Check(false, label + ": ChunkView/Chunk exists");
            return;
        }

        Check(
            view.MeshFilter != null &&
            view.MeshFilter.sharedMesh == view.Chunk.Mesh,
            label + ": MeshFilter.sharedMesh == TerrainChunk.Mesh"
        );

        Check(
            view.MeshCollider != null &&
            view.MeshCollider.sharedMesh == view.Chunk.Mesh,
            label + ": MeshCollider.sharedMesh == TerrainChunk.Mesh"
        );
    }

    private static void SetControllerSelection(
        PlayerMiningController controller,
        DensityTerrain terrain,
        Vector3Int voxel)
    {
        System.Reflection.FieldInfo hasSelected = typeof(PlayerMiningController)
            .GetField(
                "hasSelectedVoxel",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic
            );

        System.Reflection.FieldInfo selectedVoxel = typeof(PlayerMiningController)
            .GetField(
                "selectedVoxel",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic
            );

        System.Reflection.FieldInfo selectedTerrain = typeof(PlayerMiningController)
            .GetField(
                "selectedTerrain",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic
            );

        // A 阶段：生产路径的选择一定同时携带命中点/法线（TryMineVoxel 挖掘前用它复验）。
        // 测试通过反射直接注入选择，因此这里补上与目标 Voxel 一致的命中数据：
        // 取该 Voxel 的 +Y 面平面上一点 + 外向法线 —— 沿法线反向偏移
        // VoxelSize×1e-3 后仍落在同一 Voxel 内，解析结果就是目标 Voxel 本身。
        System.Reflection.FieldInfo hitWorld = typeof(PlayerMiningController)
            .GetField(
                "selectedRaycastHitWorld",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic
            );

        System.Reflection.FieldInfo hitNormal = typeof(PlayerMiningController)
            .GetField(
                "selectedRaycastHitNormal",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic
            );

        if (hasSelected != null && selectedVoxel != null && selectedTerrain != null &&
            hitWorld != null && hitNormal != null)
        {
            float voxelSize = terrain.VoxelField != null
                ? terrain.VoxelField.VoxelSize
                : 1f;
            Vector3 localPoint = new Vector3(
                (voxel.x + 0.5f) * voxelSize,
                (voxel.y + 1f) * voxelSize,
                (voxel.z + 0.5f) * voxelSize
            );

            hasSelected.SetValue(controller, true);
            selectedVoxel.SetValue(controller, voxel);
            selectedTerrain.SetValue(controller, terrain);
            hitWorld.SetValue(
                controller,
                terrain.transform.TransformPoint(localPoint)
            );
            hitNormal.SetValue(
                controller,
                terrain.transform.TransformDirection(Vector3.up)
            );
        }
    }

    private static void Check(bool condition, string message)
    {
        checks++;

        if (condition)
        {
            Debug.Log("PASS - " + message);
            return;
        }

        failures++;
        Debug.LogError("FAIL - " + message);
    }

    private static void LogSummary()
    {
        Debug.Log(
            "[TerrainVoxelMeshIntegrationValidation] ===== SUMMARY =====\n" +
            "Checks = " + checks + "\n" +
            "Failures = " + failures + "\n" +
            "Result = " + (failures == 0 ? "PASS" : "FAIL")
        );
    }
}
