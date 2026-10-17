# Voxel World Runtime Core Implementation Specification V1

> **文档性质**：本文档**不是架构设计**，而是把 `VoxelWorld_Runtime.md`（策略）、
> `VoxelWorld_Runtime_Core.md`（结构）与 `VoxelWorld_Runtime_Interface.md`（抽象）
> 已经确定的规则**具体化为可照着写代码的 V1 实现约束**：
> 字段清单、时机、阶段输入输出、失败分支、禁止事项、验收不变量。
> **不新增架构概念、不改变依赖方向、不改变任何上游不变量。**
>
> 上游规范（实现必须同时满足；冲突时按下列顺序取权威）：
> * `Specifications/World/VoxelWorld_Core.md` —— 世界真值（Voxel Data）与 Chunk 数据契约；
> * `Specifications/World/WorldGeneration_Core.md` —— 初始状态生成（Seed + ChunkCoordinate → VoxelChunk）；
> * `Specifications/World/VoxelWorld_Mesh.md` —— 几何数据契约（Meshing 只读 / 输出可丢弃 / 不变量）；
> * `Specifications/World/VoxelWorld_View.md` —— 表示层（对象模型 / 资产 / 释放 / 三态结果）；
> * `Specifications/World/VoxelWorld_Runtime.md` —— 调度、生命周期、流式加载、存档边界；
> * `Specifications/World/VoxelWorld_Runtime_Core.md` —— Runtime Core 的纯 C# 结构、阶段、纪律与不变量；
> * `Specifications/World/VoxelWorld_Runtime_Interface.md` —— 抽象层、依赖倒置、接口五要素与所有权。
>
> 本文档只做两种动作：**具体化**（把抽象规则落成字段/时机/序列）与**收窄**（把 V1 不做的部分显式排除）。
> 每一处收窄都在 §附录 B 登记，不允许"沉默偏离"。

---

## 1. 文档定位与范围

### 1.1 四层规则的分工

| 层 | 文档 | 回答的问题 |
|---|---|---|
| 策略 | `VoxelWorld_Runtime.md` | 什么时候加载/卸载、脏怎么排空、存档边界 |
| 结构 | `VoxelWorld_Runtime_Core.md` | Core 有哪几类对象、状态机、阶段 A–H、纪律、不变量 |
| 抽象 | `VoxelWorld_Runtime_Interface.md` | Core 与 Generation/Mesh/View 之间的接口、依赖倒置、所有权 |
| **实现约束（本文档）** | `VoxelWorld_Runtime_Core_Implementation.md` | **照着写代码**：每个字段、每个时机、每阶段输入输出、每条失败分支 |

### 1.2 V1 收窄声明（必须显式）

V1 的"加载集合"来源是**手动 Chunk 请求**，**不是**相机/玩家距离：

| 上游规则 | V1 实现 |
|---|---|
| Runtime §6 流式加载（`R_load` / `R_unload` / 滞回） | **不实现**（§7.2） |
| Runtime_Core §5.3 加载集合判定式 `dx² + dz² ≤ R_load²` | **不实现**；加载集合 = 显式请求集合 ∩ 垂直有效范围（§4.2） |
| Runtime_Core §11 阶段 A「焦点解析」（`Tick(focusChunk)`） | **不实现**；V1 为 **`Tick()` 无参**（请求集合即真相） |
| Runtime_Core §5.4 改动集 / §附录A `SubmitEdit` | 改动集**容器与叠加步骤保留**（安装流程的一环），但**编辑入口不实现**（属 Mining，V1 不做）⇒ V1 中改动集恒为空 |
| Runtime_Core §8.5 对账 | **实现**（每 Tick 一次，成本 O(已加载)） |

> V2 恢复距离加载时，**只能新增** `Tick(VoxelCoordinate focusChunk)` 重载与 §5.3 判定式，
> 不得改动本文档确定的阶段序、状态机与字段（Runtime_Interface §13.2：只能新增，不能改签名）。

### 1.3 通用实现规则

1. **不改上游**：不得修改 VoxelCore / Generation / Mesh / View 的既有签名与语义（Runtime_Core §14.6）；
2. **只新增**：新增类型只能落在 `Runtime/`、`Runtime/Abstractions/`、`Runtime/Adapters/`、`View/`（Runtime_Interface §2.3 的四段扫描范围）；
3. **纯 C# Core**：`Runtime/` 下不得出现 UnityEngine 标识符、不得 `new VoxelChunk`、不得 `new` 任何实现（Runtime_Core §1.1、Runtime_Interface §2.3）；
4. **单线程同步**：所有接口调用在主线程、同步、串行（Runtime_Interface §7 ID-2）；
5. **失败用返回值**：可预期失败一律通过返回值 + `out string error` 表达（Runtime_Interface §7 ID-1）；
6. **阶段序不可交换**：A→B→C→D→E→F→G→H（Runtime_Core §11，本文档 §4）。

---

## 2. ChunkRecord 实现规范

### 2.1 定义与定位

**ChunkRecord 是 Runtime Core 的管理对象，不是世界真值。**

* 它只记录"这个坐标在本帧调度中处于什么状态、失败了几次、指向哪个世界 Chunk"；
* 它**不拥有**体素数据、**不复制**体素数据、**不是**任何几何或表示；
* 删除全部 ChunkRecord 后，`VoxelWorld` 中的体素数据必须一字不变（RCI7）；
* 世界真值只有一个来源：`VoxelWorld` + `VoxelChunk`（VoxelWorld_Core §1）。

### 2.2 字段（V1 完整清单）

| 字段 | 类型 | 含义 | 可变 |
|---|---|---|---|
| `Coordinate` | `VoxelCoordinate` | Chunk 坐标（注册表键，值语义；`Equals`/`GetHashCode`/`==` 已由 VoxelCore 提供） | **只读**（`readonly`） |
| `State` | `ChunkLifecycleState` | 生命周期状态（§2.6） | 可变（仅 Core 内部） |
| `Chunk` | `VoxelChunk`（引用） | **世界拥有**的 Chunk 引用；Core 不 `new`、不 Dispose、不绕过 `VoxelWorld` API | 可变 |
| `Mesh` | `VoxelMeshData`（引用） | 本 Tick 构建出的负载（纯数据、一次性）；呈递成功或失败后即可置 `null` | 可变 |
| `FailureReason` | `string` | 最近一次失败原因（**禁止为空字符串**；无失败时为 `null`） | 可变 |
| `FailureCount` | `int` | 该坐标累计失败次数（超过 `MaxRetriesPerChunk` ⇒ 停止自动重试） | 可变 |
| `RegisteredTick` | `int` | 注册时的 `TickIndex`（Core 自有单调序号，**不是时间**） | 只读 |
| `StateChangedTick` | `int` | 最近一次状态变化的 `TickIndex` | 可变 |
| `NeedsReinstall` | `bool` | 是否需要重新执行"生成 + 安装"（`Failed(chunk == null)` 或被 `RetryFailedChunks()` 重置时为 `true`） | 可变 |

**字段禁令**：记录中**不得**出现体素数组、`Vector2/Vector3`、Mesh 资产、GameObject、
材质、场景引用、`Task`/线程对象、时间戳（`DateTime`）。负载 `Mesh` 是唯一"携带 Unity 结构体类型"的字段，
且只能作为**不透明引用**传递（Runtime_Interface §3.3）。

### 2.3 所有权

| 项 | 所有者 | 说明 |
|---|---|---|
| `ChunkRecord` 对象本身 | **Runtime Core** | 由 Core 创建、修改、删除；对外只给只读快照 |
| 记录里的 `Chunk` 字段 | 引用而已，**所有权在 `VoxelWorld`** | Core 通过 `GetOrCreateChunk` / `RemoveChunk` 间接管理 |
| 记录里的 `Mesh` 字段 | **Runtime Core（临时）** | 一次性负载，Tick 内用完即置 `null`（Runtime_Interface §5.6） |
| 注册表容器 | Runtime Core | 私有；对外只暴露排序快照（Runtime_Core §4.3-3/4） |

