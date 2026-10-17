# Voxel World Runtime Specification V1

> 适用范围：`Assets/Scripts/VoxelWorld/`（VoxelCore + Generation + Mesh + Mining + Runtime）体系。
> 本规范只定义 **运行时驱动（Runtime）**：把「生成 → 数据编辑 → 派生表示」这条链路在游戏运行期间
> 持续、按预算、可加载/可卸载地跑起来。**不涉及**既有 Terrain 系统
> （`TerrainModificationV2` / `DensityField` / Marching Cubes 地形）——那套运行时与本文档无关。
>
> 上游规范：
> * `Specifications/World/VoxelWorld_Core.md` —— 世界真值（Voxel Data）与 Chunk 数据契约；
> * `Specifications/World/WorldGeneration_Core.md` —— 初始状态生成（Seed + ChunkCoordinate → VoxelChunk）；
> * `Specifications/World/VoxelWorld_Mesh.md` —— 派生表示（Mesh 生成 / 生命周期 / Dirty·Rebuild / 验收不变量）；
> * `Specifications/World/VoxelWorld_Mining.md` —— 世界数据修改（Mining 流程 / 脏规则 / 失败语义）。
>
> 冲突时的优先级：**真值与数据契约** 以 `VoxelWorld_Core.md` 为准；**修改规则** 以 `VoxelWorld_Mining.md` 为准；
> **表示规则** 以 `VoxelWorld_Mesh.md` 为准；**调度、生命周期、流式加载** 以本规范为准。
>
> 本规范**只做调度与生命周期**：不新增世界规则、不重定义生成或挖掘语义、不改变任何上游不变量。
> 上游扩展不得要求修改 VoxelCore（WorldGeneration_Core §10）。

---

## 1. Core Principles（不可协商条款）

1. **Runtime 是调度者，不是世界规则的拥有者。**
   * Runtime 决定「何时 / 按什么顺序 / 每帧做多少」：加载、生成、重建、上传、卸载；
   * Runtime **不定义**「体素长什么样、住在哪里」——那是 Generation / Mining / Mesh 的规则；
   * 任何「在运行时顺手算一下地形 / 顺手改一下体素」的写法都视为越界（禁止第二套生成或修改逻辑）。
2. **数据先行（Data First）：表示永远不超前于数据。**
   * 顺序恒为 **数据 → Mesh 缓冲 → 呈现 / 碰撞**；
   * 不存在「已经能看到实体，但数据层还没有这个 Chunk」的状态（VoxelWorld_Core §1：Mesh 不是世界状态）。
3. **表示可丢弃（Disposable Representation）。**
   * Mesh / Collider / GameObject 全部可随时释放；释放不改变世界数据（VoxelWorld_Core §1）；
   * 释放后按 `Seed + 改动集` 重建 ⇒ 与释放前**逐元素一致**（Mesh 规范 I12）。
4. **收敛确定性（与上游同级约束）。**
   * 给定 `Seed + RuntimeSettings + 改动序列`，**任意**帧预算 / 加载顺序 / 初始已加载集合
     ⇒ 收敛后（全部 Chunk `Ready`）的世界数据与每个 Chunk 的 Mesh 完全一致；
   * **禁止** `Time` / 帧序 / 帧率 / `UnityEngine.Random` / 字典遍历顺序影响世界数据与 Meshing 结果
     （WorldGeneration_Core §2、Mesh 规范 §1.3）。
5. **焦点只影响「加载什么」，不影响「生成什么」。**
   * 相机 / 玩家位置只用于求解 Chunk 集合与优先级（§6）；
   * **不得**进入 `HeightFunction`、不得进入 Meshing、不得进入 `SetVoxel` 的语义。
6. **单一提交路径。**
   * 世界数据的唯一写入口是 `VoxelWorld.SetVoxel`（经 Mining 流程，Mining §2.1）；
   * 表示的唯一来源是 `VoxelMeshGenerator.Generate(chunk)`；
   * 禁止第二套真值（影子方块表、私有网格副本、额外脏表、缓存的世界状态）。
7. **模块边界冻结。**
   * VoxelCore / Generation / Mesh 为**纯逻辑层**：不出现 `MonoBehaviour` / `GameObject` / `Transform` /
     `Mesh` / `MeshFilter` / `MeshCollider` / `Time`（`Mesh` 层只允许 `Vector2` / `Vector3` 数学结构体）；
   * Unity 运行时类型**只允许**出现在 Runtime 的 **View 子层**（§2）；
   * 本规范不得要求为了 Runtime 而修改 VoxelCore / Generation / Mesh 的既有契约。

