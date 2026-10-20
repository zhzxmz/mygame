using System;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEngine;
using World.V2;
using VoxelWorld.Generation;

/// <summary>
/// **World V2 参数两段式发布验证**（Editor 验证脚本 · 纯只读、无副作用）。
///
/// 验证对象：`WorldParameterEvaluator`（Base 段 / Final 段）、`WorldParameterState.IsFinalized`、
/// `WorldStatePipeline`（Base → Region → Finalize 编排）。
///
/// 契约依据：
///   · `PC19` 两段式发布（Base 先行、Final 在 Region 之后定稿）；
///   · `PC20` Region 是修正量唯一写入者，WorldState 只做合成；
///   · `WSC10` `Elevation`/`Ocean` 不得进入任何修正求和式；
///   · `WSC14`/`WSC22` 零修正显式性 ⇒ Region 层关闭时 `Final == Base`（逐分量、逐值相等）；
///   · `WSC9`/`WSC12` 加法 + 钳制；`D5`/`PM52` 确定性。
///
/// 菜单：`Tools/World V2/Validate Parameter Two-Phase Publishing`
/// 批处理（统一入口）：`Unity -batchmode -quit -executeMethod WorldV2ParameterPipelineValidation.RunBatch`
/// 批处理（历史入口，保留兼容）：`Unity -batchmode -quit -executeMethod WorldV2ParameterPipelineValidation.ValidateOrThrow`
///
/// 本脚本**不修改任何运行时数据**、不触碰场景与资产、不写入文件。
/// </summary>
public static class WorldV2ParameterPipelineValidation
{
    private static int failures;

