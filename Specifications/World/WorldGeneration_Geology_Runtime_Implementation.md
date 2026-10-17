# World Generation Geology Runtime Implementation Specification V1

> **文档性质**：本文档定义 **`GeologyWorldGenerator` 如何实现 `IWorldGenerator`** ——
> 即 `WorldGeneration_Geology_Implementation.md` §13.1 的 **Stage D（接入层）** 实现规范。
>
> **本轮只写规范**：不创建 `GeologyWorldGenerator.cs`，不创建任何其它代码，不修改任何既有 `.cs`。
>
> 上游规范：
> * `Specifications/World/WorldGeneration_Geology.md` —— 地质**规则**层（P0–P3 优先级、地层、洞穴、矿藏、群系接口）；
> * `Specifications/World/WorldGeneration_Geology_Implementation.md` —— 接入层规范（Stage A–F 划分、结构纪律、GIV1–GIV9）；
> * `Specifications/World/WorldGeneration_Core.md` —— 生成契约（Seed + ChunkCoordinate → VoxelChunk、确定性、Chunk 可独立计算）；
> * `Specifications/World/VoxelWorld_Core.md` —— 世界真值、`VoxelType` 集合（冻结为 4 值）、坐标契约；
> * `Specifications/World/VoxelWorld_Runtime_Interface.md` —— `IWorldGenerator` 抽象与依赖倒置；
> * `Specifications/World/VoxelWorld_Runtime_Core_Implementation.md` —— 调用方契约（§5.1 四步、§5.3 写入纪律）；
> * `Specifications/ArchitectureDecisionWorldSpaceEntity.txt`（ADR-001）—— Voxel / Entity 边界。
>
> 冲突时的优先级：真值 > 生成契约 > 地质规则 > 调度 > 抽象 > Geology 实现规范 > **本文档**。

---

## 1. 定位

### 1.1 是什么

**`GeologyWorldGenerator` 是 `IWorldGenerator` 的一个新实现**：

```
IWorldGenerator                                  ← 抽象（冻结，不改签名）
   ├── BasicTerrainGenerator                     ← Stage 1 既有实现（**不修改**，继续可用）
   └── GeologyWorldGenerator                     ← 本规范定义的新实现（地质层 + Cave + Deposit/Biome 接入）
```

* **替换方式**：在**组装点**（游戏层，`Runtime_Interface.md` §2.4）把
  `new BasicTerrainGenerator(settings)` 换成 `new GeologyWorldGenerator(...)`；
  这是**唯一**需要的改动点；
* **Runtime Core 无感替换**：Runtime Core 只认识 `IWorldGenerator`（`Runtime_Interface.md` §4），
  它不区分、也不需要知道具体实现 ⇒ **Runtime / Streaming / Mesh / View / Bootstrap 一律零修改**；
* **不改 `BasicTerrainGenerator`**：旧实现既不被修改、也不被新实现调用（§7 的等价性以"独立表达 + 逐体素比较"证明）。

### 1.2 输入 / 输出

| 项 | 内容 |
|---|---|
| 输入（构造期） | `WorldGenerationSettings`（既有 Stage 1 参数，含 `Seed`）<br>`GeologySettings`（Stage A 地质参数）<br>`IDepositProvider`（Stage C，可选，只读）<br>`IBiomeProvider`（Stage C，可选，只读） |
| 输入（调用期） | `VoxelCoordinate chunkCoordinate`（唯一参数，`IWorldGenerator` 契约） |
| 输出 | **一个游离的、尚未安装的** `VoxelChunk`（世界真值数据的初始状态） |
| 属性（只读暴露） | `Settings`（契约要求，返回 `WorldGenerationSettings`）<br>`Geology`（`GeologySettings`）<br>`Deposits` / `Biomes`（provider 引用，供外部只读查询） |

### 1.3 依赖与所有权

