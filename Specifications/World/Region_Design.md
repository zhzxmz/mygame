# Region 系统设计规范 V1

> **文档性质**：**设计规范（Design Specification）**。本文档只描述五类内容：**概念 / 数据关系 / 规则 / 约束 / 验收标准**。
> **不含任何代码、不含接口/类/结构体草案**；不接入 Runtime；不实现生成逻辑；**不修改任何已有文件**。
>
> **定位**：Region 层位于 **WorldState（环境参数层）** 与 **Geology（地形/岩层/洞穴/材料层）** 之间并统辖两者的解释关系：
> 它把**原因**（自然 / 魔法 / 圣恶 / 历史来源）翻译为**环境修正**，再由统一的环境与群系规则产生**表现**。
>
> **本文档的直接上游（不得冲突）**：
> * `Specifications/World/WorldState_Design.md` —— 8 个环境参数、因果方向纪律（§1.5）、静态场规则（§3）、圣与恶（§4）、群系规则与退化兼容（§5/§5.8）；
> * `Specifications/World/WorldGeneration_Geology.md` —— 地形/岩层/洞穴/材料规则（`Density` / `Material` / `CaveDensity` / `DepositZone` / 群系接口）；
> * `Specifications/World/WorldGeneration_Geology_Implementation.md`、`WorldGeneration_Geology_Runtime_Implementation.md` —— 地质采样与生成器的落地边界；
> * `Specifications/World/VoxelWorld_Core.md`、`WorldGeneration_Core.md` —— 世界真值与生成契约（体素状态冻结、Seed + 坐标纯函数、Chunk 计算独立）；
> * `Specifications/World/VoxelWorld_Runtime*.md`、`VoxelWorld_Mesh.md`、`VoxelWorld_View.md`、`VoxelWorld_Mining.md`、`VoxelWorld_Architecture.md`；
> * `Specifications/ArchitectureDecisionWorldSpaceEntity.txt`（ADR-001）。
>
> **冲突时的优先级**：**真值**以 `VoxelWorld_Core.md` 为准；**生成契约**以 `WorldGeneration_Core.md` 为准；
> **地形/岩层/洞穴/材料**以 `WorldGeneration_Geology*.md` 为准；**环境参数与力量规则**以 `WorldState_Design.md` 为准；
> **调度**以 `VoxelWorld_Runtime*.md` 为准；**区域的来源/修正/标签/资格**以本文档为准。
>
> **零修改前提（硬性）**：本文档的一切内容都必须能在**不修改** VoxelCore、Geology 实现（含被冻结的 9 个地质文件）、
> WorldGeneration 既有实现、Mesh、View、Runtime Core、Streaming、Bootstrap 的前提下落地为**新增层 / 新增文件**。
>
> **V1 状态**：仅设计（§11）。本文档不产生任何代码、不生成任何世界数据、不改变任何既有行为。

---

## 世界结构中的位置

```
World
 |
 ├── History Layer          （历史事件：原因层）
 |
 ├── Region Layer           （世界状态的解释单元：本文档）
 |       |
 |       ├── Sources            （区域来源：原因记录）
 |       ├── WorldState Modifier（对 8 个环境参数的修正）
 |       ├── Biome              （把最终环境向量交给**唯一**群系解析规则）
 |       ├── History Tags       （只读历史标签）
 |       └── Ecology Rules      （资格声明：允许什么，不生成对象）
 |
 └── Geology                （地形 / 岩层 / 洞穴 / 材料）
```

**Region 不负责（硬性）**：生成 Chunk、生成 Mesh、创建 Entity、修改 Runtime。

---

## 1. Region 定义

### 1.1 定义

> **Region 是连续空间中的世界状态解释单元。**

它回答的是：**"这片空间的在世界逻辑上属于什么、它把环境参数修正成什么样、因此什么被允许"**。

| 结构 | 回答的问题 | 层次 |
|---|---|---|
| **Geology** | 这个坐标是实体还是空？是什么材料？有没有洞？有什么资源？ | 空间数据（真值层） |
| **WorldState** | 这个坐标的环境参数是多少？（8 个参数） | 环境状态（参数层） |
| **Region** | 这片空间的**原因是什么、修正多少、允许什么**？ | 世界逻辑解释（解释层） |

### 1.2 Region 不是什么

| Region **不是** | 说明 |
|---|---|
| 不是 `GameObject` | Region 不拥有生命周期、不拥有渲染/碰撞；不是场景对象 |
| 不是 Chunk | Chunk 是**技术划分**（加载/管理单位），Region 是**世界逻辑划分**（ADR-001：Chunk 只是空间分区单位） |
| 不是 Biome | Biome 是**环境表现**（离散结果）；Region 是**世界原因 + 影响**（§5） |
| 不是地图格子 | Region 不依赖网格分辨率；同一 Region 可以跨越任意数量的体素/列/Chunk |
| 不是存储容器 | Region 不是存档单位、不是跨帧存活的状态（继承 `WorldState_Design.md` §1.2 R1） |
| 不是生成器 | Region 不生成 Chunk、不生成 Mesh、不生成 Entity、不写 Voxel（§12） |

**Chunk 与 Region 的对照**

| 维度 | Chunk（技术划分） | Region（世界逻辑划分） |
|---|---|---|
| 目的 | 加载/生成/管理/呈递的单位 | 世界原因与环境修正的单位 |
| 边界 | 规则网格（`ChunkSize`，冻结） | 由影响场与显式阈值定义的**渐变**边界（§8） |
| 是否可跨 | 一个 Chunk 固定大小 | 一个 Region 可跨多个 Chunk |
| 数量关系 | 一个 Chunk 可同时落在多个 Region 的影响范围内 | 一个 Region 可覆盖多个 Chunk |
| 是否影响调度 | 是（加载/卸载的单位） | **否**（Region 不出现在任何调度接口中） |

**示例（同一世界、不同逻辑归属）**

| Chunk | 位置特征 | 所属 Region（逻辑） | 环境结果 | 表现（Biome） |
|---|---|---|---|---|
| `Chunk A` | 温带、湿度高、无魔力来源 | `Forest Region` | 中等温度 / 高湿度 / 低魔力 | 普通森林 |
| `Chunk B` | 温带、湿度高、附近存在魔力源 | `Magic Forest Region` | 适宜温度 / 高湿度 / **高魔力** | 魔法森林 |

> 两个 Chunk 的**技术划分完全相同**（都是 16³ 体素），差异完全来自 **Region 层**。

### 1.3 术语对齐（与 `WorldState_Design.md` §1.2 的关系）

`WorldState_Design.md` §1.2 已使用过 `Region` 一词，其定义是：

```
Region(R) = { 世界坐标 p | Classify(WorldState(p)) == R }     （"由结果判定的空间范围"）
```

本文档使用同一个词表达**原因侧的单元**。二者**不是同义**，必须按下表区分：

