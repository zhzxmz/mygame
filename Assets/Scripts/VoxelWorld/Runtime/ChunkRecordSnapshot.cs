namespace VoxelWorld.Runtime
{
    /// <summary>
    /// ChunkRecord 的**只读快照**（Runtime_Core §4.3-4：对外只提供只读快照）。
    ///
    /// 契约：
    ///   · 全部字段只读（构造后不可变）；
    ///   · **不含**任何可变容器、体素数组、几何负载引用（RCI7："记录不是真值"）；
    ///   · <see cref="ToString"/> 覆盖全部字段 ⇒ 可直接用于"两次运行注册表序列一致"（RCI6）的断言。
    /// </summary>
    public sealed class ChunkRecordSnapshot
    {
        /// <summary>Chunk 坐标。</summary>
        public VoxelCoordinate Coordinate { get; private set; }

        /// <summary>生命周期状态。</summary>
        public ChunkLifecycleState State { get; private set; }

        /// <summary>是否已持有世界 Chunk（数据是否存在）。</summary>
        public bool HasChunk { get; private set; }

        /// <summary>该世界 Chunk 当前是否为脏（数据侧真值投影）。</summary>
        public bool IsChunkDirty { get; private set; }

        /// <summary>是否存在尚未消费的构建负载（正常 Tick 返回时应为 false）。</summary>
        public bool HasPendingMeshData { get; private set; }

        /// <summary>最近失败原因（无失败时为 null）。</summary>
        public string FailureReason { get; private set; }

        /// <summary>累计失败次数。</summary>
        public int FailureCount { get; private set; }

        /// <summary>注册时的 Tick 序号。</summary>
        public int RegisteredTick { get; private set; }

        /// <summary>最近一次状态变化的 Tick 序号。</summary>
        public int StateChangedTick { get; private set; }

        /// <summary>是否需要重新执行"生成 + 安装"（见 ChunkRecord.NeedsReinstall）。</summary>
        public bool NeedsReinstall { get; private set; }

        internal ChunkRecordSnapshot(
            VoxelCoordinate coordinate,
            ChunkLifecycleState state,
            bool hasChunk,
            bool isChunkDirty,
            bool hasPendingMeshData,
            string failureReason,
            int failureCount,
            int registeredTick,
            int stateChangedTick,
            bool needsReinstall)
        {
            Coordinate = coordinate;
            State = state;
            HasChunk = hasChunk;
            IsChunkDirty = isChunkDirty;
            HasPendingMeshData = hasPendingMeshData;
            FailureReason = failureReason;
            FailureCount = failureCount;
            RegisteredTick = registeredTick;
            StateChangedTick = stateChangedTick;
            NeedsReinstall = needsReinstall;
        }

        public override string ToString()
        {
            return Coordinate + " state=" + State +
                   " chunk=" + (HasChunk ? "yes" : "no") +
                   " dirty=" + (IsChunkDirty ? "yes" : "no") +
                   " pendingMesh=" + (HasPendingMeshData ? "yes" : "no") +
                   " reinstall=" + (NeedsReinstall ? "yes" : "no") +
                   " failures=" + FailureCount +
                   " regTick=" + RegisteredTick +
                   " stateTick=" + StateChangedTick +
                   (FailureReason == null ? string.Empty : " reason=" + FailureReason);
        }
    }
}
