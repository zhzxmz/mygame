# Voxel World Runtime Core Specification V1

> 适用范围：`Assets/Scripts/VoxelWorld/Runtime/`（Runtime Core V1）——**纯 C# 调度核心**。
> 本规范是 `VoxelWorld_Runtime.md` §2/§5 中 **Runtime Core 子层**的细化实现契约，
> 只补充 Core 自身，不新增世界规则、不改任何上游语义。
> **不涉及**既有 Terrain 系统（`TerrainModificationV2` / `DensityField` / Marching Cubes 地形）。
>
> 上游规范（本文档不得与其冲突）：
> * `Specifications/World/VoxelWorld_Core.md` —— 世界真值（Voxel Data）与 Chunk 数据契约；
> * `Specifications/World/WorldGeneration_Core.md` —— 初始状态生成（Seed + ChunkCoordinate → VoxelChunk）；
> * `Specifications/World/VoxelWorld_Mesh.md` —— 派生表示的几何数据契约（Meshing 只读 / 不变量）；
> * `Specifications/World/VoxelWorld_Mining.md` —— 世界数据修改（编辑规则属该规范，不属 Core）；
> * `Specifications/World/VoxelWorld_Runtime.md` —— 调度、生命周期、流式加载、存档边界（本文档的上位规范）；
> * `Specifications/World/VoxelWorld_View.md` —— 表示层（Unity 侧；Core 只通过接口调用它）。
>
> 优先级：**真值** 以 `VoxelWorld_Core.md` 为准；**修改规则** 以 `VoxelWorld_Mining.md` 为准；
> **几何** 以 `VoxelWorld_Mesh.md` 为准；**调度/生命周期/流式** 以 `VoxelWorld_Runtime.md` 为准；
> **Core 的内部结构、接口、阶段实现细节** 以本文档为准。
> 本文档**只约束 Runtime Core**：不改变任何上游不变量，不重新定义生命周期语义（只做细化与收紧）。

---

## 1. Core Principles（不可协商条款）

1. **纯 C#（可机械验证，见 §1.1）。**
   Runtime Core 的**源码中不出现任何 UnityEngine / UnityEditor 标识符**，
   不创建 GameObject、不创建 Mesh 资产、不接触场景与渲染；
   它可以在**没有场景、没有 GameObject、没有渲染**的环境下完成全部调度。
2. **只调度，不决策。**
   Core 决定「何时 / 什么顺序 / 每帧多少」：加载、生成、重建、呈递、卸载；
   Core **不定义**「体素长什么样」（Generation）、「能否挖」（Mining）、「几何怎么生成」（Mesh）。
   任何在 Core 里"顺手算一下地形 / 顺手改一下体素"的写法都视为越界。
3. **数据先行（Data First）。**
   顺序恒为 **数据 → Mesh 数据 → 表示提交**；表示永不超前于数据（Runtime §1.2）。
4. **帧末状态闭合（Tick-Exit Closure，本规范的核心收紧条款）。**
   任一 `Tick` 返回时，注册表中**不存在** `Generating` / `Building` / `Unloading` 状态 ——
   三者都是 Tick **内部瞬态**。即：**要么整块完成，要么完全不动**，不存在"半加载 / 半构建 / 半卸载"的 Chunk。
5. **确定性（与上游同级约束）。**
   相同 `(初始世界状态, RuntimeCoreSettings, focusChunk 序列, 编辑序列)`
   ⇒ 相同的注册表内容、相同的状态序列、相同的每 Chunk Mesh 内容哈希；
   **预算与加载顺序不得改变收敛结果**；**时间/帧率/随机数/字典遍历顺序一律不得参与决策**。
6. **单一写入口与唯一真值容器。**
   世界数据的写入**只能**经 `VoxelWorld` 的写入 API（`GetOrCreateChunk` / `SetVoxel` / `RemoveChunk`）；
   世界数据的所有权在 `VoxelWorld`，Core **不创建 `VoxelChunk`、不复制体素到私有容器、不持有第二份真值**。
7. **表示可丢弃、可重建。**
   表示层（View）是可随时释放的派生结果；Core 只通过接口调用它，
   释放后按 `Seed + RuntimeCoreSettings + 改动集` 重建，结果必须与释放前一致（Runtime R8/R18）。

### 1.1 纯度的可验证定义（RC-P1 … RC-P4）

| 条款 | 内容 | 验证方式 |
|---|---|---|
| **RC-P1** | Runtime Core 源码中**不出现** `UnityEngine` / `UnityEditor` 标识符，也不出现 `Vector2` / `Vector3` / `Bounds` / `GameObject` / `Transform` / `Mesh` / `Material` / `Time` / `Random` 等 Unity 类型名 | 源码扫描（剔除注释与字符串后逐 token 检查），并带扫描器自检 |
| **RC-P2** | Core **不访问** `VoxelMeshData` 的 Unity 结构体成员（`Vertices` / `Normals` / `UVs`），**只**把它当作不透明负载；只允许调用纯 C# 成员：`TriangleCount` / `QuadCount` / `VertexCount` / `HasGeometry` / `GetSubmeshTriangleCount` / `ComputeContentHash` / `ToString` / `Triangles`（`int[][]`） | 源码扫描：不得出现 `.Vertices` / `.Normals` / `.UVs` |
| **RC-P3** | 全部调度在无场景、无 GameObject、无渲染条件下可完整运行（由 **FakePresenter** 驱动的纯 C# 验证） | 批处理验证：不 `new GameObject`，全部不变量通过 |
| **RC-P4** | 依赖方向单向：**Core 不引用 View 实现**；View 实现 Core 定义的接口（§2.4） | 程序集/源码检查：Core 目录不出现 `VoxelWorld.View` |

> **诚实说明（元数据层面的传递依赖）**：`VoxelMeshData` 定义在 `VoxelWorld.Mesh` 中，
> 其字段类型为 `Vector2/Vector3`，因此 Core 的程序集元数据会**间接**引用 UnityEngine。
> 这是不可避免的，也**不属于** RC-P1 的违规：RC-P1 约束的是
> **Core 自身源码不出现、不使用、不构造任何 Unity 类型**，且行为不依赖场景/渲染。

---

## 2. 模块边界与类型清单

### 2.1 位置与依赖方向

