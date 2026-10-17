using UnityEngine;

namespace TerrainModificationV2
{
    /// <summary>
    /// Terrain Modification &amp; Mining Specification V2.0 Step 1。
    /// 当前唯一有效 Operation 是 Excavate：挖除一个 Edit Grid Cell。
    /// </summary>
    public enum TerrainEditOperation
    {
        /// <summary>挖除一个 Edit Cell，Cell 内部视为 Air。</summary>
        Excavate
    }

    /// <summary>
    /// Terrain Edit Record 数据事实。
    ///
    /// 只描述：
    ///   谁（EditId）
    ///   什么时候（Sequence）
    ///   做了什么（Excavate）
    ///   哪个 Cell（CellCoordinate）
    ///
    /// CellCoordinate 是 Mining Edit 的 authoritative geometry。
    /// 它不是 Density Sample Index / MC Cell Index / Chunk Coordinate / World Position。
    ///
    /// Record 不负责 Dirty / Rebuild / Mesh / Density / Chunk 查询 / Save-Load /
    /// Transform / Raycast。
    /// </summary>
    public sealed class TerrainEditRecord
    {
        /// <summary>稳定唯一 ID。</summary>
        public long EditId { get; }

        /// <summary>Edit 创建 / 应用顺序。</summary>
        public long Sequence { get; }

        /// <summary>Edit Operation。</summary>
        public TerrainEditOperation Operation { get; }

        /// <summary>被挖除的 Edit Grid Cell 整数坐标。</summary>
        public Vector3Int CellCoordinate { get; }

        public TerrainEditRecord(
            long editId,
            long sequence,
            TerrainEditOperation operation,
            Vector3Int cellCoordinate)
        {
            EditId = editId;
            Sequence = sequence;
            Operation = operation;
            CellCoordinate = cellCoordinate;
        }
    }
}
