using System;

namespace VoxelWorld.Generation.Geology
{
    /// <summary>
    /// GeologyWorldGenerator：`IWorldGenerator` 的地质实现（Geology Stage D-1）。
    ///
    /// 规范来源：`Specifications/World/WorldGeneration_Geology_Runtime_Implementation.md`（§1–§8）。
    ///
    /// 定位：
    ///   · **`IWorldGenerator` 的一个新实现**（不修改抽象、不修改 `BasicTerrainGenerator`）；
    ///   · Runtime Core **无感替换**：Core 只认识 `IWorldGenerator` ⇒ 组装点换一个 `new` 即可，
    ///     Runtime / Streaming / Mesh / View / Bootstrap 零修改（GW9）；
    ///   · 全关地质特性时与 Stage 1 基线**逐体素一致**（GIV9 / GW2）。
    ///
    /// 生命周期（固定顺序）：
    /// <code>
    /// Generate(coordinate)
    ///    ↓
    /// GenerateVoxelData(chunk, seed)      逐体素：localX → localY → localZ
    ///    ↓
    /// Validate(chunk, coordinate, ...)     输出自检（失败抛异常，不返回非法 Chunk）
    /// </code>
    /// `Install` / `Register` **不属于本类**：它们由 Runtime Core 负责
    /// （`Runtime_Core_Implementation.md` §5.1/§5.2）—— 本类只产出**游离** VoxelChunk。
    ///
    /// 硬性禁令（§2.3）：
    ///   · 不写世界：不出现 `VoxelWorld`、不调用 `GetOrCreateChunk` / `RemoveChunk`；
    ///   · 不接触 Runtime：不出现 `VoxelRuntimeCore` / 调度类型、不碰脏标记；
    ///   · 不生成 Entity：不生成晶簇/节点/植被，不出现任何实例化 API；
    ///   · 不产几何：不出现 Mesh 类型；
    ///   · 不依赖时间/随机：噪声只来自 `GeologyNoise`（经 `GeologySampler`）；
    ///   · 判定链**不复制**：不调用 `BasicTerrainGenerator` / `ClassifyVoxel`，
    ///     也不重写 Height/Cave 逻辑 —— 一切判定都委托给 `GeologySampler`（§4/§3.1）。
    /// </summary>
    public sealed class GeologyWorldGenerator : IWorldGenerator
    {
        private readonly WorldGenerationSettings stage1Settings;
        private readonly GeologySettings geologySettings;
        private readonly GeologySampler sampler;
        private readonly IBiomeProvider biomeProvider;
        private readonly IDepositProvider depositProvider;

        public GeologyWorldGenerator(
            WorldGenerationSettings stage1Settings,
            GeologySettings geologySettings,
            IBiomeProvider biomeProvider,
            IDepositProvider depositProvider)
        {
            string reason;

            if (!stage1Settings.IsValid(out reason))
            {
                throw new ArgumentException(
                    "WorldGenerationSettings 非法：" + reason, "stage1Settings");
            }

            if (geologySettings == null)
            {
                throw new ArgumentNullException("geologySettings");
            }

            if (!geologySettings.IsValid(out reason))
            {
                throw new ArgumentException(
                    "GeologySettings 非法：" + reason, "geologySettings");
            }

            // 显式约束：声明要应用群系修正却没有 provider ⇒ 拒绝启动（禁止静默恒等）
            if (geologySettings.EnableBiomeModifiers && biomeProvider == null)
            {
                throw new ArgumentException(
                    "EnableBiomeModifiers = true 时必须提供 biomeProvider（否则无法生效）",
                    "biomeProvider");
            }

            this.stage1Settings = stage1Settings;
            this.geologySettings = geologySettings;
            this.biomeProvider = biomeProvider;
            this.depositProvider = depositProvider;

            // 判定链的唯一实现（Stage B）：本类不重写 P0–P3，也不复制 Height/Cave 逻辑
            this.sampler = new GeologySampler(stage1Settings, geologySettings);
        }

        // -----------------------------------------------------------------
        // 只读暴露
        // -----------------------------------------------------------------

        /// <summary>契约要求的 Stage 1 参数（含 `Seed`，只读）。</summary>
        public WorldGenerationSettings Settings
        {
            get { return stage1Settings; }
        }

        /// <summary>地质参数（只读）。</summary>
        public GeologySettings Geology
        {
            get { return geologySettings; }
        }

        /// <summary>判定链（只读；诊断 / 可解释性入口）。</summary>
        public GeologySampler Sampler
        {
            get { return sampler; }
        }

        /// <summary>群系查询接口（只读；可为 null ⇒ 恒等，等价 Stage 1）。</summary>
        public IBiomeProvider Biomes
        {
            get { return biomeProvider; }
        }

        /// <summary>资源查询接口（只读；可为 null）。本类不使用它的结果生成任何体素/对象。</summary>
        public IDepositProvider Deposits
        {
            get { return depositProvider; }
        }

        // -----------------------------------------------------------------
        // Generate：唯一入口（只产出游离 Chunk）
        // -----------------------------------------------------------------

        /// <summary>
        /// 生成一个**游离**的 Chunk（不属于任何世界、不安装、不注册）。
        /// 输出与"哪个 Chunk / 何时 / 以何顺序"无关：只由 `(Seed, 参数, 世界坐标)` 决定。
        /// </summary>
        public VoxelChunk Generate(VoxelCoordinate chunkCoordinate)
        {
            VoxelChunk chunk = new VoxelChunk(chunkCoordinate);

            GenerateVoxelData(chunk, stage1Settings.Seed);

            string reason;
            if (!Validate(chunk, chunkCoordinate, out reason))
            {
                // 绝不返回非法/半成品输出（生成器自检失败必须暴露）
                throw new InvalidOperationException(
                    "GeologyWorldGenerator 输出未通过自检（" + chunkCoordinate + "）：" + reason);
            }

            return chunk;
        }

