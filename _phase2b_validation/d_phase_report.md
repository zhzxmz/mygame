# Terrain Mining 后 Mesh 缺面问题：诊断报告（第一/二阶段）+ 修复决策

- 工程：`D:\gamemaking\My project`（Unity 2022.3.62f1c1）
- 范围：`Mining 修改 voxel → Terrain Runtime Rebuild → Mesh 更新 → Surface 缺失` 这一条链路
- 本轮**未修改任何生产逻辑**（原因见 §3 / §4）：诊断未能复现缺面，按任务规则“先证明问题、不得补洞、不得新增系统”，只交付**可复现的机械化诊断套件**与完整追踪报告
- **Editor Play Mode 未验证**（所有结论来自批处理断言 + 源码追踪）
- 证据：`_phase2b_validation/MiningSurfaceIntegrityValidation`（新套件）、`c_diag7.log`、`run_regression.ps1`、`d_phase_git_diff.txt`

---

## 1. 缺面“真实原因”：**诊断范围内未能复现**（13 类场景、49 条断言全部通过）

我按任务要求先做只读追踪、再做诊断测试，但**没有在 Voxel-Aware Mining Rebuild 链路上复现出 Missing Surface**。为避免“为了修复而修复”，这里给出可核查的结论与证据，而不是猜测式改动。

### 1.1 诊断方法（机械化，不靠肉眼）

对“应有表面”的定义（与 Terrain 数据一致，不用 Mesh 反推）：

```
对区域内每个 Voxel、每个主轴方向（±X/±Y/±Z）：
  两侧 Voxel 恰好一侧 Solid（VoxelField 编辑状态）
  且 Solid 一侧在基础 DensityField 中确有材料（面内侧 1e-3 采样 > 0）
  ⇒ 该面矩形范围内必须存在覆盖它的 Mesh 三角形（严格模式：轴向 ±0.02、切向半格；
     噪声模式：三角形切向包围盒覆盖面中心 + 轴向越过该面 ±0.45 Voxel）
  否则报告：Missing Surface Voxel:<v> Direction:<±d> Chunk:<c>
```

反向检查：被挖空 Voxel 内部不得残留三角形（旧表面未隐藏 ⇒ 也会被报告）。

**防“空扫描假通过”**：每次扫描统计“应有表面数”，断言必须 ≥ 阈值（实测最小 6~160 个面）。

### 1.2 覆盖到的场景与结果（`MiningSurfaceIntegrityValidation`，49 条断言 / 全 PASS）

| # | 场景 | 关键覆盖 | 结果 |
|---|------|----------|------|
| 1 | 单 Voxel 挖掘（平地） | 邻域 3×3×3 全方向扫描 + 挖空内部无残留 | 0 缺面 |
| 2 | Chunk 边界格 | x=0 / x=74 / z=0 / z=49 / y=0 边界格逐一挖掘 | 0 缺面 |
| 3 | 连续挖掘 10 次 | 每次挖掘后扫描 + 最终柱体区域 | 0 缺面 |
| 4 | 多 Chunk（2,1,2） | 其它 Chunk Mesh 不变 + Visibility 未越界 | 0 异常 |
| 5 | **完全埋藏 Voxel**（6 邻居全 Solid） | 6 面同时新生表面 | 0 缺面 |
| 6 | **噪声地形**（等值面穿 Voxel ⇒ 部分 Fragment） | 4 列表面挖掘 | 0 缺面 |
| 7 | **空腔相邻**（SphereCavity 下方/侧壁） | 3 次挖掘 | 0 缺面 |
| 8 | **挖空→恢复 Solid→再挖 3 轮** | 脏状态清理 / 残留 Visible=false | 0 缺面 |
| 9 | **表面 patch（2~3 个空气邻居的边/角）** | 4 步 patch | 0 缺面 |
| 10 | **跨 Chunk 连续挖掘**（74→75→…） | 边界两侧交替 | 0 缺面 |
| 11 | **MeshView 同步** | 每次挖掘后所有 Chunk 的 `MeshFilter/MeshCollider == Chunk.Mesh` | 0 异常 |
| 12 | **Chunk 边/角 + 噪声 + 对角 Chunk** | (74,49)/(75,50)/(75,49)… 6 次 | 0 缺面 |
| 13 | **同场景参数**（HeightField+noise, height=15.7, 75/37/50） | **25 次密集表面挖掘**，逐次扫描 + 终局扩大扫描（覆盖 160 个应有表面）+ View 同步 | 0 缺面 |
| — | 全局 | 每次挖掘 `LastMutationCommitted == true`（无 Evaluation Abort ⇒ VoxelField/Mesh 无分歧） | 0 Abort |

