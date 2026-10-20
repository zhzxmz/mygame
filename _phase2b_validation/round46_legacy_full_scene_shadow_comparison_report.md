# R46 — Legacy 全场景（4 Chunk）影子对比（已实施并真跑）

- 项目：`D:\gamemaking\My project`（Unity **2022.3.62f1c1**）
- 分支 / HEAD：`main` / **`37246478ed354a1e9a0ce7a3a3d9457caf188062`**（未变）
- 本轮只**扩展测量覆盖范围**：未统一生成算法、未建立正式坐标契约、未接入 World V2 生产流程
- 性质：**只读**（不加载 `SampleScene`、不实例化 `DensityTerrain`、不写场景）

---

## 1. 现场保护前后对照

| 项 | 轮前 | 轮后 | 结论 |
|---|---|---|---|
| HEAD | `37246478ed354a1e9a0ce7a3a3d9457caf188062` | 同 | 未变 |
| 分支 / 上游 | `main` / `origin/main` | 同 | 未变 |
| 暂存区 | 空 | 空 | 未暂存 |
| `git status --porcelain` | 35 条（`M` 20 + `??` 15） | **37 条**（`M` 20 + `??` 17） | 新增 2 条 = 本轮 `.cs` + `.meta` |
| `git diff --stat` | `20 files changed, 390 insertions(+), 142 deletions(-)` | **同** | 20 个 tracked 文件零改动 |
| 冻结表（7 表 349 条） | 349/349/0/0 | **349/349/0/0** | 未重钉 |

受保护文件内容哈希（轮前=轮后，异常数 **0**）：`progress.md D638D271767BFFF9`、R44 报告 `2B620F729B82A243`、R45 报告 `152B953F173B7BF4`、`V2SkeletonShadowComparisonValidation.cs D12164861625E43C`、`R45LegacySceneCorrespondenceValidation.cs 396A98BF0854D730`、`DensityTerrain.cs 0EB935DBA79CBD40`、`DensityFieldGenerator.cs 702BEFA889002006`、`ChunkSpace.cs 219FCBB569A413FB`、`SampleScene.unity 8BE17EDAF421908E`。

未执行 `commit`/`push`/`reset`/`clean`/`stash`/`checkout`/`add`；未回滚/覆盖任何历史改动。

---

## 2. 四个 Chunk 的覆盖证明（真实运行断言）

**Chunk 语义（源码确认，非名称假定）**：
- `DensityTerrain.Regenerate` 以 `chunkStartCoordinate + (x,y,z)` 枚举坐标（`DensityTerrain.cs:318–336`），`isFirstChunk = (x==0&&y==0&&z==0)`；
- `TerrainChunk.ChunkOrigin = ChunkSpace.ChunkToLocalOrigin(coordinate, voxelSize, chunkVoxelSize)`（`TerrainChunk.cs:76–80`）；
- `DensityFieldGenerator.Generate(mode, resolution, origin = ChunkOrigin, size = chunkWorldSize, …)`（`DensityTerrain.cs:783–787`）⇒ 表面公式输入 = `ChunkOrigin + idx × CellSize` ⇒ **各 Chunk 噪声相位不同**。

**实测枚举（验证器用生产函数 `ChunkSpace.ChunkToLocalOrigin` 计算原点）**：

| 运行断言 | 结果 |
|---|---|
| Chunk 总数 | **4**（`chunkCount=(2,1,2)`） |
| 互不相同的坐标数 | **4** |
| 坐标 → Terrain Local 原点 | `(0,0,0)->(0,0,0)` ｜ `(0,0,1)->(0,0,50)` ｜ `(1,0,0)->(75,0,0)` ｜ `(1,0,1)->(75,0,50)` |
| 每 Chunk 采样列数 | **36**（6×6 网格索引，与 R44 相同索引） |
| 采样总数 | **144** |
| 各 Chunk 均成功生成 DensityField | **4 / 4** |
| 其余 3 个 Chunk 的同索引列地表值与 chunk(0,0,0) 不同 | **3 / 3**（⇒ 只测 1 个 Chunk 不足以代表全场景） |

**唯一位置说明（重要）**：144 个样本中有 **23 个**落在跨 Chunk 共享采样边界上 ⇒ **唯一 Terrain Local 位置 = 121**（唯一世界整数坐标亦为 121）。见 §6 与 §10。

---

## 3. 坐标映射规则及源码依据

### 3.1 Legacy 侧