    [MenuItem("Tools/World V2/Validate Parameter Two-Phase Publishing")]
    public static void Validate()
    {
        failures = 0;

        WorldSeed seed = new WorldSeed(12345);
        WorldCoordinate position = new WorldCoordinate(16, 32, -48);
        // 世界生成配置由调用方显式提供（不使用 Default 示例值）
        WorldGenerationSettings settings = new WorldGenerationSettings(12345, 24, 3, 64, 0.06f, 6f);

        // 1. Base 段：未定稿
        WorldParameterState baseState = WorldParameterEvaluator.Evaluate(seed, settings, position);
        Check(baseState.IsFinalized == false, "Evaluate 只发布 Base 段（IsFinalized == false，PC19）");

        // 2. Base 段：Modifier 恒 0，Final 暂等于 Base（不具权威性）
        bool modifierZero = true;
        bool finalPlaceholder = true;
        for (int i = 0; i < baseState.Count; i++)
        {
            WorldParameterValue value = baseState[i];
            if (value.Modifier != 0f) { modifierZero = false; }
            if (value.FinalValue != value.BaseValue) { finalPlaceholder = false; }
        }
        Check(modifierZero, "Base 段所有 Modifier == 0（WSC14 零修正显式）");
        Check(finalPlaceholder, "Base 段 FinalValue 暂等于 BaseValue（未定稿中间值）");

        // 3. 零修正定稿 ⇒ Final == Base（WSC22：关闭 Region 层等价）
        RegionModifier zeroModifier = new RegionModifier(0f, 0f, 0f, 0f, 0f, 0f);
        WorldParameterState finalizedZero = WorldParameterEvaluator.Finalize(baseState, zeroModifier);
        Check(finalizedZero.IsFinalized == true, "Finalize(零修正) ⇒ IsFinalized == true");

        bool equalsBase = true;
        for (int i = 0; i < finalizedZero.Count; i++)
        {
            WorldParameterValue value = finalizedZero[i];
            if (value.FinalValue != value.BaseValue || value.Modifier != 0f) { equalsBase = false; }
        }
        Check(equalsBase, "WSC22：Region 层关闭（零修正）⇒ Final == Base（逐分量、逐值相等）");

        // 4. 非零修正定稿 ⇒ 6 个加法参数生效；Elevation/Ocean 结构上不受影响（WSC10）
        RegionModifier nonZero = new RegionModifier(0.10f, 0.20f, 0.30f, 0.40f, 0.50f, 0.60f);
        WorldParameterState finalized = WorldParameterEvaluator.Finalize(baseState, nonZero);

        float[] expectedModifier = new float[WorldParameterIds.Count + 1];
        expectedModifier[WorldParameterIds.Temperature] = 0.10f;
        expectedModifier[WorldParameterIds.Moisture] = 0.20f;
        expectedModifier[WorldParameterIds.Fertility] = 0.30f;
        expectedModifier[WorldParameterIds.MagicDensity] = 0.40f;
        expectedModifier[WorldParameterIds.HolyInfluence] = 0.50f;
        expectedModifier[WorldParameterIds.CorruptionInfluence] = 0.60f;
        expectedModifier[WorldParameterIds.Elevation] = 0f;
        expectedModifier[WorldParameterIds.Ocean] = 0f;

        bool mappingOk = true;
        bool composeOk = true;
        for (int i = 0; i < finalized.Count; i++)
        {
            WorldParameterValue baseValue = baseState[i];
            WorldParameterValue finalValue = finalized[i];
            int parameterId = finalValue.ParameterId;

            if (finalValue.Modifier != expectedModifier[parameterId])
            {
                mappingOk = false;
            }

            float expectedFinal = (parameterId == WorldParameterIds.Elevation || parameterId == WorldParameterIds.Ocean)
                ? baseValue.BaseValue
                : Clamp01(baseValue.BaseValue + expectedModifier[parameterId]);

            if (finalValue.FinalValue != expectedFinal)
            {
                composeOk = false;
            }
        }
        Check(mappingOk, "参数身份 → RegionModifier 分量映射正确（6 加法 / Elevation·Ocean 恒 0）");
        Check(composeOk, "Final = clamp01(Base + Modifier)（WSC9/WSC12）；Elevation·Ocean 的 Final == Base（WSC10）");

        // 5. 单次定稿：二次 Finalize 必须被拒绝
        bool doubleFinalizeRejected = false;
        try
        {
            WorldParameterEvaluator.Finalize(finalized, zeroModifier);
        }
        catch (InvalidOperationException)
        {
            doubleFinalizeRejected = true;
        }
        Check(doubleFinalizeRejected, "对已定稿快照二次 Finalize ⇒ InvalidOperationException（PC19 单次定稿）");

        // 6. WorldStatePipeline：两条入口的定稿状态正确
        WorldParameterState pipelineBase = WorldStatePipeline.EvaluateBase(seed, settings, position);
        WorldParameterState pipelineFinal = WorldStatePipeline.EvaluateFinal(seed, settings, position);
        Check(pipelineBase.IsFinalized == false, "WorldStatePipeline.EvaluateBase ⇒ 未定稿（WSC21：不触发 Region）");
        Check(pipelineFinal.IsFinalized == true, "WorldStatePipeline.EvaluateFinal ⇒ 已定稿（Region 之后，PC19）");

        // 7. 确定性（D5 / PM52）
        WorldParameterState pipelineFinalAgain = WorldStatePipeline.EvaluateFinal(seed, settings, position);
        bool deterministic = true;
        for (int i = 0; i < pipelineFinal.Count; i++)
        {
            if (pipelineFinal[i] != pipelineFinalAgain[i]) { deterministic = false; }
        }
        Check(deterministic, "Final 段确定性：同 Seed + 同坐标 ⇒ 逐字段相同（D5/PM52）");

        // 8. Base 段与 pipeline 的 Base 一致（同源，不产生第二套求值路径 WSC103/DR35）
        bool sameBase = true;
        for (int i = 0; i < pipelineBase.Count; i++)
        {
            if (pipelineBase[i].BaseValue != baseState[i].BaseValue) { sameBase = false; }
        }
        Check(sameBase, "Pipeline.EvaluateBase 与 Evaluator.Evaluate 的 Base 段逐值一致（无第二套求值路径）");

        // ================= SeaLevel 迁移验证（第九轮追加） =================

        // A. 配置范围（[0, WorldHeight] 闭区间；WorldHeight 前置合法性由既有 settings 保证）
        WorldGenerationSettings slUnset = new WorldGenerationSettings(12345, 24, 3, 64, 0.06f, 6f);
        WorldGenerationSettings slZero = new WorldGenerationSettings(12345, 24, 3, 64, 0.06f, 6f, 0);
        WorldGenerationSettings slTop = new WorldGenerationSettings(12345, 24, 3, 64, 0.06f, 6f, 64);
        WorldGenerationSettings slNeg = new WorldGenerationSettings(12345, 24, 3, 64, 0.06f, 6f, -1);
        WorldGenerationSettings slOver = new WorldGenerationSettings(12345, 24, 3, 64, 0.06f, 6f, 65);
        string slReason;
        Check(slUnset.IsValid(out slReason) && slUnset.WorldHeight == 64, "前置：WorldHeight = 64 且 settings 合法（" + slReason + "）");
        Check(slZero.IsValid(out slReason), "A：SeaLevel = 0 合法（" + slReason + "）");
        Check(slTop.IsValid(out slReason), "A：SeaLevel = WorldHeight 合法（闭区间上界；" + slReason + "）");
        Check(!slNeg.IsValid(out slReason), "A：SeaLevel = -1 非法");
        Check(!slOver.IsValid(out slReason), "A：SeaLevel = WorldHeight+1 非法");

        // B1. 严格边界：Elevation = SeaLevel-1 / SeaLevel / SeaLevel+1（仅取 Elevation ∈ (0, WorldHeight-1) 的内部样本）
        int midX = -1, midZ = -1, midElevation = -1;
        for (int x = 0; x < 128 && midElevation < 0; x++)
        {
            for (int z = 0; z < 128; z++)
            {
                int h = HeightFunction.GetTerrainHeight(x, z, slUnset);
                if (h > 0 && h < slUnset.WorldHeight - 1) { midX = x; midZ = z; midElevation = h; break; }
            }
        }
        Check(midElevation > 0 && midElevation < slUnset.WorldHeight - 1, "B1：取得内部 Elevation 样本（" + midElevation + " @ " + midX + "," + midZ + "）");
        if (midElevation > 0 && midElevation < slUnset.WorldHeight - 1)
        {
            WorldParameterValue oceanValue;
            WorldGenerationSettings slAtMinus1 = new WorldGenerationSettings(12345, 24, 3, 64, 0.06f, 6f, midElevation + 1);
            WorldGenerationSettings slAtEqual = new WorldGenerationSettings(12345, 24, 3, 64, 0.06f, 6f, midElevation);
            WorldGenerationSettings slAtPlus1 = new WorldGenerationSettings(12345, 24, 3, 64, 0.06f, 6f, midElevation - 1);
            WorldCoordinate probe = new WorldCoordinate(midX, 0, midZ);

            WorldParameterState stM = WorldParameterEvaluator.Evaluate(seed, slAtMinus1, probe);
            stM.TryGetValue(WorldParameterIds.Ocean, out oceanValue);
            Check(oceanValue.BaseValue == 1f, "B1：Elevation = SeaLevel-1 ⇒ Ocean = true");

            WorldParameterState stE = WorldParameterEvaluator.Evaluate(seed, slAtEqual, probe);
            stE.TryGetValue(WorldParameterIds.Ocean, out oceanValue);
            Check(oceanValue.BaseValue == 0f, "B1：Elevation = SeaLevel ⇒ Ocean = false（严格小于）");

            WorldParameterState stP = WorldParameterEvaluator.Evaluate(seed, slAtPlus1, probe);
            stP.TryGetValue(WorldParameterIds.Ocean, out oceanValue);
            Check(oceanValue.BaseValue == 0f, "B1：Elevation = SeaLevel+1 ⇒ Ocean = false");
        }

        // B2/B3. SeaLevel = 0 ⇒ 全部非海洋；SeaLevel = WorldHeight ⇒ 全部海洋
        bool noneOcean = true;
        bool allOcean = true;
        int columns = 0;
        for (int x = 0; x < 32; x += 7)
        {
            for (int z = 0; z < 32; z += 5)
            {
                WorldParameterValue ov;
                WorldCoordinate p = new WorldCoordinate(x, 0, z);
                WorldParameterState s0 = WorldParameterEvaluator.Evaluate(seed, slZero, p);
                s0.TryGetValue(WorldParameterIds.Ocean, out ov);
                if (ov.BaseValue != 0f) { noneOcean = false; }
                WorldParameterState sT = WorldParameterEvaluator.Evaluate(seed, slTop, p);
                sT.TryGetValue(WorldParameterIds.Ocean, out ov);
                if (ov.BaseValue != 1f) { allOcean = false; }
                columns++;
            }
        }
        Check(noneOcean, "B2：SeaLevel = 0 ⇒ 所有合法列均非海洋（列数 " + columns + "）");
        Check(allOcean, "B3：SeaLevel = WorldHeight ⇒ 所有合法列均为海洋（列数 " + columns + "）");

        // C. 构造兼容性 / 确定性 / HeightFunction 独立性
        Check(slUnset.SeaLevel == WorldGenerationSettings.DefaultSeaLevel && slUnset.SeaLevel == 64, "C：旧六参数构造 ⇒ SeaLevel = 64（兼容默认）");
        WorldGenerationSettings slExplicit = new WorldGenerationSettings(12345, 24, 3, 64, 0.06f, 6f, 17);
        Check(slExplicit.SeaLevel == 17, "C：七参数构造保留显式 SeaLevel = 17");

        int legacyOceanMismatch = 0;
        int heightMismatch = 0;
        for (int x = -16; x <= 16; x += 5)
        {
            for (int z = -16; z <= 16; z += 7)
            {
                WorldParameterValue ov;
                WorldCoordinate p = new WorldCoordinate(x, 0, z);
                WorldParameterState s = WorldParameterEvaluator.Evaluate(seed, slUnset, p);
                s.TryGetValue(WorldParameterIds.Ocean, out ov);
                int h = HeightFunction.GetTerrainHeight(x, z, slUnset);
                if (ov.BaseValue != (h < 64 ? 1f : 0f)) { legacyOceanMismatch++; }
                if (HeightFunction.GetTerrainHeight(x, z, slZero) != h) { heightMismatch++; }
            }
        }
        Check(legacyOceanMismatch == 0, "C：旧配置 Ocean 与迁移前 Elevation<64 逐点一致（mismatch=" + legacyOceanMismatch + "）");
        Check(heightMismatch == 0, "C：改变 SeaLevel 不改变 HeightFunction 高度（mismatch=" + heightMismatch + "）");

        WorldParameterState detA = WorldParameterEvaluator.Evaluate(seed, slExplicit, new WorldCoordinate(3, 0, 4));
        WorldParameterState detB = WorldParameterEvaluator.Evaluate(seed, slExplicit, new WorldCoordinate(3, 0, 4));
        bool slDeterministic = true;
        for (int i = 0; i < detA.Count; i++) { if (detA[i] != detB[i]) { slDeterministic = false; } }
        Check(slDeterministic, "C：同配置 + 同 Seed + 同坐标 ⇒ 逐字段确定（D5/PM52）");

        // D. Bootstrap 生产路径（**静态源码证据**，非运行时验证）
        string bootstrapPath = "Assets/Scripts/Game/VoxelWorldStreamingBootstrap.cs";
        bool bootstrapWired = false;
        if (System.IO.File.Exists(bootstrapPath))
        {
            string bootstrapSource = System.IO.File.ReadAllText(bootstrapPath);
            bootstrapWired = bootstrapSource.Contains("public int SeaLevel = 64;")
                          && bootstrapSource.Contains("TerrainFrequency, TerrainAmplitude, SeaLevel);");
        }
        Check(bootstrapWired, "D：[静态] Bootstrap 暴露 SeaLevel 字段并以七参数构造传入（非运行时验证）");

        // ================= E. WorldSkeletonFacts 查询级语义（R40：SeaLevel 唯一来源 = settings.SeaLevel） =================
        // 说明：TryEvaluate 当前无生产调用者。本节验证其自身契约（严格小于 / 海洋=0 陆地=1 / 垂直越界明确失败 /
        // Elevation 只读转发且与 SeaLevel 无关），**不构成**生产集成或真实世界 Ocean 分布证据。
        int skX = -1, skZ = -1, skElevation = -1;
        for (int x = 0; x < 128 && skElevation < 0; x++)
        {
            for (int z = 0; z < 128; z++)
            {
                int h = HeightFunction.GetTerrainHeight(x, z, slUnset);
                if (h > 0 && h < slUnset.WorldHeight - 1) { skX = x; skZ = z; skElevation = h; break; }
            }
        }
        Check(skElevation > 0 && skElevation < slUnset.WorldHeight - 1,
            "E0：取得 Skeleton 查询用的内部 Elevation 样本（" + skElevation + " @ " + skX + "," + skZ + "）");

        if (skElevation > 0 && skElevation < slUnset.WorldHeight - 1)
        {
            WorldCoordinate skPoint = new WorldCoordinate(skX, 0, skZ);
            int nonDefaultSeaLevel = 32;

            // R40：SeaLevel 唯一来源 = settings.SeaLevel。以下三个配置仅 SeaLevel 不同（其余字段全同），用于边界对照。
            WorldGenerationSettings skAtEqual = new WorldGenerationSettings(12345, 24, 3, 64, 0.06f, 6f, skElevation);
            WorldGenerationSettings skBelow = new WorldGenerationSettings(12345, 24, 3, 64, 0.06f, 6f, skElevation - 1);
            WorldGenerationSettings skAt32 = new WorldGenerationSettings(12345, 24, 3, 64, 0.06f, 6f, nonDefaultSeaLevel);

            // E1–E4：海洋样本（settings.SeaLevel = 64 > Elevation ⇒ Ocean）
            WorldSkeletonFacts oceanFacts;
            bool oceanOk = WorldSkeletonFacts.TryEvaluate(skPoint, slUnset, out oceanFacts);
            Check(oceanOk && oceanFacts.IsValid,
                "E1：海洋样本 TryEvaluate 返回 true 且事实有效（ok=" + oceanOk + " valid=" + oceanFacts.IsValid + "）");
            if (oceanOk && oceanFacts.IsValid)
            {
                Check(oceanFacts.Ocean,
                    "E2：Elevation(" + skElevation + ") < settings.SeaLevel(" + slUnset.SeaLevel + ") ⇒ facts.Ocean = true");
                Check(oceanFacts.Continent == 0,
                    "E3：海洋样本 Continent = 0（实际 " + oceanFacts.Continent + "）");
                Check(oceanFacts.Elevation == skElevation,
                    "E4：facts.Elevation 等于该列 HeightFunction 结果（" + oceanFacts.Elevation + " vs " + skElevation + "）");
            }

            // E5–E7：相等边界（settings.SeaLevel = Elevation ⇒ 严格小于不成立 ⇒ 陆地）
            WorldSkeletonFacts equalFacts;
            bool equalOk = WorldSkeletonFacts.TryEvaluate(skPoint, skAtEqual, out equalFacts);
            Check(equalOk && equalFacts.IsValid,
                "E5：相等边界样本 TryEvaluate 返回 true 且事实有效（ok=" + equalOk + " valid=" + equalFacts.IsValid + "）");
            if (equalOk && equalFacts.IsValid)
            {
                Check(!equalFacts.Ocean,
                    "E6：Elevation == settings.SeaLevel(" + skAtEqual.SeaLevel + ") ⇒ facts.Ocean = false（严格小于，不带等号）");
                Check(equalFacts.Continent == 1,
                    "E7：陆地样本 Continent = 1（实际 " + equalFacts.Continent + "）");
            }

            // E8–E9：settings.SeaLevel 低于 Elevation ⇒ 陆地
            WorldSkeletonFacts lowFacts;
            bool lowOk = WorldSkeletonFacts.TryEvaluate(skPoint, skBelow, out lowFacts);
            Check(lowOk && lowFacts.IsValid,
                "E8：SeaLevel 低于 Elevation 的样本 TryEvaluate 返回 true 且事实有效（ok=" + lowOk + " valid=" + lowFacts.IsValid + "）");
            if (lowOk && lowFacts.IsValid)
            {
                Check(!lowFacts.Ocean,
                    "E9：Elevation(" + skElevation + ") > settings.SeaLevel(" + skBelow.SeaLevel + ") ⇒ facts.Ocean = false");
            }

            // E10–E11：垂直越界 ⇒ 明确失败（false + 无效事实），不钳制、不修正
            WorldSkeletonFacts belowFacts;
            bool belowOk = WorldSkeletonFacts.TryEvaluate(new WorldCoordinate(skX, -1, skZ), slUnset, out belowFacts);
            Check(!belowOk && !belowFacts.IsValid,
                "E10：Y = -1 越界 ⇒ 返回 false 且事实无效（ok=" + belowOk + " valid=" + belowFacts.IsValid + "）");

            WorldSkeletonFacts aboveFacts;
            bool aboveOk = WorldSkeletonFacts.TryEvaluate(
                new WorldCoordinate(skX, slUnset.WorldHeight, skZ), slUnset, out aboveFacts);
            Check(!aboveOk && !aboveFacts.IsValid,
                "E11：Y = WorldHeight 越界 ⇒ 返回 false 且事实无效（ok=" + aboveOk + " valid=" + aboveFacts.IsValid + "）");

            // E12：default 结构体不是有效事实
            WorldSkeletonFacts defaultFacts = default(WorldSkeletonFacts);
            Check(!defaultFacts.IsValid, "E12：default(WorldSkeletonFacts).IsValid = false");

            // E13：同一 Elevation、不同合法 settings.SeaLevel ⇒ Ocean 判定随之改变（证明 Ocean 由统一配置控制）
            WorldSkeletonFacts sl0Facts;
            bool sl0Ok = WorldSkeletonFacts.TryEvaluate(skPoint, slZero, out sl0Facts);
            Check(oceanOk && oceanFacts.Ocean && sl0Ok && !sl0Facts.Ocean,
                "E13：同一 Elevation 下 SeaLevel=0 ⇒ Ocean=false，SeaLevel=" + slUnset.SeaLevel +
                " ⇒ Ocean=true（Ocean 由 settings.SeaLevel 控制）");

            // E14：仅改 settings.SeaLevel ⇒ facts.Elevation 不变（Elevation 不依赖 SeaLevel）
            Check(sl0Ok && sl0Facts.IsValid && oceanOk && sl0Facts.Elevation == oceanFacts.Elevation,
                "E14：改变 settings.SeaLevel 不改变 facts.Elevation（sl0=" + sl0Facts.Elevation +
                " / sl64=" + oceanFacts.Elevation + "）");

            // E15：非默认 settings.SeaLevel（32）逐列一致 —— 逐点核对 Ocean 与 (h < 32)、Elevation 与 h
            int skMismatch = 0;
            int skColumns = 0;
            for (int x = 0; x < 32; x += 7)
            {
                for (int z = 0; z < 32; z += 5)
                {
                    WorldSkeletonFacts f;
                    if (!WorldSkeletonFacts.TryEvaluate(new WorldCoordinate(x, 0, z), skAt32, out f))
                    {
                        skMismatch++;
                        continue;
                    }

                    int h = HeightFunction.GetTerrainHeight(x, z, skAt32);
                    if (f.Ocean != (h < skAt32.SeaLevel)) { skMismatch++; }
                    if (f.Elevation != h) { skMismatch++; }
                    skColumns++;
                }
            }
            Check(skMismatch == 0,
                "E15：settings.SeaLevel = " + skAt32.SeaLevel + "（非默认）逐列 Ocean/Elevation 与 HeightFunction 一致（列数 " +
                skColumns + "，mismatch=" + skMismatch + "）");
        }
        Report("World V2 参数两段式发布");
    }

