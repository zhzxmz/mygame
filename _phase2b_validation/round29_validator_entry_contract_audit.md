# R29：`WorldV2ParameterPipelineValidation` 验证器入口契约调查（只读）

- 审计性质：**只读调查**。本轮未修改任何 `.cs` / `.meta` / `.md`（除本报告）/ `.gitignore` 或既有证据；未运行 Unity；未执行 `git add` / `commit` / `push` / `reset` / `clean`。
- 唯一新增文件：`_phase2b_validation/round29_validator_entry_contract_audit.md`（本报告）。
- 现场：分支 `main`，HEAD `37246478ed354a1e9a0ce7a3a3d9457caf188062`，staged = 0，`git status --short` = 23 条（与 R28 一致）。

---

## 1. 目标验证器入口清单

文件：`Assets/Editor/WorldV2ParameterPipelineValidation.cs`（**304 行 / 30 处 `Check(`**）

| 入口 | 位置 | 可见性 | 属性 | 行为 | 失败信号 |
|---|---|---|---|---|---|
| `Validate()` | L31–32 | `public static` | `[MenuItem("Tools/World V2/Validate Parameter Two-Phase Publishing")]` | 验证主体：30 条断言，末尾 `Report("World V2 参数两段式发布")`（L253） | `Check` 失败时 `failures++` + `Debug.LogError`；**不退出、不抛异常** |
| `ValidateOrThrow()` | L257–267 | `public static` | 无 | 调用 `Validate()`（L259），随后 `if (failures > 0) throw new InvalidOperationException(...)`（L263–265） | **抛异常**；**不调用 `EditorApplication.Exit`** |
| `Check(bool, string)` | L276 | `private static` | — | PASS/FAIL 记录 | — |
| `Report(string)` | L289 | `private static` | — | 输出 `[WorldV2-TwoPhase] … ⇒ failures=N（全部通过/存在失败项）` | — |
| `Clamp01(float)` | L269 | `private static` | — | 数值辅助 | — |

**是否存在 `RunBatch()`：不存在。** 是否存在 `EditorApplication.Exit`：**不存在**。

**调用关系**：`ValidateOrThrow()` → `Validate()` → `Check(...)` ×30 → `Report(...)`。菜单与批处理**共用同一个验证主体**，无第二套断言路径。

**Unity Editor API 依赖**：
- `using UnityEditor;`（L4）→ 仅用于 `[MenuItem]`
- `using UnityEngine;`（L5）→ `Debug.Log`（L280/L297）/ `Debug.LogError`（L285/L301）
- **未使用** `EditorApplication`、`AssetDatabase`、场景或资产 API ⇒ 纯 Editor 程序集内的无副作用验证器（文件头 L25 亦自述"不修改任何运行时数据、不触碰场景与资产、不写入文件"）

**可发现性**：`[MenuItem]` 使其在 Unity 菜单可见；项目内**不存在**反射式验证器自动发现或登记表（见 §3）。

**自述批处理入口**（L23，原文）：
```
/// 批处理：`Unity -batchmode -quit -executeMethod WorldV2ParameterPipelineValidation.ValidateOrThrow`
```

> 注意：**不据方法命名推断已执行**。其真实执行证据见 §4。

---

## 2. 对照验证器入口矩阵

### 2.1 指定的 11 个对照验证器

| 验证器 | `RunBatch()` | 菜单入口 | `ValidateOrThrow()` | `EditorApplication.Exit` |
|---|---|---|---|---|
| `VoxelWorldCoreValidation` | **有** L31 | 有 | 无 | 有 L37（`ok ? 0 : 1`） |
| `WorldGenerationCoreValidation` | **有** L42 | 有 | 无 | 有 L53 |
| `VoxelMeshCoreValidation` | **有** L41 | 有 | 无 | 有 L52 |
| `VoxelWorldViewValidation` | **有** L55 | 有 | 无 | 有 L66 |
| `VoxelWorldRuntimeCoreValidation`（RTC） | **有** L63 | 有 | 无 | 有 L74 |
| `ChunkStreamingValidation`（CS12） | **有** L56 | 有 | 无 | 有 L66 |
| `GeologyStageAValidation`（Stage A） | **有** L47 | 有 | 无 | 有 L57 |
| `GeologyStageBValidation`（Stage B） | **有** L53 | 有 | 无 | 有 L63 |
| `GeologyStageCValidation`（Stage C） | **有** L50 | 有 | 无 | 有 L60 |
| `GeologyStageDValidation`（Stage D） | **有** L56 | 有 | 无 | 有 L66 |
| `GeologyRuntimeIntegrationValidation`（GRI） | **有** L58 | 有 | 无 | 有 L68 |
| **`WorldV2ParameterPipelineValidation`（目标）** | **无** | 有 | **有** L257 | **无** |

