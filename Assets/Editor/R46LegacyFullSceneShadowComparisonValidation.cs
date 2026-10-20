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
/// **R46 — Legacy 全场景（4 个 Chunk）影子高度对比**（纯只读；不加载场景、不实例化对象）。
///
/// 目标：把 R44 的 36 列基准扩展到 `SampleScene.unity` 中**全部 4 个** Legacy `DensityTerrain` Chunk，
/// 并为每个样本留下可追溯的坐标映射记录与分组统计。
///
/// 参数来源：**从场景文件只读解析**（不硬编码），并在运行时断言其与 R44/R45 已核实值一致。
///
/// Chunk 与采样位置的定义（源码依据，非名称假定）：
///   · `DensityTerrain.Regenerate` 以 `chunkStartCoordinate + (x,y,z)` 枚举坐标（`DensityTerrain.cs:318–336`）；
///   · `TerrainChunk.ChunkOrigin = ChunkSpace.ChunkToLocalOrigin(coordinate, voxelSize, chunkVoxelSize)`
///     （`TerrainChunk.cs:76–80`）⇒ 原点 = chunkCoord × chunkWorldSize；
///   · `DensityFieldGenerator.Generate(mode, resolution, origin=ChunkOrigin, size=chunkWorldSize, ...)`
///     （`DensityTerrain.cs:783–787`）⇒ 表面公式的输入是 `ChunkOrigin + idx × CellSize`，
///     因此**每个 Chunk 的噪声相位不同**（非周期重复）。
///
/// 坐标映射（**本轮测试映射，不是正式契约**）：
///   `worldX = RoundToInt(TerrainLocalX)`、`worldZ = RoundToInt(TerrainLocalZ)`、`worldY = 0`
///   前提：根 Transform 为恒等变换 ⇒ Local == World（R45 已从场景文件确认）。
///
/// 明确不做：不统一两套高度函数、不建立正式坐标契约、不接入生成流程、不改任何参数/默认值。
///
/// 菜单：`Tools/World V2/Validate R46 Legacy Full Scene Shadow Comparison`
/// 批处理：`Unity -batchmode -quit -executeMethod R46LegacyFullSceneShadowComparisonValidation.RunBatch`
/// 退出码自检：追加 `-r46SelfFailure` ⇒ 注入一条故意失败（期望 exit 1）
/// </summary>
public static class R46LegacyFullSceneShadowComparisonValidation
{
    private const string SelfFailureSwitch = "-r46SelfFailure";
    private const string DensityTerrainScriptGuid = "3af3bcfc875f4cbfae75f1f95dee772a";

    // 采样网格索引（每个 Chunk 相同）：沿用 R44 的索引，使 R44/R45/R46 数据可逐列对照
    private static readonly int[] SampleIX = { 0, 1, 19, 38, 57, 75 };
    private static readonly int[] SampleIZ = { 0, 1, 13, 26, 38, 75 };

    private static int failures;
    private static int checks;

    private struct Sample
    {
        public int ChunkIndex;
        public string ChunkLabel;
        public float LocalX;
        public float LocalZ;
        public int WorldX;
        public int WorldZ;
        public float OffsetX;
        public float OffsetZ;
        public float LegacySurface;
        public int V2Elevation;
        public float SignedDiff;
        public float AbsDiff;
        public bool IsBoundary;
        public bool IsCutaway;
        public bool IsExact;
        public bool LegacyFound;
        public bool LegacyDegenerate;
        public bool V2Ok;
        public bool V2Valid;
        public bool IsValid;
    }

    private sealed class Group
    {
        public readonly string Name;
        public int Total;
        public int Valid;
        public float SignedMin = float.MaxValue;
        public float SignedMax = float.MinValue;
        public float MaxAbs;
        public double AbsSum;

        public Group(string name) { Name = name; }

