using System;
using UnityEngine;

namespace TerrainModificationV2
{
    /// <summary>
    /// Terrain Effective Terrain Query（Step 3A）。
    ///
    /// 只回答：
    ///   一个 Terrain Local Space Position 当前是否位于已 Excavated 的 Edit Cell。
    ///
    /// 查询规则：
    ///   position
    ///     ↓ TerrainEditGrid.PositionToCell
    ///   Vector3Int CellCoordinate
    ///     ↓ 与 EditLayer 中 Excavate Records 比较
    ///   true / false
    ///
    /// 这是 Cell Membership，不是 Density Sample Membership，也不是 Mesh Triangle
    /// Membership。本类完全不接触 DensityField / Mesh / Chunk / Dirty / Rebuild。
    /// </summary>
    public sealed class TerrainEffectiveTerrainQuery
    {
        private readonly TerrainEditGrid editGrid;
        private readonly TerrainEditLayer editLayer;

        public TerrainEffectiveTerrainQuery(
            TerrainEditGrid editGrid,
            TerrainEditLayer editLayer)
        {
            if (editGrid == null)
            {
                throw new ArgumentNullException(nameof(editGrid));
            }

            if (editLayer == null)
            {
                throw new ArgumentNullException(nameof(editLayer));
            }

            this.editGrid = editGrid;
            this.editLayer = editLayer;
        }

        /// <summary>
        /// Terrain Local Position 是否位于任意已 Excavated 的 Edit Cell。
        /// 使用 [min, max) 半开区间语义，由 TerrainEditGrid 统一决定。
        /// 重复 Excavation Record 不会产生额外效果，查询天然幂等。
        /// </summary>
        public bool IsExcavated(Vector3 terrainLocalPosition)
        {
            Vector3Int cellCoordinate = editGrid.PositionToCell(
                terrainLocalPosition
            );

            Bounds cellBounds = editGrid.GetCellBounds(cellCoordinate);
            var edits = editLayer.QueryEdits(cellBounds);

            for (int i = 0; i < edits.Count; i++)
            {
                TerrainEditRecord edit = edits[i];

                if (edit == null) continue;

                if (edit.Operation != TerrainEditOperation.Excavate) continue;

                if (edit.CellCoordinate == cellCoordinate)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
