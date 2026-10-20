using System;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using VoxelWorld.Generation;
using World.V2;

/// <summary>
/// **Legacy DensityTerrain × World V2 影子高度对比验证**（R44 · 纯只读、无副作用）。
///
/// 目的：建立一个**可重复的差异基准**——逐列记录
///   · Legacy 侧：`DensityFieldGenerator`（真实生产函数，`HeightField` 模式）产出的
///     列地表高度（浮点，来自 `density = surface − localY` 的符号变化插值）；
///   · World V2 侧：`WorldSkeletonProvider.TryGetFacts` 返回的 `WorldSkeletonFacts.Elevation`（整数）；
/// 并统计两者的有符号差 / 绝对差 / 最大差 / 平均绝对差。
///
/// **本轮不统一两套高度函数、不接入生成流程、不改变玩家可见世界。**
///
/// 为什么不创建 `DensityTerrain` 实例（隔离性优先）：
///   · `DensityTerrain.OnEnable` 会触发 `RequestRegenerate()` → `Regenerate()` →
///     创建 Chunk GameObject + Mesh + MeshCollider（`TerrainChunkView`/`MeshFilter`…），
///     属重型副作用，且 `debugCutaway`/precut 会参与，超出本验证所需；
///   · 本验证只需要"列地表高度"，其真实来源是 `DensityFieldGenerator.Generate`
///     （`DensityTerrain.BuildChunk` 内的唯一生成调用）；该函数是 `public static` 纯函数，
///     不接触 GameObject / 场景 / Transform；
///   · 因此本验证器**不创建任何实例**，只调用该纯函数，并对场景与生产对象做前后一致性断言。
///
/// 关于 `ComputeSurfaceHeight`（`private static`）：
///   不修改其访问级别。本验证器用两条互补路径取 Legacy 列高，并互相校验：
///   ① 主路径：从 `DensityField` 的 density 符号变化线性插值（与生产 `FindSurfaceHeight` 同语义）；
///   ② 对照路径：**测试侧复刻**同一公式 `height + (Perlin(x·scale, z·scale)·2−1)·strength`，
///      仅用于证明①的取值与公式一致（差值断言 ≤ `CrossCheckTolerance`）。
///   复刻只存在于 Editor 验证器内，不进入生产代码。
///
/// 坐标映射（**仅测试用临时映射，不是正式契约**）：
///   `worldX = RoundToInt(localX)`、`worldZ = RoundToInt(localZ)`、`worldY = 0`
///   前提：地形根 Transform 为单位变换（位置为整数 0、旋转 identity、缩放 1）时
///   Terrain Local Space 与 World Space 数值重合。该前提在本验证器中被显式记录；
///   若加载场景中存在 `DensityTerrain` 实例，则额外断言其 Transform 为单位变换。
///
/// 菜单：`Tools/World V2/Validate Skeleton Shadow Comparison`
/// 批处理：`Unity -batchmode -quit -executeMethod V2SkeletonShadowComparisonValidation.RunBatch`
/// 退出码自检：追加 `-wsShadowSelfFailure` ⇒ 注入一条**故意失败**（期望 exit 1）
/// </summary>
public static class V2SkeletonShadowComparisonValidation
{
    private const string SelfFailureSwitch = "-wsShadowSelfFailure";

