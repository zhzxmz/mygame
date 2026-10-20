# 只读诊断工具：Mining Surface Diagnostic（生产逻辑零改动）

- 工程：`D:\gamemaking\My project`（Unity 2022.3.62f1c1）
- 交付物：`Assets/Editor/MiningSurfaceDiagnosticValidation.cs`（**Editor Only，只读**）
- 本轮**未修改任何生产代码**：`git diff` 与上一轮记录的完整 diff **逐字节一致**（SHA256 `7DF0CB05…`，长度 475,157）——见证据文件第 3 节
- 证据：`_phase2b_validation/e_phase_git_diff.txt`（含工具全文 + 真实输出样例）、`e_diag2.log`

---

## 1. 工具用法

| 入口 | 用途 |
|------|------|
| 菜单 `Tools/Terrain/Diagnose Mining Surface (Scene Voxel)` | 诊断**当前场景**中所有 `DensityTerrain` 的 `TargetVoxel`（Play Mode 或 Edit Mode 均可直接点） |
| `MiningSurfaceDiagnosticValidation.RunBatch()` | 批处理：先自检诊断器，再在夹具上导出样例报告（`-executeMethod` 可直接调用） |
| `MiningSurfaceDiagnosticValidation.Diagnose(terrain, voxel)` | 代码 API：返回结构化报告对象（可断言、可序列化） |

可配置输入：

```csharp
public static Vector3Int TargetVoxel = new Vector3Int(30, 14, 30); // 诊断目标
public static int PartialSampleResolution = 4;                     // partial cell 子采样 4³
```

报告同时以 `Debug.Log(report.ToText())` 输出**人可读文本**，并返回 `MiningSurfaceDiagnosticReport`（结构化字段，便于自动断言/导出）：

```
================ Mining Surface Diagnostic ================
Voxel=(30, 14, 30)  VoxelSize=1  Chunk=(0, 0, 0)（ChunkExists=True）
VoxelField: State=Air（DefaultState=Solid，显式存储=True，EditedAir=True）
DensityField（基础密度，Voxel 中心 / 6 面内侧探针）:
  中心        = 0.5000
  +X  面内侧 = 0.5000   -X 面内侧 = 0.5000   +Y 面内侧 = 0.0010
  -Y  面内侧 = 0.9990   +Z 面内侧 = 0.5000   -Z 面内侧 = 0.5000
Partial cell: 实体样本 64/64（占比 1.000，失败 0，min=0.1250，max=0.8750）
材料证据: HasMaterialInVoxel=True  怀疑1=False  怀疑2=False
最近一次 Mutation: committed=True evaluationFailed=False reassembledChunks=1
面（6 主轴方向）:
  +X  FaceKey=0:31:14:30 boundary=True（-Solid=False +Solid=True） expectsSurface=True
      solidProbe=0.5000 meshStrict=True meshTolerant=True
      coplanarFragments=0（active=0，隐藏覆写=0，可见覆写=0） hiddenContribution=4（active=4，polyPoints=12）
      spatialFaceRecords=0 decision= evalFailed=False
  ...
Chunk 概览:
  (0, 0, 0) mesh=9555v/18755t faceFragments=15000 覆写(隐藏/可见)=4/0 hiddenContributionKeys=5 viewSync(True/True)
  ...
结论:
  · 未发现异常：Voxel (30, 14, 30) 的密度/材料/边界面/Mesh 覆盖一致。
```

（以上为 `e_diag2.log` 中的真实输出片段；完整样例见证据文件附录 B。）

---

## 2. 输出字段 ↔ 三类怀疑的对应关系

| 怀疑 | 报告中的字段 | 判定说明 |
|------|--------------|----------|
| **1. VoxelField=Solid 但 DensityField 是空气** | `VoxelFieldState`、`VoxelFieldExplicitlyStored`、`EditedAir`、`DensityAtCentre`、`FaceProbeDensities[6]`、`PartialSamplesSolid/Total`、`HasMaterialInVoxel`、`Suspicion1VoxelSolidButAir` | `Suspicion1VoxelSolidButAir = 未编辑(DefaultState 语义) && 状态=Solid && (中心 ∨ 6 面探针 ∨ 子采样) 全部 ≤ 0` ⇒ 该 Voxel 会被选择/挖掘逻辑当成实体，但基础密度场里是空气 |
| **2. Marching Cubes partial cell 未正确生成暴露面** | `PartialSamplesSolid/Total`、`PartialSolidRatio`、`PartialMinDensity`、`PartialMaxDensity`、`Suspicion2PartialCell`、以及每个面的 `ExpectsSurface` + `SurfaceFoundStrict/Tolerant` | `Suspicion2PartialCell = 0 < 实体样本数 < 总样本数`（等值面穿 Voxel）；配合面级判定即可回答“partial cell 的暴露面是否真的生成”。`PartialSolidRatio < 0.25` 会额外给出“少量实体”提示 |
| 3. 删除旧 Surface Fragment 后没有重新生成 Surface** | 每个面的 `CoplanarFragmentCount / ActiveFragmentCount / OverrideTrueCount / OverrideFalseCount`、`ContributionFragmentCount / ContributionActiveCount / ContributionPolygonPointCount`、`SpatialFaceRecordCount / LastDecision / LastEvaluationFailed / EvaluationFailureMessage`、`SurfaceFoundStrict/Tolerant` | 命中信号：`ExpectsSurface && !SurfaceFoundStrict`（Mesh 缺面）；或 `CoplanarFragmentCount>0 && ActiveFragmentCount==0 && ContributionFragmentCount==0`（旧片段已停用且无替代面）；或“旧面有共面 Fragment 但既无隐藏覆写、也无 HiddenFaceContribution”（旧表面可能仍可见） |

