# R37：SeaLevel / Ocean 断言设计审查（只读，不实施）

- 性质：**只读调查 + 测试设计 + 证据整理**。本轮**未修改**任何 `.cs` / `.meta` / `.md`（除本报告）/ 场景 / Prefab / 配置 / 验证器 / 进度记录；**未运行 Unity**；未执行 commit / push / reset / clean / stash / checkout；未执行 `git add`。
- 本轮唯一新增：`_phase2b_validation/round37_sealevel_ocean_assertion_design.md`（本报告）。
- **结论边界**：本报告是**设计审查**，不声称 SeaLevel/Ocean 已动态验证完成，不声称 World V2 生产端到端已完成。

---

## 1. 本轮范围与保护状态

| 项 | 值 |
|---|---|
| 分支 / HEAD | `main` / `37246478ed354a1e0ce7a3a3d9457caf188062`（未变） |
| staged | **0** |
| `git status --short`（修改前 = 修改后） | **25 条**（tracked `M` **18** + untracked 源文件 4 + 证据目录 3） |
| 本轮新增文件 | **仅** 1 份审查报告 |
| R36 改动是否仍在 | **在**：`GeologyRuntimeIntegrationValidation.cs` 哈希 `3F0D4478A9399D1E`（R36 交付值），`Check(` = 54 |
| 进度记录 | `sealevel_round7_progress.md` 仍 **1032 行**（未改） |
| 349 条冻结哈希 | **349 条 / 失配 0 / 缺失 0**（本轮实测） |
| `.gitignore` | 未修改 |
| 既有证据 | 未删除 / 未移动 / 未覆盖 |

---

## 2. 当前实现事实（全部来自本轮实际读取的源码）

### 2.1 SeaLevel 的来源、范围与传递路径

| 环节 | 位置 | 事实 |
|---|---|---|
| 常量 | `WorldGenerationSettings.cs` L29 | `public const int DefaultSeaLevel = 64;` |
| 字段 | 同 L49 | `public int SeaLevel;`（**无内联初始化器**） |
| 六参构造 | 同 L62–L69 | `: this(…, DefaultSeaLevel)` ⇒ 64 |
| 七参构造 | 同 L76–L89 | `SeaLevel = seaLevel;` 显式赋值 |
| 合法性 | 同 L132–L136 | `if (SeaLevel < 0 \|\| SeaLevel > WorldHeight)` ⇒ 非法，reason 含实际值 |
| 输出 | 同 L161 | `ToString()` 含 `SeaLevel=` |
| 生产传递 | `VoxelWorldStreamingBootstrap.cs` L67 / L194–L195 | Inspector 字段 `public int SeaLevel = 64;` → 作为**第 7 实参**传入 |
| 唯一生产构造点 | 同上 | `Assets/Scripts` 内 `new WorldGenerationSettings(` = **2 处**（Bootstrap + `Default` 属性）；`Default` 的**生产调用者 = 0** |

### 2.2 Ocean 的实际输入、判定与输出（`WorldParameterRules.cs` L106–L145）

```csharp
116: private sealed class OceanRule : IWorldParameterRule
119:     public int ParameterId { get { return WorldParameterIds.Ocean; } }          // = 8
124:     public int EvaluationStep { get { return 1; } }                             // 步骤 1（Elevation 之后）
129:     public WorldParameterCompositionKind CompositionKind { get { return WorldParameterCompositionKind.Derived; } }
134:     public float EvaluateBaseValue(IWorldParameterBaseReader evaluated, WorldSeed seed,
135:                                    WorldCoordinate position, WorldGenerationSettings settings)
136:     {
137:         float elevation;
138:         if (!evaluated.TryGetBaseValue(WorldParameterIds.Elevation, out elevation))
139:         {
140:             throw new System.InvalidOperationException(
141:                 "Ocean 规则只能在步骤 0（Elevation）之后求值（WSC §2.3）。");
142:         }
143:         return elevation < settings.SeaLevel ? 1f : 0f;
144:     }
```

