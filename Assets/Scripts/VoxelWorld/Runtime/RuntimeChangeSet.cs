using System.Collections.Generic;

namespace VoxelWorld.Runtime
{
    /// <summary>
    /// 改动集（Runtime §7.1 / Runtime_Core §5.4）：<c>世界体素坐标 → 最终类型</c>，**稀疏、覆盖式**。
    ///
    /// 语义：
    ///   · 只记录"与生成结果不同"的最终值（同坐标只保留最后一次写入的值）；
    ///   · 与 `Seed + RuntimeCoreSettings` 一起构成**最小可持久化状态**（不保存 Mesh / 体素副本 / 表示）；
    ///   · 安装流程（Generate → Validate → Install → **叠加改动集** → Register）必须在本集合**之后**叠加，
    ///     以保证"生成不覆盖改动"（WorldGeneration_Core §9）。
    ///
    /// **V1 说明**：V1 **不提供编辑入口**（编辑规则属 VoxelWorld_Mining.md，V1 不实现 Mining），
    /// 因此本集合在 V1 中**恒为空**。保留 <see cref="Record"/> 与叠加步骤是为了让 V2 接入 Mining 时
    /// **无需改动安装流程**（实现规范 §4.5 / 附录 B N6）。
    ///
    /// 纯度：纯 C#、无 Unity 类型；对外只读遍历按世界坐标升序（确定性）。
    /// </summary>
    public sealed class RuntimeChangeSet
    {
        private readonly Dictionary<VoxelCoordinate, VoxelType> entries =
            new Dictionary<VoxelCoordinate, VoxelType>();

        private readonly List<VoxelCoordinate> sortBuffer = new List<VoxelCoordinate>();

        /// <summary>条目数量（V1 恒为 0）。</summary>
        public int Count
        {
            get { return entries.Count; }
        }

        /// <summary>
        /// 记录一个改动（覆盖式）。**V1 无调用者**：编辑入口属 Mining（V1 不实现）。
        /// 该方法只写入改动集，**不触碰世界数据**（写世界必须经 VoxelWorld 的写入 API）。
        /// </summary>
        public void Record(VoxelCoordinate world, VoxelType type)
        {
            entries[world] = type;
        }

        /// <summary>读取某世界坐标的最终改动值。</summary>
        public bool TryGet(VoxelCoordinate world, out VoxelType type)
        {
            return entries.TryGetValue(world, out type);
        }

        public void Clear()
        {
            entries.Clear();
        }

        /// <summary>
        /// 取属于指定 Chunk 的全部改动条目，按**世界坐标升序**追加到 <paramref name="results"/>
        /// （安装流程的叠加步骤按此顺序写入，保证确定性）。
        /// </summary>
        public void CollectForChunk(
            VoxelCoordinate chunkCoordinate,
            List<VoxelCoordinate> results)
        {
            if (results == null) return;

            sortBuffer.Clear();

            foreach (KeyValuePair<VoxelCoordinate, VoxelType> pair in entries)
            {
                if (VoxelWorld.WorldToChunkCoordinate(pair.Key) == chunkCoordinate)
                {
                    sortBuffer.Add(pair.Key);
                }
            }

            sortBuffer.Sort(CompareCoordinate);

            for (int i = 0; i < sortBuffer.Count; i++)
            {
                results.Add(sortBuffer[i]);
            }
        }

        internal static int CompareCoordinate(VoxelCoordinate a, VoxelCoordinate b)
        {
            if (a.x != b.x) return a.x.CompareTo(b.x);
            if (a.y != b.y) return a.y.CompareTo(b.y);
            return a.z.CompareTo(b.z);
        }

        public override string ToString()
        {
            return "RuntimeChangeSet(count=" + entries.Count + ")";
        }
    }
}
