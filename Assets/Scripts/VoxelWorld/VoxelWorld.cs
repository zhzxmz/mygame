using System;
using System.Collections.Generic;

namespace VoxelWorld
{
    /// <summary>
    /// VoxelWorld：多 Chunk 的体素世界数据（Voxel World Core V1 §1/§3/§5 的数据部分）。
    ///
    /// 职责（Phase 1，**不生成 Mesh**）：
    ///   · 管理 Chunk 集合（按 Chunk 坐标做 Dictionary key，懒创建）；
    ///   · 世界整数坐标 → Chunk 坐标 → 局部坐标 的换算（floor 除法，负坐标正确）；
    ///   · 读写世界体素数据，并把发生变化的 Chunk 标记为脏（供后续阶段重建）。
    ///
    /// 坐标契约（与 V1 §3 一致）：
    ///   chunkX = FloorDiv(x, ChunkSize)
    ///   localX = x - chunkX * ChunkSize      → 0..ChunkSize-1
    ///
    /// 未生成的 Chunk：
    ///   · 读取：TryGetVoxel 返回 false；GetVoxel 返回 Air（空，无碰撞）；
    ///   · 写入：自动创建该 Chunk（懒生成），不丢数据。
    /// 注意：Mesh 不是世界状态（V1 §1）；本类不持有、不生成、不修改任何 Mesh。
    /// </summary>
    public sealed class VoxelWorld
    {
        private readonly Dictionary<VoxelCoordinate, VoxelChunk> chunks =
            new Dictionary<VoxelCoordinate, VoxelChunk>();

        private readonly List<VoxelChunk> dirtyBuffer = new List<VoxelChunk>();

        /// <summary>已生成的 Chunk 数量。</summary>
        public int ChunkCount
        {
            get { return chunks.Count; }
        }

        /// <summary>已生成的 Chunk 集合（只读遍历；不要在使用中修改世界）。</summary>
        public IEnumerable<VoxelChunk> Chunks
        {
            get { return chunks.Values; }
        }

        // -----------------------------------------------------------------
        // 世界坐标 → Chunk 坐标 / 局部坐标
        // -----------------------------------------------------------------

        /// <summary>整数 floor 除法（负数同样向下取整，等价于 floor(a / b)，b &gt; 0）。</summary>
        public static int FloorDiv(int value, int divisor)
        {
            if (divisor <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    "divisor", divisor, "divisor 必须为正数。");
            }

            if (value >= 0) return value / divisor;
            return -((-value + divisor - 1) / divisor);
        }

        /// <summary>世界体素坐标 → Chunk 坐标（floor 除法）。</summary>
        public static VoxelCoordinate WorldToChunkCoordinate(
            VoxelCoordinate world)
        {
            return new VoxelCoordinate(
                FloorDiv(world.x, VoxelChunk.ChunkSize),
                FloorDiv(world.y, VoxelChunk.ChunkSize),
                FloorDiv(world.z, VoxelChunk.ChunkSize)
            );
        }

        /// <summary>世界体素坐标 → 所在 Chunk 内的局部坐标（0..ChunkSize-1）。</summary>
        public static VoxelCoordinate WorldToLocalCoordinate(
            VoxelCoordinate world)
        {
            VoxelCoordinate chunk = WorldToChunkCoordinate(world);

            return new VoxelCoordinate(
                world.x - chunk.x * VoxelChunk.ChunkSize,
                world.y - chunk.y * VoxelChunk.ChunkSize,
                world.z - chunk.z * VoxelChunk.ChunkSize
            );
        }

        // -----------------------------------------------------------------
        // Chunk 管理
        // -----------------------------------------------------------------

        public bool TryGetChunk(
            VoxelCoordinate chunkCoordinate,
            out VoxelChunk chunk)
        {
            return chunks.TryGetValue(chunkCoordinate, out chunk);
        }

        /// <summary>取得 Chunk；不存在则创建（懒生成）。</summary>
        public VoxelChunk GetOrCreateChunk(VoxelCoordinate chunkCoordinate)
        {
            VoxelChunk chunk;
            if (chunks.TryGetValue(chunkCoordinate, out chunk)) return chunk;

            chunk = new VoxelChunk(chunkCoordinate);
            chunks.Add(chunkCoordinate, chunk);
            return chunk;
        }

        /// <summary>移除指定 Chunk（供后续流式加载/卸载使用）。</summary>
        public bool RemoveChunk(VoxelCoordinate chunkCoordinate)
        {
            return chunks.Remove(chunkCoordinate);
        }

        /// <summary>清空世界（丢弃所有 Chunk 数据）。</summary>
        public void Clear()
        {
            chunks.Clear();
            dirtyBuffer.Clear();
        }

        // -----------------------------------------------------------------
        // 体素读写（世界坐标）
        // -----------------------------------------------------------------

