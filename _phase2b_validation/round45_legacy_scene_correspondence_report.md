# R45 — Legacy 实际场景与 R44 影子采样对应关系核验（只读）

- 项目：`D:\gamemaking\My project`（Unity **2022.3.62f1c1**）
- 分支 / HEAD：`main` / **`37246478ed354a1e9a0ce7a3a3d9457caf188062`**（未变）
- 本轮只回答一个问题：**R44 的 36 列高度差，在多大程度上对应当前实际场景的地表生成条件？**
- 性质：**只读核验**（只读解析场景文件 + 纯算术），未统一算法、未接线、未改变任何可见世界

---

## 1. 现场保护前后对照

| 项 | 轮前 | 轮后 | 结论 |
|---|---|---|---|
| HEAD | `37246478ed354a1e9a0ce7a3a3d9457caf188062` | 同 | 未变 |
| 分支 / 上游 | `main` / `origin/main` | 同 | 未变 |
| 暂存区 | 空 | 空 | 未暂存 |
| `git status --porcelain` | 33 条（`M` 20 + `??` 13） | **35 条**（`M` 20 + `??` 15） | 新增 2 条＝本轮 `.cs` + `.meta` |
| `git diff --stat` | `20 files changed, 390 insertions(+), 142 deletions(-)` | **同** | 20 个 tracked 文件零改动 |
| 冻结表（7 表 349 条） | 349 / 匹配 349 / 0 / 0 | **349 / 匹配 349 / 0 / 0** | 未重钉 |

受保护文件内容哈希（轮前 → 轮后，逐项一致，异常数 **0**）：

| 文件 | SHA16 |
|---|---|
| `_phase2b_validation/sealevel_round7_progress.md` | `D638D271767BFFF9` |
| `_phase2b_validation/round43_densityterrain_wiring_audit.md` | `E716B053A47D7D78` |
| `_phase2b_validation/round44_shadow_comparison_report.md` | `2B620F729B82A243` |
| `Assets/Editor/V2SkeletonShadowComparisonValidation.cs`（R44 验证器） | `D12164861625E43C` |
| `Assets/Scripts/Terrain/DensityTerrain.cs` | `0EB935DBA79CBD40` |
| `Assets/Scripts/Terrain/DensityFieldGenerator.cs` | `702BEFA889002006` |
| `Assets/Scripts/Terrain/ChunkSpace.cs` | `219FCBB569A413FB` |
| `Assets/Scenes/SampleScene.unity` | `8BE17EDAF421908E` |

本轮**未执行** `commit`/`push`/`reset`/`clean`/`stash`/`checkout`/`add`；未回滚、未覆盖、未清理任何历史改动。

---

## 2. 实际场景参数 vs R44 参数逐项对照

### 2.1 场景事实（`Assets/Scenes/SampleScene.unity`，脚本 guid `3af3bcfc875f4cbfae75f1f95dee772a`）

| 项 | 值 |
|---|---|
| 所属对象 | GameObject `Terraria`（`m_GameObject: {fileID: 482992366}`） |
| 组件启用 | **`m_Enabled: 1`** |
| 根 Transform | 位置 `(0,0,0)`、旋转 identity `(0,0,0,1)`、缩放 `(1,1,1)`、**无父节点** ⇒ 恒等变换 |
| `densityMode` | `0` = `HeightField` |
| `chunkCount` | `(2,1,2)` ⇒ **4 个 Chunk** |
| `chunkStartCoordinate` | `(0,0,0)` |
| `chunkVoxelSize` | `(75,37,50)` |
| `voxelSize` | `1` |
| `resolution` | `75` |
| `useNoise` / `height` / `noiseScale` / `noiseStrength` | `1` / `15.7` / `0.15` / `2.37` |
| `cavityCenterOffset` / `cavityRadius` | `(15.5,5.23,19.1)` / `6.13` |
| `useLegacyMaterialRules` | `0`（⇒ 材料 V2 流水线） |
| `grassDepth` / `dirtDepth` | `1.9` / `0.65` |
| 材料 7 参数（weathering/slope/gravity/soilIterations/grassStrength） | `0.8 / 8 / 15 / 45 / 1 / 4 / 1` |
| `debugCutaway` / `debugCutX` / `debugCutWidth` | **`1`** / `0` / `4` ⇒ 首 Chunk 剖切 `x∈[−2,2]` |
| 三材质槽 | 全 `{fileID: 0}`（运行时创建） |
| `regenerateInEditor` | `1` |
| `useVoxelAwareMeshPrecut` | **`1`** |
| `chunkSize` / `terrainSize` | `(0,0,0)` / `(83,20.3,55.2)` ⇒ 因 `chunkVoxelSize` 显式非零，尺寸推导分支**不触发** |