另有全局一致性信息：每个 Chunk 的 mesh 顶点/三角形数、Fragment 数、Visibility 覆写统计、`HiddenFaceContributions` key 数、`MeshFilter/MeshCollider` 是否等于 `Chunk.Mesh`（怀疑“MeshView 同步异常”），以及最近一次 Mutation 的 `committed / evaluationFailed / dirty / reassembled` 状态（用于捕捉“VoxelField 已写但 Mutation Abort”导致的数据/Mesh 分歧）。

---

## 3. 自检：证明该诊断器“能看见问题”（11/11 PASS）

诊断工具最大的风险是**假阴性**（永远报“无异常”）。因此 `RunBatch` 先跑自检与样例：

| 断言 | 作用 | 结果 |
|------|------|------|
| 自检：合成四边形所在平面找到面（**正控**） | 检测器确实能识别“有面” | PASS |
| 自检：1.0 外的平面不误报（**负控**） | 不会把远处几何当成该面 | PASS |
| 自检：面矩形外不误报（**负控**） | 切向范围判定有效 | PASS |
| 自检：错误轴向不误报（**负控**） | 轴向判定有效 | PASS |
| 样例(a)：表面之上的未编辑 Voxel 诊断为“Solid 但无材料” | 怀疑 1 的**检出能力** | PASS |
| 样例(a)：空气 Voxel 的 6 面都不应期待表面 | 期望值判定正确 | PASS |
| 样例(b)：表面之下的 Voxel 诊断为有材料（中心密度 1.5000） | 材料判定正确 | PASS |
| 样例(c)：挖掘后诊断到应有表面（5 个面） | 工具在挖掘后确实工作 | PASS |
| 样例(c)：挖掘后被挖 Voxel 邻域应有表面全部存在（缺失 0） | 当前 pipeline 一致性 | PASS |
| 样例(c)：被挖 Voxel 的 `+Y`（旧地表所在面）已被处理（**显式隐藏覆写=4**，HiddenFaceContribution=0） | 怀疑 3：旧表面确实被 `FaceFragmentVisibility=false` 覆写隐藏 | PASS |
| 样例(c)：被挖 Voxel 的 `-Y`（新暴露地板）由 **HiddenFaceContribution 生成（4 片段 / 12 多边形点）** | 怀疑 3：新表面确实由 Runtime Mutation 生成 | PASS |
| 样例(d)：噪声地形中找到 partial cell Voxel `(24,15,24)`（实体占比 0.047） | 怀疑 2 的**检出能力** | PASS |
| 样例(d)：partial cell 被正确标注 | 标注逻辑正确 | PASS |

工具定型说明：全量回归（35 套件 1723 条，0 失败）在 13 条断言版本上完成；随后只修正一处**输出标签**（非边界面不再显示为“<采样失败/出域>”，改为“<非边界（未采样）>”，纯文本无逻辑变化），并单独重跑该套件 13/13 PASS（`e_diag4.log`）。

正控的实现方式：构造一个临时 `TerrainChunk((0,0,0))` + 合成 1×1 四边形 Mesh（位于 y=5），用同一检测函数验证“平面上命中 / 平面外不命中”；用完 `chunk.Dispose()` + `DestroyImmediate(mesh)`。
> 注意：`TerrainChunk.ChunkOrigin` 由 Chunk 坐标推导（`ChunkSpace.ChunkToLocalOrigin`），自检必须用 `(0,0,0)` 号 Chunk 才能让合成 Mesh 的 chunk-local 顶点与 Terrain Local 原点对齐——这也是本轮唯一一处调试修正。

### 3.1 工具自身被诊断出来的两个 bug（已修）

第一次把工具接入回归时 `MiningDiagnostic` 套件 FAIL 2 条——排查后确认是**工具自身**的缺陷，不是 Terrain 问题：

1. **覆写计数语义反了**：`FaceFragmentVisibility` 是覆写层（`true=显式可见 / false=显式隐藏`），我最初把“值=true”记到了 `HiddenOverrideCount`。修正为 `OverrideTrueCount / OverrideFalseCount` 后，报告才能正确显示“+Y 面 4 个**隐藏**覆写”。
2. **同一 Chunk 被扫描两次**：当面的两侧 Voxel 属于同一 Chunk 时（`solidChunk == airChunk`），共面 Fragment 计数被翻倍。已加去重。

