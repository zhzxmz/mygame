# Region Runtime Data Contract V1

> **文档性质**：**运行时数据契约（Runtime Data Contract）**。本文档定义 **Region 在求值期的逻辑身份、逻辑字段边界、与 Parameter 的关系、逻辑状态、查询边界、确定性与 Legacy 隔离**。
>
> **仅创建 Markdown** ｜ **不新增 `.cs`** ｜ **不修改 `Assets`** ｜ **不修改任何已有规范** ｜ **不裁决 `A-1`/`A-3`/`A-4`/`B-1`–`B-9` 未决项** ｜ **不设计 Region 生成算法** ｜ **不设计 Region 划分算法** ｜ **不新增参数** ｜ **不新增因果边** ｜ **不含任何代码**（无 `.cs`、无伪代码、无函数签名）。
>
> **核心定位**：
>
> | 项 | 内容 |
> |---|---|
> | **它是什么** | Region 的**逻辑数据契约**：身份、逻辑字段白名单/黑名单、逻辑状态、查询边界、确定性口径 |
> | **它不是什么** | **不是**数据结构/内存布局/存储位置契约（`B-3` 保持未决）；**不是**采纳算法或划分算法；**不是** Region 规则权威（= `Region_Design.md`）；**不是** Region 数据契约本体（= `Region_Architecture_Contract.md`）；**不是**边界契约本体（= `Region_Architecture_Boundary_Contract.md`） |
>
> **必须读取的上游规范**（只读；**不得修改**）：
>
> | 规范 | 与本契约的关系 |
> |---|---|
> | `Region_Architecture_Contract.md` | **Region 数据契约本体**：§2（身份 `RA9`–`RA20`、组成、形态）、§3（候选 `RA21`–`RA30`）、§4（采纳 `RA31`–`RA58` 同族）、§5（`Modifier` 合成与权限 `RA62`–`RA79`）、§6（`RegionType` 边界 `RA85`–`RA92`）、§7（Chunk/Voxel 边界 `RA93`–`RA104`）、§8（查询关系）、§9/§10（确定性 `RA115`–`RA139`）、§11（权限矩阵 `RA140`–`RA144`）、§13（V1 禁令 `RA145`–`RA152`）、附录 C（`RAX-1`–`RAX-16`） |
> | `Region_Architecture_Boundary_Contract.md` | **边界契约**：§1 定位（`RAB8`/`RAB9`/`RAB10`–`RAB16`）、§2 三方归属（`RAB17`–`RAB30`）、§3 输入（`RABW1`–`RABW7`、`RABB1`–`RABB18`）、§4 输出（`RAB-O1`/`RAB-O2`、`RAB43`–`RAB51`）、§6 扩展登记、§7 Legacy 隔离（`RAB83`–`RAB88`、`RAB-L1`–`RAB-L5`）、§8 当前状态（`RAB93`–`RAB95`） |
> | `Region_Design.md` | **Region 规则与语义权威**：§1.1/§1.2（定义与"不是什么"）、§1.4（`RG-A`/`RG-B`）、§3（来源与因果方向）、§4（属性模型）、§6（历史关系 `RH1`–`RH5`）、§8（边界与衰减 `RBD1`–`RBD6`）、§11（V1 范围）、§12（兼容检查 `RI1`–`RI4`、`RV9`/`RV10`） |
> | `WorldParameter_Contract.md` | **参数身份/量纲/Owner 权威**：§3.2 表 A、§4 矩阵（Region 行 = `P（修正项）`；`PW2`）、§5.2 范围冻结、§6.3/§6.4（域 `PZ10`–`PZ14`）、附录 B（`ParameterId` 权威） |
> | `WorldState_Runtime_Contract.md` | **求值契约**：§2.1 公式、§2.2 合成类型、§2.3 内部顺序、`WSC9`–`WSC14`、`WSC21`/`WSC22`、`WSC103` |
> | `WorldParameter_Runtime_Model.md` | **状态模型**：`PM23`–`PM34`（三态、唯一写入者、不可变定稿）、`PM37`–`PM46`（定长索引）、`PM52`–`PM57`（坐标求值边界）、§6（生命周期四层，**不得新增层**） |
> | `WorldGeneration_Pipeline_Contract.md` | **阶段与写入权**：`Pipeline` Phase 3（`Region Evaluation`）的 Input / Output / Allowed Read / Forbidden Read / Forbidden Write、`PC19`–`PC26` |
> | `Legacy_Status.md`（`Specifications/Architecture/`） | Legacy 冻结基线与双向隔离纪律 |
> | `WorldArchitecture_CrossDocument_Audit_V1.md`（`Specifications/Audit/`） | `A-1`–`A-4`、`B-1`–`B-9`、`CD-01`–`CD-12` 的**登记来源**（本契约只登记、不裁决） |
>
> **冲突时的优先级**：Region 的**规则与语义**以 `Region_Design.md` 为准；**数据形态/采纳/权限**以 `Region_Architecture_Contract.md` 为准；**边界（输入/输出/隔离）**以 `Region_Architecture_Boundary_Contract.md` 为准；**参数身份**以 `WorldParameter_Contract.md` 为准；**合成**以 `WorldState_Runtime_Contract.md` 为准。**本契约只定义"逻辑数据与状态"**，不改变上述任何权威。
>
> **零修改声明**：本文档是**声明性文档**，不产生任何运行时效果；开启/关闭本文档**不得改变任何输出**。
>
> **命名引用声明**：本文档涉及阶段名时一律**引用** `WorldGeneration_Pipeline_Contract.md` 自身使用的名称（例如 `Phase 3 Region Evaluation`），**不代表**对阶段编号双轨制（`A-3`）的任何裁决；涉及"Context"一词时一律**引用**既有的 `Phase 3` 输出名（`B-4` 术语统一仍在未来术语表中，本契约**不裁决**）。

