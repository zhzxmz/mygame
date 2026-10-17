# Resource Architecture Contract V1

> **文档性质**：**架构契约规范（Architecture Contract）**。本文档只建立 **Resource 的架构契约**，**不实现任何代码**。
> **仅允许新增** `Specifications/World/Resource_Architecture_Contract.md`（本文件）。
> **禁止**：修改已有 `.md`；修改 `Assets/**`；新建 `.cs`；新增参数；新增 `VoxelType`；**新增资源种类列表**；新增因果边。
>
> **核心定位**：
>
> > **Resource = 世界资源存在性解释层（Resource Presence Interpretation Layer）。**
>
> | 它负责 | 它不负责 |
> |---|---|
> | ① 根据**环境条件**判断资源是否**可能**存在 | 生成物品（Item） |
> | ② 生成 **`ResourcePresence`** 解释结果 | 生成矿块实体（Entity） |
> | ③ 提供**资源存在原因**（可解释） | 决定掉落（Drop） |
> | | 修改 Geology |
> | | 修改 Voxel |
> | | 修改 WorldState |
>
> **本文档不含任何代码符号**：不定义 Provider / Manager / 生成函数式命名（禁止"具体接口/方法/类型"式设计；见 `RC140`）。
> **本文档不定义任何资源种类**：资源种类的**权威在 `Resource_Design.md` §2**；本文档**不新增、不复述**具体种类（`RC137`）。
> **本文档不定义**生成/掉落/战利品语义（属未来 Resource Content / Entity / Mining；`RC138`/`RC139`）。
>
> **编号命名空间（重要）**：本文档使用 **`RC1`–`RC150`**，属 **`Resource_Architecture_Contract`**。
> 既有 `Region_Design.md` 另有 **`RC1`–`RC5`**（语义：禁止的反向因果方向）。二者**编号重叠但语义不同**，引用时必须带文档名（见附录 D ①）。
>
> **必须兼容的上游规范**（本任务已读取，SHA 校验通过；**不得修改**）：
>
> | 规范 | 与本契约的关系 |
> |---|---|
> | `Resource_Design.md` | **资源规则的既有权威**：`RN7`（Biome 不得作为资源判定输入）、`RN8`（正确输入 = 环境参数 + 地质条件 + 纪元）、`RQ4`（生物资源两级模型）、`RU1`/`RU2`（`DepositZone` 归 Geology；Resource 只读消费 + 上层裁决）、`RY11`（纪元跨度）、`RZ2`（`HistoryTags` 仅归因）、§1.3（判定等级）、§4.2（两层存在性）、§8.2③（与 Ecology 的顺序可交换） |
> | `Geology_Architecture_Contract.md` | `GC97`（两层模型）、`GC98`（`DepositZone` 只属 Geology）、`GC99`（Geology 不得产出资源判定）、`GC100`（不得产出资源实体）、`GC101`（Resource 只读消费地质输出、不得回写）、`GC102`（`DepositZone` 五条纪律）、`GC103`（矿藏不得在空气中成立）、`GC104`（Geology 不得读资源判定）、`GC105`（不得新增资源体素类型）、`GC146`（体素化注入点唯一） |
> | `Biome_Architecture_Contract.md` | `BC92`（Biome 不得作为资源判定输入）、`BC96`（解释中出现 Biome 不得构成输入边）、`BC97`（禁止"因为群系所以生成资源"的写法）、`BC98`（可共享上游但不得互读） |
> | `Ecology_Design.md` | `ER1`（资源必须有地质/环境原因）、`ER2`（`DepositZone` 不得被 Ecology 定义/修改）、`EE1`（生态输入白名单）、`EG5`（禁 `RegionType` 作输入）、`EF1`（Entity 不得改生成规则） |
> | `WorldParameter_Contract.md` | §3.5 **术语冻结**（"地质矿藏基础层" / "资源判定层"）、`PW5`（归因读取不得作判定条件） |
> | `WorldQuery_Design.md` / `WorldQuery_Architecture_Contract.md` | `WQ1`（同一规则）、`WQ4`（查询不得成为生成输入）、`WE6`（解释必须可定位到条件）、`WA20`（无第二套阈值）；查询侧边界由 `WorldQuery_Architecture_Contract.md` 冻结 |
> | `WorldGeneration_Pipeline_Contract.md` | `PC58`（Resource → Biome 禁止）、`PC60`（泛化禁令：下游 → 上游一律禁止）、`PC104`（解释不得成为任何阶段输入） |
> | `WorldState_Runtime_Contract.md` | `WSC56`（Resource → 参数 禁止）、`WSC67`（查询必须用同一求值路径） |
> | `WorldGeneration_Architecture_Audit.md` | 审查 **P1-5**（资源 ↔ Mining ↔ Entity 边界无落点）、**AD-6**（资源对象化时点未决）——本契约只固定**解释层边界**，不裁决对象化时点 |
>
> **冲突时的优先级**：**资源规则语义**（判定条件、等级、生物资源两级模型）以 `Resource_Design.md` 为准；
> **地质基础层**以 `Geology_Architecture_Contract.md` 为准；**群系/参数/历史**以各自规范为准；
> **本契约负责**：Resource 的**定位与边界、输入白名单、`ResourcePresence` 结果形态、与 Geology/Biome/Ecology 的关系、确定性与局部求值、解释契约、权限矩阵**。
>
> **零修改声明**：本文档是**声明性文档**，不产生任何运行时效果；开启/关闭本文档**不得改变任何体素输出**（`RCV20`）。

