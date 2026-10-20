# Terrain Mining A 阶段修复报告：Air Voxel 误选与挖掘空操作

- 工程：`D:\gamemaking\My project`（Unity 2022.3.62f1c1）
- 范围：**仅 A 阶段** —— ①地表命中映射到空气侧 Voxel ②未编辑空气区域被判定可挖掘 ③已挖空 Voxel 仍可选中 ④用过期 `selectedVoxel` 挖掘并静默吞掉输入
- 不在本次范围：相机跳动（B 问题）、Weld 优化、Collider 分帧、F2（覆盖率与可见性）、A4（`SampleFaceSideMaterial` 静默 Air 回退）
- 验证方式：Editor 批处理（`-batchmode -nographics -quit -executeMethod`）自动化断言
- **Editor Play Mode 人机验证：未执行（本报告全部结论仅来自批处理自动化，不得据此宣称手感/观感已验收）**

---

## 1. 改动文件（4 个）

| # | 文件 | 跟踪状态 | 角色 | 关键改动 |
|---|------|----------|------|----------|
| 1 | `Assets/Scripts/Terrain/TerrainVoxelSelector.cs` | untracked（新增源文件） | 解析层 | 新增 `TryResolveSolidVoxel(...)`（命中点→**实体侧** Voxel）与 `TryResolveMineTarget(...)`（解析 + 权威有效性校验，失败给 `reason`）。原 `TrySelectVoxel/SelectVoxel` 保持不变（旧测试仍走旧契约）。SHA256 `DC448A37DBE9B8297158EE858F81F1F6C95881043B26ED9C612973AD1AB86509` |
| 2 | `Assets/Scripts/Terrain/DensityTerrain.cs` | tracked-modified | 权威查询 | 新增 `TryGetVoxelMaterialState(voxel, out hasBaseMaterial, out isEditedAir, out reason)`：编辑态取自 `VoxelField.Get`，材料真值取自基础 `DensityField` 在 **Voxel 中心**采样（`density > 0`）。SHA256 `CF11C25B6DB0A2F1847A6D7E69880A3E41D12F54E71583E6A6D2FBD6FF655BC4` |
| 3 | `Assets/Scripts/Player/PlayerMiningController.cs` | tracked-modified | 选择/挖掘 | 选择唯一来源改为 `TryResolveMineTarget`；新增 `selectedRaycastHitNormal` 字段并在 `ClearSelectedVoxel()` 中一并清空 + `Hide()`；`TryMineVoxel()` 挖掘**前**用捕获的命中点/法线复验、**后**再复验并刷新/清除。SHA256 `906C3DA7477E2E107FAB2FBE7443436D232A7C56F8EBA5DD390B0D3B04E3E398` |
| 4 | `Assets/Editor/VoxelCoordinateMigrationStageB3CValidation.cs` | tracked-modified | 测试 | 新增 T15 段（31 条断言，`RunAll` 中 T14 之后调用）；修正连续挖掘用例命中平面的 off-by-one（测试侧）。SHA256 `E5DB05BE4BC0E1556554797BB63B6B138822A2E646254024FDB93D95A141E066` |

未改动的关键契约（按约束保持原样）：`DensityField`/`MaterialField` 语义、`VoxelField.DefaultState`、Voxel/Chunk 坐标契约、`FaceId`/`FaceFragment`/可见性规则、`TerrainMeshGenerator`、Weld、`CameraController`、`PlayerInputController`、Mining V1 其它行为；未新增多层 Raycast / 反向目标搜索 / 注册表。

---

## 2. 原始成因

链路：`Camera Raycast → MeshCollider → TerrainChunkView → hit.point → DensityTerrain.WorldToLocalPosition → VoxelField.PositionToVoxel → selectedVoxel → mining`。

