# World Architecture Cross Document Audit V1

> **文档性质**：**只读审查报告**。本文档**只记录事实与冲突**：不修改任何已有 `.md`、不写代码、**不新增参数**、**不新增系统**、**不裁决未定义设计**。
> **审查时间**：2026-10-14
> **审查方式**：逐文件读取正文 + 关键条款定位（`文件:行号`）+ 与既有实现事实对照（引用 `WorldGeneration_Architecture_Audit.md` 的代码核验结论）。
> **证据等级**：
>
> | 标记 | 含义 |
> |---|---|
> | **[原文]** | 已在规范正文中核对到（给出文件名与行号） |
> | **[代码]** | 已在既有源码中核对到（引用上一轮审计的核验结论） |
> | **[推论]** | 由上述事实推导，未经直接验证 |
> | **[待确认]** | 无法在只读条件下确认（需下一步决策或裁决） |
>
> **本轮不做的事**：不裁决未定义设计、不补设计、不修改任何文件、不新增系统或参数、不提出新的因果边。
> 所有"未来要做的文档/决策"一律放入 **§10 Future Design Items**，不在此处展开。

---

## 1. Scope

### 1.1 审查对象（24 份，基线冻结）

| 文件 | 行数 | SHA16 |
|---|---|---|
| `WorldState_Design.md` | 853 | `D54CB1CC14859312` |
| `WorldParameter_Contract.md` | 652 | `532F9596F1348B57` |
| `WorldParameter_Runtime_Model.md` | 411 | `B8ADCE96CA1AA773` |
| `WorldGeneration_Pipeline_Design.md`（旧） | 782 | `A11C4E9373B0B9C8` |
| `WorldGeneration_Pipeline_Contract.md`（新） | 542 | `BEF7CFC1AF98AF3C` |
| `Region_Design.md` | 823 | `77FA71B2620E7396` |
| `Region_Architecture_Contract.md` | 668 | `90361CAB116697C1` |
| `Biome_Architecture_Contract.md` | 622 | `DB7F756169BA8D6C` |
| `Geology_Architecture_Contract.md` | 551 | `9E204997AE8F6D4A` |
| `Resource_Design.md` | 692 | `F857DFF93D6F24AA` |
| `Resource_Architecture_Contract.md` | 442 | `38FC968F9B208656` |
| `Ecology_Design.md` | 587 | `662C305CB8A2904B` |
| `WorldQuery_Design.md` | 573 | `6D801C60F3C6D42F` |
| `WorldQuery_Architecture_Contract.md` | 421 | `6635ED5334F9FD5C` |
| `WorldState_Runtime_Contract.md` | 557 | `04B164617833A832` |
| `WorldHistory_Design.md` | 627 | `65296B3C9CCE6451` |
| `VoxelWorld_Core.md` | 226 | `F718B68E95100C57` |
| `VoxelWorld_Mesh.md` | 258 | `81CEDBFA6E9756CE` |
| `VoxelWorld_Runtime.md` | 452 | `EDD02AC50FC03E5E` |
| `VoxelWorld_Runtime_Core.md` | 651 | `10F1EC7F10DAD8F3` |
| `VoxelWorld_Mining.md` | 240 | `8A2B020DDB97422B` |
| `WorldGeneration_Core.md` | 736 | `684753D5E6B31565` |
| `WorldGeneration_Geology.md` | 603 | `1A7DD7E7AE1EBAC9` |
| `WorldGeneration_Architecture_Audit.md` | 514 | `7633A659AF410BBB` |
| `ArchitectureDecisionWorldSpaceEntity.txt`（ADR-001） | 65 | `218D1CAAA738C59D` |

### 1.2 审查维度

| # | 维度 | 对应章节 |
|---|---|---|
| 1 | 生产权（谁唯一产出） | §2 |
| 2 | 依赖方向（R / W / X / 未定义） | §3 |
| 3 | 真值所有权（Terrain / Parameter / Resource / Runtime Modification） | §4 |
| 4 | 命名冲突（关键词 + 编号前缀 + 图例符号） | §5 |
| 5 | 阶段一致性（Phase 编号） | §6 |
| 6 | 风险分级（Blocker / Warning / Future Design） | §8–§10 |

---

## 2. Producer Ownership Matrix

> 目标：确认每一项**只有一个生产者**；发现"两个系统都认为自己拥有生产权"必须登记。

