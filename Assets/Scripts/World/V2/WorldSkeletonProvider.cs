using VoxelWorld.Generation;

namespace World.V2
{
    /// <summary>
    /// **WorldSkeleton 事实查询适配层**（World V2 · R42 · 只读适配，不含任何世界规则）。
    ///
    /// 职责（唯一）：
    ///   · 接收世界坐标 + 当前有效的 <see cref="WorldGenerationSettings"/>，转发给 R40 已统一
    ///     海平面来源的 <see cref="WorldSkeletonFacts.TryEvaluate"/>；
    ///   · 成功时原样返回该坐标的 <see cref="WorldSkeletonFacts"/>，调用方可直接读取
    ///     <see cref="WorldSkeletonFacts.Elevation"/> / <see cref="WorldSkeletonFacts.Ocean"/> /
    ///     <see cref="WorldSkeletonFacts.Continent"/>；
    ///   · 失败时**明确**返回 `false` 并把 `facts` 置为 `default`（`IsValid == false`）：
    ///     **不伪造**任何有效事实，也**不**返回部分结果。
    ///
    /// 明确**不做**（与 <see cref="WorldSkeletonFacts"/> 的职责边界完全一致）：
    ///   · 不创建、不修改、不校验 <see cref="WorldGenerationSettings"/>
    ///     （合法性属调用方前置条件，`IF-8`；本层不新增任何范围检查或钳制）；
    ///   · **不**重新实现 `Ocean`（严格小于）或 `Continent`（`Ocean ? 0 : 1`）判定（`IF-6`/`IF-7`）；
    ///   · **不**改动 `Elevation` 的来源（仍由 `HeightFunction.GetTerrainHeight` 提供，`IF-3`）；
    ///   · **不**接收独立的 `seaLevel` 参数 —— 海平面阈值唯一来源是 `settings.SeaLevel`（R40 统一来源）；
    ///   · 不生成区块 / 网格 / 地形 / 群系，不生成 Region / Magic / Resource / Ecology / WorldEvent；
    ///   · 不缓存、不持有状态、无静态可变字段、无注册表 / 依赖注入 / 服务定位；
    ///   · 不引用 UnityEngine，不读取时间 / 帧序 / 随机 / 场景 / Player（`PZ5` / `PZ6`）。
    ///
    /// 为什么需要这一层（而不是让调用方各自调 `TryEvaluate`）：
    ///   未来接入生成流程的调用方以**整数三元组** `(worldX, worldY, worldZ)` 工作
    ///   （见 `GeologySampler.Sample` 等既有入口），而 V2 的空间输入类型是
    ///   <see cref="WorldCoordinate"/>；本层是该**类型边界**上的唯一适配点，
    ///   使生成侧无需自行拼装坐标值，也使 V2 事实查询在生产侧只暴露一个入口。
    ///   本层**不引入**任何新的世界规则、抽象注册或依赖注入框架。
    ///
    /// 契约依据：`WorldSkeleton_V1_Minimal_Contract.md` `IF-1`（入口形状）、`IF-4`/`D1`（垂直范围）、
    /// `IF-5`/`D2`（成功/失败可区分、无部分结果）、`IF-6`/`WC3`（`Ocean` 严格小于 `settings.SeaLevel`）、
    /// `IF-7`/`D3`（`Continent` 映射）、`IF-8`（前置条件由调用方保证）。
    ///
    /// 现状（R42）：**尚未接入任何正式生成流程**，无生产调用者；本轮仅建立适配层与验证能力。
    /// </summary>
    public static class WorldSkeletonProvider
    {
        /// <summary>
        /// 查询某世界坐标的骨架事实（**唯一适配入口**；纯转发，零规则）。
        ///
        /// 成功 ⇒ `true` + 完整有效事实（`IsValid == true`）；
        /// 失败 ⇒ `false` + `default`（`IsValid == false`，三字段为默认值）。
        /// 唯一的失败条件是垂直坐标越界（`0 &lt;= position.Y &lt; settings.WorldHeight`）：
        /// **不钳制、不修正、不产生失败原因通道**（`IF-4` / `IF-5` / `IF-8`）。
        ///
        /// 海平面阈值只来自 `settings.SeaLevel`，本层与入口**都不接收**独立 `seaLevel` 参数。
        /// </summary>
        public static bool TryGetFacts(
            WorldCoordinate position,
            WorldGenerationSettings settings,
            out WorldSkeletonFacts facts)
        {
            return WorldSkeletonFacts.TryEvaluate(position, settings, out facts);
        }

        /// <summary>
        /// 整数三元组重载：与先构造 <see cref="WorldCoordinate"/> 再调用上面的重载**完全等价**
        /// （仅做类型适配，**不复制**任何判定逻辑）。
        /// </summary>
        public static bool TryGetFacts(
            int worldX,
            int worldY,
            int worldZ,
            WorldGenerationSettings settings,
            out WorldSkeletonFacts facts)
        {
            return WorldSkeletonFacts.TryEvaluate(
                new WorldCoordinate(worldX, worldY, worldZ), settings, out facts);
        }
    }
}