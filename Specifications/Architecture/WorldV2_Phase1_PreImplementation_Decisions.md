# World V2 — Phase 1 Pre-Implementation Decision Record

> **文档性质**：**实现前决策记录（Pre-Implementation Decision Record）**。本文档在 World V2 Phase 1 **开始编码前**冻结必要的架构决策。
> **仅创建 Markdown**；**不修改任何已有 Specification**；**不修改 `Assets`**；**不新增 C#**；**不实现任何逻辑**；**不删除 Legacy**；**不扩大 Phase 1 范围**。
>
> **本文档不重新设计世界架构**：所有决策都**只做一件事**——把**已冻结契约**中已经存在的口径**确认/落实**为 Phase 1 可实现的前提；只有契约**沉默**之处才做**最小必要裁决**，且必须标注理由与影响面。
>
> **上游依据（不得修改）**：
>
> | 依据 | 用途 |
> |---|---|
> | `Specifications/Architecture/WorldV2_Implementation_Boundary_Plan.md`（`IBP1`–`IBP76`） | Phase 1 允许项/禁止项、依赖方向、里程碑四项要求 |
> | `Specifications/Architecture/Legacy_Status.md`（`LS1`–`LS92`） | Legacy 冻结、不得成为 V2 依赖、不删除 |
> | `Specifications/Audit/WorldArchitecture_CrossDocument_Audit_V1.md`（`A-1`–`A-4`、`B-1`–`B-9`） | 未决项清单（本记录**只保留其未决状态**） |
> | `Specifications/World/VoxelWorld_Core.md`、`WorldGeneration_Core.md` | 坐标契约与体素真值（V2 权威） |
> | `Specifications/World/WorldParameter_Contract.md`、`WorldParameter_Runtime_Model.md` | 参数身份/权限、三态模型（V2 权威） |
> | `Specifications/World/WorldState_Runtime_Contract.md`、`WorldState_Design.md` | `Base + Modifier` 合成、Generation/Runtime 分离、玩家隔离 |
> | `Specifications/World/Region_Design.md`、`Region_Architecture_Contract.md` | Region 结构、`Modifier` 唯一写入者、只读发布 |
> | `Specifications/World/WorldQuery_Design.md`、`WorldQuery_Architecture_Contract.md` | 点查询与解释契约（只读、终端） |
> | `Specifications/World/WorldGeneration_Pipeline_Contract.md` | 阶段与依赖方向（`PC*`） |
>
> **编号说明（防新增冲突）**：
> * 本记录的条款前缀 = **`DR1`–`DR76`**、验收 = **`DRA1`–`DRA10`**（均为本记录专属，不与既有文档冲突）。
> * 决策标签沿用任务书命名 **`D1`–`D5`**；注意 `WorldGeneration_Geology.md` §8.2 另有 **`D1`–`D6`**（确定性禁止项）——引用时必须写成 **`D1（本记录）`** 或 **`D1（Geology §8.2）`**（见附录 B）。

---

## 1. Purpose

| # | 内容 |
|---|---|
| **DR1** | 本文档**不是**世界架构的重新设计：它不新增系统、不新增参数、不新增因果边、不新增契约 |
| **DR2** | 本文档**只解决 Phase 1 实现前的阻塞问题**：即"开始编码前必须已经确定、否则会返工"的少数决策 |
| **DR3** | 决策**只在允许范围内作出**（`DR7`–`DR13`）；范围之外一律**保持未裁决**（`DR14`、附录 A） |
| **DR4** | 每条决策必须写明：**问题 / 决策 / 理由 / 影响面**（§8 决策表）与**其依据的既有契约条款** |
| **DR5** | 决策必须可**机械核验**：能被转化为"允许/禁止清单"或"可判定的验收条件" |
| **DR6** | 本文档**不产生运行时效果**：不含代码、不含实现步骤、不含数据结构/算法/存储布局 |

---

## 2. Decision Scope

### 2.1 只允许裁决（五项，冻结）

