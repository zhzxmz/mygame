# 第十六轮审计报告：验证器交叉冻结哈希依赖图（只读）

- 分支 `main` / HEAD `37246478ed354a1e9a0ce7a3a3d9457caf188062`；本轮**未修改任何验证器、生产代码或登记值**。
- 生成方式：程序化复算 + 明细 CSV（`_phase2b_validation/round16_hash_audit_rows.csv`）。
- 复算算法：逐表核验后统一为 **SHA256 前 8 字节 → 16 位大写十六进制**（7 个验证器各自实现 `ComputeFileHash16`，失败标签分别为 CS12 / GE9 / GSA6 / GB7 / GC8 / GW10 / RCI15）。

## 1. 登记表清单

| # | 验证器文件 | 表名 | 登记项 | 不匹配 | 缺失 | 登记类别构成 |
|---|---|---|---|---|---|---|
| 1 | ChunkStreamingValidation | FrozenFiles | 40 |  | 0 | 生产 26 / 规范 9 / 验证器 5 |
| 2 | GeologyRuntimeIntegrationValidation | CriticalFiles | 25 | 0 | 0 | 生产 25 / 规范 0 / 验证器 0 |
| 3 | GeologyRuntimeIntegrationValidation | FrozenFiles | 34 |  | 0 | 生产 15 / 规范 14 / 验证器 5 |
| 4 | GeologyRuntimeIntegrationValidation | AuthorizedChangedFiles | 6 |  | 0 | 生产  / 规范 0 / 验证器 5 |
| 5 | GeologyStageAValidation | FrozenFiles | 51 | 9 | 0 | 生产 32 / 规范 13 / 验证器 6 |
| 6 | GeologyStageBValidation | FrozenFiles | 55 | 9 | 0 | 生产 35 / 规范 13 / 验证器 7 |
| 7 | GeologyStageCValidation | FrozenFiles | 55 | 9 | 0 | 生产 36 / 规范 13 / 验证器 6 |
| 8 | GeologyStageCValidation | AuthorizedChangedFiles | 2 | 0 | 0 | 生产 0 / 规范 0 / 验证器 2 |
| 9 | GeologyStageDValidation | FrozenFiles | 63 |  | 0 | 生产 40 / 规范 14 / 验证器 9 |
| 10 | VoxelWorldRuntimeCoreValidation | FrozenFiles | 18 | 0 | 0 | 生产 14 / 规范 0 / 验证器 4 |

合计：**9 张表 / 349 条登记 / 31 条不匹配 / 0 条文件缺失 / 0 条结构异常**。

## 2. 全部不匹配明细与来源分类

