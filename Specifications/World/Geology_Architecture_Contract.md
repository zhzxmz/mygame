# Geology Architecture Contract V1

> **文档性质**：**架构契约规范（Architecture Contract）**。本文档只定义 **Geology 层的定位、输入、输出、密度与体素边界，以及它与上下游的权限关系**。
> **仅创建 Markdown**；**不创建任何 `.cs`**；**不修改 `Assets/`**；**不修改任何已有 Specification 文件**；**不实现任何 Geology 逻辑**；**不新增参数**；**不新增 `VoxelType`**；**不改变已有因果方向**。
>
> **核心定位**：
>
> > **Geology = 世界空间中的地质解释层。**
> > 它回答："**这个世界坐标在地质上是什么**"——地形如何形成、密度从何而来、地表与地下结构如何、洞穴倾向如何、矿藏形成倾向如何。
>
> **必须兼容的上游规范**（本任务已读取，SHA 校验通过；**不得修改**）：
>
> | 规范 | 与本契约的关系 |
> |---|---|
> | `WorldParameter_Contract.md` | §3.5 表 D（`Density`/`Material`/`CaveDensity`/`DepositZone` **Owner = Geology**；`DepositZone` = **地质矿藏基础层**）、§4 权限矩阵（Geology = `O`；Biome = `R`（仅 P3）；Ecology = `R`）、§6.4（**保留域 `R-1`/`R-2`** = `HeightFunction`/`GeologyNoise` 通道，"只读保留、不得复用"）、`PW4`（读取必须在可读取层内） |
> | `WorldState_Runtime_Contract.md` | `WSC48`（**`Elevation` 是唯一"Geology 侧提供、WorldState 侧转发"的参数**）、`WSC49`（复用高度场合规）、`WSC50`（**Geology 不得写任何参数**）、`WSC51`（WorldState → Geology **只允许**经 Biome 的 **P3 地表材料**）、`WSC52`（**不得影响 P0/P1/P2**）、`WSC86`（不得复用保留域作为新层随机源） |
> | `Region_Architecture_Contract.md` | `RA101`（唯一间接路径 `Region → Modifier → FinalState → Biome → P3`）、`RA103`（Region 不得影响 `Density`/`CaveDensity`/地层/`DepositZone`）、`RA88`（`RegionType` 无规则权）、`RAV12`/`RAV13` |
> | `Biome_Architecture_Contract.md` | `BC15`（Biome **不得修改** `Density`/`Material`/`CaveDensity`/`DepositZone`/地层）、§1.4 材料归属澄清（**材料决定权属 Geology 的 P3**）、§4.2（Biome Identity 不含材料/体素/资源清单）、§11 矩阵（`Geology` 列 = `R`（仅 P3）） |
> | `WorldQuery_Design.md` | §2.2 第 6 步（Geology Evaluation）、§3.1（Geology 行权限）、§6（`WE1`–`WE8` 解释）、`WL1`–`WL13`（局部求值）、`WQ1`/`WQ2`（同一规则、禁止第二套）、`WA17`/`WA20` |
> | `WorldGeneration_Architecture_Audit.md` | **已验证代码事实**（§2.3/§7.2）：`GeologyRule{Unresolved,WorldBounds,Cave,AboveSurface,Surface,Depth,Deep}`、`GeologyDepositZone{None,ZoneA,ZoneB}`、`GeologyBiome{None,BiomeA,BiomeB}`、`DepositSample{Exists,Zone,Richness,Grade}`；审查 `P1-5`（资源↔Mining↔Entity 边界）、`P1-8`（占位量纲）、`M3`（Biome 经 `IBiomeProvider` 接缝接入 `GeologyWorldGenerator.ResolveMaterial`）、`M6`（Query 复用 `GeologySampler.ExplainAt` 模式） |
> | `Resource_Design.md` | §4.2 **两层存在性模型**（地质基础层 / 资源判定层）、`RU1`（`DepositZone` 只属 Geology）、`RU4`–`RU8`（Resource 不得改 `Density`/`VoxelType`/`Terrain`/`DepositZone`）、`RY15`（介质只读读取地层/材料/矿藏）、`RX-3`/`RX-12` |
> | `Ecology_Design.md` | §5 `ER2`（`DepositZone` 不得被 Ecology 定义/修改）、`EN4`（窝点只能**读取**既有洞穴/地形，不得改变地形）、`ER6`（资源候选不得写回 Geology）、`E-3` |
> | 地质规范族（**Geology 规则权威**） | `WorldGeneration_Geology.md`：§2.1（**世界是连续函数，Chunk 只是采样窗口**）、§2.3（`F1`–`F4`）、§3.2（**优先级链 P0–P3**）、§4（地层）、§5（洞穴）、§6.1（`DepositZone` 规则）、§6.2（**晶簇属 Entity**）、§7（群系接口）、§8.2（`D1`–`D6`）、§10.2/§10.4、`GE1`–`GE12`；`WorldGeneration_Geology_Implementation.md`：§3.1（唯一入口）、§3.2（**不得按 Chunk 局部坐标独立生成**）、§3.3、`GIV1`–`GIV9`；`WorldGeneration_Geology_Runtime_Implementation.md`：§2.1（固定链路）、§3.3（写入顺序）、§6.1（**群系只影响地表材料**）、§6.2（不影响项）、§7（GIV9 等价） |
> | `VoxelWorld_Mesh.md` / `VoxelWorld_Runtime*.md` / `VoxelWorld_Mining.md` / `ADR-001` | Mesh = **只读派生**；Chunk = 空间分区与管理单位；Mining = 运行时修改；Entity 不属 Chunk 所有权体系 |
>
> **冲突时的优先级**：**地质规则语义**（判定链、地层、洞穴、矿藏、群系接口）以 `WorldGeneration_Geology*.md` 为准；
> **参数身份/量纲/Owner/读写权限**以 `WorldParameter_Contract.md` 为准；**层间顺序**以 `WorldGeneration_Pipeline_Design.md` 为准；
> **查询与解释**以 `WorldQuery_Design.md` 为准；**资源判定**以 `Resource_Design.md` 为准；**生态候选**以 `Ecology_Design.md` 为准；
> **本契约负责**：Geology 的**定位与边界、输入封闭、输出形态、密度/体素/网格边界、上下游权限、确定性与局部求值口径**。
>
> **零修改声明**：本文档是**声明性文档**，不产生任何运行时效果；开启/关闭本文档**不得改变任何体素输出**（`GCV22`）。
> 与既有规范的差异一律在**附录 D 登记**，**不修正**任何既有文件。
>
> **文档构成说明（重要）**：本任务指令文本在 **§4 中途截断**。因此：
> **§1–§4 严格按任务书给定的编号段**（`GC1–GC10` / `GC11–GC30` / `GC31–GC50` / `GC51–GC70`）撰写，§4 按"正确链 + 禁止项"的明确意图补全；
> **§5–§12 与附录 A–D 按同族契约**（`Region_Architecture_Contract.md` / `WorldState_Runtime_Contract.md` / `Biome_Architecture_Contract.md`）**的一致结构补全**（上下游关系 / 确定性与局部求值 / 解释 / 消费者权限矩阵 / V1 范围 / 验收规则）。
> 若后续指令给出不同章节划分，以新指令为准。

