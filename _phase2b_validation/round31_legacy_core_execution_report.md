# R31：四个 Legacy Core 验证器真实运行报告（只读验证）

- 性质：**只读验证**。未修改任何 `.cs` / `.meta` / 规范文档 / `.gitignore` / 哈希登记 / 既有证据；未执行 commit / push / reset / clean。
- 现场：分支 `main`，HEAD `37246478ed354a1e9a0ce7a3a3d9457caf188062`（未变），staged = 0。
- 目的：补齐此前标记为 **unknown** 的四个 legacy core 验证器的真实执行状态。

---

## 1. 执行前检查

### 1.1 四个验证器的真实入口与退出码语义（源码实测）

| 验证器 | 文件行数 | `RunBatch()` 完整类型名 | 验证主体 | 退出码设置 |
|---|---:|---|---|---|
| `VoxelWorldCoreValidation` | 39 | `VoxelWorldCoreValidation.RunBatch`（无命名空间） | `VoxelWorld.VoxelWorldDebug.RunSelfCheck(out report)` → `Debug.Log(report)` | `EditorApplication.Exit(ok ? 0 : 1)`（L37） |
| `WorldGenerationCoreValidation` | 524 | `WorldGenerationCoreValidation.RunBatch` | `ValidateAll()` → `Debug.Log("[WorldGenerationCoreValidation] ===== SUMMARY =====…")` | `EditorApplication.Exit(failures == 0 ? 0 : 1)`（L53） |
| `VoxelMeshCoreValidation` | 761 | `VoxelMeshCoreValidation.RunBatch` | `ValidateAll()` → SUMMARY | `EditorApplication.Exit(failures == 0 ? 0 : 1)`（L52） |
| `VoxelWorldViewValidation` | 1434 | `VoxelWorldViewValidation.RunBatch` | `ValidateAll()` → SUMMARY | `EditorApplication.Exit(failures == 0 ? 0 : 1)`（L66） |

### 1.2 副作用检查（是否能修改项目资产 → 能否安全运行）

对四个文件扫描写盘/资产/场景/Prefab/Undo 类 API：

| 验证器 | 命中项 | 判定 |
|---|---|---|
| `VoxelWorldCoreValidation` | 无 | 安全 ✓ |
| `WorldGenerationCoreValidation` | 无 | 安全 ✓ |
| `VoxelMeshCoreValidation` | 无 | 安全 ✓ |
| `VoxelWorldViewValidation` | L1032 `new GameObject(name)`（`TestScope` 内，临时对象）；L1337 `EditorSceneManager.GetActiveScene()`；L1341 `EditorSceneManager.NewScene(EmptyScene, Single)` | **不写盘**：无 `SaveScene` / `SaveAssets` / `AssetDatabase.CreateAsset` / `DeleteAsset` / `File.WriteAllText`；`Directory.Exists/GetFiles`（L856–859）为**只读**扫描。新建场景为**未保存**的内存场景，仅存在于本次 `-batchmode` 进程内，进程退出即丢弃 ⇒ **不会修改项目资产** ✓ |

> 结论：四个验证器均可安全执行；`VoxelWorldViewValidation` 只影响自身批处理进程的内存编辑态。

### 1.3 Unity 可执行文件与项目路径（实测，未猜测）

| 项 | 值 |
|---|---|
| Unity 可执行文件 | `D:\gamemaking\2022.3.62f1c1\Editor\Unity.exe`（存在；版本 `2022.3.62.11538587`） |
| 项目路径 | `D:\gamemaking\My project`（`ProjectVersion.txt` = `m_EditorVersion: 2022.3.62f1c1`） |

### 1.4 运行前基线

| 项 | 值 |
|---|---|
| 四验证器当前哈希 | `VoxelWorldCoreValidation`=`6DE813AABCE95394`、`WorldGenerationCoreValidation`=`4A517A0F902A1066`、`VoxelMeshCoreValidation`=`5FFD8451D2CE05EB`、`VoxelWorldViewValidation`=`BEFBAAF6B111A241` |
| 四者被冻结表登记次数 | 各 **7 处**，且 pin 与实测哈希**逐一相同**（共 28 条登记全匹配） |
| R30 新增 `RunBatch` | `WorldV2ParameterPipelineValidation.cs` = `8BF57A66514F3286` / 336 行（与 R30 交付一致） |

---

