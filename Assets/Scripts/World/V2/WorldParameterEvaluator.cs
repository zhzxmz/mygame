using System;

using VoxelWorld.Generation;

namespace World.V2
{
    /// <summary>
    /// **参数求值方**（World V2 · Plan Phase 1.5 · WorldState 层的求值入口）。
    ///
    /// 职责（唯一）：
    ///   按 `WorldState_Runtime_Contract.md` §2.3 的**冻结求值顺序**（步骤 0–4）逐参数求 `BaseValue`，
    ///   并以 `WorldParameterState`（三态快照）发布结果。
    ///
    /// 数据流（与契约一致）：
    /// <code>
    ///   Seed + WorldCoordinate
    ///        ↓  WorldParameterRules（冻结顺序 0→4，规则表）
    ///   BaseValue（逐参数）                      ← Evaluate()：发布 **Base 段**（IsFinalized = false）
    ///        ↓  Modifier（唯一写入者 = Region 层；由外部传入 RegionModifier）
    ///   FinalValue = clamp01(BaseValue + Modifier) ← Finalize()：发布 **Final 段**（IsFinalized = true）
    /// </code>
    ///
    /// 契约依据：
    ///   · `DR25`：`BaseValue` 由 **WorldState 层**（参数求值方）产生；
    ///   · `PC19` **两段式发布**：本类型产出 **Base 段**；`Final` 段在 Region（Phase 3）之后定稿；
    ///   · `WSC §2.2`：逐参数合成类型（加法 + 钳制 / 无合成转发 / 无合成派生）；`WSC9` / `WSC12`；
    ///   · `WSC §2.3`：求值内部顺序与**无环性**（`WSC16`）、`Fertility` 只读 Base 级上游（`WSC17`）；
    ///   · `WSC14` **零修正显式性**与 `WSC22`：Region 层不存在 ⇒ `FinalValue == BaseValue`（逐分量、逐值相等）；
    ///   · `WSC10`：`Elevation` / `Ocean` **不得**出现在任何修正求和式中；`WSC13`：整数/布尔不得插值；
    ///   · `PM27`–`PM34`：三态、可独立求值、不可变定稿；`PM37`–`PM46`：定长索引、越界拒绝、禁动态增删；
    ///   · `WSC103` / `DR35`：**不得**存在第二套求值路径（查询必须走本入口）。
    ///
    /// 明确**不做**（本回合边界）：
    ///   · 本类型**不自行产生** Region 修正量：`Evaluate` 的 `Modifier` 恒为 0（属 `PC20` 的 Phase 3 供给），
    ///     `Finalize` 只**消费**外部传入的 `RegionModifier`，绝不自行推断或改写修正量；
    ///   · `Finalize` 的**编排**（Base → Region → Finalize 的执行顺序）由 `WorldStatePipeline` 承担，
    ///     本类保持"唯一合成权威"，不引入第二条求值路径（`WSC103` / `DR35`）；
    ///   · 不实现噪声算法、不生成地形、不读取高度场（`Elevation` 为占位，见 `WorldParameterRules`）；
    ///   · 不读取 Biome / 生态 / 资源 / Geology / 体素 / Chunk / Runtime / Player / Entity（`WSC19` / `PZ7`）；
    ///   · 无副作用：不写体素、不建 Chunk、不置脏、不生成对象（`WSC20` / `T4`）；
    ///   · 不使用 UnityEngine、`Random`、`Time`（`PZ5` / `PZ6`）。
    ///
    /// 确定性（`D5` / `PZ1` / `PM52`）：输出只由 `(Seed, WorldCoordinate)` 与**编译期固定的规则表**决定；
    /// 不读取任何全局状态、加载顺序、帧序或缓存，因此同输入**逐字段相同**。
    /// </summary>
    public static class WorldParameterEvaluator
    {
        /// <summary>
        /// 本回合的 Region 修正量：Region 层未接入（`PC20`），零修正是**显式**定义而非缺省猜测（`WSC14`）。
        /// </summary>
        private const float ZeroRegionModifier = 0f;

        /// <summary>`WSC §2.3` 中属于**参数规则**的步骤上界（0–4）；步骤 5–7 属 Region 修正与合成。</summary>
        private const int MaxBaseEvaluationStep = 4;

        static WorldParameterEvaluator()
        {
            ValidateArchitecture();
        }

