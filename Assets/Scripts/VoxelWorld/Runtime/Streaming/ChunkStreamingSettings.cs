namespace VoxelWorld.Runtime.Streaming
{
    /// <summary>
    /// Chunk Streaming 的显式参数（Runtime §3：所有影响调度的参数必须显式，禁止隐藏全局状态）。
    ///
    /// 判定式（Runtime §6.1，整数运算、可独立重算）：
    /// <code>
    /// focusChunk = VoxelWorld.WorldToChunkCoordinate(playerWorldPosition)     // VoxelCore floor 除法
    /// dx = c.x - focusChunk.x ,  dz = c.z - focusChunk.z
    /// 期望加载集合 = { c | dx² + dz² ≤ R_load²  ∧  c.y ∈ [0, VerticalChunkCount) }
    /// 保留集合     = { c | dx² + dz² ≤ R_unload² }        R_unload = R_load + Hysteresis
    /// </code>
    /// 期望加载集合 ⊆ 保留集合；两者之间的区域 **保持现状**（不请求、不释放）——这就是滞回的意义。
    ///
    /// 纯度：纯 C#、整数运算（无浮点、无 Unity 类型），可在无场景环境下验证。
    /// </summary>
    public sealed class ChunkStreamingSettings
    {
        /// <summary>水平加载半径 R_load（Chunk 单位，≥ 0）。</summary>
        public int ViewDistanceChunks = 2;

        /// <summary>卸载滞回（Chunk 单位，≥ 0）：R_unload = R_load + Hysteresis。</summary>
        public int UnloadHysteresisChunks = 1;

        /// <summary>
        /// 垂直方向的 Chunk 数量（加载 <c>y ∈ [0, VerticalChunkCount)</c>，即 Runtime §6.1 的
        /// <c>VerticalMode.Full</c> 整列加载）。**必须与 <c>RuntimeCoreSettings.VerticalChunkCount</c> 一致**，
        /// 否则越界请求会被 Core 拒绝（拒绝会被计数，不会静默）。
        /// </summary>
        public int VerticalChunkCount = 4;

        /// <summary>R_load²（判定用整数）。</summary>
        public int LoadDistanceSquared
        {
            get { return ViewDistanceChunks * ViewDistanceChunks; }
        }

        /// <summary>R_unload = R_load + Hysteresis。</summary>
        public int UnloadDistanceChunks
        {
            get { return ViewDistanceChunks + UnloadHysteresisChunks; }
        }

        /// <summary>R_unload²（判定用整数）。</summary>
        public int UnloadDistanceSquared
        {
            get { return UnloadDistanceChunks * UnloadDistanceChunks; }
        }

        /// <summary>启动前校验（Runtime §3.5：非法参数拒绝启动，禁止运行中静默修正）。</summary>
        public bool IsValid(out string reason)
        {
            if (ViewDistanceChunks < 0)
            {
                reason = "ViewDistanceChunks 必须 >= 0（实际 " + ViewDistanceChunks + "）";
                return false;
            }

            if (UnloadHysteresisChunks < 0)
            {
                reason = "UnloadHysteresisChunks 必须 >= 0（实际 " +
                         UnloadHysteresisChunks + "）";
                return false;
            }

            if (VerticalChunkCount < 1)
            {
                reason = "VerticalChunkCount 必须 >= 1（实际 " + VerticalChunkCount + "）";
                return false;
            }

            reason = null;
            return true;
        }

        /// <summary>水平距离平方（整数；只比较水平面，不含垂直层）。</summary>
        public static int HorizontalDistanceSquared(
            VoxelCoordinate chunk, VoxelCoordinate focusChunk)
        {
            int dx = chunk.x - focusChunk.x;
            int dz = chunk.z - focusChunk.z;
            return dx * dx + dz * dz;
        }

        /// <summary>坐标的垂直层是否在有效范围内（Runtime §6.4：世界高度之外不加载）。</summary>
        public bool IsWithinVerticalRange(VoxelCoordinate chunk)
        {
            return chunk.y >= 0 && chunk.y < VerticalChunkCount;
        }

        /// <summary>是否属于"期望加载集合"。</summary>
        public bool IsWithinLoadRange(VoxelCoordinate chunk, VoxelCoordinate focusChunk)
        {
            if (!IsWithinVerticalRange(chunk)) return false;
            return HorizontalDistanceSquared(chunk, focusChunk) <= LoadDistanceSquared;
        }

        /// <summary>是否属于"保留集合"（超出即可卸载）。</summary>
        public bool IsWithinUnloadRange(VoxelCoordinate chunk, VoxelCoordinate focusChunk)
        {
            return HorizontalDistanceSquared(chunk, focusChunk) <= UnloadDistanceSquared;
        }

        public override string ToString()
        {
            return "ChunkStreamingSettings(R_load=" + ViewDistanceChunks +
                   ", hysteresis=" + UnloadHysteresisChunks +
                   ", R_unload=" + UnloadDistanceChunks +
                   ", VerticalChunkCount=" + VerticalChunkCount + ")";
        }
    }
}
