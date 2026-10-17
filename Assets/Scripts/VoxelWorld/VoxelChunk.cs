using System;

namespace VoxelWorld
{
    /// <summary>
    /// Chunk：固定 ChunkSize³ 的体素数据容器（Voxel World Core V1 §3）。
    ///
    /// 契约：
    ///   · ChunkSize = 16（每轴 16 个体素），体素占 1×1×1 world unit；
    ///   · 只保存世界真值数据（Voxel[,,]），不包含 Mesh / Collider / MonoBehaviour；
    ///   · 局部坐标范围 0..ChunkSize-1，越界一律抛异常（不静默截断）；
    ///   · 写入会标记 IsDirty（V1 §5：修改数据 → 标记 Chunk 脏），
    ///     但**不**在这里做任何 Mesh 重建（Phase 1 不含 Mesh）。
    /// </summary>
    public sealed class VoxelChunk
    {
        /// <summary>Chunk 每轴的体素数量（V1 §3 默认 16）。</summary>
        public const int ChunkSize = 16;

        /// <summary>一个 Chunk 的体素总数（16³ = 4096）。</summary>
        public const int VoxelCount = ChunkSize * ChunkSize * ChunkSize;

        private readonly Voxel[,,] voxels =
            new Voxel[ChunkSize, ChunkSize, ChunkSize];

        /// <summary>该 Chunk 的 Chunk 坐标（整数）。</summary>
        public VoxelCoordinate Coordinate { get; private set; }

        /// <summary>是否存在尚未处理的修改（V1 §5 的“脏”标志）。</summary>
        public bool IsDirty { get; private set; }

        public VoxelChunk(VoxelCoordinate coordinate)
        {
            Coordinate = coordinate;
            IsDirty = true; // 新 Chunk 尚未重建过，视为脏
        }

        // -----------------------------------------------------------------
        // 局部坐标 ↔ 世界坐标
        // -----------------------------------------------------------------

        /// <summary>局部坐标是否在 0..ChunkSize-1 范围内。</summary>
        public static bool IsLocalCoordinateValid(int localX, int localY, int localZ)
        {
            return localX >= 0 && localX < ChunkSize &&
                   localY >= 0 && localY < ChunkSize &&
                   localZ >= 0 && localZ < ChunkSize;
        }

        /// <summary>局部坐标 → 世界体素坐标（chunkCoordinate * ChunkSize + local）。</summary>
        public VoxelCoordinate LocalToWorldCoordinate(
            int localX,
            int localY,
            int localZ)
        {
            CheckLocalCoordinate(localX, localY, localZ);

            return new VoxelCoordinate(
                Coordinate.x * ChunkSize + localX,
                Coordinate.y * ChunkSize + localY,
                Coordinate.z * ChunkSize + localZ
            );
        }

        /// <summary>
        /// 世界体素坐标 → 本 Chunk 的局部坐标。
        /// 若该世界坐标不落在本 Chunk 内，返回 false 并给出局部坐标（可能越界，仅用于诊断）。
        /// </summary>
        public bool TryWorldToLocalCoordinate(
            VoxelCoordinate world,
            out int localX,
            out int localY,
            out int localZ)
        {
            localX = world.x - Coordinate.x * ChunkSize;
            localY = world.y - Coordinate.y * ChunkSize;
            localZ = world.z - Coordinate.z * ChunkSize;

            return IsLocalCoordinateValid(localX, localY, localZ);
        }

        // -----------------------------------------------------------------
        // 读写
        // -----------------------------------------------------------------

        /// <summary>读取局部坐标处的体素（越界抛 ArgumentOutOfRangeException）。</summary>
        public Voxel GetVoxel(int localX, int localY, int localZ)
        {
            CheckLocalCoordinate(localX, localY, localZ);
            return voxels[localX, localY, localZ];
        }

        /// <summary>
        /// 写入局部坐标处的体素类型（越界抛 ArgumentOutOfRangeException）。
        /// 返回 true 表示类型确实发生变化（并已标记 IsDirty）；
        /// 写入相同类型返回 false，且不会置脏（避免无意义的重建请求）。
        /// </summary>
        public bool SetVoxel(int localX, int localY, int localZ, VoxelType type)
        {
            CheckLocalCoordinate(localX, localY, localZ);

            Voxel current = voxels[localX, localY, localZ];
            if (current.Type == type) return false;

            voxels[localX, localY, localZ] = new Voxel(type);
            IsDirty = true;
            return true;
        }

        /// <summary>写入局部坐标处的体素（结构体重载，语义同 VoxelType 版本）。</summary>
        public bool SetVoxel(int localX, int localY, int localZ, Voxel voxel)
        {
            return SetVoxel(localX, localY, localZ, voxel.Type);
        }

        /// <summary>把整个 Chunk 填成同一种类型（生成期初始化用）。</summary>
        public void Fill(VoxelType type)
        {
            for (int x = 0; x < ChunkSize; x++)
            {
                for (int y = 0; y < ChunkSize; y++)
                {
                    for (int z = 0; z < ChunkSize; z++)
                    {
                        voxels[x, y, z] = new Voxel(type);
                    }
                }
            }

            IsDirty = true;
        }

        // -----------------------------------------------------------------
        // 脏标记（V1 §5：Phase 1 只保留标志位，不做 Mesh 重建）
        // -----------------------------------------------------------------

        public void MarkDirty()
        {
            IsDirty = true;
        }

        public void ClearDirty()
        {
            IsDirty = false;
        }

        // -----------------------------------------------------------------
        // 边界检查
        // -----------------------------------------------------------------

        private static void CheckLocalCoordinate(
            int localX,
            int localY,
            int localZ)
        {
            if (IsLocalCoordinateValid(localX, localY, localZ)) return;

            throw new ArgumentOutOfRangeException(
                "localX",
                localX,
                "Chunk 局部坐标越界：localX/localY/localZ 必须在 [0, " +
                (ChunkSize - 1) + "] 内，实际收到 (" +
                localX + ", " + localY + ", " + localZ + ")。"
            );
        }

        public override string ToString()
        {
            return "VoxelChunk" + Coordinate +
                   (IsDirty ? "（dirty）" : "（clean）");
        }
    }
}
