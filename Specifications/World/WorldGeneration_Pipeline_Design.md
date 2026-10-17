# World Generation Pipeline Design Specification V1

> **文档性质**：**设计规范（Design Specification）**。本文档只描述 **世界生成的数据流与职责边界**。
> **不创建任何 `.cs`**；**不修改任何已有文件**；**不定义接口 / class / struct / 代码草案**；
> **不涉及 Runtime / Chunk / Mesh / Entity 的实现**；不实现任何生成逻辑。
>
> **定位**：本文档是**层间数据流与阶段顺序的唯一权威**：它规定"谁在什么时候读到什么、产出什么、绝不能读什么"。
> 各层**内部**的规则仍由各自的规范拥有（本文档只做调用顺序与边界约束，不重新定义规则）。
>
> **必须兼容的既有规范**：
>
> | 规范 | 与本 Pipeline 的关系 |
> |---|---|
> | `Specifications/World/WorldState_Design.md` | **参数与因果纪律的上游**：8 个环境参数、§1.5 倒因为果禁令（C1–C4）、§3 静态场（MF/CF/T/MX）、§4 圣与恶（HP/HX）、§5 群系规则表与退化兼容（B/CT/§5.8）、§7 世界尺度（WC） |
> | `Specifications/World/Region_Design.md` | **Region 层的上游**：术语对齐（RT1–RT6）、来源（RS/RC）、修正（RM/RP/RE）、Biome 关系（RB）、历史（RH）、遗迹候选（RR）、边界（RBD）、叠加（RO）、世界尺度（RW）、V1 禁令（RV）、间接路径（RI）、验收（RG/RGV/RGS） |
> | `Specifications/World/WorldGeneration_Geology.md` | **地形/岩层/洞穴/材料的上游**：§2 连续世界模型与确定性与禁止项（F1–F4）、§3 密度优先级链（P0–P3）、§4 地层、§5 洞穴、§6 矿藏（属性层 + Voxel/Entity 边界）、§7 群系接口、§8 确定性（D1–D6）、§10 V1 范围与验收（GE1–GE12） |
> | `Specifications/World/WorldGeneration_Geology_Implementation.md` | 地质访问层：世界坐标采样的唯一入口、新增而非替换、GIV1–GIV9 |
> | `Specifications/World/WorldGeneration_Geology_Runtime_Implementation.md` | 生成器落地：Generate 链路、§6.1 群系**只影响地表材料**、§7 GIV9 等价性 |
> | `Specifications/World/WorldGeneration_Core.md`、`VoxelWorld_Core.md` | 生成契约（`Seed + ChunkCoordinate → VoxelChunk`、确定性、Chunk 计算独立）与世界真值（体素状态集合冻结） |
> | `Specifications/World/VoxelWorld_Mesh.md`、`VoxelWorld_View.md`、`VoxelWorld_Mining.md`、`VoxelWorld_Runtime*.md`、`VoxelWorld_Architecture.md`、`ADR-001` | 下游消费者与概念边界（本文档不定义它们的行为） |
>
> **冲突时的优先级**：**真值**以 `VoxelWorld_Core.md` 为准；**生成契约**以 `WorldGeneration_Core.md` 为准；
> **地形/岩层/洞穴/材料**以 `WorldGeneration_Geology*.md` 为准；**环境参数与力量规则**以 `WorldState_Design.md` 为准；
> **区域的来源/修正/标签/资格**以 `Region_Design.md` 为准；**层间顺序与数据流**以**本文档**为准。
>
> **范围声明（硬性）**：本文档中出现的 `Voxel Output` 指"**世界坐标 → 体素状态**"这一**纯函数结果**（即世界真值），
> **不是** Chunk 容器、不是加载单位、不是 Mesh；Chunk 的划分与调度属于 Runtime/Streaming（完全不在本文档范围）。

---

## 目标数据流（本文档要定义的对象）

```
Seed
 ↓
World History
 ↓
Region Source
 ↓
Region Modifier
 ↓
WorldState
 ↓
Biome Resolution
 ↓
Geology Generation
 ↓
Voxel Output
```

**核心原则（三条，贯穿全文）**

| # | 原则 | 含义 |
|---|---|---|
| **P-1** | **因果方向唯一** | 原因在上游，结果在下游；禁止"看到结果就创建原因"（例如**看到魔法森林 → 创建魔法源**） |
| **P-2** | **每一层只能影响下一层** | 层 k 的产出只能被层 k+1 及更下游读取；**禁止任何反向依赖**（Geology 不得影响 History；Biome 不得创建 Region；Voxel 生成不得改变 WorldState） |
| **P-3** | **每个系统拥有明确责任** | 每层有唯一的输入集合、唯一的产出集合、唯一的权威规范；禁止越权、禁止第二套权威 |

---

## §1 世界生成总体架构

### 1.1 定义

> **World Generation = 多层确定性函数组合（Layered Deterministic Function Composition）。**

```
World Generation = f_Voxel ∘ f_Geology ∘ f_Biome ∘ f_WorldState ∘ f_Region ∘ f_History ∘ f_Skeleton ∘ Seed
```

| 含义 | 说明 |
|---|---|
| **多层** | 每层只解决一个问题（骨架 / 历史 / 区域 / 环境 / 群系 / 地形 / 体素） |
| **确定性** | 同 `Seed` + 同世界坐标 ⇒ 同结果（§3） |
| **函数组合** | 每层是**纯函数**：无副作用、无跨帧状态、可重复求值（`WorldState_Design.md` §3.6 T1–T4） |
| **不是一次性全量执行** | 世界无界 ⇒ **按查询点局部求值**（"惰性流水线"），不存在"世界级流水线状态"（§1.4） |

### 1.2 层次结构

```
World
 ├ History          （历史事实：事件 / 文明 / 灾难 / 圣恶事件）
 ├ Region           （世界状态的解释单元：来源 / 修正 / 标签 / 资格）
 ├ WorldState       （8 个环境参数：Base + Region 修正）
 ├ Biome            （生态分类结果）
 ├ Geology          （地形 / 岩层 / 洞穴 / 材料）
 └ Voxel            （体素真值：世界坐标 → 体素状态）
```

### 1.3 每层的输入 / 输出（唯一权威表）

