using System.Collections.Generic;
using TerrainModificationV2;
using UnityEngine;

/// <summary>
/// VoxelAwareMesh Runtime Phase 2B：
/// VoxelField Mutation → Fragment visibility → SpatialFace evaluation →
/// Hidden Face contribution → Dirty Chunk reassembly。
///
/// 纯 Runtime 数据协调；不接入 Player Mining / Collider。
/// </summary>
public static class VoxelAwareMeshRuntimeMutation
{
    public const float Tolerance = 1e-5f;

    public static int LastReassembledChunkCount { get; private set; }
    public static int LastEvaluatedFaceCount { get; private set; }
    public static bool LastBaseDensityDomainGap { get; private set; }
    public static string LastBaseDensityDomainGapMessage { get; private set; }

    /// <summary>
    /// Spec 13.9.6：Cross-Chunk Boundary Graph 出现部分 Collinear Overlap
    /// （不同 SegmentIdentity）时必须报告 Validation Failure，不能静默合并。
    /// </summary>
    public static bool LastBoundaryGraphValidationFailure { get; private set; }
    public static string LastBoundaryGraphValidationMessage { get; private set; }

    // ---------------------------------------------------------------
    // Mutation Transaction Diagnostics
    // Evaluation Success / Evaluation Failed / Mutation Committed /
    // Mutation Aborted / Assembly Executed / Assembly Skipped
    // ---------------------------------------------------------------

    /// <summary>本次 Mutation 是否出现 Face Evaluation Failure（Domain Gap 等）。</summary>
    public static bool LastEvaluationFailed { get; private set; }

    /// <summary>Evaluation Failure 的具体原因。</summary>
    public static string LastEvaluationFailureMessage { get; private set; }

    /// <summary>Face Evaluation 全部成功并已提交（Visibility / Contribution / Dirty）。</summary>
    public static bool LastMutationCommitted { get; private set; }

    /// <summary>是否已执行 Assemble（Failure 时必须为 false）。</summary>
    public static bool LastAssemblyExecuted { get; private set; }

    /// <summary>本次 Mutation 涉及的 Face 数量（含失败 Face）。</summary>
    public static int LastPlannedFaceCount { get; private set; }

    public static readonly List<Vector3Int> LastDirtyChunks =
        new List<Vector3Int>();
    public static readonly List<Vector3Int> LastEvaluationOwnerChunks =
        new List<Vector3Int>();
    public static readonly List<Vector3Int> LastMeshOwnerChunks =
        new List<Vector3Int>();
    public static readonly List<Vector3Int> LastReassembledChunks =
        new List<Vector3Int>();

    // ---- B3-D：阶段计时与调用计数（性能证据，不参与几何判定） ----
    /// <summary>最近一次 Mutation 的 Evaluation 阶段耗时（毫秒）。</summary>
    public static double LastEvaluationMilliseconds { get; private set; }

    /// <summary>最近一次 Mutation 的 Commit 阶段耗时（毫秒）。</summary>
    public static double LastCommitMilliseconds { get; private set; }

    /// <summary>最近一次 Mutation 的 Assemble 阶段耗时（毫秒）。</summary>
    public static double LastAssembleMilliseconds { get; private set; }

    /// <summary>最近一次 Mutation 中 Region/Coverage 判定的调用次数。</summary>
    public static int LastRegionSampleCount { get; private set; }

    /// <summary>Evaluation 内：BuildFaceSegments 边界图构建耗时（毫秒）。</summary>
    public static double LastSegmentBuildMilliseconds { get; private set; }

    /// <summary>Evaluation 内：ValidateBoundaryGraphSegments 校验耗时（毫秒）。</summary>
    public static double LastBoundaryGraphValidateMilliseconds { get; private set; }

    /// <summary>Evaluation 内：CollectFaceCoplanarPolygons 共面多边形收集耗时（毫秒）。</summary>
    public static double LastCollectCoplanarMilliseconds { get; private set; }

    /// <summary>Evaluation 内：BuildVisibilityTargets 可见性目标构建耗时（毫秒）。</summary>
    public static double LastVisibilityTargetsMilliseconds { get; private set; }

    /// <summary>Evaluation 内：SpatialFace.Evaluate 总耗时（毫秒）。</summary>
    public static double LastFaceEvaluateMilliseconds { get; private set; }

    /// <summary>Evaluation 内：覆盖分类 DecideRegion 累计耗时（毫秒）。</summary>
    public static double LastClassifyMilliseconds { get; private set; }

    private static long segmentBuildTicks;
    private static long boundaryGraphValidateTicks;
    private static long collectCoplanarTicks;
    private static long visibilityTargetsTicks;
    private static long faceEvaluateTicks;
    private static long classifyTicks;

    private static double TicksToMs(long ticks)
    {
        return ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
    }

    private static readonly Vector3Int[] Directions =
    {
        new Vector3Int(1, 0, 0),
        new Vector3Int(-1, 0, 0),
        new Vector3Int(0, 1, 0),
        new Vector3Int(0, -1, 0),
        new Vector3Int(0, 0, 1),
        new Vector3Int(0, 0, -1)
    };

    // =====================================================================
    // TEMP DIAGNOSTIC（缺面调查：仅 +Z FaceKey=2:50:26:16，只输出日志）
    // 不改变任何控制流/数据；调查结束后整块删除（含所有 TempTrace* 调用点）。
    // =====================================================================
    private const bool TempTraceEnabled = true;
    private const string TempTraceFaceKey = "2:50:26:16";
    private static readonly List<string> TempCoplanarSources =
        new List<string>();

    private static bool TempTraceFace(VoxelAwareMeshPrecut.FaceId face)
    {
        return TempTraceEnabled && face.Key == TempTraceFaceKey;
    }

    private static void TempLog(string tag, string message)
    {
        Debug.Log("[FaceTrace " + tag + "] " + message);
    }

    private static void TempTraceCoverage(
        VoxelAwareMeshPrecut.FaceId face,
        Vector2 uv,
        List<List<Vector2>> polygons)
    {
        if (!TempTraceFace(face)) return;

        int hit = -1;

        if (polygons != null)
        {
            for (int i = 0; i < polygons.Count; i++)
            {
                if (polygons[i] != null && polygons[i].Count >= 3 &&
                    PointInPolygon(uv, polygons[i]))
                {
                    hit = i;
                    break;
                }
            }
        }

        string source = (hit >= 0 && hit < TempCoplanarSources.Count)
            ? TempCoplanarSources[hit]
            : "<no-provenance>";

        TempLog(
            "COVERAGE",
            "Face=" + face.Key + " uv=" + uv.ToString("F4") +
            " IsCoveredByPolygons=" + (hit >= 0) +
            " coveringPolygonIndex=" + hit +
            " source=" + source +
            " polygonCount=" + (polygons != null ? polygons.Count : 0)
        );
    }
    // =================== TEMP DIAGNOSTIC END ===================

