using System;
using UnityEngine;

namespace TerrainModificationV2
{
    /// <summary>
    /// Terrain Edit Grid。
    ///
    /// 纯数学组件，只负责：
    ///   Terrain Local Position ↔ Edit Cell Coordinate / Cell Bounds
    ///
    /// Edit Grid Origin 固定为 Terrain Local Space (0,0,0)。
    /// Cell 使用 half-open interval：
    ///   [min, max)
    ///
    /// 本类不负责 Mining / Raycast / Preview / EditLayer / Density / Mesh /
    /// Chunk / Dirty / Rebuild / Transform / Save-Load。
    /// </summary>
    public sealed class TerrainEditGrid
    {
        /// <summary>单个 Edit Cell 在 Terrain Local Space 中的边长。</summary>
        public float CellSize { get; }

        public TerrainEditGrid(float cellSize)
        {
            if (float.IsNaN(cellSize) || cellSize <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(cellSize),
                    cellSize,
                    "Edit CellSize must be a positive number."
                );
            }

            CellSize = cellSize;
        }

        /// <summary>
        /// Terrain Local Position → Edit Cell Coordinate。
        /// 数学定义：
        ///   cell.x = floor(p.x / CellSize)
        ///   cell.y = floor(p.y / CellSize)
        ///   cell.z = floor(p.z / CellSize)
        /// 使用 floor，不引入 epsilon / normal / rounding / nearest。
        /// </summary>
        public Vector3Int PositionToCell(Vector3 terrainLocalPosition)
        {
            return new Vector3Int(
                Mathf.FloorToInt(terrainLocalPosition.x / CellSize),
                Mathf.FloorToInt(terrainLocalPosition.y / CellSize),
                Mathf.FloorToInt(terrainLocalPosition.z / CellSize)
            );
        }

        /// <summary>Edit Cell Coordinate → Cell 最小角（Terrain Local Space）。</summary>
        public Vector3 GetCellMin(Vector3Int cellCoordinate)
        {
            return new Vector3(
                cellCoordinate.x * CellSize,
                cellCoordinate.y * CellSize,
                cellCoordinate.z * CellSize
            );
        }

        /// <summary>Edit Cell Coordinate → Cell 最大角（Terrain Local Space，开边界）。</summary>
        public Vector3 GetCellMax(Vector3Int cellCoordinate)
        {
            return new Vector3(
                (cellCoordinate.x + 1) * CellSize,
                (cellCoordinate.y + 1) * CellSize,
                (cellCoordinate.z + 1) * CellSize
            );
        }

        /// <summary>
        /// Edit Cell Coordinate → Terrain Local Bounds。
        /// 内部数学定义是 [min, max)；Unity Bounds 仅作为返回值表示。
        /// </summary>
        public Bounds GetCellBounds(Vector3Int cellCoordinate)
        {
            Vector3 min = GetCellMin(cellCoordinate);
            Vector3 max = GetCellMax(cellCoordinate);
            Vector3 size = Vector3.one * CellSize;

            return new Bounds((min + max) * 0.5f, size);
        }
    }
}