### 2.4 创建时机

**唯一时点：D 阶段（生成）处理某个坐标时，先生成"Tick 内部占位记录"（`State = Generating`），
该占位记录在安装成功之前不进入权威注册表。**

```
D 阶段开始  → 创建占位记录（State=Generating，RegisteredTick=当前 TickIndex）
Generate    → Validate（输出自洽校验）
Install     → 搬运 + 叠加改动集 + 收尾置脏
安装成功    → 占位记录写入注册表（State=Dirty）        ← 权威注册表在此刻"注册"
安装/生成失败→ 占位记录转为 Failed 记录后写入注册表（保留以便诊断与重试）
```

> **细化登记**：Runtime_Core §4.2 表述为"注册发生在 `Generating` 开始时"。
> 本实现规范**收紧**为"注册表可见性在安装成功后建立"，以满足
> "生成过程中不得污染注册表"（§5.2）。`Generating` 状态**仍然**从 D 阶段开始存在
> （以占位记录表示），语义与 Runtime_Core §3.1（Tick 内部瞬态）完全一致。

### 2.5 销毁时机

| 时点 | 触发 | 说明 |
|---|---|---|
| **C 阶段（卸载）** | 显式 `ReleaseChunk(coord)` 使该坐标离开请求集合 | 顺序固定：`presenter.Release` → `world.RemoveChunk` → 删除记录（Runtime_Core §7.1） |
| **`Clear()`**（可选 V1 工具方法） | 显式清空（测试/重置） | 必须逐个走卸载流程（不得直接丢指针：否则表示侧残留） |
| ~~Tick 自动淘汰~~ | — | **V1 不实现**（无距离卸载） |

**`Failed` 记录不自动销毁**：它保留在注册表中直到该坐标被显式 `ReleaseChunk`（便于诊断与显式重试，
Runtime_Core §4.2）。

### 2.6 生命周期状态

沿用 Runtime_Core §3.1 的六个状态与命名（**不得改名、不得新增状态**）：

| 状态 | 是否进权威注册表 | 是否可跨 Tick | 含义 |
|---|---|---|---|
| `Generating` | 否（占位记录） | **否**（Tick 内瞬态） | 正在生成 + 校验 + 安装 |
| `Dirty` | 是 | 是 | 数据就绪且 `IsDirty == true`（表示缺失或过期） |
| `Building` | 是（允许写入以便诊断） | **否**（Tick 内瞬态） | 正在 Meshing + 呈递（成对） |
| `Ready` | 是 | 是 | 表示与当前数据一致（`IsDirty == false`） |
| `Unloading` | 否（Tick 内瞬态，正在删除） | **否** | 正在释放表示与数据 |
| `Failed` | 是 | 是 | 生成/安装/Meshing/呈递失败且已达重试判据上限 |

**帧末闭合（RCI9）**：任一 `Tick()` 返回时，注册表中**不存在** `Generating` / `Building` / `Unloading`
（Runtime_Core §1.4）。

### 2.7 状态转移合法性

**唯一允许的转移**（表外转移一律视为实现缺陷）：

| # | From | To | 触发条件 | 备注 |
|---|---|---|---|---|
| T1 | （无记录） | `Generating` | D 阶段开始处理该坐标 | 占位记录，不进注册表 |
| T2 | `Generating` | `Dirty` | 安装成功 + 收尾保证脏 | 注册表注册 + 需呈递 |
| T3 | `Generating` | `Failed` | Generate/Validate 失败（世界未被触碰，`Chunk == null`） | 保留以便重试与诊断 |
| T4 | `Generating` | `Failed` | Install 中途失败（可能部分写入，`Chunk != null`） | 重试必须全量写入（§5.3-R2） |
| T5 | `Dirty` | `Building` | F 阶段被调度（可为 Tick 内瞬态标记） | 必须在同 Tick 内离开 |
| T6 | `Building` | `Ready` | `Apply` 成功（`Presented` 或 `Empty`）+ `ClearDirty` | 唯一清脏路径 |
| T7 | `Building` | `Dirty` | Mesher 失败 / Mesher 异常 / `Apply` 返回 `Failed` | `FailureCount++`；不清脏 |
| T8 | `Building` | `Failed` | 同上且 `FailureCount > MaxRetriesPerChunk` | **仍保持 `IsDirty == true`** |
| T9 | `Ready` | `Dirty` | 数据实际变化（V1 无编辑入口 ⇒ 由对账发现外部写入） | 投影修正（Runtime_Core §8.5） |
| T10 | `Failed` | `Generating` | 重试且 `NeedsReinstall == true`（`Chunk == null` 或需重装） | 走完整 Generate→Validate→Install |
| T11 | `Failed` | `Dirty` | 重试且 `NeedsReinstall == false`（数据完整，仅表示缺失） | 只需重建表示（Runtime_Core §3.2） |
| T12 | `Dirty` / `Ready` / `Failed` | `Unloading` | C 阶段处理该坐标 | 立即完成，随即删除记录 |
| T13 | `Unloading` | （删除记录） | 释放完成 | 等价于 Runtime_Core §3.1 的 `Absent` |

**重试判据**：

```
可重试 ⟺ FailureCount ≤ MaxRetriesPerChunk
NeedsReinstall = (Chunk == null) || 上次失败发生在 Generate/Validate/Install 阶段
超过上限 ⇒ 停止自动重试（状态保持 Failed），只能由 RetryFailedChunks() 归零 FailureCount 后恢复
```

**禁止的转移（举例）**：

* `Ready → Building`（不经 `Dirty`：等于承认存在过期表示，Runtime_Core §3.2）；
* `Dirty → Ready`（不经 `Building`：等于声称表示已与数据一致，但从未呈现）；
* `Generating → Ready`（跳过安装与呈递）；
* `Failed → Ready`（跳过重试与呈递）；
* 任何**跨 Tick** 停留在 `Generating` / `Building` / `Unloading`。

### 2.8 与 VoxelCore 的投影不变量（每次 Tick 末尾断言，H 阶段检查）

1. `State == Ready` ⟺ `Chunk != null && !Chunk.IsDirty`；
2. `State ∈ {Dirty, Building}` ⇒ `Chunk != null && Chunk.IsDirty == true`；
3. `State ∈ {Dirty, Building, Ready}` ⇒ `Chunk != null`；`Failed` 允许 `Chunk == null`；
4. `State == Failed` ⇒ `FailureReason` 非空；
5. `world.ChunkCount == 注册表中 Chunk != null 的记录数`（Core 不持有世界不承认的 Chunk，也不允许世界里有无主 Chunk）。

---

## 3. VoxelRuntimeCore 类规范

### 3.1 职责（只有三项）

| # | 职责 | 具体内容 |
|---|---|---|
| **R1** | **Chunk 注册表管理** | 记录的创建/注册/更新/删除；坐标唯一；对外只读排序快照；对账（Runtime_Core §8.5） |
| **R2** | **Tick 调度** | 固定 A–H 阶段序、每帧上限、阶段内排序、帧末闭合、状态机转移、重试判据、诊断报告 |
| **R3** | **Generation / Mesh / View 编排** | 按 §5/§6 的时序调用三个接口；把接口结果翻译成状态转移与脏处理；保证配对与顺序 |

### 3.2 禁止（四类，可扫描验证）

