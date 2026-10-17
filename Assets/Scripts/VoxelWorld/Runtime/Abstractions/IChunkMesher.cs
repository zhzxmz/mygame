using VoxelWorld.Mesh;

namespace VoxelWorld.Runtime.Abstractions
{
    /// <summary>
    /// Mesher 抽象：把"世界拥有的 Chunk（只读）"变成"纯数据几何"。
    ///
    /// 契约来源：
    ///   · VoxelWorld_Runtime_Interface.md §5（输入 / 输出 / Failure / 生命周期 / 所有权）；
    ///   · VoxelWorld_Runtime_Core_Implementation.md §6.1（链路唯一允许的形式）、§6.3（失败保持 Dirty）。
    ///
    /// 硬性条款：
    ///   · **只读输入**：实现不得以任何方式修改 <paramref name="chunk"/>
    ///     （不得 SetVoxel / MarkDirty / ClearDirty / 直接改内部数组）；
    ///   · **纯函数式**：同一输入 ⇒ 同一输出（可用 ComputeContentHash 复现），
    ///     允许内部缓冲复用，但不得把上次调用的状态泄漏到本次输出；
    ///   · **空几何是成功**：Chunk 无实体体素时返回 true + HasGeometry == false 的缓冲；
    ///   · **不得抛异常表达可预期失败**（Runtime_Interface §7 ID-1）；
    ///   · **不保留负载**：返回的 <see cref="VoxelMeshData"/> 由调用方（Core）临时持有，
    ///     实现不得把它当作持久对象。
    ///
    /// 本接口存在的理由：Mesh 层对外是**静态类**，直接调用会让 Runtime Core 依赖具体实现
    /// （Runtime_Interface §5）。适配器 <c>VoxelWorld.Adapters.VoxelMeshGeneratorMesher</c> 是
    /// 唯一允许命名 <c>VoxelMeshGenerator</c> 的位置。
    /// </summary>
    public interface IChunkMesher
    {
        /// <summary>
        /// 为一个数据已就绪的 Chunk 构建纯数据几何。
        /// </summary>
        /// <param name="chunk">世界拥有、数据就绪的 Chunk（**只读**；禁止对 Generating 中的 Chunk 调用）。</param>
        /// <param name="meshData">成功时非 null（可能为空几何，用 HasGeometry 判定）；失败时必须为 null。</param>
        /// <param name="error">失败时非空的原因（禁止静默失败）；成功时为 null。</param>
        /// <returns>true = 构建完成；false = 失败（调用方保持 Dirty，不呈递）。</returns>
        bool TryBuild(VoxelChunk chunk, out VoxelMeshData meshData, out string error);
    }
}
