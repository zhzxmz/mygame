# R40 — WorldSkeletonFacts 海平面统一来源迁移报告（方案 A，正式批准）

- 项目：`D:\gamemaking\My project`（Unity **2022.3.62f1c1**）
- 分支 / HEAD：`main` / **`37246478ed354a1e9a0ce7a3a3d9457caf188062`**（**本轮未变**）
- 范围：**代码 + 契约文档 + 直接验证器**三者同轮迁移（本轮批准要求：不能只改生产代码）
- 批准口径：**方案 A** —— 移除 `WorldSkeletonFacts.TryEvaluate` 的 `int seaLevel` 入参，`Ocean` 阈值唯一来源 = `WorldGenerationSettings.SeaLevel`

---

## 1. 为什么是方案 A（依据，不重复 R39 全文）

| 事实 | 证据 |
|---|---|
| `WorldSkeletonFacts` 建于 `7c0d474`（2026-10-17），当时 `settings.SeaLevel` **尚不存在** | `git show HEAD:Assets/Scripts/VoxelWorld/Generation/WorldGenerationSettings.cs` → `SeaLevel` 命中 **0** |
| `SeaLevel` 字段是后续（未提交的 R7 工作区）追加 | 当前 `WorldGenerationSettings.cs` 含 `SeaLevel`（diff `+46`） |
| 双载体 ⇒ 静默分歧 | 静态推演：`settings.SeaLevel=32`、`Elevation=23` 时若调用方传 `seaLevel=23` 或 `22`，`Ocean=false` 而配置语义为 `true` |
| 生产调用者 = **0** ⇒ 迁移成本最低点 | 全库 `TryEvaluate` 调用点仅存在于验证器 |
| 与 `OceanRule` 统一 | `WorldParameterRules.OceanRule`（L134–144）早已使用 `settings.SeaLevel`（严格 `<`，L143） |
| 契约从未要求两者相等 | 全文 `settings.SeaLevel` 命中 0、`必须等于/须一致/保持一致/同步` 命中 0 |

---

## 2. 生产代码迁移（`Assets/Scripts/World/V2/WorldSkeletonFacts.cs`）

### 2.1 签名前后对照

```
迁移前：public static bool TryEvaluate(
            WorldCoordinate position,
            WorldGenerationSettings settings,
            int seaLevel,                      // ← 移除
            out WorldSkeletonFacts facts)

迁移后：public static bool TryEvaluate(
            WorldCoordinate position,
            WorldGenerationSettings settings,
            out WorldSkeletonFacts facts)
```

### 2.2 逐处改动（共 6 处，`git diff` 实测）

| # | 位置 | 改动 |
|---|---|---|
| 1 | L8 职责行 | `给定 (世界坐标, 世界生成配置, 海平面)` → `给定 (世界坐标, 世界生成配置)` + `（海平面由 settings.SeaLevel 提供）` |
| 2 | L11 `Ocean` 条目 | `Elevation 严格小于海平面` → `Elevation 严格小于 settings.SeaLevel` |
| 3 | L27–28 前置条件 | 删除 `海平面落在 [0, WorldHeight]` 独立前置项；改为 `settings` 合法性**已包含** `SeaLevel ∈ [0, WorldHeight]`；新增行：**海平面不再有独立入参**（R40 统一来源） |
| 4 | L91 本方法职责 | `不校验 settings 合法性与海平面范围` → `不校验 settings 合法性（…；settings.SeaLevel 范围由 WorldGenerationSettings.IsValid 负责）` |
| 5 | L96 签名 | 删除 `int seaLevel,`（唯一行为签名变更） |
| 6 | L110 `Ocean` 派生 | `bool ocean = elevation < seaLevel;` → `bool ocean = elevation < settings.SeaLevel;`（`F-2`/`WC3` 注释同步） |

**未改**：`Elevation` 来源（L107 `HeightFunction.GetTerrainHeight(position.X, position.Z, settings)`）、垂直范围失败条件（L100 `position.Y < 0 || position.Y >= settings.WorldHeight` ⇒ `default` + `false`，不钳制）、`Continent = ocean ? 0 : 1`（L113）、`private` 构造 + **唯一构造点**（L116）、严格小于比较符。

### 2.3 文件级事实

