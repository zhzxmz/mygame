using System.Collections.Generic;
using TerrainModificationV2;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Mining Surface Integrity 验证（诊断套件）：
/// 目标 —— 证明/证伪“Mining 后增量更新漏生成表面”这一类缺面问题。
///
/// 判据（机械扫描，不依赖人工观察）：
///   对体素区域内每个体素、每个主轴方向：
///     若该面两侧恰好一侧为 Solid（VoxelField 编辑状态），且 Solid 一侧在
///     基础 DensityField 中确有材料（面内侧采样 > 0），则地形在该面处必须存在
///     实体表面 ⇒ 该面矩形范围内必须能找到一个覆盖它的 Mesh 三角形。
///   找不到 ⇒ 报告 Missing Surface（Voxel / Direction / Chunk）。
///
/// 反向检查：被挖空的 Voxel 内部不得残留 Mesh 三角形（否则是“旧表面没被隐藏”）。
///
/// 本套件只读 Terrain 数据 + Assemble 结果，不修改任何生产逻辑。
/// </summary>
public static class MiningSurfaceIntegrityValidation
{
    private const int ChunkVoxelX = 75;
    private const int ChunkVoxelY = 37;
    private const int ChunkVoxelZ = 50;
    private const float FlatHeight = 15f;
    private const float ProbeEpsilon = 0.001f;
    private const float PlaneTolerance = 0.02f;
    private const float RectTolerance = 0.05f;
    private const float NormalAlignment = 0.99f;

    private static readonly Vector3Int[] Directions =
    {
        new Vector3Int(1, 0, 0),
        new Vector3Int(-1, 0, 0),
        new Vector3Int(0, 1, 0),
        new Vector3Int(0, -1, 0),
        new Vector3Int(0, 0, 1),
        new Vector3Int(0, 0, -1)
    };

    private static readonly string[] DirectionNames =
    {
        "+X", "-X", "+Y", "-Y", "+Z", "-Z"
    };

    private static int checks;
    private static int failures;

    [MenuItem("Tools/Terrain/Validate Mining Surface Integrity")]
    public static void ValidateFromMenu()
    {
        ValidateAll();
        LogSummary();

        Debug.Log(
            "[MiningSurfaceIntegrityValidation] Menu validation result = " +
            (failures == 0 ? "PASS" : "FAIL")
        );
    }