| 数据/概念 | 唯一生产者（依据） | 其他系统权限（依据） | 结论 |
|---|---|---|---|
| **Seed** | **外部显式输入**（不属任何层）：`WorldGeneration_Pipeline_Contract.md` Phase 0（`:66`）产出 Seed Context；`WorldParameter_Contract.md` §6.4 用它做域分离 | 全体只读（`PC11` 不可变） | **⚠️ 缺口**：Seed 被登记为"配置参数"（`WorldParameter_Contract.md` §2.5），该表**无 Owner 列** → 见 `B-2` |
| **World Skeleton** | **Phase 1 骨架**：`WorldGeneration_Pipeline_Contract.md` Phase 1（`:84`）；规则权威 `WorldState_Design.md` §7.4 `WC1`–`WC6`（`:639`） | 下游只读（`PC15`"只被下游读取"） | **一致**（单一生产者） |
| **World Parameter Base** | **WorldState 层**：`WorldParameter_Runtime_Model.md` §7.1/`PM110`（`BaseValue` 列唯一 `O` = WorldState） | Region 只读（用于产修正）；WorldQuery 只读（解释） | **一致** |
| **Region Modifier** | **Region 层（唯一写入者）**：`Region_Architecture_Contract.md` `RA75`（`:308`） | WorldState 只读（仅合成，`RA76`）；WorldQuery 只读 | **一致** |
| **Final Parameter State** | **WorldState 层（合成）**：`RA76`；`WorldParameter_Runtime_Model.md` `PM29`/`PM110` | Biome/Ecology/Resource/WorldQuery/Runtime 只读 | **一致** |
| **Biome Identity** | **Biome 层**：`Biome_Architecture_Contract.md` §5.1 `BC60`–`BC62`；§11 矩阵（`O` 仅 Biome 列） | Geology 只读（仅 P3）；Ecology 只读；Resource **`R*` 仅解释** | **一致** |
| **Geology Result**（`GeologyState`） | **Geology 层**：`Geology_Architecture_Contract.md` §10 矩阵（`O` = Geology）；`GC139`–`GC146` | Resource 只读（介质/矿藏）；Ecology 只读（地形庇护）；Voxel 抽样只读 | **一致** |
| **DepositPotential**（地质矿藏基础层） | **Geology 层**：`GC98`（`:275`）、`GC102`；`WorldParameter_Contract.md` §3.5 表 D（`:251`） | Resource 只读消费（`RU2`/`RC49`）；Ecology 仅资格/归因（`ER2`） | **一致** |
| **ResourcePresence**（资源判定层） | **Resource 层**：`Resource_Architecture_Contract.md` `RC31`/`RC122`；`WorldParameter_Contract.md` §3.5（`:257`） | Geology **不得产出**（`GC99`）；WorldQuery 只读；Runtime 只读展示 | **一致** |
| **Ecology Result**（`SpawnCandidate`） | **Ecology 层**：`Ecology_Design.md` `EA3`（`:94`）；`WorldParameter_Contract.md` §4 矩阵 | Resource **不得读**（`RC79`）；Entity 只读（未来系统） | **一致** |
| **Voxel Density**（术语歧义） | **歧义**：① 地质密度判定 → Geology（`GC51`–`GC53`）；② 体素化写入 → Phase 6 `Voxel Evaluation`（`PC35`）；③ 旧栈 `DensityField` → 旧 Terrain 栈（[代码]，见 `WorldGeneration_Architecture_Audit.md`:88/108） | — | **⚠️ 命名冲突 + 边界缺口**：见 `B-4`、`A-2` |
| **Mesh** | **Mesh 层（只读派生）**：`VoxelWorld_Mesh.md`（`:18`"Mesh 不是世界真值"）；`WorldGeneration_Pipeline_Contract.md` Phase 7（`:192`） | Geology **不得输出 Mesh**（`GC46`）；Generation 不得被 Mesh 反向影响（`PC57`） | **一致**（无第二声称者） |
| **Runtime Modification** | **未定义 Owner**：`VoxelWorld_Mining.md` 定义编辑语义（`:123`/`:212`）；`VoxelWorld_Runtime_Core.md` §5.2 定义"安装"路径（`:230`）；`WorldParameter_Runtime_Model.md` §6.1 把 `Modification` 列入 Runtime Lifetime（`PM66`） | — | **⚠️ Missing Boundary**：见 `B-1` |

### 2.1 双生产权检查结果

| 检查 | 结果 |
|---|---|
| 是否存在"两个系统都宣称唯一生产权"（同一数据/概念）？ | **[原文] 未发现硬性双重所有权**：12 项中 11 项的"唯一生产者"在文档中明确且与权限矩阵一致 |
| 存在的**歧义/缺口** | ① `Voxel Density` 术语三义（`B-4`）；② `Seed` 与配置参数无 Owner 登记（`B-2`）；③ `Runtime Modification` 无 Owner（`B-1`） |
| 角色区分是否一致 | `WorldParameter_Contract.md` §4.1（`:285`）明确区分 **Producer / Owner / Consumer**；后续各 Contract 的矩阵统一使用 `O`（产出/拥有）。**符号语义差异**见 `B-5` |

---

## 3. Dependency Matrix

### 3.1 图例

| 符号 | 含义 |
|---|---|
| `R` | 只读（读上游/同层输入） |
| `W` | 写入（仅允许的写入者） |
| `X` | 禁止 |
| `U` | **未被任何文档定义**（登记为缺口，见 `B-6`） |
| `—` | 自身/不适用 |

> 行 = 行为主体；列 = 被读/被写的对象。`WorldState` 与 `Parameter` 是同一层的两个视角（参数面 / 层），二者之间记为 `—`。

### 3.2 矩阵（System × System）

| 主体 ＼ 对象 | WorldState | Parameter | Region | Biome | Geology | Resource | Ecology | Voxel | Mesh | Runtime | WorldQuery |
|---|---|---|---|---|---|---|---|---|---|---|---|
| **WorldState** | — | **W**（合成 `FinalValue`） | R（读 `RegionModifier`） | X | X | X | X | X | X | X | X |
| **Parameter** | — | — | R（读 `BaseValue`） | R（仅 `FinalValue`） | X | R（环境参数） | R（白名单） | X | X | R（只读引用） | R（仅解释） |
| **Region** | X | **W**（`RegionModifier` 唯一写入者） | — | X | X | X | X | X | X | X | X |
| **Biome** | X | X | X | — | R（仅 P3） | X | R（上下文） | X | X | X | X |
| **Geology** | X | X | X | X | — | R（`DepositPotential`） | R（地形庇护） | R（抽样输入） | X | X | X |
| **Resource** | X | X | X | X | X（不得回写） | — | X（互不读取） | X | U | U | R（解释） |
| **Ecology** | X | X | X | X | R（仅庇护条件） | X（互不读取） | — | X | U | U | R（解释） |
| **Voxel** | X | X | X | X | X（`PC56`） | X | X | — | R（派生输入） | R | X |
| **Mesh** | X | X | X | X | X | U | U | X（`PC40`） | — | R（呈递） | X |
| **Runtime** | X | X | X | X | X | U | U | **W/X 冲突**（见 `A-3`） | W（构建/呈递表示） | — | R（展示） |
| **WorldQuery** | X | X | X | X | X | X | X | X | X | X（不得写） | — |