| 决策 ID | 名称 | 范围 |
|---|---|---|
| **D1** | **Coordinate Authority** | Phase 1 的**唯一空间输入**（世界坐标 / Chunk / Scene 各自是否参与） |
| **D2** | **World Parameter Ownership** | `BaseValue` / `Modifier` / `FinalValue` 的**生产关系**与 Runtime 的写权限 |
| **D3** | **WorldState 与 ParameterState 关系** | 二者是同一真值还是两个真值；谁引用谁 |
| **D4** | **Phase 1 Query Boundary** | Phase 1 查询**可以读取什么、禁止读取什么** |
| **D5** | **Seed 与 Determinism** | `Seed + Position` 的确定性要求与禁止来源 |

| # | 规则 |
|---|---|
| **DR7** | 本记录**只**对 `D1`–`D5` 作出裁决；其余问题一律不得在本文档内裁决 |
| **DR8** | 每项裁决都必须**落在 Phase 1 的允许项内**（`WorldV2_Implementation_Boundary_Plan.md` §2.1 的五项：Seed / Coordinate / Parameter / Region 基础结构 / 单点查询） |
| **DR9** | **不扩大 Phase 1 范围**：本记录不得新增允许项、不得给禁止项开口子；若发现范围不足 ⇒ **停止并报告**（继承 `IBP26`/`IBP53`） |
| **DR10** | **不修改既有契约**：裁决若与既有契约冲突 ⇒ 停止并报告，不得在本文档内改写契约语义 |

### 2.2 禁止裁决（范围内不得触碰）

| # | 禁止裁决 | 状态 |
|---|---|---|
| **DR11** | **Voxel 写入最终方案**（体素真值写入者归属：Runtime 安装 vs Phase 6 抽样写入） | **保持未裁决**（审计 `A-1` / `CD-01`） |
| **DR12** | **Mining / Mesh / Resource / Ecology / Runtime Editing** 的任何设计问题 | **保持未裁决**（属 `Plan Phase 5`–`Plan Phase 8` 范围，见 `Boundary Plan` §6） |
| **DR13** | 其它未决项：`A-3`（Phase 编号双轨）、`A-4`（编号前缀多义）、`B-1`（Runtime Modification 边界）、`B-2`（配置参数 Owner 缺口）、`B-3`（参数物理存储位置未定义）、`B-4`–`B-9` | **全部保持未裁决** |
| **DR14** | 任何"顺手决定"（例如顺带确定存储布局、顺带定义接口、顺带解决术语冲突） | **一律禁止** |

---

## 3. D1 Coordinate Authority

### 3.1 决策

| # | 条款 |
|---|---|
| **DR15** | **裁决：Phase 1 的唯一空间输入 = 世界体素坐标（整数 `(x, y, z)`）。** 世界坐标是 Phase 1 全部空间判断的**唯一来源** |
| **DR16** | **世界坐标是否唯一来源？** ⇒ **是**。Phase 1 的任何结论（参数值、修正、查询结果）只由 `(Seed, 世界坐标)` 与已登记参数决定 |
| **DR17** | **Chunk 是否参与参数求值？** ⇒ **不参与**。Chunk 只是**空间分区/采样窗口/管理单位**（`VoxelWorld_Core.md`；`WorldGeneration_Geology.md` §2.1）；Phase 1 **不得**以 Chunk 编号、Chunk 局部坐标、Chunk 是否已加载作为任何判定输入（`F1`/`F2`/`F3`；`GC86`；`PC86`） |
| **DR18** | **Scene 是否参与参数求值？** ⇒ **不参与**。Phase 1 求值**不得依赖** Unity 场景对象、`Transform`、相机、渲染状态（`VoxelWorld_Core.md` §5"不得依赖 Unity Transform"；`WSC73`；`PM55`–`PM57`） |
| **DR19** | **坐标换算的权威实现**：沿用 V2 既有坐标契约（`ChunkSize = 16`、`VoxelCount = 4096`；`world = chunk × 16 + local`；负数使用 floor 语义），换算入口复用 VoxelCore 既有面（`VoxelChunk.cs`；`VoxelWorld.cs` 的 `FloorDiv` / `WorldToChunkCoordinate` / `WorldToLocalCoordinate`） |
| **DR20** | **Phase 1 不需要 Chunk 概念**：Phase 1 的输入是**单个世界坐标点**；Chunk 换算只在"若实现需要复用 VoxelCore 工具"时作为**工具**出现，**不得**成为判定语义的一部分 |