        // -----------------------------------------------------------------
        // GenerateVoxelData：世界坐标采样 + 固定顺序写入
        // -----------------------------------------------------------------

        /// <summary>
        /// 逐体素填充：外层循环只决定坐标，内层判定全部委托 <see cref="GeologySampler"/>。
        ///
        /// 固定顺序 `localX → localY → localZ`（与 `Runtime_Core_Implementation.md` §5.3-R2 一致）；
        /// 世界坐标经 VoxelCore 换算 `chunk.LocalToWorldCoordinate(...)` 取得，
        /// **Chunk 坐标不参与任何规则计算**（不进入噪声、哈希、判定链或高度判断）。
        /// </summary>
        private void GenerateVoxelData(VoxelChunk chunk, int seed)
        {
            for (int localX = 0; localX < VoxelChunk.ChunkSize; localX++)
            {
                for (int localY = 0; localY < VoxelChunk.ChunkSize; localY++)
                {
                    for (int localZ = 0; localZ < VoxelChunk.ChunkSize; localZ++)
                    {
                        // 世界坐标：交给 VoxelCore 的换算（不得自实现坐标规则）
                        VoxelCoordinate world =
                            chunk.LocalToWorldCoordinate(localX, localY, localZ);

                        // 判定链：唯一实现在 GeologySampler（P0→P1→P2→P3）
                        GeologySample sample =
                            sampler.Sample(world.x, world.y, world.z, seed);

                        VoxelType type = sample.IsSolid
                            ? ResolveMaterial(world.x, world.y, world.z, seed, sample)
                            : VoxelType.Air;

                        // 新 Chunk 必为空 ⇒ 跳过 Air 写入与写入 Air 等价（不影响任何其它逻辑）
                        if (type == VoxelType.Air) continue;

                        chunk.SetVoxel(localX, localY, localZ, type);
                    }
                }
            }
        }

        // -----------------------------------------------------------------
        // Biome 接入（唯一允许的影响：地表材料选择）
        // -----------------------------------------------------------------

        /// <summary>
        /// 解析最终材料：
        ///   · Biome 关闭（或未注入 provider）⇒ **恒等**返回采样结果（等价 Stage 1，GIV9）；
        ///   · 只作用于**地表层**（`Rule == Surface`，即 `y == SurfaceHeight` 那一层）；
        ///   · `BiomeA`（占位：冷干）⇒ 地表材料为 `Stone`（裸岩：无植被覆盖）；
        ///     `BiomeB` / `None` ⇒ 保持采样结果（`Grass`）；
        ///   · **不产生**植被/结构/Entity，**不改变**密度分布与其它层材料。
        /// </summary>
        private VoxelType ResolveMaterial(
            int worldX, int worldY, int worldZ, int seed, GeologySample sample)
        {
            if (biomeProvider == null || !geologySettings.EnableBiomeModifiers)
            {
                return sample.Material;
            }

            if (sample.Rule != GeologyRule.Surface)
            {
                return sample.Material;
            }

            BiomeSample biome = biomeProvider.SampleBiome(worldX, worldY, worldZ, seed);

            return biome.Biome == GeologyBiome.BiomeA
                ? VoxelType.Stone
                : sample.Material;
        }

        // -----------------------------------------------------------------
        // Validate：生成器自检（§2.4）
        // -----------------------------------------------------------------

        /// <summary>
        /// 输出自检：坐标自洽、4096 个类型合法（`VoxelType` 冻结为 4 值）、新建 Chunk 的初始脏语义。
        /// 返回 false 时 <see cref="Generate"/> 必须抛出异常（禁止返回非法输出）。
        /// </summary>
        private bool Validate(
            VoxelChunk chunk, VoxelCoordinate chunkCoordinate, out string reason)
        {
            if (chunk == null)
            {
                reason = "输出 chunk 为 null";
                return false;
            }

            if (chunk.Coordinate != chunkCoordinate)
            {
                reason = "输出坐标不自洽：请求 " + chunkCoordinate + "，得到 " + chunk.Coordinate;
                return false;
            }

            for (int localX = 0; localX < VoxelChunk.ChunkSize; localX++)
            {
                for (int localY = 0; localY < VoxelChunk.ChunkSize; localY++)
                {
                    for (int localZ = 0; localZ < VoxelChunk.ChunkSize; localZ++)
                    {
                        int raw = (int)chunk.GetVoxel(localX, localY, localZ).Type;

                        if (raw < (int)VoxelType.Air || raw > (int)VoxelType.Stone)
                        {
                            reason = "存在非法体素类型：" + raw +
                                     "（" + localX + "," + localY + "," + localZ + "）";
                            return false;
                        }
                    }
                }
            }

            if (!chunk.IsDirty)
            {
                reason = "新建 Chunk 必须为脏（尚未安装/构建）";
                return false;
            }

            reason = null;
            return true;
        }

        public override string ToString()
        {
            return "GeologyWorldGenerator(seed=" + stage1Settings.Seed +
                   ", " + geologySettings +
                   ", biomes=" + (biomeProvider == null ? "none" : "yes") +
                   ", deposits=" + (depositProvider == null ? "none" : "yes") + ")";
        }
    }
}
