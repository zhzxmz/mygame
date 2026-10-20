using VoxelWorld.Generation;

namespace World.V2
{
    /// <summary>
    /// 环境参数身份表（World V2 · Plan Phase 1.5 · **只读引用**）。
    ///
    /// 权威：`WorldParameter_Contract.md` **附录 B** 的"序"列（`PM12`：`ParameterId` 分配表的权威是附录 B）。
    ///
    /// 本类型只是附录 B 序 1–8（**环境参数组**）的只读引用：
    ///   · **不新增**参数、**不删除**参数、**不改号**（`PM15` / `PM19` / `PM45`）；
    ///   · 参数集合**编译期固定**（`PM20`）；禁止运行时动态注册/增删（`PM45`）；
    ///   · 未登记的 `ParameterId` 一律**拒绝**，不得静默扩展（`PM19` / `PM46`）；
    ///   · 名称仅作人类可读别名，任何访问路径必须经 `ParameterId`（`PM13` / `PM14`）。
    ///
    /// 一致性约束：`WorldParameterState.Capacity` 必须等于本表 <see cref="Count"/>，
    /// 由 <see cref="WorldParameterEvaluator"/> 的静态自检保证（不一致即抛错，不静默容忍）。
    ///
    /// 约束：不使用 UnityEngine；不读取任何运行时状态（`PM21`）。
    /// </summary>
    public static class WorldParameterIds
    {
        /// <summary>环境参数组登记数量（附录 B 序 1–8）。</summary>
        public const int Count = 8;

        /// <summary>附录 B 序 1：归一化标量 `[0,1]`，Owner = WorldState。</summary>
        public const int Temperature = 1;

        /// <summary>附录 B 序 2：归一化标量 `[0,1]`，Owner = WorldState。</summary>
        public const int Moisture = 2;

        /// <summary>附录 B 序 3：整数（voxel, y 轴），转发量（Owner = 高度场）。</summary>
        public const int Elevation = 3;

        /// <summary>附录 B 序 4：归一化标量 `[0,1]`，Owner = WorldState。</summary>
        public const int Fertility = 4;

        /// <summary>附录 B 序 5：归一化标量 `[0,1]`，Owner = WorldState。</summary>
        public const int MagicDensity = 5;

        /// <summary>附录 B 序 6：归一化标量 `[0,1]`，Owner = WorldState。</summary>
        public const int HolyInfluence = 6;

        /// <summary>附录 B 序 7：归一化标量 `[0,1]`，Owner = WorldState。</summary>
        public const int CorruptionInfluence = 7;

        /// <summary>附录 B 序 8：布尔 `{false, true}`，派生量（`Elevation &lt; SeaLevel`）。</summary>
        public const int Ocean = 8;

        /// <summary>环境参数组首个身份（= <see cref="Temperature"/>）。</summary>
        public const int First = Temperature;

        /// <summary>环境参数组末个身份（= <see cref="Ocean"/>）。</summary>
        public const int Last = Ocean;
    }

    /// <summary>
    /// 逐参数的**合成类型**（World V2 · Plan Phase 1.5）。
    ///
    /// 权威：`WorldState_Runtime_Contract.md` §2.2「逐参数的合成类型（冻结）」与 §2.3「求值内部顺序」。
    /// 三种取值与契约一一对应，**不得**在此新增第三种合成方式（`WSC9`：合成只允许加法 + 钳制）。
    /// </summary>
    public enum WorldParameterCompositionKind
    {
        /// <summary>
        /// `Base + Modifier`，随后 `clamp01`（`WSC9` / `WSC12`）。
        /// 适用：`Temperature` / `Moisture` / `Fertility` / `MagicDensity` / `HolyInfluence` / `CorruptionInfluence`（契约 §2.2）。
        /// </summary>
        AdditiveClamped = 0,

        /// <summary>
        /// **无合成**：只读转发既有来源，不参与任何修正求和式（`WSC §2.2` / `WSC10`）。
        /// 适用：`Elevation`（只读转发高度场；Region 明令不得修改，`RE1` / `RA69`）。
        /// </summary>
        Forwarded = 1,

