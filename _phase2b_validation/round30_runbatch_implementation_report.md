# R30：`WorldV2ParameterPipelineValidation` 增加统一批处理入口 `RunBatch()`（最小代码修改）

- 修改范围：**仅** `Assets/Editor/WorldV2ParameterPipelineValidation.cs`
- 现场：分支 `main`，HEAD `37246478ed354a1e9a0ce7a3a3d9457caf188062`（未变），staged = 0
- 未修改：验证断言、`Validate()` 验证主体、`ValidateOrThrow()` 语义、规范、其他验证器、`run_regression.ps1`、Stage C/D/GRI 入口清单、`.gitignore`、任何历史证据
- 未执行：commit / push / reset / clean

---

## 1. 实施前检查（逐项实测）

| 检查项 | 实测结果 | 结论 |
|---|---|---|
| `failures` 声明位置与作用域 | `private static int failures;`（**L29**，全文件唯一声明） | **静态字段**，`RunBatch()` 可直接引用；不依赖 `Validate()` 的局部变量 ✓ |
| `ValidateOrThrow()` 失败时抛异常 | L261–266：`if (failures > 0) throw new InvalidOperationException("World V2 参数两段式发布验证失败：" + failures + " 项不通过。")` | 失败必抛 ✓ |
| `ValidateOrThrow()` 成功时正常返回 | L267：`}` 直接返回，无 `Exit`、无 `return` 提前退出 | 成功正常返回 ✓ |
| `Validate()` 的异常行为 | 失败时**仅** `Debug.LogError("[WorldV2-TwoPhase] FAIL: " + label)` + `failures++`（L317 区域）与 `Report()` 汇总 LogError（L333 区域）；**不抛异常** | ⇒ 若只调 `Validate()` 再 `Exit(0)`，会把失败吞成成功 —— **本次未采用该结构** |
| `UnityEditor` 引用 | L4 `using UnityEditor;` 已存在（用于 `[MenuItem]`）⇒ `EditorApplication` 可用 | 无需新增 using ✓ |
| `System` 异常类型 | L1 `using System;` 已存在 | `Exception` 可直接用，**未新增重复引用** ✓ |

---

## 2. 修改前后精确 diff

该文件为 **untracked**（`?? Assets/Editor/WorldV2ParameterPipelineValidation.cs`），`git diff` 对其为空；因此以 **R29 基线快照哈希** 作为对照，并用「重建基线」方式做逐字节证明（见 §3）。

**基线**：304 行 / 30 处 `Check(`（1 声明 + 29 调用）/ SHA256-16 = `160ED407F6558E9F`
**修改后**：336 行 / 30 处 `Check(`（1 声明 + 29 调用）/ SHA256-16 = `8BF57A66514F3286`

### Hunk 1 —— 文件头批处理入口声明（原 L23，1 行 → 2 行）

```diff
 /// 菜单：`Tools/World V2/Validate Parameter Two-Phase Publishing`
-/// 批处理：`Unity -batchmode -quit -executeMethod WorldV2ParameterPipelineValidation.ValidateOrThrow`
+/// 批处理（统一入口）：`Unity -batchmode -quit -executeMethod WorldV2ParameterPipelineValidation.RunBatch`
+/// 批处理（历史入口，保留兼容）：`Unity -batchmode -quit -executeMethod WorldV2ParameterPipelineValidation.ValidateOrThrow`
 ///
 /// 本脚本**不修改任何运行时数据**、不触碰场景与资产、不写入文件。
```

### Hunk 2 —— 在 `ValidateOrThrow()` 之后、`Clamp01()` 之前新增方法（+31 行，插入于 L270–300）

```csharp
    /// <summary>
    /// **统一批处理入口**（与其余验证器形式一致）：以进程退出码表示成败。
    ///
    /// 路径语义：
    ///   · 复用既有唯一验证主体 —— 调用 <see cref="ValidateOrThrow"/>（它内部执行 <see cref="Validate"/>）；
    ///   · 成功 ⇒ `EditorApplication.Exit(0)`；
    ///   · 失败 ⇒ `ValidateOrThrow` 抛异常 ⇒ 记录**一行**汇总后 `Exit(1)`。
    ///
    /// 为什么**不**直接 `Validate()` 后无条件 `Exit(0)`：`Validate()` 失败时只调用 `Debug.LogError`
    /// 并累加 `failures`，**不抛异常**，直接 `Exit(0)` 会把失败吞成成功。
    ///
    /// 异常覆盖：`catch (Exception)` 位于 `Exit(0)` 之前 ⇒ **任何**验证期异常（含 `InvalidOperationException`
    /// 与意外运行时异常）都不可能落入退出码 0。逐条失败项仍由 `Validate()` 内的
    /// `Check`/`Report` 输出（`Debug.LogError` 每条 + 汇总），本入口只补一行含失败计数的摘要，不重复刷日志。
    /// </summary>
    public static void RunBatch()
    {
        try
        {
            ValidateOrThrow();
        }
        catch (Exception exception)
        {
            Debug.LogError("[WorldV2-TwoPhase] 批处理失败（exit 1）：" + exception.Message);
            EditorApplication.Exit(1);
            return;
        }

        EditorApplication.Exit(0);
    }
```