### 2.2 逐项对照（**相同 / 不同 / 不适用**，无"未知"项）

| # | 字段 | 场景值 | R44 值 | 判定 | 是否影响高度 |
|---|---|---|---|---|---|
| A1 | `densityMode` | `HeightField` | `DensityGenerationMode.HeightField` | **相同** | 是 |
| A2 | `useNoise` | `true` | `true` | **相同** | 是 |
| A3 | `resolution` | 75 | 75 | **相同** | 是（采样密度） |
| A4 | `chunkVoxelSize × voxelSize` | `(75,37,50)` | `size=(75,37,50)` | **相同** | 是（CellSize 分母同为 75） |
| A5 | `height` | 15.7 | 15.7 | **相同** | 是 |
| A6 | `noiseScale` | 0.15 | 0.15 | **相同** | 是 |
| A7 | `noiseStrength` | 2.37 | 2.37 | **相同** | 是 |
| A8 | `cavityCenterOffset` / `cavityRadius` | `(15.5,5.23,19.1)` / 6.13 | 同 | **相同** | 否（`HeightField` 不参与） |
| A9 | `useLegacyMaterialRules` | `false` | `false` | **相同** | 否（仅材料） |
| A10 | `grassDepth` / `dirtDepth` | 1.9 / 0.65 | 同 | **相同** | 否（仅材料） |
| A11 | 材料流水线 7 参数 | 见 2.1 | 同 | **相同** | 否（仅材料） |
| A12 | `chunkStartCoordinate` | `(0,0,0)` | `origin=(0,0,0)` | **相同** | 是（首个 Chunk 原点） |
| **A13** | `chunkCount` | `(2,1,2)` = **4 个 Chunk** | **只生成 1 个** | **不同（覆盖率 25%）** | 是（覆盖范围） |
| **A14** | `debugCutaway` + `debugCutX/Width` | **`1`**，`x∈[−2,2]` | **未施加** | **不同（有意排除）** | 是（该区地表被剖掉） |
| A15 | `useVoxelAwareMeshPrecut` | `1` | 未使用 | **不同（不影响高度）** | 否（只影响网格几何；密度场在此之前已生成） |
| A16 | `chunkSize` / `terrainSize` | `(0,0,0)` / `(83,20.3,55.2)` | 未使用 | **不适用** | 否（推导分支不触发） |
| A17 | `voxelSize` | 1 | 未直接使用 | **不适用/间接一致** | 否（高度只依赖 size/resolution） |
| A18 | 三材质槽 / `regenerateInEditor` | null×3 / `1` | 未使用 | **不适用** | 否（渲染与编辑器行为） |

**小结**：**所有影响高度计算的参数逐项相同**（A1–A7、A12，且 A4 保证 CellSize 一致）；3 项已声明偏差为 **Chunk 覆盖（A13）**、**debugCutaway（A14）**、**precut（A15，与高度无关）**；`chunkSize`/`terrainSize`/`voxelSize`/材质槽为**不适用**。无"未知"项。

---

## 3. 坐标映射流程及源码依据

### 3.1 Legacy 侧坐标空间（源码）