---

## §1 Region 的身份

### 1.1 定义

| # | 规则 |
|---|---|
| **RRD1** | **定义**：**Region = 世界解释层空间单元**——它是**原因侧**的空间解释单元，解释"为什么此处的环境如此"，并因此成为 `RegionModifier` 的来源（`Region_Design.md` §1.1/§1.3 `RT1`/`RT2`；`RAB8`） |
| **RRD2** | **它提供什么**：只提供**修正项**（`RegionModifier`）与**只读解释/资格信息**（`RegionContext`）；不提供基础值、不提供最终值（`RAB9`；`RA75`；`RA76`） |
| **RRD3** | **它由什么确定**：由 `(Seed, DomainId, Position)` 唯一确定；身份由 `RegionKey` 决定（`RA9`；`RA115`） |
| **RRD4** | **唯一性**：一个 Region 由 `RegionKey` 唯一标识；身份一旦确定**不得改变**（不迁移、不漂移、不随时间变化；`RA13`） |

### 1.2 禁止的等同（身份边界）

| # | 禁止把 Region 当作… | 原因 | 依据 |
|---|---|---|---|
| **RRD5** | **Terrain Chunk / Chunk** | Chunk 是**技术划分**（加载/管理单位）；Region 是**世界逻辑划分**，边界不得与 Chunk 对齐，且不得按 Chunk 组织或存储 | `RA93`/`RA94`；`RAB15`；`Region_Design.md` §1.2 |
| **RRD6** | **Biome** | Biome 是**环境表现结论**（结果侧）；Region 是原因侧，两者不得互相替换 | `RA92`；`RTY5`；`RAB11` |
| **RRD7** | **Geology** | Geology 拥有地形真值判定（`Density`/`Material`/`CaveDensity`/`DepositZone`）；Region 不拥有、不影响、不读取这些结论 | `RA103`；`RA152`；`RAB8` |
| **RRD8** | **Resource** | 资源判定层（`ResourcePresence`）的 Owner 是 Resource；Region 不决定资源、不产出资源判定 | `RAB12`；`RAB45`；`WPS44` |
| **RRD9** | **Entity** | Region 是数据/解释单元，不创建对象、不含对象数据；与"实体/结构/植被/生物"无对应关系 | `RA8`；`RGS`；`RAB13` |
| **RRD10** | **Voxel 区域** | 体素是**数据**；Region 不是体素的集合、不写体素、不新增 `VoxelType`，其影响只能经唯一间接路径 | `RA99`/`RA100`；`RAB53` |
| **RRD11** | **场景对象 / 存档单位 / 跨帧状态** | Region 不是 `GameObject`、不是容器、不是存档单位、不跨帧存活 | `RA16`；`RT3`；`RAB14` |
| **RRD12** | **参数层** | Region 不定义参数语义、不写 `Base`、不定义参数集合与分量集合 | `RA4`；`RAB16`；`RRD33`/`RRD34` |

---

## §2 Region 数据结构边界

### 2.1 契约层次（逻辑 vs 物理）

| # | 规则 |
|---|---|
| **RRD13** | **本节只定义逻辑字段**：字段的**存在性、语义与只读性**；**不定义**数据结构、内存布局、数组/表形态、序列化格式、存储位置（`RA1`；`PM8` 同族；`B-3` **保持未决**） |
| **RRD14** | **逻辑聚合只有三类**：`RegionIdentity`（身份）、`RegionContext`（上下文/解释）、`RegionModifier`（参数修正量）；三者构成**字段闭集** |
| **RRD15** | **字段白名单 = 既有组成的映射**：三类的字段必须能一一映射到 `Region_Architecture_Contract.md` §2.2 的既有组成（`RegionKey` / `Source` / `Extent` / `ModifierProfile` / `Tags` / `Qualifications`）；**不新增字段、不新增概念**（见附录 A） |
| **RRD16** | **字段的表示法不属于本契约**：数值范围、枚举取值、量化方式、精度一律由各层规则与实现规范决定（`RA1`；`PS11` 同族纪律） |

