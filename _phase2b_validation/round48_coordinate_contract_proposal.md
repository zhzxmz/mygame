# R48 — Legacy / World V2 正式坐标契约候选方案审查（纯文档，不实施）

- 项目：`D:\gamemaking\My project`（Unity **2022.3.62f1c1**）
- 分支 / HEAD：`main` / **`37246478ed354a1e9a0ce7a3a3d9457caf188062`**（未变）
- 本轮性质：**只提交候选方案与审查**。未新增生产代码、未新增验证器、未修改任何既有文件、未运行 Unity、未实现任何方案、未接入生产流程。
- 结论摘要（详见 §11）：推荐 **以「统一世界坐标规范（C）」为契约、以「独立坐标适配层（A）」为唯一实现落点、把「各自为政（B）」登记为过渡现状**；实施它**不会**改变当前可见世界（除非后续单独授权修改 Legacy 采样或高度算法）。

---

## 1. 现场保护前后对照

| 项 | 轮前 | 轮后 | 结论 |
|---|---|---|---|
| HEAD | `37246478ed354a1e9a0ce7a3a3d9457caf188062` | 同 | 未变 |
| staged | 空 | 空 | 未暂存 |
| `git status --porcelain` | 39 条（`M` 20 + `??` 19） | **39 条（不变）** | 本报告位于**已整体未跟踪**的 `_phase2b_validation/` 目录内 ⇒ 条目数不变（该目录本身已计为 1 条 `??`） |
| `git diff --stat` | `20 files changed, 390 insertions(+), 142 deletions(-)` | **同** | 20 个 tracked 文件零改动 |
| 冻结表（7 表 349 条） | 349 / 匹配 349 / 0 / 0 | **同** | 未重钉 |

R44–R47 交付与关键源码哈希（轮前=轮后，异常数 0）：`progress.md D638D271767BFFF9`、R44 报告 `2B620F729B82A243`、R45 报告 `152B953F173B7BF4`、R46 报告 `38E52CAECF4593A3`、R47 报告 `80450D03047988B8`、`R47ShadowSamplingStabilityValidation.cs C4DF16824B9026C1`、`ChunkSpace.cs 219FCBB569A413FB`、`DensityTerrain.cs 0EB935DBA79CBD40`、`SampleScene.unity 8BE17EDAF421908E`。

未执行 `commit`/`push`/`reset`/`clean`/`stash`/`checkout`/`add`；未修改 R44–R47 报告与原始数据。

---

## 2. 已确认的现状（本轮重新核对源码行号，未依赖历史报告）

### 2.1 坐标实现现状（源码直接确认）

| 规则 | 实现位置 | 语义 |
|---|---|---|
| Terrain Local → 体素 | `ChunkSpace.cs:13–22` `LocalToVoxel(local, voxelSize)` | `Mathf.FloorToInt(local / voxelSize)`（**floor**） |
| World → Terrain Local | `ChunkSpace.cs:25–35` + `DensityTerrain.cs:1071–1074` | `transform.InverseTransformPoint(world)`（**依赖 Transform**） |
| 体素 → Chunk | `ChunkSpace.cs:38–51` | `FloorDivide(voxel, chunkVoxelSize)`（`ChunkSpace.cs:142–153`，**向 −∞**） |
| 体素 → Chunk 内局部 | `ChunkSpace.cs:54–67` | `PositiveMod(voxel, chunkVoxelSize)`（`ChunkSpace.cs:155–159`，**非负模数**） |
| Chunk → 局部原点 | `ChunkSpace.cs:95–110` | `chunkCoord × (voxelSize × chunkVoxelSize)` |
| 体素 → 局部最小角 | `ChunkSpace.cs:131–140` | `voxel × voxelSize` |
| Chunk 原点来源 | `DensityTerrain.cs:780` + `TerrainChunk.cs:76–80` | `terrainLocalOrigin = chunk.ChunkOrigin = ChunkSpace.ChunkToLocalOrigin(...)` |
| 体素身份（VoxelField） | `VoxelField.cs:130–141`（注释 L130：`coordinate = floor(position / VoxelSize)`） | 同为 **floor** |
| 固定体素尺寸 | `DensityTerrain.cs:124` `VoxelFieldVoxelSize = 1f`；`:172–175` `EffectiveVoxelSize = max(0.0001, voxelSize)`（场景 `voxelSize = 1`） | 1.0 |
| 等值面 | `DensityTerrain.cs:117` `IsoLevel = 0f` | 密度 > 0 为实体 |
| **负坐标规则状态** | `ChunkSpace.cs:7–8` 原注：「负坐标的精确规则在 Chunk Specification V2 中仍未最终确定；当前实现使用 floor / 非负模数作为临时语义」 | **实现已定、语义未冻结** |