| 术语 | 本文档含义 | 在 `WorldState_Design.md` 中的对应 | 在因果链中的位置 |
|---|---|---|---|
| **Region**（本文档主词） | 世界状态的**解释单元**：来源 + 修正 + 标签 + 资格 | 该文档未定义此概念（只定义了下方 `RegionView`） | **原因侧**（上游） |
| **RegionView**（区域视角） | 由**最终环境**判定得到的空间范围 | 该文档 §1.2 的 `Region(R)` 定义 | **结果侧**（下游） |

**对齐条款**

| # | 条款 |
|---|---|
| **RT1** | 本文档的主词 `Region` 一律指**原因侧的解释单元**；`WorldState_Design.md` §1.2 的定义一律按 `RegionView` 理解 |
| **RT2** | 二者**不矛盾、不重名冲突**：一个描述"为什么会有这片区域"，一个描述"这片区域在环境上落在哪里" |
| **RT3** | `WorldState_Design.md` §1.2 的 R1–R5 对 `RegionView` 与本文档的 `Region` **同时成立**（都不是容器、都必须可局部求值、都不得依赖 Chunk/顺序/时间） |
| **RT4** | `WorldState_Design.md` §1.2 的 **R4（不得反向定义世界状态）** 的适用对象是 `RegionView` / Biome / 生态 / 对象等**结果侧**；`Region` 作为**原因侧**对环境的修正是**正向因果**，不属于 R4 的禁止范围（详见附录 C 第 3 项） |
| **RT5** | 本文档**不修改** `WorldState_Design.md`；术语对应关系只在本规范中声明（约束：不修改任何已有文件） |
| **RT6** | 硬性：**`RegionView` 不得回写任何参数**；**`Region` 的修正量不得由 `RegionView` 反推**（即：不得用"这片看起来像魔法森林"来决定修正量——修正量只能来自 `RegionSource`） |

### 1.4 Region 的两条铁律

| # | 铁律 | 含义 |
|---|---|---|
| **RG-A** | **无源不成区** | 任何 Region 都必须能回溯到至少一个 `RegionSource`（§3）。禁止"凭空出现的区域" |
| **RG-B** | **单一方向** | 因果只有一个方向：`来源 → Region → 环境修正 → 最终环境 → 表现 / 资格`。禁止任何反向箭头（§3.3） |

---

## 2. Region 与 WorldState 的关系

### 2.1 定义：基础世界状态 + 区域修正

> **Region 不直接存储所有环境值。**

```
FinalState(p) = BaseWorldState(p) + RegionModifier(p)
```

| 组成 | 含义 | 权威来源 |
|---|---|---|
| **BaseWorldState(p)** | 不加任何区域影响时的基础环境（`WorldState_Design.md` §1.3 的 8 参数） | `WorldState_Design.md` |
| **RegionModifier(p)** | 该位置受到的全部区域修正之和（可为零） | 本文档（§4.2 / §9） |
| **FinalState(p)** | 交给群系解析与资格判定使用的最终环境 | 本文档定义形式，参数语义仍归 `WorldState_Design.md` |

**示例（与需求示例一致）**

| 参数 | Base | `Magic Forest Region` 的修正 | FinalState |
|---|---|---|---|
| Temperature | 0.5 | 0（该区域不修正温度） | 0.5 |
| Moisture | 0.7 | 0（该区域不修正湿度） | 0.7 |
| MagicDensity | 0.2 | **+0.6** | **0.8** |

### 2.2 Region 是影响场，不是固定标签

| 若把 Region 当作**固定标签** | 若把 Region 当作**影响场**（V1 选择） |
|---|---|
| 标签是离散的、非黑即白 | 影响是**连续的**、随距离衰减（§8） |
| 标签本身要回答"是哪一类" | 修正量回答"改变了多少"，类型只是**解释结果**（§4.3） |
| 边界处出现突变 | 边界处平滑过渡，不会"森林瞬间变沙漠" |
| 无法叠加多个原因 | 多来源自然叠加（§9） |

**硬性规则**

| # | 规则 |
|---|---|
| **RM1** | `RegionModifier` 必须是**世界坐标的纯函数**（同 `Seed` + 参数 + 坐标 ⇒ 同结果） |
| **RM2** | `RegionModifier` 的每个分量必须**有界**（显式上下限），叠加后必须**钳制**到参数定义域 |
| **RM3** | **零修正必须显式**：无区域影响时修正量恒为 0，且与"没有 Region"逐值等价 |
| **RM4** | 修正**只能**作用于 §4.2 列出的 6 个参数；**不得**引入新的环境参数名目 |
| **RM5** | 环境参数的**唯一权威路径**是 `Base + RegionModifier`；禁止存在第二套环境参数生成路径 |
| **RM6** | `RegionModifier` 不得依赖结果（Biome / 生态 / 对象 / 遗迹）——见 §3.3 |

### 2.3 Region 与 WorldState 的分工

| 事项 | WorldState 规范 | 本规范 |
|---|---|---|
| 参数的定义域与语义（8 个参数） | ✅ 唯一权威 | 复用（不得重新定义） |
| 参数的**基础**生成（噪声 / 海拔 / 派生） | ✅ 唯一权威 | 不参与 |
| **区域修正**（Modifier）的来源与叠加 | 只规定"修正存在" | ✅ 唯一权威 |
| 静态场纪律（紧支撑 / 单调 / 边界连续 / 无时间维度） | ✅ 定义（§3.5） | 复用并扩展到 Region 影响 |
| 群系解析规则表 | ✅ 唯一权威（§5.3） | 只提供输入（§5） |
| 圣与恶的 `Balance` 定义 | ✅ 唯一权威（§4.4） | 复用（不得另立第二定义） |

---

## 3. Region 来源设计

### 3.1 Region 必须有来源

> **禁止**："随机生成魔法森林"。

```
原因           Region            环境变化               生态结果
RegionSource → Region（修正） → FinalState = Base + Modifier → Biome / 资格
```

| # | 规则 |
|---|---|
| **RS1** | 每个 Region 必须由至少一个 `RegionSource` 驱动（`RG-A`） |
| **RS2** | `RegionSource` 的产生必须是**确定性纯函数**（`Seed` + 显式参数 + 世界规则），不得使用运行时随机 |
| **RS3** | `RegionSource` 必须**可局部枚举**：给定任意世界坐标，无需遍历全世界即可确定其影响 |
| **RS4** | `RegionSource` 不得依赖加载顺序、帧序、时间、已加载 Chunk 集合 |
| **RS5** | `RegionSource` 不是 Entity：它没有生命周期、没有行为、不需要对象化存档（ADR-001） |
| **RS6** | 若未来某个来源需要表现为可见对象（例如水晶、遗迹结构），那是**未来 Entity / Structure 系统**的事，且**不得**把影响值写入 Voxel 数据 |

### 3.2 来源分类

#### 自然来源（Natural）