| 项 | 值 |
|---|---|
| 行数 | **121 → 120** |
| 字节 | 6999 B |
| SHA-256（全量 64 位） | `98B59916CA9CDBCC0FD8CE814DDE699EE96BBD21E07886DEFFD6A31B2E0DDA1C` |
| SHA-256 前 16 位 | `98B59916CA9CDBCC` |
| git blob | `0caa54b..d3a8485`，`M`（tracked） |
| 静态断言 | `int seaLevel` 出现 **0**；`settings.SeaLevel` 出现 **6** |

---

## 3. 直接验证器迁移（`Assets/Editor/WorldV2ParameterPipelineValidation.cs`，untracked `??`）

### 3.1 全部 7 处 `TryEvaluate` 调用点迁移

每处现为 **(position, settings, out facts) 2 输入 + out**（实参计数 3 含 `out`，非 3 参遗留）：

| 行 | 形式 |
|---|---|
| L281 / L296 / L309 / L320 / L336 / L354 | 单行 `TryEvaluate(<coordinate>, <settings>, out <facts>)` |
| L325–326 | 跨行 `TryEvaluate(new WorldCoordinate(...), slUnset, out aboveFacts)` |

静态复核：`int seaLevel` 出现 **0**。

### 3.2 E 组（查询级断言）语义迁移明细

| 断言 | R38 状态 | R40 状态 | 现语义（实测输出） |
|---|---|---|---|
| E0–E12 | 存在 | **保留**，改为 2 参调用 | `E0` 取得内部 Elevation 样本 `23 @ (0,0)`（运行时发现，非硬编码）；`E2` = `settings.SeaLevel` 比较；`E10/E11` 越界失败；`E12` `default.IsValid == false` |
| **E13** | 边界值传参语义 | **重写** | `同一 Elevation 下 SeaLevel=0 ⇒ Ocean=false，SeaLevel=64 ⇒ Ocean=true（Ocean 由 settings.SeaLevel 控制）` |
| **E14** | 存在 | **保留并强化** | `改变 settings.SeaLevel 不改变 facts.Elevation（sl0=23 / sl64=23）` |
| **E15** | 使用默认配置 | **改写**，改用新配置对象 | `settings.SeaLevel = 32（非默认）逐列 Ocean/Elevation 与 HeightFunction 一致（列数 35，mismatch=0）` |

新增配置对象：`skAtEqual`（`SeaLevel = skElevation = 23`）、`skBelow`（`= skElevation - 1 = 22`）、`skAt32`（`= 32`）。

**断言数守恒**：E0–E15 仍为 **16 条**（日志实测 16 条 `[WorldV2-TwoPhase] PASS:` E 组行），验证器总断言 **45**（与 R38 相同，未增删，仅语义迁移）。

### 3.3 文件级事实

| 项 | R38 | R40 |
|---|---|---|
| 行数 | 447 | **452**（+5） |
| 行首 `Check(` | 45 | **45**（不变） |
| `Check(` 全部出现 | — | 46（含 1 处方法定义） |
| 调用点 | 45 | **45** |
| 哈希（前 16） | `2362602F9ABB194A` | **`8BB8275A655A9FEC`** |
| SHA-256（全量） | — | `8BB8275A655A9FECEB1A3058F258E617D38744BD35BF6C0666694C5A639323D2` |

---

## 4. 契约文档迁移

### 4.1 `Specifications/World/WorldSkeleton_V1_Minimal_Contract.md`（10 行 / 12 处子串，行数仍 **347**）

| 行 | 条款 | 迁移内容 |
|---|---|---|
| L58 | 实现清单 | **`121 行` → `120 行`**；SHA-256 → `98B59916…E0DDA1C` |
| L140 | `C-1 SeaLevel` | Owner/取值来源追加：**承载 = `WorldGenerationSettings.SeaLevel`（R40 起唯一来源，接口无独立入参）** |
| L160 | `IF-1` 查询入口 | 签名去除 `int seaLevel,`；追加 `R40 起去除独立 seaLevel 入参` |
| L165 | `IF-6 Ocean` | `Ocean = Elevation < seaLevel` → `Ocean = Elevation < settings.SeaLevel`（严格小于不变） |
| L167 | `IF-8` 前置条件 | 删除独立前置 ②；改为 ① `settings` 合法性**已涵盖** `SeaLevel ∈ [0, WorldHeight]` |
| L168 | `IF-9 PS5 边界` | 本入口**不接收** `seaLevel`；范围由 `WorldGenerationSettings.IsValid` 负责；**仍无任何范围处理/钳制** |
| L177 | `Q-1` 查询输入 | ③ 改为 `settings`（世界生成配置，**含 `settings.SeaLevel`**——`C-1` 登记配置量；R40 起唯一来源） |
| L306 | 实现裁决表 | 标题改「`seaLevel` **来源**与范围归属」，结论：接口不再接收独立 `seaLevel`、统一取自 `settings.SeaLevel`、**不钳制/不拒绝** |
| L334 | `AC-15` | 追加：R40 起按「SeaLevel 唯一来源 = `settings.SeaLevel`」复核（逐条对照项列全） |
| L346 | §总结「已冻结接口形状」 | 签名 → `TryEvaluate(position, settings, out facts)`；`Ocean = Elevation < settings.SeaLevel`；`settings` 合法性由调用方保证 |