| 维度 | 事实 |
|---|---|
| **输入 1** | `IWorldParameterBaseReader.TryGetBaseValue(WorldParameterIds.Elevation, out float)` —— 即**步骤 0 的 Elevation Base 值** |
| **输入 2** | `settings.SeaLevel`（`WorldGenerationSettings` 配置） |
| **比较** | **严格小于** `elevation < settings.SeaLevel` |
| **输出** | `float`：海洋 `1f` / 陆地 `0f`（**不是 bool**；表示法属登记缺口 `PM32`，见同文件 L113–L114 注释） |
| **非法 / 缺失输入** | Elevation 不可得 ⇒ **抛 `InvalidOperationException`**（L140–142）；SeaLevel 本身**不在此处校验**（依赖 `IsValid` 前置） |
| **额外隐式条件 / 默认值** | **无**：无钳制、无插值、无域哈希、无二次分支（`Ocean` 只依赖 Elevation + SeaLevel） |
| 合成类型 | `Derived` ⇒ `Compose` 直接返回 Base，**不进入任何修正求和式**（`WSC10`） |

### 2.3 Elevation 的计算路径（与 SeaLevel 独立）

```csharp
98:  public float EvaluateBaseValue(IWorldParameterBaseReader evaluated, WorldSeed seed,
99:                                 WorldCoordinate position, WorldGenerationSettings settings)
102:     return HeightFunction.GetTerrainHeight(position.X, position.Z, settings);
```

- `ElevationRule`（L81–L104）**只读转发** `HeightFunction.GetTerrainHeight(x, z, settings)`，**完全不读取 `SeaLevel`** ⇒ 结构上保证"SeaLevel 不改变 Elevation"。
- 输出量纲：整数体素 y（`float` 承载），范围 `[0, WorldHeight-1]`。

### 2.4 第二套 Ocean 派生：`WorldSkeletonFacts.TryEvaluate`（L85–L119）

```csharp
 94: public static bool TryEvaluate(
 95:     WorldCoordinate position,
 96:     WorldGenerationSettings settings,
 97:     int seaLevel,                       // ← 独立的 seaLevel 参数，不读 settings.SeaLevel
 98:     out WorldSkeletonFacts facts)
 99: {
101:     if (position.Y < 0 || position.Y >= settings.WorldHeight)
102:     { facts = default; return false; }   // 唯一失败条件：垂直坐标越界
108:     int elevation = HeightFunction.GetTerrainHeight(position.X, position.Z, settings);
111:     bool ocean = elevation < seaLevel;                                 // 同样严格小于
114:     int continent = ocean ? 0 : 1;                                     // 海洋 0 / 陆地 1
117:     facts = new WorldSkeletonFacts(elevation, ocean, continent);
118:     return true;
119: }
```

`WorldSkeletonFacts`（readonly struct）：`IsValid` / `Elevation`(int) / `Ocean`(bool) / `Continent`(int)；`default(…).IsValid == false`；有效实例唯一构造点为 L117。

**关键事实（本轮实测）**：

| 事实 | 证据 |
|---|---|
| `WorldSkeletonFacts` 在 `Assets` 全库（含 Editor）**0 个调用者** | `Select-String 'WorldSkeletonFacts\s*\.'` = **0**；`TryEvaluate(` 仅在自身文件出现 |
| **无任何验证器测试它** | `Assets/Editor` 递归搜索 `WorldSkeletonFacts` = **0** |
| 其 `seaLevel` 是**独立入参**，不取自 `settings.SeaLevel` | L94–L98 |
| 不校验 settings 合法性 / seaLevel 范围（调用方前置条件） | L91–L92 注释 + 代码（仅校验 Y） |

### 2.5 生产调用点 vs Editor 验证器调用点

| 类别 | 调用点 | 证据 |
|---|---|---|
| **生产（`Assets/Scripts`）** | `VoxelWorldStreamingBootstrap.Build()` → `GeologySampler`/`GeologyWorldGenerator`；`VoxelWorldStreamingBootstrap` 是唯一持有 `WorldGenerationSettings` 者 | R34 实测：构造点 2 处（其一为 `Default` 属性） |
| **World V2 参数层（`WorldParameterEvaluator` / `WorldStatePipeline` / `WorldParameterQuery`）** | **生产调用者 = 0** | R26/R34 实测（递归 235 个 `.cs`） |
| **Editor 验证器** | `WorldV2ParameterPipelineValidation`（`Evaluate` / `Finalize` / `EvaluateBase` / `EvaluateFinal`，17 处引用）、`GeologyRuntimeIntegrationValidation`（`BootstrapRig` 内 6 参构造） | 本轮实测 |