    // ---- Legacy 场景实测参数（来源：Assets/Scenes/SampleScene.unity 中 Terraria 的 DensityTerrain
    //      序列化字段；R43 记录 + R44 重新核验。此处仅作"输入复现"，不修改任何生产数据。）----
    private const int LegacyResolution = 75;              // resolution
    private const float LegacyChunkSizeX = 75f;           // chunkVoxelSize.x × voxelSize(1)
    private const float LegacyChunkSizeY = 37f;           // chunkVoxelSize.y
    private const float LegacyChunkSizeZ = 50f;           // chunkVoxelSize.z
    private const float LegacyHeight = 15.7f;             // height
    private const float LegacyNoiseScale = 0.15f;         // noiseScale
    private const float LegacyNoiseStrength = 2.37f;      // noiseStrength
    private const float LegacyCavityOffsetX = 15.5f;      // cavityCenterOffset
    private const float LegacyCavityOffsetY = 5.23f;
    private const float LegacyCavityOffsetZ = 19.1f;
    private const float LegacyCavityRadius = 6.13f;       // cavityRadius
    private const float LegacyGrassDepth = 1.9f;          // grassDepth
    private const float LegacyDirtDepth = 0.65f;          // dirtDepth
    private const bool LegacyUseLegacyMaterialRules = false;
    private const float LegacyWeatheringStrength = 0.8f;
    private const float LegacyWeatheringDepth = 8f;
    private const float LegacyGentleSlopeAngle = 15f;
    private const float LegacySteepSlopeAngle = 45f;
    private const float LegacyGravityStrength = 1f;
    private const int LegacySoilSimulationIterations = 4;
    private const float LegacyGrassStrength = 1f;

    /// <summary>主路径与复刻公式的允许偏差（density 与 y 线性 ⇒ 插值应精确）。</summary>
    private const float CrossCheckTolerance = 0.01f;

    private static int failures;
    private static int checks;