### 2.2 高度算法的输入空间（关键，且两套不同）

| 侧 | 输入空间 | 依据 |
|---|---|---|
| Legacy | **Terrain Local 浮点**（`local.x`, `local.z`；与 Y 无关） | `DensityFieldGenerator.cs:483–504`（`ComputeSurfaceHeight`），密度 `field = surface − local.y`（`:129`）；采样点由 `DensityField.GetLocalPosition = Origin + idx×CellSize`（`DensityField.cs:69–76`）给出，`SampleCount = Resolution+1`、`CellSize = Size/Resolution`（`:44–57`） |
| V2 | **整数世界坐标**（`worldX, worldZ`）+ `Seed` + `TerrainFrequency` | `HeightFunction.cs:27–48`，`:35–36` 使用 `settings.Seed` / `settings.TerrainFrequency`；`WorldSkeletonFacts.cs:107` 传入 `position.X / position.Z` |

### 2.3 V2 侧的坐标类型与查询门槛（源码确认）

- `WorldCoordinate` 是 `readonly struct`（`WorldCoordinate.cs:27–38`），**明确声明「不提供任何坐标换算」**（`:15`）⇒ V2 侧**故意不含** Chunk/Local 语义。
- 查询入口 `WorldSkeletonFacts.TryEvaluate(position, settings, out facts)`（`WorldSkeletonFacts.cs:94`）；唯一失败条件 `position.Y < 0 || position.Y >= settings.WorldHeight`（`:100`）；**不钳制、不修正**。
- 参数范围（`WorldGenerationSettings.cs:111–153`）：`WorldHeight ≥ 1`（`:115`）、`TerrainHeight ∈ [0, WorldHeight−1]`（`:121`）、`SeaLevel ∈ [0, WorldHeight]`（`:134`）、`TerrainFrequency > 0`、`TerrainAmplitude ≥ 0`。

### 2.4 当前场景的坐标事实（R44–R47 已确认，仍有效）

- 根 Transform 恒等（位置 0 / 旋转 identity / 缩放 1 / 无父节点）⇒ **本场景内 Terrain Local 数值 == Unity World 数值**（R45 从场景文件断言）。
- `voxelSize = 1`、`chunkVoxelSize = (75,37,50)`、`chunkCount = (2,1,2)`、`chunkStartCoordinate = (0,0,0)`、`resolution = 75` ⇒ `CellSize = (1.0, 0.4933, 0.6667)`（**各向异性**）。
- Chunk 原点（生产函数计算）：`(0,0,0) / (0,0,50) / (75,0,0) / (75,0,50)`。
- Legacy 垂直范围 `0 ≤ y < 37`；V2 默认 `WorldHeight = 64` ⇒ **两套垂直范围不一致**（见 §9 待裁决项）。

### 2.5 R47 证据边界（本轮使用规则）