> 因此：**Ocean 的 float 判定只在 Editor 验证器内被消费**；真实世界生成路径（VoxelWorld / Geology）**不读取 Ocean**。这与"World V2 参数层未接入生成运行时"的既有结论一致。

---

## 3. 现有断言盘点（逐条，不以总数代替覆盖说明）

### 3.1 `Assets/Editor/WorldV2ParameterPipelineValidation.cs` —— SeaLevel 专项（共 17 条）

| # | 行号 | 断言内容 | 输入 | 覆盖点 |
|---:|---:|---|---|---|
| A-1 | 154 | 前置：`WorldHeight=64` 且 settings 合法 | 六参构造 | 前置条件 |
| A-2 | 155 | `SeaLevel = 0` **合法** | 七参 0 | 下界合法 |
| A-3 | 156 | `SeaLevel = WorldHeight` **合法**（闭区间上界） | 七参 64 | 上界合法 |
| A-4 | 157 | `SeaLevel = -1` **非法** | 七参 −1 | 越界拒绝 |
| A-5 | 158 | `SeaLevel = WorldHeight+1 = 65` **非法** | 七参 65 | 越界拒绝 |
| B1-0 | 170 | 取得内部 Elevation 样本（扫描 128×128，条件 `0 < h < WorldHeight-1`） | `HeightFunction` | 样本有效性 |
| B1-1 | 181 | `Elevation = SeaLevel-1 ⇒ Ocean = 1f` | S = `midElevation+1` | **严格边界（下）** |
| B1-2 | 185 | `Elevation = SeaLevel ⇒ Ocean = 0f`（严格小于） | S = `midElevation` | **相等边界 ✅** |
| B1-3 | 189 | `Elevation = SeaLevel+1 ⇒ Ocean = 0f` | S = `midElevation-1` | **严格边界（上）** |
| B2 | 211 | `SeaLevel = 0` ⇒ 全部 35 列非海洋 | 35 列网格（x∈{0,7,14,21,28} × z∈{0,5,…,30}） | 极端配置 |
| B3 | 212 | `SeaLevel = WorldHeight` ⇒ 全部 35 列为海洋 | 同上 | 极端配置 |
| C-1 | 215 | 六参构造 ⇒ `SeaLevel == DefaultSeaLevel == 64` | 六参 | 兼容默认 |
| C-2 | 217 | 七参构造保留显式 `SeaLevel = 17` | 七参 17 | **非默认值** |
| C-3 | 234 | 旧配置 Ocean 与 `h < 64` 逐点一致（35 点，mismatch=0） | `slUnset` | 迁移等价（`64` 在**测试**中作为历史基线） |
| C-4 | 235 | **改变 SeaLevel 不改变 `HeightFunction` 高度**（35 点，`slZero` vs `slUnset`） | S=0 vs S=64 | **独立性 ✅（部分）** |
| C-5 | 241 | 同配置 + 同 Seed + 同坐标 ⇒ 逐字段确定 | `slExplicit`(**17**) | 确定性（非默认值） |
| D-1 | 252 | 【静态】Bootstrap 暴露 `public int SeaLevel = 64;` 且以七参传入 | 读源文件文本 | 生产接线（**非运行时**） |

### 3.2 `Assets/Editor/WorldV2ParameterPipelineValidation.cs` —— Ocean 相关但非 SeaLevel 专项（4 条）

| 行号 | 断言 | 覆盖点 |
|---:|---|---|
| 55 | Base 段所有 `Modifier == 0`（`WSC14`） | Modifier 零值显式性 |
| 107 | 参数身份 → `RegionModifier` 分量映射正确（**Elevation·Ocean 恒 0**） | `WSC10`、`RA69` |
| 108 | `Final = clamp01(Base+Modifier)`；**Elevation·Ocean 的 Final == Base** | `WSC10` |
| 143 | `Pipeline.EvaluateBase` 与 `Evaluator.Evaluate` 的 Base 段逐值一致 | 无第二套求值路径 |