    /// <summary>Batchmode 入口：-executeMethod MiningSurfaceIntegrityValidation.RunBatch。</summary>
    public static void RunBatch()
    {
        ValidateAll();
        LogSummary();
        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    private static void ValidateAll()
    {
        checks = 0;
        failures = 0;

        ValidateSingleVoxelMining();
        ValidateChunkBoundaryMining();
        ValidateSequentialMining();
        ValidateMultiChunkIsolation();
        ValidateBuriedVoxelMining();
        ValidateNaturalTerrainMining();
        ValidateCavityAdjacentMining();
        ValidateRestoreCycleMining();
        ValidateSurfacePatchMining();
        ValidateCrossChunkSequenceMining();
        ValidateMeshViewSync();
        ValidateChunkCornerMiningOnNoise();
        ValidateSceneLikeDenseMining();

        Check(
            LastMineFailure.Length == 0,
            "全局: 所有场景的挖掘均未发生 Evaluation Abort（无 VoxelField/Mesh 分歧）" +
            LastMineFailure
        );
    }

    // =====================================================================
    // 测试 13：同场景参数（HeightField + noise，height=15.7）密集表面挖掘
    // =====================================================================

    private static void ValidateSceneLikeDenseMining()
    {
        GameObject terrainObject = CreateSceneLikeFixture(
            "MiningIntegrity_SceneLike");
        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();

            const int from = 28;
            const int to = 32;   // 5×5 = 25 次挖掘

            int mined = 0;
            int missingTotal = 0;
            int staleTotal = 0;
            int coverageMin = int.MaxValue;
            string firstReport = string.Empty;

            for (int x = from; x <= to; x++)
            {
                for (int z = from; z <= to; z++)
                {
                    Vector3Int voxel;

                    if (!TryFindTopMaterialVoxel(terrain, x, z, out voxel))
                    {
                        continue;
                    }

                    if (!MineVoxel(terrain, voxel))
                    {
                        if (firstReport.Length == 0)
                        {
                            firstReport = "(" + x + "," + z + ") 挖 " + voxel +
                                " 未提交";
                        }
                        continue;
                    }

                    mined++;

                    List<string> missing = ScanRegionSurfacesNear(
                        terrain, RegionAround(voxel, 1));

                    coverageMin = Mathf.Min(
                        coverageMin, LastExpectedFaceCount);

                    if (missing.Count > 0)
                    {
                        missingTotal += missing.Count;
                        if (firstReport.Length == 0)
                        {
                            firstReport = "(" + x + "," + z + ") 挖 " + voxel +
                                "：" + missing[0];
                        }
                    }

                    staleTotal += ScanStaleSurfaces(terrain, voxel).Count;
                }
            }

            Check(
                mined == 25,
                "测试13: 场景参数下 25 次表面挖掘全部提交（实际 " + mined + "）" +
                (firstReport.Length > 0 ? " 首次异常：" + firstReport : string.Empty)
            );

            Check(
                coverageMin != int.MaxValue && coverageMin > 0,
                "测试13: 每次扫描都覆盖到应有表面（最小单次覆盖 " +
                (coverageMin == int.MaxValue ? 0 : coverageMin) + " 个面）"
            );

            Check(
                missingTotal == 0,
                "测试13: 场景参数密集挖掘后无 Missing Surface（累计 " +
                missingTotal + " 处）" + firstReport
            );

            Check(
                staleTotal == 0,
                "测试13: 场景参数密集挖掘无残留旧表面（累计 " + staleTotal + " 处）"
            );

            int staleViews = CheckAllChunkViewsSync(terrainObject, terrain);

            Check(
                staleViews == 0,
                "测试13: 密集挖掘后所有 Chunk 的 MeshFilter/MeshCollider 仍同步（异常 " +
                staleViews + " 个）" +
                (LastViewSyncDetail.Length > 0
                    ? "（" + LastViewSyncDetail + "）"
                    : string.Empty)
            );

            // 最终一致性大范围扫描：25 次挖掘区域 ±2（整段 Y 范围）
            List<string> wideMissing = ScanRegionSurfacesNear(
                terrain,
                new BoundsInt(
                    new Vector3Int(from - 2, 0, from - 2),
                    new Vector3Int(to - from + 5, ChunkVoxelY, to - from + 5)
                )
            );

            Check(
                wideMissing.Count == 0,
                "测试13: 挖掘区域扩大扫描无 Missing Surface（累计 " +
                wideMissing.Count + " 处，本次覆盖 " + LastExpectedFaceCount +
                " 个应有表面）" + FormatReport(wideMissing)
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    /// <summary>与 SampleScene 完全一致的 Terrain 参数（HeightField + noise, height 15.7）。</summary>
    private static GameObject CreateSceneLikeFixture(string name)
    {
        GameObject terrainObject = new GameObject(name);
        DensityTerrain terrain = terrainObject.AddComponent<DensityTerrain>();

        terrain.voxelSize = 1f;
        terrain.chunkVoxelSize = new Vector3Int(
            ChunkVoxelX, ChunkVoxelY, ChunkVoxelZ);
        terrain.chunkCount = new Vector3Int(2, 1, 2);
        terrain.chunkStartCoordinate = Vector3Int.zero;
        terrain.resolution = 75;
        terrain.densityMode = DensityGenerationMode.HeightField;
        terrain.useNoise = true;
        terrain.height = 15.7f;
        terrain.noiseScale = 0.15f;
        terrain.noiseStrength = 2.37f;
        terrain.regenerateInEditor = false;
        terrain.useVoxelAwareMeshPrecut = true;

        terrain.Regenerate();

        return terrainObject;
    }

    // =====================================================================
    // 测试 9：表面 patch 挖掘（2~3 个空气邻居的边/角 Voxel）
    // =====================================================================

    private static void ValidateSurfacePatchMining()
    {
        GameObject terrainObject = CreateFixture("MiningIntegrity_Patch");
        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();

            // 依次挖出 2×2 的表面 patch：每一步都让被挖 Voxel 拥有更多空气邻居。
            Vector3Int[] patch = new Vector3Int[]
            {
                new Vector3Int(30, 14, 30),   // 1 个空气邻居（+Y）
                new Vector3Int(31, 14, 30),   // 2 个（+Y, -X）
                new Vector3Int(30, 14, 31),   // 2 个（+Y, -Z）
                new Vector3Int(31, 14, 31)    // 3 个（+Y, -X, -Z）
            };

            int missingTotal = 0;
            int staleTotal = 0;
            string firstReport = string.Empty;

            for (int i = 0; i < patch.Length; i++)
            {
                Vector3Int voxel = patch[i];

                int airNeighbours = 6 - CountSolidNeighbours(terrain, voxel);

                if (!MineVoxel(terrain, voxel))
                {
                    Check(false, "测试9: patch 第 " + i + " 步 " + voxel +
                        " Mining 未提交");
                    continue;
                }

                List<string> missing = ScanRegionSurfaces(
                    terrain, RegionAround(voxel, 1));

                if (missing.Count > 0)
                {
                    missingTotal += missing.Count;
                    if (firstReport.Length == 0)
                    {
                        firstReport = "第 " + i + " 步挖 " + voxel +
                            "（空气邻居 " + airNeighbours + "）：" + missing[0];
                    }
                }

                staleTotal += ScanStaleSurfaces(terrain, voxel).Count;
            }

            Check(
                missingTotal == 0,
                "测试9: 表面 patch（边/角，含 2~3 空气邻居）挖掘后无 Missing Surface（累计 " +
                missingTotal + " 处）" + firstReport
            );

            Check(
                staleTotal == 0,
                "测试9: 表面 patch 挖掘无残留旧表面（累计 " + staleTotal + " 处）"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    // =====================================================================
    // 测试 10：跨 Chunk 连续挖掘（Chunk 边界两侧交替）
    // =====================================================================

    private static void ValidateCrossChunkSequenceMining()
    {
        GameObject terrainObject = CreateFixture("MiningIntegrity_CrossChunk");
        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();

            // x=74 属 Chunk(0,0,0)，x=75 属 Chunk(1,0,0)
            Vector3Int[] sequence = new Vector3Int[]
            {
                new Vector3Int(74, 14, 30),
                new Vector3Int(75, 14, 30),
                new Vector3Int(74, 14, 31),
                new Vector3Int(75, 14, 31)
            };

            int missingTotal = 0;
            string firstReport = string.Empty;

            for (int i = 0; i < sequence.Length; i++)
            {
                Vector3Int voxel = sequence[i];

                if (!MineVoxel(terrain, voxel))
                {
                    Check(false, "测试10: 跨 Chunk 第 " + i + " 步 " + voxel +
                        " Mining 未提交");
                    continue;
                }

                List<string> missing = ScanRegionSurfaces(
                    terrain, RegionAround(voxel, 1));

                if (missing.Count > 0)
                {
                    missingTotal += missing.Count;
                    if (firstReport.Length == 0)
                    {
                        firstReport = "第 " + i + " 步挖 " + voxel +
                            "：" + missing[0];
                    }
                }
            }

            Check(
                missingTotal == 0,
                "测试10: 跨 Chunk 连续挖掘后无 Missing Surface（累计 " +
                missingTotal + " 处）" + firstReport
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    // =====================================================================
    // 测试 11：MeshView 同步（MeshFilter / MeshCollider == Chunk.Mesh）
    // =====================================================================

    private static void ValidateMeshViewSync()
    {
        GameObject terrainObject = CreateFixture("MiningIntegrity_MeshViewSync");
        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();

            int staleViews = CheckAllChunkViewsSync(terrainObject, terrain);

            Check(
                staleViews == 0,
                "测试11: 初始生成后所有 Chunk 的 MeshFilter/MeshCollider 均同步（异常 " +
                staleViews + " 个）"
            );

            // 连续挖掘（含 Chunk 边界格），每次挖掘后校验所有 Chunk 的 View 同步
            Vector3Int[] digs = new Vector3Int[]
            {
                new Vector3Int(30, 14, 30),
                new Vector3Int(74, 14, 30),
                new Vector3Int(75, 14, 30),
                new Vector3Int(30, 14, 49),
                new Vector3Int(30, 13, 30)
            };

            int firstStale = -1;
            string detail = string.Empty;

            for (int i = 0; i < digs.Length; i++)
            {
                if (!MineVoxel(terrain, digs[i])) continue;

                int stale = CheckAllChunkViewsSync(terrainObject, terrain);

                if (stale > 0 && firstStale < 0)
                {
                    firstStale = i;
                    detail = LastViewSyncDetail;
                }
            }

            Check(
                firstStale < 0,
                "测试11: 每次 Mining 后所有 Chunk 的 MeshFilter/MeshCollider 均与 Chunk.Mesh 同步" +
                (detail.Length > 0 ? "（首次异常：" + detail + "）" : string.Empty)
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    private static string LastViewSyncDetail = string.Empty;

    /// <summary>扫描覆盖计数：防止“应有表面为 0 ⇒ 空扫描也通过”的假阳性。</summary>
    private static int LastExpectedFaceCount;
    private static int LastScannedFaceCount;

    private static void CheckScanCoverage(string label, int minimum)
    {
        Check(
            LastExpectedFaceCount >= minimum,
            label + "：扫描确实覆盖到应有表面（本次检查 " +
            LastExpectedFaceCount + " 个面，要求 ≥ " + minimum + "）"
        );
    }

    // =====================================================================
    // 测试 12：噪声地形 + Chunk 边/角（含对角 Chunk）连续挖掘
    // =====================================================================

    private static void ValidateChunkCornerMiningOnNoise()
    {
        GameObject terrainObject = CreateNaturalFixture(
            "MiningIntegrity_ChunkCornerNoise");
        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();

            // Chunk(0,0,0) 的最大 X / Z 边界 = 74 / 49；
            // (75,·,50) 属对角 Chunk(1,0,1)。按 y 自顶向下挖到有材料的 Voxel。
            Vector2Int[] corners = new Vector2Int[]
            {
                new Vector2Int(74, 49),
                new Vector2Int(75, 50),   // 对角 Chunk
                new Vector2Int(74, 50),
                new Vector2Int(75, 49),
                new Vector2Int(73, 49),
                new Vector2Int(74, 48)
            };

            int mined = 0;
            int missingTotal = 0;
            int coverageMin = int.MaxValue;
            string firstReport = string.Empty;

            for (int i = 0; i < corners.Length; i++)
            {
                Vector3Int voxel;

                if (!TryFindTopMaterialVoxel(
                    terrain, corners[i].x, corners[i].y, out voxel))
                {
                    continue;
                }

                if (!MineVoxel(terrain, voxel)) continue;

                mined++;

                List<string> missing = ScanRegionSurfacesNear(
                    terrain, RegionAround(voxel, 1));

                coverageMin = Mathf.Min(coverageMin, LastExpectedFaceCount);

                if (missing.Count > 0)
                {
                    missingTotal += missing.Count;
                    if (firstReport.Length == 0)
                    {
                        firstReport = "列 " + corners[i] + " 挖 " + voxel +
                            "：" + missing[0];
                    }
                }
            }

            Check(
                mined >= 4,
                "测试12: Chunk 边/角（含对角 Chunk）噪声地形挖掘已提交（" +
                mined + "/" + corners.Length + "）"
            );

            Check(
                coverageMin != int.MaxValue && coverageMin > 0,
                "测试12: 扫描覆盖到应有表面（最小单次覆盖 " +
                (coverageMin == int.MaxValue ? 0 : coverageMin) + " 个面）"
            );

            Check(
                missingTotal == 0,
                "测试12: Chunk 边/角噪声地形挖掘后无 Missing Surface（累计 " +
                missingTotal + " 处）" + firstReport
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    private static int CheckAllChunkViewsSync(
        GameObject terrainObject,
        DensityTerrain terrain)
    {
        LastViewSyncDetail = string.Empty;

        TerrainChunkView[] views =
            terrainObject.GetComponentsInChildren<TerrainChunkView>(true);

        int stale = 0;

        for (int i = 0; i < views.Length; i++)
        {
            TerrainChunkView view = views[i];

            if (view == null || view.Chunk == null) continue;

            Mesh mesh = view.Chunk.Mesh;

            bool filterOk = view.MeshFilter != null &&
                            view.MeshFilter.sharedMesh == mesh;
            bool colliderOk = view.MeshCollider != null &&
                              view.MeshCollider.sharedMesh == mesh;

            if (!filterOk || !colliderOk)
            {
                stale++;

                if (LastViewSyncDetail.Length == 0)
                {
                    LastViewSyncDetail =
                        "Chunk=" + view.Chunk.Coordinate +
                        " filterOk=" + filterOk +
                        " colliderOk=" + colliderOk;
                }
            }
        }

        return stale;
    }

    // =====================================================================
    // 测试 5：完全埋藏 Voxel（6 面同时新生）—— 平地夹具
    // =====================================================================

    private static void ValidateBuriedVoxelMining()
    {
        GameObject terrainObject = CreateFixture("MiningIntegrity_Buried");
        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();
            Vector3Int voxel = new Vector3Int(30, 10, 30);   // 6 个邻居都是 Solid

            int solidNeighbours = CountSolidNeighbours(terrain, voxel);

            Check(
                solidNeighbours == 6,
                "测试5: (30,10,30) 为完全埋藏 Voxel（Solid 邻居 " +
                solidNeighbours + "/6）"
            );

            bool mined = MineVoxel(terrain, voxel);
            Check(mined, "测试5: 埋藏 Voxel Mining 已提交");

            List<string> missing = ScanRegionSurfaces(
                terrain, RegionAround(voxel, 1));

            Check(
                missing.Count == 0,
                "测试5: 埋藏 Voxel 挖空后 6 面新生表面完整（缺 " +
                missing.Count + " 处）" + FormatReport(missing)
            );

            List<string> stale = ScanStaleSurfaces(terrain, voxel);
            Check(
                stale.Count == 0,
                "测试5: 埋藏 Voxel 内部无残留旧表面（" + stale.Count + " 处）"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    // =====================================================================
    // 测试 6：噪声地形（等值面穿过 Voxel ⇒ 部分 FaceFragment）表面挖掘
    // =====================================================================

    private static void ValidateNaturalTerrainMining()
    {
        GameObject terrainObject = CreateNaturalFixture("MiningIntegrity_Natural");
        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();

            int mined = 0;
            int missingTotal = 0;
            string firstReport = string.Empty;

            Vector2Int[] columns = new Vector2Int[]
            {
                new Vector2Int(20, 20),
                new Vector2Int(30, 30),
                new Vector2Int(40, 25),
                new Vector2Int(50, 40)
            };

            for (int i = 0; i < columns.Length; i++)
            {
                Vector3Int voxel;

                if (!TryFindTopMaterialVoxel(
                    terrain, columns[i].x, columns[i].y, out voxel))
                {
                    Check(false, "测试6: 列 " + columns[i] + " 未找到材料 Voxel");
                    continue;
                }

                if (!MineVoxel(terrain, voxel))
                {
                    Check(false, "测试6: 列 " + columns[i] + " Voxel " + voxel +
                        " Mining 未提交");
                    continue;
                }

                mined++;

                List<string> missing = ScanRegionSurfacesNear(
                    terrain, RegionAround(voxel, 1));

                if (missing.Count > 0)
                {
                    missingTotal += missing.Count;
                    if (firstReport.Length == 0)
                    {
                        firstReport = "列 " + columns[i] + " 挖 " + voxel +
                            "：" + missing[0];
                    }
                }

                List<string> stale = ScanStaleSurfaces(terrain, voxel);
                if (stale.Count > 0 && firstReport.Length == 0)
                {
                    firstReport = "列 " + columns[i] + " 挖 " + voxel +
                        "：" + stale[0];
                }
            }

            Check(
                mined == columns.Length,
                "测试6: 噪声地形表面挖掘全部提交（" + mined + "/" +
                columns.Length + "）"
            );

            Check(
                missingTotal == 0,
                "测试6: 噪声地形（等值面穿 Voxel）挖掘后无 Missing Surface（累计 " +
                missingTotal + " 处）" + firstReport
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    // =====================================================================
    // 测试 7：空腔（SphereCavity）相邻 Voxel 挖掘
    // =====================================================================

    private static void ValidateCavityAdjacentMining()
    {
        GameObject terrainObject = CreateNaturalFixture("MiningIntegrity_Cavity");
        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();

            // 空腔球心 (30.6, 8.53, 25.4)、半径 3.55 ⇒ 球底面 y≈4.98。
            // 挖其正下方 / 侧壁的实体 Voxel，会把空腔再打通一层。
            Vector3Int[] voxels = new Vector3Int[]
            {
                new Vector3Int(30, 4, 25),   // 空腔下方
                new Vector3Int(34, 8, 25),   // 空腔 +X 侧壁附近
                new Vector3Int(27, 8, 25),   // 空腔 -X 侧壁附近
                new Vector3Int(30, 8, 29)    // 空腔 +Z 侧壁附近
            };

            int minedCount = 0;
            int missingTotal = 0;
            string firstReport = string.Empty;

            for (int i = 0; i < voxels.Length; i++)
            {
                Vector3Int voxel = voxels[i];

                if (!HasBaseMaterial(terrain, voxel)) continue;

                if (!MineVoxel(terrain, voxel)) continue;

                minedCount++;

                List<string> missing = ScanRegionSurfacesNear(
                    terrain, RegionAround(voxel, 1));

                if (missing.Count > 0)
                {
                    missingTotal += missing.Count;
                    if (firstReport.Length == 0)
                    {
                        firstReport = "空腔邻域挖 " + voxel + "：" + missing[0];
                    }
                }
            }

            Check(
                minedCount >= 3,
                "测试7: 空腔相邻实体 Voxel 挖掘已提交（" + minedCount + "/" +
                voxels.Length + "）"
            );

            Check(
                missingTotal == 0,
                "测试7: 空腔相邻挖掘后无 Missing Surface（累计 " +
                missingTotal + " 处）" + firstReport
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    // =====================================================================
    // 测试 8：挖空 → 恢复 Solid → 再挖（脏状态清理）
    // =====================================================================

    private static void ValidateRestoreCycleMining()
    {
        GameObject terrainObject = CreateFixture("MiningIntegrity_RestoreCycle");
        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();
            Vector3Int voxel = new Vector3Int(30, 14, 30);

            int missingTotal = 0;
            int staleTotal = 0;
            string firstReport = string.Empty;

            for (int round = 0; round < 3; round++)
            {
                if (!MineVoxel(terrain, voxel))
                {
                    Check(false, "测试8: 第 " + round + " 轮 Mining 未提交");
                    break;
                }

                List<string> missing = ScanRegionSurfaces(
                    terrain, RegionAround(voxel, 1));

                if (missing.Count > 0)
                {
                    missingTotal += missing.Count;
                    if (firstReport.Length == 0)
                    {
                        firstReport = "第 " + round + " 轮挖空：" + missing[0];
                    }
                }

                staleTotal += ScanStaleSurfaces(terrain, voxel).Count;

                // 恢复 Solid
                terrain.VoxelField.Set(voxel, VoxelState.Solid);
                int rebuilt = terrain.RebuildChunksAffectedByVoxel(voxel);

                if (rebuilt < 1)
                {
                    Check(false, "测试8: 第 " + round + " 轮恢复 Solid 未提交");
                    break;
                }

                List<string> restoredMissing = ScanRegionSurfaces(
                    terrain, RegionAround(voxel, 1));

                if (restoredMissing.Count > 0)
                {
                    missingTotal += restoredMissing.Count;
                    if (firstReport.Length == 0)
                    {
                        firstReport = "第 " + round + " 轮恢复后：" +
                            restoredMissing[0];
                    }
                }
            }

            Check(
                missingTotal == 0,
                "测试8: 挖空/恢复 3 轮循环后无 Missing Surface（累计 " +
                missingTotal + " 处）" + firstReport
            );

            Check(
                staleTotal == 0,
                "测试8: 挖空/恢复循环中无残留旧表面（累计 " + staleTotal + " 处）"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    // =====================================================================
    // 自然夹具辅助
    // =====================================================================

    private static GameObject CreateNaturalFixture(string name)
    {
        GameObject terrainObject = new GameObject(name);
        DensityTerrain terrain = terrainObject.AddComponent<DensityTerrain>();

        terrain.voxelSize = 1f;
        terrain.chunkVoxelSize = new Vector3Int(
            ChunkVoxelX, ChunkVoxelY, ChunkVoxelZ);
        terrain.chunkCount = new Vector3Int(2, 1, 2);
        terrain.chunkStartCoordinate = Vector3Int.zero;
        terrain.resolution = 75;
        terrain.densityMode = DensityGenerationMode.SphereCavity;
        terrain.useNoise = true;
        terrain.height = FlatHeight;
        terrain.noiseScale = 0.15f;
        terrain.noiseStrength = 2.37f;
        terrain.cavityCenterOffset = new Vector3(30.6f, 8.53f, 25.4f);
        terrain.cavityRadius = 3.55f;
        terrain.debugCutaway = false;
        terrain.regenerateInEditor = false;
        terrain.useVoxelAwareMeshPrecut = true;

        terrain.Regenerate();

        return terrainObject;
    }

    /// <summary>自顶向下找到该列“中心采样确有基础材料”的最高 Voxel。</summary>
    private static bool TryFindTopMaterialVoxel(
        DensityTerrain terrain,
        int x,
        int z,
        out Vector3Int voxel)
    {
        voxel = default(Vector3Int);

        int maxY = Mathf.Min(
            ChunkVoxelY - 1,
            Mathf.RoundToInt(FlatHeight) + 8);

        for (int y = maxY; y >= 0; y--)
        {
            Vector3Int candidate = new Vector3Int(x, y, z);

            if (HasBaseMaterial(terrain, candidate))
            {
                voxel = candidate;
                return true;
            }
        }

        return false;
    }

    private static bool HasBaseMaterial(
        DensityTerrain terrain,
        Vector3Int voxel)
    {
        bool hasMaterial;
        bool isEditedAir;
        string reason;

        if (!terrain.TryGetVoxelMaterialState(
            voxel,
            out hasMaterial,
            out isEditedAir,
            out reason))
        {
            return false;
        }

        return hasMaterial && !isEditedAir;
    }

    private static int CountSolidNeighbours(
        DensityTerrain terrain,
        Vector3Int voxel)
    {
        int solid = 0;

        for (int d = 0; d < Directions.Length; d++)
        {
            if (terrain.VoxelField.Get(voxel + Directions[d]) ==
                VoxelState.Solid)
            {
                solid++;
            }
        }

        return solid;
    }

    // =====================================================================
    // 测试 1：单 voxel mining —— 附近六个方向的表面完整性
    // =====================================================================

    private static void ValidateSingleVoxelMining()
    {
        GameObject terrainObject = CreateFixture("MiningIntegrity_Single");
        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();
            Vector3Int voxel = new Vector3Int(30, 14, 30);

            Check(
                terrain.VoxelField.Get(voxel) == VoxelState.Solid,
                "测试1: 初始 (30,14,30) 为 Solid"
            );

            bool mined = MineVoxel(terrain, voxel);

            Check(mined, "测试1: Mining 已提交（Mutation/Assemble 执行）");

            List<string> missing = ScanRegionSurfaces(
                terrain, RegionAround(voxel, 1));

            Check(
                missing.Count == 0,
                "测试1: Mining 后邻域不存在 Missing Surface（" +
                missing.Count + " 处）" + FormatReport(missing)
            );

            List<string> stale = ScanStaleSurfaces(terrain, voxel);

            Check(
                stale.Count == 0,
                "测试1: 被挖空 Voxel 内部无残留旧表面（" +
                stale.Count + " 处）" + FormatReport(stale)
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    // =====================================================================
    // 测试 2：Chunk 边界 voxel（x=0 / x=max / z=0 / z=max / y 底面）
    // =====================================================================

    private static void ValidateChunkBoundaryMining()
    {
        GameObject terrainObject = CreateFixture("MiningIntegrity_ChunkBoundary");
        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();

            Vector3Int[] boundaryVoxels = new Vector3Int[]
            {
                new Vector3Int(0, 14, 30),                        // Chunk 最小 X
                new Vector3Int(ChunkVoxelX - 1, 14, 30),          // Chunk 最大 X（+X 邻格属下一 Chunk）
                new Vector3Int(30, 14, 0),                        // Chunk 最小 Z
                new Vector3Int(30, 14, ChunkVoxelZ - 1),          // Chunk 最大 Z
                new Vector3Int(30, 0, 30)                         // Chunk 最小 Y（-Y 无 Chunk）
            };

            for (int i = 0; i < boundaryVoxels.Length; i++)
            {
                Vector3Int voxel = boundaryVoxels[i];

                if (terrain.VoxelField.Get(voxel) != VoxelState.Solid)
                {
                    Check(false, "测试2: 边界用例 Voxel " + voxel + " 初始不是 Solid");
                    continue;
                }

                bool mined = MineVoxel(terrain, voxel);

                Check(mined, "测试2: 边界 Voxel " + voxel + " Mining 已提交");

                List<string> missing = ScanRegionSurfaces(
                    terrain, RegionAround(voxel, 1));

                Check(
                    missing.Count == 0,
                    "测试2: 边界 Voxel " + voxel +
                    " 邻域不存在 Missing Surface（" + missing.Count + " 处）" +
                    FormatReport(missing)
                );
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    // =====================================================================
    // 测试 3：连续 Mining 10 次（逐次校验）
    // =====================================================================

    private static void ValidateSequentialMining()
    {
        GameObject terrainObject = CreateFixture("MiningIntegrity_Sequential");
        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();
            Vector3Int column = new Vector3Int(30, 0, 30);

            const int digs = 10;
            int commitFailures = 0;
            int missingTotal = 0;
            int staleTotal = 0;
            string firstReport = string.Empty;

            for (int i = 0; i < digs; i++)
            {
                Vector3Int voxel = new Vector3Int(
                    column.x, 14 - i, column.z);

                if (terrain.VoxelField.Get(voxel) != VoxelState.Solid)
                {
                    commitFailures++;
                    if (firstReport.Length == 0)
                    {
                        firstReport = "第 " + i + " 次：Voxel " + voxel + " 不是 Solid";
                    }
                    break;
                }

                if (!MineVoxel(terrain, voxel))
                {
                    commitFailures++;
                    if (firstReport.Length == 0)
                    {
                        firstReport = "第 " + i + " 次：Voxel " + voxel + " Mining 未提交";
                    }
                    continue;
                }

                List<string> missing = ScanRegionSurfaces(
                    terrain, RegionAround(voxel, 1));

                if (missing.Count > 0)
                {
                    missingTotal += missing.Count;
                    if (firstReport.Length == 0)
                    {
                        firstReport = "第 " + i + " 次挖 " + voxel + "：" + missing[0];
                    }
                }

                staleTotal += ScanStaleSurfaces(terrain, voxel).Count;
            }

            Check(
                commitFailures == 0,
                "测试3: 连续 " + digs + " 次 Mining 全部提交（失败 " + commitFailures +
                "）" + firstReport
            );

            Check(
                missingTotal == 0,
                "测试3: 连续 Mining 过程中无 Missing Surface（累计 " + missingTotal +
                " 处）" + firstReport
            );

            Check(
                staleTotal == 0,
                "测试3: 连续 Mining 过程中无残留旧表面（累计 " + staleTotal + " 处）"
            );

            // 最终一次性全列扫描：整根柱子 + 邻域
            List<string> finalMissing = ScanRegionSurfaces(
                terrain,
                new BoundsInt(
                    new Vector3Int(column.x - 1, 3, column.z - 1),
                    new Vector3Int(3, 13, 3)
                )
            );

            Check(
                finalMissing.Count == 0,
                "测试3: 最终柱体区域无 Missing Surface（" + finalMissing.Count +
                " 处）" + FormatReport(finalMissing)
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    // =====================================================================
    // 测试 4：多 Chunk（2,1,2）—— 挖一个 Chunk，其它 Chunk 不受损
    // =====================================================================

    private static void ValidateMultiChunkIsolation()
    {
        GameObject terrainObject = CreateFixture("MiningIntegrity_MultiChunk");
        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();

            List<TerrainChunkView> views =
                new List<TerrainChunkView>(
                    terrainObject.GetComponentsInChildren<TerrainChunkView>(true));

            Check(views.Count == 4, "测试4: 夹具为 2×1×2 = 4 个 Chunk（实际 " +
                views.Count + "）");

            Dictionary<Vector3Int, MeshCanonical> baseline =
                SnapshotOtherChunks(views);

            Vector3Int voxel = new Vector3Int(30, 14, 30);   // Chunk(0,0,0)
            Vector3Int changedChunk = ChunkSpace.VoxelToChunk(
                voxel, new Vector3Int(ChunkVoxelX, ChunkVoxelY, ChunkVoxelZ));

            Check(
                changedChunk == new Vector3Int(0, 0, 0),
                "测试4: 目标 Voxel " + voxel + " 位于 Chunk(0,0,0)（实际 " +
                changedChunk + "）"
            );

            bool mined = MineVoxel(terrain, voxel);
            Check(mined, "测试4: Chunk(0,0,0) Mining 已提交");

            List<string> missing = ScanRegionSurfaces(
                terrain, RegionAround(voxel, 1));
            Check(
                missing.Count == 0,
                "测试4: 挖掘后目标邻域无 Missing Surface（" + missing.Count +
                " 处）" + FormatReport(missing)
            );

            int changedOthers = CountChangedOtherChunks(
                views, baseline, changedChunk);

            Check(
                changedOthers == 0,
                "测试4: 其它 Chunk 的 Mesh 未被破坏（改变 " + changedOthers + " 个）"
            );

            int crossChunkVisibilityLeak = CountVisibilityEntriesOutsideChunk(
                views, changedChunk);

            Check(
                crossChunkVisibilityLeak == 0,
                "测试4: Visibility 未越界写入其它 Chunk（越界条目 " +
                crossChunkVisibilityLeak + "）"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    // =====================================================================
    // 核心扫描：Solid|Air 边界必须存在表面
    // =====================================================================

    private static List<string> ScanRegionSurfaces(
        DensityTerrain terrain,
        BoundsInt region)
    {
        return ScanRegionSurfaces(terrain, region, false);
    }

    /// <summary>
    /// 噪声地形版本：等值面可能斜穿 Voxel 面，因此放宽为
    /// “三角形切向包围盒覆盖面中心 + 轴向上越过该面平面（±0.45 Voxel）”。
    /// </summary>
    private static List<string> ScanRegionSurfacesNear(
        DensityTerrain terrain,
        BoundsInt region)
    {
        return ScanRegionSurfaces(terrain, region, true);
    }

    private static List<string> ScanRegionSurfaces(
        DensityTerrain terrain,
        BoundsInt region,
        bool tolerant)
    {
        LastExpectedFaceCount = 0;
        LastScannedFaceCount = 0;
        List<string> report = new List<string>();
        Dictionary<Vector3Int, TerrainChunk> chunkCache =
            new Dictionary<Vector3Int, TerrainChunk>();
        Dictionary<Vector3Int, TriangleCache> triangleCache =
            new Dictionary<Vector3Int, TriangleCache>();

        Vector3Int chunkVoxelSize = terrain.EffectiveChunkVoxelSize;
        float voxelSize = terrain.VoxelField.VoxelSize;

        for (int x = region.xMin; x < region.xMax; x++)
        {
            for (int y = region.yMin; y < region.yMax; y++)
            {
                for (int z = region.zMin; z < region.zMax; z++)
                {
                    Vector3Int voxel = new Vector3Int(x, y, z);

                    for (int d = 0; d < Directions.Length; d++)
                    {
                        Vector3Int neighbour = voxel + Directions[d];

                        bool negativeSolid =
                            terrain.VoxelField.Get(voxel) == VoxelState.Solid;
                        bool positiveSolid =
                            terrain.VoxelField.Get(neighbour) == VoxelState.Solid;

                        // 只有 Solid|Air 边界才应该有表面。
                        if (negativeSolid == positiveSolid) continue;

                        Vector3Int solidVoxel = negativeSolid
                            ? voxel
                            : neighbour;
                        Vector3Int airVoxel = negativeSolid
                            ? neighbour
                            : voxel;

                        TerrainChunk solidChunk = ResolveChunk(
                            terrain, chunkCache, solidVoxel, chunkVoxelSize);
                        TerrainChunk airChunk = ResolveChunk(
                            terrain, chunkCache, airVoxel, chunkVoxelSize);

                        if (solidChunk == null)
                        {
                            // Terrain 外侧 / 未生成区域：不是可修复的缺面。
                            continue;
                        }

                        int axis = Directions[d].x != 0 ? 0 :
                            (Directions[d].y != 0 ? 1 : 2);

                        bool solidIsNegative = negativeSolid;

                        // 两侧体素沿该轴相邻 ⇒ 共享面位于较大索引的那一侧坐标。
                        float plane = Mathf.Max(
                            voxel[axis], neighbour[axis]) * voxelSize;

                        Vector3 faceCentre = FaceCentre(
                            solidVoxel, axis, plane, voxelSize);

                        // 材料探测点：在实体 Voxel 内部、紧贴该面。
                        Vector3 probe = faceCentre;
                        int inward = solidIsNegative ? -1 : 1;
                        probe[axis] +=
                            inward * Directions[d][axis] * ProbeEpsilon;

                        float material;
                        if (!VoxelAwareMeshRuntimeMutation.TrySampleBaseDensity(
                            solidChunk, probe, out material, out string _))
                        {
                            // 采样出域：诊断信息不足，不计入缺面（避免假阳性）。
                            continue;
                        }

                        if (material <= 0f) continue;

                        LastExpectedFaceCount++;

                        bool found = tolerant
                            ? (ChunkMeshHasSurfaceNear(
                                   solidChunk, triangleCache, axis, plane,
                                   faceCentre, voxelSize * 0.5f + RectTolerance) ||
                               (airChunk != null && airChunk != solidChunk &&
                                ChunkMeshHasSurfaceNear(
                                    airChunk, triangleCache, axis, plane,
                                    faceCentre,
                                    voxelSize * 0.5f + RectTolerance)))
                            : (ChunkMeshHasFace(
                                   solidChunk, triangleCache, axis, plane,
                                   faceCentre, voxelSize * 0.5f + RectTolerance) ||
                               (airChunk != null && airChunk != solidChunk &&
                                ChunkMeshHasFace(
                                    airChunk, triangleCache, axis, plane,
                                    faceCentre,
                                    voxelSize * 0.5f + RectTolerance)));

                        if (!found)
                        {
                            report.Add(
                                "Missing Surface Voxel:" + solidVoxel +
                                " Direction:" + DirectionNames[d] +
                                " Chunk:" + solidChunk.Coordinate);
                        }
                    }
                }
            }
        }

        return report;
    }

    /// <summary>被挖空的 Voxel 内部不得残留三角形（旧表面未隐藏）。</summary>
    private static List<string> ScanStaleSurfaces(
        DensityTerrain terrain,
        Vector3Int voxel)
    {
        List<string> report = new List<string>();
        float voxelSize = terrain.VoxelField.VoxelSize;
        Vector3Int chunkVoxelSize = terrain.EffectiveChunkVoxelSize;

        Dictionary<Vector3Int, TerrainChunk> chunkCache =
            new Dictionary<Vector3Int, TerrainChunk>();
        Dictionary<Vector3Int, TriangleCache> triangleCache =
            new Dictionary<Vector3Int, TriangleCache>();

        Vector3 min = new Vector3(
            voxel.x * voxelSize,
            voxel.y * voxelSize,
            voxel.z * voxelSize
        ) + Vector3.one * 0.05f;

        Vector3 max = min + Vector3.one * (voxelSize - 0.1f);

        HashSet<Vector3Int> reported = new HashSet<Vector3Int>();

        for (int x = voxel.x - 1; x <= voxel.x + 1; x++)
        {
            for (int y = voxel.y - 1; y <= voxel.y + 1; y++)
            {
                for (int z = voxel.z - 1; z <= voxel.z + 1; z++)
                {
                    Vector3Int candidate = new Vector3Int(x, y, z);
                    TerrainChunk chunk = ResolveChunk(
                        terrain, chunkCache, candidate, chunkVoxelSize);

                    if (chunk == null) continue;
                    if (!reported.Add(chunk.Coordinate)) continue;

                    if (TriangleInsideBox(chunk, triangleCache, min, max))
                    {
                        report.Add(
                            "Stale Surface Voxel:" + voxel +
                            " Chunk:" + chunk.Coordinate);
                    }
                }
            }
        }

        return report;
    }

    private static bool TriangleInsideBox(
        TerrainChunk chunk,
        Dictionary<Vector3Int, TriangleCache> cache,
        Vector3 min,
        Vector3 max)
    {
        TriangleCache triangles = GetTriangles(chunk, cache);

        for (int t = 0; t < triangles.Centroids.Count; t++)
        {
            Vector3 c = triangles.Centroids[t];

            if (c.x > min.x && c.x < max.x &&
                c.y > min.y && c.y < max.y &&
                c.z > min.z && c.z < max.z)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ChunkMeshHasFace(
        TerrainChunk chunk,
        Dictionary<Vector3Int, TriangleCache> cache,
        int axis,
        float plane,
        Vector3 faceCentre,
        float halfExtent)
    {
        TriangleCache triangles = GetTriangles(chunk, cache);

        int t1 = axis == 0 ? 1 : 0;
        int t2 = axis == 2 ? 1 : 2;

        for (int i = 0; i < triangles.Centroids.Count; i++)
        {
            Vector3 c = triangles.Centroids[i];
            Vector3 n = triangles.Normals[i];

            if (Mathf.Abs(n[axis]) < NormalAlignment) continue;
            if (Mathf.Abs(c[axis] - plane) > PlaneTolerance) continue;
            if (Mathf.Abs(c[t1] - faceCentre[t1]) > halfExtent) continue;
            if (Mathf.Abs(c[t2] - faceCentre[t2]) > halfExtent) continue;

            return true;
        }

        return false;
    }

    private static bool ChunkMeshHasSurfaceNear(
        TerrainChunk chunk,
        Dictionary<Vector3Int, TriangleCache> cache,
        int axis,
        float plane,
        Vector3 faceCentre,
        float tangentialHalfExtent)
    {
        TriangleCache triangles = GetTriangles(chunk, cache);

        int t1 = axis == 0 ? 1 : 0;
        int t2 = axis == 2 ? 1 : 2;
        float axialWindow = 0.45f;

        for (int i = 0; i < triangles.Centroids.Count; i++)
        {
            Vector3 min = triangles.Minima[i];
            Vector3 max = triangles.Maxima[i];

            if (faceCentre[t1] < min[t1] - tangentialHalfExtent ||
                faceCentre[t1] > max[t1] + tangentialHalfExtent)
            {
                continue;
            }

            if (faceCentre[t2] < min[t2] - tangentialHalfExtent ||
                faceCentre[t2] > max[t2] + tangentialHalfExtent)
            {
                continue;
            }

            if (plane < min[axis] - axialWindow ||
                plane > max[axis] + axialWindow)
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static TriangleCache GetTriangles(
        TerrainChunk chunk,
        Dictionary<Vector3Int, TriangleCache> cache)
    {
        TriangleCache cached;
        if (cache.TryGetValue(chunk.Coordinate, out cached)) return cached;

        TriangleCache result = new TriangleCache();

        Mesh mesh = chunk.Mesh;
        if (mesh != null)
        {
            Vector3[] vertices = mesh.vertices;
            int[] indices = mesh.triangles;
            Vector3 origin = chunk.ChunkOrigin;

            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                Vector3 a = vertices[indices[i]] + origin;
                Vector3 b = vertices[indices[i + 1]] + origin;
                Vector3 c = vertices[indices[i + 2]] + origin;

                Vector3 centroid = (a + b + c) / 3f;
                Vector3 normal = Vector3.Cross(b - a, c - a);

                result.Centroids.Add(centroid);
                result.Normals.Add(
                    normal.sqrMagnitude > 1e-12f
                        ? normal.normalized
                        : Vector3.zero
                );
                result.Minima.Add(Vector3.Min(a, Vector3.Min(b, c)));
                result.Maxima.Add(Vector3.Max(a, Vector3.Max(b, c)));
            }
        }

        cache[chunk.Coordinate] = result;
        return result;
    }

    private static TerrainChunk ResolveChunk(
        DensityTerrain terrain,
        Dictionary<Vector3Int, TerrainChunk> cache,
        Vector3Int voxel,
        Vector3Int chunkVoxelSize)
    {
        Vector3Int coordinate = ChunkSpace.VoxelToChunk(
            voxel, chunkVoxelSize);

        TerrainChunk cached;
        if (cache.TryGetValue(coordinate, out cached)) return cached;

        TerrainChunk chunk;
        bool found = terrain.TryGetChunk(coordinate, out chunk);

        chunk = found ? chunk : null;

        cache[coordinate] = chunk;
        return chunk;
    }

    private static Vector3 FaceCentre(
        Vector3Int voxel,
        int axis,
        float plane,
        float voxelSize)
    {
        Vector3 centre = new Vector3(
            (voxel.x + 0.5f) * voxelSize,
            (voxel.y + 0.5f) * voxelSize,
            (voxel.z + 0.5f) * voxelSize
        );

        centre[axis] = plane;
        return centre;
    }

    private static BoundsInt RegionAround(Vector3Int voxel, int radius)
    {
        return new BoundsInt(
            new Vector3Int(
                voxel.x - radius, voxel.y - radius, voxel.z - radius),
            new Vector3Int(
                radius * 2 + 1, radius * 2 + 1, radius * 2 + 1)
        );
    }

    // =====================================================================
    // Chunk 隔离 / Visibility 越界检查
    // =====================================================================

    private static Dictionary<Vector3Int, MeshCanonical> SnapshotOtherChunks(
        List<TerrainChunkView> views)
    {
        Dictionary<Vector3Int, MeshCanonical> result =
            new Dictionary<Vector3Int, MeshCanonical>();

        for (int i = 0; i < views.Count; i++)
        {
            result[views[i].Coordinate] = Canonical(views[i].Chunk);
        }

        return result;
    }

    private static int CountChangedOtherChunks(
        List<TerrainChunkView> views,
        Dictionary<Vector3Int, MeshCanonical> baseline,
        Vector3Int changedChunk)
    {
        int changed = 0;

        for (int i = 0; i < views.Count; i++)
        {
            Vector3Int coordinate = views[i].Coordinate;
            if (coordinate == changedChunk) continue;

            MeshCanonical before;
            if (!baseline.TryGetValue(coordinate, out before))
            {
                changed++;
                continue;
            }

            MeshCanonical after = Canonical(views[i].Chunk);
            if (!SameCanonical(before, after)) changed++;
        }

        return changed;
    }

    private static int CountVisibilityEntriesOutsideChunk(
        List<TerrainChunkView> views,
        Vector3Int changedChunk)
    {
        // 只统计“非零 Chunk 上出现隐藏覆写”的情况：
        // 本轮改动只应影响目标 Chunk 及其直接邻接（且不得让其它 Chunk 长出隐藏面）。
        int total = 0;

        for (int i = 0; i < views.Count; i++)
        {
            TerrainChunk chunk = views[i].Chunk;
            if (chunk == null || chunk.Geometry == null) continue;
            if (chunk.Coordinate == changedChunk) continue;

            bool hidden = false;

            foreach (KeyValuePair<int, bool> pair in
                chunk.Geometry.FaceFragmentVisibility)
            {
                if (!pair.Value)
                {
                    hidden = true;
                    break;
                }
            }

            if (hidden) total++;
        }

        return total;
    }

    private static MeshCanonical Canonical(TerrainChunk chunk)
    {
        MeshCanonical result = new MeshCanonical();

        if (chunk == null || chunk.Mesh == null) return result;

        Mesh mesh = chunk.Mesh;
        Vector3[] vertices = mesh.vertices;
        int[] indices = mesh.triangles;

        result.VertexCount = vertices.Length;
        result.IndexCount = indices.Length;

        float sumX = 0f, sumY = 0f, sumZ = 0f;
        for (int i = 0; i < vertices.Length; i++)
        {
            sumX += vertices[i].x;
            sumY += vertices[i].y;
            sumZ += vertices[i].z;
        }

        if (vertices.Length > 0)
        {
            result.Centroid = new Vector3(
                sumX / vertices.Length,
                sumY / vertices.Length,
                sumZ / vertices.Length
            );
        }

        return result;
    }

    private static bool SameCanonical(MeshCanonical a, MeshCanonical b)
    {
        return a.VertexCount == b.VertexCount &&
               a.IndexCount == b.IndexCount &&
               (a.Centroid - b.Centroid).sqrMagnitude < 1e-8f;
    }

    // =====================================================================
    // 夹具 / Mining
    // =====================================================================

    private static GameObject CreateFixture(string name)
    {
        GameObject terrainObject = new GameObject(name);
        DensityTerrain terrain = terrainObject.AddComponent<DensityTerrain>();

        terrain.voxelSize = 1f;
        terrain.chunkVoxelSize = new Vector3Int(
            ChunkVoxelX, ChunkVoxelY, ChunkVoxelZ);
        terrain.chunkCount = new Vector3Int(2, 1, 2);
        terrain.chunkStartCoordinate = Vector3Int.zero;
        terrain.resolution = 75;
        terrain.densityMode = DensityGenerationMode.HeightField;
        terrain.useNoise = false;
        terrain.height = FlatHeight;
        terrain.regenerateInEditor = false;
        terrain.useVoxelAwareMeshPrecut = true;

        terrain.Regenerate();

        return terrainObject;
    }

    private static bool MineVoxel(DensityTerrain terrain, Vector3Int voxel)
    {
        terrain.VoxelField.Set(voxel, VoxelState.Air);
        int rebuilt = terrain.RebuildChunksAffectedByVoxel(voxel);

        if (!VoxelAwareMeshRuntimeMutation.LastMutationCommitted &&
            LastMineFailure.Length == 0)
        {
            // Evaluation Abort ⇒ VoxelField 已写入但 Mesh 未更新（权威状态与 Mesh 分歧）。
            LastMineFailure =
                " Voxel=" + voxel +
                " reason=" + VoxelAwareMeshRuntimeMutation.LastEvaluationFailureMessage;
        }

        return rebuilt >= 1;
    }

    private static string LastMineFailure = string.Empty;

    private static string FormatReport(List<string> report)
    {
        if (report == null || report.Count == 0) return string.Empty;

        string text = "\n";

        for (int i = 0; i < report.Count && i < 12; i++)
        {
            text += "  [" + (i + 1) + "] " + report[i] + "\n";
        }

        if (report.Count > 12)
        {
            text += "  ...（共 " + report.Count + " 条）\n";
        }

        return text;
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
            "[MiningSurfaceIntegrityValidation] ===== SUMMARY =====\n" +
            "Checks = " + checks + "\n" +
            "Failures = " + failures + "\n" +
            "Result = " + (failures == 0 ? "PASS" : "FAIL")
        );
    }

    private class TriangleCache
    {
        public readonly List<Vector3> Centroids = new List<Vector3>();
        public readonly List<Vector3> Normals = new List<Vector3>();
        public readonly List<Vector3> Minima = new List<Vector3>();
        public readonly List<Vector3> Maxima = new List<Vector3>();
    }

    private struct MeshCanonical
    {
        public int VertexCount;
        public int IndexCount;
        public Vector3 Centroid;
    }
}