| 层 | 输入（只能读这些） | 输出 | 权威规范 |
|---|---|---|---|
| **Seed** | 无（外部给定） | 全局确定性种子 | `WorldGeneration_Core.md` §2/§3 |
| **World Skeleton**<br>（基础地理框架） | `Seed` + 显式世界参数 | 大陆/海洋框架、基础气候带、基础高度框架（来自既有 `HeightFunction`） | `WorldState_Design.md` §7.3/§7.4（WC1–WC6） |
| **History** | `Seed` + 世界骨架（只读：陆地/海洋/气候框架） | **History Tags** + **Region Source 候选** | 未来 History 规范（本文档 §4 定义边界） |
| **Region** | `Seed` + History 产物（只读）+ 自然规则 + 地形派生量（`Elevation`/`Ocean` 只读） | **Region**（来源 + `RegionModifier` + 标签 + 资格） | `Region_Design.md`（RS/RM/RC/RB/RH/RR/RBD/RO） |
| **WorldState** | `BaseWorldState`（骨架/基础规则）+ `RegionModifier` | **FinalState**：8 个环境参数 | `WorldState_Design.md`（§1.3、§3、§4、§5） |
| **Biome** | **FinalState**（只读） | **Biome**（离散生态分类） | `WorldState_Design.md` §5.3（唯一规则表） |
| **Geology** | 世界坐标 + `Biome`（只读）+ 地质参数 | `Density` / `Material` / `CaveDensity` / `DepositZone` | `WorldGeneration_Geology*.md` |
| **Voxel Output** | Geology 的判定结果（P0–P3 优先级链输出） | 世界坐标 → 体素状态（世界真值） | `VoxelWorld_Core.md`、`WorldGeneration_Core.md` |

**硬性**

| # | 规则 |
|---|---|
| **PA1** | 每层的输入集合是**封闭的**：表外的任何量都不得被读取（尤其不得读取下游产物） |
| **PA2** | 每层的产出必须**显式**（不得依赖隐藏全局状态；继承 `WorldGeneration_Core.md` §3 与 `WorldState_Design.md` §1.3 规则 3） |
| **PA3** | 层与层之间**不得共享可变状态**；只允许"上游产出 → 下游只读" |
| **PA4** | 层与层之间**不得共享随机序列**（§3.3） |
| **PA5** | 任何层都不得跳过中间层去读取更下游的产物（例如 Geology 不得直接读 History） |

### 1.4 惰性求值（管线的执行模型）

| # | 规则 |
|---|---|
| **PB1** | 管线**不是**"先全量生成世界，再从中取样"；实现形态必须是**按查询点求值** |
| **PB2** | 任意世界坐标必须能**独立**求完整条链（骨架 → … → 体素状态），不依赖邻域是否已求值 |
| **PB3** | 不允许存在"世界级中间态"（例如"先跑完历史模拟再生成地形"） |
| **PB4** | 允许缓存，但缓存必须只由 `(Seed, 参数, 世界坐标, 域标签)` 索引，且不得改变输出（`WorldState_Design.md` §3.6 T2） |
| **PB5** | 求值过程**不得产生副作用**：不写 Voxel、不创建 Chunk、不置脏、不生成对象 |

---

## §2 Generation Phase 顺序

### 2.1 阶段定义

| Phase | 名称 | 产出 | 是否在因果链上 |
|---|---|---|---|
| **Phase 0** | **Seed** | 全局确定性种子 | 因果链起点 |
| **Phase 1** | **World Skeleton** | 大陆/海洋框架、基础气候带、基础高度框架 | **舞台提供者**（见 §2.3） |
| **Phase 2** | **History Generation** | History Tags、Region Source 候选 | 是 |
| **Phase 3** | **Region Generation** | Region（含 `RegionModifier`） | 是 |
| **Phase 4** | **WorldState Evaluation** | FinalState（8 个环境参数） | 是 |
| **Phase 5** | **Biome Resolution** | Biome | 是 |
| **Phase 6** | **Geology** | `Density` / `Material` / `CaveDensity` / `DepositZone` | 是 |
| **Phase 7** | **Voxel Output** | 世界坐标 → 体素状态 | 是（终点） |

### 2.2 顺序图

```
Phase 0  Seed
   │
   ├───────────────► Phase 1  World Skeleton ───────────────┐（框架：大陆/海洋/基础气候/基础高度）
   │                        │                               │
   │                        ▼                               │
   │                 Phase 2  History Generation            │
   │                        │                               │
   │                        ▼                               │
   ├───────────────► Phase 3  Region Generation             │
   │                        │                               │
   │                        ▼                               │
   │                 Phase 4  WorldState Evaluation  ◄───────┘（Base 框架由 Phase 1 提供）
   │                        │
   │                        ▼
   │                 Phase 5  Biome Resolution
   │                        │
   │                        ▼
   │                 Phase 6  Geology
   │                        │
   │                        ▼
   └───────────────► Phase 7  Voxel Output
```

### 2.3 Phase 1（World Skeleton）的定位：舞台提供者

| # | 规则 |
|---|---|
| **PC1** | 骨架**不是因果链的一环**，而是**框架提供者**：它给 Phase 2 提供"陆地/海洋/气候"的舞台，给 Phase 4 的 `BaseWorldState` 与 Phase 6 的地形基础提供框架 |
| **PC2** | 骨架**只被下游读取**，永不读取下游（尤其不得读取 History / Region / Biome） |
| **PC3** | 骨架必须**可退化**：`ContinentCount = 1` 且掩码退化为"全部陆地"时，世界回到既有的无限地形（`WorldState_Design.md` §7.4 WC2） |
| **PC4** | 骨架**不得替换**既有高度场：`Elevation` 必须复用既有 `SurfaceHeight`（`HeightFunction`），掩码只做**叠加** |
| **PC5** | 骨架**不得**引入水平世界硬边界（`Region_Design.md` §10.3 RW2） |

> **与目标数据流图的对应**：本文档开头的目标图描述的是**因果链**（History → Region → … → Voxel）；
> 骨架在因果上位于**所有阶段之前**，但不产生"原因"，只提供"舞台"，因此不出现在因果链图中。

### 2.4 为什么顺序不能交换

| # | 禁止的交换 | 后果 |
|---|---|---|
| **1** | History 与 Skeleton 交换 | 历史将失去舞台（文明无法判定"哪块陆地"、海洋/山脉条件不存在），且 History 会去读下游（违反 `PA5`） |
| **2** | Region 与 History 交换 | Region 的来源将失去历史依据；等价于"凭空产生区域"（违反 `Region_Design.md` RG-A 与 RS1），也会退化为"随机生成魔法森林" |
| **3** | **Biome 与 Region 交换**（见 §2.5） | 群系判定只能看到基础环境，**区域原因无法体现**；并且会使"区域修正环境 → 群系"的因果链倒置（违反 `P-1`） |
| **4** | WorldState 与 Region 交换 | `RegionModifier` 将无处可加：区域修正必须作用在**基础环境之上**，先算环境再算区域等于放弃修正 |
| **5** | Biome 与 WorldState 交换 | 群系解析的输入是 `FinalState`；先于环境就无法读到参数（`WorldState_Design.md` §5.2） |
| **6** | **Geology 提前**（见 §2.6） | 地形会失去"地表材料由群系决定"的输入；更严重的是会把 Geology 变成上游，从而可能反向影响 History/Region（违反 `P-2`） |
| **7** | Voxel Output 提前 | 体素是**最下游的产物**；提前产生会让体素数据反过来成为"世界状态"的来源（违反原则 2 的明确禁令） |
| **8** | 任何阶段"并行乱序" | 只要存在一条反向读取，确定性（§3）与可解释性（附录 D `PG13`）都会失效 |

