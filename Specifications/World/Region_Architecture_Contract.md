# Region Architecture Contract V1

> **文档性质**：**架构契约规范（Architecture Contract）**。本文档只定义 **Region 的数据契约与权限边界**：
> 身份、候选输入、采纳（Resolution）、修正（Modifier）合成、类型边界、Chunk/Voxel 边界、查询关系、确定性、局部求值、消费者权限。
> **仅创建 Markdown**；**不写代码**；**不创建 class / interface / struct**；**不修改任何已有规范**；**不修改 `Assets`**；**不新增 `VoxelType`**；**不改变已有因果方向**。
>
> **本文档要补全的缺环**（来自 `WorldGeneration_Architecture_Audit.md` **P0-3**）：
>
> > "History → Region Source 候选 → Region 采纳"的**采纳规则**未定义（去重 / 合并 / 上限 / 序位无关性）。
> > —— `WorldHistory_Design.md` §1.3 / PI1 只产出候选；`Region_Design.md` §3 只定义来源类别。
>
> **目标链（本文档补全其中三段）**：
>
> ```
> RegionSourceCandidate   ← 已有（WorldHistory_Design / Region_Design §3.2）
>         ↓  ① 准入 · ② 规范化 · ③ 全序 · ④ 上限 · ⑤ 物化 · ⑥ 发布     ← 本文档补全（§4）
>      Region             ← 已有概念（Region_Design §1/§4），本文档补身份契约（§2）
>         ↓  加法叠加 + 钳制                                            ← 本文档补合成契约（§5）
>   RegionModifier          ← 已有（Region_Design §2.1/RM/RO），本文档补写入权限（§5.4）
>         ↓
>     Consumer              ← 本文档补权限矩阵与查询关系（§8/§11）
> ```
>
> **必须兼容的上游规范**：
>
> | 规范 | 与本契约的关系 |
> |---|---|
> | `Region_Design.md` | **Region 规则的唯一权威**：术语对齐（RT1–RT6）、来源（RS1–RS6/RC1–RC5）、修正（RM1–RM6/RP1–RP3/RE1–RE5）、类型（RTY1–RTY5）、Biome 关系（RB1–RB6）、标签（RH1–RH5）、边界（RBD1–RBD8）、叠加（RO1–RO7）、世界尺度（RW1–RW6）、V1 禁令（RV1–RV8）、间接路径（RI1–RI4）、验收（RG/RGV/RGS） |
> | `WorldHistory_Design.md` | **候选的上游**：§1.3 输出（`Region Source Candidates`）、PI1（采纳归 Region 层）、PI2、HE1–HE6（纪元/序位，**禁止覆盖语义**）、HN9/HN10（规模上限必须显式）、HC8 |
> | `WorldParameter_Contract.md` | **参数契约**：§3.2 表 A（Region 对 6 个参数为 **P（修正项）**、对 `Elevation`/`Ocean` 为 **R**）、§4 权限矩阵、§5.2 范围冻结、**§6.4 域 ID 分配表（`Region.Natural`=8 … `Region.Relic`=12）**、§6.3 PZ12（保留域不得复用）、§8.3（登记的对齐项）、PC1–PC18（验收） |
> | `WorldQuery_Design.md` | **查询权威**：§2.2 第 3 步（Region Query 的输入/输出）、§3.1（Region 行权限）、WP1–WP6、WL1–WL13（局部求值）、WN1–WN10、WE1–WE8（解释）、WA1–WA20（验收） |
> | `WorldGeneration_Architecture_Audit.md` | **本契约要解决的审查结论**：P0-3（采纳缺环）、P1-2（`Region.EcologyRules` 无产生规则，仅**部分**关闭，见 §12.3）、AD-1…AD-6、M2（Region 层落地顺序） |
> | `WorldGeneration_Pipeline_Design.md` | Phase 3 的定位与顺序（§2.1/§2.2）、附录 B.1 权限矩阵、附录 C RB-1…RB-14、PG1–PG14 |
> | `WorldState_Design.md` | `Base + RegionModifier` 的合成语义（§2.1）、群系规则表（§5.3）、退化兼容（§5.8） |
> | `WorldGeneration_Geology*.md`、`VoxelWorld_Core.md`、`ADR-001` | 地形真值唯一权威、体素状态集合冻结、Entity/Chunk 概念边界 |
>
> **冲突时的优先级**：**Region 的规则语义**（来源类别、修正集、边界形状要求、叠加、类型定义）以 `Region_Design.md` 为准；
> **参数的量纲/Owner/读写权限**以 `WorldParameter_Contract.md` 为准；
> **查询语义**以 `WorldQuery_Design.md` 为准；
> **本契约负责**：采纳流程、Region 身份、Modifier 写入权限、类型与 Chunk/Voxel 边界、Region 侧的确定性与局部求值契约。
>
> **零修改声明**：本文档是**声明性文档**。它不产生任何运行时效果；开启/关闭本文档**不得改变任何体素输出**（`RAV12`）。
> 与既有规范的措辞差异一律在 §12.3 **登记**，**不修改**任何既有文件。

---

## §1 定位与契约边界

### 1.1 定义

> **Region Architecture Contract = Region 层的"输入 → 采纳 → 影响 → 消费"数据契约。**

| 问题 | 由谁回答 |
|---|---|
| Region 的**规则**是什么（来源类别、修正集、边界要求、叠加规则）？ | `Region_Design.md` |
| **候选怎么变成 Region**（准入/去重/合并/上限/物化）？ | **本文档 §4** |
| Region 的**身份**如何确定？ | **本文档 §2** |
| 谁能**写** `RegionModifier`？ | **本文档 §5.4** + `WorldParameter_Contract.md` §4 |
| Region 如何被**查询**与**解释**？ | **本文档 §8** + `WorldQuery_Design.md` |
| Region 与 **Chunk / Voxel / Entity** 的边界？ | **本文档 §7** |

### 1.2 契约范围（硬性）

| # | 规则 |
|---|---|
| **RA1** | 本文档**不定义任何 Region 规则**（阈值、衰减形状、来源类别语义）；只定义**数据形态、流水线步骤、权限与验证口径** |
| **RA2** | 本文档**不新增**任何因果边：本文档使用的边必须是既有链的子集（`来源 → Region → Modifier → FinalState → 表现/资格`，`Region_Design.md` RG-B） |
| **RA3** | 本文档**不新增** `VoxelType`，不改变体素状态集合（Air/Grass/Dirt/Stone） |
| **RA4** | 本文档**不拥有**参数：参数的量纲/Owner/读写权限以 `WorldParameter_Contract.md` 为准 |
| **RA5** | 本文档**不定义**查询入口：查询语义以 `WorldQuery_Design.md` 为准 |
| **RA6** | 所有采纳与合成步骤必须**确定性**且**与遍历顺序无关**（§9/§10/`RAV3`/`RAV4`） |
| **RA7** | Region 层**不得**读取任何下游产物（Biome / 生态候选 / 资源判定 / 体素 / Chunk 状态 / 玩家状态） |
| **RA8** | Region 层**不得**产生对象（Chunk / Mesh / Entity / 结构 / 植被）——`Region_Design.md` `RGS` |