    public static bool ApplyVoxelStateChange(
        VoxelField voxelField,
        Vector3Int voxel,
        VoxelState newState,
        IReadOnlyList<TerrainChunk> chunks,
        Vector3Int chunkVoxelSize)
    {
        if (voxelField == null || chunks == null || chunks.Count == 0)
        {
            return false;
        }

        VoxelState previousState = voxelField.Get(voxel);
        voxelField.Set(voxel, newState);

        LastDirtyChunks.Clear();
        LastEvaluationOwnerChunks.Clear();
        LastMeshOwnerChunks.Clear();
        LastReassembledChunks.Clear();
        LastBaseDensityDomainGap = false;
        LastBaseDensityDomainGapMessage = string.Empty;
        LastBoundaryGraphValidationFailure = false;
        LastBoundaryGraphValidationMessage = string.Empty;
        LastEvaluationFailed = false;
        LastEvaluationFailureMessage = string.Empty;
        LastMutationCommitted = false;
        LastAssemblyExecuted = false;
        LastPlannedFaceCount = 0;
        LastEvaluatedFaceCount = 0;
        LastReassembledChunkCount = 0;

        Dictionary<Vector3Int, TerrainChunk> lookup =
            BuildLookup(chunks);

        TerrainChunk changedChunk = FindChunkContaining(
            chunks,
            voxel,
            chunkVoxelSize
        );
        if (changedChunk == null || changedChunk.Geometry == null)
        {
            FailEvaluation(
                "EVALUATION FAILURE: changed Chunk / ChunkGeometry missing." +
                " Voxel=" + voxel
            );
            return false;
        }

        // Scheme A 坐标契约检查：调用方传入的 Voxel 契约必须与 Chunk 自身一致，
        // 否则拒绝执行，避免静默混用 legacy 单位（例如 0.18m）与 1m Voxel 契约。
        if (changedChunk.VoxelSize != voxelField.VoxelSize)
        {
            FailEvaluation(
                "EVALUATION FAILURE: Voxel size contract mismatch." +
                " VoxelField.VoxelSize=" + voxelField.VoxelSize +
                " Chunk.VoxelSize=" + changedChunk.VoxelSize +
                " Chunk=" + changedChunk.Coordinate +
                " Voxel=" + voxel
            );
            return false;
        }

        if (changedChunk.ChunkVoxelSize != chunkVoxelSize)
        {
            FailEvaluation(
                "EVALUATION FAILURE: chunkVoxelSize argument mismatch." +
                " Argument=" + chunkVoxelSize +
                " Chunk.ChunkVoxelSize=" + changedChunk.ChunkVoxelSize +
                " Chunk=" + changedChunk.Coordinate +
                " Voxel=" + voxel
            );
            return false;
        }

        // -----------------------------------------------------------------
        // Phase 1 — Evaluation（只读）：
        // 6 个必要 SpatialFace 全部先完成 Evaluation，不写任何
        // FaceFragmentVisibility / HiddenFaceContribution / DirtySet。
        // -----------------------------------------------------------------
        System.Diagnostics.Stopwatch evaluationWatch =
            System.Diagnostics.Stopwatch.StartNew();
        LastRegionSampleCount = 0;
        segmentBuildTicks = 0;
        boundaryGraphValidateTicks = 0;
        collectCoplanarTicks = 0;
        visibilityTargetsTicks = 0;
        faceEvaluateTicks = 0;
        classifyTicks = 0;
        VoxelAwareMeshSpatialFace.ResetEvaluationDiagnostics();

        List<FaceEvaluationPlan> plans = new List<FaceEvaluationPlan>();
        for (int d = 0; d < Directions.Length; d++)
        {
            plans.Add(EvaluateFaceForDirection(
                voxelField,
                voxel,
                Directions[d],
                chunks,
                lookup,
                chunkVoxelSize
            ));
        }

        evaluationWatch.Stop();
        LastEvaluationMilliseconds = evaluationWatch.Elapsed.TotalMilliseconds;

        LastPlannedFaceCount = plans.Count;

        // 任意必要 Face Evaluation 失败（Domain Gap / Validation Failure）：
        // 整个 Mutation Abort，不提交、不 Assemble，不会出现部分更新 Mesh。
        for (int i = 0; i < plans.Count; i++)
        {
            if (!plans[i].Failed) continue;

            FailEvaluation(plans[i].FailureMessage);
            return false;
        }

        // -----------------------------------------------------------------
        // Phase 2 — Commit（全部 Evaluation 成功后一次性提交）
        // -----------------------------------------------------------------
        System.Diagnostics.Stopwatch commitWatch =
            System.Diagnostics.Stopwatch.StartNew();

        Dictionary<Vector3Int, bool> dirty =
            new Dictionary<Vector3Int, bool>();
        dirty[changedChunk.Coordinate] = true;

        RefreshVoxelFragments(
            changedChunk.Geometry,
            voxel,
            newState == VoxelState.Solid
        );

        for (int i = 0; i < plans.Count; i++)
        {
            if (plans[i].Skipped)
            {
                // 拓扑边界：只保留 Dirty 语义，不提交 Mesh 状态。
                // TEMP DIAGNOSTIC
                if (TempTraceFace(plans[i].Face))
                {
                    TempLog(
                        "COMMIT",
                        "Face=" + plans[i].Face.Key +
                        " SKIPPED plan（未提交 Visibility/Contribution）" +
                        " dirtyCount=" + plans[i].DirtyCoordinates.Count
                    );
                }

                for (int d = 0; d < plans[i].DirtyCoordinates.Count; d++)
                {
                    dirty[plans[i].DirtyCoordinates[d]] = true;
                }
                continue;
            }

            CommitFaceEvaluation(plans[i], dirty);
        }

        foreach (KeyValuePair<Vector3Int, bool> pair in dirty)
        {
            LastDirtyChunks.Add(pair.Key);
        }

        commitWatch.Stop();
        LastCommitMilliseconds = commitWatch.Elapsed.TotalMilliseconds;

        LastMutationCommitted = true;

        // -----------------------------------------------------------------
        // Phase 3 — Assemble（只在 Commit 成功后执行）
        // -----------------------------------------------------------------
        System.Diagnostics.Stopwatch assembleWatch =
            System.Diagnostics.Stopwatch.StartNew();
        ReassembleDirtyChunks(chunks, lookup, dirty);
        assembleWatch.Stop();
        LastAssembleMilliseconds = assembleWatch.Elapsed.TotalMilliseconds;

        LastAssemblyExecuted = LastReassembledChunkCount > 0;

        LastSegmentBuildMilliseconds = TicksToMs(segmentBuildTicks);
        LastBoundaryGraphValidateMilliseconds =
            TicksToMs(boundaryGraphValidateTicks);
        LastCollectCoplanarMilliseconds = TicksToMs(collectCoplanarTicks);
        LastVisibilityTargetsMilliseconds = TicksToMs(visibilityTargetsTicks);
        LastFaceEvaluateMilliseconds = TicksToMs(faceEvaluateTicks);
        LastClassifyMilliseconds = TicksToMs(classifyTicks);

        return true;
    }

    /// <summary>
    /// Evaluation Failure：整体 Abort。
    /// VoxelField 不回滚（权威状态），但不提交 Visibility /
    /// HiddenFaceContribution / DirtySet，也不执行 Assemble。
    /// </summary>
    private static void FailEvaluation(string message)
    {
        LastEvaluationFailed = true;
        LastEvaluationFailureMessage = string.IsNullOrEmpty(message)
            ? "EVALUATION FAILURE"
            : message;
        LastMutationCommitted = false;
        LastAssemblyExecuted = false;
        LastReassembledChunkCount = 0;
        LastDirtyChunks.Clear();
        LastReassembledChunks.Clear();
    }

    public static void ReassembleDirtyChunks(
        IReadOnlyList<TerrainChunk> chunks,
        Dictionary<Vector3Int, TerrainChunk> lookup,
        Dictionary<Vector3Int, bool> dirty)
    {
        LastReassembledChunkCount = 0;
        if (dirty == null) return;

        foreach (KeyValuePair<Vector3Int, bool> pair in dirty)
        {
            if (!lookup.TryGetValue(pair.Key, out TerrainChunk chunk))
            {
                continue;
            }

            if (chunk == null || chunk.Geometry == null) continue;

            Mesh assembled = VoxelAwareMeshPrecut.Assemble(chunk.Geometry);
            if (assembled == null) continue;

            chunk.ReplaceAssembledMesh(assembled);
            LastReassembledChunkCount++;
            LastReassembledChunks.Add(pair.Key);
        }
    }

    public static Dictionary<Vector3Int, TerrainChunk> BuildLookup(
        IReadOnlyList<TerrainChunk> chunks)
    {
        Dictionary<Vector3Int, TerrainChunk> lookup =
            new Dictionary<Vector3Int, TerrainChunk>();
        if (chunks == null) return lookup;

        for (int i = 0; i < chunks.Count; i++)
        {
            TerrainChunk chunk = chunks[i];
            if (chunk == null) continue;
            lookup[chunk.Coordinate] = chunk;
        }

        return lookup;
    }

    public static TerrainChunk FindChunkContaining(
        IReadOnlyList<TerrainChunk> chunks,
        Vector3Int voxel,
        Vector3Int chunkVoxelSize)
    {
        if (chunks == null) return null;

        Vector3Int coordinate = ChunkSpace.VoxelToChunk(
            voxel,
            chunkVoxelSize
        );

        for (int i = 0; i < chunks.Count; i++)
        {
            TerrainChunk chunk = chunks[i];
            if (chunk != null && chunk.Coordinate == coordinate)
            {
                return chunk;
            }
        }

        return null;
    }

    private static void RefreshVoxelFragments(
        VoxelAwareMeshPrecut.ChunkGeometry geometry,
        Vector3Int voxel,
        bool active)
    {
        if (!geometry.VoxelFragmentIndex.TryGetValue(
            voxel,
            out List<int> fragmentIds))
        {
            return;
        }

        for (int i = 0; i < fragmentIds.Count; i++)
        {
            int id = fragmentIds[i];
            if (id >= 0 && id < geometry.Fragments.Count)
            {
                geometry.Fragments[id].Active = active;
            }
        }
    }

