# R41 — World V2 正式生成流程接线审计（只读，不实施）

- 项目：`D:\gamemaking\My project`（Unity **2022.3.62f1c1**）
- 分支 / HEAD：`main` / **`37246478ed354a1e9a0ce7a3a3d9457caf188062`**
- 本轮性质：**只读审计 + 方案比较**。未修改任何生产代码、验证器、契约、场景、Prefab、`.meta`；未更新/重钉任何哈希；未执行任何 git 写操作。
- 唯一新增文件：`_phase2b_validation/round41_worldv2_production_wiring_audit.md`（本报告）

---

## 1. 现场基线与保护检查

| 项 | 实测值 | 结论 |
|---|---|---|
| HEAD | `37246478ed354a1e9a0ce7a3a3d9457caf188062` | 与基线**一致** |
| 分支 / 上游 | `main` / `origin/main` | 一致 |
| 暂存区 | 空（`git diff --cached --name-only` 无输出） | 未暂存任何改动 |
| `git status --porcelain` | **27 条**（tracked `M` **20** + untracked 源文件 **4** + 证据目录 **3**） | 与 R40 收尾**完全相同** |
| `git diff --stat` | `20 files changed, 390 insertions(+), 142 deletions(-)` | 与 R40 收尾**完全相同** |

受保护证据文件内容哈希（本轮开始即记录，收尾复核见 §8）：

| 文件 | 行数 | 字节 | SHA256（前 16） |
|---|---|---|---|
| `_phase2b_validation/sealevel_round7_progress.md` | **1032** | 87345 | `D638D271767BFFF9` |
| `_phase2b_validation/round40_worldskeleton_sealevel_unification_report.md` | 239 | 17777 | `0407A4D7EF109C95` |
| `_phase2b_validation/round39_worldskeleton_sealevel_contract_audit.md` | 268 | 18704 | `1E5A5F029F3F91C2` |
| `Assets/Scripts/World/V2/WorldSkeletonFacts.cs` | **120** | 6999 | `98B59916CA9CDBCC` |

Legacy 冻结基线复核（规格登记值 vs 实测，**全部一致**）：

| 文件 | 规格登记 SHA16 | 实测 SHA16 | 行数 |
|---|---|---|---|
| `Assets/Scripts/Terrain/DensityTerrain.cs` | `0EB935DBA79CBD40` | `0EB935DBA79CBD40` | 1855 |
| `Assets/Scripts/Terrain/TerrainMining.cs` | `E802CBA26C507620` | `E802CBA26C507620` | 47 |
| `Assets/Scripts/VoxelWorld/VoxelWorld.cs` | `D9E749A466635918` | `D9E749A466635918` | 258 |

**现场无「无法安全解释的变化」** ⇒ 未触发停止条件，审计继续。

---

## 2. 真实生产调用链（以代码行号为证据）

### 2.0 先给结论：项目里存在**两套世界系统**，运行中的不是 VoxelWorld 栈

| 事实 | 证据 |
|---|---|
| `SampleScene.unity` 挂载 **`DensityTerrain`**（`m_Enabled: 1`，GameObject 名 `Terraria`，`chunkCount (2,1,2)`、`resolution 75`、`useNoise 1`、`height 15.7`） | 场景内 guid `3af3bcfc875f4cbfae75f1f95dee772a` 命中 **1** 次 |
| `SampleScene.unity` **未**挂载 `VoxelWorldStreamingBootstrap` / `ChunkStreamingDriver` / `VoxelRuntimeCore` / `VoxelWorldDebug` | 各自 guid 命中 **0** 次 |
| `VoxelWorldStreamingBootstrap`（guid `2c531141a81459d499f3f2959fe28988`）在 **`.unity` / `.prefab` / `.asset` 全库命中 0 个**；6 个 prefab 均不含它 | 全 Assets 扫描 |
| 生产代码中**无任何**代码实例化 Bootstrap（`AddComponent<VoxelWorldStreamingBootstrap>` / `new` 均 0；其余 `AddComponent` 命中均为 UI/地形派生对象） | `Assets/Scripts` 全量扫描 |
| 规格层**已登记**该事实 | `Specifications/Architecture/Legacy_Status.md` **LS10**、**LS29**（Terrain 真值双轨）、**LS16**；`WorldGeneration_Architecture_Audit.md` **P1-6**「新栈未接入任何场景」 |

> 因此本审计区分两个层次：**① 代码级生产路径**（`Assets/Scripts`，非 Editor，可编译进 `Assembly-CSharp`）；**② 实际运行世界**（场景实例化，玩家可见）。VoxelWorld 栈满足 ①、不满足 ②；`DensityTerrain` 满足 ②，且被规格判为 Legacy 冻结。

### 2.1 代码级生产路径（VoxelWorld + Geology 栈，逐环节）

