using System;
using System.Globalization;

namespace World.V2
{
    /// <summary>
    /// **单区域对参数的修正贡献**（World V2 · Phase 2.5 · 数据模型 · 不可变值类型）。
    ///
    /// 语义（唯一）：表示某一个区域对 **6 个连续参数分量**的**修正增量**（契约 `RA67`；`RRD26`；`RAB20`）。
    /// 它是**产物**，不是规则：不做判定、不做合成、不做取值范围处理。
    ///
    /// 【字段闭集】恰好 6 个分量（顺序固定）：
    ///   · <see cref="TemperatureModifier"/> · <see cref="MoistureModifier"/> · <see cref="FertilityModifier"/>
    ///   · <see cref="MagicDensityModifier"/> · <see cref="HolyInfluenceModifier"/> · <see cref="CorruptionInfluenceModifier"/>
    /// 契约 `RA67` 规定分量集合**恰好为 6 个**，且**禁止新增第 7 个分量**（`RA68`）。
    ///   · `Elevation` 与 `Ocean` **不在修正集内**（`RA69`；`WSC10`）：本类型**不包含**其字段；
    ///   · `Balance` 是派生只读量、不是可修正分量（`RA70`）：本类型**不包含**其字段。
    ///
    /// 【禁止字段】本类型不包含、也不得新增：版本字段、来源字段、身份字段、上下文字段、
    /// 种子字段、坐标字段、任何世界数据字段（`RRD28`–`RRD30`；`RA68`）。
    ///
    /// 【数据流方向】**身份 → 上下文 → 修正量**；本类型是**终点**：
    /// 它**不引用**任何身份层类型、上下文层类型或判定层类型（`RID67`；`RRD67`/`RRD70`）。
    /// 修正量的唯一写入者是本层（`RA75`），消费方只做加法与钳制（`WSC9`/`WSC12`）。
    ///
    /// 【构造语义】**构造只赋值**：不做取值范围处理、不做归一化、不做自动修正、不做推导——
    /// 修正量是**产物**，取值范围与钳制属合成方（`PS5`；`WSC12`）。
    ///
    /// 【登记项 `[待确认]`】本轮任务书给出的字段名与本契约分量集**不一致**
    /// （任务书列出 `Elevation` / `Ocean` 以及一个未登记的分量名），本轮**按契约分量集实现**；
    /// 该差异登记为待确认，不改变任何契约语义：替换或新增分量必须先由 `RA68`/`RA67` 升版本。
    ///
    /// 技术约束：纯数据承载、无副作用、无缓存；不引用任何引擎类型；不使用随机源与系统时钟；
    /// `ToString` 使用 `InvariantCulture`（跨机器稳定）。
    /// </summary>
    public readonly struct RegionModifier : IEquatable<RegionModifier>
    {
        private readonly float temperatureModifier;
        private readonly float moistureModifier;
        private readonly float fertilityModifier;
        private readonly float magicDensityModifier;
        private readonly float holyInfluenceModifier;
        private readonly float corruptionInfluenceModifier;

        /// <summary>
        /// 构造修正贡献（**只赋值**：不做取值范围处理、不做归一化、不做自动修正、不做推导）。
        /// 参数顺序与字段闭集顺序一致。
        /// </summary>
        public RegionModifier(
            float temperatureModifier,
            float moistureModifier,
            float fertilityModifier,
            float magicDensityModifier,
            float holyInfluenceModifier,
            float corruptionInfluenceModifier)
        {
            this.temperatureModifier = temperatureModifier;
            this.moistureModifier = moistureModifier;
            this.fertilityModifier = fertilityModifier;
            this.magicDensityModifier = magicDensityModifier;
            this.holyInfluenceModifier = holyInfluenceModifier;
            this.corruptionInfluenceModifier = corruptionInfluenceModifier;
        }

        /// <summary>`Temperature` 分量的修正增量（`RA67`；连续量，归一化取值范围见 `PS1`/`WSC12`）。</summary>
        public float TemperatureModifier
        {
            get { return temperatureModifier; }
        }

        /// <summary>`Moisture` 分量的修正增量（`RA67`）。</summary>
        public float MoistureModifier
        {
            get { return moistureModifier; }
        }

        /// <summary>`Fertility` 分量的修正增量（`RA67`）。</summary>
        public float FertilityModifier
        {
            get { return fertilityModifier; }
        }

        /// <summary>`MagicDensity` 分量的修正增量（`RA67`）。</summary>
        public float MagicDensityModifier
        {
            get { return magicDensityModifier; }
        }

        /// <summary>`HolyInfluence` 分量的修正增量（`RA67`）。</summary>
        public float HolyInfluenceModifier
        {
            get { return holyInfluenceModifier; }
        }

        /// <summary>`CorruptionInfluence` 分量的修正增量（`RA67`）。</summary>
        public float CorruptionInfluenceModifier
        {
            get { return corruptionInfluenceModifier; }
        }

        /// <summary>值相等：6 个分量逐个比较（无其它参与比较的字段）。</summary>
        public bool Equals(RegionModifier other)
        {
            return temperatureModifier.Equals(other.temperatureModifier)
                && moistureModifier.Equals(other.moistureModifier)
                && fertilityModifier.Equals(other.fertilityModifier)
                && magicDensityModifier.Equals(other.magicDensityModifier)
                && holyInfluenceModifier.Equals(other.holyInfluenceModifier)
                && corruptionInfluenceModifier.Equals(other.corruptionInfluenceModifier);
        }

        public override bool Equals(object obj)
        {
            return obj is RegionModifier && Equals((RegionModifier)obj);
        }

        /// <summary>
        /// 稳定的哈希：按 IEEE 754 位型逐分量混合（不依赖任何运行时随机化），
        /// 同一取值序列在任意进程/任意机器上得到相同结果。
        /// </summary>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + StableBits(temperatureModifier);
                hash = (hash * 31) + StableBits(moistureModifier);
                hash = (hash * 31) + StableBits(fertilityModifier);
                hash = (hash * 31) + StableBits(magicDensityModifier);
                hash = (hash * 31) + StableBits(holyInfluenceModifier);
                hash = (hash * 31) + StableBits(corruptionInfluenceModifier);
                return hash;
            }
        }

        /// <summary>
        /// 取分量的稳定位型：把**按值相等**的特殊取值归入同一表示（正零与负零、各类非数），
        /// 从而保证"两值相等 ⇒ 哈希相同"。本方法只影响哈希输入，**不修改**存储的分量取值。
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
        /// 与区域设置无关的**稳定**文本表示（`InvariantCulture`；字段完整、顺序固定、往返可读）。
        /// 仅用于日志/调试/断言，不参与任何计算。
        /// </summary>
        public override string ToString()
        {
            return "RegionModifier(temperature=" + temperatureModifier.ToString("R", CultureInfo.InvariantCulture) +
                   ", moisture=" + moistureModifier.ToString("R", CultureInfo.InvariantCulture) +
                   ", fertility=" + fertilityModifier.ToString("R", CultureInfo.InvariantCulture) +
                   ", magicDensity=" + magicDensityModifier.ToString("R", CultureInfo.InvariantCulture) +
                   ", holyInfluence=" + holyInfluenceModifier.ToString("R", CultureInfo.InvariantCulture) +
                   ", corruptionInfluence=" + corruptionInfluenceModifier.ToString("R", CultureInfo.InvariantCulture) +
                   ")";
        }

        public static bool operator ==(RegionModifier left, RegionModifier right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(RegionModifier left, RegionModifier right)
        {
            return !left.Equals(right);
        }
    }
}