```
Assets/Scripts/VoxelWorld/Runtime/          namespace VoxelWorld.Runtime
        │  允许依赖（只读/调用）：
        ├── VoxelWorld            （VoxelCore：VoxelCoordinate / VoxelType / VoxelChunk / VoxelWorld）
        ├── VoxelWorld.Generation （IWorldGenerator / WorldGenerationSettings）
        └── VoxelWorld.Mesh       （VoxelMeshGenerator.Generate → VoxelMeshData，当作不透明负载）
        │  禁止依赖：
        ✗ UnityEngine.* / UnityEditor.*
        ✗ VoxelWorld.View（表示实现）—— 依赖方向由 View → Core 的接口实现完成
        ✗ TerrainModificationV2 / DensityField（旧 Terrain 体系）
```

| 允许 | 禁止 |
|---|---|
| 读/写世界数据（经 `VoxelWorld` API）、调用生成器、调用 Mesher、调用表示接口 | 创建 Unity 对象、触碰场景/资产/渲染/物理、读 `Time`/`Random` |
| 自有的注册表、队列、状态机、改动集（纯 C# 集合） | 自建体素表 / 影子 Mesh / 第二份世界状态 |
| 把 `VoxelMeshData` 作为不透明负载传递 | 拆分/修改/重新生成几何；访问 Unity 结构体成员（RC-P2） |
| 依据 `VoxelChunk.IsDirty` 做投影 | 批量清脏（`ClearDirtyFlags`，§8.3 明令禁止） |

### 2.2 类型清单（V1）

| 类型 | 职责 | 纯度 |
|---|---|---|
| `VoxelRuntimeCore` | 主入口：`Tick(focusChunk)`、只读快照、编辑提交、显式重试 | 纯 C# |
| `RuntimeCoreSettings` | 显式参数（全部整数/枚举，无 Unity 类型） | 纯 C# |
| `RuntimeCoreTickReport` | 单帧统计（只读） | 纯 C# |
| `RuntimeCoreDiagnostics` | 累计计数（只读） | 纯 C# |
| `ChunkLifecycleState` | 生命周期枚举（§3） | 纯 C# |
| `ChunkRecord` / `ChunkRecordSnapshot` | 注册表记录 / 对外只读快照（排序） | 纯 C# |
| `IChunkPresenter` + `PresenterOutcome` | Core → View 的唯一接缝（§2.4） | 纯 C# |
| `RuntimeChangeSet` | 改动集（坐标 → 最终类型，稀疏、覆盖式） | 纯 C# |

### 2.3 RuntimeCoreSettings（显式参数，无隐藏全局状态）

| 参数 | 含义 | V1 约束 |
|---|---|---|
| `ViewDistanceChunks`（`R_load`） | 水平加载半径（Chunk 单位） | `≥ 0` |
| `UnloadHysteresisChunks` | 卸载滞回：`R_unload = R_load + Hysteresis` | `≥ 0`（建议 `≥ 1`） |
| `VerticalChunkCount` | 垂直 Chunk 数量（由 `WorldHeight` 推导，显式传入） | `≥ 1` |
| `MaxChunkPairsPerFrame` | 每帧最多完成的「构建 + 呈递」配对数（**细化** Runtime §3 的 `MaxMeshesBuiltPerFrame` / `MaxUploadsPerFrame`：V1 两者合并为一个配对上限） | `≥ 1` |
| `MaxLoadsPerFrame` | 每帧最多加载（生成 + 安装）的 Chunk 数 | `≥ 1` |
| `MaxUnloadsPerFrame` | 每帧最多卸载的 Chunk 数 | `≥ 1` |
| `MaxPendingEdits` | 编辑请求排队上限 | `≥ 0` |
| `MaxRetriesPerChunk` | 单 Chunk 自动重试上限（超过 ⇒ 停止自动重试） | `≥ 0` |
| `OutOfRangePolicy` | 编辑目标在 `R_load` 外时的策略：`Reject` / `DemandLoad` | 必须显式选择 |
| `IsValid(out reason)` | 启动前校验（Runtime §3.5：非法参数拒绝启动，禁止静默修正） | — |

**约束**：`Seed` / 地形参数**不**由 Core 保存第二份 —— Core 只持有 `IWorldGenerator`
（其 `Settings` 即 `WorldGenerationSettings`，含 `Seed`）；
`ChunkSize` 只能使用 VoxelCore 的常量，Core 不得自行定义第二套尺寸。

### 2.4 Unity 接缝：`IChunkPresenter`（Core 定义，View 实现）

```csharp
public enum PresenterOutcome { Presented, Empty, Failed }

public interface IChunkPresenter
{
    // 输入只有坐标 + 纯数据缓冲（View §1.2）；失败必须给出原因（View §9.1）
    PresenterOutcome Apply(VoxelCoordinate coordinate, VoxelMeshData meshData, out string error);

    // 释放该坐标的表示（幂等；View §5.6）
    void Release(VoxelCoordinate coordinate);

    // 只读诊断：当前存活表示数（用于"卸载即净"断言）
    int LiveCount { get; }
}
```

* Core **只**通过该接口触碰表示层，**不知道** View 是否有 GameObject / Mesh 资产 / 对象池；
* `Presented` 与 `Empty` 都视为**成功**（View §4.3：空几何不是失败）；
* `Failed` ⇒ 该 Chunk 状态回 `Dirty`，且 **不得** `ClearDirty`（§9）。

---

## 3. Chunk 生命周期（要求：明确 Chunk 生命周期）

### 3.1 状态表

| 状态 | 含义 | 进入条件 | 离开条件（Tick 内） |
|---|---|---|---|
| `Generating` | 正在生成并安装数据（**Tick 内瞬态**，不跨帧） | 被加载集合选中 | 成功 → `Dirty`；抛错 → `Failed` |
| `Dirty` | 数据就绪、`IsDirty == true`（表示缺失或过期） | 安装完成；编辑生效；呈递失败回退 | 构建 + 呈递成功 → `Ready`；超出 `R_unload` → `Unloading` |
| `Building` | 正在 Meshing + 呈递（**Tick 内瞬态**，与呈递成对） | 从 `Dirty` 被调度 | 成功 → `Ready`；失败 → `Dirty`（或超限 → `Failed`） |
| `Ready` | 表示与当前数据一致（`IsDirty == false`） | 呈递成功 | 数据变化 → `Dirty`；超出 `R_unload` → `Unloading` |
| `Unloading` | 正在释放表示与数据（**Tick 内瞬态**，不跨帧） | 超出 `R_unload` | 释放完成 → **注销（离开注册表 = `Absent`）** |
| `Failed` | 生成/构建/呈递失败（保留现场与原因） | 任一阶段抛错 / 失败 | 显式重试或自动重试（未超上限）→ `Dirty` |