        /// <summary>
        /// 读取世界坐标处的体素。
        /// 未生成的 Chunk 返回 false（voxel = Air），**不会**创建 Chunk。
        /// </summary>
        public bool TryGetVoxel(int worldX, int worldY, int worldZ, out Voxel voxel)
        {
            VoxelCoordinate chunkCoordinate = WorldToChunkCoordinate(
                new VoxelCoordinate(worldX, worldY, worldZ));

            return TryGetVoxel(chunkCoordinate, worldX, worldY, worldZ, out voxel);
        }

        public bool TryGetVoxel(
            VoxelCoordinate world,
            out Voxel voxel)
        {
            return TryGetVoxel(world.x, world.y, world.z, out voxel);
        }

        /// <summary>读取世界坐标处的体素；未生成区域视为 Air（空、无碰撞）。</summary>
        public Voxel GetVoxel(int worldX, int worldY, int worldZ)
        {
            Voxel voxel;
            TryGetVoxel(worldX, worldY, worldZ, out voxel);
            return voxel;
        }

        public Voxel GetVoxel(VoxelCoordinate world)
        {
            return GetVoxel(world.x, world.y, world.z);
        }

        /// <summary>
        /// 写入世界坐标处的体素类型（必要时懒创建 Chunk）。
        /// 返回 true 表示数据确实发生了变化（对应 Chunk 已被标记为脏）。
        /// 越界：世界坐标本身无“越界”概念，越界只发生在 Chunk 局部坐标（由 VoxelChunk 保证）。
        /// </summary>
        public bool SetVoxel(int worldX, int worldY, int worldZ, VoxelType type)
        {
            return SetVoxel(new VoxelCoordinate(worldX, worldY, worldZ), type);
        }

        public bool SetVoxel(VoxelCoordinate world, VoxelType type)
        {
            VoxelCoordinate chunkCoordinate = WorldToChunkCoordinate(world);
            VoxelCoordinate local = WorldToLocalCoordinate(world);

            VoxelChunk chunk = GetOrCreateChunk(chunkCoordinate);

            // VoxelChunk 负责“无变化不置脏”，并返回是否真的发生变化。
            return chunk.SetVoxel(local.x, local.y, local.z, type);
        }

        // -----------------------------------------------------------------
        // 脏 Chunk（V1 §5：修改数据 → 标记脏；本阶段不做 Mesh 重建）
        // -----------------------------------------------------------------

        /// <summary>收集当前所有脏 Chunk（结果追加到 results，按 Chunk 坐标排序保证确定性）。</summary>
        public void CollectDirtyChunks(List<VoxelChunk> results)
        {
            if (results == null) return;

            dirtyBuffer.Clear();

            foreach (KeyValuePair<VoxelCoordinate, VoxelChunk> pair in chunks)
            {
                if (pair.Value != null && pair.Value.IsDirty)
                {
                    dirtyBuffer.Add(pair.Value);
                }
            }

            dirtyBuffer.Sort(CompareChunk);

            for (int i = 0; i < dirtyBuffer.Count; i++)
            {
                results.Add(dirtyBuffer[i]);
            }
        }

        /// <summary>清空所有 Chunk 的脏标志（后续阶段完成重建后调用）。</summary>
        public void ClearDirtyFlags()
        {
            foreach (KeyValuePair<VoxelCoordinate, VoxelChunk> pair in chunks)
            {
                if (pair.Value != null) pair.Value.ClearDirty();
            }
        }

        // -----------------------------------------------------------------
        // 内部
        // -----------------------------------------------------------------

        private bool TryGetVoxel(
            VoxelCoordinate chunkCoordinate,
            int worldX,
            int worldY,
            int worldZ,
            out Voxel voxel)
        {
            VoxelChunk chunk;
            if (!chunks.TryGetValue(chunkCoordinate, out chunk) || chunk == null)
            {
                voxel = Voxel.Air; // 未生成 = 空
                return false;
            }

            VoxelCoordinate local = WorldToLocalCoordinate(
                new VoxelCoordinate(worldX, worldY, worldZ));

            voxel = chunk.GetVoxel(local.x, local.y, local.z);
            return true;
        }

        private static int CompareChunk(VoxelChunk a, VoxelChunk b)
        {
            if (a == null || b == null) return 0;

            if (a.Coordinate.x != b.Coordinate.x)
            {
                return a.Coordinate.x.CompareTo(b.Coordinate.x);
            }

            if (a.Coordinate.y != b.Coordinate.y)
            {
                return a.Coordinate.y.CompareTo(b.Coordinate.y);
            }

            return a.Coordinate.z.CompareTo(b.Coordinate.z);
        }

        public override string ToString()
        {
            return "VoxelWorld(chunks=" + chunks.Count + ")";
        }
    }
}