### 2.2 `RegionIdentity`（逻辑字段）

| # | 字段 | 语义 | 硬性约束 |
|---|---|---|---|
| **RRD17** | `RegionId` | 稳定标识（由 `RegionKey` 派生的可读/可比标识） | 仅用于**标识、调试、解释输出**；**不得**作为规则输入、**不得**作为排序/截断键（`RA15`） |
| **RRD18** | `Seed来源` | 该 Region 的确定性来源标识（来自 `Seed` 的推导链） | 必须确定性、可解释（`RA115`；`RA28`）；不得依赖运行时状态（`RH1`） |
| **RRD19** | `空间范围描述` | 锚点（世界坐标）+ 尺度 + 衰减形状**标识** | 必须紧支撑、随距离单调不增、边界连续趋于 0（`RA72`；`RBD1`/`RBD3`）；形状本身属 Region 规则（本契约不定义） |
| **RRD20** | `生命周期状态` | `Candidate` / `Accepted` / `Frozen` 之一（§4） | 逻辑状态标签；**不得**进入调度（`RA95`）、**不得**跨帧存活（`RA137`）、**不得**作为采纳规则输入（`RRD58`） |

### 2.3 `RegionContext`（逻辑字段）

| # | 字段 | 语义 | 硬性约束 |
|---|---|---|---|
| **RRD21** | `身份信息` | `RegionIdentity` 的只读引用 | 只读发布（`RA20`） |
| **RRD22** | `来源解释` | 来源类别与原因引用链（引用既有来源类别体系，**不复制、不新增**清单） | 来源类别权威 = `Region_Design.md` §3.2；候选必须来自 ≥1 个来源（`RG-A`；`RA21`） |
| **RRD23** | `标签` | 只读标签集合（来源类别、纪元/序位、引用到的历史标签） | 只读；**不得**作为环境参数求值输入（`RH3`；`Region_Architecture_Contract.md` §2.2 `Tags`） |
| **RRD24** | `只读引用` | 解释链引用（原因引用 / 可解释链引用） | 必须可解析、只读、不得作为判定条件（`RA26`；`PW5` 归因纪律） |
| **RRD25** | `资格声明` | 区域级资格声明（`Qualifications`） | 形状固定为 `(Category, Allowed)`，`Allowed ∈ {允许, 不允许, 未声明}`；只读；不得生成对象；不得参与参数求值（`RA80`–`RA83`） |

### 2.4 `RegionModifier`（逻辑字段）

| # | 字段 | 语义 | 硬性约束 |
|---|---|---|---|
| **RRD26** | `参数修正量` | 恰好 **6 个分量**的修正量（`Temperature` / `Moisture` / `Fertility` / `MagicDensity` / `HolyInfluence` / `CorruptionInfluence`） | 分量集合封闭（`RA67`/`RA68`）；`Elevation`/`Ocean`/`Balance` 不在其中（`RA69`/`RA70`） |
| **RRD27** | `来源引用`（可选） | 该修正量的来源解释引用 | 只读；只用于解释（`RA26`；`RRD24`） |

### 2.5 字段级禁令

| # | 规则 |
|---|---|
| **RRD28** | 三类逻辑字段**不得**携带：Mesh 数据、Voxel 数据、Entity 数据、Runtime 状态、Player 状态（完整清单见附录 B） |
| **RRD29** | 字段**不得**携带下游产物与"目标结果"（Biome 结论、生态候选、资源判定、地质结论、体素值、Chunk 状态）；候选只描述**原因**（`RA23`；`RA24`） |
| **RRD30** | 字段**不得**携带跨帧状态、计时器、演化状态或"上次求值结果"；不得把派生标识当规则输入（`RA124`；`RA137`；`RA15`） |

---

## §3 Region 与 Parameter 的关系

### 3.1 读取（封闭白名单）

| # | 规则 |
|---|---|
| **RRD31** | **允许读取**：`Seed`、世界坐标（`WorldCoordinate`）与**已登记的允许事实**（Allowed Facts）——白名单的权威是 `Region_Architecture_Boundary_Contract.md` §3.1（`RABW1`–`RABW7`），本契约**不复制、不放宽** |
| **RRD32** | 白名单**封闭**：表外量一律不得读取；Region 求值路径的输入符号必须全部落在白名单内（`RAB31`；`RAB37`；`RA7`） |
| **RRD33** | **禁止读取 `FinalState`**：Region 不得读取自己将要影响的 6 个分量的最终值（未定稿，读它构成环）（`RAB19`；`RAB32`；`PC19`/`PC21`） |
| **RRD34** | **禁止读取未定稿的 `Base` 运行值**：`Base` 必须能在不计算任何 Region 的前提下求出，反向不成立（`RAB20`；`WSC21`） |

### 3.2 产生（唯一产物）

