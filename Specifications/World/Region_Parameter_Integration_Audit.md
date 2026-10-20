# Region → Parameter Integration Audit（Phase 3.1.0 · 只读审查）

> **文档性质**：**只读审查报告（Read-only Audit）**。回答"`RegionModifier` 应当通过什么边界进入最终世界参数计算"。
>
> **本轮限制（已遵守）**：只读；**不修改任何现有文件**；**不新增 `.cs` / `.meta` / 测试 / Scene / Prefab**；不实现参数合成；不修改契约；不裁决未决项；不清理、不重置工作区。
>
> **证据口径**：所有结论必须落在**真实文件路径 / 类名 / 方法名 / 行号 / 契约条款**上。工作区 14 个 V2 `.cs` 的 SHA16 见附录 A；本报告以"`SHA16` 冻结 + 去注释结构扫描 + 关键行抽取"方式核验（**非**全文逐字阅读，见附录 A 说明）。
>
> **审查基准（工作区快照，本轮未变）**：
> `WorldParameterEvaluator.cs` `7B35DBB38A02E719` · `WorldParameterQuery.cs` `4935EE144B3EF932` · `WorldParameterRules.cs` `EB345A4E03674FF7` · `WorldParameterRule.cs` `73F45DE2178C58BF` · `WorldParameterState.cs` `6E65E7C8F5DC2CA1` · `WorldParameterHash.cs` `E9BF9D755F6A1813` · `RegionIdentity.cs` `398E66FFB4D809DC` · `RegionDetermination.cs` `081DCE7C2D6F1B87` · `RegionContext.cs` `67185B6213178DA1` · `RegionPipeline.cs` `BC68677D0443AE53` · `RegionModifier.cs` `14445D42F69F051A` · `RegionModifierDetermination.cs` `A0BA308631929D80` · `WorldSeed.cs` `954DD11740A86885` · `WorldCoordinate.cs` `CD9ECDCB6722823B`
>
> **【历史语境（后续轮次追加，不改本文历史结论）】** 本报告是 **Phase 3.1.0 时点快照**，§1.1 / §2.1 / §2.3 描述的是**当时**的代码状态，**不是当前规则**。经复核，上列 14 个 SHA16 基线中 **5 个已漂移**（`WorldParameterEvaluator.cs`、`WorldParameterQuery.cs`、`WorldParameterRules.cs`、`WorldParameterRule.cs`、`WorldParameterState.cs`），另 9 个未变。因此文中下列表述仅作**历史记录**保留：
> - `Elevation` 为「域哈希占位 `Hash.Range → [0,128)`」⇒ 现为**只读转发** `HeightFunction.GetTerrainHeight(position.X, position.Z, settings)`（域依赖已移除，`ProvisionalWorldHeight` 已删除）。
> - `Ocean` 为「`Elevation < 64`」⇒ 现为 `OceanRule` 严格比较 `elevation < settings.SeaLevel`；`SeaLevel` 是**世界生成配置**（`WorldGenerationSettings.SeaLevel`，登记默认值 `WorldGenerationSettings.DefaultSeaLevel = 64`），**不是** `Elevation` 的计算输入，也不改变 `HeightFunction` 的地形高度。
> - `Modifier` 恒为 `0f`、`RegionModifier` 无人调用 ⇒ 现由 `WorldStatePipeline` 两段式编排（Base → Region → `Finalize`）调用，但 Region 修正量仍为**占位实现**（`RegionModifierDetermination`，区间 `[0, 0.25)`），且 World V2 参数结果**仍无生产消费者**。
>
> 文中数字 `64` 出现在上述位置时均为**该时点的占位常量/测试条件**，不得据此推断当前实现。

---

## §1 当前实际代码调用链

### 1.1 真实调用链（含文件与行号）

```
[入口] WorldParameterQuery.Evaluate(WorldSeed, WorldCoordinate)          WorldParameterQuery.cs:42
   │        └─ 方法体仅一行转发（无逻辑）
   ▼
[Base 段 + 合成段 同一方法] WorldParameterEvaluator.Evaluate(seed, position)   WorldParameterEvaluator.cs 的公开入口
   │
   ├─ 静态自检 ValidateArchitecture()（规则表完整性，静态构造期一次性）        WorldParameterEvaluator.cs
   │
   ├─ 阶段 A：按冻结顺序遍历规则表 WorldParameterRules.GetRule(i)             WorldParameterEvaluator.cs
   │        └─ rule.EvaluateBaseValue(reader, seed, position)               WorldParameterEvaluator.cs:78
   │              · reader = EvaluatedBaseReader（只暴露**已完成步骤**的 Base 值） WorldParameterEvaluator.cs:69 / :201
   │              · 规则实现 = WorldParameterRules 内 8 个私有规则类：
   │                    ElevationRule(:80, step 0, Forwarded, Hash.Range→[0,128))
   │                    OceanRule(:123, step 1, Derived, `Elevation < 64`)
   │                    TemperatureRule(:163, step 2, AdditiveClamped, Hash.Unit)
   │                    MoistureRule(:197, step 2, AdditiveClamped, Hash.Unit)
   │                    FertilityRule(:235, step 3, AdditiveClamped, clamp01(T×M))
   │                    MagicDensityRule(:274, step 4, AdditiveClamped, 常量 0f)
   │                    HolyInfluenceRule(:306, step 4, AdditiveClamped, 常量 0f)
   │                    CorruptionInfluenceRule(:338, step 4, AdditiveClamped, 常量 0f)
   │
   ├─ 阶段 B：按身份顺序装配三态快照（槽位 = ParameterId − 1）
   │        float modifier = ZeroRegionModifier;                             WorldParameterEvaluator.cs:99
   │        float finalValue = Compose(kind, baseValue, modifier);            WorldParameterEvaluator.cs:100
   │        return new WorldParameterState(values);                          WorldParameterEvaluator.cs:106
   │
   └─ Compose(...)（唯一的合成实现）                                         WorldParameterEvaluator.cs:115
            AdditiveClamped → WorldParameterRules.Clamp01(base + modifier)  :119–120
            Forwarded / Derived → baseValue（**不加** modifier）            :122–124

[孤立链，当前无人调用]
WorldSeed + WorldCoordinate → RegionPipeline.ResolveContext(...)             RegionPipeline.cs:60
   └─ RegionDetermination.Determine(seed, position)                          RegionPipeline.cs:63
        └─ RegionIdentity（占位：域 8、固定来源/范围/标签 1，标识由整数混合导出）
              → new RegionContext(identity, 1L, 1, 0, 1)                     RegionPipeline.cs:62
                    ↓（无人调用）
              RegionModifierDetermination.Determine(RegionContext)           RegionModifierDetermination.cs:69
                    └─ new RegionModifier(Unit×6)                            RegionModifierDetermination.cs:90–96
                          ↓（无人消费）
                    RegionModifier（六分量，`[0, 0.25)` 占位区间）             RegionModifier.cs:38–43
```

