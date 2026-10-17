# WorldState Runtime Contract V1

> **文档性质**：**求值契约规范（Evaluation Contract）**。本文档只定义 **WorldState 层的最终求值契约**：
> `Base` 的来源边界、`Modifier` 合成、History / Region / Geology 的参数贡献关系、Generation State 与 Runtime State 的分离、Query 的真值来源、玩家修改隔离。
> **仅创建 Markdown**；**不写代码**；**不创建 class / interface / struct**；**不修改 `Assets`**；**不修改任何已有规范**；**不新增参数**；**不新增 `VoxelType`**。
>
> **必须兼容的上游规范**：
>
> | 规范 | 与本契约的关系 |
> |---|---|
> | `WorldState_Design.md` | **参数语义权威**：§1.3 表（8 参数、`[0,1]` 归一化、规则 1–5）、§2.1（`Base + RegionModifier`）、§3.4–§3.5（静态场与贡献形状）、§3.6（T1–T4 求值时机）、§4.4（`Balance` 派生）、§4.5（HP2/HP3）、§5.3/§5.8（群系与退化兼容）、§7.4（WC1–WC6 骨架）、§8（VX1–VX8） |
> | `WorldParameter_Contract.md` | **参数契约**：§3.2 表 A（Owner/可修改层/可读取层）、§3.3 表 B（影响面）、§4 权限矩阵（WorldState = `O（合成）`、Region = `P（修正项）`）、§5.2 范围冻结、§6.4 域 ID（`Skeleton`=1、`Region.*`=8–12）、§7（`PX1`–`PX6` Generation/Runtime 分离）、§8.3（登记项）、PC1–PC18 |
> | `Region_Architecture_Contract.md` | **Region 侧契约**：§5.1 合成公式、§5.2 分量封闭表、§5.4 写入权限（`RA75`–`RA79`）、§7.2 Voxel 边界（`RA101`/`RA104`）、§11 消费者权限矩阵、`RAV8`/`RAV10`/`RAV12`/`RAV13` |
> | `WorldQuery_Design.md` | **查询权威**：§1.3（`WQ1`–`WQ6`：同一规则 / 只读 / 不反馈生成 / 无副作用 / 无状态）、§2.2 第 4 步（WorldState Evaluation）、§3.1（WorldState 行权限）、§4（`WD1`–`WD12`）、§5（`WL1`–`WL13`）、§6（`WE1`–`WE8` 解释）、§9（`WA17`/`WA18`/`WA20`） |
> | `WorldGeneration_Architecture_Audit.md` | **本契约要解决的审查结论**：**P0-4**（查询真值语义未区分规则求值与当前真值）、**AD-2**（Pristine/Current 是否都要）、**AD-3**（量纲迁移）、**M1**（WorldState 层为最小路径第一步） |
> | `WorldGeneration_Pipeline_Design.md` | Phase 4 的定位与输入封闭（§1.3 输入/输出表、`PA1`/`PA5`）、域分离（`PE1`–`PE7`）、附录 B 权限矩阵、附录 C 反向依赖、`PG1`–`PG14` |
> | `WorldGeneration_Geology*.md`、`VoxelWorld_Core.md`、`WorldGeneration_Core.md`、`ADR-001` | 地形真值唯一权威、体素状态集合冻结、真值契约与概念边界 |
>
> **冲突时的优先级**：**参数语义与规则**以 `WorldState_Design.md` 为准；**参数的身份/量纲/Owner/权限**以 `WorldParameter_Contract.md` 为准；
> **RegionModifier 的写入与叠加**以 `Region_Architecture_Contract.md` 为准；**查询语义**以 `WorldQuery_Design.md` 为准；
> **本契约负责**：`Base` 与 `Modifier` 的**求值边界与顺序**、**层间贡献关系**、**Generation/Runtime 分离**、**真值来源映射**、**玩家修改隔离**。
>
> **零修改声明**：本文档是**声明性文档**，不产生任何运行时效果；开启/关闭本文档**不得改变任何体素输出**（`WSV19`）。
> 与既有规范的措辞差异一律在**附录 D 登记**，**不修改**任何既有文件。

---

## §1 定位与边界

### 1.1 定义

> **WorldState Runtime Contract = 环境参数的最终求值契约。**
>
> 它回答：**8 个环境参数的最终值由谁、按什么顺序、从哪些输入算出来；哪些量不能参与；玩家与运行时能不能改它；查询读到的是哪一个真值。**

| 问题 | 由谁回答 |
|---|---|
| 8 个参数的**语义与定义域**？ | `WorldState_Design.md` §1.3 |
| 参数的**身份/Owner/量纲/读写权限**？ | `WorldParameter_Contract.md` |
| `RegionModifier` 怎么产生、谁能写？ | `Region_Architecture_Contract.md` §5 |
| **最终值怎么求（Base + Modifier 的边界与顺序）**？ | **本文档 §2/§3/§4** |
| **History / Region / Geology 各贡献什么**？ | **本文档 §5** |
| **玩家修改会不会影响参数**？ | **本文档 §6/§8** |
| **查询读到的是哪个真值**？ | **本文档 §7** |

### 1.2 契约边界（硬性）

| # | 规则 |
|---|---|
| **WSC1** | 本文档**不定义**参数语义（定义域、来源类别、群系规则表）；只定义**求值边界与顺序** |
| **WSC2** | 本文档**不新增参数**：参数集合固定为 `WorldParameter_Contract.md` 附录 B 的登记集合 |
| **WSC3** | 本文档**不新增 `VoxelType`**；不改变体素状态集合（Air/Grass/Dirt/Stone） |
| **WSC4** | 本文档**不新增因果边**；使用的边必须是既有链的子集（`来源 → Region → Modifier → Base+Modifier → Biome → Geology(P3)`） |
| **WSC5** | 本文档**不定义查询入口**（属 `WorldQuery_Design.md`），只定义"查询读到的是哪一份真值" |
| **WSC6** | 本文档**不定义 Region 的采纳规则**（属 `Region_Architecture_Contract.md` §4） |
| **WSC7** | 本文档**不接触** Chunk / Mesh / Entity / Runtime 的实现 |
| **WSC8** | 求值必须**确定性**（§9）且**无副作用**（`WorldState_Design.md` §3.6 `T4`） |