**`Absent` 不是注册表状态**：不在注册表中即视为 `Absent`（未加载）。
注册表只保存"已加载或已知"的 Chunk 记录（§4）。

### 3.2 合法转移（唯一允许的集合）

```
（注册）        → Generating → Dirty → Building → Ready
                                   ↑__________|          （失败回退）
                                   ↑____________________|  （数据变化 / 编辑）
Dirty|Ready|Failed → Unloading → （注销）
Failed →（重试）→ Generating | Dirty
```

任何其它转移都是缺陷（例如 `Ready → Building` 而不经过 `Dirty`：那等于承认存在过期表示）。

### 3.3 与 VoxelCore 的投影不变量（双向可断言）

1. `State == Ready` **⟺** `chunk != null && !chunk.IsDirty`；
2. `State ∈ { Dirty, Building }` ⇒ `chunk != null && chunk.IsDirty == true`；
3. `State ∈ { Generating, Building, Unloading }` **只能**出现在 `Tick` 内部（§1.4 帧末闭合）；
4. `State == Failed` ⇒ `FailureReason` 非空（禁止静默失败）；
5. `State ∈ { Dirty, Building, Ready }` ⇒ `chunk != null`（数据必须存在）；`Failed` 允许 `chunk == null`（生成本身失败，世界未被触碰）。

---

## 4. Chunk 注册表（要求：明确 Chunk 注册表）

### 4.1 结构与记录字段

```
registry : Dictionary<VoxelCoordinate /* Chunk 坐标 */, ChunkRecord>
```

| 字段 | 含义 |
|---|---|
| `Coordinate` | Chunk 坐标（值语义键；`VoxelCoordinate` 已实现 `Equals`/`GetHashCode`/`==`） |
| `State` | §3 的生命周期状态 |
| `Chunk` | **世界拥有**的 `VoxelChunk` 引用（由 `VoxelWorld` 创建；Core 不 `new`） |
| `Mesh` | 最近一次成功构建的 `VoxelMeshData`（纯数据、可丢弃；V1 中同 Tick 内即用完，不必长期保留） |
| `FailureReason` / `FailureCount` | 最近失败原因 / 失败次数（可观测，Runtime §9） |
| `RegisteredTick` / `StateChangedTick` | Core 自有的**单调 Tick 序号**（不是时间；用于诊断与陈旧检测） |

### 4.2 注册 / 注销时机

* **注册**：`Generating` 开始时（每帧 B/C/D 阶段，被加载集合选中）；
* **注销**：`Unloading` 完成时（表示 → 数据 → 记录）；
* **失败记录保留**：`Failed` 记录**保留**在注册表中（便于诊断与重试），直到其坐标超出 `R_unload` 被清理。

### 4.3 不变量（RC4）

1. **坐标唯一**：同一 Chunk 坐标至多一条记录（禁止重复加载/重复表示，Runtime R6）；
2. **计数一致**：`world.ChunkCount == registry 中 Chunk != null 的记录数`
   （Core 不持有未被 `VoxelWorld` 承认的 Chunk，也不允许"世界里有无主 Chunk"）；
3. **对外集合必须排序**：所有暴露给外部的坐标集合（已加载、脏、失败、加载候选、卸载候选）
   **一律按 `(x, y, z)` 升序**返回（确定性；禁止暴露字典遍历顺序）；
4. **只读快照**：对外只提供只读快照（`ChunkRecordSnapshot`），不得让外部拿到可变记录；
5. **不持有第二份真值**：Core 不缓存体素副本、不缓存"世界的另一份状态"。

---

## 5. Generation 调用（要求：明确 Generation 调用）

### 5.1 调用契约

```
IWorldGenerator generator   （由外部显式注入；含 WorldGenerationSettings / Seed）
VoxelChunk source = generator.Generate(chunkCoordinate);   // 生成结果：不注册、世界未触碰
```

* 输入 = Chunk 坐标（不假设生成顺序、不看相邻 Chunk 是否已加载，WorldGeneration_Core §3/§6）；
* 输出 = 一个**新的、不属于任何世界**的 `VoxelChunk`（`IWorldGenerator` 契约）；
* 生成必须是**纯调用**：Core 不传时间/帧序/随机状态，不修改生成器状态。

### 5.2 安装（生成结果 → 世界真值）—— 冻结 VoxelCore 下的唯一合法路径

VoxelCore 冻结且**没有**"安装 Chunk"的 API（只有 `GetOrCreateChunk` 懒创建空 Chunk），
因此 Runtime Core V1 的安装路径**必须**是：

```
1) source = generator.Generate(coord)                    // 可能抛错 ⇒ 世界未被触碰（干净失败）
2) target = world.GetOrCreateChunk(coord)                // 由 VoxelWorld 拥有（懒创建）
3) 逐体素搬运（固定顺序 localX → localY → localZ）：
       for each local: target.SetVoxel(local, source.GetVoxel(local).Type)
4) 叠加改动集（§5.4，必须在 3) 之后 —— 改动优先，生成不覆盖改动）
5) 收尾：保证 target.IsDirty == true（必要时显式 MarkDirty()）
```

**约束与说明**

1. 写入**只能**经 `VoxelChunk.SetVoxel`（VoxelCore 唯一写入口语义，Mining §1.1）；
   Core 不得反射/绕过 API 直接改内部数组；
2. **"只写非 Air"是允许的显式优化，但只有在前置条件成立时**：
   目标 Chunk 由 `GetOrCreateChunk` **新创建**（必为空，全部 Air）⇒ 跳过 Air 写入与全量写入**结果等价**；
   若目标 Chunk **已存在**（例如被外部/编辑懒创建），**必须全量写入（含 Air）**，
   否则会残留旧数据 ⇒ 违反"生成结果 = 世界真值"；
