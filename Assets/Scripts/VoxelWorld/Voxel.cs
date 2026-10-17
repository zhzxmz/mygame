using System;

namespace VoxelWorld
{
    /// <summary>
    /// 单个 Voxel 的数据（Voxel World Core V1 §2）。
    ///
    /// 契约：
    ///   · Voxel 是世界最小空间单位，占 1×1×1 world unit；
    ///   · 本结构只描述“类型”，不携带坐标 —— 坐标由 VoxelCoordinate（世界）
    ///     或 Chunk 内局部坐标表达；
    ///   · 世界真值 = 体素数据（本结构），Mesh 永不作为真值（V1 §1）。
    ///
    /// 提供 IsSolid()：Air ⇒ false（空、无碰撞）；其它类型 ⇒ true（实体、有碰撞）。
    /// </summary>
    [Serializable]
    public struct Voxel : IEquatable<Voxel>
    {
        /// <summary>体素类型。默认值 = VoxelType.Air。</summary>
        public VoxelType Type;

        public Voxel(VoxelType type)
        {
            Type = type;
        }

        /// <summary>空气体素（IsSolid() == false）。</summary>
        public static Voxel Air
        {
            get { return new Voxel(VoxelType.Air); }
        }

        /// <summary>
        /// 是否为实体（可碰撞）。
        /// 规则（V1 §2）：Air ⇒ false；任何非 Air ⇒ true。
        /// </summary>
        public bool IsSolid()
        {
            return Type != VoxelType.Air;
        }

        /// <summary>是否为空气（IsSolid() 的取反，便于判定“可通行/可放置”）。</summary>
        public bool IsAir()
        {
            return Type == VoxelType.Air;
        }

        public bool Equals(Voxel other)
        {
            return Type == other.Type;
        }

        public override bool Equals(object obj)
        {
            return obj is Voxel && Equals((Voxel)obj);
        }

        public override int GetHashCode()
        {
            return (int)Type;
        }

        public override string ToString()
        {
            return Type.ToString();
        }

        public static bool operator ==(Voxel a, Voxel b)
        {
            return a.Type == b.Type;
        }

        public static bool operator !=(Voxel a, Voxel b)
        {
            return a.Type != b.Type;
        }
    }
}