| 来源 | 语义 | 典型影响参数 |
|---|---|---|
| **大面积气候** | 纬度带 / 大尺度气候分区 | `Temperature`、`Moisture` |
| **海洋** | 水体对沿岸环境的调节 | `Moisture`（沿海增湿）、`Temperature`（海洋性气候） |
| **山脉** | 地形抬升导致的温度递减与雨影效应 | `Temperature`（下降）、`Moisture`（背风面下降） |
| **河流** | 沿线水分与肥力富集 | `Moisture`、`Fertility` |
| **土壤** | 地层/母质导致的肥力差异 | `Fertility` |

> 自然来源的作用是"**修正基础环境**"，不是"重新定义地形"：地形与海陆仍由 Geology 与 `Elevation` 决定（§12）。

#### 魔法来源（Magic）

| 来源 | 语义 |
|---|---|
| **AncientMagicSite** | 远古魔力场址 |
| **MagicCrystalDeposit** | 魔法水晶矿脉（与 `DepositZone` 属性层对应） |
| **AncientWizardBattle** | 古代法师战争残留（历史事件来源） |
| **PowerfulCreatureDeath** | 强大生物死亡 |
| **MagicSeal** | 封印 |

> 魔法来源的场规则与 `WorldState_Design.md` §2/§3 完全一致（`Position` / `Strength` / `Radius` / `Type` + 静态叠加）。

#### 圣恶来源（Holy / Corruption）

| 类别 | 来源 | 语义 |
|---|---|---|
| **腐化** | 墓地 | 集中死亡之地 |
| **腐化** | 邪恶遗迹 | 腐化性质的远古构造 |
| **腐化** | 污染区域 | 被污染/破坏的空间 |
| **圣** | 圣物 | 圣性物体所在 |
| **圣** | 圣地 | 长期被尊崇的地点 |
| **圣** | 圣生物聚集 | 圣性生物长期聚集 |

### 3.3 因果方向（硬性）

**唯一允许的方向**

```
自然来源 ┐
魔法来源 ├─→ RegionSource ─→ Region ─→ RegionModifier ─→ FinalState ─→ Biome / 资格
历史事件 ┘
```

**禁止的方向**

| # | 禁止 | 理由 |
|---|---|---|
| **RC1** | 用 Biome / 生态 / 遗迹 / 对象的存在反推 Region 或修正量 | 循环解释、不可复现（继承 `WorldState_Design.md` §1.5 C1–C4） |
| **RC2** | 用 `RegionView`（最终环境分类）反推 `RegionModifier` | `RegionView` 是结果侧（RT6） |
| **RC3** | 让两个 Region 互相读取对方的存在或强度来调整自身 | 引入顺序依赖、破坏可验证性 |
| **RC4** | 让 Region 读取"已生成内容"（Chunk 是否加载、体素是什么） | 违反纯函数性与 Chunk 计算独立 |
| **RC5** | 让 Region 的类型名（`MagicForest` 等）参与任何规则判定 | 类型是解释结果（§4.3） |

---

## 4. Region 属性模型

> 本节定义的是**概念属性**，不是数据结构；本文档不给出任何字段/类型/接口草案。

### 4.1 空间属性

| 属性 | 含义 | 约束 |
|---|---|---|
| **Position** | 区域的锚点（来自来源的世界坐标） | 必须是**世界坐标**；不得是 Chunk 局部坐标、不得依赖 Chunk 编号 |
| **Size** | 区域的影响尺度（半径 / 尺度参数） | 显式参数；与"紧支撑"要求一致（影响在尺度之外为 0） |
| **Boundary** | 区域的边界 | **不是硬几何体**：由影响场 + 显式阈值共同定义的**可查询、可渐变**边界（§8） |

| # | 规则 |
|---|---|
| **RP1** | 三个空间属性都必须是**可查询结果**，不得是"存储的容器"或"跨帧存活对象" |
| **RP2** | 任意世界坐标必须能**独立**求出"该点受哪些 Region 影响、修正多少"（继承 R2 与 RS3） |
| **RP3** | 空间属性不得依赖 Chunk 划分、加载顺序、帧序、时间 |

### 4.2 环境影响属性（修正集）

**V1 允许的修正分量（6 个，与需求一致）**

| 修正量 | 作用对象 | 叠加规则 |
|---|---|---|
| **TemperatureModifier** | `Temperature` | 加法 + 钳制 |
| **MoistureModifier** | `Moisture` | 加法 + 钳制 |
| **FertilityModifier** | `Fertility` | 加法 + 钳制 |
| **MagicDensityModifier** | `MagicDensity` | 加法 + 钳制 |
| **HolyInfluenceModifier** | `HolyInfluence` | 加法 + 钳制（独立计算） |
| **CorruptionInfluenceModifier** | `CorruptionInfluence` | 加法 + 钳制（独立计算） |

**硬性**

| # | 规则 |
|---|---|
| **RE1** | 修正集**不含** `Elevation` 与 `Ocean`：Region **永远不能**改变地形高度与海陆边界 |
| **RE2** | 理由：`Elevation`/`Ocean` 是地形与海陆的派生量，属于 Geology 与 `WorldState_Design.md` 的关系；若允许 Region 修改它们，就等于修改地形（违反 §12 的"不得修改 `Density`/`Material`/`Cave`"） |
| **RE3** | 修正量的**值域与钳制**必须显式声明；修正后必须落在参数定义域内（`WorldState_Design.md` §1.3 规则 1） |
| **RE4** | 修正量必须是**纯函数**且**可重复求值**（无副作用、无缓存依赖） |
| **RE5** | 未来若要允许 Region 改变地形，必须**另立规范并升版本**（V1 明令禁止） |

### 4.3 类型信息（解释结果，不是原因）

**示例类型**：`Forest`、`Desert`、`Mountain`、`MagicForest`、`CorruptedLand`、`HolyLand`、`Ocean`。

> **注意：类型不是原因。类型是解释结果。**

| # | 规则 |
|---|---|
| **RTY1** | `RegionType` 是**描述性标签**：由"来源类别 + 修正侧写 + 最终环境"**推导**得到，用于调试、工具、历史标签与可读性 |
| **RTY2** | `RegionType` **没有任何规则权**：任何规则（环境求值、群系解析、资格判定、遗迹判定）**都不得读取它** |
| **RTY3** | 禁止"因为这里被标注为 `MagicForest`，所以提高魔力"（这正是 `WorldState_Design.md` §1.5 禁止的倒因为果） |
| **RTY4** | `RegionType` 必须由**确定性规则**推导（同输入同结果），不得随机、不得人工干预 |
| **RTY5** | `RegionType` 与 **Biome 不是同一个概念**：Biome 是环境表现（§5），`RegionType` 是世界原因侧的解释标签 |

### 4.4 属性总表

