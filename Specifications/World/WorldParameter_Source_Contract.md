# WorldParameter Source Contract V1

> **文档性质**：**来源边界契约规范（Source Boundary Contract）**。本文档冻结 **WorldParameter 各参数的来源边界**：每个参数的**当前实现来源**、**临时性质**、**未来替换方向**、**允许读取对象**与**禁止读取对象**。这里的“当前实现”必须以附录 C 的代码符号和实际实现为准，不表示所有已登记参数均已接入；“Phase 1”如出现在历史登记中，指参数系统早期实现里程碑，而非世界生成 Pipeline 的 Phase 1（World Skeleton Generation）。
>
> **维护说明**：本文档定义参数来源边界与依赖方向，不以历史任务的修改范围限制后续维护。文中“Phase 1 临时来源”指参数系统早期实现里程碑中的占位来源登记，不等同于 World Generation Pipeline 的 Phase 1（World Skeleton Generation）。
>
> **本文档不是实现规范**。**不定义**：数值、阈值、噪声公式、派生公式的最终形式；**只定义来源边界与依赖方向**。
>
> **核心定位**：
>
> | 项 | 内容 |
> |---|---|
> | **它是什么** | **参数来源与读取边界的登记契约**：谁产生、允许读什么、禁止读什么、当前哪些是临时来源、将来怎么替换 |
> | **它不是什么** | **不是参数身份权威**（= `WorldParameter_Contract.md` 附录 B）；**不是参数语义权威**（= `WorldState_Design.md`）；**不是求值契约**（= `WorldState_Runtime_Contract.md`）；**不是生成器设计**（不产生世界、不产生体素、不产生对象） |
>
> **必须读取的上游规范**（只读；**不得修改**）：
>
> | 规范 | 与本契约的关系 |
> |---|---|
> | `WorldParameter_Contract.md` | **身份/量纲/范围/Owner/权限权威**：§3.2 表 A（来源列、可修改层、可读取层）、§3.4 表 C、§3.5 表 D、§4 矩阵与 `PW1`–`PW10`、§5.2 范围冻结、§6.2 禁止事项（`PZ5`–`PZ9`）、§6.3/§6.4 域、**附录 B（`ParameterId` 分配权威）**、附录 C 变更规则 |
> | `WorldState_Runtime_Contract.md` | **求值与来源契约**：§2.1 最终公式、§2.2 逐参数合成类型、§2.3 求值内部顺序、§3.1 白名单（`WSC25`–`WSC29`）、§3.4 `Fertility` Base 输入（`WSC33`–`WSC36`）、`WSC19`/`WSC20`、`WSC56`、`WSC58`/`WSC78`、`WSC103` |
> | `WorldParameter_Runtime_Model.md` | **状态与消费者契约**：`PM12`/`PM19`/`PM20`（身份与封闭集合）、`PM27`–`PM34`（三态、不可变）、`PM37`–`PM46`（定长索引模型）、§5 坐标求值（`PM51`–`PM57`）、§7 消费者读取集 |
> | `WorldGeneration_Pipeline_Contract.md` | **阶段与写入权**：Phase 2 `World Parameter Evaluation` 的 Input / Allowed Read / Forbidden Read / **Forbidden Write**、`PC19`（两段式发布）、`PC20`/`PC21`/`PC22`、`PC43`/`PC53`（Runtime → Generation 禁止）、`PC86`/`PC87` |
> | `WorldV2_Phase1_PreImplementation_Decisions.md` | 已冻结决策 `D1`–`D5`、`DR21`（允许读取面）、`DR25`（`BaseValue` 由 WorldState 层产生）、`DR30`、`DR35`（禁第二套求值路径）、`DR17`/`DR18`（Chunk / Scene 不参与） |
> | `Region_Architecture_Contract.md` / `Region_Design.md` | `RA69`/`RE1`（`Elevation`/`Ocean` 不在修正集内）、`RA75`/`RA76`（`RegionModifier` 唯一写入者 = Region；WorldState 只合成）、`RA79`（禁结果反推修正）、`RA70`（`Balance` 不得被直写）、`RH3`（参数求值禁止读标签） |
> | `Biome_Architecture_Contract.md` / `Geology_Architecture_Contract.md` / `Resource_Architecture_Contract.md` / `Ecology_Design.md` | `BC27`/`BC30`（Biome 只读**已合成** `FinalState`，输入封闭）、`BC122`（Biome 无写权限）、`GC25`/`GC75`（Geology **不得直读** 8 参数，只能经 Biome）、`WSC56`（Resource → 参数 禁止）、`RN7`、`EF1`（Entity 不得改变生成规则） |
>
> **冲突时的优先级**：参数**身份/量纲/Owner/权限**以 `WorldParameter_Contract.md` 为准；参数**语义**以 `WorldState_Design.md` 为准；**求值边界与顺序**以 `WorldState_Runtime_Contract.md` 为准；**阶段顺序与写入权**以 `WorldGeneration_Pipeline_Contract.md` 为准；**各区来源规则**以各层契约（Region / Biome / Geology / Resource / Ecology）为准。**本契约只登记"来源与读取边界"**，不改变上述任何权威。
>
> **零修改声明**：本文档是**声明性文档**，不产生任何运行时效果；开启/关闭本文档**不得改变任何体素输出**。与本契约相关的既有规范差异一律在 **§8 / 附录 C 登记**，**不修正**任何既有文件。

---

## §1 定位与范围

### 1.1 定义

