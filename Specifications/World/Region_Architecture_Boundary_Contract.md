# Region Architecture Boundary Contract V1

> **文档性质**：**边界契约规范（Boundary Contract）**。本文档冻结 **Region 作为 WorldParameter `Modifier` 来源**的边界：Region 的定位、输入、输出、`Base` / `Modifier` / `Final` 的三方归属、未来扩展边界与 **Legacy 隔离**。
>
> **维护说明**：本文档定义 Region 与 WorldParameter Modifier 的职责边界，不以历史任务的修改范围限制后续维护。Region 生成算法及各下游系统的实现状态，应以对应实现和专项契约为准。
>
> **本文档不是实现规范**。**不定义**：来源类别、阈值、衰减形状、上限值、采纳算法数值；**只定义边界**。
>
> **核心定位**：
>
> | 项 | 内容 |
> |---|---|
> | **它是什么** | **Region 与参数层之间的边界契约**：Region 是**空间解释层**与 **`Modifier` 提供者**；定义它**能读什么、能输出什么、不能做什么** |
> | **它不是什么** | **不是地形生成器**；**不是生物群系（Biome）**；**不是资源系统**；**不是实体系统**；**不是 Region 数据契约本体**（= `Region_Architecture_Contract.md`）；**不是 Region 规则权威**（= `Region_Design.md`） |
>
> **权威依赖与职责映射**（如发现冲突，应修订拥有该规则的文件并同步引用）：
>
> | 规范 | 与本契约的关系 |
> |---|---|
> | `Region_Architecture_Contract.md` | **Region 数据契约本体**：`RA1`–`RA8`（范围与禁令）、§2 `RegionKey`/组成/形态、§3 候选契约（输入侧）、§5 `RegionModifier` 合成与写入权（`RA62`–`RA79`）、§6 `RegionType` 边界（`RA85`–`RA92`）、§7 Chunk/Voxel 边界（`RA93`–`RA104`）、§9 确定性（`RA115`–`RA126`）、§10 局部求值（`RA127`–`RA139`）、§11 消费者权限矩阵（`RA140`–`RA144`）、§13 V1 禁令（`RA145`–`RA152`）、附录 C 禁止清单（`RAX-1`–`RAX-16`） |
> | `Region_Design.md` | **Region 规则与语义权威**：§1.1/§1.2（Region 是什么/不是什么）、§1.4 `RG-A`/`RG-B`（无源不成区、单一方向）、§3.1–§3.3（来源与因果方向）、§4.2（修正集）、§6（与历史的关系、`RH1`–`RH5`）、§8（边界与衰减要求 `RBD1`–`RBD6`）、§11（V1 范围与禁令）、§12.1–§12.5（与 Geology / WorldState / Runtime 的兼容检查与 `RI1`–`RI4`、`RV9`/`RV10`）、附录 B（验收规则） |
> | `WorldParameter_Contract.md` | **参数身份/量纲/Owner/权限权威**：§3.2 表 A（可修改层、可读取层）、§3.4 表 C、§4 权限矩阵（Region 行 = `P（修正项）`；`PW2`/`PW3`/`PW5`）、§6.4 域分配（Region 域；`PZ10`–`PZ14`）、附录 B（`ParameterId` 权威）、附录 C 变更规则 |
> | `WorldState_Runtime_Contract.md` | **求值契约**：§2.1 最终公式、§2.2 逐参数合成类型、§2.3 求值内部顺序（含步骤 5 的 `RegionModifier` 与步骤 2/3 的 `Fertility` 约束）、`WSC9`–`WSC14`（`WSC10` 禁 `Elevation`/`Ocean` 进入求和式、`WSC14` 零修正显式性）、`WSC21`/`WSC22`（Base 可分离、关闭 Region ⇒ `Final == Base`）、`WSC103`（禁第二套求值路径） |
> | `WorldParameter_Runtime_Model.md` | **状态与权限模型**：`PM23`–`PM34`（三态、`Modifier` 唯一写入者、不可变定稿）、`PM37`–`PM46`（定长索引）、`PM52`–`PM57`（坐标求值边界） |
> | `WorldGeneration_Pipeline_Contract.md` | **阶段与写入权**：Phase 3 `Region Evaluation` 的 Input / Allowed Read / Forbidden Read / **Forbidden Write**、`PC19`（两段式发布）、`PC20`/`PC21`、`PC23`（六步采纳）、`PC24`（分量恰好 6 个）、`PC25`（交换律/无放大反馈）、`PC26`（History 接入方式） |
> | `WorldParameter_Source_Contract.md` | **参数侧边界**（Phase 1.6）：§2 依赖方向与反向禁令、§3 参数来源边界、§5 临时来源登记、§6 白名单/黑名单、`WPS18`（`Biome → Parameter` 禁止）、`WPS58`/`WPS76`（`Modifier` 表示与未接入退化） |
> | `WorldV2_Phase1_PreImplementation_Decisions.md` | 已冻结决策：**`D2`**（`BaseValue` = WorldState 层；`Modifier` = **Region 层唯一写入者**；`FinalValue` = WorldState 合成 `clamp01(Base + Modifier)`）、`DR17`/`DR18`（Chunk / Scene 不参与）、`DR25`、`DR35` |
> | `Legacy_Status.md`（`Specifications/Architecture/`） | **Legacy 冻结基线**：Legacy 与 V2 双向隔离、Legacy 不迁移、基线 SHA 清单 |
>
> **冲突时的优先级**：Region 的**规则与语义**以 `Region_Design.md` 为准；Region 的**数据形态/采纳/权限**以 `Region_Architecture_Contract.md` 为准；参数的**身份/量纲/Owner**以 `WorldParameter_Contract.md` 为准；**求值与合成**以 `WorldState_Runtime_Contract.md` 为准；**阶段顺序与写入权**以 `WorldGeneration_Pipeline_Contract.md` 为准。**本契约只登记"边界"**，不改变上述任何权威。
>
> **声明性规则**：本文档本身不产生运行时效果，文档存在与否不应改变任何体素输出。§10.3 / 附录 C 用于追踪需要协调的差异；确认冲突后，应修订拥有相应规则的权威文件并同步引用。