    /// <summary>
    /// Phase 1 — 单个 SpatialFace 的 Evaluation（只读）。
    /// 返回 FaceEvaluationPlan；不写 FaceFragmentVisibility /
    /// HiddenFaceContribution / DirtySet，失败时只返回 EvaluationFailed 状态。
    /// </summary>
    private static FaceEvaluationPlan EvaluateFaceForDirection(
        VoxelField voxelField,
        Vector3Int voxel,
        Vector3Int direction,
        IReadOnlyList<TerrainChunk> chunks,
        Dictionary<Vector3Int, TerrainChunk> lookup,
        Vector3Int chunkVoxelSize)
    {
        FaceEvaluationPlan plan = new FaceEvaluationPlan();

        VoxelAwareMeshPrecut.FaceId face;
        Vector3Int negativeVoxel;
        Vector3Int positiveVoxel;
        bool hasPositive;

        if (!TryGetFaceForDirection(
            voxel,
            direction,
            out face,
            out negativeVoxel,
            out positiveVoxel,
            out hasPositive))
        {
            return plan;
        }

        plan.Face = face;

        bool negativeSolid = voxelField.Get(negativeVoxel) == VoxelState.Solid;
        bool positiveSolid = hasPositive &&
                             voxelField.Get(positiveVoxel) == VoxelState.Solid;

        plan.NegativeSolid = negativeSolid;
        plan.PositiveSolid = positiveSolid;

        bool currentBoundary =
            (negativeSolid && !positiveSolid) ||
            (!negativeSolid && positiveSolid);

        plan.CurrentBoundary = currentBoundary;

        Vector3Int ownerCoordinate;
        TerrainChunk evaluationOwner = ResolveEvaluationOwner(
            face,
            negativeVoxel,
            positiveVoxel,
            chunkVoxelSize,
            lookup,
            out ownerCoordinate
        );

        plan.OwnerCoordinate = ownerCoordinate;
        plan.EvaluationOwner = evaluationOwner;

        if (evaluationOwner == null || evaluationOwner.Geometry == null)
        {
            // 拓扑边界：Negative-side Voxel 不在任何 Chunk 内（Terrain 外侧）。
            // 该 Face 无可评估对象，不属于 Domain Gap；
            // 保持既有边界语义：不提交、不 Dirty、不 Assemble（跳过）。
            // TEMP DIAGNOSTIC
            if (TempTraceFace(face))
            {
                TempLog(
                    "EVAL",
                    "Face=" + face.Key + " SKIPPED: evaluationOwner==null" +
                    " negativeVoxel=" + negativeVoxel +
                    " positiveVoxel=" + positiveVoxel +
                    " hasPositive=" + hasPositive +
                    " ownerCoordinate=" + ownerCoordinate
                );
            }

            plan.Skipped = true;
            return plan;
        }

        TerrainChunk negativeChunk = FindChunkContaining(
            chunks,
            negativeVoxel,
            chunkVoxelSize
        );
        TerrainChunk positiveChunk = hasPositive
            ? FindChunkContaining(chunks, positiveVoxel, chunkVoxelSize)
            : null;

        plan.NegativeChunk = negativeChunk;
        plan.PositiveChunk = positiveChunk;

        long segmentBuildStart = System.Diagnostics.Stopwatch.GetTimestamp();
        List<VoxelAwareMeshSpatialFace.FaceSegment> segments =
            BuildFaceSegments(
                face,
                evaluationOwner,
                negativeChunk,
                positiveChunk
            );

        segmentBuildTicks +=
            System.Diagnostics.Stopwatch.GetTimestamp() - segmentBuildStart;

        // Spec 13.9.6：部分 Collinear Overlap 是 Validation Failure，
        // 不能被 Planar Graph 静默吞掉，也不能继续 Assemble。
        long validateStart = System.Diagnostics.Stopwatch.GetTimestamp();
        bool segmentsValid = ValidateBoundaryGraphSegments(
            face, segments, out string overlapError);
        boundaryGraphValidateTicks +=
            System.Diagnostics.Stopwatch.GetTimestamp() - validateStart;

        if (!segmentsValid)
        {
            LastBoundaryGraphValidationFailure = true;
            LastBoundaryGraphValidationMessage = overlapError;

            // TEMP DIAGNOSTIC
            if (TempTraceFace(face))
            {
                TempLog(
                    "EVAL",
                    "Face=" + face.Key + " FAILED: segmentsInvalid 原因=" +
                    overlapError
                );
            }

            plan.Failed = true;
            plan.FailureMessage = overlapError;
            return plan;
        }

        LastEvaluatedFaceCount++;
        AddUnique(LastEvaluationOwnerChunks, ownerCoordinate);

        if (!currentBoundary)
        {
            // Spec 13.8.3：Solid/Solid 与 Air/Air 都没有 current
            // Solid/Air boundary，因此 Coplanar Base Geometry 在
            // Final Assembly 阶段被过滤（Base Geometry 本身不修改）。
            long visibilityStartNoBoundary =
                System.Diagnostics.Stopwatch.GetTimestamp();
            BuildVisibilityTargets(
                plan,
                null,
                chunks,
                negativeChunk,
                positiveChunk,
                evaluationOwner
            );
            visibilityTargetsTicks +=
                System.Diagnostics.Stopwatch.GetTimestamp() - visibilityStartNoBoundary;

            plan.DirtyCoordinates.Add(ownerCoordinate);
            return plan;
        }

        TerrainChunk meshOwner = negativeSolid
            ? negativeChunk
            : positiveChunk;

        if (meshOwner == null || meshOwner.Geometry == null)
        {
            // 拓扑边界：Solid-side Voxel 不在任何 Chunk 内（Terrain 外侧）。
            // 没有 Hidden Face 的 Owner Chunk，也没有可提交的 Mesh 贡献；
            // 这不是 Domain Gap，保持既有语义：只 Dirty Owner，不 Abort。
            plan.Skipped = true;
            plan.DirtyCoordinates.Add(ownerCoordinate);
            return plan;
        }

        plan.MeshOwner = meshOwner;
        AddUnique(LastMeshOwnerChunks, meshOwner.Coordinate);

        Vector3Int solidVoxel = negativeSolid
            ? negativeVoxel
            : positiveVoxel;
        TerrainChunk solidChunk = negativeSolid
            ? negativeChunk
            : positiveChunk;

        MaterialField materialField = solidChunk != null &&
                                      solidChunk.Geometry != null
            ? solidChunk.Geometry.BaseMaterialField
            : meshOwner.Geometry.BaseMaterialField;

        bool solidIsNegative = negativeSolid;

        VoxelAwareMeshPrecut.ChunkGeometry derivedGeometry =
            new VoxelAwareMeshPrecut.ChunkGeometry
            {
                ChunkOrigin = meshOwner.Geometry.ChunkOrigin,
                VoxelSize = meshOwner.Geometry.VoxelSize,
                Pool = new VoxelAwareMeshPrecut.VertexPool()
            };

        string regionFailureMessage = string.Empty;

        // 性能：Coplanar Base Geometry 覆盖判定按 Face 预计算一次，
        // 供该面所有 Region / 细分采样点复用（原先每个采样点都要重新
        // 遍历三个 Chunk 的全部 FaceFragments 并分配临时 List）。
        long collectStart = System.Diagnostics.Stopwatch.GetTimestamp();

        // TEMP DIAGNOSTIC
        if (TempTraceFace(face)) TempCoplanarSources.Clear();

        List<List<Vector2>> coplanarPolygons = CollectFaceCoplanarPolygons(
            face,
            negativeChunk,
            positiveChunk,
            evaluationOwner
        );
        collectCoplanarTicks +=
            System.Diagnostics.Stopwatch.GetTimestamp() - collectStart;

        long faceEvaluateStart = System.Diagnostics.Stopwatch.GetTimestamp();
        VoxelAwareMeshSpatialFace.SpatialFaceRecord record =
            VoxelAwareMeshSpatialFace.Evaluate(
                face,
                negativeVoxel,
                positiveVoxel,
                hasPositive,
                !negativeSolid,
                !positiveSolid,
                ownerCoordinate,
                segments,
                materialField,
                meshOwner.Geometry.VoxelSize,
                derivedGeometry,
                uv =>
                {
                    LastRegionSampleCount++;
                    long classifyStart = System.Diagnostics.Stopwatch.GetTimestamp();
                    VoxelAwareMeshSpatialFace.RegionDecision decision =
                        DecideRegion(
                            face,
                            uv,
                            solidIsNegative,
                            solidVoxel,
                            coplanarPolygons,
                            evaluationOwner,
                            solidChunk,
                            out string failureMessage
                        );

                    if (decision ==
                            VoxelAwareMeshSpatialFace.RegionDecision
                                .EvaluationFailed &&
                        string.IsNullOrEmpty(regionFailureMessage))
                    {
                        regionFailureMessage = failureMessage;
                    }

                    classifyTicks +=
                        System.Diagnostics.Stopwatch.GetTimestamp() - classifyStart;
                    return decision;
                }
            );

        faceEvaluateTicks +=
            System.Diagnostics.Stopwatch.GetTimestamp() - faceEvaluateStart;

        if (record.EvaluationFailed)
        {
            // Domain Gap 等 Evaluation Failure：该 Face 不产生任何
            // HiddenFaceContribution / Visibility 提交，Mutation 将 Abort。
            // TEMP DIAGNOSTIC
            if (TempTraceFace(face))
            {
                TempLog(
                    "EVAL",
                    "Face=" + face.Key + " FAILED: record.EvaluationFailed" +
                    " Decision=" + record.Decision +
                    " 原因=" + (string.IsNullOrEmpty(regionFailureMessage)
                        ? record.EvaluationFailureMessage
                        : regionFailureMessage)
                );
            }

            plan.Failed = true;
            plan.FailureMessage = string.IsNullOrEmpty(regionFailureMessage)
                ? record.EvaluationFailureMessage
                : regionFailureMessage;
            return plan;
        }

        plan.Record = record;
        plan.HiddenFaces = record.HiddenFaces;

        // TEMP DIAGNOSTIC
        if (TempTraceFace(face))
        {
            TempLog(
                "EVAL",
                "Face=" + face.Key +
                " Decision=" + record.Decision +
                " EvaluationFailed=" + record.EvaluationFailed +
                " NegativeAir=" + record.NegativeAir +
                " PositiveAir=" + record.PositiveAir +
                " NegativeSolid=" + negativeSolid +
                " PositiveSolid=" + positiveSolid +
                " CurrentBoundary=" + currentBoundary +
                " HiddenFaces=" + (record.HiddenFaces != null
                    ? record.HiddenFaces.Count : -1) +
                " Regions=" + (record.Regions != null
                    ? record.Regions.Count : -1) +
                " MeshOwner=" + meshOwner.Coordinate +
                " EvaluationOwner=" + evaluationOwner.Coordinate
            );
        }

        // Spec 13.8.2 / 13.8.3：Final Visibility 是派生结果，
        // 用 Region Classification 的同一个 CoplanarBaseCoverage 判定。
        long visibilityStart = System.Diagnostics.Stopwatch.GetTimestamp();
        BuildVisibilityTargets(
            plan,
            record,
            chunks,
            negativeChunk,
            positiveChunk,
            evaluationOwner
        );
        visibilityTargetsTicks +=
            System.Diagnostics.Stopwatch.GetTimestamp() - visibilityStart;

        plan.DirtyCoordinates.Add(ownerCoordinate);
        plan.DirtyCoordinates.Add(meshOwner.Coordinate);
        return plan;
    }