| # | 规则 |
|---|---|
| **WPS1** | **定义**：WorldParameter Source Contract = **参数来源边界的登记契约**——对每个已登记参数登记"当前来源 / 临时性质 / 未来替换方向 / 允许读取 / 禁止读取"，并冻结**依赖方向**与**五条硬边界** |
| **WPS2** | **适用对象**：`WorldParameter_Contract.md` **附录 B** 登记的全部参数（序 1–20）。其中**环境参数组（序 1–8）**是 Phase 1 已实现求值的部分，其余为"已登记、未接入" |
| **WPS3** | **引用而非复制**：本文档对身份、量纲、范围、Owner、权限矩阵**一律引用**，**不重述**为第二权威（`PM12`/`PM16`/`PW3`） |
| **WPS4** | **登记口径**："当前来源"= **已落地**（有实现符号可定位）的来源；"临时性质"= 该来源是**占位**、**契约默认值**还是**已按契约实现**；"未来替换方向"= 契约已冻结的目标来源 |
| **WPS5** | **可定位性**：每条"当前来源"必须能对应到实现符号（附录 C 给出实现对应表），不得出现"来源不明"的参数 |

### 1.2 **不是**什么

| # | 规则 |
|---|---|
| **WPS6** | **≠ 参数身份权威**：不新增、不删除、不改名、不改号任何参数；`ParameterId` 分配权威始终是附录 B（`PM12`/`PM15`） |
| **WPS7** | **≠ 参数语义权威**：不定义参数含义、阈值、取值范围、默认值（归 `WorldParameter_Contract.md` §3/§5 与 `WorldState_Design.md`） |
| **WPS8** | **≠ 求值契约**：不定义求值顺序、合成公式、钳制语义（归 `WorldState_Runtime_Contract.md` §2/§3） |
| **WPS9** | **≠ 生成设计**：不产生世界结构、不产生体素、不产生对象；不定义任何算法（§4） |
| **WPS10** | **≠ 各层规则权威**：Biome / Geology / Resource / Ecology 的来源规则归各自契约；本文档只登记**参数层与它们的边界** |

### 1.3 三条硬声明

| # | 规则 |
|---|---|
| **WPS11** | **不新增参数**：本文档的参数集合**恒等于**附录 B（20 项）；任何新增/删除必须走 `WorldParameter_Contract.md` **附录 C** 升版本，且不得由本文档先行 |
| **WPS12** | **不修改既有契约**：本文档只读引用上游规范；发现差异一律**登记**（§8.3），**不修正** |
| **WPS13** | **不修改代码**：本文档不要求任何代码变更；若实现与本文档登记不一致，按 **§8.2** 登记为"实现待对齐项"，由后续实现轮次处理 |

### 1.4 与既有文档的分工

| 问题 | 权威文档 | 本文档的作用 |
|---|---|---|
| 参数是谁、几号、什么范围、谁能改 | `WorldParameter_Contract.md` | 引用（不重述） |
| 参数值怎么算、怎么合成、什么顺序 | `WorldState_Runtime_Contract.md` / `WorldState_Design.md` | 只登记**来源边界**与**临时性** |
| 参数怎么表示、怎么被读 | `WorldParameter_Runtime_Model.md` | 引用消费者读取集 |
| 每个阶段允许读/写什么 | `WorldGeneration_Pipeline_Contract.md` | 细化到**逐参数来源**与**替换方向** |
| 各层自己的来源规则 | Region / Biome / Geology / Resource / Ecology 契约 | 只登记边界，不复制规则 |

---

## §2 依赖方向（唯一方向）

### 2.1 正向链（冻结）

```
Seed
  ↓
Parameter                         （WorldState 层求值：Base + RegionModifier ⇒ FinalState）
  ↓
Biome / Geology / Resource / Ecology
```

| # | 规则 |
|---|---|
| **WPS14** | **唯一方向**：`Seed → Parameter → 下游层`。参数是**上游产物**，下游层只读消费 |
| **WPS15** | **精度说明（不改变方向）**：下游的读取方式并不完全同构——`Biome` / `Ecology` / `Resource` 直接读**已定稿的** `FinalState`（`BC27`；表 A 可读取层列）；**`Geology` 不得直读 8 参数**，只能经 `Biome` 压缩后间接进入（`GC25`/`GC75`）。方向不变，路径不同 |
| **WPS16** | **参数不得被下游反推**：禁止由下游结论反推参数值（`PZ9`；`RA79`） |
| **WPS17** | **同一性**：查询参数与生成使用**同一规则**（`WQ1`/`WSC67`），禁止第二套求值路径（`DR35`/`WSC103`） |

### 2.2 反向禁令（全部禁止）

| # | 反向路径 | 判定 | 依据 |
|---|---|---|---|
| **WPS18** | `Biome → Parameter` | **禁止**：Biome 无参数写权限，只读已合成 `FinalState` | `BC27`/`BC122`；`PZ9`；表 A 可修改层列 |
| **WPS19** | `Resource → Parameter` | **禁止**：Resource 判定不得回写或驱动任何参数 | `WSC56`；`RN7`；`PW2` |
| **WPS20** | `Runtime → Parameter` | **禁止**：Runtime 对三态只有只读权限；不得回写 Generation | `PC43`/`PC53`；`PM55`；`WSC58`/`WSC78` |
| **WPS21** | `Geology → Parameter` | **禁止**：Geology 结论（`Density`/`Material`/`CaveDensity`/`DepositZone`）不得作为参数输入 | `WSC26`；`PA1`；`GC52` |
| **WPS22** | `Ecology → Parameter` | **禁止**：生态候选不得回写参数 | `WSC19`；`EF1` |
| **WPS23** | `Entity → Parameter` | **禁止**：任何对象/结构/生物状态不得参与参数求值 | `PM57`；`EF1` |
| **WPS24** | `Chunk / Scene / Player → Parameter` | **禁止**：Chunk 状态、场景对象、玩家状态均不得参与 | `DR17`/`DR18`；`PC86`；`WSC58` |
| **WPS25** | `History Tag → Parameter` | **禁止**：`HistoryTags` 只用于归因/资格，不得参与参数求值 | `WSC28`；`RH3`；`RZ2` |
| **WPS26** | **违反即缺陷** | 上述任一路径在实现中出现（含"只读一次作为快捷方式"），一律判为**架构缺陷**，不得以"不影响结果"为由豁免 | 本契约；`PA1`/`WSC25` |

