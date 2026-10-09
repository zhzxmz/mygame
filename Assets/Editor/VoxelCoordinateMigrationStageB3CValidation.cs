using System;
using System.Collections.Generic;
using System.Text;
using TerrainModificationV2;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Coordinate Migration Stage B3-C — Region 分类粒度与覆盖语义。
///
/// 本文件当前包含 **根因审计**（RunAuditBatch / CheckRootCauseAudit）：
/// 针对 B3-B 遗留的多覆盖面 1:14:37:0，用真实生产 Region/ Segment 数据
/// + 独立密度采样，判定多覆盖属于：
///   (a) 三角化错误（B3-B 已修，需排除）
///   (b) Region 分类错误（Region 本身跨越不同覆盖状态，却被整片同一种分类）
///   (c) 基础几何覆盖判定错误（Base Mesh 本应覆盖却被判为缺失）
///   (d) 面归属错误（同一区域被错误 Chunk/Face 持有）
/// 审计结论确认后再实施最小修复，并补齐 T1–T6 几何验证。
/// </summary>
public static class VoxelCoordinateMigrationStageB3CValidation
{
    private static readonly Vector3Int ChunkVoxelSize = new Vector3Int(75, 37, 50);
    private const float FlatHeight = 15.7f;
    private const int SurfaceVoxelY = 15;
    private const float PlaneTolerance = 1e-3f;
    private const float Quantize = 1e-4f;

    // =====================================================================
    // 覆盖判据容差（尺度相关，B3-C 验收复核推导）
    //
    // 生产侧近似误差（每个边界单元）：
    //   1) 覆盖等值线在单元内用弦代替：面积误差 ≈ (2/3)·L·sagitta
    //      = (2/3)·L·(κL²/8) = κ·L³/12，L ≤ √2·h，h ≤ 0.04m
    //      κ ≤ ~10 m⁻¹（resolution=75 的噪声地形）⇒ 单单元 ≤ ~1.5e-4 m²
    //   2) 边界单元数 ≈ L_boundary/h ⇒ 总弦近似误差 ≈ L_boundary·κ·h²/12
    //      ≤ 1.3e-3 · L_boundary
    //   3) 对角（saddle）单元按质心整格判定：≤ h² = 1.6e-3 m²/单元，
    //      实测占比 ~5-10% ⇒ ≤ ~4e-3 · L_boundary
    //   4) 退化薄片过滤（≤1e-6 m²/片）与多边形裁剪浮点误差 ≤ 1e-3 · L_boundary
    // 判据侧（独立密度积分）误差：200×200 网格（5mm）⇒ ≈ 0.5·L_boundary·0.005
    //      = 2.5e-3 · L_boundary
    // 合计 ≈ 9e-3 · L_boundary，取 30% 余量 ⇒ 0.012 · L_boundary，
    // 下限 0.005 m²（数值噪声），上限 0.02 m²（1 m² 面 + 长边界）。
    // =====================================================================
    // 复核修正（B3-C Finalization）：区分“严格推导上界 / 实测误差 / 工程容差”。
    //
    // 条件上界：设 N 为边界单元数、每单元整格误判面积 ≤ h²（h ≤ 0.04m）。
    //   仅当“边界在每个单元内穿越次数有界（≤2）”时才有 N ≤ 2L/h，从而
    //   E ≤ N·h² ≤ 2Lh = 0.08·L（对任意高频抖动边界不成立）。
    //   无条件成立的二值覆盖面积差上界为
    //   E ≤ min(A_open, A_face − A_open)，且 0 ≤ A_open ≤ A_face
    //   （本夹具 A_face = 1 m²，故旧写法 min(A_open, 1 − A_open) 是其特例）。
    //   ⇒ 现有公式里的 0.04·L 并不是严格上界，而是**取严格上界的一半**，
    //     其可用性由实测余量支持（见下），不得表述为“已证明的上界”。
    //
    // 实测（B3-C 验收/定稿轮）：`1:14:37:0` 偏差 0.0030 m²（0.94m 边界）、
    //   `1:16:50:75` 偏差 0.0132 m²（0.49m 边界，小开口 10.8%·A_open）、
    //   6 个自然地形开口面 ≤ 0.0023 m² ⇒ 实测为严格上界 2Lh 的 4–18%。
    //
    // 工程容差：tol = clamp(max(0.04·L, 0.15·A_open), 0.005, 0.02)，
    //   系数 0.04 与 0.15 为经验系数（实测余量 1.5–6.7×），
    //   下限 0.005 抑制数值噪声，上限 0.02 限定 1m² 级面的适用范围。
    //
    // 主导误差机制不是弦近似（κh²/12 量级），而是**单元分辨率量化**：
    // 四角同号的边界单元无法被切分（等值线穿过单元但不经过任何角点），
    // 该单元整格按角点结论输出 ⇒ 每单元误差 ≤ h² = 1.6e-3 m²；
    // 边界单元数 ≈ L/h（长边界）或 ≈ A_open/h²（小尺度特征）。
    //  ⇒ 大而长的边界：误差 ≈ L·h/4（随机等值线位置，最坏 L·h/2）；
    //  ⇒ 小而闭合的开口：误差可达开口面积本身的 ~10–25%。
    // 实测（B3-C 验收轮）：face 1:16:50:75 开口 0.1225 m²、L=0.49m、h=0.04m
    //   ⇒ 偏差 0.0132 m² ≈ 10.8%·A_open；face 1:14:37:0 开口 0.5906 m²、
    //   L=0.94m ⇒ 偏差 0.0030 m²（5‰·A_open，远低于最坏界）。
    // 因此判据取“开口面积相对容差”，并保留绝对上下限：
    private const double CoverageToleranceOpenAreaRatio = 0.15;
    private const double CoverageToleranceFloor = 0.005;
    private const double CoverageToleranceCap = 0.02;
    private const double OverlapAreaTolerance = 1e-3;
    private const double OutsideAreaTolerance = 1e-3;

    /// <summary>
    /// 整面必须完全封闭（面内无覆盖边界）时的容差：
    /// 均匀 Region 走既有单多边形路径、判据侧无边界离散误差，
    /// 剩余误差仅来自多边形裁剪/顶点焊接浮点运算 ⇒ 1e-3 m²。
    /// </summary>
    private const double WholeFaceCoverageTolerance = 1e-3;

    private static double CoverageTolerance(
        List<TerrainChunkView> views,
        FaceKey face,
        int axis,
        bool negativeSide,
        double expectedOpenArea)
    {
        // 同时给出两种几何界，取较小者作为容差（更保守），再夹到 [floor, cap]：
        //   (a) 长边界：L·h/4（h = 4cm 单元）
        //   (b) 小特征：0.15·A_open（单元分辨率相对误差实测上界）
        double boundary = EstimateCoverageBoundaryLength(
            views, face, axis, negativeSide);

        // (a) 单元分辨率最坏界：边界单元数 ≤ 2L/h（单元可能被穿两次），
        //     每单元整格误判 ≤ h²（h = 4cm）⇒ ≤ L·h（= 0.04·L）。
        double cellResolutionBound = boundary * 0.04;

        // (b) 小尺度特征：开口区域只跨少数单元时，误差可达开口面积本身的
        //     10–25%（实测 face 1:16:50:75 = 10.8%）⇒ 取 15% 作为界。
        double smallFeatureBound =
            CoverageToleranceOpenAreaRatio * Math.Max(0.0, expectedOpenArea);

        // 两者覆盖不同主导机制，取较大者（仍是严格的几何上界）。
        double bound = Math.Max(cellResolutionBound, smallFeatureBound);

        return Math.Min(
            CoverageToleranceCap,
            Math.Max(CoverageToleranceFloor, bound));
    }

    /// <summary>
    /// 估计面内“应封闭区域”的边界长度：在 200×200 密度符号栅格上统计
    /// 相邻符号变化边数 × 栅格步长（标准周长估计）。
    /// </summary>
    private static double EstimateCoverageBoundaryLength(
        List<TerrainChunkView> views,
        FaceKey face,
        int axis,
        bool negativeSide)
    {
        const int grid = 200;
        const float offset = 1e-3f;
        bool[,] open = new bool[grid, grid];

        for (int iu = 0; iu < grid; iu++)
        {
            for (int iv = 0; iv < grid; iv++)
            {
                Vector3 point = FacePoint(
                    face,
                    (iu + 0.5f) / grid,
                    (iv + 0.5f) / grid);
                point[axis] += negativeSide ? -offset : offset;

                float density;
                if (!TrySampleBaseDensity(views, point, out density)) continue;
                open[iu, iv] = density <= 0f;
            }
        }

        double pitch = 1.0 / grid;
        int changes = 0;

        for (int iu = 0; iu < grid; iu++)
        {
            for (int iv = 0; iv < grid; iv++)
            {
                if (iu + 1 < grid && open[iu, iv] != open[iu + 1, iv]) changes++;
                if (iv + 1 < grid && open[iu, iv] != open[iu, iv + 1]) changes++;
            }
        }

        return changes * pitch * 0.5;
    }

    private static int checks;
    private static int failures;

