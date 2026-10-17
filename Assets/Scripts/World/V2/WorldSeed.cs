using System;
using System.Globalization;

namespace World.V2
{
    /// <summary>
    /// 世界唯一 Seed（World V2 · Plan Phase 1 · 骨架）。
    ///
    /// 职责（唯一）：
    ///   · 表示"世界唯一 Seed"这一**不可变值**，作为全流程的确定性输入。
    ///
    /// 非职责（明确禁止，本阶段不实现）：
    ///   · 不包含随机生成，不包含任何伪随机算法；
    ///   · 不包含世界生成逻辑（骨架 / 区域 / 参数求值 / 群系 / 地质 / 体素 / 网格）；
    ///   · 不依赖 UnityEngine（纯 C#）。
    ///
    /// 契约依据：
    ///   · Specifications/Architecture/WorldV2_Phase1_PreImplementation_Decisions.md
    ///     D1（唯一空间输入）· D5（确定性：同 Seed + 同 Position ⇒ 同结果）· DR15 / DR25 / DR62；
    ///   · Specifications/World/WorldParameter_Contract.md §6.4（Seed 作为域分离输入之一）；
    ///   · Specifications/World/WorldGeneration_Pipeline_Contract.md Phase 0（PC11：Seed Context 不可变）。
    ///
    /// 说明：
    ///   · 域分离所需的**域标签（Domain ID）不在本类型内定义**；域 ID 的分配表由
    ///     WorldParameter_Contract.md §6.4 冻结（本骨架不新增域、不新增参数）；
    ///   · 本类型只承载"值"，不承载任何解释或派生语义。
    /// </summary>
    public readonly struct WorldSeed : IEquatable<WorldSeed>
    {
        private readonly int value;

        public WorldSeed(int value)
        {
            this.value = value;
        }

        /// <summary>Seed 的整数值（仅表示；不含任何派生解释）。</summary>
        public int Value
        {
            get { return value; }
        }

        public bool Equals(WorldSeed other)
        {
            return value == other.value;
        }

        public override bool Equals(object obj)
        {
            return obj is WorldSeed && Equals((WorldSeed)obj);
        }

        /// <summary>稳定的哈希（自定义实现，不依赖运行时随机化；用于确定性集合语义）。</summary>
        public override int GetHashCode()
        {
            return value;
        }

        /// <summary>与区域设置无关的稳定文本表示（InvariantCulture）。</summary>
        public override string ToString()
        {
            return "WorldSeed(" + value.ToString(CultureInfo.InvariantCulture) + ")";
        }

        public static bool operator ==(WorldSeed left, WorldSeed right)
        {
            return left.value == right.value;
        }

        public static bool operator !=(WorldSeed left, WorldSeed right)
        {
            return left.value != right.value;
        }
    }
}