---

## §1 Geology Position and Boundary

### 1.1 定义与位置

| # | 内容 |
|---|---|
| **GC1** | **Geology = 世界空间中的地质解释层**：把**世界坐标**解释为地质结论（**连续空间的地质解释**），是**世界坐标的纯函数组**（`WorldGeneration_Geology.md` §2.1） |
| **GC2** | Geology 位于因果链的 **Phase 6**：`Seed → WorldSkeleton → History/Region → WorldState FinalState → Biome → **Geology** → Voxel Evaluation → Mesh`（`Pipeline` §2.1/§2.2） |

### 1.2 Geology 负责

| # | 责任 |
|---|---|
| **GC3** | **地形形成解释** + **密度场来源解释** + **地表/地下结构解释** + **洞穴倾向解释** + **矿藏形成倾向解释**：以世界坐标为唯一自变量给出上述五类地质结论（Chunk 只是采样窗口） |

### 1.3 Geology **不等于**什么（四条硬性区分）

| # | Geology ≠ | 理由 | 正确归属 |
|---|---|---|---|
| **GC4** | **TerrainMesh** | Mesh 是**只读派生表示**，不参与判定 | `VoxelWorld_Mesh.md`（只读派生） |
| **GC5** | **VoxelStorage** | 体素真值由世界侧持有（`VoxelWorld`/`VoxelChunk`；`ChunkSize = 16`、`VoxelCount = 4096`） | `VoxelWorld_Core.md` |
| **GC6** | **ChunkSystem** | Chunk 是**空间分区与管理单位**（ADR-001），加载/生命周期归 Runtime/Streaming | `VoxelWorld_Runtime*.md`、`ADR-001` |
| **GC7** | **MiningSystem** | 挖掘是**运行时修改**（生成洞穴与运行时挖掘来源不同、结果相同） | `VoxelWorld_Mining.md`；`WorldGeneration_Geology.md` §5.3 |

### 1.4 明确不负责（Non-responsibility）

| # | Geology 不负责 |
|---|---|
| **GC8** | **WorldState 参数生成**（参数 Owner = WorldState 层）、**Region 影响生成**（Region 层）、**Biome 分类**（Biome 层）、**Resource 最终判定**（Resource 层）、**Ecology**（Ecology 层）、**Runtime 修改 / 玩家编辑**（Runtime State）、**Voxel 存储**（世界侧） |

### 1.5 硬性禁止与身份声明

| # | 规则 |
|---|---|
| **GC9** | **禁止** Geology **直接创建 Mesh**、**直接保存 Voxel**、**直接管理 Chunk**（三项边界禁止） |
| **GC10** | **Geology 是解释层，不是世界状态拥有者**：不得修改任何上游（参数/`RegionModifier`/Biome/History）、不得改写资源判定、不得新增 `VoxelType`（体素状态集合冻结为 Air/Grass/Dirt/Stone） |

---

## §2 Geology Input Contract

### 2.1 允许的输入（封闭白名单）

> 依据：`Pipeline` §1.3 Geology 行（输入 = **世界坐标 + 显式地质参数 + Biome（仅用于 P3 地表材料）**）、`WSC51`/`WSC52`。

| # | 允许输入 | 说明 |
|---|---|---|
| **GC11** | **`Seed`** | 地质域内的确定性来源（`WorldGeneration_Core.md` §3） |
| **GC12** | **`Position`（世界体素坐标）** | 唯一自变量；必须经 VoxelCore 换算得到（`chunk.LocalToWorldCoordinate(...)`，`..._Runtime_Implementation.md` §3.2） |
| **GC13** | **WorldSkeleton 产物** | 基础高度框架（既有 `HeightFunction`）与大陆掩码（掩码 = 全陆地 ⇒ 退化为既有地形，`WC2`） |
| **GC14** | **显式地质配置参数** | `WorldGenerationSettings`（`Seed`/`TerrainHeight`/`SoilDepth`/`WorldHeight`/`TerrainFrequency`/`TerrainAmplitude`）+ `GeologySettings`（`EnableCaves`/`CaveThreshold`/`CaveFrequency`/`EnableDeposits`/`DepositFrequency`/`DepositRichness`/`EnableBiomeModifiers`） |
| **GC15** | **Biome Identity（只读）** | **仅在 P3（地表材料）**：读取 Biome **结论**作为材料判定输入之一（`BC71`；`§6.1`） |
| **GC16** | **既有保留域通道** | `HeightFunction` / `GeologyNoise` 的既有混合方式（保留域 `R-1`/`R-2`）：Geology 作为**既有所有者**可继续使用；**不得修改其混合方式**（`WorldParameter_Contract.md` §6.4） |

### 2.2 禁止读取（黑名单）

| # | 禁止 Geology 读取 | 依据 |
|---|---|---|
| **GC17** | **`ResourcePresence`**（资源判定结果）与"资源清单" | `Resource_Design.md` `RX-12`；§7 |
| **GC18** | **EcologyResult**（生态候选 / `SpawnCandidate` / 区域级资格声明） | `Ecology_Design.md` `E-3`；`WSC55` |
| **GC19** | **RuntimeState**（脏标记、加载集合、tick、帧序、预算） | `F3`；`D3` |
| **GC20** | **PlayerState**（位置、进度、背包、已采集统计） | `PZ7`；`WSC58` |
| **GC21** | **VoxelCurrentState**（"该坐标当前是什么体素"、已生成内容、玩家编辑结果） | `WSC77`；§4 |
| **GC22** | **MiningModification**（挖掘记录 / 编辑网格 / 编辑层） | `WorldGeneration_Geology.md` §5.3 |
| **GC23** | **Mesh / 呈现缓冲 / 几何** | `VoxelWorld_Mesh.md`（方向相反：Mesh 只从体素派生） |
| **GC24** | **Chunk 生命周期**（是否已加载/已生成/已卸载） | `F3`；`Geology_Implementation.md` §3.3 |
| **GC25** | **WorldState 的 8 个参数（`FinalState`）直读** | **见 §2.3 与附录 D ①**：参数经 **Biome** 压缩后进入 Geology |
| **GC26** | **HistoryTags / 历史事实 / Region 来源 / `RegionType`** | `RA88`；`RA103`；`BC28` |
| **GC27** | **上游规则内部量**（噪声中间值、未登记派生量、其它层阈值） | `PW4`；`WQ2` |

### 2.3 输入封闭性（硬性）