| 允许依赖 | 禁止依赖 |
|---|---|
| `WorldGenerationSettings` / `HeightFunction`（Stage 1 定义） | `BasicTerrainGenerator`（不调用、不继承、不复制源码） |
| `GeologySettings` / `GeologyNoise` / `GeologySample` / `GeologySampler`（Stage A/B） | `VoxelWorld`（**不持有、不查询**世界；生成器没有安装/注册能力） |
| `IDepositProvider` / `IBiomeProvider`（Stage C，只读） | Runtime / Streaming（`VoxelRuntimeCore` / `ChunkStreamingDriver` / `ChunkRecord` …） |
| `VoxelChunk` / `VoxelCoordinate` / `VoxelType`（VoxelCore，写入数据） | Mesh / View（`VoxelMeshGenerator` / `VoxelMeshData` / `VoxelChunkView` …） |
| 纯 C#（`System`） | UnityEngine / UnityEditor、Entity / 预制体 / 实例化 API |

**所有权**：`GeologyWorldGenerator` 拥有它**返回**的 `VoxelChunk`（游离对象）；
该对象在被 Runtime Core 搬运进世界之后即由 `VoxelWorld` 拥有（`Runtime_Core_Implementation.md` §5.6）。

### 1.4 术语澄清（"不创建 Chunk"的含义）

本文档与任务里的"**不创建 Chunk**"一律指：

> **不创建"世界拥有的"Chunk** —— 即不调用 `VoxelWorld` 的任何 API（`GetOrCreateChunk` / `SetVoxel` / `RemoveChunk` …），
> 也不安装、不注册。

`IWorldGenerator.Generate` 的契约要求返回一个 `VoxelChunk`，因此生成器**必然**构造一个**游离**的
`VoxelChunk` 对象（与既有 `BasicTerrainGenerator` 完全一致）；
它属于"输出数据的载体"，不属于"世界状态"。

---

## 2. Generate 生命周期

### 2.1 固定链路

```
Generate(chunkCoordinate)
   ↓
GenerateVoxelData(...)      逐体素填充（世界坐标采样 + 判定链）
   ↓
Validate(...)               生成器自检（输出必须自洽）
   ↓
Install(...)                ← **Runtime Core 负责**（生成器不参与）
   ↓
Register(...)               ← **Runtime Core 负责**（生成器不参与）
```

顺序**固定、不可交换、不可跳过**（`GenerateVoxelData` → `Validate` 是生成器内部的强顺序；
`Install` → `Register` 是调用方的强顺序）。

### 2.2 逐步职责与所有者

| 步骤 | 所有者 | 职责 | 失败处理 |
|---|---|---|---|
| `Generate` | **生成器** | `IWorldGenerator` 契约入口：校验坐标 → 分配游离 Chunk → 调用 `GenerateVoxelData` → 调用 `Validate` → 返回 | 抛出异常（禁止返回半成品/非法 Chunk） |
| `GenerateVoxelData` | **生成器（私有）** | 按 `localX → localY → localZ` 逐体素：取世界坐标 → 调 `GeologySampler.Sample` → 写 `VoxelChunk.SetVoxel` | 异常向上传播（`Generate` 抛出） |
| `Validate` | **生成器（私有）** | 输出自检：`Coordinate == 请求坐标`、4096 个类型均为合法值、结果自洽（见 §2.4） | 返回 false ⇒ `Generate` 抛出异常（不带非法输出返回） |
| `Install` | **Runtime Core** | 把游离 Chunk 的体素搬运进世界拥有的 Chunk（`GetOrCreateChunk` + 逐体素 `SetVoxel` + 叠加改动集 + 收尾置脏） | 由 Core 判定为 `Failed`（保留 `NeedsReinstall`） |
| `Register` | **Runtime Core** | 把记录插入权威注册表（状态 `Dirty`），之后进入构建/呈递 | 由 Core 判定（`DuplicateRegistration` 计数） |

> 该分工与 `Runtime_Core_Implementation.md` §5.1/§5.2 完全一致：**生成器只产出数据，安装与注册永远属于 Core。**

### 2.3 Generate 阶段禁令（硬性）

`Generate`（含 `GenerateVoxelData` / `Validate`）**禁止**：