---

## §1 定位与契约边界

### 1.1 定义

| # | 规则 |
|---|---|
| **RAB1** | **定义**：Region Architecture Boundary Contract = **Region 作为参数 `Modifier` 来源的边界契约**——登记 Region 的**定位、输入、输出、三方归属、扩展边界与 Legacy 隔离** |
| **RAB2** | **引用而非复制**：本文档对 Region 的身份、字段、采纳流程、修正集、域分配**一律引用**（`Region_Architecture_Contract.md` / `Region_Design.md` / `WorldParameter_Contract.md` §6.4），**不重述**为第二权威 |
| **RAB3** | **不新增 Region 类型列表**：来源类别、子类、`RegionType` 取值集合的权威是 `Region_Design.md` §3.2 / §4.3 与 `Region_Architecture_Contract.md` 附录 B；本文档**不新增、不扩展、不复制**该列表 |
| **RAB4** | **不新增因果边**：本文档只能细化既有边（`来源 → Region → 环境修正 → 最终环境 → 表现 / 资格`，`RG-B`；`RA2`） |
| **RAB5** | **不定义算法**：不定义任何 Region 生成、采纳、衰减、上限、排序算法及其数值（`RA1`；`RA145` 同族纪律） |
| **RAB6** | **不修改既有层**：不修改 Biome / Geology / Resource / Ecology 的任何规则，也不为其定义边界（其边界归各自契约） |
| **RAB7** | **本文档不得被实现为"第二个 Region 契约"**：凡与 `Region_Architecture_Contract.md` 冲突处，一律以该文档为准（§10.3 登记） |

### 1.2 Region 是 / 不是（定位边界）

**Region **是**：**

| # | 定位 | 含义 | 依据 |
|---|---|---|---|
| **RAB8** | **空间解释层** | Region 是**连续空间中的世界状态解释单元**（原因侧）：解释"为什么这里的环境如此"，不是结果侧标签 | `Region_Design.md` §1.1/§1.3 `RT1`/`RT2`；`Region_Architecture_Contract.md` §2.1 |
| **RAB9** | **`Modifier` 提供者** | Region 是 `RegionModifier`（6 分量）的**唯一写入者**：只提供**修正项**，不提供基础值 | `RA75`；`D2`；`PM28`；`PW` 矩阵 Region 行 = `P（修正项）` |

**Region 不是：**

| # | 不是 | 含义（边界） | 依据 |
|---|---|---|---|
| **RAB10** | **地形生成器** | 不生成地形、不改高度、不改海陆、不改 `Density`/`CaveDensity`/地层、不写 Voxel、不生成 Chunk/Mesh | `RE1`；`RA69`；`RA99`–`RA104`；`RA8`；`Region_Design.md` §1.2 |
| **RAB11** | **生物群系（Biome）** | Biome 是**环境表现结论**（下游）；Region 是**原因侧**。Region 不定义群系规则表、不产出群系结论、不读群系结论 | `RA92`；`RB2`/`RB3`；§5.2 |
| **RAB12** | **资源系统** | Region **不决定资源**：不产出 `ResourcePresence`、不作资源判定、不读资源结论 | `RA7`；`WPS44`/`WPS45`；`Resource_Design.md` 同族纪律 |
| **RAB13** | **实体系统** | Region **不产生对象**：不创建 Entity / 结构 / 植被 / 生物 / Chunk / Mesh；只产出**资格声明**（`Qualifications`） | `RA8`；`RGS`；`RA82`；`EF1` |
| **RAB14** | **存储容器 / 跨帧状态** | Region 不是存档单位、不是跨帧存活对象、不保存计时器或演化状态；不是 `GameObject` | `RA16`；`RT3`；`Region_Design.md` §1.2 |
| **RAB15** | **技术划分（Chunk）** | Region 边界**不得**与 Chunk 对齐、不得按 Chunk 组织或存储、不出现在任何调度接口中 | `RA93`–`RA95`；`Region_Design.md` §1.2 |
| **RAB16** | **参数层** | Region **不拥有参数**：不定义参数语义、不写 `Base`、不定义参数集合 | `RA4`；`WSC21`；§2 |

### 1.3 与既有文档的分工

| 问题 | 权威文档 | 本文档的作用 |
|---|---|---|
| Region 的规则（来源类别、修正集、边界要求） | `Region_Design.md` | 引用（不重述） |
| Region 的选择/采纳/排序/上限/物化/发布 | `Region_Architecture_Contract.md` §4 | 引用（不重述） |
| `Modifier` 的写入权与合成权 | `Region_Architecture_Contract.md` §5.4 + `WorldState_Runtime_Contract.md` §2 | 明确三方归属（§2） |
| 参数是谁、什么范围、谁能改 | `WorldParameter_Contract.md` | 引用（不重述） |
| 阶段顺序、Allowed/Forbidden Read & Write | `WorldGeneration_Pipeline_Contract.md` Phase 3 | 细化为**输入/输出白名单与黑名单**（§3/§4） |
| 参数侧来源边界 | `WorldParameter_Source_Contract.md` | 与本契约互为对侧（输入 ↔ 输出） |
| Legacy 状态 | `Legacy_Status.md` | 登记完整隔离要求（§7） |

---

## §2 三方归属：`Base` / `Modifier` / `Final`

### 2.1 归属表（冻结）

