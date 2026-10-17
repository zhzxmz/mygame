# Biome Architecture Contract V1

> **文档性质**：**架构契约规范（Architecture Contract）**。本文档只定义 **Biome 层的边界、输入、分类契约、确定性、解释与权限**。
> **仅创建 Markdown**；**不修改任何已有文件**；**不创建 `.cs`**；**不创建 class / interface / struct**；**不修改 `Assets/**`**；
> **不实现 Biome 逻辑**；**不新增参数**；**不新增 `VoxelType`**。
>
> **核心定位**：
>
> > **Biome = "WorldState explanation layer"（世界状态解释层）。**
> > 它回答：**"给定这里的世界条件，这里属于什么环境类别？"**
>
> **Biome 不是**：
>
> | Biome 不是 | 说明 |
> |---|---|
> | 世界生成来源（world generation source） | 不产生参数、不产生区域、不产生地形、不产生体素 |
> | 参数拥有者（parameter owner） | 8 个环境参数的 Owner 是 WorldState 层（`WorldParameter_Contract.md` §4） |
> | Region 的替代品（Region replacement） | Region 是**原因层**，Biome 是**解释层**（§6） |
> | Runtime 状态（Runtime state） | 不属于 Runtime State，不随运行时变化（§9） |
> | 资源权威（Resource authority） | 资源判定权归 Resource 层（`Resource_Design.md` §4.2；§8） |
> | 生态权威（Ecology authority） | 生态候选权归 Ecology 层（`Ecology_Design.md` EA3；§7） |
> | 体素存储（Voxel storage） | 不写体素、不存材料、不新增体素类型（§1/§11） |
> | 第二套世界规则系统 | 不重新定义参数、阈值、噪声或地形（§5/§12） |
>
> **目标数据流（本文档要固定契约的部分）**：
>
> ```
> WorldState（FinalState：8 个环境参数）
>     │
>     v
> Biome（分类结论：Category + Profile + Explanation）
>     │
>     +---- Ecology（读取 Biome 作为环境上下文 → 生态候选）
>     │
>     +---- Resource interpretation（**仅解释上下文**；判定输入不得含 Biome，§8）
> ```
>
> **必须兼容的上游规范**：
>
> | 规范 | 与本契约的关系 |
> |---|---|
> | `WorldState_Design.md` | **群系规则权威**：§5.1（不是随机选择）、§5.2（输入=环境参数向量）、**§5.3 有序规则表（first-match-wins）与 B1–B5**、§5.4（示例，非实现清单）、§5.5（海洋短路）、§5.6（CT1–CT4 连续性）、§5.7（V1 边界：只能影响地表材料）、§5.8（退化兼容）、§1.4/§1.5（允许语义与倒因为果禁令 C1–C4） |
> | `WorldParameter_Contract.md` | **参数契约**：§3.2 表 A（`Biome` 的可读取层 = Biome/Ecology/Resource/WorldQuery/Runtime；`Slope`/`Continent` **未列入** Biome 的可读取层）、§3.5 表 D（`Biome` 是**结论**，禁止作为资源判定输入）、§4 权限矩阵（`Biome` 行：WorldState = `O（规则表）`、Biome 层 = `O（结论）`、Geology = `R（仅 P3）`、Ecology = `R`、**Resource = `—`（禁止）**、WorldQuery = `R`、Runtime = `R`）、§5.2 范围、§6.4（域 `18 = Biome`）、§8.2①（Biome 只在 Geology 的 **P3** 被读取）、`PW4`（读取必须落在可读取层内） |
> | `Region_Architecture_Contract.md` | **Region 侧契约**：§6（`RegionType` 无规则权）、`RA86`/`RA88`（**禁止 `RegionType → Biome`**）、`RA101`（唯一间接路径 `Region → Modifier → FinalState → Biome → P3`）、§11 权限矩阵 |
> | `WorldState_Runtime_Contract.md` | **求值契约**：`WSC29`（Base 不得读取 Biome）、`WSC51`（WorldState → Geology 只经 Biome 的 **P3 地表材料**）、`WSC52`（不得影响 P0/P1/P2）、§7 真值来源表（Biome 行：真值来源 = 群系规则表，**无"编辑后版本"**） |
> | `WorldQuery_Design.md` | **查询权威**：§2.2 第 5 步（Biome Resolution：输入 = `FinalState`，输出 = Biome，权威 = `WorldState_Design.md` §5.3）、§3.1（Biome 行权限）、§6（`WE1`–`WE8` 解释要求）、`WL1`–`WL13`（局部求值）、`WA17`/`WA20` |
> | `Ecology_Design.md` | **生态权威**：§1.2（Ecology 输入含 Biome）、`EA2`（输入集合）、`EA3`（产出只有候选）、`E-3`（不改世界）、`EG3`/`EF1`（禁止反向写） |
> | `Resource_Design.md` | **资源权威**：`RN7`（**Biome 不得作为资源判定输入**）、`RN8`（正确形式 = 参数 + 地质 + 纪元）、`RN9`（理由）、`RX-1`（缺陷判定）、§4.2（两层存在性） |
> | `WorldGeneration_Pipeline_Design.md` | Phase 5 的定位与输入封闭（§2.1/§2.2、§7.2 群系集合描述 + `PM3`）、`PA1`、附录 B.1、附录 C |
> | `WorldGeneration_Geology.md` / `..._Runtime_Implementation.md` | **下游消费者**：§7 群系接口（`Biome(x,z)`、`BiomeProfile`）、§6.1（群系在 V1 **只影响地表材料**）、§6.2（不改变密度分布） |
> | `VoxelWorld_Core.md`、`ADR-001` | 体素状态集合冻结（Air/Grass/Dirt/Stone）、Entity/Chunk 概念边界 |
> | `WorldGeneration_Architecture_Audit.md` | 审查结论：P1-8（占位群系与量纲迁移）、P0-5（Ecology 输入集合）、AD-3、M3（Biome 层落地顺序） |
>
> **冲突时的优先级**：**群系规则与判定顺序**以 `WorldState_Design.md` §5 为准；**参数身份/量纲/读写权限**以 `WorldParameter_Contract.md` 为准；
> **查询与解释格式**以 `WorldQuery_Design.md` 为准；**Region 侧边界**以 `Region_Architecture_Contract.md` 为准；
> **资源判定输入**以 `Resource_Design.md` 为准；**生态输入**以 `Ecology_Design.md` 为准；
> **本契约负责**：Biome 的**定位、输入封闭、分类契约、身份形态、规则归属、与四类消费者的边界、局部求值与权限矩阵**。
>
> **零修改声明**：本文档是**声明性文档**，不产生任何运行时效果；开启/关闭本文档**不得改变任何体素输出**（`BCV21`）。
> 与既有规范的措辞差异一律在**附录 D 登记**，**不修改**任何既有文件。