### 1.2 逐类型职责（按真实代码）

| 文件 | 类型 / 成员 | 实际职责（代码事实） | 关键证据 |
|---|---|---|---|
| `WorldParameterQuery.cs` | `WorldParameterQuery.Evaluate` | **纯转发**：方法体只有 `return WorldParameterEvaluator.Evaluate(seed, position);` | `:42`、`:44` |
| `WorldParameterEvaluator.cs` | `WorldParameterEvaluator.Evaluate` / `Compose` | **Base 段求值 + 合成段**（两者在同一方法内）；合成规则按 `WorldParameterCompositionKind` 分支 | `:78`、`:99`、`:100`、`:115`–`:124` |
| `WorldParameterRule.cs` | `IWorldParameterRule` / `IWorldParameterBaseReader` / `WorldParameterIds` / `WorldParameterCompositionKind` | 规则扩展点 + 身份表引用 + **只暴露已完成步骤**的只读读取器 | `:197`、`:201`（读取器实现） |
| `WorldParameterRules.cs` | `WorldParameterRules` + 8 个私有规则类 | 冻结顺序（step 0–4）的**规则表**；每条规则产出 `BaseValue`；`CompositionKind` 决定其是否参与加法合成 | `:80`/`:123`/`:163`/`:197`/`:235`/`:274`/`:306`/`:338`；`:97`/`:140`/`:177`/`:211`/`:249`/`:291`/`:323`/`:355` |
| `WorldParameterState.cs` | `WorldParameterState` / `WorldParameterValue` | **定长 8 槽容器**（`Capacity = 8`，`:124`），三态（Id/Base/Modifier/Final），只读检索 | `:124`、`:126`、`:158`、`:175` |
| `WorldParameterHash.cs` | `WorldParameterHash` | 域标签确定性整数哈希（供规则使用；域参与混合） | `WorldParameterRules.cs:102`/`:182`/`:216` 调用 |
| `RegionIdentity.cs` | `RegionIdentity` | 身份值（5 字段：Id/Domain/Source/ExtentReference/ExplanationTag） | 字段清单见 §2.1 |
| `RegionDetermination.cs` | `RegionDetermination.Determine` | 占位身份判定（域固定 8，标识由整数混合导出） | `:69`（Phase 2.3） |
| `RegionContext.cs` | `RegionContext` | 解释上下文（5 字段：Identity + 3 引用/标签 + Version） | `:41`–`:45` |
| `RegionPipeline.cs` | `RegionPipeline.ResolveContext` | **连接边界**：`身份 → 上下文`；占位字段 1/1/0/1 | `:60`、`:62`、`:63` |
| `RegionModifier.cs` | `RegionModifier` | **六分量修正量**（T/M/F/Magic/Holy/Corruption），无其它字段 | `:38`–`:43` |
| `RegionModifierDetermination.cs` | `RegionModifierDetermination.Determine` | **`Context → Modifier` 唯一入口**（占位整数混合，幅度 `[0,0.25)`） | `:69`、`:90`–`:96` |

### 1.3 消费者映射（工作区实测）

| 被调用者 | 调用者（V2 目录内） | 说明 |
|---|---|---|
| `WorldParameterEvaluator` | `WorldParameterQuery`（5 处引用） | 参数侧唯一消费者 |
| `WorldParameterRules` / `WorldParameterRule` | `WorldParameterEvaluator`（8 / 2 处） | 规则表由求值方驱动 |
| `RegionContext` | `RegionPipeline`（5）、`RegionModifierDetermination`（3）、`RegionDetermination`（1） | 上下文只被 Region 侧消费 |
| `RegionModifier` | `RegionModifierDetermination`（4）、`RegionDetermination`（1，文档引用） | **无参数侧消费者** |
| `RegionPipeline` / `RegionModifierDetermination` | **无消费者**（0） | 两条 Region 链当前为**孤立入口** |
| `WorldParameterQuery` | **无消费者**（0） | 参数侧入口当前无人调用 |

| # | 事实 | 证据 |
|---|---|---|
| **A-1** | **两条链完全不相交**：参数侧文件的依赖矩阵中，对 `RegionIdentity` / `RegionContext` / `RegionModifier` 的引用均为 **0**；Region 侧文件对 `WorldParameter*` 的引用同样为 **0** | 全目录依赖矩阵（14×15）扫描结果 |
| **A-2** | `RegionModifier` 目前是**终端产物**（无消费者） | 同上 |
| **A-3** | V2 目录之外**没有任何** `.cs` 引用这 10 个类型（217 个外部文件逐一扫描，全部 0 处） | 外部引用扫描 |
| **A-4** | Editor 侧验证入口共 **45** 个 `*Validation*.cs`，其中 12 个名称属于 V2 侧（`VoxelWorldCoreValidation`、`WorldGenerationCoreValidation`、`VoxelMeshCoreValidation`、`VoxelWorldViewValidation`、`VoxelWorldRuntimeCoreValidation`、`ChunkStreamingValidation`、`GeologyStageA/B/C/DValidation`、`GeologyRuntimeIntegrationValidation`、`TerrainVoxelMeshIntegrationValidation`） | 文件清单扫描 |

> **注（与既有报告的分类差异）**：此前轮次把 Editor 验证入口记为"Legacy 34 + V2 11 = 45"。本轮实测总数 **45 一致**，但按名称归类得 12 个 V2 名；差异来自 `TerrainVoxelMeshIntegrationValidation` 的归类边界（它同时含 Legacy 地形与 V2 网格词元）。**该差异不影响本审查任何结论**，仅登记为口径差异（附录 B）。

---

## §2 当前参数计算的真实行为及其证据

### 2.1 事实清单