---

## §2 求值链与最终公式

### 2.1 最终公式（唯一权威路径）

```
FinalState(p) = clamp01( BaseWorldState(p) + RegionModifier(p) )       ← 连续参数逐分量
```

| 项 | 归属 | 依据 |
|---|---|---|
| `BaseWorldState(p)` | 由**骨架框架 + 配置 + 派生规则**确定（本文档 §3） | `WorldState_Design.md` §1.3/§7.4；`WorldParameter_Contract.md` §3.2（来源列） |
| `RegionModifier(p)` | **唯一写入者 = Region 层**（`Region_Architecture_Contract.md` §5.4） | `WorldParameter_Contract.md` §4 矩阵（Region 行 = `P（修正项）`） |
| **合成权** | **WorldState 层**（`O（合成）`） | 同矩阵（WorldState 行 = `O（合成）`）；`Region_Architecture_Contract.md` `RA76` |

### 2.2 逐参数的合成类型（冻结）

| 参数 | 合成类型 | 说明 |
|---|---|---|
| **Temperature** | `Base + Modifier` | Base = 气候带基线 + 配置；Modifier = 自然类 Region 修正 |
| **Moisture** | `Base + Modifier` | 同上 |
| **Fertility** | `Base + Modifier` | Base = 派生（配置级土壤深度 + **Base 级** 温度/湿度）；Modifier = 自然类修正（见 §3.4） |
| **MagicDensity** | `Base + Modifier` | Base = 全局基底常量（默认 `0`）；Modifier = 魔法来源修正 |
| **HolyInfluence** | `Base + Modifier` | Base = 全局基底常量（默认 `0`）；Modifier = 圣性来源修正 |
| **CorruptionInfluence** | `Base + Modifier` | Base = 全局基底常量（默认 `0`）；Modifier = 腐化来源修正 |
| **Elevation** | **无合成**（只读转发） | 由既有高度场产生；Region **不得修正**（`RE1`/`RA69`） |
| **Ocean** | **无合成**（派生） | `Elevation < SeaLevel`；Region **不得修正**（`RE1`/`RA69`） |
| **Balance** | **派生（非合成）** | `HolyInfluence − CorruptionInfluence`（`WorldState_Design.md` §4.4；`RA70`） |

| # | 规则 |
|---|---|
| **WSC9** | 合成**只允许加法 + 钳制**（`MF1`/`MF5`；`RA62`） |
| **WSC10** | `Elevation` / `Ocean` **不得**出现在任何修正求和式中（"缺省 = 不存在"，不是"修正为 0 后参与"） |
| **WSC11** | `Balance` **不得**被任何 Modifier 直接写入（`RA70`）；它只能由 `HolyInfluence`/`CorruptionInfluence` 派生 |
| **WSC12** | 连续参数合成后必须钳制到 `[0,1]`（`WorldParameter_Contract.md` §5.2；`RA64`） |
| **WSC13** | **禁止**对整数参数（`Elevation`）与布尔参数（`Ocean`）做插值/加权合成 |
| **WSC14** | **零修正的显式性**：无 Region 影响时 `RegionModifier(p) = 0`（逐分量），且必须与"Region 层不存在"逐值等价（`Region_Design.md` RM3） |

### 2.3 求值内部顺序（冻结；不新增 Pipeline Phase）

```
0. Elevation  ← 只读转发既有高度场（HeightFunction / SurfaceHeightAt）
1. Ocean      ← 派生：Elevation < SeaLevel
2. Base.Temperature / Base.Moisture   ← 骨架气候带 + 配置
3. Base.Fertility                     ← 配置级土壤深度 + Base 级 Temperature/Moisture
4. Base.MagicDensity / Base.HolyInfluence / Base.CorruptionInfluence  ← 全局基底常量
5. RegionModifier                     ← Region 层提供（6 分量，加法 + 钳制）
6. FinalState                         ← clamp01(Base + Modifier)（逐分量）
7. Balance                            ← 派生（Holy − Corruption）
```

| # | 规则 |
|---|---|
| **WSC15** | 上述顺序是 **Phase 4 内部**的求值顺序，**不新增**任何 Pipeline 阶段（`Pipeline` §2.1） |
| **WSC16** | 顺序必须**无环**：步骤 k 不得读取步骤 k+1…7 的结果（`WSC14`/`C2`） |
| **WSC17** | `Fertility` 的 Base **只允许**使用步骤 2 的 **Base 级** `Temperature`/`Moisture`（**不得**使用 `FinalState` 级），以保证无环与可分解解释（见附录 D ②） |
| **WSC18** | `Balance` 使用 **FinalState 级** 的两个力量参数（这是唯一的"跨参数派生"，且无环） |
| **WSC19** | 求值**不得**读取任何下游产物：Biome / 生态候选 / 资源判定 / `Density` / `Material` / `CaveDensity` / `DepositZone` / 体素值 / Chunk 状态（§5.4 禁令表） |
| **WSC20** | 求值**不得**产生副作用：不写体素、不创建 Chunk、不置脏、不生成对象（`T4`；`WSC8`） |

---

## §3 Base WorldState 契约

### 3.1 定义性要求（Base 的可分离性）

| # | 规则 |
|---|---|
| **WSC21** | `BaseWorldState(p)` **必须**能在**不计算任何 Region** 的前提下求值（这是 Base 的定义性要求） |
| **WSC22** | 因此必须成立：**关闭 Region 层 ⇒ `FinalState == Base`**（逐分量、逐值相等） |
| **WSC23** | `Base` 必须是 `(Seed, Domain, Position)` 的纯函数：同输入同输出、可重复求值（`RM1`；`PZ1`） |
| **WSC24** | `Base` 不得依赖加载顺序、帧序、时间、玩家状态、已生成内容（`PZ4`–`PZ8`） |

