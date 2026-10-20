# R38：WorldSkeletonFacts 与 GRI 配置一致性断言（最小实施）

- 范围：仅**新增断言**（`Assets/Editor/WorldV2ParameterPipelineValidation.cs` + `Assets/Editor/GeologyRuntimeIntegrationValidation.cs`）；未改生产逻辑、`TryEvaluate` 签名、默认值、合法范围、Ocean 输出类型/比较符、HeightFunction、Region/Slope/地形、规范/脚本/`.gitignore`/进度记录。
- 未执行：commit / push / reset / clean / stash / checkout / `git add`。
- **结论边界**：本轮是**直接接口测试 + 测试装置一致性验证**，**不构成** World V2 生产端到端验证，**不证明**真实世界 Ocean 分布正确。

---

## 1. 实施前检查（全部通过）

| 检查 | 结果 |
|---|---|
| `WorldSkeletonFacts.TryEvaluate` 真实签名 | `public static bool TryEvaluate(WorldCoordinate position, WorldGenerationSettings settings, int seaLevel, out WorldSkeletonFacts facts)`（L94–L98）——**与 R37 一致，无冲突** |
| 返回契约 | 成员 `IsValid`(bool) / `Elevation`(int) / `Ocean`(bool) / `Continent`(int)；唯一失败条件 `position.Y < 0 \|\| Y >= settings.WorldHeight` ⇒ `facts = default; return false`（L101–L105）；`ocean = elevation < seaLevel`（L111）；`continent = ocean ? 0 : 1`（L114） |
| WSF 文件基线 | 哈希 `EE8627035B72FD16` / 121 行（本轮**未改动**） |
| 目标 A 基线 | `WorldV2ParameterPipelineValidation.cs`：`8BF57A66514F3286` / 336 行 / `Check(` = **29**；`WorldSkeletonFacts` 引用 = 0；插入锚点 `Report("World V2 参数两段式发布");` 唯一 |
| 目标 B 基线 | `GeologyRuntimeIntegrationValidation.cs`：`3F0D4478A9399D1E` / 1145 行 / `Check(` = **54**；R36 两行对齐仍在（2 处）；插入锚点 `GE1：Generator 属性类型…` 唯一 |
| **冻结表登记（实测，未复用旧结论）** | `WorldV2ParameterPipelineValidation` = **0**；`GeologyRuntimeIntegrationValidation` = **0** ⇒ **无哈希级联风险** |

---

## 2. 实施内容与精确 diff

### 2.1 目标 A：`WorldV2ParameterPipelineValidation.cs`（+111 行 / `Check(` 29 → 45，**+16**）

插入位置：D 组之后、`Report("World V2 参数两段式发布");` 之前（原 L252/L254 之间）。

新增 **E 组（WorldSkeletonFacts 查询级语义）16 条断言**，全部位于 `if (sample found)` 内（与既有 B1 组同构），每条只覆盖**单一失败原因**：

| # | 断言内容 | 覆盖的 R38 语义 |
|---:|---|---|
| E0 | 取得 Skeleton 查询用的**内部** Elevation 样本（运行时扫描 `0 < h < WorldHeight-1`，**不硬编码 23**） | 前置 |
| E1 | 海洋样本 `TryEvaluate` 返回 true **且**事实有效 | 区分 false/异常/有效事实 |
| E2 | `Elevation(23) < seaLevel(24) ⇒ Ocean = true` | 语义 3 |
| E3 | 海洋样本 `Continent == 0` | 语义 2 |
| E4 | `facts.Elevation` 等于同列 `HeightFunction` 结果 | 语义 7 |
| E5 | 相等边界样本 `TryEvaluate` 成功且有效 | 前置 |
| E6 | `Elevation == seaLevel ⇒ Ocean = false`（严格小于） | 语义 4 |
| E7 | 陆地样本 `Continent == 1` | 语义 1 |
| E8 | `seaLevel` 低于 Elevation 的样本成功且有效 | 前置 |
| E9 | `Elevation > seaLevel ⇒ Ocean = false` | 语义 5 |
| E10 | `Y = -1` 越界 ⇒ 返回 **false 且事实无效**（不钳制） | 语义 6（下界） |
| E11 | `Y = WorldHeight` 越界 ⇒ 返回 **false 且事实无效** | 语义 6（上界） |
| E12 | `default(WorldSkeletonFacts).IsValid == false` | 语义 6（无效状态） |
| E13 | 仅改 **`seaLevel` 入参** ⇒ `facts.Elevation` 不变 | 语义 8（入参维度） |
| E14 | 仅改 **`settings.SeaLevel`**（`slZero` vs `slUnset`）⇒ `facts.Elevation` 不变 | 语义 8（配置维度） |
| E15 | 非默认 `seaLevel = 32` 下逐列（35 列）核对 `Ocean == (h < 32)` 与 `Elevation == h` | 非默认值覆盖 + 逐列一致性 |