行数核算：`304 + 1（Hunk1）+ 31（Hunk2）= 336` ✓ 与实测一致。

---

## 3. 「除两个 hunk 外逐字节未变」的证明（因文件 untracked）

方法：读入当前文件（BOM=False / 换行=LF / 17164 B），移除新增的 31 行块、把 Hunk 1 的 2 行还原为原 1 行，按相同编码与换行重写，比对 SHA256。

| 结果 | 值 |
|---|---|
| 重建后行数 | 305（= 原 304 行 + 末尾空段） |
| 重建后 SHA256-16 | **`160ED407F6558E9F`** |
| 与 R29 基线一致 | **True** |

⇒ **原有 304 行（含 `Validate()` 的全部验证逻辑与 29 条 `Check()` 调用、`ValidateOrThrow()`、`MenuItem`、`Report`/`Check`/`Clamp01` 助手）逐字节未变**，本轮改动严格限于上述两个 hunk。

### 入口完整性复核

| 入口 | 修改前 | 修改后 |
|---|---|---|
| `[MenuItem("Tools/World V2/…")]` | L31 | **L32**（仍在） |
| `public static void Validate()` | L32 | **L33**（仍在） |
| `public static void ValidateOrThrow()` | L257 | **L258**（仍在，语义未改） |
| `public static void RunBatch()` | 无 | **L285（新增）** |
| `Check(` 计数 | 30（1 声明 + 29 调用） | **30（1 声明 + 29 调用）** ✓ |

---

## 4. 入口实现与异常处理理由

| 设计决策 | 理由 |
|---|---|
| 调用 `ValidateOrThrow()` 而非 `Validate()` | `Validate()` 失败**不抛异常**，其后若 `Exit(0)` 会把失败误报为成功（本轮明令禁止该结构）；`ValidateOrThrow()` 是既有唯一能"把失败变成可捕获信号"的入口，**复用而非新增验证路径**（不产生第二套断言） |
| `catch (Exception)` 而非仅 `catch (InvalidOperationException)` | 要求「任何验证异常都不能落入退出码 0」。验证期除 `InvalidOperationException` 外还可能出现意外运行时异常（如空引用），窄捕获会让这些异常逃出 `try` ⇒ 退出码不确定。广捕获保证**唯一**到 `Exit(0)` 的路径是"`ValidateOrThrow()` 正常返回" |
| `catch` 之后 `Exit(1)` + `return;` | `EditorApplication.Exit` 在 batchmode 下请求退出，`return` 确保不继续执行到 `Exit(0)` |
| 只加**一行** `Debug.LogError` | 逐条失败项已由 `Validate()` 内 `Check()` 逐条 `Debug.LogError`，汇总已由 `Report()` 输出；`ValidateOrThrow` 抛出的异常消息（含失败计数）此前**从未被记录**，补这一行才不丢信息；未重复刷全部失败项 |
| 保留 `ValidateOrThrow()` | R9 证据引用的就是它（进度记录 L47 / 日志实证）；删除会使历史运行指令不可复现 |
| 未改动 `failures` 重置/累加逻辑 | `Validate()` 首行 `failures = 0;`（L34 区域）保持不变，多次调用语义不变 |

---

## 5. 成功路径真实运行结果（Unity Batchmode）

| 项 | 值 |
|---|---|
| 命令 | `D:\gamemaking\2022.3.62f1c1\Editor\Unity.exe -batchmode -nographics -quit -projectPath "D:\gamemaking\My project" -logFile <TEMP>\unity_r30_runbatch.log -executeMethod WorldV2ParameterPipelineValidation.RunBatch` |
| **进程退出码** | **0** |
| 执行时长 | 17 s |
| 日志 | `_phase2b_validation/round30_RunBatch_execution.log`（100927 bytes）；stdout/stderr 各 0 字节 |
| `[WorldV2-TwoPhase] PASS:` 行 | **29** |
| `[WorldV2-TwoPhase] FAIL:` 行 | **0** |
| 汇总行（原文） | `[WorldV2-TwoPhase] World V2 参数两段式发布 ⇒ failures=0（全部通过）` |
| `批处理失败（exit 1）` 标记行 | **0**（未进入失败分支，符合预期） |
| 编译错误 | `error CS = 0` |
| 编译警告 | 4 行 = **2 条唯一警告各重复 2 次**：`VoxelCoordinateMigrationStageB3CValidation.cs(2130,50) CS0162`、`MiningSurfaceIntegrityValidation.cs(432,50 → 432,24) CS0414`（与 R9–R27 同源，**无新增警告**） |

