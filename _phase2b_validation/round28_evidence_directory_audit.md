# R28：`_phase2b_validation/` 证据目录与 Git 纳入风险审计（只读）

- 审计性质：**只读**。本轮**未删除、移动、重命名或覆盖任何原有文件**；未修改 `.gitignore`、生产代码、验证器、规范文档与进度记录；未运行任何 Unity 任务；未执行 `git add`。
- 本轮唯一新增文件：本报告 `_phase2b_validation/round28_evidence_directory_audit.md`。
- 未执行：commit / push / reset / clean。

---

## 1. 现场

| 项 | 值 |
|---|---|
| 分支 | `main` |
| HEAD | `37246478ed354a1e9a0ce7a3a3d9457caf188062`（与 R27 一致，未变） |
| staged | **0 个文件**（`git diff --cached --name-only` 为空） |
| `git status --short` | 23 条（16 tracked `M` + 4 untracked 源文件 + 3 untracked 证据目录） |
| `git diff --stat` | `16 files changed, 352 insertions(+), 121 deletions(-)` |

---

## 2. 目录规模统计

**统计口径**：`Get-ChildItem -LiteralPath <path> -Recurse -File -Force`（含隐藏文件）；大小 = 各文件 `Length` 之和；**不做**硬链接/稀疏文件去重，**不做** NTFS 压缩前大小换算。逐条实测，非估算。

| 层级 | 文件数 | 大小 |
|---|---:|---:|
| `_phase2b_validation/` 合计 | **3413** | **223.72 MB**（234,583,863 bytes） |

### 2.1 一级构成（按大小降序）

| 条目 | 类型 | 文件数 | 大小 |
|---|---|---:|---:|
| `Library/` | 目录 | 2156 | 86.84 MB |
| `Assets/` | 目录（Unity 工程副本源码） | 783 | 60.77 MB |
| `_unity_temp/` | 目录（Unity 临时/崩溃转储） | 148 | 20.97 MB |
| （一级散文件） | 文件 | 295 | 55.05 MB |
| `ProjectSettings/` | 目录 | 24 | 0.05 MB |
| `Packages/` | 目录 | 2 | 0.01 MB |
| `UserSettings/` | 目录 | 3 | 0.03 MB |
| `Logs/` | 目录 | 1 | < 0.01 MB |
| `_unity_localappdata/` | 目录 | 1 | < 0.01 MB |

### 2.2 `Library/` 二级构成

| 子目录 | 文件数 | 大小 |
|---|---:|---:|
| `Artifacts` | 704 | 58.25 MB |
| `PackageCache` | 1175 | 13.88 MB |
| `Bee` | 159 | 5.35 MB |
| `ScriptAssemblies` | 12 | 2.83 MB |
| `ShaderCache` | 76 | 0.19 MB |
| 其余（`PackageManager`/`StateCache`/`Search`/`TempArtifacts`） | 8 | 0.05 MB |

### 2.3 一级散文件 295 个的扩展名构成

| 扩展名 | 数量 | 大小 |
|---|---:|---:|
| `.log` | 193 | 49.05 MB |
| `.txt` | 69 | 3.82 MB |
| `.md` | 18 | 0.26 MB |
| `.dll` | 13 | 1.86 MB |
| `.ps1` | 1 | 0.01 MB |
| `.csv` | 1 | 0.05 MB |

### 2.4 全目录扩展名前 14（含子目录，用于识别"源码/构建产物"占比）

`.meta` 943 / 无扩展 717（64.33 MB）/ `.cs` 544（6.67 MB）/ `.log` 268（50.24 MB）/ `.png` 137 / `.mvfrm` 97 / `.md` 89 / `.txt` 85 / `.bin` 75 / **`.dmp` 73（19.70 MB）** / `.asset` 56 / `.json` 51 / `.psd` 37 / `.dll` 33。

> `.dmp` 73 个（19.7 MB）位于 `_unity_temp/Unity/Editor/Crashes/`，是早期 Unity 崩溃转储，属**临时产物**类。

---

## 3. 文件用途分类（仅分类，未更改）