| # | 文件 | 类 / 方法 | 谁调用它 | 它调用谁 | 传递的数据 |
|---|---|---|---|---|---|
| L1 | `Assets/Scripts/Game/VoxelWorldStreamingBootstrap.cs`（395 行） | `VoxelWorldStreamingBootstrap.Awake()` **L155** → `Build()` **L179** | Unity 生命周期（若被场景实例化；**当前无实例**） | 见 L2–L7 | Inspector 显式字段（`Seed=12345`、`TerrainHeight=24`、`SoilDepth=3`、`WorldHeight=64`、`SeaLevel=64`、`TerrainFrequency=0.06`、`TerrainAmplitude=6`、地质 7 项开关） |
| L2 | 同上 | `Build()` **L194–L197** | L1 | `WorldGenerationSettings` 七参数构造 + `IsValid` | `(Seed, TerrainHeight, SoilDepth, WorldHeight, TerrainFrequency, TerrainAmplitude, SeaLevel)` |
| L3 | 同上 | `Build()` **L271–L288** | L1 | `GeologySettings` + `IsValid` | 洞穴/矿藏/群系开关与频率、丰度 |
| L4 | 同上 | `Build()` **L291** | L1 | `new GeologySampler(generationSettings, geologySettings)` | 两套设置（只读） |
| L5 | 同上 | `Build()` **L293–L294** | L1 | `new GeologyBiomeProvider(sampler)` / `new GeologyDepositProvider(sampler)` | 采样器 |
| L6 | 同上 | `Build()` **L296–L297** | L1 | `new GeologyWorldGenerator(genSettings, geoSettings, biomeProvider, depositProvider)` | 四依赖显式注入 |
| L7 | 同上 | `Build()` **L308 / L310 / L311** | L1 | `VoxelRuntimeCore(world, generator, mesher, presenter, coreSettings)` → `RuntimeCoreRequestSink(core)` → `ChunkStreamingDriver(sink, streamingSettings)` | 生成器以**抽象** `IWorldGenerator` 注入 |
| L8 | 同上 | `Step()` **L327–L337** | `Update()` L160（`TickEveryFrame`） | `streaming.Update(WorldPositionToVoxelCoordinate(focus))` L334 → `core.Tick()` L335 | 焦点世界坐标 → 整数体素坐标（floor） |
| L9 | `Assets/Scripts/VoxelWorld/Runtime/VoxelRuntimeCore.cs`（980 行） | Core Tick 内 **L517** `source = generator.Generate(coordinate);` | L8 | `IWorldGenerator.Generate` | `VoxelCoordinate`（Chunk 坐标） |
| L10 | `Assets/Scripts/VoxelWorld/Generation/Geology/GeologyWorldGenerator.cs`（276 行） | `Generate(coordinate)` **L128** → `GenerateVoxelData(chunk, seed)` **L156** | L9 | `sampler.Sample(world.x, world.y, world.z, seed)` **L170**；`ResolveMaterial(...)` **L197** | 逐体素（16³=4096）固定顺序 `localX→localY→localZ` |
| L11 | `GeologyWorldGenerator.ResolveMaterial` **L197–L215** | 群系钩子 | L10 | `biomeProvider.SampleBiome(x,y,z,seed)` **L210** | **唯一**外部"世界描述"入口；仅当 `EnableBiomeModifiers == true` 且 `Rule == Surface`；`BiomeA ⇒ Stone` |
| L12 | `Assets/Scripts/VoxelWorld/Generation/Geology/GeologySampler.cs`（350 行） | `Sample(...)` **L93** → `Resolve(...)` **L165** | L10 / providers | P0 `WorldBounds` L183 → P1 `Cave` L189 → P2 `AboveSurface` L195 → P3 `ResolveLayerMaterial` **L224** | `GeologySample`（`Rule` / `IsSolid` / `Material` / `SurfaceHeight` / `SoilDepth` / `CaveActive`） |
| L13 | 同上 | `ResolveSurfaceHeight` **L248–L258** | L12 | `HeightFunction.GetTerrainHeight(worldX, worldZ, stage1Settings)` **L252** | 列级地表高度（整数体素） |
| L14 | `Assets/Scripts/VoxelWorld/Generation/HeightFunction.cs`（138 行） | `GetTerrainHeight` **L27–L48** | L13 与 V2 | `GetNoise` → `clamp(round(TerrainHeight + TerrainAmplitude × noise), 0, WorldHeight−1)` | **不读取** `SeaLevel`（该文件 0 命中） |
| L15 | `GeologyWorldGenerator.Validate` **L225–L266** | 输出自检 | L10 | 坐标自洽 / 4096 类型合法 / 新 Chunk 必脏 | 失败即抛异常（不返回半成品） |
| L16 | `VoxelRuntimeCore` + `ChunkViewPresenter` + `VoxelChunkView` | Install / Register / Mesh / Present | L9 | Mesher 适配器 → Presenter → Unity 容器 | `VoxelChunk`（世界真值）→ Mesh（派生） |

**生物群系 / 区域选择实际形态**：
- 群系：`GeologyBiomeProvider.SampleBiome`（**生产在使用**，噪声 + 海拔递减 + 整数定点温度/湿度 ⇒ `BiomeA/B/None`），但**默认 `EnableGeologyBiomeModifiers = false`**（Bootstrap L91）⇒ 默认世界 `Biome = None` ⇒ 表面材料恒为采样结果。
- 区域：**生产链中不存在 Region 概念**。V2 的 `RegionPipeline` / `RegionDetermination` / `RegionModifierDetermination` 只被 `WorldStatePipeline` 调用，而 `WorldStatePipeline` 无生产调用者（§3）。

### 2.2 实际运行世界路径（DensityTerrain 栈，Legacy）

| 环节 | 位置 | 说明 |
|---|---|---|
| 场景挂载 | `SampleScene.unity` → `DensityTerrain`（`m_Enabled: 1`） | 唯一在运行的世界载体 |
| 参数来源 | `DensityTerrain.cs` **L31–L115** MonoBehaviour 字段（`voxelSize=0.1`、`resolution=75`、`height=15.7`、`noiseScale=0.15`、`noiseStrength=2.37`、`cavityRadius`…） | **不使用** `WorldGenerationSettings`（该文件对 `WorldGenerationSettings`/`HeightFunction`/`IWorldGenerator`/`SeaLevel` 命中均为 **0**） |
| 生成 | `DensityFieldGenerator.cs`（556 行）+ `DensityField`/`MaterialField` + Marching Cubes | 与 VoxelWorld 的离散体素链**完全独立** |
| 玩家交互 | `PlayerMiningController.cs` **L31/L996–L1000** `FindObjectOfType<DensityTerrain>()` → `VoxelField.Set(Air)` | 挖掘写的是 Legacy 真值 |

