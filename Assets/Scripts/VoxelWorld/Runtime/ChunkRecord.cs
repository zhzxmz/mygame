using VoxelWorld.Mesh;

namespace VoxelWorld.Runtime
{
    /// <summary>
    /// ChunkRecord：**Runtime Core 的管理对象，不是世界真值**（实现规范 §2.1）。
    ///
    /// 它只记录"这个坐标在本帧调度中处于什么状态、失败了几次、指向哪个世界 Chunk"：
    ///   · **不拥有**体素数据、**不复制**体素数据、**不是**任何几何或表示；
    ///   · 删除全部 ChunkRecord 后，<c>VoxelWorld</c> 中的体素数据必须一字不变（RCI7）；
    ///   · 真值只有一个来源：<c>VoxelWorld</c> + <c>VoxelChunk</c>（VoxelWorld_Core §1）。
    ///
    /// 可见性：<c>internal</c> —— 记录**不得**通过任何公开 API 暴露可变实例；
    /// 对外只提供只读快照 <see cref="ChunkRecordSnapshot"/>（RCI7 / Runtime_Core §4.3-4）。
    ///
    /// 字段禁令（实现规范 §2.2）：不得出现体素数组、Unity 数学结构体、Mesh 资产、GameObject、
    /// 材质、场景引用、线程/任务对象、时间戳。
    /// </summary>
    internal sealed class ChunkRecord
    {
        /// <summary>Chunk 坐标（注册表键；构造后只读）。</summary>
        public readonly VoxelCoordinate Coordinate;

        /// <summary>生命周期状态（实现规范 §2.6；Tick 返回时不得为瞬态状态）。</summary>
        public ChunkLifecycleState State;

        /// <summary>
        /// **世界拥有**的 Chunk 引用（由 <c>VoxelWorld.GetOrCreateChunk</c> 创建）。
        /// Core 只持有引用：不 new、不 Dispose、不绕过 VoxelWorld API。
        /// </summary>
        public VoxelChunk Chunk;

        /// <summary>
        /// 本 Tick 构建出的纯数据负载（一次性；呈递成功或失败后立即置 null）。
        /// 名字用 MeshData 而非 Mesh：避免与 Unity 类型名混淆，并让"Core 不得出现 Unity 类型标识符"
        /// 的结构扫描保持严格（Runtime_Core §1.1 RC-P1）。
        /// </summary>
        public VoxelMeshData MeshData;

        /// <summary>最近一次失败原因（无失败时为 null；失败时禁止为空字符串）。</summary>
        public string FailureReason;

        /// <summary>累计失败次数（超过 MaxRetriesPerChunk ⇒ 停止自动重试）。</summary>
        public int FailureCount;

        /// <summary>注册时的 Tick 序号（Core 自有的单调序号，不是时间）。</summary>
        public readonly int RegisteredTick;

        /// <summary>最近一次状态变化的 Tick 序号。</summary>
        public int StateChangedTick;

        /// <summary>
        /// 是否需要重新执行"生成 + 安装"（Runtime_Core §2.7 重试判据）：
        ///   true  = 生成/校验失败（Chunk == null）或安装失败（可能部分写入）⇒ 重试必须全量写入；
        ///   false = 安装已完成（数据完整）⇒ 重试只需重建表示。
        /// </summary>
        public bool NeedsReinstall;

        public ChunkRecord(VoxelCoordinate coordinate, int registeredTick)
        {
            Coordinate = coordinate;
            RegisteredTick = registeredTick;
            StateChangedTick = registeredTick;
            State = ChunkLifecycleState.Generating;
        }

        /// <summary>供诊断与"两次运行一致"断言使用的确定性文本（不含任何数据引用）。</summary>
        public ChunkRecordSnapshot ToSnapshot()
        {
            return new ChunkRecordSnapshot(
                Coordinate,
                State,
                Chunk != null,
                Chunk != null && Chunk.IsDirty,
                MeshData != null,
                FailureReason,
                FailureCount,
                RegisteredTick,
                StateChangedTick,
                NeedsReinstall);
        }
    }
}