| # | 禁止 | 说明 / 判据 |
|---|---|---|
| **P1** | **不写世界** | 不出现 `VoxelWorld` / `SetVoxel`（对世界）/ `RemoveChunk` / `Clear`；生成器**不持有**世界引用（结构性保证） |
| **P2** | **不创建"世界拥有的"Chunk** | 不调用 `GetOrCreateChunk`；只构造游离 `VoxelChunk`（§1.4） |
| **P3** | **不接触 Runtime** | 不出现 `VoxelRuntimeCore` / `ChunkStreamingDriver` / `ChunkRecord` / `IChunkPresenter` / `IChunkMesher`；不请求加载、不触碰脏标记（`MarkDirty` / `ClearDirty`） |
| **P4** | **不创建 Entity** | 不出现 `CrystalCluster` / `Entity` / `Prefab` / `Instantiate` / `GameObject`；不写"资源标记"进体素 |
| **P5** | **不生成几何** | 不出现 `VoxelMeshGenerator` / `VoxelMeshData`；生成阶段只读写体素数据 |
| **P6** | **不依赖时间/随机** | 不出现 `Time` / `DateTime` / `Random` / `Guid` / `Mathf.PerlinNoise`；噪声只来自 `GeologyNoise` |
| **P7** | **不做隐藏全局状态** | 无可变静态字段；所有参数显式构造注入 |

### 2.4 Validate 的具体检查项（生成器自检）

对**输出 Chunk** 逐项断言（任一失败 ⇒ `Generate` 抛出异常）：

1. `chunk.Coordinate == chunkCoordinate`（坐标自洽；Core 也会复核，双重保险）；
2. 抽样/全量校验 4096 个体素的 `Type ∈ {Air, Grass, Dirt, Stone}`
   （`VoxelType` 集合冻结为 4 值 —— `VoxelWorld_Core.md` §2）；
3. 每个体素的判定链可解释（`GeologySampler.ExplainAt` 与 `Sample` 一致 —— 由 Stage B 保证，
   生成器只在**诊断模式**下抽查，不进入热路径）；
4. 该 Chunk 的 `IsDirty == true`（新建 Chunk 的初始语义：尚未安装/构建过）。

**禁止**：Validate 通过后仍返回"部分填充"的 Chunk；也禁止用"修数据"的方式让 Validate 通过
（例如把非法类型改成 Stone）—— 非法输出必须**暴露**，不得掩盖。

---

## 3. 世界坐标生成

### 3.1 输入：只有世界坐标 + seed

规则计算**只能**使用：

```
(worldX, worldY, worldZ, seed) + 显式参数（WorldGenerationSettings / GeologySettings / providers）
```

* `seed` 取自 `WorldGenerationSettings.Seed`（构造期注入），并原样传给 `GeologySampler.Sample`；
* 世界坐标必须经 **VoxelCore 换算**得到：`chunk.LocalToWorldCoordinate(localX, localY, localZ)`
  —— **不得**自实现坐标规则（`Mining.md` §1.1 的同级要求）；
* 采样入口只有一个：`GeologySampler.Sample(worldX, worldY, worldZ, seed)`
  （Stage B 的**唯一判定链**；生成器不得另写一套 P0–P3 判定）。

### 3.2 禁止：Chunk 坐标参与规则计算

* `chunkX / chunkY / chunkZ` **不得**进入任何规则计算（不得作为噪声输入、不得作为哈希输入、
  不得用于"每个 Chunk 一套参数"）；
* Chunk 坐标**只允许**用于两件事：
  1. 决定**写入哪个 Chunk 的哪个局部坐标**（`chunkCoordinate` → 局部循环索引 → `LocalToWorldCoordinate`）；
  2. 作为输出 Chunk 的 `Coordinate` 字段（契约要求）；
* **禁止**任何"按 Chunk 编号扰动"的写法（例如 `hash(chunkX, chunkY, chunkZ)` 参与噪声种子）。

**可验证判据**：同一世界坐标在任意 Chunk 生成流程中都必须得到同一体素
（`WorldGeneration_Geology_Implementation.md` §3.3 / GIV2；本文档 GW3/GW4）。

### 3.3 写入顺序：固定 `localX → localY → localZ`

```
for localX in 0..15
  for localY in 0..15
    for localZ in 0..15
       world = chunk.LocalToWorldCoordinate(localX, localY, localZ)
       sample = sampler.Sample(world.x, world.y, world.z, seed)
       type = sample.IsSolid ? sample.Material : VoxelType.Air
       chunk.SetVoxel(localX, localY, localZ, type)     ← 参数顺序与循环顺序一致
```