| # | 禁止 | 判定方式 |
|---|---|---|
| **P1** | **生成规则**：不得包含任何地形/高度/噪声/体素类型判定逻辑；不得复制 `HeightFunction`/`BasicTerrainGenerator` 的算法 | 源码扫描：不出现 `HeightFunction` / `BasicTerrainGenerator` / `HeightFunction.GetTerrainHeight`；无 `VoxelType` 判定表 |
| **P2** | **Mesh 规则**：不得包含构面/顶点/索引/子网格/UV/法线逻辑；不得加工、裁剪、合并几何 | 源码扫描：不出现 `VoxelMeshGenerator`；不访问 `.Vertices` / `.Normals` / `.UVs`（Runtime_Core §1.1 RC-P2） |
| **P3** | **View 细节**：不得创建/销毁 GameObject、Mesh 资产、Collider；不得引用 `VoxelChunkView` / `UnityMeshBuilder`；不得查询表示内部 | 源码扫描：`Runtime/` 下不出现 `UnityEngine` / `MonoBehaviour` / `GameObject` / `Mesh` / `VoxelChunkView`；只通过 `IChunkPresenter` |
| **P4** | **Unity 生命周期**：不得实现 `MonoBehaviour`、不得用 `Awake/Start/Update/Coroutine`、不得读 `Time.deltaTime` | 源码扫描：无 `MonoBehaviour` / `IEnumerator` / `Time.` / `Coroutine` |

### 3.3 可持有的抽象（构造注入，不依赖具体实现）

```csharp
public VoxelRuntimeCore(
    VoxelWorld world,                 // 真值容器（组装点拥有；Core 不 Dispose、不 new）
    IWorldGenerator generator,        // Generation 抽象（Runtime_Interface §4）
    IChunkMesher mesher,              // Mesh 抽象（Runtime_Interface §5）
    IChunkPresenter presenter,        // View 抽象（Runtime_Interface §6）
    RuntimeCoreSettings settings);    // 显式参数（Runtime_Core §2.3）
```

**构造期校验**（全部失败即抛 `ArgumentException`，禁止运行中静默修正）：

1. 五个参数非 `null`；`settings.IsValid(out reason)` 通过；
2. 不在此处调用任何接口方法（构造期不得产生副作用；不得探测实现类型、不得 `as`/`is` 具体类型）。

**禁止**：`new BasicTerrainGenerator(...)` / `new VoxelMeshGeneratorMesher()` / `new ChunkViewPresenter(...)` /
`FindObjectOfType` / `Camera.main` / 静态单例 / 服务定位器（Runtime_Interface §2.4）。

### 3.4 V1 公开 API（完整清单）

| API | 语义 | 幂等 | 失败 |
|---|---|---|---|
| `bool RequestChunk(VoxelCoordinate chunkCoordinate)` | 加入请求集合（当前加载集合的来源）；`y ∉ [0, VerticalChunkCount)` ⇒ 拒绝并计数 | 是（重复请求返回 `false`，不重复生成） | 非法请求返回 `false` + `RejectedRequests++` |
| `bool ReleaseChunk(VoxelCoordinate chunkCoordinate)` | 从请求集合移除；若已加载 ⇒ 下一次 Tick 的 C 阶段卸载 | 是 | 不存在该请求 ⇒ 返回 `false` |
| `void ClearRequests()` | 清空请求集合（不直接删数据；数据在后续 Tick 由 C 阶段释放） | 是 | — |
| `RuntimeCoreTickReport Tick()` | 执行一次完整 A–H 调度（同步、无参、无时间） | 帧内幂等（重复 Tick 不产生额外生成/构建/呈递） | 内部失败转为状态/计数，不抛异常 |
| `void RetryFailedChunks()` | 归零全部 `Failed` 记录的 `FailureCount`，恢复自动重试 | 是 | — |
| `IList<VoxelCoordinate> GetRequestedCoordinates()` | 请求集合快照（`(x,y,z)` 升序） | — | — |
| `IList<VoxelCoordinate> GetLoadedCoordinates()` | 已加载坐标快照（升序） | — | — |
| `IList<VoxelCoordinate> GetFailedCoordinates()` | 失败坐标快照（升序） | — | — |
| `bool TryGetSnapshot(VoxelCoordinate c, out ChunkRecordSnapshot s)` | 只读记录快照 | — | 未注册 ⇒ `false` |
| `RuntimeCoreDiagnostics Diagnostics { get; }` | 累计计数（只读） | — | — |

**API 禁令**：V1 **不提供** `SubmitEdit` / `Mine` / `Place` / `Save` / `Load` / `Tick(float dt)` /
`Tick(VoxelCoordinate focusChunk)`（前者属 Mining/存档，后者属 V2 距离加载）；
不得暴露可变记录、不得暴露注册表容器。

### 3.5 内部状态（只有四类）

| 内部状态 | 类型 | 说明 |
|---|---|---|
| 注册表 | `Dictionary<VoxelCoordinate, ChunkRecord>` | 唯一权威记录集（§2） |
| 请求集合 | `HashSet<VoxelCoordinate>` | 手动请求（V1 的加载集合来源） |
| 改动集 | `RuntimeChangeSet`（坐标 → 类型） | **V1 恒为空**（无编辑入口）；保留以承载 Runtime_Core §5.2-4 的叠加步骤 |
| 计数器 | `RuntimeCoreDiagnostics` + `TickIndex` | 累计诊断 + 单调 Tick 序号（**不是时间**） |

**无第二份真值**：不得缓存体素副本、不得缓存世界的另一份集合、不得缓存"上次已加载集合"作为决策依据
（请求集合重算 + 排序快照即可；Runtime_Core §10.2）。

---

## 4. Tick 流程实现规则

### 4.0 固定阶段序与工作集快照

```
Tick()
 ├ A 更新请求集合        （定格本帧工作集）
 ├ B 计算目标 Chunk 集合  （目标集合 + 差异集合 + 对账）
 ├ C 卸载                （≤ MaxUnloadsPerFrame）
 ├ D 生成                （≤ MaxLoadsPerFrame；Generate→Validate→Install→Register）
 ├ E 编辑提交预留         （V1 空操作，位置保留）
 ├ F Mesh 构建           （≤ MaxChunkPairsPerFrame；Dirty → meshData）
 ├ G View 呈现           （与 F 配对；成功清脏 → Ready）
 └ H 诊断                （只读：报告 + 不变量断言）
```

**工作集快照规则（确定性关键）**：

1. A 阶段把请求集合定格为**升序不可变列表** `requestedThisTick`；B 阶段据此产出
   `loadTargets` / `unloadTargets`（同样升序**不可变列表**）；
2. C–G 各阶段只消费这些列表，**不得**在阶段中途重新读取请求集合或注册表来决定"下一个是谁"；
3. 阶段内处理顺序 = 列表顺序（`(x, y, z)` 升序），**与 `Dictionary`/`HashSet` 遍历顺序无关**；
4. 每帧上限只限制"本帧执行多少个"，**不改变列表顺序**（预算 ⟂ 顺序）。

### 4.1 A：更新请求集合

| 项 | 内容 |
|---|---|
| **输入** | `requestSet`（`RequestChunk`/`ReleaseChunk` 的当前状态）+ 上一 Tick 的注册表 |
| **输出** | `requestedThisTick`：升序不可变坐标列表（本帧唯一工作集） |
| **状态变化** | **无**（只读快照；不创建/不删除记录、不触碰世界、不触碰表示） |
| **失败处理** | 无失败分支（纯计算）；请求集合中的非法坐标已在 `RequestChunk` 时被拒绝并计数 |

**规则**：A 阶段**不**做任何加载/卸载判定（判定属 B）；A 阶段**不得**调用任何接口。

### 4.2 B：计算目标 Chunk 集合

| 项 | 内容 |
|---|---|
| **输入** | `requestedThisTick`、注册表、`RuntimeCoreSettings.VerticalChunkCount`、`world`（用于对账） |
| **输出** | `targetSet`（请求集合 ∩ 垂直有效范围）、`loadTargets`（`targetSet − 注册表`，升序）、`unloadTargets`（`注册表 − targetSet`，升序）、对账结果 |
| **状态变化** | **对账**：① 世界里有 Chunk 但无记录 ⇒ 补登记（`Dirty`，保留数据，`NeedsReinstall = true`）；② 记录有 `Chunk` 但世界里已无 ⇒ 置 `Failed(DataMissing)`；③ `Ready` 但 `IsDirty == true` ⇒ 投影回 `Dirty`。**不修改任何体素数据** |
| **失败处理** | 无失败分支（纯计算 + 投影）；对账发现的不一致转为上述状态（可观测、不静默） |