---

## §1 Resource Position and Boundary

| # | 规则 |
|---|---|
| **RC1** | **定义**：Resource = **世界资源存在性解释层**：在**不生成对象、不修改世界**的前提下，把环境条件与地质基础解释为"**此处资源是否可能存在**"的结论 |
| **RC2** | **责任（三项，唯一）**：① 根据**环境条件**判断资源**是否可能存在**；② 生成 **`ResourcePresence`** 解释结果；③ 提供**资源存在原因**（可解释、可核对） |
| **RC3** | **不负责（六项）**：① 生成物品；② 生成矿块/资源实体；③ 决定掉落；④ 修改 Geology；⑤ 修改 Voxel；⑥ 修改 WorldState |
| **RC4** | **位置（冻结链条）**：`WorldState → Region → Biome Context → Geology DepositPotential → ResourcePresence`；Resource 位于**解释链末端的判定层**，只读消费上游 |
| **RC5** | **Resource 不是 Geology 的子系统**：二者是**分层关系**（地质基础层 / 资源判定层），Resource 不得复制地质判定（§4） |
| **RC6** | **不拥有**：WorldState · Region · Biome · Geology · Ecology · Runtime State · Voxel 真值（全部只读；`RCV9`） |
| **RC7** | **术语冻结**：`DepositZone` = **地质矿藏基础层**（Owner = Geology）；`ResourcePresence` = **资源判定层**（Owner = Resource）（`WorldParameter_Contract.md` §3.5；`GC97`） |
| **RC8** | **不是生成阶段**：Resource 是**旁路解释层**，不新增 Phase、不改变阶段顺序（`WorldGeneration_Pipeline_Contract.md` §2） |
| **RC9** | **不新增因果边**：只允许既有边（`Geology → DepositPotential → Resource`）与只读消费边 |
| **RC10** | **不定义资源种类清单**：种类权威 = `Resource_Design.md` §2；本文档不新增、不复述任何具体种类 |
| **RC11** | **不定义对象化/采集规则**：物品、实体、掉落、采集结果属未来 Resource Content / Entity / Mining 规范 |
| **RC12** | **声明性**：开启/关闭本文档 ⇒ 体素输出逐体素一致（`RCV20`） |

---

## §2 Input Contract

### 2.1 允许的输入（封闭白名单）

| # | 允许输入 | 说明 |
|---|---|---|
| **RC13** | **`Seed`** | 确定性来源之一（域由参数契约 §6.4 登记） |
| **RC14** | **World Position** | 世界坐标点（唯一空间自变量；不得以 Chunk/局部坐标为主键） |
| **RC15** | **FinalState（仅环境解释）** | 环境参数（只读）；**仅用于环境条件判定**，不得写回 |
| **RC16** | **Geology Explanation** | 地质解释字段（判定命中、地层/深度、介质等；只读） |
| **RC17** | **`DepositPotential`** | **地质矿藏基础层**（`DepositZone`；Owner = Geology，只读） |
| **RC18** | **Biome Context（解释用途）** | 群系结论**仅作上下文/归因**；**不得作为资源判定输入**（§5） |
| **RC19** | **Epoch / History Context（若已登记）** | 纪元段落/序位等**已登记**的历史上下文（只读）；用于"纪元跨度"条件；`HistoryTags` 仅归因（`RZ2`） |
| **RC20** | **显式资源参数（已登记集合）** | 只能来自已登记的配置集合（不得在本层新增参数） |
| **RC21** | **输入封闭（全局约束）**：白名单（`RC13`–`RC20`）之外的量一律不得读取 | 越权读取 = 缺陷 |

### 2.2 禁止的输入（黑名单）

| # | 禁止 Resource 读取 |
|---|---|
| **RC22** | **Player**（位置、进度、背包、已探索、已采集统计） |
| **RC23** | **Runtime State**（脏标记、加载集合、tick、帧序、预算） |
| **RC24** | **Mining Result**（挖掘记录、编辑层、采集状态） |
| **RC25** | **Inventory**（任何物品/库存状态） |
| **RC26** | **Entity**（对象、结构、矿块实体、生物的存在与状态） |
| **RC27** | **Voxel Current State**（"该坐标当前是什么体素"、已生成内容、玩家编辑结果） |
| **RC28** | **Mesh**（几何、呈现缓冲；几何不得作为世界真值） |
| **RC29** | **Chunk Loading State**（是否已加载/已生成/已卸载；不得按 Chunk 决定判定） |
| **RC30** | **非权威来源**：Debug/工具自行重算的阈值、未登记派生量、其它层内部量（`WA20` 同构） |

---

