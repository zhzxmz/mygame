using System;
using System.Globalization;

namespace World.V2
{
    /// <summary>
    /// **Region 身份**（World V2 · Phase 2.2 · 数据模型 · 不可变值类型）。
    ///
    /// 语义（唯一）：表示"**某个位置属于哪个世界解释区域**"的**身份判定结果**（`Region_Identity_Determination_Contract.md` `RID1`/`RID2`）。
    ///
    /// **字段闭集（恰好五类）**：本类型的字段**只能**来自契约 `RID §4`（`RID40`–`RID44`），**不得**新增：
    ///   · `RegionId`                —— 稳定标识（`RID40`；仅标识/调试/解释，**不得**作规则输入或排序键）
    ///   · `RegionDomain`            —— 已登记域标识（`RID41`；未登记域不得使用）
    ///   · `RegionSource`            —— 来源引用（`RID42`；取值语义由 `Region_Design.md` §3.2 定义）
    ///   · `RegionExtentReference`   —— 空间范围**引用**（`RID43`；只承载引用，不承载几何/体素范围）
    ///   · `ExplanationTag`          —— 解释标签编码（`RID44`；**无规则权**，仅解释/调试用）
    ///
    /// **明确禁止的字段**（不得出现在本类型中）：Voxel 字段、Chunk 字段、Biome 字段、Resource 字段、
    /// Entity 字段、Mesh 字段、Runtime 字段（`RID48`–`RID52`；`RID45` 字段闭集）。
    ///
    /// **本类型不做的事**（边界，非实现选择）：
    ///   · **不做判定**：不含 `(Seed, Coordinate) → RegionIdentity` 的求值入口、不含 `Evaluate` 之类的成员；
    ///     判定边界归契约 `RID §3`（`RID25`/`RID27`/`RID28`），**实现判定属后续轮次**；
    ///   · **不含任何算法**：无划分、无噪声、无 Voronoi、无形状/阈值计算、无 Biome/Modifier 逻辑（`RID28`）；
    ///   · **不做校验与推导**：构造只做赋值，不推断、不归一化、不补默认、不解释引用码
    ///     （引用码的语义权威在上游契约，见各字段说明）；
    ///   · **不裁决未决项**：本类型表示**单个**身份值；"单点输出的基数是单值还是集合"属未决设计问题
    ///     （`RID60`），本文档与类型均**不裁决**；
    ///   · **不可变**：发布后即只读（`RID10`）；不持有任何跨帧状态（`RID36`）。
    ///
    /// **技术约束**：纯 C#；不使用 `UnityEngine`、`MonoBehaviour`、`ScriptableObject`、`Random`、`Time`、`Scene`；
    /// 不读取任何全局状态；`ToString` 与区域设置无关（`InvariantCulture`）。
    /// </summary>
    public readonly struct RegionIdentity : IEquatable<RegionIdentity>
    {
        private readonly long regionId;
        private readonly int regionDomain;
        private readonly long regionSource;
        private readonly long regionExtentReference;
        private readonly int explanationTag;

        /// <summary>
        /// 构造身份值（**只赋值，不校验、不推导、不归一化**）。
        /// 五个参数与字段闭集一一对应（`RID45`）。
        /// </summary>
        public RegionIdentity(
            long regionId,
            int regionDomain,
            long regionSource,
            long regionExtentReference,
            int explanationTag)
        {
            this.regionId = regionId;
            this.regionDomain = regionDomain;
            this.regionSource = regionSource;
            this.regionExtentReference = regionExtentReference;
            this.explanationTag = explanationTag;
        }

        /// <summary>
        /// 该区域的稳定标识（由 `RegionKey` 派生；`RID40`）。
        /// 仅用于标识、调试与解释输出；**不得**作为规则输入、**不得**作为排序/截断键（`RA15`）。
        /// </summary>
        public long RegionId
        {
            get { return regionId; }
        }

        /// <summary>
        /// 该区域所属的**已登记域**标识（`RID41`；域分配权威 = `WorldParameter_Contract.md` §6.4）。
        /// 未登记域不得使用；本类型不校验登记状态（校验属契约执行层）。
        /// </summary>
        public int RegionDomain
        {
            get { return regionDomain; }
        }

        /// <summary>
        /// 来源引用（`RID42`）：该区域的原因来源**句柄**。
        /// 取值语义（来源类别与原因引用链）由 `Region_Design.md` §3.2 定义；
        /// 本类型**不枚举、不复制、不解释**来源类别清单（`RID45`）。
        /// </summary>
        public long RegionSource
        {
            get { return regionSource; }
        }

        /// <summary>
        /// 空间范围**引用**（`RID43`）：标识该区域的锚点/尺度/衰减形状所属的引用。
        /// **只是引用**——不含几何、不含包围盒、不含体素范围、不含掩码（`RID48`）。
        /// </summary>
        public long RegionExtentReference
        {
            get { return regionExtentReference; }
        }

        /// <summary>
        /// 解释标签编码（`RID44`）：取值集合权威 = `Region_Design.md` §4.3（开放式列举）。
        /// 该标签**无规则权**：不得参与判定、采纳、排序、上限或修正计算，只能用于解释/调试/工具（`RA85`–`RA90`）。
        /// </summary>
        public int ExplanationTag
        {
            get { return explanationTag; }
        }

        /// <summary>值相等：五个字段逐个比较（`RID 字段闭集`）。</summary>
        public bool Equals(RegionIdentity other)
        {
            return regionId == other.regionId
                && regionDomain == other.regionDomain
                && regionSource == other.regionSource
                && regionExtentReference == other.regionExtentReference
                && explanationTag == other.explanationTag;
        }

        public override bool Equals(object obj)
        {
            return obj is RegionIdentity && Equals((RegionIdentity)obj);
        }

        /// <summary>稳定的哈希（自定义实现，不依赖运行时随机化；与"结果与遍历顺序无关"的纪律一致）。</summary>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + regionId.GetHashCode();
                hash = (hash * 31) + regionDomain;
                hash = (hash * 31) + regionSource.GetHashCode();
                hash = (hash * 31) + regionExtentReference.GetHashCode();
                hash = (hash * 31) + explanationTag;
                return hash;
            }
        }

        /// <summary>与区域设置无关的**稳定**文本表示（`InvariantCulture`；仅用于日志/调试/断言，不参与任何计算）。</summary>
        public override string ToString()
        {
            return "RegionIdentity(domain=" + regionDomain.ToString(CultureInfo.InvariantCulture) +
                   ", id=" + regionId.ToString(CultureInfo.InvariantCulture) +
                   ", source=" + regionSource.ToString(CultureInfo.InvariantCulture) +
                   ", extent=" + regionExtentReference.ToString(CultureInfo.InvariantCulture) +
                   ", tag=" + explanationTag.ToString(CultureInfo.InvariantCulture) +
                   ")";
        }

        public static bool operator ==(RegionIdentity left, RegionIdentity right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(RegionIdentity left, RegionIdentity right)
        {
            return !left.Equals(right);
        }
    }
}