| # | 规则 |
|---|---|
| **RRD35** | **Region 只产生 `RegionModifier`**（6 分量修正量）与 `RegionContext`（只读解释/资格）；不产生任何其它产物（`RAB9`；`RA73`；`RAB-O2`） |
| **RRD36** | **禁止 Region 修改 `BaseValue`**：`Base` 的产生者是参数层（WorldState 求值方），Region 无写权限（`RAB17`/`RAB18`；`RA4`；`D2`；`PM27`） |
| **RRD37** | **禁止 Region 写 `FinalValue`**：合成权归 WorldState 层（`Final = clamp01(Base + Modifier)`）（`RAB17`；`RA76`；`PC20`；`D2`） |
| **RRD38** | Region **不得**把修正量写回参数、不得发明/补偿/归一化修正（`RA76`；`PC20`） |
| **RRD39** | 合成语义必须遵守：只有加法 + 钳制、交换律、逐分量钳制到契约范围（`RA62`–`RA64`；`WSC9`/`WSC12`） |
| **RRD40** | **零修正的显式性**：Region 层不存在或未生效时逐分量为 0，且必须与"Region 层不存在"逐值等价（`WSC14`；`RAB24`） |
| **RRD41** | **退化等价**：关闭 Region 层 ⇒ `Final == Base`（逐分量）且输出逐体素一致（`WSC22`；`RA104`；`RAB25`） |
| **RRD42** | **不新增参数、不新增分量**：参数集合与 `ParameterId` 权威是 `WorldParameter_Contract.md` 附录 B；修正分量权威是 `RA67`（本契约两者皆不新增） |

---

## §4 Region 生命周期

### 4.1 三态定义（逻辑状态）

| # | 规则 |
|---|---|
| **RRD43** | **生命周期只定义三个逻辑状态**：`Candidate`（候选）、`Accepted`（已采纳）、`Frozen`（已冻结发布）；三态构成**状态闭集** |
| **RRD44** | **这不是运行时对象生命周期**：三态是**求值期内的逻辑状态标签**，不表示"已加载/已生成/跨帧存活"，**不新增生命周期层**（`RA137`；`PM18`/`PM49` 同族纪律；`RAB41`） |
| **RRD45** | **`Candidate`（候选）**：采纳之前的来源候选状态；此状态下记录**只读**，且在提交时**不做空间合并**（合并统一发生在采纳阶段）（`RA26`；`RA21`–`RA30`） |
| **RRD46** | **`Accepted`（已采纳）**：候选通过采纳流程（准入 / 规范化 / 全序 / 上限）后的状态；此状态仍为**只读输入**，不代表已物化（`RA31`–`RA49` 同族；`PC23` 六步流程的前四步） |
| **RRD47** | **`Frozen`（已冻结）**：物化并**只读发布**后的状态；该状态下 Region 的任何字段**不可修改**（`RA20`；`RA54`–`RA58` 同族；`PC23` 后两步；`PM34` 不可变定稿纪律） |

### 4.2 状态转移与禁令

| # | 规则 |
|---|---|
| **RRD48** | **唯一合法转移链**：`Candidate → Accepted → Frozen`（顺序方向唯一） |
| **RRD49** | **禁止**：跳过 `Accepted` 直接 `Frozen`；**禁止**逆向转移（`Frozen → Accepted`、`Accepted → Candidate`）；**禁止** `Frozen` 的任何再修改（`RA20`；`RA13`） |
| **RRD50** | **禁止把状态当作规则输入**：不得用生命周期状态影响准入、去重、合并、全序、上限或修正量（`RA2`；`RA87` 同族纪律） |
| **RRD51** | **状态不改变权限**：任何状态下 Region 都**没有**写参数、写体素、创建对象、修改 Chunk/Mesh 的权限（`RA75`；`RA99`；`RA8`） |
| **RRD52** | **状态不进入调度**：不参与加载/卸载/预算/并发；Region 不出现在任何调度接口中（`RA95`） |
| **RRD53** | **状态不得跨帧存活、不得被缓存为权威**：连续两次查询之间不需要世界实例；缓存（若实现）必须透明（`RA137`；`RA138`/`RA139`） |
| **RRD54** | **状态数量冻结**：恰为三态；新增第四态必须**升版本**并同步更新本契约（`RAB98` 同族纪律） |
| **RRD55** | **状态必须可解释**：查询必须能说明当前状态（"为什么它只是一个候选/已被采纳/已冻结"）（`RA28`；`RA82`；`RA83`） |
| **RRD56** | **不实现**：生成流程、采纳算法、Streaming、缓存实现、存档格式（`RA145`；`Region_Architecture_Contract.md` §13.2；`RAB94`） |
| **RRD57** | `Accepted` 阶段的**规范化语义**（去重键、`Strength` 有界求和、`Radius` 取 max、`Origins` 无损、`Tags` 并集）属数据契约本体，本文档只引用不改写（`Region_Architecture_Contract.md` §4.2 同族） |
| **RRD58** | 三态的**判定条件与阈值**属实现规范；本契约只固定"存在三态、转移方向唯一、不参与规则"（`RA1`） |

---

## §5 Region 查询边界

