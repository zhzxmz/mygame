using System.Collections.Generic;
using UnityEngine;

namespace TerrainModificationV2
{
    /// <summary>
    /// 一个 Effective Surface Sample：
    /// - GridCoordinate：Surface Grid 的整数坐标。
    /// - TerrainLocalPosition：由整数坐标 × SurfaceCellSize 得到，无累计浮点漂移。
    /// - State：通过 TerrainEffectiveTerrainQuery 判断，而非通过 Sampling Region。
    /// </summary>
    public readonly struct TerrainEffectiveSample
    {
        public Vector3Int GridCoordinate { get; }
        public Vector3 TerrainLocalPosition { get; }
        public TerrainEffectiveSampleState State { get; }

        public TerrainEffectiveSample(
            Vector3Int gridCoordinate,
            Vector3 terrainLocalPosition,
            TerrainEffectiveSampleState state)
        {
            GridCoordinate = gridCoordinate;
            TerrainLocalPosition = terrainLocalPosition;
            State = state;
        }
    }

    /// <summary>
    /// Terrain Effective Surface Sample Enumerator（Step 3C）。
    ///
    /// 只负责：
    ///   Edit Cell / Sampling Region → 规则 Surface Sample Grid → State
    ///
    /// Surface Grid 坐标定义：
    ///   SurfaceCellSize = EditCellSize / SamplesPerEditCell
    ///   Surface Grid Origin = Terrain Local (0,0,0)
    ///   position = GridCoordinate × SurfaceCellSize
    ///
    /// 枚举优先使用整数 Grid Coordinate，最后才转换为 Vector3。
    /// 不执行 Density / Mesh / Chunk / MC / Mining / Raycast。
    /// </summary>
    public sealed class TerrainEffectiveSurfaceSampleEnumerator
    {
        private readonly TerrainEffectiveSurfaceSampling surfaceSampling;

        public TerrainEffectiveSurfaceSampleEnumerator(
            TerrainEffectiveSurfaceSampling surfaceSampling)
        {
            if (surfaceSampling == null)
            {
                throw new System.ArgumentNullException(nameof(surfaceSampling));
            }

            this.surfaceSampling = surfaceSampling;
        }

        /// <summary>
        /// 枚举一个 Edit Cell 要求的 Sampling Region 内的全部 Surface Samples。
        /// </summary>
        public List<TerrainEffectiveSample> Enumerate(
            Vector3Int editCellCoordinate)
        {
            return Enumerate(
                surfaceSampling.GetRequiredSamplingRegion(editCellCoordinate)
            );
        }

        /// <summary>
        /// 枚举指定 Sampling Region 内的全部 Surface Samples。
        /// Region 只是候选采样区域；每个 Sample 的 State 仍由
        /// TerrainEffectiveTerrainQuery.IsExcavated() 决定。
        /// </summary>
        public List<TerrainEffectiveSample> Enumerate(
            TerrainEffectiveSampleRegion region)
        {
            List<TerrainEffectiveSample> samples =
                new List<TerrainEffectiveSample>();

            float spacing = region.Spacing;
            if (spacing <= 0f) return samples;

            int minX = Mathf.CeilToInt(region.Min.x / spacing);
            int maxX = Mathf.FloorToInt(region.Max.x / spacing);

            int minY = Mathf.CeilToInt(region.Min.y / spacing);
            int maxY = Mathf.FloorToInt(region.Max.y / spacing);

            int minZ = Mathf.CeilToInt(region.Min.z / spacing);
            int maxZ = Mathf.FloorToInt(region.Max.z / spacing);

            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    for (int z = minZ; z <= maxZ; z++)
                    {
                        Vector3Int gridCoordinate =
                            new Vector3Int(x, y, z);

                        // 每个轴由整数 × spacing 独立计算，不累积浮点误差。
                        Vector3 terrainLocalPosition = new Vector3(
                            x * spacing,
                            y * spacing,
                            z * spacing
                        );

                        TerrainEffectiveSample sample =
                            new TerrainEffectiveSample(
                                gridCoordinate,
                                terrainLocalPosition,
                                surfaceSampling.Sample(terrainLocalPosition)
                            );

                        samples.Add(sample);
                    }
                }
            }

            return samples;
        }
    }
}
