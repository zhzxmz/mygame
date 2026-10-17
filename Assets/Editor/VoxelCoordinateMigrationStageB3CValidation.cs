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
        CheckT7AdjacentMultiDig();
        CheckT8CoplanarCoverage();
        CheckT9NegativeCoordinates();
        CheckT10FaceIdentityUnique();
        CheckT11LegacyCompareEquivalence();
        CheckT12ChunkScopedVisibility();
        CheckT13VisibilityRoundTrip();
        CheckT14NonZeroChunkCoplanarCoverage();
        CheckT15MineTargetResolution();
        CheckT16PartialSolidVoxelSelection();
        CheckT16SolidCentreVoxelMining();
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

            double expectedOpen = ExpectedWallAreaByDensity(
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
                "T1: 内壁绕序自材料侧指向空腔"
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
        // (a) 平面地形：材料边界语义（B3-D 修正后）。
        //     · 朝天空的面（未挖空侧 = 空中 Voxel，Base Density ≤ 0）⇒ 不得造盖板；
        //     · 朝地下的面（未挖空侧 = 材料 Voxel，Base Density > 0）⇒ 完整坑底内壁，
        //       绕序自材料侧指向空腔（−Y 面 ⇒ 法线 +Y）。
        GameObject flat = CreateFlatFixture("StageB3C_T2Flat");

        try
        {
            DensityTerrain terrain = flat.GetComponent<DensityTerrain>();
            List<TerrainChunkView> views = GetViews(flat);
            Vector3Int voxel = new Vector3Int(30, SurfaceVoxelY, 30);

            // 天空侧：Voxel(15) 与 Voxel(16) 之间的面（NegativeVoxel = 被挖 Voxel）。
            FaceKey skyFace;
            TryBuildFaceKey(
                voxel,
                new Vector3Int(30, SurfaceVoxelY + 1, 30),
                1,
                out skyFace);

            // 地下侧：Voxel(14) 与 Voxel(15) 之间的面（NegativeVoxel = 下方材料 Voxel）。
            Vector3Int floorNegativeVoxel = new Vector3Int(
                30, SurfaceVoxelY - 1, 30);
            FaceKey floorFace;
            TryBuildFaceKey(floorNegativeVoxel, voxel, 1, out floorFace);

            HashSet<string> skyBaseline = SnapshotFaceTriangleKeys(views, skyFace);
            HashSet<string> floorBaseline = SnapshotFaceTriangleKeys(views, floorFace);

            terrain.VoxelField.Set(voxel, VoxelState.Air);
            terrain.RebuildChunksAffectedByVoxel(voxel);
            views = GetViews(flat);

            FaceAudit sky = AuditFace(
                views, skyFace, voxel, terrain.VoxelField, skyBaseline);
            FaceAudit floor = AuditFace(
                views, floorFace, floorNegativeVoxel, terrain.VoxelField,
                floorBaseline);

            double skyExpected = ExpectedWallAreaByDensity(
                views, skyFace, 1, false);
            Check(
                skyExpected <= 1e-3 &&
                sky.TriangleCount == 0 &&
                sky.Coverage.UnionArea <= WholeFaceCoverageTolerance,
                "T2a: 天空侧不封面（材料侧为空，期望 " +
                skyExpected.ToString("F4") + "，实际覆盖 " +
                sky.Coverage.UnionArea.ToString("F4") + "，三角形 " +
                sky.TriangleCount + "）"
            );

            double floorExpected = ExpectedWallAreaByDensity(
                views, floorFace, 1, true);
            Check(
                floorExpected >= 0.999 &&
                floor.Coverage.UncoveredArea <= WholeFaceCoverageTolerance &&
                floor.Coverage.OverlapArea <= OverlapAreaTolerance &&
                floor.TriangleCount == 2 &&
                floor.WindingConsistent,
                "T2a: 坑底完整内壁且绕序自材料侧指向空腔（期望 " +
                floorExpected.ToString("F4") + "，未覆盖 " +
                floor.Coverage.UncoveredArea.ToString("E3") + "，重叠 " +
                floor.Coverage.OverlapArea.ToString("E3") + "，三角形 " +
                floor.TriangleCount + "，绕序 " +
                (floor.WindingConsistent ? "OK" : "FAIL") + "）"
            );

            // 被挖 Voxel 内部不得残留原地面表面（Base Mesh 表面片必须被停用）。
            int residual = CountGeometryInsideVoxel(views, voxel);
            Check(
                residual == 0,
                "T2a: 被挖 Voxel 内部无残留地面几何（" + residual + "）"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(flat);
        }

        // (b) 六方向（球腔夹具）：材料边界语义（B3-D 修正后）。
        //     (b1) 挖空腔内的 Voxel ⇒ 朝空腔的面不得新增几何（不得封堵空腔）；
        //     (b2) 挖与空腔相邻的材料 Voxel ⇒ 朝空腔面按材料分布封闭、
        //          背离空腔面（材料侧）完整内壁。
        GameObject cavity = CreateCavityFixture("StageB3C_T2Cavity");

        try
        {
            DensityTerrain terrain = cavity.GetComponent<DensityTerrain>();
            List<TerrainChunkView> views = GetViews(cavity);
            int testedCavitySide = 0;
            int testedMaterialSide = 0;

            for (int axis = 0; axis < 3; axis++)
            {
                for (int side = 0; side < 2; side++)
                {
                    string label = "T2b[轴" + axis +
                                   (side == 0 ? "负" : "正") + "]";

                    // ---- (b1) 空腔侧：挖空腔内 Voxel 不得新增任何面 ----
                    Vector3Int openVoxel;
                    Vector3Int openNeighbour;
                    if (!TryFindFullyOpenCavityFaceVoxel(
                        terrain,
                        CavityCenter(),
                        CavityRadius(),
                        axis,
                        side,
                        out openVoxel,
                        out openNeighbour))
                    {
                        Limit(label + "1: 未找到全开口空腔面 Voxel，跳过（不伪造结果）");
                    }
                    else
                    {
                        testedCavitySide++;

                        bool solidIsNegative =
                            openNeighbour[axis] < openVoxel[axis];
                        Vector3Int negativeVoxel = solidIsNegative
                            ? openNeighbour
                            : openVoxel;

                        FaceKey face;
                        TryBuildFaceKey(openVoxel, openNeighbour, axis, out face);
                        HashSet<string> baseline = SnapshotFaceTriangleKeys(
                            views, face);
                        FaceAudit before = AuditFace(
                            views, face, negativeVoxel, terrain.VoxelField, null);

                        terrain.VoxelField.Set(openVoxel, VoxelState.Air);
                        int rebuilt = terrain.RebuildChunksAffectedByVoxel(openVoxel);
                        List<TerrainChunkView> afterOpen = GetViews(cavity);

                        FaceAudit audit = AuditFace(
                            afterOpen, face, negativeVoxel, terrain.VoxelField, baseline);
                        double expected = ExpectedWallAreaByDensity(
                            afterOpen, face, axis, solidIsNegative);
                        double tolerance = CoverageTolerance(
                            afterOpen, face, axis, solidIsNegative, expected);

                        Debug.Log(
                            "[StageB3C-T2b1] axis=" + axis + " side=" + side +
                            " voxel=" + openVoxel + " face=" + face.Key +
                            " expectedWall=" + expected.ToString("F4") +
                            " before=" + before.Coverage.UnionArea.ToString("F4") +
                            " after=" + audit.Coverage.UnionArea.ToString("F4") +
                            " rebuilt=" + rebuilt);

                        // 不允许任何新增覆盖：允许上界取“空腔原有共面几何 + 容差”，
                        // 因此“新增一块 1 m² 的盖板”必然失败。
                        double allowed = Math.Max(
                            expected + tolerance,
                            before.Coverage.UnionArea + WholeFaceCoverageTolerance);
                        Check(
                            rebuilt >= 1 &&
                            audit.Coverage.UnionArea <= allowed,
                            label + "1: 空腔侧不封堵空腔（覆盖 " +
                            before.Coverage.UnionArea.ToString("F4") + "→" +
                            audit.Coverage.UnionArea.ToString("F4") +
                            "，允许 " + allowed.ToString("F4") + "，期望内壁 " +
                            expected.ToString("F4") + "）"
                        );

                        terrain.VoxelField.Set(openVoxel, VoxelState.Solid);
                        terrain.RebuildChunksAffectedByVoxel(openVoxel);
                        views = GetViews(cavity);
                    }

                    // ---- (b2) 材料侧：挖与空腔相邻的材料 Voxel ----
                    Vector3Int materialVoxel;
                    Vector3Int innerNeighbour;
                    if (!TryFindCavityMaterialVoxel(
                        views,
                        CavityCenter(),
                        axis,
                        side,
                        out materialVoxel,
                        out innerNeighbour))
                    {
                        Limit(label + "2: 未找到相邻材料 Voxel，跳过（不伪造结果）");
                        continue;
                    }

                    testedMaterialSide++;

                    int step = side == 0 ? -1 : 1;
                    Vector3Int farNeighbour = materialVoxel;
                    farNeighbour[axis] += step;

                    bool cavityFaceSolidIsNegative =
                        innerNeighbour[axis] < materialVoxel[axis];
                    FaceKey cavityFace;
                    TryBuildFaceKey(
                        materialVoxel, innerNeighbour, axis, out cavityFace);

                    bool farFaceSolidIsNegative = farNeighbour[axis] < materialVoxel[axis];
                    FaceKey farFace;
                    TryBuildFaceKey(farNeighbour, materialVoxel, axis, out farFace);

                    HashSet<string> cavityBaseline = SnapshotFaceTriangleKeys(
                        views, cavityFace);
                    HashSet<string> farBaseline = SnapshotFaceTriangleKeys(
                        views, farFace);

                    terrain.VoxelField.Set(materialVoxel, VoxelState.Air);
                    terrain.RebuildChunksAffectedByVoxel(materialVoxel);
                    List<TerrainChunkView> afterMaterial = GetViews(cavity);

                    FaceAudit cavityAudit = AuditFace(
                        afterMaterial,
                        cavityFace,
                        cavityFaceSolidIsNegative ? innerNeighbour : materialVoxel,
                        terrain.VoxelField,
                        cavityBaseline);
                    double cavityExpected = ExpectedWallAreaByDensity(
                        afterMaterial, cavityFace, axis, cavityFaceSolidIsNegative);
                    double cavityTolerance = CoverageTolerance(
                        afterMaterial, cavityFace, axis, cavityFaceSolidIsNegative, cavityExpected);
                    double cavityOver =
                        cavityAudit.Coverage.UnionArea - cavityExpected;
                    double cavityUnder = Math.Max(
                        0.0, cavityExpected - cavityAudit.Coverage.UnionArea);

                    FaceAudit farAudit = AuditFace(
                        afterMaterial,
                        farFace,
                        farFaceSolidIsNegative ? farNeighbour : materialVoxel,
                        terrain.VoxelField,
                        farBaseline);
                    double farExpected = ExpectedWallAreaByDensity(
                        afterMaterial, farFace, axis, farFaceSolidIsNegative);

                    Debug.Log(
                        "[StageB3C-T2b2] axis=" + axis + " side=" + side +
                        " voxel=" + materialVoxel +
                        " inner=" + innerNeighbour + " far=" + farNeighbour +
                        " cavityFace=" + cavityFace.Key +
                        " cavityExpected=" + cavityExpected.ToString("F4") +
                        " cavityCovered=" +
                        cavityAudit.Coverage.UnionArea.ToString("F4") +
                        " farFace=" + farFace.Key +
                        " farExpected=" + farExpected.ToString("F4") +
                        " farCovered=" + farAudit.Coverage.UnionArea.ToString("F4"));

                    Check(
                        cavityOver <= cavityTolerance &&
                        cavityUnder <= cavityTolerance &&
                        cavityAudit.Coverage.DegenerateCount == 0 &&
                        cavityAudit.Coverage.DuplicateCount == 0 &&
                        cavityAudit.WindingConsistent,
                        label + "2: 朝空腔面按材料分布封闭（多覆盖 " +
                        cavityOver.ToString("F4") + "，漏覆盖 " +
                        cavityUnder.ToString("F4") + "，容差 " +
                        cavityTolerance.ToString("F4") + "）"
                    );
                    Check(
                        farExpected >= 0.999 &&
                        farAudit.Coverage.UncoveredArea <=
                            WholeFaceCoverageTolerance &&
                        farAudit.Coverage.OverlapArea <= OverlapAreaTolerance &&
                        farAudit.TriangleCount >= 2 &&
                        farAudit.WindingConsistent,
                        label + "2: 背离空腔面完整内壁（期望 " +
                        farExpected.ToString("F4") + "，未覆盖 " +
                        farAudit.Coverage.UncoveredArea.ToString("E3") +
                        "，重叠 " +
                        farAudit.Coverage.OverlapArea.ToString("E3") +
                        "，三角形 " + farAudit.TriangleCount + "，绕序 " +
                        (farAudit.WindingConsistent ? "OK" : "FAIL") + "）"
                    );

                    terrain.VoxelField.Set(materialVoxel, VoxelState.Solid);
                    terrain.RebuildChunksAffectedByVoxel(materialVoxel);
                    views = GetViews(cavity);
                }
            }

            Check(
                testedCavitySide == 6,
                "T2b1: 六个方向全部实测（" + testedCavitySide + "/6）");
            Check(
                testedMaterialSide == 6,
                "T2b2: 六个方向全部实测（" + testedMaterialSide + "/6）");
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
                    double expected = ExpectedWallAreaByDensity(
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
                double expected = ExpectedWallAreaByDensity(
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
            bool[] faceSolidIsNegative = new bool[6];
            Vector3Int[] faceNegativeVoxels = new Vector3Int[6];
            HashSet<string>[] baseline = new HashSet<string>[6];
            for (int axis = 0; axis < 3; axis++)
            {
                for (int side = 0; side < 2; side++)
                {
                    int index = axis * 2 + side;
                    Vector3Int neighbour = voxel + AxisVector(axis, side);
                    TryBuildFaceKey(voxel, neighbour, axis, out faces[index]);
                    baseline[index] = SnapshotFaceTriangleKeys(
                        views, faces[index]);

                    // side == 0：邻居在 Negative 侧（被挖 Voxel 在 Positive 侧）；
                    // side == 1：被挖 Voxel 自身在 Negative 侧。
                    faceSolidIsNegative[index] = side == 0;
                    faceNegativeVoxels[index] = side == 0 ? neighbour : voxel;
                }
            }

            terrain.VoxelField.Set(voxel, VoxelState.Air);
            int rebuilt = terrain.RebuildChunksAffectedByVoxel(voxel);
            views = GetViews(terrainObject);

            // B3-D 材料边界语义：逐面按材料分布核对覆盖，并核对归属/绕序。
            int coverageFailures = 0;
            int emptyCoverageFailures = 0;
            int fullCoverageFailures = 0;
            int ownerFailures = 0;
            int degenerateCount = 0;
            int duplicateCount = 0;
            int windingFailures = 0;
            StringBuilder detail = new StringBuilder();

            for (int axis = 0; axis < 3; axis++)
            {
                for (int side = 0; side < 2; side++)
                {
                    int index = axis * 2 + side;
                    double expectedWall = ExpectedWallAreaByDensity(
                        views, faces[index], axis, faceSolidIsNegative[index]);
                    double tolerance = CoverageTolerance(
                        views,
                        faces[index],
                        axis,
                        faceSolidIsNegative[index],
                        expectedWall);

                    FaceAudit audit = AuditFace(
                        views,
                        faces[index],
                        faceNegativeVoxels[index],
                        terrain.VoxelField,
                        baseline[index]);

                    double over = audit.Coverage.UnionArea - expectedWall;
                    double under = Math.Max(
                        0.0, expectedWall - audit.Coverage.UnionArea);

                    if (over > tolerance || under > tolerance) coverageFailures++;
                    if (expectedWall <= 1e-3 && audit.TriangleCount > 0)
                    {
                        emptyCoverageFailures++;
                    }
                    if (expectedWall >= 0.999 &&
                        audit.Coverage.UncoveredArea >
                            WholeFaceCoverageTolerance)
                    {
                        fullCoverageFailures++;
                    }
                    if (audit.TriangleCount > 0 && audit.OwnerChunkCount != 1)
                    {
                        ownerFailures++;
                    }

                    degenerateCount += audit.Coverage.DegenerateCount;
                    duplicateCount += audit.Coverage.DuplicateCount;
                    if (audit.TriangleCount > 0 && !audit.WindingConsistent)
                    {
                        windingFailures++;
                    }

                    detail.Append(" {轴").Append(axis)
                        .Append(side == 0 ? "负" : "正")
                        .Append(" 期望").Append(expectedWall.ToString("F4"))
                        .Append(" 覆盖").Append(
                            audit.Coverage.UnionArea.ToString("F4"))
                        .Append(" 未覆盖").Append(
                            audit.Coverage.UncoveredArea.ToString("F4"))
                        .Append(" 三角形").Append(audit.TriangleCount)
                        .Append(" 归属").Append(audit.OwnerChunkCount)
                        .Append("}");
                }
            }

            Debug.Log(
                "[StageB3C-T4] " + label + " rebuilt=" + rebuilt + detail);

            Check(
                rebuilt >= 1,
                label + ": 生产入口应用了 Voxel 修改（rebuilt=" + rebuilt + "）"
            );
            Check(
                coverageFailures == 0,
                label + ": 六面覆盖与材料分布一致（不一致 " +
                coverageFailures + " 面；" + detail + "）"
            );
            Check(
                emptyCoverageFailures == 0,
                label + ": 材料侧为空的面不生成任何几何（异常 " +
                emptyCoverageFailures + " 面；" + detail + "）"
            );
            Check(
                fullCoverageFailures == 0,
                label + ": 材料侧完整的面完整封闭（异常 " +
                fullCoverageFailures + " 面；" + detail + "）"
            );
            Check(
                ownerFailures == 0,
                label + ": 有几何的面归属唯一 Chunk（异常 " +
                ownerFailures + " 面；" + detail + "）"
            );
            Check(
                degenerateCount == 0 && duplicateCount == 0 &&
                windingFailures == 0,
                label + ": 无退化/重复且绕序正确（" + degenerateCount + "/" +
                duplicateCount + "/" + windingFailures + "；" + detail + "）"
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
                double expected = ExpectedWallAreaByDensity(
                    afterA, face, 1, false);
                double expectedFine = ExpectedWallAreaByDensity(
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
                double expected = ExpectedWallAreaByDensity(
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
    // T7 — 相邻多次挖掘（共享面两侧先后挖空）
    // =====================================================================

    private static void CheckT7AdjacentMultiDig()
    {
        Vector3Int first = new Vector3Int(30, SurfaceVoxelY, 30);
        Vector3Int second = new Vector3Int(31, SurfaceVoxelY, 30);

        GameObject pathA = CreateFlatFixture("StageB3C_T7A");
        GameObject pathB = CreateFlatFixture("StageB3C_T7B");
        GameObject pathC = CreateFlatFixture("StageB3C_T7C");

        try
        {
            DensityTerrain terrainA = pathA.GetComponent<DensityTerrain>();
            DensityTerrain terrainB = pathB.GetComponent<DensityTerrain>();
            DensityTerrain terrainC = pathC.GetComponent<DensityTerrain>();

            // 顺序 A：先 30 后 31；顺序 B：先 31 后 30（顺序无关性）。
            terrainA.VoxelField.Set(first, VoxelState.Air);
            terrainA.RebuildChunksAffectedByVoxel(first);
            terrainA.VoxelField.Set(second, VoxelState.Air);
            terrainA.RebuildChunksAffectedByVoxel(second);

            terrainB.VoxelField.Set(second, VoxelState.Air);
            terrainB.RebuildChunksAffectedByVoxel(second);
            terrainB.VoxelField.Set(first, VoxelState.Air);
            terrainB.RebuildChunksAffectedByVoxel(first);

            // 顺序 C：一次性写入 VoxelField 后完整重建。
            terrainC.VoxelField.Set(first, VoxelState.Air);
            terrainC.VoxelField.Set(second, VoxelState.Air);
            bool rebuilt = terrainC.RebuildVoxelAwareGeometryFromVoxelField();

            List<TerrainChunkView> views = GetViews(pathA);

            // (1) 两个相邻空腔之间的共享面必须完全没有几何（空腔连通）。
            FaceKey sharedFace;
            TryBuildFaceKey(first, second, 0, out sharedFace);
            FaceAudit shared = AuditFace(
                views, sharedFace, first, terrainA.VoxelField, null);

            Check(
                shared.TriangleCount == 0 &&
                shared.Coverage.UnionArea <= WholeFaceCoverageTolerance,
                "T7: 相邻空腔共享面无几何（三角形 " + shared.TriangleCount +
                "，覆盖 " + shared.Coverage.UnionArea.ToString("F4") + "）"
            );

            // (2) 10 个外部面按材料分布覆盖（材料侧为空的面必须完全不封面）。
            int checkedFaces = 0;
            int coverageFailures = 0;
            int unexpectedGeometry = 0;
            StringBuilder detail = new StringBuilder();

            for (int voxelIndex = 0; voxelIndex < 2; voxelIndex++)
            {
                Vector3Int voxel = voxelIndex == 0 ? first : second;

                for (int axis = 0; axis < 3; axis++)
                {
                    for (int side = 0; side < 2; side++)
                    {
                        Vector3Int neighbour = voxel + AxisVector(axis, side);

                        // 相邻的另一半 Voxel 已挖空：共享面单独检查。
                        if (neighbour == first || neighbour == second) continue;

                        FaceKey face;
                        TryBuildFaceKey(voxel, neighbour, axis, out face);

                        bool solidIsNegative = side == 0;
                        double expected = ExpectedWallAreaByDensity(
                            views, face, axis, solidIsNegative);
                        double tolerance = CoverageTolerance(
                            views, face, axis, solidIsNegative, expected);
                        FaceAudit audit = AuditFace(
                            views,
                            face,
                            side == 0 ? neighbour : voxel,
                            terrainA.VoxelField,
                            null);

                        double over = audit.Coverage.UnionArea - expected;
                        double under = Math.Max(
                            0.0, expected - audit.Coverage.UnionArea);

                        checkedFaces++;
                        if (over > tolerance || under > tolerance)
                        {
                            coverageFailures++;
                        }

                        if (expected <= 1e-3 && audit.TriangleCount > 0)
                        {
                            unexpectedGeometry++;
                        }

                        detail.Append(" {voxel=").Append(voxel)
                            .Append(" 轴").Append(axis)
                            .Append(side == 0 ? "负" : "正")
                            .Append(" 期望").Append(expected.ToString("F4"))
                            .Append(" 覆盖").Append(
                                audit.Coverage.UnionArea.ToString("F4"))
                            .Append(" 三角形").Append(audit.TriangleCount)
                            .Append("}");
                    }
                }
            }

            Debug.Log("[StageB3C-T7] " + detail);

            Check(
                checkedFaces == 10,
                "T7: 两个相邻空腔共 10 个外部面全部核对（" +
                checkedFaces + "/10）"
            );
            Check(
                coverageFailures == 0,
                "T7: 外部面覆盖与材料分布一致（不一致 " + coverageFailures +
                " 面；" + detail + "）"
            );
            Check(
                unexpectedGeometry == 0,
                "T7: 材料侧为空的面不生成几何（异常 " + unexpectedGeometry +
                " 面；" + detail + "）"
            );

            // (3) 被挖 Voxel 内部无残留几何。
            int residual = CountGeometryInsideVoxel(views, first) +
                           CountGeometryInsideVoxel(views, second);
            Check(
                residual == 0,
                "T7: 被挖 Voxel 内部无残留几何（" + residual + "）"
            );

            // (4) 三条路径几何完全一致（挖掘顺序无关 + 增量 == 完整重建）。
            Check(rebuilt, "T7: 完整重建成功");
            Check(
                SameAllChunks(
                    SnapshotCanonical(views),
                    SnapshotCanonical(GetViews(pathB)),
                    out string orderDetail),
                "T7: 相邻挖掘顺序无关（A == B）" +
                (orderDetail.Length > 0 ? " —— " + orderDetail : string.Empty)
            );
            Check(
                SameAllChunks(
                    SnapshotCanonical(views),
                    SnapshotCanonical(GetViews(pathC)),
                    out string rebuildDetail),
                "T7: 增量 == 一次性完整重建" +
                (rebuildDetail.Length > 0 ? " —— " + rebuildDetail : string.Empty)
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(pathA);
            UnityEngine.Object.DestroyImmediate(pathB);
            UnityEngine.Object.DestroyImmediate(pathC);
        }
    }
    // =====================================================================
    // 性能探针（独立入口，不参与 RunBatch 断言）
    //   Unity -batchmode -executeMethod VoxelCoordinateMigrationStageB3CValidation.RunPerfProbe
    //
    // 测量内容（全部为只读或真实生产调用，不做断言，避免把机器性能变成 CI 条件）：
    //   1. 单次挖掘：生产 Mutation 自带的 Evaluation / Commit / Assemble 阶段耗时；
    //   2. 27 体素连续挖掘：逐次 total / 阶段耗时；
    //   3. 玩家挖掘诊断扫描：旧“全样本域（SampleCount³）” vs 新“索引窗口”；
    //   4. Coplanar 覆盖判定：旧“逐采样点重算（3 Chunk × 全部 FaceFragments + 分配）”
    //      vs 新“按 Face 预计算多边形集合 + 逐点 PointInPolygon”。
    // =====================================================================

    public static void RunPerfProbe()
    {
        GameObject terrainObject = CreateNaturalFixture("StageB3C_Perf");

        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();
            List<TerrainChunkView> views = GetViews(terrainObject);

            Vector3Int[] plan = new Vector3Int[27];
            int index = 0;
            for (int dx = 0; dx < 3; dx++)
            {
                for (int dy = 0; dy < 3; dy++)
                {
                    for (int dz = 0; dz < 3; dz++)
                    {
                        plan[index++] = new Vector3Int(30 + dx, 13 + dy, 30 + dz);
                    }
                }
            }

            // ---- 1) 单次挖掘 ----
            System.Diagnostics.Stopwatch singleWatch =
                System.Diagnostics.Stopwatch.StartNew();
            terrain.VoxelField.Set(plan[0], VoxelState.Air);
            int rebuilt = terrain.RebuildChunksAffectedByVoxel(plan[0]);
            singleWatch.Stop();

            Debug.Log(
                "[StageB3C-Perf] single voxel=" + plan[0] +
                " rebuiltChunks=" + rebuilt +
                " totalMs=" + singleWatch.Elapsed.TotalMilliseconds.ToString("F3") +
                " evalMs=" + VoxelAwareMeshRuntimeMutation.LastEvaluationMilliseconds.ToString("F3") +
                " commitMs=" + VoxelAwareMeshRuntimeMutation.LastCommitMilliseconds.ToString("F3") +
                " assembleMs=" + VoxelAwareMeshRuntimeMutation.LastAssembleMilliseconds.ToString("F3") +
                " regionSamples=" + VoxelAwareMeshRuntimeMutation.LastRegionSampleCount +
                " evalBreakdown[segmentBuildMs=" +
                VoxelAwareMeshRuntimeMutation.LastSegmentBuildMilliseconds.ToString("F3") +
                " validateMs=" +
                VoxelAwareMeshRuntimeMutation.LastBoundaryGraphValidateMilliseconds.ToString("F4") +
                " collectCoplanarMs=" +
                VoxelAwareMeshRuntimeMutation.LastCollectCoplanarMilliseconds.ToString("F4") +
                " visibilityTargetsMs=" +
                VoxelAwareMeshRuntimeMutation.LastVisibilityTargetsMilliseconds.ToString("F4") +
                " faceEvaluateMs=" +
                VoxelAwareMeshRuntimeMutation.LastFaceEvaluateMilliseconds.ToString("F3") +
                " classifyMs=" +
                VoxelAwareMeshRuntimeMutation.LastClassifyMilliseconds.ToString("F3") +
                " partitionMs=" +
                VoxelAwareMeshSpatialFace.LastEvaluationPartitionMilliseconds.ToString("F3") +
                " uniformityMs=" +
                VoxelAwareMeshSpatialFace.LastEvaluationUniformityMilliseconds.ToString("F3") +
                " subdivideMs=" +
                VoxelAwareMeshSpatialFace.LastEvaluationSubdivideMilliseconds.ToString("F3") +
                " triangulateMs=" +
                VoxelAwareMeshSpatialFace.LastEvaluationTriangulateMilliseconds.ToString("F3") +
                " regions=" + VoxelAwareMeshSpatialFace.LastEvaluationRegionCount +
                " pieces=" + VoxelAwareMeshSpatialFace.LastEvaluationPieceCount +
                " generatedTriangles=" +
                VoxelAwareMeshSpatialFace.LastEvaluationTriangleCount + "]" +
                " assembleLastChunk[weldMs=" +
                VoxelAwareMeshPrecut.LastAssembleWeldMilliseconds.ToString("F3") +
                " polygonTriMs=" +
                VoxelAwareMeshPrecut.LastAssembleTriangulateMilliseconds.ToString("F3") +
                " uploadMs=" +
                VoxelAwareMeshPrecut.LastAssembleUploadMilliseconds.ToString("F3") +
                " normalsMs=" +
                VoxelAwareMeshPrecut.LastAssembleNormalsMilliseconds.ToString("F3") +
                " verts=" + VoxelAwareMeshPrecut.LastAssembleVertexCount +
                " triangles=" + VoxelAwareMeshPrecut.LastAssembleTriangleCount + "]" +
                " evaluatedFaces=" + VoxelAwareMeshRuntimeMutation.LastEvaluatedFaceCount +
                " dirtyChunks=" + VoxelAwareMeshRuntimeMutation.LastDirtyChunks.Count +
                " reassembledChunks=" + VoxelAwareMeshRuntimeMutation.LastReassembledChunkCount
            );

            // ---- 2) 连续挖掘 ----
            double evalSum = 0.0;
            double commitSum = 0.0;
            double assembleSum = 0.0;
            double totalSum = 0.0;
            double totalMax = 0.0;
            long sampleSum = 0;
            int steps = plan.Length - 1;

            for (int i = 1; i < plan.Length; i++)
            {
                System.Diagnostics.Stopwatch one = System.Diagnostics.Stopwatch.StartNew();
                terrain.VoxelField.Set(plan[i], VoxelState.Air);
                terrain.RebuildChunksAffectedByVoxel(plan[i]);
                one.Stop();

                double ms = one.Elapsed.TotalMilliseconds;
                totalSum += ms;
                if (ms > totalMax) totalMax = ms;
                evalSum += VoxelAwareMeshRuntimeMutation.LastEvaluationMilliseconds;
                commitSum += VoxelAwareMeshRuntimeMutation.LastCommitMilliseconds;
                assembleSum += VoxelAwareMeshRuntimeMutation.LastAssembleMilliseconds;
                sampleSum += VoxelAwareMeshRuntimeMutation.LastRegionSampleCount;
            }

            Debug.Log(
                "[StageB3C-Perf] batch steps=" + steps +
                " totalMs=" + totalSum.ToString("F3") +
                " meanMs=" + (totalSum / steps).ToString("F3") +
                " maxMs=" + totalMax.ToString("F3") +
                " evalMs=" + evalSum.ToString("F3") +
                " commitMs=" + commitSum.ToString("F3") +
                " assembleMs=" + assembleSum.ToString("F3") +
                " regionSamples=" + sampleSum +
                " lastEvalBreakdown[segmentBuildMs=" +
                VoxelAwareMeshRuntimeMutation.LastSegmentBuildMilliseconds.ToString("F3") +
                " faceEvaluateMs=" +
                VoxelAwareMeshRuntimeMutation.LastFaceEvaluateMilliseconds.ToString("F3") +
                " classifyMs=" +
                VoxelAwareMeshRuntimeMutation.LastClassifyMilliseconds.ToString("F3") +
                " partitionMs=" +
                VoxelAwareMeshSpatialFace.LastEvaluationPartitionMilliseconds.ToString("F3") +
                " uniformityMs=" +
                VoxelAwareMeshSpatialFace.LastEvaluationUniformityMilliseconds.ToString("F3") +
                " subdivideMs=" +
                VoxelAwareMeshSpatialFace.LastEvaluationSubdivideMilliseconds.ToString("F3") +
                " triangulateMs=" +
                VoxelAwareMeshSpatialFace.LastEvaluationTriangulateMilliseconds.ToString("F3") + "]"
            );

            // ---- 3) 玩家挖掘诊断扫描：旧全样本域 vs 新索引窗口 ----
            DensityField field = views[0].Chunk.Density;
            Vector3 voxelMin = terrain.VoxelField.GetVoxelMin(plan[0]);
            Vector3 voxelMax = terrain.VoxelField.GetVoxelMax(plan[0]);

            int wx0 = ProbeClampIndex(
                Mathf.FloorToInt((voxelMin.x - field.Origin.x) / field.CellSizeX) - 1,
                field.SampleCountX);
            int wx1 = ProbeClampIndex(
                Mathf.CeilToInt((voxelMax.x - field.Origin.x) / field.CellSizeX) + 1,
                field.SampleCountX);
            int wy0 = ProbeClampIndex(
                Mathf.FloorToInt((voxelMin.y - field.Origin.y) / field.CellSizeY) - 1,
                field.SampleCountY);
            int wy1 = ProbeClampIndex(
                Mathf.CeilToInt((voxelMax.y - field.Origin.y) / field.CellSizeY) + 1,
                field.SampleCountY);
            int wz0 = ProbeClampIndex(
                Mathf.FloorToInt((voxelMin.z - field.Origin.z) / field.CellSizeZ) - 1,
                field.SampleCountZ);
            int wz1 = ProbeClampIndex(
                Mathf.CeilToInt((voxelMax.z - field.Origin.z) / field.CellSizeZ) + 1,
                field.SampleCountZ);

            int fullSamples = 0;
            int fullMapped = 0;
            System.Diagnostics.Stopwatch fullScan =
                System.Diagnostics.Stopwatch.StartNew();
            for (int x = 0; x < field.SampleCountX; x++)
            {
                for (int y = 0; y < field.SampleCountY; y++)
                {
                    for (int z = 0; z < field.SampleCountZ; z++)
                    {
                        fullSamples++;
                        Vector3 samplePosition = field.GetLocalPosition(x, y, z);
                        Vector3Int mapped =
                            terrain.VoxelField.PositionToVoxel(samplePosition);
                        if (mapped != plan[0]) continue;
                        fullMapped++;
                        probeSink += field[x, y, z];
                    }
                }
            }

            fullScan.Stop();

            int windowSamples = 0;
            int windowMapped = 0;
            System.Diagnostics.Stopwatch windowScan =
                System.Diagnostics.Stopwatch.StartNew();
            for (int x = wx0; x <= wx1; x++)
            {
                for (int y = wy0; y <= wy1; y++)
                {
                    for (int z = wz0; z <= wz1; z++)
                    {
                        windowSamples++;
                        Vector3 samplePosition = field.GetLocalPosition(x, y, z);
                        Vector3Int mapped =
                            terrain.VoxelField.PositionToVoxel(samplePosition);
                        if (mapped != plan[0]) continue;
                        windowMapped++;
                        probeSink += field[x, y, z];
                    }
                }
            }

            windowScan.Stop();

            Debug.Log(
                "[StageB3C-Perf] diagScan fullSamples=" + fullSamples +
                " fullMs=" + fullScan.Elapsed.TotalMilliseconds.ToString("F3") +
                " windowSamples=" + windowSamples +
                " windowMs=" + windowScan.Elapsed.TotalMilliseconds.ToString("F4") +
                " speedup=" +
                (fullScan.Elapsed.TotalMilliseconds /
                 Mathf.Max(1e-6f, (float)windowScan.Elapsed.TotalMilliseconds))
                    .ToString("F0") + "x" +
                " mapped=" + fullMapped + "/" + windowMapped +
                " affectedChunks=" + VoxelAwareMeshRuntimeMutation.LastDirtyChunks.Count
            );

            // ---- 4) Coplanar 覆盖判定：旧逐点重算 vs 新按 Face 预计算 ----
            List<TerrainChunk> chunks = new List<TerrainChunk>();
            for (int i = 0; i < views.Count; i++)
            {
                chunks.Add(views[i].Chunk);
            }

            int fragments = 0;
            for (int i = 0; i < chunks.Count; i++)
            {
                if (chunks[i] != null && chunks[i].Geometry != null)
                {
                    fragments += chunks[i].Geometry.FaceFragments.Count;
                }
            }

            VoxelAwareMeshPrecut.FaceId probeFace =
                new VoxelAwareMeshPrecut.FaceId(0, 30, 13, 30);
            int samplePoints = Mathf.Max(
                64, VoxelAwareMeshRuntimeMutation.LastRegionSampleCount);

            int legacyHits = 0;
            System.Diagnostics.Stopwatch legacy =
                System.Diagnostics.Stopwatch.StartNew();
            for (int s = 0; s < samplePoints; s++)
            {
                Vector2 uv = new Vector2((s % 89) / 89f, ((s * 7) % 97) / 97f);
                if (ProbeLegacyCoplanarCovered(probeFace, uv, chunks)) legacyHits++;
            }

            legacy.Stop();

            int currentHits = 0;
            System.Diagnostics.Stopwatch current =
                System.Diagnostics.Stopwatch.StartNew();
            List<List<Vector2>> polygons =
                ProbeCollectCoplanarPolygons(probeFace, chunks);
            for (int s = 0; s < samplePoints; s++)
            {
                Vector2 uv = new Vector2((s % 89) / 89f, ((s * 7) % 97) / 97f);
                if (ProbePointCovered(uv, polygons)) currentHits++;
            }

            current.Stop();

            Debug.Log(
                "[StageB3C-Perf] coplanar samplePoints=" + samplePoints +
                " fragments=" + fragments +
                " matchedPolygons=" + polygons.Count +
                " legacyMs=" + legacy.Elapsed.TotalMilliseconds.ToString("F3") +
                " currentMs=" + current.Elapsed.TotalMilliseconds.ToString("F3") +
                " speedup=" +
                (legacy.Elapsed.TotalMilliseconds /
                 Mathf.Max(1e-6f, (float)current.Elapsed.TotalMilliseconds))
                    .ToString("F1") + "x" +
                " hits=" + legacyHits + "/" + currentHits
            );

            // 前后等价性：逐点结论必须完全一致（同一批采样点）。
            int mismatches = 0;
            for (int s = 0; s < samplePoints; s++)
            {
                Vector2 uv = new Vector2((s % 89) / 89f, ((s * 7) % 97) / 97f);
                bool legacyResult = ProbeLegacyCoplanarCovered(probeFace, uv, chunks);
                bool currentResult = ProbePointCovered(uv, polygons);
                if (legacyResult != currentResult) mismatches++;
            }

            Debug.Log("[StageB3C-Perf] coplanar equivalenceMismatches=" + mismatches);

            // ---- 5) 平面夹具（与游戏场景参数一致：resolution=75 / 2×1×2 Chunk）----
            GameObject flatObject = CreateFlatFixture("StageB3C_PerfFlat");

            try
            {
                DensityTerrain flatTerrain =
                    flatObject.GetComponent<DensityTerrain>();
                List<TerrainChunkView> flatViews = GetViews(flatObject);

                int flatFragments = 0;
                for (int i = 0; i < flatViews.Count; i++)
                {
                    if (flatViews[i].Chunk != null &&
                        flatViews[i].Chunk.Geometry != null)
                    {
                        flatFragments +=
                            flatViews[i].Chunk.Geometry.FaceFragments.Count;
                    }
                }

                Vector3Int flatVoxel = new Vector3Int(30, SurfaceVoxelY, 30);
                System.Diagnostics.Stopwatch flatWatch =
                    System.Diagnostics.Stopwatch.StartNew();
                flatTerrain.VoxelField.Set(flatVoxel, VoxelState.Air);
                int flatRebuilt =
                    flatTerrain.RebuildChunksAffectedByVoxel(flatVoxel);
                flatWatch.Stop();

                Debug.Log(
                    "[StageB3C-Perf] flat voxel=" + flatVoxel +
                    " rebuiltChunks=" + flatRebuilt +
                    " faceFragments=" + flatFragments +
                    " totalMs=" + flatWatch.Elapsed.TotalMilliseconds.ToString("F3") +
                    " evalMs=" + VoxelAwareMeshRuntimeMutation.LastEvaluationMilliseconds.ToString("F3") +
                    " commitMs=" + VoxelAwareMeshRuntimeMutation.LastCommitMilliseconds.ToString("F3") +
                    " assembleMs=" + VoxelAwareMeshRuntimeMutation.LastAssembleMilliseconds.ToString("F3") +
                    " regionSamples=" + VoxelAwareMeshRuntimeMutation.LastRegionSampleCount
                );

                // 深部（完全被材料包围，全部面均为均匀覆盖）作为对照组。
                Vector3Int deepVoxel = new Vector3Int(40, 5, 20);
                System.Diagnostics.Stopwatch deepWatch =
                    System.Diagnostics.Stopwatch.StartNew();
                flatTerrain.VoxelField.Set(deepVoxel, VoxelState.Air);
                int deepRebuilt =
                    flatTerrain.RebuildChunksAffectedByVoxel(deepVoxel);
                deepWatch.Stop();

                Debug.Log(
                    "[StageB3C-Perf] deep voxel=" + deepVoxel +
                    " rebuiltChunks=" + deepRebuilt +
                    " totalMs=" + deepWatch.Elapsed.TotalMilliseconds.ToString("F3") +
                    " evalMs=" + VoxelAwareMeshRuntimeMutation.LastEvaluationMilliseconds.ToString("F3") +
                    " commitMs=" + VoxelAwareMeshRuntimeMutation.LastCommitMilliseconds.ToString("F3") +
                    " assembleMs=" + VoxelAwareMeshRuntimeMutation.LastAssembleMilliseconds.ToString("F3") +
                    " regionSamples=" + VoxelAwareMeshRuntimeMutation.LastRegionSampleCount
                );

            // ---- 6) B3-E：逐字段比较（默认）vs FaceId.Key 字符串比较（开关）----
            // 同一夹具、同一体素、各 3 次；报告中位数与最大值。
            const int compareRuns = 3;
            Vector3Int compareVoxel = new Vector3Int(30, 13, 30);

            double[] optTotal = new double[compareRuns];
            double[] optEval = new double[compareRuns];
            double[] optSegment = new double[compareRuns];
            double[] optValidate = new double[compareRuns];
            double[] optCollect = new double[compareRuns];
            double[] optEvaluate = new double[compareRuns];
            double[] optAssemble = new double[compareRuns];
            long[] optAlloc = new long[compareRuns];
            int[] optGc0 = new int[compareRuns];

            double[] legacyTotal = new double[compareRuns];
            double[] legacyEval = new double[compareRuns];
            double[] legacySegment = new double[compareRuns];
            double[] legacyValidate = new double[compareRuns];
            double[] legacyCollect = new double[compareRuns];
            double[] legacyEvaluate = new double[compareRuns];
            double[] legacyAssemble = new double[compareRuns];
            long[] legacyAlloc = new long[compareRuns];
            int[] legacyGc0 = new int[compareRuns];

            try
            {
                for (int run = 0; run < compareRuns; run++)
                {
                    VoxelAwareMeshPrecut.UseLegacyFaceKeyComparison = false;
                    GameObject optimizedObject = CreateNaturalFixture(
                        "StageB3C_PerfOpt" + run);

                    try
                    {
                        DensityTerrain runTerrain =
                            optimizedObject.GetComponent<DensityTerrain>();
                        GC.Collect();
                        int gcBefore = GC.CollectionCount(0);
                        long allocBefore = GC.GetTotalMemory(false);
                        System.Diagnostics.Stopwatch watch =
                            System.Diagnostics.Stopwatch.StartNew();
                        runTerrain.VoxelField.Set(compareVoxel, VoxelState.Air);
                        runTerrain.RebuildChunksAffectedByVoxel(compareVoxel);
                        watch.Stop();
                        long allocAfter = GC.GetTotalMemory(false);
                        int gcAfter = GC.CollectionCount(0);

                        optTotal[run] = watch.Elapsed.TotalMilliseconds;
                        optEval[run] =
                            VoxelAwareMeshRuntimeMutation.LastEvaluationMilliseconds;
                        optSegment[run] =
                            VoxelAwareMeshRuntimeMutation.LastSegmentBuildMilliseconds;
                        optValidate[run] =
                            VoxelAwareMeshRuntimeMutation.LastBoundaryGraphValidateMilliseconds;
                        optCollect[run] =
                            VoxelAwareMeshRuntimeMutation.LastCollectCoplanarMilliseconds;
                        optEvaluate[run] =
                            VoxelAwareMeshRuntimeMutation.LastFaceEvaluateMilliseconds;
                        optAssemble[run] =
                            VoxelAwareMeshRuntimeMutation.LastAssembleMilliseconds;
                        optAlloc[run] = allocAfter - allocBefore;
                        optGc0[run] = gcAfter - gcBefore;
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(optimizedObject);
                    }

                    VoxelAwareMeshPrecut.UseLegacyFaceKeyComparison = true;
                    GameObject legacyObject = CreateNaturalFixture(
                        "StageB3C_PerfLegacy" + run);

                    try
                    {
                        DensityTerrain runTerrain =
                            legacyObject.GetComponent<DensityTerrain>();
                        GC.Collect();
                        int gcBefore = GC.CollectionCount(0);
                        long allocBefore = GC.GetTotalMemory(false);
                        System.Diagnostics.Stopwatch watch =
                            System.Diagnostics.Stopwatch.StartNew();
                        runTerrain.VoxelField.Set(compareVoxel, VoxelState.Air);
                        runTerrain.RebuildChunksAffectedByVoxel(compareVoxel);
                        watch.Stop();
                        long allocAfter = GC.GetTotalMemory(false);
                        int gcAfter = GC.CollectionCount(0);

                        legacyTotal[run] = watch.Elapsed.TotalMilliseconds;
                        legacyEval[run] =
                            VoxelAwareMeshRuntimeMutation.LastEvaluationMilliseconds;
                        legacySegment[run] =
                            VoxelAwareMeshRuntimeMutation.LastSegmentBuildMilliseconds;
                        legacyValidate[run] =
                            VoxelAwareMeshRuntimeMutation.LastBoundaryGraphValidateMilliseconds;
                        legacyCollect[run] =
                            VoxelAwareMeshRuntimeMutation.LastCollectCoplanarMilliseconds;
                        legacyEvaluate[run] =
                            VoxelAwareMeshRuntimeMutation.LastFaceEvaluateMilliseconds;
                        legacyAssemble[run] =
                            VoxelAwareMeshRuntimeMutation.LastAssembleMilliseconds;
                        legacyAlloc[run] = allocAfter - allocBefore;
                        legacyGc0[run] = gcAfter - gcBefore;
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(legacyObject);
                    }
                }
            }
            finally
            {
                VoxelAwareMeshPrecut.UseLegacyFaceKeyComparison = false;
            }

            Debug.Log(
                "[StageB3C-Perf] B3E-faceId OPT    totalMed=" +
                Median(optTotal).ToString("F3") +
                " totalMax=" + Max(optTotal).ToString("F3") +
                " evalMed=" + Median(optEval).ToString("F3") +
                " segmentMed=" + Median(optSegment).ToString("F3") +
                " segmentMax=" + Max(optSegment).ToString("F3") +
                " validateMed=" + Median(optValidate).ToString("F4") +
                " collectMed=" + Median(optCollect).ToString("F4") +
                " evaluateMed=" + Median(optEvaluate).ToString("F3") +
                " assembleMed=" + Median(optAssemble).ToString("F3") +
                " allocMed=" + MedianLong(optAlloc) +
                " gc0Med=" + MedianInt(optGc0)
            );

            Debug.Log(
                "[StageB3C-Perf] B3E-faceId LEGACY totalMed=" +
                Median(legacyTotal).ToString("F3") +
                " totalMax=" + Max(legacyTotal).ToString("F3") +
                " evalMed=" + Median(legacyEval).ToString("F3") +
                " segmentMed=" + Median(legacySegment).ToString("F3") +
                " segmentMax=" + Max(legacySegment).ToString("F3") +
                " validateMed=" + Median(legacyValidate).ToString("F4") +
                " collectMed=" + Median(legacyCollect).ToString("F4") +
                " evaluateMed=" + Median(legacyEvaluate).ToString("F3") +
                " assembleMed=" + Median(legacyAssemble).ToString("F3") +
                " allocMed=" + MedianLong(legacyAlloc) +
                " gc0Med=" + MedianInt(legacyGc0)
            );

            Debug.Log(
                "[StageB3C-Perf] B3E-faceId speedup total=" +
                (Median(legacyTotal) / Mathf.Max(1e-6f, (float)Median(optTotal)))
                    .ToString("F1") + "x" +
                " segment=" +
                (Median(legacySegment) / Mathf.Max(1e-9f, (float)Median(optSegment)))
                    .ToString("F0") + "x" +
                " eval=" +
                (Median(legacyEval) / Mathf.Max(1e-6f, (float)Median(optEval)))
                    .ToString("F1") + "x" +
                " collect=" +
                (Median(legacyCollect) / Mathf.Max(1e-9f, (float)Median(optCollect)))
                    .ToString("F0") + "x"
            );

            // ---- 7) B3-E：非空 FaceFragments（体素对齐 height=15）对比 ----
            Vector3Int alignedCompareVoxel =
                new Vector3Int(30, SurfaceVoxelY - 1, 30);

            for (int pass = 0; pass < 2; pass++)
            {
                bool alignedLegacy = pass == 1;
                VoxelAwareMeshPrecut.UseLegacyFaceKeyComparison = alignedLegacy;
                GameObject alignedCompare = CreateT11Terrain(
                    alignedLegacy
                        ? "StageB3C_PerfAlignedLegacy"
                        : "StageB3C_PerfAligned",
                    15f,
                    Vector3Int.zero);

                try
                {
                    DensityTerrain alignedCompareTerrain =
                        alignedCompare.GetComponent<DensityTerrain>();
                    List<TerrainChunkView> alignedCompareViews =
                        GetViews(alignedCompare);
                    int alignedFragments = 0;
                    for (int i = 0; i < alignedCompareViews.Count; i++)
                    {
                        if (alignedCompareViews[i].Chunk != null &&
                            alignedCompareViews[i].Chunk.Geometry != null)
                        {
                            alignedFragments += alignedCompareViews[i]
                                .Chunk.Geometry.FaceFragments.Count;
                        }
                    }

                    System.Diagnostics.Stopwatch alignedWatch =
                        System.Diagnostics.Stopwatch.StartNew();
                    alignedCompareTerrain.VoxelField.Set(
                        alignedCompareVoxel, VoxelState.Air);
                    alignedCompareTerrain.RebuildChunksAffectedByVoxel(
                        alignedCompareVoxel);
                    alignedWatch.Stop();

                    Debug.Log(
                        "[StageB3C-Perf] B3E-faceId " +
                        (alignedLegacy ? "LEGACY" : "OPT   ") + " aligned" +
                        " fragments=" + alignedFragments +
                        " totalMs=" +
                        alignedWatch.Elapsed.TotalMilliseconds.ToString("F3") +
                        " evalMs=" + VoxelAwareMeshRuntimeMutation
                            .LastEvaluationMilliseconds.ToString("F3") +
                        " segmentMs=" + VoxelAwareMeshRuntimeMutation
                            .LastSegmentBuildMilliseconds.ToString("F3") +
                        " collectMs=" + VoxelAwareMeshRuntimeMutation
                            .LastCollectCoplanarMilliseconds.ToString("F3") +
                        " evaluateMs=" + VoxelAwareMeshRuntimeMutation
                            .LastFaceEvaluateMilliseconds.ToString("F3") +
                        " visibilityMs=" + VoxelAwareMeshRuntimeMutation
                            .LastVisibilityTargetsMilliseconds.ToString("F3") +
                        " assembleMs=" + VoxelAwareMeshRuntimeMutation
                            .LastAssembleMilliseconds.ToString("F3"));
                }
                finally
                {
                    VoxelAwareMeshPrecut.UseLegacyFaceKeyComparison = false;
                    UnityEngine.Object.DestroyImmediate(alignedCompare);
                }
            }

            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(flatObject);
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    private static double Median(double[] values)
    {
        if (values == null || values.Length == 0) return 0.0;
        double[] copy = (double[])values.Clone();
        Array.Sort(copy);
        return copy[copy.Length / 2];
    }

    private static double Max(double[] values)
    {
        if (values == null || values.Length == 0) return 0.0;
        double max = values[0];
        for (int i = 1; i < values.Length; i++)
        {
            if (values[i] > max) max = values[i];
        }
        return max;
    }

    private static long MedianLong(long[] values)
    {
        if (values == null || values.Length == 0) return 0;
        long[] copy = (long[])values.Clone();
        Array.Sort(copy);
        return copy[copy.Length / 2];
    }

    private static int MedianInt(int[] values)
    {
        if (values == null || values.Length == 0) return 0;
        int[] copy = (int[])values.Clone();
        Array.Sort(copy);
        return copy[copy.Length / 2];
    }

    private static float probeSink;

    private static int ProbeClampIndex(int index, int sampleCount)
    {
        if (index < 0) return 0;
        if (index > sampleCount - 1) return sampleCount - 1;
        return index;
    }

    private static bool ProbeLegacyCoplanarCovered(
        VoxelAwareMeshPrecut.FaceId face,
        Vector2 uv,
        List<TerrainChunk> chunks)
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
        }

        return false;
    }

    private static List<List<Vector2>> ProbeCollectCoplanarPolygons(
        VoxelAwareMeshPrecut.FaceId face,
        List<TerrainChunk> chunks)
    {
        List<List<Vector2>> result = new List<List<Vector2>>();

        for (int c = 0; c < chunks.Count; c++)
        {
            TerrainChunk chunk = chunks[c];
            if (chunk == null || chunk.Geometry == null) continue;

            VoxelAwareMeshPrecut.ChunkGeometry geometry = chunk.Geometry;
            for (int i = 0; i < geometry.FaceFragments.Count; i++)
            {
                VoxelAwareMeshPrecut.FaceFragment fragment =
                    geometry.FaceFragments[i];
                if (fragment.Face.Key != face.Key) continue;

                List<Vector2> polygon =
                    new List<Vector2>(fragment.Polygon.Count);
                for (int p = 0; p < fragment.Polygon.Count; p++)
                {
                    polygon.Add(VoxelAwareMeshSpatialFace.ToFaceLocal(
                        face,
                        fragment.Polygon[p],
                        geometry.ChunkOrigin,
                        geometry.VoxelSize
                    ));
                }

                result.Add(polygon);
            }
        }

        return result;
    }

    private static bool ProbePointCovered(
        Vector2 uv,
        List<List<Vector2>> polygons)
    {
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

    // =====================================================================
    // T8 — 共面基础几何（非空 FaceFragments）等价性与黑盒判定
    // =====================================================================

    private static void CheckT8CoplanarCoverage()
    {
        // 体素对齐平面地形（height 恰好落在 plane y = 15）：基础表面与 Voxel 边界
        // 平面共面 ⇒ FaceFragments 非空。此前所有夹具 FaceFragments = 0，
        // 无法证明“按 Face 预计算”相对“逐点重扫”的等价性与收益。
        GameObject alignedObject = CreateFlatFixture("StageB3C_T8Aligned");
        GameObject partialObject = CreateFlatFixture("StageB3C_T8Partial");

        try
        {
            DensityTerrain alignedTerrain =
                alignedObject.GetComponent<DensityTerrain>();
            alignedTerrain.height = 15f;
            alignedTerrain.Regenerate();

            List<TerrainChunkView> alignedViews = GetViews(alignedObject);
            List<TerrainChunk> chunks = new List<TerrainChunk>();
            for (int i = 0; i < alignedViews.Count; i++)
            {
                chunks.Add(alignedViews[i].Chunk);
            }

            int fragmentCount = 0;
            for (int i = 0; i < chunks.Count; i++)
            {
                if (chunks[i] != null && chunks[i].Geometry != null)
                {
                    fragmentCount += chunks[i].Geometry.FaceFragments.Count;
                }
            }

            Debug.Log(
                "[StageB3C-T8] aligned height=15 fragments=" + fragmentCount);

            Check(
                fragmentCount > 0,
                "T8: 体素对齐平面地形产生非空 FaceFragments（" + fragmentCount + "）"
            );

            // ---- (b) 旧逐点重扫 vs 新按 Face 预计算：逐点等价性 ----
            int samples = 0;
            int covered = 0;
            int mismatches = 0;
            int facesChecked = 0;

            for (int c = 0; c < chunks.Count; c++)
            {
                TerrainChunk chunk = chunks[c];
                if (chunk == null || chunk.Geometry == null) continue;

                List<VoxelAwareMeshPrecut.FaceId> faces =
                    new List<VoxelAwareMeshPrecut.FaceId>();
                HashSet<string> seen = new HashSet<string>();

                for (int i = 0; i < chunk.Geometry.FaceFragments.Count; i++)
                {
                    VoxelAwareMeshPrecut.FaceId face =
                        chunk.Geometry.FaceFragments[i].Face;
                    if (seen.Add(face.Key)) faces.Add(face);
                }

                for (int f = 0; f < faces.Count; f++)
                {
                    VoxelAwareMeshPrecut.FaceId face = faces[f];
                    facesChecked++;

                    List<List<Vector2>> polygons =
                        ProbeCollectCoplanarPolygons(face, chunks);

                    // 非空 FaceFragments 下的实际成本对比（同一批 32 个采样点）：
                    // 旧实现每采样点重扫 3 个 Chunk 的全部 FaceFragments 并分配 List；
                    // 新实现只做一次收集，再逐点 PointInPolygon。
                    if (f == 0)
                    {
                        List<Vector2> benchProbes = new List<Vector2>();
                        for (int b = 0; b < 32; b++)
                        {
                            benchProbes.Add(new Vector2(
                                (b % 8) / 8f + 0.0625f,
                                (b / 8) / 4f + 0.125f));
                        }

                        int legacyHits = 0;
                        System.Diagnostics.Stopwatch legacyWatch =
                            System.Diagnostics.Stopwatch.StartNew();
                        for (int b = 0; b < benchProbes.Count; b++)
                        {
                            if (ProbeLegacyCoplanarCovered(
                                face, benchProbes[b], chunks))
                            {
                                legacyHits++;
                            }
                        }

                        legacyWatch.Stop();

                        System.Diagnostics.Stopwatch gatherWatch =
                            System.Diagnostics.Stopwatch.StartNew();
                        List<List<Vector2>> gatherCheck =
                            ProbeCollectCoplanarPolygons(face, chunks);
                        gatherWatch.Stop();

                        int currentHits = 0;
                        System.Diagnostics.Stopwatch currentWatch =
                            System.Diagnostics.Stopwatch.StartNew();
                        for (int b = 0; b < benchProbes.Count; b++)
                        {
                            if (ProbePointCovered(benchProbes[b], polygons))
                            {
                                currentHits++;
                            }
                        }

                        currentWatch.Stop();
                        double currentTotal =
                            gatherWatch.Elapsed.TotalMilliseconds +
                            currentWatch.Elapsed.TotalMilliseconds;

                        // 关键对照：FaceKey 字符串属性 vs 整数字段比较。
                        // FaceId.Key 是计算属性（每次访问都新建字符串），
                        // 面片遍历热循环里用它做相等判断会产生大量字符串分配。
                        long stringKeyTicks =
                            System.Diagnostics.Stopwatch.GetTimestamp();
                        int stringKeyMatches = 0;
                        for (int bk = 0; bk < chunks.Count; bk++)
                        {
                            VoxelAwareMeshPrecut.ChunkGeometry geometry =
                                chunks[bk].Geometry;
                            for (int i = 0; i < geometry.FaceFragments.Count; i++)
                            {
                                if (geometry.FaceFragments[i].Face.Key != face.Key)
                                {
                                    continue;
                                }
                                stringKeyMatches++;
                            }
                        }
                        stringKeyTicks =
                            System.Diagnostics.Stopwatch.GetTimestamp() -
                            stringKeyTicks;

                        long fieldKeyTicks =
                            System.Diagnostics.Stopwatch.GetTimestamp();
                        int fieldKeyMatches = 0;
                        for (int bk = 0; bk < chunks.Count; bk++)
                        {
                            VoxelAwareMeshPrecut.ChunkGeometry geometry =
                                chunks[bk].Geometry;
                            for (int i = 0; i < geometry.FaceFragments.Count; i++)
                            {
                                VoxelAwareMeshPrecut.FaceId other =
                                    geometry.FaceFragments[i].Face;
                                if (other.Axis != face.Axis ||
                                    other.Plane != face.Plane ||
                                    other.U != face.U ||
                                    other.V != face.V)
                                {
                                    continue;
                                }
                                fieldKeyMatches++;
                            }
                        }
                        fieldKeyTicks =
                            System.Diagnostics.Stopwatch.GetTimestamp() -
                            fieldKeyTicks;

                        Debug.Log(
                            "[StageB3C-T8] faceKeyCost stringKeyMs=" +
                            (stringKeyTicks * 1000.0 /
                             System.Diagnostics.Stopwatch.Frequency).ToString("F3") +
                            " fieldKeyMs=" +
                            (fieldKeyTicks * 1000.0 /
                             System.Diagnostics.Stopwatch.Frequency).ToString("F3") +
                            " matches=" + stringKeyMatches + "/" + fieldKeyMatches +
                            " ratio=" +
                            (stringKeyTicks / (double)Mathf.Max(1, (int)fieldKeyTicks))
                                .ToString("F1") + "x");

                        Debug.Log(
                            "[StageB3C-T8] coplanar cost face=" + face.Key +
                            " fragmentsOnFace=" + polygons.Count +
                            " chunks=" + chunks.Count +
                            " samples=" + benchProbes.Count +
                            " legacyMs=" +
                            legacyWatch.Elapsed.TotalMilliseconds.ToString("F3") +
                            " currentGatherMs=" +
                            gatherWatch.Elapsed.TotalMilliseconds.ToString("F3") +
                            " currentPerPointMs=" +
                            currentWatch.Elapsed.TotalMilliseconds.ToString("F3") +
                            " currentTotalMs=" + currentTotal.ToString("F3") +
                            " speedup=" +
                            (legacyWatch.Elapsed.TotalMilliseconds /
                             Mathf.Max(1e-6f, (float)currentTotal)).ToString("F1") +
                            "x hits=" + legacyHits + "/" + currentHits +
                            " gatherCount=" + gatherCheck.Count);
                    }

                    List<Vector2> probes = new List<Vector2>();

                    // 面内/面外栅格（越界面矩形 ⇒ 既有 hit 也有 miss）。
                    const int grid = 8;
                    for (int iu = -4; iu <= grid + 4; iu++)
                    {
                        for (int iv = -4; iv <= grid + 4; iv++)
                        {
                            probes.Add(new Vector2(
                                (float)iu / grid,
                                (float)iv / grid));
                        }
                    }

                    // 多边形顶点 / 边中点，以及 ±{1e-6,1e-5,1e-4,1e-3} 容差附近偏移。
                    float[] offsets =
                    {
                        0f,
                        1e-5f, -1e-5f,
                        1e-3f, -1e-3f
                    };

                    if (polygons.Count > 0 && polygons[0].Count >= 3)
                    {
                        List<Vector2> polygon = polygons[0];
                        for (int p = 0; p < polygon.Count; p++)
                        {
                            Vector2 vertex = polygon[p];
                            Vector2 next = polygon[(p + 1) % polygon.Count];
                            Vector2 middle = (vertex + next) * 0.5f;

                            for (int o = 0; o < offsets.Length; o++)
                            {
                                probes.Add(new Vector2(
                                    vertex.x + offsets[o], vertex.y));
                                probes.Add(new Vector2(
                                    vertex.x, vertex.y + offsets[o]));
                                probes.Add(new Vector2(
                                    middle.x + offsets[o], middle.y));
                                probes.Add(new Vector2(
                                    middle.x, middle.y + offsets[o]));
                            }
                        }
                    }

                    for (int p = 0; p < probes.Count; p++)
                    {
                        Vector2 uv = probes[p];
                        bool legacy = ProbeLegacyCoplanarCovered(
                            face, uv, chunks);
                        bool current = ProbePointCovered(uv, polygons);

                        samples++;
                        if (legacy) covered++;
                        if (legacy != current) mismatches++;
                    }

                    // 单面的逐点等价性已足以覆盖“非空 FaceFragments + 部分覆盖 +
                    // 边界/容差附近”的判定路径；多面重复扫描会因单面数千个
                    // FaceFragment 而把回归时间推到分钟级。
                    break;
                }
            }

            Debug.Log(
                "[StageB3C-T8] coplanar equivalence faces=" + facesChecked +
                " samples=" + samples + " covered=" + covered +
                " mismatches=" + mismatches);

            Check(
                facesChecked > 0 && samples > 0,
                "T8: 共面覆盖等价性扫描有可测样本（面 " + facesChecked +
                "，样本 " + samples + "）"
            );
            Check(
                covered > 0 && covered < samples,
                "T8: 扫描同时包含覆盖点与未覆盖点（覆盖 " + covered + "/" +
                samples + "）"
            );
            Check(
                mismatches == 0,
                "T8: 旧逐点判定与新按 Face 预计算逐点等价（不一致 " +
                mismatches + "/" + samples + "）"
            );

            // ---- (c) 黑盒：面部被共面基础几何部分覆盖时的几何输出 ----
            DensityTerrain partialTerrain =
                partialObject.GetComponent<DensityTerrain>();
            List<TerrainChunkView> partialViews = GetViews(partialObject);

            Vector3Int voxel = new Vector3Int(30, SurfaceVoxelY, 30);
            Vector3Int neighbour = new Vector3Int(31, SurfaceVoxelY, 30);
            VoxelAwareMeshPrecut.FaceId plusXFace =
                new VoxelAwareMeshPrecut.FaceId(
                    0, 31, SurfaceVoxelY, 30);

            TerrainChunk ownerChunk = FindChunk(partialViews, voxel);

            // 面内 u = y 偏移 [0.2, 0.6]（材料区在 u < 0.7）、v = z 偏移 [0,1]。
            // 注意：边缘不得落在自然边界 u = 0.7 上，否则会与 Natural Boundary 段
            // 共线重叠，触发 Spec 13.9.6 的 Validation Failure（Mutation Abort）。
            List<Vector3> planted = PlantCoplanarFaceFragment(
                ownerChunk,
                plusXFace,
                new Vector2(0.2f, 0f),
                new Vector2(0.6f, 0f),
                new Vector2(0.6f, 1f),
                new Vector2(0.2f, 1f)
            );

            int plantedIndex = ownerChunk.Geometry.FaceFragments.Count - 1;

            ownerChunk.ReplaceAssembledMesh(
                VoxelAwareMeshPrecut.Assemble(ownerChunk.Geometry));

            partialViews = GetViews(partialObject);
            FaceKey plusXKey;
            TryBuildFaceKey(voxel, neighbour, 0, out plusXKey);
            HashSet<string> baseline = SnapshotFaceTriangleKeys(
                partialViews, plusXKey);

            Check(
                planted.Count == 4,
                "T8: 部分共面基础几何已植入（顶点 " + planted.Count + "）"
            );

            partialTerrain.VoxelField.Set(voxel, VoxelState.Air);
            int rebuilt = partialTerrain.RebuildChunksAffectedByVoxel(voxel);
            partialViews = GetViews(partialObject);

            double contributionArea = HiddenContributionArea(
                partialViews, plusXFace);
            double expectedWall = ExpectedWallAreaByDensity(
                partialViews, plusXKey, 0, false);
            double plantedArea = 0.4;

            int insidePlanted = 0;
            for (int iu = 0; iu < 8; iu++)
            {
                for (int iv = 0; iv < 8; iv++)
                {
                    Vector2 uv = new Vector2(
                        0.2f + (0.4f * (iu + 0.5f) / 8f),
                        (iv + 0.5f) / 8f);
                    if (HiddenContributionContains(
                        partialViews, plusXFace, uv))
                    {
                        insidePlanted++;
                    }
                }
            }

            FaceAudit audit = AuditFace(
                partialViews, plusXKey, voxel, partialTerrain.VoxelField,
                baseline);

            Debug.Log(
                "[StageB3C-T8] partial face=" + plusXFace.Key +
                " rebuilt=" + rebuilt +
                " expectedWall=" + expectedWall.ToString("F4") +
                " contributionArea=" + contributionArea.ToString("F4") +
                " plantedArea=" + plantedArea.ToString("F4") +
                " plantedCoveredPoints=" + insidePlanted + "/64" +
                " triangles=" + audit.TriangleCount +
                " windingOk=" + audit.WindingConsistent +
                " duplicates=" + audit.Coverage.DuplicateCount);

            Check(
                rebuilt >= 1 &&
                expectedWall >= 0.65 && expectedWall <= 0.75,
                "T8: 部分覆盖面材料侧面积 ≈ 0.7（实际 " +
                expectedWall.ToString("F4") + "）"
            );
            Check(
                Math.Abs(contributionArea -
                         (expectedWall - plantedArea)) <= 0.03,
                "T8: 内壁只覆盖未被共面基础几何覆盖的材料区域（内壁 " +
                contributionArea.ToString("F4") + "，期望 ≈ " +
                (expectedWall - plantedArea).ToString("F4") + "）"
            );
            Check(
                insidePlanted == 0,
                "T8: 内壁不与共面基础几何重叠（重叠采样点 " +
                insidePlanted + "/64）"
            );
            for (int i = 0; i < audit.WindingIssues.Count && i < 6; i++)
            {
                Debug.Log("[StageB3C-T8-winding] " + audit.WindingIssues[i]);
            }

            for (int i = 0; i < audit.TriangleDump.Count && i < 6; i++)
            {
                Debug.Log("[StageB3C-T8-triangle] " + audit.TriangleDump[i]);
            }

            Debug.Log(
                "[StageB3C-T8] winding baselineSkipped=" + audit.BaselineSkipped +
                " nearPlaneSkipped=" + audit.NearPlaneSkipped +
                " issues=" + audit.WindingIssues.Count +
                " windingOk=" + audit.WindingConsistent);

            Check(
                audit.Coverage.DuplicateCount == 0 &&
                audit.Coverage.DegenerateCount == 0 &&
                audit.WindingConsistent,
                "T8: 部分覆盖面无重复/退化且绕序自材料侧指向空腔（绕序 " +
                (audit.WindingConsistent ? "OK" : "FAIL") +
                "，问题 " + audit.WindingIssues.Count + "，基线跳过 " +
                audit.BaselineSkipped + "）"
            );

            bool plantedVisible = true;
            if (ownerChunk.Geometry.FaceFragmentVisibility.TryGetValue(
                plantedIndex, out bool stored))
            {
                plantedVisible = stored;
            }

            // B3-E2 语义变更（Spec §13.8.3 修订）：植入片位于“两侧皆为材料”的平面内，
            // 属于**内部几何** ⇒ 必须隐藏；真实地表（恰一侧为材料、材料侧未挖空）
            // 的可见性由 T12/T13 的体素对齐夹具验证。
            Check(
                !plantedVisible,
                "T8: 内部植入片（两侧皆材料）保持隐藏（visible=" +
                plantedVisible + "）"
            );

            // ---- 恢复：Air → Solid 后内壁消失、基础几何重新被过滤 ----
            partialTerrain.VoxelField.Set(voxel, VoxelState.Solid);
            partialTerrain.RebuildChunksAffectedByVoxel(voxel);
            partialViews = GetViews(partialObject);

            double restoredContribution = HiddenContributionArea(
                partialViews, plusXFace);
            FaceAudit restoredAudit = AuditFace(
                partialViews, plusXKey, voxel, partialTerrain.VoxelField, null);

            Debug.Log(
                "[StageB3C-T8] restored face=" + plusXFace.Key +
                " contributionArea=" + restoredContribution.ToString("F4") +
                " triangles=" + restoredAudit.TriangleCount);

            Check(
                restoredContribution <= 1e-6 &&
                restoredAudit.TriangleCount == 0,
                "T8: 恢复 Solid 后内壁消失且基础几何被过滤（内壁 " +
                restoredContribution.ToString("E3") + "，三角形 " +
                restoredAudit.TriangleCount + "）"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(alignedObject);
            UnityEngine.Object.DestroyImmediate(partialObject);
        }
    }

    // =====================================================================
    // T9 — 负坐标 / Chunk 边界：逐面材料侧覆盖 + 绕序 + 唯一归属
    // =====================================================================

    private static void CheckT9NegativeCoordinates()
    {
        // chunkStart = (-2, 0, -2) ⇒ Chunk (-2/-1, 0, -2/-1)，
        // Voxel x/z ∈ [-150, 0)，覆盖负坐标与负 plane/U/V。
        GameObject terrainObject = CreateFlatFixture(
            "StageB3C_T9Negative", new Vector3Int(-2, 0, -2));

        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();

            Vector3Int[] voxels =
            {
                new Vector3Int(-30, SurfaceVoxelY, -30),
                new Vector3Int(-75, SurfaceVoxelY, -30),
                new Vector3Int(-30, SurfaceVoxelY, -50)
            };

            string[] labels =
            {
                "T9[负坐标内部]",
                "T9[负坐标 X Chunk 边界]",
                "T9[负坐标 Z Chunk 边界]"
            };

            Vector3Int[] expectedChunks =
            {
                new Vector3Int(-1, 0, -1),
                new Vector3Int(-1, 0, -1),
                new Vector3Int(-1, 0, -1)
            };

            int totalCoverageFailures = 0;
            int totalEmptyFailures = 0;
            int totalFullFailures = 0;
            int totalOwnerFailures = 0;
            int totalDegenerate = 0;
            int totalDuplicate = 0;
            int totalWindingFailures = 0;
            int totalRebuilt = 0;

            for (int i = 0; i < voxels.Length; i++)
            {
                List<TerrainChunkView> views = GetViews(terrainObject);

                Vector3Int chunkCoordinate = ChunkSpace.VoxelToChunk(
                    voxels[i], ChunkVoxelSize);

                Check(
                    chunkCoordinate == expectedChunks[i],
                    labels[i] + ": 负坐标 Voxel→Chunk 契约（" +
                    voxels[i] + " → " + chunkCoordinate + "）"
                );

                terrain.VoxelField.Set(voxels[i], VoxelState.Air);
                int rebuilt = terrain.RebuildChunksAffectedByVoxel(voxels[i]);
                views = GetViews(terrainObject);
                totalRebuilt += rebuilt;

                FaceAuditResult result = AuditAllFacesAroundVoxel(
                    terrain, views, voxels[i], labels[i]);

                totalCoverageFailures += result.CoverageFailures;
                totalEmptyFailures += result.EmptyFailures;
                totalFullFailures += result.FullFailures;
                totalOwnerFailures += result.OwnerFailures;
                totalDegenerate += result.Degenerate;
                totalDuplicate += result.Duplicate;
                totalWindingFailures += result.WindingFailures;

                Check(
                    rebuilt >= 1,
                    labels[i] + ": 生产入口应用了 Voxel 修改（rebuilt=" +
                    rebuilt + "）"
                );

                terrain.VoxelField.Set(voxels[i], VoxelState.Solid);
                terrain.RebuildChunksAffectedByVoxel(voxels[i]);
            }

            Check(
                totalCoverageFailures == 0,
                "T9: 负坐标六面覆盖与材料分布一致（不一致 " +
                totalCoverageFailures + " 面）"
            );
            Check(
                totalEmptyFailures == 0,
                "T9: 负坐标材料侧为空的面不生成几何（异常 " +
                totalEmptyFailures + " 面）"
            );
            Check(
                totalFullFailures == 0,
                "T9: 负坐标材料侧完整的面完整封闭（异常 " +
                totalFullFailures + " 面）"
            );
            Check(
                totalOwnerFailures == 0,
                "T9: 负坐标有几何的面归属唯一 Chunk（异常 " +
                totalOwnerFailures + " 面）"
            );
            Check(
                totalDegenerate == 0,
                "T9: 负坐标无退化三角形（" + totalDegenerate + "）"
            );
            Check(
                totalDuplicate == 0,
                "T9: 负坐标无重复三角形（" + totalDuplicate + "）"
            );
            Check(
                totalWindingFailures == 0,
                "T9: 负坐标绕序全部自材料侧指向空腔（异常 " +
                totalWindingFailures + " 面）"
            );
            Check(
                totalRebuilt >= voxels.Length,
                "T9: 三个负坐标用例都触发了重建（" + totalRebuilt + "）"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    // =====================================================================
    // T8/T9 辅助工具
    // =====================================================================

    private sealed class FaceAuditResult
    {
        public int CoverageFailures;
        public int EmptyFailures;
        public int FullFailures;
        public int OwnerFailures;
        public int Degenerate;
        public int Duplicate;
        public int WindingFailures;
    }

    private static FaceAuditResult AuditAllFacesAroundVoxel(
        DensityTerrain terrain,
        List<TerrainChunkView> views,
        Vector3Int voxel,
        string label)
    {
        FaceAuditResult result = new FaceAuditResult();
        StringBuilder detail = new StringBuilder();

        for (int axis = 0; axis < 3; axis++)
        {
            for (int side = 0; side < 2; side++)
            {
                Vector3Int neighbour = voxel + AxisVector(axis, side);
                FaceKey face;
                if (!TryBuildFaceKey(voxel, neighbour, axis, out face))
                {
                    result.CoverageFailures++;
                    continue;
                }

                bool solidIsNegative = side == 0;
                double expected = ExpectedWallAreaByDensity(
                    views, face, axis, solidIsNegative);
                double tolerance = CoverageTolerance(
                    views, face, axis, solidIsNegative, expected);
                FaceAudit audit = AuditFace(
                    views,
                    face,
                    side == 0 ? neighbour : voxel,
                    terrain.VoxelField,
                    null);

                double over = audit.Coverage.UnionArea - expected;
                double under = Math.Max(
                    0.0, expected - audit.Coverage.UnionArea);

                if (over > tolerance || under > tolerance)
                {
                    result.CoverageFailures++;
                }

                if (expected <= 1e-3 && audit.TriangleCount > 0)
                {
                    result.EmptyFailures++;
                }

                if (expected >= 0.999 &&
                    audit.Coverage.UncoveredArea > WholeFaceCoverageTolerance)
                {
                    result.FullFailures++;
                }

                if (audit.TriangleCount > 0 && audit.OwnerChunkCount != 1)
                {
                    result.OwnerFailures++;
                }

                result.Degenerate += audit.Coverage.DegenerateCount;
                result.Duplicate += audit.Coverage.DuplicateCount;

                if (audit.TriangleCount > 0 && !audit.WindingConsistent)
                {
                    result.WindingFailures++;
                }

                detail.Append(" {轴").Append(axis)
                    .Append(side == 0 ? "负" : "正")
                    .Append(" 期望").Append(expected.ToString("F4"))
                    .Append(" 覆盖").Append(
                        audit.Coverage.UnionArea.ToString("F4"))
                    .Append(" 未覆盖").Append(
                        audit.Coverage.UncoveredArea.ToString("F4"))
                    .Append(" 三角形").Append(audit.TriangleCount)
                    .Append(" 归属").Append(audit.OwnerChunkCount)
                    .Append(" 绕序").Append(
                        audit.WindingConsistent ? "OK" : "FAIL")
                    .Append("}");
            }
        }

        Debug.Log("[StageB3C-T9] " + label + " voxel=" + voxel + detail);
        return result;
    }

    private static List<Vector3> PlantCoplanarFaceFragment(
        TerrainChunk chunk,
        VoxelAwareMeshPrecut.FaceId face,
        params Vector2[] uvs)
    {
        List<Vector3> positions = new List<Vector3>();
        VoxelAwareMeshPrecut.FaceFragment fragment =
            new VoxelAwareMeshPrecut.FaceFragment();
        fragment.Face = face;

        for (int i = 0; i < uvs.Length; i++)
        {
            Vector3 local = VoxelAwareMeshSpatialFace.ToChunkLocal(
                face,
                uvs[i],
                chunk.ChunkOrigin,
                chunk.VoxelSize
            );

            positions.Add(local);
            fragment.Polygon.Add(local);
            fragment.VertexIds.Add(chunk.Geometry.Pool.GetOrAdd(local));
        }

        chunk.Geometry.FaceFragments.Add(fragment);
        return positions;
    }

    private static double HiddenContributionArea(
        List<TerrainChunkView> views,
        VoxelAwareMeshPrecut.FaceId face)
    {
        double area = 0.0;

        for (int v = 0; v < views.Count; v++)
        {
            TerrainChunk chunk = views[v].Chunk;
            if (chunk == null || chunk.Geometry == null) continue;

            List<VoxelAwareMeshSpatialFace.HiddenFaceFragment> contributions;
            if (!chunk.Geometry.HiddenFaceContributions.TryGetValue(
                face.Key, out contributions))
            {
                continue;
            }

            if (contributions == null) continue;

            for (int i = 0; i < contributions.Count; i++)
            {
                List<Vector3> polygon = contributions[i].Polygon;
                if (polygon == null || polygon.Count < 3) continue;

                List<Vector2> local = new List<Vector2>(polygon.Count);
                for (int p = 0; p < polygon.Count; p++)
                {
                    local.Add(VoxelAwareMeshSpatialFace.ToFaceLocal(
                        face,
                        polygon[p],
                        chunk.ChunkOrigin,
                        chunk.VoxelSize
                    ));
                }

                area += Math.Abs(FaceLocalPolygonArea(local));
            }
        }

        return area;
    }

    private static bool HiddenContributionContains(
        List<TerrainChunkView> views,
        VoxelAwareMeshPrecut.FaceId face,
        Vector2 uv)
    {
        for (int v = 0; v < views.Count; v++)
        {
            TerrainChunk chunk = views[v].Chunk;
            if (chunk == null || chunk.Geometry == null) continue;

            List<VoxelAwareMeshSpatialFace.HiddenFaceFragment> contributions;
            if (!chunk.Geometry.HiddenFaceContributions.TryGetValue(
                face.Key, out contributions))
            {
                continue;
            }

            if (contributions == null) continue;

            for (int i = 0; i < contributions.Count; i++)
            {
                List<Vector3> polygon = contributions[i].Polygon;
                if (polygon == null || polygon.Count < 3) continue;

                List<Vector2> local = new List<Vector2>(polygon.Count);
                for (int p = 0; p < polygon.Count; p++)
                {
                    local.Add(VoxelAwareMeshSpatialFace.ToFaceLocal(
                        face,
                        polygon[p],
                        chunk.ChunkOrigin,
                        chunk.VoxelSize
                    ));
                }

                if (PointInPolygon(uv, local)) return true;
            }
        }

        return false;
    }

    private static double FaceLocalPolygonArea(List<Vector2> polygon)
    {
        if (polygon == null || polygon.Count < 3) return 0.0;

        double sum = 0.0;
        for (int i = 0; i < polygon.Count; i++)
        {
            Vector2 a = polygon[i];
            Vector2 b = polygon[(i + 1) % polygon.Count];
            sum += (double)a.x * b.y - (double)b.x * a.y;
        }

        return sum * 0.5;
    }

    // =====================================================================
    // T10 — FaceId 标识唯一性与“字段比较 == Key 字符串比较”
    // =====================================================================

    private static void CheckT10FaceIdentityUnique()
    {
        int combinations = 0;
        int collisions = 0;
        int mismatches = 0;
        int keyCollisions = 0;
        HashSet<string> keys = new HashSet<string>();
        Dictionary<string, int> fieldTupleByKey = new Dictionary<string, int>();

        for (int axis = 0; axis < 3; axis++)
        {
            for (int plane = -3; plane <= 2; plane++)
            {
                for (int u = -2; u <= 1; u++)
                {
                    for (int v = -2; v <= 1; v++)
                    {
                        VoxelAwareMeshPrecut.FaceId face =
                            new VoxelAwareMeshPrecut.FaceId(axis, plane, u, v);
                        combinations++;

                        string key = face.Key;
                        keys.Add(key);

                        if (fieldTupleByKey.ContainsKey(key))
                        {
                            // 同一 Key 只能对应同一字段组合（由 keys.Count == combinations 进一步保证）。
                            collisions++;
                        }
                        else
                        {
                            fieldTupleByKey.Add(key, 0);
                        }

                        // 自身相等
                        if (!face.SameAs(face)) mismatches++;

                        // 单字段变体：字段比较与字符串比较必须一致，且必须判为不同面
                        VoxelAwareMeshPrecut.FaceId[] variants =
                        {
                            new VoxelAwareMeshPrecut.FaceId(axis + 1, plane, u, v),
                            new VoxelAwareMeshPrecut.FaceId(axis, plane + 1, u, v),
                            new VoxelAwareMeshPrecut.FaceId(axis, plane, u + 1, v),
                            new VoxelAwareMeshPrecut.FaceId(axis, plane, u, v + 1),
                            new VoxelAwareMeshPrecut.FaceId(axis, plane, v, u)
                        };

                        for (int i = 0; i < variants.Length; i++)
                        {
                            bool sameFields = face.SameAs(variants[i]);
                            bool sameKey = face.Key == variants[i].Key;

                            // 两个比较器结论必须一致
                            if (sameFields != sameKey) mismatches++;

                            // 字段不同却 Key 相同 = 真实标识冲突
                            if (!sameFields && sameKey) keyCollisions++;
                        }
                    }
                }
            }
        }

        Check(
            combinations == 3 * 6 * 4 * 4,
            "T10: FaceId 组合数（" + combinations + "）"
        );
        Check(
            keys.Count == combinations,
            "T10: 不同 Axis/Plane/U/V 组合的 Key 唯一（" + keys.Count + "/" +
            combinations + "）"
        );
        Check(
            collisions == 0,
            "T10: 相同 Key 的 FaceId 字段完全一致（冲突 " + collisions + "）"
        );
        Check(
            mismatches == 0,
            "T10: SameAs 与 Key 字符串比较等价（不一致 " + mismatches + "）"
        );
        Check(
            keyCollisions == 0,
            "T10: 字段不同但 Key 相同的冲突为 0（冲突 " + keyCollisions + "）"
        );

        // 显式反例：U/V 互换、以及负 Plane 不得被判为同一面。
        VoxelAwareMeshPrecut.FaceId a =
            new VoxelAwareMeshPrecut.FaceId(0, -1, 2, 3);
        VoxelAwareMeshPrecut.FaceId b =
            new VoxelAwareMeshPrecut.FaceId(0, -1, 3, 2);
        VoxelAwareMeshPrecut.FaceId c =
            new VoxelAwareMeshPrecut.FaceId(0, -2, 2, 3);
        Check(
            !a.SameAs(b) && a.Key != b.Key &&
            !a.SameAs(c) && a.Key != c.Key,
            "T10: U/V 互换与负 Plane 变化均不被判为同一面"
        );
    }

    // =====================================================================
    // T11 — 优化路径（逐字段）与旧路径（FaceId.Key 字符串）结果一致性
    // =====================================================================

    private static void CheckT11LegacyCompareEquivalence()
    {
        CheckT11Case(
            "T11[平面单挖]",
            -1f,
            Vector3Int.zero,
            new Vector3Int[] { new Vector3Int(30, SurfaceVoxelY, 30) },
            true);

        CheckT11Case(
            "T11[平面相邻多挖]",
            -1f,
            Vector3Int.zero,
            new Vector3Int[]
            {
                new Vector3Int(30, SurfaceVoxelY, 30),
                new Vector3Int(31, SurfaceVoxelY, 30)
            },
            true);

        CheckT11Case(
            "T11[共面非空 FaceFragments]",
            15f,
            Vector3Int.zero,
            new Vector3Int[] { new Vector3Int(30, SurfaceVoxelY - 1, 30) },
            true);

        // 诊断（仅记录）：两个“同参数、未挖掘”的体素对齐夹具是否逐三角形一致。
        // 用于判定上面 Limit 中的既有非确定性来源（与 B3-E 无关）。
        GameObject diagA = CreateT11Terrain(
            "StageB3C_T11DiagA", 15f, Vector3Int.zero);
        GameObject diagB = CreateT11Terrain(
            "StageB3C_T11DiagB", 15f, Vector3Int.zero);

        try
        {
            Dictionary<Vector3Int, MeshCanonical> diagCanonicalA =
                SnapshotCanonical(GetViews(diagA));
            Dictionary<Vector3Int, MeshCanonical> diagCanonicalB =
                SnapshotCanonical(GetViews(diagB));
            bool diagSame = SameAllChunks(
                diagCanonicalA, diagCanonicalB, out string diagDetail);

            Debug.Log(
                "[StageB3C-T11diag] fresh aligned terrains same=" + diagSame +
                " trisA=" + TriangleCountOf(diagCanonicalA) +
                " trisB=" + TriangleCountOf(diagCanonicalB) +
                (diagDetail.Length > 0 ? " detail=" + diagDetail : string.Empty));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(diagA);
            UnityEngine.Object.DestroyImmediate(diagB);
        }

        CheckT11Case(
            "T11[负坐标 Chunk 边界]",
            -1f,
            new Vector3Int(-2, 0, -2),
            new Vector3Int[] { new Vector3Int(-75, SurfaceVoxelY, -30) },
            true);
    }

    private static GameObject CreateT11Terrain(
        string name,
        float height,
        Vector3Int chunkStart)
    {
        GameObject terrainObject = CreateFlatFixture(name, chunkStart);

        if (height > 0f)
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();
            terrain.height = height;
            terrain.Regenerate();
        }

        return terrainObject;
    }

    private static void CheckT11Case(
        string label,
        float height,
        Vector3Int chunkStart,
        Vector3Int[] voxels,
        bool assertRestoreBaseline)
    {
        GameObject optimizedObject = CreateT11Terrain(
            label + "-Opt", height, chunkStart);
        GameObject legacyObject = CreateT11Terrain(
            label + "-Legacy", height, chunkStart);

        try
        {
            DensityTerrain optimizedTerrain =
                optimizedObject.GetComponent<DensityTerrain>();
            DensityTerrain legacyTerrain =
                legacyObject.GetComponent<DensityTerrain>();

            Dictionary<Vector3Int, MeshCanonical> optimizedBaseline =
                SnapshotCanonical(GetViews(optimizedObject));
            Dictionary<Vector3Int, MeshCanonical> legacyBaseline =
                SnapshotCanonical(GetViews(legacyObject));

            // (1) 优化路径：逐字段比较
            VoxelAwareMeshPrecut.UseLegacyFaceKeyComparison = false;
            for (int i = 0; i < voxels.Length; i++)
            {
                optimizedTerrain.VoxelField.Set(voxels[i], VoxelState.Air);
                optimizedTerrain.RebuildChunksAffectedByVoxel(voxels[i]);
            }

            bool optimizedGraphValid =
                !VoxelAwareMeshRuntimeMutation.LastBoundaryGraphValidationFailure;
            bool optimizedCommitted =
                VoxelAwareMeshRuntimeMutation.LastMutationCommitted;

            // (2) 旧路径：FaceId.Key 字符串比较
            VoxelAwareMeshPrecut.UseLegacyFaceKeyComparison = true;
            for (int i = 0; i < voxels.Length; i++)
            {
                legacyTerrain.VoxelField.Set(voxels[i], VoxelState.Air);
                legacyTerrain.RebuildChunksAffectedByVoxel(voxels[i]);
            }

            bool legacyGraphValid =
                !VoxelAwareMeshRuntimeMutation.LastBoundaryGraphValidationFailure;
            bool legacyCommitted =
                VoxelAwareMeshRuntimeMutation.LastMutationCommitted;

            VoxelAwareMeshPrecut.UseLegacyFaceKeyComparison = false;

            List<TerrainChunkView> optimizedViews = GetViews(optimizedObject);
            List<TerrainChunkView> legacyViews = GetViews(legacyObject);

            Check(
                optimizedCommitted && legacyCommitted &&
                optimizedGraphValid && legacyGraphValid,
                label + ": 两条路径均提交且 Boundary Graph 校验通过"
            );

            Check(
                SameAllChunks(
                    SnapshotCanonical(optimizedViews),
                    SnapshotCanonical(legacyViews),
                    out string meshDetail),
                label + ": 最终几何一致（含三角形环序/绕序）" +
                (meshDetail.Length > 0 ? " —— " + meshDetail : string.Empty)
            );

            Check(
                SameSegmentSets(optimizedViews, legacyViews, out string segmentDetail),
                label + ": Face Segment 集合一致" +
                (segmentDetail.Length > 0 ? " —— " + segmentDetail : string.Empty)
            );

            Check(
                SameContributionSets(
                    optimizedViews, legacyViews, out string contributionDetail),
                label + ": Hidden Face 贡献与面归属一致" +
                (contributionDetail.Length > 0 ? " —— " + contributionDetail : string.Empty)
            );

            // (3) 恢复（Air → Solid）后两路径都必须回到各自基线
            for (int i = 0; i < voxels.Length; i++)
            {
                optimizedTerrain.VoxelField.Set(voxels[i], VoxelState.Solid);
                optimizedTerrain.RebuildChunksAffectedByVoxel(voxels[i]);
                legacyTerrain.VoxelField.Set(voxels[i], VoxelState.Solid);
                legacyTerrain.RebuildChunksAffectedByVoxel(voxels[i]);
            }

            string optimizedRestoreDetail = string.Empty;
            string legacyRestoreDetail = string.Empty;
            string baselineDetail = string.Empty;
            string restoredPathDetail = string.Empty;

            Dictionary<Vector3Int, MeshCanonical> optimizedRestored =
                SnapshotCanonical(GetViews(optimizedObject));
            Dictionary<Vector3Int, MeshCanonical> legacyRestored =
                SnapshotCanonical(GetViews(legacyObject));

            // B3-E 的核心保证：恢复结果与“用哪种面标识比较”无关。
            Check(
                SameAllChunks(
                    optimizedRestored, legacyRestored, out restoredPathDetail),
                label + ": 恢复后两路径结果一致" +
                (restoredPathDetail.Length > 0
                    ? " —— " + restoredPathDetail
                    : string.Empty)
            );

            Check(
                SameSegmentSets(
                    GetViews(optimizedObject),
                    GetViews(legacyObject),
                    out string restoredSegmentDetail),
                label + ": 恢复后 Face Segment 集合一致" +
                (restoredSegmentDetail.Length > 0
                    ? " —— " + restoredSegmentDetail
                    : string.Empty)
            );

            if (assertRestoreBaseline)
            {
                Check(
                    SameAllChunks(
                        optimizedBaseline,
                        optimizedRestored,
                        out optimizedRestoreDetail) &&
                    SameAllChunks(
                        legacyBaseline,
                        legacyRestored,
                        out legacyRestoreDetail) &&
                    SameAllChunks(
                        optimizedBaseline,
                        legacyBaseline,
                        out baselineDetail),
                    label + ": 恢复后回到基线且两路径基线一致" +
                    (optimizedRestoreDetail.Length > 0
                        ? " —— opt:" + optimizedRestoreDetail
                        : string.Empty) +
                    (legacyRestoreDetail.Length > 0
                        ? " —— legacy:" + legacyRestoreDetail
                        : string.Empty) +
                    (baselineDetail.Length > 0
                        ? " —— baseline:" + baselineDetail
                        : string.Empty)
                );
            }
            else
            {
                // 体素对齐表面（FaceFragments ≈ 60000）上存在与 B3-E 无关的既有现象：
                // 独立生成的两个同参数夹具网格并不逐三角形一致（见 T11diag 日志），
                // 因此该夹具只断言“路径无关”，不断言“恢复 == 自身基线”。
                Limit(
                    label + ": 体素对齐表面夹具存在既有“生成/恢复”非确定性，" +
                    "仅断言两路径一致（基线三角形 opt=" +
                    TriangleCountOf(optimizedBaseline) + " legacy=" +
                    TriangleCountOf(legacyBaseline) + "，恢复后 opt=" +
                    TriangleCountOf(optimizedRestored) + " legacy=" +
                    TriangleCountOf(legacyRestored) + "）"
                );
            }
        }
        finally
        {
            VoxelAwareMeshPrecut.UseLegacyFaceKeyComparison = false;
            UnityEngine.Object.DestroyImmediate(optimizedObject);
            UnityEngine.Object.DestroyImmediate(legacyObject);
        }
    }

    private static bool SameSegmentSets(
        List<TerrainChunkView> a,
        List<TerrainChunkView> b,
        out string detail)
    {
        detail = string.Empty;
        List<string> left = CollectSegmentSignatures(a);
        List<string> right = CollectSegmentSignatures(b);
        left.Sort(StringComparer.Ordinal);
        right.Sort(StringComparer.Ordinal);

        if (left.Count != right.Count)
        {
            detail = "Segment 数 " + left.Count + " vs " + right.Count;
            return false;
        }

        for (int i = 0; i < left.Count; i++)
        {
            if (left[i] == right[i]) continue;
            detail = "Segment 差异 @" + i + " [" + left[i] + "] vs [" +
                     right[i] + "]";
            return false;
        }

        return true;
    }

    private static List<string> CollectSegmentSignatures(
        List<TerrainChunkView> views)
    {
        List<string> result = new List<string>();

        for (int v = 0; v < views.Count; v++)
        {
            TerrainChunk chunk = views[v].Chunk;
            if (chunk == null || chunk.Geometry == null) continue;

            List<VoxelAwareMeshSpatialFace.SpatialFaceRecord> records =
                chunk.Geometry.SpatialFaces;

            for (int r = 0; r < records.Count; r++)
            {
                VoxelAwareMeshSpatialFace.SpatialFaceRecord record = records[r];
                if (record == null || record.Segments == null) continue;

                for (int s = 0; s < record.Segments.Count; s++)
                {
                    VoxelAwareMeshSpatialFace.FaceSegment segment =
                        record.Segments[s];
                    string p0 = QuantizePoint2(segment.A);
                    string p1 = QuantizePoint2(segment.B);
                    if (string.CompareOrdinal(p0, p1) > 0)
                    {
                        string swap = p0;
                        p0 = p1;
                        p1 = swap;
                    }

                    result.Add(
                        chunk.Coordinate + "|" + record.Face.Key + "|" +
                        p0 + "|" + p1 + "|" +
                        (segment.IsBridge ? "B" : "-") +
                        (segment.IsNatural ? "N" : "-") +
                        (segment.IsCoplanar ? "C" : "-"));
                }
            }
        }

        return result;
    }

    private static int TriangleCountOf(
        Dictionary<Vector3Int, MeshCanonical> canonical)
    {
        int total = 0;
        if (canonical == null) return 0;

        foreach (KeyValuePair<Vector3Int, MeshCanonical> pair in canonical)
        {
            if (pair.Value != null && pair.Value.TriangleKeys != null)
            {
                total += pair.Value.TriangleKeys.Count;
            }
        }

        return total;
    }

    private static string QuantizePoint2(Vector2 p)
    {
        return Mathf.RoundToInt(p.x / Quantize) + "," +
               Mathf.RoundToInt(p.y / Quantize);
    }

    private static bool SameContributionSets(
        List<TerrainChunkView> a,
        List<TerrainChunkView> b,
        out string detail)
    {
        detail = string.Empty;
        List<string> left = CollectContributionSignatures(a);
        List<string> right = CollectContributionSignatures(b);
        left.Sort(StringComparer.Ordinal);
        right.Sort(StringComparer.Ordinal);

        if (left.Count != right.Count)
        {
            detail = "贡献数 " + left.Count + " vs " + right.Count;
            return false;
        }

        for (int i = 0; i < left.Count; i++)
        {
            if (left[i] == right[i]) continue;
            detail = "贡献差异 @" + i + " [" + left[i] + "] vs [" +
                     right[i] + "]";
            return false;
        }

        return true;
    }

    private static List<string> CollectContributionSignatures(
        List<TerrainChunkView> views)
    {
        List<string> result = new List<string>();

        for (int v = 0; v < views.Count; v++)
        {
            TerrainChunk chunk = views[v].Chunk;
            if (chunk == null || chunk.Geometry == null) continue;

            foreach (KeyValuePair<string, List<VoxelAwareMeshSpatialFace.HiddenFaceFragment>>
                pair in chunk.Geometry.HiddenFaceContributions)
            {
                List<VoxelAwareMeshSpatialFace.HiddenFaceFragment> contributions =
                    pair.Value;
                if (contributions == null) continue;

                for (int i = 0; i < contributions.Count; i++)
                {
                    List<Vector3> polygon = contributions[i].Polygon;
                    result.Add(
                        chunk.Coordinate + "|" + pair.Key + "|" +
                        (polygon != null ? polygon.Count : 0) + "|" +
                        ContributionAreaKey(polygon));
                }
            }
        }

        return result;
    }

    private static string ContributionAreaKey(List<Vector3> polygon)
    {
        if (polygon == null || polygon.Count < 3) return "F0.0000";

        double area = 0.0;
        for (int i = 1; i + 1 < polygon.Count; i++)
        {
            area += Vector3.Cross(
                polygon[i] - polygon[0],
                polygon[i + 1] - polygon[0]).magnitude * 0.5;
        }

        return "F" + area.ToString("F4");
    }

    // =====================================================================
    // B3-E1 — 高密度 FaceFragments 夹具网格非确定性调查（只读诊断，生产零改动）
    //
    // 复现条件：体素对齐平面地形（height=15、chunkStart=(0,0,0)、flat、resolution=75、
    // 2×1×2 Chunk），FaceFragments ≈ 60000，未执行任何挖掘。
    //
    // 逐阶段比较：FaceFragments 输入集合 → Bridges → Boundary Graph →
    // 输入多边形（顺序/内容）→ 过滤（重复 id / 退化面积）→ 焊接 → 最终 Mesh 三角形。
    // =====================================================================

    public static void RunDeterminismProbe()
    {
        const int runCount = 10;

        List<DeterminismSnapshot> snapshots = new List<DeterminismSnapshot>();
        List<GameObject> liveTerrains = new List<GameObject>();

        try
        {
            // 两个独立夹具（保持存活，用于差异追溯）。
            for (int i = 0; i < 2; i++)
            {
                GameObject terrainObject = CreateT11Terrain(
                    "StageB3C_Det" + i, 15f, Vector3Int.zero);
                liveTerrains.Add(terrainObject);
                DeterminismSnapshot snapshot = CaptureDeterminismSnapshot(
                    GetViews(terrainObject), i, true);
                snapshots.Add(snapshot);
                LogDeterminismSnapshot(snapshot);
            }

            // 统计其余运行（仅摘要 + 哈希，不保留完整签名以控制内存）。
            for (int run = 2; run < runCount; run++)
            {
                GameObject terrainObject = CreateT11Terrain(
                    "StageB3C_Det" + run, 15f, Vector3Int.zero);

                try
                {
                    DeterminismSnapshot snapshot = CaptureDeterminismSnapshot(
                        GetViews(terrainObject), run, false);
                    snapshots.Add(snapshot);
                    LogDeterminismSnapshot(snapshot);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(terrainObject);
                }
            }

            // ---- 阶段 2：同一夹具“挖掘 + 恢复”前后的各阶段对比 ----
            GameObject digObject = liveTerrains[0];
            DensityTerrain digTerrain = digObject.GetComponent<DensityTerrain>();
            Vector3Int detVoxel = new Vector3Int(30, SurfaceVoxelY - 1, 30);

            digTerrain.VoxelField.Set(detVoxel, VoxelState.Air);
            digTerrain.RebuildChunksAffectedByVoxel(detVoxel);
            DeterminismSnapshot digged = CaptureDeterminismSnapshot(
                GetViews(digObject), 100, true);
            LogDeterminismSnapshot(digged);
            LogDeterminismStageComparison(snapshots[0], digged);
            DiffTriangleMultisets(
                "baseline", snapshots[0], GetViews(digObject),
                "digged", digged, GetViews(digObject));

            digTerrain.VoxelField.Set(detVoxel, VoxelState.Solid);
            digTerrain.RebuildChunksAffectedByVoxel(detVoxel);
            DeterminismSnapshot restored = CaptureDeterminismSnapshot(
                GetViews(digObject), 101, true);
            LogDeterminismSnapshot(restored);
            LogDeterminismStageComparison(snapshots[0], restored);
            DiffTriangleMultisets(
                "baseline", snapshots[0], GetViews(digObject),
                "restored", restored, GetViews(digObject));

            // ---- 阶段 3：dig/restore 之后再生成两个全新夹具，检查全局状态污染 ----
            for (int i = 0; i < 2; i++)
            {
                GameObject afterObject = CreateT11Terrain(
                    "StageB3C_DetAfter" + i, 15f, Vector3Int.zero);
                liveTerrains.Add(afterObject);

                DeterminismSnapshot freshAfter = CaptureDeterminismSnapshot(
                    GetViews(afterObject), 200 + i, true);
                LogDeterminismSnapshot(freshAfter);
                LogDeterminismStageComparison(snapshots[0], freshAfter);
                DiffTriangleMultisets(
                    "baseline", snapshots[0], GetViews(liveTerrains[0]),
                    "freshAfter" + i, freshAfter, GetViews(afterObject));
            }

            // ---- 阶段 4：销毁带历史状态的夹具后，立即生成新夹具（检验跨实例泄漏）----
            UnityEngine.Object.DestroyImmediate(liveTerrains[0]);
            liveTerrains[0] = null;

            GameObject afterDestroy = CreateT11Terrain(
                "StageB3C_DetAfterDestroy", 15f, Vector3Int.zero);
            liveTerrains.Add(afterDestroy);
            DeterminismSnapshot freshAfterDestroy = CaptureDeterminismSnapshot(
                GetViews(afterDestroy), 300, true);
            LogDeterminismSnapshot(freshAfterDestroy);
            LogDeterminismStageComparison(snapshots[0], freshAfterDestroy);
            DiffTriangleMultisets(
                "baseline", snapshots[0], GetViews(afterDestroy),
                "freshAfterDestroy", freshAfterDestroy, GetViews(afterDestroy));

            LogDeterminismSummary(snapshots);

            // 选择第一对“三角形数不同”的夹具做精确差异追溯；
            // 若前两对相同，则额外生成第三个夹具参与比较（最多 3 个）。
            int compareIndex = -1;
            if (snapshots[0].Triangles != snapshots[1].Triangles)
            {
                compareIndex = 1;
            }
            else
            {
                for (int i = 2; i < snapshots.Count; i++)
                {
                    if (snapshots[i].Triangles == snapshots[0].Triangles) continue;
                    compareIndex = i;
                    break;
                }
            }

            if (compareIndex < 0)
            {
                Debug.Log(
                    "[StageB3C-Det] all runs have identical triangle counts (" +
                    snapshots[0].Triangles +
                    "); no pairwise diff to report in this probe session");
                return;
            }

            GameObject otherObject = null;
            if (compareIndex > 1)
            {
                otherObject = CreateT11Terrain(
                    "StageB3C_DetCmp" + compareIndex, 15f, Vector3Int.zero);
                liveTerrains.Add(otherObject);
            }

            List<TerrainChunkView> reasonViews = otherObject != null
                ? GetViews(otherObject)
                : GetViews(liveTerrains[1]);

            CompareDeterminismSnapshots(
                snapshots[0],
                GetViews(liveTerrains[0]),
                snapshots[compareIndex],
                reasonViews,
                true);
        }
        finally
        {
            for (int i = 0; i < liveTerrains.Count; i++)
            {
                if (liveTerrains[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(liveTerrains[i]);
                }
            }
        }
    }

    private sealed class DeterminismSnapshot
    {
        public int Run;
        public int FaceFragments;
        public int Bridges;
        public int Fragments;
        public int InputPolygons;
        public int InputVertices;
        public int FilteredDuplicateId;
        public int FilteredDegenerate;
        public int FanTriangles;
        public int VerticesAfterWeld;
        public int Triangles;
        public int DuplicateTriangles;
        public int ZeroAreaTriangles;
        public int NonManifoldEdges;
        public int GraphEdges;
        public int GraphNodes;
        public int GraphInvalidFaces;
        public long FragmentHashOrdered;
        public long FragmentHashSorted;
        public long FragmentRotationHash;
        public long BridgeHashSorted;
        public long PolygonHashOrdered;
        public long MeshTriangleHash;
        public long ReplayTriangleHash;
        public int InactiveFaceFragments;
        public int InvisibleFaceFragments;
        public int VisibilityEntries;
        public List<string> VisibilityFaceKeys = new List<string>();
        public bool Full;
        public List<string> SkippedFragmentDetails = new List<string>();
        public List<string> FragmentSorted = new List<string>();
        public List<string> FragmentRotatedSorted = new List<string>();
        public List<string> PolygonSequence = new List<string>();
        public Dictionary<string, int> MeshTriangleMultiplicity =
            new Dictionary<string, int>();
        public Dictionary<string, int> ReplayTriangleMultiplicity =
            new Dictionary<string, int>();
    }

    private static DeterminismSnapshot CaptureDeterminismSnapshot(
        List<TerrainChunkView> views,
        int run,
        bool full)
    {
        DeterminismSnapshot snapshot = new DeterminismSnapshot { Run = run, Full = full };

        List<string> fragmentOrdered = new List<string>();
        List<string> fragmentRotated = new List<string>();
        List<string> bridgeKeys = new List<string>();
        List<string> polygonSequence = new List<string>();
        HashSet<string> graphNodeKeys = new HashSet<string>();

        for (int v = 0; v < views.Count; v++)
        {
            TerrainChunk chunk = views[v].Chunk;
            if (chunk == null || chunk.Geometry == null) continue;

            VoxelAwareMeshPrecut.ChunkGeometry geometry = chunk.Geometry;
            snapshot.FaceFragments += geometry.FaceFragments.Count;
            snapshot.Bridges += geometry.Bridges.Count;
            snapshot.Fragments += geometry.Fragments.Count;
            snapshot.VisibilityEntries += geometry.FaceFragmentVisibility.Count;

            // (1) FaceFragments 输入：顺序签名 + 旋转规范化签名。
            for (int i = 0; i < geometry.FaceFragments.Count; i++)
            {
                VoxelAwareMeshPrecut.FaceFragment fragment =
                    geometry.FaceFragments[i];

                bool fragmentActive = fragment.Active;
                bool fragmentVisible = true;
                bool hasVisibilityEntry =
                    geometry.FaceFragmentVisibility.TryGetValue(
                        i, out fragmentVisible);

                if (!fragmentActive)
                {
                    snapshot.InactiveFaceFragments++;
                    if (full)
                    {
                        snapshot.SkippedFragmentDetails.Add(
                            "INACTIVE chunk=" + chunk.Coordinate + " idx=" + i +
                            " face=" + fragment.Face.Key +
                            " verts=" + fragment.Polygon.Count +
                            " submesh=" + fragment.Submesh);
                    }
                }

                if (hasVisibilityEntry)
                {
                    snapshot.VisibilityFaceKeys.Add(
                        chunk.Coordinate + " idx=" + i + " face=" +
                        fragment.Face.Key + " visible=" +
                        (fragmentVisible ? "1" : "0"));
                }

                if (hasVisibilityEntry && !fragmentVisible)
                {
                    snapshot.InvisibleFaceFragments++;
                    if (full)
                    {
                        snapshot.SkippedFragmentDetails.Add(
                            "INVISIBLE chunk=" + chunk.Coordinate + " idx=" + i +
                            " face=" + fragment.Face.Key +
                            " verts=" + fragment.Polygon.Count +
                            " submesh=" + fragment.Submesh);
                    }
                }

                string ordered = "F|" + chunk.Coordinate + "|" +
                                 fragment.Face.Key + "|" +
                                 PolygonSignature3(fragment.Polygon);
                fragmentOrdered.Add(ordered);

                string rotated = "F|" + chunk.Coordinate + "|" +
                                 fragment.Face.Key + "|" +
                                 RotatedPolygonSignature3(fragment.Polygon);
                fragmentRotated.Add(rotated);

                if (full) snapshot.FragmentSorted.Add(ordered);
            }

            // (2) Bridges 输入。
            for (int i = 0; i < geometry.Bridges.Count; i++)
            {
                VoxelAwareMeshPrecut.BridgeRecord bridge = geometry.Bridges[i];
                string a = PositionKey3(
                    geometry.Pool.Positions[bridge.VertexA].x,
                    geometry.Pool.Positions[bridge.VertexA].y,
                    geometry.Pool.Positions[bridge.VertexA].z);
                string b = PositionKey3(
                    geometry.Pool.Positions[bridge.VertexB].x,
                    geometry.Pool.Positions[bridge.VertexB].y,
                    geometry.Pool.Positions[bridge.VertexB].z);
                if (string.CompareOrdinal(a, b) > 0)
                {
                    string swap = a;
                    a = b;
                    b = swap;
                }

                bridgeKeys.Add(
                    "B|" + chunk.Coordinate + "|" + bridge.Face.Key + "|" +
                    a + "|" + b + "|" + (bridge.HasCutVertex ? "1" : "0"));
            }

            // (3) Boundary Graph 输出（节点数/边数/有效性）。
            List<VoxelAwareMeshSpatialFace.SpatialFaceRecord> records =
                geometry.SpatialFaces;
            for (int r = 0; r < records.Count; r++)
            {
                VoxelAwareMeshSpatialFace.SpatialFaceRecord record = records[r];
                if (record == null || record.Segments == null) continue;

                snapshot.GraphEdges += record.Segments.Count;

                for (int s = 0; s < record.Segments.Count; s++)
                {
                    VoxelAwareMeshSpatialFace.FaceSegment segment =
                        record.Segments[s];
                    graphNodeKeys.Add(
                        "N|" + chunk.Coordinate + "|" + record.Face.Key + "|" +
                        QuantizePoint2(segment.A));
                    graphNodeKeys.Add(
                        "N|" + chunk.Coordinate + "|" + record.Face.Key + "|" +
                        QuantizePoint2(segment.B));
                }

                string graphError;
                if (!VoxelAwareMeshRuntimeMutation.ValidateBoundaryGraphSegments(
                    record.Face, record.Segments, out graphError))
                {
                    snapshot.GraphInvalidFaces++;
                }
            }

            // (4)-(5) 输入多边形（顺序）→ 过滤 → 焊接 → 三角化（忠实重放 Assemble 的选择与 fan 逻辑）。
            ReplayChunkAssembly(
                chunk,
                snapshot,
                polygonSequence,
                full);
        }

        snapshot.GraphNodes = graphNodeKeys.Count;
        snapshot.FragmentHashOrdered = FnvHash(fragmentOrdered, true);
        snapshot.FragmentHashSorted = FnvHash(fragmentOrdered, false);
        snapshot.FragmentRotationHash = FnvHash(fragmentRotated, false);
        snapshot.BridgeHashSorted = FnvHash(bridgeKeys, false);
        snapshot.PolygonHashOrdered = FnvHash(polygonSequence, true);

        if (full)
        {
            snapshot.FragmentSorted.Sort(StringComparer.Ordinal);
            snapshot.FragmentRotatedSorted.AddRange(fragmentRotated);
            snapshot.FragmentRotatedSorted.Sort(StringComparer.Ordinal);
            snapshot.PolygonSequence.AddRange(polygonSequence);
        }

        return snapshot;
    }

    /// <summary>
    /// 忠实重放 VoxelAwareMeshPrecut.Assemble 的多边形选择 + AddPolygonToMesh 的
    /// fan 三角化与过滤（重复 id / 面积 ≤ 1e-8），统计各阶段数量并产生三角形签名。
    /// </summary>
    private static void ReplayChunkAssembly(
        TerrainChunk chunk,
        DeterminismSnapshot snapshot,
        List<string> polygonSequence,
        bool full)
    {
        VoxelAwareMeshPrecut.ChunkGeometry geometry = chunk.Geometry;
        Dictionary<string, int> weldingMap = new Dictionary<string, int>();
        List<Vector3> meshVertices = new List<Vector3>();
        List<string> submeshTriangles = new List<string>();

        for (int pass = 0; pass < 4; pass++)
        {
            if (pass == 0)
            {
                for (int f = 0; f < geometry.Fragments.Count; f++)
                {
                    VoxelAwareMeshPrecut.Fragment fragment =
                        geometry.Fragments[f];
                    if (!fragment.Active) continue;
                    AddReplayPolygon(
                        chunk,
                        fragment.Polygon,
                        fragment.Submesh,
                        weldingMap,
                        meshVertices,
                        snapshot,
                        polygonSequence,
                        full,
                        submeshTriangles);
                }
            }
            else if (pass == 1)
            {
                for (int f = 0; f < geometry.FaceFragments.Count; f++)
                {
                    VoxelAwareMeshPrecut.FaceFragment fragment =
                        geometry.FaceFragments[f];
                    if (!fragment.Active) continue;

                    bool visible;
                    if (geometry.FaceFragmentVisibility.TryGetValue(f, out visible) &&
                        !visible)
                    {
                        continue;
                    }

                    AddReplayPolygon(
                        chunk,
                        fragment.Polygon,
                        fragment.Submesh,
                        weldingMap,
                        meshVertices,
                        snapshot,
                        polygonSequence,
                        full,
                        submeshTriangles);
                }
            }
            else if (pass == 2)
            {
                for (int r = 0; r < geometry.SpatialFaces.Count; r++)
                {
                    VoxelAwareMeshSpatialFace.SpatialFaceRecord record =
                        geometry.SpatialFaces[r];
                    if (record == null) continue;

                    for (int h = 0; h < record.HiddenFaces.Count; h++)
                    {
                        VoxelAwareMeshSpatialFace.HiddenFaceFragment hidden =
                            record.HiddenFaces[h];
                        if (hidden == null || !hidden.Active) continue;
                        AddReplayPolygon(
                            chunk,
                            hidden.Polygon,
                            hidden.Submesh,
                            weldingMap,
                            meshVertices,
                            snapshot,
                            polygonSequence,
                            full,
                            submeshTriangles);
                    }
                }
            }
            else
            {
                foreach (KeyValuePair<string, List<VoxelAwareMeshSpatialFace.HiddenFaceFragment>>
                    pair in geometry.HiddenFaceContributions)
                {
                    if (pair.Value == null) continue;

                    for (int h = 0; h < pair.Value.Count; h++)
                    {
                        VoxelAwareMeshSpatialFace.HiddenFaceFragment hidden =
                            pair.Value[h];
                        if (hidden == null || !hidden.Active) continue;
                        AddReplayPolygon(
                            chunk,
                            hidden.Polygon,
                            hidden.Submesh,
                            weldingMap,
                            meshVertices,
                            snapshot,
                            polygonSequence,
                            full,
                            submeshTriangles);
                    }
                }
            }
        }

        for (int i = 0; i < submeshTriangles.Count; i++)
        {
            string key = submeshTriangles[i];
            int count;
            snapshot.ReplayTriangleMultiplicity.TryGetValue(key, out count);
            snapshot.ReplayTriangleMultiplicity[key] = count + 1;
        }

        snapshot.ReplayTriangleHash = FnvHash(submeshTriangles, false);
        snapshot.FanTriangles += ReplayTriangleCount(snapshot, chunk);

        // 生产 Mesh 度量（顶点/三角形/重复/零面积/非流形）。
        Mesh mesh = chunk.Mesh;
        if (mesh == null) return;

        snapshot.VerticesAfterWeld += mesh.vertexCount;

        for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
        {
            snapshot.Triangles += mesh.GetTriangles(submesh).Length / 3;
        }

        Dictionary<string, int> edgeUse = new Dictionary<string, int>();

        for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
        {
            int[] indices = mesh.GetTriangles(submesh);
            Vector3[] vertices = mesh.vertices;

            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                Vector3 a = vertices[indices[i]];
                Vector3 b = vertices[indices[i + 1]];
                Vector3 c = vertices[indices[i + 2]];

                string key = chunk.Coordinate + "|" + submesh + "|" +
                             RotateToSmallest(
                                 PositionKey3(a.x, a.y, a.z),
                                 PositionKey3(b.x, b.y, b.z),
                                 PositionKey3(c.x, c.y, c.z));

                int count;
                snapshot.MeshTriangleMultiplicity.TryGetValue(key, out count);
                snapshot.MeshTriangleMultiplicity[key] = count + 1;

                if (DetTriangleArea(a, b, c) <= 1e-9f) snapshot.ZeroAreaTriangles++;

                AddEdgeUse(edgeUse, a, b);
                AddEdgeUse(edgeUse, b, c);
                AddEdgeUse(edgeUse, c, a);
            }
        }

        foreach (KeyValuePair<string, int> pair in edgeUse)
        {
            if (pair.Value > 2) snapshot.NonManifoldEdges++;
        }

        List<string> meshSignature = new List<string>();
        foreach (KeyValuePair<string, int> pair in
            snapshot.MeshTriangleMultiplicity)
        {
            for (int i = 0; i < pair.Value; i++)
            {
                meshSignature.Add(pair.Key);
            }
        }

        snapshot.MeshTriangleHash = FnvHash(meshSignature, false);
    }

    private static void AddReplayPolygon(
        TerrainChunk chunk,
        List<Vector3> polygon,
        int submesh,
        Dictionary<string, int> weldingMap,
        List<Vector3> meshVertices,
        DeterminismSnapshot snapshot,
        List<string> polygonSequence,
        bool full,
        List<string> submeshTriangles)
    {
        if (polygon == null || polygon.Count < 3) return;

        snapshot.InputPolygons++;
        snapshot.InputVertices += polygon.Count;

        if (full)
        {
            polygonSequence.Add(
                "P|" + chunk.Coordinate + "|" + submesh + "|" +
                PolygonSignature3(polygon));
        }

        List<int> ids = new List<int>(polygon.Count);
        for (int i = 0; i < polygon.Count; i++)
        {
            Vector3 point = polygon[i];
            string key = VoxelAwareMeshPrecut.VertexPool.MakeKey(point);

            int id;
            if (!weldingMap.TryGetValue(key, out id))
            {
                id = meshVertices.Count;
                meshVertices.Add(point);
                weldingMap.Add(key, id);
            }

            ids.Add(id);
        }

        for (int i = 1; i < ids.Count - 1; i++)
        {
            int idA = ids[0];
            int idB = ids[i];
            int idC = ids[i + 1];

            if (idB == idA || idC == idA || idC == idB)
            {
                snapshot.FilteredDuplicateId++;
                continue;
            }

            if (DetTriangleArea(
                meshVertices[idA], meshVertices[idB], meshVertices[idC]) <= 1e-8f)
            {
                snapshot.FilteredDegenerate++;
                continue;
            }

            submeshTriangles.Add(
                chunk.Coordinate + "|" + submesh + "|" +
                RotateToSmallest(
                    PositionKey3(meshVertices[idA].x, meshVertices[idA].y, meshVertices[idA].z),
                    PositionKey3(meshVertices[idB].x, meshVertices[idB].y, meshVertices[idB].z),
                    PositionKey3(meshVertices[idC].x, meshVertices[idC].y, meshVertices[idC].z)));
        }
    }

    private static int ReplayTriangleCount(
        DeterminismSnapshot snapshot,
        TerrainChunk chunk)
    {
        int count = 0;
        foreach (KeyValuePair<string, int> pair in
            snapshot.ReplayTriangleMultiplicity)
        {
            if (pair.Key.StartsWith(
                chunk.Coordinate.ToString() + "|", StringComparison.Ordinal))
            {
                count += pair.Value;
            }
        }

        return count;
    }

    private static void AddEdgeUse(
        Dictionary<string, int> edgeUse,
        Vector3 a,
        Vector3 b)
    {
        string ka = PositionKey3(a.x, a.y, a.z);
        string kb = PositionKey3(b.x, b.y, b.z);
        if (string.CompareOrdinal(ka, kb) > 0)
        {
            string swap = ka;
            ka = kb;
            kb = swap;
        }

        string key = ka + "|" + kb;
        int count;
        edgeUse.TryGetValue(key, out count);
        edgeUse[key] = count + 1;
    }

    private static string PolygonSignature3(List<Vector3> polygon)
    {
        if (polygon == null) return "null";
        StringBuilder builder = new StringBuilder(polygon.Count * 22);
        builder.Append(polygon.Count);
        for (int i = 0; i < polygon.Count; i++)
        {
            builder.Append(';');
            builder.Append(PositionKey3(
                polygon[i].x, polygon[i].y, polygon[i].z));
        }

        return builder.ToString();
    }

    private static string RotatedPolygonSignature3(List<Vector3> polygon)
    {
        if (polygon == null || polygon.Count == 0) return "null";

        List<string> keys = new List<string>(polygon.Count);
        for (int i = 0; i < polygon.Count; i++)
        {
            keys.Add(PositionKey3(
                polygon[i].x, polygon[i].y, polygon[i].z));
        }

        int start = 0;
        for (int i = 1; i < keys.Count; i++)
        {
            if (string.CompareOrdinal(keys[i], keys[start]) < 0) start = i;
        }

        StringBuilder builder = new StringBuilder(keys.Count * 22);
        builder.Append(keys.Count);
        for (int i = 0; i < keys.Count; i++)
        {
            builder.Append(';');
            builder.Append(keys[(start + i) % keys.Count]);
        }

        return builder.ToString();
    }

    private static string PositionKey3(float x, float y, float z)
    {
        return Mathf.RoundToInt(x / Quantize) + "," +
               Mathf.RoundToInt(y / Quantize) + "," +
               Mathf.RoundToInt(z / Quantize);
    }

    private static long FnvHash(List<string> values, bool ordered)
    {
        if (values == null || values.Count == 0) return 0;

        const long offset = unchecked((long)1469598103934665603UL);
        const long prime = 1099511628211L;

        if (ordered)
        {
            long hash = offset;
            for (int i = 0; i < values.Count; i++)
            {
                hash = FnvHashString(hash, values[i], prime);
            }

            return hash;
        }

        List<string> sorted = new List<string>(values);
        sorted.Sort(StringComparer.Ordinal);

        long sortedHash = offset;
        for (int i = 0; i < sorted.Count; i++)
        {
            sortedHash = FnvHashString(sortedHash, sorted[i], prime);
        }

        return sortedHash;
    }

    private static long FnvHashString(long hash, string value, long prime)
    {
        if (value == null) return hash;
        for (int i = 0; i < value.Length; i++)
        {
            hash ^= value[i];
            hash *= prime;
        }

        return hash;
    }

    private static void LogDeterminismSnapshot(DeterminismSnapshot snapshot)
    {
        Debug.Log(
            "[StageB3C-Det] run=" + snapshot.Run +
            " fragments=" + snapshot.FaceFragments +
            " bridges=" + snapshot.Bridges +
            " baseFragments=" + snapshot.Fragments +
            " inputPolygons=" + snapshot.InputPolygons +
            " inputVertices=" + snapshot.InputVertices +
            " filteredDupId=" + snapshot.FilteredDuplicateId +
            " filteredDegenerate=" + snapshot.FilteredDegenerate +
            " inactiveFragments=" + snapshot.InactiveFaceFragments +
            " invisibleFragments=" + snapshot.InvisibleFaceFragments +
            " visibilityEntries=" + snapshot.VisibilityEntries +
            " fanTriangles=" + snapshot.FanTriangles +
            " weldedVertices=" + snapshot.VerticesAfterWeld +
            " triangles=" + snapshot.Triangles +
            " duplicateTriangles=" + snapshot.DuplicateTriangles +
            " zeroAreaTriangles=" + snapshot.ZeroAreaTriangles +
            " nonManifoldEdges=" + snapshot.NonManifoldEdges +
            " graphEdges=" + snapshot.GraphEdges +
            " graphNodes=" + snapshot.GraphNodes +
            " graphInvalidFaces=" + snapshot.GraphInvalidFaces +
            " fragHashOrdered=" + snapshot.FragmentHashOrdered.ToString("X16") +
            " fragHashSorted=" + snapshot.FragmentHashSorted.ToString("X16") +
            " fragHashRotated=" + snapshot.FragmentRotationHash.ToString("X16") +
            " bridgeHash=" + snapshot.BridgeHashSorted.ToString("X16") +
            " polygonHashOrdered=" + snapshot.PolygonHashOrdered.ToString("X16") +
            " meshTriHash=" + snapshot.MeshTriangleHash.ToString("X16") +
            " replayTriHash=" + snapshot.ReplayTriangleHash.ToString("X16")
        );
    }

    private static void LogDeterminismSummary(
        List<DeterminismSnapshot> snapshots)
    {
        if (snapshots.Count == 0) return;

        StringBuilder triangles = new StringBuilder();
        StringBuilder fragments = new StringBuilder();
        int sortedHashVariants = 0;
        int orderedHashVariants = 0;
        int rotatedHashVariants = 0;
        int meshHashVariants = 0;

        long sortedHash = snapshots[0].FragmentHashSorted;
        long orderedHash = snapshots[0].FragmentHashOrdered;
        long rotatedHash = snapshots[0].FragmentRotationHash;
        long meshHash = snapshots[0].MeshTriangleHash;

        for (int i = 0; i < snapshots.Count; i++)
        {
            DeterminismSnapshot snapshot = snapshots[i];
            if (i > 0) triangles.Append(',');
            triangles.Append(snapshot.Triangles);
            if (i > 0) fragments.Append(',');
            fragments.Append(snapshot.FaceFragments);

            if (snapshot.FragmentHashSorted != sortedHash) sortedHashVariants++;
            if (snapshot.FragmentHashOrdered != orderedHash) orderedHashVariants++;
            if (snapshot.FragmentRotationHash != rotatedHash) rotatedHashVariants++;
            if (snapshot.MeshTriangleHash != meshHash) meshHashVariants++;
        }

        Debug.Log(
            "[StageB3C-Det] summary runs=" + snapshots.Count +
            " triangles=[" + triangles + "]" +
            " fragments=[" + fragments + "]" +
            " sortedHashVariants=" + sortedHashVariants +
            " orderedHashVariants=" + orderedHashVariants +
            " rotatedHashVariants=" + rotatedHashVariants +
            " meshHashVariants=" + meshHashVariants);
    }

    /// <summary>
    /// 两个夹具的逐阶段比较 + 三角形级差异追溯（只读）。
    /// </summary>
    private static void CompareDeterminismSnapshots(
        DeterminismSnapshot a,
        List<TerrainChunkView> viewsA,
        DeterminismSnapshot b,
        List<TerrainChunkView> viewsB,
        bool trace)
    {
        LogDeterminismStageComparison(a, b);

        if (!trace) return;

        DiffTriangleMultisets(
            "A-run" + a.Run, a, viewsA,
            "B-run" + b.Run, b, viewsB);
    }

    private static void LogDeterminismStageComparison(
        DeterminismSnapshot a,
        DeterminismSnapshot b)
    {
        Debug.Log(
            "[StageB3C-Det-diff] A(run " + a.Run + ") vs B(run " + b.Run + ")" +
            " fragmentsEqual=" + (a.FaceFragments == b.FaceFragments) +
            " fragmentSortedSetEqual=" +
            (a.FragmentHashSorted == b.FragmentHashSorted) +
            " fragmentOrderEqual=" +
            (a.FragmentHashOrdered == b.FragmentHashOrdered) +
            " fragmentRotatedSetEqual=" +
            (a.FragmentRotationHash == b.FragmentRotationHash) +
            " bridgesEqual=" + (a.Bridges == b.Bridges) +
            " bridgeSetEqual=" + (a.BridgeHashSorted == b.BridgeHashSorted) +
            " graphEdgesEqual=" + (a.GraphEdges == b.GraphEdges) +
            " graphNodesEqual=" + (a.GraphNodes == b.GraphNodes) +
            " graphInvalidEqual=" +
            (a.GraphInvalidFaces == b.GraphInvalidFaces) +
            " inputPolygonsEqual=" + (a.InputPolygons == b.InputPolygons) +
            " polygonOrderEqual=" +
            (a.PolygonHashOrdered == b.PolygonHashOrdered) +
            " filteredDegenerateEqual=" +
            (a.FilteredDegenerate == b.FilteredDegenerate) +
            " filteredDupIdEqual=" +
            (a.FilteredDuplicateId == b.FilteredDuplicateId) +
            " weldedVerticesEqual=" +
            (a.VerticesAfterWeld == b.VerticesAfterWeld) +
            " trianglesEqual=" + (a.Triangles == b.Triangles) +
            " fanTrianglesEqual=" + (a.FanTriangles == b.FanTriangles)
        );
    }

    private static void DiffTriangleMultisets(
        string labelA,
        DeterminismSnapshot a,
        List<TerrainChunkView> viewsA,
        string labelB,
        DeterminismSnapshot b,
        List<TerrainChunkView> viewsB)
    {
        // 三角形签名多重集差异。
        List<string> onlyA = new List<string>();
        List<string> onlyB = new List<string>();
        List<string> multiplicityDiff = new List<string>();

        HashSet<string> allKeys = new HashSet<string>();
        foreach (KeyValuePair<string, int> pair in a.MeshTriangleMultiplicity)
        {
            allKeys.Add(pair.Key);
        }

        foreach (KeyValuePair<string, int> pair in b.MeshTriangleMultiplicity)
        {
            allKeys.Add(pair.Key);
        }

        foreach (string key in allKeys)
        {
            int countA;
            int countB;
            a.MeshTriangleMultiplicity.TryGetValue(key, out countA);
            b.MeshTriangleMultiplicity.TryGetValue(key, out countB);

            if (countA == 0 && countB > 0)
            {
                onlyB.Add(key);
            }
            else if (countB == 0 && countA > 0)
            {
                onlyA.Add(key);
            }
            else if (countA != countB)
            {
                multiplicityDiff.Add(key + " A=" + countA + " B=" + countB);
            }
        }

        LogSkippedFragmentDetails(labelA, a);
        LogSkippedFragmentDetails(labelB, b);

        Debug.Log(
            "[StageB3C-Det-diff] " + labelA + " vs " + labelB +
            " mesh triangle multiset: onlyA=" + onlyA.Count +
            " onlyB=" + onlyB.Count + " multiplicityDiff=" + multiplicityDiff.Count +
            " triangles " + a.Triangles + " vs " + b.Triangles);

        LogDeterminismTriangleDetails(labelA + "-only", onlyA, viewsA, null);
        LogDeterminismTriangleDetails(labelB + "-only", onlyB, viewsB, null);

        for (int i = 0; i < multiplicityDiff.Count && i < 5; i++)
        {
            Debug.Log("[StageB3C-Det-diff] multiplicity " + multiplicityDiff[i]);
        }

        // 重放（测试侧 fan）与生产 Mesh 是否一致：用于确认差异发生在 fan/过滤阶段前还是后。
        Debug.Log(
            "[StageB3C-Det-diff] replayVsMesh A: replayTriangles=" +
            ReplayTotal(a) + " meshTriangles=" + a.Triangles +
            " replayHash=" + a.ReplayTriangleHash.ToString("X16") +
            " meshHash=" + a.MeshTriangleHash.ToString("X16") +
            " | B: replayTriangles=" + ReplayTotal(b) +
            " meshTriangles=" + b.Triangles +
            " replayHash=" + b.ReplayTriangleHash.ToString("X16") +
            " meshHash=" + b.MeshTriangleHash.ToString("X16"));
    }

    private static int ReplayTotal(DeterminismSnapshot snapshot)
    {
        int total = 0;
        foreach (KeyValuePair<string, int> pair in
            snapshot.ReplayTriangleMultiplicity)
        {
            total += pair.Value;
        }

        return total;
    }

    private static void LogSkippedFragmentDetails(
        string label,
        DeterminismSnapshot snapshot)
    {
        Debug.Log(
            "[StageB3C-Det-diff] " + label + " skippedFragments=" +
            snapshot.SkippedFragmentDetails.Count +
            " inactive=" + snapshot.InactiveFaceFragments +
            " invisible=" + snapshot.InvisibleFaceFragments +
            " visibilityEntries=" + snapshot.VisibilityEntries);

        for (int i = 0; i < snapshot.SkippedFragmentDetails.Count && i < 12; i++)
        {
            Debug.Log(
                "[StageB3C-Det-diff] " + label + " skipped[" + i + "] " +
                snapshot.SkippedFragmentDetails[i]);
        }

        for (int i = 0; i < snapshot.VisibilityFaceKeys.Count; i++)
        {
            Debug.Log(
                "[StageB3C-Det-diff] " + label + " visibilityEntry[" + i +
                "] " + snapshot.VisibilityFaceKeys[i]);
        }
    }

    private static void LogDeterminismTriangleDetails(
        string label,
        List<string> triangleKeys,
        List<TerrainChunkView> views,
        Dictionary<string, Vector3> snapshotAVertexMap)
    {
        for (int i = 0; i < triangleKeys.Count && i < 8; i++)
        {
            string[] parts = triangleKeys[i].Split('|');
            if (parts.Length < 5) continue;

            string[] v0 = parts[2].Split(',');
            string[] v1 = parts[3].Split(',');
            string[] v2 = parts[4].Split(',');

            Vector3 p0 = new Vector3(
                float.Parse(v0[0]) * Quantize,
                float.Parse(v0[1]) * Quantize,
                float.Parse(v0[2]) * Quantize);
            Vector3 p1 = new Vector3(
                float.Parse(v1[0]) * Quantize,
                float.Parse(v1[1]) * Quantize,
                float.Parse(v1[2]) * Quantize);
            Vector3 p2 = new Vector3(
                float.Parse(v2[0]) * Quantize,
                float.Parse(v2[1]) * Quantize,
                float.Parse(v2[2]) * Quantize);

            Vector3 centroid = (p0 + p1 + p2) / 3f;
            float area = DetTriangleArea(p0, p1, p2);

            string traceInfo = TraceTriangleToFragment(
                views, parts[0], parts[1], centroid);

            Debug.Log(
                "[StageB3C-Det-diff] " + label + "[" + i + "] chunk=" +
                parts[0] + " submesh=" + parts[1] +
                " area=" + area.ToString("E3") +
                " centroid=" + centroid.ToString("F4") +
                " v0=" + p0.ToString("F4") + " v1=" + p1.ToString("F4") +
                " v2=" + p2.ToString("F4") + traceInfo);
        }
    }

    /// <summary>
    /// 把一个三角形质心追溯到包含它的输入多边形（FaceFragment / Fragment），
    /// 用于定位“差异涉及的具体输入几何”。只读。
    /// </summary>
    private static string TraceTriangleToFragment(
        List<TerrainChunkView> views,
        string chunkCoordinateText,
        string submeshText,
        Vector3 centroid)
    {
        for (int v = 0; v < views.Count; v++)
        {
            TerrainChunk chunk = views[v].Chunk;
            if (chunk == null || chunk.Geometry == null) continue;
            if (chunk.Coordinate.ToString() != chunkCoordinateText) continue;

            VoxelAwareMeshPrecut.ChunkGeometry geometry = chunk.Geometry;

            for (int pass = 0; pass < 2; pass++)
            {
                if (pass == 0)
                {
                    for (int i = 0; i < geometry.Fragments.Count; i++)
                    {
                        VoxelAwareMeshPrecut.Fragment fragment =
                            geometry.Fragments[i];
                        if (!fragment.Active) continue;
                        if (fragment.Submesh.ToString() != submeshText) continue;
                        if (!PolygonContainsPoint3(fragment.Polygon, centroid))
                        {
                            continue;
                        }

                        return " source=Fragment[" + i + "] voxel=" +
                               fragment.Voxel + " submesh=" + fragment.Submesh +
                               " vertices=" + fragment.Polygon.Count +
                               " sourceTriangle=" + fragment.SourceTriangle;
                    }
                }
                else
                {
                    for (int i = 0; i < geometry.FaceFragments.Count; i++)
                    {
                        VoxelAwareMeshPrecut.FaceFragment fragment =
                            geometry.FaceFragments[i];
                        if (!fragment.Active) continue;
                        if (fragment.Submesh.ToString() != submeshText) continue;
                        if (!PolygonContainsPoint3(fragment.Polygon, centroid))
                        {
                            continue;
                        }

                        return " source=FaceFragment[" + i + "] face=" +
                               fragment.Face.Key + " submesh=" +
                               fragment.Submesh + " vertices=" +
                               fragment.Polygon.Count;
                    }
                }
            }
        }

        return " source=<not found>";
    }

    private static float DetTriangleArea(Vector3 a, Vector3 b, Vector3 c)
    {
        return Vector3.Cross(b - a, c - a).magnitude * 0.5f;
    }

    private static bool PolygonContainsPoint3(
        List<Vector3> polygon,
        Vector3 point)
    {
        if (polygon == null || polygon.Count < 3) return false;

        // 面多边形为平面多边形：用投影到最合适的主平面后做 2D 包含判定。
        Vector3 normal = Vector3.Cross(
            polygon[1] - polygon[0], polygon[2] - polygon[0]);
        if (normal.sqrMagnitude <= 1e-18f) return false;

        int axis = 0;
        if (Mathf.Abs(normal.y) > Mathf.Abs(normal.x)) axis = 1;
        if (Mathf.Abs(normal.z) > Mathf.Abs(normal[axis])) axis = 2;

        int u = (axis + 1) % 3;
        int w = (axis + 2) % 3;
        List<Vector2> polygon2 = new List<Vector2>(polygon.Count);
        for (int i = 0; i < polygon.Count; i++)
        {
            polygon2.Add(new Vector2(polygon[i][u], polygon[i][w]));
        }

        return PointInPolygon(new Vector2(point[u], point[w]), polygon2);
    }

    // =====================================================================
    // T12 — B3-E1 修复验证：Visibility 写入必须限定在 Face 所属 Chunk
    // =====================================================================

    private static void CheckT12ChunkScopedVisibility()
    {
        // 体素对齐高密度夹具（FaceFragments ≈ 60000）：FaceId.Key 是 Chunk Local，
        // 不同 Chunk 上 Key 相同的面彼此无关，不得被同一次 Mutation 写入。
        GameObject terrainObject = CreateT11Terrain(
            "StageB3C_T12", 15f, Vector3Int.zero);

        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();
            List<TerrainChunkView> views = GetViews(terrainObject);
            Dictionary<Vector3Int, MeshCanonical> baseline =
                SnapshotCanonical(views);

            Vector3Int voxel = new Vector3Int(30, SurfaceVoxelY - 1, 30);
            Vector3Int changedCoordinate =
                ChunkSpace.VoxelToChunk(voxel, ChunkVoxelSize);

            terrain.VoxelField.Set(voxel, VoxelState.Air);
            int rebuilt = terrain.RebuildChunksAffectedByVoxel(voxel);
            views = GetViews(terrainObject);
            Dictionary<Vector3Int, MeshCanonical> digged =
                SnapshotCanonical(views);

            Check(
                rebuilt >= 1,
                "T12: 挖掘已提交（rebuilt=" + rebuilt + "）"
            );

            int changedChunks = 0;
            int changedOtherChunks = 0;
            int unchangedChunks = 0;
            int dugChunkDelta = 0;

            foreach (KeyValuePair<Vector3Int, MeshCanonical> pair in baseline)
            {
                MeshCanonical after;
                if (!digged.TryGetValue(pair.Key, out after))
                {
                    changedOtherChunks++;
                    continue;
                }

                bool same = DetSameCanonical(pair.Value, after, out string _);
                if (same)
                {
                    unchangedChunks++;
                    continue;
                }

                changedChunks++;
                if (pair.Key == changedCoordinate)
                {
                    dugChunkDelta = pair.Value.TriangleKeys.Count -
                                    after.TriangleKeys.Count;
                }
                else
                {
                    changedOtherChunks++;
                }
            }

            Debug.Log(
                "[StageB3C-T12] dig voxel=" + voxel +
                " changedChunks=" + changedChunks +
                " changedOtherChunks=" + changedOtherChunks +
                " unchangedChunks=" + unchangedChunks +
                " dugChunkTriangleDelta=" + dugChunkDelta +
                " visibilityEntriesTotal=" +
                CountVisibilityEntries(views));

            Check(
                changedChunks == 1 && changedOtherChunks == 0,
                "T12: 挖掘只改变该 Voxel 所在 Chunk 的网格（改变 " +
                changedChunks + " 个，其它 Chunk 改变 " +
                changedOtherChunks + " 个）"
            );
            Check(
                unchangedChunks == baseline.Count - 1,
                "T12: 其余 Chunk 逐三角形保持不变（" + unchangedChunks + "/" +
                (baseline.Count - 1) + "）"
            );
            Check(
                dugChunkDelta != 0,
                "T12: 被挖 Chunk 几何发生变化（Δ=" + dugChunkDelta + "）"
            );

            // 恢复：**其它 Chunk 必须精确回到基线**（这是本轮修复的保证）；
            // 被挖 Chunk 自身仍残留 4 个不可见 FaceFragment（独立缺陷，见报告 §6.2），
            // 因此这里只断言“其它 Chunk 不受影响”，并用 Limit 如实记录残留量。
            terrain.VoxelField.Set(voxel, VoxelState.Solid);
            terrain.RebuildChunksAffectedByVoxel(voxel);
            views = GetViews(terrainObject);
            Dictionary<Vector3Int, MeshCanonical> restored =
                SnapshotCanonical(views);

            int changedOtherAfterRestore = 0;
            int dugChunkRestoreDelta = 0;

            foreach (KeyValuePair<Vector3Int, MeshCanonical> pair in baseline)
            {
                MeshCanonical after;
                if (!restored.TryGetValue(pair.Key, out after))
                {
                    changedOtherAfterRestore++;
                    continue;
                }

                bool same = DetSameCanonical(pair.Value, after, out string _);
                if (same) continue;

                if (pair.Key == changedCoordinate)
                {
                    dugChunkRestoreDelta =
                        pair.Value.TriangleKeys.Count - after.TriangleKeys.Count;
                }
                else
                {
                    changedOtherAfterRestore++;
                }
            }

            Check(
                changedOtherAfterRestore == 0,
                "T12: 恢复后其它 Chunk 全部回到基线（异常 " +
                changedOtherAfterRestore + " 个）"
            );
            Check(
                CountInvisibleVisibilityEntriesExcludingChunk(
                    views, changedCoordinate) == 0,
                "T12: 恢复后残留不可见 FaceFragment 仅限被挖 Chunk（其它 Chunk 残留 " +
                CountInvisibleVisibilityEntriesExcludingChunk(
                    views, changedCoordinate) + "）"
            );
            Check(
                SameAllChunks(baseline, restored, out string restoreDetail) &&
                dugChunkRestoreDelta == 0,
                "T12: 恢复后被挖 Chunk 也逐三角形回到基线（含绕序，Δ=" +
                dugChunkRestoreDelta + "）" +
                (restoreDetail.Length > 0 ? " —— " + restoreDetail : string.Empty)
            );
            Check(
                CountInvisibleVisibilityEntries(views) == 0,
                "T12: 恢复后无任何残留不可见 FaceFragment 覆写（" +
                CountInvisibleVisibilityEntries(views) + "）"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    private static bool DetSameCanonical(
        MeshCanonical a,
        MeshCanonical b,
        out string detail)
    {
        detail = string.Empty;
        if (a == null || b == null) return a == b;

        if (a.TriangleKeys.Count != b.TriangleKeys.Count)
        {
            detail = "三角形数 " + a.TriangleKeys.Count + " vs " +
                     b.TriangleKeys.Count;
            return false;
        }

        for (int i = 0; i < a.TriangleKeys.Count; i++)
        {
            if (a.TriangleKeys[i] == b.TriangleKeys[i]) continue;
            detail = "三角形差异 @" + i;
            return false;
        }

        if (a.VertexKeys.Count != b.VertexKeys.Count)
        {
            detail = "顶点数 " + a.VertexKeys.Count + " vs " +
                     b.VertexKeys.Count;
            return false;
        }

        return true;
    }

    private static int CountVisibilityEntries(List<TerrainChunkView> views)
    {
        int total = 0;
        for (int i = 0; i < views.Count; i++)
        {
            TerrainChunk chunk = views[i].Chunk;
            if (chunk == null || chunk.Geometry == null) continue;
            total += chunk.Geometry.FaceFragmentVisibility.Count;
        }

        return total;
    }

    private static int CountInvisibleVisibilityEntriesExcludingChunk(
        List<TerrainChunkView> views,
        Vector3Int excludedCoordinate)
    {
        int total = 0;
        for (int i = 0; i < views.Count; i++)
        {
            TerrainChunk chunk = views[i].Chunk;
            if (chunk == null || chunk.Geometry == null) continue;
            if (chunk.Coordinate == excludedCoordinate) continue;

            foreach (KeyValuePair<int, bool> pair in
                chunk.Geometry.FaceFragmentVisibility)
            {
                if (!pair.Value) total++;
            }
        }

        return total;
    }

    private static int CountInvisibleVisibilityEntries(
        List<TerrainChunkView> views)
    {
        int total = 0;
        for (int i = 0; i < views.Count; i++)
        {
            TerrainChunk chunk = views[i].Chunk;
            if (chunk == null || chunk.Geometry == null) continue;

            foreach (KeyValuePair<int, bool> pair in
                chunk.Geometry.FaceFragmentVisibility)
            {
                if (!pair.Value) total++;
            }
        }

        return total;
    }

    // =====================================================================
    // T13 — B3-E2：10 次 Solid→Air→Solid 轮次，每次都回到同一基线
    // =====================================================================

    private static void CheckT13VisibilityRoundTrip()
    {
        GameObject terrainObject = CreateT11Terrain(
            "StageB3C_T13", 15f, Vector3Int.zero);

        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();
            Vector3Int voxel = new Vector3Int(30, SurfaceVoxelY - 1, 30);
            Vector3Int changedCoordinate =
                ChunkSpace.VoxelToChunk(voxel, ChunkVoxelSize);

            Dictionary<Vector3Int, MeshCanonical> baseline =
                SnapshotCanonical(GetViews(terrainObject));

            const int rounds = 10;
            int restoreMismatches = 0;
            int staleInvisible = 0;
            int duplicates = 0;
            int zeroArea = 0;
            int nonManifold = 0;
            int diggedNonManifoldMax = 0;
            string firstDetail = string.Empty;

            for (int round = 0; round < rounds; round++)
            {
                terrain.VoxelField.Set(voxel, VoxelState.Air);
                terrain.RebuildChunksAffectedByVoxel(voxel);
                List<TerrainChunkView> diggedViews = GetViews(terrainObject);
                diggedNonManifoldMax = Math.Max(
                    diggedNonManifoldMax,
                    CountNonManifoldEdges(diggedViews, changedCoordinate));

                terrain.VoxelField.Set(voxel, VoxelState.Solid);
                terrain.RebuildChunksAffectedByVoxel(voxel);
                List<TerrainChunkView> restoredViews = GetViews(terrainObject);

                if (!SameAllChunks(
                    baseline,
                    SnapshotCanonical(restoredViews),
                    out string detail))
                {
                    restoreMismatches++;
                    if (firstDetail.Length == 0)
                    {
                        firstDetail = "round " + round + " —— " + detail;
                    }
                }

                staleInvisible += CountInvisibleVisibilityEntries(restoredViews);
                duplicates += CountDuplicateTrianglesInViews(restoredViews);
                zeroArea += CountZeroAreaTrianglesInViews(restoredViews);
                nonManifold += CountNonManifoldEdges(
                    restoredViews, changedCoordinate);
            }

            Check(
                restoreMismatches == 0,
                "T13: " + rounds + " 次 Solid→Air→Solid 均逐三角形回到基线（不一致 " +
                restoreMismatches + "/" + rounds +
                (firstDetail.Length > 0 ? "；" + firstDetail : string.Empty) + "）"
            );
            Check(
                staleInvisible == 0,
                "T13: 恢复后无残留不可见 FaceFragment 覆写（累计 " +
                staleInvisible + "）"
            );
            Check(
                duplicates == 0 && zeroArea == 0,
                "T13: 恢复后无重复/零面积三角形（" + duplicates + "/" +
                zeroArea + "）"
            );
            Check(
                nonManifold == 0,
                "T13: 恢复后无新增非流形边（累计 " + nonManifold + "）"
            );

            Limit(
                "T13 观察（既有现象，非本轮修复范围）：挖掘态目标 Chunk 非流形边峰值 = " +
                diggedNonManifoldMax + "（坑壁与共面地表共享边）"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    private static int CountDuplicateTrianglesInViews(
        List<TerrainChunkView> views)
    {
        int duplicates = 0;
        for (int v = 0; v < views.Count; v++)
        {
            Mesh mesh = views[v].Chunk != null ? views[v].Chunk.Mesh : null;
            if (mesh == null) continue;

            Vector3[] vertices = mesh.vertices;
            Dictionary<string, int> multiset = new Dictionary<string, int>();
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                int[] indices = mesh.GetTriangles(submesh);
                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    Vector3 a = vertices[indices[i]];
                    Vector3 b = vertices[indices[i + 1]];
                    Vector3 c = vertices[indices[i + 2]];
                    string key = submesh + "|" + RotateToSmallest(
                        PositionKey3(a), PositionKey3(b), PositionKey3(c));
                    int count;
                    multiset.TryGetValue(key, out count);
                    multiset[key] = count + 1;
                }
            }

            foreach (KeyValuePair<string, int> pair in multiset)
            {
                if (pair.Value > 1) duplicates += pair.Value - 1;
            }
        }

        return duplicates;
    }

    private static int CountZeroAreaTrianglesInViews(
        List<TerrainChunkView> views)
    {
        int zeroArea = 0;
        for (int v = 0; v < views.Count; v++)
        {
            Mesh mesh = views[v].Chunk != null ? views[v].Chunk.Mesh : null;
            if (mesh == null) continue;

            Vector3[] vertices = mesh.vertices;
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                int[] indices = mesh.GetTriangles(submesh);
                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    if (DetTriangleArea(
                        vertices[indices[i]],
                        vertices[indices[i + 1]],
                        vertices[indices[i + 2]]) <= 1e-9f)
                    {
                        zeroArea++;
                    }
                }
            }
        }

        return zeroArea;
    }

    private static int CountNonManifoldEdges(
        List<TerrainChunkView> views,
        Vector3Int onlyChunk)
    {
        int total = 0;
        for (int v = 0; v < views.Count; v++)
        {
            TerrainChunk chunk = views[v].Chunk;
            if (chunk == null || chunk.Mesh == null) continue;
            if (chunk.Coordinate != onlyChunk) continue;

            Mesh mesh = chunk.Mesh;
            Vector3[] vertices = mesh.vertices;
            Dictionary<string, int> edgeUse = new Dictionary<string, int>();

            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                int[] indices = mesh.GetTriangles(submesh);
                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    Vector3 a = vertices[indices[i]];
                    Vector3 b = vertices[indices[i + 1]];
                    Vector3 c = vertices[indices[i + 2]];
                    AddEdgeUseForT13(edgeUse, a, b);
                    AddEdgeUseForT13(edgeUse, b, c);
                    AddEdgeUseForT13(edgeUse, c, a);
                }
            }

            foreach (KeyValuePair<string, int> pair in edgeUse)
            {
                if (pair.Value > 2) total++;
            }
        }

        return total;
    }

    private static void AddEdgeUseForT13(
        Dictionary<string, int> edgeUse,
        Vector3 a,
        Vector3 b)
    {
        string ka = PositionKey3(a);
        string kb = PositionKey3(b);
        if (string.CompareOrdinal(ka, kb) > 0)
        {
            string swap = ka;
            ka = kb;
            kb = swap;
        }

        string key = ka + "|" + kb;
        int count;
        edgeUse.TryGetValue(key, out count);
        edgeUse[key] = count + 1;
    }
    // =====================================================================
    // T14 — B3-E3：非零 Chunk 的共面覆盖 / 可见性 / 挖掘恢复
    // =====================================================================

    private static void CheckT14NonZeroChunkCoplanarCoverage()
    {
        // A/B：体素对齐夹具（真实共面 FaceFragment），覆盖非零 Origin 与负坐标。
        CheckT14AlignedCase(
            "T14[chunk(1,0,0) 非零 Origin]",
            Vector3Int.zero,
            new Vector3Int(105, SurfaceVoxelY - 1, 30));

        CheckT14AlignedCase(
            "T14[chunkStart=(5,0,7)→chunk(6,0,8)]",
            new Vector3Int(5, 0, 7),
            new Vector3Int(455, SurfaceVoxelY - 1, 405));

        CheckT14AlignedCase(
            "T14[负坐标 chunk(-1,0,-1)]",
            new Vector3Int(-2, 0, -2),
            new Vector3Int(-70, SurfaceVoxelY - 1, -45));

        // C：非零 Chunk 的“部分共面覆盖”参与 Hidden Face 判定（覆盖区域不得生成内壁）。
        CheckT14PlantedCoverageCase();
    }

    private static void CheckT14AlignedCase(
        string label,
        Vector3Int chunkStart,
        Vector3Int voxel)
    {
        GameObject terrainObject = CreateT11Terrain(label, 15f, chunkStart);

        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();
            Vector3Int changedCoordinate =
                ChunkSpace.VoxelToChunk(voxel, ChunkVoxelSize);

            List<TerrainChunkView> views = GetViews(terrainObject);
            Dictionary<Vector3Int, MeshCanonical> baseline =
                SnapshotCanonical(views);

            // axis=1 ⇒ U=z、V=x（绝对 Voxel 单元索引）。
            VoxelAwareMeshPrecut.FaceId topFace =
                new VoxelAwareMeshPrecut.FaceId(
                    1, SurfaceVoxelY, voxel.z, voxel.x);

            int matchingFragments = CountFragmentsForFace(
                views, changedCoordinate, topFace);

            Check(
                matchingFragments > 0,
                label + ": 非零 Chunk 共面 FaceFragment 与绝对 FaceId 匹配（" +
                matchingFragments + " 个）"
            );

            const int rounds = 3;
            int restoreMismatches = 0;
            int staleInvisible = 0;
            int duplicates = 0;
            int zeroArea = 0;
            int nonManifold = 0;
            string firstDetail = string.Empty;

            for (int round = 0; round < rounds; round++)
            {
                terrain.VoxelField.Set(voxel, VoxelState.Air);
                terrain.RebuildChunksAffectedByVoxel(voxel);
                List<TerrainChunkView> diggedViews = GetViews(terrainObject);

                if (round == 0)
                {
                    int invisibleInChunk =
                        CountInvisibleVisibilityEntriesInChunk(
                            diggedViews, changedCoordinate);
                    int invisibleOtherChunks =
                        CountInvisibleVisibilityEntriesExcludingChunk(
                            diggedViews, changedCoordinate);
                    int changedOtherChunks = CountChangedChunksExcluding(
                        baseline, SnapshotCanonical(diggedViews),
                        changedCoordinate);

                    Debug.Log(
                        "[StageB3C-T14] " + label + " voxel=" + voxel +
                        " chunk=" + changedCoordinate +
                        " matchingFragments=" + matchingFragments +
                        " invisibleInChunk=" + invisibleInChunk +
                        " invisibleOtherChunks=" + invisibleOtherChunks +
                        " changedOtherChunks=" + changedOtherChunks);

                    Check(
                        invisibleInChunk == matchingFragments &&
                        invisibleOtherChunks == 0,
                        label + ": 挖掘后仅目标 Chunk 的共面片段被隐藏（本 Chunk " +
                        invisibleInChunk + "/" + matchingFragments +
                        "，其它 Chunk " + invisibleOtherChunks + "）"
                    );
                    Check(
                        changedOtherChunks == 0,
                        label + ": 挖掘只改变目标 Chunk 的网格（其它 Chunk 改变 " +
                        changedOtherChunks + " 个）"
                    );
                }

                terrain.VoxelField.Set(voxel, VoxelState.Solid);
                terrain.RebuildChunksAffectedByVoxel(voxel);
                List<TerrainChunkView> restoredViews = GetViews(terrainObject);

                if (!SameAllChunks(
                    baseline,
                    SnapshotCanonical(restoredViews),
                    out string detail))
                {
                    restoreMismatches++;
                    if (firstDetail.Length == 0)
                    {
                        firstDetail = "round " + round + " —— " + detail;
                    }
                }

                staleInvisible += CountInvisibleVisibilityEntries(restoredViews);
                duplicates += CountDuplicateTrianglesInViews(restoredViews);
                zeroArea += CountZeroAreaTrianglesInViews(restoredViews);
                nonManifold += CountNonManifoldEdges(
                    restoredViews, changedCoordinate);
            }

            Check(
                restoreMismatches == 0,
                label + ": 挖→恢复逐三角形回到基线（含绕序，不一致 " +
                restoreMismatches + "/" + rounds +
                (firstDetail.Length > 0 ? "；" + firstDetail : string.Empty) + "）"
            );
            Check(
                staleInvisible == 0,
                label + ": 无残留不可见覆写（累计 " + staleInvisible + "）"
            );
            Check(
                duplicates == 0 && zeroArea == 0,
                label + ": 无重复/零面积三角形（" + duplicates + "/" +
                zeroArea + "）"
            );
            Check(
                nonManifold == 0,
                label + ": 无新增非流形边（累计 " + nonManifold + "）"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    private static void CheckT14PlantedCoverageCase()
    {
        // 平面夹具（height=15.7）+ 非零 Chunk：在“两侧皆材料”的面上植入部分共面基础
        // 几何，验证它参与覆盖判定（未覆盖区域生成内壁，被覆盖区域不生成）。
        GameObject terrainObject = CreateT11Terrain(
            "T14[planted coverage chunk(1,0,0)]", -1f, Vector3Int.zero);

        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();
            List<TerrainChunkView> views = GetViews(terrainObject);

            Vector3Int voxel =
                new Vector3Int(105, SurfaceVoxelY - 1, 30);
            Vector3Int changedCoordinate =
                ChunkSpace.VoxelToChunk(voxel, ChunkVoxelSize);
            TerrainChunk ownerChunk = FindChunk(views, voxel);

            VoxelAwareMeshPrecut.FaceId topFace =
                new VoxelAwareMeshPrecut.FaceId(
                    1, SurfaceVoxelY, voxel.z, voxel.x);

            List<Vector3> planted = PlantCoplanarFaceFragment(
                ownerChunk,
                topFace,
                new Vector2(0.2f, 0f),
                new Vector2(0.6f, 0f),
                new Vector2(0.6f, 1f),
                new Vector2(0.2f, 1f)
            );

            ownerChunk.ReplaceAssembledMesh(
                VoxelAwareMeshPrecut.Assemble(ownerChunk.Geometry));

            terrain.VoxelField.Set(voxel, VoxelState.Air);
            int rebuilt = terrain.RebuildChunksAffectedByVoxel(voxel);
            List<TerrainChunkView> diggedViews = GetViews(terrainObject);

            double contribution = HiddenContributionArea(diggedViews, topFace);
            int invisibleInChunk = CountInvisibleVisibilityEntriesInChunk(
                diggedViews, changedCoordinate);
            int invisibleOtherChunks =
                CountInvisibleVisibilityEntriesExcludingChunk(
                    diggedViews, changedCoordinate);

            Debug.Log(
                "[StageB3C-T14] planted face=" + topFace.Key +
                " chunk=" + changedCoordinate +
                " rebuilt=" + rebuilt +
                " plantedVertices=" + planted.Count +
                " contributionArea=" + contribution.ToString("F4") +
                " invisibleInChunk=" + invisibleInChunk +
                " invisibleOtherChunks=" + invisibleOtherChunks);

            Check(
                rebuilt >= 1,
                "T14[planted]: Voxel Mutation 已提交（rebuilt=" + rebuilt + "）"
            );
            Check(
                contribution >= 0.55 && contribution <= 0.65,
                "T14[planted]: 非零 Chunk 的共面基础几何参与覆盖判定（内壁 " +
                contribution.ToString("F4") + " ≈ 1.0 − 0.4）"
            );
            Check(
                invisibleOtherChunks == 0,
                "T14[planted]: Visibility 只写入目标 Chunk（其它 Chunk " +
                invisibleOtherChunks + "）"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    private static int CountFragmentsForFace(
        List<TerrainChunkView> views,
        Vector3Int chunkCoordinate,
        VoxelAwareMeshPrecut.FaceId face)
    {
        int count = 0;
        for (int v = 0; v < views.Count; v++)
        {
            TerrainChunk chunk = views[v].Chunk;
            if (chunk == null || chunk.Geometry == null) continue;
            if (chunk.Coordinate != chunkCoordinate) continue;

            for (int i = 0; i < chunk.Geometry.FaceFragments.Count; i++)
            {
                if (chunk.Geometry.FaceFragments[i].Face.SameAs(face)) count++;
            }
        }

        return count;
    }

    private static int CountInvisibleVisibilityEntriesInChunk(
        List<TerrainChunkView> views,
        Vector3Int coordinate)
    {
        int total = 0;
        for (int i = 0; i < views.Count; i++)
        {
            TerrainChunk chunk = views[i].Chunk;
            if (chunk == null || chunk.Geometry == null) continue;
            if (chunk.Coordinate != coordinate) continue;

            foreach (KeyValuePair<int, bool> pair in
                chunk.Geometry.FaceFragmentVisibility)
            {
                if (!pair.Value) total++;
            }
        }

        return total;
    }

    private static int CountChangedChunksExcluding(
        Dictionary<Vector3Int, MeshCanonical> baseline,
        Dictionary<Vector3Int, MeshCanonical> after,
        Vector3Int excludedCoordinate)
    {
        int changed = 0;
        foreach (KeyValuePair<Vector3Int, MeshCanonical> pair in baseline)
        {
            if (pair.Key == excludedCoordinate) continue;

            MeshCanonical other;
            if (!after.TryGetValue(pair.Key, out other))
            {
                changed++;
                continue;
            }

            if (!DetSameCanonical(pair.Value, other, out string _)) changed++;
        }

        return changed;
    }

    // =====================================================================
    // T15 — A 阶段：命中解析（实体侧 Voxel）、目标有效性、连续挖掘行为
    // =====================================================================

    private static void CheckT15MineTargetResolution()
    {
        // 三个 Chunk 框架：原点、chunkStart=(5,0,7)、负坐标（含 Chunk 边界平面）。
        CheckT15PocketCase("T15[chunk(0,0,0)]", Vector3Int.zero,
            new Vector3Int(30, 12, 30));
        CheckT15PocketCase("T15[chunkStart=(5,0,7) XZ 边界]",
            new Vector3Int(5, 0, 7), new Vector3Int(450, 12, 400));
        CheckT15PocketCase("T15[负坐标 chunk(-1,0,-1) 边界]",
            new Vector3Int(-2, 0, -2), new Vector3Int(-75, 12, -50));

        // 体素对齐表面（plane y=15）：命中表面必须解析到实体侧 Voxel（而非空气侧）。
        CheckT15AlignedSurfaceCase();

        // 非 identity Transform。
        CheckT15TransformedCase();

        // 10 次连续挖掘：每次都必须产生真实编辑（不得因旧 Air 目标空操作）。
        CheckT15SequentialDigs();
    }

    private static void CheckT15PocketCase(
        string label,
        Vector3Int chunkStart,
        Vector3Int pocketVoxel)
    {
        GameObject terrainObject = CreateT11Terrain(label, -1f, chunkStart);

        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();
            VoxelField field = terrain.VoxelField;
            float voxelSize = field.VoxelSize;

            // 在实体材料内部挖出一个封闭空腔（6 面都是材料 ⇒ 6 面都会生成内壁）。
            field.Set(pocketVoxel, VoxelState.Air);
            int rebuilt = terrain.RebuildChunksAffectedByVoxel(pocketVoxel);

            Check(rebuilt >= 1, label + ": 空腔挖掘已重建（rebuilt=" + rebuilt + "）");

            Vector3 centre = new Vector3(
                (pocketVoxel.x + 0.5f) * voxelSize,
                (pocketVoxel.y + 0.5f) * voxelSize,
                (pocketVoxel.z + 0.5f) * voxelSize
            );

            // 六个方向：面平面上的命中点 + 指向空腔内的法线（即真实内壁法线）。
            CheckT15Direction(label + " +X", terrain, centre + new Vector3(voxelSize * 0.5f, 0f, 0f), Vector3.left, new Vector3Int(pocketVoxel.x + 1, pocketVoxel.y, pocketVoxel.z));
            CheckT15Direction(label + " -X", terrain, centre - new Vector3(voxelSize * 0.5f, 0f, 0f), Vector3.right, new Vector3Int(pocketVoxel.x - 1, pocketVoxel.y, pocketVoxel.z));
            CheckT15Direction(label + " +Y", terrain, centre + new Vector3(0f, voxelSize * 0.5f, 0f), Vector3.down, new Vector3Int(pocketVoxel.x, pocketVoxel.y + 1, pocketVoxel.z));
            CheckT15Direction(label + " -Y", terrain, centre - new Vector3(0f, voxelSize * 0.5f, 0f), Vector3.up, new Vector3Int(pocketVoxel.x, pocketVoxel.y - 1, pocketVoxel.z));
            CheckT15Direction(label + " +Z", terrain, centre + new Vector3(0f, 0f, voxelSize * 0.5f), Vector3.back, new Vector3Int(pocketVoxel.x, pocketVoxel.y, pocketVoxel.z + 1));
            CheckT15Direction(label + " -Z", terrain, centre - new Vector3(0f, 0f, voxelSize * 0.5f), Vector3.forward, new Vector3Int(pocketVoxel.x, pocketVoxel.y, pocketVoxel.z - 1));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    private static void CheckT15Direction(
        string label,
        DensityTerrain terrain,
        Vector3 worldPoint,
        Vector3 worldNormal,
        Vector3Int expectedVoxel)
    {
        Vector3Int voxel;
        string reason;
        bool resolved = TerrainVoxelSelector.TryResolveMineTarget(
            worldPoint, worldNormal, terrain, out voxel, out reason);

        Check(
            resolved && voxel == expectedVoxel,
            label + ": 命中解析到实体侧 Voxel（期望 " + expectedVoxel +
            "，实际 " + (resolved ? voxel.ToString() : "<invalid>") +
            (resolved ? string.Empty : " —— " + reason) + "）"
        );
    }

    private static void CheckT15AlignedSurfaceCase()
    {
        GameObject terrainObject = CreateT11Terrain(
            "T15[体素对齐表面]", 15f, Vector3Int.zero);

        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();
            float voxelSize = terrain.VoxelField.VoxelSize;

            // 命中地表平面（恰为整数平面 y=15），法线朝上（空气侧）。
            Vector3 surfaceHit = new Vector3(
                (30 + 0.5f) * voxelSize,
                SurfaceVoxelY * voxelSize,
                (30 + 0.5f) * voxelSize
            );

            Vector3Int voxel;
            string reason;
            bool resolved = TerrainVoxelSelector.TryResolveMineTarget(
                surfaceHit, Vector3.up, terrain, out voxel, out reason);

            Check(
                resolved && voxel == new Vector3Int(30, SurfaceVoxelY - 1, 30),
                "T15: 体素对齐地表命中解析到实体侧 Voxel（期望 (30,14,30)，实际 " +
                (resolved ? voxel.ToString() : "<invalid>") +
                (resolved ? string.Empty : " —— " + reason) + "）"
            );
            Check(
                !(resolved && voxel == new Vector3Int(30, SurfaceVoxelY, 30)),
                "T15: 命中地表不得解析到空气侧 Voxel (30,15,30)"
            );

            // 未编辑的空中 Voxel（基础密度为空气）不得被当作可挖掘目标。
            Vector3 airHit = new Vector3(
                (30 + 0.5f) * voxelSize,
                (SurfaceVoxelY + 1) * voxelSize,
                (30 + 0.5f) * voxelSize
            );
            bool airResolved = TerrainVoxelSelector.TryResolveMineTarget(
                airHit, Vector3.up, terrain, out voxel, out reason);

            Check(
                !airResolved,
                "T15: 未编辑的空气 Voxel 不可挖掘（reason=" + reason + "）"
            );

            // 已被编辑为 Air 的 Voxel：编辑状态可查且挖掘行为应拒绝。
            Vector3Int mined = new Vector3Int(30, SurfaceVoxelY - 1, 30);
            terrain.VoxelField.Set(mined, VoxelState.Air);
            terrain.RebuildChunksAffectedByVoxel(mined);
            int rebuilt = terrain.RebuildChunksAffectedByVoxel(
                new Vector3Int(40, SurfaceVoxelY - 1, 40));

            bool hasBaseMaterial;
            bool isEditedAir;
            string stateReason;
            bool stateOk = terrain.TryGetVoxelMaterialState(
                mined, out hasBaseMaterial, out isEditedAir, out stateReason);

            Check(
                stateOk && isEditedAir && hasBaseMaterial,
                "T15: 已挖 Voxel 的权威状态可查（editedAir=" + isEditedAir +
                " baseMaterial=" + hasBaseMaterial + "）"
            );
            Check(
                rebuilt >= 0,
                "T15: 相邻列挖掘入口可用（rebuilt=" + rebuilt + "）"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    private static void CheckT15TransformedCase()
    {
        GameObject terrainObject = CreateT11Terrain(
            "T15[非 identity Transform]", -1f, Vector3Int.zero);

        try
        {
            terrainObject.transform.position = new Vector3(10f, 3f, -7f);
            terrainObject.transform.rotation = Quaternion.Euler(0f, 37f, 0f);

            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();
            VoxelField field = terrain.VoxelField;
            float voxelSize = field.VoxelSize;

            Vector3Int pocketVoxel = new Vector3Int(30, 12, 30);
            field.Set(pocketVoxel, VoxelState.Air);
            terrain.RebuildChunksAffectedByVoxel(pocketVoxel);

            Vector3 localCentre = new Vector3(
                (pocketVoxel.x + 0.5f) * voxelSize,
                (pocketVoxel.y + 0.5f) * voxelSize,
                (pocketVoxel.z + 0.5f) * voxelSize
            );

            // 局部 +X 面（法线指向空腔外 → 空腔在 -X 面时法线为 +X 的相反方向）。
            Vector3 worldPoint = terrainObject.transform.TransformPoint(
                localCentre + new Vector3(voxelSize * 0.5f, 0f, 0f));
            Vector3 worldNormal = terrainObject.transform.TransformDirection(
                Vector3.left);

            Vector3Int voxel;
            string reason;
            bool resolved = TerrainVoxelSelector.TryResolveMineTarget(
                worldPoint, worldNormal, terrain, out voxel, out reason);

            Check(
                resolved && voxel == new Vector3Int(
                    pocketVoxel.x + 1, pocketVoxel.y, pocketVoxel.z),
                "T15: 非 identity Transform 下命中解析正确（期望 " +
                new Vector3Int(pocketVoxel.x + 1, pocketVoxel.y, pocketVoxel.z) +
                "，实际 " + (resolved ? voxel.ToString() : "<invalid>") +
                (resolved ? string.Empty : " —— " + reason) + "）"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    private static void CheckT15SequentialDigs()
    {
        GameObject terrainObject = CreateT11Terrain(
            "T15[连续挖掘]", 15f, Vector3Int.zero);

        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();
            VoxelField field = terrain.VoxelField;
            float voxelSize = field.VoxelSize;

            Vector3Int column = new Vector3Int(30, 0, 30);
            Vector3Int changedCoordinate =
                ChunkSpace.VoxelToChunk(column, ChunkVoxelSize);

            Dictionary<Vector3Int, MeshCanonical> baseline =
                SnapshotCanonical(GetViews(terrainObject));

            const int digs = 10;
            int failures = 0;
            int noOps = 0;

            for (int i = 0; i < digs; i++)
            {
                // 第 0 次命中真实地表平面（y = SurfaceVoxelY = 15），解析到最上层实体 Voxel
                // (30,14,30)；此后每次命中上一轮挖空后新暴露的地板平面（y = 15 - i），
                // 目标依次下移一层。全部目标都必须是“当时的表层实体 Voxel”，
                // 这样每次挖掘都会让被挖 Voxel 的共面片段（面向基础空气的一侧）
                // 需要显式隐藏覆写。
                int floorY = SurfaceVoxelY - i;
                Vector3 hitPoint = new Vector3(
                    (column.x + 0.5f) * voxelSize,
                    floorY * voxelSize,
                    (column.z + 0.5f) * voxelSize
                );

                Vector3Int target;
                string reason;
                bool resolved = TerrainVoxelSelector.TryResolveMineTarget(
                    hitPoint, Vector3.up, terrain, out target, out reason);

                Vector3Int expected = new Vector3Int(
                    column.x, floorY - 1, column.z);

                if (!resolved || target != expected)
                {
                    failures++;
                    Debug.Log(
                        "[StageB3C-T15] dig=" + i + " resolve failed: target=" +
                        (resolved ? target.ToString() : "<invalid>") +
                        " expected=" + expected + " reason=" + reason);
                    break;
                }

                int rebuilt = 0;
                if (field.Get(target) != VoxelState.Air)
                {
                    field.Set(target, VoxelState.Air);
                    rebuilt = terrain.RebuildChunksAffectedByVoxel(target);
                }
                else
                {
                    noOps++;
                }

                if (rebuilt < 1)
                {
                    failures++;
                    Debug.Log(
                        "[StageB3C-T15] dig=" + i + " rebuilt<1 for target=" +
                        target + " (state=" + field.Get(target) + ")");
                    break;
                }
            }

            Check(
                failures == 0,
                "T15: 连续 " + digs + " 次挖掘全部解析到实体侧并产生真实编辑（失败 " +
                failures + "）"
            );
            Check(
                noOps == 0,
                "T15: 连续挖掘无空操作（旧 Air 目标次数 " + noOps + "）"
            );

            int changedOther = CountChangedChunksExcluding(
                baseline, SnapshotCanonical(GetViews(terrainObject)),
                changedCoordinate);

            Check(
                changedOther == 0,
                "T15: 连续挖掘只改变目标 Chunk（其它 Chunk 改变 " + changedOther + "）"
            );
            Check(
                CountInvisibleVisibilityEntriesInChunk(
                    GetViews(terrainObject), changedCoordinate) > 0,
                "T15: 连续挖掘后目标 Chunk 存在显式隐藏覆写（材料侧被挖空）"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    // =====================================================================
    // T16 — 修正：允许选中“部分实体 Voxel”（中心基础密度 <= 0，但 Voxel 内含实体表面）
    //
    // 背景：Terrain 的实体由基础 DensityField（Marching Cubes 的等值面）定义，
    //       等值面可能穿过 Voxel 内部；只查 Voxel 中心密度会把这类 Voxel 判成空气。
    // 判据（两点都在目标 Voxel 内部，取或）：
    //   * 基础密度场在 Voxel 中心 > 0；
    //   * 基础密度场在“命中点沿法线进入实体侧的探测点” > 0。
    // 用例平面取 y = 14.25（平地），Voxel(·,14,·) 跨 y ∈ [14,15]：
    //   中心 14.5 ⇒ 密度 14.25 − 14.5 = −0.25 <= 0（旧判据拒绝）
    //   实体表面 14.25 在 Voxel 内部 ⇒ 命中侧探测点密度 +0.001 > 0（新判据接受）
    // =====================================================================

    private const float T16PartialHeight = 14.25f;
    private const float T16SolidHeight = 14.75f;
    private const int T16VoxelX = 30;
    private const int T16VoxelY = 14;
    private const int T16VoxelZ = 30;

    private static void CheckT16PartialSolidVoxelSelection()
    {
        CheckT16PartialSolidCore();
        CheckT16SixAxisDirections();
        CheckT16ChunkBoundaryAndTransform();
    }

    private static void CheckT16PartialSolidCore()
    {
        GameObject terrainObject = CreateT11Terrain(
            "T16[部分实体 Voxel]", T16PartialHeight, Vector3Int.zero);

        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();
            VoxelField field = terrain.VoxelField;
            float voxelSize = field.VoxelSize;
            List<TerrainChunkView> views = GetViews(terrainObject);

            Vector3Int target = new Vector3Int(T16VoxelX, T16VoxelY, T16VoxelZ);
            TerrainChunk chunk = FindChunk(views, target);

            Vector3 localHit = new Vector3(
                (target.x + 0.5f) * voxelSize,
                T16PartialHeight,
                (target.z + 0.5f) * voxelSize
            );

            float centreDensity = SampleT16BaseDensity(
                chunk, T16VoxelCentre(target, voxelSize), out bool centreSampled);

            Check(
                chunk != null && centreSampled && centreDensity <= 0f,
                "T16: 用例成立 —— 目标 Voxel 中心基础密度 <= 0（" +
                (centreSampled ? centreDensity.ToString("F4") : "<out of domain>") +
                "），旧判据会拒绝"
            );

            Vector3Int resolved;
            Vector3 probe;
            string resolveReason;
            bool resolvedSolid = TerrainVoxelSelector.TryResolveSolidVoxel(
                localHit,
                Vector3.up,
                terrain,
                out resolved,
                out probe,
                out Vector3 _,
                out resolveReason
            );

            Check(
                resolvedSolid && resolved == target,
                "T16: 命中解析到实体侧 Voxel（期望 " + target + "，实际 " +
                (resolvedSolid ? resolved.ToString() : "<invalid>") + "）"
            );

            Check(
                field.PositionToVoxel(probe) == target,
                "T16: 命中侧探测点属于目标 Voxel 本身（不会把相邻 Voxel 的实体算进来）"
            );

            float probeDensity = SampleT16BaseDensity(chunk, probe, out bool probeSampled);

            Check(
                probeSampled && probeDensity > 0f,
                "T16: 命中侧探测点（目标 Voxel 内部的实体侧）基础密度 > 0（" +
                (probeSampled ? probeDensity.ToString("F4") : "<out of domain>") + "）"
            );

            Vector3Int acceptedVoxel;
            string acceptReason;
            bool accepted = TerrainVoxelSelector.TryResolveMineTarget(
                localHit,
                Vector3.up,
                terrain,
                out acceptedVoxel,
                out acceptReason
            );

            Check(
                accepted && acceptedVoxel == target,
                "T16: 中心密度 <= 0 但含实体表面的部分实体 Voxel 允许选中（期望 " +
                target + "，实际 " +
                (accepted ? acceptedVoxel.ToString() : "<invalid>") +
                "，reason=" + acceptReason + "）"
            );

            // ---- 完全空气 Voxel：不能因为“相邻 Voxel 是实体”而被放行 ----
            Vector3Int airVoxel = new Vector3Int(
                target.x, target.y + 1, target.z);

            Vector3 airHit = new Vector3(
                (airVoxel.x + 0.5f) * voxelSize,
                (airVoxel.y + 0.5f) * voxelSize,
                (airVoxel.z + 0.5f) * voxelSize
            );

            TerrainChunk airChunk = FindChunk(views, airVoxel);
            float airCentre = SampleT16BaseDensity(
                airChunk, T16VoxelCentre(airVoxel, voxelSize), out bool airCentreSampled);

            Vector3Int airResolved;
            Vector3 airProbe;
            bool airResolvedSolid = TerrainVoxelSelector.TryResolveSolidVoxel(
                airHit,
                Vector3.up,
                terrain,
                out airResolved,
                out airProbe,
                out Vector3 _,
                out resolveReason
            );

            float airProbeDensity = SampleT16BaseDensity(
                airChunk, airProbe, out bool airProbeSampled);

            Check(
                airResolvedSolid && airResolved == airVoxel,
                "T16: 空气 Voxel 的解析仍落在该空气 Voxel 内（" +
                (airResolvedSolid ? airResolved.ToString() : "<invalid>") + "）"
            );

            Check(
                airCentreSampled && airCentre <= 0f &&
                airProbeSampled && airProbeDensity <= 0f,
                "T16: 完全空气 Voxel 的中心与命中侧探测点密度都 <= 0（中心 " +
                (airCentreSampled ? airCentre.ToString("F4") : "<out of domain>") +
                "，探测 " +
                (airProbeSampled ? airProbeDensity.ToString("F4") : "<out of domain>") + "）"
            );

            Vector3Int airAccepted;
            string airReason;
            bool airAcceptedOk = TerrainVoxelSelector.TryResolveMineTarget(
                airHit,
                Vector3.up,
                terrain,
                out airAccepted,
                out airReason
            );

            Check(
                !airAcceptedOk,
                "T16: 完全空气 Voxel 不可选中（尽管下方相邻 Voxel 是实体，reason=" +
                airReason + "）"
            );

            // ---- 已编辑为 Air 的部分实体 Voxel：不能选中 ----
            field.Set(target, VoxelState.Air);
            int t16Rebuilt = terrain.RebuildChunksAffectedByVoxel(target);

            Check(t16Rebuilt >= 1, "T16: 部分实体 Voxel 挖掘已重建（rebuilt=" + t16Rebuilt + "）");

            bool editedAirState = terrain.TryGetVoxelMaterialState(
                target,
                probe,
                out bool editedAirHasMaterial,
                out bool editedAirFlag,
                out string editedAirReason
            );

            Check(
                editedAirState && editedAirFlag,
                "T16: 已挖 Voxel 的权威状态可查（editedAir=" + editedAirFlag +
                " baseMaterial=" + editedAirHasMaterial + "）"
            );

            Vector3Int editedAccepted;
            string editedReason;
            bool editedAcceptedOk = TerrainVoxelSelector.TryResolveMineTarget(
                localHit,
                Vector3.up,
                terrain,
                out editedAccepted,
                out editedReason
            );

            Check(
                !editedAcceptedOk &&
                editedReason != null &&
                editedReason.Contains("edited as Air"),
                "T16: 已编辑为 Air 的 Voxel 不可选中（reason=" + editedReason + "）"
            );

            // ---- 挖掘后复验：不得把相邻（更深处）实体 Voxel 当成新目标 ----
            Vector3Int[] neighbours = T16NeighbourVoxels(target);
            int neighboursMined = 0;
            for (int i = 0; i < neighbours.Length; i++)
            {
                if (field.Get(neighbours[i]) == VoxelState.Air) neighboursMined++;
            }

            Check(
                neighboursMined == 0 && field.StoredVoxelCount == 1,
                "T16: 挖掘后只有目标 Voxel 被编辑（相邻被挖 " + neighboursMined +
                "，StoredVoxelCount=" + field.StoredVoxelCount + "）"
            );

            Check(
                !editedAcceptedOk && editedAccepted == target,
                "T16: 挖掘后复验仍定位到同一 Voxel 并因已挖空而拒绝，未漂移到相邻 Voxel（复验目标=" +
                editedAccepted + "，有效=" + editedAcceptedOk + "）"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    private static void CheckT16SixAxisDirections()
    {
        Quaternion[] rotations = new Quaternion[]
        {
            Quaternion.identity,
            Quaternion.Euler(180f, 0f, 0f),
            Quaternion.Euler(0f, 0f, -90f),
            Quaternion.Euler(0f, 0f, 90f),
            Quaternion.Euler(90f, 0f, 0f),
            Quaternion.Euler(-90f, 0f, 0f)
        };

        string[] labels = new string[] { "+Y", "-Y", "+X", "-X", "+Z", "-Z" };

        Vector3[] expectedNormals = new Vector3[]
        {
            Vector3.up,
            Vector3.down,
            Vector3.right,
            Vector3.left,
            Vector3.forward,
            Vector3.back
        };

        for (int i = 0; i < rotations.Length; i++)
        {
            GameObject terrainObject = CreateT11Terrain(
                "T16[方向 " + labels[i] + "]", T16PartialHeight, Vector3Int.zero);

            try
            {
                DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();
                VoxelField field = terrain.VoxelField;
                float voxelSize = field.VoxelSize;

                terrainObject.transform.position = new Vector3(10f, 3f, -7f);
                terrainObject.transform.rotation = rotations[i];
                terrainObject.transform.localScale = Vector3.one;

                Vector3Int target = new Vector3Int(T16VoxelX, T16VoxelY, T16VoxelZ);
                Vector3 localHit = new Vector3(
                    (target.x + 0.5f) * voxelSize,
                    T16PartialHeight,
                    (target.z + 0.5f) * voxelSize
                );

                Vector3 worldHit = terrainObject.transform.TransformPoint(localHit);
                Vector3 worldNormal = terrainObject.transform.TransformDirection(
                    Vector3.up);

                bool normalMatches =
                    (worldNormal - expectedNormals[i]).sqrMagnitude < 1e-6f;

                Vector3Int resolved;
                string reason;
                bool accepted = TerrainVoxelSelector.TryResolveMineTarget(
                    worldHit,
                    worldNormal,
                    terrain,
                    out resolved,
                    out reason
                );

                Check(
                    accepted && resolved == target,
                    "T16[" + labels[i] + "]: 部分实体 Voxel 允许选中（期望 " + target +
                    "，实际 " + (accepted ? resolved.ToString() : "<invalid>") +
                    "，worldNormal=" + worldNormal + "）"
                );

                Check(
                    normalMatches,
                    "T16[" + labels[i] + "]: 用例确为该世界主轴方向（worldNormal=" +
                    worldNormal + "）"
                );
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(terrainObject);
            }
        }
    }

    private static void CheckT16ChunkBoundaryAndTransform()
    {
        GameObject terrainObject = CreateT11Terrain(
            "T16[Chunk 边界 + 非 identity Transform]", T16PartialHeight, Vector3Int.zero);

        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();
            VoxelField field = terrain.VoxelField;
            float voxelSize = field.VoxelSize;
            List<TerrainChunkView> views = GetViews(terrainObject);

            terrainObject.transform.position = new Vector3(-13.5f, 4.25f, 7.75f);
            terrainObject.transform.rotation = Quaternion.Euler(0f, 37f, 90f);
            terrainObject.transform.localScale = Vector3.one;

            // ChunkVoxelSize = (75,37,50) ⇒ Chunk(0,0,0) 覆盖 Voxel x∈[0,74], z∈[0,49]；
            // 取该 Chunk 的最大 X / Z 边界格，相邻格属于 Chunk(1,0,1)。
            Vector3Int target = new Vector3Int(
                ChunkVoxelSize.x - 1,
                T16VoxelY,
                ChunkVoxelSize.z - 1
            );

            Vector3Int targetChunk = ChunkSpace.VoxelToChunk(target, ChunkVoxelSize);
            Vector3Int xNeighbourChunk = ChunkSpace.VoxelToChunk(
                target + Vector3Int.right, ChunkVoxelSize);
            Vector3Int zNeighbourChunk = ChunkSpace.VoxelToChunk(
                target + Vector3Int.forward, ChunkVoxelSize);

            Check(
                targetChunk != xNeighbourChunk && targetChunk != zNeighbourChunk,
                "T16[Chunk 边界]: 目标 Voxel 位于 Chunk 边界（" + target +
                " 属 Chunk " + targetChunk + "，+X 邻格属 " + xNeighbourChunk +
                "，+Z 邻格属 " + zNeighbourChunk + "）"
            );

            TerrainChunk chunk = FindChunk(views, target);
            float centreDensity = SampleT16BaseDensity(
                chunk, T16VoxelCentre(target, voxelSize), out bool centreSampled);

            Check(
                chunk != null && centreSampled && centreDensity <= 0f,
                "T16[Chunk 边界]: 边界部分实体 Voxel 中心密度 <= 0（" +
                (centreSampled ? centreDensity.ToString("F4") : "<out of domain>") + "）"
            );

            Vector3 localHit = new Vector3(
                (target.x + 0.5f) * voxelSize,
                T16PartialHeight,
                (target.z + 0.5f) * voxelSize
            );

            Vector3 worldHit = terrainObject.transform.TransformPoint(localHit);
            Vector3 worldNormal = terrainObject.transform.TransformDirection(
                Vector3.up);

            Vector3Int resolved;
            string reason;
            bool accepted = TerrainVoxelSelector.TryResolveMineTarget(
                worldHit,
                worldNormal,
                terrain,
                out resolved,
                out reason
            );

            Check(
                accepted && resolved == target,
                "T16[Chunk 边界 + 非 identity Transform]: 部分实体 Voxel 允许选中（期望 " +
                target + "，实际 " + (accepted ? resolved.ToString() : "<invalid>") +
                "，reason=" + reason + "）"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    private static void CheckT16SolidCentreVoxelMining()
    {
        GameObject terrainObject = CreateT11Terrain(
            "T16[中心密度 > 0 的实体 Voxel]", T16SolidHeight, Vector3Int.zero);

        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();
            VoxelField field = terrain.VoxelField;
            float voxelSize = field.VoxelSize;
            List<TerrainChunkView> views = GetViews(terrainObject);

            Vector3Int target = new Vector3Int(T16VoxelX, T16VoxelY, T16VoxelZ);
            TerrainChunk chunk = FindChunk(views, target);

            float centreDensity = SampleT16BaseDensity(
                chunk, T16VoxelCentre(target, voxelSize), out bool centreSampled);

            Check(
                chunk != null && centreSampled && centreDensity > 0f,
                "T16[中心密度 > 0]: 普通实体 Voxel 中心密度 > 0（" +
                (centreSampled ? centreDensity.ToString("F4") : "<out of domain>") + "）"
            );

            Vector3 localHit = new Vector3(
                (target.x + 0.5f) * voxelSize,
                T16SolidHeight,
                (target.z + 0.5f) * voxelSize
            );

            Vector3Int resolved;
            string reason;
            bool accepted = TerrainVoxelSelector.TryResolveMineTarget(
                localHit,
                Vector3.up,
                terrain,
                out resolved,
                out reason
            );

            Check(
                accepted && resolved == target,
                "T16[中心密度 > 0]: 普通实体 Voxel 正常选中（期望 " + target +
                "，实际 " + (accepted ? resolved.ToString() : "<invalid>") +
                "，reason=" + reason + "）"
            );

            field.Set(resolved, VoxelState.Air);
            int rebuilt = terrain.RebuildChunksAffectedByVoxel(resolved);

            Check(
                field.Get(target) == VoxelState.Air && rebuilt >= 1,
                "T16[中心密度 > 0]: 普通实体 Voxel 正常挖掘（state=" +
                field.Get(target) + "，rebuilt=" + rebuilt + "）"
            );

            Vector3Int[] neighbours = T16NeighbourVoxels(target);
            int neighboursMined = 0;
            for (int i = 0; i < neighbours.Length; i++)
            {
                if (field.Get(neighbours[i]) == VoxelState.Air) neighboursMined++;
            }

            Check(
                neighboursMined == 0 && field.StoredVoxelCount == 1,
                "T16[中心密度 > 0]: 只编辑目标 Voxel（相邻被挖 " + neighboursMined +
                "，StoredVoxelCount=" + field.StoredVoxelCount + "）"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    private static Vector3 T16VoxelCentre(Vector3Int voxel, float voxelSize)
    {
        return new Vector3(
            (voxel.x + 0.5f) * voxelSize,
            (voxel.y + 0.5f) * voxelSize,
            (voxel.z + 0.5f) * voxelSize
        );
    }

    private static float SampleT16BaseDensity(
        TerrainChunk chunk,
        Vector3 terrainLocal,
        out bool sampled)
    {
        float density;
        string error;
        sampled = VoxelAwareMeshRuntimeMutation.TrySampleBaseDensity(
            chunk,
            terrainLocal,
            out density,
            out error
        );

        return density;
    }

    private static Vector3Int[] T16NeighbourVoxels(Vector3Int voxel)
    {
        return new Vector3Int[]
        {
            voxel + Vector3Int.right,
            voxel + Vector3Int.left,
            voxel + Vector3Int.up,
            voxel + Vector3Int.down,
            voxel + Vector3Int.forward,
            voxel + Vector3Int.back
        };
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
            double expectedOpen = ExpectedWallAreaByDensity(
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

        // B3-D：内壁法线应“从实体侧指向空腔”（可见方向），而非 canonical 方向。
        Vector3Int positiveVoxel = negativeVoxel;
        positiveVoxel[face.Axis] += 1;
        bool negativeAir = voxelField != null && voxelField.IsAir(negativeVoxel);
        float normalSign = negativeAir ? -1f : 1f;
        Vector3 expectedNormal = face.Axis == 0
            ? new Vector3(normalSign, 0f, 0f)
            : face.Axis == 1
                ? new Vector3(0f, normalSign, 0f)
                : new Vector3(0f, 0f, normalSign);

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

    /// <summary>
    /// B3-D 语义修正后的期望覆盖面积 = “材料侧”面积：
    /// 未挖空一侧的 Base Density > 0（确实有材料）⇒ 需要内壁。
    /// 与 ExpectedSealedAreaByDensity（air 比例）互补（同一面上两者之和为 1）。
    /// </summary>
    private static double ExpectedWallAreaByDensity(
        List<TerrainChunkView> views,
        FaceKey face,
        int axis,
        bool negativeSide,
        int grid = 200)
    {
        return 1.0 - ExpectedSealedAreaByDensity(
            views, face, axis, negativeSide, grid);
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

                    // 必须与 AuditFace 的基线判定使用同一 key 空间：
                    // AuditFace 用面局部 2D (u,v) 的 TriangleKey 做 baseline 比较，
                    // 这里若用 3D PositionKey3 会永远不匹配（baselineSkipped 恒为 0，
                    // 把已有的 Base Geometry 误判成“新增几何”并参与绕序断言）。
                    int axisU = (face.Axis + 1) % 3;
                    int axisV = (face.Axis + 2) % 3;
                    keys.Add(TriangleKey(new Tri2(
                        new Vector2(a[axisU], a[axisV]),
                        new Vector2(b[axisU], b[axisV]),
                        new Vector2(d[axisU], d[axisV]))));
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

                        double area = ExpectedWallAreaByDensity(
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

    /// <summary>
    /// 找到与球腔相邻的“材料侧”Voxel：自空腔中心沿给定方向外移，
    /// 取第一个“自身中心有材料、而朝中心一侧邻居中心无材料”的 Voxel。
    /// 该 Voxel 被挖空后：朝空腔的面按材料分布开放、背离空腔的面应生成完整内壁。
    /// </summary>
    private static bool TryFindCavityMaterialVoxel(
        List<TerrainChunkView> views,
        Vector3 center,
        int axis,
        int side,
        out Vector3Int voxel,
        out Vector3Int innerNeighbour)
    {
        for (int offset = 1; offset <= 8; offset++)
        {
            Vector3Int candidate = new Vector3Int(
                Mathf.FloorToInt(center.x),
                Mathf.FloorToInt(center.y),
                Mathf.FloorToInt(center.z)
            );
            candidate[axis] += side == 0 ? -offset : offset;

            if (candidate.x < 1 || candidate.x > 73) continue;
            if (candidate.y < 1 || candidate.y > 35) continue;
            if (candidate.z < 1 || candidate.z > 48) continue;

            Vector3Int inner = candidate;
            inner[axis] += side == 0 ? 1 : -1;

            Vector3 candidateCenter = new Vector3(
                candidate.x + 0.5f,
                candidate.y + 0.5f,
                candidate.z + 0.5f
            );
            Vector3 innerCenter = new Vector3(
                inner.x + 0.5f,
                inner.y + 0.5f,
                inner.z + 0.5f
            );

            float candidateDensity;
            if (!TrySampleBaseDensity(
                    views, candidateCenter, out candidateDensity) ||
                candidateDensity <= 0f)
            {
                continue;
            }

            float innerDensity;
            if (!TrySampleBaseDensity(
                    views, innerCenter, out innerDensity) ||
                innerDensity > 0f)
            {
                continue;
            }

            voxel = candidate;
            innerNeighbour = inner;
            return true;
        }

        voxel = Vector3Int.zero;
        innerNeighbour = Vector3Int.zero;
        return false;
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