### 5.1 定义

| # | 规则 |
|---|---|
| **RRD59** | **Region Query（逻辑定义）**：输入 = **世界坐标**（外加 `Seed`）；输出 = **`RegionContext`**（含该点受影响的 Region 集合与解释）（`RA17`；`RA127`） |
| **RRD60** | **输入唯一性**：查询只以世界坐标为自变量；**不得**以 Chunk 编号、Chunk 局部坐标或"是否已加载"为主键（`RA96`；`PC86`；`RABW2`） |
| **RRD61** | **输出封闭**：查询只输出 `RegionContext`；需要修正量时经 `RegionModifier`（`RAB-O1`/`RAB-O2`）；**不得**输出体素、实体、资源、群系、地质或 Chunk 相关产物（`RAB43`–`RAB51`） |

### 5.2 查询禁令

| # | 规则 |
|---|---|
| **RRD62** | **禁止 Query 触发生成**：查询不得创建/修改/删除 Region，不得写体素、不得创建 Chunk、不得置脏、不得生成对象或 Mesh（`RA110`；`RA97`；`WQ4`；`RAX-13`） |
| **RRD63** | **禁止世界级前置**：为回答一个点，不得枚举全世界 Region、不得要求全局 Region 表 / 全局候选表 / 全局注册表（`RA131`–`RA135`；`RAX-12`） |
| **RRD64** | **单点独立性**：单个点的查询必须能独立完成，不依赖任何世界级步骤（`RA127`） |
| **RRD65** | **局部性**：查询代价必须与该点相关 Region 数量相关，与"世界 Region 总量"无关（`RA128`） |
| **RRD66** | **禁止邻域依赖**：不得因邻居是否已求值/已查询/已加载而改变结果（`RA136`） |
| **RRD67** | **未生成区域必须可答**：查询不得要求"该 Chunk 已生成"（`RA96`；`RABW2`） |
| **RRD68** | **缓存纪律**：缓存（若实现）必须由 `(Seed, 参数, 世界坐标, 域标签)` 索引、可清空、不改变输出、非权威（`RA138`/`RA139`） |
| **RRD69** | **同源规则**：查询与生成必须使用同一套 Region 规则，禁止第二套求值路径（`WQ1`；`WSC67`） |
| **RRD70** | **查询结果是终端产物**：不得成为任何生成/求值环的输入，且不得触发重算（`WQ4`；`PC104`） |
| **RRD71** | **只读性**：Query 对 Region 侧产物只有只读权限；不得修改 `RegionModifier`（`RA110`；`RA142`） |
| **RRD72** | **解释能力**：查询的 `RegionContext` 必须能回答"该点受哪些 Region 影响、各自修正多少、为什么"（`RA17`；`RA82`；`WE1`–`WE8` 解释族） |

---

## §6 Determinism

### 6.1 核心等式

| # | 规则 |
|---|---|
| **RRD73** | **同一 `Seed` + 同一世界坐标 ⇒ 同一 Region 结果**（逐字段）（`RA115`；`PM52`；`RP2`/`RS3`；`D5` 同族纪律） |
| **RRD74** | 判定必须**可独立重复求值**：多次求值、两个独立实例/进程、任意遍历顺序 ⇒ 逐字段相同（`RAV3`；`RAV4`） |
| **RRD75** | Region 产物必须由 `(Seed, DomainId, Position)` 唯一确定；**域登记**权威是 `WorldParameter_Contract.md` §6.4（`RA115`/`RA116`） |
| **RRD76** | **域必须参与哈希混合**；**未登记域不得使用**（`RA117`；`PZ11`；`PZ10`） |
| **RRD77** | **域间统计独立**：改变一个域的输入不得影响其它域的输出（`RA118`；`PZ13`） |

### 6.2 禁止事项（全部禁止）

| # | 禁止 | 依据 |
|---|---|---|
| **RRD78** | **时间**：`Time` / `DateTime` / 时间戳 / 真实时长 / tick / 帧序 / `Guid` | `RA120`；`PZ6` |
| **RRD79** | **随机状态**：`Random` / 任何有状态伪随机源 | `RA119`；`PZ5` |
| **RRD80** | **加载顺序**：加载/卸载顺序、已加载集合、遍历顺序、查询顺序、世界级中间态 | `RA122`；`PZ8` |
| **RRD81** | **Runtime 状态**：脏标记、加载集合、预算、调度状态、已生成内容 | `RABB1`；`RA95`；`PX2` |
| **RRD82** | **Player 状态**：位置、进度、背包、已探索、已采集 | `RA121`；`PZ7` |
| **RRD83** | **由下游反推**：由 `RegionType`/Biome/生态/资源结果反推来源或修正 | `RA123`；`RA79`；`PZ9` |
| **RRD84** | **非平凡性必须可验证**：验收必须证明"不同 `Seed`/显式参数 ⇒ 至少一处不同"，并证明"采纳确实发生"（防止永远空集的假通过） | `RA125`；`RA126` |

