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
    ///   · SeaLevel         —— 海平面（世界体素 y，合法范围 [0, WorldHeight]；默认 64）。
    ///                          契约：`SeaLevel` 属**配置（世界创建期）**，由海陆判定
    ///                          `Ocean = Elevation &lt; SeaLevel` 消费（严格小于）；
    ///   · TerrainFrequency —— 地形噪声频率（每 1 体素采样步长，&gt; 0）；
    ///   · TerrainAmplitude —— 地形起伏幅度（± 体素数，&gt;= 0）。
    ///
    /// 该结构是纯数据（不可变语义由调用方遵守：生成器只读取，不写回）。
    /// </summary>
    [Serializable]
    public struct WorldGenerationSettings
    {
        /// <summary>
        /// `SeaLevel` 的登记默认值（契约表 C：默认 `64`）。
        /// 旧六参数构造函数使用该值，保证 SeaLevel 迁移**不改变既有行为**。
        /// </summary>
        public const int DefaultSeaLevel = 64;

        /// <summary>世界种子。</summary>
        public int Seed;

        /// <summary>平均地表高度（Grass 所在的 Voxel y）。</summary>
        public int TerrainHeight;

        /// <summary>土壤层（Dirt）层数：y ∈ (TerrainHeight - SoilDepth, TerrainHeight)。</summary>
        public int SoilDepth;

        /// <summary>世界竖直体素数：有效范围 [0, WorldHeight-1]。</summary>
        public int WorldHeight;

        /// <summary>
        /// 海平面（世界体素 y；合法范围 [0, WorldHeight]；默认 64）。
        /// 单位与 `TerrainHeight` / `Elevation` 一致（世界体素 y），不涉及 Chunk 局部坐标或归一化量。
        /// 边界语义：`SeaLevel = 0` ⇒ 任何合法 `Elevation` 都不是海洋；
        /// `SeaLevel = WorldHeight` ⇒ 任何合法 `Elevation` 都是海洋（闭区间上界）。
        /// </summary>
        public int SeaLevel;

        /// <summary>噪声频率（每体素），必须 &gt; 0。</summary>
        public float TerrainFrequency;

        /// <summary>起伏幅度（± 体素数），必须 &gt;= 0。</summary>
        public float TerrainAmplitude;

        /// <summary>
        /// **兼容构造（旧六参数）**：`SeaLevel` 取登记默认值
        /// <see cref="DefaultSeaLevel"/>（= 64），保持迁移前的既有行为。
        /// 需要显式海平面时使用七参数构造函数。
        /// </summary>
        public WorldGenerationSettings(
            int seed,
            int terrainHeight,
            int soilDepth,
            int worldHeight,
            float terrainFrequency,
            float terrainAmplitude)
            : this(seed, terrainHeight, soilDepth, worldHeight, terrainFrequency, terrainAmplitude, DefaultSeaLevel)
        {
        }

        /// <summary>
        /// **显式 SeaLevel 构造**：海平面由世界创建期配置显式给出（单位：世界体素 y）。
        /// </summary>
        public WorldGenerationSettings(
            int seed,
            int terrainHeight,
            int soilDepth,
            int worldHeight,
            float terrainFrequency,
            float terrainAmplitude,
            int seaLevel)
        {
            Seed = seed;
            TerrainHeight = terrainHeight;
            SoilDepth = soilDepth;
            WorldHeight = worldHeight;
            SeaLevel = seaLevel;
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

            if (SeaLevel < 0 || SeaLevel > WorldHeight)
            {
                reason = "SeaLevel 必须在 [0, WorldHeight] 内（实际 " +
                         SeaLevel + " / WorldHeight=" + WorldHeight + "）。";
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
                   ", SeaLevel=" + SeaLevel +
                   ", TerrainFrequency=" + TerrainFrequency +
                   ", TerrainAmplitude=" + TerrainAmplitude + ")";
        }
    }
}
