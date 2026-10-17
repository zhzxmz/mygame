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
using VoxelWorld.Generation;
using VoxelWorld.Generation.Geology;

/// <summary>
/// Geology Stage C 验证（WorldGeneration_Geology_Implementation.md §13.1 Stage C）。
///
/// 用法：
///   · Editor 菜单：Tools/VoxelWorld/Validate Geology Stage C
///   · 批处理：-executeMethod GeologyStageCValidation.RunBatch
///
/// 覆盖：
///   GC1 Deposit 同坐标确定性（含独立实例 / 交错调用 / 不同 seed 差异 / 开关）
///   GC2 Biome 同坐标确定性（同型覆盖 + 环境值始终有效）
///   GC3 Deposit 不影响 GeologySample.Material（只改矿藏参数 ⇒ 体素判定逐字段一致）
///   GC4 Deposit 不生成 Entity（纯数据 + 无 Entity/预制体/实例化 API）
///   GC5 Biome 不生成植被（纯数据 + 无 Tree/Plant/Animal/Vegetation）
///   GC6 无 UnityEngine 依赖（扫描 4 个新文件 + 静态字段 + 正向对照）
///   GC7 已有 6 个入口 PASS（入口文件存在 + 哈希不变 + RunBatch 可反射调用）
///   GC8 既有文件 SHA 不变（含本轮两处**已授权修改**的显式登记）
///
/// 本文件是 Editor 批处理验证：允许使用 UnityEngine 与既有实现；
/// 被扫描的禁令只针对 `Assets/Scripts/VoxelWorld/Generation/Geology/*.cs`。
/// </summary>
public static class GeologyStageCValidation
{
    private static int checks;
    private static int failures;

    private const string GeologyFolder = "Scripts/VoxelWorld/Generation/Geology";

    [MenuItem("Tools/VoxelWorld/Validate Geology Stage C")]
    public static void ValidateFromMenu()
    {
        ValidateAll();

        Debug.Log("[GeologyStageCValidation] Menu validation result = " +
                  (failures == 0 ? "PASS" : "FAIL"));
    }