> **WorldQuery 行**：全部为**读 `R`**（白名单：`WQ13`–`WQ21`）+ **写 `X`**（`WQ3`/`PC104`）；为可读性，上表在其行内只标注"写"的判定（`X`），其**读**权限见 `WorldQuery_Architecture_Contract.md` §2.1/§8。

### 3.3 三类关键禁止的检查结果

| 检查 | 结果 | 依据 |
|---|---|---|
| **下游反向读取**（例：`Resource → Geology`） | **[原文] 文档层禁止且无反向边**：`RC50`、`PC60`（`:284`）、`GC104`、`WSC56` | `Resource_Architecture_Contract.md` §4；`WorldGeneration_Pipeline_Contract.md` §3.2 |
| **Runtime 反向污染**（例：`Runtime → Biome`） | **[原文] 对参数/区域/群系/地质均禁止**（`PC53`/`PC54`/`WSC61`/`PX2`）；**但发现 1 例冲突**：`Runtime → Voxel`（安装）→ `A-3` | `VoxelWorld_Runtime_Core.md` §5.2（`:230`–`:256`）vs `WorldGeneration_Pipeline_Contract.md` `PC35`/`PC43`（`:174`/`:210`） |
| **查询污染生成**（例：`WorldQuery → Parameter`） | **[原文] 禁止**：`WQ4`（`WorldQuery_Design.md`:68）、`PC104`（`:363`）、`WQ38`；`WorldQuery` 对全部层零写入（`WQ3`） | — |

### 3.4 未定义（`U`）单元清单

| # | 单元 | 状态 |
|---|---|---|
| 1 | `Resource → Mesh` | 未定义（无文档化边） |
| 2 | `Resource → Runtime` | 未定义 |
| 3 | `Ecology → Mesh` | 未定义 |
| 4 | `Ecology → Runtime` | 未定义 |
| 5 | `Mesh → Resource` | 未定义 |
| 6 | `Mesh → Ecology` | 未定义 |

> 说明：这些是**横向（非同一主链）系统之间**的边；现有禁令主要覆盖"同一链的下游 → 上游"（`PC60`）与各层的输入白名单。缺显式禁令 → 见 `B-6`（**不补设计**，仅登记）。

---

## 4. Truth Ownership Audit

### 4.1 Terrain（Density Field / Voxel State / Mesh）

| 概念 | 声称所有者 | 依据 | 结论 |
|---|---|---|---|
| **Density Field（判定）** | Geology（`DensityTendency`，P0–P2） | `Geology_Architecture_Contract.md` §3.1 `GC32`；`GC51`/`GC52` | **文档层一致** |
| **Voxel State（体素真值）** | 世界侧（`VoxelWorld`/`VoxelChunk`） | `VoxelWorld_Core.md` §1（`:11`"Voxel Data 是世界唯一真值"）；`GC51` | **文档层一致** |
| **Mesh（派生表示）** | Mesh 层（只读派生） | `VoxelWorld_Mesh.md`（`:18`"Mesh 不是世界真值"）；`PC39` | **文档层一致**（无文档把 Mesh 当作世界事实） |

| 检查 | 结果 |
|---|---|
| 三者关系是否清晰？ | **[原文] 清晰**：`判定 → 抽样为体素真值 → 派生 Mesh`（`GC52`/`GC53`；`VoxelWorld_Mesh.md`:11），且 `GC146` 冻结"体素化注入点唯一" |
| 是否有人把 Mesh 当作世界事实？ | **[原文] 没有**：`VoxelWorld_Mesh.md`:18、`PC39`/`PC40`、`GC46`（Geology 不得输出 Mesh）三处明确禁止 |
| **项目级**是否只有一个 Terrain 真值？ | **[代码] 否**：运行时世界为旧栈 `DensityTerrain`（[代码] `WorldGeneration_Architecture_Audit.md`:88/103），新栈 `VoxelWorld` 未接入场景 → **双真值共存** → `A-2` |

### 4.2 Parameter（BaseValue / Modifier / FinalValue）

| 检查 | 结果 | 依据 |
|---|---|---|
| 是否存在多个存储位置？ | **[原文] 文档层声明为单一真值**：`WorldState_Runtime_Contract.md` `WSC68`（`:315`"参数只有一份真值"）、`WorldParameter_Runtime_Model.md` `PM89`/`PM90`（禁止旧版本、禁止不同系统拥有不同 `ParameterState`） | `WorldState_Runtime_Contract.md` §8；`WorldParameter_Runtime_Model.md` §8 |
| 三态分工是否清晰？ | **[原文] 清晰**：`BaseValue`（WorldState 求值）· `Modifier`（Region 唯一写入）· `FinalValue`（WorldState 合成） | `PM23`/`PM27`–`PM29`；`RA75`/`RA76` |
| **物理存储位置**（谁持有数组/表）是否定义？ | **[原文] 未定义**：`WorldParameter_Runtime_Model.md` §4 只冻结"存储模型选择（枚举索引为权威模型）"与"禁止哈希容器作为唯一基础"，未指定持有者与布局（`PM47` 明确存储优化不属 V1） | → `B-3` |

### 4.3 Resource（DepositPotential / ResourcePresence / Voxel Material）