### 3.2 Base 的允许输入白名单（封闭）

| 参数 | 允许的 Base 输入 | 依据 |
|---|---|---|
| `Temperature` | `TemperatureBase`（配置，默认 `0.5`）+ 骨架基础气候带 + 显式高度衰减参数 | `WorldParameter_Contract.md` §3.2；`WorldState_Design.md` §7.4 |
| `Moisture` | `MoistureBase`（配置，默认 `0.5`）+ 骨架基础气候带 | 同上 |
| `Fertility` | **配置级** `SoilDepth`（`WorldGenerationSettings.SoilDepth`）+ **Base 级** `Temperature`/`Moisture` | 见 §3.4 与附录 D ① |
| `MagicDensity` | 全局基底常量（默认 `0`） | `WorldState_Design.md` §3.4 |
| `HolyInfluence` | 全局基底常量（默认 `0`） | `WorldState_Design.md` §4 |
| `CorruptionInfluence` | 全局基底常量（默认 `0`） | `WorldState_Design.md` §4 |
| `Elevation` | 既有高度场（`HeightFunction.GetTerrainHeight` / `GeologySampler.SurfaceHeightAt`）+ 骨架大陆掩码叠加 | `WorldState_Design.md` §1.3；`WC2` |
| `Ocean` | `Elevation` + `SeaLevel`（配置） | `WorldState_Design.md` §1.3；`WC3` |

| # | 规则 |
|---|---|
| **WSC25** | Base 的输入集合是**封闭的**：白名单之外的量一律不得读取（禁止隐式全局状态，`PA1`） |
| **WSC26** | **禁止** Base 读取 **Geology 的结论**：层分类（Surface/Depth/Deep）、`CaveDensity`、`DepositZone`、`Density`、`Material`——这些是 **Phase 6** 产物，读取它们构成**前向读取**（`Pipeline` §1.3 输入封闭） |
| **WSC27** | `Fertility` 的 Base 允许使用**配置级**土壤深度（`WorldGenerationSettings.SoilDepth`，默认 `3`），因为它是**显式配置参数**而非 Geology 结论（附录 D ①） |
| **WSC28** | Base 不得读取 `HistoryTags` / 候选 / Region 的任何字段（`RH3`：参数求值禁止读标签） |
| **WSC29** | Base 不得读取 Biome 结论（群系是**结果**，`RB3`/`RTY5`） |

### 3.3 Base 与 Modifier 的可分解性（解释要求）

| # | 规则 |
|---|---|
| **WSC30** | 对任一参数，实现必须能**分别给出** `Base` 值、各 Region 的贡献值、`Modifier` 合计值、`FinalState` 值（四者可分解，供 `WorldQuery_Design.md` §6 解释使用） |
| **WSC31** | 分解必须满足：`Σ 各贡献 == Modifier`，且 `clamp01(Base + Modifier) == FinalState`（数值可核对） |
| **WSC32** | 当某参数无任何贡献时，解释必须显式说明"该参数无 Region 影响"（`WE8` 的"无影响的层"） |

### 3.4 Fertility 的 Base 契约（冻结项）

```
Base.Fertility = f( 配置级 SoilDepth , Base.Temperature , Base.Moisture )      ← 规则由实现规范给出
```

| # | 规则 |
|---|---|
| **WSC33** | `f` 的具体形式（阈值/映射）属实现规范；本契约只冻结**输入集合**（`WSC27`/`WSC17`）与**无环性** |
| **WSC34** | `Base.Fertility` 必须落在 `[0,1]`；不得为负、不得溢出 |
| **WSC35** | `Base.Fertility` 不得读取 `Ocean` 之外的地理量（`Ocean` 可用于"海洋处肥力为 0"这类显式分支，但该分支必须显式声明） |
| **WSC36** | `Fertility` 的最终值仍遵守 §2.1 公式（Base + 自然类 Region 修正，钳制） |

---

## §4 RegionModifier 合成契约

### 4.1 分量与权限

| 项 | 内容 |
|---|---|
| 分量集合 | 恰好 6 个：`Temperature`、`Moisture`、`Fertility`、`MagicDensity`、`HolyInfluence`、`CorruptionInfluence`（`RA67`） |
| 唯一写入者 | **Region 层**（`RA75`；`WorldParameter_Contract.md` §4 `P（修正项）`） |
| 合成者 | **WorldState 层**（`O（合成）`；`RA76`） |
| 读取者 | Biome · Geology（仅 P3）· Ecology · Resource · WorldQuery · Runtime（全部只读，`RA140`） |

### 4.2 硬性规则

| # | 规则 |
|---|---|
| **WSC37** | 合成必须满足**交换律**：与 Region 遍历顺序无关（`RO1`；`RA63`；`RAV8`） |
| **WSC38** | 合成**不得**产生放大反馈（某 Region 不得因附近参数高而增强，`RO5`；`RA66`） |
| **WSC39** | 合成**不得**有覆盖语义（"后出现的覆盖先出现的"一律禁止，`HE5`；`RA42`） |
| **WSC40** | 合成**不得**引入第 7 个分量（`RA68`）；新增分量必须修改 `Region_Design.md` §4.2 并升版本 |
| **WSC41** | 合成**不得**写入 `Balance`（`WSC11`）与地形量（`WSC10`） |
| **WSC42** | 单点参与合成的 Region 数量必须由显式上限约束（`RO7`；`RA47`） |
| **WSC43** | WorldState **不得**发明修正项、不得修改 Region 的 `ModifierProfile`、不得"补偿"或"归一化"Region 的贡献（`RA76`） |
| **WSC44** | 合成**不得**读取下游：不得用 Biome/生态/资源/体素结果调整修正（`RA77`/`RA79`） |

---

## §5 History / Region / Geology 的参数贡献关系

### 5.1 贡献总表（逐参数）

