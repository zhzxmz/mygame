using System.Text;

namespace VoxelWorld.Runtime.Streaming
{
    /// <summary>
    /// 单次 <see cref="ChunkStreamingDriver.Update"/> 的只读统计快照。
    ///
    /// 契约：只读、不含任何数据/表示引用、<see cref="ToString"/> 覆盖全部字段
    /// ⇒ 可直接用于"同路径两次运行报告序列一致"（确定性）断言。
    /// </summary>
    public sealed class ChunkStreamingReport
    {
        /// <summary>本次更新的序号（驱动自有的单调序号，不是时间）。</summary>
        public int UpdateIndex { get; internal set; }

        /// <summary>本次使用的焦点 Chunk 坐标（由玩家世界坐标经 VoxelCore 换算得到）。</summary>
        public VoxelCoordinate FocusChunk { get; internal set; }

        /// <summary>期望加载集合大小（= 水平候选数 × VerticalChunkCount）。</summary>
        public int DesiredCount { get; internal set; }

        /// <summary>已请求集合中落在滞回带（R_load 之外、R_unload 之内）而被保留的数量。</summary>
        public int RetainedInHysteresisBand { get; internal set; }

        /// <summary>本次新增请求数。</summary>
        public int RequestedThisUpdate { get; internal set; }

        /// <summary>本次撤销请求数（实际撤销成功的数量）。</summary>
        public int ReleasedThisUpdate { get; internal set; }

        /// <summary>本次被拒绝的请求数（越界 / 上游拒绝；非静默）。</summary>
        public int RejectedThisUpdate { get; internal set; }

        /// <summary>更新结束时的已请求集合大小。</summary>
        public int StreamedCount { get; internal set; }

        public override string ToString()
        {
            StringBuilder text = new StringBuilder();
            text.Append("Update#").Append(UpdateIndex);
            text.Append(" focus=").Append(FocusChunk);
            text.Append(" desired=").Append(DesiredCount);
            text.Append(" band=").Append(RetainedInHysteresisBand);
            text.Append(" [+").Append(RequestedThisUpdate);
            text.Append(" -").Append(ReleasedThisUpdate);
            text.Append(" reject=").Append(RejectedThisUpdate).Append(']');
            text.Append(" streamed=").Append(StreamedCount);
            return text.ToString();
        }
    }
}