    [MenuItem("Tools/World V2/Validate Skeleton Shadow Comparison")]
    public static void Validate()
    {
        failures = 0;
        checks = 0;

        // ================= 0. 隔离性前置快照 =================
        Scene scene = SceneManager.GetActiveScene();
        bool sceneDirtyBefore = scene.isDirty;
        int terrainInstancesBefore = Resources.FindObjectsOfTypeAll<DensityTerrain>().Length;
        int gameObjectsBefore = Resources.FindObjectsOfTypeAll<GameObject>().Length;

        Check(true, "隔离性：本验证器不创建任何 GameObject / DensityTerrain 实例（只调用纯函数）");

        // ---- World V2 测试配置：项目认可的六参数构造 ⇒ SeaLevel 取登记默认值 ----
        WorldGenerationSettings settings =
            new WorldGenerationSettings(12345, 24, 3, 64, 0.06f, 6f);

        string settingsReason;
        Check(settings.IsValid(out settingsReason),
            "V2 测试配置合法（" + settings + "）");
        Check(settings.SeaLevel == WorldGenerationSettings.DefaultSeaLevel,
            "V2 测试配置未修改 SeaLevel 默认值（SeaLevel=" +
            settings.SeaLevel.ToString(CultureInfo.InvariantCulture) + "）");

        // ================= 1. Legacy 侧：真实生成函数产出列高 =================
        MaterialField legacyMaterial;
        DensityField legacyField = DensityFieldGenerator.Generate(
            DensityGenerationMode.HeightField,
            LegacyResolution,
            Vector3.zero,
            new Vector3(LegacyChunkSizeX, LegacyChunkSizeY, LegacyChunkSizeZ),
            true,
            LegacyHeight,
            LegacyNoiseScale,
            LegacyNoiseStrength,
            new Vector3(LegacyCavityOffsetX, LegacyCavityOffsetY, LegacyCavityOffsetZ),
            LegacyCavityRadius,
            LegacyGrassDepth,
            LegacyDirtDepth,
            LegacyUseLegacyMaterialRules,
            LegacyWeatheringStrength,
            LegacyWeatheringDepth,
            LegacyGentleSlopeAngle,
            LegacySteepSlopeAngle,
            LegacyGravityStrength,
            LegacySoilSimulationIterations,
            LegacyGrassStrength,
            out legacyMaterial);

        Check(legacyField != null && legacyMaterial != null,
            "Legacy 生成函数返回非空 DensityField / MaterialField");
        Check(legacyField != null &&
              legacyField.SampleCountX == LegacyResolution + 1 &&
              legacyField.SampleCountY == LegacyResolution + 1 &&
              legacyField.SampleCountZ == LegacyResolution + 1,
            "Legacy 采样网格 = (Resolution+1)^3 = " +
            (LegacyResolution + 1).ToString(CultureInfo.InvariantCulture) + "^3");
        Check(legacyField != null &&
              Mathf.Abs(legacyField.CellSizeX - 1f) < 0.0001f &&
              Mathf.Abs(legacyField.CellSizeY - (LegacyChunkSizeY / LegacyResolution)) < 0.0001f &&
              Mathf.Abs(legacyField.CellSizeZ - (LegacyChunkSizeZ / LegacyResolution)) < 0.0001f,
            "Legacy CellSize 与 chunkVoxelSize/resolution 一致（X=1 / Y≈0.4933 / Z≈0.6667）");

        // ================= 2. 采样列定义（固定、可重复、覆盖正常区与边界）=================
        // 网格索引：X 取 SampleCountX 的 0/1/…/75；Z 取 0…75（z 步长 0.6667 ⇒ 覆盖浮点→整数舍入误差）
        int[] sampleIX = { 0, 1, 19, 38, 57, 75 };
        int[] sampleIZ = { 0, 1, 13, 26, 38, 75 };
        int sampleCount = sampleIX.Length * sampleIZ.Length;

        float[] legacySurface = new float[sampleCount];
        bool[] legacyFound = new bool[sampleCount];
        bool[] legacyDegenerate = new bool[sampleCount];
        float[] legacyLocalX = new float[sampleCount];
        float[] legacyLocalZ = new float[sampleCount];
        float[] analyticSurface = new float[sampleCount];

        int index = 0;
        for (int a = 0; a < sampleIX.Length; a++)
        {
            for (int b = 0; b < sampleIZ.Length; b++)
            {
                int ix = sampleIX[a];
                int iz = sampleIZ[b];

                Vector3 localPos = legacyField.GetLocalPosition(ix, 0, iz);
                legacyLocalX[index] = localPos.x;
                legacyLocalZ[index] = localPos.z;

                float derived;
                bool degenerate;
                legacyFound[index] = TryDeriveColumnSurface(legacyField, ix, iz, out derived, out degenerate);
                legacySurface[index] = derived;
                legacyDegenerate[index] = degenerate;

                // 对照路径：测试侧复刻生产公式（仅用于交叉校验①的取值）
                float noise = Mathf.PerlinNoise(localPos.x * LegacyNoiseScale, localPos.z * LegacyNoiseScale) * 2f - 1f;
                analyticSurface[index] = LegacyHeight + noise * LegacyNoiseStrength;

                index++;
            }
        }

        int notFound = 0;
        int degenerateCount = 0;
        int crossCheckMismatch = 0;
        float maxCrossCheckDelta = 0f;
        float legacyMin = float.MaxValue;
        float legacyMax = float.MinValue;

        for (int i = 0; i < sampleCount; i++)
        {
            if (!legacyFound[i]) { notFound++; }
            if (legacyDegenerate[i]) { degenerateCount++; }

            float delta = Mathf.Abs(legacySurface[i] - analyticSurface[i]);
            if (delta > maxCrossCheckDelta) { maxCrossCheckDelta = delta; }
            if (delta > CrossCheckTolerance) { crossCheckMismatch++; }

            if (legacySurface[i] < legacyMin) { legacyMin = legacySurface[i]; }
            if (legacySurface[i] > legacyMax) { legacyMax = legacySurface[i]; }
        }

        Check(notFound == 0, "Legacy：全部采样列均取到地表高度（未取到 " +
                             notFound.ToString(CultureInfo.InvariantCulture) + "）");
        Check(degenerateCount == 0, "Legacy：无退化列（未命中密度符号变化 " +
                                    degenerateCount.ToString(CultureInfo.InvariantCulture) +
                                    " 列，退化属独立记录的边界情况）");
        Check(crossCheckMismatch == 0,
            "Legacy：符号变化插值与生产公式复刻一致（最大偏差 " +
            maxCrossCheckDelta.ToString("F6", CultureInfo.InvariantCulture) +
            " ≤ " + CrossCheckTolerance.ToString("F6", CultureInfo.InvariantCulture) + "）");
        Check(legacyMin >= 0f && legacyMax <= LegacyChunkSizeY,
            "Legacy：地表高度落在 Chunk 垂直范围内（[" +
            legacyMin.ToString("F4", CultureInfo.InvariantCulture) + ", " +
            legacyMax.ToString("F4", CultureInfo.InvariantCulture) + "] ⊂ [0, " +
            LegacyChunkSizeY.ToString("F0", CultureInfo.InvariantCulture) + "]）");

        // ================= 3. V2 侧：逐列事实查询 + 映射 =================
        int[] worldX = new int[sampleCount];
        int[] worldZ = new int[sampleCount];
        float[] roundingErrorX = new float[sampleCount];
        float[] roundingErrorZ = new float[sampleCount];
        bool[] v2Ok = new bool[sampleCount];
        bool[] v2Valid = new bool[sampleCount];
        int[] v2Elevation = new int[sampleCount];
        bool[] v2Ocean = new bool[sampleCount];
        int[] v2Continent = new int[sampleCount];
        bool[] v2MatchesHeightFunction = new bool[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            // 仅测试用临时映射：同一整数 X/Z 对应的世界坐标
            worldX[i] = Mathf.RoundToInt(legacyLocalX[i]);
            worldZ[i] = Mathf.RoundToInt(legacyLocalZ[i]);
            roundingErrorX[i] = Mathf.Abs(legacyLocalX[i] - worldX[i]);
            roundingErrorZ[i] = Mathf.Abs(legacyLocalZ[i] - worldZ[i]);

            WorldSkeletonFacts facts;
            v2Ok[i] = WorldSkeletonProvider.TryGetFacts(worldX[i], 0, worldZ[i], settings, out facts);
            v2Valid[i] = facts.IsValid;
            v2Elevation[i] = facts.Elevation;
            v2Ocean[i] = facts.Ocean;
            v2Continent[i] = facts.Continent;

            // 错列/错坐标检测：V2 的 Elevation 必须等于同一 (X,Z) 的独立高度函数结果
            int independent = HeightFunction.GetTerrainHeight(worldX[i], worldZ[i], settings);
            v2MatchesHeightFunction[i] = (facts.Elevation == independent);
        }

        int v2Failures = 0;
        int v2Invalid = 0;
        int misaligned = 0;
        int oceanConsistent = 0;
        int continentConsistent = 0;

        for (int i = 0; i < sampleCount; i++)
        {
            if (!v2Ok[i]) { v2Failures++; }
            if (!v2Valid[i]) { v2Invalid++; }
            if (!v2MatchesHeightFunction[i]) { misaligned++; }

            if (v2Ok[i] && v2Valid[i])
            {
                if (v2Ocean[i] == (v2Elevation[i] < settings.SeaLevel)) { oceanConsistent++; }
                if (v2Continent[i] == (v2Ocean[i] ? 0 : 1)) { continentConsistent++; }
            }
        }

        Check(v2Failures == 0, "V2：全部采样列 TryGetFacts 成功（失败 " +
                               v2Failures.ToString(CultureInfo.InvariantCulture) + "）");
        Check(v2Invalid == 0, "V2：全部成功结果 IsValid == true（无效 " +
                              v2Invalid.ToString(CultureInfo.InvariantCulture) + "）");
        Check(misaligned == 0,
            "对比：逐列一一对应、无错列/错坐标（facts.Elevation == HeightFunction(X,Z)，不一致 " +
            misaligned.ToString(CultureInfo.InvariantCulture) + "）");
        Check(oceanConsistent == sampleCount && continentConsistent == sampleCount,
            "V2：Ocean/Continent 与既有规则自洽（Ocean 一致 " +
            oceanConsistent.ToString(CultureInfo.InvariantCulture) + "/" +
            sampleCount.ToString(CultureInfo.InvariantCulture) + "；Continent 一致 " +
            continentConsistent.ToString(CultureInfo.InvariantCulture) + "/" +
            sampleCount.ToString(CultureInfo.InvariantCulture) + "）");
        Check(MaxOf(roundingErrorX) < 0.5f + 0.0001f && MaxOf(roundingErrorZ) < 0.5f + 0.0001f,
            "映射：浮点局部坐标→整数世界坐标的舍入误差 ≤ 0.5（X 最大 " +
            MaxOf(roundingErrorX).ToString("F4", CultureInfo.InvariantCulture) + "，Z 最大 " +
            MaxOf(roundingErrorZ).ToString("F4", CultureInfo.InvariantCulture) + "）");

        // ================= 4. 差异统计（有符号 / 绝对 / 最大 / 平均绝对）=================
        float[] signedDiff = new float[sampleCount];
        float[] absDiff = new float[sampleCount];
        float maxSigned = float.MinValue;
        float minSigned = float.MaxValue;
        float maxAbs = 0f;
        double absSum = 0d;
        int positive = 0;
        int negative = 0;
        int zero = 0;

        for (int i = 0; i < sampleCount; i++)
        {
            // 仅在"两侧都有效"时计入统计（失败/无效样本不得当作有效对比）
            if (!legacyFound[i] || !v2Ok[i] || !v2Valid[i])
            {
                signedDiff[i] = float.NaN;
                absDiff[i] = float.NaN;
                continue;
            }

            signedDiff[i] = legacySurface[i] - v2Elevation[i];
            absDiff[i] = Mathf.Abs(signedDiff[i]);
            absSum += absDiff[i];

            if (signedDiff[i] > maxSigned) { maxSigned = signedDiff[i]; }
            if (signedDiff[i] < minSigned) { minSigned = signedDiff[i]; }
            if (absDiff[i] > maxAbs) { maxAbs = absDiff[i]; }

            if (signedDiff[i] > 0f) { positive++; }
            else if (signedDiff[i] < 0f) { negative++; }
            else { zero++; }
        }

        int comparedCount = positive + negative + zero;
        float meanAbs = comparedCount > 0 ? (float)(absSum / comparedCount) : 0f;

        Check(comparedCount == sampleCount,
            "统计：全部采样列均计入对比（计入 " +
            comparedCount.ToString(CultureInfo.InvariantCulture) + "/" +
            sampleCount.ToString(CultureInfo.InvariantCulture) + "）");
        Check(!float.IsNaN(meanAbs) && !float.IsInfinity(meanAbs) &&
              !float.IsNaN(maxAbs) && !float.IsInfinity(maxAbs),
            "统计：差值指标有限（meanAbs=" + meanAbs.ToString("F4", CultureInfo.InvariantCulture) +
            " / maxAbs=" + maxAbs.ToString("F4", CultureInfo.InvariantCulture) + "）");
        Check(meanAbs <= maxAbs + 0.0001f,
            "统计：平均绝对差 ≤ 最大绝对差（一致性）");

        // ================= 5. 稳定性（同一输入重复计算）=================
        MaterialField legacyMaterial2;
        DensityField legacyField2 = DensityFieldGenerator.Generate(
            DensityGenerationMode.HeightField,
            LegacyResolution,
            Vector3.zero,
            new Vector3(LegacyChunkSizeX, LegacyChunkSizeY, LegacyChunkSizeZ),
            true,
            LegacyHeight,
            LegacyNoiseScale,
            LegacyNoiseStrength,
            new Vector3(LegacyCavityOffsetX, LegacyCavityOffsetY, LegacyCavityOffsetZ),
            LegacyCavityRadius,
            LegacyGrassDepth,
            LegacyDirtDepth,
            LegacyUseLegacyMaterialRules,
            LegacyWeatheringStrength,
            LegacyWeatheringDepth,
            LegacyGentleSlopeAngle,
            LegacySteepSlopeAngle,
            LegacyGravityStrength,
            LegacySoilSimulationIterations,
            LegacyGrassStrength,
            out legacyMaterial2);

        int legacyUnstable = 0;
        int v2Unstable = 0;
        index = 0;
        for (int a = 0; a < sampleIX.Length; a++)
        {
            for (int b = 0; b < sampleIZ.Length; b++)
            {
                int ix = sampleIX[a];
                int iz = sampleIZ[b];

                float derived2;
                bool degenerate2;
                TryDeriveColumnSurface(legacyField2, ix, iz, out derived2, out degenerate2);
                if (derived2 != legacySurface[index]) { legacyUnstable++; }

                WorldSkeletonFacts facts2;
                WorldSkeletonProvider.TryGetFacts(worldX[index], 0, worldZ[index], settings, out facts2);
                if (facts2.Elevation != v2Elevation[index] || facts2.Ocean != v2Ocean[index]) { v2Unstable++; }

                index++;
            }
        }

        Check(legacyUnstable == 0,
            "稳定性：Legacy 两次独立生成的列高逐值相同（不一致 " +
            legacyUnstable.ToString(CultureInfo.InvariantCulture) + "）");
        Check(v2Unstable == 0,
            "稳定性：V2 重复查询结果逐值相同（不一致 " +
            v2Unstable.ToString(CultureInfo.InvariantCulture) + "）");

        // ================= 6. 两侧边界与失败情况（独立记录，不计入对比）=================
        WorldSkeletonFacts belowZero;
        bool belowOk = WorldSkeletonProvider.TryGetFacts(worldX[0], -1, worldZ[0], settings, out belowZero);
        WorldSkeletonFacts atTop;
        bool topOk = WorldSkeletonProvider.TryGetFacts(worldX[0], settings.WorldHeight, worldZ[0], settings, out atTop);
        WorldGenerationSettings emptySettings = default(WorldGenerationSettings);
        WorldSkeletonFacts emptyFacts;
        bool emptyOk = WorldSkeletonProvider.TryGetFacts(worldX[0], 0, worldZ[0], emptySettings, out emptyFacts);

        Check(!belowOk && !belowZero.IsValid,
            "V2 失败路径①：Y=-1 ⇒ 查询失败且事实无效（已独立记录，不计入对比）");
        Check(!topOk && !atTop.IsValid,
            "V2 失败路径②：Y=WorldHeight(" +
            settings.WorldHeight.ToString(CultureInfo.InvariantCulture) +
            ") ⇒ 查询失败且事实无效（已独立记录，不计入对比）");
        Check(!emptyOk && !emptyFacts.IsValid,
            "V2 失败路径③：default(WorldGenerationSettings)（WorldHeight=0）⇒ 失败且事实无效（独立记录）");
        Check(comparedCount == sampleCount,
            "失败路径样本未被误计入有效对比（有效对比仍为 " +
            comparedCount.ToString(CultureInfo.InvariantCulture) + " 列）");

        // ---- Legacy 侧边界记录（退化列 / 越界列属于独立记录项）----
        Check(degenerateCount == 0 && notFound == 0,
            "Legacy 边界：本次采样无退化/未命中列（若出现会单独记录，不并入有效对比）");

        // ================= 7. 隔离性后置断言 =================
        int terrainInstancesAfter = Resources.FindObjectsOfTypeAll<DensityTerrain>().Length;
        int gameObjectsAfter = Resources.FindObjectsOfTypeAll<GameObject>().Length;
        Scene sceneAfter = SceneManager.GetActiveScene();

        Check(terrainInstancesAfter == terrainInstancesBefore,
            "隔离性：DensityTerrain 实例数未变化（" +
            terrainInstancesBefore.ToString(CultureInfo.InvariantCulture) + " → " +
            terrainInstancesAfter.ToString(CultureInfo.InvariantCulture) + "）");
        Check(gameObjectsAfter == gameObjectsBefore,
            "隔离性：GameObject 数量未变化（" +
            gameObjectsBefore.ToString(CultureInfo.InvariantCulture) + " → " +
            gameObjectsAfter.ToString(CultureInfo.InvariantCulture) +
            "）⇒ 未创建任何测试对象");
        Check(!sceneAfter.isDirty || sceneDirtyBefore,
            "隔离性：未把当前场景标记为脏（before=" + sceneDirtyBefore + " / after=" + sceneAfter.isDirty + "）");

        // 若加载场景中存在 DensityTerrain 实例，则校验其 Transform 是否为恒等（映射前提）
        DensityTerrain[] loadedTerrains = Resources.FindObjectsOfTypeAll<DensityTerrain>();
        if (loadedTerrains.Length > 0)
        {
            Transform root = loadedTerrains[0].transform;
            bool identity =
                root.localPosition == Vector3.zero &&
                root.localRotation == Quaternion.identity &&
                root.localScale == Vector3.one &&
                root.parent == null;
            Check(identity,
                "映射前提：加载场景中的 DensityTerrain 根为恒等变换（Local == World 成立）");
        }
        else
        {
            Check(true,
                "映射前提：batchmode 未加载含 DensityTerrain 的场景 ⇒ 临时映射前提" +
                "（根恒等 ⇒ Local==World）按 R43/R44 场景序列化证据记录，未在本进程内复验");
        }

        // ================= 8. 输出：逐采样点明细 + 差异汇总 =================
        LogSampleTable(sampleCount, legacyLocalX, legacyLocalZ, worldX, worldZ,
            roundingErrorX, roundingErrorZ, legacySurface, v2Elevation, signedDiff, absDiff);

        StringBuilder summary = new StringBuilder();
        summary.Append("[V2Shadow] 差异汇总：n=").Append(comparedCount.ToString(CultureInfo.InvariantCulture))
               .Append(" | Legacy 范围=[").Append(legacyMin.ToString("F4", CultureInfo.InvariantCulture))
               .Append(", ").Append(legacyMax.ToString("F4", CultureInfo.InvariantCulture)).Append("]")
               .Append(" | V2 范围=[")
               .Append(MinOf(v2Elevation).ToString(CultureInfo.InvariantCulture)).Append(", ")
               .Append(MaxOf(v2Elevation).ToString(CultureInfo.InvariantCulture)).Append("]")
               .Append(" | signedDiff min=").Append(minSigned.ToString("F4", CultureInfo.InvariantCulture))
               .Append(" max=").Append(maxSigned.ToString("F4", CultureInfo.InvariantCulture))
               .Append(" | maxAbs=").Append(maxAbs.ToString("F4", CultureInfo.InvariantCulture))
               .Append(" | meanAbs=").Append(meanAbs.ToString("F4", CultureInfo.InvariantCulture))
               .Append(" | 正/负/零=").Append(positive.ToString(CultureInfo.InvariantCulture)).Append("/")
               .Append(negative.ToString(CultureInfo.InvariantCulture)).Append("/")
               .Append(zero.ToString(CultureInfo.InvariantCulture));
        Debug.Log(summary.ToString());

        // ================= 9. 判定语义自检：高度不等 ≠ 生产故障 =================
        // 本轮**不**统一两套高度函数，因此"两套高度不相等"是预期事实，不是失败条件。
        // 这里用一条显式断言证明该语义：实测存在差异（maxAbs > 0）且 failures 仍为 0。
        Check(maxAbs > 0f && failures == 0,
            "判定语义：已实测到两套高度差异（maxAbs=" +
            maxAbs.ToString("F4", CultureInfo.InvariantCulture) +
            "）但未产生任何失败 ⇒ 高度不等不被当作生产故障");

        // ================= 10. 退出码映射自检 =================
        Check(ExitCodeFor(0) == 0, "退出码：失败计数 0 ⇒ 0");
        Check(ExitCodeFor(1) == 1, "退出码：失败计数 1 ⇒ 1");
        Check(ExitCodeFor(4) == 1, "退出码：失败计数 4 ⇒ 1");

        if (IsSelfFailureRequested())
        {
            // 仅在命令行显式给出 -wsShadowSelfFailure 时触发；不写入任何生产数据
            Check(false, "[自检] 命令行开关注入的故意失败（用于验证 Batchmode 退出码传播）");
        }

        Report();
    }