---

## §7 Legacy 隔离

| # | 规则 |
|---|---|
| **RRD85** | **完全隔离声明**：Region 层与 Legacy **双向隔离**——Region 不得依赖 Legacy，Legacy 不得依赖 Region（`RAB83`；`RAB84` 同族） |
| **RRD86** | **禁止引用**：`TerrainChunk`、`DensityTerrain`、`TerrainMining`、`VoxelAwareMesh` 及**任何 Legacy Terrain 类型**（含 `Assets/Scripts/Terrain/**` 全部文件与其类型） | 
| **RRD87** | **禁止 Legacy 坐标栈**：不得使用 Legacy 的坐标迁移语义/旧 Chunk 局部坐标约定；Region 只用世界坐标（`RAB-L3`；`RRD60`） |
| **RRD88** | **禁止 Legacy 运行时产物**：场景对象、`VoxelAwareMesh` 运行时突变结果、Legacy 挖掘/已采集状态（`RAB-L4`/`RAB-L5`；`RABB5`） |
| **RRD89** | **禁止语义借用**：不得把 Legacy 的网格/挖掘/坐标语义（含空间面、评估归属等概念）翻译或映射为 Region 概念（`RAB85`） |
| **RRD90** | **可机械验证**：结构扫描中 Legacy 符号命中必须为 **0**；Legacy 基线 SHA 必须保持不变（`RAB86`；`RV9`；`Legacy_Status.md` 基线清单） |
| **RRD91** | **实现位置**：Region 的实现只能落在 V2 侧；不得写入 Legacy 目录，也不得改写受 SHA 冻结的既有实现（`RAB84`；`RV10`） |
| **RRD92** | **Legacy 退役不在本契约范围**：Legacy 在 V2 稳定前保持冻结；退役/共存决策属未决项（`B-8`；`Legacy_Status.md`） |

---

## §8 验收、变更与登记

### 8.1 本契约的验收

| # | 规则 |
|---|---|
| **RRD93** | **条款编号连续**：`RRD1`–`RRD102` 必须是**连续、唯一、无跳号**的编号序列（可用脚本机械核验） |
| **RRD94** | **无代码**：本文档不含任何代码（无 `.cs`、无伪代码、无函数签名、无可执行片段） |
| **RRD95** | **无新增参数**：不新增参数、不新增 `ParameterId`、不新增修正分量（`RRD42`） |
| **RRD96** | **无新增因果边**：只引用既有边（`来源 → Region → 修正 → 最终环境 → 表现/资格`）（`RA2`；`RAB4`；`RG-B`） |
| **RRD97** | **不解除未决项**：`A-1`–`A-4`、`B-1`–`B-9` 全部**保持未决**，本契约**不裁决、不依赖、不预先实现**其任何特定结论（附录 D） |
| **RRD98** | **不修改既有规范、不修改 `Assets`**：本文档只读引用上游；发现差异一律登记（附录 D / §8.3），不修正 |
| **RRD99** | **违规判定**：违反本契约的字段禁令（§2.5）、查询禁令（§5.2）或 Legacy 隔离（§7）者，判为**边界缺陷**，不得以"临时实现/仅调试/不影响结果"豁免 |
| **RRD100** | **声明性**：本文档存在/缺失不改变任何输出；其内容不得成为任何生成结果的一部分 |

### 8.2 变更规则

| # | 规则 |
|---|---|
| **RRD101** | 字段许可范围、三态集合、查询输入/输出、隔离要求的任何变更：**必须升版本**，且不得放宽上游约束（`RA2`/`RA68`/`RA75`/`RA104`/`RAB31`/`RAB38`） |
| **RRD102** | 若任一未决项（附录 D）将来被裁决，本契约**必须重新审阅**：受影响条款按新裁决对齐后升版本；在此之前，任何实现**不得**依赖对未决项的单方面解读 |

### 8.3 本轮登记（不裁决）

| 登记项 | 内容 | 处置 |
|---|---|---|
| **语境说明 · `A-3`** | 本文档引用阶段名（`Phase 3 Region Evaluation`）时**只引用** `WorldGeneration_Pipeline_Contract.md` 的自身命名；两套阶段编号（0–7 / 0–8）并存问题**未被本契约解决** | 登记；不裁决 |
| **语境说明 · `B-3`** | 本文档只定义**逻辑字段**；参数三态/Region 数据的**物理存储位置**未被本契约定义 | 登记；不裁决 |
| **语境说明 · `B-4`** | "Context"一词沿用既有 `Phase 3` 输出名；术语多义（`Modifier`/`State`/`Context`/`Result`/`Rule`/`Presence`/`Source`/`Generation`）未被本契约统一 | 登记；不裁决 |
| **语境说明 · `B-2`** | 本文档不登记任何配置参数的 Owner；配置参数 Owner 缺口保持原状 | 登记；不裁决 |

---

## 附录 A：Region 字段白名单