### 2.5 重点解释：为什么 **Biome 必须在 Region 之后**

| # | 理由 |
|---|---|
| **1** | **群系的定义就是"环境表现"**：`WorldState_Design.md` §5.1 规定 Biome 由环境参数向量判定；而在有区域影响的世界里，环境 = `Base + RegionModifier`。若 Biome 先算，就只可能读到 `Base`，**区域修正永远无法体现** |
| **2** | **因果方向**：Region 是**原因**，Biome 是**结果**（`Region_Design.md` §5.1）。先算结果等于让结果先于原因存在 |
| **3** | **禁止"Region 直接指定 Biome"**：`Region_Design.md` §5.3 RB2 规定 Region 只能通过修改环境参数**间接**影响群系。若 Biome 在 Region 之前，实现上就必然要"由 Region 名直接映射 Biome"，正是被禁止的形态 |
| **4** | **单一权威**：群系解析只有一套规则表（`WorldState_Design.md` §5.3）；它必须接受**完整的**环境向量（含区域修正），否则同一坐标会出现两套结论 |
| **5** | **可解释性**：魔法森林必须能解释为"魔力源 → 魔力环境 → 群系规则命中"（附录 A 数据流图），这要求 Region 与 WorldState 都已完成 |

### 2.6 重点解释：为什么 **Geology 最后**（在 Biome 之后、Voxel 之前）

| # | 理由 |
|---|---|
| **1** | **Geology 是唯一的地形权威**：`Density` / `Material` / `CaveDensity` 只能由地质规则产生（`WorldGeneration_Geology.md` §3.2 优先级链 P0–P3）。它必须在所有**上游规则**确定之后再执行 |
| **2** | **生物群系是它的输入之一（且极其有限）**：群系只影响**地表材料**（`WorldGeneration_Geology_Runtime_Implementation.md` §6.1：只作用于地表层、取值限 `{Grass, Dirt, Stone}`）。材料规则在判定链的 **P3**，因此 Biome 必须在此之前确定 |
| **3** | **P0/P1/P2 不得依赖 Biome**：世界边界（P0）、洞穴（P1）、地形（P2）必须与群系无关；地质"最后"指的是**在材料层之前完成输入准备**，而不是让 Biome 参与密度与洞穴判定 |
| **4** | **保持 Geology 独立**：地质规则不认识 Region / History / 群系标识之外的世界状态语义（`WorldGeneration_Geology.md` §9.1）；把 Geology 放在最后可以保证它**只读输入、不反向影响** |
| **5** | **Voxel Output 是它的直接下游**：Geology 的输出就是"世界坐标 → 体素状态"的判定结果，中间不应再插入任何会修改世界逻辑的层 |
| **6** | **零修改前提**：既有地质实现（Stage A–E）已按此顺序落地为"采样器 → 生成器 → 运行时安装"；本顺序与既有实现一致，因此不需要修改任何既有文件（附录 D `PG12`） |

### 2.7 阶段间的契约（只读传递）

| 传递 | 允许 | 禁止 |
|---|---|---|
| Phase 1 → 2/4/6 | 只读框架 | 下游修改框架 |
| Phase 2 → 3 | 只读事实 | Region 改写历史 |
| Phase 3 → 4 | 只读修正 | WorldState 改写 Region |
| Phase 4 → 5 | 只读参数 | Biome 改写参数 |
| Phase 5 → 6 | 只读群系（且只用 P3 地表材料） | Geology 改写群系 |
| Phase 6 → 7 | 只读判定结果 | Voxel 生成改写判定结果 |

---

## §3 Seed 与确定性

### 3.1 定义

> **同一个 `Seed` ⇒ 同一个世界。**

**`Seed` 决定（且仅由 `Seed` + 显式参数决定）的全部产物**

| 产物 | 所属 Phase | 域标签（Domain）概念 |
|---|---|---|
| 大陆 / 海洋框架 | Phase 1 | `Skeleton` |
| 基础气候带（温度带 / 湿度带基线） | Phase 1 | `Skeleton.Climate` |
| 基础高度框架 | Phase 1/6 | **既有** `HeightFunction` 的既有域（**不得改动**） |
| 历史事件（战争 / 灾难 / 魔法事件 / 圣恶事件） | Phase 2 | `History`（按事件类别细分域） |
| 文明（存在、偏好归属） | Phase 2 | `History.Civilization` |
| **Region 位置** | Phase 3 | `Region.Natural` / `Region.Magic` / `Region.Holy` / `Region.Corruption` |
| **魔法源** | Phase 3 | `Region.Magic` |
| **圣恶区域** | Phase 3 | `Region.Holy` / `Region.Corruption` |
| **遗迹候选** | Phase 3（区域级） | `Region.Relic` |
| **Biome** | Phase 5 | 由 `FinalState` 决定（不是独立随机产物） |
| **Terrain** | Phase 6 | **既有**地质域（**不得改动**） |
| 体素状态 | Phase 7 | 由 Phase 6 决定（不是独立随机产物） |

### 3.2 确定性要求

| # | 要求 |
|---|---|
| **PD1** | 同一 `(Seed, 显式参数, 世界坐标)` ⇒ 任意时刻、任意顺序、任意机器状态下结果完全一致 |
| **PD2** | 每一层的产出必须**可独立重复求值**（同输入同输出，无副作用） |
| **PD3** | 不得依赖加载顺序、邻居是否已求值、帧序、tick 次数、已加载集合 |
| **PD4** | 不得使用非确定性来源：`UnityEngine.Random` / `System.Random` / `Time` / `DateTime` / `Guid` / 平台相关噪声（继承地质规范 §8.2 D1–D6 与 `WorldState_Design.md` §3.7 MX3） |
| **PD5** | 不得依赖 `Dictionary` / `HashSet` 的遍历顺序（若叠加顺序影响结果，即为缺陷） |
| **PD6** | 所有参与判定的量必须**显式**（禁止隐藏全局状态与隐式默认） |
| **PD7** | 钳制规则必须显式（参数落在定义域内，`WorldState_Design.md` §1.3 规则 1） |

### 3.3 **域分离**（本 Pipeline 的核心确定性机制）

> **每个系统必须由 `Seed + Position + Domain` 独立计算。**

| # | 规则 |
|---|---|
| **PE1** | 每个系统的每个输出都必须由**三元组**唯一确定：`(Seed, Domain, Position)` |
| **PE2** | `Domain` 是**显式域标签**（上表），用于隔离不同系统的确定性来源 |
| **PE3** | **不同系统绝不能共享随机状态或随机序列**：不得出现"先用一个随机序列生成历史，再把同一个序列继续给地形用" |
| **PE4** | 禁止"先创建 A，再由 A 的状态决定 B 的随机序列"（这会让 B 依赖 A 的求值顺序，破坏 `PD3`） |
| **PE5** | 新增层只能**新增域标签**；**不得修改既有域**（尤其不得改动既有地形噪声域，否则会破坏既有世界的逐体素一致性） |
| **PE6** | 域标签必须与**空间位置**共同参与：仅靠 `Seed`（与世界坐标无关）产生的常量不构成世界分布 |
| **PE7** | 域分离必须**可机械验证**：同 `Seed` 不同域 ⇒ 输出独立；同域同坐标 ⇒ 输出恒等 |