| 量 | 产生者（唯一） | 消费者 | 禁止 |
|---|---|---|---|
| **`BaseValue`** | **参数层（WorldState 层求值方）** | WorldState 合成步骤 | 禁止 Region 产生/修改 `Base`（`RA4`；`RAB16`） |
| **`Modifier`** | **Region 层（唯一写入者）** | WorldState 合成步骤 | 禁止任何其它系统写入（`RA75`）；禁止 Region 越过分量集合（`RA67`/`RA68`） |
| **`FinalValue`** | **WorldState 层（合成）** | Biome · Geology（仅 P3）· Ecology · Resource · WorldQuery · Runtime（只读） | 禁止 Region 直接产出 `Final`（`RA76`；`PC20`） |

| # | 规则 |
|---|---|
| **RAB17** | **三方归属不可交换**：`Base` ≡ 参数层、`Modifier` ≡ Region 层、`Final` ≡ WorldState 层（`D2`；`PM27`–`PM29`；`RA75`/`RA76`） |
| **RAB18** | Region 的产物**只能是**"修正项 + 解释/资格信息"，**不得**包含任何基础值或最终值（`RA73`：不得含与位置无关的常量偏置——那是 `Base` 的职责） |
| **RAB19** | Region **不得**读取自己将要影响的 6 个分量的 `Final` 值（构成环）：`FinalState` 在 Region 完成前**尚未定稿**（`Pipeline` Phase 3 Forbidden Read；`PC19`/`PC21`） |
| **RAB20** | Region **不得**读取未定稿的 `Base` 值以"预判"自己的修正（`WSC21`：`Base` 必须能在不计算任何 Region 的前提下求出；反向不再成立） |

### 2.2 公式与合成语义（引用）

```
RegionModifier(p) = Σ_r Contribution_r(p)                      （逐分量、加法、交换律）
FinalState(p)     = clamp01( BaseWorldState(p) + RegionModifier(p) )
```

| # | 规则 |
|---|---|
| **RAB21** | 合成**只有加法 + 钳制**（`RA62`；`WSC9`/`WSC12`）；Region 不得引入乘法、覆盖、优先级、胜负语义（`RA63`/`RA66`/`RA71`；`RA124`） |
| **RAB22** | `RegionModifier` 的**分量集合恰好 6 个**（`Temperature` / `Moisture` / `Fertility` / `MagicDensity` / `HolyInfluence` / `CorruptionInfluence`），与 `PC24` 一致（`RA67`） |
| **RAB23** | **`Elevation` / `Ocean` 不在修正集内**：Region 永远不能改变地形高度与海陆边界（`RA69`；`RE1`）；`Balance` 不是可修正分量（`RA70`） |
| **RAB24** | **零修正的显式性**：Region 层不存在或未生效时 `RegionModifier ≡ 0`（逐分量），且必须与"Region 层不存在"逐值等价（`WSC14`；`RA104`） |
| **RAB25** | **退化等价（最强隔离保证）**：关闭 Region 层 ⇒ `FinalState == Base`（逐分量）且**体素输出与今天逐体素一致**（`WSC22`；`RA104`；`RI3`；`RG10`） |

### 2.3 反向禁令（三方归属的推论）

| # | 反向路径 | 判定 | 依据 |
|---|---|---|---|
| **RAB26** | `下游 → RegionModifier`（Biome/Ecology/Resource/Query/Runtime 写修正） | **禁止** | `RA75`/`RA77`；`PW2`；`RAX-1` |
| **RAB27** | `下游 → Region`（创建/修改 Region 或补出 Region） | **禁止** | `RA20`；`RAX-2` |
| **RAB28** | `结果 → Modifier`（由 `RegionType`/Biome/生态/资源结果反推修正） | **禁止** | `RA79`；`RA86`；`RAX-3` |
| **RAB29** | `Runtime → Region`（运行时回写） | **禁止**：体素编辑与玩家状态属 Runtime State，不是修正项 | `RA78`；`PX2` |
| **RAB30** | `Region → 自举`（Region 产生新候选） | **禁止** | `RA19`；`RAX-16` |

---

## §3 Region 输入边界

### 3.1 输入白名单 `RABW`（封闭）

| 编号 | 允许读取 | 条件 / 依据 |
|---|---|---|
| **RABW1** | `Seed`（含 Seed Context） | 只读；确定性来源（`RA115`；`PZ1`） |
| **RABW2** | **世界坐标**（唯一空间输入） | 不得用 Chunk 局部坐标（`RA93`/`RA94`；`PC86`；`Region_Architecture_Contract.md` §3.2 对 `Position` 的约束） |
| **RABW3** | **已登记 WorldParameter 中的"地形派生量"**：`Elevation`、`Ocean` | **只读**（`Region_Design.md` §12.2；`Pipeline` Phase 3 Allowed Read："地形派生量（`Elevation`/`Ocean`，只读）"） |
| **RABW4** | `Balance` 的**定义**（只读复用） | 只复用定义，**不得**另立第二定义（`RA70`；`RO2`） |
| **RABW5** | **骨架事实 / WorldSkeleton**（自然来源的输入） | 只读；**读边 `Skeleton → Region` 为已批准的上游只读依赖**（阶段输入面：`WorldGeneration_Pipeline_Contract.md` Phase 3 `Allowed Read`；允许边枚举登记：同文档 `PC52`）；自然来源的规则归 `Region_Design.md` §3.2（本文档不复制来源清单）；**产物接口与查询契约**见 `WorldSkeleton_V1_Minimal_Contract.md`（本文档不定义其字段） |
| **RABW6** | **History 事实与 `HistoryTags`**（只读） | **允许**用于 Region 属性推导（`RH2`）；不得用于参数求值（`RH3`）；不得引入历史模拟（`RH5`） |
| **RABW7** | **显式 Region 参数**（登记在案的规则参数） | 参数值必须落在契约允许域内（`Region_Architecture_Contract.md` §3.2：显式标量、范围由 Region 规则声明） |

