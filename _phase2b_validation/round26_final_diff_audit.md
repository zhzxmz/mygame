# 第二十六轮：SeaLevel / Geology 验证体系全量只读审计

- 审计性质：**只读**（本轮未修改任何源文件；新增本报告一份）
- 审计时间点：Round 25 交付之后
- 分支：`main`
- HEAD：`37246478ed354a1e9a0ce7a3a3d9457caf188062`
- 未执行：commit / push / reset / clean；未删除或覆盖任何历史证据
- 说明：按本轮要求**未修改** `sealevel_round7_progress.md`（如需更新，见 §8 建议）

---

## 1. 现场

| 项 | 结果 |
|---|---|
| 分支 | `main` ✓ |
| HEAD | `37246478ed354a1e9a0ce7a3a3d9457caf188062` ✓（与 R16–R25 全程一致） |
| `git diff --cached` | **0 个 staged 文件**（无暂存） |
| tracked 修改 | **16 个 `M`** |
| untracked 源文件 | **4 个**（2 个 `.cs` + 2 个 `.meta`） |
| untracked 证据目录 | **3 个**（`_b3b_diff/`、`_finalization_backup/`、`_phase2b_validation/`） |

现场与 R25 报告完全一致，**无现场不一致**。

---

## 2. 全量差异审查（逐文件）

### 2.1 tracked 修改：验证器（7 个，+/− 已核对实际内容）

| 文件 | numstat | 修改性质 | 对应轮次 | 最近真实运行 | 是否被历史解释 |
|---|---|---|---|---|---|
| `Assets/Editor/ChunkStreamingValidation.cs` | +6/−4 | CS12 登记值重钉（Settings / 2 规范 / RTC）+ 2 行注释 | R11–R12、R20 | R20 exit0 / 107 / 0 PASS | ✓ |
| `Assets/Editor/GeologyRuntimeIntegrationValidation.cs` | +14/−14 | GE7 1 条 + GE9 5 条登记重钉（含 4 行行尾注释） | R12、R25 | R25 exit0 / 45 / 0 PASS | ✓ |
| `Assets/Editor/GeologyStageAValidation.cs` | +9/−9 | 9 条（R17）+ 1 条（R21）登记重钉 | R17、R21 | R21 exit0 / 105 / 0 PASS | ✓ |
| `Assets/Editor/GeologyStageBValidation.cs` | +10/−10 | 10 条（R18）+ 2 条值替换（R22，同一行二次改值故 numstat 不变） | R18、R22 | R22 exit0 / 122 / 0 PASS | ✓ |
| `Assets/Editor/GeologyStageCValidation.cs` | +11/−11 | 11 条（R19）+ 3 条值替换（R23） | R19、R23 | R23 exit0 / 49 / 0 PASS | ✓ |
| `Assets/Editor/GeologyStageDValidation.cs` | +19/−13 | 6 规范钉 + ChunkStreaming 钉（R13，含 4 行注释）+ 5 条值替换（R24） | R13、R24 | R24 exit0 / 44 / 0 PASS | ✓ |
| `Assets/Editor/VoxelWorldRuntimeCoreValidation.cs` | +1/−1 | RCI15 单条登记值 | R15 | R15 exit0 / 196 / 0 PASS | ✓ |

审查方式：逐文件读取 `git diff -U0/-U1` 实际内容，确认变更**全部为登记哈希字符串与说明注释**；无 `Check(` 行被增删，无集合条目增删，无路径规则/算法改动。

### 2.2 tracked 修改：生产代码（7 个）