### 3.3 `Assets/Editor/GeologyRuntimeIntegrationValidation.cs`

| 项 | 事实 |
|---|---|
| `Check(` 总数 | **54** |
| `Check(...SeaLevel...)` | **0** |
| `Ocean` 出现次数 | **0** |
| `BootstrapRig.GenerationSettings` 用途 | 仅构造 `BasicTerrainGenerator` 基线（L302），而该类**不读 `SeaLevel`** |
| R36 对齐后 | rig 已显式 `Bootstrap.SeaLevel = 64;`（L943）+ 七参构造（L953）；**但仍无断言验证 rig 与 Bootstrap 的一致性** |

### 3.4 其他验证器

`Assets/Editor` 递归搜索 `Ocean` / `SeaLevel` / `WorldSkeletonFacts`：**除上述两个文件外无相关断言**（`ChunkStreamingValidation`/`StageD` 中的 `SeaLevel` 仅为静态文本断言，已在 R34 记录为 E 类）。

### 3.5 覆盖结论（明确区分"已覆盖 / 未覆盖"）

| 覆盖项 | 状态 | 依据 |
|---|---|---|
| Ocean 严格边界 S−1 / S / S+1 | **已覆盖**（evaluator 级，非默认 S） | B1-1..B1-3 |
| 相等边界 `Elevation == SeaLevel` | **已覆盖** | B1-2 |
| 非默认 SeaLevel（≠64） | **已覆盖**（S=`midElevation`、显式 17） | B1 组、C-2、C-5 |
| SeaLevel = 0 / = WorldHeight 极端 | **已覆盖**（35 列扫描） | B2/B3 |
| SeaLevel 越界拒绝 | **已覆盖**（−1、65） | A-4、A-5 |
| Elevation 与 SeaLevel 独立 | **已覆盖（部分）**：仅比较 S=0 vs S=64 的 `HeightFunction` 输出 | C-4 |
| Ocean 不进入修正求和（Modifier/Final） | **已覆盖** | L107、L108 |
| **`OceanRule` 缺 Elevation 的异常路径** | **未覆盖且不可测** | 规则类为 `private sealed`，验证器无法实例化；经 evaluator 调用时 Elevation 必先求值 ⇒ 实际不可达 |
| **`WorldSkeletonFacts.TryEvaluate` 全部语义** | **完全未覆盖**（0 断言、0 调用者） | 本轮实测 |
| **GRI 的 rig↔Bootstrap 配置一致性断言** | **未覆盖**（R36 只做了赋值对齐，无断言） | §3.3 |
| **真实世界 Ocean 分布（生产端到端）** | **无法验证**（参数层无生产消费者） | §2.5 |

---

## 4. 测试矩阵（具体输入 / 预期 / 层级 / 接口）

### 4.1 A. Ocean 边界语义（evaluator 级）

接口：`WorldParameterEvaluator.Evaluate(seed, settings, position)` → `WorldParameterState.TryGetValue(WorldParameterIds.Ocean, out WorldParameterValue v)` → 读 `v.BaseValue`（**float**，非 bool）。

固定合法基准：`seed = new WorldSeed(12345)`；`settingsBase = new WorldGenerationSettings(12345, 24, 3, 64, 0.06f, 6f)`（`WorldHeight = 64`）；内部样本由 `HeightFunction` 扫描取得（R9 实测：`midElevation = 23` @ `(0,0)`；**实现时应运行时取得，不硬编码**）。设样本列 `h0`，取 `S = h0`：

| 用例 | SeaLevel | 该列 h | 预期 `Ocean.BaseValue` | 已覆盖？ |
|---|---:|---:|---|---|
| 低于海平面 | `h0 + 1` | `h0` | **`1f`** | ✅ L181 |
| **等于海平面** | `h0` | `h0` | **`0f`** | ✅ L185 |
| 高于海平面 | `h0 - 1` | `h0` | **`0f`** | ✅ L189 |

### 4.2 B. 非默认 SeaLevel（具体数值与计算）

**工作示例（以 R9 实测样本 `h0 = 23 @ (0,0)` 为例）**：同一坐标 `(0, 0, 0)`、同一 `seed`，仅改 `SeaLevel`：

