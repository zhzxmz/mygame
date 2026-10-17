using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using VoxelWorld;
using VoxelWorld.Generation;
using VoxelWorld.Generation.Geology;

// 命名空间 VoxelWorld 内存在同名类型 VoxelWorld.VoxelWorld ⇒ 用别名消除解析歧义
using VoxelWorldData = VoxelWorld.VoxelWorld;

/// <summary>
/// Geology Stage B 验证（WorldGeneration_Geology_Implementation.md §13.1 Stage B）。
///
/// 用法：
///   · Editor 菜单：Tools/VoxelWorld/Validate Geology Stage B
///   · 批处理：-executeMethod GeologyStageBValidation.RunBatch
///
/// 覆盖：
///   GB1 同坐标同 seed Sample 逐字段一致
///   GB2 跨 Chunk 边界坐标连续（x=15/16、x=-1/0，含真实相邻 Chunk 边界列）
///   GB3 Chunk Independence（同世界坐标，通过不同 Chunk 请求/不同已加载集合）
///   GB4 ExplainAt 与 Sample 一致（同一判定链）
///   GB5 GIV9：关闭全部 Geology 特性 ⇒ 与 Stage 1 基线逐体素一致
///   GB6 扫描：GeologySampler 不引用 UnityEngine / Chunk / Runtime / Entity
///   GB7 零修改（既有文件 SHA 不变）      ← 补充（§13.2 硬性要求）
///
/// 本文件是 Editor 批处理验证：允许使用 UnityEngine 与既有实现（`BasicTerrainGenerator`）
/// 来构造"旧基线"与"Chunk 窗口"；被扫描的禁令只针对
/// `Assets/Scripts/VoxelWorld/Generation/Geology/*.cs`。
/// </summary>
public static class GeologyStageBValidation
{
    private static int checks;
    private static int failures;

    private const string GeologyFolder = "Scripts/VoxelWorld/Generation/Geology";

    [MenuItem("Tools/VoxelWorld/Validate Geology Stage B")]
    public static void ValidateFromMenu()
    {
        ValidateAll();

        Debug.Log("[GeologyStageBValidation] Menu validation result = " +
                  (failures == 0 ? "PASS" : "FAIL"));
    }

