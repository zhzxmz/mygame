using System.Globalization;

namespace World.V2
{
    /// <summary>
    /// **参数查询入口**（World V2 · Plan Phase 1.5 · 入口层，**不含任何求值逻辑**）。
    ///
    /// 职责（唯一）：
    ///   · 暴露单点参数查询的入口签名 `Seed + WorldCoordinate → WorldParameterState`；
    ///   · 把请求**原样转发**给 WorldState 层的求值方 <see cref="WorldParameterEvaluator"/>。
    ///
    /// 明确**不做**：
    ///   · **不生成任何参数值**：本类型不包含哈希、混合、派生、钳制或任何取值逻辑（本回合起全部由
    ///     `WorldParameterEvaluator` + `WorldParameterRules` 承担）；
    ///   · 不做缓存、不做解释、不做修正合成、不持有状态；
    ///   · 不读取 Chunk / Mesh / Runtime / Entity / Player / Scene（`WSC19` / `PZ7`–`PZ8`）；
    ///   · 不引用 UnityEngine。
    ///
    /// 契约依据：
    ///   · `DR35` / `WSC103`：**禁止第二套求值路径**——查询只能走求值方的唯一入口；
    ///   · `WSC67`：查询参数时必须使用契约的求值路径（同一规则）；
    ///   · `WQ1` / `WQ2`：查询与生成同源规则、只读、不触发重算；
    ///   · `PM51`：查询只以世界坐标（外加 `Seed`）为自变量；`PM52`：同坐标 + 同 `Seed` ⇒ 同结果（D5）；
    ///   · `PM53`：纯函数、无副作用。
    ///
    /// 参数身份范围（`FirstEnvironmentParameterId`..`LastEnvironmentParameterId`）是对
    /// `WorldParameter_Contract.md` 附录 B 序 1–8 的**只读引用**，见 <see cref="WorldParameterIds"/>。
    /// </summary>
    public static class WorldParameterQuery
    {
        /// <summary>环境参数组首个身份（含）＝ <see cref="WorldParameterIds.Temperature"/> = 1。</summary>
        public const int FirstEnvironmentParameterId = WorldParameterIds.First;

        /// <summary>环境参数组末个身份（含）＝ <see cref="WorldParameterIds.Ocean"/> = 8。</summary>
        public const int LastEnvironmentParameterId = WorldParameterIds.Last;

        /// <summary>
        /// 单点参数查询（唯一入口）：转发给 <see cref="WorldParameterEvaluator"/>。
        /// 同 `Seed` + 同 <see cref="WorldCoordinate"/> ⇒ 逐字段相同（`D5` / `PM52`）；
        /// 不要求任何 Chunk 已加载 / 世界实例存在（`PM54` / `PC77`）。
        /// </summary>
        public static WorldParameterState Evaluate(WorldSeed seed, WorldCoordinate position)
        {
            return WorldParameterEvaluator.Evaluate(seed, position);
        }

        /// <summary>调试用文本（与区域设置无关；不参与任何计算、不读取任何求值结果）。</summary>
        public static string Describe(WorldSeed seed, WorldCoordinate position)
        {
            return "WorldParameterQuery{ seed=" + seed.ToString() +
                   ", position=" + position.ToString() +
                   ", parameterIds=" + FirstEnvironmentParameterId.ToString(CultureInfo.InvariantCulture) +
                   ".." + LastEnvironmentParameterId.ToString(CultureInfo.InvariantCulture) +
                   ", evaluation=" + "WorldParameterEvaluator" +
                   " }";
        }
    }
}