| 类别 | 具体内容 | 文件数/大小 | 判定依据 |
|---|---|---|---|
| **A. Unity 工程副本（快照）** | `Assets/`（783 文件 / 60.77 MB）、`ProjectSettings/`、`Packages/`、`UserSettings/`、`Logs/`、`Library/` | 合计约 2970 文件 / 168.6 MB | 含 `ProjectVersion.txt`、`Assets/`、`Library/`、`Packages/manifest.json`，是完整 Unity 工程布局 |
| **B. Unity 临时/崩溃产物** | `_unity_temp/`（148 文件 / 20.97 MB，含 `.dmp` 73 个）、`_unity_localappdata/` | 149 文件 / 20.97 MB | 目录名与内容均为 TEMP/LOCALAPPDATA 重定向产物 |
| **C. 轮次真实运行日志（R10–R25）** | `round10`–`round15`（raw/rerun `.log`）、`round17`–`round25`（`_rerun.log`） | 19 个 `.log` / 约 3.0 MB（含在 70 个 round* 内） | 文件名含轮次 + 验证器名，内含 `===== SUMMARY =====` |
| **D. 轮次 stdout/stderr 捕获** | `round*_stdout.txt` / `round*_stderr.txt`（R10–R25） | 38 个 `.txt`，**全部 0 字节** | 与本会话历次记录一致（Unity 把输出写入 `-logFile`，管道为空） |
| **E. 哈希依赖审计证据** | `round16_hash_dependency_audit.md`、`round16_hash_audit_rows.csv`（349 条逐项明细）、`round17`–`round25_hash_dependency_audit_delta.md` | 11 个文件 / 约 0.13 MB | 文件名与内容均为哈希登记审计链 |
| **F. 轮次审计报告** | `round26_final_diff_audit.md`、`round27_document_consistency_audit.md` | 2 个 `.md` | — |
| **G. 历史回归日志/报告（本会话之前）** | 非 `round*` 的 `.log`（约 167 个）、`.txt`（约 31 个，含 `*_git_diff.txt` 快照）、6 份 `*_phase_report.md`、`partial_solid_report.md` | 约 205 文件 / 约 46 MB | 命名含 `regression_*`、`b3b/b3c/b3d/b3e*`、`stage*`、`*_git_diff.txt` 等历史里程碑标记 |
| **H. 独立编译产物** | `csc_audit*.dll`、`csc_check*.dll`、`csc_phase3*.dll`、`csc_phaseA.dll`、`csc_stageB1.dll`（13 个） | 13 个 `.dll` / 1.86 MB | 名称与历史轮次"csc 独立编译检查"记录一致 |
| **I. 工具脚本** | `run_regression.ps1` | 1 个 / 0.01 MB | 回归批处理脚本 |
| **J. 进度主记录** | `sealevel_round7_progress.md` | 1 个 / 0.08 MB / **1032 行** | R7–R27 单一进度记录，含全部轮次结论 |

---

## 4. Git 跟踪 / 修改 / 未跟踪 / 忽略分类

| 分类 | 结果 | 证据 |
|---|---|---|
| **已被 Git 跟踪** | **0 个**（该目录下无任何 tracked 文件） | `git ls-files -- _phase2b_validation` → 空 |
| **已修改的跟踪文件** | **0 个**（`M` 状态不含该目录任何文件） | `git status --short` 的 16 条 `M` 全部位于 `Assets/`、`Specifications/` |
| **未跟踪（非忽略）** | **1253 个文件**（逐文件展开） | `git status --porcelain -uall -- _phase2b_validation` → 1253 条 `??` |
| **被忽略** | **2160 个条目**（含目录与文件） | `git status --porcelain --ignored -uall` → 2160 条 `!!` |
| **普通状态列表中的表现** | 折叠为 **1 条** `?? _phase2b_validation/` | 未跟踪目录默认折叠 |

### 4.1 ignore 规则检查

存在**一个** ignore 文件：仓库根 `.gitignore`（`Assets/`、`Specifications/`、`_phase2b_validation/` 内均**无** `.gitignore`）。根 `.gitignore` 相关规则：

```
[Ll]ibrary/
[Tt]emp/
[Oo]bj/
[Bb]uild/
[Bb]uilds/
[Ll]ogs/
[Uu]ser[Ss]ettings/

.vs/
.vscode/
*.csproj
*.sln
```

关键机制：以上模式**不带前导 `/`**，因此在**任意深度**匹配 ⇒ 同样命中副本内部的同名目录。`git check-ignore -v` 抽样实测：