**规则**：`y ∉ [0, VerticalChunkCount)` 的请求**不进入** `targetSet`（WorldHeight 之外不加载，Runtime §6.4）；
`Failed` 且 `FailureCount ≤ MaxRetriesPerChunk` 的坐标**视为需要加载**（进入 D 阶段）；
`Failed` 且超上限的坐标**跳过**（需 `RetryFailedChunks()` 恢复）。

### 4.3 C：卸载

| 项 | 内容 |
|---|---|
| **输入** | `unloadTargets`（升序）+ `MaxUnloadsPerFrame` |
| **输出** | 本帧卸载坐标列表（顺序 = 列表顺序）；`UnloadedThisTick` 计数 |
| **状态变化** | 逐坐标（**整块完成**）：`State = Unloading` → `presenter.Release(coord)` → `world.RemoveChunk(coord)` → **删除记录** |
| **失败处理** | 见下 |

**规则**：

1. **顺序固定**：释放表示 → 删除数据 → 删除记录（Runtime_Core §7.1：表示先于数据）；
2. **`Release` 抛异常**：防御性捕获 ⇒ 计数 `ReleaseFailures++` + 记录原因；**仍必须**执行
   `RemoveChunk` 与删除记录（数据释放不能被表示层失败阻塞）；
   > **细化登记**：Runtime_Core §9.1 表述为"仍记为 `Failed`"。卸载的目的是移除该坐标，
   > 没有可承载 `Failed` 状态的记录 ⇒ 实现改为"计入 `ReleaseFailures` 计数 + 原因日志"。
   > 不变量"失败可观测"仍然满足。
3. 每个坐标必须**整块完成**（不允许"释放了一半就下一帧继续"）；
4. `Release` 对**没有数据**的坐标（`Failed` + `Chunk == null`）也必须调用（Runtime_Core §7.2-4）；
5. 卸载阶段**不得**触发生成、不得修改脏标记、不得调用 `Apply`。

### 4.4 D：生成

| 项 | 内容 |
|---|---|
| **输入** | `loadTargets`（升序）+ `MaxLoadsPerFrame` + `RuntimeChangeSet`（V1 恒为空） |
| **输出** | 本帧加载坐标列表；`LoadedThisTick` 计数；新记录（`Dirty`）或 `Failed` 记录 |
| **状态变化** | 逐坐标**整块完成**：创建占位记录（`Generating`）→ Generate → Validate → Install → Register（`Dirty`）；任一步失败 ⇒ `Failed` 记录 |
| **失败处理** | 见 §5（Generate/Validate/Install 三段；世界污染规则见 RCI1） |

**规则**：

1. 每个坐标必须**整块完成**（Runtime_Core §1.4）；本帧预算不够 ⇒ **不开始**该坐标；
2. 处理顺序 = 列表顺序（升序）；**不得**重排（例如"先处理就绪的"）；
3. 已在注册表（`Dirty`/`Ready`/`Building`）的坐标**不得**重复生成（RCI8-R3）；
4. `Failed` + `NeedsReinstall == true` + 可重试 ⇒ 走完整 Generate→Validate→Install（T10）；
5. `Failed` + `NeedsReinstall == false`（数据完整，仅表示缺失）⇒ **不重新生成**，直接置 `Dirty`（T11）；
6. D 阶段**不得**调用 `IChunkMesher` 或 `IChunkPresenter`。

### 4.5 E：编辑提交预留

| 项 | 内容 |
|---|---|
| **输入** | （V1）无 |
| **输出** | （V1）无 |
| **状态变化** | **无**（V1 空操作） |
| **失败处理** | 无 |

**规则**：

1. E 阶段**必须存在于阶段序中**且**不得删除**（保证 A–H 顺序与 Runtime_Core §11 稳定，V2 接入 Mining 时不改变顺序）；
2. V1 **不提供**编辑入口（`SubmitEdit` 不在 V1 API，§3.4），因此 E 阶段无输入、无副作用；
3. V1 的改动集恒为空；**但安装流程中的"叠加改动集"步骤必须实现**（Runtime_Core §5.2-4），
   这样 V2 接入 Mining 时无需改动安装流程；
4. E 阶段**不得**触碰脏标记、不得调用任何接口。

### 4.6 F：Mesh 构建

| 项 | 内容 |
|---|---|
| **输入** | 注册表中 `State == Dirty` 且 `Chunk != null` 的记录（按坐标升序）+ `MaxChunkPairsPerFrame` |
| **输出** | 本帧"构建 + 呈递"配对列表；每个坐标的 `VoxelMeshData` 负载（同 Tick 临时） |
| **状态变化** | `Dirty → Building`（允许写入以便诊断，必须同 Tick 离开） |
| **失败处理** | `mesher.TryBuild` 返回 `false` 或抛异常 ⇒ `FailureCount++` + `FailureReason` + **保持/回到 `Dirty`**（超上限 ⇒ `Failed`），**不调用 `Apply`**、**不清脏** |

**规则**：

1. 构建顺序 = `CollectDirtyChunks`（VoxelCore 已按 Chunk 坐标排序）∩ 已加载集合，再按升序截取前 N；
2. 每 Chunk 每 Tick **至多 1 次** `TryBuild`；
3. **只读性**：构建前后不得修改 `Chunk`（RCI12）；
4. 负载是**不透明**的：Core 不得访问 `.Vertices` / `.Normals` / `.UVs`（Runtime_Interface §3.3）；
5. F 阶段**不得**触碰表示（`Apply` 只在 G 阶段）。

### 4.7 G：View 呈现

| 项 | 内容 |
|---|---|
| **输入** | F 阶段成功产出的 `(coordinate, meshData)` 配对（同一 Tick、紧接其后） |
| **输出** | `PresenterOutcome` 三态；`PresentedThisTick` / `EmptyPresentedThisTick` 计数 |
| **状态变化** | 成功（`Presented` / `Empty`）⇒ `Chunk.ClearDirty()` ⇒ `State = Ready`；失败 ⇒ `State = Dirty`（不清脏） |
| **失败处理** | `Apply` 返回 `Failed` 或抛异常 ⇒ `FailureCount++` + 原因 + 回 `Dirty`（超上限 ⇒ `Failed`）；旧表示由实现保证不变 |

**规则**：

1. **只有** `Apply` 成功后才允许 `ClearDirty()` —— 这是全代码库中**唯一**的清脏位置（RCI10）；
2. **禁止** `VoxelWorld.ClearDirtyFlags()`（Runtime_Core §8.3-2）；
3. `meshData.HasGeometry == false` ⇒ 期望 `Empty`；否则期望 `Presented`；不一致 ⇒ `Failed(OutcomeMismatch)` + 计数（Runtime_Interface §6.3）；
4. 每 Chunk 每 Tick **至多 1 次** `Apply`；
5. G 阶段结束后立即把 `record.Mesh = null`（负载一次性；Runtime_Interface §5.6/§6.6）；
6. `Apply` 失败**不得**调用 `Release`（旧表示必须保持，Runtime_Core §7.2-5）。

### 4.8 H：诊断

| 项 | 内容 |
|---|---|
| **输入** | 本帧全部阶段的计数与状态 |
| **输出** | `RuntimeCoreTickReport`（只读快照） |
| **状态变化** | **无**（只读；不得修改世界、注册表、请求集合、脏标记、表示） |
| **失败处理** | 不变量断言失败 ⇒ 记入 `InvariantViolationCount` + 原因（**不抛异常、不改变状态**），由验收层判定为缺陷 |

**H 阶段必须检查**（§2.8 五条投影不变量 + 帧末闭合）：