    public static void RunBatch()
    {
        ValidateAll();

        Debug.Log(
            "[GeologyStageCValidation] ===== SUMMARY =====\n" +
            "Checks = " + checks + "\n" +
            "Failures = " + failures + "\n" +
            "Result = " + (failures == 0 ? "PASS" : "FAIL"));

        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    private static void ValidateAll()
    {
        checks = 0;
        failures = 0;

        Run("GC1 Deposit 同坐标确定性", ValidateDepositDeterminism);
        Run("GC2 Biome 同坐标确定性", ValidateBiomeDeterminism);
        Run("GC3 Deposit 不影响 Material", ValidateDepositDoesNotAffectMaterial);
        Run("GC4 Deposit 不生成 Entity", ValidateDepositGeneratesNoEntity);
        Run("GC5 Biome 不生成植被", ValidateBiomeGeneratesNoVegetation);
        Run("GC6 无 UnityEngine 依赖", ValidateStructuralScan);
        Run("GC7 已有入口可用", ValidateExistingEntries);
        Run("GC8 既有文件 SHA 不变", ValidateExistingFilesUnchanged);
    }

    // =====================================================================
    // GC1：Deposit 同坐标确定性
    // =====================================================================

    private static void ValidateDepositDeterminism()
    {
        WorldGenerationSettings stage1 = WorldGenerationSettings.Default;
        GeologySettings geology = GeologySettings.Default;
        geology.DepositRichness = 70;                 // 让矿藏足够常见（非平凡）
        geology.EnableBiomeModifiers = false;

        GeologySampler sampler = new GeologySampler(stage1, geology);
        GeologyDepositProvider a = new GeologyDepositProvider(sampler);
        GeologyDepositProvider b = new GeologyDepositProvider(new GeologySampler(stage1, geology));

        int[][] coordinates =
        {
            new int[] { 0, 0, 0 },
            new int[] { 0, 10, 0 },
            new int[] { 5, 15, 7 },
            new int[] { 15, 20, 15 },
            new int[] { 16, 20, 16 },
            new int[] { -1, 20, -1 },
            new int[] { -33, 12, 47 },
            new int[] { 128, 8, -256 }
        };

        int[] seeds = { 0, 12345, -777 };

        int compared = 0;
        int mismatch = 0;
        int existsCount = 0;
        string firstMismatch = null;

        for (int s = 0; s < seeds.Length; s++)
        {
            for (int c = 0; c < coordinates.Length; c++)
            {
                int x = coordinates[c][0];
                int y = coordinates[c][1];
                int z = coordinates[c][2];

                DepositSample first = a.TryGetDeposit(x, y, z, seeds[s]);
                DepositSample second = a.TryGetDeposit(x, y, z, seeds[s]);
                DepositSample other = b.TryGetDeposit(x, y, z, seeds[s]);

                DepositSample outSample;
                bool outExists = a.TryGetDeposit(x, y, z, seeds[s], out outSample);

                compared++;
                existsCount += first.Exists ? 1 : 0;

                string difference = CompareDeposits(first, second) ??
                                    CompareDeposits(first, other);

                if (difference != null)
                {
                    mismatch++;
                    if (firstMismatch == null) firstMismatch = difference;
                }

                if (outExists != first.Exists || CompareDeposits(first, outSample) != null)
                {
                    mismatch++;
                    if (firstMismatch == null) firstMismatch = "out 重载结果不一致";
                }

                if (!first.IsConsistent())
                {
                    mismatch++;
                    if (firstMismatch == null) firstMismatch = "结果不自洽：" + first;
                }
            }
        }

        Check(mismatch == 0,
            "GC1：同 (坐标, seed) 的 Deposit 结果逐字段一致（比较 " + compared +
            " 组 + out 重载，不一致 " + mismatch + "）" +
            (firstMismatch == null ? "" : " 首次：" + firstMismatch));

        Check(existsCount > 0,
            "GC1：样本中确实存在资源（非平凡断言；命中 " + existsCount + " / " + compared + "）");

        // 交错调用不影响（无隐藏状态）
        DepositSample beforeInterleave = a.TryGetDeposit(7, 12, 9, 12345);
        for (int i = 0; i < 128; i++)
        {
            a.TryGetDeposit(i * 3, 5 + (i % 20), -i, 999);
            sampler.Sample(i, 5, -i, 999);
        }

        Check(CompareDeposits(beforeInterleave, a.TryGetDeposit(7, 12, 9, 12345)) == null,
            "GC1：交错大量调用后结果不变（无隐藏状态）");

        // 不同 seed 应有差异（反例保护）
        int seedDifferences = 0;
        for (int i = 0; i < 256; i++)
        {
            DepositSample first = a.TryGetDeposit(i, 10, i * 2, 1);
            DepositSample other = a.TryGetDeposit(i, 10, i * 2, 2);
            if (CompareDeposits(first, other) != null) seedDifferences++;
        }

        Check(seedDifferences > 0,
            "GC1：不同 seed 产生不同资源结果（差异 " + seedDifferences + " / 256）");

        // 显式关闭 ⇒ 恒无资源（且不影响其它系统）
        GeologySettings disabled = geology.Clone();
        disabled.EnableDeposits = false;
        GeologyDepositProvider disabledProvider =
            new GeologyDepositProvider(new GeologySampler(stage1, disabled));

        int disabledHits = 0;
        for (int i = 0; i < 256; i++)
        {
            DepositSample sample = disabledProvider.TryGetDeposit(i, 10, i, 12345);
            if (sample.Exists || !sample.IsConsistent()) disabledHits++;
        }

        Check(disabledHits == 0,
            "GC1：EnableDeposits=false ⇒ 恒返回 None 且自洽（违反 " + disabledHits + "）");

        // 只在实体内：世界之外 / 地表之上 / 空气坐标必须无资源
        int outsideViolations = 0;
        for (int y = -8; y < 0; y++)
        {
            if (a.TryGetDeposit(3, y, 4, 12345).Exists) outsideViolations++;
        }

        for (int y = stage1.WorldHeight; y < stage1.WorldHeight + 8; y++)
        {
            if (a.TryGetDeposit(3, y, 4, 12345).Exists) outsideViolations++;
        }

        Check(outsideViolations == 0,
            "GC1：世界之外的坐标必须无资源（违反 " + outsideViolations + "）");

        Debug.Log("[GeologyStageCValidation] Deposit 样例：" +
                  a.TryGetDeposit(5, 15, 7, 12345) + " / " +
                  a.TryGetDeposit(0, 0, 0, 12345));
    }

    // =====================================================================
    // GC2：Biome 同坐标确定性
    // =====================================================================

    private static void ValidateBiomeDeterminism()
    {
        WorldGenerationSettings stage1 = WorldGenerationSettings.Default;

        GeologySettings enabled = GeologySettings.Default;
        enabled.EnableBiomeModifiers = true;

        GeologySampler sampler = new GeologySampler(stage1, enabled);
        GeologyBiomeProvider a = new GeologyBiomeProvider(sampler);
        GeologyBiomeProvider b = new GeologyBiomeProvider(new GeologySampler(stage1, enabled));

        int[][] coordinates =
        {
            new int[] { 0, 0, 0 },
            new int[] { 0, 24, 0 },
            new int[] { 40, 16, -40 },
            new int[] { 15, 30, 15 },
            new int[] { 16, 30, 16 },
            new int[] { -1, 30, -1 },
            new int[] { -64, 48, 64 },
            new int[] { 1000, 0, -1000 }
        };

        int[] seeds = { 0, 12345, -777 };

        int compared = 0;
        int mismatch = 0;
        string firstMismatch = null;
        int biomeAVotes = 0;
        int biomeBVotes = 0;

        for (int s = 0; s < seeds.Length; s++)
        {
            for (int c = 0; c < coordinates.Length; c++)
            {
                int x = coordinates[c][0];
                int y = coordinates[c][1];
                int z = coordinates[c][2];

                BiomeSample first = a.SampleBiome(x, y, z, seeds[s]);
                BiomeSample second = a.SampleBiome(x, y, z, seeds[s]);
                BiomeSample other = b.SampleBiome(x, y, z, seeds[s]);

                compared++;

                string difference = CompareBiomes(first, second) ?? CompareBiomes(first, other);
                if (difference != null)
                {
                    mismatch++;
                    if (firstMismatch == null) firstMismatch = difference;
                }

                if (!first.IsConsistent())
                {
                    mismatch++;
                    if (firstMismatch == null) firstMismatch = "环境值越界：" + first;
                }

                if (first.Biome == GeologyBiome.BiomeA) biomeAVotes++;
                if (first.Biome == GeologyBiome.BiomeB) biomeBVotes++;
            }
        }

        Check(mismatch == 0,
            "GC2：同 (坐标, seed) 的 Biome 结果逐字段一致（比较 " + compared +
            " 组，不一致 " + mismatch + "）" +
            (firstMismatch == null ? "" : " 首次：" + firstMismatch));

        Check(biomeAVotes > 0 && biomeBVotes > 0,
            "GC2：两个占位群系都被实际命中（A=" + biomeAVotes + " / B=" + biomeBVotes + "）");

        // 海拔递减：同一列上温度必须**不增**（未触碰到范围边界时每体素严格下降 LapsePerVoxel），
        // 湿度是列属性 ⇒ 同一列上恒定
        int warmingViolations = 0;
        int lapseViolations = 0;
        int moistureDrift = 0;
        int measuredSteps = 0;

        for (int z = -8; z < 8; z++)
        {
            for (int x = 0; x < 8; x++)
            {
                int previousTemperature = a.SampleBiome(x, 0, z, 12345).Temperature;
                int columnMoisture = a.SampleBiome(x, 0, z, 12345).Moisture;

                for (int y = 1; y < 64; y++)
                {
                    BiomeSample sample = a.SampleBiome(x, y, z, 12345);

                    if (sample.Temperature > previousTemperature) warmingViolations++;

                    bool unclamped =
                        previousTemperature > BiomeSample.MinTemperature &&
                        sample.Temperature > BiomeSample.MinTemperature;

                    if (unclamped)
                    {
                        measuredSteps++;
                        if (previousTemperature - sample.Temperature !=
                            GeologyBiomeProvider.LapsePerVoxel)
                        {
                            lapseViolations++;
                        }
                    }

                    if (sample.Moisture != columnMoisture) moistureDrift++;

                    previousTemperature = sample.Temperature;
                }
            }
        }

        Check(warmingViolations == 0,
            "GC2：海拔越高温度不增（违反 " + warmingViolations + " 处）");
        Check(lapseViolations == 0 && measuredSteps > 0,
            "GC2：未触边界时温度每体素严格下降 " + GeologyBiomeProvider.LapsePerVoxel +
            "（检查 " + measuredSteps + " 步，违反 " + lapseViolations + "）");
        Check(moistureDrift == 0,
            "GC2：湿度是列属性（同一列上恒定；漂移 " + moistureDrift + "）");

        // 不同 seed 应有差异（反例保护）
        int seedDifferences = 0;
        for (int i = 0; i < 256; i++)
        {
            BiomeSample first = a.SampleBiome(i, 20, i * 2, 1);
            BiomeSample other = a.SampleBiome(i, 20, i * 2, 2);
            if (CompareBiomes(first, other) != null) seedDifferences++;
        }

        Check(seedDifferences > 0,
            "GC2：不同 seed 产生不同环境结果（差异 " + seedDifferences + " / 256）");

        // 关闭群系判定 ⇒ Biome=None，但温度/湿度仍然有效（环境测量与"修正开关"分离）
        GeologySettings disabled = GeologySettings.Default;
        disabled.EnableBiomeModifiers = false;
        GeologyBiomeProvider disabledProvider =
            new GeologyBiomeProvider(new GeologySampler(stage1, disabled));

        int noneViolations = 0;
        int environmentMissing = 0;
        for (int i = 0; i < 256; i++)
        {
            BiomeSample sample = disabledProvider.SampleBiome(i, 20, i, 12345);
            if (sample.Biome != GeologyBiome.None) noneViolations++;
            if (sample.Temperature == 0 && sample.Moisture == 0) environmentMissing++;
        }

        Check(noneViolations == 0,
            "GC2：EnableBiomeModifiers=false ⇒ Biome=None（违反 " + noneViolations + "）");
        Check(environmentMissing == 0,
            "GC2：关闭群系判定时温度/湿度仍然有效（缺失 " + environmentMissing + "）");

        Debug.Log("[GeologyStageCValidation] Biome 样例：" +
                  a.SampleBiome(0, 0, 0, 12345) + " / " +
                  a.SampleBiome(0, 48, 0, 12345) + " / " +
                  disabledProvider.SampleBiome(0, 0, 0, 12345));
    }

    // =====================================================================
    // GC3：Deposit 不影响 GeologySample.Material
    // =====================================================================

    private static void ValidateDepositDoesNotAffectMaterial()
    {
        WorldGenerationSettings stage1 = WorldGenerationSettings.Default;

        GeologySettings withoutDeposits = GeologySettings.Default;
        withoutDeposits.EnableCaves = false;
        withoutDeposits.EnableDeposits = false;
        withoutDeposits.DepositRichness = 0;

        GeologySettings withDeposits = GeologySettings.Default;
        withDeposits.EnableCaves = false;
        withDeposits.EnableDeposits = true;
        withDeposits.DepositRichness = 100;          // 实体处全部为资源（最强对照）
        withDeposits.DepositFrequency = 1;           // 格胞最大 ⇒ 覆盖最广

        GeologySampler withoutSampler = new GeologySampler(stage1, withoutDeposits);
        GeologySampler withSampler = new GeologySampler(stage1, withDeposits);

        GeologyDepositProvider withProvider = new GeologyDepositProvider(withSampler);
        GeologyDepositProvider withoutProvider = new GeologyDepositProvider(withoutSampler);

        int totalVoxels = 0;
        int materialMismatches = 0;
        int depositHits = 0;
        string firstMismatch = null;

        VoxelChunk probe = new BasicTerrainGenerator(stage1).Generate(new VoxelCoordinate(0, 1, 0));

        for (int y = 0; y < VoxelChunk.ChunkSize; y++)
        {
            for (int z = 0; z < VoxelChunk.ChunkSize; z++)
            {
                for (int x = 0; x < VoxelChunk.ChunkSize; x++)
                {
                    VoxelCoordinate world = probe.LocalToWorldCoordinate(x, y, z);

                    GeologySample sampleWithout =
                        withoutSampler.Sample(world.x, world.y, world.z, stage1.Seed);
                    GeologySample sampleWith =
                        withSampler.Sample(world.x, world.y, world.z, stage1.Seed);

                    totalVoxels++;

                    // 体素判定（岩性/密度）必须**完全一致**：矿藏只是属性层
                    if (CompareGeologySamples(sampleWithout, sampleWith) != null)
                    {
                        materialMismatches++;
                        if (firstMismatch == null)
                        {
                            firstMismatch = world + "：" +
                                            CompareGeologySamples(sampleWithout, sampleWith);
                        }
                    }

                    if (withProvider.TryGetDeposit(world.x, world.y, world.z, stage1.Seed).Exists)
                    {
                        depositHits++;
                    }
                }
            }
        }

        Check(totalVoxels == VoxelChunk.VoxelCount,
            "GC3：比较全 Chunk 体素（" + totalVoxels + "）");
        Check(materialMismatches == 0,
            "GC3：仅改变矿藏参数 ⇒ 采样结果（Rule/IsSolid/Material/…）逐字段一致" +
            "（不一致 " + materialMismatches + "）" +
            (firstMismatch == null ? "" : " 首次：" + firstMismatch));

        Check(depositHits > 0,
            "GC3：最强对照下确实检测到资源（命中 " + depositHits +
            " / " + totalVoxels + " ⇒ 断言非恒真）");

        // 关闭矿藏时必须恒无资源（与开启形成对照）
        int disabledHits = 0;
        for (int x = 0; x < 16; x++)
        {
            VoxelCoordinate world = probe.LocalToWorldCoordinate(x, 8, 0);
            if (withoutProvider.TryGetDeposit(world.x, world.y, world.z, stage1.Seed).Exists)
            {
                disabledHits++;
            }
        }

        Check(disabledHits == 0, "GC3：关闭矿藏 ⇒ 无任何资源命中（违反 " + disabledHits + "）");

        // 矿藏查询本身不改变世界数据（无副作用）：连续查询后 Chunk 快照不变
        VoxelType[] before = SnapshotVoxels(probe);
        for (int i = 0; i < 512; i++)
        {
            withProvider.TryGetDeposit(i, 20, -i, 12345);
            withSampler.Sample(i, 20, -i, 12345);
        }

        Check(CompareVoxels(probe, before, out string detail),
            "GC3：查询矿藏不修改任何体素（" + (detail ?? "OK") + "）");
    }

    // =====================================================================
    // GC4：Deposit 不生成 Entity
    // =====================================================================

    private static void ValidateDepositGeneratesNoEntity()
    {
        // 结构 1：DepositSample 是纯值类型，字段只有 4 个基础数据项
        Type sampleType = typeof(DepositSample);
        Check(sampleType.IsValueType, "GC4：DepositSample 是值类型（纯数据）");

        FieldInfo[] fields = sampleType.GetFields(BindingFlags.Public | BindingFlags.Instance);
        Check(fields.Length == 4,
            "GC4：DepositSample 恰好 4 个数据字段（Exists/Zone/Richness/Grade；实际 " +
            fields.Length + "）");

        int referenceFields = 0;
        for (int i = 0; i < fields.Length; i++)
        {
            if (!fields[i].FieldType.IsValueType) referenceFields++;
        }

        Check(referenceFields == 0,
            "GC4：DepositSample 不含引用类型字段（引用字段 " + referenceFields + "）");

        MethodInfo[] methods = sampleType.GetMethods(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Check(methods.Length == 2,
            "GC4：DepositSample 只有 IsConsistent/ToString 两个只读方法（实际 " +
            methods.Length + "）");

        // 结构 2：接口只返回描述，不返回任何对象/句柄
        MethodInfo[] interfaceMethods = typeof(IDepositProvider).GetMethods();
        int objectReturns = 0;
        for (int i = 0; i < interfaceMethods.Length; i++)
        {
            if (!interfaceMethods[i].ReturnType.IsValueType &&
                interfaceMethods[i].ReturnType != typeof(bool))
            {
                objectReturns++;
            }
        }

        Check(objectReturns == 0,
            "GC4：IDepositProvider 只返回值类型/布尔（返回引用类型的方法 " + objectReturns + "）");

        // 结构 3：源码中不得出现 Entity / 晶簇 / 预制体 / 实例化
        string folder = Path.Combine(Application.dataPath, GeologyFolder);
        string[] depositSources =
        {
            Path.Combine(folder, "IDepositProvider.cs"),
            Path.Combine(folder, "GeologyDepositProvider.cs")
        };

        string[] entityTokens =
        {
            "CrystalCluster", "Entity", "Spawn", "Prefab", "Instantiate",
            "GameObject", "MonoBehaviour", "Transform"
        };

        int entityHits = 0;
        for (int i = 0; i < depositSources.Length; i++)
        {
            string code = StripComments(File.ReadAllText(depositSources[i], Encoding.UTF8));
            List<string> found = ScanTokens(code, entityTokens);

            if (found.Count > 0)
            {
                entityHits += found.Count;
                Check(false, "GC4：" + Path.GetFileName(depositSources[i]) +
                             " 出现 Entity 相关标识：" + string.Join(", ", found.ToArray()));
            }
        }

        Check(entityHits == 0,
            "GC4：Deposit 实现/接口不出现 Entity/晶簇/预制体/实例化（命中 " + entityHits + "）");

        // 行为：查询 1000 次不产生任何对象（以场景对象计数为证）
        WorldGenerationSettings stage1 = WorldGenerationSettings.Default;
        GeologySampler sampler = new GeologySampler(stage1, GeologySettings.Default);
        GeologyDepositProvider provider = new GeologyDepositProvider(sampler);

        GameObject probeHost = new GameObject("GeologyStageCDepositProbe");

        try
        {
            int before = FindObjectCountInScene();
            int hits = 0;

            for (int i = 0; i < 1000; i++)
            {
                if (provider.TryGetDeposit(i % 64, 4 + (i % 24), (i * 7) % 64, 12345).Exists) hits++;
            }

            int after = FindObjectCountInScene();

            Check(after == before,
                "GC4：1000 次矿藏查询不产生任何 GameObject（前 " + before + " → 后 " + after +
                "；命中 " + hits + "）");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(probeHost);
        }
    }

    // =====================================================================
    // GC5：Biome 不生成植被
    // =====================================================================

    private static void ValidateBiomeGeneratesNoVegetation()
    {
        Type sampleType = typeof(BiomeSample);
        Check(sampleType.IsValueType, "GC5：BiomeSample 是值类型（纯数据）");

        FieldInfo[] fields = sampleType.GetFields(BindingFlags.Public | BindingFlags.Instance);
        Check(fields.Length == 3,
            "GC5：BiomeSample 恰好 3 个数据字段（Biome/Temperature/Moisture；实际 " +
            fields.Length + "）");

        MethodInfo[] methods = sampleType.GetMethods(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Check(methods.Length == 2,
            "GC5：BiomeSample 只有 IsConsistent/ToString 两个只读方法（实际 " +
            methods.Length + "）");

        MethodInfo[] interfaceMethods = typeof(IBiomeProvider).GetMethods();
        Check(interfaceMethods.Length == 1 &&
              interfaceMethods[0].ReturnType == typeof(BiomeSample),
            "GC5：IBiomeProvider 只提供 SampleBiome 一个查询方法且返回纯数据");

        // 源码中不得出现植被/生物相关内容
        string folder = Path.Combine(Application.dataPath, GeologyFolder);
        string[] biomeSources =
        {
            Path.Combine(folder, "IBiomeProvider.cs"),
            Path.Combine(folder, "GeologyBiomeProvider.cs")
        };

        string[] vegetationTokens =
        {
            "Tree", "Plant", "Animal", "Vegetation", "Grass", "Flower", "Crop",
            "Spawn", "Prefab", "Instantiate", "GameObject", "Entity"
        };

        int vegetationHits = 0;
        for (int i = 0; i < biomeSources.Length; i++)
        {
            string code = StripComments(File.ReadAllText(biomeSources[i], Encoding.UTF8));
            List<string> found = ScanTokens(code, vegetationTokens);

            if (found.Count > 0)
            {
                vegetationHits += found.Count;
                Check(false, "GC5：" + Path.GetFileName(biomeSources[i]) +
                             " 出现植被/生物相关标识：" + string.Join(", ", found.ToArray()));
            }
        }

        Check(vegetationHits == 0,
            "GC5：Biome 实现/接口不出现 Tree/Plant/Animal/Vegetation/Entity（命中 " +
            vegetationHits + "）");

        // 行为：采样 1000 次不产生任何对象
        WorldGenerationSettings stage1 = WorldGenerationSettings.Default;
        GeologySettings geology = GeologySettings.Default;
        geology.EnableBiomeModifiers = true;
        GeologyBiomeProvider provider =
            new GeologyBiomeProvider(new GeologySampler(stage1, geology));

        GameObject probeHost = new GameObject("GeologyStageCBiomeProbe");

        try
        {
            int before = FindObjectCountInScene();
            int samples = 0;

            for (int i = 0; i < 1000; i++)
            {
                BiomeSample sample = provider.SampleBiome(i % 64, i % 64, (i * 3) % 64, 12345);
                if (sample.IsConsistent()) samples++;
            }

            int after = FindObjectCountInScene();

            Check(after == before,
                "GC5：1000 次群系采样不产生任何 GameObject（前 " + before + " → 后 " + after +
                "；有效 " + samples + "）");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(probeHost);
        }
    }

    // =====================================================================
    // GC6：无 UnityEngine 依赖（4 个新文件的扫描）
    // =====================================================================

    private static readonly string[] ForbiddenPatterns =
    {
        // Unity 依赖
        "UnityEngine", "UnityEditor", "MonoBehaviour", "GameObject", "Transform",
        @"\bVector2\b", @"\bVector3\b", @"new Material\(", @"\bRandom\b", @"\bMathf\b",
        @"Time\.", "DateTime", @"\bGuid\b",
        // Chunk / 数据容器
        @"\bchunkCoordinate\b", @"\bchunkX\b", @"\bchunkY\b", @"\bchunkZ\b",
        "VoxelChunk", @"new VoxelWorld\b", @"VoxelWorld\.(?!Generation)",
        "SetVoxel", "MarkDirty", "ClearDirty",
        // Runtime / Streaming / Mesh
        "VoxelRuntimeCore", "ChunkRecord", "ChunkStreamingDriver",
        "IChunkPresenter", "IChunkMesher", "VoxelMeshGenerator", "VoxelMeshData",
        // Entity（不得进入地质层）
        "CrystalCluster", "Instantiate", "Prefab", @"\bEntity\b"
    };

    private static void ValidateStructuralScan()
    {
        string folder = Path.Combine(Application.dataPath, GeologyFolder);
        Check(Directory.Exists(folder), "GC6：Geology 目录存在（" + folder + "）");
        if (!Directory.Exists(folder)) return;

        string[] stageCFiles =
        {
            "IDepositProvider.cs", "IBiomeProvider.cs",
            "GeologyDepositProvider.cs", "GeologyBiomeProvider.cs"
        };

        for (int i = 0; i < stageCFiles.Length; i++)
        {
            Check(File.Exists(Path.Combine(folder, stageCFiles[i])),
                "GC6：存在 " + stageCFiles[i]);
        }

        // 扫描器自检
        string probe = "using UnityEngine;\n// UnityEngine 在注释里不计\nvoid P(){ }\n";
        Check(ScanTokens(StripComments(probe), ForbiddenPatterns).Count == 1,
            "GC6：扫描器自检（代码区命中 1）");

        string commentProbe = "// UnityEngine Entity CrystalCluster VoxelChunk\nvoid Q(){ }\n";
        Check(ScanTokens(StripComments(commentProbe), ForbiddenPatterns).Count == 0,
            "GC6：扫描器忽略注释");

        string namespaceProbe = "namespace VoxelWorld.Generation.Geology { }\n";
        Check(ScanTokens(StripComments(namespaceProbe), ForbiddenPatterns).Count == 0,
            "GC6：namespace VoxelWorld.Generation.Geology 不算违规（正向对照）");

        int hits = 0;
        for (int i = 0; i < stageCFiles.Length; i++)
        {
            string path = Path.Combine(folder, stageCFiles[i]);
            if (!File.Exists(path)) continue;

            string code = StripComments(File.ReadAllText(path, Encoding.UTF8));
            List<string> found = ScanTokens(code, ForbiddenPatterns);

            if (found.Count > 0)
            {
                hits += found.Count;
                Check(false, "GC6：" + stageCFiles[i] + " 命中禁止项：" +
                             string.Join(", ", found.ToArray()));
            }

            // 静态可变字段
            string[] lines = code.Replace("\r\n", "\n").Split('\n');
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

                hits++;
                Check(false, "GC6：" + stageCFiles[i] + " 存在静态可变字段候选：L" +
                             (l + 1) + " " + line);
            }
        }

        Check(hits == 0,
            "GC6：Stage C 的 4 个文件不含 Unity / Chunk / Runtime / Mesh / Entity、" +
            "无静态可变字段（命中 " + hits + "）");

        // 正向对照：两个实现都使用 GeologyNoise（确定性噪声），且接口不依赖密度实现细节
        string depositCode = StripComments(
            File.ReadAllText(Path.Combine(folder, "GeologyDepositProvider.cs"), Encoding.UTF8));
        string biomeCode = StripComments(
            File.ReadAllText(Path.Combine(folder, "GeologyBiomeProvider.cs"), Encoding.UTF8));

        Check(depositCode.Contains("GeologyNoise.Value"),
            "GC6：Deposit 实现使用 GeologyNoise（确定性噪声，正向对照）");
        Check(biomeCode.Contains("GeologyNoise.Value"),
            "GC6：Biome 实现使用 GeologyNoise（确定性噪声，正向对照）");
        Check(depositCode.Contains("GeologySample") && depositCode.Contains("IsSolid"),
            "GC6：Deposit 实现只在实体内输出资源（复用采样器的密度判定，正向对照）");

        Debug.Log("[GeologyStageCValidation] 扫描文件：\n" +
                  string.Join("\n", stageCFiles));
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
    // GC7：已有入口可用（存在 + 哈希不变 + RunBatch 可反射调用）
    // =====================================================================

    private static readonly string[] ExistingEntryTypes =
    {
        "VoxelWorldCoreValidation", "WorldGenerationCoreValidation", "VoxelMeshCoreValidation",
        "VoxelWorldViewValidation", "VoxelWorldRuntimeCoreValidation", "ChunkStreamingValidation",
        "GeologyStageAValidation", "GeologyStageBValidation"
    };

    private static void ValidateExistingEntries()
    {
        Assembly editorAssembly = typeof(GeologyStageCValidation).Assembly;
        int missingTypes = 0;
        int missingRunBatch = 0;

        for (int i = 0; i < ExistingEntryTypes.Length; i++)
        {
            Type type = editorAssembly.GetType(ExistingEntryTypes[i]);
            if (type == null) { missingTypes++; continue; }

            MethodInfo runBatch = type.GetMethod(
                "RunBatch", BindingFlags.Public | BindingFlags.Static);

            if (runBatch == null || runBatch.ReturnType != typeof(void)) missingRunBatch++;
        }

        Check(missingTypes == 0,
            "GC7：" + ExistingEntryTypes.Length + " 个既有验证入口类型都存在（缺失 " +
            missingTypes + "）");
        Check(missingRunBatch == 0,
            "GC7：全部入口都提供 public static void RunBatch()（缺失 " + missingRunBatch + "）");

        // 入口文件存在（6 个既有 + 2 个地质阶段）
        string[] entryFiles =
        {
            "Editor/VoxelWorldCoreValidation.cs",
            "Editor/WorldGenerationCoreValidation.cs",
            "Editor/VoxelMeshCoreValidation.cs",
            "Editor/VoxelWorldViewValidation.cs",
            "Editor/VoxelWorldRuntimeCoreValidation.cs",
            "Editor/ChunkStreamingValidation.cs",
            "Editor/GeologyStageAValidation.cs",
            "Editor/GeologyStageBValidation.cs"
        };

        int missingFiles = 0;
        for (int i = 0; i < entryFiles.Length; i++)
        {
            if (!File.Exists(Path.Combine(Application.dataPath, entryFiles[i]))) missingFiles++;
        }

        Check(missingFiles == 0,
            "GC7：8 个验证入口文件都存在（缺失 " + missingFiles + "）");

        Debug.Log("[GeologyStageCValidation] GC7：既有 6 个入口由外部编排逐个运行" +
                  "（本入口只能校验其存在性与签名；实际 PASS 结果见运行报告）");
    }

    // =====================================================================
    // GC8：既有文件 SHA 不变（含本轮两处已授权修改的登记）
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
            // 既有 Editor 验证入口（除下列两处已授权修改）
            { "Editor/VoxelWorldCoreValidation.cs", "6DE813AABCE95394" },
            { "Editor/WorldGenerationCoreValidation.cs", "4A517A0F902A1066" },
            { "Editor/VoxelMeshCoreValidation.cs", "5FFD8451D2CE05EB" },
            { "Editor/VoxelWorldViewValidation.cs", "BEFBAAF6B111A241" },
            { "Editor/VoxelWorldRuntimeCoreValidation.cs", "0E0B5C160FEAB236" },
            { "Editor/ChunkStreamingValidation.cs", "13E244B6EE978FC8" },
            // Stage A/B 的地质文件（除 StageAValidation 已授权修改）
            { "Scripts/VoxelWorld/Generation/Geology/GeologySettings.cs", "BEA8EB4F4967721C" },
            { "Scripts/VoxelWorld/Generation/Geology/GeologyNoise.cs", "2266E7A880E43315" },
            { "Scripts/VoxelWorld/Generation/Geology/GeologySample.cs", "4946B90F3E56262E" },
            { "Scripts/VoxelWorld/Generation/Geology/GeologySampler.cs", "62836145F8F3E212" }
        };

    /// <summary>
    /// 本轮**经用户明确授权**修改的两个文件（修改内容与原因见下方注释），
    /// 因此与上一轮记录的哈希不同；这里以**新哈希**钉住，避免"悄悄改变"。
    ///   · `Editor/GeologyStageAValidation.cs`：修复 GSA5（固定文件数 ⇒ 核心文件存在 + 禁止未知 Runtime/Entity 类型 + 允许扩展）；
    ///   · `Editor/GeologyStageBValidation.cs`：GB7 重新钉住上述被修改文件的哈希（使其继续可验证）。
    /// </summary>
    private static readonly Dictionary<string, string> AuthorizedChangedFiles =
        new Dictionary<string, string>
        {
            { "Editor/GeologyStageAValidation.cs", "DF42F8605BA60CAF" },
            { "Editor/GeologyStageBValidation.cs", "CFA08E4EE8D49C99" }
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
            if (!VerifyOne(pair.Key, pair.Value, specificationRoot, ref compared, ref missing,
                    ref mismatch, ref skipped, "GC8：未授权修改")) continue;
        }

        foreach (KeyValuePair<string, string> pair in AuthorizedChangedFiles)
        {
            if (!VerifyOne(pair.Key, pair.Value, specificationRoot, ref compared, ref missing,
                    ref mismatch, ref skipped, "GC8：已授权修改(新哈希)")) continue;
        }

        Check(missing == 0, "GC8：既有文件全部存在（缺失 " + missing + "）");
        Check(mismatch == 0,
            "GC8：既有文件 SHA256(前16) 与登记一致（比对 " + compared +
            " 个；其中 " + AuthorizedChangedFiles.Count + " 个为本轮已授权修改并用新哈希钉住；跳过 " +
            skipped + "）");
    }

    private static bool VerifyOne(
        string key, string expected, string specificationRoot,
        ref int compared, ref int missing, ref int mismatch, ref int skipped, string label)
    {
        string path;

        if (key.StartsWith("../Specifications/", StringComparison.Ordinal))
        {
            if (specificationRoot == null) { skipped++; return true; }

            path = Path.Combine(
                specificationRoot, key.Substring(3).Replace('/', Path.DirectorySeparatorChar));
        }
        else
        {
            path = Path.Combine(Application.dataPath, key);
        }

        if (!File.Exists(path))
        {
            missing++;
            Check(false, label + "：缺失 " + key);
            return false;
        }

        string hash = ComputeFileHash16(path);
        if (hash == null) return true;

        compared++;
        if (hash != expected)
        {
            mismatch++;
            Check(false, label + "：" + key + " 哈希不符（" + hash + " != " + expected + "）");
            return false;
        }

        return true;
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
            Check(false, "GC8：无法计算哈希（" + path + "）：" + ex.Message);
            return null;
        }
    }

