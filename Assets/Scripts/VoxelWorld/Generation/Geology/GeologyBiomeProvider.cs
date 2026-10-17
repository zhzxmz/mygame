namespace VoxelWorld.Generation.Geology
{
    /// <summary>
    /// <see cref="IBiomeProvider"/> 的 V1 实现（Geology Stage C）：用 <see cref="GeologyNoise"/>
    /// 的**世界坐标噪声**描述环境（温度 / 湿度）与群系标识。
    ///
    /// 规则（全部显式、确定、无副作用）：
    ///   1. **温度**：基准 <see cref="BaseTemperature"/>（0.1℃ 定点）+ **列噪声**（只依赖 x/z）
    ///      − 海拔递减（每体素 <see cref="LapsePerVoxel"/>，0.1℃）
    ///      ⇒ 同一列上温度严格每体素下降 `LapsePerVoxel`（这既是 `worldY` 参与采样的理由，
    ///      也让"越高越冷"成为**可证明的不变量**而不是统计趋势）；
    ///   2. **湿度**：列属性（只依赖 x/z，同一列上恒定）；
    ///   3. **群系标识**：`EnableBiomeModifiers == false` ⇒ 恒为
    ///      <see cref="GeologyBiome.None"/>（群系判定被显式关闭；温度/湿度**仍然有效**）；
    ///      否则按"暖且湿 ⇒ <see cref="GeologyBiome.BiomeB"/>，其余 ⇒
    ///      <see cref="GeologyBiome.BiomeA"/>"的显式阈值划分（温度与湿度两个轴都参与判定）；
    ///   4. **只输出环境**：不生成植被、不生成树/草/动物，不生成任何 Entity，
    ///      不写体素、不触碰 Runtime / Mesh / View（GC5）；
    ///   5. **确定性**：同 `(worldX, worldY, worldZ, seed)` + 同参数 ⇒ 同结果（GC2）。
    ///
    /// 纯度：纯 C#、只读依赖（<see cref="GeologySampler"/> 提供参数与开关）、
    /// 无可变静态字段、无 UnityEngine 标识符。
    /// </summary>
    public sealed class GeologyBiomeProvider : IBiomeProvider
    {
        /// <summary>海拔温度基准（0.1℃ 定点；200 = 20.0℃）。</summary>
        public const int BaseTemperature = 200;

        /// <summary>海拔递减率（每体素 0.1℃ 定点；2 = 0.2℃/体素）。</summary>
        public const int LapsePerVoxel = 2;

        /// <summary>温度噪声扰动幅度（0.1℃ 定点）。</summary>
        public const int TemperatureNoiseAmplitude = 100;

        /// <summary>湿度基准（0.1% 定点；500 = 50.0%）。</summary>
        public const int BaseMoisture = 500;

        /// <summary>湿度噪声扰动幅度（0.1% 定点）。</summary>
        public const int MoistureNoiseAmplitude = 500;

        /// <summary>群系划分的温度阈值（0.1℃ 定点；100 = 10.0℃）。</summary>
        public const int BiomeTemperatureThreshold = 100;

        /// <summary>群系划分的湿度阈值（0.1% 定点；500 = 50.0%）。</summary>
        public const int BiomeMoistureThreshold = 500;

        private readonly GeologySampler sampler;

        public GeologyBiomeProvider(GeologySampler sampler)
        {
            if (sampler == null)
            {
                throw new System.ArgumentNullException("sampler");
            }

            this.sampler = sampler;
        }

        /// <summary>地质参数（只读；来自被注入的采样器）。</summary>
        public GeologySettings Geology
        {
            get { return sampler.Geology; }
        }

        public BiomeSample SampleBiome(int worldX, int worldY, int worldZ, int seed)
        {
            int cell = sampler.Geology.DepositCellVoxels;   // 环境与矿藏共用同一格胞尺度（显式）

            // 温度 = 列噪声（只依赖 x/z）+ 海拔递减（只依赖 y）
            //   ⇒ 同一列上温度**严格**每体素下降 LapsePerVoxel（未触碰到范围边界时），
            //     因此"越高越冷"是**可证明**的不变量（GC2），而不是统计趋势。
            int temperature = BaseTemperature - LapsePerVoxel * worldY +
                              (int)(GeologyNoise.Value2D(worldX, worldZ, seed, cell) *
                                    TemperatureNoiseAmplitude);

            // 湿度 = 列属性（只依赖 x/z）：同一列上恒定
            int moisture = BaseMoisture +
                           (int)(GeologyNoise.Value2D(worldX + 7919, worldZ + 15485863,
                                                      seed, cell) * MoistureNoiseAmplitude);

            BiomeSample sample;
            sample.Temperature = ClampTemperature(temperature);
            sample.Moisture = ClampMoisture(moisture);
            sample.Biome = ResolveBiome(sample.Temperature, sample.Moisture);
            return sample;
        }

        // -----------------------------------------------------------------
        // 内部（纯整数阈值判定）
        // -----------------------------------------------------------------

        /// <summary>
        /// 群系标识：显式开关关闭 ⇒ <see cref="GeologyBiome.None"/>；
        /// 否则按"**暖且湿** ⇒ <see cref="GeologyBiome.BiomeB"/>，其余 ⇒
        /// <see cref="GeologyBiome.BiomeA"/>"的显式阈值划分（两个坐标轴都参与判定）。
        /// 这是占位规则：真实群系表（森林/沙漠/雪原…）属后续阶段，本阶段只提供稳定的划分与可验证的确定性。
        /// </summary>
        private GeologyBiome ResolveBiome(int temperature, int moisture)
        {
            if (!sampler.Geology.EnableBiomeModifiers) return GeologyBiome.None;

            bool warmAndWet = temperature >= BiomeTemperatureThreshold &&
                              moisture >= BiomeMoistureThreshold;

            return warmAndWet ? GeologyBiome.BiomeB : GeologyBiome.BiomeA;
        }

        private static int ClampTemperature(int value)
        {
            if (value < BiomeSample.MinTemperature) return BiomeSample.MinTemperature;
            if (value > BiomeSample.MaxTemperature) return BiomeSample.MaxTemperature;
            return value;
        }

        private static int ClampMoisture(int value)
        {
            if (value < BiomeSample.MinMoisture) return BiomeSample.MinMoisture;
            if (value > BiomeSample.MaxMoisture) return BiomeSample.MaxMoisture;
            return value;
        }

        public override string ToString()
        {
            return "GeologyBiomeProvider(" + sampler.Geology + ")";
        }
    }
}
