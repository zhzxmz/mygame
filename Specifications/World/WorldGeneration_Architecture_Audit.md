# World Generation 全局架构审查报告（只读）

> **报告性质**：**只读审查报告**。本报告不修改任何既有文件、不新增设计规范、不实现代码。
> **审查时间**：2026-10-13（本会话）
> **审查方式**：逐文件阅读规范正文与实现源码；对源码做符号级检索；对场景做 GUID 级核验；计算关键文件 SHA16 作为基线。
> **本轮未执行**：未运行 Unity、未执行任何构建/测试/自动修复命令；未修改任何 `.cs` / `.asmdef` / `.meta` / 配置 / 测试代码。
> **证据等级标注**（全文使用）：
>
> | 标记 | 含义 |
> |---|---|
> | **[已验证-原文]** | 已在规范正文中逐行核对到的条款 |
> | **[已验证-代码]** | 已在源码中核对到的类/方法/常量/字段/行号 |
> | **[已验证-执行]** | 本会话早前实际运行 Unity 批处理得到的通过结果（**本轮未重跑**） |
> | **[推论]** | 由上述事实推导，未经代码或测试直接验证 |
> | **[待确认]** | 无法在只读条件下验证，必须由后续决策或执行确认 |
>
> **重要限制**：本报告**不**声称任何"未实现的设计"已经通过验证。凡未在代码中找到对应实现的层，一律标记为"未实现"。

---

## 1. 实际审查的文件与代码清单

### 1.1 规范（`Specifications/`，26 份，含行数与 SHA16）

| 文件 | 行数 | SHA16 | 与本审查的关系 |
|---|---|---|---|
| `World/WorldState_Design.md` | 853 | `D54CB1CC14859312` | 环境参数权威（8 参数、因果纪律、群系规则表、退化兼容） |
| `World/Region_Design.md` | 823 | `77FA71B2620E7396` | 区域权威（来源、修正、标签、资格、边界、叠加） |
| `World/WorldGeneration_Pipeline_Design.md` | 782 | `A11C4E9373B0B9C8` | 层间顺序与数据流权威（Phase 0–7、域分离、权限矩阵） |
| `World/WorldHistory_Design.md` | 627 | `65296B3C9CCE6451` | 历史权威（Event=Cause、纪元序位、文明偏好、遗迹候选） |
| `World/Ecology_Design.md` | 587 | `662C305CB8A2904B` | 生态权威（候选语义、输入封闭、资源边界） |
| `World/Resource_Design.md` | 692 | `F857DFF93D6F24AA` | 资源权威（判定等级、两层存在性、魔法资源三条件） |
| `World/WorldQuery_Design.md` | 573 | `6D801C60F3C6D42F` | 查询语义权威（入口、链、权限、局部求值、解释） |
| `World/WorldGeneration_Geology.md` | 603 | `1A7DD7E7AE1EBAC9` | 地质规则权威（P0–P3、地层、洞穴、`DepositZone`、V1 范围） |
| `World/WorldGeneration_Geology_Implementation.md` | 648 | `9BF0E11A4E5CA87D` | 地质访问层（世界坐标采样、GIV1–GIV9） |
| `World/WorldGeneration_Geology_Runtime_Implementation.md` | 428 | `ABF96EEE9E9850E6` | 地质生成器落地（§6.1 群系只影响地表材料、§7 GIV9） |
| `World/WorldGeneration_Core.md` | 736 | `684753D5E6B31565` | 生成契约（Seed+ChunkCoordinate→VoxelChunk、确定性、stage 划分） |
| `World/VoxelWorld_Core.md` | 226 | `F718B68E95100C57` | 世界真值与体素状态集合 |
| `World/VoxelWorld_Architecture.md` | 262 | `779DF1EFC4082579` | 概念分层 |
| `World/VoxelWorld_Mesh.md` | 258 | `81CEDBFA6E9756CE` | 派生表示（只读） |
| `World/VoxelWorld_Mining.md` | 240 | `8A2B020DDB97422B` | 运行时修改语义、未生成区域策略 |
| `World/VoxelWorld_Runtime.md` | 452 | `EDD02AC50FC03E5E` | 调度（加载集合判定、WorldHeight 之上不加载） |
| `World/VoxelWorld_Runtime_Core.md` | 651 | `10F1EC7F10DAD8F3` | 生命周期/安装/脏/重建契约 |
| `World/VoxelWorld_Runtime_Core_Implementation.md` | 741 | `D816F09C60A810C4` | 落地阶段与不变量 RCI1–RCI15 |
| `World/VoxelWorld_Runtime_Interface.md` | 577 | `0AE21EB684412685` | `IWorldGenerator` 抽象与依赖倒置 |
| `World/VoxelWorld_View.md` | 436 | `D9BD5422026E21B6` | 表现层边界 |
| `ArchitectureDecisionWorldSpaceEntity.txt`（ADR-001） | 65 | `218D1CAAA738C59D` | World/Space/Entity 概念边界（Entity 不属 Chunk 所有权） |
| `Chunk.md` | 935 | `E401E4F52AFF6C27` | **旧世界** Chunk 概念（与 `ChunkSpace`/`DensityTerrain` 对应） |
| `CoordinateMigration.md` | 366 | `45AB2D33FAE1C558` | 坐标迁移方案（旧→新迁移历史） |
| `Mining.md` | 720 | `E600CFBB4A1F15AE` | 旧世界挖掘规范 |
| `VoxelAwareMesh.md` | 2,089 | `675053608E47161E` | 旧世界体素感知网格规范 |

### 1.2 新栈代码（`Assets/Scripts/VoxelWorld`，39 个 `.cs`，关键文件 SHA16）

| 分组 | 文件（行数） |
|---|---|
| 核心 | `VoxelType.cs`(20) `Voxel.cs`(78) `VoxelCoordinate.cs`(80) `VoxelChunk.cs`(173) `VoxelWorld.cs`(258) `VoxelWorldDebug.cs`(260) |
| Generation | `IWorldGenerator.cs`(28) `WorldGenerationSettings.cs`(120) `HeightFunction.cs`(138) `BasicTerrainGenerator.cs`(107) |
| Geology（9） | `GeologySettings.cs`(171) `GeologyNoise.cs`(223) `GeologySample.cs`(180) `GeologySampler.cs`(350) `IDepositProvider.cs`(98) `IBiomeProvider.cs`(75) `GeologyDepositProvider.cs`(123) `GeologyBiomeProvider.cs`(127) `GeologyWorldGenerator.cs`(276) |
| Runtime | `VoxelRuntimeCore.cs`(980) `ChunkRecord.cs`(83) `ChunkRecordSnapshot.cs`(80) `RuntimeChangeSet.cs`(93) `RuntimeCoreDiagnostics.cs`(74) `RuntimeCoreSettings.cs`(94) `RuntimeCoreTickReport.cs`(76) `ChunkLifecycleState.cs`(37) + `Abstractions/`(2) + `Adapters/`(1) |
| Streaming | `ChunkStreamingDriver.cs`(312) `ChunkStreamingSettings.cs`(112) `ChunkStreamingReport.cs`(51) `IChunkRequestSink.cs`(24) `RuntimeCoreRequestSink.cs`(44) |
| Mesh / View | `VoxelMeshData.cs`(237) `VoxelMeshGenerator.cs`(242) `ChunkViewPresenter.cs`(135) `UnityMeshBuilder.cs`(291) `VoxelChunkView.cs`(443) |
| 组装 | `Assets/Scripts/Game/VoxelWorldStreamingBootstrap.cs` |