| # | 规则 |
|---|---|
| **GC28** | 输入集合**封闭**：白名单（§2.1）之外的量一律不得读取（`Pipeline` `PA1`；`PW4`） |
| **GC29** | 输入必须**逐点可得**（不得要求"先算整个世界/先加载邻域"），且**不得**携带下游语义（资源/生态/对象/体素真值/玩家信息）或"目标结果"（`GIV1`；`C3`/`C4`） |
| **GC30** | 显式地质参数必须**已登记**（§2.1 的配置集合）；不得引入未登记的隐式全局状态（`WorldGeneration_Core.md` §3） |

---

## §3 Geology Output Contract

### 3.1 允许的输出（`GeologyState`：连续解释结果）

> **全部是连续/逐点的解释结果**（世界坐标的纯函数求值）：**不是**数组、**不是**容器、**不是**真值存储。

| # | 输出 | 含义 | 依据 |
|---|---|---|---|
| **GC31** | **`Elevation`（高度解释/转发）** | 既有高度场给出的地表高度；WorldState 侧只读转发 | `WSC48`；`WC2` |
| **GC32** | **`DensityTendency`（密度倾向）** | 实体/空判定（P0 世界边界 → P1 洞穴 → P2 地表之上/之下） | `WorldGeneration_Geology.md` §3.2 |
| **GC33** | **`SurfaceLayerInterpretation`（地表层解释）** | 地表层归属（`Rule = Surface`）与其材料判定（**P3**） | 同上 §3.2/§4 |
| **GC34** | **`SubsurfaceInterpretation`（地下结构解释）** | 地层解释（`Depth` / `Deep`，由 `SurfaceHeight` + `SoilDepth` 决定） | 同上 §4 |
| **GC35** | **`CavePotential`（洞穴倾向）** | 洞穴强度（`CaveDensity`）与洞穴状态（用于 **P1** 覆盖） | 同上 §5 |
| **GC36** | **`DepositPotential`（矿藏形成倾向）** | `DepositZone`（**地质矿藏基础层**）：哪里**地质上**存在资源倾向 | 同上 §6.1；`WorldParameter_Contract.md` §3.5 |

### 3.2 输出硬性规则

| # | 规则 |
|---|---|
| **GC37** | 输出必须是**世界坐标纯函数**：同一坐标重复求值恒等（`GE1`；`GIV1`） |
| **GC38** | 输出必须**连续**：跨 Chunk 边界一致（`GE3`；`GIV2`），不得出现"边界修补"式特例 |
| **GC39** | 输出必须**可独立求值**（单点采样，`Geology_Implementation.md` §3.1） |
| **GC40** | 输出**不得**包含任何"世界的副本"（不得输出体素集合、Chunk、区域表） |
| **GC41** | `DepositPotential` **不等于** `ResourcePresence`：前者是**地质倾向（基础层）**，后者是**资源判定（Resource 层）**（`WorldParameter_Contract.md` §3.5；`Resource_Design.md` §4.2） |
| **GC42** | `DepositPotential` 必须遵守 `§6.1` 规则：**属性层**（不改 `Density`/`Material`）、**受地层约束**、**空坐标必须返回 None 或视为无效**、**可查询**、**不参与 Mesh** |
| **GC43** | 输出的判定链必须**可解释**（§9）：每个结论都能回答"命中哪条规则、基于哪些条件" |

### 3.3 禁止的输出

| # | 禁止 Geology 输出 |
|---|---|
| **GC44** | **`Voxel[]`**（体素数组 / 体素真值 / 体素标记） |
| **GC45** | **`Chunk`**（Chunk 对象/容器/归属表） |
| **GC46** | **`Mesh`**（顶点/索引/材质槽/子网格） |
| **GC47** | **`Item`**（物品、掉落、战利品） |
| **GC48** | **Resource 实体**（晶簇、矿脉节点、可拾取物）——`§6.2`：**晶簇属 Entity（ADR-001）** |
| **GC49** | **Biome 结论**（分类属 Biome 层，Geology 只消费）、**资源判定**（Resource 层）、**生态候选**（Ecology 层）、**参数值**（WorldState 层） |
| **GC50** | **既有实现形态说明（登记项 ②）**：既有 `GeologySampler.Sample(...)` 返回逐点解释结果（`GeologySample`：`Rule`/`IsSolid`/`Material`/`SurfaceHeight`/`SoilDepth`/`CaveActive`/`CaveDensity`/`DepositZone`/`Biome`），符合 §3.1；而既有 `GeologyWorldGenerator.Generate(chunkCoordinate)` **返回 `VoxelChunk`** 属 **Phase 7（Voxel Output）** 的落地形态（把解释结果抽样为体素状态），**不改变**"Geology 层不拥有体素"的契约（所有权 vs 生产流程）——此条为**说明**，不作为禁止项 |

---

## §4 Density and Voxel Boundary（重点章节）

### 4.1 明确：**Geology 不拥有 voxel**

| # | 规则 |
|---|---|
| **GC51** | **Geology 不拥有体素**：体素真值的持有者是世界侧（`VoxelWorld` / `VoxelChunk`）；Geology 只提供**判定结果** |
| **GC52** | **正确链（冻结）**：`Geology → Density Evaluation → Voxel Query → Mesh` |
| **GC53** | **四段职责**：① Geology 给出**解释/判定**；② Density Evaluation 把判定**抽样**为"实体/空 + 材料"；③ Voxel Query 读取**体素真值**（生成结果 ⊕ 玩家编辑）；④ Mesh 从**体素数据**派生几何 |
| **GC54** | **方向不可逆**：Mesh 不得反向影响体素；Voxel Query 不得反向影响判定；判定不得依赖体素真值（`GC21`） |
| **GC55** | **`Chunk` 只是采样窗口**：Chunk 划分与世界性质无关（`WorldGeneration_Geology.md` §2.1）；Chunk 粒度属 Voxel Output 的落地形态 |

### 4.2 禁止事项（Density / Voxel / Mesh / Chunk）

| # | 禁止 |
|---|---|
| **GC56** | Geology **直接创建 Mesh**、写 Mesh、触碰 View |
| **GC57** | Geology **持有/返回 Voxel**（不得缓存体素、不得返回体素数组、不得把体素当输入） |
| **GC58** | Geology **管理 Chunk**（创建/销毁/加载/卸载/置脏/预算） |
| **GC59** | **新增 `VoxelType`** 或"地质专用体素"（`§10.2`） |
| **GC60** | Biome / Region / WorldState / History / Resource / Ecology 影响 **P0/P1/P2**（P3 是唯一允许的上游输入点，§6） |
| **GC61** | 任何下游**回写** Geology 判定（不得改写 `DepositZone`/`CaveDensity`/`Material` 的判定）（`RU1`；`BC15`） |
| **GC62** | 依赖 Voxel Query 结果或"已生成/已加载"状态做判定（`F3`；`WSC52`） |