---

## 2. 分层与所有权（Layer Boundary & Ownership）

```
玩法层（输入意图 / UI / 音效）
   │  VoxelCoordinate（"改哪里"）                  ← 查询层解析（§8.3），Runtime 只消费坐标
   ▼
VoxelWorld.Runtime · Core（纯 C#：集合求解 / 生命周期 / 队列 / 预算 / 改动集）
   │                                   │
   │ 读/写 Voxel 数据（唯一入口）        │ 产出 VoxelMeshData（纯数据，可丢弃）
   ▼                                   ▼
VoxelWorld（世界真值）          VoxelWorld.Runtime · View（Unity：Mesh / MeshFilter / MeshCollider / GameObject）
   ▲
   │ Seed + ChunkCoordinate → VoxelChunk
VoxelWorld.Generation
```

Runtime 内部**必须**再分成两个子层，禁止混合：

| 子层 | 职责 | 允许依赖 | 必须禁止 |
|---|---|---|---|
| **Runtime Core**（调度核心） | 焦点→集合求解、Chunk 生命周期、队列与预算、脏排空、改动集 | VoxelCore / Generation / Mesh 的纯 C# 类型 | `MonoBehaviour` / `GameObject` / `Transform` / `Mesh` / `Collider` / `Time` / `Camera` / `Physics` |
| **Runtime View**（表示层） | `GameObject` 生命周期、`Mesh` 资产创建/销毁/上传、`MeshFilter` / `MeshRenderer` / `MeshCollider`、坐标摆放 | Unity 运行时 API + Runtime Core 的输出 | 世界规则（生成/挖掘算法）、直接写 Voxel 数据、缓存「另一份」世界状态 |

> **目的**：调度核心必须能脱离 Unity 场景做**批处理自动化验证**（与既有
> `Assets/Editor/*Validation.cs` + `Tools/VoxelWorld/*` 的验证风格一致）。
> 若 Runtime Core 无法在纯 C# 下被测试，说明依赖画错了，必须先修依赖再实现。

### 2.1 所有权表

| 资产 | 所有者 | 生命周期 |
|---|---|---|
| `VoxelChunk` 数据 | `VoxelWorld`（Runtime Core 间接持有） | 加载时创建；卸载时释放（改动已入改动集） |
| 脏标记 `chunk.IsDirty` | VoxelCore 置位（数据实际变化）；**构建+上传成功后**由调度方清除 | 见 §4 / §5 |
| `VoxelMeshData` 缓冲 | Runtime Core 的构建步骤（纯数据） | 单次构建产物；上传后即可丢弃 |
| `UnityEngine.Mesh` / `MeshCollider` | Runtime View | 与 Chunk 的加载期绑定；卸载 / 重载必须成对释放 |
| Chunk 容器 `GameObject` | Runtime View | 同上 |
| 改动集（`VoxelCoordinate → VoxelType`） | Runtime Core | 跨卸载 / 跨存档存活（§7） |
| 存档 | 存档层（本规范只定义**内容契约**，§7.1） | `Seed + RuntimeSettings + 改动集` |

---

## 3. Runtime 配置（显式参数，无隐藏全局状态）

与 WorldGeneration_Core §3 同级要求：**所有影响调度或世界结果的参数都必须显式**。
禁止从 `Camera.main`、`FindObjectOfType`、静态单例、`PlayerPrefs`、`Time` 隐式取值。

| 参数 | 含义 | V1 约束 |
|---|---|---|
| `Seed` | 世界种子（传递给 Generation） | 必填，显式 |
| `GenerationSettings` | `WorldGenerationSettings`（地形参数） | 必填，显式（`IsValid(out reason)`） |
| `Focus` | 焦点（相机 / 玩家）的**世界位置**提供者 | 显式引用；不得隐式取 `Camera.main` |
| `ViewDistanceChunks`（记作 `R_load`） | 水平加载半径（Chunk 单位） | 整数，`≥ 0` |
| `UnloadHysteresisChunks` | 卸载滞回，`R_unload = R_load + Hysteresis` | 整数，建议 `≥ 1` |
| `VerticalMode` | 垂直加载策略：`Full`（整列，V1 默认）/ `FocusRange`（后续） | 必须显式选择 |
| `MaxChunksGeneratedPerFrame` | 每帧生成上限 | `≥ 1` |
| `MaxMeshesBuiltPerFrame` | 每帧 Mesh 构建上限 | `≥ 1` |
| `MaxUploadsPerFrame` | 每帧表示上传 / 替换上限 | `≥ 1` |
| `MaxUnloadsPerFrame` | 每帧卸载上限 | `≥ 1` |
| `MaxPendingEdits` | 编辑请求排队上限（§5.3） | 显式上限，超出必须可观测 |

