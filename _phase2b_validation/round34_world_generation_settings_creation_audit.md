# R34：`WorldGenerationSettings` 全量创建与默认值风险审计（只读）

- 性质：**只读静态审计**。未修改任何 `.cs` / 场景 / Prefab / 配置资产 / 文档 / 验证器 / 哈希登记；未改 `sealevel_round7_progress.md`。
- 未执行 commit / push / reset / clean。
- 现场：`main`，HEAD `37246478ed354a1e9a0ce7a3a3d9457caf188062`，staged = **0**，`git status --short` = 25 条。
- 349 条冻结哈希复核：**349 / 失配 0 / 缺失 0**。

---

## 1. 搜索命令与扫描范围

### 1.1 实际使用的检索方式（递归、非通配符假设）

```powershell
$src = Get-ChildItem -LiteralPath "<proj>\Assets"    -Recurse -Filter *.cs -File
$src+= Get-ChildItem -LiteralPath "<proj>\Packages"  -Recurse -Filter *.cs -File
$src | Select-String -Pattern '<各模式>' -Encoding UTF8
```

> 说明：**不使用** `Assets\**\*.cs` 作为路径通配（PowerShell 5.1 下 `**` 非递归，仅匹配一层；该缺陷已在 R27 报告中更正）。全部检索均以 `Get-ChildItem -Recurse` 得到的文件集合为准。

### 1.2 纳入范围

| 根目录 | `.cs` 文件数 | 是否纳入 | 理由 |
|---|---:|---|---|
| `Assets/` | **235** | **纳入** | 项目源码（`Assembly-CSharp` + `Assembly-CSharp-Editor`） |
| `Packages/` | **0** | 纳入但为空 | 仅 `manifest.json` / `packages-lock.json`，无项目自有 `.cs` |

### 1.3 排除项（明确声明，避免把有限扫描说成全仓覆盖）

| 目录 | `.cs` 数 | 排除理由 | 抽样结论 |
|---|---:|---|---|
| `_phase2b_validation/` | 544 | **历史证据目录**，内含一份**完整工程副本**（其 `Assets` 217 个 `.cs`），**不参与当前编译** | 副本内 `new WorldGenerationSettings(` = 3、`default(…)` = 0、`new …()` = 0、序列化 API = 0 ⇒ 结构与本轮结论一致，但**不作为本轮证据** |
| `Library/` | 327 | Unity 构建缓存 + `PackageCache`（第三方包），不属项目源码 | 其中提及 `WorldGenerationSettings` 的 = **0** |
| `ProjectSettings/` | 0 | 无 `.cs` | — |
| `.git/`、`.vscode/`、`Logs/`、`UserSettings/`、`Specifications/`、`_b3b_diff/`、`_finalization_backup/` | — | 非 C# 源码（规范/配置/证据） | — |

**范围声明**：本轮结论适用于 `Assets/`（235 个 `.cs`）+ `Packages/`（空）。**未**纳入 `_phase2b_validation` 内的工程副本与 `Library` 第三方代码。

---

## 2. 八类模式的命中统计（范围：Assets + Packages）

| # | 模式 | 命中数 |
|---|---|---:|
| 1 | `new WorldGenerationSettings(` | **13** |
| 2a | `default(WorldGenerationSettings)` | **0** |
| 2b | `new WorldGenerationSettings()`（无参/对象初始化式） | **0** |
| 3 | 七参数构造器调用（含多行） | **10**（Bootstrap 1 + 验证器 9） |
| 4 | 六参数构造器调用 | **3**（`Default` 属性 1 + GRI 1 + 验证器 1… 详见 §3） |
| 5 | 序列化 API（`JsonUtility` / `FromJson*` / `BinaryFormatter` / `XmlSerializer` / `DataContractSerializer`） | **0** |
| 6 | `WorldGenerationSettings` 类型出现 | **137** |
| 7 | `SeaLevel` 出现 | **54**（9 个文件） |
| 8 | `.IsValid(` 调用 | 48（其中作用于 `WorldGenerationSettings` 的仅 **2**：Bootstrap L197、WorldGenerationCoreValidation L78；其余属 `RuntimeCoreSettings` / `ChunkStreamingSettings` / `ChunkViewSettings` / `GeologySettings`） |

---

## 3. 逐调用点清单与分类

分类口径：**A** 显式构造（SeaLevel 来源明确）；**B** 六参构造（依赖 `DefaultSeaLevel`）；**C** 默认结构体/缺字段反序列化（可能为 0）；**D** 仅传递或读取；**E** 测试/验证器代码；**U** 无法判断。

### 3.1 构造调用点（13 处）