**因此：在本轮可构造的确定性场景里，Mining → Rebuild → Assemble → SetMesh 的表现与 Terrain 数据一致，没有缺面。**

---

## 2. 只读诊断：四个关键问题的追踪结论

### 2.1 问题 1：Mining 一个 Voxel 后影响范围

链路（`DensityTerrain.cs` / `VoxelAwareMeshRuntimeMutation.cs`）：

```
PlayerMiningController.TryMineVoxel
  → VoxelField.Set(voxel, Air)                      （调用方先写权威编辑状态）
  → DensityTerrain.RebuildChunksAffectedByVoxel      (L690)
  → ApplyVoxelAwareStateChangeAndSync                (L527)
  → VoxelAwareMeshRuntimeMutation.ApplyVoxelStateChange (L111)
        Phase 1 Evaluation：对该 Voxel 的 6 个 SpatialFace 逐一 EvaluateFaceForDirection
                            （Directions = ±X/±Y/±Z，L101-109、L202-212）
        Phase 2 Commit    ：RefreshVoxelFragments + CommitFaceEvaluation（写 Visibility/Contribution/Dirty）
        Phase 3 Assemble  ：ReassembleDirtyChunks → chunk.ReplaceAssembledMesh
  → SyncReassembledChunkViews → TerrainChunkView.SetMesh(MeshFilter + MeshCollider)
```

**受影响 Chunk 的构成（`dirty` 集合，L235-263 / L714-776）**：

```
dirty = { changedChunk }
      ∪ plan.DirtyCoordinates        // ownerCoordinate + meshOwner.Coordinate
      ∪ MarkDirty(EvaluationOwner)   // 面评估所有者
      ∪ MarkDirty(NegativeChunk)     // 面负侧 Voxel 所属 Chunk
      ∪ MarkDirty(PositiveChunk)     // 面正侧 Voxel 所属 Chunk
      ∪ MarkDirty(MeshOwner)         // 实体侧（隐藏面贡献）所属 Chunk
```

- `EvaluateFaceForDirection` 对**每个方向**都解析 `NegativeVoxel/PositiveVoxel` 并调用 `FindChunkContaining`，再用 `ResolveEvaluationOwner` 解析 `ownerCoordinate` ⇒ **±X/±Y/±Z 相邻 Chunk 会被加入 dirty**，而不是只重建“所在 Chunk”。
- 边界情况：`evaluationOwner == null`（拓扑边界）⇒ `plan.Skipped`，只保留 dirty 语义、不提交 Visibility/Contribution（L458-465）；`meshOwner == null` 时同样只 dirty owner（L538-546）。
- `LastDirtyChunks` / `LastReassembledChunks` 是可观测的（测试 4/11/13 均据实核对）。

**是否修改 affected Chunk 计算：未修改**（追踪显示它已包含六个主轴方向邻居；测试 2/10/12 在 Chunk 边界/边角/对角 Chunk 上验证了邻居 Chunk 的表面与 View 同步都正确）。

### 2.2 问题 2：FaceFragment 生命周期

- **基础共面 Fragment** 在 `VoxelAwareMeshPrecut.Build` 时一次生成，存在 `ChunkGeometry.FaceFragments`，并由 `VoxelFragmentIndex[voxel]` 反查“该 Voxel 拥有哪些片段”。
- 挖空时：`RefreshVoxelFragments(changedChunk.Geometry, voxel, active:false)`（L239-243 / L377-397）把**被挖 Voxel 自己的基础片段**置为非激活 ⇒ 旧地表被移除。
- **新暴露的表面不是基础片段**，而是由 `VoxelAwareMeshSpatialFace.Evaluate` + `DecideRegion` 在“未被共面基础几何覆盖、且实体侧基础密度 > 0”时生成的 `HiddenFaces`，在 Commit 阶段写入**实体侧（mesh owner）Chunk** 的 `Geometry.HiddenFaceContributions[face.Key]`（L746-759）。
- 旧数据清理：`CommitFaceEvaluation` 每次都先对 evaluationOwner / negative / positive 三个 Chunk 执行 `RemoveContribution(face)`（L718-721），再写入新值或删除 key ⇒ **不会累积、也不会残留**。
- “Density 已变但 FaceFragment 未创建”的唯一路径是 **Evaluation Abort**（Abort 时不提交、不 Assemble；`VoxelField` 不回滚，L297-308 与 L543-551）。本轮诊断对每一次挖掘都断言 `LastMutationCommitted == true`（全局断言），**未出现 Abort**。

**是否修改 FaceFragment 生命周期：未修改。**

### 2.3 问题 3：Visibility 生命周期