| 参数 | Base 来源 | **History** 的贡献 | **Region** 的贡献 | **Geology** 的贡献 | **Runtime/玩家** 的贡献 |
|---|---|---|---|---|---|
| `Temperature` | 骨架气候带 + 配置 | 仅**经** Region Source 候选间接（无直连） | ✅ 修正项（自然类） | ❌ 禁止 | ❌ 禁止（`PX2`） |
| `Moisture` | 骨架气候带 + 配置 | 同上 | ✅ 修正项（自然类） | ❌ 禁止 | ❌ 禁止 |
| `Fertility` | 配置级土壤深度 + Base 级 T/M | 同上 | ✅ 修正项（自然类） | ❌ 禁止（只可用配置级 `SoilDepth`，`WSC27`） | ❌ 禁止 |
| `MagicDensity` | 全局基底常量 | 仅经 Region Source 候选（魔法来源） | ✅ 修正项（魔法来源） | ❌ 禁止 | ❌ 禁止 |
| `HolyInfluence` | 全局基底常量 | 仅经 Region Source 候选（圣性来源） | ✅ 修正项（圣性来源） | ❌ 禁止 | ❌ 禁止 |
| `CorruptionInfluence` | 全局基底常量 | 仅经 Region Source 候选（腐化来源） | ✅ 修正项（腐化来源） | ❌ 禁止 | ❌ 禁止 |
| `Elevation` | 既有高度场（+ 大陆掩码） | ❌ | ❌（`RE1`/`RA69`） | ⚠️ **只读转发通道**（高度场由 Skeleton/Geology 共用同一函数） | ❌ |
| `Ocean` | `Elevation < SeaLevel`（派生） | ❌ | ❌（`RE1`） | ❌（派生自 `Elevation`） | ❌ |
| `Balance`（派生） | — | ❌ | ❌（`RA70`） | ❌ | ❌ |

### 5.2 History 的贡献路径（唯一）

```
History 事实（事件/文明）
   ↓  产出候选（WorldHistory_Design §1.3 / PI1）
RegionSourceCandidate
   ↓  采纳（Region_Architecture_Contract §4）
Region
   ↓  合成（§5）
RegionModifier
   ↓  Base + Modifier（本文档 §2）
FinalState
```

| # | 规则 |
|---|---|
| **WSC45** | **History 不得直接写任何参数**（`HG3`；`HS7`）；History 对参数的**唯一影响路径**是上式 |
| **WSC46** | `HistoryTags` 只能用于**归因/资格**，**不得**参与参数求值（`RH3`；`RZ2`） |
| **WSC47** | 历史的时间序位**不得**引入覆盖/衰减语义（`HE5`；`RA124`）：多纪元贡献**同时成立并叠加** |

### 5.3 Geology 的关系（单向通道 + 只读消费）

| # | 规则 |
|---|---|
| **WSC48** | **`Elevation` 是唯一的"Geology 侧提供、WorldState 侧转发"的参数**：高度场由既有函数产生（`HeightFunction` / `GeologySampler.SurfaceHeightAt`），WorldState **只读转发**，**不重新定义** |
| **WSC49** | 复用既有高度场属于**允许的读取**；它**不构成**"复用保留域作为新层随机源"的违规（`PZ12` 禁止的是后者，见附录 D ⑤） |
| **WSC50** | Geology **不得写任何参数**（`Density`/`Material`/`CaveDensity`/`DepositZone` 属 Geology 自己的真值层，不是 WorldState 参数） |
| **WSC51** | 反向通道（WorldState → Geology）**只允许**经 Biome 的 **P3 地表材料**（`RA101`；`WorldGeneration_Geology_Runtime_Implementation.md` §6.1） |
| **WSC52** | 该反向通道**不得**影响 Geology 的 P0/P1/P2（世界边界 / 洞穴 / 地形密度） |

### 5.4 贡献禁令（层 × 参数）

| # | 禁止 |
|---|---|
| **WSC53** | Biome → 任何参数（禁止群系回写，`PT9`/`RA86`） |
| **WSC54** | Geology → 任何参数（除 `Elevation` 的只读转发通道） |
| **WSC55** | Ecology → 任何参数（`PT10`；`Ecology_Design.md` `EA1`） |
| **WSC56** | Resource → 任何参数（`PT11`；`RX-3`） |
| **WSC57** | WorldQuery → 任何参数（`WP5`；`RA110`） |
| **WSC58** | Runtime / 玩家 → 任何参数（`PX2`；`RA78`；本文档 §6/§8） |
| **WSC59** | 对象 / Entity / 结构 / 生态候选 → 任何参数（ADR-001；`PT12`） |

---

## §6 Generation State 与 Runtime State 的分离

### 6.1 定义（沿用参数契约 §7）

| 类别 | 定义 | 生命周期 | 归属 |
|---|---|---|---|
| **Generation State** | 由世界规则在求值时确定的参数值与派生结论（"世界**本来**是什么样"） | 求值期存在；可由 `(Seed, Domain, Position)` **重算** | 本文档 + 各层规范 |
| **Runtime State** | 由运行时行为产生的状态（"世界**现在**是什么样"）：体素编辑结果、玩家背包/资源、加载与脏标记等 | 运行期存在；可能存档 | `VoxelWorld_Runtime*.md` / `Mining` 规范 |

### 6.2 硬性规则

| # | 规则 |
|---|---|
| **WSC60** | 8 个参数**全部属于 Generation State**：`MagicDensity` **不是**"当前玩家释放魔法后的实时数值"（`PX1`） |
| **WSC61** | Runtime 的改变**必须**进入 Runtime State（体素编辑 → `VoxelWorld.SetVoxel`；玩家资源 → 玩家侧状态），**不得**覆盖任何参数（`PX2`） |
| **WSC62** | **禁止**"因为运行时事件而提高 `MagicDensity`/`CorruptionInfluence`/`Fertility`"（`PX3`）；若未来需要，必须作为**新的世界来源类型**在上游规范定义 |
| **WSC63** | Runtime **只读**参数（`PX4`；`WorldParameter_Contract.md` §4 矩阵 Runtime 列全 `R`） |
| **WSC64** | 参数求值**不得**触发 Runtime 行为：不创建 Chunk、不置脏、不生成 Mesh、不生成对象（`PX6`；`T4`） |
| **WSC65** | 参数**不需要存档**：任何参数都可由 `(Seed, Domain, Position)` 重算（`WorldParameter_Contract.md` §9.2 第 6 项） |
| **WSC66** | **禁止**存在"参数的历史快照/版本链"（例如"上一次求值时的 `MagicDensity`"）——参数无时间维度（`HE3`；`T1`） |