约束：

1. `ChunkSize = 16` 固定（VoxelCore），**不允许**在 Runtime 覆盖；
2. 垂直 Chunk 数由显式参数推导：`chunkCountY = ceil(WorldHeight / ChunkSize)`
   （`WorldHeight` 来自 `GenerationSettings`）；V1 **不加载、不生成** `chunkY ≥ chunkCountY` 的区域；
3. 参数变更 ⇒ 视为**新世界**：禁止在既有已加载 Chunk 上「热改」生成参数后继续沿用旧数据；
4. 参数必须可序列化（存档记录 `Seed + 参数快照 + 改动集`，§7.1）；
5. 参数非法（`R_load < 0`、任一上限 `≤ 0`、`WorldHeight ≤ 0` 等）⇒ **启动前拒绝**，
   禁止运行中静默修正或静默截断。

---

## 4. Chunk 生命周期状态机

一个 Chunk 在 Runtime 中恰好处于以下状态之一：

| 状态 | 含义 | 进入条件 | 离开条件 |
|---|---|---|---|
| `Absent` | 未加载：无数据、无表示 | 初始；卸载完成 | 被加载集合选中 → `Generating` |
| `Generating` | 已分配 Chunk，正在生成数据（数据未就绪） | 开始加载 | 成功 → `Dirty`；抛错 → `Failed` |
| `Dirty` | 数据就绪且 `IsDirty == true`（表示缺失或过期） | 生成成功；`SetVoxel` 实际变化；刚加载 | 构建 + 上传成功 → `Ready` |
| `Building` | 正在用当前数据构建 Mesh（**禁止呈现半成品**） | 从 `Dirty` 调度构建 | 上传成功 → `Ready`；中断 / 失败 → 回 `Dirty` |
| `Ready` | 表示与当前数据一致（`IsDirty == false`） | 构建 + 上传成功 | 数据变化 → `Dirty`；超出 `R_unload` → `Unloading` |
| `Unloading` | 正在释放表示与数据（改动先入改动集） | 超出 `R_unload` | 释放完成 → `Absent` |
| `Failed` | 生成 / 构建失败，保留现场以便重试 | 生成或构建抛错 | 显式重试 → `Dirty`（**必须**保留脏语义） |

规则：

1. **`Ready` 的定义是可验证的**：呈现缓冲 == 用**当前** `VoxelChunk` 数据重新 Meshing 的结果
   （§10 R2）。「看起来对」不算通过。
2. **原子替换**：`Building → Ready` 必须是单步提交；任何中断都不得留下「部分顶点已上传 / 半成品被呈现」。
3. **失败保持脏**：生成 / 构建 / 上传失败**不得** `ClearDirty`
   （Mesh 规范 §6.3、Mining §5.3）。
4. **禁止跳过 `Dirty`**：数据变化后必须先回到 `Dirty`，不允许从 `Ready` 直接再进 `Ready`
   （那等于承认存在过期表示）。
5. **与 VoxelCore 的投影关系（双向断言）**：
   `Ready ⟺ !chunk.IsDirty`；`Dirty` / `Building` 期间 `chunk.IsDirty == true`
   （成功上传后才清脏）。
   禁止出现「`IsDirty == true` 但状态 `Ready`」或「`IsDirty == false` 但状态 `Dirty`」。
6. **`Failed` 不等于放弃**：进入 `Failed` 的 Chunk 仍是世界的一部分（数据可能是完整的），
   必须可重试且重试结果与成功路径一致。

---

## 5. 每帧调度管线（Frame Pipeline）

`Tick(deltaTime, focusWorldPosition)` 的**固定阶段序**（顺序不可交换）：

```
A. 焦点解析     focusWorldPosition → focusChunk = (FloorDiv(x,16), FloorDiv(y,16), FloorDiv(z,16))
B. 集合求解     desiredLoad（R_load 内）/ unloadCandidates（R_unload 外），按固定键排序（§5.1）
C. 卸载         表示释放 → 数据释放（改动必须先入改动集）
D. 加载 / 生成  生成数据 → 叠加改动集 → 状态 = Dirty
E. 编辑提交     Mining 请求 → 数据写入 + 脏标记（只影响 owning Chunk）
F. Mesh 构建    对已加载且 Dirty 的 Chunk：Generate(chunk) → 缓冲（纯数据）
G. 表示提交     原子替换 Mesh / Collider → ClearDirty → 状态 = Ready
H. 诊断         计数与耗时（只读）
```

