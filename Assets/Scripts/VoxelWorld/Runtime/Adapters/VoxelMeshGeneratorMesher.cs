using System;
using VoxelWorld.Mesh;
using VoxelWorld.Runtime.Abstractions;

namespace VoxelWorld.Adapters
{
    /// <summary>
    /// <see cref="IChunkMesher"/> 的适配器：包装 Mesh 层的**静态**生成器
    /// <c>VoxelWorld.Mesh.VoxelMeshGenerator</c>。
    ///
    /// 存在理由（VoxelWorld_Runtime_Interface.md §5）：Mesh 层对外是静态类，无法被替换/伪造；
    /// 若 Runtime Core 直接调用它，Core 就会依赖具体实现（违反依赖倒置）。
    /// 因此由本适配器承担唯一的具体依赖 —— **Mesh 层零改动**。
    ///
    /// 契约：
    ///   · 只读输入：不修改传入的 Chunk（Meshing 只读，Mesh §1.2 / I1）；
    ///   · 纯函数式：同输入 ⇒ 同输出（可用 ComputeContentHash 复现）；
    ///   · 空几何是成功：Chunk 无实体体素时返回 true + HasGeometry == false 的缓冲；
    ///   · 失败用返回值表达：捕获异常并转为 <c>false</c> + 原因（禁止异常做控制流）。
    ///
    /// 纯度：本文件是**适配器**，不属于 Runtime Core 的扫描范围（Interface §2.3 四段扫描）；
    /// 它同样是纯 C#，不出现任何 Unity 运行时类型。
    /// </summary>
    public sealed class VoxelMeshGeneratorMesher : IChunkMesher
    {
        public bool TryBuild(VoxelChunk chunk, out VoxelMeshData meshData, out string error)
        {
            meshData = null;

            if (chunk == null)
            {
                error = "Mesh 构建失败：chunk 为 null";
                return false;
            }

            try
            {
                meshData = VoxelMeshGenerator.Generate(chunk);
            }
            catch (Exception ex)
            {
                meshData = null;
                error = "Mesh 构建异常（" + chunk.Coordinate + "）：" +
                        ex.GetType().Name + ": " + ex.Message;
                return false;
            }

            if (meshData == null)
            {
                error = "Mesh 构建失败：生成器返回 null（" + chunk.Coordinate + "）";
                return false;
            }

            error = null;
            return true;
        }
    }
}