| 环节 | 源码依据 | 结论 |
|---|---|---|
| 采样空间 | `DensityField.Origin` + `idx × CellSize`（`DensityField.cs:69`、`:55`） | **Terrain Local Space** |
| Chunk 原点 | `ChunkSpace.ChunkToLocalOrigin`（`ChunkSpace.cs:95–110`） | `chunkCoord × (voxelSize × chunkVoxelSize)` |
| CellSize | `Size / Resolution`（实测 X=**1.0000**、Z=**0.6667**；Y=0.4933 不参与列高） | X 与 `voxelSize` 一致、Z 不同（**各向异性**） |
| 高度公式输入 | `ComputeSurfaceHeight(local.x, local.z)`（`DensityFieldGenerator.cs:483–504`，`private static`） | 只依赖 local-x/z，**与 y、与 Chunk 坐标无关** |
| 列高取值 | 由 `DensityField` 的密度符号变化线性插值（与生产 `FindSurfaceHeight` 同语义） | 与生产公式复刻**最大偏差 0.000001**（运行断言） |

### 3.2 本轮测试映射（**不是正式契约**）

```
Legacy 采样点  = ChunkOrigin + (ix · CellSizeX, 0, iz · CellSizeZ)      // Terrain Local
worldX         = RoundToInt(TerrainLocalX)                             // 根恒等 ⇒ Local == World（R45 已确认）
worldZ         = RoundToInt(TerrainLocalZ)
worldY         = 0                                                     // Elevation 为列级量
```

- X 轴：`CellSizeX = 75/75 = 1` ⇒ 6 个 X 采样点全为整数 ⇒ **偏移 0.0000**；
- Z 轴：`CellSizeZ = 50/75 = 0.6667` ⇒ 6 个 Z 中仅 `iz∈{0,75}` 为整数 ⇒ **96/144 列存在 ≤0.3333 的偏移**；
- 未涉及负坐标、缩放、原点平移（`chunkStart=(0,0,0)`、`voxelSize=1`、根为恒等变换）。

**每个样本记录的字段（逐样本原始数据见 §4.3）**：Chunk 标识、Legacy 原始采样坐标、Legacy 实际参与高度计算的坐标（与前者相同：生产只在密度采样点求值）、`WorldCoordinate`、前后偏移量、Legacy 原始地表高度、V2 原始 Elevation、signedDiff、absDiff、是否 Chunk 边界、是否 debugCutaway 区、坐标对应分类、有效性。

---

## 4. 每组样本统计与逐样本原始数据位置

### 4.1 分组统计（真实运行输出）

| 组 | n | valid | signedMin | signedMax | meanAbs | maxAbs |
|---|---|---|---|---|---|---|
| **全部样本** | 144 | 144 | −14.4646 | −2.6127 | **8.5154** | **14.4646** |
| Chunk (0,0,0) 原点(0,0,0) | 36 | 36 | −13.3777 | −2.8957 | **8.2301** | 13.3777 |
| Chunk (0,0,1) 原点(0,0,50) | 36 | 36 | −14.4646 | −4.4050 | **10.1499** | 14.4646 |
| Chunk (1,0,0) 原点(75,0,0) | 36 | 36 | −11.6207 | −2.6127 | **6.6085** | 11.6207 |
| Chunk (1,0,1) 原点(75,0,50) | 36 | 36 | −14.3373 | −3.0651 | **9.0732** | 14.3373 |
| **X/Z 完全对应** | 48 | 48 | −14.4646 | −2.6127 | **8.9267** | 14.4646 |
| **存在坐标偏移** | 96 | 96 | −13.3285 | −2.8957 | **8.3098** | 13.3285 |
| **Chunk 边界样本** | 80 | 80 | −14.4646 | −2.6127 | **8.4593** | 14.4646 |
| **debugCutaway 区样本** | 12 | 12 | −12.3754 | −7.4646 | **9.2929** | 12.3754 |
| **非剖切区样本** | 132 | 132 | −14.4646 | −2.6127 | **8.4448** | 14.4646 |

- **各组均有有效样本，无空组**（三组划分恒等式均通过断言：`48+96=144`、`12+132=144`、`ΣChunk=144`）。
- 全部 **144/144** 样本 `signedDiff < 0`（方向一致，无正号样本）。
- **去重补充统计（报告内对原始数据的后处理，非验证器断言）**：按 121 个唯一 Terrain Local 位置去重后 `meanAbs = 8.5880`、`maxAbs = 14.4646`（vs 144 样本 8.5154；差异 −0.073 / −0.85%）。

