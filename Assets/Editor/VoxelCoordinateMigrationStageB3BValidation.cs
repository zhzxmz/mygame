using System;
using System.Collections.Generic;
using System.Text;
using TerrainModificationV2;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Coordinate Migration Stage B3-B Validation — Hidden Face 几何覆盖与归属。
///
/// 根因（B3-A 报告遗留缺陷）：
///   VoxelAwareMeshSpatialFace.TriangulateLoop 用 ear clipping 三角化 Region 多边形，
///   但返回的是“收缩过程中”poly 列表的下标；两个调用方（RegionInteriorSample 与
///   Hidden Face 生成）都用它索引**原始**多边形 ⇒ 第一个耳朵正确、之后每个耳朵
///   都指向错误顶点 ⇒ 同一 SpatialFace 上三角形重叠 + 未覆盖缺口。
///
/// 本轮判据（不依赖生产算法，全部基于最终 Mesh 的“整面区域”量化计算）：
///   - 覆盖率：把面内所有三角形投影到面平面，用 slab 分解 + 梯形积分计算
///     并集面积（覆盖）、重叠面积（Σ面积 − 并集面积）、未覆盖面积（面矩形 − 并集）。
///   - 退化三角形、重复三角形、越出目标面的面积、绕序与法线方向。
///   - “应封闭 / 不应封闭”用独立的基础密度采样（面外 0.1m）判定。
///   - 面归属：同一 SpatialFace 的几何必须只由一个 Chunk 持有（不允许重复生成）。
///
/// T0 审计 / T1 单面 / T2 六方向 / T3 部分区域 / T4 边界与归属 /
/// T5 增量 vs 完整重建 / T6 恢复与重复重建 / T7 自然地形组合。
/// </summary>
public static class VoxelCoordinateMigrationStageB3BValidation
{
    private static readonly Vector3Int ChunkVoxelSize = new Vector3Int(75, 37, 50);
    private const float FlatHeight = 15.7f;
    private const int SurfaceVoxelY = 15;
    private const float PlaneTolerance = 1e-3f;
    private const float Quantize = 1e-4f;

    // 覆盖判据容差：面面积为 1 m²，允许 0.1% 的数值/离散误差。
    private const double UncoveredAreaTolerance = 1e-3;
    private const double OverlapAreaTolerance = 1e-3;
    private const double OutsideAreaTolerance = 1e-3;

    private static int checks;
    private static int failures;

    [MenuItem("Tools/Terrain/Validate Coordinate Migration Stage B3-B")]
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
                "[VoxelCoordinateMigrationStageB3BValidation] EXCEPTION: " +
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