| SeaLevel | 比较式 | 结果 |
|---:|---|---|
| **24** | `23 < 24` → true | `Ocean = 1f`（海洋） |
| **23** | `23 < 23` → false | `Ocean = 0f`（陆地） |
| **22** | `23 < 22` → false | `Ocean = 0f`（陆地） |

⇒ **同一 Elevation（23）、不同 SeaLevel（24 vs 23）⇒ Ocean 判定翻转**，证明 SeaLevel 确实参与判定且严格小于语义正确。
**已覆盖**（B1 组即该设计）。建议**追加**：显式使用 `S = 32`（`32 ∈ [0,64]` 合法、≠64）对**多个列**做逐点比对 `Ocean == (h < 32 ? 1f : 0f)`，覆盖"非默认 S 的逐列一致性"，而不仅是单列边界。

### 4.3 C. SeaLevel 与 Elevation 的独立性

| 项 | 设计 |
|---|---|
| 调用接口 | ① `HeightFunction.GetTerrainHeight(x, z, settings)`（最低层）② `WorldParameterEvaluator.Evaluate(...)` 读 `Elevation.BaseValue`（参数层）③ `WorldSkeletonFacts.TryEvaluate(...)` 读 `facts.Elevation`（查询层） |
| 输入 | 同 `seed`、同坐标集合（如 `x,z ∈ {-16,-11,…,16} × {-16,-9,…,16}`，35 点，沿用 C-3/C-4 网格）；`settings` 仅 `SeaLevel` 不同：`{0, 17, 23, 32, 64}`，其余 6 字段完全相同 |
| 比较 | 对每个固定点：`HeightFunction` 输出、`Elevation.BaseValue`、`facts.Elevation` 三者**在同一点、不同 SeaLevel 下必须完全相等** |
| 如何排除"同时改变了其他变量" | 其他 6 个字段（Seed/TerrainHeight/SoilDepth/WorldHeight/TerrainFrequency/TerrainAmplitude）与 `seed`、坐标显式保持同一常量；断言时以 `SeaLevel=64` 的基线为参照逐点比较 |
| 现状 | **部分已覆盖**（C-4 仅 0 vs 64 且仅查 `HeightFunction`）⇒ 建议扩展到 5 个 SeaLevel × 3 层接口 |

**限制（据实）**：`WorldSkeletonFacts.TryEvaluate` 的 `seaLevel` 是**入参**，因此"改变 `settings.SeaLevel` 但传同一 `seaLevel` 入参"可精确隔离两者——这既便于测试独立性，也暴露了**API 与配置不一致的可能**（见 §5）。

### 4.4 D. 合法范围与非法输入

| 用例 | 输入 | 预期 | 层级 | 已覆盖 |
|---|---|---|---|---|
| `SeaLevel = 0` | 七参 0 | `IsValid == true`；全列 `Ocean == 0f` | 配置级 + evaluator | ✅ A-2、B2 |
| `SeaLevel = WorldHeight` | 七参 64（`WorldHeight=64`） | `IsValid == true`；全列 `Ocean == 1f` | 同上 | ✅ A-3、B3 |
| `SeaLevel < 0` | −1 | `IsValid == false`，reason 含实际值 | 配置级 | ✅ A-4 |
| `SeaLevel > WorldHeight` | 65 | `IsValid == false` | 配置级 | ✅ A-5 |
| `Elevation == SeaLevel` | S = `h0` | `Ocean == 0f` | evaluator | ✅ B1-2 |
| `Elevation == SeaLevel`（Skeleton 层） | `seaLevel = h0` | `facts.Ocean == false && facts.Continent == 1` | 查询级 | ❌ 建议新增 |

> **特别说明（遵约束）**：`WorldHeight = 64` 且 `SeaLevel = 64` 时，合法高度域 `[0,63]` **全部满足 `h < 64`** ⇒ 全列皆海洋。这是**当前配置下的正确结果**，既有 B3 已按此断言；**不得**据此判定实现错误，也**不得**擅自更改默认值或范围。

### 4.5 E. WorldSkeleton 语义（三个层级必须区分）