> **结论**：`SeaLevel`、`WorldGenerationSettings` 乃至整个 World V2 目前**不可能**影响实际运行世界——运行世界根本不读这些类型。

---

## 3. 目标符号引用统计（生产 vs Editor，实测）

口径：**生产** = `Assets/Scripts/**`（153 个 `.cs`）；**Editor** = `Assets/Editor/**`（48 个 `.cs`）。`[n]` = 该文件内出现次数。

| 目标符号 | 生产侧引用 | Editor 侧引用 | 判定 |
|---|---|---|---|
| **`WorldStatePipeline`** | 3 文件，**全部非调用**：`WorldParameterEvaluator.cs[1]`（`///` 注释）、`WorldParameterQuery.cs[1]`（`///` 注释）、`WorldStatePipeline.cs[1]`（自身声明） | `WorldV2ParameterPipelineValidation.cs[7]` | **生产调用者 = 0** |
| **`WorldParameterQuery`** | 1 文件（**仅自身** `[2]`：类声明 + `ToString`） | **0 文件** | **生产调用者 = 0** |
| **`WorldParameterQuery.Evaluate`** | 仅 `WorldParameterQuery.cs:49` 定义本身 | 0 | 同上 |
| **`WorldParameterEvaluator`** | 6 文件，**全部在 `World/V2` 内**：自身`[2]`、`WorldParameterQuery.cs[5]`（含 L51 实调用）、`WorldParameterRule.cs[2]`（注释）、`WorldParameterRules.cs[1]`（注释）、`WorldParameterState.cs[2]`（注释）、`WorldStatePipeline.cs[8]`（含 L45/L60/L65 实调用） | `WorldV2ParameterPipelineValidation.cs[13]` | **跨层调用者 = 0**（仅 V2 内部 + 验证器） |
| **`WorldSkeletonFacts.TryEvaluate`** | 1 文件（**仅自身** `[8]`，全为声明/注释/单构造点） | `WorldV2ParameterPipelineValidation.cs[18]` | **生产调用者 = 0**（R40 结论复核成立） |
| **`WorldGenerationSettings.SeaLevel`** | 生产仅 4 处**语义使用**：`Bootstrap L67/L195`（声明与传参）、`WorldGenerationSettings.IsValid L132–L135`（范围校验）、`WorldParameterRules.OceanRule L143`（`elevation < settings.SeaLevel`）、`WorldSkeletonFacts L110`（同式）；`HeightFunction`/`BasicTerrainGenerator`/`GeologySampler`/`DensityTerrain` 命中 **0** | 多文件（验证器断言） | **不参与任何地形/体素生成** |
| **`IBiomeProvider`** | **7 文件**：`VoxelWorldStreamingBootstrap.cs[2]`、`GeologyBiomeProvider.cs[2]`、`GeologySample.cs[1]`、`GeologySampler.cs[1]`、`GeologySettings.cs[1]`、`GeologyWorldGenerator.cs[3]`、自身`[1]` | 4 文件（GRI/StageA/StageC/StageD） | **生产已接线**（真实参与生成，默认关闭） |
| **`GeologyBiomeProvider`** | 2 文件：`Bootstrap[1]`（L293 构造）、自身`[3]` | 4 文件 | 生产已接线 |
| **`GeologyWorldGenerator`（实际入口）** | 4 文件：`Bootstrap[2]`（L296 构造 + 属性）、`GeologySampler.cs[1]`/`GeologySettings.cs[2]`（注释）、自身`[5]` | 4 文件（GRI 14 / StageD 23 等） | 生产已接线（构造点唯一） |
| `RegionPipeline` | 2 文件：自身`[1]`、`WorldStatePipeline.cs[3]`（L62 实调用） | **0 文件** | **无任何外部消费者** |
| `RegionDetermination` | 3 文件（`RegionContext`/自身/`RegionPipeline` L63） | **0 文件** | 仅 Region 内部 |
| `RegionModifierDetermination` | 2 文件（自身 / `WorldStatePipeline` L63） | **0 文件** | 仅 V2 内部 |
| `RegionContext` / `RegionIdentity` / `RegionSourceCandidate` / `RegionModifier` | 5 / 4 / 1 / 5 文件（全在 `World/V2`） | 0 / 0 / 0 / 1 | 仅 V2 内部 |
| `WorldParameterRules` / `WorldParameterState` / `WorldCoordinate` / `WorldSeed` / `WorldParameterHash` | 3 / 5 / 12 / 10 / 3 文件（全在 `World/V2`） | 0 / 1 / 1 / 1 / 0 | 仅 V2 内部 |
| `IWorldGenerator` | 5 文件：`Bootstrap[5]`、`BasicTerrainGenerator`、自身、`GeologyWorldGenerator[4]`、`VoxelRuntimeCore[2]` | 8 文件 | 生产已接线（唯一抽象边界） |
| `HeightFunction` | 7 文件：`BasicTerrainGenerator[3]`、自身、`GeologySampler[5]`、`WorldParameterHash[1]`、`WorldParameterRule[1]`、`WorldParameterRules[3]`、`WorldSkeletonFacts[2]` | 10 文件 | 生产已接线，且被 V2 复用 |
| `VoxelRuntimeCore` / `ChunkStreamingDriver` / `BasicTerrainGenerator` | 6 / 4 / 4 文件 | 7 / 6 / 9 文件 | 生产已接线（但栈未实例化，见 §2.0） |
| `WorldBiomeProvider` | **0**（全库不存在） | 0 | 未存在（本轮禁止新增，未新增） |

