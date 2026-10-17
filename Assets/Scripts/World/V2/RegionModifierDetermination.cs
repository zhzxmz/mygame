namespace World.V2
{
    /// <summary>
    /// **修正量确定边界**（World V2 · Phase 3.0 · 唯一入口：上下文 → 修正量）。
    ///
    /// 职责（唯一）：接收一个 <see cref="RegionContext"/>，返回该上下文对应的 <see cref="RegionModifier"/>
    /// （六个分量的修正贡献）。**只解释上下文**：本层不重新认识世界。
    ///
    /// 【单向】允许 `Context → Modifier`；**禁止反向**：不得由修正量反推上下文、身份或来源
    /// （`RID67`；`RRD67`/`RRD70`；`RA79`）。
    ///
    /// 【允许读取】（仅此四项与身份值本体，`RRD21`–`RRD25`）：
    ///   · `context.Identity`（身份值本体：域、标识、来源、范围引用、身份解释标签）；
    ///   · `context.RegionSourceReference`（来源解释引用）；
    ///   · `context.ExplanationTag`（解释标签）；
    ///   · `context.QualificationMask`（资格位集合）。
    /// 本实现**不读取** `ContextVersion`（不在允许清单内），也不读取任何其它内容。
    ///
    /// 【禁止读取】任何空间输入（种子、坐标）、任何运行时状态、任何下游层产物：
    /// 修正量只能来自上文四项（`RID23`/`RID36`；`RABB1`–`RABB6`）。
    ///
    /// 【当前实现 = 占位】真实的修正规则尚未冻结：
    ///   · 六个分量由"**基于允许字段的稳定整数混合**"导出（无空间计算、无插值、无噪声、无频率、
    ///     无随机源、无系统时钟）——同一上下文恒得同一修正量；
    ///   · 分量幅度落在 `[0, 0.25)` 的**占位区间**内：该上限只是占位约束，**不代表**任何真实修正幅度；
    ///   · 占位映射**不代表**最终修正规则（贡献形状、衰减、上限属未来规则）；
    ///   · 替换点 = 本类的占位映射整体；替换**不得**改变入口签名与单向性。
    ///
    /// 【不进入参数系统】本类型不引用任何参数侧类型（参数状态、参数规则、参数求值方）：
    /// 修正量在这里只是**输出**；"修正如何进入最终环境参数"属后续阶段（Phase 3.1）。
    ///
    /// 结构约束：本类型**没有字段**（只有编译期常量）——**无缓存、无静态可变状态**；
    /// 唯一公开入口是 <see cref="Determine"/>；方法为**纯函数**（同一上下文 ⇒ 同一修正量），**无副作用**。
    ///
    /// 技术约束：纯 C#；不引用任何引擎类型。
    /// </summary>
    public static class RegionModifierDetermination
    {
        /// <summary>混合起始常量（无特殊语义）。</summary>
        private const int MixStart = 0x2F1B3C5D;

        /// <summary>混合步乘子（幂等整数常量）。</summary>
        private const int MixPrime = 0x01000193;

        /// <summary>雪崩步乘子（截断为 32 位）。</summary>
        private const int AvalanchePrime = unchecked((int)0x85EBCA6B);

        /// <summary>
        /// 占位幅度上限 = `0.25`：只用于把占位输出限制在一个小而明确的区间内，
        /// **不代表**任何真实修正幅度、不代表任何规则。
        /// </summary>
        private const float PlaceholderAmplitude = 0.25f;

        /// <summary>占位取值面的分子上界 = `2^24`（`float` 可精确表示该范围内整数）。</summary>
        private const float PlaceholderUnitRange = 16777216f;

        /// <summary>
        /// 占位幅度除数 = `2^24 / 0.25 = 2^26`：把 24 位整数映射到 `[0, 0.25)`；
        /// 分子与除数在 `float` 中均可精确表示，结果与平台浮点路径无关。
        /// </summary>
        private const float PlaceholderScale = PlaceholderUnitRange / PlaceholderAmplitude;

        /// <summary>
        /// **唯一公开入口**：`RegionContext → RegionModifier`（占位映射）。
        ///
        /// 纯函数、无副作用：同一上下文 ⇒ 逐分量相同的修正量；不写任何状态、不产生任何缓存。
        /// 本方法**不读取**任何空间输入或运行时状态，**不产生**任何世界数据。
        /// </summary>
        public static RegionModifier Determine(RegionContext context)
        {
            RegionIdentity identity = context.Identity;

            int baseMix = Mix(MixStart, identity.RegionDomain);
            baseMix = Mix(baseMix, identity.ExplanationTag);
            baseMix = Mix(baseMix, context.ExplanationTag);
            baseMix = Mix(baseMix, context.QualificationMask);

            unchecked
            {
                baseMix = Mix(baseMix, (int)identity.RegionId);
                baseMix = Mix(baseMix, (int)(identity.RegionId >> 32));
                baseMix = Mix(baseMix, (int)identity.RegionSource);
                baseMix = Mix(baseMix, (int)(identity.RegionSource >> 32));
                baseMix = Mix(baseMix, (int)identity.RegionExtentReference);
                baseMix = Mix(baseMix, (int)(identity.RegionExtentReference >> 32));
                baseMix = Mix(baseMix, (int)context.RegionSourceReference);
                baseMix = Mix(baseMix, (int)(context.RegionSourceReference >> 32));
            }

            return new RegionModifier(
                Unit(baseMix, 1),
                Unit(baseMix, 2),
                Unit(baseMix, 3),
                Unit(baseMix, 4),
                Unit(baseMix, 5),
                Unit(baseMix, 6));
        }

        /// <summary>取第 `channel` 个分量的占位取值：`[0, 0.25)`。</summary>
        private static float Unit(int baseMix, int channel)
        {
            unchecked
            {
                int mixed = Mix(baseMix, channel);
                int positive = mixed & 0x00FFFFFF;
                return positive / PlaceholderScale;
            }
        }

        /// <summary>
        /// 确定性的整数位混合（占位用途；非噪声、无插值、无空间含义）。
        /// 本文件**不引用**参数侧的哈希实现：其输入含种子与世界坐标，而本层被禁止读取它们。
        /// </summary>
        private static int Mix(int accumulator, int value)
        {
            unchecked
            {
                int hash = (accumulator ^ value) * MixPrime;
                hash ^= hash >> 13;
                hash *= AvalanchePrime;
                hash ^= hash >> 16;
                return hash;
            }
        }
    }
}