        /// <summary>
        /// 单点参数求值（唯一入口）：`Seed + WorldCoordinate → WorldParameterState`。
        /// 纯函数、无副作用、可独立重复求值（`WSC23` / `PM53`）；不要求任何 Chunk / 世界实例存在（`PM54` / `PC77`）。
        /// </summary>
        public static WorldParameterState Evaluate(WorldSeed seed, WorldGenerationSettings settings, WorldCoordinate position)
        {
            int capacity = WorldParameterState.Capacity;

            // 定长索引容器（索引 = ParameterId；槽 0 不使用，便于按身份直读）：PM37 / PM45 / PM46。
            float[] baseValues = new float[capacity + 1];
            bool[] evaluated = new bool[capacity + 1];

            IWorldParameterBaseReader reader = new EvaluatedBaseReader(baseValues, evaluated);

            // 阶段 A：按冻结求值顺序逐参数求 BaseValue（WSC §2.3 步骤 0–4）。
            // 规则只能读到"已完成步骤"的值 ⇒ 顺序无环（WSC16）。
            for (int i = 0; i < WorldParameterRules.Count; i++)
            {
                IWorldParameterRule rule = WorldParameterRules.GetRule(i);
                int parameterId = rule.ParameterId;

                float baseValue = rule.EvaluateBaseValue(reader, seed, position, settings);

                baseValues[parameterId] = baseValue;
                evaluated[parameterId] = true;
            }

            // 阶段 B：按**参数身份顺序**装配 **Base 段**快照（槽位 = ParameterId - 1；与容器索引语义一致）。
            // 【两段式发布 · PC19】本段**不产生定稿的 Final**：Region（Phase 3）尚未产出修正量，
            // 因此 `Modifier` 显式取 0（WSC14 零修正显式性），`FinalValue` 暂置为 Base 且**不具权威性**
            // （由 `WorldParameterState.IsFinalized == false` 显式标记；定稿走 `Finalize`）。
            WorldParameterValue[] values = new WorldParameterValue[capacity];
            for (int slot = 0; slot < capacity; slot++)
            {
                int parameterId = slot + 1;

                if (!evaluated[parameterId])
                {
                    throw new InvalidOperationException(
                        "参数身份 " + parameterId.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                        " 没有求值规则；规则表必须完整覆盖环境参数组（PC19 / PM45）。");
                }

                float baseValue = baseValues[parameterId];

                values[slot] = new WorldParameterValue(parameterId, baseValue, ZeroRegionModifier, baseValue);
            }

            // Base 段快照（不可变、未定稿：PM34 / PC19）。
            return new WorldParameterState(values, false);
        }

        /// <summary>
        /// **Final 段定稿（唯一合成权威）**：`Base 段 + Region 修正量 → Final 段`（`PC19` 第二段）。
        ///
        /// 前置：`baseState` 必须是**未定稿**的 Base 段快照（来自 <see cref="Evaluate"/>）；
        /// `regionModifier` 由 Region 层产出（`PC20`：Region 是修正量的唯一写入者）。
        ///
        /// 语义（`WSC §2.2` 冻结）：
        ///   · 6 个 `AdditiveClamped` 参数：`Final = clamp01(Base + Modifier)`（`WSC9` / `WSC12`）；
        ///   · `Elevation` / `Ocean`：`Modifier` **结构上恒为 0**、`Final = Base`（`WSC10` / `WSC13`）。
        /// 纯函数、无副作用、不缓存；同输入 ⇒ 逐字段相同（`PM52`）。
        /// </summary>
        /// <param name="baseState">`Evaluate` 产出的 Base 段快照（`IsFinalized == false`）。</param>
        /// <param name="regionModifier">Region 层修正量（占位实现期可为全零）。</param>
        /// <returns>已定稿快照（`IsFinalized == true`）。</returns>
        public static WorldParameterState Finalize(WorldParameterState baseState, RegionModifier regionModifier)
        {
            if (baseState == null)
            {
                throw new ArgumentNullException("baseState");
            }

            // 注意：`RegionModifier` 是**值类型**（readonly struct），不存在 null，
            // 因此**不做** null 检查；"无修正"必须以全零修正量显式表达（WSC14）。
            if (baseState.IsFinalized)
            {
                throw new InvalidOperationException(
                    "Base 段快照已定稿，不得二次定稿（PC19：Final 段只能由 Base 段 + Region 修正量产生一次）。");
            }

            int capacity = WorldParameterState.Capacity;
            WorldParameterValue[] values = new WorldParameterValue[capacity];

            for (int slot = 0; slot < capacity; slot++)
            {
                int parameterId = slot + 1;
                WorldParameterValue source = baseState[slot];

                if (source.ParameterId != parameterId)
                {
                    throw new InvalidOperationException(
                        "Base 段快照的身份顺序非法（槽位 " + slot.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                        " 期望身份 " + parameterId.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                        "，实际 " + source.ParameterId.ToString(System.Globalization.CultureInfo.InvariantCulture) + "）（PM37）。");
                }

                float modifier = ModifierFor(parameterId, regionModifier);
                float finalValue = Compose(KindOf(parameterId), source.BaseValue, modifier);

                values[slot] = new WorldParameterValue(parameterId, source.BaseValue, modifier, finalValue);
            }

            return new WorldParameterState(values, true);
        }