## §3 Resource Presence Contract

### 3.1 `ResourcePresence` 的字段（冻结，闭合集合）

| # | 字段 | 含义 |
|---|---|---|
| **RC31** | **`ResourcePresence` 定义** | 资源存在性的**判定结果**（**不是**对象、不是物品、不是体素、不是掉落） |
| **RC32** | **Existence Result** | 存在性结论：取值集合**沿用既有登记的判定等级**（`Resource_Design.md` §1.3）；本文档**不新增等级** |
| **RC33** | **Confidence / Tendency** | 置信度 / 倾向：**连续量**，范围沿用契约量纲（`WorldParameter_Contract.md` §5.2）；**不得**被解释为"掉落概率"（`RC138`） |
| **RC34** | **Supporting Conditions** | 支撑条件：参与判定的**具体条件值与阈值**（可核对、可复算；`WE6`） |
| **RC35** | **Influencing Layers** | 影响层：哪些层对该结论有贡献（含贡献为 0 的**显式说明**） |
| **RC36** | **Explanation Data** | 解释数据：与 §8 的解释契约一致（条件清单、命中规则引用、排除项） |

### 3.2 硬性规则

| # | 规则 |
|---|---|
| **RC37** | 结果必须**只读**：不得携带任何写入口、不得诱导调用方修改世界 |
| **RC38** | 结果必须**确定**：同 `(Seed, Position, WorldState)` ⇒ 逐字段相同 |
| **RC39** | 结果**粒度到条件为止**：不含对象、物品、掉落、几何、生态内容清单（`WE7` 同构） |
| **RC40** | 结果**不得成为任何阶段的输入**（防环）：任何生成/判定/运行时逻辑不得读取资源判定（`WQ4`；`PC104`） |
| **RC41** | 结果**不得包含指令或建议**：只描述"是否存在/为什么"，不得给出"应该生成什么" |
| **RC42** | 结果必须**标注定稿状态与真值语义**：未定稿/缺失必须显式标记；不得把半成品当有效结论 |
| **RC43** | 结果必须能**区分两层**：明确"地质基础层的结论"与"资源判定层的结论"（`GC97`；§4） |
| **RC44** | 字段是**闭集**（`RC31`–`RC36`）：新增字段必须修改本契约并升版本 |
| **RC45** | **禁止输出 Item**（物品） |
| **RC46** | **禁止输出 Drop**（掉落/战利品） |
| **RC47** | **禁止输出 Entity / Voxel / Chunk / Mesh**（对象、体素、分区、几何） |
| **RC48** | **禁止输出 Mining Result / 采集状态**（采集属 Mining 与未来系统） |

---

## §4 Resource and Geology Relationship（重点）

### 4.1 允许与禁止（冻结）

| # | 规则 |
|---|---|
| **RC49** | **允许方向**：`Geology → DepositPotential → Resource`（**只读消费**；`RU2`；`GC101`） |
| **RC50** | **禁止方向**：`Resource → 修改 Geology`（任何形式的写路径 = 缺陷；`PC60`） |
| **RC51** | Resource **不重新计算洞穴**（洞穴属 Geology 的 P1；`GC51` 同族） |
| **RC52** | Resource **不重新计算密度**（实体/空判定属 Geology 的 P0–P2） |
| **RC53** | Resource **不重新计算地层**（Surface/Depth/Deep 属 Geology） |
| **RC54** | Resource **不重新计算材料**（`Material` 属 Geology 的 P3） |
| **RC55** | `DepositPotential` 的**权威 = Geology**：Resource 不得改写、不得扩展、不得重新定义其分布（`RU1`；`GC98`） |
| **RC56** | **两层模型**：`DepositZone`（地质基础层）与 `ResourcePresence`（资源判定层）是"**基础 ∧ 条件**"关系，不是重复定义（`GC97`） |
| **RC57** | **属性层纪律**：判定不得在"空气"处成立（矿藏倾向必须在实体内；`GC103` 同构） |
| **RC58** | **禁止新增资源体素类型**：V1 体素状态集合冻结（`GC105`） |
| **RC59** | **判定不得仅靠地质**：必须同时包含**环境条件**（与纪元条件，若适用）（`RN8`） |
| **RC60** | Resource **不得声明"地质应为 X"**：不得反向建议地质判定（防止隐式回写） |
| **RC61** | **Geology 不得读资源判定**来调整自身判定（`GC104`） |
| **RC62** | **介质/地层条件只读**：只能读取地质解释字段，不得触发地质重算（`RC21`） |
| **RC63** | 解释必须**显式区分**"地质基础"与"资源判定"（`RC43`） |
| **RC64** | 违反 §4 任一条 ⇒ 判定为**缺陷**（`RCV6`） |

---

## §5 Resource and Biome Relationship

