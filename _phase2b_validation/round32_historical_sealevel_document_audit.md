# R32：历史文档 SeaLevel 语义审计与最小修正

- 性质：**静态文本审计 + 最小文本修改**（不涉及任何运行时验证；本文不宣称任何验证器状态变化）
- 修改范围：**仅 2 份 Markdown 文档**（`Specifications/World/Region_Parameter_Integration_Audit.md`、`Specifications/World/Region_Source_Candidate_Generation_Minimal_Design.md`）
- 未修改：任何 `.cs` / `.asmdef` / 场景 / Prefab / 配置资产 / 验证器 / 哈希登记 / `.gitignore` / `_phase2b_validation/sealevel_round7_progress.md` / 既有证据
- 未执行：commit / push / reset / clean；未运行自动格式化；未做全局替换
- 现场：`main`，HEAD `37246478ed354a1e9a0ce7a3a3d9457caf188062`，staged = **0**

---

## 1. 已确认的设计约束（本轮核对依据，非本文档推导）

| 约束 | 代码证据（本轮实测） |
|---|---|
| `WorldGenerationSettings.DefaultSeaLevel = 64` | `WorldGenerationSettings.cs` L29 |
| `OceanRule` 严格比较 `elevation < settings.SeaLevel` | `WorldParameterRules.cs` L143 |
| `SeaLevel` 是世界生成配置，不是 `Elevation` 的计算输入 | `OceanRule` 才读取 `settings.SeaLevel`；`ElevationRule` 只调 `HeightFunction.GetTerrainHeight`（L102），不读 `SeaLevel` |
| `HeightFunction` 不因 `SeaLevel` 改变地形高度 | `HeightFunction.cs` 在 `git status` 中无改动，且其冻结哈希 `715341D5FC652408` 在多张表中匹配 |
| `ProvisionalSeaLevel` / `ProvisionalWorldHeight` | 全库 `.cs` 递归（235 文件）命中 **0** |

---

## 2. 「历史快照」性质的硬证据

`Region_Parameter_Integration_Audit.md` 在 L9–L10 自行声明了 **14 个 V2 `.cs` 的 SHA16 审查基准**。本轮逐项与当前工作树实测比对：

| 文件 | 文档基线 | 当前实测 | 一致 |
|---|---|---|---|
| `WorldParameterEvaluator.cs` | `7B35DBB38A02E719` | `ABFB3F89B0CADD6D` | **否** |
| `WorldParameterQuery.cs` | `4935EE144B3EF932` | `F72A67F57605B724` | **否** |
| `WorldParameterRules.cs` | `EB345A4E03674FF7` | `47DC85401349B685` | **否** |
| `WorldParameterRule.cs` | `73F45DE2178C58BF` | `D3594F6883C5551C` | **否** |
| `WorldParameterState.cs` | `6E65E7C8F5DC2CA1` | `3691FD2CCC95F9AB` | **否** |
| `WorldParameterHash.cs` | `E9BF9D755F6A1813` | 同 | 是 |
| `RegionIdentity.cs` / `RegionDetermination.cs` / `RegionContext.cs` / `RegionPipeline.cs` / `RegionModifier.cs` / `RegionModifierDetermination.cs` / `WorldSeed.cs` / `WorldCoordinate.cs` | — | 同 | 是（9 个） |

**5 / 14 已漂移**，且漂移项恰为 World V2 与 SeaLevel 迁移改动过的 `WorldParameter*.cs` ⇒ 该文档确实是 **Phase 3.1.0 时点快照**，其中 `Elevation = 域哈希 [0,128)`、`Ocean = Elevation < 64`、`Modifier 恒 0` 等是**当时的真实记录**，而非当前规则。

---

## 3. 逐处审计（分类 + 理由 + 处置）

分类口径：**A** = 当前设计说明，必须纠正；**B** = 历史测试/审查记录，保留原文并明确历史语境；**C** = 含义不明或修改有风险，先记录不擅改。