| # | 事实（代码行为） | 证据（文件:行） | 契约对照 |
|---|---|---|---|
| **B-1** | 参数查询入口只做转发，**不含任何求值逻辑** | `WorldParameterQuery.cs:42`–`:44` | `WSC67`（查询必须走同一求值路径）、`DR35` |
| **B-2** | `BaseValue` 由规则表按冻结顺序 0–4 求出；规则只能读**已完成步骤**的 Base 值 | `WorldParameterEvaluator.cs:69`/`:78`；`WorldParameterRules.cs` 各 `EvaluationStep` | `WSC §2.3`、`WSC16`、`WSC17` |
| **B-3** | **`Modifier` 是编译期常量 `0f`**（`ZeroRegionModifier`），对全部 8 个参数一律为 0 | `WorldParameterEvaluator.cs:46`、`:99` | `WSC14`（零修正显式性）、`WSC22`（Region 关闭 ⇒ `Final == Base`） |
| **B-4** | 6 个连续参数（T/M/F/Magic/Holy/Corruption）走 `AdditiveClamped`，即 `clamp01(Base + Modifier)`；因 B-3，**当前实际等于 `clamp01(Base)`** | `WorldParameterEvaluator.cs:119`–`:120`；`WorldParameterRules.cs:177`/`:211`/`:249`/`:291`/`:323`/`:355` | `WSC9`、`WSC12`、`RA64` |
| **B-5** | `Elevation`（Forwarded）与 `Ocean`（Derived）**不参与加法**：`Compose` 直接返回 `baseValue`，`modifier` 被忽略 | `WorldParameterEvaluator.cs:122`–`:124`；`WorldParameterRules.cs:97`/`:140` | `WSC10`（"缺省 = 不存在"）、`RA69`、`RE1` |
| **B-6** | 快照容器**定长 8 槽**（`Capacity = 8`），槽位 = `ParameterId − 1`；**容器内没有 `Balance`** | `WorldParameterState.cs:124`、`:126`、`:158`；`WorldParameterIds`（1..8） | 附录 B（`Balance` 为序 15、派生只读）、`WSC §2.3` 步骤 7 |
| **B-7** | 规则的占位来源均来自**已登记面**：T/M = 域 1 标签整数哈希；Elevation = 哈希整数 `[0,128)`；Fertility = `clamp01(Base.T × Base.M)`；Magic/Holy/Corruption = 常量 `0f`；Ocean = `Elevation < 64` | `WorldParameterRules.cs:102`/`:182`/`:216`/`:263`/`:277`/`:309`/`:341`/`:83`/`:126` | `PZ11`/`PZ12`；`WSC27`（SoilDepth 未接入，已登记差异） |
| **B-8** | `RegionModifier` 六分量 = **契约分量集**（Temperature/Moisture/Fertility/MagicDensity/HolyInfluence/CorruptionInfluence），**不含** `Elevation`/`Ocean`/`Balance` | `RegionModifier.cs:38`–`:43` | `RA67`（分量恰好 6）、`RA69`（不含地形量）、`PC24` |
| **B-9** | `RegionContext` = **单区域粒度**（1 个 `Identity` + 3 个引用/标签 + 1 个版本），不含"区域集合" | `RegionContext.cs:41`–`:45` | `RRD21`–`RRD25`（上下文组成）、`RRD26`（修正量 6 分量） |
| **B-10** | `RegionPipeline.ResolveContext` 每次只返回**一个** `RegionContext`（= 一个区域） | `RegionPipeline.cs:60`–`:63` | 与 `RA17`/`RA63`/`Region_Design §9.2`（一点可受**多个** Region 影响）**形状不匹配** → 见 §3.5 |

### 2.2 `BaseValue` / `RegionModifier` / `FinalValue` 的**实际**关系

```
代码现状（可执行事实）：
    BaseValue(参数 p)   ← WorldParameterRules 规则（step 0–4）
    RegionModifier(六分量) ← RegionModifierDetermination（**无人调用**）
    Modifier(参数 p)    ← 常量 0f（ZeroRegionModifier）  ← **不是** RegionModifier
    FinalValue(参数 p)  = Compose(kind, BaseValue, 0f)
                          ├─ 6 连续分量：clamp01(BaseValue)
                          └─ Elevation / Ocean：BaseValue（不合成）

契约目标（已冻结，尚未实现）：
    FinalState(p) = clamp01( BaseWorldState(p) + RegionModifier(p) )      WorldState_Runtime_Contract.md §2.1
    RegionModifier(p) = Σ_r Contribution_r(p)                            Region_Design.md §9.2 / Region_Architecture_Contract.md RA62–RA63
```

| # | 结论 | 证据 |
|---|---|---|
| **B-11** | **"三态已就位、修正量尚未接入"**：`WorldParameterValue` 已携带 `Modifier` 字段，但它的值恒为 0，且 `RegionModifier` 从未被读取 | `WorldParameterEvaluator.cs:99`；依赖矩阵中 `RegionModifier` 的参数侧引用 = 0 |
| **B-12** | **不存在第二套求值路径**（当前）：查询唯一入口 → 求值唯一入口 → 规则表 | `WorldParameterQuery.cs:44`；`WorldParameterEvaluator.cs` 为唯一公开求值入口 |

### 2.3 `Elevation` / `Ocean` / `Balance` 的现状（逐项）

| 参数 | 当前来源 | 是否参与加法合成 | 保护机制（现有，已生效） |
|---|---|---|---|
| `Elevation` | `ElevationRule` 占位哈希整数 `[0,128)`（`WorldParameterRules.cs:83`） | **否**（`Forwarded`，`Compose` 返回 Base） | 类型级：`RegionModifier` 无该字段；合成级：`Compose` 忽略 modifier；契约级：`WSC10`/`RA69`/`RE1` |
| `Ocean` | `OceanRule` 派生：`Elevation < 64`（`:126`） | **否**（`Derived`） | 同上 |
| `Balance` | **不存在**于 8 参数容器（`Capacity = 8`，容器内无该槽） | 不适用 | 容器级：无槽位；契约级：`WSC11`/`RA70`/`RO2`（派生只读，不得被修正直写） |

---

## §3 Region 修正的推荐接入位置

### 3.1 唯一合成权威：契约 + 代码都已存在

| # | 结论 | 依据 |
|---|---|---|
| **C-1** | **契约已指定唯一合成权威 = WorldState 层**（不是 Region 层、不是 Query 层、不是下游层） | `WorldState_Runtime_Contract.md` §2.1（合成权 = WorldState）；`WorldParameter_Contract.md` §4 权限矩阵（Region 行 = `P（修正项）`、WorldState 行 = `O（合成）`）；`RA75`/`RA76`；`PC20` |
| **C-2** | **代码中已有该权威的实现位点**：`WorldParameterEvaluator.Compose(...)` 是当前唯一的加法+钳制实现 | `WorldParameterEvaluator.cs:115`–`:124` |
| **C-3** | **接入不需要新公式**：`clamp01(Base + Modifier)`、逐分量加法、交换律、钳制到 `[0,1]` 全部已冻结 | `WSC9`/`WSC12`/`WSC14`；`RA62`/`RA63`/`RA64`；`Region_Design.md` §4.2（加法+钳制）、§9.2（`Σ` 后钳制）、`RO1`/`RO4`；`PS5` |

### 3.2 为什么**不能**接在 Base 段

| # | 原因 | 依据 |
|---|---|---|
| **C-4** | `Base` 必须**能在不计算任何区域的前提下**求出；Base 输入集合**封闭**，不得读取 Region 的任何字段 | `WSC21`（Base 可分离）、`WSC25`（输入封闭）、`WSC28`（不得读 Region 字段/标签） |
| **C-5** | Region 又**不得**读取 `FinalState`（未定稿）与未定稿 `Base` 运行值，否则构成环 | `RAB19`/`RAB20`/`RAB32`；`PC19`/`PC21` |
| **C-6** | `WSC §2.3` 把 `RegionModifier` 明确定义为**步骤 5**（在步骤 0–4 的 Base 之后、步骤 6 的 FinalState 之前） | `WorldState_Runtime_Contract.md` §2.3（已逐字核对） |
| **C-7** | 当前代码把 Base 段与合成段放在**同一方法** `Evaluate`（`:78` 求 Base、`:99` 取 modifier、`:100` 合成）——若直接把 Region 调用塞进该方法的 Base 循环，会让 Region 输入**进入 Base 求值路径**，违反 C-4 | `WorldParameterEvaluator.cs:78` 与 `:99` 的位置关系 |

