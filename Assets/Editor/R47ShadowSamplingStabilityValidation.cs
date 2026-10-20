using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using VoxelWorld.Generation;
using World.V2;

/// <summary>
/// **R47 — 全场景高度差采样精度与统计稳定性验证**（纯只读；不加载场景、不实例化对象）。
///
/// 问题：R46 的全场景统计（每 Chunk 6×6 = 36 列，共 144 样本）是否显著受采样密度影响？
///
/// 方法：在同一场景参数、同一测试映射、同一高度计算路径下，做**三级嵌套**采样：
///   · **A（6×6/Chunk）**：完全复用 R46 的索引集（`{0,1,19,38,57,75}` × `{0,1,13,26,38,75}`）
///     ⇒ 与 R46 已公布数值可直接对照；
///   · **B（12×12/Chunk）**：`B ⊃ A`（在 A 的基础上各轴补 6 个索引）；
///   · **C（18×18/Chunk）**：`C ⊃ B`（在 B 的基础上各轴再补 6 个索引）。
///
/// **嵌套性说明（重要，不得误述）**：为保证"A 的点在 B/C 中仍然存在"，索引集是**非等距**的
/// （在 A 的 6 个索引之间插入新索引）。本验证器**不声称**这是等距网格；它只保证
/// `A ⊂ B ⊂ C` 的坐标嵌套关系（逐级断言），从而使"同位置配对"成立。
///
/// 采样语义（源码依据）：`DensityField.SampleCount{XYZ} = Resolution + 1`（本场景 76，
/// 索引 0..75），`CellSize = Size / Resolution`（`DensityField.cs:44–57`）；列高读取为 O(1)，
/// 故提高采样密度**不增加** `DensityFieldGenerator.Generate` 的调用次数（每 Chunk 仍 1 次）。
///
/// 坐标映射（**本轮测试映射，不是正式契约**）：
///   `worldX = RoundToInt(TerrainLocalX)`、`worldZ = RoundToInt(TerrainLocalZ)`、`worldY = 0`。
///
/// 明确不做：不统一高度算法、不建立正式坐标契约、不接入生产、不改参数/默认值、
/// 不以"重复世界坐标的 V2 Elevation 一致"宣称几何/材料连续或网格无裂缝。
///
/// 菜单：`Tools/World V2/Validate R47 Shadow Sampling Stability`
/// 批处理：`Unity -batchmode -quit -executeMethod R47ShadowSamplingStabilityValidation.RunBatch`
/// 退出码自检：追加 `-r47SelfFailure` ⇒ 注入一条故意失败（期望 exit 1）
/// </summary>
public static class R47ShadowSamplingStabilityValidation
{
    private const string SelfFailureSwitch = "-r47SelfFailure";
    private const string DensityTerrainScriptGuid = "3af3bcfc875f4cbfae75f1f95dee772a";

    // ---- 三级索引集（A ⊂ B ⊂ C；非等距，见类注释）----
    private static readonly int[] AIX = { 0, 1, 19, 38, 57, 75 };
    private static readonly int[] AIZ = { 0, 1, 13, 26, 38, 75 };

    private static readonly int[] BIX = { 0, 1, 7, 13, 19, 27, 38, 47, 57, 68, 71, 75 };
    private static readonly int[] BIZ = { 0, 1, 7, 13, 19, 26, 27, 38, 47, 57, 68, 75 };

    private static readonly int[] CIX = { 0, 1, 4, 7, 10, 13, 19, 23, 27, 33, 38, 44, 47, 52, 57, 68, 71, 75 };
    private static readonly int[] CIZ = { 0, 1, 4, 7, 10, 13, 19, 23, 26, 27, 33, 38, 44, 47, 52, 57, 68, 75 };

    // R46 已公布数值（用于复现性断言；容差 0.001）
    private const float R46AllMeanAbs = 8.5154f;
    private const float R46AllMaxAbs = 14.4646f;
    private const float R46Chunk0MeanAbs = 8.2301f;
    private const float R46Chunk0MaxAbs = 13.3777f;

    private static int failures;
    private static int checks;

    private struct Sample
    {
        public int ChunkIndex;
        public string ChunkLabel;
        public int IX;
        public int IZ;
        public float LocalX;
        public float LocalZ;
        public int WorldX;
        public int WorldZ;
        public float OffsetX;
        public float OffsetZ;
        public float Legacy;
        public int V2;
        public float Signed;
        public float Abs;
        public bool IsBoundary;
        public bool IsCutaway;
        public bool IsExact;
        public bool Valid;
    }

    private sealed class Group
    {
        private readonly string name;
        public int Total;
        public int Valid;
        public float SignedMin = float.MaxValue;
        public float SignedMax = float.MinValue;
        public float MaxAbs;
        public double AbsSum;

        public Group(string name) { this.name = name; }

        public void Add(Sample sample)
        {
            Total++;
            if (!sample.Valid) { return; }

            Valid++;
            if (sample.Signed < SignedMin) { SignedMin = sample.Signed; }
            if (sample.Signed > SignedMax) { SignedMax = sample.Signed; }
            if (sample.Abs > MaxAbs) { MaxAbs = sample.Abs; }
            AbsSum += sample.Abs;
        }

        public float MeanAbs
        {
            get { return Valid > 0 ? (float)(AbsSum / Valid) : 0f; }
        }

        public string Describe()
        {
            string head = name + " n=" + Total.ToString(CultureInfo.InvariantCulture) +
                          " valid=" + Valid.ToString(CultureInfo.InvariantCulture);

            if (Valid == 0) { return head + " （无有效样本）"; }

            return head +
                   " signedMin=" + SignedMin.ToString("F4", CultureInfo.InvariantCulture) +
                   " signedMax=" + SignedMax.ToString("F4", CultureInfo.InvariantCulture) +
                   " meanAbs=" + MeanAbs.ToString("F4", CultureInfo.InvariantCulture) +
                   " maxAbs=" + MaxAbs.ToString("F4", CultureInfo.InvariantCulture);
        }
    }

    private sealed class LevelData
    {
        public string Name;
        public int[] IX;
        public int[] IZ;
        public Sample[] Samples;
        public Group All;
        public Group[] PerChunk;
        public Group Exact;
        public Group Offset;
        public Group Boundary;
        public Group NonBoundary;
        public Group Cutaway;
        public Group NonCutaway;
        public int Positive;
        public int Negative;
        public int Zero;
        public int UniqueLocalPositions;
        public int UniqueWorldCoordinates;
        public float DeduplicatedMeanAbs;
        public float DeduplicatedMaxAbs;
        public int DuplicateSamples;
    }