| 分类 | 验证器（表） | 被登记键 | 登记值 | 实际值 |
|---|---|---|---|---|
| A | ChunkStreamingValidation (FrozenFiles) | `Editor/VoxelWorldRuntimeCoreValidation.cs` | `0E0B5C160FEAB236` | `C5ABCECD2C338BED` |
| A | GeologyRuntimeIntegrationValidation (FrozenFiles) | `Editor/VoxelWorldRuntimeCoreValidation.cs` | `0E0B5C160FEAB236` | `C5ABCECD2C338BED` |
| B | GeologyRuntimeIntegrationValidation (AuthorizedChangedFiles) | `Editor/GeologyStageDValidation.cs` | `ABA08564B8C26871` | `A26D957C4B698097` |
| B | GeologyStageAValidation (FrozenFiles) | `../Specifications/World/VoxelWorld_Architecture.md` | `779DF1EFC4082579` | `F09B17A1002D7CD2` |
| B | GeologyStageAValidation (FrozenFiles) | `../Specifications/World/VoxelWorld_Core.md` | `F718B68E95100C57` | `6D471FFA184B13EC` |
| B | GeologyStageAValidation (FrozenFiles) | `../Specifications/World/WorldGeneration_Core.md` | `684753D5E6B31565` | `91F0FCDA380C91F7` |
| B | GeologyStageAValidation (FrozenFiles) | `../Specifications/World/WorldGeneration_Geology.md` | `1A7DD7E7AE1EBAC9` | `D1E70FE71BC400E1` |
| B | GeologyStageAValidation (FrozenFiles) | `../Specifications/World/WorldGeneration_Geology_Implementation.md` | `9BF0E11A4E5CA87D` | `2700AE40B0CB09A0` |
| C | GeologyStageAValidation (FrozenFiles) | `Scripts/VoxelWorld/Generation/WorldGenerationSettings.cs` | `140B7E289F05C6FA` | `7F11DEE8061E4FA1` |
| C | GeologyStageAValidation (FrozenFiles) | `Scripts/Game/VoxelWorldStreamingBootstrap.cs` | `4738634952568520` | `8A59289F0194D20F` |
| A | GeologyStageAValidation (FrozenFiles) | `Editor/VoxelWorldRuntimeCoreValidation.cs` | `0E0B5C160FEAB236` | `C5ABCECD2C338BED` |
| B | GeologyStageAValidation (FrozenFiles) | `Editor/ChunkStreamingValidation.cs` | `13E244B6EE978FC8` | `55AC4FFF4BFC13C8` |
| B | GeologyStageBValidation (FrozenFiles) | `../Specifications/World/VoxelWorld_Architecture.md` | `779DF1EFC4082579` | `F09B17A1002D7CD2` |
| B | GeologyStageBValidation (FrozenFiles) | `../Specifications/World/VoxelWorld_Core.md` | `F718B68E95100C57` | `6D471FFA184B13EC` |
| B | GeologyStageBValidation (FrozenFiles) | `../Specifications/World/WorldGeneration_Core.md` | `684753D5E6B31565` | `91F0FCDA380C91F7` |
| B | GeologyStageBValidation (FrozenFiles) | `../Specifications/World/WorldGeneration_Geology.md` | `1A7DD7E7AE1EBAC9` | `D1E70FE71BC400E1` |
| B | GeologyStageBValidation (FrozenFiles) | `../Specifications/World/WorldGeneration_Geology_Implementation.md` | `9BF0E11A4E5CA87D` | `2700AE40B0CB09A0` |
| C | GeologyStageBValidation (FrozenFiles) | `Scripts/VoxelWorld/Generation/WorldGenerationSettings.cs` | `140B7E289F05C6FA` | `7F11DEE8061E4FA1` |
| C | GeologyStageBValidation (FrozenFiles) | `Scripts/Game/VoxelWorldStreamingBootstrap.cs` | `4738634952568520` | `8A59289F0194D20F` |
| A | GeologyStageBValidation (FrozenFiles) | `Editor/VoxelWorldRuntimeCoreValidation.cs` | `0E0B5C160FEAB236` | `C5ABCECD2C338BED` |
| B | GeologyStageBValidation (FrozenFiles) | `Editor/ChunkStreamingValidation.cs` | `13E244B6EE978FC8` | `55AC4FFF4BFC13C8` |
| B | GeologyStageCValidation (FrozenFiles) | `../Specifications/World/VoxelWorld_Architecture.md` | `779DF1EFC4082579` | `F09B17A1002D7CD2` |
| B | GeologyStageCValidation (FrozenFiles) | `../Specifications/World/VoxelWorld_Core.md` | `F718B68E95100C57` | `6D471FFA184B13EC` |
| B | GeologyStageCValidation (FrozenFiles) | `../Specifications/World/WorldGeneration_Core.md` | `684753D5E6B31565` | `91F0FCDA380C91F7` |
| B | GeologyStageCValidation (FrozenFiles) | `../Specifications/World/WorldGeneration_Geology.md` | `1A7DD7E7AE1EBAC9` | `D1E70FE71BC400E1` |
| B | GeologyStageCValidation (FrozenFiles) | `../Specifications/World/WorldGeneration_Geology_Implementation.md` | `9BF0E11A4E5CA87D` | `2700AE40B0CB09A0` |
| C | GeologyStageCValidation (FrozenFiles) | `Scripts/VoxelWorld/Generation/WorldGenerationSettings.cs` | `140B7E289F05C6FA` | `7F11DEE8061E4FA1` |
| C | GeologyStageCValidation (FrozenFiles) | `Scripts/Game/VoxelWorldStreamingBootstrap.cs` | `4738634952568520` | `8A59289F0194D20F` |
| A | GeologyStageCValidation (FrozenFiles) | `Editor/VoxelWorldRuntimeCoreValidation.cs` | `0E0B5C160FEAB236` | `C5ABCECD2C338BED` |
| B | GeologyStageCValidation (FrozenFiles) | `Editor/ChunkStreamingValidation.cs` | `13E244B6EE978FC8` | `55AC4FFF4BFC13C8` |
| A | GeologyStageDValidation (FrozenFiles) | `Editor/VoxelWorldRuntimeCoreValidation.cs` | `0E0B5C160FEAB236` | `C5ABCECD2C338BED` |