### 3.4 为什么必须域分离

| 理由 | 说明 |
|---|---|
| **可独立求值** | 域分离后，任一坐标的任一系统输出可单独计算，不需要"先跑完上游系统"（`PB2`） |
| **防止顺序耦合** | 共享序列会把"求值顺序"变成结果的一部分，直接破坏确定性 |
| **隔离演化** | 未来某层修改（例如历史规则调整）不会连带改变地形与区域分布 |
| **可验证** | "同域同坐标恒等"是可机械验证的强断言（附录 D `PG2`） |

---

## §4 World History 层

### 4.1 设计原则：历史不是实时模拟

| # | 规则 |
|---|---|
| **PH1** | **历史不是实时模拟**：不存在"每隔 N 年推进一次"的世界时钟，不存在随时间演化的历史状态 |
| **PH2** | **V1：`Seed` 决定历史结果**：历史是"已经发生的事实"，由 `(Seed, Domain, Position)` 确定性派生 |
| **PH3** | 历史**不产生**任何对象（不生成建筑、不生成 NPC、不生成物品），只产生**事实与候选** |
| **PH4** | 历史**不读取**下游（不得读 Region / Biome / 地形细节之外的内容；`PA5`） |
| **PH5** | 历史**不得**依赖 Runtime 状态（不读 Chunk、不读加载集合、不读 tick） |

### 4.2 V1 覆盖的历史内容

| 类别 | 示例 | 说明 |
|---|---|---|
| **文明** | 文明的存在与偏好归属（例如"某区域属于某文明的活动范围"） | 只作为事实与范围依据 |
| **战争** | 魔法战争、文明冲突 | 作为"历史事件"事实存在 |
| **魔法事件** | 远古魔法场址形成、封印、法师战争 | 作为区域来源候选 |
| **灾难** | 亡灵灾难、污染扩散的历史结果 | 作为区域来源候选 |
| **圣恶事件** | 圣地形成、腐化爆发 | 作为区域来源候选 |

> 上述内容在 V1 中**只作为事实**存在：**不模拟过程、不推进时间、不产生对象**。

### 4.3 输出（唯一）

| 输出 | 含义 | 下游消费者 |
|---|---|---|
| **History Tags** | 只读的历史事实标签（类型 / 位置 / 序位语义） | Region 层（`Region_Design.md` §6.4 允许读取方）；未来系统（§9 只读） |
| **Region Source 候选** | 潜在的区域来源（位置 + 类别 + 强度倾向 + 尺度倾向） | Region 层（§5，由 Region 层**采纳**为 Region Source） |

**硬性**

| # | 规则 |
|---|---|
| **PI1** | 历史只产出**事实与候选**；Region Source 的**采纳与归属**属于 Region 层（单一权威） |
| **PI2** | 候选的产生必须满足 `RS2`/`RS3`（确定性、可局部枚举） |
| **PI3** | `HistoryTags` 不得被环境参数求值读取（`Region_Design.md` §6.4 RH3）；参数只能来自 `Base + RegionModifier` |
| **PI4** | 历史产物不得被用于生成任何对象（只产出资格与依据） |

### 4.4 禁止事项

| # | 禁止 |
|---|---|
| **PJ1** | **直接生成建筑**（房屋、城市、道路、工坊） |
| **PJ2** | **直接生成 NPC**（角色、生物、种群、行为） |
| **PJ3** | 直接放置遗迹结构、放置方块、修改体素 |
| **PJ4** | 实时推进历史（事件随 tick 发生/扩散/结束） |
| **PJ5** | 由下游结果反推历史（例如"看到腐化区域 → 补写亡灵灾难"） |

---

## §5 Region 生成层

### 5.1 定义

> **Region 是连续空间中的世界状态解释单元**（`Region_Design.md` §1.1）。Region **不是 Biome**：**Region 是原因**。

### 5.2 输入 / 输出

| 方向 | 内容 |
|---|---|
| **输入** | `Seed` + **History**（只读：`HistoryTags`、Region Source 候选）+ **Natural Rules**（自然规则）+ 地形派生量（`Elevation` / `Ocean`，**只读**） |
| **输出** | **Region**：来源 + `RegionModifier` + 标签 + 资格 |

### 5.3 覆盖的 Region 类型（V1）

| 类别 | 来源 | 影响的主要参数 |
|---|---|---|
| **魔法源** | AncientMagicSite / MagicCrystalDeposit / AncientWizardBattle / PowerfulCreatureDeath / MagicSeal | `MagicDensity` |
| **腐化源** | 墓地 / 邪恶遗迹 / 污染区域 / 灾难事件 | `CorruptionInfluence` |
| **圣地** | 圣物 / 圣地 / 圣生物聚集 | `HolyInfluence` |
| **自然区域** | 大面积气候 / 海洋 / 山脉 / 河流 / 土壤 | `Temperature` / `Moisture` / `Fertility` |
| **遗迹区域** | 文明偏好 + 活动范围 + 历史事件 | 不修改参数：产出**遗迹候选区域**（资格） |

### 5.4 规则（继承上游，本文档只做管线约束）

| # | 规则 | 出处 |
|---|---|---|
| **PK1** | **无源不成区**：每个 Region 必须能回溯到至少一个 Region Source | `Region_Design.md` RG-A / RS1 |
| **PK2** | Region **只提供 Modifier**：不得直接指定 Biome、不得修改地形量（`Elevation` / `Ocean` 不在修正集内） | `Region_Design.md` RE1 / RM4 / RB2 |
| **PK3** | Region 的修正按**加法 + 钳制**叠加，且必须满足**交换律** | `Region_Design.md` RO1 / RO4 |
| **PK4** | Region 的影响必须**随距离下降**、紧支撑、边界连续趋于 0 | `Region_Design.md` RBD1–RBD3 |
| **PK5** | Region 的来源与修正必须**可局部枚举**、可独立求值 | `Region_Design.md` RS3 / RP2 |
| **PK6** | Region 的**类型**只是解释标签，**无规则权** | `Region_Design.md` RTY1–RTY5 |
| **PK7** | `RegionView`（由最终环境反查的空间范围）**不得回写任何参数**，也不得被用来反推修正量 | `Region_Design.md` RT6 / RC2 |
| **PK8** | 圣与恶的"抵消"只经**派生量** `Balance = HolyInfluence − CorruptionInfluence` 表达 | `Region_Design.md` §9.3 / RO2 |

### 5.5 说明（与需求的直接对应）

