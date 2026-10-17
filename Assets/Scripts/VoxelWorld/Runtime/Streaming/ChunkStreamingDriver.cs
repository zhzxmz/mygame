using System;
using System.Collections.Generic;

// 命名空间 VoxelWorld 内存在同名类型 VoxelWorld.VoxelWorld；
// 用别名显式指向"世界真值容器"以调用其坐标换算（不得自实现坐标规则，Mining §1.1）。
using VoxelWorldCore = global::VoxelWorld.VoxelWorld;

namespace VoxelWorld.Runtime.Streaming
{
    /// <summary>
    /// ChunkStreamingDriver：玩家驱动的 Chunk 流式加载求解器（**纯 C#**）。
    ///
    /// 完整链路（本类的职责边界）：
    /// <code>
    /// Player Position ──▶ VoxelCore 坐标换算 ──▶ focusChunk
    ///                          │
    ///                          ▼
    ///          期望加载集合（R_load）/ 保留集合（R_unload）求解
    ///                          │
    ///                          ▼
    ///              IChunkRequestSink.RequestChunk / ReleaseChunk
    ///                          │
    ///                          ▼
    ///                  （调用方随后执行 VoxelRuntimeCore.Tick()）
    /// </code>
    ///
    /// 规范来源：
    ///   · VoxelWorld_Runtime.md §6（流式加载与卸载：判定式、排序、滞回、释放顺序）；
    ///   · VoxelWorld_Runtime_Core_Implementation.md §4.2/§4.3（目标集合与卸载集合的语义）；
    ///   · VoxelWorld_Runtime_Interface.md §1（依赖抽象、不依赖具体实现）。
    ///
    /// 硬性条款：
    ///   1. **坐标换算只用 VoxelCore**（<c>WorldToChunkCoordinate</c>，floor 除法，负坐标正确）；
    ///   2. **不改世界数据**：不写 Voxel、不碰脏标记（"加载/卸载"由 Core 在 Tick 内完成）；
    ///   3. **期望集合 ⊆ 保留集合**；两者之间保持现状（滞回），既不请求也不释放；
    ///   4. **确定性**：请求顺序 = (距离², x, y, z) 升序（最近优先）；释放顺序 = 同键降序（最远先释放）；
    ///      不使用时间、随机数、字典遍历顺序；
    ///   5. **只释放自己请求过的坐标**（自持 <c>streamed</c> 集合）——不影响其它来源的请求；
    ///   6. **失败可观测**：被上游拒绝的请求计入报告与累计计数，禁止静默丢弃。
    ///
    /// V1 范围：同步、单线程、每次 <see cref="Update"/> 只求解并提交请求集合（不做 Tick、不做异步、
    /// 不做存档、不做 Mining / Structure）。
    /// </summary>
    public sealed class ChunkStreamingDriver
    {
        private readonly IChunkRequestSink sink;
        private readonly ChunkStreamingSettings settings;

        /// <summary>本驱动自己请求过的坐标（只释放这些，避免影响其它请求来源）。</summary>
        private readonly HashSet<VoxelCoordinate> streamed =
            new HashSet<VoxelCoordinate>();

        /// <summary>水平候选偏移（[dx, dz, d²]），按 (d², dx, dz) 升序 = 近 → 远。</summary>
        private readonly List<int[]> loadOffsets = new List<int[]>();

        private readonly List<VoxelCoordinate> requestBuffer = new List<VoxelCoordinate>();
        private readonly List<VoxelCoordinate> releaseBuffer = new List<VoxelCoordinate>();
        private readonly List<VoxelCoordinate> snapshotBuffer = new List<VoxelCoordinate>();
        private readonly Comparison<VoxelCoordinate> releaseComparison;

        private VoxelCoordinate focusChunk;
        private int updateIndex;