    // =====================================================================
    // 测试辅助
    // =====================================================================

    private static string CompareDeposits(DepositSample a, DepositSample b)
    {
        if (a.Exists != b.Exists) return "Exists " + a.Exists + " != " + b.Exists;
        if (a.Zone != b.Zone) return "Zone " + a.Zone + " != " + b.Zone;
        if (a.Richness != b.Richness) return "Richness " + a.Richness + " != " + b.Richness;
        if (a.Grade != b.Grade) return "Grade " + a.Grade + " != " + b.Grade;
        return null;
    }

    private static string CompareBiomes(BiomeSample a, BiomeSample b)
    {
        if (a.Biome != b.Biome) return "Biome " + a.Biome + " != " + b.Biome;
        if (a.Temperature != b.Temperature)
            return "Temperature " + a.Temperature + " != " + b.Temperature;
        if (a.Moisture != b.Moisture) return "Moisture " + a.Moisture + " != " + b.Moisture;
        return null;
    }

    private static string CompareGeologySamples(GeologySample a, GeologySample b)
    {
        if (a.Rule != b.Rule) return "Rule " + a.Rule + " != " + b.Rule;
        if (a.IsSolid != b.IsSolid) return "IsSolid " + a.IsSolid + " != " + b.IsSolid;
        if (a.Material != b.Material) return "Material " + a.Material + " != " + b.Material;
        if (a.SurfaceHeight != b.SurfaceHeight)
            return "SurfaceHeight " + a.SurfaceHeight + " != " + b.SurfaceHeight;
        if (a.SoilDepth != b.SoilDepth) return "SoilDepth " + a.SoilDepth + " != " + b.SoilDepth;
        if (a.CaveActive != b.CaveActive) return "CaveActive 不一致";
        if (BitConverter.SingleToInt32Bits(a.CaveDensity) !=
            BitConverter.SingleToInt32Bits(b.CaveDensity)) return "CaveDensity 不一致";
        if (a.DepositZone != b.DepositZone) return "DepositZone 不一致";
        if (a.Biome != b.Biome) return "Biome 不一致";
        return null;
    }