约束：

1. **C 先于 D**：先释放再申请，避免瞬时「双份」占用（内存与对象数不得出现尖峰翻倍）。
2. **D 先于 E**：本帧新加载的 Chunk 已经「生成 + 叠加改动」，因此同帧编辑不会覆盖历史改动；
   概括为：**生成永远发生在改动之前**（WorldGeneration_Core §9：生成不覆盖玩家改动）。
3. **F / G 只在数据就绪后进行**：禁止对 `Generating` 中的 Chunk 构建 Mesh
   （否则会用空数据产出空 Mesh 并清脏 ⇒ 永久缺面）。
4. **帧内幂等**：同一帧重复调用 `Tick` 不得产生额外的生成 / 构建（状态与脏集合在帧内收敛后即稳定）；
   `Tick` 的**结果**不依赖 `deltaTime`——`deltaTime` 只能用于预算分摊与统计（§10 R14）。
5. **每帧上限**：C / D / F / G 各自受 §3 上限约束；未处理项**保留**在队列（不得丢弃、不得跳过置脏）。
6. **数据写入的原子粒度**：单个 Chunk 的生成是原子的、单个 Voxel 的写入是原子的；
   只有「多个 Chunk 的批量工作」才允许分帧。

### 5.1 确定性排序

* 加载候选：按 `(水平距离², chunkX, chunkY, chunkZ)` **升序**（近处优先；键为全序 ⇒ 无并列歧义）；
* 卸载候选：按 `(水平距离², chunkX, chunkY, chunkZ)` **降序**（最远先卸载）；
* 脏 Chunk 重建：使用 `VoxelWorld.CollectDirtyChunks`（VoxelCore 已按 Chunk 坐标排序）；
* **禁止**把 `Dictionary` 遍历顺序、`GetHashCode`、对象引用地址、帧序当作排序键（§10 R4 / R14）。

### 5.2 分帧与预算

* 预算单位 = **条目数**（V1）；毫秒预算属于后续优化（§11）；
* 分帧**不得**改变最终几何：任意预算下的收敛结果必须逐字节一致（§10 R4）；
* 未在预算内处理的脏 Chunk 保持 `Dirty`（下次继续），不得静默丢弃。

### 5.3 编辑请求与加载状态（Mining 的运行时前置条件）

Mining §3.5 规定：对**未生成**坐标的挖掘**不创建 Chunk、不置脏**（当前值即 Air ⇒ 无变化）。
因此 Runtime **必须先保证目标 Chunk 已加载**，否则玩家的编辑会被静默吞掉。三态处理：

| 目标 Chunk 状态 | Runtime 行为 | 可观测结果 |
|---|---|---|
| 已加载（`Dirty` / `Building` / `Ready`） | 立即提交 Mining（数据 + 脏） | `Mined` / `Unchanged` / `Rejected`（Mining §5） |
| 未加载，但在 `R_load` 内 | **排队**（`Pending`），该 Chunk 加载完成后的后续 `Tick` 重投 | `Pending`（计数可见；**禁止**当作 `Unchanged` 丢弃） |
| 未加载，且在 `R_load` 外 | 显式策略二选一：`DemandLoad`（为该请求额外加载）或 `Rejected(OutOfLoadRange)` | 必须返回原因 + 计数（禁止静默） |

* 排队上限 = `MaxPendingEdits`（§3）；超出 ⇒ `Rejected(reason)` + 计数；
* 排队请求按**提交序号 FIFO** 提交（保证同一帧内多次编辑的先后语义确定）；
* 排队请求在目标 Chunk 变为 `Ready` 后仍必须提交（不允许因「已经重建过」而丢弃）。

---

## 6. 流式加载与卸载（Streaming）

### 6.1 加载集合判定式（可独立重算）

设 `focusChunk = (fx, fy, fz)`（floor 除法，负坐标同样正确；VoxelCore `FloorDiv` 语义），
Chunk `c` 的水平偏移 `dx = c.x − fx`、`dz = c.z − fz`：

```
期望加载集合 = { c | dx² + dz² ≤ R_load²   ∧   c.y ∈ 垂直范围 }
垂直范围（V1，VerticalMode.Full）= [0, chunkCountY)，chunkCountY = ceil(WorldHeight / 16)

保留集合     = { c | dx² + dz² ≤ R_unload² }，R_unload = R_load + Hysteresis
```

