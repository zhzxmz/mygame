namespace VoxelWorld.Runtime
{
    /// <summary>
    /// Chunk 生命周期状态（Runtime_Core §3.1；实现规范 §2.6）。
    ///
    /// 状态名与上游规范**完全一致，不得改名、不得新增状态**。
    ///
    /// 帧末闭合（RCI9 / Runtime_Core §1.4）：任一 <c>Tick()</c> 返回时，权威注册表中
    /// **不存在** <see cref="Generating"/> / <see cref="Building"/> / <see cref="Unloading"/>
    /// —— 三者都是 Tick 内部的瞬态状态。
    ///
    /// `Absent` 不是枚举成员：**不在注册表中**即视为 `Absent`（未加载；Runtime_Core §3.1）。
    /// </summary>
    public enum ChunkLifecycleState
    {
        /// <summary>正在生成 + 校验 + 安装（Tick 内瞬态，不跨帧，不进入权威注册表）。</summary>
        Generating,

        /// <summary>数据就绪且 <c>IsDirty == true</c>（表示缺失或过期），等待构建 + 呈递。</summary>
        Dirty,

        /// <summary>正在 Meshing + 呈递（Tick 内瞬态，必须与本 Chunk 的呈递配对完成）。</summary>
        Building,

        /// <summary>表示与当前数据一致（<c>IsDirty == false</c>）。</summary>
        Ready,

        /// <summary>正在释放表示与数据（Tick 内瞬态；完成后记录被删除）。</summary>
        Unloading,

        /// <summary>
        /// 生成 / 校验 / 安装 / Meshing / 呈递失败，且已达到重试判据上限
        /// （停止自动重试；**仍保持 Chunk.IsDirty == true**，需显式 RetryFailedChunks）。
        /// </summary>
        Failed
    }
}