| # | 规则 |
|---|---|
| **RC65** | **Biome 只能作为 Context（解释上下文）**：用于解释/归因/可读性 |
| **RC66** | **Biome 不能作为 Resource Rule（判定规则）**：不得把群系结论作为判定输入（`RN7`；`BC92`） |
| **RC67** | **错误形式（禁止）**：以群系直接映射资源（例如"某群系 → 某资源"的映射表作为规则） |
| **RC68** | **正确形式**：`Temperature + Geology + Epoch + Biome Context ⇒ Resource Explanation`（`RN8` 同族；`BC97` 同构） |
| **RC69** | Biome Context 的用途**限定**为：解释中的"环境归类说明"与归因；**不得**参与条件判定（`PW5`） |
| **RC70** | **禁止 `Biome → Resource` 输入边**：结构扫描须证明该边为 0（`BC96`） |
| **RC71** | **禁止 `RegionType` 作输入**（类型标签无规则权；`EG5` 同构） |
| **RC72** | **禁止以群系名替代参数条件**：任何"群系条件"必须能**展开为参数条件**（否则不得使用） |
| **RC73** | **Biome 不得读资源判定**：群系层不得消费 `ResourcePresence`（`BC98`；`PC58`） |
| **RC74** | 群系侧的**可关闭性**必须保持：关闭群系修正 ⇒ 与既有基线一致（不影响资源判定存在性） |
| **RC75** | **群系缺失时判定仍须成立**：判定不得依赖 Biome 的存在（Biome Context 为可选上下文；`BC98` 同族） |
| **RC76** | 违反 §5 任一条 ⇒ 判定为**缺陷**（`RCV16`/`RCV17`） |

---

## §6 Resource and Ecology Relationship

| # | 规则 |
|---|---|
| **RC77** | **二者独立**：Resource 与 Ecology **互不读取对方的判定/候选结果**（`Resource_Design.md` §8.2③；`RX-8`） |
| **RC78** | **禁止 Ecology → Resource 判定输入**（不得由"已有植被/生物"推导资源存在性） |
| **RC79** | **禁止 Resource → Ecology 候选**（不得由资源判定产出生态内容） |
| **RC80** | **生物资源两级模型**：Resource 只给**载体资格**；**实际存在性归 Ecology**（`RQ4`） |
| **RC81** | 生态结果**只能作为解释上下文**（若被解释层引用），**不得**作为判定条件 |
| **RC82** | 二者**可以共享同一上游**（`FinalState` 等），但**不得互相读取** |
| **RC83** | **顺序无关**：Resource 与 Ecology 的求值顺序**可交换**（`WorldQuery_Architecture_Contract` 同族可交换纪律） |
| **RC84** | **禁止反推**：不得以"这里有植被/生物"反推资源判定，也不得以资源判定反推生态 |
| **RC85** | Resource **不得生成任何生态内容**（生态候选属 Ecology） |
| **RC86** | 违反 §6 任一条 ⇒ 判定为**缺陷**（`RCV18`） |

---

## §7 Determinism and Local Evaluation

### 7.1 确定性

| # | 规则 |
|---|---|
| **RC87** | **同 `Seed` + 同 `Position` + 同 `WorldState` ⇒ 同结果**（逐字段） |
| **RC88** | 判定必须**可独立重复求值**（无副作用、无状态累积） |
| **RC89** | **禁止 `Random()`** 与任何有状态伪随机源 |
| **RC90** | **禁止时间类来源**：`Time` / `DateTime` / `Guid` / 时间戳 / tick / 帧序 |
| **RC91** | **禁止 Chunk 顺序与加载顺序**依赖：不得按 Chunk 划分、按 Chunk 缓存、按加载状态改变判定 |
| **RC92** | **禁止 Runtime 状态**依赖（`RC23`） |
| **RC93** | **禁止玩家状态**依赖（`RC22`） |
| **RC94** | **禁止遍历顺序**依赖（容器遍历不得影响结果与解释顺序） |
| **RC95** | **域分离**：只使用已登记域；**禁止复用保留域**（`R-1`/`R-2`；`PZ12`） |
| **RC96** | **单点可求值**：任一世界坐标可独立求判定，无需世界级前置 |
| **RC97** | **禁止世界级前置**：不得要求"先算出全量资源分布/全局资源表" |
| **RC98** | **禁止依赖邻域**：不得因邻居是否存在/已求值而改变判定 |
| **RC99** | 成本必须与**该点相关数据量**相关，而非与世界总量相关 |
| **RC100** | **缓存必须透明**：可随时清空、不改变输出、不成为权威、不跨 `Seed`/参数复用 |
| **RC101** | **不要求跨帧世界实例**：连续两次查询之间不需要存活对象 |
| **RC102** | **反例保护**：不同 `Seed`（或不同显式参数）⇒ 至少一处判定不同（防恒真断言） |
| **RC103** | 与**查询顺序**无关（乱序/并发不改结果） |
| **RC104** | 违反 §7 任一条 ⇒ 判定为**缺陷**（`RCV1`–`RCV5`） |

---

## §8 WorldQuery Explanation

### 8.1 必须能给出的解释内容（冻结）