---

## §1 Biome Position and Boundary

### 1.1 责任（Biome 做什么）

| # | 责任 |
|---|---|
| **BC1** | **分类**：把 `FinalState`（环境参数向量）解释为一个**离散的环境类别**（Biome 结论） |
| **BC2** | **侧写（Profile）**：给出该结论的**环境侧写**（只读的参数引用 + 分类标签），供下游作为**上下文**使用 |
| **BC3** | **解释数据**：给出"为什么是这个类别"所需的判定信息（命中的规则分支、使用的参数值与阈值、被排除的分支） |
| **BC4** | **确定性**：同 `(Seed, Position, WorldState)` ⇒ 同结论（§3） |

### 1.2 非责任（Biome 不做什么）

| # | 非责任 |
|---|---|
| **BC5** | 不**产生/修改**任何参数（`WorldParameter_Contract.md` `PT9`/`PW2`） |
| **BC6** | 不产生/修改 `RegionModifier`（`Region_Design.md` `RM5`；`Region_Architecture_Contract.md` `RA75`） |
| **BC7** | 不产生/修改地形（`Density`/`Material`/`CaveDensity`/`DepositZone`）与体素 |
| **BC8** | 不产生/修改 `ResourcePresence`（资源判定归 Resource 层，§8） |
| **BC9** | 不产生/修改生态候选（归 Ecology 层，§7） |
| **BC10** | 不产生/修改 Runtime State，也不依赖 Runtime State（§9） |
| **BC11** | 不定义群系**清单/阈值/噪声**（属 `WorldState_Design.md` §5.3 与实现规范；§5/§12） |

### 1.3 与六类对象的关系（摘要）

| 对象 | Biome 与之的关系 | 依据 |
|---|---|---|
| **WorldState**（参数层） | **唯一上游**：Biome 读取 `FinalState`（只读），不写任何参数 | §2；`Pipeline` §1.3 |
| **Region**（原因层） | Region 经由 `RegionModifier → FinalState` **间接**影响 Biome；Biome **不得**读 `RegionType`/`RegionView` | §6；`RA88` |
| **Ecology**（生态层） | Biome 提供**环境上下文**；Ecology **消费** Biome；Ecology **不得**改 Biome | §7；`EA2` |
| **Resource**（资源层） | Biome **不得**作为资源判定输入；Biome 只可出现在**解释上下文**中 | §8；`RN7`/`RX-1` |
| **Voxel / Geology** | Biome **不写体素**；Geology 在 **P3** 只读 Biome 后**自行**决定地表材料 | §1.4；`WSC51` |
| **Runtime / Entity** | 只读消费（Entity 可读 Biome 作为上下文）；**禁止**任何写回 | §11 |

### 1.4 Biome 不能修改什么（硬性禁止）

| # | 禁止 Biome 修改 | 依据 |
|---|---|---|
| **BC12** | **`WorldParameter`（8 个环境参数）** | `PT9`；`WorldState_Design.md` C1–C4 |
| **BC13** | **`RegionModifier`**（含任何分量） | `RA75`/`RA77` |
| **BC14** | **WorldState**（`Base` 与 `FinalState`，含"补偿/归一化"修正） | `WSC43`/`PT9` |
| **BC15** | **Geology**（`Density` / `Material` / `CaveDensity` / `DepositZone` / 地层） | `RE1`/`RA103`；`WorldGeneration_Geology.md` §10 |
| **BC16** | **`ResourcePresence`** | `Resource_Design.md` §4.2；`RX-1` |
| **BC17** | **Runtime State**（体素编辑、玩家状态、加载/脏标记） | `PX2`；`RA78` |
| **BC18** | **Ecology 候选** | `EA3`；`E-3` |
| **BC19** | **体素真值**（不得写体素、不得新增 `VoxelType`、不得要求新的地表材料） | `RI4`；`WSC3` |

> **材料归属澄清（重要）**：**地表材料的决定权属 Geology**（其 **P3** 规则把 Biome 结论作为**输入之一**）。
> Biome **不拥有**材料，也不"设置"材料；Biome 只提供**分类结论**（§4/§8.3）。
> 该结论与"Biome 不得定义 final material"（本契约要求）**不矛盾**：一个是**所有权**，一个是**消费关系**。

---

## §2 Biome Input Contract

### 2.1 允许的输入（封闭白名单）

| 输入 | 说明 | 依据 |
|---|---|---|
| **`FinalState`（8 个环境参数）** | `Temperature` · `Moisture` · `Elevation` · `Fertility` · `MagicDensity` · `HolyInfluence` · `CorruptionInfluence` · `Ocean`（逐点只读） | `Pipeline` §1.3（Biome 输入 = `FinalState`）；`WorldState_Design.md` §5.2 |
| **`Seed`** | 仅用于域内确定性（若规则需要域内扰动） | `WorldParameter_Contract.md` §6.3/§6.4 |
| **世界坐标 `Position`** | 仅用于**局部枚举/连续性判定/量化**（不得由坐标推断原因） | `HE8` 同构；`RS3` 局部枚举纪律 |
| **显式群系参数（若存在）** | 只能来自**已登记的配置参数集合**（不得在 Biome 层新增参数） | `BC11`；`PT8` |

**地理信息（Geography）的允许范围**

