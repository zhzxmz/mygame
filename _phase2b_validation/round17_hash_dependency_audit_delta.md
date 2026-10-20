# 第十七轮：Stage A 冻结哈希修复与交叉影响增量记录（追加式，不覆盖第十六轮审计）

- 生成时间起点：HEAD 37246478 / 第十六轮审计 round16_hash_dependency_audit.md 保持原样未改
- 本轮唯一源文件改动：`Assets/Editor/GeologyStageAValidation.cs`（9 条登记值替换，+9/−9）

## 1. 已修复的 9 条（第十六轮 C 类）

| 键 | 旧值 | 新值 |
|---|---|---|
| `../Specifications/World/VoxelWorld_Architecture.md` | `779DF1EFC4082579` | `F09B17A1002D7CD2` |
| `../Specifications/World/VoxelWorld_Core.md` | `F718B68E95100C57` | `6D471FFA184B13EC` |
| `../Specifications/World/WorldGeneration_Core.md` | `684753D5E6B31565` | `91F0FCDA380C91F7` |
| `../Specifications/World/WorldGeneration_Geology.md` | `1A7DD7E7AE1EBAC9` | `D1E70FE71BC400E1` |
| `../Specifications/World/WorldGeneration_Geology_Implementation.md` | `9BF0E11A4E5CA87D` | `2700AE40B0CB09A0` |
| `Scripts/VoxelWorld/Generation/WorldGenerationSettings.cs` | `140B7E289F05C6FA` | `7F11DEE8061E4FA1` |
| `Scripts/Game/VoxelWorldStreamingBootstrap.cs` | `4738634952568520` | `8A59289F0194D20F` |
| `Editor/VoxelWorldRuntimeCoreValidation.cs` | `0E0B5C160FEAB236` | `C5ABCECD2C338BED` |
| `Editor/ChunkStreamingValidation.cs` | `13E244B6EE978FC8` | `55AC4FFF4BFC13C8` |

## 2. 当前不匹配集合（26 条）