| # | 文件 | 行 | 实参形态 | **SeaLevel 实参真实来源** | 分类 | 理由（调用链证据） |
|---|---:|---|---|---|---|---|
| 1 | `Assets/Scripts/Game/VoxelWorldStreamingBootstrap.cs` | L194–195 | **7 参**（多行） | `SeaLevel` = 同文件 **Inspector 序列化字段** `public int SeaLevel = 64;`（L67） | **A** | **唯一生产构造点**：`Bootstrap.Build()` 内创建 → `IsValid` 校验（L197）→ `new GeologySampler(generationSettings, …)`（L291）→ `new GeologyWorldGenerator(generationSettings, …)`（L296–297）⇒ 直达世界生成路径 |
| 2 | `Assets/Scripts/VoxelWorld/Generation/WorldGenerationSettings.cs` | L99 | **6 参**（命名实参） | 委托 `DefaultSeaLevel = 64` | **B** | 位于 `public static WorldGenerationSettings Default` 属性 getter；**生产代码 0 调用者**（`Assets/Scripts` 内 `.Default` 命中仅 1 处**注释**）；实际调用者全部为 Editor 验证器（51 处，见 §3.3） |
| 3 | `Assets/Editor/WorldV2ParameterPipelineValidation.cs` | L40 | 6 参 | `DefaultSeaLevel`（64） | **E** | 验证器基准配置 |
| 4 | 同上 | L148 | 6 参 | `DefaultSeaLevel`（64） | **E** | 「未设置 SeaLevel」对照组 `slUnset` |
| 5 | 同上 | L149 | 7 参，`0` | 字面量 0 | **E** | 合法下界用例 `slZero` |
| 6 | 同上 | L150 | 7 参，`64` | 字面量 64 | **E** | 等于 `WorldHeight` 用例 `slTop` |
| 7 | 同上 | L151 | 7 参，`-1` | 字面量 −1 | **E** | **故意越界**反例 `slNeg`（期望 `IsValid=false`） |
| 8 | 同上 | L152 | 7 参，`65` | 字面量 65 | **E** | **故意越界**反例 `slOver` |
| 9 | 同上 | L174 | 7 参，`midElevation + 1` | 由被测 `Elevation` 推导 | **E** | 边界 B1 用例 |
| 10 | 同上 | L175 | 7 参，`midElevation` | 由被测 `Elevation` 推导 | **E** | 边界 B2 用例 |
| 11 | 同上 | L176 | 7 参，`midElevation - 1` | 由被测 `Elevation` 推导 | **E** | 边界 B3 用例 |
| 12 | 同上 | L216 | 7 参，`17` | 字面量 17 | **E** | 确定性对照组 |
| 13 | `Assets/Editor/GeologyRuntimeIntegrationValidation.cs` | L950–952 | **6 参**（多行） | 取 `Bootstrap.Seed / TerrainHeight / SoilDepth / WorldHeight / TerrainFrequency / TerrainAmplitude`，**未传 `Bootstrap.SeaLevel`** | **E** | `BootstrapRig` 测试装置内的**参考副本**；因走六参 ⇒ 恒为 64。**注意**：若某测试把 `Bootstrap.SeaLevel` 改为非 64，该 rig 的 `GenerationSettings` 与实际 `Bootstrap.Build()` 内部构造的设置会**不一致**（见 §5 R-2） |

**分类小计（构造点 13）**：A = **1**、B = **1**、E = **11**、C = **0**、D = **0**、U = **0**。

### 3.2 类型承载位置（字段 / 属性 / 返回值）

| 位置 | 行 | 形态 | 是否 Unity 序列化承载 | 分类 |
|---|---|---|---|---|
| `BasicTerrainGenerator.cs` | L27 | `private readonly WorldGenerationSettings settings;` | **否**（普通 C# 类，非 Unity 对象） | **D**（仅持有/读取） |
| `GeologySampler.cs` | L43 | `private readonly WorldGenerationSettings stage1Settings;` | **否** | **D** |
| `GeologyWorldGenerator.cs` | L38 | `private readonly WorldGenerationSettings stage1Settings;` | **否** | **D** |
| `GeologyRuntimeIntegrationValidation.cs` | L918 | `public readonly WorldGenerationSettings GenerationSettings;`（`BootstrapRig` 内） | **否**（非 MonoBehaviour/ScriptableObject，且 `readonly`） | **E** |
| `IWorldGenerator.cs` | L20 | `WorldGenerationSettings Settings { get; }` | 接口成员（非存储） | **D** |
| `VoxelWorldRuntimeCoreValidation.cs` | L1348 | `public WorldGenerationSettings Settings { get { return inner.Settings; } }` | 委托包装（无存储） | **E** |
| `WorldGenerationSettings.cs` | L95 | `public static WorldGenerationSettings Default` | 属性（运行时新建，非存储） | **B** |