| # | 规则 |
|---|---|
| **RAB31** | 白名单**封闭**：表外量一律不得读取（`RA7`；`PA1`；`WSC25` 同构纪律） |
| **RAB32** | **`FinalState` 不在白名单内**：Region 不得读取将要由自己修正的那 6 个分量的最终值（`RAB19`；`PC19`/`PC21`） |
| **RAB33** | **`Base` 运行值不在白名单内**：Region 不得读取未定稿的基础值（`RAB20`；`WSC21`） |
| **RAB34** | 白名单中的每一项都必须是 `(Seed, Domain, Position)` 的确定性函数（`RA115`；`RA21`） |
| **RAB35** | Region **不得**把白名单读取结果**缓存为权威**；若缓存必须透明、可清空、由 `(Seed, 参数, 世界坐标, 域标签)` 索引（`RA138`/`RA139`） |

### 3.2 输入黑名单 `RABB`（整体禁止）

| 编号 | 禁止读取 | 依据 |
|---|---|---|
| **RABB1** | **Runtime**：脏标记 / 加载集合 / 预算 / 帧序 / 调度状态 / 已生成内容 | `RA7`；`RA78`；`RA95`；`PX2` |
| **RABB2** | **Player**：位置 / 进度 / 背包 / 已探索 / 已采集 | `RA121`；`WSC58` |
| **RABB3** | **Entity**：任何对象 / 结构 / 生物 / 实体状态 | `RA8`；`EF1`；`RAB13` |
| **RABB4** | **Mesh**：几何、面、材质、子网格、任何网格产物 | `Region_Design.md` §12.3；`RA8` |
| **RABB5** | **Mining / 挖掘**：挖掘状态、开采进度、已采集统计、任何挖掘产物 | `RA121`；`RABB2`；Legacy Mining 另见 §7.2 |
| **RABB6** | **Voxel 修改结果**：玩家编辑后的体素真值、体素状态、材料、Chunk 体素数据 | `RA7`；`RA99`；`RA100` |
| **RABB7** | **Biome 结论 / 群系地图 / 群系分类结果** | `RA7`；`RA92`；`RAB11` |
| **RABB8** | **Geology 产物**：`Density` / `Material` / `CaveDensity` / `DepositZone` / 地层结论 | `RA103`；`RA7` |
| **RABB9** | **Resource 判定 / 生态候选 / 生态资格** | `RA7`；`RAB12` |
| **RABB10** | **Chunk 状态**：生命周期、是否加载/已生成、局部坐标、Chunk 划分 | `RA93`–`RA96` |
| **RABB11** | **Scene / `Transform` / 相机 / 渲染状态** | `RA94` 同族；`DR18` |
| **RABB12** | `Random` / 任何有状态伪随机源 | `RA119`；`PZ5` |
| **RABB13** | `Time` / `DateTime` / 时间戳 / tick / 帧序 / `Guid` | `RA120`；`PZ6` |
| **RABB14** | 加载顺序 / 遍历顺序 / 查询顺序 / 世界级中间态 | `RA122`；`PZ8` |
| **RABB15** | 保留域 `R-1` / `R-2` 的混合方式（`HeightFunction` / `GeologyNoise` 通道） | `RA117`；`PZ12`；`RAX-14` |
| **RABB16** | `RegionType`（作为规则输入） | `RA85`；`RAX-4`：`RegionType` 无规则权 |
| **RABB17** | 世界级前置：全世界 Region 表、全量候选、全局注册表 | `RA131`–`RA135`；`RAX-12` |
| **RABB18** | **Legacy 任何类型/产物**（含 Legacy 坐标栈与 Legacy 地形/挖掘/网格语义） | §7；`Legacy_Status.md` |

| # | 规则 |
|---|---|
| **RAB36** | 黑名单是**整体性**的：任一项出现即判**边界违规**，不得以"只读一次""仅用于调试""不影响结果"为由豁免（`RA7` 同族纪律） |
| **RAB37** | 输入封闭必须**可机械验证**：Region 求值路径的输入符号必须全部落在 `RABW` 内（`RA141` 同族；`WPS65` 同构） |

---

## §4 Region 输出边界

### 4.1 输出白名单（**只能输出这两类**）

| 编号 | 允许输出的产物 | 形态与约束 |
|---|---|---|
| **RAB-O1** | **Region Context** | Phase 3 产物（`Pipeline` Phase 3 **Output**）：**只读聚合**，含 Region 集合（身份 / 来源 / `Extent` / `Profile` / `Tags` / `Qualifications`）与解释链引用；必须**只读发布**（`RA20`；`RA54`–`RA58` 同族） |
| **RAB-O2** | **RegionModifier** | 恰好 **6 个分量**（§2.2；`RA67`）；由 Region 层**唯一写入**（`RA75`）；只提供修正项（`RA73`） |

> `Region Context` 内部的 `RegionType` 属**解释标签**（`Region_Design.md` §4.3），**不是独立输出**：它只能作为 `Region Context` 的解释字段发布，**无规则权**（`RA85`–`RA92`；`RABB16`）。

| # | 规则 |
|---|---|
| **RAB38** | 除 `RAB-O1` / `RAB-O2` 及其**内部字段**外，Region **不得**输出任何其它产物（`RAB39`–`RAB45`） |
| **RAB39** | 输出必须**确定性**：同 `(Seed, DomainId, Position)` ⇒ 逐字段相同（`RA115`；`PM52`） |
| **RAB40** | 输出必须**无副作用**：不写体素、不建 Chunk、不置脏、不生成对象、不改任何参数（`RA97`；`WSC20`） |
| **RAB41** | 输出不得**跨帧存活**为世界状态：不存在"上次求值的 Region"（`RA16`；`RA137`；`WSC66` 同族纪律） |
| **RAB42** | 输出的**解释能力**（`Tags` / `OriginRef` / `ReasonChainRef` / `Qualifications`）必须只读，不得成为规则输入（`RA82`；`RA81`；`PW5`） |

### 4.2 输出黑名单（禁止产出）