### 6.3 两类量的交叉禁令

| 动作 | 是否允许 | 依据 |
|---|---|---|
| 参数 → 影响体素 | ✅ 唯一路径：参数 → Biome → **P3 地表材料**（且只在**生成时**生效） | `RA101`；`WorldGeneration_Core.md` §9 |
| 参数 → 影响已生成区域 | ❌（生成不重跑；已生成区域不被重新生成） | `WorldGeneration_Core.md` §9 |
| 体素真值 → 影响参数 | ❌ **禁止**（否则引入加载顺序依赖） | `WSC19`/`WSC23` |
| 玩家操作 → 影响参数 | ❌ **禁止** | §8 |
| 参数 → 影响玩家状态 | ❌（参数不产生副作用） | `WSC20`/`WSC64` |

---

## §7 Query 读取的真值来源

### 7.1 真值来源映射（冻结）

| 查询内容 | 真值来源（唯一） | 是否存在"编辑后版本" |
|---|---|---|
| **8 个环境参数**（`Temperature`/`Moisture`/`Elevation`/`Fertility`/`MagicDensity`/`HolyInfluence`/`CorruptionInfluence`/`Ocean`） | **WorldState 求值**（`Base + Modifier`，本文档 §2） | ❌ **不存在**（玩家不能改参数，`WSC58`） |
| **`Balance`** | 由 `FinalState` 派生（`Holy − Corruption`） | ❌ |
| **Region / RegionModifier** | Region 层的只读发布（`Region_Architecture_Contract.md` §4.6/§5） | ❌ |
| **Biome** | 群系规则表（`FinalState → Biome`，`WorldState_Design.md` §5.3） | ❌ |
| **地形规则真值**（`Density`/`Material`/`CaveDensity`/`DepositZone`） | Geology 判定链（世界坐标纯函数） | ❌（规则层无"编辑"概念） |
| **体素真值**（"该坐标当前是什么体素"） | `VoxelWorld`（生成结果 **⊕** 玩家编辑） | ✅ **存在**：`Pristine`（仅规则）与 `Current`（真值含编辑）之分 |
| **生态候选 / 资源判定** | 各自层规范（只读判定） | ❌ |

### 7.2 硬性规则

| # | 规则 |
|---|---|
| **WSC67** | 查询参数时**必须**使用本契约的求值路径（同一规则，`WQ1`/`WQ2`；`WA20`） |
| **WSC68** | **参数只有一份真值**：不存在"玩家编辑后的参数版本"（参数不可被运行时修改，§6） |
| **WSC69** | 因此 `Pristine` / `Current` 的二分**只适用于体素真值**，**不适用于参数**（对审查 **P0-4** 的参数侧收窄；体素侧仍归 `AD-2` 决策，见附录 D ③） |
| **WSC70** | 查询**不得**用体素真值反推参数（例如"这里被挖空了 ⇒ 魔力应该更低"一律禁止，`WSC19`） |
| **WSC71** | 查询**不得**修改参数、Region、Modifier 或任何 Generation State（`WQ3`/`WQ5`；`RA110`） |
| **WSC72** | 查询结果**不得**成为任何生成阶段的输入（`WQ4`） |
| **WSC73** | 参数查询必须在**未生成 Chunk** 处同样可回答（参数是规则求值，与是否生成无关，`WSC21`/`WSC23`） |
| **WSC74** | 参数查询的解释必须能区分 **Base 与 Modifier**（`WSC30`/`WSC31`），并区分"无影响"与"修正为 0" |
| **WSC75** | 缓存（若实现）必须透明：不得改变输出、不得成为权威、不得跨 `Seed`/参数复用（`WL9`/`WL10`） |

---

## §8 玩家修改隔离

### 8.1 定义

> **玩家修改（挖掘/放置/编辑）= Runtime State 的变更，只作用于体素真值，绝不影响任何 Generation 参数。**

### 8.2 硬性规则

| # | 规则 |
|---|---|
| **WSC76** | 玩家修改的**唯一写入对象**是体素真值（`VoxelWorld.SetVoxel` / 既有 Mining 规范）；**不得**写参数、不得写 Region、不得写 Modifier |
| **WSC77** | 参数求值**不得读取**体素真值、已生成内容、Chunk 状态、玩家状态（`WSC19`） |
| **WSC78** | 因此必须恒成立：**玩家修改前后，同一 `(Seed, Position)` 的 8 个参数逐字段不变**（可机械验证，`WSV14`） |
| **WSC79** | 生成**不覆盖**玩家修改：生成只产出"初始世界状态"，运行时改动另行存放（`WorldGeneration_Core.md` §9；`IWorldGenerator.cs` 契约注释） |
| **WSC80** | 参数变化**不追溯**：参数只在**生成时刻**经 Biome 影响地表材料；已生成区域不因参数"重算"而改变（`WorldGeneration_Core.md` §9；`WSC79`/`WSC80`） |
| **WSC81** | 未生成区域的参数必须**仍可求值**（规则求值，与是否生成无关），但**不得**因此创建 Chunk（`WSC20`；`VoxelWorld.cs:124` 语义：读取不创建 Chunk） |
| **WSC82** | **禁止**"参数快照写回体素"：不得把参数值编码进体素数据（例如"用体素标记魔力浓度"）——那需要新增 `VoxelType`，V1 禁止（`WSC3`） |

### 8.3 隔离的可验证形式

