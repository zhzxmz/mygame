# World Parameter Contract V1

> **文档性质**：**参数契约规范（Parameter Contract Specification）**。本文档只定义**跨系统共享参数的契约**：是什么、谁产生、谁拥有最终解释权、谁能读、谁能写、范围与量纲、是否影响 Voxel。
> **维护说明**：本文档定义长期有效的参数身份、语义、来源归属与跨系统读写契约；不以历史任务的修改范围限制后续维护。运行时实现状态以代码和专门的实现状态登记为准。
>
> **定位**：Parameter Contract 是**规则之间的数据契约**。
>
> ```
> 规则（各层规范）= 怎么算
> 参数契约（本文档）= 传什么、谁能读、谁能写、量纲是多少
> ```
>
> **必须兼容的既有规范**：
>
> | 规范 | 与本文档的关系 |
> |---|---|
> | `WorldState_Design.md` | 8 个环境参数的**定义域与语义权威**（§1.3 表、§1.3 规则 1「全部归一化到 [0,1]」、§3 静态场、§4 圣与恶、§5 群系规则表） |
> | `WorldGeneration_Pipeline_Design.md` | 概念层因果顺序与输入/输出；具体执行阶段编号、生命周期与调度权限见 `WorldGeneration_Pipeline_Contract.md` 的阶段映射（§2.0）及本契约对应的权限表 |
> | `Region_Design.md` | 区域修正权威（RM/RE：修正集**不含** `Elevation`/`Ocean`；RO 叠加；RTY 类型无规则权；RH 标签读取权限） |
> | `WorldHistory_Design.md` | 历史权威（HE1–HE6 纪元与序位；HN9/HN10 规模参数；HB-1…HB-14 反向依赖禁令） |
> | `Ecology_Design.md` | 生态权威（EA1–EA7 输入与产出；EG/EB 反向禁令） |
> | `Resource_Design.md` | 资源权威（§4.2 两层存在性；RY11–RY14 纪元跨度；RN7 禁止 Biome 作输入；RZ1 历史必经 WorldState） |
> | `WorldQuery_Design.md` | 查询权威（WQ1/WQ2 同一规则；WN/WP/WD/WL/WE；WA1–WA20 验收） |
> | `WorldGeneration_Geology*.md` | 地形真值权威（P0–P3 判定链、§6.1 `DepositZone`、§6.2 晶簇属 Entity） |
> | `WorldGeneration_Architecture_Audit.md` | **本契约要解决的审查结论**：P0-1（缺参数/量纲/域标签/纪元契约）、P0-2（资源存在性表述）、P0-5（Ecology 输入集合）、P1-3（域分离编码）、P1-4（纪元参数）、P1-8（实现量纲不一致） |
> | `VoxelWorld_Core.md`、`WorldGeneration_Core.md`、`ADR-001` | 体素状态集合、生成契约、概念边界 |
>
> **冲突时的优先级**：**参数的规则语义**以其自身权威规范为准（本文档**不定义任何规则**）；
> **参数的身份、量纲、范围、默认值、Owner、读写权限、域 ID 分配**以**本文档**为准。
> 若本文档与某层规范对同一参数的**量纲/范围**表述冲突，本文档负责参数契约的量纲/范围；必须登记差异并按各自权威范围修订正确文件、同步更新引用，不得把“登记”解释为永久禁止维护其他规范。
>
> **零修改声明**：本文档是**声明性文档**，不产生任何运行时效果。开/关（存在/不存在）本文档**不得改变任何体素输出**（§10 `PC5`）。

---

## §1 Parameter Contract 定位

### 1.1 定义

> **Parameter Contract = 规则之间的数据契约。**
> 它回答：**跨系统共享的参数"是什么、谁能读、谁能写、量纲是多少、是否影响体素"**。

| 问题 | 由谁回答 |
|---|---|
| 参数**怎么算**？ | 各层规范（WorldState / Region / History / Geology / Ecology / Resource） |
| 参数**传什么、谁能读、谁能写、量纲**？ | **本文档** |
| 参数**在什么时候被求值**？ | `WorldGeneration_Pipeline_Design.md`（Phase 顺序） |
| 参数**怎么被问**？ | `WorldQuery_Design.md`（查询与解释） |

### 1.2 Parameter Contract **不是**什么

| 不是 | 说明 |
|---|---|
| **不是世界生成器** | 不产生体素、不创建 Chunk、不生成对象 |
| **不是 Runtime State** | 不保存任何跨帧状态；不定义玩家操作产生的实时数值（§7） |
| **不是 Cache** | 不定义缓存策略；缓存（若有）必须透明且不得改变输出（`WorldQuery_Design.md` WL9/WL10） |
| **不是 Query 系统** | 不定义查询入口与解释格式（属 `WorldQuery_Design.md`） |
| **不是第二套规则系统** | 不定义任何阈值、判定链、优先级或噪声（`WorldQuery_Design.md` WQ2） |
| **不是配置系统** | 不定义配置文件的存放/加载方式（属 Runtime/Bootstrap；本文档只声明"哪些量必须是显式参数"） |

### 1.3 核心声明

> **Parameter Contract 只定义参数，不拥有规则。**

| # | 规则 |
|---|---|
| **PT1** | 本文档**不拥有**任何参数的**计算规则**；每个参数的规则权威见 §3 的"唯一权威"列 |
| **PT2** | 本文档**拥有**参数的身份（名称/ID）、**类型、范围、单位、默认值、Owner、读写权限、域 ID 分配** |
| **PT3** | 本文档**不新增**任何参数的产生者：所有参数的"来源层"必须**已经存在**于既有规范的因果链中 |
| **PT4** | 本文档**不改变**因果方向（§4 权限矩阵必须与 `Pipeline` 附录 B 一致） |
| **PT5** | 本文档**不新增** `VoxelType`，不改变体素状态集合（Air/Grass/Dirt/Stone） |
| **PT6** | 参数必须**显式**：不得存在"影响结果但未在契约中登记"的量（继承 `WorldGeneration_Core.md` §3） |
| **PT7** | 参数必须**确定性可求值**：同 `Seed` + 同 `Position` ⇒ 同参数值（§6） |
| **PT8** | 任何**未登记**的跨系统共享量**不得**在系统之间传递；新增必须登记并升版本（§10 `PC16`） |

### 1.4 四条核心原则

| # | 原则 | 含义 |
|---|---|---|
| **P1** | **每个参数必须有唯一权威层** | 一个参数只有一个 Owner（最终解释权），见 §3 与 §4 |
| **P2** | **参数只能沿已有因果方向传播** | 方向 = `Seed → Skeleton → History → Region → WorldState → Biome → Geology → Ecology / Resource → Query` |
| **P3** | **下游系统不能修改上游参数** | 写权限只属于 Producer/Owner（§4 矩阵） |
| **P4** | **参数不能因为某个结果出现而反推生成** | 禁止一切反向箭头（`WorldState_Design.md` §1.5 C1–C4） |

### 1.5 明确禁止（示例）

| # | 禁止 | 违反的原则 | 依据 |
|---|---|---|---|
| **PT9** | **Biome → 修改 `MagicDensity`** | P2/P3 | `WorldState_Design.md` §1.5；`Region_Design.md` RB1–RB3 |
| **PT10** | **Ecology → 修改 `Fertility`** | P2/P3 | `Ecology_Design.md` E-3 / EA1 |
| **PT11** | **Resource → 修改 WorldState（任何参数）** | P2/P3 | `Resource_Design.md` RX-3 |
| **PT12** | **Entity → 修改 Generation 参数** | P2/P3 | `Ecology_Design.md` EF1；ADR-001 |
| **PT13** | 由结果反推原因（"是森林所以湿润"） | P4 | `WorldState_Design.md` §1.5 C1/C2 |
| **PT14** | 由 Biome / `RegionType` 名称决定资源或生态（`RN7`/`EG5`） | P2/P4 | `Resource_Design.md` RN7；`Region_Design.md` RTY2 |

---

## §2 参数分类