- **采用**：三级嵌套采样（A 6×6 = 144、B 12×12 = 576、C 18×18 = 1296）；C 级作为"更高精度参考"，**不是数学极限**。
- **保留**：R46 的 6×6 原始基准（`round46_*_run.log`）不得覆盖。
- **不得再用**：R45/R46 中「坐标偏移导致约 7.42% 差异」——R47 已显示该差值在 B 级降至 0.15%、C 级 3.21%，属**采样波动**，已**降级**。
- **不得推出**：23 个重复点上 V2 查询一致 ⇒ Legacy 网格/材料连续（R47 §六明令）。
- **不得混同**：参数基准差（C 级 `meanAbs=8.3372` vs 预测 8.3，差 0.45%）已强支持为主导项；但**噪声算法/振幅/相位**的贡献仍**未定量分离**。

---

## 3. 三种方案的比较

### 方案 A — 独立坐标适配层（纯函数，单一职责）

| 维度 | 评估 |
|---|---|
| 优点 | 单一实现点；可单测、可穷举边界；正/负/非整数/边界规则集中；**两套系统都不必依赖对方类型**（符合 `LS65`/`RAB-L2`）；不接线即零影响，易回滚 |
| 缺点 | 若语义与 Legacy `ChunkSpace` 不一致，就会变成**第三套**坐标实现；必须先声明"唯一先例" |
| 对当前 Legacy 地形影响 | **不接线 = 0**；若替换 `ChunkSpace` 内部则影响采样与挖掘定位（高风险） |
| 对 World V2 影响 | **0**（V2 只接收整数，`WorldCoordinate.cs:15` 明确无换算职责） |
| 需改现有生产文件 | 新增文件即可；接线才需改 Legacy |
| 改变地表/Chunk 边界/挖掘/可见效果 | 不接线时**否**；接线后**是**（改变体素归属 ⇒ 挖掘与网格可能变） |
| 负坐标/非整数/边界/重复查询 | 正是该层要**定义并冻结**的四类规则 |
| 后续接 Seed/参数载体/其他系统 | **最容易**（统一入口） |
| 验证与回滚 | 新验证器 + "不接线即回滚"；删除新增文件即可完全撤销 |

### 方案 B — Legacy / V2 各用自身坐标，仅在边界显式转换（= 现状模式）

| 维度 | 评估 |
|---|---|
| 优点 | 零风险、零生产改动；不引入新实现（Legacy 继续用 `ChunkSpace`，V2 继续用整数） |
| 缺点 | **转换逻辑分散在各调用点**，极易漂移 —— R44–R47 已经实证：各验证器各自写 `RoundToInt`，与 Legacy 自身的 `floor` 规则**冲突**（§5 量化：C 级 **126/324 = 38.9%** 样本指向不同世界列）；无单一权威 |
| 对当前 Legacy 地形影响 | 0 |
| 对 World V2 影响 | 0 |
| 需改现有生产文件 | 否 |
| 改变可见效果 | 否 |
| 负坐标/边界 | **未被任何契约定义**（`ChunkSpace.cs:7–8` 自注未定稿）⇒ 每个调用点各自解释 |
| 后续接 Seed/参数 | 难（无统一入口，容易再次漂移） |
| 验证与回滚 | 无需（但也没有可验证对象） |

### 方案 C — 逐步建立统一世界坐标约定（规范先行）

| 维度 | 评估 |
|---|---|
| 优点 | 长期一致；未来系统按规范接入；可与 A 互补（C 是"规范"，A 是"实现"）；能冻结 `ChunkSpace.cs:7–8` 的未定稿负坐标语义 |
| 缺点 | 规范若与 Legacy 现状不符（例如把 `round` 写进去），会迫使 Legacy 改动（风险）；需要迁移窗口与 owner 授权 |
| 对当前 Legacy 地形影响 | 仅"文档化"时为 0；若规范要求改 `voxelSize`/Chunk 尺寸则**会改变挖掘与网格** |
| 对 World V2 影响 | 0（除非要求 V2 接收非整数） |
| 需改现有生产文件 | 规范阶段否；接入阶段是 |
| 改变可见效果 | 规范阶段否；接入阶段**取决于是否动 Legacy 采样** |
| 负坐标/非整数/边界 | 正是规范要裁定的对象 |
| 后续接 Seed/参数 | 容易（有唯一规范） |
| 验证与回滚 | 规范评审 + 变更记录；回滚 = 恢复旧版规范 |