**关键文件 SHA16（本次审查基线）**

| 文件 | SHA16 |
|---|---|
| `HeightFunction.cs` | `715341D5FC652408` |
| `BasicTerrainGenerator.cs` | `7FFD8F4B1226EA4E` |
| `IWorldGenerator.cs` | `B39614CA9BF37921` |
| `GeologySampler.cs` | `62836145F8F3E212` |
| `GeologyWorldGenerator.cs` | `73EB59CFB939DC96` |
| `IBiomeProvider.cs` | `6EF3344B319436B7` |
| `IDepositProvider.cs` | `6F25E54FE64F7935` |
| `GeologyDepositProvider.cs` | `7B1B8CF998402D3A` |
| `VoxelChunk.cs` | `4DBDFBAC5BCE500C` |
| `VoxelWorld.cs` | `D9E749A466635918` |
| `VoxelType.cs` | `02C1F54580AEE541` |
| `VoxelRuntimeCore.cs` | `9119E8D3292A4C71` |
| `ChunkStreamingDriver.cs` | `26DF1B6B22E2DFF3` |
| `VoxelWorldStreamingBootstrap.cs` | `4738634952568520` |

### 1.3 旧栈代码（`Assets/Scripts/Terrain`，30 个 `.cs`，与本审查相关者）

| 文件 | 行数 | SHA16 | 作用 |
|---|---|---|---|
| `DensityTerrain.cs` | 1,855 | `0EB935DBA79CBD40` | **场景中实际运行的世界** |
| `DensityFieldGenerator.cs` | 556 | `702BEFA889002006` | 旧密度/材料生成规则 |
| `ChunkSpace.cs` | 160 | `219FCBB569A413FB` | 旧坐标转换（Terrain Local ↔ Voxel ↔ Chunk） |
| `TerrainChunk.cs` | 303 | — | 旧 Chunk 数据 |
| `TerrainEffectiveTerrainQueryV2.cs` | 76 | `6D66D4CF9474BCA7` | **旧地形查询 API** |
| `TerrainMeshGenerator.cs` | 413 | — | 旧网格生成 |
| `VoxelAwareMeshRuntimeMutation.cs` | 1,890 | — | 旧运行时网格修改 |
| `VoxelAwareMeshSpatialFace.cs` | 1,829 | — | 旧空间面（内含 `class FaceRegion`，第 112 行） |
| `VoxelAwareMeshPrecut.cs` | 1,090 | — | 旧预切 |
| `Player/PlayerMiningController.cs` | 1,003 | `906C3DA7477E2E10` | **玩家挖掘入口（面向旧世界）** |
| `Player/PlayerResources.cs` | 46 | — | 玩家计数资源（`soulFragments`/`growthResource`） |

### 1.4 场景与 Editor 验证入口

* `Assets/Scenes/SampleScene.unity`：**GUID 级核验**结果——
  `DensityTerrain`（guid `3af3bcfc875f4cbfae75f1f95dee772a`）出现 **1** 次；
  `PlayerMiningController`（`b5728d61172b2cf46949a1ebb34500c7`）**1** 次；
  `PlayerInputController`（`d480d937e3edfa144ab6725b3adec4a1`）**1** 次；
  `CameraController`（`1e1dff732c029b54384a00a74722db58`）**1** 次；
  **`VoxelWorldStreamingBootstrap`（`2c531141a81459d499f3f2959fe28988`）出现 0 次**。[已验证-代码]
* `Assets/Editor/` 下 `*Validation*.cs` 共 **45** 个，分属两套栈（新栈：VoxelWorld 系列 + Geology Stage A–E + Streaming；旧栈：TerrainModificationV2 / VoxelAwareMesh / MiningSurface / SpatialFace / DensityTerrain / PlayerLook / TerrainVoxel 系列）。[已验证-代码]
* 本会话早前实际执行（**本轮未重跑**）：`GeologyRuntimeIntegrationValidation`（主工程 + 验证工程各 1 次，45 项）、`GeologyStageA`（105）、`GeologyStageB`（122）、`GeologyStageC`（49）、`GeologyStageD`（44）、`VoxelWorldCore`（61）、`WorldGenerationCore`（41）、`VoxelMeshCore`（37）、`VoxelWorldView`（127）、`VoxelWorldRuntimeCore`（196）、`ChunkStreaming`（107）——**12/12 PASS，0 失败**。[已验证-执行]

### 1.5 本次未审查 / 无法验证

* 未运行 Unity Play Mode → **旧世界的实际运行行为未复核**（[待确认]）。
* 未构造新世界与新层的运行时对照 → 任何"实现后会更一致"的判断均为 [推论]。
* `Chunk.md`/`Mining.md`/`VoxelAwareMesh.md`/`CoordinateMigration.md` 仅做**结构与关键定义核对**（未逐行精读全部 4,110 行）→ 涉及旧栈迁移历史的细节结论标 [待确认]。

---

## 2. 总体架构与真实依赖关系

### 2.1 规范层面的目标架构

```
Seed
 └ Phase 1 World Skeleton（WorldState_Design §7.3/§7.4 WC1–WC6；Pipeline §2.1/§2.3）
    └ Phase 2 History（WorldHistory_Design：Event=Cause → HistoryTags + Region Source 候选）
       └ Phase 3 Region（Region_Design：来源采纳 + RegionModifier + 标签 + 资格）
          └ Phase 4 WorldState（WorldState_Design：FinalState = Base + Modifier，8 参数）
             └ Phase 5 Biome（WorldState_Design §5.3 唯一规则表）
                └ Phase 6 Geology（Geology 三份规范：P0→P1→P2→P3）
                   ├ Phase 7 Voxel Output（VoxelWorld_Core / WorldGeneration_Core）
                   ├ 旁路 7b Resource（Resource_Design：判定 / 载体资格）
                   └ 旁路 7a Ecology（Ecology_Design：生态候选）
                        └ World Query（WorldQuery_Design：点查询 + 解释链，只读）
```

### 2.2 代码层面的真实架构（**两套世界并存**）[已验证-代码]

```
【旧栈 · 场景实际运行】
SampleScene.unity
  └ DensityTerrain (1855 行)
       ├ ChunkSpace（Terrain Local + voxelSize + 可配置 chunkVoxelSize）
       ├ DensityFieldGenerator（HeightField / ThreeDimensional / MaterialV2 / 洞穴相关参数）
       ├ TerrainChunk / TerrainChunkView / TerrainMeshGenerator
       ├ TerrainModification / VoxelAwareMesh*（运行时网格修改 4,809 行）
       └ TerrainEffectiveTerrainQuery（IsExcavated(Vector3 terrainLocalPosition)）
  └ PlayerMiningController (1003 行)
       └ ResolveTerrain() → DensityTerrain → VoxelField.Set(Air) → 重建 Mesh/Collider

【新栈 · 仅批处理验证，未接入场景】
Assets/Editor/*Validation*.cs（12 项，本会话 12/12 PASS）
  └ VoxelWorldStreamingBootstrap（GUID 未出现在任何场景）
       └ GeologyWorldGenerator : IWorldGenerator
            ├ GeologySampler（世界坐标点采样：Sample / SurfaceHeightAt / CaveDensityAt /
            │                 CaveStateAt / DensityModifierAt / ExplainAt）
            ├ GeologyDepositProvider : IDepositProvider（DepositSample{Exists,Zone,Richness,Grade}）
            └ GeologyBiomeProvider : IBiomeProvider（BiomeSample{Temperature,Moisture}）
       └ VoxelRuntimeCore → ChunkStreamingDriver → VoxelMeshGeneratorMesher → ChunkViewPresenter
```