| 现象 | 原始成因 |
|------|----------|
| ①命中地表却选中**空气侧** Voxel | `VoxelField.PositionToVoxel` 是 min-corner `floor` 半开区间；命中点恰好落在整数表面平面（`y = k`，例如平面 y=15）时，`floor` 把点判给 **k 那一层**（空气侧），而拥有该三角面的实体 Voxel 是 **k−1 层**。旧链路无任何法线偏移或实体侧校正，且不做有效性检查。 |
| ②未编辑的空气区域被判定**可挖掘** | 选择链路只做"点→Voxel"，没有材料判据。`VoxelField.DefaultState = Solid` 的语义是"**未编辑**"，不是"该处有材料"；地表以上从未被编辑的空气 Voxel 因此 `Get()` 返回 `Solid`，被当成实体目标。 |
| ③**已挖空**的 Voxel 仍可被选中 | 选择链路未查编辑态（Air）；且被 ①放大：挖掉第 k 层后，下一次命中新地板平面 k 时 `floor` 恰好落到刚挖空的 k 层 Air Voxel 本身。 |
| ④用**过期 `selectedVoxel`** 挖掘、静默消耗输入 | `TryMineVoxel` 直接使用上一帧缓存的 `selectedVoxel`；目标失效时 `if (previousState != VoxelState.Air)` 条件不成立 ⇒ 跳过重建、仍 `return true` ⇒ F 键输入被静默吞掉（"挖掘空气"、无任何反馈）。 |

---

## 3. 修复如何工作

**(1) 命中点 → 实体侧 Voxel**（`TerrainVoxelSelector.TryResolveSolidVoxel`）
1. `terrain.WorldToLocalPosition(hit.point)` 得到 Terrain Local 命中点；
2. `terrain.transform.InverseTransformDirection(hit.normal)` 转 Local 后取 **主轴 + 符号**，得到指向表面外侧的轴对齐法线（Voxel 契约要求 Terrain Local 中轴对齐的面）；
3. `probe = localPoint − axisNormal × epsilon`，其中 `epsilon = Mathf.Max(1e-4f, VoxelSize × 1e-3f)`：把探测点严格推离整数平面、进入**实体侧**内部；
4. `PositionToVoxel(probe)` ⇒ 命中"拥有该面的实体 Voxel"（平面 y=15 → `(30,14,30)`，不再是 `(30,15,30)`）。
   代价：仍是 **单次 Raycast + 一次本地解析**，无多层 Raycast、无反向搜索、无注册表；偏移量 ≪ 1 个 Voxel，不会跨到相邻表面。

**(2) 目标有效性 = 解析成功 ∧ 未编辑为 Air ∧ 该处确有基础材料**（`TryResolveMineTarget`）
- 不满足即 `false` + 明确 `reason`（例如 `Voxel (30, 15, 30) has no base material (BaseDensity <= 0 at voxel centre).`）。

**(3) 选择与高亮同步**（`PlayerMiningController.UpdateVoxelSelectionAndHighlight`）
- 唯一来源是 `TryResolveMineTarget`；成功才 `selectedVoxel = …` + 记录命中点/法线（供挖掘复验）+ `highlight.Show(...)`；
- 失败 ⇒ `ClearSelectedVoxel()`（清空点/法线状态）并 `Hide()` ⇒ 空气侧不再残留高亮。

**(4) 挖掘前复验、挖掘后复验**（`TryMineVoxel`）
- **前**：用捕捉到的 `selectedRaycastHitWorld/selectedRaycastHitNormal` 重新解析（不重新 Raycast、不另算一套坐标）；
  - 无效 ⇒ 记录 `[PlayerMining] Selection invalid, mine skipped: <reason>`，`ClearSelectedVoxel()`，`return false` —— **不写 `VoxelField`、不触发重建**；
  - 有效 ⇒ **以复验得到的坐标**作为本次目标写 `Air` 并 `RebuildChunksAffectedByVoxel`（写的必然是刚校验过的那个 Voxel）；
- 因为复验已排除 `isEditedAir`，不存在"重复写 Air 假装成功"的路径；
- **后**：用同一命中点/法线再复验一次，仍有效 ⇒ 刷新 `selectedVoxel` 并同步高亮；失效 ⇒ `ClearSelectedVoxel()` + `Hide()`（避免高亮停留在已挖空位置）。

---

## 4. 使用了哪些权威数据

| 数据 | 来源（既有权威接口） | 说明 |
|------|----------------------|------|
| 编辑状态 | `VoxelField.Get(voxel)` | 唯一编辑真值；`DefaultState = Solid` 只表示"未编辑"，**不**表示"有材料" |
| 材料真值 | `chunk.Density`（基础 `DensityField`） | 在 **Voxel 中心** `(voxel + 0.5) × VoxelSize` 采样，`density > 0` 才算有材料；与本工程既有约定一致（`VoxelDensityAdapter.Build` 同样按中心采样） |
| 采样入口 | `VoxelAwareMeshRuntimeMutation.TrySampleBaseDensity(chunk, centre, out density, out error)` | 复用既有接口，不新建密度/材料系统 |
| 空间映射 | `DensityTerrain.WorldToLocalPosition` / `VoxelField.PositionToVoxel` / `ChunkSpace.VoxelToChunk` | 全部沿用既有坐标契约（未修改） |