* 固定顺序的理由与要求同 `Runtime_Core_Implementation.md` §5.3-R2：可复现、可断点比较、与既有阶段一致；
* **不得**并行、乱序、分帧（调度与分帧属于 Runtime Core）；
* **允许**跳过 `Air` 写入（新建 Chunk 必为空 ⇒ 结果等价），但**不得**让该优化影响任何其它逻辑
  （不得据此决定是否置脏、是否继续循环、是否改变判定）。

### 3.4 写入 API 与边界

| 规则 | 内容 |
|---|---|
| 唯一写入口 | `VoxelChunk.SetVoxel(localX, localY, localZ, type)`；不得反射/绕过 |
| 局部坐标范围 | `[0, ChunkSize-1]`（由 VoxelCore 保证越界抛异常；生成器不得触发越界） |
| 世界之外 | `y < 0` 或 `y ≥ WorldHeight` ⇒ 由 P0 判空（`Air`），**不得**写出实体 |
| 垂直范围 | 与 Runtime 的 `VerticalChunkCount = ceil(WorldHeight / 16)` 一致：**世界高度之上永远是 Air** |
| 类型取值 | 只能是 `Air / Grass / Dirt / Stone`（V1 不新增体素类型） |

---

## 4. 地质判定链

### 4.1 固定优先级（唯一实现）

```
P0 WorldBounds   →  y < 0 或 y ≥ WorldHeight            ⇒ Empty（无条件）
        ↓
P1 Cave          →  CaveDensity ≥ CaveThreshold          ⇒ Empty（洞穴挖空密度）
        ↓
P2 Terrain Layer →  y > SurfaceHeight                    ⇒ Empty；否则实体
        ↓
P3 Material      →  Surface / Depth / Deep ⇒ Grass / Dirt / Stone
```

* 判定链**只有一份实现**：`GeologySampler.Resolve(...)`（Stage B）。
  生成器**不得**重复实现、不得部分内联、不得改写顺序；
* `SurfaceHeight` 复用 Stage 1 的**高度场定义** `HeightFunction`（`WorldGeneration_Core.md` §4）；
* 判定链与外层循环的关系：**外层循环只决定坐标，内层判定只决定该坐标的结果**。

### 4.2 Cave 优先于 Material

* 洞穴（P1）**优先于**任何实体/材料判定（P2/P3）：被洞穴命中的坐标 ⇒ `Empty`（`Air`），
  即使它位于 Deep 层（或未来矿带）内部；
* 洞穴**只改变密度**：它不产生几何、不产生装饰、不产生 Entity；
  洞壁面由 Meshing 按"邻居为 Air 则生成面"自然产生（`VoxelWorld_Mesh.md` §1.2）；
* 洞穴不影响 P0：世界之外恒空（P0 具体优先，不参与洞穴判定）。

### 4.3 Deposit / Biome 的边界（必须精确区分）

| 层 | 是否改变 Voxel | 允许的影响 |
|---|---|---|
| **Deposit** | **完全不改变** | 只是"这里存在资源"的**属性查询**（`IDepositProvider`）；不写资源体素、不改密度、不改材料 |
| **Biome** | **只允许改变"地表材料选择"** | 见 §6：仅 `y == SurfaceHeight` 那一层的材料可在 `{Grass, Dirt, Stone}` 内选择；**不得**新增体素类型、不得生成结构/植被/Entity、不得改变密度分布（实体/空的分界不变） |

> **术语澄清（消除任务描述中的表观张力）**：任务 §4 的"Deposit/Biome 只采样、不改变 Voxel"与
> 任务 §6 的"Biome 影响地表材料选择"指的是同一件事的两面 ——
> **Deposit 一律不改变 Voxel**；**Biome 只在地表材料这一处生效**（且取值被限制在 V1 的 4 种体素状态内）。
> 任何超出"地表材料"的 Biome 影响（高度修正、土壤深度修正、结构）都属于**后续显式扩展**，
> 必须先在 `WorldGeneration_Geology.md` 与本文档中登记（见附录 B）。

---

## 5. Deposit 接入

### 5.1 允许