3. **空 Chunk 也必须是 `Dirty`**：新 `VoxelChunk` 初始即 `IsDirty == true`（VoxelCore），
   因此"生成结果全为 Air ⇒ 零次 `SetVoxel`"仍然满足收尾条款 5)；实现**不得**依赖"写了几次"来判断是否需要置脏；
4. **生成不覆盖改动**：改动集叠加必须在搬运**之后**（WorldGeneration_Core §9 / Mining §7.3）；
5. **安装是原子的**：安装未完成 ⇒ 该 Chunk 不得被标记为可用（`Dirty`/`Ready`）；
   若安装中途抛错，重试时**必须走全量写入路径**（覆盖残留），保证结果确定（§9.2）。

> **未来扩展（V2 候选，不在 V1 范围）**：为 `IWorldGenerator` 增加
> `GenerateInto(chunkCoordinate, target)` 可去掉整块搬运；
> 那属于修改 Generation 契约（本轮明确不做）。

### 5.3 加载集合与顺序

```
期望加载集合 = { c | dx² + dz² ≤ R_load² ∧ c.y ∈ [0, VerticalChunkCount) }
其中 (dx, dz) = (c.x − focusChunk.x, c.z − focusChunk.z)      // 整数运算，负坐标同样正确
```

* 每帧**重算**（不跨帧缓存集合状态）——确定性优先，规模为 `(2R+1)²`，成本可忽略；
* 执行顺序 = 先按 `(dx²+dz², c.x, c.y, c.z)` **升序**取前 `MaxLoadsPerFrame` 个；
* 已在注册表中（含 `Failed` 且未超重试上限）的坐标**不再生成**；
* 每帧最多加载 `MaxLoadsPerFrame` 个，且**每个必须整块完成**（§1.4）。

### 5.4 改动集（最小面）

* 结构：`runtimeChangeSet : Dictionary<VoxelCoordinate /* 世界体素坐标 */, VoxelType>`，**覆盖式**（同坐标只留最终值）；
* 叠加：取该 Chunk 的条目（`VoxelWorld.WorldToChunkCoordinate(entry) == chunkCoordinate`），
  按世界坐标 `(x, y, z)` **升序**，用 `VoxelWorld.WorldToLocalCoordinate` 换算局部坐标后 `SetVoxel`；
* 记录时机：编辑**成功写入世界之后**（`SetVoxel` 返回 `true`）才记录；同值写入不记录新条目（Mining §6）；
* 语义：改动集 = "与生成结果不同的最终值"，与 `Seed + 参数` 一起构成最小可持久化状态（Runtime §7.1）。
* **编辑规则不属 Core**：是否允许编辑、工具/权限/玩法判定属于 Mining 与玩法层；
  Core 只负责「写入 + 记录 + 脏投影 + 前置条件排队」（§7.4）。

---

## 6. Mesh 调用（要求：明确 Mesh 调用）

### 6.1 调用契约

```
VoxelMeshData meshData = VoxelMeshGenerator.Generate(chunk);   // 输入 = 世界拥有的 Chunk（只读）
```

| 项 | 契约 |
|---|---|
| 输入 | `VoxelChunk`（`record.Chunk`，**当前数据已就绪**；禁止对 `Generating` 中的 Chunk 构建） |
| 输出 | `VoxelMeshData`（纯数据）——Core 只作**不透明负载**传递（RC-P2） |
| 只读性 | 构建前后 `chunk` 的 4096 体素、`Coordinate`、`IsDirty` **必须完全不变**（Mesh §1.2 / I1） |
| 次数 | 同一 Chunk **每帧至多 1 次**构建；同一 Tick 内不重复构建（去重由状态机保证） |
| 失败 | 抛异常 ⇒ 捕获 ⇒ 状态回 `Dirty`（或超限 → `Failed`），**不**调用 `Apply`，**不** `ClearDirty` |
| 加工 | Core **不得**修改/裁剪/合并几何（Greedy/Welding 属 Mesh 层未来扩展） |
| 预算 | 每帧最多 `MaxChunkPairsPerFrame` 对「构建 + 呈递」 |

### 6.2 构建与呈递必须配对（V1 收紧）

```
F 阶段：VoxelMeshGenerator.Generate(chunk) → meshData        （纯数据）
G 阶段：presenter.Apply(coord, meshData, out error)          （同一 Tick、同一 Chunk、紧接其后）
        成功 ⇒ chunk.ClearDirty() ⇒ State = Ready
```

* **不得**让"已构建但未呈递"的缓冲跨 Tick 存活（那会拉长"数据 ↔ 表示"不一致窗口，
  也不会产生新的可用不变量）；因此 `ChunkRecord.Mesh` 在 V1 中只是同 Tick 的临时引用；
* `meshData.HasGeometry == false`（空 Chunk）⇒ 仍必须调用 `Apply` 并按其成功结果清脏
  （View §4.3 / §5.4：空几何是成功路径）；
* 只有 `Apply` 成功后**才**允许 `ClearDirty()`；`ClearDirty` **只能**作用于该 Chunk 自己（§8.3）。

### 6.3 确定性证据

Core 必须能给出每个 Chunk 的 `meshData.ComputeContentHash()`（Mesh 层提供的纯数据哈希），
作为"收敛确定性 / 卸载-重载一致"的机械证据（§10.3、RC7/RC12）。

---

## 7. View 调用顺序（要求：明确 View 调用顺序）

### 7.1 三个调用点（唯一允许的调用位置）

| 阶段 | 调用 | 前置条件 | 顺序约束 | 后置条件 |
|---|---|---|---|---|
| **C 卸载** | `presenter.Release(coord)` → `world.RemoveChunk(coord)` → 注销记录 | 该坐标超出 `R_unload` | **表示先释放，数据后删除**（Runtime §6.3：避免一帧"幽灵呈现"） | 表示与数据都已释放；`world.ChunkCount` 减少 |
| **G 呈递** | `presenter.Apply(coord, meshData, out error)` | 数据就绪 **且** Mesh 构建成功（或空几何） | 紧接该 Chunk 的构建之后（同 Tick 配对） | 成功 ⇒ `ClearDirty()` ⇒ `Ready`；失败 ⇒ 回 `Dirty`（不 `ClearDirty`） |
| **D 加载** | **不调用** presenter | — | 数据先行：加载阶段绝不触碰表示 | 加载完成后表示仍为空/旧（随后由 F/G 呈递） |

