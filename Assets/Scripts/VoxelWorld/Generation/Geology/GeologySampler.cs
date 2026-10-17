using System.Globalization;
using System.Text;

namespace VoxelWorld.Generation.Geology
{
    /// <summary>
    /// GeologySampler：**世界坐标 → 地质解释结果** 的采样层（Geology Stage B）。
    ///
    /// 规范来源：
    ///   · `WorldGeneration_Geology.md` §2（连续世界模型）/ §3（密度优先级）/ §4（地层）/ §5（洞穴）；
    ///   · `WorldGeneration_Geology_Implementation.md` §2.2（每层契约）/ §3（世界坐标采样）/
    ///     §11-GIV1·GIV2·GIV5·GIV9（采样层不变量）。
    ///
    /// 职责边界（本阶段严格限制）：
    ///   · **只做采样与解释**：把世界坐标翻译成 <see cref="GeologySample"/>（岩性 + 洞穴 + 占位属性）；
    ///   · **不生成体素**：没有 `VoxelChunk` 输入/输出，不调用 `SetVoxel`（Stage D 才是接入层）；
    ///   · **不接入 Runtime / Mesh / View**：不加载、不调度、不产几何、不生成 Entity；
    ///   · **不做地形生成器**：本阶段不创建 `GeologyWorldGenerator`、不修改 `BasicTerrainGenerator`。
    ///
    /// 硬性条款：
    ///   1. **世界坐标纯函数**：输入只有 `(worldX, worldY, worldZ, seed, 参数)`；
    ///      签名中不出现任何 Chunk 坐标，也不查询 Chunk 状态（§3.2）；
    ///   2. **判定顺序固定**（§3.2，唯一实现）：
    ///      `P0 WorldBounds → P1 Cave → P2 Terrain → P3 Material`；
    ///   3. **Cave 只影响密度**：洞穴把"实体"改成"空"（`Rule = Cave`），
    ///      **不生成任何体素、不产生几何、不产生 Entity**；
    ///   4. **单一判定链**：`Sample` 与 `ExplainAt` 共用同一个私有 `Resolve(...)`，
    ///      `ExplainAt` 只是把同一个 `GeologySample` 渲染成文本（禁止第二套计算）；
    ///   5. **高度场复用 Stage 1 定义**：`SurfaceHeight` 由既有 `HeightFunction` 提供
    ///      （它是 `WorldGeneration_Core.md` §4 的高度场定义）；
    ///      **判定链**（P0–P3 与 层→材料）由本类**独立表达** ——
    ///      不调用 `BasicTerrainGenerator`、不调用 `ClassifyVoxel`、不复制其源码（§12.3）。
    ///      这条分工是 GB5/GIV9（关闭地质特性 ⇒ 与 Stage 1 基线逐体素一致）能够成立的前提。
    ///   6. **纯 C#**：不引用 UnityEngine、不使用 `Random` / `Time` / `Mathf.PerlinNoise`；
    ///      噪声全部来自 <see cref="GeologyNoise"/>（整数哈希 + 精确缩放）。
    ///
    /// Stage B 的占位：`DepositZone` 与 `Biome` 恒为占位取值（`None`），
    /// 群系修正恒为恒等（`EnableBiomeModifiers` 在无 provider 时不产生效果）；
    /// 真正的矿藏/群系规则属 Stage C（`IDepositProvider` / `IBiomeProvider`）。
    /// </summary>
    public sealed class GeologySampler
    {
        private readonly WorldGenerationSettings stage1Settings;
        private readonly GeologySettings geologySettings;

        public GeologySampler(
            WorldGenerationSettings stage1Settings,
            GeologySettings geologySettings)
        {
            string reason;
            if (!stage1Settings.IsValid(out reason))
            {
                throw new System.ArgumentException(
                    "WorldGenerationSettings 非法：" + reason, "stage1Settings");
            }

            if (geologySettings == null)
            {
                throw new System.ArgumentNullException("geologySettings");
            }

            if (!geologySettings.IsValid(out reason))
            {
                throw new System.ArgumentException(
                    "GeologySettings 非法：" + reason, "geologySettings");
            }

            this.stage1Settings = stage1Settings;
            this.geologySettings = geologySettings;
        }