| 地理量 | Biome 是否可读 | 说明 |
|---|---|---|
| **`Elevation`** | ✅ **可读**（已属 8 参数） | 地形高度（只读转发） |
| **`Ocean`** | ✅ **可读**（已属 8 参数，用于**短路**） | `WorldState_Design.md` §5.5 |
| **`Slope`** | ❌ **不可读** | `WorldParameter_Contract.md` §3.4 的 `Slope` 可读取层**未列入** Biome（`PW4`：越权读取 = 缺陷）；若未来需要，必须先修改该契约并升版本 |
| **`Continent`** | ❌ **不可读** | 同上（其可读取层为 History/Region/WorldQuery/Runtime） |
| **`SeaLevel`**（配置） | ❌ 不可读（`Ocean` 已由上游派生完成） | 派生规则属 Skeleton（`WC3`） |

### 2.2 明确禁止读取（黑名单）

| # | Biome 不得读取 |
|---|---|
| **BC20** | **Resource 结果**（`ResourcePresence` / 资源判定 / `DepositZone`） |
| **BC21** | **Ecology 结果**（生态候选 / `SpawnCandidate` / 区域级资格声明） |
| **BC22** | **Runtime state**（脏标记、加载集合、tick、帧序） |
| **BC23** | **Player state**（位置、进度、背包、已探索、已采集） |
| **BC24** | **Chunk state**（是否已加载/已生成、Chunk 编号作为规则输入） |
| **BC25** | **Entity state**（任何对象/结构/生物的存在与状态） |
| **BC26** | **体素真值**（`Voxel` 值、`Material`、已生成内容） |
| **BC27** | **`RegionType` / `RegionView` / `RegionModifier` 的原始分量**（只能读**已合成的** `FinalState`） |
| **BC28** | **`HistoryTags` 与历史事实**（Biome 的输入不含历史；历史只能经 Region 修正进入 `FinalState`） |
| **BC29** | **上游规则内部量**（噪声中间值、未登记的派生量） |

### 2.3 输入封闭性（硬性）

| # | 规则 |
|---|---|
| **BC30** | Biome 的输入集合是**封闭的**：白名单之外的量一律不得读取（`Pipeline` `PA1`；`PW4`） |
| **BC31** | 输入必须**逐点可求值**：不得要求"先算出整个世界的参数" |
| **BC32** | 输入**不得**携带"目标类别"信息（禁止由期望结果反推输入，`C1`/`C2`） |
| **BC33** | 显式群系参数必须**已登记**；不得引入隐含默认值或隐式全局状态（`PT6`） |

---

## §3 Biome Classification Contract

### 3.1 分类函数（定义）

```
Biome = f( Seed , Position , WorldState )      ← WorldState = 该点的 FinalState（8 参数）
```

| # | 规则 |
|---|---|
| **BC34** | 分类是**确定性函数**：同一 `(Seed, Position, WorldState)` ⇒ **同一 Biome 结论**（逐字段） |
| **BC35** | 判定必须遵循 `WorldState_Design.md` §5.3 的**有序规则表（first-match-wins）**；顺序是规范的一部分（`B1`） |
| **BC36** | `Ocean == true` 必须**短路**（不再参与后续判定，`§5.5`） |
| **BC37** | 所有阈值必须**显式参数化**（`B2`），不得隐藏魔法数字 |
| **BC38** | 禁止"因为某个群系名"反推参数（`B3`）：Biome 是**结果**，不是原因 |
| **BC39** | 判定必须**可重复求值**（`B4`），且不依赖加载顺序、邻域、时间、查询顺序 |
| **BC40** | 结论必须是**离散标识**；相邻坐标结论不同时，其**参数**必须平滑过渡（`B5`/`CT1`–`CT2`） |

### 3.2 禁止的非确定性来源

| # | 禁止 |
|---|---|
| **BC41** | `Random()` / `UnityEngine.Random` / `System.Random` / 任何有状态伪随机源（`PZ5`） |
| **BC42** | `Time` / `DateTime` / 时间戳 / tick / 帧序 / `Guid`（`PZ6`） |
| **BC43** | **加载顺序** / 已加载 Chunk 集合 / 邻域是否已求值（`PZ8`；`WL8`） |
| **BC44** | **查询顺序** / 遍历顺序 / 缓存命中与否（`WL12`；`WD10`） |
| **BC45** | 玩家状态（`PZ7`） |
| **BC46** | 由下游结果反推分类（`WD11`；`WE3`） |

### 3.3 域（Domain）

| # | 规则 |
|---|---|
| **BC47** | 若规则表需要域内扰动，必须使用 `WorldParameter_Contract.md` §6.4 登记的域：**`18 = Biome`**（"若规则表需要；否则不使用"） |
| **BC48** | 域必须参与哈希混合（`PZ11`）；**不得**复用保留域 `R-1`/`R-2`（`PZ12`） |
| **BC49** | 不得新增域 ID（新增必须登记并升版本，`PZ14`） |

---

## §4 Biome Identity Contract

### 4.1 Biome 身份（Identity）的组成

| 组成 | 含义 | 是否可作规则输入 |
|---|---|---|
| **Category** | 离散类别标识（结论本身） | ✅ 作为**下游的上下文输入**（Ecology 上下文 / Geology P3）；**不得**作为上游输入 |
| **Profile** | **环境侧写**：对该点环境参数的**只读引用**与分类标签（例如主导参数、强度档位） | ⚠️ 仅作为上下文/解释；**不得**被当作参数的替代 |
| **Explanation data** | 判定数据：命中的分支、参与比较的参数值与阈值、被排除的分支（§10） | ❌ 仅用于解释输出 |

### 4.2 Biome Identity **不得包含**（硬性）

