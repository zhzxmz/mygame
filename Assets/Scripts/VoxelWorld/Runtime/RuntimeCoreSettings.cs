namespace VoxelWorld.Runtime
{
    /// <summary>
    /// Runtime Core 的显式参数（Runtime_Core §2.3 的 **V1 子集**）。
    ///
    /// V1 收窄（实现规范 §7.2 / 附录 B N1·N6）：
    ///   · **不含** <c>ViewDistanceChunks</c> / <c>UnloadHysteresisChunks</c>（不实现距离流式加载）；
    ///   · **不含** <c>MaxPendingEdits</c> / <c>OutOfRangePolicy</c>（不实现编辑入口，Mining 不在 V1）；
    ///   · 加载集合来源 = 手动请求集合（<c>RequestChunk</c>）∩ 垂直有效范围。
    ///
    /// 全部参数都是纯整数（无 Unity 类型），所有影响调度的参数**必须显式传入**
    /// （Generation §3 / Runtime §3：禁止隐藏全局状态）。
    /// </summary>
    public sealed class RuntimeCoreSettings
    {
        /// <summary>
        /// 垂直方向的 Chunk 数量（有效范围 <c>y ∈ [0, VerticalChunkCount)</c>）。
        /// 由组装点按 <c>ceil(WorldHeight / ChunkSize)</c> 推导后显式传入（Runtime §6.4：
        /// 世界高度之外不加载、不生成、不呈现）。
        /// </summary>
        public int VerticalChunkCount = 4;

        /// <summary>每帧最多加载（生成 + 安装）的 Chunk 数（实现规范 §4.4）。</summary>
        public int MaxLoadsPerFrame = 4;

        /// <summary>
        /// 每帧最多完成的「构建 + 呈递」配对数（实现规范 §6.5 / 附录 B N5：
        /// V1 把 Runtime_Core §2.3 的 MaxMeshesBuiltPerFrame 与 MaxUploadsPerFrame 合并为一个配对上限）。
        /// </summary>
        public int MaxChunkPairsPerFrame = 4;

        /// <summary>每帧最多卸载的 Chunk 数（实现规范 §4.3）。</summary>
        public int MaxUnloadsPerFrame = 4;

        /// <summary>
        /// 单 Chunk 自动重试上限（Runtime_Core §9.2-4）。
        /// 首次失败后 <c>FailureCount == 1</c>；超过本值即停止自动重试并进入 <c>Failed</c>。
        /// <c>0</c> 表示"不自动重试"（第一次失败即 Failed）。
        /// </summary>
        public int MaxRetriesPerChunk = 3;

        /// <summary>启动前校验（Runtime §3.5：非法参数拒绝启动，禁止运行中静默修正）。</summary>
        public bool IsValid(out string reason)
        {
            if (VerticalChunkCount < 1)
            {
                reason = "VerticalChunkCount 必须 >= 1（实际 " + VerticalChunkCount + "）";
                return false;
            }

            if (MaxLoadsPerFrame < 1)
            {
                reason = "MaxLoadsPerFrame 必须 >= 1（实际 " + MaxLoadsPerFrame + "）";
                return false;
            }

            if (MaxChunkPairsPerFrame < 1)
            {
                reason = "MaxChunkPairsPerFrame 必须 >= 1（实际 " + MaxChunkPairsPerFrame + "）";
                return false;
            }

            if (MaxUnloadsPerFrame < 1)
            {
                reason = "MaxUnloadsPerFrame 必须 >= 1（实际 " + MaxUnloadsPerFrame + "）";
                return false;
            }

            if (MaxRetriesPerChunk < 0)
            {
                reason = "MaxRetriesPerChunk 必须 >= 0（实际 " + MaxRetriesPerChunk + "）";
                return false;
            }

            reason = null;
            return true;
        }

        /// <summary>坐标是否落在垂直有效范围内（超出 ⇒ 请求被拒绝，Runtime §6.4）。</summary>
        public bool IsWithinVerticalRange(VoxelCoordinate chunkCoordinate)
        {
            return chunkCoordinate.y >= 0 && chunkCoordinate.y < VerticalChunkCount;
        }

        public override string ToString()
        {
            return "RuntimeCoreSettings(VerticalChunkCount=" + VerticalChunkCount +
                   ", MaxLoadsPerFrame=" + MaxLoadsPerFrame +
                   ", MaxChunkPairsPerFrame=" + MaxChunkPairsPerFrame +
                   ", MaxUnloadsPerFrame=" + MaxUnloadsPerFrame +
                   ", MaxRetriesPerChunk=" + MaxRetriesPerChunk + ")";
        }
    }
}