分类口径：
- **A**＝本轮（第十五轮）已确认的合法修改导致的新增失效：`Editor/VoxelWorldRuntimeCoreValidation.cs`（0E0B5C160FEAB236 → C5ABCECD2C338BED），共 6 处。
- **B**＝此前已发生、但此前未被发现或未报告的失效：GRI 对 `Editor/GeologyStageDValidation.cs`（1 处，第十三轮编辑所致）；Stage A/B/C 对 `Editor/ChunkStreamingValidation.cs`（3 处，第十一/十二轮所致）；Stage A/B/C 对 5 份规范的登记（15 处，早前提交的规范更新；实际值与 StageD/GRI 已校验通过的值一致）。合计 19 处。
- **C**＝已有充分证据确认属批准修改、但尚未同步：`WorldGenerationSettings.cs` 与 `VoxelWorldStreamingBootstrap.cs` 在 Stage A/B/C 中的登记（6 处）。这两个文件已在 StageD / CS12 / GRI / RTC 四处登记为同一新值。
- **D**＝原因不明：**0 处**。
- **E**＝文件缺失或结构异常：**0 处**（349 条登记对应文件全部存在）。

## 3. 验证器交叉依赖边（验证器 A → 登记了验证器 B 的哈希）

| 发起方 | 表 | 被登记验证器 | 状态 | 登记值 | 实际值 |
|---|---|---|---|---|---|
| ChunkStreamingValidation | FrozenFiles | `Editor/VoxelWorldCoreValidation.cs` | 匹配 | `6DE813AABCE95394` | `6DE813AABCE95394` |
| ChunkStreamingValidation | FrozenFiles | `Editor/WorldGenerationCoreValidation.cs` | 匹配 | `4A517A0F902A1066` | `4A517A0F902A1066` |
| ChunkStreamingValidation | FrozenFiles | `Editor/VoxelMeshCoreValidation.cs` | 匹配 | `5FFD8451D2CE05EB` | `5FFD8451D2CE05EB` |
| ChunkStreamingValidation | FrozenFiles | `Editor/VoxelWorldViewValidation.cs` | 匹配 | `BEFBAAF6B111A241` | `BEFBAAF6B111A241` |
| ChunkStreamingValidation | FrozenFiles | `Editor/VoxelWorldRuntimeCoreValidation.cs` | 不匹配 | `0E0B5C160FEAB236` | `C5ABCECD2C338BED` |
| GeologyRuntimeIntegrationValidation | FrozenFiles | `Editor/VoxelWorldCoreValidation.cs` | 匹配 | `6DE813AABCE95394` | `6DE813AABCE95394` |
| GeologyRuntimeIntegrationValidation | FrozenFiles | `Editor/WorldGenerationCoreValidation.cs` | 匹配 | `4A517A0F902A1066` | `4A517A0F902A1066` |
| GeologyRuntimeIntegrationValidation | FrozenFiles | `Editor/VoxelMeshCoreValidation.cs` | 匹配 | `5FFD8451D2CE05EB` | `5FFD8451D2CE05EB` |
| GeologyRuntimeIntegrationValidation | FrozenFiles | `Editor/VoxelWorldViewValidation.cs` | 匹配 | `BEFBAAF6B111A241` | `BEFBAAF6B111A241` |
| GeologyRuntimeIntegrationValidation | FrozenFiles | `Editor/VoxelWorldRuntimeCoreValidation.cs` | 不匹配 | `0E0B5C160FEAB236` | `C5ABCECD2C338BED` |
| GeologyRuntimeIntegrationValidation | AuthorizedChangedFiles | `Editor/ChunkStreamingValidation.cs` | 匹配 | `55AC4FFF4BFC13C8` | `55AC4FFF4BFC13C8` |
| GeologyRuntimeIntegrationValidation | AuthorizedChangedFiles | `Editor/GeologyStageAValidation.cs` | 匹配 | `DF42F8605BA60CAF` | `DF42F8605BA60CAF` |
| GeologyRuntimeIntegrationValidation | AuthorizedChangedFiles | `Editor/GeologyStageBValidation.cs` | 匹配 | `CFA08E4EE8D49C99` | `CFA08E4EE8D49C99` |
| GeologyRuntimeIntegrationValidation | AuthorizedChangedFiles | `Editor/GeologyStageCValidation.cs` | 匹配 | `4379D1DD622EB737` | `4379D1DD622EB737` |
| GeologyRuntimeIntegrationValidation | AuthorizedChangedFiles | `Editor/GeologyStageDValidation.cs` | 不匹配 | `ABA08564B8C26871` | `A26D957C4B698097` |
| GeologyStageAValidation | FrozenFiles | `Editor/VoxelWorldCoreValidation.cs` | 匹配 | `6DE813AABCE95394` | `6DE813AABCE95394` |
| GeologyStageAValidation | FrozenFiles | `Editor/WorldGenerationCoreValidation.cs` | 匹配 | `4A517A0F902A1066` | `4A517A0F902A1066` |
| GeologyStageAValidation | FrozenFiles | `Editor/VoxelMeshCoreValidation.cs` | 匹配 | `5FFD8451D2CE05EB` | `5FFD8451D2CE05EB` |
| GeologyStageAValidation | FrozenFiles | `Editor/VoxelWorldViewValidation.cs` | 匹配 | `BEFBAAF6B111A241` | `BEFBAAF6B111A241` |
| GeologyStageAValidation | FrozenFiles | `Editor/VoxelWorldRuntimeCoreValidation.cs` | 不匹配 | `0E0B5C160FEAB236` | `C5ABCECD2C338BED` |
| GeologyStageAValidation | FrozenFiles | `Editor/ChunkStreamingValidation.cs` | 不匹配 | `13E244B6EE978FC8` | `55AC4FFF4BFC13C8` |
| GeologyStageBValidation | FrozenFiles | `Editor/VoxelWorldCoreValidation.cs` | 匹配 | `6DE813AABCE95394` | `6DE813AABCE95394` |
| GeologyStageBValidation | FrozenFiles | `Editor/WorldGenerationCoreValidation.cs` | 匹配 | `4A517A0F902A1066` | `4A517A0F902A1066` |
| GeologyStageBValidation | FrozenFiles | `Editor/VoxelMeshCoreValidation.cs` | 匹配 | `5FFD8451D2CE05EB` | `5FFD8451D2CE05EB` |
| GeologyStageBValidation | FrozenFiles | `Editor/VoxelWorldViewValidation.cs` | 匹配 | `BEFBAAF6B111A241` | `BEFBAAF6B111A241` |
| GeologyStageBValidation | FrozenFiles | `Editor/VoxelWorldRuntimeCoreValidation.cs` | 不匹配 | `0E0B5C160FEAB236` | `C5ABCECD2C338BED` |
| GeologyStageBValidation | FrozenFiles | `Editor/ChunkStreamingValidation.cs` | 不匹配 | `13E244B6EE978FC8` | `55AC4FFF4BFC13C8` |
| GeologyStageBValidation | FrozenFiles | `Editor/GeologyStageAValidation.cs` | 匹配 | `DF42F8605BA60CAF` | `DF42F8605BA60CAF` |
| GeologyStageCValidation | FrozenFiles | `Editor/VoxelWorldCoreValidation.cs` | 匹配 | `6DE813AABCE95394` | `6DE813AABCE95394` |
| GeologyStageCValidation | FrozenFiles | `Editor/WorldGenerationCoreValidation.cs` | 匹配 | `4A517A0F902A1066` | `4A517A0F902A1066` |
| GeologyStageCValidation | FrozenFiles | `Editor/VoxelMeshCoreValidation.cs` | 匹配 | `5FFD8451D2CE05EB` | `5FFD8451D2CE05EB` |
| GeologyStageCValidation | FrozenFiles | `Editor/VoxelWorldViewValidation.cs` | 匹配 | `BEFBAAF6B111A241` | `BEFBAAF6B111A241` |
| GeologyStageCValidation | FrozenFiles | `Editor/VoxelWorldRuntimeCoreValidation.cs` | 不匹配 | `0E0B5C160FEAB236` | `C5ABCECD2C338BED` |
| GeologyStageCValidation | FrozenFiles | `Editor/ChunkStreamingValidation.cs` | 不匹配 | `13E244B6EE978FC8` | `55AC4FFF4BFC13C8` |
| GeologyStageCValidation | AuthorizedChangedFiles | `Editor/GeologyStageAValidation.cs` | 匹配 | `DF42F8605BA60CAF` | `DF42F8605BA60CAF` |
| GeologyStageCValidation | AuthorizedChangedFiles | `Editor/GeologyStageBValidation.cs` | 匹配 | `CFA08E4EE8D49C99` | `CFA08E4EE8D49C99` |
| GeologyStageDValidation | FrozenFiles | `Editor/VoxelWorldCoreValidation.cs` | 匹配 | `6DE813AABCE95394` | `6DE813AABCE95394` |
| GeologyStageDValidation | FrozenFiles | `Editor/WorldGenerationCoreValidation.cs` | 匹配 | `4A517A0F902A1066` | `4A517A0F902A1066` |
| GeologyStageDValidation | FrozenFiles | `Editor/VoxelMeshCoreValidation.cs` | 匹配 | `5FFD8451D2CE05EB` | `5FFD8451D2CE05EB` |
| GeologyStageDValidation | FrozenFiles | `Editor/VoxelWorldViewValidation.cs` | 匹配 | `BEFBAAF6B111A241` | `BEFBAAF6B111A241` |
| GeologyStageDValidation | FrozenFiles | `Editor/VoxelWorldRuntimeCoreValidation.cs` | 不匹配 | `0E0B5C160FEAB236` | `C5ABCECD2C338BED` |
| GeologyStageDValidation | FrozenFiles | `Editor/ChunkStreamingValidation.cs` | 匹配 | `55AC4FFF4BFC13C8` | `55AC4FFF4BFC13C8` |
| GeologyStageDValidation | FrozenFiles | `Editor/GeologyStageAValidation.cs` | 匹配 | `DF42F8605BA60CAF` | `DF42F8605BA60CAF` |
| GeologyStageDValidation | FrozenFiles | `Editor/GeologyStageBValidation.cs` | 匹配 | `CFA08E4EE8D49C99` | `CFA08E4EE8D49C99` |
| GeologyStageDValidation | FrozenFiles | `Editor/GeologyStageCValidation.cs` | 匹配 | `4379D1DD622EB737` | `4379D1DD622EB737` |
| VoxelWorldRuntimeCoreValidation | FrozenFiles | `Editor/VoxelWorldCoreValidation.cs` | 匹配 | `6DE813AABCE95394` | `6DE813AABCE95394` |
| VoxelWorldRuntimeCoreValidation | FrozenFiles | `Editor/WorldGenerationCoreValidation.cs` | 匹配 | `4A517A0F902A1066` | `4A517A0F902A1066` |
| VoxelWorldRuntimeCoreValidation | FrozenFiles | `Editor/VoxelMeshCoreValidation.cs` | 匹配 | `5FFD8451D2CE05EB` | `5FFD8451D2CE05EB` |
| VoxelWorldRuntimeCoreValidation | FrozenFiles | `Editor/VoxelWorldViewValidation.cs` | 匹配 | `BEFBAAF6B111A241` | `BEFBAAF6B111A241` |

