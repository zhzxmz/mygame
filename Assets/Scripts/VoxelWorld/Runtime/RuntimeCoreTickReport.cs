using System.Text;

namespace VoxelWorld.Runtime
{
    /// <summary>
    /// 单次 <c>Tick()</c> 的只读统计快照（实现规范 §4.8 H 阶段产出）。
    ///
    /// 契约：
    ///   · **只读**：H 阶段写完之后不允许再变化；调用方只能读取；
    ///   · **不改世界**：报告只是数字与文本，不持有任何数据/表示引用（因此可以安全地长期保存用于对比）；
    ///   · **可对比**：<see cref="ToString"/> 覆盖全部字段 ⇒ 直接用于"两次运行 Tick 序列一致"（RCI6）的断言。
    /// </summary>
    public sealed class RuntimeCoreTickReport
    {
        /// <summary>本报告的 Tick 序号（Core 自有的单调序号，**不是时间**）。</summary>
        public int TickIndex { get; internal set; }

        /// <summary>本帧请求集合大小（A 阶段工作集，实现规范 §4.1）。</summary>
        public int RequestedCount { get; internal set; }

        /// <summary>已加载 Chunk 数（记录中 Chunk != null 的数量）。</summary>
        public int LoadedCount { get; internal set; }

        /// <summary>等待重建表示的 Chunk 数（State ∈ {Dirty, Building}）。</summary>
        public int DirtyCount { get; internal set; }

        /// <summary>表示与数据一致的 Chunk 数（State == Ready）。</summary>
        public int ReadyCount { get; internal set; }

        /// <summary>停止自动重试的失败 Chunk 数（State == Failed）。</summary>
        public int FailedCount { get; internal set; }

        /// <summary>本帧完成的加载数（D 阶段）。</summary>
        public int LoadedThisTick { get; internal set; }

        /// <summary>本帧完成的卸载数（C 阶段）。</summary>
        public int UnloadedThisTick { get; internal set; }

        /// <summary>本帧成功构建的 Mesh 数（F 阶段）。</summary>
        public int BuiltThisTick { get; internal set; }

        /// <summary>本帧成功呈递数（G 阶段；含空几何）。</summary>
        public int PresentedThisTick { get; internal set; }

        /// <summary>本帧成功呈递的"空几何"数（G 阶段，PresenterOutcome.Empty）。</summary>
        public int EmptyPresentedThisTick { get; internal set; }

        /// <summary>累计被拒绝的请求数（越界请求，Runtime §6.4）。</summary>
        public int RejectedRequestCount { get; internal set; }

        /// <summary>H 阶段检出的不变量违规数量（应为 0；非 0 视为实现缺陷）。</summary>
        public int InvariantViolationCount { get; internal set; }

        /// <summary>第一条不变量违规描述（无违规时为 null）。</summary>
        public string FirstInvariantViolation { get; internal set; }

        public override string ToString()
        {
            StringBuilder text = new StringBuilder();
            text.Append("Tick#").Append(TickIndex);
            text.Append(" requested=").Append(RequestedCount);
            text.Append(" loaded=").Append(LoadedCount);
            text.Append(" dirty=").Append(DirtyCount);
            text.Append(" ready=").Append(ReadyCount);
            text.Append(" failed=").Append(FailedCount);
            text.Append(" [load+").Append(LoadedThisTick);
            text.Append(" unload-").Append(UnloadedThisTick);
            text.Append(" build+").Append(BuiltThisTick);
            text.Append(" present+").Append(PresentedThisTick);
            text.Append(" empty+").Append(EmptyPresentedThisTick).Append("]");
            text.Append(" rejected=").Append(RejectedRequestCount);
            text.Append(" violations=").Append(InvariantViolationCount);
            return text.ToString();
        }
    }
}