| # | 文档 | 原文（节选） | 分类 | 判断理由 | 处置 |
|---|---|---|---|---|---|
| 1 | Audit L31（改后 L38） | ``OceanRule(:123, step 1, Derived, `Elevation < 64`)``（位于「§1.1 **真实**调用链（含文件与行号）」） | **B** | 属「当时真实调用链」的快照记录，行号 `:123` 亦为当时行号（现为 `:143`） | 原文**保留**；由文档级历史语境标注覆盖 |
| 2 | Audit L111（改后 L118） | ``B-7 … Ocean = `Elevation < 64` ``（附 `WorldParameterRules.cs:83/:126` 等当时行号） | **B** | 属「§2.1 事实清单」的当时代码行为记录 | 原文**保留**；标注覆盖 |
| 3 | Audit L142（改后 L149） | `` | `Ocean` | `OceanRule` 派生：`Elevation < 64`（`:126`） | **否**（`Derived`）…`` | **B** | §2.3「现状」表描述该时点现状 | 原文**保留**；标注覆盖 |
| 4 | Audit L141（改后 L148） | `` | `Elevation` | `ElevationRule` 占位哈希整数 `[0,128)`（…:83）…`` | **B** | 同上（同表相邻行，与 #3 同源） | 原文**保留**；标注覆盖 |
| 5 | Minimal_Design L45（改后 L47） | ``地形派生量 `Elevation`/`Ocean` … Ocean` = `Elevation < 64`，派生形式合规、`SeaLevel` 为登记默认值占位…`` | **B** | §2.2 是「只读代码核查」的当时结果（Phase 3.1.13） | 原文**保留**；标注覆盖 |
| 6 | Minimal_Design L179（改后 L181） | ``**停止声明**：…**不得**…复用占位链（`Elevation` 占位哈希、`SeaLevel` 占位默认值）来制造"可运行实现"`` | **C** | 这是**仍在生效的禁止条款**；直接改写其措辞会改变约束效力与适用范围 | **原文一字未改**；仅在历史语境标注中追加一句说明"该处同样指该时点的占位链，停止声明继续有效"，以消除歧义而不削弱约束 |

**结论**：4 处 `Elevation < 64` 字面出现 + 1 处相邻同源表述，**全部为 B 类历史记录**；无 A 类「作为当前规则陈述」的条目。因此本轮采用**「保留原文 + 显式历史语境标注」**的最小处理，**未做任何把 `Elevation < 64` 改写为 `settings.SeaLevel` 的文本替换**（那会伪造历史记录）。

---

## 4. 精确 diff

### 4.1 `Specifications/World/Region_Parameter_Integration_Audit.md`（`numstat = 7 / 0`）

```diff
@@ -8,6 +8,13 @@
 > **审查基准（工作区快照，本轮未变）**：
 > `WorldParameterEvaluator.cs` `7B35DBB38A02E719` · … · `WorldCoordinate.cs` `CD9ECDCB6722823B`
+>
+> **【历史语境（后续轮次追加，不改本文历史结论）】** 本报告是 **Phase 3.1.0 时点快照**，§1.1 / §2.1 / §2.3 描述的是**当时**的代码状态，**不是当前规则**。经复核，上列 14 个 SHA16 基线中 **5 个已漂移**（`WorldParameterEvaluator.cs`、`WorldParameterQuery.cs`、`WorldParameterRules.cs`、`WorldParameterRule.cs`、`WorldParameterState.cs`），另 9 个未变。因此文中下列表述仅作**历史记录**保留：
+> - `Elevation` 为「域哈希占位 `Hash.Range → [0,128)`」⇒ 现为**只读转发** `HeightFunction.GetTerrainHeight(position.X, position.Z, settings)`（域依赖已移除，`ProvisionalWorldHeight` 已删除）。
+> - `Ocean` 为「`Elevation < 64`」⇒ 现为 `OceanRule` 严格比较 `elevation < settings.SeaLevel`；`SeaLevel` 是**世界生成配置**（`WorldGenerationSettings.SeaLevel`，登记默认值 `WorldGenerationSettings.DefaultSeaLevel = 64`），**不是** `Elevation` 的计算输入，也不改变 `HeightFunction` 的地形高度。
+> - `Modifier` 恒为 `0f`、`RegionModifier` 无人调用 ⇒ 现由 `WorldStatePipeline` 两段式编排（Base → Region → `Finalize`）调用，但 Region 修正量仍为**占位实现**（`RegionModifierDetermination`，区间 `[0, 0.25)`），且 World V2 参数结果**仍无生产消费者**。
+>
+> 文中数字 `64` 出现在上述位置时均为**该时点的占位常量/测试条件**，不得据此推断当前实现。
```

### 4.2 `Specifications/World/Region_Source_Candidate_Generation_Minimal_Design.md`（`numstat = 2 / 0`）

```diff
@@ -5,6 +5,8 @@
 > **原始起草轮次的工作范围（历史记录）**：…