### 3.3 推荐接入点（结论）

> **推荐：把"Region 修正的读取"放在 Base 段**之后**、由 WorldState 层的合成步骤消费；`Modifier` 从入参/上游结果获得，而不是由 `WorldParameterEvaluator` 内部去认识 Region 层。**

| # | 结论 | 依据 |
|---|---|---|
| **C-8** | 接入语义 = "把 `ZeroRegionModifier` 常量替换为**上游给定的修正量**"，`Compose` 的加法+钳制**保持不变** | `WorldParameterEvaluator.cs:46`/`:99`/`:115`–`:120` |
| **C-9** | 必须保留 `Compose` 的 `kind` 分支：**只有 `AdditiveClamped` 的 6 个分量接受修正**；`Forwarded`/`Derived` 继续无视修正 | `WSC10`、`RA69`、`RE1`；代码现状 `:122`–`:124` |
| **C-10** | 钳制必须由**合成方**执行（`clamp01`），Region 侧不得预钳制、不得归一化（Region 只提供修正项） | `WSC12`；`RA73`（贡献不得含常量偏置）、`RA64`；`Region_Design.md` `RE3`（值域与钳制必须显式） |
| **C-11** | 修正量的**表示法缺口**：`Elevation`/`Ocean` 的 `Modifier` 按契约应表示为 `None` 而非 `0`（本轮不裁决） | `PM32`；`WSC10`；已登记 `B-3`、`WPS58` |

### 3.4 多 Region 叠加：**契约已定**与**未定**分别是什么

| # | 问题 | 状态 | 依据 |
|---|---|---|---|
| **C-12** | **聚合方式（求和 vs 最大 vs 覆盖）** | **已冻结 = 逐分量加法求和 + 钳制**，且必须满足交换律、不得有放大反馈 | `Region_Design.md` §2.1（`RegionModifier(p)` = "该位置受到的全部区域修正**之和**"）、§9.2（`Σ` 后钳制）；`RA62`/`RA63`/`RA66`；`RO1`/`RO5` |
| **C-13** | **`Holy` / `Corruption` 是否互相抵消** | **已冻结 = 各自独立叠加**；"抵消"只出现在派生量 `Balance` | `Region_Design.md` §9.3；`RA71`；`WSC18` |
| **C-14** | **单点参与叠加的 Region 数量上限** | **未定的数值**（"必须有显式上限"已冻结，但上限值属实现规范） | `RA65`/`RA130`；`RO7`；`Region_Architecture_Contract.md` §13.2 第 3 项 |
| **C-15** | **身份判定的输出基数（单值 vs 集合）** | **未决**（`RID60`）：当前代码只能表达**单区域**（`RegionPipeline.ResolveContext` 返回一个 `RegionContext`），与 C-12 的"求和"形状**不匹配** | `RID60`（§5.2 未决问题登记）；`RegionPipeline.cs:60`；`RegionContext.cs:41`–`:45` |
| **C-16** | **某点"有哪些 Region 影响我"的枚举** | 契约要求**可局部枚举**且代价与相关数量相关；**实现不存在** | `RA17`、`RA127`–`RA130`；`RRD63`/`RRD65` |

> **本审计不裁决 C-14/C-15/C-16**：按 `RID64`（登记纪律）与 `RRD102`，实现方不得单方面解读；报告只登记"接入方案在此依赖未决项"。

---

## §4 可行方案与依赖方向

### 4.1 方案 A（推荐）：**参数层新增"合成入口"，修正量以数据入参**

```
WorldSeed + WorldCoordinate
      │
      ├──────────────► RegionPipeline.ResolveContext ─► RegionContext
      │                        └─► RegionModifierDetermination.Determine ─► RegionModifier
      │                                                                        │（值传递，不传类型依赖）
      ▼                                                                        ▼
WorldParameterEvaluator（Base 段，保持输入封闭）  ──►  新合成入口（WorldState 层）
                                                        Final = clamp01(Base + Modifier)
      ▲                                                        │
      └──────────── WorldParameterQuery（唯一查询入口，转发） ◄──┘
```

| # | 要点 | 说明 |
|---|---|---|
| **D-1** | 新增**一个** WorldState 层合成入口（建议签名：`Compose(WorldSeed, WorldCoordinate, RegionModifier)` → `WorldParameterState`），内部复用既有 Base 求值 + `Compose` | 不改 Base 段；`ZeroRegionModifier` 仅作为"无修正"退化值 |
| **D-2** | 参数侧**不引用** Region 类型：`RegionModifier` 由外部传入 | 参数层 → Region 层依赖 = **0**；无环 |
| **D-3** | 查询入口 `WorldParameterQuery.Evaluate` 改为调用合成入口（保持"仅转发"） | `WSC67`/`DR35`/`WSC103`（单一求值路径） |
| **D-4** | 退化保证：不传修正量时输出与今日**逐字段一致** | `WSC22`；`RA104`（关闭 Region ⇒ 逐体素一致） |
| **D-5** | 依赖的未决项：`RID60`（集合形态）——当前只能表达单区域修正 | 见 §3.4 `C-15` |

### 4.2 方案 B：**WorldState 层编排器**（自己调用 Region 链）

```
WorldSeed + WorldCoordinate
      │
      ▼
[新] WorldState 层编排器 ──► RegionPipeline.ResolveContext ──► RegionModifierDetermination.Determine
      │                                                                     │
      └──────────────► WorldParameterEvaluator（Base 段）◄───────────────────┘
                              ▼
                       Final = clamp01(Base + Modifier)
```

| # | 要点 | 说明 |
|---|---|---|
| **D-6** | 编排器负责"取 Base + 取修正 + 合成"，**唯一入口**由 `WorldParameterQuery` 调用 | 单路径最强保证（`WSC103`） |
| **D-7** | 代价：参数层出现对 Region 层的**显式依赖**（新增跨层引用） | 契约允许（WorldState 是合成 Owner，需消费 `RegionModifier`，`RA76`/`PC20`），但违反本会话一贯的"层间零引用"风格；且必须**只允许合成段**引用，Base 段不得引用 |
| **D-8** | 同样依赖未决项 `RID60` | 同 `D-5` |

### 4.3 两方案差异与选择建议

