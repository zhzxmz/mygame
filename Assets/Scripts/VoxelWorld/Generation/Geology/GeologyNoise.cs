namespace VoxelWorld.Generation.Geology
{
    /// <summary>
    /// 确定性世界坐标噪声（Geology Stage A）。
    ///
    /// 契约（WorldGeneration_Geology_Implementation.md §2.2 / §10）：
    ///   · **纯函数**：同输入永远同输出（不依赖时间、帧序、调用顺序、平台随机源）；
    ///   · **无状态**：只有 `const` 常量，没有任何静态可变字段（禁止隐藏全局状态）；
    ///   · **连续**：格胞内用 smoothstep 三线性/双线性插值 ⇒ 相邻体素的变化有显式上界
    ///     （<see cref="MaxAdjacentDelta3D"/> / <see cref="MaxAdjacentDelta2D"/>）；
    ///   · **只依赖世界坐标**：不接受 Chunk 坐标、不接受局部坐标（§3.2）；
    ///   · **纯 C#**：不引用 UnityEngine（不使用 `Mathf.PerlinNoise` / `Random` / `Time`）。
    ///
    /// 实现要点（确定性优先）：
    ///   1. 格胞值由**整数哈希**得到（只做整数乘/异或/移位）⇒ 与平台无关；
    ///   2. 格胞值映射到 [-1, 1)：取 24 位有效位后乘以 2⁻²³（2 的幂缩放，**精确无舍入**）；
    ///   3. 格胞边长只允许 **2 的幂**（1…256 体素）⇒ 插值参数 `余数 × (1/边长)` 也是精确运算；
    ///   4. 负坐标用整数 floor 除法定位格胞（不经过 float→int 截断）。
    ///
    /// 输出范围：`[-1, 1)`（插值是格胞值的凸组合 ⇒ 必然落在 [-1, 1] 内）。
    /// </summary>
    public static class GeologyNoise
    {
        /// <summary>默认格胞边长（体素）。越大越平滑、特征尺度越大。</summary>
        public const int DefaultCellVoxels = 16;

        /// <summary>允许的最小格胞边长（体素）。</summary>
        public const int MinCellVoxels = 1;

        /// <summary>允许的最大格胞边长（体素）。</summary>
        public const int MaxCellVoxels = 256;

        /// <summary>
        /// 频率换算基准：`格胞边长 = FrequencyDivisorVoxels / frequency`。
        /// 因此频率的合法集合 = 64 的 2 的幂因子 {1,2,4,8,16,32,64}
        /// ⇒ 格胞边长 ∈ {64,32,16,8,4,2,1}（全部为 2 的幂）。
        /// </summary>
        public const int FrequencyDivisorVoxels = 64;

        // -----------------------------------------------------------------
        // 公开采样入口
        // -----------------------------------------------------------------

        /// <summary>三维噪声（默认格胞边长 16 体素）。输出 [-1, 1)。</summary>
        public static float Value3D(int worldX, int worldY, int worldZ, int seed)
        {
            return Value3D(worldX, worldY, worldZ, seed, DefaultCellVoxels);
        }

        /// <summary>三维噪声（指定格胞边长，非 2 的幂会自动就近取整）。输出 [-1, 1)。</summary>
        public static float Value3D(int worldX, int worldY, int worldZ, int seed, int cellVoxels)
        {
            int cell = ClampCellVoxels(cellVoxels);

            int cellX = FloorDiv(worldX, cell);
            int cellY = FloorDiv(worldY, cell);
            int cellZ = FloorDiv(worldZ, cell);

            float inverse = 1f / cell;

            float tx = Smoothstep((worldX - cellX * cell) * inverse);
            float ty = Smoothstep((worldY - cellY * cell) * inverse);
            float tz = Smoothstep((worldZ - cellZ * cell) * inverse);

            // 8 个格胞角点（只做整数哈希）
            float v000 = LatticeValue(cellX, cellY, cellZ, seed);
            float v100 = LatticeValue(cellX + 1, cellY, cellZ, seed);
            float v010 = LatticeValue(cellX, cellY + 1, cellZ, seed);
            float v110 = LatticeValue(cellX + 1, cellY + 1, cellZ, seed);
            float v001 = LatticeValue(cellX, cellY, cellZ + 1, seed);
            float v101 = LatticeValue(cellX + 1, cellY, cellZ + 1, seed);
            float v011 = LatticeValue(cellX, cellY + 1, cellZ + 1, seed);
            float v111 = LatticeValue(cellX + 1, cellY + 1, cellZ + 1, seed);

            float x00 = Lerp(v000, v100, tx);
            float x10 = Lerp(v010, v110, tx);
            float x01 = Lerp(v001, v101, tx);
            float x11 = Lerp(v011, v111, tx);

            float y0 = Lerp(x00, x10, ty);
            float y1 = Lerp(x01, x11, ty);

            return Lerp(y0, y1, tz);
        }

        /// <summary>二维噪声（默认格胞边长 16 体素）。输出 [-1, 1)。用于高度/群系等列式采样。</summary>
        public static float Value2D(int worldX, int worldZ, int seed)
        {
            return Value2D(worldX, worldZ, seed, DefaultCellVoxels);
        }

        /// <summary>二维噪声（指定格胞边长，非 2 的幂会自动就近取整）。输出 [-1, 1)。</summary>
        public static float Value2D(int worldX, int worldZ, int seed, int cellVoxels)
        {
            int cell = ClampCellVoxels(cellVoxels);

            int cellX = FloorDiv(worldX, cell);
            int cellZ = FloorDiv(worldZ, cell);

            float inverse = 1f / cell;

            float tx = Smoothstep((worldX - cellX * cell) * inverse);
            float tz = Smoothstep((worldZ - cellZ * cell) * inverse);

            float v00 = LatticeValue(cellX, 0, cellZ, seed);
            float v10 = LatticeValue(cellX + 1, 0, cellZ, seed);
            float v01 = LatticeValue(cellX, 0, cellZ + 1, seed);
            float v11 = LatticeValue(cellX + 1, 0, cellZ + 1, seed);

            return Lerp(Lerp(v00, v10, tx), Lerp(v01, v11, tx), tz);
        }

        // -----------------------------------------------------------------
        // 参数与上界（供 GeologySettings 校验与验证入口使用）
        // -----------------------------------------------------------------

        /// <summary>频率是否合法（64 的 2 的幂因子）。</summary>
        public static bool IsValidFrequency(int frequency)
        {
            return frequency == 1 || frequency == 2 || frequency == 4 || frequency == 8 ||
                   frequency == 16 || frequency == 32 || frequency == 64;
        }

        /// <summary>频率 → 格胞边长（体素）。频率非法时按 1（最平滑）处理。</summary>
        public static int CellVoxelsForFrequency(int frequency)
        {
            if (!IsValidFrequency(frequency)) return CellVoxelsForFrequency(1);
            return FrequencyDivisorVoxels / frequency;
        }

        /// <summary>
        /// 把任意格胞边长规整到 [1,256] 内**最近**的 2 的幂。
        /// 平局（恰好在两个 2 的幂正中间，例如 3/12/24）时**取较小值**，
        /// 即偏向更平滑的噪声，保证行为可预测且与实现无关。
        /// </summary>
        public static int ClampCellVoxels(int cellVoxels)
        {
            if (cellVoxels <= MinCellVoxels) return MinCellVoxels;

            int result = MinCellVoxels;
            while (result < cellVoxels && result < MaxCellVoxels) result *= 2;

            // 就近取整（平局取较小值）
            int lower = result / 2;
            if (lower >= MinCellVoxels && (cellVoxels - lower) <= (result - cellVoxels))
            {
                result = lower;
            }

            return result > MaxCellVoxels ? MaxCellVoxels : result;
        }

        /// <summary>
        /// 相邻整数坐标（沿任意单轴移动 1 体素）的最大变化量上界（三维）。
        ///
        /// 推导：每个轴向上的变化 ≤ 3 / cell（smoothstep 导数上界 1.5 × 角点差上界 2 × 权重和为 1）；
        /// 三个轴同时移动 1 体素 ⇒ ≤ 9 / cell；又因为取值恒定在 [-1,1] ⇒ ≤ 2。
        /// </summary>
        public static float MaxAdjacentDelta3D(int cellVoxels)
        {
            int cell = ClampCellVoxels(cellVoxels);
            float bound = 9f / cell;
            return bound < 2f ? bound : 2f;
        }

        /// <summary>相邻整数坐标的最大变化量上界（二维）：≤ min(2, 6 / cell)。</summary>
        public static float MaxAdjacentDelta2D(int cellVoxels)
        {
            int cell = ClampCellVoxels(cellVoxels);
            float bound = 6f / cell;
            return bound < 2f ? bound : 2f;
        }

        // -----------------------------------------------------------------
        // 内部（整数哈希 + 精确缩放 + 插值）
        // -----------------------------------------------------------------

        /// <summary>
        /// 格胞角点值 → [-1, 1)。
        /// 取 24 位有效位后乘以 2⁻²³（2 的幂缩放）⇒ 结果为精确可表示的浮点值，跨平台一致。
        /// </summary>
        private static float LatticeValue(int cellX, int cellY, int cellZ, int seed)
        {
            uint bits = LatticeHash(cellX, cellY, cellZ, seed) & 0x00FFFFFFu;
            return bits * (1f / 8388608f) - 1f;   // 2^23 = 8388608
        }

        /// <summary>整数哈希：只做乘/异或/移位，无状态、无分配、与调用顺序无关。</summary>
        private static uint LatticeHash(int cellX, int cellY, int cellZ, int seed)
        {
            unchecked
            {
                uint h = 0x9E3779B9u;
                h = (h ^ (uint)cellX) * 0x85EBCA6Bu;
                h ^= h >> 13;
                h = (h ^ (uint)cellY) * 0xC2B2AE35u;
                h ^= h >> 11;
                h = (h ^ (uint)cellZ) * 0x27D4EB2Fu;
                h ^= h >> 15;
                h = (h ^ (uint)seed) * 0x165667B1u;
                h ^= h >> 16;
                return h;
            }
        }

        /// <summary>整数 floor 除法（divisor &gt; 0；负数同样向下取整）。</summary>
        private static int FloorDiv(int value, int divisor)
        {
            if (value >= 0) return value / divisor;
            return -((-value + divisor - 1) / divisor);
        }

        private static float Smoothstep(float t)
        {
            return t * t * (3f - 2f * t);
        }

        private static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }
    }
}