设计要点：
- **SeaLevel 使用非默认值**：`skElevation ± 1`（样本 23 ⇒ 24/22）、`32`；全部落在 `[0, WorldHeight]` 内。
- **失败原因不互相掩盖**：E2–E4 受 `if (oceanOk && oceanFacts.IsValid)` 保护；E6–E7、E9 同理；E13/E14 显式要求 `lowOk/oceanOk/sl0Ok` 成立，避免"默认结构体恰好不满足条件"造成的假通过。
- **未硬编码 23**：E0 通过 `HeightFunction` 扫描发现样本（实测本轮仍得 `23 @ (0,0)`）。

### 2.2 目标 B：`GeologyRuntimeIntegrationValidation.cs`（+8 行 / `Check(` 54 → 56，**+2**）

插入位置：`ValidateBootstrapUsesGeologyGenerator`（GE1）内，`bootstrap.IsBuilt` 早退守卫之后、`IWorldGenerator` 属性断言之后（原 L125 之后）。

```diff
                 "GE1：Generator 属性类型为 IWorldGenerator（抽象，不是具体类型）");
+
+            // R38：rig 参考设置与 Bootstrap 实际配置的一致性（仅当前默认配置；不宣称覆盖非默认 SeaLevel）
+            Check(rig.GenerationSettings.SeaLevel == rig.Bootstrap.SeaLevel,
+                "GE1：rig 参考设置 SeaLevel 与 Bootstrap 配置一致（" +
+                rig.GenerationSettings.SeaLevel + " / " + rig.Bootstrap.SeaLevel + "）");
+            Check(rig.Bootstrap.SeaLevel == WorldGenerationSettings.DefaultSeaLevel,
+                "GE1：Bootstrap.SeaLevel 等于登记默认值 DefaultSeaLevel（" +
+                rig.Bootstrap.SeaLevel + " / " + WorldGenerationSettings.DefaultSeaLevel + "）");
```

- rig 生命周期确认：`rig` 在 `using (BootstrapRig rig = new BootstrapRig("GE1"))` 内（L103），构造器已完成 `Bootstrap.SeaLevel = 64`（R36 L943）与七参 `GenerationSettings` 构造（R36 L953），且 `Bootstrap.Build()` 已执行 ⇒ **断言时对象已正确初始化**。
- 断言位于 `if (!bootstrap.IsBuilt) return;`（L108）**之后** ⇒ 仅在组装成功时求值。

---

## 3. 两个验证器真实运行结果（Unity Batchmode）

### 3.1 `WorldV2ParameterPipelineValidation.RunBatch`

| 项 | 值 |
|---|---|
| 命令 | `"D:\gamemaking\2022.3.62f1c1\Editor\Unity.exe" -batchmode -nographics -quit -projectPath "D:\gamemaking\My project" -logFile <TEMP>\unity_r38_WorldV2ParameterPipelineValidation.log -executeMethod WorldV2ParameterPipelineValidation.RunBatch` |
| **退出码** | **0**（27 s） |
| 摘要（原文） | `[WorldV2-TwoPhase] World V2 参数两段式发布 ⇒ failures=0（全部通过）` |
| **PASS / FAIL 行** | **45 / 0** |
| 目标方法执行证据 | 日志含 `E0`–`E15` 全部 16 条 `[WorldV2-TwoPhase] PASS:` 行 |
| `error CS` | **0** |
| `warning CS` 行 | **4** = 既有 2 条唯一警告各重复 2 次（`CS0162` / `CS0414`）⇒ **无新增** |
| 是否真实重编译 | **是**（5 条 `Csc`/编译行）⇒ 警告判定**有效** |
| 日志 | `_phase2b_validation/round38_WorldV2ParameterPipelineValidation_rerun.log`（115247 B）；stdout/stderr 各 **0 B** |
| 崩溃/异常 | **0** |

