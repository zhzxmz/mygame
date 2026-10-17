using System;
using System.Collections.Generic;
using UnityEngine;

namespace TerrainModificationV2
{
    /// <summary>
    /// Voxel 状态。
    /// 第一阶段只表达“该空间是否有地形”，不包含 Material / Water / Ore 等。
    /// </summary>
    public enum VoxelState
    {
        Air,
        Solid
    }

    /// <summary>
    /// Voxel Field：纯 C# 可修改体素数据层。
    ///
    /// Voxel 是世界中的逻辑几何单元，不是 Density Sample。
    /// Voxel 使用整数坐标与 half-open interval [min, max)。
    ///
    /// 存储策略：
    ///   只存储与 DefaultState 不同的 Voxel。
    ///   Dictionary 中没有坐标 → 返回 DefaultState。
    /// </summary>
    public sealed class VoxelField
    {
        private readonly Dictionary<Vector3Int, VoxelState> voxels =
            new Dictionary<Vector3Int, VoxelState>();

        /// <summary>单个 Voxel 在 Terrain Local Space 中的边长。</summary>
        public float VoxelSize { get; }

        /// <summary>Dictionary 未记录坐标时返回的默认状态。</summary>
        public VoxelState DefaultState { get; }

        /// <summary>当前实际存储的非默认 Voxel 数量。</summary>
        public int StoredVoxelCount
        {
            get { return voxels.Count; }
        }

        public VoxelField(
            float voxelSize,
            VoxelState defaultState = VoxelState.Solid)
        {
            if (float.IsNaN(voxelSize) ||
                float.IsInfinity(voxelSize) ||
                voxelSize <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(voxelSize),
                    voxelSize,
                    "VoxelSize must be a positive finite number."
                );
            }

            VoxelSize = voxelSize;
            DefaultState = defaultState;
        }

        /// <summary>读取 Voxel 状态。未记录坐标返回 DefaultState。</summary>
        public VoxelState Get(Vector3Int coordinate)
        {
            return voxels.TryGetValue(coordinate, out VoxelState state)
                ? state
                : DefaultState;
        }

        /// <summary>
        /// 写入 Voxel 状态。
        /// 如果状态等于 DefaultState，则从 sparse storage 中移除，保持稀疏。
        /// </summary>
        public void Set(Vector3Int coordinate, VoxelState state)
        {
            if (state == DefaultState)
            {
                voxels.Remove(coordinate);
                return;
            }

            voxels[coordinate] = state;
        }

        public bool IsSolid(Vector3Int coordinate)
        {
            return Get(coordinate) == VoxelState.Solid;
        }

        public bool IsAir(Vector3Int coordinate)
        {
            return Get(coordinate) == VoxelState.Air;
        }

        /// <summary>
        /// 只读枚举：返回当前显式存储且状态等于 state 的 Voxel 坐标快照，
        /// 按 (x, y, z) 升序排列（顺序稳定，便于确定性重放）。
        ///
        /// Sparse 语义：未记录的坐标等于 DefaultState，因此调用方无需遍历整个世界，
        /// 该 API 不修改存储，也不改变 Set() / 事务语义。
        /// </summary>
        public List<Vector3Int> GetStoredCoordinates(VoxelState state)
        {
            List<Vector3Int> result = new List<Vector3Int>();

            foreach (KeyValuePair<Vector3Int, VoxelState> pair in voxels)
            {
                if (pair.Value == state)
                {
                    result.Add(pair.Key);
                }
            }

            result.Sort(CompareCoordinates);

            return result;
        }

        private static int CompareCoordinates(Vector3Int a, Vector3Int b)
        {
            if (a.x != b.x) return a.x.CompareTo(b.x);
            if (a.y != b.y) return a.y.CompareTo(b.y);
            return a.z.CompareTo(b.z);
        }

        /// <summary>
        /// Terrain Local Position → Voxel Coordinate。
        /// 数学定义：
        ///   coordinate = floor(position / VoxelSize)
        /// 禁止 Round / Nearest / Epsilon。
        /// </summary>
        public Vector3Int PositionToVoxel(Vector3 terrainLocalPosition)
        {
            return new Vector3Int(
                Mathf.FloorToInt(terrainLocalPosition.x / VoxelSize),
                Mathf.FloorToInt(terrainLocalPosition.y / VoxelSize),
                Mathf.FloorToInt(terrainLocalPosition.z / VoxelSize)
            );
        }

        /// <summary>Voxel 最小角（Terrain Local Space）。</summary>
        public Vector3 GetVoxelMin(Vector3Int coordinate)
        {
            return new Vector3(
                coordinate.x * VoxelSize,
                coordinate.y * VoxelSize,
                coordinate.z * VoxelSize
            );
        }

        /// <summary>Voxel 最大角（Terrain Local Space，开边界）。</summary>
        public Vector3 GetVoxelMax(Vector3Int coordinate)
        {
            return new Vector3(
                (coordinate.x + 1) * VoxelSize,
                (coordinate.y + 1) * VoxelSize,
                (coordinate.z + 1) * VoxelSize
            );
        }

        /// <summary>
        /// Voxel 的 Terrain Local Bounds。
        /// 内部数学定义是 [min, max)；Unity Bounds 只作为返回值表示。
        /// </summary>
        public Bounds GetVoxelBounds(Vector3Int coordinate)
        {
            Vector3 min = GetVoxelMin(coordinate);
            Vector3 max = GetVoxelMax(coordinate);
            Vector3 size = Vector3.one * VoxelSize;

            return new Bounds((min + max) * 0.5f, size);
        }
    }
}
