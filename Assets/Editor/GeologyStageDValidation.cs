using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using VoxelWorld;
using VoxelWorld.Adapters;
using VoxelWorld.Generation;
using VoxelWorld.Generation.Geology;
using VoxelWorld.Mesh;
using VoxelWorld.Runtime;
using VoxelWorld.Runtime.Abstractions;

// 命名空间 VoxelWorld 内存在同名类型 VoxelWorld.VoxelWorld ⇒ 用别名消除解析歧义
using VoxelWorldData = VoxelWorld.VoxelWorld;

/// <summary>
/// Geology Stage D-1 验证（WorldGeneration_Geology_Runtime_Implementation.md §8）。
///
/// 用法：
///   · Editor 菜单：Tools/VoxelWorld/Validate Geology Stage D
///   · 批处理：-executeMethod GeologyStageDValidation.RunBatch
///
/// 覆盖 GW1–GW10：
///   GW1  Generate 确定性（同 seed 同 chunk 逐体素一致）
///   GW2  GIV9：地质全关 ⇒ 与 BasicTerrainGenerator 逐体素一致（+ 反例保护）
///   GW3  Chunk 顺序无关（Chunk 独立计算）
///   GW4  跨 Chunk 缝一致（坐标相邻 + 两侧由同一世界函数解释）
///   GW5  Deposit 不改变 Voxel（0 差异）+ provider 仍可查询资源
///   GW6  Biome 范围限制（差异 ⊆ 地表层且密度分布不变）
///   GW7  不生成 Entity / 植被（结构扫描 + 运行期 GameObject 计数）
///   GW8  Generate 失败不污染世界（结构性 + 输出契约 + Core 端到端）
///   GW9  Runtime 零修改 + 无感替换（SHA + 反射 + Core 闭环）
///   GW10 SHA 检查（既有 61 个文件）+ 既有入口存在性
/// </summary>
public static class GeologyStageDValidation
{
    private static int checks;
    private static int failures;

    private const string GeologyFolder = "Scripts/VoxelWorld/Generation/Geology";

    [MenuItem("Tools/VoxelWorld/Validate Geology Stage D")]
    public static void ValidateFromMenu()
    {
        ValidateAll();

        Debug.Log("[GeologyStageDValidation] Menu validation result = " +
                  (failures == 0 ? "PASS" : "FAIL"));
    }