- `FaceFragmentVisibility` 是**覆写层**：无条目 = 默认可见（`Assemble` 只过滤显式 `false`）。
- 写入点唯一：`CommitFaceEvaluation`（L737-744），值来自 `plan.Visibility`；而 `plan.Visibility` 由 `BuildVisibilityTargets`（L1357-1453）在 **evaluationOwner / negativeChunk / positiveChunk / meshOwner** 四个 Chunk 内按 `FaceId.SameAs` 收集同面片段，用 `FaceFragmentSurfaceVisible(negativeMaterial, positiveMaterial, negativeSolid, positiveSolid)`（L996-1006，B3-E2 材料边界规则）判定。
- 这 4 个 Chunk **恰好就是** `CommitFaceEvaluation` 里 `MarkDirty` 的 4 个（L727-730）⇒ 凡收到 Visibility 写入的 Chunk 都会重新 Assemble ⇒ 不存在“写了可见性但没重建 Mesh”的窗口。
- 本轮诊断的“Visibility 未越界写入其它 Chunk”断言（测试 4）验证了无关 Chunk 不会长出隐藏覆写。

**是否修改 Visibility：未修改。**

### 2.4 问题 4：Assemble

- `ReassembleDirtyChunks`（L310-334）对每个 dirty Chunk 调 `VoxelAwareMeshPrecut.Assemble(chunk.Geometry)` → `chunk.ReplaceAssembledMesh` → `SyncChunkMesh`（MeshFilter + MeshCollider）。
- 诊断证据：所有场景在**每次挖掘后**都核对“assembled mesh 是否表达了应有的 Solid-Air 边界”（§1.1）与“所有 Chunk 的 MeshFilter/MeshCollider 是否等于 `Chunk.Mesh`”（测试 11/13）⇒ 在当前场景集合内 Assemble 未丢数据。
- 因此没有依据去修改 Assemble（任务明确要求：若 FaceFragment 正确而 Mesh 错误才考虑 Assemble）。

**是否修改 Assemble：未修改。**

---

## 3. 修改文件列表（本轮）

| # | 文件 | 状态 | 说明 |
|---|------|------|------|
| 1 | `Assets/Editor/MiningSurfaceIntegrityValidation.cs` | **新增**（未跟踪） | 诊断/回归套件：13 类场景、49 条断言，含 Missing Surface / Stale Surface / View 同步 / Abort 检查与“扫描覆盖数”防假阳性守卫 |
| 2 | `_phase2b_validation/run_regression.ps1` | 未跟踪（测试脚手架） | 注册新套件 `MiningSurface`，使其进入全量回归 |

**生产代码改动：0 个文件。**（`DensityTerrain.cs`、`TerrainChunk.cs`、`VoxelAwareMeshRuntimeMutation.cs`、`VoxelAwareMeshPrecut.cs`、`VoxelAwareMeshSpatialFace.cs`、`PlayerMiningController.cs` 等在本轮均未改动。）

## 4. 为什么没有修改生产逻辑（修复决策）

按任务的三条硬约束判断：

1. **“先证明问题”**：13 类场景（含场景同参 25 次密集挖掘）都未出现 Missing Surface，也未出现 VoxelField/Mesh 分歧 ⇒ 无法在可复现范围内指认“affected Chunk 不完整 / FaceFragment 漏生成 / Visibility 判定错误 / Assemble 丢数据”中的任何一条。
2. **“禁止直接补洞”**：没有复现证据就补面/补 Quad，会破坏 `Mesh == Density` 的一致性契约，并给 Mining / Collider / Visibility 留下更隐蔽的偏差。
3. **“禁止新增系统/Registry”**：在没有定位到具体缺陷前新增缓存或二次校验层，属于用新状态掩盖未知偏差。

因此本轮只新增“可重复执行的证据产生器”（诊断套件），并把结论与残余假设交回给你决策。

## 5. 残余假设（未证实，需要 Play Mode / 真实日志确认）

