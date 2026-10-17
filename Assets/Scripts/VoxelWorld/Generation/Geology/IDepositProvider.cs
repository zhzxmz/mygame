namespace VoxelWorld.Generation.Geology
{
    /// <summary>
    /// 一处世界坐标的**资源描述**（Geology Stage C）。
    ///
    /// 定位（WorldGeneration_Geology.md §6.1 / 实现规范 §7）：
    ///   · **只描述"这里有矿"**：纯数据，不生成任何对象、不写入任何体素、不改变世界状态；
    ///   · **不生成晶簇**：裸露晶簇、矿石节点等属于未来 Entity 系统（ADR-001），
    ///     不属于 Chunk 所有权，也不在本结构/本接口的语义范围之内；
    ///   · 矿藏是**属性层**：它不改变 `Density`/`Material`（GIV5 / GC3）。
    ///
    /// 字段（全部为值类型，无引用、无容器、无 Unity 类型）：
    ///   · <see cref="Exists"/>   是否存在资源；
    ///   · <see cref="Zone"/>     类型标识（<see cref="GeologyDepositZone"/> 占位取值空间）；
    ///   · <see cref="Richness"/> 丰富度：0..100（本地强度，非储量上限）；
    ///   · <see cref="Grade"/>     等级：1..5（由深度决定；不存在时为 0）。
    /// </summary>
    public struct DepositSample
    {
        /// <summary>丰富度下界。</summary>
        public const int MinRichness = 0;

        /// <summary>丰富度上界。</summary>
        public const int MaxRichness = 100;

        /// <summary>等级下界（存在资源时的最小等级）。</summary>
        public const int MinGrade = 1;

        /// <summary>等级上界。</summary>
        public const int MaxGrade = 5;

        /// <summary>是否存在资源。</summary>
        public bool Exists;

        /// <summary>类型标识（`Exists == false` 时必须为 <see cref="GeologyDepositZone.None"/>）。</summary>
        public GeologyDepositZone Zone;

        /// <summary>丰富度（0..100；`Exists == false` 时为 0）。</summary>
        public int Richness;

        /// <summary>等级（1..5；`Exists == false` 时为 0）。</summary>
        public int Grade;

        /// <summary>"无资源"的规范取值（用于所有否定结果）。</summary>
        public static DepositSample None
        {
            get { return new DepositSample(); }
        }

        /// <summary>
        /// 自洽性检查（纯谓词）：不存在 ⇒ 全部字段为零值；存在 ⇒ 类型非 None、
        /// 丰富度与等级都落在合法区间内。
        /// </summary>
        public bool IsConsistent()
        {
            if (!Exists)
            {
                return Zone == GeologyDepositZone.None && Richness == 0 && Grade == 0;
            }

            if (Zone == GeologyDepositZone.None) return false;
            if (Richness < MinRichness || Richness > MaxRichness) return false;
            if (Grade < MinGrade || Grade > MaxGrade) return false;

            return true;
        }

        public override string ToString()
        {
            if (!Exists) return "Deposit(none)";

            return "Deposit(zone=" + Zone + ", richness=" + Richness +
                   ", grade=" + Grade + ")";
        }
    }

    /// <summary>
    /// 资源查询接口（Geology Stage C）：描述世界坐标处的**资源存在性**。
    ///
    /// 硬性条款：
    ///   · **纯 C# / 纯数据**：不引用 UnityEngine、不引用 Chunk（`VoxelChunk`）、
    ///     不引用 Runtime、不生成 Entity；
    ///   · **只输出可能性**：返回的是"这里存在资源"的描述，**不是**"生成晶簇/矿石节点"的指令；
    ///   · **确定性**：同 `(worldX, worldY, worldZ, seed)` + 同参数 ⇒ 同结果；
    ///   · **不改变世界**：查询不改体素、不改脏标记、不产生副作用（GC3/GC4）。
    ///
    /// 两个重载语义完全一致：<see cref="TryGetDeposit(int,int,int,int)"/> 直接返回描述，
    /// <see cref="TryGetDeposit(int,int,int,int,out DepositSample)"/> 便于 `if (TryGetDeposit(...))` 写法。
    /// </summary>
    public interface IDepositProvider
    {
        /// <summary>查询某世界坐标的资源描述（无资源时返回 <see cref="DepositSample.None"/>）。</summary>
        DepositSample TryGetDeposit(int worldX, int worldY, int worldZ, int seed);

        /// <summary>查询某世界坐标的资源描述；返回是否存在资源（结果与上面完全一致）。</summary>
        bool TryGetDeposit(int worldX, int worldY, int worldZ, int seed, out DepositSample sample);
    }
}
