using System;
using System.Globalization;

namespace World.V2
{
    /// <summary>
    /// 单个世界参数的一组值（World V2 · Plan Phase 1 · 骨架）。
    ///
    /// 字段语义（严格按 WorldParameter_Runtime_Model.md 定义，三者不可互换）：
    ///   · ParameterId —— 参数身份（与其他层共享的稳定 ID）；本文件**不定义** ID 表；
    ///   · BaseValue   —— 基础值，归属 WorldState 层（D2）；
    ///   · Modifier    —— 修正量，归属 Region 层，且 Region 是**唯一写入者**（D2）；
    ///   · FinalValue  —— 合成结果，由 WorldState 以 clamp01(BaseValue + Modifier) 得出（D2），
    ///                    Runtime 只读（D2 / RC 系列契约）。
    ///
    /// 约束：
    ///   · 纯值类型，不持有任何运行时对象引用；
    ///   · 本文件不做任何参数计算，也不做值域校验（校验属契约执行层，不在 Phase 1 骨架范围）；
    ///   · 不使用 UnityEngine。
    /// </summary>
    public readonly struct WorldParameterValue : IEquatable<WorldParameterValue>
    {
        private readonly int parameterId;
        private readonly float baseValue;
        private readonly float modifier;
        private readonly float finalValue;

        public WorldParameterValue(int parameterId, float baseValue, float modifier, float finalValue)
        {
            this.parameterId = parameterId;
            this.baseValue = baseValue;
            this.modifier = modifier;
            this.finalValue = finalValue;
        }

        public int ParameterId
        {
            get { return parameterId; }
        }

        public float BaseValue
        {
            get { return baseValue; }
        }

        public float Modifier
        {
            get { return modifier; }
        }

        public float FinalValue
        {
            get { return finalValue; }
        }

        public bool Equals(WorldParameterValue other)
        {
            return parameterId == other.parameterId &&
                   baseValue.Equals(other.baseValue) &&
                   modifier.Equals(other.modifier) &&
                   finalValue.Equals(other.finalValue);
        }

        public override bool Equals(object obj)
        {
            return obj is WorldParameterValue && Equals((WorldParameterValue)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + parameterId;
                hash = (hash * 31) + baseValue.GetHashCode();
                hash = (hash * 31) + modifier.GetHashCode();
                hash = (hash * 31) + finalValue.GetHashCode();
                return hash;
            }
        }

        /// <summary>与区域设置无关的稳定文本表示（仅用于日志/测试断言，不参与任何计算）。</summary>
        public override string ToString()
        {
            return "WorldParameterValue(" +
                   parameterId.ToString(CultureInfo.InvariantCulture) + ", base=" +
                   baseValue.ToString("R", CultureInfo.InvariantCulture) + ", mod=" +
                   modifier.ToString("R", CultureInfo.InvariantCulture) + ", final=" +
                   finalValue.ToString("R", CultureInfo.InvariantCulture) + ")";
        }

        public static bool operator ==(WorldParameterValue left, WorldParameterValue right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(WorldParameterValue left, WorldParameterValue right)
        {
            return !left.Equals(right);
        }
    }

    /// <summary>
    /// 某一点上世界参数的**状态快照**（World V2 · Plan Phase 1 · 骨架）。
    ///
    /// 设计要点（D3）：WorldParameterState 是 WorldState 的"参数面"表示，两者是**同一份事实**，
    /// 只允许单方向引用（WorldState → ParameterState），不得反向持有 WorldState、不得各自维护一份真值。
    ///
    /// 结构：
    ///   · Capacity —— 已登记的"环境参数组"数量（= 8）。
    ///     参数身份顺序取自 WorldParameter_Contract.md 附录 B 的登记顺序（1..8）；
    ///     本文件**不重新定义**该表，也不新增/删除任何参数。
    ///   · 容量固定、不可变（readonly 快照）：构造后不再变化，保证"同 Seed + 同 Position ⇒ 同结果"可被机械校验（D5）。
    ///
    /// 已登记缺口（Phase 1 不解决，仅在契约中登记）：
    ///   · 非连续型参数（如 Ocean / Elevation 这类 bool 或 int 语义）如何在同一容器内表达，
    ///     仍属未决项（见 WorldParameter_Contract.md 与审计 B-3）；本骨架一律以 float 承载，不做特例。
    ///
    /// 约束：不使用 UnityEngine；不做任何参数计算；不访问 Chunk / Mesh / Player / Runtime / Entity。
    /// </summary>
    public sealed class WorldParameterState
    {
        /// <summary>环境参数组容量（与 WorldParameter_Contract.md 附录 B 登记数量一致；此处不复制 ID 表）。</summary>
        public const int Capacity = 8;

        private readonly WorldParameterValue[] values;

        /// <param name="values">按参数身份排列的状态值；长度必须等于 Capacity。</param>
        public WorldParameterState(WorldParameterValue[] values)
        {
            if (values == null)
            {
                throw new ArgumentNullException("values");
            }

            if (values.Length != Capacity)
            {
                throw new ArgumentException(
                    "WorldParameterState 需要恰好 " +
                    Capacity.ToString(CultureInfo.InvariantCulture) +
                    " 个参数值，实际收到 " +
                    values.Length.ToString(CultureInfo.InvariantCulture) + " 个。",
                    "values");
            }

            // 防御性拷贝：快照不可被外部篡改（不可变语义）。
            this.values = new WorldParameterValue[Capacity];
            Array.Copy(values, this.values, Capacity);
        }

        /// <summary>参数个数（恒为 Capacity）。</summary>
        public int Count
        {
            get { return values.Length; }
        }

        /// <summary>按容器槽位读取（0..Capacity-1），不改变任何状态。</summary>
        public WorldParameterValue this[int index]
        {
            get
            {
                if (index < 0 || index >= values.Length)
                {
                    throw new ArgumentOutOfRangeException("index");
                }

                return values[index];
            }
        }

        /// <summary>
        /// 按参数身份读取（线性扫描，Phase 1 规模下无需索引结构）。
        /// 只读查询，不隐式创建、不填充默认值。
        /// </summary>
        public bool TryGetValue(int parameterId, out WorldParameterValue value)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i].ParameterId == parameterId)
                {
                    value = values[i];
                    return true;
                }
            }

            value = default(WorldParameterValue);
            return false;
        }
    }
}
