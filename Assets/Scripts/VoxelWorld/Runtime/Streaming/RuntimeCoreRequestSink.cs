namespace VoxelWorld.Runtime.Streaming
{
    /// <summary>
    /// <see cref="IChunkRequestSink"/> 的适配器：把流式层的请求转发给 <c>VoxelRuntimeCore</c>。
    ///
    /// 存在理由：流式求解器（<see cref="ChunkStreamingDriver"/>）不应该依赖具体调度实现；
    /// 本类是**唯一**允许命名 <c>VoxelRuntimeCore</c> 的流式层文件（结构扫描的显式豁免项）。
    /// Runtime Core 本身**零修改**（冻结），只使用其公开的请求 API：
    ///   · <c>RequestChunk</c> / <c>ReleaseChunk</c>（手动请求集合，V1 的加载集合来源）。
    ///
    /// 纯度：纯 C#、无状态（只持有 Core 引用）、无 Unity 类型标识符。
    /// </summary>
    public sealed class RuntimeCoreRequestSink : IChunkRequestSink
    {
        private readonly VoxelRuntimeCore core;

        public RuntimeCoreRequestSink(VoxelRuntimeCore core)
        {
            if (core == null) throw new System.ArgumentNullException("core");
            this.core = core;
        }

        /// <summary>被包装的 Core（只读；供诊断/验证使用，不得用于绕过请求 API）。</summary>
        public VoxelRuntimeCore Core
        {
            get { return core; }
        }

        public bool RequestChunk(VoxelCoordinate chunkCoordinate)
        {
            return core.RequestChunk(chunkCoordinate);
        }

        public bool ReleaseChunk(VoxelCoordinate chunkCoordinate)
        {
            return core.ReleaseChunk(chunkCoordinate);
        }

        public override string ToString()
        {
            return "RuntimeCoreRequestSink(" + core + ")";
        }
    }
}
