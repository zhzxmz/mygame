using System;

namespace VoxelWorld.Generation
{
    /// <summary>
    /// V1 基础地形生成器（World Generation Core V1 §4：Height based terrain）。
    ///
    /// 范围（严格限制）：
    ///   输入  Seed + ChunkCoordinate（+ 显式参数） ⇒ 输出 VoxelChunk。
    ///   **不**生成 Mesh / 不渲染 / 不涉及玩家、挖掘、洞穴、生物群系、矿物、结构。
    ///
    /// 生成规则（逐 Voxel，世界坐标 y 与地表高度 height 比较）：
    ///   y &gt; height                     → Air
    ///   y == height                     → Grass
    ///   height-SoilDepth &lt; y &lt; height    → Dirt
    ///   更低（y &lt;= height-SoilDepth）    → Stone
    ///   另外：y &gt;= WorldHeight          → Air（世界之上）
    ///
    /// 确定性（V1 §2/§6）：
    ///   · 只依赖 (Seed, ChunkCoordinate, settings)，无随机数状态、无时间/帧序依赖；
    ///   · Chunk 可独立生成：结果与生成顺序、相邻 Chunk 是否加载无关。
    /// 坐标（V1 §5）：世界坐标由 <see cref="VoxelChunk.LocalToWorldCoordinate"/>
    ///   换算（不依赖 Unity Transform），保证与 VoxelCore 的坐标契约完全一致。
    /// </summary>
    public sealed class BasicTerrainGenerator : IWorldGenerator
    {
        private readonly WorldGenerationSettings settings;

        public BasicTerrainGenerator(WorldGenerationSettings settings)
        {
            string reason;
            if (!settings.IsValid(out reason))
            {
                throw new ArgumentException(
                    "WorldGenerationSettings 非法：" + reason, "settings");
            }

            this.settings = settings;
        }

        /// <summary>生成参数（只读）。</summary>
        public WorldGenerationSettings Settings
        {
            get { return settings; }
        }

        /// <summary>
        /// 生成一个 Chunk 的初始体素数据。
        /// 同参数下重复调用 / 任意顺序调用，结果完全一致。
        /// </summary>
        public VoxelChunk Generate(VoxelCoordinate chunkCoordinate)
        {
            VoxelChunk chunk = new VoxelChunk(chunkCoordinate);

            for (int localX = 0; localX < VoxelChunk.ChunkSize; localX++)
            {
                for (int localY = 0; localY < VoxelChunk.ChunkSize; localY++)
                {
                    for (int localZ = 0; localZ < VoxelChunk.ChunkSize; localZ++)
                    {
                        // 世界坐标：交给 VoxelCore 的换算，避免自行实现坐标规则。
                        VoxelCoordinate world = chunk.LocalToWorldCoordinate(
                            localX, localY, localZ);

                        int terrainHeight = HeightFunction.GetTerrainHeight(
                            world.x, world.z, settings);

                        VoxelType type = ClassifyVoxel(
                            world.y, terrainHeight, settings);

                        chunk.SetVoxel(localX, localY, localZ, type);
                    }
                }
            }

            return chunk;
        }

        /// <summary>
        /// 单个体素的判定规则（公开为纯函数，便于测试与后续阶段复用）：
        ///   世界之上（y &gt;= WorldHeight）→ Air；
        ///   y &gt; height → Air；y == height → Grass；
        ///   height-SoilDepth &lt; y &lt; height → Dirt；更低 → Stone。
        /// </summary>
        public static VoxelType ClassifyVoxel(
            int worldY,
            int terrainHeight,
            WorldGenerationSettings settings)
        {
            if (worldY >= settings.WorldHeight) return VoxelType.Air;
            if (worldY > terrainHeight) return VoxelType.Air;
            if (worldY == terrainHeight) return VoxelType.Grass;
            if (worldY > terrainHeight - settings.SoilDepth)
            {
                return VoxelType.Dirt;
            }

            return VoxelType.Stone;
        }

        /// <summary>便捷方法：计算某世界列的地表高度（等价于 HeightFunction.GetTerrainHeight）。</summary>
        public int GetTerrainHeight(int worldX, int worldZ)
        {
            return HeightFunction.GetTerrainHeight(worldX, worldZ, settings);
        }
    }
}