| 编号 | 禁止产出 | 依据 |
|---|---|---|
| **RAB43** | **Voxel**：体素值 / 地表材料 / 新 `VoxelType` / 直接放置或清除方块 | `RA99`；`RA100`；`RA3`；`RAX-7` |
| **RAB44** | **Entity**：对象 / 结构 / 植被 / 生物 / 遗迹实体（只允许产出**资格声明**） | `RA8`；`RGS`；`RA82`；`EF1` |
| **RAB45** | **Resource**：`ResourcePresence` 判定 / 资源清单 / 矿藏结论 | `RAB12`；`RA7`；`WPS44` |
| **RAB46** | **Biome Result**：群系结论 / 群系地图 / 群系分类 | `RA92`；`RA7`；`RAB11` |
| **RAB47** | **Geology 产物**：`Density` / `Material` / `CaveDensity` / `DepositZone` / 地层结构 | `RA103`；`RAX-8` |
| **RAB48** | **地形量**：`Elevation` / `Ocean` 的任何修改 | `RA69`；`RA152`；`RAX-8` |
| **RAB49** | **Chunk / Mesh / 调度条目**：Chunk 创建、脏标记、网格数据、调度接口条目 | `RA97`；`RA8`；`Region_Design.md` §12.3 |
| **RAB50** | **存档 / 跨帧状态 / 全局 Region 表** | `RA132`；`RA137`；`RA16` |
| **RAB51** | **第 7 个修正分量**或任何对分量集合的扩展 | `RA68`；`RAX-9` |

| # | 规则 |
|---|---|
| **RAB52** | 输出黑名单是**缺陷判据**：出现任一项即判该实现**违反 Region 边界**（`RAX-*` 同族） |

### 4.3 唯一允许的"Region → 体素"间接路径

```
Region → RegionModifier(6 分量) → FinalState（WorldState 合成）
        → Biome（唯一规则表）→ Geology 的 P3 地表材料（{Grass, Dirt, Stone}）
```

| # | 规则 |
|---|---|
| **RAB53** | 上述路径是**唯一**允许影响体素的路径（`RA101`；`RI1`） |
| **RAB54** | 该路径**只作用于地表层**，不改变 `Density` / 洞穴 / 地层 / 地表高度，不新增体素类型（`RA102`；`RA103`；`RI2`） |
| **RAB55** | 任何"Region 直接改材料/加方块/改高度"的实现**一律禁止**（`RA99`；`RAX-7`；`RI4`） |

---

## §5 四条硬边界（逐条）

### 5.1 Region 不是地形生成器

| # | 规则 |
|---|---|
| **RAB56** | 地形高度、海陆边界、实体/空判定、洞穴、地层、矿藏**全部**不属于 Region（`RA69`；`RA103`；`RA152`） |
| **RAB57** | Region 不生成 Chunk、不生成 Mesh、不写体素（`RA8`；`RA97`；`RA99`） |
| **RAB58** | 关闭 Region 层必须**逐体素等价**（`RA104`；`RI3`；`RG10`） |

### 5.2 Region 不是生物群系

| # | 规则 |
|---|---|
| **RAB59** | Region **不等于** Biome：Region 是原因侧解释单元，Biome 是环境表现结论（`RA92`；`RTY5`；`RB2`） |
| **RAB60** | Region **不得**产出群系结论、**不得**读群系结论、**不得**定义第二套群系判定（`RA7`；`RB3`） |
| **RAB61** | Region **不得**用 `RegionType` 代替 Biome（`RA92`；`RAX-4`） |

### 5.3 Region 不是资源系统

| # | 规则 |
|---|---|
| **RAB62** | Region **不决定资源**：不产出 `ResourcePresence`、不判定资源存在性（`RAB45`） |
| **RAB63** | Region 只能提供**条件性输入**（经 `FinalState`），资源判定权归 Resource 层（`WPS44`；`RN7`） |
| **RAB64** | Region **不得**读取资源结论或由资源结论反推修正（`RA7`；`RA79`） |

### 5.4 Region 不是实体系统

| # | 规则 |
|---|---|
| **RAB65** | Region **不创建**任何对象：结构 / 植被 / 生物 / 遗迹 / `GameObject` / Chunk / Mesh（`RA8`；`RGS`；`RAX-7`） |
| **RAB66** | Region 的产物中与"存在性"相关的部分**只能是资格声明**（`Qualifications`），且不得被解释为"已生成"（`RA80`–`RA83`） |
| **RAB67** | Region **不得**读取任何实体状态（`RA8`；`EF1`；`RABB3`） |

---

## §6 未来扩展边界（**只登记，不实现**）

> 本节登记三条**未来扩展方向**的边界。**本节不实现任何内容**，**不新增因果边**（`RA2`/`RAB4`），**不得**作为当前实现依据。
> 凡与既有契约的方向不一致处，一律按 §10.3 登记为**待确认项**，**不自行裁决**。

### 6.1 `Region → Biome`

| # | 登记项 | 内容 |
|---|---|---|
| **RAB68** | **既有事实** | 方向**已由契约覆盖**：Region 是原因侧，Biome 读取**已合成**的 `FinalState`（`BC27`；`RA92`；`PC21`） |
| **RAB69** | **可扩展内容（未实现）** | Biome 规则表如何消费修正后的参数（阈值、分类边界）属 **Biome 层**（Phase 4）的实现规范；Region 层**不得**定义、不得读取其规则 |
| **RAB70** | **禁止** | 禁止 `Biome → Region` / `Biome → Modifier`（`RAB26`/`RAB27`；`RAX-1`/`RAX-2`）与 `RegionType → Biome`（`RA88`） |
| **RAB71** | **前置条件** | 任何实现必须等：Region 采纳链落地 + Biome 输入契约（`BC27`/`BC30`）就位 + 通过 `RABV*`（§9） |

### 6.2 `Region → History`