**校验**：契约登记的 64 位 SHA-256 与实际文件 `Get-FileHash` **逐字符相等**（`一致 = True`）；行数文本 `120` 与物理行 `120` 一致。

### 4.2 `Specifications/World/WorldParameter_Contract.md`（2 行，行数仍 **668**）

| 行 | 位置 | 迁移内容 |
|---|---|---|
| L344 | §5 `PS5` 适用范围澄清 | `TryEvaluate(...)` 不接收独立 `seaLevel`（R40 统一来源，`Ocean` 用 `settings.SeaLevel`）；对 `settings` 合法性仍**不校验/不钳制/不修正**；**`PS1`–`PS7` 效力与数值不变** |
| L374 | §6 消费登记 | `Ocean`/`Continent` 由 `Elevation` **与 `settings.SeaLevel`** 派生，不引入新参数；占位状态（`WPS54a`）描述不变 |

**未写「未来建议」为既成事实**：Region 修正仍标注「占位实现」、World V2 仍标注「无生产调用者、未接入生成链」。

### 4.3 哈希口径（推导依据）

- **冻结表 16 位**：`SHA256(文件全字节)` → 取前 8 字节 → 每字节 `X2` 大写拼接（= 前 16 个 hex 字符），与验证器 `ComputeFileHash16` 实现一致。
- **契约 L58 的 64 位**：`SHA256(文件全字节)` 全量 hex 大写，直接对 `Get-FileHash` 结果比对。

---

## 5. 真实运行（Unity Batchmode，本轮实跑）

命令模板（与 R31 一致）：

```
"D:\gamemaking\2022.3.62f1c1\Editor\Unity.exe" -batchmode -nographics -quit \
  -projectPath "D:\gamemaking\My project" \
  -logFile _phase2b_validation/round40_<Validator>_run.log \
  -executeMethod <Validator>.RunBatch
```

| # | 验证器 | 退出码 | 耗时 | PASS 行 | FAIL 行 | error CS | warning CS | 崩溃特征 | 日志 |
|---|---|---|---|---|---|---|---|---|---|
| 1 | `WorldV2ParameterPipelineValidation` | **0** | 29.2 s | **45** | **0** | **0** | 4（既有、无关文件） | **0** | `round40_WorldV2ParameterPipelineValidation_run.log`（115394 B）；stdout/stderr **0/0 B** |
| 2 | `GeologyRuntimeIntegrationValidation` | **0** | 21.2 s | **47** | **0** | **0** | **0** | **0** | `round40_GeologyRuntimeIntegrationValidation_run.log`（121222 B）；stdout/stderr **0/0 B** |

- 验证器自报总结行（原文）：`[WorldV2-TwoPhase] World V2 参数两段式发布 ⇒ failures=0（全部通过）`。
- 4 条 warning 明细（**均属既有文件、非本轮改动**）：`VoxelCoordinateMigrationStageB3CValidation.cs(2130,50) CS0162` ×2、`MiningSurfaceIntegrityValidation.cs(432,24) CS0414` ×2。
- 崩溃精确正则（`Crash!!!` / `Aborting batchmode` / `Unhandled exception` / `StackOverflowException` / `Fatal error` / `Native Crash` / `Received signal`）命中 **0**；日志末行 `~StackAllocator(ALLOC_TEMP_MAIN) …` 为 batchmode 退出期既有提示（R25/R31 同款），非崩溃。

### 5.1 关键断言实测原文（节选）

