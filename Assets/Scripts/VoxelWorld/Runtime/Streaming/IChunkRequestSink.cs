namespace VoxelWorld.Runtime.Streaming
{
    /// <summary>
    /// 流式层与"请求消费者"之间的抽象（Runtime §6 的落地接缝）。
    ///
    /// 语义与 <c>VoxelRuntimeCore.RequestChunk / ReleaseChunk</c> 完全一致：
    ///   · <see cref="RequestChunk"/>：请求加载（幂等；返回是否新增；坐标越界 ⇒ false + 计数）；
    ///   · <see cref="ReleaseChunk"/>：撤销请求（幂等；已加载者由下一次 Tick 卸载）。
    ///
    /// 存在理由：让"玩家位置 → ChunkCoordinate → 请求集合"的求解过程可以**脱离 Runtime Core**
    /// 单独测试（纯集合运算），也保持"流式层不依赖具体调度实现"的方向；
    /// Runtime Core 本身保持冻结，由 <c>RuntimeCoreRequestSink</c> 适配（唯一命名 Core 的位置）。
    ///
    /// 纯度：纯 C#、无状态、无 Unity 类型标识符。
    /// </summary>
    public interface IChunkRequestSink
    {
        /// <summary>请求一个 Chunk（幂等）。返回 true = 本次新增了请求。</summary>
        bool RequestChunk(VoxelCoordinate chunkCoordinate);

        /// <summary>撤销一个 Chunk 的请求（幂等）。返回 true = 本次确实撤销了请求。</summary>
        bool ReleaseChunk(VoxelCoordinate chunkCoordinate);
    }
}