> **机械扫描**：`Assets/Scripts` 中除 `World/V2/**` 自身外，**没有任何文件引用任何 `World.V2` 命名空间类型**（上表 `WorldStatePipeline` / `WorldParameterQuery` / `WorldSkeletonFacts` / `WorldParameterEvaluator` 的生产引用全部落在 `World/V2/*` 内部，且多为 `///` 注释）。

---

## 4. 当前断点及其直接原因

### 断点 B1 — **V2 数据出口断点**（最直接）
- **现象**：`WorldStatePipeline`（V2 唯一编排入口，68 行）在生产侧**只有 2 处 `///` 注释提及**，无任何调用。
- **直接原因**：`Assets/Scripts` 中除 `World/V2/**` 自身外，零文件引用 `World.V2` 类型；`Bootstrap.Build()`（唯一组装点）只 `new` VoxelWorld/Geology 栈的 8 个对象，**未 `new` 任何 V2 类型**。
- **缺失的那一条调用**：`VoxelWorldStreamingBootstrap.Build()` →（任何）V2 构造/查询调用。这一条不存在。

### 断点 B2 — **生成侧无读入口**
- **现象**：生成链中唯一接受"外部世界描述"的接口是 `IBiomeProvider.SampleBiome`，且受 `EnableBiomeModifiers`（默认 `false`）+ `Rule == Surface` 双重限制，影响范围只有**地表材料**（`Grass` → `Stone`）。
- **直接原因**：`GeologySampler.Resolve` 的 P0–P3 判定（L165–L206）全部由 `WorldGenerationSettings` + `GeologySettings` + 噪声决定；`GeologySample` 结构（`Rule/IsSolid/Material/SurfaceHeight/SoilDepth/CaveActive`）**没有**任何 `Ocean`/`Continent`/`Region` 字段。
- **缺失的数据传递**：V2 的 `Ocean`（bool）/`Continent`（int）/8 参数快照 → `GeologySample` 或 `ResolveMaterial` 的任何字段。

### 断点 B3 — **宿主断点（决定性）**
- **现象**：即使把 V2 接进 VoxelWorld 栈，**玩家仍然看不到任何变化**——因为该栈**从未被场景实例化**（Bootstrap guid 在 `.unity/.prefab/.asset` 命中 0；无代码实例化）。
- **直接原因**：`SampleScene.unity` 挂的是 Legacy `DensityTerrain`（`m_Enabled: 1`）。
- **规格登记**：`Legacy_Status.md` **LS10 / LS29 / LS16**；`WorldGeneration_Architecture_Audit.md` **P1-6**；`WorldV2_Implementation_Boundary_Plan.md` **IBP28**（"Phase 1 不切换场景、不接入运行角色 ⇒ 场景接入属未来阶段（`M7` 类决策）"）。

### 断点 B4 — **语义断点（会立即爆炸的那一个）**
- **事实**：`Elevation = clamp(round(TerrainHeight + TerrainAmplitude × noise), 0, WorldHeight−1)`（`HeightFunction.cs` L39–L47，`noise ∈ [−1, 1]`），默认 `TerrainHeight=24`、`TerrainAmplitude=6` ⇒ **`Elevation ∈ [18, 30]`**；而默认配置 `WorldHeight=64` 且 `SeaLevel=64`（Bootstrap L64/L67；`WorldGenerationSettings.Default` 亦为 64/64）。
- **推论**：`Ocean = Elevation < SeaLevel = 64` ⇒ **默认配置下每一列都是"海洋"**。
- **实证**：R40 真实运行断言 **B3**：`SeaLevel = WorldHeight ⇒ 所有合法列均为海洋（列数 35）`——已由 Unity Batchmode 实跑确认。
- **直接原因**：`SeaLevel` 的登记默认值 `64` 与 `WorldHeight` 默认值 `64` 相等；该"全海洋"语义是否合法**尚待 owner 裁决**（R40 §9 UNKNOWN 已登记）。
- **后果**：任何"让 `Ocean` 驱动生成"的接线，在默认配置下会把世界变成全海洋/海底材料 ⇒ **不得在未裁决前默认开启**。

### 断点 B5 — **治理断点**
- `A-2` / `CD-02`（Terrain 真值双轨）为 **Blocker** 且**未裁决**；`AD-1`（旧/新世界关系：并存 / 迁移 / 替换 / 双轨）为**未决决策**；`LS78` 把"V2 已在场景中接入并可运行"列为 V2 稳定判据之一。
- **直接原因**：场景级宿主选择属 owner 决策域，实施轮不得自行决定。

### 4.x 职责边界现状（第 3 项要求）

| 层 | 当前是否分离 | 证据 |
|---|---|---|
| WorldSkeleton（大尺度地理事实） | **分离且未接入** | `WorldSkeletonFacts` 生产引用仅自身；`Elevation` 复用 `HeightFunction`、`Ocean` 用 `settings.SeaLevel`（R40 契约仍有效，见下） |
| Region（区域身份/环境） | **分离且未接入** | Region 全部类型生产者=0 调用者；`RegionModifierDetermination` 仍为占位 `[0, 0.25)`（`WorldStatePipeline` L25 自注） |
| Terrain / Geology（地形与地质生成） | **分离、生产在用** | `GeologySampler` P0–P3 + `HeightFunction`；不读取 V2 任何类型 |
| Magic / WorldHistory / Resource / Ecology | **契约存在、实现未接入** | V2 侧仅有参数身份与占位规则（`MagicDensity`/`HolyInfluence`/`CorruptionInfluence` 均在 `WorldParameterRules`，无产线消费者）；矿藏 `IDepositProvider` 已接线但"只读查询、不生成任何体素/对象"（`GeologyWorldGenerator` L114–L118） |