### 2.3 关键真实调用链（证据）

| 链路 | 证据 |
|---|---|
| 场景世界 = 旧栈 | `SampleScene.unity` 含 `DensityTerrain` guid（1 次）；不含 `VoxelWorldStreamingBootstrap` guid（0 次）|
| 玩家挖掘 = 旧栈 | `PlayerMiningController.cs:31` 字段 `DensityTerrain terrain`；`:996-1000` `ResolveTerrain() → FindObjectOfType<DensityTerrain>()`；文件头 `:10-12` 明示 `TerrainVoxelSelector → SelectedVoxel → DensityTerrain.VoxelField.Set(Air) → 重建 Mesh/Collider`；对 `VoxelWorld`/`SetVoxel`/`IWorldGenerator`/`DepositZone` 命中 **0** |
| 旧坐标契约 = Transform + 可配置尺寸 | `ChunkSpace.cs:13/25/38/54`（`LocalToVoxel(Vector3, float voxelSize)`、`VoxelToChunk(voxel, Vector3Int chunkVoxelSize)`、`PositiveMod`）；`DensityTerrain.cs:16-37`（`terrainSize`/`chunkSize`/`chunkCount`/`chunkVoxelSize`/`resolution`）、`:177` `EffectiveChunkVoxelSize` |
| 新坐标契约 = 固定 16³ + 整数世界坐标 | `VoxelChunk.cs:18` `ChunkSize = 16`、`:21` `VoxelCount = 4096`；`VoxelWorld.cs:47` `FloorDiv`、`:60` `WorldToChunkCoordinate`、`:71` `WorldToLocalCoordinate` |
| 未生成区域语义 | `VoxelWorld.cs:124`（`TryGetVoxel`：未生成 Chunk 返回 `false`（voxel=Air），**不会**创建 Chunk）、`:142/141`（`GetVoxel`：未生成区域视为 Air）|
| 新栈确定性来源 | `HeightFunction.cs:114` `HashToUnit(int x, int z, int seed)`、`:124` `Hash(...)`（自实现整数哈希）；`GeologyNoise.cs:45/87`（`Value3D/Value2D(..., int seed[, int cellVoxels])`）、`:189` `LatticeHash(cellX, cellY, cellZ, seed)` |
| 新栈点查询先例 | `GeologySampler.cs:93/99` `Sample(worldX, worldY, worldZ[, seed])`、`:129/134` `SurfaceHeightAt`、`:140/146/152` `CaveDensityAt`/`CaveStateAt`/`DensityModifierAt`、`:113/119` `ExplainAt` |
| 地质判定链落地 | `GeologySample.cs:124` `GeologyRule{Unresolved, WorldBounds, Cave, AboveSurface, Surface, Depth, Deep}`；`GeologyWorldGenerator.cs:128` `Generate`、`:156` `GenerateVoxelData`、`:197` `ResolveMaterial`（群系只参与材料）、`:225` `Validate` |
| 资源/群系均为**占位** | `GeologySample.cs:153` `GeologyDepositZone{None, ZoneA, ZoneB}`；`:169` `GeologyBiome{None, BiomeA, BiomeB}` |

### 2.4 规范 ↔ 实现 对照表

| 层 | 权威规范 | 实现状态 | 证据 |
|---|---|---|---|
| Seed | `WorldGeneration_Core.md` | **已实现** | `WorldGenerationSettings.cs:23` `Seed` |
| Phase 1 World Skeleton | `WorldState_Design.md` §7.3/§7.4 | **未实现** | `Continent`/`SeaLevel`/`Skeleton` 在 `Assets/Scripts` 命中 **0** |
| Phase 2 History | `WorldHistory_Design.md` | **未实现** | `Epoch`/`Civilization`/`HistoryTag` 命中 **0** |
| Phase 3 Region | `Region_Design.md` | **未实现** | `RegionModifier` 命中 **0**（仅旧栈 `VoxelAwareMeshSpatialFace.cs:112` 同名 `FaceRegion`，语义无关） |
| Phase 4 WorldState | `WorldState_Design.md` | **未实现** | `MagicDensity`/`HolyInfluence`/`CorruptionInfluence`/`Fertility` 命中 **0**；`Temperature`/`Moisture` 仅出现在 `BiomeSample` 占位 |
| Phase 5 Biome | `WorldState_Design.md` §5.3 | **占位** | `GeologyBiome{None, BiomeA, BiomeB}` + `GeologyBiomeProvider` |
| Phase 6 Geology | Geology 三份规范 | **已实现（Stage A–E）** | 12 项验证 PASS [已验证-执行] |
| Phase 7 Voxel Output | `VoxelWorld_Core.md` | **已实现** | `VoxelChunk`/`VoxelWorld` |
| Resource | `Resource_Design.md` | **未实现** | `ManaOre`/`HolyCrystal`/`CorruptedMaterial`/`MagicCrystal` 命中 **0**；仅有地质层 `DepositSample.Exists` |
| Ecology | `Ecology_Design.md` | **未实现** | `SpawnCandidate`/`EcologyRules` 命中 **0** |
| World Query | `WorldQuery_Design.md` | **未实现** | `WorldQuery` 命中 **0**；但 `GeologySampler` 已有点查询先例 |
| Runtime / Streaming / Mesh / View | 对应规范 | **已实现** | `VoxelRuntimeCore`(980) / `ChunkStreamingDriver`(312) / `VoxelMeshGenerator` / `ChunkViewPresenter` |

---

## 3. 规范冲突与实现偏差清单（总表）