### 1.3 目标链条（本文档补全的三段）

```
┌──────────────────────────┐
│ RegionSourceCandidate    │  ← 已有：WorldHistory_Design §1.3（History 产出）
│ （History 事实 + 自然规则）│      Region_Design §3.2（自然来源）
└───────────┬──────────────┘
            │  本文档 §4：① 准入 → ② 规范化（去重/合并）→ ③ 确定性全序 → ④ 上限 → ⑤ 物化 → ⑥ 发布
            ▼
┌──────────────────────────┐
│ Region                   │  ← 本文档 §2 补身份契约（RegionKey / 组成 / 非容器）
│ （Source + Extent + Profile + Tags + Qualifications）│
└───────────┬──────────────┘
            │  本文档 §5：逐分量加法叠加 + 钳制（唯一写入者 = Region 层）
            ▼
┌──────────────────────────┐
│ RegionModifier           │  ← 6 个分量（不含 Elevation / Ocean，RE1）
└───────────┬──────────────┘
            │  合成权归 WorldState（WorldParameter_Contract.md §4：WorldState = O（合成））
            ▼
   FinalState（8 参数）→ Biome（唯一规则表）→ Geology（仅 P3 地表材料）
            │
            └──► Consumer：Biome · Geology(P3) · Ecology · Resource · WorldQuery · Runtime

  ═════ 反向箭头一律禁止（RA2 / RAX-*）═════
```

---

## §2 Region 定义契约

### 2.1 定义（沿用上游，不重新定义）

> **Region 是连续空间中的世界状态解释单元**（`Region_Design.md` §1.1），是**原因侧**（RT1），**不是** Biome、不是容器、不是存档单位、不是跨帧状态（RT3）。

### 2.2 Region 的组成（概念字段，**不是数据结构**）

| 组成 | 含义 | 约束 |
|---|---|---|
| **RegionKey** | 身份键（见 §2.3） | 确定性；唯一标识一个 Region |
| **Source** | 原因记录：来源类别（自然 / 魔法 / 圣 / 腐化 + 子类）、锚点位置、强度、尺度 | 必须来自 ≥1 个候选（`RG-A`） |
| **Extent** | 影响空间：紧支撑半径 + 衰减形状**标识**（形状本身属 Region 规则） | 必须满足 `RBD1`/`RBD3`（随距离下降、紧支撑、边界连续） |
| **ModifierProfile** | 该 Region 对 6 个分量的**强度侧写**（方向与幅度上界） | 分量集合封闭（§5.2）；不得含地形量 |
| **Tags** | 只读标签：来源类别、纪元/序位、引用到的 `HistoryTags` | 只读；不得作为环境参数求值输入（`RH3`） |
| **Qualifications** | 区域级资格声明（形状见 §5.5） | 只读；不得生成对象（`RGS`）；不得参与参数求值 |

### 2.3 RegionKey（身份契约）

| # | 规则 |
|---|---|
| **RA9** | `RegionKey` 必须由**客观、确定性、整数可比**的量组成：`(DomainId, Epoch, Ordinal, CanonicalX, CanonicalY, CanonicalZ)` |
| **RA10** | `DomainId` 必须来自 `WorldParameter_Contract.md` §6.4 的 Region 域（`8` `Region.Natural` … `12` `Region.Relic`）；**未登记域不得使用** |
| **RA11** | `Epoch`/`Ordinal` 必须来自 `WorldHistory_Design.md` 的纪元体系（HE1–HE4）；自然来源若不含纪元信息，必须使用**显式保留值**（例如 `Epoch = EpochCount`，即"非历史纪元"），不得省略 |
| **RA12** | `CanonicalPosition` 必须由来源锚点位置经**显式量化**得到：`Canonical = Quantize(Position, QuantumVoxels)`，`QuantumVoxels` 是显式参数（默认 `1`）；量化必须是确定性整数运算 |
| **RA13** | `RegionKey` 一旦确定**不得改变**（Region 不迁移、不漂移、不随时间变化） |
| **RA14** | `RegionKey` 是**去重键**（§4.2）；**不得**参与修正叠加的语义（叠加是集合级加法，不看键） |
| **RA15** | 任何**派生标识**（例如由键计算的可读 ID/哈希）只允许用于**标识、调试、解释输出**；**不得**作为规则输入、**不得**作为排序键（§4.3） |

### 2.4 Region 的形态（硬性）

| # | 规则 |
|---|---|
| **RA16** | Region 是**可由 `(Seed, Domain, Position)` 求值的查询结果**；不是容器、不是存档单位、不是跨帧存活对象（RT3） |
| **RA17** | 任意世界坐标必须能**独立**求出"该点受哪些 Region 影响、各修正多少"（`RP2`/`RS3`） |
| **RA18** | Region 的空间属性不得依赖 Chunk 划分、加载顺序、帧序、时间（`RP3`） |
| **RA19** | Region **不得**自举：Region 不得产生新的 `RegionSourceCandidate`（禁止"由区域生成新的区域"） |
| **RA20** | Region **不得**被任何下游修改：Region 集合、Extent、ModifierProfile、Tags、Qualifications 均为**只读发布**（`RGS`） |

---

## §3 RegionSourceCandidate 契约（输入侧）

### 3.1 候选的两个来源（都进入同一候选集合）

| 来源 | 生产者 | 依据 |
|---|---|---|
| **历史事件 → 候选** | `History`（Phase 2） | `WorldHistory_Design.md` §1.3 第 4 项、PI1、§2.6（事件类型 → 候选映射） |
| **自然规则 → 候选** | `Region` 层的自然来源（Phase 3 内部，来自骨架事实） | `Region_Design.md` §3.2「自然来源」（气候/海洋/山脉/河流/土壤） |

### 3.2 候选的必需字段（概念）

| 字段 | 含义 | 来源/约束 |
|---|---|---|
| **Kind** | 来源类别（自然子类 / 魔法子类 / 圣 / 腐化子类） | `Region_Design.md` §3.2；子类枚举**不得**由本文档新增 |
| **DomainId** | 该候选的确定性域 | `WorldParameter_Contract.md` §6.4（8–12） |
| **Position** | 锚点世界坐标 | 必须是世界坐标；不得是 Chunk 局部坐标 |
| **Strength** | 强度倾向 | 显式标量；范围由 Region 规则声明，且必须落在契约允许域内 |
| **Radius / Scale** | 影响尺度 | 世界单位；必须 > 0 |
| **Epoch / Ordinal** | 时间序位 | 来自 History；自然来源用显式保留值（`RA11`） |
| **OriginRef** | 原因引用（历史事件引用 / 自然规则引用） | 用于解释链（`WE*`）；**必须可解析** |
| **ReasonChainRef** | 可解释链引用 | 供 `WorldQuery_Design.md` §6 解释使用 |

