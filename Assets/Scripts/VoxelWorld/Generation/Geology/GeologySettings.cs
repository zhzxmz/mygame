using System;

namespace VoxelWorld.Generation.Geology
{
    /// <summary>
    /// 地质生成参数（Geology Stage A；WorldGeneration_Geology_Implementation.md §9.1）。
    ///
    /// 硬性要求（本文档 §9 与规范 §12.1）：
    ///   · **纯 C# 数据结构**：不引用 UnityEngine、不使用任何 Unity 类型；
    ///   · **不含 Chunk 坐标**：参数与世界规则相关，与"加载/采样哪个 Chunk"无关；
    ///   · **不含随机状态**：没有任何 RNG 状态；随机性只来自显式 `Seed`（由
    ///     `WorldGenerationSettings.Seed` 提供，见 `GeologyWorldGenerator` 的构造）；
    ///   · **显式**：所有影响世界规则的参数都在这里声明，禁止隐藏全局状态；
    ///   · **不修改既有类型**：`WorldGenerationSettings` 的字段集保持不变（Stage 1 契约）。
    ///
    /// 与 `WorldGenerationSettings` 的分工：
    ///   · `WorldGenerationSettings`：Stage 1 参数（Seed / TerrainHeight / SoilDepth / WorldHeight / 频率 / 振幅）；
    ///   · `GeologySettings`（本类型）：地质层参数（洞穴 / 矿藏 / 群系修正开关）。
    /// </summary>
    [Serializable]
    public sealed class GeologySettings
    {
        /// <summary>洞穴影响的合法阈值范围（与 <see cref="GeologyNoise"/> 的输出范围一致）。</summary>
        public const float MinCaveThreshold = -1f;
        public const float MaxCaveThreshold = 1f;

        /// <summary>矿藏丰度（百分比标量）的合法范围。</summary>
        public const int MinDepositRichness = 0;
        public const int MaxDepositRichness = 100;

        // -----------------------------------------------------------------
        // 参数（全部显式）
        // -----------------------------------------------------------------

        /// <summary>是否启用 P1 洞穴覆盖（关闭 ⇒ 与 Stage 1 等价，见 GIV9）。</summary>
        public bool EnableCaves = true;

        /// <summary>
        /// 洞穴阈值：`CaveDensity ≥ CaveThreshold` ⇒ 该坐标被洞穴覆盖（P1）。
        /// 范围 [-1, 1]（噪声输出范围）；极值分别表示"几乎全覆盖 / 几乎不覆盖"。
        /// </summary>
        public float CaveThreshold = 0f;

        /// <summary>
        /// 洞穴噪声频率，取值集合 = {1,2,4,8,16,32,64}
        /// （格胞边长 = 64 / frequency ∈ {64,32,16,8,4,2,1} 体素，全部为 2 的幂 ⇒ 插值精确）。
        /// </summary>
        public int CaveFrequency = 8;

        /// <summary>是否启用矿藏属性层（关闭 ⇒ 不产生任何矿藏查询结果；不影响体素，见 GIV5）。</summary>
        public bool EnableDeposits = true;

        /// <summary>矿藏噪声频率，取值集合同 <see cref="CaveFrequency"/>。</summary>
        public int DepositFrequency = 8;

        /// <summary>矿藏丰度（0..100 的显式标量；具体用法属 Stage C）。</summary>
        public int DepositRichness = 50;

        /// <summary>
        /// 是否应用 Biome 修正。注意：当没有注入 `IBiomeProvider` 时必须为 false
        /// （该约束由 `GeologyWorldGenerator` 在构造期检查，而不是本类型的纯参数校验）。
        /// </summary>
        public bool EnableBiomeModifiers = false;

        // -----------------------------------------------------------------
        // 默认配置
        // -----------------------------------------------------------------

        /// <summary>
        /// 默认配置（每次返回**独立实例**，修改一个不会影响另一个；
        /// 与 `WorldGenerationSettings.Default` 的语义一致）。
        /// 默认值必须是合法参数（`IsValid` 通过）。
        /// </summary>
        public static GeologySettings Default
        {
            get { return new GeologySettings(); }
        }

        /// <summary>深拷贝（用于"独立实例"语义与验证对照）。</summary>
        public GeologySettings Clone()
        {
            return new GeologySettings
            {
                EnableCaves = EnableCaves,
                CaveThreshold = CaveThreshold,
                CaveFrequency = CaveFrequency,
                EnableDeposits = EnableDeposits,
                DepositFrequency = DepositFrequency,
                DepositRichness = DepositRichness,
                EnableBiomeModifiers = EnableBiomeModifiers
            };
        }

        // -----------------------------------------------------------------
        // 派生量（只读；不改变判定规则）
        // -----------------------------------------------------------------

        /// <summary>洞穴噪声的格胞边长（体素）= 64 / CaveFrequency（频率非法时取 64）。</summary>
        public int CaveCellVoxels
        {
            get { return GeologyNoise.CellVoxelsForFrequency(CaveFrequency); }
        }

        /// <summary>矿藏噪声的格胞边长（体素）= 64 / DepositFrequency（频率非法时取 64）。</summary>
        public int DepositCellVoxels
        {
            get { return GeologyNoise.CellVoxelsForFrequency(DepositFrequency); }
        }

        // -----------------------------------------------------------------
        // 校验
        // -----------------------------------------------------------------

        /// <summary>
        /// 参数合法性验证（启动前校验；非法参数必须被拒绝，禁止运行中静默修正）。
        /// 返回 false 时 <paramref name="reason"/> 给出**第一条**失败原因。
        /// </summary>
        public bool IsValid(out string reason)
        {
            if (!GeologyNoise.IsValidFrequency(CaveFrequency))
            {
                reason = "CaveFrequency 必须属于 {1,2,4,8,16,32,64}（实际 " +
                         CaveFrequency + "）";
                return false;
            }

            if (!GeologyNoise.IsValidFrequency(DepositFrequency))
            {
                reason = "DepositFrequency 必须属于 {1,2,4,8,16,32,64}（实际 " +
                         DepositFrequency + "）";
                return false;
            }

            // 注意：NaN 的所有比较都为 false，因此必须显式检查
            if (float.IsNaN(CaveThreshold))
            {
                reason = "CaveThreshold 不能为 NaN";
                return false;
            }

            if (CaveThreshold < MinCaveThreshold || CaveThreshold > MaxCaveThreshold)
            {
                reason = "CaveThreshold 必须在 [" + MinCaveThreshold + ", " +
                         MaxCaveThreshold + "] 内（实际 " + CaveThreshold + "）";
                return false;
            }

            if (DepositRichness < MinDepositRichness ||
                DepositRichness > MaxDepositRichness)
            {
                reason = "DepositRichness 必须在 [" + MinDepositRichness + ", " +
                         MaxDepositRichness + "] 内（实际 " + DepositRichness + "）";
                return false;
            }

            reason = null;
            return true;
        }

        public override string ToString()
        {
            return "GeologySettings(caves=" + (EnableCaves ? "on" : "off") +
                   ", threshold=" + CaveThreshold +
                   ", caveCell=" + CaveCellVoxels + "vx" +
                   ", deposits=" + (EnableDeposits ? "on" : "off") +
                   ", depositCell=" + DepositCellVoxels + "vx" +
                   ", richness=" + DepositRichness +
                   ", biomeModifiers=" + (EnableBiomeModifiers ? "on" : "off") + ")";
        }
    }
}