        public void Add(Sample sample)
        {
            Total++;
            if (!sample.IsValid) { return; }

            Valid++;
            if (sample.SignedDiff < SignedMin) { SignedMin = sample.SignedDiff; }
            if (sample.SignedDiff > SignedMax) { SignedMax = sample.SignedDiff; }
            if (sample.AbsDiff > MaxAbs) { MaxAbs = sample.AbsDiff; }
            AbsSum += sample.AbsDiff;
        }

        public float MeanAbs
        {
            get { return Valid > 0 ? (float)(AbsSum / Valid) : 0f; }
        }

        public string Describe()
        {
            string head = "[R46] " + Name + "：n=" + Total.ToString(CultureInfo.InvariantCulture);

            if (Valid == 0)
            {
                return head + " valid=0 ⇒ 该组无有效样本（不以其它组数据代替）";
            }

            return head +
                   " valid=" + Valid.ToString(CultureInfo.InvariantCulture) +
                   " signedMin=" + SignedMin.ToString("F4", CultureInfo.InvariantCulture) +
                   " signedMax=" + SignedMax.ToString("F4", CultureInfo.InvariantCulture) +
                   " meanAbs=" + MeanAbs.ToString("F4", CultureInfo.InvariantCulture) +
                   " maxAbs=" + MaxAbs.ToString("F4", CultureInfo.InvariantCulture);
        }
    }

    [MenuItem("Tools/World V2/Validate R46 Legacy Full Scene Shadow Comparison")]
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
        Check(terrainInstancesBefore == 0,
            "隔离性：场景中 DensityTerrain 实例数 = 0（" +
            terrainInstancesBefore.ToString(CultureInfo.InvariantCulture) + "）");

        // ================= 1. 从场景文件只读解析参数 =================
        if (!File.Exists(scenePath))
        {
            Check(false, "前置：场景文件存在（缺失）⇒ 不具备验证条件");
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
            "前置：组件块可解析出 m_GameObject（fileID " + gameObjectId +
            "）⇒ 映射前提的定位依据完整");

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
        int densityMode = (int)ParseNumber(fields, "densityMode");
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

        // ---- 断言：解析值与 R44/R45 已核实值一致（参数来源可追溯）----
        Check(densityMode == 0 && useNoise && resolution == 75,
            "参数：densityMode=HeightField / useNoise=true / resolution=75（实际 " +
            densityMode.ToString(CultureInfo.InvariantCulture) + " / " + useNoise + " / " +
            resolution.ToString(CultureInfo.InvariantCulture) + "）");
        Check(Approximately(height, 15.7f) && Approximately(noiseScale, 0.15f) &&
              Approximately(noiseStrength, 2.37f),
            "参数：height/noiseScale/noiseStrength = 15.7 / 0.15 / 2.37（实际 " +
            height.ToString("F4", CultureInfo.InvariantCulture) + " / " +
            noiseScale.ToString("F4", CultureInfo.InvariantCulture) + " / " +
            noiseStrength.ToString("F4", CultureInfo.InvariantCulture) + "）");
        Check(chunkVoxelX == 75 && chunkVoxelY == 37 && chunkVoxelZ == 50 && Approximately(voxelSize, 1f),
            "参数：chunkVoxelSize=(75,37,50) 且 voxelSize=1（实际 (" + chunkVoxelX + "," + chunkVoxelY + "," +
            chunkVoxelZ + ") / " + voxelSize.ToString("F3", CultureInfo.InvariantCulture) + "）");
        Check(chunkCountX == 2 && chunkCountY == 1 && chunkCountZ == 2,
            "参数：chunkCount=(2,1,2) ⇒ 场景共 4 个 Chunk（实际 (" + chunkCountX + "," + chunkCountY + "," +
            chunkCountZ + ")）");
        Check(chunkStartX == 0 && chunkStartY == 0 && chunkStartZ == 0,
            "参数：chunkStartCoordinate=(0,0,0)（实际 (" + chunkStartX + "," + chunkStartY + "," + chunkStartZ + ")）");