### 3.2 允许读取的既有 V2 代码面（**不扩大范围，只固定读取面**）

| # | 条款 |
|---|---|
| **DR21** | Phase 1 **允许**读取的既有 V2 实现（**只读**）：① **VoxelCore**（坐标/体素契约：`VoxelCoordinate` / `VoxelChunk` / `VoxelWorld` 的坐标换算与体素读取语义）；② **`WorldGenerationSettings`**（已登记的显式配置值，如 `Seed` / `SoilDepth` / `WorldHeight`）；③ **`HeightFunction`**（`Elevation` 的既有高度场来源，按 `WSC48`/`WSC49` **只读转发**） |
| **DR22** | Phase 1 **禁止**接入的 V2 既有模块：Geology（`GeologySampler`/`GeologyWorldGenerator` 等）、Mesh、View、Runtime Core、Streaming、Bootstrap —— 保持不接入、不修改（继承 `IBP24`/`IBP25`） |
| **DR23** | **对既有边界计划的细化声明**：`WorldV2_Implementation_Boundary_Plan.md` `IBP24` 的"唯一允许 V2 依赖 = VoxelCore 坐标/体素契约 + 契约文档"在本记录中**细化为 `DR21` 的三项**（新增：配置值来源与高度场只读来源）。**这不是范围扩大**：`Elevation` 与配置值本属 Phase 1 允许项（参数求值）的必要输入，且**均非 Legacy** |
| **DR24** | 目标达成判据：**同一世界坐标永远得到同一结果**（与 Chunk、Scene、加载状态无关） |

---

## 4. D2 World Parameter Ownership

### 4.1 决策（生产关系冻结）

```
Seed
 ↓
Parameter Evaluation
 ↓
WorldParameterState
```

| # | 条款 |
|---|---|
| **DR25** | **谁生成 `BaseValue`？** ⇒ **WorldState 层**（参数求值方）：`BaseValue` 由已登记语义求值得到；其输入只能是 Phase 1 允许面（`DR21`）+ 已登记参数（`WorldParameter_Contract.md` §3.2 表 A） |
| **DR26** | **谁生成 `Modifier`？** ⇒ **Region 层（且是唯一写入者）**：`RegionModifier` 的唯一写入者是 Region 层（`Region_Architecture_Contract.md` `RA75`；`WorldParameter_Contract.md` §4 矩阵 Region 行 = `P（修正项）`）；Phase 1 的 `Modifier` 只能来自 **Region 基础结构**（`IBP14`） |
| **DR27** | **谁生成 `FinalValue`？** ⇒ **WorldState 层（合成）**：`FinalValue = clamp01(BaseValue + Modifier)`（`RA76`；`PM24`/`PM29`；`WSC24` 同族）；WorldState **只做合成**，不得发明修正项、不得补偿/归一化 Region 贡献（`RA76`/`WSC43`） |
| **DR28** | **Runtime 是否拥有写权限？** ⇒ **没有**。Runtime（及玩家、Entity）对参数**只有只读权限**（`WorldParameter_Contract.md` §4 矩阵 Runtime 列全 `R`；`PX2`/`PM67`/`WSC58`） |
| **DR29** | **两段式发布在 Phase 1 的落法（确认既有口径）**：`BaseValue` 可先行定稿；`FinalValue` 只能在 **Region 基础结构就位后**定稿；下游只能读**已定稿**的 `FinalValue`（`PC19`/`PC21`/`PC50`；`PM61`） |
| **DR30** | **零修正退化**：Region 基础结构不存在或修正为空 ⇒ `FinalValue == BaseValue`（逐字段；`RM3`；`IBP50`） |

### 4.2 边界与例外

