using System;

namespace VoxelWorld.Generation
{
    /// <summary>
    /// 世界生成参数（World Generation Core V1 §3：参数必须显式、不得有隐藏全局状态）。
    ///
    /// 字段全部显式存在，含义如下：
    ///   · Seed             —— 世界种子，决定全部地形（同 Seed 同坐标同参数 ⇒ 同结果）；
    ///   · TerrainHeight    —— 平均地表高度（surface layer 所在的 Voxel y）；
    ///   · SoilDepth        —— 土壤层（Dirt）层数：占据 y ∈ (height-SoilDepth, height)；
    ///   · WorldHeight      —— 世界竖直方向体素数（可生成 y ∈ [0, WorldHeight-1]；
    ///                          y &gt;= WorldHeight 一律 Air）；
    ///   · TerrainFrequency —— 地形噪声频率（每 1 体素采样步长，&gt; 0）；
    ///   · TerrainAmplitude —— 地形起伏幅度（± 体素数，&gt;= 0）。
    ///
    /// 该结构是纯数据（不可变语义由调用方遵守：生成器只读取，不写回）。
    /// </summary>
    [Serializable]
    public struct WorldGenerationSettings
    {
        /// <summary>世界种子。</summary>
        public int Seed;

        /// <summary>平均地表高度（Grass 所在的 Voxel y）。</summary>
        public int TerrainHeight;

        /// <summary>土壤层（Dirt）层数：y ∈ (TerrainHeight - SoilDepth, TerrainHeight)。</summary>
        public int SoilDepth;

        /// <summary>世界竖直体素数：有效范围 [0, WorldHeight-1]。</summary>
        public int WorldHeight;

        /// <summary>噪声频率（每体素），必须 &gt; 0。</summary>
        public float TerrainFrequency;

        /// <summary>起伏幅度（± 体素数），必须 &gt;= 0。</summary>
        public float TerrainAmplitude;

        public WorldGenerationSettings(
            int seed,
            int terrainHeight,
            int soilDepth,
            int worldHeight,
            float terrainFrequency,
            float terrainAmplitude)
        {
            Seed = seed;
            TerrainHeight = terrainHeight;
            SoilDepth = soilDepth;
            WorldHeight = worldHeight;
            TerrainFrequency = terrainFrequency;
            TerrainAmplitude = terrainAmplitude;
        }

        /// <summary>默认参数（示例值，非隐藏状态：调用方需显式传入或显式使用本默认值）。</summary>
        public static WorldGenerationSettings Default
        {
            get
            {
                return new WorldGenerationSettings(
                    seed: 12345,
                    terrainHeight: 24,
                    soilDepth: 3,
                    worldHeight: 64,
                    terrainFrequency: 0.06f,
                    terrainAmplitude: 6f
                );
            }
        }

        /// <summary>参数合法性检查（生成器构造时会强制校验，非法直接抛异常）。</summary>
        public bool IsValid(out string reason)
        {
            if (WorldHeight < 1)
            {
                reason = "WorldHeight 必须 >= 1（实际 " + WorldHeight + "）。";
                return false;
            }

            if (TerrainHeight < 0 || TerrainHeight >= WorldHeight)
            {
                reason = "TerrainHeight 必须在 [0, WorldHeight-1] 内（实际 " +
                         TerrainHeight + " / WorldHeight=" + WorldHeight + "）。";
                return false;
            }

            if (SoilDepth < 0)
            {
                reason = "SoilDepth 必须 >= 0（实际 " + SoilDepth + "）。";
                return false;
            }

            if (TerrainFrequency <= 0f)
            {
                reason = "TerrainFrequency 必须 > 0（实际 " + TerrainFrequency + "）。";
                return false;
            }

            if (TerrainAmplitude < 0f)
            {
                reason = "TerrainAmplitude 必须 >= 0（实际 " + TerrainAmplitude + "）。";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        public override string ToString()
        {
            return "WorldGenerationSettings(Seed=" + Seed +
                   ", TerrainHeight=" + TerrainHeight +
                   ", SoilDepth=" + SoilDepth +
                   ", WorldHeight=" + WorldHeight +
                   ", TerrainFrequency=" + TerrainFrequency +
                   ", TerrainAmplitude=" + TerrainAmplitude + ")";
        }
    }
}
