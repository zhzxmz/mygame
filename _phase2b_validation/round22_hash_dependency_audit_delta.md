# 第二十二轮：Stage B 两条冻结哈希最小修复、真实验证与级联增量记录

- HEAD 37246478；本轮唯一源文件改动：`Assets/Editor/GeologyStageBValidation.cs`（2 条登记值替换）
- 第十六至二十一轮审计文件与全部历史日志未被改动或覆盖

## 1. 两条差异的复核证据

| 键 | 登记值 | 实测值 | 独立证据 |
|---|---|---|---|
| `Editor/ChunkStreamingValidation.cs` | `55AC4FFF4BFC13C8` | `BEB05E3DD16D48F3` | 第二十轮 CS12 真实运行通过（exit0/107 PASS，CS12 比对 40 个全匹配）；本轮实测该文件哈希 = 同值（True） |
| `Editor/GeologyStageAValidation.cs` | `83731D5FB896BA38` | `67DB0CD6161EE4F4` | 第二十一轮 StageA 真实运行通过（exit0/105 PASS，GSA6 比对 51 个全匹配）；本轮实测该文件哈希 = 同值（True） |

- 全量重测 StageB 55 条：不匹配恰 **2** 条、缺失 0，与第二十一轮基线逐字段一致

## 2. 精确 diff 与结构计数

| 项 | 修改前 | 修改后 |
|---|---|---|
| 文件行数 | 1167 | 1167 |
| FrozenFiles 条目 | 55 | 55 |
| AuthorizedChangedFiles | 该验证器无此表 | 无 |
| `Check(` 调用 | 59 | 59 |
| 55 条复算不匹配 | 2 | **0** |
| 旧值残留 | — | `55AC4FFF4BFC13C8`=0 / `83731D5FB896BA38`=0 |
| 新值计数 | — | `BEB05E3DD16D48F3`=1 / `67DB0CD6161EE4F4`=1 |

本轮两条替换（值替换，行数不变，故相对 HEAD 的 numstat 仍为 +10/−10，含第 18 轮的 10 条）：

```
-            { "Editor/ChunkStreamingValidation.cs", "55AC4FFF4BFC13C8" },
+            { "Editor/ChunkStreamingValidation.cs", "BEB05E3DD16D48F3" },
-            { "Editor/GeologyStageAValidation.cs", "83731D5FB896BA38" },
+            { "Editor/GeologyStageAValidation.cs", "67DB0CD6161EE4F4" }
```

## 3. Unity 真实运行结果

| 项 | 值 |
|---|---|
| 入口 | `GeologyStageBValidation.RunBatch()` |
| 退出码 | 0 |
| 入口执行证据 | 目标类标记行 4 条 + PASS 122 条断言真实产出 |
| Checks / Failures | 122 / 0 → PASS |
| 缺失文件 / 哈希不匹配 | 0 / 0（GB7：比对 55 个；跳过 0） |
| 编译错误 / 警告 | error CS 0 / warning CS 4（2 条唯一警告×2，既有非新增） |
| 日志 | _phase2b_validation/round22_GeologyStageBValidation_rerun.log（182517 bytes） |
| 判定 | **Stage B 本轮可复现 PASS** |

## 4. Stage B 新哈希与新增级联（1 处）

`GeologyStageBValidation.cs`：`B5EE9A249FCF5AE3` → **`58DA2160E30ABA72`**

| 登记表 | 分类 | 旧值 | 新值 |
|---|---|---|---|
| `GeologyStageCValidation.AuthorizedChangedFiles` | 本轮新建失配（此前匹配） | `B5EE9A249FCF5AE3` | `58DA2160E30ABA72` |
| `GeologyRuntimeIntegrationValidation.AuthorizedChangedFiles` | 值变更仍失配（历史失配延续） | `CFA08E4EE8D49C99`（未变） | `58DA2160E30ABA72` |
| `GeologyStageDValidation.FrozenFiles` | 值变更仍失配（历史失配延续） | `CFA08E4EE8D49C99`（未变） | `58DA2160E30ABA72` |

## 5. 全量失配总数（14 条）与分布

- 算式：R21 基线 15 − 本轮修复 2 + 本轮新建 1 = 14（实测 14）；值变更类不改变计数
- 无缺失文件；未发现依赖图之外的额外变化；未修复 StageC / StageD / GRI 中任何登记

| 验证器 | 表 | 键 | 登记值 | 实际值 |
|---|---|---|---|---|
| GeologyRuntimeIntegrationValidation | AuthorizedChangedFiles | `Editor/ChunkStreamingValidation.cs` | `55AC4FFF4BFC13C8` | `BEB05E3DD16D48F3` |
| GeologyRuntimeIntegrationValidation | AuthorizedChangedFiles | `Editor/GeologyStageAValidation.cs` | `DF42F8605BA60CAF` | `67DB0CD6161EE4F4` |
| GeologyRuntimeIntegrationValidation | AuthorizedChangedFiles | `Editor/GeologyStageBValidation.cs` | `CFA08E4EE8D49C99` | `58DA2160E30ABA72` |
| GeologyRuntimeIntegrationValidation | AuthorizedChangedFiles | `Editor/GeologyStageCValidation.cs` | `4379D1DD622EB737` | `F2FED5F4A6D68117` |
| GeologyRuntimeIntegrationValidation | AuthorizedChangedFiles | `Editor/GeologyStageDValidation.cs` | `ABA08564B8C26871` | `A26D957C4B698097` |
| GeologyRuntimeIntegrationValidation | FrozenFiles | `Editor/VoxelWorldRuntimeCoreValidation.cs` | `0E0B5C160FEAB236` | `C5ABCECD2C338BED` |
| GeologyStageCValidation | AuthorizedChangedFiles | `Editor/GeologyStageAValidation.cs` | `83731D5FB896BA38` | `67DB0CD6161EE4F4` |
| GeologyStageCValidation | AuthorizedChangedFiles | `Editor/GeologyStageBValidation.cs` | `B5EE9A249FCF5AE3` | `58DA2160E30ABA72` |
| GeologyStageCValidation | FrozenFiles | `Editor/ChunkStreamingValidation.cs` | `55AC4FFF4BFC13C8` | `BEB05E3DD16D48F3` |
| GeologyStageDValidation | FrozenFiles | `Editor/ChunkStreamingValidation.cs` | `55AC4FFF4BFC13C8` | `BEB05E3DD16D48F3` |
| GeologyStageDValidation | FrozenFiles | `Editor/GeologyStageAValidation.cs` | `DF42F8605BA60CAF` | `67DB0CD6161EE4F4` |
| GeologyStageDValidation | FrozenFiles | `Editor/GeologyStageBValidation.cs` | `CFA08E4EE8D49C99` | `58DA2160E30ABA72` |
| GeologyStageDValidation | FrozenFiles | `Editor/GeologyStageCValidation.cs` | `4379D1DD622EB737` | `F2FED5F4A6D68117` |
| GeologyStageDValidation | FrozenFiles | `Editor/VoxelWorldRuntimeCoreValidation.cs` | `0E0B5C160FEAB236` | `C5ABCECD2C338BED` |