+>
+> **【历史语境（后续轮次追加，不改本文历史结论）】** §2.2 的「只读代码核查」是 **Phase 3.1.13 时点**的代码状态，其中关于地形派生量的表述现已过时，按**历史记录**保留：`Elevation` 不再是「域标签整数哈希 `Range(seed, 1, ParameterId, position, 128)`」，现为**只读转发** `HeightFunction.GetTerrainHeight(position.X, position.Z, settings)`；`Ocean` 不再是「`Elevation < 64`」，现为 `OceanRule` 严格比较 `elevation < settings.SeaLevel`，其中 `SeaLevel` 是**世界生成配置**（登记默认值 `WorldGenerationSettings.DefaultSeaLevel = 64`）。上文所列 `MS-1`–`MS-4` 决策门槛与 §8 停止声明**仍然有效**，本标注不解除任何约束；§8 停止声明中提及的「`Elevation` 占位哈希」「`SeaLevel` 占位默认值」同样指该时点的占位链，**停止声明继续有效**。
```

合计：**+9 行，0 删除**（文档1 +7、文档2 +2）。两份文件原有正文（含全部历史结论、行号引用、契约条款）**一字未删**。

---

## 5. 验收结果

| 验收项 | 结果 |
|---|---|
| 精确 diff 已提交 | ✓ 见 §4（`numstat` 7/0 与 2/0） |
| 逐处记录原文/分类/理由/修改后文本 | ✓ 见 §3 |
| **不再存在把 `Elevation < 64` 描述为当前固定规则的表述** | ✓ 现存 6 处字面出现，**全部**为「历史语境标注内的否定性引述」（如「不再是「`Elevation < 64`」」）或「位于标注之后的原始历史记录」。逐处位置见下表 |
| 未意外修改目标之外的文件 | ✓ `tracked M` 由 16 → **18**，新增的 2 条恰为这两份文档；其余 16 条为 R7–R30 的历史改动，未被触碰 |
| HEAD 未变 / staged = 0 | ✓ `37246478ed354a1e9a0ce7a3a3d9457caf188062` / 0 |
| 349 条冻结哈希 | ✓ **349 / 失配 0 / 缺失 0**（独立重算；未因形式重跑验证器） |
| 进度记录未改 | ✓ `sealevel_round7_progress.md` 仍 **1032 行** |

### 5.1 `Elevation < 64` 字面出现的逐处定位（修改后行号）

| 文档 | 行号 | 性质 |
|---|---|---|
| `Region_Parameter_Integration_Audit.md` | **L14** | 位于历史语境标注内（否定性引述「`Ocean` 为「`Elevation < 64`」⇒ 现为 …」） |
| 同上 | **L38** | §1.1 原历史记录（标注在 L12，位于其前） |
| 同上 | **L118** | §2.1 `B-7` 原历史记录 |
| 同上 | **L149** | §2.3 `Ocean` 行原历史记录 |
| `Region_Source_Candidate_Generation_Minimal_Design.md` | **L9** | 位于历史语境标注内（否定性引述「不再是「`Elevation < 64`」」） |
| 同上 | **L47** | §2.2 原历史记录（标注在 L9，位于其前） |

> 说明：文档2 的自动检查曾报「每个出现处均位于标注之后 = False」，原因是**标注自身**（L9）就含有该字面串（作为否定性引述）；原始历史记录 L47 确在标注之后。此为检测逻辑的边界情况，非文档问题。

### 5.2 冻结登记影响

| 项 | 结果 |
|---|---|
| 7 张冻结表对这两份文档的登记/引用 | **0**（本轮实测） |
| 349 条冻结哈希 | 未受影响（**0 失配 / 0 缺失**） |
| 是否触发级联 / 需重钉 | **否** |

---

## 6. 未解决项 / 待决事项（本轮不改）

| # | 项 | 分类 | 说明 |
|---|---|---|---|
| 1 | `Region_Source_Candidate_Generation_Minimal_Design.md` §8 停止声明中「`SeaLevel` 占位默认值」措辞 | **C** | 停止声明仍在生效；直接改措辞会改变约束效力。本轮仅加语境说明，**条文一字未改**。是否正式修订该条文需你批准 |
| 2 | `Region_Parameter_Integration_Audit.md` 全篇行号引用（`:123` / `:83` / `:126` / `:102` 等）均已随实现变更失效 | **C** | 属历史快照的固有属性；逐条更新行号等于重写历史审计，风险高于收益。若需「当前行号版本」，应**新建**文档而非改历史件 |
| 3 | 文档1 L30/L141 的 `Elevation = Hash.Range → [0,128)` 表述 | **B** | 已由历史语境标注覆盖；原文保留 |
| 4 | 其他 `Specifications` 文档是否还有同类过时 SeaLevel 表述 | 未审计 | 本轮范围严格限定于 R27 指出的两份文档；若需全库扫描应另立一轮 |

---

## 7. 本轮文件变更清单

| 变更 | 文件 | 规模 |
|---|---|---|
| 修改 | `Specifications/World/Region_Parameter_Integration_Audit.md` | **+7 行 / 0 删除**（463 → 470 行） |
| 修改 | `Specifications/World/Region_Source_Candidate_Generation_Minimal_Design.md` | **+2 行 / 0 删除**（210 → 212 行） |
| 新增 | `_phase2b_validation/round32_historical_sealevel_document_audit.md`（本报告） | — |
| 删除 / 移动 / 覆盖 | **无** | — |

### 7.1 最终现场

| 项 | 值 |
|---|---|
| 分支 / HEAD | `main` / `37246478ed354a1e9a0ce7a3a3d9457caf188062`（未变） |
| staged | **0** |
| `git status --short` | **25 条**（18 tracked `M` + 4 untracked 源文件 + 3 证据目录） |
| `git diff --stat` 末行 | `18 files changed, 361 insertions(+), 121 deletions(-)`（R31 时为 `16 / 352 / 121`，差值恰为 +2 文件 / +9 行） |

---

## 8. 结论

1. R27 指出的 4 处 `Elevation < 64`（+1 处相邻同源表述）经逐处审计，**全部为 B 类历史快照记录**，其真实性由「14 个 SHA16 基线中 5 个已漂移」独立佐证；**不存在 A 类「当作当前规则陈述」的条目**。
2. 因此未做任何「把 `Elevation < 64` 替换为 `settings.SeaLevel`」的文本改写（那会伪造历史），而是**追加 9 行历史语境标注**，使每处旧表述的历史性质与当前设计（`SeaLevel` = 配置、`DefaultSeaLevel = 64`、严格 `<`、`Elevation` 只读转发）同时可读。
3. 所有历史结论、契约条款、禁止条款与行号引用**一字未删**；§8 停止声明**继续有效**。
4. 349 条冻结哈希仍为 **0 失配 / 0 缺失**；两份文档不在任何冻结表中，无级联。
5. **本轮为静态文本审计，不构成任何运行时验证结论。**