| ID | 类型 | 问题 | 严重度 | 证据位置 |
|---|---|---|---|---|
| **P0-1** | 契约缺失 | 新层（WorldState/Region/Biome/History/Skeleton）的具体参数、量纲、阈值、域标签编码、纪元参数均无定义；且实现层占位与规范量纲不一致 | **P0** | `WorldState_Design.md:108` vs `IBiomeProvider.cs:22-31` |
| **P0-2** | 规范冲突 | 资源存在性权威表述冲突：Ecology 说"属于 Geology"，Resource 建"两层模型"并承担"存在性裁决" | **P0** | `Ecology_Design.md:286` vs `Resource_Design.md:242/252/265` |
| **P0-3** | 链路缺环 | "History → Region Source 候选 → Region 采纳"的**采纳规则**未定义（去重/合并/上限/序位无关性） | **P0** | `WorldHistory_Design.md:1.3/PI1` vs `Region_Design.md` §3（只有来源类别） |
| **P0-4** | 语义未定义 | World Query 的"地形真值"未区分**规则求值**与**当前真值（含玩家编辑）**；未生成区域在 `VoxelWorld.GetVoxel` 下返回 Air | **P0** | `WorldQuery_Design.md:107/46` vs `VoxelWorld.cs:124/141`、`VoxelWorld_Mining.md` 未生成区域策略 |
| **P0-5** | 规范冲突 | Ecology 输入集合自相矛盾：`EA2`（封闭：WorldState+Biome+Region Tags）vs `EN4`（读洞穴/地形）vs `§2.1`（含 `Elevation`）vs `WorldQuery_Design.md:125`（允许读地质地形） | **P0** | `Ecology_Design.md:93` vs `:328` vs `WorldQuery_Design.md:125` |
| **P1-1** | 未实现 | World Skeleton（大陆/海陆/气候带/水系倾向）无实现，且"骨架规则"无独立文档指向 | **P1** | `Continent`/`SeaLevel` 命中 0；`Pipeline §1.3` 权威列写"骨架规则" |
| **P1-2** | 规则缺失 | `Region.EcologyRules`（区域级资格声明）只有属性定义，**无产生规则**，但 Ecology 要读它 | **P1** | `Region_Design.md:328`；`Ecology_Design.md:106/366` |
| **P1-3** | 契约缺失 | 域分离 `(Seed, Domain, Position)` 无编码契约；现有噪声只有 `seed` 参数 | **P1** | `Pipeline §3.3 PE1–PE7`；`HeightFunction.cs:124`；`GeologyNoise.cs:189` |
| **P1-4** | 参数缺失 | 纪元划分与历史规模参数未定义（"100 年前/1000 年前/远古"无确定性映射） | **P1** | `WorldHistory_Design.md:HE1/HE4/HN9/HN10` |
| **P1-5** | 边界缺失 | 资源 ↔ Mining ↔ Entity 无落点：Mining 完全不认识资源/新世界；资源无对象、无采集路径 | **P1** | `PlayerMiningController.cs`（`VoxelWorld`/`DepositZone` 命中 0）；`Resource_Design.md:RQ8/§4.4` |
| **P1-6** | 集成缺失 | 新栈**未接入任何场景**；当前运行时世界是旧 `DensityTerrain` 栈 | **P1** | `SampleScene.unity` GUID 核验（新 Bootstrap 0 次） |
| **P1-7** | 契约不一致 | 两套坐标契约并存：旧 = Terrain Local + `voxelSize` + 可配置 `chunkVoxelSize`；新 = 固定 16³ + 整数世界坐标 | **P1** | `ChunkSpace.cs:13/25/38/54` + `DensityTerrain.cs:16-37` vs `VoxelChunk.cs:18-21` + `VoxelWorld.cs:47-71` |
| **P1-8** | 实现偏差 | 已实现的群系输入量纲与规范不一致：整数域 Temperature ∈ [-500,600]、Moisture ∈ [0,1000]；规范要求 [0,1] 归一化（`Elevation` 除外）；群系集合差异（A/B 占位 vs 6 类） | **P1** | `IBiomeProvider.cs:22-31` vs `WorldState_Design.md:108`、`WorldState_Design.md` §5.3 |
| **P1-9** | 风险 | World Query 若照现有 `GeologySampler` 的阈值另写一套条件，将违反"同一规则"（第二套规则） | **P1** | `WorldQuery_Design.md:WQ1/WQ2/WA20`；`GeologySampler.cs:165-288` |
| **P2-1** | 命名碰撞 | 旧栈 `FaceRegion`、`PlayerResources`、`TerrainEffectiveTerrainQuery` 与新层术语（Region/Resource/World Query）同名不同义 | **P2** | `VoxelAwareMeshSpatialFace.cs:112`；`PlayerResources.cs:7`；`TerrainEffectiveTerrainQueryV2.cs:22` |
| **P2-2** | 指针陈旧 | `Pipeline §1.3` 把 History 权威写为"未来 History 规范"；`WorldQuery §2.2` 第 1 步权威写"骨架规则"（非文档名） | **P2** | `Pipeline_Design.md:96`；`WorldQuery_Design.md` §2.2 表 |
| **P2-3** | 符号名不一致 | 规范引用 `SurfaceHeight`（`HeightFunction`）为规则名；实现为 `HeightFunction.GetTerrainHeight` 与 `GeologySampler.SurfaceHeightAt` | **P2** | `WorldState_Design.md:94/111`；`HeightFunction.cs:27`；`GeologySampler.cs:129` |
| **P2-4** | 表述差异 | Ecology/Resource 求值顺序在两份规范中表述相反（已由可交换性条款对齐，但需在实现中显式断言） | **P2** | `Resource_Design.md` §8.2③ vs `WorldQuery_Design.md:135`（WN4） |
| **P2-5** | 覆盖不足 | `Desert` 等群系未出现在 `WorldState_Design.md` §5.3 的有序规则表中（Pipeline 已登记需回到该表定义） | **P2** | `Pipeline_Design.md` PM3；`WorldState_Design.md` §5.3/§5.4 |
| **P2-6** | 延后项 | 大陆骨架、批量查询、缓存策略、动态生态、资源对象化等扩展 | **P2** | 各规范"未来扩展"附录 |

---

## 4. 问题详情（证据 / 影响 / 严重度 / 建议）

### P0-1 缺少实现级契约（参数、量纲、阈值、域标签、纪元）

**证据**

* 规范侧：`WorldState_Design.md:108`——"**全部归一化到 `[0,1]`**（`Elevation` 例外）"；§1.3 表给出 8 参数的定义域；§3.4 给出静态场公式；但**未给出** `Base` 的具体生成规则、各修正项形状、群系规则表的具体阈值（规范明确把落地形态交给实现规范）。
* 实现侧：`IBiomeProvider.cs:22-31`——`MinTemperature = -500`、`MaxTemperature = 600`、`MinMoisture = 0`、`MaxMoisture = 1000`（**整数域**，与 [0,1] 不一致）；`GeologySample.cs:169` 群系只有 `None/BiomeA/BiomeB`。
* 缺失符号检索（`Assets/Scripts`）：`MagicDensity`=0、`HolyInfluence`=0、`CorruptionInfluence`=0、`Fertility`=0、`Continent`=0、`SeaLevel`=0、`Epoch`=0、`RegionModifier`=0、`SpawnCandidate`=0、`WorldQuery`=0。

**影响**：M1（WorldState）、M2（Region）、M3（Biome）、M4（History）、M5（Resource/Ecology）**均无法开始编码**——没有权威的数值契约，任何实现都是猜测，且必然与后续验证口径不一致。

**严重度**：**P0**。

**建议**：在**单一实现规范**内补齐（不新建多份文档）：① 8 参数的生成规则与量纲（并明确 `Elevation` 与既有 `HeightFunction.GetTerrainHeight` / `GeologySampler.SurfaceHeightAt` 的复用口径）；② 群系规则表的具体阈值；③ 修正项形状与钳制；④ 域标签编码；⑤ 纪元参数。补齐后即可编码 M1。

---

### P0-2 资源存在性的权威表述冲突

**证据**

* `Ecology_Design.md:286`（ER2）："**资源存在性属于 Geology**：Ecology 不得定义、修改或扩展 `DepositZone`"。
* `Resource_Design.md:242`："Resource —— **资源判定**：…给出**资源存在性裁决**与**载体资格**"；`:252`："第二层 … **最终资源存在性裁决**"；`:265`（RU1）："`DepositZone` 的定义、产生与修改**只属于 Geology**"。
* 用户任务书亦要求"Resource 负责：资源存在性"。

**影响**：实现者面对同一问题（"这个坐标存在什么资源"）会得到两个归属结论，导致职责边界漂移（Ecology 侧或 Geology 侧可能被要求承担资源裁定）；也会影响 Mining 与未来 Entity 的消费口径。

**严重度**：**P0**（表述冲突，不是语义不可调和）。

**建议**：统一术语为两层——**"地质矿藏基础层（Geology / `DepositZone`）"** 与 **"资源判定层（Resource）"**；在 `Ecology_Design.md` 的 ER2 增补限定语（"属于 Geology 的是**地质矿藏分布**，不是**资源判定**"）。注意：二者均为**已冻结文件**，修改需显式授权并级联重钉验证哈希（本轮不修改）。

---

### P0-3 "Region Source 候选 → Region 采纳"缺环

**证据**