### 4.3 优先级链与 P3 纪律（冻结，唯一允许的顺序）

```
P0 WorldBounds  → y < 0 或 y ≥ WorldHeight ⇒ 恒为空
P1 Cave         → CaveDensity ≥ CaveThreshold ⇒ 空（可覆盖岩石）
P2 Terrain      → y > SurfaceHeight ⇒ 空
P3 Material     → 地表层（Biome 可参与）/ Depth / Deep 的材料判定
```

| # | 规则 |
|---|---|
| **GC63** | 上述优先级链是 Geology 的**唯一**判定顺序（`WorldGeneration_Geology.md` §3.2） |
| **GC64** | 不得插入新优先级、不得调整顺序、不得并行化到破坏优先级语义 |
| **GC65** | **P0/P1/P2 与 Biome、Region、History、WorldState 参数、资源全部无关**（`WSC52`） |
| **GC66** | **P3 是唯一**可读取 Biome 结论的位置（`BC71`；`§6.1`） |
| **GC67** | P3 的 Biome 影响**只作用于地表层**（`y == SurfaceHeight`），取值限 `{Grass, Dirt, Stone}`（`Air` 不作地表材料） |
| **GC68** | P3 的 Biome 影响**可关闭**：`EnableBiomeModifiers == false` 或 `biomeProvider == null` ⇒ 退化地出力 `Grass`，且必须保持 **GIV9**（全特性关闭 ⇒ 与 `BasicTerrainGenerator` 逐体素一致） |
| **GC69** | **属性层纪律**：`DepositZone` 不改变 `Density`/`Material`（`GE7`：开关矿藏不影响任何体素状态） |
| **GC70** | **禁止在 Geology 内维护"世界的体素/Chunk 副本"**，禁止把 Voxel Query 结果回灌为判定输入（`GC21`/`GC57`/`GC62`） |

---

## §5 Geology 与 WorldState / Region / History 的关系

### 5.1 两条单向通道（唯一）

| # | 规则 |
|---|---|
| **GC71** | **通道 A（高度场）**：`Elevation` 由既有高度场产生（`HeightFunction` / `GeologySampler.SurfaceHeightAt`），**Geology/高度场 → WorldState 只读转发**（`WSC48`/`WSC49`） |
| **GC72** | **通道 B（群系）**：`WorldState → Biome → Geology（仅 P3）`（`WSC51`；`BC71`） |
| **GC73** | **除通道 A 与通道 B，Geology 与 WorldState 之间不存在任何其它边**（`WSC51`/`WSC52`） |
| **GC74** | **Geology 不得写任何参数**（`WSC50`；`PW2`） |
| **GC75** | **Geology 不得直读 `FinalState` 的 8 个参数**（只能经 Biome 进入，`GC25`） |
| **GC76** | **Geology 不得创建/修改 Region**，不得读 `RegionType`/`RegionView`/`RegionModifier` 原始分量（`RA88`/`RA103`） |
| **GC77** | Region 对 Geology 的影响**只能**经 `RegionModifier → FinalState → Biome → P3`（`RA101`） |
| **GC78** | **Geology 不得读历史事实或 `HistoryTags`**（历史只能经 Region 修正 → 参数 → Biome → P3）（`WSC46`；`BC28`） |
| **GC79** | **通道 A 是唯一**"Geology 侧提供、WorldState 侧转发"的参数通道；不得扩展为第二个参数（`WSC48`） |

### 5.2 与 Skeleton / WorldSkeleton 的关系

| # | 规则 |
|---|---|
| **GC80** | Geology 的地形基础来自 **WorldSkeleton 的基础高度框架**（既有高度场）；**掩码只做叠加**，不得替换既有高度场定义（`WC2`） |
| **GC81** | 掩码退化为"全部陆地"时，Geology 输出必须回到既有无限地形（退化兼容，`WC2`/`WorldState_Design.md` §5.8） |
| **GC82** | Geology **不得**读取骨架以外的世界状态；大陆元数据不得参与材料/密度判定 |

### 5.3 关系表（方向与权限）

| 关系 | 方向 | 允许的用途 | 禁止 |
|---|---|---|---|
| Geology ↔ WorldState | 双向但**各自单向**：高度场 → WorldState（只读转发）；WorldState → Biome → Geology（仅 P3） | 生成本身 | 写参数、直读参数、影响 P0/P1/P2 |
| Geology ↔ Region | Region → … → P3（间接） | 无直连 | 创建/修改 Region、读 `RegionType` |
| Geology ↔ History | 无直连（经 Region → 参数 → Biome） | 无 | 读历史事实/标签 |

| # | 规则 |
|---|---|
| **GC83** | 上述关系必须在**查询解释**中可呈现（解释链需标明"该结论受 Biome 影响 / 不受其影响"）（§9） |
| **GC84** | **违反上述任一方向的路径一律判为缺陷**（§12 `GCV17`） |

---

## §6 Geology 与 Biome 的关系（唯一直接消费）

### 6.1 边界

| # | 规则 |
|---|---|
| **GC85** | Biome 对 Geology 的作用**只有一个**：在 **P3** 作为**地表材料判定**的输入之一（`BC71`；`WSC51`） |
| **GC86** | **只作用于地表层**（`y == SurfaceHeight`）；不得扩展到 Depth/Deep，不得改变地表高度 |
| **GC87** | 取值限 `{Grass, Dirt, Stone}`；`Air` 不得作为地表材料（`..._Runtime_Implementation.md` §6.1 `B1`） |
| **GC88** | **不得改变密度分布**：不得改变 `SurfaceHeight`、不得改变实体/空分界（`§6.2`；`WSC52`） |
| **GC89** | **不得影响 P0/P1/P2**（世界边界 / 洞穴 / 地形密度）（`GC65`） |
| **GC90** | **不得影响调度**：不改变加载/卸载、Chunk 生命周期、脏标记、预算、并发（`§6.2`） |
| **GC91** | **不得影响几何**：不产生几何、不改变面/材质/子网格语义（`VoxelWorld_Mesh.md`） |
| **GC92** | **不得生成 Entity**：Biome 的"植被条件"只是**条件声明**（`§6.1 B2`） |
| **GC93** | Biome 结论在 Geology 内**只读**：不得修改 Biome、不得创建 Biome、不得回写其 Identity（`BC57`/`BC87`） |
| **GC94** | Biome 的输入**不得**含 Geology 产物（顺序：Geology 在 Biome 之后；`WorldQuery_Design.md` §2.2） |
| **GC95** | Biome 影响**必须可关闭**，且关闭后必须与既有基线**逐体素一致**（`GIV9`/`GW2`） |
| **GC96** | 不得为 Biome 影响新增体素类型、新增材料类别或新增隐式映射；映射表必须**显式**且唯一（`§6.1`「唯一允许的形式」） |