`RunBatch()` 的统一实现形态（10 个验证器同构，逐字摘自源码）：

```csharp
public static void RunBatch()
{
    ValidateAll();

    Debug.Log(
    "[GeologyStageDValidation] ===== SUMMARY =====\n" +
    "Checks = " + checks + "\n" +
    "Failures = " + failures + "\n" +
    "Result = " + (failures == 0 ? "PASS" : "FAIL"));

    EditorApplication.Exit(failures == 0 ? 0 : 1);
}
```

`VoxelWorldCoreValidation.RunBatch()` 形态不同（L31–38），但同样以 `EditorApplication.Exit(ok ? 0 : 1)` 结束：

```csharp
public static void RunBatch()
{
    string report;
    bool ok = VoxelWorld.VoxelWorldDebug.RunSelfCheck(out report);
    Debug.Log(report);
    EditorApplication.Exit(ok ? 0 : 1);
}
```

### 2.2 全量统计（`Assets/Editor` 下全部 `*Validation.cs`）

| 指标 | 数值 |
|---|---|
| `*Validation.cs` 总数 | **46** |
| 含 `public static void RunBatch` | **45** |
| **不含 `RunBatch`** | **1** —— 恰为 `WorldV2ParameterPipelineValidation.cs` |
| `RunBatch` 内使用统一形态 `EditorApplication.Exit(failures == 0 ? 0 : 1)` | **44** |
| 使用其它退出形态 | **1** —— `VoxelWorldCoreValidation.cs`（`ok` 变量） |
| 含 `ValidateOrThrow` 的验证器 | **1** —— 目标验证器（唯一） |

结论：`RunBatch()` 是 **45/46 的事实命名约定**，但**不是**全库无例外的规则（且目标验证器是唯一豁免者）；失败信号约定（`EditorApplication.Exit`）为 45/46 一致。

---

## 3. 入口契约与调用方证据

### 3.1 规格层（`Specifications/**/*.md`）—— **无该约定**

递归搜索 `Specifications` 下全部 `.md`：**`RunBatch` 命中 = 0**。

规格中与"验证入口"有关的条文只涉及**集合规模与冻结政策**，不含入口签名要求：

- `Specifications/Architecture/Legacy_Status.md` L65（`LS23`）：Legacy 判定含"属于 `LS15` 的 **34 个验证入口名单**"；L124（`LS59`）："Legacy 的 34 个验证入口保持原状（不删不改）"。
- `Specifications/Architecture/WorldV2_Implementation_Boundary_Plan.md` L75（`IBP23`）：V2 已实现并已通过验证的**11 个验证入口**（附录 C）；L194（`IBP66`）：Legacy 的 34 个入口保持原状。
- `Specifications/Audit/WorldArchitecture_CrossDocument_Audit_V1.md` L342（`B-8`）：**未决项** —— "旧栈遗留文档/验证入口与新契约族**未建立映射**（**45 个验证入口**）"。

⇒ **规格层没有"每个验证器必须提供 `RunBatch()`"的明文规则**；`B-8` 反而登记了"验证入口未建立映射"这一**未决审计项**。

### 3.2 验证器内部的入口断言 —— 存在，但作用域是**显式硬编码清单**

| 断言（原文） | 位置 | 清单规模 | 目标是否在清单内 |
|---|---|---|---|
| `"GC7：全部入口都提供 public static void RunBatch()（缺失 " + missingRunBatch + "）"` | `GeologyStageCValidation.cs` L887 | 由 L890–899 的 `string[] entryFiles`（**8 个**）驱动 | **否**（0 次提及） |
| `"GC7：8 个验证入口文件都存在（缺失 " + missingFiles + "）"` | 同上 L909 | 8 | **否** |
| `"GW10：" + entryFiles.Length + " 个验证入口文件都存在（缺失 " + missingEntries + "）"` | `GeologyStageDValidation.cs` L1039 | L1018–1029，**10 个** | **否**（0 次提及） |
| `"GE9：" + entries.Length + " 个验证入口文件都存在（缺失 " + missingEntries + "）"` | `GeologyRuntimeIntegrationValidation.cs` L822 | L800–812，**11 个** | **否**（0 次提及） |

三个清单的成员均为：`VoxelWorldCore / WorldGenerationCore / VoxelMeshCore / VoxelWorldView / VoxelWorldRuntimeCore / ChunkStreaming / GeologyStageA–D（+ GRI 自身）`。
**全库 `.cs` 递归搜索 `WorldV2ParameterPipelineValidation`：仅 2 处命中，均在其自身文件内（L23 注释、L27 类声明）** ⇒ 目标验证器**未登记进任何入口清单**。

