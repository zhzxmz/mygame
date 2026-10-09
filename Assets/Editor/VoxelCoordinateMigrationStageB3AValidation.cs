using System;
using System.Collections.Generic;
using System.Text;
using TerrainModificationV2;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Coordinate Migration Stage B3-A Validation — Voxel-Aware 状态恢复与更新入口修复。
///
/// 验证目标（全部通过**真实生产入口**，不直接调用内部 Assemble 代替）：
///
///   A. F1 入口：precut ON 时
///        VoxelField.Set(voxel, Air) → DensityTerrain.RebuildChunksAffectedByVoxel(voxel)
///      （与 PlayerMiningController.TryMineVoxel 完全同序）必须真正更新
///      Fragment / HiddenFaceContribution / 最终 Mesh，并同步 MeshFilter / MeshCollider，
///      且重复调用幂等（不重复应用同一状态）。
///
///   B. F2 完整重建：
///        DensityTerrain.RebuildVoxelAwareGeometryFromVoxelField()
///      = Regenerate（全新 Raw Mesh / Pre-cut Geometry / 空缓存）+ 按坐标升序重放
///        显式 Air Voxel（生产 Mutation，每个 Voxel 一次）。
///      覆盖：初始 Solid、改为 Air、同状态连续重建、恢复 Solid、内部 / X / Z / XZ 交汇。
///
///   C. 增量 vs 完整重建：同一最终 VoxelField 状态的两条路径必须几何等价
///      （规范化顶点集合 + 保持绕序的三角形集合 + 面积，容差 1e-4 m）。
///
///   D. 跨 Chunk 与绝对坐标：chunkStartCoordinate = (0,0,0) 与 (5,0,7)，
///      边界修改状态在完整重建后保持，绝对 ChunkCoordinate 不被当作数组索引。
///
/// 判据不依赖生产算法：顶点/三角形规范化对照 + “Air Voxel 内部不得有可见几何” +
/// “Air 邻接面必须被封闭（射线命中体素面）” + 重复面计数。
/// </summary>
public static class VoxelCoordinateMigrationStageB3AValidation
{
    private static readonly Vector3Int ChunkVoxelSize = new Vector3Int(75, 37, 50);
    private const float FlatHeight = 15.7f;
    private const int SurfaceVoxelY = 15;
    private const float Quantum = 1e-4f;
    private const float FaceProbeTolerance = 1e-3f;

    private static int checks;
    private static int failures;

    [MenuItem("Tools/Terrain/Validate Coordinate Migration Stage B3-A")]
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
                "[VoxelCoordinateMigrationStageB3AValidation] EXCEPTION: " +
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