| # | 规则 |
|---|---|
| **RC105** | 必须支持**资源类解释查询**（"这里为什么有/没有资源"） |
| **RC106** | 必须给出 **`DepositPotential` 的取值**及其来源层（地质基础层） |
| **RC107** | 必须给出 **深度/地层条件**（Depth condition）的成立情况 |
| **RC108** | 必须给出**环境条件**的成立情况（例如温度类条件等；条件名沿用各层登记的结论类型） |
| **RC109** | 必须给出**纪元条件**（Epoch condition）的成立情况（`RY11`） |
| **RC110** | 必须给出**"命中的资源判定规则/条件"的引用**（引用而非定义；规则定义权归 `Resource_Design.md`） |
| **RC111** | **错误形式（禁止）**："这里有矿，因为是矿区"——**不得以地名/区域名/群系名充当解释** |
| **RC112** | **正确形式**：`地质倾向 = X` → `深度条件 = Y` → `环境条件 = Z` → `纪元条件 = W` → `判定规则命中` ⇒ 结论（条件清单形式，可核对） |

### 8.2 硬性规则

| # | 规则 |
|---|---|
| **RC113** | 解释必须**与判定同源**：使用同一套条件与阈值（`WQ1`；`WA20`） |
| **RC114** | **禁止 Debug/工具另算一套**（简化/近似/独立实现一律禁止） |
| **RC115** | 解释必须**只读**：不得改变判定、缓存或任何层输出 |
| **RC116** | 解释必须**确定**：同查询点解释内容与顺序恒定 |
| **RC117** | 解释**粒度到条件为止**：不含对象、物品、掉落、几何、生态内容清单 |
| **RC118** | 必须支持**"为什么这里没有资源"**：指出具体失败条件（无地质倾向 / 深度不符 / 环境不足 / 纪元不足 / 属性层关闭） |
| **RC119** | 必须**显式说明"无影响的层"**（贡献为 0 的层不得静默省略） |
| **RC120** | 解释**不得成为任何阶段的输入**（终端产物；`PC104`） |

---

## §9 Consumer Permission Matrix

**行** = Resource 侧产物；**列** = 消费者。
**图例**：`O` = 拥有/产出；`R` = 只读；`R*` = **仅解释**只读；`✗` = 禁止；`—` = 不适用。

| Resource 侧产物 ＼ 消费者 | WorldState | Region | Biome | Geology | Resource | Ecology | Voxel | Runtime | Entity | WorldQuery | Tool / UI |
|---|---|---|---|---|---|---|---|---|---|---|---|
| **`ResourcePresence`** | ✗ | ✗ | ✗ | ✗ | **`O`**（唯一产出者） | ✗（判定互不读取） | ✗ | `R`（仅展示） | `R`（只读消费，未来系统） | `R`（解释） | `R` |
| **Explanation Data** | ✗ | ✗ | ✗ | ✗ | **`O`** | ✗ | ✗ | `R`（仅展示） | ✗ | `R*` | `R` |
| **Rule Definition** | ✗ | ✗ | ✗ | ✗ | **`O`**（规则归属本层） | ✗ | ✗ | ✗ | ✗ | `R*`（同源求值所需） | ✗ |
| **Debug Metadata** | ✗ | ✗ | ✗ | ✗ | **`O`** | ✗ | ✗ | `R`（仅调试展示） | ✗ | ✗ | `R` |

**矩阵硬性规则与自查**

| # | 规则 / 自查 | 期望 |
|---|---|---|
| **RC121** | 矩阵按上表冻结；图例与单元语义唯一 | 结构一致性 |
| **RC122** | **Resource 对自身规则 `O`**：`Rule Definition` 行仅 Resource 列为 `O` | 必须为 1 个 `O` |
| **RC123** | **Resource 对外只读**：除自身三行外，Resource 列对上游全部为 `✗`（不得写任何上游） | 上游写路径 = 0（`RC50`；`WSC56`） |
| **RC124** | **Runtime 无写**：`Runtime` 列全部为 `R`/`✗`，无 `O`、无写权限 | 必须为 0（`RC92`） |
| **RC125** | **Mining 无写**（显式条款）：Mining 不是本矩阵列；Mining 对 `ResourcePresence`/Explanation/Rule/Debug **全部不得写**；采集结果属 Mining 与未来系统 | 写路径 = 0（`RC48`） |
| **RC126** | **Entity 无写**：`Entity` 列无 `O`、无写权限（只允许 `R` 消费判定） | 必须为 0（`RC79`；`EF1`） |
| **RC127** | **WorldQuery 只读**：`WorldQuery` 列只有 `R`/`R*`，无 `O`、无写权限 | 必须为 0 写标记（`WQ4`） |
| **RC128** | **生成层不得读资源判定**：`WorldState`/`Region`/`Biome`/`Geology`/`Voxel` 五列对全部四行均为 `✗` | 必须为 0 |
| **RC129** | **Ecology 不得读判定**：`Ecology` 列对 `ResourcePresence`/Explanation 为 `✗`（仅可共享上游，不得互读） | 必须是（`RC77`） |
| **RC130** | **Tool/UI 无生成权限**：`Tool / UI` 列全部为 `R`（只读展示） | 必须为 0 写标记 |
| **RC131** | 全表 `O` 只允许出现在 `Resource` 列（四行），且不得出现"对上游的 `O`" | 必须为 4 个 `O`、全部在 Resource 列 |
| **RC132** | 不存在"读判定又写判定"的单元（读写分离） | 必须为 0 |
| **RC133** | `Rule Definition` 行不得被任何生成层读取（防第二套规则） | 必须为 0（`RC114`） |
| **RC134** | `Debug Metadata` 行不得被生成层与 Entity 读取 | 必须为 0 |
| **RC135** | 矩阵必须可由静态结构扫描核验（每格语义可判定） | 可验证性 |
| **RC136** | 违反 §9 任一条 ⇒ 判定为**缺陷** | `RCV22` |