* `GeologyWorldGenerator` **可以持有** `IDepositProvider`（构造注入，可为 `null`）；
* **只能读取**：调用 `TryGetDeposit(...)` 是唯一允许的交互；接口本身只有查询方法（Stage C 已保证）；
* 通过只读属性暴露：`public IDepositProvider Deposits { get; }`，供未来的 Entity / 玩法层查询
  （"这里存在什么资源"）。

### 5.2 禁止

| # | 禁止 |
|---|---|
| **D1** | 生成 `CrystalCluster`（晶簇）—— 它是 Entity，不属于 Voxel / Chunk（ADR-001） |
| **D2** | 生成任何 Entity / 矿石节点 / 掉落物 |
| **D3** | `Instantiate` / `Prefab` / `GameObject` 等实例化或 Unity 对象操作 |
| **D4** | 把资源信息**写入体素**（没有"矿石体素"、没有资源标记位） |
| **D5** | 让 Deposit 影响 `Density` / `Material` / `SurfaceHeight`（GIV5 / GW5） |
| **D6** | 在生成热路径中依赖 provider 的返回值（生成输出必须与 Deposit 设置**无关**） |

> **结论（可断言）**：改变 `EnableDeposits` / `DepositRichness` / provider 是否存在，
> `GeologyWorldGenerator` 的输出必须**逐体素完全一致**（0 差异），同时 provider 仍能查询到资源。

---

## 6. Biome 接入

### 6.1 Biome 影响什么

| # | 影响 | V1 边界 |
|---|---|---|
| **B1** | **地表材料选择** | 只作用于 `y == SurfaceHeight` 的那一层体素；取值只能是 `{Grass, Dirt, Stone}`（V1 的 4 种体素状态内，`Air` 不作为地表材料） |
| **B2** | **植被条件接口** | 只提供**环境条件**（`BiomeSample.Temperature / Moisture` 与 `Biome` 标识）；**不生成植被**、不生成树/草/动物、不生成任何 Entity；条件的消费者是未来 Entity / Vegetation 系统 |

**V1 的显式地表材料映射（唯一允许的形式，必须写成显式表，不得隐式推导）**：

| 条件 | 地表材料 |
|---|---|
| `EnableBiomeModifiers == false` 或 `biomeProvider == null` | `Grass`（= Stage 1 行为，见 §7） |
| `Biome == None`（群系判定被关闭） | `Grass` |
| `Biome == BiomeA`（占位：冷干） | `Stone`（裸岩：无植被覆盖的地表） |
| `Biome == BiomeB`（占位：暖湿） | `Grass` |

> 该映射是 V1 的唯一群系影响：它**完全在既有体素类型内**、**不改变密度分布**（地表位置不变）、
> 且让"Biome 真的生效"成为**可验证**的（GW6：差异集合 ⊆ {地表层}）。

### 6.2 Biome 不影响什么（硬性）

| 不影响 | 说明 |
|---|---|
| **Runtime** | 不改变加载/卸载、Chunk 生命周期、脏标记、预算、并发（`Runtime_Interface.md` §1：Biome 不出现在任何调度接口中） |
| **Chunk** | 不改变 Chunk 划分、坐标语义、Chunk 独立性（Chunk 仍只是采样窗口） |
| **Mesh** | 不产生几何、不改变面/材质/子网格语义（`VoxelWorld_Mesh.md`） |
| **密度分布** | 不改变 `SurfaceHeight`、不改变实体/空的分界（V1；高度/土壤修正属后续显式扩展） |
| **Entity 生成** | 不生成任何 Entity（`Tree` / `Plant` / `Animal` / 结构） |

### 6.3 确定性

* `IBiomeProvider.SampleBiome` 是世界坐标纯函数（Stage C 已保证）；
* 生成器对每个**地表体素**调用一次（或按列缓存一次 —— 缓存必须由世界坐标索引且不改变输出）；
* 同一 `(seed, 参数, 坐标)` ⇒ 同一材料（GW1/GW3）。

---

## 7. 与旧生成器兼容（GIV9）

### 7.1 必须实现的不变量

```
EnableCaves = false
EnableDeposits = false
EnableBiomeModifiers = false（或 biomeProvider == null）

⇒ GeologyWorldGenerator 的输出  ==  BasicTerrainGenerator 的输出（同 seed、同参数、同坐标）
   逐体素一致（4096 个体素全部相等）
```