| 关系 | 声明 | 结论 |
|---|---|---|
| `DepositPotential` ↔ `ResourcePresence` | **两层**：地质基础层（Geology）∧ 条件（Resource 判定） | **[原文] 清晰**：`GC97`（`:274`）、`RC7`/`RC56`；术语冻结见 `WorldParameter_Contract.md` §3.5（`:256`/`:257`） |
| `ResourcePresence` ↔ **Voxel Material** | **无关系**：V1 不存在资源体素类型（矿物体素 = Out of Scope） | **[原文] 清晰**：`GC105`（不得新增资源体素类型）、`RC58`；`Resource_Design.md` §6.1 规则 1（矿藏是属性层） |
| 是否有人把资源判定写进体素？ | 禁止 | `GC100`/`GC105`、`RC45`–`RC48`（禁止输出 Voxel/Mining Result） |

### 4.4 Runtime Modification（重点：**Missing Boundary**）

| 事实 | 依据 |
|---|---|
| `Modification` 被列入 **Runtime Lifetime**（`Entity` / `Player` / `Modification`） | `WorldParameter_Runtime_Model.md` §6.1 `PM66`（`:170`） |
| 挖掘/编辑的**语义**由 Mining 规范定义（未生成区域不创建 Chunk、不置脏） | `VoxelWorld_Mining.md`（`:123`/`:212`） |
| **安装**（生成结果 → 世界真值）被定义为 **Runtime Core 的唯一合法路径**，且**逐体素搬运**写入 `VoxelChunk.SetVoxel` | `VoxelWorld_Runtime_Core.md` §5.2（`:230`–`:256`） |
| **编辑层/改动集的存储归谁、跨帧如何存活、与 Runtime Lifetime 的关系** | **未在任何文档中定义** → **Missing Boundary**（登记为 `B-1`，**不补设计**） |

---

## 5. Naming Collision Audit

### 5.1 编号前缀冲突（ID 命名空间）

| 前缀 | 冲突双方 | 证据 | 风险 |
|---|---|---|---|
| **PC** | `WorldGeneration_Pipeline_Design.md` `PC1`–`PC5`（Phase 1 骨架）**vs** `WorldGeneration_Pipeline_Contract.md` `PC1`–`PC120`（调度契约）**vs** `WorldParameter_Contract.md` `PC1`–`PC18`（参数验收） | Pipeline_Design `:170`–`:174`；Pipeline_Contract `:48`；Parameter_Contract `:564` | **三文档同号不同义** → 引用 `PC1` 无法唯一确定 |
| **PM** | `WorldGeneration_Pipeline_Design.md` `PM1`–`PM6`（群系解析规则）**vs** `WorldParameter_Runtime_Model.md` `PM1`–`PM120` | Pipeline_Design `:446`–`:451`；Runtime_Model `:39` | 同号不同义 |
| **WQ** | `WorldQuery_Design.md` `WQ1`–`WQ6`（查询语义）**vs** `WorldQuery_Architecture_Contract.md` `WQ1`–`WQ120` | Design `:65`–`:70`；Architecture `:54` 起 | 已在 Architecture 附录 D ⑥ 登记；仍需引用时带文档名 |
| **RC** | `Region_Design.md` `RC1`–`RC5`（反向因果禁令）**vs** `Resource_Architecture_Contract.md` `RC1`–`RC150` | Region_Design `:257`–`:260`；Resource_Architecture `:49` 起 | 已在 Resource 附录 D ① 登记 |
| **RB / RB-** | `Region_Design.md` `RB1`–`RB6`（Biome 关系）**vs** `WorldGeneration_Pipeline_Design.md` `RB-1`–`RB-14`（反向依赖清单） | Region_Design §5.3；Pipeline_Design 附录 C | 形近（有无连字符）→ 易混 |
| **RA** | `Region_Architecture_Contract.md` `RA1`–`RA152` | 无第二使用者（已核验） | 无冲突 |

### 5.2 关键词冲突（同名不同义）