| 环节 | 依据 | 结论 |
|---|---|---|
| 生成输入 | `DensityTerrain.BuildChunk`：`Vector3 terrainLocalOrigin = chunk.ChunkOrigin;`（`DensityTerrain.cs:780`）→ 传给 `DensityFieldGenerator.Generate(..., origin: terrainLocalOrigin, ...)`（`:783`） | Legacy 采样输入处于 **Terrain Local Space** |
| 采样点位置 | `DensityField.GetLocalPosition(x,y,z) = Origin + (x·CellSizeX, y·CellSizeY, z·CellSizeZ)`（`DensityField.cs:69`）；`CellSize = Size / Resolution`（`:55`） | 采样位置 **由 CellSize 决定**，不是由 `voxelSize` 决定 |
| Chunk 局部 → 采样坐标 | 每个 Chunk 单独调用一次 `Generate`，`origin = chunkCoordinate × (voxelSize × chunkVoxelSize)`（`ChunkSpace.ChunkToLocalOrigin`，`ChunkSpace.cs:95–110`） | 只有 `origin=(0,0,0)` 的 Chunk，其 Chunk 局部坐标 == Terrain Local 坐标 |
| 高度公式 | `ComputeSurfaceHeight = height + (PerlinNoise(x·noiseScale, z·noiseScale)·2−1)·noiseStrength`（`DensityFieldGenerator.cs:483–504`），入参为 **Terrain Local 浮点 x/z** | 高度是 local-x/z 的纯函数，**与 y 无关** |
| 体素/Chunk 坐标 | `ChunkSpace.LocalToVoxel = floor(local / voxelSize)`；`VoxelToChunk = floorDiv(voxel, chunkVoxelSize)` | `voxelSize=1` ⇒ 体素坐标 == 取整后的 local 坐标 |

### 3.2 R44 的临时映射（**仅测试用，不是正式契约**）

```
Legacy 采样点：localX = ix · CellSizeX = ix · 1.0     （ix ∈ {0,1,19,38,57,75} ⇒ 全部为整数）
              localZ = iz · CellSizeZ = iz · 0.6667   （iz ∈ {0,1,13,26,38,75}）
V2 查询坐标：  worldX = RoundToInt(localX)
              worldZ = RoundToInt(localZ)
              worldY = 0（仅需落在 [0,WorldHeight−1]；Elevation 为列级量，与 Y 无关）
```

| 差异类别 | 是否涉及 | 量化（本轮实测/推算） |
|---|---|---|
| **X 轴取整** | 涉及，但**误差为 0** | `CellSizeX = 75/75 = 1` ⇒ 6 个 X 采样点全为整数 ⇒ 舍入误差 **0.0000** |
| **Z 轴取整** | **涉及，产生真实位置偏差** | `CellSizeZ = 50/75 = 0.6667` ⇒ 6 个 Z 中仅 `iz∈{0,75}` 为整数 ⇒ **24/36 列**存在 ≤ **0.3333** 的采样位置偏移 |
| **偏移/原点** | 不涉及 | `chunkStartCoordinate=(0,0,0)` ⇒ 首 Chunk `origin=(0,0,0)`，与 R44 的 `Vector3.zero` 一致 |
| **缩放** | 不涉及 | `voxelSize=1`、根缩放 1 ⇒ 无缩放 |
| **Y 轴语义** | 不涉及数值换算 | Legacy 高度是 **Terrain Local Y（float）**；V2 `Elevation` 是 **世界整数 Y**。根为恒等变换 ⇒ 两者 Y=0 平面重合，单位均为 1 m 体素 ⇒ 可直接比较数值 |
| **Local == World 前提** | 本轮**首次在进程内确认** | R45 验证器从场景文件解析根 Transform：位置 `(0,0,0)`、旋转 identity、缩放 `(1,1,1)`、无父节点 ⇒ 断言 PASS（修正了 R44「未在进程内复验」的缺口） |

### 3.3 源码直接证明 vs 仍属推断

