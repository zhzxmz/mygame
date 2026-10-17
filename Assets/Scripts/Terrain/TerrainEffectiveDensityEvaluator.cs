using System;
using UnityEngine;

namespace TerrainModificationV2
{
    /// <summary>
    /// Terrain Effective Density Evaluator（Step 3D 最小实验模型）。
    ///
    /// 本类型不接触真实 DensityField / Chunk / Mesh / MC。
    /// 它只验证：
    ///   Base Density + Excavated Cell State → Effective Density
    ///
    /// 实验 Base Density：
    ///   BaseDensity(p) = 0.5 - p.y
    ///
    /// 实验 Excavated 规则：
    ///   如果 position 所属 Edit Cell 已 Excavated → EffectiveDensity = -1
    ///   否则 → EffectiveDensity = BaseDensity
    ///
    /// -1 只是本实验的 Excavated 常量，不是最终游戏的 Density 规则。
    /// </summary>
    public sealed class TerrainEffectiveDensityEvaluator
    {
        /// <summary>Step 3D 实验用 Excavated Density 常量。</summary>
        public const float ExcavatedDensity = -1f;

        private readonly TerrainEffectiveTerrainQuery effectiveTerrainQuery;

        public TerrainEffectiveDensityEvaluator(
            TerrainEffectiveTerrainQuery effectiveTerrainQuery)
        {
            if (effectiveTerrainQuery == null)
            {
                throw new ArgumentNullException(nameof(effectiveTerrainQuery));
            }

            this.effectiveTerrainQuery = effectiveTerrainQuery;
        }

        /// <summary>
        /// 实验 Base Density：无限水平面 y = 0.5。
        /// Density &gt; 0 → Solid；Density &lt;= 0 → Air。
        /// </summary>
        public float EvaluateBaseDensity(Vector3 terrainLocalPosition)
        {
            return 0.5f - terrainLocalPosition.y;
        }

        /// <summary>
        /// Effective Density：
        ///   IsExcavated(position) == true  → ExcavatedDensity
        ///   否则                            → BaseDensity(position)
        ///
        /// 不使用 min / fallback / nearest / sampling region。
        /// </summary>
        public float Evaluate(Vector3 terrainLocalPosition)
        {
            if (effectiveTerrainQuery.IsExcavated(terrainLocalPosition))
            {
                return ExcavatedDensity;
            }

            return EvaluateBaseDensity(terrainLocalPosition);
        }
    }
}