        /// <summary>Stage 1 参数（只读；含 Seed / 地表高度 / 土壤深度 / 世界高度）。</summary>
        public WorldGenerationSettings Stage1Settings
        {
            get { return stage1Settings; }
        }

        /// <summary>地质参数（只读）。</summary>
        public GeologySettings Geology
        {
            get { return geologySettings; }
        }

        // -----------------------------------------------------------------
        // 主入口：采样（世界坐标 + seed）
        // -----------------------------------------------------------------

        /// <summary>
        /// 采样一个世界坐标的地质解释结果（唯一主入口）。
        /// 输入只有 `(worldX, worldY, worldZ, seed)` + 构造期注入的参数；
        /// 相同输入永远得到相同结果（逐位一致，见 GIV1/GB1）。
        /// </summary>
        public GeologySample Sample(int worldX, int worldY, int worldZ, int seed)
        {
            return Resolve(worldX, worldY, worldZ, seed);
        }

        /// <summary>采样一个世界坐标（使用构造期注入的 `Seed`）。</summary>
        public GeologySample Sample(int worldX, int worldY, int worldZ)
        {
            return Resolve(worldX, worldY, worldZ, stage1Settings.Seed);
        }

        // -----------------------------------------------------------------
        // 可读诊断（与 Sample 共用同一判定链）
        // -----------------------------------------------------------------

        /// <summary>
        /// 可读诊断文本（与 <see cref="Sample"/> 使用**同一套内部判定逻辑**：
        /// 先 <see cref="Resolve"/>，再渲染；不存在第二套计算）。
        /// 输出为稳定的多行文本（十进制小数点用不变文化，便于比较与人工阅读）。
        /// </summary>
        public string ExplainAt(int worldX, int worldY, int worldZ, int seed)
        {
            return RenderExplanation(worldX, worldY, worldZ, seed, Resolve(worldX, worldY, worldZ, seed));
        }

        /// <summary>可读诊断文本（使用构造期注入的 `Seed`）。</summary>
        public string ExplainAt(int worldX, int worldY, int worldZ)
        {
            return ExplainAt(worldX, worldY, worldZ, stage1Settings.Seed);
        }

        // -----------------------------------------------------------------
        // 便利访问器（全部委托给同一条判定链）
        // -----------------------------------------------------------------

        /// <summary>该列的地表高度（世界 y；由 Stage 1 高度场给出）。</summary>
        public int SurfaceHeightAt(int worldX, int worldZ, int seed)
        {
            return ResolveSurfaceHeight(worldX, worldZ, seed);
        }

        public int SurfaceHeightAt(int worldX, int worldZ)
        {
            return ResolveSurfaceHeight(worldX, worldZ, stage1Settings.Seed);
        }

        /// <summary>洞穴噪声值（[-1, 1)；未启用洞穴时恒为 0）。</summary>
        public float CaveDensityAt(int worldX, int worldY, int worldZ, int seed)
        {
            return ResolveCaveDensity(worldX, worldY, worldZ, seed);
        }

        /// <summary>洞穴状态（未生效 / 已挖空 / 被更高优先级覆盖）。</summary>
        public GeologyCaveState CaveStateAt(int worldX, int worldY, int worldZ, int seed)
        {
            return ResolveCaveState(Resolve(worldX, worldY, worldZ, seed));
        }

        /// <summary>密度修正量（`Carve` ⇔ `Rule == Cave`；属性层永不改变密度）。</summary>
        public GeologyDensityModifier DensityModifierAt(int worldX, int worldY, int worldZ, int seed)
        {
            return ResolveDensityModifier(Resolve(worldX, worldY, worldZ, seed));
        }

        // -----------------------------------------------------------------
        // 唯一判定链（P0 → P1 → P2 → P3）
        // -----------------------------------------------------------------