---

## §10 V1 Scope

| # | 规则 |
|---|---|
| **RC137** | **不定义资源种类列表**：不列出、不命名任何具体资源种类（种类权威 = `Resource_Design.md` §2；本文档不新增、不复述） |
| **RC138** | **不定义物品 / 掉落 / 战利品语义**（无掉落表、无概率掉落、无数量规则） |
| **RC139** | **不定义生成/放置规则**（矿块实体、可拾取物、节点对象 → 未来 Resource Content / Entity 系统） |
| **RC140** | **不定义代码符号**：不定义接口名、方法名、类型名、管理器/提供者式命名或调用约定 |
| **RC141** | **不定义数值/阈值/算法/噪声**（判定条件与阈值归 `Resource_Design.md` 与实现规范） |
| **RC142** | **不定义存储/缓存/索引实现**（只要求"透明、可清空、非权威"） |
| **RC143** | **不定义 Mining 交互**：不定义开采判定、开采结果、工具需求 |
| **RC144** | **不定义 UI / 工具实现**：不定义面板、可视化、导出格式 |
| **RC145** | **只定义架构**：定位与边界（§1）、输入（§2）、结果形态（§3）、与 Geology（§4）/ Biome（§5）/ Ecology（§6）的关系、确定性与局部求值（§7）、解释契约（§8）、权限矩阵（§9）、验收（§11） |
| **RC146** | **不新增**：不新增参数、不新增 `VoxelType`、不新增因果边 |
| **RC147** | **不修改**：不修改任何既有规范、任何 `Assets/**`、任何代码 |
| **RC148** | **声明性**：本契约不影响任何生成结果（`RCV20`） |
| **RC149** | **与既有规范的关系**：资源**规则权威**仍是 `Resource_Design.md`；本契约只固定**架构边界**，不复制其规则内容 |
| **RC150** | 变更必须走附录 D 的规则（升版本 + 同步更新 §1–§9 与附录 A/B） |

---

## §11 Acceptance Rules

### 11.1 确定性

| # | 不变量 | 验证方法 |
|---|---|---|
| **RCV1** | 同 `(Seed, Position, WorldState)` ⇒ 判定逐字段一致 | 重复求值对照（`RC87`） |
| **RCV2** | 不同 `Seed`/参数 ⇒ 至少一处不同（**反例保护**） | 双世界差异断言（`RC102`） |
| **RCV3** | 与 Chunk 顺序 / 加载顺序 / 帧序 / 查询顺序无关 | 乱序与多顺序对照（`RC90`/`RC91`/`RC103`） |
| **RCV4** | 单点可求值：无世界级前置、不依赖邻域、不要求世界实例 | 求值范围检查（`RC96`–`RC101`） |
| **RCV5** | 缓存透明：清空后结果不变 | 清缓存前后对照（`RC100`） |

### 11.2 边界与所有权

| # | 不变量 | 验证方法 |
|---|---|---|
| **RCV6** | **不修改 Geology**：四类重算（洞穴/密度/地层/材料）路径 = 0；无对 `DepositZone` 的写 | 结构扫描（`RC50`–`RC55`） |
| **RCV7** | 不修改 WorldState / Voxel / Runtime State | 结构扫描（`RC3`/`RC123`；`WSC56`） |
| **RCV8** | 不生成物品/实体/掉落；不输出采集结果 | 结构扫描（`RC45`–`RC48`） |
| **RCV9** | 不拥有任何上游产物（无私有真值副本） | 结构扫描（`RC6`） |
| **RCV10** | 不是 Geology 子系统：不存在复制的地质判定链 | 结构扫描（`RC5`/`RC51`–`RC54`） |

### 11.3 输入与输出

| # | 不变量 | 验证方法 |
|---|---|---|
| **RCV11** | 输入封闭：只出现白名单（`RC13`–`RC20`） | 结构扫描（`RC21`） |
| **RCV12** | 禁用输入 = 0：Player / Runtime / Mining Result / Inventory / Entity / Voxel Current / Mesh / Chunk Loading 八类路径为 0 | 结构扫描（`RC22`–`RC29`） |
| **RCV13** | 输出字段闭合：只出现 `RC31`–`RC36` 的字段 | 字段集合比对（`RC44`） |
| **RCV14** | 输出版禁止项 = 0：Item / Drop / Entity / Voxel / Chunk / Mesh / Mining Result 均不得出现 | 结构扫描（`RC45`–`RC48`） |
| **RCV15** | 输出不得成为任何阶段输入 | 结构扫描（`RC40`；`PC104`） |