### 反向计数：每个验证器被多少张表登记

| 被登记验证器 | 被登记次数 | 其中失效 |
|---|---|---|
| `Editor/ChunkStreamingValidation.cs` | 5 | 3 |
| `Editor/GeologyStageAValidation.cs` | 4 | 0 |
| `Editor/GeologyStageBValidation.cs` | 3 | 0 |
| `Editor/GeologyStageCValidation.cs` | 2 | 0 |
| `Editor/GeologyStageDValidation.cs` | 1 | 1 |
| `Editor/VoxelMeshCoreValidation.cs` | 7 | 0 |
| `Editor/VoxelWorldCoreValidation.cs` | 7 | 0 |
| `Editor/VoxelWorldRuntimeCoreValidation.cs` | 6 | 6 |
| `Editor/VoxelWorldViewValidation.cs` | 7 | 0 |
| `Editor/WorldGenerationCoreValidation.cs` | 7 | 0 |

### 结构与环路
- 环路检查：依赖方向严格为「旧阶段 → 新阶段」（A→∅；B→A；C→A,B；D→A,B,C；GRI→A,B,C,D,CS；CS/RTC→仅 4 个 legacy core 验证器），**未发现 A→B 与 B→A 同时存在的情形，无循环依赖**。
- 重复登记：4 个 legacy core 验证器（VoxelWorldCore / WorldGenerationCore / VoxelMeshCore / VoxelWorldView）各被 7 张表登记；`VoxelWorldRuntimeCoreValidation.cs` 被 6 张；`ChunkStreamingValidation.cs` 被 5 张；`GeologyStageDValidation.cs` 仅被 1 张（GRI）。
- 自钉：无任何验证器登记自身哈希（`GeologyStageDValidation` / `GeologyRuntimeIntegrationValidation` / `VoxelWorldRuntimeCoreValidation` 均不自钉）。
- 影响面推算：修改 `ChunkStreamingValidation.cs` 最多影响 5 张表；修改 `VoxelWorldRuntimeCoreValidation.cs` 影响 6 张；修改 4 个 legacy core 验证器影响 7 张。

