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

/// <summary>
/// **R45 — Legacy 实际场景与 R44 影子采样对应关系核验**（纯只读：只读场景文件，不加载场景）。
///
/// 回答一个问题：R44 测量的 36 列高度差，在多大程度上对应当前实际场景的地表生成条件？
///
/// 方法（**不加载 SampleScene、不实例化任何对象**）：
///   ① 以文本方式只读 `Assets/Scenes/SampleScene.unity`，按脚本 guid 定位 `DensityTerrain`
///      的序列化组件块，逐字段解析实际参数；
///   ② 沿 `m_GameObject` → GameObject → `m_Component` → Transform 解析根 Transform，
///      核实"根为恒等变换（Local == World）"这一 R44 临时映射的前提；
///   ③ 把解析值与 R44 验证器（`V2SkeletonShadowComparisonValidation`）使用的常量**逐项对照**；
///   ④ 用解析出的 chunkCount / chunkVoxelSize / voxelSize / resolution / debugCutaway 计算
///      R44 采样的**覆盖范围**与**坐标对应缺口**（CellSize、chunk 覆盖率、Z 舍入错位列数、
///      debugCutaway 影响列数、边界列）；
///   ⑤ 前后对场景文件取 SHA256，证明本验证器**未修改**场景。
///
/// 明确**不做**：不加载场景（避免 `OnEnable → Regenerate` 创建 Chunk 对象并污染场景）、
/// 不实例化 `DensityTerrain`、不改任何生产文件、不重钉哈希、不把临时映射当正式契约。
///
/// 关键前提不满足时**返回失败**（不以 PASS 静默跳过）：场景缺失 / 组件块未找到 / 参数不可解析 /
/// 根 Transform 非恒等（映射前提失效）/ 参数与 R44 不一致（除已声明的不适用项）。
///
/// 菜单：`Tools/World V2/Validate R45 Legacy Scene Correspondence`
/// 批处理：`Unity -batchmode -quit -executeMethod R45LegacySceneCorrespondenceValidation.RunBatch`
/// 退出码自检：追加 `-r45SelfFailure` ⇒ 注入一条故意失败（期望 exit 1）
/// </summary>
public static class R45LegacySceneCorrespondenceValidation
{
    private const string SelfFailureSwitch = "-r45SelfFailure";

    private const string SceneRelativePath = "Assets/Scenes/SampleScene.unity";
    private const string DensityTerrainScriptGuid = "3af3bcfc875f4cbfae75f1f95dee772a";

    // ---- R44 验证器实际使用的常量（来源：Assets/Editor/V2SkeletonShadowComparisonValidation.cs）----
    private const int R44Resolution = 75;
    private const float R44ChunkSizeX = 75f;
    private const float R44ChunkSizeY = 37f;
    private const float R44ChunkSizeZ = 50f;
    private const float R44Height = 15.7f;
    private const float R44NoiseScale = 0.15f;
    private const float R44NoiseStrength = 2.37f;
    private const float R44CavityX = 15.5f;
    private const float R44CavityY = 5.23f;
    private const float R44CavityZ = 19.1f;
    private const float R44CavityRadius = 6.13f;
    private const float R44GrassDepth = 1.9f;
    private const float R44DirtDepth = 0.65f;
    private const bool R44UseLegacyMaterialRules = false;
    private const float R44WeatheringStrength = 0.8f;
    private const float R44WeatheringDepth = 8f;
    private const float R44GentleSlopeAngle = 15f;
    private const float R44SteepSlopeAngle = 45f;
    private const float R44GravityStrength = 1f;
    private const int R44SoilSimulationIterations = 4;
    private const float R44GrassStrength = 1f;

    // R44 的采样网格索引（来源同上：sampleIX / sampleIZ）
    private static readonly int[] R44SampleIX = { 0, 1, 19, 38, 57, 75 };
    private static readonly int[] R44SampleIZ = { 0, 1, 13, 26, 38, 75 };

    private static int failures;
    private static int checks;