### 2.3 下游读取方式（只读）

| # | 规则 |
|---|---|
| **WPS27** | 下游只能读**已定稿的** `FinalState`；不得读未合成的 `Base` 代替 `FinalState`，也不得读 Region 内部字段（`PC21`；`BC27`） |
| **WPS28** | 参数对下游是**只读**的：任何下游层都不得持有写入口（表 A 可修改层列只出现 `WorldState` / `Skeleton` / `History` / `配置` / `无`） |
| **WPS29** | 查询结果（含参数快照）是**终端产物**：不得成为任何生成/求值环的输入（`WQ4`；`PC104`） |

### 2.4 可交换性

| # | 规则 |
|---|---|
| **WPS30** | 参数求值**不依赖**下游求值顺序：同族下游之间可交换，均以 `FinalState` 为输入（`RC83` 同构纪律） |
| **WPS31** | 参数求值**不得**因为"某个下游是否已求值/已加载"而改变（`WSC92`；`WL8`；`PZ4`/`PZ8`） |

---

## §3 参数来源边界总表（逐参数）

> 说明：本节是本文档的**核心登记**。为可读性，每个参数组拆为**来源表**（当前来源 / 临时性质 / 未来替换方向）与**读取边界表**（允许 / 禁止）。
> 黑名单以 `WPSB`（§6.2）**整体引用**，避免逐格重复；白名单以 `WPSW`（§6.1）为基。

### 3.1 环境参数组（附录 B 序 1–8）

**表 A1：来源与临时性**

| 参数（序） | Owner（引用 §3.2 表 A） | 当前 Phase 1 来源（已落地） | 临时性质 | 未来替换方向 |
|---|---|---|---|---|
| `Temperature`（1） | WorldState | 域 1（`Skeleton`）标签整数哈希归一化到 `[0,1)` | **占位**：骨架气候带未接入；**无空间连续性**，与真实气候语义无关 | 步骤 2：`TemperatureBase`（配置，默认 `0.5`）+ 骨架基础气候带；域 = §6.4 域 1 |
| `Moisture`（2） | WorldState | 域 1 标签整数哈希归一化到 `[0,1)` | **占位**（同 `Temperature`） | 步骤 2：`MoistureBase`（配置，默认 `0.5`）+ 骨架基础气候带 |
| `Elevation`（3） | 高度场（Skeleton / Geology 共用）· **转发量** | 域 1 标签整数哈希 → 整数区间 `[0,127]` | **占位 + 临时域标签**：契约 §6.4 域 1 覆盖清单**未含** `Elevation`；占位高度跨度 `128` **不是登记值** | 步骤 0：**只读转发**既有高度场（`HeightFunction.GetTerrainHeight` / `GeologySampler.SurfaceHeightAt`）+ 骨架大陆掩码叠加（`WC2`）；整数体素 `[0, WorldHeight-1]`；**终态 = 只读转发且不使用任何参数域**（`Elevation` 的域依赖被**移除**；**不得**以扩充 §6.4 域 1 覆盖清单替代） |
| `Fertility`（4） | WorldState | 占位派生：`clamp01(Base.Temperature × Base.Moisture)`（只读步骤 2 的 Base 级值） | **占位公式**：未使用**已登记**输入 `WorldGenerationSettings.SoilDepth`（默认 `3`，`WSC27`） | 步骤 3：`f(配置级 SoilDepth, Base 级 Temperature, Base 级 Moisture)`（`WSC33`：只冻结输入集合；`WSC35`：不得读除 `Ocean` 外的其它地理量） |
| `MagicDensity`（5） | WorldState | 常量 `0`（= 表 A 登记默认值） | **契约默认值**（非随机占位）：真实 `Base` 即"全局基底常量（默认 `0`）"（§2.2） | 步骤 4：全局基底常量（可配置化）+ `RegionModifier`（Phase 3 供给，`PC20`） |
| `HolyInfluence`（6） | WorldState | 常量 `0`（= 表 A 登记默认值） | 同上 | 同上（圣性来源修正由 Region 提供） |
| `CorruptionInfluence`（7） | WorldState | 常量 `0`（= 表 A 登记默认值） | 同上 | 同上（腐化来源修正由 Region 提供） |
| `Ocean`（8） | Skeleton（派生规则） | **派生已按契约实现**：`Elevation < SeaLevel`（`SeaLevel` 取登记默认值 `64`） | **派生形式非临时**；唯一临时量 = `SeaLevel` 常量（真实值来自**显式配置**） | 步骤 1：`Elevation < SeaLevel（配置读取）`；布尔语义的**表示法**属登记缺口，本契约不裁决 |

**表 A2：读取边界**

