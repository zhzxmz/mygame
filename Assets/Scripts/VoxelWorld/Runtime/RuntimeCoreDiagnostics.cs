namespace VoxelWorld.Runtime
{
    /// <summary>
    /// Runtime Core 的累计诊断计数（只读；Runtime §9.2 / Runtime_Core §11 H 阶段）。
    ///
    /// 契约：**只读诊断**——读取计数不得改变世界状态、不得改变调度结果（RCI 中"诊断只读"的落地）。
    /// 计数器由 Core 在阶段内累加（<c>internal set</c>），对外只读。
    /// </summary>
    public sealed class RuntimeCoreDiagnostics
    {
        /// <summary>已执行的 Tick 次数。</summary>
        public int TickCount { get; internal set; }

        /// <summary>成功"生成 + 安装 + 注册"的累计次数。</summary>
        public int GeneratedTotal { get; internal set; }

        /// <summary>Mesher 成功返回负载的累计次数。</summary>
        public int BuiltTotal { get; internal set; }

        /// <summary>成功呈递的累计次数（含空几何）。</summary>
        public int PresentedTotal { get; internal set; }

        /// <summary>成功呈递的"空几何"累计次数。</summary>
        public int EmptyPresentedTotal { get; internal set; }

        /// <summary>成功卸载（表示 + 数据 + 记录全部释放）的累计次数。</summary>
        public int UnloadedTotal { get; internal set; }

        /// <summary>进入 <c>Failed</c> 状态的累计次数（达到重试上限）。</summary>
        public int FailedTotal { get; internal set; }

        /// <summary>被拒绝的请求累计数（坐标超出垂直有效范围）。</summary>
        public int RejectedRequests { get; internal set; }

        /// <summary>Mesher 失败（返回 false 或抛异常）累计次数。</summary>
        public int BuildFailures { get; internal set; }

        /// <summary>Presenter 失败（返回 Failed、抛异常或结果自相矛盾）累计次数。</summary>
        public int PresentFailures { get; internal set; }

        /// <summary>PresenterOutcome 与 HasGeometry 不一致的累计次数（防御性交叉校验）。</summary>
        public int OutcomeMismatches { get; internal set; }

        /// <summary>卸载阶段 presenter.Release 抛异常的累计次数（数据仍照常释放）。</summary>
        public int ReleaseFailures { get; internal set; }

        /// <summary>对账时"世界有 Chunk 但注册表无记录"的补登记累计次数（Runtime_Core §8.5）。</summary>
        public int ReconcileRegistrations { get; internal set; }

        /// <summary>对账时"记录有 Chunk 但世界里已无该 Chunk"的累计次数（→ Failed(DataMissing)）。</summary>
        public int DataMissingFailures { get; internal set; }

        /// <summary>最近一次失败原因（无失败时为 null）。禁止静默失败（Runtime §9.1-3）。</summary>
        public string LastFailureReason { get; internal set; }

        public override string ToString()
        {
            return "Diagnostics(ticks=" + TickCount +
                   ", generated=" + GeneratedTotal +
                   ", built=" + BuiltTotal +
                   ", presented=" + PresentedTotal +
                   ", empty=" + EmptyPresentedTotal +
                   ", unloaded=" + UnloadedTotal +
                   ", failed=" + FailedTotal +
                   ", rejected=" + RejectedRequests +
                   ", buildFailures=" + BuildFailures +
                   ", presentFailures=" + PresentFailures +
                   ", outcomeMismatches=" + OutcomeMismatches +
                   ", releaseFailures=" + ReleaseFailures +
                   ", reconciled=" + ReconcileRegistrations +
                   ", dataMissing=" + DataMissingFailures + ")";
        }
    }
}
