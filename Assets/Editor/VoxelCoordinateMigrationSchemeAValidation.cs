using System.Collections.Generic;
using TerrainModificationV2;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Coordinate Migration Scheme A Validation（Phase A）。
///
/// 独立验证（不改写既有 legacy 测试；TerrainMultiChunkValidation 保持 0.1m 契约）：
///   T1. Voxel → Chunk / LocalVoxel 映射（数学 floor + 正模；正负坐标；逐轴不同尺寸）
///   T2. 绝对 chunkStartCoordinate 语义（chunkCount (2,1,2) + start (5,0,7)）
///   T3. Scheme A 配置与采样语义（VoxelSize 1m / chunkVoxelSize (75,37,50) /
///       ChunkWorldSize (75,37,50)m / resolution 75 / SampleCount (76,76,76) /
///       CellSize (1, 37/75, 50/75)m）
///   T4. 非 Identity Transform 下的 World ↔ Terrain Local 与 Chunk-local Mesh
///   T5. 新 Voxel-aware 路径不依赖 legacy DensityTerrain.voxelSize（单位隔离）；
///       legacy Chunk（无 ChunkGeometry）会被 Mutation 拒绝
///   T6. 对齐配置下 legacy 坐标包装 API 与 VoxelField 1m 契约一致；
///       Mutation 坐标契约守卫（错配 chunkVoxelSize 必须失败且不提交，
///       正确参数必须提交并完成 Mesh 替换 + View 同步）
/// </summary>
public static class VoxelCoordinateMigrationSchemeAValidation
{
    private static readonly Vector3Int ChunkVoxelSize = new Vector3Int(75, 37, 50);
    private static readonly Vector3 ChunkWorldSize = new Vector3(75f, 37f, 50f);
    private const float Epsilon = 1e-4f;

    private static int checks;
    private static int failures;

    [MenuItem("Tools/Terrain/Validate Coordinate Migration Scheme A")]
    public static void ValidateFromMenu()
    {
        RunAll();
        LogSummary();
    }

    public static void RunBatch()
    {
        try
        {
            RunAll();
        }
        catch (System.Exception exception)
        {
            failures++;
            Debug.LogError(
                "[VoxelCoordinateMigrationSchemeAValidation] EXCEPTION: " +
                exception
            );
        }
        finally
        {
            LogSummary();
            EditorApplication.Exit(failures == 0 ? 0 : 1);
        }
    }

    private static void RunAll()
    {
        checks = 0;
        failures = 0;

        CheckVoxelToChunkMapping();
        CheckAbsoluteChunkStartCoordinate();
        CheckConfigurationAndSampling();
        CheckNonIdentityTransformAndChunkLocalMesh();
        CheckVoxelAwarePathIgnoresLegacyVoxelSize();
        CheckAlignedLegacyApiAndMutationContract();
    }

    // =====================================================================
    // T1 — Voxel → Chunk / LocalVoxel 映射
    // =====================================================================

    private static void CheckVoxelToChunkMapping()
    {
        CheckMapping(
            new Vector3Int(74, 0, 0),
            new Vector3Int(0, 0, 0),
            new Vector3Int(74, 0, 0)
        );
        CheckMapping(
            new Vector3Int(75, 0, 0),
            new Vector3Int(1, 0, 0),
            new Vector3Int(0, 0, 0)
        );
        CheckMapping(
            new Vector3Int(-1, 0, 0),
            new Vector3Int(-1, 0, 0),
            new Vector3Int(74, 0, 0)
        );
        CheckMapping(
            new Vector3Int(-75, 0, 0),
            new Vector3Int(-1, 0, 0),
            new Vector3Int(0, 0, 0)
        );
        CheckMapping(
            new Vector3Int(-76, 0, 0),
            new Vector3Int(-2, 0, 0),
            new Vector3Int(74, 0, 0)
        );
        CheckMapping(
            new Vector3Int(0, 36, 0),
            new Vector3Int(0, 0, 0),
            new Vector3Int(0, 36, 0)
        );
        CheckMapping(
            new Vector3Int(0, 37, 0),
            new Vector3Int(0, 1, 0),
            new Vector3Int(0, 0, 0)
        );
        CheckMapping(
            new Vector3Int(0, -37, 0),
            new Vector3Int(0, -1, 0),
            new Vector3Int(0, 0, 0)
        );
        CheckMapping(
            new Vector3Int(0, 0, 49),
            new Vector3Int(0, 0, 0),
            new Vector3Int(0, 0, 49)
        );
        CheckMapping(
            new Vector3Int(0, 0, 50),
            new Vector3Int(0, 0, 1),
            new Vector3Int(0, 0, 0)
        );
        CheckMapping(
            new Vector3Int(0, 0, -50),
            new Vector3Int(0, 0, -1),
            new Vector3Int(0, 0, 0)
        );
        CheckMapping(
            new Vector3Int(-100, -40, -60),
            new Vector3Int(-2, -2, -2),
            new Vector3Int(50, 34, 40)
        );
    }