| 关键词 | 含义数 | 各含义与出处 | 风险 |
|---|---|---|---|
| **Modifier** | **3+** | ① `RegionModifier`（值，6 分量）：`Region_Architecture_Contract.md` §5.1；② `ModifierProfile`（区域侧写）：同文档 §2.2（`:117`）；③ `TerrainModifier`（地质侧接口字段，V1 未启用）：`WorldGeneration_Geology.md`:307；④ 代码 `GeologyDensityModifier`（枚举，[代码]） | **高**（"修正"与"侧写"易混） |
| **Rule** | **4** | ① `Rule Definition`（资源规则定义行）：`Resource_Architecture_Contract.md` §9；② `EcologyRules`（区域级资格声明）：`Region_Design.md`:328；③ 群系规则表：`WorldState_Design.md` §5.3；④ `GeologySample.Rule`（判定来源枚举，[代码]） | **中高** |
| **Presence** | **2** | ① `ResourcePresence`（资源判定产物）：`Resource_Design.md` §1.3；② `CivilizationPresence`（历史参数，逐文明标记）：`WorldParameter_Contract.md`:137 | **中**（一个是产物、一个是参数） |
| **State** | **4+** | ① `WorldParameterState`（参数三态）：`WorldParameter_Runtime_Model.md` §3；② `Runtime State`（运行时状态）：`WorldParameter_Contract.md` §7；③ `GeologyState`（地质解释结果）：`Geology_Architecture_Contract.md` §3；④ `Voxel State`（体素真值）：`VoxelWorld_Core.md`；⑤ `ChunkLifecycleState`（[代码]） | **中高** |
| **Context** | **5** | ① `Region Context`（区域上下文）：`WorldGeneration_Pipeline_Contract.md` Phase 3（`:120`）；② `Biome Context`（解释上下文，**禁止作判定**）：`Resource_Architecture_Contract.md` `RC18`/`RC65`；③ `Epoch/History Context`：同文档 `RC19`；④ `Runtime Context`（**仅展示**）：`WorldQuery_Architecture_Contract.md` `WQ21`；⑤ `Seed Context`：Pipeline_Contract Phase 0（`:66`） | **中高**（"解释上下文"与"展示上下文"语义不同） |
| **Result** | **5** | ① `Query Result`（解释产物，含 `Current Result` 字段）：`WorldQuery_Architecture_Contract.md` §3；② `Voxel Query Result`（Phase 6 输出）：`WorldGeneration_Pipeline_Contract.md` Phase 6（`:174`）；③ `Geology Result`/`GeologyState`：`Geology_Architecture_Contract.md` §3；④ `Ecology Result`/`SpawnCandidate`：`Ecology_Design.md` `EA3`；⑤ `ResourcePresence`（资源判定"结果"） | **中** |
| **Source** | **2** | ① `RegionSource`/`SourceKind`（因果来源）：`Region_Design.md` §3；② 参数"来源"列（Owner/Producer）：`WorldParameter_Contract.md` §3 表 A | **中** |
| **Generation** | **4** | ① `Generation Phase`（阶段）：Pipeline_Contract §2；② `Generation Parameter`（参数类别）：`WorldParameter_Contract.md` §7；③ `Generation Lifetime`（生命周期）：`WorldParameter_Runtime_Model.md` §6.1；④ `Generation State`（与 Runtime State 相对）：`WorldState_Runtime_Contract.md` §6 | **中**（复合词含义不同） |
| **Value** | **3** | ① `BaseValue`/`FinalValue`（参数三态）：Runtime_Model §3；② "参数值"（通用）；③ 诊断/报告数值（[代码]） | **低** |
| **Condition** | **3** | ① `Direct Conditions`（解释字段）：Query §3；② `Supporting Conditions`（资源结果字段）：Resource §3；③ 判定条件（阈值语义） | **低** |
| **Identity** | **3** | ① `Biome Identity`：Biome §4；② `Parameter Identity`：Runtime_Model §2；③ `RegionKey`（区域身份）：Region §2.3 | **低**（语义一致：身份元数据） |

### 5.3 图例符号冲突

| 符号 | 语义 A | 语义 B | 证据 |
|---|---|---|---|
| **`O`** | **Owner（拥有最终解释权）**：`WorldParameter_Contract.md` §4.1 图例 | **产出/拥有（Producer 或 Owner）**：Pipeline/Region/Biome/Geology/Resource/Query 各矩阵图例 | Parameter_Contract `:285` vs Pipeline_Contract `:369` |
| **`P`** | **Producer（产生）**：仅 `WorldParameter_Contract.md` §4.1 定义 | 其它矩阵用 `O` 兼表产出 | 同上 |
| **`R*`** | **仅归因**只读（不得作判定条件）：Parameter_Contract `:285` | **仅解释**只读：Query/Pipeline 图例 | 同一符号两种限定语义 |

---

## 6. Phase Consistency Audit

### 6.1 两套编号方案（并存）

| 方案 | 来源 | 阶段序列 |
|---|---|---|
| **方案 A（旧，Phase 0–7）** | `WorldGeneration_Pipeline_Design.md` §2.1（`:131`–`:138`） | 0 Seed · 1 World Skeleton · **2 History** · 3 Region · **4 WorldState** · **5 Biome** · **6 Geology** · **7 Voxel Output** |
| **方案 B（新，Phase 0–8）** | `WorldGeneration_Pipeline_Contract.md` §2（`:66`–`:210`） | 0 Seed Initialization · 1 World Skeleton · **2 World Parameter** · 3 Region · **4 Biome** · **5 Geology** · **6 Voxel** · **7 Mesh** · **8 Runtime** |

### 6.2 各系统"自声明编号" vs 两方案

| 系统 | 方案 A | 方案 B | 系统文档自声明（依据） | 一致性 |
|---|---|---|---|---|
| Seed | 0 | 0 | — | ✔ |
| World Skeleton | 1 | 1 | `WorldState_Design.md` §7.4（框架提供者） | ✔ |
| History | 2 | **不在编号内**（作为 Phase 3 输入） | `WorldHistory_Design.md`（无 Phase 自称）；Pipeline_Contract `PC26` | ✖（编号差异，已登记） |
| WorldState / Parameter | 4 | **2**（两段式发布） | `WorldParameter_Runtime_Model.md` `PM74`（"Phase 2/3"） | ✖ |
| Region | 3 | 3 | `Region_Architecture_Contract.md`（`:33` 引 Phase 3） | ✔ |
| Biome | **5** | **4** | `Biome_Architecture_Contract.md`（`:49` 引 Pipeline_Design Phase 5；`:550` "Biome（Phase 5 …）"） | **✖ 冲突** |
| Geology | **6** | **5** | `Geology_Architecture_Contract.md` `GC2`（`:48`"位于因果链的 **Phase 6**"）；`:470`"Geology（Phase 6 …）" | **✖ 冲突** |
| Voxel | **7** | **6** | `Geology_Architecture_Contract.md` `GC50`/`GC146`（`:374`"Voxel Evaluation（Phase 7）"）；`Resource_Design.md`（`:118`"位于 Phase 6 之后"、`RN6`"不被 Phase 6/7 读取"） | **✖ 冲突** |
| Mesh | 不在管线内（派生层） | **7** | `VoxelWorld_Mesh.md`（无 Phase 自称） | ▲ 新增（无冲突，但旧文档无对应编号） |
| Runtime | 不在管线内（调度者） | **8** | `VoxelWorld_Runtime.md`（`:24`"Runtime 是调度者"） | ▲ 新增 |
| Resource / Ecology | "Phase 6 之后的旁路层" | 不在 Phase 编号内（旁路只读） | `Resource_Design.md`（`:118`）；`Ecology_Design.md`（`:116`–`:120` 用方案 A 编号） | ▲ 表述差异 |
| WorldQuery | 无 Phase（旁路） | 无 Phase（`WQ9` 明示"不在 Phase 0–8 生成链内"） | `WorldQuery_Architecture_Contract.md` `WQ9` | ▲ 表述与方案 B 对齐 |