### 7.2 为什么这是可满足的（实现前提）

| 组成 | 来源 | 说明 |
|---|---|---|
| `SurfaceHeight` | **`HeightFunction`**（Stage 1 的高度场定义） | 复用同一函数 ⇒ 高度分布完全一致 |
| P0 / P2 / P3 判定链 | **`GeologySampler` 独立表达**（不调用 `ClassifyVoxel`、不复制源码） | 与 `BasicTerrainGenerator.ClassifyVoxel` 逐条等价（`WorldGeneration_Geology.md` §3.3） |
| P3 的三段材料 | 同一规则：`y==h ⇒ Grass`、`h-SoilDepth<y<h ⇒ Dirt`、其余 ⇒ `Stone` | 与 Stage 1 一致 |
| P1 Cave | 关闭（`EnableCaves = false`） | 不参与 |
| Deposit / Biome | 关闭 / provider 为 null | 不参与 |

### 7.3 反例保护（防止"恒真断言"）

* 必须能证明 GIV9 **不是因为断言写错**：
  启用洞穴（`EnableCaves = true`，阈值合理）后，输出必须与 Stage 1 基线**产生差异**，
  且差异集合**只含 `Solid → Empty`**（不得把空变实体、不得改变材料种类）；
* 新增的体素类型、新的密度规则一律禁止（否则 GIV9 不可能成立）。

---

## 8. 验收（GW1–GW10）

> 全部由**新增**的 `Assets/Editor/GeologyStageDValidation.cs` 实现（批处理入口
> `Tools/VoxelWorld/Validate Geology Stage D` + `-executeMethod`）；
> 纯 C# 部分不依赖场景；既有 8 个入口与 6 个经典入口由外部编排逐个运行。

| # | 不变量 | 验证方法 |
|---|---|---|
| **GW1** | **同 seed 同 chunk 一致**：同 `(Seed, 参数, 坐标)` ⇒ 逐体素一致 | 两次生成（含两个独立实例、交错生成其它 Chunk、逆序批量生成）⇒ 全量 4096 比较 + `ComputeContentHash()` 相等；反例保护：断言该 Chunk 非全 Air |
| **GW2** | **GIV9 与旧生成器等价**：全部地质特性关闭 ⇒ 与 `BasicTerrainGenerator` 逐体素一致 | 对多个 Chunk（含负坐标、`y=0/顶层`、`SoilDepth=0` 边界）比较 4096×N 体素；**反例保护**：启用洞穴必须产生差异（且只含 `Solid→Empty`） |
| **GW3** | **不同 chunk 顺序一致**（Chunk 独立计算） | 以多种顺序/交错/重复生成同一批 Chunk ⇒ 每个坐标结果不变；生成器不读取任何其它 Chunk 的数据（结构性 + 行为双重验证） |
| **GW4** | **跨 Chunk 缝一致**：缝处坐标严格相邻，两侧体素都由同一世界函数解释 | 缝对（`x=15→16`、`x=-1→0`、`31→32`）取 16×16 列：断言坐标相邻（无缝隙/无重叠）+ 两侧 `Voxel == GeologySampler` 判定；洞穴噪声相邻差分 ≤ 显式上界 |
| **GW5** | **Deposit 不改变 Voxel**：仅改矿藏设置/provider ⇒ 输出逐体素一致（0 差异） | 两套设置（`EnableDeposits` 开/关、`DepositRichness` 0/100、provider 有/无）生成同一 Chunk ⇒ 全量比较；同时断言 provider 能查询到资源（非恒真） |
| **GW6** | **Biome 影响有界**：开启群系后差异**只允许**出现在地表层 | 开/关 Biome 的两份输出比较：断言差异集合 ⊆ `{y == SurfaceHeight 的体素}`、差异只发生在 `{Grass ↔ Stone}` 之间、实体/空的分界完全不变（`IsSolid` 逐体素相同） |
| **GW7** | **Biome 不生成实体** | 结构扫描：生成器与 Biome 相关源码不出现 `Tree`/`Plant`/`Animal`/`Vegetation`/`Entity`/`Prefab`/`Instantiate`/`GameObject`；运行期：多次生成后 GameObject 计数不变 |
| **GW8** | **Generate 失败不污染世界** | ① 结构性：生成器**不持有** `VoxelWorld`、无世界写入路径 ⇒ 失败不可能污染世界；② 输出契约：多坐标 × 全量类型合法 + `Coordinate` 自洽 + 自检失败即抛异常；③ 端到端：引用 Runtime Core 的既有验证（生成失败 ⇒ 世界 `ChunkCount == 0`、记录 `Failed + NeedsReinstall`、之后可成功重试） |
| **GW9** | **Runtime Core 无需修改**（无感替换） | ① `Runtime/*.cs` 与 `IWorldGenerator.cs` **SHA256 不变**；② 反射：`GeologyWorldGenerator` 实现 `IWorldGenerator`、`Settings` 属性类型为 `WorldGenerationSettings`；③ 端到端：用新生成器跑一次 Core 闭环（请求 → `Tick` → `Ready`）成功，且 Core 无任何改动 |
| **GW10** | **`BasicTerrainGenerator` SHA 不变** + 既有入口全绿 + 结构纪律 | ① 文件哈希比对（含 `BasicTerrainGenerator` / `IWorldGenerator` / VoxelCore / Mesh / View / Runtime / Streaming / Bootstrap）；② 既有 8 个地质/核心入口由外部编排全 PASS；③ 目录扫描：无 UnityEngine / Chunk 坐标参与规则 / Runtime / Entity / 静态可变字段 |