* `WorldHistory_Design.md` §1.3 第 4 项：输出 = **Region Source Candidates**；`PI1`：历史只产出事实与候选，**采纳与归属属于 Region 层**。
* `Region_Design.md` §3.1/§3.2：Region 必须由 `RegionSource` 驱动（`RS1`）；§3.2 只列出**来源类别**（自然/魔法/圣恶与其子类），并给出 `Position/Strength/Radius/Type` 属性；**未定义**候选的采纳算法（如何合并同类候选？多候选重叠如何处理？上限？排序？序位如何参与？）。
* 相关：`WorldQuery_Design.md` WN4 已要求"两者无依赖、可交换"，但采纳过程本身仍未定义。

**影响**：`History → Region` 是本架构的**核心因果链**；缺采纳规则时，同一输入可能产生不同 Region 集合（取决于实现遍历顺序），直接违反 `RO1` 交换律与 `HA1`/`RG6` 确定性验收。

**严重度**：**P0**。

**建议**：在实现规范中定义采纳算法的最小契约：① 采纳的判定式（候选 → 来源）；② 同类候选的合并规则（例如按显式上限 + 确定性排序键）；③ 序位仅用于**成立性判定**而非覆盖语义（`HE5`）；④ 采纳结果的"与遍历顺序无关"证明口径（可作为验收断言）。

---

### P0-4 World Query 的"地形真值"语义未定义

**证据**

* `WorldQuery_Design.md:46` 输出内容含"**地形真值**"；`:107` 第 6 步"Geology Evaluation（地形真值：Density / Material / 地层 / CaveDensity / 地质矿藏）"；但全文未区分**规则求值结果**与**当前世界真值（含玩家编辑）**。
* 实现事实：`VoxelWorld.cs:124`——"未生成的 Chunk 返回 false（voxel = Air），**不会**创建 Chunk"；`:142`——"未生成区域视为 Air"。
* 既有规范事实：`VoxelWorld_Mining.md` 规定未生成区域挖掘为 `NotGenerated/Unchanged` 且**不创建 Chunk**；世界真值 = 生成结果 + 运行时修改（`WorldGeneration_Core.md` §9 不覆盖玩家修改）。

**影响**：查询"某点是什么"会出现两种答案：`GeologySampler.Sample(...)`（规则求值，任何坐标都可回答）与 `VoxelWorld.GetVoxel(...)`（仅已生成区域有效，未生成返回 Air，且已生成区域可能已被玩家修改）。若 Query 层不显式区分，则"完整世界状态"的语义不确定，且会与 Mining/存档口径冲突。

**严重度**：**P0**。

**建议**：在查询规范中显式定义两种模式（名称可自定）：**Pristine（规则求值；与是否生成 Chunk 无关）** 与 **Current（真值 = 规则 ⊕ 玩家编辑）**，并规定：① `Current` 在未生成区域的行为（回退 Pristine 或返回"未生成"）；② 两种模式的判定链与证据格式；③ 两种模式都必须确定性、只读。

---

### P0-5 Ecology 输入集合自相矛盾

**证据**

* `Ecology_Design.md:93`（EA2）："Ecology 的输入**只有**：`FinalState`（只读）、`Biome`（只读）、Region Tags（只读）、显式生态参数"。
* 同文件 `:328`（EN4）："窝点候选**不得**改变地形（不得挖洞、不得放置方块）——地形庇护只能**读取**既有洞穴/地形"。
* 同文件 §2.1 表把 `Elevation` 列为参与判定参数（`Elevation` 属 WorldState 参数，与 EA2 相容），但洞穴（`CaveDensity`）属 **Geology**（Phase 6），不在 EA2 列表中。
* `WorldQuery_Design.md:125`（7a 行）：Ecology 输入含"**地质地形（只读，用于地形庇护）**"。

**影响**：实现者无法同时满足"输入封闭"与"读洞穴做庇护判定"；会导致两种实现分叉（一种关闭地形庇护、一种越权读 Geology），破坏 `Ecology_Design.md` 的验收 `WA13`/`EC-*`（越权读取 = 0）。

**严重度**：**P0**。

**建议**：在 Ecology 规范内把输入改为**两段式白名单**：① 必需输入（`FinalState` + `Biome` + Region Tags）；② 允许的**只读地形庇护条件**（`CaveDensity`/高程等，明确列出），并同步修正 `WorldQuery_Design.md` §3.1 与 §2.2 的 7a 行，使两份文档一致。

---

### P1-1 World Skeleton 未实现且权威指向缺文档

**证据**：`Continent`/`SeaLevel`/`Skeleton` 在 `Assets/Scripts` 命中 **0**；`Pipeline_Design.md:95` 把骨架输入/输出列出，但权威列写"`WorldState_Design.md` §7.3/§7.4（WC1–WC6）"，而 `Pipeline §1.3` 下方（`:96`）History 的权威写"**未来 History 规范**"（该文档现已存在）；`WorldQuery_Design.md` §2.2 第 1 步权威写"`Pipeline` §2.3 / **骨架规则**"（"骨架规则"不是文档名）。

**影响**：Phase 1 无实现、无独立规范落点；下游（History 的地点规则、WorldState 的 `Base`、Geology 的地形基础）都依赖它。

**严重度**：**P1**。

**建议**：明确骨架规则的**唯一权威落点**（建议保留在 `WorldState_Design.md` §7.4，避免新增文档），并修正两处指针（`Pipeline:96`、`WorldQuery §2.2`）。

---

### P1-2 `Region.EcologyRules` 无产生规则

**证据**：`Region_Design.md:328` 仅把 `EcologyRules` 列为属性（"资格声明 | ✅ 产出'允许/不允许'，不生成对象"）；`Ecology_Design.md:106/366` 要求 Ecology 读取"区域级资格声明"；但**没有任何规范定义该声明如何由来源/修正推导**。

**影响**：Region 层无法产出该属性，Ecology 的"区域级粗粒度约束"落空（或被迫由实现自由发明）。

**严重度**：**P1**。

**建议**：在实现规范中定义 `EcologyRules` 的推导规则（由来源类别 + 修正侧写 + 显式参数决定），并纳入 `RG*` 验收。

---

### P1-3 域分离缺编码契约

**证据**：`Pipeline_Design.md` §3.3 要求"每个输出由 `(Seed, Domain, Position)` 唯一确定"，`PE5` 要求"新增层只能新增域标签"；实现层：`HeightFunction.cs:124` `Hash(int x, int z, int seed)`、`GeologyNoise.cs:189` `LatticeHash(cellX, cellY, cellZ, seed)` —— **只有 seed，没有 domain 维度**。

**影响**：新层若各自"随便再抽一个随机通道"，可能出现域复用/串扰（同一 hash 通道被两个系统消费），破坏 `PE3` 与跨层独立性验收（`PG2`/`WA3`/`EC2`）。

**严重度**：**P1**。

**建议**：在实现规范中定义域标签的**数值编码契约**（例如"域 ID 参与哈希混合"）与**域注册表**归属（谁是唯一分配者）；并规定既有地形/地质域**不得修改**（否则破坏既有世界逐体素一致性）。

---

### P1-4 纪元与历史规模参数缺失

**证据**：`WorldHistory_Design.md` `HE1`（时间 = `Epoch` + `Ordinal`，由 Seed 派生）、`HE4`（时间只用于排序/归因/成立性）、`HN9`（事件数/文明数/影响半径/候选密度上限必须显式参数化）；`Resource_Design.md` `RY11`–`RY13` 依赖"纪元跨度"；但**没有任何数值化的纪元边界与规模上限定义**。