## 4. 当前真实运行结果 vs 历史运行结果

| 验证器 | 会话内最近一次真实运行 | 当时的结论 | 当前工作树下复算 | 当前若复跑是否仍能通过 |
|---|---|---|---|---|
| VoxelWorldRuntimeCoreValidation | R15：exit 0 / Checks 196 / Failures 0 | PASS | 18 条全匹配 | **是（唯一当前可用 PASS 证据）** |
| GeologyStageDValidation | R13：exit 0 / Checks 44 / Failures 0 | PASS | 63 条中 1 条失效（RTC） | **否**（R15 编辑后失效） |
| GeologyRuntimeIntegrationValidation | R12：exit 0 / Checks 45 / Failures 0 | PASS | 65 条中 2 条失效 | **否**（R13+R15 编辑后失效） |
| ChunkStreamingValidation | R11：exit 0 / Checks 107 / Failures 0 | PASS | 40 条中 1 条失效 | **否**（R15 编辑后失效） |
| GeologyStageAValidation | **本会话从未运行** | 无证据 | 51 条中 9 条失效 | **否**，且状态未知 |
| GeologyStageBValidation | **本会话从未运行** | 无证据 | 55 条中 9 条失效 | **否**，且状态未知 |
| GeologyStageCValidation | **本会话从未运行** | 无证据 | 57 条中 9 条失效 | **否**，且状态未知 |
| VoxelWorldCore / WorldGenerationCore / VoxelMeshCore / VoxelWorldView | **本会话从未运行** | 无证据 | 被登记值全部匹配 | **未知（不得标记为 PASS）** |