| # | 验证 |
|---|---|
| **WSV14 验证 A** | 取一组坐标 → 记录 8 参数快照 → 执行玩家编辑（体素真值变化）→ 重新求值 → 逐字段比较（差异必须为 0） |
| **WSV14 验证 B** | 未生成区域：对未加载 Chunk 的坐标求参数 → 结果与"之后生成该 Chunk"时的参数逐字段一致 |

---

## §9 确定性与局部求值

### 9.1 确定性

| # | 规则 |
|---|---|
| **WSC83** | 同一 `(Seed, Domain, Position)` ⇒ **完全一致的** `Base`、`Modifier`、`FinalState`（逐分量） |
| **WSC84** | 必须提供**反例保护**：不同 `Seed` 或不同显式参数 ⇒ 至少一处参数值不同（`WD3`） |
| **WSC85** | 域 ID 使用 `WorldParameter_Contract.md` §6.4：`Skeleton` = `1`（基础气候带/大陆）；`Region.*` = `8`–`12`（修正来源） |
| **WSC86** | **禁止**复用保留域 `R-1`/`R-2` 作为新层的随机源（`PZ12`；`WSC49` 的例外仅限 `Elevation` 的只读转发） |
| **WSC87** | 禁止：`Random()` / `Time` / `DateTime` / `Guid` / 玩家状态 / 加载顺序 / 帧序 / tick / 遍历顺序 / 查询顺序（`PZ5`–`PZ8`；`WD10`） |
| **WSC88** | 禁止：由下游结果反推参数（`PZ9`；`WD11`） |

### 9.2 局部求值

| # | 规则 |
|---|---|
| **WSC89** | 单点参数求值必须能**独立完成**，不依赖任何世界级前置（`WL1`；`WSC21`） |
| **WSC90** | 求值成本必须与"该点相关的 Region 数量"相关，而不是与"世界 Region 总量"相关（`WL11`） |
| **WSC91** | 禁止：生成整个世界 / 保存所有 Region / 保存所有 Biome / 全局注册表前置（`WL3`–`WL6`） |
| **WSC92** | 禁止：依赖邻域是否已求值、是否已加载（`WL8`） |
| **WSC93** | 禁止：跨帧存活状态（`WL13`；`WSC66`） |

---

## §10 验收规则

### 10.1 求值正确性

| # | 不变量 | 验证方法 |
|---|---|---|
| **WSV1** | **Base 可分离**：不计算任何 Region 即可得到 `Base`（`WSC21`） | 关闭 Region 层求值 ⇒ 输出 = Base |
| **WSV2** | **唯一路径**：`FinalState == clamp01(Base + Modifier)`（逐分量，数值核对） | 分解核对（`WSC31`） |
| **WSV3** | **零修正等价**：无 Region ⇒ `Modifier == 0` 且 `FinalState == Base`（逐字段） | 两态对照 |
| **WSV4** | **范围合规**：连续参数 ∈ `[0,1]`；`Balance` ∈ `[-1,1]`；`Elevation` 整数；`Ocean` 布尔 | 值域检查 |
| **WSV5** | **无环**：`Fertility` 只依赖 Base 级 T/M；`Balance` 依赖 FinalState 级 Holy/Corruption（`WSC17`/`WSC18`） | 依赖顺序检查（步骤图静态扫描） |
| **WSV6** | **交换律**：打乱 Region 顺序 ⇒ 逐分量相同的 `Modifier`（`WSC37`） | 顺序打乱对照 |
| **WSV7** | **无放大反馈 / 无覆盖**：不存在"因邻居而增强"或"后来的覆盖先前的"（`WSC38`/`WSC39`） | 结构扫描 + 单/多 Region 对照 |

### 10.2 边界与贡献

| # | 不变量 | 验证方法 |
|---|---|---|
| **WSV8** | **修正集封闭**：恰好 6 个分量；`Elevation`/`Ocean`/`Balance` 不出现在任何求和式（`WSC10`/`WSC11`） | 结构扫描（分量集合比对） |
| **WSV9** | **Base 输入封闭**：Base 求值路径不出现 Geology 结论 / `HistoryTags` / Biome / 下游产物（`WSC25`–`WSC29`） | 结构扫描（输入符号白名单比对） |
| **WSV10** | **History 无直连**：不存在"事件 → 参数"的路径；历史只能经候选 → Region（`WSC45`） | 结构扫描 + 判定链检查 |
| **WSV11** | **Geology 只提供 Elevation 通道**：Geology 不写任何参数；WorldState → Geology 只经 P3（`WSC48`–`WSC52`） | 依赖矩阵校验 |
| **WSV12** | **下游不回写**：Biome/Geology/Ecology/Resource/Query/Runtime 对参数的写路径数 = 0（`WSC53`–`WSC59`） | 权限矩阵校验 + 结构扫描 |

### 10.3 真值来源与玩家隔离

| # | 不变量 | 验证方法 |
|---|---|---|
| **WSV13** | **参数无 Current 版本**：不存在"编辑后参数"的读取路径；参数读取只经 §2 路径（`WSC68`/`WSC69`） | 结构扫描（真值来源唯一） |
| **WSV14** | **玩家修改隔离**：玩家编辑前后，参数逐字段不变（`WSC78`） | 前后快照逐字段比较（WSV14 验证 A）+ 未生成区域对照（WSV14 验证 B） |
| **WSV15** | **生成不追溯**：已生成区域不因参数变化而改变；体素只在生成时受参数影响（`WSC79`/`WSC80`） | 生成后修改参数并重查 ⇒ 体素快照不变 |
| **WSV16** | **未生成区域可求值**：未加载 Chunk 处参数可答，且不创建 Chunk（`WSC81`） | 求值前后 `ChunkCount` 不变 + 结果一致性检查 |
| **WSV17** | **查询一致性**：查询结论与本文档求值结果逐项一致（`WSC67`；`WA17`） | 交叉比对（查询 vs 直接求值） |
| **WSV18** | **可解释**：每个参数可分解为 Base + 各贡献，且能说明"无影响"（`WSC30`–`WSC32`/`WSC74`） | 解释链检查（逐分量可分解） |

### 10.4 零修改与退化