### 2.1 Environment Parameters（环境参数）

| 项 | 内容 |
|---|---|
| 成员（V1） | `Temperature`、`Moisture`、`Elevation`、`Fertility` |
| 来源层 | **Skeleton（Base 框架）→ Region（修正）→ WorldState（合成）** |
| 生命周期 | **求值期存在**：由 `(Seed, Domain, Position)` 唯一确定；**不跨帧存活**、不存储、不缓存为权威 |
| 允许读取层 | Biome、Ecology、Resource、WorldQuery；Runtime **只读**（不得回写，`PT12`） |
| 唯一权威 | `WorldState_Design.md`（§1.3 定义域与语义；§3 静态场公式） |
| 量纲 | 连续量一律 `[0,1]`（`Elevation` 例外：整数世界坐标） |

### 2.2 Influence Parameters（力量/影响参数）

| 项 | 内容 |
|---|---|
| 成员（V1） | `MagicDensity`、`HolyInfluence`、`CorruptionInfluence` |
| 来源层 | **Region（来源 → 修正）→ WorldState（合成）**；来源类别归 `Region_Design.md` §3.2 |
| 生命周期 | 求值期存在；**静态**（无扩散、无时间演化） |
| 允许读取层 | Biome、Ecology、Resource、WorldQuery；Runtime **只读** |
| 唯一权威 | `WorldState_Design.md` §3（魔力静态场）、§4（圣与恶）；修正来源权威 = `Region_Design.md` |
| 量纲 | `[0,1]`；两两**独立计算**（不得互相读取/压制）；`Balance` 是**派生只读量**（§3.3） |

### 2.3 Geographic Parameters（地理参数）

| 项 | 内容 |
|---|---|
| 成员（V1） | `Ocean`、`Continent`、`Slope`（+ 配置量 `SeaLevel`、`ContinentCount`） |
| 来源层 | **Skeleton（Phase 1 骨架）**；`Elevation` 来自既有高度场（唯一来源） |
| 生命周期 | 求值期存在；由 `Seed` + 显式参数确定 |
| 允许读取层 | History（文明舞台）、Region（自然区域 / 只读）、Biome（`Ocean` 短路）、Ecology（分支与地形庇护）、Resource（陆地/海洋排除）、WorldQuery |
| 唯一权威 | 骨架规则：`WorldState_Design.md` §7.3/§7.4（WC1–WC6）；`Pipeline` §2.1/§2.3（Phase 1 定位） |
| 量纲 | `Ocean` 布尔；`Continent` 整数 ID；`Slope` 归一化 `[0,1]`；`Elevation`/`SeaLevel` 整数世界坐标 |

### 2.4 Historical Parameters（历史参数）

| 项 | 内容 |
|---|---|
| 成员（V1） | `Epoch`、`Ordinal`、`HistoryScale`、`CivilizationPresence` |
| 来源层 | **History（Phase 2）**：由 `Seed` + 显式历史参数确定 |
| 生命周期 | **固定的过去事实**；无"当前时间"、无推进（`WorldHistory_Design.md` HE3） |
| 允许读取层 | Region（时序与归因）、Resource（纪元跨度条件）、WorldQuery（解释）；Ecology/Resource 对 `CivilizationPresence` 仅**归因/资格**用途（不得作为判定条件，`Resource_Design.md` RZ2） |
| 唯一权威 | `WorldHistory_Design.md`（HE1–HE6、HN9/HN10） |
| 量纲 | `Epoch`/`Ordinal` 整数；`HistoryScale` `[0,1]`；`CivilizationPresence` 逐文明布尔（或 `[0,1]` 强度，二者必须二选一并在实现规范中固定） |

### 2.5 Configuration Parameters（显式配置参数）

> 本类**不是世界状态**，而是"必须在配置中显式声明"的量；它们**不参与因果链**，只决定其它参数的取值方式。

| 项 | 内容 |
|---|---|
| 成员（V1） | `Seed`、`WorldHeight`、`SeaLevel`、`ContinentCount`、`EpochCount`、`OrdinalMax`、`TemperatureBase`、`MoistureBase`、各阈值参数 |
| 生命周期 | 世界创建期确定，运行期**只读** |
| 允许读取层 | 依赖该参数的各层（见 §3 表 A 的"可读取层"） |
| 唯一权威 | 各层规范中定义该配置量的条款；本文档只登记**存在性与量纲** |

### 2.6 参数数据流图

```
Seed（配置）
  │
  ├─► Phase 1 Skeleton ────────────► Elevation · Ocean · Continent · Slope
  │                                        │（只读）
  ├─► Phase 2 History ─────────────► Epoch · Ordinal · HistoryScale · CivilizationPresence
  │                                        │（只读：舞台 + 标签）
  ├─► Phase 3 Region ──────────────► 来源集合 · RegionModifier（Temperature/Moisture/
  │                                   Fertility/MagicDensity/HolyInfluence/CorruptionInfluence 的修正项）
  │                                        │
  ├─► Phase 4 WorldState ──────────► FinalState：Temperature · Moisture · Fertility ·
  │                                   MagicDensity · HolyInfluence · CorruptionInfluence
  │                                   （Elevation / Ocean 为**只读转发**，不得修正）
  │                                        │
  ├─► Phase 5 Biome ───────────────► Biome（结论；**不是**环境参数）
  │                                        │
  ├─► Phase 6 Geology ─────────────► Density · Material · CaveDensity · DepositZone
  │                                        │（真值层参数）
  ├─► 旁路 Resource ───────────────► ResourcePresence（判定：None / Substrate / Present）
  └─► 旁路 Ecology ────────────────► SpawnCandidate（资格；**不是**参数流的输出）

  查询（WorldQuery）只读整条链；**不产生任何参数**，也不写回任何参数。

  ═════ 反向箭头一律禁止（§4 / PT9–PT14）═════
```

---

## §3 参数定义表

> 为保证可读性，13 个必需字段拆为**两张同序总表**：
> **表 A** = 身份与权限（参数 / 类型 / 数值范围 / 单位 / 默认值 / 来源 / 唯一权威 / 可修改层 / 可读取层）；
> **表 B** = 影响面（参数 / 是否影响 Voxel / 是否影响 Biome / 是否影响 Ecology / 是否影响 Resource）。
> 两表**行序一一对应**；表 B 的取值含义见 §3.1 图例。

### 3.1 表 B 取值图例

| 取值 | 含义 |
|---|---|
| **是（直接）** | 该参数**直接**参与决定该层结论 |
| **是（间接）** | 该参数只通过另一参数/结论**间接**影响该层 |
| **条件** | 仅在显式条件下影响（图例在单元格内注明） |
| **否** | V1 明确不影响 |

### 3.2 表 A：身份与权限