        // ================= 2. 枚举 4 个 Chunk（原点用生产函数计算）=================
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

        Check(totalChunks == 4,
            "覆盖：Chunk 总数 = " + totalChunks.ToString(CultureInfo.InvariantCulture) + "（期望 4）");

        HashSet<string> coordinateSet = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < totalChunks; i++)
        {
            coordinateSet.Add(chunkCoordinates[i].x + "," + chunkCoordinates[i].y + "," + chunkCoordinates[i].z);
        }
        Check(coordinateSet.Count == 4, "覆盖：枚举出 4 个互不相同的 Chunk 坐标");
        Check(Approximately(chunkOrigins[0].x, 0f) && Approximately(chunkOrigins[0].z, 0f),
            "覆盖：枚举顺序首个 Chunk 为 (0,0,0)，原点 (0,0,0)（即 debugCutaway 作用的那个 Chunk）");

        StringBuilder originText = new StringBuilder();
        for (int i = 0; i < totalChunks; i++)
        {
            if (i > 0) { originText.Append(" | "); }
            originText.Append("(").Append(chunkCoordinates[i].x).Append(",")
                      .Append(chunkCoordinates[i].y).Append(",")
                      .Append(chunkCoordinates[i].z).Append(")->(")
                      .Append(chunkOrigins[i].x.ToString("F0", CultureInfo.InvariantCulture)).Append(",")
                      .Append(chunkOrigins[i].y.ToString("F0", CultureInfo.InvariantCulture)).Append(",")
                      .Append(chunkOrigins[i].z.ToString("F0", CultureInfo.InvariantCulture)).Append(")");
        }
        Check(true, "覆盖：4 个 Chunk 与 Terrain Local 原点（生产函数 ChunkSpace.ChunkToLocalOrigin）= " + originText);

        // ================= 3. 逐 Chunk 生成 Legacy DensityField 并采样 =================
        float cellX = (float)chunkVoxelX / resolution;
        float cellZ = (float)chunkVoxelZ / resolution;
        Vector3 chunkWorldSize = new Vector3(chunkVoxelX * voxelSize, chunkVoxelY * voxelSize, chunkVoxelZ * voxelSize);

        WorldGenerationSettings settings = new WorldGenerationSettings(12345, 24, 3, 64, 0.06f, 6f);
        string settingsReason;
        Check(settings.IsValid(out settingsReason), "V2 配置合法（" + settings + "）");
        Check(settings.SeaLevel == WorldGenerationSettings.DefaultSeaLevel,
            "V2 配置 SeaLevel == DefaultSeaLevel（" + settings.SeaLevel.ToString(CultureInfo.InvariantCulture) + "）");

        int samplesPerChunk = SampleIX.Length * SampleIZ.Length;
        Sample[] samples = new Sample[totalChunks * samplesPerChunk];
        int sampleIndex = 0;
        int fieldsGenerated = 0;
        int legacyNotFound = 0;
        int legacyDegenerate = 0;
        float maxCrossCheckDelta = 0f;
        int crossCheckFailures = 0;

        float cutMinX = debugCutX - debugCutWidth * 0.5f;
        float cutMaxX = debugCutX + debugCutWidth * 0.5f;

        for (int chunkIndex = 0; chunkIndex < totalChunks; chunkIndex++)
        {
            MaterialField materialField;
            DensityField field = DensityFieldGenerator.Generate(
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

            if (field == null) { continue; }
            fieldsGenerated++;

            bool cutAppliesToChunk = debugCutaway && chunkIndex == 0;

            for (int a = 0; a < SampleIX.Length; a++)
            {
                for (int b = 0; b < SampleIZ.Length; b++)
                {
                    int ix = SampleIX[a];
                    int iz = SampleIZ[b];

                    Vector3 localPosition = field.GetLocalPosition(ix, 0, iz);

                    Sample sample = new Sample();
                    sample.ChunkIndex = chunkIndex;
                    sample.ChunkLabel = "(" + chunkCoordinates[chunkIndex].x + "," +
                                        chunkCoordinates[chunkIndex].y + "," +
                                        chunkCoordinates[chunkIndex].z + ")";
                    // Legacy 实际参与高度计算的坐标 == DensityField 采样点（生产在该点求值）
                    sample.LocalX = localPosition.x;
                    sample.LocalZ = localPosition.z;

                    float derived;
                    bool degenerate;
                    sample.LegacyFound = TryDeriveColumnSurface(field, ix, iz, out derived, out degenerate);
                    sample.LegacySurface = derived;
                    sample.LegacyDegenerate = sample.LegacyFound && degenerate;
                    if (!sample.LegacyFound) { legacyNotFound++; }
                    if (sample.LegacyDegenerate) { legacyDegenerate++; }

                    // 交叉校验：符号变化插值 vs 生产公式复刻（仅验证取值路径；不代替渲染验证）
                    float noise = Mathf.PerlinNoise(localPosition.x * noiseScale, localPosition.z * noiseScale) * 2f - 1f;
                    float analytic = height + noise * noiseStrength;
                    float delta = Mathf.Abs(derived - analytic);
                    if (delta > maxCrossCheckDelta) { maxCrossCheckDelta = delta; }
                    if (delta > 0.01f) { crossCheckFailures++; }

                    // 测试映射：Terrain Local → 世界整数坐标（根恒等 ⇒ Local == World）
                    sample.WorldX = Mathf.RoundToInt(localPosition.x);
                    sample.WorldZ = Mathf.RoundToInt(localPosition.z);
                    sample.OffsetX = Mathf.Abs(localPosition.x - sample.WorldX);
                    sample.OffsetZ = Mathf.Abs(localPosition.z - sample.WorldZ);

                    WorldSkeletonFacts facts;
                    sample.V2Ok = WorldSkeletonProvider.TryGetFacts(
                        sample.WorldX, 0, sample.WorldZ, settings, out facts);
                    sample.V2Valid = facts.IsValid;
                    sample.V2Elevation = facts.Elevation;

                    sample.IsExact = sample.OffsetX < 0.0001f && sample.OffsetZ < 0.0001f;
                    sample.IsBoundary = ix == 0 || ix == field.SampleCountX - 1 ||
                                        iz == 0 || iz == field.SampleCountZ - 1;
                    sample.IsCutaway = cutAppliesToChunk &&
                                       localPosition.x >= cutMinX && localPosition.x <= cutMaxX;

                    sample.IsValid = sample.LegacyFound && sample.V2Ok && sample.V2Valid;
                    if (sample.IsValid)
                    {
                        sample.SignedDiff = sample.LegacySurface - sample.V2Elevation;
                        sample.AbsDiff = Mathf.Abs(sample.SignedDiff);
                    }
                    else
                    {
                        sample.SignedDiff = float.NaN;
                        sample.AbsDiff = float.NaN;
                    }

                    samples[sampleIndex] = sample;
                    sampleIndex++;
                }
            }
        }

        Check(fieldsGenerated == totalChunks,
            "Legacy：为全部 " + totalChunks.ToString(CultureInfo.InvariantCulture) +
            " 个 Chunk 成功生成 DensityField（实际 " + fieldsGenerated.ToString(CultureInfo.InvariantCulture) + "）");
        Check(sampleIndex == totalChunks * samplesPerChunk,
            "覆盖：采样总数 = " + sampleIndex.ToString(CultureInfo.InvariantCulture) + "（每 Chunk " +
            samplesPerChunk.ToString(CultureInfo.InvariantCulture) + " 列 × " + totalChunks + "）");
        Check(legacyNotFound == 0 && legacyDegenerate == 0,
            "Legacy：全部列取到地表高度（未取到 " + legacyNotFound.ToString(CultureInfo.InvariantCulture) +
            " / 退化 " + legacyDegenerate.ToString(CultureInfo.InvariantCulture) + "）");
        Check(crossCheckFailures == 0,
            "Legacy：符号变化插值与生产公式复刻一致（最大偏差 " +
            maxCrossCheckDelta.ToString("F6", CultureInfo.InvariantCulture) + " ≤ 0.01）");
        Check(Approximately(cellX, voxelSize) && !Approximately(cellZ, cellX),
            "坐标：CellSize = Size/Resolution = (X=" + cellX.ToString("F4", CultureInfo.InvariantCulture) +
            ", Z=" + cellZ.ToString("F4", CultureInfo.InvariantCulture) +
            ") ⇒ X 与 voxelSize 一致、Z 不同（各向异性采样 ⇒ Z 整数列有限）");

        // ---- 每 Chunk 采样数 + 相位差异（覆盖非平凡的证明）----
        int[] perChunkCounts = new int[totalChunks];
        for (int i = 0; i < samples.Length; i++) { perChunkCounts[samples[i].ChunkIndex]++; }

        bool eachChunkCovered = true;
        for (int i = 0; i < totalChunks; i++)
        {
            if (perChunkCounts[i] != samplesPerChunk) { eachChunkCovered = false; }
        }
        Check(eachChunkCovered,
            "覆盖：每个 Chunk 均采到 " + samplesPerChunk.ToString(CultureInfo.InvariantCulture) + " 列");

        int chunksWithDifferentPhase = 0;
        for (int chunkIndex = 1; chunkIndex < totalChunks; chunkIndex++)
        {
            bool anyDifferent = false;
            for (int j = 0; j < samplesPerChunk; j++)
            {
                Sample reference = samples[j];
                Sample current = samples[chunkIndex * samplesPerChunk + j];
                if (!Approximately(reference.LegacySurface, current.LegacySurface)) { anyDifferent = true; break; }
            }
            if (anyDifferent) { chunksWithDifferentPhase++; }
        }
        Check(chunksWithDifferentPhase == totalChunks - 1,
            "覆盖：其余 " + (totalChunks - 1).ToString(CultureInfo.InvariantCulture) +
            " 个 Chunk 的同索引列地表值与 chunk(0,0,0) 不同（噪声相位随 origin 变化；实际差异 Chunk 数 " +
            chunksWithDifferentPhase.ToString(CultureInfo.InvariantCulture) + "）⇒ 仅测 1 个 Chunk 不足以代表全场景");

        // ================= 4. V2 正确性与错列检测 =================
        int v2Failures = 0;
        int v2Invalid = 0;
        int misaligned = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            if (!samples[i].V2Ok) { v2Failures++; }
            if (!samples[i].V2Valid) { v2Invalid++; }

            int independent = HeightFunction.GetTerrainHeight(samples[i].WorldX, samples[i].WorldZ, settings);
            if (samples[i].V2Ok && samples[i].V2Elevation != independent) { misaligned++; }
        }
        Check(v2Failures == 0 && v2Invalid == 0,
            "V2：全部样本查询成功且 IsValid==true（失败 " + v2Failures.ToString(CultureInfo.InvariantCulture) +
            " / 无效 " + v2Invalid.ToString(CultureInfo.InvariantCulture) + "）");
        Check(misaligned == 0,
            "V2：逐样本无错列/错坐标（facts.Elevation == HeightFunction(worldX,worldZ)，不一致 " +
            misaligned.ToString(CultureInfo.InvariantCulture) + "）");

        // ================= 5. 分组统计 =================
        Group all = new Group("全部样本");
        Group[] perChunk = new Group[totalChunks];
        for (int i = 0; i < totalChunks; i++) { perChunk[i] = new Group("Chunk"); }
        Group exact = new Group("X/Z 完全对应");
        Group offset = new Group("存在坐标偏移");
        Group boundary = new Group("Chunk 边界样本");
        Group cutaway = new Group("debugCutaway 区样本");
        Group nonCutaway = new Group("非剖切区样本");

        for (int i = 0; i < samples.Length; i++)
        {
            Sample sample = samples[i];
            all.Add(sample);
            perChunk[sample.ChunkIndex].Add(sample);
            if (sample.IsExact) { exact.Add(sample); } else { offset.Add(sample); }
            if (sample.IsBoundary) { boundary.Add(sample); }
            if (sample.IsCutaway) { cutaway.Add(sample); } else { nonCutaway.Add(sample); }
        }

        int perChunkSum = 0;
        for (int i = 0; i < totalChunks; i++) { perChunkSum += perChunk[i].Total; }

        Check(perChunkSum == all.Total,
            "统计：各 Chunk 样本数之和 == 全部样本数（" + perChunkSum.ToString(CultureInfo.InvariantCulture) + "）");
        Check(exact.Total + offset.Total == all.Total,
            "统计：完全对应 " + exact.Total.ToString(CultureInfo.InvariantCulture) + " + 偏移 " +
            offset.Total.ToString(CultureInfo.InvariantCulture) + " == 全部 " +
            all.Total.ToString(CultureInfo.InvariantCulture));
        Check(cutaway.Total + nonCutaway.Total == all.Total,
            "统计：剖切区 " + cutaway.Total.ToString(CultureInfo.InvariantCulture) + " + 非剖切区 " +
            nonCutaway.Total.ToString(CultureInfo.InvariantCulture) + " == 全部 " +
            all.Total.ToString(CultureInfo.InvariantCulture));
        Check(exact.Total == totalChunks * SampleIX.Length * 2,
            "统计：完全对应组 = " + exact.Total.ToString(CultureInfo.InvariantCulture) +
            " 列（每 Chunk 仅 iz∈{0,75} 的 Z 为整数 ⇒ 6×2=12/Chunk）；其余 " +
            offset.Total.ToString(CultureInfo.InvariantCulture) + " 列存在 Z 舍入偏移");

        double recomputedAbsSum = 0d;
        int recomputedValid = 0;
        float recomputedMaxAbs = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            if (!samples[i].IsValid) { continue; }
            recomputedValid++;
            recomputedAbsSum += samples[i].AbsDiff;
            if (samples[i].AbsDiff > recomputedMaxAbs) { recomputedMaxAbs = samples[i].AbsDiff; }
        }
        float recomputedMeanAbs = recomputedValid > 0 ? (float)(recomputedAbsSum / recomputedValid) : 0f;
        Check(recomputedValid == all.Valid &&
              Mathf.Abs(recomputedMeanAbs - all.MeanAbs) < 0.0005f &&
              Mathf.Abs(recomputedMaxAbs - all.MaxAbs) < 0.0005f,
            "统计：由逐样本原始数据重算 == 分组统计（valid " +
            recomputedValid.ToString(CultureInfo.InvariantCulture) + " / meanAbs " +
            recomputedMeanAbs.ToString("F4", CultureInfo.InvariantCulture) + " / maxAbs " +
            recomputedMaxAbs.ToString("F4", CultureInfo.InvariantCulture) + "）");

        // ================= 6. 稳定性 =================
        int v2Unstable = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            WorldSkeletonFacts again;
            WorldSkeletonProvider.TryGetFacts(samples[i].WorldX, 0, samples[i].WorldZ, settings, out again);
            if (again.Elevation != samples[i].V2Elevation) { v2Unstable++; }
        }
        Check(v2Unstable == 0,
            "稳定性：V2 重复查询逐样本一致（不一致 " + v2Unstable.ToString(CultureInfo.InvariantCulture) + "）");

        MaterialField repeatMaterial;
        DensityField repeatField = DensityFieldGenerator.Generate(
            DensityGenerationMode.HeightField, resolution, chunkOrigins[totalChunks - 1], chunkWorldSize,
            useNoise, height, noiseScale, noiseStrength, new Vector3(cavityX, cavityY, cavityZ), cavityRadius,
            grassDepth, dirtDepth, useLegacyMaterialRules, weatheringStrength, weatheringDepth,
            gentleSlopeAngle, steepSlopeAngle, gravityStrength, soilIterations, grassStrength,
            out repeatMaterial);

        int legacyUnstable = 0;
        for (int j = 0; j < samplesPerChunk; j++)
        {
            Sample sample = samples[(totalChunks - 1) * samplesPerChunk + j];
            int ix = SampleIX[j / SampleIZ.Length];
            int iz = SampleIZ[j % SampleIZ.Length];

            float repeatSurface;
            bool repeatDegenerate;
            TryDeriveColumnSurface(repeatField, ix, iz, out repeatSurface, out repeatDegenerate);
            if (!Approximately(repeatSurface, sample.LegacySurface)) { legacyUnstable++; }
        }
        Check(legacyUnstable == 0,
            "稳定性：最后一个 Chunk 重新生成后列高逐值一致（不一致 " +
            legacyUnstable.ToString(CultureInfo.InvariantCulture) + "）");

        // ================= 7. 失败路径（独立记录，不计入统计）=================
        int validBeforeFailureProbes = all.Valid;
        WorldSkeletonFacts belowZero;
        bool belowOk = WorldSkeletonProvider.TryGetFacts(samples[0].WorldX, -1, samples[0].WorldZ, settings, out belowZero);
        WorldSkeletonFacts atTop;
        bool topOk = WorldSkeletonProvider.TryGetFacts(
            samples[0].WorldX, settings.WorldHeight, samples[0].WorldZ, settings, out atTop);
        WorldGenerationSettings emptySettings = default(WorldGenerationSettings);
        WorldSkeletonFacts emptyFacts;
        bool emptyOk = WorldSkeletonProvider.TryGetFacts(
            samples[0].WorldX, 0, samples[0].WorldZ, emptySettings, out emptyFacts);

        Check(!belowOk && !belowZero.IsValid, "失败路径①：Y=-1 ⇒ 查询失败且事实无效（独立记录）");
        Check(!topOk && !atTop.IsValid,
            "失败路径②：Y=WorldHeight(" + settings.WorldHeight.ToString(CultureInfo.InvariantCulture) +
            ") ⇒ 失败且事实无效（独立记录）");
        Check(!emptyOk && !emptyFacts.IsValid,
            "失败路径③：default(WorldGenerationSettings) ⇒ 失败且事实无效（独立记录）");
        Check(all.Valid == validBeforeFailureProbes,
            "失败路径样本未被计入有效统计（有效数仍为 " + all.Valid.ToString(CultureInfo.InvariantCulture) + "）");

        // ================= 8. 隔离性后置 =================
        string sceneHashAfter = ComputeFileHash(scenePath);
        Check(sceneHashBefore == sceneHashAfter,
            "隔离性：场景文件 SHA256 前后一致（" + ShortHash(sceneHashBefore) + "）⇒ 本轮未写入场景");
        Check(Resources.FindObjectsOfTypeAll<GameObject>().Length == gameObjectsBefore,
            "隔离性：GameObject 数量未变 ⇒ 未创建任何测试对象");
        Check(Resources.FindObjectsOfTypeAll<DensityTerrain>().Length == terrainInstancesBefore,
            "隔离性：DensityTerrain 实例数未变");
        Check(!SceneManager.GetActiveScene().isDirty, "隔离性：当前场景未被标记为脏");

        // ================= 9. 输出：分组统计 + 逐样本原始数据 =================
        Debug.Log(all.Describe());
        for (int i = 0; i < totalChunks; i++)
        {
            Debug.Log("[R46] Chunk " + chunkCoordinates[i].x + "," + chunkCoordinates[i].y + "," +
                      chunkCoordinates[i].z + " 原点(" +
                      chunkOrigins[i].x.ToString("F0", CultureInfo.InvariantCulture) + "," +
                      chunkOrigins[i].y.ToString("F0", CultureInfo.InvariantCulture) + "," +
                      chunkOrigins[i].z.ToString("F0", CultureInfo.InvariantCulture) + ") ⇒ " +
                      perChunk[i].Describe());
        }
        Debug.Log(exact.Describe());
        Debug.Log(offset.Describe());
        Debug.Log(boundary.Describe());
        Debug.Log(cutaway.Describe());
        Debug.Log(nonCutaway.Describe());

        Debug.Log("[R46] 逐样本原始数据（idx | chunk | localX,localZ | worldX,worldZ | offX,offZ | legacy | v2 | signed | abs | boundary | cutaway | coordClass | validity）");
        for (int i = 0; i < samples.Length; i++)
        {
            Sample sample = samples[i];
            StringBuilder line = new StringBuilder();
            line.Append("[R46] ").Append(i.ToString("D3", CultureInfo.InvariantCulture)).Append(" | ")
                .Append(sample.ChunkLabel).Append(" | ")
                .Append(sample.LocalX.ToString("F4", CultureInfo.InvariantCulture)).Append(",")
                .Append(sample.LocalZ.ToString("F4", CultureInfo.InvariantCulture)).Append(" | ")
                .Append(sample.WorldX.ToString(CultureInfo.InvariantCulture)).Append(",")
                .Append(sample.WorldZ.ToString(CultureInfo.InvariantCulture)).Append(" | ")
                .Append(sample.OffsetX.ToString("F4", CultureInfo.InvariantCulture)).Append(",")
                .Append(sample.OffsetZ.ToString("F4", CultureInfo.InvariantCulture)).Append(" | ")
                .Append(sample.LegacySurface.ToString("F4", CultureInfo.InvariantCulture)).Append(" | ")
                .Append(sample.V2Elevation.ToString(CultureInfo.InvariantCulture)).Append(" | ")
                .Append(sample.SignedDiff.ToString("F4", CultureInfo.InvariantCulture)).Append(" | ")
                .Append(sample.AbsDiff.ToString("F4", CultureInfo.InvariantCulture)).Append(" | ")
                .Append(sample.IsBoundary ? "B" : "-").Append(" | ")
                .Append(sample.IsCutaway ? "CUT" : "-").Append(" | ")
                .Append(sample.IsExact ? "EXACT" : "OFFSET").Append(" | ")
                .Append(sample.IsValid ? "valid" : "INVALID");
            Debug.Log(line.ToString());
        }

        // ================= 10. 判定语义 + 退出码 =================
        Check(all.MaxAbs > 0f && failures == 0,
            "判定语义：已实测全场景高度差异（maxAbs=" +
            all.MaxAbs.ToString("F4", CultureInfo.InvariantCulture) +
            "）但未产生任何失败 ⇒ 高度不等不被当作生产故障");

        Check(ExitCodeFor(0) == 0, "退出码：失败计数 0 ⇒ 0");
        Check(ExitCodeFor(1) == 1, "退出码：失败计数 1 ⇒ 1");
        Check(ExitCodeFor(6) == 1, "退出码：失败计数 6 ⇒ 1");

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
            Debug.LogError("[R46] 批处理失败（exit 1）：" + exception.Message);
            EditorApplication.Exit(1);
            return;
        }

        EditorApplication.Exit(ExitCodeFor(failures));
    }

    // -----------------------------------------------------------------
    // 纯辅助（无世界规则）
    // -----------------------------------------------------------------

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
            Debug.LogError("[R46] 无法计算场景哈希：" + exception.Message);
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
            Debug.Log("[R46] PASS: " + label);
        }
        else
        {
            failures++;
            Debug.LogError("[R46] FAIL: " + label);
        }
    }

    private static void Report()
    {
        StringBuilder summary = new StringBuilder();
        summary.Append("[R46] Legacy 全场景（4 Chunk）影子对比 ⇒ checks=")
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
