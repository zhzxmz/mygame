# 第十八轮：Stage B 冻结哈希修复与交叉影响增量记录（追加式，不覆盖第十六、十七轮记录）

- HEAD 37246478；本轮唯一源文件改动：`Assets/Editor/GeologyStageBValidation.cs`（10 条登记值替换，+10/−10）
- 第十六轮 round16_hash_dependency_audit.md 与第十七轮 round17_hash_dependency_audit_delta.md 均未被改动

## 1. 已修复的 10 条

| 分组 | 键 | 旧值 | 新值 |
|---|---|---|---|
| 历史9 | `../Specifications/World/VoxelWorld_Architecture.md` | `779DF1EFC4082579` | `F09B17A1002D7CD2` |
| 历史9 | `../Specifications/World/VoxelWorld_Core.md` | `F718B68E95100C57` | `6D471FFA184B13EC` |
| 历史9 | `../Specifications/World/WorldGeneration_Core.md` | `684753D5E6B31565` | `91F0FCDA380C91F7` |
| 历史9 | `../Specifications/World/WorldGeneration_Geology.md` | `1A7DD7E7AE1EBAC9` | `D1E70FE71BC400E1` |
| 历史9 | `../Specifications/World/WorldGeneration_Geology_Implementation.md` | `9BF0E11A4E5CA87D` | `2700AE40B0CB09A0` |
| 历史9 | `Scripts/VoxelWorld/Generation/WorldGenerationSettings.cs` | `140B7E289F05C6FA` | `7F11DEE8061E4FA1` |
| 历史9 | `Scripts/Game/VoxelWorldStreamingBootstrap.cs` | `4738634952568520` | `8A59289F0194D20F` |
| 历史9 | `Editor/VoxelWorldRuntimeCoreValidation.cs` | `0E0B5C160FEAB236` | `C5ABCECD2C338BED` |
| 历史9 | `Editor/ChunkStreamingValidation.cs` | `13E244B6EE978FC8` | `55AC4FFF4BFC13C8` |
| R17新增 | `Editor/GeologyStageAValidation.cs` | `DF42F8605BA60CAF` | `83731D5FB896BA38` |

## 2. 本轮新增的交叉失效（3 处，均为 GeologyStageBValidation.cs 旧钉）

| 登记表 | 位置 | 旧值 | 新值 |
|---|---|---|---|
| GeologyStageCValidation.AuthorizedChangedFiles | `Editor/GeologyStageBValidation.cs` | `CFA08E4EE8D49C99` | `B5EE9A249FCF5AE3` |
| GeologyRuntimeIntegrationValidation.AuthorizedChangedFiles | `Editor/GeologyStageBValidation.cs` | `CFA08E4EE8D49C99` | `B5EE9A249FCF5AE3` |
| GeologyStageDValidation.FrozenFiles | `Editor/GeologyStageBValidation.cs` | `CFA08E4EE8D49C99` | `B5EE9A249FCF5AE3` |

## 3. 当前仍存在的 16 条不匹配（历史遗留，本轮按范围限定未处理）

| 验证器 | 表 | 键 | 登记值 | 实际值 |
|---|---|---|---|---|
| ChunkStreamingValidation | FrozenFiles | `Editor/VoxelWorldRuntimeCoreValidation.cs` | `0E0B5C160FEAB236` | `C5ABCECD2C338BED` |
| GeologyRuntimeIntegrationValidation | AuthorizedChangedFiles | `Editor/GeologyStageAValidation.cs` | `DF42F8605BA60CAF` | `83731D5FB896BA38` |
| GeologyRuntimeIntegrationValidation | AuthorizedChangedFiles | `Editor/GeologyStageDValidation.cs` | `ABA08564B8C26871` | `A26D957C4B698097` |
| GeologyRuntimeIntegrationValidation | FrozenFiles | `Editor/VoxelWorldRuntimeCoreValidation.cs` | `0E0B5C160FEAB236` | `C5ABCECD2C338BED` |
| GeologyStageCValidation | AuthorizedChangedFiles | `Editor/GeologyStageAValidation.cs` | `DF42F8605BA60CAF` | `83731D5FB896BA38` |
| GeologyStageCValidation | FrozenFiles | `../Specifications/World/VoxelWorld_Architecture.md` | `779DF1EFC4082579` | `F09B17A1002D7CD2` |
| GeologyStageCValidation | FrozenFiles | `../Specifications/World/VoxelWorld_Core.md` | `F718B68E95100C57` | `6D471FFA184B13EC` |
| GeologyStageCValidation | FrozenFiles | `../Specifications/World/WorldGeneration_Core.md` | `684753D5E6B31565` | `91F0FCDA380C91F7` |
| GeologyStageCValidation | FrozenFiles | `../Specifications/World/WorldGeneration_Geology.md` | `1A7DD7E7AE1EBAC9` | `D1E70FE71BC400E1` |
| GeologyStageCValidation | FrozenFiles | `../Specifications/World/WorldGeneration_Geology_Implementation.md` | `9BF0E11A4E5CA87D` | `2700AE40B0CB09A0` |
| GeologyStageCValidation | FrozenFiles | `Editor/ChunkStreamingValidation.cs` | `13E244B6EE978FC8` | `55AC4FFF4BFC13C8` |
| GeologyStageCValidation | FrozenFiles | `Editor/VoxelWorldRuntimeCoreValidation.cs` | `0E0B5C160FEAB236` | `C5ABCECD2C338BED` |
| GeologyStageCValidation | FrozenFiles | `Scripts/Game/VoxelWorldStreamingBootstrap.cs` | `4738634952568520` | `8A59289F0194D20F` |
| GeologyStageCValidation | FrozenFiles | `Scripts/VoxelWorld/Generation/WorldGenerationSettings.cs` | `140B7E289F05C6FA` | `7F11DEE8061E4FA1` |
| GeologyStageDValidation | FrozenFiles | `Editor/GeologyStageAValidation.cs` | `DF42F8605BA60CAF` | `83731D5FB896BA38` |
| GeologyStageDValidation | FrozenFiles | `Editor/VoxelWorldRuntimeCoreValidation.cs` | `0E0B5C160FEAB236` | `C5ABCECD2C338BED` |

## 4. 结构与运行证据

- GeologyStageBValidation.cs：1167 行（不变）/ FrozenFiles 55 条（不变）/ `Check(` 59（不变）/ git numstat +10 −10 / 复算 55 条 0 不匹配
- Stage B 真实复跑：exit 0 / Checks 122 / Failures 0 / PASS 122 / error CS 0 / warning CS 4（2 条唯一警告×2，既有非新增）
- GB7：既有文件全部存在（缺失 0）PASS；SHA256(前16) 全部不变（比对 55 个；跳过 0）PASS
- 日志：_phase2b_validation/round18_GeologyStageBValidation_rerun.log
- 级联校验：基线 26 − 修复 10 + 新增 3 = 19（实测 19）