    public static void RunBatch()
    {
        ValidateAll();

        Debug.Log(
            "[GeologyStageDValidation] ===== SUMMARY =====\n" +
            "Checks = " + checks + "\n" +
            "Failures = " + failures + "\n" +
            "Result = " + (failures == 0 ? "PASS" : "FAIL"));

        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    private static void ValidateAll()
    {
        checks = 0;
        failures = 0;

        Run("GW1 Generate 确定性", ValidateGenerateDeterminism);
        Run("GW2 GIV9 与 Stage 1 基线一致", ValidateGiv9);
        Run("GW3 Chunk 顺序无关", ValidateChunkOrderIndependence);
        Run("GW4 跨 Chunk 缝一致", ValidateSeamConsistency);
        Run("GW5 Deposit 不改变 Voxel", ValidateDepositDoesNotAffectVoxel);
        Run("GW6 Biome 范围限制", ValidateBiomeScope);
        Run("GW7 不生成 Entity / 植被", ValidateNoEntityGenerated);
        Run("GW8 Generate 失败不污染世界", ValidateFailureIsolation);
        Run("GW9 Runtime 零修改 + 无感替换", ValidateRuntimeReplacement);
        Run("GW10 SHA 检查 + 入口存在性", ValidateFrozenFiles);
    }

    // =====================================================================
    // GW1：Generate 确定性
    // =====================================================================

    private static void ValidateGenerateDeterminism()
    {
        GeologySettings geology = AllDisabled();
        GeologyWorldGenerator a = CreateGenerator(geology, null, null);
        GeologyWorldGenerator b = CreateGenerator(geology, null, null);

        VoxelCoordinate[] coordinates =
        {
            new VoxelCoordinate(0, 0, 0),
            new VoxelCoordinate(0, 1, 0),
            new VoxelCoordinate(-1, 0, 0),
            new VoxelCoordinate(-1, 1, -1),
            new VoxelCoordinate(2, 3, 2),
            new VoxelCoordinate(0, 3, 0)
        };

        int totalVoxels = 0;
        int mismatches = 0;
        string firstMismatch = null;

        for (int c = 0; c < coordinates.Length; c++)
        {
            VoxelChunk first = a.Generate(coordinates[c]);
            VoxelChunk second = a.Generate(coordinates[c]);          // 重复调用
            VoxelChunk other = b.Generate(coordinates[c]);           // 独立实例

            totalVoxels += VoxelChunk.VoxelCount;

            string difference = CompareChunks(first, second) ?? CompareChunks(first, other);
            if (difference != null)
            {
                mismatches++;
                if (firstMismatch == null) firstMismatch = difference;
            }

            // 派生哈希（Mesh 层只读派生，仅用于比较）
            ulong hashFirst = VoxelMeshGenerator.Generate(first).ComputeContentHash();
            ulong hashOther = VoxelMeshGenerator.Generate(other).ComputeContentHash();
            if (hashFirst != hashOther)
            {
                mismatches++;
                if (firstMismatch == null) firstMismatch = "内容哈希不一致";
            }

            if (first.Coordinate != coordinates[c] || !first.IsDirty)
            {
                mismatches++;
                if (firstMismatch == null) firstMismatch = "坐标/脏语义不正确";
            }
        }

        Check(mismatches == 0,
            "GW1：重复调用与独立实例逐体素一致（比较 " + totalVoxels + " 体素 + 哈希，不一致 " +
            mismatches + "）" + (firstMismatch == null ? "" : " 首次：" + firstMismatch));

        // 交错 + 逆序批量后，结果仍不变
        VoxelChunk[] forward = new VoxelChunk[coordinates.Length];
        for (int c = 0; c < coordinates.Length; c++) forward[c] = a.Generate(coordinates[c]);

        for (int i = 0; i < 16; i++) a.Generate(new VoxelCoordinate(100 + i, 1, -100 - i));

        int interleaveMismatch = 0;
        for (int c = coordinates.Length - 1; c >= 0; c--)
        {
            VoxelChunk again = a.Generate(coordinates[c]);
            if (CompareChunks(forward[c], again) != null) interleaveMismatch++;
        }

        Check(interleaveMismatch == 0,
            "GW1：交错/逆序生成后结果不变（不一致 " + interleaveMismatch + "）");

        // 反例保护：输出不是恒定空 Chunk
        int solidVoxels = CountSolid(forward[1]);
        Check(solidVoxels > 0,
            "GW1：输出非全空（chunk(0,1,0) 实体体素 " + solidVoxels + " ⇒ 断言非平凡）");
    }

    // =====================================================================
    // GW2：GIV9 —— 全关地质特性 ⇒ 与 BasicTerrainGenerator 逐体素一致
    // =====================================================================

    private static void ValidateGiv9()
    {
        WorldGenerationSettings stage1 = WorldGenerationSettings.Default;

        GeologySettings disabled = AllDisabled();
        GeologyWorldGenerator geology = CreateGenerator(disabled, null, null);
        BasicTerrainGenerator baseline = new BasicTerrainGenerator(stage1);

        VoxelCoordinate[] chunks =
        {
            new VoxelCoordinate(0, 0, 0),
            new VoxelCoordinate(0, 1, 0),
            new VoxelCoordinate(0, 2, 0),
            new VoxelCoordinate(0, 3, 0),
            new VoxelCoordinate(-1, 0, 0),
            new VoxelCoordinate(-1, 1, -1),
            new VoxelCoordinate(2, 3, 2),
            new VoxelCoordinate(1, 0, 1)
        };

        int totalVoxels = 0;
        int mismatches = 0;
        int solidVoxels = 0;
        int airVoxels = 0;
        string firstMismatch = null;

        for (int c = 0; c < chunks.Length; c++)
        {
            VoxelChunk mine = geology.Generate(chunks[c]);
            VoxelChunk theirs = baseline.Generate(chunks[c]);

            for (int y = 0; y < VoxelChunk.ChunkSize; y++)
            {
                for (int z = 0; z < VoxelChunk.ChunkSize; z++)
                {
                    for (int x = 0; x < VoxelChunk.ChunkSize; x++)
                    {
                        VoxelType expected = theirs.GetVoxel(x, y, z).Type;
                        VoxelType actual = mine.GetVoxel(x, y, z).Type;

                        totalVoxels++;
                        if (expected == VoxelType.Air) airVoxels++; else solidVoxels++;

                        if (actual != expected)
                        {
                            mismatches++;
                            if (firstMismatch == null)
                            {
                                VoxelCoordinate world = mine.LocalToWorldCoordinate(x, y, z);
                                firstMismatch = world + " 期望 " + expected + " 实际 " + actual;
                            }
                        }
                    }
                }
            }
        }

        Check(totalVoxels == chunks.Length * VoxelChunk.VoxelCount,
            "GW2：比较体素数 = " + chunks.Length + " × 4096（实际 " + totalVoxels + "）");
        Check(solidVoxels > 0 && airVoxels > 0,
            "GW2：样本同时含实体与空气（非平凡；实体 " + solidVoxels + " / 空气 " + airVoxels + "）");
        Check(mismatches == 0,
            "GW2/GIV9：地质全关 ⇒ 与 BasicTerrainGenerator 逐体素一致（不一致 " +
            mismatches + "）" + (firstMismatch == null ? "" : " 首次：" + firstMismatch));

        // 边界参数：SoilDepth = 0 也必须一致
        WorldGenerationSettings noSoil = stage1;
        noSoil.SoilDepth = 0;

        GeologyWorldGenerator noSoilGeology = CreateGenerator(AllDisabled(), null, null, noSoil);
        BasicTerrainGenerator noSoilBaseline = new BasicTerrainGenerator(noSoil);

        int noSoilMismatch = 0;
        VoxelChunk noSoilMine = noSoilGeology.Generate(new VoxelCoordinate(0, 0, 0));
        VoxelChunk noSoilTheirs = noSoilBaseline.Generate(new VoxelCoordinate(0, 0, 0));
        if (CompareChunks(noSoilMine, noSoilTheirs) != null) noSoilMismatch++;

        Check(noSoilMismatch == 0,
            "GW2：SoilDepth=0 边界同样与基线一致（不一致 " + noSoilMismatch + "）");

        // 反例保护：开启洞穴必须产生差异，且差异只允许 Solid → Empty
        GeologySettings withCaves = AllDisabled();
        withCaves.EnableCaves = true;
        withCaves.CaveThreshold = 0f;

        GeologyWorldGenerator caveGenerator = CreateGenerator(withCaves, null, null);
        int solidToAir = 0;
        int illegalChanges = 0;

        for (int c = 0; c < chunks.Length; c++)
        {
            VoxelChunk mine = caveGenerator.Generate(chunks[c]);
            VoxelChunk theirs = baseline.Generate(chunks[c]);

            for (int y = 0; y < VoxelChunk.ChunkSize; y++)
            {
                for (int z = 0; z < VoxelChunk.ChunkSize; z++)
                {
                    for (int x = 0; x < VoxelChunk.ChunkSize; x++)
                    {
                        VoxelType expected = theirs.GetVoxel(x, y, z).Type;
                        VoxelType actual = mine.GetVoxel(x, y, z).Type;
                        if (actual == expected) continue;

                        if (expected != VoxelType.Air && actual == VoxelType.Air) solidToAir++;
                        else illegalChanges++;
                    }
                }
            }
        }

        Check(solidToAir > 0,
            "GW2：启用洞穴确实产生差异（GIV9 非恒真；Solid→Empty " + solidToAir + " 处）");
        Check(illegalChanges == 0,
            "GW2：洞穴只允许 Solid→Empty（非法变化 " + illegalChanges + "）");
    }

    // =====================================================================
    // GW3：Chunk 顺序无关（Chunk 独立计算）
    // =====================================================================

    private static void ValidateChunkOrderIndependence()
    {
        GeologySettings geology = GeologySettings.Default;
        geology.CaveThreshold = -0.5f;                     // 洞穴部分生效（更强的非平凡性）
        geology.EnableDeposits = true;
        geology.DepositRichness = 60;
        geology.EnableBiomeModifiers = true;

        WorldGenerationSettings stage1 = WorldGenerationSettings.Default;
        GeologySampler sampler = new GeologySampler(stage1, geology);
        GeologyWorldGenerator generator = CreateGenerator(
            geology,
            new GeologyBiomeProvider(sampler),
            new GeologyDepositProvider(sampler));

        VoxelCoordinate[] order =
        {
            new VoxelCoordinate(0, 1, 0),
            new VoxelCoordinate(1, 1, 0),
            new VoxelCoordinate(-1, 1, 0),
            new VoxelCoordinate(0, 1, 1),
            new VoxelCoordinate(0, 1, -1)
        };

        VoxelChunk[] baseline = new VoxelChunk[order.Length];
        for (int i = 0; i < order.Length; i++) baseline[i] = generator.Generate(order[i]);

        // 以相反顺序 + 交错重复的方式重生成
        int mismatches = 0;
        for (int round = 0; round < 3; round++)
        {
            for (int i = order.Length - 1; i >= 0; i--)
            {
                VoxelChunk again = generator.Generate(order[i]);
                if (CompareChunks(baseline[i], again) != null) mismatches++;
            }

            // 交错生成"其它坐标"，确认不污染已生成结果
            for (int i = 0; i < 8; i++) generator.Generate(new VoxelCoordinate(50 + i, 1, 50 + i));
        }

        Check(mismatches == 0,
            "GW3：任意顺序/交错生成 ⇒ 每个 Chunk 结果不变（不一致 " + mismatches + "）");

        // 独立实例在"其它 Chunk 已生成"的环境下仍得同结果（无隐藏状态）
        GeologyWorldGenerator fresh = CreateGenerator(
            geology,
            new GeologyBiomeProvider(new GeologySampler(stage1, geology)),
            new GeologyDepositProvider(new GeologySampler(stage1, geology)));

        int crossInstanceMismatch = 0;
        for (int i = 0; i < order.Length; i++)
        {
            if (CompareChunks(baseline[i], fresh.Generate(order[i])) != null) crossInstanceMismatch++;
        }

        Check(crossInstanceMismatch == 0,
            "GW3：独立实例结果一致（不一致 " + crossInstanceMismatch + "）");

        Debug.Log("[GeologyStageDValidation] GW3 样例（chunk(0,1,0) 实体体素 " +
                  CountSolid(baseline[0]) + "）：" +
                  sampler.ExplainAt(0, 24, 0, stage1.Seed).Replace("\n", " | "));
    }

    // =====================================================================
    // GW4：跨 Chunk 缝一致
    // =====================================================================

    private static void ValidateSeamConsistency()
    {
        WorldGenerationSettings stage1 = WorldGenerationSettings.Default;
        GeologySettings disabled = AllDisabled();

        GeologyWorldGenerator generator = CreateGenerator(disabled, null, null);
        GeologySampler sampler = new GeologySampler(stage1, disabled);

        int[] seamLeft = { 15, -1, 31, -33, 47 };

        int coordinateBreaks = 0;
        int samplerMismatch = 0;
        int compared = 0;
        string firstMismatch = null;

        for (int i = 0; i < seamLeft.Length; i++)
        {
            int leftX = seamLeft[i];
            int rightX = leftX + 1;

            VoxelCoordinate leftChunkCoordinate =
                VoxelWorldData.WorldToChunkCoordinate(new VoxelCoordinate(leftX, 0, 0));
            VoxelCoordinate rightChunkCoordinate =
                VoxelWorldData.WorldToChunkCoordinate(new VoxelCoordinate(rightX, 0, 0));

            // 只处理真正的缝（左列是所在 Chunk 最后一列、右列是下一 Chunk 第一列）
            if (leftX - leftChunkCoordinate.x * VoxelChunk.ChunkSize != VoxelChunk.ChunkSize - 1)
            {
                continue;
            }

            VoxelChunk left = generator.Generate(leftChunkCoordinate);
            VoxelChunk right = generator.Generate(rightChunkCoordinate);

            for (int y = 0; y < VoxelChunk.ChunkSize; y++)
            {
                for (int z = 0; z < VoxelChunk.ChunkSize; z++)
                {
                    VoxelCoordinate worldLeft =
                        left.LocalToWorldCoordinate(VoxelChunk.ChunkSize - 1, y, z);
                    VoxelCoordinate worldRight = right.LocalToWorldCoordinate(0, y, z);

                    compared++;

                    // 缝处坐标必须严格相邻（无缝隙、无重叠）
                    if (worldRight.x != worldLeft.x + 1 ||
                        worldRight.y != worldLeft.y ||
                        worldRight.z != worldLeft.z)
                    {
                        coordinateBreaks++;
                    }

                    // 两侧体素都必须能由同一个世界函数解释（Chunk 独立性）
                    GeologySample sampleLeft =
                        sampler.Sample(worldLeft.x, worldLeft.y, worldLeft.z, stage1.Seed);
                    GeologySample sampleRight =
                        sampler.Sample(worldRight.x, worldRight.y, worldRight.z, stage1.Seed);

                    VoxelType expectedLeft = sampleLeft.IsSolid ? sampleLeft.Material : VoxelType.Air;
                    VoxelType expectedRight = sampleRight.IsSolid ? sampleRight.Material : VoxelType.Air;

                    VoxelType actualLeft = left.GetVoxel(VoxelChunk.ChunkSize - 1, y, z).Type;
                    VoxelType actualRight = right.GetVoxel(0, y, z).Type;

                    if (actualLeft != expectedLeft || actualRight != expectedRight)
                    {
                        samplerMismatch++;
                        if (firstMismatch == null)
                        {
                            firstMismatch = worldLeft + " 生成 " + actualLeft + " 采样 " + expectedLeft;
                        }
                    }
                }
            }
        }

        Check(compared > 0, "GW4：缝体素比较覆盖充分（" + compared + " 个体素）");
        Check(coordinateBreaks == 0,
            "GW4：缝处世界坐标严格相邻（无缝隙/无重叠；违反 " + coordinateBreaks + "）");
        Check(samplerMismatch == 0,
            "GW4：缝两侧体素都由同一世界函数解释（不一致 " + samplerMismatch + "）" +
            (firstMismatch == null ? "" : " 首次：" + firstMismatch));

        // 洞穴噪声跨缝连续（相邻体素差分 ≤ 显式上界）
        GeologySettings caves = AllDisabled();
        caves.EnableCaves = true;
        GeologySampler caveSampler = new GeologySampler(stage1, caves);

        float bound = GeologyNoise.MaxAdjacentDelta3D(caves.CaveCellVoxels) + 1e-5f;
        float worst = 0f;
        int exceeded = 0;

        for (int x = -8; x < 8; x++)
        {
            for (int y = -2; y < 66; y++)
            {
                float delta = Math.Abs(caveSampler.CaveDensityAt(x + 1, y, 0, stage1.Seed) -
                                       caveSampler.CaveDensityAt(x, y, 0, stage1.Seed));
                if (delta > worst) worst = delta;
                if (delta > bound) exceeded++;
            }
        }

        Check(exceeded == 0 && worst <= bound,
            "GW4：洞穴噪声相邻连续（最大差分 " + worst.ToString("F6") + " ≤ 上界 " +
            bound.ToString("F6") + "，越界 " + exceeded + "）");
    }

    // =====================================================================
    // GW5：Deposit 不改变 Voxel
    // =====================================================================

    private static void ValidateDepositDoesNotAffectVoxel()
    {
        WorldGenerationSettings stage1 = WorldGenerationSettings.Default;
        VoxelCoordinate coordinate = new VoxelCoordinate(0, 1, 0);

        // 四套设置：关闭 / 开启低丰度 / 开启满丰度 / 换成不同频率
        GeologySettings off = AllDisabled();
        off.EnableDeposits = false;

        GeologySettings onLow = AllDisabled();
        onLow.EnableDeposits = true;
        onLow.DepositRichness = 1;
        onLow.DepositFrequency = 1;

        GeologySettings onFull = AllDisabled();
        onFull.EnableDeposits = true;
        onFull.DepositRichness = 100;
        onFull.DepositFrequency = 64;

        GeologySettings onOtherSeed = AllDisabled();
        onOtherSeed.EnableDeposits = true;
        onOtherSeed.DepositRichness = 50;

        GeologyWorldGenerator[] generators =
        {
            CreateGenerator(off, null, null),
            CreateGenerator(onLow, null, new GeologyDepositProvider(new GeologySampler(stage1, onLow))),
            CreateGenerator(onFull, null, new GeologyDepositProvider(new GeologySampler(stage1, onFull))),
            CreateGenerator(onOtherSeed, null, new GeologyDepositProvider(new GeologySampler(stage1, onOtherSeed)))
        };

        VoxelChunk reference = generators[0].Generate(coordinate);
        int mismatches = 0;
        string firstMismatch = null;

        for (int i = 1; i < generators.Length; i++)
        {
            VoxelChunk candidate = generators[i].Generate(coordinate);
            string difference = CompareChunks(reference, candidate);

            if (difference != null)
            {
                mismatches++;
                if (firstMismatch == null) firstMismatch = "设置#" + i + "：" + difference;
            }
        }

        Check(mismatches == 0,
            "GW5：仅改变 Deposit 设置/provider ⇒ 输出逐体素完全一致（不一致 " + mismatches + "）" +
            (firstMismatch == null ? "" : " 首次：" + firstMismatch));

        // 非恒真：provider 确实能查询到资源
        GeologyDepositProvider provider =
            new GeologyDepositProvider(new GeologySampler(stage1, onFull));

        int hits = 0;
        for (int x = 0; x < 16; x++)
        {
            for (int y = 0; y < 16; y++)
            {
                for (int z = 0; z < 16; z++)
                {
                    VoxelCoordinate world = reference.LocalToWorldCoordinate(x, y, z);
                    if (provider.TryGetDeposit(world.x, world.y, world.z, stage1.Seed).Exists) hits++;
                }
            }
        }

        Check(hits > 0,
            "GW5：满丰度下确实存在资源（命中 " + hits + " / 4096 ⇒ 断言非恒真）");

        // 生成器即使持有 provider 也不读取其数据生成体素：禁用/启用输出一致已证明（上式）
        Check(generators[2].Deposits != null && generators[1].Deposits != null,
            "GW5：生成器按契约持有（不读取）Deposit provider（只读暴露）");
    }

    // =====================================================================
    // GW6：Biome 范围限制
    // =====================================================================

    private static void ValidateBiomeScope()
    {
        WorldGenerationSettings stage1 = WorldGenerationSettings.Default;

        GeologySettings noBiome = AllDisabled();
        GeologySettings withBiome = AllDisabled();
        withBiome.EnableBiomeModifiers = true;

        GeologyWorldGenerator plain = CreateGenerator(noBiome, null, null);
        GeologyWorldGenerator biome = CreateGenerator(
            withBiome, new GeologyBiomeProvider(new GeologySampler(stage1, withBiome)), null);

        VoxelCoordinate[] chunks =
        {
            new VoxelCoordinate(0, 0, 0),
            new VoxelCoordinate(0, 1, 0),
            new VoxelCoordinate(1, 0, 1),
            new VoxelCoordinate(-1, 1, 0)
        };

        GeologySampler sampler = new GeologySampler(stage1, withBiome);

        int differences = 0;
        int outsideSurfaceLayer = 0;
        int densityChanges = 0;
        int illegalMaterialChanges = 0;
        int checkedVoxels = 0;

        for (int c = 0; c < chunks.Length; c++)
        {
            VoxelChunk a = plain.Generate(chunks[c]);
            VoxelChunk b = biome.Generate(chunks[c]);

            for (int y = 0; y < VoxelChunk.ChunkSize; y++)
            {
                for (int z = 0; z < VoxelChunk.ChunkSize; z++)
                {
                    for (int x = 0; x < VoxelChunk.ChunkSize; x++)
                    {
                        VoxelType typeA = a.GetVoxel(x, y, z).Type;
                        VoxelType typeB = b.GetVoxel(x, y, z).Type;

                        checkedVoxels++;

                        // 密度分布必须完全一致（实体/空的分界不变）
                        if ((typeA == VoxelType.Air) != (typeB == VoxelType.Air)) densityChanges++;

                        if (typeA == typeB) continue;

                        differences++;

                        VoxelCoordinate world = a.LocalToWorldCoordinate(x, y, z);
                        GeologySample sample =
                            sampler.Sample(world.x, world.y, world.z, stage1.Seed);

                        // 差异只允许出现在地表层（Rule == Surface，即 y == SurfaceHeight）
                        if (sample.Rule != GeologyRule.Surface) outsideSurfaceLayer++;

                        // 差异只允许 Grass ↔ Stone
                        bool legal = (typeA == VoxelType.Grass && typeB == VoxelType.Stone) ||
                                     (typeA == VoxelType.Stone && typeB == VoxelType.Grass);
                        if (!legal) illegalMaterialChanges++;
                    }
                }
            }
        }

        Check(checkedVoxels == chunks.Length * VoxelChunk.VoxelCount,
            "GW6：比较体素数 = " + checkedVoxels);
        Check(densityChanges == 0,
            "GW6：Biome 不改变密度分布（实体/空分界；违反 " + densityChanges + "）");
        Check(outsideSurfaceLayer == 0,
            "GW6：差异只出现在地表层（越界 " + outsideSurfaceLayer + "）");
        Check(illegalMaterialChanges == 0,
            "GW6：差异只允许 Grass ↔ Stone（非法变化 " + illegalMaterialChanges + "）");
        Check(differences > 0,
            "GW6：Biome 确实生效（差异 " + differences + " 处 ⇒ 断言非恒真）");

        Debug.Log("[GeologyStageDValidation] GW6：地表材料差异 " + differences +
                  " 处（只在地表层，密度不变）");
    }

    // =====================================================================
    // GW7：不生成 Entity / 植被
    // =====================================================================

    private static readonly string[] GeneratorForbiddenPatterns =
    {
        // Entity / 植被 / 实例化
        "CrystalCluster", @"\bEntity\b", @"\bSpawn\b", "Prefab", "Instantiate",
        "GameObject", "MonoBehaviour", "Transform", @"\bTree\b", @"\bPlant\b",
        @"\bAnimal\b", "Vegetation",
        // Unity / 几何
        "UnityEngine", "UnityEditor", "VoxelMeshGenerator", "VoxelMeshData",
        // Runtime / 世界写入
        "VoxelRuntimeCore", "ChunkStreamingDriver", "ChunkRecord", "IChunkPresenter", "IChunkMesher",
        @"new VoxelWorld\b", @"VoxelWorld\.(?!Generation)", "GetOrCreateChunk", "RemoveChunk",
        "MarkDirty", "ClearDirty",
        // 旧实现（禁止复制/调用判定链）
        "BasicTerrainGenerator", "ClassifyVoxel",
        // 非确定性
        @"\bRandom\b", @"Time\.", "DateTime", @"\bGuid\b", @"\bMathf\b"
    };

    private static void ValidateNoEntityGenerated()
    {
        string path = Path.Combine(Application.dataPath, GeologyFolder, "GeologyWorldGenerator.cs");
        Check(File.Exists(path), "GW7：存在 GeologyWorldGenerator.cs");
        if (!File.Exists(path)) return;

        string code = StripComments(File.ReadAllText(path, Encoding.UTF8));

        // 扫描器自检
        string probe = "void P(){ var x = new GameObject(); } // GameObject 在注释里不计\n";
        Check(ScanTokens(StripComments(probe), GeneratorForbiddenPatterns).Count == 1,
            "GW7：扫描器自检（代码区命中 1）");

        List<string> found = ScanTokens(code, GeneratorForbiddenPatterns);
        if (found.Count > 0)
        {
            Check(false, "GW7：GeologyWorldGenerator.cs 命中禁止项：" +
                         string.Join(", ", found.ToArray()));
        }

        Check(found.Count == 0,
            "GW7：生成器不生成 Entity/植被/Unity 对象，也不含世界写入与旧实现依赖（命中 " +
            found.Count + "）");

        // 静态可变字段
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
            Check(false, "GW7：存在静态可变字段候选：L" + (l + 1) + " " + line);
        }

        Check(mutableStatic == 0, "GW7：无静态可变字段（命中 " + mutableStatic + "）");

        // 正向对照：必须调用 GeologySampler、必须用 LocalToWorldCoordinate、必须有固定链路
        Check(code.Contains("sampler.Sample("),
            "GW7：判定链委托给 GeologySampler（正向对照）");
        Check(code.Contains("LocalToWorldCoordinate"),
            "GW7：世界坐标经 VoxelCore 换算（正向对照）");
        Check(code.Contains("GenerateVoxelData") && code.Contains("Validate"),
            "GW7：存在固定的 Generate → GenerateVoxelData → Validate 链路（正向对照）");
        Check(code.Contains("IWorldGenerator"),
            "GW7：实现 IWorldGenerator（正向对照）");

        // 运行期：生成 64 个 Chunk 不产生任何场景对象
        GeologyWorldGenerator generator = CreateGenerator(AllDisabled(), null, null);

        GameObject probeHost = new GameObject("GeologyStageDEntityProbe");

        try
        {
            int before = UnityEngine.Object.FindObjectsOfType<GameObject>().Length;
            for (int i = 0; i < 64; i++) generator.Generate(new VoxelCoordinate(i, 1, -i));
            int after = UnityEngine.Object.FindObjectsOfType<GameObject>().Length;

            Check(after == before,
                "GW7：生成 64 个 Chunk 不产生任何 GameObject（前 " + before + " → 后 " +
                after + "）");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(probeHost);
        }
    }