| 文件 | numstat | 修改内容摘要 | 对应轮次 | 分类 |
|---|---|---|---|---|
| `Assets/Scripts/VoxelWorld/Generation/WorldGenerationSettings.cs` | **+46/−0** | `DefaultSeaLevel = 64` 常量；`public int SeaLevel;` 字段；六参构造委托（传 `DefaultSeaLevel`）；新增七参构造；`SeaLevel = seaLevel` 赋值；`IsValid` 增加 `[0, WorldHeight]` 校验；`ToString` 增加 `SeaLevel=` | R7 | 生产（SeaLevel 迁移） |
| `Assets/Scripts/Game/VoxelWorldStreamingBootstrap.cs` | +4/−1 | `public int SeaLevel = 64;` 配置字段（含文档注释）；构造调用由六参改为七参并传 `SeaLevel` | R7–R8 | 生产（SeaLevel 迁移） |
| `Assets/Scripts/World/V2/WorldParameterRules.cs` | +18/−27 | Elevation 由「域 1 哈希占位 + `ProvisionalWorldHeight=128`」改为 `HeightFunction.GetTerrainHeight(position.X, position.Z, settings)`；Ocean 由 `ProvisionalSeaLevel=64f` 改为 `elevation < settings.SeaLevel`；8 个规则方法签名统一增加 `settings` 参数；删除 2 个占位常量 | R8 | 生产（SeaLevel/Elevation 接线） |
| `Assets/Scripts/World/V2/WorldParameterRule.cs` | +3/−1 | 接口方法增加 `WorldGenerationSettings settings` 参数 + using | R8 | 生产（签名级联） |
| `Assets/Scripts/World/V2/WorldParameterEvaluator.cs` | +156/−14 | `Evaluate` 增加 `settings` 参数并改为**只发布 Base 段**（返回 `IsFinalized=false`）；新增 `Finalize(baseState, regionModifier)`（唯一合成权威 `clamp01(Base+Modifier)`）、`ModifierFor`、`KindOf`；删除 per-call `compositionKinds` 数组；新增启动不变量断言（Elevation=Forwarded / Ocean=Derived / 其余=AdditiveClamped） | R3–R8 | 生产（PC19 两段式） |
| `Assets/Scripts/World/V2/WorldParameterQuery.cs` | +10/−3 | `Evaluate` 增加 `settings` 参数；文档明确「Base 段入口、`IsFinalized == false` 不是权威值」 | R8 | 生产（签名级联 + 文档） |
| `Assets/Scripts/World/V2/WorldParameterState.cs` | +30/−0 | 新增 `isFinalized` 字段、`(values, bool)` 构造、`IsFinalized` 属性；旧构造委托 `: this(values, false)` | R3–R8 | 生产（PC19 载体） |

### 2.3 tracked 修改：文档（2 个）

| 文件 | numstat | 审查结果 |
|---|---|---|
| `Specifications/World/WorldParameter_Contract.md` | +3/−1 | 追加一段「Elevation 接线状态与两段式职责边界」说明（内容与代码一致）**但 H1 标题被污染（见 F1）** |
| `Specifications/World/WorldParameter_Source_Contract.md` | +6/−6 | 更新 Elevation/WPS54a/WPS55 状态行（与代码一致）**但 H1 标题被污染（F1）、Ocean/SeaLevel 行仍过期（F2）、含自相矛盾句（F3）** |

### 2.4 未跟踪源文件（4 个，已读取完整内容）

| 文件 | 规模 | 内容审查结论 |
|---|---|---|
| `Assets/Editor/WorldV2ParameterPipelineValidation.cs` | 15580 B / 304 行 / 30 处 `Check(` / 2 处 `Report(` | 新增 Editor 验证器：两段式发布断言 + SeaLevel 组（A/B1/B2/B3/C/D）；`SeaLevel` 提及 18 次；`ValidateOrThrow` 为入口；**无 `RunBatch()`**；无 TODO/FIXME/临时标记残留 |
| `Assets/Editor/WorldV2ParameterPipelineValidation.cs.meta` | 243 B | guid `ccca706593e79fa4588fcd522dfb524f`（唯一） |
| `Assets/Scripts/World/V2/WorldStatePipeline.cs` | 4168 B / 68 行 | `EvaluateBase` / `EvaluateFinal` 两个静态入口；无生产调用者（见 §2.6） |
| `Assets/Scripts/World/V2/WorldStatePipeline.cs.meta` | 243 B | guid `0e46905e114bdb04b8fcc2e08011017a`（唯一） |

meta 完整性：全库 89 个 `.meta`，**重复 GUID 组 = 0**。

### 2.5 未跟踪证据目录（3 个）

| 目录 | 文件数 | 总量 | 内容 |
|---|---:|---:|---|
| `_b3b_diff/` | 2 | <0.1 MB | 早期 B3B 差异证据 |
| `_finalization_backup/` | 12 | 0.5 MB | finalization 备份 |
| `_phase2b_validation/` | **3411** | **223.7 MB** | 含 R10–R25 全部 `round*` 证据（69 个文件）+ 一份**完整 Unity 工程副本**（含 `Library/`、`ProjectSettings/`、`_unity_temp/`） |

R10–R25 的 `round*` 证据文件齐全（每轮 raw/rerun 日志 + stdout/stderr + 哈希依赖增量报告），**未发现证据覆盖或缺失**（唯一例外见 F5）。

### 2.6 交叉验证（实际搜索，非文档声称）

