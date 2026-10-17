using System;

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
    ///   BaseValue（逐参数）
    ///        ↓  Modifier（唯一写入者 = Region 层；本回合未接入 ⇒ 0）
    ///   FinalValue = Evaluate(BaseValue + Modifier)
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
    ///   · 不接入 Region（`Modifier` 恒为 0，属 `PC20` 的 Phase 3 供给）；
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
        public static WorldParameterState Evaluate(WorldSeed seed, WorldCoordinate position)
        {
            int capacity = WorldParameterState.Capacity;

            // 定长索引容器（索引 = ParameterId；槽 0 不使用，便于按身份直读）：PM37 / PM45 / PM46。
            float[] baseValues = new float[capacity + 1];
            bool[] evaluated = new bool[capacity + 1];
            WorldParameterCompositionKind[] compositionKinds = new WorldParameterCompositionKind[capacity + 1];

            IWorldParameterBaseReader reader = new EvaluatedBaseReader(baseValues, evaluated);

            // 阶段 A：按冻结求值顺序逐参数求 BaseValue（WSC §2.3 步骤 0–4）。
            // 规则只能读到"已完成步骤"的值 ⇒ 顺序无环（WSC16）。
            for (int i = 0; i < WorldParameterRules.Count; i++)
            {
                IWorldParameterRule rule = WorldParameterRules.GetRule(i);
                int parameterId = rule.ParameterId;

                float baseValue = rule.EvaluateBaseValue(reader, seed, position);

                baseValues[parameterId] = baseValue;
                evaluated[parameterId] = true;
                compositionKinds[parameterId] = rule.CompositionKind;
            }

            // 阶段 B：按**参数身份顺序**装配三态快照（槽位 = ParameterId - 1；与容器索引语义一致）。
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
                float modifier = ZeroRegionModifier;
                float finalValue = Compose(compositionKinds[parameterId], baseValue, modifier);

                values[slot] = new WorldParameterValue(parameterId, baseValue, modifier, finalValue);
            }

            // 快照不可变定稿（PM34）；容器自行防御性拷贝。
            return new WorldParameterState(values);
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