---

## §7 Geology 与 Resource 的关系（分层：`DepositZone` vs `ResourcePresence`）

### 7.1 两层模型（冻结）

| # | 规则 |
|---|---|
| **GC97** | **第一层 = 地质矿藏基础层（`DepositZone`，Owner = Geology）**；**第二层 = 资源判定层（`ResourcePresence`，Owner = Resource）**；两层是"基础 ∧ 条件"关系，不是重复定义（`WorldParameter_Contract.md` §3.5；`Resource_Design.md` §4.2） |
| **GC98** | `DepositZone` 的定义、产生与修改**只属于 Geology**（`RU1`） |
| **GC99** | Geology **不得**产出 `ResourcePresence`、不得判定"资源是否达到可采集程度"（`Resource_Design.md` §1.3/§4.2） |
| **GC100** | Geology **不得**产出资源实体（晶簇、矿脉节点、可拾取物）；**晶簇属 Entity**（ADR-001；`§6.2`） |
| **GC101** | Resource 对地质输出的读取是**只读消费**（介质/地层/矿藏），**不得回写**（`RU2`；`RX-3`） |
| **GC102** | `DepositZone` 必须遵守：**属性层**、**受地层约束**、**空坐标返回 None/无效**、**可查询**、**不参与 Mesh**（`§6.1` 规则 1–5） |
| **GC103** | 矿藏判定**不得**在空气中成立（`GE9`：`DepositZone ≠ None` 必须是实体坐标） |
| **GC104** | Geology **不得**读资源判定来调整自身判定（"因为资源判定有矿，所以提高地质倾向"禁止，`RX-12`） |
| **GC105** | 不得新增"资源体素类型"（矿物体素在 V1 **不存在**：`§6.1` 规则 1、`§10.2`） |
| **GC106** | **术语冻结**：既有规范中出现的"资源存在性"一律按 §7.1 的**两层**理解（基础层归 Geology、判定层归 Resource）；本文档**不修正**既有文件表述（附录 D ④） |

---

## §8 Geology 确定性与局部求值

### 8.1 确定性（世界坐标纯函数）

| # | 规则 |
|---|---|
| **GC107** | 同一 `Seed` + 同一**世界坐标** + 同一显式参数 ⇒ **完全一致**的地质解释（逐字段）（`GE1`；`GIV1`） |
| **GC108** | 每种地质量都可**独立重复求值**（无副作用、无状态累积）（`Geology_Implementation.md` §2.2） |
| **GC109** | **跨 Chunk 边界一致**：相邻 Chunk 共享边界的世界坐标必须得到相同结果（`GE3`；`GIV2`） |
| **GC110** | 不同 `Seed` ⇒ 至少一处地质差异（**反例保护**）（`GIV3`） |

### 8.2 禁止事项（继承 `F1`–`F4` 与 `D1`–`D6`）

| # | 禁止 |
|---|---|
| **GC111** | 依据 **Chunk 编号 / Chunk 局部坐标**注入随机性（`F1`；`Geology_Implementation.md` §3.2） |
| **GC112** | 相邻 Chunk **规则不同**（每 Chunk 自带噪声参数/种子）（`F2`） |
| **GC113** | 依赖加载顺序、邻居是否已生成、帧序、时间（`F3`） |
| **GC114** | `UnityEngine.Random` / `System.Random` / 任何有状态伪随机源（`F4`；`D1`） |
| **GC115** | `Time` / `DateTime` / `Guid` / 时间戳（`D2`） |
| **GC116** | 依赖 `Mathf.PerlinNoise` 等平台相关噪声；必须沿用**自实现整数哈希值噪声**策略（`D5`；`§8.3`） |
| **GC117** | 依赖 `Dictionary`/`HashSet` 遍历顺序（`D6`） |
| **GC118** | 读玩家状态、运行时状态、已生成内容（§2.2） |

### 8.3 域（Domain）

| # | 规则 |
|---|---|
| **GC119** | 地质层使用**既有保留域** `R-1`（`HeightFunction`）/ `R-2`（`GeologyNoise`）；它们是**既有实现的所有者**，本契约**不允许修改其混合方式**（`WorldParameter_Contract.md` §6.4；`WSC86`） |
| **GC120** | **新增层不得复用保留域**：History/Region/WorldState/Biome/Resource/Ecology 各自的域必须与地质保留域隔离（`PZ12`） |
| **GC121** | 地质层**不得**新增第二套噪声通道（如确需新材料/新特征域，必须修改地质规范并升版本） |

### 8.4 局部求值（Local Evaluation）

| # | 规则 |
|---|---|
| **GC122** | 地质解释必须支持**单点求值**：任意世界坐标可独立得到全部地质解释字段（`Geology_Implementation.md` §3.1 唯一入口） |
| **GC123** | **禁止**为回答一个点而生成整个世界、枚举全部 Chunk、建立全局地质表（`WL3`/`WL4`） |
| **GC124** | **禁止**依赖邻域是否已生成/已加载才能得出解释（`F3`；`WL8`） |
| **GC125** | 缓存（若实现）必须**透明**（由 `(Seed, 参数, 世界坐标)` 索引、可清空、不改输出、非权威）；求值成本必须与该点相关计算量相关（`WL9`/`WL10`/`WL11`） |
| **GC126** | 生成器按 Chunk 产出体素的**写入顺序**必须固定（`localX → localY → localZ`），且**不得**影响解释结果（`..._Runtime_Implementation.md` §3.3） |

---

## §9 Geology 与 World Query（解释能力）

| # | 规则 |
|---|---|
| **GC127** | Query 必须能解释 **"为什么这里是实体/空？"**：命中 P0/P1/P2 的哪一条 |
| **GC128** | Query 必须能解释 **"为什么这个坐标是这种材料？"**：P3 分支 + 层归属 + 是否受 Biome 影响（含"无影响"的显式说明） |
| **GC129** | Query 必须能解释 **"为什么这里有洞？"**：`CaveDensity` 值 + 阈值 + 洞穴状态 |
| **GC130** | Query 必须能解释 **"为什么这里的地质矿藏倾向是 X？"**：`DepositZone` 判定 + 地层约束 + 空坐标返回 None 的说明 |
| **GC131** | Query 必须能解释 **"为什么这里没有 X？"**：**可解释的"没有"**——指出哪一条前提不成立（越界 / 未达阈值 / 地层不符 / 属性层关闭） |
| **GC132** | 解释必须**与生成同源**：不得另算一套阈值/规则（`WQ1`/`WQ2`；`WA20`） |
| **GC133** | 解释必须**确定性**且**只读**（`WE4`/`WE5`） |
| **GC134** | 解释必须按**因果方向**组织（条件 → 结论）；禁止由材料反推密度（`WE2`/`WE3`） |
| **GC135** | 解释粒度**到层与条件为止**：不含资源清单、生态、对象细节（`WE7`） |
| **GC136** | 解释不得改变任何判定结果（`WQ5`） |
| **GC137** | 既有 `GeologySampler.ExplainAt(...)` 是解释能力的**既有先例**（审查 `M6`）；新增查询层必须复用同一规则集（不得复制阈值） |
| **GC138** | 解释链必须能呈现 §5 的两条通道（何处受 Biome 影响 / 何处不受影响）（`GC83`） |