| # | 不得包含 | 理由 |
|---|---|---|
| **BC50** | **final material（地表材料）** | 材料决定权属 Geology 的 **P3**（§1.4 澄清） |
| **BC51** | **voxel type** | 体素状态集合冻结（Air/Grass/Dirt/Stone），Biome 不新增类型（`BC19`） |
| **BC52** | **resource list** | 资源判定权属 Resource 层（§8；`RN7`） |
| **BC53** | **creature list / vegetation list** | 生态候选权属 Ecology 层（§7；`EA3`）；Biome 只给**环境上下文** |
| **BC54** | **地形修改量**（高度/土壤/频率修正） | V1 明确不改变密度分布（`WorldGeneration_Geology_Runtime_Implementation.md` §6.2）；属后续显式扩展 |
| **BC55** | **对象/结构引用** | 对象属未来 Entity / Structure 系统（ADR-001） |

| # | 规则 |
|---|---|
| **BC56** | **Biome 是分类，不是内容容器**：Identity 的字段集合**不得**随世界内容增长（不得出现"这里有什么"的列表字段） |
| **BC57** | Identity 必须**只读发布**：任何消费者不得修改（§11 矩阵） |
| **BC58** | Identity 必须**逐点确定**：同一 `(Seed, Position, WorldState)` ⇒ 同 Identity（含 Explanation 数据） |
| **BC59** | Explanation 数据必须是**解释用**的，不得作为下游判定条件（`WE7` 粒度：到层与条件为止） |

---

## §5 Biome Rule Ownership

### 5.1 Biome **拥有**（唯一权威）

| # | Biome 拥有 |
|---|---|
| **BC60** | **分类规则**：环境参数向量 → 离散类别的判定规则（含有序分支顺序与阈值判定） |
| **BC61** | **Biome Identity 的产出**（Category / Profile / Explanation） |
| **BC62** | **"为什么是这个类别"的判定数据**（分支与条件命中情况） |

> **注意**：分类规则的**语义权威**仍是 `WorldState_Design.md` §5.3（B1–B5）；本契约只声明"Biome 层是这些规则的**唯一执行者**"，不重新定义规则。

### 5.2 Biome **不拥有**（上游所有）

| # | Biome 不拥有 | 归属 |
|---|---|---|
| **BC63** | `Temperature` 的生成 | WorldState 层（`WorldParameter_Contract.md` §3.2；`WorldState_Runtime_Contract.md` §3） |
| **BC64** | `Moisture` 的生成 | 同上 |
| **BC65** | `Fertility` 的生成 | 同上 |
| **BC66** | `MagicDensity` 的生成 | WorldState + Region（来源/修正） |
| **BC67** | `CorruptionInfluence` 的生成 | 同上 |
| **BC68** | `HolyInfluence` 的生成 | 同上 |
| **BC69** | `Elevation` / `Ocean` 的生成 | Skeleton + 既有高度场（`WSC48`） |
| **BC70** | `RegionModifier` 的生成 | Region 层（`RA75`） |
| **BC71** | 地表材料的决定 | Geology（P3） |
| **BC72** | 资源判定 | Resource 层 |
| **BC73** | 生态候选 | Ecology 层 |
| **BC74** | 阈值与噪声公式的**定义** | `WorldState_Design.md` §5.3 / 实现规范（§12） |

| # | 规则 |
|---|---|
| **BC75** | Biome **不得"重算"上游参数**（不得自带一套温度/湿度/魔力计算；`WQ2` 禁止第二套规则） |
| **BC76** | Biome **不得**在自身内部缓存"参数的不同版本"（例如"Biome 自己算的温度"） |
| **BC77** | Biome **不得**声明"某参数应为何值"（只声明"给定这些值，类别是什么"） |

---

## §6 Biome and Region Relationship

### 6.1 明确：Region ≠ Biome

| 层 | 性质 | 角色 |
|---|---|---|
| **Region** | **cause layer（原因层）** | 来源 → `RegionModifier` → 影响环境参数（`Region_Design.md` RT1） |
| **Biome** | **interpretation layer（解释层）** | `FinalState` → 分类结论（`WorldState_Design.md` §5.1） |

### 6.2 允许与禁止

| # | 规则 |
|---|---|
| **BC78** | **Region 只能通过 `RegionModifier → FinalState` 间接影响 Biome**（不允许任何直连边） |
| **BC79** | **禁止 `RegionType → Biome` 的直接映射**（`RA88`：`RegionType` 无规则权、`RTY2`） |
| **BC80** | Biome **不得读取** `RegionType` / `RegionView` / `RegionModifier` 原始分量 / Region 来源列表（`BC27`；`Region_Design.md` §7.2 白名单：应读取**已合成的** `FinalState`） |
| **BC81** | Biome **不得创建/修改 Region**，不得回写区域级资格声明（`EG3` 同构；`RA20`） |
| **BC82** | 禁止 `Biome → Region`、`Biome → RegionModifier`、`Biome → RegionType`（任何方向） |

### 6.3 正确链条（冻结）

```
WorldState（FinalState；已含 Region 的间接影响）
      +
Geography（仅 `Elevation` / `Ocean`，二者已属 FinalState 的 8 参数）
            ↓
          Biome
```

| # | 规则 |
|---|---|
| **BC83** | 该链条是 Biome 的**唯一**输入路径；不得插入或旁路任何层（`BC30`） |
| **BC84** | 链条的**上游影响必须已经"烘进"参数**：Biome 看到的是**结果值**，看不到原因（原因由查询解释链提供，§10） |
| **BC85** | Biome 结论**不得**被用来解释"为什么这里有这个 Region"（Region 的原因是来源与修正，不由 Biome 解释） |

---

## §7 Biome and Ecology Relationship

### 7.1 定义

> **Biome 只提供环境上下文（environmental context）；Ecology 消费 Biome。**

| # | 规则 |
|---|---|
| **BC86** | Biome 对 Ecology 是**只读上下文**：Ecology 可以读取 Biome 结论作为**环境条件**之一（`Ecology_Design.md` §1.2 `EA2` 明确列出 Biome） |
| **BC87** | Ecology **不得修改 Biome**：不得改写结论、不得回写 Identity、不得创建新的 Biome 类别（`E-3`；`EG3` 同构） |
| **BC88** | Ecology 的候选**不得**成为 Biome 的输入（`BC21`） |
| **BC89** | Biome **不得**声明生态内容（不列物种、不列植被、不列窝点；`BC53`） |
| **BC90** | Ecology 的输入集合仍以 `WorldParameter_Contract.md` §3 的**参数白名单**为准（审查 P0-5 的登记项，见附录 D）；Biome 只是其**上下文之一** |