### 7.2 硬性顺序条款

1. **数据 → Mesh → 表示**：永不出现"表示已呈现但数据未就绪"（Runtime R1）；
2. **同 Tick 配对**：`Apply` 不得与它对应的构建跨 Tick；
3. **每 Chunk 每 Tick 至多一次 `Apply`**；
4. **`Release` 无条件调用**：卸载时即使该 Chunk 没有数据（例如 `Failed` + `chunk == null`）也必须调用
   `Release`，保证表示侧无残留（View §9.1 的失败路径可能保留了旧表示）；
5. **`Apply` 失败不得调用 `Release`**：旧表示必须保持（View §5.5 / §9.1 原子性）；
6. **释放顺序固定**：`Release` → `RemoveChunk` → 注销（三者同一 Tick 完成）；
7. **Core 不检查表示内部**：只看 `PresenterOutcome` 与 `LiveCount`（不参与任何世界规则判断）。

### 7.3 呈递与脏标记的权责（单一权威）

| 动作 | 执行者 | 说明 |
|---|---|---|
| 置脏 | VoxelCore（数据实际变化）/ 新 Chunk 初始 | Core 不手工置脏（仅在安装收尾兜底，§5.2-5） |
| 构建 Mesh | Core（F 阶段） | 只读数据 |
| 提交表示 | Core → `IChunkPresenter.Apply` | 结果三态：`Presented / Empty / Failed` |
| **清脏** | Core（G 阶段**成功后**） | 只能 `chunk.ClearDirty()`，且只清"刚刚呈递成功"的 Chunk |
| 释放表示 | Core → `IChunkPresenter.Release` | 只在卸载阶段 |

### 7.4 编辑请求的加载前置条件（Runtime §5.3 的 Core 侧落地）

| 目标 Chunk 状态 | Core 行为 | 可观测结果 |
|---|---|---|
| 已加载（`Dirty` / `Building` / `Ready`） | 立即 `world.SetVoxel(coord, type)`；若返回 `true` ⇒ 记录改动集 + 状态投影为 `Dirty` | `Applied` / `Unchanged`（`SetVoxel` 返回值） |
| 未加载，且在 `R_load` 内 | 入 **FIFO 待办队列**（上限 `MaxPendingEdits`），该 Chunk 加载完成后**必须**重投 | `Pending`（计数可见；**禁止**当作 `Unchanged` 丢弃） |
| 未加载，且在 `R_load` 外 | 按 `OutOfRangePolicy`：`Reject`（带原因）或 `DemandLoad`（加入加载集合，仍受每帧上限） | 结果与原因必须可观测 |
| 队列已满 | `Rejected(QueueFull)` + 计数 | 禁止静默丢弃 |

---

## 8. Dirty 处理（要求：明确 Dirty 处理）

### 8.1 置脏来源（只有两个）

1. **数据实际变化**：`VoxelChunk.SetVoxel` 类型真正变化 ⇒ `IsDirty = true`（VoxelCore 保证；同值写入返回 `false` 且不置脏）；
2. **新 Chunk 初始脏**：`new VoxelChunk(...)` 即为 `Dirty`（"尚未构建过"）。

Core **不得**引入 per-voxel dirty、不得引入额外 dirty 注册表、不得用其它信号（是否高亮/是否已渲染）代替数据判定。

### 8.2 排空（每帧）

```
1) world.CollectDirtyChunks(buffer)        // VoxelCore 已按 Chunk 坐标排序（确定性）
2) 过滤：只保留"在注册表中且 State ∈ { Dirty }"的 Chunk（未加载的脏 Chunk 不构建）
3) 按排序顺序取前 MaxChunkPairsPerFrame 个 → 构建 + 呈递（配对，§6.2）
4) 未处理项保持 Dirty（下次继续；不得丢弃）
```

### 8.3 清脏（最严格的一条）

1. **只能** `chunk.ClearDirty()`，且**只在**该 Chunk 的 `Apply` **成功之后**；
2. **禁止** `VoxelWorld.ClearDirtyFlags()`：批量清脏会清掉"尚未重建"的 Chunk ⇒ **永久过期表示**
   （"看着是实体、实际已挖空"类缺陷）。该 API 只允许用于 VoxelCore 自检/测试代码，禁止出现在 Core；
3. 失败路径**绝不**清脏：生成失败、构建失败、`Apply` 失败、预算不足、异常 ⇒ 一律保持 `Dirty`；
4. `Ready` 状态与 `IsDirty == false` 必须**同时**成立（§3.3-1）。

### 8.4 卸载与脏

* 卸载一个**脏**的 Chunk 是允许的（超出 `R_unload`）；
  它的数据经改动集保存，重载后由「生成 + 叠加改动」重建（§5.2/§5.4），
  重载后**必然**重新回到 `Dirty`（新 Chunk 初始脏 + 叠加写入），随后被重建；
* 卸载**不得**试图"先重建再卸载"（那会把预算浪费在马上要丢弃的几何上）。

### 8.5 数据 / 注册表对账（Reconcile，每帧一次）

| 情况 | 判定 | Core 行为 |
|---|---|---|
| `world` 中有 Chunk，但注册表无记录 | 外部写入（有人绕过 Core 直接 `SetVoxel`） | **保留其数据**（生成不覆盖改动）⇒ 纳入注册表并置 `Dirty`（随后按 §5.2 路径 2 全量补齐生成结果） |
| 注册表记录 `chunk != null`，但 `world` 中已无该 Chunk | 数据丢失（外部 `RemoveChunk`/`Clear`） | 状态 → `Failed`（原因 = `DataMissing`），**不**伪造数据、**不**静默重建 |
| `State == Ready` 但 `chunk.IsDirty == true` | 外部改了数据 | 状态 → `Dirty`（投影修正，Runtime §4.5） |

对账是**只投影、不改数据**的检查（除"纳入注册表"这一登记动作），且必须确定性地执行（顺序 = 坐标升序）。

---

## 9. Failure 处理（要求：明确 Failure 处理）

### 9.1 失败矩阵