| 维度 | 方案 A（数据入参） | 方案 B（编排器） |
|---|---|---|
| 跨层依赖 | **无**（参数层不认识 Region 类型） | 有（WorldState 层 → Region 层） |
| 单一求值路径保证 | 依赖调用方纪律（较弱） | **结构性保证**（较强，`WSC103`） |
| 循环依赖风险 | 无 | 无（Region 侧对参数层引用 = 0，已实测） |
| 与 `PC19` 两段式发布一致性 | **高**（Base 段与 Final 段可分别发布） | 中（同一次调用内完成两段） |
| 适配未决项 `RID60` 的难度 | 低（入参可从"单个"扩展到"集合/聚合值"） | 低（编排器内部聚合） |
| 本审计建议 | **推荐先落地方案 A 的"入参 + 合成"骨架**，并按 B 的方式在**同一 WorldState 层**提供唯一编排入口（两者可并存：入口属 B，依赖方向属 A） | — |

### 4.4 依赖方向校验（本轮实测）

| # | 待验证命题 | 实测结果 | 判定 |
|---|---|---|---|
| **D-9** | 参数计算**不**反向依赖 Region 内部实现 | 参数侧 6 个文件对 Region 类型引用 = **0** | ✅ 当前成立（接入后需保持） |
| **D-10** | Region **不**读取 `FinalValue` | Region 侧 6 个文件对参数类型引用 = **0**；`RegionModifierDetermination` 只读 `Identity`/3 个上下文字段 | ✅ 成立 |
| **D-11** | 是否会产生**循环依赖** | 无环：Region 链只依赖 `RegionIdentity`/`RegionContext`/`RegionModifier`；参数链只依赖参数类型 | ✅ 成立（两方案都不引入环） |
| **D-12** | 同一位置的**查询与生成**是否会采用不同规则 | 当前两者**都不使用**本套类型（外部引用 0 处）；一旦接入，二者**必须**共用唯一合成入口，否则违反单路径 | ⚠️ **接入时必须建立的约束** |
| **D-13** | 查询过程是否存在写入/缓存/副作用 | 参数侧与 Region 侧实现均**无字段、无缓存**（仅 `const` + 纯函数，反射已验证）；查询只读 | ✅ 成立 |
| **D-14** | 目标单向链可行性 | `Seed+Coord → RegionPipeline → RegionContext → RegionModifierDetermination → RegionModifier → 合成 → Final` 在类型层面**可连通**，唯一缺口 = 聚合形态（`C-15`/`D-5`） | ⚠️ 有条件成立 |

---

## §5 对现有契约的逐项核对

| 契约条款 | 要求 | 当前代码实际 | 判定 |
|---|---|---|---|
| `WorldState_Runtime_Contract.md` §2.1（最终公式） | `FinalState = clamp01(Base + RegionModifier)` | `Compose` 已实现 `clamp01(Base + Modifier)`，但 `Modifier ≡ 0` | **部分实现**（公式在、输入缺） |
| §2.2（逐参数合成类型） | 6 连续分量加法；`Elevation`/`Ocean` 无合成；`Balance` 派生 | 完全一致（`AdditiveClamped` / `Forwarded` / `Derived`） | ✅ 一致 |
| §2.3（求值内部顺序 0–7） | 步骤 0–4 Base、5 RegionModifier、6 Final、7 Balance | 代码实现步骤 0–4，**步骤 5 缺失**（恒 0），步骤 6 为退化形态，步骤 7 未实现 | **缺口 = 步骤 5** |
| `WSC9`/`WSC12`（只加法+钳制；钳制 `[0,1]`） | 同 | `Compose` 仅加法 + `Clamp01` | ✅ 一致 |
| `WSC10`（`Elevation`/`Ocean` 不进入求和式） | 同 | `Forwarded`/`Derived` 分支返回 `baseValue`，忽略 modifier | ✅ 一致（保护已生效） |
| `WSC14`（零修正显式性） | 无 Region 时逐分量为 0 | `ZeroRegionModifier = 0f`（显式常量） | ✅ 一致 |
| `WSC21`/`WSC22`（Base 可分离；关闭 Region ⇒ `Final == Base`） | 同 | Base 段不读 Region；`Modifier = 0` ⇒ `Final == clamp01(Base)` | ✅ 一致（退化即今日行为） |
| `WSC25`（Base 输入封闭） | 白名单外不得读取 | Base 段输入 = `Seed`/坐标/域 1/只读哈希 | ✅ 一致（接入不得破坏） |
| `WSC103`（禁第二套求值路径） | 只能一个求值入口 | 唯一入口 = `WorldParameterEvaluator`；`WorldParameterQuery` 只转发 | ✅ 一致（接入后必须保持） |
| `Region_Architecture_Contract.md` `RA62`–`RA64`（加法/交换律/钳制） | 同 | 加法已实现；交换律天然满足（单一修正量入参时无顺序问题） | ✅（单区域时）/ **多区域待实现** |
| `RA67`/`RA68`（分量恰好 6、禁第 7 个） | 同 | `RegionModifier` 恰好 6 字段 | ✅ 一致 |
| `RA69`（`Elevation`/`Ocean` 不在修正集） | 同 | 类型与合成双重保护 | ✅ 一致 |
| `RA70`（`Balance` 不得被修正直写） | 同 | 容器无 `Balance` 槽；`RegionModifier` 无该字段 | ✅ 一致 |
| `RA75`/`RA76`（Region 唯一写入者；WorldState 只合成） | 同 | Region 侧产出 `RegionModifier`；参数侧只做加法 | ✅ 一致（接入后仍须保持） |
| `RA104`（关闭 Region ⇒ 逐体素一致） | 同 | 当前 `Modifier = 0` 即"关闭"状态 | ✅ 一致（未来须以回归测试守住） |
| `RA127`–`RA130`（局部求值/枚举上限） | 单点可枚举、代价局部、上限显式 | **未实现**（无枚举入口；`ResolveContext` 只给单区域） | **缺口**（依赖 `C-14`/`C-16`） |
| `Region_Design.md` §2.1/§9.2（`Σ` 求和后钳制） | 同 | 未实现（仅单值） | **缺口 = 聚合** |
| `Region_Design.md` §4.2/`RE1`–`RE3` | 6 分量加法+钳制；不含地形量；值域显式 | 分量集一致；钳制在合成方（`WSC12`） | ✅ 一致 |
| `Region_Runtime_Data_Contract.md` `RRD21`–`RRD27` | 上下文 5 类字段、修正量 6 分量 | 类型完全一致 | ✅ 一致 |
| `RRD59`–`RRD72`（查询边界：只读、不触发生成） | 同 | Region 侧实现无写操作、无缓存 | ✅ 一致（尚无 Query 实现） |
| `Region_Identity_Determination_Contract.md` `RID25`–`RID38`（判定边界与确定性） | 同 | `RegionDetermination` 满足（纯函数、无空间输入、占位） | ✅ 一致 |
| `RID60`（输出基数未决） | **未决** | 当前实现 = 单值 | ⚪ **未决**（不裁决） |
| `RID67`–`RID76`（`Identity → Context → Modifier` 单向） | 同 | 类型依赖方向一致（Region 侧不引用参数侧） | ✅ 一致 |
| `WorldParameter_Contract.md` §4 矩阵（Region = `P`、WorldState = `O`） | 同 | 与代码职责一致 | ✅ 一致 |
| `WorldGeneration_Pipeline_Contract.md` `PC19`/`PC20`/`PC21`/`PC24` | 两段式发布；Region 唯一写入；下游只读定稿 `FinalState`；6 分量 | 步骤 5 缺失 ⇒ 目前只有 Base 段定稿 | **部分一致**（Base 段符合，Final 段未实现） |
| `PC25`/`PC26`（交换律/无放大反馈；History 接入） | 同 | 当前无多源合成，无法验证 | **未验证**（多区域落地后验证） |
| `WorldParameter_Source_Contract.md` `WPS18`–`WPS31`（依赖方向禁令） | 禁止反向边 | 实测成立 | ✅ 一致 |