| # | 假设 | 现有代码里的证据 | 如何确认 |
|---|------|------------------|----------|
| H1 | **Evaluation Abort**：`VoxelField` 已写 Air 但 Mutation 评估失败 ⇒ Mesh 未更新（代码注释明确“VoxelField 不回滚”） | `DensityTerrain` 在 Abort 时打印 `[DensityTerrain] Voxel-Aware Mutation 未提交，Voxel=… 原因=…`；`ApplyVoxelAwareStateChangeAndSync` 返回 0 | 查 Player.log 是否有该 Warning / `BASE DENSITY SAMPLE DOMAIN GAP`；Play Mode 里在挖掘后打印 `VoxelAwareMeshRuntimeMutation.LastEvaluationFailureMessage` |
| H2 | 真实 Raycast 选择会挖到我未覆盖的 Voxel 序列（例如站在洞里挖 ⇒ 4~5 个空气邻居、或贴地形外边界） | 本轮覆盖到 3 个空气邻居（测试 9）与外边界（测试 2/10/12） | Play Mode 复现录制：记录每次 `VoxelField.Set` 的坐标序列，再用本套件的扫描逻辑离线核对 |
| H3 | 卡顿帧内 Collider 与 Mesh 短暂不一致，视觉上像“穿透” | 测试 11/13 断言每次挖掘后 View/Collider 已同步（同一帧内） | Play Mode 观察 + 在挖掘后立即 `Physics.Raycast` 验证 Collider 表面 |
| H4 | 缺面来自 Mining 链路之外（其它脚本改 Mesh / 破坏物体） | 场景内 `RayDestory` 只销毁自身 GameObject；未发现其它改 Terrain Mesh 的运行时脚本 | Play Mode 复现时抓 `Mesh.vertices` 变化来源（或在 `TerrainChunkView.SetMesh` 加一次性日志） |

**建议的下一步（需要你提供其一）**：一段 Play Mode 复现录像/坐标记录，或出现缺面时的 Player.log（重点看 `[DensityTerrain]` / `Voxel-Aware Mutation 未提交` / `BASE DENSITY SAMPLE DOMAIN GAP`）。有了它，我可以直接把复现坐标喂给本套件，把“偶发”变成“必现”，再按方案 A/B/C 做最小修复。

---

## 6. 新增测试（MiningSurfaceIntegrityValidation）

- 入口：`-batchmode -nographics -quit -projectPath <validationProject> -executeMethod MiningSurfaceIntegrityValidation.RunBatch`
- 覆盖（任务要求的 4 项 + 扩展 9 项）：单 Voxel / Chunk 边界 / 连续 10 次 / 多 Chunk(2,1,2) / 完全埋藏 / 噪声部分 Fragment / 空腔相邻 / 挖空-恢复循环 / 表面 patch 边角 / 跨 Chunk 序列 / MeshView 同步 / Chunk 边角+对角 Chunk+噪声 / **场景同参 25 次密集挖掘 + 终局扩大扫描** / 全局 Abort 检查。
- 报告格式（缺面时可直接定位）：

```
Missing Surface Voxel:(30, 14, 30) Direction:+Y Chunk:(0, 0, 0)
Stale Surface  Voxel:(30, 14, 30) Chunk:(0, 0, 0)
```

- 防假阳性：每次扫描都断言“应有表面数 ≥ 阈值”（实测最小 6，终局扫描 160）。

## 7. 全量回归

**34/34 PASS —— 1710 条检查、0 失败、0 编译错误、全部 exit=0**（明细见文末「回归判定」）。

## 8. git diff

- 证据文件：`_phase2b_validation/d_phase_git_diff.txt`
- **生产代码 diff：无**（本轮零生产改动；文件内 `git diff` 只包含此前各轮已存在的修改，用于确认本轮未触碰任何生产文件）
- 未跟踪新增：`Assets/Editor/MiningSurfaceIntegrityValidation.cs`（全文附在证据文件附录 A）
- 校验：`git diff --check` / `--cached --check` 均 exit 0；主↔验证工程 `.cs` 逐文件 SHA256 比对 0 不匹配

## 9. 尚未验证 / 明确未做

- **Editor Play Mode 未验证**：真实鼠标 Raycast 挖掘的坐标序列、卡顿帧内的实际表现、以及 H1~H4 任一假设，都需要 Play Mode 或你的日志才能确诊。
- 未修改：DensityField / MaterialField 语义、VoxelField 默认状态、Chunk/Voxel 坐标、FaceId Absolute UV、Terrain Local Space、Marching Cubes 逻辑、Visibility / Weld / Collider / Chunk 生命周期（本轮全部零改动）。
- 未实现任何新玩法系统（Inventory/Item/Creature/Tool/多阶段 Mining 均未触碰）。
- 未 `git add` / `commit` / `push`。

---

## 回归判定

**结论：全量回归 34/34 PASS —— 1710 条检查、0 失败、0 编译错误、全部 exit=0**（`_phase2b_validation/regression_summary.txt`；上一轮 33 套件 1661 条，本轮新增 `MiningSurface` 49 条 ⇒ 1710 条）。

**任务点名的既有验收套件全部通过**：
StageB1=51，StageB2=317，StageB3A=52，StageB3B=106，StageB3C=264，MeshIntegration=53，VoxelMining=41，VoxelSelector=43，VoxelDensityAdapter=22，VoxelMarchingCubes=29，MiningV1=0
。

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
| **合计（34 套件）** | **0** | **1710** | **0** | **0** | **34/34 PASS** |

**具体失败用例：0 条**（含新增诊断套件 49 条；无任何套件失败）。