    /// <summary>
    /// 单个 SpatialFace 的 Staged Evaluation 计划。
    /// Evaluation 阶段只读；Commit 阶段才写入 Visibility / Contribution / Dirty。
    /// </summary>
    private sealed class FaceEvaluationPlan
    {
        public VoxelAwareMeshPrecut.FaceId Face;
        public bool CurrentBoundary;
        public bool NegativeSolid;
        public bool PositiveSolid;
        public Vector3Int OwnerCoordinate;
        public TerrainChunk EvaluationOwner;
        public TerrainChunk NegativeChunk;
        public TerrainChunk PositiveChunk;
        public TerrainChunk MeshOwner;

        public VoxelAwareMeshSpatialFace.SpatialFaceRecord Record;
        public List<VoxelAwareMeshSpatialFace.HiddenFaceFragment> HiddenFaces;

        public readonly List<VisibilityTarget> Visibility =
            new List<VisibilityTarget>();
        public readonly List<Vector3Int> DirtyCoordinates =
            new List<Vector3Int>();

        public bool Failed;

        /// <summary>
        /// 拓扑边界（Solid side / Negative side 不在任何 Chunk 内）：
        /// 不提交、不参与 Abort 判定，仅保留 Dirty 语义。
        /// </summary>
        public bool Skipped;

        public string FailureMessage = string.Empty;
    }

    private sealed class VisibilityTarget
    {
        public VoxelAwareMeshPrecut.ChunkGeometry Geometry;
        public int FragmentIndex;
        public bool Visible;
    }

    /// <summary>
    /// Phase 2 — Commit：只有全部 Face Evaluation 成功后才写入
    /// FaceFragmentVisibility / HiddenFaceContribution / DirtySet。
    /// </summary>
    private static void CommitFaceEvaluation(
        FaceEvaluationPlan plan,
        Dictionary<Vector3Int, bool> dirty)
    {
        // 无论状态如何，先清掉该 Face 的旧 contribution，避免累积。
        RemoveContribution(plan.EvaluationOwner, plan.Face);
        if (plan.NegativeChunk != null) RemoveContribution(plan.NegativeChunk, plan.Face);
        if (plan.PositiveChunk != null) RemoveContribution(plan.PositiveChunk, plan.Face);

        // B3-C：任何被移除 contribution 的 Chunk 都必须重新 Assemble。
        // 否则当 Face 从 Boundary 变回 Solid/Solid（例如恢复 Solid）时，
        // 旧的 Hidden Face 会残留在 MeshOwner（可能是相邻 Chunk）的 Mesh 中。
        // Solid/Solid 路径不设置 plan.MeshOwner，因此这里补上 owner / 负侧 / 正侧。
        MarkDirty(dirty, plan.EvaluationOwner);
        MarkDirty(dirty, plan.NegativeChunk);
        MarkDirty(dirty, plan.PositiveChunk);
        MarkDirty(dirty, plan.MeshOwner);

        // 注（B3-E1 调查）：这里保持 Spec 13.8.3 的既有规则
        // `Visible = currentBoundary && coplanarBaseCoverage`（不做语义改动）。
        // 该规则在“基岩表面恰好落在 Voxel 平面、且两侧编辑状态均为 Solid”的
        // 情形下会把地表片段判为不可见（残留 Visible=false），属于本轮
        // 已定位但**未修复**的独立缺陷（见报告 §6.2），需要单独决策。
        for (int i = 0; i < plan.Visibility.Count; i++)
        {
            VisibilityTarget target = plan.Visibility[i];
            if (target.Geometry == null) continue;

            target.Geometry.FaceFragmentVisibility[target.FragmentIndex] =
                target.Visible;
        }

        if (plan.MeshOwner != null && plan.MeshOwner.Geometry != null)
        {
            if (plan.HiddenFaces != null && plan.HiddenFaces.Count > 0)
            {
                plan.MeshOwner.Geometry.HiddenFaceContributions[plan.Face.Key] =
                    plan.HiddenFaces;
            }
            else
            {
                plan.MeshOwner.Geometry.HiddenFaceContributions.Remove(
                    plan.Face.Key
                );
            }
        }

        // TEMP DIAGNOSTIC（回读被写入的 Contribution：验证是否 Commit / Active）
        if (TempTraceFace(plan.Face))
        {
            string ownerText = plan.MeshOwner != null
                ? plan.MeshOwner.Coordinate.ToString()
                : "null";

            List<VoxelAwareMeshSpatialFace.HiddenFaceFragment> stored = null;
            bool storedOk = plan.MeshOwner != null &&
                            plan.MeshOwner.Geometry != null &&
                            plan.MeshOwner.Geometry.HiddenFaceContributions
                                .TryGetValue(plan.Face.Key, out stored);

            int storedCount = storedOk && stored != null ? stored.Count : 0;
            int storedActive = 0;

            if (storedOk && stored != null)
            {
                for (int i = 0; i < stored.Count; i++)
                {
                    if (stored[i] != null && stored[i].Active) storedActive++;
                }
            }

            int visibilityFalse = 0;
            for (int i = 0; i < plan.Visibility.Count; i++)
            {
                if (!plan.Visibility[i].Visible) visibilityFalse++;
            }

            TempLog(
                "COMMIT",
                "Face=" + plan.Face.Key +
                " CurrentBoundary=" + plan.CurrentBoundary +
                " MeshOwner=" + ownerText +
                " HiddenFacesComputed=" + (plan.HiddenFaces != null
                    ? plan.HiddenFaces.Count : -1) +
                " contributionStored=" + storedOk +
                " contributionCount=" + storedCount +
                " contributionActive=" + storedActive +
                " visibilityTargets=" + plan.Visibility.Count +
                " visibilityFalse=" + visibilityFalse
            );
        }

        for (int i = 0; i < plan.DirtyCoordinates.Count; i++)
        {
            dirty[plan.DirtyCoordinates[i]] = true;
        }
    }