---

## §6 风险清单（P0 / P1 / P2）

### P0（阻塞正确性，接入前必须解决或显式降级）

| # | 风险 | 说明 | 依据 |
|---|---|---|---|
| **P0-1** | **聚合形态缺口**：`RegionPipeline.ResolveContext` 每次只产出**单区域**上下文，而契约要求该点修正 = **多点/多区域之和** | 若直接接入，会把"单区域修正"误当成"该点全部修正"，静默产生错误环境值 | `Region_Design.md` §9.2；`RA63`；`RID60`（未决） |
| **P0-2** | **步骤 5 缺失导致的"零修正"是隐式事实**：当前 `Modifier ≡ 0` 是**编译期常量**，没有任何机制阻止未来实现者把 `ZeroRegionModifier` 直接替换为某一处 Region 调用，从而让 Region 输入渗入 Base 段 | 需在接入时**结构性地**把 Base 段与合成段分开（或至少把修正量作为入参），否则违反 `WSC21`/`WSC25` | `WorldParameterEvaluator.cs:78`/`:99`；`WSC21`/`WSC25` |
| **P0-3** | **单一求值路径退化风险**：接入后若出现"查询走 A、生成走 B"两条路径 | 违反 `WSC103`/`DR35`/`WQ1`；且外部 0 消费者的现状说明**没有任何机制**强制二者一致 | `WSC67`/`WSC103`；`WorldParameterQuery.cs:44` |
| **P0-4** | **`Modifier` 字段与参数身份的映射缺少机械校验**：`RegionModifier` 是 6 个同类型 `float` 字段，位置一旦错配（如把 Magic 写到 Holy）不会被编译器发现 | 需显式映射表 + 逐分量验收 | `RA67`；附录 B 身份表；`RegionModifier.cs:38`–`:43` |

### P1（影响可维护性/可验证性，应在接入轮次内一并处理）

| # | 风险 | 说明 | 依据 |
|---|---|---|---|
| **P1-1** | **`Modifier` 的 `None` 表示缺口**：转发/派生参数按契约应表示为 `None`（而非 `0`） | 接入时若统一以 `0f` 承载，会让"无修正"与"修正为 0"不可区分 | `PM32`；`WSC10`；登记项 `B-3`/`WPS58` |
| **P1-2** | **`RegionContext` 单实体的语义边界**：`RegionContext` 是"一个区域"的解释，但查询"某点的最终环境"需要该点**全部**区域 | 语义混用会导致解释输出与合成输入不一致 | `RRD21`–`RRD25`；`RA17` |
| **P1-3** | **Region 侧的占位强度**：`RegionModifierDetermination` 目前输出 `[0,0.25)` 的**非语义**占位值；一旦接入合成，它会**真实改变**参数结果 | 占位期与接入期必须区分，否则占位值会被当作世界事实 | `RRD102`；`RegionModifierDetermination.cs:47`–`:56`（占位常量） |
| **P1-4** | **验收入口缺位**：45 个 Editor 验证文件中**没有**参数装置/区域修正相关的验证入口 | 接入轮必须同时给出验证入口（或明确不新增、改由外部脚本验证） | Editor 文件清单实测 |

### P2（已登记、暂不阻塞）

| # | 风险 | 说明 |
|---|---|---|
| **P2-1** | `B-7`（`Elevation` 占位跨度 128、临时域标签）与接入无直接关系，但会让"退化基线"含占位值 | 已在 Phase 1.6 登记（`WPS55`/`WPS73`/`WPS74`） |
| **P2-2** | 未决项 `A-1`/`A-2`/`A-3`/`A-4`、`B-1`–`B-9` 与本次接入无直接依赖 | 见 §9 |
| **P2-3** | Editor 验证入口分类口径差异（V2 名 12 vs 旧报告 11，总数 45 一致） | 仅口径问题（附录 B） |

---

## §7 结论三分类

### 7.1 已确定事实（代码或契约可直接验证）

| # | 事实 |
|---|---|
| **F-1** | 参数侧与 Region 侧**当前完全不相交**：双向类型引用均为 0（依赖矩阵实测） |
| **F-2** | `RegionModifier` 目前是**终端产物**，无任何消费者；`RegionPipeline` / `RegionModifierDetermination` 同为孤立入口 |
| **F-3** | 参数合成的**唯一实现**位于 `WorldParameterEvaluator.Compose`（`:115`–`:124`），且已按 `kind` 区分"可加"与"不可加" |
| **F-4** | `Modifier` 的实际取值 = 常量 `0f`（`:46`/`:99`）；因此当前 `Final == clamp01(Base)`（6 连续分量）或 `Final == Base`（`Elevation`/`Ocean`） |
| **F-5** | 契约已冻结：合成权属 WorldState 层；Region 是 `Modifier` 唯一写入者；只允许加法 + 钳制；多区域聚合 = **求和**；`Elevation`/`Ocean` 不得进入求和式；`Balance` 为派生只读 |
| **F-6** | `RegionModifier` 的字段集**恰为**契约 6 分量（无 `Elevation`/`Ocean`/`Balance`/`Danger`） |
| **F-7** | `WorldParameterState` 容器**定长 8 槽**、无 `Balance` 槽位 |
| **F-8** | 参数侧与 Region 侧实现**均无字段、无缓存、无副作用**（反射验证：仅 `const` + 纯函数） |
| **F-9** | V2 目录之外 **0 处**引用这 10 个类型（含运行时与场景脚本） |
| **F-10** | `RID60`（身份输出基数）**未决**，且当前 API 形状为**单区域** |

### 7.2 推断（由事实 + 契约推导，尚未实现验证）