### 7.2 边界图

```
WorldState ──┬──► Biome ──(只读上下文)──► Ecology ──► Spawn Candidates（资格，不是对象）
             │                              ▲
             └──(参数白名单，直接只读)───────┘

  ✗ Ecology ──► Biome（禁止：改写/回写）
  ✗ Spawn Candidate ──► Biome（禁止：作为 Biome 输入）
```

---

## §8 Biome and Resource Relationship

### 8.1 定义（硬性）

| # | 规则 |
|---|---|
| **BC91** | **资源不能仅由 Biome 产生**（`Resource cannot be generated from Biome alone`） |
| **BC92** | **Biome 不得作为资源判定的输入**（`Resource_Design.md` `RN7`；`RX-1` 判为缺陷） |
| **BC93** | 资源判定的正确输入形式：**Environment 参数** + **地质条件**（介质/矿藏/地层）+ 纪元跨度 + 区域信息（`RN8`） |
| **BC94** | 资源判定的**权威**仍是 **Resource 层**（`ResourcePresence`：None / Substrate / Present；`Resource_Design.md` §4.2） |
| **BC95** | Biome **不得**产生资源对象、不得声明"这里有什么资源"（`BC52`） |

### 8.2 "Biome context" 的合法位置（口径澄清）

| 路径 | 是否允许 | 说明 |
|---|---|---|
| **判定路径**：Biome → 资源判定 | ❌ **禁止** | 判定输入必须挂在**原因**（参数/地质）上（`RN7`/`RN9`） |
| **解释路径**：Biome 出现在**解释文本**中（例如"该处为魔法森林，且 `MagicDensity` 高，故存在魔晶"） | ✅ **允许** | 解释由 `WorldQuery` 组装（`WE1`–`WE8`）；Biome 是**并列上下文**，不是判定依据（`BC96`） |

| # | 规则 |
|---|---|
| **BC96** | 解释中出现 Biome **不得**被实现为"Biome → 资源判定"的输入边；解释必须显式指出**真正的判定依据**（参数值 + 地质条件 + 纪元跨度） |
| **BC97** | 禁止"因为是魔法森林，所以生成魔晶"这一写法（`Resource_Design.md` §3.3；`RX-1`）；正确写法是"因为存在高魔源 → `MagicDensity` 高 + 介质 + 积累，所以形成魔晶" |
| **BC98** | Biome 与资源判定**可以共享同一上游**（`FinalState`），但**不得互相读取**（无 `Biome → Resource`、无 `Resource → Biome` 边） |

---

## §9 Local Evaluation and Determinism

### 9.1 单点求值（硬性）

| # | 规则 |
|---|---|
| **BC99** | Biome 必须支持**单点查询**：给定 `(Seed, Position)`，可独立求出该点的 Biome 结论（`WL1`） |
| **BC100** | 求值**不得**依赖"先算出整个世界的参数"或"先划分世界群系"（`WL4`；`WSC89`） |
| **BC101** | 求值成本必须与该点相关输入相关，而不是与"世界总量"相关（`WL11`） |
| **BC102** | 求值不得要求任何世界级前置（全局注册表、全局群系表、全局候选表；`WL6`） |

### 9.2 禁止事项（用户要求）

| # | 禁止 |
|---|---|
| **BC103** | **禁止生成所有 Biomes**（不得为回答一个点而枚举全世界群系） |
| **BC104** | **禁止存储所有世界 Biomes**（不存在"世界群系表"作为权威） |
| **BC105** | **禁止创建 Biome map（群系地图）**：不得生成"群系栅格/瓦片/区域表"，也不得把它当作真值（`RA93` 同构：禁止按 Chunk 组织） |
| **BC106** | **禁止依赖相邻 Chunk**：不得因邻居是否加载/是否已求值而改变结论（`CT3`；`WL8`） |
| **BC107** | 禁止按 Chunk 缓存 Biome 归属表（`BC105`）；跨 Chunk 边界的分类必须由**参数连续性**保证（`CT1`–`CT3`） |
| **BC108** | 禁止跨帧存活状态：连续两次查询之间不需要"世界实例"（`WL13`） |
| **BC109** | 缓存（若实现）必须**透明**：由 `(Seed, 参数, 世界坐标, 域标签)` 索引、可清空、不得改变输出、不得成为权威（`WL9`/`WL10`） |

### 9.3 确定性验收要点

| # | 规则 |
|---|---|
| **BC110** | 必须提供**反例保护**：不同 `Seed` 或不同参数 ⇒ 至少一处 Biome 结论不同（`WD3`） |
| **BC111** | 必须能证明**顺序无关**：打乱查询顺序/遍历顺序 ⇒ 逐点结论不变（`WL12`） |

---

## §10 World Query Explanation

### 10.1 Query 必须能解释的内容

| # | 必须包含 | 对应上游要求 |
|---|---|---|
| **BC112** | **Result Biome**：最终类别结论 | `WE1`（结论） |
| **BC113** | **Direct conditions**：命中的分支 + 参与比较的参数值与阈值（"0.62 ≥ 0.55 命中"） | `WE1`/`WE6` |
| **BC114** | **WorldState values**：该点 `FinalState` 的 8 参数值（只读，可分解为 Base + Modifier） | `WSC30`/`WSC74` |
| **BC115** | **Influencing layers**：影响该结论的层（Skeleton 气候带 → Region 修正 → WorldState → Biome），以及**各层贡献为 0 时的显式说明** | `WE8` |
| **BC116** | **Excluded layers**：对该结论**无影响**的层与**未命中**的分支（例如"无海洋短路 / 无腐化高值 / 无圣性高值 …"） | `WE8` |
| **BC117** | **"Why not another Biome?"**：必须能回答"为什么不是其它类别"——指出**每个未命中分支**的失败条件，并说明**有序表中更早分支为何未命中** | `WL`/`WE`（可解释的"没有"）；`§5.3` 顺序语义 |