| # | 不变量 | 验证方法 |
|---|---|---|
| **WSV19** | **声明性**：开启/关闭本文档 ⇒ 体素输出逐体素一致（差异 = 0） | 两态体素快照比较 |
| **WSV20** | **零修改 / 无新增**：既有实现与既有规范 SHA 不变；不新增参数；不新增 `VoxelType`（`WSC2`/`WSC3`） | 文件哈希比对 + 参数集合比对 |
| **WSV21** | **退化兼容**：不存在 Region / 关闭新层时，世界与既有基线逐体素一致（`WSC22`；`WS11`；`RA104`） | 与既有基线对照（体素快照 + 内容哈希） |

---

## §11 V1 范围与禁令

### 11.1 V1 只定义

| # | 只定义 |
|---|---|
| 1 | 最终公式与逐参数合成类型（§2.2） |
| 2 | 求值内部顺序与无环性（§2.3） |
| 3 | `Base` 的输入白名单与可分离性（§3） |
| 4 | `RegionModifier` 的合成契约（§4） |
| 5 | History / Region / Geology 的参数贡献关系（§5） |
| 6 | Generation State 与 Runtime State 的分离（§6） |
| 7 | Query 的真值来源映射（§7） |
| 8 | 玩家修改隔离（§8）与确定性与局部求值（§9） |

### 11.2 V1 不实现

| # | 不实现 |
|---|---|
| 1 | 任何代码（不写 `.cs`、不建 class/interface/struct） |
| 2 | 参数的**具体计算规则**（`f` 的形式、阈值、衰减形状）——属实现规范 |
| 3 | 缓存、索引、查询接口 |
| 4 | Runtime 同步、存档格式、UI/工具 |
| 5 | 体素真值的 `Pristine`/`Current` 二分实现（属 `VoxelWorld`/`Mining`/Query 的后续决策，`AD-2`） |

### 11.3 硬性禁令

| # | 禁令 |
|---|---|
| **WSC94** | 不创建任何代码结构（class/interface/struct） |
| **WSC95** | 不修改任何已有规范文件与任何 `Assets` 文件 |
| **WSC96** | 不新增参数、不新增 `VoxelType`、不改 VoxelCore 与地质实现 |
| **WSC97** | 不新增因果边、不改变因果方向（`WSC4`） |
| **WSC98** | 不允许任何下游系统获得参数的写权限（§5.4） |
| **WSC99** | 不允许 Runtime / 玩家修改参数（§6/§8） |
| **WSC100** | 不允许参数读取体素真值、Chunk 状态、已生成内容（§8.2） |
| **WSC101** | 不允许 `Elevation` / `Ocean` / `Balance` 参与修正求和（`WSC10`/`WSC11`） |
| **WSC102** | 不允许参数求值产生任何副作用（`WSC20`） |
| **WSC103** | 不允许存在第二套参数求值路径（`WSC67`；`WA20`） |

---

## 附录 A：求值数据流图

```
Seed（配置）
  │
  ├─ Skeleton（Phase 1）────────────────────────► Elevation（只读转发）· Ocean（派生）· 基础气候带
  │                                                     │（只读）
  ├─ History（Phase 2）──► Region Source 候选 ──┐        │
  │                                             │        │
  ├─ 自然规则 ─────────────────────────────────┘        │
  │                                             ▼        │
  │                                   Region（采纳：Region_Architecture_Contract §4）
  │                                             │        │
  │                                             ▼        │
  │                            RegionModifier（6 分量，Region 唯一写入）│
  │                                             │        │
  └─────────────────────────────────────────────┴────────┴──────┐
                                                                 ▼
        ┌──────────────────────────────────────────────────────────────────┐
        │ WorldState Evaluation（Phase 4；合成权 = WorldState = O（合成））  │
        │   0 Elevation（转发） → 1 Ocean（派生）                           │
        │   2 Base.T/M → 3 Base.Fertility（Base 级 T/M）                    │
        │   4 Base.Magic/Holy/Corruption（全局基底）                        │
        │   5 Modifier（加法） → 6 FinalState = clamp01(Base + Modifier)     │
        │   7 Balance = Holy − Corruption（派生）                            │
        └──────────────────────────────┬───────────────────────────────────┘
                                       │ FinalState（8 参数，只读）
        ┌──────────────────────────────┼───────────────────────────────────┐
        ▼                              ▼                                  ▼
   Biome（唯一规则表）          Ecology / Resource（只读）          WorldQuery（只读解释）
        │
        ▼
   Geology P3（仅地表材料）→ Voxel（唯一体素路径）

   Runtime State（玩家编辑/背包/加载状态）──×──► 参数（禁止回写，WSC58/WSC76）
   Voxel 真值（生成结果 ⊕ 玩家编辑）────×──► 参数（禁止读取，WSC19/WSC77）
```

## 附录 B：8 参数来源 / 贡献总表（速查）

| 参数 | Base 输入 | 可修正 | 修正来源 | 影响 Biome | 影响体素 | 可被玩家改变 | 真值份数 |
|---|---|---|---|---|---|---|---|
| `Temperature` | 气候带 + 配置（默认 `0.5`） | ✅ | 自然类 Region | 是（直接） | 间接（经 Biome → P3） | ❌ | 1 |
| `Moisture` | 气候带 + 配置（默认 `0.5`） | ✅ | 自然类 Region | 是（直接） | 间接（P3） | ❌ | 1 |
| `Fertility` | 配置级 `SoilDepth` + Base 级 T/M | ✅ | 自然类 Region | 是（直接） | 间接（P3） | ❌ | 1 |
| `MagicDensity` | 全局基底（默认 `0`） | ✅ | 魔法来源 | 是（直接） | ❌（属性层） | ❌ | 1 |
| `HolyInfluence` | 全局基底（默认 `0`） | ✅ | 圣性来源 | 是（直接） | ❌ | ❌ | 1 |
| `CorruptionInfluence` | 全局基底（默认 `0`） | ✅ | 腐化来源 | 是（直接） | ❌ | ❌ | 1 |
| `Elevation` | 既有高度场（+ 大陆掩码） | ❌ | — | 间接（经 Ocean/高程带） | **是（P2）** | ❌ | 1 |
| `Ocean` | `Elevation < SeaLevel` | ❌ | — | 是（短路） | 间接（派生） | ❌ | 1 |
| `Balance`（派生） | — | ❌ | — | 间接（经两力量参数） | ❌ | ❌ | 1 |