### 3.3 批量执行脚本 —— 显式清单，不含目标，且不依赖命名约定扫描

`_phase2b_validation/run_regression.ps1`（74 行，untracked 证据文件）：

- 以 `@{ Name = '…'; Method = '<Class>.<Method>' }` 形式**显式登记 36 个方法**；
- 其中 **34 个**为 `.RunBatch` 后缀，**2 个不是**（`TerrainMultiChunkValidation.RunMiningBatch`、`TerrainMultiChunkValidation.RunLifecycleBatch`）⇒ 脚本**不依赖 `RunBatch` 命名约定**，而是逐条列举；
- 脚本内 `Get-ChildItem` / `GetTypes` / 目录扫描 / `foreach *Validation` **命中 = 0** ⇒ **无自动发现**；
- 36 个方法中属于 World V2 / Geology / Streaming / RuntimeCore 的 = **0** ⇒ 该脚本是**早期 legacy 栈（VoxelAwareMesh* / CoordinateMigration* / Terrain* / Mining* / Player*）的回归清单**，早于上述验证器存在；
- 该脚本被引用 **11 次**，全部位于 `_phase2b_validation` 的历史 `*_git_diff.txt` / `*_phase_report.md` 证据中，**无任何现行脚本或验证器调用它**。

### 3.4 自动发现机制 —— 不存在

全库递归搜索结果：反射仅出现在两类用途——

1. `GeologyStageCValidation.cs` L874：`Type type = editorAssembly.GetType(ExistingEntryTypes[i]);` —— 遍历**显式硬编码的类型名数组**，用于检查这些类型是否提供 `RunBatch`；
2. `GeologyStageCValidation.cs` L508/L515/L603/L609、`VoxelWorldRuntimeCoreValidation.cs` L643/L647 —— 检查特定类型的接口方法（`IDepositProvider` / `IBiomeProvider` / `ChunkRecord`），与验证器入口发现无关。

⇒ **没有"扫描 Editor 程序集找出所有 `*Validation` 类型"的注册表或自动发现机制**；入口是"显式点名 + `-executeMethod` 直接指定"模式。

---

## 4. Round 9 实际执行路径

| 证据 | 内容 |
|---|---|
| 进度主记录 `sealevel_round7_progress.md` L47（原文） | `命令：Unity.exe -batchmode -nographics -quit -projectPath <proj> -logFile <TEMP>\unity_r9_v2.log -executeMethod WorldV2ParameterPipelineValidation.ValidateOrThrow` |
| 同记录 L52 | 日志路径 `C:\Users\xmz\AppData\Local\Temp\unity_r9_v2.log` |
| 目标文件自述 L23 | `-executeMethod WorldV2ParameterPipelineValidation.ValidateOrThrow` |
| R9 日志实证（本轮复核，文件仍存在） | 含 `[WorldV2-TwoPhase] PASS: Evaluate 只发布 Base 段（IsFinalized == false，PC19）`（L328）等 30 条 `[WorldV2-TwoPhase]` 输出；汇总行 `failures=0（全部通过）` |

⇒ **R9 是通过 `ValidateOrThrow()` 执行的，不是 `RunBatch()`**；执行成功、断言真实产出、`failures=0`。
（本轮仅复读日志，**未重新运行** Unity，未重建或覆盖任何报告。）

**失败路径的证据缺口（据实标注为 D 类子项）**：本会话内该验证器**从未失败过**，因此 `ValidateOrThrow()` 抛异常时 Unity `-executeMethod` 的实际退出码**从未被观测**。对照的 45 个验证器使用 `EditorApplication.Exit(0/1)`，退出码语义**确定可依赖**；目标的"抛异常→非零退出"路径属**未验证**。

---

## 5. 结论：**B（没有违反契约，但自动化集成不足）**

### 5.1 为什么不是 A（确认违反已有入口契约）

- **规格层无 `RunBatch` 明文要求**（`Specifications/**/*.md` 命中 0）；
- 存在"全部入口都提供 `RunBatch()`"的断言（`GC7`），但其作用域是**显式 8/10/11 个文件清单**，目标验证器**从未被登记进任何清单**；
- 因此不存在"被现有规则判定为违约"的事实基础。**不为了统一风格而默认归为 A**。

### 5.2 为什么不是 C（现有入口足够）

- 现有入口（`ValidateOrThrow`）**可执行且有真实成功证据**（§4），这一点满足 C；
- 但存在四项可测的集成不足：
  1. **命名不一致**：45/46 验证器提供 `RunBatch`，目标为唯一例外；
  2. **未纳入任何入口清单**：StageC/StageD/GRI 的入口存在性断言天然不覆盖它；
  3. **未纳入任何回归脚本**：`run_regression.ps1` 不含它（也不含同批的 Geology/Streaming/RTC 验证器）；
  4. **失败信号语义不一致**：45 个用 `EditorApplication.Exit(0/1)`（退出码确定），目标用抛异常（**退出码语义未验证**）。
  ⇒ 因此不归为 C。

