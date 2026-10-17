# World Generation Pipeline Contract V1

> **文档性质**：**调度契约规范（Pipeline Contract）**。本文档是**所有 World 系统的调度契约**，只定义：
> **阶段顺序 · 输入输出关系 · 数据生命周期 · 依赖方向 · 禁止跨阶段访问**。
> **仅创建 Markdown**；**不修改任何 `.cs`**；**不修改 `Assets`**；**不创建 class / interface / struct**；**不实现任何 Pipeline 代码**；**不修改任何已有规范**。
>
> **核心定位**：
>
> > **Pipeline 只负责"什么时候调用什么层"。它不拥有任何生成规则。**
>
> | Pipeline **不是** | 说明 |
> |---|---|
> | **不是 Generator** | 不产生骨架、参数、区域、群系、地质、体素、网格；不定义任何判定规则 |
> | **不是 WorldState** | 不拥有参数、不做合成、不做钳制（合成权归 WorldState 层） |
> | **不是 Runtime** | 不管理加载/卸载、不持有跨帧状态、不处理玩家输入 |
> | **不是第二套规则系统** | 不定义阈值、噪声、优先级链（各层自己的规范是唯一权威） |
> | **不是缓存/存储** | 不定义数据布局、不持有世界数据（生命周期只作**约束**，见 §4） |
>
> **必须先读取的上游规范**（本任务已读取，SHA 校验通过；**不得修改**）：
>
> | 规范 | 与本契约的关系 |
> |---|---|
> | `WorldState_Design.md` | 参数语义、`[0,1]` 归一化（§1.3）、`Base + RegionModifier`（§2.1）、群系规则表（§5.3）、退化兼容（§5.8）、骨架规则（§7.4 `WC2`） |
> | `WorldParameter_Contract.md` | 参数身份/量纲/Owner/权限（§3、§4 矩阵：WorldState = `O（合成）`、Region = `P（修正项）`）、`PW3`/`PW9`、`PC2`/`PC11`、域表（`Skeleton`=1、`Region.*`=8–12、`Biome`=18）、§7 `PX1`–`PX6`（Generation vs Runtime） |
> | `Region_Architecture_Contract.md` | §4 六步采纳、§5 合成契约、**`RA75`**（`RegionModifier` 唯一写入者 = Region）、**`RA76`**（WorldState 只做合成）、**`RA101`**（唯一间接路径）、`RA140`/`RAV8`/`RAV12`/`RAV13` |
> | `Biome_Architecture_Contract.md` | `BC34`（分类确定性）、§9 局部求值（**`BC103`–`BC107`**：禁全量群系/群系地图/邻域依赖）、`BC122`（无写权限） |
> | `Geology_Architecture_Contract.md` | `GC2`（Geology 位于 Phase 6）、`GC51`/`GC52`（不拥有体素、正确链 `Geology → Density Evaluation → Voxel Query → Mesh`）、`GC66`（P3 唯一读 Biome）、`GC139`–`GC146`（矩阵自查）、`GCV17`/`GCV22` |
> | `WorldState_Runtime_Contract.md` | §2 求值链与内部顺序、`WSC51`/`WSC52`（WorldState → Geology 只经 P3）、`WSC60`/`WSC63`/`WSC68`（Runtime 只读、无编辑后参数）、`WSC78`/`WSV14`（玩家修改隔离） |
> | `WorldQuery_Design.md` | §2.2 查询链、**`WQ1`**（同源规则）、**`WQ4`**（查询不得成为生成输入）、`WL4`/`WL9`/`WL13`（局部求值与透明缓存）、`WE1`（解释四要素） |
> | `Resource_Design.md` | **`RN7`/`RN8`**（Biome 不得作为资源判定输入；正确输入 = 参数 + 地质 + 纪元）、§4.2（两层存在性） |
> | `Ecology_Design.md` | `EA2`（Ecology 输入白名单）、`EA3`（产出只有候选）、**`E-3`**（不改世界）、`EG3`/`EF1`（禁止反向写） |
> | `WorldGeneration_Pipeline_Design.md`（既有） | **阶段划分的既有权威**（Phase 0–7）；本契约与其差异见**附录 D**（只登记，不改该文件） |
>
> **冲突时的优先级**：**各层规则**以其自身规范为准；**层间顺序与输入封闭**以 `WorldGeneration_Pipeline_Design.md` 为准；
> **因果方向**以 `WorldState_Design.md` / `Region_Architecture_Contract.md` / `WorldState_Runtime_Contract.md` 为准；
> **调度契约（阶段编号 · 生命周期 · 依赖方向 · 跨阶段访问禁令）** 以**本文档**为准。
> **依赖方向优先于编号顺序**：当二者冲突时，以因果方向为准（`PC61`；附录 D ②）。
>
> **零修改声明**：本文档是**声明性文档**，不产生任何运行时效果；开启/关闭本文档**不得改变任何体素输出**（`PCV11`）。
> 与既有规范的差异一律在**附录 D 登记**，**不修正**任何既有文件。

---

## §1 Pipeline Position and Boundary

| # | 规则 |
|---|---|
| **PC1** | **Pipeline = 调度契约**：定义"**什么时候调用什么层**"，以及阶段之间的输入输出、生命周期与访问边界 |
| **PC2** | **Pipeline ≠ Generator**：Pipeline 不产生任何数据；每阶段的产物由该层自己的规则产生（Pipeline 只规定"该阶段必须已产出什么"） |
| **PC3** | **Pipeline ≠ WorldState**：不拥有参数、不做 `Base + Modifier` 合成、不做钳制；合成权归 WorldState 层（`WorldParameter_Contract.md` §4 矩阵；`RA76`） |
| **PC4** | **Pipeline ≠ Runtime**：不管理加载/卸载、不持有跨帧状态、不处理玩家输入；Runtime 的调度归 Runtime/Streaming 规范 |
| **PC5** | **Pipeline 只定义五件事**：① 阶段顺序（§2）；② 输入输出关系（§2）；③ 数据生命周期（§4）；④ 依赖方向（§3）；⑤ 禁止跨阶段访问（§3/§8） |
| **PC6** | **Pipeline 不拥有任何生成规则**：阈值、噪声、优先级链、群系规则表、资源判定规则一律归各层规范（`WQ1` 同源纪律） |
| **PC7** | **Pipeline 不实现算法**：不定义线程/Job/Burst/ECS/Streaming 算法/缓存算法（§9） |
| **PC8** | **Pipeline 不产生数据**：任何"Pipeline 产物"都是**引用**（阶段输出的所有权归对应层） |
| **PC9** | **Pipeline 只读消费各层规范**：阶段契约中的"输出"必须是该层规范已定义的产物名称 |
| **PC10** | **声明性**：开启/关闭本文档 ⇒ 体素输出**逐体素一致**（`PCV11`） |