| 层级 | 可用接口 | 是否可测 | 说明 |
|---|---|---|---|
| **① 直接规则级** | `OceanRule`（`private sealed`） | **不可测** | 规则类是私有嵌套类，验证器无法实例化；且 `EvaluateBaseValue` 需自造 `IWorldParameterBaseReader` |
| **② 查询级（推荐）** | `WorldSkeletonFacts.TryEvaluate(position, settings, seaLevel, out facts)` —— **public static** | **可测（当前 0 覆盖）** | 纯函数、无副作用 |
| **③ 生产端到端** | 真实世界生成 → 观察体素/海洋 | **当前不可行** | 参数层无生产消费者（§2.5） |

**查询级建议断言（具体输入 / 预期）**，基准：`settings64 = new WorldGenerationSettings(12345, 24, 3, 64, 0.06f, 6f)`；样本列 `(x0,z0)` 的 `HeightFunction` 高度 `h0`（运行时取得，R9 实测 23）：

| # | 调用 | 预期 |
|---:|---|---|
| 1 | `TryEvaluate(new WorldCoordinate(x0, 0, z0), settings64, h0 + 1, out f)` | `true`；`f.IsValid`；`f.Elevation == h0`；**`f.Ocean == true`**；**`f.Continent == 0`** |
| 2 | `TryEvaluate(…, settings64, h0, out f)` | `true`；**`f.Ocean == false`**；**`f.Continent == 1`**（相等边界，海洋=0/陆地=1） |
| 3 | `TryEvaluate(…, settings64, h0 - 1, out f)` | `true`；`f.Ocean == false`；`f.Continent == 1` |
| 4 | `TryEvaluate(new WorldCoordinate(x0, -1, z0), settings64, h0, out f)` | **`false`**；`f.IsValid == false`（`D1` 垂直越界：不钳制、不修正） |
| 5 | `TryEvaluate(new WorldCoordinate(x0, settings64.WorldHeight, z0), settings64, h0, out f)` | **`false`**（上界越界） |
| 6 | `default(WorldSkeletonFacts).IsValid` | **`false`** |
| 7 | 同一列、`seaLevel = 64` vs `seaLevel = h0` | `Ocean` 由 `true` 变 `false` ⇒ 证明 `seaLevel` 入参生效 |
| 8 | `TryEvaluate` 的 `f.Elevation` vs `HeightFunction.GetTerrainHeight(x0,z0,settings64)` | **相等**（Elevation 只读转发、与 Y 无关） |

**语义约束对照（§四）**：陆地 `Continent = 1` ✓（L114）、海洋 `Continent = 0` ✓（L114）、越界明确失败 ✓（L101–L105）、`Elevation/Ocean` 职责独立 ✓（L108 vs L111）。**当前代码与既定约束无冲突**（本轮未发现冲突项）。

---

## 5. 缺口与风险

| # | 缺口 / 风险 | 类型 | 说明 |
|---|---|---|---|
| G-1 | `WorldSkeletonFacts.TryEvaluate` **完全无测试且无任何调用者** | 覆盖缺口（可直接补） | 语义已写在代码注释中但**从未被验证**；#1–#8 断言全部可加在现有验证器内 |
| G-2 | `WorldSkeletonFacts` 的 `seaLevel` 是**独立入参**，不读 `settings.SeaLevel` | **接口契约风险（需先定契约）** | 现状允许 `seaLevel != settings.SeaLevel` 而不被发现；是否应改为从 `settings` 读取、或增加一致性前置校验，**属契约决策，本轮不裁决** |
| G-3 | `OceanRule` 的"Elevation 缺失 ⇒ 抛异常"路径**不可测** | 结构性限制 | 规则类 `private sealed` + evaluator 保证顺序 ⇒ 实际不可达；若确需覆盖，必须先确定是否暴露规则类（**不得擅自修改**） |
| G-4 | GRI 中**无** rig↔Bootstrap 配置一致性断言 | 可补（低成本） | R36 已把 rig 对齐（`Bootstrap.SeaLevel=64` + 七参），但**没有任何断言验证这一点**，未来可能被静默破坏 |
| G-5 | 非默认 SeaLevel 的**逐列一致性**未覆盖 | 可补 | 现有 B1 仅单列边界 + B2/B3 极端配置；建议补 `S=32` 的逐列 `Ocean == (h<32)` |
| G-6 | 独立性与 SeaLevel 的关系只在 `HeightFunction` 层覆盖（0 vs 64） | 可补 | 建议扩展到参数层 `Elevation.BaseValue` 与查询层 `facts.Elevation`，并覆盖 5 个 SeaLevel |
| G-7 | 真实世界 Ocean 分布 | **必须等生产接入** | 参数层无生产消费者；即便补断言也只是规则级，不等于世界输出正确 |
| G-8 | Ocean 的 float 表示法（`PM32` 登记缺口） | 契约未决 | 本轮测试只能断言"恰为 0f/1f"，**不能**断言"表示法已定" |