### 3.3 候选的硬性约束

| # | 规则 |
|---|---|
| **RA21** | 候选必须是 `(Seed, Domain, Position)` 的确定性函数（`RS2`） |
| **RA22** | 候选必须**可局部枚举**：给定世界坐标与半径，无需遍历全世界即可确定相关候选（`RS3`） |
| **RA23** | 候选**不得**携带下游产物（Biome / 生态候选 / 资源判定 / 体素值 / Chunk 状态） |
| **RA24** | 候选**不得**携带"目标结果"（例如"这里应该长成魔法森林"）——候选只描述**原因**（`PT13`/`RC1`） |
| **RA25** | 候选**不得**依赖玩家状态、加载顺序、帧序、时间、`Random`（`WorldHistory_Design.md` HD7–HD12） |
| **RA26** | 候选在提交时**不做空间合并**：合并统一发生在采纳阶段（§4.2），以保证合并规则唯一 |
| **RA27** | 候选集合的规模必须受**显式上限**约束（`WorldHistory_Design.md` HN9/HN10；`Region_Design.md` RO7） |
| **RA28** | 候选的**缺失**必须是可解释的（"为什么这里没有候选"可回答；`WorldQuery_Design.md` §6.1 第 4 项） |
| **RA29** | 候选的**时间语义不得引入覆盖**：多纪元候选**同时成立**（`HE5`/`HG4`；禁止"后来的覆盖先前的"） |
| **RA30** | 候选不得由结果反推产生（禁止"看到腐化区域 ⇒ 补写亡灵灾难"，`HG6`/`RC1`） |

---

## §4 Region Resolution 契约（**核心：候选 → Region**）

> 本节是 `WorldGeneration_Architecture_Audit.md` **P0-3** 的补全。六个步骤必须**按序执行**，且**每一步都是候选集合的纯函数**（与遍历顺序无关）。

### 4.1 步骤 ①：准入（Admissibility）

| # | 准入条件（全部必须成立） |
|---|---|
| **RA31** | 候选的 `Kind` 属于**已登记来源类别**（`Region_Design.md` §3.2；未登记类别一律拒绝） |
| **RA32** | 锚点位置在世界垂直边界内：`0 ≤ Position.y < WorldHeight`（水平方向不设边界，`RW2`） |
| **RA33** | 陆地/海洋一致性：陆地类候选**不得**落在 `Ocean == true` 处；海洋类候选必须由**显式**海上/水下类别声明（`Region_Design.md` §3.2 未列出的类别不得擅自新增） |
| **RA34** | `Strength`、`Radius/Scale` 落在 Region 规则声明的范围内（越界拒绝，**不静默钳制**——钳制只用于**叠加结果**，§5.1） |
| **RA35** | 候选不携带任何被禁止的输入（`RA23`/`RA24`） |
| **RA36** | 准入结果与候选枚举顺序**无关**（顺序无关性由 §4.7 的交换律验收保证） |

### 4.2 步骤 ②：规范化（Canonicalization：去重与合并）

| # | 规则 |
|---|---|
| **RA37** | **去重键** = `(Kind 所属 DomainId, Epoch, Ordinal, CanonicalPosition)`；同一键的候选属于**同一 Region** |
| **RA38** | **Strength 合并**：采用 **有界求和** `min(StrengthCap, Σ Strength_i)`（`StrengthCap` 为显式参数）；**必须**在实现规范中固定并全局一致 |
| **RA39** | **Radius/Scale 合并**：取 `max`（单调不减）；不得取平均（会破坏"越靠越强"的单调性验收 `RAV9`） |
| **RA40** | **OriginRef 合并**：**保留全部**来源引用（不得有损合并）；输出顺序必须由确定性全序（§4.3）决定 |
| **RA41** | **Tags 合并**：集合并集（去重）；输出顺序确定性 |
| **RA42** | **禁止**"后来的覆盖先前的"以及任何**覆盖语义**（`HE5`）；合并只允许"累加/取上界/并集"三类操作 |
| **RA43** | **禁止放大反馈**：合并不得因为"附近已有 Region"而改变本次合并的强度（`RO5`） |

### 4.3 步骤 ③：确定性全序（Ordering）

| # | 规则 |
|---|---|
| **RA44** | 必须定义**全序键**：`(DomainId, Epoch, Ordinal, CanonicalY, CanonicalZ, CanonicalX)`（整数可比；**不得**使用派生哈希/ID 排序，`RA15`） |
| **RA45** | 全序**只允许**用于：① 上限截断（步骤 ④）；② 枚举输出；③ 解释输出。**禁止**用于改变合并语义（否则破坏 `RO1` 交换律） |
| **RA46** | 全序必须与"求值顺序/查询顺序"无关：任何实现都必须能对同一候选集合给出**同一**全序 |

### 4.4 步骤 ④：上限（Capping）

| # | 规则 |
|---|---|
| **RA47** | 上限必须**显式参数化**（`WorldHistory_Design.md` HN9；`Region_Design.md` RO7）：至少包含"每大陆每个 DomainId 的 Region 数量上限"与"单点参与叠加的 Region 数量上限" |
| **RA48** | 截断必须按 §4.3 全序进行（确定性）；**被截断的候选必须可被解释**（"因上限未采纳"必须可回答，`RA28`） |
| **RA49** | 上限**不得**由下游结果决定（例如"因为这里已经有生物所以减少区域"一律禁止） |

### 4.5 步骤 ⑤：物化（Materialization）

| # | 规则 |
|---|---|
| **RA50** | 物化 = 由合并后的候选生成 **Region 记录**（§2.2 组成）：`RegionKey`（§2.3）、`Source`、`Extent`、`ModifierProfile`、`Tags`、`Qualifications` |
| **RA51** | `Extent` 必须由 `Radius/Scale` + **显式衰减形状标识** 决定；形状要求沿用 `RBD1`/`RBD3`（紧支撑、单调不增、边界连续趋于 0） |
| **RA52** | `ModifierProfile` 的**分量映射**必须遵守 §5.2 的封闭表：一个来源类别只允许影响其登记的分量 |
| **RA53** | `Qualifications` 的推导输入只允许是 `(Kind, ModifierProfile, 显式参数)`；**不得**读取 Biome/生态/资源/体素（§5.5） |

### 4.6 步骤 ⑥：发布（Publication）

