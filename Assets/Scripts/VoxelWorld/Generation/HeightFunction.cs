using System;

namespace VoxelWorld.Generation
{
    /// <summary>
    /// 地形高度函数（World Generation Core V1 §2/§4）：
    ///
    ///     TerrainHeight = HeightFunction(worldX, worldZ, Seed, settings)
    ///
    /// 确定性要求（V1 §2）：
    ///   · 只依赖入参（worldX / worldZ / Seed / 参数），不依赖时间、帧序、加载顺序、
    ///     随机数状态或 Unity Transform；
    ///   · 同 seed + 同 x + 同 z ⇒ 永远得到同一高度；
    ///   · 实现为**简单确定性 value noise**（整数 hash + smoothstep 双线性插值）：
    ///     不依赖 UnityEngine 版本/平台，也不需要 PerlinNoise 的全局状态。
    ///     （如需改用 Mathf.PerlinNoise，只需替换本类内部实现，签名与规则不变。）
    ///
    /// 输出：被 clamp 到 [0, WorldHeight-1] 的**整数**地表 Voxel 高度，
    ///       便于严格套用“y == height ⇒ Grass”等规则。
    /// </summary>
    public static class HeightFunction
    {
        /// <summary>
        /// 计算世界列 (worldX, worldZ) 的地表高度（整数 Voxel y）。
        /// 结果始终落在 [0, settings.WorldHeight - 1] 内。
        /// </summary>
        public static int GetTerrainHeight(
            int worldX,
            int worldZ,
            WorldGenerationSettings settings)
        {
            float noise = GetNoise(
                worldX,
                worldZ,
                settings.Seed,
                settings.TerrainFrequency
            );

            float height = settings.TerrainHeight +
                           settings.TerrainAmplitude * noise;

            int rounded = (int)Math.Round(
                height,
                MidpointRounding.AwayFromZero
            );

            return Clamp(rounded, 0, settings.WorldHeight - 1);
        }

        /// <summary>
        /// 归一化噪声值（[-1, 1]），仅依赖 (worldX, worldZ, seed, frequency)。
        /// </summary>
        public static float GetNoise(
            int worldX,
            int worldZ,
            int seed,
            float frequency)
        {
            if (frequency <= 0f) frequency = 1f;

            float fx = worldX * frequency;
            float fz = worldZ * frequency;

            int x0 = FloorToInt(fx);
            int z0 = FloorToInt(fz);
            int x1 = x0 + 1;
            int z1 = z0 + 1;

            float tx = Smoothstep(fx - x0);
            float tz = Smoothstep(fz - z0);

            float v00 = HashToUnit(x0, z0, seed);
            float v10 = HashToUnit(x1, z0, seed);
            float v01 = HashToUnit(x0, z1, seed);
            float v11 = HashToUnit(x1, z1, seed);

            float v0 = Lerp(v00, v10, tx);
            float v1 = Lerp(v01, v11, tx);

            float unit = Lerp(v0, v1, tz);   // [0, 1]

            return unit * 2f - 1f;           // [-1, 1]
        }

        // -----------------------------------------------------------------
        // 确定性工具（整数运算 / 纯函数）
        // -----------------------------------------------------------------

        /// <summary>整数化的 floor（负坐标同样向下取整，不依赖 Mathf）。</summary>
        private static int FloorToInt(float value)
        {
            int truncated = (int)value;
            return value < truncated ? truncated - 1 : truncated;
        }

        private static float Smoothstep(float t)
        {
            return t * t * (3f - 2f * t);
        }

        private static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        /// <summary>把 (x, z, seed) 哈希为 [0, 1] 的确定性数值。</summary>
        private static float HashToUnit(int x, int z, int seed)
        {
            uint hash = Hash(x, z, seed);
            return (hash & 0x00FFFFFFu) / (float)0x00FFFFFFu;
        }

        /// <summary>
        /// 整数 hash（确定性、无状态、无分配）。
        /// 只使用整数乘/异或/移位，结果与平台、帧序、调用顺序无关。
        /// </summary>
        private static uint Hash(int x, int z, int seed)
        {
            unchecked
            {
                uint h = (uint)seed * 374761393u;
                h ^= (uint)x * 668265263u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= (uint)z * 2246822519u;
                h = (h ^ (h >> 15)) * 3266489917u;
                h ^= h >> 16;
                return h;
            }
        }
    }
}