| # | 登记项 | 内容 |
|---|---|---|
| **RAB72** | **方向澄清（重要）** | 既有契约冻结的方向是 **`History → RegionSourceCandidate → Region`**（History 是 Region 的**输入**）：`Region_Design.md` §6.1/§6.3；`Region_Architecture_Contract.md` §3.1；`PC26` |
| **RAB73** | **禁止（反向）** | **`Region → History`（写入/修改历史事实与标签）一律禁止**：`HistoryTags` 是**只读**历史事实（`RH1`/`RH2`/`RH4`），Region 只能**附着并只读暴露**（`Region_Design.md` §6.3） |
| **RAB74** | **可扩展内容（未实现，`[待确认]`）** | 允许登记的未来方向是"**Region 产物作为 History 解释链的只读引用（归因）**"；其可用性、范围与权限**未在任何既有规范中定义** ⇒ 登记为待确认，**不得实现**，不得作为判定条件（`PW5` 只读归因纪律） |
| **RAB75** | **前置条件** | 任何实现必须先由 `WorldHistory_Design.md` **升版本**定义该边，本文档不得先行 |

### 6.3 `Region → Civilization`

| # | 登记项 | 内容 |
|---|---|---|
| **RAB76** | **既有事实** | `CivilizationPresence` 的 Owner 是 **History**（`WorldParameter_Contract.md` 附录 B 序 13；表 C）；Region **无写权限**（`PW2`；表 A 可修改层列） |
| **RAB77** | **禁止** | 禁止 Region 写 `CivilizationPresence`、不得写 `Epoch`/`Ordinal`（同属 History） |
| **RAB78** | **可扩展内容（未实现，`[待确认]`）** | 允许登记的未来方向是"**Region 产物作为文明活动证据的只读归因输入**"（`R*`，仅归因，**不得作判定条件**，`PW5`）；该边**未在既有规范中定义** ⇒ 待确认 |
| **RAB79** | **前置条件** | 同 `RAB75`（须由 `WorldHistory_Design.md` 先行定义） |

### 6.4 扩展纪律

| # | 规则 |
|---|---|
| **RAB80** | 三条扩展方向**全部**：不得新增因果边（`RA2`）、不得新增修正分量（`RA68`）、不得新增 Region 类型或来源类别（`RAB3`/`RA1`） |
| **RAB81** | 任何扩展必须**独立升版本**并同步更新对应权威文档；本文档只做**登记**（`Region_Architecture_Contract.md` 附录 D 同族规则） |
| **RAB82** | 扩展不得破坏 `RAB25`（关闭 Region ⇒ 逐体素等价）与 `RA104` |

---

## §7 与 Legacy 完全隔离

### 7.1 隔离声明

| # | 规则 |
|---|---|
| **RAB83** | **双向隔离**：Region 层**不得**依赖 Legacy 任何类型/产物；Legacy 也**不得**依赖 Region（`Legacy_Status.md`：Legacy 冻结、与 V2 无相互依赖） |
| **RAB84** | Region 的**实现位置**只能落在 V2 侧（`Assets/Scripts/World/**`）；**不得**写入 `Assets/Scripts/Terrain/**`，也不得改写既有 V2 栈文件（`RV9`/`RV10` 同族纪律） |
| **RAB85** | Legacy **不可迁移、不可复用其语义**：Legacy 的网格/挖掘/坐标语义（含 `VoxelAwareMesh*` 的 `EvaluationOwner`、`SpatialFace` 等）与 Region 无任何对应关系，禁止借用为 Region 概念 |
| **RAB86** | 隔离状态**可机械验证**：结构扫描（Legacy 符号命中 = 0）+ Legacy 基线 SHA 不变 + `RAB25` 退化等价（`RV9`；`Legacy_Status.md` 基线清单） |

### 7.2 禁止触碰的 Legacy 面

| 编号 | 禁止对象 | 说明 |
|---|---|---|
| **RAB-L1** | `Assets/Scripts/Terrain/**`（Legacy 地形/密度/网格/挖掘全部文件） | 不读取、不调用、不修改、不新增依赖 |
| **RAB-L2** | Legacy 类型与命名空间（`DensityTerrain`、`DensityFieldGenerator`、`ChunkSpace`、`TerrainChunk`、`TerrainMeshGenerator`、`TerrainMining`、`VoxelAwareMesh*`、`TerrainEffective*` 等） | 不得出现在 Region 的输入/输出/规则中 |
| **RAB-L3** | Legacy 坐标栈与迁移语义（`CoordinateMigration` 及旧 Chunk/局部坐标约定） | Region 只用**世界坐标**（`RABW2`） |
| **RAB-L4** | Legacy 挖掘与已采集状态 | `RABB5`：不得作为 Region 输入 |
| **RAB-L5** | Legacy Scene / 场景对象 / `VoxelAwareMesh` 运行时突变结果 | `RABB1`/`RABB6`/`RABB11` |

| # | 规则 |
|---|---|
| **RAB87** | 上述 Legacy 面**不得**被引用、镜像、翻译或"临时借用"（`RAB85`） |
| **RAB88** | Legacy 的**删除/迁移**不在本文档范围内：在 V2（含 Region 层）稳定前，Legacy **保持冻结**（`Legacy_Status.md`） |

---

## §8 确定性与当前状态

### 8.1 确定性（引用）

| # | 规则 |
|---|---|
| **RAB89** | Region 产物必须由 `(Seed, DomainId, Position)` **唯一确定**（`RA115`；`PZ1`） |
| **RAB90** | `Domain` 必须**参与哈希混合**且使用 §6.4 登记的 Region 域；**未登记域不得使用**（`RA116`；`RA117`；`PZ10`/`PZ14`） |
| **RAB91** | 同输入多次求值、两个独立实例/进程 ⇒ **逐字段相同**（`RAV3`；`PM52`；`IBP46`） |
| **RAB92** | 验收必须证明**非平凡性**与**采纳确实发生**（`RA125`/`RA126`）——防止"永远空集"的假通过 |