> 29 PASS 与 30 处 `Check(` 的差额已核实：30 = **1 处方法声明**（`private static void Check(bool, string)`，现 L308）+ **29 处调用**。与 R9 记录「PASS 29 / FAIL 0」完全一致，**非断言缺失**。

---

## 6. 失败路径是否经过动态测试

**没有。** 本轮只验证了**成功路径**（`failures == 0` ⇒ `ValidateOrThrow()` 正常返回 ⇒ `Exit(0)`，实测退出码 **0**）。

- 失败分支（`catch` → `Debug.LogError` → `EditorApplication.Exit(1)`）在本轮**未被触发**，其退出码**未经过动态验证**；日志中 `批处理失败（exit 1）` 出现次数为 0 即为证据。
- 因此本轮**不宣称**"失败退出码已验证"。构造失败用例（如临时注入不满足的断言条件）需另立一轮，且不得修改验证断言或生产代码——建议通过**外部临时手段**（如一次性构造 `WorldGenerationSettings` 非法配置的独立脚本）在受控范围内进行，需你批准。

---

## 7. 哈希登记影响

| 项 | 结果 |
|---|---|
| 7 个冻结验证器登记表对 `WorldV2ParameterPipelineValidation` 的登记项 | **0**（本轮实测复核，仍为 0；未修改任何登记表） |
| 349 条冻结哈希整体 | **349 / 失配 0 / 缺失 0**（本轮独立重算，与 R25 闭合结果一致 ⇒ 本轮未破坏闭合） |
| 是否触发级联 | **否**（该文件未被任何冻结表登记） |

---

## 8. 最终现场

### 8.1 `git status --short`（23 条，与 R28/R29 一致；目标文件仍为 `??`）

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

- **staged = 0**
- `git diff --stat` 末行：`16 files changed, 352 insertions(+), 121 deletions(-)`（与 R29 相同——目标文件为 untracked，不计入 `git diff`）
- 目标文件状态：`?? Assets/Editor/WorldV2ParameterPipelineValidation.cs`（内容已修改，仍 untracked）

### 8.2 本轮文件变更清单

| 变更 | 文件 | 说明 |
|---|---|---|
| **修改** | `Assets/Editor/WorldV2ParameterPipelineValidation.cs` | +32 行（1 行文档 + 31 行新方法）；`Check(` 仍 30；哈希 `160ED407F6558E9F` → `8BF57A66514F3286` |
| 新增（证据） | `_phase2b_validation/round30_RunBatch_execution.log`（100927 B） | Unity 真实运行日志 |
| 新增（证据） | `_phase2b_validation/round30_RunBatch_stdout.txt`（0 B） | 如实空捕获 |
| 新增（证据） | `_phase2b_validation/round30_RunBatch_stderr.txt`（0 B） | 如实空捕获 |
| 新增（报告） | `_phase2b_validation/round30_runbatch_implementation_report.md` | 本文件 |
| 删除 / 移动 / 覆盖 | **无** | 未删除、未移动、未覆盖任何历史证据 |

---

## 9. 结论

1. **入口形式不一致已解决**：目标验证器现提供 `public static void RunBatch()`，与其余 45 个验证器命名一致，且以 `EditorApplication.Exit(0/1)` 表达退出码。
2. **改动严格最小**：仅 2 个 hunk，共 +32 行；原有 304 行**逐字节未变**（重建基线哈希等于基线 `160ED407F6558E9F` 已证）。
3. **成功路径已动态验证**：`-executeMethod …RunBatch` 真实执行，退出码 **0**，PASS 29 / FAIL 0，`failures=0（全部通过）`，编译错误 0，无新增警告。
4. **失败路径未动态验证**，本报告不宣称其退出码已确认。
5. **无哈希级联**：目标文件仍未见于任何冻结登记表；349 条冻结哈希仍为 0 失配 / 0 缺失。
6. 未触碰验证断言、其他验证器、回归脚本、Stage C/D/GRI 入口清单、`.gitignore` 或任何证据。