> **重要边界**：以上均为**测试/契约缺口**，**不等于**生产逻辑错误。现有代码在既定语义下自洽（严格小于、Derived、不进求和）。

---

## 6. 建议的最小实施方案（**本轮不实施**）

### 6.1 推荐位置与新增断言

**验证器 1：`Assets/Editor/WorldV2ParameterPipelineValidation.cs`**（SeaLevel 专项的既有归属；R30 已提供 `RunBatch()`）

- 插入位置：**D 组之后、`Report("World V2 参数两段式发布")` 之前**（现 L252 与 L254 之间）
- 新增分组：**「E. WorldSkeleton 查询级语义（§4.5 #1–#8）」+「F. 非默认 SeaLevel 逐列一致性（§4.2 S=32）」+「G. 独立性扩展（§4.3）」**
- 计划新增断言数：**约 12 条**
  - E 组：8（含相等边界、越界失败 ×2、`default.IsValid`、`seaLevel` 入参生效、`facts.Elevation` 与 `HeightFunction` 一致）
  - F 组：2（`S=32` 逐列 `Ocean == (h<32)` 一致；`S=32` 下"同时存在海洋列与陆地列"非平凡性）
  - G 组：2（`Elevation.BaseValue` 在 5 个 SeaLevel 下逐点相等；`facts.Elevation` 与 `settings.SeaLevel` 无关）
- 复用现有样本发现机制（`HeightFunction` 扫描取 `0 < h < WorldHeight-1`），不硬编码 23

**验证器 2：`Assets/Editor/GeologyRuntimeIntegrationValidation.cs`**（R36 对齐的验证缺口）

- 插入位置：`ValidateBootstrapUsesGeologyGenerator`（GE1）内，紧接现有 `Bootstrap.Generator` 断言之后（现 L112 之后）
- 新增断言：**2 条**
  - `rig.GenerationSettings.SeaLevel == bootstrap.SeaLevel`（rig 参考设置与 Bootstrap 一致）
  - `bootstrap.SeaLevel == WorldGenerationSettings.DefaultSeaLevel`（rig 依赖的默认值显式成立）
- 理由：直接验证 R36 的对齐成果，防止未来回归

### 6.2 计划运行的验证器与预期证据

| 验证器 | 入口 | 当前基线（真实运行） | 预期（含新增断言） |
|---|---|---|---|
| `WorldV2ParameterPipelineValidation` | `RunBatch`（R30 新增） | R9 记录：PASS 29 / FAIL 0 | PASS **≈41** / FAIL 0（+12）；以实际运行为准 |
| `GeologyRuntimeIntegrationValidation` | `RunBatch` | R36 实测：`Checks = 45 / Failures = 0` | PASS **47** / Failures 0（+2） |

证据要求：每轮保存独立 `raw/rerun` 日志 + stdout/stderr + 退出码 + SUMMARY，并由人工读取日志确认（不得仅凭退出码）。

### 6.3 哈希影响预估

| 对象 | 是否被冻结表登记 | 影响 |
|---|---|---|
| `WorldV2ParameterPipelineValidation.cs` | **0 处**（R30 实测；实施前须再次确认） | 无级联 |
| `GeologyRuntimeIntegrationValidation.cs` | **0 处**（R35/R36 实测） | 无级联 |

⇒ 预计**无需重钉**，349 条应保持 0 失配；但**实施前必须再次实测**确认登记数仍为 0。

### 6.4 实施顺序建议（需批准）