| 验证器 | 表 | 键 | 登记值 | 实际值 |
|---|---|---|---|---|
| ChunkStreamingValidation | FrozenFiles | `Editor/VoxelWorldRuntimeCoreValidation.cs` | `0E0B5C160FEAB236` | `C5ABCECD2C338BED` |
| GeologyRuntimeIntegrationValidation | FrozenFiles | `Editor/VoxelWorldRuntimeCoreValidation.cs` | `0E0B5C160FEAB236` | `C5ABCECD2C338BED` |
| GeologyRuntimeIntegrationValidation | AuthorizedChangedFiles | `Editor/GeologyStageAValidation.cs` | `DF42F8605BA60CAF` | `83731D5FB896BA38` |
| GeologyRuntimeIntegrationValidation | AuthorizedChangedFiles | `Editor/GeologyStageDValidation.cs` | `ABA08564B8C26871` | `A26D957C4B698097` |
| GeologyStageBValidation | FrozenFiles | `../Specifications/World/VoxelWorld_Architecture.md` | `779DF1EFC4082579` | `F09B17A1002D7CD2` |
| GeologyStageBValidation | FrozenFiles | `../Specifications/World/VoxelWorld_Core.md` | `F718B68E95100C57` | `6D471FFA184B13EC` |
| GeologyStageBValidation | FrozenFiles | `../Specifications/World/WorldGeneration_Core.md` | `684753D5E6B31565` | `91F0FCDA380C91F7` |
| GeologyStageBValidation | FrozenFiles | `../Specifications/World/WorldGeneration_Geology.md` | `1A7DD7E7AE1EBAC9` | `D1E70FE71BC400E1` |
| GeologyStageBValidation | FrozenFiles | `../Specifications/World/WorldGeneration_Geology_Implementation.md` | `9BF0E11A4E5CA87D` | `2700AE40B0CB09A0` |
| GeologyStageBValidation | FrozenFiles | `Scripts/VoxelWorld/Generation/WorldGenerationSettings.cs` | `140B7E289F05C6FA` | `7F11DEE8061E4FA1` |
| GeologyStageBValidation | FrozenFiles | `Scripts/Game/VoxelWorldStreamingBootstrap.cs` | `4738634952568520` | `8A59289F0194D20F` |
| GeologyStageBValidation | FrozenFiles | `Editor/VoxelWorldRuntimeCoreValidation.cs` | `0E0B5C160FEAB236` | `C5ABCECD2C338BED` |
| GeologyStageBValidation | FrozenFiles | `Editor/ChunkStreamingValidation.cs` | `13E244B6EE978FC8` | `55AC4FFF4BFC13C8` |
| GeologyStageBValidation | FrozenFiles | `Editor/GeologyStageAValidation.cs` | `DF42F8605BA60CAF` | `83731D5FB896BA38` |
| GeologyStageCValidation | FrozenFiles | `../Specifications/World/VoxelWorld_Architecture.md` | `779DF1EFC4082579` | `F09B17A1002D7CD2` |
| GeologyStageCValidation | FrozenFiles | `../Specifications/World/VoxelWorld_Core.md` | `F718B68E95100C57` | `6D471FFA184B13EC` |
| GeologyStageCValidation | FrozenFiles | `../Specifications/World/WorldGeneration_Core.md` | `684753D5E6B31565` | `91F0FCDA380C91F7` |
| GeologyStageCValidation | FrozenFiles | `../Specifications/World/WorldGeneration_Geology.md` | `1A7DD7E7AE1EBAC9` | `D1E70FE71BC400E1` |
| GeologyStageCValidation | FrozenFiles | `../Specifications/World/WorldGeneration_Geology_Implementation.md` | `9BF0E11A4E5CA87D` | `2700AE40B0CB09A0` |
| GeologyStageCValidation | FrozenFiles | `Scripts/VoxelWorld/Generation/WorldGenerationSettings.cs` | `140B7E289F05C6FA` | `7F11DEE8061E4FA1` |
| GeologyStageCValidation | FrozenFiles | `Scripts/Game/VoxelWorldStreamingBootstrap.cs` | `4738634952568520` | `8A59289F0194D20F` |
| GeologyStageCValidation | FrozenFiles | `Editor/VoxelWorldRuntimeCoreValidation.cs` | `0E0B5C160FEAB236` | `C5ABCECD2C338BED` |
| GeologyStageCValidation | FrozenFiles | `Editor/ChunkStreamingValidation.cs` | `13E244B6EE978FC8` | `55AC4FFF4BFC13C8` |
| GeologyStageCValidation | AuthorizedChangedFiles | `Editor/GeologyStageAValidation.cs` | `DF42F8605BA60CAF` | `83731D5FB896BA38` |
| GeologyStageDValidation | FrozenFiles | `Editor/VoxelWorldRuntimeCoreValidation.cs` | `0E0B5C160FEAB236` | `C5ABCECD2C338BED` |
| GeologyStageDValidation | FrozenFiles | `Editor/GeologyStageAValidation.cs` | `DF42F8605BA60CAF` | `83731D5FB896BA38` |

## 3. 与第十六轮相比的变化

- 已修复：9 条（全部为 Stage A 的 9 条 C 类登记）
- 本轮新引入：4 条（本轮编辑 GeologyStageAValidation.cs 所波及的登记处）
  - `GeologyStageDValidation|FrozenFiles|Editor/GeologyStageAValidation.cs`
  - `GeologyRuntimeIntegrationValidation|AuthorizedChangedFiles|Editor/GeologyStageAValidation.cs`
  - `GeologyStageCValidation|AuthorizedChangedFiles|Editor/GeologyStageAValidation.cs`
  - `GeologyStageBValidation|FrozenFiles|Editor/GeologyStageAValidation.cs`
- 仍然存在（历史遗留，本轮按范围限定未处理）：22 条

## 4. 结构与运行证据

- GeologyStageAValidation.cs：行数 1088（不变）/ FrozenFiles 51 条（不变）/ `Check(` 83（不变）/ git numstat +9 −9
- Stage A 真实复跑：exit 0 / Checks 105 / Failures 0 / PASS 105 / error CS 0 / warning CS 4（2 条唯一警告×2，既有）
- GSA6：既有文件全部存在（缺失 0）PASS；SHA256(前16) 全部不变（比对 51 个；跳过 0）PASS
- 日志：_phase2b_validation/round17_GeologyStageAValidation_rerun.log