| # | 规则 |
|---|---|
| **RA54** | Region 集合以**只读**形式发布；发布后不得被任何系统修改（`RA20`） |
| **RA55** | 发布必须支持**双向可追溯**：`Region → OriginRef 集合` 与 `OriginRef → RegionKey`（解释与验收要求，`RAV2`） |
| **RA56** | 发布**不得**产生任何副作用：不写体素、不创建 Chunk、不置脏、不生成对象（`RA8`/`RGS`） |
| **RA57** | 发布必须支持**单点查询**：给定坐标可确定"影响该点的 Region 子集"（§10） |
| **RA58** | 发布**不得**要求"全量 Region 表"作为前置（§10；`WorldQuery_Design.md` WL4） |

### 4.7 采纳阶段的顺序无关性（硬性）

| # | 规则 |
|---|---|
| **RA59** | **候选 enumeration 顺序不得影响采纳结果**：对同一候选集合，任意枚举顺序必须得到**逐字段相同**的 Region 集合 |
| **RA60** | **采纳结果不得影响叠加结果之外的任何东西**：采纳只决定"哪些 Region 存在及其 Profile"，不决定"最终参数值"（最终值由 §5 合成） |
| **RA61** | 采纳**不得**依赖任何查询状态（谁先被查询、缓存是否命中） |

---

## §5 RegionModifier 合成契约与权限

### 5.1 合成公式（沿用上游）

```
RegionModifier(p) = Σ_r Contribution_r(p)          （逐分量，加法）
FinalState(p)     = BaseWorldState(p) + RegionModifier(p)   （合成权归 WorldState）
```

| # | 规则 |
|---|---|
| **RA62** | 合成**只有加法 + 钳制**（`RO1`/`RO4`；`WorldState_Design.md` MF1/MF5） |
| **RA63** | 合成必须满足**交换律**：与 Region 遍历顺序无关（`RO1`） |
| **RA64** | 每个分量合成后必须钳制到契约范围（`WorldParameter_Contract.md` §5.2）：连续量 `[0,1]` |
| **RA65** | 单点参与合成的 Region 数量必须由显式上限约束（`RO7`/`RA47`） |
| **RA66** | 合成**不得**产生"放大反馈"（某 Region 不得因为附近参数高而增强，`RO5`） |

### 5.2 分量封闭表（唯一允许的映射）

| 来源类别 | 允许影响的分量 | 依据 |
|---|---|---|
| 自然（气候/海洋/山脉/河流/土壤） | `Temperature` / `Moisture` / `Fertility` | `Region_Design.md` §3.2 自然来源 |
| 魔法（6 类魔法来源） | `MagicDensity` | `Region_Design.md` §3.2；`WorldState_Design.md` §3 |
| 圣性（圣物/圣地/圣生物聚集） | `HolyInfluence` | `Region_Design.md` §3.2；`WorldState_Design.md` §4 |
| 腐化（墓地/邪恶遗迹/污染区域） | `CorruptionInfluence` | `Region_Design.md` §3.2；`WorldState_Design.md` §4 |

| # | 规则 |
|---|---|
| **RA67** | 分量集合**恰好为 6 个**：`Temperature`、`Moisture`、`Fertility`、`MagicDensity`、`HolyInfluence`、`CorruptionInfluence` |
| **RA68** | **禁止**新增第 7 个分量（新增必须按 `Region_Design.md` §4.2 修改规则集合并升版本） |
| **RA69** | **`Elevation` 与 `Ocean` 不在修正集内**（`RE1`）：Region 永远不能改变地形高度与海陆边界 |
| **RA70** | `Balance` **不是**可修正分量：它是派生只读量（`WorldState_Design.md` §4.4）；Region 只能分别影响 `HolyInfluence` 与 `CorruptionInfluence`，**不得**直接写 `Balance` |
| **RA71** | 两个力量分量必须**独立**叠加：任何"用腐化抵消圣性后写回"的实现一律禁止（`Region_Design.md` §9.3、`RO2`） |

### 5.3 贡献形状（由 Region 规则决定，本契约只固定"必须满足"）

| # | 规则 |
|---|---|
| **RA72** | 单 Region 贡献必须：**紧支撑**（超出 `Radius` 恒为 0）、**单调不增**、**边界连续趋于 0**（`RBD1`/`RBD3`） |
| **RA73** | 贡献不得含"与位置无关的常量偏置"（那是 Base 的职责，不是 Region 的职责） |
| **RA74** | 衰减形状参数必须显式（不得依赖平台相关噪声，`RBD6`/`D5`） |

### 5.4 写入权限（**核心：谁能写 Modifier**）

| 角色 | 系统 | 权限 |
|---|---|---|
| **Producer（修正项）** | **Region 层（唯一）** | 产出 `RegionModifier` 的 6 个分量（`WorldParameter_Contract.md` §4 矩阵：Region 行 = `P（修正项）`） |
| **Owner（合成）** | **WorldState 层** | `FinalState = Base + RegionModifier`（该矩阵：WorldState 行 = `O（合成）`） |
| **Consumer（只读）** | Biome · Geology（仅 P3）· Ecology · Resource · WorldQuery · Runtime | 只读；**禁止**写入任何分量（`PW2`） |

| # | 规则 |
|---|---|
| **RA75** | `RegionModifier` 的**唯一写入者是 Region 层**；任何其它系统写入 = 缺陷（`PW2`/`PX2`） |
| **RA76** | WorldState **只做合成**，不得发明修正项、不得修改 Region 的 Profile（`RM5`） |
| **RA77** | 下游（Biome/Ecology/Resource/Query/Runtime）**不得**通过任何路径影响 `RegionModifier`（`PX1`/`PF5`） |
| **RA78** | Runtime **不得**回写：运行时的体素编辑与玩家状态属 Runtime State，**不是**修正项（`WorldParameter_Contract.md` §7 `PX2`） |
| **RA79** | 禁止"由结果反推修正"：不得用 `RegionView`/Biome/生态/资源结果反推 `RegionModifier`（`RT6`/`RC2`） |

### 5.5 Qualifications（区域级资格声明）——**部分关闭审查 P1-2**

| # | 规则 |
|---|---|
| **RA80** | `Qualifications` 的形态固定为：一组 `(Category, Allowed)` 声明，`Allowed ∈ {允许, 不允许, 未声明}`；`Category` 必须来自**显式登记的类别集合** |
| **RA81** | 推导输入**只允许**：`Kind`（来源类别）、`ModifierProfile`、显式参数；**禁止**读取 Biome / 生态候选 / 资源判定 / 体素 |
| **RA82** | `Qualifications` 是**只读输出**：不得参与环境参数求值（`RH3`）、不得生成对象（`RGS`）、不得被回写（`RA20`） |
| **RA83** | `Qualifications` 必须确定性、可重复求值，且可被查询解释（`WE*`） |
| **RA84** | **仍未关闭的部分（登记，不改文件）**：`Category` 的具体集合与"来源 → 资格"的详细映射表属实现规范内容；在补齐前，本文档只固定**形状与输入约束**（见 §12.3 第 ② 项） |

