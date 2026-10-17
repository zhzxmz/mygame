# World Generation Geology Specification V1

> 适用范围：`Assets/Scripts/VoxelWorld/Generation/`（世界规则层）及其未来的地质实现。
> 本规范定义 **VoxelWorld 的连续世界地质规则**：一个坐标"为什么"是这种地形、这种材料、有没有洞、有没有矿。
>
> **表示层边界**：这里的“连续”指生成规则以世界坐标纯函数表达，并不意味着本规范直接生成 Marching Cubes 的连续 Density Mesh。此规范的接入目标仍是离散 `VoxelWorld`：采样结果最终分类为 `Air / Grass / Dirt / Stone` 并写入 `VoxelChunk`。它与 `DensityTerrain` 的 `DensityField` / `MaterialField` 体系是不同的实现路径；不可将二者的真值、编辑状态或 Mesh 契约混为一谈。
>
> **本文档只定义"世界是什么"，不定义"如何加载世界"。**
> * Generation 决定：**某个坐标为什么是这个地形**（世界规则）；
> * Runtime / Streaming 决定：**什么时候加载、加载哪些 Chunk**（调度）。
>
> 上游规范（本文档不得与其冲突）：
> * `Specifications/World/VoxelWorld_Core.md` —— 世界真值（Voxel Data）、体素状态集合、Chunk 数据契约；
> * `Specifications/World/WorldGeneration_Core.md` —— 生成契约（Seed + ChunkCoordinate → VoxelChunk、确定性、Stage 划分）；
> * `Specifications/World/VoxelWorld_Runtime_Core.md` —— 调度核心（加载 / 生成调用 / 安装 / 脏 / 重建）；
> * `Specifications/World/VoxelWorld_Runtime_Interface.md` —— 抽象层与依赖倒置（`IWorldGenerator` 由 Core 依赖、由实现实现）；
> * `Specifications/ArchitectureDecisionWorldSpaceEntity.txt`（ADR-001）—— World / Space / Entity 的概念关系与边界。
>
> 冲突时的优先级：**真值** 以 `VoxelWorld_Core.md` 为准；**生成契约与 stage 划分** 以 `WorldGeneration_Core.md` 为准；
> **调度** 以 `VoxelWorld_Runtime*.md` 为准；**具体地质规则（层 / 洞 / 矿 / 群系）** 以本文档为准。
>
> **零修改前提（硬性）**：本规范的所有内容都必须能在**不修改** VoxelCore、WorldGeneration 既有实现、
> Mesh、View、Runtime Core、Streaming、Bootstrap 的前提下落地为**新增 stage / 新增生成器实现**。
> 任何"必须改 VoxelCore 才能实现"的地质想法，一律不得进入 V1（`WorldGeneration_Core.md` §10）。

---

## 1. 定位

### 1.1 本层是什么

**WorldGeneration_Geology 是世界规则层**：它把"世界长什么样"定义为一组**连续函数**，
并规定这些函数如何被采样成体素数据。

负责：

| 职责 | 内容 |
|---|---|
| **连续空间采样** | 定义 `Density(x,y,z)` / `Material(x,y,z)` 等**世界坐标的纯函数**；Chunk 只是采样窗口 |
| **地形密度** | 定义"哪里是实体、哪里是空"（基础地形 + 洞穴的覆盖关系） |
| **地层** | 定义层（Surface / Depth / Deep）的高度范围、材料、密度影响 |
| **洞穴** | 定义 `CaveDensity(x,y,z)` 及其对密度的覆盖规则 |
| **矿物区域** | 定义 `DepositZone(x,y,z)`：**哪里存在资源**（属性层，不是体素类型） |
| **生物群系接口** | 只定义接口（温度 / 湿度 / 地表材料 / 植被条件），不实现具体群系 |

### 1.2 本层不是什么

| 不负责 | 归属 |
|---|---|
| Chunk 生命周期（加载 / 生成 / 脏 / 重建 / 卸载） | `VoxelWorld_Runtime_Core.md` |
| Streaming（玩家位置 → 请求集合 → 卸载半径） | `VoxelWorld_Runtime.md` §6 + `Runtime/Streaming/` |
| Mesh 生成 | `VoxelWorld_Mesh.md` |
| Unity 对象（GameObject / Mesh / Collider / Transform） | `VoxelWorld_View.md` |
| **Entity 生成**（晶簇、矿石节点、生物、建筑、树） | 未来 Entity 系统（ADR-001：Entity 不属于 Chunk 所有权体系） |
| 存档 / 编辑 / 挖掘 | `VoxelWorld_Mining.md` + 存档层 |
| 玩法规则（掉落、工具、耐久） | 玩法层 |

### 1.3 一句话分工

```
Generation 拥有"世界规则"（某个坐标为什么是这个地形）
Runtime    只有"调度权"（什么时候算、算哪些、谁来呈递）
```

---

## 2. 连续世界模型

### 2.1 世界是连续函数，不是 Chunk 集合