> 白名单 = `Region_Architecture_Contract.md` §2.2 既有组成的**逻辑映射**；**不新增字段**（`RRD15`）。

| 逻辑聚合 | 逻辑字段 | 映射到既有组成 | 依据 |
|---|---|---|---|
| `RegionIdentity` | `RegionId` | 派生标识（由 `RegionKey` 派生） | `RA15`；`RRD17` |
| `RegionIdentity` | `Seed来源` | 来源的确定性推导链 | `RA115`；`RRD18` |
| `RegionIdentity` | `空间范围描述` | `Extent`（锚点 + 尺度 + 形状标识） | `RA72`；`RRD19` |
| `RegionIdentity` | `生命周期状态` | 求值期逻辑状态（三态，§4） | `RRD43`–`RRD47` |
| `RegionContext` | `身份信息` | `RegionKey` | `RRD21` |
| `RegionContext` | `来源解释` | `Source` | `RRD22` |
| `RegionContext` | `标签` | `Tags` | `RRD23` |
| `RegionContext` | `只读引用` | 解释链引用（原因引用 / 可解释链引用） | `RRD24` |
| `RegionContext` | `资格声明` | `Qualifications` | `RRD25` |
| `RegionModifier` | `参数修正量`（6 分量） | `ModifierProfile` → 贡献汇总 | `RA67`；`RRD26` |
| `RegionModifier` | `来源引用`（可选） | 修正量来源解释引用 | `RRD27` |

---

## 附录 B：Region 禁止字段清单

| 编号 | 禁止字段 | 依据 |
|---|---|---|
| **RRD-N1** | Mesh 数据（几何 / 面 / 材质 / 子网格） | `RAB43`；`Region_Design.md` §12.3；`RRD28` |
| **RRD-N2** | Voxel 数据（体素值 / 材料 / 新 `VoxelType`） | `RA99`/`RA100`；`RAB43` |
| **RRD-N3** | Entity 数据（对象 / 结构 / 植被 / 生物） | `RA8`；`RAB44`；`RRD9` |
| **RRD-N4** | Runtime 状态（脏标记 / 加载集合 / 预算 / 调度 / 帧序） | `RABB1`；`RA95`；`RRD81` |
| **RRD-N5** | Player 状态（位置 / 进度 / 背包 / 已探索 / 已采集） | `RABB2`；`RA121`；`RRD82` |
| **RRD-N6** | Biome 结论（群系分类 / 群系地图） | `RA7`；`RA92`；`RAB46` |
| **RRD-N7** | Geology 产物（`Density` / `Material` / `CaveDensity` / `DepositZone` / 地层） | `RA103`；`RAB47` |
| **RRD-N8** | Resource 判定（`ResourcePresence` / 资源清单 / 矿藏结论） | `RAB45`；`WPS44` |
| **RRD-N9** | 生态候选 / 生态资格结论 | `RA7`；`RAB9` 同族纪律 |
| **RRD-N10** | Chunk 状态与 Chunk 相关字段（生命周期 / 局部坐标 / 划分 / 归属表） | `RA93`–`RA95`；`RAB15` |
| **RRD-N11** | 地形量修改（`Elevation` / `Ocean` 的写入） | `RA69`；`RA152`；`RAB48` |
| **RRD-N12** | "目标结果"字段（例如"这里应该是魔法森林"） | `RA24`；`RRD29` |
| **RRD-N13** | 下游产物字段（任何由 Region 下游层产出的结论） | `RA23`；`RA7` |
| **RRD-N14** | 跨帧状态 / 存档字段 / 全局 Region 表 / 全局注册表 | `RA16`；`RA132`；`RA137`；`RRD30` |
| **RRD-N15** | 时间 / 随机状态字段（含"上次求值时间""随机种子状态"） | `RA119`/`RA120`；`RRD78`/`RRD79` |
| **RRD-N16** | 派生标识作为规则输入（把 `RegionId`/哈希当判定或排序依据） | `RA15`；`RRD17` |
| **RRD-N17** | 第 7 个修正分量或对分量集合的扩展 | `RA68`；`RAB51` |
| **RRD-N18** | Legacy 任何类型/字段（含 Legacy 地形、挖掘、网格、坐标栈语义） | `RRD86`–`RRD89`；`RAB-L1`–`RAB-L5` |

---

## 附录 C：未来实现入口登记

> **只登记逻辑位置**：不定义签名、不定义数据结构、不定义算法、不定义文件路径（`RRD13`；`RRD56`）。所有条目当前状态均为**未实现**。