## 2. 执行结果（逐个真实运行，独立日志；一个失败不阻断其余）

统一命令形态：
`"D:\gamemaking\2022.3.62f1c1\Editor\Unity.exe" -batchmode -nographics -quit -projectPath "D:\gamemaking\My project" -logFile <TEMP>\unity_r31_<Validator>.log -executeMethod <Validator>.RunBatch`

| # | 验证器 | `-executeMethod` | 真实退出码 | 耗时 | Checks | Failures | PASS 行 | FAIL 行 | error CS | warning CS | 崩溃 | 日志（bytes） |
|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---|---:|
| 1 | `VoxelWorldCoreValidation` | `VoxelWorldCoreValidation.RunBatch` | **0** | 15 s | **61** | **0** | 61 | 0 | 0 | 0 | 无 | `round31_VoxelWorldCoreValidation_run.log`（73954） |
| 2 | `WorldGenerationCoreValidation` | `WorldGenerationCoreValidation.RunBatch` | **0** | 13 s | **41** | **0** | 41 | 0 | 0 | 0 | 无 | `round31_WorldGenerationCoreValidation_run.log`（106784） |
| 3 | `VoxelMeshCoreValidation` | `VoxelMeshCoreValidation.RunBatch` | **0** | 13 s | **37** | **0** | 37 | 0 | 0 | 0 | 无 | `round31_VoxelMeshCoreValidation_run.log`（98599） |
| 4 | `VoxelWorldViewValidation` | `VoxelWorldViewValidation.RunBatch` | **0** | 14 s | **127** | **0** | 127 | 0 | 0 | 0 | 无 | `round31_VoxelWorldViewValidation_run.log`（182828） |

每个验证器的独立日志命名：`_phase2b_validation/round31_<Validator>_run.log`，配套 `round31_<Validator>_stdout.txt` / `_stderr.txt`（**均 0 字节**，Unity 输出走 `-logFile`，管道为空——如实保留，非伪造）。

### 2.1 入口真实执行的证据（不只看退出码）

| 验证器 | 证据 |
|---|---|
| `VoxelWorldCoreValidation` | 日志含 `RunSelfCheck` 报告的 61 条 `PASS - …` 明细（如 L358–362 `PASS - RemoveChunk 生效（ChunkCount=1）`）与 L363–365 三行 `Checks = 61 / Failures = 0 / Result = PASS` |
| `WorldGenerationCoreValidation` | L796 `[WorldGenerationCoreValidation] ===== SUMMARY =====` + `Checks = 41 / Failures = 0 / Result = PASS` |
| `VoxelMeshCoreValidation` | L745 `[VoxelMeshCoreValidation] ===== SUMMARY =====` + `Checks = 37 / Failures = 0 / Result = PASS` |
| `VoxelWorldViewValidation` | L1989 `[VoxelWorldViewValidation] ===== SUMMARY =====` + `Checks = 127 / Failures = 0 / Result = PASS` |

### 2.2 崩溃 / 异常 / 超时复核（含对自身误报的更正）

- 初筛用宽正则曾报 `crash=4`，复核后确认**全部为 Unity 启动横幅误报**：命中行是 `StartBugReporterOnCrash: 0`、`WinEditorMain.ConfigureCrashHandler`、`WinEditorMain.InitializeCrashHandler`。
- 用精确正则（`Crash!!!` / `Aborting batchmode` / `Unhandled exception` / `StackOverflowException` / `Fatal error` / `Native Crash` / `Received signal`）复检：**四份日志命中均为 0** ⇒ **无真实崩溃**。
- 含 `Exception` 字样的行：仅 `WorldGenerationCoreValidation` 1 处，原文为 **`PASS - 非法参数构造生成器时抛 ArgumentException（显式失败，不静默用默认值）`** ⇒ 属**预期的异常断言**，非失败。
- **无超时**（单次 13–15 s，均正常退出）。
- 四份日志末行均为 `~StackAllocator(ALLOC_TEMP_MAIN) m_LastAlloc not NULL. Did you forget to call FreeAllStackAllocations()?` —— 经抽查 **R25 日志末行完全相同**，属 Unity batchmode 退出期的既有提示，**非崩溃**。

### 2.3 编译状态说明