```
PASS: E13：同一 Elevation 下 SeaLevel=0 ⇒ Ocean=false，SeaLevel=64 ⇒ Ocean=true（Ocean 由 settings.SeaLevel 控制）
PASS: E14：改变 settings.SeaLevel 不改变 facts.Elevation（sl0=23 / sl64=23）
PASS: E15：settings.SeaLevel = 32（非默认）逐列 Ocean/Elevation 与 HeightFunction 一致（列数 35，mismatch=0）
PASS - GE1：rig 参考设置 SeaLevel 与 Bootstrap 配置一致（64 / 64）
PASS - GE1：Bootstrap.SeaLevel 等于登记默认值 DefaultSeaLevel（64 / 64）
PASS - GE7：BasicTerrainGenerator / IWorldGenerator / Generation / Runtime Core(12) / Streaming(5) / Mesh(2) / View(3) 共 25 个文件 SHA 不变
PASS - GE9：既有文件 SHA256(前16) 与登记一致（比对 40 个；其中 6 个为本轮已授权修改并以新哈希钉住；跳过 0）
```

---

## 6. 冻结哈希表复跑（7 个验证器，**独立静态复算** + 验证器自断言双向确认）

| 验证器 | 表构成 | 条目 |
|---|---|---|
| `ChunkStreamingValidation` | Frozen 40 | 40 |
| `GeologyRuntimeIntegrationValidation` | Critical 25 / Frozen 34 / Authorized 6 | 65 |
| `GeologyStageAValidation` | Frozen 51 | 51 |
| `GeologyStageBValidation` | Frozen 55 | 55 |
| `GeologyStageCValidation` | Frozen 55 / Authorized 2 | 57 |
| `GeologyStageDValidation` | Frozen 63 | 63 |
| `VoxelWorldRuntimeCoreValidation` | Frozen 18 | 18 |
| **合计** | — | **349** |

- 结果：**匹配 349 / 不符 0 / 缺失 0**（与 R38/R39 的 349/0/0 一致）。
- **4 个 R40 目标文件的登记数 = 0**（`WorldSkeletonFacts.cs`、`WorldV2ParameterPipelineValidation.cs`、`WorldSkeleton_V1_Minimal_Contract.md`、`WorldParameter_Contract.md`）⇒ **无哈希级联风险，本轮无需重钉任何条目**。
- 交叉确认：`GRI` 自身在真实运行中经 `GE7`（25 个 Critical）与 `GE9`（比对 40、授权 6、跳过 0）双向断言并通过。

---

## 7. 现场与环境（禁止项遵守）

| 项 | 状态 |
|---|---|
| `git commit` / `push` / `reset` / `clean` / `stash` / `checkout` / `git add` | **全部未执行**（全程仅 `rev-parse` / `log` / `status` / `diff` 等只读命令） |
| HEAD | `37246478ed354a1e9a0ce7a3a3d9457caf188062`（**未变**） |
| 暂存区 | **空**（`git diff --cached --name-only` 无输出） |
| `git status` 条目 | **27 条**（tracked `M` **20** + untracked 源文件 **4** + 证据目录 **3**）。R38 记录为 25 条 / 18 `M`，本轮新增的 2 个 `M` 即本轮修改、且在 R38 结束时**对 HEAD 无差异**的两个 tracked 文件：`Assets/Scripts/World/V2/WorldSkeletonFacts.cs`、`Specifications/World/WorldSkeleton_V1_Minimal_Contract.md`；`Specifications/World/WorldParameter_Contract.md` 在 R38 时已是 `M`（其 diff 内仍含更早轮次的 Region/Elevation 接线段），本轮仅改内容、条目数不变。**无新增文件、无删除文件**；4 个 untracked 源文件与 3 个证据目录本轮前后完全一致 |
| `git diff --stat` | `20 files changed, 390 insertions(+), 142 deletions(-)` |
| `_b3b_diff/` `_finalization_backup/` `_phase2b_validation/` | 仅新增本报告与 `round40_*` 日志；**未删除、未覆盖任何历史证据** |
| `_phase2b_validation/sealevel_round7_progress.md` | **未修改**：1032 行 / 87345 B / LastWriteTime `2026/10/18 22:19:37` 未变 |
| 生产逻辑 / 默认值 / 合法范围 / `HeightFunction` / Region / 地形 | **未改**（`DefaultSeaLevel = 64`、`[0, WorldHeight]`、严格 `<`、`Ocean` 输出类型均保持） |

---

## 8. 本轮三项测量缺陷（如实更正）