---

## §10 Consumer Permission Matrix

**图例**：`O` = 拥有/产出；`R` = 只读；`R*` = **仅解释**只读；`✗` = 明确无权限；`—` = 不适用。

| Geology 输出 ＼ 消费者 | WorldState | Region | Biome | Resource | Ecology | Voxel Evaluation（Phase 7） | Mesh / View | Runtime / Streaming | WorldQuery | Entity（未来） |
|---|---|---|---|---|---|---|---|---|---|---|
| **`Elevation`（高度解释）** | `R`（通道 A：只读转发） | `R`（只读；不得修正） | `R`（经 8 参数之一） | `R`（介质/地层） | `R`（地形庇护） | `R` | ✗（经体素间接） | `R` | `R` | — |
| **`DensityTendency`（P0–P2 判定）** | ✗ | ✗ | ✗ | ✗ | ✗ | **`O` 的输入**（抽样为体素） | ✗（只读体素） | ✗ | `R`（解释） | — |
| **`SurfaceLayerInterpretation` / `SubsurfaceInterpretation`** | ✗ | ✗ | ✗ | `R`（介质/地层，`RY15`） | `R`（地形庇护，`EN4`） | `R`（抽样） | ✗ | ✗ | `R` | — |
| **`CavePotential`（`CaveDensity`）** | ✗ | ✗ | ✗ | `R`（介质） | `R`（地形庇护，`EN4`） | `R`（P1 覆盖） | ✗ | ✗ | `R` | — |
| **`DepositPotential`（`DepositZone`）** | ✗ | ✗ | ✗ | `R`（**基础层输入**，`RU2`） | `R`（仅资格/归因；不得定义，`ER2`） | `R`（属性层，不写体素） | ✗ | ✗ | `R` | `R*`（仅解释） |
| **Geology 规则本身（阈值/噪声）** | ✗ | ✗ | ✗ | `R`（只读复用，不得复制） | ✗ | `R`（同源求值） | ✗ | ✗ | `R`（同源求值） | ✗ |

**矩阵自查**

| # | 自查项 | 期望 |
|---|---|---|
| **GC139** | 是否存在**任何列**写出 Geology 输出？ | **必须为 0**（只读矩阵；`GC61`） |
| **GC140** | `WorldState` 列是否出现写权限或"Geology → 参数"的读权限？ | **必须为 0**（除通道 A 的只读转发）（`GC73`/`GC74`） |
| **GC141** | `Biome` 列是否出现任何对 Geology 的读/写？ | **必须为 ✗**（Biome 不得读 Geology） |
| **GC142** | `Mesh / View` 列是否出现读 Geology？ | **必须为 ✗**（Mesh 只从体素派生）（§4.1） |
| **GC143** | `Runtime / Streaming` 列是否出现写权限？ | **必须为 0**（`GC58`） |
| **GC144** | `Resource` 列是否只出现 `R`（不得写）？ | **必须是**（`RU2`） |
| **GC145** | `Ecology` 列是否只出现 `R`（不得写）？ | **必须是**（`ER2`/`ER6`） |
| **GC146** | **体素化注入点唯一**：是否只有 `Voxel Evaluation（Phase 7）` 一列把 Geology 判定转为体素？ | **必须是**（`GC52`/`GC53`） |

---

## §11 V1 Scope

### 11.1 V1 只定义

| # | 只定义 |
|---|---|
| **GC147** | **Biome…Geology 的八项内容**：① 定位与边界（§1）；② 输入契约（§2）；③ 输出契约（§3）；④ 密度/体素/网格边界（§4）；⑤ 上下游关系（§5 WorldState/Region/History、§6 Biome、§7 Resource）；⑥ 确定性与局部求值（§8）；⑦ 解释能力（§9）；⑧ 消费者权限矩阵（§10）与验收规则（§12） |

### 11.2 V1 **不定义**

| # | 不定义 | 归属 |
|---|---|---|
| **GC148** | **具体阈值**（洞穴阈值、频率、矿藏富集度映射等数值） | 显式配置参数 + 地质规范 §10.4 阶段规范 |
| **GC149** | **噪声公式 / 插值 / 衰减形状** | 地质规范族（`D5`：自实现整数哈希值噪声） |
| **GC150** | **地层数值**（`SoilDepth` 取值策略、层厚分布） | `WorldGeneration_Geology.md` §4 + 配置参数 |
| **GC151** | **新增地质特征**（河流、矿脉几何、结构洞、地热等） | 未来规范（不得在 V1 内引入） |
| **GC152** | **体素类型的扩展**（矿物体素、魔法方块、腐化方块等） | **V1 明令禁止**（`§10.2` 新增体素类型 = Out of Scope） |
| **GC153** | **材料纹理 / 表现 / Mesh 细节** | `VoxelWorld_Mesh.md` / `VoxelWorld_View.md` |
| **GC154** | **开采与掉落规则** | `VoxelWorld_Mining.md` / 未来规范（不得修改） |

### 11.3 硬性禁令

| # | 禁令 |
|---|---|
| **GC155** | 不创建任何代码结构（class/interface/struct）；不实现 Geology 逻辑；不改任何已有规范文件与 `Assets/` 文件 |
| **GC156** | 不新增参数（地质参数只能来自 §2.1 已登记的配置集合） |
| **GC157** | 不新增 `VoxelType`；不改变体素状态集合；不新增因果边、不改变因果方向（`GC73`/`GC77`/`GC85`） |
| **GC158** | 不允许任何下游获得 Geology 输出的写权限（`GC139`）；不允许 Geology 读下游（§2.2）；不允许按 Chunk 组织解释规则或缓存（`GC111`/`GC112`/`GC125`） |

---

## §12 Acceptance Rules

### 12.1 确定性与纯函数性

| # | 不变量 | 验证方法 |
|---|---|---|
| **GCV1** | 同 `Seed` + 同世界坐标 ⇒ 同解释结果（逐字段） | 重复求值 N 次比较（`GE1`/`GIV1`） |
| **GCV2** | 跨 Chunk 边界一致（相邻 Chunk 边界列逐值相同） | 边界列比对（`GE3`/`GIV2`） |
| **GCV3** | 与生成顺序 / 交错生成 / 乱序生成无关 | 乱序对照（`GIV1` §3.3） |
| **GCV4** | 不同 `Seed` ⇒ 至少一处不同（**反例保护**） | 双世界差异断言（`GIV3`） |
| **GCV5** | 无非确定性来源（`Random`/`Time`/`DateTime`/`Guid`/平台噪声/遍历顺序） | 结构扫描（`D1`–`D6`；`GC114`–`GC117`） |