### 3.1 比较结论表

| 判据 | A | B | C |
|---|---|---|---|
| 是否消除"多套转换口径" | ✅ 是 | ❌ 否（已实证漂移） | ✅ 是（规范层） |
| 是否引入新实现风险 | ⚠️ 需声明唯一先例 | — | ⚠️ 需与 Legacy 对齐 |
| 当前可见世界是否会变 | 否（不接线） | 否 | 否（仅文档） |
| 能否承载负坐标冻结 | ✅ | ❌ | ✅ |
| 长期可扩展性 | 高 | 低 | 高 |

---

## 4. 推荐的正式坐标契约草案

> **推荐：C（规范）+ A（实现）组合**；B 仅作为"过渡期现状登记"，<span>不得</span>作为目标形态。
> 证据等级标注：**[源码确认]** / **[合理推断]** / **[候选设计]** / **[待裁决]**

### 4.1 契约草案（10 项议题逐条给出推荐规则）

| # | 议题 | 推荐规则 | 等级 |
|---|---|---|---|
| 1 | **原点与单位** | 世界坐标 = **整数体素格**；原点 = Unity 世界原点 `(0,0,0)`；单位 = **1 体素 = 1 world unit**（当前场景 `voxelSize = 1`、`VoxelFieldVoxelSize = 1`）。契约以"体素尺寸"参数化，不把"米"写死 | [源码确认]+[候选设计] |
| 2 | **X/Y/Z 方向** | 与 Unity 世界轴一致（右手系、+Y 向上）；**Terrain Local 与 World 的关系必须由显式 Transform 决定**（`DensityTerrain.cs:1073` 用 `InverseTransformPoint`），适配器**不得假设轴对齐或恒等** | [源码确认] |
| 3 | **Chunk ↔ 局部** | `chunk = floorDiv(voxel, chunkVoxelSize)`；`local = positiveMod(voxel, chunkVoxelSize)`；`chunkOriginLocal = chunk × (voxelSize × chunkVoxelSize)`；`voxelMinLocal = voxel × voxelSize` | [源码确认]（`ChunkSpace.cs:38/54/95/131`） |
| 4 | **Legacy 浮点 → V2 整数** | **`floor(local / voxelSize)`**（当前 `voxelSize = 1` ⇒ `floor(local)`）。**明确不采用四舍五入**：与 `ChunkSpace.cs:13`、`VoxelField.cs:133` 一致；R44–R47 的 `RoundToInt` 是**测试期临时映射**，不是契约 | [源码确认]+[候选设计] |
| 5 | **负坐标取整** | **向 −∞ 取整**（floor），不是截断也不是四舍五入：`floor(−0.5) = −1`；`floorDiv(−1, 75) = −1`；`positiveMod(−1, 75) = 74`。建议**冻结** `ChunkSpace.cs:142–159` 的现有实现语义 | [候选设计]（实现已有，语义待 owner 确认） |
| 6 | **非整数 / 边界 / 无效输入** | ① 浮点输入一律 floor 到体素；② 采样网格点可落在体素面/角上（`CellSize` 各轴不同），但**体素身份由 floor 唯一确定**；③ 水平方向无界（无无效水平坐标）；④ 垂直 `Y` 有效范围**由所用算法的配置给出**（V2 侧 `[0, WorldHeight−1]`），越界**明确失败**、不钳制；⑤ 调用方必须显式提供 Transform（或"恒等"标记），缺失即**不具备契约条件** | [源码确认]+[候选设计] |
| 7 | **高度与坐标的职责边界** | 坐标契约**只定义位置→位置**；**不定义高度**。高度由具体算法（Legacy：Terrain Local 浮点 Perlin；V2：整数世界 hash + Seed）产生。坐标契约只规定"把哪个坐标喂给哪个算法" | [候选设计] |
| 8 | **`Elevation` 的语义** | `Elevation` = **某套高度算法在某一列上的采样结果**（V2：`HeightFunction.GetTerrainHeight(worldX, worldZ, settings)`，`WorldSkeletonFacts.cs:107`），**不是世界固有高度**。任何 `Elevation` 数值必须同时标注"算法 + 参数 + 坐标空间"；禁止把两套算法的 `Elevation` 相减当作"世界高度误差"（R43–R47 测的是**算法差**） | [源码确认]+[候选设计] |
| 9 | **Seed / 噪声 / 参数载体** | 均**不属于**坐标契约。Seed 决定"同坐标是否同高度"，不改变"坐标如何映射"。参数载体必须能同时表达 Legacy（Perlin 参数 `height/noiseScale/noiseStrength`）与 V2（`TerrainHeight/TerrainAmplitude/TerrainFrequency/Seed`），**不得复用同一结构假装等价**（R44 已证参数语义不同） | [合理推断] |
| 10 | **SeaLevel / Ocean** | **不属于坐标契约**。`Ocean = Elevation < settings.SeaLevel`（`WorldSkeletonFacts.cs:110`）是"高度 + 参数"的派生；坐标层不得参与海陆判定 | [源码确认] |

