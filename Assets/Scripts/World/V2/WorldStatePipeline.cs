using VoxelWorld.Generation;

namespace World.V2
{
    /// <summary>
    /// **WorldState 两段式发布编排**（World V2 · Phase 3.0 · 阶段连接层）。
    ///
    /// 职责（唯一）：把契约规定的**阶段顺序**变成可执行的调用链：
    /// <code>
    ///   Phase 2  Base 段：WorldParameterEvaluator.Evaluate(seed, position)          → IsFinalized = false
    ///   Phase 3  Region：RegionPipeline.ResolveContext → RegionModifierDetermination.Determine → RegionModifier
    ///   Phase 2' Final 段：WorldParameterEvaluator.Finalize(baseState, regionModifier) → IsFinalized = true
    /// </code>
    ///
    /// 契约依据：
    ///   · `PC19` **两段式发布**：① `Base` 段在 Phase 2 可定稿；② `FinalState = clamp01(Base + RegionModifier)`
    ///     必须在 **Phase 3 完成之后**定稿；
    ///   · `PC20`：`RegionModifier` 的**唯一写入者**是 Region 层，WorldState 侧**只做合成**（`RA76`）；
    ///   · `PC21`：下游只能读**已定稿**的 `FinalState`，不得读未合成的 `Base` 代替它，也不得读 Region 内部字段；
    ///   · `WSC21`：`Base` 必须能在不计算任何 Region 的前提下求值（因此 `EvaluateBase` 不调用 Region）；
    ///   · `WSC22`：关闭 Region 层（修正量全零）⇒ `FinalState` 与 `Base` **逐分量、逐值相等**；
    ///   · `WSC103` / `DR35`：**不得**存在第二套求值/合成路径 —— 本类型不含任何参数计算，
    ///     只按顺序调用既有入口（合成权威仍是 `WorldParameterEvaluator.Compose`）。
    ///
    /// 【占位登记】当前 `RegionModifierDetermination` 为 Phase 3.0 的**占位实现**（区间 `[0, 0.25)`），
    /// 因此 `EvaluateFinal` 给出的 `FinalValue` 在**结构上**是定稿值，在**数据上**仍是占位数据
    /// （`Region_Architecture_Contract.md` 与 `Region_Contribution_Decision_Record.md` 已登记该状态）。
    /// 替换占位实现后本类型无需改动（调用链不变）。
    ///
    /// 明确**不做**：
    ///   · 不计算任何参数值、不产生修正量、不做钳制（全部属被调用的既有入口）；
    ///   · 不缓存、不持有状态、无静态可变字段；不读取 Chunk / Scene / Runtime / Player；
    ///   · 不使用 UnityEngine、`Random`、`Time`（`PZ5` / `PZ6`）。
    ///
    /// 确定性（`D5` / `PM52`）：纯函数；同 `seed` + 同位置 ⇒ 逐字段相同的 Base 段与 Final 段。
    /// </summary>
    public static class WorldStatePipeline
    {
        /// <summary>
        /// **Phase 2（第一段）**：只发布 Base 段（`IsFinalized == false`）。
        /// 不触发任何 Region 计算（`WSC21`：Base 必须能在不计算 Region 的前提下求值）。
        /// </summary>
        public static WorldParameterState EvaluateBase(WorldSeed seed, WorldGenerationSettings settings, WorldCoordinate position)
        {
            return WorldParameterEvaluator.Evaluate(seed, settings, position);
        }

        /// <summary>
        /// **完整两段式发布**：Base 段 → Region 修正量 → Final 段（`IsFinalized == true`）。
        ///
        /// 顺序即契约（`PC19` / `PC20` / `PC21`）：
        ///   ① `WorldParameterEvaluator.Evaluate`（Base）
        ///   ② `RegionPipeline.ResolveContext` + `RegionModifierDetermination.Determine`（Region → 修正量）
        ///   ③ `WorldParameterEvaluator.Finalize`（合成，唯一权威）
        ///
        /// 纯函数、无副作用；不缓存、不写任何状态。
        /// </summary>
        public static WorldParameterState EvaluateFinal(WorldSeed seed, WorldGenerationSettings settings, WorldCoordinate position)
        {
            WorldParameterState baseState = WorldParameterEvaluator.Evaluate(seed, settings, position);

            RegionContext context = RegionPipeline.ResolveContext(seed, position);
            RegionModifier regionModifier = RegionModifierDetermination.Determine(context);

            return WorldParameterEvaluator.Finalize(baseState, regionModifier);
        }
    }
}