        /// <summary>
        /// 判定顺序**固定**为 `P0 WorldBounds → P1 Cave → P2 Terrain → P3 Material`；
        /// 这是整个采样层唯一的判定实现（`Sample` / `ExplainAt` / 全部访问器都走这里）。
        /// </summary>
        private GeologySample Resolve(int worldX, int worldY, int worldZ, int seed)
        {
            GeologySample sample = new GeologySample();

            // 该列地表高度与有效土壤深度（Stage B：无群系修正 ⇒ 直接取 Stage 1 参数）
            sample.SurfaceHeight = ResolveSurfaceHeight(worldX, worldZ, seed);
            sample.SoilDepth = stage1Settings.SoilDepth;

            // P1 的原始噪声判定（属性层，不影响 P0 的优先级）
            sample.CaveDensity = ResolveCaveDensity(worldX, worldY, worldZ, seed);
            sample.CaveActive = geologySettings.EnableCaves &&
                                sample.CaveDensity >= geologySettings.CaveThreshold;

            // 属性占位（Stage B：无 Deposit/Biome provider ⇒ 恒为占位取值，不改变密度）
            sample.DepositZone = GeologyDepositZone.None;
            sample.Biome = GeologyBiome.None;

            // ---- P0 WorldBounds：世界之外恒为空（最高优先级，无条件）----
            if (worldY < 0 || worldY >= stage1Settings.WorldHeight)
            {
                return NotSolid(sample, GeologyRule.WorldBounds);
            }

            // ---- P1 Cave：只影响密度（把"实体"改成"空"），不生成任何体素 ----
            if (sample.CaveActive)
            {
                return NotSolid(sample, GeologyRule.Cave);
            }

            // ---- P2 Terrain：地表之上为空 ----
            if (worldY > sample.SurfaceHeight)
            {
                return NotSolid(sample, GeologyRule.AboveSurface);
            }

            // ---- P3 Material：Surface / Depth / Deep ----
            sample.IsSolid = true;
            sample.Material = ResolveLayerMaterial(
                worldY, sample.SurfaceHeight, sample.SoilDepth, out GeologyRule layerRule);
            sample.Rule = layerRule;
            return sample;
        }

        /// <summary>把"非实体"的结论写入样本（材料恒为 Air；避免三处重复赋值逻辑分叉）。</summary>
        private static GeologySample NotSolid(GeologySample sample, GeologyRule rule)
        {
            sample.Rule = rule;
            sample.IsSolid = false;
            sample.Material = VoxelType.Air;
            return sample;
        }

        /// <summary>
        /// 地层 → 材料（Surface / Depth / Deep 三段，顺序固定）：
        ///   `y == SurfaceHeight`                     ⇒ Grass（Surface）
        ///   `SurfaceHeight - SoilDepth &lt; y &lt; SurfaceHeight` ⇒ Dirt（Depth）
        ///   其余（`y ≤ SurfaceHeight - SoilDepth`）    ⇒ Stone（Deep）
        /// 该判定**独立表达** Stage 1 的规则等价形式（不调用 `ClassifyVoxel`、不复制其源码）。
        /// </summary>
        private static VoxelType ResolveLayerMaterial(
            int worldY, int surfaceHeight, int soilDepth, out GeologyRule rule)
        {
            if (worldY == surfaceHeight)
            {
                rule = GeologyRule.Surface;
                return VoxelType.Grass;
            }

            if (worldY > surfaceHeight - soilDepth)
            {
                rule = GeologyRule.Depth;
                return VoxelType.Dirt;
            }

            rule = GeologyRule.Deep;
            return VoxelType.Stone;
        }

        /// <summary>
        /// 地表高度：复用 Stage 1 的**高度场定义** <see cref="HeightFunction"/>（WorldGeneration_Core §4）。
        /// 显式 seed 与注入 seed 不同时，通过**参数副本**传入（不修改既有类型）；
        /// 相同时走快路径（无副本）。
        /// </summary>
        private int ResolveSurfaceHeight(int worldX, int worldZ, int seed)
        {
            if (seed == stage1Settings.Seed)
            {
                return HeightFunction.GetTerrainHeight(worldX, worldZ, stage1Settings);
            }

            WorldGenerationSettings copy = stage1Settings;
            copy.Seed = seed;
            return HeightFunction.GetTerrainHeight(worldX, worldZ, copy);
        }