未引入任何新的真值层、缓存或注册表。

---

## 5. 选择 / 高亮 / 挖掘的一致性

同一帧、同一 `hit.point` + `hit.normal` 下三者共用同一解析结果与同一份权威数据：

| 环节 | 行为 |
|------|------|
| 选择 | `selectedVoxel` = 实体侧 Voxel（T15 覆盖 ±X/±Y/±Z × 3 种 chunk 框架（含非零 `chunkStart` 与负坐标）× 非 identity Transform） |
| 高亮 | 只在解析成功时 `Show(同一个 voxel)`；失败/失效/挖空后 `Hide()` —— 不会高亮空气侧 |
| 挖掘 | 写 `Air` 的目标 = 挖掘前刚复验通过的 Voxel（同一坐标来源）；无效时明确失败（`return false` + 日志），不再静默消耗输入 |
| 失败语义 | 任何一步失败都走 `ClearSelectedVoxel()`，下一帧由正常选择流程重建目标（不存在"半失效"状态） |

因此不存在"高亮 A / 挖掘 B"或"选中空气 / 挖掘实体"的路径。

---

## 6. 测试与回归

**新增 T15（31 条断言，全部 PASS）**，覆盖：
1. 六方向（±X/±Y/±Z）命中解析 → 实体侧 Voxel，在 3 种 chunk 框架下（`chunk(0,0,0)`、`chunkStart=(5,0,7)` 边界、负坐标 `chunk(-1,0,-1)`，共 3 组 × 7 = 21 条）；
2. 体素对齐地表（平面 y=15）⇒ `(30,14,30)`，并断言**不得**解析到空气侧 `(30,15,30)`；
3. 未编辑的空气 Voxel 不可挖掘（`reason` 明确给出 `BaseDensity <= 0 at voxel centre`）；
4. 已挖 Voxel 的权威状态可查（`editedAir=True baseMaterial=True`）；
5. 非 identity Transform（pos `(10,3,-7)` + rotY 37°）下解析正确；另有 1 条"相邻列挖掘入口可用（rebuilt=1）"断言；
6. 连续 10 次挖掘：每次都解析到**当时的表层实体 Voxel**、无空操作（旧 Air 目标 0 次）、只改变目标 Chunk（其它 Chunk 改变 0 个）、且目标 Chunk 出现显式隐藏覆写（材料侧被挖空）。

**B3C 套件（含 T1–T15 全部既有断言）**：`Checks = 232`、`Failures = 0`、`Result = PASS`、exit=0（日志 `_phase2b_validation/a_b3c2.log`）。

**全量回归（32 套件）：32/32 PASS —— 共 1599 条检查，0 失败，0 编译错误，全部套件 exit=0**（汇总：`_phase2b_validation/regression_summary.txt`，逐套件日志 `regression_<Suite>.log`）。

**过程中发现并修正 1 处测试自身的缺陷（不影响生产代码）**：T15 连续挖掘用例最初把命中平面取低了一格（`SurfaceVoxelY - 1 - i`），导致 10 次挖掘全部挖在被埋藏的 Voxel 上——埋藏 Voxel 的共面片段在基础几何中并不存在，因此"显式隐藏覆写"断言合理地失败。修正为 `SurfaceVoxelY - i` 后，每轮都挖当时的表层 Voxel，断言按真实语义成立；该修正**只改测试**，未放宽任何既有断言、未改任何生产逻辑。