- `WorldStatePipeline.` / `WorldParameterQuery.Evaluate` / `WorldParameterEvaluator.(Evaluate|Finalize)` 在 `Assets/**/*.cs` 中共 17 处引用，**全部位于新增 Editor 验证器内**；非 V2/非 Editor 的生产调用点 = **0** ⇒ World V2 参数层**确无生产消费者**，与文档表述一致。
- `HeightFunction.cs` 在 `git status` 中**无改动**（0 项）⇒ Elevation 接线未触碰高度场实现本身。
- `ProvisionalSeaLevel` 全库残留 = **0**；`ProvisionalWorldHeight` 全库残留 = **0** ⇒ 占位常量已按设计彻底移除。

---

## 3. 验证器冻结哈希闭合复核（独立重算）

| 验证器 | 登记数 | 失配 | 缺失 |
|---|---:|---:|---:|
| ChunkStreamingValidation | 40 | 0 | 0 |
| GeologyRuntimeIntegrationValidation | 65 | 0 | 0 |
| GeologyStageAValidation | 51 | 0 | 0 |
| GeologyStageBValidation | 55 | 0 | 0 |
| GeologyStageCValidation | 57 | 0 | 0 |
| GeologyStageDValidation | 63 | 0 | 0 |
| VoxelWorldRuntimeCoreValidation | 18 | 0 | 0 |
| **合计** | **349** | **0** | **0** |

结论：**349 / 349 匹配，0 失配，0 缺失**，与 Round 25 结果**完全一致**；R25 之后没有新的源文件修改破坏该状态（16 个 tracked `M` 与 R25 结束时逐项相同，`git diff --cached` 为空）。

---

## 4. SeaLevel 生产改动审查（静态）

| 核对项 | 结论 | 证据 |
|---|---|---|
| `SeaLevel` 默认值 | `public const int DefaultSeaLevel = 64;`；六参构造以 `: this(..., DefaultSeaLevel)` 委托 ⇒ 旧调用点行为不变 | `WorldGenerationSettings.cs` L29 / L69 |
| 构造函数兼容性 | 原六参签名保留（委托）；新增七参构造；库内构造点均传 `WorldHeight=64`，故 `SeaLevel=64` 合法 | 结构体 diff + R7 兼容性证据 |
| 范围验证 | `IsValid` 新增 `if (SeaLevel < 0 \|\| SeaLevel > WorldHeight)` ⇒ 闭区间 `[0, WorldHeight]` | L132 附近 |
| `OceanRule` 比较语义 | `return elevation < settings.SeaLevel ? 1f : 0f;` —— **严格小于**，未变 | `WorldParameterRules.cs` L143 |
| `ProvisionalSeaLevel` 移除 | 全库残留 0（`ProvisionalWorldHeight` 亦为 0） | 全文搜索 |
| `SeaLevel` 未污染 HeightFunction / Elevation | Elevation 走 `HeightFunction.GetTerrainHeight(position.X, position.Z, settings)`（L102），**不读取 SeaLevel**；`HeightFunction.cs` 无改动；且其冻结哈希 `715341D5FC652408` 在 6 张表中匹配 | L102 + 冻结哈希 |
| 世界生成行为 | `BasicTerrainGenerator` / `IWorldGenerator` / `VoxelCore` 等冻结哈希全部匹配 ⇒ 生成行为相关文件未被改动 | §3 349/349 |
| 序列化字段 | `Bootstrap.SeaLevel` 带内联初始化器 `= 64`；R9 调查确认工程内无该组件的场景/Prefab 实例 | R9 证据 |
| Round 9 证据可追溯性 | 日志 `C:\Users\xmz\AppData\Local\Temp\unity_r9_v2.log` **仍存在**（78750 B，2026-10-18 10:55）；含 `[WorldV2-TwoPhase] … ⇒ failures=0（全部通过）`；匹配 `FAIL - \| [FAIL] \| Result = FAIL` 的行 = **0**；进度记录 L50 记载「PASS 29 / FAIL 0」 | 实测 |

**需要动态验证（无法由静态差异确定）**

1. `WorldGenerationSettings.SeaLevel` 声明为 `public int SeaLevel;`（**无内联初始化器**）。六参构造路径下必为 64，但 `default(WorldGenerationSettings)`、反射构造或未来反序列化缺字段路径下将得到 **0**（与默认 64 不同）。当前无场景/Prefab 实例 ⇒ 无现时影响，但**属需动态验证的边界**。
2. `WorldHeight=64` 且 `SeaLevel=64` ⇒ `Ocean` 在全部合法高度为 true（迁移前既有行为，非本轮引入）。语义是否为期望值仍**待所有者裁决**。
3. `Finalize` 的 Region 修正量当前为占位数据 ⇒ Final 段「结构已定稿、数据仍是占位」，真实世界影响**未经动态验证**。