| 逻辑入口 | 职责（一句话） | 权威文档 | 当前状态 | 依赖的未决项 |
|---|---|---|---|---|
| 采纳链入口 | 候选 → （准入/规范化/全序/上限/物化/发布）→ 只读发布 | `Region_Architecture_Contract.md` §4；`PC23` | 未实现 | 无（不得依赖 `A-1`） |
| Region 查询入口 | 世界坐标 → `RegionContext` | §5；`RA127`–`RA139` | 未实现 | 无 |
| `Modifier` 供给入口 | 向 WorldState 合成步骤提供 6 分量修正量 | `RA75`；`PC20`；`WSC §2.3` | 未实现 | 无 |
| 解释入口 | 输出 `Tags` / 引用链 / 资格的可读解释 | `RA82`；`WorldQuery_Design.md` §6 | 未实现 | 无 |
| 类型标签派生（解释用） | 派生 `RegionType`（**无规则权**） | `RA85`–`RA91` | 未实现 | 无 |
| 验证入口 | Region 侧验收（确定性/顺序无关性/边界） | `RAV1`–`RAV20`；`RV9` | 未实现 | 无 |
| 域接入 | 使用 §6.4 已登记的 Region 域 | `WorldParameter_Contract.md` §6.4；`RA116` | 域已登记，实现未接入 | 无 |
| 阶段位置命名 | 引用 `Pipeline` 的 `Phase 3 Region Evaluation` 名称 | `WorldGeneration_Pipeline_Contract.md` | 引用即可 | 是（`A-3`：编号双轨，**不裁决**） |
| Legacy 退役/共存 | 决定 Legacy 文档与验证入口的去留 | `Legacy_Status.md` | 未实现 | 是（`B-8`；`C-6`，**不裁决**） |

---

## 附录 D：未决项登记

> **本契约不裁决、不解除、不依赖**以下任一项；如实实现需要触及它们，必须先由对应权威文档升版本或由决策记录裁决（`RRD97`；`RRD102`）。
> 登记来源：`Specifications/Audit/WorldArchitecture_CrossDocument_Audit_V1.md`（`A-1`–`A-4`、`B-1`–`B-9` 与 `CD-01`–`CD-12`）。

| 编号 | 未决项（摘要） | 与 Region 的关系 | 本契约态度 |
|---|---|---|---|
| **A-1** | 体素真值写入者归属冲突（Runtime 安装 vs `Phase 6 Voxel Evaluation`） | Region **不写体素**，因此**不受该裁决影响**；也不得借该裁决扩展自身权限 | 保持未决；不裁决；不依赖 |
| **A-2** | Terrain 真值双轨（项目级，Legacy 栈与新栈） | 决定 Legacy 与 V2 的共存方式（`RRD92`） | 保持未决；不裁决；不依赖 |
| **A-3** | 阶段编号双轨制（两套 `Phase` 编号并存） | 仅影响本文档对阶段名的**引用**（`§8.3`） | 保持未决；不裁决；不依赖 |
| **A-4** | 编号前缀多义（可核验性） | 本文档使用 `RRD` 前缀族以**避免**新增冲突，但不裁决既有前缀处置 | 保持未决；不裁决；不依赖 |
| **B-1** | Runtime Modification 边界缺失（改动集/编辑层所有权、跨帧、存档） | 明确**不属于** Region：编辑与玩家状态属 Runtime State（`RA78`；`RABB1`） | 保持未决；不裁决；不依赖 |
| **B-2** | 配置参数无 Owner 登记（`Seed`/`WorldHeight`/`SeaLevel` 等） | 本文档**不登记**任何配置参数 Owner（`§8.3`） | 保持未决；不裁决；不依赖 |
| **B-3** | 参数三态物理存储位置未定义（谁持有、缓存归属） | 本文档只定义**逻辑字段**，不定义物理存储（`RRD13`） | 保持未决；不裁决；不依赖 |
| **B-4** | 术语多义（`Modifier`/`State`/`Context`/`Result`/`Rule`/`Presence`/`Source`/`Generation`） | 本文档的 `Context`/`State` 用词一律沿用既有契约的既有名称（`§8.3`） | 保持未决；不裁决；不依赖 |
| **B-5** | 图例符号语义差异（`O`、`R*`） | 本文档不引用图例符号作判定，只引用条款编号 | 保持未决；不裁决；不依赖 |
| **B-6** | 6 个横向单元未定义（`Resource↔Mesh` 等禁令缺口） | 与 Region 边界无关（Region 侧边界已在 `RAB*` 冻结） | 保持未决；不裁决；不依赖 |
| **B-7** | `Elevation` 双角色易误读（提供者 vs 转发者） | 本文档按既有口径引用：Region 对 `Elevation`/`Ocean` **只读**（`RA69`；`RABW3`） | 保持未决；不裁决；不依赖 |
| **B-8** | 旧栈遗留文档/验证入口与新契约族未映射（含 45 个验证入口） | 与 Region 实现入口登记同域（附录 C 末行） | 保持未决；不裁决；不依赖 |
| **B-9** | `Phase` 集合扩展（Mesh/Runtime 入管线）未在旧文档登记 | 与阶段名引用相关（`A-3` 同族） | 保持未决；不裁决；不依赖 |
| **CD-01 … CD-12** | 跨文档冲突决策项（完整清单见审计文档） | 均**不在**本契约范围内 | 保持未决；不裁决；不依赖 |
