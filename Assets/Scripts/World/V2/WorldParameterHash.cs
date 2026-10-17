using System;

namespace World.V2
{
    /// <summary>
    /// **域标签确定性整数哈希**（World V2 · Plan Phase 1.5）。
    ///
    /// 用途：为参数求值提供**确定性来源**的形态，满足 `WorldParameter_Contract.md` §6.3：
    ///   · `PZ10`：`Domain` 是显式登记的 32 位整数标识，**未登记的域不得使用**；
    ///   · `PZ11`：确定性来源必须形如"以 `(seed, domain, 世界坐标)` 为输入的自实现整数哈希"；
    ///   · `PZ12`：**不得**调用/复用保留域 `R-1`（`HeightFunction.Hash`）与 `R-2`（`GeologyNoise.LatticeHash`）的混合方式；
    ///   · `PZ13`：同域内恒等，不同域之间统计独立。
    ///
    /// **本类型不是噪声函数**（明确声明，避免误读）：
    ///   · 无插值、无梯度、无频率、无八度、无空间连续性承诺；
    ///   · 不生成地形、不生成体素、不接触任何网格或世界数据；
    ///   · 只做整数位混合（整数运算 + 一次精确的 `[0,1)` 归一化），跨平台结果一致（`PS7`）。
    ///
    /// **禁止**（`WorldParameter_Contract.md` §6.2）：
    ///   · `Random` / `System.Random` / `UnityEngine.Random`（`PZ5`）；
    ///   · `Time` / `DateTime` / 时间戳 / `Guid`（`PZ6`）；
    ///   · 任何读取全局状态、加载顺序、帧序、玩家状态的量（`PZ4` / `PZ7` / `PZ8`）。
    ///
    /// 本类型不使用 UnityEngine。
    /// </summary>
    public static class WorldParameterHash
    {
        /// <summary>
        /// 契约 §6.4 登记的域 `1 = Skeleton`：覆盖 `Continent`、`Slope`、**基础气候带**
        /// （`TemperatureBase` / `MoistureBase` 的噪声部分）。其它域在需要时由对应阶段按 §6.4 引入。
        /// </summary>
        public const int SkeletonDomainId = 1;

        /// <summary>64 位混合用的奇数种子常量（无特殊语义，仅用于打散初始状态）。</summary>
        private const int SeedSalt = 0x2F1B3C5D;

        /// <summary>混合步使用的乘子（FNV-1a 素数 16777619）。</summary>
        private const int MixPrime = 0x01000193;

        /// <summary>雪崩步使用的乘子（Murmur3 常量，截断为 32 位）。</summary>
        private const int AvalanchePrime = unchecked((int)0x85EBCA6B);

        /// <summary>确定性的 32 位整数哈希（`PZ11` 形态：seed + domain + 参数身份 + 世界坐标）。</summary>
        public static uint Hash(WorldSeed seed, int domainId, int parameterId, WorldCoordinate position)
        {
            unchecked
            {
                int hash = Mix(SeedSalt, seed.Value);
                hash = Mix(hash, domainId);
                hash = Mix(hash, parameterId);
                hash = Mix(hash, position.X);
                hash = Mix(hash, position.Y);
                hash = Mix(hash, position.Z);
                return (uint)hash;
            }
        }

        /// <summary>
        /// 把哈希映射到 `[0,1)`（连续参数的占位取值面）。
        /// 取低 24 位后除以 `2^24`：除数与分子在 `float` 中**均可精确表示**，结果与平台浮点路径无关（`PS7`）。
        /// </summary>
        public static float Unit(WorldSeed seed, int domainId, int parameterId, WorldCoordinate position)
        {
            uint bits = Hash(seed, domainId, parameterId, position) & 0x00FFFFFFu;
            return bits / 16777216f;
        }

        /// <summary>
        /// 把哈希映射到 `[0, exclusiveMax)` 的**整数**（整数参数/布尔参数的占位取值面；`PS6`：不得插值）。
        /// </summary>
        public static int Range(WorldSeed seed, int domainId, int parameterId, WorldCoordinate position, int exclusiveMax)
        {
            if (exclusiveMax <= 0)
            {
                throw new ArgumentOutOfRangeException("exclusiveMax");
            }

            return (int)(Hash(seed, domainId, parameterId, position) % (uint)exclusiveMax);
        }

        /// <summary>整数位混合的一步（自实现，不引用任何保留域的混合方式，`PZ12`）。</summary>
        private static int Mix(int accumulator, int value)
        {
            unchecked
            {
                int hash = (accumulator ^ value) * MixPrime;
                hash ^= hash >> 13;
                hash *= AvalanchePrime;
                hash ^= hash >> 16;
                return hash;
            }
        }
    }
}