| 参数 | 允许读取对象（白名单为 `WPSW` 的子集） | 禁止读取对象 |
|---|---|---|
| `Temperature` | `Seed`；世界坐标；`TemperatureBase`（配置，未接入）；骨架基础气候带（未接入）；域 1 | `WPSB` 全部 |
| `Moisture` | `Seed`；世界坐标；`MoistureBase`（配置，未接入）；骨架基础气候带（未接入）；域 1 | `WPSB` 全部 |
| `Elevation` | **只读高度场**（未接入）；骨架大陆掩码（未接入）；`Seed`；世界坐标 | `WPSB` 全部；**另加**：任何 `RegionModifier`（`RE1`/`RA69`）、任何修正写入、插值（`WSC13`） |
| `Fertility` | **Base 级** `Temperature` / `Moisture`（`WSC17`）；配置级 `SoilDepth`（`WSC27`，未接入）；`Seed`；世界坐标 | `WPSB` 全部；**另加**：`FinalValue` 级 `Temperature` / `Moisture`、除 `Ocean` 外的其它地理量（`WSC35`） |
| `MagicDensity` | 全局基底常量（默认 `0`）；`RegionModifier`（**仅合成阶段**由 WorldState 消费，`RA75`/`RA76`） | `WPSB` 全部 |
| `HolyInfluence` | 全局基底常量（默认 `0`）；`RegionModifier`（仅合成阶段） | `WPSB` 全部 |
| `CorruptionInfluence` | 全局基底常量（默认 `0`）；`RegionModifier`（仅合成阶段） | `WPSB` 全部 |
| `Ocean` | 步骤 0 的 `Elevation`；`SeaLevel`（配置） | `WPSB` 全部；**另加**：任何 `RegionModifier`（`RE1`/`RA69`）、插值（`WSC13`） |

### 3.2 其余已登记参数（附录 B 序 9–15，**未接入**）

> 本契约**不为其定义来源规则**（归各 Owner 规范），只登记"参数层不得代其求值、不得读取、不得改写"的边界。

**表 B1：状态与来源方向**

| 参数（序） | Owner（引用表 A / 表 C） | 当前 Phase 1 来源 | 临时性质 | 未来替换方向（权威） |
|---|---|---|---|---|
| `Continent`（9） | Skeleton | **未接入**（不在 8 参数组内） | — | Skeleton 大陆函数（`Seed` + 显式参数）：`WorldState_Design.md` §7.4 `WC1`–`WC6` |
| `Epoch`（10） | History | **未接入** | — | History（`Seed` + 显式纪元划分）：`WorldHistory_Design.md` `HE1`–`HE4` |
| `Ordinal`（11） | History | **未接入** | — | History（序位由 `Seed` 派生）：`HE1`/`HE6` |
| `HistoryScale`（12） | History | **未接入** | — | History 配置化：`HN9`/`HN10` |
| `CivilizationPresence`（13） | History | **未接入** | — | History：`WorldHistory_Design.md` §3 |
| `Slope`（14） | Skeleton | **未接入** | — | 由 `Elevation` 梯度派生：`WorldState_Design.md` §7.4 |
| `Balance`（15） | WorldState（**派生只读**） | **未接入** | — | 派生：`HolyInfluence − CorruptionInfluence`（`FinalState` 级；`WSC §2.3` 步骤 7；`WSC11` 不得被任何 `Modifier` 直写） |

**表 B2：读取边界**

| 参数 | 允许读取对象 | 禁止读取对象 |
|---|---|---|
| `Continent` / `Slope` | `Seed`；世界坐标；Skeleton 显式参数；`Elevation`（`Slope` 派生用） | `WPSB` 全部 |
| `Epoch` / `Ordinal` / `HistoryScale` / `CivilizationPresence` | `Seed`；世界坐标；History 显式参数 | `WPSB` 全部；**另加**：参数求值**不得读** `HistoryTags`（`WSC28`/`RH3`）——反向亦然 |
| `Balance` | `FinalValue` 级 `HolyInfluence` 与 `CorruptionInfluence`（`WSC18`） | `WPSB` 全部；**另加**：任何 `Modifier` 直接写入（`WSC11`；`RA70`） |

### 3.3 既有层间量（附录 B 序 16–20，**不是**参数层的量）

| 序 | 量 | Owner | 本契约登记的边界 |
|---|---|---|---|
| 16 | `Density` | Geology | 参数层**不得读取**（`WSC26`）；参数层**不得写入**（`PW6`）；**不得**反向驱动参数（`WPS21`） |
| 17 | `Material` | Geology | 同上 |
| 18 | `CaveDensity` | Geology | 同上 |
| 19 | `DepositZone` | Geology | 同上；与 `ResourcePresence` **分层**，禁止混用（契约 §3.5 术语冻结） |
| 20 | `ResourcePresence` | Resource | 参数层**不得读取**、**不得写入**；**不得**反向驱动参数（`WSC56`；`RN7`） |

| # | 规则 |
|---|---|
| **WPS32** | 序 16–20 的量**不属于** WorldParameter；本文档只登记其**与参数层的边界**，不改变其既有权威（契约 §3.5：表 D"只登记，不新增权威"） |
| **WPS33** | 参数**不决定**序 16–20 的任何取值；它们由 Geology / Resource 按各自契约判定（§4.4） |

---

## §4 五条硬边界

### 4.1 Parameter 不拥有世界生成权

| # | 规则 |
|---|---|
| **WPS34** | 参数层**不产生**世界结构：大陆、气候带、地形高度、骨架掩码一律**不由**参数层决定；参数层只**求值环境参数** |
| **WPS35** | Phase 2（`World Parameter Evaluation`）的 **Forbidden Write** 为"除参数求值结果之外的一切"：不得写 Region、不得写骨架、不得写体素（`Pipeline` Phase 2；`PC19`） |
| **WPS36** | 参数层的输出形态**只能是** `WorldParameterState`（三态快照）；不得返回世界结构、Region 集合、群系或任何下游产物（`PM35`；`PC21`） |