---

## §2 Generation Phase Contract

> **冻结以下 Phase（编号、名称、输出均不得改名）**：Phase 0 `Seed Initialization` · Phase 1 `World Skeleton Generation` · Phase 2 `World Parameter Evaluation` · Phase 3 `Region Evaluation` · Phase 4 `Biome Classification` · Phase 5 `Geology Evaluation` · Phase 6 `Voxel Evaluation` · Phase 7 `Mesh Construction` · Phase 8 `Runtime Activation`。
> 每个 Phase 必须定义 **Purpose / Input / Output / Allowed Read / Forbidden Read / Forbidden Write**。

### Phase 0 — Seed Initialization

| 项 | 内容 |
|---|---|
| **Purpose** | 建立全流程唯一的种子上下文（确定性起点） |
| **Input** | World Seed（外部显式输入） |
| **Output** | **Seed Context** |
| **Allowed Read** | World Seed、显式世界创建参数 |
| **Forbidden Read** | 一切运行时状态、玩家状态、其它阶段的产物 |
| **Forbidden Write** | 除 Seed Context 之外的一切 |

| # | 规则 |
|---|---|
| **PC11** | Seed Context 必须是**不可变**的：一经建立不得修改（不得被任何下游写入） |
| **PC12** | Seed Context 必须携带**域分离所需的最小信息**（`Seed` 与显式参数）；域编码归 `WorldParameter_Contract.md` §6.4 |
| **PC13** | 禁止在 Seed Context 中携带任何"世界数据"（骨架/区域/参数值/体素） |
| **PC14** | 种子初始化必须**与时间/玩家/环境无关**（不得使用 `Time`/`DateTime`/`Guid`/系统随机） |

### Phase 1 — World Skeleton Generation

| 项 | 内容 |
|---|---|
| **Purpose** | 提供全流程的**舞台框架**（大陆/海洋、基础气候带、基础高度框架） |
| **Input** | Seed Context |
| **Output** | **WorldSkeleton** |
| **Allowed Read** | Seed Context（只读）；**既有高度场 `HeightFunction`（只读复用，不得改变其语义、输出与实现）** |
| **Forbidden Read** | Phase 2–8 的任何产物（History/Region/WorldState/Biome/Geology/Voxel/Mesh/Runtime） |
| **Forbidden Write** | 除 WorldSkeleton 之外的一切 |

| # | 规则 |
|---|---|
| **PC15** | WorldSkeleton 是**框架提供者**，不是因果链的一环（既有 `Pipeline_Design` §2.3 `PC1`）；只被下游读取 |
| **PC16** | 骨架**不得替换**既有高度场定义：掩码只做叠加；掩码退化为"全部陆地" ⇒ 回到既有无限地形（`WC2`） |
| **PC17** | 骨架**不得**读取 History/Region/Biome/Geology 的任何结论（无反向、无旁路） |
| **PC18** | 骨架输出必须是世界坐标的纯函数（同 Seed + 坐标 ⇒ 同骨架，与加载顺序无关） |

### Phase 2 — World Parameter Evaluation

| 项 | 内容 |
|---|---|
| **Purpose** | 求值 8 个环境参数：`Base` 与 `FinalState` |
| **Input** | Seed Context + WorldSkeleton + **RegionModifier（来自 Phase 3）** |
| **Output** | **WorldParameter FinalState**（8 参数） |
| **Allowed Read** | Seed Context、WorldSkeleton、显式参数配置、**RegionModifier**（只读） |
| **Forbidden Read** | Biome / Geology / Voxel / Mesh / Runtime 的任何产物；玩家状态；体素真值 |
| **Forbidden Write** | 除参数求值结果之外的一切（不得写 Region、不得写骨架、不得写体素） |

| # | 规则 |
|---|---|
| **PC19** | **两段式发布（冻结）**：① **`WorldParameterBase`** 在第一段可定稿（仅依赖 Seed Context + WorldSkeleton + 配置）；② **`WorldParameterFinal = clamp01(Base + RegionModifier)`** 在 **Phase 3 完成之后**定稿 |
| **PC20** | `RegionModifier` 的**唯一写入者**是 Region 层（`RA75`）；WorldState 侧**只做合成**（`RA76`），不得发明/补偿/归一化修正 |
| **PC21** | **下游（Phase 4 起）只能读"已定稿的" FinalState**：不得读未合成的 Base 代替 FinalState，也不得读 Region 内部字段 |
| **PC22** | 参数必须落在契约范围（连续量 `[0,1]`；`Elevation` 整数；`Ocean` 布尔）；`Balance` 为派生只读量（不得被修正项直写） |

### Phase 3 — Region Evaluation

| 项 | 内容 |
|---|---|
| **Purpose** | 建立区域上下文：来源采纳、修正合成、只读标签与资格 |
| **Input** | Seed Context + WorldSkeleton + **History 事实（只读；Region Source 候选）** + 自然规则 + 显式 Region 参数 |
| **Output** | **Region Context**（含 Region 集合、`RegionModifier`、只读标签、资格声明） |
| **Allowed Read** | Seed Context、WorldSkeleton、**History 事实/`HistoryTags`（只读）**、显式 Region 参数、地形派生量（`Elevation`/`Ocean`，只读） |
| **Forbidden Read** | WorldParameter FinalState（尚在定稿中）、Biome、Geology、Voxel、Mesh、Runtime、玩家状态、已生成内容 |
| **Forbidden Write** | 除 Region Context（含 `RegionModifier`）之外的一切 |