| # | 条款 |
|---|---|
| **DR31** | **`Elevation` 的特殊口径（确认既有契约）**：`Elevation` 是**只读转发值**（来源 = 既有高度场；`WSC48`/`WSC49`/`GC71`），其 `Modifier` 必须显式为 **`None`**（`PM32`）——**不是**"修正为 0" |
| **DR32** | **派生量不参与三态写关系**：`Balance` 等派生量不得被任何写入者直接写（`RA70`；`PM32` 同族） |
| **DR33** | **不裁决存储布局**：本决策只冻结"**生产关系**"（谁产出哪一态）；**谁在物理上持有数组/表**属 `B-3`，**保持未裁决** |
| **DR34** | **不裁决配置值 Owner 缺口**：`Seed`/`SoilDepth`/`WorldHeight`/基础值等**配置参数**的 Owner 登记缺口属 `B-2`，**保持未裁决**；Phase 1 只能使用**已登记**的配置值，若需要未登记值 ⇒ 停止并报告（`IBP52`） |
| **DR35** | **禁止第二套求值路径**：Phase 1 不得存在"另一处也算一遍参数"的实现（`WQ2`/`PC64`；`PM103` 同族） |
| **DR36** | **禁止下游写参数**：Biome/Geology/Resource/Ecology/Query/Runtime/Entity 均无参数写权限（`PW2`；`WSC53`–`WSC59`） |

---

## 5. D3 WorldState 与 ParameterState

### 5.1 决策（关系冻结）

| # | 条款 |
|---|---|
| **DR37** | **不存在两个世界真值**：`WorldState` 与 `ParameterState` **不是**两个并列真值（`WSC68`"参数只有一份真值"；`PM89`/`PM90` 禁止旧版本与多份状态） |
| **DR38** | **关系定义（裁决）**：**`ParameterState` 属于 `WorldState` 层的"参数面表示"**；`WorldState`（层）**引用**其 `ParameterState`（表示）作为"该层的参数结论"。即：**层 = 规则与合成权；State = 该层结论的表示** |
| **DR39** | **为什么不是两个真值**：`WorldParameter_Runtime_Model.md` §1 已明确该模型**不是** WorldState 生成器、**不做**求值/合成/钳制，只表示**已完成**的结果（`PM2`/`PM3`）；表示与产出不是两个真值 |
| **DR40** | **引用方向唯一**：`WorldState` → 引用 → `ParameterState`；**禁止反向**（`ParameterState` 不得反过来定义 WorldState 的规则、不得成为规则的输入） |
| **DR41** | **不产生"第二个 WorldState"**：`ParameterState` 不得携带超出参数三态与身份/解释引用的内容（`PM31` 字段闭集），不得携带 Biome/Geology/资源/生态/体素信息（`PM35`） |
| **DR42** | **只读发布**：`ParameterState` 发布后只读（`PM34`；`RA20` 同族）；Phase 1 不得存在"就地更新"语义 |
| **DR43** | **不裁决持久化**：`ParameterState` 是否需要存档属既有"参数无需存档"口径（`WSC65`/`PC78`/`PM7`），本记录**不新增**存档要求 |

### 5.2 与既有计划的衔接

| # | 条款 |
|---|---|
| **DR44** | 本裁决与 `WorldV2_Implementation_Boundary_Plan.md` §2.1 `IBP13`（World Parameter Evaluation → 返回 `WorldParameterState`）一致；不改变 Phase 1 允许项 |
| **DR45** | 本裁决**不**改变 `WorldParameter_Runtime_Model.md` 的任何条款：该模型仍是"表示契约"，仍禁止多份真值/旧版本/下游写（`PM89`/`PM90`/`PM84`） |
| **DR46** | **术语口径**：本记录中"`WorldState`"指**层（规则与合成权）**；"`WorldState` 的结论"以 `ParameterState` 表示；引用两者时必须保持该区分，避免被读成两个真值 |

---

## 6. D4 Phase 1 Query Boundary

### 6.1 允许读取（白名单，冻结）

| # | 允许 Query 读取 |
|---|---|
| **DR47** | **`Seed`**（以及由 Seed 派生的、不可变的种子上下文） |
| **DR48** | **`Coordinate`**（世界坐标及其经 VoxelCore 契约的换算语义，`DR19`） |
| **DR49** | **`ParameterState`**（`BaseValue` / `Modifier` / `FinalValue` 三态，只读；`PM81`） |
| **DR50** | **Region minimal data**（Phase 1 的 Region 基础结构所发布的最小只读数据：身份/来源记录/修正侧写；`IBP14`） |