| 路径 | 结果 |
|---|---|
| `_phase2b_validation/Library` | **已忽略** ← `.gitignore:1:[Ll]ibrary/` |
| `_phase2b_validation/Logs` | **已忽略** ← `.gitignore:6:[Ll]ogs/` |
| `_phase2b_validation/UserSettings` | **已忽略** ← `.gitignore:7:[Uu]ser[Ss]ettings/` |
| `_phase2b_validation/_unity_temp` | **未忽略**（`_unity_temp` ≠ `temp`） |
| `_phase2b_validation/_unity_localappdata` | **未忽略** |
| `_phase2b_validation/ProjectSettings` | **未忽略** |
| `_phase2b_validation/Packages` | **未忽略** |
| `_phase2b_validation/Assets/Scripts/World/V2/WorldParameterRules.cs` | **未忽略** |

---

## 5. `git add -A` 风险分析（未实际执行）

在当前状态下执行 `git add -A` 会：

| 会纳入 | 数量 | 大小 |
|---|---:|---:|
| `_phase2b_validation/Assets/`（工程副本源码） | 783 文件 | **60.77 MB** |
| `_phase2b_validation/_unity_temp/`（含 73 个 `.dmp`） | 148 文件 | **20.97 MB** |
| `_phase2b_validation/` 一级散文件（含全部 round* 证据 + 历史日志/dll/ps1） | 295 文件 | **55.05 MB** |
| `_phase2b_validation/ProjectSettings/` | 24 文件 | 0.05 MB |
| `_phase2b_validation/Packages/` | 2 文件 | 0.01 MB |
| `_phase2b_validation/_unity_localappdata/` | 1 文件 | < 0.01 MB |
| **合计（= `-uall` 的 1253 条）** | **1253 文件** | **约 136.85 MB** |

| 不会纳入（被忽略） | 数量 | 大小 |
|---|---:|---:|
| `Library/` | 2156 文件 | 86.84 MB |
| `Logs/` + `UserSettings/` | 4 文件 | 0.03 MB |

**风险结论**：

1. **136.85 MB 会被直接入库**，其中 60.77 MB 是**工程副本源码**、20.97 MB 是**Unity 临时/崩溃转储**——两者都不属于"应入库的证据"。
2. 这些内容**当前不受任何 ignore 规则保护**（`Assets`、`_unity_temp`、散文件均未忽略）。
3. 唯一"幸运"的是 `Library/`（86.84 MB）与 `Logs/`、`UserSettings/` 因根 `.gitignore` 的任意深度匹配而逃过。
4. 另有 `_b3b_diff/`、`_finalization_backup/` 两个**同级**证据目录同样未跟踪；本轮未在统计范围内展开，但它们同样会被 `git add -A` 一并纳入（`_b3b_diff` 2 文件、`_finalization_backup` 12 文件 / 0.5 MB，据 R26 测量）。
5. 本轮**未执行** `git add`、未修改 `.gitignore`、未做任何清理。

---

## 6. 关键历史证据存在性清单（R10–R27）

逐项核对 32 个关键证据文件：**全部存在，缺失 0**。

| 轮次 | 关键证据 | 状态 |
|---|---|---|
| R10 | `round10_GeologyStageDValidation_raw.log` + `_rerun.log` | ✅ 存在 |
| R11 | `round11_ChunkStreamingValidation_raw.log` + `_rerun.log` | ✅ |
| R12 | `round12_GeologyRuntimeIntegrationValidation_raw.log` + `_rerun.log` | ✅ |
| R13 | `round13_GeologyStageDValidation_raw.log` + `_rerun.log` | ✅ |
| R14 | `round14_VoxelWorldRuntimeCoreValidation_raw.log` | ✅ |
| R15 | `round15_VoxelWorldRuntimeCoreValidation_rerun.log` | ✅ |
| R16 | `round16_hash_audit_rows.csv`（349 条）+ `round16_hash_dependency_audit.md` | ✅ |
| R17–R25 | 各轮 `*_rerun.log` + `hash_dependency_audit_delta.md`（9 轮 × 2） | ✅ 18/18 |
| R26 | `round26_final_diff_audit.md` | ✅ |
| R27 | `round27_document_consistency_audit.md` | ✅ |
| 进度主记录 | `sealevel_round7_progress.md`（**1032 行**，R7–R27） | ✅ |

**已知例外（据实报告，未重建）**：