| 阶段 | 失败情形 | 状态 | 世界数据 | 表示 | 脏标记 |
|---|---|---|---|---|---|
| 生成 | `generator.Generate` 抛错 | `Failed`（`chunk == null`） | **未被触碰** | 不调用 | — |
| 安装 | 搬运/叠加抛错 | `Failed`（可能部分写入） | 可能部分写入 ⇒ 重试必须**全量写入** | 不调用 | 保持 `Dirty` |
| 构建 | `VoxelMeshGenerator.Generate` 抛错 | 回 `Dirty` | 不变 | 不调用 | 保持 `Dirty` |
| 呈递 | `Apply` 返回 `Failed` | 回 `Dirty` | 不变 | 旧表示保持（View 自身保证） | **不得**清脏 |
| 卸载 | `Release` 抛错 | 仍记为 `Failed`（但数据必须已释放） | 已 `RemoveChunk` | 已尝试释放 | — |
| 预算不足 | 未处理 | 保持 `Dirty` / 保持排队 | 不变 | 不变 | 保持 `Dirty` |
| 参数非法 | `IsValid` 失败 | 拒绝启动 | — | — | — |

### 9.2 硬性条款

1. **不得回滚已生效的数据**（Mining §5.2）：写入成功后世界以数据为准；
   后续失败不得造成"数据改了但脏丢了"或"脏置了但数据没改"；
2. **禁止隐式重生成**：不得用"重新生成整个世界 / 整个 Chunk"掩盖状态不一致
   （WorldGeneration_Core §9：生成不得覆盖改动）；
3. **失败必须可见**：`FailureReason` + `FailureCount` + 累计计数 + 日志；禁止静默空操作；
4. **重试有界**：`FailureCount < MaxRetriesPerChunk` 时自动重试（下一 Tick）；
   达到上限 ⇒ 停止自动重试并保持 `Failed`（**仍然保持 `IsDirty == true`**），
   只能由显式 `RetryFailedChunks()` 重新开始（Runtime §9.1-3：禁止无限重试打满帧）；
5. **失败不破坏不变量**：任一失败路径下，§3.3 的投影不变量、§4.3 的注册表不变量、
   §1.4 的帧末闭合仍然成立；
6. **重试幂等**：重试同一坐标必须收敛到与成功路径**相同**的数据与 Mesh 内容哈希；
7. **禁止表示层补丁**：不得用"藏起来/强制重建/整体重置"掩盖数据层或 Meshing 缺陷
   （Mesh §10.3、Mining §1.6、Runtime §9.1-4）。

---

## 10. Determinism（要求：明确 Determinism）

### 10.1 定义

给定 `(初始世界状态, RuntimeCoreSettings, focusChunk 序列, 编辑序列)`：

1. **状态序列确定**：每个 Chunk 的状态转移序列相同；
2. **收敛确定**：全部 Chunk 达到 `Ready` 后，世界数据与每 Chunk 的
   `VoxelMeshData.ComputeContentHash()` 完全相同；
3. **预算无关**：任意 `MaxLoadsPerFrame` / `MaxUnloadsPerFrame` / `MaxChunkPairsPerFrame`
   ⇒ 收敛结果相同（分帧只改变"何时"，不改变"最终是什么"）；
4. **顺序无关**：初始已加载集合、加载顺序、卸载/重载次数不得改变收敛结果。

### 10.2 排序与禁止项

* 一切对外集合按 `(x, y, z)` **升序**（§4.3-3）；待办编辑按**提交序号 FIFO**；
* 加载候选按 `(水平距离², x, y, z)` 升序；卸载候选按同一键**降序**；
* 禁止把 `Dictionary` / `HashSet` 遍历顺序、`GetHashCode`、对象引用身份、
  `DateTime` / `Time` / `Random` / `Guid.NewGuid` / `Task` / 线程调度 当作决策依据或排序键；
* 所有"距离/集合判定"使用**整数运算**（`dx² + dz² ≤ R²`），不使用浮点（避免平台差异）；
* Core 自有的时间概念只有**单调 Tick 序号**（`TickIndex++`），它不参与世界数据与 Meshing 结果。

### 10.3 可机械验证的证据

| 证据 | 用途 |
|---|---|
| 每 Chunk `ComputeContentHash()` | 收敛一致 / 卸载-重载一致 / 重试一致 |
| 注册表只读快照（排序） | 状态序列与集合一致 |
| 累计计数（`Generated` / `Built` / `Presented` / `Unloaded` / `Failed` / `Rejected` / `Pending`） | 无重复生成、无静默丢弃、预算守恒 |
| 全量体素快照（4096/Chunk） | 生成不覆盖改动、Meshing 只读、失败不破坏数据 |

---

## 11. Tick 契约

```
TickReport report = core.Tick(focusChunk);      // focusChunk 为整数 Chunk 坐标（不接受 Transform / 世界浮点位置）
```

| 阶段 | 本次 Tick 执行的内容 | 对应 Runtime §5 |
|---|---|---|
| A 焦点解析 | **由调用方完成**（Core 只接受 `focusChunk` 整数坐标 ⇒ 完全不接触 `Transform`/浮点） | A |
| B 集合求解 | 重算加载集合 / 卸载候选；对账（§8.5） | B |
| C 卸载 | 表示释放 → 数据删除 → 注销（每帧 ≤ `MaxUnloadsPerFrame`） | C |
| D 加载/生成 | 生成 → 安装 → 叠加改动集（每帧 ≤ `MaxLoadsPerFrame`） | D |
| E 编辑提交 | 提交 FIFO 待办编辑（§7.4） | E |
| F Mesh 构建 | 对 `Dirty` 且已加载的 Chunk 构建（`CollectDirtyChunks` 顺序） | F |
| G 表示提交 | 紧接构建：`Apply` → 成功则 `ClearDirty` → `Ready` | G |
| H 诊断 | 仅统计并写入 `TickReport`（只读，不改变世界/表示） | H |

* 顺序**不可交换**：C 先于 D（释放优先，避免瞬时双份）；E 先于 F（编辑在构建前落地，
  同一 Tick 内的编辑不会被本次构建忽略）；F/G 成对（§6.2）；