| # | 推断 | 置信度 | 依据 |
|---|---|---|---|
| **I-1** | 接入点应当是"**合成步骤**"，而不是 Base 段或 Region 侧 | 高 | `WSC §2.3` 步骤序、`RA75`/`RA76`、`PC20` |
| **I-2** | 最小侵入的接线方式 = 把 `ZeroRegionModifier` 换成**入参修正量**，保持 `Compose` 不变 | 高 | `:99`/`:115`–`:120` 的现状 |
| **I-3** | 若不先解决 `RID60`，接入只能实现"**单区域退化版**"，与契约的多区域语义不等价 | 高 | `Region_Design.md` §9.2 vs `RegionPipeline.cs:60` |
| **I-4** | 参数侧一旦引用 Region 类型，就会让"Base 输入封闭"难以机械验证（虽不必然违反） | 中 | `WSC25`/`WSC21` 的验证方式（结构扫描） |
| **I-5** | 接入后必须新增参数装置侧的验证入口，否则退化等价与钳制行为无法守住 | 中 | `RA104`/`WSC22` 的验收要求 |

### 7.3 未验证事项（本轮未做，需在实现轮验证）

| # | 未验证事项 | 原因 |
|---|---|---|
| **N-1** | 多区域聚合的实际行为（求和/交换律/上限） | 无枚举入口，`RID60` 未决 |
| **N-2** | 接入后"查询 vs 生成"是否真的一致 | 两侧均无消费者，无法观测 |
| **N-3** | `RegionModifier` 与 8 参数身份映射的**数值级**正确性 | 尚未接线，无数据流 |
| **N-4** | 15 个 V2/Editor 验证入口对本议题的覆盖度 | 本轮只统计文件数量，未逐个读取其断言 |
| **N-5** | 三个 `Region` 占位值（域 8 / 来源 1 / 标签 1）接入后的影响面 | 属占位期，接入前应保持"可关闭/可忽略" |

---

## §8 下一阶段最小实现建议（**尚未执行**）

### 8.1 拟新增 / 修改的文件

| 动作 | 文件 | 说明 |
|---|---|---|
| **新增（1 个）** | `Assets/Scripts/World/V2/WorldParameterFinalState.cs`（名称待定） | WorldState 层的**合成入口**：Base 段复用既有求值方；修正量由入参提供；返回 `WorldParameterState`（`Final` 已合成） |
| **修改（1 个，仅 1 行级改动）** | `WorldParameterQuery.cs` | 把转发目标从"Base 求值"改为"合成入口"（保持"仅转发"形态） |
| **不改** | `WorldParameterEvaluator.cs` / `WorldParameterRules.cs` | Base 段保持**输入封闭**（不引入 Region 类型） |
| **不改** | Region 侧 6 个文件 | Region 只提供 `RegionModifier`，不反向依赖 |

### 8.2 公开 API 应承担的职责（建议）

| API（建议签名） | 职责 | 明确不做 |
|---|---|---|
| `Compose(WorldSeed seed, WorldCoordinate position, RegionModifier regionModifier)` → `WorldParameterState` | ① 取 Base（调用既有 `WorldParameterEvaluator.Evaluate`）；② 把 `regionModifier` 逐分量映射到 6 个 `AdditiveClamped` 参数；③ `clamp01(Base + Modifier)`；④ `Forwarded`/`Derived` 参数**忽略**修正 | 不读 Region 类型、不创建 Region、不写体素、不缓存、不做任何判定 |
| （若采纳方案 B）`Resolve(...)` 编排入口 | ① `RegionPipeline.ResolveContext` → ② `RegionModifierDetermination.Determine` → ③ 调用 `Compose` | 同上；且**必须**是查询/生成的唯一入口 |

### 8.3 验证方式（最小集）

| # | 验证项 | 判据 | 依据 |
|---|---|---|---|
| **V-1** | **退化等价**（最重要） | 传入"零修正"时，输出与今日**逐字段相同**（含 `Elevation = 哈希整数`、`Ocean = 0/1`） | `WSC22`；`RA104` |
| **V-2** | 加法 + 钳制 | 构造 `Base + Modifier > 1` 与 `< 0` 的样例，验证落在 `[0,1]` 且等于 `clamp01` | `WSC9`/`WSC12`；`RA64` |
| **V-3** | `Elevation`/`Ocean` 免疫 | 任意 `RegionModifier` 下，两者 `Final` 与 Base 求值结果**逐位相同** | `WSC10`；`RA69` |
| **V-4** | 分量映射 | 6 个分量分别单独置非零，验证只有对应参数变化 | `RA67`；附录 B 身份表 |
| **V-5** | 身份/顺序 | 同 `Seed` + 同坐标重复求值逐字段一致；跨进程一致 | `PZ1`/`PM52`；`RAV3` |
| **V-6** | 单一路径（结构扫描） | 参数求值路径中不出现"第二套"合成实现；查询入口只有一条转发链 | `WSC103`；`DR35` |
| **V-7** | 无副作用/无缓存 | 反射：新增类型无字段（或仅 `const`）；求值前后无外部状态变化 | `WQ4`；`RA138`；`T4` |

---

## §9 未决项及其对实施的影响

| 未决项 | 内容 | 对"接入"的影响 | 是否阻塞 |
|---|---|---|---|
| **`RID60`** | 身份判定输出基数（单值 vs 集合） | 决定"一点的全部修正"如何取得：单区域退化 vs 多区域求和 | **阻塞**（P0-1） |
| `RA65` / `RA130` / `RO7` | 单点参与叠加的数量上限（**必须有**已冻结，**数值**未定） | 影响性能边界与验收样例设计，不阻塞结构 | 不阻塞 |
| `C-16`（`RA17`/`RA127`–`RA129`） | "影响该点的 Region 枚举"实现 | 多区域聚合的前置；无枚举则只能单区域 | **阻塞多区域** |
| **`B-3`**（含 `WPS58`、`PM32`） | 参数三态物理存储 + 转发/派生参数的 `None` 表示 | 影响 `Modifier` 在"无修正"与"修正为 0"间的可区分性 | 不阻塞（可用 `0f` 暂代并登记） |
| `B-1` | Runtime Modification 边界（改动集/编辑层） | 与本议题无关（Region 修正属生成期） | 不阻塞 |
| `B-2` | 配置参数无 Owner 登记 | 本议题不新增配置参数 | 不阻塞 |
| `A-1`/`A-2`/`A-3`/`A-4` | 体素写入者 / 双真值 / 阶段编号 / 前缀多义 | 与参数合成边界无直接依赖 | 不阻塞 |
| `B-4`（术语多义：`Modifier`/`State`/`Context`…） | 术语统一 | 影响新类型命名与文档措辞（本报告已按现有命名引用） | 不阻塞 |
| `RID57`–`RID59`、`RID61`–`RID63`（`RID §5.2`） | 空间连续性/突变/空洞/锚点/状态/优先级 | 不影响"求和"聚合语义；影响"某点属于哪些区域"的取值集合 | 不阻塞结构 |
| `B-5`/`B-6`/`B-7`/`B-8`/`B-9` | 图例语义 / 横向单元 / `Elevation` 双角色 / 旧栈映射 / Phase 集合 | 与本议题无直接依赖（`B-7` 需注意 `Elevation` 的"提供者 vs 转发者"措辞） | 不阻塞 |