* 落入「期望加载」⇒ 需要加载（若当前 `Absent`）；
* 超出「保留集合」⇒ 需要卸载（若当前已加载）；
* **两者之间 ⇒ 保持现状**（不加载、不卸载）。这正是滞回的全部意义：
  焦点在边界抖动时**不得**产生加载 / 卸载抖动（避免反复重建与反复分配 `Mesh`）。

### 6.2 加载顺序与优先级

1. 期望加载集合内按 §5.1 排序（最近优先）；
2. 每帧最多加载 `MaxChunksGeneratedPerFrame` 个；
3. 加载 = `Generate` + 叠加该 Chunk 的改动集（§7.2）；
   结果必须与「从未卸载过」完全一致（§10 R8）。

### 6.3 卸载顺序与释放

1. 最远优先（§5.1 降序），每帧最多 `MaxUnloadsPerFrame` 个；
2. **卸载前必须先把该 Chunk 的改动写入改动集**（若有），否则玩家的挖掘/建造会丢失；
3. 释放顺序：表示（`Mesh` 资产 / `MeshCollider` / `GameObject`）→ 数据（Chunk）；
4. 卸载**不得改变世界语义**：同一坐标在卸载前后读到的 Voxel 值必须相同（由改动集保证）。

### 6.4 与「无限世界」的关系

* 水平方向无边界（`VoxelCoordinate` 为整数、floor 除法对负数正确）⇒ 支持无限扩展；
* 垂直方向 V1 有界受 `WorldHeight` 约束：越界读为 Air（VoxelCore 语义）、越界写应被拒绝
  （Mining §5：非法输入 ⇒ `Rejected`）；
* `chunkCountY` 之外**不加载、不生成、不呈现**（不产生「世界下方的假地形」）。

---

## 7. 运行时与存档边界（改动集 / 重载重建）

### 7.1 可持久化状态 = 最小集

```
存档内容 = Seed + RuntimeSettings 快照 + 改动集
改动集条目 = (VoxelCoordinate, VoxelType)      // 只记录"与生成结果不同"的 Voxel（稀疏）
```

* **不保存**：未改动部分的 `VoxelChunk` 全量数据（可由 Seed 重新生成）、`Mesh`、顶点 / 索引缓冲、
  `MeshCollider`、`GameObject` 与场景层级、脏标记、加载集合，以及任何表示层状态；
* 与 VoxelWorld_Core §6（`seed + changed voxel data`、`Do not save mesh`）和
  WorldGeneration_Core §9 完全一致；
* 本规范只定义**内容契约**；字节布局 / 文件格式 / 压缩 / 版本迁移属于存档层（§11）。

### 7.2 重载 = 生成 + 叠加改动

```
chunk = Generate(chunkCoordinate)               // 确定性：同 Seed + 同参数 ⇒ 同结果
ApplyChanges(chunk, 改动集中属于该 Chunk 的条目)   // 覆盖生成结果（在生成之后）
chunk.MarkDirty()                               // 叠加改动后必须为 Dirty（等待重建表示）
```

规则：

1. **叠加顺序无关**：改动集对同一坐标**至多一条**（同一坐标的多次编辑只保留最终状态）；
2. **生成不覆盖改动**：`ApplyChanges` 必须在 `Generate` **之后**执行（WorldGeneration_Core §9）；
3. **未加载 Chunk 的改动**：改动集里属于未加载 Chunk 的条目只在该 Chunk 被加载时叠加；
   **不得**为了叠加而提前加载（加载集合只由 §6.1 决定）；
4. **无历史**：改动集按「最终值」覆盖，不追加编辑历史（撤销 / 重做属于独立系统，§11）；
5. **改动集与真值一致**：任一时刻，「生成结果 + 改动集」必须等于当前世界的 Voxel 真值；
   可验证方式 = 全量 Chunk 快照 vs 「重新生成 + 叠加改动」的结果比对（§10 R9 / R10）。

---

## 8. 表示层契约（呈现 / 碰撞 / 输入解析）

### 8.1 呈现（Runtime View 的职责）

1. **输入**：Runtime Core 产出的 `VoxelMeshData`（纯数据）+ Chunk 坐标；不得自行生成几何；
2. **变换**：`localToWorld = chunkCoordinate * ChunkSize`（整数平移，**禁止缩放 / 旋转 / 非整数位移**，
   否则会出现面缝或重叠）；