```
世界 = 定义在整个世界坐标空间上的纯函数组：

    Density(x, y, z)      → Empty | Solid          （是否实体）
    Material(x, y, z)     → V1 体素状态之一         （实体时是哪种体素）
    CaveDensity(x, y, z)  → 标量（洞穴强度，参与 Density 的覆盖判定）
    DepositZone(x, y, z)  → ZoneId（矿物区域归属，属性层）
    Biome(x, z)           → BiomeId（群系归属，可修正地表规则）

Chunk = 该函数组在某一区域的**采样窗口**：
    world = chunkCoordinate * ChunkSize + local      （VoxelWorld_Core §3）
```

**Chunk 不是世界的一部分，而是观察世界的一种分辨率/窗口**（ADR-001：Chunk 是 Space 的空间分区与管理单位，
Terrain 属于 Space 的空间数据而不是 Chunk 本身）。

因此：

1. 任意世界坐标的结果**只由该坐标（+ Seed + 参数）决定**；
2. Chunk 的边界与世界性质无关（世界的连续性与 Chunk 划分无关）；
3. 体素数据是这些函数**在网格上的采样结果**，不是"每个 Chunk 自己长出来的东西"。

### 2.2 确定性（本模型的第一性要求）

同一个 `Seed + WorldGenerationSettings + 世界坐标` ⇒ **任何时候、任何顺序、任何机器状态**下结果完全一致
（`WorldGeneration_Core.md` §2）。

### 2.3 禁止事项

| # | 禁止 | 理由 |
|---|---|---|
| **F1** | 依据 **Chunk 编号**（或 Chunk 内局部坐标）注入随机性 | 违反"世界坐标纯函数"；会让同一世界坐标在不同 Chunk 中结果不同 |
| **F2** | 相邻 Chunk **规则不同**（例如每个 Chunk 自带一套噪声参数/种子） | 会在 Chunk 边界产生断层、悬崖、材质突变 |
| **F3** | 采样结果依赖加载顺序、邻居 Chunk 是否已生成、帧序、时间 | 违反确定性；同一世界会随加载路径变化 |
| **F4** | 用 `UnityEngine.Random` / `System.Random` / `DateTime` / `Time` 生成地形 | 非确定性来源（§8） |

> **术语澄清（与 `WorldGeneration_Core.md` §6 不冲突）**
> 上游 §6「Chunk Independence」指的是 **计算独立**：`GenerateChunk(c)` 可以在不考虑邻居、任意顺序下求值，
> 且结果相同 —— 这是**必须保留**的（Streaming 与 Meshing 的 B1 边界规则都依赖它）。
> 本规范禁止的是 **规则独立**：每个 Chunk 拥有自己的生成规则/随机种子。
> 两者关系：**规则只有一份（本规范），计算可以独立（上游 §6）**。

---

## 3. 地形密度模型

### 3.1 密度的组成

`Density` 由三层叠加判定（自外向内）：

| 组成 | 作用 | 判定量 |
|---|---|---|
| **WorldBounds** | 世界之外的区域恒为空 | `y < 0` 或 `y ≥ WorldHeight` |
| **Cave** | 在实体区域中"挖空" | `CaveDensity(x,y,z) ≥ CaveThreshold` |
| **Base Terrain** | 决定地表高度与实体/空的分界 | `SurfaceHeight(x,z)` |
| **Underground** | 决定地表之下的材料分带 | 相对 `SurfaceHeight` 与 `SoilDepth` |

### 3.2 密度优先级（唯一的总覆盖顺序）

**优先级从高到低（高者覆盖低者）**：

```
P0  WorldBounds   ：y < 0 或 y ≥ WorldHeight                    ⇒ Empty（无条件）
P1  Cave          ：CaveDensity(x,y,z) ≥ CaveThreshold          ⇒ Empty（洞穴可以覆盖岩石）
P2  Terrain       ：y > SurfaceHeight(x,z)                      ⇒ Empty
P3  Material      ：y ≤ SurfaceHeight(x,z)                       ⇒ Solid + 地层材料（§4）
──  属性层（不参与实体性判定）──
A1  DepositZone   ：附加"此处存在资源"的属性                      （不改变 P0–P3 的结果）
A2  Biome         ：可修正 SurfaceHeight / SoilDepth / 地表材料    （通过 P2/P3 生效）
```

**硬性条款**

1. **Cave > Rock**：洞穴优先级高于任何实体判定（含 Deep 层与矿带）；
2. **属性不改密度**：`DepositZone` **绝不**改变"是不是实体"（V1：矿藏不是体素类型，§6）；
3. **世界之外恒空**：`y < 0` 或 `y ≥ WorldHeight` 永远 `Empty`
   （保证与 `WorldGeneration_Core.md` §4 的 `y ≥ WorldHeight → Air`、
   与 Runtime 的垂直范围 `VerticalChunkCount = ceil(WorldHeight/16)` 一致）；
4. **Biome 只通过 P2/P3 生效**：它修正地形参数与地表材料，不得直接改写 P0/P1 的结论。

### 3.3 与既有 Stage 1 实现的关系（逐条对应，不修改它）

