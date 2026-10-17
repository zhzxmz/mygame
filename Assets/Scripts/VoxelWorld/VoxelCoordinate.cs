using System;

namespace VoxelWorld
{
    /// <summary>
    /// 世界整数体素坐标（Voxel World Core V1 §3/§5）。
    ///
    /// 坐标语义：
    ///   · 整数坐标 (x, y, z) 表示占据 [x, x+1) × [y, y+1) × [z, z+1) 的体素；
    ///   · Chunk 划分使用 floor 除法（负数同样正确）：
    ///         chunkX = floor(x / ChunkSize)
    ///         localX = x - chunkX * ChunkSize   → 0..ChunkSize-1
    ///     具体换算由 VoxelWorld / VoxelChunk 提供（本结构保持纯数据）。
    ///
    /// 作为 Dictionary key 使用：
    ///   · 实现 IEquatable&lt;VoxelCoordinate&gt;，重写 Equals/GetHashCode；
    ///   · 提供 == / != 运算符；
    ///   · GetHashCode 只依赖 x/y/z，与 Equals 一致（Dictionary 契约）。
    /// </summary>
    [Serializable]
    public struct VoxelCoordinate : IEquatable<VoxelCoordinate>
    {
        public int x;
        public int y;
        public int z;

        public VoxelCoordinate(int x, int y, int z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public static VoxelCoordinate Zero
        {
            get { return new VoxelCoordinate(0, 0, 0); }
        }

        public VoxelCoordinate Offset(int dx, int dy, int dz)
        {
            return new VoxelCoordinate(x + dx, y + dy, z + dz);
        }

        public bool Equals(VoxelCoordinate other)
        {
            return x == other.x && y == other.y && z == other.z;
        }

        public override bool Equals(object obj)
        {
            return obj is VoxelCoordinate && Equals((VoxelCoordinate)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = x;
                hash = (hash * 397) ^ y;
                hash = (hash * 397) ^ z;
                return hash;
            }
        }

        public override string ToString()
        {
            return "(" + x + ", " + y + ", " + z + ")";
        }

        public static bool operator ==(VoxelCoordinate a, VoxelCoordinate b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(VoxelCoordinate a, VoxelCoordinate b)
        {
            return !a.Equals(b);
        }
    }
}