    /// <summary>B3-C：把可能持有旧 contribution 的 Chunk 标记为需要重新 Assemble。</summary>
    private static void MarkDirty(
        Dictionary<Vector3Int, bool> dirty,
        TerrainChunk chunk)
    {
        if (dirty == null || chunk == null) return;
        if (chunk.Geometry == null) return;

        dirty[chunk.Coordinate] = true;
    }

    /// <summary>
    /// Region Classification（Spec 13.7 / 13.10）的显式结果。
    /// Domain Gap 等失败返回 EvaluationFailed，绝不被折叠成 NoHiddenFace；
    /// 也不生成 Hidden Face，从而阻止错误 Mesh 进入 Final Assembly。
    ///
    /// B3-D 语义修正（材料边界模型）：
    ///   该函数只在 Face 处于“一侧已挖空 / 另一侧未挖空”时被调用，
    ///   故 `solidVoxel` 一侧代表仍然存在的材料，已挖空一侧是空腔。
    ///   内壁只在“未挖空一侧的基础地形确实有材料（Base Density > 0）”时生成，
    ///   即暴露出来的实体表面；若那一侧本来就是空气（Base Density <= 0），
    ///   生成任何面都会在空中造出盖板（旧实现正是如此）。
    /// </summary>
    private static VoxelAwareMeshSpatialFace.RegionDecision DecideRegion(
        VoxelAwareMeshPrecut.FaceId face,
        Vector2 uv,
        bool solidIsNegative,
        Vector3Int solidVoxel,
        List<List<Vector2>> coplanarPolygons,
        TerrainChunk evaluationOwner,
        TerrainChunk solidChunk,
        out string failureMessage)
    {
        failureMessage = string.Empty;

        if (solidChunk == null || solidChunk.Density == null)
        {
            failureMessage =
                "EVALUATION FAILURE: Solid-side Base DensityField missing." +
                " Face=" + face.Key +
                " Region=" + uv +
                " Side=" + (solidIsNegative ? "Negative" : "Positive") +
                " SolidVoxel=" + solidVoxel;
            return VoxelAwareMeshSpatialFace.RegionDecision.EvaluationFailed;
        }

        // Coplanar Base Geometry 覆盖：按 Face 预计算的多边形集合（等价于旧逐点扫描）。
        // TEMP DIAGNOSTIC（只读：重算同一判据并打印覆盖来源）
        TempTraceCoverage(face, uv, coplanarPolygons);

        if (IsCoveredByPolygons(uv, coplanarPolygons))
        {
            return VoxelAwareMeshSpatialFace.RegionDecision.NoHiddenFace;
        }

        Vector3 terrainLocal = VoxelAwareMeshSpatialFace.ToChunkLocal(
            face,
            uv,
            evaluationOwner.Geometry.ChunkOrigin,
            evaluationOwner.Geometry.VoxelSize
        ) + evaluationOwner.Geometry.ChunkOrigin;

        Vector3 normal = FaceNormal(face);
        Vector3 solidSample = solidIsNegative
            ? terrainLocal - normal * Tolerance
            : terrainLocal + normal * Tolerance;

        if (!TrySampleBaseDensity(
            solidChunk,
            solidSample,
            out float baseDensity,
            out string domainError))
        {
            LastBaseDensityDomainGap = true;
            LastBaseDensityDomainGapMessage =
                domainError +
                " Face=" + face.Key +
                " Region=" + uv +
                " Side=" + (solidIsNegative ? "Negative" : "Positive") +
                " Sample=" + (solidIsNegative ? "P-" : "P+") +
                " SolidVoxel=" + solidVoxel +
                " SolidChunk=" + solidChunk.Coordinate +
                " EvaluationOwnerChunk=" + evaluationOwner.Coordinate;

            failureMessage = LastBaseDensityDomainGapMessage;
            return VoxelAwareMeshSpatialFace.RegionDecision.EvaluationFailed;
        }

        // 材料边界模型（B3-D 修正）：
        //   Base Density > 0 ⇒ 未挖空一侧确实有材料 ⇒ 生成内壁（暴露出来的实体表面）
        //   Base Density <= 0 ⇒ 那一侧本来就是空气 ⇒ 不生成任何面（避免在空中造盖板）
        return baseDensity > 0f
            ? VoxelAwareMeshSpatialFace.RegionDecision.HiddenFace
            : VoxelAwareMeshSpatialFace.RegionDecision.NoHiddenFace;
    }

    /// <summary>
    /// 收集与该 Face 共面的 Base Geometry 多边形（Face Local 2D），按 Face 计算一次。
    /// 结果与原先 IsCoveredByCoplanar 的逐点重算完全等价，仅去掉重复扫描与分配。
    /// </summary>
    private static List<List<Vector2>> CollectFaceCoplanarPolygons(
        VoxelAwareMeshPrecut.FaceId face,
        TerrainChunk negativeChunk,
        TerrainChunk positiveChunk,
        TerrainChunk evaluationOwner)
    {
        List<List<Vector2>> result = new List<List<Vector2>>();

        AddFaceCoplanarPolygons(face, negativeChunk, result);
        AddFaceCoplanarPolygons(face, positiveChunk, result);
        AddFaceCoplanarPolygons(face, evaluationOwner, result);

        return result;
    }

    private static void AddFaceCoplanarPolygons(
        VoxelAwareMeshPrecut.FaceId face,
        TerrainChunk chunk,
        List<List<Vector2>> target)
    {
        if (chunk == null || chunk.Geometry == null) return;

        VoxelAwareMeshPrecut.ChunkGeometry geometry = chunk.Geometry;
        for (int i = 0; i < geometry.FaceFragments.Count; i++)
        {
            VoxelAwareMeshPrecut.FaceFragment fragment =
                geometry.FaceFragments[i];
            if (VoxelAwareMeshPrecut.UseLegacyFaceKeyComparison
                    ? fragment.Face.Key != face.Key
                    : !fragment.Face.SameAs(face))
            {
                continue;
            }

            List<Vector2> polygon = new List<Vector2>(fragment.Polygon.Count);
            for (int p = 0; p < fragment.Polygon.Count; p++)
            {
                polygon.Add(VoxelAwareMeshSpatialFace.ToFaceLocal(
                    face,
                    fragment.Polygon[p],
                    geometry.ChunkOrigin,
                    geometry.VoxelSize
                ));
            }

            target.Add(polygon);

            // TEMP DIAGNOSTIC（与 target 同序：记录覆盖多边形的来源）
            if (TempTraceFace(face))
            {
                bool overrideValue;
                bool hasOverride = geometry.FaceFragmentVisibility
                    .TryGetValue(i, out overrideValue);

                TempCoplanarSources.Add(
                    "chunk=" + chunk.Coordinate +
                    " fragmentIndex=" + i +
                    " active=" + fragment.Active +
                    " visibilityOverride=" + (hasOverride
                        ? overrideValue.ToString() : "<none>") +
                    " points=" + polygon.Count +
                    " area=" + fragment.Area.ToString("F4") +
                    " sourceTriangle=" + fragment.SourceTriangle
                );
            }
        }
    }