```
1) Ready ⟺ (Chunk != null && !IsDirty)
2) Dirty|Building ⇒ (Chunk != null && IsDirty)
3) Dirty|Building|Ready ⇒ Chunk != null
4) Failed ⇒ FailureReason 非空
5) world.ChunkCount == 记录数(Chunk != null)
6) 帧末状态 ⊆ {Dirty, Ready, Failed}（无 Generating/Building/Unloading）
```

### 4.9 阶段间禁止事项

1. **不得重排**阶段；**不得跳过**阶段（E 即使空操作也必须执行）；
2. **不得跨阶段继续**：某坐标在某阶段失败 ⇒ 本帧不再处理该坐标（不进入后续阶段）；
3. **不得在阶段内重新求解**工作集（§4.0）；
4. **不得让接口互相调用**（只有 Core 编排，Runtime_Interface §8-6）；
5. **不得在一次 Tick 内对同一坐标执行两次** `Generate` / `TryBuild` / `Apply` / `Release`；
6. H 阶段之后**不得**再有状态变化（Tick 返回值必须与仪表一致）。

---

## 5. Generation 安装流程

### 5.1 四步（唯一允许的顺序）

```
Generate()  →  Validate  →  Install  →  Register
```

| 步 | 输入 | 输出 | 失败处理 |
|---|---|---|---|
| **Generate** | `Coordinate`（+ 生成器实例上的显式参数，含 Seed） | `VoxelChunk source`（**未注册、不属于任何世界**） | 返回 `null` 或抛异常 ⇒ 跳过后续三步 ⇒ 创建 `Failed` 记录（`Chunk == null`, `NeedsReinstall = true`）；**世界未被触碰** |
| **Validate** | `source` | 校验结论（坐标自洽 + 非空） | `source.Coordinate != Coordinate` ⇒ `Failed(ContractViolation)`（`NeedsReinstall = true`），**不搬运**；`source == null` ⇒ `Failed(NullOutput)` |
| **Install** | `source` + `world` + 改动集 | 世界里该坐标的 Chunk（数据就绪、`IsDirty == true`） | 中途抛异常 ⇒ `Failed`（`Chunk != null`，可能部分写入，`NeedsReinstall = true`）；重试**必须全量写入**（§5.3-R2） |
| **Register** | 安装结果 | 权威注册表中的 `Dirty` 记录 | 注册失败（坐标已存在，理论不可能）⇒ `Failed(DuplicateRegistration)` + 计数，**不得**静默覆盖已有记录 |

### 5.2 禁止事项（本条对应用户要求的"生成过程中不得污染世界注册表"）

1. **Generate / Validate 期间不得改动世界与注册表**：

   * 不得 `world.GetOrCreateChunk`（生成阶段只能产出一个**游离**的 Chunk）；
   * 不得 `SetVoxel` / `MarkDirty` / 删除记录；
   * 不得创建/修改任何 `ChunkRecord`（**也不得创建占位记录**？—— 占位记录只允许存在于
     `Generating` 的**局部变量**中；禁止把它插入注册表容器）；
2. **注册表容器只在 Register 步被写入**：`registry.Add(Coordinate, record)` —— 全代码库中
   **唯一**的插入点；在此之前该坐标在注册表中"不存在"（对外快照不可见）；
3. **失败路径不得留下记录**：Generate/Validate 失败时，占位记录被丢弃后替换为 `Failed` 记录
   （这一步是 **Register 步的失败分支**，不是"污染"）；Install 失败时记录携带
   `Chunk != null` 与 `NeedsReinstall = true`；
4. **禁止"边生成边注册"**：不得先把空记录放进注册表再补数据（会让 B 阶段把半成品算进集合，
   并破坏 RCI1）；
5. **不得污染其它坐标**：安装只允许触碰 `Coordinate` 对应的那一个世界 Chunk。

### 5.3 安装（Install）写入规则

| 规则 | 内容 |
|---|---|
| **R1** | 目标 Chunk **必须**由 `world.GetOrCreateChunk(Coordinate)` 取得（世界拥有；Core 不 `new VoxelChunk`） |
| **R2** | 写入方式：**逐体素 `Chunk.SetVoxel(localX, localY, localZ, type)`**，遍历顺序固定 `x → y → z` |
| **R3** | **"只写非 Air"优化**：仅当目标 Chunk 由 `GetOrCreateChunk` **新创建**（必为空）时允许；已存在（含"外部懒创建"与"上次安装残留"）时**必须全量写入（含 Air）** |
| **R4** | **重试必须全量写入**（R2 全量而非 R3 优化）：保证"安装中途失败留下的残留"被覆盖 ⇒ 结果确定（RCI14） |
| **R5** | **叠加改动集必须在写入之后**（Runtime_Core §5.2-4：改动优先，生成不覆盖改动）；V1 改动集恒为空 ⇒ 该步骤为无操作，但必须保留 |
| **R6** | **收尾**：安装结束时必须保证 `Chunk.IsDirty == true`；若发现为 `false`（异常情形，例如外部调用过 `ClearDirty`）⇒ 显式 `MarkDirty()` |
| **R7** | 空 Chunk 语义：生成结果全为 Air ⇒ 可能零次 `SetVoxel`；但**新 Chunk 初始即 `IsDirty == true`**（VoxelCore），R6 仍然成立 |
| **R8** | 局部坐标换算**必须**使用 `world.WorldToLocalCoordinate` / `world.WorldToChunkCoordinate`（不得自行实现坐标规则，Mining §1.1） |

### 5.4 与改动集的关系（V1）

* 改动集容器（`RuntimeChangeSet`）与叠加步骤（R5）**在 V1 实现但恒为空**；
* V1 **没有**写入改动集的入口（`SubmitEdit` 不在 V1 API，§3.4；编辑规则属 `VoxelWorld_Mining.md`）；
* 因此 V1 的安装结果 = 生成结果（逐体素一致），这一点可被 RCI2 直接验证。

---

## 6. Mesh / View 流程

### 6.1 链路（唯一允许的形式）

```
Dirty Chunk（数据就绪，IsDirty == true）
   │  VoxelChunk（世界拥有，只读）
   ▼
IChunkMesher.TryBuild(chunk, out meshData, out error)      ← 纯 C# 适配器 → VoxelMeshGenerator.Generate
   │  VoxelMeshData（纯数据、一次性负载）
   ▼
IChunkPresenter.Apply(coordinate, meshData, out error)     ← Unity 侧适配器 → VoxelChunkView.TryApply
   │  PresenterOutcome ∈ {Presented, Empty, Failed}
   ▼
Presented / Empty（成功）  ⇒  Chunk.ClearDirty()  ⇒  State = Ready
Failed（失败）             ⇒  保持 Dirty（不清脏）
```

**禁止**：跳过 Mesher 直接调用 Presenter；把 `VoxelChunk` 传给 Presenter；让 Mesher 调用 Presenter；
在 Mesher 内上传 Unity 资产；在 Presenter 内生成几何（Runtime_Interface §8-6、View §1）。

### 6.2 成功后：`ClearDirty`（唯一位置）

| 条件 | 动作 |
|---|---|
| `Apply` 返回 `Presented` | `record.Chunk.ClearDirty()`；`State = Ready`；`record.Mesh = null` |
| `Apply` 返回 `Empty` | 同上（空几何是成功路径，View §4.3） |
| `PresenterOutcome` 与 `meshData.HasGeometry` 不一致 | 视为 `Failed(OutcomeMismatch)`（不 `ClearDirty`） |

* `ClearDirty()` **只能**作用于刚刚呈递成功的那一个 Chunk；
* **禁止** `VoxelWorld.ClearDirtyFlags()`（Runtime_Core §8.3-2）；
* 清脏**必须**在 `Apply` 之后（顺序可被 FakePresenter 的调用序列断言，RCI10）。

### 6.3 失败：保持 Dirty（三条路径）