### 4.2 Parameter 不生成 Voxel

| # | 规则 |
|---|---|
| **WPS37** | 参数**不得被任何系统写入 Voxel**：`Density` / `Material` 的写入属 Geology 判定链，不是"参数写入"（`PW6`） |
| **WPS38** | 参数求值**不得触发**任何 Runtime 行为：不写体素、不创建 Chunk、不置脏、不生成 Mesh、不生成对象（`WSC20`；`WSC64`；`T4`） |
| **WPS39** | 正确链中**没有参数**：`Geology → Density Evaluation → Voxel Query → Mesh`（`GC52`/`GC53`）；参数只能经 `Biome` 影响 **P3 地表材料**（`WSC51`；`GC66`） |
| **WPS40** | 可机械验证：参数求值前后 **`ChunkCount` 不变、体素真值不变**（`WSV16` 同构） |

### 4.3 Parameter 不生成 Entity

| # | 规则 |
|---|---|
| **WPS41** | 参数求值**不得生成任何对象**（结构/生物/道具/遗迹实体）（`WSC20`；`T4`） |
| **WPS42** | 参数**不得读取**任何 Entity / 对象状态（`PM57`；`EF1`/`ADR-001`） |
| **WPS43** | `CivilizationPresence` 等历史标记只是**参数**，不构成实体；参数层不得因"文明存在"而生成对象（`WPS9`） |

### 4.4 Parameter 不决定资源

| # | 规则 |
|---|---|
| **WPS44** | 资源判定层（`ResourcePresence`）的 Owner 是 **Resource**，不是参数层（契约 §3.5 表 D） |
| **WPS45** | 参数只能作为资源的**只读输入**参与资格条件（表 B：Resource 可读取层）；**不得**决定资源存在性 |
| **WPS46** | **禁止反向**：`Resource → 任何参数`（`WSC56`）；禁止由资源结论反推参数（`PZ9`） |
| **WPS47** | Biome 结论**不得**作为资源判定输入（`RN7`）——该禁令保护的是"结论不得反哺判定"，与参数方向一致 |

### 4.5 Parameter 不读取 Runtime

| # | 规则 |
|---|---|
| **WPS48** | 参数求值**不得读取** Runtime 任何状态：脏标记、加载集合、预算、帧序、调度状态（`PM55`；`PC4`/`PX4`） |
| **WPS49** | **禁止** `Runtime → Generation` 回写（`PC43`/`PC53`）；Runtime 对三态**只有只读权限**（`PM30`；`PX2`） |
| **WPS50** | **禁止**参数的"历史快照/版本链"：参数无时间维度，"上一次求值时的值"不得参与任何判定（`WSC66`） |
| **WPS51** | 玩家编辑**不得**改变参数：同一 `(Seed, Position)` 在玩家操作前后逐字段不变（`WSC58`；`WSC78`；`WSV14`） |

---

## §5 Phase 1 临时来源登记（快照）

### 5.1 占位来源清单

| 参数 | 临时来源（实现） | 类别 | 到期条件（替换判据） |
|---|---|---|---|
| `Temperature` | 域 1 标签整数哈希 → `[0,1)` | 占位 | 骨架基础气候带 + 配置读取落地（Phase 1 `World Skeleton`） |
| `Moisture` | 域 1 标签整数哈希 → `[0,1)` | 占位 | 同上 |
| `Elevation` | 域 1 标签整数哈希 → `[0,127]` | 占位 + 临时域标签 | **终态**：只读高度场转发接线（`Elevation` **不再使用任何参数域**；**不得**以扩充 §6.4 域 1 覆盖清单替代）。**本轮仅更新计划与契约状态：代码接线未完成**（实现仍为域 1 占位） |
| `Fertility` | `clamp01(Base.Temperature × Base.Moisture)` | 占位公式 | 契约 §1.3 派生规则实现（含 `SoilDepth` 输入） |
| `Ocean` | `Elevation < 64` | **非临时**（派生形式已按契约）+ 临时常量 `SeaLevel` | `SeaLevel` 改为显式配置读取 |

| # | 规则 |
|---|---|
| **WPS52** | 占位来源**只保证确定性**，不保证语义正确；不得据此推导任何世界结论（不得被下游当作气候/高度真值使用） |
| **WPS53** | 占位出处在代码中**必须可定位**（附录 C）；替换时必须**同步更新本文档 §5** |
| **WPS54** | 占位**不得**演化成"事实标准"：不得为占位值增加阈值、缓存、下游依赖（`PZ9`/`WSC103`） |
| **WPS54a** | **本轮状态（不变量）**：`Elevation` 的域依赖替换**只在计划与契约状态层面更新**（`WPS55`–`WPS57`）；**代码接线未完成、`WorldParameterEvaluator` 未修改**。`Continent`（9）与 `Slope`（14）**保持"未接入"**，直至实现与验证完成（不得在文档中预先记为已接入） |

### 5.2 临时常量与临时域标签登记

