# 第二十七轮：SeaLevel 规范文档一致性修复（仅文档）

- 修改范围：**仅两份 Markdown 规范文档**（未修改任何 C# / 验证器 / 生产代码 / 测试入口 / Git 忽略规则 / 证据目录）
- 分支：`main`　HEAD：`37246478ed354a1e9a0ce7a3a3d9457caf188062`（未变）
- 未执行：commit / push / reset / clean；未覆盖任何历史证据；**未修改** `sealevel_round7_progress.md`
- 本轮**不宣称**任何动态验证或端到端验证完成

---

## 1. 现场（修改前后）

| 项 | 修改前 | 修改后 |
|---|---|---|
| 分支 / HEAD | `main` / `37246478…` | 同（未变） |
| `git diff --cached` | 0 个 staged | 0 个 staged |
| tracked `M` | 16 | **16**（数量不变，仅 2 个 `.md` 的内容变化） |
| 本轮改动的文件 | — | `Specifications/World/WorldParameter_Contract.md`、`Specifications/World/WorldParameter_Source_Contract.md` |
| untracked 源文件 / 证据目录 | 4 / 3 | 4 / 3（未变） |

`git status --short` 中 `.md` 行仅两条：
```
 M Specifications/World/WorldParameter_Contract.md
 M Specifications/World/WorldParameter_Source_Contract.md
```

---

## 2. 精确 diff

### 2.1 本轮增量（逐行）

**`WorldParameter_Contract.md`（1 行，F1）**

```diff
-  OK WPC§5.3 # World Parameter Contract V1
+# World Parameter Contract V1
```

> 该行修复后与 `HEAD` 内容**逐字节相同**，因此**不再出现在**相对 HEAD 的累计 diff 中（累计 numstat 仅剩第 5 轮追加的说明段落：`2 / 0`）。

**`WorldParameter_Source_Contract.md`（10 行，F1 + F2 + F3）**