| 组 | 属性 | 性质 | 是否有规则权 |
|---|---|---|---|
| 空间 | Position / Size / Boundary | 查询结果 | 作为**影响几何**参与求值 |
| 环境 | 6 个 Modifier | 影响场 | ✅ 参与 `Base + Modifier` |
| 类型 | RegionType | 解释标签 | ❌ 无规则权 |
| 标签 | HistoryTags | 只读历史事实 | ✅ 可被资格规则读取（不得被参数求值读取，§6.4） |
| 资格 | EcologyRules | 资格声明 | ✅ 产出"允许/不允许"，不生成对象 |

---

## 5. Region 与 Biome 的关系

### 5.1 明确：Region ≠ Biome

```
Region
  |
  └── 环境参数（FinalState = Base + RegionModifier）
            |
            └── Biome Resolver（唯一权威：WorldState_Design.md §5.3 的有序规则表）
                      |
                      └── Biome
```

| | Region | Biome |
|---|---|---|
| 性质 | **世界原因**（来源 + 影响 + 标签 + 资格） | **环境表现**（离散分类结果） |
| 数量关系 | 一个区域可跨多个群系；一个群系可由多个区域共同促成 | 一个坐标只有一个群系结论 |
| 是否连续 | 影响是连续的（随距离衰减） | 标识是离散的（参数穿越阈值时切换） |
| 是否可由另一个推导 | 不能由 Biome 反推 Region | 由 `FinalState` + 规则表唯一确定 |

### 5.2 示例（与需求示例一致）

| 环节 | 内容 |
|---|---|
| **Region** | `AncientMagicForestSource`（远古魔力森林来源） |
| **WorldState** | `MagicDensity` 高 |
| **Biome** | 魔法森林 |

### 5.3 单调因果链（硬性）

| # | 规则 |
|---|---|
| **RB1** | **Region → 环境参数 → Biome**，顺序不可颠倒 |
| **RB2** | Region **不直接决定** Biome：Region 只能通过修改环境参数间接影响群系结果 |
| **RB3** | 群系解析**只有一个权威**：`WorldState_Design.md` §5.3 的有序规则表。本规范**不得**定义第二套群系判定 |
| **RB4** | 若 Region 层需要"区域专属群系语义"，只能以**来源 + 修正**表达（即"让参数落进既有规则表的对应分支"），不得新增规则表 |
| **RB5** | 群系结果**不得**回写 Region 的属性（禁止反向箭头） |
| **RB6** | 群系的表现边界（§5.7 的唯一影响 = 地表材料）仍由 Geology 侧的既有规则决定，本规范不放宽 |

---

## 6. Region 与历史系统的关系

### 6.1 定义：历史决定部分 Region

```
History Event
      ↓
Region Source（区域来源）
      ↓
Region（修正 + 标签）
      ↓
世界状态变化（FinalState）
```

### 6.2 示例（与需求示例一致）

| 历史事件 | 产生的 Region | 环境变化 |
|---|---|---|
| **1000 年前的魔法战争** | `MagicScar Region`（魔法伤痕） | `MagicDensity` ↑ |
| **亡灵灾难** | `Corruption Region`（腐化区域） | `CorruptionInfluence` ↑ |

### 6.3 历史层与 Region 层的分工

| 事项 | History Layer | Region Layer |
|---|---|---|
| 事件的发生（类型 / 位置 / 序位） | ✅ 拥有（未来规范） | ❌ |
| 事件 → 来源的转换 | 提供事件事实 | ✅ 产出 `RegionSource` |
| 来源 → 修正 | ❌ | ✅ 拥有 |
| 历史标签 | 提供事件语义 | ✅ 附着并只读暴露（`HistoryTags`） |
| 事件的**过程模拟** | ❌（V1 不实现） | ❌ |

### 6.4 History Tags 的读取权限

| # | 规则 |
|---|---|
| **RH1** | `HistoryTags` 是**只读的历史事实标签**，必须来自确定性规则（`Seed` + 世界坐标），不得依赖运行时状态 |
| **RH2** | **允许**被读取方：Region 属性的推导、群系**资格**规则、遗迹资格判定（它们是原因侧信息） |
| **RH3** | **禁止**被读取方：环境参数求值（`Base + Modifier` 的求值过程）。参数只能来自 `Base` 与 `RegionModifier`（`RM5`） |
| **RH4** | `HistoryTags` 不得被用于生成任何对象（只产出资格） |
| **RH5** | 历史标签不得引入"历史模拟"：V1 中历史只是**事实与归因**，不是过程（§11） |

---

## 7. Region 与遗迹生成

### 7.1 定义：遗迹属于历史结果

> **禁止**：随机放遗迹。

```
文明（偏好）
      ↓
活动范围（哪些空间符合偏好）
      ↓
历史事件（发生过什么）
      ↓
遗迹候选区域
```

### 7.2 示例（与需求示例一致）

| 文明 | 偏好（环境参数条件） | 结果（遗迹候选） |
|---|---|---|
| **矮人** | 山脉（高 `Elevation`）、矿产丰富区域（`DepositZone ≠ None`） | 矮人工坊、矮人矿城 |
| **精灵** | 森林（高 `Moisture`、中 `Temperature`）、高魔区域（高 `MagicDensity`） | 精灵遗迹 |

### 7.3 遗迹候选规则

| # | 规则 |
|---|---|
| **RR1** | 遗迹候选必须能回溯到"**文明偏好 → 活动范围 → 历史事件 → 候选区域**"这条链（可解释性，附录 B 的 `RGV`） |
| **RR2** | 候选必须是**只读查询结果**，可重复求值，不得依赖加载顺序/时间 |
| **RR3** | 候选的产出**不得**写 Voxel、不得创建 Chunk、不得生成对象（ADR-001） |
| **RR4** | 禁止随机撒点；密度必须由**显式规则 + 显式参数**决定 |
| **RR5** | 候选**不得**违反地形常识（例如 `Ocean == true` 处不产生陆地遗迹，除非显式定义水下遗迹类型） |
| **RR6** | 候选**不得**改变 `Density` / `Material` / `CaveDensity`（遗迹不是地形的一部分） |
| **RR7** | Region 层产出的粒度是**候选区域（空间范围）**；区域内部的**具体落点/朝向/结构/战利品/存档**属于未来 **Structure / Entity 系统** |

> **与 `WorldState_Design.md` §6.4 的对齐**：该文档描述"遗迹候选（位置 + 类型 + 归属 + 资格）"；
> 本文档把粒度明确为"**先得到候选区域，再由未来系统决定区域内的具体落点**"。二者是**细化关系**，不是矛盾（附录 C 第 8 项）。

---

## 8. Region 边界设计

### 8.1 问题

Region 的边界**如何产生**？三种候选方案：

**方案 A：硬边界**——用几何体/多边形/严格阈值把空间切开，内部一个 Region，外部另一个。

**方案 B：连续影响场**——Region 是一张随空间连续变化的修正场；边界只是"影响显著/不显著"的过渡带。