| 命题 | 结论 |
|---|---|
| Region 是不是 Biome？ | **不是**。Biome 是环境表现（§7），Region 是世界原因（§5） |
| Region 的位置从哪来？ | 从**来源**（自然规则 / 历史事实 / 魔法与圣恶来源）确定性派生 |
| Region 会不会"随机出现"？ | **禁止**（`PK1`） |
| Region 会不会直接给出群系？ | **禁止**（`PK2`）；只能通过修改环境参数间接影响 |

---

## §6 WorldState 计算

### 6.1 定义

```
FinalState(p) = BaseWorldState(p) + RegionModifier(p)
```

（形式与语义归 `WorldState_Design.md` §2.1 与 `Region_Design.md` §2.1；本文档把它落到 **Phase 4**。）

### 6.2 参数（8 个）

| # | 参数 | Phase 4 中的角色 | 是否可被 Region 修正 |
|---|---|---|---|
| 1 | **Temperature** | 列属性 | ✅ `TemperatureModifier` |
| 2 | **Moisture** | 列属性 | ✅ `MoistureModifier` |
| 3 | **Elevation** | 世界坐标高度（复用既有 `SurfaceHeight`） | ❌ **只读**（`RE1`） |
| 4 | **Fertility** | 派生属性 | ✅ `FertilityModifier` |
| 5 | **Ocean** | 由 `Elevation` 与 `SeaLevel` **派生** | ❌ **只读**（`RE1`） |
| 6 | **MagicDensity** | 三维场（地表点取值用于分类） | ✅ `MagicDensityModifier` |
| 7 | **HolyInfluence** | 三维场 | ✅ `HolyInfluenceModifier` |
| 8 | **CorruptionInfluence** | 三维场 | ✅ `CorruptionInfluenceModifier` |

### 6.3 规则

| # | 规则 |
|---|---|
| **PL1** | **Region 只提供 Modifier**；`BaseWorldState` 由骨架与基础规则提供（Phase 1 + 既有规则） |
| **PL2** | 环境参数只有**一条**权威路径：`Base + RegionModifier`（`Region_Design.md` RM5 / 附录 B RG9） |
| **PL3** | **禁止 Region 直接指定 Biome**（`RB2`）：Phase 4 的输出只有 8 个参数，不含任何群系标识 |
| **PL4** | 三维能量场用于列判定时，必须在**地表点**取值（`WorldState_Design.md` §1.3 规则 4） |
| **PL5** | 所有参数必须**钳制**在定义域内；修正量为 0 时必须与"无 Region"逐值等价（`RM3`） |
| **PL6** | Phase 4 不得读取 Biome / 生态 / 遗迹 / 任何下游产物（`P-2`） |

---

## §7 Biome Resolution

### 7.1 定义

```
WorldState（FinalState） → Biome
```

**Biome 是结果**：由环境参数向量经**唯一有序规则表**判定（`WorldState_Design.md` §5.3，first-match-wins）。

### 7.2 V1 的群系集合（对规则表输出集合的描述）

| Biome | 判定要点（对应规则表分支） |
|---|---|
| **Ocean** | `Ocean == true`（**短路**：不再参与后续判定） |
| **CorruptedLand** | 腐化高 **且** 肥力低 |
| **HolyLand** | 圣性高 **且** 肥力高 |
| **MagicForest** | 魔力高 **且** 温度适宜 **且** 湿度高 |
| **Forest** | 温度中等 **且** 湿度高 |
| **Desert** | 低湿 / 低肥分支（**必须归属**规则表，见 `PM3`） |

**硬性**

| # | 规则 |
|---|---|
| **PM1** | 群系判定**只能**读取 `FinalState`（在 `PB1` 意义上的一次局部求值）与显式参数 |
| **PM2** | 群系判定**只有一套权威**（`RB3`）：本 Pipeline **不得**定义第二套规则或第二套阈值 |
| **PM3** | 上表是对该规则表**输出集合**的描述；若某个群系（例如 `Desert`）在该规则表中尚无显式分支，**必须在 `WorldState_Design.md` 的规则表内定义**（属该规范的变更），**Pipeline 不新增分支** |
| **PM4** | **Biome 只是生态分类，不是原因**：任何层都不得由 Biome 反推参数、创建 Region/Source、或改写历史（`P-1`） |
| **PM5** | 群系的**表现边界**（唯一影响 = 地表材料）仍由 Geology 侧既有规则决定，Pipeline 不放宽（§8） |
| **PM6** | 群系结果不得写回参数（`RT6`） |

---

## §8 Geology 连接

### 8.1 说明：Biome 如何有限影响地表材料

```
Biome ──(只读)──► P3 Material 判定（地表层）
                    └── 地表材料 ∈ {Grass, Dirt, Stone}
                        （只作用于 y == SurfaceHeight 的那一层体素）
```

| # | 规则 | 出处 |
|---|---|---|
| **PN1** | 群系只影响**地表材料选择**；取值限既有的 `{Grass, Dirt, Stone}`（`Air` 不作为地表材料） | `WorldGeneration_Geology_Runtime_Implementation.md` §6.1 |
| **PN2** | 影响范围**仅限地表层**（`y == SurfaceHeight`）；不得扩展到其它层 | 同上 §6.1 B1 |
| **PN3** | 群系必须**可关闭**：关闭后必须与既有基线逐体素一致（退化兼容） | `WorldState_Design.md` §5.8 / WS11 |
| **PN4** | 该路径是"环境 → 体素"的**唯一允许路径** | `Region_Design.md` RI1–RI4 |

### 8.2 限制：不能改变什么

| 不得改变 | 说明 |
|---|---|
| **Density** | 实体/空判定（优先级链 **P0 WorldBounds → P1 Cave → P2 Terrain**）与群系、Region、历史**完全无关** |
| **Cave** | 洞穴（`CaveDensity` 与 P1 覆盖语义）不得被群系/Region/历史影响 |
| **Chunk** | Chunk 划分、坐标语义、加载/卸载、脏标记、预算：**Pipeline 不涉及**（`P-2`） |
| **Voxel 规则** | 不新增 `VoxelType`、不改变体素状态集合（V1 冻结为 Air/Grass/Dirt/Stone）、不改变写入顺序与边界规则 |

### 8.3 保持 Geology 独立

| # | 规则 |
|---|---|
| **PO1** | Geology **不认识** History / Region / `RegionModifier` / 生态资格：它的输入只有世界坐标、显式地质参数与（P3 用途的）Biome 结论 |
| **PO2** | Geology **不得反向影响**任何上游（不得改 WorldState、不得改 Region、不得改 History） |
| **PO3** | Pipeline 的 Phase 6 **就是**既有地质判定链；不得在它之外新增地形路径 |
| **PO4** | 关闭新增层（History / Region / 能量参数）后，Phase 6/7 的输出必须与今天**逐体素一致** |
| **PO5** | 零修改：既有地质实现（含被 SHA 冻结的 9 个地质文件）不得因本 Pipeline 而被修改；若必须改动，走显式授权 + 级联重钉（`Region_Design.md` RV10） |

