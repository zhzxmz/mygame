using System;
using UnityEngine;

namespace TerrainModificationV2
{
    /// <summary>
    /// Effective Surface Sample 状态。
    /// </summary>
    public enum TerrainEffectiveSampleState
    {
        /// <summary>该位置使用 Base Terrain 状态。</summary>
        Base,

        /// <summary>该位置位于已 Excavated Edit Cell。</summary>
        Excavated
    }

    /// <summary>
    /// 一个 Excavated Edit Cell 要求未来 Surface Sampler 覆盖的采样区域契约。
    /// 坐标全部位于 Terrain Local Space。
    /// </summary>
    public readonly struct TerrainEffectiveSampleRegion
    {
        /// <summary>被 Excavated 的 Edit Cell Coordinate。</summary>
        public Vector3Int EditCellCoordinate { get; }

        /// <summary>采样区域最小角（闭区间）。</summary>
        public Vector3 Min { get; }

        /// <summary>采样区域最大角（闭区间）。</summary>
        public Vector3 Max { get; }

        /// <summary>未来 Surface Sampling Grid 的 spacing。</summary>
        public float Spacing { get; }

        public TerrainEffectiveSampleRegion(
            Vector3Int editCellCoordinate,
            Vector3 min,
            Vector3 max,
            float spacing)
        {
            EditCellCoordinate = editCellCoordinate;
            Min = min;
            Max = max;
            Spacing = spacing;
        }
    }

    /// <summary>
    /// Terrain Effective Surface Sampling Contract（Step 3B）。
    ///
    /// 本类型只定义契约，不执行 Surface Sampling，不修改 DensityField / Mesh。
    ///
    /// 契约核心：
    ///   1. Edit Grid 是 Mining 逻辑 Grid。
    ///   2. Surface Sampling Grid 是未来 Mesh 采样 Grid。
    ///   3. SurfaceCellSize = EditCellSize / SamplesPerEditCell。
    ///     这样两个 Grid 有确定整数关系，Edit Cell 边界永远对齐未来采样边界。
    ///   4. 任意 Terrain Local Position 的 Effective State 通过
    ///      TerrainEffectiveTerrainQuery 决定，与 Density Grid 无关。
    ///   5. 一个 Excavated Cell 的采样区域必须向六个方向扩展一个 SurfaceCell，
    ///      以便 Marching Cubes 在 Cell 每个面两侧都存在 sign change。
    /// </summary>
    public sealed class TerrainEffectiveSurfaceSampling
    {
        /// <summary>Edit Grid 契约。</summary>
        public TerrainEditGrid EditGrid { get; }

        /// <summary>每个 Edit Cell 边被分成多少个 Surface Sampling Cell。</summary>
        public int SamplesPerEditCell { get; }

        /// <summary>未来 Surface Sampling Grid 的 Cell Size。</summary>
        public float SurfaceCellSize
        {
            get { return EditGrid.CellSize / SamplesPerEditCell; }
        }

        private readonly TerrainEffectiveTerrainQuery effectiveTerrainQuery;

        public TerrainEffectiveSurfaceSampling(
            TerrainEditGrid editGrid,
            TerrainEffectiveTerrainQuery effectiveTerrainQuery,
            int samplesPerEditCell = 2)
        {
            if (editGrid == null)
            {
                throw new ArgumentNullException(nameof(editGrid));
            }

            if (effectiveTerrainQuery == null)
            {
                throw new ArgumentNullException(nameof(effectiveTerrainQuery));
            }

            if (samplesPerEditCell <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(samplesPerEditCell),
                    samplesPerEditCell,
                    "SamplesPerEditCell must be a positive integer."
                );
            }

            EditGrid = editGrid;
            SamplesPerEditCell = samplesPerEditCell;
            this.effectiveTerrainQuery = effectiveTerrainQuery;
        }

        /// <summary>
        /// 查询任意 Terrain Local Position 的 Effective Surface State。
        /// 只做 Cell Membership 查询，不接触 Density / Mesh。
        /// </summary>
        public TerrainEffectiveSampleState Sample(
            Vector3 terrainLocalPosition)
        {
            return effectiveTerrainQuery.IsExcavated(terrainLocalPosition)
                ? TerrainEffectiveSampleState.Excavated
                : TerrainEffectiveSampleState.Base;
        }

        /// <summary>
        /// 返回一个 Excavated Edit Cell 要求未来 Surface Sampler 覆盖的采样区域。
        ///
        /// Edit Cell Bounds 向六个方向扩展一个 SurfaceCell：
        ///   min = cellMin - SurfaceCellSize
        ///   max = cellMax + SurfaceCellSize
        ///
        /// 该区域保证未来 Marching Cubes 在每个 Cell 面的内外两侧都有采样机会。
        /// </summary>
        public TerrainEffectiveSampleRegion GetRequiredSamplingRegion(
            Vector3Int editCellCoordinate)
        {
            Vector3 cellMin = EditGrid.GetCellMin(editCellCoordinate);
            Vector3 cellMax = EditGrid.GetCellMax(editCellCoordinate);
            Vector3 padding = Vector3.one * SurfaceCellSize;

            return new TerrainEffectiveSampleRegion(
                editCellCoordinate,
                cellMin - padding,
                cellMax + padding,
                SurfaceCellSize
            );
        }
    }
}
