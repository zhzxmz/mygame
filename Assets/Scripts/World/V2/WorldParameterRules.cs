using VoxelWorld.Generation;

namespace World.V2
{
    /// <summary>
    /// **参数求值规则表**（World V2 · Plan Phase 1.5）。
    ///
    /// 结构纪律（全部来自已冻结契约，替换规则时**不得**放宽）：
    ///   · **定长索引模型**：规则集合编译期固定、有限、显式，禁止动态注册/运行时增删（`PM20` / `PM37` / `PM45`）；
    ///   · **禁止字典/哈希容器作为架构基础**（`PM40`）：规则表是**有序定长数组**，遍历顺序 = 冻结求值顺序；
    ///   · **顺序 = `WorldState_Runtime_Contract.md` §2.3 步骤 0–4**（步骤 5–7 属 Region 修正与合成，不在规则表内）；
    ///   · **顺序无环**：规则只能通过 <see cref="IWorldParameterBaseReader"/> 读取**已完成步骤**的 Base 值（`WSC16`）；
    ///   · 每条规则必须声明 `ParameterId`（附录 B）与合成类型（`WSC §2.2`），由
    ///     <see cref="WorldParameterEvaluator"/> 静态自检（缺项 / 重号 / 顺序倒置一律抛错）。
    ///
    /// **本回合（Phase 1.5）的规则内容是占位实现**，允许返回确定性基础结果：
    /// 每条规则的注释逐条声明【真实来源】与【替换点】；真实求值属后续轮次，本回合不实现任何
    /// 噪声算法、不生成地形、不读取 Biome / Geology / 生态 / 资源 / 体素 / Chunk / 运行时 / 玩家。
    ///
    /// 约束：不使用 UnityEngine；无副作用；不写任何状态。
    /// </summary>
    public static class WorldParameterRules
    {
        /// <summary>冻结的求值顺序（`WSC §2.3` 步骤 0 → 4；同一参数只出现一次）。</summary>
        private static readonly IWorldParameterRule[] ordered = new IWorldParameterRule[]
        {
            new ElevationRule(),                 // 步骤 0
            new OceanRule(),                     // 步骤 1
            new TemperatureRule(),               // 步骤 2
            new MoistureRule(),                  // 步骤 2
            new FertilityRule(),                 // 步骤 3
            new MagicDensityRule(),              // 步骤 4
            new HolyInfluenceRule(),             // 步骤 4
            new CorruptionInfluenceRule()        // 步骤 4
        };

        /// <summary>规则条数（= 环境参数组登记数量，由静态自检与 <see cref="WorldParameterIds.Count"/> 比对）。</summary>
        public static int Count
        {
            get { return ordered.Length; }
        }

        /// <summary>按**求值顺序**取规则（只读；越界即拒绝，`PM46`）。</summary>
        public static IWorldParameterRule GetRule(int index)
        {
            if (index < 0 || index >= ordered.Length)
            {
                throw new System.ArgumentOutOfRangeException("index");
            }

            return ordered[index];
        }

        /// <summary>契约钳制语义的本地实现（`WSC9` / `WSC12` / `PS5`）：连续量合成后必须落在 `[0,1]`。</summary>
        internal static float Clamp01(float value)
        {
            if (value < 0f)
            {
                return 0f;
            }

            if (value > 1f)
            {
                return 1f;
            }

            return value;
        }

        /// <summary>
        /// 步骤 0 · `Elevation` · **只读转发**量（`WSC §2.2` / `§2.3` 步骤 0）。
        ///
        /// 【真实来源】既有高度场（`HeightFunction` / `SurfaceHeightAt`）——只读转发；
        ///   量纲 = 整数体素（y 轴），范围 `[0, WorldHeight-1]`（契约 §5.2；`WorldHeight` 来自 `WorldGenerationSettings`）。
        ///   按契约 `WSC13` / `PS6`：整数参数**不得**被当作连续量插值。
        ///
        /// 【已接线】直接只读转发既有高度场 `HeightFunction.GetTerrainHeight(x, z, settings)`（不再是域哈希占位）。
        /// 【登记】真实 `Elevation` 是转发量，**不使用任何参数域**；契约 §6.4 登记保持不变（`WPS57`）。
        /// 【前置】`settings` 由调用方（世界创建 owner）显式提供，本层不自行创建默认配置。
        /// </summary>
        private sealed class ElevationRule : IWorldParameterRule
        {
            public int ParameterId
            {
                get { return WorldParameterIds.Elevation; }
            }

            public int EvaluationStep
            {
                get { return 0; }
            }

            public WorldParameterCompositionKind CompositionKind
            {
                get { return WorldParameterCompositionKind.Forwarded; }
            }

            public float EvaluateBaseValue(IWorldParameterBaseReader evaluated, WorldSeed seed, WorldCoordinate position, WorldGenerationSettings settings)
            {
                // Elevation 终态（WPS55–WPS57 / A-11）：**只读转发**既有高度场，**不使用任何参数域**。
                // 单位沿用既有高度场（整数体素 y），不归一化（PS6 / WSC13）。
                return HeightFunction.GetTerrainHeight(position.X, position.Z, settings);
            }
        }