---

## §9 未来系统接口边界

### 9.1 定位

`Structure` / `Entity` / `Creature` / `Quest` / `NPC` 都是**消费者**（读取者），不是生成链的一环。

### 9.2 允许的读取（只读）

| 系统 | 可读 | 用途（示例） |
|---|---|---|
| **Structure** | History（事实/标签）、Region（遗迹候选区域）、Biome | 决定"在哪里放置什么结构" |
| **Entity** | Region（来源与影响）、Biome、WorldState（只读参数） | 决定"此处允许什么对象存在" |
| **Creature** | Biome、WorldState（只读）、Region（只读） | 决定"此处适合什么生物生存" |
| **Quest** | History（事实/标签）、Region、Biome | 决定"可发生什么剧情与目标" |
| **NPC** | History（事实）、Region（只读）、Biome | 决定"谁在此处活动" |

### 9.3 禁止

| # | 禁止 |
|---|---|
| **PP1** | **这些系统修改世界生成结果**：不得改环境参数、不得改 Region、不得改 History、不得改 Biome |
| **PP2** | 不得写回体素真值以外的世界生成数据；**不得**把对象状态写进 Voxel 数据（ADR-001：Entity 不属于 Chunk 所有权体系） |
| **PP3** | 不得成为上游的依据（禁止"因为生成了某个 NPC，所以这里魔力更高"） |
| **PP4** | 不得改变确定性：这些系统的存在与运行不得影响任何阶段的输出 |
| **PP5** | 不得依赖"世界级全量状态"：只能按坐标/范围做局部只读查询（`PB2`） |
| **PP6** | 不得要求 Runtime/Chunk/Mesh 侧的特殊处理（Pipeline 与这些系统之间的耦合只能通过**只读查询**） |

### 9.4 边界图

```
Pipeline（本文档）
  Seed → Skeleton → History → Region → WorldState → Biome → Geology → Voxel
                                                                          │（只读）
   ┌──────────────────────────────────────────────────────────────────────┘
   ▼
未来系统（只读消费者）
  Structure / Entity / Creature / Quest / NPC
      └── 产出：对象与内容（不属于 Pipeline；不得回写世界生成数据）
```

---

## §10 有限大陆与世界尺度

### 10.1 结合已有设计

> **保持** `WorldState_Design.md` §7.3 与 `Region_Design.md` §10.3 的推荐：**无限海洋 + 有限大陆**。

```
水平世界：保持无限（Chunk 空间无边界）
垂直世界：沿用既有 WorldHeight
陆地：    由大陆函数限定的有限块（大陆 A / 大陆 B / …）
大陆之外：海洋（可无限延伸）
```

### 10.2 为什么适合事件系统（Pipeline 视角）

| # | 论证 |
|---|---|
| **1** | **Phase 2（History）需要舞台**：历史事件必须有与世界范围可比的舞台；无限世界里"世界级事件"没有边界 |
| **2** | **事件的影响必须收敛**：有限大陆中"事件影响了哪些区域"是可枚举的，才能被 Phase 3 采纳为 Region Source |
| **3** | **Region 来源集合必须有界**：Phase 3 的叠加必须可枚举（`PK5`），来源无界时无法自然定义 |
| **4** | **可解释性**：有限世界能回答"为什么这里没有遗迹"（超出所有文明活动范围） |
| **5** | **跨大陆语义**：海洋隔离使"大陆 A = 玩家区域、大陆 B = 其他文明"成为有意义的目标 |
| **6** | **零修改**：陆地有限但**水平世界仍无限** ⇒ 不引入硬边界 ⇒ Streaming / Runtime / Chunk 语义不变（`PC5`） |

### 10.3 限制

| # | 规则 |
|---|---|
| **PQ1** | 骨架 Stage（Phase 1）必须**可退化**：掩码退化为全陆地时，世界回到既有无限地形 |
| **PQ2** | 不得引入"世界边界特例"到任何下游阶段（Region/Biome/Geology 都不得有"因为到边界了"的分支） |
| **PQ3** | 大陆元数据属于骨架/历史标签，**不是** Chunk 数据，也不是 Region 容器 |
| **PQ4** | 大陆生成**只在 Phase 1**；其它阶段不得重新定义大陆 |

---

## §11 V1 范围

### 11.1 V1 实现顺序（四步）

| 顺序 | 步骤 | 内容 | 关键约束 |
|---|---|---|---|
| **1** | **WorldState** | 8 个参数的求值：`Base + RegionModifier`（修正可为 0） | 参数语义归 `WorldState_Design.md`；`Elevation` 复用既有高度场；可关闭 |
| **2** | **Region Query** | `位置 → RegionModifier`（只读查询，不接入生成） | 无源不成区；可局部枚举；交换律；类型无规则权 |
| **3** | **Biome** | `FinalState → Biome`（复用唯一规则表） | 不新增第二套规则表；可关闭 ⇒ 退化一致 |
| **4** | **Geology 接入** | 把 Biome 结论接入**既有** P3 地表材料规则 | 只影响地表层材料；不改 `Density`/`Cave`；零修改既有实现 |

> 既有实现现状：地形（Stage 1）与地质（Stage A–E：地层/洞穴/矿藏/群系接口）**已经存在**；
> 上述四步指**新增层**的落地顺序，且每一步都必须能与既有验收套件共存（附录 D）。

### 11.2 V1 暂不实现

| # | 暂不实现 | 说明 |
|---|---|---|
| 1 | **动态战争** | 圣与恶不做传播/侵蚀/净化/胜负演化（`Region_Design.md` RO3、`WorldState_Design.md` §4.5 HP2） |
| 2 | **生态演化** | 不模拟物种演化、适应、种群变化 |
| 3 | **NPC 历史模拟** | 不模拟 NPC 行为与文明兴衰过程 |
| 4 | **世界实时变化** | 不存在"随时间推进的世界状态"；管线是纯函数组合（`PH1`、`PD3`） |

### 11.3 硬性禁令（Pipeline 级）

| # | 禁令 |
|---|---|
| **PR1** | 不创建 `.cs`、不定义接口/类/结构体（本文档性质） |
| **PR2** | 不修改任何已有文件（含本 Pipeline 的四个上游规范） |
| **PR3** | 不新增 `VoxelType`；不修改 VoxelCore 与体素状态集合 |
| **PR4** | 不让任何下游反向影响上游（附录 C） |
| **PR5** | 不引入第二套权威（参数 / 群系 / 地形 / 区域） |
| **PR6** | 不把 Chunk / Mesh / Entity / Runtime 的实现纳入本文档范围 |

---

## 附录 A：完整数据流图

### A.1 端到端数据流（含域标签）