* **帧内幂等**：同一帧重复 `Tick` 不产生额外的生成/构建/呈递（状态与队列在帧内收敛后即稳定）；
* **`Tick` 无时间参数**：不接受 `deltaTime`；预算单位 = **条目数**（确定性优先）；
* 帧末必须满足 §1.4 帧末闭合与 §3.3 全部投影不变量。

---

## 12. 验收不变量（Runtime Core）

> 全部必须可用**纯 C#** 自动化验证（无场景、无 GameObject；用 FakePresenter 驱动），
> 与既有 `Assets/Editor/*Validation.cs` + `Tools/VoxelWorld/*` 的批处理风格一致。

| # | 不变量 | 验证方法 |
|---|---|---|
| **RC1** | **纯度**：源码零 UnityEngine 标识符、不访问 `Vertices/Normals/UVs` | 剔除注释后逐 token 扫描（RC-P1/P2）+ 扫描器自检 |
| **RC2** | **无场景依赖**：全流程在无场景/无 GameObject 下完成 | FakePresenter 跑完整 Tick 序列，全部断言通过（RC-P3） |
| **RC3** | **帧末闭合**：任一 Tick 返回后无 `Generating`/`Building`/`Unloading` | 每次 Tick 后遍历注册表断言状态集合 ⊆ `{Dirty, Ready, Failed}` |
| **RC4** | **注册表一致**：坐标唯一 + `world.ChunkCount == 记录数(chunk != null)` | 直接计数比较；重复加载后计数不变 |
| **RC5** | **加载集合正确**：帧末记录集 == 期望集合 ∪ 滞回保留区（独立重算，含负坐标/边界） | 按 §5.3 判定式独立重算并与注册表比较 |
| **RC6** | **预算守恒**：每帧处理数 ≤ 上限；未处理项不丢失 | 断言每帧计数上限 + 「队列变化 == 本帧处理数」 |
| **RC7** | **收敛确定（预算/顺序无关）**：不同预算与不同初始集合 ⇒ 相同数据与内容哈希 | 多组预算跑到全部 `Ready`，比较全量快照 + 每 Chunk 哈希 |
| **RC8** | **确定性（重复运行）**：同一 Tick 序列跑两次 ⇒ 逐 Tick 状态与报告完全一致 | 两次运行的报告序列逐项比较 |
| **RC9** | **无重复生成**：同一坐标只生成一次（重试除外，且重试后哈希一致） | 累计 `Generated` 计数 + 生成计数器探针 |
| **RC10** | **生成不覆盖改动**：改动集条目在（重）加载后仍生效 | 编辑 → 卸载 → 重载 → 读回目标坐标 == 改动值 |
| **RC11** | **卸载即净**：卸载后数据与表示都被释放，`LiveCount` 回基线 | N 次加载/卸载循环后 `world.ChunkCount == 0` 且 `LiveCount == 0` |
| **RC12** | **卸载-重载一致**：重载后该 Chunk 的内容哈希与卸载前一致 | 比较 `ComputeContentHash()` |
| **RC13** | **Meshing 只读**：构建前后 Chunk 全量快照不变（4096 体素 + `Coordinate` + `IsDirty`） | 快照比较 |
| **RC14** | **清脏纪律**：只用 `ClearDirty()` 且仅成功呈递后；源码不出现 `ClearDirtyFlags` | 源码扫描 + 「`Ready` ⟹ `!IsDirty`」持续断言 + 注入呈递失败后 `IsDirty` 仍为 `true` |
| **RC15** | **失败可观测且状态正确**：注入生成/构建/呈递失败 ⇒ 状态与原因符合 §9.1 | 失败注入 + 状态/原因断言 |
| **RC16** | **失败不破坏数据**：失败路径下 4096 体素快照不变（安装失败除外，此时重试后哈希仍一致） | 快照比较 + 重试后哈希比较 |
| **RC17** | **编辑前置条件**：未加载目标 ⇒ `Pending`（不创建 Chunk、不置脏）；超上限 ⇒ `Rejected` 且计数可见 | 断言 `ChunkCount` 不变 + 返回码 + 计数 |
| **RC18** | **可持久化最小集**：Core 暴露的可持久化状态 == `Seed + RuntimeCoreSettings + 改动集` | 结构检查：不含 Mesh/几何/表示/场景数据 |

---

## 13. 明确不做（Runtime Core V1 out of scope）

* 任何 Unity 类型与场景交互：GameObject / Mesh 资产 / MeshFilter / MeshCollider / Transform / 渲染 / 物理；
* View 的实现（`VoxelChunkView` / `UnityMeshBuilder` 属于 View 规范）；
* **编辑规则**：是否可挖、同值语义之外的判定、工具/权限/玩法（属于 Mining 与玩法层）；
* 命中/射线/目标解析（查询层独立规范）；
* 后台线程 / Jobs / Burst / 异步加载（Core V1 为主线程、同步、分帧）；
* 毫秒预算调度、优先级抢占、`VerticalMode.FocusRange`（V1 只用整列加载）；
* 跨 Chunk 剔除模式（Mesh §4 B3）、Greedy Meshing、顶点焊接、LOD、遮挡剔除；
* 洞穴/矿物/生物群系/结构等生成阶段（它们的产物只会改变 Voxel 数据，不改变 Core）；
* 存档的字节布局/文件格式/压缩/版本迁移（只定义内容契约，Runtime §7.1）；
* 网络同步、并发编辑、撤销/重做、多人。

---

## 14. 变更规则

1. 与上游冲突时的优先级：`VoxelWorld_Core.md`（真值）> `VoxelWorld_Mining.md`（修改）
   > `VoxelWorld_Mesh.md`（几何）> `VoxelWorld_Runtime.md`（调度）> 本文档（Core 内部结构）。
2. 本规范**只收紧、不放松**：§1.4（帧末闭合）、§6.2（构建-呈递配对）、§8.3（清脏纪律）、
   §5.2（安装路径）是对 Runtime 规范的**细化或收紧**；若上游未来放宽，必须先改上游再改本文档。
3. 任何修改若触及 §1（原则）、§9（失败语义）或 §12（不变量），必须：
   ① 升版本号；② 同步更新验收测试；③ 说明与既有存档（`Seed + 改动集`）的兼容性。