**R40 契约有效性复核（未被本轮改变）**：
- `WorldSkeletonFacts.TryEvaluate(WorldCoordinate, WorldGenerationSettings, out WorldSkeletonFacts)` —— 生产 `int seaLevel` 残留 **0**；`bool ocean = elevation < settings.SeaLevel;`（L110）；`settings.SeaLevel` 命中 6 处（全为注释/该式）。
- `WorldParameterRules.OceanRule` L143 `return elevation < settings.SeaLevel ? 1f : 0f;` —— 与骨架事实同源，**无双载体分歧**。
- **未发现任何"为接线而合并系统"或"SeaLevel 影响 Elevation"的既成事实**：`HeightFunction`、`BasicTerrainGenerator`、`GeologySampler`、`DensityTerrain` 对 `SeaLevel` 命中均为 **0**。

---

## 5. 三个接入方案与风险比较

> 共同前提（本审计认定，不可绕过）：① 不得修改 Legacy `Assets/Scripts/Terrain/**`（`RAB-L1`/`RRD86`/`RID80`，且 `LS13/LS80` 冻结）；② 不得让 V2 依赖 Legacy 类型（`LS65`/`RAB-L2`）；③ 场景级宿主切换需 owner 裁决（`A-2`/`AD-1`）；④ `SeaLevel` 全海洋语义未裁决前不得默认开启 `Ocean` 驱动行为。

### 方案 1（**推荐**）— 只读事实接线（Observation Seam），零行为变更

| 项 | 内容 |
|---|---|
| 需要修改/新增文件 | **新增 1 个**：`Assets/Scripts/World/V2/WorldSkeletonProvider.cs`（薄封装：`settings + WorldCoordinate → WorldSkeletonFacts`，仅转发 `TryEvaluate`）；**可选修改 1 个**：`GeologyWorldGenerator.cs`（新增只读访问器 `TryGetSkeletonFacts(...)`，不参与生成） |
| 真正进入正式流程的 V2 数据 | `WorldSkeletonFacts`（`Elevation`/`Ocean`/`Continent`）与 `WorldParameterQuery.Evaluate` 的 `WorldParameterState` 快照，**可被生产链读取**；同时用于**守卫断言**：`facts.Elevation == sampler.SurfaceHeightAt(x,z)` |
| 是否改变地形/群系/世界分布 | **否，且结构上不可能**：`Elevation` 与生产 `SurfaceHeight` 同为 `HeightFunction.GetTerrainHeight`；`Ocean`/`Continent` 不进入任何写入路径 |
| 接口影响 | 纯新增（新类型 + 只读 getter）；`IWorldGenerator`/`GeologySample`/`WorldGenerationSettings` **零变更**；R40 签名不受影响 |
| 性能影响 | 每列 1 次纯函数调用（与现有 `ResolveSurfaceHeight` 同阶）；可按列缓存避免重复；无分配（`readonly struct`） |
| 兼容性 / 现有可用生成流程 | 默认路径**逐体素不变**（可用"接线前后 Chunk SHA 相同"证明）；不触碰 Legacy；不改场景 ⇒ 玩家可见世界**零变化** |
| 验证器影响 | 需**授权重钉**：`GeologyWorldGenerator.cs` 在 `GeologyRuntimeIntegrationValidation.cs` 的 **`FrozenFiles`** 登记 **1 处**（现值 `73EB59CFB939DC96`，改则需授权重钉）；若不动 `GeologyWorldGenerator` 而只在 V2 侧新增文件，则**重钉 0 处**（`WorldStatePipeline.cs` / `WorldSkeletonFacts.cs` 均未登记） |
| 如何测试 | 新增断言组 A：`facts.Elevation == SurfaceHeightAt` 逐列（多 Seed/多列）；B：`WorldParameterQuery.Evaluate` 的 `Elevation` 参数 == `HeightFunction` 结果；C：接线前后同一 Chunk 逐体素 SHA 相同（零行为变更证明） |
| 失败时如何不破坏现有流程 | 不涉及任何生成写入、不涉及场景、不改默认值；验证器失败仅阻断本轮提交，不影响运行世界 |

### 方案 2 — 体素栈内"表层材料"行为接线（opt-in）