    /// <summary>
    /// **批处理入口**：退出码由断言结果决定；异常一律 exit 1。
    /// </summary>
    public static void RunBatch()
    {
        try
        {
            Validate();
        }
        catch (Exception exception)
        {
            Debug.LogError("[V2Shadow] 批处理失败（exit 1）：" + exception.Message);
            EditorApplication.Exit(1);
            return;
        }

        EditorApplication.Exit(ExitCodeFor(failures));
    }

    // -----------------------------------------------------------------
    // 纯辅助（无世界规则：只做取值、插值与统计）
    // -----------------------------------------------------------------

    /// <summary>
    /// 从 DensityField 派生某列地表高度：与生产 `FindSurfaceHeight` 同语义
    /// （自下而上找 density 由 &gt;0 变 ≤0 的第一段并线性插值；无交点时按底部/顶部退化）。
    /// </summary>
    private static bool TryDeriveColumnSurface(
        DensityField field, int x, int z, out float surface, out bool degenerate)
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

        // 无符号变化：底部实体 ⇒ 视为地形顶部；否则视为底部（与生产退化分支一致）
        if (field[x, 0, z] > 0f)
        {
            surface = field.Origin.y + field.Size.y;
        }
        else
        {
            surface = field.Origin.y;
        }

        return true;
    }

    private static void LogSampleTable(
        int sampleCount,
        float[] localX, float[] localZ,
        int[] worldX, int[] worldZ,
        float[] errorX, float[] errorZ,
        float[] legacySurface, int[] v2Elevation,
        float[] signedDiff, float[] absDiff)
    {
        Debug.Log("[V2Shadow] 逐采样点明细（i | LegacyLocalX,Z | WorldX,Z | 舍入误差X,Z | LegacySurface | V2Elevation | signedDiff | absDiff）");
        for (int i = 0; i < sampleCount; i++)
        {
            StringBuilder line = new StringBuilder();
            line.Append("[V2Shadow] ")
                .Append(i.ToString("D2", CultureInfo.InvariantCulture)).Append(" | ")
                .Append(localX[i].ToString("F4", CultureInfo.InvariantCulture)).Append(", ")
                .Append(localZ[i].ToString("F4", CultureInfo.InvariantCulture)).Append(" | ")
                .Append(worldX[i].ToString(CultureInfo.InvariantCulture)).Append(", ")
                .Append(worldZ[i].ToString(CultureInfo.InvariantCulture)).Append(" | ")
                .Append(errorX[i].ToString("F4", CultureInfo.InvariantCulture)).Append(", ")
                .Append(errorZ[i].ToString("F4", CultureInfo.InvariantCulture)).Append(" | ")
                .Append(legacySurface[i].ToString("F4", CultureInfo.InvariantCulture)).Append(" | ")
                .Append(v2Elevation[i].ToString(CultureInfo.InvariantCulture)).Append(" | ")
                .Append(signedDiff[i].ToString("F4", CultureInfo.InvariantCulture)).Append(" | ")
                .Append(absDiff[i].ToString("F4", CultureInfo.InvariantCulture));
            Debug.Log(line.ToString());
        }
    }

    private static float MaxOf(float[] values)
    {
        float max = 0f;
        for (int i = 0; i < values.Length; i++) { if (values[i] > max) { max = values[i]; } }
        return max;
    }

    private static int MaxOf(int[] values)
    {
        int max = int.MinValue;
        for (int i = 0; i < values.Length; i++) { if (values[i] > max) { max = values[i]; } }
        return max;
    }

    private static int MinOf(int[] values)
    {
        int min = int.MaxValue;
        for (int i = 0; i < values.Length; i++) { if (values[i] < min) { min = values[i]; } }
        return min;
    }

    /// <summary>退出码映射（唯一权威）：0 个失败 ⇒ 0；任何非零失败 ⇒ 1。</summary>
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
            Debug.Log("[V2Shadow] PASS: " + label);
        }
        else
        {
            failures++;
            Debug.LogError("[V2Shadow] FAIL: " + label);
        }
    }

    private static void Report()
    {
        StringBuilder summary = new StringBuilder();
        summary.Append("[V2Shadow] Legacy × World V2 影子高度对比 ⇒ checks=")
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