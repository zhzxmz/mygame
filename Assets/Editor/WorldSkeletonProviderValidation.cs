using System;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEngine;
using World.V2;
using VoxelWorld.Generation;

/// <summary>
/// **WorldSkeleton 事实适配层验证**（Editor 验证脚本 · 纯只读、无副作用）。
///
/// 验证对象：`WorldSkeletonProvider`（R42 新增适配层）与其转发目标
/// `WorldSkeletonFacts.TryEvaluate`（R40 统一海平面来源）。
///
/// 覆盖（对应 R42 任务的 10 项要求）：
///   1. 合法坐标成功获取事实；
///   2. 成功结果的 `IsValid` 与既有契约一致（成功 ⇒ true；`default` ⇒ false）；
///   3. `Elevation` 与同坐标同设置的 `HeightFunction.GetTerrainHeight` 一致；
///   4. `Ocean` 与 `settings.SeaLevel` 的**严格小于**规则一致（含相等边界）；
///   5. `Continent` 与既有 `Ocean ? 0 : 1` 映射一致；
///   6. `Y = -1` 与 `Y = settings.WorldHeight` 失败且事实无效；
///   7. 改变合法 `SeaLevel` 改变 `Ocean` 但不改变 `Elevation`；
///   8. 不同坐标不会因错误缓存而返回同一份事实（含"断言非平凡"前提）；
///   9. 任何失败路径都不返回可被误认为有效的事实；
///  10. 断言失败能够反映为**非零** Batchmode 退出码（映射自检 + 命令行注入自检）。
///
/// 重要纪律：
///   · **不硬编码**任何具体 `Elevation` 数值：采样列由 `HeightFunction` 在运行时发现；
///   · **不修改** 默认值 / 合法范围 / 边界规则（不改 `WorldHeight`/`SeaLevel` 的语义，
///     也不为迎合测试而调整配置；`WorldHeight = 64`、`SeaLevel = 64` 属合法配置）；
///   · 所有测试配置都用项目认可的构造方式（`WorldGenerationSettings` 六/七参数构造）并断言其合法；
///   · 不触碰场景 / 资产 / 生产状态，不写文件；不读取 Player / Scene / Time / 随机。
///
/// 菜单：`Tools/World V2/Validate WorldSkeleton Provider`
/// 批处理：`Unity -batchmode -quit -executeMethod WorldSkeletonProviderValidation.RunBatch`
/// 退出码自检：追加 `-wsProviderSelfFailure` ⇒ 注入一条**故意失败**，期望退出码为 `1`
///            （正常运行不带该开关，永远不会触发）。
/// </summary>
public static class WorldSkeletonProviderValidation
{
    private const string SelfFailureSwitch = "-wsProviderSelfFailure";
    private const int MaxSampleColumns = 8;

    private static int failures;
    private static int checks;