| 路径 | 触发 | 结果 |
|---|---|---|
| **F-1** Mesher 返回 `false` | 适配器捕获 `VoxelMeshGenerator` 异常并转成 `false` + `error` | `FailureCount++`、`FailureReason` 非空、`State` 回 `Dirty`（或超上限 ⇒ `Failed`）、`Apply` **不被调用**、**不清脏**（RCI3） |
| **F-2** Mesher 抛异常 | 适配器实现违规 | Core 防御性捕获 ⇒ 同 F-1 |
| **F-3** Presenter 返回 `Failed` | 负载非法 / 资产创建或上传失败 | `FailureCount++`、原因非空、`State` 回 `Dirty`、旧表示保持不变、**不清脏**（RCI4） |

**共同条款**：失败**不改变世界数据**（除 Install 路径外，任何失败都不得改动体素）；
失败**不得**回滚已生效的数据；失败**必须**可见（`FailureReason` + 计数）；
超 `MaxRetriesPerChunk` ⇒ 状态 `Failed` 但**仍保持 `Chunk.IsDirty == true`**
（否则会出现"永久过期表示"，Runtime_Core §9.2-4）。

### 6.4 空几何路径

* `meshData.HasGeometry == false` ⇒ 仍然**必须**调用 `Apply`（View 需要它把已有表示撤下并转 `Empty`）；
* `Apply` 返回 `Empty` ⇒ 与 `Presented` 同等对待（`ClearDirty` + `Ready`）；
* 空几何**不是失败**，不得计入 `FailureCount`。

### 6.5 预算与配对

| 规则 | 内容 |
|---|---|
| 配对 | 同一坐标的 `TryBuild` 与 `Apply` **必须同 Tick 连续**；不得跨 Tick 保留"已构建未呈递"的负载 |
| 上限 | 每帧最多完成 `MaxChunkPairsPerFrame` 对（`MaxMeshesBuiltPerFrame` 与 `MaxUploadsPerFrame` 在 V1 合并为一个配对上限，Runtime_Core §2.3） |
| 未处理 | 保持 `Dirty`，下一 Tick 继续（不得丢弃、不得静默跳过） |
| 顺序 | 按坐标升序（`CollectDirtyChunks` 顺序）取前 N —— 预算不改变顺序 |

### 6.6 调用序列约束（可被 Fake 实现断言）

```
合法序列（每个坐标）：  TryBuild → Apply →（成功时）ClearDirty
合法序列（卸载）：      Apply → … → Release
非法序列：              ClearDirty 早于 Apply；Apply 早于 TryBuild；Release → Apply（未重新加载）
                       同一 Tick 内 TryBuild ×2 或 Apply ×2；Apply 与 Release 同一 Tick 同一坐标
```

---

## 7. 第一阶段实现范围

### 7.1 只实现（V1 In Scope）

| # | 范围 | 落地形式 |
|---|---|---|
| 1 | **单线程** | 全部调用在主线程、同步、串行（无锁、无并发容器、无 `Task`） |
| 2 | **同步 Tick** | `Tick()` 一次调用完成 A–H；无分帧中断、无协程、无时间参数 |
| 3 | **手动 Chunk 请求** | `RequestChunk` / `ReleaseChunk` / `ClearRequests`（加载集合 = 请求集合 ∩ 垂直范围） |
| 4 | **Generation 连接** | 经 `IWorldGenerator` 抽象调用；安装流程 = §5 的 Generate→Validate→Install→Register |
| 5 | **Mesh 连接** | 经 `IChunkMesher` 抽象；适配器 `VoxelMeshGeneratorMesher` 包装冻结的静态 Mesher |
| 6 | **View 连接** | 经 `IChunkPresenter` 抽象；适配器 `ChunkViewPresenter` 包装 `VoxelChunkView` |

附带实现（V1 必需，因为它们是上述流程的组成部分）：
注册表与 `ChunkRecord`、状态机与转移校验、脏排空与 `ClearDirty` 纪律、失败与重试判据、
每 Tick 上限、对账、`RuntimeCoreTickReport` + `RuntimeCoreDiagnostics`、改动集容器（恒空）。

### 7.2 不实现（V1 Out of Scope，禁止潜入）

| # | 不实现 | 说明 |
|---|---|---|
| 1 | **玩家距离加载** | 无 `focusChunk`、无 `R_load`/`R_unload`、无滞回（Runtime §6 / Runtime_Core §5.3 在 V1 挂起） |
| 2 | **异步** | 无 `async`/`await`/`Task`/`Coroutine`/回调式分帧 |
| 3 | **Job System** | 无 `IJob` / `JobHandle` / `Burst` / `NativeArray` |
| 4 | **多线程** | 无 `Thread` / 线程池 / 并发容器 / 锁 |
| 5 | **存档** | 无序列化/反序列化、无文件 IO、无版本迁移（Runtime §7.1 只定义内容契约） |
| 6 | **Mining** | 无编辑入口、无挖掘规则、无 `MiningOutcome`（属 `VoxelWorld_Mining.md`） |
| 7 | **Building** | 无放置/模板/覆盖模式（Mining §7.3 属后续阶段） |

同样**不做**（与 Runtime_Core §13 一致）：跨 Chunk 剔除、Greedy Meshing、LOD、遮挡剔除、
后台生成、毫秒预算、对象池策略、`VerticalMode.FocusRange`。

### 7.3 文件与类型映射（V1 落地时的实现清单；**本文档不创建代码**）

| 文件 | 类型 / 内容 | 命名空间 | 纯度 |
|---|---|---|---|
| `Runtime/Abstractions/IChunkMesher.cs` | `IChunkMesher` | `VoxelWorld.Runtime.Abstractions` | 纯 C#（无实现、无状态） |
| `Runtime/Abstractions/IChunkPresenter.cs` | `IChunkPresenter` + `PresenterOutcome` | `VoxelWorld.Runtime.Abstractions` | 纯 C# |
| `Runtime/ChunkRecord.cs` | `ChunkRecord`（§2.2 字段） | `VoxelWorld.Runtime` | 纯 C# |
| `Runtime/ChunkRecordSnapshot.cs` | `ChunkRecordSnapshot`（只读快照） | `VoxelWorld.Runtime` | 纯 C# |
| `Runtime/ChunkLifecycleState.cs` | `ChunkLifecycleState`（6 状态） | `VoxelWorld.Runtime` | 纯 C# |
| `Runtime/RuntimeCoreSettings.cs` | `RuntimeCoreSettings`（Runtime_Core §2.3，去掉距离项） | `VoxelWorld.Runtime` | 纯 C# |
| `Runtime/RuntimeCoreTickReport.cs` | `RuntimeCoreTickReport` | `VoxelWorld.Runtime` | 纯 C# |
| `Runtime/RuntimeCoreDiagnostics.cs` | `RuntimeCoreDiagnostics` | `VoxelWorld.Runtime` | 纯 C# |
| `Runtime/RuntimeChangeSet.cs` | `RuntimeChangeSet`（V1 恒空） | `VoxelWorld.Runtime` | 纯 C# |
| `Runtime/VoxelRuntimeCore.cs` | `VoxelRuntimeCore`（§3） | `VoxelWorld.Runtime` | 纯 C# |
| `Runtime/Adapters/VoxelMeshGeneratorMesher.cs` | `IChunkMesher` 适配器（唯一命名 `VoxelMeshGenerator` 的位置） | `VoxelWorld.Adapters` | 纯 C# |
| `View/ChunkViewPresenter.cs` | `IChunkPresenter` 适配器（坐标 → `VoxelChunkView` 映射 + `LiveCount`） | `VoxelWorld.View` | Unity 侧 |
| `Assets/Editor/VoxelWorldRuntimeCoreValidation.cs` | 批处理验证入口（RCI1–RCI14；用 Fake 实现驱动，无场景要求） | — | Editor |

> `ChunkSize` 只能取自 VoxelCore；`VerticalChunkCount` 由组装点按 `WorldHeight` 推导后显式传入。