| # | 规则 |
|---|---|
| **DR51** | 查询**只读**：不得创建/修改任何产物、不得触发重算（`WQ3`/`WQ5`；`WQ38`） |
| **DR52** | 查询结果是**终端产物**：不得成为任何生成/求值环的输入（`WQ4`；`PC104`） |
| **DR53** | 查询必须能回答**"为什么是这些值"**的最小解释：`Base` / `Modifier` / `Final` 的可分解说明 + "无修正/无影响"的显式说明（`IBP15`/`IBP45`；`PM100`/`PM107`） |

### 6.2 禁止读取（黑名单，冻结）

| # | 禁止 Query 读取 |
|---|---|
| **DR54** | **Chunk**（是否存在/是否加载/Chunk 编号作为判定） |
| **DR55** | **Mesh**（几何/呈现缓冲；`PC40`/`PC57`：Mesh 不是世界数据） |
| **DR56** | **Player**（位置/进度/背包/已探索/已采集；`PC54`/`WSC58`） |
| **DR57** | **Runtime**（Runtime State / 脏标记 / 加载集合 / tick；`PX2`；`PC53`） |
| **DR58** | **Entity**（任何对象/结构/生物的存在与状态；`EF1`） |

| # | 规则 |
|---|---|
| **DR59** | 黑名单为**封闭清单**：Phase 1 查询不得读取清单之外的"顺手可得"信息（如全局单例、静态缓存、场景查询） |
| **DR60** | 查询缓存（若实现）必须**透明**：可清空、不改变输出、不成为权威、按 `(Seed, 参数, 世界坐标, 域标签)` 索引（`WL9`/`WL10`） |
| **DR61** | 查询不得要求"世界实例"存活；连续两次查询之间不需要跨帧状态（`WL13`；`PM59`） |

---

## 7. D5 Determinism

### 7.1 决策（冻结）

> **同 `Seed` + 同 `Position` ⇒ 返回完全一致结果。**

| # | 条款 |
|---|---|
| **DR62** | 一致性范围：`BaseValue` / `Modifier` / `FinalValue` / 最小解释**逐字段相同**（`WSC83`/`WSV1`；`PMV6`；`PM52`） |
| **DR63** | 一致性必须对**任意顺序、任意实例、任意时刻**成立（可重复求值、无副作用；`WS1`/`PM2`） |
| **DR64** | **反例保护**：不同 `Seed`（或不同显式参数）⇒ 至少一处不同（防"恒真断言"；`WD3`；`RAV17`） |
| **DR65** | **域分离**：各环节只使用**已登记域**；不得复用保留域、不得新增域（`PZ11`/`PZ12`；`IBP51`） |

### 7.2 禁止来源（封闭清单）

| # | 禁止 |
|---|---|
| **DR66** | **时间**：`Time` / `DateTime` / 时间戳 / tick / 帧序（`PZ6`；`D2` 同族） |
| **DR67** | **随机状态**：`UnityEngine.Random` / `System.Random` / 任何有状态伪随机源（`PZ5`） |
| **DR68** | **加载顺序**：Chunk 加载/卸载顺序、邻居是否已求值、查询顺序、遍历顺序（`PZ8`；`F3`）；**Scene 状态**：场景/对象/Transform/渲染状态（`DR18`）；以及玩家状态（`PZ7`） |

| # | 规则 |
|---|---|
| **DR69** | Phase 1 的确定性要求 = `Boundary Plan` 里程碑四项要求的**同义冻结**：可重复（`IBP46`）、与 Scene 无关（`IBP47`）、与 Chunk 无关（`IBP48`）、与 Player 无关（`IBP49`） |
| **DR70** | 违反任一禁止来源 ⇒ 判定为**缺陷**（不是"实现细节选择"） |

---

## 8. Decision Table