**影响**：魔法资源"时间积累"条件、历史事件数量、遗迹密度都无法实现；"同 Seed 同历史"无法被验证到具体数值层。

**严重度**：**P1**。

**建议**：在实现规范中定义纪元段（名称 → 序位范围）、单点事件查询的相关性判定、以及规模上限参数表。

---

### P1-5 资源 ↔ Mining ↔ Entity 边界无落点

**证据**：`PlayerMiningController.cs`（1003 行）对 `VoxelWorld`/`SetVoxel`/`DepositZone`/`VoxelType` 命中 **0**，其挖掘路径是 `DensityTerrain.VoxelField.Set(Air)`（文件头 `:10-12`）；`PlayerMiningController.cs:996-1000` 通过 `FindObjectOfType<DensityTerrain>()` 解析目标。`Resource_Design.md` §4.4 与 `RQ8` 声明资源对象形态属未来 Entity/Item 系统；`WorldGeneration_Geology.md` §6.2 声明晶簇属 Entity。

**影响**：即便资源判定层实现，也没有任何消费方：Mining 不识别资源（面对的是旧世界），Entity/Item 系统不存在；"资源"在 V1 只能是只读判定，无法产出。

**严重度**：**P1**（V1 可接受"只读判定 + 零对象"，但必须在实现前明确并写进验收）。

**建议**：明确三条：① V1 资源层**只产出判定**（对象化延后）；② Mining 的接入属未来规范，**不得**修改已冻结的 `VoxelWorld_Mining.md`；③ 若未来接入，消费路径必须是"只读查询"，不得回写。

---

### P1-6 新栈未接入场景

**证据**：`SampleScene.unity` 中 `VoxelWorldStreamingBootstrap` guid `2c531141a81459d499f3f2959fe28988` 出现 **0** 次；`DensityTerrain` guid `3af3bcfc875f4cbfae75f1f95dee772a` 出现 **1** 次；`PlayerMiningController` 还通过 `FindObjectOfType<DensityTerrain>()` 解析目标。

**影响**：新架构（VoxelCore + Geology + Runtime/Streaming/Mesh/View）目前**只存在于批处理验证**中，玩家实际体验到的是旧 `DensityTerrain` 世界。任何"最小可运行世界生成"都必须先决定二者关系，否则新层的落地不改变游戏行为。

**严重度**：**P1**（不是代码缺陷，而是**架构决策缺口**）。

**建议**：把"新旧世界关系"提为显式决策（见 §5 AD-1），并明确接入点唯一性（`VoxelWorldStreamingBootstrap` 是既有唯一 authorized 组装点）。

---

### P1-7 两套坐标契约并存

**证据**

* 旧：`ChunkSpace.cs:13` `LocalToVoxel(Vector3 terrainLocalPosition, float voxelSize)`、`:25` `WorldToVoxel(...)`、`:38` `VoxelToChunk(voxelCoordinate, Vector3Int chunkVoxelSize)`、`:54` `VoxelToLocal(...)`；`DensityTerrain.cs:16-37`（`terrainSize`/`chunkSize`/`chunkCount`/`chunkStartCoordinate`/`chunkVoxelSize`/`resolution`）、`:177` `EffectiveChunkVoxelSize`（**可配置、可为非立方**）。
* 新：`VoxelChunk.cs:18` `ChunkSize = 16`（固定立方）；`VoxelWorld.cs:47` `FloorDiv`、`:60` `WorldToChunkCoordinate`、`:71` `WorldToLocalCoordinate`（整数世界坐标，无 Transform）。
* 查询：`TerrainEffectiveTerrainQueryV2.cs:22/50` `IsExcavated(Vector3 terrainLocalPosition)`（Terrain Local）vs `WorldQuery_Design.md` §1.1（整数世界坐标点）。

**影响**：同一"点"在两套契约下含义不同（Local vs World、float `voxelSize` vs 单位体素 1×1×1、可配置 chunk 尺寸 vs 16³）；跨栈共享数据（存档、挖掘记录、查询结果）会静默错位。

**严重度**：**P1**。

**建议**：在实现前定义**转换契约**（哪一侧是权威、如何换算、`voxelSize` 是否恒为 1）与**过渡策略**；明确新栈 V1 不接受 Terrain Local 语义。

---

### P1-8 Biome 实现与规范量纲/集合不一致

**证据**：`IBiomeProvider.cs:22-31`（`MinTemperature = -500`、`MaxTemperature = 600`、`MinMoisture = 0`、`MaxMoisture = 1000`；`BiomeSample` 只含 `Biome/Temperature/Moisture`）；`GeologySample.cs:169`（`GeologyBiome{None, BiomeA, BiomeB}`）；规范侧 `WorldState_Design.md:108` 要求 `[0,1]` 归一化、§5.3 规定 first-match 有序规则表与 6 类群系（`Pipeline §7.2` 列出 Ocean/Desert/Forest/MagicForest/CorruptedLand/HolyLand）。

**影响**：若直接落地规范阈值到现有整数域，则"群系判定"与既有 `GeologyBiomeProvider` 的结论不一致；且 `GW6`/`GIV9` 退化等价性依赖既有映射（`Geology_Runtime_Implementation §6.1`），迁移不当会破坏已 PASS 的 12 项验证。

**严重度**：**P1**。

**建议**：定义**量纲迁移契约**（规范 [0,1] 为权威；实现侧换算在何处发生）与**群系集合迁移路径**（占位 A/B → 6 类；保留"能量为零时退化为 (Temperature, Moisture) 二维判定"的 `§5.8/WS11` 兼容条款），并在迁移后重跑 12 项验证。

---

### P1-9 World Query 有成为"第二套规则"的风险

**证据**：`WorldQuery_Design.md` `WQ1`/`WQ2`/`WV8` 明确禁止第二套规则，`WA20` 要求"条件集合比对"；但实现层已有的点查询 `GeologySampler` 内含完整判定与阈值（`GeologySampler.cs:165-288` 的 `Resolve`/`ResolveLayerMaterial`/`ResolveSurfaceHeight`/`ResolveCaveDensity`），其 `ExplainAt`（`:113`）已经具备解释能力——若 Query 层"照抄并微调"就会形成两套阈值。

**影响**：一旦出现两套条件，`WA17`（查询结论与生成结论逐项一致）必然失败，且会产生"查询说有、生成说没有"的不可解释差异。

**严重度**：**P1**。

**建议**：规定 Query 层**只能调用**各层已有的求值入口（例如 Geology 的 `GeologySampler.Sample`），不得复制条件；把 `WA20` 落成结构扫描项（阈值常量只能出现在各层规范对应的实现文件中）。

---

### P2 级问题（简述）