### 11.4 关系与解释

| # | 不变量 | 验证方法 |
|---|---|---|
| **RCV16** | Biome 只作 Context：`Biome → Resource` 判定边 = 0 | 结构扫描（`RC66`/`RC70`） |
| **RCV17** | `RegionType` / 群系名 不得作判定输入；条件必须可展开为参数 | 结构扫描 + 条件展开检查（`RC71`/`RC72`） |
| **RCV18** | Resource 与 Ecology 互不读取判定 | 结构扫描（`RC77`/`RC78`/`RC79`） |
| **RCV19** | 解释同源且要素齐备：`DepositPotential` → 深度条件 → 环境条件 → 纪元条件 → 命中规则；与判定逐项一致 | 条件集合比对 + 问答检查（`RC105`–`RC113`；`WA20`） |

### 11.5 零修改与声明性

| # | 不变量 | 验证方法 |
|---|---|---|
| **RCV20** | **声明性**：开启/关闭本契约 ⇒ 体素输出逐体素一致（差异 = 0） | 两态体素快照比较（`RC12`） |
| **RCV21** | **零修改**：既有实现与既有规范 SHA 不变；不新增参数/`VoxelType`/因果边/资源种类 | 文件哈希比对 + 集合比对（`RC10`/`RC146`） |
| **RCV22** | **矩阵自查全过**（`RC121`–`RC136`） | 矩阵解析与结构扫描 |

---

## 附录 A：数据流图

```
        ┌──────────────── 只读上游（既有解释信息）────────────────┐
        │  Seed · World Position                                  │
        │  WorldState FinalState（仅环境解释）                      │
        │  Region（只读上下文）                                    │
        │  Biome Context（仅解释用途）                              │
        │  Geology Explanation + DepositPotential（地质基础层）      │
        │  Epoch / History Context（若已登记；仅归因/纪元条件）       │
        └───────────────────────┬─────────────────────────────────┘
                                │ 只读
                                ▼
                 ┌──────────────────────────────────────┐
                 │  Resource（资源存在性解释层 · 本契约）   │
                 │  · 环境条件 + 地质基础 + 纪元条件        │
                 │  · 判定等级沿用既有登记                 │
                 │  · 不重算洞穴/密度/地层/材料             │
                 └──────────────┬───────────────────────┘
                                │ 只读发布
                                ▼
                        ResourcePresence
                 （Existence / Confidence·Tendency /
                   Supporting Conditions / Influencing Layers /
                   Explanation Data）

  消费：WorldQuery（解释）· Runtime（仅展示）· Entity（只读消费，未来系统）
  禁止：WorldState / Region / Biome / Geology / Voxel 读取判定（RC128）
  禁止：Resource → Geology（RC50）· Biome → Resource（RC66）· Ecology ↔ Resource 判定互读（RC77–RC79）

  ═══ 注：物品/实体/掉落/采集结果 属未来 Resource Content / Entity / Mining（RC139/RC143）═══
```

## 附录 B：权限速查表

| 对象 | 拥有者（`O`） | 只读消费方 | 禁止读取方 | 写权限 |
|---|---|---|---|---|
| **`ResourcePresence`** | **Resource** | WorldQuery（解释）· Runtime（仅展示）· Entity（只读消费）· Tool/UI | WorldState · Region · Biome · Geology · Ecology · Voxel | 无（`RC123`） |
| **Explanation Data** | **Resource** | WorldQuery（`R*`）· Runtime（仅展示）· Tool/UI | 全部生成层 · Ecology · Entity | 无 |
| **Rule Definition** | **Resource** | WorldQuery（`R*`，同源求值） | 全部生成层 · Tool/UI（不得复制规则） | 无（`RC133`） |
| **Debug Metadata** | **Resource** | Runtime（调试展示）· Tool/UI | 全部生成层 · Entity | 无（`RC134`） |
| **上游各层产物** | 各自层 | Resource（白名单只读） | — | **Resource 无任何写权限**（`RC50`；`WSC56`） |

## 附录 C：禁止清单