### 7.4 组装点（游戏层，唯一允许 `new` 具体实现的位置）

```csharp
var world     = new VoxelWorld();                                 // 真值容器（唯一）
var generator = new BasicTerrainGenerator(generationSettings);    // IWorldGenerator（既有）
var mesher    = new VoxelMeshGeneratorMesher();                   // IChunkMesher（纯 C# 适配器）
var presenter = new ChunkViewPresenter(rootTransform, materials); // IChunkPresenter（Unity 侧）
var core      = new VoxelRuntimeCore(world, generator, mesher, presenter, coreSettings);

// V1 使用方式：手动请求 + 同步 Tick
core.RequestChunk(new VoxelCoordinate(0, 0, 0));
core.Tick();                       // 生成 → 脏 → 构建 → 呈递
core.ReleaseChunk(new VoxelCoordinate(0, 0, 0));
core.Tick();                       // 卸载：Release → RemoveChunk → 注销
```

---

## 8. 验收不变量（RCI）

> 全部可用**纯 C# + Fake 实现**批处理验证（FakeGenerator / FakeMesher / FakePresenter 记录调用序列）；
> 由 `Assets/Editor/VoxelWorldRuntimeCoreValidation.cs` 以菜单项 `Tools/VoxelWorld/Validate Runtime Core`
> 与 `-executeMethod` 双入口运行（与既有 `VoxelWorldViewValidation` 等入口同风格）。

| # | 不变量 | 验证方法 |
|---|---|---|
| **RCI1** | **生成失败不污染世界**：`Generate` 抛错/返回 `null`/坐标不自洽 ⇒ 世界 `ChunkCount` 与全量体素快照**完全不变**；注册表中该坐标最多只有 `Failed` 记录（无 `Dirty`/`Ready`）；该坐标仍可被成功重试 | 注入 FaultyGenerator（三种失败）+ 世界快照比较 + `ChunkCount` 比较 + 重试后成功 |
| **RCI2** | **同 Seed 同 Chunk 结果一致**：同 `Seed + 参数 + 坐标` ⇒ 逐体素一致；安装到世界后的数据与生成结果一致（V1 改动集恒空） | 两个独立世界（同参数）各加载一次 ⇒ 体素快照 + `ComputeContentHash` 相等；再与 `generator.Generate` 的直接输出比较 |
| **RCI3** | **Mesh 失败不清 Dirty**：`TryBuild` 返回 `false` 或抛异常 ⇒ 状态回 `Dirty`、`Chunk.IsDirty == true`、`FailureReason` 非空、`Apply` **未被调用** | 注入 FailingMesher + 状态/脏标记/调用序列断言 |
| **RCI4** | **View 失败不清 Dirty**：`Apply` 返回 `Failed` ⇒ 状态回 `Dirty`、`Chunk.IsDirty == true`、`FailureReason` 非空、无 `ClearDirty` 调用 | 注入 FailingPresenter + 调用序列（记录 `Apply`/`Release`/`ClearDirty` 顺序）断言 |
| **RCI5** | **删除 View 不影响 Voxel 数据**：`ReleaseChunk` / 删除全部表示后，世界体素快照与（被卸载前的）数据一致；重新请求加载后数据与内容哈希与首次一致 | 快照比较 + 卸载/重载哈希比较 |
| **RCI6** | **Tick 顺序确定**：同一初始状态 + 同一请求集合，两次独立运行（两个 Core 实例）的 `TickReport` 序列与注册表快照序列**逐项一致**；阶段内处理顺序 == 升序列表顺序 | 双实例同脚本对照 + 报告/快照逐项比较 + 源码扫描（无字典遍历序依赖） |
| **RCI7** | **ChunkRecord 不是真值**：删除全部记录与表示后，`VoxelWorld` 数据一字不变；`ChunkRecordSnapshot` 不含任何可变数据容器 | 结构检查（快照字段清单）+ 全量体素快照比较 |
| **RCI8** | **请求-加载一致**：(a) 已加载集合 == 请求集合 ∩ 垂直有效范围（无 `Failed` 时）；(b) 释放后 `ChunkCount` 相应减少；(c) 重复请求幂等（不重复生成） | 集合比较 + `GeneratedTotal` 计数 + 重复 `RequestChunk` 断言 |
| **RCI9** | **帧末闭合**：任一 `Tick()` 返回后注册表状态 ⊆ `{Dirty, Ready, Failed}`；无 `Generating`/`Building`/`Unloading` | 每次 Tick 后遍历注册表断言 |
| **RCI10** | **清脏纪律**：清脏只发生在成功的 `Apply` 之后；源码不出现 `ClearDirtyFlags` | `ClearDirty` 调用序列断言 + 源码扫描（剔除注释） |
| **RCI11** | **依赖倒置（结构性）**：`Runtime/` 源码不出现 `BasicTerrainGenerator` / `VoxelMeshGenerator` / `VoxelChunkView` / `UnityMeshBuilder` / `UnityEngine` / `new VoxelChunk` / `MonoBehaviour` / `Time.` / `FindObjectOfType` / `Camera.main` | 源码扫描（剔注释 + 扫描器自检） |
| **RCI12** | **Meshing 只读**：F 阶段前后该 Chunk 的 4096 体素 + `Coordinate` + `IsDirty` 完全不变 | 快照比较（真实适配器 + FakeMesher 各一次） |
| **RCI13** | **所有权与计数一致**：`world.ChunkCount == 记录数(Chunk != null)`；Core 不 Dispose `world`/实现（卸载全部坐标后仍可继续请求与 Tick） | 计数断言 + 卸载后再次加载成功 |
| **RCI14** | **安装原子性与全量重试**：安装中途失败（注入第 N 个体素抛错）⇒ 重试走全量写入 ⇒ 最终数据与成功路径**相同哈希** | 注入分段失败 Mesher/Installer 钩子 + 哈希比较 + 快照比较 |
| **RCI15** | **不改上游**：VoxelCore / Generation / Mesh / View 既有文件在实现落地后 SHA256 不变 | 文件哈希比对（与既有验证入口一致的做法） |

**与上游不变量的对应**：RCI1↔Runtime_Core RC15/RC16；RCI2↔Runtime_Core RC9 + WorldGeneration_Core §2；
RCI3/RCI4↔Runtime_Core RC14 + Mesh §6.3；RCI5↔View V9/V12 + Runtime R18；
RCI6↔Runtime_Core RC6/RC8；RCI7↔VoxelWorld_Core §1；RCI8↔Runtime_Core RC5/RC6；
RCI9↔Runtime_Core §1.4/RC3；RCI10↔Runtime_Core RC14；RCI11↔Runtime_Core RC1 + Runtime_Interface IF1；
RCI12↔Mesh I1/RC13；RCI13↔Runtime_Core §4.3-2/RC4；RCI14↔Runtime_Core §9.2-6/RC16。

---

## 9. 明确不做与变更规则

### 9.1 明确不做

* 所有 §7.2 条目（距离加载 / 异步 / Job / 多线程 / 存档 / Mining / Building）；
* 任何架构层面的改动：不改状态机、不改阶段序、不改接口签名、不引入新抽象层
  （本文档的性质是"具体化 + 收窄"，Runtime_Interface §13.2）；
* 对象池、毫秒预算、优先级抢占、增量集合缓存（V1 每 Tick 重算工作集，简单优先）；
* 诊断 UI / 可视化 / 日志框架（`RuntimeCoreTickReport` 与计数即可）。

### 9.2 变更规则

1. 与上游冲突时优先级：VoxelWorld_Core > Mining > Mesh > Runtime > Runtime_Core > Runtime_Interface > 本文档；
2. 本文档**只能**通过"新增条目 + 登记"的方式变更；任何**收窄或偏离**必须在 §附录 B 表格中新增一行
   （写明：被改动的上游条款、原因、是否影响不变量）；