### 4.2 契约边界（明确不覆盖）

- 不定义世界垂直范围（由算法配置给出，各算法可不同）。
- 不定义高度函数、Seed 语义、噪声算法、参数默认值。
- 不定义材料、生物群系、Region、海陆语义。
- 不定义采样密度与统计口径（R44–R47 的采样属测量方法，不属契约）。
- 不把 R44–R47 的临时映射（`RoundToInt`）升格为契约。

---

## 5. 具体示例（负坐标 / Chunk 边界 / 非整数）

场景参数：`voxelSize = 1`、`chunkVoxelSize = (75,37,50)`、`chunkCount = (2,1,2)`、`chunkStartCoordinate = (0,0,0)`、`CellSize = (1, 0.4933, 0.6667)`。

### 5.1 非整数坐标（floor 与 round 会指向**不同**世界列）

| 例 | Terrain Local Z | `floor(z)` | `RoundToInt(z)` | 差异 |
|---|---|---|---|---|
| `iz = 13` | `8.6667` | **8** | 9 | **不同体素** |
| `iz = 4` | `2.6667` | **2** | 3 | 不同体素 |
| `iz = 52` | `34.6667` | **34** | 35 | 不同体素 |
| `iz = 23` | `15.3333` | 15 | 15 | 一致 |

**量化（R47 索引集，X 轴 `CellSizeX = 1` 恒为整数故无分歧）**：

| 级别 | Z 索引中 floor≠round 的个数 | 受影响样本 | 占比 |
|---|---|---|---|
| A 6×6 | **2 / 6**（`iz = 1, 13`） | 12 / 36 | **33.3%** |
| B 12×12 | **4 / 12**（`iz = 1, 7, 13, 19`） | 48 / 144 | **33.3%** |
| C 18×18 | **7 / 18**（`iz = 1, 4, 7, 10, 13, 19, 52`） | **126 / 324** | **38.9%** |

⇒ **`floor` vs `round` 是实质性技术选择**（C 级近 4 成样本会改指向另一列）。正式契约应采用 **floor**（与 Legacy 体素身份一致）；R44–R47 的 `RoundToInt` 结果应被视为"测试口径"，不代表契约。

### 5.2 负坐标（floor 语义，与 round/截断均不同）

| 例 | 值 | `floor` 结果 | 若用 round | 若用截断 |
|---|---|---|---|---|
| 体素坐标 | `local x = −0.5` | **−1** | 0（银行家舍入） | 0 |
| Chunk | `floorDiv(−1, 75)` | **−1** | — | 0（整数除法截断） |
| Chunk 内局部 | `positiveMod(−1, 75)` | **74** | — | −1（`%` 的符号） |
| 体素 → 局部最小角 | `voxel −1 × 1` | **−1.0** | — | — |

