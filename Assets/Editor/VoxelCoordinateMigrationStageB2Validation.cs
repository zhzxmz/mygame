using System;
using System.Collections.Generic;
using System.Text;
using TerrainModificationV2;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Coordinate Migration Stage B2 Validation — 跨 Chunk Dirty 传播与重建依赖。
///
/// 审计结论（真实调用链，详见报告）：
///
/// [密度路径 useVoxelAwareMeshPrecut = false]
///   VoxelField.Set（调用方）
///     → DensityTerrain.RebuildChunksAffectedByVoxel
///         ├ ChunkMayContainVoxelSamples（LocalBounds vs Voxel 盒，保守含共享边界）
///         ├ BuildEffectiveDensityField（Base + VoxelField.IsAir(PositionToVoxel(pos))）
///         ├ TerrainChunk.RebuildMesh(effective) → TerrainMeshGenerator.Generate
///         └ SyncChunkMesh → TerrainChunkView.SetMesh（MeshFilter + MeshCollider）
///
/// [Voxel-Aware 路径 useVoxelAwareMeshPrecut = true]
///   Regenerate → BuildChunk → Raw MC Mesh
///     → VoxelAwareMeshPrecut.Build(rawMesh, origin, voxelSize, coord, chunkVoxelSize)
///        （纯几何预切，不读 VoxelField ⇒ 初始 Fragments 全 Active / 无 Visibility / 无 HiddenFace）
///     → Assemble → Chunk.ApplyVoxelAwareGeometry
///
///   VoxelAwareMeshRuntimeMutation.ApplyVoxelStateChange
///     ├ FindChunkContaining → ChunkSpace.VoxelToChunk（绝对坐标，与 chunkStartCoordinate 无关）
///     ├ 契约检查（VoxelSize / chunkVoxelSize）
///     ├ Phase1 Evaluation ×6 面：
///     │    ResolveEvaluationOwner → owner = VoxelToChunk(NegativeVoxel)（缺失则 PositiveVoxel）
///     │    BuildFaceSegments（Coplanar Base 覆盖 + Bridges）→ SpatialFace.Evaluate → HiddenFaces
///     │    BuildVisibilityTargets → 跨所有 Chunk 的 FaceFragment 可见性计划
///     │    Dirty: owner（边界面再加 meshOwner = Solid 侧 Chunk）
///     ├ Phase2 Commit：FaceFragmentVisibility / HiddenFaceContributions / dirty
///     ├ RefreshVoxelFragments(changedChunk.Geometry, voxel, active=false)（只动 Fragments）
///     └ Phase3 ReassembleDirtyChunks：dirty → Assemble → ReplaceAssembledMesh
///        （View 同步是调用方责任，见 SchemeA-T6；本轮只验证 LastReassembledChunks 是否完整）
///
/// 独立判据（不复制生产算法）：
///   O1 密度样本判据：Chunk 的 Density 样本中是否至少有一个 PositionToVoxel(samplePos) == voxel。
///      这是 BuildEffectiveDensityField 的定义本身，但用样本网格直接枚举，不引用
///      ChunkMayContainVoxelSamples 的 Bounds 判定。
///   O2 结构接触上界：Chunk 的 LocalBounds 与 Voxel 盒（闭区间）是否相交 ⇒ “可能受影响”的上界。
///   O3 几何快照判据：ChunkGeometry 的 (Active Fragments / FaceFragmentVisibility /
///      HiddenFaceContributions / SpatialFaces) 内容快照是否变化 ⇒ “实际必须重新 Assemble”的集合。
///   O4 规范化几何对照：按位置量化后的顶点集合 + 保持绕序的三角形键集合 + 总面积。
///   O5 往返判据：mine → restore(Solid) 后必须精确回到基线几何（不依赖任何生产判定）。
///
/// 本轮只记录证据；仅当存在可复现的 Dirty / 缓存缺陷时才改生产代码。
/// </summary>
public static class VoxelCoordinateMigrationStageB2Validation
{
    private static readonly Vector3Int ChunkVoxelSize = new Vector3Int(75, 37, 50);
    private const float FlatHeight = 15.7f;
    private const int FlatSurfaceVoxelY = 15;
    private const float Quantum = 1e-4f;
    private const float RayTolerance = 1e-3f;

    private static int checks;
    private static int failures;

    private static int casesWithActiveChange;
    private static int casesWithVisibilityChange;
    private static int casesWithContributionChange;
    private static int casesWithSpatialFaceChange;

