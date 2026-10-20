# R36：GRI `BootstrapRig` 的 SeaLevel 配置对齐（获批最小实施）

- 授权范围：R35 §5 的两处最小修改，**仅限** `GeologyRuntimeIntegrationValidation.cs` 的测试装置 `BootstrapRig`。
- 未修改：验证断言、生产逻辑、`WorldGenerationSettings` 默认值/合法范围、其他验证器、注释体系、场景/Prefab/配置资产、哈希登记、进度记录。
- 未执行：commit / push / reset / clean。
- **结论边界**：本轮是**测试装置配置对齐**，**不是**新增 SeaLevel/Ocean 断言，**不是** World V2 生产端到端验证，**不得**据此宣称"非默认 SeaLevel 的地质行为已验证"。

---

## 1. 实施前检查（全部通过）

| 检查项 | 结果 |
|---|---|
| 目标文件与 R35 报告一致性 | **逐行一致**（L918 字段声明、L950 `new WorldGenerationSettings(`、L951–L952 六参实参、L954 `Bootstrap.Build()` 均匹配；比对结果 `True`） |
| 目标区是否已被既往修改覆盖 | **否**：`git diff` vs HEAD 共 5 个 hunk，全部位于哈希登记区（`@@ -550`、`@@ -711`、`@@ -721`、`@@ -747`、`@@ -766`），**无 hunk 覆盖 L938–L953** |
| 修改前文件哈希 | `0D12877FEACCCD60`（与 R25 交付一致，未被他轮改动） |
| 修改前行数 / `Check(` | 1144 / **54** |
| 修改前 `Bootstrap.SeaLevel =` 赋值次数 | **0** |
| 编码 / 换行 | BOM = False / **LF**（回写保持原编码与换行） |
| 修改前 git 状态 | status 25 条 / tracked `M` 18（快照已记录） |
| 修改前内容备份 | `%TEMP%\r36_gri_before.cs`（49155 B，用于最小改动证明） |

**目标行原文（缩进 12/16 空格，实证）**

```csharp
942:            Bootstrap.WorldHeight = 64;
943:            Bootstrap.TerrainFrequency = 0.06f;
944:            Bootstrap.TerrainAmplitude = 6f;
...
950:            GenerationSettings = new WorldGenerationSettings(
951:                Bootstrap.Seed, Bootstrap.TerrainHeight, Bootstrap.SoilDepth,
952:                Bootstrap.WorldHeight, Bootstrap.TerrainFrequency, Bootstrap.TerrainAmplitude);
```

---

## 2. 实施内容（两处，严格按授权）

### 修改 1：显式设置 `Bootstrap.SeaLevel`

```csharp
            Bootstrap.WorldHeight = 64;
            Bootstrap.SeaLevel = 64;              // ← 新增（第 1 处）
            Bootstrap.TerrainFrequency = 0.06f;
```

### 修改 2：参考设置改用七参数构造（末位 `Bootstrap.SeaLevel`）

```csharp
            GenerationSettings = new WorldGenerationSettings(
                Bootstrap.Seed, Bootstrap.TerrainHeight, Bootstrap.SoilDepth,
                Bootstrap.WorldHeight, Bootstrap.TerrainFrequency, Bootstrap.TerrainAmplitude, Bootstrap.SeaLevel);
```

> **实施决策（据实说明）**：R35 §5 的建议 diff 中含一句行尾注释（`// 显式声明依赖的默认值…`），本轮**未添加该注释**——因授权书明确"不得顺带修改…注释体系"，故只落两处**逻辑**改动。此取舍记录于此，供后续决定是否补注释。

---

## 3. 精确 diff

### 3.1 本轮新增的两个 hunk（相对 HEAD 的完整 diff 中，仅这两处属本轮）

```diff
@@ -942,2 +942,3 @@ public static class GeologyRuntimeIntegrationValidation
             Bootstrap.WorldHeight = 64;
+            Bootstrap.SeaLevel = 64;
             Bootstrap.TerrainFrequency = 0.06f;
@@ -951,3 +952,3 @@ public static class GeologyRuntimeIntegrationValidation
                 Bootstrap.Seed, Bootstrap.TerrainHeight, Bootstrap.SoilDepth,
-                Bootstrap.WorldHeight, Bootstrap.TerrainFrequency, Bootstrap.TerrainAmplitude);
+                Bootstrap.WorldHeight, Bootstrap.TerrainFrequency, Bootstrap.TerrainAmplitude, Bootstrap.SeaLevel);
```

