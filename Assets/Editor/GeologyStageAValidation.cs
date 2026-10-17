using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using VoxelWorld;
using VoxelWorld.Generation.Geology;

/// <summary>
/// Geology Stage A 验证（WorldGeneration_Geology_Implementation.md §13.1 Stage A）。
///
/// 用法：
///   · Editor 菜单：Tools/VoxelWorld/Validate Geology Stage A
///   · 批处理：-executeMethod GeologyStageAValidation.RunBatch
///
/// 覆盖：
///   GSA1 参数合法性
///   GSA2 同 seed 同坐标输出一致
///   GSA3 不同 seed 输出差异
///   GSA4 相邻坐标变化有限（连续性）
///   GSA5 无 UnityEngine 依赖扫描（+ 结构纪律）
///   GSA6 零修改（既有文件 SHA 不变）      ← 补充，Stage A 硬性要求（§13.2）
///   GSA7 采样结果描述结构（GeologySample） ← 补充，Stage A 文件清单之一
///
/// 本文件是 Editor 批处理验证：允许使用 UnityEngine 与具体类型；
/// 被扫描的禁令只针对 `Assets/Scripts/VoxelWorld/Generation/Geology/*.cs`。
/// </summary>
public static class GeologyStageAValidation
{
    private static int checks;
    private static int failures;

    private const string GeologyFolder = "Scripts/VoxelWorld/Generation/Geology";

    [MenuItem("Tools/VoxelWorld/Validate Geology Stage A")]
    public static void ValidateFromMenu()
    {
        ValidateAll();

        Debug.Log("[GeologyStageAValidation] Menu validation result = " +
                  (failures == 0 ? "PASS" : "FAIL"));
    }