| 结论 | 等级 |
|---|---|
| Legacy 采样输入是 Terrain Local Space；CellSize = Size/Resolution | **源码直接证明** |
| 高度公式只依赖 local-x/z，与 y 及 Chunk 坐标无关 | **源码直接证明** |
| `chunkSize`/`terrainSize` 在当前配置下不参与（`GetEffectiveChunkVoxelSize` 在 `chunkVoxelSize>0` 时直接返回） | **源码直接证明**（`DensityTerrain.cs:1001–1008`） |
| `useVoxelAwareMeshPrecut` 不影响 DensityField/高度 | **源码直接证明**（precut 在 `chunk.Build` 之后作用于 Mesh） |
| `debugCutaway` 把首 Chunk `x∈[−2,2]` 的密度置 `−1` | **源码直接证明**（`DensityTerrain.cs:1343–1376`） |
| 该剖切区在真实场景中"看不到地表"（整段被挖空） | **推断**（density 恒负 ⇒ 无等值面 ⇒ 无三角形；未做渲染验证） |
| 其余 3 个 Chunk 的地表形态与 chunk(0,0,0) 相似但相位不同 | **推断**（同一函数、不同 origin；未采样） |

---

## 4. 动态验证：已执行（但**不加载场景**）

### 4.1 为什么不做"加载场景"式验证

`DensityTerrain.OnEnable` → `RequestRegenerate()` →（编辑器非播放态）`EditorApplication.delayCall` → `DelayedRegenerate()` → `Regenerate()`，会**创建 4 个 Chunk GameObject + Mesh + MeshCollider** 并把当前场景标记为脏（`DensityTerrain.cs:213–219 / 250–264 / 301–356`）。即使在 batchmode 下不会自动保存，**也无法证明"绝不会写入场景"**（任何隐式 `SaveScene`/`AssetDatabase` 路径都可能落盘）。按 R45 §三「安全性无法确认则停止动态验证」的要求，**放弃加载场景**。

### 4.2 实际采用的动态验证（零场景风险）

新增 `R45LegacySceneCorrespondenceValidation`，在 Unity Batchmode 内**以文本方式只读解析** `SampleScene.unity`（`File.ReadAllText` + 正则），并做纯算术与断言：
- 不加载场景、不实例化任何对象；
- 解析 `DensityTerrain` 序列化块（按脚本 guid 定位，到下一 `--- !u!` 为止）；
- 沿 `m_GameObject` → GameObject → `m_Component` → `--- !u!4` 解析根 Transform；
- 前后对场景文件取 `SHA256`，**实测前后一致 `8BE17EDAF421908E`** ⇒ 证明未写入。

---

## 5. R44 的 36 列基准适用范围与证据等级（核心答案）

### 5.1 三组划分（依据本轮实测的坐标对应性，对 R44 已公布的 36 列数据分组统计）

| 组 | 定义 | 列数 | 列索引 | meanAbs | maxAbs |
|---|---|---|---|---|---|
| 全部 | R44 全部采样列 | 36 | 0–35 | **8.2301** | **13.3777** |
| **A** | 落在 `debugCutaway` 剖切区（真实场景中该处地表被挖空） | **12** | 0–11 | 9.2929 | 12.3754 |
| **B** | 非剖切 **且** Z 为整数（采样位置与世界坐标**完全对应**） | **8** | 12,17,18,23,24,29,30,35 | **8.6666** | **13.3777** |
| **C** | 非剖切 **但** Z 非整数（采样位置偏移 ≤0.3333） | **16** | 13–16,19–22,25–28,31–34 | 7.2148 | 13.3285 |

（A+B+C = 36 ✓；统计口径与 R44 一致：`signedDiff = LegacySurface − V2Elevation`，取绝对值。）

### 5.2 对 R45 §四-D 三个选项的判定

| 选项 | 判定 | 依据 |
|---|---|---|
| ① R44 结果仅代表隔离采样路径 | **不成立（过弱）** | 参数与实际场景**逐项相同**（A1–A12 运行期断言 PASS），且根变换前提已在进程内确认 |
| ② 参数与实际场景相同，但坐标对应尚未完全确认 | **✅ 成立（有证据支持）** | Chunk 覆盖 1/4（A13/B6）；**24/36 列 Z 采样位置偏移 ≤0.3333**（B3）；**12/36 列落在真实场景被剖切区**（B4）；12 列位于 Chunk 采样边界（B5） |
| ③ 参数与坐标均与实际场景对应，可作为当前场景高度差基准 | **不成立（未达严格标准）** | 存在上述 3 类已量化坐标缺口；仅 **B 组 8 列**满足"参数 + 坐标严格对应"，其 `meanAbs=8.6666 / maxAbs=13.3777` 与全 36 列的 `8.2301 / 13.3777` 相差 +5.3%（均值），**方向与量级一致但样本不同** |

