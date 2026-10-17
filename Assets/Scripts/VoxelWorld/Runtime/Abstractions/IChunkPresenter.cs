using VoxelWorld.Mesh;

namespace VoxelWorld.Runtime.Abstractions
{
    /// <summary>
    /// 一次呈递的三种结果（View 规范 §8.1：必须能区分"已呈现 / 已确认空几何 / 失败"）。
    /// </summary>
    public enum PresenterOutcome
    {
        /// <summary>已完整呈现几何。</summary>
        Presented,

        /// <summary>已确认无几何（成功路径，不是失败；View §4.3 / §5.4）。</summary>
        Empty,

        /// <summary>未完成呈递（旧表示保持原样；必须携带原因）。</summary>
        Failed
    }

    /// <summary>
    /// 表示层抽象：Runtime Core ↔ View 之间的**唯一**通道。
    ///
    /// 契约来源：
    ///   · VoxelWorld_Runtime_Interface.md §6（输入 / 输出 / Failure / 生命周期 / 所有权）；
    ///   · VoxelWorld_Runtime_Core_Implementation.md §6（Mesh/View 流程）、§6.2（清脏唯一位置）、§6.5（配对）。
    ///
    /// 硬性条款：
    ///   · **输入只有坐标 + 纯数据负载**：不得要求 Core 传入 Transform / 世界浮点位置 / 相机 / 时间；
    ///   · **不写世界**：实现不得触碰 Voxel 数据与脏标记（清脏由 Core 在 `Apply` 成功之后执行）；
    ///   · **原子替换**：失败时旧表示必须保持不变（View §5.5 / §9.1）；
    ///   · **幂等**：同一坐标同一负载重复 <see cref="Apply"/> 结果一致；<see cref="Release"/> 可重复调用；
    ///   · **不保留负载**：`Apply` 返回后实现不得继续持有 <see cref="VoxelMeshData"/> 引用；
    ///   · **不得抛异常表达可预期失败**（Runtime_Interface §7 ID-1）。
    /// </summary>
    public interface IChunkPresenter
    {
        /// <summary>
        /// 建立或更新一个坐标的表示。
        /// </summary>
        /// <param name="coordinate">Chunk 坐标（整数；摆放规则由实现自行按 coordinate * ChunkSize 计算）。</param>
        /// <param name="meshData">纯数据负载（一次性；实现必须在本次调用内完成复制/上传）。</param>
        /// <param name="error">失败时非空的原因；成功时为 null。</param>
        /// <returns>三态结果；只有 <see cref="PresenterOutcome.Failed"/> 会被 Core 视为失败。</returns>
        PresenterOutcome Apply(VoxelCoordinate coordinate, VoxelMeshData meshData, out string error);

        /// <summary>释放该坐标的表示（幂等；卸载阶段由 Core 无条件调用）。</summary>
        void Release(VoxelCoordinate coordinate);

        /// <summary>只读诊断：当前存活表示槽数量（用于"卸载即净"断言）。</summary>
        int LiveCount { get; }
    }
}