> **关键结论**：**没有任何 MonoBehaviour / ScriptableObject 把 `WorldGenerationSettings` 作为序列化字段持有** ⇒ 该结构当前**没有** Inspector/场景/Prefab 序列化承载面（与 R33 C-3「UNKNOWN 但无暴露」一致，本轮进一步确认"无承载"）。

### 3.3 `WorldGenerationSettings.Default` 的 52 处使用

| 归属 | 数量 | 分类 |
|---|---:|---|
| `Assets/Editor/` 各验证器（`ChunkStreaming`/`GRI`/`StageB`/`StageC`/`StageD`/`RuntimeCore`/`View`/`WorldGenerationCore`/`WorldV2`） | **51** | **E**（测试/验证器） |
| `Assets/Scripts/VoxelWorld/Generation/Geology/GeologySettings.cs` L71 | 1 | **文档注释**（非调用） |
| **`Assets/Scripts`（生产代码）中的实际调用** | **0** | — |

⇒ 六参构造 / `DefaultSeaLevel` 路径**仅被测试代码使用**；生产路径唯一构造点是 **7 参显式传入**（§3.1 #1）。

### 3.4 `SeaLevel` 的 54 处出现（按文件）

| 文件 | 处数 | 性质 | 分类 |
|---|---:|---|---|
| `WorldGenerationSettings.cs` | 19 | 字段/构造/`IsValid` 校验/`ToString`/注释 | **定义侧**（含 A/B 两种构造的语义说明） |
| `WorldV2ParameterPipelineValidation.cs` | 18 | 验证器用例与断言 | **E** |
| `WorldParameterRules.cs` | 4 | `OceanRule` 读取 `settings.SeaLevel`（L143，**唯一活消费者**）+ 3 条注释 | **D**（消费） |
| `VoxelWorldStreamingBootstrap.cs` | 3 | 字段声明（L67）、文档注释、作为第 7 实参（L195） | **A** |
| `GeologyStageDValidation.cs` | 3 | 静态断言（检查 Bootstrap 暴露 `SeaLevel` 并以七参传入） | **E** |
| `GeologyRuntimeIntegrationValidation.cs` | 2 | 注释 + 使用 Bootstrap 字段构造（未传 SeaLevel） | **E** |
| `WorldParameterRule.cs` | 2 | 注释（`Elevation < SeaLevel`） | 文档 |
| `WorldSkeletonFacts.cs` | 2 | `TryEvaluate(..., int seaLevel, ...)` 参数 + `elevation < seaLevel` | **U**（见 §5 R-3：**无调用者**） |
| `ChunkStreamingValidation.cs` | 1 | 静态断言 | **E** |

### 3.5 `IsValid` 中作用于 `WorldGenerationSettings` 的调用点

| 位置 | 行 | 分类 | 说明 |
|---|---:|---|---|
| `VoxelWorldStreamingBootstrap.cs` | L197 | **A（生产校验）** | 失败写入 `buildError = "WorldGenerationSettings 非法：" + reason`（L200）⇒ **显式失败，不静默降级** |
| `WorldGenerationCoreValidation.cs` | L78 | **E** | 验证器断言 |
| 生成器内部 | `BasicTerrainGenerator.cs` L32、`GeologySampler.cs` L51/L62、`GeologyWorldGenerator.cs` L52/L63 | **D（强制校验）** | 构造即校验，非法抛异常 ⇒ 形成**第二道防线** |

---

## 4. 是否存在通向生产生成路径的 C 类调用点

**结论：不存在。**

证据链：

1. **C 类的三种触发方式在本项目全部为 0 命中**：
   - `default(WorldGenerationSettings)` = **0**
   - `new WorldGenerationSettings()`（无参/对象初始化式，R33 实测会得 `SeaLevel = 0` 且 `IsValid = true`）= **0**
   - 序列化 API（`JsonUtility` / `FromJson*` / `BinaryFormatter` / `XmlSerializer` / `DataContractSerializer`）= **0**
2. **无 Unity 序列化承载**：`WorldGenerationSettings` 的 4 处字段承载全部是普通 C# 类的 `readonly` 字段或测试装置字段（§3.2），无 MonoBehaviour / ScriptableObject 字段。
3. **生产构造点唯一且为 A 类**：`VoxelWorldStreamingBootstrap.Build()`（L194–195）以 7 参显式传入 Inspector 字段 `SeaLevel`，并经 `IsValid` 校验后才进入 `GeologySampler` / `GeologyWorldGenerator`。
4. **生产代码不依赖 `DefaultSeaLevel` 路径**：`WorldGenerationSettings.Default` 在 `Assets/Scripts` 中的实际调用 = 0。

⇒ R33 观测到的「缺字段 ⇒ 0 且 IsValid=true」风险，**当前没有可到达的生产调用点**。

---

## 5. 当前可确认的风险与 UNKNOWN 项