| 项 | 内容 |
|---|---|
| 需要修改/新增文件 | 新增 `Assets/Scripts/World/V2/V2BiomeProvider.cs`（实现 `IBiomeProvider`，由 V2 参数映射 `Temperature/Moisture/Biome`）；修改 `VoxelWorldStreamingBootstrap.cs`（新增显式开关 `EnableV2SurfaceMaterial`，默认 `false`，+ 构造注入）、`GeologyWorldGenerator.cs`（`ResolveMaterial` 增加 ocean/continent 分支）、可能 `GeologySampler.cs`（`GeologySample` 增字段） |
| 真正进入正式流程的 V2 数据 | `Ocean`（bool）与 `Continent`（int）→ 地表材料（如 `Ocean ⇒` 无植被岩/砂；`Continent` 暂固定 1）；`Temperature/Moisture` → 群系划分（**与现有 `GeologyBiomeProvider` 噪声源冲突，需明确替换或并存策略**） |
| 是否改变地形/群系/世界分布 | **是**（仅当开关开启）。默认配置（`SeaLevel=64 == WorldHeight`）开启 ⇒ **全列海洋** ⇒ 世界完全变样 |
| 接口影响 | `IBiomeProvider` 语义扩张（"环境描述"出现两个实现）；`GeologySample` 可能新增字段（结构变更，影响 4 个验证器） |
| 性能影响 | 每体素已有 biome 调用同阶；V2 查询为纯函数 + 可缓存 |
| 兼容性 | 关闭开关时**必须**逐体素等价（需专门断言）；开启时世界改变 ⇒ 与既有 `GIV9`/`GW2`「全关逐体素一致」断言互为补充，不得破坏 |
| 验证器影响 | **重钉级联**：`VoxelWorldStreamingBootstrap.cs` 在 **5 个**验证器登记（GRI 的 `AuthorizedChangedFiles` + StageA/B/C/D 的 `FrozenFiles`，现值 `8A59289F0194D20F`）；`GeologyWorldGenerator.cs`/`GeologySampler.cs`/`GeologyBiomeProvider.cs`/`IBiomeProvider.cs` 分别在 GRI/StageC/StageD 登记 ⇒ 合计 **21 处**哈希需授权重钉（`VoxelWorldStreamingBootstrap` 5 / `GeologySettings` 4 / `GeologySample` 4 / `GeologySampler` 3 / `GeologyBiomeProvider` 2 / `IBiomeProvider` 2 / `GeologyWorldGenerator` 1）；`GeologyStageC/D` 已有群系断言需复核 |
| 如何测试 | 双跑对照组：`EnableV2SurfaceMaterial=false`（逐体素 SHA 必须与接线前相同）vs `true`；断言"海洋列的地表材料全为指定类型"、"Elevation/几何不变（只变材料）"；确定性断言（同 Seed 同坐标 ⇒ 同结果） |
| 失败时如何避免破坏 | 开关默认 `false` + `Build()` 失败即 `BuildError` 且不产生对象（现有语义）；开关关闭路径即回退到当前行为 |
| **先决条件** | **必须**先有 owner 对 `WorldHeight == SeaLevel == 64`（全海洋）语义的裁决，否则"开启即为全海洋"是不可接受的默认效果 |

### 方案 3 — 场景级接线（VoxelWorld 取代 DensityTerrain）

| 项 | 内容 |
|---|---|
| 需要修改文件 | `Assets/Scenes/SampleScene.unity`（挂载/参数化 Bootstrap 或 Prefab）、可能新增 Prefab、`VoxelWorldStreamingBootstrap.cs`（字段参数化）、`PlayerMiningController.cs`（挖掘目标切换，**Legacy 依赖**） |
| V2 数据进入方式 | 同方案 2，但**玩家可见** |
| 是否改变世界 | **是，彻底改变**（不同真值载体、不同坐标契约、不同 Chunk/Mesh/挖掘路径） |
| 风险 | ① `A-2`/`CD-02`/`AD-1` 未裁决 ⇒ **越权决策**；② `LS78` 把场景接入列为 V2 稳定判据 ⇒ 需完整替代旧运行角色；③ 坐标契约双轨（`LS30`）⇒ 存档/挖掘/查询会静默错位；④ Legacy 冻结（`LS13/LS80`）与玩家交互脚本耦合 |
| 结论 | **本轮不推荐、近期不实施**；需 owner 先裁决 `A-2`/`AD-1`（`Specifications/World/WorldGeneration_Architecture_Audit.md` `AD-1`） |

### 5.x 副作用差异汇总

| 维度 | 方案 1 | 方案 2 | 方案 3 |
|---|---|---|---|
| 玩家可见变化 | 无 | 仅在显式开启时 | 彻底改变 |
| 需授权重钉哈希 | 0–1 处（仅新增文件时 = 0） | **21 处**（实测明细见方案 2 行） | ≥ 10 处（含场景/Prefab） |
| 触碰 Legacy 代码 | 否 | 否 | 是（挖掘路径） |
| 需 owner 新裁决 | 否 | **是**（SeaLevel 语义） | **是**（`A-2`/`AD-1`） |
| 可独立验证 | 是（纯断言） | 是（开关双跑） | 否（需人工验收世界观感） |
| 破坏现有可用生成流程的风险 | **极低** | 中（开关隔离） | 高 |

---

## 6. 推荐方案与理由

### 推荐：**方案 1（只读事实接线）**，作为下一轮实施的唯一范围

**理由（按优先级）**：
1. **不触碰任何未裁决项**：不需要 `A-2`/`AD-1` 裁决，也不需要 `SeaLevel` 语义裁决，因此可以**现在就实施**且不越权。
2. **零行为风险**：`Elevation` 与生产 `SurfaceHeight` 同源（同一 `HeightFunction`），`Ocean`/`Continent` 不进入写入路径 ⇒ 世界分布**结构上不可能**改变，且可用"逐体素 SHA 不变"机械证明。
3. **它确实建立了缺失的那条边**：`Bootstrap`/生成器首次可以在生产代码中取到 V2 事实，断点 B1 的"缺失调用"被补上，且**不是**为了让类出现在生产调用图里（有守卫断言支撑：两套 `Elevation` 来源必须恒等，否则说明有人复制了高度算法）。
4. **反向保护**：一旦未来有人让 `SeaLevel` 影响 `Elevation`，或复制一套高度计算，方案 1 的守卫断言会立刻失败——这正好是对 R40 统一来源的**回归护栏**。
5. **成本最低**：可在**零哈希重钉**下完成（新文件 + `World/V2` 内部改动；`WorldStatePipeline.cs`、`WorldSkeletonFacts.cs`、`WorldParameterQuery.cs` 均**未登记**在 7 张冻结表中）。

**预期修改文件清单（方案 1，供下一轮授权时确认）**：