**方案 C：距离函数**——以"到来源的距离"为唯一自变量的影响形状（越近越强）。

### 8.2 优缺点比较

| 维度 | A. 硬边界 | B. 连续影响场 | C. 距离函数 |
|---|---|---|---|
| 是否突变 | **会突变**（边界两侧环境跳变） | 平滑过渡 | 平滑（取决于形状） |
| 与 Geology 的一致性 | 差：会产生"森林瞬间变沙漠" | **好**：与地形的连续性一致 | 好 |
| 叠加多来源 | 需要额外规则（谁覆盖谁） | **自然叠加**（§9） | 自然叠加 |
| 确定性/可验证性 | 容易（但边界本身需要规则） | **容易**（纯函数叠加） | 容易 |
| 表达"区域中心感" | 差（内部均匀） | 好 | **最好**（显式中心） |
| 实现复杂度 | 低 | 中 | 中 |
| 风险 | 边界处出现不可解释的断层 | 参数过多时需要纪律（本规范提供） | 形状选择需要显式参数 |

### 8.3 V1 推荐：**连续影响（方案 B）**

**理由**

1. **避免"森林突然变沙漠"**：环境参数必须连续，群系的切换只能由参数穿越阈值引起（`WorldState_Design.md` §5.6 CT1/CT2）；
2. **与既有世界的连续性纪律一致**：地质规范 §2.3 F2 禁止"相邻 Chunk 规则不同"，硬边界等价于在边界处换了一套规则；
3. **支持多来源叠加**：多个 Region 的影响可以自然相加（§9），不需要"谁覆盖谁"的优先级规则；
4. **可验证**：连续性可以用"相邻坐标差值的上界"机械验证（附录 B）。

### 8.4 距离衰减要求（硬性）

| # | 规则 |
|---|---|
| **RBD1** | **Region 影响必须随距离下降**（越靠近来源越强） |
| **RBD2** | 方案 C（距离函数）是方案 B 的**合法实现形态**：以"到来源的世界距离"为唯一自变量的衰减形状 |
| **RBD3** | 衰减形状必须满足：**紧支撑**（超出 `Size` 后为 0）、**单调不增**、**边界连续趋于 0**（不得跳变切断）——与 `WorldState_Design.md` §3.5 CF1–CF3 同一条纪律 |
| **RBD4** | 边界必须**可查询**：给定世界坐标可判定"受哪些 Region 影响、各影响多少" |
| **RBD5** | 不得引入与影响场无关的第二套边界（例如"按 Chunk 划分的 Region 归属表"） |
| **RBD6** | 过渡带宽度必须是**显式参数**；不得依赖平台相关的噪声/浮点路径 |
| **RBD7** | Region 允许**重叠**：同一坐标可以同时受多个 Region 影响（§9） |
| **RBD8** | Region 允许**无强边界**（例如"大面积气候"类自然区域），但此时仍必须满足 `RM1`–`RM6` |

---

## 9. 多种 Region 叠加

### 9.1 问题

如果一个地方同时存在：**魔法源 + 腐化源 + 圣地**，如何处理？

### 9.2 叠加规则（定义）

**总原则**：`RegionModifier(p) = Σ 各 Region 在同一点的修正(p)`，逐参数相加后**钳制**。

| 参数 | 叠加规则 | 说明 |
|---|---|---|
| **Temperature** | 加法叠加 + 钳制 | 山脉降温与海洋调节可同时作用 |
| **Moisture** | 加法叠加 + 钳制 | 河流增湿与山脉雨影可同时作用 |
| **Fertility** | 加法叠加 + 钳制 | 土壤与河流可同时作用 |
| **MagicDensity** | **可叠加**（加法 + 钳制） | 多个魔力来源叠加增强 |
| **HolyInfluence** | **独立**加法叠加 + 钳制 | 多个圣性来源叠加增强 |
| **CorruptionInfluence** | **独立**加法叠加 + 钳制 | 多个腐化来源叠加增强 |
| **Balance**（派生量） | `Balance = HolyInfluence − CorruptionInfluence` | **圣与恶的"抵消"只在这里发生** |

### 9.3 "Holy / Corruption 互相抵消"的合法实现（硬性）

| 视角 | 正确理解 | 禁止的误实现 |
|---|---|---|
| 参数层 | `HolyInfluence` 与 `CorruptionInfluence` **各自独立叠加**，互不读取 | ❌ "一方减去另一方"后把残值写回参数 |
| 派生层 | **抵消**通过 `Balance = Holy − Corruption` 表达；`Contested` 由 `Balance` 落在显式区间得出 | ❌ 用抵消结果覆盖两个原始参数 |
| 时间层 | V1 中抵消**没有时间演化**（不是"谁在侵蚀谁"） | ❌ 动态战争、传播、净化、占领 |

| # | 规则 |
|---|---|
| **RO1** | 加法叠加必须满足**交换律**：叠加结果与"遍历顺序"无关（可机械验证） |
| **RO2** | `Balance` 的定义**必须复用** `WorldState_Design.md` §4.4，不得另立第二定义 |
| **RO3** | **V1 不实现动态战争**：不存在传播、侵蚀、净化、转化、占领、随时间的胜负变化（沿用该文档 §4.5 HP2/HP3） |
| **RO4** | 叠加后每个参数必须落在定义域内；钳制规则必须显式 |
| **RO5** | 叠加**不得**产生"放大反馈"（某 Region 不得因为附近参数高而增强自身） |
| **RO6** | 同一坐标允许同时存在多个方向相反的力量（"争夺带"是**合法且可解释**的状态） |
| **RO7** | 叠加结果的规模必须**有界可枚举**：给定世界坐标，参与叠加的 Region 数量必须由显式规则限定（与 `RS3` 一致） |

---

## 10. 世界大小设计

> 本节**保持** `WorldState_Design.md` §7 的推荐，并补充 Region / History / Civilization / Events 视角的分析。

### 10.1 两个候选

| 候选 | 含义 |
|---|---|
| **A. 无限世界** | 水平方向无边界（今天的既有行为），地形/区域/来源随坐标无限延伸 |
| **B. 有限大陆** | 世界中存在**有限数量**的陆块；大陆之外是海洋 |

### 10.2 为什么事件系统更适合有限区域（核心论证）

| # | 论证 |
|---|---|
| **1** | **事件需要"舞台边界"才成立**：所谓"世界级事件"必须有与事件影响范围可比的世界范围；无限回廊中"整个世界"没有意义 |
| **2** | **因果必须收敛**：有限世界里，"某个事件影响了哪些区域"是可枚举、可解释、可验证的；无限世界里影响集合无界，只能靠人为截断（等于人为造边界） |
| **3** | **Region 来源集合必须有界**：Region 依赖来源（`RS1`/`RS3`）；来源集合无界时，"每点受多少区域影响"无法自然定义（`RO7`） |
| **4** | **遗迹与文明需要"活动范围"**：文明的活动范围是**有限**的（偏好 + 可达性）；无限世界里"文明范围"没有自然尺度，遗迹密度只能靠参数硬压 |
| **5** | **可解释性要求**：有限世界可以回答"为什么这里没有遗迹"（超出所有文明活动范围）；无限世界无法给出这种解释 |
| **6** | **探索与历史的一致性**：有限大陆 + 海洋隔离 ⇒ 跨大陆是**有意义的目标**，各大陆可有独立历史（"大陆 A = 玩家区域，大陆 B = 其他文明"） |