| ID | 证据 | 影响 | 建议 |
|---|---|---|---|
| **P2-1 命名碰撞** | `VoxelAwareMeshSpatialFace.cs:112` `class FaceRegion`；`PlayerResources.cs:7`；`TerrainEffectiveTerrainQueryV2.cs:22` | 新术语（Region / Resource / World Query）在旧栈已有同名不同义概念，检索与沟通易混 | 在新文档中统一以限定名区分（"世界 Region（World Region）"等）；不改旧代码 |
| **P2-2 指针陈旧** | `Pipeline_Design.md:96`（"未来 History 规范"）；`WorldQuery_Design.md` §2.2 第 1 步（"骨架规则"） | 读者无法定位权威文档 | 后续版本更新指针（指向 `WorldHistory_Design.md`、`WorldState_Design.md` §7.4） |
| **P2-3 符号名不一致** | `WorldState_Design.md:94/111` 引用 `SurfaceHeight`；实现为 `HeightFunction.GetTerrainHeight`（`HeightFunction.cs:27`）与 `GeologySampler.SurfaceHeightAt`（`:129`） | 实现者需自行映射 | 在实现规范中登记符号映射表 |
| **P2-4 顺序表述差异** | `Resource_Design.md` §8.2③ vs `WorldQuery_Design.md:135`（`WN4`） | 已由"无依赖 + 可交换"条款对齐，但实现中若不显式断言仍会分叉 | 把 `WA12`（顺序打乱结果逐项一致）纳入门禁 |
| **P2-5 群系覆盖不足** | `Pipeline_Design.md` `PM3` 已登记"`Desert` 必须在 `WorldState_Design.md` §5.3 规则表内定义"，但该表目前无 Desert 分支 | 规则表与目标群系集合不闭合 | 在规则表内补分支（属该规范的版本变更） |
| **P2-6 延后项** | 各规范"未来扩展"附录（大陆骨架、批量查询、缓存策略、动态生态、资源对象化、时间切片查询） | 不影响 V1 最小可运行 | 保持延后，不在实现前引入 |

---

## 5. 尚未解决的架构决策

| ID | 决策问题 | 为什么必须先解决 | 相关证据 |
|---|---|---|---|
| **AD-1** | 旧 `DensityTerrain` 世界与新 `VoxelWorld` 世界的关系：**并存 / 迁移 / 替换 / 双轨**？ | 决定 M7 接入方式、玩家挖掘路径、存档口径与全部下游（Mesh/View/挖掘） | 场景 GUID 核验（新栈 0 次）；`PlayerMiningController.cs:996` |
| **AD-2** | 体素真值查询语义：**Pristine（规则）** 与 **Current（真值含编辑）** 是否都要？未生成区域回退策略？ | 直接决定 World Query 的输出定义与 Mining/存档一致性 | `VoxelWorld.cs:124/141`；`VoxelWorld_Mining.md` 未生成策略 |
| **AD-3** | 参数契约：**采用 [0,1] 归一化（规范）并把实现换算**，还是**规范改为整数域**？群系集合 A/B → 6 类如何迁移？ | 阻塞 M1–M3；影响既有 `GIV9`/`GW6` 退化等价 | `WorldState_Design.md:108` vs `IBiomeProvider.cs:22-31` |
| **AD-4** | 域标签编码与注册表归属（谁分配 Domain ID）？ | 阻塞 M1–M5 的确定性验收 | `Pipeline §3.3`；`HeightFunction.cs:124`；`GeologyNoise.cs:189` |
| **AD-5** | 纪元划分与历史规模参数（纪元边界、事件上限、跨大陆事件的显式化形式） | 阻塞 M4；影响 Resource 的"时间积累" | `WorldHistory_Design.md` HE1/HN9；`Resource_Design.md` RY11 |
| **AD-6** | 资源对象化与采集的归属与时点（Entity/Item 系统何时出现；Mining 是否成为消费者） | 决定 V1 资源层是"只读判定"还是"可消费内容" | `Resource_Design.md` RQ8/§4.4；`PlayerMiningController.cs` |

---

## 6. 从当前代码到最小可运行世界生成的建议实施顺序

> **前提**：先完成 AD-1…AD-4 的决策，并把 P0-1/P0-3/P1-2/P1-3/P1-4 的契约补进**单一实现规范**（不新增多份文档）。每一步都必须保持既有 12 项验证 PASS，且不得修改被 SHA 冻结的文件。

| 步骤 | 内容 | 依赖 | 关键验收 | 不得触碰 |
|---|---|---|---|---|
| **M0** | 契约与决策定稿（AD-1…AD-4 + P0-1/P0-3/P1-2/P1-3/P1-4） | — | 契约可被机械比对（阈值/量纲/域标签唯一） | 既有规范与实现 |
| **M1** | **WorldState 层**：Base（5 参数）+ 派生（`Ocean`/`Fertility`）；`Elevation` 复用 `HeightFunction.GetTerrainHeight` / `GeologySampler.SurfaceHeightAt` | M0 | `WS1`（纯函数）、`WS3`（边界连续）、`WS7`（属性不改密度）、`WS11`（退化兼容） | `HeightFunction.cs` 等既有文件 |
| **M2** | **Region 层**：Source 采纳（P0-3）+ `RegionModifier` 合成 + 交换律 | M1 | `RG6`（交换律）、`RG9`（单一权威）、`RG10`（可关闭） | 既有 Geology/WorldState 语义 |
| **M3** | **Biome 层**：8 参数 → 6 类（含 `Desert` 分支）+ 阈值显式；通过既有 `IBiomeProvider` 接缝接入 `GeologyWorldGenerator.ResolveMaterial` | M2 | `GW1–GW10`、`GIV9` 保持 PASS；`WS11` 退化 | `GeologyWorldGenerator.cs`（已冻结）、`IBiomeProvider.cs` |
| **M4** | **History 层（最小形态）**：Seed 派生事实 + 自然来源 + `HistoryTags`；**不做**文明模拟 | M1（骨架量） | `HA1`（决定性与反例保护）、`HA11`（无反向依赖） | 下游产物 |
| **M5** | **Resource / Ecology 双分支**（互不读取）：Resource 读地质基础 + 环境 + 纪元 + 介质；Ecology 读 `FinalState` + `Biome` + Region 标签（+ 地形庇护，按 P0-5 修订后） | M3 | `RL15`/`RL18`（体素差异 0、零修改）、`EC16`/`EC18`；顺序打乱一致性（`WA12`） | 既有 Geology/Voxel |
| **M6** | **World Query 入口**：点查询（Pristine/Current 两模式，按 AD-2）+ 解释链；复用 `GeologySampler.ExplainAt` 模式；**新增**点查询类型，**不得**修改 `IWorldGenerator` | M1–M5 | `WA1–WA20`（尤其 `WA17` 与生成结论逐项一致、`WA20` 无第二套阈值） | `IWorldGenerator.cs`、`VoxelRuntimeCore.cs` |
| **M7** | **接入与切换**（依赖 AD-1）：唯一 authorized 组装点 = `VoxelWorldStreamingBootstrap` | M6 + AD-1 | 12 项既有验证保持 PASS；场景行为按 AD-1 决策验收 | Phase 6 既有实现文件 |

**顺序理由**：M1→M2→M3 是单向依赖链（环境 → 区域 → 群系）；M4 可与 M2/M3 并行（只读骨架）；M5 必须在 M3 之后（Resource 需要介质/地质，Ecology 需要 Biome）；M6 必须最后（需要全部层的结论才能给出解释链）；M7 需要 AD-1 决策。

---

## 7. 结论摘要

### 7.1 结论