    [MenuItem("Tools/World V2/Validate R45 Legacy Scene Correspondence")]
    public static void Validate()
    {
        failures = 0;
        checks = 0;

        string scenePath = Path.Combine(Application.dataPath, "Scenes", "SampleScene.unity");

        // ================= 0. 隔离性快照（本验证器不加载场景）=================
        bool sceneLoaded = SceneManager.GetActiveScene().IsValid() &&
                           !string.IsNullOrEmpty(SceneManager.GetActiveScene().path);
        int terrainInstances = Resources.FindObjectsOfTypeAll<DensityTerrain>().Length;
        int gameObjectsBefore = Resources.FindObjectsOfTypeAll<GameObject>().Length;
        string sceneHashBefore = ComputeFileHash(scenePath);

        Check(!sceneLoaded,
            "隔离性：未加载任何场景资源（activeScene.path 为空 ⇒ 未打开 SampleScene）");
        Check(terrainInstances == 0,
            "隔离性：场景中没有 DensityTerrain 实例（" + terrainInstances.ToString(CultureInfo.InvariantCulture) +
            "）⇒ 未触发 OnEnable/Regenerate 副作用");

        // ================= 1. 前置：场景文件可读 =================
        if (!File.Exists(scenePath))
        {
            Check(false, "前置：场景文件存在（缺失 " + scenePath + "）⇒ 不具备验证条件");
            Report();
            return;
        }

        string sceneText = File.ReadAllText(scenePath);
        Check(sceneText.Length > 0, "前置：场景文件非空（" + sceneText.Length.ToString(CultureInfo.InvariantCulture) + " 字符）");

        // ================= 2. 定位 DensityTerrain 组件块并解析字段 =================
        string componentBlock;
        string gameObjectId;
        if (!TryExtractDensityTerrainBlock(sceneText, out componentBlock, out gameObjectId))
        {
            Check(false, "前置：在场景中找到 DensityTerrain 组件块 ⇒ 不具备验证条件");
            Report();
            return;
        }

        Check(true, "前置：定位到 DensityTerrain 组件块（m_Script guid=" + DensityTerrainScriptGuid + "）");

        Dictionary<string, string> fields = ParseKeyValues(componentBlock);

        string enabledText;
        bool enabled = fields.TryGetValue("m_Enabled", out enabledText) && enabledText.Trim() == "1";
        Check(enabled, "场景：DensityTerrain 组件 m_Enabled = 1（实际 " + Value(fields, "m_Enabled") + "）");

        // ---- 逐字段解析 ----
        int resolution = (int)ParseNumber(fields, "resolution");
        float voxelSize = ParseNumber(fields, "voxelSize");
        int chunkVoxelX, chunkVoxelY, chunkVoxelZ;
        int chunkCountX, chunkCountY, chunkCountZ;
        int chunkStartX, chunkStartY, chunkStartZ;
        int densityMode = (int)ParseNumber(fields, "densityMode");
        bool useNoise = ParseNumber(fields, "useNoise") != 0f;
        float height = ParseNumber(fields, "height");
        float noiseScale = ParseNumber(fields, "noiseScale");
        float noiseStrength = ParseNumber(fields, "noiseStrength");
        float cavityX, cavityY, cavityZ, cavityRadius;
        bool useLegacyMaterialRules = ParseNumber(fields, "useLegacyMaterialRules") != 0f;
        float grassDepth = ParseNumber(fields, "grassDepth");
        float dirtDepth = ParseNumber(fields, "dirtDepth");
        float weatheringStrength = ParseNumber(fields, "weatheringStrength");
        float weatheringDepth = ParseNumber(fields, "weatheringDepth");
        float gentleSlopeAngle = ParseNumber(fields, "gentleSlopeAngle");
        float steepSlopeAngle = ParseNumber(fields, "steepSlopeAngle");
        float gravityStrength = ParseNumber(fields, "gravityStrength");
        int soilIterations = (int)ParseNumber(fields, "soilSimulationIterations");
        float grassStrength = ParseNumber(fields, "grassStrength");
        bool debugCutaway = ParseNumber(fields, "debugCutaway") != 0f;
        float debugCutX = ParseNumber(fields, "debugCutX");
        float debugCutWidth = ParseNumber(fields, "debugCutWidth");
        bool usePrecut = ParseNumber(fields, "useVoxelAwareMeshPrecut") != 0f;

        if (!TryParseVectorInt(fields, "chunkVoxelSize", out chunkVoxelX, out chunkVoxelY, out chunkVoxelZ) ||
            !TryParseVectorInt(fields, "chunkCount", out chunkCountX, out chunkCountY, out chunkCountZ) ||
            !TryParseVectorInt(fields, "chunkStartCoordinate", out chunkStartX, out chunkStartY, out chunkStartZ) ||
            !TryParseVectorFloat(fields, "cavityCenterOffset", out cavityX, out cavityY, out cavityZ))
        {
            Check(false, "前置：关键向量字段可解析（chunkVoxelSize/chunkCount/chunkStartCoordinate/cavityCenterOffset）");
            Report();
            return;
        }

        cavityRadius = ParseNumber(fields, "cavityRadius");

        Check(true,
            "场景：密度模式 = " + densityMode.ToString(CultureInfo.InvariantCulture) +
            "（0 = HeightField）；useNoise = " + useNoise +
            "；resolution = " + resolution.ToString(CultureInfo.InvariantCulture) +
            "；chunkVoxelSize = (" + chunkVoxelX + "," + chunkVoxelY + "," + chunkVoxelZ + ")" +
            "；chunkCount = (" + chunkCountX + "," + chunkCountY + "," + chunkCountZ + ")" +
            "；voxelSize = " + voxelSize.ToString("F3", CultureInfo.InvariantCulture));

        // ================= 3. A 节：场景参数 vs R44 参数 逐项对照 =================
        Check(densityMode == 0, "A1 densityMode：场景 HeightField == R44 HeightField");
        Check(useNoise, "A2 useNoise：场景 true == R44 true");
        Check(resolution == R44Resolution,
            "A3 resolution：场景 " + resolution.ToString(CultureInfo.InvariantCulture) + " == R44 " +
            R44Resolution.ToString(CultureInfo.InvariantCulture));
        Check(Approximately(chunkVoxelX, R44ChunkSizeX) && Approximately(chunkVoxelY, R44ChunkSizeY) &&
              Approximately(chunkVoxelZ, R44ChunkSizeZ),
            "A4 chunkVoxelSize×voxelSize：场景 (" + chunkVoxelX + "," + chunkVoxelY + "," + chunkVoxelZ +
            ") == R44 size (" + R44ChunkSizeX + "," + R44ChunkSizeY + "," + R44ChunkSizeZ + ")");
        Check(Approximately(height, R44Height), "A5 height：场景 " + height.ToString("F4", CultureInfo.InvariantCulture) +
                                                 " == R44 " + R44Height.ToString("F4", CultureInfo.InvariantCulture));
        Check(Approximately(noiseScale, R44NoiseScale), "A6 noiseScale：场景 " + noiseScale.ToString("F4", CultureInfo.InvariantCulture) +
                                                          " == R44 " + R44NoiseScale.ToString("F4", CultureInfo.InvariantCulture));
        Check(Approximately(noiseStrength, R44NoiseStrength), "A7 noiseStrength：场景 " + noiseStrength.ToString("F4", CultureInfo.InvariantCulture) +
                                                                " == R44 " + R44NoiseStrength.ToString("F4", CultureInfo.InvariantCulture));
        Check(Approximately(cavityX, R44CavityX) && Approximately(cavityY, R44CavityY) && Approximately(cavityZ, R44CavityZ) &&
              Approximately(cavityRadius, R44CavityRadius),
            "A8 cavity 参数：场景 (" + cavityX + "," + cavityY + "," + cavityZ + ")/" + cavityRadius +
            " == R44（注意：HeightField 模式下该组参数不参与高度计算）");
        Check(useLegacyMaterialRules == R44UseLegacyMaterialRules,
            "A9 useLegacyMaterialRules：场景 " + useLegacyMaterialRules + " == R44 " + R44UseLegacyMaterialRules +
            "（仅影响材料流水线，不影响高度）");
        Check(Approximately(grassDepth, R44GrassDepth) && Approximately(dirtDepth, R44DirtDepth),
            "A10 grassDepth/dirtDepth：场景 " + grassDepth + "/" + dirtDepth + " == R44（仅材料）");
        Check(Approximately(weatheringStrength, R44WeatheringStrength) &&
              Approximately(weatheringDepth, R44WeatheringDepth) &&
              Approximately(gentleSlopeAngle, R44GentleSlopeAngle) &&
              Approximately(steepSlopeAngle, R44SteepSlopeAngle) &&
              Approximately(gravityStrength, R44GravityStrength) &&
              soilIterations == R44SoilSimulationIterations &&
              Approximately(grassStrength, R44GrassStrength),
            "A11 材料 V2 流水线 7 参数：场景与 R44 全等（仅材料，不影响高度）");
        Check(chunkStartX == 0 && chunkStartY == 0 && chunkStartZ == 0,
            "A12 chunkStartCoordinate：场景 (" + chunkStartX + "," + chunkStartY + "," + chunkStartZ +
            ") ⇒ 首个 Chunk 原点即 R44 使用的 origin=(0,0,0)");

        // ---- 已声明的差异项（必须显式记录，不能算作"一致"）----
        int totalChunks = Mathf.Max(1, chunkCountX) * Mathf.Max(1, chunkCountY) * Mathf.Max(1, chunkCountZ);
        Check(totalChunks > 1,
            "A13【差异·覆盖】场景 chunkCount=(" + chunkCountX + "," + chunkCountY + "," + chunkCountZ + ") ⇒ 共 " +
            totalChunks.ToString(CultureInfo.InvariantCulture) + " 个 Chunk，R44 只生成 1 个 ⇒ 覆盖率 " +
            ((1f / totalChunks) * 100f).ToString("F1", CultureInfo.InvariantCulture) + "%");
        Check(debugCutaway,
            "A14【差异·有意排除】场景 debugCutaway=" + debugCutaway + "（剖切 x∈[" +
            (debugCutX - debugCutWidth * 0.5f).ToString("F2", CultureInfo.InvariantCulture) + ", " +
            (debugCutX + debugCutWidth * 0.5f).ToString("F2", CultureInfo.InvariantCulture) +
            "]），R44 未施加该剖切");
        Check(usePrecut,
            "A15【差异·不影响高度】场景 useVoxelAwareMeshPrecut=" + usePrecut +
            "（只影响网格几何，不影响 DensityField/高度），R44 未使用该路径");

        // R44 未使用、且场景中不参与当前 Chunk 尺寸推导的字段（记录为"不适用"）
        string chunkSizeText = Value(fields, "chunkSize");
        string terrainSizeText = Value(fields, "terrainSize");
        Check(chunkVoxelX > 0 && chunkVoxelY > 0 && chunkVoxelZ > 0,
            "A16 chunkSize/terrainSize【不适用】chunkVoxelSize 显式非零 ⇒ 尺寸推导分支不触发" +
            "（场景 chunkSize=" + chunkSizeText + "，terrainSize=" + terrainSizeText + "）");

        // ================= 4. 根 Transform 与映射前提 =================
        string transformBlock;
        if (TryExtractTransformBlock(sceneText, gameObjectId, out transformBlock))
        {
            Check(IsIdentityTransform(transformBlock),
                "B1 映射前提：DensityTerrain 根 Transform 为恒等变换（位置 0 / 旋转 identity / 缩放 1 / 无父节点）" +
                " ⇒ 该场景下 Terrain Local Space 与 World Space 数值重合");
        }
        else
        {
            Check(false, "B1 映射前提：未能解析根 Transform ⇒ 无法确认 Local==World ⇒ 不具备验证条件");
        }

        // ================= 5. B 节：坐标对应量化 =================
        float cellX = (float)chunkVoxelX / resolution;
        float cellY = (float)chunkVoxelY / resolution;
        float cellZ = (float)chunkVoxelZ / resolution;
        Check(Approximately(cellX, voxelSize) && !Approximately(cellZ, cellX),
            "B2 CellSize 由 size/resolution 决定（X=" + cellX.ToString("F4", CultureInfo.InvariantCulture) +
            " / Y=" + cellY.ToString("F4", CultureInfo.InvariantCulture) +
            " / Z=" + cellZ.ToString("F4", CultureInfo.InvariantCulture) +
            "）⇒ X 步长为 1（整数对齐），Z 步长 " + cellZ.ToString("F4", CultureInfo.InvariantCulture));

        int sampleCount = R44SampleIX.Length * R44SampleIZ.Length;
        int zNonIntegerColumns = 0;
        float maxRoundingErrorZ = 0f;
        for (int a = 0; a < R44SampleIX.Length; a++)
        {
            for (int b = 0; b < R44SampleIZ.Length; b++)
            {
                float localZ = R44SampleIZ[b] * cellZ;
                int worldZ = Mathf.RoundToInt(localZ);
                float error = Mathf.Abs(localZ - worldZ);
                if (error > maxRoundingErrorZ) { maxRoundingErrorZ = error; }
                if (error > 0.0001f) { zNonIntegerColumns++; }
            }
        }

        Check(zNonIntegerColumns == 24,
            "B3【坐标缺口】Z 舍入错位列数 = " + zNonIntegerColumns.ToString(CultureInfo.InvariantCulture) +
            "/" + sampleCount.ToString(CultureInfo.InvariantCulture) +
            "（Z 步长 " + cellZ.ToString("F4", CultureInfo.InvariantCulture) +
            " ⇒ 仅有 iz∈{0,75} 两行为整数；最大舍入误差 " +
            maxRoundingErrorZ.ToString("F4", CultureInfo.InvariantCulture) + "）");

        float cutMinX = debugCutX - debugCutWidth * 0.5f;
        float cutMaxX = debugCutX + debugCutWidth * 0.5f;
        int cutAffectedColumns = 0;
        for (int a = 0; a < R44SampleIX.Length; a++)
        {
            float localX = R44SampleIX[a] * cellX;
            if (localX >= cutMinX && localX <= cutMaxX) { cutAffectedColumns += R44SampleIZ.Length; }
        }

        Check(cutAffectedColumns == 12,
            "B4【坐标缺口】落在 debugCutaway 区间的采样列 = " +
            cutAffectedColumns.ToString(CultureInfo.InvariantCulture) + "/" +
            sampleCount.ToString(CultureInfo.InvariantCulture) + "（局部 x∈[" +
            cutMinX.ToString("F2", CultureInfo.InvariantCulture) + ", " +
            cutMaxX.ToString("F2", CultureInfo.InvariantCulture) + "] ⇒ ix∈{0,1}）");

        int boundaryColumns = 0;
        for (int a = 0; a < R44SampleIX.Length; a++)
        {
            float localX = R44SampleIX[a] * cellX;
            if (Approximately(localX, chunkVoxelX)) { boundaryColumns += R44SampleIZ.Length; }
        }
        for (int b = 0; b < R44SampleIZ.Length; b++)
        {
            float localZ = R44SampleIZ[b] * cellZ;
            if (Approximately(localZ, chunkVoxelZ)) { boundaryColumns += R44SampleIX.Length; }
        }

        Check(boundaryColumns > 0,
            "B5【边界说明】落在 Chunk 采样边界上的列 = " + boundaryColumns.ToString(CultureInfo.InvariantCulture) +
            "（含 x=" + chunkVoxelX + " 与 z=" + chunkVoxelZ + " 的共享边样本，属 chunk(0,0,0) 采样范围内的最外圈）");

        // chunk 原点（scene 语义：chunkCoord × chunkVoxelSize × voxelSize）
        StringBuilder origins = new StringBuilder();
        int originCount = 0;
        for (int cx = 0; cx < Mathf.Max(1, chunkCountX); cx++)
        {
            for (int cz = 0; cz < Mathf.Max(1, chunkCountZ); cz++)
            {
                Vector3Int coordinate = new Vector3Int(chunkStartX + cx, chunkStartY, chunkStartZ + cz);
                Vector3 origin = new Vector3(
                    coordinate.x * (chunkVoxelX * voxelSize),
                    coordinate.y * (chunkVoxelY * voxelSize),
                    coordinate.z * (chunkVoxelZ * voxelSize));
                if (originCount > 0) { origins.Append(" | "); }
                origins.Append("(").Append(origin.x.ToString("F0", CultureInfo.InvariantCulture)).Append(",")
                       .Append(origin.y.ToString("F0", CultureInfo.InvariantCulture)).Append(",")
                       .Append(origin.z.ToString("F0", CultureInfo.InvariantCulture)).Append(")");
                originCount++;
            }
        }
        Check(originCount == totalChunks,
            "B6 Chunk 原点枚举 = " + originCount.ToString(CultureInfo.InvariantCulture) +
            " 个：" + origins + "；R44 采样全部落在第 1 个 origin=(0,0,0) 的 Chunk 内");

        // ================= 6. 稳定性：重复解析一致 =================
        string componentBlock2;
        string gameObjectId2;
        bool secondOk = TryExtractDensityTerrainBlock(sceneText, out componentBlock2, out gameObjectId2);
        Check(secondOk && componentBlock2 == componentBlock && gameObjectId2 == gameObjectId,
            "稳定性：两次独立解析得到完全相同的组件块（确定性）");

        // ================= 7. 隔离性后置：场景文件哈希未变 =================
        string sceneHashAfter = ComputeFileHash(scenePath);
        Check(sceneHashBefore == sceneHashAfter,
            "隔离性：场景文件 SHA256 前后一致（" + ShortHash(sceneHashBefore) + "）⇒ 本轮未写入场景");
        Check(Resources.FindObjectsOfTypeAll<GameObject>().Length == gameObjectsBefore,
            "隔离性：GameObject 数量未变化 ⇒ 未创建任何测试对象");
        Check(!SceneManager.GetActiveScene().isDirty,
            "隔离性：当前场景未被标记为脏");

        // ================= 8. 退出码映射 + 自检 =================
        Check(ExitCodeFor(0) == 0, "退出码：失败计数 0 ⇒ 0");
        Check(ExitCodeFor(1) == 1, "退出码：失败计数 1 ⇒ 1");
        Check(ExitCodeFor(5) == 1, "退出码：失败计数 5 ⇒ 1");

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
            Debug.LogError("[R45] 批处理失败（exit 1）：" + exception.Message);
            EditorApplication.Exit(1);
            return;
        }