### 10.3 推荐方案：**无限海洋 + 有限大陆**

```
水平世界：保持无限（Chunk 空间无边界 —— 与今天的流式行为完全一致）
垂直世界：沿用既有 WorldHeight（不改变）
陆地：    由"大陆函数"限定的有限块（大陆 A / 大陆 B / …）
大陆之外：海洋（可无限延伸）
```

| # | 规则 |
|---|---|
| **RW1** | 推荐**保持** `WorldState_Design.md` §7.3 的方案，本文档不放宽也不替换 |
| **RW2** | Region **不引入水平世界硬边界**：水平世界仍无限 ⇒ Streaming / Runtime / Chunk 坐标语义**零修改** |
| **RW3** | 大陆之间由海洋隔离，隔离语义由显式参数保证（大陆间隔 ≥ 最小值） |
| **RW4** | 大陆数量、中心、尺度、形状必须是**确定性纯函数**（`Seed` + 显式参数），不得依赖模拟 |
| **RW5** | Region 层可以读取"大陆归属"作为**原因侧信息**（例如"这是大陆 B 的森林"），但**不得**把它当作环境参数之外的隐藏规则 |
| **RW6** | 大陆元数据属于**世界状态/历史标签**（`HistoryTags` 同族），**不是** Chunk 数据、不是 Region 容器 |

### 10.4 明确不实现（本节范围）

* 不实现大陆生成、世界边界、跨大陆交通；
* 不实现历史 / 事件 / 文明模拟；
* 本节内容仅作为后续实现规范的依据。

---

## 11. V1 实现范围限制

### 11.1 V1 只设计

| 内容 | 形态 |
|---|---|
| **Region 查询** | `位置 → WorldState Modifier`：给定世界坐标，求出受到的区域修正（可为 0） |
| **Biome 输入** | 把 `FinalState = Base + Modifier` 交给**唯一**的群系解析规则（`RB3`） |

即：**V1 的 Region 层只负责"解释与修正"，不负责"产出"**。

### 11.2 V1 不实现

| # | 不实现 | 说明 |
|---|---|---|
| 1 | **Region 动态增长** | 区域不扩散、不收缩、不迁移、不随时间变化 |
| 2 | **文明模拟** | 不模拟文明兴衰、迁徙、扩张 |
| 3 | **NPC 历史模拟** | 不模拟 NPC 行为与历史过程 |
| 4 | **世界战争** | 圣与恶不做动态胜负（`RO3`） |
| 5 | **生态演化** | 不模拟物种演化、适应、种群变化 |

### 11.3 硬性禁令

| # | 禁令 |
|---|---|
| **RV1** | 不新增 `VoxelType`（V1 只有 Air/Grass/Dirt/Stone） |
| **RV2** | 不修改 VoxelCore / WorldGeneration 既有实现 / Geology 实现（含被冻结的 9 个地质文件） |
| **RV3** | 不修改 Runtime Core / Streaming / Bootstrap / Mesh / View |
| **RV4** | 不生成 Chunk、不生成 Mesh、不创建 Entity、不写 Voxel |
| **RV5** | 不使用非确定性来源（`UnityEngine.Random` / `System.Random` / `Time` / `DateTime` / `Guid`） |
| **RV6** | 不引入第二套环境参数权威、不引入第二套群系判定（`RM5` / `RB3`） |
| **RV7** | 不引入水平世界硬边界（`RW2`） |
| **RV8** | 不修改任何已有规范文件（本文档只新增） |

---

## 12. 与已有规范兼容检查

### 12.1 Geology：不得修改 `Density` / `Material` / `Cave`

| # | 检查项 | 结论 | 依据 |
|---|---|---|---|
| 1 | Region 是否影响 `Density`（实体/空判定）？ | **否**（硬性禁止） | `RE1`/`RE2`：修正集不含地形量 |
| 2 | Region 是否影响 `Material`（体素材料）？ | **否**（除既有群系→地表材料的唯一间接路径） | §12.4 |
| 3 | Region 是否影响 `CaveDensity`（洞穴）？ | **否** | 修正集不含洞穴量 |
| 4 | Region 是否定义地形高度/海陆？ | **否**：`Elevation` / `Ocean` 不在修正集内 | `RE1` |
| 5 | Region 是否新增体素类型？ | **否** | `RV1` |
| 6 | 关闭 Region 层后地形是否逐体素一致？ | **必须一致** | 附录 B 的 `RG10` |

### 12.2 WorldState：必须复用既有参数

| 必须复用 | 复用方式 | 禁止 |
|---|---|---|
| `Temperature` | 只提供 `TemperatureModifier` | 不得重新定义其定义域/语义 |
| `Moisture` | 只提供 `MoistureModifier` | 同上 |
| `Elevation` | **只读**（用于文明偏好、大陆/海洋语义） | **不得修改**（`RE1`） |
| `Fertility` | 只提供 `FertilityModifier` | 不得改变其派生来源 |
| `Ocean` | **只读**（遗迹排除、群系短路） | **不得修改**（`RE1`） |
| `MagicDensity` | 只提供 `MagicDensityModifier` | 不得另立魔力来源体系（复用 §3 静态场规则） |
| `HolyInfluence` | 只提供 `HolyInfluenceModifier` | 不得引入耦合/压制 |
| `CorruptionInfluence` | 只提供 `CorruptionInfluenceModifier` | 同上 |
| `Balance` | **只读复用**其定义 | 不得另立第二定义（`RO2`） |
| 群系规则表 | 只作为**输入**使用 | 不得定义第二套判定（`RB3`） |
| 群系边界（表层材料） | 由 Geology 侧既有规则决定 | 不得放宽（`RB6`） |

### 12.3 Runtime：不得接触 Chunk / Mesh / Entity

| 不得接触 | 说明 |
|---|---|
| **Chunk** | Region 不参与加载/卸载、不改变 Chunk 划分与坐标语义、不置脏、不影响预算与并发 |
| **Mesh** | Region 不产生几何、不改变面/材质/子网格语义 |
| **Entity** | Region 不创建对象、不生成结构/植被/生物（只产出资格） |
| **调度** | Region **不出现在任何调度接口中**：Runtime 只认识 `IWorldGenerator` |

### 12.4 唯一允许的"Region → 体素"间接路径