```diff
-  OK 索引表   OK WPS55   OK WPS54a   OK 临时性质(纯文本)   OK 临时性质(加粗)   OK 占位清单来源   OK A1来源 # WorldParameter Source Contract V1
+# WorldParameter Source Contract V1

-> - 求值框架 `WorldParameterEvaluator.Evaluate` 已实现，按固定规则表生成 8 项参数快照；Region Modifier 当前恒为 `0`。
+> - 求值框架 `WorldParameterEvaluator.Evaluate` 已实现，按固定规则表生成 8 项参数的 **Base 段**快照（`IsFinalized == false`，Base 段 Modifier 恒为 `0`）；**Final 段**由 `WorldParameterEvaluator.Finalize` 合成，其 Region 修正量当前取自**占位实现**（`RegionModifierDetermination`，区间 `[0, 0.25)`，`PlaceholderAmplitude = 0.25f`；真实修正规则尚未冻结）。

-> - `Elevation` 仍是域哈希占位（高度跨度硬编码为 `128`），**尚未接入** `HeightFunction.GetTerrainHeight`。
+> - `Elevation` **已接线**：`ElevationRule` 直接只读转发 `HeightFunction.GetTerrainHeight(position.X, position.Z, settings)`（整数体素，域依赖已移除；`ProvisionalWorldHeight` 已删除，`WPS55`/`WPS57` 达成）。

-> - `Ocean` 的 `Elevation < SeaLevel` 派生形式已实现，但 `SeaLevel` 仍硬编码为 `64`，且输入 `Elevation` 仍是占位值。
+> - `Ocean` 的 `Elevation < SeaLevel` 派生形式已实现：`OceanRule` 以**严格小于**读取 `WorldGenerationSettings.SeaLevel`（配置，世界创建期），**不再硬编码 64**；`64` 仅是契约表 C 的**设计默认值**（代码常量 `WorldGenerationSettings.DefaultSeaLevel`，供六参数构造兼容旧调用）。`ProvisionalSeaLevel` 已删除。

-| `Ocean`（8） | … | **派生已按契约实现**：`Elevation < SeaLevel`（`SeaLevel` 取登记默认值 `64`） | **派生形式非临时**；唯一临时量 = `SeaLevel` 常量（真实值来自**显式配置**） | 步骤 1：`Elevation < SeaLevel（配置读取）`；… |
+| `Ocean`（8） | … | **派生已按契约实现（含配置读取）**：`OceanRule` 读取 `settings.SeaLevel`；设计默认值 `64` = `DefaultSeaLevel` | **非临时**：派生形式与配置读取均已落地；`ProvisionalSeaLevel` 已删除 | 步骤 1 **已达成**；布尔语义**表示法**属登记缺口，本契约不裁决 |

-| `Elevation` | **只读高度场**（未接入）；骨架大陆掩码（未接入）；… |
+| `Elevation` | **只读高度场**（已接入：`HeightFunction.GetTerrainHeight`）；骨架大陆掩码（未接入）；… |

-| `Elevation` | 域 1 标签整数哈希 → `[0,127]` | 占位 + 临时域标签 | …**本轮仅更新计划与契约状态：代码接线未完成**（实现仍为域 1 占位） |
+| `Elevation` | `HeightFunction.GetTerrainHeight(...)`（整数体素） | 已接线（真实高度场） | …**代码接线已完成**（`ElevationRule` 调用 `HeightFunction.GetTerrainHeight(x, z, settings)`；`ProvisionalWorldHeight` 已删除）。**仍待完成**：World V2 参数结果接入生成运行时（生产调用点 = 0，见 `WPS54a`） |

-| `Ocean` | `Elevation < 64` | **非临时**（派生形式已按契约）+ 临时常量 `SeaLevel` | `SeaLevel` 改为显式配置读取 |
+| `Ocean` | `Elevation < settings.SeaLevel`（`WorldGenerationSettings` 配置） | **非临时**（派生形式 + 配置读取均已落地） | 显式配置读取**已达成**（`ProvisionalSeaLevel` 已删除；`64` = 设计默认值）；**仍待完成**：生成运行时消费 |

-| **WPS56** | `SeaLevel` 占位值 `64` | 硬编码常量 | **登记默认值**（契约表 C `SeaLevel` 默认 `64`） | 改为显式配置读取（值本身可不变） |
+| **WPS56** | `SeaLevel` 占位值 `64` | **已达成**：`ProvisionalSeaLevel` 已删除，`OceanRule` 读取 `settings.SeaLevel` | **登记默认值**（契约表 C `SeaLevel` 默认 `64`；代码常量 `WorldGenerationSettings.DefaultSeaLevel = 64`，仅作六参数构造的兼容默认） | **已达成**：显式配置读取落地（值本身不变）；**未完成**：World V2 参数结果尚未被生成运行时消费（生产调用点 = 0） |

-| **WPS57** | … | … | …**该条记录对应的历史轮次仅更新计划与契约状态，未修改实现** |
+| **WPS57** | … | … | …**实现状态（已更正）**：域标签已在实现中移除 —— `ElevationRule` 改为只读转发 `HeightFunction.GetTerrainHeight(position.X, position.Z, settings)`，`ProvisionalWorldHeight` 已删除；§6.4 登记未变更；**仍待完成**：生成运行时消费（生产调用点 = 0） |

-| `Ocean` | 同上 → `OceanRule` | 契约派生（`Elevation < 64`） | 表 A1 行 8；§5.1 行 5；`WPS56` |
+| `Ocean` | 同上 → `OceanRule` | 契约派生（`Elevation < settings.SeaLevel`；设计默认值 `64`） | 表 A1 行 8；§5.1 行 5；`WPS56` |
```

### 2.2 相对 HEAD 的累计 numstat

| 文档 | 累计 numstat | 说明 |
|---|---|---|
| `WorldParameter_Contract.md` | `2 / 0` | 仅第 5 轮追加的说明段落；**本轮 F1 修复使该行回到 HEAD 原样，故不计入** |
| `WorldParameter_Source_Contract.md` | `13 / 13` | 含第 5 轮 4 处（表 A1 `Elevation`、`WPS54a`、`WPS55`、附录 C `Elevation`）+ 本轮 10 处（其中 `§5.1 Elevation` 行在第 5 轮已改过，故计入一次） |

---

## 3. F1：标题与索引污染

| 文档 | 污染内容（修复前首行） | 修复后 | 依据 |
|---|---|---|---|
| `WorldParameter_Contract.md` | `  OK WPC§5.3 # World Parameter Contract V1` | `# World Parameter Contract V1` | `git show HEAD:` 原始首行**逐字节相同**，故可**可靠还原**（非猜测） |
| `WorldParameter_Source_Contract.md` | `  OK 索引表   OK WPS55   OK WPS54a   OK 临时性质(纯文本)   OK 临时性质(加粗)   OK 占位清单来源   OK A1来源 # WorldParameter Source Contract V1` | `# WorldParameter Source Contract V1` | 同上（`git show HEAD:` 原始首行） |

