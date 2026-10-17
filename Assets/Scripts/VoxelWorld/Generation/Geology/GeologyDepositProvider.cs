namespace VoxelWorld.Generation.Geology
{
    /// <summary>
    /// <see cref="IDepositProvider"/> 的 V1 实现（Geology Stage C）：用 <see cref="GeologyNoise"/>
    /// 的**世界坐标噪声**描述"这里是否存在资源"。
    ///
    /// 规则（全部显式、确定、无副作用）：
    ///   1. **显式开关**：`GeologySettings.EnableDeposits == false` ⇒ 恒返回
    ///      <see cref="DepositSample.None"/>（不产生任何资源信息）；
    ///   2. **只在实体内**：由 <see cref="GeologySampler.Sample"/> 判定非实体（空气、地表之上、
    ///      世界之外、被洞穴挖空）⇒ 返回 None（地质规则 §6.1：不得在空气中"存在资源"）；
    ///   3. **阈值由丰富度决定**（整数千分比比较，避免浮点判定）：
    ///      `thresholdMilli = 1000 - 20 * DepositRichness`
    ///      ⇒ 丰富度 0 ⇒ 阈值 +1.000（几乎没有资源）、50 ⇒ 0.000、100 ⇒ -1.000（实体处全部为资源）；
    ///      噪声（[-1,1) 的千分比取整）`noiseMilli >= thresholdMilli` 即视为存在资源；
    ///   4. **类型标识**：噪声高值带（≥ +0.5）⇒ <see cref="GeologyDepositZone.ZoneB"/>
    ///      （更稀有/更富集的带），其余 ⇒ <see cref="GeologyDepositZone.ZoneA"/>
    ///      （具体矿种语义属后续阶段；本阶段只输出占位类型标识）；
    ///   5. **丰富度**：由噪声强度线性映射到 0..100（`50 + noise × 50`）；
    ///   6. **等级**：由**深度**决定（`SurfaceHeight - worldY`，每 <see cref="GradeDepthStepVoxels"/>
    ///      体素升 1 级，1..5 级）—— 深浅分带是矿产常识且完全确定；
    ///   7. **绝不生成对象**：不创建晶簇/矿石节点/Entity，不写体素，不触碰 Runtime / Mesh / View。
    ///
    /// 纯度：纯 C#、只读依赖（<see cref="GeologySampler"/> 提供参数与密度判定）、
    /// 无可变静态字段、无 UnityEngine 标识符。
    /// </summary>
    public sealed class GeologyDepositProvider : IDepositProvider
    {
        /// <summary>每多少体素深度提升 1 个等级。</summary>
        public const int GradeDepthStepVoxels = 8;

        /// <summary>ZoneB（高值带）的噪声阈值（千分比）。</summary>
        public const int ZoneBThresholdMilli = 500;

        private readonly GeologySampler sampler;

        public GeologyDepositProvider(GeologySampler sampler)
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

        public DepositSample TryGetDeposit(int worldX, int worldY, int worldZ, int seed)
        {
            // 1) 显式关闭 ⇒ 无资源信息
            if (!sampler.Geology.EnableDeposits) return DepositSample.None;

            // 2) 只在实体内（空气/地表之上/世界之外/洞穴挖空 ⇒ 无资源）
            GeologySample geology = sampler.Sample(worldX, worldY, worldZ, seed);
            if (!geology.IsSolid) return DepositSample.None;

            // 3) 整数千分比阈值比较（确定性、无浮点判定）
            int thresholdMilli = 1000 - 20 * sampler.Geology.DepositRichness;

            float noise = GeologyNoise.Value3D(
                worldX, worldY, worldZ, seed, sampler.Geology.DepositCellVoxels);

            int noiseMilli = ToMilli(noise);
            if (noiseMilli < thresholdMilli) return DepositSample.None;

            // 4) 类型标识 / 5) 丰富度 / 6) 等级（全部由既有输入推导，不引入随机）
            DepositSample sample;
            sample.Exists = true;
            sample.Zone = noiseMilli >= ZoneBThresholdMilli
                ? GeologyDepositZone.ZoneB
                : GeologyDepositZone.ZoneA;
            sample.Richness = ClampRichness(50 + noiseMilli / 20);
            sample.Grade = ResolveGrade(worldY, geology.SurfaceHeight);
            return sample;
        }

        public bool TryGetDeposit(
            int worldX, int worldY, int worldZ, int seed, out DepositSample sample)
        {
            sample = TryGetDeposit(worldX, worldY, worldZ, seed);
            return sample.Exists;
        }

        // -----------------------------------------------------------------
        // 内部（纯整数运算）
        // -----------------------------------------------------------------

        /// <summary>噪声 → 千分比整数（截断向零；同输入恒同输出）。</summary>
        private static int ToMilli(float noise)
        {
            return (int)(noise * 1000f);
        }

        private static int ClampRichness(int value)
        {
            if (value < DepositSample.MinRichness) return DepositSample.MinRichness;
            if (value > DepositSample.MaxRichness) return DepositSample.MaxRichness;
            return value;
        }

        /// <summary>深度决定等级：越深等级越高（1..5）。</summary>
        private static int ResolveGrade(int worldY, int surfaceHeight)
        {
            int depth = surfaceHeight - worldY;
            if (depth < 0) depth = 0;

            int grade = DepositSample.MinGrade + depth / GradeDepthStepVoxels;
            if (grade > DepositSample.MaxGrade) grade = DepositSample.MaxGrade;

            return grade;
        }

        public override string ToString()
        {
            return "GeologyDepositProvider(" + sampler.Geology + ")";
        }
    }
}
