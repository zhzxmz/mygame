namespace VoxelWorld.Generation.Geology
{
    /// <summary>
    /// 一处世界坐标的**环境描述**（Geology Stage C）。
    ///
    /// 定位（WorldGeneration_Geology.md §7）：
    ///   · **只输出环境**：纯数据，不生成植被、不生成树、不生成动物、不生成任何 Entity；
    ///   · 环境值属于"世界规则"的一部分（温度/湿度是世界的固有属性），
    ///     但**不得**改变 Runtime（加载/卸载/生命周期/预算），也不得写入体素。
    ///
    /// 数值约定（**整数定点**，避免平台相关浮点差异）：
    ///   · <see cref="Temperature"/>：单位 0.1 ℃，范围 [-500, 600]（即 -50.0℃ ~ +60.0℃）；
    ///   · <see cref="Moisture"/>：单位 0.1 %，范围 [0, 1000]（即 0.0% ~ 100.0%）；
    ///   · <see cref="Biome"/>：群系标识（<see cref="GeologyBiome"/> 占位取值空间）。
    ///
    /// `Biome == None` 的语义：**群系判定被显式关闭**（`GeologySettings.EnableBiomeModifiers == false`）。
    /// 此时 Temperature / Moisture **仍然有效**（它们是环境测量，不是"修正"）。
    /// </summary>
    public struct BiomeSample
    {
        /// <summary>温度下界（-50.0℃）。</summary>
        public const int MinTemperature = -500;

        /// <summary>温度上界（+60.0℃）。</summary>
        public const int MaxTemperature = 600;

        /// <summary>湿度下界（0.0%）。</summary>
        public const int MinMoisture = 0;

        /// <summary>湿度上界（100.0%）。</summary>
        public const int MaxMoisture = 1000;

        /// <summary>群系标识（`None` 表示群系判定被关闭）。</summary>
        public GeologyBiome Biome;

        /// <summary>温度（0.1 ℃ 定点）。</summary>
        public int Temperature;

        /// <summary>湿度（0.1 % 定点）。</summary>
        public int Moisture;

        /// <summary>自洽性检查（纯谓词）：数值必须落在显式范围内。</summary>
        public bool IsConsistent()
        {
            if (Temperature < MinTemperature || Temperature > MaxTemperature) return false;
            if (Moisture < MinMoisture || Moisture > MaxMoisture) return false;

            return true;
        }

        public override string ToString()
        {
            return "Biome(biome=" + Biome +
                   ", temperature=" + Temperature +
                   ", moisture=" + Moisture + ")";
        }
    }

    /// <summary>
    /// 环境（群系）查询接口（Geology Stage C）：描述世界坐标处的温度/湿度与群系标识。
    ///
    /// 硬性条款：
    ///   · **纯 C# / 纯数据**：不引用 UnityEngine、不引用 Chunk、不引用 Runtime、不生成 Entity；
    ///   · **不生成植被**：树木/草/动物等属于未来 Entity / Vegetation 系统，
    ///     本接口只输出"环境是多少"，不输出"该放什么对象"（GC5）；
    ///   · **确定性**：同 `(worldX, worldY, worldZ, seed)` + 同参数 ⇒ 同结果（GC2）；
    ///   · **世界坐标纯函数**：不依赖 Chunk 坐标、加载状态、时间或随机状态；
    ///   · 允许 `worldY` 参与判定（例如海拔导致的温度梯度）。
    /// </summary>
    public interface IBiomeProvider
    {
        /// <summary>采样某世界坐标的环境描述。</summary>
        BiomeSample SampleBiome(int worldX, int worldY, int worldZ, int seed);
    }
}