E 组实测输出（节选，全部 PASS）：
```
E0：取得 Skeleton 查询用的内部 Elevation 样本（23 @ 0,0）
E1：海洋样本 TryEvaluate 返回 true 且事实有效（ok=True valid=True）
E2：Elevation(23) < seaLevel(24) ⇒ facts.Ocean = true
E3：海洋样本 Continent = 0（实际 0）
E4：facts.Elevation 等于该列 HeightFunction 结果（23 vs 23）
E5：相等边界样本 TryEvaluate 返回 true 且事实有效（ok=True valid=True）
E6：Elevation == seaLevel ⇒ facts.Ocean = false（严格小于，不带等号）
E7：陆地样本 Continent = 1（实际 1）
E8：seaLevel 低于 Elevation 的样本 TryEvaluate 返回 true 且事实有效（ok=True valid=True）
E9：Elevation(23) > seaLevel(22) ⇒ facts.Ocean = false
E10：Y = -1 越界 ⇒ 返回 false 且事实无效（ok=False valid=False）
E11：Y = WorldHeight 越界 ⇒ 返回 false 且事实无效（ok=False valid=False）
E12：default(WorldSkeletonFacts).IsValid = false
E13：改变 seaLevel 入参不改变 facts.Elevation（low=23 / ocean=23 / expected=23）
E14：改变 settings.SeaLevel 不改变 facts.Elevation（sl0=23 / sl64=23）
E15：seaLevel = 32（非默认）逐列 Ocean/Elevation 与 HeightFunction 一致（列数 35，mismatch=0）
```

### 3.2 `GeologyRuntimeIntegrationValidation.RunBatch`

| 项 | 值 |
|---|---|
| 命令 | `… -logFile <TEMP>\unity_r38_GeologyRuntimeIntegrationValidation.log -executeMethod GeologyRuntimeIntegrationValidation.RunBatch` |
| **退出码** | **0**（21 s） |
| 摘要（原文） | `Checks = 47` / `Failures = 0` / `Result = PASS` |
| **PASS / FAIL 行** | **47 / 0** |
| 新增断言实测输出 | `PASS - GE1：rig 参考设置 SeaLevel 与 Bootstrap 配置一致（64 / 64）`<br>`PASS - GE1：Bootstrap.SeaLevel 等于登记默认值 DefaultSeaLevel（64 / 64）` |
| `error CS` | **0** |
| `warning CS` 行 | **0** |
| 是否真实重编译 | **否（缓存命中）**：仅 2 条 `CopyFiles …Assembly-CSharp-Editor.dll` 行、无 `Csc` 编译行（因 V2 轮已编译同一 Editor 程序集）⇒ **本轮 GRI 无法据警告输出判断"无新增警告"**；权威依据是同日 V2 轮的真实重编译（4 行既有警告、无新增） |
| 日志 | `_phase2b_validation/round38_GeologyRuntimeIntegrationValidation_rerun.log`（121247 B）；stdout/stderr 各 **0 B** |
| 崩溃/异常 | **0** |

---

## 4. 哈希与冻结表

| 检查 | 结果 |
|---|---|
| `WorldV2ParameterPipelineValidation` 在 7 张冻结表登记次数（**实施后复测**） | **0** |
| `GeologyRuntimeIntegrationValidation` 同上 | **0** |
| 是否重钉任何哈希 | **未做**（无登记 ⇒ 无需也不允许改） |
| 全局 349 条冻结哈希 | **349 条 / 失配 0 / 缺失 0** 独立重算 |
| 是否出现哈希级联 | **无** |

---

## 5. 现场核验

| 项 | 值 |
|---|---|
| 分支 / HEAD | `main` / **`37246478ed354a1e0ce7a3a3d9457caf188062`**（未变） |
| staged | **0** |
| `git status --short` | **25 条**（tracked `M` **18**、untracked 源文件 **4**、证据目录 3）——与 R37 结束时**条目数相同**（本轮为内容修改，未新增/删除文件） |
| `git diff --stat` 末行 | `18 files changed, 371 insertions(+), 122 deletions(-)`（R37 为 363/122，差值 **+8** 即 GRI 新增行；V2 文件为 untracked，不计入） |
| 目标文件最终哈希 | V2 = `2362602F9ABB194A`（447 行 / `Check(` 45）；GRI = `E3DE56D14D75F010`（1153 行 / `Check(` 56） |
| 进度记录 | 仍 **1032 行**（未改） |
| `.gitignore` / 规范 / `run_regression.ps1` | 未修改 |
| 既有证据 | 未删除 / 移动 / 覆盖（R36 及以前全部保留） |
| 未 add 任何项目副本 / Library / 临时文件 | 确认：staged = 0 |