**为适配 A 阶段新契约，同步更新了 2 个既有测试套件的"前置条件"（断言语义保持并强化）**：
- `Assets/Editor/TerrainVoxelMiningValidation.cs`：原夹具是没有 Chunk/Density 的**空壳 `DensityTerrain`**（无法提供材料证据），改为真实生成的 2×2×2 Chunk、`VoxelSize=1`、平地 `height=12`（Chunk 起点 (−1,−1,−1) ⇒ 覆盖 Voxel 范围 [−16,15]³，含负坐标 (−2,−3,−4)）；`SetSelectedVoxel` 注入选择时一并写入与目标一致的命中点/法线（生产路径必然存在，因为选择本身就来自 Raycast 命中）。唯一一处**断言更新**：Case 2 原断言"挖掘后第二次 F 仍应成功（旧选择继续有效）"与 A 阶段要求"被挖空的 Voxel 不得再作为有效目标 / 不得静默空操作"直接冲突，改为断言"第二次 F **明确失败** + 选择已被清除（`HasSelectedVoxel == false`）"，同时保留原有"Air 仍为 Air / 稀疏存储不新增条目"断言（该套件 30 → 31 条断言，全通过）。
- `Assets/Editor/TerrainVoxelMeshIntegrationValidation.cs`：仅在其 `SetControllerSelection` 中补写与目标 Voxel 一致的命中点/法线（**断言一字未改**，53 条全通过）。
- 说明：这两处都是"把测试注入的状态补齐为生产必然成立的状态"，不是放宽生产校验；生产代码未因此改动。

---

## 7. 未解决项 / 明确本轮不做

- **相机跳动（B 问题）未修**：`Input.GetAxis("Mouse X"/"Mouse Y")` 是逐帧累积增量，`PlayerInputController.Update` 又乘 `Time.deltaTime`，而 `CameraController.RotateCamera` 立即按绝对角度应用 ⇒ 卡顿帧（实测 ~160 ms/次挖掘：eval ≈13 ms、Assemble ≈127 ms（其中 weld ≈110–125 ms）、Collider cook ≈25–30 ms）会把累积位移一次性应用，表现为视角跳变。本阶段未改输入语义、未加 clamp/回放策略。
- **性能热点未动**：Weld 占 Assemble 绝大部分、Collider 重建未分帧。
- **F2（覆盖率不属于可见性）与 A4（`SampleFaceSideMaterial` 静默回退 Air）**：保持已知状态，未修改。
- **Editor Play Mode 未验证**：批处理断言通过 ≠ 手感/观感验收；真机手感仍需在 Editor Play Mode 中确认（本阶段不得声明已验证）。
- **已知设计副作用（1 帧高亮闪烁）**：F 键挖空目标后，后置复验用同一命中点解析会指向"已成 Air 的 Voxel"⇒ 判定失效 ⇒ 立即清除选择并 `Hide()`；下一帧 `Update()` 的正常选择流程会用新的 Raycast 命中下降后的地表，重新选中/高亮下一个实体 Voxel。即"每次挖掘后高亮消失 1 帧"。这是"不保留失效（已挖空）目标"的必然结果，符合 A 阶段要求；如后续希望完全无闪烁，需要授权在挖掘后按法线方向推进命中点重算目标（本轮明确不做：属于新增目标搜索机制）。
- 未执行 `git add` / `commit` / `push`。

---

## 8. 完整 git diff 与未跟踪清单

证据文件：`_phase2b_validation/a_phase_git_diff.txt`（约 666 KB），包含 8 节：
1. `git status --porcelain -uall` 全量输出；
2. 未跟踪文件全清单（**生成时刻快照 1090 项**；其后 `_phase2b_validation/` 内新增回归日志与本报告，当前为 1094 项）+ 聚合说明；
3. `git diff --stat`；
4. 空白校验：`git diff --check` → **exit=0，空白类发现 0**；`git diff --cached --check` → **exit=0（暂存区为空）**；
5. **完整 `git diff`（全部跟踪文件修改）**；
6. 主工程 ↔ 验证工程 `.cs` 同步校验：**160 对，0 不匹配，0 缺失**（仅存在于验证工程的 `.cs`：0 个）；
7. A 阶段 4 个改动文件的跟踪状态 + SHA256（见本文第 1 节表格）；
8. 为适配 A 阶段新契约而补齐前置条件的 2 个既有测试（`TerrainVoxelMiningValidation.cs`、`TerrainVoxelMeshIntegrationValidation.cs`：SHA256 + 逐文件理由 + "断言只强化不放宽"的说明）。

汇总（**当前**，`git status` 复核）：跟踪文件修改 **15** 个、暂存区 **0** 项；未跟踪 **`Assets/` 84 个（39 个 `.cs` + 45 个 `.meta`）+ 3 个目录**（`_b3b_diff/` 2 文件、`_finalization_backup/` 12 文件、`_phase2b_validation/` 996 文件），合计 **1094** 个未跟踪文件（证据文件内的清单为生成时刻的 1090 项快照，差额即本轮新增的回归日志与报告）。