| # | 登记项 | 现状 | 性质 | 替换方向 |
|---|---|---|---|---|
| **WPS55** | `Elevation` 占位高度跨度 `128` | 硬编码常量 | **非登记值**（`WorldHeight` 的真实来源是 `WorldGenerationSettings`） | 读取 `WorldGenerationSettings.WorldHeight`；占位常量删除 |
| **WPS56** | `SeaLevel` 占位值 `64` | 硬编码常量 | **登记默认值**（契约表 C `SeaLevel` 默认 `64`） | 改为显式配置读取（值本身可不变） |
| **WPS57** | `Elevation` 使用**域 1**（`Skeleton`）标签 | 硬编码常量 | **契约 §6.4 覆盖清单未含 `Elevation`**（真实 `Elevation` 是转发量、不需要域） | **终态**：`Elevation` 改为只读转发后**移除**域标签；**§6.4 登记不变**（**不得**把"把 `Elevation` 加入域 1 覆盖清单"当作终态方案）。**该条记录对应的历史轮次仅更新计划与契约状态，未修改实现** |
| **WPS58** | `Modifier` 以 `0` 承载（含 `Elevation`/`Ocean`） | 表示形态 | **登记缺口**：`PM32` 要求转发/派生参数的 `Modifier` 表示为 **`None`**（非"修正 0"），`WSC10` 要求其不进入任何求和式 | 表示法决策属后续轮次（`B-3` 类登记）；本文档**不裁决** |

### 5.3 替换纪律

| # | 规则 |
|---|---|
| **WPS59** | 替换占位来源**不得**改变参数集合、`ParameterId`、求值顺序（`WSC §2.3`）与合成类型（`WSC §2.2`） |
| **WPS60** | 替换后必须重新满足 `WPSV1`–`WPSV8`（§7）全部验收 |
| **WPS61** | 替换**不得**新增第二套求值路径（`DR35`；`WSC103`）：查询入口与求值入口保持不变 |

---

## §6 读取白名单 / 黑名单（汇总）

### 6.1 白名单 `WPSW`（封闭）

| 编号 | 允许读取的对象 |
|---|---|
| **WPSW1** | `Seed` |
| **WPSW2** | **世界坐标**（唯一空间输入；`DR17`/`PC86`） |
| **WPSW3** | **已登记的显式配置**（如 `TemperatureBase` / `MoistureBase` / `SeaLevel` / `WorldHeight` / `SoilDepth`；来源 = `WorldGenerationSettings` 与契约 §3.2/§3.4） |
| **WPSW4** | **契约 §6.4 已登记的域**（当前仅域 1 = `Skeleton`，用于基础气候带部分） |
| **WPSW5** | **只读高度场**（`Elevation` 转发源；`DR21` 白名单） |
| **WPSW6** | **求值顺序中严格更早步骤**的 **Base 级**值（`WSC16`/`WSC17`） |
| **WPSW7** | `RegionModifier`（**仅合成阶段**由 WorldState 消费；Region 是唯一写入者，`RA75`/`RA76`） |

| # | 规则 |
|---|---|
| **WPS62** | 白名单**封闭**：表外量一律不得读取（`WSC25`；`PA1`；`BC30` 同构） |
| **WPS63** | `WPSW7` **不是**参数层的输入：参数层（`Base` 段）不得读取或推测 `RegionModifier`；它只在合成步骤参与（`WSC21`：Base 必须能在不计算任何 Region 的前提下求出） |
| **WPS64** | 白名单**不含**任何下游产物、任何 Chunk/Player/Scene/Runtime 状态、任何统计量（§6.2） |

### 6.2 黑名单 `WPSB`（整体禁止）

| 编号 | 禁止读取的对象 | 依据 |
|---|---|---|
| **WPSB1** | Biome 结论 / 群系地图 / 分类结果 | `WSC29`；`BC30` |
| **WPSB2** | Ecology 候选 / 资格声明 / 危险生物判定 | `WSC19`；`EF1` |
| **WPSB3** | Resource 判定（`ResourcePresence`）/ 各类资源结论 | `WSC56`；`RN7` |
| **WPSB4** | Geology 结论：`Density` / `Material` / `CaveDensity` / `DepositZone` / 层分类 | `WSC26`；`PA1` |
| **WPSB5** | 体素真值 / 已生成内容 / Chunk 状态 / Chunk 局部坐标 / 是否已加载 | `WSC19`；`PC86`/`PC87`；`GC86` |
| **WPSB6** | Runtime 状态：脏标记 / 加载集合 / 预算 / 帧序 / 调度 | `PM55`；`PC43`/`PC53` |
| **WPSB7** | Player 状态：位置 / 进度 / 背包 / 已探索 / 已采集统计 | `WSC58`；`PM56` |
| **WPSB8** | Entity / 对象 / 结构 / 生物状态 | `PM57`；`EF1`/`ADR-001` |
| **WPSB9** | `HistoryTags` / Region 候选 / Region 任何字段 / 资格声明 | `WSC28`；`RH3`；`RA82` |
| **WPSB10** | Scene / 场景对象 / `Transform` / 相机 / 渲染状态 | `DR18`；`WSC73` |
| **WPSB11** | `Random` / `System.Random` / 任何有状态伪随机源 | `PZ5` |
| **WPSB12** | `Time` / `DateTime` / 时间戳 / `Guid` / tick / 帧数 | `PZ6` |
| **WPSB13** | 加载顺序 / 遍历顺序 / 查询顺序 / 世界级中间态 | `PZ4`/`PZ8`；`WSC92` |
| **WPSB14** | 由下游结论反推的参数值（Biome/Resource → 参数） | `PZ9`；`RA79` |
| **WPSB15** | 保留域 `R-1`/`R-2`（`HeightFunction.Hash` / `GeologyNoise.LatticeHash`）的混合方式复用 | `PZ12` |
| **WPSB16** | 参数的"历史快照 / 版本链 / 上次求值结果" | `WSC66` |