    // =====================================================================
    // GW8：Generate 失败不污染世界
    // =====================================================================

    private static void ValidateFailureIsolation()
    {
        string path = Path.Combine(Application.dataPath, GeologyFolder, "GeologyWorldGenerator.cs");
        if (!File.Exists(path))
        {
            Check(false, "GW8：缺少 GeologyWorldGenerator.cs");
            return;
        }

        string code = StripComments(File.ReadAllText(path, Encoding.UTF8));

        // 1) 结构性：生成器没有任何世界引用/世界写入路径 ⇒ 失败不可能污染世界
        //    （namespace VoxelWorld.Generation.Geology 不算"访问世界"，故用精确模式）
        bool holdsWorld = Regex.IsMatch(code, @"new VoxelWorld\b") ||
                          Regex.IsMatch(code, @"VoxelWorld\.(?!Generation)") ||
                          code.Contains("GetOrCreateChunk") ||
                          code.Contains("RemoveChunk") ||
                          code.Contains("world.Clear");

        Check(!holdsWorld,
            "GW8：生成器不持有/不访问世界（无写入路径 ⇒ 失败不可能污染世界）");

        // 2) 输出契约：多坐标 × 全量类型合法 + 坐标自洽 + 脏语义
        GeologyWorldGenerator generator = CreateGenerator(GeologySettings.Default, null, null);

        VoxelCoordinate[] coordinates =
        {
            new VoxelCoordinate(0, 0, 0),
            new VoxelCoordinate(0, 3, 0),
            new VoxelCoordinate(-2, 1, -2),
            new VoxelCoordinate(3, 2, 3)
        };

        int contractViolations = 0;

        for (int c = 0; c < coordinates.Length; c++)
        {
            VoxelChunk chunk = generator.Generate(coordinates[c]);

            if (chunk.Coordinate != coordinates[c] || !chunk.IsDirty) contractViolations++;

            for (int y = 0; y < VoxelChunk.ChunkSize; y++)
            {
                for (int z = 0; z < VoxelChunk.ChunkSize; z++)
                {
                    for (int x = 0; x < VoxelChunk.ChunkSize; x++)
                    {
                        int raw = (int)chunk.GetVoxel(x, y, z).Type;
                        if (raw < (int)VoxelType.Air || raw > (int)VoxelType.Stone) contractViolations++;
                    }
                }
            }
        }

        Check(contractViolations == 0,
            "GW8：输出契约满足（坐标自洽 + 4 种体素状态 + 初始脏；违反 " + contractViolations + "）");

        // 3) 非法参数 ⇒ 构造期拒绝（不产生任何对象、不触碰世界）
        int rejected = 0;

        GeologySettings invalid = GeologySettings.Default;
        invalid.CaveFrequency = 3;                       // 非法频率

        try
        {
            CreateGenerator(invalid, null, null);
        }
        catch (ArgumentException)
        {
            rejected++;
        }

        GeologySettings needsBiome = GeologySettings.Default;
        needsBiome.EnableBiomeModifiers = true;          // 声明启用群系但没有 provider

        try
        {
            CreateGenerator(needsBiome, null, null);
        }
        catch (ArgumentException)
        {
            rejected++;
        }

        Check(rejected == 2,
            "GW8：非法参数/缺失 provider 在构造期被拒绝（拒绝 " + rejected + " / 2）");

        // 4) 端到端：用新生成器跑一次 Runtime Core 闭环（失败与成功的完整路径由 Core 管理）
        VoxelWorldData world = new VoxelWorldData();
        CountingPresenter presenter = new CountingPresenter();
        RuntimeCoreSettings coreSettings = new RuntimeCoreSettings
        {
            VerticalChunkCount = 4,
            MaxLoadsPerFrame = 4,
            MaxChunkPairsPerFrame = 4,
            MaxUnloadsPerFrame = 4,
            MaxRetriesPerChunk = 3
        };

        VoxelRuntimeCore core = new VoxelRuntimeCore(
            world, generator, new VoxelMeshGeneratorMesher(), presenter, coreSettings);

        VoxelCoordinate target = new VoxelCoordinate(0, 1, 0);
        core.RequestChunk(target);
        RuntimeCoreTickReport report = core.Tick();

        ChunkRecordSnapshot snapshot;
        core.TryGetSnapshot(target, out snapshot);

        Check(snapshot != null && snapshot.State == ChunkLifecycleState.Ready,
            "GW8：新生成器可直接被 Runtime Core 使用并到达 Ready（" + snapshot + "）");
        Check(world.ChunkCount == 1 && report.InvariantViolationCount == 0,
            "GW8：Core 闭环无违规（" + report + "）");

        // Core 侧的"生成失败 ⇒ 世界零污染 + Failed + 可重试"由既有 RuntimeCore 入口覆盖（196 项）
        Debug.Log("[GeologyStageDValidation] GW8：生成失败语义由 RuntimeCore 既有验证覆盖" +
                  "（VoxelWorldRuntimeCoreValidation 196 项，其中 RCI1/RC15/RC16 覆盖失败路径）");
    }