3. **资产配对**：每个已呈现 Chunk 恰好 1 个 `Mesh` + 1 个 `MeshFilter`（可选 1 个 `MeshCollider`）；
   释放必须成对，禁止泄漏（§10 R7）；
4. **空几何**：`VoxelMeshData.HasGeometry == false` ⇒ 允许不创建表示或直接释放表示；
   但**仍必须**按成功路径处理脏标记（数据确实已一致），且不得因此改变世界数据；
5. **原子替换**：先准备好完整缓冲 → 一次性赋给 `Mesh`（或新建资产后整体替换旧资产）→ 成功后再
   `ClearDirty` 并切到 `Ready`；**禁止**在 View 里做几何优化（剔除 / 合并 / 顶点焊接），
   那属于 Mesh 层且必须保持不变量（Mesh 规范 §8）。

### 8.2 碰撞

* `MeshCollider` 与呈现 `Mesh` **同源**（同一缓冲），属于派生表示，**不是**世界真值；
* 任何 gameplay 判定（「这个位置能不能站 / 能不能挖」）必须走数据层
  `VoxelWorld.GetVoxel` / `TryGetVoxel`，不得以 Collider 存在性或 `Physics.Raycast` 命中作为世界状态；
* Collider 的构建 / 更新可独立调度（允许滞后于呈现），但**不得**参与 §4 的状态一致性判定。

### 8.3 输入解析（查询层，独立规范）

* 「射线 / 交互 → 目标 `VoxelCoordinate`」属于**查询层**（Mining §1.2、§9）：
  查询层可以把 Collider / Mesh 当作**输入辅助**（例如用 `Physics.Raycast` 找到一个面），
  但其**唯一输出**必须是 `VoxelCoordinate`（可附带面方向）；
* Runtime / Mining 只消费 `VoxelCoordinate`；**禁止**把 `RaycastHit`、三角形索引、Collider 引用
  传入修改入口（Mining §1.2、§10 R17）；
* 查询层自身的规则不在本规范范围（§11）。

---

## 9. 失败语义、预算与诊断

### 9.1 失败语义

| 情形 | 判定 | 必须的行为 |
|---|---|---|
| 生成失败（异常 / 参数非法） | `Failed` | 保留 `Dirty`；不得呈现半成品；返回原因 + 日志；**不得静默** |
| Mesh 构建失败 | 回 `Dirty` | 旧表示保持不变（或保持无表示）；**不得** `ClearDirty`（Mesh 规范 §6.3） |
| 上传 / 资产创建失败 | 回 `Dirty` | 释放本次临时资产；不得留下「已清脏但未呈现」的状态 |
| 预算耗尽 | 保留队列 | 未处理项保持 `Dirty` / 排队；计数守恒（条目不丢） |
| 编辑请求被拒 | `Rejected(reason)` | 不写数据、不置脏；必须可观测（Mining §5） |
| 参数非法 | 拒绝启动 | 启动前校验（§3），禁止运行中静默修正 |

**硬性条款**

1. **不得回滚已生效的数据**（Mining §5.2）：数据写入成功后即以数据为准；
   后续步骤失败不得造成「数据改了但脏丢了」或「脏置了但数据没改」。
2. **禁止隐式重生成**：不得用「重新生成整个世界 / 整个 Chunk」掩盖状态不一致
   （生成不覆盖改动：Mining §5.6、WorldGeneration_Core §9）。
3. **失败必须可见**：计数 + 原因 + 日志；连续失败必须有上限与升级路径
   （例如停止重试并上报），禁止无限重试打满帧。
4. **禁止表示层补丁**：缺面 / 穿透 / 闪烁类问题必须回到「Voxel 数据 + Meshing 规则」定位
   （Mesh 规范 §10.3、Mining §1.6）。

### 9.2 诊断（只读）

必须可观测（供 `Tools/VoxelWorld/*` 类批处理验证入口读取，且**不得**修改世界状态）：

* 瞬时量：`LoadedChunkCount`、`ReadyChunkCount`、`DirtyChunkCount`、
  `PendingLoadCount`、`PendingEditCount`、`UnloadCandidateCount`；
* 累计量：`Generated / Meshed / Uploaded / Unloaded / Failed / Rejected / Dropped`；
* 耗时：`LastTickMs`（仅统计，不得参与世界规则）。

### 9.3 并发（V1 约束）

* V1 为**主线程分帧**：不引入后台线程 / Jobs / Burst；
* 若未来引入后台构建：只允许在**私有副本**上生成 / 构建，提交必须回主线程原子替换；
  同一 Chunk 禁止并发处理；**数据层（VoxelCore）永远不允许被并发写**。