### 5.3 偏差来源与影响（不归类为生产 Bug）

| 偏差 | 量级 | 对基准的影响 | 是否解释 8.23 的主因 |
|---|---|---|---|
| Z 采样位置偏移（24/36 列，≤0.3333） | 高度影响 ≤0.17（R44 已用相邻列梯度估算） | 使 C 组与 B 组样本不同源 | **否**（仅占 2.1%） |
| Chunk 覆盖仅 1/4（A13） | 影响"世界范围"代表性 | 基准只代表 chunk(0,0,0) 区域 | 否（不代表世界全部） |
| debugCutaway 12 列（A14） | 该区真实场景无地表 | 这 12 列的 Legacy 值**不代表可见地表** | 否（但必须在引用时剔除） |
| 两套高度函数本身不同（R44 已归因） | meanAbs 8.23 主体 | — | **是**（参数基准差 ≈ −8.3 + 噪声算法差异） |

---

## 6. 所有新增文件

| 路径 | 行数 | 字节 | SHA16 | 备注 |
|---|---|---|---|---|
| `Assets/Editor/R45LegacySceneCorrespondenceValidation.cs` | **611** | 30836 | `396A98BF0854D730` | 新增只读验证器 |
| `Assets/Editor/R45LegacySceneCorrespondenceValidation.cs.meta` | 11 | 243 | `261A2786B197391B` | Unity 自动生成，GUID `fb1e4495c323c6b4fa8e6f7341ecb04b` |
| `_phase2b_validation/round45_legacy_scene_correspondence_report.md` | 本报告 | — | — | — |
| `_phase2b_validation/round45_R45LegacySceneCorrespondenceValidation_run.log`（+`_stdout/_stderr`） | — | — | — | 运行 1 证据 |
| `_phase2b_validation/round45_R45LegacySceneCorrespondenceValidation_selffail_run.log`（+`_stdout/_stderr`） | — | — | — | 运行 2（故障注入）证据 |
| `_phase2b_validation/round45_GeologyRuntimeIntegrationValidation_run.log`（+`_stdout/_stderr`） | — | — | — | 回归 + 冻结表运行时自断言 |

未修改任何既有文件（见 §8）。

---

## 7. Unity 真实运行记录

命令模板：
`"D:\gamemaking\2022.3.62f1c1\Editor\Unity.exe" -batchmode -nographics -quit -projectPath "D:\gamemaking\My project" -logFile <log> -executeMethod <M> [附加开关]`

| # | 入口 | 附加 | 退出码 | 耗时 | PASS | FAIL | error CS | warning CS | 是否重编译 | 崩溃/超时 | 日志 |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | `R45LegacySceneCorrespondenceValidation.RunBatch` | — | **0** | 19.2 s | **35** | **0** | 0 | 4（既有无关） | **是（首次全量）** | 无 | `round45_R45LegacySceneCorrespondenceValidation_run.log`（105757 B） |
| 2 | 同上 | `-r45SelfFailure` | **1** | 14.1 s | 35 | **1（仅注入项）** | 0 | 0 | 否（缓存） | 无 | `..._selffail_run.log`（106351 B） |
| 3 | `GeologyRuntimeIntegrationValidation.RunBatch` | — | **0** | 14.1 s | 48 | **0** | 0 | 0 | 否（缓存） | 无 | `round45_GeologyRuntimeIntegrationValidation_run.log`（121950 B） |