---

## §6 Region 与 RegionType 的边界

### 6.1 定义

| 项 | 内容 |
|---|---|
| `RegionType` | **解释标签**（例如 `Forest` / `Desert` / `Mountain` / `MagicForest` / `CorruptedLand` / `HolyLand` / `Ocean`，`Region_Design.md` §4.3） |
| 性质 | **描述性**：由来源类别 + 修正侧写 + 最终环境**推导**得到 |
| 权威 | 取值集合与示例归 `Region_Design.md` §4.3（**开放式列举**）；本契约只约束其**使用边界** |

### 6.2 硬性边界

| # | 规则 |
|---|---|
| **RA85** | `RegionType` **没有任何规则权**（`RTY2`）：任何规则（环境求值、群系解析、资源判定、生态资格、遗迹判定）**都不得读取它** |
| **RA86** | 禁止 `RegionType → RegionModifier`（不得"因为类型是魔法森林所以提高魔力"，`RTY3`/`RC5`） |
| **RA87** | 禁止 `RegionType → 采纳`：`RegionType` 不得参与准入、去重键、合并、全序、上限（§4） |
| **RA88** | 禁止 `RegionType → Biome / Resource / Ecology`（`WorldQuery_Design.md` WP3；`Resource_Design.md` `RX-2`） |
| **RA89** | 禁止由 `RegionType` 反推来源、修正或历史（`RT6`/`RC2`；P4） |
| **RA90** | `RegionType` 的**唯一允许用途**：解释输出（`WorldQuery_Design.md` §6）、调试、工具、只读历史标签 |
| **RA91** | `RegionType` 必须**确定性推导**（同输入同结果），不得人工干预、不得随机（`RTY4`） |
| **RA92** | `RegionType` **不等于** `Biome`（`RTY5`/`RB2`）：前者是世界原因侧的解释标签，后者是环境表现结论；两者不得互相替换 |

---

## §7 Region 与 Chunk / Voxel 的边界

### 7.1 与 Chunk 的边界

| # | 规则 |
|---|---|
| **RA93** | **禁止**任何"按 Chunk 划分/存储 Region"的结构（禁止每 Chunk Region 表、禁止 Chunk → Region 归属表；`RBD5`） |
| **RA94** | Region 边界**不得**与 Chunk 边界对齐或依赖 Chunk 尺寸（`RP3`） |
| **RA95** | Region 的采纳、合成、查询**不得**读取 Chunk 生命周期（加载/卸载/脏/预算）；Region **不出现在任何调度接口中**（`Region_Design.md` §12.3） |
| **RA96** | 单点 Region 查询**不得**要求"该 Chunk 已加载"或"已生成"（`WorldQuery_Design.md` WL1/WL9；`RA57`） |
| **RA97** | Region 求值**不得**创建 Chunk、不得置脏、不得触发 Mesh（`RA56`） |
| **RA98** | 跨 Chunk 边界的 Region 影响必须**连续**：相邻坐标的修正差值必须有界（`RBD3`；`RAV20`） |

### 7.2 与 Voxel 的边界

| # | 规则 |
|---|---|
| **RA99** | Region **不得写体素**：不得调用任何体素写入路径（`RI4`/`RGS`） |
| **RA100** | Region **不得新增 `VoxelType`**，不得要求新的地表材料（V1 冻结为 Air/Grass/Dirt/Stone） |
| **RA101** | **唯一允许的"Region → 体素"路径**：`Region → RegionModifier → FinalState → Biome（唯一规则表）→ Geology 的 P3 地表材料`（`RI1`–`RI3`） |
| **RA102** | 该路径**只作用于地表层**（`y == SurfaceHeight`），取值限 `{Grass, Dirt, Stone}`；不得扩展到其它层（`WorldGeneration_Geology_Runtime_Implementation.md` §6.1） |
| **RA103** | Region **不得**影响 `Density` / `CaveDensity` / 地层结构 / `DepositZone`（§12.1 检查项） |
| **RA104** | 关闭 Region 层（或不存在 Region）时，**体素输出必须与今天逐体素一致**（`RI3`；`RAV12`） |

---

## §8 Region 与 WorldQuery 的关系

### 8.1 定位

| 项 | 内容 |
|---|---|
| Query 的角色 | **Region 的唯一只读消费入口**（查询语义权威 = `WorldQuery_Design.md`） |
| Region 的角色 | 提供**只读数据**：影响该点的 Region 子集、各 Region 的贡献、合成后的 Modifier、解释链 |

### 8.2 Query 必须能回答的 Region 内容

| # | 必须可回答 |
|---|---|
| **RA105** | 该点**受哪些 Region 影响**（按 §4.3 全序输出），以及每个 Region 的 `RegionKey` 与来源类别 |
| **RA106** | 每个 Region 在**每个分量**上的**贡献值**（逐分量可分解，`RAV15`） |
| **RA107** | 合成后的 `RegionModifier`（6 个分量）以及 `FinalState` 中"哪些部分来自 Region"（与 Base 可区分） |
| **RA108** | **解释链**：`用户问题 ← 结论 ← 条件（参数值与阈值）← Region（RegionKey/来源类别/纪元）← RegionSourceCandidate ← 历史事件或自然规则`（与 `WorldQuery_Design.md` §6 `WE1`–`WE8` 一致） |
| **RA109** | **"为什么这里没有 X"**：必须能指出"没有候选 / 候选被准入拒绝 / 被上限截断 / 影响不足"中的**具体一条**（`RA28`/`RA48`） |

### 8.3 Query 对 Region 的权限（硬性）

| # | 规则 |
|---|---|
| **RA110** | Query **只读**：不得创建、修改、删除 Region；不得修改 `RegionModifier`（`WP5`；`WorldParameter_Contract.md` §4 矩阵：WorldQuery 列全 `R`） |
| **RA111** | 查询缓存（若实现）必须**透明**：由 `(Seed, 参数, 世界坐标, 域标签)` 索引；可随时清空而结果不变；不得成为权威（`WL9`/`WL10`） |
| **RA112** | Query 不得因为"查询过"而改变后续查询结果（`RA61`；`WL12`） |
| **RA113** | Query 不得要求"全量 Region 表"（`WL4`；`RA58`） |
| **RA114** | Query 的解释输出不得成为任何规则的输入（解释是终端产物；`WQ4`） |

---

## §9 Region 确定性契约

### 9.1 输入契约