### 8.2 当前状态：Region 层**未接入**

| # | 规则 |
|---|---|
| **RAB93** | 当前实现中 `RegionModifier ≡ 0`（Region 层不存在），`FinalValue == BaseValue`（逐分量）——这是契约允许的**退化情形**，不是缺陷（`WSC14`；`WSC22`；`WPS76`） |
| **RAB94** | 因此本文档**不要求任何代码变更**；Region 落地属后续实现轮次（`PC23` 六步采纳流程 + `RABV*` 全部验收） |
| **RAB95** | 落地时 `Modifier` 的表示法（转发/派生参数的 `None` 语义）按 `PM32`/`WSC10` 与 `WPS58` 的登记处理，本文档**不裁决** |

---

## §9 验收规则

| 编号 | 验收项 | 判据 |
|---|---|---|
| **RABV1** | **输入封闭** | Region 求值路径的输入符号 ⊆ `RABW`（`RAB37`） |
| **RABV2** | **无黑名单读取** | `RABB` 全部条目在 Region 路径中出现次数为 **0**（`RAB36`） |
| **RABV3** | **输出封闭** | Region 只产出 `RAB-O1` / `RAB-O2`；`RAB43`–`RAB51` 命中为 **0** |
| **RABV4** | **三方归属正确** | `Base` 不被 Region 写、`Modifier` 仅 Region 写、`Final` 仅 WorldState 合成（`RAB17`；`RA75`） |
| **RABV5** | **不生成** | Region 求值前后：体素真值不变、`ChunkCount` 不变、无对象创建（`RAB56`/`RAB57`/`RAB65`） |
| **RABV6** | **唯一路径** | 影响体素的路径只有 §4.3 一条（`RAB53`；`RA101`） |
| **RABV7** | **确定性** | 同 `(Seed, Position)` 逐字段相同；多实例/跨进程一致（`RAB89`/`RAB91`） |
| **RABV8** | **非平凡性** | 至少一处 `Seed`/显式参数变化引起 Region 或 Modifier 变化（`RAB92`） |
| **RABV9** | **退化等价** | 关闭 Region 层 ⇒ `Final == Base` 且体素输出逐体素一致（`RAB25`；`RA104`） |
| **RABV10** | **Legacy 隔离** | 结构扫描中 Legacy 符号命中 = **0**；Legacy 基线 SHA 不变（`RAB86`；`RAB-L1`–`RAB-L5`） |
| **RABV11** | **权限矩阵自查** | 下游列无 `P`/`O`；`Runtime`、`WorldQuery` 列全 `R`（`RA140`–`RA142`） |
| **RABV12** | **声明性** | 本文档存在/缺失不改变任何输出（零修改声明） |

---

## §10 变更规则

### 10.1 允许的变更

| # | 规则 |
|---|---|
| **RAB96** | 细化"未来扩展边界"（§6）的登记内容、补充引用、更新 §7 Legacy 基线：**允许**，且必须同步更新 §9 |
| **RAB97** | Region 来源类别、`RegionType` 取值、修正分量、域分配的新增/变更：**必须**由 `Region_Design.md` / `Region_Architecture_Contract.md` / `WorldParameter_Contract.md` **先行升版本**（`RAB3`/`RAB80`/`RAB81`） |
| **RAB98** | 输入白名单新增条目：**必须**有上游契约条款依据（`RAB31`） |

### 10.2 违规判定

| # | 规则 |
|---|---|
| **RAB99** | `RAB36`/`RAB52` 的违规是**架构缺陷**，不得以"不影响结果""仅调试""临时实现"豁免（`RA7` 同族纪律） |
| **RAB100** | 违反 `RAB25`（退化等价）或 `RAB83`（Legacy 双向隔离）视为**最高级别缺陷** |

### 10.3 已登记差异（本轮）

| # | 登记项 | 内容 | 依据 | 处置 |
|---|---|---|---|---|
| **RAB101** | 任务书表述"Region 允许读取**已登记 WorldParameter**"与既有契约的**范围差异** | 既有契约只允许：`Elevation`/`Ocean`（只读）+ `Balance`（定义复用）；**`FinalState` 为 Forbidden Read**（未定稿，读它构成环） | `Pipeline` Phase 3；`Region_Design.md` §12.2；`PC19`/`PC21`；`RAB19`/`RAB32` | 本文档按既有契约**收紧**并登记为 `[待确认]`；**不自行放宽** |
| **RAB102** | 任务书扩展方向"**Region → History**"与既有冻结方向的差异 | 既有方向是 `History → RegionSourceCandidate → Region`（History 是输入）；`Region → History` 的**写**被禁止 | `Region_Design.md` §6.1/§6.3；`RH1`–`RH5`；`PC26` | §6.2 登记方向澄清；只保留"只读归因"为 `[待确认]`，**不实现** |
| **RAB103** | 任务书扩展方向"**Region → Civilization**" | `CivilizationPresence` Owner = History；Region 无写权限 | 附录 B 序 13；`PW2`；§6.3 | 登记为 `[待确认]`（只读归因），**不实现** |
| **RAB104** | 术语"Region Context" | 既有契约中该名称对应 Phase 3 的**输出聚合**（含 Region 集合 / `RegionModifier` / 只读标签 / 资格声明）；`RegionModifier` 为其关键成分 | `Pipeline` Phase 3 Output；§11 权限矩阵 | 本文档按此口径使用，**不新增**产物类型 |
| **RAB105** | "Mining" 一词的双重来源 | 既有 V2 侧存在 Mining 规范，Legacy 侧存在 `TerrainMining`/玩家挖掘 | `RABB5`；`RAB-L4`；`Legacy_Status.md` | 本文档按"**任何挖掘状态/结果均不得作为 Region 输入**"统一处理 |

---

