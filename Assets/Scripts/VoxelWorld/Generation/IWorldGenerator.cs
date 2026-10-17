namespace VoxelWorld.Generation
{
    /// <summary>
    /// 世界生成器接口（World Generation Core V1 §3/§6/§8）。
    ///
    /// 契约：
    ///   · 输入 = ChunkCoordinate（不假设生成顺序、不依赖相邻 Chunk 是否已加载）；
    ///   · 输出 = 一个已写入初始体素数据的 <see cref="VoxelChunk"/>（世界真值，非 Mesh）；
    ///   · 生成必须是确定性的：同 Seed + 同 ChunkCoordinate + 同参数 ⇒ 同结果
    ///     （V1 §2，且不得依赖执行顺序 / 加载顺序 / 帧时间）；
    ///   · 生成只产出“初始世界状态”，不覆盖玩家修改（V1 §9：运行时改动另行存放）；
    ///   · 不生成 Mesh、不依赖 Unity Transform（V1 §5/§8）。
    ///
    /// 后续阶段（洞穴 / 生物群系 / 结构 / 矿物）作为独立 generation stage 叠加，
    /// 不需要修改 VoxelCore（V1 §7/§10）。
    /// </summary>
    public interface IWorldGenerator
    {
        /// <summary>生成参数（显式、只读）。</summary>
        WorldGenerationSettings Settings { get; }

        /// <summary>
        /// 生成指定 Chunk 的初始体素数据。
        /// 同一参数下可任意顺序、任意次数调用，结果必须一致。
        /// </summary>
        VoxelChunk Generate(VoxelCoordinate chunkCoordinate);
    }
}