**验收通过条件**：GW1–GW10 全绿 + 既有 8 个入口全 PASS + 零未授权文件改动。

---

## 附录 A：类型与构造草案（Stage D 落地依据）

```csharp
namespace VoxelWorld.Generation.Geology
{
    /// 地质世界生成器：IWorldGenerator 的新实现（不修改抽象、不修改旧实现）
    public sealed class GeologyWorldGenerator : IWorldGenerator
    {
        public GeologyWorldGenerator(
            WorldGenerationSettings stage1Settings,   // 既有类型（Stage 1 参数，含 Seed）
            GeologySettings geologySettings,          // Stage A 地质参数
            IBiomeProvider biomeProvider,             // Stage C，可为 null（⇒ 恒等，等价 Stage 1）
            IDepositProvider depositProvider);        // Stage C，可为 null（⇒ 无资源查询）

        // 契约（签名与既有实现完全一致）
        public WorldGenerationSettings Settings { get; }

        // 只读暴露（诊断 / 外部只读查询）
        public GeologySettings Geology { get; }
        public GeologySampler Sampler { get; }
        public IBiomeProvider Biomes { get; }
        public IDepositProvider Deposits { get; }

        /// 生成一个游离 Chunk：localX → localY → localZ（世界坐标经 LocalToWorldCoordinate 换算）
        public VoxelChunk Generate(VoxelCoordinate chunkCoordinate);

        // ---- 内部（固定命名，便于结构扫描与正向对照）----
        private void GenerateVoxelData(VoxelChunk chunk, int seed);
        private bool Validate(VoxelChunk chunk, VoxelCoordinate chunkCoordinate, out string reason);
        private VoxelType ResolveSurfaceMaterial(int worldX, int worldY, int worldZ, int seed,
                                                 GeologySample sample);   // Biome 只在此处生效
    }
}
```

**附录 A 禁令**：以上类型与实现中不得出现
`BasicTerrainGenerator` / `ClassifyVoxel` / `VoxelWorld` / `GetOrCreateChunk` / `RemoveChunk` /
`MarkDirty` / `ClearDirty` / `VoxelRuntimeCore` / `ChunkStreamingDriver` / `VoxelMeshGenerator` /
`VoxelMeshData` / `VoxelChunkView` / `UnityEngine` / `Random` / `Time` / `DateTime` /
`Entity` / `CrystalCluster` / `Prefab` / `Instantiate` / `GameObject`。

## 附录 B：与已有规范一致性映射