### 5.3 为什么不是 D（证据不足）

除"失败路径退出码未观测"这一**子项**外，其余判定所需证据均已取得：规格搜索、46 个验证器全量矩阵、3 份入口清单、回归脚本清单、R9 执行记录与日志实证。故整体结论为 **B**，并把"抛异常退出码"单列为待验证项。

---

## 6. 最小修改方案与影响范围（**本轮不实施**）

### 6.1 建议的最小实现

```csharp
/// <summary>批处理入口（与其余 45 个验证器形态一致）：以退出码表示成败。</summary>
public static void RunBatch()
{
    Validate();                                             // 复用唯一验证主体，不新增断言
    EditorApplication.Exit(failures == 0 ? 0 : 1);           // 与 45 个验证器一致的退出语义
}
```

### 6.2 它解决的具体问题

1. **命名一致性**：46/46 满足 `RunBatch` 约定，可被"入口清单/回归脚本"按统一方式点名；
2. **退出码确定性**：把"抛异常→退出码未验证"变为与 44 个验证器一致的 `Exit(0/1)`；
3. **可登记性**：为后续把这些验证器纳入统一清单/脚本铺平道路（不改断言即可被点名）。

### 6.3 最小实现的约束与副作用评估

| 关注点 | 结论 |
|---|---|
| 调用哪个既有入口 | 必须调用 **`Validate()`**（唯一验证主体）。**不得**调用 `ValidateOrThrow()`——否则异常会先于 `Exit` 抛出，退出码仍不确定 |
| 是否重复执行验证 | **否**：`RunBatch` 内只调用一次 `Validate()`；`ValidateOrThrow()` 保持原样不动，二者被分别调用时互不影响 |
| 是否重复输出报告 | **否**：`Report(...)` 在 `Validate()` 内 L253 触发一次 |
| 是否改变断言/覆盖 | **否**：不新增、不删改任何 `Check(`（保持 30 条）；验证内容零变化 |
| 是否改变哈希登记 | **否**（已实测）：7 个验证器的冻结表对 `WorldV2ParameterPipelineValidation` 的登记项 = **0**，该文件未被任何冻结表登记 ⇒ 无级联、无需重钉 |
| 是否需要同步其他验证器 | **不需要**（且**不建议本轮做**）：若把它加入 StageC/StageD/GRI 的 `entryFiles`/`entries` 清单，将修改这 3 个验证器源码 ⇒ 触发 349 条登记中的多条失效 ⇒ 需再走一轮"改动+审查+重钉"，**非最小改动** |
| 是否需要同步脚本 | 可选：在 `run_regression.ps1` 增加一条 `@{ Name='WorldV2'; Method='WorldV2ParameterPipelineValidation.RunBatch' }`。该脚本位于 untracked 证据目录，不影响源码与哈希 |
| 是否保留 `ValidateOrThrow` | **建议保留**：R9 证据引用的就是它；删除会使历史记录失去可复现指令 |

### 6.4 影响面清单

| 对象 | 影响 |
|---|---|
| `Assets/Editor/WorldV2ParameterPipelineValidation.cs` | +约 5 行（新方法），其余不变；`Check(` 仍 30 |
| 349 条冻结哈希登记 | **无影响**（目标文件不在任何表中） |
| 3 个含入口清单的验证器 | **不改**（如需纳入，另行立轮） |
| `run_regression.ps1` | 可选 +1 行（untracked 证据） |
| 历史证据/报告 | 只读，不覆盖 |

---

## 7. 精确文件变更清单

| 变更 | 文件 |
|---|---|
| 新增 | `_phase2b_validation/round29_validator_entry_contract_audit.md`（本报告） |
| 修改 | **无** |
| 删除 / 移动 / 重命名 | **无** |

**现场复核**：`main` / `37246478ed354a1e9a0ce7a3a3d9457caf188062`（未变）；staged = **0**；`git status --short` = **23 条**（16 tracked `M` + 4 untracked 源文件 + 3 untracked 证据目录，与 R28 结束时一致）；原有源码、验证器、规范与证据**均未修改**。

---

## 8. 待决定事项（不自行实施）

1. 是否采纳 §6.1 的最小 `RunBatch()` 方案（**需要你的批准**；本轮未改任何代码）。
2. 若采纳，是否在同一轮内把它登记进 StageC/StageD/GRI 的入口清单（**会触发哈希级联**，建议单独立轮）。
3. 是否需要在真实失败用例下观测 `ValidateOrThrow()` 的退出码语义，以关闭 §4 的 D 类子项。