### 12.2 输入封闭与无新增

| # | 不变量 | 验证方法 |
|---|---|---|
| **GCV6** | **输入封闭**：求值路径只出现 §2.1 白名单量 | 结构扫描（输入符号白名单比对；`GC28`） |
| **GCV7** | **无下游读取**：不出现资源/生态/体素真值/Runtime/玩家/Mesh/Chunk 生命周期 | 结构扫描（黑名单命中 = 0；§2.2） |
| **GCV8** | **无参数直读**：不出现 8 参数的直读（仅通道 A 的只读转发声明） | 结构扫描（`GC25`/`GC75`） |
| **GCV9** | **无新增参数**：不出现未登记的"地质世界参数" | 参数集合比对（`GC156`） |
| **GCV10** | **无新增 `VoxelType`** | 结构性检查（`GC59`/`GC157`） |

### 12.3 输出与边界

| # | 不变量 | 验证方法 |
|---|---|---|
| **GCV11** | **输出形态合规**：只输出连续解释字段（不输出 `Voxel[]`/`Chunk`/`Mesh`/`Item`/资源实体/Biome 结论/生态/参数） | 结构检查（`GC44`–`GC49`） |
| **GCV12** | **优先级链正确**：P0→P1→P2→P3 顺序不变；`Cave > Rock`；`y ≥ WorldHeight` 恒空；`y > SurfaceHeight` 恒空 | 逐体素与独立重算比对（`GE5`） |
| **GCV13** | **属性层纪律**：开关 `DepositZone` 不改变任何体素状态 | 两次生成体素快照完全相等（`GE7`） |
| **GCV14** | **矿藏只在实体内**：`DepositZone ≠ None` ⇒ 实体 | 全量查询比对（`GE9`） |
| **GCV15** | **Biome 只影响 P3 地表材料**：差异集合 ⊆ 地表层材料，且取值在 `{Grass, Dirt, Stone}` 内 | 差异集合检查 + 开关对照（§6.1） |
| **GCV16** | **Biome 影响可关闭**：`EnableBiomeModifiers == false` ⇒ 地表材料退化为既有行为 | 与基线逐体素对照（`GC68`/`GC95`） |

### 12.4 无反向依赖 · 无所有权越界

| # | 不变量 | 验证方法 |
|---|---|---|
| **GCV17** | **无反向边**：不存在 `Geology → WorldState/Region/Biome/ResourcePresence/RuntimeState` 的写路径 | 结构扫描（反向写 = 0；`GC10`/`GC74`/`GC99`） |
| **GCV18** | **无 Voxel / Chunk / Mesh 所有权**：Geology 不持有体素、不管理 Chunk、不创建 Mesh | 结构扫描（`GC9`；`GC139`/`GC142`） |
| **GCV19** | **无 Resource / Ecology 所有权**：Geology 不产出 `ResourcePresence`、不产出生态候选、不得被它们回写 | 结构扫描（`GC99`/`GC100`/`GC104`） |
| **GCV20** | **无 Runtime 污染**：Geology 不读/不写 Runtime 状态；无跨帧地质状态 | 结构扫描 + 前后对照（`GC19`/`GC143`） |

### 12.5 解释一致性与零修改

| # | 不变量 | 验证方法 |
|---|---|---|
| **GCV21** | **解释与生成同源**：查询解释使用同一套阈值/规则（无第二套） | 条件集合比对（`GC132`；`WA20`） |
| **GCV22** | **零修改**：既有实现与既有规范 SHA 不变；**GIV9 等价**（全特性关闭 ⇒ 与 `BasicTerrainGenerator` 逐体素一致）与既有 12 项验证保持 PASS | 文件哈希比对 + GIV9 对照（`GE11`；`GW2`） |

---

## 附录 A：数据流图

```
Seed ──────────┐
               │
WorldSkeleton ─┼─►（基础高度框架 + 大陆掩码，只读）
               │
WorldState FinalState ──► Biome（Phase 5）──► Biome Identity（只读）
               │                                  │
               │                                  ▼（仅 P3）
               └──────────────────────────────► Geology（Phase 6 · 地质解释层 · 本契约）
                                                  │
                                                  │  GeologyState（连续解释结果）
                                                  │  · Elevation（转发）
                                                  │  · DensityTendency（P0–P2）
                                                  │  · SurfaceLayerInterpretation / SubsurfaceInterpretation
                                                  │  · CavePotential（P1）
                                                  │  · DepositPotential（DepositZone 基础层）
                                                  ▼
                                     Density Evaluation（抽样：实体/空 + 材料）
                                                  ▼
                                     Voxel Query（体素真值 = 生成结果 ⊕ 玩家编辑）
                                                  ▼
                                              Mesh（只读派生）

  Geology ──► WorldState：仅 `Elevation` 通道（只读转发）        [通道 A]
  WorldState ──► Biome ──► Geology：仅 P3 地表材料                [通道 B]

  ═══ 禁止：Geology → WorldState / Region / Biome / ResourcePresence / RuntimeState（GCV17）═══
  ═══ 禁止：Geology 创建 Mesh / 持有 Voxel / 管理 Chunk（GC9 / GC56–GC58）═══
```

## 附录 B：归属速查表

| 事项 | 唯一归属 | 依据 |
|---|---|---|
| 地质判定规则（优先级链、地层、洞穴、矿藏、群系接口） | 地质规范族（`WorldGeneration_Geology*.md`） | `GC63`；§3.2 |
| `Density` / `Material` / `CaveDensity` / `DepositZone` 的值 | **Geology** | `WorldParameter_Contract.md` §3.5 表 D |
| 体素真值（`VoxelWorld` / `VoxelChunk`） | 世界侧（VoxelCore） | `GC51`；`VoxelWorld_Core.md` |
| 体素的**写入动作**（生成时抽样） | Voxel Output（Phase 7；既有 `GeologyWorldGenerator.Generate`） | `GC50`/`GC53` |
| Mesh / 几何 | Mesh 层（只读派生） | `GC46`；`VoxelWorld_Mesh.md` |
| Chunk 生命周期 / 加载 / 脏 / 预算 | Runtime / Streaming | `GC6`/`GC58`；`VoxelWorld_Runtime*.md` |
| 运行时修改（玩家编辑） | Mining / Runtime State | `GC8`；`VoxelWorld_Mining.md` |
| 8 个环境参数（含 `Elevation` 转发） | WorldState 层 | `GC71`/`GC74`；`WSC48`/`WSC50` |
| Biome 分类结论 | Biome 层 | `Biome_Architecture_Contract.md` §3 |
| `ResourcePresence`（资源最终判定） | Resource 层 | `GC99`；`Resource_Design.md` §4.2 |
| 生态候选 | Ecology 层 | `GC8` |
| 对象 / 晶簇 / 结构 | 未来 Entity / Structure 系统 | `GC48`/`GC100`；ADR-001 |