    // =====================================================================
    // GW9：Runtime 零修改 + 无感替换
    // =====================================================================

    private static void ValidateRuntimeReplacement()
    {
        // 反射：实现 IWorldGenerator、契约签名正确
        Type type = typeof(GeologyWorldGenerator);
        Check(typeof(IWorldGenerator).IsAssignableFrom(type),
            "GW9：GeologyWorldGenerator 实现 IWorldGenerator");

        PropertyInfo settingsProperty = type.GetProperty("Settings");
        Check(settingsProperty != null &&
              settingsProperty.PropertyType == typeof(WorldGenerationSettings),
            "GW9：Settings 属性类型 == WorldGenerationSettings（契约不变）");

        MethodInfo generate = type.GetMethod("Generate", new Type[] { typeof(VoxelCoordinate) });
        Check(generate != null && generate.ReturnType == typeof(VoxelChunk),
            "GW9：Generate(VoxelCoordinate) → VoxelChunk（与既有实现一致）");

        MethodInfo validate = type.GetMethod(
            "Validate", BindingFlags.NonPublic | BindingFlags.Instance);
        Check(validate != null,
            "GW9：存在内部 Validate（固定的 Generate → GenerateVoxelData → Validate 链路）");

        Debug.Log("[GeologyStageDValidation] GW9：Runtime / Streaming / Mesh / View / Bootstrap / " +
                  "IWorldGenerator 的 SHA 由 GW10 表核对（无感替换已在 GW8 端到端验证）");
    }