    [MenuItem("Tools/Terrain/Validate Coordinate Migration Stage B2")]
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
        catch (Exception exception)
        {
            failures++;
            Debug.LogError(
                "[VoxelCoordinateMigrationStageB2Validation] EXCEPTION: " +
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

        CheckDensityNecessityOracle();
        CheckPrecutDensityEntrySemantics();
        CheckMutationGeometryPropagation();
        CheckRoundTripRestore();
        CheckIdempotenceAndRebuildEntries();
        CheckCoordinateEdges();
        CheckExceptionPaths();
        CheckCacheCleanupAgainstPristine();
        CheckFullRebuildComparison();
    }

    // =====================================================================
    // T1 — 密度路径：必要 / 变化 / 实际 重建集合（5 项报告）
    // =====================================================================

    private static void CheckDensityNecessityOracle()
    {
        GameObject terrainObject = CreateFixture(
            "StageB2_DensityFixture",
            Vector3Int.zero,
            new Vector3Int(2, 1, 2),
            false,
            false
        );

        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();
            List<TerrainChunkView> views = GetViews(terrainObject);

            Check(
                views.Count == 4,
                "StageB2-T1: fixture has 4 Chunks"
            );

            Vector3Int[] cases =
            {
                new Vector3Int(30, 10, 30),
                new Vector3Int(30, 15, 30),
                new Vector3Int(74, 5, 10),
                new Vector3Int(75, 5, 10),
                new Vector3Int(76, 5, 10),
                new Vector3Int(10, 5, 49),
                new Vector3Int(10, 5, 50),
                new Vector3Int(10, 5, 51),
                new Vector3Int(74, 15, 49),
                new Vector3Int(75, 15, 50)
            };

            for (int i = 0; i < cases.Length; i++)
            {
                RunDensityCase(terrain, views, cases[i]);
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    private static void RunDensityCase(
        DensityTerrain terrain,
        List<TerrainChunkView> views,
        Vector3Int voxel)
    {
        string label = "StageB2-T1[" + voxel + "]";

        // ---- 快照 ----
        Dictionary<Vector3Int, DensityField> beforeFields =
            new Dictionary<Vector3Int, DensityField>();
        Dictionary<Vector3Int, Mesh> beforeMeshes =
            new Dictionary<Vector3Int, Mesh>();

        for (int i = 0; i < views.Count; i++)
        {
            TerrainChunk chunk = views[i].Chunk;
            beforeFields[chunk.Coordinate] = terrain.BuildEffectiveDensityField(chunk);
            beforeMeshes[chunk.Coordinate] = chunk.Mesh;
        }

        // ---- 独立判据 O1：含样本的 Chunk（必要重建候选） ----
        HashSet<Vector3Int> necessary = ChunksWithSamplesInVoxel(
            terrain,
            views,
            voxel
        );

        // ---- 应用修改 ----
        terrain.VoxelField.Set(voxel, VoxelState.Air);

        // ---- 实测：Effective Density 发生变化的 Chunk ----
        HashSet<Vector3Int> changed = new HashSet<Vector3Int>();
        for (int i = 0; i < views.Count; i++)
        {
            TerrainChunk chunk = views[i].Chunk;
            DensityField after = terrain.BuildEffectiveDensityField(chunk);
            if (EffectiveFieldDiffers(beforeFields[chunk.Coordinate], after))
            {
                changed.Add(chunk.Coordinate);
            }
        }

        // ---- 实测：RebuildChunksAffectedByVoxel 实际重建的 Chunk ----
        int rebuiltCount = terrain.RebuildChunksAffectedByVoxel(voxel);

        HashSet<Vector3Int> rebuilt = new HashSet<Vector3Int>();
        for (int i = 0; i < views.Count; i++)
        {
            TerrainChunk chunk = views[i].Chunk;
            if (chunk.Mesh != beforeMeshes[chunk.Coordinate])
            {
                rebuilt.Add(chunk.Coordinate);
            }
        }

        HashSet<Vector3Int> extra = Difference(rebuilt, necessary);
        HashSet<Vector3Int> missing = Difference(necessary, rebuilt);

        Debug.Log(
            "[StageB2-T1] voxel=" + voxel +
            " necessary=" + Format(necessary) +
            " changed=" + Format(changed) +
            " rebuilt=" + Format(rebuilt) +
            " extra=" + Format(extra) +
            " missing=" + Format(missing) +
            " returned=" + rebuiltCount
        );

        Check(
            necessary.Count > 0 && SetEquals(changed, necessary),
            label + ": 变化样本 Chunk 集合 == 样本判据集合 (" +
            Format(changed) + " vs " + Format(necessary) + ")"
        );
        Check(
            SetEquals(rebuilt, necessary),
            label + ": 实际重建集合 == 必要集合 (" +
            Format(rebuilt) + " vs " + Format(necessary) + ")"
        );
        Check(
            extra.Count == 0,
            label + ": 无多余重建 (" + Format(extra) + ")"
        );
        Check(
            missing.Count == 0,
            label + ": 无遗漏重建 (" + Format(missing) + ")"
        );
        Check(
            rebuiltCount == rebuilt.Count,
            label + ": 返回值 " + rebuiltCount + " == 实际替换 Mesh 的 Chunk 数 " +
            rebuilt.Count
        );

        // 视图同步（密度路径内部完成）
        bool viewsSynced = true;
        for (int i = 0; i < views.Count; i++)
        {
            TerrainChunkView view = views[i];
            if (view.MeshFilter.sharedMesh != view.Chunk.Mesh) viewsSynced = false;
        }

        Check(
            viewsSynced,
            label + ": 所有 Chunk 的 MeshFilter 指向当前 Chunk.Mesh（View 同步）"
        );

        // ---- 还原，避免污染后续用例 ----
        terrain.VoxelField.Set(voxel, VoxelState.Solid);
        terrain.RebuildChunksAffectedByVoxel(voxel);
    }

    // =====================================================================
    // T2 — Voxel-Aware 路径下调用密度入口的语义（Geometry 缓存是否失效）
    // =====================================================================

    private static void CheckPrecutDensityEntrySemantics()
    {
        GameObject terrainObject = CreateFixture(
            "StageB2_PrecutDensityEntry",
            Vector3Int.zero,
            new Vector3Int(2, 1, 2),
            true,
            false
        );

        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();
            List<TerrainChunkView> views = GetViews(terrainObject);

            Vector3Int voxel = new Vector3Int(30, FlatSurfaceVoxelY, 30);

            Dictionary<Vector3Int, MeshCanonical> beforeCanonical =
                SnapshotCanonical(views);
            Dictionary<Vector3Int, Mesh> beforeMeshes = SnapshotMeshes(views);

            HashSet<Vector3Int> necessary = ChunksWithSamplesInVoxel(
                terrain,
                views,
                voxel
            );

            terrain.VoxelField.Set(voxel, VoxelState.Air);
            int rebuiltCount = terrain.RebuildChunksAffectedByVoxel(voxel);

            int meshReplaced = 0;
            int canonicalChanged = 0;
            for (int i = 0; i < views.Count; i++)
            {
                TerrainChunk chunk = views[i].Chunk;
                if (chunk.Mesh != beforeMeshes[chunk.Coordinate]) meshReplaced++;

                MeshCanonical current = BuildCanonical(
                    chunk.Mesh,
                    chunk.ChunkOrigin
                );
                if (!SameCanonical(
                    beforeCanonical[chunk.Coordinate],
                    current,
                    out string difference))
                {
                    canonicalChanged++;
                    Debug.Log(
                        "[StageB2-T2] geometry changed by Voxel entry (B3-A F1 fix): " +
                        chunk.Coordinate + " " + difference
                    );
                }
            }

            Debug.Log(
                "[StageB2-T2] precut ON + RebuildChunksAffectedByVoxel: returned=" +
                rebuiltCount + " meshReplaced=" + meshReplaced +
                " canonicalGeometryChangedChunks=" + canonicalChanged +
                " sampleDependency=" + Format(necessary)
            );

            Check(
                meshReplaced > 0,
                "StageB2-T2: 密度入口在 precut ON 下仍替换了 Mesh 对象 (" +
                meshReplaced + ")"
            );
            // B3-A（F1）之后：precut ON 的入口改为走 VoxelAwareMeshRuntimeMutation，
            // 因此必须真正改变几何（修复前该值为 0，即只重新 Assemble 旧 Geometry）。
            Check(
                canonicalChanged > 0,
                "StageB2-T2: precut ON 下密度入口真正应用了 Voxel 状态（几何变化 Chunk = " +
                canonicalChanged + "）"
            );

            bool viewsSynced = true;
            for (int i = 0; i < views.Count; i++)
            {
                if (views[i].MeshFilter.sharedMesh != views[i].Chunk.Mesh)
                {
                    viewsSynced = false;
                }
            }

            Check(
                viewsSynced,
                "StageB2-T2: 密度入口同步了 View（MeshFilter == Chunk.Mesh）"
            );

            Limit(
                "B3-A（F1）已修复：precut ON 下 DensityTerrain.RebuildChunksAffectedByVoxel " +
                "不再只 Assemble 旧 Geometry，而是走 VoxelAwareMeshRuntimeMutation 生产路径， " +
                "并负责同步 View 的 MeshFilter / MeshCollider。"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    // =====================================================================
    // T3 — Voxel-Aware 路径：Geometry 变化 / Dirty / 重新 Assemble / 视图责任
    // =====================================================================

    private static void CheckMutationGeometryPropagation()
    {
        GameObject terrainObject = CreateFixture(
            "StageB2_MutationFixture",
            Vector3Int.zero,
            new Vector3Int(2, 1, 2),
            true,
            false
        );

        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();
            List<TerrainChunkView> views = GetViews(terrainObject);

            Vector3Int[] cases =
            {
                new Vector3Int(30, 10, 30),
                new Vector3Int(30, FlatSurfaceVoxelY, 30),
                new Vector3Int(74, FlatSurfaceVoxelY, 30),
                new Vector3Int(75, FlatSurfaceVoxelY, 30),
                new Vector3Int(76, FlatSurfaceVoxelY, 30),
                new Vector3Int(10, FlatSurfaceVoxelY, 49),
                new Vector3Int(10, FlatSurfaceVoxelY, 50),
                new Vector3Int(10, FlatSurfaceVoxelY, 51),
                new Vector3Int(74, FlatSurfaceVoxelY, 49),
                new Vector3Int(75, FlatSurfaceVoxelY, 50)
            };

            for (int i = 0; i < cases.Length; i++)
            {
                RunMutationCase(
                    terrain,
                    views,
                    cases[i],
                    "StageB2-T3[" + i + ":" + cases[i] + "]"
                );
            }

            Debug.Log(
                "[StageB2-T3] cache-part change counts: Active=" +
                casesWithActiveChange + " Visibility=" +
                casesWithVisibilityChange + " Contributions=" +
                casesWithContributionChange + " SpatialFaces=" +
                casesWithSpatialFaceChange
            );

            Check(
                casesWithActiveChange > 0,
                "StageB2-T3: 至少一个用例实际改动了 Fragment Active 缓存 (" +
                casesWithActiveChange + ")"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    private static void RunMutationCase(
        DensityTerrain terrain,
        List<TerrainChunkView> views,
        Vector3Int voxel,
        string label,
        bool restoreAfter = true)
    {
        Dictionary<Vector3Int, GeometrySnapshot> beforeGeometry =
            SnapshotGeometry(views);
        Dictionary<Vector3Int, MeshCanonical> beforeCanonical =
            SnapshotCanonical(views);
        Dictionary<Vector3Int, Mesh> beforeMeshes = SnapshotMeshes(views);
        HashSet<Vector3Int> touching = TouchingChunks(views, voxel);

        // 仅作信息对照：密度采样依赖集合（precut 路径下不驱动几何重建）。
        HashSet<Vector3Int> densityDependency = ChunksWithSamplesInVoxel(
            terrain,
            views,
            voxel
        );

        bool committed = VoxelAwareMeshRuntimeMutation.ApplyVoxelStateChange(
            terrain.VoxelField,
            voxel,
            VoxelState.Air,
            ChunksOf(views),
            ChunkVoxelSize
        );

        if (!committed)
        {
            Check(
                false,
                label + ": mutation committed (failure = " +
                VoxelAwareMeshRuntimeMutation.LastEvaluationFailureMessage + ")"
            );
            return;
        }

        HashSet<Vector3Int> dirty = new HashSet<Vector3Int>(
            VoxelAwareMeshRuntimeMutation.LastDirtyChunks
        );
        HashSet<Vector3Int> reassembled = new HashSet<Vector3Int>(
            VoxelAwareMeshRuntimeMutation.LastReassembledChunks
        );

        HashSet<Vector3Int> changedGeometry = new HashSet<Vector3Int>();
        HashSet<Vector3Int> changedMesh = new HashSet<Vector3Int>();
        for (int i = 0; i < views.Count; i++)
        {
            TerrainChunk chunk = views[i].Chunk;

            if (!SameGeometry(
                beforeGeometry[chunk.Coordinate],
                BuildGeometrySnapshot(chunk),
                out string geometryDifference))
            {
                changedGeometry.Add(chunk.Coordinate);
                GeometrySnapshot beforeSnapshot = beforeGeometry[chunk.Coordinate];
                GeometrySnapshot afterSnapshot = BuildGeometrySnapshot(chunk);
                if (beforeSnapshot.ActiveFragments != afterSnapshot.ActiveFragments)
                {
                    casesWithActiveChange++;
                }

                if (beforeSnapshot.Visibility != afterSnapshot.Visibility)
                {
                    casesWithVisibilityChange++;
                }

                if (beforeSnapshot.Contributions != afterSnapshot.Contributions)
                {
                    casesWithContributionChange++;
                }

                if (beforeSnapshot.SpatialFaces != afterSnapshot.SpatialFaces)
                {
                    casesWithSpatialFaceChange++;
                }

                Debug.Log(
                    "[StageB2-T3] geometry changed: " + chunk.Coordinate +
                    " (" + geometryDifference + ") parts=[" +
                    GeometryChangeFlags(beforeSnapshot, afterSnapshot) + "]"
                );
            }

            if (chunk.Mesh != beforeMeshes[chunk.Coordinate])
            {
                changedMesh.Add(chunk.Coordinate);
            }
        }

        HashSet<Vector3Int> dirtyMissing = Difference(changedGeometry, dirty);
        HashSet<Vector3Int> reassembleMissing = Difference(
            changedGeometry,
            reassembled
        );
        HashSet<Vector3Int> silentMeshChange = Difference(changedMesh, reassembled);
        HashSet<Vector3Int> outOfBoundRebuild = Difference(reassembled, touching);
        HashSet<Vector3Int> dirtyNotReassembled = Difference(dirty, reassembled);

        Debug.Log(
            "[StageB2-T3] voxel=" + voxel +
            " dirty=" + Format(dirty) +
            " reassembled=" + Format(reassembled) +
            " geometryChanged=" + Format(changedGeometry) +
            " meshChanged=" + Format(changedMesh) +
            " touchingBound=" + Format(touching) +
            " densityDependency=" + Format(densityDependency) +
            " dirtyMissing=" + Format(dirtyMissing) +
            " reassembleMissing=" + Format(reassembleMissing) +
            " outOfBound=" + Format(outOfBoundRebuild) +
            " dirtyNotReassembled=" + Format(dirtyNotReassembled)
        );

        Check(
            dirtyMissing.Count == 0,
            label + ": 所有 Geometry 变化的 Chunk 都被标记 Dirty (" +
            Format(dirtyMissing) + ")"
        );
        Check(
            reassembleMissing.Count == 0,
            label + ": 所有 Geometry 变化的 Chunk 都被重新 Assemble (" +
            Format(reassembleMissing) + ")"
        );
        Check(
            silentMeshChange.Count == 0,
            label + ": 没有“Mesh 变了但未在重新 Assemble 列表中”的 Chunk (" +
            Format(silentMeshChange) + ")"
        );
        Check(
            outOfBoundRebuild.Count == 0,
            label + ": 重新 Assemble 集合不超出结构接触上界 (" +
            Format(outOfBoundRebuild) + ")"
        );
        Check(
            dirtyNotReassembled.Count == 0,
            label + ": Dirty 集合中的每个 Chunk 都被重新 Assemble (" +
            Format(dirtyNotReassembled) + ")"
        );
        Check(
            dirty.Contains(ChunkSpace.VoxelToChunk(voxel, ChunkVoxelSize)),
            label + ": 被编辑 Voxel 所在 Chunk 一定 Dirty"
        );

        // 调用方责任：LastReassembledChunks 必须覆盖所有需要同步 View 的 Chunk。
        bool syncListComplete = true;
        for (int i = 0; i < views.Count; i++)
        {
            TerrainChunk chunk = views[i].Chunk;
            bool meshReplaced = changedMesh.Contains(chunk.Coordinate);
            bool inList = reassembled.Contains(chunk.Coordinate);
            if (meshReplaced != inList) syncListComplete = false;
        }

        Check(
            syncListComplete,
            label + ": LastReassembledChunks 恰好等于需要同步 View 的 Chunk 集合"
        );

        // 还原并验证往返一致（同时为后续用例复位）
        if (!restoreAfter) return;

        bool restored = VoxelAwareMeshRuntimeMutation.ApplyVoxelStateChange(
            terrain.VoxelField,
            voxel,
            VoxelState.Solid,
            ChunksOf(views),
            ChunkVoxelSize
        );

        Check(
            restored,
            label + ": restore(Solid) mutation committed"
        );

        bool roundTrip = true;
        string roundTripDetail = string.Empty;
        for (int i = 0; i < views.Count; i++)
        {
            TerrainChunk chunk = views[i].Chunk;
            MeshCanonical current = BuildCanonical(chunk.Mesh, chunk.ChunkOrigin);
            if (!SameCanonical(
                beforeCanonical[chunk.Coordinate],
                current,
                out string difference))
            {
                roundTrip = false;
                if (roundTripDetail.Length == 0)
                {
                    roundTripDetail = chunk.Coordinate + ": " + difference;
                }
            }
        }

        Check(
            roundTrip,
            label + ": mine → restore 回到基线几何" +
            (roundTripDetail.Length > 0 ? " (" + roundTripDetail + ")" : string.Empty)
        );
    }

    // =====================================================================
    // T4 — 往返一致性（多 Voxel / 场景参数 / 负坐标）
    // =====================================================================

    private static void CheckRoundTripRestore()
    {
        // (a) 连续多个 Voxel 修改后一次性还原。
        GameObject terrainObject = CreateFixture(
            "StageB2_RoundTripMulti",
            Vector3Int.zero,
            new Vector3Int(2, 1, 2),
            true,
            false
        );

        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();
            List<TerrainChunkView> views = GetViews(terrainObject);

            Dictionary<Vector3Int, MeshCanonical> baseline =
                SnapshotCanonical(views);

            Vector3Int[] voxels =
            {
                new Vector3Int(30, FlatSurfaceVoxelY, 30),
                new Vector3Int(31, FlatSurfaceVoxelY, 30),
                new Vector3Int(74, FlatSurfaceVoxelY, 30),
                new Vector3Int(75, FlatSurfaceVoxelY, 30),
                new Vector3Int(75, FlatSurfaceVoxelY, 50),
                new Vector3Int(10, FlatSurfaceVoxelY, 50)
            };

            int committedCount = 0;
            for (int i = 0; i < voxels.Length; i++)
            {
                if (VoxelAwareMeshRuntimeMutation.ApplyVoxelStateChange(
                    terrain.VoxelField,
                    voxels[i],
                    VoxelState.Air,
                    ChunksOf(views),
                    ChunkVoxelSize))
                {
                    committedCount++;
                }
            }

            Check(
                committedCount == voxels.Length,
                "StageB2-T4a: 连续 " + voxels.Length + " 次 Voxel 修改全部提交 (" +
                committedCount + ")"
            );

            Dictionary<Vector3Int, MeshCanonical> mined = SnapshotCanonical(views);
            int changedChunks = 0;
            for (int i = 0; i < views.Count; i++)
            {
                Vector3Int coordinate = views[i].Chunk.Coordinate;
                if (!SameCanonical(
                    baseline[coordinate],
                    mined[coordinate],
                    out string _))
                {
                    changedChunks++;
                }
            }

            Check(
                changedChunks > 0,
                "StageB2-T4a: 连续修改确实改变了最终几何 (" + changedChunks +
                " Chunk)"
            );

            for (int i = 0; i < voxels.Length; i++)
            {
                VoxelAwareMeshRuntimeMutation.ApplyVoxelStateChange(
                    terrain.VoxelField,
                    voxels[i],
                    VoxelState.Solid,
                    ChunksOf(views),
                    ChunkVoxelSize
                );
            }

            bool restored = true;
            string detail = string.Empty;
            for (int i = 0; i < views.Count; i++)
            {
                TerrainChunk chunk = views[i].Chunk;
                MeshCanonical current = BuildCanonical(
                    chunk.Mesh,
                    chunk.ChunkOrigin
                );
                if (!SameCanonical(
                    baseline[chunk.Coordinate],
                    current,
                    out string difference))
                {
                    restored = false;
                    if (detail.Length == 0)
                    {
                        detail = chunk.Coordinate + ": " + difference;
                    }
                }
            }

            Check(
                restored,
                "StageB2-T4a: 全部还原后与基线几何一致" +
                (detail.Length > 0 ? " (" + detail + ")" : string.Empty)
            );
            Check(
                terrain.VoxelField.StoredVoxelCount == 0,
                "StageB2-T4a: 还原后 VoxelField 回到稀疏空（StoredVoxelCount = " +
                terrain.VoxelField.StoredVoxelCount + "）"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }

        // (b) 场景同参数（noise + debug cutaway）下由几何扫描选择地表 Voxel。
        GameObject sceneLike = CreateFixture(
            "StageB2_RoundTripSceneLike",
            Vector3Int.zero,
            new Vector3Int(2, 1, 2),
            true,
            true
        );

        try
        {
            DensityTerrain terrain = sceneLike.GetComponent<DensityTerrain>();
            List<TerrainChunkView> views = GetViews(sceneLike);

            Vector3Int voxel;
            bool found = TryFindSurfaceVoxel(views, out voxel);

            Check(found, "StageB2-T4b: 能定位一个含几何的 Voxel (" + voxel + ")");

            if (found)
            {
                Dictionary<Vector3Int, MeshCanonical> baseline =
                    SnapshotCanonical(views);

                bool mined = VoxelAwareMeshRuntimeMutation.ApplyVoxelStateChange(
                    terrain.VoxelField,
                    voxel,
                    VoxelState.Air,
                    ChunksOf(views),
                    ChunkVoxelSize
                );

                Check(
                    mined,
                    "StageB2-T4b: 场景参数（noise + cutaway）下 mutation 提交" +
                    (mined
                        ? string.Empty
                        : " —— " + VoxelAwareMeshRuntimeMutation
                            .LastEvaluationFailureMessage)
                );

                if (mined)
                {
                    bool restored =
                        VoxelAwareMeshRuntimeMutation.ApplyVoxelStateChange(
                            terrain.VoxelField,
                            voxel,
                            VoxelState.Solid,
                            ChunksOf(views),
                            ChunkVoxelSize
                        );

                    bool equal = true;
                    string detail = string.Empty;
                    for (int i = 0; i < views.Count; i++)
                    {
                        TerrainChunk chunk = views[i].Chunk;
                        MeshCanonical current = BuildCanonical(
                            chunk.Mesh,
                            chunk.ChunkOrigin
                        );
                        if (!SameCanonical(
                            baseline[chunk.Coordinate],
                            current,
                            out string difference))
                        {
                            equal = false;
                            if (detail.Length == 0)
                            {
                                detail = chunk.Coordinate + ": " + difference;
                            }
                        }
                    }

                    Check(
                        restored && equal,
                        "StageB2-T4b: 场景参数下 mine → restore 回到基线几何" +
                        (detail.Length > 0 ? " (" + detail + ")" : string.Empty)
                    );
                }
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(sceneLike);
        }
    }

    // =====================================================================
    // T5 — 幂等 / 重建入口顺序 / 缓存保持
    // =====================================================================

    private static void CheckIdempotenceAndRebuildEntries()
    {
        GameObject terrainObject = CreateFixture(
            "StageB2_Idempotence",
            Vector3Int.zero,
            new Vector3Int(2, 1, 2),
            true,
            false
        );

        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();
            List<TerrainChunkView> views = GetViews(terrainObject);
            Vector3Int voxel = new Vector3Int(30, FlatSurfaceVoxelY, 30);

            VoxelAwareMeshRuntimeMutation.ApplyVoxelStateChange(
                terrain.VoxelField,
                voxel,
                VoxelState.Air,
                ChunksOf(views),
                ChunkVoxelSize
            );

            Dictionary<Vector3Int, MeshCanonical> afterFirst =
                SnapshotCanonical(views);

            // 同一 Voxel 再次修改为 Air：几何必须不变。
            bool secondCommit =
                VoxelAwareMeshRuntimeMutation.ApplyVoxelStateChange(
                    terrain.VoxelField,
                    voxel,
                    VoxelState.Air,
                    ChunksOf(views),
                    ChunkVoxelSize
                );

            bool idempotent = true;
            string detail = string.Empty;
            for (int i = 0; i < views.Count; i++)
            {
                TerrainChunk chunk = views[i].Chunk;
                MeshCanonical current = BuildCanonical(chunk.Mesh, chunk.ChunkOrigin);
                if (!SameCanonical(
                    afterFirst[chunk.Coordinate],
                    current,
                    out string difference))
                {
                    idempotent = false;
                    if (detail.Length == 0)
                    {
                        detail = chunk.Coordinate + ": " + difference;
                    }
                }
            }

            Check(
                secondCommit && idempotent,
                "StageB2-T5: 重复 mine 同一 Voxel 幂等（几何不变）" +
                (detail.Length > 0 ? " (" + detail + ")" : string.Empty)
            );

            // 未标记 Dirty 时 RebuildDirtyChunks 不应改变任何几何。
            int dirtyRebuilt = CountDirtyChunks(views);
            terrain.RebuildDirtyChunks();

            bool dirtyPreserved = true;
            for (int i = 0; i < views.Count; i++)
            {
                TerrainChunk chunk = views[i].Chunk;
                MeshCanonical current = BuildCanonical(chunk.Mesh, chunk.ChunkOrigin);
                if (!SameCanonical(
                    afterFirst[chunk.Coordinate],
                    current,
                    out string _))
                {
                    dirtyPreserved = false;
                }
            }

            Check(
                dirtyPreserved,
                "StageB2-T5: RebuildDirtyChunks（无 Dirty 标记，dirty=" +
                dirtyRebuilt + "）后几何保持已挖掘状态"
            );

            // precut ON 下密度入口：只重新 Assemble，几何保持。
            Dictionary<Vector3Int, Mesh> beforeMeshes = SnapshotMeshes(views);
            terrain.RebuildChunksAffectedByVoxel(voxel);

            bool densityEntryPreserved = true;
            int meshReplaced = 0;
            for (int i = 0; i < views.Count; i++)
            {
                TerrainChunk chunk = views[i].Chunk;
                if (chunk.Mesh != beforeMeshes[chunk.Coordinate]) meshReplaced++;

                MeshCanonical current = BuildCanonical(chunk.Mesh, chunk.ChunkOrigin);
                if (!SameCanonical(
                    afterFirst[chunk.Coordinate],
                    current,
                    out string _))
                {
                    densityEntryPreserved = false;
                }
            }

            Check(
                densityEntryPreserved,
                "StageB2-T5: precut ON 下 RebuildChunksAffectedByVoxel 保持已挖掘几何" +
                "（替换了 " + meshReplaced + " 个 Mesh 对象）"
            );

            // MarkDirty + RebuildDirtyChunks：从既有 Geometry 重新 Assemble，仍需保持。
            for (int i = 0; i < views.Count; i++)
            {
                views[i].Chunk.MarkDirty();
            }

            terrain.RebuildDirtyChunks();

            bool markDirtyPreserved = true;
            for (int i = 0; i < views.Count; i++)
            {
                TerrainChunk chunk = views[i].Chunk;
                MeshCanonical current = BuildCanonical(chunk.Mesh, chunk.ChunkOrigin);
                if (!SameCanonical(
                    afterFirst[chunk.Coordinate],
                    current,
                    out string _))
                {
                    markDirtyPreserved = false;
                }
            }

            Check(
                markDirtyPreserved,
                "StageB2-T5: MarkDirty + RebuildDirtyChunks 后几何仍与已挖掘状态一致"
            );

            bool allClean = true;
            for (int i = 0; i < views.Count; i++)
            {
                if (views[i].Chunk.IsDirty) allClean = false;
            }

            Check(
                allClean,
                "StageB2-T5: RebuildDirtyChunks 后所有 Chunk IsDirty == false"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    // =====================================================================
    // T6 — 坐标边界：负坐标 / chunkStartCoordinate != 0 / 不存在 Chunk
    // =====================================================================

    private static void CheckCoordinateEdges()
    {
        // (a) 负坐标 Chunk（chunkStart = (-2,0,-2)）。
        GameObject negative = CreateFixture(
            "StageB2_NegativeCoords",
            new Vector3Int(-2, 0, -2),
            new Vector3Int(2, 1, 2),
            true,
            false
        );

        try
        {
            DensityTerrain terrain = negative.GetComponent<DensityTerrain>();
            List<TerrainChunkView> views = GetViews(negative);

            HashSet<Vector3Int> coordinates = new HashSet<Vector3Int>();
            for (int i = 0; i < views.Count; i++)
            {
                coordinates.Add(views[i].Coordinate);
            }

            Check(
                coordinates.Contains(new Vector3Int(-2, 0, -2)) &&
                coordinates.Contains(new Vector3Int(-1, 0, -2)) &&
                coordinates.Contains(new Vector3Int(-2, 0, -1)) &&
                coordinates.Contains(new Vector3Int(-1, 0, -1)),
                "StageB2-T6a: 负坐标 Chunk 集合正确 (" + Format(coordinates) + ")"
            );

            Vector3Int[] cases =
            {
                new Vector3Int(-76, FlatSurfaceVoxelY, -1),
                new Vector3Int(-75, FlatSurfaceVoxelY, -1),
                new Vector3Int(-1, FlatSurfaceVoxelY, -51),
                new Vector3Int(-1, FlatSurfaceVoxelY, -50),
                new Vector3Int(-76, FlatSurfaceVoxelY, -51)
            };

            for (int i = 0; i < cases.Length; i++)
            {
                Vector3Int voxel = cases[i];
                Vector3Int expected = ChunkSpace.VoxelToChunk(voxel, ChunkVoxelSize);

                Check(
                    coordinates.Contains(expected),
                    "StageB2-T6a: Voxel " + voxel + " 的归属 Chunk " + expected +
                    " 存在"
                );

                RunMutationCase(
                    terrain,
                    views,
                    voxel,
                    "StageB2-T6a[" + voxel + "]"
                );
            }

            // 不存在的 Chunk：两个入口都必须报告“找不到”。
            Vector3Int outside = new Vector3Int(-151, FlatSurfaceVoxelY, -1);

            Check(
                !coordinates.Contains(
                    ChunkSpace.VoxelToChunk(outside, ChunkVoxelSize)),
                "StageB2-T6a: 越界 Voxel " + outside + " 归属的 Chunk 不存在"
            );

            CheckNotFoundBehaviour(terrain, views, outside, "StageB2-T6a");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(negative);
        }

        // (b) chunkStartCoordinate = (5,0,7)。
        GameObject shifted = CreateFixture(
            "StageB2_ShiftedStart",
            new Vector3Int(5, 0, 7),
            new Vector3Int(2, 1, 2),
            true,
            false
        );

        try
        {
            DensityTerrain terrain = shifted.GetComponent<DensityTerrain>();
            List<TerrainChunkView> views = GetViews(shifted);

            HashSet<Vector3Int> coordinates = new HashSet<Vector3Int>();
            for (int i = 0; i < views.Count; i++)
            {
                coordinates.Add(views[i].Coordinate);
            }

            Check(
                coordinates.Contains(new Vector3Int(5, 0, 7)) &&
                coordinates.Contains(new Vector3Int(6, 0, 7)) &&
                coordinates.Contains(new Vector3Int(5, 0, 8)) &&
                coordinates.Contains(new Vector3Int(6, 0, 8)),
                "StageB2-T6b: 绝对 ChunkCoordinate (5,0,7)… 未被当作数组索引 (" +
                Format(coordinates) + ")"
            );

            // Chunk (5,0,7) 的原点应为 (375,0,350)。
            TerrainChunk first = FindChunk(views, new Vector3Int(5, 0, 7));

            Check(
                first != null &&
                Approx(first.ChunkOrigin, new Vector3(375f, 0f, 350f)),
                "StageB2-T6b: Chunk (5,0,7) 原点 = (375,0,350)（实际 " +
                (first != null ? first.ChunkOrigin.ToString() : "缺失") + "）"
            );

            Vector3Int[] cases =
            {
                new Vector3Int(375, FlatSurfaceVoxelY, 350),
                new Vector3Int(405, FlatSurfaceVoxelY, 380),
                new Vector3Int(449, FlatSurfaceVoxelY, 380),
                new Vector3Int(450, FlatSurfaceVoxelY, 380),
                new Vector3Int(405, FlatSurfaceVoxelY, 400)
            };

            for (int i = 0; i < cases.Length; i++)
            {
                Vector3Int voxel = cases[i];
                Vector3Int expected = ChunkSpace.VoxelToChunk(voxel, ChunkVoxelSize);

                Check(
                    coordinates.Contains(expected),
                    "StageB2-T6b: Voxel " + voxel + " 归属 " + expected + " 存在"
                );

                RunMutationCase(
                    terrain,
                    views,
                    voxel,
                    "StageB2-T6b[" + voxel + "]"
                );
            }

            // chunkStart 偏移后，绝对原点附近的 Voxel 不属于任何 Chunk。
            Vector3Int notOwned = new Vector3Int(10, FlatSurfaceVoxelY, 10);

            Check(
                !coordinates.Contains(
                    ChunkSpace.VoxelToChunk(notOwned, ChunkVoxelSize)),
                "StageB2-T6b: Voxel " + notOwned + " 归属 Chunk (0,0,0) 不存在"
            );

            CheckNotFoundBehaviour(terrain, views, notOwned, "StageB2-T6b");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(shifted);
        }
    }

    private static void CheckNotFoundBehaviour(
        DensityTerrain terrain,
        List<TerrainChunkView> views,
        Vector3Int voxel,
        string label)
    {
        // 密度入口：返回 0，不抛异常、不替换任何 Mesh。
        Dictionary<Vector3Int, Mesh> beforeMeshes = SnapshotMeshes(views);
        terrain.VoxelField.Set(voxel, VoxelState.Air);
        int returned = terrain.RebuildChunksAffectedByVoxel(voxel);

        int replaced = 0;
        for (int i = 0; i < views.Count; i++)
        {
            if (views[i].Chunk.Mesh != beforeMeshes[views[i].Coordinate])
            {
                replaced++;
            }
        }

        Check(
            returned == 0 && replaced == 0,
            label + ": 越界 Voxel 的密度入口返回 0 且不重建（returned=" +
            returned + " replaced=" + replaced + "）"
        );

        terrain.VoxelField.Set(voxel, VoxelState.Solid);

        TerrainChunk found = VoxelAwareMeshRuntimeMutation.FindChunkContaining(
            ChunksOf(views),
            voxel,
            ChunkVoxelSize
        );

        Check(
            found == null,
            label + ": RuntimeMutation.FindChunkContaining 返回 null（越界 Voxel）"
        );

        bool committed = VoxelAwareMeshRuntimeMutation.ApplyVoxelStateChange(
            terrain.VoxelField,
            voxel,
            VoxelState.Air,
            ChunksOf(views),
            ChunkVoxelSize
        );

        Check(
            !committed &&
            VoxelAwareMeshRuntimeMutation.LastEvaluationFailed &&
            !VoxelAwareMeshRuntimeMutation.LastMutationCommitted &&
            !VoxelAwareMeshRuntimeMutation.LastAssemblyExecuted &&
            VoxelAwareMeshRuntimeMutation.LastReassembledChunks.Count == 0,
            label + ": 越界 Voxel 的 RuntimeMutation 整体 Abort（消息 = " +
            VoxelAwareMeshRuntimeMutation.LastEvaluationFailureMessage + "）"
        );

        // 已知语义：VoxelField 不回滚（本轮不修复）。
        Check(
            terrain.VoxelField.IsAir(voxel),
            label + ": 记录 —— Abort 后 VoxelField 仍保持已写入的 Air（无回滚）"
        );

        terrain.VoxelField.Set(voxel, VoxelState.Solid);
    }

    // =====================================================================
    // T7 — 异常 / 诊断路径
    // =====================================================================

    private static void CheckExceptionPaths()
    {
        GameObject terrainObject = CreateFixture(
            "StageB2_ExceptionPaths",
            Vector3Int.zero,
            new Vector3Int(2, 1, 2),
            true,
            false
        );

        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();
            List<TerrainChunkView> views = GetViews(terrainObject);

            // 目标 Chunk 不存在（地形外侧），两个入口的行为对照。
            CheckNotFoundBehaviour(
                terrain,
                views,
                new Vector3Int(1000, FlatSurfaceVoxelY, 1000),
                "StageB2-T7"
            );

            // 评估失败后不得产生任何 Mesh 替换。
            Dictionary<Vector3Int, Mesh> meshesBefore = SnapshotMeshes(views);

            VoxelAwareMeshRuntimeMutation.ApplyVoxelStateChange(
                terrain.VoxelField,
                new Vector3Int(-500, FlatSurfaceVoxelY, -500),
                VoxelState.Air,
                ChunksOf(views),
                ChunkVoxelSize
            );

            int replaced = 0;
            for (int i = 0; i < views.Count; i++)
            {
                if (views[i].Chunk.Mesh != meshesBefore[views[i].Coordinate])
                {
                    replaced++;
                }
            }

            Check(
                replaced == 0,
                "StageB2-T7: Abort 后没有任何 Chunk 的 Mesh 被替换 (" + replaced + ")"
            );

            // 一次修改后立即再次重建：几何必须保持。
            Vector3Int voxel = new Vector3Int(30, FlatSurfaceVoxelY, 30);
            VoxelAwareMeshRuntimeMutation.ApplyVoxelStateChange(
                terrain.VoxelField,
                voxel,
                VoxelState.Air,
                ChunksOf(views),
                ChunkVoxelSize
            );

            Dictionary<Vector3Int, MeshCanonical> mined = SnapshotCanonical(views);
            terrain.RebuildDirtyChunks();
            terrain.RebuildChunksAffectedByVoxel(voxel);

            bool stable = true;
            for (int i = 0; i < views.Count; i++)
            {
                TerrainChunk chunk = views[i].Chunk;
                MeshCanonical current = BuildCanonical(chunk.Mesh, chunk.ChunkOrigin);
                if (!SameCanonical(mined[chunk.Coordinate], current, out string _))
                {
                    stable = false;
                }
            }

            Check(
                stable,
                "StageB2-T7: 修改后立刻 RebuildDirtyChunks + 密度入口重建，几何保持稳定"
            );

            // 相邻 Voxel 连续修改（跨 Chunk 边界 x=74/75/76）。
            Vector3Int[] neighbours =
            {
                new Vector3Int(74, FlatSurfaceVoxelY, 31),
                new Vector3Int(75, FlatSurfaceVoxelY, 31),
                new Vector3Int(76, FlatSurfaceVoxelY, 31)
            };

            Dictionary<Vector3Int, MeshCanonical> beforeSequence =
                SnapshotCanonical(views);

            for (int i = 0; i < neighbours.Length; i++)
            {
                RunMutationCase(
                    terrain,
                    views,
                    neighbours[i],
                    "StageB2-T7[seq" + i + ":" + neighbours[i] + "]",
                    false
                );
            }

            Dictionary<Vector3Int, MeshCanonical> minedSequence =
                SnapshotCanonical(views);

            int sequenceChangedChunks = 0;
            for (int i = 0; i < views.Count; i++)
            {
                Vector3Int coordinate = views[i].Chunk.Coordinate;
                if (!SameCanonical(
                    beforeSequence[coordinate],
                    minedSequence[coordinate],
                    out string _))
                {
                    sequenceChangedChunks++;
                }
            }

            Check(
                sequenceChangedChunks > 0,
                "StageB2-T7: 相邻跨 Chunk 连续修改改变了几何 (" +
                sequenceChangedChunks + " Chunk)"
            );

            for (int i = 0; i < neighbours.Length; i++)
            {
                VoxelAwareMeshRuntimeMutation.ApplyVoxelStateChange(
                    terrain.VoxelField,
                    neighbours[i],
                    VoxelState.Solid,
                    ChunksOf(views),
                    ChunkVoxelSize
                );
            }

            bool sequenceRestored = true;
            string sequenceDetail = string.Empty;
            for (int i = 0; i < views.Count; i++)
            {
                TerrainChunk chunk = views[i].Chunk;
                MeshCanonical current = BuildCanonical(chunk.Mesh, chunk.ChunkOrigin);
                if (!SameCanonical(
                    beforeSequence[chunk.Coordinate],
                    current,
                    out string difference))
                {
                    sequenceRestored = false;
                    if (sequenceDetail.Length == 0)
                    {
                        sequenceDetail = chunk.Coordinate + ": " + difference;
                    }
                }
            }

            Check(
                sequenceRestored,
                "StageB2-T7: 相邻跨 Chunk 连续修改后全部还原，几何回到序列前基线" +
                (sequenceDetail.Length > 0 ? " —— " + sequenceDetail : string.Empty)
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    // =====================================================================
    // T9 — 缓存清理：mine → restore 后几何缓存必须与全新 Fixture 一致
    // =====================================================================

    private static void CheckCacheCleanupAgainstPristine()
    {
        GameObject pristine = CreateFixture(
            "StageB2_CachePristine",
            Vector3Int.zero,
            new Vector3Int(2, 1, 2),
            true,
            false
        );

        GameObject roundTripped = CreateFixture(
            "StageB2_CacheRoundTrip",
            Vector3Int.zero,
            new Vector3Int(2, 1, 2),
            true,
            false
        );

        try
        {
            List<TerrainChunkView> pristineViews = GetViews(pristine);
            List<TerrainChunkView> roundTripViews = GetViews(roundTripped);

            Dictionary<Vector3Int, GeometrySnapshot> baseline =
                SnapshotGeometry(pristineViews);

            DensityTerrain terrain =
                roundTripped.GetComponent<DensityTerrain>();

            Vector3Int[] voxels =
            {
                new Vector3Int(30, FlatSurfaceVoxelY, 30),
                new Vector3Int(75, FlatSurfaceVoxelY, 30),
                new Vector3Int(10, FlatSurfaceVoxelY, 50),
                new Vector3Int(75, FlatSurfaceVoxelY, 50)
            };

            for (int i = 0; i < voxels.Length; i++)
            {
                VoxelAwareMeshRuntimeMutation.ApplyVoxelStateChange(
                    terrain.VoxelField,
                    voxels[i],
                    VoxelState.Air,
                    ChunksOf(roundTripViews),
                    ChunkVoxelSize
                );

                VoxelAwareMeshRuntimeMutation.ApplyVoxelStateChange(
                    terrain.VoxelField,
                    voxels[i],
                    VoxelState.Solid,
                    ChunksOf(roundTripViews),
                    ChunkVoxelSize
                );
            }

            Dictionary<Vector3Int, GeometrySnapshot> restored =
                SnapshotGeometry(roundTripViews);

            bool equal = true;
            StringBuilder detail = new StringBuilder();

            foreach (KeyValuePair<Vector3Int, GeometrySnapshot> pair in baseline)
            {
                GeometrySnapshot current;
                if (!restored.TryGetValue(pair.Key, out current))
                {
                    equal = false;
                    detail.Append(" 缺少 ").Append(pair.Key);
                    continue;
                }

                if (!SameGeometry(pair.Value, current, out string difference))
                {
                    equal = false;
                    detail.Append(' ').Append(pair.Key).Append(':')
                        .Append(difference).Append(' ');
                }
            }

            Check(
                equal,
                "StageB2-T9: " + voxels.Length +
                " 轮 mine→restore 后几何缓存与全新 Fixture 完全一致" +
                (equal ? string.Empty : " —— " + detail)
            );

            // 可见几何同样必须一致。
            Dictionary<Vector3Int, MeshCanonical> baselineMesh =
                SnapshotCanonical(pristineViews);
            Dictionary<Vector3Int, MeshCanonical> restoredMesh =
                SnapshotCanonical(roundTripViews);

            bool meshEqual = true;
            string meshDetail = string.Empty;
            for (int i = 0; i < roundTripViews.Count; i++)
            {
                Vector3Int coordinate = roundTripViews[i].Chunk.Coordinate;
                if (!SameCanonical(
                    baselineMesh[coordinate],
                    restoredMesh[coordinate],
                    out string difference))
                {
                    meshEqual = false;
                    if (meshDetail.Length == 0)
                    {
                        meshDetail = coordinate + ": " + difference;
                    }
                }
            }

            Check(
                meshEqual,
                "StageB2-T9: 同一轮往返后可见几何与全新 Fixture 一致" +
                (meshDetail.Length > 0 ? " —— " + meshDetail : string.Empty)
            );

            Debug.Log(
                "[StageB2-T9] contribution keys: pristine=" +
                CountContributionKeys(pristineViews) +
                " roundTripped=" + CountContributionKeys(roundTripViews) +
                " visibility keys: pristine=" +
                CountVisibilityKeys(pristineViews) +
                " roundTripped=" + CountVisibilityKeys(roundTripViews)
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(pristine);
            UnityEngine.Object.DestroyImmediate(roundTripped);
        }
    }

    private static int CountContributionKeys(List<TerrainChunkView> views)
    {
        int count = 0;
        for (int i = 0; i < views.Count; i++)
        {
            if (views[i].Chunk.Geometry == null) continue;
            count += views[i].Chunk.Geometry.HiddenFaceContributions.Count;
        }

        return count;
    }

    private static int CountVisibilityKeys(List<TerrainChunkView> views)
    {
        int count = 0;
        for (int i = 0; i < views.Count; i++)
        {
            if (views[i].Chunk.Geometry == null) continue;
            count += views[i].Chunk.Geometry.FaceFragmentVisibility.Count;
        }

        return count;
    }

    // =====================================================================
    // T8 — 增量更新 vs 完整重建
    // =====================================================================

    private static void CheckFullRebuildComparison()
    {
        // (a) 密度路径：增量（RebuildChunksAffectedByVoxel）vs Regenerate 完整重建。
        GameObject terrainObject = CreateFixture(
            "StageB2_FullRebuildDensity",
            Vector3Int.zero,
            new Vector3Int(2, 1, 2),
            false,
            false
        );

        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();
            List<TerrainChunkView> views = GetViews(terrainObject);

            Vector3Int[] voxels =
            {
                new Vector3Int(30, FlatSurfaceVoxelY, 30),
                new Vector3Int(74, FlatSurfaceVoxelY, 30),
                new Vector3Int(75, FlatSurfaceVoxelY, 50)
            };

            for (int i = 0; i < voxels.Length; i++)
            {
                terrain.VoxelField.Set(voxels[i], VoxelState.Air);
                terrain.RebuildChunksAffectedByVoxel(voxels[i]);
            }

            Dictionary<Vector3Int, MeshCanonical> incremental =
                SnapshotCanonical(views);

            // 完整重建：Regenerate 重新生成 DensityField / MaterialField / Mesh
            // （precut OFF 时 BuildChunk 会重新构造 Effective Density，因此真值来自当前 VoxelField）。
            terrain.Regenerate();

            List<TerrainChunkView> regeneratedViews = GetViews(terrainObject);
            Dictionary<Vector3Int, MeshCanonical> full =
                SnapshotCanonical(regeneratedViews);

            Check(
                regeneratedViews.Count == views.Count,
                "StageB2-T8a: Regenerate 后 Chunk 数量一致 (" +
                regeneratedViews.Count + " vs " + views.Count + ")"
            );

            bool allEqual = true;
            string detail = string.Empty;
            for (int i = 0; i < regeneratedViews.Count; i++)
            {
                TerrainChunk chunk = regeneratedViews[i].Chunk;
                MeshCanonical incrementalMesh;
                if (!incremental.TryGetValue(chunk.Coordinate, out incrementalMesh))
                {
                    allEqual = false;
                    detail = chunk.Coordinate + ": 增量结果缺少该 Chunk";
                    continue;
                }

                if (!SameCanonical(
                    incrementalMesh,
                    full[chunk.Coordinate],
                    out string difference))
                {
                    allEqual = false;
                    if (detail.Length == 0)
                    {
                        detail = chunk.Coordinate + ": " + difference;
                    }
                }
            }

            Check(
                allEqual,
                "StageB2-T8a: 密度路径增量更新 == 完整重建（顶点/三角形/面积，容差 " +
                Quantum + "）" +
                (detail.Length > 0 ? " —— " + detail : string.Empty)
            );

            // 完整重建的确定性：全新 Fixture 预置同一批 Voxel 状态后完整重建。
            GameObject fresh = CreateFixture(
                "StageB2_FullRebuildFresh",
                Vector3Int.zero,
                new Vector3Int(2, 1, 2),
                false,
                false
            );

            try
            {
                DensityTerrain freshTerrain = fresh.GetComponent<DensityTerrain>();
                for (int i = 0; i < voxels.Length; i++)
                {
                    freshTerrain.VoxelField.Set(voxels[i], VoxelState.Air);
                }

                // 让完整重建真正发生（VoxelField 状态已就位）。
                freshTerrain.Regenerate();

                List<TerrainChunkView> freshViews = GetViews(fresh);
                Dictionary<Vector3Int, MeshCanonical> freshCanonical =
                    SnapshotCanonical(freshViews);

                bool deterministic = true;
                for (int i = 0; i < freshViews.Count; i++)
                {
                    Vector3Int coordinate = freshViews[i].Chunk.Coordinate;
                    if (!SameCanonical(
                        freshCanonical[coordinate],
                        full[coordinate],
                        out string _))
                    {
                        deterministic = false;
                    }
                }

                Check(
                    deterministic,
                    "StageB2-T8a: Regenerate 完整重建与全新 Fixture 的基线几何一致（确定性）"
                );
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(fresh);
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }

        // (b) Voxel-Aware 路径：是否存在“从当前 VoxelField 状态完整重建”的能力？
        GameObject precut = CreateFixture(
            "StageB2_FullRebuildPrecut",
            Vector3Int.zero,
            new Vector3Int(2, 1, 2),
            true,
            false
        );

        try
        {
            DensityTerrain terrain = precut.GetComponent<DensityTerrain>();
            List<TerrainChunkView> views = GetViews(precut);
            Vector3Int voxel = new Vector3Int(30, FlatSurfaceVoxelY, 30);

            Dictionary<Vector3Int, MeshCanonical> baseline =
                SnapshotCanonical(views);

            bool mined = VoxelAwareMeshRuntimeMutation.ApplyVoxelStateChange(
                terrain.VoxelField,
                voxel,
                VoxelState.Air,
                ChunksOf(views),
                ChunkVoxelSize
            );

            Check(mined, "StageB2-T8b: mutation 提交");

            Dictionary<Vector3Int, MeshCanonical> incremental =
                SnapshotCanonical(views);

            terrain.Regenerate();

            List<TerrainChunkView> regenerated = GetViews(precut);
            Dictionary<Vector3Int, MeshCanonical> afterRegenerate =
                SnapshotCanonical(regenerated);

            int equalToIncremental = 0;
            int equalToBaseline = 0;
            for (int i = 0; i < regenerated.Count; i++)
            {
                Vector3Int coordinate = regenerated[i].Chunk.Coordinate;
                if (SameCanonical(
                    incremental[coordinate],
                    afterRegenerate[coordinate],
                    out string _))
                {
                    equalToIncremental++;
                }

                if (SameCanonical(
                    baseline[coordinate],
                    afterRegenerate[coordinate],
                    out string _))
                {
                    equalToBaseline++;
                }
            }

            Check(
                terrain.VoxelField.IsAir(voxel),
                "StageB2-T8b: Regenerate 后 VoxelField 仍记录 Air"
            );

            Debug.Log(
                "[StageB2-T8b] precut ON + Regenerate: chunks=" +
                regenerated.Count + " equalToIncremental=" + equalToIncremental +
                " equalToBaseline=" + equalToBaseline
            );

            // B3-A（F2）之后：Regenerate 从权威 VoxelField 重放显式 Air Voxel，
            // 因此重建结果必须等于增量结果（已挖掘 Voxel 保持挖空）。
            Check(
                equalToIncremental == regenerated.Count,
                "StageB2-T8b: Regenerate 从 VoxelField 恢复状态，几何等于增量结果" +
                "（等于增量 " + equalToIncremental + "/" + regenerated.Count +
                "，等于基线 " + equalToBaseline + "/" + regenerated.Count + "）"
            );

            Limit(
                "B3-A（F2）已修复：Voxel-Aware 路径现在具备从当前 VoxelField 状态完整重建的通道" +
                "（Regenerate / RebuildVoxelAwareGeometryFromVoxelField 重放显式 Air Voxel）。"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(precut);
        }
    }

    // =====================================================================
    // 独立判据实现
    // =====================================================================

    /// <summary>O1：Chunk 是否含有落在该 Voxel 内的 Density 样本。</summary>
    private static HashSet<Vector3Int> ChunksWithSamplesInVoxel(
        DensityTerrain terrain,
        List<TerrainChunkView> views,
        Vector3Int voxel)
    {
        HashSet<Vector3Int> result = new HashSet<Vector3Int>();

        for (int i = 0; i < views.Count; i++)
        {
            TerrainChunk chunk = views[i].Chunk;
            DensityField field = chunk.Density;
            if (field == null) continue;

            bool hit = false;
            for (int x = 0; x < field.SampleCountX && !hit; x++)
            {
                for (int y = 0; y < field.SampleCountY && !hit; y++)
                {
                    for (int z = 0; z < field.SampleCountZ && !hit; z++)
                    {
                        Vector3 position = field.GetLocalPosition(x, y, z);
                        if (terrain.VoxelField.PositionToVoxel(position) == voxel)
                        {
                            hit = true;
                        }
                    }
                }
            }

            if (hit) result.Add(chunk.Coordinate);
        }

        return result;
    }

    /// <summary>O2：LocalBounds 与 Voxel 盒（闭区间）相交的 Chunk —— 结构上界。</summary>
    private static HashSet<Vector3Int> TouchingChunks(
        List<TerrainChunkView> views,
        Vector3Int voxel)
    {
        HashSet<Vector3Int> result = new HashSet<Vector3Int>();
        Vector3 min = new Vector3(voxel.x, voxel.y, voxel.z);
        Vector3 max = min + Vector3.one;

        for (int i = 0; i < views.Count; i++)
        {
            TerrainChunk chunk = views[i].Chunk;
            Bounds bounds = chunk.LocalBounds;

            if (bounds.max.x >= min.x - RayTolerance &&
                bounds.min.x <= max.x + RayTolerance &&
                bounds.max.y >= min.y - RayTolerance &&
                bounds.min.y <= max.y + RayTolerance &&
                bounds.max.z >= min.z - RayTolerance &&
                bounds.min.z <= max.z + RayTolerance)
            {
                result.Add(chunk.Coordinate);
            }
        }

        return result;
    }

    /// <summary>O3：几何缓存内容快照。</summary>
    private sealed class GeometrySnapshot
    {
        public string ActiveFragments = string.Empty;
        public string Visibility = string.Empty;
        public string Contributions = string.Empty;
        public string SpatialFaces = string.Empty;
    }

    private static Dictionary<Vector3Int, GeometrySnapshot> SnapshotGeometry(
        List<TerrainChunkView> views)
    {
        Dictionary<Vector3Int, GeometrySnapshot> result =
            new Dictionary<Vector3Int, GeometrySnapshot>();

        for (int i = 0; i < views.Count; i++)
        {
            TerrainChunk chunk = views[i].Chunk;
            result[chunk.Coordinate] = BuildGeometrySnapshot(chunk);
        }

        return result;
    }

    private static GeometrySnapshot BuildGeometrySnapshot(TerrainChunk chunk)
    {
        GeometrySnapshot snapshot = new GeometrySnapshot();
        if (chunk == null || chunk.Geometry == null) return snapshot;

        VoxelAwareMeshPrecut.ChunkGeometry geometry = chunk.Geometry;

        List<string> activeFragments = new List<string>();
        for (int i = 0; i < geometry.Fragments.Count; i++)
        {
            VoxelAwareMeshPrecut.Fragment fragment = geometry.Fragments[i];
            if (!fragment.Active) continue;

            activeFragments.Add(
                fragment.Voxel + "|s" + fragment.Submesh + "|a" +
                Mathf.RoundToInt(fragment.Area * 1000f) + "|c" +
                PolygonDigest(fragment.Polygon)
            );
        }

        activeFragments.Sort(StringComparer.Ordinal);
        snapshot.ActiveFragments = string.Join(";", activeFragments);

        List<string> visibility = new List<string>();
        foreach (KeyValuePair<int, bool> pair in geometry.FaceFragmentVisibility)
        {
            visibility.Add(pair.Key + ":" + (pair.Value ? "1" : "0"));
        }

        visibility.Sort(StringComparer.Ordinal);
        snapshot.Visibility = string.Join(";", visibility);

        List<string> contributions = new List<string>();
        foreach (KeyValuePair<string, List<VoxelAwareMeshSpatialFace.HiddenFaceFragment>>
            pair in geometry.HiddenFaceContributions)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append(pair.Key).Append('#');
            if (pair.Value != null)
            {
                builder.Append(pair.Value.Count).Append('#');
                for (int i = 0; i < pair.Value.Count; i++)
                {
                    VoxelAwareMeshSpatialFace.HiddenFaceFragment hidden =
                        pair.Value[i];
                    if (hidden == null)
                    {
                        builder.Append("null,");
                        continue;
                    }

                    builder.Append(hidden.Active ? "1" : "0")
                        .Append('@')
                        .Append(PolygonDigest(hidden.Polygon))
                        .Append(',');
                }
            }

            contributions.Add(builder.ToString());
        }

        contributions.Sort(StringComparer.Ordinal);
        snapshot.Contributions = string.Join(";", contributions);

        List<string> spatialFaces = new List<string>();
        for (int i = 0; i < geometry.SpatialFaces.Count; i++)
        {
            VoxelAwareMeshSpatialFace.SpatialFaceRecord record =
                geometry.SpatialFaces[i];
            if (record == null)
            {
                spatialFaces.Add("null");
                continue;
            }

            spatialFaces.Add(
                record.Face.Key + "#" + record.HiddenFaces.Count + "#" +
                (record.Evaluated ? "1" : "0")
            );
        }

        spatialFaces.Sort(StringComparer.Ordinal);
        snapshot.SpatialFaces = string.Join(";", spatialFaces);

        return snapshot;
    }

    private static string PolygonDigest(List<Vector3> polygon)
    {
        if (polygon == null || polygon.Count == 0) return "0";

        Vector3 sum = Vector3.zero;
        for (int i = 0; i < polygon.Count; i++) sum += polygon[i];

        Vector3 centroid = sum / polygon.Count;
        return polygon.Count + "*" +
               Quantize(centroid.x) + "," +
               Quantize(centroid.y) + "," +
               Quantize(centroid.z);
    }

    private static bool SameGeometry(
        GeometrySnapshot a,
        GeometrySnapshot b,
        out string difference)
    {
        difference = string.Empty;

        if (a.ActiveFragments != b.ActiveFragments)
        {
            difference = "ActiveFragments 变化";
            return false;
        }

        if (a.Visibility != b.Visibility)
        {
            difference = "FaceFragmentVisibility 变化";
            return false;
        }

        if (a.Contributions != b.Contributions)
        {
            difference = "HiddenFaceContributions 变化";
            return false;
        }

        if (a.SpatialFaces != b.SpatialFaces)
        {
            difference = "SpatialFaces 变化";
            return false;
        }

        return true;
    }

    /// <summary>逐缓存部件的变更标记（用于确认 Contribution / Visibility 是否同步更新）。</summary>
    private static string GeometryChangeFlags(
        GeometrySnapshot a,
        GeometrySnapshot b)
    {
        StringBuilder builder = new StringBuilder();

        if (a.ActiveFragments != b.ActiveFragments) builder.Append("Active ");
        if (a.Visibility != b.Visibility) builder.Append("Visibility ");
        if (a.Contributions != b.Contributions) builder.Append("Contributions ");
        if (a.SpatialFaces != b.SpatialFaces) builder.Append("SpatialFaces ");

        return builder.Length == 0
            ? "none"
            : builder.ToString().Trim();
    }

    /// <summary>O4：规范化 Mesh（顶点集合 + 保持绕序的三角形集合 + 面积）。</summary>
    private sealed class MeshCanonical
    {
        public List<string> VertexKeys = new List<string>();
        public List<string> TriangleKeys = new List<string>();
        public double Area;
        public int VertexCount;
        public int TriangleCount;
    }

    private static Dictionary<Vector3Int, MeshCanonical> SnapshotCanonical(
        List<TerrainChunkView> views)
    {
        Dictionary<Vector3Int, MeshCanonical> result =
            new Dictionary<Vector3Int, MeshCanonical>();

        for (int i = 0; i < views.Count; i++)
        {
            TerrainChunk chunk = views[i].Chunk;
            result[chunk.Coordinate] = BuildCanonical(chunk.Mesh, chunk.ChunkOrigin);
        }

        return result;
    }

    private static MeshCanonical BuildCanonical(Mesh mesh, Vector3 origin)
    {
        MeshCanonical canonical = new MeshCanonical();
        if (mesh == null) return canonical;

        Vector3[] vertices = mesh.vertices;
        canonical.VertexCount = vertices.Length;

        HashSet<string> vertexKeys = new HashSet<string>();
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 world = vertices[i] + origin;
            vertexKeys.Add(Quantize(world.x) + "," + Quantize(world.y) + "," +
                           Quantize(world.z));
        }

        canonical.VertexKeys.AddRange(vertexKeys);
        canonical.VertexKeys.Sort(StringComparer.Ordinal);

        for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
        {
            int[] triangles = mesh.GetTriangles(submesh);
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                string a = VertexKey(vertices[triangles[i]] + origin);
                string b = VertexKey(vertices[triangles[i + 1]] + origin);
                string c = VertexKey(vertices[triangles[i + 2]] + origin);

                Vector3 pa = vertices[triangles[i]] + origin;
                Vector3 pb = vertices[triangles[i + 1]] + origin;
                Vector3 pc = vertices[triangles[i + 2]] + origin;
                canonical.Area += Vector3.Cross(pb - pa, pc - pa).magnitude * 0.5;

                canonical.TriangleKeys.Add(
                    "s" + submesh + "|" + RotateToSmallest(a, b, c)
                );
            }
        }

        canonical.TriangleCount = canonical.TriangleKeys.Count;
        canonical.TriangleKeys.Sort(StringComparer.Ordinal);
        return canonical;
    }

    private static string VertexKey(Vector3 position)
    {
        return Quantize(position.x) + "," + Quantize(position.y) + "," +
               Quantize(position.z);
    }

    private static string RotateToSmallest(string a, string b, string c)
    {
        if (string.CompareOrdinal(a, b) <= 0 && string.CompareOrdinal(a, c) <= 0)
        {
            return a + ">" + b + ">" + c;
        }

        if (string.CompareOrdinal(b, c) <= 0)
        {
            return b + ">" + c + ">" + a;
        }

        return c + ">" + a + ">" + b;
    }

    private static int Quantize(float value)
    {
        return Mathf.RoundToInt(value / Quantum);
    }

    private static bool SameCanonical(
        MeshCanonical a,
        MeshCanonical b,
        out string difference)
    {
        difference = string.Empty;

        if (a.VertexKeys.Count != b.VertexKeys.Count)
        {
            difference = "顶点数不同 " + a.VertexKeys.Count + " vs " +
                         b.VertexKeys.Count;
            return false;
        }

        if (a.TriangleKeys.Count != b.TriangleKeys.Count)
        {
            difference = "三角形数不同 " + a.TriangleKeys.Count + " vs " +
                         b.TriangleKeys.Count;
            return false;
        }

        for (int i = 0; i < a.VertexKeys.Count; i++)
        {
            if (a.VertexKeys[i] != b.VertexKeys[i])
            {
                difference = "顶点集合不同 @" + i + " " + a.VertexKeys[i] +
                             " vs " + b.VertexKeys[i];
                return false;
            }
        }

        for (int i = 0; i < a.TriangleKeys.Count; i++)
        {
            if (a.TriangleKeys[i] != b.TriangleKeys[i])
            {
                difference = "三角形集合不同 @" + i;
                return false;
            }
        }

        float tolerance = Mathf.Max(1e-3f, (float)(a.Area * 1e-5));
        if (Mathf.Abs((float)(a.Area - b.Area)) > tolerance)
        {
            difference = "总面积不同 " + a.Area.ToString("F6") + " vs " +
                         b.Area.ToString("F6");
            return false;
        }

        return true;
    }

    private static Dictionary<Vector3Int, Mesh> SnapshotMeshes(
        List<TerrainChunkView> views)
    {
        Dictionary<Vector3Int, Mesh> result =
            new Dictionary<Vector3Int, Mesh>();

        for (int i = 0; i < views.Count; i++)
        {
            TerrainChunk chunk = views[i].Chunk;
            result[chunk.Coordinate] = chunk.Mesh;
        }

        return result;
    }

    private static bool EffectiveFieldDiffers(
        DensityField before,
        DensityField after)
    {
        if (before == null || after == null) return before != after;

        for (int x = 0; x < before.SampleCountX; x++)
        {
            for (int y = 0; y < before.SampleCountY; y++)
            {
                for (int z = 0; z < before.SampleCountZ; z++)
                {
                    if (Mathf.Abs(before[x, y, z] - after[x, y, z]) > 1e-6f)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool TryFindSurfaceVoxel(
        List<TerrainChunkView> views,
        out Vector3Int voxel)
    {
        for (int i = 0; i < views.Count; i++)
        {
            TerrainChunk chunk = views[i].Chunk;
            if (chunk.Geometry == null) continue;

            List<Vector3Int> candidates = new List<Vector3Int>();
            foreach (KeyValuePair<Vector3Int, List<int>> pair in
                chunk.Geometry.VoxelFragmentIndex)
            {
                candidates.Add(pair.Key);
            }

            candidates.Sort(CompareVector3Int);

            for (int c = 0; c < candidates.Count; c++)
            {
                Vector3Int candidate = candidates[c];
                if (candidate.x < 10 || candidate.x > 65) continue;
                if (candidate.z < 10 || candidate.z > 45) continue;

                voxel = candidate;
                return true;
            }
        }

        voxel = Vector3Int.zero;
        return false;
    }

    private static int CompareVector3Int(Vector3Int a, Vector3Int b)
    {
        if (a.x != b.x) return a.x.CompareTo(b.x);
        if (a.y != b.y) return a.y.CompareTo(b.y);
        return a.z.CompareTo(b.z);
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    private static GameObject CreateFixture(
        string name,
        Vector3Int chunkStart,
        Vector3Int chunkCount,
        bool voxelAware,
        bool sceneLike)
    {
        GameObject terrainObject = new GameObject(name);
        DensityTerrain terrain = terrainObject.AddComponent<DensityTerrain>();

        terrain.voxelSize = 1f;
        terrain.chunkVoxelSize = ChunkVoxelSize;
        terrain.chunkCount = chunkCount;
        terrain.chunkStartCoordinate = chunkStart;
        terrain.resolution = 75;
        terrain.densityMode = DensityGenerationMode.HeightField;
        terrain.cavityRadius = 0f;
        terrain.regenerateInEditor = false;
        terrain.useVoxelAwareMeshPrecut = voxelAware;
        terrain.debugCutaway = sceneLike;
        terrain.debugCutX = 0f;
        terrain.debugCutWidth = 4f;
        terrain.useNoise = sceneLike;
        terrain.height = FlatHeight;
        terrain.noiseScale = 0.15f;
        terrain.noiseStrength = 2.37f;

        terrain.Regenerate();
        return terrainObject;
    }

    private static List<TerrainChunkView> GetViews(GameObject terrainObject)
    {
        TerrainChunkView[] array = terrainObject
            .GetComponentsInChildren<TerrainChunkView>(true);

        return new List<TerrainChunkView>(array);
    }

    private static List<TerrainChunk> ChunksOf(List<TerrainChunkView> views)
    {
        List<TerrainChunk> chunks = new List<TerrainChunk>();
        for (int i = 0; i < views.Count; i++) chunks.Add(views[i].Chunk);
        return chunks;
    }

    private static TerrainChunk FindChunk(
        List<TerrainChunkView> views,
        Vector3Int coordinate)
    {
        for (int i = 0; i < views.Count; i++)
        {
            if (views[i].Coordinate == coordinate) return views[i].Chunk;
        }

        return null;
    }

    private static int CountDirtyChunks(List<TerrainChunkView> views)
    {
        int count = 0;
        for (int i = 0; i < views.Count; i++)
        {
            if (views[i].Chunk.IsDirty) count++;
        }

        return count;
    }

    private static HashSet<Vector3Int> Difference(
        HashSet<Vector3Int> a,
        HashSet<Vector3Int> b)
    {
        HashSet<Vector3Int> result = new HashSet<Vector3Int>();
        foreach (Vector3Int value in a)
        {
            if (!b.Contains(value)) result.Add(value);
        }

        return result;
    }

    private static bool SetEquals(
        HashSet<Vector3Int> a,
        HashSet<Vector3Int> b)
    {
        if (a.Count != b.Count) return false;
        foreach (Vector3Int value in a)
        {
            if (!b.Contains(value)) return false;
        }

        return true;
    }

    private static string Format(IEnumerable<Vector3Int> values)
    {
        List<string> items = new List<string>();
        foreach (Vector3Int value in values)
        {
            items.Add("(" + value.x + "," + value.y + "," + value.z + ")");
        }

        items.Sort(StringComparer.Ordinal);
        return items.Count == 0 ? "∅" : string.Join(" ", items);
    }

    private static bool Approx(Vector3 a, Vector3 b)
    {
        return (a - b).sqrMagnitude < 1e-6f;
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

    /// <summary>记录结论（不计入断言，但会打印到日志）。</summary>
    private static void Limit(string message)
    {
        Debug.Log("[StageB2-LIMITATION] " + message);
    }

    private static void LogSummary()
    {
        Debug.Log(
            "[VoxelCoordinateMigrationStageB2Validation] ===== SUMMARY =====\n" +
            "Checks = " + checks + "\n" +
            "Failures = " + failures + "\n" +
            "Result = " + (failures == 0 ? "PASS" : "FAIL")
        );
    }
}
