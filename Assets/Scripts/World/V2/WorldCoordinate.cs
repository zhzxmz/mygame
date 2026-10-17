using System;
using System.Globalization;

namespace World.V2
{
    /// <summary>
    /// Phase 1 世界空间坐标（World V2 · Plan Phase 1 · 骨架）。
    ///
    /// 职责（唯一）：
    ///   · 表示**世界体素坐标**的值（整数三元组），作为 Phase 1 的唯一空间输入值。
    ///
    /// 硬性约束：
    ///   · 只使用整数坐标；
    ///   · 不依赖 Transform、不依赖 Scene、不依赖 Chunk；
    ///   · **不提供任何坐标换算**（Chunk ↔ 世界、Chunk 局部坐标等一律不在此类型内）——
    ///     换算语义的唯一权威是既有 VoxelCore 契约
    ///     （VoxelWorld_Core.md；ChunkSize = 16；world = chunk × 16 + local；负数使用 floor 语义），
    ///     本类型**不得**成为"第二套坐标契约"；
    ///   · 不定义世界边界（水平/垂直边界语义属世界与体素层，本骨架不新增参数）。
    ///
    /// 契约依据：
    ///   · Specifications/Architecture/WorldV2_Phase1_PreImplementation_Decisions.md
    ///     D1（DR15：世界坐标是唯一空间输入；DR19：换算复用 VoxelCore；DR20：Phase 1 不需要 Chunk 语义）；
    ///   · Specifications/Architecture/WorldV2_Implementation_Boundary_Plan.md
    ///     IBP12（坐标契约）· IBP46–IBP49（可重复 / 与 Scene 无关 / 与 Chunk 无关 / 与 Player 无关）。
    /// </summary>
    public readonly struct WorldCoordinate : IEquatable<WorldCoordinate>
    {
        private readonly int x;
        private readonly int y;
        private readonly int z;

        public WorldCoordinate(int x, int y, int z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public int X
        {
            get { return x; }
        }

        public int Y
        {
            get { return y; }
        }

        public int Z
        {
            get { return z; }
        }

        /// <summary>世界原点 (0,0,0)（仅表示坐标值，不含任何特殊语义）。</summary>
        public static WorldCoordinate Origin
        {
            get { return new WorldCoordinate(0, 0, 0); }
        }

        /// <summary>按整数偏移返回新坐标（纯值运算，不涉及 Chunk / Scene / Transform）。</summary>
        public WorldCoordinate Offset(int deltaX, int deltaY, int deltaZ)
        {
            return new WorldCoordinate(x + deltaX, y + deltaY, z + deltaZ);
        }

        public bool Equals(WorldCoordinate other)
        {
            return x == other.x && y == other.y && z == other.z;
        }

        public override bool Equals(object obj)
        {
            return obj is WorldCoordinate && Equals((WorldCoordinate)obj);
        }

        /// <summary>稳定的哈希（自定义实现，不依赖运行时随机化；与"遍历顺序不得影响结果"的纪律一致）。</summary>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + x;
                hash = (hash * 31) + y;
                hash = (hash * 31) + z;
                return hash;
            }
        }

        /// <summary>与区域设置无关的稳定文本表示（InvariantCulture）。</summary>
        public override string ToString()
        {
            return "WorldCoordinate(" +
                   x.ToString(CultureInfo.InvariantCulture) + ", " +
                   y.ToString(CultureInfo.InvariantCulture) + ", " +
                   z.ToString(CultureInfo.InvariantCulture) + ")";
        }

        public static bool operator ==(WorldCoordinate left, WorldCoordinate right)
        {
            return left.x == right.x && left.y == right.y && left.z == right.z;
        }

        public static bool operator !=(WorldCoordinate left, WorldCoordinate right)
        {
            return !(left == right);
        }
    }
}