- **R9 证据不在此目录**：`[WorldV2-TwoPhase] … failures=0（全部通过）` 的日志位于系统 TEMP（`C:\Users\xmz\AppData\Local\Temp\unity_r9_v2.log`，R26 实测存在，78750 B）。本目录内 R9 相关文件 = **0 个**。E5 已在 R26 报告登记，本轮仅确认现状，未复制、未重建。
- 各轮 `*_stdout.txt` / `*_stderr.txt` 全部为 **0 字节**（Unity 输出走 `-logFile`，管道为空），属**如实的空捕获**，不是损坏。

---

## 7. 项目副本与当前工作树的关键身份对比

**明确声明：副本是历史快照，不是权威版本；本轮未以副本覆盖任何当前文件。**

| 身份项 | 当前工作树 | 副本 `_phase2b_validation/` | 判定 |
|---|---|---|---|
| `ProjectSettings/ProjectVersion.txt` | `m_EditorVersion: 2022.3.62f1c1` / `(b0109b07edb8)` | 完全相同 | 同一 Unity 版本 |
| `Packages/manifest.json`（SHA256 前 16） | `68473770D0854A32` | `68473770D0854A32` | **完全相同** |
| `Assets` 规模 | 235 个 `.cs` / 820 文件 | 217 个 `.cs` / 783 文件 | 副本**更小** ⇒ 较早快照 |
| `Assets/Scripts/World/V2/` | 存在（World V2 参数层） | **不存在** | 副本早于 World V2 参数层 |
| `Assets/Scripts/VoxelWorld/Generation/WorldGenerationSettings.cs` | `7F11DEE8061E4FA1`（含 SeaLevel） | `140B7E289F05C6FA`（**SeaLevel 出现 0 次**） | 副本是 **SeaLevel 迁移之前**的版本（该哈希正是 R10–R24 修复过的旧登记值） |
| `Assets/Scripts/World/V2/WorldParameterRules.cs` | 存在（含 `settings.SeaLevel`） | **不存在** | 同上 |
| `Assets/Editor/*Validation.cs` | 6 个（含 R15–R27 修复后状态） | **45 个** | 副本含更多历史验证器文件，与当前目录结构不一致 |
| `Library/` 大小 | 95.7 MB | 86.8 MB | 副本为独立构建缓存，**不可混用** |

结论：副本是一份 **早期（World V2 参数层与 SeaLevel 迁移之前）的完整工程快照 + 其独立 Library**，可用于回溯历史状态，但**不能**当作当前工作树的权威副本，也不能与当前 `Library` 混用。

---

## 8. 保留建议（分三类）

> 本轮**不实施任何清理**。以下仅为建议，需明确批准后才可执行。

### 8.1 必须保留的原始证据（建议永久保留）

| 项 | 理由 |
|---|---|
| `sealevel_round7_progress.md`（1032 行） | R7–R27 唯一进度主记录；含每轮命令、退出码、日志路径、结论与遗留项 |
| `round10`–`round15` 的 `raw`/`rerun` `.log`（19 个 round* `.log`） | 7 个验证器**真实运行**的唯一原始日志证据；任何"PASS"必须以它们为凭 |
| `round16_hash_audit_rows.csv` | 349 条登记的逐项实测明细，是 R25「349/0/0 闭合」的原始依据，不可再生（依赖当时工作树状态） |
| `round16_hash_dependency_audit.md` + `round17`–`round25` delta（9 份） | 交叉依赖图的完整演化链；缺失将无法重建「哪一轮引入了哪条级联」 |
| `round26_final_diff_audit.md`、`round27_document_consistency_audit.md` | 全量差异审计与文档一致性修复记录 |
| `round*_stdout.txt` / `*_stderr.txt`（0 字节） | 虽为空，但"当时确为空"本身是证据（不可用事后文件替代） |

### 8.2 可考虑归档、但**必须经你批准**的文件