        /// <summary>
        /// **无合成**：由其它参数派生，不参与任何修正求和式（`WSC §2.2` / `WSC10`）。
        /// 适用：`Ocean`（`Elevation &lt; SeaLevel`；Region 明令不得修改，`RE1` / `RA69`）。
        /// </summary>
        Derived = 2
    }

    /// <summary>
    /// **已求值 Base 值的只读视图**（World V2 · Plan Phase 1.5）。
    ///
    /// 语义（架构性约束，不是实现细节）：
    ///   · 只暴露**求值顺序中已完成步骤**的 Base 值；步骤尚未到达时返回 `false`；
    ///   · 因此规则在**结构上不可能**读取后续步骤的结果 ⇒ 求值顺序天然**无环**（`WSC16`）；
    ///   · 越界 `parameterId` 必须**拒绝**（抛错），不得静默扩展（`PM19` / `PM46`）；
    ///   · 只读：不提供任何写入入口（`PM39`）。
    /// </summary>
    public interface IWorldParameterBaseReader
    {
        /// <summary>读取已完成步骤的 Base 值；未求值返回 `false`（不得读取后续步骤，`WSC16`）。</summary>
        bool TryGetBaseValue(int parameterId, out float baseValue);
    }

    /// <summary>
    /// **单参数求值规则**（World V2 · Plan Phase 1.5 · 求值架构的接入面）。
    ///
    /// 这是本层唯一允许的扩展点：未来真实求值（骨架气候带、高度场转发、Region 修正等）以
    /// **实现本接口的规则**替换现有占位规则，**不得**新增第二套求值路径（`DR35` / `WSC103`）。
    ///
    /// 规则的实现约束（全部来自已冻结契约）：
    ///   · 必须是 `(Seed, Domain, Position)` 的**纯函数**：同输入同输出、无副作用、不写任何状态（`WSC23` / `PZ1`–`PZ2`）；
    ///   · **不得**读取 Biome / 生态 / 资源 / Geology / 体素 / Chunk / Runtime / Player / Entity（`WSC19` / `PZ7` / `PC21`）；
    ///   · **不得**使用 `Random` / `Time` / `DateTime` / `Guid` / 任何有状态伪随机源（`PZ5` / `PZ6`）；
    ///   · 确定性来源必须是"以 `(seed, domain, 世界坐标)` 为输入的自实现整数哈希"（`PZ11`），
    ///     且**不得**复用保留域 `R-1`/`R-2`（`HeightFunction` / `GeologyNoise`）的混合方式（`PZ12`）；
    ///   · 只能读取 <see cref="EvaluationStep"/> **之前**步骤的结果（`WSC16`；由 `IWorldParameterBaseReader` 在结构上保证）。
    /// </summary>
    public interface IWorldParameterRule
    {
        /// <summary>本规则负责的参数身份（附录 B 序，见 <see cref="WorldParameterIds"/>）。</summary>
        int ParameterId { get; }

        /// <summary>
        /// 本规则在**冻结求值顺序**中的步骤号（`WSC §2.3` 步骤 0–4；步骤 5–7 属 Region/合成，不在此处）。
        /// 规则表按步骤号非递减排列，并由 <see cref="WorldParameterEvaluator"/> 静态自检。
        /// </summary>
        int EvaluationStep { get; }

        /// <summary>逐参数合成类型（`WSC §2.2`，见 <see cref="WorldParameterCompositionKind"/>）。</summary>
        WorldParameterCompositionKind CompositionKind { get; }

        /// <summary>
        /// 求该点的 **BaseValue**（不含任何 Region 修正；`PM27` / `WSC21`）。
        /// 只允许读取 <paramref name="evaluated"/> 中已完成步骤的 Base 值。
        /// </summary>
        float EvaluateBaseValue(IWorldParameterBaseReader evaluated, WorldSeed seed, WorldCoordinate position, WorldGenerationSettings settings);
    }
}