        EditorApplication.Exit(ExitCodeFor(failures));
    }

    // -----------------------------------------------------------------
    // 只读解析辅助（无世界规则：仅文本解析与算术）
    // -----------------------------------------------------------------

    /// <summary>按脚本 guid 定位 DensityTerrain 的 MonoBehaviour 块（到下一个 `--- !u!` 为止）。</summary>
    private static bool TryExtractDensityTerrainBlock(
        string sceneText, out string block, out string gameObjectId)
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

    /// <summary>由 GameObject fileID 解析其 Transform 块（恒等性核验用）。</summary>
    private static bool TryExtractTransformBlock(string sceneText, string gameObjectId, out string transformBlock)
    {
        transformBlock = null;
        if (string.IsNullOrEmpty(gameObjectId)) { return false; }

        // 1) 定位 GameObject 块，收集其 m_Component 列表中的 fileID
        string gameObjectHeader = "--- !u!1 &" + gameObjectId;
        int goStart = sceneText.IndexOf(gameObjectHeader, StringComparison.Ordinal);
        if (goStart < 0) { return false; }

        int goEnd = sceneText.IndexOf("--- !u!", goStart + 10, StringComparison.Ordinal);
        if (goEnd < 0) { goEnd = sceneText.Length; }
        string gameObjectBlock = sceneText.Substring(goStart, goEnd - goStart);

        // 2) 在这些 fileID 中找第一个 `!u!4`（Transform）块
        MatchCollection components = Regex.Matches(gameObjectBlock, @"-\s*component:\s*\{fileID:\s*(\d+)\}");
        for (int i = 0; i < components.Count; i++)
        {
            string id = components[i].Groups[1].Value;
            string transformHeader = "--- !u!4 &" + id;
            int tStart = sceneText.IndexOf(transformHeader, StringComparison.Ordinal);
            if (tStart < 0) { continue; }

            int tEnd = sceneText.IndexOf("--- !u!", tStart + 10, StringComparison.Ordinal);
            if (tEnd < 0) { tEnd = sceneText.Length; }
            transformBlock = sceneText.Substring(tStart, tEnd - tStart);
            return true;
        }

        return false;
    }

    private static bool IsIdentityTransform(string transformBlock)
    {
        Dictionary<string, string> values = ParseKeyValues(transformBlock);

        float px, py, pz, rx, ry, rz, rw, sx, sy, sz;
        if (!TryParseVectorFloat(values, "m_LocalPosition", out px, out py, out pz)) { return false; }
        if (!TryParseVectorFloat(values, "m_LocalScale", out sx, out sy, out sz)) { return false; }

        Match rotation = Regex.Match(transformBlock, @"m_LocalRotation:\s*\{x:\s*(-?[\d.]+),\s*y:\s*(-?[\d.]+),\s*z:\s*(-?[\d.]+),\s*w:\s*(-?[\d.]+)\}");
        if (!rotation.Success) { return false; }
        rx = ParseFloat(rotation.Groups[1].Value);
        ry = ParseFloat(rotation.Groups[2].Value);
        rz = ParseFloat(rotation.Groups[3].Value);
        rw = ParseFloat(rotation.Groups[4].Value);

        string fatherText = Value(values, "m_Father");
        bool noParent = fatherText != null && fatherText.Contains("fileID: 0");

        return Approximately(px, 0f) && Approximately(py, 0f) && Approximately(pz, 0f) &&
               Approximately(rx, 0f) && Approximately(ry, 0f) && Approximately(rz, 0f) && Approximately(rw, 1f) &&
               Approximately(sx, 1f) && Approximately(sy, 1f) && Approximately(sz, 1f) &&
               noParent;
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
        return fields.TryGetValue(key, out value) ? value : "(缺失)";
    }

    private static float ParseNumber(Dictionary<string, string> fields, string key)
    {
        string text = Value(fields, key);
        return ParseFloat(text);
    }

    private static float ParseFloat(string text)
    {
        if (string.IsNullOrEmpty(text)) { return float.NaN; }

        string trimmed = text.Trim();
        Match vector = Regex.Match(trimmed, @"\{x:\s*(-?[\d.]+)");
        if (vector.Success) { trimmed = vector.Groups[1].Value; }

        float parsed;
        if (float.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)) { return parsed; }
        return float.NaN;
    }

    private static bool TryParseVectorInt(Dictionary<string, string> fields, string key, out int x, out int y, out int z)
    {
        x = y = z = 0;
        Match match = Regex.Match(Value(fields, key),
            @"\{x:\s*(-?\d+),\s*y:\s*(-?\d+),\s*z:\s*(-?\d+)\}");
        if (!match.Success) { return false; }

        x = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        y = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        z = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
        return true;
    }

    private static bool TryParseVectorFloat(Dictionary<string, string> fields, string key, out float x, out float y, out float z)
    {
        x = y = z = float.NaN;
        Match match = Regex.Match(Value(fields, key),
            @"\{x:\s*(-?[\d.]+),\s*y:\s*(-?[\d.]+),\s*z:\s*(-?[\d.]+)\}");
        if (!match.Success) { return false; }

        x = ParseFloat(match.Groups[1].Value);
        y = ParseFloat(match.Groups[2].Value);
        z = ParseFloat(match.Groups[3].Value);
        return true;
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
                    for (int i = 0; i < digest.Length; i++) { text.Append(digest[i].ToString("X2", CultureInfo.InvariantCulture)); }
                    return text.ToString();
                }
            }
        }
        catch (Exception exception)
        {
            Debug.LogError("[R45] 无法计算场景哈希：" + exception.Message);
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
            Debug.Log("[R45] PASS: " + label);
        }
        else
        {
            failures++;
            Debug.LogError("[R45] FAIL: " + label);
        }
    }

    private static void Report()
    {
        StringBuilder summary = new StringBuilder();
        summary.Append("[R45] Legacy 场景 × R44 采样对应核验 ⇒ checks=")
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