| 参数 | 类型 | 数值范围 | 单位 | 默认值 | 来源 | 唯一权威 | 可修改层 | 可读取层 |
|---|---|---|---|---|---|---|---|---|
| **Temperature** | 归一化标量（float） | `[0,1]` | 无量纲 | `TemperatureBase`（配置，默认 `0.5`） | Skeleton Base + Region 修正 | `WorldState_Design.md` §1.3/§3 | **WorldState**（= Base + Modifier 合成） | Biome · Ecology · Resource · WorldQuery · Runtime（只读） |
| **Moisture** | 归一化标量（float） | `[0,1]` | 无量纲 | `MoistureBase`（配置，默认 `0.5`） | Skeleton Base + Region 修正 | `WorldState_Design.md` §1.3/§3 | **WorldState** | Biome · Ecology · Resource · WorldQuery · Runtime（只读） |
| **Elevation** | 整数（int） | `[0, WorldHeight-1]` | 体素（voxel，y 轴） | 无默认（= 高度场求值结果） | **既有高度场**（Skeleton 基础高度框架） | `HeightFunction`（实现）/ `WorldState_Design.md` §1.3 | **无**（不存在修正者：Region 明令不得修改，`Region_Design.md` RE1） | Region（只读）· History（只读：高程带/山脉）· Biome（只读）· Ecology（只读：地形庇护）· Resource（只读：介质）· WorldQuery · Runtime（只读） |
| **Fertility** | 归一化标量（float） | `[0,1]` | 无量纲 | `0`（无肥力） | 派生（土壤深度 + Moisture + Temperature）+ Region 修正 | `WorldState_Design.md` §1.3 | **WorldState** | Biome · Ecology · Resource（生物资源载体）· WorldQuery · Runtime（只读） |
| **MagicDensity** | 归一化标量（float） | `[0,1]` | 无量纲 | `0`（无魔力） | Region（来源 → 修正）+ WorldState（合成） | `WorldState_Design.md` §3 | **WorldState**（修正项只由 Region 提供） | Biome · Ecology · Resource · WorldQuery · Runtime（只读） |
| **HolyInfluence** | 归一化标量（float） | `[0,1]` | 无量纲 | `0` | Region（圣性来源 → 修正）+ WorldState（合成） | `WorldState_Design.md` §4 | **WorldState** | Biome · Ecology · Resource · WorldQuery · Runtime（只读） |
| **CorruptionInfluence** | 归一化标量（float） | `[0,1]` | 无量纲 | `0` | Region（腐化来源 → 修正）+ WorldState（合成） | `WorldState_Design.md` §4 | **WorldState** | Biome · Ecology · Resource · WorldQuery · Runtime（只读） |
| **Ocean** | 布尔（bool） | `{false, true}` | 无量纲 | `false` | 派生：`Elevation < SeaLevel` | `WorldState_Design.md` §1.3（派生规则） | **无**（Region 明令不得修改，`RE1`） | Region（只读）· Biome（短路）· Ecology（分支）· Resource（排除）· History（只读）· WorldQuery · Runtime（只读） |
| **Continent** | 整数 ID（int） | `[0, ContinentCount]`；`0` = 无大陆（海洋）；**单大陆退化模式：所有陆地查询结果 = `1`**（`D3`，已批准；见 `WorldSkeleton_V1_Minimal_Contract.md` `Q-10`）；本项是**位置所属大陆编号**，与 `ContinentCount`（**数量配置**）**不得混淆** | 无量纲（标识） | `0` | Skeleton 大陆函数（`Seed` + 显式参数） | `WorldState_Design.md` §7.4（WC1–WC6） | **Skeleton**（Phase 1） | History（文明舞台）· Region（自然区域）· WorldQuery · Runtime（只读） |
| **Epoch** | 整数 ID（int） | `[0, EpochCount-1]`；`EpochCount` 默认 `3` | 纪元段（标识） | `0`（最早纪元） | History（`Seed` + 显式纪元划分） | `WorldHistory_Design.md` HE1–HE4 | **History** | Region（时序）· Resource（纪元跨度）· WorldQuery · Runtime（只读） |

### 3.3 表 B：影响面（是否影响 Voxel / Biome / Ecology / Resource）

| 参数 | 是否影响 Voxel | 是否影响 Biome | 是否影响 Ecology | 是否影响 Resource |
|---|---|---|---|---|
| **Temperature** | 是（间接：经 Biome → **仅 P3 地表材料**） | **是（直接）** | 是（直接：适生区间） | 是（直接：载体资格条件） |
| **Moisture** | 是（间接：经 Biome → 仅 P3 地表材料） | **是（直接）** | 是（直接：植被条件） | 是（直接：载体资格条件） |
| **Elevation** | **是（直接：P2 地形高度决定实体/空）** | 是（间接：经 `Ocean` 与高程带） | 是（直接：垂直带 / 地形庇护） | 是（直接：介质与地层） |
| **Fertility** | 是（间接：经 Biome → 仅 P3 地表材料） | **是（直接）** | 是（直接：繁茂度） | 是（直接：生物资源载体） |
| **MagicDensity** | **否**（V1 属性层，不新增体素类型） | **是（直接）** | 是（直接：魔法类资格） | 是（直接：魔法资源三条件之一） |
| **HolyInfluence** | **否** | **是（直接）** | 是（直接：圣性类资格） | 是（直接：圣性资源条件） |
| **CorruptionInfluence** | **否** | **是（直接）** | 是（直接：腐化类资格与危险生物） | 是（直接：腐化资源条件） |
| **Ocean** | 是（间接：派生自 `Elevation`） | **是（直接：短路）** | 是（直接：陆生/水生分支） | 是（直接：排除陆地资源与遗迹） |
| **Continent** | 是（间接：经 `Elevation` 掩码叠加） | 是（间接：经 `Ocean`/气候带） | 条件（只读：可选的自然区域归属） | 条件（只读：可选的归属信息） |
| **Epoch** | **否**（不参与任何体素判定） | 否 | 条件（仅**归因**，不作判定条件） | 是（直接：纪元跨度条件，`Resource_Design.md` RY11） |

### 3.4 表 C：次要参数（V1 同样受契约约束）

| 参数 | 类型 | 数值范围 | 单位 | 默认值 | 来源 | 唯一权威 | 可修改层 | 可读取层 | Voxel | Biome | Ecology | Resource |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **Ordinal** | 整数 | `[0, OrdinalMax-1]`；`OrdinalMax` 默认 `8` | 序位 | `0` | History | `WorldHistory_Design.md` HE1/HE6 | **History** | Region · Resource · WorldQuery | 否 | 否 | 归因 | 是（纪元跨度） |
| **HistoryScale** | 归一化标量 | `[0,1]` | 无量纲 | `0.5` | History（配置化） | `WorldHistory_Design.md` HN9/HN10 | **History** | History 内部 · WorldQuery（解释） | 否 | 否 | 否 | 否 |
| **CivilizationPresence** | 逐文明标记（bool 或 `[0,1]`） | `{false,true}` 或 `[0,1]` | 无量纲 | `false` / `0` | History | `WorldHistory_Design.md` §3 | **History** | Region（活动范围）· WorldQuery · Ecology/Resource（**仅归因**） | 否 | 否 | 归因 | 归因 |
| **Slope** | 归一化标量 | `[0,1]`（0 = 平地） | 无量纲 | `0` | Skeleton（由 `Elevation` 梯度派生） | 本文档 §3.4（派生定义）+ `WorldState_Design.md` §7.4 | **Skeleton** | History（偏好）· Ecology（地形庇护）· WorldQuery | 是（间接：派生自 `Elevation`） | 否 | 是（地形庇护） | 否 |
| **SeaLevel** | 整数（配置） | `[0, WorldHeight]` | 体素 | `64`（显式配置） | 配置 | `WorldState_Design.md` §1.3（派生规则） | **配置（世界创建期）** | Skeleton（`Ocean` 派生） | 是（间接） | 是（间接） | 是（间接） | 是（间接） |
| **ContinentCount** | 整数（配置） | `[1, 上限显式]` | 个 | `1`（可退化为全部陆地） | 配置 | `WorldState_Design.md` §7.4 WC1/WC2 | **配置** | Skeleton | 是（间接） | 是（间接） | 否 | 否 |
| **ContinentCenters** | **待确认（未批准）** | **未批准（本轮不设定）** | **待确认（未批准）** | **无登记默认值（本轮不批准默认值）** | **待确认**：`Seed` 派生 / 世界创建期显式提供 / 其它 —— 均未获批准（`WC1` 只要求**确定性**） | `WorldState_Design.md` §7.3 **L622**（命名；该节自注"参数设计（显式，不实现）"）；登记见 `WorldSkeleton_V1_Minimal_Contract.md` §3B `C-3` | **待确认** | Skeleton | 否 | 否 | 否 | 否 |
| **ContinentRadius** | 显式参数（概念；编码待实现期确定） | **未批准（本轮不设定）** | 待定 | **无登记默认值（本轮不批准默认值）** | 配置（世界创建期） | `WorldState_Design.md` §7.3/§7.4（`WC1`）；登记见 `WorldSkeleton_V1_Minimal_Contract.md` | **配置（世界创建期）** | Skeleton | 否 | 否 | 否 | 否 |
| **ContinentShape** | 显式参数（概念；编码待实现期确定） | **未批准（本轮不设定）** | 待定 | **无登记默认值（本轮不批准默认值）** | 配置（世界创建期） | `WorldState_Design.md` §7.3/§7.4（`WC1`）；登记见 `WorldSkeleton_V1_Minimal_Contract.md` | **配置（世界创建期）** | Skeleton | 否 | 否 | 否 | 否 |
| **OceanGap** | 显式参数（概念；编码待实现期确定） | **未批准（本轮不设定）** | 待定 | **无登记默认值（本轮不批准默认值）** | 配置（世界创建期） | `WorldState_Design.md` §7.3/§7.4（`WC4`）；登记见 `WorldSkeleton_V1_Minimal_Contract.md` | **配置（世界创建期）** | Skeleton | 否 | 否 | 否 | 否 |
| **EpochCount** | 整数（配置） | `[1, 上限显式]` | 段 | `3` | 配置 | `WorldHistory_Design.md` HE1 | **配置** | History · WorldQuery | 否 | 否 | 归因 | 是（纪元跨度） |
| **Balance** | 归一化标量（**派生**） | `[-1, 1]` | 无量纲 | 不可配置（恒为派生） | 派生：`HolyInfluence − CorruptionInfluence` | `WorldState_Design.md` §4.4 | **无**（只读派生量） | Biome（仅以参数形式间接）· WorldQuery · Runtime（只读） | 否 | 是（间接：经参数） | 是（间接） | 否 |

