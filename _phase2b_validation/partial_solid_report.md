# 修正轮报告：允许选中“部分实体 Voxel”（中心密度 ≤ 0 但 Voxel 内含实体表面）

- 工程：`D:\gamemaking\My project`（Unity 2022.3.62f1c1）
- 范围：**只修 Voxel 选择的“实体存在判据”**；相机 / Weld / Collider / FaceId / Visibility / Terrain Mesh 生成 / Mining V1 其它逻辑均未改动
- **Editor Play Mode 人机验证：未执行**（本报告全部结论来自批处理自动化断言）
- 证据文件：`_phase2b_validation/partial_solid_git_diff.txt`（完整 diff + 未跟踪清单 + 附录全文）、`fix_B3C_T16.log`、`fix_VoxelMining_T16.log`、`regression_summary.txt`

---

## 1. 结论

旧判据把“目标 Voxel 中心的基础密度 > 0”当作实体存在的唯一证据；但地形的实体由**基础 DensityField 的 Marching Cubes 等值面**定义，等值面会穿过 Voxel 内部 ⇒ 中心密度 ≤ 0 的 Voxel 完全可能含有可见实体表面（“部分实体 Voxel”），被旧判据误拒。

本轮把判据改为：**基础密度场在目标 Voxel 内的“命中侧探测点” > 0，或在 Voxel 中心 > 0（两者取或）**。两个采样点都在目标 Voxel 内部，且采样接口与 Marching Cubes 使用的 DensityField（Origin / CellSize / SampleCount / 三线性插值）完全一致，`IsoLevel = 0` ⇒ `density > 0` 即实体侧。

T16 用例的实测数值：目标 Voxel 中心 `−0.2500`（旧判据拒绝），命中侧探测点 `+0.0010`（新判据接受）；完全空气 Voxel 中心 `−1.2500`、探测点 `−1.2490`（新判据仍拒绝）。

---

## 2. 调用链定位（中心密度检查在哪里）

```
Camera Raycast → MeshCollider → TerrainChunkView → hit.point + hit.normal
  → TerrainVoxelSelector.TryResolveMineTarget(hitPointWorld, hitNormalWorld, terrain, …)
       ├─ TryResolveSolidVoxel(...)                       // 命中点 → 实体侧 Voxel + terrainLocalProbe
       │      probe = localHit − axisNormal × ε,  ε = max(1e-4, VoxelSize×1e-3)
       │      voxel = VoxelField.PositionToVoxel(probe)   // ⇒ probe ∈ voxel（按定义）
       └─ DensityTerrain.TryGetVoxelMaterialState(...)    // ← 本轮的改动点
              isEditedAir     = VoxelField.Get(voxel) == Air
              hasBaseMaterial = BaseDensity(?) > 0         // 旧：仅 Voxel 中心
  → PlayerMiningController（选择/高亮/挖掘共用该结果；挖掘前用同一命中点/法线复验）
```

旧实现（A 阶段）中 `TryResolveMineTarget` 丢弃了 `TryResolveSolidVoxel` 的 `terrainLocalProbe`（`out _`），只把 **Voxel 中心**交给 `TrySampleBaseDensity`。本轮把探测点接上，并在 `DensityTerrain` 新增带探测点的重载。

---

## 3. 坐标一致性核查（为什么“中心密度”不能当唯一判据）

| 环节 | 结论 |
|------|------|
| Marching Cubes（`TerrainMeshGenerator.Generate`） | 以 **DensityField 的采样点作为 MC Cell 角点**：`field[sx,sy,sz]` + `field.GetChunkLocalPosition(sx,sy,sz)`（= 索引 × CellSize，Chunk Local），`density > isoLevel`（`IsoLevel = 0`）判实体，等值面在“正负采样之间”线性插值得到 |
| 基础 DensityField 的构建（`DensityTerrain.BuildChunk` → `DensityFieldGenerator.Generate`） | `new DensityField(resolution, …, origin = chunk.ChunkOrigin, size = chunkWorldSize)`；`SampleCount = resolution + 1`；`CellSize = chunkWorldSize / resolution` ⇒ **采样网格锚定在 Chunk 最小角**，不是 Voxel 中心 |
| `VoxelAwareMeshRuntimeMutation.TrySampleBaseDensity` | `(terrainLocal − Origin) / CellSize` → 连续坐标 → 对同一组采样做**三线性插值**（越界会给出 `BASE DENSITY SAMPLE DOMAIN GAP` 诊断）⇒ 与 MC 使用同一份数据、同一套坐标约定 ✅ |
| 实际场景参数（`SampleScene.unity`） | `voxelSize = 1`、`chunkVoxelSize = (75,37,50)`、`resolution = 75`、HeightField、`height = 15.7`、noise on ⇒ `CellSize = (1.0000, 0.4933, 0.6667)`，即每 Voxel 的采样数 ≈ X 1.00 / Y 2.03 / Z 1.50 |