### 10.2 硬性

| # | 规则 |
|---|---|
| **BC118** | 解释必须按**因果方向**组织：`原因 → 条件 → 结论`；**禁止**"因为是森林，所以湿润"（`WE2`/`WE3`） |
| **BC119** | 解释必须**确定性**且**只读**：同一查询点的解释内容与顺序恒定；解释不得改变任何求值结果（`WE4`/`WE5`） |
| **BC120** | 解释的粒度**到层与条件为止**：不含材料、资源、生物、对象细节（`WE7`；`BC50`–`BC55`） |
| **BC121** | 解释必须与**生成结论逐项一致**（`WA17`）：不得为解释另算一套阈值（`WA20`/`WQ1`/`WQ2`） |

---

## §11 Consumer Permission Matrix

**图例**：`O` = 拥有/产出；`R` = 只读；`R\*` = **仅解释上下文**只读（不得作为判定输入）；`—` = 禁止；`✗` = 明确无该能力（且不允许新增）。

| Biome 输出 ＼ 消费者 | WorldState（规则表） | Geology（P3） | Ecology | Resource | WorldQuery | Voxel（真值层） | Runtime | Entity（未来） |
|---|---|---|---|---|---|---|---|---|
| **Biome Category** | `O`（规则表所有者） | `R`（**仅 P3** 地表材料） | `R`（环境上下文） | `R*`（**仅解释**；判定禁用，`BC92`） | `R`（解释） | `✗`（不读；材料由 Geology 决定） | `R`（只读） | `R`（上下文） |
| **Biome Profile** | `—` | `R`（仅 P3，如需要） | `R` | `R*`（仅解释） | `R` | `✗` | `R` | `R` |
| **Biome Explanation data** | `—` | `—` | `—` | `—` | `R`（解释输出） | `✗` | `R`（调试） | `—` |

**矩阵自查**

| # | 自查项 | 期望 |
|---|---|---|
| **BC122** | 是否存在**任何列**（消费者）写出 Biome 输出？ | **必须为 0**（只读矩阵；`BC57`） |
| **BC123** | `Resource` 列是否出现非 `R*` 的读权限？ | **必须为 0**（判定禁用，`BC92`） |
| **BC124** | `Voxel` 列是否出现任何读权限？ | **必须为 `✗`**（Biome 不参与体素；材料由 Geology 的 P3 决定） |
| **BC125** | `Runtime` 列是否出现写权限？ | **必须为 0**；且 Runtime 状态**不得**影响 Biome（`BC22`/`BC106`） |
| **BC126** | `WorldState` 列是否出现"读 Biome"？ | **必须为 0**（`WSC29`：Base 不得读取 Biome 结论） |
| **BC127** | `Entity` 列是否出现写权限？ | **必须为 0**（`EF1`/`PT12`） |

---

## §12 V1 Scope

### 12.1 V1 只定义

| # | 只定义 |
|---|---|
| 1 | **Biome 边界**（§1：责任/非责任/六类关系/禁止修改清单） |
| 2 | **输入契约**（§2：白名单、地理量允许范围、黑名单、封闭性） |
| 3 | **分类契约**（§3：确定性函数、有序规则语义、禁止来源、域） |
| 4 | **身份契约**（§4：Category/Profile/Explanation；不得含材料/体素/资源/生物清单） |
| 5 | **规则归属**（§5：Biome 拥有分类规则，不拥有参数生成） |
| 6 | **与四类消费者的边界**（§6 Region / §7 Ecology / §8 Resource / §11 矩阵） |
| 7 | **确定性与局部求值**（§9：单点、禁止 biome map、透明缓存） |
| 8 | **解释契约**（§10：五要素 + "为什么不是别的"） |
| 9 | **验收规则**（§13） |

### 12.2 V1 **不定义**（硬性）

| # | 不定义 | 归属 |
|---|---|---|
| **BC128** | **具体群系清单**（类别名称集合） | `WorldState_Design.md` §5.3/§5.4；`Pipeline` §7.2（含 `PM3` 对 `Desert` 的登记） |
| **BC129** | **阈值**（任何数值边界） | `WorldState_Design.md` §5.3 + 实现规范（`B2` 要求显式） |
| **BC130** | **噪声公式 / 映射函数 / 衰减形状** | 实现规范（`D5`：自实现整数哈希；`RBD3` 形状纪律） |
| **BC131** | **植被（vegetation）** | 未来 Entity / Vegetation 系统（`WorldGeneration_Geology.md` §7 `VegetationRule` 只是条件声明） |
| **BC132** | **生物（creatures）** | `Ecology_Design.md`（生态候选） |
| **BC133** | **材料（materials）** | Geology 的 P3（地表材料 `{Grass, Dirt, Stone}`） |

### 12.3 硬性禁令

| # | 禁令 |
|---|---|
| **BC134** | 不创建任何代码结构（class/interface/struct）；不实现 Biome 逻辑 |
| **BC135** | 不修改任何已有规范文件与任何 `Assets/**` 文件 |
| **BC136** | 不新增参数（不得为群系引入"群系专用参数"；`BC70`–`BC74`） |
| **BC137** | 不新增 `VoxelType`；不改变体素状态集合 |
| **BC138** | 不新增因果边、不改变因果方向（`BC82`/`BC91`） |
| **BC139** | 不允许任何消费者写 Biome 输出（`BC122`） |
| **BC140** | 不允许 Biome 读取下游（资源/生态/体素/Runtime/Entity；§2.2） |
| **BC141** | 不允许按 Chunk 组织或缓存 Biome（`BC105`/`BC107`） |

---

## §13 Acceptance Rules

### 13.1 确定性