        /// <summary>洞穴噪声（未启用洞穴 ⇒ 恒 0：既省算力，也让"关闭特性"的对照完全确定）。</summary>
        private float ResolveCaveDensity(int worldX, int worldY, int worldZ, int seed)
        {
            if (!geologySettings.EnableCaves) return 0f;

            return GeologyNoise.Value3D(
                worldX, worldY, worldZ, seed, geologySettings.CaveCellVoxels);
        }

        private static GeologyCaveState ResolveCaveState(GeologySample sample)
        {
            if (!sample.CaveActive) return GeologyCaveState.Inactive;
            return sample.Rule == GeologyRule.Cave
                ? GeologyCaveState.Carved
                : GeologyCaveState.Suppressed;
        }

        private static GeologyDensityModifier ResolveDensityModifier(GeologySample sample)
        {
            return sample.Rule == GeologyRule.Cave
                ? GeologyDensityModifier.Carve
                : GeologyDensityModifier.None;
        }

        // -----------------------------------------------------------------
        // 渲染（把同一个样本渲染成文本；不重新计算任何判定）
        // -----------------------------------------------------------------

        private string RenderExplanation(
            int worldX, int worldY, int worldZ, int seed, GeologySample sample)
        {
            StringBuilder text = new StringBuilder();

            text.Append("Coordinate:  (").Append(worldX).Append(", ").Append(worldY)
                .Append(", ").Append(worldZ).Append(")\n");
            text.Append("Seed:        ").Append(seed).Append('\n');
            text.Append("Rule:        ").Append(sample.Rule).Append('\n');
            text.Append("Surface:     ").Append(sample.SurfaceHeight)
                .Append(" (soil ").Append(sample.SoilDepth).Append(")\n");
            text.Append("Noise:       ").Append(Format(
                HeightFunction.GetNoise(worldX, worldZ, seed,
                    stage1Settings.TerrainFrequency))).Append('\n');
            text.Append("Density:     ").Append(sample.IsSolid ? "Solid" : "Empty").Append('\n');
            text.Append("DensityMod:  ").Append(ResolveDensityModifier(sample)).Append('\n');
            text.Append("Material:    ").Append(sample.Material).Append('\n');
            text.Append("CaveDensity: ").Append(Format(sample.CaveDensity)).Append('\n');
            text.Append("CaveState:   ").Append(ResolveCaveState(sample)).Append('\n');
            text.Append("Deposit:     ").Append(sample.DepositZone).Append('\n');
            text.Append("Biome:       ").Append(sample.Biome);

            return text.ToString();
        }

        private static string Format(float value)
        {
            return value.ToString("F6", CultureInfo.InvariantCulture);
        }

        public override string ToString()
        {
            return "GeologySampler(seed=" + stage1Settings.Seed +
                   ", worldHeight=" + stage1Settings.WorldHeight +
                   ", " + geologySettings + ")";
        }
    }

    /// <summary>
    /// 洞穴状态（由 <see cref="GeologySample.CaveActive"/> 与最终命中的规则共同决定）：
    ///   · <see cref="Inactive"/>：噪声未达阈值；
    ///   · <see cref="Carved"/>：噪声达阈值且**生效**（最终规则就是 Cave，密度被改成空）；
    ///   · <see cref="Suppressed"/>：噪声达阈值但被更高优先级覆盖（例如世界之外 P0）。
    /// </summary>
    public enum GeologyCaveState
    {
        Inactive = 0,
        Carved = 1,
        Suppressed = 2
    }

    /// <summary>
    /// 密度修正量（P1 对密度的作用）：
    ///   · <see cref="None"/>：密度由 P2/P3 决定（未经洞穴修改）；
    ///   · <see cref="Carve"/>：洞穴把实体改成空（`Rule == Cave`）—— **只改密度**，
    ///     不生成体素、不生成几何、不生成 Entity。
    /// </summary>
    public enum GeologyDensityModifier
    {
        None = 0,
        Carve = 1
    }
}