| # | 规则 |
|---|---|
| **PC23** | Region 的采纳必须遵守 `Region_Architecture_Contract.md` §4 的**六步流程**（准入 / 规范化 / 全序 / 上限 / 物化 / 只读发布） |
| **PC24** | `RegionModifier` 的分量集合**恰好 6 个**（`Temperature`/`Moisture`/`Fertility`/`MagicDensity`/`HolyInfluence`/`CorruptionInfluence`）；不含 `Elevation`/`Ocean`/`Balance` |
| **PC25** | 叠加必须满足**交换律**与"无放大反馈""无覆盖语义"（`RA63`/`RA66`；`RAV8`） |
| **PC26** | **History 的接入方式（冻结）**：History 不在本契约的 Phase 编号内（见附录 D ①）；其产物以 **Region Source 候选 + `HistoryTags`（只读）** 的形式作为 Phase 3 的**输入** |

### Phase 4 — Biome Classification

| 项 | 内容 |
|---|---|
| **Purpose** | 把环境参数向量解释为离散的环境类别 |
| **Input** | 已定稿的 **WorldParameter FinalState** |
| **Output** | **Biome Identity**（Category + Profile + Explanation data） |
| **Allowed Read** | FinalState（8 参数，只读）、Seed、Position、域 `18 = Biome` 所需的域标签（`WorldParameter_Contract.md` §6.4） |
| **Forbidden Read** | Region 内部字段（`RegionType`/`RegionView`/Modifier 原始分量）、Geology 产物、Resource/Ecology 结果、Runtime/玩家状态、体素真值 |
| **Forbidden Write** | 一切（Biome 输出只读；`BC122`） |

| # | 规则 |
|---|---|
| **PC27** | 分类必须遵循 `WorldState_Design.md` §5.3 的**有序规则表（first-match-wins）**；`Ocean == true` **短路** |
| **PC28** | 分类必须支持**单点求值**；**禁止**生成全部群系、存储世界群系、创建群系地图、依赖相邻 Chunk（`BC103`–`BC107`） |
| **PC29** | Biome Identity **不得包含**材料、体素类型、资源清单、生物/植被清单（Biome 是分类，不是内容容器） |
| **PC30** | 分类必须**可关闭**：关闭后地表材料退化为既有行为，且与既有基线逐体素一致（`WorldState_Design.md` §5.8） |

### Phase 5 — Geology Evaluation

| 项 | 内容 |
|---|---|
| **Purpose** | 地质解释：地形形成、密度倾向、地表/地下结构、洞穴倾向、矿藏倾向 |
| **Input** | 世界坐标 + 显式地质配置 + WorldSkeleton 高度框架 + **Biome Identity（只读，仅 P3）** |
| **Output** | **Geology State**（连续解释结果：`DensityTendency` / `SurfaceLayerInterpretation` / `SubsurfaceInterpretation` / `CavePotential` / `DepositPotential` / `Elevation` 转发） |
| **Allowed Read** | 世界坐标、Seed Context、WorldSkeleton、显式地质配置、Biome Identity（仅 P3）、既有保留域通道（`R-1`/`R-2`） |
| **Forbidden Read** | 8 参数直读、Region 产物/`RegionType`、History 事实、Resource/Ecology 结果、Voxel 真值、Runtime/玩家状态、Mesh、Chunk 生命周期 |
| **Forbidden Write** | 一切生成产物（含体素、Chunk、Mesh）（`GC139`–`GC146`） |

| # | 规则 |
|---|---|
| **PC31** | 判定必须遵守**唯一优先级链** `P0 WorldBounds → P1 Cave → P2 Terrain → P3 Material`（`GC63`/`GC64`） |
| **PC32** | **P0/P1/P2 与 Biome/Region/History/参数/资源全部无关**（`GC65`）；**P3 是唯一可读 Biome 的位置**（`GC66`） |
| **PC33** | 地质输出必须是世界坐标纯函数（跨 Chunk 边界一致；`GC107`–`GC109`） |
| **PC34** | **P3 的 Biome 影响只能作用于地表层**（`y == SurfaceHeight`），取值限 `{Grass, Dirt, Stone}`（`GC67`） |

### Phase 6 — Voxel Evaluation

| 项 | 内容 |
|---|---|
| **Purpose** | 把地质判定**抽样**为体素状态，形成体素真值（世界真值） |
| **Input** | Geology State（只读）+ 显式体素化配置 |
| **Output** | **Voxel Query Result**（世界坐标 → 体素状态） |
| **Allowed Read** | Geology State、世界坐标、显式配置 |
| **Forbidden Read** | Biome 结论（不得绕过 Geology 的 P3）、Resource/Ecology 结果、玩家状态（除非"当前真值"语义显式声明，见 `PC37`）、Mesh |
| **Forbidden Write** | 上游任何阶段（不得回写 Geology/Biome/Region/WorldState/骨架） |

| # | 规则 |
|---|---|
| **PC35** | **Geology 不拥有体素**：体素真值的持有者是世界侧（`VoxelWorld`/`VoxelChunk`）；Phase 6 只负责**抽样与写入动作**（`GC51`/`GC52`） |
| **PC36** | 体素状态集合**冻结**为 `{Air, Grass, Dirt, Stone}`；**禁止新增 `VoxelType`** |
| **PC37** | **必须区分两种真值语义**（审查 `AD-2` 未决项，登记不改规则）：**Pristine**（仅规则抽样）与 **Current**（规则结果 ⊕ 玩家编辑）；本契约只要求**语义必须显式声明**，不得混用 |
| **PC38** | 体素写入**不得影响**任何生成阶段的输出（生成不覆盖玩家修改；生成结果不被回写） |

### Phase 7 — Mesh Construction

| 项 | 内容 |
|---|---|
| **Purpose** | 从体素数据构造可渲染几何 |
| **Input** | Voxel Query Result（只读） |
| **Output** | **Render Mesh** |
| **Allowed Read** | 体素数据、显式网格化配置 |
| **Forbidden Read** | Geology State、Biome Identity、Region、WorldState、History、Resource/Ecology 结果 |
| **Forbidden Write** | 一切生成产物（Mesh 是**只读派生**） |