```
Region → FinalState（环境参数） → 既有群系解析（Geology 侧，未修改） → 地表层材料（{Grass, Dirt, Stone}）
```

| # | 规则 |
|---|---|
| **RI1** | 上述路径是**唯一**允许影响体素的路径 |
| **RI2** | 该路径**不改变** `Density`、不改变地表高度、不改变洞穴、不新增体素类型 |
| **RI3** | 若 Region 层被关闭，世界必须与今天**逐体素一致**（退化兼容，`RG10`） |
| **RI4** | 任何其它路径（例如"Region 直接改材料/加方块"）**一律禁止** |

### 12.5 机械可验证的零修改纪律

| # | 纪律 |
|---|---|
| **RV9** | VoxelCore / WorldGeneration 既有实现 / Geology 9 个实现文件 / Runtime（Core+Streaming）/ Mesh / View / Bootstrap 的 SHA256 必须保持不变（沿用既有验证入口的 SHA 比对做法） |
| **RV10** | 若未来落地时**必须**触及被 SHA 冻结的文件，必须作为**一次显式授权的变更**，并**级联重钉**相关验证入口的哈希（与既有 Stage A–E 的纪律一致） |

---

## 附录 A：Region 与已有系统关系图

### A.1 层次与所有权

```
World
 |
 ├── History Layer                     （事件：原因层 · 未来规范）
 |        └── 产出：RegionSource / HistoryTag
 |
 ├── Region Layer                      （本文档）
 |        ├── Sources          来源记录：Position / Strength / Size / Kind
 |        ├── WorldState Modifier   6 个修正分量（不含 Elevation / Ocean）
 |        ├── Biome            把 FinalState 交给唯一群系解析规则（输入，不是权威）
 |        ├── History Tags     只读历史事实标签
 |        └── Ecology Rules    资格声明（允许什么；不生成对象）
 |
 └── Geology                          （Terrain truth · 唯一权威）
          └── Density / Material / CaveDensity / DepositZone / 群系接口
```

### A.2 单向数据流

```
   [自然来源] [魔法来源] [圣恶来源] [历史事件]
                 \      |      /        /
                  \     |     /        /
                   RegionSource（原因记录）
                            |
                            v
                  Region（解释单元：修正 / 标签 / 资格）
                            |
                            v
          FinalState(p) = BaseWorldState(p) + RegionModifier(p)
                            |
              +-------------+-------------+
              |                           |
              v                           v
   Biome Resolver（唯一权威）        Ecology Rules（资格声明）
              |                           |
              v                           v
        Biome（环境表现）          允许/不允许（未来 Entity / Vegetation 消费）
              |
              v
   （既有 Geology 规则）→ 地表层材料 {Grass, Dirt, Stone}

   ── 反向箭头一律禁止（RC1–RC5 / RT6）──
```

### A.3 读 / 写权限矩阵

| 系统 | 可读 | 可写 | 禁止读取 |
|---|---|---|---|
| **History Layer** | 环境参数（只读） | — | — |
| **Region Layer（本文档）** | 来源、历史标签、基础环境参数、地形派生量（`Elevation`/`Ocean` 只读） | `RegionModifier`、`HistoryTags`、`EcologyRules`、`RegionType` | 已生成内容、Chunk 状态、Biome 结果 |
| **WorldState 参数求值** | `Base`、`RegionModifier` | 参数值 | `HistoryTags`、Biome、生态、对象 |
| **Biome 解析** | `FinalState` | Biome | Region 内部属性、来源列表 |
| **Geology** | 既有规则（`HeightFunction` 等） | `Density`/`Material`/`CaveDensity` | **Region（完全不认识）** |
| **Runtime / Streaming** | `IWorldGenerator` | Chunk 生命周期 | **Region（完全不知道其存在）** |
| **Mesh / View** | Voxel 数据 / 呈现缓冲 | 几何 / 表示 | **Region** |

---

## 附录 B：Region 验收规则

| # | 验收规则 | 验证方法 |
|---|---|---|
| **RG1** | **无源不成区**：每个 Region 都能回溯到至少一个 `RegionSource` | 结构性检查（来源链完整性；无"独立区域"） |
| **RG2** | **无凭空生成**：不存在"随机 Region" | 结构扫描（无随机源；来源必须是 `Seed` 的确定性函数） |
| **RG3** | **纯函数性**：同一 `(Seed, 参数, 坐标)` 重复求值结果恒等 | 同坐标重复求值 N 次逐值比较 |
| **RG4** | **Seed 决定性 + 非平凡性**：同 `Seed` ⇒ 同结果；不同 `Seed` ⇒ 至少一处修正不同（反例保护） | 双世界对照 + 非平凡性断言 |
| **RG5** | **局部可求值**：任意坐标无需遍历全世界即可求出所受修正 | 复杂度/枚举范围检查（来源必须可局部枚举） |
| **RG6** | **叠加交换律**：多 Region 叠加结果与遍历顺序无关 | 打乱顺序重算并比较 |
| **RG7** | **边界连续**：影响随距离单调不增、边界处连续趋于 0，无跳变 | 沿距离采样 + 相邻坐标差值上界断言 |
| **RG8** | **类型无规则权**：`RegionType` 不被任何规则读取 | 结构扫描（`RegionType` 只出现在标签/调试路径） |
| **RG9** | **单一权威**：环境参数只能由 `Base + RegionModifier` 得到；群系只能由唯一解析规则得到 | 结构扫描（无第二套参数路径 / 无第二套群系表） |
| **RG10** | **退化兼容**：关闭 Region 层 ⇒ 世界与今天逐体素一致 | 与既有基线（Stage 1 / Stage C / Stage D 输出）逐体素 + 内容哈希比较 |
| **RG11** | **地形不变**：Region 不影响 `Density` / `Material`（除唯一间接路径）/ `CaveDensity` | 开关 Region 前后体素快照比较（差异集合 ⊆ 允许集合） |
| **RG12** | **无新增体素类型**：不引入任何 `VoxelType` | 结构性检查（类型集合冻结） |
| **RG13** | **零修改**：VoxelCore / Generation / Geology / Runtime / Mesh / View / Bootstrap 的 SHA 不变 | 文件哈希比对（`RV9`） |
| **RG14** | **Chunk 无关性**：Region 影响与 Chunk 划分、加载顺序、帧序无关 | 跨 Chunk 边界连续性 + 不同加载顺序对照 |
| **RGV** | **可解释性**：任意 Region / 遗迹候选都能完整回溯"来源 → 修正 → 参数 → 分类"链 | 判定链问答（沿用 `WorldState_Design.md` 附录 C 的形式） |
| **RGS** | **不生成对象**：Region 层不产生 Chunk / Mesh / Entity / 结构 / 植被 | 结构性检查（产出只有修正、标签、资格） |

---

## 附录 C：与 Geology / WorldState 冲突检查

