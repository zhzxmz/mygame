using System;

namespace VoxelWorld.Generation.Geology
{
    /// <summary>
    /// 一次地质采样的**结果描述**（Geology Stage A）。
    ///
    /// 定位（WorldGeneration_Geology_Implementation.md §2.2 / §11-GIV6）：
    ///   · **只描述结果**：纯数据容器，不修改任何 voxel、不生成任何对象、不产生副作用；
    ///   · **不生成 Entity**：晶簇/矿石节点/植被等属于未来 Entity 系统（ADR-001），
    ///     本结构只提供"岩性 / 洞穴 / 矿藏 / 群系"的**查询结果**；
    ///   · **属性层不改密度**：矿藏与群系是可空占位，不参与实体性判定（P0–P3）。
    ///
    /// 判定语义（与 WorldGeneration_Geology.md §3.2 的优先级一一对应）：
    ///   · <see cref="Rule"/>：最终命中的规则（优先级最高者）；
    ///   · <see cref="CaveActive"/>：洞穴噪声是否超阈值的**原始判定**（与优先级无关；
    ///     `y` 在世界之外时可以不计算）；
    ///   · <see cref="IsSolid"/> ⟺ `Rule ∈ {Surface, Depth, Deep}`；
    ///   · <see cref="Material"/>：`IsSolid == false` 时必须为 `VoxelType.Air`。
    /// </summary>
    [Serializable]
    public struct GeologySample
    {
        // -----------------------------------------------------------------
        // 岩性相关结果
        // -----------------------------------------------------------------

        /// <summary>最终命中的规则（P0 WorldBounds &gt; P1 Cave &gt; P2 AboveSurface &gt; P3 地层）。</summary>
        public GeologyRule Rule;

        /// <summary>该坐标是否为实体（`Rule ∈ {Surface, Depth, Deep}`）。</summary>
        public bool IsSolid;

        /// <summary>实体坐标的材料；非实体时必须是 <see cref="VoxelType.Air"/>。</summary>
        public VoxelType Material;

        /// <summary>该列的地表高度（世界 y；由 Stage 1 规则 + 群系修正决定）。</summary>
        public int SurfaceHeight;

        /// <summary>该列的有效土壤层厚度（Stage 1 参数；Biome 修正预留）。</summary>
        public int SoilDepth;

        // -----------------------------------------------------------------
        // 洞穴影响结果
        // -----------------------------------------------------------------

        /// <summary>洞穴噪声是否达到阈值（原始判定，与优先级无关）。</summary>
        public bool CaveActive;

        /// <summary>洞穴噪声值（[-1, 1)）；世界之外可以不计算。</summary>
        public float CaveDensity;

        // -----------------------------------------------------------------
        // 占位（Stage A 只定义字段与取值空间；实际采样属 Stage C）
        // -----------------------------------------------------------------

        /// <summary>矿藏区域查询结果占位（Stage A 恒为 <see cref="GeologyDepositZone.None"/>）。</summary>
        public GeologyDepositZone DepositZone;

        /// <summary>生物群系查询结果占位（Stage A 恒为 <see cref="GeologyBiome.None"/>）。</summary>
        public GeologyBiome Biome;

        // -----------------------------------------------------------------
        // 工具
        // -----------------------------------------------------------------

        /// <summary>全默认结果（未判定；用于"尚无采样"的显式表达）。</summary>
        public static GeologySample Empty
        {
            get { return new GeologySample(); }
        }

        /// <summary>
        /// 结果自洽性检查（纯谓词，不修改任何状态）：
        ///   · `IsSolid` ⟺ `Rule ∈ {Surface, Depth, Deep}`；
        ///   · `Material == Air` ⟺ `!IsSolid`；
        ///   · `Rule == Cave ⇒ !IsSolid`。
        /// </summary>
        public bool IsConsistent()
        {
            bool ruleSaysSolid = Rule == GeologyRule.Surface ||
                                 Rule == GeologyRule.Depth ||
                                 Rule == GeologyRule.Deep;

            if (IsSolid != ruleSaysSolid) return false;

            if (IsSolid)
            {
                if (Material == VoxelType.Air) return false;
                if (Material != VoxelType.Grass &&
                    Material != VoxelType.Dirt &&
                    Material != VoxelType.Stone) return false;
            }
            else
            {
                if (Material != VoxelType.Air) return false;
            }

            if (Rule == GeologyRule.Cave && IsSolid) return false;
            if (Rule == GeologyRule.WorldBounds && IsSolid) return false;
            if (Rule == GeologyRule.AboveSurface && IsSolid) return false;

            return true;
        }

        public override string ToString()
        {
            return "GeologySample(rule=" + Rule +
                   ", solid=" + (IsSolid ? "yes" : "no") +
                   ", material=" + Material +
                   ", surface=" + SurfaceHeight +
                   ", soil=" + SoilDepth +
                   ", cave=" + (CaveActive ? "active" : "none") +
                   "(" + CaveDensity + ")" +
                   ", deposit=" + DepositZone +
                   ", biome=" + Biome + ")";
        }
    }

    /// <summary>
    /// 命中的地质规则（WorldGeneration_Geology.md §3.2 的优先级链）。
    /// 取值顺序即优先级顺序（数值越小优先级越高），便于比较与调试输出。
    /// </summary>
    public enum GeologyRule
    {
        /// <summary>尚未判定（默认值；由 <see cref="GeologySample.Empty"/> 使用）。</summary>
        Unresolved = 0,

        /// <summary>P0：世界之外（`y &lt; 0` 或 `y ≥ WorldHeight`）⇒ 恒为空。</summary>
        WorldBounds = 1,

        /// <summary>P1：洞穴覆盖（`CaveDensity ≥ CaveThreshold`）⇒ 空（可覆盖岩石）。</summary>
        Cave = 2,

        /// <summary>P2：地表之上（`y &gt; SurfaceHeight`）⇒ 空。</summary>
        AboveSurface = 3,

        /// <summary>P3：地表层（`y == SurfaceHeight`）⇒ Grass。</summary>
        Surface = 4,

        /// <summary>P3：土壤层（`SurfaceHeight - SoilDepth &lt; y &lt; SurfaceHeight`）⇒ Dirt。</summary>
        Depth = 5,

        /// <summary>P3：深层（`y ≤ SurfaceHeight - SoilDepth`）⇒ Stone。</summary>
        Deep = 6
    }

    /// <summary>
    /// 矿物区域占位（Stage A 只定义**取值空间**）。
    /// 具体矿种、分布规则与查询实现属 Stage C（`IDepositProvider`）；
    /// 本枚举不代表任何体素类型（矿藏是属性层，见 GIV5）。
    /// </summary>
    public enum GeologyDepositZone : byte
    {
        /// <summary>无资源。</summary>
        None = 0,

        /// <summary>占位区域 A（语义待 Stage C 定义）。</summary>
        ZoneA = 1,

        /// <summary>占位区域 B（语义待 Stage C 定义）。</summary>
        ZoneB = 2
    }

    /// <summary>
    /// 生物群系占位（Stage A 只定义**取值空间**）。
    /// 具体群系与其参数表属 Stage C/E（`IBiomeProvider`）；本枚举不生成任何植被。
    /// </summary>
    public enum GeologyBiome : byte
    {
        /// <summary>无群系（未应用群系修正）。</summary>
        None = 0,

        /// <summary>占位群系 A（语义待 Stage C 定义）。</summary>
        BiomeA = 1,

        /// <summary>占位群系 B（语义待 Stage C 定义）。</summary>
        BiomeB = 2
    }
}