### 3.2 最小改动证明（重建法）

把上述两处**回退**后重算 SHA256-16，结果 = **`0D12877FEACCCD60`**，与修改前**完全一致（`True`）** ⇒ 证明本轮**仅改动这两行**，其余 1144 行**逐字节未变**。

### 3.3 文件级统计

| 项 | 修改前 | 修改后 |
|---|---|---|
| 行数 | 1144 | **1145**（+1 行） |
| `Check(` 数量 | **54** | **54**（未增未减） |
| `Bootstrap.SeaLevel =` 赋值 | 0 | **1** |
| 文件哈希（SHA256-16） | `0D12877FEACCCD60` | **`3F0D4478A9399D1E`** |
| 相对 HEAD numstat | 14 / 14 | **16 / 15**（+2 / +1，差值即上述 2 行：1 新增行 + 1 行改写） |

---

## 4. 真实运行验证（Unity Batchmode）

| 项 | 值 |
|---|---|
| 入口 | `GeologyRuntimeIntegrationValidation.RunBatch()` |
| 命令 | `"D:\gamemaking\2022.3.62f1c1\Editor\Unity.exe" -batchmode -nographics -quit -projectPath "D:\gamemaking\My project" -logFile <TEMP>\unity_r36_gri_rerun.log -executeMethod GeologyRuntimeIntegrationValidation.RunBatch` |
| **进程退出码** | **0** |
| 耗时 | 40 s |
| **验证器 SUMMARY（原文）** | `[GeologyRuntimeIntegrationValidation] ===== SUMMARY =====`<br>`Checks = 45`<br>`Failures = 0`<br>`Result = PASS` |
| PASS / FAIL 行 | **45 / 0** |
| 入口执行证据 | 目标类标记行 **4** 条 |
| 崩溃 / 异常 | `Crash!!!` / `Aborting batchmode` / `Unhandled exception` 命中 **0** |
| 日志 | `_phase2b_validation/round36_GeologyRuntimeIntegrationValidation_rerun.log`（125386 bytes） |
| stdout / stderr | 各 **0 字节**（Unity 输出走 `-logFile`，管道为空——如实保留） |

### 4.1 编译错误与警告（含"是否真正重编译"判定）

| 项 | 值 |
|---|---|
| `error CS` | **0** |
| `warning CS` 行 | **4** = 既有 2 条唯一警告各重复 2 次：`VoxelCoordinateMigrationStageB3CValidation.cs(2130,50) CS0162`、`MiningSurfaceIntegrityValidation.cs(432,24) CS0414` ⇒ **无新增警告** |
| **是否触发重新编译** | **是**：日志含 4 条 `Csc`/编译相关行（本轮修改了 `Editor/*.cs`，Unity 必然重编译）⇒ 警告判定**有效**，非"因缓存未重编译而无法判断" |

### 4.2 关键断言（GE1 相关，全部 PASS）

```
PASS - GE1：组装成功（OK）
PASS - GE1：组装点注入的是 GeologyWorldGenerator（实际 GeologyWorldGenerator）
PASS - GE1：组装点不再实例化 BasicTerrainGenerator
PASS - GE1：地质参数已显式配置（配置入口就位）
PASS - GE1：Biome / Deposit provider 已装配（只读查询接口）
PASS - GE1：Generator 属性类型为 IWorldGenerator（抽象，不是具体类型）
PASS - GE1：地质开关来自显式字段（EnableCaves = True）
```

> GE3（与 Stage-1 基线差异）等其余断言亦全部 PASS（FAIL 行 = 0）。**注意**：`rig.GenerationSettings` 的唯一消费点 `BasicTerrainGenerator` 不读 `SeaLevel`（R35 已证），因此本轮 PASS **不能**作为"非默认 SeaLevel 行为正确"的证据。

---

## 5. 哈希登记复核

| 检查 | 结果 |
|---|---|
| GRI 自身在 7 张冻结表中的**登记次数** | **0** ⇒ 本轮修改**不触发任何重钉** |
| 本轮是否触碰任何**被登记文件** | **否**（仅改 GRI；GRI 不在任何登记表内） |
| 全局 349 条冻结哈希 | **349 条 / 失配 0 / 缺失 0**（独立重算，逐表逐项） |
| 是否需同步修改其他文件 | **无** |