        /// <summary>
        /// 步骤 1 · `Ocean` · **派生**量（`WSC §2.2` / `§2.3` 步骤 1）。
        ///
        /// 【真实来源】派生规则 `Elevation &lt; SeaLevel`（契约 §3.2 表 A 来源列）；量纲 = 布尔 `{false, true}`。
        ///   依赖**步骤 0** 的 `Elevation`（严格更早的步骤，无环，`WSC16`）；按 `WSC10` **不得**进入任何修正求和式。
        ///
        /// 【已接线】派生形式按契约实现；`SeaLevel` 取自 `WorldGenerationSettings.SeaLevel`（配置，世界创建期）。
        /// 【登记缺口】布尔量在 `float` 容器中的**表示法**属登记缺口（`PM32` 要求"显式 None/布尔语义"，本回合不裁决）：
        ///   本规则只保证**不插值**（`WSC13`，返回 0 或 1），不定义最终表示形态。
        /// </summary>
        private sealed class OceanRule : IWorldParameterRule
        {
            /// <summary>契约表 C 登记的 `SeaLevel` 默认值（占位；真实值来自显式配置）。</summary>
            public int ParameterId
            {
                get { return WorldParameterIds.Ocean; }
            }

            public int EvaluationStep
            {
                get { return 1; }
            }

            public WorldParameterCompositionKind CompositionKind
            {
                get { return WorldParameterCompositionKind.Derived; }
            }

            public float EvaluateBaseValue(IWorldParameterBaseReader evaluated, WorldSeed seed, WorldCoordinate position, WorldGenerationSettings settings)
            {
                float elevation;
                if (!evaluated.TryGetBaseValue(WorldParameterIds.Elevation, out elevation))
                {
                    throw new System.InvalidOperationException(
                        "Ocean 规则只能在步骤 0（Elevation）之后求值（WSC §2.3）。");
                }

                return elevation < settings.SeaLevel ? 1f : 0f;
            }
        }

        /// <summary>
        /// 步骤 2 · `Temperature` · `Base + Modifier`（`WSC §2.2`）。
        ///
        /// 【真实来源】骨架气候带基线 + 配置（`TemperatureBase`，默认 `0.5`）；域 = §6.4 域 1（`Skeleton`，基础气候带）。
        /// 【本回合】骨架未接入 ⇒ 占位：域 1 标签整数哈希归一化到 `[0,1)`（`PZ11` 形态）。
        /// 【替换点】换成骨架气候带 + 配置读取；本规则体整体替换，接口不变。
        /// </summary>
        private sealed class TemperatureRule : IWorldParameterRule
        {
            public int ParameterId
            {
                get { return WorldParameterIds.Temperature; }
            }

            public int EvaluationStep
            {
                get { return 2; }
            }

            public WorldParameterCompositionKind CompositionKind
            {
                get { return WorldParameterCompositionKind.AdditiveClamped; }
            }

            public float EvaluateBaseValue(IWorldParameterBaseReader evaluated, WorldSeed seed, WorldCoordinate position, WorldGenerationSettings settings)
            {
                return WorldParameterHash.Unit(
                    seed,
                    WorldParameterHash.SkeletonDomainId,
                    ParameterId,
                    position);
            }
        }

        /// <summary>
        /// 步骤 2 · `Moisture` · `Base + Modifier`（`WSC §2.2`）。
        ///
        /// 【真实来源】骨架气候带基线 + 配置（`MoistureBase`，默认 `0.5`）；域 = §6.4 域 1（`Skeleton`）。
        /// 【本回合】骨架未接入 ⇒ 占位：域 1 标签整数哈希归一化到 `[0,1)`（`PZ11` 形态）。
        /// 【替换点】同 `Temperature`。
        /// </summary>
        private sealed class MoistureRule : IWorldParameterRule
        {
            public int ParameterId
            {
                get { return WorldParameterIds.Moisture; }
            }

            public int EvaluationStep
            {
                get { return 2; }
            }

            public WorldParameterCompositionKind CompositionKind
            {
                get { return WorldParameterCompositionKind.AdditiveClamped; }
            }

            public float EvaluateBaseValue(IWorldParameterBaseReader evaluated, WorldSeed seed, WorldCoordinate position, WorldGenerationSettings settings)
            {
                return WorldParameterHash.Unit(
                    seed,
                    WorldParameterHash.SkeletonDomainId,
                    ParameterId,
                    position);
            }
        }