### 4.2 逐样本原始数据位置

- 全部 144 行原始数据位于：`_phase2b_validation/round46_R46LegacyFullSceneShadowComparisonValidation_run.log`
- 行格式（可直接正则解析）：
  `[R46] {idx} | ({cx},{cy},{cz}) | {localX},{localZ} | {worldX},{worldZ} | {offX},{offZ} | {legacy} | {v2} | {signed} | {abs} | {B|-} | {CUT|-} | {EXACT|OFFSET} | {valid|INVALID}`
- 日志大小 **221281 B**，总行数 **3101**；分组统计行共 10 行（`[R46] …：n=…`）。

### 4.3 验证器断言（42 条，全部 PASS）

| 类别 | 断言要点 |
|---|---|
| 隔离性（前/后） | 未加载场景；`DensityTerrain` 实例 0→0；GameObject 数不变；场景未脏；**场景文件 SHA256 前后一致 `8BE17EDAF421908E`** |
| 参数（来自场景文件解析） | `densityMode/useNoise/resolution/height/noiseScale/noiseStrength/chunkVoxelSize/voxelSize/chunkCount/chunkStartCoordinate` 全部与 R44/R45 已核实值一致 |
| 覆盖 | Chunk 数 4、坐标互异、原点正确、每 Chunk 36 列、总 144、4/4 成功生成、3/3 相位不同 |
| Legacy 正确性 | 全部列取到高度（未取到 0 / 退化 0）；插值 vs 公式复刻偏差 ≤1e-6；CellSize 各向异性 |
| V2 正确性 | 144/144 查询成功且 `IsValid==true`；**无错列/错坐标**（`facts.Elevation == HeightFunction(worldX,worldZ)`） |
| 统计自洽 | 三组恒等式 + 由 144 条原始数据重算 == 分组统计（valid 144 / meanAbs 8.5154 / maxAbs 14.4646） |
| 稳定性 | V2 重复查询逐样本一致；最后一个 Chunk 重新生成后列高逐值一致 |
| 失败路径 | `Y=-1` / `Y=WorldHeight` / `default(settings)` 均失败且事实无效；未污染有效统计 |
| 判定语义 | **已实测差异（maxAbs=14.4646）但 failures=0** ⇒ 高度不等不作为失败条件 |
| 退出码 | 0⇒0 / 1⇒1 / 6⇒1，另有注入开关实测 exit 1 |

---

## 5. R44 的 36 列、R45 的 8 列与 R46 全场景数据的关系

**结论：R44 与 R45 的数值在 R46 中被逐值精确复现**（同一采样索引、同一纯函数、同一配置）。

| 对照 | R44/R45 记录 | R46 复算（从 144 条原始数据筛出对应子集） | 一致性 |
|---|---|---|---|
| R44 全部 36 列 | n=36，min −13.3777，max −2.8957，meanAbs **8.2301**，maxAbs **13.3777** | R46 chunk(0,0,0) 全部 36 列：**完全相同** | ✅ 逐值一致 |
| R45 A 组（剖切区） | n=12，meanAbs **9.2929**，maxAbs **12.3754** | R46 chunk0 ∩ 剖切区：n=12，9.2929 / 12.3754 | ✅ |
| R45 B 组（非剖切 + Z 整数） | n=8，meanAbs **8.6666**，maxAbs **13.3777** | R46 chunk0 ∩ 非剖切 ∩ EXACT：n=8，8.6666 / 13.3777 | ✅ |
| R45 C 组（非剖切 + Z 非整数） | n=16，meanAbs **7.2148**，maxAbs **13.3285** | R46 chunk0 ∩ 非剖切 ∩ OFFSET：n=16，7.2148 / 13.3285 | ✅ |

**关系图**：
- **R44 = R46 的 1/4**（chunk(0,0,0) 子集），数值可复现，但**只代表一个 Chunk**；
- **R45 的 A/B/C = R44 这 36 列的三分组**，R46 证明该分组口径可复现且可扩展到其余 3 个 Chunk；
- **R46 新增 108 列**（3 个 Chunk × 36）⇒ 全场景基准 `n=144 / meanAbs=8.5154 / maxAbs=14.4646`；
- R46 的各 Chunk `meanAbs` 跨度 **6.6085–10.1499（53%）** ⇒ **不能把 4 个 Chunk 混合后忽略 Chunk 差异**（本轮已按 §四-C 要求分组报告）。