### 6.3 关键事实

| # | 事实 | 说明 |
|---|---|---|
| 1 | **依赖顺序本身一致** | 两方案对"谁在谁之前"的**因果关系**描述相同；分歧仅在**编号**（且方案 B 额外包含 Mesh/Runtime、并把 History 折为 Phase 3 输入） |
| 2 | **系统文档多沿用方案 A 编号** | Geology（Phase 6）、Biome（Phase 5）、Resource/Ecology（"Phase 6 之后"）、WorldQuery_Design（Phase 1–7） |
| 3 | **新契约沿用方案 B 编号** | Pipeline_Contract（Phase 0–8）、Query_Architecture（"Phase 0–8"）、Parameter_Runtime_Model（Phase 2/3） |
| 4 | 后果 | 同名"Phase 5"在两套方案中分别指 **Biome**（A）与 **Geology**（B）→ 机械验收（如 `PCV3`"Phase 顺序固定"）与跨文档引用**无法唯一解析** → `A-1` |

---

## 7. Conflict Summary

| ID | 类型 | 冲突/缺口 | 严重度 | 主要证据 |
|---|---|---|---|---|
| **CD-01** | 真值写入者冲突 | 体素真值的写入者：`Runtime Core 安装`（既有规范"唯一合法路径"）vs `Phase 6 Voxel Evaluation 抽样写入` + `PC43`/`PC53`（Runtime 不得写生成产物） | **Blocker** | `VoxelWorld_Runtime_Core.md`:230–:256 vs `WorldGeneration_Pipeline_Contract.md` `PC35`/`PC43`/`PC53`（`:187`/`:223`/`:272`） |
| **CD-02** | 真值双轨（项目级） | Terrain 存在两套真值：旧 `DensityTerrain` 栈（运行时世界）与新 `VoxelWorld` 栈（未接入场景） | **Blocker** | [代码] `WorldGeneration_Architecture_Audit.md`:88/103/108/142/149（场景 GUID 核验 + `ChunkSpace` vs `VoxelChunk`） |
| **CD-03** | 阶段编号双轨 | 方案 A（0–7）vs 方案 B（0–8）；系统文档沿用 A，新契约沿用 B | **Blocker（文档级）** | `WorldGeneration_Pipeline_Design.md`:131–:138 vs `WorldGeneration_Pipeline_Contract.md`:66–:210；`Geology_Architecture_Contract.md` `GC2`/`GC50`；`Biome_Architecture_Contract.md`:550 |
| **CD-04** | 编号前缀多义 | `PC`（三义）/`PM`（双义）/`WQ`（双义）/`RC`（双义）/`RB` 与 `RB-` | **Blocker（可核验性）** | Pipeline_Design`:170`/`:446`；Pipeline_Contract`:48`；Parameter_Contract`:564`；Runtime_Model`:39`；Query_Design`:65`；Query_Architecture`:54`；Region_Design`:257`；Resource_Architecture`:49` |
| **CD-05** | Missing Boundary | `Runtime Modification` 无所有者/存储/生命周期定义 | **Warning** | `WorldParameter_Runtime_Model.md` `PM66`；`VoxelWorld_Mining.md`:123/212；`VoxelWorld_Runtime_Core.md` §5.2 |
| **CD-06** | 所有权登记缺口 | 配置参数（`Seed`/`WorldHeight`/`SeaLevel`/…）无 Owner 列 | **Warning** | `WorldParameter_Contract.md` §2.5（`:144`）；`PC11`（Seed 不可变） |
| **CD-07** | 存储边界缺口 | 参数三态的**物理存储位置**未定义（仅冻结"模型选择"，存储优化排除在 V1 外） | **Warning** | `WorldParameter_Runtime_Model.md` §4 `PM47`/`PM117` |
| **CD-08** | 术语多义 | `Modifier`(3+) / `State`(4+) / `Context`(5) / `Result`(5) / `Rule`(4) / `Presence`(2) / `Source`(2) / `Generation`(4) | **Warning** | §5.2 表（逐项出处） |
| **CD-09** | 图例符号语义差异 | `O` = Owner（参数契约）vs 产出/拥有（其余矩阵）；`R*` = 仅归因 vs 仅解释 | **Warning** | `WorldParameter_Contract.md`:285 vs `WorldGeneration_Pipeline_Contract.md`:369 |
| **CD-10** | 横向禁令缺口 | 6 个未定义（`U`）单元：`Resource↔Mesh`、`Resource→Runtime`、`Ecology↔Mesh`、`Ecology→Runtime` | **Warning** | §3.4 |
| **CD-11** | Phase 集合扩展未登记 | 新契约把 **Mesh（P7）/Runtime（P8）** 纳入管线，旧文档（Phase 0–7 终点的 Voxel Output）未登记该扩展 | **Warning** | Pipeline_Design`:138` vs Pipeline_Contract`:192`/`:210`；Pipeline_Contract 附录 D ③（已自行登记） |
| **CD-12** | 旧栈遗留文档/验证入口 | 45 个 Editor 验证入口分属两套栈；旧 `Specifications/*.md`（Chunk/Mining/VoxelAwareMesh/CoordinateMigration）与新契约族未建立关系映射 | **Warning** | [代码] `WorldGeneration_Architecture_Audit.md`:108 |