---

## 5. 发现的问题与风险

| 编号 | 级别 | 位置 | 问题 | 影响 |
|---|---|---|---|---|
| **F1** | **高（文档）** | `WorldParameter_Contract.md` L1；`WorldParameter_Source_Contract.md` L1 | 两份规范的 H1 标题被前缀污染：`  OK WPC§5.3 # World Parameter Contract V1`、`  OK 索引表   OK WPS55   OK WPS54a   OK 临时性质(纯文本)   OK 临时性质(加粗)   OK 占位清单来源   OK A1来源 # WorldParameter Source Contract V1` | H1 失效、文档渲染错乱；疑似编辑残留（checklist 文本）被误写入。**未提交**，提交前必须清理。两份文件**不在任何冻结哈希集合内**（冻结集合仅含 `VoxelWorld_*.md` / `WorldGeneration_*.md` / ADR），故未破坏 349 条校验 |
| **F2** | **高（文档真实性）** | `WorldParameter_Source_Contract.md` L10 / L154 / L267 / L281 / L464 | 仍将 `SeaLevel` 描述为**硬编码常量 64**（如 L267「临时常量 `SeaLevel`」「改为显式配置读取」、L281 `WPS56` 状态列未更新、L464「契约派生（`Elevation < 64`）」），与代码现状（`settings.SeaLevel`、占位常量已删）不符 | 文档**低报**实现进度，违反「文档不得与实现状态不符」的项目规则；`WPS56` 与 A1 Ocean 行同步**未完成** |
| **F3** | 中（文档自相矛盾） | `WorldParameter_Source_Contract.md` L265 | 同一行既称「已接线（真实高度场）」，又保留旧句「**本轮仅更新计划与契约状态：代码接线未完成**（实现仍为域 1 占位）」 | 同一单元格内相互矛盾，读者无法判断真实状态 |
| **F4** | 中（仓库卫生） | `_phase2b_validation/` | 3411 文件 / **223.7 MB**，其中包含完整 Unity 工程副本与 `Library/`；`_finalization_backup/` 0.5 MB；三者均 untracked | 若执行 `git add -A` 会把 223 MB 证据与工程副本一并入库；建议加入 `.gitignore` 或仅保留 `round*` 证据文件 |
| **F5** | 低（覆盖/可追溯） | `WorldV2ParameterPipelineValidation.cs`；R9 日志 | 该验证器**无 `RunBatch()`**（仅 `ValidateOrThrow`），因此不被「所有入口需提供 `RunBatch()`」类断言覆盖；其 R9 证据日志位于系统 TEMP（已验证存在），**未归档**到 `_phase2b_validation` | 入口一致性与长期证据保全存在缺口；若 TEMP 被清理则 R9 证据丢失 |

**未发现**：意外修改、重复内容、路径错误、临时文件残留（`TODO/FIXME/HACK` 搜索为 0）、证据覆盖。

---

## 6. 未完成事项清单（严格状态）

| 事项 | 状态 | 依据 |
|---|---|---|
| 4 个 legacy core 验证器的真实运行（`VoxelWorldCoreValidation` / `WorldGenerationCoreValidation` / `VoxelMeshCoreValidation` / `VoxelWorldViewValidation`） | **未完成** | 本会话从未运行；其冻结哈希被 7 张表登记且全部匹配，仅证明「文件未被改动」，**不能替代真实运行**。状态 = **未知，不得标记 PASS** |
| Elevation 等价验证复跑 | **未完成** | 上一次证据为历史外部 harness（12/12，216 点），本会话未复跑 |
| `WorldParameter_Source_Contract.md` 的 `WPS56` / A1 Ocean 文档同步 | **未完成** | 见 F2（5 处仍为硬编码 64 表述） |
| GE7 说明文本「Runtime Core(12)」与实际 11 条目不一致 | **未完成（仅记录）** | `CriticalFiles` 实测构成为 Generation=4 / Runtime Core=**11** / Streaming=5 / Mesh+View=5 = 25；注释与聚合断言文本仍写 12。属描述性文本，未修改 |
| 生产系统端到端验证 | **未完成** | World V2 参数层**无生产调用者**（§2.6 实测 0）；`WorldStatePipeline` 未接入生成运行时；Region 修正量为占位 |
| 七个验证器 349 条冻结哈希登记闭合 | **已完成** | §3 独立重算 349/0/0 |
| SeaLevel 迁移（静态差异层面） | **已完成** | §4 全部核对项通过（含 2 项标注为需动态验证） |
| SeaLevel 迁移（行为/端到端层面） | **证据不足** | 仅 R9 局部验证（29 PASS / 0 FAIL，V2 参数层单元级），**不构成**世界输出正确性证明 |