由此可确认两件事：

1. **“基础密度采样坐标”与“MC 使用的 Density Field 坐标”确实一致** ✅（同一 `Origin`/`CellSize`/`SampleCount`，同一份 `chunk.Density` 数据；采样接口只是对同一网格做三线性求值）。
2. **A 阶段把“Voxel 中心采样”当权威判据是对地形 DensityField 的误建模**：`VoxelDensityAdapter.Build` 生成的是“每个 Voxel 一个中心采样”的**合成**网格（Origin 偏移 0.5 Voxel），而地形 Chunk 的 DensityField 是**按 resolution 采样、锚定 Chunk 最小角**的网格（X 轴采样恰好落在 Voxel 角点、Y/Z 轴每 Voxel 有 1.5–2 个采样）。因此：
   - “Voxel 中心”只是一个插值值，不是存储样本；
   - 等值面穿过 Voxel 内部时，Voxel 内完全可能有实体（甚至多个正采样），而中心插值 ≤ 0。

---

## 4. 新判据（实现方向与约束逐条对应）

```csharp
// DensityTerrain.TryGetVoxelMaterialState(voxel, terrainLocalHitProbe, out hasBaseMaterial, out isEditedAir, out reason)
isEditedAir = VoxelField.Get(voxel) == VoxelState.Air;              // 编辑真值，语义未改

float centreDensity = TrySampleBaseDensity(chunk, voxelCentre);     // 证据 1（既有判据，保留）
bool  probeInside   = VoxelField.PositionToVoxel(terrainLocalHitProbe) == voxel; // 防御校验
float probeDensity  = probeInside ? TrySampleBaseDensity(chunk, terrainLocalHitProbe) : 0f; // 证据 2（新增）

hasBaseMaterial = (centreDensity > 0f) || (probeDensity > 0f);      // 两点都在目标 Voxel 内部
```

| 约束 | 如何满足 |
|------|----------|
| 真实空气区域不能被选中 | 完全空气 Voxel 的**两个**证据点密度都 ≤ 0 ⇒ 拒绝（T16 实测 中心 −1.2500 / 探测 −1.2490，且其下方相邻 Voxel 是实体也救不了它） |
| 已编辑为 Air 的 Voxel 不能被选中 | `isEditedAir` 检查保持（选择与挖掘前复验都会走这条），T16 断言 `reason=… is edited as Air.` |
| 命中点仍通过 `hit.normal` 解析实体侧 Voxel | `TryResolveSolidVoxel` 未改（主轴 + 符号 + 单向 ε 偏移） |
| 选择/高亮/挖掘同一最终目标 | `TryResolveMineTarget` 仍是唯一来源；控制器把同一个 `selectedVoxel` 用于 `highlight.Show(...)` 与 `VoxelField.Set(...)`；Case 10 断言 `highlight.CurrentVoxel == 注入目标 == 被写 Air 的 Voxel` |
| 挖掘前仍重新验证目标 | `TryMineVoxel` 前/后复验逻辑未改，只是判据升级；Case 10 断言二次 F 明确失败、不写任何 Voxel |
| 不修改 VoxelField 默认状态语义 | 未改 `VoxelField`（`DefaultState = Solid` 仍只表示“未编辑”） |
| 不改变坐标契约 / Terrain Local Space | 未改 `PositionToVoxel`、`ChunkSpace`、Density 采样坐标；探测点直接复用解析阶段的 Terrain Local 结果 |
| 不把邻近 Voxel 的实体算进来 | 两个证据点都在目标 Voxel 内部；且新增防御校验 `PositionToVoxel(probe) == voxel`（T16 显式断言该等式） |
| 不因“Raycast 命中地形”就无条件放行 | 放行仍要求基础密度场在 Voxel 内判为实体（探测点或中心），命中只是用来确定“哪个 Voxel + 哪个探测点” |
| 不用中心密度作为唯一判据 | 判据 = 中心 ∨ 命中侧探测点 |
| 不向多个方向搜索 Voxel | 只有 1 个采样点（沿命中法线单向 ε 偏移，且落在解析出的目标 Voxel 内），没有任何方向搜索 / 邻域扫描 / Registry |
| 不修改 Mesh 生成 / 可见性 | 未改 `TerrainMeshGenerator`、`VoxelAwareMeshPrecut`、Visibility 相关代码 |