3. 触及 §2.7（状态转移）、§4（阶段规则）、§6.2（清脏纪律）、§8（不变量）的修改 ⇒ 升版本号 +
   同步更新验收测试 + 说明与既有存档（`Seed + 改动集`）的兼容性；
4. **禁止**用实现侧补丁（跳过清脏、强制重建、静默重生成、整体重置）绕过上游规则；
5. 本规范不得要求修改任何既有 `.cs` 文件（RCI15）。

---

## 附录 A：Tick 阶段 ↔ Runtime_Core §11 映射

| 本文档 | Runtime_Core §11 | 差异说明 |
|---|---|---|
| A 更新请求集合 | A 焦点解析 + B 集合求解（前半） | **收窄**：V1 无焦点/无距离 ⇒ 请求集合即加载集合来源；`Tick()` 无参 |
| B 计算目标 Chunk 集合 | B 集合求解（后半）+ §8.5 对账 | 一致：目标/差异集合 + 对账；差异仅在"判定式换成请求集合" |
| C 卸载 | C | 一致（含 `Release` 失败细化，§4.3-2） |
| D 生成 | D | 一致（§5 把四步具体化） |
| E 编辑提交预留 | E | **收窄**：V1 空操作（无 Mining 入口）；位置保留 |
| F Mesh 构建 | F | 一致（§6 具体化） |
| G View 呈现 | G | 一致（含 `OutcomeMismatch` 交叉校验） |
| H 诊断 | H | 一致 + 补 6 条帧末断言 |

## 附录 B：与已有规范一致性检查

### B.1 继承（零改动、零冲突）

| 本文档章节 | 上游条款 | 关系 |
|---|---|---|
| §2.1 ChunkRecord 定位 | VoxelWorld_Core §1（数据是真值）/ Runtime_Core §4.1 | 继承：记录是管理对象，不是真值 |
| §2.6 六状态 + §2.7 转移 | Runtime_Core §3.1/§3.2 | 继承：状态名与允许转移集合一致（本文档补齐"触发条件/字段/时机"） |
| §2.8 投影不变量 | Runtime_Core §3.3 | 继承：五条原样；H 阶段逐条断言 |
| §3.1/§3.2 职责与禁止 | Runtime_Core §1.2/§2.2、Runtime_Interface §2.2 | 继承：只调度不决策；四类禁止可扫描 |
| §3.3 三个抽象注入 | Runtime_Interface §1.1/§2.4、Runtime_Core §2.4 | 继承：构造注入、不依赖具体实现、不隐式查找 |
| §4.0 阶段序 | Runtime_Core §11、Runtime_Interface §8 | 继承：A–H 顺序不可交换 |
| §5.3-R2/R8 写入与坐标 | Runtime_Core §5.2、Mining §1.1 | 继承：唯一写入口、坐标换算用 VoxelCore |
| §6.1 链路 / §6.2 清脏 / §6.3 失败 | Runtime_Core §6/§7.3/§8.3/§9、Runtime_Interface §5/§6 | 继承：三态结果、唯一清脏位置、失败保持 Dirty |
| §7.1 六项范围 | Runtime_Core §13（V1 范围）、Runtime_Interface §12 | 继承：单线程同步、手动请求、三条连接 |
| §8 RCI1–RCI6 | 用户本轮指定 + Runtime_Core §12 | 继承：语义与上游不变量一一对应（§8 末表） |
| §9.1 明确不做 | Runtime_Core §13、Runtime §11、View §11 | 继承：不新增可做项 |

### B.2 收窄（显式登记，不视为冲突）

| 条目 | 被改动的上游表述 | 本实现规范的处理 | 原因 / 影响 |
|---|---|---|---|
| **N1** | Runtime_Core §5.3 加载集合判定式 `dx²+dz² ≤ R_load²` | V1 不实现；加载集合 = 显式请求集合 ∩ `y ∈ [0, VerticalChunkCount)` | 用户本轮明确"不实现玩家距离加载"；判定式保持有效，留给 V2；不影响任何不变量 |
| **N2** | Runtime_Core §11 阶段 A「焦点解析」与 `Tick(focusChunk)` | V1 为 `Tick()` 无参；阶段 A 变为"更新请求集合" | 同上；V2 只能**新增**重载（Runtime_Interface §13.2），不得改阶段序 |
| **N3** | Runtime_Core §4.2「注册：`Generating` 开始时」 | 权威注册表插入点收紧为**安装成功后**（Register 步）；`Generating` 以 Tick 内部占位记录表示 | 满足"生成过程中不得污染注册表"（§5.2）；与 §1.4 帧末闭合一致（`Generating` 本就是 Tick 内瞬态） |
| **N4** | Runtime_Core §9.1「卸载时 `Release` 抛错 ⇒ 仍记为 `Failed`」 | 实现为 `ReleaseFailures` 计数 + 原因记录；记录照常删除（不产生 `Failed` 记录） | 卸载语义下该坐标将被移除，没有可承载 `Failed` 状态的记录；"失败可观测"仍满足 |
| **N5** | Runtime_Core §2.3 的 `MaxMeshesBuiltPerFrame` + `MaxUploadsPerFrame` | V1 合并为单个 `MaxChunkPairsPerFrame`（构建+呈递配对上限） | 与 Runtime_Core §6.2「构建与呈递同 Tick 配对」一致；两个独立上限在 V1 无观测差异 |
| **N6** | Runtime_Core §5.4 改动集 / §附录A `SubmitEdit` | V1 保留改动集容器与叠加步骤，但**不提供**编辑入口（恒为空） | 用户本轮明确"不实现 Mining"；保留叠加步骤使 V2 接入 Mining 时无需改动安装流程 |
| **N7** | Runtime_Core §附录A `VoxelRuntimeCore` 构造（4 个参数） | V1 完全一致（`world, generator, mesher, presenter, settings` 共 5 项，其中 world 为数据容器） | 与 Runtime_Interface §附录A 的"3 抽象 + world"一致 |

### B.3 细化（把抽象规则落成可检查形式）

| 条目 | 上游条款 | 本实现规范的细化 |
|---|---|---|
| **D1** | Runtime_Core §1.4 帧末闭合 | §4.0 工作集快照规则（A/B 产出升序不可变列表）+ §4.8 六条帧末断言 |
| **D2** | Runtime_Core §5.2 安装路径 | §5.1 四步表（每步输入/输出/失败）+ §5.2 注册表唯一插入点 + §5.3 R1–R8 写入规则 |
| **D3** | Runtime_Core §8.5 对账三情形 | §4.2 输出定义（补登记 / `Failed(DataMissing)` / 投影回 `Dirty`）与"只投影不改数据" |
| **D4** | Runtime_Core §9.2-4 重试有界 | §2.7 重试判据表 + `NeedsReinstall` 字段（区分"需重装"与"仅需重建表示"） |
| **D5** | Runtime_Core §7.1 三处调用点 | §6.6 合法/非法调用序列清单（可被 Fake 实现断言） |
| **D6** | Runtime_Interface §6.3 交叉校验 | §6.2 末行（`OutcomeMismatch` ⇒ `Failed`，不清脏） |
| **D7** | Runtime_Core §12 RC1–RC18 | §8 RCI1–RCI15（V1 可执行版本，含上游对应关系表） |

### B.4 与被冻结文件的零冲突确认

* 本实现规范要求的**新增**文件全部落在 `Runtime/`、`Runtime/Abstractions/`、`Runtime/Adapters/`、`View/`、
  `Assets/Editor/`（§7.3），**不触碰** VoxelCore（6 文件）、Generation（4 文件）、Mesh（2 文件）
  与既有 Editor 验证入口（4 文件）；
* 对既有类型的**唯一要求**是"实现既有接口"（`BasicTerrainGenerator : IWorldGenerator` 已经成立；
  View 侧由新适配器 `ChunkViewPresenter` 实现 `IChunkPresenter`），既有无需任何修改；
* 落地后须以 RCI15 的哈希比对作为"不改已有代码"的机械证据。