        /// <summary>
        /// 步骤 3 · `Fertility` · `Base + Modifier`（`WSC §2.2`）。
        ///
        /// 【真实来源】派生（配置级土壤深度 + **Base 级** `Temperature` / `Moisture`）+ Region 修正（契约 §3.2 表 A）。
        /// 【硬性】`WSC17`：只允许使用**步骤 2 的 Base 级** `Temperature` / `Moisture`，
        ///   **不得**使用 `FinalValue` 级；本规则通过 <see cref="IWorldParameterBaseReader"/> 读取，
        ///   若上游步骤尚未求值即抛错（不可能读取后续步骤，`WSC16`）。
        /// 【本回合】占位派生形式 = `clamp01(Base.Temperature × Base.Moisture)`；
        ///   真实派生形式由 `WorldState_Design.md` §1.3 规则表定义，**尚未实现**。
        /// 【替换点】换成契约 §1.3 的派生规则（含土壤深度来源登记）——属后续轮次。
        /// </summary>
        private sealed class FertilityRule : IWorldParameterRule
        {
            public int ParameterId
            {
                get { return WorldParameterIds.Fertility; }
            }

            public int EvaluationStep
            {
                get { return 3; }
            }

            public WorldParameterCompositionKind CompositionKind
            {
                get { return WorldParameterCompositionKind.AdditiveClamped; }
            }

            public float EvaluateBaseValue(IWorldParameterBaseReader evaluated, WorldSeed seed, WorldCoordinate position, WorldGenerationSettings settings)
            {
                float temperature;
                float moisture;
                if (!evaluated.TryGetBaseValue(WorldParameterIds.Temperature, out temperature) ||
                    !evaluated.TryGetBaseValue(WorldParameterIds.Moisture, out moisture))
                {
                    throw new System.InvalidOperationException(
                        "Fertility 规则只能在步骤 2（Base 级 Temperature / Moisture）之后求值（WSC §2.3 / WSC17）。");
                }

                return Clamp01(temperature * moisture);
            }
        }

        /// <summary>
        /// 步骤 4 · `MagicDensity` · `Base + Modifier`（`WSC §2.2`）。
        ///
        /// 【真实来源】`Base` = **全局基底常量（默认 0）**；`Modifier` = 魔法来源修正（由 Region 层提供，`PC20`）。
        /// 【本回合】Base 直接取**契约登记的默认值 0**（不是随机占位）；Region 未接入 ⇒ 修正为 0（`WSC14`）。
        /// 【替换点】若未来引入配置化基底常量，改为读取配置；`Modifier` 属 Phase 3（Region）供给。
        /// </summary>
        private sealed class MagicDensityRule : IWorldParameterRule
        {
            /// <summary>契约表 A 登记的默认值（无魔力）。</summary>
            private const float BaseConstant = 0f;

            public int ParameterId
            {
                get { return WorldParameterIds.MagicDensity; }
            }

            public int EvaluationStep
            {
                get { return 4; }
            }

            public WorldParameterCompositionKind CompositionKind
            {
                get { return WorldParameterCompositionKind.AdditiveClamped; }
            }

            public float EvaluateBaseValue(IWorldParameterBaseReader evaluated, WorldSeed seed, WorldCoordinate position, WorldGenerationSettings settings)
            {
                return BaseConstant;
            }
        }

        /// <summary>
        /// 步骤 4 · `HolyInfluence` · `Base + Modifier`（`WSC §2.2`）。
        ///
        /// 【真实来源】`Base` = **全局基底常量（默认 0）**；`Modifier` = 圣性来源修正（Region 层提供）。
        /// 【本回合】Base = 契约默认值 0；Region 未接入 ⇒ 修正为 0（`WSC14`）。
        /// </summary>
        private sealed class HolyInfluenceRule : IWorldParameterRule
        {
            /// <summary>契约表 A 登记的默认值（无圣性）。</summary>
            private const float BaseConstant = 0f;

            public int ParameterId
            {
                get { return WorldParameterIds.HolyInfluence; }
            }

            public int EvaluationStep
            {
                get { return 4; }
            }

            public WorldParameterCompositionKind CompositionKind
            {
                get { return WorldParameterCompositionKind.AdditiveClamped; }
            }

            public float EvaluateBaseValue(IWorldParameterBaseReader evaluated, WorldSeed seed, WorldCoordinate position, WorldGenerationSettings settings)
            {
                return BaseConstant;
            }
        }

        /// <summary>
        /// 步骤 4 · `CorruptionInfluence` · `Base + Modifier`（`WSC §2.2`）。
        ///
        /// 【真实来源】`Base` = **全局基底常量（默认 0）**；`Modifier` = 腐化来源修正（Region 层提供）。
        /// 【本回合】Base = 契约默认值 0；Region 未接入 ⇒ 修正为 0（`WSC14`）。
        /// </summary>
        private sealed class CorruptionInfluenceRule : IWorldParameterRule
        {
            /// <summary>契约表 A 登记的默认值（无腐化）。</summary>
            private const float BaseConstant = 0f;

            public int ParameterId
            {
                get { return WorldParameterIds.CorruptionInfluence; }
            }

            public int EvaluationStep
            {
                get { return 4; }
            }

            public WorldParameterCompositionKind CompositionKind
            {
                get { return WorldParameterCompositionKind.AdditiveClamped; }
            }

            public float EvaluateBaseValue(IWorldParameterBaseReader evaluated, WorldSeed seed, WorldCoordinate position, WorldGenerationSettings settings)
            {
                return BaseConstant;
            }
        }
    }
}