1. 先在 `WorldV2ParameterPipelineValidation` 加 E/F/G 组（12 条）→ 跑 `RunBatch` → 保留日志。
2. 再在 GRI 加 2 条 rig 一致性断言 → 跑 `RunBatch` → 保留日志。
3. 两轮各自独立审查 diff（断言只增不改），确认 `Check(` 数量变化与预期一致。
4. **G-2（`seaLevel` 入参 vs `settings.SeaLevel` 契约）与 G-3（规则类异常路径）需先由所有者裁决契约**，不在本轮或下一轮自动处理。

---

## 7. 六、验证失败语义（**静态审查结论**，未人为制造失败）

| 入口 | 失败时行为 | 是否能被外部检测到 |
|---|---|---|
| `WorldV2ParameterPipelineValidation.Validate()`（L33） | 逐条 `Debug.LogError("[WorldV2-TwoPhase] FAIL: …")` + `failures++`；末尾 `Report()` 再 `LogError` 汇总；**不抛异常、不调用 `Exit`** | **不能**：若以 `-executeMethod …Validate` 作为批处理入口，Unity 默认以 **0** 退出 ⇒ **存在"只打印错误但退出 0"的路径**（当前文档化批处理入口是 `RunBatch`，故未被使用） |
| `WorldV2ParameterPipelineValidation.ValidateOrThrow()`（L258） | 调 `Validate()` 后若 `failures > 0` **抛 `InvalidOperationException`** | **能**（依赖 Unity 对未捕获异常的退出码；**该路径本会话从未实际触发**，属静态结论） |
| `WorldV2ParameterPipelineValidation.RunBatch()`（L285） | `try { ValidateOrThrow(); }` → `catch (Exception)` ⇒ `LogError` 一行 + `EditorApplication.Exit(1)`；成功 ⇒ `Exit(0)` | **能，且确定**（R30 实现，成功路径已实跑 exit 0） |
| `GeologyRuntimeIntegrationValidation.RunBatch()`（L58/L68） | `EditorApplication.Exit(failures == 0 ? 0 : 1)` | **能，且确定**（R36 实跑 exit 0） |

**结论（静态）**：在**文档化的批处理入口**（两个 `RunBatch`）下，失败必然映射为退出码 1；唯一"只记录不退出"的路径是 `WorldV2ParameterPipelineValidation.Validate()` 被直接当作 `-executeMethod` 目标时。**未做动态失败验证**（本轮不需）。

---

## 8. 结论边界

1. 本轮为**只读设计审查**：未修改任何生产代码 / 验证器 / 规范 / 进度记录；仅新增本报告。
2. 现有 SeaLevel/Ocean 覆盖**比预期更充分**：严格边界（含**相等边界**）、非默认 SeaLevel（`midElevation` / 17）、极端配置（0 与 WorldHeight）、越界拒绝、`HeightFunction` 独立性、`WSC10` 复合语义**均已有断言**（17 + 4 条）。
3. **主要空缺**：`WorldSkeletonFacts.TryEvaluate` **完全未测且无调用者**（G-1）、GRI **无 rig↔Bootstrap 一致性断言**（G-4）、非默认 SeaLevel 的逐列一致性（G-5）、独立性仅覆盖 0 vs 64（G-6）。
4. **不可测/需契约裁决**：`OceanRule` 异常路径（G-3）、`seaLevel` 独立入参的契约（G-2）、Ocean 的 float 表示法（G-8）。
5. **生产端到端仍不可验证**（G-7）：World V2 参数层无生产消费者，Ocean 只在 Editor 验证器内被消费。
6. 本轮**不声称** SeaLevel/Ocean 已动态验证完成，**不声称** World V2 生产端到端已完成。

---

## 9. 本轮自检

| 检查 | 结果 |
|---|---|
| 生产文件内容是否改变 | **未改变**（`git status --short` 仍 25 条 / tracked `M` 18，与本轮开始时相同） |
| R36 改动是否仍在 | **在**（GRI `3F0D4478A9399D1E`，`Check(` = 54） |
| 进度文件 | 仍 **1032 行**，未改 |
| HEAD / staged | `37246478ed354a1e9a0ce7a3a3d9457caf188062` / **0** |
| `.gitignore` | 未修改 |
| 既有证据 | 未删除 / 移动 / 覆盖 |
| 本轮新增 | **仅**本审查报告 |
| 349 条冻结哈希 | **349 / 0 / 0** |
