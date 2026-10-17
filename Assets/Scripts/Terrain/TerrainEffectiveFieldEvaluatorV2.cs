using System.Collections.Generic;
using UnityEngine;

namespace TerrainModificationV2
{
    /// <summary>
    /// Terrain Effective Density Evaluator。
    ///
    /// Step 1 数据模型迁移期：
    /// - TerrainEditRecord 已经改为 Excavate + CellCoordinate。
    /// - Effective Terrain 的 Cell Occupancy 语义需要 TerrainEditGrid 与
    ///   Surface Sampling Policy，属于后续 Step。
    ///
    /// 因此当前 Evaluator 暂时原样返回 baseDensity。
    /// 本文件只保证 Step 1 数据模型迁移后项目可编译；
    /// 后续 Step 会替换为真正的：
    ///     if position ∈ excavated cell → Air
    ///     else → baseDensity
    ///
    /// Evaluator 不修改 Base Density / Edit Record / Edit Layer / Chunk / Mesh。
    /// </summary>
    public static class TerrainEffectiveFieldEvaluator
    {
        /// <summary>
        /// Step 1 deferred implementation：直接返回 baseDensity。
        /// </summary>
        public static float Evaluate(
            float baseDensity,
            Vector3 samplePosition,
            IReadOnlyList<TerrainEditRecord> edits)
        {
            return baseDensity;
        }
    }
}