| # | 规则 |
|---|---|
| **PC39** | Mesh **只从体素数据派生**（`VoxelWorld_Mesh.md`：只读、不改真值） |
| **PC40** | Mesh 不得反向影响体素、不得反向影响任何生成阶段（`Mesh → Generation` 禁止，`PC57`） |
| **PC41** | Mesh 不得成为真值来源：几何可丢弃、可重建，结果由体素唯一决定 |
| **PC42** | Mesh 不得要求生成规则变化（不得为几何需要而新增体素类型/材料/子网格语义） |

### Phase 8 — Runtime Activation

| 项 | 内容 |
|---|---|
| **Purpose** | 把世界接入运行时：实体、交互、生命周期管理 |
| **Input** | 体素真值（只读）+ 显式运行时配置 |
| **Output** | **Runtime Entity / Interaction** |
| **Allowed Read** | 体素真值（只读）、生成期只读产物（如参数/Biome/Region 标签，**仅只读**） |
| **Forbidden Read** | 生成期的**写入权**（不得改写任何生成产物） |
| **Forbidden Write** | **一切生成产物**（参数、RegionModifier、Biome、Geology State、体素真值以外的生成数据） |

| # | 规则 |
|---|---|
| **PC43** | Runtime 只能**消费**生成结果；玩家编辑只能进入 **Runtime State**（体素编辑 / 玩家状态），不得回写 Generation（`WSC60`/`WSC61`/`PX2`） |
| **PC44** | **玩家修改隔离**：玩家编辑前后，同一 `(Seed, Position)` 的 8 个参数逐字段不变（`WSC78`/`WSV14`） |
| **PC45** | `Runtime → Generation`、`Player → Generation`、`Entity → Generation` **一律禁止**（§3） |
| **PC46** | 运行时不得要求"生成期重跑"：生成不覆盖玩家修改；已生成区域不因运行时状态而变化 |

### §2 通用规则（适用于 Phase 0–8）

| # | 规则 |
|---|---|
| **PC47** | **Phase 顺序固定且不可交换**：任何"跳过 / 换序 / 并行破坏优先级"的实现即为缺陷（`PCV3`） |
| **PC48** | **输出即下游输入**：禁止旁路引用（例如 Phase 4 不得直接读骨架或 Region 内部字段；Phase 7 不得直接读 Geology State）（`PA5`） |
| **PC49** | **每个系统只有一个入口**：Phase k 的产物只能由 Phase k 产出（唯一生产者），其它阶段只能只读消费（`PCV4`） |
| **PC50** | **定稿点规则**：任何阶段的输出必须在**进入下一阶段之前定稿**（含 Phase 2 的第二段：`FinalState` 在 Phase 3 之后定稿，见 `PC19`） |

---

## §3 Dependency Direction Contract

### 3.1 冻结的允许链

```
Seed
 ↓
Skeleton
 ↓
WorldState
 ↓
Region
 ↓
Biome
 ↓
Geology
 ↓
Voxel
 ↓
Mesh
 ↓
Runtime
```

| # | 规则 |
|---|---|
| **PC51** | 上表是**唯一下载链**；允许的读边只能沿该链方向（或读同阶段输入） |
| **PC52** | **允许边（枚举，共 9 条）**：`Seed→Skeleton`、`Seed→WorldState`、`Seed→Region`、**`Skeleton→Region`（只读；本轮补登记；其产物接口与查询契约的权威见附录 B）**、`Skeleton→WorldState`、`Region→WorldState`（**仅 `RegionModifier`**）、`WorldState→Biome`、`Biome→Geology`（**仅 P3**）、`Geology→Voxel`；以及 `Voxel→Mesh`、`Mesh/Runtime` 的只读消费边 |

### 3.2 明确禁止（冻结）

| # | 禁止的边 | 说明 | 依据 |
|---|---|---|---|
| **PC53** | **Runtime → Generation** | 运行时不得回写任何生成产物 | `PX2`；`WSC61` |
| **PC54** | **Player → Generation** | 玩家状态不得参与生成；玩家编辑只进 Runtime State | `WSC58`；`WSC78` |
| **PC55** | **Entity → Generation** | 对象不得改写生成规则/产物 | `EF1`；`PT12` |
| **PC56** | **Voxel → Geology** | 体素真值不得反向影响地质判定 | `GC62`；`WSC52` |
| **PC57** | **Mesh → Generation** | 网格不得反向影响任何生成阶段 | `VoxelWorld_Mesh.md`（只读派生）；`GC23` |
| **PC58** | **Resource → Biome** | 资源判定不得作为群系输入（群系是结果） | `RN7`；`BC92` 同构 |
| **PC59** | **Ecology → WorldState** | 生态不得回写参数（生态是结果） | `E-3`；`EA1` |

### 3.3 依赖方向通用规则

| # | 规则 |
|---|---|
| **PC60** | **泛化禁令**：**任何"下游 → 上游"的读或写边一律禁止**（`PC53`–`PC59` 是实例化） |
| **PC61** | **依赖方向优先于编号顺序**：当阶段编号与因果方向冲突时（如 Phase 2 与 Phase 3），以**因果方向**为准（附录 D ②） |
| **PC62** | **禁止跨阶段访问（skip-layer）**：不得跳过中间阶段读取更下游产物（`PA5`） |
| **PC63** | **禁止回写已完成阶段**：任何阶段不得修改已定稿的上游输出 |
| **PC64** | **禁止同一规则两处实现**：每层规则只能有一份实现（`WQ1`/`WQ2`；`PG7` 同构） |
| **PC65** | 禁止"以结果为条件改变输入"（例如"因为检测到体素为空，所以调高魔力"） |
| **PC66** | 违反 §3 任一条 ⇒ 判定为**缺陷**（`PCV1`/`PCV2`） |

---

## §4 Data Lifetime Contract

### 4.1 生命周期定义（冻结）