⇒ 负坐标必须固定为「floor + floorDiv + positiveMod」三件套；`ChunkSpace.cs:142–159` 已如此实现，但语义**尚未冻结**（`ChunkSpace.cs:7–8`）。

### 5.3 Chunk 边界（面共享 ≠ 体素共享）

| 体素 x | `voxelToChunk` | `voxelToLocal` | 说明 |
|---|---|---|---|
| 74 | 0 | 74 | chunk(0,0,0) 的最后一个体素 |
| **75** | **1** | **0** | **属于 chunk(1,0,0)**；同时是 chunk(0,0,0) 密度网格的**最后一个采样面**（`SampleCountX = 76`，索引 0..75） |
| 76 | 1 | 1 | chunk(1,0,0) 第 2 个体素 |

⇒ 契约要求：**采样点可跨 Chunk 重复（网格面共享），但体素归属唯一**（由 `floorDiv` 决定）。R46/R47 观测到的 23/47/71 个"重复采样点"正是这种面共享，**不是**体素重复，也不能据此推断网格或材料连续（R47 §六）。

---

## 6. 与高度算法 / Seed / 参数载体 / SeaLevel 的职责边界

| 层 | 负责 | 不负责 | 依据 |
|---|---|---|---|
| **坐标契约层**（本草案） | 位置↔位置：体素化、Chunk 归属、局部原点、负坐标、边界 | 高度数值、Seed、参数、材料、海陆 | `ChunkSpace.cs` 全部函数；`WorldCoordinate.cs:15`（V2 侧无换算） |
| **高度算法层** | 给定坐标输出高度：Legacy `ComputeSurfaceHeight`（Local 浮点）、V2 `HeightFunction`（整数世界 + Seed） | 坐标归属、Chunk 划分 | `DensityFieldGenerator.cs:483–504`；`HeightFunction.cs:27–48` |
| **参数载体层** | 提供算法所需参数（Legacy：`height/noiseScale/noiseStrength`；V2：`TerrainHeight/TerrainAmplitude/TerrainFrequency/WorldHeight/SeaLevel/Seed`） | 坐标换算、海陆判定 | `WorldGenerationSettings.cs:111–153` |
| **派生层** | `Ocean = Elevation < SeaLevel`、`Continent = Ocean ? 0 : 1` | 坐标、高度计算 | `WorldSkeletonFacts.cs:107–113` |

**三处必须显式承认的"不统一"**（不得用契约掩盖）：
1. **高度算法不同**：Legacy Perlin（无种子、Terrain Local 浮点、未钳制）；V2 整数 hash（带 Seed、世界整数、`clamp(round(·), 0, WorldHeight−1)`）。
2. **垂直范围不同**：Legacy `0 ≤ y < 37`；V2 默认 `WorldHeight = 64`（⇒ `SeaLevel = 64` 在此配置下使 `Ocean` 恒真）。
3. **坐标空间不同**：Legacy 为 Terrain Local（经 Transform）；V2 为整数世界。

⇒ 坐标契约能统一的是**第 3 项的位置映射**；第 1、2 项属**算法与参数**，需 owner 决策（§9）。

---

## 7. 迁移步骤与每步验证方法