> **骨架生成配置登记状态（本轮）**：`ContinentCenters` / `ContinentRadius` / `ContinentShape` / `OceanGap` 四项**只登记语义与 Owner/取值来源**（概念依据：`WorldState_Design.md` §7.3/§7.4 `WC1`/`WC4`）；**本轮不批准默认值、数值范围、编码或生成算法**（未批准项以"未批准（本轮不设定）"显式标注，**不得**在实现中自行取值）。`SeaLevel`（默认 `64`）与 `ContinentCount`（默认 `1`，可退化为全部陆地）沿用本表既有登记值，**不另设默认值**。
> **`ContinentCenters` 的待确认边界**：其**取值来源、坐标类型与单位**均**未获批准**（**待确认**）；`WC1` 只要求大陆骨架结果具有**确定性**，**不得**把"确定性"扩大解释为"中心只能由 `Seed` 派生"。其余三项（`ContinentRadius` / `ContinentShape` / `OceanGap`）的取值来源 = 显式参数（配置，世界创建期）。
> **大陆编号语义（`D3`，已批准）**：`Continent = 0` = 海洋/无大陆（**合法结果**）；**单大陆退化模式：所有陆地 = `1`**；`Continent`（编号）与 `ContinentCount`（数量配置）**不得混淆**（见 `WorldSkeleton_V1_Minimal_Contract.md` `Q-8`/`Q-10`）。
>
> **`Elevation` 域归属（登记边界）**：`Elevation` 的**终态是既有高度场只读转发且不使用任何参数域**（见 `WorldParameter_Source_Contract.md` `WPS57`）；**不得**以"扩充 §6.4 域 1 覆盖清单"作为终态方案（§6.4 登记不变）。

### 3.5 表 D：既有层间参数（**只登记，不新增权威**）

| 参数 | 归属层（Owner） | 权威规范 | 说明 |
|---|---|---|---|
| **Density** | Geology | `WorldGeneration_Geology.md` §3 | 实体/空判定（P0–P2） |
| **Material** | Geology | 同上 §4 | 体素状态（`{Air, Grass, Dirt, Stone}` 内） |
| **CaveDensity** | Geology | 同上 §5 | 洞穴强度（P1） |
| **DepositZone** | Geology | 同上 §6.1 | **地质矿藏基础层**（属性层，**不是**体素类型） |
| **Biome** | WorldState（规则表）/ Biome 层（结论） | `WorldState_Design.md` §5.3 | 群系**结论**：是结果，不是原因；禁止作为资源判定输入（`RN7`） |
| **ResourcePresence** | Resource | `Resource_Design.md` §1.3 | 资源**判定层**（None / Substrate / Present）；与 `DepositZone` **分层**（§8.2 ①） |

> **术语冻结（解决审查 P0-2）**：
> * **"地质矿藏基础层"** = `DepositZone`（Owner = Geology）；
> * **"资源判定层"** = `ResourcePresence`（Owner = Resource）；
> * 任何既有规范中出现的“资源存在性”字样，一律按上述**两层**之一理解；本契约固定术语与归属，不重复定义资源规则。若既有规范措辞与本契约冲突，应由相应权威文件修订并同步交叉引用。

---

## §4 参数权限规则

### 4.1 角色定义

| 角色 | 定义 | 权力 |
|---|---|---|
| **Producer** | **产生**参数的系统（提供 Base、修正项或事实） | 产生；**不得**被下游修改 |
| **Owner** | 拥有参数**最终解释权**的系统（定义量纲、语义、合成方式） | 定义与合成；只有 Owner 能确定最终值 |
| **Consumer** | **读取**参数的系统 | **只读**；不得修改上游（`PT9`–`PT12`） |

**硬性**

| # | 规则 |
|---|---|
| **PW1** | 每个参数必须有**唯一 Owner**（P1） |
| **PW2** | **Consumer 不得修改 Producer / Owner 的参数**（P3） |
| **PW3** | 写权限只出现在矩阵中标注为 **P**（Producer）或 **O**（Owner）的单元；其余单元一律为**只读或禁止** |
| **PW4** | 读取权限必须落在 §3 表 A 的"可读取层"内；越权读取 = 缺陷 |
| **PW5** | **归因读取（R\*）**：只允许用于解释/标签，**不得**作为判定条件（`Resource_Design.md` RZ2、`Region_Design.md` RH3） |
| **PW6** | 参数不得被**任何**系统写入 Voxel（`Density`/`Material` 的写入属 Geology 的判定链，不是"参数写入"） |

### 4.2 权限矩阵（参数 × 系统）

**图例**：`P` = 产生（Producer）；`O` = 拥有最终解释权（Owner）；合成 = 由 Owner 依据上游修正合成最终值；`R` = 只读；`R*` = **仅归因**只读；`—` = 禁止（无任何权限）。

| 参数 ＼ 系统 | Seed | History | Region | WorldState | Biome | Geology | Ecology | Resource | WorldQuery | Runtime |
|---|---|---|---|---|---|---|---|---|---|---|
| **Temperature** | P（配置） | — | P（修正项） | **O**（合成） | R | — | R | R | R | R |
| **Moisture** | P（配置） | — | P（修正项） | **O**（合成） | R | — | R | R | R | R |
| **Elevation** | P（Seed） | R | R | R（转发） | R | R | R | R | R | R |
| **Fertility** | P（配置） | — | P（修正项） | **O**（合成） | R | — | R | R | R | R |
| **MagicDensity** | P（Seed） | — | P（修正项） | **O**（合成） | R | — | R | R | R | R |
| **HolyInfluence** | P（Seed） | — | P（修正项） | **O**（合成） | R | — | R | R | R | R |
| **CorruptionInfluence** | P（Seed） | — | P（修正项） | **O**（合成） | R | — | R | R | R | R |
| **Ocean** | P（Seed） | R | R | R（转发） | R | R（P3） | R | R | R | R |
| **Continent** | P（Seed） | R | R | — | — | — | R* | R* | R | R |
| **Epoch** | P（配置） | **O** | R | — | — | — | R* | R | R | R |
| **Ordinal** | P（配置） | **O** | R | — | — | — | R* | R | R | R |
| **HistoryScale** | P（配置） | **O** | — | — | — | — | — | — | R | R |
| **CivilizationPresence** | P（Seed） | **O** | R | — | — | — | R* | R* | R | R |
| **Slope** | P（Seed） | R | R | — | — | — | R | — | R | R |
| **Balance** | — | — | — | **O**（派生） | R | — | R | — | R | R |
| **Density / Material / CaveDensity / DepositZone** | P（Seed） | — | — | — | — | **O** | R | R | R | R |
| **Biome** | — | — | — | **O**（规则表） | O（结论） | R（**仅 P3**） | R | — | R | R |
| **ResourcePresence** | — | — | — | — | — | R | R | **O** | R | R |
| **SpawnCandidate** | — | — | R（区域级资格） | — | R | R（地形庇护） | **O** | — | R | R |