| # | 生命周期 | 覆盖数据 | 边界要求 |
|---|---|---|---|
| **PC67** | **World Lifetime** | `Seed`、`WorldSkeleton`、`WorldParameter` | 与"世界"同寿；**可重算**（由种子 + 坐标 + 显式参数），**不需要存档** |
| **PC68** | **Region Lifetime** | `Region Context`（Region 集合 / `RegionModifier` / 只读标签 / 资格） | 与 Region 规则同寿；**不得跨帧存活为权威**（可按需重算；缓存必须透明） |
| **PC69** | **Point Evaluation** | `Biome`、`Geology State`、`Density` | 由 `(Seed, Domain, Position)` 唯一确定；**逐点可求值、可丢弃** |
| **PC70** | **Chunk Lifetime** | `Voxel Query Cache`、`Mesh` | 与 Chunk/表示同寿；**可丢弃可重建**；**不承载生成规则** |
| **PC71** | **Runtime Lifetime** | `Entity`、`Player`、`Modification` | 与运行时会话同寿；**唯一可能需要存档的生命周期** |

### 4.2 生命周期规则

| # | 规则 |
|---|---|
| **PC72** | **生命周期不得反向依赖**：长寿命数据不得引用短寿命数据（例：World Lifetime 不得持有 Chunk/Mesh 引用） |
| **PC73** | **Generation 数据不得依赖 Runtime 生命周期**（用户要求）：生成产物的有效性不得以"运行时对象存在"为前提 |
| **PC74** | 跨生命周期读取只允许**只读**且方向必须沿 §3.1 允许链 |
| **PC75** | **短生命周期结束不得改变生成结果**：卸载/丢弃 Chunk、Mesh、Runtime 对象后，重新求值必须得到相同结果 |
| **PC76** | `Chunk Lifetime` 的缓存（若存在）必须**透明**：由 `(Seed, 参数, 世界坐标, 域标签)` 索引、可清空、不改变输出、不成为权威（`WL9`/`WL10`） |
| **PC77** | **不得要求"世界实例"存活**才能求值：连续两次单点求值之间不需要任何跨帧状态（`WL13`；`WSC66`） |
| **PC78** | **存档边界**：只有 `Runtime Lifetime` 数据可被持久化为世界演进的一部分；`World/Region/Point` 生命周期数据必须可**纯函数重算**（`PX`/参数契约 §9.2 第 6 项） |

---

## §5 Chunk Interaction Contract

| # | 规则 |
|---|---|
| **PC79** | **Chunk 不是生成单位**：Chunk 划分与世界的性质无关（`WorldGeneration_Geology.md` §2.1） |
| **PC80** | **Chunk 是"局部采样和缓存单位"**：它只承载"局部体素的采样结果与派生表示"，不承载生成规则 |
| **PC81** | **禁止 Chunk 决定 Biome**：群系结论只能由 `FinalState` 决定；不得按 Chunk 划分群系、不得按 Chunk 缓存群系归属表（`BC105`/`BC107`） |
| **PC82** | **禁止 Chunk 决定 Geology**：地质判定只能依赖世界坐标；不得按 Chunk 注入随机性、不得让相邻 Chunk 规则不同（`F1`/`F2`） |
| **PC83** | **禁止 Chunk 产生随机规则**：任何"每 Chunk 自带噪声参数/种子/规则集"一律禁止 |
| **PC84** | **禁止 Chunk 保存世界状态**：Chunk 不得作为参数的权威、不得作为 Region/Biome/地质判定的存储、不得持有"世界副本" |
| **PC85** | Chunk 边界**不得与世界性质对齐**：不得出现"边界修补"式特例（`GIV2`/`GC38`） |
| **PC86** | **规则签名不得含 Chunk 粒度**：所有生成层的求值入口只能以**世界坐标**为自变量（`GIV2`/`GC122`） |
| **PC87** | Chunk 的加载/卸载**不得改变生成结果**（`F3`；`WL8`） |
| **PC88** | Chunk 级缓存必须**透明**且**非权威**（`PC76`）；缓存清理不得影响任何求值结果 |

---

## §6 Streaming Contract

| # | 规则 |
|---|---|
| **PC89** | **Streaming 只能请求三类动作**：① **Voxel Query**（Phase 6）；② **Mesh Build**（Phase 7）；③ **Runtime Activation**（Phase 8） |
| **PC90** | **Streaming 不能改变生成结果**：不得改写任何生成产物、不得改变判定输入、不得触发"另一套规则" |
| **PC91** | Streaming **不得请求**上游阶段"按 Chunk 重算"（不得以 Chunk 为粒度调用 Region/Biome/Geology 规则；这些层按**世界坐标**求值） |
| **PC92** | **加载集合判定与规则无关**：何时加载、加载哪些 Chunk 属调度决策，不得影响任何规则结果（`WorldState_Design.md` 世界规则 vs 调度分离） |
| **PC93** | **卸载不得改变结果**：卸载后重新求值必须得到逐字段相同结果（`PC75`/`PC87`） |
| **PC94** | **顺序无关**：Streaming 的请求顺序（含并发）不得改变生成结果（`WL12`） |
| **PC95** | **预算无关**：帧预算/节流只影响**时机**，不得影响**结果**（不得因预算不足而改变判定） |
| **PC96** | Streaming **不是世界状态所有者**：不得持有"世界数据"作为权威；其状态属 Runtime Lifetime（`PC71`） |

---

## §7 Explanation Contract