    private static VoxelType[] SnapshotVoxels(VoxelChunk chunk)
    {
        VoxelType[] snapshot = new VoxelType[VoxelChunk.VoxelCount];
        int index = 0;

        for (int x = 0; x < VoxelChunk.ChunkSize; x++)
        {
            for (int y = 0; y < VoxelChunk.ChunkSize; y++)
            {
                for (int z = 0; z < VoxelChunk.ChunkSize; z++)
                {
                    snapshot[index++] = chunk.GetVoxel(x, y, z).Type;
                }
            }
        }

        return snapshot;
    }

    private static bool CompareVoxels(VoxelChunk chunk, VoxelType[] expected, out string detail)
    {
        detail = null;
        int index = 0;

        for (int x = 0; x < VoxelChunk.ChunkSize; x++)
        {
            for (int y = 0; y < VoxelChunk.ChunkSize; y++)
            {
                for (int z = 0; z < VoxelChunk.ChunkSize; z++)
                {
                    if (chunk.GetVoxel(x, y, z).Type != expected[index])
                    {
                        detail = "(" + x + "," + y + "," + z + ") 体素被修改";
                        return false;
                    }

                    index++;
                }
            }
        }

        return true;
    }

    private static int FindObjectCountInScene()
    {
        return UnityEngine.Object.FindObjectsOfType<GameObject>().Length;
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