---

## 8. Blocker

> **定义**：若不解决，未来写代码必然推翻架构（或无法通过机械验收）。

### A-1（对应 `CD-01`）体素真值写入者归属冲突

| 项 | 内容 |
|---|---|
| **事实** | ① 既有规范：`VoxelWorld_Runtime_Core.md` §5.2（`:230`）声明"**安装（生成结果 → 世界真值）—— 冻结 VoxelCore 下的唯一合法路径**"，并由 Runtime Core **逐体素**执行 `target.SetVoxel(...)`（`:239`）；② 新契约：`WorldGeneration_Pipeline_Contract.md` Phase 6 = `Voxel Evaluation`，`PC35`（`:187`）声明"Phase 6 只负责**抽样与写入动作**"；同时 `PC43`/`PC53`（`:223`/`:272`）声明"Runtime 只能消费生成结果""`Runtime → Generation` 禁止" |
| **补充事实** | 新契约族**从未出现"安装"一词**（脚本检索：Pipeline_Contract / Parameter_Contract / WorldState_Runtime_Contract / Geology_Architecture_Contract / Parameter_Runtime_Model 全部为空） |
| **冲突点** | 同一个"把生成结果写入 `VoxelWorld`"的动作，被**两套文档**分别指派给 **Runtime Core**（既有、被 Stage E 验证以 SHA 冻结：`VoxelWorld_Runtime_Core.md` `10F1EC7F10DAD8F3`）与 **Phase 6 Voxel Evaluation**（新契约） |
| **为何是 Blocker** | 实现者无法同时满足两者：按既有规范写（Runtime 安装）⇒ 违反新契约 `PC53`；按新契约写（Phase 6 写入）⇒ 与"Runtime Core 唯一合法路径"冲突并可能破坏既有 12 项验证所依赖的安装语义 |
| **本审计不裁决** | 两种解读（"Runtime 是执行者、写入语义归 Voxel Evaluation" vs "写入者必须是 Phase 6"）均**未在文档中写明** → 需 `C-1`/`C-7` 类决策 |

### A-2（对应 `CD-02`）Terrain 真值双轨（项目级）

| 项 | 内容 |
|---|---|
| **事实** | 文档层已冻结："Voxel Data 是世界唯一真值"（`VoxelWorld_Core.md`:11）、"Mesh 不是世界真值"（`VoxelWorld_Mesh.md`:18）；但**项目层**存在第二套 Terrain：运行时场景挂载旧 `DensityTerrain`（[代码] 场景 GUID 核验：`DensityTerrain` ×1、`VoxelWorldStreamingBootstrap` ×0），且两套坐标契约不同（`ChunkSpace` 可配置 `chunkVoxelSize` + Terrain Local vs `VoxelChunk` 固定 16³ 整数世界坐标） |
| **为何是 Blocker** | "Terrain 只能有一个真值"在当前项目**不成立**；任何在旧栈之上接入新生成层（或反之）的实现都会绕不开"哪套是真值"的裁决（对应上一轮审计 `AD-1`，仍未决） |
| **本审计不裁决** | 不选择迁移/共存/替换方案（属实现边界规划） |

### A-3（对应 `CD-03`）阶段编号双轨制

| 项 | 内容 |
|---|---|
| **事实** | 方案 A（0–7，`:131`–`:138`）与方案 B（0–8，`:66`–`:210`）并存；系统文档多沿用 A：`Geology_Architecture_Contract.md` `GC2`（"Phase 6"）、`GC50`/`GC146`（"Voxel Evaluation（Phase 7）"）、`Biome_Architecture_Contract.md`（"Phase 5"）、`Resource_Design.md`（"Phase 6 之后""Phase 6/7"）、`Ecology_Design.md`（Phase 3/4/5/6/7）、`WorldQuery_Design.md`（Phase 1–7）；新契约沿用 B |
| **后果** | 同一编号在跨文档引用中指向**不同阶段**（例：`Phase 5` = Biome（A） / Geology（B）；`Phase 6` = Geology（A） / Voxel（B））→ 机械验收条款（如 `PCV3` 的"Phase 顺序固定"、`GC146` 的"体素化注入点唯一"）**无法以编号为准跨文档核验** |
| **为何是 Blocker** | 验收与实现以编号为锚点；编号双轨会直接导致"校验通过但阶段错位"或断言误判 |
| **本审计不裁决** | 不统一编号、不裁决保留哪套（属文档治理） |

### A-4（对应 `CD-04`）编号前缀多义（可核验性）

| 项 | 内容 |
|---|---|
| **事实** | `PC`：`Pipeline_Design PC1–PC5`（骨架）／`Pipeline_Contract PC1–PC120`（调度）／`Parameter_Contract PC1–PC18`（参数验收）；`PM`：`Pipeline_Design PM1–PM6`（群系）／`Runtime_Model PM1–PM120`；`WQ`：`Query_Design WQ1–WQ6`／`Query_Architecture WQ1–WQ120`；`RC`：`Region_Design RC1–RC5`／`Resource_Architecture RC1–RC150` |
| **后果** | 引用与结构扫描（如"扫描 `PC2` 断言"）会命中不同条款 → 验收结论不可信 |
| **说明** | 两处已在相应契约附录 D 自行登记（Query ⑥、Resource ①），但**未建立统一的前缀注册表** → 见 `C-5` |

---

## 9. Warning