| # | 规则 |
|---|---|
| **PC97** | Pipeline 必须支持 `WorldQuery` 的"**为什么这里是这样的？**"：每个阶段都必须能给出该阶段的**判定依据**（命中条件、参与输入、结论） |
| **PC98** | **解释路径必须与生成路径一致**：解释必须复用生成所用的同一套规则与同一阶段顺序（`WQ1`；`PC64`） |
| **PC99** | **禁止 Debug/工具使用另一套计算**：任何"调试用的近似/简化/独立实现"一律禁止（否则解释与生成必然分叉） |
| **PC100** | 解释必须**可分解到阶段（层）**：能指明"该结论由哪个 Phase 的哪条条件产生"（`WE1` 四要素：结论 / 直接条件 / 所在层 / 上游原因） |
| **PC101** | 解释必须**只读**：解释不得改变任何阶段输出或缓存语义（`WQ5`） |
| **PC102** | 解释必须**确定性**：同一 `(Seed, Position)` 的解释内容与顺序恒定（`WE4`） |
| **PC103** | 解释粒度**到阶段/层与条件为止**：不含对象、物品、生态内容细节（`WE7`） |
| **PC104** | 解释**不得成为任何阶段的输入**：查询/解释是终端产物（`WQ4`；`PCV13`） |

---

## §8 Consumer Permission Matrix

**图例**：`O` = 该阶段产出/拥有；`R` = 只读；`R*` = **仅解释**只读；`—` = 不适用；`✗` = **禁止**。

| Phase ＼ System | WorldState | Region | Biome | Geology | Resource | Ecology | Voxel | Mesh | Runtime | WorldQuery |
|---|---|---|---|---|---|---|---|---|---|---|
| **P0 Seed Initialization** | `O`（Seed Context 提供） | `R` | `R` | `R` | `R` | `R` | `R` | `R` | `R` | `R` |
| **P1 World Skeleton** | `R`（框架） | `R` | `—` | `R` | `—` | `—` | `—` | `—` | `—` | `R` |
| **P2 World Parameter** | **`O`**（FinalState） | `R` | `R` | ✗（`PC31`/`GC25`） | `R` | `R` | `R` | `—` | `R` | `R` |
| **P3 Region** | `O`（Modifier 供给） | **`O`**（Region Context） | `R`（经参数） | ✗ | `R` | `R` | `—` | `—` | `R` | `R` |
| **P4 Biome** | ✗ | ✗ | **`O`**（Biome Identity） | `R`（仅 P3） | `R*`（仅解释，`RN7`） | `R` | ✗ | ✗ | `R` | `R` |
| **P5 Geology** | ✗ | ✗ | ✗ | **`O`**（Geology State） | `R`（介质/矿藏） | `R`（地形庇护） | `R`（抽样输入） | ✗ | `R` | `R` |
| **P6 Voxel** | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | **`O`**（体素真值） | `R`（派生输入） | `R` | `R` |
| **P7 Mesh** | ✗ | ✗ | ✗ | ✗ | ✗ | ✗ | `R`（只读） | **`O`**（Render Mesh） | `R` | `R` |
| **P8 Runtime** | `R` | `R` | `R` | `R` | `R` | `R` | `R` | `R` | **`O`**（Entity/Interaction） | `R` |

**矩阵自查**

| # | 自查项 | 期望 |
|---|---|---|
| **PC105** | 矩阵必须按上表冻结；图例与单元语义唯一 | 结构一致性 |
| **PC106** | **每个阶段产物只有一个 `O`**（唯一生产者） | `O` 计数 = 1/行 |
| **PC107** | `Runtime` 列是否出现 `O` 或写权限？ | **必须为 0**（Runtime 只读消费） |
| **PC108** | `WorldQuery` 列是否出现 `O` 或写权限？ | **必须为 0**（查询只读，`WQ4`） |
| **PC109** | `Resource` / `Ecology` 列是否只出现 `R` / `R*`？ | **必须是**（不得写任何上游） |
| **PC110** | 是否存在"下游行在上游列出现 `R`"（= 反向读）？ | **必须为 0**（`PC60`） |
| **PC111** | `Mesh` 列是否只被 Phase 6/7/8 访问？ | **必须是** |
| **PC112** | `Geology` 列是否出现来自 P3/P4 的直读 `R`（除 Biome 的 P3 通道）？ | **必须为 0** |
| **PC113** | `Voxel` 列是否被 P0–P5 访问？ | **必须为 0** |
| **PC114** | 是否存在"某阶段同时读其下游并写其上游"的行？ | **必须为 0** |

---

## §9 V1 Scope

| # | 规则 |
|---|---|
| **PC115** | **不定义**：线程模型、Job System、Burst、ECS —— 并发与数据布局属实现选择 |
| **PC116** | **不定义**：Streaming 算法（加载/卸载策略、优先级、预算分配） |
| **PC117** | **不定义**：Cache 算法（淘汰、驻留、压缩、布局） |
| **PC118** | **不定义**：网络同步（权威服务器、快照、回滚、预测） |
| **PC119** | **只定义架构**：阶段顺序（§2）、依赖方向（§3）、数据生命周期（§4）、Chunk/Streaming 边界（§5/§6）、解释契约（§7）、权限矩阵（§8）、验收（§10） |
| **PC120** | **不定义实现细节**：内存布局、序列化格式、性能预算、工具接口、调试面板；**也不定义任何生成规则**（`PC6`） |

---

## §10 Acceptance Rules

### 10.1 架构完整性（用户要求的六项确认）

| # | 不变量 | 验证方法 |
|---|---|---|
| **PCV1** | **无循环依赖**：依赖图必须是有向无环图（DAG） | 图算法检查（拓扑排序可完成；`PC52` 允许边集合内无环） |
| **PCV2** | **无反向边**：不存在任何"下游 → 上游"的读/写边（含 `Runtime/Player/Entity/Voxel/Mesh/Resource/Ecology` 的实例化禁令） | 结构扫描（`PC53`–`PC60` 命中 = 0） |
| **PCV3** | **Phase 顺序固定**：0→1→2→3→4→5→6→7→8 不可交换、不可跳过 | 阶段序列校验 + 结构扫描（`PC47`） |
| **PCV4** | **所有系统存在唯一入口**：每个阶段产物只有一个生产者入口（唯一 `O`） | 矩阵校验（`PC106`）+ 结构扫描 |
| **PCV5** | **Chunk 不拥有生成权**：不存在"Chunk 决定 Biome/Geology""Chunk 产生随机规则""Chunk 保存世界状态"的路径 | 结构扫描（`PC81`–`PC84` 命中 = 0） |
| **PCV6** | **Runtime 不污染生成**：Runtime/Player/Entity 对生成产物的写路径 = 0；玩家编辑前后参数逐字段不变 | 结构扫描 + 前后快照对照（`PC43`/`PC44`；`WSV14`） |