| # | 规则 |
|---|---|
| **RA115** | 每个 Region 产物（候选采纳结果 / Region / Modifier 分量 / Qualifications）必须由 `(Seed, DomainId, Position)` 唯一确定（`RM1`；`WorldParameter_Contract.md` §6.1 `PZ1`） |
| **RA116** | `DomainId` 必须使用 `WorldParameter_Contract.md` §6.4 的 Region 域：`Region.Natural`=8、`Region.Magic`=9、`Region.Holy`=10、`Region.Corruption`=11、`Region.Relic`=12 |
| **RA117** | `Domain` 必须**参与哈希混合**（`PZ11`）；**禁止**复用保留域 `R-1`/`R-2`（`HeightFunction`/`GeologyNoise` 的既有通道，`PZ12`） |
| **RA118** | 不同域必须**统计独立**：改变 `Region.Magic` 的域参数**不得**改变 `Region.Corruption` 的任何输出（`PZ13`；`RAV16`） |

### 9.2 禁止事项

| # | 禁止 |
|---|---|
| **RA119** | `Random()` / `UnityEngine.Random` / `System.Random` / 任何有状态伪随机源（`PZ5`） |
| **RA120** | `Time` / `DateTime` / 时间戳 / 真实时长 / tick / 帧序 / `Guid`（`PZ6`） |
| **RA121** | 玩家状态（位置、进度、背包、已探索、已采集）（`PZ7`） |
| **RA122** | 加载顺序、已加载 Chunk 集合、世界级中间态、**遍历顺序**、**查询顺序**（`PZ8`/`WL12`） |
| **RA123** | 由下游结果反推 Region 或修正（`RT6`/`RC2`；`PZ9`） |
| **RA124** | 时间覆盖语义（"后来的覆盖先前的"）与胜负演化（`HE5`/`HG4`） |

### 9.3 反例保护（防止恒真断言）

| # | 规则 |
|---|---|
| **RA125** | 验收必须能证明**非平凡性**：不同 `Seed` 或不同显式参数必须产生**至少一处**不同的 Region 集合或 Modifier（`RAV17`） |
| **RA126** | 验收必须能证明**采纳确实发生**：构造至少一个"候选集合非空 ⇒ Region 非空"的对照（防止"永远空集"的假通过） |

---

## §10 Region 局部求值契约

### 10.1 单点求值

| # | 规则 |
|---|---|
| **RA127** | 单点 Region 查询必须能**独立完成**：不依赖任何"世界级"前置步骤（`WL1`） |
| **RA128** | 给定世界坐标 `p`，必须能确定性枚举"影响 `p` 的 Region 集合"，且**代价与该处相关 Region 数量相关**，与"世界 Region 总量"无关（`WL11`；`RS3`） |
| **RA129** | 枚举的判定必须**纯函数化**：候选的局部枚举必须由显式规则（例如显式量化网格 + 半径判定）给出，且该规则必须满足"覆盖完备 + 无重复 + 确定性" |
| **RA130** | 枚举上限（单点参与叠加的 Region 数量）必须显式（`RA47`） |

### 10.2 禁止事项

| # | 禁止 |
|---|---|
| **RA131** | **禁止生成整个世界**以回答一个点（`WL3`） |
| **RA132** | **禁止保存所有 Region**（不得要求"先建立全局 Region 表"，`WL4`） |
| **RA133** | **禁止保存所有 Biome**（`WL5`）；Region 层**不得**展开世界级群系划分 |
| **RA134** | 禁止全局注册表/全局候选表作为查询前置（`WL6`） |
| **RA135** | 禁止要求"遍历全部候选"或"连接所有大陆"（`WL7`） |
| **RA136** | 禁止依赖"邻域是否已求值/已查询"（`WL8`） |
| **RA137** | 禁止跨帧存活状态：连续两次查询之间不需要"世界实例"（`WL13`；`RA16`） |

### 10.3 缓存（若实现）

| # | 规则 |
|---|---|
| **RA138** | 缓存必须：由 `(Seed, 参数, 世界坐标, 域标签)` 索引、**不改变输出**、可清空、不成为权威（`WL9`） |
| **RA139** | 禁止跨 `Seed`/跨参数复用缓存（`WL10`） |

---

## §11 消费者权限矩阵（Region 侧产物 × 系统）

**图例**：`P` = 产生（Producer）；`O` = 拥有最终解释权/合成（Owner）；`R` = 只读；`R*` = **仅归因**只读（不得作判定条件）；`—` = 禁止。

| Region 侧产物 ＼ 系统 | Seed | History | Region | WorldState | Biome | Geology | Ecology | Resource | WorldQuery | Runtime |
|---|---|---|---|---|---|---|---|---|---|---|
| **RegionSourceCandidate** | — | **P** | R（采纳输入） | — | — | — | R\*（仅归因） | R\*（仅归因） | R | R |
| **Region**（含 RegionKey / Extent / Profile / Tags） | — | — | **O** | R | R | R | R | R | R | R |
| **RegionModifier**（6 分量） | — | — | **P**（唯一写入者） | **O**（合成） | R | R（仅 P3 用途之外的只读） | R | R | R | R |
| **RegionType** | — | — | **O**（派生标签） | — | — | — | — | — | R（仅解释输出） | R（仅调试） |
| **Qualifications** | — | — | **O** | — | — | — | R（粗粒度约束） | R | R | R |
| **RegionView**（结果侧分类视角） | — | — | — | **O**（由 FinalState 判定） | R | — | R | R | R | R |

**矩阵自查**

| # | 自查项 | 期望 |
|---|---|---|
| **RA140** | 下游列（Biome/Geology/Ecology/Resource/WorldQuery/Runtime）是否出现 `P`/`O`？ | **必须为 0** |
| **RA141** | `Runtime` 列是否出现非 `R`？ | **必须为 0** |
| **RA142** | `WorldQuery` 列是否出现非 `R`？ | **必须为 0** |
| **RA143** | `RegionModifier` 行的写入者是否唯一（只有 Region 列）？ | **必须是** |
| **RA144** | `RegionType` 行是否存在除 Region 之外的任何读写？ | 只允许 Query/Runtime 的**解释用只读**；其余为 0 |

---

## §12 验收规则与登记项

### 12.1 采纳链（本文档的核心验收）