本规范把既有 `BasicTerrainGenerator.ClassifyVoxel(worldY, terrainHeight, settings)` 的规则
**提升为正式的密度模型表述**，二者逐条一致：

| 本规范 | 既有实现（Stage 1） |
|---|---|
| P0 `y ≥ WorldHeight ⇒ Empty` | `if (worldY >= settings.WorldHeight) return VoxelType.Air;` |
| P2 `y > SurfaceHeight ⇒ Empty` | `if (worldY > terrainHeight) return VoxelType.Air;` |
| P3 Surface（`y == SurfaceHeight`）⇒ Grass | `if (worldY == terrainHeight) return VoxelType.Grass;` |
| P3 Depth（`SurfaceHeight-SoilDepth < y < SurfaceHeight`）⇒ Dirt | `if (worldY > terrainHeight - settings.SoilDepth) return VoxelType.Dirt;` |
| P3 Deep（其余）⇒ Stone | `return VoxelType.Stone;` |
| P1 Cave | **Stage 2 新增**（本规范定义规则） |

> Stage 1 的实现保持不变；地质层以**新增 stage / 新增生成器实现**的方式叠加
> （`WorldGeneration_Core.md` §7：每个 stage 接收上一阶段的体素数据）。

---

## 4. 地层系统

### 4.1 层的定义（规则，不绑定数值）

每层必须显式定义三项：**高度范围**（相对地表）、**材料**、**密度影响**。

| 层 | 高度范围（相对 `SurfaceHeight`） | 材料（V1 体素状态） | 密度影响 |
|---|---|---|---|
| **Above（地表之上）** | `y > SurfaceHeight` | `Air` | 空 |
| **Surface（地表层）** | `y == SurfaceHeight` | `Grass` | 实体 |
| **Depth（土壤层 / 地下）** | `SurfaceHeight - SoilDepth < y < SurfaceHeight` | `Dirt` | 实体 |
| **Deep（深层）** | `y ≤ SurfaceHeight - SoilDepth` | `Stone` | 实体 |
| **WorldAbove（世界之上）** | `y ≥ WorldHeight` | `Air` | 空（P0，无条件） |

规则：

1. **层边界由 `SurfaceHeight(x,z)` 与 `SoilDepth` 决定**，二者都是显式参数/Biome 可修正量；
2. **层的顺序固定**（Surface → Depth → Deep），只有"上一层不成立"时才询问下一层（唯一解释路径）；
3. `SoilDepth = 0` 时 Depth 层为空（Surface 直接落在 Deep 之上）；
4. 层的**具体数值不写进本规范**：`TerrainHeight` / `SoilDepth` / `WorldHeight` / 频率 / 振幅仍是显式参数
   （`WorldGenerationSettings`），未来的地质参数（洞穴阈值、矿带频率等）必须在**新增的显式参数结构**里声明（§9.4）。

### 4.2 Deep 的进一步分带（属性层，不新增体素类型）

真实的"深部岩性差异"（deep stone 等）在 V1 **不作为新的 `VoxelType`**：

* V1 的体素状态集合由 `VoxelWorld_Core.md` §2 冻结为 `Air / Grass / Dirt / Stone`；
* 深部分带（例如"更深处的岩层"）在 V1 表达为**地质属性**（可由 `DepositZone`/GeologyLayer 查询答案），
  其体素仍为 `Stone`；
* 若未来确实需要独立的矿物/岩性体素类型，必须**先在 VoxelCore 正规新增 `VoxelType`**，
  并同步升级本规范版本与全部不变量（V1 明令禁止直接构造非法枚举值 —— 见 §10.3 与 Runtime Core 的
  `TryValidateTypes` 越界检查）。

---

## 5. 洞穴系统

### 5.1 定义

```
CaveDensity(x, y, z) : 标量（世界坐标的纯函数；Seed 确定）

Cave(x, y, z) = CaveDensity(x, y, z) ≥ CaveThreshold
```

`CaveThreshold` 是显式参数（地质参数结构，§9.4）。

### 5.2 硬性规则

1. **连续性**：`CaveDensity` 必须是**连续噪声**的结果 —— 相邻世界坐标的取值变化有界（不得逐点随机跳变）；
2. **Seed 确定**：同 Seed + 同坐标 ⇒ 同值；
3. **Chunk 边界连续**：`CaveDensity` 只依赖世界坐标 ⇒ 相邻 Chunk 的边界列必然一致（可机械验证，§10.4-G4）；
4. **洞穴只改变 Density**：洞穴把 `Solid` 变成 `Empty`（写进 Voxel 的是 `Air`），
   **不生成 Mesh、不生成洞壁几何、不生成装饰**（`VoxelWorld_Mesh.md` §1：Mesh 从体素派生；洞穴的洞壁面
   由 Meshing 按"邻居为 Air 则生成面"的规则自然产生）；
5. **世界之外无洞穴**：`y < 0` 或 `y ≥ WorldHeight` 由 P0 直接判空，洞穴判定不参与（保持 P0 优先级）；
6. **洞穴不破坏地表规则的一致性**：允许洞穴穿透地表（形成洞口），但必须由同一条规则自然产生，
   不得为"特定坐标"写特例。