---

## 6. 最终现场

| 项 | 值 |
|---|---|
| 分支 / HEAD | `main` / **`37246478ed354a1e9a0ce7a3a3d9457caf188062`**（未变） |
| staged | **0** |
| `git status --short` | **25 条**（tracked `M` **18**，与修改前一致；本轮为**内容修改**，未新增/删除文件） |
| `git diff --stat` 末行 | `18 files changed, 363 insertions(+), 122 deletions(-)`（R35 时为 361/121，差值恰为 **+2 插入 / +1 删除**，即上述 2 行） |
| 进度记录 | `sealevel_round7_progress.md` 仍 **1032 行**（未改） |
| 既有证据 | 未删除、未移动、未覆盖 |

---

## 7. 未解决项 / 明确不做的事

| # | 项 | 状态 |
|---|---|---|
| 1 | **非默认 SeaLevel 的覆盖** | **仍未实现**：本次仅让 rig 的参考设置**跟随** `Bootstrap.SeaLevel`，rig 仍固定为 64；要真正覆盖需另加断言（如把 `Bootstrap.SeaLevel` 设为 0 / WorldHeight 并断言 `Ocean` 分布），**本轮未做且未授权** |
| 2 | **`Ocean` 语义断言** | GRI 仍为 0 条（`Check(...SeaLevel...)` = 0、`Ocean` 出现 = 0） |
| 3 | R35 §5 建议的行尾注释 | **未添加**（授权限定不改注释体系）；如需补，另行批准 |
| 4 | 其他遗留（非本轮范围） | Elevation 等价验证复跑、`WPS56`/A1 Ocean 文档同步、GE7 `Runtime Core(12)` 文本专项、World V2 生产端到端（参数层仍无生产消费者）、全量 git diff 审计与提交评审 |

---

## 8. 本轮文件变更清单

| 变更 | 文件 | 说明 |
|---|---|---|
| 修改 | `Assets/Editor/GeologyRuntimeIntegrationValidation.cs` | **+2/−1 行**（1 行新增 + 1 行改写）；`Check(` 仍 54；哈希 `0D12877FEACCCD60` → `3F0D4478A9399D1E` |
| 新增（证据） | `_phase2b_validation/round36_GeologyRuntimeIntegrationValidation_rerun.log` | 125386 bytes |
| 新增（证据） | `_phase2b_validation/round36_GeologyRuntimeIntegrationValidation_stdout.txt` | 0 bytes（如实空捕获） |
| 新增（证据） | `_phase2b_validation/round36_GeologyRuntimeIntegrationValidation_stderr.txt` | 0 bytes（如实空捕获） |
| 新增（报告） | `_phase2b_validation/round36_geology_test_sealevel_alignment_report.md` | 本文件 |
| 删除 / 移动 / 覆盖历史证据 | **无** | — |
| 临时文件（项目外） | `%TEMP%\r36_gri_before.cs`、`%TEMP%\r36_gri_reverted.cs` | 仅用于最小改动证明，不在项目内 |

---

## 9. 结论

1. **两处获批修改已精确落地**：`Bootstrap.SeaLevel = 64;`（新增 1 行）与六参 → 七参构造（末位 `Bootstrap.SeaLevel`），**回退后哈希恢复为 `0D12877FEACCCD60`**，证明无其它改动。
2. **断言零变化**：`Check(` 仍 **54**；未新增/删除任何断言；验证器结构、生产逻辑、默认值、合法范围均未触碰。
3. **真实运行 PASS**：`exit 0`、`Checks = 45 / Failures = 0 / Result = PASS`、`error CS = 0`、**无新增警告**（且**确实重编译**，警告判定有效）、无崩溃。
4. **无哈希级联**：GRI 登记 0 次 ⇒ 无需重钉；全局 **349/0/0** 保持。
5. **本次修改的语义增益有限且明确**：只是让 rig 的参考设置**语义跟随** Bootstrap 配置，为将来覆盖非默认 SeaLevel 打开通道；**它本身不验证任何 SeaLevel/Ocean 行为**，也不构成 World V2 端到端验证。