| Decision ID | Question | Decision | Reason | Affected Future Phase |
|---|---|---|---|---|
| **D1** | Phase 1 的唯一空间输入是什么？世界坐标是否唯一来源？Chunk / Scene 是否参与参数求值？ | **世界坐标是唯一空间输入**；**Chunk 不参与**（仅分区/采样窗口/管理单位）；**Scene 不参与**（无 Transform/场景依赖）；坐标换算复用 VoxelCore 契约；Phase 1 无需 Chunk 语义 | `VoxelWorld_Core.md` §5；`Geology_Architecture_Contract.md` `GC86`；`PC86`；`F1`–`F3`；`WSC73`；`PM55`–`PM57` | Phase 1（直接）· Phase 2 Region（位置语义）· Phase 6 Voxel（采样仍按世界坐标）· Phase 7 Mesh（几何不得反哺世界数据） |
| **D2** | 谁生成 `BaseValue` / `Modifier` / `FinalValue`？Runtime 是否有写权限？ | `BaseValue` = **WorldState 层求值**；`Modifier` = **Region 层（唯一写入者）**；`FinalValue` = **WorldState 合成** `clamp01(Base + Modifier)`；**Runtime 无写权限**（只读） | `RA75`/`RA76`；`WorldParameter_Contract.md` §4 矩阵；`PX2`；`PM24`/`PM29`；`PC19`/`PC21` | Phase 1（直接）· Phase 2 Region（`Modifier` 供给）· Phase 6 Voxel（体素化只消费结论）· Phase 8 Runtime（无参数写权） |
| **D3** | `WorldState` 与 `ParameterState` 是什么关系？是否两个真值？ | **不是两个真值**：`ParameterState` 是 `WorldState` 层的**参数面表示**；`WorldState` **引用** `ParameterState`；引用方向单向、`State` 只读、字段闭集 | `WorldParameter_Runtime_Model.md` §1（`PM2`/`PM3`）、`PM31`/`PM34`；`WSC68`；`PM89`/`PM90` | Phase 1（直接）· Phase 2 Region（修正进入同一 State）· Phase 6 Voxel（消费同一 State）· Phase 8 Runtime（只读引用） |
| **D4** | Phase 1 Query 可以读取什么？禁止读取什么？ | **允许**：`Seed` · `Coordinate` · `ParameterState` · **Region minimal data**；**禁止**：`Chunk` · `Mesh` · `Player` · `Runtime` · `Entity`；查询只读、终端、带最小解释 | `WQ3`/`WQ4`/`WQ5`；`PW5`；`PC104`；`IBP15`；`WL9`/`WL13` | Phase 1（直接）· Phase 2 Region（查询只读区域结论）· Phase 6/7（查询不得成为生成输入）· Phase 8 Runtime（运行时只读展示） |
| **D5** | `Seed + Position` 的确定性要求？禁止哪些来源？ | **同 `Seed` + 同 `Position` ⇒ 完全一致**（逐字段、任意顺序/实例/时刻）；**禁止**：时间 · 随机状态 · 加载顺序 · Scene 状态（+ 玩家状态）；反例保护；域分离 | `WSC83`/`WSV1`；`PM52`/`PMV6`；`PZ5`–`PZ8`；`PZ11`/`PZ12`；`IBP46`–`IBP49` | Phase 1（直接）· 全部后续阶段（作为基线不变式） |

---

## 9. Impact

| # | 影响面 | 说明 |
|---|---|---|
| **DR71** | **Phase 1（直接生效）** | 空间输入唯一（`D1`）；参数三态生产关系冻结（`D2`）；`WorldState` ↔ `ParameterState` 单真值（`D3`）；查询白/黑名单冻结（`D4`）；确定性口径冻结（`D5`）——这五项构成 Phase 1 实现的前提 |
| **DR72** | **Phase 2 Region** | Region 的 `Modifier` **唯一写入者**口径不变（`D2`）；`FinalValue` 的合成仍由 WorldState 负责；**Plan Phase 2 需自行决定**的部分（采纳流程边界、上限、区域级资格推导）**不在本记录范围** |
| **DR73** | **Phase 6 Voxel** | 本记录**不裁决**体素写入者（`A-1` 保持未裁决，`DR11`）；但 `D1`/`D2` 确立的"世界坐标唯一来源 + 参数结论单向消费"会约束体素化阶段的输入方向（体素化必须消费结论，不得反向改写） |
| **DR74** | **Phase 7 Mesh** | `D4` 禁止查询读 Mesh；`D1` 禁止以 Scene/几何作为空间输入；Mesh 仍只是**只读派生**（`PC40`/`PC57`），不得成为世界数据来源 |
| **DR75** | **Phase 8 Runtime** | `D2` 冻结 Runtime **无参数写权限**（只读）；**Runtime Modification 的边界仍保持未裁决**（`B-1`，`DR13`），需独立决策文档 |
| **DR76** | **全阶段不变式** | `D5` 的确定性口径与 `D3` 的单真值口径**对所有后续阶段继续有效**；后续阶段不得以"新阶段需要"为由放宽这两项 |