---

## 附录 A：审查覆盖清单

| 文件 | SHA16 | 读取方式 |
|---|---|---|
| `Assets/Scripts/World/V2/WorldParameterState.cs` | `6E65E7C8F5DC2CA1` | 结构扫描 + 关键行（`:124`/`:126`/`:158`/`:175`） |
| `Assets/Scripts/World/V2/WorldParameterQuery.cs` | `4935EE144B3EF932` | 结构扫描 + 关键行（`:42`/`:44`） |
| `Assets/Scripts/World/V2/WorldParameterRule.cs` | `73F45DE2178C58BF` | 结构扫描（接口/身份表/组合类型） |
| `Assets/Scripts/World/V2/WorldParameterHash.cs` | `E9BF9D755F6A1813` | 结构扫描 + 调用点（`WorldParameterRules.cs:102`/`:182`/`:216`） |
| `Assets/Scripts/World/V2/WorldParameterRules.cs` | `EB345A4E03674FF7` | 结构扫描 + 逐规则关键行（8 条规则的 step / kind / 占位来源） |
| `Assets/Scripts/World/V2/WorldParameterEvaluator.cs` | `7B35DBB38A02E719` | 结构扫描 + 关键行（`:46`/`:69`/`:78`/`:99`/`:100`/`:106`/`:115`–`:124`/`:197`/`:201`） |
| `Assets/Scripts/World/V2/RegionIdentity.cs` | `398E66FFB4D809DC` | 字段清单核验 |
| `Assets/Scripts/World/V2/RegionDetermination.cs` | `081DCE7C2D6F1B87` | 结构扫描（占位判定入口） |
| `Assets/Scripts/World/V2/RegionContext.cs` | `67185B6213178DA1` | 字段清单核验（`:41`–`:45`） |
| `Assets/Scripts/World/V2/RegionPipeline.cs` | `BC68677D0443AE53` | 结构扫描 + 关键行（`:60`/`:62`/`:63`） |
| `Assets/Scripts/World/V2/RegionModifier.cs` | `14445D42F69F051A` | 字段清单核验（`:38`–`:43`） |
| `Assets/Scripts/World/V2/RegionModifierDetermination.cs` | `A0BA308631929D80` | 结构扫描 + 关键行（`:69`/`:90`–`:96`） |
| `Assets/Scripts/World/V2/WorldSeed.cs` | `954DD11740A86885` | 依赖矩阵 |
| `Assets/Scripts/World/V2/WorldCoordinate.cs` | `CD9ECDCB6722823B` | 依赖矩阵 |
| `Specifications/World/Region_Design.md` | `77FA71B2620E7396` | 章节定位 + 条款抽取（§2.1/§4.2/§9.2/§9.3） |
| `Specifications/World/WorldState_Design.md` | `D54CB1CC14859312` | 条款抽取（§1.3/§1.5/§2.1/§3.3 MF/§3.6 T/§4.4） |
| `Specifications/World/WorldState_Runtime_Contract.md` | `04B164617833A832` | 条款抽取（§2.1–§2.3、§3.1–§3.4、`WSC9`–`WSC22`、`WSC25`） |
| `Specifications/World/WorldParameter_Contract.md` | `532F9596F1348B57` | 权限矩阵 / 范围 / 域 / 附录 B |
| `Specifications/World/WorldGeneration_Pipeline_Contract.md` | `BEF7CFC1AF98AF3C` | Phase 2/3 与 `PC19`–`PC26` |
| `Specifications/World/Region_Architecture_Contract.md` | `90361CAB116697C1` | 条款抽取（§5/§9/§10/§11/§13/附录 B/C） |
| `Specifications/World/Region_Runtime_Data_Contract.md` | `CFF36BA11EBDD4B6` | 条款抽取（§2/§4/§5/§6/附录 A/B） |
| `Specifications/World/Region_Identity_Determination_Contract.md` | `31C3B7E24E46E5B4` | 条款抽取（§1–§8，含 `RID60` 与 §8.3） |
| `Specifications/World/WorldParameter_Source_Contract.md` | `05CA5D156C666624` | 条款抽取（§2/§3/§6/§8.3） |
| `Specifications/World/Region_Architecture_Boundary_Contract.md` | `529FACA57AACD092` | 条款抽取（§2/§3/§4/§8/§10.3） |
| `Specifications/Audit/WorldArchitecture_CrossDocument_Audit_V1.md` | `068664697B376128` | `A-*`/`B-*`/`CD-*` 登记项 |
| `Specifications/Architecture/Legacy_Status.md` | `9B8488B2D1335DC3` | Legacy 冻结基线 |

| # | 说明 |
|---|---|
| **A-1** | **未能读取的文件：无**（清单内 12 个 `.cs` 与全部契约文件均存在且已核验） |
| **A-2** | **读取方式说明**：代码以"`SHA16` 冻结 + 去注释结构扫描 + 关键行抽取"核验，**不是**全文逐字阅读；这些文件的 SHA16 与本会话各产出轮次的报告值**完全一致**，未发生漂移 |
| **A-3** | 契约以"章节定位 + 条款行抽取"核验；本报告引用的每条条款均给出条款号（如 `WSC9`/`RA62`/`RID60`）与所在文档 |
| **A-4** | 工作区另有 45 个 Editor `*Validation*.cs`；本轮**只统计数量与名称**，未逐份读取其断言内容（见 `N-4`） |

---

## 附录 B：与既有报告不一致之处（登记）

| # | 项 | 既有报告 | 本轮实测 | 处置 |
|---|---|---|---|---|
| **B-1** | Editor 验证入口分类 | "Legacy 34 + V2 11 = 45" | 总数 **45** 一致；按名称归类得 **12** 个 V2 名（多出 `TerrainVoxelMeshIntegrationValidation`） | 登记口径差异；**不影响**本审查结论 |
| **B-2** | `RegionModifier` 字段名 | 任务书（Phase 2.5）曾列 `Elevation/Ocean/Danger` | 实现为契约 6 分量（已由用户当轮确认） | 已登记 `[待确认]`，本轮无新增分歧 |
| **B-3** | 本报告新增的未决依赖 | — | `RID60` + `RA65`/`RA130`/`RO7` + `B-3` 被明确列为"接入依赖项" | 新增登记，**不裁决** |

---

## 附：本轮结论一句话

> **修正量应当接在"合成步骤"（WorldState 层的加法 + 钳制），而不是 Base 段；接线的最小形态是"把常量 `0f` 换成入参修正量"。真正的阻塞点不是合成逻辑（契约与代码都已就位），而是"该点到底有多少个区域、修正集合如何枚举"——这依赖未决项 `RID60` 与尚未实现的区域枚举入口。**