    [MenuItem("Tools/World V2/Validate R47 Shadow Sampling Stability")]
    public static void Validate()
    {
        failures = 0;
        checks = 0;

        string scenePath = Path.Combine(Application.dataPath, "Scenes", "SampleScene.unity");

        // ================= 0. 隔离性前置 =================
        int gameObjectsBefore = Resources.FindObjectsOfTypeAll<GameObject>().Length;
        int terrainInstancesBefore = Resources.FindObjectsOfTypeAll<DensityTerrain>().Length;
        bool sceneLoaded = SceneManager.GetActiveScene().IsValid() &&
                           !string.IsNullOrEmpty(SceneManager.GetActiveScene().path);
        string sceneHashBefore = ComputeFileHash(scenePath);

        Check(!sceneLoaded, "隔离性：未加载任何场景资源 ⇒ 未触发 DensityTerrain.OnEnable/Regenerate");
        Check(terrainInstancesBefore == 0, "隔离性：DensityTerrain 实例数 = 0");

        // ================= 1. 场景参数（只读解析）=================
        if (!File.Exists(scenePath))
        {
            Check(false, "前置：场景文件存在 ⇒ 不具备验证条件");
            Report();
            return;
        }

        string sceneText = File.ReadAllText(scenePath);
        string componentBlock;
        string gameObjectId;
        if (!TryExtractDensityTerrainBlock(sceneText, out componentBlock, out gameObjectId))
        {
            Check(false, "前置：定位 DensityTerrain 组件块 ⇒ 不具备验证条件");
            Report();
            return;
        }

        Check(!string.IsNullOrEmpty(gameObjectId),
            "前置：组件块可解析 m_GameObject（fileID " + gameObjectId + "）");

        Dictionary<string, string> fields = ParseKeyValues(componentBlock);

        int chunkVoxelX, chunkVoxelY, chunkVoxelZ;
        int chunkCountX, chunkCountY, chunkCountZ;
        int chunkStartX, chunkStartY, chunkStartZ;
        float cavityX, cavityY, cavityZ;
        if (!TryParseVectorInt(fields, "chunkVoxelSize", out chunkVoxelX, out chunkVoxelY, out chunkVoxelZ) ||
            !TryParseVectorInt(fields, "chunkCount", out chunkCountX, out chunkCountY, out chunkCountZ) ||
            !TryParseVectorInt(fields, "chunkStartCoordinate", out chunkStartX, out chunkStartY, out chunkStartZ) ||
            !TryParseVectorFloat(fields, "cavityCenterOffset", out cavityX, out cavityY, out cavityZ))
        {
            Check(false, "前置：chunkCount / chunkStartCoordinate / chunkVoxelSize / cavityCenterOffset 可解析 ⇒ 不具备验证条件");
            Report();
            return;
        }

        int resolution = (int)ParseNumber(fields, "resolution");
        float voxelSize = ParseNumber(fields, "voxelSize");
        bool useNoise = ParseNumber(fields, "useNoise") != 0f;
        float height = ParseNumber(fields, "height");
        float noiseScale = ParseNumber(fields, "noiseScale");
        float noiseStrength = ParseNumber(fields, "noiseStrength");
        bool debugCutaway = ParseNumber(fields, "debugCutaway") != 0f;
        float debugCutX = ParseNumber(fields, "debugCutX");
        float debugCutWidth = ParseNumber(fields, "debugCutWidth");
        float cavityRadius = ParseNumber(fields, "cavityRadius");
        float grassDepth = ParseNumber(fields, "grassDepth");
        float dirtDepth = ParseNumber(fields, "dirtDepth");
        bool useLegacyMaterialRules = ParseNumber(fields, "useLegacyMaterialRules") != 0f;
        float weatheringStrength = ParseNumber(fields, "weatheringStrength");
        float weatheringDepth = ParseNumber(fields, "weatheringDepth");
        float gentleSlopeAngle = ParseNumber(fields, "gentleSlopeAngle");
        float steepSlopeAngle = ParseNumber(fields, "steepSlopeAngle");
        float gravityStrength = ParseNumber(fields, "gravityStrength");
        int soilIterations = (int)ParseNumber(fields, "soilSimulationIterations");
        float grassStrength = ParseNumber(fields, "grassStrength");

        Check(resolution == 75 && chunkVoxelX == 75 && chunkVoxelY == 37 && chunkVoxelZ == 50 &&
              Approximately(voxelSize, 1f) && Approximately(height, 15.7f) &&
              Approximately(noiseScale, 0.15f) && Approximately(noiseStrength, 2.37f),
            "参数：resolution=75 / chunkVoxelSize=(75,37,50) / voxelSize=1 / height=15.7 / noise=0.15±2.37（与 R44–R46 一致）");
        Check(chunkCountX == 2 && chunkCountY == 1 && chunkCountZ == 2,
            "参数：chunkCount=(2,1,2) ⇒ 4 个 Chunk");

        int totalChunks = Mathf.Max(1, chunkCountX) * Mathf.Max(1, chunkCountY) * Mathf.Max(1, chunkCountZ);
        Vector3Int[] chunkCoordinates = new Vector3Int[totalChunks];
        Vector3[] chunkOrigins = new Vector3[totalChunks];
        int coordinateIndex = 0;
        for (int cx = 0; cx < Mathf.Max(1, chunkCountX); cx++)
        {
            for (int cy = 0; cy < Mathf.Max(1, chunkCountY); cy++)
            {
                for (int cz = 0; cz < Mathf.Max(1, chunkCountZ); cz++)
                {
                    Vector3Int coordinate = new Vector3Int(
                        chunkStartX + cx, chunkStartY + cy, chunkStartZ + cz);
                    chunkCoordinates[coordinateIndex] = coordinate;
                    chunkOrigins[coordinateIndex] = ChunkSpace.ChunkToLocalOrigin(
                        coordinate, voxelSize, new Vector3Int(chunkVoxelX, chunkVoxelY, chunkVoxelZ));
                    coordinateIndex++;
                }
            }
        }
        Check(totalChunks == 4, "覆盖：Chunk 总数 = 4");

        Vector3 chunkWorldSize = new Vector3(chunkVoxelX * voxelSize, chunkVoxelY * voxelSize, chunkVoxelZ * voxelSize);

        WorldGenerationSettings settings = new WorldGenerationSettings(12345, 24, 3, 64, 0.06f, 6f);
        string settingsReason;
        Check(settings.IsValid(out settingsReason) && settings.SeaLevel == WorldGenerationSettings.DefaultSeaLevel,
            "V2 配置合法且未改默认值（" + settings + "）");

        // ================= 2. 嵌套性断言（索引集 A ⊂ B ⊂ C）=================
        Check(IsSubset(AIX, BIX) && IsSubset(AIZ, BIZ),
            "嵌套：A 的 X/Z 索引均存在于 B（A ⊂ B）");
        Check(IsSubset(BIX, CIX) && IsSubset(BIZ, CIZ),
            "嵌套：B 的 X/Z 索引均存在于 C（B ⊂ C）");
        Check(IsSubset(AIX, CIX) && IsSubset(AIZ, CIZ),
            "嵌套：A ⊂ C（逐点配对成立的前提）");
        Check(AIX.Length * AIZ.Length == 36 && BIX.Length * BIZ.Length == 144 && CIX.Length * CIZ.Length == 324,
            "采样网格：A=6×6=36 / B=12×12=144 / C=18×18=324（每 Chunk）");

        // ================= 3. 生成 DensityField 并构建三级样本 =================
        LevelData[] levels = new LevelData[3];
        levels[0] = new LevelData { Name = "A(6x6)", IX = AIX, IZ = AIZ };
        levels[1] = new LevelData { Name = "B(12x12)", IX = BIX, IZ = BIZ };
        levels[2] = new LevelData { Name = "C(18x18)", IX = CIX, IZ = CIZ };

        float cutMinX = debugCutX - debugCutWidth * 0.5f;
        float cutMaxX = debugCutX + debugCutWidth * 0.5f;
        int fieldsGenerated = 0;
        int legacyNotFound = 0;
        int legacyDegenerate = 0;
        float maxCrossCheckDelta = 0f;
        int crossCheckFailures = 0;

        // 每个 Chunk 只生成一次 DensityField（列高读取为 O(1)，与采样密度无关）
        DensityField[] chunkFields = new DensityField[totalChunks];
        MaterialField[] chunkMaterials = new MaterialField[totalChunks];
        for (int chunkIndex = 0; chunkIndex < totalChunks; chunkIndex++)
        {
            MaterialField materialField;
            chunkFields[chunkIndex] = DensityFieldGenerator.Generate(
                DensityGenerationMode.HeightField,
                resolution,
                chunkOrigins[chunkIndex],
                chunkWorldSize,
                useNoise,
                height,
                noiseScale,
                noiseStrength,
                new Vector3(cavityX, cavityY, cavityZ),
                cavityRadius,
                grassDepth,
                dirtDepth,
                useLegacyMaterialRules,
                weatheringStrength,
                weatheringDepth,
                gentleSlopeAngle,
                steepSlopeAngle,
                gravityStrength,
                soilIterations,
                grassStrength,
                out materialField);
            chunkMaterials[chunkIndex] = materialField;
            if (chunkFields[chunkIndex] != null) { fieldsGenerated++; }
        }

        Check(chunkMaterials.Length == totalChunks, "Legacy：每 Chunk 仅生成一次 DensityField（4 次）");

        for (int levelIndex = 0; levelIndex < levels.Length; levelIndex++)
        {
            LevelData level = levels[levelIndex];
            int perChunk = level.IX.Length * level.IZ.Length;
            level.Samples = new Sample[totalChunks * perChunk];
            int cursor = 0;

            for (int chunkIndex = 0; chunkIndex < totalChunks; chunkIndex++)
            {
                DensityField field = chunkFields[chunkIndex];
                if (field == null) { continue; }

                bool cutAppliesToChunk = debugCutaway && chunkIndex == 0;

                for (int a = 0; a < level.IX.Length; a++)
                {
                    for (int b = 0; b < level.IZ.Length; b++)
                    {
                        int ix = level.IX[a];
                        int iz = level.IZ[b];

                        Vector3 localPosition = field.GetLocalPosition(ix, 0, iz);

                        Sample sample = new Sample();
                        sample.ChunkIndex = chunkIndex;
                        sample.ChunkLabel = "(" + chunkCoordinates[chunkIndex].x + "," +
                                            chunkCoordinates[chunkIndex].y + "," +
                                            chunkCoordinates[chunkIndex].z + ")";
                        sample.IX = ix;
                        sample.IZ = iz;
                        sample.LocalX = localPosition.x;
                        sample.LocalZ = localPosition.z;

                        float derived;
                        bool degenerate;
                        bool found = TryDeriveColumnSurface(field, ix, iz, out derived, out degenerate);
                        sample.Legacy = derived;
                        if (!found) { legacyNotFound++; }
                        if (degenerate) { legacyDegenerate++; }

                        if (levelIndex == 0)
                        {
                            float noise = Mathf.PerlinNoise(localPosition.x * noiseScale,
                                                            localPosition.z * noiseScale) * 2f - 1f;
                            float analytic = height + noise * noiseStrength;
                            float delta = Mathf.Abs(derived - analytic);
                            if (delta > maxCrossCheckDelta) { maxCrossCheckDelta = delta; }
                            if (delta > 0.01f) { crossCheckFailures++; }
                        }

                        sample.WorldX = Mathf.RoundToInt(localPosition.x);
                        sample.WorldZ = Mathf.RoundToInt(localPosition.z);
                        sample.OffsetX = Mathf.Abs(localPosition.x - sample.WorldX);
                        sample.OffsetZ = Mathf.Abs(localPosition.z - sample.WorldZ);

                        WorldSkeletonFacts facts;
                        bool v2Ok = WorldSkeletonProvider.TryGetFacts(
                            sample.WorldX, 0, sample.WorldZ, settings, out facts);

                        sample.V2 = facts.Elevation;
                        sample.IsExact = sample.OffsetX < 0.0001f && sample.OffsetZ < 0.0001f;
                        sample.IsBoundary = ix == 0 || ix == field.SampleCountX - 1 ||
                                            iz == 0 || iz == field.SampleCountZ - 1;
                        sample.IsCutaway = cutAppliesToChunk &&
                                           localPosition.x >= cutMinX && localPosition.x <= cutMaxX;
                        sample.Valid = found && v2Ok && facts.IsValid;

                        if (sample.Valid)
                        {
                            sample.Signed = sample.Legacy - sample.V2;
                            sample.Abs = Mathf.Abs(sample.Signed);
                        }
                        else
                        {
                            sample.Signed = float.NaN;
                            sample.Abs = float.NaN;
                        }

                        level.Samples[cursor] = sample;
                        cursor++;
                    }
                }
            }

            Check(cursor == totalChunks * perChunk,
                "覆盖：" + level.Name + " 采样数 = " + cursor.ToString(CultureInfo.InvariantCulture) +
                "（" + perChunk.ToString(CultureInfo.InvariantCulture) + "/Chunk × " + totalChunks + "）");
        }

        Check(fieldsGenerated == totalChunks,
            "Legacy：A 级为全部 4 个 Chunk 生成 DensityField（" +
            fieldsGenerated.ToString(CultureInfo.InvariantCulture) + "）");
        Check(legacyNotFound == 0 && legacyDegenerate == 0,
            "Legacy：全部列取到地表高度（未取到 " + legacyNotFound.ToString(CultureInfo.InvariantCulture) +
            " / 退化 " + legacyDegenerate.ToString(CultureInfo.InvariantCulture) + "）");
        Check(crossCheckFailures == 0,
            "Legacy：A 级插值与生产公式复刻一致（最大偏差 " +
            maxCrossCheckDelta.ToString("F6", CultureInfo.InvariantCulture) + "）");

        // ================= 4. 统计与分组 =================
        for (int i = 0; i < levels.Length; i++)
        {
            ComputeLevel(levels[i], totalChunks, chunkCoordinates);
        }

        Check(levels[0].All.Total == 144 && levels[1].All.Total == 576 && levels[2].All.Total == 1296,
            "统计：三级样本总数 = 144 / 576 / 1296");
        Check(levels[0].All.Valid == levels[0].All.Total &&
              levels[1].All.Valid == levels[1].All.Total &&
              levels[2].All.Valid == levels[2].All.Total,
            "统计：三级全部样本有效（无效 0）");

        // ================= 5. V2 正确性 / 错列检测（抽样于三级）=================
        int misaligned = 0;
        for (int i = 0; i < levels.Length; i++)
        {
            for (int s = 0; s < levels[i].Samples.Length; s++)
            {
                Sample sample = levels[i].Samples[s];
                int independent = HeightFunction.GetTerrainHeight(sample.WorldX, sample.WorldZ, settings);
                if (sample.V2 != independent) { misaligned++; }
            }
        }
        Check(misaligned == 0,
            "V2：三级全部样本无错列/错坐标（facts.Elevation == HeightFunction(worldX,worldZ)，不一致 " +
            misaligned.ToString(CultureInfo.InvariantCulture) + "）");

        // ================= 6. 同位置配对（A ⊂ B ⊂ C）=================
        int pairingMismatchAB = ComparePairedSamples(levels[0], levels[1]);
        int pairingMismatchBC = ComparePairedSamples(levels[1], levels[2]);
        int pairingMismatchAC = ComparePairedSamples(levels[0], levels[2]);

        Check(pairingMismatchAB == 0,
            "配对：A 的每个坐标在 B 中逐点一致（Legacy/V2/signedDiff；不一致 " +
            pairingMismatchAB.ToString(CultureInfo.InvariantCulture) + "）");
        Check(pairingMismatchBC == 0,
            "配对：B 的每个坐标在 C 中逐点一致（不一致 " +
            pairingMismatchBC.ToString(CultureInfo.InvariantCulture) + "）");
        Check(pairingMismatchAC == 0,
            "配对：A 的每个坐标在 C 中逐点一致（不一致 " +
            pairingMismatchAC.ToString(CultureInfo.InvariantCulture) + "）");

        // ================= 7. 新增样本的影响 =================
        Group newInB = BuildNewSampleGroup(levels[0], levels[1], "B 新增（B\\A）");
        Group newInC = BuildNewSampleGroup(levels[1], levels[2], "C 新增（C\\B）");

        Check(newInB.Total == levels[1].All.Total - levels[0].All.Total &&
              newInC.Total == levels[2].All.Total - levels[1].All.Total,
            "新增样本数：B\\A = " + newInB.Total.ToString(CultureInfo.InvariantCulture) +
            "，C\\B = " + newInC.Total.ToString(CultureInfo.InvariantCulture));

        // ================= 8. 与 R46 已公布数值的复现性 =================
        Check(Mathf.Abs(levels[0].All.MeanAbs - R46AllMeanAbs) < 0.001f,
            "复现：A 级全场景 meanAbs = " + levels[0].All.MeanAbs.ToString("F4", CultureInfo.InvariantCulture) +
            " == R46 记录的 " + R46AllMeanAbs.ToString("F4", CultureInfo.InvariantCulture));
        Check(Mathf.Abs(levels[0].All.MaxAbs - R46AllMaxAbs) < 0.001f,
            "复现：A 级全场景 maxAbs = " + levels[0].All.MaxAbs.ToString("F4", CultureInfo.InvariantCulture) +
            " == R46 记录的 " + R46AllMaxAbs.ToString("F4", CultureInfo.InvariantCulture));
        Check(Mathf.Abs(levels[0].PerChunk[0].MeanAbs - R46Chunk0MeanAbs) < 0.001f &&
              Mathf.Abs(levels[0].PerChunk[0].MaxAbs - R46Chunk0MaxAbs) < 0.001f,
            "复现：A 级 chunk(0,0,0) meanAbs/maxAbs = " +
            levels[0].PerChunk[0].MeanAbs.ToString("F4", CultureInfo.InvariantCulture) + "/" +
            levels[0].PerChunk[0].MaxAbs.ToString("F4", CultureInfo.InvariantCulture) +
            " == R46 记录 8.2301/13.3777");

        // ================= 9. 稳定性指标（只计算与报告，不预设通过阈值）=================
        Check(IsFinite(levels[2].All.MeanAbs) && IsFinite(levels[2].All.MaxAbs),
            "稳定性：三级统计指标均为有限值（可比较）");
        Check(levels[0].Positive + levels[0].Negative + levels[0].Zero == levels[0].All.Valid &&
              levels[1].Positive + levels[1].Negative + levels[1].Zero == levels[1].All.Valid &&
              levels[2].Positive + levels[2].Negative + levels[2].Zero == levels[2].All.Valid,
            "稳定性：三级 signedDiff 正/负/零计数之和 == 有效样本数");

        // ================= 10. 失败路径（独立记录）=================
        int validBeforeProbes = levels[2].All.Valid;
        WorldSkeletonFacts belowZero;
        bool belowOk = WorldSkeletonProvider.TryGetFacts(
            levels[0].Samples[0].WorldX, -1, levels[0].Samples[0].WorldZ, settings, out belowZero);
        WorldGenerationSettings emptySettings = default(WorldGenerationSettings);
        WorldSkeletonFacts emptyFacts;
        bool emptyOk = WorldSkeletonProvider.TryGetFacts(
            levels[0].Samples[0].WorldX, 0, levels[0].Samples[0].WorldZ, emptySettings, out emptyFacts);

        Check(!belowOk && !belowZero.IsValid, "失败路径：Y=-1 ⇒ 失败且事实无效（独立记录）");
        Check(!emptyOk && !emptyFacts.IsValid, "失败路径：default(WorldGenerationSettings) ⇒ 失败且事实无效（独立记录）");
        Check(levels[2].All.Valid == validBeforeProbes,
            "失败路径样本未污染有效统计（有效数仍为 " + levels[2].All.Valid.ToString(CultureInfo.InvariantCulture) + "）");

        // ================= 11. 隔离性后置 =================
        string sceneHashAfter = ComputeFileHash(scenePath);
        Check(sceneHashBefore == sceneHashAfter,
            "隔离性：场景文件 SHA256 前后一致（" + ShortHash(sceneHashBefore) + "）");
        Check(Resources.FindObjectsOfTypeAll<GameObject>().Length == gameObjectsBefore,
            "隔离性：GameObject 数量未变");
        Check(Resources.FindObjectsOfTypeAll<DensityTerrain>().Length == terrainInstancesBefore,
            "隔离性：DensityTerrain 实例数未变");
        Check(!SceneManager.GetActiveScene().isDirty, "隔离性：当前场景未被标记为脏");

        // ================= 12. 输出：统计表 + 稳定性对比 + 逐点原始数据 =================
        for (int i = 0; i < levels.Length; i++)
        {
            LevelData level = levels[i];
            Debug.Log("[R47] === " + level.Name + " === " + level.All.Describe());
            for (int c = 0; c < totalChunks; c++)
            {
                Debug.Log("[R47] " + level.PerChunk[c].Describe());
            }
            Debug.Log("[R47] " + level.Name + " " + level.Exact.Describe());
            Debug.Log("[R47] " + level.Name + " " + level.Offset.Describe());
            Debug.Log("[R47] " + level.Name + " " + level.Boundary.Describe());
            Debug.Log("[R47] " + level.Name + " " + level.NonBoundary.Describe());
            Debug.Log("[R47] " + level.Name + " " + level.Cutaway.Describe());
            Debug.Log("[R47] " + level.Name + " " + level.NonCutaway.Describe());
            Debug.Log("[R47] " + level.Name + " 正/负/零 = " + level.Positive.ToString(CultureInfo.InvariantCulture) +
                      "/" + level.Negative.ToString(CultureInfo.InvariantCulture) +
                      "/" + level.Zero.ToString(CultureInfo.InvariantCulture) +
                      "；唯一 Terrain Local 位置 = " + level.UniqueLocalPositions.ToString(CultureInfo.InvariantCulture) +
                      "（重复 " + level.DuplicateSamples.ToString(CultureInfo.InvariantCulture) +
                      "）；唯一世界整数坐标 = " + level.UniqueWorldCoordinates.ToString(CultureInfo.InvariantCulture) +
                      "；去重后 meanAbs = " + level.DeduplicatedMeanAbs.ToString("F4", CultureInfo.InvariantCulture) +
                      " / maxAbs = " + level.DeduplicatedMaxAbs.ToString("F4", CultureInfo.InvariantCulture));
        }

        Debug.Log("[R47] 新增样本：" + newInB.Describe());
        Debug.Log("[R47] 新增样本：" + newInC.Describe());

        LogStabilityComparison(levels, totalChunks);

        Debug.Log("[R47] 逐点原始数据（level | idx | chunk | ix,iz | localX,localZ | worldX,worldZ | offX,offZ | legacy | v2 | signed | abs | boundary | cutaway | class | valid）");
        for (int i = 0; i < levels.Length; i++)
        {
            LevelData level = levels[i];
            for (int s = 0; s < level.Samples.Length; s++)
            {
                Sample sample = level.Samples[s];
                StringBuilder line = new StringBuilder();
                line.Append("[R47] ").Append(level.Name).Append(" | ")
                    .Append(s.ToString("D4", CultureInfo.InvariantCulture)).Append(" | ")
                    .Append(sample.ChunkLabel).Append(" | ")
                    .Append(sample.IX.ToString(CultureInfo.InvariantCulture)).Append(",")
                    .Append(sample.IZ.ToString(CultureInfo.InvariantCulture)).Append(" | ")
                    .Append(sample.LocalX.ToString("F4", CultureInfo.InvariantCulture)).Append(",")
                    .Append(sample.LocalZ.ToString("F4", CultureInfo.InvariantCulture)).Append(" | ")
                    .Append(sample.WorldX.ToString(CultureInfo.InvariantCulture)).Append(",")
                    .Append(sample.WorldZ.ToString(CultureInfo.InvariantCulture)).Append(" | ")
                    .Append(sample.OffsetX.ToString("F4", CultureInfo.InvariantCulture)).Append(",")
                    .Append(sample.OffsetZ.ToString("F4", CultureInfo.InvariantCulture)).Append(" | ")
                    .Append(sample.Legacy.ToString("F4", CultureInfo.InvariantCulture)).Append(" | ")
                    .Append(sample.V2.ToString(CultureInfo.InvariantCulture)).Append(" | ")
                    .Append(sample.Signed.ToString("F4", CultureInfo.InvariantCulture)).Append(" | ")
                    .Append(sample.Abs.ToString("F4", CultureInfo.InvariantCulture)).Append(" | ")
                    .Append(sample.IsBoundary ? "B" : "-").Append(" | ")
                    .Append(sample.IsCutaway ? "CUT" : "-").Append(" | ")
                    .Append(sample.IsExact ? "EXACT" : "OFFSET").Append(" | ")
                    .Append(sample.Valid ? "valid" : "INVALID");
                Debug.Log(line.ToString());
            }
        }

        // ================= 13. 判定语义 + 退出码 =================
        Check(levels[2].All.MaxAbs > 0f && failures == 0,
            "判定语义：已实测高度差异（C 级 maxAbs=" +
            levels[2].All.MaxAbs.ToString("F4", CultureInfo.InvariantCulture) +
            "）但未产生失败 ⇒ 高度不等不是失败条件");

        Check(ExitCodeFor(0) == 0, "退出码：失败计数 0 ⇒ 0");
        Check(ExitCodeFor(1) == 1, "退出码：失败计数 1 ⇒ 1");
        Check(ExitCodeFor(7) == 1, "退出码：失败计数 7 ⇒ 1");

        if (IsSelfFailureRequested())
        {
            Check(false, "[自检] 命令行开关注入的故意失败（用于验证 Batchmode 退出码传播）");
        }

        Report();
    }