        public ChunkStreamingDriver(IChunkRequestSink sink, ChunkStreamingSettings settings)
        {
            if (sink == null) throw new ArgumentNullException("sink");
            if (settings == null) throw new ArgumentNullException("settings");

            string reason;
            if (!settings.IsValid(out reason))
            {
                throw new ArgumentException("ChunkStreamingSettings 非法：" + reason, "settings");
            }

            this.sink = sink;
            this.settings = settings;
            this.releaseComparison = CompareFarthestFirst;

            BuildLoadOffsets();
        }

        public ChunkStreamingSettings Settings
        {
            get { return settings; }
        }

        /// <summary>当前焦点 Chunk 坐标（未更新过时为原点）。</summary>
        public VoxelCoordinate FocusChunk
        {
            get { return focusChunk; }
        }

        /// <summary>已请求集合的大小。</summary>
        public int StreamedCount
        {
            get { return streamed.Count; }
        }

        public int UpdatesTotal { get; private set; }

        public int RequestedTotal { get; private set; }

        public int ReleasedTotal { get; private set; }

        public int RejectedTotal { get; private set; }

        /// <summary>已请求坐标快照（升序）。</summary>
        public IList<VoxelCoordinate> GetStreamedCoordinates()
        {
            return BuildSortedSnapshot(streamed);
        }

        /// <summary>
        /// 当前焦点下的"期望加载集合"快照（升序）——与是否已请求无关，可用于独立校验。
        /// </summary>
        public IList<VoxelCoordinate> GetDesiredCoordinates()
        {
            List<VoxelCoordinate> result = new List<VoxelCoordinate>();
            int vertical = settings.VerticalChunkCount;

            for (int i = 0; i < loadOffsets.Count; i++)
            {
                int[] offset = loadOffsets[i];
                int x = focusChunk.x + offset[0];
                int z = focusChunk.z + offset[1];

                for (int y = 0; y < vertical; y++)
                {
                    result.Add(new VoxelCoordinate(x, y, z));
                }
            }

            result.Sort(CompareCoordinate);
            return result;
        }

        /// <summary>
        /// 用玩家世界坐标更新流式请求集合：
        ///   位置 →（VoxelCore 换算）→ focusChunk → 求解 → Request/Release。
        /// 调用方在返回后执行 <c>VoxelRuntimeCore.Tick()</c> 完成实际的加载/卸载。
        /// </summary>
        public ChunkStreamingReport Update(VoxelCoordinate playerWorldPosition)
        {
            updateIndex++;
            UpdatesTotal++;

            // 焦点换算：必须用 VoxelCore（floor 除法、负坐标正确），不得自实现坐标规则
            focusChunk = VoxelWorldCore.WorldToChunkCoordinate(playerWorldPosition);

            ChunkStreamingReport report = new ChunkStreamingReport();
            report.UpdateIndex = updateIndex;
            report.FocusChunk = focusChunk;

            int vertical = settings.VerticalChunkCount;
            report.DesiredCount = loadOffsets.Count * vertical;

            // ---- 1) 期望加载集合：升序（近 → 远），只提交"尚未请求"的坐标 ----
            requestBuffer.Clear();
            for (int i = 0; i < loadOffsets.Count; i++)
            {
                int[] offset = loadOffsets[i];
                int x = focusChunk.x + offset[0];
                int z = focusChunk.z + offset[1];

                for (int y = 0; y < vertical; y++)
                {
                    VoxelCoordinate chunk = new VoxelCoordinate(x, y, z);
                    if (!streamed.Contains(chunk)) requestBuffer.Add(chunk);
                }
            }

            // ---- 2) 释放候选：保留集合之外；降序（远 → 近）----
            releaseBuffer.Clear();
            int retainedInBand = 0;

            foreach (VoxelCoordinate chunk in streamed)
            {
                int distanceSquared =
                    ChunkStreamingSettings.HorizontalDistanceSquared(chunk, focusChunk);

                if (distanceSquared <= settings.UnloadDistanceSquared)
                {
                    if (distanceSquared > settings.LoadDistanceSquared) retainedInBand++;
                    continue;
                }

                releaseBuffer.Add(chunk);
            }

            releaseBuffer.Sort(releaseComparison);
            report.RetainedInHysteresisBand = retainedInBand;

            // ---- 3) 提交请求（失败必须可观测，禁止静默丢弃）----
            int requested = 0;
            int rejected = 0;

            for (int i = 0; i < requestBuffer.Count; i++)
            {
                VoxelCoordinate chunk = requestBuffer[i];
                if (sink.RequestChunk(chunk))
                {
                    streamed.Add(chunk);
                    requested++;
                }
                else
                {
                    rejected++;
                }
            }

            int released = 0;
            for (int i = 0; i < releaseBuffer.Count; i++)
            {
                VoxelCoordinate chunk = releaseBuffer[i];
                if (sink.ReleaseChunk(chunk)) released++;
                streamed.Remove(chunk);   // 无论上游是否报告"确实撤销"，本地记录都必须移除
            }

            report.RequestedThisUpdate = requested;
            report.ReleasedThisUpdate = released;
            report.RejectedThisUpdate = rejected;
            report.StreamedCount = streamed.Count;

            RequestedTotal += requested;
            ReleasedTotal += released;
            RejectedTotal += rejected;

            return report;
        }