| # | 禁止项 | 反例（必须判为缺陷） | 依据 |
|---|---|---|---|
| **RCX-1** | Resource 修改 Geology | 判定时顺手调整矿藏倾向 | `RC50` |
| **RCX-2** | Resource 重算地质判定 | 自行重算洞穴/密度/地层/材料 | `RC51`–`RC54` |
| **RCX-3** | Resource 修改 WorldState | 由资源判定回写参数 | `RC3`；`WSC56` |
| **RCX-4** | Resource 修改 Voxel / Runtime | 放置方块、写体素、写加载状态 | `RC3`/`RC27` |
| **RCX-5** | Resource 生成物品/实体/掉落 | 判定通过即创建对象 | `RC3`/`RC45`–`RC47` |
| **RCX-6** | Resource 定义资源种类 | 在契约里列出具体资源清单 | `RC10`/`RC137` |
| **RCX-7** | Biome → Resource 判定 | 由"森林"直接推出某资源 | `RC66`/`RC70`；`RN7` |
| **RCX-8** | 群系名/区域名充当解释 | "这里有矿，因为是矿区" | `RC111` |
| **RCX-9** | 由结果反推 | 由"已有植被"推断资源存在 | `RC78`/`RC84` |
| **RCX-10** | Resource → Ecology | 由资源判定产出生态候选 | `RC79` |
| **RCX-11** | 玩家状态参与判定 | 由玩家进度决定资源是否存在 | `RC22`/`RC93` |
| **RCX-12** | Runtime 写判定 | 运行时事件改写存在性结论 | `RC23`/`RC124` |
| **RCX-13** | Chunk 决定判定 | 按 Chunk 缓存或按加载状态改变结论 | `RC29`/`RC91` |
| **RCX-14** | 以 Mesh 作真值 | 由几何形态反推矿藏倾向 | `RC28` |
| **RCX-15** | Debug 另算一套 | 调试面板用简化公式解释资源 | `RC114` |
| **RCX-16** | 判定成为生成输入 | 生成阶段读取 `ResourcePresence` 或规则定义 | `RC40`/`RC133`；`PC104` |

## 附录 D：对齐登记与变更规则

### D.1 登记项（**本文档不修改任何既有文件**）

| # | 需对齐内容 | 位置 | 真相（按本契约） |
|---|---|---|---|
| ① | **编号命名空间冲突**：本文档 `RC1`–`RC150` 与 `Region_Design.md` 的 `RC1`–`RC5`（反向因果禁令）同前缀 | 本文档 vs `Region_Design.md` §3.3 | 语义不同、文档不同；**引用必须带文档名**；既有文件未被修改 |
| ② | 任务书 §5 的链条（… → Biome Context → Geology DepositPotential → ResourcePresence）与既有因果链的关系 | 任务书；`GC97`；`RN7`/`RN8` | **一致**：Biome 只作 Context（`RC65`），地质基础层由 Geology 提供（`RC17`），判定层在末端（`RC31`） |
| ③ | "资源存在性"表述歧义 | `Ecology_Design.md:286`（`ER2`）；`WorldParameter_Contract.md` §3.5；`Resource_Design.md` §4.2 | **两层模型**：`DepositZone` = 地质基础层（Geology）；`ResourcePresence` = 资源判定层（Resource）；`ER2` 的意图（Ecology 不得定义/修改地质基础层）保持成立（`RC7`/`RC56`） |
| ④ | 任务书 §3 要求"至少包含"五项字段 | 任务书；`Resource_Design.md` §1.3（判定等级） | 五字段在 `RC32`–`RC36` 冻结；判定**等级取值**沿用既有登记，本文档**不新增等级** |
| ⑤ | 任务书禁止"新增资源种类列表" | 任务书；`Resource_Design.md` §2 | 一致：种类清单权威在既有规范；本文档**不新增、不复述**（`RC10`/`RC137`） |
| ⑥ | 任务书禁止代码符号（Provider / Manager / 生成函数命名） | 任务书 | 一致：`RC140` 明确不定义任何符号；全文不含代码符号（详见提交报告的核验） |
| ⑦ | 审查 `P1-5`（资源 ↔ Mining ↔ Entity 无落点）/ `AD-6`（对象化时点） | 审查 §3/§4 详情 | 本契约只固定**解释层边界**（`RC11`/`RC139`/`RC143`）；**对象化与采集**仍属未来规范，`AD-6` 保持开放 |
| ⑧ | Resource 与 Ecology 的顺序 | `WorldQuery_Architecture_Contract` / `RN7` 同族可交换纪律 | 冻结为**可交换且互不读取判定**（`RC77`/`RC83`） |

### D.2 变更规则

1. 修改输入白名单、结果字段集合、与 Geology/Biome/Ecology 的关系或权限矩阵，**必须**升版本并同步更新 §2–§9 与附录 A/B。
2. **不得放宽**：`RC3`（六项不负责）、`RC5`（非子系统）、`RC10`（不定义种类）、`RC21`/`RC22`–`RC30`（输入封闭与黑名单）、`RC37`–`RC48`（结果硬性规则与禁止输出）、`RC49`–`RC64`（Geology 关系）、`RC65`–`RC76`（Biome 关系）、`RC77`–`RC86`（Ecology 关系）、`RC87`–`RC104`（确定性）、`RC121`–`RC136`（矩阵自查）。
3. 与既有规范冲突时，先按 D.1 **登记**，再由对应规范的**版本变更**解决；本文档不得直接改写其它规范。
4. 任何"资源对象化 / 采集 / 掉落"提案必须**独立成规范**（Resource Content / Entity / Mining），并证明：不修改 Geology/WorldState/Voxel/Runtime、不破坏确定性、既有 12 项验证保持 PASS、且**可关闭**。
5. 任何"新增资源种类"提案必须提交到 `Resource_Design.md`（规则权威），**不得**在本契约内新增（`RC10`/`RC137`）。