> 注意：未跟踪的 `Assets/**/*.cs`（含 `TerrainVoxelSelector.cs`、`VoxelField.cs`、`VoxelDensityAdapter.cs`、`VoxelAwareMeshPrecut.cs`、TerrainModification V2 系列等）是本工程编译所必需的新增源文件，其正文**不在** `git diff` 中，因此第 7 节额外给出 A 阶段相关文件的归属与 SHA256。工作区无暂存内容；未做任何提交。

---

## 回归判定

**结论：全量回归 32/32 PASS —— 1599 条检查、0 失败、0 编译错误、全部 exit=0。**
（`_phase2b_validation/regression_summary.txt`；本表为本轮 A 阶段修复完成后的最终结果。）

| 套件 | exit | checks | failedChecks | csErrors | 结果 |
|---|---|---|---|---|---|
| ProtoAB | 0 | 55 | 0 | 0 | PASS |
| Phase1 | 0 | 25 | 0 | 0 | PASS |
| Phase2A | 0 | 32 | 0 | 0 | PASS |
| Phase2B | 0 | 77 | 0 | 0 | PASS |
| Phase3 | 0 | 108 | 0 | 0 | PASS |
| SchemeA | 0 | 85 | 0 | 0 | PASS |
| StageB1 | 0 | 51 | 0 | 0 | PASS |
| StageB2 | 0 | 317 | 0 | 0 | PASS |
| StageB3A | 0 | 52 | 0 | 0 | PASS |
| StageB3B | 0 | 106 | 0 | 0 | PASS |
| StageB3C（含新增 T15×31） | 0 | 232 | 0 | 0 | PASS |
| MeshIntegration | 0 | 53 | 0 | 0 | PASS |
| VoxelMining（30 → 31 条断言） | 0 | 31 | 0 | 0 | PASS |
| VoxelHighlight | 0 | 36 | 0 | 0 | PASS |
| VoxelSelector | 0 | 43 | 0 | 0 | PASS |
| VoxelDensityAdapter | 0 | 22 | 0 | 0 | PASS |
| VoxelMarchingCubes | 0 | 29 | 0 | 0 | PASS |
| VoxelFieldOwnership | 0 | 14 | 0 | 0 | PASS |
| MultiChunk | 0 | 0 | 0 | 0 | PASS |
| MiningV1 | 0 | 0 | 0 | 0 | PASS |
| ChunkLifecycle | 0 | 0 | 0 | 0 | PASS |
| V2_ChunkIntegration | 0 | 5 | 0 | 0 | PASS |
| V2_EditGrid | 0 | 40 | 0 | 0 | PASS |
| V2_EditLayer | 0 | 24 | 0 | 0 | PASS |
| V2_EffectiveDensity | 0 | 26 | 0 | 0 | PASS |
| V2_EffectiveField | 0 | 2 | 0 | 0 | PASS |
| V2_SurfaceSampleEnumerator | 0 | 22 | 0 | 0 | PASS |
| V2_SurfaceSampling | 0 | 23 | 0 | 0 | PASS |
| V2_TerrainQuery | 0 | 21 | 0 | 0 | PASS |
| V2_Mining | 0 | 10 | 0 | 0 | PASS |
| V2_Main | 0 | 16 | 0 | 0 | PASS |
| V2_VoxelField | 0 | 42 | 0 | 0 | PASS |
| **合计** | **0** | **1599** | **0** | **0** | **32/32 PASS** |

过程中的中间态（供追溯）：首轮全量回归曾出现 `MeshIntegration` 1 处、`VoxelMining` 14 处失败，原因**不是**生产语义回归，而是这两个既有套件用反射直接注入 `hasSelectedVoxel/selectedVoxel/selectedTerrain` 时既没有注入命中点/法线、`TerrainVoxelMiningValidation` 的夹具又是没有 Chunk/Density 的空壳地形（A 阶段要求"以权威材料证据为前提"，这类注入状态在生产中不可能出现）。按"补齐生产必然成立的前置条件"更新测试夹具与注入数据（并修正上面提到的 Case 2 过期契约断言）后，本轮 32/32 全通过。