    [MenuItem("Tools/World V2/Validate WorldSkeleton Provider")]
    public static void Validate()
    {
        failures = 0;
        checks = 0;

        // 世界生成配置：显式六参数构造 ⇒ SeaLevel 取登记默认值 DefaultSeaLevel（= 64）。
        // 不使用 Default 示例值，也不修改任何默认语义。
        WorldGenerationSettings settings =
            new WorldGenerationSettings(12345, 24, 3, 64, 0.06f, 6f);

        Check(settings.SeaLevel == WorldGenerationSettings.DefaultSeaLevel,
            "前置：六参数构造 ⇒ SeaLevel == DefaultSeaLevel（" +
            settings.SeaLevel.ToString(CultureInfo.InvariantCulture) + " / " +
            WorldGenerationSettings.DefaultSeaLevel.ToString(CultureInfo.InvariantCulture) + "）");

        string reason;
        Check(settings.IsValid(out reason), "前置：基准配置合法（" + DescribeValidity(settings) + "）");

        // ---- 运行时发现采样列（不硬编码任何 Elevation 数值）----
        int[] sampleX = new int[MaxSampleColumns];
        int[] sampleZ = new int[MaxSampleColumns];
        int[] sampleElevation = new int[MaxSampleColumns];
        int sampleCount = CollectSampleColumns(settings, sampleX, sampleZ, sampleElevation);

        Check(sampleCount >= 2,
            "前置：发现至少 2 个采样列（实际 " + sampleCount.ToString(CultureInfo.InvariantCulture) + "）");

        int distinctElevations = CountDistinct(sampleElevation, sampleCount);
        Check(distinctElevations >= 2,
            "前置（断言非平凡）：采样列覆盖至少 2 个不同 Elevation（实际 " +
            distinctElevations.ToString(CultureInfo.InvariantCulture) + "）");

        // ================= 1. 合法坐标成功获取事实 =================
        int px = sampleX[0];
        int pz = sampleZ[0];
        int columnHeight = sampleElevation[0];
        WorldCoordinate coordinate = new WorldCoordinate(px, columnHeight, pz);

        WorldSkeletonFacts facts;
        bool ok = WorldSkeletonProvider.TryGetFacts(coordinate, settings, out facts);
        Check(ok, "1a：合法坐标 TryGetFacts 返回 true（" + coordinate + "）");
        Check(facts.IsValid, "1b：成功结果 IsValid == true");

        // 整数三元组重载必须与坐标重载完全等价（纯类型适配，不得引入分歧）
        WorldSkeletonFacts factsByTriple;
        bool okByTriple = WorldSkeletonProvider.TryGetFacts(px, columnHeight, pz, settings, out factsByTriple);
        Check(okByTriple && factsByTriple.IsValid, "1c：整数三元组重载同样成功（ok=True valid=True）");
        Check(SameFacts(facts, factsByTriple), "1d：两个重载结果逐字段相同（无第二套路径）");

        // 纯函数确定性：同坐标同设置重复查询逐字段相同（D5/PM52）
        WorldSkeletonFacts factsAgain;
        WorldSkeletonProvider.TryGetFacts(coordinate, settings, out factsAgain);
        Check(SameFacts(facts, factsAgain), "1e：同坐标同设置重复查询 ⇒ 逐字段相同（确定性）");

        // ================= 2. IsValid 与既有契约一致 =================
        WorldSkeletonFacts invalidDefault = default(WorldSkeletonFacts);
        Check(!invalidDefault.IsValid, "2a：default(WorldSkeletonFacts).IsValid == false（契约）");
        Check(invalidDefault.Elevation == 0 && !invalidDefault.Ocean && invalidDefault.Continent == 0,
            "2b：default 事实三字段为默认值（0 / false / 0）");

        bool allValid = true;
        for (int i = 0; i < sampleCount; i++)
        {
            WorldSkeletonFacts columnFacts;
            if (!WorldSkeletonProvider.TryGetFacts(
                    sampleX[i], sampleElevation[i], sampleZ[i], settings, out columnFacts) ||
                !columnFacts.IsValid)
            {
                allValid = false;
            }
        }
        Check(allValid, "2c：全部采样列成功路径 IsValid 恒为 true（" +
                        sampleCount.ToString(CultureInfo.InvariantCulture) + " 列）");

        // ================= 3. Elevation == HeightFunction =================
        bool elevationMatches = true;
        bool columnInvariant = true;
        for (int i = 0; i < sampleCount; i++)
        {
            int expected = HeightFunction.GetTerrainHeight(sampleX[i], sampleZ[i], settings);

            WorldSkeletonFacts atSurface;
            WorldSkeletonProvider.TryGetFacts(sampleX[i], sampleElevation[i], sampleZ[i], settings, out atSurface);
            if (atSurface.Elevation != expected) { elevationMatches = false; }

            // 同一列、不同 Y（仍在合法垂直范围内）⇒ Elevation 必须恒定（列级量，IF-3）
            WorldSkeletonFacts atBottom;
            WorldSkeletonFacts atZero;
            WorldSkeletonProvider.TryGetFacts(sampleX[i], 0, sampleZ[i], settings, out atBottom);
            WorldSkeletonProvider.TryGetFacts(sampleX[i], settings.WorldHeight - 1, sampleZ[i], settings, out atZero);
            if (atBottom.Elevation != expected || atZero.Elevation != expected) { columnInvariant = false; }
        }
        Check(elevationMatches, "3a：facts.Elevation == HeightFunction.GetTerrainHeight（逐列，"
                                + sampleCount.ToString(CultureInfo.InvariantCulture) + " 列）");
        Check(columnInvariant, "3b：同列不同 Y（0 / SurfaceHeight / WorldHeight-1）⇒ Elevation 恒定（列级量）");

        // ================= 4. Ocean 严格小于 settings.SeaLevel =================
        bool oceanFormula = true;
        for (int i = 0; i < sampleCount; i++)
        {
            WorldSkeletonFacts columnFacts;
            WorldSkeletonProvider.TryGetFacts(sampleX[i], sampleElevation[i], sampleZ[i], settings, out columnFacts);
            if (columnFacts.Ocean != (columnFacts.Elevation < settings.SeaLevel)) { oceanFormula = false; }
        }
        Check(oceanFormula, "4a：默认配置下 Ocean == (Elevation < settings.SeaLevel)（逐列；SeaLevel=" +
                           settings.SeaLevel.ToString(CultureInfo.InvariantCulture) + "）");

        // 【契约锚点】Ocean = Elevation < SeaLevel（严格小于；IF-6 / WC3）：
        //   SeaLevel 高于地表（Elevation < SeaLevel）⇒ 该列被淹没 ⇒ Ocean = true（海洋）；
        //   SeaLevel 低于地表（Elevation > SeaLevel）⇒ 地表露出 ⇒ Ocean = false（陆地）；
        //   SeaLevel == Elevation ⇒ Ocean = false（不带等号）。
        // 命名一律以『海平面相对地表的位置』为准（seaAboveSurface / seaAtSurface / seaBelowSurface），
        // 避免再次出现上下颠倒的期望（R42 首次真跑即因期望写反而产生 4 条 FAIL）。
        WorldGenerationSettings seaAtSurface = WithSeaLevel(settings, columnHeight);
        WorldGenerationSettings seaBelowSurface = WithSeaLevel(settings, columnHeight - 1);
        WorldGenerationSettings seaAboveSurface = WithSeaLevel(settings, columnHeight + 1);
        WorldGenerationSettings atZeroSea = WithSeaLevel(settings, 0);

        CheckValidSettings(seaAtSurface, "4b-前置：SeaLevel = Elevation 的配置合法");
        CheckValidSettings(seaBelowSurface, "4c-前置：SeaLevel = Elevation-1 的配置合法");
        CheckValidSettings(seaAboveSurface, "4d-前置：SeaLevel = Elevation+1 的配置合法");
        CheckValidSettings(atZeroSea, "4e-前置：SeaLevel = 0 的配置合法");

        WorldSkeletonFacts seaAtFacts;
        WorldSkeletonFacts seaBelowFacts;
        WorldSkeletonFacts seaAboveFacts;
        WorldSkeletonFacts zeroSeaFacts;
        WorldSkeletonProvider.TryGetFacts(coordinate, seaAtSurface, out seaAtFacts);
        WorldSkeletonProvider.TryGetFacts(coordinate, seaBelowSurface, out seaBelowFacts);
        WorldSkeletonProvider.TryGetFacts(coordinate, seaAboveSurface, out seaAboveFacts);
        WorldSkeletonProvider.TryGetFacts(coordinate, atZeroSea, out zeroSeaFacts);

        Check(seaAtFacts.Ocean == false,
            "4f：Elevation == SeaLevel(" + columnHeight.ToString(CultureInfo.InvariantCulture) +
            ") ⇒ Ocean == false（严格小于，不带等号）");
        Check(seaAboveFacts.Ocean == true,
            "4g：Elevation(" + columnHeight.ToString(CultureInfo.InvariantCulture) + ") < SeaLevel(" +
            (columnHeight + 1).ToString(CultureInfo.InvariantCulture) + ") ⇒ Ocean == true（海平面高于地表 ⇒ 该列被淹没）");
        Check(seaBelowFacts.Ocean == false,
            "4h：Elevation(" + columnHeight.ToString(CultureInfo.InvariantCulture) + ") > SeaLevel(" +
            (columnHeight - 1).ToString(CultureInfo.InvariantCulture) + ") ⇒ Ocean == false（地表高于海平面 ⇒ 陆地）");
        Check(zeroSeaFacts.Ocean == false, "4i：SeaLevel = 0 ⇒ Ocean == false（合法下界）");
        Check(facts.Ocean == (columnHeight < settings.SeaLevel),
            "4j：默认 SeaLevel=64 下的海陆结论由既有规则给出（Ocean=" + facts.Ocean +
            "，未修改默认值或边界）");

        // ================= 5. Continent == (Ocean ? 0 : 1) =================
        bool continentFormula = true;
        for (int i = 0; i < sampleCount; i++)
        {
            WorldSkeletonFacts columnFacts;
            WorldSkeletonProvider.TryGetFacts(sampleX[i], sampleElevation[i], sampleZ[i], settings, out columnFacts);
            if (columnFacts.Continent != (columnFacts.Ocean ? 0 : 1)) { continentFormula = false; }
        }
        Check(continentFormula, "5a：Continent == (Ocean ? 0 : 1)（逐列）");
        Check(seaAboveFacts.Continent == 0, "5b：海洋（Ocean=true）⇒ Continent == 0");
        Check(seaAtFacts.Continent == 1, "5c：陆地（Ocean=false）⇒ Continent == 1（单大陆退化模式）");

        // ================= 6. 垂直越界失败且事实无效 =================
        WorldSkeletonFacts belowWorld;
        bool okBelowWorld = WorldSkeletonProvider.TryGetFacts(
            new WorldCoordinate(px, -1, pz), settings, out belowWorld);
        Check(!okBelowWorld, "6a：Y = -1 ⇒ TryGetFacts 返回 false");
        Check(!belowWorld.IsValid, "6b：Y = -1 ⇒ facts.IsValid == false");

        WorldSkeletonFacts atWorldHeight;
        bool okAtWorldHeight = WorldSkeletonProvider.TryGetFacts(
            new WorldCoordinate(px, settings.WorldHeight, pz), settings, out atWorldHeight);
        Check(!okAtWorldHeight,
            "6c：Y = WorldHeight(" + settings.WorldHeight.ToString(CultureInfo.InvariantCulture) +
            ") ⇒ TryGetFacts 返回 false");
        Check(!atWorldHeight.IsValid, "6d：Y = WorldHeight ⇒ facts.IsValid == false");
        Check(IsDefaultFacts(belowWorld) && IsDefaultFacts(atWorldHeight),
            "6e：两种越界返回的事实三字段均为默认值（不返回部分结果）");

        // ================= 7. SeaLevel 变 ⇒ Ocean 变、Elevation 不变 =================
        Check(seaAboveFacts.Ocean != seaBelowFacts.Ocean,
            "7a：改变合法 SeaLevel（" + (columnHeight - 1).ToString(CultureInfo.InvariantCulture) +
            " → " + (columnHeight + 1).ToString(CultureInfo.InvariantCulture) + "）⇒ Ocean 判定改变（陆地 → 海洋）");
        Check(seaBelowFacts.Elevation == seaAtFacts.Elevation &&
              seaAtFacts.Elevation == seaAboveFacts.Elevation &&
              seaAboveFacts.Elevation == zeroSeaFacts.Elevation,
            "7b：三种合法 SeaLevel 下 facts.Elevation 完全相同（" +
            seaAtFacts.Elevation.ToString(CultureInfo.InvariantCulture) + "）");
        int independentHeight = HeightFunction.GetTerrainHeight(px, pz, settings);
        Check(seaAtFacts.Elevation == independentHeight &&
              seaAboveFacts.Elevation == independentHeight &&
              zeroSeaFacts.Elevation == independentHeight,
            "7c：Elevation 与 SeaLevel 无关，恒等于 HeightFunction 结果（" +
            independentHeight.ToString(CultureInfo.InvariantCulture) + "）");

        // ================= 8. 跨坐标不返回同一份事实 =================
        bool perColumnTruth = true;
        for (int i = 0; i < sampleCount; i++)
        {
            WorldSkeletonFacts columnFacts;
            WorldSkeletonProvider.TryGetFacts(sampleX[i], sampleElevation[i], sampleZ[i], settings, out columnFacts);
            int expectedHeight = HeightFunction.GetTerrainHeight(sampleX[i], sampleZ[i], settings);
            if (columnFacts.Elevation != expectedHeight) { perColumnTruth = false; }
        }
        Check(perColumnTruth, "8a：每一列的 facts.Elevation 与其坐标的独立高度计算一致（无跨坐标串值）");

        bool distinctFactsForDistinctColumns = true;
        for (int i = 0; i < sampleCount; i++)
        {
            for (int j = i + 1; j < sampleCount; j++)
            {
                if (sampleElevation[i] == sampleElevation[j]) { continue; }
                WorldSkeletonFacts left;
                WorldSkeletonFacts right;
                WorldSkeletonProvider.TryGetFacts(sampleX[i], sampleElevation[i], sampleZ[i], settings, out left);
                WorldSkeletonProvider.TryGetFacts(sampleX[j], sampleElevation[j], sampleZ[j], settings, out right);
                if (left.Elevation == right.Elevation) { distinctFactsForDistinctColumns = false; }
            }
        }
        Check(distinctFactsForDistinctColumns,
            "8b：不同高度的列不会返回相同 Elevation（无错误缓存；比较 " +
            sampleCount.ToString(CultureInfo.InvariantCulture) + " 列两两组合）");

        // ================= 9. 失败路径不得返回"看似有效"的事实 =================
        WorldGenerationSettings emptySettings = default(WorldGenerationSettings);
        WorldSkeletonFacts emptyFacts;
        bool okEmpty = WorldSkeletonProvider.TryGetFacts(
            new WorldCoordinate(0, 0, 0), emptySettings, out emptyFacts);
        Check(!okEmpty && !emptyFacts.IsValid,
            "9a：default(WorldGenerationSettings)（WorldHeight=0）⇒ 失败且事实无效");

        WorldSkeletonFacts emptyFactsTriple;
        bool okEmptyTriple = WorldSkeletonProvider.TryGetFacts(0, 0, 0, emptySettings, out emptyFactsTriple);
        Check(!okEmptyTriple && !emptyFactsTriple.IsValid,
            "9b：整数三元组重载在失败路径上行为一致（失败且事实无效）");

        WorldSkeletonFacts negativeFactsTriple;
        bool okNegativeTriple = WorldSkeletonProvider.TryGetFacts(px, -1, pz, settings, out negativeFactsTriple);
        Check(!okNegativeTriple && !negativeFactsTriple.IsValid,
            "9c：整数三元组重载在 Y=-1 上行为一致（失败且事实无效）");

        Check(IsDefaultFacts(emptyFacts) && IsDefaultFacts(negativeFactsTriple),
            "9d：失败返回的事实保持默认值（不会被误认为有效事实）");

        // ================= 10. 退出码传播能力 =================
        Check(ExitCodeFor(0) == 0, "10a：失败计数 0 ⇒ 退出码 0（Batchmode 视为通过）");
        Check(ExitCodeFor(1) == 1, "10b：失败计数 1 ⇒ 退出码 1");
        Check(ExitCodeFor(3) == 1, "10c：失败计数 3 ⇒ 退出码 1（任何非零失败都传播）");

        if (IsSelfFailureRequested())
        {
            // 仅在命令行显式给出 -wsProviderSelfFailure 时触发：用于证明"断言失败 ⇒ 非零退出码"。
            Check(false, "[自检] 命令行开关注入的故意失败（用于验证 Batchmode 退出码传播）");
        }

        Report();
    }

