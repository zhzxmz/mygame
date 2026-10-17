using System;
using System.Globalization;

namespace World.V2
{
    /// <summary>
    /// **Region 来源候选**（World V2 · Phase 3.1.11 · 数据模型 · 不可变值类型）。
    ///
    /// 职责（唯一）：**承载**契约 §3.2「候选的必需字段」的取值——**纯数据载体**。
    /// 本类型**不做**：候选生成、枚举、空间查询、半径衰减与形状、排序、去重、缓存、
    /// 引用解析、数量上限与失败策略（`RA22`/`RA26`/`RA37`/`RA44`/`RA47`/`RA51`/`RA65`/
    /// `RA72`/`RA129`/`RA130`/`RA138`）。
    ///
    /// ────────────────────────────────────────────────────────────────
    /// 【1】**字段闭集 = 8 个概念字段**（`RA` §3.2；顺序固定）。其中 `Epoch` 槽位拆为
    ///       **两个独立存储成员**（<see cref="Epoch"/> + <see cref="Ordinal"/>），
    ///       因此**存储成员共 9 个**（3.1.9 `R-1`；3.1.10 `AP-6`；`HE1`）。
    /// 【2】存储类型经设计者批准（3.1.10 `AP-1`–`AP-8`）：本类型只承载**存储类型**，
    ///       **不代表**各字段的完整运行时语义已冻结。
    /// 【3】本类型是**原因侧**数据：只描述"为什么"，不携带下游产物、也不携带"目标结果"
    ///       （`RA23`/`RA24`）；不依赖玩家状态、加载顺序、帧序、时间或 `Random`（`RA25`）。
    /// 【4】本类型是**查询结果**的承载，不是容器、不是存档单位、不是跨帧存活对象（`RA16`）。
    /// ────────────────────────────────────────────────────────────────
    ///
    /// 【构造语义】**构造只赋值**：不校验、不钳制、不归一化、不补默认值、不做字段联动或推导。
    /// `Strength` 与 `Radius` 的取值域属 Region 规则（`RA34`：越界拒绝**不在**本类型内实现；
    /// 钳制只用于叠加结果）。
    ///
    /// 【字段取值与语义的未决项（本类型不做任何假设，也不提供默认值）】：
    ///   · `Kind` 的类别 ↔ 码映射表（在 `Region_Design.md` §3.2 侧管理）；
    ///   · `DomainId` 的域值校验（`RA116` 域登记）；
    ///   · `Strength` 的有效范围；`Radius` 的单位与最大值；
    ///   · `Epoch`/`Ordinal` 的位宽、`EpochCount`/`OrdinalMax` 的读取方式、保留值编码（`RA11`）；
    ///   · `OriginRef`/`ReasonChainRef` 的解析目标、责任、生命周期与失效语义。
    ///
    /// 【禁止字段】不得新增第 9 个概念字段；不得新增 `Scale` / `Size` 字段
    /// （它们是同一字段的历史术语别名；`D-RS-1`/`D-RS-2`）。
    ///
    /// 技术约束：纯 C# 数据承载；不使用 `UnityEngine` / `MonoBehaviour` / `ScriptableObject` /
    /// `Scene` / `Random` / `Time`；不读取任何全局或运行时状态；无副作用、无缓存；
    /// 值语义与稳定文本表示（`InvariantCulture`）与区域设置无关。
    /// </summary>
    public readonly struct RegionSourceCandidate : IEquatable<RegionSourceCandidate>
    {
        private readonly int kind;
        private readonly int domainId;
        private readonly WorldCoordinate position;
        private readonly float strength;
        private readonly float radius;
        private readonly int epoch;
        private readonly int ordinal;
        private readonly long originRef;
        private readonly long reasonChainRef;

        /// <summary>
        /// 构造候选（**只赋值**：不校验、不钳制、不归一化、不补默认值、不做推导）。
        /// 参数顺序与字段闭集顺序一致（`Epoch` 槽位对应 `epoch` + `ordinal` 两个参数）。
        /// </summary>
        public RegionSourceCandidate(
            int kind,
            int domainId,
            WorldCoordinate position,
            float strength,
            float radius,
            int epoch,
            int ordinal,
            long originRef,
            long reasonChainRef)
        {
            this.kind = kind;
            this.domainId = domainId;
            this.position = position;
            this.strength = strength;
            this.radius = radius;
            this.epoch = epoch;
            this.ordinal = ordinal;
            this.originRef = originRef;
            this.reasonChainRef = reasonChainRef;
        }

        /// <summary>
        /// 来源类别（`RA` §3.2 `Kind` 行）：以**整数码**承载；类别 ↔ 码的映射表不在本类型内
        /// （`Region_Design.md` §3.2 侧独立管理），本类型不解释该值（`AP-1`）。
        /// </summary>
        public int Kind
        {
            get { return kind; }
        }

        /// <summary>
        /// 该候选的确定性域（`RA` §3.2 `DomainId` 行；`RA116` 域登记）：以整数标识承载。
        /// 域值**合法性校验**不在本类型内实现（`AP-2`）。
        /// </summary>
        public int DomainId
        {
            get { return domainId; }
        }

        /// <summary>
        /// 锚点**世界坐标**（`RA` §3.2 `Position` 行）：复用 <see cref="WorldCoordinate"/>
        /// （世界体素坐标值；不含任何坐标换算）。必须是世界坐标、不得是 Chunk 局部坐标；
        /// `CanonicalPosition` 的显式量化属采纳链（`RA12`），不在本类型内（`AP-3`）。
        /// </summary>
        public WorldCoordinate Position
        {
            get { return position; }
        }

        /// <summary>
        /// 强度倾向（`RA` §3.2 `Strength` 行）：显式标量。有效范围由 Region 规则声明，
        /// **构造期不做值域校验**（`RA34`；`AP-4`）。
        /// </summary>
        public float Strength
        {
            get { return strength; }
        }

        /// <summary>
        /// 影响尺度（`RA` §3.2 `Radius` 行）：**规范字段名 = `Radius`**；
        /// `Scale` / `Size` 为**历史术语别名**（不构成第二字段，也不得解释为独立缩放因子；
        /// `D-RS-1`/`D-RS-2`）。单位、最大值与量化**未决**；`Extent`/衰减形状与空间运算
        /// 不在本类型内（`RA51`/`RA72`；`AP-5`）。
        /// </summary>
        public float Radius
        {
            get { return radius; }
        }

        /// <summary>
        /// 时间序位的**纪元段**分量（`Epoch` 槽位第 1 个成员；`HE1`；`WorldParameter_Contract.md` 附录 B 序 10）。
        /// 与 <see cref="Ordinal"/> **分别可取值**（`RA37`/`RA44`）；保留值规则（`RA11`）**未决**，
        /// 本类型不做保留值判定、不推导、不排序（`AP-6`）。
        /// </summary>
        public int Epoch
        {
            get { return epoch; }
        }

        /// <summary>
        /// 时间序位的**段内序位**分量（`Epoch` 槽位第 2 个成员；`HE1`；`WorldParameter_Contract.md` 附录 B 序 11）。
        /// 与 <see cref="Epoch"/> 分别可取值；本类型不做排序、去重或派生（`AP-6`）。
        /// </summary>
        public int Ordinal
        {
            get { return ordinal; }
        }

        /// <summary>
        /// 原因引用（`RA` §3.2 `OriginRef` 行）：**不透明**句柄（历史事件引用 / 自然规则引用）。
        /// 本类型**不解析、不校验、不注册**该值；解析目标、责任与失效语义**另立契约**（`AP-7`）。
        /// </summary>
        public long OriginRef
        {
            get { return originRef; }
        }

        /// <summary>
        /// 可解释链引用（`RA` §3.2 `ReasonChainRef` 行）：**独立**的**不透明**句柄。
        /// 与 <see cref="OriginRef"/> 是**两个独立引用**，**不得默认指向同一对象**；
        /// 本类型不解析该值（`AP-8`）。
        /// </summary>
        public long ReasonChainRef
        {
            get { return reasonChainRef; }
        }

        /// <summary>值相等：9 个存储成员逐个比较（含 <see cref="Position"/> 的值相等）。</summary>
        public bool Equals(RegionSourceCandidate other)
        {
            return kind == other.kind
                && domainId == other.domainId
                && position.Equals(other.position)
                && strength.Equals(other.strength)
                && radius.Equals(other.radius)
                && epoch == other.epoch
                && ordinal == other.ordinal
                && originRef == other.originRef
                && reasonChainRef == other.reasonChainRef;
        }

        public override bool Equals(object obj)
        {
            return obj is RegionSourceCandidate && Equals((RegionSourceCandidate)obj);
        }

        /// <summary>
        /// 稳定的哈希：按 IEEE 754 位型混合两个连续分量，其余分量按整数混合
        /// （不依赖任何运行时随机化；同一取值序列在任意进程/任意机器上得到相同结果）。
        /// </summary>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + kind;
                hash = (hash * 31) + domainId;
                hash = (hash * 31) + position.GetHashCode();
                hash = (hash * 31) + StableBits(strength);
                hash = (hash * 31) + StableBits(radius);
                hash = (hash * 31) + epoch;
                hash = (hash * 31) + ordinal;
                hash = (hash * 31) + originRef.GetHashCode();
                hash = (hash * 31) + reasonChainRef.GetHashCode();
                return hash;
            }
        }

        /// <summary>
        /// 取连续分量的稳定位型：把**按值相等**的特殊取值归入同一表示（正零与负零、各类非数），
        /// 从而保证"两值相等 ⇒ 哈希相同"。本方法只影响哈希输入，**不修改**存储的取值。
        /// </summary>
        private static int StableBits(float value)
        {
            if (value == 0f)
            {
                return 0;
            }

            if (float.IsNaN(value))
            {
                return unchecked((int)0x7FC00000);
            }

            return BitConverter.SingleToInt32Bits(value);
        }

        /// <summary>
        /// 与区域设置无关的**稳定**文本表示（`InvariantCulture`；字段完整、顺序固定）。
        /// 仅用于日志/调试/断言，**不参与任何计算**，也不表示字段语义已冻结。
        /// </summary>
        public override string ToString()
        {
            return "RegionSourceCandidate(kind=" + kind.ToString(CultureInfo.InvariantCulture) +
                   ", domain=" + domainId.ToString(CultureInfo.InvariantCulture) +
                   ", position=" + position.ToString() +
                   ", strength=" + strength.ToString("R", CultureInfo.InvariantCulture) +
                   ", radius=" + radius.ToString("R", CultureInfo.InvariantCulture) +
                   ", epoch=" + epoch.ToString(CultureInfo.InvariantCulture) +
                   ", ordinal=" + ordinal.ToString(CultureInfo.InvariantCulture) +
                   ", originRef=" + originRef.ToString(CultureInfo.InvariantCulture) +
                   ", reasonChainRef=" + reasonChainRef.ToString(CultureInfo.InvariantCulture) +
                   ")";
        }

        public static bool operator ==(RegionSourceCandidate left, RegionSourceCandidate right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(RegionSourceCandidate left, RegionSourceCandidate right)
        {
            return !left.Equals(right);
        }
    }
}