判定：`OK …` 串是**编辑残留的 checklist 文本**误拼到标题前（含 `OK WPS55`/`OK A1来源` 等自检项），不属文档正文；文档原有结构（H1 + 空行 + 引用块）在 HEAD 中清晰可辨，因此**无内容被删除**——`WPS55`/`WPS54a`/`A1` 等规范标识在正文中**全部保留且数量未变**（见 §6 校验）。

---

## 4. F2：SeaLevel 实现状态描述

按代码事实（本轮复核，非引用旧报告）：

| 事实 | 静态证据 |
|---|---|
| `WorldGenerationSettings` 存在 `DefaultSeaLevel = 64` | `WorldGenerationSettings.cs` L29 |
| `SeaLevel` 作为设置值传入规则 | `WorldParameterRule.cs` 接口新增 `WorldGenerationSettings settings` 参数；`WorldParameterRules.cs` L142 使用 `settings.SeaLevel` |
| `OceanRule` 使用**严格小于** | `WorldParameterRules.cs` L143：`return elevation < settings.SeaLevel ? 1f : 0f;` |
| 六参数构造经七参数构造的默认值路径兼容 | `WorldGenerationSettings.cs` L69：`: this(..., DefaultSeaLevel)` |
| `ProvisionalSeaLevel` 已移除 | 全库 `.cs` **递归**扫描（235 文件）命中 **0**；`ProvisionalWorldHeight` 亦 **0** |

据此修正的 5 处（Round 26 报告指定位置，按实际文本定位，行号已因编辑位移）：

| 位置（修复后行号） | 原表述问题 | 修正要点 |
|---|---|---|
| 顶部「当前实现快照」L10 | 「`SeaLevel` 仍硬编码为 `64`，且输入 `Elevation` 仍是占位值」 | 改为「读取 `settings.SeaLevel`、严格小于；`64` 仅设计默认值；`ProvisionalSeaLevel` 已删除」 |
| 表 A1 `Ocean` 行 L154 | 「`SeaLevel` 取登记默认值 `64`」「唯一临时量 = `SeaLevel` 常量」 | 改为「读取 `settings.SeaLevel`；配置读取已落地、无临时常量」；并注明生成运行时未消费 |
| §5.1 `Ocean` 行 L267 | 「`Elevation < 64`」「临时常量 `SeaLevel`」 | 改为 `Elevation < settings.SeaLevel`；配置读取已达成 |
| §5.2 `WPS56` L281 | 状态列「硬编码常量」、替换方向「改为显式配置读取」 | 改为「**已达成**：`ProvisionalSeaLevel` 已删除，`OceanRule` 读取 `settings.SeaLevel`」；替换方向改为「已达成 + 未完成项 = 生成运行时消费」 |
| 附录 C `Ocean` 行 L464 | 「契约派生（`Elevation < 64`）」 | 改为「`Elevation < settings.SeaLevel`；设计默认值 `64`」 |

**同一事实矛盾的相邻行（Round 26 未列入，本轮一并修正，理由：属同一「SeaLevel/Elevation 已接线 vs 未接线」矛盾类）**

| 位置 | 原表述 | 修正 |
|---|---|---|
| 顶部快照 L8 | 「…生成 8 项参数快照；Region Modifier 当前恒为 `0`」 | 区分 Base 段（Modifier 恒 0）与 Final 段（Region 修正量取自占位实现 `[0, 0.25)`） |
| 顶部快照 L9 | 「`Elevation` 仍是域哈希占位…**尚未接入**」 | 改为「已接线：只读转发 `HeightFunction.GetTerrainHeight`」 |
| 表 A2 `Elevation` 读取边界 L162 | 「**只读高度场**（未接入）」 | 改为「（已接入：`HeightFunction.GetTerrainHeight`）」 |

**明确保留（未改）的三类表述**

1. **设计默认值 `64`** —— 文档中作为契约表 C 登记默认值保留（`WorldParameter_Contract.md` L239「`64`（显式配置）」、`WPS56` 行「登记默认值 `64`」），**未被删除**。
2. **固定测试基准** —— 契约正文中 `Ocean` 派生规则写作 `Elevation < SeaLevel`（`WorldParameter_Contract.md` L212），未出现过期硬编码。
3. **尚未完成的生产管线** —— 所有相关行均**保留**「World V2 尚未接入生成运行时 / 生产调用点 = 0 / 生成运行时消费未完成」的表述，**未把未完成项标记为完成**。

---

## 5. F3：Region 相关描述统一