    /// <summary>
    /// **批处理入口**：返回码由断言结果决定（`failures == 0 ? 0 : 1`）；
    /// 任何异常（配置非法、类型缺失、意外运行时错误）都不可能落入退出码 0。
    /// </summary>
    public static void RunBatch()
    {
        try
        {
            Validate();
        }
        catch (Exception exception)
        {
            Debug.LogError("[WorldSkeletonProvider] 批处理失败（exit 1）：" + exception.Message);
            EditorApplication.Exit(1);
            return;
        }

        EditorApplication.Exit(ExitCodeFor(failures));
    }

    // -----------------------------------------------------------------
    // 纯辅助（无世界规则：只做采样发现、构造与比较）
    // -----------------------------------------------------------------

    /// <summary>退出码映射（唯一权威）：0 个失败 ⇒ 0；任何非零失败 ⇒ 1。</summary>
    private static int ExitCodeFor(int failureCount)
    {
        return failureCount == 0 ? 0 : 1;
    }

    /// <summary>命令行是否请求注入自检失败（仅用于退出码传播验证）。</summary>
    private static bool IsSelfFailureRequested()
    {
        string[] arguments = Environment.GetCommandLineArgs();
        for (int i = 0; i < arguments.Length; i++)
        {
            if (string.Equals(arguments[i], SelfFailureSwitch, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 运行时发现采样列：在固定网格内找出地表高度严格位于 (0, WorldHeight-1) 的列，
    /// 优先收集 **不同 Elevation** 的列（保证断言非平凡）。不硬编码任何高度数值。
    /// </summary>
    private static int CollectSampleColumns(
        WorldGenerationSettings settings, int[] sampleX, int[] sampleZ, int[] sampleElevation)
    {
        bool[] usedElevations = new bool[settings.WorldHeight + 1];
        int count = 0;

        for (int x = 0; x < 32 && count < MaxSampleColumns; x++)
        {
            for (int z = 0; z < 32 && count < MaxSampleColumns; z++)
            {
                int height = HeightFunction.GetTerrainHeight(x, z, settings);

                // 只接受"不贴边界"的列，使 SeaLevel = height ± 1 / 0 等构造都保持合法
                if (height <= 0 || height >= settings.WorldHeight - 1) { continue; }
                if (usedElevations[height]) { continue; }

                usedElevations[height] = true;
                sampleX[count] = x;
                sampleZ[count] = z;
                sampleElevation[count] = height;
                count++;
            }
        }

        return count;
    }

    /// <summary>用项目认可的七参数构造复制配置，仅替换 SeaLevel（不改其它字段）。</summary>
    private static WorldGenerationSettings WithSeaLevel(WorldGenerationSettings settings, int seaLevel)
    {
        return new WorldGenerationSettings(
            settings.Seed,
            settings.TerrainHeight,
            settings.SoilDepth,
            settings.WorldHeight,
            settings.TerrainFrequency,
            settings.TerrainAmplitude,
            seaLevel);
    }

    private static int CountDistinct(int[] values, int count)
    {
        int distinct = 0;
        for (int i = 0; i < count; i++)
        {
            bool seen = false;
            for (int j = 0; j < i; j++)
            {
                if (values[j] == values[i]) { seen = true; break; }
            }
            if (!seen) { distinct++; }
        }
        return distinct;
    }

    private static bool SameFacts(WorldSkeletonFacts left, WorldSkeletonFacts right)
    {
        return left.IsValid == right.IsValid &&
               left.Elevation == right.Elevation &&
               left.Ocean == right.Ocean &&
               left.Continent == right.Continent;
    }

    private static bool IsDefaultFacts(WorldSkeletonFacts facts)
    {
        return !facts.IsValid && facts.Elevation == 0 && !facts.Ocean && facts.Continent == 0;
    }

    private static string DescribeValidity(WorldGenerationSettings settings)
    {
        string reason;
        bool valid = settings.IsValid(out reason);
        return "valid=" + valid + " / " + settings;
    }

    private static void CheckValidSettings(WorldGenerationSettings settings, string label)
    {
        string reason;
        Check(settings.IsValid(out reason), label + "（" + settings + "）");
    }

    private static void Check(bool condition, string label)
    {
        checks++;

        if (condition)
        {
            Debug.Log("[WorldSkeletonProvider] PASS: " + label);
        }
        else
        {
            failures++;
            Debug.LogError("[WorldSkeletonProvider] FAIL: " + label);
        }
    }

    private static void Report()
    {
        StringBuilder summary = new StringBuilder();
        summary.Append("[WorldSkeletonProvider] WorldSkeleton 事实适配层验证 ⇒ checks=")
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