### 5.1 本轮新增 / 修改清单

| 变更 | 文件 | 说明 |
|---|---|---|
| 修改（新增断言） | `Assets/Editor/WorldV2ParameterPipelineValidation.cs` | +111 行，`Check(` 29 → **45**（+16），untracked `??` |
| 修改（新增断言） | `Assets/Editor/GeologyRuntimeIntegrationValidation.cs` | +8 行，`Check(` 54 → **56**（+2），tracked `M` |
| 新增（证据） | `round38_WorldV2ParameterPipelineValidation_rerun.log` + `.stdout.txt` + `.stderr.txt` | 115247 B / 0 B / 0 B |
| 新增（证据） | `round38_GeologyRuntimeIntegrationValidation_rerun.log` + `.stdout.txt` + `.stderr.txt` | 121247 B / 0 B / 0 B |
| 新增（报告） | `round38_worldskeleton_sealevel_validation_report.md` | 本文件 |
| 删除 / 移动 / 覆盖 | **无** | — |

---

## 6. 结果性质区分（务必严格）

| 结果 | 性质 | 说明 |
|---|---|---|
| E0–E15（V2，16 条） | **直接接口测试（查询级）** | 直接调用 `WorldSkeletonFacts.TryEvaluate` 验证其自身契约；该 API **当前无生产调用者**，故**不构成生产集成证据** |
| GE1 两条 rig 一致性断言 | **测试装置一致性验证** | 仅验证 `BootstrapRig` 的参考设置与 Bootstrap 配置一致、且等于 `DefaultSeaLevel`（当前默认 64）；**不宣称覆盖非默认 SeaLevel** |
| A–D 组（既有 17+4 条） | 规则/求值器级测试 | 保持原样，未修改 |
| 真实世界 Ocean 分布 | **未验证** | 参数层无生产消费者；本轮不改变这一状态 |

---

## 7. 未做 / 未解决（据实）

| # | 项 | 状态 |
|---|---|---|
| 1 | `OceanRule` 的 Elevation 缺失异常路径 | **未测**（规则类 `private sealed`；按契约 R38 §二.7 不改可见性） |
| 2 | `WorldSkeletonFacts.seaLevel` 独立入参是否应改为读 `settings.SeaLevel` | **未改**（契约决策，`G-2`） |
| 3 | Ocean 的 `float` 表示法（`PM32` 登记缺口） | **未裁决**；本轮仅断言恰为 0f/1f 语义 |
| 4 | 生产接入 `TryEvaluate`（新增生产调用点） | **未做**（本轮明令禁止） |
| 5 | 真实世界 Ocean 分布 / 端到端 | **未验证**（需生产接入后另立轮） |
| 6 | GRI 轮因缓存未重编译 | 已说明：警告结论以 V2 轮真实重编译为准 |

---

## 8. 结论

1. **两处缺口已补足并真实运行通过**：V2 新增 **16 条** 查询级断言（`Check(` 29→45，PASS **45/0**，`failures=0`）；GRI 新增 **2 条** rig 一致性断言（`Check(` 54→56，`Checks = 47 / Failures = 0`）。两者退出码均 **0**、`error CS = 0`、**无崩溃**。
2. **未修改任何生产逻辑/契约/签名/默认值/范围**；`WorldSkeletonFacts.cs` 未被触碰（哈希 `EE8627035B72FD16` 与实施前一致）。
3. **无哈希级联**：两目标文件在 7 张冻结表登记均为 **0**（实施前后各测一次）；全局 **349/0/0** 保持。
4. **现场干净**：HEAD 未变、staged = 0、进度记录 1032 行、`git status` 条目数与 R37 相同（18 `M` / 4 untracked 源文件）。
5. **边界声明**：E 组是**直接接口测试**（该 API 无生产调用者），GRI 两条是**装置一致性验证**；本轮**不构成** World V2 生产端到端验证，**不证明**真实世界 Ocean 分布正确。