        /// <summary>清空本地已请求记录（**不**通知上游；仅用于拆除/诊断）。</summary>
        public void ResetTracking()
        {
            streamed.Clear();
        }

        // -----------------------------------------------------------------
        // 内部
        // -----------------------------------------------------------------

        private void BuildLoadOffsets()
        {
            loadOffsets.Clear();

            int radius = settings.ViewDistanceChunks;
            int radiusSquared = settings.LoadDistanceSquared;

            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dz = -radius; dz <= radius; dz++)
                {
                    int distanceSquared = dx * dx + dz * dz;
                    if (distanceSquared > radiusSquared) continue;

                    loadOffsets.Add(new int[] { dx, dz, distanceSquared });
                }
            }

            loadOffsets.Sort(CompareOffset);
        }

        private static int CompareOffset(int[] a, int[] b)
        {
            if (a[2] != b[2]) return a[2].CompareTo(b[2]);
            if (a[0] != b[0]) return a[0].CompareTo(b[0]);
            return a[1].CompareTo(b[1]);
        }

        private static int CompareCoordinate(VoxelCoordinate a, VoxelCoordinate b)
        {
            if (a.x != b.x) return a.x.CompareTo(b.x);
            if (a.y != b.y) return a.y.CompareTo(b.y);
            return a.z.CompareTo(b.z);
        }

        /// <summary>卸载顺序：水平距离² 降序（最远先释放），同距离按 (x, y, z) 降序（全序、无并列歧义）。</summary>
        private int CompareFarthestFirst(VoxelCoordinate a, VoxelCoordinate b)
        {
            int distanceA = ChunkStreamingSettings.HorizontalDistanceSquared(a, focusChunk);
            int distanceB = ChunkStreamingSettings.HorizontalDistanceSquared(b, focusChunk);

            if (distanceA != distanceB) return distanceB.CompareTo(distanceA);

            if (a.x != b.x) return b.x.CompareTo(a.x);
            if (a.y != b.y) return b.y.CompareTo(a.y);
            return b.z.CompareTo(a.z);
        }

        private IList<VoxelCoordinate> BuildSortedSnapshot(HashSet<VoxelCoordinate> source)
        {
            snapshotBuffer.Clear();

            foreach (VoxelCoordinate chunk in source)
            {
                snapshotBuffer.Add(chunk);
            }

            snapshotBuffer.Sort(CompareCoordinate);

            return new List<VoxelCoordinate>(snapshotBuffer);
        }

        public override string ToString()
        {
            return "ChunkStreamingDriver(updates=" + UpdatesTotal +
                   ", streamed=" + streamed.Count +
                   ", requested=" + RequestedTotal +
                   ", released=" + ReleasedTotal +
                   ", rejected=" + RejectedTotal + ")";
        }
    }
}