| 动作 | 文件 | 冻结表登记 | 是否需重钉 |
|---|---|---|---|
| 新增 | `Assets/Scripts/World/V2/WorldSkeletonProvider.cs`（+ `.meta`） | 未登记 | 否 |
| 新增（可选） | `Assets/Editor/WorldV2ProductionSeamValidation.cs`（+ `.meta`，`RunBatch`） | 未登记 | 否 |
| 修改（可选，默认不做） | `Assets/Scripts/VoxelWorld/Generation/Geology/GeologyWorldGenerator.cs`（+ 只读 getter） | **GRI `FrozenFiles`** 登记 1 处（现值 `73EB59CFB939DC96`） | **是（1 处）** |
| 修改（可选） | `Assets/Scripts/Game/VoxelWorldStreamingBootstrap.cs`（暴露 `SkeletonFacts` 只读属性） | **5 处**（GRI Authorized + StageA–D Frozen） | **是（5 处）** |

> 因此建议下一轮**只做"新增文件"部分**（不动任何已登记文件 ⇒ **重钉 0 处**），把"暴露到 Bootstrap/生成器"留到需要时的独立一轮。

### 明确不做（本轮与推荐方案均不包含）
- 不新增 `WorldBiomeProvider`（用户明令禁止；全库 0 处，未新增）。
- 不修改 `SeaLevel`/`WorldHeight`/`Elevation`/海陆边界/世界分布规则。
- 不更新任何冻结哈希表。
- 不把"静态引用存在"表述为"正式生成已接入"。

---

## 7. 后续实施应采用的验证顺序与验收标准

### 7.1 验证顺序（逐级 gate，前一级不过不得进入下一级）

| 序 | 验证 | 通过标准 |
|---|---|---|
| V0 | 现场基线 | HEAD 仍为 `37246478…`、staged 空、`git status` 条目数与启动时相同；`sealevel_round7_progress.md` 仍 1032 行且 SHA 未变 |
| V1 | 编译 | Unity Batchmode 编译 `error CS = 0`；新增文件命名空间不与 `World.V2`/`VoxelWorld.Generation` 冲突 |
| V2 | 新断言组（纯静态/纯函数） | ① `facts.Elevation == sampler.SurfaceHeightAt(x,z)` 逐列一致（多 Seed × 多列）；② `WorldParameterQuery.Evaluate` 的 `Elevation` 参数 == `HeightFunction.GetTerrainHeight`；③ `Ocean == (Elevation < settings.SeaLevel)`；④ 越界坐标 `TryEvaluate` 返回 `false` + `default` |
| V3 | **零行为变更证明** | 接线前后同一组 Chunk 的逐体素 SHA 完全相同（含 `EnableGeologyCaves/Deposits` 各组合） |
| V4 | 既有验证器全绿 | `WorldV2ParameterPipelineValidation`（45 PASS / 0 FAIL）、`GeologyRuntimeIntegrationValidation`（47 / 0，含 GE7/GE9 冻结表）、`GeologyStageA–D`、`ChunkStreamingValidation`、`VoxelWorldRuntimeCoreValidation`、以及 4 个 legacy core（61/41/37/127）**全部 exit 0 / failures 0** |
| V5 | 冻结表复算 | 7 张表合计 **349 条匹配 / 0 不符 / 0 缺失**；若本轮未改已登记文件 ⇒ 应为 0 重钉 |
| V6 | 现场收尾 | HEAD 未变、staged 空、历史证据零覆盖、仅新增本轮文件 |

### 7.2 验收标准（方案 1）
1. 生产代码首次**可读取** `WorldSkeletonFacts`（存在从 `Assets/Scripts`（非 `World/V2`）出发的合法引用路径），且该路径**不修改**任何生成写入逻辑。
2. V3 的逐体素 SHA 对照**完全一致**（0 差异列）。
3. V4 全绿；V5 = 349/0/0 且重钉数 = 0。
4. 报告中必须明确：**"V2 已可在生产链中读取"≠"V2 已影响世界生成"**，且**≠**"V2 已接入正式运行世界"（后者需 `A-2`/`AD-1` + 场景接线）。

### 7.3 若将来推进方案 2/3 的前置条件
- 方案 2：owner 裁决 `WorldHeight == SeaLevel == 64` 语义 ⇒ 决定开启 `Ocean` 驱动时的合法 SeaLevel 配置；随后按 §5 的重钉清单逐项授权。
- 方案 3：owner 裁决 `A-2`/`AD-1`（并存 / 迁移 / 替换 / 双轨）；随后才能改场景与玩家交互路径。

---

## 8. 本轮未修改任何既有代码的证据

### 8.1 未执行任何写操作（自述 + 机械证据）
- 本轮执行的命令**仅**：`git rev-parse/log/status/diff`（只读）、`Get-ChildItem`/`Get-Content`/`ReadAllLines`/`Get-FileHash`（只读）、以及**唯一一次写**＝创建本报告文件。
- 未调用任何编辑器/构建/Unity 进程（本轮**没有**跑 Unity Batchmode）。
- **一处自我更正（必须登记）**：本报告初稿在方案 1 的「验证器影响」行曾写 `4F6E...` 充当 `GeologyWorldGenerator.cs` 的登记哈希，该值是**未核实的占位串**（写稿臆测，非实测）；经机械实测更正为 `73EB59CFB939DC96`，并核实其登记表为 **`FrozenFiles`**（非初稿所写的 `CriticalFiles`）。方案 2 的重钉处数亦由初稿的下界「≥ 8 处」更正为实测 **21 处**。
- **一次失败的辅助尝试**：曾试图用 PowerShell 复刻 `HeightFunction` 哈希噪声以估算海拔分布，因 `uint` 掩码实现错误且耗时过长而**中止并放弃**（未写入任何文件、未改动任何项目内容）；海拔边界改为由源码解析推导（`TerrainHeight ± TerrainAmplitude` ⇒ `[18, 30]`）+ 引用 R40 的实跑断言 B3。此项如实登记为方法缺陷。