先按本轮实测事实分层（**不引用旧报告，逐项核实**）：

| 判定维度 | 实测结论 | 证据 |
|---|---|---|
| 接口 / 数据结构是否存在 | **存在** | `RegionContext.cs`、`RegionModifier.cs`、`RegionPipeline.cs`、`RegionModifierDetermination.cs`、`RegionDetermination.cs`、`RegionIdentity.cs`、`RegionSourceCandidate.cs`（均在 `Assets/Scripts/World/V2/`） |
| Region 规则是否有实际实现 | **有实现，但为占位实现** | `RegionModifierDetermination.cs` L52 `private const float PlaceholderAmplitude = 0.25f;`；L22「【当前实现 = 占位】真实的修正规则尚未冻结」；L25「分量幅度落在 `[0, 0.25)` 的占位区间」 |
| 是否有真实 Region 数据进入最终参数计算 | **结构上进入、数据上为占位** | `WorldStatePipeline.EvaluateFinal` L62–L65：`RegionPipeline.ResolveContext` → `RegionModifierDetermination.Determine` → `WorldParameterEvaluator.Finalize` |
| 最终参数是否由生产消费者使用并影响世界生成输出 | **否** | 递归扫描 235 个 `.cs`：`WorldStatePipeline.` / `WorldParameterQuery.Evaluate` / `WorldParameterEvaluator.(Evaluate\|Finalize)` 共 **28** 处引用，**非 V2 / 非 Editor 的生产调用点 = 0** |

据此修正的矛盾表述：

| 位置 | 冲突 | 修正 |
|---|---|---|
| §5.1 `Elevation` 行原 L265 | 同一单元格既称「已接线（真实高度场）」又称「**代码接线未完成**（实现仍为域 1 占位）」 | 保留「已接线」事实，改为「**代码接线已完成**（`ElevationRule` 调用 `HeightFunction.GetTerrainHeight`；`ProvisionalWorldHeight` 已删除）」，并把真正未完成项写成「**仍待完成**：World V2 参数结果接入生成运行时（生产调用点 = 0）」 |
| §5.2 `WPS57` 行原 L282 | 「该条记录对应的历史轮次仅更新计划与契约状态，**未修改实现**」 | 改为「**实现状态（已更正）**：域标签已在实现中移除…；§6.4 登记未变更；**仍待完成**：生成运行时消费」 |
| 顶部快照 L8 | 「Region Modifier 当前恒为 `0`」（与占位实现 `[0,0.25)` 不符） | 区分 Base 段 Modifier 恒 0 与 Final 段 Region 修正量取占位实现 |

**未做的事**：没有把 Editor 验证器内部的调用当作生产调用；「生产调用链尚未接通」的状态**如实保留**在 4 处（顶部快照、表 A1 Ocean 行、§5.1 Elevation 行、§5.1 Ocean 行、`WPS56`、`WPS57`）。

---

## 6. 修改后检查

| 检查项 | 结果 |
|---|---|
| 只修改目标文档 | **是**：`.md` 变更仅 2 条；tracked `M` 总数仍 16（其余 14 个为历史轮次的 C# 改动，本轮未触碰） |
| 规范编号/约束/范围未被改动 | `WorldParameter_Source_Contract.md` 标识集合：当前 182 / HEAD 182，**新增 0、删除 0**；`WorldParameter_Contract.md` 标识集合新增 `WSC10`/`WSC13` 两处**引用**，来自第 5 轮已存在的说明段落（非本轮编辑），本轮仅还原首行 ⇒ **未新增/删除任何规范编号定义，未修改任何有效约束或参数范围** |
| 污染文本残留 | `OK WPC§5.3` / `OK 索引表` = **0**；两文档首行分别为 `# World Parameter Contract V1`、`# WorldParameter Source Contract V1` |
| `ProvisionalSeaLevel` | 全库 `.cs` 递归 = **0**；文档中不再作为现存实现表述 |
| `硬编码为 64` / `仍硬编码` / `代码接线未完成` / `未修改实现` | 两份目标文档中均为 **0** |
| 七验证器冻结哈希 | **349 条 / 失配 0 / 缺失 0**（独立重算，与 Round 25 基线一致） |
| 生产代码 / 验证器 / 入口 | 未运行、未修改、未新增 |
| 进度记录 / 历史证据 | `sealevel_round7_progress.md` 仍 **1032 行**（未改）；未覆盖任何证据 |