四轮 `error CS = 0`，`warning CS = 0`。原因：本轮**未改动任何源文件**，Unity 命中既有编译缓存、**未触发重新编译**，故不产生警告输出。这**不代表**此前记录的 2 条既有警告（`VoxelCoordinateMigrationStageB3CValidation.cs CS0162`、`MiningSurfaceIntegrityValidation.cs CS0414`）已消失；它们在 R10–R30 的重新编译轮次中均被观测到。

---

## 3. 交叉检查

| 检查项 | 结果 |
|---|---|
| 四验证器各自在冻结表中的登记（各 7 处、共 28 条） | **运行后逐一复核仍与实测哈希相同**（`6DE813AABCE95394` / `4A517A0F902A1066` / `5FFD8451D2CE05EB` / `BEFBAAF6B111A241`） |
| 全量 349 条冻结哈希 | **349 条 / 失配 0 / 缺失 0**（运行后独立重算，闭合未破坏） |
| R30 新增 `WorldV2ParameterPipelineValidation.RunBatch()` | **未被修改**：哈希 `8BF57A66514F3286`、336 行，与 R30 交付完全一致 |
| 是否重新运行其余冻结验证器 | **未运行**（本轮无意外修改、无登记不一致，按约束不重跑） |
| 是否修改哈希登记 / 重钉 | **未做任何修改** |

---

## 4. unknown 状态补齐情况

| 验证器 | R31 之前状态 | R31 之后状态 |
|---|---|---|
| `VoxelWorldCoreValidation` | 本会话从未运行 ⇒ **unknown** | **真实运行 PASS**（exit 0 / 61 checks / 0 failures） |
| `WorldGenerationCoreValidation` | unknown | **真实运行 PASS**（exit 0 / 41 / 0） |
| `VoxelMeshCoreValidation` | unknown | **真实运行 PASS**（exit 0 / 37 / 0） |
| `VoxelWorldViewValidation` | unknown | **真实运行 PASS**（exit 0 / 127 / 0） |

> 说明：以上为**各自验证器的执行结果**。四个验证器属 legacy 栈，其 PASS **不构成** World V2 或整个地质系统的端到端验证；World V2 参数管线仍**无生产消费者**（R26/R29 结论未变）。

---

## 5. 最终复核

### 5.1 现场

| 项 | 值 |
|---|---|
| 分支 / HEAD | `main` / `37246478ed354a1e9a0ce7a3a3d9457caf188062`（**未变**） |
| staged | **0** |
| `git status --short` | **23 条**（16 tracked `M` + 4 untracked 源文件 + 3 untracked 证据目录，与 R30 结束时一致） |
| `git diff --stat` 末行 | `16 files changed, 352 insertions(+), 121 deletions(-)`（未变） |
| 源码 / 资产 / 规范 / `.gitignore` | **均未修改** |
| 既有证据 | **未覆盖**（本轮只新增 `round31_*` 文件） |

### 5.2 本轮新增文件（12 个日志 + 3 个空捕获 + 1 份本报告）

| 文件 | 大小 |
|---|---:|
| `round31_VoxelWorldCoreValidation_run.log` | 73954 B |
| `round31_WorldGenerationCoreValidation_run.log` | 106784 B |
| `round31_VoxelMeshCoreValidation_run.log` | 98599 B |
| `round31_VoxelWorldViewValidation_run.log` | 182828 B |
| `round31_{四个验证器}_stdout.txt`（4 个） | 各 0 B |
| `round31_{四个验证器}_stderr.txt`（4 个） | 各 0 B |
| `round31_legacy_core_execution_report.md`（本文件） | — |

### 5.3 变更清单

| 变更 | 内容 |
|---|---|
| 修改 | **无** |
| 删除 / 移动 / 覆盖 | **无** |
| 新增 | 上述 13 个 `round31_*` 证据文件 + 本报告 |

---

## 6. 结论

1. 四个 legacy core 验证器**全部通过真实 Batchmode 运行**：退出码均 **0**，`Failures = 0`，checks 分别为 **61 / 41 / 37 / 127**，`error CS = 0`，**无崩溃、无异常、无超时**。
2. 每个结果均以**验证器自身输出的 SUMMARY 与 PASS 明细**为证据，未仅凭退出码或日志存在性判定。
3. 未修改任何源码、资产、规范、哈希登记或既有证据；349 条冻结哈希仍为 **0 失配 / 0 缺失**；R30 的 `RunBatch` 未被触碰。
4. 本轮**未**运行其余冻结验证器（无触发条件），也**未**宣称任何端到端验证。