### 8.2 内容哈希复核（收尾实测，与 §1 逐项比对）

| 文件 | 轮前 SHA256（前 16） | 轮后 SHA256（前 16） | 一致 |
|---|---|---|---|
| `_phase2b_validation/sealevel_round7_progress.md` | `D638D271767BFFF9` | `D638D271767BFFF9` | ✅ |
| `round40_worldskeleton_sealevel_unification_report.md` | `0407A4D7EF109C95` | `0407A4D7EF109C95` | ✅ |
| `round39_worldskeleton_sealevel_contract_audit.md` | `1E5A5F029F3F91C2` | `1E5A5F029F3F91C2` | ✅ |
| `Assets/Scripts/World/V2/WorldSkeletonFacts.cs` | `98B59916CA9CDBCC` | `98B59916CA9CDBCC` | ✅ |

（完整逐文件哈希见 §8.3 与收尾核验输出；不以文件修改时间作为唯一证据。）

### 8.3 Git 视角
- `git status --porcelain` 条目数 **27**（20 `M` + 4 `??` + 3 目录），与 R40 收尾**完全相同**；本轮**未新增/删除**任何 tracked 或 untracked 源文件（仅 `_phase2b_validation/` 目录内新增本报告，该目录整体已为 `??`）。
- `git diff --stat` = `20 files changed, 390 insertions(+), 142 deletions(-)`，与轮前完全一致 ⇒ 20 个 tracked 文件的内容**未被本轮改动**。
- HEAD 未变、staged 空；未执行 commit/push/reset/clean/stash/checkout/add。

---

## 9. 最终结论（严格区分三种状态）

### 9.1 当前**已经接入**并能真实生效的生产能力
1. **VoxelWorld 离散体素栈**（代码级生产路径）：`VoxelWorldStreamingBootstrap → WorldGenerationSettings → GeologySettings/GeologySampler → GeologyWorldGenerator → VoxelRuntimeCore → ChunkStreamingDriver → Mesh/View`，唯一抽象边界为 `IWorldGenerator`。
2. **地质判定链**：`GeologySampler.Resolve` 的 P0 `WorldBounds` → P1 `Cave` → P2 `AboveSurface` → P3 `Surface/Depth/Deep ⇒ Grass/Dirt/Stone`；`SurfaceHeight` 来自 `HeightFunction.GetTerrainHeight`。
3. **群系环境接口**：`IBiomeProvider` / `GeologyBiomeProvider`（噪声温度/湿度 + 阈值群系），已接线但**默认关闭**（`EnableGeologyBiomeModifiers = false`），且只影响地表材料。
4. **实际运行世界**：Legacy `DensityTerrain`（`SampleScene.unity`，`m_Enabled: 1`），与本栈**无关**。

### 9.2 **尚未接入**的 World V2 能力
1. `WorldStatePipeline` / `WorldParameterQuery.Evaluate` / `WorldParameterEvaluator` / `WorldParameterState`：生产调用者 **0**（唯一外部使用者是 `WorldV2ParameterPipelineValidation`）。
2. `WorldSkeletonFacts.TryEvaluate`（`Elevation`/`Ocean`/`Continent`）：生产调用者 **0**。
3. Region 全族（`RegionPipeline`/`RegionDetermination`/`RegionModifierDetermination`/`RegionContext`/`RegionIdentity`/`RegionSourceCandidate`/`RegionModifier`）：仅 V2 内部互相调用，**无外部消费者**，且修正量为占位 `[0, 0.25)`。
4. `WorldGenerationSettings.SeaLevel`：**不参与任何地形/体素生成**（仅配置校验 + V2 的 `Ocean` 派生）。
5. 8 个环境参数（`Temperature/Moisture/Elevation/Fertility/MagicDensity/HolyInfluence/CorruptionInfluence/Ocean`）在产线中**无任何消费者**。

### 9.3 建议下一轮实施的最小改动
- **只做方案 1 的"新增文件"部分**：新增 `Assets/Scripts/World/V2/WorldSkeletonProvider.cs`（+ meta）与一个 `RunBatch` 验证器，把 V2 事实接入生产可读路径并加上 `Elevation` 同源守卫断言。
- **重钉 0 处**、**零世界行为变化**、**不碰 Legacy**、**不需要任何未裁决决策**。
- **不**在本轮或下一轮擅自开启 `Ocean` 驱动行为（需 `SeaLevel` 语义裁决），**不**做场景接线（需 `A-2`/`AD-1` 裁决）。

### 9.4 明确指出仍缺失的证据（不猜测）
| # | 缺失证据 | 影响 |
|---|---|---|
| E1 | `A-2`/`AD-1` 的 owner 裁决（旧/新世界关系） | 决定方案 3 能否实施；当前**无法**判断"正式世界"最终归属 |
| E2 | `WorldHeight == SeaLevel == 64` 语义合法性裁决 | 决定方案 2 的默认行为是否可接受 |
| E3 | V2 `Temperature`/`Moisture`（配置基线 0.5 + Region 占位修正）与 `GeologyBiomeProvider`（噪声 + 海拔递减）**谁是环境的权威来源** | 两者语义/量纲不同（float 0–1 vs 0.1℃/0.1% 整数定点），直接替换会改变群系分布；**未裁决前不得替换** |
| E4 | `RegionModifier` 真实数据来源（当前占位 `[0, 0.25)`） | 决定 V2 参数最终是否会偏离 Base 段 |
| E5 | 玩家可见世界的验收方式（无自动化观感验证） | 方案 3 无法用现有验证器闭环 |