| # | 不变量 | 验证方法 |
|---|---|---|
| **BCV1** | 同 `(Seed, Position, WorldState)` ⇒ 递归结论一致（Category/Profile/Explanation 逐字段） | 重复求值 N 次比较 |
| **BCV2** | 不同 `Seed`（或不同参数）⇒ 至少一处结论不同（**反例保护**） | 双世界差异断言（`BC110`） |
| **BCV3** | 与查询顺序 / 遍历顺序 / 加载顺序 / 帧序无关 | 乱序对照 + 多顺序对照（`BC43`/`BC44`/`BC111`） |
| **BCV4** | 无非确定性来源（`Random()` / `Time` / `DateTime` / `Guid` / 玩家状态） | 结构扫描（`BC41`–`BC46`） |

### 13.2 输入封闭与无新增

| # | 不变量 | 验证方法 |
|---|---|---|
| **BCV5** | **输入封闭**：Biome 求值路径只出现 §2.1 白名单量 | 结构扫描（输入符号白名单比对；`BC30`） |
| **BCV6** | **无越权地理读取**：不出现 `Slope` / `Continent` | 结构扫描（`PW4` 校验） |
| **BCV7** | **无下游读取**：不出现资源/生态/体素/Runtime/Entity/`RegionType` | 结构扫描（黑名单命中 = 0；`BC20`–`BC29`） |
| **BCV8** | **无新增参数**：实现中不出现未登记的"群系参数" | 参数集合比对（`BC136`） |
| **BCV9** | **无新增 `VoxelType`** | 结构性检查（`BC137`） |

### 13.3 无新增权威 / 无反向依赖

| # | 不变量 | 验证方法 |
|---|---|---|
| **BCV10** | **无新增权威**：分类规则语义权威仍是 `WorldState_Design.md` §5.3；Biome 只是执行者 | 规则集合比对（无第二套阈值/顺序） |
| **BCV11** | **无反向依赖**：不存在 `Biome → 参数/Region/Modifier/Geology/资源/生态/Runtime` 的写路径或输入路径 | 结构扫描（反向边 = 0；`BC12`–`BC19`/`BC82`） |
| **BCV12** | **无 RegionType 直连**：`RegionType → Biome` 路径 = 0 | 结构扫描（`BC79`） |
| **BCV13** | **无 Biome → Resource**：资源判定输入不含 Biome | 结构扫描（`BC92`；`RX-1`） |

### 13.4 无 Runtime / Resource / Ecology / Voxel 所有权

| # | 不变量 | 验证方法 |
|---|---|---|
| **BCV14** | **无 Runtime 污染**：Biome 不依赖 Runtime state；Runtime 结果不影响 Biome；Biome 无跨帧状态 | 结构扫描 + 前后对照（`BC22`/`BC108`/`BC125`） |
| **BCV15** | **无 Resource 所有权**：Biome 不产出/不修改 `ResourcePresence` | 结构扫描（`BC91`/`BC94`） |
| **BCV16** | **无 Ecology 所有权**：Biome 不产出生态候选；Ecology 不得改写 Biome | 结构扫描（`BC87`/`BC89`） |
| **BCV17** | **无 Voxel 所有权**：Biome 不写体素、不决定材料、不影响 `Density`/`CaveDensity` | 结构扫描 + 开关对照（体素差异 = 0；`BC19`） |

### 13.5 局部求值与解释

| # | 不变量 | 验证方法 |
|---|---|---|
| **BCV18** | **局部求值**：单点查询可独立完成；不产生全量群系集合、不创建 biome map、不依赖邻域 | 结构性检查（无世界级产物）+ 求值范围检查（`BC99`–`BC107`） |
| **BCV19** | **解释完整**：五要素齐备（结论/直接条件/参数值/影响层/排除层）+ 支持"为什么不是其它类别" | 解释链问答（`BC112`–`BC117`） |
| **BCV20** | **解释与生成一致**：解释所用条件与生成判定同源（无第二套阈值） | 条件集合比对（`BC121`；`WA20`） |

### 13.6 零修改与退化

| # | 不变量 | 验证方法 |
|---|---|---|
| **BCV21** | **声明性**：开启/关闭本契约 ⇒ 体素**逐体素一致**（差异 = 0） | 两态体素快照比较 |
| **BCV22** | **零修改**：既有实现与既有规范 SHA 不变；退化兼容仍成立（无 Region/无能量 ⇒ 与既有基线一致） | 文件哈希比对 + 退化对照（`§5.8`；`WS11`；`RAV12`） |

---

## 附录 A：数据流图

```
             Seed ───────────────┐（域 18 = Biome，若规则需要）
                                 │
Skeleton（气候带/高度/海陆）      │
        │                        │
        v                        │
RegionModifier ──► FinalState（8 参数）◄── 只读
                        │
                        ▼
        ┌────────────────────────────────────────┐
        │  Biome（Phase 5 · 解释层 · 本契约）      │
        │  有序规则表（first-match-wins）          │
        │  Ocean 短路 → 力量分支 → 气候分支 → 默认  │
        │  输出：Category + Profile + Explanation  │
        └───────────────┬────────────────────────┘
                        │（只读）
      ┌─────────────────┼──────────────────┬─────────────────┐
      ▼                 ▼                  ▼                 ▼
  Ecology（上下文）  Geology P3（材料）  WorldQuery（解释）  Entity/Runtime（只读）
      │                 │
      ▼                 ▼
  Spawn Candidates   地表材料 {Grass, Dirt, Stone}

  Resource：判定输入 **不含 Biome**（RN7）；Biome 只出现在 Query 组装的**解释文本**中

  ═══ 禁止：Biome → 参数/Region/Modifier/Geology/资源/生态/Runtime（BC12–BC19 / BCV11）═══
```

## 附录 B：归属速查表

| 事项 | 唯一归属 | 依据 |
|---|---|---|
| 8 个参数的值（`Base + Modifier`） | WorldState 层（合成） | `WSC`/`RM5` |
| 参数修正项（6 分量） | Region 层 | `RA75` |
| 分类规则语义（有序表、阈值要求） | `WorldState_Design.md` §5.3 | `B1`–`B5` |
| **分类执行与 Biome 结论** | **Biome 层（本契约）** | `BC60`–`BC62` |
| 地表材料决定 | Geology（P3） | `WorldGeneration_Geology_Runtime_Implementation.md` §6.1 |
| 资源判定 | Resource 层 | `RN7`/§4.2 |
| 生态候选 | Ecology 层 | `EA3` |
| 群系清单与阈值数值 | `WorldState_Design.md` + 实现规范 | `BC128`/`BC129` |
| 解释文本组装 | WorldQuery | `WE1`–`WE8` |