---

## 10. Acceptance

| # | 不变量 | 验证方法 |
|---|---|---|
| **DRA1** | **无代码修改**：本次仅新增本文档；`Assets/**` 写入 = 0；既有规范 SHA 不变 | `git status` + mtime 扫描 + 哈希比对 |
| **DRA2** | **无 Legacy 依赖**：不新增任何 V2 → Legacy 的引用；Legacy 文件数量/哈希不变 | 结构扫描 + `Legacy_Status.md` 基线对照 |
| **DRA3** | **无新增参数**：本文档不新增参数，也不要求新增；Phase 1 只使用已登记参数/配置值 | 与 `WorldParameter_Contract.md` 附录 B 集合比对 |
| **DRA4** | **无新增因果边**：只使用 `Seed → Coordinate → Parameter → Region → Query` 与只读输入；不新增边 | 依赖图检查（DAG；`IBP39`） |
| **DRA5** | **不修改已有契约**：被引用契约的 SHA16 与本记录登记值一致 | 哈希比对（附录 C） |
| **DRA6** | **未裁决项保持未裁决**：`A-1`、`A-3`、`A-4`、`B-1`、`B-2`、`B-3`、`B-4`–`B-9` 在本文档中未被裁决、未被改变 | 与审计报告条目逐一对照（附录 A） |
| **DRA7** | **不扩大 Phase 1 范围**：允许项仍为五类；禁止项（Mesh / Terrain Rendering / Mining / Resource Generation / Biome Rule / Ecology / Runtime Editing）未开口子 | 与 `Boundary Plan` §2.1/§2.2 逐条对照 |
| **DRA8** | **决策完整**：`D1`–`D5` 各自都有"问题 / 决策 / 理由 / 影响面"（§8 决策表 5 行齐备） | 决策表完整性检查 |
| **DRA9** | **可机械核验**：`D1`–`D5` 的每一项都能转化为允许/禁止清单或可判定条件 | 逐条对照 `DR15`–`DR70` |
| **DRA10** | **声明性**：本文档不产生运行时效果、不改变任何体素输出、不改变既有验收结论 | 结构性论证（无代码参与） |

---

## 附录 A：未裁决项清单（保持未裁决）

| # | 未裁决项 | 来源 | 为什么不在本记录范围 |
|---|---|---|---|
| 1 | **体素写入最终方案**（Runtime 安装 vs Phase 6 抽样写入） | 审计 `A-1` / `CD-01` | 属 Plan Phase 6；本记录 `DR11` 明确排除 |
| 2 | Terrain 真值双轨的处置（场景/运行角色切换） | 审计 `A-2` / `CD-02` | 属接入阶段；Phase 1 不接入场景 |
| 3 | Phase 编号双轨（方案 A vs 方案 B） | 审计 `A-3` / `CD-03` | 属文档治理；本记录只用 `D1`–`D5` 与 `DR*` |
| 4 | 编号前缀多义（`PC`/`PM`/`WQ`/`RC`） | 审计 `A-4` / `CD-04` | 属文档治理；本记录使用独立前缀 |
| 5 | Runtime Modification 边界 | 审计 `B-1` / `CD-05` | 属 Plan Phase 8；`DR13` 明确排除 |
| 6 | 配置参数 Owner 缺口 | 审计 `B-2` / `CD-06` | `DR34` 明确保留未裁决 |
| 7 | 参数物理存储位置 | 审计 `B-3` / `CD-07` | `DR33` 明确保留未裁决 |
| 8 | 术语/图例多义 | 审计 `B-4`/`B-5` / `CD-08`/`CD-09` | 属术语治理 |
| 9 | 横向禁令缺口（`U` 单元） | 审计 `B-6` / `CD-10` | 与 Phase 1 无关 |
| 10 | `Elevation` 双角色澄清 | 审计 `B-7` | 既有契约已给出唯一通道口径（`GC71`/`WSC48`），本记录仅确认（`DR31`），不重新裁决 |
| 11 | 旧栈遗留文档/验证入口映射 | 审计 `B-8` / `CD-12` | 由 `Legacy_Status.md` 承接 |
| 12 | Phase 集合扩展登记（Mesh/Runtime 入管线） | 审计 `B-9` / `CD-11` | 属文档治理（`A-3` 同族） |