### 5.3 与挖掘的区别（不得互相替代）

| | 生成期洞穴 | 运行时挖掘 |
|---|---|---|
| 来源 | 本规范（Seed 决定的规则） | `VoxelWorld_Mining.md`（玩家编辑） |
| 结果 | 该坐标的体素 = `Air` | 该坐标的体素 = `Air` |
| 判定 | 由 `CaveDensity` 决定（可重复求值） | 由挖掘请求决定（改动集记录） |
| 允许 | ✅ | ✅ |

两者结果相同但**来源不同**：不得用"重新生成"实现挖掘，也不得把洞穴写进改动集
（`WorldGeneration_Core.md` §9：生成不覆盖玩家改动）。

---

## 6. 矿物系统

矿物分为**两层**，二者都属于不同系统，**不得混淆**。

### 6.1 第一层：Voxel Geological Deposit（体素地质矿藏）

**表示：哪里存在资源**（不是"哪里有矿物体素"）。

```
DepositZone(x, y, z) : ZoneId     例如 None / IronZone / CrystalZone / ...
```

规则：

1. **属性层**：`DepositZone` **不改变** `Density`/`Material`（V1 中矿藏不是体素类型）；
2. **连续 + Seed 确定**：与洞穴同级要求（连续噪声 / 纯函数 / Chunk 边界连续）；
3. **受地层约束**：区域必须由显式规则限定（例如"仅出现在 Deep 层"），
   不得在空气中"存在资源"（对 `Empty` 坐标查询必须返回 `None` 或被视为无效）；
4. **可查询**：任何系统都可以问"这个坐标存在什么资源"（只读、可重复求值、无需加载 Chunk 之外的数据）；
5. **不参与 Mesh**：矿藏不改变几何（V1 没有矿物体素 ⇒ 没有矿石纹理/独立面）。

### 6.2 第二层：Entity Spawn（实体生成）

**表示：裸露晶簇、矿石节点等"有自身状态的对象"如何出现。**

| 判定 | 归属 |
|---|---|
| "此处存在资源"（`DepositZone`） | **本规范 / Generation** |
| "此处是否适合生成一个晶簇对象"（暴露条件、数量、朝向、稀有度） | **未来 Entity 系统** |
| "晶簇对象本身的状态与生命周期" | **未来 Entity 系统** |
| "晶簇的渲染 / 碰撞 / 存档" | 未来 Entity 系统 + View/存档层 |

**明确：晶簇不属于 Voxel。**

* Voxel **只负责**回答"这里存在矿物资源"（`DepositZone`）；
* 晶簇、矿石节点、可拾取物都是 **Entity**：它们有自己的状态，通过**空间位置**与 Space 建立关系，
  **不属于 Chunk 的所有权体系**（ADR-001）；
* 因此 **Generation 不生成 Entity、不往 Voxel 里写"晶簇标记"、不占用体素类型**；
* Generation 为 Entity 系统提供的是**只读查询接口**（"资源存在性 + 暴露条件"），而不是生成行为。

### 6.3 Generation 与 Entity 的边界（总表）

| 问题 | Voxel / Generation（本文档） | Entity（未来独立规范） |
|---|---|---|
| 这个坐标是实体还是空？ | ✅ `Density` | ❌ 不参与 |
| 这个坐标是什么材料？ | ✅ `Material`（4 种体素状态） | ❌ |
| 这个坐标存在什么资源？ | ✅ `DepositZone`（属性） | 只读消费 |
| 这个坐标该不该放一个晶簇？ | ❌ 不决定 | ✅ 决定（可读 `DepositZone` + 暴露条件） |
| 晶簇的状态/数量/生命期 | ❌ | ✅ 拥有 |
| 晶簇的 Mesh / 渲染 | ❌ | ✅（Entity 的表现层） |
| 晶簇的持久化 | ❌（Voxel 只保存被编辑的体素） | ✅（Entity 系统自己的存档） |

---

## 7. 生物群系接口

**本规范只定义接口，不实现任何具体群系。**

```
Biome(x, z) : BiomeId                                  // 纯函数（Seed + 世界坐标）+ 连续过渡

BiomeProfile(BiomeId) →
    Temperature      : 标量        // 温度
    Humidity         : 标量        // 湿度
    SurfaceMaterial  : VoxelType   // 地表材料（V1 只能是 Grass/Dirt/Stone 之一）
    VegetationRule   : 标识        // 植被条件（供未来 Entity/Vegetation 系统消费）
    TerrainModifier  : 修正量      // 对 SurfaceHeight / SoilDepth / 地表频率的**显式**修正
```

规则：

1. **纯函数**：`Biome(x,z)` 只依赖 Seed 与世界坐标；不得依赖时间、加载顺序、已加载的 Chunk；
2. **连续过渡**：群系边界必须可连续过渡（不得在相邻 Chunk 之间出现"规则不同"的跳变）；
   允许过渡带由显式参数控制宽度；
