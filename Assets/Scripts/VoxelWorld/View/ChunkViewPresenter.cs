using System;
using System.Collections.Generic;
using VoxelWorld.Mesh;
using VoxelWorld.Runtime.Abstractions;

namespace VoxelWorld.View
{
    /// <summary>
    /// <see cref="IChunkPresenter"/> 的 Unity 侧适配器：把"坐标 + 纯数据负载"翻译成
    /// <see cref="VoxelChunkView"/> 的建立 / 更新 / 释放，并向 Core 报告三态结果。
    ///
    /// 契约（VoxelWorld_Runtime_Interface.md §6）：
    ///   · 输入只有坐标 + 负载（不要求 Core 传 Transform / 世界浮点位置 / 相机 / 时间）；
    ///   · **不写世界数据、不碰脏标记**（清脏由 Core 在 Apply 成功之后执行）；
    ///   · **原子替换**：失败时旧表示保持不变（由 VoxelChunkView 保证，View §5.5）；
    ///   · **幂等**：同一坐标可多次 Apply；Release 可重复调用；
    ///   · **不保留负载**：Apply 内部完成上传，返回后不再持有 VoxelMeshData 引用；
    ///   · **失败必须带原因**（View §9.1）。
    ///
    /// 表示槽（coordinate → VoxelChunkView）由本类拥有；<see cref="LiveCount"/> 暴露槽数量，
    /// 供"卸载即净"（LiveCount 回基线）断言使用。
    /// </summary>
    public sealed class ChunkViewPresenter : IChunkPresenter
    {
        private readonly ChunkViewSettings settings;
        private readonly Dictionary<VoxelCoordinate, VoxelChunkView> views =
            new Dictionary<VoxelCoordinate, VoxelChunkView>();

        public ChunkViewPresenter(ChunkViewSettings settings)
        {
            if (settings == null) throw new ArgumentNullException("settings");

            string reason;
            if (!settings.IsValid(out reason))
            {
                throw new ArgumentException("ChunkViewSettings 非法：" + reason, "settings");
            }

            this.settings = settings;
        }

        /// <summary>当前表示槽数量（含尚未成功呈现、但已建立的槽；失败槽仍会被 Release 清理）。</summary>
        public int LiveCount
        {
            get { return views.Count; }
        }

        /// <summary>
        /// 建立或更新一个坐标的表示。
        /// 成功（Presented / Empty）⇒ 该坐标的表示与负载一致；失败 ⇒ 旧表示保持不变。
        /// </summary>
        public PresenterOutcome Apply(
            VoxelCoordinate coordinate,
            VoxelMeshData meshData,
            out string error)
        {
            if (meshData == null)
            {
                error = "Apply 失败：meshData 为 null（" + coordinate + "）";
                return PresenterOutcome.Failed;
            }

            VoxelChunkView view;
            if (!views.TryGetValue(coordinate, out view) || view == null || view.IsReleased)
            {
                try
                {
                    view = new VoxelChunkView(coordinate, settings);
                }
                catch (Exception ex)
                {
                    error = "创建 VoxelChunkView 失败（" + coordinate + "）：" +
                            ex.GetType().Name + ": " + ex.Message;
                    return PresenterOutcome.Failed;
                }

                views[coordinate] = view;
            }

            string applyError;
            ChunkViewApplyResult result = view.TryApply(meshData, out applyError);

            if (result == ChunkViewApplyResult.Failed)
            {
                error = applyError;
                return PresenterOutcome.Failed;   // 槽位保留（旧表示仍由该 View 持有）
            }

            error = null;
            return result == ChunkViewApplyResult.Empty
                ? PresenterOutcome.Empty
                : PresenterOutcome.Presented;
        }

        /// <summary>释放该坐标的表示（幂等；卸载阶段由 Core 无条件调用）。</summary>
        public void Release(VoxelCoordinate coordinate)
        {
            VoxelChunkView view;
            if (!views.TryGetValue(coordinate, out view)) return;

            views.Remove(coordinate);
            if (view != null) view.Release();
        }

        /// <summary>释放全部表示（拆场 / 测试收尾用；不触碰世界数据）。</summary>
        public void ReleaseAll()
        {
            List<VoxelCoordinate> coordinates = new List<VoxelCoordinate>(views.Keys);
            coordinates.Sort(CompareCoordinate);

            for (int i = 0; i < coordinates.Count; i++)
            {
                Release(coordinates[i]);
            }
        }

        /// <summary>只读查询：取某坐标的表示（供诊断与自动化验证读取，禁止写世界）。</summary>
        public bool TryGetView(VoxelCoordinate coordinate, out VoxelChunkView view)
        {
            return views.TryGetValue(coordinate, out view);
        }

        private static int CompareCoordinate(VoxelCoordinate a, VoxelCoordinate b)
        {
            if (a.x != b.x) return a.x.CompareTo(b.x);
            if (a.y != b.y) return a.y.CompareTo(b.y);
            return a.z.CompareTo(b.z);
        }

        public override string ToString()
        {
            return "ChunkViewPresenter(live=" + views.Count + ")";
        }
    }
}