### 6.3 结构扫描验收

| # | 规则 |
|---|---|
| **WPS65** | **输入封闭必须可机械验证**：对参数求值路径做**符号扫描**，出现的输入符号必须全部落在 `WPSW` 内（`WSC25`；`GIV6`/`WSV9` 同构） |
| **WPS66** | **反向读取必须可机械验证**：`WPSB` 的符号**不得出现**在参数求值路径的任何代码行（含注释外的引用） |
| **WPS67** | 扫描范围 = 参数求值入口及其可达的规则/哈希实现；扫描结果必须存档为验收证据（见 §7 `WPSV1`/`WPSV2`） |

---

## §7 验收规则

| 编号 | 验收项 | 判据 |
|---|---|---|
| **WPSV1** | **来源封闭** | 参数求值路径的输入符号 ⊆ `WPSW`（结构扫描，`WPS65`） |
| **WPSV2** | **无反向读取** | `WPSB` 符号在参数求值路径中出现次数为 **0**（`WPS66`） |
| **WPSV3** | **临时来源已登记** | §5 的每条占位/临时常量都能在实现中定位；实现中的每条占位都能在 §5 找到登记（**双向**，`WPS53`） |
| **WPSV4** | **不生成** | 参数求值前后：体素真值不变、`ChunkCount` 不变、无对象创建（`WPS37`–`WPS41`） |
| **WPSV5** | **参数身份不变** | 参数集合与 `ParameterId` **恒等于**附录 B（序 1–20）；无新增、无缺号、无改号（`WPS11`；`PM12`/`PM15`） |
| **WPSV6** | **确定性（`D5`）** | 同 `Seed` + 同世界坐标 ⇒ 逐字段相同；多次求值、两个独立实例/进程对照一致（`PZ1`；`PM52`；`IBP46`） |
| **WPSV7** | **只读** | 求值不写任何状态；Runtime 侧前后逐字段一致（`WPS48`/`WPS51`） |
| **WPSV8** | **依赖方向** | Biome / Geology / Resource / Ecology / Runtime 侧**不存在**"写参数"或"驱动参数"的规则（§2.2；`WPS18`–`WPS26`） |
| **WPSV9** | **声明性** | 本文档的存在/缺失**不改变**任何生成结果或体素输出（零修改声明） |
| **WPSV10** | **单一求值路径** | 查询与生成共用同一求值入口；不存在第二套参数求值（`WPS17`；`DR35`/`WSC103`） |

---

## §8 变更规则

### 8.1 允许的变更

| # | 规则 |
|---|---|
| **WPS68** | 替换占位来源、补充"未来替换方向"细节、更新附录 C 实现对应表：**允许**，且**必须**同步 §5 |
| **WPS69** | 参数的新增 / 删除 / 改名 / 改号 / 范围或 Owner 变更：**必须先**由 `WorldParameter_Contract.md` 附录 C 升版本（`PM15`；`PC16` 同构），本契约**不得先行** |
| **WPS70** | 白名单新增条目（`WPSW1`–`WPSW7`）：**必须**有上游契约条款依据，否则禁止 |

### 8.2 实现待对齐项的登记方式

| # | 规则 |
|---|---|
| **WPS71** | 实现与本文档不一致时：**登记**为"实现待对齐项"（注明参数、差异、依据条款），**不修改代码**、**不修改既有规范** |
| **WPS72** | 待对齐项必须在后续**实现轮次**关闭，且关闭后重新跑 `WPSV1`–`WPSV10` |

### 8.3 已登记差异（本轮）

| # | 登记项 | 差异 | 依据 | 处置 |
|---|---|---|---|---|
| **WPS73** | `Fertility` 占位公式未使用 `SoilDepth` | 实现 = `clamp01(T × M)`；契约允许输入含**配置级 `SoilDepth`**（默认 `3`） | `WSC27`；`WSC33` | 列为**实现待对齐项**；属占位，不构成契约冲突（`WPS52`） |
| **WPS74** | `Elevation` 临时域标签 | 占位使用域 1；契约 §6.4 域 1 覆盖清单未含 `Elevation` | `PZ10`/`PZ14` | 列为**临时登记**（`WPS57`）；替换为只读转发时移除 |
| **WPS75** | 转发/派生参数的 `Modifier` 表示 | 实现以 `0` 承载；契约要求表示为 `None` 且不进入求和式 | `PM32`；`WSC10` | 列为**表示法缺口**（`WPS58`）；本契约不裁决 |
| **WPS76** | 区域修正未接入 | 实现 `Modifier ≡ 0`（Region 层不存在） | `WSC14`/`WSC22` | **非差异**：契约允许的退化情形；Phase 3 供给后按 `PC19`/`PC20` 合成 |

---

## 附录 A：依赖方向图

```
                        ┌───────────────────────────────┐
                        │  Seed（+ 已登记显式配置）      │
                        └───────────────┬───────────────┘
                                        │
                        ┌───────────────▼───────────────┐
                        │  Parameter（WorldState 层）    │
                        │  Base ──(+ RegionModifier)──▶  │
                        │  FinalState（8 参数）          │
                        └───────┬───────────┬───────────┘
                                │           │
              ┌─────────────────┘           └──────────────────┐
              │                                                │
   ┌──────────▼──────────┐                        ┌────────────▼────────────┐
   │ Biome / Ecology /   │                        │  Geology（**经 Biome**） │
   │ Resource（只读）     │                        │  GC25 / GC75：不得直读   │
   └──────────┬──────────┘                        └────────────┬────────────┘
              │                                                │
              └───────────────► 下游产物（Voxel / Mesh / Resource / Entity）

═════════ 禁止的反向路径（全部禁止） ═════════
   Biome    ──✗──▶ Parameter
   Resource ──✗──▶ Parameter
   Runtime  ──✗──▶ Parameter
   Geology  ──✗──▶ Parameter
   Ecology  ──✗──▶ Parameter
   Entity   ──✗──▶ Parameter
   Chunk / Scene / Player ──✗──▶ Parameter
   HistoryTags ──✗──▶ Parameter
```