## 5. 按依赖顺序的最小修复计划（本轮不实施）

修复顺序遵循「先被钉者、后钉人」，避免边修边失效：

1. **叶子层（不自钉者）先修**：Stage A/B/C 各自 9 条（含 5 规范 + 2 SeaLevel 生产文件 + ChunkStreamingValidation + RuntimeCoreValidation）。每个验证器单独一轮、逐条审查，禁止批量重钉。
2. **再修 ChunkStreamingValidation（CS12）1 条**：RuntimeCoreValidation 0E0B5C160FEAB236 → C5ABCECD2C338BED。
3. **再修 StageD（GW10）1 条**：同上新值。
4. **最后修 GRI（GE9）2 条**：RuntimeCoreValidation（同上）与 `Editor/GeologyStageDValidation.cs` ABA08564B8C26871 → A26D957C4B698097。注意该值会在 StageD 被再次编辑时继续变化，故 GRI 必须最后修。
5. RTC（RCI15）当前 0 失效，无需改动。

风险提示：第 1 步会再次改动 Stage A/B/C 三个验证器文件，从而使其被钉处（GRI、StageD、StageC 等）再次失效；因此实际执行时**必须把「最后一轮编辑」安排在依赖链末端**，或接受一次末端集中重钉。

## 6. 减少验证器互钉的设计代价与收益（仅供决策，未实施）