## 附录 C：禁止清单

| # | 禁止项 | 反例（必须判为缺陷） | 依据 |
|---|---|---|---|
| **GCX-1** | Geology 写 WorldState 参数 | 地质规则调整 `MagicDensity` | `GC74`；`WSC50` |
| **GCX-2** | Geology 直读 `FinalState` | 材料判定里直接读 `Temperature` | `GC25`/`GC75` |
| **GCX-3** | Geology 写 Region / 读 `RegionType` | 由 `RegionType` 决定材料 | `GC76`；`RA88` |
| **GCX-4** | Geology 改 Biome | 地质结果回写群系结论 | `GC93` |
| **GCX-5** | Geology 产出/改写资源判定 | 地质层直接给出"可采集"判定 | `GC99`；`RU1` |
| **GCX-6** | Geology 产出资源实体 | 地质层放置晶簇对象 | `GC48`/`GC100` |
| **GCX-7** | Geology 读生态候选 | 由"已有植被"决定材料 | `GC18` |
| **GCX-8** | Geology 读/写 Runtime 状态 | 由加载状态决定洞穴 | `GC19`；`F3` |
| **GCX-9** | Geology 读玩家/挖掘记录 | 由"玩家挖过"改变判定 | `GC20`/`GC22` |
| **GCX-10** | Geology 持有体素 / 返回体素数组 | 地质层缓存 `VoxelChunk` 作为真值 | `GC5`/`GC44`/`GC57` |
| **GCX-11** | Geology 创建 Mesh | 地质层直接生成几何 | `GC9`/`GC56` |
| **GCX-12** | Geology 管理 Chunk | 地质层创建/卸载 Chunk、置脏 | `GC6`/`GC58` |
| **GCX-13** | 新增 `VoxelType` / 矿物体素 | 为铁矿新增体素类型 | `GC10`/`GC59`/`GC105` |
| **GCX-14** | Biome 影响 P0/P1/P2 | 群系改变洞穴或地形高度 | `GC60`/`GC88`/`GC89` |
| **GCX-15** | 按 Chunk 注入随机性 / 每 Chunk 规则不同 | 每个 Chunk 使用自己的噪声种子 | `GC111`/`GC112`；`F1`/`F2` |
| **GCX-16** | 第二套地质规则 | 查询层另算一套洞穴阈值 | `GC132`/`GCV21`；`WQ2` |

## 附录 D：与既有规范的对齐登记与变更规则

### D.1 登记项（**本文档不修改任何既有文件**）

| # | 需对齐内容 | 位置 | 真相（按本契约） |
|---|---|---|---|
| ① | 任务书 §2 把 **"WorldState FinalState"** 列为 Geology 允许输入 | 任务书（本次指令）；`Pipeline` §1.3（Geology 行）；`WSC51`/`WSC52`；`BC71` | **按既有规范冻结**：Geology **不得直读** 8 参数；参数只能经 **Biome Identity（只读）** 在 **P3** 进入 Geology（`GC25`/`GC75`）。任务书该条目按"**经 Biome 间接**"理解 |
| ② | 既有 `GeologyWorldGenerator.Generate` 返回 `VoxelChunk` | `GeologyWorldGenerator.cs:128`；审查 §2.3/§7.2 | 属 **Phase 7（Voxel Output）** 的落地形态；**不改变**"Geology 层不拥有体素"的契约（`GC50`） |
| ③ | 任务书 §3 的 `GeologyState` 字段 | 本契约 §3.1；既有 `GeologySample` 字段 | 一致：既有字段可逐项映射到 §3.1 的解释字段；其中 `Biome` 字段属**输入**（只读）而非 Geology 输出（`GC49`） |
| ④ | `DepositZone` 与 `ResourcePresence` 的"存在性"表述 | `WorldParameter_Contract.md` §3.5；`Resource_Design.md` §4.2；`Ecology_Design.md:286`（`ER2`） | 两层模型（§7.1）：基础层归 Geology、判定层归 Resource；`ER2` 的意图保持成立（`GC106`） |
| ⑤ | Biome 影响的既有实现形态（`IBiomeProvider` + `EnableBiomeModifiers`） | `GeologyWorldGenerator.cs:41/70/173/186`；`..._Runtime_Implementation.md` §6.1 | 既有**接缝**保留；Biome 层落地（审查 `M3`）通过该接缝接入，**不得修改**既有实现文件（`GCV22`） |
| ⑥ | 占位量纲与占位类别（审查 `P1-8`） | `IBiomeProvider.cs:22-31`（整数域）；`GeologySample.cs:153/169`（`ZoneA`/`ZoneB`、`BiomeA`/`BiomeB`） | 按 `WorldParameter_Contract.md` §5.3 的**边界映射**理解；替换占位属审查 `AD-3` 决策；**不修改代码** |
| ⑦ | 审查 `P1-5`（资源 ↔ Mining ↔ Entity 无落点） | 审查 §3/§4 详情 | 本契约只固定"地质基础层 vs 资源判定层"的分层（§7）；**开采/对象化**仍属未来规范（`GC154`） |
| ⑧ | 任务书编号段与本文档编号段的一致性 | 任务书 §1/§2/§3 给定 `GC1–GC10`/`GC11–GC30`/`GC31–GC50`；§4 指令截断 | §1–§4 严格按给定段编号；§4 补全为 `GC51–GC70`；§5 之后（`GC71+`）与 `GCV*`/`GCX-*` 按同族结构补全 |

### D.2 变更规则

1. 修改 Geology 的输入白名单、输出字段集合、密度/体素边界或权限矩阵，**必须**升版本并同步更新 §2/§3/§4/§10 与附录 B。
2. **不得放宽**：`GC9`/`GC10`（禁止清单与身份）、`GC28`（输入封闭）、`GC51`/`GC52`（不拥有体素与正确链）、`GC63`/`GC66`（优先级链与 P3 唯一性）、`GC73`（两条单向通道）、`GC97`–`GC106`（分层与属性层纪律）、`GC139`–`GC146`（矩阵自查）。
3. 与既有规范冲突时，先按 D.1 **登记**，再由对应规范的**版本变更**解决；本文档不得直接改写其它规范。
4. 任何"新增地质特征"提案（河流 / 矿脉几何 / 新地层 / 新矿藏类别）必须**独立成规范**，并证明：不新增 `VoxelType`、不改变优先级链、`GIV9` 与既有 12 项验证保持 PASS、且**可关闭**。
5. 任何"Geology 输出体素/网格"的提案**一律禁止**（§4.1）：体素化必须是 Phase 7 的独立落地形态。