**矩阵自查**

| # | 自查项 | 期望 |
|---|---|---|
| **PW7** | 是否存在"下游列在**上游**行出现 P/O"？ | **必须为 0**（例如 Biome 列不得在 `MagicDensity` 行出现 O） |
| **PW8** | 是否存在 **Runtime 列出现 P/O**？ | **必须为 0**（Runtime 只能 R） |
| **PW9** | 是否存在 **WorldQuery 列出现非 R**？ | **必须为 0**（Query 只读，`WQ4`） |
| **PW10** | 是否存在 **Geology 列在非地质参数上出现 O**？ | **必须为 0**（Geology 只拥有地形真值参数） |

---

## §5 参数量纲和范围规范

### 5.1 冻结的量纲规则

| # | 规则 |
|---|---|
| **PS1** | **所有连续参数必须归一化到 `[0,1]`**（唯一例外：`Balance` 为 `[-1,1]`，`Elevation`/`SeaLevel` 为整数世界坐标） |
| **PS2** | **每个参数必须显式声明范围与单位**；未声明者不得跨系统传递（`PT8`） |
| **PS3** | **禁止不同系统自行解释量纲**：任何系统不得对同一参数使用不同范围（例如把 `[0,1]` 的魔力当作 `[0,100]`） |
| **PS4** | **跨层传递必须是契约量纲**；实现可以内部使用其它表示，但必须在**边界**完成**可逆、确定性**的归一化映射，并登记映射表（§5.3） |
| **PS5** | 越界一律**钳制**（clamp）到范围内（继承 `WorldState_Design.md` §1.3 规则 1） |
| **PS6** | 整数参数（`Elevation`/`Ocean` 派生/ID 类）不得被当作连续量插值；连续参数不得被四舍五入为离散判定条件 |
| **PS7** | 归一化必须**与量纲无关的确定性**：不得依赖平台浮点路径差异（继承 `WorldGeneration_Geology.md` §8.2 D5） |

> **`PS5` 的适用范围（澄清，不新增规则、不改数值）**：`PS5` 的"越界一律钳制"适用于**参数值域**（即本契约 §5.2 冻结范围内的参数值）。它**不**延伸为 WorldSkeleton V1 查询入口的输入处理逻辑：`WorldSkeletonFacts.TryEvaluate(...)` 对 `seaLevel` **不校验、不钳制、不修正**，`seaLevel ∈ [0, WorldHeight]` 与 `settings` 合法性均由**调用方**保证（见 `WorldSkeleton_V1_Minimal_Contract.md` `IF-8`/`IF-9`）。本澄清**不**改变 `PS1`–`PS7` 任何条款的效力与数值。

### 5.2 范围冻结表

| 类别 | 参数 | 冻结范围 | 说明 |
|---|---|---|---|
| 连续（环境） | `Temperature` `Moisture` `Fertility` | `[0,1]` | 0 = 最低，1 = 最高 |
| 连续（影响） | `MagicDensity` `HolyInfluence` `CorruptionInfluence` | `[0,1]` | 0 = 无影响，1 = 饱和 |
| 连续（地理） | `Slope` | `[0,1]` | 0 = 平地，1 = 最陡（映射规则显式） |
| 连续（历史/派生） | `HistoryScale` | `[0,1]` | 0 = 最小历史规模，1 = 最大 |
| 派生（双向） | `Balance` | `[-1,1]` | 负 = 腐化主导，正 = 圣性主导 |
| 整数（世界坐标） | `Elevation` `SeaLevel` | `[0, WorldHeight]`（体素单位） | `WorldHeight` 来自 `WorldGenerationSettings` |
| 布尔 | `Ocean` | `{false, true}` | `true` = 海洋 |
| 标识（整数） | `Continent` | `[0, ContinentCount]` | `0` = 无大陆（海洋） |
| 标识（整数） | `Epoch` `Ordinal` | `[0, EpochCount-1]` / `[0, OrdinalMax-1]` | 序位由 `Seed` 派生 |
| 逐文明标记 | `CivilizationPresence` | `{false,true}` 或 `[0,1]` | 二选一必须在实现规范中固定 |

### 5.3 与既有实现的映射表（**解决审查 P1-8 / P2-3；不修改代码**）

| 既有实现符号 | 既有量纲 | 契约参数 | 映射（确定性、可逆） |
|---|---|---|---|
| `BiomeSample.Temperature`（`IBiomeProvider.cs:22-25`） | 整数 `[-500, 600]` | `Temperature` | `t = (T + 500) / 1100` |
| `BiomeSample.Moisture`（`IBiomeProvider.cs:28-31`） | 整数 `[0, 1000]` | `Moisture` | `m = M / 1000` |
| `HeightFunction.GetTerrainHeight(...)`（`HeightFunction.cs:27`） | 整数世界坐标 | `Elevation` | 直接对应（同一量纲） |
| `GeologySampler.SurfaceHeightAt(...)`（`GeologySampler.cs:129/134`） | 整数世界坐标 | `Elevation` | 直接对应（规范名 `SurfaceHeight` → 实现名映射） |
| `GeologySample.CaveDensity`（`GeologySample.cs:51`） | 浮点（噪声标量） | `CaveDensity`（表 D） | 同一量纲；范围由 Geology 规范声明 |
| `DepositSample.Richness`（`IDepositProvider.cs:39`） | 整数 `[0,100]` | （属 `DepositZone` 层） | 与 `ResourcePresence` **不同层**，禁止混用（§3.5） |

> **WorldSkeleton V1 查询接口的消费方式（登记，不新增参数、不新增规则）**：`WorldSkeletonFacts.TryEvaluate(...)` 对 `Elevation` **直接调用**上表登记的 `HeightFunction.GetTerrainHeight(position.X, position.Z, settings)`（同一量纲、无映射、无域哈希）；`Ocean` 与 `Continent` 由该 `Elevation` 按 `WorldState_Design.md` `WC3` 与 `WorldSkeleton_V1_Minimal_Contract.md` `IF-6`/`IF-7` 派生，**不引入**新参数。该查询接口**不读取**本契约的占位实现（域 1 哈希占位），也**不改变**参数系统侧的占位状态（`WPS54a` 仍有效）。

**硬性**

| # | 规则 |
|---|---|
| **PS8** | 上表映射是**边界映射**：新层之间传递必须使用契约量纲（`[0,1]`） |
| **PS9** | 既有占位实现（`BiomeA`/`BiomeB`、`ZoneA`/`ZoneB`）**不得**被当作契约参数值；它们只是**待替换的占位**（属审查 AD-3 决策） |
| **PS10** | 归一化映射必须写入实现规范，并纳入验收（`PC7`） |
| **PS11** | **禁止**为迁就既有整数域而修改契约范围（契约是唯一权威） |

---

## §6 参数与确定性

### 6.1 基本要求

| # | 规则 |
|---|---|
| **PZ1** | **同 `Seed` + 同 `Position` ⇒ 同参数值**（对每个参数独立成立） |
| **PZ2** | 参数必须**可独立重复求值**（无副作用、无状态累积） |
| **PZ3** | 参数必须**可局部求值**：任意世界坐标可独立求出全部参数，不需要遍历全世界（`WorldQuery_Design.md` WL1–WL8） |
| **PZ4** | 参数不得依赖加载顺序、已加载 Chunk 集合、帧序、tick、查询顺序、遍历顺序 |

### 6.2 禁止事项