---

## 10. 验收不变量（Runtime）

> 每条必须可自动化验证（纯数据断言 + 缓冲比较 + 结构扫描，不依赖人眼 / 渲染）。

| # | 不变量 | 验证方法 |
|---|---|---|
| **R1** | **数据先行**：不存在「已呈现非空表示但数据未就绪」的 Chunk | 遍历已呈现集合：其 `VoxelChunk` 必须存在且 `IsDirty == false` |
| **R2** | **呈现一致（无过期 Mesh）**：`Ready` Chunk 的呈现缓冲 == 用当前数据重新 `Generate(chunk)` 的结果 | 逐元素比较（顶点 / 法线 / UV / 各 submesh 索引）；或用 `ComputeContentHash()` 比较 |
| **R3** | **原子替换**：呈现缓冲恒等于**某一次完整构建**的结果；失败 / 预算中断时旧缓冲不变 | 注入失败（null / 异常 / 预算 = 0）后断言旧缓冲逐字节不变且 `IsDirty` 仍为 `true` |
| **R4** | **收敛确定性（调度无关）**：同 `Seed + RuntimeSettings + 改动序列`，任意预算 / 加载顺序 / 初始集合 ⇒ 收敛后数据与 Mesh 一致 | 以不同预算与顺序跑到全部 `Ready`，比较全量数据快照 + 每 Chunk 内容哈希 |
| **R5** | **加载集合正确**：帧末已加载集合 == 由焦点 + `R_load` 独立重算的期望集合 ∪ 仍在队列的集合；滞回区内不得被卸载 | 按 §6.1 判定式独立重算并与 Runtime 集合比较（含负坐标 Chunk） |
| **R6** | **无重复加载**：同一 Chunk 不重复生成、不重复创建表示 | `ChunkCount` == 唯一已加载 Chunk 数；重复 `Tick` 不增加累计 `Generated` |
| **R7** | **卸载即净（无泄漏）**：卸载后数据与表示均释放；资产数回落 | N 次「加载→卸载」循环后 `Mesh` 与 `GameObject` 计数回到基线；`ChunkCount` 归零 |
| **R8** | **卸载→重载一致**：重载后数据与 Mesh 与卸载前完全一致（改动保留） | 数据快照 + 内容哈希逐 Chunk 比较 |
| **R9** | **可持久化最小集**：只有 `Seed + RuntimeSettings + 改动集` 可持久化 | 结构检查：导出可持久化字段清单，断言不含 Mesh / 顶点 / 索引 / Collider / GameObject |
| **R10** | **重载不覆盖改动**：重载 / 再次生成后，已挖掘 Voxel 仍为 Air | 生成 → 挖掘 → 卸载 → 重载 → 读回目标坐标（Mining M12 同级） |
| **R11** | **编辑影响范围（V1）**：一次 Mining 只使 owning Chunk 变脏；其它 `Ready` Chunk 的呈现缓冲逐字节不变 | 编辑 A 后比较所有其它 Chunk 的缓冲（Mesh 规范 I11 / Mining M3） |
| **R12** | **脏生命周期闭合**：`Dirty` Chunk 必在有限帧内被调度；成功才清脏 | 断言 `CollectDirtyChunks ⊆ 已加载集合`；清脏只发生在其构建 + 上传成功之后（顺序断言 / 计数） |
| **R13** | **预算受控与守恒**：每帧处理数 ≤ 上限；未处理项不丢失 | 断言每帧计数 ≤ 上限，且「队列长度变化 == 本帧处理数」（无静默丢弃） |
| **R14** | **帧序无关（世界规则）**：生成与 Meshing 结果不依赖 `Time` / 帧率 / 帧序 / `UnityEngine.Random` | 同一 `Seed + 改动` 在不同预算与帧率路径下收敛结果一致；结构扫描：生成 / Meshing 路径不出现 `Time` / `Random` |
| **R15** | **只读诊断**：开启诊断 / 日志前后世界数据与表示不变 | 快照比较（数据 + 缓冲） |
| **R16** | **层边界（结构性）**：纯逻辑层不出现 Unity 运行时类型；View 层不含世界规则 | 源码 / 程序集扫描（标识符 + 程序集引用检查，见 §1.7 清单） |
| **R17** | **不依赖 Mesh 真值（结构性）**：修改入口只接受 `VoxelCoordinate` | API 签名检查：修改入口不出现 Mesh / 三角形 / `RaycastHit` / Collider 类型（Mining M11） |
| **R18** | **世界真值唯一**：释放**全部**表示后世界数据不变，重建后与释放前完全一致 | 全量释放表示 → 断言数据快照不变 → 重建 → 比较缓冲（Mesh 规范 I12） |