---

## 6. 参数、坐标偏移、边界与剖切对统计结果的影响

| 因素 | 实测证据 | 对统计的影响 | 是否主因 |
|---|---|---|---|
| **两套高度函数的参数基准 + 算法差异** | 参数逐项断言一致；预期基准偏移 `24 − 15.7 = +8.3` ⇒ `signedDiff ≈ −8.3`；实测 144 列均值 **−8.5154**（偏差 0.22 / 2.6%）；逐列散布来自不同噪声算法 | **决定差异的量级与方向**（144/144 全负） | **是（主因）** |
| **坐标偏移（Z 舍入 ≤0.3333，96/144 列）** | 完全对应组 meanAbs **8.9267** vs 偏移组 **8.3098**（差 0.62 / 7.0%） | 影响很小，且**偏移组反而略小** ⇒ 不构成主因，也不能据此声称"完全对齐" | 否 |
| **Chunk 边界** | 边界组 n=80 meanAbs **8.4593** vs 全部 8.5154（差 0.06）；**23 个跨 Chunk 共享采样点上 Legacy 高度与 V2 Elevation 完全一致（偏差 0）** | 边界既不引入额外偏差，也**未发现跨 Chunk 不连续**（与"公式只依赖 Terrain Local 坐标"一致） | 否 |
| **debugCutaway（12/144 列）** | 剖切区 meanAbs **9.2929** vs 非剖切 8.4448（+10.0%）；该区仅存在于 chunk(0,0,0) 的 `x∈[−2,2]` | 使首个 Chunk 的统计略偏高；**该区在真实场景中被挖空，不代表可见地表** ⇒ 引用时必须剔除 | 否（但须排除） |
| **Chunk 相位（origin 不同）** | 各 Chunk meanAbs **6.6085 / 8.2301 / 9.0732 / 10.1499**；Legacy 各 Chunk 均值 15.32–15.65（跨度 0.33），V2 各 Chunk 均值 22.22–25.47（跨度 **3.25**） | **显著影响幅度统计**；波动主要来自 V2 侧抽样（振幅 6 vs Legacy 2.37） | 幅度差异主因（**非方向**） |
| 重复样本加权 | 144 样本含 23 个重复位置；去重后 meanAbs 8.5880（+0.073 / +0.85%） | 影响可忽略但已披露 | 否 |

**明确不允许的结论（本轮均未得出）**：未把高度不等判为 Bug；未把 4 Chunk 混合后忽略差异；未把非整数坐标说成完全对齐；未把剖切区样本当可见地表；未据局部统计推断整体视觉一致；未以公式复刻成功宣称已验证渲染。

---

## 7. 全部真实 Unity 运行记录（7 次）

命令模板：`"D:\gamemaking\2022.3.62f1c1\Editor\Unity.exe" -batchmode -nographics -quit -projectPath "D:\gamemaking\My project" -logFile <log> -executeMethod <M> [附加开关]`

| # | 入口 | 附加 | 退出码 | 耗时 | PASS | FAIL | error CS | warning CS | 重编译 | 崩溃/超时 | 日志 |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | `R46LegacyFullSceneShadowComparisonValidation.RunBatch` | — | **0** | 17.1 s | **42** | **0** | 0 | 4（既有无关） | **是（首次全量）** | 无 | `round46_R46LegacyFullSceneShadowComparisonValidation_run.log`（221281 B） |
| 2 | 同上 | `-r46SelfFailure` | **1** | 14.1 s | 42 | **1（仅注入项）** | 0 | 0 | 否（缓存） | 无 | `..._selffail_run.log`（215756 B） |
| 3 | `V2SkeletonShadowComparisonValidation.RunBatch` | — | **0** | 14.1 s | 34（checks=33） | 0 | 0 | 0 | 否 | 无 | `round46_V2SkeletonShadowComparisonValidation_run.log`（128809 B） |
| 4 | `R45LegacySceneCorrespondenceValidation.RunBatch` | — | **0** | 13.1 s | 36（checks=35） | 0 | 0 | 0 | 否 | 无 | `round46_R45LegacySceneCorrespondenceValidation_run.log`（99189 B） |
| 5 | `WorldSkeletonProviderValidation.RunBatch` | — | **0** | 14.1 s | 45（checks=44） | 0 | 0 | 0 | 否 | 无 | `round46_WorldSkeletonProviderValidation_run.log`（105159 B） |
| 6 | `WorldV2ParameterPipelineValidation.RunBatch` | — | **0** | 13.1 s | 46 | 0 | 0 | 0 | 否 | 无 | `round46_WorldV2ParameterPipelineValidation_run.log`（110658 B） |
| 7 | `GeologyRuntimeIntegrationValidation.RunBatch` | — | **0** | 15.1 s | 48（`Checks=47`） | 0 | 0 | 0 | 否 | 无 | `round46_GeologyRuntimeIntegrationValidation_run.log`（121964 B） |

