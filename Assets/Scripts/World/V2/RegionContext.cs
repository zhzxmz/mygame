using System;
using System.Globalization;

namespace World.V2
{
    /// <summary>
    /// **Region 解释上下文**（World V2 · Phase 2.4 · 数据模型 · 不可变值类型）。
    ///
    /// 职责（唯一）：**表示 <see cref="RegionIdentity"/> 的解释上下文**——只作为**数据承载**（解释层的只读快照），
    /// 不含任何行为、规则、判定或计算（`Region_Runtime_Data_Contract.md` `RRD21`–`RRD25`；`RID §6`）。
    ///
    /// ────────────────────────────────────────────────────────────────
    /// 【1】Context 是 Identity 的**解释层**：`Identity → Context → Modifier` 是单向链（`RID67`）；
    ///       身份由 <see cref="Identity"/> **整体承载**，本类型**不重复声明** `RegionId` / `RegionDomain` /
    ///       `RegionSource` 等身份字段（**不复制身份**，`RID68`/`RID45`）。
    /// 【2】本类型**不负责 Determination**：不接收 `WorldSeed` / `WorldCoordinate`、不含 `Determine`/`Evaluate`
    ///       之类成员；身份判定的唯一入口是 `RegionDetermination`（`RID25`/`RID27`）。
    /// 【3】本类型**不负责 Modifier**：不计算、不承载、不推导修正量（`RAB9`；`RRD26`；`RA75`/`RA76`）。
    /// 【4】本类型**不生成世界数据**：无副作用、无缓存、无 Manager/Generator；不创建 Region、不写体素、
    ///       不建 Chunk、不生成对象或网格（`RRD32`；`RA8`/`RA97`；`RID32`）。
    /// 【5】未来规则系统**只能读取 Context**，**不得反向修改 Identity**：身份是只读发布信息
    ///       （`RID10`/`RID71`/`RID72`；`RA20`）；禁止 `Modifier → Identity`、`Context → Identity` 的反推（`RID71`/`RID72`）。
    /// ────────────────────────────────────────────────────────────────
    ///
    /// **字段闭集（最小化，恰好五个）**：字段必须来自契约 `RRD` 的 `RegionContext` 组成（`RRD21`–`RRD25`）：
    ///   · <see cref="Identity"/>               —— `身份信息`（`RRD21`）：身份**引用**，不是身份字段副本；
    ///   · <see cref="RegionSourceReference"/>  —— `来源解释`的**只读引用**（`RRD22`；`RA26`/`RA28`）；
    ///   · <see cref="ExplanationTag"/>          —— `解释标签`（`RRD23`；`RID44`：**无规则权**）；
    ///   · <see cref="QualificationMask"/>      —— `资格声明`（`RRD25`；`RA80`–`RA84`：只读，类别集合未关闭）；
    ///   · <see cref="ContextVersion"/>         —— **只读状态字段**：本上下文定稿版本标签（`RRD47`；`PM34` 同族）。
    ///
    /// **禁止字段**（不得出现在本类型中）：空间数据（几何 / 包围盒 / 体素范围）、Mesh、Voxel、Biome 结果、
    /// Resource 结果、Entity 数据、玩家数据、Runtime 状态、地形数据、Chunk 字段（`RRD28`–`RRD30`；
    /// `RID48`–`RID52`；`RABB1`–`RABB11`）。
    ///
    /// **技术约束**：纯 C# 数据承载；不使用 `UnityEngine` / `MonoBehaviour` / `ScriptableObject` / `Scene` /
    /// `Random` / `Time`；不读取任何全局或运行时状态；`ToString` 与区域设置无关（`InvariantCulture`）。
    /// </summary>
    public readonly struct RegionContext : IEquatable<RegionContext>
    {
        private readonly RegionIdentity identity;
        private readonly long regionSourceReference;
        private readonly int explanationTag;
        private readonly int qualificationMask;
        private readonly int contextVersion;

        /// <summary>
        /// 构造上下文快照（**只赋值，不校验、不推导、不归一化、不做关联推理**）。
        /// 五个参数与字段闭集一一对应。
        /// </summary>
        public RegionContext(
            RegionIdentity identity,
            long regionSourceReference,
            int explanationTag,
            int qualificationMask,
            int contextVersion)
        {
            this.identity = identity;
            this.regionSourceReference = regionSourceReference;
            this.explanationTag = explanationTag;
            this.qualificationMask = qualificationMask;
            this.contextVersion = contextVersion;
        }

        /// <summary>
        /// 身份信息（`RRD21`）：该上下文所解释的 <see cref="RegionIdentity"/>。
        /// 身份**整体**由该字段承载；本类型不重复声明任何身份字段，也不修改身份（只读，`RID10`）。
        /// </summary>
        public RegionIdentity Identity
        {
            get { return identity; }
        }

        /// <summary>
        /// 来源解释的**只读引用**（`RRD22`；`RA26`/`RA28`）：指向原因解释链/来源引用的句柄。
        /// **不是**身份字段（身份字段只在 <see cref="Identity"/> 内出现一次），也不是来源类别清单；
        /// 其语义权威在 `Region_Design.md` 与解释族条款，本类型不解释该值。
        /// </summary>
        public long RegionSourceReference
        {
            get { return regionSourceReference; }
        }

        /// <summary>
        /// 解释标签（`RRD23`；`RID44`）：**无规则权**——不得参与判定、采纳、排序、上限或修正计算，
        /// 只能用于解释/调试/工具（`RA85`–`RA90`）。取值集合权威 = `Region_Design.md` §4.3。
        /// </summary>
        public int ExplanationTag
        {
            get { return explanationTag; }
        }

        /// <summary>
        /// 资格声明的只读位掩码（`RRD25`；`RA80`–`RA84`）：以 `int` 承载的类别位集合。
        /// 位与类别的对应关系**属实现规范**（`RA84`：类别集合尚未关闭）；本类型**不解释位语义**、
        /// 不做判定，也不生成任何对象（`RA82`）。
        /// </summary>
        public int QualificationMask
        {
            get { return qualificationMask; }
        }

        /// <summary>
        /// 只读状态字段：本上下文的**定稿版本标签**（供解释/工具识别版本；`RRD47`；`PM34` 同族纪律）。
        /// **不是**运行时生命周期状态：不进入调度、不跨帧存活、不表示"已加载/已生成"（`RRD44`；`RA95`；`RA137`）；
        /// 本类型不做版本比较、迁移或缓存。
        /// </summary>
        public int ContextVersion
        {
            get { return contextVersion; }
        }

        /// <summary>值相等：五个字段逐个比较（含 <see cref="Identity"/> 的值相等）。</summary>
        public bool Equals(RegionContext other)
        {
            return identity.Equals(other.identity)
                && regionSourceReference == other.regionSourceReference
                && explanationTag == other.explanationTag
                && qualificationMask == other.qualificationMask
                && contextVersion == other.contextVersion;
        }

        public override bool Equals(object obj)
        {
            return obj is RegionContext && Equals((RegionContext)obj);
        }

        /// <summary>稳定的哈希（自定义实现，不依赖运行时随机化）。</summary>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + identity.GetHashCode();
                hash = (hash * 31) + regionSourceReference.GetHashCode();
                hash = (hash * 31) + explanationTag;
                hash = (hash * 31) + qualificationMask;
                hash = (hash * 31) + contextVersion;
                return hash;
            }
        }

        /// <summary>与区域设置无关的**稳定**文本表示（`InvariantCulture`；仅用于日志/调试/断言，不参与任何计算）。</summary>
        public override string ToString()
        {
            return "RegionContext(version=" + contextVersion.ToString(CultureInfo.InvariantCulture) +
                   ", identity=" + identity.ToString() +
                   ", sourceRef=" + regionSourceReference.ToString(CultureInfo.InvariantCulture) +
                   ", tag=" + explanationTag.ToString(CultureInfo.InvariantCulture) +
                   ", qualifications=" + qualificationMask.ToString(CultureInfo.InvariantCulture) +
                   ")";
        }

        public static bool operator ==(RegionContext left, RegionContext right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(RegionContext left, RegionContext right)
        {
            return !left.Equals(right);
        }
    }
}