> **定义**：不阻塞实现，但应在实现前明确；或属"未定义边界"。

| ID | 对应 | 问题 | 事实/证据 | 建议处置（不属本审计裁决范围） |
|---|---|---|---|---|
| **B-1** | CD-05 | **Runtime Modification 边界缺失**：谁拥有"改动集/编辑层"、如何跨帧存活、与 Runtime Lifetime/存档的关系均未定义 | `WorldParameter_Runtime_Model.md` `PM66`；`VoxelWorld_Mining.md`:123/212；`VoxelWorld_Runtime_Core.md` §5.2/§5.4 | 未来 `Modification Layer Contract`（`C-1`） |
| **B-2** | CD-06 | **配置参数无 Owner 登记**（`Seed`/`WorldHeight`/`SeaLevel`/`ContinentCount`/`EpochCount`/`OrdinalMax` 等） | `WorldParameter_Contract.md` §2.5（配置类表无 Owner 列）；`PC11` 要求 Seed Context 不可变 | 在参数契约内补登记（不改本文档） |
| **B-3** | CD-07 | **参数三态物理存储位置未定义**（谁持有数组/表、缓存归属） | `WorldParameter_Runtime_Model.md` `PM47`/`PM117`（存储优化不属 V1）；`PM36`（缓存须透明） | 未来"存储布局契约"（`C-3`） |
| **B-4** | CD-08 | **术语多义**（`Modifier`/`State`/`Context`/`Result`/`Rule`/`Presence`/`Source`/`Generation`） | §5.2 表（逐项给出出处） | 未来术语表（`C-4`） |
| **B-5** | CD-09 | **图例符号语义差异**（`O`、`R*`） | `WorldParameter_Contract.md`:285 vs `WorldGeneration_Pipeline_Contract.md`:369 | 统一图例或加交叉说明（`C-5`） |
| **B-6** | CD-10 | **6 个横向单元未定义**（`Resource↔Mesh`、`Resource→Runtime`、`Ecology↔Mesh`、`Ecology→Runtime`） | §3.4 | 在相关契约补显式禁令（不改本文档） |
| **B-7** | — | **`Elevation` 双角色易误读**：Geology 侧提供（高度场）+ WorldState 侧只读转发（`GC71`/`WSC48`）；虽已登记为"唯一通道"，但在"生产者矩阵"中易被读成双所有权 | `Geology_Architecture_Contract.md` `GC71`；`WorldState_Runtime_Contract.md` `WSC48` | 引用时强调"提供者 = 高度场；转发者 = WorldState" |
| **B-8** | CD-12 | **旧栈遗留文档/验证入口与新契约族未建立映射**（45 个验证入口；旧 `Chunk.md`/`Mining.md`/`VoxelAwareMesh.md`/`CoordinateMigration.md`） | [代码] `WorldGeneration_Architecture_Audit.md`:108 | 未来退役/共存决策（`C-6`） |
| **B-9** | CD-11 | **Phase 集合扩展（Mesh/Runtime 入管线）未在旧文档登记** | Pipeline_Design`:138` vs Pipeline_Contract`:192`/`:210` | 与新编号统一一并处理（`C-2`） |

---

## 10. Future Design Items

> **本轮不设计**：以下仅为"未来需要一份文档/一次决策"的清单，不含方案内容，不新增系统。

| ID | 未来文档/决策 | 触发原因（关联） | 依赖 |
|---|---|---|---|
| **C-1** | **Modification Layer Contract**（运行时改动集/编辑层的所有权、存储、生命周期、与 Mining/存档的关系） | `B-1`（Missing Boundary）、`A-1`（写入者归属） | 需先裁决 `A-1` |
| **C-2** | **Phase 编号统一或映射表**（在 A/B 两方案间建立权威编号 + 映射） | `A-3`、`B-9` | — |
| **C-3** | **参数存储布局契约**（谁持有三态；缓存与权威的物理边界） | `B-3` | 依赖 `C-2`（Phase/生命周期口径） |
| **C-4** | **术语表（Glossary）**（统一 `Modifier`/`Rule`/`Presence`/`State`/`Context`/`Result`/`Source`/`Generation`/`Value`/`Condition`/`Identity`） | `B-4` | — |
| **C-5** | **引用编号命名空间规范**（前缀注册表 + 引用格式"文档名 + 编号"；含 `PC`/`PM`/`WQ`/`RC`/`RB` 冲突处置） | `A-4`、`B-5` | — |
| **C-6** | **旧栈退役/共存决策文档**（`DensityTerrain` 栈 vs `VoxelWorld` 栈；坐标契约迁移） | `A-2`、`B-8`（对应上一轮审计 `AD-1`） | 需先裁决 `A-2` |
| **C-7** | **体素写入/安装语义裁决**（是否允许 Runtime 作为"执行者"；Phase 6 与 Runtime 的边界） | `A-1` | 可与 `C-1` 合并 |

---

## 附录：本审计未做的事（避免误解）

1. **未修改任何文件**（24 份被审文档 SHA16 已在 §1.1 冻结，审计后应保持一致）。
2. **未裁决任何未定义设计**：`A-1`–`A-4`、`B-1`–`B-9` 只登记事实与冲突，不含解决方案。
3. **未新增系统、参数、因果边、`VoxelType`**。
4. **未运行任何构建/测试/自动修复**；引用的实现事实来自上一轮只读审计（`WorldGeneration_Architecture_Audit.md`）的既有核验结论。
5. **未逐行精读全部旧栈文档**（`Chunk.md`/`Mining.md`/`VoxelAwareMesh.md`/`CoordinateMigration.md` 仅做结构核对）→ 涉及旧栈细节的结论标 `[待确认]`。