    // =====================================================================
    // GW10：SHA 检查（既有 61 个文件）+ 入口存在性
    // =====================================================================

    private static readonly Dictionary<string, string> FrozenFiles =
        new Dictionary<string, string>
        {
            // 规范（13 份）+ ADR
            { "../Specifications/World/VoxelWorld_Architecture.md", "F09B17A1002D7CD2" },
            { "../Specifications/World/VoxelWorld_Core.md", "6D471FFA184B13EC" },
            { "../Specifications/World/WorldGeneration_Core.md", "91F0FCDA380C91F7" },
            { "../Specifications/World/VoxelWorld_Mesh.md", "81CEDBFA6E9756CE" },
            { "../Specifications/World/VoxelWorld_Mining.md", "8A2B020DDB97422B" },
            { "../Specifications/World/VoxelWorld_View.md", "D9BD5422026E21B6" },
            { "../Specifications/World/VoxelWorld_Runtime.md", "EDD02AC50FC03E5E" },
            { "../Specifications/World/VoxelWorld_Runtime_Core.md", "10F1EC7F10DAD8F3" },
            { "../Specifications/World/VoxelWorld_Runtime_Interface.md", "0AE21EB684412685" },
            { "../Specifications/World/VoxelWorld_Runtime_Core_Implementation.md", "D816F09C60A810C4" },
            { "../Specifications/World/WorldGeneration_Geology.md", "D1E70FE71BC400E1" },
            { "../Specifications/World/WorldGeneration_Geology_Implementation.md", "2700AE40B0CB09A0" },
            { "../Specifications/World/WorldGeneration_Geology_Runtime_Implementation.md", "373A8BF712D73C3B" },
            { "../Specifications/ArchitectureDecisionWorldSpaceEntity.txt", "218D1CAAA738C59D" },
            // VoxelCore / Generation / Mesh / View（含 BasicTerrainGenerator 与 IWorldGenerator）
            { "Scripts/VoxelWorld/VoxelType.cs", "02C1F54580AEE541" },
            { "Scripts/VoxelWorld/Voxel.cs", "DC52145E6E82132D" },
            { "Scripts/VoxelWorld/VoxelCoordinate.cs", "F327B2761DFDCFC2" },
            { "Scripts/VoxelWorld/VoxelChunk.cs", "4DBDFBAC5BCE500C" },
            { "Scripts/VoxelWorld/VoxelWorld.cs", "D9E749A466635918" },
            { "Scripts/VoxelWorld/VoxelWorldDebug.cs", "6AE0C41274AF9154" },
            // SeaLevel 迁移（第十轮受信同步：新增 SeaLevel 字段 / DefaultSeaLevel 常量 /
            // 七参数构造 / IsValid 范围校验 / ToString 输出；diff 已逐行审查，+46-0，无无关改动）
            { "Scripts/VoxelWorld/Generation/WorldGenerationSettings.cs", "7F11DEE8061E4FA1" },
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
            // SeaLevel 迁移（第十轮受信同步：新增 public int SeaLevel = 64 配置字段 +
            // 构造改为七参数显式传入 SeaLevel；diff 已逐行审查，+5-1，无无关改动）
            { "Scripts/Game/VoxelWorldStreamingBootstrap.cs", "8A59289F0194D20F" },
            // 既有 Editor 验证入口
            { "Editor/VoxelWorldCoreValidation.cs", "6DE813AABCE95394" },
            { "Editor/WorldGenerationCoreValidation.cs", "4A517A0F902A1066" },
            { "Editor/VoxelMeshCoreValidation.cs", "5FFD8451D2CE05EB" },
            { "Editor/VoxelWorldViewValidation.cs", "BEFBAAF6B111A241" },
            { "Editor/VoxelWorldRuntimeCoreValidation.cs", "C5ABCECD2C338BED" },
            // 第十一轮 CS12 登记重钉 + 规范哈希重钉（第十三轮 GW10 受信同步：仅登记行与注释变化，
            // 无断言改动；diff +5-3 已独立逐行审查，实际哈希与第十二轮 GE9 登记值一致）
            { "Editor/ChunkStreamingValidation.cs", "BEB05E3DD16D48F3" },
            // Stage A/B/C 验证入口：经用户明确授权修改（GSA5 扫描作用域拆分 + 白名单重钉）
            { "Editor/GeologyStageAValidation.cs", "67DB0CD6161EE4F4" },
            { "Editor/GeologyStageBValidation.cs", "58DA2160E30ABA72" },
            { "Editor/GeologyStageCValidation.cs", "F52E2D1A4B515581" },
            // Stage A/B/C 的地质实现
            { "Scripts/VoxelWorld/Generation/Geology/GeologySettings.cs", "BEA8EB4F4967721C" },
            { "Scripts/VoxelWorld/Generation/Geology/GeologyNoise.cs", "2266E7A880E43315" },
            { "Scripts/VoxelWorld/Generation/Geology/GeologySample.cs", "4946B90F3E56262E" },
            { "Scripts/VoxelWorld/Generation/Geology/GeologySampler.cs", "62836145F8F3E212" },
            { "Scripts/VoxelWorld/Generation/Geology/IDepositProvider.cs", "6F25E54FE64F7935" },
            { "Scripts/VoxelWorld/Generation/Geology/IBiomeProvider.cs", "6EF3344B319436B7" },
            { "Scripts/VoxelWorld/Generation/Geology/GeologyDepositProvider.cs", "7B1B8CF998402D3A" },
            { "Scripts/VoxelWorld/Generation/Geology/GeologyBiomeProvider.cs", "CC537685742367D9" }
        };