3. **只能影响地形规则**：群系通过 `TerrainModifier` 与 `SurfaceMaterial` 影响 P2/P3（§3.2），
   **不得**改变 P0/P1 的优先级语义、不得新增体素类型；
4. **不得改变 Runtime**：群系不得影响加载/卸载、Chunk 生命周期、脏标记、并发与预算；
5. **接口稳定**：Runtime / Streaming / Bootstrap / Mesh / View **都不认识 Biome**
   —— 它们只认识 `IWorldGenerator`（`VoxelWorld_Runtime_Interface.md` §4），
   因此群系的引入**不需要修改任何既有调度代码**；
6. **不决定 Entity**：`VegetationRule` 只是"条件"的声明（供未来植被/Entity 系统消费），
   本规范不生成任何植被对象。

---

## 8. 确定性要求

继承 `WorldGeneration_Core.md` §2 与 `VoxelWorld_Runtime_Core.md` §10，并在地质层细化。

### 8.1 必须保证

```
同样的：
    Seed
  + WorldGenerationSettings（+ 地质参数结构）
  + 世界坐标
⇒ 完全一致的 Density / Material / CaveDensity / DepositZone / Biome 结果
⇒ 完全一致的 VoxelChunk 体素数据（逐体素）
⇒ 完全一致的派生 Mesh 内容哈希
```

### 8.2 禁止

| # | 禁止 | 说明 |
|---|---|---|
| **D1** | `UnityEngine.Random` / `System.Random` / 任何有状态伪随机源 | 状态会被调用顺序影响 |
| **D2** | `Time` / `DateTime` / 帧序 / `Guid.NewGuid` | 非确定性来源 |
| **D3** | 依赖 Chunk 加载顺序、邻居是否已生成、已加载集合 | 违反"世界坐标纯函数" |
| **D4** | 依赖 Chunk 编号的扰动（F1） | 会让同一世界坐标在不同窗口下结果不同 |
| **D5** | 依赖 `Mathf.PerlinNoise` 等实现依赖平台的噪声 | 跨平台/跨版本可能不一致；既有 `HeightFunction` 已改为**自实现整数哈希值噪声**，地质层必须沿用同一策略 |
| **D6** | 依赖 `Dictionary`/`HashSet` 遍历顺序 | 若采样顺序影响结果，就是缺陷 |

### 8.3 建议（可验证的确定性策略）

1. 噪声 = **整数哈希（Seed + 世界坐标）→ 定点/整数插值**，避免平台相关浮点路径差异；
2. 所有参数显式（`Seed` + 参数结构）；禁止隐藏全局状态；
3. 每个采样函数都可**独立重复求值**（同一坐标重复采样结果恒等）；
4. 用 `VoxelMeshData.ComputeContentHash()` 与"全量体素快照"作为机械证据（§10.4）。

---

## 9. 与现有规范的关系

### 9.1 引用的规范

| 规范 | 本文档与其关系 |
|---|---|
| `VoxelWorld_Core.md` | **上游**：世界真值只有 Voxel Data；体素状态集合（Air/Grass/Dirt/Stone）与 Chunk 数据契约由它冻结，本文档不得新增状态 |
| `WorldGeneration_Core.md` | **上游**：Seed + ChunkCoordinate → VoxelChunk 契约、确定性、Chunk 可独立计算（§6）、stage 划分（§7）、不覆盖玩家改动（§9）、扩展不得要求修改 VoxelCore（§10） |
| `VoxelWorld_Runtime_Core.md` | **下游（消费者）**：Runtime Core 只调用 `IWorldGenerator` 并把结果安装进世界（§5.2），按 Seed/坐标做确定性断言（§10）；它不知道地质规则的存在 |
| `VoxelWorld_Runtime_Interface.md` | **边界**：`IWorldGenerator` 是 Core 依赖的抽象（§4）；本文档的实现属于"Generation 实现"，不得反向依赖 Core（§2.2） |
| `VoxelWorld_Runtime.md` | **调度**：什么时候加载 / 加载哪些 / 何时卸载由它决定，与本文档无关（§5.3 的加载集合判定式） |
| `VoxelWorld_Mesh.md` | **派生表示**：洞穴/地层只改变体素数据；几何由 Meshing 从体素派生（§1.2 只读、§8 不变量） |
| `VoxelWorld_Mining.md` | **运行时修改**：挖掘与生成洞穴结果相同（Air）但来源不同；生成不得覆盖改动（§9） |
| `VoxelWorld_View.md` | **表现层**：与地质规则无关；矿藏/群系不产生 View 侧特殊处理 |
| `ADR-001`（World / Space / Entity） | **概念边界**：Chunk 只是空间分区；Terrain 是 Space 的空间数据；**Entity 不属于 Chunk 所有权体系**（§6.2 的晶簇据此归属 Entity） |

### 9.2 权威链