任何一条不变量被破坏，都视为 **Runtime 缺陷**（而非数据缺陷）；
修复必须落回本规范与上游规则，禁止「只在表示层打补丁」。

---

## 11. 明确不做（V1 out of scope）

* 后台线程 / Jobs / Burst / 异步 GPU 上传（§9.3 仅定义未来的约束）；
* LOD、遮挡剔除、Greedy Meshing、顶点焊接、跨 Chunk 剔除模式（Mesh 规范 §4 B3）；
* 光照 / 材质 / Shader / 贴图资产 / 后处理；
* 命中与射线解析规则、交互高亮、选中轮廓（查询层独立规范，§8.3）；
* 存档的字节布局 / 文件格式 / 压缩 / 版本迁移（本规范只定义内容契约，§7.1）；
* 洞穴 / 矿物 / 生物群系 / 结构（生成阶段，WorldGeneration_Core §7）；
* 玩法层内容：掉落物、物品、背包、合成、耐久、工具、音效、粒子、动画；
* 网络同步 / 多人、并发编辑、撤销 / 重做；
* 毫秒级预算调度器、优先级抢占、`VerticalMode.FocusRange` 的完整规则（后续显式扩展）。

---

## 12. 变更规则

1. 与上游冲突时的优先级：`VoxelWorld_Core.md`（真值）> `VoxelWorld_Mining.md`（修改）
   > `VoxelWorld_Mesh.md`（表示）> 本规范（调度 / 生命周期 / 流式）。
2. 任何修改若触及 §1（原则）、§9（失败语义）或 §10（不变量），必须：
   ① 升版本号；② 同步更新验收测试；③ 说明与既有存档（`Seed + 改动集`）的兼容性。
3. **禁止**用 Runtime 侧的补丁（强制重建、隐藏、跳过清脏、整体重置）掩盖数据层或 Meshing 缺陷。
4. V1 实现范围：**加载 → 生成 → 脏 → 构建 → 上传 → 卸载**这条闭环，加上
   「`Seed + 改动集` 重载重建」；§11 的内容一律不得进入 V1。
5. Runtime 的实现不得要求修改 VoxelCore / Generation / Mesh 的既有契约
   （WorldGeneration_Core §10：扩展不得要求修改 VoxelCore）。

---

## 附录 A：当前落地状态与建议落地顺序

**已实现（冻结，纯 C#）**

* VoxelCore：`VoxelType` / `Voxel` / `VoxelCoordinate` / `VoxelChunk` / `VoxelWorld` / `VoxelWorldDebug`
  （`Assets/Scripts/VoxelWorld/`）；
* Generation：`WorldGenerationSettings` / `IWorldGenerator` / `HeightFunction` / `BasicTerrainGenerator`
  （`Assets/Scripts/VoxelWorld/Generation/`）；
* Mesh 数据与生成器：`VoxelMeshData` / `VoxelMeshGenerator`
  （`Assets/Scripts/VoxelWorld/Mesh/`，`.Mesh` 命名空间，只依赖 `Vector2/Vector3`）。

**仅有规范、尚无实现**

* Mining（`VoxelWorld_Mining.md`）：单 Voxel 挖掘流程 + 脏语义；
* Runtime（本文档）：调度核心 + View 层。

**建议落地顺序（每步都必须可独立验证，不得跳步）**

1. **Mining 实现**（纯 C#）：`Mine(VoxelCoordinate)` → `MiningOutcome`，验收 Mining 规范 M1–M12；
2. **Runtime Core（无 Unity 类型）**：焦点→集合求解、生命周期状态机、预算队列、改动集；
   以纯 C# 驱动 + 伪造焦点位置，验证 R4 / R5 / R6 / R11 / R12 / R13；
3. **Runtime View（Unity）**：`Mesh` 资产、`MeshFilter` / `MeshCollider`、坐标摆放、原子替换；
   验证 R1 / R2 / R3 / R7 / R18；
4. **流式卸载 + 改动集重载**：验证 R8 / R9 / R10；
5. **结构性扫描**：R14 / R16 / R17（与既有 `Tools/VoxelWorld/*` 验证入口同风格）。

> 第 2 步是关键分界：**Runtime Core 必须能在没有 Unity 场景、没有 GameObject 的情况下被完整测试**；
> 做不到就说明依赖画错了，先修依赖，再往第 3 步走。