```
                       ┌───────────────────────────────┐
                       │            Seed               │
                       └───────────────┬───────────────┘
                                       │
        ┌──────────────────────────────┼──────────────────────────────┐
        │ Domain=Skeleton              │ Domain=History               │
        ▼                              ▼                              │
┌──────────────────┐          ┌──────────────────────┐                │
│ Phase 1          │          │ Phase 2              │                │
│ World Skeleton   │──舞台───►│ World History        │                │
│ 大陆/海洋/基础气候│  (只读)  │ 事件/文明/灾难/圣恶   │                │
└────────┬─────────┘          └──────────┬───────────┘                │
         │ 框架                           │ HistoryTags              │
         │                                │ + Region Source 候选     │
         │                                ▼                          │
         │                     ┌──────────────────────┐              │
         │                     │ Phase 3              │              │
         │                     │ Region Generation    │              │
         │                     │ 来源 + Modifier + 标签│              │
         │                     └──────────┬───────────┘              │
         │  Base 框架                     │ RegionModifier           │
         └───────────────────────────────►▼                          │
                              ┌──────────────────────┐                │
                              │ Phase 4              │◄───────────────┘
                              │ WorldState Evaluation│  （Base 由 Phase 1 提供）
                              │ FinalState = Base + M│
                              └──────────┬───────────┘
                                         │ FinalState（8 参数）
                                         ▼
                              ┌──────────────────────┐
                              │ Phase 5              │
                              │ Biome Resolution     │（唯一有序规则表）
                              └──────────┬───────────┘
                                         │ Biome（只读，且仅用于 P3）
                                         ▼
                              ┌──────────────────────┐
                              │ Phase 6  Geology     │
                              │ P0 → P1 → P2 → P3    │
                              └──────────┬───────────┘
                                         │ Density/Material/Cave/Deposit
                                         ▼
                              ┌──────────────────────┐
                              │ Phase 7  Voxel Output│
                              │ 世界坐标 → 体素状态   │
                              └──────────┬───────────┘
                                         │ （只读）
                                         ▼
                       未来系统：Structure / Entity / Creature / Quest / NPC
                                （只读消费者；不得回写生成数据）

   ══════════════ 反向箭头一律禁止（附录 C）══════════════
```

### A.2 判定链可解释性（示例）

| 提问 | 判定链 |
|---|---|
| "为什么这里是魔法森林？" | `Phase 2` 历史事实（例如远古法师战争）→ `Phase 3` 魔法来源 → `Phase 4` `MagicDensity` 高 → `Phase 5` 规则表命中（魔力高 + 温度适宜 + 湿度高）→ `魔法森林` |
| "为什么这里是腐化荒地？" | `Phase 2` 灾难事实 → `Phase 3` 腐化来源 → `Phase 4` `CorruptionInfluence` 高、`Fertility` 低 → `Phase 5` 规则表命中 → `腐化荒地` |
| "为什么这里的地表是裸岩？" | `Phase 5` 群系结论 → `Phase 6` **P3** 地表材料规则（限 `{Grass, Dirt, Stone}`）→ 裸岩 |
| "为什么这里有洞？" | `Phase 6` **P1** 洞穴覆盖（`CaveDensity ≥ 阈值`）——**与群系、Region、历史无关** |
| "为什么这里是实体/空？" | `Phase 6` **P0 → P2**（世界边界 → 地形）——**与群系、Region、历史无关** |

---

## 附录 B：职责权限矩阵

### B.1 阶段 × 权限

| Phase | 产出 | 可读上游 | **禁止读取** | 是否可写 Voxel | 是否接触 Runtime/Chunk/Mesh/Entity |
|---|---|---|---|---|---|
| 0 Seed | 种子 | — | 一切 | ❌ | ❌ |
| 1 Skeleton | 大陆/海洋/气候框架 | Seed | History / Region / WorldState / Biome / Geology / Voxel | ❌ | ❌ |
| 2 History | HistoryTags、Source 候选 | Seed、骨架（只读） | Region / WorldState / Biome / Geology / Voxel | ❌ | ❌ |
| 3 Region | Region + Modifier | Seed、History、自然规则、`Elevation`/`Ocean`（只读） | WorldState 结果 / Biome / Geology / Voxel / 已生成内容 | ❌ | ❌ |
| 4 WorldState | FinalState（8 参数） | Base（骨架+基础规则）、RegionModifier | HistoryTags、Biome、生态、对象、Voxel | ❌ | ❌ |
| 5 Biome | Biome | FinalState | Region 内部属性 / 来源列表 / Geology / Voxel | ❌ | ❌ |
| 6 Geology | Density/Material/Cave/Deposit | 世界坐标、显式地质参数、Biome（仅 P3） | History / Region / WorldState / Voxel | ❌（只判定） | ❌ |
| 7 Voxel Output | 体素状态 | Phase 6 判定结果 | 一切下游 | ✅（唯一写入者） | ❌ |

### B.2 系统 × 权限（跨层总览）

| 系统 | 可读 | 可写 | 绝不接触 |
|---|---|---|---|
| **Pipeline 各阶段** | 见 B.1 | 各自的产出 | Runtime / Chunk / Mesh / Entity |
| **Geology** | 世界坐标、地质参数、Biome（P3） | `Density`/`Material`/`CaveDensity`/`DepositZone` | History / Region / RegionModifier |
| **Runtime / Streaming** | `IWorldGenerator` 与生成结果 | Chunk 生命周期 | **Pipeline 内部概念（完全不知道）** |
| **Mesh / View** | Voxel 数据 / 呈现缓冲 | 几何 / 表示 | Pipeline |
| **Structure / Entity / Creature / Quest / NPC** | History / Region / Biome / WorldState（只读） | 自身对象与内容 | 世界生成数据（不得回写） |

### B.3 单一权威表

| 权威对象 | 唯一权威 |
|---|---|
| 体素状态集合 / Chunk 契约 | `VoxelWorld_Core.md` |
| 生成契约 / 确定性 / stage 划分 | `WorldGeneration_Core.md` |
| 地形 / 岩层 / 洞穴 / 材料 / 矿藏属性 | `WorldGeneration_Geology*.md` |
| 环境参数（8 个）与力量规则 | `WorldState_Design.md` |
| 群系解析规则表 | `WorldState_Design.md` §5.3 |
| Region 的来源 / 修正 / 标签 / 资格 | `Region_Design.md` |
| **层间顺序与数据流** | **本文档** |

---

## 附录 C：禁止反向依赖列表