顺带得到一个**结构性事实**（已写进报告输出）：当等值面恰好落在 Voxel 平面上时（例如平地 `height=15` 的 y=15 平面），该面的基础 Fragment 通过 `VoxelFragmentIndex` 归属**相邻（空气侧）Voxel**，被挖 Voxel 自身“没有基础片段”。因此旧地表的移除**不是**靠“停用自身片段”，而是靠 `FaceFragmentVisibility=false` 覆写；工具的“自身基础片段”一栏现在会明确说明这一点（避免误判为“Fragment 丢失”）。

---

## 4. 只读保证

- 诊断路径只调用**只读 API**：`VoxelField.Get/IsAir/GetStoredCoordinates/DefaultState/VoxelSize`、`DensityTerrain.TryGetChunk/EffectiveChunkVoxelSize`、`VoxelAwareMeshRuntimeMutation.TrySampleBaseDensity`（静态只读采样）、`ChunkGeometry` 的只读遍历、`TerrainChunk.Mesh/ChunkOrigin/Coordinate`、`TerrainChunkView.MeshFilter/MeshCollider`、`VoxelAwareMeshRuntimeMutation.Last*` 诊断快照。
- **没有**任何 `VoxelField.Set` / `SetMesh` / `ReplaceAssembledMesh`（自检里的 `ReplaceAssembledMesh` 只作用于**新建的临时 Chunk**，与 Terrain 无关）/ Visibility 写入 / Mesh 修改 / 补面 / fallback。
- 未改动：`DensityField`、`MaterialField`、`VoxelField` 默认状态、`FaceId`、Visibility、Weld、Mesh 生成、Chunk 生命周期、Mining 生产逻辑。
- 机械证明：本轮 `git diff`（全部跟踪文件）SHA256 = `7DF0CB05AC21902939E7E9A89E455943AEAEF281B0C64D5B4C75365E3ADF8167`，与上一轮记录的同一 diff 完全一致 ⇒ **零生产改动**。

---

## 5. 修改文件列表（本轮）

| # | 文件 | 状态 | 说明 |
|---|------|------|------|
| 1 | `Assets/Editor/MiningSurfaceDiagnosticValidation.cs` | **新增**（未跟踪） | 只读诊断工具（13 条自检/样例断言；含正控/负控）；SHA256 `D90426D7A252B8055A296C793EF78F73DF251A164E2AB0245D772C91E59110B5` |
| 2 | `_phase2b_validation/run_regression.ps1` | 未跟踪（测试脚手架） | 注册 `MiningDiagnostic`，使其进入全量回归 |

生产代码改动：**0 个文件**。

---

## 6. 全量回归

**35/35 PASS —— 1723 条检查、0 失败、0 编译错误、全部 exit=0**（明细见文末「回归判定」）。

## 7. 尚未验证 / 明确未做

- **Editor Play Mode 未验证**：工具的 Scene 菜单入口需要在 Play Mode/Edit Mode 里人工指定 `TargetVoxel` 使用；本轮只在批处理夹具上验证了工具自身与样例判定。
- 工具只**报告**数据一致性，不判定“缺面根因”：真正的偶发缺面仍需要一份 Play Mode 复现坐标（把复现坐标填进 `TargetVoxel` 即可立刻得到该 Voxel 的完整表面状态）。
- 未做：不补面、不加 fallback mesh、不改 Visibility/FaceId/DensityField/VoxelField（本轮全部遵守）。

## 8. diff 与证据

证据文件 `_phase2b_validation/e_phase_git_diff.txt`：

1. `git status --porcelain`；
2. 本轮改动文件（含 SHA256）；
3. **生产代码零改动机械证明**（本轮 git diff SHA256 = 上一轮记录的 SHA256）；
4. 空白校验（`git diff --check` exit 0、暂存 0）；
5. 主工程 ↔ 验证工程 `.cs` 同步：**163 对，0 不匹配，0 缺失**；
6. 未跟踪聚合；
7. **附录 B**：诊断工具真实输出样例；
8. **附录 A**：`MiningSurfaceDiagnosticValidation.cs` 全文。

未执行 `git add` / `commit` / `push`。

---

## 回归判定

**结论：全量回归 35/35 PASS —— 1723 条检查、0 失败、0 编译错误、全部 exit=0**（`_phase2b_validation/regression_summary.txt`；上一轮 34 套件 1710 条，本轮新增 `MiningDiagnostic` 13 条 ⇒ 1723 条）。

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
| PlayerLook | 0 | 20 | 0 | 0 | PASS |
| MiningSurface | 0 | 49 | 0 | 0 | PASS |
| MiningDiagnostic | 0 | 13 | 0 | 0 | PASS |
| **合计（35 套件）** | **0** | **1723** | **0** | **0** | **35/35 PASS** |

**具体失败用例：0 条**（`MiningDiagnostic` 13/13、`MiningSurface` 49/49）。

过程记录：把工具接入回归的第一次运行中 `MiningDiagnostic` 曾 FAIL 2 条，经查为**工具自身**缺陷（覆写计数语义反向 + 同一 Chunk 重复扫描），修正后 13/13 通过；生产代码始终零改动。