诊断增强：材料判定的失败原因现在会打印两个采样值（`centre=…, hitProbe=…`），越界/缺 Chunk 时保留原有诊断文本。

---

## 5. 修改文件（4 个）

| # | 文件 | 跟踪状态 | 改动 | SHA256 |
|---|------|----------|------|--------|
| 1 | `Assets/Scripts/Terrain/DensityTerrain.cs` | tracked-modified | `TryGetVoxelMaterialState` 新增“命中侧探测点”重载（中心 ∨ 探测点 取或）、探测点归属防御校验、诊断 reason；旧 4 参重载保留（等价于中心判据，供既有调用/查询使用） | `0EB935DBA79CBD40ECC9F1731F705E7BBCDC206911E606EF7A4744499BC756EC` |
| 2 | `Assets/Scripts/Terrain/TerrainVoxelSelector.cs` | untracked（既有新文件） | `TryResolveMineTarget` 捕获并传入 `TryResolveSolidVoxel` 的 `terrainLocalProbe`；文档说明新判据 | `35D62EEEA13D3CDD725BDA5AD641B1EEB0AB8DA28C1BAB9C1B4C25221A34E529` |
| 3 | `Assets/Editor/VoxelCoordinateMigrationStageB3CValidation.cs` | tracked-modified | 新增 **T16**（32 条断言），`RunAll` 中注册 | `1900F201C5CC0838814885DFF3600B652D4186AFCDFA26B2D8C0B79E1ACA477D` |
| 4 | `Assets/Editor/TerrainVoxelMiningValidation.cs` | untracked（既有新文件） | 新增 **Case 10**（10 条断言，控制器级）；夹具 `CreateTemporaryTerrain` 增加 `height` 可选参数；`SetSelectedVoxel` 支持显式命中点/法线 | `0D5D02B3E75F3CDD33490264F8FBFFD4FF671BAB9968AC95216C4EAB6063CC590` |

未改动的关键文件（本轮零改动）：`PlayerMiningController.cs`（选择/挖掘流程不变）、`VoxelField.cs`、`TerrainMeshGenerator.cs`、`VoxelAwareMeshPrecut.cs`、`VoxelAwareMeshRuntimeMutation.cs`（仅复用其 `TrySampleBaseDensity`）、`CameraController.cs`/`PlayerInputController.cs`。

---

## 6. 新增测试与结果

### 6.1 T16（B3C 套件，32 条断言，全 PASS）