| # | 检查对象 | 潜在冲突 | 处理 | 是否冲突 |
|---|---|---|---|---|
| 1 | Geology `Density` / `Material` / `CaveDensity` | Region 可能被误用为地形修改器 | 修正集**不含**地形量（`RE1`/`RE2`）；唯一间接路径限于既有"群系 → 地表材料"（`RI1`–`RI4`） | **否** |
| 2 | Geology `DepositZone` | Region 可能重复定义"资源存在性" | Region 只**读取** `DepositZone`（文明偏好），不定义、不修改 | **否** |
| 3 | `WorldState_Design.md` §1.2 的 `Region` 定义 | **同名不同义**：该处定义的是"由结果判定的空间范围"，本文档定义的是"原因侧解释单元" | 术语对齐条款 `RT1`–`RT6`：该处定义按 `RegionView` 理解；R4（禁止反向定义）只适用于 `RegionView`/Biome/生态等**结果侧**；本文档的 `Region` 属**原因侧**，其修正环境是**正向因果**（`RS1`–`RS6`） | **否**（术语对应，非语义矛盾） |
| 4 | `WorldState_Design.md` §1.2 R1–R3/R5 | Region 可能被误当作容器/跨帧状态 | 本文档 `RP1`–`RP3`、`RM1`–`RM6` 全盘继承：Region 是查询结果，不是容器、不跨帧、不依赖顺序/时间 | **否** |
| 5 | `WorldState_Design.md` §1.5 倒因为果禁令 | Region 的"类型"可能被当作规则输入 | `RTY1`–`RTY5`：类型是解释标签、**无规则权**；`RC1`–`RC5` 明确禁止反向箭头 | **否** |
| 6 | `WorldState_Design.md` §4.4 `Balance` 与 §4.5 HP2/HP3 | "Holy / Corruption 互相抵消"可能被实现为互相压制 | `RO1`–`RO7`：两个参数**独立加法叠加**；"抵消"只发生在**派生量** `Balance`；V1 不做动态战争；`Balance` 复用其定义（不另立第二定义） | **否**（需按本文档 §9.3 实现） |
| 7 | `WorldState_Design.md` §5.3 群系规则表 | Region 层可能定义第二套群系判定 | `RB1`–`RB6`：群系解析**唯一权威**属于该文档；Region 只提供 `FinalState` 输入；"区域专属群系"只能通过来源+修正表达 | **否** |
| 8 | `WorldState_Design.md` §6.4 遗迹候选 | 两处"候选"粒度可能不一致 | 本文档 `RR7` 明确粒度：**先候选区域，后由未来系统决定区域内的具体落点**（细化关系） | **否** |
| 9 | `WorldState_Design.md` §7.3 世界大小推荐 | 可能给出不同推荐 | `RW1`：本文档**保持**其推荐（无限海洋 + 有限大陆），只补充 Event/Region 视角论证 | **否** |
| 10 | `WorldState_Design.md` §5.8 / WS11 退化兼容 | Region 层可能破坏既有 GW/GE 验收 | `RG10`/`RI3`：关闭 Region 层必须逐体素一致 | **否** |
| 11 | `WorldState_Design.md` §8.3 VX1–VX8 | Region 可能违反其硬性禁令 | `RV1`–`RV8` 逐条沿用并补充（`RV6` 单一权威、`RV8` 不修改已有文件） | **否** |
| 12 | `WorldGeneration_Geology_Runtime_Implementation.md` §6.1/§6.2 | 群系边界可能被放宽 | `RB6`/`RI2`：地表材料仍限 `{Grass, Dirt, Stone}`，仍只作用于地表层 | **否** |
| 13 | `VoxelWorld_Runtime*.md`（调度） | Region 可能影响加载/生命周期 | `RG14`/§12.3：Region 不出现在任何调度接口；不置脏、不影响预算与并发 | **否** |
| 14 | `VoxelWorld_Mesh.md` / `VoxelWorld_View.md` | Region 可能产生几何/表现 | Region 不产生几何、不产生 View 侧处理（§12.3） | **否** |
| 15 | `ADR-001`（World / Space / Entity） | Region 可能被当作对象/容器 | Region 不是 Entity、不是 Chunk、不是 Space 数据；遗迹/资源/生物的对象化归未来 Entity / Structure 系统；Region 只产出只读资格（`RR3`/`RGS`） | **否** |
| 16 | 既有被 SHA 冻结的实现文件 | 落地时可能需要修改它们 | `RV9`/`RV10`：优先零修改（新增文件）；若必须改动，走显式授权 + 级联重钉 | **否**（纪律条款） |

**结论**：本文档只**新增解释层**，不修改任何既有契约、不引入第二套权威；
与 `WorldState_Design.md` 的**唯一实质接触点**是术语（第 3 项），已通过 `RT1`–`RT6` 完全对齐；**与已有规范无冲突**。

---

## 附录 D：未来实现阶段建议

> 本附录仅为**建议顺序**，本轮不实现任何一项。
> 任何落地必须另立实现规范（例如 `Region_Implementation.md`），并在其中给出类型/接口/参数结构（本文档不给出）。

| 阶段 | 内容 | 关键约束 | 验收 |
|---|---|---|---|
| **R-A** | `RegionSource` 记录 + **只读查询**（不接入生成） | 确定性纯函数；可局部枚举；不写 Voxel、不建 Chunk | `RG1`、`RG2`、`RG3`、`RG5` |
| **R-B** | `RegionModifier` 叠加 → `FinalState = Base + Modifier` | 加法 + 钳制；交换律；**可关闭**（关闭即退化） | `RG6`、`RG9`、`RG10` |
| **R-C** | 把 `FinalState` 交给**既有**群系解析规则 | 不定义第二套群系表；群系边界不放宽 | `RG9`、`RG11`、`RG12` |
| **R-D** | `HistoryTags`（只读标签） | 不得被参数求值读取；不得引入历史模拟 | `RH1`–`RH5`、`RGV` |
| **R-E** | `EcologyRules`（资格查询） | 只产出"允许/不允许"；不生成对象 | `RGS`、`RV4` |
| **R-F** | 大陆 / 世界尺度（可选，最后） | 掩码叠加；不引入水平硬边界；不替换既有高度场 | `RW2`、`RG13`、`RG14` |

**顺序理由**：先来源（原因）、再修正（影响）、再接表现（群系）、再归因（历史标签）、再资格（生态）、最后才是世界尺度。
每一步都必须能**独立验证**，且**关闭即退回**既有行为（`RG10`）。

**每阶段的共同硬性检查**

1. 既有验收套件（VoxelCore / WorldGeneration / Mesh / View / RuntimeCore / Streaming / Geology Stage A–E）全部保持 PASS；
2. `RV9` 的 SHA 冻结集合不变（或按 `RV10` 走显式授权 + 级联重钉）；
3. 不使用任何非确定性来源（`RV5`）；
4. 不新增 `VoxelType`、不修改 VoxelCore（`RV1`/`RV2`）。