    public static void RunBatch()
    {
        ValidateAll();

        Debug.Log(
            "[GeologyStageBValidation] ===== SUMMARY =====\n" +
            "Checks = " + checks + "\n" +
            "Failures = " + failures + "\n" +
            "Result = " + (failures == 0 ? "PASS" : "FAIL"));

        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    private static void ValidateAll()
    {
        checks = 0;
        failures = 0;

        Run("GB1 同坐标同 seed 逐字段一致", ValidateSameInputConsistency);
        Run("GB2 跨 Chunk 边界坐标连续", ValidateBoundaryContinuity);
        Run("GB3 Chunk Independence", ValidateChunkIndependence);
        Run("GB4 ExplainAt 与 Sample 一致", ValidateExplainMatchesSample);
        Run("GB5 GIV9 与 Stage 1 基线一致", ValidateStage1BaselineEquivalence);
        Run("GB6 结构扫描", ValidateStructuralScan);
        Run("GB7 零修改（既有文件 SHA 不变）", ValidateExistingFilesUnchanged);
    }

    // =====================================================================
    // GB1：同坐标同 seed Sample 逐字段一致
    // =====================================================================

    private static void ValidateSameInputConsistency()
    {
        GeologySampler a = CreateSampler(WorldGenerationSettings.Default, GeologySettings.Default);
        GeologySampler b = CreateSampler(WorldGenerationSettings.Default, GeologySettings.Default);

        int[][] coordinates =
        {
            new int[] { 0, 0, 0 },
            new int[] { 15, 16, 15 },
            new int[] { 16, 24, 16 },
            new int[] { -1, 24, -1 },
            new int[] { -17, 30, 33 },
            new int[] { 100, 63, -100 },
            new int[] { -1000, 12, 1000 }
        };

        int[] seeds = { 0, 12345, -777, int.MaxValue };

        int compared = 0;
        int mismatches = 0;
        string firstDifference = null;

        for (int s = 0; s < seeds.Length; s++)
        {
            for (int c = 0; c < coordinates.Length; c++)
            {
                int x = coordinates[c][0];
                int y = coordinates[c][1];
                int z = coordinates[c][2];

                GeologySample first = a.Sample(x, y, z, seeds[s]);
                GeologySample second = a.Sample(x, y, z, seeds[s]);
                GeologySample other = b.Sample(x, y, z, seeds[s]);

                compared++;

                string differenceA = CompareSamples(first, second);
                string differenceB = CompareSamples(first, other);

                if (differenceA != null) { mismatches++; if (firstDifference == null) firstDifference = "重复调用：" + differenceA; }
                if (differenceB != null) { mismatches++; if (firstDifference == null) firstDifference = "独立实例：" + differenceB; }
            }
        }

        Check(mismatches == 0,
            "GB1：重复调用与独立实例均逐字段一致（比较 " + compared + " 组，不一致 " +
            mismatches + "）" + (firstDifference == null ? "" : " 首次差异：" + firstDifference));

        // 采样语义正确性：默认参数下（TerrainHeight=24, SoilDepth=3, WorldHeight=64）
        GeologySample deep = a.Sample(0, 10, 0, 12345);
        Check(deep.IsSolid && deep.Material == VoxelType.Stone && deep.Rule == GeologyRule.Deep,
            "GB1：深层应为 Stone/Deep（" + deep + "）");

        GeologySample surface = a.Sample(0, deep.SurfaceHeight, 0, 12345);
        Check(surface.IsSolid && surface.Material == VoxelType.Grass &&
              surface.Rule == GeologyRule.Surface,
            "GB1：地表应为 Grass/Surface（y=" + deep.SurfaceHeight + "，" + surface + "）");

        GeologySample above = a.Sample(0, 63, 0, 12345);
        Check(!above.IsSolid && above.Material == VoxelType.Air,
            "GB1：世界顶部之上应为空（" + above + "）");

        GeologySample outside = a.Sample(0, -1, 0, 12345);
        Check(!outside.IsSolid && outside.Rule == GeologyRule.WorldBounds,
            "GB1：y=-1 应为 WorldBounds/空（" + outside + "）");

        GeologySample tooHigh = a.Sample(0, 64, 0, 12345);
        Check(!tooHigh.IsSolid && tooHigh.Rule == GeologyRule.WorldBounds,
            "GB1：y=WorldHeight 应为 WorldBounds/空（" + tooHigh + "）");

        // 注入 Seed 与显式 Seed 等价
        Check(SameSample(a.Sample(37, 20, -5), a.Sample(37, 20, -5, WorldGenerationSettings.Default.Seed)),
            "GB1：无参重载 == 显式注入 Seed 的重载");

        // 不同坐标不应全部同值（反例保护）
        HashSet<string> distinct = new HashSet<string>();
        for (int i = 0; i < 64; i++) distinct.Add(a.Sample(i, 20, 0, 12345).ToString());
        Check(distinct.Count > 1, "GB1：不同坐标产生不同结果（去重 " + distinct.Count + "）");
    }

    // =====================================================================
    // GB2：跨 Chunk 边界坐标连续（x=15/16、x=-1/0 等）
    // =====================================================================

    private static void ValidateBoundaryContinuity()
    {
        WorldGenerationSettings stage1 = WorldGenerationSettings.Default;

        // 洞穴恒生效的采样器（用于检验**洞穴噪声自身**的连续性）
        GeologySettings alwaysCave = GeologySettings.Default;
        alwaysCave.CaveThreshold = -1f;
        GeologySampler caveSampler = CreateSampler(stage1, alwaysCave);

        // 关闭洞穴的采样器（用于与 Stage 1 生成结果逐体素比对）
        GeologySettings noCave = GeologySettings.Default;
        noCave.EnableCaves = false;
        GeologySampler plainSampler = CreateSampler(stage1, noCave);

        BasicTerrainGenerator generator = new BasicTerrainGenerator(stage1);

        // 缝对：xA 必须是某个 Chunk 的**最后一列**（xA ≡ 15 mod 16），xB = xA + 1 是相邻 Chunk 的第一列
        int[] seamLeft = { 15, -1, 31, -17, -33 };
        int[] comparisonLeft = { 0, 16 };          // 非缝对照（同一 Chunk 内相邻列）

        float caveBound = GeologyNoise.MaxAdjacentDelta3D(alwaysCave.CaveCellVoxels) + 1e-5f;
        int caveExceeded = 0;
        float caveWorst = 0f;

        int coordinateBreaks = 0;
        int seamVoxelsCompared = 0;
        int samplerMismatch = 0;
        int adjacentColumnVoxelDifferences = 0;
        int worstHeightDelta = 0;
        string firstSamplerMismatch = null;

        for (int i = 0; i < seamLeft.Length + comparisonLeft.Length; i++)
        {
            int leftX = i < seamLeft.Length ? seamLeft[i] : comparisonLeft[i - seamLeft.Length];
            int rightX = leftX + 1;

            // 1) 洞穴噪声跨边界连续（相邻体素差分 ≤ 显式上界）
            for (int y = -4; y < 68; y++)
            {
                for (int z = -2; z < 18; z++)
                {
                    float delta = Math.Abs(caveSampler.CaveDensityAt(rightX, y, z, stage1.Seed) -
                                           caveSampler.CaveDensityAt(leftX, y, z, stage1.Seed));

                    if (delta > caveWorst) caveWorst = delta;
                    if (delta > caveBound) caveExceeded++;
                }
            }

            // 2) 地表高度相邻差分有界（平滑性；相邻列允许相差 1，但不得出现断崖）
            int maxHeightDelta = 0;
            for (int z = 0; z < 16; z++)
            {
                int delta = Math.Abs(plainSampler.SurfaceHeightAt(rightX, z, stage1.Seed) -
                                     plainSampler.SurfaceHeightAt(leftX, z, stage1.Seed));
                if (delta > maxHeightDelta) maxHeightDelta = delta;
            }

            if (maxHeightDelta > worstHeightDelta) worstHeightDelta = maxHeightDelta;

            // 3) 真实相邻 Chunk：缝处坐标必须**相邻**（无缝隙/无重叠），
            //    且两侧体素都必须能被同一个世界函数解释（这是 Chunk 独立性的核心证据）
            VoxelCoordinate chunkLeftCoordinate =
                VoxelWorldData.WorldToChunkCoordinate(new VoxelCoordinate(leftX, 0, 0));
            VoxelCoordinate chunkRightCoordinate =
                VoxelWorldData.WorldToChunkCoordinate(new VoxelCoordinate(rightX, 0, 0));

            int localLeft = leftX - chunkLeftCoordinate.x * VoxelChunk.ChunkSize;
            int localRight = rightX - chunkRightCoordinate.x * VoxelChunk.ChunkSize;

            // 只有真正的"缝"（左列是所在 Chunk 的最后一列、右列是下一 Chunk 的第一列）才做 Chunk 级比较；
            // 对照组（同一 Chunk 内的相邻列）只参与上面的噪声/高度连续性检查。
            if (localLeft != VoxelChunk.ChunkSize - 1 || localRight != 0) continue;

            VoxelChunk chunkLeft = generator.Generate(chunkLeftCoordinate);
            VoxelChunk chunkRight = generator.Generate(chunkRightCoordinate);

            for (int y = 0; y < VoxelChunk.ChunkSize; y++)
            {
                for (int z = 0; z < VoxelChunk.ChunkSize; z++)
                {
                    VoxelCoordinate worldLeft =
                        chunkLeft.LocalToWorldCoordinate(localLeft, y, z);
                    VoxelCoordinate worldRight =
                        chunkRight.LocalToWorldCoordinate(localRight, y, z);

                    seamVoxelsCompared++;

                    // 3a) 缝处无缝隙 / 无重叠：两侧世界坐标必须严格相邻
                    if (worldRight.x != worldLeft.x + 1 ||
                        worldRight.y != worldLeft.y ||
                        worldRight.z != worldLeft.z)
                    {
                        coordinateBreaks++;
                    }

                    VoxelType fromLeft = chunkLeft.GetVoxel(localLeft, y, z).Type;
                    VoxelType fromRight = chunkRight.GetVoxel(localRight, y, z).Type;

                    // 3b) 诊断：相邻列体素差异（地形坡度允许差异；不参与硬断言）
                    if (fromLeft != fromRight) adjacentColumnVoxelDifferences++;

                    // 3c) 硬断言：两侧各自都由**同一世界函数**解释
                    GeologySample sampleLeft =
                        plainSampler.Sample(worldLeft.x, worldLeft.y, worldLeft.z, stage1.Seed);
                    GeologySample sampleRight =
                        plainSampler.Sample(worldRight.x, worldRight.y, worldRight.z, stage1.Seed);

                    VoxelType sampledLeft = sampleLeft.IsSolid ? sampleLeft.Material : VoxelType.Air;
                    VoxelType sampledRight = sampleRight.IsSolid ? sampleRight.Material : VoxelType.Air;

                    if (sampledLeft != fromLeft || sampledRight != fromRight)
                    {
                        samplerMismatch++;
                        if (firstSamplerMismatch == null)
                        {
                            firstSamplerMismatch = "左 " + worldLeft + " 生成 " + fromLeft +
                                                   " 采样 " + sampledLeft + "；右 " + worldRight +
                                                   " 生成 " + fromRight + " 采样 " + sampledRight;
                        }
                    }
                }
            }
        }

        Check(caveExceeded == 0 && caveWorst <= caveBound,
            "GB2：洞穴噪声跨边界连续（最大相邻差分 " + caveWorst.ToString("F6") +
            " ≤ 上界 " + caveBound.ToString("F6") + "，越界 " + caveExceeded + "）");

        Check(worstHeightDelta <= 2,
            "GB2：地表高度相邻差分有界（最大 " + worstHeightDelta + " ≤ 2；无断崖）");

        Check(coordinateBreaks == 0,
            "GB2：缝处世界坐标严格相邻（无缝隙、无重叠；违反 " + coordinateBreaks + "）");

        Check(seamVoxelsCompared > 0 && samplerMismatch == 0,
            "GB2：缝两侧的体素都由同一世界函数解释（比较 " + seamVoxelsCompared +
            " 个体素，不一致 " + samplerMismatch + "）" +
            (firstSamplerMismatch == null ? "" : " 首次：" + firstSamplerMismatch));

        // 4) 世界原点跨越（x=-1 与 x=0 必须相邻，floor 语义最容易出错处）
        VoxelCoordinate originLeft = new VoxelChunk(new VoxelCoordinate(-1, 0, 0))
            .LocalToWorldCoordinate(15, 0, 0);
        VoxelCoordinate originRight = new VoxelChunk(new VoxelCoordinate(0, 0, 0))
            .LocalToWorldCoordinate(0, 0, 0);
        Check(originLeft.x == -1 && originRight.x == 0 && originRight.x - originLeft.x == 1,
            "GB2：世界原点跨越正确（" + originLeft + " → " + originRight + "）");

        // 5) 相邻 Chunk 的**地下切片**必须逐体素一致（与既有 WorldGenerationCoreValidation 同型：
        //    缝处的地层数据来自同一世界函数 ⇒ 在地下（纯地层）区域应完全一致）
        int undergroundCompared = 0;
        int undergroundDifferences = 0;

        VoxelChunk undergroundLeft =
            generator.Generate(VoxelWorldData.WorldToChunkCoordinate(new VoxelCoordinate(15, 0, 0)));
        VoxelChunk undergroundRight =
            generator.Generate(VoxelWorldData.WorldToChunkCoordinate(new VoxelCoordinate(16, 0, 0)));

        for (int y = 0; y < VoxelChunk.ChunkSize; y++)
        {
            for (int z = 0; z < VoxelChunk.ChunkSize; z++)
            {
                undergroundCompared++;
                if (undergroundLeft.GetVoxel(15, y, z).Type !=
                    undergroundRight.GetVoxel(0, y, z).Type)
                {
                    undergroundDifferences++;
                }
            }
        }

        Check(undergroundDifferences == 0,
            "GB2：相邻 Chunk 边界地下切片逐体素一致（与既有入口同型；比较 " +
            undergroundCompared + "，不一致 " + undergroundDifferences + "）");

        Debug.Log("[GeologyStageBValidation] GB2 诊断：缝体素比较 " + seamVoxelsCompared +
                  "，相邻列差异 " + adjacentColumnVoxelDifferences +
                  "（坡度允许），最大高度差 " + worstHeightDelta +
                  "，洞穴最大差分 " + caveWorst.ToString("F6"));
    }

    // =====================================================================
    // GB3：Chunk Independence（同世界坐标，通过不同 Chunk 请求/不同已加载集合）
    // =====================================================================

    private static void ValidateChunkIndependence()
    {
        GeologySampler sampler = CreateSampler(WorldGenerationSettings.Default, GeologySettings.Default);

        // 关注点：一个世界坐标，在"不同 Chunk 请求顺序 / 不同已加载集合"下采样结果不变
        int[][] coordinates =
        {
            new int[] { 15, 24, 7 },        // chunk(0,1,0) 的边界
            new int[] { 16, 24, 0 },        // chunk(1,1,0) 的起点
            new int[] { -1, 24, -1 },       // chunk(-1,1,-1) 的末端
            new int[] { 7, 40, 9 }
        };

        GeologySample[] baseline = new GeologySample[coordinates.Length];
        for (int i = 0; i < coordinates.Length; i++)
        {
            baseline[i] = sampler.Sample(coordinates[i][0], coordinates[i][1], coordinates[i][2], 12345);
        }

        // 通过"不同的 Chunk 请求"来构造不同的已加载集合，然后重新采样同一世界坐标
        VoxelWorldData world = new VoxelWorldData();
        BasicTerrainGenerator generator = new BasicTerrainGenerator(WorldGenerationSettings.Default);

        VoxelCoordinate[] requestOrderA =
        {
            new VoxelCoordinate(0, 1, 0), new VoxelCoordinate(1, 1, 0),
            new VoxelCoordinate(-1, 1, -1), new VoxelCoordinate(0, 1, -1)
        };

        VoxelCoordinate[] requestOrderB =
        {
            new VoxelCoordinate(0, 1, -1), new VoxelCoordinate(-1, 1, -1),
            new VoxelCoordinate(1, 1, 0), new VoxelCoordinate(0, 1, 0)
        };

        int mismatch = 0;

        for (int i = 0; i < requestOrderA.Length; i++)
        {
            world.GetOrCreateChunk(requestOrderA[i]);
            generator.Generate(requestOrderA[i]);

            for (int c = 0; c < coordinates.Length; c++)
            {
                GeologySample again = sampler.Sample(
                    coordinates[c][0], coordinates[c][1], coordinates[c][2], 12345);
                if (!SameSample(baseline[c], again)) mismatch++;
            }
        }

        Check(world.ChunkCount == requestOrderA.Length,
            "GB3：已加载集合 A 建立（" + world.ChunkCount + " 个 Chunk）");

        for (int i = 0; i < requestOrderB.Length; i++)
        {
            world.GetOrCreateChunk(requestOrderB[i]);
            generator.Generate(requestOrderB[i]);

            for (int c = 0; c < coordinates.Length; c++)
            {
                GeologySample again = sampler.Sample(
                    coordinates[c][0], coordinates[c][1], coordinates[c][2], 12345);
                if (!SameSample(baseline[c], again)) mismatch++;
            }
        }

        Check(mismatch == 0,
            "GB3：不同 Chunk 请求顺序 / 不同已加载集合下，采样结果完全一致（不一致 " +
            mismatch + "）");

        // 采样器与"Chunk 窗口"无关：Chunk 生成结果本身也不受请求顺序影响
        VoxelChunk forward = generator.Generate(new VoxelCoordinate(0, 1, 0));
        VoxelWorldData secondWorld = new VoxelWorldData();
        secondWorld.GetOrCreateChunk(new VoxelCoordinate(1, 1, 0));
        VoxelChunk backward = generator.Generate(new VoxelCoordinate(0, 1, 0));

        int chunkDifferences = 0;
        for (int y = 0; y < VoxelChunk.ChunkSize; y++)
        {
            for (int z = 0; z < VoxelChunk.ChunkSize; z++)
            {
                for (int x = 0; x < VoxelChunk.ChunkSize; x++)
                {
                    if (forward.GetVoxel(x, y, z).Type != backward.GetVoxel(x, y, z).Type)
                    {
                        chunkDifferences++;
                    }
                }
            }
        }

        Check(chunkDifferences == 0,
            "GB3：同一 Chunk 的生成结果与请求顺序无关（差异 " + chunkDifferences + "）");

        Debug.Log("[GeologyStageBValidation] GB3 采样基线示例：\n" +
                  sampler.ExplainAt(coordinates[0][0], coordinates[0][1], coordinates[0][2], 12345));
    }

    // =====================================================================
    // GB4：ExplainAt 与 Sample 一致（同一判定链）
    // =====================================================================

    private static void ValidateExplainMatchesSample()
    {
        GeologySettings geology = GeologySettings.Default;
        geology.CaveThreshold = -0.5f;      // 让洞穴既可能生效、也可能不生效
        GeologySampler sampler = CreateSampler(WorldGenerationSettings.Default, geology);

        int[][] coordinates =
        {
            new int[] { 0, 0, 0 },
            new int[] { 0, 10, 0 },
            new int[] { 0, 24, 0 },
            new int[] { 0, 25, 0 },
            new int[] { 0, 63, 0 },
            new int[] { 0, 64, 0 },
            new int[] { 0, -1, 0 },
            new int[] { 15, 20, 15 },
            new int[] { 16, 20, 16 },
            new int[] { -1, 20, -1 }
        };

        int parsed = 0;
        int mismatch = 0;
        string firstMismatch = null;

        for (int i = 0; i < coordinates.Length; i++)
        {
            int x = coordinates[i][0];
            int y = coordinates[i][1];
            int z = coordinates[i][2];

            GeologySample sample = sampler.Sample(x, y, z, 12345);
            string text = sampler.ExplainAt(x, y, z, 12345);

            Check(text.Contains("Coordinate:  (" + x + ", " + y + ", " + z + ")"),
                "GB4：诊断文本含坐标行（" + x + "," + y + "," + z + "）");
            Check(text.Contains("Rule:        " + sample.Rule),
                "GB4：诊断文本的 Rule == Sample.Rule（" + sample.Rule + "）");
            Check(text.Contains("Density:     " + (sample.IsSolid ? "Solid" : "Empty")),
                "GB4：诊断文本的 Density == Sample.IsSolid");
            Check(text.Contains("Material:    " + sample.Material),
                "GB4：诊断文本的 Material == Sample.Material（" + sample.Material + "）");
            Check(text.Contains("CaveDensity: " + sample.CaveDensity.ToString("F6", CultureInfo.InvariantCulture)),
                "GB4：诊断文本的 CaveDensity == Sample.CaveDensity（" +
                sample.CaveDensity.ToString("F6", CultureInfo.InvariantCulture) + "）");
            Check(text.Contains("Deposit:     " + sample.DepositZone) &&
                  text.Contains("Biome:       " + sample.Biome),
                "GB4：诊断文本含 Deposit / Biome 占位");
            Check(text.Contains("Surface:     " + sample.SurfaceHeight + " (soil " +
                                sample.SoilDepth + ")"),
                "GB4：诊断文本含地表高度与土壤深度");

            // 解析回读（更强：文本值必须能被解析并等于 Sample 字段）
            string ruleLine = ExtractLine(text, "Rule:");
            string densityLine = ExtractLine(text, "Density:");
            string materialLine = ExtractLine(text, "Material:");
            string densityModLine = ExtractLine(text, "DensityMod:");
            string caveStateLine = ExtractLine(text, "CaveState:");

            parsed++;

            if (ruleLine != sample.Rule.ToString() ||
                densityLine != (sample.IsSolid ? "Solid" : "Empty") ||
                materialLine != sample.Material.ToString() ||
                densityModLine != sampler.DensityModifierAt(x, y, z, 12345).ToString() ||
                caveStateLine != sampler.CaveStateAt(x, y, z, 12345).ToString())
            {
                mismatch++;
                if (firstMismatch == null)
                {
                    firstMismatch = "(" + x + "," + y + "," + z + ") rule=" + ruleLine +
                                    " material=" + materialLine;
                }
            }
        }

        Check(parsed == coordinates.Length,
            "GB4：解析覆盖全部坐标（" + parsed + " / " + coordinates.Length + "）");
        Check(mismatch == 0,
            "GB4：ExplainAt 解析值与 Sample/访问器一致（不一致 " + mismatch + "）" +
            (firstMismatch == null ? "" : " 首次：" + firstMismatch));

        // 无参重载与显式 seed 重载一致
        Check(sampler.ExplainAt(3, 20, 4) ==
              sampler.ExplainAt(3, 20, 4, WorldGenerationSettings.Default.Seed),
            "GB4：ExplainAt 无参重载 == 显式 Seed 重载");

        // 诊断文本必须包含任务要求的字段行
        string sampleText = sampler.ExplainAt(10, 25, 30, 12345);
        foreach (string required in new string[]
        {
            "Coordinate:", "Rule:", "Noise:", "CaveDensity:", "Deposit:",
            "DensityMod:", "CaveState:", "Material:", "Density:", "Biome:"
        })
        {
            Check(sampleText.Contains(required), "GB4：诊断文本包含字段 " + required);
        }

        // 洞穴"生效 vs 被压制"两种状态都可观测（Suppressed 需要世界之外的 y，即 P0 覆盖 P1）
        int carved = 0;
        int suppressed = 0;
        for (int x = -16; x < 16; x++)
        {
            for (int y = -2; y < 66; y++)
            {
                GeologyCaveState state = sampler.CaveStateAt(x, y, 0, 12345);
                if (state == GeologyCaveState.Carved) carved++;
                if (state == GeologyCaveState.Suppressed) suppressed++;
            }
        }

        Check(carved > 0, "GB4：存在 Carved 状态（" + carved + "）");
        Check(suppressed > 0,
            "GB4：存在 Suppressed 状态（世界之外的洞穴噪声被 P0 覆盖：" + suppressed + "）");

        // Suppressed 的坐标必须同时满足"噪声达阈值"与"最终规则为 WorldBounds"
        GeologySample suppressedSample = default(GeologySample);
        bool foundSuppressed = false;

        for (int y = -2; y < 0 && !foundSuppressed; y++)
        {
            for (int x = -8; x < 8 && !foundSuppressed; x++)
            {
                GeologySample candidate = sampler.Sample(x, y, 0, 12345);
                if (candidate.CaveActive && candidate.Rule == GeologyRule.WorldBounds)
                {
                    suppressedSample = candidate;
                    foundSuppressed = true;
                }
            }
        }

        Check(foundSuppressed && !suppressedSample.IsSolid &&
              suppressedSample.Material == VoxelType.Air,
            "GB4：Suppressed 样本满足 P0 覆盖 P1（" + suppressedSample + "）");
    }

    // =====================================================================
    // GB5：GIV9 —— 关闭全部 Geology 特性 ⇒ 与 Stage 1 基线逐体素一致
    // =====================================================================

    private static void ValidateStage1BaselineEquivalence()
    {
        WorldGenerationSettings stage1 = WorldGenerationSettings.Default;

        GeologySettings disabled = GeologySettings.Default;
        disabled.EnableCaves = false;
        disabled.EnableDeposits = false;
        disabled.EnableBiomeModifiers = false;

        GeologySampler sampler = CreateSampler(stage1, disabled);
        BasicTerrainGenerator baseline = new BasicTerrainGenerator(stage1);

        VoxelCoordinate[] chunks =
        {
            new VoxelCoordinate(0, 0, 0),
            new VoxelCoordinate(0, 1, 0),
            new VoxelCoordinate(0, 2, 0),
            new VoxelCoordinate(-1, 0, 0),
            new VoxelCoordinate(-1, 1, -1),
            new VoxelCoordinate(2, 3, 2)
        };

        int totalVoxels = 0;
        int mismatches = 0;
        string firstMismatch = null;
        int solidVoxels = 0;
        int airVoxels = 0;

        for (int c = 0; c < chunks.Length; c++)
        {
            VoxelChunk chunk = baseline.Generate(chunks[c]);

            for (int y = 0; y < VoxelChunk.ChunkSize; y++)
            {
                for (int z = 0; z < VoxelChunk.ChunkSize; z++)
                {
                    for (int x = 0; x < VoxelChunk.ChunkSize; x++)
                    {
                        VoxelCoordinate world = chunk.LocalToWorldCoordinate(x, y, z);
                        VoxelType expected = chunk.GetVoxel(x, y, z).Type;

                        GeologySample sample = sampler.Sample(world.x, world.y, world.z, stage1.Seed);
                        VoxelType actual = sample.IsSolid ? sample.Material : VoxelType.Air;

                        totalVoxels++;
                        if (expected == VoxelType.Air) airVoxels++; else solidVoxels++;

                        if (actual != expected)
                        {
                            mismatches++;
                            if (firstMismatch == null)
                            {
                                firstMismatch = world + " 期望 " + expected + " 实际 " + actual +
                                                "（" + sample + "）";
                            }
                        }
                    }
                }
            }
        }

        Check(totalVoxels == chunks.Length * VoxelChunk.VoxelCount,
            "GB5：比较体素数 = " + chunks.Length + " × 4096（实际 " + totalVoxels + "）");
        Check(solidVoxels > 0 && airVoxels > 0,
            "GB5：样本同时含实体与空气（非平凡断言；实体 " + solidVoxels +
            " / 空气 " + airVoxels + "）");
        Check(mismatches == 0,
            "GB5/GIV9：关闭全部地质特性 ⇒ 与 Stage 1 基线逐体素一致（不一致 " + mismatches + "）" +
            (firstMismatch == null ? "" : " 首次：" + firstMismatch));

        // 边界参数：SoilDepth = 0 也必须一致
        WorldGenerationSettings noSoil = stage1;
        noSoil.SoilDepth = 0;

        GeologySampler noSoilSampler = CreateSampler(noSoil, disabled);
        BasicTerrainGenerator noSoilBaseline = new BasicTerrainGenerator(noSoil);

        int noSoilMismatch = 0;
        VoxelChunk noSoilChunk = noSoilBaseline.Generate(new VoxelCoordinate(0, 0, 0));

        for (int y = 0; y < VoxelChunk.ChunkSize; y++)
        {
            for (int z = 0; z < VoxelChunk.ChunkSize; z++)
            {
                for (int x = 0; x < VoxelChunk.ChunkSize; x++)
                {
                    VoxelCoordinate world = noSoilChunk.LocalToWorldCoordinate(x, y, z);
                    VoxelType expected = noSoilChunk.GetVoxel(x, y, z).Type;
                    GeologySample sample = noSoilSampler.Sample(world.x, world.y, world.z, noSoil.Seed);
                    VoxelType actual = sample.IsSolid ? sample.Material : VoxelType.Air;
                    if (actual != expected) noSoilMismatch++;
                }
            }
        }

        Check(noSoilMismatch == 0,
            "GB5：SoilDepth=0 边界同样与基线一致（不一致 " + noSoilMismatch + "）");

        // 启用洞穴后必须产生差异（证明 GIV9 不是"恒真断言"）
        GeologySettings enabled = disabled.Clone();
        enabled.EnableCaves = true;
        enabled.CaveThreshold = 0f;
        GeologySampler caveSampler = CreateSampler(stage1, enabled);

        int caveDifferences = 0;
        VoxelChunk reference = baseline.Generate(new VoxelCoordinate(0, 0, 0));

        for (int y = 0; y < VoxelChunk.ChunkSize; y++)
        {
            for (int z = 0; z < VoxelChunk.ChunkSize; z++)
            {
                for (int x = 0; x < VoxelChunk.ChunkSize; x++)
                {
                    VoxelCoordinate world = reference.LocalToWorldCoordinate(x, y, z);
                    VoxelType expected = reference.GetVoxel(x, y, z).Type;
                    GeologySample sample = caveSampler.Sample(world.x, world.y, world.z, stage1.Seed);
                    VoxelType actual = sample.IsSolid ? sample.Material : VoxelType.Air;

                    // 洞穴只允许把实体变成空：不得把空变成实体、不得改变材料种类
                    if (actual != expected)
                    {
                        if (expected == VoxelType.Air || actual != VoxelType.Air)
                        {
                            caveDifferences = -1;
                        }
                        else
                        {
                            caveDifferences++;
                        }
                    }
                }
            }
        }

        Check(caveDifferences > 0,
            "GB5：启用洞穴后确实产生差异（GIV9 非恒真；实体→空 " + caveDifferences + " 处）");
    }

    // =====================================================================
    // GB6：结构扫描（GeologySampler 不引用 UnityEngine / Chunk / Runtime / Entity）
    // =====================================================================

    private static readonly string[] ForbiddenPatterns =
    {
        // Unity 依赖
        "UnityEngine", "UnityEditor", "MonoBehaviour", "GameObject", "Transform",
        @"\bVector2\b", @"\bVector3\b", @"new Material\(", @"\bRandom\b", @"\bMathf\b",
        @"Time\.", "DateTime", @"\bGuid\b",
        // Chunk 坐标 / Chunk 状态 / 数据容器（Stage B 不得出现）
        @"\bchunkCoordinate\b", @"\bchunkX\b", @"\bchunkY\b", @"\bchunkZ\b",
        "VoxelChunk", @"new VoxelWorld\b", @"VoxelWorld\.(?!Generation)",
        "SetVoxel", "MarkDirty", "ClearDirty",
        // Runtime / Streaming / Mesh / Entity
        "VoxelRuntimeCore", "ChunkStreamingDriver", "IChunkPresenter", "IChunkMesher",
        "VoxelMeshGenerator", "VoxelMeshData",
        "CrystalCluster", "Instantiate", "Prefab",
        // 必须"独立表达"判定链：禁止调用/复制既有生成器规则
        "BasicTerrainGenerator", "ClassifyVoxel"
    };

    private static void ValidateStructuralScan()
    {
        string folder = Path.Combine(Application.dataPath, GeologyFolder);
        Check(Directory.Exists(folder), "GB6：Geology 目录存在（" + folder + "）");
        if (!Directory.Exists(folder)) return;

        string samplerPath = Path.Combine(folder, "GeologySampler.cs");
        Check(File.Exists(samplerPath), "GB6：存在 GeologySampler.cs");
        if (!File.Exists(samplerPath)) return;

        string code = StripComments(File.ReadAllText(samplerPath, Encoding.UTF8));

        // 扫描器自检
        string probe = "using UnityEngine;\n// UnityEngine 在注释里不计\nvoid P(){ }\n";
        Check(ScanTokens(StripComments(probe), ForbiddenPatterns).Count == 1,
            "GB6：扫描器自检（代码区命中 1）");

        string commentProbe = "// UnityEngine VoxelChunk BasicTerrainGenerator\nvoid Q(){ }\n";
        Check(ScanTokens(StripComments(commentProbe), ForbiddenPatterns).Count == 0,
            "GB6：扫描器忽略注释");

        string namespaceProbe = "namespace VoxelWorld.Generation.Geology { }\n";
        Check(ScanTokens(StripComments(namespaceProbe), ForbiddenPatterns).Count == 0,
            "GB6：namespace VoxelWorld.Generation.Geology 不算违规（正向对照）");

        List<string> found = ScanTokens(code, ForbiddenPatterns);
        if (found.Count > 0)
        {
            Check(false, "GB6：GeologySampler.cs 命中禁止项：" +
                         string.Join(", ", found.ToArray()));
        }

        Check(found.Count == 0,
            "GB6：GeologySampler 不引用 UnityEngine / Chunk / Runtime / Mesh / Entity / " +
            "既有生成器规则（命中 " + found.Count + "）");

        // 静态可变字段（禁止隐藏全局状态）
        string[] lines = code.Replace("\r\n", "\n").Split('\n');
        int mutableStatic = 0;

        for (int l = 0; l < lines.Length; l++)
        {
            string line = lines[l].Trim();

            if (line.Length == 0) continue;
            if (line.IndexOf("static", StringComparison.Ordinal) < 0) continue;
            if (line.IndexOf("const", StringComparison.Ordinal) >= 0) continue;
            if (line.IndexOf("readonly", StringComparison.Ordinal) >= 0) continue;
            if (line.IndexOf('(') >= 0) continue;
            if (line.IndexOf("=>", StringComparison.Ordinal) >= 0) continue;
            if (Regex.IsMatch(line, @"\b(class|struct|interface|enum|delegate)\b")) continue;
            if (line.EndsWith("{", StringComparison.Ordinal)) continue;
            if (NextNonEmptyLineStartsWithBrace(lines, l)) continue;

            mutableStatic++;
            Check(false, "GB6：存在静态可变字段候选：L" + (l + 1) + " " + line);
        }

        Check(mutableStatic == 0, "GB6：无静态可变字段（命中 " + mutableStatic + "）");

        // 单一判定链（ExplainAt 必须走 Resolve，不得复制另一套计算）
        Check(code.Contains("private GeologySample Resolve("),
            "GB6：存在唯一判定链 Resolve(...)（正向对照）");
        Check(CountOccurrences(code, "GeologySample Resolve(") == 1,
            "GB6：判定链只有一份实现（GeologySample Resolve( 出现 " +
            CountOccurrences(code, "GeologySample Resolve(") + " 次）");
        Check(code.Contains("Resolve(worldX, worldY, worldZ, seed)"),
            "GB6：ExplainAt / Sample 复用 Resolve（正向对照）");
        Check(!code.Contains("ClassifyVoxel") && !code.Contains("BasicTerrainGenerator"),
            "GB6：判定链为独立表达（不调用既有生成器规则、不复制其源码）");

        // 高度场复用（Stage 1 定义）——正向对照
        Check(code.Contains("HeightFunction.GetTerrainHeight"),
            "GB6：地表高度复用 Stage 1 高度场定义 HeightFunction（正向对照；GB5 可满足的前提）");

        Debug.Log("[GeologyStageBValidation] 扫描文件：" + samplerPath);
    }

    private static int CountOccurrences(string text, string token)
    {
        int count = 0;
        int index = 0;

        while (true)
        {
            index = text.IndexOf(token, index, StringComparison.Ordinal);
            if (index < 0) break;
            count++;
            index += token.Length;
        }

        return count;
    }

    private static List<string> ScanTokens(string text, string[] patterns)
    {
        List<string> hits = new List<string>();

        for (int i = 0; i < patterns.Length; i++)
        {
            if (Regex.IsMatch(text, patterns[i])) hits.Add(patterns[i]);
        }

        return hits;
    }

    private static bool NextNonEmptyLineStartsWithBrace(string[] lines, int index)
    {
        for (int i = index + 1; i < lines.Length; i++)
        {
            string next = lines[i].Trim();
            if (next.Length == 0) continue;
            return next.StartsWith("{", StringComparison.Ordinal);
        }

        return false;
    }

    private static string StripComments(string text)
    {
        StringBuilder output = new StringBuilder(text.Length);
        string[] lines = text.Replace("\r\n", "\n").Split('\n');
        bool inBlockComment = false;

        for (int l = 0; l < lines.Length; l++)
        {
            string line = lines[l];
            StringBuilder kept = new StringBuilder(line.Length);
            int i = 0;

            while (i < line.Length)
            {
                if (inBlockComment)
                {
                    int end = line.IndexOf("*/", i, StringComparison.Ordinal);
                    if (end < 0) { i = line.Length; break; }
                    inBlockComment = false;
                    i = end + 2;
                    continue;
                }

                if (i + 1 < line.Length && line[i] == '/' && line[i + 1] == '/') break;

                if (i + 1 < line.Length && line[i] == '/' && line[i + 1] == '*')
                {
                    inBlockComment = true;
                    i += 2;
                    continue;
                }

                kept.Append(line[i]);
                i++;
            }

            output.Append(kept).Append('\n');
        }

        return output.ToString();
    }

    // =====================================================================
    // GB7：零修改（既有文件 SHA 不变）
    // =====================================================================

    private static readonly Dictionary<string, string> FrozenFiles =
        new Dictionary<string, string>
        {
            // 规范（12 份）+ ADR
            { "../Specifications/World/VoxelWorld_Architecture.md", "779DF1EFC4082579" },
            { "../Specifications/World/VoxelWorld_Core.md", "F718B68E95100C57" },
            { "../Specifications/World/WorldGeneration_Core.md", "684753D5E6B31565" },
            { "../Specifications/World/VoxelWorld_Mesh.md", "81CEDBFA6E9756CE" },
            { "../Specifications/World/VoxelWorld_Mining.md", "8A2B020DDB97422B" },
            { "../Specifications/World/VoxelWorld_View.md", "D9BD5422026E21B6" },
            { "../Specifications/World/VoxelWorld_Runtime.md", "EDD02AC50FC03E5E" },
            { "../Specifications/World/VoxelWorld_Runtime_Core.md", "10F1EC7F10DAD8F3" },
            { "../Specifications/World/VoxelWorld_Runtime_Interface.md", "0AE21EB684412685" },
            { "../Specifications/World/VoxelWorld_Runtime_Core_Implementation.md", "D816F09C60A810C4" },
            { "../Specifications/World/WorldGeneration_Geology.md", "1A7DD7E7AE1EBAC9" },
            { "../Specifications/World/WorldGeneration_Geology_Implementation.md", "9BF0E11A4E5CA87D" },
            { "../Specifications/ArchitectureDecisionWorldSpaceEntity.txt", "218D1CAAA738C59D" },
            // VoxelCore / Generation / Mesh / View
            { "Scripts/VoxelWorld/VoxelType.cs", "02C1F54580AEE541" },
            { "Scripts/VoxelWorld/Voxel.cs", "DC52145E6E82132D" },
            { "Scripts/VoxelWorld/VoxelCoordinate.cs", "F327B2761DFDCFC2" },
            { "Scripts/VoxelWorld/VoxelChunk.cs", "4DBDFBAC5BCE500C" },
            { "Scripts/VoxelWorld/VoxelWorld.cs", "D9E749A466635918" },
            { "Scripts/VoxelWorld/VoxelWorldDebug.cs", "6AE0C41274AF9154" },
            { "Scripts/VoxelWorld/Generation/WorldGenerationSettings.cs", "140B7E289F05C6FA" },
            { "Scripts/VoxelWorld/Generation/IWorldGenerator.cs", "B39614CA9BF37921" },
            { "Scripts/VoxelWorld/Generation/HeightFunction.cs", "715341D5FC652408" },
            { "Scripts/VoxelWorld/Generation/BasicTerrainGenerator.cs", "7FFD8F4B1226EA4E" },
            { "Scripts/VoxelWorld/Mesh/VoxelMeshData.cs", "8B78A03ED1C78E26" },
            { "Scripts/VoxelWorld/Mesh/VoxelMeshGenerator.cs", "653804C19C272C72" },
            { "Scripts/VoxelWorld/View/VoxelChunkView.cs", "9D92BA355535ADCA" },
            { "Scripts/VoxelWorld/View/UnityMeshBuilder.cs", "F9E4224996AC7CE2" },
            { "Scripts/VoxelWorld/View/ChunkViewPresenter.cs", "C6346E735591E2BF" },
            // Runtime Core / Streaming / Bootstrap
            { "Scripts/VoxelWorld/Runtime/Abstractions/IChunkMesher.cs", "8389FA2E4A4DCF7D" },
            { "Scripts/VoxelWorld/Runtime/Abstractions/IChunkPresenter.cs", "B743B37CD921AC8E" },
            { "Scripts/VoxelWorld/Runtime/ChunkLifecycleState.cs", "D05B26A6E84BF314" },
            { "Scripts/VoxelWorld/Runtime/RuntimeCoreSettings.cs", "C44D9D517F9974CB" },
            { "Scripts/VoxelWorld/Runtime/RuntimeCoreTickReport.cs", "25D00EEA0C43B246" },
            { "Scripts/VoxelWorld/Runtime/RuntimeCoreDiagnostics.cs", "8006333FE230FD65" },
            { "Scripts/VoxelWorld/Runtime/ChunkRecord.cs", "95BA7DB5D34198E1" },
            { "Scripts/VoxelWorld/Runtime/ChunkRecordSnapshot.cs", "ACCC7B1BC8677AD5" },
            { "Scripts/VoxelWorld/Runtime/RuntimeChangeSet.cs", "1425E571E78401FF" },
            { "Scripts/VoxelWorld/Runtime/VoxelRuntimeCore.cs", "9119E8D3292A4C71" },
            { "Scripts/VoxelWorld/Runtime/Adapters/VoxelMeshGeneratorMesher.cs", "8A21FE91027AA135" },
            { "Scripts/VoxelWorld/Runtime/Streaming/IChunkRequestSink.cs", "9481A8BA397ACE60" },
            { "Scripts/VoxelWorld/Runtime/Streaming/ChunkStreamingSettings.cs", "6BA404AF57F553C3" },
            { "Scripts/VoxelWorld/Runtime/Streaming/ChunkStreamingReport.cs", "B57EA40C7FA6ECC9" },
            { "Scripts/VoxelWorld/Runtime/Streaming/ChunkStreamingDriver.cs", "26DF1B6B22E2DFF3" },
            { "Scripts/VoxelWorld/Runtime/Streaming/RuntimeCoreRequestSink.cs", "FD0E5CD9D1C9D77B" },
            { "Scripts/Game/VoxelWorldStreamingBootstrap.cs", "4738634952568520" },
            // 既有 Editor 验证入口
            { "Editor/VoxelWorldCoreValidation.cs", "6DE813AABCE95394" },
            { "Editor/WorldGenerationCoreValidation.cs", "4A517A0F902A1066" },
            { "Editor/VoxelMeshCoreValidation.cs", "5FFD8451D2CE05EB" },
            { "Editor/VoxelWorldViewValidation.cs", "BEFBAAF6B111A241" },
            { "Editor/VoxelWorldRuntimeCoreValidation.cs", "0E0B5C160FEAB236" },
            { "Editor/ChunkStreamingValidation.cs", "13E244B6EE978FC8" },
            // Stage A 的地质实现（本轮未改）
            { "Scripts/VoxelWorld/Generation/Geology/GeologySettings.cs", "BEA8EB4F4967721C" },
            { "Scripts/VoxelWorld/Generation/Geology/GeologyNoise.cs", "2266E7A880E43315" },
            { "Scripts/VoxelWorld/Generation/Geology/GeologySample.cs", "4946B90F3E56262E" },
            // Stage A 验证入口：**经用户明确授权**修改两次（① GSA5 固定文件数 ⇒ 核心文件存在 +
            // 禁止未知 Runtime/Entity 类型 + 允许未来扩展文件；② GSA5 扫描作用域拆分：目录级禁令 vs
            // 采样层专属禁令，接入层豁免）⇒ 重新钉住新哈希；
            // 修改登记见 GeologyStageCValidation.AuthorizedChangedFiles。
            { "Editor/GeologyStageAValidation.cs", "DF42F8605BA60CAF" }
        };

    private static void ValidateExistingFilesUnchanged()
    {
        int compared = 0;
        int missing = 0;
        int mismatch = 0;
        int skipped = 0;

        string specificationRoot = FindSpecificationsRoot();

        foreach (KeyValuePair<string, string> pair in FrozenFiles)
        {
            bool isSpecification = pair.Key.StartsWith("../Specifications/", StringComparison.Ordinal);

            if (isSpecification)
            {
                if (specificationRoot == null) { skipped++; continue; }

                string specificationPath = Path.Combine(
                    specificationRoot, pair.Key.Substring(3).Replace('/', Path.DirectorySeparatorChar));

                if (!File.Exists(specificationPath))
                {
                    missing++;
                    Check(false, "GB7：缺失 " + pair.Key);
                    continue;
                }

                string specificationHash = ComputeFileHash16(specificationPath);
                if (specificationHash == null) continue;

                compared++;
                if (specificationHash != pair.Value)
                {
                    mismatch++;
                    Check(false, "GB7：" + pair.Key + " 已改变（" + specificationHash +
                                 " != " + pair.Value + "）");
                }

                continue;
            }

            string path = Path.Combine(Application.dataPath, pair.Key);
            if (!File.Exists(path))
            {
                missing++;
                Check(false, "GB7：缺失 " + pair.Key);
                continue;
            }

            string hash = ComputeFileHash16(path);
            if (hash == null) continue;

            compared++;
            if (hash != pair.Value)
            {
                mismatch++;
                Check(false, "GB7：" + pair.Key + " 已改变（" + hash + " != " + pair.Value + "）");
            }
        }

        Check(missing == 0, "GB7：既有文件全部存在（缺失 " + missing + "）");
        Check(mismatch == 0,
            "GB7：既有文件 SHA256(前16) 全部不变（比对 " + compared + " 个；跳过 " + skipped + "）");
    }

    private static string FindSpecificationsRoot()
    {
        DirectoryInfo directory = new DirectoryInfo(Application.dataPath);

        for (int i = 0; i < 4 && directory != null; i++)
        {
            string candidate = Path.Combine(directory.FullName, "Specifications", "World");
            if (Directory.Exists(candidate)) return directory.FullName;
            directory = directory.Parent;
        }

        return null;
    }

    private static string ComputeFileHash16(string path)
    {
        try
        {
            using (FileStream stream = File.OpenRead(path))
            {
                SHA256 sha = SHA256.Create();
                byte[] digest = sha.ComputeHash(stream);
                StringBuilder text = new StringBuilder(16);
                for (int i = 0; i < 8; i++) text.Append(digest[i].ToString("X2"));
                return text.ToString();
            }
        }
        catch (Exception ex)
        {
            Check(false, "GB7：无法计算哈希（" + path + "）：" + ex.Message);
            return null;
        }
    }

    // =====================================================================
    // 测试辅助
    // =====================================================================

    private static GeologySampler CreateSampler(
        WorldGenerationSettings stage1, GeologySettings geology)
    {
        return new GeologySampler(stage1, geology);
    }

    private static bool SameSample(GeologySample a, GeologySample b)
    {
        return CompareSamples(a, b) == null;
    }

    private static string CompareSamples(GeologySample a, GeologySample b)
    {
        if (a.Rule != b.Rule) return "Rule " + a.Rule + " != " + b.Rule;
        if (a.IsSolid != b.IsSolid) return "IsSolid " + a.IsSolid + " != " + b.IsSolid;
        if (a.Material != b.Material) return "Material " + a.Material + " != " + b.Material;
        if (a.SurfaceHeight != b.SurfaceHeight)
            return "SurfaceHeight " + a.SurfaceHeight + " != " + b.SurfaceHeight;
        if (a.SoilDepth != b.SoilDepth) return "SoilDepth " + a.SoilDepth + " != " + b.SoilDepth;
        if (a.CaveActive != b.CaveActive) return "CaveActive " + a.CaveActive + " != " + b.CaveActive;
        if (BitEquals(a.CaveDensity, b.CaveDensity) == false)
            return "CaveDensity " + a.CaveDensity + " != " + b.CaveDensity;
        if (a.DepositZone != b.DepositZone)
            return "DepositZone " + a.DepositZone + " != " + b.DepositZone;
        if (a.Biome != b.Biome) return "Biome " + a.Biome + " != " + b.Biome;

        return null;
    }

    private static bool BitEquals(float a, float b)
    {
        return BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);
    }

    private static string ExtractLine(string text, string key)
    {
        string[] lines = text.Replace("\r\n", "\n").Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            int index = line.IndexOf(key, StringComparison.Ordinal);
            if (index < 0) continue;

            string value = line.Substring(index + key.Length).Trim();
            return value;
        }

        return null;
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
        }
        catch (Exception ex)
        {
            Check(false, "测试「" + name + "」抛异常：" + ex.GetType().Name + ": " + ex.Message);
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
}