| 阶段 | 内容 | 生产影响 | 验证方法 | 回滚 |
|---|---|---|---|---|
| **P0（本轮）** | 本候选方案文档 | 无 | 审阅 | 删除本报告 |
| **P1** | 冻结契约规范（含负坐标裁定）+ 变更记录 | 无（纯文档） | 与 §2 源码逐条对照复核 | 恢复旧版规范 |
| **P2** | 新增**中立坐标适配层**（纯 C#、无 Unity 依赖、无 Legacy/V2 类型依赖）+ 独立验证器 | **无**（不接线） | ① 与 `ChunkSpace` 在**正坐标**逐点等价（直接调用对照）；② 负坐标按 §5.2；③ 复现 `floor≠round` 的 126/324 分歧；④ 边界/无效输入用例；⑤ 冻结表 349/0/0 | 删除新增文件（零残留） |
| **P3** | 让 R44–R5x 影子验证器改用该适配层（替换各处临时 `RoundToInt`） | 无（仅验证器） | 统计差异需可解释：预期 C 级 `meanAbs` 由 8.3372 变为新值，须给出"floor 重指向"的解释 | 恢复旧测试映射 |
| **P4（需授权）** | 参数载体单一化（不复用同一结构假装等价）+ Seed 语义 | **可能改变世界**（若替换高度算法） | 仅搬运参数时：`DensityField` 逐样本 bitwise 相同 | 回退参数来源 |
| **P5（需授权）** | 高度算法统一 / Legacy 采样接线 / 场景宿主切换 | **会改变可见世界** | 新世界基线 + 视觉验收 + 挖掘/碰撞回归 + 存档口径 | 关闭开关/回退提交 |

**每阶段通用门禁**：冻结表 349/0/0；`progress.md` 1032 行未改；`SampleScene.unity` 哈希不变（除非该阶段明确授权）；不得让 V2 依赖 Legacy 类型、亦不得让 Legacy 依赖 V2 类型。

---

## 8. 可能改变玩家可见世界的风险清单

| # | 风险动作 | 影响 | 当前状态 |
|---|---|---|---|
| R1 | 把适配层**接进** Legacy 的采样/挖掘（替换 `ChunkSpace` 调用） | 体素归属可能变化 ⇒ **挖掘位置与网格可能改变** | 未做（P5 才允许） |
| R2 | 引入 Seed 替换 `Mathf.PerlinNoise` | **世界外观完全改变**（Perlin 无种子，任何 Seed 都是新世界） | 未做 |
| R3 | 高度算法统一（V2 `HeightFunction` 接管） | Legacy 地表 ~15.5 / V2 ~24.1（R47 均值）⇒ 地形整体抬升约 **8.5 单位**，形态与量化完全不同 | 未做，需 owner |
| R4 | `SeaLevel` / `Ocean` 生效 | 当前 `SeaLevel = 64 > Legacy 垂直范围 37` ⇒ 若直接接入，**全图判为海洋** | 未做，需裁决 |
| R5 | 场景宿主切换（VoxelWorld 取代 `DensityTerrain`） | 世界、挖掘、存档口径全部改变 | 未做（`A-2`/`AD-1` 未裁） |
| R6 | 移除 `debugCutaway`（当前 `= 1`） | 首 Chunk 的 4 m 剖切消失 ⇒ **可见变化** | 未做 |
| R7 | 把 R44–R47 的采样口径当契约 | 不会直接改世界，但会把 `round` 语义固化，导致未来与 Legacy `floor` 冲突 | **本轮已明确排除** |

---

## 9. 仍需用户（游戏设计/owner）决定的事项

| # | 事项 | 为什么必须由人决定 | 若不决定会怎样 |
|---|---|---|---|
| D1 | **世界高度算法最终归属**：保留 Legacy Perlin / 由 V2 `HeightFunction` 接管 / 长期双轨 | 直接决定**外观**（R47 显示两套地表均值差 ~8.5 单位） | 坐标契约无法定稿"Elevation 以谁为准" |
| D2 | **是否需要 Seed 确定性** | 当前 Perlin 无种子；引入 Seed 必然改变世界 | 参数载体与契约无法收敛 |
| D3 | **世界垂直范围口径**：Legacy 37 vs V2 `WorldHeight = 64` | 决定 `Y` 有效区间与 `SeaLevel` 合法域 | `Ocean` 判定无法有意义的落地 |
| D4 | **`SeaLevel` / `Ocean` 是否成为游戏内容** | 属游戏设计（海陆是否存在于本世界） | 派生层无法启用 |
| D5 | **`debugCutaway` 是否长期保留** | 影响可见世界（首 Chunk 剖切） | 影子基准的"可见地表"定义不确定 |
| D6 | **是否允许一次性外观变更**及迁移窗口 | 属产品决策 | 任何统一动作都无法排期 |