- 汇总原文：运行 1 `[R46] Legacy 全场景（4 Chunk）影子对比 ⇒ checks=42 failures=0（全部通过；exit 0）`；运行 2 同一行 `failures=1`，唯一 FAIL 为 `[自检] 命令行开关注入的故意失败`；运行 7 `Checks = 47 / Failures = 0 / Result = PASS`（含 `GE7`/`GE9` 冻结表自断言 PASS）。
- `stdout`/`stderr` 全部 **0 B**；崩溃特征正则命中 **0**；无超时；Unity 正常启动（`licErr=0`）。
- 运行 1 的 4 条警告均为既有、与本轮无关：`VoxelCoordinateMigrationStageB3CValidation.cs(2130,50) CS0162`、`MiningSurfaceIntegrityValidation.cs(432,24) CS0414`。
- **无任何入口因签名/环境无法运行**；全部 7 次均真实启动 Unity 并产生编译/执行证据。

---

## 8. 所有新增文件

| 路径 | 行数 | 字节 | SHA16 | 备注 |
|---|---|---|---|---|
| `Assets/Editor/R46LegacyFullSceneShadowComparisonValidation.cs` | **857** | 41682 | `764B7D0126C6E2DE` | 新增只读验证器 |
| `Assets/Editor/R46LegacyFullSceneShadowComparisonValidation.cs.meta` | 11 | 243 | `B18A247C07D1CA7F` | Unity 自动生成，GUID `21a5db64cc3b5ef45983b725befd92af` |
| `_phase2b_validation/round46_legacy_full_scene_shadow_comparison_report.md` | 本报告 | — | — | — |
| `_phase2b_validation/round46_R46LegacyFullSceneShadowComparisonValidation_run.log`（+`_stdout/_stderr`） | — | — | — | 运行 1（含 144 条逐样本原始数据） |
| `_phase2b_validation/round46_R46LegacyFullSceneShadowComparisonValidation_selffail_run.log`（+`_stdout/_stderr`） | — | — | — | 运行 2（故障注入） |
| `_phase2b_validation/round46_{V2SkeletonShadowComparisonValidation,R45LegacySceneCorrespondenceValidation,WorldSkeletonProviderValidation,WorldV2ParameterPipelineValidation,GeologyRuntimeIntegrationValidation}_run.log`（+`_stdout/_stderr`） | — | — | — | 运行 3–7（回归入口） |

未修改任何既有文件（R44/R45 验证器、生产代码、场景、契约、冻结表）。

---

## 9. 冻结表、受保护文件哈希、HEAD、staged 与工作区状态

| 检查 | 结果 |
|---|---|
| 冻结表 7 表复算 | **349 / 匹配 349 / 不符 0 / 缺失 0**；本轮新文件登记数 **0** ⇒ **未重钉** |
| 运行时冻结表自断言（运行 7） | `GE7`（25 个 Critical 文件 SHA 不变）+ `GE9`（比对 40 / 授权 6 / 跳过 0 / 缺失 0）**PASS** |
| 受保护文件（9 项，含场景与 3 个 Legacy 文件、R44/R45 验证器与报告） | 哈希**异常数 0** |
| 场景文件 | `8BE17EDAF421908E`：验证器内部前后 SHA256 **一致**（运行断言） |
| HEAD / staged | 未变 / 空 |
| `git diff --stat` | `20 files changed, 390 insertions(+), 142 deletions(-)`（未变） |
| 工作区 | 仅新增本轮 2 个文件与 `_phase2b_validation/` 内日志/报告（该目录整体为 `??`） |

---

## 10. 仍无法确定的问题与后续最小建议