    private static void ValidateFrozenFiles()
    {
        int compared = 0;
        int missing = 0;
        int mismatch = 0;
        int skipped = 0;
        string specificationRoot = FindSpecificationsRoot();

        foreach (KeyValuePair<string, string> pair in FrozenFiles)
        {
            string path;

            if (pair.Key.StartsWith("../Specifications/", StringComparison.Ordinal))
            {
                if (specificationRoot == null) { skipped++; continue; }
                path = Path.Combine(
                    specificationRoot, pair.Key.Substring(3).Replace('/', Path.DirectorySeparatorChar));
            }
            else
            {
                path = Path.Combine(Application.dataPath, pair.Key);
            }

            if (!File.Exists(path))
            {
                missing++;
                Check(false, "GW10：缺失 " + pair.Key);
                continue;
            }

            string hash = ComputeFileHash16(path);
            if (hash == null) continue;

            compared++;
            if (hash != pair.Value)
            {
                mismatch++;
                Check(false, "GW10：" + pair.Key + " 已改变（" + hash + " != " + pair.Value + "）");
            }
        }

        Check(missing == 0, "GW10：既有文件全部存在（缺失 " + missing + "）");
        Check(mismatch == 0,
            "GW10：既有文件 SHA256(前16) 全部不变（比对 " + compared +
            " 个：BasicTerrainGenerator / IWorldGenerator / VoxelCore / Mesh / View / " +
            "Runtime / Streaming / Bootstrap / Stage A-C；跳过 " + skipped + "）");

        // 入口存在性（既有 8 个 + 本入口）
        string[] entryFiles =
        {
            "Editor/VoxelWorldCoreValidation.cs",
            "Editor/WorldGenerationCoreValidation.cs",
            "Editor/VoxelMeshCoreValidation.cs",
            "Editor/VoxelWorldViewValidation.cs",
            "Editor/VoxelWorldRuntimeCoreValidation.cs",
            "Editor/ChunkStreamingValidation.cs",
            "Editor/GeologyStageAValidation.cs",
            "Editor/GeologyStageBValidation.cs",
            "Editor/GeologyStageCValidation.cs",
            "Editor/GeologyStageDValidation.cs"
        };

        int missingEntries = 0;
        for (int i = 0; i < entryFiles.Length; i++)
        {
            if (!File.Exists(Path.Combine(Application.dataPath, entryFiles[i]))) missingEntries++;
        }

        Check(missingEntries == 0,
            "GW10：" + entryFiles.Length + " 个验证入口文件都存在（缺失 " + missingEntries + "）");
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
            Check(false, "GW10：无法计算哈希（" + path + "）：" + ex.Message);
            return null;
        }
    }

    // =====================================================================
    // 测试辅助
    // =====================================================================

    private sealed class CountingPresenter : IChunkPresenter
    {
        private readonly HashSet<VoxelCoordinate> live = new HashSet<VoxelCoordinate>();

        public int LiveCount { get { return live.Count; } }

        public PresenterOutcome Apply(
            VoxelCoordinate coordinate, VoxelMeshData meshData, out string error)
        {
            live.Add(coordinate);
            error = null;

            if (meshData == null || !meshData.HasGeometry) return PresenterOutcome.Empty;
            return PresenterOutcome.Presented;
        }

        public void Release(VoxelCoordinate coordinate)
        {
            live.Remove(coordinate);
        }
    }

    private static GeologySettings AllDisabled()
    {
        GeologySettings settings = GeologySettings.Default;
        settings.EnableCaves = false;
        settings.EnableDeposits = false;
        settings.EnableBiomeModifiers = false;
        return settings;
    }

    private static GeologyWorldGenerator CreateGenerator(
        GeologySettings geology, IBiomeProvider biomes, IDepositProvider deposits)
    {
        return CreateGenerator(geology, biomes, deposits, WorldGenerationSettings.Default);
    }

    private static GeologyWorldGenerator CreateGenerator(
        GeologySettings geology, IBiomeProvider biomes, IDepositProvider deposits,
        WorldGenerationSettings stage1)
    {
        return new GeologyWorldGenerator(stage1, geology, biomes, deposits);
    }

    private static string CompareChunks(VoxelChunk a, VoxelChunk b)
    {
        if (a == null || b == null) return "chunk 为 null";
        if (a.Coordinate != b.Coordinate) return "坐标 " + a.Coordinate + " != " + b.Coordinate;

        for (int x = 0; x < VoxelChunk.ChunkSize; x++)
        {
            for (int y = 0; y < VoxelChunk.ChunkSize; y++)
            {
                for (int z = 0; z < VoxelChunk.ChunkSize; z++)
                {
                    VoxelType ta = a.GetVoxel(x, y, z).Type;
                    VoxelType tb = b.GetVoxel(x, y, z).Type;
                    if (ta != tb) return "(" + x + "," + y + "," + z + ") " + ta + " != " + tb;
                }
            }
        }

        return null;
    }

    private static int CountSolid(VoxelChunk chunk)
    {
        int count = 0;

        for (int x = 0; x < VoxelChunk.ChunkSize; x++)
        {
            for (int y = 0; y < VoxelChunk.ChunkSize; y++)
            {
                for (int z = 0; z < VoxelChunk.ChunkSize; z++)
                {
                    if (chunk.GetVoxel(x, y, z).IsSolid()) count++;
                }
            }
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
