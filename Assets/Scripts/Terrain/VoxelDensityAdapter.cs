using System;
using UnityEngine;

namespace TerrainModificationV2
{
    /// <summary>
    /// VoxelField → DensityField 转换 Adapter。
    ///
    /// 映射规则：cell-centered sampling。
    ///   - 每个 Voxel 对应一个 Density Sample，Sample 位于该 Voxel 的中心。
    ///   - Solid Voxel → Density = +1
    ///   - Air Voxel    → Density = -1
    ///
    /// 为什么使用 Voxel Center Sample：
    ///   当前 TerrainMeshGenerator 把 Density Sample 当作 MC Cell 的角点。
    ///   如果 Sample 放在 Voxel 边界，相邻 Voxel 会共享角点，无法表达
    ///   Solid|Air|Solid 的明确边界。
    ///   把 Sample 放在 Voxel 中心后，两个相邻 Voxel 的中心 Sample 构成一个
    ///   MC Cell 的一条边；+1 / -1 等距插值时，零等值面精确落在共享 Voxel 面上。
    ///
    /// 因此：
    ///   Voxel Cell 与 MC Cell 不要求一一对应；
    ///   MC Cell 是两个相邻 Voxel Center 之间的插值单元。
    ///
    /// 本类不负责 Mining / Mesh / Chunk / Dirty / Collider。
    /// </summary>
    public sealed class VoxelDensityAdapter
    {
        /// <summary>Solid Voxel 对应的实验 Density。</summary>
        public const float SolidDensity = 1f;

        /// <summary>Air Voxel 对应的实验 Density。</summary>
        public const float AirDensity = -1f;

        /// <summary>
        /// 将指定空间范围的 VoxelField 转换为规则 DensityField。
        ///
        /// 要求：
        ///   size 每个轴必须等于 resolution × VoxelField.VoxelSize。
        ///
        /// DensityField 内部：
        ///   Origin = origin + (0.5,0.5,0.5) × VoxelSize
        ///   Size   = resolution × VoxelSize
        ///   因此 Sample i 位于 Voxel 中心：
        ///       origin + (i + 0.5) × VoxelSize
        /// </summary>
        public DensityField Build(
            VoxelField voxelField,
            Vector3 origin,
            Vector3 size,
            int resolution)
        {
            if (voxelField == null)
            {
                throw new ArgumentNullException(nameof(voxelField));
            }

            if (resolution <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(resolution),
                    resolution,
                    "Resolution must be a positive integer."
                );
            }

            float voxelSize = voxelField.VoxelSize;

            Vector3 expectedSize = new Vector3(
                resolution * voxelSize,
                resolution * voxelSize,
                resolution * voxelSize
            );

            if (Mathf.Abs(size.x - expectedSize.x) > 0.00001f ||
                Mathf.Abs(size.y - expectedSize.y) > 0.00001f ||
                Mathf.Abs(size.z - expectedSize.z) > 0.00001f)
            {
                throw new ArgumentException(
                    "size must equal resolution × VoxelSize on every axis.",
                    nameof(size)
                );
            }

            // Sample 网格整体向 Voxel 中心偏移半个 Voxel。
            Vector3 densityOrigin = origin +
                                    Vector3.one * (voxelSize * 0.5f);

            DensityField densityField = new DensityField(
                resolution,
                resolution,
                resolution,
                densityOrigin,
                expectedSize
            );

            for (int x = 0; x < densityField.SampleCountX; x++)
            {
                for (int y = 0; y < densityField.SampleCountY; y++)
                {
                    for (int z = 0; z < densityField.SampleCountZ; z++)
                    {
                        Vector3 samplePosition = densityField.GetLocalPosition(
                            x,
                            y,
                            z
                        );

                        Vector3Int voxelCoordinate =
                            voxelField.PositionToVoxel(samplePosition);

                        densityField[x, y, z] = voxelField.IsAir(voxelCoordinate)
                            ? AirDensity
                            : SolidDensity;
                    }
                }
            }

            return densityField;
        }
    }
}