### 10.2 阶段与数据

| # | 不变量 | 验证方法 |
|---|---|---|
| **PCV7** | **阶段输入封闭**：每阶段只读其 Allowed Read 列出的产物 | 结构扫描（每阶段输入集合比对） |
| **PCV8** | **定稿点遵守**：下游只读"已定稿"的上游产物（含 `FinalState` 在 Phase 3 之后定稿） | 顺序检查（`PC19`/`PC21`/`PC50`） |
| **PCV9** | **单点可求值**：任一世界坐标可独立求完整链，不需要世界级前置 | 求值范围检查（`PC86`；`WL4`） |
| **PCV10** | **无跨阶段访问**：不存在 skip-layer 读取 | 结构扫描（`PC62`） |
| **PCV11** | **声明性**：开启/关闭本文档 ⇒ 体素输出逐体素一致（差异 = 0） | 两态体素快照比较 |
| **PCV12** | **退化兼容**：关闭新增层（Region/能量参数/Biome 修正）⇒ 与既有基线逐体素一致（`GIV9`/`WS11`） | 与基线对照（体素 + 内容哈希） |

### 10.3 规则单一权威与解释

| # | 不变量 | 验证方法 |
|---|---|---|
| **PCV13** | **解释同源**：解释路径 = 生成路径（无第二套计算，含 Debug/工具） | 条件集合比对（`PC98`/`PC99`；`WA20`） |
| **PCV14** | **缓存透明**：缓存可清空、不改变输出、不成为权威 | 清缓存前后对照（`PC76`/`PC88`） |
| **PCV15** | **生命周期隔离**：Generation 数据不依赖 Runtime 生命周期；短生命周期结束不改变结果 | 结构扫描 + 卸载后重算对照（`PC72`–`PC75`） |
| **PCV16** | **无第二套规则**：每层规则只有一份实现（含参数、群系、地质判定、资源判定） | 结构扫描（重复阈值/规则表 = 0） |

### 10.4 零修改与矩阵

| # | 不变量 | 验证方法 |
|---|---|---|
| **PCV17** | **无新增参数 / 无新增 `VoxelType`** | 参数集合比对 + 类型集合比对（`PC36`） |
| **PCV18** | **零修改**：既有实现与既有规范 SHA 不变；既有 12 项验证保持 PASS | 文件哈希比对 + 既有验证套件 |
| **PCV19** | **矩阵自查全过**：`PC105`–`PC114` 全部满足 | 矩阵解析与结构扫描 |
| **PCV20** | **编号与依赖一致性**：阶段编号顺序与因果方向冲突处，必须有显式调和规则且不改变因果方向（附录 D ②） | 依赖图 + 调和规则审查 |

---

## 附录 A：数据流图

```
Phase 0  Seed Initialization        → Seed Context
   ↓
Phase 1  World Skeleton Generation  → WorldSkeleton（舞台框架）
   ↓
Phase 2  World Parameter Evaluation → WorldParameter Base ──┐
   ↓                                                          │（第二段：Phase 3 之后定稿）
Phase 3  Region Evaluation          → Region Context（含 RegionModifier）
   │                                      └──► 合成 ──────────┘
   ↓
        WorldParameter FinalState（8 参数，只读）
   ↓
Phase 4  Biome Classification        → Biome Identity
   ↓（仅 P3）
Phase 5  Geology Evaluation          → Geology State（连续解释结果）
   ↓
Phase 6  Voxel Evaluation            → Voxel Query Result（体素真值）
   ↓
Phase 7  Mesh Construction           → Render Mesh（只读派生）
   ↓
Phase 8  Runtime Activation          → Runtime Entity / Interaction

  History（既有 Phase 2）──► Region Source 候选（只读）──► Phase 3 输入     [附录 D ①]

  ═══ 禁止：Runtime/Player/Entity → Generation（PC53–PC55）
             Voxel → Geology（PC56）· Mesh → Generation（PC57）
             Resource → Biome（PC58）· Ecology → WorldState（PC59）═══
```

## 附录 B：阶段 → 输出 → 生命周期 → 权威

| Phase | 输出 | 生命周期（§4） | 规则权威（谁定义它） |
|---|---|---|---|
| P0 Seed Initialization | Seed Context | World Lifetime | `WorldGeneration_Core.md` §3 + `WorldParameter_Contract.md` §6.4 |
| P1 World Skeleton | WorldSkeleton | World Lifetime | **规则**（大陆/掩码/退化）：`WorldState_Design.md` §7.4（`WC1`–`WC6`）；**产物接口与查询契约**：`WorldSkeleton_V1_Minimal_Contract.md` |
| P2 World Parameter | WorldParameter FinalState | World Lifetime | `WorldState_Design.md` + `WorldState_Runtime_Contract.md`（合成权） |
| P3 Region | Region Context（含 `RegionModifier`） | Region Lifetime | `Region_Design.md` + `Region_Architecture_Contract.md` |
| P4 Biome | Biome Identity | Point Evaluation | `WorldState_Design.md` §5.3 + `Biome_Architecture_Contract.md` |
| P5 Geology | Geology State | Point Evaluation | `WorldGeneration_Geology*.md` + `Geology_Architecture_Contract.md` |
| P6 Voxel | Voxel Query Result | Chunk Lifetime | `VoxelWorld_Core.md` + `WorldGeneration_Core.md` |
| P7 Mesh | Render Mesh | Chunk Lifetime | `VoxelWorld_Mesh.md` |
| P8 Runtime | Runtime Entity / Interaction | Runtime Lifetime | `VoxelWorld_Runtime*.md` + `ADR-001` |

## 附录 C：禁止清单