| 覆盖点 | 断言证据（节选） |
|--------|------------------|
| ① 中心 ≤ 0 但含实体表面 ⇒ 允许选择 | `T16: 用例成立 —— 目标 Voxel 中心基础密度 <= 0（-0.2500）`；`T16: 命中侧探测点基础密度 > 0（0.0010）`；`T16: …部分实体 Voxel 允许选中（期望 (30, 14, 30)，实际 (30, 14, 30)）` |
| 不把相邻 Voxel 的实体算进来 | `T16: 命中侧探测点属于目标 Voxel 本身`（`PositionToVoxel(probe) == voxel`） |
| ② 完全空气 Voxel ⇒ 不能选择 | `中心 -1.2500，探测 -1.2490`；`T16: 完全空气 Voxel 不可选中（尽管下方相邻 Voxel 是实体，reason=Voxel (30, 15, 30) has no base material …）` |
| ③ 已挖空 Voxel ⇒ 不能选择 | `T16: 已挖 Voxel 的权威状态可查（editedAir=True baseMaterial=True）`；`T16: 已编辑为 Air 的 Voxel 不可选中（reason=… is edited as Air.）` |
| ④ 中心 > 0 的普通实体 Voxel ⇒ 正常选择与挖掘 | `中心密度 > 0（0.2500）`；`正常选中`；`正常挖掘（state=Air，rebuilt=1）`；`只编辑目标 Voxel（相邻被挖 0）` |
| ⑤ 六个主轴方向 | 用同一“局部 +Y 法线平面”经 3 组旋转映射到世界 ±X/±Y/±Z，逐方向断言 `worldNormal=(±1,0,0)/(0,±1,0)/(0,0,±1)` 且都能选中 `(30,14,30)`（12 条断言） |
| ⑥ Chunk 边界 + 非 identity Transform | `目标 Voxel 位于 Chunk 边界（(74,14,49) 属 Chunk (0,0,0)，+X 邻格属 (1,0,0)，+Z 邻格属 (0,0,1)）`；平移 + rotY 37° + rotZ 90° 下仍选中 `(74,14,49)` |
| ⑦ 挖掘后复验不得误挖相邻 Voxel | `挖掘后只有目标 Voxel 被编辑（相邻被挖 0，StoredVoxelCount=1）`；`挖掘后复验仍定位到同一 Voxel 并因已挖空而拒绝，未漂移到相邻 Voxel（复验目标=(30,14,30)，有效=False）` |
| ⑧ A 阶段 Air 误选回归 | T15 全部 31 条断言仍 PASS（见 6.3） |

### 6.2 Case 10（VoxelMining 套件，控制器级，10 条断言，全 PASS）

`夹具 height = 10.25 ⇒ Voxel(0,10,0) 中心密度 −0.25，实体表面 y = 10.25 位于该 Voxel 内部`：

```
PASS - Case 10: 部分实体 Voxel 初始为 Solid（未编辑）
PASS - Case 10: 用例成立 —— 目标 Voxel 中心基础密度 <= 0（-0.2500），旧判据会拒绝
PASS - Case 10: 测试选择（含命中点/法线）已注入
PASS - Case 10: 选择与高亮指向同一个最终目标 (0, 10, 0)
PASS - Case 10: 部分实体 Voxel 正常挖掘（F 返回成功）
PASS - Case 10: 目标 Voxel 变为 Air
PASS - Case 10: 只编辑目标 Voxel（相邻被挖 0，StoredVoxelCount=1）
PASS - Case 10: 挖掘后复验未漂移到相邻 Voxel（失效选择已清除）
PASS - Case 10: 挖掘后高亮不再停留在已挖空的目标上
PASS - Case 10: 再次 F 明确失败且不写任何 Voxel（不静默空操作、不误挖相邻）
```

### 6.3 既有回归（本轮相关套件）

| 套件 | 结果 |
|------|------|
| StageB3C（T1–T15 + 新增 T16） | `Checks = 264`，`Failures = 0`，exit=0（上一轮为 232 ⇒ +32） |
| VoxelMining（Case 1–9 + 新增 Case 10） | `Checks = 41`，`Failures = 0`，exit=0（上一轮为 31 ⇒ +10） |
| MeshIntegration / VoxelSelector / VoxelHighlight / StageB3A/B/B2 / V2_* 等 | 见第 7 节全量回归 |

---

## 7. 全量回归（32 套件）

见文末「回归判定」一节（执行中 → 完成后回填逐套件结果）。

---

## 8. 具体失败用例

**本轮新增测试：首次运行即全部通过**（B3C 264/0，VoxelMining 41/0），没有需要修复的新失败用例。

**上一轮（A 阶段）回归中曾出现的失败（供追溯，本轮的改动未引入新的失败）**：