| # | 规范 | 本文档的处理 | 冲突 |
|---|---|---|---|
| 1 | `WorldGeneration_Geology.md` | P0–P3 优先级（§3.2）、地层（§4）、洞穴（§5）、矿藏（§6.1）、群系（§7）逐条沿用；**§7 的 `TerrainModifier`（高度/土壤修正）在 V1 不实现**，登记为后续显式扩展（§6.1 已声明） | 否（收窄） |
| 2 | `WorldGeneration_Geology_Implementation.md` | 本文档 = §13.1 **Stage D** 的实现细化；§12.1 禁令清单、§12.3"等价而非复用"、GIV1–GIV9 全部继承并细化为 GW1–GW10 | 否 |
| 3 | `WorldGeneration_Core.md` | 只实现 `IWorldGenerator`；输入 Seed + ChunkCoordinate；Chunk 可独立计算（§6）；确定性（§2）；不覆盖改动（§9）；不要求改 VoxelCore（§10） | 否 |
| 4 | `VoxelWorld_Core.md` | 不新增 `VoxelType`（仍 4 值）；坐标经 VoxelCore 换算；只经 `SetVoxel` 写入游离 Chunk | 否 |
| 5 | `VoxelWorld_Runtime_Interface.md` | `IWorldGenerator` **不改名/不重复定义/不改签名**；新实现属于 Generation 实现侧；组装点换一个 `new` 即完成替换 | 否 |
| 6 | `VoxelWorld_Runtime_Core_Implementation.md` | 四步链路的所有权划分与 §5.1/§5.2 一致（生成器只产出数据；Install/Register 属 Core）；写入顺序 `x→y→z` 与 §5.3-R2 一致；`Validate` 为"生成器自检 + Core 防御"双重保险 | 否 |
| 7 | `VoxelWorld_Runtime.md` | 调度与加载判定与生成器无关；`y ≥ WorldHeight` 恒空与垂直范围一致 | 否 |
| 8 | `VoxelWorld_Mesh.md` | 生成阶段不产几何；洞穴/地层的面由 Meshing 派生 | 否 |
| 9 | `VoxelWorld_Mining.md` | 生成与挖掘来源不同、结果同型（Air）；生成不覆盖改动 | 否 |
| 10 | `VoxelWorld_View.md` | 表现层与地质无关 | 否 |
| 11 | `ADR-001` | 晶簇/植被等对象归 Entity，不属于 Chunk 所有权；生成器只提供只读查询接口 | 否 |
| 12 | Stage A/B/C 既有实现 | 本文档**不修改**它们（`GeologySettings` / `GeologyNoise` / `GeologySample` / `GeologySampler` / 4 个 provider 文件 SHA 不变）；生成器只**组合**它们 | 否 |

**术语澄清登记**（消除任务描述中的两处表观张力，均已在正文解释）：

* §1.4：「不创建 Chunk」= 不创建**世界拥有的** Chunk（游离 Chunk 是 `IWorldGenerator` 契约的必需输出）；
* §4.3：`Deposit` 一律不改变 Voxel；`Biome` **只在地表材料这一处**生效（取值限定在 V1 的 4 种体素状态内）。

## 附录 C：落地清单与顺序（Stage D，本轮不执行）

| 步骤 | 内容 | 完成判据 |
|---|---|---|
| D-1 | 新增 `Assets/Scripts/VoxelWorld/Generation/Geology/GeologyWorldGenerator.cs`（本文档 §2–§6） | 编译通过；GW1–GW8 绿 |
| D-2 | 新增 `Assets/Editor/GeologyStageDValidation.cs`（GW1–GW10） | 批处理 PASS |
| D-3 | 跑既有 8 个入口 + 6 个经典入口 | 全部 PASS（GW9/GW10） |
| D-4 | （可选、独立）组装点试验：把 `BasicTerrainGenerator` 换成 `GeologyWorldGenerator` 跑一次 Runtime 闭环 | Core 零修改、闭环到 `Ready` |
| D-5 | （独立）Stage F：Bootstrp/玩法层显式选择新生成器 | 仅改组装点；V1 默认仍用旧实现 |

**Stage D 不得做**：修改 VoxelCore / `BasicTerrainGenerator` / `IWorldGenerator` / WorldGenerationSettings /
HeightFunction / Runtime / Streaming / Mesh / View / Bootstrap；创建 Entity；新增 `VoxelType`。
