using VoxelWorld.Generation;

namespace World.V2
{
    /// <summary>
    /// **WorldSkeleton V1 单大陆退化查询结果 + 最小查询入口**（World V2 · Phase 3.2 · 纯数据 + 纯函数）。
    ///
    /// 职责（唯一）：给定 `(世界坐标, 世界生成配置, 海平面)`，产出该坐标的 3 个 V1 骨架事实：
    ///   · <see cref="Elevation"/> —— 该**列**的地表高度（既有高度场只读转发；与 `Y` 无关）；
    ///   · <see cref="Ocean"/>     —— 是否海洋（`Elevation` 严格小于海平面）；
    ///   · <see cref="Continent"/> —— 大陆编号（海洋 = `0`；单大陆退化模式陆地 = `1`）。
    ///
    /// 契约依据：
    ///   · `WorldSkeleton_V1_Minimal_Contract.md`：`F-1`/`F-2`/`F-3`（事实集合）、`Q-4`（垂直范围，`D1`）、
    ///     `Q-8`（失败与结果分离，`D2`）、`Q-10`（大陆编号语义，`D3`）；
    ///   · `WorldState_Design.md` `WC3`（`Ocean` 由 `Elevation` 与海平面派生，不得用独立噪声）；
    ///   · `WorldParameter_Contract.md` `Elevation` 行（来源 = 既有高度场）、`PS6`（整数参数不得插值）。
    ///
    /// ────────────────────────────────────────────────────────────────
    /// 【1】`Elevation` 的**唯一来源**是既有高度场：直接只读调用
    ///       `HeightFunction.GetTerrainHeight(position.X, position.Z, settings)`。
    ///       本类型**不复制、不重写、不修改**该算法，也不引用任何参数域哈希。
    /// 【2】**失败语义**（`D1`/`D2`）：垂直坐标超出 `[0, settings.WorldHeight)` ⇒
    ///       <see cref="TryEvaluate"/> 返回 `false` 且 `facts` 为 `default`（`IsValid == false`）；
    ///       **不钳制**坐标、**不**用合法默认值冒充成功。`Continent = 0`（海洋）是**合法结果**，与失败无关。
    /// 【3】**前置条件由调用方保证**（本类型不校验、不修正、不钳制）：
    ///       · `settings` 合法（见 `WorldGenerationSettings.IsValid`）；
    ///       · 海平面落在 `[0, WorldHeight]`（`WorldParameter_Contract.md` §5.2 冻结范围）。
    /// 【4】**有效实例的构造权限**：构造函数为 `private`。C# 中 `private` 的可见范围是
    ///       "本类型及其嵌套类型"，因此**只有本类型内部**能构造有效实例；同时本实现把
    ///       `new WorldSkeletonFacts(...)` 的调用点**显式集中**在 <see cref="TryEvaluate"/> 内的
    ///       **唯一一处**。`private` 只是语言层限制，"只有一个调用点"是**本实现的书面约定**，
    ///       不是编译器保证。
    /// ────────────────────────────────────────────────────────────────
    ///
    /// 技术约束：纯 C# 数据承载 + 纯函数查询；不使用 `UnityEngine`；不读取时间/帧序/随机/全局状态；
    /// 无缓存、无副作用、无注册表/服务/Builder；`default(WorldSkeletonFacts).IsValid == false`。
    /// </summary>
    public readonly struct WorldSkeletonFacts
    {
        private readonly bool isValid;
        private readonly int elevation;
        private readonly bool ocean;
        private readonly int continent;

        /// <summary>
        /// 有效实例的**唯一构造函数**（`private`）：只允许在本类型内部调用。
        /// 本实现的唯一调用点是 <see cref="TryEvaluate"/>（该处有显式标记）。
        /// 三个取值必须同时给出 ⇒ 结构上不存在"部分成功"的结果。
        /// </summary>
        private WorldSkeletonFacts(int elevation, bool ocean, int continent)
        {
            this.isValid = true;
            this.elevation = elevation;
            this.ocean = ocean;
            this.continent = continent;
        }

        /// <summary>
        /// 该实例是否为**完整有效**的查询结果。`default(WorldSkeletonFacts).IsValid == false`
        /// （有效位只由 <see cref="TryEvaluate"/> 内的唯一构造点置位）。
        /// </summary>
        public bool IsValid
        {
            get { return isValid; }
        }

        /// <summary>该**列**的地表高度（体素 y；值域 `[0, WorldHeight-1]`；与查询坐标的 `Y` 无关）。</summary>
        public int Elevation
        {
            get { return elevation; }
        }

        /// <summary>是否海洋：`Elevation` **严格小于**海平面时为 `true`（`WC3`）。</summary>
        public bool Ocean
        {
            get { return ocean; }
        }

        /// <summary>大陆编号：海洋 = `0`（**合法结果，不是失败**）；单大陆退化模式陆地 = `1`（`D3`）。</summary>
        public int Continent
        {
            get { return continent; }
        }

        /// <summary>
        /// V1 最小查询入口（纯函数，只读）。
        ///
        /// 成功：`true` + 完整有效结果（`IsValid == true`）。
        /// 失败：`false` + `default`（`IsValid == false`）；唯一的失败条件是垂直坐标越界（`D1`）。
        /// 本方法**不**校验 `settings` 合法性与海平面范围（属调用方前置条件），
        /// **不**钳制坐标，**不**引入失败原因通道。
        /// </summary>
        public static bool TryEvaluate(
            WorldCoordinate position,
            WorldGenerationSettings settings,
            int seaLevel,
            out WorldSkeletonFacts facts)
        {
            // `D1`（垂直范围，已批准）：`0 <= Y < WorldHeight` 之外一律**明确失败**；不钳制、不修正。
            if (position.Y < 0 || position.Y >= settings.WorldHeight)
            {
                facts = default;
                return false;
            }

            // `F-1`：`Elevation` 直接只读转发既有高度场（列级量，与 `Y` 无关）；不复制其算法。
            int elevation = HeightFunction.GetTerrainHeight(position.X, position.Z, settings);

            // `F-2` / `WC3`：`Ocean` 由 `Elevation` 严格小于海平面派生（不得使用独立噪声）。
            bool ocean = elevation < seaLevel;

            // `F-3` / `D3`：海洋 = 0；单大陆退化模式陆地 = 1。
            int continent = ocean ? 0 : 1;

            // ===【有效实例的唯一构造点】本文件中 `new WorldSkeletonFacts(...)` 仅此一处 ===
            facts = new WorldSkeletonFacts(elevation, ocean, continent);
            return true;
        }
    }
}