| # | 禁止项 | 反例（必须判为缺陷） | 依据 |
|---|---|---|---|
| **PCX-1** | Pipeline 拥有规则 | 在 Pipeline 阶段表里写阈值/噪声公式 | `PC6` |
| **PCX-2** | Pipeline 实现算法 | 定义线程模型/Job/缓存淘汰策略 | `PC7`/`PC115`–`PC118` |
| **PCX-3** | 阶段顺序换序/跳序 | 先算 Biome 再算参数；跳过 Region 合成 | `PC47`/`PCV3` |
| **PCX-4** | 旁路引用（skip-layer） | Phase 4 直接读骨架；Phase 7 直接读 Geology State | `PC48`/`PC62` |
| **PCX-5** | 阶段产物多生产者 | 两个地方都产出 `RegionModifier` | `PC49`/`PC106` |
| **PCX-6** | 未定稿就读下游 | 在 Phase 3 之前读 `FinalState` | `PC21`/`PC50` |
| **PCX-7** | Runtime → Generation | 运行时事件提高 `MagicDensity` | `PC53`；`PX2` |
| **PCX-8** | Player → Generation | 玩家进度参与生成判定 | `PC54` |
| **PCX-9** | Entity → Generation | 对象存在改变生成规则 | `PC55`；`EF1` |
| **PCX-10** | Voxel → Geology | 由"体素已为空"调整洞穴倾向 | `PC56`；`GC62` |
| **PCX-11** | Mesh → Generation | 由几何需要改变体素类型/材料 | `PC57`/`PC42` |
| **PCX-12** | Resource → Biome | 由资源判定决定群系 | `PC58`；`RN7` |
| **PCX-13** | Ecology → WorldState | 由生态候选回写参数 | `PC59`；`E-3` |
| **PCX-14** | Chunk 拥有生成权 | 每 Chunk 一套噪声/规则；Chunk 决定群系 | `PC81`–`PC83` |
| **PCX-15** | Streaming 改变结果 | 低预算时降低细节/跳过判定 | `PC90`/`PC95` |
| **PCX-16** | Debug 另算一套 | 调试面板用简化公式解释群系 | `PC99`；`PCV13` |

## 附录 D：与既有规范的对齐登记与变更规则

### D.1 登记项（**本文档不修改任何既有文件**）

| # | 需对齐内容 | 位置 | 真相（按本契约） |
|---|---|---|---|
| ① | **History 阶段**：任务书给定的 Phase 列表（Phase 0–8）**不含 History**，而既有 `WorldGeneration_Pipeline_Design.md` §2.1 有 **Phase 2 = History Generation** | 任务书（本次指令）§2；`WorldGeneration_Pipeline_Design.md`:133；`WorldHistory_Design.md` §1.3/`PI1` | History 的**因果关系不变**（`History → Region Source 候选 → Region`）；在本契约中它作为 **Phase 3 的输入**出现（`PC26`）。**编号差异已登记，不修改既有文件** |
| ② | **WorldState 与 Region 的先后**：任务书 §2/§3 把 **WorldState（Phase 2）写在 Region（Phase 3）之前**，而既有因果方向是 `Region → RegionModifier → WorldState FinalState` | 任务书 §2/§3；`WorldState_Design.md` §2.1；`RA75`/`RA76`；`WSC19`（`FinalState = clamp01(Base + RegionModifier)`） | **依赖方向优先**（`PC61`）：Phase 2 采用**两段式发布**（`PC19`）——`Base` 先定稿，`FinalState` 在 Phase 3 之后定稿；**因果方向未被改变**（Region 仍是 Modifier 的唯一写入者） |
| ③ | **Phase 7 Mesh / Phase 8 Runtime**：既有 `Pipeline_Design` 的 Phase 集合到 **Phase 7 = Voxel Output** 为终点（Mesh/Runtime 不在管线内） | 任务书 §2；`WorldGeneration_Pipeline_Design.md`:138；`VoxelWorld_Mesh.md`（只读派生）；`VoxelWorld_Runtime*.md` | 本契约把 **Mesh Construction（P7）** 与 **Runtime Activation（P8）** 纳入调度契约，但保持其既有性质：**Mesh = 只读派生**（`PC39`）、**Runtime = 只调度不回写**（`PC43`） |
| ④ | **Phase 6 Voxel Evaluation 与既有 Phase 7 Voxel Output** | 任务书 §2；`Pipeline_Design` §2.1 Phase 7；`GC50`（既有 `GeologyWorldGenerator.Generate` 返回 `VoxelChunk` 属 Phase 7 落地形态） | 同一件事的**改名与分层**：本契约的 Phase 6 承担"抽样 + 写入动作"，Geology（Phase 5）只提供判定（`PC35`） |
| ⑤ | **Pristine / Current 真值语义**（审查 `AD-2` 未决） | 任务书 §2 Phase 6 输出"Voxel Query Result"；审查 §5 `AD-2` | `PC37` 只要求**语义必须显式声明**，不裁决 `AD-2`（该决策仍开放，归 `VoxelWorld`/`Mining`/`WorldQuery`） |
| ⑥ | 审查 `P1-6`（新栈未接入场景） | 审查 §3 表/§4 详情 | 本契约只定义调度边界；**接入决策**仍属审查 `AD-1`/`M7`，不在本文档解决 |

### D.2 变更规则

1. 修改阶段集合/编号、依赖方向、生命周期或权限矩阵，**必须**升版本并同步更新 §2/§3/§4/§8 与附录 A/B。
2. **不得放宽**：`PC2`–`PC4`（定位）、`PC6`（不拥有规则）、`PC47`–`PC50`（顺序与定稿点）、`PC53`–`PC60`（反向边禁令）、`PC73`（Generation 不依赖 Runtime 生命周期）、`PC81`–`PC84`（Chunk 无生成权）、`PC90`（Streaming 不改结果）、`PC98`–`PC99`（解释同源）。
3. 与既有规范冲突时，先按 D.1 **登记**，再由对应规范的**版本变更**解决；本文档不得直接改写其它规范。
4. 任何"新增 Phase"提案必须**独立成规范**，并证明：DAG 无环、无反向边、既有 12 项验证保持 PASS、且**可关闭**。
5. 任何"合并/取消 Phase"提案必须证明：不改变因果方向、不取消定稿点（`PC50`）、不引入 skip-layer 访问（`PC62`）。