    /// <summary>批处理入口：失败时抛异常（使 `-executeMethod` 以非零语义失败）。</summary>
    public static void ValidateOrThrow()
    {
        Validate();

        if (failures > 0)
        {
            throw new InvalidOperationException(
                "World V2 参数两段式发布验证失败：" +
                failures.ToString(CultureInfo.InvariantCulture) + " 项不通过。");
        }
    }

    /// <summary>
    /// **统一批处理入口**（与其余验证器形式一致）：以进程退出码表示成败。
    ///
    /// 路径语义：
    ///   · 复用既有唯一验证主体 —— 调用 <see cref="ValidateOrThrow"/>（它内部执行 <see cref="Validate"/>）；
    ///   · 成功 ⇒ `EditorApplication.Exit(0)`；
    ///   · 失败 ⇒ `ValidateOrThrow` 抛异常 ⇒ 记录**一行**汇总后 `Exit(1)`。
    ///
    /// 为什么**不**直接 `Validate()` 后无条件 `Exit(0)`：`Validate()` 失败时只调用 `Debug.LogError`
    /// 并累加 `failures`，**不抛异常**，直接 `Exit(0)` 会把失败吞成成功。
    ///
    /// 异常覆盖：`catch (Exception)` 位于 `Exit(0)` 之前 ⇒ **任何**验证期异常（含 `InvalidOperationException`
    /// 与意外运行时异常）都不可能落入退出码 0。逐条失败项仍由 `Validate()` 内的
    /// `Check`/`Report` 输出（`Debug.LogError` 每条 + 汇总），本入口只补一行含失败计数的摘要，不重复刷日志。
    /// </summary>
    public static void RunBatch()
    {
        try
        {
            ValidateOrThrow();
        }
        catch (Exception exception)
        {
            Debug.LogError("[WorldV2-TwoPhase] 批处理失败（exit 1）：" + exception.Message);
            EditorApplication.Exit(1);
            return;
        }

        EditorApplication.Exit(0);
    }

    private static float Clamp01(float value)
    {
        if (value < 0f) { return 0f; }
        if (value > 1f) { return 1f; }
        return value;
    }

    private static void Check(bool condition, string label)
    {
        if (condition)
        {
            Debug.Log("[WorldV2-TwoPhase] PASS: " + label);
        }
        else
        {
            failures++;
            Debug.LogError("[WorldV2-TwoPhase] FAIL: " + label);
        }
    }

    private static void Report(string title)
    {
        StringBuilder summary = new StringBuilder();
        summary.Append("[WorldV2-TwoPhase] ").Append(title).Append(" ⇒ failures=")
               .Append(failures.ToString(CultureInfo.InvariantCulture));

        if (failures == 0)
        {
            Debug.Log(summary.ToString() + "（全部通过）");
        }
        else
        {
            Debug.LogError(summary.ToString() + "（存在失败项）");
        }
    }
}