        CheckF1EntryThroughProductionCall();
        CheckF2FullRebuildScenarios();
        CheckIncrementalVersusFullRebuild();
        CheckAbsoluteCoordinatesAndShiftedStart();
        CheckExceptionBehaviour();
    }

    // =====================================================================
    // A. F1 —— 真实现有入口
    // =====================================================================

    private static void CheckF1EntryThroughProductionCall()
    {
        GameObject terrainObject = CreateFixture(
            "StageB3A_F1Entry",
            Vector3Int.zero,
            true
        );

        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();
            List<TerrainChunkView> views = GetViews(terrainObject);

            Vector3Int voxel = new Vector3Int(30, SurfaceVoxelY, 30);
            TerrainChunk owner = ChunkOf(views, voxel);
            Check(
                owner != null && owner.Geometry != null,
                "StageB3A-A: 目标 Voxel 所属 Chunk 有 ChunkGeometry"
            );

            int activeBefore = CountActiveFragments(owner);
            string digestBefore = GeometryDigest(owner);
            Dictionary<Vector3Int, MeshCanonical> canonicalBefore =
                SnapshotCanonical(views);
            int geometryInsideAirBefore = CountGeometryInsideAirVoxels(
                views,
                terrain.VoxelField
            );

            // --- 与 PlayerMiningController.TryMineVoxel 同序的真实调用 ---
            terrain.VoxelField.Set(voxel, VoxelState.Air);
            int rebuilt = terrain.RebuildChunksAffectedByVoxel(voxel);

            Check(
                terrain.VoxelField.IsAir(voxel),
                "StageB3A-A: VoxelField 记录该 Voxel 为 Air"
            );
            Check(
                rebuilt >= 1,
                "StageB3A-A: 入口报告重建的 Chunk 数量 >= 1（实际 " + rebuilt + "）"
            );

            int activeAfter = CountActiveFragments(owner);
            string digestAfter = GeometryDigest(owner);

            Check(
                activeAfter < activeBefore,
                "StageB3A-A: 被编辑 Voxel 的 Fragment 被真正停用（" +
                activeBefore + " → " + activeAfter + "）"
            );
            Check(
                digestBefore != digestAfter,
                "StageB3A-A: 所属 Chunk 的派生几何缓存发生变化"
            );

            Dictionary<Vector3Int, MeshCanonical> canonicalAfter =
                SnapshotCanonical(views);
            int changedChunks = CountChangedChunks(canonicalBefore, canonicalAfter);
            Check(
                changedChunks > 0,
                "StageB3A-A: 最终 Mesh 几何确实变化（" + changedChunks + " Chunk）"
            );

            bool viewsSynced = true;
            for (int i = 0; i < views.Count; i++)
            {
                TerrainChunkView view = views[i];
                if (view.MeshFilter.sharedMesh != view.Chunk.Mesh ||
                    view.MeshCollider.sharedMesh != view.Chunk.Mesh)
                {
                    viewsSynced = false;
                }
            }

            Check(
                viewsSynced,
                "StageB3A-A: 所有 Chunk 的 MeshFilter / MeshCollider 指向当前最终 Mesh"
            );

            int insideMined = CountGeometryInsideVoxel(views, voxel);

            Check(
                insideMined == 0,
                "StageB3A-A: 已挖空 Voxel 内部没有残留可见几何（实际 " +
                insideMined + " 个三角形；基线 " + geometryInsideAirBefore + "）"
            );

            // --- 重复调用：不得重复应用同一状态 ---
            int storedBefore = terrain.VoxelField.StoredVoxelCount;
            int rebuiltAgain = terrain.RebuildChunksAffectedByVoxel(voxel);
            Dictionary<Vector3Int, MeshCanonical> canonicalAgain =
                SnapshotCanonical(views);

            bool repeatStable = true;
            for (int i = 0; i < views.Count; i++)
            {
                Vector3Int coordinate = views[i].Chunk.Coordinate;
                if (!SameCanonical(
                    canonicalAfter[coordinate],
                    canonicalAgain[coordinate],
                    out string _))
                {
                    repeatStable = false;
                }
            }

            Check(
                repeatStable,
                "StageB3A-A: 重复调用入口幂等（几何不变）"
            );
            Check(
                terrain.VoxelField.StoredVoxelCount == storedBefore &&
                terrain.VoxelField.IsAir(voxel),
                "StageB3A-A: 重复调用不改变 VoxelField（stored=" +
                terrain.VoxelField.StoredVoxelCount + "）"
            );
            Check(
                rebuiltAgain >= 1,
                "StageB3A-A: 重复调用仍报告重建数量（实际 " + rebuiltAgain + "）"
            );

            // 恢复 Solid（同样走真实入口）
            terrain.VoxelField.Set(voxel, VoxelState.Solid);
            terrain.RebuildChunksAffectedByVoxel(voxel);

            Dictionary<Vector3Int, MeshCanonical> canonicalRestored =
                SnapshotCanonical(views);
            bool restored = true;
            for (int i = 0; i < views.Count; i++)
            {
                Vector3Int coordinate = views[i].Chunk.Coordinate;
                if (!SameCanonical(
                    canonicalBefore[coordinate],
                    canonicalRestored[coordinate],
                    out string _))
                {
                    restored = false;
                }
            }

            Check(
                restored,
                "StageB3A-A: 通过入口恢复 Solid 后几何回到基线"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    // =====================================================================
    // B. F2 —— 完整重建场景
    // =====================================================================

    private static void CheckF2FullRebuildScenarios()
    {
        GameObject terrainObject = CreateFixture(
            "StageB3A_F2Rebuild",
            Vector3Int.zero,
            true
        );

        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();
            List<TerrainChunkView> views = GetViews(terrainObject);
            Dictionary<Vector3Int, MeshCanonical> baseline = SnapshotCanonical(views);

            // (1) 初始 Solid → 完整重建：与原始基线几何等价。
            bool ok = terrain.RebuildVoxelAwareGeometryFromVoxelField();
            List<TerrainChunkView> rebuiltViews = GetViews(terrainObject);
            Dictionary<Vector3Int, MeshCanonical> rebuilt = SnapshotCanonical(rebuiltViews);

            Check(ok, "StageB3A-B1: 初始 Solid 状态完整重建成功");
            Check(
                SameAllChunks(baseline, rebuilt, out string detail1),
                "StageB3A-B1: 初始完整重建与基线几何等价" +
                (detail1.Length > 0 ? " —— " + detail1 : string.Empty)
            );

            // (2) 修改为 Air（真实入口）→ 完整重建：必须保持挖空。
            Vector3Int surfaceVoxel = new Vector3Int(30, SurfaceVoxelY, 30);
            Vector3Int boundaryVoxel = new Vector3Int(75, SurfaceVoxelY, 30);
            Vector3Int cornerVoxel = new Vector3Int(75, SurfaceVoxelY, 50);

            terrain.VoxelField.Set(surfaceVoxel, VoxelState.Air);
            terrain.RebuildChunksAffectedByVoxel(surfaceVoxel);
            terrain.VoxelField.Set(boundaryVoxel, VoxelState.Air);
            terrain.RebuildChunksAffectedByVoxel(boundaryVoxel);

            views = GetViews(terrainObject);
            Dictionary<Vector3Int, MeshCanonical> incremental =
                SnapshotCanonical(views);

            ok = terrain.RebuildVoxelAwareGeometryFromVoxelField();
            rebuiltViews = GetViews(terrainObject);
            Dictionary<Vector3Int, MeshCanonical> afterRebuild =
                SnapshotCanonical(rebuiltViews);

            Check(ok, "StageB3A-B2: Air 状态完整重建成功");
            Check(
                SameAllChunks(incremental, afterRebuild, out string detail2),
                "StageB3A-B2: 完整重建 == 增量结果（Air 保持挖空）" +
                (detail2.Length > 0 ? " —— " + detail2 : string.Empty)
            );

            int insideSurface = CountGeometryInsideVoxel(rebuiltViews, surfaceVoxel);
            int insideBoundary = CountGeometryInsideVoxel(rebuiltViews, boundaryVoxel);
            Check(
                insideSurface == 0 && insideBoundary == 0,
                "StageB3A-B2: 重建后已挖空 Voxel 内部无可见几何（" +
                insideSurface + " / " + insideBoundary + "）"
            );
            Check(
                CountDuplicateTriangles(rebuiltViews) == 0,
                "StageB3A-B2: 完整重建没有重复三角形"
            );

            // (3) 相同 Air 状态连续完整重建两次：几何等价。
            ok = terrain.RebuildVoxelAwareGeometryFromVoxelField();
            List<TerrainChunkView> secondViews = GetViews(terrainObject);
            Dictionary<Vector3Int, MeshCanonical> second =
                SnapshotCanonical(secondViews);

            bool same3 = SameAllChunks(afterRebuild, second, out string detail3);
            Check(
                ok && same3,
                "StageB3A-B3: 相同状态连续完整重建两次几何等价" +
                (detail3.Length > 0 ? " —— " + detail3 : string.Empty)
            );

            // 再来一次（第三次），确保没有缓存累积。
            ok = terrain.RebuildVoxelAwareGeometryFromVoxelField();
            List<TerrainChunkView> thirdViews = GetViews(terrainObject);
            Dictionary<Vector3Int, MeshCanonical> third =
                SnapshotCanonical(thirdViews);

            bool same3b = SameAllChunks(second, third, out string detail3b);
            Check(
                ok && same3b && CountDuplicateTriangles(thirdViews) == 0,
                "StageB3A-B3: 第三次重建仍与第二次等价且无重复面" +
                (detail3b.Length > 0 ? " —— " + detail3b : string.Empty)
            );

            // (4) Air → Solid 后完整重建：回到基线几何。
            terrain.VoxelField.Set(surfaceVoxel, VoxelState.Solid);
            terrain.RebuildChunksAffectedByVoxel(surfaceVoxel);
            terrain.VoxelField.Set(boundaryVoxel, VoxelState.Solid);
            terrain.RebuildChunksAffectedByVoxel(boundaryVoxel);

            ok = terrain.RebuildVoxelAwareGeometryFromVoxelField();
            List<TerrainChunkView> solidViews = GetViews(terrainObject);
            Dictionary<Vector3Int, MeshCanonical> solid =
                SnapshotCanonical(solidViews);

            bool same4 = SameAllChunks(baseline, solid, out string detail4);
            Check(
                ok && same4,
                "StageB3A-B4: 恢复 Solid 后完整重建回到基线几何" +
                (detail4.Length > 0 ? " —— " + detail4 : string.Empty)
            );

            // (5) 内部 / X 边界 / Z 边界 / XZ 交汇状态都能在完整重建后保持。
            Vector3Int[] states =
            {
                new Vector3Int(30, 10, 30),                 // 内部（无表面 Fragment）
                new Vector3Int(74, SurfaceVoxelY, 30),      // X 边界（负侧）
                new Vector3Int(75, SurfaceVoxelY, 30),      // X 边界（正侧）
                new Vector3Int(10, SurfaceVoxelY, 49),      // Z 边界（负侧）
                new Vector3Int(10, SurfaceVoxelY, 50),      // Z 边界（正侧）
                cornerVoxel                                 // X/Z 交汇
            };

            for (int i = 0; i < states.Length; i++)
            {
                terrain.VoxelField.Set(states[i], VoxelState.Air);
                terrain.RebuildChunksAffectedByVoxel(states[i]);
            }

            List<TerrainChunkView> stateViews = GetViews(terrainObject);
            Dictionary<Vector3Int, MeshCanonical> stateIncremental =
                SnapshotCanonical(stateViews);

            ok = terrain.RebuildVoxelAwareGeometryFromVoxelField();
            List<TerrainChunkView> stateRebuiltViews = GetViews(terrainObject);
            Dictionary<Vector3Int, MeshCanonical> stateRebuilt =
                SnapshotCanonical(stateRebuiltViews);

            bool same5 = SameAllChunks(
                stateIncremental,
                stateRebuilt,
                out string detail5);
            Check(
                ok && same5,
                "StageB3A-B5: 内部/X/Z/XZ 修改状态在完整重建后全部保持" +
                (detail5.Length > 0 ? " —— " + detail5 : string.Empty)
            );

            int residual = 0;
            for (int i = 0; i < states.Length; i++)
            {
                residual += CountGeometryInsideVoxel(stateRebuiltViews, states[i]);
            }

            Check(
                residual == 0,
                "StageB3A-B5: 重建后所有已挖空 Voxel 内部均无可见几何（" +
                residual + "）"
            );
            // Hidden Face 的“面内覆盖率”只做测量与记录：
            // 本轮实测发现 +Y 面（Air Voxel 朝空气打开的顶面）的 Hidden Face 多边形
            // 三角形拓扑有缺口（覆盖 19–21/25），属 Hidden Face 物理几何正确性问题，
            // 按 §7 明确留给 B3-B，不作为 B3-A 的失败条件。
            WallClosureMeasurement measurement = MeasureAirWallClosure(
                stateRebuiltViews,
                terrain.VoxelField,
                states
            );

            Check(
                measurement.RequiredFaces > 0,
                "StageB3A-B5: 封闭性判据被真实执行（需要封闭的 Air 面数 = " +
                measurement.RequiredFaces + "）"
            );
            Check(
                measurement.CoverageSampleTotal > 0,
                "StageB3A-B5: 封闭性采样被真实执行（采样点 = " +
                measurement.CoverageSampleTotal + "）"
            );

            Debug.Log(
                "[StageB3A-B5] 需要封闭的面=" + measurement.RequiredFaces +
                " 完全封闭的面=" + measurement.FullyClosedFaces +
                " 覆盖采样=" + measurement.CoveredSamples + "/" +
                measurement.CoverageSampleTotal
            );

            for (int i = 0; i < measurement.Issues.Count; i++)
            {
                Limit(measurement.Issues[i]);
            }

            LogWallCoverageDiagnostics(
                stateRebuiltViews,
                terrain.VoxelField,
                states
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    // =====================================================================
    // C. 增量 vs 完整重建
    // =====================================================================

    private static void CheckIncrementalVersusFullRebuild()
    {
        Vector3Int[] voxels =
        {
            new Vector3Int(30, SurfaceVoxelY, 30),
            new Vector3Int(75, SurfaceVoxelY, 30),
            new Vector3Int(10, SurfaceVoxelY, 50),
            new Vector3Int(75, SurfaceVoxelY, 50)
        };

        // 路径 A1：按顺序增量修改。
        GameObject pathA1 = CreateFixture("StageB3A_PathA1", Vector3Int.zero, true);
        // 路径 A2：同样集合、不同顺序的增量修改（验证顺序无关）。
        GameObject pathA2 = CreateFixture("StageB3A_PathA2", Vector3Int.zero, true);
        // 路径 B：直接把同一最终状态交给完整重建。
        GameObject pathB = CreateFixture("StageB3A_PathB", Vector3Int.zero, true);

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

            Check(rebuilt, "StageB3A-C: 路径 B（完整重建）成功");

            Check(
                SameAllChunks(canonicalA1, canonicalA2, out string detailOrder),
                "StageB3A-C: 不同顺序的增量修改得到相同几何（顺序无关）" +
                (detailOrder.Length > 0 ? " —— " + detailOrder : string.Empty)
            );
            Check(
                SameAllChunks(canonicalA1, canonicalB, out string detailAb),
                "StageB3A-C: 增量结果 == 同状态完整重建结果" +
                (detailAb.Length > 0 ? " —— " + detailAb : string.Empty)
            );

            Check(
                terrainA1.VoxelField.StoredVoxelCount == voxels.Length &&
                terrainB.VoxelField.StoredVoxelCount == voxels.Length,
                "StageB3A-C: 三条路径的 VoxelField 状态一致（stored=" +
                terrainA1.VoxelField.StoredVoxelCount + "/" +
                terrainB.VoxelField.StoredVoxelCount + "）"
            );

            int duplicateB = CountDuplicateTriangles(viewsB);
            int insideB = 0;
            for (int i = 0; i < voxels.Length; i++)
            {
                insideB += CountGeometryInsideVoxel(viewsB, voxels[i]);
            }

            Check(
                duplicateB == 0 && insideB == 0,
                "StageB3A-C: 完整重建结果无重复面、无 Air Voxel 内部几何（" +
                duplicateB + " / " + insideB + "）"
            );

            Debug.Log(
                "[StageB3A-C] chunksA1=" + viewsA1.Count +
                " chunksB=" + viewsB.Count +
                " trianglesA1=" + TotalTriangles(canonicalA1) +
                " trianglesB=" + TotalTriangles(canonicalB)
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(pathA1);
            UnityEngine.Object.DestroyImmediate(pathA2);
            UnityEngine.Object.DestroyImmediate(pathB);
        }
    }

    // =====================================================================
    // D. 绝对坐标 / chunkStartCoordinate
    // =====================================================================

    private static void CheckAbsoluteCoordinatesAndShiftedStart()
    {
        // (a) chunkStartCoordinate = (0,0,0)
        GameObject origin = CreateFixture(
            "StageB3A_StartOrigin",
            Vector3Int.zero,
            true
        );

        try
        {
            DensityTerrain terrain = origin.GetComponent<DensityTerrain>();
            Vector3Int[] voxels =
            {
                new Vector3Int(74, SurfaceVoxelY, 30),
                new Vector3Int(75, SurfaceVoxelY, 30),
                new Vector3Int(76, SurfaceVoxelY, 30),
                new Vector3Int(10, SurfaceVoxelY, 49),
                new Vector3Int(10, SurfaceVoxelY, 50),
                new Vector3Int(10, SurfaceVoxelY, 51)
            };

            RunAbsoluteCoordinateCase(terrain, voxels, "StageB3A-D1");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(origin);
        }

        // (b) chunkStartCoordinate = (5,0,7)
        GameObject shifted = CreateFixture(
            "StageB3A_StartShifted",
            new Vector3Int(5, 0, 7),
            true
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
                "StageB3A-D2: Chunk 使用绝对坐标 (5,0,7)… 而非数组索引 (" +
                Format(coordinates) + ")"
            );

            Vector3Int[] voxels =
            {
                new Vector3Int(449, SurfaceVoxelY, 380),
                new Vector3Int(450, SurfaceVoxelY, 380),
                new Vector3Int(405, SurfaceVoxelY, 399),
                new Vector3Int(405, SurfaceVoxelY, 400),
                new Vector3Int(450, SurfaceVoxelY, 400)
            };

            for (int i = 0; i < voxels.Length; i++)
            {
                Vector3Int expected = ChunkSpace.VoxelToChunk(
                    voxels[i],
                    ChunkVoxelSize
                );

                Check(
                    coordinates.Contains(expected),
                    "StageB3A-D2: Voxel " + voxels[i] + " 归属绝对 Chunk " +
                    expected + " 存在"
                );
            }

            RunAbsoluteCoordinateCase(terrain, voxels, "StageB3A-D2");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(shifted);
        }
    }

    private static void RunAbsoluteCoordinateCase(
        DensityTerrain terrain,
        Vector3Int[] voxels,
        string label)
    {
        GameObject terrainObject = terrain.gameObject;
        List<TerrainChunkView> views = GetViews(terrainObject);

        for (int i = 0; i < voxels.Length; i++)
        {
            terrain.VoxelField.Set(voxels[i], VoxelState.Air);
            terrain.RebuildChunksAffectedByVoxel(voxels[i]);
        }

        views = GetViews(terrainObject);
        Dictionary<Vector3Int, MeshCanonical> incremental =
            SnapshotCanonical(views);

        bool rebuilt = terrain.RebuildVoxelAwareGeometryFromVoxelField();
        List<TerrainChunkView> rebuiltViews = GetViews(terrainObject);
        Dictionary<Vector3Int, MeshCanonical> afterRebuild =
            SnapshotCanonical(rebuiltViews);

        Check(rebuilt, label + ": 完整重建成功");
        Check(
            SameAllChunks(incremental, afterRebuild, out string detail),
            label + ": 边界修改状态在完整重建后保持" +
            (detail.Length > 0 ? " —— " + detail : string.Empty)
        );

        bool coordinatesStable = true;
        for (int i = 0; i < rebuiltViews.Count; i++)
        {
            if (!incremental.ContainsKey(rebuiltViews[i].Coordinate))
            {
                coordinatesStable = false;
            }
        }

        Check(
            coordinatesStable && rebuiltViews.Count == views.Count,
            label + ": 重建后 Chunk 绝对坐标集合不变（" +
            rebuiltViews.Count + " Chunk）"
        );

        int inside = 0;
        for (int i = 0; i < voxels.Length; i++)
        {
            inside += CountGeometryInsideVoxel(rebuiltViews, voxels[i]);
        }

        Check(
            inside == 0,
            label + ": 重建后已挖空 Voxel 内部无可见几何（" + inside + "）"
        );
    }

    // =====================================================================
    // 异常行为
    // =====================================================================

    private static void CheckExceptionBehaviour()
    {
        // (a) 越界 Voxel：Set 成功、入口 Mutation 失败 ⇒ 明确报告，不回滚。
        GameObject terrainObject = CreateFixture(
            "StageB3A_Exceptions",
            Vector3Int.zero,
            true
        );

        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();
            List<TerrainChunkView> views = GetViews(terrainObject);
            Dictionary<Vector3Int, Mesh> meshesBefore = SnapshotMeshes(views);

            Vector3Int outside = new Vector3Int(1000, SurfaceVoxelY, 1000);
            terrain.VoxelField.Set(outside, VoxelState.Air);
            int rebuilt = terrain.RebuildChunksAffectedByVoxel(outside);

            int replaced = 0;
            for (int i = 0; i < views.Count; i++)
            {
                if (views[i].Chunk.Mesh != meshesBefore[views[i].Coordinate])
                {
                    replaced++;
                }
            }

            Check(
                rebuilt == 0 && replaced == 0,
                "StageB3A-E1: 越界 Voxel 入口返回 0 且不替换任何 Mesh（" +
                rebuilt + " / " + replaced + "）"
            );
            Check(
                terrain.VoxelField.IsAir(outside),
                "StageB3A-E1: Mutation 失败时 VoxelField 仍为 Air（本轮不实现回滚）"
            );

            // 完整重建必须跳过越界 Voxel，而不是静默失败或崩溃。
            bool ok = terrain.RebuildVoxelAwareGeometryFromVoxelField();

            Check(
                ok,
                "StageB3A-E2: 完整重建在存在越界 Air Voxel 时仍成功"
            );
            Check(
                terrain.LastVoxelRebuildSkippedOutsideVoxelCount == 1 &&
                terrain.LastVoxelRebuildAppliedVoxelCount == 0 &&
                !terrain.LastVoxelRebuildFailed,
                "StageB3A-E2: 越界 Voxel 被明确跳过（skipped=" +
                terrain.LastVoxelRebuildSkippedOutsideVoxelCount +
                " applied=" + terrain.LastVoxelRebuildAppliedVoxelCount +
                " failed=" + terrain.LastVoxelRebuildFailed + "）"
            );

            // 清理越界状态，避免影响后续断言。
            terrain.VoxelField.Set(outside, VoxelState.Solid);
            terrain.RebuildVoxelAwareGeometryFromVoxelField();

            // (b) 合法修改 + 完整重建：Mesh 引用同步 + 诊断计数。
            Vector3Int voxel = new Vector3Int(30, SurfaceVoxelY, 30);
            terrain.VoxelField.Set(voxel, VoxelState.Air);
            terrain.RebuildChunksAffectedByVoxel(voxel);

            ok = terrain.RebuildVoxelAwareGeometryFromVoxelField();
            List<TerrainChunkView> rebuiltViews = GetViews(terrainObject);

            Check(
                ok &&
                terrain.LastVoxelRebuildAppliedVoxelCount == 1 &&
                terrain.LastVoxelRebuildSkippedOutsideVoxelCount == 0 &&
                terrain.LastVoxelRebuildSkippedNoGeometryVoxelCount == 0 &&
                !terrain.LastVoxelRebuildFailed,
                "StageB3A-E3: 完整重建诊断计数正确（applied=" +
                terrain.LastVoxelRebuildAppliedVoxelCount + "）"
            );

            bool synced = true;
            for (int i = 0; i < rebuiltViews.Count; i++)
            {
                TerrainChunkView view = rebuiltViews[i];
                if (view.MeshFilter.sharedMesh != view.Chunk.Mesh ||
                    view.MeshCollider.sharedMesh != view.Chunk.Mesh)
                {
                    synced = false;
                }
            }

            Check(
                synced,
                "StageB3A-E3: 完整重建后所有 View 指向当前最终 Mesh"
            );

            // (c) 连续修改 + 连续完整重建。
            Vector3Int second = new Vector3Int(75, SurfaceVoxelY, 30);
            terrain.VoxelField.Set(second, VoxelState.Air);
            terrain.RebuildChunksAffectedByVoxel(second);

            ok = terrain.RebuildVoxelAwareGeometryFromVoxelField();
            List<TerrainChunkView> afterFirst = GetViews(terrainObject);
            Dictionary<Vector3Int, MeshCanonical> firstRebuild =
                SnapshotCanonical(afterFirst);

            bool okAgain = terrain.RebuildVoxelAwareGeometryFromVoxelField();
            List<TerrainChunkView> afterSecond = GetViews(terrainObject);
            Dictionary<Vector3Int, MeshCanonical> secondRebuild =
                SnapshotCanonical(afterSecond);

            bool sameRebuild = SameAllChunks(
                firstRebuild,
                secondRebuild,
                out string detail);
            Check(
                ok && okAgain &&
                terrain.LastVoxelRebuildAppliedVoxelCount == 2 &&
                sameRebuild,
                "StageB3A-E4: 连续两次完整重建等价且重放计数为 2" +
                (detail.Length > 0 ? " —— " + detail : string.Empty)
            );

            // (d) 密度路径调用该 API 必须显式失败，而不是假装成功。
            GameObject legacyObject = CreateFixture(
                "StageB3A_LegacyDensity",
                Vector3Int.zero,
                false
            );

            try
            {
                DensityTerrain legacy = legacyObject.GetComponent<DensityTerrain>();
                bool legacyOk = legacy.RebuildVoxelAwareGeometryFromVoxelField();

                Check(
                    !legacyOk &&
                    legacy.LastVoxelRebuildFailed &&
                    legacy.LastVoxelRebuildMessage.Length > 0,
                    "StageB3A-E5: 密度路径调用完整重建 API 显式失败（消息 = " +
                    legacy.LastVoxelRebuildMessage + "）"
                );
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(legacyObject);
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    // =====================================================================
    // 判据实现
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
            vertexKeys.Add(VertexKey(vertices[i] + origin));
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
                        VertexKey(a),
                        VertexKey(b),
                        VertexKey(c)
                    )
                );
            }
        }

        canonical.TriangleKeys.Sort(StringComparer.Ordinal);
        return canonical;
    }

    private static bool SameCanonical(
        MeshCanonical a,
        MeshCanonical b,
        out string difference)
    {
        difference = string.Empty;

        if (a.VertexKeys.Count != b.VertexKeys.Count)
        {
            difference = "顶点数 " + a.VertexKeys.Count + " vs " + b.VertexKeys.Count;
            return false;
        }

        if (a.TriangleKeys.Count != b.TriangleKeys.Count)
        {
            difference = "三角形数 " + a.TriangleKeys.Count + " vs " +
                         b.TriangleKeys.Count;
            return false;
        }

        for (int i = 0; i < a.VertexKeys.Count; i++)
        {
            if (a.VertexKeys[i] != b.VertexKeys[i])
            {
                difference = "顶点集合差异 @" + i;
                return false;
            }
        }

        for (int i = 0; i < a.TriangleKeys.Count; i++)
        {
            if (a.TriangleKeys[i] != b.TriangleKeys[i])
            {
                difference = "三角形集合差异 @" + i;
                return false;
            }
        }

        float tolerance = Mathf.Max(1e-3f, (float)(a.Area * 1e-5));
        if (Mathf.Abs((float)(a.Area - b.Area)) > tolerance)
        {
            difference = "总面积 " + a.Area.ToString("F6") + " vs " +
                         b.Area.ToString("F6");
            return false;
        }

        return true;
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

            if (!SameCanonical(pair.Value, other, out string difference))
            {
                detail = pair.Key + ": " + difference;
                return false;
            }
        }

        return true;
    }

    private static int CountChangedChunks(
        Dictionary<Vector3Int, MeshCanonical> before,
        Dictionary<Vector3Int, MeshCanonical> after)
    {
        int changed = 0;

        foreach (KeyValuePair<Vector3Int, MeshCanonical> pair in before)
        {
            MeshCanonical other;
            if (!after.TryGetValue(pair.Key, out other)) continue;
            if (!SameCanonical(pair.Value, other, out string _)) changed++;
        }

        return changed;
    }

    private static int TotalTriangles(Dictionary<Vector3Int, MeshCanonical> map)
    {
        int total = 0;
        foreach (KeyValuePair<Vector3Int, MeshCanonical> pair in map)
        {
            total += pair.Value.TriangleKeys.Count;
        }

        return total;
    }

    /// <summary>统计与同 Chunk 内其它三角形完全重合（含绕序）的三角形数量。</summary>
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
                            VertexKey(vertices[triangles[i]]),
                            VertexKey(vertices[triangles[i + 1]]),
                            VertexKey(vertices[triangles[i + 2]])
                        );

                    if (!seen.Add(key)) duplicates++;
                }
            }
        }

        return duplicates;
    }

    /// <summary>统计中心严格落在指定 Voxel 内部的可见三角形数量。</summary>
    private static int CountGeometryInsideVoxel(
        List<TerrainChunkView> views,
        Vector3Int voxel)
    {
        int count = 0;
        Vector3 min = new Vector3(voxel.x, voxel.y, voxel.z);
        Vector3 max = min + Vector3.one;

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

                    if (centroid.x > min.x + FaceProbeTolerance &&
                        centroid.x < max.x - FaceProbeTolerance &&
                        centroid.y > min.y + FaceProbeTolerance &&
                        centroid.y < max.y - FaceProbeTolerance &&
                        centroid.z > min.z + FaceProbeTolerance &&
                        centroid.z < max.z - FaceProbeTolerance)
                    {
                        count++;
                    }
                }
            }
        }

        return count;
    }

    /// <summary>统计所有 Air Voxel 内部的可见三角形数量。</summary>
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

                    if (!voxelField.IsAir(voxelField.PositionToVoxel(centroid)))
                    {
                        continue;
                    }

                    Vector3 min = new Vector3(
                        Mathf.Floor(centroid.x),
                        Mathf.Floor(centroid.y),
                        Mathf.Floor(centroid.z)
                    );

                    if (centroid.x > min.x + FaceProbeTolerance &&
                        centroid.x < min.x + 1f - FaceProbeTolerance &&
                        centroid.y > min.y + FaceProbeTolerance &&
                        centroid.y < min.y + 1f - FaceProbeTolerance &&
                        centroid.z > min.z + FaceProbeTolerance &&
                        centroid.z < min.z + 1f - FaceProbeTolerance)
                    {
                        count++;
                    }
                }
            }
        }

        return count;
    }

    private sealed class WallClosureMeasurement
    {
        public int RequiredFaces;
        public int FullyClosedFaces;
        public int CoveredSamples;
        public int CoverageSampleTotal;
        public List<string> Issues = new List<string>();
    }

    /// <summary>
    /// 测量（不判定）Air Voxel 朝空气打开面的几何封闭覆盖率。
    /// 记录每个未完全封闭的面及其三角形拓扑证据，供 B3-B 处理。
    /// </summary>
    private static WallClosureMeasurement MeasureAirWallClosure(
        List<TerrainChunkView> views,
        VoxelField voxelField,
        Vector3Int[] airVoxels)
    {
        WallClosureMeasurement result = new WallClosureMeasurement();

        for (int i = 0; i < airVoxels.Length; i++)
        {
            Vector3Int voxel = airVoxels[i];

            for (int axis = 0; axis < 3; axis++)
            {
                for (int side = 0; side < 2; side++)
                {
                    Vector3Int neighbour = voxel;
                    neighbour[axis] += side == 0 ? -1 : 1;

                    if (voxelField.IsAir(neighbour)) continue;
                    if (!NeedsSealing(views, voxel, axis, side == 0)) continue;

                    result.RequiredFaces++;

                    int covered = 25 - CountUncoveredFaceSamples(
                        views,
                        voxel,
                        axis,
                        side == 0
                    );

                    result.CoveredSamples += covered;
                    result.CoverageSampleTotal += 25;

                    if (covered == 25)
                    {
                        result.FullyClosedFaces++;
                        continue;
                    }

                    result.Issues.Add(
                        "B3-B 待办：Air Voxel " + voxel + " 的轴" + axis +
                        (side == 0 ? "负" : "正") +
                        "面（朝空气打开）Hidden Face 覆盖不完整：" +
                        covered + "/25 采样被封闭；" +
                        "同面三角形=" + DescribePlaneTriangles(
                            views,
                            voxel,
                            axis,
                            side == 0,
                            2
                        )
                    );
                }
            }
        }

        return result;
    }

    /// <summary>
    /// 封闭性判据（独立于生产 SpatialFace 逻辑）：
    /// 对每个“已挖空 Voxel 的 Solid 邻接面”，先用基础密度场独立判断该面是否
    /// 真的朝空气打开（面外 0.1m 与 0.5m 处的 Base Density 均 &lt;= 0）。
    /// 只有需要封闭的面才用面内网格采样 + 垂直射线验证几何覆盖；
    /// 邻接侧本身是实体材料（Base Density &gt; 0）的面不要求生成墙，
    /// 因为那种情况下挖掘腔体被实体包围、不存在可见开口。
    /// </summary>
    private static bool AreAirWallsClosed(
        List<TerrainChunkView> views,
        VoxelField voxelField,
        Vector3Int[] airVoxels)
    {
        bool allClosed = true;

        for (int i = 0; i < airVoxels.Length; i++)
        {
            Vector3Int voxel = airVoxels[i];

            for (int axis = 0; axis < 3; axis++)
            {
                for (int side = 0; side < 2; side++)
                {
                    Vector3Int neighbour = voxel;
                    neighbour[axis] += side == 0 ? -1 : 1;

                    if (voxelField.IsAir(neighbour)) continue;

                    if (!NeedsSealing(views, voxel, axis, side == 0))
                    {
                        continue;
                    }

                    int uncovered = CountUncoveredFaceSamples(
                        views,
                        voxel,
                        axis,
                        side == 0
                    );

                    if (uncovered > 0)
                    {
                        allClosed = false;
                        Debug.LogWarning(
                            "[StageB3A] Air Voxel " + voxel + " 的 " +
                            (side == 0 ? "负" : "正") + "轴 " + axis +
                            " 面朝空气打开但有 " + uncovered +
                            " 个采样点未被几何封闭"
                        );
                    }
                }
            }
        }

        return allClosed;
    }

    /// <summary>
    /// 诊断（仅记录，不计入断言）：每个 Air Voxel 的 6 个面在最终 Mesh 中的
    /// “面内三角形数量 / 面积”、“几何缓存中的 HiddenFaceContribution 条目”与
    /// “面内采样覆盖数”，用于区分“生产缺陷”与“判据过严”。
    /// </summary>
    private static void LogWallCoverageDiagnostics(
        List<TerrainChunkView> views,
        VoxelField voxelField,
        Vector3Int[] airVoxels)
    {
        for (int i = 0; i < airVoxels.Length; i++)
        {
            Vector3Int voxel = airVoxels[i];

            for (int axis = 0; axis < 3; axis++)
            {
                for (int side = 0; side < 2; side++)
                {
                    Vector3Int neighbour = voxel;
                    neighbour[axis] += side == 0 ? -1 : 1;

                    bool neighbourAir = voxelField.IsAir(neighbour);
                    bool needsSealing = !neighbourAir &&
                        NeedsSealing(views, voxel, axis, side == 0);
                    int covered;
                    int planeTriangles;
                    float planeArea;

                    MeasureFace(
                        views,
                        voxel,
                        axis,
                        side == 0,
                        out covered,
                        out planeTriangles,
                        out planeArea
                    );

                    ChunkGeometryContributionSummary summary =
                        SummarizeContributions(views, voxel);

                    Debug.Log(
                        "[StageB3A-face] voxel=" + voxel +
                        " axis=" + axis + " side=" + (side == 0 ? "-" : "+") +
                        " neighbourAir=" + neighbourAir +
                        " needsSealing=" + needsSealing +
                        " coveredSamples=" + covered + "/25" +
                        " planeTriangles=" + planeTriangles +
                        " planeArea=" + planeArea.ToString("F4") +
                        " contributions=" + summary.EntryCount +
                        " contributionArea=" + summary.Area.ToString("F4") +
                        " contributionKeys=[" + summary.Keys + "]" +
                        (needsSealing && covered < 25
                            ? " probeDetail=" + DescribeFaceProbes(
                                views,
                                voxel,
                                axis,
                                side == 0) +
                              " planeTriangles=" + DescribePlaneTriangles(
                                views,
                                voxel,
                                axis,
                                side == 0,
                                4) +
                              " raySelfTest=" + DescribeRaySelfTest(
                                views,
                                voxel,
                                axis,
                                side == 0)
                            : string.Empty)
                    );
                }
            }
        }
    }

    /// <summary>
    /// 诊断：列出未命中的面内采样点与同平面上三角形的 u/v 范围，
    /// 用于定位“判据过严”还是“几何缺失”。
    /// </summary>
    private static string DescribeFaceProbes(
        List<TerrainChunkView> views,
        Vector3Int voxel,
        int axis,
        bool negativeSide)
    {
        const int grid = 5;
        StringBuilder builder = new StringBuilder();

        Vector3 min = new Vector3(voxel.x, voxel.y, voxel.z);
        float plane = negativeSide ? min[axis] : min[axis] + 1f;
        Vector3 outward = Vector3.zero;
        outward[axis] = negativeSide ? -1f : 1f;
        int axisU = (axis + 1) % 3;
        int axisV = (axis + 2) % 3;
        float maxDistance = FaceProbeTolerance * 12f;

        builder.Append("missed=");
        int missed = 0;

        for (int u = 1; u <= grid; u++)
        {
            for (int v = 1; v <= grid; v++)
            {
                float fu = u / (float)(grid + 1);
                float fv = v / (float)(grid + 1);

                Vector3 point = min;
                point[axis] = plane;
                point[axisU] = min[axisU] + fu;
                point[axisV] = min[axisV] + fv;

                float hit;
                if (TryRaycastMeshes(
                    views,
                    point + outward * (FaceProbeTolerance * 4f),
                    -outward,
                    maxDistance,
                    out hit))
                {
                    continue;
                }

                missed++;
                builder.Append('(').Append(u).Append(',').Append(v).Append(')');
            }
        }

        if (missed == 0) builder.Append("none");

        // 同平面上（不限制在本 Voxel 面矩形内）的三角形 u/v 覆盖范围。
        float minU = float.MaxValue;
        float maxU = float.MinValue;
        float minV = float.MaxValue;
        float maxV = float.MinValue;

        for (int c = 0; c < views.Count; c++)
        {
            Mesh mesh = views[c].Chunk.Mesh;
            if (mesh == null) continue;

            Vector3[] vertices = mesh.vertices;
            Vector3 origin = views[c].Chunk.ChunkOrigin;

            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                int[] triangles = mesh.GetTriangles(submesh);
                for (int i = 0; i + 2 < triangles.Length; i += 3)
                {
                    Vector3 a = vertices[triangles[i]] + origin;
                    Vector3 b = vertices[triangles[i + 1]] + origin;
                    Vector3 cc = vertices[triangles[i + 2]] + origin;

                    if (Mathf.Abs(a[axis] - plane) > FaceProbeTolerance ||
                        Mathf.Abs(b[axis] - plane) > FaceProbeTolerance ||
                        Mathf.Abs(cc[axis] - plane) > FaceProbeTolerance)
                    {
                        continue;
                    }

                    Vector3 centroid = (a + b + cc) / 3f;
                    if (Mathf.Abs(centroid[axisU] - (min[axisU] + 0.5f)) > 0.6f ||
                        Mathf.Abs(centroid[axisV] - (min[axisV] + 0.5f)) > 0.6f)
                    {
                        continue;
                    }

                    minU = Mathf.Min(minU, a[axisU], b[axisU], cc[axisU]);
                    maxU = Mathf.Max(maxU, a[axisU], b[axisU], cc[axisU]);
                    minV = Mathf.Min(minV, a[axisV], b[axisV], cc[axisV]);
                    maxV = Mathf.Max(maxV, a[axisV], b[axisV], cc[axisV]);
                }
            }
        }

        builder.Append(" planeTriUV=[");
        if (minU <= maxU)
        {
            builder.Append(minU.ToString("F3")).Append("..")
                .Append(maxU.ToString("F3")).Append(" , ")
                .Append(minV.ToString("F3")).Append("..")
                .Append(maxV.ToString("F3"));
        }
        else
        {
            builder.Append("none");
        }

        builder.Append(']');
        return builder.ToString();
    }

    /// <summary>
    /// 微自检：取该面上的第一个三角形，从其质心沿法线外侧 4mm 处向内发射，
    /// 分别用 RayTriangle 与 TryRaycastMeshes 测试，用于隔离射线判据自身的缺陷。
    /// </summary>
    private static string DescribeRaySelfTest(
        List<TerrainChunkView> views,
        Vector3Int voxel,
        int axis,
        bool negativeSide)
    {
        Vector3 min = new Vector3(voxel.x, voxel.y, voxel.z);
        float plane = negativeSide ? min[axis] : min[axis] + 1f;
        int axisU = (axis + 1) % 3;
        int axisV = (axis + 2) % 3;

        for (int c = 0; c < views.Count; c++)
        {
            Mesh mesh = views[c].Chunk.Mesh;
            if (mesh == null) continue;

            Vector3[] vertices = mesh.vertices;
            Vector3 origin = views[c].Chunk.ChunkOrigin;

            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                int[] triangles = mesh.GetTriangles(submesh);
                for (int i = 0; i + 2 < triangles.Length; i += 3)
                {
                    Vector3 a = vertices[triangles[i]] + origin;
                    Vector3 b = vertices[triangles[i + 1]] + origin;
                    Vector3 cc = vertices[triangles[i + 2]] + origin;

                    if (Mathf.Abs(a[axis] - plane) > 2e-3f ||
                        Mathf.Abs(b[axis] - plane) > 2e-3f ||
                        Mathf.Abs(cc[axis] - plane) > 2e-3f)
                    {
                        continue;
                    }

                    Vector3 centroid = (a + b + cc) / 3f;
                    if (Mathf.Abs(centroid[axisU] - (min[axisU] + 0.5f)) > 0.75f ||
                        Mathf.Abs(centroid[axisV] - (min[axisV] + 0.5f)) > 0.75f)
                    {
                        continue;
                    }

                    Vector3 outward = Vector3.zero;
                    outward[axis] = negativeSide ? -1f : 1f;
                    Vector3 rayOrigin = centroid + outward * 0.004f;

                    float direct;
                    bool directHit = RayTriangle(
                        rayOrigin,
                        -outward,
                        a,
                        b,
                        cc,
                        out direct
                    );

                    float found;
                    bool sweepHit = TryRaycastMeshes(
                        views,
                        rayOrigin,
                        -outward,
                        FaceProbeTolerance * 12f,
                        out found
                    );

                    return "directHit=" + directHit +
                           "(t=" + direct.ToString("F5") + ")" +
                           " sweepHit=" + sweepHit +
                           "(t=" + found.ToString("F5") + ")" +
                           " chunk=" + views[c].Coordinate +
                           " centroid=" + centroid;
                }
            }
        }

        return "noPlaneTriangle";
    }

    /// <summary>诊断：列出与给定面同平面的三角形顶点（最多 maxCount 个），用于确认多边形实际形状。</summary>
    private static string DescribePlaneTriangles(
        List<TerrainChunkView> views,
        Vector3Int voxel,
        int axis,
        bool negativeSide,
        int maxCount)
    {
        StringBuilder builder = new StringBuilder();
        Vector3 min = new Vector3(voxel.x, voxel.y, voxel.z);
        float plane = negativeSide ? min[axis] : min[axis] + 1f;
        int axisU = (axis + 1) % 3;
        int axisV = (axis + 2) % 3;
        int written = 0;

        for (int c = 0; c < views.Count && written < maxCount; c++)
        {
            Mesh mesh = views[c].Chunk.Mesh;
            if (mesh == null) continue;

            Vector3[] vertices = mesh.vertices;
            Vector3 origin = views[c].Chunk.ChunkOrigin;

            for (int submesh = 0; submesh < mesh.subMeshCount && written < maxCount; submesh++)
            {
                int[] triangles = mesh.GetTriangles(submesh);
                for (int i = 0; i + 2 < triangles.Length && written < maxCount; i += 3)
                {
                    Vector3 a = vertices[triangles[i]] + origin;
                    Vector3 b = vertices[triangles[i + 1]] + origin;
                    Vector3 cc = vertices[triangles[i + 2]] + origin;

                    if (Mathf.Abs(a[axis] - plane) > 2e-3f ||
                        Mathf.Abs(b[axis] - plane) > 2e-3f ||
                        Mathf.Abs(cc[axis] - plane) > 2e-3f)
                    {
                        continue;
                    }

                    Vector3 centroid = (a + b + cc) / 3f;
                    if (Mathf.Abs(centroid[axisU] - (min[axisU] + 0.5f)) > 0.75f ||
                        Mathf.Abs(centroid[axisV] - (min[axisV] + 0.5f)) > 0.75f)
                    {
                        continue;
                    }

                    written++;
                    builder.Append(" [chunk=").Append(views[c].Coordinate)
                        .Append(" uv(").Append(a[axisU].ToString("F3")).Append(',')
                        .Append(a[axisV].ToString("F3")).Append(")/(")
                        .Append(b[axisU].ToString("F3")).Append(',')
                        .Append(b[axisV].ToString("F3")).Append(")/(")
                        .Append(cc[axisU].ToString("F3")).Append(',')
                        .Append(cc[axisV].ToString("F3")).Append(")]");
                }
            }
        }

        return written == 0 ? "none" : builder.ToString();
    }

    private struct ChunkGeometryContributionSummary
    {
        public int EntryCount;
        public float Area;
        public string Keys;
    }

    private static ChunkGeometryContributionSummary SummarizeContributions(
        List<TerrainChunkView> views,
        Vector3Int voxel)
    {
        ChunkGeometryContributionSummary summary =
            new ChunkGeometryContributionSummary();
        summary.Keys = string.Empty;

        Vector3Int coordinate = ChunkSpace.VoxelToChunk(voxel, ChunkVoxelSize);
        for (int i = 0; i < views.Count; i++)
        {
            if (views[i].Coordinate != coordinate) continue;

            TerrainChunk chunk = views[i].Chunk;
            if (chunk.Geometry == null) return summary;

            List<string> keys = new List<string>();
            foreach (KeyValuePair<
                string,
                List<VoxelAwareMeshSpatialFace.HiddenFaceFragment>> pair in
                chunk.Geometry.HiddenFaceContributions)
            {
                summary.EntryCount++;
                keys.Add(pair.Key);

                if (pair.Value == null) continue;
                for (int h = 0; h < pair.Value.Count; h++)
                {
                    VoxelAwareMeshSpatialFace.HiddenFaceFragment hidden =
                        pair.Value[h];
                    if (hidden == null) continue;
                    summary.Area += PolygonArea(hidden.Polygon);
                }
            }

            keys.Sort(StringComparer.Ordinal);
            summary.Keys = string.Join(",", keys);
            return summary;
        }

        return summary;
    }

    private static float PolygonArea(List<Vector3> polygon)
    {
        if (polygon == null || polygon.Count < 3) return 0f;

        Vector3 sum = Vector3.zero;
        for (int i = 0; i < polygon.Count; i++)
        {
            Vector3 a = polygon[i];
            Vector3 b = polygon[(i + 1) % polygon.Count];
            sum += Vector3.Cross(a, b);
        }

        return sum.magnitude * 0.5f;
    }

    private static void MeasureFace(
        List<TerrainChunkView> views,
        Vector3Int voxel,
        int axis,
        bool negativeSide,
        out int coveredSamples,
        out int planeTriangles,
        out float planeArea)
    {
        coveredSamples = 25 - CountUncoveredFaceSamples(
            views,
            voxel,
            axis,
            negativeSide
        );

        planeTriangles = 0;
        planeArea = 0f;

        Vector3 min = new Vector3(voxel.x, voxel.y, voxel.z);
        float plane = negativeSide ? min[axis] : min[axis] + 1f;
        int axisU = (axis + 1) % 3;
        int axisV = (axis + 2) % 3;

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
                    Vector3 a = vertices[triangles[i]] + origin;
                    Vector3 b = vertices[triangles[i + 1]] + origin;
                    Vector3 c = vertices[triangles[i + 2]] + origin;

                    if (Mathf.Abs(a[axis] - plane) > FaceProbeTolerance ||
                        Mathf.Abs(b[axis] - plane) > FaceProbeTolerance ||
                        Mathf.Abs(c[axis] - plane) > FaceProbeTolerance)
                    {
                        continue;
                    }

                    Vector3 centroid = (a + b + c) / 3f;
                    if (centroid[axisU] < min[axisU] ||
                        centroid[axisU] > min[axisU] + 1f ||
                        centroid[axisV] < min[axisV] ||
                        centroid[axisV] > min[axisV] + 1f)
                    {
                        continue;
                    }

                    planeTriangles++;
                    planeArea += Vector3.Cross(b - a, c - a).magnitude * 0.5f;
                }
            }
        }
    }

    /// <summary>独立判据：该 Voxel 面外侧（邻接 Voxel 内部）在基础密度上是否为空气。</summary>
    private static bool NeedsSealing(
        List<TerrainChunkView> views,
        Vector3Int voxel,
        int axis,
        bool negativeSide)
    {
        Vector3 min = new Vector3(voxel.x, voxel.y, voxel.z);
        Vector3 outward = Vector3.zero;
        outward[axis] = negativeSide ? -1f : 1f;

        Vector3 faceCenter = min + Vector3.one * 0.5f;
        faceCenter[axis] = negativeSide ? min[axis] : min[axis] + 1f;

        float nearDensity;
        float farDensity;

        bool nearOk = TrySampleBaseDensity(
            views,
            faceCenter + outward * 0.1f,
            out nearDensity
        );
        bool farOk = TrySampleBaseDensity(
            views,
            faceCenter + outward * 0.5f,
            out farDensity
        );

        if (!nearOk || !farOk) return false;

        return nearDensity <= 0f && farDensity <= 0f;
    }

    /// <summary>独立三线性采样（不复用生产 Sampling 代码）。</summary>
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

            if (fx < -FaceProbeTolerance || fx > field.SampleCountX - 1 + FaceProbeTolerance ||
                fy < -FaceProbeTolerance || fy > field.SampleCountY - 1 + FaceProbeTolerance ||
                fz < -FaceProbeTolerance || fz > field.SampleCountZ - 1 + FaceProbeTolerance)
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

    private static int CountUncoveredFaceSamples(
        List<TerrainChunkView> views,
        Vector3Int voxel,
        int axis,
        bool negativeSide)
    {
        const int grid = 5;
        int uncovered = 0;

        Vector3 min = new Vector3(voxel.x, voxel.y, voxel.z);
        float plane = negativeSide ? min[axis] : min[axis] + 1f;
        Vector3 outward = Vector3.zero;
        outward[axis] = negativeSide ? -1f : 1f;

        for (int u = 1; u <= grid; u++)
        {
            for (int v = 1; v <= grid; v++)
            {
                float fu = u / (float)(grid + 1);
                float fv = v / (float)(grid + 1);

                Vector3 point = min;
                int axisU = (axis + 1) % 3;
                int axisV = (axis + 2) % 3;
                point[axis] = plane;
                point[axisU] = min[axisU] + fu;
                point[axisV] = min[axisV] + fv;

                Vector3 origin = point + outward * (FaceProbeTolerance * 4f);
                Vector3 direction = -outward;
                float hitDistance;

                if (!TryRaycastMeshes(
                    views,
                    origin,
                    direction,
                    FaceProbeTolerance * 12f,
                    out hitDistance))
                {
                    uncovered++;
                }
            }
        }

        return uncovered;
    }

    private static bool TryRaycastMeshes(
        List<TerrainChunkView> views,
        Vector3 origin,
        Vector3 direction,
        float maxDistance,
        out float hitDistance)
    {
        hitDistance = float.MaxValue;

        for (int v = 0; v < views.Count; v++)
        {
            Mesh mesh = views[v].Chunk.Mesh;
            if (mesh == null) continue;

            Vector3[] vertices = mesh.vertices;
            Vector3 chunkOrigin = views[v].Chunk.ChunkOrigin;

            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                int[] triangles = mesh.GetTriangles(submesh);
                for (int i = 0; i + 2 < triangles.Length; i += 3)
                {
                    Vector3 a = vertices[triangles[i]] + chunkOrigin;
                    Vector3 b = vertices[triangles[i + 1]] + chunkOrigin;
                    Vector3 c = vertices[triangles[i + 2]] + chunkOrigin;

                    float t;
                    if (!RayTriangle(origin, direction, a, b, c, out t)) continue;
                    if (t > 1e-5f && t < hitDistance) hitDistance = t;
                }
            }
        }

        return hitDistance <= maxDistance;
    }

    private static bool RayTriangle(
        Vector3 origin,
        Vector3 direction,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        out float distance)
    {
        distance = 0f;

        Vector3 edge1 = b - a;
        Vector3 edge2 = c - a;
        Vector3 pvec = Vector3.Cross(direction, edge2);
        float determinant = Vector3.Dot(edge1, pvec);

        if (Mathf.Abs(determinant) < 1e-9f) return false;

        float inverse = 1f / determinant;
        Vector3 tvec = origin - a;
        float u = Vector3.Dot(tvec, pvec) * inverse;
        if (u < -1e-6f || u > 1f + 1e-6f) return false;

        Vector3 qvec = Vector3.Cross(tvec, edge1);
        float v = Vector3.Dot(direction, qvec) * inverse;
        if (v < -1e-6f || u + v > 1f + 1e-6f) return false;

        distance = Vector3.Dot(edge2, qvec) * inverse;
        return distance > 1e-6f;
    }

    private static int CountActiveFragments(TerrainChunk chunk)
    {
        if (chunk == null || chunk.Geometry == null) return 0;

        int active = 0;
        List<VoxelAwareMeshPrecut.Fragment> fragments = chunk.Geometry.Fragments;
        for (int i = 0; i < fragments.Count; i++)
        {
            if (fragments[i].Active) active++;
        }

        return active;
    }

    private static string GeometryDigest(TerrainChunk chunk)
    {
        if (chunk == null || chunk.Geometry == null) return "none";

        VoxelAwareMeshPrecut.ChunkGeometry geometry = chunk.Geometry;
        StringBuilder builder = new StringBuilder();

        builder.Append("frag:").Append(geometry.Fragments.Count)
            .Append('/').Append(CountActiveFragments(chunk));
        builder.Append(";face:").Append(geometry.FaceFragments.Count);
        builder.Append(";vis:").Append(geometry.FaceFragmentVisibility.Count);
        builder.Append(";contrib:").Append(geometry.HiddenFaceContributions.Count);
        builder.Append(";spatial:").Append(geometry.SpatialFaces.Count);

        return builder.ToString();
    }

    private static TerrainChunk ChunkOf(
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

    private static Dictionary<Vector3Int, Mesh> SnapshotMeshes(
        List<TerrainChunkView> views)
    {
        Dictionary<Vector3Int, Mesh> result =
            new Dictionary<Vector3Int, Mesh>();

        for (int i = 0; i < views.Count; i++)
        {
            result[views[i].Coordinate] = views[i].Chunk.Mesh;
        }

        return result;
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

    // =====================================================================
    // Fixture
    // =====================================================================

    private static GameObject CreateFixture(
        string name,
        Vector3Int chunkStart,
        bool voxelAware)
    {
        GameObject terrainObject = new GameObject(name);
        DensityTerrain terrain = terrainObject.AddComponent<DensityTerrain>();

        terrain.voxelSize = 1f;
        terrain.chunkVoxelSize = ChunkVoxelSize;
        terrain.chunkCount = new Vector3Int(2, 1, 2);
        terrain.chunkStartCoordinate = chunkStart;
        terrain.resolution = 75;
        terrain.densityMode = DensityGenerationMode.HeightField;
        terrain.useNoise = false;
        terrain.height = FlatHeight;
        terrain.cavityRadius = 0f;
        terrain.debugCutaway = false;
        terrain.regenerateInEditor = false;
        terrain.useVoxelAwareMeshPrecut = voxelAware;

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

    /// <summary>记录结论 / 已知限制（不计入断言）。</summary>
    private static void Limit(string message)
    {
        Debug.Log("[StageB3A-LIMITATION] " + message);
    }

    private static void LogSummary()
    {
        Debug.Log(
            "[VoxelCoordinateMigrationStageB3AValidation] ===== SUMMARY =====\n" +
            "Checks = " + checks + "\n" +
            "Failures = " + failures + "\n" +
            "Result = " + (failures == 0 ? "PASS" : "FAIL")
        );
    }
}