    private static bool IsCoveredByPolygons(
        Vector2 uv,
        List<List<Vector2>> polygons)
    {
        if (polygons == null) return false;

        for (int i = 0; i < polygons.Count; i++)
        {
            if (polygons[i] != null && polygons[i].Count >= 3 &&
                PointInPolygon(uv, polygons[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsCoveredByCoplanar(
        VoxelAwareMeshPrecut.FaceId face,
        Vector2 uv,
        TerrainChunk chunk)
    {
        if (chunk == null || chunk.Geometry == null) return false;

        VoxelAwareMeshPrecut.ChunkGeometry geometry = chunk.Geometry;
        for (int i = 0; i < geometry.FaceFragments.Count; i++)
        {
            VoxelAwareMeshPrecut.FaceFragment fragment =
                geometry.FaceFragments[i];
            if (VoxelAwareMeshPrecut.UseLegacyFaceKeyComparison
                    ? fragment.Face.Key != face.Key
                    : !fragment.Face.SameAs(face))
            {
                continue;
            }

            List<Vector2> polygon = new List<Vector2>();
            for (int p = 0; p < fragment.Polygon.Count; p++)
            {
                polygon.Add(VoxelAwareMeshSpatialFace.ToFaceLocal(
                    face,
                    fragment.Polygon[p],
                    geometry.ChunkOrigin,
                    geometry.VoxelSize
                ));
            }

            if (PointInPolygon(uv, polygon)) return true;
        }

        return false;
    }

    /// <summary>
    /// CoplanarBaseCoverage(Region)：Region 是否被 Coplanar Base Geometry 覆盖。
    /// Natural Coverage 与 FaceFragment Visibility 必须使用同一个判定。
    /// </summary>
    private static bool HasCoplanarBaseCoverage(
        VoxelAwareMeshPrecut.FaceId face,
        Vector2 uv,
        TerrainChunk negativeChunk,
        TerrainChunk positiveChunk,
        TerrainChunk evaluationOwner)
    {
        return IsCoveredByCoplanar(face, uv, negativeChunk) ||
               IsCoveredByCoplanar(face, uv, positiveChunk) ||
               IsCoveredByCoplanar(face, uv, evaluationOwner);
    }

    /// <summary>
    /// Spec 13.8.3 FaceFragment Visibility Rule。
    /// 该规则只派生 Final Assembly 输出，不修改 Base Geometry。
    /// </summary>
    /// <summary>
    /// B3-E2（Spec §13.8.3 修订）：FaceFragment（与 SpatialFace 共面的基础几何片）
    /// 的可见性以**基础密度材料边界**为准，而非编辑状态边界：
    ///   · 平面两侧基础密度恰有一侧 > 0（真实材料表面）且材料侧体素未被挖空 ⇒ 可见；
    ///   · 两侧都有材料（内部植入 / 内部裁剪面）或两侧都无材料（悬浮片）⇒ 不可见；
    ///   · 材料侧体素已被挖空（该处材料已移除）⇒ 不可见。
    /// 注意：FaceFragmentVisibility 是**覆写层**——无条目 = 默认可见（Assemble 只过滤
    /// 显式 false 的条目），因此本判定只用于写入 true/false 覆写值。
    /// </summary>
    public static bool FaceFragmentSurfaceVisible(
        bool negativeMaterial,
        bool positiveMaterial,
        bool negativeSolid,
        bool positiveSolid)
    {
        if (negativeMaterial == positiveMaterial) return false;

        bool materialSideSolid = negativeMaterial ? negativeSolid : positiveSolid;
        return materialSideSolid;
    }

    /// <summary>
    /// 旧的“编辑状态边界 + 共面覆盖”可见性判据。B3-E2 起不再用于
    /// BuildVisibilityTargets（该判据在“基岩表面落在 Voxel 平面 + 两侧 Solid”
    /// 时会把真实地表判为不可见），保留仅供历史调用方与回归对照。
    /// </summary>
    public static bool FaceFragmentFinalVisible(
        bool negativeSolid,
        bool positiveSolid,
        bool coplanarBaseCoverage)
    {
        bool currentBoundary =
            (negativeSolid && !positiveSolid) ||
            (!negativeSolid && positiveSolid);

        return currentBoundary && coplanarBaseCoverage;
    }

    private static List<VoxelAwareMeshSpatialFace.FaceSegment> BuildFaceSegments(
        VoxelAwareMeshPrecut.FaceId face,
        TerrainChunk evaluationOwner,
        TerrainChunk negativeChunk,
        TerrainChunk positiveChunk)
    {
        Dictionary<string, VoxelAwareMeshSpatialFace.FaceSegment> dedup =
            new Dictionary<string, VoxelAwareMeshSpatialFace.FaceSegment>();

        AddSegmentsFromChunk(face, evaluationOwner, dedup, true);
        AddSegmentsFromChunk(face, negativeChunk, dedup, false);
        AddSegmentsFromChunk(face, positiveChunk, dedup, false);

        return new List<VoxelAwareMeshSpatialFace.FaceSegment>(dedup.Values);
    }

    private static void AddSegmentsFromChunk(
        VoxelAwareMeshPrecut.FaceId face,
        TerrainChunk chunk,
        Dictionary<string, VoxelAwareMeshSpatialFace.FaceSegment> dedup,
        bool ownerSource)
    {
        if (chunk == null || chunk.Geometry == null) return;

        VoxelAwareMeshPrecut.ChunkGeometry geometry = chunk.Geometry;

        for (int i = 0; i < geometry.Bridges.Count; i++)
        {
            VoxelAwareMeshPrecut.BridgeRecord bridge = geometry.Bridges[i];
            if (VoxelAwareMeshPrecut.UseLegacyFaceKeyComparison
                    ? bridge.Face.Key != face.Key
                    : !bridge.Face.SameAs(face))
            {
                continue;
            }

            Vector2 a = VoxelAwareMeshSpatialFace.ToFaceLocal(
                face,
                geometry.Pool.Positions[bridge.VertexA],
                geometry.ChunkOrigin,
                geometry.VoxelSize
            );
            Vector2 b = VoxelAwareMeshSpatialFace.ToFaceLocal(
                face,
                geometry.Pool.Positions[bridge.VertexB],
                geometry.ChunkOrigin,
                geometry.VoxelSize
            );

            AddSegment(
                face,
                a,
                b,
                bridge.HasCutVertex,
                !bridge.HasCutVertex,
                false,
                dedup,
                ownerSource
            );
        }

        for (int i = 0; i < geometry.FaceFragments.Count; i++)
        {
            VoxelAwareMeshPrecut.FaceFragment fragment =
                geometry.FaceFragments[i];
            if (VoxelAwareMeshPrecut.UseLegacyFaceKeyComparison
                    ? fragment.Face.Key != face.Key
                    : !fragment.Face.SameAs(face))
            {
                continue;
            }

            for (int p = 0; p < fragment.Polygon.Count; p++)
            {
                Vector2 a = VoxelAwareMeshSpatialFace.ToFaceLocal(
                    face,
                    fragment.Polygon[p],
                    geometry.ChunkOrigin,
                    geometry.VoxelSize
                );
                Vector2 b = VoxelAwareMeshSpatialFace.ToFaceLocal(
                    face,
                    fragment.Polygon[(p + 1) % fragment.Polygon.Count],
                    geometry.ChunkOrigin,
                    geometry.VoxelSize
                );

                AddSegment(
                    face,
                    a,
                    b,
                    false,
                    false,
                    true,
                    dedup,
                    ownerSource
                );
            }
        }
    }

    private static void AddSegment(
        VoxelAwareMeshPrecut.FaceId face,
        Vector2 a,
        Vector2 b,
        bool isBridge,
        bool isNatural,
        bool isCoplanar,
        Dictionary<string, VoxelAwareMeshSpatialFace.FaceSegment> dedup,
        bool ownerSource)
    {
        string key = MakeSegmentKey(face, a, b);

        if (dedup.TryGetValue(key, out VoxelAwareMeshSpatialFace.FaceSegment existing))
        {
            existing.IsBridge |= isBridge;
            existing.IsNatural |= isNatural;
            existing.IsCoplanar |= isCoplanar;
            return;
        }

        dedup.Add(key, new VoxelAwareMeshSpatialFace.FaceSegment(a, b)
        {
            IsBridge = isBridge,
            IsNatural = isNatural,
            IsCoplanar = isCoplanar
        });
    }

    /// <summary>
    /// Spec 13.9.6：独立校验 Cross-Chunk Boundary Graph。
    /// 同一 GeometricDedupKey（同一端点对）允许合并为一条几何边；
    /// 不同 SegmentIdentity 之间出现部分 Collinear Overlap 时必须报告
    /// Validation Failure，不允许被 Planar Graph 静默吞掉。
    /// </summary>
    public static bool ValidateBoundaryGraphSegments(
        VoxelAwareMeshPrecut.FaceId face,
        IReadOnlyList<VoxelAwareMeshSpatialFace.FaceSegment> segments,
        out string error)
    {
        error = string.Empty;
        if (segments == null) return true;

        for (int i = 0; i < segments.Count; i++)
        {
            for (int j = i + 1; j < segments.Count; j++)
            {
                VoxelAwareMeshSpatialFace.FaceSegment first = segments[i];
                VoxelAwareMeshSpatialFace.FaceSegment second = segments[j];

                if (!TryGetCollinearOverlap(first, second, out float overlap))
                {
                    continue;
                }

                // 同一 GeometricDedupKey：允许合并。
                if (IsSameEndpointPair(first, second)) continue;

                // Point Touch（只在端点相接）不是 Overlap。
                if (overlap <= Tolerance) continue;

                error =
                    "COLLINEAR OVERLAP: Face=" + face.Key +
                    " SegmentA=(" + first.A + ")-(" + first.B + ")" +
                    " SegmentB=(" + second.A + ")-(" + second.B + ")" +
                    " OverlapLength=" + overlap;
                return false;
            }
        }

        return true;
    }

    private static bool IsSameEndpointPair(
        VoxelAwareMeshSpatialFace.FaceSegment first,
        VoxelAwareMeshSpatialFace.FaceSegment second)
    {
        bool sameWay =
            (first.A - second.A).magnitude <= Tolerance &&
            (first.B - second.B).magnitude <= Tolerance;
        bool flipped =
            (first.A - second.B).magnitude <= Tolerance &&
            (first.B - second.A).magnitude <= Tolerance;

        return sameWay || flipped;
    }

    private static bool TryGetCollinearOverlap(
        VoxelAwareMeshSpatialFace.FaceSegment first,
        VoxelAwareMeshSpatialFace.FaceSegment second,
        out float overlapLength)
    {
        overlapLength = 0f;

        Vector2 direction = first.B - first.A;
        float length = direction.magnitude;
        if (length <= Tolerance) return false;

        Vector2 unit = direction / length;
        if (Mathf.Abs(Cross2D(unit, second.A - first.A)) > Tolerance) return false;
        if (Mathf.Abs(Cross2D(unit, second.B - first.A)) > Tolerance) return false;

        float tA = 0f;
        float tB = length;
        float tC = Vector2.Dot(second.A - first.A, unit);
        float tD = Vector2.Dot(second.B - first.A, unit);

        float overlapMin = Mathf.Max(Mathf.Min(tA, tB), Mathf.Min(tC, tD));
        float overlapMax = Mathf.Min(Mathf.Max(tA, tB), Mathf.Max(tC, tD));
        if (overlapMax <= overlapMin) return false;

        overlapLength = overlapMax - overlapMin;
        return true;
    }

    private static float Cross2D(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }

    private static string MakeSegmentKey(
        VoxelAwareMeshPrecut.FaceId face,
        Vector2 a,
        Vector2 b)
    {
        int ax = Mathf.RoundToInt(a.x / Tolerance);
        int ay = Mathf.RoundToInt(a.y / Tolerance);
        int bx = Mathf.RoundToInt(b.x / Tolerance);
        int by = Mathf.RoundToInt(b.y / Tolerance);

        bool aFirst = ax < bx || (ax == bx && ay <= by);
        string p0 = aFirst
            ? ax + ":" + ay
            : bx + ":" + by;
        string p1 = aFirst
            ? bx + ":" + by
            : ax + ":" + ay;

        return face.Key + "|" + p0 + "|" + p1;
    }

    private static bool TryGetFaceForDirection(
        Vector3Int voxel,
        Vector3Int direction,
        out VoxelAwareMeshPrecut.FaceId face,
        out Vector3Int negativeVoxel,
        out Vector3Int positiveVoxel,
        out bool hasPositive)
    {
        face = default(VoxelAwareMeshPrecut.FaceId);
        negativeVoxel = voxel;
        positiveVoxel = voxel + direction;
        hasPositive = true;

        int axis = -1;
        if (direction.x != 0) axis = 0;
        else if (direction.y != 0) axis = 1;
        else if (direction.z != 0) axis = 2;

        if (axis < 0) return false;

        int sign = Component(direction, axis);
        if (sign > 0)
        {
            negativeVoxel = voxel;
            positiveVoxel = voxel + direction;
        }
        else
        {
            negativeVoxel = voxel + direction;
            positiveVoxel = voxel;
        }

        int plane = Component(positiveVoxel, axis);
        int u = Component(negativeVoxel, (axis + 1) % 3);
        int v = Component(negativeVoxel, (axis + 2) % 3);
        face = new VoxelAwareMeshPrecut.FaceId(axis, plane, u, v);
        hasPositive = true;
        return true;
    }

    private static TerrainChunk ResolveEvaluationOwner(
        VoxelAwareMeshPrecut.FaceId face,
        Vector3Int negativeVoxel,
        Vector3Int positiveVoxel,
        Vector3Int chunkVoxelSize,
        Dictionary<Vector3Int, TerrainChunk> lookup,
        out Vector3Int ownerCoordinate)
    {
        ownerCoordinate = ChunkSpace.VoxelToChunk(
            negativeVoxel,
            chunkVoxelSize
        );

        if (lookup.TryGetValue(ownerCoordinate, out TerrainChunk owner))
        {
            return owner;
        }

        ownerCoordinate = ChunkSpace.VoxelToChunk(
            positiveVoxel,
            chunkVoxelSize
        );
        lookup.TryGetValue(ownerCoordinate, out owner);
        return owner;
    }

    public static List<VoxelAwareMeshSpatialFace.FaceSegment>
        BuildFaceSegmentsForDiagnostics(
            VoxelAwareMeshPrecut.FaceId face,
            TerrainChunk evaluationOwner,
            TerrainChunk negativeChunk,
            TerrainChunk positiveChunk)
    {
        return BuildFaceSegments(
            face,
            evaluationOwner,
            negativeChunk,
            positiveChunk
        );
    }

    private static void AddUnique(List<Vector3Int> list, Vector3Int value)
    {
        if (!list.Contains(value)) list.Add(value);
    }

    /// <summary>
    /// Spec 13.8.3：为当前 Face 的每个 FaceFragment 派生 Final Visibility。
    /// FaceFragment 本身（Polygon / SourceTriangle / Submesh）保持不可变；
    /// 这里只生成 Commit 阶段的 Visibility 计划，不直接写入。
    /// </summary>
    private static void BuildVisibilityTargets(
        FaceEvaluationPlan plan,
        VoxelAwareMeshSpatialFace.SpatialFaceRecord record,
        IReadOnlyList<TerrainChunk> chunks,
        TerrainChunk negativeChunk,
        TerrainChunk positiveChunk,
        TerrainChunk evaluationOwner)
    {
        // B3-E1 修复：FaceId.Key 是 **Chunk Local** 标识（U/V 为 Chunk 内体素坐标），
        // 因此不能遍历“全部 Chunk”按 Key 匹配——不同 Chunk 上 Key 相同的面是
        // 彼此无关的面。旧实现会把本次 Mutation 的 Visible 值写入其它 Chunk 的
        // 同 Key 面，并用错误的坐标框架计算 CoplanarBaseCoverage，导致
        // · 其它 Chunk 的 Base Geometry 被错误隐藏（表面缺失）；
        // · 恢复（Air → Solid）后本 Chunk 的覆盖区域也无法恢复为 Visible。
        // 与本 Face 相关的 Chunk 集合与 BuildFaceSegments / Coplanar 收集一致：
        // EvaluationOwner / Negative / Positive（Boundary 情况下含 MeshOwner）。
        TerrainChunk meshOwnerChunk = plan.NegativeSolid
            ? negativeChunk
            : positiveChunk;

        TerrainChunk[] faceChunks =
        {
            evaluationOwner,
            negativeChunk,
            positiveChunk,
            meshOwnerChunk
        };

        for (int c = 0; c < faceChunks.Length; c++)
        {
            TerrainChunk chunk = faceChunks[c];
            if (chunk == null || chunk.Geometry == null) continue;

            bool duplicated = false;
            for (int p = 0; p < c; p++)
            {
                if (faceChunks[p] == chunk)
                {
                    duplicated = true;
                    break;
                }
            }

            if (duplicated) continue;

            VoxelAwareMeshPrecut.ChunkGeometry geometry = chunk.Geometry;
            for (int i = 0; i < geometry.FaceFragments.Count; i++)
            {
                VoxelAwareMeshPrecut.FaceFragment fragment =
                    geometry.FaceFragments[i];
                if (VoxelAwareMeshPrecut.UseLegacyFaceKeyComparison
                        ? fragment.Face.Key != plan.Face.Key
                        : !fragment.Face.SameAs(plan.Face))
                {
                    continue;
                }

                // B3-E2：材料边界可见性（替代旧的 currentBoundary + coverage 判据）：
                // 只有“基础密度恰好一侧为材料、且该材料侧体素未被挖空”的共面基础几何
                // 才是当前可见地表；内部植入/内部裁剪面（两侧材料）保持隐藏。
                Vector2 fragmentSample = FaceFragmentInteriorSample(
                    plan.Face,
                    fragment,
                    geometry
                );

                bool negativeMaterial = SampleFaceSideMaterial(
                    plan.Face,
                    fragmentSample,
                    true,
                    negativeChunk,
                    geometry.ChunkOrigin,
                    geometry.VoxelSize
                );
                bool positiveMaterial = SampleFaceSideMaterial(
                    plan.Face,
                    fragmentSample,
                    false,
                    positiveChunk,
                    geometry.ChunkOrigin,
                    geometry.VoxelSize
                );

                plan.Visibility.Add(new VisibilityTarget
                {
                    Geometry = geometry,
                    FragmentIndex = i,
                    Visible = FaceFragmentSurfaceVisible(
                        negativeMaterial,
                        positiveMaterial,
                        plan.NegativeSolid,
                        plan.PositiveSolid
                    )
                });
            }
        }
    }

    /// <summary>
    /// FaceFragment 所属 Region 是否被 Coplanar Base Geometry 覆盖。
    /// Region 用 FaceFragment 的确定性 interior sample 解析，
    /// 判定采样点与 Hidden Face 的 Region Classification 保持一致
    /// （Spec 13.7.2 / 13.7.4 / 13.10）。
    /// </summary>
    private static bool IsFaceFragmentRegionCovered(
        VoxelAwareMeshPrecut.FaceId face,
        VoxelAwareMeshPrecut.FaceFragment fragment,
        VoxelAwareMeshPrecut.ChunkGeometry ownerGeometry,
        VoxelAwareMeshSpatialFace.SpatialFaceRecord record,
        TerrainChunk negativeChunk,
        TerrainChunk positiveChunk,
        TerrainChunk evaluationOwner)
    {
        if (fragment == null || fragment.Polygon.Count < 3) return false;

        Vector2 sample = FaceFragmentInteriorSample(
            face,
            fragment,
            ownerGeometry
        );

        if (record != null)
        {
            for (int i = 0; i < record.Regions.Count; i++)
            {
                VoxelAwareMeshSpatialFace.FaceRegion region = record.Regions[i];
                if (region == null) continue;
                if (!PointInPolygon(sample, region.Polygon)) continue;

                sample = region.Midpoint;
                break;
            }
        }

        return HasCoplanarBaseCoverage(
            face,
            sample,
            negativeChunk,
            positiveChunk,
            evaluationOwner
        );
    }

    /// <summary>
    /// B3-E2：在 Face Local `uv` 处向指定一侧偏移 `Tolerance` 采样基础密度，
    /// 判断该侧是否为材料（density > 0）。Chunk 缺失 / 采样出域按 Air 处理
    /// （世界外侧 = Air，与 Spec §13.2 一致）。
    /// </summary>
    private static bool SampleFaceSideMaterial(
        VoxelAwareMeshPrecut.FaceId face,
        Vector2 uv,
        bool negativeSide,
        TerrainChunk sideChunk,
        Vector3 evaluationOrigin,
        float voxelSize)
    {
        if (sideChunk == null || sideChunk.Density == null) return false;

        Vector3 terrainLocal = VoxelAwareMeshSpatialFace.ToChunkLocal(
            face,
            uv,
            evaluationOrigin,
            voxelSize
        ) + evaluationOrigin;

        Vector3 normal = FaceNormal(face);
        Vector3 samplePoint = negativeSide
            ? terrainLocal - normal * Tolerance
            : terrainLocal + normal * Tolerance;

        float density;
        string error;
        if (!TrySampleBaseDensity(sideChunk, samplePoint, out density, out error))
        {
            return false;
        }

        return density > 0f;
    }

    /// <summary>FaceFragment 的确定性 interior sample（Face Local）。</summary>
    private static Vector2 FaceFragmentInteriorSample(
        VoxelAwareMeshPrecut.FaceId face,
        VoxelAwareMeshPrecut.FaceFragment fragment,
        VoxelAwareMeshPrecut.ChunkGeometry geometry)
    {
        List<Vector2> polygon = new List<Vector2>();
        for (int i = 0; i < fragment.Polygon.Count; i++)
        {
            polygon.Add(VoxelAwareMeshSpatialFace.ToFaceLocal(
                face,
                fragment.Polygon[i],
                geometry.ChunkOrigin,
                geometry.VoxelSize
            ));
        }

        Vector2 sum = Vector2.zero;
        for (int i = 0; i < polygon.Count; i++) sum += polygon[i];
        Vector2 centroid = sum / polygon.Count;

        if (polygon.Count >= 3 && !PointInPolygon(centroid, polygon))
        {
            // 非凸 / 退化多边形：回退到第一个三角形 centroid。
            centroid = (polygon[0] + polygon[1] + polygon[2]) / 3f;
        }

        return centroid;
    }

    public static bool TrySampleBaseDensity(
        TerrainChunk chunk,
        Vector3 terrainLocal,
        out float density,
        out string error)
    {
        density = 0f;
        error = string.Empty;

        if (chunk == null || chunk.Density == null)
        {
            error = "Chunk or Base DensityField is null.";
            return false;
        }

        DensityField field = chunk.Density;
        float fx = (terrainLocal.x - field.Origin.x) / field.CellSizeX;
        float fy = (terrainLocal.y - field.Origin.y) / field.CellSizeY;
        float fz = (terrainLocal.z - field.Origin.z) / field.CellSizeZ;

        float maxX = field.SampleCountX - 1;
        float maxY = field.SampleCountY - 1;
        float maxZ = field.SampleCountZ - 1;

        if (fx < -Tolerance || fx > maxX + Tolerance ||
            fy < -Tolerance || fy > maxY + Tolerance ||
            fz < -Tolerance || fz > maxZ + Tolerance)
        {
            string outOfRangeAxis = string.Empty;
            if (fx < -Tolerance || fx > maxX + Tolerance) outOfRangeAxis += "X";
            if (fy < -Tolerance || fy > maxY + Tolerance)
            {
                outOfRangeAxis += outOfRangeAxis.Length > 0 ? ",Y" : "Y";
            }
            if (fz < -Tolerance || fz > maxZ + Tolerance)
            {
                outOfRangeAxis += outOfRangeAxis.Length > 0 ? ",Z" : "Z";
            }

            error =
                "BASE DENSITY SAMPLE DOMAIN GAP: " +
                "P=" + terrainLocal +
                " continuous=(" + fx + "," + fy + "," + fz + ")" +
                " OutOfRangeAxis=" + outOfRangeAxis +
                " valid=[0.." + maxX + "],[0.." + maxY + "],[0.." + maxZ + "]" +
                " Origin=" + field.Origin +
                " Size=" + field.Size +
                " SampleCount=(" + field.SampleCountX + "," +
                field.SampleCountY + "," + field.SampleCountZ + ")" +
                " CellSize=(" + field.CellSizeX + "," +
                field.CellSizeY + "," + field.CellSizeZ + ")";
            return false;
        }

        int x0 = Mathf.Clamp(Mathf.FloorToInt(fx), 0, field.SampleCountX - 1);
        int y0 = Mathf.Clamp(Mathf.FloorToInt(fy), 0, field.SampleCountY - 1);
        int z0 = Mathf.Clamp(Mathf.FloorToInt(fz), 0, field.SampleCountZ - 1);

        int x1 = Mathf.Min(x0 + 1, field.SampleCountX - 1);
        int y1 = Mathf.Min(y0 + 1, field.SampleCountY - 1);
        int z1 = Mathf.Min(z0 + 1, field.SampleCountZ - 1);

        float tx = Mathf.Clamp01(fx - x0);
        float ty = Mathf.Clamp01(fy - y0);
        float tz = Mathf.Clamp01(fz - z0);

        float c000 = field[x0, y0, z0];
        float c100 = field[x1, y0, z0];
        float c010 = field[x0, y1, z0];
        float c110 = field[x1, y1, z0];
        float c001 = field[x0, y0, z1];
        float c101 = field[x1, y0, z1];
        float c011 = field[x0, y1, z1];
        float c111 = field[x1, y1, z1];

        float c00 = Mathf.Lerp(c000, c100, tx);
        float c10 = Mathf.Lerp(c010, c110, tx);
        float c01 = Mathf.Lerp(c001, c101, tx);
        float c11 = Mathf.Lerp(c011, c111, tx);

        float c0 = Mathf.Lerp(c00, c10, ty);
        float c1 = Mathf.Lerp(c01, c11, ty);
        density = Mathf.Lerp(c0, c1, tz);
        return true;
    }

    private static void RemoveContribution(
        TerrainChunk chunk,
        VoxelAwareMeshPrecut.FaceId face)
    {
        if (chunk == null || chunk.Geometry == null) return;
        chunk.Geometry.HiddenFaceContributions.Remove(face.Key);
    }

    private static Vector3 FaceNormal(VoxelAwareMeshPrecut.FaceId face)
    {
        if (face.Axis == 0) return Vector3.right;
        if (face.Axis == 1) return Vector3.up;
        return Vector3.forward;
    }

    private static bool PointInPolygon(Vector2 point, List<Vector2> polygon)
    {
        if (polygon == null || polygon.Count < 3) return false;

        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            Vector2 a = polygon[i];
            Vector2 b = polygon[j];

            bool intersects = (a.y > point.y) != (b.y > point.y);
            if (intersects)
            {
                float x = (b.x - a.x) * (point.y - a.y) /
                          (b.y - a.y) + a.x;
                if (point.x < x) inside = !inside;
            }
        }

        return inside;
    }

    private static int Component(Vector3Int value, int axis)
    {
        return axis == 0 ? value.x : axis == 1 ? value.y : value.z;
    }
}