| # | 禁止 |
|---|---|
| **PZ5** | `Random()` / `UnityEngine.Random` / `System.Random` / 任何有状态伪随机源 |
| **PZ6** | `Time` / `DateTime` / 时间戳 / 真实时长 / tick / 帧数 / `Guid` |
| **PZ7** | Player State（位置、进度、背包、已探索区域、已采集统计） |
| **PZ8** | Load Order（加载顺序、邻居是否已求值、世界级中间态） |
| **PZ9** | 由下游结果反推参数（Biome → 参数、Resource → 参数，`PT13`） |

### 6.3 Domain Separation（**编码契约**）

> 每个参数必须由 `(Seed, Domain, Position)` 唯一确定（`Pipeline` §3.3 PE1–PE7）。本节把 `Domain` 从"概念"冻结为**可实现的编码契约**（解决审查 P1-3）。

| # | 规则 |
|---|---|
| **PZ10** | `Domain` 是**显式整数标识**（32 位无符号），由本文档**登记分配**；未登记的域**不得使用**（`PC8`） |
| **PZ11** | `Domain` 必须**参与哈希混合**：新层的确定性来源必须形如"以 `(seed, domain, 世界坐标)` 为输入的自实现整数哈希"，禁止直接复用既有函数而不带域 |
| **PZ12** | **保留域（Reserved Domains）**：既有实现 `HeightFunction.Hash(x, z, seed)` 与 `GeologyNoise.LatticeHash(cellX, cellY, cellZ, seed)` 的混合方式视为**保留域**；新层**不得**调用它们作为自身随机源（否则产生串扰），**也不得**修改它们 |
| **PZ13** | 同一参数在**同一域**内求值必须恒等；不同域之间必须**统计独立**（可机械验证：改一个域的种子不影响其它域的输出） |
| **PZ14** | 域 ID 的分配者是**本文档**；新增域必须登记并升版本（`PC16`） |

### 6.4 V1 域 ID 分配表（冻结）

| Domain ID | 名称 | 覆盖参数/系统 |
|---|---|---|
| `1` | `Skeleton` | `Continent`、`Slope`、基础气候带（`TemperatureBase`/`MoistureBase` 的噪声部分） |
| `2` | `History.Civilization` | `CivilizationPresence` |
| `3` | `History.War` | 战争类事件（→ 来源候选） |
| `4` | `History.Magic` | 魔法实验/仪式类事件 |
| `5` | `History.Disaster` | 灾难类事件 |
| `6` | `History.HolyCorruption` | 圣恶冲突类事件 |
| `7` | `History.Resource` | 发现与开采类事件 |
| `8` | `Region.Natural` | 自然区域来源 |
| `9` | `Region.Magic` | 魔法来源（→ `MagicDensity` 修正） |
| `10` | `Region.Holy` | 圣性来源（→ `HolyInfluence` 修正） |
| `11` | `Region.Corruption` | 腐化来源（→ `CorruptionInfluence` 修正） |
| `12` | `Region.Relic` | 遗迹候选区域 |
| `13` | `Resource.Common` | Iron / Copper / Gold 判定调制 |
| `14` | `Resource.Magic` | Magic Crystal / Mana Ore 判定 |
| `15` | `Resource.Holy` | Holy Crystal 判定 |
| `16` | `Resource.Corruption` | Corrupted Material 判定 |
| `17` | `Resource.Substrate` | 生物资源载体资格 |
| `18` | `Biome` | 群系判定所需的域内扰动（若规则表需要；否则不使用） |
| `100+` | **保留区** | **禁止使用**（留给未来版本；未登记即禁止） |
| `R-1` / `R-2` | **保留域（既有实现）** | `HeightFunction` / `GeologyNoise` 的既有通道（**只读保留，不得复用**） |

> `Base`（`TemperatureBase`/`MoistureBase`）若为常量则不需要域；若为噪声场，必须使用 `Domain = 1`。

---

## §7 参数与 Runtime

### 7.1 必须区分的两类量

| 类别 | 定义 | 生命周期 | 归属规范 | 示例 |
|---|---|---|---|---|
| **Generation Parameter** | 由**世界规则**在求值时确定、描述"这个世界**本来**是什么样" | 求值期存在（可由 `(Seed, Domain, Position)` 重算） | 本文档 + 各层规范 | `MagicDensity`（某坐标的魔力环境） |
| **Runtime State** | 由**运行时行为**产生、描述"这个世界**现在**是什么样" | 运行期存在（可能存档） | `VoxelWorld_Runtime*.md` / `Mining` 规范 | 玩家编辑后的体素值、玩家背包、当前技能状态 |

### 7.2 硬性规则

| # | 规则 |
|---|---|
| **PX1** | **`MagicDensity` 属于 Generation Parameter**：它**不是**"当前玩家释放魔法后的实时数值" |
| **PX2** | Runtime 的改变**必须进入 Runtime State**（体素编辑 → `VoxelWorld.SetVoxel`；玩家资源 → 玩家侧状态），**不得**覆盖任何 Generation Parameter |
| **PX3** | 任何"因为运行时事件而提高 `MagicDensity`/`CorruptionInfluence`/`Fertility`"的诉求，V1 一律**禁止**；若未来需要，必须作为**新的世界来源类型**在上游规范中定义（`Resource_Design.md` RY21 同构），而不是由 Runtime 回写参数 |
| **PX4** | Runtime **只读**生成参数（§4 矩阵 Runtime 列全部为 `R`） |
| **PX5** | 查询若需要"当前真值（含玩家编辑）"，必须显式走 **Runtime State 合成**；参数契约只定义 Generation Parameter（与 `WorldQuery_Design.md` 的 Pristine/Current 两种模式对应；该模式划分属审查 `AD-2` 决策） |
| **PX6** | 参数不得触发任何 Runtime 行为：参数求值**不创建 Chunk、不置脏、不生成 Mesh、不生成对象** |

---

## §8 与已有规范兼容检查

### 8.1 检查总览

| 规范 | 是否新增权威 | 是否改变因果方向 | 是否允许下游反写 | 是否影响已有 Voxel | 结论 |
|---|---|---|---|---|---|
| `WorldState_Design.md` | **否**（参数 Owner 指向该文档 §1.3/§3/§4/§5） | 否 | 否 | 否（声明性） | **无冲突** |
| `WorldGeneration_Pipeline_Design.md` | **否**（权限矩阵与附录 B.1 同构） | 否（P/O/R 矩阵与 §1.3 输入表一致） | 否 | 否 | **无冲突** |
| `Region_Design.md` | **否**（Region 只产生修正项，Owner 仍是 WorldState） | 否（沿用 RT/RM/RE/RO） | 否 | 否 | **无冲突** |
| `WorldHistory_Design.md` | **否**（`Epoch`/`Ordinal`/`HistoryScale`/`CivilizationPresence` 属 History） | 否（沿用 HE/HN/HA） | 否 | 否 | **无冲突** |
| `Ecology_Design.md` | **否** | 否 | 否 | 否 | **无冲突**（含 1 项表述对齐，见 §8.3 ①） |
| `Resource_Design.md` | **否** | 否 | 否 | 否 | **无冲突**（含 1 项术语对齐，见 §8.3 ②） |
| `WorldQuery_Design.md` | **否**（Query 列全为只读） | 否 | 否 | 否 | **无冲突** |
| `WorldGeneration_Geology*.md` | **否**（`Density`/`Material`/`CaveDensity`/`DepositZone` 仍归 Geology） | 否 | 否 | 否 | **无冲突** |
| `VoxelWorld_Core.md` / `WorldGeneration_Core.md` | 否 | 否 | 否 | 否（不新增 `VoxelType`） | **无冲突** |
| `WorldGeneration_Architecture_Audit.md` | 否 | 否 | 否 | 否 | **已解决 6 项审查结论**（见 §8.4） |

### 8.2 关键接触点（4 项）