- 汇总原文：运行 1 `[R45] Legacy 场景 × R44 采样对应核验 ⇒ checks=35 failures=0（全部通过；exit 0）`；运行 2 `⇒ checks=36 failures=1（存在失败项；exit 1）`（唯一 FAIL 为注入项）；运行 3 `Checks = 47 / Failures = 0 / Result = PASS`。
- `stdout` / `stderr`：全部 **0 B**。
- 运行 1 的 4 条警告均为**既有、与本次无关**：`VoxelCoordinateMigrationStageB3CValidation.cs(2130,50) CS0162`、`MiningSurfaceIntegrityValidation.cs(432,24) CS0414`。
- 崩溃特征正则命中 **0**；无超时；Unity 正常启动（`licErr=0`）。

### 7.1 R45 验证器 35 条断言清单（全部 PASS）

| 断言组 | 关键结论 |
|---|---|
| 隔离性（前置） | 未加载任何场景资源；场景中 `DensityTerrain` 实例 = 0 |
| 前置 | 场景文件非空（276169 字符）；定位到组件块；`m_Enabled=1` |
| A1–A12 | **全部"相同"**：densityMode / useNoise / resolution / chunkVoxelSize×voxelSize / height / noiseScale / noiseStrength / cavity / useLegacyMaterialRules / grass·dirtDepth / 材料 7 参数 / chunkStartCoordinate |
| A13–A15 | **显式记录差异**：覆盖 25.0% / debugCutaway 剖切 `x∈[−2,2]` 未施加 / precut 不影响高度 |
| A16 | `chunkSize`/`terrainSize` 不适用（推导分支不触发） |
| **B1** | 根 Transform 恒等 ⇒ **Local == World 成立（进程内确认）** |
| **B2** | CellSize = (1, 0.4933, 0.6667)；X == voxelSize、Z ≠ X（各向异性） |
| **B3** | **Z 舍入错位 24/36**，最大误差 0.3333 |
| **B4** | **debugCutaway 影响 12/36**（`ix∈{0,1}`） |
| **B5** | 落在 Chunk 采样边界的列 = 12（`x=75`、`z=50` 共享边） |
| **B6** | Chunk 原点 4 个：`(0,0,0) / (0,0,50) / (75,0,0) / (75,0,50)`；R44 全部落在第 1 个 |
| 稳定性 | 两次独立解析得到完全相同的组件块 |
| 隔离性（后置） | 场景文件 SHA256 前后一致 `8BE17EDAF421908E`；GameObject 数未变；场景未脏 |
| 退出码 | 0⇒0 / 1⇒1 / 5⇒1（+ 运行 2 实测 exit 1） |

---

## 8. 冻结表、受保护文件、HEAD 与 staged

| 检查 | 结果 |
|---|---|
| 冻结表 7 表复算 | **349 / 匹配 349 / 不符 0 / 缺失 0**；本轮新增文件在 7 表中登记数 = **0** ⇒ **未重钉** |
| 运行时冻结表自断言（运行 3） | `GE7`（25 个 Critical 文件 SHA 不变）+ `GE9`（比对 40 / 授权 6 / 跳过 0 / 缺失 0）**均 PASS** |
| 受保护文件（8 项，含场景与 Legacy 三个文件） | 哈希**异常数 0** |
| 场景文件 | `8BE17EDAF421908E`：轮前 = 轮后 = **验证器内部实测**（未写入） |
| HEAD | `37246478ed354a1e9a0ce7a3a3d9457caf188062`（未变） |
| staged | 空 |
| `git diff --stat` | `20 files changed, 390 insertions(+), 142 deletions(-)`（未变） |
| 生产代码 | `DensityTerrain.cs` / `DensityFieldGenerator.cs` / `ChunkSpace.cs` / `TerrainChunk.cs` / `V2SkeletonShadowComparisonValidation.cs` **全部未修改** |

---

## 9. 尚未解决的问题与下一步建议