| 维度 | 结论 |
|---|---|
| **规范层一致性** | **基本一致但有 5 项 P0**：单一权威、单向依赖、局部求值、解释能力都有明确条款（`Pipeline` §1.3/§3.3、`WorldQuery` WQ/WN/WL/WE），但存在 **P0-2（资源存在性表述）、P0-5（Ecology 输入集合）** 两处规范内部冲突，以及 **P0-1/P0-3/P0-4** 三处契约/链路/语义缺口 |
| **规范层可落地性** | **不能直接落地**：新层缺少实现级契约（P0-1）与关键算法（P0-3/P1-2/P1-3/P1-4），实现者无权威数值可依 |
| **规范层可测试性** | **可测试（设计上）**：各规范都给出可机械验证的验收条款（`WS*/RG*/PG*/HA*/EC*/RL*/WA*`），且已建立"结构扫描 + 双世界对照 + 内容哈希 + SHA 冻结"的验证套路（本会话 12 项 PASS 证明了该套路可执行） |
| **实现层现状** | **只有一条实现链**：VoxelCore + Geology（Stage A–E）+ Runtime/Streaming/Mesh/View + Bootstrap，且**未接入任何场景**；**WorldState / Region / History / Biome(真) / Resource / Ecology / World Query 七层零实现** |
| **当前能否组成最小可运行世界生成** | **不能**。需要先完成 M0（决策与契约），再按 M1→M7 实施；其中 AD-1（新旧世界关系）与 AD-2（真值语义）是最大的两个未决问题 |

### 7.2 已验证事实（可直接引用）

1. **[已验证-代码]** 新栈坐标契约：`VoxelChunk.ChunkSize = 16`、`VoxelCount = 4096`；`VoxelWorld` 提供 `FloorDiv`/`WorldToChunkCoordinate`/`WorldToLocalCoordinate`/`GetVoxel`/`SetVoxel`/`CollectDirtyChunks`；`VoxelType = {Air, Grass, Dirt, Stone}`（`VoxelType.cs:13-20`（取值 :15-18））。
2. **[已验证-代码]** 未生成区域语义：`VoxelWorld.cs:124` 返回 `false`（voxel=Air，**不创建 Chunk**）；`:141` `GetVoxel` 视为 Air。
3. **[已验证-代码]** 新栈确定性来源为自实现整数哈希：`HeightFunction.cs:114/124`、`GeologyNoise.cs:189`（**无 `UnityEngine.Random`**，符合 `WorldGeneration_Geology.md` §8.2 D1–D6）。
4. **[已验证-代码]** 地质层已提供**世界坐标点查询**与**解释**：`GeologySampler.cs:93/99/113/119/129/134/140/146/152`。
5. **[已验证-代码]** 地质判定链与占位集合：`GeologyRule{Unresolved,WorldBounds,Cave,AboveSurface,Surface,Depth,Deep}`（`GeologySample.cs:124`）；`GeologyDepositZone{None,ZoneA,ZoneB}`（`:153`）；`GeologyBiome{None,BiomeA,BiomeB}`（`:169`）。
6. **[已验证-代码]** 群系输入量纲为整数域：`BiomeSample` `Temperature ∈ [-500,600]`、`Moisture ∈ [0,1000]`（`IBiomeProvider.cs:22-31`）；与之配套的 `DepositSample` 含 `Exists/Zone/Richness/Grade`（`IDepositProvider.cs:33-42`）。
7. **[已验证-代码]** 新层零实现：`MagicDensity`/`HolyInfluence`/`CorruptionInfluence`/`Fertility`/`Continent`/`SeaLevel`/`Epoch`/`Civilization`/`HistoryTag`/`RegionModifier`/`SpawnCandidate`/`EcologyRules`/`WorldQuery` 在 `Assets/Scripts` 命中 **0**。
8. **[已验证-代码]** 场景实际运行旧栈：`SampleScene.unity` 中 `DensityTerrain` guid 出现 1 次、`VoxelWorldStreamingBootstrap` guid 出现 **0** 次；玩家挖掘脚本通过 `FindObjectOfType<DensityTerrain>()` 解析目标（`PlayerMiningController.cs:996-1000`）。
9. **[已验证-代码]** 两套坐标契约并存：`ChunkSpace.cs:13/25/38/54` + `DensityTerrain.cs:16-37`（Terrain Local / `voxelSize` / 可配置 `chunkVoxelSize`）vs `VoxelChunk.cs:18-21` + `VoxelWorld.cs:47-71`（固定 16³ / 整数世界坐标）。
10. **[已验证-执行]** 本会话早前 12 项验证全部 PASS（含 Geology Stage A–E 与 `GeologyRuntimeIntegrationValidation` 45 项 ×2），证明"新增层 + 零修改 + SHA 冻结 + 结构扫描"的验证套路在本项目可执行。

### 7.3 设计推论（需要在实现阶段验证）

1. **[推论]** 既有 `IBiomeProvider`（`IBiomeProvider.cs:70`）与 `IDepositProvider`（`IDepositProvider.cs:90`）是**可复用的接缝**：新的 Biome 层可作为其新实现接入 `GeologyWorldGenerator`（构造参数已有 `biomeProvider`，`GeologyWorldGenerator.cs:41`），从而**无需修改**地质实现文件。
2. **[推论]** `GeologySampler` 的点查询能力可支撑 World Query 的"Pristine 模式"实现；但 **`Current` 模式**必须另经 `VoxelWorld.TryGetVoxel`（`VoxelWorld.cs:126`）与编辑记录，且该模式与 `IWorldGenerator` 无关。
3. **[推论]** World Query 不应通过 `IWorldGenerator` 实现（其粒度是 `VoxelCoordinate` Chunk，`IWorldGenerator.cs` 接口成员只有一个 `Generate`），而应新增独立的点查询入口——这要求 M6 引入**新增类型**，不触碰 Runtime Core 依赖的抽象。
4. **[推论]** 若不先解决 AD-3（量纲），M3 落地会破坏既有的 `GIV9` 退化等价（`Geology_Runtime_Implementation.md` §7.1：关闭全部地质开关 ⇒ 输出与 Stage 1 逐体素一致）。
5. **[推论]** 若 M7 直接切换场景世界，则玩家挖掘路径必须同步迁移（`PlayerMiningController` 目前 0 命中新栈），否则会出现"看得见新地形、挖不动"的割裂。

### 7.4 待确认事项（不得当作已通过）

| # | 待确认 | 为什么无法在只读审查中确认 |
|---|---|---|
| 1 | 旧栈与新栈在**运行时**的行为差异（含 Play Mode 表现） | 本轮未运行 Unity；Play Mode 验证在本会话中从未执行 |
| 2 | 12 项验证在**本轮**是否仍然 PASS | 本轮未重跑（引用的是本会话早前执行结果） |
| 3 | 旧世界存档 / 编辑记录是否需要在 AD-1 的迁移中被保留 | 需要查看存档格式与 `TerrainEditLayer`/`TerrainEditRecordV2` 的落盘策略（未逐行精读） |
| 4 | 大陆骨架（Phase 1）是否会影响既有 `Streaming` 的加载集合判定 | 需在实现后以 `VoxelWorld_Runtime.md` §5.3 的判定式做回归（未验证） |
| 5 | `Chunk.md`/`Mining.md`/`VoxelAwareMesh.md` 与旧栈实现之间的偏差 | 本轮仅核对结构与关键定义，未逐行比对（三份合计 3,744 行） |
| 6 | 域标签的具体编码是否会与既有 `HeightFunction`/`GeologyNoise` 的 64/32 位哈希混合方式冲突 | 需在实现规范给出编码后才能验证 |

---

## 附录：本报告未做的事（避免误解）

1. **未修改任何文件**（含被 SHA 冻结的地质实现、任何 `.cs`/`.meta`/`.asmdef`/配置）。
2. **未新增任何设计规范**；仅新增本审查报告。
3. **未运行任何构建、测试或自动修复命令**；未触发会改变项目状态的操作。
4. **未对既有"兼容性声明"直接采信**：本报告中的所有兼容性结论均基于**实际文件正文与源码核对**；凡无法核对者一律标为 [待确认]。
5. **未开始修复**：P0/P1/P2 问题均只给出建议，等待进一步指令。