| # | 项 | 类型 | 证据 / 说明 |
|---|---|---|---|
| **R-1** | `SeaLevel` 字段无内联初始化器；若未来引入任何结构化反序列化或对象初始化式构造，将得 **0** 且可能 `IsValid=true` | **已确认行为 + 未来风险** | R33 实测（C2 / A3）；本轮确认**当前 0 命中**、无承载面 |
| **R-2** | `GeologyRuntimeIntegrationValidation.BootstrapRig`（L950–952）用**六参**构造参考设置，**忽略** `Bootstrap.SeaLevel` | **测试语义偏差（E 类）** | 若测试把 `Bootstrap.SeaLevel` 设为非 64，rig 的 `GenerationSettings` 与被测 Bootstrap 内部设置不一致；当前无测试这么做，故无实际影响。**未修复**（本轮只读） |
| **R-3** | `WorldSkeletonFacts.TryEvaluate(..., int seaLevel, ...)`（L94–L117）是 `SeaLevel` 的**另一消费者**，但**全库 0 个调用者**（8 处命中全部在自身文件内） | **U（未使用 API，来源不可判定）** | 一旦被接入，其 `seaLevel` 实参由谁提供、是否走配置读取，**无现行约束** |
| **R-4** | `WorldHeight = 64` 且 `SeaLevel = 64` ⇒ 全部合法高度皆海洋（`elevation < 64` 恒真） | 既有未决项 | 本轮未改变；语义是否期望仍待所有者裁决 |
| **U-1** | 验证器中 L151 `-1`、L152 `65` 为**故意越界**用例 | **已解释，非缺陷** | 期望 `IsValid = false`；R33 实测确认该反例保护有效 |
| **U-2** | `_phase2b_validation` 工程副本内的 3 处构造点 | **E（历史副本，未参与编译）** | 不作为本轮证据，但若将来复用该副本需重新审计 |
| **U-3** | 场景/Prefab 中通过 Inspector 把 `SeaLevel` 设为非默认值的情形 | **无实例 ⇒ 不存在** | R33 实测 35 个 `.unity/.prefab` + 全部 `.asset/.mat/.json` 中 Bootstrap GUID 命中 **0** |

---

## 6. 命中数量与分类统计（汇总）

| 维度 | 数量 |
|---|---:|
| 纳入审计的 `.cs` 文件 | **235**（`Assets`）+ 0（`Packages`） |
| `WorldGenerationSettings` 类型出现 | **137** |
| **构造调用点** | **13** |
| ├ A（显式构造，SeaLevel 来源明确） | **1**（Bootstrap L194–195，**唯一生产点**） |
| ├ B（六参，依赖 `DefaultSeaLevel`） | **1**（`Default` 属性 getter；**生产调用者 0**） |
| ├ C（默认结构体 / 缺字段反序列化） | **0** |
| ├ D（仅传递/读取的构造点） | **0**（构造点维度） |
| ├ E（测试/验证器） | **11** |
| └ U | **0** |
| 类型承载字段 | **4**（全部非 Unity 序列化承载；3×D 生产只读字段 + 1×E 测试字段） |
| 属性/返回值承载 | **3**（`Default`=B、`IWorldGenerator.Settings`=D、验证器包装=E） |
| `.Default` 使用 | **52**（51×E + 1 文档注释；生产 = **0**） |
| `SeaLevel` 出现 | **54**（含 2 处 U：`WorldSkeletonFacts`） |
| 序列化 API 使用 | **0** |
| `default(WorldGenerationSettings)` / 无参构造 | **0 / 0** |

---

## 7. 结论

1. **`WorldGenerationSettings` 的生产创建点只有 1 处**：`VoxelWorldStreamingBootstrap.Build()`（7 参显式传入 Inspector 字段 `SeaLevel`，默认 64），并经 `IsValid` + 生成器构造校验双重防线。
2. **不存在通向生产生成路径的 C 类调用点**：`default(struct)`、无参构造、序列化 API 均 0 命中，且无 Unity 序列化承载字段。
3. **`DefaultSeaLevel` 兼容路径仅被测试代码使用**（生产 `.Default` 调用 = 0）。
4. **仍为 UNKNOWN / 待决**：`WorldSkeletonFacts.TryEvaluate` 的 `seaLevel` 来源（无调用者）、`BootstrapRig` 六参偏差（测试语义）、R-1 的未来序列化风险、R-4 全域海洋语义裁决。
5. 本轮**未修改任何文件**（除非本报告），默认值与合法范围均未动；349 条冻结哈希仍为 **0 失配 / 0 缺失**。

### 交付物

| 文件 | 说明 |
|---|---|
| `_phase2b_validation/round34_world_generation_settings_creation_audit.md` | 本报告 |

### 变更清单

| 变更 | 内容 |
|---|---|
| 修改 | **无** |
| 删除 / 移动 / 覆盖 | **无** |