```
真值            VoxelWorld_Core.md
  ↑
生成契约        WorldGeneration_Core.md
  ↑
地质规则        本文档（层 / 洞 / 矿藏 / 群系接口）
  ↑
调度            VoxelWorld_Runtime.md → _Runtime_Core.md → _Runtime_Core_Implementation.md
  ↑
抽象与装配      VoxelWorld_Runtime_Interface.md
  ↑
派生表示        VoxelWorld_Mesh.md → VoxelWorld_View.md
```

**Generation 拥有世界规则；Runtime 只调度。**

### 9.3 与已有架构的数据流（不得改变）

```
VoxelCore                世界真值容器与坐标/脏语义（冻结）
    ↓
WorldGeneration          Seed + ChunkCoordinate → VoxelChunk（本规范在此层细化规则）
    ↓
VoxelChunk               Chunk 内的体素数据（4096 个体素）
    ↓
Mesh                     VoxelChunk → VoxelMeshData（只读、纯数据）
    ↓
View                     VoxelMeshData → Unity 表示（唯一允许 Unity 类型的层）
```

### 9.4 落地形态（零修改既有实现）

| 事项 | 落地方式（强制） |
|---|---|
| 既有 `WorldGenerationSettings` | **不得修改**：其字段集是 Stage 1 的契约 |
| 既有 `IWorldGenerator` | **不得修改**（`VoxelWorld_Runtime_Interface.md` §4：不改名、不重复定义、不改签名） |
| 既有 `BasicTerrainGenerator` | **不得修改**：Stage 1 行为保持不变 |
| 地质参数 | 在**新增的显式参数结构**（例如 `GeologySettings`）中声明；由**新增的生成器实现**持有并显式注入 |
| 新 stage | 以新的生成器实现（实现 `IWorldGenerator`）叠加 Stage 1：先调用 Stage 1 规则，再应用 Cave（P1）与属性层 |
| Runtime / Streaming / Bootstrap | **不需要任何修改**（它们只依赖 `IWorldGenerator`；见 `Runtime_Interface.md` §2.4 组装点） |
| VoxelCore / Mesh / View / Mining | **不需要任何修改** |

---

## 10. V1 范围

### 10.1 只设计（In Scope）

| # | 内容 | 形态 |
|---|---|---|
| 1 | **连续 Terrain** | `Density`/`Material` 的世界坐标纯函数（§2/§3） |
| 2 | **Layer** | Surface / Depth / Deep 的规则表（§4） |
| 3 | **Cave** | `CaveDensity` 与 P1 覆盖规则（§5） |
| 4 | **Ore Deposit** | `DepositZone` 属性层（§6.1） |
| 5 | **Biome 接口** | 只定义接口（§7） |

### 10.2 暂不设计（Out of Scope）

* **Entity System**（含晶簇、矿石节点、掉落物）；
* **Building**（建筑/结构放置）；
* **Tree**（树木）；
* **Creature / NPC**（生物与角色）；
* **Civilization**（聚落、道路、文明痕迹）；
* 任何需要新增 `VoxelType` 的内容；
* 任何需要修改 VoxelCore / WorldGeneration 既有实现 / Mesh / View / Runtime / Streaming / Bootstrap 的内容。

### 10.3 与"当前实现范围"的关系

`WorldGeneration_Core.md` §8 描述的"当前只实现 height based terrain、不实现洞穴/矿物/群系"仍然是
**既有实现的现状**；本文档是**地质规则层的规范**（§7 的 Stage 2/3/5），其落地必须按 §10.4 的顺序
**逐步以新增实现的方式推进**，且每一步都必须保持 §8 的既有实现不变。

### 10.4 落地顺序（每步都必须可独立验证）

| 阶段 | 内容 | 验收不变量 |
|---|---|---|
| **G-A（Stage 1，已存在）** | height-based terrain（`BasicTerrainGenerator`） | 既有 `WorldGenerationCoreValidation`（41 项）保持 PASS；零修改 |
| **G-B（Stage 2）** | Cave 覆盖（P1） | GE1–GE8、GE12 |
| **G-C（Stage 3）** | Biome 接口 + 地形修正 | GE3–GE6、GE12 |
| **G-D（Stage 5）** | DepositZone 属性层 | GE7、GE9–GE12 |

**验收不变量（地质层，可机械验证）**