---

## 附录 B：禁止清单速查

| 编号 | 若出现… | 判定 |
|---|---|---|
| **WPSX-1** | 参数求值读取 Biome / 群系结论 | 违反 `WSC29`；`WPSV2` 失败 |
| **WPSX-2** | 参数求值读取 Geology 结论（`Density`/`Material`/`CaveDensity`/`DepositZone`） | 违反 `WSC26` |
| **WPSX-3** | 参数求值读取 Resource 判定 / 生态候选 | 违反 `WSC19`；`WSC56` |
| **WPSX-4** | 参数求值读取体素真值 / Chunk 状态 / 局部坐标 | 违反 `WSC19`；`PC86`/`PC87` |
| **WPSX-5** | 参数求值读取 Runtime 状态（脏标记/加载/预算/帧序） | 违反 `PM55`；`PC43`/`PC53` |
| **WPSX-6** | 参数求值读取 Player 状态（位置/进度/背包/已探索/已采集） | 违反 `WSC58`；`PM56` |
| **WPSX-7** | 参数求值读取 Entity / 对象状态 | 违反 `PM57`；`EF1` |
| **WPSX-8** | 参数求值读取 `HistoryTags` / Region 字段 / 资格声明 | 违反 `WSC28`；`RH3` |
| **WPSX-9** | 参数求值读取 Scene / `Transform` / 相机 / 渲染状态 | 违反 `DR18`；`WSC73` |
| **WPSX-10** | 参数求值使用 `Random` / `Time` / `DateTime` / `Guid` | 违反 `PZ5`/`PZ6` |
| **WPSX-11** | 参数求值依赖加载顺序 / 遍历顺序 / 是否已求值 | 违反 `PZ4`/`PZ8`；`WSC92` |
| **WPSX-12** | 由下游结论反推参数 | 违反 `PZ9`；`RA79` |
| **WPSX-13** | 参数被写入 Voxel / 生成 Chunk / 生成 Mesh | 违反 `PW6`；`WSC20`/`WSC64` |
| **WPSX-14** | 参数求值生成对象（Entity） | 违反 `WSC20`；`EF1`；`WPS41` |
| **WPSX-15** | 参数决定资源存在性 | 违反 `WPS44`/`WPS45`；表 D Owner |
| **WPSX-16** | 出现第二套参数求值路径 | 违反 `DR35`；`WSC103` |
| **WPSX-17** | 参数被下游写入 / 下游驱动参数 | 违反 §2.2；`PW2`；`BC122` |
| **WPSX-18** | 参数快照/版本链参与判定 | 违反 `WSC66` |
| **WPSX-19** | 占位来源被当作真实语义使用 | 违反 `WPS52`；`PS9` |
| **WPSX-20** | 新增参数 / 新增 `ParameterId` | 违反 `WPS11`；`PM12`/`PM19` |

---

## 附录 C：实现对应表（Phase 1.6 状态）

| 参数 | 实现位置（规则） | 当前来源类别 | 本文档登记 |
|---|---|---|---|
| `Temperature` | `Assets/Scripts/World/V2/WorldParameterRules.cs` → `TemperatureRule` | 占位（域 1 哈希） | 表 A1 行 1；§5.1 行 1 |
| `Moisture` | 同上 → `MoistureRule` | 占位（域 1 哈希） | 表 A1 行 2；§5.1 行 2 |
| `Elevation` | 同上 → `ElevationRule` | 占位（域 1 哈希 + 占位跨度） | 表 A1 行 3；§5.1 行 3；`WPS55`/`WPS57` |
| `Fertility` | 同上 → `FertilityRule` | 占位派生（Base 级 T/M） | 表 A1 行 4；§5.1 行 4；`WPS73` |
| `MagicDensity` | 同上 → `MagicDensityRule` | 契约默认值 `0` | 表 A1 行 5 |
| `HolyInfluence` | 同上 → `HolyInfluenceRule` | 契约默认值 `0` | 表 A1 行 6 |
| `CorruptionInfluence` | 同上 → `CorruptionInfluenceRule` | 契约默认值 `0` | 表 A1 行 7 |
| `Ocean` | 同上 → `OceanRule` | 契约派生（`Elevation < 64`） | 表 A1 行 8；§5.1 行 5；`WPS56` |
| 序 9–15 | **无实现**（未接入） | — | 表 B1 / 表 B2 |
| 序 16–20 | **不属参数层**（Owner = Geology / Resource） | — | §3.3 |

| # | 规则 |
|---|---|
| **WPS77** | 本附录是**登记**，不是权威：实现符号可重命名/重构，但**来源类别与替换方向**（表 A1 / §5）必须保持可追溯 |
| **WPS78** | 求值入口 = `WorldParameterEvaluator`（唯一）；查询入口 = `WorldParameterQuery`（只转发）；规则表 = `WorldParameterRules`（定长、编译期固定）；身份引用 = `WorldParameterIds`（附录 B 序 1–8） |
| **WPS79** | 本文档**不改变**任何实现；§8.3 的登记项由后续实现轮次关闭 |