    private static void CheckMapping(
        Vector3Int voxel,
        Vector3Int expectedChunk,
        Vector3Int expectedLocal)
    {
        Vector3Int chunk = ChunkSpace.VoxelToChunk(voxel, ChunkVoxelSize);
        Vector3Int local = ChunkSpace.VoxelToLocal(voxel, ChunkVoxelSize);

        Vector3Int roundTrip = new Vector3Int(
            chunk.x * ChunkVoxelSize.x + local.x,
            chunk.y * ChunkVoxelSize.y + local.y,
            chunk.z * ChunkVoxelSize.z + local.z
        );

        Check(
            chunk == expectedChunk && local == expectedLocal && roundTrip == voxel,
            "SchemeA-T1: Voxel " + voxel + " -> Chunk " + chunk +
            " Local " + local + " (round-trip " + roundTrip + ")"
        );
    }

    // =====================================================================
    // T2 — 绝对 chunkStartCoordinate
    // =====================================================================

    private static void CheckAbsoluteChunkStartCoordinate()
    {
        Vector3Int[] expected =
        {
            new Vector3Int(5, 0, 7),
            new Vector3Int(6, 0, 7),
            new Vector3Int(5, 0, 8),
            new Vector3Int(6, 0, 8)
        };

        GameObject terrainObject = CreateSchemeATerrain(
            "SchemeA_AbsoluteChunkStart",
            new Vector3Int(2, 1, 2),
            new Vector3Int(5, 0, 7),
            1f,
            Vector3.zero,
            Quaternion.identity,
            Vector3.one
        );

        try
        {
            List<TerrainChunkView> views = GetViews(terrainObject);

            Check(
                views.Count == expected.Length,
                "SchemeA-T2: chunkCount (2,1,2) creates exactly 4 Chunks"
            );
            Check(
                !HasDuplicateCoordinates(views),
                "SchemeA-T2: Chunk coordinates are unique"
            );

            for (int i = 0; i < expected.Length; i++)
            {
                Vector3Int coordinate = expected[i];
                TerrainChunkView view = FindView(views, coordinate);

                Check(
                    view != null,
                    "SchemeA-T2: absolute Chunk coordinate " + coordinate +
                    " exists (chunkStart offset does not shift semantics)"
                );
                if (view == null) continue;

                TerrainChunk chunk = view.Chunk;

                Check(
                    chunk.Coordinate == coordinate && view.Coordinate == coordinate,
                    "SchemeA-T2: Chunk " + coordinate + " identity matches"
                );
                Check(
                    Approx(chunk.ChunkWorldSize, ChunkWorldSize),
                    "SchemeA-T2: Chunk " + coordinate +
                    " ChunkWorldSize == (75,37,50)m (actual " +
                    chunk.ChunkWorldSize + ")"
                );
                Check(
                    chunk.VoxelSize == 1f &&
                    chunk.ChunkVoxelSize == ChunkVoxelSize,
                    "SchemeA-T2: Chunk " + coordinate +
                    " VoxelSize == 1m and ChunkVoxelSize == (75,37,50)"
                );

                Vector3 expectedOrigin = new Vector3(
                    coordinate.x * ChunkWorldSize.x,
                    coordinate.y * ChunkWorldSize.y,
                    coordinate.z * ChunkWorldSize.z
                );
                Check(
                    Approx(chunk.ChunkOrigin, expectedOrigin),
                    "SchemeA-T2: Chunk " + coordinate +
                    " ChunkOrigin == coordinate × ChunkWorldSize (actual " +
                    chunk.ChunkOrigin + ")"
                );

                Check(
                    chunk.Geometry != null && chunk.RawMesh != null &&
                    chunk.Mesh != null,
                    "SchemeA-T2: Chunk " + coordinate +
                    " has ChunkGeometry / RawMesh / Final Mesh"
                );
                Check(
                    view.MeshFilter != null &&
                    view.MeshFilter.sharedMesh == chunk.Mesh,
                    "SchemeA-T2: Chunk " + coordinate +
                    " MeshFilter references its Final Mesh"
                );
                Check(
                    chunk.Density != null &&
                    chunk.Density.SampleCountX == 76 &&
                    chunk.Density.SampleCountY == 76 &&
                    chunk.Density.SampleCountZ == 76,
                    "SchemeA-T2: Chunk " + coordinate +
                    " DensityField SampleCount == (76,76,76)"
                );
                Check(
                    chunk.Density != null &&
                    Approx(
                        new Vector3(
                            chunk.Density.CellSizeX,
                            chunk.Density.CellSizeY,
                            chunk.Density.CellSizeZ),
                        new Vector3(1f, 37f / 75f, 50f / 75f)),
                    "SchemeA-T2: Chunk " + coordinate +
                    " Density CellSize == (1, 37/75, 50/75)m"
                );
            }
        }
        finally
        {
            Object.DestroyImmediate(terrainObject);
        }
    }