| # | 不变量 | 验证方法 |
|---|---|---|
| **GE1** | **纯函数性**：同一坐标重复采样结果恒等 | 同一坐标采样 N 次逐字节比较 |
| **GE2** | **Seed 决定性**：同 Seed ⇒ 同结果；不同 Seed ⇒ 至少一列/一体素不同 | 双世界对照 + 反例保护 |
| **GE3** | **Chunk 边界连续**：相邻 Chunk 共享边界的体素逐体素相等 | 取 16×16 边界列比对（既有验证入口已有同类做法） |
| **GE4** | **洞穴连续**：相邻坐标的 `CaveDensity` 差值有界（无逐点跳变） | 差分上界断言 |
| **GE5** | **优先级正确**：`Cave > Rock`；`y ≥ WorldHeight` 恒空；`y > SurfaceHeight` 恒空 | 逐体素判定与独立重算结果比对（全 4096） |
| **GE6** | **地层规则合规**：每个体素的材料符合 §4 的层表（含 `SoilDepth = 0` 边界） | 全 Chunk 逐体素规则检查 |
| **GE7** | **属性不改密度**：开启/关闭 `DepositZone` 不影响任何体素状态 | 两次生成的体素快照完全相等 |
| **GE8** | **洞穴只改 Density**：洞穴开启/关闭只造成 Solid→Empty 的变化，不改变其它体素的材料 | 差异集合 ⊆ {Stone/Dirt/Grass → Air} |
| **GE9** | **矿藏只在实体内**：`DepositZone ≠ None` 的坐标必须是实体 | 全量查询比对 |
| **GE10** | **Entity 边界**：生成过程不产生任何 Entity、不写非体素的资源标记 | 结构性检查（生成接口不出现 Entity 类型 / 无资源体素写入） |
| **GE11** | **不改既有实现**：VoxelCore / WorldGeneration / Mesh / View / Runtime / Streaming / Bootstrap 的 SHA 不变 | 文件哈希比对（既有验证入口做法） |
| **GE12** | **确定性证据**：同 Seed + 坐标 ⇒ 体素快照与派生 Mesh 内容哈希完全一致 | `ComputeContentHash()` 比较 |

---

## 附录 A：可解释性（判定链问答）

> 本附录直接回答验收要求的三个问题：**"为什么是石头" / "为什么这里有洞" / "为什么这里生成晶簇"**。
> 每一条都给出**唯一可复现的判定链**（Generation 必须能被这样逐条解释）。

### A.1 "这个坐标为什么是石头（Stone）？"

以世界坐标 `(x, y, z)` 为例，判定链（自高优先级到低优先级）：

```
1) WorldBounds ：y ≥ 0 且 y < WorldHeight           → 通过（不是世界之外）
2) Cave        ：CaveDensity(x,y,z) < CaveThreshold  → 通过（这里不是洞）
3) Terrain     ：y ≤ SurfaceHeight(x,z)             → 通过（在地表或地表之下）
4) Depth 检查  ：y > SurfaceHeight - SoilDepth ?     → 否
5) Deep 规则   ：⇒ Material = Stone
```

结论：该坐标是 `Stone`，因为它**在地表之下、超出土壤层深度、且没有被洞穴挖空**。

反例（同一条链的其它出口）：

| 改变条件 | 结果 |
|---|---|
| `y > SurfaceHeight` | `Air`（地表之上） |
| `SurfaceHeight - SoilDepth < y ≤ SurfaceHeight` | `Dirt` / `Grass` |
| `CaveDensity ≥ CaveThreshold` | `Air`（洞穴覆盖岩石 —— P1 > P3） |
| `y ≥ WorldHeight` | `Air`（世界之外，P0 无条件） |

可自动化验证：**逐体素重算这条链**并与生成器输出比对（GE5/GE6）。

### A.2 "为什么这里有洞？"

```
1) WorldBounds ：y ≥ 0 且 y < WorldHeight            → 通过
2) Cave        ：CaveDensity(x,y,z) ≥ CaveThreshold   → 成立
3) 覆盖        ：P1 优先级高于 P2/P3 ⇒ Density = Empty
4) 结果        ：Voxel = Air（洞穴）
（几何由 Meshing 自然产生：邻居为 Air ⇒ 生成洞壁面；本层不生成任何几何）
```

结论：这里有洞，因为**洞穴噪声在该坐标超过阈值**，且洞穴规则优先于地层实体规则。

要点：

* 洞是**规则的结果**，不是"被放进来的对象"；
* 判定只依赖 `(Seed, 参数, 世界坐标)` ⇒ 同一世界任何时候都得到同一个洞；
* 与挖掘的区别见 §5.3（来源不同，结果相同）。

### A.3 "为什么这里生成晶簇？"

判定被**明确拆成两段**（这是 Voxel 与 Entity 的边界）：

```
【Voxel / Generation 段】—— 本规范负责
1) WorldBounds ：y ≥ 0 且 y < WorldHeight
2) 实体检查    ：Density(x,y,z) == Solid      （晶簇不会出现在空气里）
3) 矿藏查询    ：DepositZone(x,y,z) == CrystalZone
4) 暴露条件    ：该坐标至少一个相邻面为 Air（"裸露"条件的声明）
   ⇒ 产出：一个**只读的查询答案**："此处理论上存在资源，且已满足暴露条件"

【Entity 段】—— 未来 Entity 系统负责（本规范不决定、不生成）
5) 稀有度 / 密度上限 / 群聚形态：Entity 系统按自己的规则决定是否真的生成
6) 对象状态与生命周期：Entity 拥有
7) 渲染 / 碰撞 / 存档：Entity 的表现层与存档层拥有
```

结论：这里"生成晶簇"的原因分两半：**Voxel 层回答"这里存在 CrystalZone 资源且满足暴露条件"**，
**Entity 层决定"是否真的放一个晶簇对象"**。

硬性边界（§6.2）：