### 9.1 未解决（本轮不裁决）
| # | 问题 | 影响 |
|---|---|---|
| Q1 | Z 采样位置与世界整数坐标的对齐（`CellSizeZ=0.6667` 与 1 m 体素格不同步） | 若要"逐列严格对应"，需在**整数世界坐标**处采样（用公式在任意 x/z 求值），而不是在采样网格索引处 |
| Q2 | `debugCutaway=1` 是否为有意保留的**调试状态**（当前场景确实带 4 m 剖切） | 决定基准是否应把该区列为"非代表性样本"或建议后续关闭（需 owner 决定） |
| Q3 | 其余 3 个 Chunk 的地表未采样 | 基准目前只代表 chunk(0,0,0)（25% 面积） |
| Q4 | 剖切区"无地表"仍是源码推断 | 未渲染验证 |
| Q5 | 两套高度函数是否统一、Seed 语义、`SeaLevel` 语义 | 沿用 R43/R44 未决项（C1–C7） |

### 9.2 下一步建议（最小、可回退，**需批准后才实施**）
1. **把 R44 采样网格改为整数世界坐标**（x/z 取整数，直接在公式处求值），从而消除 B3 的 24/36 偏移；同时把结果按"是否落在 debugCutaway 区"分组输出（沿用本轮 A/B/C 分组口径）。
2. **扩展到 4 个 Chunk**（用各 Chunk 的 `origin` 偏移调用同一纯函数），把覆盖率从 25% 提到 100%，并按 Chunk 分组统计。
3. 上述两步完成后，R44 基准可升级为"参数 + 坐标均对应"，即达成本轮未通过的选项③标准。
4. 仍**不**触碰：Seed 设计、`SeaLevel` 裁决、高度函数统一、生产接线、场景修改。

---

## 10. 结论（严格三态区分）

### 10.1 已由源码或真实运行证据**确认**的事实
1. 场景中 `DensityTerrain` 组件 `m_Enabled=1`，根 Transform 为恒等变换（位置 0 / identity / 缩放 1 / 无父节点）。
2. R44 使用的**全部影响高度的参数**（`densityMode`、`useNoise`、`resolution`、`chunkVoxelSize×voxelSize`、`height`、`noiseScale`、`noiseStrength`、`chunkStartCoordinate`）与实际场景**逐项相同**。
3. 已声明的 3 项偏差：**Chunk 覆盖 25%（1/4）**、**`debugCutaway=1` 未被施加（影响 12/36 列）**、**`useVoxelAwareMeshPrecut=1` 未使用（不影响高度）**。
4. 坐标缺口已量化：**Z 舍入错位 24/36（≤0.3333）**、**落在剖切区 12/36**、**边界列 12**。
5. Legacy 采样空间为 Terrain Local；`CellSize = Size/Resolution = (1, 0.4933, 0.6667)`；高度只依赖 local-x/z。

### 10.2 仍**未证实**的假设
- 剖切区"真实场景中看不到地表"（源码推断，未渲染验证）；
- 其余 3 个 Chunk 的地表形态与 chunk(0,0,0) 的关系（未采样）；
- 玩家实际观感与该剖切的关系（未人工验收）。

### 10.3 R44 基准能否代表当前实际场景
**部分代表（对应 R45 §四-D 的选项②）**：
- 在**参数**层面：完全代表（逐项相同）；
- 在**坐标**层面：X 轴完全对应（误差 0），Z 轴 24/36 列存在 ≤0.3333 偏移，覆盖率 25%，12/36 列落在调试剖切区；
- 因此 **36 列的全量统计不宜直接作为"当前场景地表高度差基准"**；若必须引用，应使用 **B 组 8 列（`meanAbs=8.6666 / maxAbs=13.3777`）** 并注明其代表性局限。

### 10.4 本轮是否改变生产逻辑 / 场景 / 玩家可见世界
**均未改变**：未修改任何生产代码、未修改 `SampleScene.unity`（哈希轮前=轮后=`8BE17EDAF421908E`）、未修改 Prefab/Asset/材质/生成参数/既有验证器/契约；冻结表 349/0/0 未重钉；HEAD 未变、staged 空；玩家可见世界与 R44 时完全相同。

**未开始**：参数载体统一、正式坐标契约、Seed 设计、`SeaLevel` 裁决、World V2 生产接线、任何外观变更。