| # | 接触点 | 本文档的处理 |
|---|---|---|
| ① | **参数 vs 群系结论** | `Biome` 是**结论**不是参数（表 D）；它**不得**作为 Resource 的输入（`RN7`），只在 Geology 的 **P3** 被读取 |
| ② | **`Elevation`/`Ocean` 的不可修改性** | 二者"可修改层 = 无"，与 `Region_Design.md` `RE1` 一致；`Continent` 只能通过 `Elevation` 掩码**间接**影响体素（`WC2`） |
| ③ | **`Epoch` 的读取用途** | Resource 可用作**纪元跨度条件**（`RY11`）；Ecology 只能**归因**（`R*`），不得作为判定条件（`Resource_Design.md` RZ2、`Region_Design.md` RH3） |
| ④ | **`Balance` 是派生量** | 无独立 Owner 写入方；合成规则沿用 `WorldState_Design.md` §4.4（两参数**独立**叠加，"抵消"只体现在 `Balance`） |

### 8.3 需要既有规范对齐的表述（**登记项；本文档不修改任何文件**）

| # | 需对齐内容 | 位置 | 真相（按本契约） |
|---|---|---|---|
| ① | Ecology "输入只有 FinalState + Biome + Region Tags" | `Ecology_Design.md:93`（EA2） | **真相**：Ecology 的输入 = 本契约 §3 表 A/表 C 中标注"可读取层含 Ecology"的参数（含 `Elevation`、`Slope`、`CaveDensity` 等地形庇护条件）。建议该规范把 EA2 改为"参数白名单（见 `WorldParameter_Contract.md` §3）" |
| ② | "资源存在性属于 Geology" | `Ecology_Design.md:286`（ER2） | **真相**：`DepositZone` = **地质矿藏基础层**（Geology）；`ResourcePresence` = **资源判定层**（Resource）。ER2 的意图（Ecology 不得定义/修改 `DepositZone`）保持成立 |
| ③ | "求值顺序 Resource 在 Ecology 之前" | `Resource_Design.md` §8.2③ | **真相**：二者**互不读取**，顺序不影响结果（`WorldQuery_Design.md` WN4 可交换条款）；本契约在 §4 矩阵中把两者都标为只读上游、互不写 |
| ④ | 既有实现的整数域与规范 `[0,1]` 不一致 | `IBiomeProvider.cs:22-31` | **真相**：契约 `[0,1]` 为唯一跨层量纲；既有整数域只是**占位实现**，按 §5.3 边界映射（不改代码）；替换属审查 `AD-3` 决策 |

### 8.4 对审查报告问题的解决状态

| 审查项 | 本文档的处理 | 状态 |
|---|---|---|
| **P0-1**（缺参数/量纲/域标签/纪元契约） | §3 表 A/B/C/D 定义 10 个必需参数 + 次要参数 + 既有层间参数；§5 冻结量纲；§6.3/§6.4 冻结域编码与域 ID 表；§3.4 冻结 `Epoch`/`Ordinal`/`HistoryScale`/`EpochCount`/`OrdinalMax` | **已解决（契约层）** |
| **P0-2**（资源存在性表述） | §3.5 术语冻结：`DepositZone`（Geology）/ `ResourcePresence`（Resource）两层 | **已解决（契约层）**；既有文件表述对齐登记于 §8.3 ② |
| **P0-5**（Ecology 输入集合） | Ecology 的输入以**参数白名单**表达（§3 表 A/表 C 的"可读取层"列） | **已解决（契约层）**；既有文件表述对齐登记于 §8.3 ① |
| **P1-3**（域分离缺编码） | §6.3 `PZ10`–`PZ14` + §6.4 域 ID 分配表（含保留域 `R-1`/`R-2`） | **已解决** |
| **P1-4**（纪元/规模参数缺失） | §3.4：`Epoch`（`[0,EpochCount-1]`，默认 `EpochCount=3`）、`Ordinal`（`OrdinalMax=8`）、`HistoryScale` `[0,1]` 默认 `0.5`、`CivilizationPresence` | **已解决（契约层）**；计数上限映射仍需实现规范（属 `AD-5`） |
| **P1-8**（实现量纲不一致） | §5.3 边界映射表（`[-500,600]`、`[0,1000]` → `[0,1]`），§5.2 范围冻结 | **已解决（映射契约）**；替换占位实现属 `AD-3` |
| **P2-3**（符号名不一致） | §5.3 符号映射表（`SurfaceHeight` → `GetTerrainHeight`/`SurfaceHeightAt`） | **已解决（映射登记）** |

### 8.5 兼容性与变更纪律

| # | 纪律 |
|---|---|
| **PF1（历史记录）** | 原始起草轮次不修改任何既有规范文件；此项只记录当时的任务范围，不构成当前维护既有规范的限制 |
| **PF2** | 不新增 `VoxelType`；不修改 VoxelCore 与体素状态集合 |
| **PF3（历史限制已解除）** | 原始起草轮次对既有实现的零修改要求不是永久禁令。若变更触及已冻结的 Geology / Runtime / Streaming / Mesh / View / Bootstrap 契约，必须显式说明影响、兼容性与回归验证；必要时走授权与级联更新流程 |
| **PF4** | 若未来落地必须触及被冻结文件，走**显式授权 + 级联重钉**流程 |
| **PF5** | 参数契约不得被实现为"第二套规则"（禁止在契约层复述阈值或判定链） |

---

## §9 V1 范围

### 9.1 V1 只定义

| # | 只定义 | 形态 |
|---|---|---|
| 1 | **参数的身份与分类**（§2） | 四类 + 配置类 + 既有层间参数 |
| 2 | **参数定义表**（§3 表 A/B/C/D）：类型、范围、单位、默认值、来源、Owner、可修改层、可读取层、影响面 | 表驱动契约 |
| 3 | **权限规则与矩阵**（§4）：Producer / Owner / Consumer | 矩阵 + 自查 |
| 4 | **量纲与范围冻结**（§5）+ 既有实现映射表 | 冻结表 |
| 5 | **确定性与域分离编码**（§6）：域 ID 分配表 + 保留域 | 编码契约 |
| 6 | **Generation Parameter 与 Runtime State 的分离**（§7） | 边界规则 |
| 7 | **兼容检查与登记项**（§8） | 检查表 |
| 8 | **验收规则**（§10） | 机械检查 |

### 9.2 V1 不实现

| # | 不实现 | 说明 |
|---|---|---|
| 1 | **参数计算代码** | 不写 `.cs`、不建 class/struct/interface；计算规则归各层规范 |
| 2 | **缓存** | 不定义也不实现 |
| 3 | **查询接口** | 属 `WorldQuery_Design.md`；本契约只声明"谁能读" |
| 4 | **Runtime 同步** | 不定义参数如何进入/退出 Runtime（§7 只做分离声明） |
| 5 | **UI / 工具** | 不定义调试面板、可视化、编辑器扩展 |
| 6 | **存档格式** | 不定义参数的持久化（Generation Parameter 由 `(Seed, Domain, Position)` 可重算，无需存档） |
| 7 | **具体的阈值/噪声/优先级** | 属各层规范（`PF5`） |

### 9.3 长期架构边界

| # | 禁令 |
|---|---|
| **PY1** | 本契约不直接定义代码结构；具体 class/interface/struct 由对应实现规范负责。这不是禁止实现代码 |
| **PY2** | 不得只为消除文字差异而随意改写其他规范；如确认存在真实冲突，应按权威分工修订对应文件并同步交叉引用。这不是永久禁止修改既有规范 |
| **PY3** | 不新增 `VoxelType`；不改 VoxelCore |
| **PY4** | 不在契约层定义任何规则（阈值、优先级、判定链、噪声） |
| **PY5** | 不允许任何下游系统获得参数的写权限（§4） |
| **PY6** | 不允许使用未登记的域 ID（§6.4） |
| **PY7** | 不允许参数直接写 Voxel（`PW6`） |
| **PY8** | 不允许 Runtime 回写 Generation Parameter（§7） |

---

## §10 验收规则

> **编号说明**：本表的 `PC*` 属于**本契约**；引用时写成 `WorldParameter_Contract.md PC5`，以区别于 `WorldGeneration_Pipeline_Design.md` 的 `PC1–PC5`（骨架规则）。