        /// <summary>
        /// 逐参数取 Region 修正分量（参数身份 → `RegionModifier` 分量）。
        /// `Elevation`（序 3）与 `Ocean`（序 8）**结构上恒为 0**：不得进入任何修正求和式（`WSC10`）。
        /// </summary>
        private static float ModifierFor(int parameterId, RegionModifier regionModifier)
        {
            switch (parameterId)
            {
                case WorldParameterIds.Temperature:
                    return regionModifier.TemperatureModifier;

                case WorldParameterIds.Moisture:
                    return regionModifier.MoistureModifier;

                case WorldParameterIds.Fertility:
                    return regionModifier.FertilityModifier;

                case WorldParameterIds.MagicDensity:
                    return regionModifier.MagicDensityModifier;

                case WorldParameterIds.HolyInfluence:
                    return regionModifier.HolyInfluenceModifier;

                case WorldParameterIds.CorruptionInfluence:
                    return regionModifier.CorruptionInfluenceModifier;

                case WorldParameterIds.Elevation:
                case WorldParameterIds.Ocean:
                    return ZeroRegionModifier;

                default:
                    throw new ArgumentOutOfRangeException("parameterId");
            }
        }

        /// <summary>
        /// 参数身份 → 合成类型（**按规则表现查**，不建静态表：保持"无静态可变状态"纪律）。
        /// 规则表已在静态构造期校验完整性，因此必有一项匹配。
        /// </summary>
        private static WorldParameterCompositionKind KindOf(int parameterId)
        {
            for (int i = 0; i < WorldParameterRules.Count; i++)
            {
                IWorldParameterRule rule = WorldParameterRules.GetRule(i);

                if (rule.ParameterId == parameterId)
                {
                    return rule.CompositionKind;
                }
            }

            throw new InvalidOperationException(
                "参数身份没有登记合成类型（PM45）：" + parameterId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// 逐参数合成（`WSC §2.2` 冻结）：
        ///   · `AdditiveClamped`：`clamp01(Base + Modifier)`（`WSC9` / `WSC12`）；
        ///   · `Forwarded` / `Derived`：**无合成**，`Final = Base`，且 `Modifier` 不参与任何求和式（`WSC10`），
        ///     不做插值/加权（`WSC13`）。
        /// </summary>
        private static float Compose(WorldParameterCompositionKind kind, float baseValue, float modifier)
        {
            switch (kind)
            {
                case WorldParameterCompositionKind.AdditiveClamped:
                    return WorldParameterRules.Clamp01(baseValue + modifier);

                case WorldParameterCompositionKind.Forwarded:
                case WorldParameterCompositionKind.Derived:
                    return baseValue;

                default:
                    throw new ArgumentOutOfRangeException("kind");
            }
        }

        /// <summary>
        /// **架构自检**（静态构造期执行一次）：把契约里的结构性约束变成"不满足即启动失败"，避免静默退化。
        /// 校验项：容器容量 = 身份表数量；规则表完整覆盖身份表（无缺项 / 无重号 / 无越界）；
        /// 步骤号落在 `WSC §2.3` 的参数规则区间（0–4）且**非递减**（顺序即契约，禁止倒置）。
        /// </summary>
        private static void ValidateArchitecture()
        {
            if (WorldParameterState.Capacity != WorldParameterIds.Count)
            {
                throw new InvalidOperationException(
                    "WorldParameterState.Capacity 与附录 B 环境参数组数量不一致（PM19 / PM46）。");
            }

            if (WorldParameterRules.Count != WorldParameterIds.Count)
            {
                throw new InvalidOperationException(
                    "规则表条数与附录 B 环境参数组数量不一致（PM20 / PM45）。");
            }

            bool[] seen = new bool[WorldParameterIds.Count + 1];
            int previousStep = -1;

            for (int i = 0; i < WorldParameterRules.Count; i++)
            {
                IWorldParameterRule rule = WorldParameterRules.GetRule(i);

                if (rule.ParameterId < WorldParameterIds.First || rule.ParameterId > WorldParameterIds.Last)
                {
                    throw new InvalidOperationException(
                        "规则表出现未登记的 ParameterId（PM19 / PM46）：" + rule.ParameterId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }

                if (seen[rule.ParameterId])
                {
                    throw new InvalidOperationException(
                        "规则表出现重复的 ParameterId（PM45）：" + rule.ParameterId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }

                seen[rule.ParameterId] = true;

                if (rule.EvaluationStep < 0 || rule.EvaluationStep > MaxBaseEvaluationStep)
                {
                    throw new InvalidOperationException(
                        "EvaluationStep 必须落在 WSC §2.3 的参数规则步骤区间 0–4 内：" + rule.EvaluationStep.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }

                if (rule.EvaluationStep < previousStep)
                {
                    throw new InvalidOperationException(
                        "规则表顺序与 WSC §2.3 的冻结求值顺序不一致（步骤倒置），违反 WSC16。");
                }

                previousStep = rule.EvaluationStep;
            }

            for (int parameterId = WorldParameterIds.First; parameterId <= WorldParameterIds.Last; parameterId++)
            {
                if (!seen[parameterId])
                {
                    throw new InvalidOperationException(
                        "规则表缺少已登记参数（PC19）：" + parameterId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
            }

            // 【合成类型不变式（WSC §2.2 / WSC10 / WSC13）】把契约文本变成"不满足即启动失败"：
            //   · Elevation（序 3）必须 Forwarded、Ocean（序 8）必须 Derived —— 二者**无合成**，
            //     Region 修正量在结构上不得进入其求和式；
            //   · 其余 6 个环境参数必须 AdditiveClamped —— 否则 Region 修正量无法被合成。
            for (int i = 0; i < WorldParameterRules.Count; i++)
            {
                IWorldParameterRule rule = WorldParameterRules.GetRule(i);
                WorldParameterCompositionKind kind = rule.CompositionKind;

                if (rule.ParameterId == WorldParameterIds.Elevation && kind != WorldParameterCompositionKind.Forwarded)
                {
                    throw new InvalidOperationException("Elevation 的合成类型必须是 Forwarded（WSC10 / WSC13）。");
                }

                if (rule.ParameterId == WorldParameterIds.Ocean && kind != WorldParameterCompositionKind.Derived)
                {
                    throw new InvalidOperationException("Ocean 的合成类型必须是 Derived（WSC10 / WSC13）。");
                }

                if (rule.ParameterId != WorldParameterIds.Elevation &&
                    rule.ParameterId != WorldParameterIds.Ocean &&
                    kind != WorldParameterCompositionKind.AdditiveClamped)
                {
                    throw new InvalidOperationException(
                        "环境参数（除 Elevation / Ocean）的合成类型必须是 AdditiveClamped（WSC §2.2）：" +
                        rule.ParameterId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
            }
        }

        /// <summary>
        /// **已求值 Base 值的只读视图**（`IWorldParameterBaseReader` 的实现）。
        /// 只暴露已完成步骤的值：未求值返回 `false`，越界身份抛错（`PM46`）；
        /// 因此规则**结构上不可能**读取后续步骤（`WSC16` 无环）。
        /// </summary>
        private sealed class EvaluatedBaseReader : IWorldParameterBaseReader
        {
            private readonly float[] baseValues;
            private readonly bool[] evaluated;

            public EvaluatedBaseReader(float[] baseValues, bool[] evaluated)
            {
                this.baseValues = baseValues;
                this.evaluated = evaluated;
            }

            public bool TryGetBaseValue(int parameterId, out float baseValue)
            {
                if (parameterId < WorldParameterIds.First || parameterId > WorldParameterIds.Last)
                {
                    throw new ArgumentOutOfRangeException("parameterId");
                }

                if (evaluated[parameterId])
                {
                    baseValue = baseValues[parameterId];
                    return true;
                }

                baseValue = 0f;
                return false;
            }
        }
    }
}