1. **progress 文件行数**：先用 `Get-Content`（默认 ANSI 解码）得 **759 行**，属**测量缺陷**；改用 `Get-Content -Encoding UTF8` / `[IO.File]::ReadAllLines` / `ReadAllText` 切分，三者一致得 **1032 行**（末元素为空 ⇒ 物理行 1031），与 R26–R39 记录一致，且 `LastWriteTime` 证明**文件未被本轮触碰**。
2. **`Check(` 计数口径**：R38 记录的是**行首** `Check(`（V2 = 45 / GRI = 56）；本轮若按「全部出现」会得 V2 = 46 / GRI = 60（含 1 处方法定义 + GRI 的 12 处行内 `Check(false, …)` 失败路径）。**两套口径下行首数完全一致（45 / 56）**，且 `GRI` 哈希 `E3DE56D14D75F010` 与 R38 记录相同 ⇒ **无文件漂移**；`GRI` 本轮未修改，其 47 条 PASS = 59 个调用点 − 12 条仅失败路径断言。
3. **`git status` 条目数**：本轮早先在报告中写作「25 条（18 `M`）」系沿用 R38 口径未复核，属**计数错误**；本轮实测 **27 条 = 20 `M` + 4 untracked 源文件 + 3 证据目录**，与 `git diff --stat` 的 `20 files changed` 自洽。差异来源已定位为本轮两个 tracked 文件由干净转为 `M`（`WorldSkeletonFacts.cs`、`WorldSkeleton_V1_Minimal_Contract.md`），**非**新增产物：Unity 批处理运行后 `git status` 中未出现任何新文件。

---

## 9. UNKNOWN / 本轮明确不声称

| 项 | 状态 |
|---|---|
| 端到端运行时验证（世界参数注入 `VoxelWorld` / Geology 生成链） | **未验证 / 不存在**：World V2 参数层生产调用者 = **0**（本轮未改变该事实） |
| 「真实世界 Ocean 分布已验证」 | **不成立，不得声称**：仅验证了查询级派生一致性与逐列一致性（35 列样本） |
| `TryEvaluate` 生产调用者 | **0**（迁移仅涉及验证器；生产代码零调用点，故无回归面） |
| `Ocean` 的 `float` 表示缺口（`PM32`） | **仍未处理**（`Ocean` 输出为 `bool`，与 `OceanRule` 的 `float` 返回值存在表示差异） |
| `OceanRule` 异常路径（缺 `Elevation` 时抛 `InvalidOperationException`） | **仍不可测**（`WorldParameterRules` 为内部类，无验证器覆盖） |
| Region 修正数据 | **仍为占位**（`PlaceholderAmplitude = 0.25f`，区间 `[0, 0.25)`） |
| `WorldHeight == SeaLevel == 64` ⇒ 语义退化为全海洋 | **仍待 owner 裁决**（本轮未改动） |
| 4 个 legacy core 验证器（`VoxelWorldCore` / `WorldGenerationCore` / `VoxelMeshCore` / `VoxelWorldView`） | **本轮未重跑**（R31 的 61/41/37/127 仍为历史证据，非本轮结论） |
| Unity 编译警告 4 条 | **未修复**（既有、与本次迁移无关；本轮不扩大改动面） |

---

## 10. 结论（与本轮批准要求逐条对照）

1. **代码迁移完成**：`TryEvaluate` 已去除 `int seaLevel`，`Ocean` 阈值唯一来源 = `settings.SeaLevel`；文件 121 → 120 行，SHA-256 `98B59916…DDA1C`。
2. **直接验证器同步迁移**：7 处调用点全部为 2 输入 + `out`；E13 重写、E14 保留强化、E15 改用非默认 `SeaLevel = 32`；断言数守恒 45；真实运行 **exit 0 / 45 PASS / 0 FAIL / 0 error CS / 0 崩溃**。
3. **两份契约文档同步迁移**：`WorldSkeleton_V1_Minimal_Contract.md` 10 行（含行数 120 与新哈希）+ `WorldParameter_Contract.md` 2 行；无「未来建议」被写成既成事实。
4. **冻结表未受影响**：349 / 0 / 0，4 个目标文件登记数 0；GRI 真实运行中 GE7/GE9 双向自断言通过。
5. **现场干净**：HEAD 未变、staged 空、进度记录 1032 行未改、未执行任何禁止的 git 写操作。
6. **未越界声称**：端到端接线、真实世界 Ocean 分布、「生产调用者存在」等均**未**声称；`PM32`、`OceanRule` 异常路径、Region 占位、`WorldHeight==SeaLevel` 语义等遗留项**原样保留**。