### 10.1 权威与权限

| # | 不变量 | 验证方法 |
|---|---|---|
| **PC1** | **所有参数存在唯一 Owner**：§3 表 A/表 C 的"唯一权威"列非空且唯一；参数集合恰好等于 §3 定义的集合 | 表驱动检查（解析契约表，逐行断言 Owner 唯一且存在） |
| **PC2** | **不存在下游修改上游参数**：§4 矩阵中，写标记（`P`/`O`）只出现在 Producer/Owner 所在列；下游列（Biome/Geology/Ecology/Resource/WorldQuery/Runtime）写标记数 = 0 | 矩阵结构扫描（按 §4 图例解析，统计违规单元） |
| **PC3** | **参数范围唯一**：同一参数在任何规范/实现中出现的范围表述必须与 §5.2 完全一致 | 交叉检索（规范正文 + 源码常量）与 §5.2 比对 |
| **PC4** | **参数解释不存在分叉**：同一参数只有一个定义行；符号映射唯一（§5.3） | 符号表检查（同一参数名在同一文档内不得有两种范围/单位） |

### 10.2 因果与反写

| # | 不变量 | 验证方法 |
|---|---|---|
| **PC5** | **开关该规范不改变 Voxel 输出**：本文档增删/启用与否，体素输出逐体素一致（差异 = 0） | 两态体素快照比对（本文档为声明性文档，预期差异恒为 0） |
| **PC6** | **量纲冻结**：所有连续参数 ∈ `[0,1]`（`Balance` ∈ `[-1,1]`）；整数参数为世界坐标/标识 | 契约表校验 + 实现边界扫描 |
| **PC7** | **归一化映射显式且确定**：实现若使用其它内部量纲，必须存在 §5.3 类型的可逆映射，且映射是确定性整数/浮点运算 | 映射表存在性 + 双向round-trip断言 |
| **PC8** | **域 ID 唯一且已登记**：实现中出现的每个域 ID 都在 §6.4 表内；未登记域 = 0 | 域 ID 常量扫描（源码 + 契约表比对） |
| **PC9** | **保留域未被新层使用**：新层不得调用 `HeightFunction`/`GeologyNoise` 的既有通道作为自身随机源 | 结构扫描（新层不得引用保留域符号） |
| **PC10** | **参数不得由下游结果反推**：不存在"Biome/生态/资源/对象 → 参数"的写入路径 | 结构扫描（反向写入路径 = 0） |
| **PC11** | **读取集合封闭**：每层只读 §3 "可读取层"列中列出的参数；越权读取 = 0 | 权限矩阵校验 + 结构扫描 |
| **PC12** | **参数不新增体素状态**：不存在"参数 → 新 `VoxelType`"路径；对体素的唯一影响路径是 `Elevation → P2` 与 `Biome → P3 地表材料` | 结构扫描 + 差异集合检查（仅允许地表层材料变化） |
| **PC13** | **Runtime 不回写参数**：Runtime 列无写标记；不存在"玩家行为 → 参数"的路径 | 矩阵校验 + 结构扫描（§7 `PX2`） |
| **PC14** | **同 Seed + 同 Position ⇒ 同参数**（逐参数）：重复求值恒等；不同 `Seed` 至少一处不同（反例保护） | 双世界对照 + 逐参数比较 |
| **PC15** | **默认值显式且可覆盖**：§3 的默认值只允许被**显式配置参数**覆盖，不允许隐式环境/隐式全局状态 | 配置项检查（默认值必须出现在显式参数集合中） |
| **PC16** | **参数/域标识稳定**：参数名与域 ID 一旦发布不得重命名/改号；若必须变更，须升版本并提供映射表 | 版本与映射表检查 |

### 10.3 与既有验收的关系

| # | 说明 |
|---|---|
| **PC17** | 本契约的验收**不替代**既有 12 项实现验证；落地新层时既有验证（VoxelCore / WorldGeneration / Mesh / View / RuntimeCore / Streaming / Geology Stage A–E / GeologyRuntimeIntegration）必须保持 PASS |
| **PC18** | 本契约**不得**引入任何需要修改 VoxelCore / Geology 实现 / Runtime / Streaming / Mesh / View / Bootstrap 的要求（`PF2`/`PF3`） |

---

## 附录 A：数据流与权限速查

```
Seed（配置：Seed / WorldHeight / SeaLevel / ContinentCount / EpochCount / OrdinalMax / Base 值）
 │
 ├─ Phase 1 Skeleton ─────────────────────────► Continent · Ocean（派生）· Slope · Elevation（转发）
 │                                                   │ 只读（R）
 ├─ Phase 2 History ──────────────────────────► Epoch · Ordinal · HistoryScale · CivilizationPresence
 │                                                   │ 只读（R / R*）
 ├─ Phase 3 Region ───────────────────────────► 修正项（6 个参数各自的 Modifier）
 │                                                   │ 只读（R）
 ├─ Phase 4 WorldState（Owner：合成）─────────► Temperature · Moisture · Fertility ·
 │                                              MagicDensity · HolyInfluence · CorruptionInfluence · Balance（派生）
 │                                                   │ 只读（R）
 ├─ Phase 5 Biome ────────────────────────────► Biome（结论，不是参数）
 │                                                   │ 只读（R，仅 P3 给 Geology）
 ├─ Phase 6 Geology（Owner：地形真值）────────► Density · Material · CaveDensity · DepositZone
 │                                                   │ 只读（R）
 ├─ 旁路 Resource（Owner）────────────────────► ResourcePresence
 └─ 旁路 Ecology（Owner）─────────────────────► SpawnCandidate

WorldQuery：整条链只读（R），不产生参数（PW9）
Runtime：只读（R），不回写（PX2/PC13）

═════ 禁止：Biome→MagicDensity · Ecology→Fertility · Resource→WorldState · Entity→Generation ═════
```

## 附录 B：V1 参数清单（共 10 个必需 + 8 个次要/既有）

| 序 | 参数 | 类别 | Owner |
|---|---|---|---|
| 1 | `Temperature` | Environment | WorldState |
| 2 | `Moisture` | Environment | WorldState |
| 3 | `Elevation` | Environment（转发） | 高度场（Skeleton/Geology 共用） |
| 4 | `Fertility` | Environment | WorldState |
| 5 | `MagicDensity` | Influence | WorldState |
| 6 | `HolyInfluence` | Influence | WorldState |
| 7 | `CorruptionInfluence` | Influence | WorldState |
| 8 | `Ocean` | Geographic | Skeleton（派生规则） |
| 9 | `Continent` | Geographic | Skeleton |
| 10 | `Epoch` | Historical | History |
| 11 | `Ordinal` | Historical（次要） | History |
| 12 | `HistoryScale` | Historical（次要） | History |
| 13 | `CivilizationPresence` | Historical（次要） | History |
| 14 | `Slope` | Geographic（次要） | Skeleton |
| 15 | `Balance` | 派生（次要） | WorldState（§4.4） |
| 16–19 | `Density` / `Material` / `CaveDensity` / `DepositZone` | 既有层间（表 D） | Geology |
| 20 | `ResourcePresence` | 既有层间（表 D） | Resource |

> **未登记即禁止**（`PT8`）：任何未出现在本附录的跨系统共享量都不得传递。

## 附录 C：变更规则

1. 参数的新增/删除/改名、范围或默认值变更、Owner 变更、域 ID 分配变更，**必须**升版本并更新本附录 B、§3、§5.2、§6.4。
2. 不得放宽 §1.4 的 P1–P4，也不得放宽 §4 的写权限约束。
3. 不得以"实现方便"为理由修改契约范围（`PS11`）；实现侧只能增加边界映射（`PS8`）。
4. 与既有规范冲突时，先按 §8.3 登记，再由对应规范的**版本变更**解决；本文档不得直接改写其它规范。
5. 任何"动态参数"（随运行时演化的参数）提案必须**独立成规范**，且必须证明：确定性不受影响、既有验收全部 PASS、且**可关闭**。