    public static void RunBatch()
    {
        ValidateAll();

        Debug.Log(
            "[GeologyStageAValidation] ===== SUMMARY =====\n" +
            "Checks = " + checks + "\n" +
            "Failures = " + failures + "\n" +
            "Result = " + (failures == 0 ? "PASS" : "FAIL"));

        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    private static void ValidateAll()
    {
        checks = 0;
        failures = 0;

        Run("GSA1 参数合法性", ValidateSettings);
        Run("GSA2 同 seed 同坐标输出一致", ValidateDeterminism);
        Run("GSA3 不同 seed 输出差异", ValidateSeedSensitivity);
        Run("GSA4 相邻坐标变化有限", ValidateContinuity);
        Run("GSA5 无 UnityEngine 依赖扫描", ValidateStructuralScan);
        Run("GSA6 零修改（既有文件 SHA 不变）", ValidateExistingFilesUnchanged);
        Run("GSA7 采样结果描述结构", ValidateSampleStruct);
    }

    // =====================================================================
    // GSA1：参数合法性
    // =====================================================================

    private static void ValidateSettings()
    {
        string reason;

        GeologySettings defaults = GeologySettings.Default;
        Check(defaults.IsValid(out reason),
            "GSA1：默认配置合法（" + (reason ?? "OK") + "）");
        Check(defaults.EnableCaves && defaults.EnableDeposits && !defaults.EnableBiomeModifiers,
            "GSA1：默认配置的开关状态符合预期（" + defaults + "）");
        Check(defaults.CaveCellVoxels == 8 && defaults.DepositCellVoxels == 8,
            "GSA1：默认格胞边长 = 8 体素（实际 " + defaults.CaveCellVoxels +
            " / " + defaults.DepositCellVoxels + "）");

        // 默认配置每次返回独立实例
        GeologySettings first = GeologySettings.Default;
        GeologySettings second = GeologySettings.Default;
        first.CaveFrequency = 64;
        Check(second.CaveFrequency == 8,
            "GSA1：Default 每次返回独立实例（第二份未被修改：" + second.CaveFrequency + "）");

        // Clone 独立性
        GeologySettings original = GeologySettings.Default;
        GeologySettings copy = original.Clone();
        copy.EnableCaves = false;
        copy.CaveThreshold = 0.5f;
        copy.DepositRichness = 7;
        Check(original.EnableCaves && original.CaveThreshold == 0f &&
              original.DepositRichness == 50,
            "GSA1：Clone 独立（原实例未被修改：" + original + "）");
        Check(copy.IsValid(out reason), "GSA1：Clone 结果仍合法");

        // 非法频率（必须被拒绝并给出原因）
        int[] badFrequencies = { 0, -1, 3, 5, 63, 65, 128 };
        for (int i = 0; i < badFrequencies.Length; i++)
        {
            GeologySettings bad = GeologySettings.Default;
            bad.CaveFrequency = badFrequencies[i];
            bool valid = bad.IsValid(out reason);
            Check(!valid && !string.IsNullOrEmpty(reason),
                "GSA1：CaveFrequency=" + badFrequencies[i] + " 被拒绝（" + reason + "）");
        }

        GeologySettings badDeposit = GeologySettings.Default;
        badDeposit.DepositFrequency = 0;
        Check(!badDeposit.IsValid(out reason), "GSA1：DepositFrequency=0 被拒绝");

        // 合法频率全集
        int[] goodFrequencies = { 1, 2, 4, 8, 16, 32, 64 };
        for (int i = 0; i < goodFrequencies.Length; i++)
        {
            GeologySettings good = GeologySettings.Default;
            good.CaveFrequency = goodFrequencies[i];
            good.DepositFrequency = goodFrequencies[i];

            bool valid = good.IsValid(out reason);
            int expectedCell = 64 / goodFrequencies[i];
            Check(valid && good.CaveCellVoxels == expectedCell,
                "GSA1：Frequency=" + goodFrequencies[i] + " 合法且格胞=" + expectedCell +
                " 体素（实际 " + good.CaveCellVoxels + "）");
        }

        // 阈值边界与 NaN
        GeologySettings thresholdLow = GeologySettings.Default;
        thresholdLow.CaveThreshold = -1f;
        Check(thresholdLow.IsValid(out reason), "GSA1：CaveThreshold=-1 合法（下界）");

        GeologySettings thresholdHigh = GeologySettings.Default;
        thresholdHigh.CaveThreshold = 1f;
        Check(thresholdHigh.IsValid(out reason), "GSA1：CaveThreshold=1 合法（上界）");

        GeologySettings thresholdOver = GeologySettings.Default;
        thresholdOver.CaveThreshold = 1.5f;
        Check(!thresholdOver.IsValid(out reason),
            "GSA1：CaveThreshold=1.5 被拒绝（" + reason + "）");

        GeologySettings thresholdUnder = GeologySettings.Default;
        thresholdUnder.CaveThreshold = -2f;
        Check(!thresholdUnder.IsValid(out reason), "GSA1：CaveThreshold=-2 被拒绝");

        GeologySettings thresholdNaN = GeologySettings.Default;
        thresholdNaN.CaveThreshold = float.NaN;
        Check(!thresholdNaN.IsValid(out reason),
            "GSA1：CaveThreshold=NaN 被拒绝（" + reason + "）");

        // 丰度边界
        GeologySettings richnessLow = GeologySettings.Default;
        richnessLow.DepositRichness = 0;
        Check(richnessLow.IsValid(out reason), "GSA1：DepositRichness=0 合法");

        GeologySettings richnessHigh = GeologySettings.Default;
        richnessHigh.DepositRichness = 100;
        Check(richnessHigh.IsValid(out reason), "GSA1：DepositRichness=100 合法");

        GeologySettings richnessOver = GeologySettings.Default;
        richnessOver.DepositRichness = -1;
        Check(!richnessOver.IsValid(out reason), "GSA1：DepositRichness=-1 被拒绝");

        Check(!string.IsNullOrEmpty(GeologySettings.Default.ToString()),
            "GSA1：ToString 可用（" + GeologySettings.Default + "）");
    }

    // =====================================================================
    // GSA2：同 seed 同坐标输出一致
    // =====================================================================

    private static void ValidateDeterminism()
    {
        int[] seeds = { 0, 1, 12345, -98765, int.MaxValue, int.MinValue };

        int[][] coordinates =
        {
            new int[] { 0, 0, 0 },
            new int[] { 1, 0, 0 },
            new int[] { -1, 0, 0 },
            new int[] { 16, 17, 18 },
            new int[] { -17, 33, -49 },
            new int[] { 1024, 512, -2048 },
            new int[] { 1000000, 64, -1000000 }
        };

        int compared = 0;
        int mismatches = 0;

        for (int s = 0; s < seeds.Length; s++)
        {
            for (int c = 0; c < coordinates.Length; c++)
            {
                int x = coordinates[c][0];
                int y = coordinates[c][1];
                int z = coordinates[c][2];

                float first = GeologyNoise.Value3D(x, y, z, seeds[s]);
                float second = GeologyNoise.Value3D(x, y, z, seeds[s]);

                compared++;
                if (!BitEquals(first, second)) mismatches++;

                // 2D 同源检查
                if (!BitEquals(GeologyNoise.Value2D(x, z, seeds[s]),
                               GeologyNoise.Value2D(x, z, seeds[s])))
                {
                    mismatches++;
                }
            }
        }

        Check(mismatches == 0,
            "GSA2：重复采样逐位一致（比较 " + compared + " 组 3D + 同量 2D，不一致 " + mismatches + "）");

        // 调用顺序无关（无静态可变状态）
        float[] forward = new float[coordinates.Length];
        for (int i = 0; i < coordinates.Length; i++)
        {
            forward[i] = GeologyNoise.Value3D(
                coordinates[i][0], coordinates[i][1], coordinates[i][2], 12345);
        }

        float[] backward = new float[coordinates.Length];
        for (int i = coordinates.Length - 1; i >= 0; i--)
        {
            backward[i] = GeologyNoise.Value3D(
                coordinates[i][0], coordinates[i][1], coordinates[i][2], 12345);
        }

        int orderMismatch = 0;
        for (int i = 0; i < forward.Length; i++)
        {
            if (!BitEquals(forward[i], backward[i])) orderMismatch++;
        }

        Check(orderMismatch == 0,
            "GSA2：调用顺序不影响结果（逆序重算，不一致 " + orderMismatch + "）");

        // 交错其它采样后再回头重算，仍必须一致
        for (int i = 0; i < 64; i++)
        {
            GeologyNoise.Value3D(i * 7, -i * 3, i * 11, 999);
            GeologyNoise.Value2D(i, -i, 999);
        }

        int interleavedMismatch = 0;
        for (int i = 0; i < forward.Length; i++)
        {
            float again = GeologyNoise.Value3D(
                coordinates[i][0], coordinates[i][1], coordinates[i][2], 12345);
            if (!BitEquals(forward[i], again)) interleavedMismatch++;
        }

        Check(interleavedMismatch == 0,
            "GSA2：交错采样后重算仍逐位一致（不一致 " + interleavedMismatch + "）");

        // 非 2 的幂格胞半径会被规整 ⇒ 规整后的两种写法结果一致
        Check(BitEquals(GeologyNoise.Value3D(37, -21, 5, 7, 8),
                        GeologyNoise.Value3D(37, -21, 5, 7, 9)),
            "GSA2：非 2 的幂格胞（9）就近规整为 8（结果与 cell=8 一致）");

        Check(GeologyNoise.ClampCellVoxels(0) == 1 &&
              GeologyNoise.ClampCellVoxels(1) == 1 &&
              GeologyNoise.ClampCellVoxels(3) == 2 &&
              GeologyNoise.ClampCellVoxels(9) == 8 &&
              GeologyNoise.ClampCellVoxels(12) == 8 &&
              GeologyNoise.ClampCellVoxels(16) == 16 &&
              GeologyNoise.ClampCellVoxels(24) == 16 &&
              GeologyNoise.ClampCellVoxels(4096) == 256,
            "GSA2：ClampCellVoxels 就近取 2 的幂且平局取小值" +
            "（0→1 / 3→2 / 9→8 / 12→8 / 16→16 / 24→16 / 4096→256；实际 " +
            GeologyNoise.ClampCellVoxels(0) + " / " + GeologyNoise.ClampCellVoxels(3) + " / " +
            GeologyNoise.ClampCellVoxels(9) + " / " + GeologyNoise.ClampCellVoxels(12) + " / " +
            GeologyNoise.ClampCellVoxels(16) + " / " + GeologyNoise.ClampCellVoxels(24) + " / " +
            GeologyNoise.ClampCellVoxels(4096) + "）");
    }

    // =====================================================================
    // GSA3：不同 seed 输出差异
    // =====================================================================

    private static void ValidateSeedSensitivity()
    {
        int[] seeds = { 0, 1, 2, 12345, 54321 };
        int samples = 0;
        int differentPairs = 0;
        int totalPairs = 0;

        for (int s = 0; s < seeds.Length; s++)
        {
            float[] values = new float[64];
            for (int i = 0; i < 64; i++)
            {
                values[i] = GeologyNoise.Value3D(i * 3, i - 32, i * 5, seeds[s]);
            }

            samples += values.Length;

            // 与后续 seed 逐一比较（相同坐标、不同 seed）
            for (int t = s + 1; t < seeds.Length; t++)
            {
                for (int i = 0; i < values.Length; i++)
                {
                    totalPairs++;
                    float other = GeologyNoise.Value3D(i * 3, i - 32, i * 5, seeds[t]);
                    if (!BitEquals(values[i], other)) differentPairs++;
                }
            }
        }

        Check(samples > 0 && totalPairs > 0, "GSA3：存在可比较样本（" + samples + " 点）");
        Check(differentPairs > 0,
            "GSA3：不同 seed 输出存在差异（" + differentPairs + " / " + totalPairs + " 对不同）");

        double ratio = totalPairs > 0 ? (double)differentPairs / totalPairs : 0.0;
        Check(ratio > 0.99,
            "GSA3：不同 seed 几乎处处不同（比例 " + ratio.ToString("F4") + "，期望 > 0.99）");

        // 反例保护：同一 seed 下不同坐标也必须产生不同值（否则可能是常量函数）
        int sameSeedDistinct = 0;
        float reference = GeologyNoise.Value3D(0, 0, 0, 4242);
        for (int i = 1; i <= 32; i++)
        {
            if (!BitEquals(GeologyNoise.Value3D(i, i * 2, -i, 4242), reference))
            {
                sameSeedDistinct++;
            }
        }

        Check(sameSeedDistinct == 32,
            "GSA3：同一 seed 下不同坐标输出不同（" + sameSeedDistinct + " / 32）");

        // 2D 同样对 seed 敏感
        int twoDDifferent = 0;
        for (int i = 0; i < 32; i++)
        {
            if (!BitEquals(GeologyNoise.Value2D(i * 5, -i * 3, 1),
                           GeologyNoise.Value2D(i * 5, -i * 3, 2)))
            {
                twoDDifferent++;
            }
        }

        Check(twoDDifferent > 0, "GSA3：2D 噪声对 seed 敏感（" + twoDDifferent + " / 32）");
    }

    // =====================================================================
    // GSA4：相邻坐标变化有限（连续性）
    // =====================================================================

    private static void ValidateContinuity()
    {
        int[] cells = { 1, 4, 16, 64 };
        int[] cellFrequencies = { 64, 16, 4, 1 };   // 频率 → 格胞（64/frequency）

        for (int c = 0; c < cells.Length; c++)
        {
            int cell = cells[c];
            int frequency = cellFrequencies[c];

            Check(GeologyNoise.CellVoxelsForFrequency(frequency) == cell,
                "GSA4：frequency=" + frequency + " ⇒ 格胞 " + cell + " 体素");

            float bound = GeologyNoise.MaxAdjacentDelta3D(cell);
            float worst = 0f;
            float minValue = float.MaxValue;
            float maxValue = float.MinValue;
            int outOfRange = 0;
            int exceeded = 0;
            int evaluated = 0;

            for (int x = -24; x < 24; x++)
            {
                for (int y = -8; y < 8; y++)
                {
                    for (int z = -24; z < 24; z++)
                    {
                        float v = GeologyNoise.Value3D(x, y, z, 20240607, cell);

                        evaluated++;
                        if (v < minValue) minValue = v;
                        if (v > maxValue) maxValue = v;
                        if (v < -1f || v > 1f) outOfRange++;

                        float dx = Math.Abs(GeologyNoise.Value3D(x + 1, y, z, 20240607, cell) - v);
                        float dy = Math.Abs(GeologyNoise.Value3D(x, y + 1, z, 20240607, cell) - v);
                        float dz = Math.Abs(GeologyNoise.Value3D(x, y, z + 1, 20240607, cell) - v);

                        if (dx > worst) worst = dx;
                        if (dy > worst) worst = dy;
                        if (dz > worst) worst = dz;

                        float slack = 1e-5f;
                        if (dx > bound + slack || dy > bound + slack || dz > bound + slack)
                        {
                            exceeded++;
                        }
                    }
                }
            }

            Check(outOfRange == 0,
                "GSA4：cell=" + cell + " 输出落在 [-1,1]（越界 " + outOfRange +
                "，实际范围 [" + minValue.ToString("F6") + ", " + maxValue.ToString("F6") + "]）");
            Check(exceeded == 0,
                "GSA4：cell=" + cell + " 相邻差分 ≤ 上界 " + bound.ToString("F6") +
                "（越界 " + exceeded + "，实测最大 " + worst.ToString("F6") + "）");
            Check(worst > 0f,
                "GSA4：cell=" + cell + " 噪声确实在变化（非平凡常量，实测最大差分 " +
                worst.ToString("F6") + "）");
            Check(evaluated > 10000,
                "GSA4：cell=" + cell + " 采样覆盖充分（" + evaluated + " 点）");

            Debug.Log("[GeologyStageAValidation] cell=" + cell +
                      "：最大相邻差分 " + worst.ToString("F6") + "，上界 " +
                      bound.ToString("F6") + "，范围 [" +
                      minValue.ToString("F6") + ", " + maxValue.ToString("F6") + "]");
        }

        // 二维连续性
        for (int c = 0; c < cells.Length; c++)
        {
            int cell = cells[c];
            float bound = GeologyNoise.MaxAdjacentDelta2D(cell);
            float worst = 0f;
            int exceeded = 0;

            for (int x = -32; x < 32; x++)
            {
                for (int z = -32; z < 32; z++)
                {
                    float v = GeologyNoise.Value2D(x, z, 777, cell);

                    float dx = Math.Abs(GeologyNoise.Value2D(x + 1, z, 777, cell) - v);
                    float dz = Math.Abs(GeologyNoise.Value2D(x, z + 1, 777, cell) - v);

                    if (dx > worst) worst = dx;
                    if (dz > worst) worst = dz;

                    float slack = 1e-5f;
                    if (dx > bound + slack || dz > bound + slack) exceeded++;
                }
            }

            Check(exceeded == 0,
                "GSA4：2D cell=" + cell + " 相邻差分 ≤ 上界 " + bound.ToString("F6") +
                "（越界 " + exceeded + "，实测最大 " + worst.ToString("F6") + "）");
        }

        // 格胞越大越平滑：同等跨度下最大差分应单调不增
        float previousWorst = float.MaxValue;
        int nonMonotonic = 0;

        for (int c = 0; c < cells.Length; c++)
        {
            float worst = 0f;
            for (int x = 0; x < 64; x++)
            {
                float v = GeologyNoise.Value2D(x, 0, 31337, cells[c]);
                float d = Math.Abs(GeologyNoise.Value2D(x + 1, 0, 31337, cells[c]) - v);
                if (d > worst) worst = d;
            }

            if (worst > previousWorst + 1e-6f) nonMonotonic++;
            previousWorst = worst;
        }

        Check(nonMonotonic == 0,
            "GSA4：格胞越大越平滑（最大差分随 cell 单调不增，违反 " + nonMonotonic + " 次）");
    }

    // =====================================================================
    // GSA5：无 UnityEngine 依赖扫描（+ Stage A 结构纪律）
    // =====================================================================

    /// <summary>
    /// **目录级禁令**：作用于 `Generation/Geology/` 下的**全部** `*.cs`（含接入层）。
    /// 只包含"运行时污染项"：Unity 依赖、非确定性来源、Runtime / Streaming 调度类型、
    /// Entity / 预制体 / 实例化 API、几何类型。
    /// </summary>
    private static readonly string[] DirectoryForbiddenPatterns =
    {
        // Unity 依赖（用正则避免误伤同名字段，例如 GeologySample.Material）
        "UnityEngine", "UnityEditor", "MonoBehaviour", "GameObject", "Transform",
        @"\bVector2\b", @"\bVector3\b", @"new Material\(", @"\bRandom\b", @"\bMathf\b",
        @"Time\.", "DateTime", @"\bGuid\b",
        // Runtime / Streaming 调度类型
        "VoxelRuntimeCore", "ChunkRecord", "ChunkStreamingDriver",
        "IChunkPresenter", "IChunkMesher", "RuntimeChangeSet",
        // Entity / 预制体 / 实例化
        "CrystalCluster", "Instantiate", "Prefab", "SpawnEntity",
        // 几何
        "VoxelMeshGenerator", "VoxelMeshData"
    };

    /// <summary>
    /// **采样层专属禁令**：只作用于**采样层文件**（见 <see cref="SamplingLayerFiles"/>）。
    /// 采样层不得引用 Chunk 数据容器 / Chunk API / 世界成员，也不得触碰脏标记。
    ///
    /// **接入层（`GeologyWorldGenerator.cs`）不适用本表**：它的职责就是产出 `VoxelChunk`，
    /// 必然出现 `VoxelChunk`（返回类型）/ `SetVoxel`（唯一写入口）/ `chunkCoordinate`（契约参数名）；
    /// 它另有专门的禁令集（见 GeologyStageDValidation 的 GW7/GW8）约束"不得访问世界"。
    /// </summary>
    private static readonly string[] SamplingLayerForbiddenPatterns =
    {
        // Chunk 数据容器
        "VoxelChunk",
        // Chunk 坐标（用词边界 + 小写参数名，避免误伤方法名 WorldToChunkCoordinate）
        @"\bchunkCoordinate\b", @"\bchunkX\b", @"\bchunkY\b", @"\bchunkZ\b",
        // 数据写入 / 脏标记
        "SetVoxel", "MarkDirty", "ClearDirty",
        // Chunk API（容器/换算/生命周期）
        "GetOrCreateChunk", "RemoveChunk", "TryGetChunk", "CollectDirtyChunks",
        "WorldToChunkCoordinate", "LocalToWorldCoordinate", "ChunkSize", "VoxelCount",
        // 世界成员访问（本族文件自身的 namespace VoxelWorld.Generation.Geology 不算违规）
        @"new VoxelWorld\b", @"VoxelWorld\.(?!Generation)"
    };

    /// <summary>
    /// 采样层文件白名单（<see cref="SamplingLayerForbiddenPatterns"/> 只对这些文件生效）。
    /// 新增的**接入层**（生成器）或其它非采样文件默认只受目录级禁令约束。
    /// </summary>
    private static readonly string[] SamplingLayerFiles =
    {
        "GeologySettings.cs", "GeologyNoise.cs", "GeologySample.cs", "GeologySampler.cs",
        "IDepositProvider.cs", "IBiomeProvider.cs",
        "GeologyDepositProvider.cs", "GeologyBiomeProvider.cs"
    };

    /// <summary>接入层文件（需要且允许出现 `VoxelChunk` / `SetVoxel` / `chunkCoordinate`）。</summary>
    private const string GeneratorFile = "GeologyWorldGenerator.cs";

    /// <summary>判断某文件是否属于采样层（需应用采样层专属禁令）。</summary>
    private static bool IsSamplingLayerFile(string fileName)
    {
        for (int i = 0; i < SamplingLayerFiles.Length; i++)
        {
            if (string.Equals(fileName, SamplingLayerFiles[i], StringComparison.Ordinal)) return true;
        }

        return false;
    }

    private static void ValidateStructuralScan()
    {
        string folder = Path.Combine(Application.dataPath, GeologyFolder);
        Check(Directory.Exists(folder), "GSA5：Geology 目录存在（" + folder + "）");
        if (!Directory.Exists(folder)) return;

        string[] files = Directory.GetFiles(folder, "*.cs", SearchOption.TopDirectoryOnly);
        Array.Sort(files, StringComparer.Ordinal);

        // 核心文件存在检查（**不固定文件总数**：允许后续阶段在同一目录追加文件，
        // 例如 Stage B 的 GeologySampler.cs、Stage C 的 provider 等）
        Check(files.Length >= 3,
            "GSA5：地质目录至少含 3 个核心文件（实际 " + files.Length +
            "；允许未来扩展）");

        foreach (string expected in new string[]
        {
            "GeologySettings.cs", "GeologyNoise.cs", "GeologySample.cs"
        })
        {
            Check(File.Exists(Path.Combine(folder, expected)), "GSA5：存在 " + expected);
        }

        // ---- 扫描器自检（两层禁令各一份）----
        string probe = "using UnityEngine;\n// UnityEngine 在注释里不计\nvoid P(){ }\n";
        Check(ScanTokens(StripComments(probe), DirectoryForbiddenPatterns).Count == 1,
            "GSA5：目录级扫描器自检（代码区命中 1）");

        string commentProbe = "// UnityEngine Random VoxelChunk\n/* Mathf Time. */\nvoid Q(){ }\n";
        Check(ScanTokens(StripComments(commentProbe), DirectoryForbiddenPatterns).Count == 0 &&
              ScanTokens(StripComments(commentProbe), SamplingLayerForbiddenPatterns).Count == 0,
            "GSA5：两层扫描器都忽略注释");

        // 自检：namespace 声明中的 VoxelWorld 不得被误判为"世界数据访问"
        string namespaceProbe = "namespace VoxelWorld.Generation.Geology { }\n";
        Check(ScanTokens(StripComments(namespaceProbe), SamplingLayerForbiddenPatterns).Count == 0,
            "GSA5：namespace VoxelWorld.Generation.Geology 不算违规（正向对照）");

        string worldAccessProbe = "void R(){ VoxelWorld.WorldToChunkCoordinate(default); }\n";
        Check(ScanTokens(StripComments(worldAccessProbe), SamplingLayerForbiddenPatterns).Count >= 1,
            "GSA5：采样层的 VoxelWorld.<成员> 访问会被检出（反例自检）");

        string chunkApiProbe = "void S(){ var c = new VoxelChunk(default); c.SetVoxel(0,0,0,VoxelType.Air); }\n";
        Check(ScanTokens(StripComments(chunkApiProbe), SamplingLayerForbiddenPatterns).Count >= 2,
            "GSA5：采样层的 VoxelChunk / SetVoxel 会被检出（反例自检）");

        // ---- ① 目录级扫描：全部文件（含接入层）----
        int directoryHits = 0;
        for (int i = 0; i < files.Length; i++)
        {
            string code = StripComments(File.ReadAllText(files[i], Encoding.UTF8));
            List<string> found = ScanTokens(code, DirectoryForbiddenPatterns);

            if (found.Count > 0)
            {
                directoryHits += found.Count;
                Check(false, "GSA5：" + Path.GetFileName(files[i]) +
                             " 命中目录级禁止项（运行时污染）：" +
                             string.Join(", ", found.ToArray()));
            }
        }

        Check(directoryHits == 0,
            "GSA5：地质目录全部 " + files.Length +
            " 个文件不含 Unity 依赖 / 非确定性来源 / Runtime / Entity / 几何类型（命中 " +
            directoryHits + "）");

        // ---- ② 采样层专属扫描：只作用于采样层文件 ----
        int samplingFilesScanned = 0;
        int samplingHits = 0;

        for (int i = 0; i < files.Length; i++)
        {
            string fileName = Path.GetFileName(files[i]);
            if (!IsSamplingLayerFile(fileName)) continue;

            samplingFilesScanned++;

            string code = StripComments(File.ReadAllText(files[i], Encoding.UTF8));
            List<string> found = ScanTokens(code, SamplingLayerForbiddenPatterns);

            if (found.Count > 0)
            {
                samplingHits += found.Count;
                Check(false, "GSA5：" + fileName + " 命中采样层禁止项：" +
                             string.Join(", ", found.ToArray()));
            }
        }

        Check(samplingFilesScanned == SamplingLayerFiles.Length,
            "GSA5：采样层文件齐备（" + samplingFilesScanned + " / " +
            SamplingLayerFiles.Length + "）");
        Check(samplingHits == 0,
            "GSA5：采样层（" + samplingFilesScanned +
            " 个文件）仍然不引用 Chunk 容器 / Chunk API / 世界成员 / 脏标记（命中 " +
            samplingHits + "）");

        // ---- ③ 接入层豁免与正向对照 ----
        string generatorPath = Path.Combine(folder, GeneratorFile);
        Check(File.Exists(generatorPath), "GSA5：接入层文件存在（" + GeneratorFile + "）");
        Check(!IsSamplingLayerFile(GeneratorFile),
            "GSA5：" + GeneratorFile + " 不应用采样层专属禁令（作用域正确）");

        if (File.Exists(generatorPath))
        {
            string generatorCode = StripComments(File.ReadAllText(generatorPath, Encoding.UTF8));

            // 接入层允许（且必需）出现这三项
            Check(generatorCode.Contains("VoxelChunk"),
                "GSA5：接入层允许使用 VoxelChunk（返回类型；正向对照）");
            Check(generatorCode.Contains("SetVoxel"),
                "GSA5：接入层允许使用 SetVoxel（唯一写入口；正向对照）");
            Check(generatorCode.Contains("chunkCoordinate"),
                "GSA5：接入层允许使用 chunkCoordinate（契约参数名；正向对照）");

            // 但接入层仍受目录级禁令约束（且不得访问世界：由 Stage D 的 GW7/GW8 精查）
            List<string> generatorDirectoryHits =
                ScanTokens(generatorCode, DirectoryForbiddenPatterns);
            Check(generatorDirectoryHits.Count == 0,
                "GSA5：接入层仍不得出现运行时污染项（命中 " +
                generatorDirectoryHits.Count + "）");

            Check(!generatorCode.Contains("GetOrCreateChunk") &&
                  !generatorCode.Contains("RemoveChunk"),
                "GSA5：接入层不得访问世界（无 GetOrCreateChunk / RemoveChunk；接入层精查见 GW8）");
        }

        // ---- ④ 静态可变状态扫描（全部文件）----
        int mutableStatic = 0;
        for (int i = 0; i < files.Length; i++)
        {
            string[] lines = StripComments(File.ReadAllText(files[i], Encoding.UTF8))
                .Replace("\r\n", "\n").Split('\n');

            for (int l = 0; l < lines.Length; l++)
            {
                string line = lines[l].Trim();

                if (line.Length == 0) continue;
                if (line.IndexOf("static", StringComparison.Ordinal) < 0) continue;
                if (line.IndexOf("const", StringComparison.Ordinal) >= 0) continue;
                if (line.IndexOf("readonly", StringComparison.Ordinal) >= 0) continue;
                if (line.IndexOf('(') >= 0) continue;                              // 方法 / 构造
                if (line.IndexOf("=>", StringComparison.Ordinal) >= 0) continue;    // 表达式体成员
                if (Regex.IsMatch(line, @"\b(class|struct|interface|enum|delegate)\b"))
                    continue;                                                      // 类型声明
                if (line.EndsWith("{", StringComparison.Ordinal)) continue;         // 块声明（属性/类）

                // 属性块：声明行本身不含 `{`，但紧随其后（下一非空行）是 `{`
                if (NextNonEmptyLineStartsWithBrace(lines, l)) continue;

                mutableStatic++;
                Check(false, "GSA5：" + Path.GetFileName(files[i]) +
                             " 存在静态可变字段候选：L" + (l + 1) + " " + line);
            }
        }

        Check(mutableStatic == 0,
            "GSA5：无静态可变字段（禁止隐藏全局状态，命中 " + mutableStatic + "）");

        // 正向对照
        string noiseCode = StripComments(
            File.ReadAllText(Path.Combine(folder, "GeologyNoise.cs"), Encoding.UTF8));
        Check(noiseCode.Contains("Value3D") && noiseCode.Contains("Value2D"),
            "GSA5：GeologyNoise 提供 Value3D / Value2D（正向对照）");
        Check(noiseCode.Contains("MaxAdjacentDelta3D"),
            "GSA5：GeologyNoise 暴露相邻差分上界（供 GSA4 使用）");

        string settingsCode = StripComments(
            File.ReadAllText(Path.Combine(folder, "GeologySettings.cs"), Encoding.UTF8));
        Check(settingsCode.Contains("IsValid") && settingsCode.Contains("Default"),
            "GSA5：GeologySettings 提供 IsValid / Default（正向对照）");
        Check(settingsCode.Contains("serializable") || settingsCode.Contains("Serializable"),
            "GSA5：GeologySettings 标注 [Serializable]（纯数据结构，正向对照）");

        string sampleCode = StripComments(
            File.ReadAllText(Path.Combine(folder, "GeologySample.cs"), Encoding.UTF8));
        Check(sampleCode.Contains("GeologyRule") && sampleCode.Contains("GeologyDepositZone") &&
              sampleCode.Contains("GeologyBiome"),
            "GSA5：GeologySample 提供规则枚举 + Deposit/Biome 占位（正向对照）");

        Debug.Log("[GeologyStageAValidation] 扫描范围：\n" + string.Join("\n", files));
    }

    private static List<string> ScanTokens(string code, string[] patterns)
    {
        List<string> hits = new List<string>();

        for (int i = 0; i < patterns.Length; i++)
        {
            if (Regex.IsMatch(code, patterns[i])) hits.Add(patterns[i]);
        }

        return hits;
    }

    /// <summary>属性/类的块声明：声明行不含 `{`，但下一非空行以 `{` 开头。</summary>
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
    // GSA6：零修改（既有文件 SHA 不变）
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
            // Stage E 授权修改：世界生成组装点换用 GeologyWorldGenerator（哈希已重钉）
            { "Scripts/Game/VoxelWorldStreamingBootstrap.cs", "4738634952568520" },
            // 既有 Editor 验证入口
            { "Editor/VoxelWorldCoreValidation.cs", "6DE813AABCE95394" },
            { "Editor/WorldGenerationCoreValidation.cs", "4A517A0F902A1066" },
            { "Editor/VoxelMeshCoreValidation.cs", "5FFD8451D2CE05EB" },
            { "Editor/VoxelWorldViewValidation.cs", "BEFBAAF6B111A241" },
            { "Editor/VoxelWorldRuntimeCoreValidation.cs", "0E0B5C160FEAB236" },
            { "Editor/ChunkStreamingValidation.cs", "13E244B6EE978FC8" }
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
                    Check(false, "GSA6：缺失 " + pair.Key);
                    continue;
                }