| # | 禁止的反向依赖 | 反例（必须判为缺陷） | 机械可验证形式 |
|---|---|---|---|
| **RB-1** | **Biome → Source** | 由群系结论反推"这里应该有魔法源" | 结构扫描：群系判定路径不得引用来源/Region |
| **RB-2** | **看到魔法森林 → 创建魔法源** | 由 `Biome == MagicForest` 触发来源生成 | 同上；来源的产生只能来自 Phase 2/3 的输入 |
| **RB-3** | **Geology → History** | 由地形结果补写历史事件 | 结构扫描：History 产物不得被 Geology 读取，Geology 不得写历史 |
| **RB-4** | **Biome → Region** | 由群系创建/修改 Region | 结构扫描：Region 的产出不得引用 Biome |
| **RB-5** | **Voxel 生成 → WorldState** | 由已生成体素回推/改写环境参数 | 结构扫描：Phase 4 不得读取体素数据或已生成内容 |
| **RB-6** | Region → 已生成内容 | Region 读取"这个 Chunk 是否已加载/体素是什么" | 结构扫描：Region 阶段不得读取 Voxel/Chunk 状态 |
| **RB-7** | WorldState → Region / Biome | 参数求值读取 Region 类型或群系结果 | 结构扫描：`Base + Modifier` 求值路径只允许两个输入 |
| **RB-8** | Geology → Biome | 地质结果反向决定群系 | 结构扫描：Phase 5 的输入只有 `FinalState` |
| **RB-9** | 未来系统 → 世界生成数据 | 对象/NPC/任务回写参数、Region、历史、群系 | 结构扫描：未来系统只能只读查询，无写回路径 |
| **RB-10** | 阶段 k → 阶段 k+1…7 的任何产物 | 例如 Phase 3 读取 `FinalState` | 依赖方向矩阵（附录 B.1）逐格校验 |
| **RB-11** | Region 类型参与规则 | `RegionType == MagicForest` 被用作判定条件 | 结构扫描：类型标签只出现在调试/标签路径 |
| **RB-12** | Region 直接指定 Biome | Region 产出中携带群系标识 | 结构扫描：Phase 3 的产出不含群系标识（`PL3`） |
| **RB-13** | 系统间共享随机状态 | 一个随机序列被多个系统连续消费 | 检查每个系统的确定性输入是否为 `(Seed, Domain, Position)` |
| **RB-14** | 结果回写上游参数 | 把群系/生态/遗迹结论写回环境参数或区域修正 | 结构扫描：上游产出的写入点必须唯一 |

**共同判据**：任何一条被违反，都会同时破坏**确定性**（§3）、**可解释性**（附录 A.2）与**既有验收**（附录 D `PG12`）。

---

## 附录 D：未来实现阶段建议

> 本附录只给**建议顺序与共同硬性检查**，本轮不实现任何一项。
> 落地必须另立实现规范（本文档不给出任何类型/接口/参数结构草案）。

### D.1 阶段建议（与 §11.1 的四步对应）

| 阶段 | 内容 | 对应步骤 | 关键约束 |
|---|---|---|---|
| **PG-A** | WorldState 参数层（Base 5 参数 + 派生 `Ocean`/`Fertility`） | 1 | `Elevation` 复用既有高度场；修正可为 0；可关闭 |
| **PG-B** | Region 查询层（Source + Modifier → `FinalState`） | 2 | 无源不成区；交换律；可局部枚举；类型无规则权 |
| **PG-C** | Biome 解析（复用唯一规则表） | 3 | 不新增第二套规则；可关闭 ⇒ 退化一致 |
| **PG-D** | Geology 接入（既有 P3 地表材料路径） | 4 | 只影响地表层材料；不改 `Density`/`Cave`；零修改 |
| **PG-E** | History 事实层（HistoryTags / Source 候选） | 前置（可最小化） | 只产出事实与候选；不生成对象；不推进时间 |
| **PG-F** | 大陆骨架（Phase 1，可选，最后） | 可选 | 掩码叠加；可退化为全陆地；不引入硬边界 |

> **V1 的最小可用形态**：`PG-A → PG-B → PG-C → PG-D`。
> 在 `PG-E` 未实现前，Region 来源可以只由**自然规则 + `Seed` 派生的固定事实**驱动（仍满足"无源不成区"）。

### D.2 Pipeline 验收不变量

| # | 不变量 | 验证方法 |
|---|---|---|
| **PG1** | **Seed 决定性**：同 `Seed` ⇒ 同世界（区域/历史/魔法源/圣恶/遗迹候选/群系/地形/体素全部一致）；不同 `Seed` ⇒ 至少一处不同（反例保护） | 双世界逐体素 + 内容哈希对照 + 非平凡性断言 |
| **PG2** | **域分离**：每个系统的输出由 `(Seed, Domain, Position)` 唯一确定；改变某域不得影响其它域 | 分域对照（改一个域的种子/参数，其余输出必须逐值不变） |
| **PG3** | **阶段顺序不可交换**：依赖方向矩阵（附录 B.1）逐格为"只读上游" | 结构扫描 + 依赖矩阵校验 |
| **PG4** | **纯函数性**：每个阶段重复求值结果恒等 | 同坐标重复求值 N 次逐值比较 |
| **PG5** | **局部可求值**：任意坐标可独立求完整链，无需遍历全世界 | 求值范围检查（无全量遍历路径） |
| **PG6** | **无共享可变状态 / 无共享随机序列** | 结构扫描（无跨阶段可变状态；无共享序列） |
| **PG7** | **单一权威**：参数只来自 `Base + Modifier`；群系只有一套规则表；地形只有一条判定链 | 结构扫描（无第二套路径） |
| **PG8** | **退化兼容**：关闭 History / Region / 能量参数后，Phase 6/7 输出与今天**逐体素一致** | 与既有基线（Stage 1 / Stage C / Stage D）逐体素 + 内容哈希比较 |
| **PG9** | **地形不变**：Region / 群系 / 历史不影响 `Density` / `CaveDensity`（群系仅影响 P3 地表材料） | 开关前后的体素快照差异集合 ⊆ 允许集合（仅地表层材料） |
| **PG10** | **唯一地表材料路径**：影响体素的路径仅 `FinalState → 群系 → P3 地表材料` | 结构扫描 + 差异集合检查 |
| **PG11** | **无新增 VoxelType**：不引入任何体素状态 | 结构性检查（类型集合冻结） |
| **PG12** | **零修改**：VoxelCore / WorldGeneration / Geology 9 个实现文件 / Runtime / Streaming / Mesh / View / Bootstrap 的 SHA 不变 | 文件哈希比对（既有验证入口做法） |
| **PG13** | **可解释性**：任意坐标的结论都能回溯完整判定链（附录 A.2） | 判定链问答 + 结构扫描（无孤立分支） |
| **PG14** | **无反向依赖**：附录 C 的 RB-1…RB-14 全部成立 | 结构扫描（反向读取路径为 0） |

### D.3 每阶段的共同硬性检查

1. 既有验收套件（VoxelCore / WorldGeneration / Mesh / View / RuntimeCore / Streaming / Geology Stage A–E）全部保持 PASS；
2. `PG12` 的 SHA 冻结集合不变；若必须改动被冻结文件，走**显式授权 + 级联重钉**流程；
3. 不使用任何非确定性来源（`PD4`）；
4. 不新增 `VoxelType`、不修改 VoxelCore（`PR3`）；
5. 每阶段都必须**可关闭**，且关闭后逐体素退回既有行为（`PG8`）。