冻结表旁证：7 个验证器登记的 `../Specifications/...` 键共 **14 个**（`VoxelWorld_*` 10 + `WorldGeneration_*` 4 + ADR 1…去重后 14），其中含 `WorldParameter_` 的 = **0** ⇒ 这两份文档不在任何冻结哈希登记表内。此外递归扫描确认它们被 15 处 C# 注释作为**文档路径引用**（如 `RegionDetermination.cs` L51、`WorldParameterHash.cs` L8），属正常文档引用、无哈希耦合。

---

## 7. 仍存在的问题 / 无法确认的文本

1. **`Elevation < 64` 仍有 4 处，但不在本轮允许修改的两份文档内**：
   - `Region_Parameter_Integration_Audit.md` L31、L111、L142
   - `Region_Source_Candidate_Generation_Minimal_Design.md` L45
   这些是**历史审计/设计快照**，正文含具体代码行号引用（如 `WorldParameterRules.cs:123`、`L75`/`L102–L107`），描述的是当时的状态。按本轮范围与「仅在语义确实错误时修改」的规则，**未修改**；建议单独立轮更新（其行号引用已随 R8 实现变更而过期）。
2. **`WorldParameter_Contract.md` L344** 提到 `WorldSkeletonFacts.TryEvaluate(...)` 对 `seaLevel` 不校验不钳制 —— 该表述与代码一致（未改动），但该 API 的行为**未在本会话动态验证**。
3. **Round 26 报告的审计方法缺陷（据实更正）**：Round 26 使用 `Assets\**\*.cs` 作为搜索路径，但 PowerShell 5.1 中 `**` **不是递归通配符**（仅匹配一层），导致 `Assets/Scripts/World/V2/`、`Assets/Scripts/VoxelWorld/**` 等深层目录**未被扫描**。本轮改用 `Get-ChildItem -Recurse`（235 个 `.cs`）重做全部关键搜索：
   - 结论**全部维持**：生产调用点 0、`ProvisionalSeaLevel` 残留 0、两份文档无哈希耦合；
   - 但**新增事实**：Region 层实现文件确实存在（`RegionModifier*` / `RegionPipeline` / `RegionContext` 等 7 个文件），Round 26 未记录该事实，本轮已在 §5 补齐。
   - 影响评估：Round 26 的 F1–F5 结论与「349/349」结论**不受影响**（后者用逐表登记表解析、未依赖通配符搜索）。

---

## 8. 状态三分（不混淆）

| 类别 | 内容 |
|---|---|
| **已实现（静态可确认）** | `SeaLevel` 配置字段 + `DefaultSeaLevel=64` + 六参兼容委托 + `[0, WorldHeight]` 校验；`OceanRule` 严格 `< settings.SeaLevel`；`Elevation` 只读转发 `HeightFunction.GetTerrainHeight`；`ProvisionalSeaLevel`/`ProvisionalWorldHeight` 已删除；Base/Final 两段式（`Evaluate` → `Finalize`）与 Region 调用链**结构存在**；7 个验证器 349 条冻结哈希闭合 |
| **设计默认值（非实现状态）** | `SeaLevel` 默认 `64`（契约表 C 登记默认值，代码 `DefaultSeaLevel`）；`WorldHeight` 默认 `64`；`SoilDepth` 默认 `3` |
| **仍待生产接线** | World V2 参数结果**未被任何生产消费者使用**（生产调用点 = 0，不以 Editor 验证器内部调用代替）；Region 修正量为**占位数据**（`[0, 0.25)`，真实规则未冻结）；`Temperature`/`Moisture` 仍为域哈希占位；`Fertility` 仍未读 `SoilDepth`；`Continent`/`Slope` 未接入 |
| **本条不宣称** | 动态验证完成、端到端验证完成、世界输出正确性 |

---

## 9. 证据引用

- 本报告：`_phase2b_validation/round27_document_consistency_audit.md`
- 上一轮审计：`_phase2b_validation/round26_final_diff_audit.md`
- 冻结哈希闭合链：`round16_hash_dependency_audit.md` + `round16_hash_audit_rows.csv` + `round17`–`round25_hash_dependency_audit_delta.md`
- 本轮验证的代码证据：`WorldGenerationSettings.cs`(L29/L41/L49/L69/L132) 、`WorldParameterRules.cs`(L102/L143) 、`WorldStatePipeline.cs`(L43/L58/L62–L65) 、`RegionModifierDetermination.cs`(L22/L25/L52)
- 进度记录：`_phase2b_validation/sealevel_round7_progress.md`（**本轮未修改**，仍 1032 行；如需追加本轮记录请批准）