### 10.1 仍无法确定
| # | 问题 | 现状 |
|---|---|---|
| Q1 | **视觉一致性** | 未渲染验证；本轮的"公式级/密度场级"测量**不能**替代渲染与人工观感验收 |
| Q2 | 剖切区在真实场景中"看不到地表" | 仍为源码推断（`density = −1` ⇒ 无等值面），未做渲染验证 |
| Q3 | Chunk 间 `meanAbs` 差异（6.61–10.15）的**定量分解** | 已观测到主要由 V2 侧抽样波动驱动（V2 均值跨度 3.25 vs Legacy 0.33），但未把"抽样波动"与"算法差异"定量分离 |
| Q4 | 采样粒度 | 每 Chunk 36 列（6×6），未测更细网格；`n=144`（唯一位置 121）不足以刻画整幅地形 |
| Q5 | 跨 Chunk 边界**材料**连续性 | 本轮只验证了高度连续（共享点偏差 0），材料字段未测 |
| Q6 | 两套高度函数是否统一、Seed 语义、`SeaLevel` 语义、正式坐标契约 | 沿用 R43–R45 未决项（C1–C7），本轮**未裁决** |

### 10.2 后续最小建议（需批准后才实施）
1. **密度化采样**：把每 Chunk 的 36 列提高到更细网格（例如 12×12/Chunk），并按唯一位置去重后统计，以降低抽样波动对 per-Chunk 均值的干扰（回应 Q3/Q4）。
2. **显式剔除剖切区**：在统计口径中把 `debugCutaway` 区独立报告（本轮已分组，但建议后续默认从"可见地表基准"中排除），并请 owner 决定该调试状态是否长期保留（回应 Q2）。
3. **材料连续性**：在现有只读采样路径上追加"跨 Chunk 共享点的 `MaterialField` 是否一致"检查（回应 Q5）。
4. 仍**不**触碰：正式坐标契约、Seed 语义、高度参数载体、`SeaLevel` 裁决、生产接线、外观变更。

---

## 11. 结论

1. **四个 Chunk 是否全部覆盖**：**是**。`chunkCount=(2,1,2)` + `chunkStartCoordinate=(0,0,0)` 枚举出 `(0,0,0)/(0,0,1)/(1,0,0)/(1,0,1)` 四个坐标，原点经生产函数 `ChunkSpace.ChunkToLocalOrigin` 计算为 `(0,0,0)/(0,0,50)/(75,0,0)/(75,0,50)`；每 Chunk 36 列、共 **144** 样本（唯一位置 121）、4/4 成功生成，且**其余 3 个 Chunk 的地表与 chunk(0,0,0) 不同**（相位随 origin 变化）。

2. **全场景统计是否可靠**：**在所述口径下可靠，但有明确边界**。全场景 `n=144 / meanAbs=8.5154 / maxAbs=14.4646`，144/144 样本方向一致（全为负）、144/144 有效、统计与原始数据自洽（重算一致）。可靠性边界：仅公式/密度场级（非渲染）；每 Chunk 36 列粒度；含 23 个跨 Chunk 重复位置（去重后 8.5880）；12 列落在调试剖切区（不代表可见地表）；`meanAbs` 按 Chunk 分组跨度可达 53%（6.61–10.15），**不可只报混总值**。

3. **哪些差异已有证据解释**：
   - **主因**：两套高度函数的参数基准差（`24 − 15.7 ≈ +8.3`，实测均值 −8.5154，偏差 2.6%）+ 不同噪声算法（R44 已归因）；方向系统性（全负）。
   - **次要**：Z 舍入偏移（完全对应 8.9267 vs 偏移 8.3098，7.0%）；Chunk 边界（8.4593 vs 8.5154，0.7%；且共享点高度**完全一致**）；重复样本加权（+0.85%）。
   - **须排除**：`debugCutaway` 区 12 列（9.2929，且该区真实场景被挖空）。
   - **幅度差异**：Chunk 相位（V2 侧抽样波动主导）。

4. **哪些仍未知**：视觉一致性、剖切区可见性、per-Chunk 差异的定量分解、更细粒度与材料连续性（见 §10.1 Q1–Q6）。

5. **本轮是否改变生产逻辑或玩家可见世界**：**均未改变**。未修改任何生产代码/既有验证器/场景/`Prefab`/配置/契约；`SampleScene.unity` 哈希轮前=轮后=`8BE17EDAF421908E`（验证器内部实测）；冻结表 349/0/0 未重钉；HEAD 未变、staged 空；玩家可见世界与 R45 时完全相同。

**未开始**：正式坐标契约、Seed 语义统一、高度参数载体统一、`SeaLevel` 裁决、World V2 生产接线、任何外观变更。