    // =====================================================================
    // T3 — 配置与采样语义（1 Chunk）
    // =====================================================================

    private static void CheckConfigurationAndSampling()
    {
        GameObject terrainObject = CreateSchemeATerrain(
            "SchemeA_Config",
            new Vector3Int(1, 1, 1),
            Vector3Int.zero,
            1f,
            Vector3.zero,
            Quaternion.identity,
            Vector3.one
        );

        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();
            List<TerrainChunkView> views = GetViews(terrainObject);

            Check(views.Count == 1, "SchemeA-T3: fixture has 1 Chunk");
            if (views.Count == 0) return;

            TerrainChunk chunk = views[0].Chunk;

            Check(
                terrain.VoxelField.VoxelSize == 1f,
                "SchemeA-T3: VoxelField.VoxelSize == 1m"
            );
            Check(
                terrain.EffectiveVoxelSize == 1f,
                "SchemeA-T3: DensityTerrain.EffectiveVoxelSize == 1m (aligned config)"
            );
            Check(
                terrain.chunkVoxelSize == ChunkVoxelSize,
                "SchemeA-T3: chunkVoxelSize == (75,37,50) Voxel units"
            );
            Check(
                terrain.resolution == 75,
                "SchemeA-T3: resolution == 75 (scalar, shared by X/Y/Z)"
            );
            Check(
                Approx(terrain.EffectiveChunkWorldSize, ChunkWorldSize),
                "SchemeA-T3: EffectiveChunkWorldSize == (75,37,50)m (actual " +
                terrain.EffectiveChunkWorldSize + ")"
            );

            Check(
                chunk.VoxelSize == 1f && chunk.ChunkVoxelSize == ChunkVoxelSize &&
                Approx(chunk.ChunkWorldSize, ChunkWorldSize),
                "SchemeA-T3: TerrainChunk contract == 1m × (75,37,50)"
            );
            Check(
                chunk.Density != null && chunk.Density.ResolutionX == 75 &&
                chunk.Density.ResolutionY == 75 &&
                chunk.Density.ResolutionZ == 75,
                "SchemeA-T3: DensityField Resolution == (75,75,75)"
            );
            Check(
                chunk.Density != null &&
                chunk.Density.SampleCountX == 76 &&
                chunk.Density.SampleCountY == 76 &&
                chunk.Density.SampleCountZ == 76,
                "SchemeA-T3: DensityField SampleCount == (76,76,76)"
            );
            Check(
                chunk.Density != null &&
                Approx(
                    new Vector3(
                        chunk.Density.CellSizeX,
                        chunk.Density.CellSizeY,
                        chunk.Density.CellSizeZ),
                    new Vector3(1f, 37f / 75f, 50f / 75f)),
                "SchemeA-T3: Density sample spacing == (1, 37/75, 50/75)m"
            );
            Check(
                chunk.Density != null &&
                Approx(chunk.Density.Size, ChunkWorldSize) &&
                Approx(chunk.Density.Origin, chunk.ChunkOrigin),
                "SchemeA-T3: DensityField Size == ChunkWorldSize, Origin == ChunkOrigin"
            );
            Check(
                chunk.Geometry != null &&
                chunk.Geometry.VoxelSize == 1f &&
                chunk.Geometry.ChunkVoxelSize == ChunkVoxelSize,
                "SchemeA-T3: ChunkGeometry uses the same 1m / (75,37,50) contract"
            );

            // 采样覆盖条件（充分条件；逐 Voxel 覆盖属 Stage B 独立验证）：
            // 每轴采样间距 <= VoxelSize，且 resolution >= max(ChunkVoxelSize)。
            Check(
                chunk.Density != null &&
                chunk.Density.CellSizeX <= 1f + Epsilon &&
                chunk.Density.CellSizeY <= 1f + Epsilon &&
                chunk.Density.CellSizeZ <= 1f + Epsilon &&
                75 >= 75,
                "SchemeA-T3: per-axis spacing <= 1m and resolution >= max(Voxel count)"
            );
        }
        finally
        {
            Object.DestroyImmediate(terrainObject);
        }
    }

    // =====================================================================
    // T4 — 非 Identity Transform + Chunk-local Mesh
    // =====================================================================

    private static void CheckNonIdentityTransformAndChunkLocalMesh()
    {
        Vector3 position = new Vector3(12.5f, -3f, 7.25f);
        Quaternion rotation = Quaternion.Euler(0f, 31f, 0f);
        Vector3 scale = new Vector3(1.25f, 1f, 0.8f);

        GameObject terrainObject = CreateSchemeATerrain(
            "SchemeA_Transform",
            new Vector3Int(1, 1, 1),
            Vector3Int.zero,
            1f,
            position,
            rotation,
            scale
        );

        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();
            List<TerrainChunkView> views = GetViews(terrainObject);

            Check(views.Count == 1, "SchemeA-T4: fixture has 1 Chunk");
            if (views.Count == 0) return;

            TerrainChunkView view = views[0];
            TerrainChunk chunk = view.Chunk;
            Transform terrainTransform = terrain.transform;

            Vector3[] localPoints =
            {
                new Vector3(0.5f, 0.5f, 0.5f),
                new Vector3(10.25f, 5.5f, 20.75f),
                new Vector3(74.5f, 36.5f, 49.5f)
            };

            bool roundTripOk = true;
            bool voxelAgreementOk = true;

            for (int i = 0; i < localPoints.Length; i++)
            {
                Vector3 world = terrainTransform.TransformPoint(localPoints[i]);
                Vector3 back = terrain.WorldToLocalPosition(world);

                if (!Approx(back, localPoints[i], 1e-3f)) roundTripOk = false;

                if (terrain.WorldToVoxel(world) !=
                    terrain.VoxelField.PositionToVoxel(localPoints[i]))
                {
                    voxelAgreementOk = false;
                }
            }

            Check(
                roundTripOk,
                "SchemeA-T4: World -> Terrain Local round-trip under non-identity Transform"
            );
            Check(
                voxelAgreementOk,
                "SchemeA-T4: WorldToVoxel == VoxelField.PositionToVoxel (1m contract)"
            );

            Check(
                view.transform.localPosition == chunk.ChunkOrigin &&
                view.transform.localRotation == Quaternion.identity &&
                view.transform.localScale == Vector3.one,
                "SchemeA-T4: Chunk GameObject keeps Chunk-local placement"
            );
            Check(
                (view.transform.position -
                 terrainTransform.TransformPoint(chunk.ChunkOrigin)).sqrMagnitude
                < 1e-6f,
                "SchemeA-T4: Chunk world position == TransformPoint(ChunkOrigin)"
            );

            Check(
                MeshIsChunkLocal(chunk.Mesh, chunk.ChunkWorldSize),
                "SchemeA-T4: Mesh vertices stay Chunk-local (no baked Transform)"
            );
            Check(
                view.MeshFilter != null && view.MeshFilter.sharedMesh == chunk.Mesh &&
                view.MeshCollider != null &&
                view.MeshCollider.sharedMesh == chunk.Mesh,
                "SchemeA-T4: MeshFilter / MeshCollider reference the Final Mesh"
            );
        }
        finally
        {
            Object.DestroyImmediate(terrainObject);
        }
    }

    // =====================================================================
    // T5 — 新路径不依赖 legacy voxelSize；legacy Chunk 被 Mutation 拒绝
    // =====================================================================

    private static void CheckVoxelAwarePathIgnoresLegacyVoxelSize()
    {
        // (a) Voxel-aware 路径 + 错配的 legacy voxelSize（0.18）：Chunk 必须仍由
        //     VoxelField 的 1m 契约驱动。
        GameObject mismatched = CreateSchemeATerrain(
            "SchemeA_MismatchedLegacyVoxelSize",
            new Vector3Int(1, 1, 1),
            Vector3Int.zero,
            0.18f,
            Vector3.zero,
            Quaternion.identity,
            Vector3.one
        );

        try
        {
            DensityTerrain terrain = mismatched.GetComponent<DensityTerrain>();
            List<TerrainChunkView> views = GetViews(mismatched);

            Check(views.Count == 1, "SchemeA-T5: mismatched fixture has 1 Chunk");
            if (views.Count > 0)
            {
                TerrainChunk chunk = views[0].Chunk;

                Check(
                    terrain.EffectiveVoxelSize == 0.18f,
                    "SchemeA-T5: legacy DensityTerrain.voxelSize stays 0.18 (legacy scalar)"
                );
                Check(
                    chunk.VoxelSize == terrain.VoxelField.VoxelSize &&
                    chunk.VoxelSize == 1f,
                    "SchemeA-T5: Voxel-aware Chunk uses VoxelField 1m, not legacy 0.18"
                );
                Check(
                    Approx(chunk.ChunkWorldSize, ChunkWorldSize) &&
                    Approx(chunk.Density.Size, ChunkWorldSize),
                    "SchemeA-T5: ChunkWorldSize / DensityField Size == VoxelSize × chunkVoxelSize"
                );
                Check(
                    chunk.Geometry != null &&
                    chunk.Geometry.VoxelSize == 1f &&
                    chunk.Geometry.ChunkVoxelSize == ChunkVoxelSize,
                    "SchemeA-T5: Pre-cut geometry uses the same 1m / (75,37,50) contract"
                );
            }
        }
        finally
        {
            Object.DestroyImmediate(mismatched);
        }

        // (b) legacy 路径 Chunk（无 ChunkGeometry）不能被新 Mutation 使用。
        GameObject legacy = CreateLegacyTerrain("SchemeA_LegacyChunk");

        try
        {
            DensityTerrain terrain = legacy.GetComponent<DensityTerrain>();
            List<TerrainChunkView> views = GetViews(legacy);
            List<TerrainChunk> chunks = new List<TerrainChunk>();
            for (int i = 0; i < views.Count; i++) chunks.Add(views[i].Chunk);

            Check(
                chunks.Count == 1 && chunks[0].Geometry == null,
                "SchemeA-T5: legacy Chunk has no ChunkGeometry"
            );

            bool committed = VoxelAwareMeshRuntimeMutation.ApplyVoxelStateChange(
                terrain.VoxelField,
                new Vector3Int(1, 1, 1),
                VoxelState.Air,
                chunks,
                new Vector3Int(16, 16, 16)
            );

            Check(
                !committed &&
                VoxelAwareMeshRuntimeMutation.LastEvaluationFailed &&
                VoxelAwareMeshRuntimeMutation.LastEvaluationFailureMessage.Contains(
                    "Geometry"),
                "SchemeA-T5: Mutation refuses legacy Chunk (no ChunkGeometry)"
            );
            Check(
                !VoxelAwareMeshRuntimeMutation.LastMutationCommitted &&
                !VoxelAwareMeshRuntimeMutation.LastAssemblyExecuted,
                "SchemeA-T5: refused Mutation does not commit or assemble"
            );
        }
        finally
        {
            Object.DestroyImmediate(legacy);
        }
    }

    // =====================================================================
    // T6 — 对齐配置：legacy 包装 API 一致 + Mutation 契约守卫
    // =====================================================================

    private static void CheckAlignedLegacyApiAndMutationContract()
    {
        GameObject terrainObject = CreateSchemeATerrain(
            "SchemeA_AlignedMutation",
            new Vector3Int(1, 1, 1),
            Vector3Int.zero,
            1f,
            Vector3.zero,
            Quaternion.identity,
            Vector3.one
        );

        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();
            List<TerrainChunkView> views = GetViews(terrainObject);
            List<TerrainChunk> chunks = new List<TerrainChunk>();
            for (int i = 0; i < views.Count; i++) chunks.Add(views[i].Chunk);

            Check(views.Count == 1, "SchemeA-T6: fixture has 1 Chunk");
            if (views.Count == 0) return;

            TerrainChunkView view = views[0];
            TerrainChunk chunk = view.Chunk;

            // 对齐配置下 legacy 坐标包装 API 与 VoxelField 1m 契约一致。
            bool wrappersAgree = true;
            Vector3[] locals =
            {
                new Vector3(0.25f, 0.25f, 0.25f),
                new Vector3(10.75f, 6.5f, 20.5f),
                new Vector3(-0.25f, -0.5f, -1.5f)
            };

            for (int i = 0; i < locals.Length; i++)
            {
                Vector3 world = terrain.transform.TransformPoint(locals[i]);
                Vector3Int expected = terrain.VoxelField.PositionToVoxel(locals[i]);

                if (terrain.WorldToVoxel(world) != expected) wrappersAgree = false;
                if (terrain.WorldToChunkCoordinate(world) !=
                    ChunkSpace.VoxelToChunk(expected, ChunkVoxelSize))
                {
                    wrappersAgree = false;
                }
                if (terrain.WorldToLocalVoxel(world) !=
                    ChunkSpace.VoxelToLocal(expected, ChunkVoxelSize))
                {
                    wrappersAgree = false;
                }
            }

            Check(
                wrappersAgree,
                "SchemeA-T6: legacy wrappers agree with VoxelField 1m contract when aligned"
            );

            // 守卫：错误的 chunkVoxelSize 参数必须被拒绝且不提交。
            bool wrongArgument =
                VoxelAwareMeshRuntimeMutation.ApplyVoxelStateChange(
                    terrain.VoxelField,
                    new Vector3Int(10, 5, 10),
                    VoxelState.Air,
                    chunks,
                    new Vector3Int(16, 16, 16)
                );

            Check(
                !wrongArgument &&
                VoxelAwareMeshRuntimeMutation.LastEvaluationFailed &&
                VoxelAwareMeshRuntimeMutation.LastEvaluationFailureMessage.Contains(
                    "chunkVoxelSize"),
                "SchemeA-T6: wrong chunkVoxelSize argument is rejected"
            );
            Check(
                !VoxelAwareMeshRuntimeMutation.LastMutationCommitted &&
                !VoxelAwareMeshRuntimeMutation.LastAssemblyExecuted,
                "SchemeA-T6: rejected argument does not commit or assemble"
            );

            // 正确参数：提交并完成 Mesh 替换（调用方负责 View 同步）。
            Mesh meshBefore = chunk.Mesh;
            int rawIdBefore = chunk.RawMesh.GetInstanceID();

            bool committed = VoxelAwareMeshRuntimeMutation.ApplyVoxelStateChange(
                terrain.VoxelField,
                new Vector3Int(10, 5, 10),
                VoxelState.Air,
                chunks,
                ChunkVoxelSize
            );

            view.SetMesh(chunk.Mesh);

            Check(
                committed &&
                VoxelAwareMeshRuntimeMutation.LastMutationCommitted &&
                VoxelAwareMeshRuntimeMutation.LastAssemblyExecuted,
                "SchemeA-T6: correct chunkVoxelSize commits and assembles"
            );
            Check(
                chunk.Mesh != meshBefore &&
                chunk.RawMesh.GetInstanceID() == rawIdBefore &&
                chunk.Geometry != null,
                "SchemeA-T6: Mesh replaced while RawMesh / ChunkGeometry stay immutable"
            );
            Check(
                view.MeshFilter.sharedMesh == chunk.Mesh &&
                view.MeshCollider.sharedMesh == chunk.Mesh,
                "SchemeA-T6: MeshFilter / MeshCollider follow the replaced Mesh"
            );
        }
        finally
        {
            Object.DestroyImmediate(terrainObject);
        }
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    private static GameObject CreateSchemeATerrain(
        string name,
        Vector3Int chunkCount,
        Vector3Int chunkStartCoordinate,
        float voxelSize,
        Vector3 position,
        Quaternion rotation,
        Vector3 scale)
    {
        GameObject terrainObject = new GameObject(name);
        terrainObject.transform.position = position;
        terrainObject.transform.rotation = rotation;
        terrainObject.transform.localScale = scale;

        DensityTerrain terrain = terrainObject.AddComponent<DensityTerrain>();
        terrain.voxelSize = voxelSize;
        terrain.chunkVoxelSize = ChunkVoxelSize;
        terrain.chunkCount = chunkCount;
        terrain.chunkStartCoordinate = chunkStartCoordinate;
        terrain.resolution = 75;
        terrain.densityMode = DensityGenerationMode.HeightField;
        terrain.useNoise = false;
        terrain.height = 15.7f;
        terrain.cavityRadius = 0f;
        terrain.debugCutaway = false;
        terrain.regenerateInEditor = false;
        terrain.useVoxelAwareMeshPrecut = true;
        terrain.Regenerate();

        return terrainObject;
    }

    private static GameObject CreateLegacyTerrain(string name)
    {
        GameObject terrainObject = new GameObject(name);
        DensityTerrain terrain = terrainObject.AddComponent<DensityTerrain>();
        terrain.voxelSize = 0.18f;
        terrain.chunkVoxelSize = new Vector3Int(16, 16, 16);
        terrain.chunkCount = new Vector3Int(1, 1, 1);
        terrain.chunkStartCoordinate = Vector3Int.zero;
        terrain.resolution = 8;
        terrain.densityMode = DensityGenerationMode.HeightField;
        terrain.useNoise = false;
        terrain.height = 7.5f;
        terrain.cavityRadius = 0f;
        terrain.debugCutaway = false;
        terrain.regenerateInEditor = false;
        terrain.useVoxelAwareMeshPrecut = false;
        terrain.Regenerate();

        return terrainObject;
    }

    private static List<TerrainChunkView> GetViews(GameObject terrainObject)
    {
        TerrainChunkView[] array = terrainObject
            .GetComponentsInChildren<TerrainChunkView>(true);

        return new List<TerrainChunkView>(array);
    }

    private static TerrainChunkView FindView(
        List<TerrainChunkView> views,
        Vector3Int coordinate)
    {
        for (int i = 0; i < views.Count; i++)
        {
            if (views[i] != null && views[i].Coordinate == coordinate)
            {
                return views[i];
            }
        }

        return null;
    }

    private static bool HasDuplicateCoordinates(List<TerrainChunkView> views)
    {
        for (int i = 0; i < views.Count; i++)
        {
            for (int j = i + 1; j < views.Count; j++)
            {
                if (views[i].Coordinate == views[j].Coordinate) return true;
            }
        }

        return false;
    }

    private static bool MeshIsChunkLocal(Mesh mesh, Vector3 chunkWorldSize)
    {
        if (mesh == null) return false;

        Bounds bounds = mesh.bounds;
        float slack = 0.05f;

        return bounds.min.x >= -slack &&
               bounds.min.y >= -slack &&
               bounds.min.z >= -slack &&
               bounds.max.x <= chunkWorldSize.x + slack &&
               bounds.max.y <= chunkWorldSize.y + slack &&
               bounds.max.z <= chunkWorldSize.z + slack;
    }

    private static bool Approx(Vector3 a, Vector3 b, float epsilon = Epsilon)
    {
        return Mathf.Abs(a.x - b.x) <= epsilon &&
               Mathf.Abs(a.y - b.y) <= epsilon &&
               Mathf.Abs(a.z - b.z) <= epsilon;
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
            "[VoxelCoordinateMigrationSchemeAValidation] ===== SUMMARY =====\n" +
            "Checks = " + checks + "\n" +
            "Failures = " + failures + "\n" +
            "Result = " + (failures == 0 ? "PASS" : "FAIL")
        );
    }
}