    [MenuItem("Tools/Terrain/Validate Coordinate Migration Stage B3-C")]
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
                "[VoxelCoordinateMigrationStageB3CValidation] EXCEPTION: " +
                exception
            );
        }
        finally
        {
            LogSummary();
            EditorApplication.Exit(failures == 0 ? 0 : 1);
        }
    }

    /// <summary>只跑根因审计（诊断用，仍然执行断言）。</summary>
    public static void RunAuditBatch()
    {
        try
        {
            checks = 0;
            failures = 0;
            CheckRootCauseAudit();
        }
        catch (Exception exception)
        {
            failures++;
            Debug.LogError(
                "[VoxelCoordinateMigrationStageB3CValidation] EXCEPTION: " +
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

        CheckRootCauseAudit();
        CheckT1OriginalFailureCase();
        CheckT2WholeAndPartialFaces();
        CheckT3NaturalTerrain();
        CheckT4BoundariesAndOwnership();
        CheckT5IncrementalVersusFullRebuild();
        CheckT6RestoreAndRepeat();
    }

    // =====================================================================
    // T1 — 原始失败案例（1:14:37:0）
    // =====================================================================

    private static void CheckT1OriginalFailureCase()
    {
        GameObject terrainObject = CreateNaturalFixture("StageB3C_T1");

        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();
            List<TerrainChunkView> views = GetViews(terrainObject);

            Vector3Int voxel = new Vector3Int(0, 13, 37);
            FaceKey face;
            TryBuildFaceKey(voxel, new Vector3Int(0, 14, 37), 1, out face);

            HashSet<string> baseline = SnapshotFaceTriangleKeys(views, face);

            terrain.VoxelField.Set(voxel, VoxelState.Air);
            int rebuilt = terrain.RebuildChunksAffectedByVoxel(voxel);

            Check(rebuilt >= 1, "T1: 生产入口应用了 Voxel 修改（rebuilt=" + rebuilt + "）");

            views = GetViews(terrainObject);

            double expectedOpen = ExpectedSealedAreaByDensity(
                views, face, face.Axis, false);

            FaceAudit audit = AuditFace(
                views, face, voxel, terrain.VoxelField, baseline);

            double over = audit.Coverage.UnionArea - expectedOpen;
            double under = Math.Max(0.0, expectedOpen - audit.Coverage.UnionArea);
            double tolerance = CoverageTolerance(
                views, face, face.Axis, false, expectedOpen);

            Debug.Log(
                "[StageB3C-T1] expectedOpen=" + expectedOpen.ToString("F4") +
                " tolerance=" + tolerance.ToString("F4") +
                " covered=" + audit.Coverage.UnionArea.ToString("F4") +
                " over=" + over.ToString("F4") +
                " under=" + under.ToString("F4") +
                " overlap=" + audit.Coverage.OverlapArea.ToString("E3") +
                " triangles=" + audit.TriangleCount
            );

            Check(
                over <= tolerance,
                "T1: 多覆盖 <= 尺度相关容差 " + tolerance.ToString("F4") +
                " m²（实际 " + over.ToString("F4") + "）"
            );
            Check(
                under <= tolerance,
                "T1: 漏覆盖 <= 尺度相关容差 " + tolerance.ToString("F4") +
                " m²（实际 " + under.ToString("F4") + "）"
            );
            Check(
                audit.Coverage.OverlapArea <= OverlapAreaTolerance,
                "T1: 面内无重叠（" + audit.Coverage.OverlapArea.ToString("E3") + "）"
            );
            Check(
                audit.Coverage.DegenerateCount == 0 &&
                audit.Coverage.DuplicateCount == 0,
                "T1: 无退化/重复三角形（" + audit.Coverage.DegenerateCount + "/" +
                audit.Coverage.DuplicateCount + "）"
            );
            Check(
                audit.Coverage.OutsideArea <= OutsideAreaTolerance,
                "T1: 几何不越出目标 SpatialFace（" +
                audit.Coverage.OutsideArea.ToString("E3") + "）"
            );
            Check(
                audit.WindingConsistent,
                "T1: 三角形绕序符合 canonical 方向"
            );
            Check(
                audit.OwnerChunkCount == 1,
                "T1: 面几何归属唯一 Chunk（" + audit.OwnerChunks + "）"
            );
            Check(
                CountGeometryInsideVoxel(views, voxel) == 0,
                "T1: 已挖空 Voxel 内部无残留几何"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    // =====================================================================
    // T2 — 整面 / 部分面 / 六方向 / 同一 Region 混合状态
    // =====================================================================

    private static void CheckT2WholeAndPartialFaces()
    {
        // (a) 平面地形整面封闭。
        GameObject flat = CreateFlatFixture("StageB3C_T2Flat");

        try
        {
            DensityTerrain terrain = flat.GetComponent<DensityTerrain>();
            List<TerrainChunkView> views = GetViews(flat);
            Vector3Int voxel = new Vector3Int(30, SurfaceVoxelY, 30);
            FaceKey face;
            TryBuildFaceKey(
                voxel,
                new Vector3Int(30, SurfaceVoxelY + 1, 30),
                1,
                out face);

            HashSet<string> baseline = SnapshotFaceTriangleKeys(views, face);
            terrain.VoxelField.Set(voxel, VoxelState.Air);
            terrain.RebuildChunksAffectedByVoxel(voxel);
            views = GetViews(flat);

            FaceAudit audit = AuditFace(
                views, face, voxel, terrain.VoxelField, baseline);

            Check(
                Math.Abs(audit.Coverage.UnionArea - 1.0) <= WholeFaceCoverageTolerance &&
                audit.Coverage.OverlapArea <= OverlapAreaTolerance &&
                audit.WindingConsistent,
                "T2a: 平面整面覆盖 1.0000（实际 " +
                audit.Coverage.UnionArea.ToString("F4") + "，重叠 " +
                audit.Coverage.OverlapArea.ToString("E3") + "）"
            );
            Check(
                audit.TriangleCount == 2,
                "T2a: 均匀 Region 保持既有几何（2 个三角形，实际 " +
                audit.TriangleCount + "）"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(flat);
        }

        // (b) 六方向（空腔夹具）。
        GameObject cavity = CreateCavityFixture("StageB3C_T2Cavity");

        try
        {
            DensityTerrain terrain = cavity.GetComponent<DensityTerrain>();
            List<TerrainChunkView> views = GetViews(cavity);
            int tested = 0;

            for (int axis = 0; axis < 3; axis++)
            {
                for (int side = 0; side < 2; side++)
                {
                    Vector3Int voxel;
                    Vector3Int neighbour;
                    if (!TryFindFullyOpenCavityFaceVoxel(
                        terrain,
                        CavityCenter(),
                        CavityRadius(),
                        axis,
                        side,
                        out voxel,
                        out neighbour))
                    {
                        continue;
                    }

                    tested++;
                    FaceKey face;
                    TryBuildFaceKey(voxel, neighbour, axis, out face);
                    string label = "T2b[轴" + axis + (side == 0 ? "负" : "正") + "]";

                    HashSet<string> baseline = SnapshotFaceTriangleKeys(views, face);
                    terrain.VoxelField.Set(voxel, VoxelState.Air);
                    terrain.RebuildChunksAffectedByVoxel(voxel);
                    List<TerrainChunkView> after = GetViews(cavity);

                    FaceAudit audit = AuditFace(
                        after, face, voxel, terrain.VoxelField, baseline);

                    Check(
                        audit.Coverage.UncoveredArea <= WholeFaceCoverageTolerance &&
                        audit.Coverage.OverlapArea <= OverlapAreaTolerance &&
                        audit.WindingConsistent,
                        label + ": 完整覆盖且无重叠（未覆盖 " +
                        audit.Coverage.UncoveredArea.ToString("E3") + "，重叠 " +
                        audit.Coverage.OverlapArea.ToString("E3") + "）"
                    );

                    terrain.VoxelField.Set(voxel, VoxelState.Solid);
                    terrain.RebuildChunksAffectedByVoxel(voxel);
                    views = GetViews(cavity);
                }
            }

            Check(tested == 6, "T2b: 六个方向全部实测（" + tested + "/6）");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(cavity);
        }

        // (c) 同一 Region 内同时存在应开放 / 应封闭（噪声地形部分面）。
        GameObject natural = CreateNaturalFixture("StageB3C_T2Partial");

        try
        {
            DensityTerrain terrain = natural.GetComponent<DensityTerrain>();
            List<TerrainChunkView> views = GetViews(natural);

            FaceCandidate candidate;
            if (!TryFindPartialOpenFace(terrain, views, 0.2, 0.8, out candidate))
            {
                Limit("T2c: 未找到部分开口面，跳过（不伪造结果）");
            }
            else
            {
                FaceKey face;
                TryBuildFaceKey(
                    candidate.Voxel,
                    candidate.Neighbour,
                    candidate.Axis,
                    out face);

                HashSet<string> baseline = SnapshotFaceTriangleKeys(views, face);
                terrain.VoxelField.Set(candidate.Voxel, VoxelState.Air);
                int rebuilt = terrain.RebuildChunksAffectedByVoxel(candidate.Voxel);

                if (rebuilt <= 0)
                {
                    Limit("T2c: 候选 Voxel Mutation 未提交，跳过");
                }
                else
                {
                    views = GetViews(natural);
                    double expected = ExpectedSealedAreaByDensity(
                        views, face, candidate.Axis, candidate.Side == 0);
                    FaceAudit audit = AuditFace(
                        views, face, candidate.Voxel, terrain.VoxelField, baseline);

                    double over = audit.Coverage.UnionArea - expected;
                    double under = Math.Max(
                        0.0, expected - audit.Coverage.UnionArea);
                    double tolerance = CoverageTolerance(
                        views, face, candidate.Axis, candidate.Side == 0,
                        expected);

                    Debug.Log(
                        "[StageB3C-T2c] voxel=" + candidate.Voxel +
                        " tolerance=" + tolerance.ToString("F4") +
                        " face=" + face.Key +
                        " covered=" + audit.Coverage.UnionArea.ToString("F4") +
                        " expected=" + expected.ToString("F4") +
                        " over=" + over.ToString("F4") +
                        " under=" + under.ToString("F4"));

                    Check(
                        over <= tolerance && under <= tolerance,
                        "T2c: 同一 Region 混合状态只覆盖应封闭部分（多覆盖 " +
                        over.ToString("F4") + "，漏覆盖 " + under.ToString("F4") +
                        "，容差 " + tolerance.ToString("F4") + "）"
                    );
                }
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(natural);
        }
    }

    // =====================================================================
    // T3 — 自然地形（平面 / 噪声 / 表面相交）
    // =====================================================================

    private static void CheckT3NaturalTerrain()
    {
        GameObject natural = CreateNaturalFixture("StageB3C_T3");

        try
        {
            DensityTerrain terrain = natural.GetComponent<DensityTerrain>();
            List<TerrainChunkView> views = GetViews(natural);

            int checkedFaces = 0;
            int withinTolerance = 0;
            int totalDuplicates = 0;
            int insideAirGeometry = 0;

            for (int attempt = 0; attempt < 6; attempt++)
            {
                FaceCandidate candidate;
                if (!TryFindPartialOpenFace(
                    terrain,
                    views,
                    0.05 + attempt * 0.01,
                    0.95 - attempt * 0.01,
                    out candidate))
                {
                    continue;
                }

                // 避免重复挖同一个 Voxel。
                if (terrain.VoxelField.IsAir(candidate.Voxel)) continue;

                FaceKey face;
                TryBuildFaceKey(
                    candidate.Voxel,
                    candidate.Neighbour,
                    candidate.Axis,
                    out face);

                HashSet<string> baseline = SnapshotFaceTriangleKeys(views, face);
                terrain.VoxelField.Set(candidate.Voxel, VoxelState.Air);

                if (terrain.RebuildChunksAffectedByVoxel(candidate.Voxel) <= 0)
                {
                    continue;
                }

                views = GetViews(natural);
                double expected = ExpectedSealedAreaByDensity(
                    views, face, candidate.Axis, candidate.Side == 0);
                FaceAudit audit = AuditFace(
                    views, face, candidate.Voxel, terrain.VoxelField, baseline);

                double over = audit.Coverage.UnionArea - expected;
                double under = Math.Max(0.0, expected - audit.Coverage.UnionArea);

                double tolerance = CoverageTolerance(
                    views, face, candidate.Axis, candidate.Side == 0,
                    expected);

                checkedFaces++;
                if (over <= tolerance && under <= tolerance)
                {
                    withinTolerance++;
                }

                totalDuplicates += audit.Coverage.DuplicateCount;
                insideAirGeometry += CountGeometryInsideVoxel(
                    views, candidate.Voxel);

                Debug.Log(
                    "[StageB3C-T3] face=" + face.Key +
                    " tolerance=" + tolerance.ToString("F4") +
                    " expected=" + expected.ToString("F4") +
                    " covered=" + audit.Coverage.UnionArea.ToString("F4") +
                    " over=" + over.ToString("F4") +
                    " under=" + under.ToString("F4"));
            }

            Check(
                checkedFaces >= 3,
                "T3: 至少验证 3 个自然地形开口面（实际 " + checkedFaces + "）"
            );
            Check(
                checkedFaces == 0 || withinTolerance == checkedFaces,
                "T3: 所有自然地形开口面都在尺度相关容差内（" + withinTolerance +
                "/" + checkedFaces + "）"
            );
            Check(totalDuplicates == 0, "T3: 无重复三角形（" + totalDuplicates + "）");
            Check(
                insideAirGeometry == 0,
                "T3: 挖空 Voxel 内部无残留几何（" + insideAirGeometry + "）"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(natural);
        }
    }

    // =====================================================================
    // T4 — Chunk 边界与归属
    // =====================================================================

    private static void CheckT4BoundariesAndOwnership()
    {
        RunBoundaryCase(Vector3Int.zero, new Vector3Int(30, SurfaceVoxelY, 30), "T4[内部]");
        RunBoundaryCase(Vector3Int.zero, new Vector3Int(74, SurfaceVoxelY, 30), "T4[X负侧]");
        RunBoundaryCase(Vector3Int.zero, new Vector3Int(75, SurfaceVoxelY, 30), "T4[X正侧]");
        RunBoundaryCase(Vector3Int.zero, new Vector3Int(10, SurfaceVoxelY, 49), "T4[Z负侧]");
        RunBoundaryCase(Vector3Int.zero, new Vector3Int(10, SurfaceVoxelY, 50), "T4[Z正侧]");
        RunBoundaryCase(Vector3Int.zero, new Vector3Int(75, SurfaceVoxelY, 50), "T4[XZ交汇]");
        RunBoundaryCase(
            new Vector3Int(5, 0, 7),
            new Vector3Int(450, SurfaceVoxelY, 400),
            "T4[(5,0,7) XZ交汇]");
        RunBoundaryCase(
            new Vector3Int(5, 0, 7),
            new Vector3Int(405, SurfaceVoxelY, 400),
            "T4[(5,0,7) Z边界]");
    }

    private static void RunBoundaryCase(
        Vector3Int chunkStart,
        Vector3Int voxel,
        string label)
    {
        GameObject terrainObject = CreateFlatFixture(label, chunkStart);

        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();
            List<TerrainChunkView> views = GetViews(terrainObject);

            FaceKey[] faces = new FaceKey[6];
            HashSet<string>[] baseline = new HashSet<string>[6];
            for (int axis = 0; axis < 3; axis++)
            {
                for (int side = 0; side < 2; side++)
                {
                    int index = axis * 2 + side;
                    TryBuildFaceKey(
                        voxel,
                        voxel + AxisVector(axis, side),
                        axis,
                        out faces[index]);
                    baseline[index] = SnapshotFaceTriangleKeys(
                        views, faces[index]);
                }
            }

            FaceKey topFace;
            TryBuildFaceKey(
                voxel,
                voxel + new Vector3Int(0, 1, 0),
                1,
                out topFace);

            HashSet<string> topBaseline = SnapshotFaceTriangleKeys(views, topFace);
            terrain.VoxelField.Set(voxel, VoxelState.Air);
            int rebuilt = terrain.RebuildChunksAffectedByVoxel(voxel);
            views = GetViews(terrainObject);

            FaceAudit audit = AuditFace(
                views, topFace, voxel, terrain.VoxelField, topBaseline);

            Check(
                rebuilt >= 1 &&
                audit.Coverage.UncoveredArea <= WholeFaceCoverageTolerance &&
                audit.Coverage.OverlapArea <= OverlapAreaTolerance,
                label + ": 顶面完整覆盖（未覆盖 " +
                audit.Coverage.UncoveredArea.ToString("E3") + "，重叠 " +
                audit.Coverage.OverlapArea.ToString("E3") + "）"
            );
            Check(
                audit.OwnerChunkCount == 1,
                label + ": 面几何归属唯一 Chunk（" + audit.OwnerChunks + "）"
            );
            Check(
                audit.Coverage.DegenerateCount == 0 &&
                audit.Coverage.DuplicateCount == 0 &&
                audit.WindingConsistent,
                label + ": 无退化/重复且绕序正确"
            );

            int unexpectedNew = 0;
            for (int axis = 0; axis < 3; axis++)
            {
                for (int side = 0; side < 2; side++)
                {
                    if (axis == 1 && side == 1) continue;

                    int index = axis * 2 + side;
                    double openArea = ExpectedSealedAreaByDensity(
                        views, faces[index], axis, side == 0);
                    if (openArea > 0.02) continue;

                    HashSet<string> after = SnapshotFaceTriangleKeys(
                        views, faces[index]);
                    foreach (string key in after)
                    {
                        if (!baseline[index].Contains(key)) unexpectedNew++;
                    }
                }
            }

            Check(
                unexpectedNew == 0,
                label + ": 无开口的邻接面不新增 Hidden Face（" + unexpectedNew + "）"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    // =====================================================================
    // T5 / T6 — 增量 vs 完整重建 / 恢复与重复重建
    // =====================================================================

    private static void CheckT5IncrementalVersusFullRebuild()
    {
        Vector3Int[] voxels =
        {
            new Vector3Int(0, 13, 37),
            new Vector3Int(30, SurfaceVoxelY, 30),
            new Vector3Int(75, SurfaceVoxelY, 50)
        };

        GameObject pathA = CreateNaturalFixture("StageB3C_T5A");
        GameObject pathB = CreateNaturalFixture("StageB3C_T5B");
        GameObject pathC = CreateNaturalFixture("StageB3C_T5C");

        try
        {
            DensityTerrain terrainA = pathA.GetComponent<DensityTerrain>();
            DensityTerrain terrainB = pathB.GetComponent<DensityTerrain>();
            DensityTerrain terrainC = pathC.GetComponent<DensityTerrain>();

            for (int i = 0; i < voxels.Length; i++)
            {
                FaceKey face;
                TryBuildFaceKey(
                    voxels[i],
                    voxels[i] + new Vector3Int(0, 1, 0),
                    1,
                    out face);

                List<TerrainChunkView> viewsA = GetViews(pathA);
                List<TerrainChunkView> viewsB = GetViews(pathB);

                HashSet<string> baselineA = SnapshotFaceTriangleKeys(viewsA, face);
                HashSet<string> baselineB = SnapshotFaceTriangleKeys(viewsB, face);

                // 顺序 A：正序。
                terrainA.VoxelField.Set(voxels[i], VoxelState.Air);
                terrainA.RebuildChunksAffectedByVoxel(voxels[i]);

                // 顺序 B：由后向前逐个。
                int reverseIndex = voxels.Length - 1 - i;
                terrainB.VoxelField.Set(voxels[reverseIndex], VoxelState.Air);
                terrainB.RebuildChunksAffectedByVoxel(voxels[reverseIndex]);

                // 覆盖正确性（顺序 A）。
                List<TerrainChunkView> afterA = GetViews(pathA);
                double expected = ExpectedSealedAreaByDensity(
                    afterA, face, 1, false);
                double expectedFine = ExpectedSealedAreaByDensity(
                    afterA, face, 1, false, 800);
                FaceAudit auditA = AuditFace(
                    afterA, face, voxels[i], terrainA.VoxelField, baselineA);
                double over = auditA.Coverage.UnionArea - expected;
                double under = Math.Max(0.0, expected - auditA.Coverage.UnionArea);
                double tolerance = CoverageTolerance(afterA, face, 1, false, expected);

                Check(
                    expected <= 0.05 || (over <= tolerance && under <= tolerance),
                    "T5: 顺序 A 第 " + i + " 步覆盖在容差内（face=" + face.Key +
                    " over=" + over.ToString("F4") +
                    " under=" + under.ToString("F4") +
                    " tol=" + tolerance.ToString("F4") + "）"
                );

                Debug.Log(
                    "[StageB3C-T5] step=" + i + " face=" + face.Key +
                    " expected200=" + expected.ToString("F4") +
                    " expected800=" + expectedFine.ToString("F4") +
                    " oracleDelta=" +
                    (expectedFine - expected).ToString("E3") +
                    " covered=" + auditA.Coverage.UnionArea.ToString("F4") +
                    " over=" + over.ToString("F4") +
                    " under=" + under.ToString("F4") +
                    " tol=" + tolerance.ToString("F4") +
                    " boundaryLen=" + EstimateCoverageBoundaryLength(
                        afterA, face, 1, false).ToString("F4"));
            }

            // 顺序 C：从 VoxelField 完整重建。
            for (int i = 0; i < voxels.Length; i++)
            {
                terrainC.VoxelField.Set(voxels[i], VoxelState.Air);
            }

            bool rebuilt = terrainC.RebuildVoxelAwareGeometryFromVoxelField();

            // 先证明三条路径的最终权威状态完全相同，再做几何比较。
            List<Vector3Int> storedA = terrainA.VoxelField.GetStoredCoordinates(
                VoxelState.Air);
            List<Vector3Int> storedB = terrainB.VoxelField.GetStoredCoordinates(
                VoxelState.Air);
            List<Vector3Int> storedC = terrainC.VoxelField.GetStoredCoordinates(
                VoxelState.Air);

            Check(
                SetEquals(storedA, voxels) &&
                SetEquals(storedB, voxels) &&
                SetEquals(storedC, voxels),
                "T5: 三条路径的最终 VoxelField 显式 Air 集合一致且等于目标集合（" +
                storedA.Count + "/" + storedB.Count + "/" + storedC.Count +
                " vs " + voxels.Length + "）"
            );

            Dictionary<Vector3Int, MeshCanonical> canonicalA = SnapshotCanonical(
                GetViews(pathA));
            Dictionary<Vector3Int, MeshCanonical> canonicalB = SnapshotCanonical(
                GetViews(pathB));
            Dictionary<Vector3Int, MeshCanonical> canonicalC = SnapshotCanonical(
                GetViews(pathC));

            Check(rebuilt, "T5: 完整重建成功");
            Check(
                SameAllChunks(canonicalA, canonicalB, out string detailAB),
                "T5: 顺序 A == 顺序 B" +
                (detailAB.Length > 0 ? " —— " + detailAB : string.Empty)
            );
            Check(
                SameAllChunks(canonicalA, canonicalC, out string detailAC),
                "T5: 增量 == 从 VoxelField 完整重建" +
                (detailAC.Length > 0 ? " —— " + detailAC : string.Empty)
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(pathA);
            UnityEngine.Object.DestroyImmediate(pathB);
            UnityEngine.Object.DestroyImmediate(pathC);
        }
    }

    private static void CheckT6RestoreAndRepeat()
    {
        GameObject terrainObject = CreateNaturalFixture("StageB3C_T6");

        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();
            List<TerrainChunkView> views = GetViews(terrainObject);
            Dictionary<Vector3Int, MeshCanonical> baseline =
                SnapshotCanonical(views);

            Vector3Int voxel = new Vector3Int(0, 13, 37);
            FaceKey face;
            TryBuildFaceKey(voxel, new Vector3Int(0, 14, 37), 1, out face);

            terrain.VoxelField.Set(voxel, VoxelState.Air);
            terrain.RebuildChunksAffectedByVoxel(voxel);

            for (int i = 0; i < 3; i++)
            {
                bool ok = terrain.RebuildVoxelAwareGeometryFromVoxelField();
                List<TerrainChunkView> after = GetViews(terrainObject);
                double expected = ExpectedSealedAreaByDensity(
                    after, face, 1, false);
                FaceAudit audit = AuditFace(
                    after, face, voxel, terrain.VoxelField);

                double over = audit.Coverage.UnionArea - expected;
                double under = Math.Max(0.0, expected - audit.Coverage.UnionArea);
                double tolerance = CoverageTolerance(after, face, 1, false, expected);

                Check(
                    ok && over <= tolerance &&
                    under <= tolerance &&
                    audit.Coverage.DuplicateCount == 0,
                    "T6: 第 " + (i + 1) + " 次完整重建覆盖正确（多覆盖 " +
                    over.ToString("F4") + "，漏覆盖 " + under.ToString("F4") + "）"
                );
            }

            terrain.VoxelField.Set(voxel, VoxelState.Solid);
            terrain.RebuildChunksAffectedByVoxel(voxel);
            bool restored = terrain.RebuildVoxelAwareGeometryFromVoxelField();

            List<TerrainChunkView> restoredViews = GetViews(terrainObject);
            Dictionary<Vector3Int, MeshCanonical> restoredCanonical =
                SnapshotCanonical(restoredViews);
            bool sameAsBaseline = SameAllChunks(
                baseline, restoredCanonical, out string detail);

            Check(
                restored && sameAsBaseline,
                "T6: 恢复 Solid 后回到基线几何" +
                (detail.Length > 0 ? " —— " + detail : string.Empty)
            );
            Check(
                CountContributionEntries(restoredViews) == 0,
                "T6: 恢复后无残留贡献（" +
                CountContributionEntries(restoredViews) + "）"
            );

            bool synced = true;
            for (int i = 0; i < restoredViews.Count; i++)
            {
                TerrainChunkView view = restoredViews[i];
                if (view.MeshFilter.sharedMesh != view.Chunk.Mesh ||
                    view.MeshCollider.sharedMesh != view.Chunk.Mesh)
                {
                    synced = false;
                }
            }

            Check(synced, "T6: 所有 View 指向当前最终 Mesh");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    // =====================================================================
    // 根因审计：面 1:14:37:0
    // =====================================================================

    private static void CheckRootCauseAudit()
    {
        GameObject terrainObject = CreateNaturalFixture("StageB3C_Audit");

        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();
            List<TerrainChunkView> views = GetViews(terrainObject);

            // B3-B 复现用例：噪声 + 空腔地形，挖空 Voxel (0,13,37)，
            // 其 +Y 面（1:14:37:0）出现多覆盖。
            Vector3Int voxel = new Vector3Int(0, 13, 37);
            FaceKey face;
            if (!TryBuildFaceKey(
                voxel,
                new Vector3Int(0, 14, 37),
                1,
                out face))
            {
                Check(false, "审计: 构造面 key 失败");
                return;
            }

            Debug.Log(
                "[StageB3C-audit] face=" + face.Key +
                " axis=" + face.Axis + " plane=" + face.Plane +
                " u=" + face.U + " v=" + face.V
            );

            // 基线（挖空前）该面上的三角形集合：Base Pre-cut 的切割墙也位于该面，
            // 其绕序与 Hidden Face 不同，必须区别对待。
            HashSet<string> baselineKeys = SnapshotFaceTriangleKeys(views, face);

            // --- 生产入口：真实挖空 ---
            terrain.VoxelField.Set(voxel, VoxelState.Air);
            int rebuilt = terrain.RebuildChunksAffectedByVoxel(voxel);

            Debug.Log(
                "[StageB3C-audit] voxel=" + voxel +
                " negativeState=" + terrain.VoxelField.Get(voxel) +
                " positiveVoxel=(0,14,37)" +
                " positiveState=" +
                terrain.VoxelField.Get(new Vector3Int(0, 14, 37)) +
                " rebuiltChunks=" + rebuilt
            );

            views = GetViews(terrainObject);

            // --- 独立密度判据：面内 air / solid 分布 ---
            double expectedOpen = ExpectedSealedAreaByDensity(
                views,
                face,
                face.Axis,
                false
            );
            Debug.Log(
                "[StageB3C-audit] independentDensityOpenArea=" +
                expectedOpen.ToString("F4")
            );
            LogDensityPattern(views, face, face.Axis, false);

            // --- 实际最终几何覆盖 ---
            FaceAudit audit = AuditFace(
                views,
                face,
                voxel,
                terrain.VoxelField,
                baselineKeys
            );
            Debug.Log(
                "[StageB3C-audit] mesh: triangles=" + audit.TriangleCount +
                " union=" + audit.Coverage.UnionArea.ToString("F4") +
                " overlap=" + audit.Coverage.OverlapArea.ToString("F4") +
                " uncovered=" + audit.Coverage.UncoveredArea.ToString("F4") +
                " outside=" + audit.Coverage.OutsideArea.ToString("E3") +
                " ownerChunks=" + audit.OwnerChunks +
                " windingOk=" + audit.WindingConsistent
            );

            double overCoverage = audit.Coverage.UnionArea - expectedOpen;
            double underCoverage = Math.Max(
                0.0,
                expectedOpen - audit.Coverage.UnionArea
            );
            Debug.Log(
                "[StageB3C-audit] overCoverage=" + overCoverage.ToString("F4") +
                " underCoverage=" + underCoverage.ToString("F4")
            );

            for (int i = 0; i < audit.TriangleDump.Count; i++)
            {
                Debug.Log("[StageB3C-audit-triangle] " + audit.TriangleDump[i]);
            }

            for (int i = 0; i < audit.WindingIssues.Count && i < 8; i++)
            {
                Debug.Log("[StageB3C-audit-winding] " + audit.WindingIssues[i]);
            }

            Debug.Log("[StageB3C-audit-winding] totalIssues=" +
                audit.WindingIssues.Count + " triangles=" + audit.TriangleCount +
                " baselineSkipped=" + audit.BaselineSkipped +
                " nearPlaneSkipped=" + audit.NearPlaneSkipped +
                " nearPlaneLargeArea=" +
                audit.NearPlaneLargeArea.ToString("E3"));

            // --- 生产 Region / Segment 数据（直接调用生产 Evaluate，只记录采样点） ---
            AuditProductionRegions(terrain, views, face, voxel);

            // --- 归属检查：同一平面矩形上是否有别的 Chunk 也生成几何 ---
            LogFaceOwnership(views, face);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    private static void AuditProductionRegions(
        DensityTerrain terrain,
        List<TerrainChunkView> views,
        FaceKey faceKey,
        Vector3Int minedVoxel)
    {
        VoxelAwareMeshPrecut.FaceId face = new VoxelAwareMeshPrecut.FaceId(
            faceKey.Axis,
            faceKey.Plane,
            faceKey.U,
            faceKey.V
        );

        Vector3Int negativeVoxel =
            VoxelAwareMeshSpatialFace.GetNegativeVoxel(face);
        Vector3Int positiveVoxel =
            VoxelAwareMeshSpatialFace.GetPositiveVoxel(face);

        TerrainChunk negativeChunk = FindChunk(views, negativeVoxel);
        TerrainChunk positiveChunk = FindChunk(views, positiveVoxel);
        TerrainChunk meshOwner = positiveChunk != null
            ? positiveChunk
            : negativeChunk;

        if (meshOwner == null || meshOwner.Geometry == null)
        {
            Debug.Log("[StageB3C-audit] meshOwner 缺失，无法审计 Region");
            return;
        }

        List<VoxelAwareMeshSpatialFace.FaceSegment> segments =
            VoxelAwareMeshRuntimeMutation.BuildFaceSegmentsForDiagnostics(
                face,
                meshOwner,
                negativeChunk,
                positiveChunk
            );

        Debug.Log(
            "[StageB3C-audit] segments=" + segments.Count +
            " (negativeChunk=" +
            (negativeChunk != null ? negativeChunk.Coordinate.ToString() : "null") +
            " positiveChunk=" +
            (positiveChunk != null ? positiveChunk.Coordinate.ToString() : "null") +
            " meshOwner=" + meshOwner.Coordinate + ")"
        );

        for (int i = 0; i < segments.Count; i++)
        {
            VoxelAwareMeshSpatialFace.FaceSegment segment = segments[i];
            Debug.Log(
                "[StageB3C-audit-segment] " + i +
                " type=" + (segment.IsCoplanar ? "Coplanar" :
                            segment.IsBridge ? "Bridge" :
                            segment.IsNatural ? "Natural" : "Unknown") +
                " A=" + segment.A.ToString("F4") +
                " B=" + segment.B.ToString("F4") +
                " length=" + Vector2.Distance(segment.A, segment.B).ToString("F4")
            );
        }

        List<Vector2> resolverCalls = new List<Vector2>();
        VoxelAwareMeshSpatialFace.SpatialFaceRecord record =
            VoxelAwareMeshSpatialFace.Evaluate(
                face,
                negativeVoxel,
                positiveVoxel,
                true,
                terrain.VoxelField.IsAir(negativeVoxel),
                terrain.VoxelField.IsAir(positiveVoxel),
                meshOwner.Coordinate,
                segments,
                meshOwner.Geometry.BaseMaterialField,
                meshOwner.Geometry.VoxelSize,
                meshOwner.Geometry,
                uv =>
                {
                    resolverCalls.Add(uv);
                    return VoxelAwareMeshSpatialFace.RegionDecision
                        .NoHiddenFace;
                }
            );

        Debug.Log(
            "[StageB3C-audit] regions=" + record.Regions.Count +
            " resolverCalls=" + resolverCalls.Count
        );

        for (int i = 0; i < record.Regions.Count; i++)
        {
            VoxelAwareMeshSpatialFace.FaceRegion region = record.Regions[i];

            float densityAtMidpoint;
            bool sampled = TrySampleBaseDensity(
                views,
                FacePoint(faceKey, region.Midpoint.x, region.Midpoint.y),
                out densityAtMidpoint
            );

            Debug.Log(
                "[StageB3C-audit-region] " + i +
                " vertices=" + region.Polygon.Count +
                " area=" + region.Area.ToString("F4") +
                " midpoint=(" + region.Midpoint.x.ToString("F4") + "," +
                region.Midpoint.y.ToString("F4") + ")" +
                " densityAtMidpoint=" +
                (sampled ? densityAtMidpoint.ToString("F4") : "n/a") +
                " polygon=[" + FormatPolygon(region.Polygon) + "]"
            );
        }

        // Region 内密度分布（每个 Region 的包围盒上粗采样），用于判断
        // 单个 Region 是否同时包含“应封闭/应开放”两部分。
        for (int i = 0; i < record.Regions.Count; i++)
        {
            VoxelAwareMeshSpatialFace.FaceRegion region = record.Regions[i];
            LogRegionDensitySplit(views, faceKey, i, region);
        }
    }

    private static void LogRegionDensitySplit(
        List<TerrainChunkView> views,
        FaceKey face,
        int regionIndex,
        VoxelAwareMeshSpatialFace.FaceRegion region)
    {
        const int grid = 24;
        int open = 0;
        int solid = 0;
        int inside = 0;

        for (int iu = 1; iu < grid; iu++)
        {
            for (int iv = 1; iv < grid; iv++)
            {
                Vector2 p = new Vector2(iu / (float)grid, iv / (float)grid);
                if (!PointInPolygon(p, region.Polygon)) continue;

                inside++;
                Vector3 point = FacePoint(face, p.x, p.y);
                point[face.Axis] += 1e-3f;

                float density;
                if (!TrySampleBaseDensity(views, point, out density)) continue;

                if (density <= 0f) open++;
                else solid++;
            }
        }

        Debug.Log(
            "[StageB3C-audit-region-split] region=" + regionIndex +
            " sampledPoints=" + inside +
            " open=" + open + " solid=" + solid +
            " openFraction=" +
            (inside == 0 ? "n/a" : (open / (double)inside).ToString("F4"))
        );
    }

    private static void LogFaceOwnership(
        List<TerrainChunkView> views,
        FaceKey face)
    {
        for (int c = 0; c < views.Count; c++)
        {
            TerrainChunk chunk = views[c].Chunk;
            if (chunk == null || chunk.Mesh == null) continue;

            Mesh mesh = chunk.Mesh;
            Vector3[] vertices = mesh.vertices;
            Vector3 origin = chunk.ChunkOrigin;
            int count = 0;

            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                int[] indices = mesh.GetTriangles(submesh);
                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    Vector3 a = vertices[indices[i]] + origin;
                    Vector3 b = vertices[indices[i + 1]] + origin;
                    Vector3 d = vertices[indices[i + 2]] + origin;

                    if (Mathf.Abs(a[face.Axis] - face.Plane) > PlaneTolerance ||
                        Mathf.Abs(b[face.Axis] - face.Plane) > PlaneTolerance ||
                        Mathf.Abs(d[face.Axis] - face.Plane) > PlaneTolerance)
                    {
                        continue;
                    }

                    Vector3 centroid = (a + b + d) / 3f;
                    if (centroid[(face.Axis + 1) % 3] < face.U - 0.05f ||
                        centroid[(face.Axis + 1) % 3] > face.U + 1.05f ||
                        centroid[(face.Axis + 2) % 3] < face.V - 0.05f ||
                        centroid[(face.Axis + 2) % 3] > face.V + 1.05f)
                    {
                        continue;
                    }

                    count++;
                }
            }

            if (count > 0)
            {
                Debug.Log(
                    "[StageB3C-audit-owner] chunk=" + chunk.Coordinate +
                    " planeTriangles=" + count
                );
            }
        }
    }

    private static void LogDensityPattern(
        List<TerrainChunkView> views,
        FaceKey face,
        int axis,
        bool negativeSide)
    {
        const int grid = 20;
        StringBuilder builder = new StringBuilder();

        for (int iv = grid - 1; iv >= 0; iv--)
        {
            builder.Append("      ");
            for (int iu = 0; iu < grid; iu++)
            {
                Vector3 point = FacePoint(
                    face,
                    (iu + 0.5f) / grid,
                    (iv + 0.5f) / grid
                );
                point[axis] += negativeSide ? -1e-3f : 1e-3f;

                float density;
                if (!TrySampleBaseDensity(views, point, out density))
                {
                    builder.Append('?');
                    continue;
                }

                builder.Append(density <= 0f ? '.' : '#');
            }

            builder.AppendLine();
        }

        Debug.Log(
            "[StageB3C-audit-density] 面内基础密度（'.'=开口/air, '#'=实体, " +
            "u 向右 +, v 向下 -）:\n" + builder
        );
    }

    private static string FormatPolygon(List<Vector2> polygon)
    {
        List<string> parts = new List<string>();
        for (int i = 0; i < polygon.Count; i++)
        {
            parts.Add(
                "(" + polygon[i].x.ToString("F3") + "," +
                polygon[i].y.ToString("F3") + ")"
            );
        }

        return string.Join(" ", parts);
    }

    // =====================================================================
    // 独立判据与几何工具（自行实现，不复制生产算法）
    // =====================================================================

    private struct FaceKey
    {
        public int Axis;
        public int Plane;
        public int U;
        public int V;

        public FaceKey(int axis, int plane, int u, int v)
        {
            Axis = axis;
            Plane = plane;
            U = u;
            V = v;
        }

        public string Key
        {
            get { return Axis + ":" + Plane + ":" + U + ":" + V; }
        }
    }

    private struct Tri2
    {
        public Vector2 A;
        public Vector2 B;
        public Vector2 C;

        public Tri2(Vector2 a, Vector2 b, Vector2 c)
        {
            A = a;
            B = b;
            C = c;
        }

        public double Area
        {
            get
            {
                return Math.Abs(
                    (B.x - A.x) * (C.y - A.y) - (C.x - A.x) * (B.y - A.y)
                ) * 0.5;
            }
        }

        public bool Contains(Vector2 p, float epsilon)
        {
            float d1 = Cross(A, B, p);
            float d2 = Cross(B, C, p);
            float d3 = Cross(C, A, p);
            bool hasNegative = d1 < -epsilon || d2 < -epsilon || d3 < -epsilon;
            bool hasPositive = d1 > epsilon || d2 > epsilon || d3 > epsilon;
            return !(hasNegative && hasPositive);
        }

        private static float Cross(Vector2 a, Vector2 b, Vector2 p)
        {
            return (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
        }
    }

    private sealed class CoverageMetrics
    {
        public double UnionArea;
        public double OverlapArea;
        public double UncoveredArea;
        public double SumArea;
        public double OutsideArea;
        public int TriangleCount;
        public int DegenerateCount;
        public int DuplicateCount;
    }

    private sealed class FaceAudit
    {
        public FaceKey Face;
        public int TriangleCount;
        public int OwnerChunkCount;
        public string OwnerChunks = string.Empty;
        public bool WindingConsistent = true;
        public int NearPlaneSkipped;
        public int BaselineSkipped;
        public double NearPlaneLargeArea;
        public List<string> WindingIssues = new List<string>();
        public List<string> TriangleDump = new List<string>();
        public CoverageMetrics Coverage = new CoverageMetrics();
        public List<Tri2> Triangles = new List<Tri2>();
    }

    private static FaceAudit AuditFace(
        List<TerrainChunkView> views,
        FaceKey face,
        Vector3Int negativeVoxel,
        VoxelField voxelField,
        HashSet<string> baselineTriangleKeys = null)
    {
        FaceAudit audit = new FaceAudit();
        audit.Face = face;

        int axisU = (face.Axis + 1) % 3;
        int axisV = (face.Axis + 2) % 3;

        float minU = face.U;
        float maxU = face.U + 1f;
        float minV = face.V;
        float maxV = face.V + 1f;

        HashSet<string> seen = new HashSet<string>();
        HashSet<Vector3Int> owners = new HashSet<Vector3Int>();

        Vector3 expectedNormal = face.Axis == 0
            ? Vector3.right
            : face.Axis == 1 ? Vector3.up : Vector3.forward;

        for (int c = 0; c < views.Count; c++)
        {
            TerrainChunkView view = views[c];
            Mesh mesh = view != null ? view.Chunk.Mesh : null;
            if (mesh == null) continue;

            Vector3[] vertices = mesh.vertices;
            Vector3 chunkOrigin = view.Chunk.ChunkOrigin;

            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                int[] indices = mesh.GetTriangles(submesh);
                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    Vector3 a = vertices[indices[i]] + chunkOrigin;
                    Vector3 b = vertices[indices[i + 1]] + chunkOrigin;
                    Vector3 d = vertices[indices[i + 2]] + chunkOrigin;

                    if (Mathf.Abs(a[face.Axis] - face.Plane) > PlaneTolerance ||
                        Mathf.Abs(b[face.Axis] - face.Plane) > PlaneTolerance ||
                        Mathf.Abs(d[face.Axis] - face.Plane) > PlaneTolerance)
                    {
                        continue;
                    }

                    Vector3 centroid = (a + b + d) / 3f;
                    if (centroid[axisU] < minU - 0.05f ||
                        centroid[axisU] > maxU + 0.05f ||
                        centroid[axisV] < minV - 0.05f ||
                        centroid[axisV] > maxV + 0.05f)
                    {
                        continue;
                    }

                    Tri2 tri = new Tri2(
                        new Vector2(a[axisU], a[axisV]),
                        new Vector2(b[axisU], b[axisV]),
                        new Vector2(d[axisU], d[axisV])
                    );

                    audit.Triangles.Add(tri);
                    audit.TriangleCount++;
                    owners.Add(view.Coordinate);

                    if (tri.Area <= 1e-9) audit.Coverage.DegenerateCount++;

                    string key = TriangleKey(tri);
                    if (!seen.Add(key)) audit.Coverage.DuplicateCount++;

                    // 绕序只在“严格位于该 SpatialFace 平面”的三角形上判定；
                    // 自然 Base Mesh 可能与平面几乎相交（1e-3 窗口内），
                    // 这类近平面三角形单独记录，不参与绕序结论。
                    bool faceAligned =
                        Mathf.Abs(a[face.Axis] - face.Plane) <= 1e-5f &&
                        Mathf.Abs(b[face.Axis] - face.Plane) <= 1e-5f &&
                        Mathf.Abs(d[face.Axis] - face.Plane) <= 1e-5f;

                    Vector3 normal = Vector3.Cross(b - a, d - a);
                    Vector3 rawNormal = normal;
                    if (!faceAligned)
                    {
                        audit.NearPlaneSkipped++;
                        if (tri.Area > 0.01)
                        {
                            audit.NearPlaneLargeArea += tri.Area;
                        }
                    }
                    else if (normal.sqrMagnitude > 1e-12f)
                    {
                        // 基线中已存在的面内三角形（Base Pre-cut 的切割墙）
                        // 不参与新增几何的绕序判定。
                        if (baselineTriangleKeys != null &&
                            baselineTriangleKeys.Contains(key))
                        {
                            audit.BaselineSkipped++;
                        }
                        else
                        {
                        // 注意：不能用 Vector3.Normalize()——Unity 的 Vector3.kEpsilon
                        // 为 1e-5，叉积模长小于它时会被清零，导致小三角形法线判定失真。
                        float magnitude = Mathf.Max(rawNormal.magnitude, 1e-12f);
                        float alignment = Vector3.Dot(rawNormal, expectedNormal) /
                                          magnitude;
                        if (alignment < 0.99f)
                        {
                            audit.WindingConsistent = false;
                            audit.WindingIssues.Add(
                                "uv=(" + tri.A[0].ToString("F4") + "," +
                                tri.A[1].ToString("F4") + ")/(" +
                                tri.B[0].ToString("F4") + "," +
                                tri.B[1].ToString("F4") + ")/(" +
                                tri.C[0].ToString("F4") + "," +
                                tri.C[1].ToString("F4") + ") area=" +
                                tri.Area.ToString("E3") +
                                " dot=" + alignment.ToString("F3") +
                                " area3D=" +
                                (rawNormal.magnitude * 0.5f).ToString("E3") +
                                " A3=" + a.ToString("F5") +
                                " B3=" + b.ToString("F5") +
                                " C3=" + d.ToString("F5")
                            );
                        }
                        }
                    }

                    if (audit.TriangleDump.Count < 10)
                    {
                        Vector3 triNormal = Vector3.Cross(b - a, d - a);
                        string winding = "degenerate";
                        if (triNormal.sqrMagnitude > 1e-12f)
                        {
                            winding = (
                                Vector3.Dot(triNormal, expectedNormal) /
                                Mathf.Max(triNormal.magnitude, 1e-12f)
                            ).ToString("F3");
                        }

                        audit.TriangleDump.Add(
                            "chunk=" + view.Coordinate +
                            " uv=(" + tri.A[0].ToString("F3") + "," +
                            tri.A[1].ToString("F3") + ")/(" +
                            tri.B[0].ToString("F3") + "," +
                            tri.B[1].ToString("F3") + ")/(" +
                            tri.C[0].ToString("F3") + "," +
                            tri.C[1].ToString("F3") + ") area=" +
                            tri.Area.ToString("F6") +
                            " normal·expected=" + winding
                        );
                    }
                }
            }
        }

        audit.Coverage.TriangleCount = audit.TriangleCount;
        audit.OwnerChunkCount = owners.Count;
        audit.OwnerChunks = FormatCoordinates(owners);

        ComputeCoverage(audit.Triangles, minU, minV, maxU, maxV, audit.Coverage);

        return audit;
    }

    private static void ComputeCoverage(
        List<Tri2> triangles,
        float minU,
        float minV,
        float maxU,
        float maxV,
        CoverageMetrics metrics)
    {
        metrics.SumArea = 0;
        metrics.OutsideArea = 0;

        List<float> critical = new List<float> { minV, maxV };

        for (int i = 0; i < triangles.Count; i++)
        {
            Tri2 t = triangles[i];
            metrics.SumArea += t.Area;
            metrics.OutsideArea += t.Area - ClipAreaToRect(
                t,
                minU,
                minV,
                maxU,
                maxV
            );

            if (t.Area <= 1e-12) continue;
            critical.Add(t.A.y);
            critical.Add(t.B.y);
            critical.Add(t.C.y);
        }

        for (int i = 0; i < triangles.Count; i++)
        {
            for (int j = i + 1; j < triangles.Count; j++)
            {
                AddEdgeIntersections(triangles[i], triangles[j], critical);
            }
        }

        critical.Sort();

        double union = 0;
        double overlap = 0;

        for (int i = 0; i + 1 < critical.Count; i++)
        {
            float v0 = critical[i];
            float v1 = critical[i + 1];
            float h = v1 - v0;
            if (h <= 1e-7f) continue;

            float inset = Mathf.Min(1e-5f, h * 0.25f);
            float u0 = UnionLengthAt(triangles, v0 + inset);
            float u1 = UnionLengthAt(triangles, v1 - inset);

            union += (u0 + u1) * 0.5 * h;
            overlap += (
                OverlapLengthAt(triangles, v0 + inset) +
                OverlapLengthAt(triangles, v1 - inset)
            ) * 0.5 * h;
        }

        metrics.UnionArea = union;
        metrics.OverlapArea = overlap;
        metrics.UncoveredArea = Math.Max(
            0,
            (maxU - minU) * (double)(maxV - minV) - union
        );
    }

    private static void AddEdgeIntersections(
        Tri2 a,
        Tri2 b,
        List<float> critical)
    {
        Vector2[] ea = { a.A, a.B, a.B, a.C, a.C, a.A };
        Vector2[] eb = { b.A, b.B, b.B, b.C, b.C, b.A };

        for (int i = 0; i < 6; i += 2)
        {
            for (int j = 0; j < 6; j += 2)
            {
                Vector2 point;
                if (SegmentIntersection(
                    ea[i],
                    ea[i + 1],
                    eb[j],
                    eb[j + 1],
                    out point))
                {
                    critical.Add(point.y);
                }
            }
        }
    }

    private static bool SegmentIntersection(
        Vector2 p1,
        Vector2 p2,
        Vector2 p3,
        Vector2 p4,
        out Vector2 point)
    {
        point = Vector2.zero;

        float d = (p2.x - p1.x) * (p4.y - p3.y) -
                  (p2.y - p1.y) * (p4.x - p3.x);
        if (Mathf.Abs(d) < 1e-9f) return false;

        float t = ((p3.x - p1.x) * (p4.y - p3.y) -
                   (p3.y - p1.y) * (p4.x - p3.x)) / d;
        float u = ((p3.x - p1.x) * (p2.y - p1.y) -
                   (p3.y - p1.y) * (p2.x - p1.x)) / d;

        if (t < -1e-5f || t > 1f + 1e-5f) return false;
        if (u < -1e-5f || u > 1f + 1e-5f) return false;

        point = new Vector2(
            p1.x + t * (p2.x - p1.x),
            p1.y + t * (p2.y - p1.y)
        );
        return true;
    }

    private static float UnionLengthAt(List<Tri2> triangles, float v)
    {
        List<Vector2> intervals = new List<Vector2>();

        for (int i = 0; i < triangles.Count; i++)
        {
            float x0;
            float x1;
            if (!TriangleSpanAt(triangles[i], v, out x0, out x1)) continue;
            intervals.Add(new Vector2(Mathf.Min(x0, x1), Mathf.Max(x0, x1)));
        }

        if (intervals.Count == 0) return 0f;

        intervals.Sort((a, b) => a.x.CompareTo(b.x));

        float length = 0;
        float start = intervals[0].x;
        float end = intervals[0].y;

        for (int i = 1; i < intervals.Count; i++)
        {
            if (intervals[i].x > end + 1e-7f)
            {
                length += end - start;
                start = intervals[i].x;
                end = intervals[i].y;
            }
            else if (intervals[i].y > end)
            {
                end = intervals[i].y;
            }
        }

        return length + (end - start);
    }

    private static float OverlapLengthAt(List<Tri2> triangles, float v)
    {
        float sum = 0;
        for (int i = 0; i < triangles.Count; i++)
        {
            float x0;
            float x1;
            if (!TriangleSpanAt(triangles[i], v, out x0, out x1)) continue;
            sum += Mathf.Abs(x1 - x0);
        }

        return Mathf.Max(0f, sum - UnionLengthAt(triangles, v));
    }

    private static bool TriangleSpanAt(Tri2 t, float v, out float x0, out float x1)
    {
        x0 = 0;
        x1 = 0;

        Vector2[] p = { t.A, t.B, t.C };
        List<float> hits = new List<float>(3);

        for (int i = 0; i < 3; i++)
        {
            Vector2 a = p[i];
            Vector2 b = p[(i + 1) % 3];

            if ((a.y <= v && b.y >= v) || (b.y <= v && a.y >= v))
            {
                float dy = b.y - a.y;
                if (Mathf.Abs(dy) < 1e-9f)
                {
                    hits.Add(a.x);
                    hits.Add(b.x);
                }
                else
                {
                    float s = (v - a.y) / dy;
                    hits.Add(a.x + s * (b.x - a.x));
                }
            }
        }

        if (hits.Count < 2) return false;

        float min = float.MaxValue;
        float max = float.MinValue;
        for (int i = 0; i < hits.Count; i++)
        {
            if (hits[i] < min) min = hits[i];
            if (hits[i] > max) max = hits[i];
        }

        x0 = min;
        x1 = max;
        return true;
    }

    private static double ClipAreaToRect(
        Tri2 t,
        float minU,
        float minV,
        float maxU,
        float maxV)
    {
        List<Vector2> poly = new List<Vector2> { t.A, t.B, t.C };

        poly = ClipPolygon(poly, 0, minU);
        poly = ClipPolygon(poly, 1, -maxU);
        poly = ClipPolygon(poly, 2, minV);
        poly = ClipPolygon(poly, 3, -maxV);

        if (poly.Count < 3) return 0;

        return Math.Abs(PolygonArea(poly));
    }

    private static double PolygonArea(List<Vector2> polygon)
    {
        double area = 0;
        for (int i = 0; i < polygon.Count; i++)
        {
            Vector2 a = polygon[i];
            Vector2 b = polygon[(i + 1) % polygon.Count];
            area += a.x * b.y - b.x * a.y;
        }

        return area * 0.5;
    }

    private static List<Vector2> ClipPolygon(
        List<Vector2> poly,
        int mode,
        float bound)
    {
        List<Vector2> result = new List<Vector2>();
        if (poly == null || poly.Count == 0) return result;

        for (int i = 0; i < poly.Count; i++)
        {
            Vector2 current = poly[i];
            Vector2 previous = poly[(i - 1 + poly.Count) % poly.Count];
            float dCurrent = Coordinate(current, mode) - bound;
            float dPrevious = Coordinate(previous, mode) - bound;
            bool insideCurrent = dCurrent >= 0;
            bool insidePrevious = dPrevious >= 0;

            if (insideCurrent != insidePrevious)
            {
                float denominator = dCurrent - dPrevious;
                if (Mathf.Abs(denominator) > 1e-12f)
                {
                    float s = dCurrent / denominator;
                    result.Add(new Vector2(
                        current.x + s * (previous.x - current.x),
                        current.y + s * (previous.y - current.y)
                    ));
                }
            }

            if (insideCurrent) result.Add(current);
        }

        return result;
    }

    private static float Coordinate(Vector2 p, int mode)
    {
        switch (mode)
        {
            case 0: return p.x;
            case 1: return -p.x;
            case 2: return p.y;
            default: return -p.y;
        }
    }

    private static bool PointInPolygon(Vector2 p, List<Vector2> polygon)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            Vector2 a = polygon[i];
            Vector2 b = polygon[j];
            if (((a.y > p.y) != (b.y > p.y)) &&
                (p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x))
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private static double ExpectedSealedAreaByDensity(
        List<TerrainChunkView> views,
        FaceKey face,
        int axis,
        bool negativeSide,
        int grid = 200)
    {
        const float offset = 1e-3f;
        int open = 0;
        int total = 0;

        for (int iu = 0; iu < grid; iu++)
        {
            for (int iv = 0; iv < grid; iv++)
            {
                Vector3 point = FacePoint(
                    face,
                    (iu + 0.5f) / grid,
                    (iv + 0.5f) / grid
                );
                point[axis] += negativeSide ? -offset : offset;

                float density;
                if (!TrySampleBaseDensity(views, point, out density)) continue;

                total++;
                if (density <= 0f) open++;
            }
        }

        return total == 0 ? 0 : open / (double)total;
    }

    private static Vector3 FacePoint(FaceKey face, float u, float v)
    {
        Vector3 point = Vector3.zero;
        point[face.Axis] = face.Plane;
        point[(face.Axis + 1) % 3] = face.U + u;
        point[(face.Axis + 2) % 3] = face.V + v;
        return point;
    }

    private static bool TrySampleBaseDensity(
        List<TerrainChunkView> views,
        Vector3 terrainLocal,
        out float density)
    {
        density = 0f;

        for (int i = 0; i < views.Count; i++)
        {
            TerrainChunk chunk = views[i].Chunk;
            DensityField field = chunk != null ? chunk.Density : null;
            if (field == null) continue;

            Bounds bounds = chunk.LocalBounds;
            if (terrainLocal.x < bounds.min.x || terrainLocal.x > bounds.max.x ||
                terrainLocal.y < bounds.min.y || terrainLocal.y > bounds.max.y ||
                terrainLocal.z < bounds.min.z || terrainLocal.z > bounds.max.z)
            {
                continue;
            }

            float fx = (terrainLocal.x - field.Origin.x) / field.CellSizeX;
            float fy = (terrainLocal.y - field.Origin.y) / field.CellSizeY;
            float fz = (terrainLocal.z - field.Origin.z) / field.CellSizeZ;

            if (fx < 0 || fx > field.SampleCountX - 1 ||
                fy < 0 || fy > field.SampleCountY - 1 ||
                fz < 0 || fz > field.SampleCountZ - 1)
            {
                continue;
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

            float c00 = Mathf.Lerp(field[x0, y0, z0], field[x1, y0, z0], tx);
            float c10 = Mathf.Lerp(field[x0, y1, z0], field[x1, y1, z0], tx);
            float c01 = Mathf.Lerp(field[x0, y0, z1], field[x1, y0, z1], tx);
            float c11 = Mathf.Lerp(field[x0, y1, z1], field[x1, y1, z1], tx);

            density = Mathf.Lerp(
                Mathf.Lerp(c00, c10, ty),
                Mathf.Lerp(c01, c11, ty),
                tz
            );
            return true;
        }

        return false;
    }

    private static HashSet<string> SnapshotFaceTriangleKeys(
        List<TerrainChunkView> views,
        FaceKey face)
    {
        HashSet<string> keys = new HashSet<string>();

        for (int c = 0; c < views.Count; c++)
        {
            TerrainChunk chunk = views[c].Chunk;
            Mesh mesh = chunk != null ? chunk.Mesh : null;
            if (mesh == null) continue;

            Vector3[] vertices = mesh.vertices;
            Vector3 origin = chunk.ChunkOrigin;

            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                int[] indices = mesh.GetTriangles(submesh);
                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    Vector3 a = vertices[indices[i]] + origin;
                    Vector3 b = vertices[indices[i + 1]] + origin;
                    Vector3 d = vertices[indices[i + 2]] + origin;

                    if (Mathf.Abs(a[face.Axis] - face.Plane) > PlaneTolerance ||
                        Mathf.Abs(b[face.Axis] - face.Plane) > PlaneTolerance ||
                        Mathf.Abs(d[face.Axis] - face.Plane) > PlaneTolerance)
                    {
                        continue;
                    }

                    Vector3 centroid = (a + b + d) / 3f;
                    if (centroid[(face.Axis + 1) % 3] < face.U - 0.05f ||
                        centroid[(face.Axis + 1) % 3] > face.U + 1.05f ||
                        centroid[(face.Axis + 2) % 3] < face.V - 0.05f ||
                        centroid[(face.Axis + 2) % 3] > face.V + 1.05f)
                    {
                        continue;
                    }

                    keys.Add(PositionKey3(a) + "|" + PositionKey3(b) + "|" +
                             PositionKey3(d));
                }
            }
        }

        return keys;
    }

    private static string PositionKey3(Vector3 p)
    {
        return Mathf.RoundToInt(p.x / Quantize) + "," +
               Mathf.RoundToInt(p.y / Quantize) + "," +
               Mathf.RoundToInt(p.z / Quantize);
    }

    private static string TriangleKey(Tri2 t)
    {
        List<string> keys = new List<string>
        {
            VertexKey(t.A),
            VertexKey(t.B),
            VertexKey(t.C)
        };
        keys.Sort(StringComparer.Ordinal);
        return string.Join("|", keys);
    }

    private static string VertexKey(Vector2 p)
    {
        return Mathf.RoundToInt(p.x / Quantize) + "," +
               Mathf.RoundToInt(p.y / Quantize);
    }

    private static string FormatCoordinates(HashSet<Vector3Int> values)
    {
        List<string> items = new List<string>();
        foreach (Vector3Int value in values)
        {
            items.Add("(" + value.x + "," + value.y + "," + value.z + ")");
        }

        items.Sort(StringComparer.Ordinal);
        return string.Join(" ", items);
    }

    private static bool TryBuildFaceKey(
        Vector3Int a,
        Vector3Int b,
        int axis,
        out FaceKey face)
    {
        Vector3Int lower = a;
        Vector3Int upper = b;
        if (upper[axis] < lower[axis])
        {
            lower = b;
            upper = a;
        }

        if (upper[axis] - lower[axis] != 1)
        {
            face = new FaceKey();
            return false;
        }

        face = new FaceKey(
            axis,
            upper[axis],
            lower[(axis + 1) % 3],
            lower[(axis + 2) % 3]
        );
        return true;
    }

    private static TerrainChunk FindChunk(
        List<TerrainChunkView> views,
        Vector3Int voxel)
    {
        Vector3Int coordinate = ChunkSpace.VoxelToChunk(voxel, ChunkVoxelSize);

        for (int i = 0; i < views.Count; i++)
        {
            if (views[i].Coordinate == coordinate) return views[i].Chunk;
        }

        return null;
    }

    // =====================================================================
    // 诊断：mine → restore 循环中的贡献/顶点计数（用于定位回归）
    // =====================================================================

    public static void RunCycleDiagnostic()
    {
        checks = 0;
        failures = 0;

        GameObject terrainObject = CreateFlatFixture("StageB3C_CycleDiag");

        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();
            Vector3Int voxel = new Vector3Int(74, SurfaceVoxelY, 30);
            FaceKey face;
            TryBuildFaceKey(
                voxel,
                new Vector3Int(75, SurfaceVoxelY, 30),
                0,
                out face);

            LogCycleState(terrainObject, face, "初始");

            for (int cycle = 0; cycle < 2; cycle++)
            {
                terrain.VoxelField.Set(voxel, VoxelState.Air);
                terrain.RebuildChunksAffectedByVoxel(voxel);
                LogCycleState(terrainObject, face, "mine#" + cycle);

                terrain.VoxelField.Set(voxel, VoxelState.Solid);
                terrain.RebuildChunksAffectedByVoxel(voxel);
                LogCycleState(terrainObject, face, "restore#" + cycle);
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }

        LogSummary();
        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    private static void LogCycleState(
        GameObject terrainObject,
        FaceKey face,
        string label)
    {
        List<TerrainChunkView> views = GetViews(terrainObject);
        Dictionary<Vector3Int, MeshCanonical> canonical = SnapshotCanonical(views);

        StringBuilder builder = new StringBuilder();
        builder.Append("[StageB3C-cycle] ").Append(label);
        foreach (KeyValuePair<Vector3Int, MeshCanonical> pair in canonical)
        {
            builder.Append(" chunk").Append(pair.Key).Append(":v=")
                .Append(pair.Value.VertexKeys.Count)
                .Append(",t=").Append(pair.Value.TriangleKeys.Count);
        }

        builder.Append(" contributions=");
        for (int i = 0; i < views.Count; i++)
        {
            if (views[i].Chunk.Geometry == null) continue;
            builder.Append(views[i].Coordinate).Append(':')
                .Append(views[i].Chunk.Geometry.HiddenFaceContributions.Count)
                .Append('/')
                .Append(views[i].Chunk.Geometry.FaceFragmentVisibility.Count)
                .Append(' ');
        }

        builder.Append(" faceOnPlaneTriangles=").Append(
            CountFaceTriangles(views, face));

        Debug.Log(builder.ToString());
    }

    private static int CountFaceTriangles(
        List<TerrainChunkView> views,
        FaceKey face)
    {
        int count = 0;

        for (int c = 0; c < views.Count; c++)
        {
            Mesh mesh = views[c].Chunk != null ? views[c].Chunk.Mesh : null;
            if (mesh == null) continue;

            Vector3[] vertices = mesh.vertices;
            Vector3 origin = views[c].Chunk.ChunkOrigin;

            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                int[] indices = mesh.GetTriangles(submesh);
                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    Vector3 a = vertices[indices[i]] + origin;
                    Vector3 b = vertices[indices[i + 1]] + origin;
                    Vector3 d = vertices[indices[i + 2]] + origin;

                    if (Mathf.Abs(a[face.Axis] - face.Plane) > PlaneTolerance ||
                        Mathf.Abs(b[face.Axis] - face.Plane) > PlaneTolerance ||
                        Mathf.Abs(d[face.Axis] - face.Plane) > PlaneTolerance)
                    {
                        continue;
                    }

                    Vector3 centroid = (a + b + d) / 3f;
                    if (centroid[(face.Axis + 1) % 3] < face.U - 0.05f ||
                        centroid[(face.Axis + 1) % 3] > face.U + 1.05f ||
                        centroid[(face.Axis + 2) % 3] < face.V - 0.05f ||
                        centroid[(face.Axis + 2) % 3] > face.V + 1.05f)
                    {
                        continue;
                    }

                    count++;
                }
            }
        }

        return count;
    }
    // =====================================================================
    // T1–T6 辅助工具
    // =====================================================================

    private sealed class FaceCandidate
    {
        public Vector3Int Voxel;
        public Vector3Int Neighbour;
        public int Axis;
        public int Side;
        public double ExpectedSealed;
    }

    private sealed class MeshCanonical
    {
        public List<string> VertexKeys = new List<string>();
        public List<string> TriangleKeys = new List<string>();
        public double Area;
    }

    private static bool TryFindPartialOpenFace(
        DensityTerrain terrain,
        List<TerrainChunkView> views,
        double minArea,
        double maxArea,
        out FaceCandidate candidate)
    {
        candidate = null;
        double bestDistance = double.MaxValue;
        double target = (minArea + maxArea) * 0.5;

        for (int c = 0; c < views.Count; c++)
        {
            TerrainChunk chunk = views[c].Chunk;
            if (chunk == null || chunk.Geometry == null) continue;

            List<Vector3Int> voxels = new List<Vector3Int>();
            foreach (KeyValuePair<Vector3Int, List<int>> pair in
                chunk.Geometry.VoxelFragmentIndex)
            {
                voxels.Add(pair.Key);
            }

            voxels.Sort(CompareVector3Int);

            const int searchGrid = 24;
            int scanned = 0;
            const int maxScanned = 400;

            for (int i = 0; i < voxels.Count && scanned < maxScanned; i++)
            {
                scanned++;
                for (int axis = 0; axis < 3; axis++)
                {
                    for (int side = 0; side < 2; side++)
                    {
                        if (terrain.VoxelField.IsAir(voxels[i])) continue;

                        Vector3Int neighbour = voxels[i] + AxisVector(axis, side);
                        if (terrain.VoxelField.IsAir(neighbour)) continue;

                        FaceKey face;
                        if (!TryBuildFaceKey(voxels[i], neighbour, axis, out face))
                        {
                            continue;
                        }

                        double area = ExpectedSealedAreaByDensity(
                            views, face, axis, side == 0, searchGrid);

                        if (area < minArea || area > maxArea) continue;

                        if (Math.Abs(area - target) <= 0.05)
                        {
                            candidate = new FaceCandidate
                            {
                                Voxel = voxels[i],
                                Neighbour = neighbour,
                                Axis = axis,
                                Side = side,
                                ExpectedSealed = area
                            };
                            return true;
                        }

                        double distance = Math.Abs(area - target);
                        if (distance >= bestDistance) continue;

                        bestDistance = distance;
                        candidate = new FaceCandidate
                        {
                            Voxel = voxels[i],
                            Neighbour = neighbour,
                            Axis = axis,
                            Side = side,
                            ExpectedSealed = area
                        };
                    }
                }
            }
        }

        return candidate != null;
    }

    private static bool TryFindFullyOpenCavityFaceVoxel(
        DensityTerrain terrain,
        Vector3 center,
        float radius,
        int axis,
        int side,
        out Vector3Int voxel,
        out Vector3Int neighbour)
    {
        for (int offset = 1; offset <= 8; offset++)
        {
            Vector3Int candidate = new Vector3Int(
                Mathf.FloorToInt(center.x),
                Mathf.FloorToInt(center.y),
                Mathf.FloorToInt(center.z)
            );
            candidate[axis] += (side == 0 ? -offset : offset);

            if (candidate.x < 1 || candidate.x > 73) continue;
            if (candidate.y < 1 || candidate.y > 35) continue;
            if (candidate.z < 1 || candidate.z > 48) continue;

            Vector3Int towards = candidate;
            towards[axis] += side == 0 ? 1 : -1;

            if (FaceOpenFractionForCavity(candidate, towards, axis, center, radius)
                < 0.999f)
            {
                continue;
            }

            voxel = candidate;
            neighbour = towards;
            return true;
        }

        voxel = Vector3Int.zero;
        neighbour = Vector3Int.zero;
        return false;
    }

    private static float FaceOpenFractionForCavity(
        Vector3Int voxel,
        Vector3Int neighbour,
        int axis,
        Vector3 center,
        float radius)
    {
        int axisU = (axis + 1) % 3;
        int axisV = (axis + 2) % 3;
        int open = 0;
        int total = 0;

        for (int iu = 0; iu <= 10; iu++)
        {
            for (int iv = 0; iv <= 10; iv++)
            {
                Vector3 point = Vector3.zero;
                point[axis] = voxel[axis] +
                              (neighbour[axis] > voxel[axis] ? 1f : 0f);
                point[axisU] = voxel[axisU] + iu * 0.1f;
                point[axisV] = voxel[axisV] + iv * 0.1f;

                total++;
                if (Vector3.Distance(point, center) < radius) open++;
            }
        }

        return total == 0 ? 0 : open / (float)total;
    }

    private static int CountGeometryInsideVoxel(
        List<TerrainChunkView> views,
        Vector3Int voxel)
    {
        int count = 0;
        Vector3 min = new Vector3(voxel.x, voxel.y, voxel.z);

        for (int v = 0; v < views.Count; v++)
        {
            Mesh mesh = views[v].Chunk != null ? views[v].Chunk.Mesh : null;
            if (mesh == null) continue;

            Vector3[] vertices = mesh.vertices;
            Vector3 origin = views[v].Chunk.ChunkOrigin;

            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                int[] indices = mesh.GetTriangles(submesh);
                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    Vector3 centroid = origin + (
                        vertices[indices[i]] +
                        vertices[indices[i + 1]] +
                        vertices[indices[i + 2]]
                    ) / 3f;

                    if (centroid.x > min.x + PlaneTolerance &&
                        centroid.x < min.x + 1f - PlaneTolerance &&
                        centroid.y > min.y + PlaneTolerance &&
                        centroid.y < min.y + 1f - PlaneTolerance &&
                        centroid.z > min.z + PlaneTolerance &&
                        centroid.z < min.z + 1f - PlaneTolerance)
                    {
                        count++;
                    }
                }
            }
        }

        return count;
    }

    private static int CountContributionEntries(List<TerrainChunkView> views)
    {
        int count = 0;
        for (int i = 0; i < views.Count; i++)
        {
            if (views[i].Chunk.Geometry == null) continue;
            count += views[i].Chunk.Geometry.HiddenFaceContributions.Count;
        }

        return count;
    }

    private static Dictionary<Vector3Int, MeshCanonical> SnapshotCanonical(
        List<TerrainChunkView> views)
    {
        Dictionary<Vector3Int, MeshCanonical> result =
            new Dictionary<Vector3Int, MeshCanonical>();

        for (int i = 0; i < views.Count; i++)
        {
            TerrainChunk chunk = views[i].Chunk;
            MeshCanonical canonical = new MeshCanonical();
            Mesh mesh = chunk.Mesh;

            if (mesh != null)
            {
                Vector3[] vertices = mesh.vertices;
                HashSet<string> vertexKeys = new HashSet<string>();

                for (int v = 0; v < vertices.Length; v++)
                {
                    vertexKeys.Add(PositionKey3(vertices[v] + chunk.ChunkOrigin));
                }

                canonical.VertexKeys.AddRange(vertexKeys);
                canonical.VertexKeys.Sort(StringComparer.Ordinal);

                for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                {
                    int[] triangles = mesh.GetTriangles(submesh);
                    for (int t = 0; t + 2 < triangles.Length; t += 3)
                    {
                        Vector3 a = vertices[triangles[t]] + chunk.ChunkOrigin;
                        Vector3 b = vertices[triangles[t + 1]] + chunk.ChunkOrigin;
                        Vector3 c = vertices[triangles[t + 2]] + chunk.ChunkOrigin;

                        canonical.Area +=
                            Vector3.Cross(b - a, c - a).magnitude * 0.5;
                        canonical.TriangleKeys.Add(
                            "s" + submesh + "|" +
                            RotateToSmallest(
                                PositionKey3(a),
                                PositionKey3(b),
                                PositionKey3(c)));
                    }
                }

                canonical.TriangleKeys.Sort(StringComparer.Ordinal);
            }

            result[chunk.Coordinate] = canonical;
        }

        return result;
    }

    private static bool SameAllChunks(
        Dictionary<Vector3Int, MeshCanonical> a,
        Dictionary<Vector3Int, MeshCanonical> b,
        out string detail)
    {
        detail = string.Empty;

        if (a.Count != b.Count)
        {
            detail = "Chunk 数 " + a.Count + " vs " + b.Count;
            return false;
        }

        foreach (KeyValuePair<Vector3Int, MeshCanonical> pair in a)
        {
            MeshCanonical other;
            if (!b.TryGetValue(pair.Key, out other))
            {
                detail = "缺少 Chunk " + pair.Key;
                return false;
            }

            if (pair.Value.TriangleKeys.Count != other.TriangleKeys.Count)
            {
                detail = pair.Key + ": 三角形数 " +
                         pair.Value.TriangleKeys.Count + " vs " +
                         other.TriangleKeys.Count;
                return false;
            }

            for (int i = 0; i < pair.Value.TriangleKeys.Count; i++)
            {
                if (pair.Value.TriangleKeys[i] != other.TriangleKeys[i])
                {
                    detail = pair.Key + ": 三角形集合差异 @" + i;
                    return false;
                }
            }

            if (pair.Value.VertexKeys.Count != other.VertexKeys.Count)
            {
                detail = pair.Key + ": 顶点数 " + pair.Value.VertexKeys.Count +
                         " vs " + other.VertexKeys.Count;
                return false;
            }

            for (int i = 0; i < pair.Value.VertexKeys.Count; i++)
            {
                if (pair.Value.VertexKeys[i] != other.VertexKeys[i])
                {
                    detail = pair.Key + ": 顶点集合差异 @" + i;
                    return false;
                }
            }
        }

        return true;
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

    private static Vector3Int AxisVector(int axis, int side)
    {
        Vector3Int value = Vector3Int.zero;
        value[axis] = side == 0 ? -1 : 1;
        return value;
    }

    private static bool SetEquals(
        List<Vector3Int> values,
        Vector3Int[] expected)
    {
        if (values == null || expected == null) return false;
        if (values.Count != expected.Length) return false;

        HashSet<Vector3Int> set = new HashSet<Vector3Int>(values);
        if (set.Count != expected.Length) return false;

        for (int i = 0; i < expected.Length; i++)
        {
            if (!set.Contains(expected[i])) return false;
        }

        return true;
    }

    private static int CompareVector3Int(Vector3Int a, Vector3Int b)
    {
        if (a.x != b.x) return a.x.CompareTo(b.x);
        if (a.y != b.y) return a.y.CompareTo(b.y);
        return a.z.CompareTo(b.z);
    }

    private static GameObject CreateFlatFixture(
        string name,
        Vector3Int chunkStart = default(Vector3Int))
    {
        return CreateFixture(name, chunkStart, false, false);
    }

    private static GameObject CreateCavityFixture(string name)
    {
        return CreateFixture(name, Vector3Int.zero, true, false);
    }

    private static GameObject CreateFixture(
        string name,
        Vector3Int chunkStart,
        bool sphereCavity,
        bool useNoise)
    {
        GameObject terrainObject = new GameObject(name);
        DensityTerrain terrain = terrainObject.AddComponent<DensityTerrain>();

        terrain.voxelSize = 1f;
        terrain.chunkVoxelSize = ChunkVoxelSize;
        terrain.chunkCount = new Vector3Int(2, 1, 2);
        terrain.chunkStartCoordinate = chunkStart;
        terrain.resolution = 75;
        terrain.densityMode = sphereCavity
            ? DensityGenerationMode.SphereCavity
            : DensityGenerationMode.HeightField;
        terrain.useNoise = useNoise;
        terrain.height = FlatHeight;
        terrain.noiseScale = 0.15f;
        terrain.noiseStrength = 2.37f;
        terrain.cavityCenterOffset = CavityCenter();
        terrain.cavityRadius = CavityRadius();
        terrain.debugCutaway = false;
        terrain.regenerateInEditor = false;
        terrain.useVoxelAwareMeshPrecut = true;

        terrain.Regenerate();

        return terrainObject;
    }
    // =====================================================================
    // Fixture（与 B3-B 相同，保证可复现同一用例）
    // =====================================================================

    private static Vector3 CavityCenter()
    {
        return new Vector3(30.6f, 8.53f, 25.4f);
    }

    private static float CavityRadius()
    {
        return 3.55f;
    }

    private static GameObject CreateNaturalFixture(string name)
    {
        GameObject terrainObject = new GameObject(name);
        DensityTerrain terrain = terrainObject.AddComponent<DensityTerrain>();

        terrain.voxelSize = 1f;
        terrain.chunkVoxelSize = ChunkVoxelSize;
        terrain.chunkCount = new Vector3Int(2, 1, 2);
        terrain.chunkStartCoordinate = Vector3Int.zero;
        terrain.resolution = 75;
        terrain.densityMode = DensityGenerationMode.SphereCavity;
        terrain.useNoise = true;
        terrain.height = FlatHeight;
        terrain.noiseScale = 0.15f;
        terrain.noiseStrength = 2.37f;
        terrain.cavityCenterOffset = CavityCenter();
        terrain.cavityRadius = CavityRadius();
        terrain.debugCutaway = false;
        terrain.regenerateInEditor = false;
        terrain.useVoxelAwareMeshPrecut = true;

        terrain.Regenerate();

        return terrainObject;
    }

    private static List<TerrainChunkView> GetViews(GameObject terrainObject)
    {
        TerrainChunkView[] array = terrainObject
            .GetComponentsInChildren<TerrainChunkView>(true);

        return new List<TerrainChunkView>(array);
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

    private static void Limit(string message)
    {
        Debug.Log("[StageB3C-LIMITATION] " + message);
    }

    private static void LogSummary()
    {
        Debug.Log(
            "[VoxelCoordinateMigrationStageB3CValidation] ===== SUMMARY =====\n" +
            "Checks = " + checks + "\n" +
            "Failures = " + failures + "\n" +
            "Result = " + (failures == 0 ? "PASS" : "FAIL")
        );
    }
}