| # | 不变量 | 验证方法 |
|---|---|---|
| **RAV1** | **无源不成区**：每个 Region 至少可回溯到一个候选（`RG-A`） | 结构断言：`Region → OriginRef ≠ ∅` |
| **RAV2** | **双向可追溯**：`候选 → Region` 与 `Region → 候选` 集合一致（无丢失、无凭空） | 双射检查（考虑多对一合并） |
| **RAV3** | **采纳确定性**：同 `(Seed, DomainId, Position)` ⇒ 逐字段相同的 Region 集合 | 重复求值 + 双世界对照 |
| **RAV4** | **采纳顺序无关**：打乱候选枚举顺序 ⇒ 逐字段相同结果 | 顺序打乱对照（`RA59`） |
| **RAV5** | **去重键唯一**：同一 `RegionKey` 不出现两次 | 键唯一性检查（`RA37`） |
| **RAV6** | **上限确定性**：截断结果与全序一致，且与顺序无关 | 全序复算 + 顺序打乱对照（`RA47`/`RA48`） |
| **RAV7** | **合并规则合规**：`Strength` 有界求和、`Radius` 取 max、`Origins` 无损、`Tags` 并集；**无覆盖语义** | 合并规则结构扫描（禁止覆盖/平均/衰减合并） |

### 12.2 合成、边界与体素

| # | 不变量 | 验证方法 |
|---|---|---|
| **RAV8** | **叠加交换律**：打乱 Region 顺序 ⇒ 逐分量相同的 Modifier（`RO1`） | 顺序打乱对照 |
| **RAV9** | **贡献形状合规**：紧支撑 + 单调不增 + 边界连续（`RBD1`/`RBD3`） | 沿半径采样 + 相邻差值上界断言 |
| **RAV10** | **分量封闭**：恰好 6 个分量；不含 `Elevation`/`Ocean`；无第 7 分量（`RA67`–`RA69`） | 结构扫描（分量集合比对） |
| **RAV11** | **`RegionType` 无规则权**：类型标签不出现在任何判定路径（`RA85`–`RA89`） | 结构扫描（类型标识只出现在解释/输出路径） |
| **RAV12** | **开关 Region 层不改变体素**：体素快照逐体素一致（差异 = 0） | 两态体素快照比较（`RA104`） |
| **RAV13** | **唯一间接路径合规**：对体素的影响只经 `Region → Modifier → FinalState → Biome → P3`；差异集合 ⊆ 地表层材料 | 差异集合检查 + 结构扫描（`RA101`/`RA102`） |
| **RAV14** | **Chunk 无关**：不存在按 Chunk 的 Region 表/归属；跨 Chunk 边界影响连续 | 结构扫描 + 跨边界差分（`RA93`/`RA98`） |

### 12.3 确定性与局部求值

| # | 不变量 | 验证方法 |
|---|---|---|
| **RAV15** | **解释完整**：每个 Modifier 分量的每个贡献都能回溯到 `Region → Candidate`（`RA106`/`RA108`） | 查询解释链检查（逐分量可分解） |
| **RAV16** | **域独立**：改变一个 Region 域的参数不影响其它域的输出（`RA118`） | 分域对照 |
| **RAV17** | **反例保护**：不同 `Seed`/参数 ⇒ 至少一处 Region 或 Modifier 不同（`RA125`） | 双世界差异断言 |
| **RAV18** | **局部求值**：单点查询不产生全量 Region 集合/全量 Biome；代价与局部数量相关（`RA127`–`RA130`） | 结构性检查 + 求值范围检查 |
| **RAV19** | **查询只读**：查询前后 Region/Modifier 逐字段不变；缓存清空后结果不变（`RA110`/`RA111`/`RA138`） | 前后快照比较 + 清缓存对照 |
| **RAV20** | **零修改**：既有实现与既有规范 SHA 不变；不新增 `VoxelType`（`RA3`） | 文件哈希比对 + 结构性检查 |

### 12.4 与既有规范的措辞对齐（**登记项，本文档不修改任何文件**）

| # | 需对齐内容 | 位置 | 真相（按本契约） |
|---|---|---|---|
| ① | `Region_Design.md` §3 只定义来源类别与属性，未定义采纳流程 | `Region_Design.md` §3.1/§3.2 | **采纳流程归本文档 §4**；该规范的 `RS1`/`RG-A`/`RO1` 语义不变 |
| ② | `Region.EcologyRules` 只有属性定义，无产生规则（审查 **P1-2**） | `Region_Design.md`:328；`Ecology_Design.md`:106/366 | 本文档 §5.5 固定其**形态与输入约束**；**具体类别集合与映射表仍属实现规范**（**部分关闭**） |
| ③ | 审查 P0-3 的"采纳规则未定义" | `WorldGeneration_Architecture_Audit.md`:201/256 | **已由本文档 §4（RA31–RA61）关闭** |
| ④ | 审查 P1-2 的"区域级资格声明无规则" | 同上 §3 表 | **部分关闭**（形状已定，映射待实现规范） |
| ⑤ | `WorldQuery_Design.md` §3.1 Region 行权限与本契约一致 | `WorldQuery_Design.md`:174/175 | **无冲突**，无需对齐 |

---

## §13 V1 范围与禁令

### 13.1 V1 只定义

| # | 只定义 |
|---|---|
| 1 | Region 身份契约（§2：`RegionKey`、组成、形态） |
| 2 | 候选契约（§3：字段与约束） |
| 3 | **采纳流程**（§4：准入 / 规范化 / 全序 / 上限 / 物化 / 发布 + 顺序无关性） |
| 4 | Modifier 合成契约与**写入权限**（§5） |
| 5 | `RegionType` 边界（§6） |
| 6 | Chunk / Voxel 边界（§7） |
| 7 | WorldQuery 关系（§8） |
| 8 | 确定性与局部求值契约（§9/§10） |
| 9 | 消费者权限矩阵（§11）与验收（§12） |

### 13.2 V1 不实现

| # | 不实现 |
|---|---|
| 1 | 任何代码（不写 `.cs`、不建 class/interface/struct） |
| 2 | Region 的计算实现、空间索引实现、缓存实现 |
| 3 | 衰减形状、阈值、`StrengthCap`、`QuantumVoxels`、上限值等**具体数值**（属实现规范；本文档只要求"必须显式"） |
| 4 | 查询接口实现（属 `WorldQuery_Design.md`） |
| 5 | Runtime 同步、存档格式、UI/工具 |
| 6 | 动态 Region（增长/迁移/演化）——V1 静态（`Region_Design.md` §8） |

### 13.3 硬性禁令

| # | 禁令 |
|---|---|
| **RA145** | 不创建任何代码结构（class/interface/struct） |
| **RA146** | 不修改任何已有规范文件与任何 `Assets` 文件 |
| **RA147** | 不新增 `VoxelType`、不改 VoxelCore、不改地质实现 |
| **RA148** | 不新增因果边、不改变因果方向（`RA2`） |
| **RA149** | 不允许任何下游系统获得 Region 侧产物的写权限（§11；`RA140`） |
| **RA150** | 不允许按 Chunk 组织 Region（`RA93`） |
| **RA151** | 不允许使用未登记的域 ID、不允许复用保留域（`RA117`） |
| **RA152** | 不允许 Region 影响 `Elevation`/`Ocean`/`Density`/`CaveDensity`/`DepositZone`（`RA69`/`RA103`） |