| 项 | 规模 | 建议与理由 |
|---|---|---|
| `_unity_temp/`（含 73 个 `.dmp`） | 148 文件 / 20.97 MB | Unity 崩溃转储与 TEMP 重定向产物；对当前结论无因果作用，但属"当时环境证据"。建议迁出仓库或压缩归档，**需批准** |
| 工程副本 `Assets/` + `ProjectSettings/` + `Packages/` | 783+24+2 文件 / 60.83 MB | 早期快照，可用于回溯；建议整体打包归档（保留 `ProjectVersion.txt` 与 `manifest.json` 以便识别），**需批准** |
| `Library/`（已被 gitignore） | 2156 文件 / 86.84 MB | 纯构建缓存，可由 Unity 重新生成；但属于副本完整性的组成部分。建议保留现状（不入库），若要释放磁盘需批准 |
| 非 round* 的历史回归日志（约 167 个 `.log`） | 约 46 MB | 属本会话之前的历史里程碑证据；与本轮 SeaLevel/哈希结论无直接因果，但可能是更早轮次的唯一证据。建议**先逐条判定归属轮次**再归档，**需批准** |
| `csc_*.dll`（13 个） | 1.86 MB | 独立编译检查产物（可重编译再生）；作为"曾经编译通过"的旁证价值有限。建议归档或删除前需批准 |
| 6 份 `*_phase_report.md` + `partial_solid_report.md` | 0.26 MB | 历史阶段性报告；建议保留（体积小、信息密度高） |
| `run_regression.ps1` | 0.01 MB | 回归批处理工具脚本；**用途明确**，建议保留（可能仍被引用） |

### 8.3 当前用途尚不明确 —— **不能建议清理**

| 项 | 不明之处 |
|---|---|
| 副本 `Assets/Editor/` 下的 **45 个 `*Validation.cs`** | 与当前工作树 6 个验证器的对应关系**未逐文件比对**；可能是历史验证器版本的集合，也可能是被删除验证器的唯一留档。在完成逐文件比对前，**不具备清理依据** |
| 副本 `Assets/` 中与当前工作树**不对应**的 18 个 `.cs` 差异（235 vs 217，且副本缺 `World/V2`） | 未做逐文件差异清单，无法判断哪些是"历史已删除文件" |
| `_b3b_diff/`、`_finalization_backup/`（同级目录） | 本轮未展开审计（不在统计范围），内容用途未核实 ⇒ 同样**不得假定可清理** |

---

## 9. 明确声明

- 本轮**没有修改、删除、移动、重命名或覆盖任何原有文件**。
- 未修改 `.gitignore`、生产代码、验证器、规范文档、进度记录。
- 未执行 `git add` / `commit` / `push` / `reset` / `clean`。
- 未运行任何 Unity 验证任务。
- 未因体积大、疑似重复或未被跟踪而认定任何文件可删除。
- 本轮唯一新增文件为本报告。

---

## 10. 交付附录

### 10.1 精确 `git status --short`

```
 M Assets/Editor/ChunkStreamingValidation.cs
 M Assets/Editor/GeologyRuntimeIntegrationValidation.cs
 M Assets/Editor/GeologyStageAValidation.cs
 M Assets/Editor/GeologyStageBValidation.cs
 M Assets/Editor/GeologyStageCValidation.cs
 M Assets/Editor/GeologyStageDValidation.cs
 M Assets/Editor/VoxelWorldRuntimeCoreValidation.cs
 M Assets/Scripts/Game/VoxelWorldStreamingBootstrap.cs
 M Assets/Scripts/VoxelWorld/Generation/WorldGenerationSettings.cs
 M Assets/Scripts/World/V2/WorldParameterEvaluator.cs
 M Assets/Scripts/World/V2/WorldParameterQuery.cs
 M Assets/Scripts/World/V2/WorldParameterRule.cs
 M Assets/Scripts/World/V2/WorldParameterRules.cs
 M Assets/Scripts/World/V2/WorldParameterState.cs
 M Specifications/World/WorldParameter_Contract.md
 M Specifications/World/WorldParameter_Source_Contract.md
?? Assets/Editor/WorldV2ParameterPipelineValidation.cs
?? Assets/Editor/WorldV2ParameterPipelineValidation.cs.meta
?? Assets/Scripts/World/V2/WorldStatePipeline.cs
?? Assets/Scripts/World/V2/WorldStatePipeline.cs.meta
?? _b3b_diff/
?? _finalization_backup/
?? _phase2b_validation/
```

### 10.2 精确 `git diff --stat`（末行）

```
 16 files changed, 352 insertions(+), 121 deletions(-)
```

### 10.3 本轮文件变更清单

| 变更 | 文件 |
|---|---|
| 新增 | `_phase2b_validation/round28_evidence_directory_audit.md`（本报告） |
| 修改 | **无** |
| 删除/移动 | **无** |