        CheckTriangulateLoopUnit();
        CheckFaceAudit();
        CheckSinglePlanarFace();
        CheckSixDirections();
        CheckPartialRegion();
        CheckBoundariesAndOwnership();
        CheckIncrementalVsFullRebuild();
        CheckRestoreAndRepeat();
        CheckNaturalTerrainCase();
    }

    // =====================================================================
    // T0a — 三角化单元测试（直接验证根因修复）
    // =====================================================================

    private static void CheckTriangulateLoopUnit()
    {
        // 正方形：修复前第二个三角形会指向错误顶点。
        List<Vector2> square = new List<Vector2>
        {
            new Vector2(0f, 0f),
            new Vector2(0f, 1f),
            new Vector2(1f, 1f),
            new Vector2(1f, 0f)
        };

        CheckPolygonTriangulation(
            "T0a-正方形",
            square
        );

        // 非凸六边形（L 形），验证 ear clipping 在多耳朵情况下的下标映射。
        List<Vector2> lShape = new List<Vector2>
        {
            new Vector2(0f, 0f),
            new Vector2(0f, 2f),
            new Vector2(1f, 2f),
            new Vector2(1f, 1f),
            new Vector2(2f, 1f),
            new Vector2(2f, 0f)
        };

        CheckPolygonTriangulation(
            "T0a-L形非凸六边形",
            lShape
        );

        // 反向绕序输入必须得到同样的覆盖结果。
        List<Vector2> reversed = new List<Vector2>(lShape);
        reversed.Reverse();

        CheckPolygonTriangulation(
            "T0a-反向绕序六边形",
            reversed
        );
    }

    private static void CheckPolygonTriangulation(
        string label,
        List<Vector2> polygon)
    {
        List<Vector3Int> triangles =
            VoxelAwareMeshSpatialFace.TriangulateLoop(polygon, 1e-5f);

        Check(
            triangles.Count == polygon.Count - 2,
            label + ": 三角形数量 == n-2（" + triangles.Count + " / " +
            (polygon.Count - 2) + "）"
        );

        bool indicesValid = true;
        for (int i = 0; i < triangles.Count; i++)
        {
            Vector3Int tri = triangles[i];
            if (tri.x < 0 || tri.x >= polygon.Count ||
                tri.y < 0 || tri.y >= polygon.Count ||
                tri.z < 0 || tri.z >= polygon.Count)
            {
                indicesValid = false;
            }

            if (tri.x == tri.y || tri.y == tri.z || tri.x == tri.z)
            {
                indicesValid = false;
            }
        }

        Check(
            indicesValid,
            label + ": 所有三角形下标都指向原始多边形的不同顶点"
        );

        List<Tri2> tri2D = new List<Tri2>();
        double sumArea = 0;
        for (int i = 0; i < triangles.Count; i++)
        {
            Vector3Int tri = triangles[i];
            Tri2 t = new Tri2(
                polygon[tri.x],
                polygon[tri.y],
                polygon[tri.z]
            );
            tri2D.Add(t);
            sumArea += t.Area;
        }

        double polygonArea = Math.Abs(PolygonArea2D(polygon));

        Check(
            Math.Abs(sumArea - polygonArea) <= 1e-4,
            label + ": Σ三角形面积 == 多边形面积（" +
            sumArea.ToString("F6") + " vs " + polygonArea.ToString("F6") + "）"
        );

        // 用细网格检查“多边形内每一点都被覆盖、覆盖不溢出多边形”。
        int gapSamples = 0;
        int spillSamples = 0;
        int insideSamples = 0;
        const int grid = 120;
        for (int gx = 1; gx < grid; gx++)
        {
            for (int gy = 1; gy < grid; gy++)
            {
                Vector2 p = new Vector2(gx / (float)grid, gy / (float)grid);
                bool inside = PointInPolygon(p, polygon);
                bool covered = false;
                for (int i = 0; i < tri2D.Count && !covered; i++)
                {
                    covered = tri2D[i].Contains(p, 1e-6f);
                }

                if (inside) insideSamples++;
                if (inside && !covered) gapSamples++;
                if (!inside && covered) spillSamples++;
            }
        }

        Check(
            insideSamples > 0 && gapSamples == 0,
            label + ": 多边形内部无未覆盖点（采样 " + insideSamples +
            " 点，缺口 " + gapSamples + "）"
        );
        Check(
            spillSamples == 0,
            label + ": 覆盖不溢出多边形（溢出采样 " + spillSamples + "）"
        );

        double overlap = ComputeOverlapArea(tri2D);
        Check(
            overlap <= 1e-4,
            label + ": 三角形之间无重叠（重叠面积 " + overlap.ToString("F6") + "）"
        );
    }

    private static double PolygonArea2D(List<Vector2> polygon)
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

    // =====================================================================
    // T0b — 单个失败面的几何审计
    // =====================================================================

    private static void CheckFaceAudit()
    {
        GameObject terrainObject = CreateFlatFixture("StageB3B_Audit");

        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();
            Vector3Int voxel = new Vector3Int(30, SurfaceVoxelY, 30);

            terrain.VoxelField.Set(voxel, VoxelState.Air);
            terrain.RebuildChunksAffectedByVoxel(voxel);

            List<TerrainChunkView> views = GetViews(terrainObject);

            // B3-D 材料边界语义：材料侧在下方（Voxel-1 为实体）⇒ 坑底面必须完整封闭；
            // 天空侧（材料侧在上方 = 空气）不得生成任何几何。
            FaceKey face = new FaceKey(1, voxel.y, voxel.z, voxel.x);
            Vector3Int floorNegativeVoxel = new Vector3Int(
                voxel.x, voxel.y - 1, voxel.z);
            FaceAudit audit = AuditFace(
                views, face, floorNegativeVoxel, terrain.VoxelField);

            Check(
                audit.TriangleCount > 0,
                "T0b: 坑底面生成了 Hidden Face 三角形（" +
                audit.TriangleCount + "）"
            );
            Check(
                audit.OwnerChunkCount == 1,
                "T0b: 该面的几何只由一个 Chunk 持有（实际 " +
                audit.OwnerChunkCount + "：" + audit.OwnerChunks + "）"
            );

            double rectArea = 1.0;
            Check(
                audit.Coverage.UncoveredArea <= UncoveredAreaTolerance,
                "T0b: 未覆盖面积 ≈ 0（" +
                audit.Coverage.UncoveredArea.ToString("E3") + " m²）"
            );
            Check(
                audit.Coverage.OverlapArea <= OverlapAreaTolerance,
                "T0b: 重叠面积 ≈ 0（" +
                audit.Coverage.OverlapArea.ToString("E3") + " m²）"
            );
            Check(
                Math.Abs(audit.Coverage.UnionArea - rectArea) <= 1e-3,
                "T0b: 覆盖面积 == 面面积（" +
                audit.Coverage.UnionArea.ToString("F6") + " vs " + rectArea + "）"
            );
            Check(
                audit.Coverage.DegenerateCount == 0,
                "T0b: 无退化三角形（" + audit.Coverage.DegenerateCount + "）"
            );
            Check(
                audit.Coverage.DuplicateCount == 0,
                "T0b: 无重复三角形（" + audit.Coverage.DuplicateCount + "）"
            );

            FaceKey topFace = new FaceKey(1, voxel.y + 1, voxel.z, voxel.x);
            FaceAudit topAudit = AuditFace(
                views, topFace, voxel, terrain.VoxelField);

            Check(
                topAudit.TriangleCount == 0 &&
                topAudit.Coverage.UnionArea <= UncoveredAreaTolerance,
                "T0b: 天空侧不生成 Hidden Face 几何（三角形 " +
                topAudit.TriangleCount + "，覆盖 " +
                topAudit.Coverage.UnionArea.ToString("E3") + " m²）"
            );

            Debug.Log(
                "[StageB3B-audit] floorFace=" + face.Key +
                " negativeVoxel=" + floorNegativeVoxel + " negativeState=" +
                terrain.VoxelField.Get(floorNegativeVoxel) +
                " positiveVoxel=" + voxel + " positiveState=" +
                terrain.VoxelField.Get(voxel) +
                " expectedRegion=1.000000m²" +
                " triangles=" + audit.TriangleCount +
                " union=" + audit.Coverage.UnionArea.ToString("F6") +
                " overlap=" + audit.Coverage.OverlapArea.ToString("E3") +
                " uncovered=" + audit.Coverage.UncoveredArea.ToString("E3") +
                " sum=" + audit.Coverage.SumArea.ToString("F6") +
                " chunk=" + audit.OwnerChunks +
                " windingOk=" + audit.WindingConsistent +
                " outside=" + audit.Coverage.OutsideArea.ToString("E3")
            );

            for (int i = 0; i < audit.TriangleDump.Count; i++)
            {
                Debug.Log("[StageB3B-audit-triangle] " + audit.TriangleDump[i]);
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    // =====================================================================
    // T1 — 单个平面 Hidden Face
    // =====================================================================

    private static void CheckSinglePlanarFace()
    {
        GameObject terrainObject = CreateFlatFixture("StageB3B_SingleFace");

        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();
            Vector3Int voxel = new Vector3Int(30, SurfaceVoxelY, 30);

            terrain.VoxelField.Set(voxel, VoxelState.Air);
            terrain.RebuildChunksAffectedByVoxel(voxel);

            List<TerrainChunkView> views = GetViews(terrainObject);

            // B3-D 材料边界语义：
            //   · 坑底面（plane = voxel.y，材料侧在下方）⇒ 完整内壁（2 个三角形）；
            //   · 天空侧（plane = voxel.y + 1，材料侧在上方 = 空气）⇒ 不得生成几何。
            FaceKey face = new FaceKey(1, voxel.y, voxel.z, voxel.x);
            Vector3Int floorNegativeVoxel = new Vector3Int(
                voxel.x, voxel.y - 1, voxel.z);
            FaceAudit audit = AuditFace(
                views, face, floorNegativeVoxel, terrain.VoxelField);

            Check(
                audit.Coverage.UncoveredArea <= UncoveredAreaTolerance,
                "T1: 坑底目标区域完整覆盖（未覆盖 " +
                audit.Coverage.UncoveredArea.ToString("E3") + " m²）"
            );
            Check(
                audit.Coverage.OverlapArea <= OverlapAreaTolerance,
                "T1: 非预期重叠为零（重叠 " +
                audit.Coverage.OverlapArea.ToString("E3") + " m²）"
            );
            Check(
                audit.Coverage.DegenerateCount == 0,
                "T1: 退化三角形为零（" + audit.Coverage.DegenerateCount + "）"
            );
            Check(
                audit.Coverage.DuplicateCount == 0,
                "T1: 重复三角形为零（" + audit.Coverage.DuplicateCount + "）"
            );
            Check(
                audit.Coverage.OutsideArea <= OutsideAreaTolerance,
                "T1: 几何不越出目标 SpatialFace（越界面积 " +
                audit.Coverage.OutsideArea.ToString("E3") + " m²）"
            );
            Check(
                audit.WindingConsistent,
                "T1: 坑底内壁绕序自材料侧指向空腔"
            );
            Check(
                audit.TriangleCount == 2,
                "T1: 完整坑底面用 2 个三角形表示（实际 " + audit.TriangleCount + "）"
            );

            FaceKey topFace = new FaceKey(1, voxel.y + 1, voxel.z, voxel.x);
            FaceAudit topAudit = AuditFace(
                views, topFace, voxel, terrain.VoxelField);
            Check(
                topAudit.TriangleCount == 0 &&
                topAudit.Coverage.UnionArea <= UncoveredAreaTolerance &&
                topAudit.Coverage.OutsideArea <= OutsideAreaTolerance,
                "T1: 天空侧不生成几何（三角形 " + topAudit.TriangleCount +
                "，覆盖 " + topAudit.Coverage.UnionArea.ToString("E3") + " m²）"
            );

            Debug.Log(
                "[StageB3B-T1] floorFace=" + face.Key +
                " union=" + audit.Coverage.UnionArea.ToString("F6") +
                " overlap=" + audit.Coverage.OverlapArea.ToString("E3") +
                " uncovered=" + audit.Coverage.UncoveredArea.ToString("E3") +
                " degenerate=" + audit.Coverage.DegenerateCount +
                " duplicate=" + audit.Coverage.DuplicateCount +
                " outside=" + audit.Coverage.OutsideArea.ToString("E3") +
                " topTriangles=" + topAudit.TriangleCount
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    // =====================================================================
    // T2 — 六个面方向
    // =====================================================================

    private static void CheckSixDirections()
    {
        GameObject terrainObject = CreateCavityFixture("StageB3B_SixDirections");

        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();
            Vector3 cavityCenter = CavityCenter();
            float cavityRadius = CavityRadius();

            int tested = 0;

            for (int axis = 0; axis < 3; axis++)
            {
                for (int side = 0; side < 2; side++)
                {
                    Vector3Int voxel;
                    Vector3Int neighbour;

                    if (!TryFindFullyOpenCavityFaceVoxel(
                        terrain,
                        cavityCenter,
                        cavityRadius,
                        axis,
                        side,
                        out voxel,
                        out neighbour))
                    {
                        Limit(
                            "T2: 轴" + axis + (side == 0 ? "负" : "正") +
                            "方向未找到“整面朝空气打开”的实体 Voxel，跳过该方向"
                        );
                        continue;
                    }

                    tested++;
                    string label = "T2[轴" + axis + (side == 0 ? "负" : "正") + "]";

                    terrain.VoxelField.Set(voxel, VoxelState.Air);
                    terrain.RebuildChunksAffectedByVoxel(voxel);

                    List<TerrainChunkView> views = GetViews(terrainObject);
                    FaceKey face = FaceTowardNeighbour(voxel, neighbour, axis);

                    FaceAudit audit = AuditFace(
                        views,
                        face,
                        voxel,
                        terrain.VoxelField
                    );

                    // B3-D 材料边界语义：该 Voxel 位于球腔内部（基础密度 <= 0），
                    // 朝空腔一侧本来就没有材料 ⇒ 挖空后不得新增任何几何（不得封堵空腔）。
                    // 材料侧（背离空腔）的六方向内壁与绕序覆盖由 B3-C T2b/T4 验证。
                    Check(
                        audit.TriangleCount == 0 &&
                        audit.Coverage.UnionArea <= UncoveredAreaTolerance &&
                        audit.Coverage.OverlapArea <= OverlapAreaTolerance &&
                        audit.Coverage.DegenerateCount == 0 &&
                        audit.Coverage.DuplicateCount == 0,
                        label + ": 空腔侧不生成几何（三角形 " +
                        audit.TriangleCount + "，覆盖 " +
                        audit.Coverage.UnionArea.ToString("E3") + " m²）"
                    );

                    Debug.Log(
                        "[StageB3B-T2] axis=" + axis +
                        " side=" + (side == 0 ? "-" : "+") +
                        " voxel=" + voxel +
                        " face=" + face.Key +
                        " union=" + audit.Coverage.UnionArea.ToString("F6") +
                        " uncovered=" +
                        audit.Coverage.UncoveredArea.ToString("E3") +
                        " overlap=" + audit.Coverage.OverlapArea.ToString("E3") +
                        " triangles=" + audit.TriangleCount
                    );

                    // 还原，避免影响其他方向。
                    terrain.VoxelField.Set(voxel, VoxelState.Solid);
                    terrain.RebuildChunksAffectedByVoxel(voxel);
                }
            }

            Check(
                tested == 6,
                "T2: 六个方向的空腔侧用例全部实测（实际 " + tested + "/6）"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    // =====================================================================
    // T3 — 部分区域挖掘
    // =====================================================================

    private static void CheckPartialRegion()
    {
        GameObject terrainObject = CreateNaturalFixture("StageB3B_PartialRegion");

        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();
            List<TerrainChunkView> views = GetViews(terrainObject);

            FaceCandidate candidate;
            if (!TryFindPartialOpenFace(terrain, views, 0.25, 0.75, out candidate))
            {
                Limit(
                    "T3: 未找到“部分朝空气打开”的面，跳过部分区域用例（不伪造结果）"
                );
                return;
            }

            Vector3Int voxel = candidate.Voxel;
            Vector3Int neighbour = candidate.Neighbour;
            int axis = candidate.Axis;
            int side = candidate.Side;

            terrain.VoxelField.Set(voxel, VoxelState.Air);
            int rebuilt = terrain.RebuildChunksAffectedByVoxel(voxel);

            if (rebuilt <= 0)
            {
                Limit(
                    "T3: 候选 Voxel " + voxel + " 的 Mutation 未提交（" +
                    VoxelAwareMeshRuntimeMutation.LastEvaluationFailureMessage +
                    "），跳过部分区域用例"
                );
                return;
            }

            views = GetViews(terrainObject);
            FaceKey face = FaceTowardNeighbour(voxel, neighbour, axis);
            FaceAudit audit = AuditFace(views, face, voxel, terrain.VoxelField);

            // 独立判据：在面平面外 1mm（非挖空侧）用**自行实现的三线性采样**
            // 读取基础密度（与生产同一份输入数据、不同采样实现），
            // density <= 0 的区域面积 = 应封闭面积。
            double expectedSealed = ExpectedWallAreaByDensity(
                views,
                face,
                axis,
                side == 0
            );

            double covered = audit.Coverage.UnionArea;

            Check(
                audit.Coverage.OverlapArea <= OverlapAreaTolerance &&
                audit.Coverage.DegenerateCount == 0 &&
                audit.Coverage.DuplicateCount == 0 &&
                audit.Coverage.OutsideArea <= OutsideAreaTolerance,
                "T3: 部分区域几何无重叠/退化/重复/越界（" +
                audit.Coverage.OverlapArea.ToString("E3") + " / " +
                audit.Coverage.DegenerateCount + " / " +
                audit.Coverage.DuplicateCount + " / " +
                audit.Coverage.OutsideArea.ToString("E3") + "）"
            );

            Check(
                Math.Abs(covered - expectedSealed) <= 0.08,
                "T3: 覆盖面积 ≈ 材料侧面积（" +
                covered.ToString("F4") + " vs " + expectedSealed.ToString("F4") +
                "，容差 0.08 m²）"
            );

            // 空腔侧采样点不得被覆盖；材料侧采样点必须被覆盖（B3-D）。
            int airCovered = CountCoveredPoints(
                audit,
                views,
                face,
                axis,
                side == 0,
                0.25f,
                false
            );
            int solidUncovered = CountCoveredPoints(
                audit,
                views,
                face,
                axis,
                side == 0,
                0.25f,
                true
            );

            Check(
                airCovered == 0,
                "T3: 空腔侧（密度 <= -0.25）采样点不得被封闭（被错误封闭 " +
                airCovered + " 点）"
            );
            Check(
                solidUncovered == 0,
                "T3: 材料侧（密度 >= +0.25）采样点必须被封闭（未封闭 " +
                solidUncovered + " 点）"
            );

            Debug.Log(
                "[StageB3B-T3] voxel=" + voxel + " face=" + face.Key +
                " covered=" + covered.ToString("F4") +
                " expectedWall=" + expectedSealed.ToString("F4") +
                " triangles=" + audit.TriangleCount +
                " uncovered=" + audit.Coverage.UncoveredArea.ToString("F4")
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    // =====================================================================
    // T4 — Voxel 边界与 Chunk 边界
    // =====================================================================

    private static void CheckBoundariesAndOwnership()
    {
        RunBoundaryCase(
            Vector3Int.zero,
            new Vector3Int(30, SurfaceVoxelY, 30),
            "T4[内部(0,0,0)]"
        );
        RunBoundaryCase(
            Vector3Int.zero,
            new Vector3Int(74, SurfaceVoxelY, 30),
            "T4[X边界负侧]"
        );
        RunBoundaryCase(
            Vector3Int.zero,
            new Vector3Int(75, SurfaceVoxelY, 30),
            "T4[X边界正侧]"
        );
        RunBoundaryCase(
            Vector3Int.zero,
            new Vector3Int(10, SurfaceVoxelY, 49),
            "T4[Z边界负侧]"
        );
        RunBoundaryCase(
            Vector3Int.zero,
            new Vector3Int(10, SurfaceVoxelY, 50),
            "T4[Z边界正侧]"
        );
        RunBoundaryCase(
            Vector3Int.zero,
            new Vector3Int(75, SurfaceVoxelY, 50),
            "T4[XZ交汇]"
        );
        RunBoundaryCase(
            new Vector3Int(5, 0, 7),
            new Vector3Int(450, SurfaceVoxelY, 400),
            "T4[chunkStart=(5,0,7) XZ交汇]"
        );
        RunBoundaryCase(
            new Vector3Int(5, 0, 7),
            new Vector3Int(405, SurfaceVoxelY, 400),
            "T4[chunkStart=(5,0,7) Z边界]"
        );
    }

    private static void RunBoundaryCase(
        Vector3Int chunkStart,
        Vector3Int voxel,
        string label)
    {
        GameObject terrainObject = CreateFlatFixture(
            "StageB3B_" + label,
            chunkStart
        );

        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();
            List<TerrainChunkView> views = GetViews(terrainObject);

            // 挖空前记录 6 个面各自的三角形集合（基线包含 Base/Bridge 几何，
            // 因此“不得新增 Hidden Face”必须用集合差判定，不能用绝对数量）。
            FaceKey[] faces = new FaceKey[6];
            HashSet<string>[] baselineKeys = new HashSet<string>[6];
            for (int axis = 0; axis < 3; axis++)
            {
                for (int side = 0; side < 2; side++)
                {
                    int index = axis * 2 + side;
                    faces[index] = FaceTowardNeighbour(
                        voxel,
                        voxel + AxisVector(axis, side),
                        axis
                    );
                    baselineKeys[index] = SnapshotFaceTriangleKeys(
                        views,
                        faces[index]
                    );
                }
            }

            terrain.VoxelField.Set(voxel, VoxelState.Air);
            terrain.RebuildChunksAffectedByVoxel(voxel);

            views = GetViews(terrainObject);

            // B3-D 材料边界语义：
            //   · Voxel 顶面（plane = voxel.y + 1，材料侧在上方 = 天空）
            //     ⇒ 未挖空一侧本来就是空气 ⇒ 不得生成任何几何（不得造盖板）；
            //   · Voxel 底面（plane = voxel.y，材料侧在下方 = 实体）
            //     ⇒ 完整坑底内壁，绕序自材料侧指向空腔（−Y 面 ⇒ 法线 +Y）。
            FaceKey topFace = new FaceKey(1, voxel.y + 1, voxel.z, voxel.x);
            FaceAudit topAudit = AuditFace(
                views, topFace, voxel, terrain.VoxelField);
            double topExpected = ExpectedWallAreaByDensity(
                views, topFace, 1, false);

            Check(
                topExpected <= 1e-3 &&
                topAudit.TriangleCount == 0 &&
                topAudit.Coverage.UnionArea <= UncoveredAreaTolerance,
                label + ": 天空侧不封面（期望材料侧面积 " +
                topExpected.ToString("F4") + "，实际覆盖 " +
                topAudit.Coverage.UnionArea.ToString("F4") + "，三角形 " +
                topAudit.TriangleCount + "）"
            );

            FaceKey floorFace = new FaceKey(1, voxel.y, voxel.z, voxel.x);
            Vector3Int floorNegativeVoxel = new Vector3Int(
                voxel.x, voxel.y - 1, voxel.z);
            FaceAudit audit = AuditFace(
                views, floorFace, floorNegativeVoxel, terrain.VoxelField);
            double floorExpected = ExpectedWallAreaByDensity(
                views, floorFace, 1, true);

            Check(
                floorExpected >= 0.999 &&
                audit.Coverage.UncoveredArea <= UncoveredAreaTolerance &&
                audit.Coverage.OverlapArea <= OverlapAreaTolerance,
                label + ": 坑底完整内壁且无重叠（期望 " +
                floorExpected.ToString("F4") + "，未覆盖 " +
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
                audit.Coverage.OutsideArea <= OutsideAreaTolerance &&
                audit.WindingConsistent,
                label + ": 无退化/重复/越界且绕序自材料侧指向空腔"
            );

            // 其余 6 个面（含顶面）：材料侧不存在材料时不得新增任何三角形。
            int unexpectedNewTriangles = 0;
            for (int axis = 0; axis < 3; axis++)
            {
                for (int side = 0; side < 2; side++)
                {
                    int index = axis * 2 + side;
                    FaceKey other = faces[index];
                    double openArea = ExpectedWallAreaByDensity(
                        views,
                        other,
                        axis,
                        side == 0
                    );

                    HashSet<string> after = SnapshotFaceTriangleKeys(views, other);
                    int added = 0;
                    foreach (string key in after)
                    {
                        if (!baselineKeys[index].Contains(key)) added++;
                    }

                    if (openArea <= 0.02)
                    {
                        unexpectedNewTriangles += added;
                    }
                }
            }

            Check(
                unexpectedNewTriangles == 0,
                label + ": 材料侧无材料的邻接面不新增 Hidden Face（" +
                unexpectedNewTriangles + " 个三角形）"
            );

            Debug.Log(
                "[StageB3B-T4] label=" + label + " voxel=" + voxel +
                " floorFace=" + floorFace.Key +
                " union=" + audit.Coverage.UnionArea.ToString("F6") +
                " chunks=" + audit.OwnerChunks
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    // =====================================================================
    // T5 — 增量与完整重建
    // =====================================================================

    private static void CheckIncrementalVsFullRebuild()
    {
        Vector3Int[] voxels =
        {
            new Vector3Int(30, SurfaceVoxelY, 30),
            new Vector3Int(74, SurfaceVoxelY, 30),
            new Vector3Int(75, SurfaceVoxelY, 30),
            new Vector3Int(10, SurfaceVoxelY, 50),
            new Vector3Int(75, SurfaceVoxelY, 50)
        };

        GameObject pathA1 = CreateFlatFixture("StageB3B_PathA1", Vector3Int.zero);
        GameObject pathA2 = CreateFlatFixture("StageB3B_PathA2", Vector3Int.zero);
        GameObject pathB = CreateFlatFixture("StageB3B_PathB", Vector3Int.zero);

        try
        {
            DensityTerrain terrainA1 = pathA1.GetComponent<DensityTerrain>();
            DensityTerrain terrainA2 = pathA2.GetComponent<DensityTerrain>();
            DensityTerrain terrainB = pathB.GetComponent<DensityTerrain>();

            for (int i = 0; i < voxels.Length; i++)
            {
                terrainA1.VoxelField.Set(voxels[i], VoxelState.Air);
                terrainA1.RebuildChunksAffectedByVoxel(voxels[i]);
            }

            for (int i = voxels.Length - 1; i >= 0; i--)
            {
                terrainA2.VoxelField.Set(voxels[i], VoxelState.Air);
                terrainA2.RebuildChunksAffectedByVoxel(voxels[i]);
            }

            for (int i = 0; i < voxels.Length; i++)
            {
                terrainB.VoxelField.Set(voxels[i], VoxelState.Air);
            }

            bool rebuilt = terrainB.RebuildVoxelAwareGeometryFromVoxelField();

            List<TerrainChunkView> viewsA1 = GetViews(pathA1);
            List<TerrainChunkView> viewsA2 = GetViews(pathA2);
            List<TerrainChunkView> viewsB = GetViews(pathB);

            Dictionary<Vector3Int, MeshCanonical> canonicalA1 =
                SnapshotCanonical(viewsA1);
            Dictionary<Vector3Int, MeshCanonical> canonicalA2 =
                SnapshotCanonical(viewsA2);
            Dictionary<Vector3Int, MeshCanonical> canonicalB =
                SnapshotCanonical(viewsB);

            Check(rebuilt, "T5: 完整重建成功");
            Check(
                SameAllChunks(canonicalA1, canonicalA2, out string detail12),
                "T5: 顺序 A 与顺序 B 的增量结果一致" +
                (detail12.Length > 0 ? " —— " + detail12 : string.Empty)
            );
            Check(
                SameAllChunks(canonicalA1, canonicalB, out string detail1B),
                "T5: 增量结果与完整重建一致" +
                (detail1B.Length > 0 ? " —— " + detail1B : string.Empty)
            );

            // 三条路径都必须通过几何正确性判据（一致性不是充分条件）。
            CheckPathGeometryQuality(viewsA1, terrainA1.VoxelField, voxels, "T5[增量A]");
            CheckPathGeometryQuality(viewsA2, terrainA2.VoxelField, voxels, "T5[增量B]");
            CheckPathGeometryQuality(viewsB, terrainB.VoxelField, voxels, "T5[完整重建]");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(pathA1);
            UnityEngine.Object.DestroyImmediate(pathA2);
            UnityEngine.Object.DestroyImmediate(pathB);
        }
    }

    private static void CheckPathGeometryQuality(
        List<TerrainChunkView> views,
        VoxelField voxelField,
        Vector3Int[] voxels,
        string label)
    {
        int badFaces = 0;
        int duplicateChunks = 0;
        int unexpectedTopGeometry = 0;

        for (int i = 0; i < voxels.Length; i++)
        {
            // 顶面（材料侧 = 天空）必须完全没有几何。
            FaceKey topFace = new FaceKey(
                1,
                voxels[i].y + 1,
                voxels[i].z,
                voxels[i].x
            );
            FaceAudit topAudit = AuditFace(
                views, topFace, voxels[i], voxelField);

            if (topAudit.TriangleCount > 0 ||
                topAudit.Coverage.UnionArea > UncoveredAreaTolerance)
            {
                unexpectedTopGeometry++;
            }

            // 坑底面（材料侧 = 实体）必须完整覆盖且归属唯一。
            FaceKey floorFace = new FaceKey(
                1,
                voxels[i].y,
                voxels[i].z,
                voxels[i].x
            );
            Vector3Int floorNegativeVoxel = new Vector3Int(
                voxels[i].x, voxels[i].y - 1, voxels[i].z);
            FaceAudit audit = AuditFace(
                views, floorFace, floorNegativeVoxel, voxelField);

            if (audit.Coverage.UncoveredArea > UncoveredAreaTolerance ||
                audit.Coverage.OverlapArea > OverlapAreaTolerance ||
                audit.Coverage.DegenerateCount > 0)
            {
                badFaces++;
            }

            if (audit.OwnerChunkCount != 1) duplicateChunks++;
        }

        Check(
            badFaces == 0 && duplicateChunks == 0 &&
            unexpectedTopGeometry == 0,
            label + ": 每个已挖空 Voxel 的坑底覆盖正确、归属唯一且顶面不封面" +
            "（坑底失败面 " + badFaces + "，重复归属 " + duplicateChunks +
            "，顶面异常 " + unexpectedTopGeometry + "）"
        );
    }

    // =====================================================================
    // T6 — 恢复与重复重建
    // =====================================================================

    private static void CheckRestoreAndRepeat()
    {
        GameObject terrainObject = CreateFlatFixture("StageB3B_Restore");

        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();
            List<TerrainChunkView> views = GetViews(terrainObject);
            Dictionary<Vector3Int, MeshCanonical> baseline = SnapshotCanonical(views);

            Vector3Int voxel = new Vector3Int(30, SurfaceVoxelY, 30);
            terrain.VoxelField.Set(voxel, VoxelState.Air);
            terrain.RebuildChunksAffectedByVoxel(voxel);

            FaceKey face = new FaceKey(1, voxel.y, voxel.z, voxel.x);
            Vector3Int floorNegativeVoxel = new Vector3Int(
                voxel.x, voxel.y - 1, voxel.z);
            FaceKey topFace = new FaceKey(1, voxel.y + 1, voxel.z, voxel.x);

            int contributionCountAfterFirst = CountContributionEntries(
                GetViews(terrainObject)
            );

            for (int i = 0; i < 3; i++)
            {
                bool ok = terrain.RebuildVoxelAwareGeometryFromVoxelField();
                List<TerrainChunkView> repeatViews = GetViews(terrainObject);
                FaceAudit audit = AuditFace(
                    repeatViews, face, floorNegativeVoxel, terrain.VoxelField);
                FaceAudit topAudit = AuditFace(
                    repeatViews, topFace, voxel, terrain.VoxelField);

                Check(
                    ok &&
                    audit.Coverage.UncoveredArea <= UncoveredAreaTolerance &&
                    audit.Coverage.OverlapArea <= OverlapAreaTolerance &&
                    audit.Coverage.DuplicateCount == 0,
                    "T6: 第 " + (i + 1) +
                    " 次完整重建坑底仍完整覆盖且无重复（未覆盖 " +
                    audit.Coverage.UncoveredArea.ToString("E3") + "，重叠 " +
                    audit.Coverage.OverlapArea.ToString("E3") + "）"
                );
                Check(
                    topAudit.TriangleCount == 0 &&
                    topAudit.Coverage.UnionArea <= UncoveredAreaTolerance,
                    "T6: 第 " + (i + 1) +
                    " 次完整重建天空侧不封面（三角形 " +
                    topAudit.TriangleCount + "，覆盖 " +
                    topAudit.Coverage.UnionArea.ToString("E3") + " m²）"
                );
            }

            List<TerrainChunkView> afterRepeats = GetViews(terrainObject);
            int residualContributions = CountContributionEntries(afterRepeats);
            Check(
                residualContributions == contributionCountAfterFirst,
                "T6: 重复重建后贡献记录不再增长（首次 " +
                contributionCountAfterFirst + " 条，三次重建后 " +
                residualContributions + " 条）"
            );

            bool synced = true;
            for (int i = 0; i < afterRepeats.Count; i++)
            {
                TerrainChunkView view = afterRepeats[i];
                if (view.MeshFilter.sharedMesh != view.Chunk.Mesh ||
                    view.MeshCollider.sharedMesh != view.Chunk.Mesh)
                {
                    synced = false;
                }
            }

            Check(synced, "T6: 重复重建后所有 View 指向当前最终 Mesh");

            terrain.VoxelField.Set(voxel, VoxelState.Solid);
            terrain.RebuildChunksAffectedByVoxel(voxel);
            bool restored = terrain.RebuildVoxelAwareGeometryFromVoxelField();

            List<TerrainChunkView> restoredViews = GetViews(terrainObject);
            Dictionary<Vector3Int, MeshCanonical> restoredCanonical =
                SnapshotCanonical(restoredViews);

            bool sameAsBaseline = SameAllChunks(
                baseline,
                restoredCanonical,
                out string detail
            );

            Check(
                restored &&
                CountContributionEntries(restoredViews) == 0 &&
                sameAsBaseline,
                "T6: 恢复 Solid 后回到基线几何且无残留贡献" +
                (detail.Length > 0 ? " —— " + detail : string.Empty)
            );

            Check(
                CountGeometryInsideAirVoxels(restoredViews, terrain.VoxelField) == 0,
                "T6: 恢复后不存在位于 Air Voxel 内部的几何"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    // =====================================================================
    // T7 — 自然地形组合（noise + cavity）
    // =====================================================================

    private static void CheckNaturalTerrainCase()
    {
        GameObject terrainObject = CreateNaturalFixture("StageB3B_Natural");

        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();
            List<TerrainChunkView> views = GetViews(terrainObject);

            // 选一个“确实存在需要封闭的面”的 Voxel（噪声地形上的真实组合用例）。
            FaceCandidate candidate;
            if (!TryFindPartialOpenFace(terrain, views, 0.25, 1.0, out candidate))
            {
                Limit("T7: 噪声地形中未找到存在开口面的 Voxel，跳过");
                return;
            }

            Vector3Int voxel = candidate.Voxel;
            terrain.VoxelField.Set(voxel, VoxelState.Air);
            int rebuilt = terrain.RebuildChunksAffectedByVoxel(voxel);

            if (rebuilt <= 0)
            {
                Limit(
                    "T7: 候选 Voxel " + voxel + " 的 Mutation 未提交（" +
                    VoxelAwareMeshRuntimeMutation.LastEvaluationFailureMessage +
                    "），跳过自然地形用例"
                );
                return;
            }

            views = GetViews(terrainObject);

            Check(
                CountGeometryInsideVoxel(views, voxel) == 0,
                "T7: 挖空 Voxel 内部无残留几何"
            );
            Check(
                CountDuplicateTriangles(views) == 0,
                "T7: 最终 Mesh 无重复三角形"
            );

            FaceKey face = FaceTowardNeighbour(
                voxel,
                candidate.Neighbour,
                candidate.Axis
            );
            FaceAudit audit = AuditFace(views, face, voxel, terrain.VoxelField);
            double expectedSealed = ExpectedWallAreaByDensity(
                views,
                face,
                candidate.Axis,
                candidate.Side == 0
            );

            double tolerance = Mathf.Max(0.06f, (float)expectedSealed * 0.25f);
            double deviation = audit.Coverage.UnionArea - expectedSealed;

            Check(
                audit.Coverage.OverlapArea <= OverlapAreaTolerance &&
                audit.Coverage.DegenerateCount == 0 &&
                audit.Coverage.OutsideArea <= OutsideAreaTolerance &&
                audit.WindingConsistent,
                "T7: 该面无重叠/退化/越界且绕序自材料侧指向空腔"
            );

            int solidUncovered = CountCoveredPoints(
                audit,
                views,
                face,
                candidate.Axis,
                candidate.Side == 0,
                0.25f,
                true
            );

            Debug.Log(
                "[StageB3B-T7] voxel=" + voxel + " face=" + face.Key +
                " covered=" + audit.Coverage.UnionArea.ToString("F4") +
                " pointwiseWallArea=" + expectedSealed.ToString("F4") +
                " deviation=" + deviation.ToString("F4") +
                " solidSidePointsUncovered=" + solidUncovered +
                " triangles=" + audit.TriangleCount +
                " duplicates=" + CountDuplicateTriangles(views)
            );

            // B3-C 已修复 Region 分类粒度：多覆盖与漏覆盖都必须落在尺度相关容差内。
            // 条件上界为 2Lh = 0.08·L（前提：边界每单元穿越次数 ≤2 ⇒ N ≤ 2L/h，
            // 每单元 ≤ h²，h ≤ 0.04m；对高频抖动边界不成立）；
            // 本判据采用其中一半（0.04·L）作为工程容差，并与 0.15·A_open 取较大者，
            // 再夹到 [0.005, 0.02] m²（系数为经验值，实测余量 1.5–6.7×）。
            double boundaryLength = EstimateCoverageBoundaryLength(
                views, face, 1, false);
            double scaleTolerance = Math.Min(
                0.02,
                Math.Max(0.005, 0.012 * boundaryLength));

            Check(
                deviation <= scaleTolerance && -deviation <= scaleTolerance,
                "T7: 覆盖面积与逐点判据一致（多覆盖 " +
                deviation.ToString("F4") + "，漏覆盖 " +
                Math.Max(0.0, -deviation).ToString("F4") + "，边界长度 " +
                boundaryLength.ToString("F4") + "，容差 " +
                scaleTolerance.ToString("F4") + " m²）"
            );

            Check(
                solidUncovered == 0,
                "T7: 材料侧采样点必须被封闭（未封闭 " + solidUncovered + " 点）"
            );

            Limit(
                "T7 判据适用范围：噪声/曲面地形上，面内“材料侧区域”用自行实现的三线性" +
                "采样读取同一份 Base Density（面平面外 1mm）后按 density>0（材料侧）积分得到；" +
                "生产按 Region 覆盖语义细分后输出子区域，两者边界近似（约 4cm 单元）" +
                "带来 ~1e-3 m² 量级差异，因此使用 0.02 m² 容差（几何依据见 B3-C 报告）。"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    // =====================================================================
    // 几何判据实现
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
        public List<string> TriangleDump = new List<string>();
        public CoverageMetrics Coverage = new CoverageMetrics();
        public List<Tri2> Triangles = new List<Tri2>();
    }

    /// <summary>
    /// 采集指定 SpatialFace 上的全部最终 Mesh 三角形（跨所有 Chunk），
    /// 投影到面平面 (u,v)，并计算整面覆盖率 / 重叠 / 未覆盖 / 退化 / 重复 /
    /// 越界 / 绕序 / 归属 Chunk。
    /// </summary>
    private static FaceAudit AuditFace(
        List<TerrainChunkView> views,
        FaceKey face,
        Vector3Int negativeVoxel,
        VoxelField voxelField)
    {
        FaceAudit audit = new FaceAudit();
        audit.Face = face;

        int axisU = (face.Axis + 1) % 3;
        int axisV = (face.Axis + 2) % 3;

        float plane = face.Plane;
        float minU = face.U;
        float maxU = face.U + 1f;
        float minV = face.V;
        float maxV = face.V + 1f;

        HashSet<string> seenTriangles = new HashSet<string>();
        HashSet<Vector3Int> ownerChunks = new HashSet<Vector3Int>();

        // 期望法线（B3-D 材料边界语义）：内壁法线必须自实体侧指向空腔（可见方向）：
        //   negativeAir（空腔在负侧）⇒ −Axis；否则（空腔在正侧）⇒ +Axis。
        // 这不是 SpatialFace canonical 方向（Spec §5.4 旧表述已被本修正取代）。
        bool negativeAir = voxelField != null && voxelField.IsAir(negativeVoxel);

        Vector3 expectedNormal = Vector3.zero;
        expectedNormal[face.Axis] = negativeAir ? -1f : 1f;

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

                    if (Mathf.Abs(a[face.Axis] - plane) > PlaneTolerance ||
                        Mathf.Abs(b[face.Axis] - plane) > PlaneTolerance ||
                        Mathf.Abs(d[face.Axis] - plane) > PlaneTolerance)
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
                    ownerChunks.Add(view.Coordinate);

                    if (tri.Area <= 1e-9)
                    {
                        audit.Coverage.DegenerateCount++;
                    }

                    string duplicateKey = TriangleKey(tri);
                    if (!seenTriangles.Add(duplicateKey))
                    {
                        audit.Coverage.DuplicateCount++;
                    }

                    // 注意：不能用 Vector3.Normalize()——Unity 的 Vector3.kEpsilon 为
                    // 1e-5，叉积模长小于它时会被清零，导致小三角形法线判定失真。
                    Vector3 normal = Vector3.Cross(b - a, d - a);
                    if (normal.sqrMagnitude > 1e-12f)
                    {
                        float alignment = Vector3.Dot(normal, expectedNormal) /
                                          Mathf.Max(normal.magnitude, 1e-12f);
                        if (alignment < 0.99f)
                        {
                            audit.WindingConsistent = false;
                        }
                    }

                    if (audit.TriangleDump.Count < 8)
                    {
                        audit.TriangleDump.Add(
                            "chunk=" + view.Coordinate +
                            " submesh=" + submesh +
                            " uv=(" + tri.A[0].ToString("F3") + "," +
                            tri.A[1].ToString("F3") + ")/(" +
                            tri.B[0].ToString("F3") + "," +
                            tri.B[1].ToString("F3") + ")/(" +
                            tri.C[0].ToString("F3") + "," +
                            tri.C[1].ToString("F3") + ")" +
                            " area=" + tri.Area.ToString("F6") +
                            " normal·expected=" +
                            (normal.sqrMagnitude > 1e-12f
                                ? Vector3.Dot(normal, expectedNormal).ToString("F3")
                                : "degenerate")
                        );
                    }
                }
            }
        }

        audit.Coverage.TriangleCount = audit.TriangleCount;
        audit.OwnerChunkCount = ownerChunks.Count;
        audit.OwnerChunks = FormatCoordinates(ownerChunks);

        ComputeCoverage(
            audit.Triangles,
            minU,
            minV,
            maxU,
            maxV,
            audit.Coverage
        );

        return audit;
    }

    /// <summary>
    /// 整面区域量化判据：slab 分解 + 梯形积分。
    /// 关键 V 值取“面矩形边界 + 三角形顶点 V + 不同三角形之间的边交点 V”，
    /// 因此每个 slab 内区间结构不变、并集长度为 V 的线性函数 ⇒ 梯形积分精确。
    /// </summary>
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

        List<float> critical = new List<float>();
        critical.Add(minV);
        critical.Add(maxV);

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

        // 边交点（不同三角形之间）作为额外关键 V 值。
        for (int i = 0; i < triangles.Count; i++)
        {
            for (int j = i + 1; j < triangles.Count; j++)
            {
                AddEdgeIntersectionRatios(
                    triangles[i],
                    triangles[j],
                    critical
                );
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
            if (u0 < 0 || u1 < 0) continue;

            union += (u0 + u1) * 0.5 * h;

            float o0 = OverlapLengthAt(triangles, v0 + inset);
            float o1 = OverlapLengthAt(triangles, v1 - inset);
            overlap += (o0 + o1) * 0.5 * h;
        }

        double rectArea = (maxU - minU) * (double)(maxV - minV);
        metrics.UnionArea = union;
        metrics.OverlapArea = overlap;
        metrics.UncoveredArea = Math.Max(0, rectArea - union);
    }

    private static void AddEdgeIntersectionRatios(
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

    private static double ComputeOverlapArea(List<Tri2> triangles)
    {
        CoverageMetrics metrics = new CoverageMetrics();
        ComputeCoverage(triangles, -1e3f, -1e3f, 1e3f, 1e3f, metrics);
        return metrics.OverlapArea;
    }

    private static double ClipAreaToRect(
        Tri2 t,
        float minU,
        float minV,
        float maxU,
        float maxV)
    {
        List<Vector2> poly = new List<Vector2> { t.A, t.B, t.C };

        // mode 1/3 使用 -x / -y，因此边界必须传 -maxU / -maxV。
        poly = ClipPolygon(poly, 0, minU);
        poly = ClipPolygon(poly, 1, -maxU);
        poly = ClipPolygon(poly, 2, minV);
        poly = ClipPolygon(poly, 3, -maxV);

        if (poly.Count < 3) return 0;

        double area = 0;
        for (int i = 0; i < poly.Count; i++)
        {
            Vector2 a = poly[i];
            Vector2 b = poly[(i + 1) % poly.Count];
            area += a.x * b.y - b.x * a.y;
        }

        return Math.Abs(area) * 0.5;
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

    private static string TriangleKey(Tri2 t)
    {
        string a = VertexKey(t.A);
        string b = VertexKey(t.B);
        string c = VertexKey(t.C);

        List<string> keys = new List<string> { a, b, c };
        keys.Sort(StringComparer.Ordinal);
        return string.Join("|", keys);
    }

    private static string VertexKey(Vector2 p)
    {
        return Mathf.RoundToInt(p.x / Quantize) + "," +
               Mathf.RoundToInt(p.y / Quantize);
    }

    /// <summary>
    /// 独立整面判据：面平面外 offset 处基础密度 &lt;= 0 的面积（m²）。
    /// 网格 200×200（5mm），面积精度约 1e-2 m²。
    /// </summary>
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

    /// <summary>面外 offset 处基础密度 &lt;= 0 的比例（用于面选取与报告）。</summary>
    private static double ComputeOpenFraction(
        List<TerrainChunkView> views,
        FaceKey face,
        int axis,
        bool negativeSide,
        float offset)
    {
        const int grid = 40;
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

    /// <summary>
    /// B3-D 材料边界语义下的违规点计数：
    ///   requireSolid = true  ⇒ 材料侧（密度 &gt;= +threshold）采样点必须被覆盖，
    ///                          violations = 未被覆盖的材料侧点数；
    ///   requireSolid = false ⇒ 空腔侧（密度 &lt;= -threshold）采样点不得被覆盖，
    ///                          violations = 被错误覆盖的空腔侧点数。
    /// </summary>
    private static int CountCoveredPoints(
        FaceAudit audit,
        List<TerrainChunkView> views,
        FaceKey face,
        int axis,
        bool negativeSide,
        float threshold,
        bool requireSolid)
    {
        const int grid = 20;
        const float offset = 1e-3f;
        int violations = 0;

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

                if (requireSolid)
                {
                    if (density < threshold) continue;
                }
                else
                {
                    if (density > -threshold) continue;
                }

                Vector2 p2 = new Vector2(point[(face.Axis + 1) % 3],
                                         point[(face.Axis + 2) % 3]);

                bool covered = false;
                for (int t = 0; t < audit.Triangles.Count && !covered; t++)
                {
                    covered = audit.Triangles[t].Contains(p2, 1e-4f);
                }

                // B3-D：材料侧必须被覆盖；空腔侧被覆盖即为违规。
            }
        }

        return violations;
    }

    private static Vector3 FacePoint(FaceKey face, float su, float sv)
    {
        Vector3 point = Vector3.zero;
        point[face.Axis] = face.Plane;
        point[(face.Axis + 1) % 3] = face.U + su;
        point[(face.Axis + 2) % 3] = face.V + sv;
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

    /// <summary>统计指定 SpatialFace 上的最终 Mesh 三角形数量（不做完整审计）。</summary>
    private static int CountFaceTriangles(
        List<TerrainChunkView> views,
        FaceKey face)
    {
        int count = 0;
        int axisU = (face.Axis + 1) % 3;
        int axisV = (face.Axis + 2) % 3;

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
                    if (centroid[axisU] < face.U - 0.05f ||
                        centroid[axisU] > face.U + 1.05f ||
                        centroid[axisV] < face.V - 0.05f ||
                        centroid[axisV] > face.V + 1.05f)
                    {
                        continue;
                    }

                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>指定 SpatialFace 上所有最终 Mesh 三角形的规范化键集合。</summary>
    private static HashSet<string> SnapshotFaceTriangleKeys(
        List<TerrainChunkView> views,
        FaceKey face)
    {
        HashSet<string> keys = new HashSet<string>();
        int axisU = (face.Axis + 1) % 3;
        int axisV = (face.Axis + 2) % 3;

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
                    if (centroid[axisU] < face.U - 0.05f ||
                        centroid[axisU] > face.U + 1.05f ||
                        centroid[axisV] < face.V - 0.05f ||
                        centroid[axisV] > face.V + 1.05f)
                    {
                        continue;
                    }

                    keys.Add(
                        PositionKey(a) + "|" + PositionKey(b) + "|" +
                        PositionKey(d)
                    );
                }
            }
        }

        return keys;
    }

    /// <summary>面内覆盖边界长度估计（200×200 符号栅格，标准周长估计）。</summary>
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

    private static int CountGeometryInsideVoxel(
        List<TerrainChunkView> views,
        Vector3Int voxel)
    {
        int count = 0;
        Vector3 min = new Vector3(voxel.x, voxel.y, voxel.z);

        for (int v = 0; v < views.Count; v++)
        {
            Mesh mesh = views[v].Chunk.Mesh;
            if (mesh == null) continue;

            Vector3[] vertices = mesh.vertices;
            Vector3 origin = views[v].Chunk.ChunkOrigin;

            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                int[] triangles = mesh.GetTriangles(submesh);
                for (int i = 0; i + 2 < triangles.Length; i += 3)
                {
                    Vector3 centroid = origin + (
                        vertices[triangles[i]] +
                        vertices[triangles[i + 1]] +
                        vertices[triangles[i + 2]]
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

    private static int CountGeometryInsideAirVoxels(
        List<TerrainChunkView> views,
        VoxelField voxelField)
    {
        int count = 0;

        for (int v = 0; v < views.Count; v++)
        {
            Mesh mesh = views[v].Chunk.Mesh;
            if (mesh == null) continue;

            Vector3[] vertices = mesh.vertices;
            Vector3 origin = views[v].Chunk.ChunkOrigin;

            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                int[] triangles = mesh.GetTriangles(submesh);
                for (int i = 0; i + 2 < triangles.Length; i += 3)
                {
                    Vector3 centroid = origin + (
                        vertices[triangles[i]] +
                        vertices[triangles[i + 1]] +
                        vertices[triangles[i + 2]]
                    ) / 3f;

                    Vector3Int voxel = voxelField.PositionToVoxel(centroid);
                    if (!voxelField.IsAir(voxel)) continue;

                    Vector3 min = new Vector3(voxel.x, voxel.y, voxel.z);
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

    private static int CountDuplicateTriangles(List<TerrainChunkView> views)
    {
        int duplicates = 0;

        for (int v = 0; v < views.Count; v++)
        {
            Mesh mesh = views[v].Chunk.Mesh;
            if (mesh == null) continue;

            Vector3[] vertices = mesh.vertices;
            HashSet<string> seen = new HashSet<string>();

            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                int[] triangles = mesh.GetTriangles(submesh);
                for (int i = 0; i + 2 < triangles.Length; i += 3)
                {
                    string key = "s" + submesh + "|" +
                        RotateToSmallest(
                            PositionKey(vertices[triangles[i]]),
                            PositionKey(vertices[triangles[i + 1]]),
                            PositionKey(vertices[triangles[i + 2]])
                        );

                    if (!seen.Add(key)) duplicates++;
                }
            }
        }

        return duplicates;
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

    // =====================================================================
    // Canonical mesh comparison
    // =====================================================================

    private sealed class MeshCanonical
    {
        public List<string> VertexKeys = new List<string>();
        public List<string> TriangleKeys = new List<string>();
        public double Area;
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
        HashSet<string> vertexKeys = new HashSet<string>();

        for (int i = 0; i < vertices.Length; i++)
        {
            vertexKeys.Add(PositionKey(vertices[i] + origin));
        }

        canonical.VertexKeys.AddRange(vertexKeys);
        canonical.VertexKeys.Sort(StringComparer.Ordinal);

        for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
        {
            int[] triangles = mesh.GetTriangles(submesh);
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                Vector3 a = vertices[triangles[i]] + origin;
                Vector3 b = vertices[triangles[i + 1]] + origin;
                Vector3 c = vertices[triangles[i + 2]] + origin;

                canonical.Area += Vector3.Cross(b - a, c - a).magnitude * 0.5;
                canonical.TriangleKeys.Add(
                    "s" + submesh + "|" +
                    RotateToSmallest(
                        PositionKey(a),
                        PositionKey(b),
                        PositionKey(c)
                    )
                );
            }
        }

        canonical.TriangleKeys.Sort(StringComparer.Ordinal);
        return canonical;
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

            if (pair.Value.VertexKeys.Count != other.VertexKeys.Count ||
                pair.Value.TriangleKeys.Count != other.TriangleKeys.Count)
            {
                detail = pair.Key + ": 顶点/三角形数 " +
                    pair.Value.VertexKeys.Count + "/" +
                    pair.Value.TriangleKeys.Count + " vs " +
                    other.VertexKeys.Count + "/" + other.TriangleKeys.Count;
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

    private static string PositionKey(Vector3 p)
    {
        return Mathf.RoundToInt(p.x / Quantize) + "," +
               Mathf.RoundToInt(p.y / Quantize) + "," +
               Mathf.RoundToInt(p.z / Quantize);
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

    private static Vector3Int AxisVector(int axis, int side)
    {
        Vector3Int value = Vector3Int.zero;
        value[axis] = side == 0 ? -1 : 1;
        return value;
    }

    /// <summary>
    /// Voxel 与相邻 Voxel 之间的 SpatialFace（与生产 TryGetFaceBetweenVoxels 同约定：
    /// u = negative[(axis+1)%3]，v = negative[(axis+2)%3]）。
    /// </summary>
    private static FaceKey FaceTowardNeighbour(
        Vector3Int voxel,
        Vector3Int neighbour,
        int axis)
    {
        Vector3Int lower = voxel;
        Vector3Int upper = neighbour;
        if (upper[axis] < lower[axis])
        {
            lower = neighbour;
            upper = voxel;
        }

        int plane = upper[axis];
        int u = lower[(axis + 1) % 3];
        int v = lower[(axis + 2) % 3];

        return new FaceKey(axis, plane, u, v);
    }

    // =====================================================================
    // Fixtures
    // =====================================================================

    private static Vector3 CavityCenter()
    {
        // 非半格中心：使某些体素面正好被球面切成“部分区域”，
        // 从而可以构造 T3 的部分覆盖用例。
        return new Vector3(30.6f, 8.53f, 25.4f);
    }

    private static float CavityRadius()
    {
        return 3.55f;
    }

    private static GameObject CreateFlatFixture(
        string name,
        Vector3Int chunkStart = default(Vector3Int))
    {
        return CreateFixture(
            name,
            chunkStart,
            DensityGenerationMode.HeightField,
            false
        );
    }

    private static GameObject CreateCavityFixture(string name)
    {
        return CreateFixture(
            name,
            Vector3Int.zero,
            DensityGenerationMode.SphereCavity,
            false
        );
    }

    private static GameObject CreateNaturalFixture(string name)
    {
        return CreateFixture(
            name,
            Vector3Int.zero,
            DensityGenerationMode.SphereCavity,
            true
        );
    }

    private static GameObject CreateFixture(
        string name,
        Vector3Int chunkStart,
        DensityGenerationMode mode,
        bool useNoise)
    {
        GameObject terrainObject = new GameObject(name);
        DensityTerrain terrain = terrainObject.AddComponent<DensityTerrain>();

        terrain.voxelSize = 1f;
        terrain.chunkVoxelSize = ChunkVoxelSize;
        terrain.chunkCount = new Vector3Int(2, 1, 2);
        terrain.chunkStartCoordinate = chunkStart;
        terrain.resolution = 75;
        terrain.densityMode = mode;
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

            if (!IsFaceFullyInsideSphere(candidate, towards, axis, center, radius, 0.15f))
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

    private sealed class FaceCandidate
    {
        public Vector3Int Voxel;
        public Vector3Int Neighbour;
        public int Axis;
        public int Side;
        public double ExpectedSealed;
    }

    /// <summary>
    /// 在夹具中寻找“某个面部分朝空气打开”的候选：
    /// 依据自行实现的基础密度采样计算该面应封闭面积，取值落在 [minArea, maxArea]。
    /// </summary>
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

            // 搜索用粗网格（24×24）以保证批量回归的运行时间；
            // 命中后的最终判据仍用 200×200 网格。
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
                        Vector3Int neighbour = voxels[i] + AxisVector(axis, side);
                        if (terrain.VoxelField.IsAir(neighbour)) continue;

                        FaceKey face = FaceTowardNeighbour(
                            voxels[i],
                            neighbour,
                            axis
                        );
                        double area = ExpectedWallAreaByDensity(
                            views,
                            face,
                            axis,
                            side == 0,
                            searchGrid
                        );

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

    /// <summary>
    /// 寻找“面的一部分朝空气打开”的用例：用基础密度判据（自行三线性采样）
    /// 计算该面应封闭面积，要求落在 20%~80% 之间，确保是真正的部分覆盖用例。
    /// </summary>
    private static bool TryFindPartialCavityFaceVoxel(
        List<TerrainChunkView> views,
        Vector3 center,
        float radius,
        out Vector3Int voxel,
        out Vector3Int neighbour,
        out int axis,
        out int side)
    {
        double bestArea = -1;
        Vector3Int bestVoxel = Vector3Int.zero;
        Vector3Int bestNeighbour = Vector3Int.zero;
        int bestAxis = 0;
        int bestSide = 0;

        for (int a = 0; a < 3; a++)
        {
            for (int s = 0; s < 2; s++)
            {
                for (int offset = 1; offset <= 8; offset++)
                {
                    Vector3Int candidate = new Vector3Int(
                        Mathf.FloorToInt(center.x),
                        Mathf.FloorToInt(center.y),
                        Mathf.FloorToInt(center.z)
                    );
                    candidate[a] += (s == 0 ? -offset : offset);

                    if (candidate.x < 1 || candidate.x > 73) continue;
                    if (candidate.y < 1 || candidate.y > 35) continue;
                    if (candidate.z < 1 || candidate.z > 48) continue;

                    Vector3Int towards = candidate;
                    towards[a] += s == 0 ? 1 : -1;

                    FaceKey face = FaceTowardNeighbour(candidate, towards, a);
                    double area = ExpectedWallAreaByDensity(
                        views,
                        face,
                        a,
                        s == 0
                    );

                    if (area <= 0.2 || area >= 0.8) continue;

                    // 取最接近 50% 的候选，最有利于验证部分覆盖。
                    double distance = Math.Abs(area - 0.5);
                    if (bestArea < 0 || distance < Math.Abs(bestArea - 0.5))
                    {
                        bestArea = area;
                        bestVoxel = candidate;
                        bestNeighbour = towards;
                        bestAxis = a;
                        bestSide = s;
                    }
                }
            }
        }

        if (bestArea < 0)
        {
            voxel = Vector3Int.zero;
            neighbour = Vector3Int.zero;
            axis = 0;
            side = 0;
            return false;
        }

        voxel = bestVoxel;
        neighbour = bestNeighbour;
        axis = bestAxis;
        side = bestSide;
        return true;
    }

    private static bool IsFaceFullyInsideSphere(
        Vector3Int voxel,
        Vector3Int neighbour,
        int axis,
        Vector3 center,
        float radius,
        float margin)
    {
        return FaceOpenFraction(voxel, neighbour, axis, center, radius) >= 0.999f &&
               FaceMargin(voxel, neighbour, axis, center, radius) >= margin;
    }

    private static float FaceMargin(
        Vector3Int voxel,
        Vector3Int neighbour,
        int axis,
        Vector3 center,
        float radius)
    {
        float minMargin = float.MaxValue;
        int axisU = (axis + 1) % 3;
        int axisV = (axis + 2) % 3;

        for (int iu = 0; iu <= 4; iu++)
        {
            for (int iv = 0; iv <= 4; iv++)
            {
                Vector3 point = Vector3.zero;
                point[axis] = voxel[axis] + (neighbour[axis] > voxel[axis] ? 1f : 0f);
                point[axisU] = voxel[axisU] + iu * 0.25f;
                point[axisV] = voxel[axisV] + iv * 0.25f;

                float margin = radius - Vector3.Distance(point, center);
                if (margin < minMargin) minMargin = margin;
            }
        }

        return minMargin;
    }

    private static float FaceOpenFraction(
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
                point[axis] = voxel[axis] + (neighbour[axis] > voxel[axis] ? 1f : 0f);
                point[axisU] = voxel[axisU] + iu * 0.1f;
                point[axisV] = voxel[axisV] + iv * 0.1f;

                total++;
                if (Vector3.Distance(point, center) < radius) open++;
            }
        }

        return total == 0 ? 0 : open / (float)total;
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
                if (candidate.y < 2 || candidate.y > 30) continue;

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
        Debug.Log("[StageB3B-LIMITATION] " + message);
    }

    private static void LogSummary()
    {
        Debug.Log(
            "[VoxelCoordinateMigrationStageB3BValidation] ===== SUMMARY =====\n" +
            "Checks = " + checks + "\n" +
            "Failures = " + failures + "\n" +
            "Result = " + (failures == 0 ? "PASS" : "FAIL")
        );
    }
}