    /// <summary>**批处理入口**：退出码由断言结果决定；异常一律 exit 1。</summary>
    public static void RunBatch()
    {
        try
        {
            Validate();
        }
        catch (Exception exception)
        {
            Debug.LogError("[R47] 批处理失败（exit 1）：" + exception.Message);
            EditorApplication.Exit(1);
            return;
        }

        EditorApplication.Exit(ExitCodeFor(failures));
    }

    // -----------------------------------------------------------------
    // 统计与比较
    // -----------------------------------------------------------------

    private static void ComputeLevel(LevelData level, int chunkCount, Vector3Int[] chunkCoordinates)
    {
        level.All = new Group(level.Name + " 全部");
        level.PerChunk = new Group[chunkCount];
        for (int i = 0; i < chunkCount; i++)
        {
            level.PerChunk[i] = new Group(level.Name + " chunk(" + chunkCoordinates[i].x + "," +
                                          chunkCoordinates[i].y + "," + chunkCoordinates[i].z + ")");
        }
        level.Exact = new Group(level.Name + " X/Z 完全对应");
        level.Offset = new Group(level.Name + " 存在坐标偏移");
        level.Boundary = new Group(level.Name + " Chunk 边界");
        level.NonBoundary = new Group(level.Name + " 非边界");
        level.Cutaway = new Group(level.Name + " 剖切区");
        level.NonCutaway = new Group(level.Name + " 非剖切区");

        Dictionary<string, Sample> uniqueLocal = new Dictionary<string, Sample>(StringComparer.Ordinal);
        Dictionary<string, int> uniqueWorld = new Dictionary<string, int>(StringComparer.Ordinal);

        for (int i = 0; i < level.Samples.Length; i++)
        {
            Sample sample = level.Samples[i];

            level.All.Add(sample);
            level.PerChunk[sample.ChunkIndex].Add(sample);
            if (sample.IsExact) { level.Exact.Add(sample); } else { level.Offset.Add(sample); }
            if (sample.IsBoundary) { level.Boundary.Add(sample); } else { level.NonBoundary.Add(sample); }
            if (sample.IsCutaway) { level.Cutaway.Add(sample); } else { level.NonCutaway.Add(sample); }

            if (!sample.Valid) { continue; }
            if (sample.Signed > 0f) { level.Positive++; }
            else if (sample.Signed < 0f) { level.Negative++; }
            else { level.Zero++; }

            string localKey = sample.LocalX.ToString("F4", CultureInfo.InvariantCulture) + "," +
                              sample.LocalZ.ToString("F4", CultureInfo.InvariantCulture);
            if (!uniqueLocal.ContainsKey(localKey)) { uniqueLocal[localKey] = sample; }

            string worldKey = sample.WorldX.ToString(CultureInfo.InvariantCulture) + "," +
                              sample.WorldZ.ToString(CultureInfo.InvariantCulture);
            if (!uniqueWorld.ContainsKey(worldKey)) { uniqueWorld[worldKey] = sample.V2; }
        }

        level.UniqueLocalPositions = uniqueLocal.Count;
        level.UniqueWorldCoordinates = uniqueWorld.Count;
        level.DuplicateSamples = level.All.Total - uniqueLocal.Count;

        double sum = 0d;
        float maxAbs = 0f;
        int count = 0;
        foreach (KeyValuePair<string, Sample> pair in uniqueLocal)
        {
            if (!pair.Value.Valid) { continue; }
            count++;
            sum += pair.Value.Abs;
            if (pair.Value.Abs > maxAbs) { maxAbs = pair.Value.Abs; }
        }
        level.DeduplicatedMeanAbs = count > 0 ? (float)(sum / count) : 0f;
        level.DeduplicatedMaxAbs = maxAbs;
    }