现状：互钉使「任一验证器被编辑」→ 约 5～7 处登记同时失效，形成 N 方锁步。

| 方案 | 收益 | 代价/风险 |
|---|---|---|
| 维持现状（互钉） | 强约束：任何验证器被篡改都能被多张表发现 | 每次合法编辑都要连锁重钉，历史结论快速过期；如本轮 31 条失效中 25 条属纯登记滞后 |
| 只钉生产代码，不钉验证器 | 编辑验证器不再引发级联；语义上「冻结」只针对被测代码 | 失去对验证器自身被弱化/篡改的交叉检测（可改为「被钉验证器清单 = 断言条数快照」等弱形式） |
| 只由一张「主表」钉全部验证器 | 单一事实来源，重钉集中在一处 | 单点维护；主表缺失即全盘失守；需明确谁是主表 |
| 哈希改为「语义指纹」（断言数 + 关键符号集合） | 对纯注释/格式改动免疫，降低噪声 | 需要设计指纹定义，属**数据契约/架构变更**，须所有者批准 |

## 7. 三类结论的严格区分

- **当前哈希基线一致性**：349 条登记中 318 条匹配、31 条不匹配、0 缺失；不匹配 100% 已有 diff/历史证据解释（A 6 / B 19 / C 6 / D 0）。
- **当前验证器实际执行结果**：本会话内真实运行的 4 个验证器中，仅 `VoxelWorldRuntimeCoreValidation` 当前仍处于 PASS 状态（R15 实跑证据）；另 3 个（StageD / GRI / CS12）当前复算已失效，需重钉后才能恢复 PASS；Stage A/B/C 与 4 个 legacy core 验证器**本会话从未运行**，状态未知。
- **生产功能正确性**：本轮为哈希/依赖审计，**不构成任何功能正确性证明**。哈希一致只代表「文件未被改动」，不代表行为正确；反之哈希失效也不代表功能回归（本轮 31 条失效中无一条对应功能断言失败）。