| 套件 | 失败数 | 具体断言 | 原因与处理 |
|------|--------|----------|------------|
| MeshIntegration | 1 | `Case 11: TryMineVoxel writes VoxelField` | 既有套件用反射注入选择但**没有注入命中点/法线**（A 阶段要求挖掘前用命中数据复验）⇒ 已补齐注入数据（断言未改） |
| VoxelMining | 14 | `Case 1/2/3/4/5/6/7/9 …` | 同上，且该套件原夹具是**没有 Chunk/Density 的空壳地形**（无法提供材料证据）⇒ 夹具改为真实生成的 2×2×2 Chunk 平地；`Case 2`（旧契约“挖空后仍保有有效选择”）与 A 阶段要求冲突，改为断言“第二次 F 明确失败 + 选择被清除” |

**A 阶段 Air 误选回归**：T15 的 31 条断言（含“命中地表不得解析到空气侧 Voxel”“未编辑空气 Voxel 不可挖掘”“连续 10 次挖掘”）在本轮全部保持 PASS。

---

## 9. 尚未验证 / 明确未做

- **Editor Play Mode 未验证**：批处理断言无法覆盖真实鼠标 Raycast、手感与视觉表现；本轮结论不构成手感/观感验收（需在 Editor Play Mode 里用真实挖矿确认“小/部分实体 Voxel 能选中并且高亮落点正确”）。
- **高亮视觉**：Case 10 只断言 `IsVisible/CurrentVoxel` 与目标一致（以及挖空后隐藏），未做像素级/画面级确认。
- 未改动（按范围限制）：相机跳动（B 问题）、Weld 优化、Collider 分帧、FaceId/Visibility、Terrain Mesh 生成算法、Mining V1 其它逻辑、`VoxelField` 默认状态语义、坐标契约。
- 已知行为（沿用上一轮结论，未变）：一次成功挖掘后，同一条命中点复验会指向“已成 Air 的目标”⇒ 立即清除选择并隐藏高亮，下一帧由新的 Raycast 重新选中下降后的实体 Voxel（表现为“挖后高亮消失 1 帧”）。
- 未执行 `git add` / `commit` / `push`。
- 未验证项：真实场景（noise + height 15.7 + 75/37/50 分辨率）下的**大范围**逐个 Voxel 扫描式选择回归未做（本轮用等值面穿 Voxel 的解析用例 + 六方向 + 边界/变换覆盖，可视化扫描需 Play Mode）。

---

## 10. 完整 diff 与未跟踪清单

证据文件：`_phase2b_validation/partial_solid_git_diff.txt`，包含：

1. `git status --porcelain`（含 15 个跟踪修改）；
2. `git diff --stat`；
3. 空白校验：`git diff --check` exit=0、空白类发现 0；`git diff --cached --check` exit=0、暂存条目 0；
4. **完整 `git diff`（跟踪文件，含本轮 `DensityTerrain.cs` 与 B3C 验证套件的全部改动）**；
5. 本轮 4 个改动文件 + 1 个未改动引用文件的跟踪状态与 SHA256；
6. 主工程 ↔ 验证工程 `.cs` 同步校验：**160 对，0 不匹配，0 缺失**；
7. 未跟踪文件聚合（`Assets/` 84 个：39 `.cs` + 45 `.meta`；3 个未跟踪目录）；
8. **附录 A / B**：未跟踪且本轮改动的 `TerrainVoxelSelector.cs`、`TerrainVoxelMiningValidation.cs` **全文**（未跟踪文件正文不在 `git diff` 中，故直接附全文，便于逐行审阅）。

---

## 回归判定

**结论：全量回归 32/32 PASS —— 1641 条检查、0 失败、0 编译错误、全部 exit=0**（`_phase2b_validation/regression_summary.txt`；上一轮为 1599 条检查，本轮 +32（T16）+10（Case 10）= 1641）。

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
| StageB3C | 0 | 264 | 0 | 0 | PASS |
| MeshIntegration | 0 | 53 | 0 | 0 | PASS |
| VoxelMining | 0 | 41 | 0 | 0 | PASS |
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
| **合计（32 套件）** | **0** | **1641** | **0** | **0** | **32/32 PASS** |

本轮新增/变化的套件：`StageB3C` 232 → **264**（T16×32）、`VoxelMining` 31 → **41**（Case 10×10）；其余 30 个套件检查数与上一轮完全一致，全部 PASS。