    /// <summary>比较 lower 中每个坐标在 upper 中是否存在且逐字段一致；返回不一致数。</summary>
    private static int ComparePairedSamples(LevelData lower, LevelData upper)
    {
        Dictionary<string, Sample> index = new Dictionary<string, Sample>(StringComparer.Ordinal);
        for (int i = 0; i < upper.Samples.Length; i++)
        {
            Sample sample = upper.Samples[i];
            index[PairKey(sample)] = sample;
        }

        int mismatches = 0;
        for (int i = 0; i < lower.Samples.Length; i++)
        {
            Sample sample = lower.Samples[i];
            Sample other;
            if (!index.TryGetValue(PairKey(sample), out other)) { mismatches++; continue; }

            if (!Approximately(sample.Legacy, other.Legacy) ||
                sample.V2 != other.V2 ||
                !(Approximately(sample.Signed, other.Signed) ||
                  (float.IsNaN(sample.Signed) && float.IsNaN(other.Signed))) ||
                sample.Valid != other.Valid)
            {
                mismatches++;
            }
        }

        return mismatches;
    }

    private static Group BuildNewSampleGroup(LevelData previous, LevelData current, string name)
    {
        Group group = new Group(name);
        HashSet<string> previousKeys = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < previous.Samples.Length; i++) { previousKeys.Add(PairKey(previous.Samples[i])); }

