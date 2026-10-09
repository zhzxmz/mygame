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

    private static readonly Vector3Int[] Directions =
    {
        new Vector3Int(1, 0, 0),
        new Vector3Int(-1, 0, 0),
        new Vector3Int(0, 1, 0),
        new Vector3Int(0, -1, 0),
        new Vector3Int(0, 0, 1),
        new Vector3Int(0, 0, -1)
    };

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

        LastMutationCommitted = true;

        // -----------------------------------------------------------------
        // Phase 3 — Assemble（只在 Commit 成功后执行）
        // -----------------------------------------------------------------
        ReassembleDirtyChunks(chunks, lookup, dirty);
        LastAssemblyExecuted = LastReassembledChunkCount > 0;

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

        List<VoxelAwareMeshSpatialFace.FaceSegment> segments =
            BuildFaceSegments(
                face,
                evaluationOwner,
                negativeChunk,
                positiveChunk
            );

        // Spec 13.9.6：部分 Collinear Overlap 是 Validation Failure，
        // 不能被 Planar Graph 静默吞掉，也不能继续 Assemble。
        if (!ValidateBoundaryGraphSegments(face, segments, out string overlapError))
        {
            LastBoundaryGraphValidationFailure = true;
            LastBoundaryGraphValidationMessage = overlapError;

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
            BuildVisibilityTargets(
                plan,
                null,
                chunks,
                negativeChunk,
                positiveChunk,
                evaluationOwner
            );

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
                    VoxelAwareMeshSpatialFace.RegionDecision decision =
                        DecideRegion(
                            face,
                            uv,
                            solidIsNegative,
                            solidVoxel,
                            evaluationOwner,
                            negativeChunk,
                            positiveChunk,
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

                    return decision;
                }
            );

        if (record.EvaluationFailed)
        {
            // Domain Gap 等 Evaluation Failure：该 Face 不产生任何
            // HiddenFaceContribution / Visibility 提交，Mutation 将 Abort。
            plan.Failed = true;
            plan.FailureMessage = string.IsNullOrEmpty(regionFailureMessage)
                ? record.EvaluationFailureMessage
                : regionFailureMessage;
            return plan;
        }

        plan.Record = record;
        plan.HiddenFaces = record.HiddenFaces;

        // Spec 13.8.2 / 13.8.3：Final Visibility 是派生结果，
        // 用 Region Classification 的同一个 CoplanarBaseCoverage 判定。
        BuildVisibilityTargets(
            plan,
            record,
            chunks,
            negativeChunk,
            positiveChunk,
            evaluationOwner
        );

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
    /// </summary>
    private static VoxelAwareMeshSpatialFace.RegionDecision DecideRegion(
        VoxelAwareMeshPrecut.FaceId face,
        Vector2 uv,
        bool solidIsNegative,
        Vector3Int solidVoxel,
        TerrainChunk evaluationOwner,
        TerrainChunk negativeChunk,
        TerrainChunk positiveChunk,
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

        // Coplanar Base Geometry 覆盖：由实际 Face Fragment 决定。
        if (HasCoplanarBaseCoverage(
            face,
            uv,
            negativeChunk,
            positiveChunk,
            evaluationOwner))
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

        return baseDensity <= 0f
            ? VoxelAwareMeshSpatialFace.RegionDecision.HiddenFace
            : VoxelAwareMeshSpatialFace.RegionDecision.NoHiddenFace;
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
            if (fragment.Face.Key != face.Key) continue;

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
            if (bridge.Face.Key != face.Key) continue;

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
            if (fragment.Face.Key != face.Key) continue;

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
        for (int c = 0; c < chunks.Count; c++)
        {
            TerrainChunk chunk = chunks[c];
            if (chunk == null || chunk.Geometry == null) continue;

            VoxelAwareMeshPrecut.ChunkGeometry geometry = chunk.Geometry;
            for (int i = 0; i < geometry.FaceFragments.Count; i++)
            {
                VoxelAwareMeshPrecut.FaceFragment fragment =
                    geometry.FaceFragments[i];
                if (fragment.Face.Key != plan.Face.Key) continue;

                bool coplanarCoverage = plan.CurrentBoundary &&
                    IsFaceFragmentRegionCovered(
                        plan.Face,
                        fragment,
                        geometry,
                        record,
                        negativeChunk,
                        positiveChunk,
                        evaluationOwner
                    );

                plan.Visibility.Add(new VisibilityTarget
                {
                    Geometry = geometry,
                    FragmentIndex = i,
                    Visible = FaceFragmentFinalVisible(
                        plan.NegativeSolid,
                        plan.PositiveSolid,
                        coplanarCoverage
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