* 晶簇**不是** Voxel 状态：`VoxelType` 里没有、也不会有"晶簇"；
* Generation **不**写任何"资源标记"进体素数据；
* Entity **不属于** Chunk 的所有权体系（ADR-001），只通过空间位置与 Space 建立关系；
* 因此"为什么这里生成晶簇"的完整答案 = **Generation 的资源规则 + Entity 系统的生成规则**，
  两者各自可独立验证、互不修改。

---

## 附录 B：与已有规范的一致性检查

| # | 已有规范 | 本文档的兼容性处理 | 是否冲突 |
|---|---|---|---|
| 1 | `VoxelWorld_Core.md` | 不新增 `VoxelType`（V1 只有 Air/Grass/Dirt/Stone）；矿藏/岩性作为**属性层**；不改变 Chunk 坐标契约与脏语义 | 否 |
| 2 | `WorldGeneration_Core.md` | 沿用 §2 确定性、§4 高度模型（逐条对应 §3.3）、§6 计算独立（术语澄清见 §2.3）、§7 stage 划分、§9 不覆盖改动、§10 不要求修改 VoxelCore；§8"当前范围"按 §10.3 处理 | 否 |
| 3 | `VoxelWorld_Mesh.md` | 地质层只改体素数据；几何仍由 Meshing 派生（§1.2 只读）；不新增面/材质/子网格语义 | 否 |
| 4 | `VoxelWorld_Mining.md` | 生成洞穴与运行时挖掘来源不同、结果相同；生成不覆盖改动；编辑入口不变 | 否 |
| 5 | `VoxelWorld_Runtime.md` | 加载/卸载判定与本文档无关（世界规则 vs 调度）；`WorldHeight` 之上恒空与 §6.4 一致 | 否 |
| 6 | `VoxelWorld_Runtime_Core.md` | Runtime Core 只调用 `IWorldGenerator` 并安装结果（§5.2）；其"类型越界检查"与本文档"不新增体素类型"一致 | 否 |
| 7 | `VoxelWorld_Runtime_Interface.md` | 地质实现 = "Generation 实现"，只依赖抽象层；不反向依赖 Core；组装点不需要改动 | 否 |
| 8 | `VoxelWorld_Runtime_Core_Implementation.md` | 安装流程（Generate → Validate → Install → Register）与地质规则无关；`NeedsReinstall`/脏语义不变 | 否 |
| 9 | `VoxelWorld_View.md` | 矿藏/群系不产生 View 侧特殊处理；View 仍只做搬运/摆放/释放 | 否 |
| 10 | `VoxelWorld_Architecture.md` | 与"体素数据是唯一真值 / Mesh 是派生"一致 | 否 |
| 11 | `ADR-001`（World / Space / Entity） | 晶簇等对象归 **Entity**，不属于 Chunk 所有权体系；Chunk 只是空间分区 | 否 |

**结论**：本文档只**新增规则层**，不改动任何既有契约；与已有 10 份世界规范 + ADR-001 均无冲突。

---

## 附录 C：术语表

| 术语 | 含义 |
|---|---|
| **Density** | "该坐标是否实体"的判定（Empty / Solid），由优先级链（§3.2）决定 |
| **Material** | 实体坐标的体素状态（V1：Grass / Dirt / Stone） |
| **SurfaceHeight** | 地表高度（世界坐标的纯函数），决定实体边界与层划分 |
| **Layer** | 地层（Surface / Depth / Deep），决定材料与密度影响（§4） |
| **CaveDensity** | 洞穴强度标量；超阈值 ⇒ 覆盖为 Empty（P1） |
| **DepositZone** | 矿物区域属性（哪里存在资源），**不是**体素类型 |
| **Biome** | 群系（温度/湿度/地表材料/植被条件/地形修正），只定义接口 |
| **Entity** | 具有自身状态、通过空间位置与 Space 建立关系的对象（晶簇、矿石节点、生物、建筑）；**不属于 Voxel，也不属于 Chunk 所有权** |
| **采样窗口** | Chunk 在世界函数上的作用：抽取某一区域的采样结果（§2.1） |

---

## 附录 D：变更规则

1. 与上游冲突时优先级见 §9.2；本文档不得放宽上游的硬性条款（真值隔离、确定性、不覆盖改动、不要求改 VoxelCore）。
2. 任何修改若触及 §2.3（禁止项）、§3.2（优先级）、§6.2（Voxel/Entity 边界）、§8（确定性）
   或附录 B 的兼容性结论，必须：① 升版本号；② 同步更新 §10.4 的验收不变量；③ 说明与既有存档/世界的兼容性。
3. **V1 明令禁止**：以"地质需要"为理由新增 `VoxelType`、修改 VoxelCore、修改既有生成实现、
   在 Mesh/View/Runtime/Streaming 里为地质打补丁。
4. 地质层的落地只能以**新增 stage / 新增生成器实现 + 新增显式参数结构**的方式进行（§9.4）。
5. 本文档不定义 Entity 系统；Entity 相关规则必须由独立规范定义，且不得回写 Voxel 数据。