4. **禁止**用 Core 侧补丁（强制重建、跳过清脏、整体重置、静默重生成）掩盖数据层或 Meshing 缺陷。
5. V1 实现范围：**加载 → 生成 → 安装 → 脏 → 构建 → 呈递 → 卸载**闭环 + 改动集重载 + 失败/确定性条款；
   §13 的内容一律不得进入 V1。
6. 实现不得要求修改 VoxelCore / Generation / Mesh 的既有契约（WorldGeneration_Core §10）。

---

## 附录 A：类型与接口草案（纯 C#，实现阶段按此落地）

```csharp
namespace VoxelWorld.Runtime      // Assets/Scripts/VoxelWorld/Runtime/
{
    public enum ChunkLifecycleState { Generating, Dirty, Building, Ready, Unloading, Failed }

    public enum PresenterOutcome { Presented, Empty, Failed }

    public interface IChunkPresenter      // Core 定义；由 VoxelWorld.View 实现
    {
        PresenterOutcome Apply(VoxelCoordinate coordinate, VoxelMeshData meshData, out string error);
        void Release(VoxelCoordinate coordinate);
        int LiveCount { get; }
    }

    public enum EditSubmissionOutcome { Applied, Unchanged, Pending, Rejected }

    public enum OutOfRangePolicy { Reject, DemandLoad }

    public sealed class RuntimeCoreSettings
    {
        public int ViewDistanceChunks;
        public int UnloadHysteresisChunks;
        public int VerticalChunkCount;
        public int MaxChunkPairsPerFrame;   // 构建 + 呈递（配对）
        public int MaxLoadsPerFrame;
        public int MaxUnloadsPerFrame;
        public int MaxPendingEdits;
        public int MaxRetriesPerChunk;
        public OutOfRangePolicy OutOfRangePolicy;

        public bool IsValid(out string reason);
        public bool IsWithinUnloadRange(VoxelCoordinate focus, VoxelCoordinate chunk);  // R_unload 判定
        public bool IsWithinLoadRange(VoxelCoordinate focus, VoxelCoordinate chunk);     // R_load 判定
    }

    public sealed class RuntimeCoreTickReport     // 只读快照（H 阶段产出）
    {
        public int TickIndex;
        public int LoadedChunkCount;
        public int DirtyChunkCount;
        public int ReadyChunkCount;
        public int FailedChunkCount;
        public int LoadedThisTick;
        public int UnloadedThisTick;
        public int BuiltPresentedThisTick;
        public int PendingEditCount;
    }

    public sealed class VoxelRuntimeCore
    {
        public VoxelRuntimeCore(
            VoxelWorld world, IWorldGenerator generator, IChunkPresenter presenter,
            RuntimeCoreSettings settings);                 // 全部显式注入，无隐式查找

        public RuntimeCoreTickReport Tick(VoxelCoordinate focusChunk);   // 无时间参数

        public EditSubmissionOutcome SubmitEdit(VoxelCoordinate world, VoxelType type, out string reason);

        public void RetryFailedChunks();                        // 显式重试入口

        public IList<VoxelCoordinate> GetLoadedCoordinates();   // 排序（只读）
        public IList<VoxelCoordinate> GetFailedCoordinates();   // 排序（只读）
        public bool TryGetSnapshot(VoxelCoordinate chunk, out ChunkRecordSnapshot snapshot);
        public RuntimeChangeSet ChangeSet { get; }              // 最小可持久化状态
    }
}
```

**附录 A 禁令（可扫描验证）**：以上类型与实现中不得出现
`UnityEngine` / `UnityEditor` / `Vector2` / `Vector3` / `Bounds` / `GameObject` / `Transform` /
`Mesh` / `Material` / `Time` / `Random` / `ClearDirtyFlags` / `new VoxelChunk` / `.Vertices` / `.Normals` / `.UVs`。

## 附录 B：与已有规范的一致性映射

| 本文档条款 | 上游依据 | 关系 |
|---|---|---|
| §1.1 纯度（RC-P1…P4） | Runtime §2（Runtime Core 子层：禁止 Unity 类型） | 细化：给出可机械验证的判据 |
| §1.4 帧末闭合 | Runtime §5.2（每个 Chunk 原子完成） | **收紧**：把"原子"升级为"Tick 返回时无瞬态状态" |
| §3 生命周期 | Runtime §4（状态表）/ §8.1 | 一致：状态名与转移完全相同，补 §3.2/§3.3 可断言形式 |
| §4 注册表 | Runtime §2.1（所有权表）/ §5.1（确定性排序）/ R4/R6 | 细化：记录字段、注册时机、计数一致不变量 |
| §5 Generation | WorldGeneration_Core §2/§3/§6/§9；IWorldGenerator 契约 | 一致 + 补"冻结 VoxelCore 下的安装路径"（§5.2） |
| §5.4 改动集 | Runtime §7.1/§7.2；Mining §6（同值语义） | 一致：稀疏、覆盖式、叠加在生成之后 |
| §6 Mesh 调用 | Mesh §1.2（只读）/ §5（输出可丢弃）/ I1；Runtime §5 F/G | 一致 + **收紧**：构建与呈递同 Tick 配对（§6.2） |
| §7 View 调用顺序 | View §8.1（三态结果）/ §5.5/§5.6/§9.1；Runtime §8.1 | 一致：三态结果、原子替换、Release 顺序 |
| §7.4 编辑前置条件 | Runtime §5.3（三态表）；Mining §2/§5 | 一致：Pending/Rejected 可观测，规则仍属 Mining |
| §8.3 清脏纪律 | Runtime §7.4；Mesh §6.3；Mining §5.3；VoxelCore 脏语义 | **收紧**：禁止 `ClearDirtyFlags`，只允许成功呈递后 `ClearDirty` |
| §8.5 对账 | Runtime §1（表示不超前）/ §7.2（生成+叠加） | 新增：闭合"第二写入源"导致的不一致 |
| §9 失败处理 | Runtime §9.1；Mining §5；Mesh §6.3 | 一致：保留脏、禁止静默、禁止隐式重生成 |
| §10 确定性 | Runtime §1.4/§10 R4/R14；WorldGeneration_Core §2；Mesh §1.3 | 一致 + 补：整数判定、单调 Tick 序号、证据清单 |
| §12 RC14/RC17/RC18 | Runtime R9/R10/R12/R13；Mining M3/M7/M11 | 一致：Core 侧的落地形式 |