## 附录 C：禁止清单

| # | 禁止项 | 反例（必须判为缺陷） | 依据 |
|---|---|---|---|
| **WSX-1** | 下游写参数 | Biome 提高 `MagicDensity` | `WSC53` |
| **WSX-2** | Runtime 回写参数 | 玩家释放魔法 ⇒ `MagicDensity` 上升 | `WSC58`/`WSC62` |
| **WSX-3** | 参数读取体素真值 | "这里被挖空 ⇒ 魔力降低" | `WSC19`/`WSC77` |
| **WSX-4** | 参数读取 Geology 结论 | `Fertility` 读 `CaveDensity`/层分类/`DepositZone` | `WSC26` |
| **WSX-5** | 参数读取标签/群系 | 参数求值读 `HistoryTags` 或 Biome | `WSC28`/`WSC29` |
| **WSX-6** | History 直连参数 | `事件 ⇒ MagicDensity`（跳过 Region） | `WSC45` |
| **WSX-7** | 修正 `Elevation`/`Ocean` | Region 抬高地形/改变海陆 | `WSC10`/`WSC99` |
| **WSX-8** | 直接写 `Balance` | 用修正项直接设 `Balance` | `WSC11` |
| **WSX-9** | 第 7 个修正分量 | 新增"腐蚀度"并求和 | `WSC40` |
| **WSX-10** | 覆盖语义 | "后出现的 Region 覆盖先出现的" | `WSC39` |
| **WSX-11** | 放大反馈 | 某 Region 因邻域参数高而增强 | `WSC38` |
| **WSX-12** | 覆盖/补偿 Region 贡献 | 合成时"归一化"或"补偿"Region 的修正 | `WSC43` |
| **WSX-13** | 参数快照/版本链 | 保留"上次求值的参数"并参与判定 | `WSC66` |
| **WSX-14** | 参数写回体素 | 用体素标记参数值 | `WSC82` |
| **WSX-15** | 第二套求值路径 | 查询时另算一套阈值求参数 | `WSC67`/`WSC103` |
| **WSX-16** | 世界级前置 | 为回答一个点而全量求值世界 | `WSC89`–`WSC92` |

## 附录 D：与既有规范的对齐登记与变更规则

### D.1 登记项（**本文档不修改任何既有文件**）

| # | 需对齐内容 | 位置 | 真相（按本契约） |
|---|---|---|---|
| ① | `Fertility` 由 `SoilDepth` / `Moisture` / `Temperature` 派生 | `WorldState_Design.md`:95 | **`SoilDepth` 必须理解为配置级**（`WorldGenerationSettings.SoilDepth`，默认 `3`，见 `WorldGenerationSettings.cs:29`）；**禁止**读取 Geology 的层分类结论（Surface/Depth/Deep）——否则构成 Phase 4 前向读取 Phase 6 产物（`WSC26`/`WSC27`） |
| ② | 同一行未指明 `Temperature`/`Moisture` 的"级别" | `WorldState_Design.md`:95 | 冻结为 **Base 级**（`WSC17`）：`Base.Fertility = f(配置级 SoilDepth, Base.T, Base.M)`；理由：无环、可分解、可独立验证 |
| ③ | 查询真值语义（审查 **P0-4**：`Pristine` 与 `Current` 未区分） | `WorldGeneration_Architecture_Audit.md`:202/272；`WorldQuery_Design.md`:107/46 | **参数侧**：只有一份真值（`Base + Modifier`），**无 `Current` 版本**（`WSC68`/`WSC69`）；**体素侧**：`Pristine`/`Current` 二分**仍然开放**（`AD-2`），归 `VoxelWorld`/`Mining`/`WorldQuery` 的后续决策 |
| ④ | 量纲迁移（审查 **AD-3**：实现整数域 vs 契约 `[0,1]`） | `WorldParameter_Contract.md` §5.3 | 沿用其**边界映射**（`[-500,600]→[0,1]`、`[0,1000]→[0,1]`）；**不修改代码** |
| ⑤ | `Elevation` 复用既有高度场 vs 保留域禁令（`PZ12`） | `WorldParameter_Contract.md`:400；`WorldState_Design.md`:94 | 复用高度场是**只读转发**（允许）；`PZ12` 禁止的是"**复用保留域作为新层的随机源**"，两者不冲突（`WSC49`） |
| ⑥ | 审查 **M1**（WorldState 层落地顺序） | `WorldGeneration_Architecture_Audit.md`:450 | 本契约是 M1 的**求值契约依据**；M1 仍需 M0 的决策（`AD-1`–`AD-4`）后执行 |

### D.2 变更规则

1. 修改最终公式、逐参数合成类型、求值顺序、`Base` 白名单、真值来源映射，**必须**升版本并同步更新 §2/§3/§7 与附录 B。
2. **不得放宽**：`WSC2`（不新增参数）、`WSC3`（不新增 VoxelType）、`WSC4`（不新增因果边）、`WSC10`/`WSC11`（不可修正量与派生量）、`WSC45`（History 无直连）、`WSC58`（Runtime 不回写）、`WSC78`（玩家修改隔离）。
3. 与既有规范冲突时，先按 D.1 **登记**，再由对应规范的**版本变更**解决；本文档不得直接改写其它规范。
4. 任何"运行时改变参数"的提案必须**独立成规范**，并证明：确定性不受影响、玩家修改隔离仍成立（`WSV14`）、既有验收全部保持 PASS、且**可关闭**。
5. 任何"参数快照/时间维度"的提案必须**独立成规范**，并证明其不与 `WSC66`（无时间维度）与 `HE5`（无覆盖语义）冲突。