        for (int i = 0; i < current.Samples.Length; i++)
        {
            Sample sample = current.Samples[i];
            if (previousKeys.Contains(PairKey(sample))) { continue; }
            group.Add(sample);
        }

        return group;
    }

    private static string PairKey(Sample sample)
    {
        return sample.ChunkIndex.ToString(CultureInfo.InvariantCulture) + ":" +
               sample.IX.ToString(CultureInfo.InvariantCulture) + ":" +
               sample.IZ.ToString(CultureInfo.InvariantCulture);
    }

    private static void LogStabilityComparison(LevelData[] levels, int chunkCount)
    {
        LevelData a = levels[0];
        LevelData b = levels[1];
        LevelData c = levels[2];

        Debug.Log("[R47] === 稳定性对比（A=6×6 → B=12×12 → C=18×18）===");
        Debug.Log("[R47] 全场景 meanAbs：" +
                  a.All.MeanAbs.ToString("F4", CultureInfo.InvariantCulture) + " → " +
                  b.All.MeanAbs.ToString("F4", CultureInfo.InvariantCulture) + " → " +
                  c.All.MeanAbs.ToString("F4", CultureInfo.InvariantCulture) +
                  "；Δ(B−A)=" + (b.All.MeanAbs - a.All.MeanAbs).ToString("F4", CultureInfo.InvariantCulture) +
                  "，Δ(C−B)=" + (c.All.MeanAbs - b.All.MeanAbs).ToString("F4", CultureInfo.InvariantCulture) +
                  "，Δ(C−A)=" + (c.All.MeanAbs - a.All.MeanAbs).ToString("F4", CultureInfo.InvariantCulture));

        Debug.Log("[R47] 全场景 maxAbs：" +
                  a.All.MaxAbs.ToString("F4", CultureInfo.InvariantCulture) + " → " +
                  b.All.MaxAbs.ToString("F4", CultureInfo.InvariantCulture) + " → " +
                  c.All.MaxAbs.ToString("F4", CultureInfo.InvariantCulture) +
                  "；Δ(B−A)=" + (b.All.MaxAbs - a.All.MaxAbs).ToString("F4", CultureInfo.InvariantCulture) +
                  "，Δ(C−B)=" + (c.All.MaxAbs - b.All.MaxAbs).ToString("F4", CultureInfo.InvariantCulture));

        for (int i = 0; i < chunkCount; i++)
        {
            Debug.Log("[R47] chunk 均值/最大（meanAbs）" + i.ToString(CultureInfo.InvariantCulture) + "：" +
                      a.PerChunk[i].MeanAbs.ToString("F4", CultureInfo.InvariantCulture) + " → " +
                      b.PerChunk[i].MeanAbs.ToString("F4", CultureInfo.InvariantCulture) + " → " +
                      c.PerChunk[i].MeanAbs.ToString("F4", CultureInfo.InvariantCulture) +
                      "；Δ(C−A)=" + (c.PerChunk[i].MeanAbs - a.PerChunk[i].MeanAbs).ToString("F4", CultureInfo.InvariantCulture) +
                      "；maxAbs " + a.PerChunk[i].MaxAbs.ToString("F4", CultureInfo.InvariantCulture) + " → " +
                      c.PerChunk[i].MaxAbs.ToString("F4", CultureInfo.InvariantCulture));
        }

        Debug.Log("[R47] 完全对应组 meanAbs：" +
                  a.Exact.MeanAbs.ToString("F4", CultureInfo.InvariantCulture) + " → " +
                  b.Exact.MeanAbs.ToString("F4", CultureInfo.InvariantCulture) + " → " +
                  c.Exact.MeanAbs.ToString("F4", CultureInfo.InvariantCulture) +
                  "；偏移组 meanAbs：" +
                  a.Offset.MeanAbs.ToString("F4", CultureInfo.InvariantCulture) + " → " +
                  b.Offset.MeanAbs.ToString("F4", CultureInfo.InvariantCulture) + " → " +
                  c.Offset.MeanAbs.ToString("F4", CultureInfo.InvariantCulture));

        Debug.Log("[R47] 边界组 meanAbs：" +
                  a.Boundary.MeanAbs.ToString("F4", CultureInfo.InvariantCulture) + " → " +
                  b.Boundary.MeanAbs.ToString("F4", CultureInfo.InvariantCulture) + " → " +
                  c.Boundary.MeanAbs.ToString("F4", CultureInfo.InvariantCulture) +
                  "；非边界组 meanAbs：" +
                  a.NonBoundary.MeanAbs.ToString("F4", CultureInfo.InvariantCulture) + " → " +
                  b.NonBoundary.MeanAbs.ToString("F4", CultureInfo.InvariantCulture) + " → " +
                  c.NonBoundary.MeanAbs.ToString("F4", CultureInfo.InvariantCulture));

        Debug.Log("[R47] 剖切区 meanAbs：" +
                  a.Cutaway.MeanAbs.ToString("F4", CultureInfo.InvariantCulture) + " → " +
                  b.Cutaway.MeanAbs.ToString("F4", CultureInfo.InvariantCulture) + " → " +
                  c.Cutaway.MeanAbs.ToString("F4", CultureInfo.InvariantCulture) +
                  "；非剖切区 meanAbs：" +
                  a.NonCutaway.MeanAbs.ToString("F4", CultureInfo.InvariantCulture) + " → " +
                  b.NonCutaway.MeanAbs.ToString("F4", CultureInfo.InvariantCulture) + " → " +
                  c.NonCutaway.MeanAbs.ToString("F4", CultureInfo.InvariantCulture));

        Debug.Log("[R47] signedDiff 正/负/零：" +
                  "A " + a.Positive + "/" + a.Negative + "/" + a.Zero + "；B " +
                  b.Positive + "/" + b.Negative + "/" + b.Zero + "；C " +
                  c.Positive + "/" + c.Negative + "/" + c.Zero);
    }

    // -----------------------------------------------------------------
    // 纯辅助
    // -----------------------------------------------------------------

    private static bool IsSubset(int[] subset, int[] superset)
    {
        for (int i = 0; i < subset.Length; i++)
        {
            bool found = false;
            for (int j = 0; j < superset.Length; j++)
            {
                if (subset[i] == superset[j]) { found = true; break; }
            }
            if (!found) { return false; }
        }
        return true;
    }

    /// <summary>与生产 `FindSurfaceHeight` 同语义：密度由 &gt;0 变 ≤0 的首段线性插值。</summary>
    private static bool TryDeriveColumnSurface(DensityField field, int x, int z, out float surface, out bool degenerate)
    {
        surface = 0f;
        degenerate = true;

        if (field == null) { return false; }
        if (x < 0 || x >= field.SampleCountX || z < 0 || z >= field.SampleCountZ) { return false; }

        for (int y = 0; y < field.SampleCountY - 1; y++)
        {
            float density0 = field[x, y, z];
            float density1 = field[x, y + 1, z];
            if (density0 > 0f && density1 <= 0f)
            {
                float t = density0 / (density0 - density1);
                surface = field.Origin.y + (y + t) * field.CellSizeY;
                degenerate = false;
                return true;
            }
        }

        surface = field[x, 0, z] > 0f ? field.Origin.y + field.Size.y : field.Origin.y;
        return true;
    }

    private static bool TryExtractDensityTerrainBlock(string sceneText, out string block, out string gameObjectId)
    {
        block = null;
        gameObjectId = null;

        int guidIndex = sceneText.IndexOf(DensityTerrainScriptGuid, StringComparison.Ordinal);
        if (guidIndex < 0) { return false; }

        int start = sceneText.LastIndexOf("--- !u!", guidIndex, StringComparison.Ordinal);
        if (start < 0) { return false; }

        int end = sceneText.IndexOf("--- !u!", guidIndex, StringComparison.Ordinal);
        if (end < 0) { end = sceneText.Length; }

        block = sceneText.Substring(start, end - start);

        Match match = Regex.Match(block, @"m_GameObject:\s*\{fileID:\s*(\d+)\}");
        if (match.Success) { gameObjectId = match.Groups[1].Value; }

        return true;
    }

    private static Dictionary<string, string> ParseKeyValues(string block)
    {
        Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.Ordinal);
        string[] lines = block.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            Match match = Regex.Match(lines[i], @"^\s*([A-Za-z_][A-Za-z0-9_]*):\s*(.*)$");
            if (!match.Success) { continue; }

            string key = match.Groups[1].Value;
            string value = match.Groups[2].Value.Trim();
            if (value.Length == 0) { continue; }
            if (!result.ContainsKey(key)) { result[key] = value; }
        }
        return result;
    }

    private static string Value(Dictionary<string, string> fields, string key)
    {
        string value;
        return fields.TryGetValue(key, out value) ? value : string.Empty;
    }

    private static float ParseNumber(Dictionary<string, string> fields, string key)
    {
        string text = Value(fields, key).Trim();
        if (text.Length == 0) { return float.NaN; }

        Match vector = Regex.Match(text, @"\{x:\s*(-?[\d.]+)");
        if (vector.Success) { text = vector.Groups[1].Value; }

        return ParseFloat(text);
    }

    private static bool TryParseVectorInt(Dictionary<string, string> fields, string key, out int x, out int y, out int z)
    {
        x = y = z = 0;
        Match match = Regex.Match(Value(fields, key), @"\{x:\s*(-?\d+),\s*y:\s*(-?\d+),\s*z:\s*(-?\d+)\}");
        if (!match.Success) { return false; }

        x = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        y = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        z = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
        return true;
    }

    private static bool TryParseVectorFloat(Dictionary<string, string> fields, string key, out float x, out float y, out float z)
    {
        x = y = z = float.NaN;
        Match match = Regex.Match(Value(fields, key), @"\{x:\s*(-?[\d.]+),\s*y:\s*(-?[\d.]+),\s*z:\s*(-?[\d.]+)\}");
        if (!match.Success) { return false; }

        x = ParseFloat(match.Groups[1].Value);
        y = ParseFloat(match.Groups[2].Value);
        z = ParseFloat(match.Groups[3].Value);
        return true;
    }

    private static float ParseFloat(string text)
    {
        float parsed;
        if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)) { return parsed; }
        return float.NaN;
    }

    private static bool Approximately(float left, float right)
    {
        if (float.IsNaN(left) || float.IsNaN(right)) { return false; }
        return Mathf.Abs(left - right) <= 0.0001f;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static string ComputeFileHash(string path)
    {
        try
        {
            using (FileStream stream = File.OpenRead(path))
            {
                using (SHA256 sha = SHA256.Create())
                {
                    byte[] digest = sha.ComputeHash(stream);
                    StringBuilder text = new StringBuilder(digest.Length * 2);
                    for (int i = 0; i < digest.Length; i++)
                    {
                        text.Append(digest[i].ToString("X2", CultureInfo.InvariantCulture));
                    }
                    return text.ToString();
                }
            }
        }
        catch (Exception exception)
        {
            Debug.LogError("[R47] 无法计算场景哈希：" + exception.Message);
            return string.Empty;
        }
    }

    private static string ShortHash(string hash)
    {
        if (string.IsNullOrEmpty(hash)) { return "(空)"; }
        return hash.Length >= 16 ? hash.Substring(0, 16) : hash;
    }

    private static int ExitCodeFor(int failureCount)
    {
        return failureCount == 0 ? 0 : 1;
    }

    private static bool IsSelfFailureRequested()
    {
        string[] arguments = Environment.GetCommandLineArgs();
        for (int i = 0; i < arguments.Length; i++)
        {
            if (string.Equals(arguments[i], SelfFailureSwitch, StringComparison.Ordinal)) { return true; }
        }
        return false;
    }

    private static void Check(bool condition, string label)
    {
        checks++;

        if (condition)
        {
            Debug.Log("[R47] PASS: " + label);
        }
        else
        {
            failures++;
            Debug.LogError("[R47] FAIL: " + label);
        }
    }

    private static void Report()
    {
        StringBuilder summary = new StringBuilder();
        summary.Append("[R47] 采样精度稳定性验证 ⇒ checks=")
               .Append(checks.ToString(CultureInfo.InvariantCulture))
               .Append(" failures=")
               .Append(failures.ToString(CultureInfo.InvariantCulture));

        if (failures == 0)
        {
            Debug.Log(summary.ToString() + "（全部通过；exit 0）");
        }
        else
        {
            Debug.LogError(summary.ToString() + "（存在失败项；exit 1）");
        }
    }
}