## 附录 A：边界数据流图

```
   ┌──────────────────────────────────────────────────────────────┐
   │ 白名单输入（RABW）                                            │
   │  Seed · 世界坐标 · Elevation/Ocean（只读）· Balance 定义复用   │
   │  骨架事实 · History 事实与 HistoryTags（只读）· 显式 Region 参数│
   └───────────────────────────┬──────────────────────────────────┘
                               ▼
        ┌──────────────────────────────────────────────┐
        │ Region（空间解释层 · 原因侧）                 │
        │  来源 → 采纳（RA*）→ Region Context（只读）    │
        │  输出：Region Context + RegionModifier（6 分量）│
        └───────────────────────────┬──────────────────┘
                                    ▼
        ┌──────────────────────────────────────────────┐
        │ WorldState（合成 Owner）                      │
        │  FinalState = clamp01(Base + RegionModifier)  │
        └───────────────────────────┬──────────────────┘
                                    ▼
        Biome（唯一规则表）→ Geology 的 P3 地表材料 → Voxel（唯一体素路径 · RAB53）
                                    │
        只读消费：Biome · Geology(P3) · Ecology · Resource · WorldQuery · Runtime

  ═════ 禁止 ═════
   输入侧（RABB）：Runtime · Player · Entity · Mesh · Mining · Voxel 修改结果 ·
                   FinalState · Biome/Geology/Resource/Ecology 结论 · Chunk · Scene ·
                   Random/Time · 加载顺序 · 保留域 · Legacy 任何类型
   输出侧（RAB43–RAB51）：Voxel · Entity · Resource · Biome Result · Geology 产物 ·
                   地形量 · Chunk/Mesh/调度 · 存档 · 第 7 分量
   反向（RAB26–RAB30）：下游 → Modifier / Region；结果反推；Runtime 回写；自举
   Legacy（§7）：双向禁止（RAB83）
```

---

## 附录 B：禁止清单速查

| 编号 | 若出现… | 判定 |
|---|---|---|
| **RABX-1** | Region 读取 `FinalState` | 违反 `RAB19`/`RAB32`；`PC19`/`PC21` |
| **RABX-2** | Region 读取 Runtime / 加载状态 / 脏标记 | 违反 `RABB1`；`RA95` |
| **RABX-3** | Region 读取 Player / 挖掘 / 已采集状态 | 违反 `RABB2`/`RABB5`；`RA121` |
| **RABX-4** | Region 读取 Entity / 对象状态 | 违反 `RABB3`；`RA8` |
| **RABX-5** | Region 读取 Mesh / 几何 / 材质 | 违反 `RABB4`；`Region_Design.md` §12.3 |
| **RABX-6** | Region 读取体素修改结果 | 违反 `RABB6`；`RA7` |
| **RABX-7** | Region 读取 Biome / Geology / Resource / 生态结论 | 违反 `RABB7`–`RABB9`；`RA7` |
| **RABX-8** | Region 读取 Chunk 状态 / 局部坐标 | 违反 `RABB10`；`RA93`–`RA96` |
| **RABX-9** | Region 使用 `Random` / `Time` / 加载顺序 | 违反 `RABB12`–`RABB14`；`RA119`–`RA122` |
| **RABX-10** | Region 复用保留域 `R-1`/`R-2` | 违反 `RABB15`；`RA117`/`PZ12` |
| **RABX-11** | Region 写体素 / 改材料 / 改高度 | 违反 `RAB43`/`RAB48`；`RA99`/`RA69` |
| **RABX-12** | Region 生成对象 / 结构 / 植被 / 生物 | 违反 `RAB44`；`RA8`/`RGS` |
| **RABX-13** | Region 产出资源判定 / 群系结论 | 违反 `RAB45`/`RAB46`；`RA92`/`RAB12` |
| **RABX-14** | 下游写 `RegionModifier` / 创建 Region | 违反 `RAB26`/`RAB27`；`RA75`/`RA20` |
| **RABX-15** | 由结果反推修正 / `RegionType` 参与判定 | 违反 `RAB28`；`RA79`/`RA85` |
| **RABX-16** | 新增第 7 个修正分量 | 违反 `RAB51`；`RA68` |
| **RABX-17** | 按 Chunk 组织 Region 或对齐边界 | 违反 `RAB15`；`RA93`/`RA94` |
| **RABX-18** | 世界级前置（全量 Region 表 / 全局注册表） | 违反 `RABB17`；`RA131`–`RA135` |
| **RABX-19** | Region 依赖 Legacy 任何类型/产物 | 违反 `RAB83`/`RAB-L2`；`Legacy_Status.md` |
| **RABX-20** | 关闭 Region 后输出发生变化 | 违反 `RAB25`；`RA104`/`RI3` |

---

## 附录 C：与本文档直接相关的既有条款索引

| 主题 | 既有条款 |
|---|---|
| Region 定位与范围 | `RA1`–`RA8`；`Region_Design.md` §1.1/§1.2/§1.4 |
| Region 身份与形态 | `RA9`–`RA20`；`RT1`–`RT6` |
| 候选（输入侧） | `RA21`–`RA30`；§3.1/§3.2 |
| `Modifier` 合成与权限 | `RA62`–`RA79`；`WSC9`–`WSC14`；`PM23`–`PM34`；`PC19`/`PC20`/`PC24` |
| Chunk / Voxel 边界 | `RA93`–`RA104`；`RI1`–`RI4`；`RV1` |
| 确定性与局部求值 | `RA115`–`RA139`；`RAV3`/`RAV4` |
| 消费者权限矩阵 | `RA140`–`RA144` |
| V1 禁令 | `RA145`–`RA152`；`RAX-1`–`RAX-16` |
| 参数侧反向禁令 | `WPS18`；`WSC56`；`PZ9` |
| Legacy 冻结基线 | `Legacy_Status.md`（Legacy 清单与 SHA 基线） |