                string specificationHash = ComputeFileHash16(specificationPath);
                if (specificationHash == null) continue;

                compared++;
                if (specificationHash != pair.Value)
                {
                    mismatch++;
                    Check(false, "GSA6：" + pair.Key + " 已改变（" + specificationHash +
                                 " != " + pair.Value + "）");
                }

                continue;
            }

            string path = Path.Combine(Application.dataPath, pair.Key);
            if (!File.Exists(path))
            {
                missing++;
                Check(false, "GSA6：缺失 " + pair.Key);
                continue;
            }

            string hash = ComputeFileHash16(path);
            if (hash == null) continue;

            compared++;
            if (hash != pair.Value)
            {
                mismatch++;
                Check(false, "GSA6：" + pair.Key + " 已改变（" + hash + " != " + pair.Value + "）");
            }
        }

        Check(missing == 0, "GSA6：既有文件全部存在（缺失 " + missing + "）");
        Check(mismatch == 0,
            "GSA6：既有文件 SHA256(前16) 全部不变（比对 " + compared + " 个；跳过 " + skipped + "）");
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
            Check(false, "GSA6：无法计算哈希（" + path + "）：" + ex.Message);
            return null;
        }
    }

    // =====================================================================
    // GSA7：采样结果描述结构
    // =====================================================================

    private static void ValidateSampleStruct()
    {
        GeologySample empty = GeologySample.Empty;
        Check(empty.Rule == GeologyRule.Unresolved && !empty.IsSolid &&
              empty.Material == VoxelType.Air &&
              empty.DepositZone == GeologyDepositZone.None &&
              empty.Biome == GeologyBiome.None,
            "GSA7：Empty 是全默认（" + empty + "）");
        Check(empty.IsConsistent(), "GSA7：Empty 自洽");

        // 实体样本
        GeologySample stone = new GeologySample
        {
            Rule = GeologyRule.Deep,
            IsSolid = true,
            Material = VoxelType.Stone,
            SurfaceHeight = 24,
            SoilDepth = 3,
            CaveActive = false,
            CaveDensity = -0.4f
        };

        Check(stone.IsConsistent(), "GSA7：深层 Stone 样本自洽（" + stone + "）");
        Check(stone.ToString().Contains("Deep") && stone.ToString().Contains("Stone"),
            "GSA7：ToString 含规则与材料（" + stone + "）");

        // 洞穴样本（P1 覆盖岩石）
        GeologySample cave = new GeologySample
        {
            Rule = GeologyRule.Cave,
            IsSolid = false,
            Material = VoxelType.Air,
            SurfaceHeight = 24,
            SoilDepth = 3,
            CaveActive = true,
            CaveDensity = 0.9f
        };

        Check(cave.IsConsistent(), "GSA7：洞穴样本自洽（空 + CaveActive）");

        // 不自洽的样本必须被识别（验证结构能支撑后续阶段的断言）
        GeologySample inconsistentSolid = cave;
        inconsistentSolid.IsSolid = true;          // Cave 却声称实体 ⇒ 自洽性应失败
        Check(!inconsistentSolid.IsConsistent(),
            "GSA7：Rule=Cave 却 IsSolid=true 被判为不自洽");

        GeologySample inconsistentMaterial = stone;
        inconsistentMaterial.Material = VoxelType.Air;   // 实体却是 Air ⇒ 失败
        Check(!inconsistentMaterial.IsConsistent(),
            "GSA7：实体却 Air 被判为不自洽");

        GeologySample inconsistentRule = stone;
        inconsistentRule.Rule = GeologyRule.AboveSurface; // AboveSurface 却实体 ⇒ 失败
        Check(!inconsistentRule.IsConsistent(),
            "GSA7：AboveSurface 却实体被判为不自洽");

        // 枚举取值空间（占位必须存在但只有占位语义）
        Check(GeologyRule.WorldBounds == (GeologyRule)1 &&
              GeologyRule.Cave == (GeologyRule)2 &&
              GeologyRule.AboveSurface == (GeologyRule)3 &&
              GeologyRule.Surface == (GeologyRule)4 &&
              GeologyRule.Depth == (GeologyRule)5 &&
              GeologyRule.Deep == (GeologyRule)6,
            "GSA7：GeologyRule 取值顺序 == 优先级顺序（WorldBounds < Cave < AboveSurface < 地层）");
        Check((byte)GeologyDepositZone.None == 0 &&
              (byte)GeologyDepositZone.ZoneA == 1 &&
              (byte)GeologyDepositZone.ZoneB == 2,
            "GSA7：Deposit 占位取值空间（None=0 / ZoneA=1 / ZoneB=2，无具体矿种语义）");
        Check((byte)GeologyBiome.None == 0 &&
              (byte)GeologyBiome.BiomeA == 1 &&
              (byte)GeologyBiome.BiomeB == 2,
            "GSA7：Biome 占位取值空间（None=0 / BiomeA=1 / BiomeB=2，无具体群系语义）");

        // 结构本身不产生副作用：两次读取 Empty 都得到默认值
        GeologySample again = GeologySample.Empty;
        Check(again.Rule == GeologyRule.Unresolved && again.SurfaceHeight == 0,
            "GSA7：Empty 无副作用（重复读取一致）");
    }

    // =====================================================================
    // 工具
    // =====================================================================

    private static bool BitEquals(float a, float b)
    {
        return BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);
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
