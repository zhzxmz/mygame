namespace VoxelWorld
{
    /// <summary>
    /// 体素类型（Voxel World Core V1 §2 的 Voxel State）。
    ///
    /// 规则（由 <see cref="Voxel.IsSolid"/> 统一判定）：
    ///   · Air   = 空、无碰撞；
    ///   · 其它  = 实体、有碰撞。
    ///
    /// 注意：Air 显式为 0，保证 default(VoxelType) / default(Voxel) 是空气，
    /// 与“未写入 = 空”的直觉一致（真正的“未生成”由世界侧表达，见 VoxelWorld）。
    /// </summary>
    public enum VoxelType
    {
        Air = 0,
        Grass = 1,
        Dirt = 2,
        Stone = 3
    }
}