---

## 附录 B：编号与命名冲突登记（本记录自身）

| # | 冲突 | 处置 |
|---|---|---|
| 1 | 本记录的决策标签 **`D1`–`D5`** 与 `WorldGeneration_Geology.md` §8.2 的 **`D1`–`D6`**（确定性禁止项）同形 | 引用时**必须带限定**：`D1（本记录）` / `D1（Geology §8.2）`；本记录不修改既有文档 |
| 2 | 本记录条款前缀 `DR*`、验收 `DRA*` | 已核查：不与既有文档前缀冲突 |
| 3 | "Plan Phase"（`Boundary Plan`）与 "Pipeline Phase"（`WorldGeneration_Pipeline_Contract.md`） | 沿用 `Boundary Plan` 附录 A 的映射纪律；本记录不使用新的阶段编号 |

---

## 附录 C：基线（核验锚点）

| 文件 | SHA16 |
|---|---|
| `Specifications/Architecture/WorldV2_Implementation_Boundary_Plan.md` | `2B00E6D07A651698` |
| `Specifications/Architecture/Legacy_Status.md` | `9B8488B2D1335DC3` |
| `Specifications/Audit/WorldArchitecture_CrossDocument_Audit_V1.md` | `068664697B376128` |
| `Specifications/World/VoxelWorld_Core.md` | `F718B68E95100C57` |
| `Specifications/World/WorldGeneration_Core.md` | `684753D5E6B31565` |
| `Specifications/World/WorldParameter_Contract.md` | `532F9596F1348B57` |
| `Specifications/World/WorldParameter_Runtime_Model.md` | `B8ADCE96CA1AA773` |
| `Specifications/World/WorldState_Runtime_Contract.md` | `04B164617833A832` |
| `Specifications/World/WorldState_Design.md` | `D54CB1CC14859312` |
| `Specifications/World/Region_Design.md` | `77FA71B2620E7396` |
| `Specifications/World/Region_Architecture_Contract.md` | `90361CAB116697C1` |
| `Specifications/World/WorldQuery_Design.md` | `6D801C60F3C6D42F` |
| `Specifications/World/WorldQuery_Architecture_Contract.md` | `6635ED5334F9FD5C` |
| `Specifications/World/WorldGeneration_Pipeline_Contract.md` | `BEF7CFC1AF98AF3C` |
| `Assets/Scripts/VoxelWorld/VoxelChunk.cs` | `4DBDFBAC5BCE500C` |
| `Assets/Scripts/VoxelWorld/VoxelWorld.cs` | `D9E749A466635918` |
| `Assets/Scripts/VoxelWorld/Generation/HeightFunction.cs` | `715341D5FC652408` |

---

## 附录 D：变更规则

1. 修改 `D1`–`D5` 的任一项裁决、或修改 §8 决策表的"决策/理由/影响面"，**必须**修改本文档并升版本。
2. **不得放宽**：`DR15`–`DR20`（坐标唯一来源）、`DR25`–`DR28`（生产关系与 Runtime 无写权限）、`DR37`–`DR42`（单真值）、`DR47`–`DR61`（查询边界）、`DR62`–`DR70`（确定性）。
3. **不得扩张**：不得借本记录扩大 Phase 1 范围、不得借本记录裁决 `DR11`–`DR13` 未决项。
4. 若实现过程中发现某项裁决与契约冲突、或需要未登记参数/新契约 ⇒ **停止并报告**（`IBP26`/`IBP52`/`IBP53`），不得就地处理。
5. 后续阶段若需要与本文档不同的口径，必须**独立决策文档**说明理由，并证明不破坏 `D3`（单真值）与 `D5`（确定性）。