---

## 7. 结论（回答本轮 5 个问题）

1. **是否发现无法解释的代码差异？**
   **没有。** 16 个 tracked 修改与 4 个 untracked 源文件全部可归属到已记录轮次（SeaLevel 迁移 R7–R8、World V2 两段式 R3–R8、验证器登记重钉 R11–R25、文档同步 R3/R5），逐文件实际内容审查未发现未解释改动、临时文件或重复内容。
   **但发现 5 项非代码差异问题**（F1–F5），其中 F1（文档标题污染）与 F2（SeaLevel 文档过期）应在提交前处理。

2. **是否确认冻结哈希登记仍为 349 / 349 匹配？**
   **是。** 独立重算：7 个验证器共 **349 条登记，失配 0，缺失 0**，与 R25 一致；R25 后无新源文件修改。

3. **SeaLevel 迁移的静态差异是否符合设计？**
   **符合。** 默认值常量 64 + 六参兼容委托、`[0, WorldHeight]` 范围校验、`OceanRule` 严格 `< settings.SeaLevel`、占位常量全库清零、`SeaLevel` 未进入 `HeightFunction`/`Elevation` 路径、生成相关文件冻结哈希未变。
   **两项需动态验证**：`struct` 字段无内联初始化器导致的缺省/反序列化路径（0 vs 64）；`WorldHeight=SeaLevel=64` ⇒ 全域海洋的语义裁决。

4. **是否存在必须在继续开发前解决的高风险问题？**
   **没有阻断开发的高风险代码问题**，但有 2 项应在**提交前**解决：
   - **F1**：两份规范 H1 标题被 `OK …` 前缀污染（未提交，会随提交入库）；
   - **F2**：`WPS56` 与 A1 Ocean 行仍称 `SeaLevel` 为硬编码 64，与实现不符（文档低报进度）。
   另有 1 项仓库卫生风险：**F4**（223.7 MB untracked 证据目录，`git add -A` 风险）。

5. **哪些动态验证仍然缺失？**
   - 4 个 legacy core 验证器的真实运行（本会话 0 次）
   - Elevation 等价验证复跑
   - `WorldGenerationSettings.SeaLevel` 的缺省/反序列化路径行为
   - Region 修正量从占位到真实数据后的 Final 段真实影响
   - 生产系统端到端（World V2 参数 → 世界输出；当前**无生产消费者**）
   - `WPS56`/A1 Ocean 文档同步后的文档一致性复核

---

## 8. 证据引用

- 本报告：`_phase2b_validation/round26_final_diff_audit.md`
- 哈希依赖审计链：`round16_hash_dependency_audit.md`、`round16_hash_audit_rows.csv`、`round17`–`round25_hash_dependency_audit_delta.md`
- 各轮真实运行日志：`round10`–`round25_*_rerun.log`（+ 对应 stdout/stderr）
- 进度记录：`_phase2b_validation/sealevel_round7_progress.md`（1032 行，R7–R25）
- Round 9 证据：`C:\Users\xmz\AppData\Local\Temp\unity_r9_v2.log`（系统 TEMP，未归档 —— 见 F5）

## 9. 建议（需批准后执行，本轮未执行）

1. 清理 F1：删除两份规范 H1 前的 `OK …` 前缀（纯文档修复，不影响 349 条校验）。
2. 修复 F2/F3：更新 `WPS56`、A1 Ocean 行、L265 矛盾句，使文档与 `settings.SeaLevel` 实现一致。
3. 处理 F4：为 `_phase2b_validation/` 等证据目录增加 `.gitignore` 规则，或仅保留 `round*` 证据文件。
4. 处理 F5：把 R9 日志复制归档到 `_phase2b_validation/`；为 `WorldV2ParameterPipelineValidation` 补 `RunBatch()` 或将其登记为「非 RunBatch 入口」的显式例外。
5. 安排 4 个 legacy core 验证器的首次真实运行 + Elevation 等价验证复跑。
6. 上述完成后再进行提交评审（当前 16 `M` + 4 untracked 源文件仍全部未提交）。