---

## 附录 A：数据流图（含本文档补全段落）

```
 ┌──────────────────────────────────────────────────────────────┐
 │ History（Phase 2）             自然规则（Phase 3 内部）        │
 │  事件 → Region Source 候选      气候/海洋/山脉/河流/土壤        │
 └───────────────┬───────────────────────────┬──────────────────┘
                 └───────────┬───────────────┘
                             ▼
       ① 准入 Admissibility（RA31–RA36）
                             ▼
       ② 规范化 Canonicalization（RA37–RA43）
          去重键 = (DomainId, Epoch, Ordinal, CanonicalPosition)
          Strength 有界求和 · Radius 取 max · Origins 无损 · Tags 并集
                             ▼
       ③ 确定性全序 Ordering（RA44–RA46）      ← 仅用于截断/枚举/解释
                             ▼
       ④ 上限 Capping（RA47–RA49）
                             ▼
       ⑤ 物化 Materialization（RA50–RA53）
          Region = RegionKey + Source + Extent + Profile + Tags + Qualifications
                             ▼
       ⑥ 只读发布 Publication（RA54–RA58）
                             ▼
            RegionModifier = Σ Contribution（6 分量，加法 + 钳制）
                             ▼
            FinalState = Base + Modifier（合成权：WorldState）
                             ▼
     Biome（唯一规则表）→ Geology（仅 P3 地表材料）→ Voxel（唯一体素路径）
                             │
        Consumer（只读）：Biome · Geology(P3) · Ecology · Resource · WorldQuery · Runtime

  ═══ 禁止：下游 → Region/Modifier（RA140）· Region → Chunk/Voxel 写入（RA99）· 时间覆盖（RA124）═══
```

## 附录 B：身份与域对照表

| 域 ID | 名称 | 允许的来源类别 | 允许影响的分量 |
|---|---|---|---|
| `8` | `Region.Natural` | 气候 / 海洋 / 山脉 / 河流 / 土壤 | `Temperature` · `Moisture` · `Fertility` |
| `9` | `Region.Magic` | 6 类魔法来源（`Region_Design.md` §3.2） | `MagicDensity` |
| `10` | `Region.Holy` | 圣物 / 圣地 / 圣生物聚集 | `HolyInfluence` |
| `11` | `Region.Corruption` | 墓地 / 邪恶遗迹 / 污染区域 | `CorruptionInfluence` |
| `12` | `Region.Relic` | 文明活动 + 历史事件（遗迹候选区域） | **无**（只产出候选区域与资格，不改参数） |
| `R-1` / `R-2` | 保留域（既有实现） | **禁止使用**（`HeightFunction` / `GeologyNoise` 通道） | — |

| 身份 | 组成 | 是否可作为规则输入 |
|---|---|---|
| `RegionKey` | `(DomainId, Epoch, Ordinal, CanonicalX, CanonicalY, CanonicalZ)` | ✅ 作为**去重键** |
| 派生标识（哈希/可读 ID） | 由 `RegionKey` 派生 | ❌（仅标识/调试/解释） |
| `RegionType` | 解释标签 | ❌（无规则权，`RA85`） |
| `Tags` | 只读标签集合 | ⚠️ 仅资格/归因用途（`RH2`/`PW5`） |
| `Qualifications` | 区域级资格声明 | ⚠️ 仅作为下游**粗粒度约束**（`RA81`/`RA82`） |

## 附录 C：禁止清单

| # | 禁止项 | 反例（必须判为缺陷） | 依据 |
|---|---|---|---|
| **RAX-1** | 下游写 `RegionModifier` | Biome 提高 `MagicDensity` 修正 | `RA75`/`PW2` |
| **RAX-2** | 下游创建/修改 Region | 由生态候选"补出"一个 Region | `RA20`/`RA140` |
| **RAX-3** | 结果反推修正 | 由 `RegionType == MagicForest` 反推魔力修正 | `RA79`/`RA86` |
| **RAX-4** | `RegionType` 参与判定 | 用类型名决定 Biome/资源/生态 | `RA85`–`RA89` |
| **RAX-5** | 按 Chunk 组织 Region | 每 Chunk 一张 Region 归属表 | `RA93`/`RBD5` |
| **RAX-6** | 按 Chunk 尺寸对齐边界 | 边界恰好落在 16 的倍数上 | `RA94` |
| **RAX-7** | Region 写体素 | 用 Region 直接放置方块/清洞 | `RA99`/`RI4` |
| **RAX-8** | Region 改地形量 | 修改 `Elevation`/`Ocean`/`Density`/`CaveDensity` | `RA69`/`RA103` |
| **RAX-9** | 新增第 7 个分量 | 新增"腐蚀度"分量参与合成 | `RA67`/`RA68` |
| **RAX-10** | 覆盖语义 | "后出现的 Region 覆盖先出现的" | `RA42`/`HE5` |
| **RAX-11** | 用派生哈希排序 | 以 RegionId 哈希序做截断 | `RA15`/`RA44` |
| **RAX-12** | 世界级前置 | 为回答一个点而枚举全世界 Region | `RA131`/`RA132` |
| **RAX-13** | 查询写 Region | 查询时"顺手固化"Region 到缓存并作为权威 | `RA111`/`RA138` |
| **RAX-14** | 复用保留域 | 新 Region 层调用 `HeightFunction`/`GeologyNoise` 通道 | `RA117`/`PZ12` |
| **RAX-15** | 时间覆盖 / 演化 | "100 年后的 Region 覆盖 1000 年前的" | `RA124` |
| **RAX-16** | 自举 Region | 由 Region 产生新候选 | `RA19` |

## 附录 D：变更规则

1. 新增/删除/改名 Region 侧产物字段、变更采纳步骤或合并规则、变更写权限、变更域 ID 分配，**必须**升版本并同步更新 §2/§4/§5/§11 与附录 B。
2. **不得放宽**：`RA2`（不新增因果边）、`RA3`（不新增 VoxelType）、`RA69`（不含 `Elevation`/`Ocean`）、`RA75`（Modifier 唯一写入者）、`RA140`–`RA144`（矩阵自查）。
3. 与既有规范冲突时，先按 §12.4 **登记**，再由对应规范的**版本变更**解决；本文档**不得**直接改写其它规范。
4. 采纳流程的任何变更都必须重新通过 `RAV1`–`RAV20`（尤其 `RAV4` 顺序无关性与 `RAV8` 交换律）。
5. 任何"动态 Region"（增长/迁移/演化）提案必须**独立成规范**，并证明：确定性不受影响、既有验收全部 PASS、且**可关闭**（关闭后与静态 V1 逐字段一致）。