## 附录 C：禁止清单

| # | 禁止项 | 反例（必须判为缺陷） | 依据 |
|---|---|---|---|
| **BCX-1** | Biome 写参数 | Biome 调整 `MagicDensity` | `BC12`；`PT9` |
| **BCX-2** | Biome 写 `RegionModifier` | Biome 增强某 Region 的修正 | `BC13` |
| **BCX-3** | Biome 写地形/体素 | Biome 放置方块、改材料、改洞穴 | `BC15`/`BC19` |
| **BCX-4** | Biome 写资源 | Biome 产出晶体/矿脉 | `BC91`/`BC94` |
| **BCX-5** | Biome 写 Runtime/Entity | Biome 触发加载、生成对象 | `BC17`/`BC126` |
| **BCX-6** | Biome 读资源/生态结果 | 由"已有生态"决定 Biome | `BC20`/`BC21` |
| **BCX-7** | Biome 读 Runtime / 玩家 / Chunk 状态 | 由加载状态决定 Biome | `BC22`–`BC24` |
| **BCX-8** | Biome 读体素/材料 | 由已生成材料反推 Biome | `BC26` |
| **BCX-9** | `RegionType → Biome` | 用 `MagicForest` 标签直接映射类别 | `BC79`；`RA88` |
| **BCX-10** | 读 `RegionModifier` 原始分量 | 绕过 `FinalState` 直接读修正量 | `BC80` |
| **BCX-11** | **Biome → 资源判定** | "因为是魔法森林，所以生成魔晶" | `BC92`/`BC97`；`RN7`/`RX-1` |
| **BCX-12** | Biome 声明材料/资源/生物清单 | Identity 内含 `materials[]`/`creatures[]` | `BC50`–`BC53` |
| **BCX-13** | 生成/存储全量群系或 biome map | 预生成世界群系栅格并作为权威 | `BC103`–`BC105` |
| **BCX-14** | 依赖邻域 Chunk | "等邻居算完再定 Biome" | `BC106` |
| **BCX-15** | 第二套阈值/噪声 | Biome 自带一套温度计算 | `BC75`；`WQ2` |
| **BCX-16** | 由结果反推原因 | "因为是森林，所以这里湿润" | `BC118`；`WE3` |

## 附录 D：与既有规范的对齐登记与变更规则

### D.1 登记项（**本文档不修改任何既有文件**）

| # | 需对齐内容 | 位置 | 真相（按本契约） |
|---|---|---|---|
| ① | `BiomeProfile` 含 `SurfaceMaterial` 与 `TerrainModifier` | `WorldGeneration_Geology.md` §7（`:302`–`:307`） | 该字段属**地质侧接口**（接口形状归 Geology），**不是** Biome Identity 的组成部分（`BC50`）；V1 仅启用"Geology 在 **P3** 读取 Biome 结论后**自行**决定地表材料"；`TerrainModifier` 在 V1 **不启用**（`..._Runtime_Implementation.md` §6.2：不改变密度分布） |
| ② | 群系清单/阈值 | `WorldState_Design.md` §5.3/§5.4；`Pipeline` §7.2 + `PM3` | 清单与阈值**不属本契约**（`BC128`/`BC129`）；`Desert` 等分支必须在 `WorldState_Design.md` 规则表内定义（`PM3` 已登记） |
| ③ | "Biome context 可影响资源解释"（任务书 §8 的表述） | 本契约 §8.2；`Resource_Design.md` `RN7`；`WorldParameter_Contract.md` §4 矩阵（Resource 列 = `—`） | **判定路径禁止**（`BC92`）；"context"仅允许出现在 **WorldQuery 组装的解释文本**中（`BC96`），且必须显式给出真正的判定依据 |
| ④ | Ecology 输入集合（审查 **P0-5**） | `Ecology_Design.md` `EA2`；`WorldParameter_Contract.md` §8.3 ① | 以**参数白名单**为准；Biome 是其中的**上下文之一**（`BC90`） |
| ⑤ | 占位群系与量纲迁移（审查 **P1-8**） | `IBiomeProvider.cs:22-31`（整数域 `[-500,600]` / `[0,1000]`）；`GeologySample.cs:169`（`BiomeA`/`BiomeB`） | 按 `WorldParameter_Contract.md` §5.3 的**边界映射**迁移；**不修改代码**；替换占位属审查 `AD-3` 决策 |
| ⑥ | Biome 层在最小路径中的位置 | `WorldGeneration_Architecture_Audit.md` `M3`（`M1`→`M2`→`M3`） | 本契约是 M3 的**分类与权限依据**；M3 必须在 M1（WorldState）、M2（Region）之后执行 |

### D.2 变更规则

1. 修改 Biome 的输入白名单、身份字段、权限矩阵或解释要素，**必须**升版本并同步更新 §2/§4/§10/§11 与附录 B。
2. **不得放宽**：`BC12`–`BC19`（禁止修改清单）、`BC30`（输入封闭）、`BC79`（禁止 `RegionType → Biome`）、`BC92`（禁止 Biome → 资源判定）、`BC103`–`BC107`（禁止全量/地图/邻域依赖）、`BC122`–`BC127`（矩阵自查）。
3. 与既有规范冲突时，先按 D.1 **登记**，再由对应规范的**版本变更**解决；本文档不得直接改写其它规范。
4. 任何"让 Biome 拥有更多内容"的提案（材料、资源、生物、地形修正）必须**独立成规范**，并证明：确定性不受影响、既有验收全部 PASS、且**可关闭**。
5. 任何"群系参数化配置"提案必须登记到 `WorldParameter_Contract.md` 的配置参数集合（不得在本契约内新增参数，`BC136`）。