**纯技术选择（本报告已给推荐，无需用户裁决）**：floor 取整、`floorDiv`/`positiveMod`、体素归属唯一性、适配层位置与命名、契约文档结构。

---

## 10. 后续最小实施任务与停止条件

### 10.1 建议的下一轮（需显式授权后才可开始）
1. **P1 + P2 合并为一次最小实施**：先冻结规范（含 §4.1 十条），再新增**中立坐标适配层**（纯 C#、零 Unity 依赖、零 Legacy/V2 类型依赖）+ 一个只读验证器。
2. 验证器须断言：正坐标与 `ChunkSpace` 逐点等价、负坐标符合 §5.2、`floor≠round` 的 126/324 分歧可复现、边界（x=75 归属 chunk 1）、无效输入（缺 Transform/越界 Y）明确失败。
3. **不接线**：Legacy 与 V2 一行不改。

### 10.2 本轮的停止条件（已满足）
- 仅新增 `_phase2b_validation/round48_coordinate_contract_proposal.md`；
- 未新增生产代码/验证器；未修改任何既有文件；未运行 Unity；
- 冻结表 349/0/0；HEAD 未变；staged 空；工作区仅多出本报告。

### 10.3 明确不做
- 不把本草案实施为正式契约（需 P1 授权）；
- 不修改 Legacy 高度算法/坐标实现；
- 不统一 Seed、不统一参数载体、不裁决 `SeaLevel`；
- 不接入 World V2 生产流程；不开始 R49。

---

## 11. 结论

1. **推荐方案**：**C（统一世界坐标规范）+ A（独立坐标适配层）** 的组合 —— 以 C 产出的规范作为唯一契约文本，以 A 作为该规范的**唯一实现落点**；**B（各用自身坐标）仅登记为过渡现状**，不作为目标形态。
2. **为什么**：
   - B 已被实证证伪：R44–R47 各验证器各自实现临时映射（`RoundToInt`），与 Legacy 自身 `floor` 规则冲突，**C 级 126/324（38.9%）样本会指向不同世界列** ⇒ "边界显式转换"在实践中必然漂移。
   - A 是唯一能同时满足"两套系统互不依赖"（`LS65`/`RAB-L2`）与"单一实现点"的形态；
   - C 能冻结 `ChunkSpace.cs:7–8` 自注"未最终确定"的负坐标语义；Legacy 现有实现（`floor`/`floorDiv`/`positiveMod`）可直接作为冻结蓝本，**不需要改 Legacy 代码**。
3. **实施它会影响什么**：
   - **规范与适配层阶段：零影响** —— 不改 Legacy、不改 V2、不接线、不改场景；当前可见世界与 R47 时完全相同。
   - **只有到 P4/P5（需授权）才可能改变可见世界**：Seed 引入（R2）、高度算法统一（R3，地表差 ~8.5 单位）、`SeaLevel` 生效（R4，当前配置下全海洋）、宿主切换（R5）、移除 `debugCutaway`（R6）。
4. **仍需证据或授权的部分**：
   - **需 owner 裁决**：D1–D6（尤其高度算法归属、Seed 必要性、垂直范围口径、`SeaLevel` 是否成为内容、`debugCutaway` 去留、是否允许一次性外观变更）。
   - **仍需证据**：噪声算法中"振幅差 vs 相位差"的定量分离；单 Chunk 统计的收敛密度；映射效应的精确净量级（R47 已把"7.42%"降级为采样波动）；渲染/网格层面的一致性（本系列至今**未做**渲染验证）。

**本轮未改变生产逻辑，未改变场景，未改变玩家可见世界。**
