# Voxel World Runtime Interface Specification V1

> 适用范围：`Assets/Scripts/VoxelWorld/Runtime/Abstractions/`（抽象层）与其三类实现
> （Generation 实现 / Mesh 适配器 / View 实现）。
> 本规范只定义 **Runtime Core 与 Generation、Mesh、View 之间的通信接口（抽象）、
> 依赖倒置结构、接口纪律、所有权与配对生命周期**；
> 不定义调度策略（属 `VoxelWorld_Runtime.md`）、不定义 Core 内部结构（属 `VoxelWorld_Runtime_Core.md`）、
> 不定义几何规则（属 `VoxelWorld_Mesh.md`）、不定义表示细节（属 `VoxelWorld_View.md`）。
> **不涉及**既有 Terrain 系统（`TerrainModificationV2` / `DensityField` / Marching Cubes 地形）。
>
> 上游规范：
> * `VoxelWorld_Core.md` —— 世界真值（Voxel Data）与 Chunk 数据契约；
> * `WorldGeneration_Core.md` —— 初始状态生成（Seed + ChunkCoordinate → VoxelChunk）；
> * `VoxelWorld_Mesh.md` —— 派生表示的几何数据契约（Meshing 只读 / 输出可丢弃 / 不变量）；
> * `VoxelWorld_View.md` —— 表示层（Unity 侧：对象模型 / 资产 / 材质槽 / Collider / 释放）；
> * `VoxelWorld_Runtime.md` —— 调度、生命周期、流式加载、存档边界；
> * `VoxelWorld_Runtime_Core.md` —— Runtime Core 的纯 C# 结构、阶段与不变量。
>
> 优先级：真值 > 修改 > 几何 > 调度 > Core 结构 > **本文档（抽象与装配）**。
> 本文档**只定义抽象与依赖方向**：不新增世界规则、不改变任何上游不变量、
> 不要求修改任何既有类型（Generation / Mesh / View 的既有实现保持原样）。

---

## 1. Core Principles（不可协商条款）

1. **依赖倒置（本规范的全部意义）。**
   ```
   Core ──依赖──▶ 抽象 ◀──实现── Generation / Mesh 适配器 / View
   ```
   Runtime Core **只认识抽象**；具体实现通过**组装点**注入。
   Core 中不得出现任何实现类型名（§2.3 可扫描验证）。
2. **抽象层不含实现、不含状态、不含 Unity 类型。**
   抽象层只声明契约（接口 / 枚举 / 结果类型），不提供算法、不持有资源、不创建对象；
   签名中**不得出现** `MonoBehaviour` / `GameObject` / `Transform` / `Mesh` / `Material` /
   `Vector2` / `Vector3` / `Bounds` 等 Unity 类型（`VoxelMeshData` 作为**不透明负载**是唯一例外，§3.3）。
3. **组装点唯一且显式。**
   谁创建世界、生成器、Mesher 适配器、Presenter、Core，全部在**游戏层的组装点**里显式完成；
   禁止隐式查找（`Camera.main` / `FindObjectOfType` / 静态单例 / `Resources.Load` / 服务定位器）。
4. **接口即边界：三类接口 = 三个方向的通信。**

   | 方向 | 接口 | 一句话 |
   |---|---|---|
   | Core ← Generation | `IWorldGenerator`（上游既有） | 「给我这个坐标的初始数据」 |
   | Core ← Mesh | `IChunkMesher`（本文档新增） | 「把这个 Chunk 变成纯数据几何」 |
   | Core → View | `IChunkPresenter`（本文档确认归属） | 「呈递/释放这个坐标的表示」 |

5. **接口调用不转移所有权。**
   `IWorldGenerator` / `IChunkMesher` / `IChunkPresenter` 的调用**不转移**世界数据、
   不转移世界容器的所有权；实现只输出自己的产物（新 Chunk / 纯数据缓冲 / 表示），
   由 Core 决定何时搬运、何时丢弃、何时释放（§10）。
6. **失败必须用返回值表达（禁止异常做控制流）。**
   接口方法通过返回值 + `out string error` 报告失败，**实现不得用异常表达可预期失败**；
   Core 仍必须**防御性捕获**异常并转为 `Failed`（Runtime_Core §9.1）。
7. **单线程、禁重入、确定。**
   V1 全部接口调用在主线程、同步、串行；实现**不得**回调 Core、不得启动线程/异步任务、
   不得读取时间/随机数；同输入必得同输出（Generation §2、Mesh §1.3、Core §10）。
8. **不改变上游契约。**
   本规范只能**新增**抽象与适配器，不得修改 `IWorldGenerator`、`VoxelMeshGenerator`、
   `VoxelChunkView`、`UnityMeshBuilder` 的既有签名或语义。

---

## 2. 依赖倒置结构

### 2.1 架构图

```
                        ┌──────────────────── 抽象层（无实现、无状态） ────────────────────┐
                        │  VoxelWorld.Generation.IWorldGenerator   （上游既有，直接复用） │
                        │  VoxelWorld.Runtime.Abstractions.IChunkMesher   （本文档新增） │
                        │  VoxelWorld.Runtime.Abstractions.IChunkPresenter（本文档确认） │
                        └───▲────────────────────────▲──────────────────────────▲────────┘
                            │ 依赖抽象                │ 实现                     │ 实现
        ┌───────────────────┴──────────┐   ┌─────────┴──────────────┐  ┌────────┴─────────────────────┐
        │ Runtime Core（纯 C#）         │   │ Generation 实现         │  │ View 实现（Unity 侧）         │
        │ VoxelWorld.Runtime           │   │ BasicTerrainGenerator   │  │ VoxelChunkView               │
        │ 注册表 / 状态机 / 队列 / 预算 │   │ （既有，零改动）        │  │ UnityMeshBuilder（既有）      │
        │ 加载 / 生成 / 脏 / 构建 / 呈递│   ├─────────────────────────┤  │ + Mesher 适配器（纯 C#）      │
        │ 卸载 / 失败 / 确定性          │   │ VoxelMeshGenerator      │  │ + Presenter 适配器（Unity）   │
        └──────────────────────────────┘   │ （既有静态类 → 经适配器）│  └──────────────────────────────┘
                                           └─────────────────────────┘
                          ▲
                          │ 显式注入（组装点：游戏层 Bootstrap，唯一）
```

### 2.2 依赖矩阵

| 组件 | 允许依赖 | 禁止依赖 |
|---|---|---|
| **抽象层** | VoxelCore 的值类型（`VoxelCoordinate` 等）、`VoxelMeshData`（负载） | 任何实现类、任何 Unity 类型、`VoxelWorld.View`、Terrain 旧体系 |
| **Runtime Core** | 抽象层（三个接口）+ VoxelCore 数据 API + Generation 的**设置/接口**类型 + `VoxelMeshData`（不透明） | 任何实现类名（`BasicTerrainGenerator` / `VoxelMeshGenerator` / `VoxelChunkView` / `UnityMeshBuilder`）、`UnityEngine` / `UnityEditor`、场景/资产/渲染/物理 |
| **Generation 实现** | VoxelCore + 自己的规则 | Core、抽象层中被它实现之外的接口、Unity 运行时类型（现有契约不变） |
| **Mesher 适配器**（纯 C#） | 抽象层 + `VoxelWorld.Mesh`（`VoxelMeshGenerator`）+ VoxelCore | Core、View、Unity 运行时类型 |
| **View 实现**（Unity） | 抽象层 + `VoxelWorld.Mesh`（负载类型）+ Unity 运行时 | Core、Generation、数据写入 API、Terrain 旧体系 |
| **组装点**（游戏层） | 全部（唯一允许 `new` 具体实现的地方） | — |

### 2.3 结构性禁令（可机械扫描）

| 扫描范围 | 不得出现 |
|---|---|
| `Runtime/*.cs`（Core） | `BasicTerrainGenerator` / `VoxelMeshGenerator` / `VoxelChunkView` / `UnityMeshBuilder` / `new VoxelChunk` / `UnityEngine` / `UnityEditor` / `Vector2` / `Vector3` / `Mesh` / `GameObject` / `Transform` / `Camera.main` / `FindObjectOfType` / `ClearDirtyFlags` |
| `Runtime/Abstractions/*.cs`（抽象层） | 任何实现类名、任何 Unity 类型标识符（`VoxelMeshData` 仅可作为参数/返回类型）、任何字段/静态状态、任何算法 |
| `Runtime/Adapters/*.cs`（纯 C# 适配器） | `UnityEngine` / `UnityEditor`、Core 的具体类型（`VoxelRuntimeCore`）、View 类型 |
| `View/*.cs`（View 实现） | `VoxelChunk` / `SetVoxel` / `MarkDirty` / `ClearDirty` / Generation 类型 / Mining 类型 / `VoxelRuntimeCore` / `Physics.Raycast` / `RecalculateNormals` |

> 扫描时必须先剔除注释与字符串字面量（既有验证入口已采用该做法），并带**扫描器自检**。

### 2.4 组装点（唯一允许 `new` 具体实现的位置）

```csharp
// 游戏层 Bootstrap（示例：全部显式、无隐式查找）
var world     = new VoxelWorld();                                    // 真值容器（唯一）
var generator = new BasicTerrainGenerator(generationSettings);       // IWorldGenerator
var mesher    = new VoxelMeshGeneratorMesher();                      // IChunkMesher（纯 C# 适配器）
var presenter = new ChunkViewPresenter(rootTransform, materials);    // IChunkPresenter（Unity 侧适配器）
var core      = new VoxelRuntimeCore(world, generator, mesher, presenter, coreSettings);

// 每帧：core.Tick(focusChunk);   ← focusChunk 为整数 Chunk 坐标（由游戏层从相机/玩家换算）
```

**组装点纪律**：实现实例一次性创建、与 Core 同寿命（V1 不做热替换）；
`VoxelRuntimeCore` 不负责创建/销毁任何实现（Runtime_Core §1.2）。

---

## 3. 接口总览（三类 × 五要素）

| 接口 | 定义者 | V1 实现者 | 调用方向 | 每次调用的语义 |
|---|---|---|---|---|
| `IWorldGenerator` | **Generation（上游既有）** | `BasicTerrainGenerator`（既有，零改动） | Core **调用** Generation | 生成一个坐标的初始数据（新 Chunk，不注册） |
| `IChunkMesher` | **本文档（新增）** | `VoxelMeshGeneratorMesher`（纯 C# 适配器，V1 待落地） | Core **调用** Mesh | 把一个 Chunk 变成纯数据几何 |
| `IChunkPresenter` | **本文档（确认归属 Core 定义）** | `ChunkViewPresenter`（Unity 适配器，内部持有 `VoxelChunkView`，V1 待落地） | Core **调用** View | 呈递 / 释放一个坐标的表示 |

### 3.1 五要素总表

| 维度 | `IWorldGenerator` | `IChunkMesher` | `IChunkPresenter` |
|---|---|---|---|
| **输入** | Chunk 坐标（含生成参数在实例上，显式） | 世界拥有的 `VoxelChunk`（**只读**） | 坐标 + `VoxelMeshData`（不透明负载） |
| **输出** | `VoxelChunk`（新实例，未注册） | `VoxelMeshData`（可能为空几何） | `PresenterOutcome`（`Presented`/`Empty`/`Failed`） |
| **Failure** | 返回 `null` 或抛异常 ⇒ Core 判 `Failed`（世界未被触碰） | 返回 `false` + `error` ⇒ 状态回 `Dirty`，不清脏 | 返回 `Failed` ⇒ 回 `Dirty`，不清脏；旧表示保持 |
| **生命周期** | 实例：组装点创建、与 Core 同寿命；调用：无状态、任意次数/顺序 | 实例：同寿命、可复用缓冲但输出只依赖输入；调用：纯函数式 | 实例：同寿命；**调用：与表示槽配对**（`Apply` 建立/更新、`Release` 结束） |
| **所有权** | 实现拥有生成器；**产出的 Chunk 不归实现**（Core 搬运后即丢弃） | 实现拥有内部缓冲；**输出负载归 Core**（一次性，不得保留） | 实现拥有 GameObject/Mesh 资产；**Core 拥有调度权与生命周期边界** |

### 3.2 依赖方向不变量

* Core → 抽象：**只允许**单向依赖；
* 实现 → 抽象：实现接口（`class BasicTerrainGenerator : IWorldGenerator`）；
* 抽象 → 谁都不依赖；
* **实现不得反向依赖 Core**（不得出现 `VoxelRuntimeCore`），因此编译期即不可能"回调 Core"（禁重入的结构性保证）。

### 3.3 `VoxelMeshData` 作为不透明负载的例外（诚实说明）

`VoxelMeshData` 定义在 `VoxelWorld.Mesh`，其字段类型是 Unity 数学结构体（`Vector2/Vector3`）。
抽象层签名中出现它**是允许的**，但：

1. 抽象层与 Core **只能**把它当**不透明负载**（RC-P2）：不得访问 `.Vertices` / `.Normals` / `.UVs`，
   只允许纯 C# 成员（`TriangleCount` / `QuadCount` / `VertexCount` / `HasGeometry` /
   `GetSubmeshTriangleCount` / `ComputeContentHash` / `Triangles`）；
2. 因此"接口签名不出现 Unity 类型"的判据是：**不出现 Unity 类型标识符**（`Mesh` / `Vector3` / …），
   而不是"不出现任何携带 Unity 结构体的类型"。这一点在 Runtime_Core §1.1 已有同样说明，
   本文档保持一致。

---

## 4. Generation 接口：`IWorldGenerator`

> **归属**：已由 `WorldGeneration_Core.md` §3/§6 与既有类型 `VoxelWorld.Generation.IWorldGenerator` 定义。
> **本文档不改名、不重复定义、不改签名**；Core 依赖该抽象，不依赖 `BasicTerrainGenerator`。

```
ChunkCoordinate ──▶ IWorldGenerator.Generate(coord) ──▶ VoxelChunk（新实例，未注册、未属于任何世界）
```

### 4.1 契约（上游已定义，此处只做接口层复述）

| 项 | 内容 |
|---|---|
| 输入 | `VoxelCoordinate chunkCoordinate`（Chunk 坐标；实现不得假设生成顺序、不得读取相邻 Chunk） |
| 输入（实例上） | `WorldGenerationSettings Settings { get; }`（含 Seed；**显式**，无隐藏全局状态） |
| 输出 | `VoxelChunk`（`Coordinate == chunkCoordinate`；未注册到任何 `VoxelWorld`） |
| 只读性 | 实现**不得**访问/修改任何世界数据（它只产出新实例） |
| 确定性 | 同 `Seed + 坐标 + 参数` ⇒ 同结果；不依赖顺序/帧序/时间/随机数（Generation §2） |
| 不生成 Mesh | 只产出体素数据（Generation §8） |

### 4.2 输入

* 只有 Chunk 坐标 + 实例上的显式参数；**不得**传入世界、相机、时间、帧序、随机种子外的状态；
* Core 保证：同一 Tick 内串行调用；不并发、不重入。

### 4.3 输出

* `VoxelChunk`；Core 的**防御性校验**：
  * `source == null` ⇒ `Failed(NullOutput)`；
  * `source.Coordinate != 请求坐标` ⇒ `Failed(ContractViolation)`（**世界未被污染**：校验发生在搬运之前）；
* 搬运契约（Runtime_Core §5.2）：Core 把 `source` 的体素写入世界拥有的 Chunk（`GetOrCreateChunk`），
  **搬运之后 Core 不再引用 `source`**；实现可以复用/缓存自己返回的实例（Core 每次全量搬运，不依赖实例身份）。

### 4.4 Failure

| 情形 | 实现 | Core |
|---|---|---|
| 参数非法（生成器构造期） | 构造时抛异常（上游契约，允许） | 由组装点处理（拒绝启动） |
| 生成失败（不可预期） | 优先返回 `null`；也可抛异常 | 均判 `Failed`，原因入 `FailureReason`；世界**未被触碰** |
| 坐标不匹配 / 空输出 | 不得发生 | `Failed(ContractViolation)` / `Failed(NullOutput)`，计数可见 |

### 4.5 生命周期

* **实例**：组装点创建，与 Core 同寿命；V1 不热替换；
* **调用**：无状态（实现可缓存内部只读数据，但输出只依赖输入）；
* **产物**：`VoxelChunk` 是**临时对象**，其生命周期 = "直到 Core 完成搬运"（同一 Tick 内）。

### 4.6 所有权

| 资产 | 所有者 | 说明 |
|---|---|---|
| 生成器实例 | 组装点 | Core 只持有引用，不 Dispose |
| 生成器内部状态/缓冲 | Generation 实现 | Core 不感知 |
| `source` Chunk（返回值） | **Core 临时持有**（搬运前） | 搬运后即可被 GC；实现不得把它当持久对象（也不得指望它被回收） |
| 世界拥有的目标 Chunk | `VoxelWorld` | 由 `GetOrCreateChunk` 创建（Core 不 `new`） |

---

## 5. Mesh 接口：`IChunkMesher`

> **归属**：**本文档新增**。原因：`VoxelWorld.Mesh` 暴露的是**静态类** `VoxelMeshGenerator`（冻结），
> 静态类无法被替换/伪造，直接调用会让 Core 依赖具体实现（违反要求 3）。
> 因此由抽象层引入 `IChunkMesher`，并提供**纯 C# 适配器** `VoxelMeshGeneratorMesher` 包装它 ——
> **Mesh 层零改动**。

```
VoxelChunk（只读） ──▶ IChunkMesher.TryBuild(chunk, out meshData, out error) ──▶ VoxelMeshData（纯数据）
```

### 5.1 签名与语义（草案）

```csharp
namespace VoxelWorld.Runtime.Abstractions
{
    public interface IChunkMesher
    {
        /// 返回 true  = 构建完成（meshData 非 null；可能为空几何，用 HasGeometry 判定）
        /// 返回 false = 失败（meshData 必须为 null，error 必须非空）
        bool TryBuild(VoxelChunk chunk, out VoxelMeshData meshData, out string error);
    }
}
```

### 5.2 输入

* `VoxelChunk chunk`：**世界拥有**、数据已就绪的 Chunk；调用方（Core）保证
  绝不对 `Generating` 中的 Chunk 调用（Runtime_Core §6.1）；
* **只读**：实现**不得**以任何方式修改它（`SetVoxel` / `MarkDirty` / `ClearDirty` / 直接改内部数组）
  —— 校验方式 = 构建前后 4096 体素 + `Coordinate` + `IsDirty` 快照比较（Mesh §1.2 / I1）。

### 5.3 输出

* `VoxelMeshData`（纯数据；内容必须满足 `VoxelWorld_Mesh.md` §3/§5：子网格 3 组、索引 3 的倍数、
  索引在范围内、法线为 6 个轴向单位向量之一、顶点位于 `[0, ChunkSize]`）；
* **空几何是成功**：Chunk 无实体体素 ⇒ 返回 `true` + `HasGeometry == false` 的缓冲
  （既有 `VoxelMeshGenerator` 已如此：全 Air ⇒ 0 三角形；`VoxelMeshData.CreateEmpty()` 亦然）；
* Core 不加工几何，只把负载交给 Presenter（§6）；
* **确定性证据**：`meshData.ComputeContentHash()` 必须可复现（同输入 ⇒ 同哈希）。

### 5.4 Failure

| 情形 | 实现 | Core |
|---|---|---|
| 输入为 `null` | 返回 `false` + `error`（禁异常） | 判 `Failed`，状态回 `Dirty`（若来自 `Dirty`） |
| 几何生成异常 | 优先捕获并返回 `false` + `error` | 同左；**不清脏、不呈递** |
| 抛异常（实现违规） | — | Core 防御性捕获 ⇒ `Failed`（Runtime_Core §9.1） |
| 输出非法（索引越界等） | 不得发生（`VoxelMeshData` 构造期已校验） | 若发生 ⇒ `VoxelMeshData` 构造期抛错 ⇒ 被防御性捕获 ⇒ `Failed` |

### 5.5 生命周期

* **实例**：组装点创建，与 Core 同寿命；
* **调用**：**纯函数式**（同输入 ⇒ 同输出）；允许内部缓冲复用，但
  ① 输出只依赖输入；② 不得把上一次调用的状态泄漏到本次输出；
* **产物**：`VoxelMeshData` 是**一次性负载**（见 §6.5）。

### 5.6 所有权

| 资产 | 所有者 | 说明 |
|---|---|---|
| Mesher 适配器实例 | 组装点 | Core 只持有引用 |
| 适配器内部缓冲 | 适配器 | 不得被 Core 或 View 直接引用 |
| `VoxelMeshData` 负载 | **Core（临时）** | 生命周期 = 该 Chunk 的 `Building` 阶段（同 Tick 内用完） |
| 包在里面的 `VoxelMeshGenerator` | 静态类（无实例） | 只有适配器可以命名它（§2.3） |

---

## 6. View 接口：`IChunkPresenter`

> **归属**：由 `VoxelWorld_Runtime_Core.md` §2.4 定义（Core 定义、View 实现）。
> 本文档将其确认为**跨层公共抽象**并给出完整的输入/输出/失败/生命周期/所有权契约。
> View 侧的具体实现（`VoxelChunkView` / `UnityMeshBuilder`）保持原样，由适配器 `ChunkViewPresenter` 封装。

```
VoxelCoordinate + VoxelMeshData ──▶ IChunkPresenter.Apply(...) ──▶ PresenterOutcome
VoxelCoordinate                 ──▶ IChunkPresenter.Release(...) ──▶ （幂等）
```

### 6.1 签名与语义（草案）

```csharp
namespace VoxelWorld.Runtime.Abstractions
{
    public enum PresenterOutcome { Presented, Empty, Failed }

    public interface IChunkPresenter
    {
        /// 输入只有坐标 + 纯数据负载（View §1.2）；失败必须带原因（View §9.1）
        PresenterOutcome Apply(VoxelCoordinate coordinate, VoxelMeshData meshData, out string error);

        /// 释放该坐标的表示（幂等；View §5.6）
        void Release(VoxelCoordinate coordinate);

        /// 只读诊断：当前存活表示数（用于"卸载即净"断言）
        int LiveCount { get; }
    }
}
```

### 6.2 输入

* `coordinate`（Chunk 坐标，整数）+ `meshData`（不透明负载）；
* **不得**要求 Core 传入 `Transform` / 世界浮点位置 / 相机 / 时间 / 材质之外的任何状态
  （摆放规则 `coordinate * ChunkSize` 由实现自己算，View §3.3）；
* `meshData` 必须来自 `IChunkMesher` 的输出（Core 不做几何加工）。

### 6.3 输出

| 输出 | 含义 | Core 的处理 |
|---|---|---|
| `Presented` | 已完整呈现几何 | 状态 → `Ready`；随后 `chunk.ClearDirty()`（**Core 执行**，View 不碰脏标记） |
| `Empty` | 已确认无几何（成功） | 同上（空几何是成功路径，View §4.3） |
| `Failed` | 未完成（旧表示保持） | 状态回 `Dirty`；**不** `ClearDirty`；原因入 `FailureReason` |

* **一致性交叉校验（防御性）**：`meshData.HasGeometry == false` 时实现应返回 `Empty`，
  否则应返回 `Presented`；不一致 ⇒ Core 判 `Failed(OutcomeMismatch)` 并计数
  （不掩盖"表示声称有几何但数据没有"这类缺陷）。

### 6.4 Failure

| 情形 | 实现 | Core |
|---|---|---|
| 非法负载（null / 越界） | 返回 `Failed` + `error`；**不得**抛出 | 回 `Dirty`，不清脏（View §9.1） |
| 资产创建/上传失败 | 返回 `Failed` + `error`，释放本次临时资源 | 同左；旧表示由实现保证不变（原子性） |
| 空几何 | **成功**（`Empty`） | 按成功处理 |
| `Release` 时资产已被外部销毁 | 静默完成剩余释放（幂等） | 视为成功 |
| 抛异常（实现违规） | — | Core 防御性捕获 ⇒ `Failed` |

### 6.5 生命周期（两级：实例 / 表示槽）

| 级别 | 契约 |
|---|---|
| **实例** | 组装点创建，与 Core 同寿命；`LiveCount` 是可观测的只读诊断 |
| **表示槽**（每个 Chunk 坐标一个） | `Apply` **建立或更新**（可多次调用，幂等，View V14）；`Release` **结束**（幂等，View V9）；**同一坐标同一时刻至多一个活跃槽** |
| **负载引用** | `Apply` 返回后实现**不得**继续持有 `meshData`（必须在调用内完成复制/上传）—— 负载是**一次性**的 |
| **配对性** | 每个活跃槽最终必须被 `Release`（Core 保证：卸载时无条件 `Release`，Runtime_Core §7.2-4）；`LiveCount` 与 Core 的已加载集合数量一致 |

### 6.6 所有权

| 资产 | 所有者 | 说明 |
|---|---|---|
| Presenter 适配器实例 | 组装点 | Core 只持有引用 |
| `GameObject` / `Mesh` 资产 / `MeshCollider` / 材质槽 | **View 实现** | Core 不创建、不销毁、不查询（View §5/§7） |
| 表示槽（坐标 → 表示） | View 实现拥有资源，**Core 拥有调度权**（何时 `Apply`/`Release`） | 生命周期边界由 Core 的调用界定 |
| `meshData` 负载 | Core（临时） | 不得被实现跨调用保留 |
| 共享材质 | 组装点/游戏层 | View 只引用，不销毁（View §5.6） |

---

## 7. 接口纪律（对三类实现通用）

| # | 纪律 | 理由 |
|---|---|---|
| **ID-1** | **禁止异常做控制流**：可预期失败用返回值 + `error` 表达 | 失败必须可观测、可计数（Runtime §9.1） |
| **ID-2** | **单线程、同步、串行**：不启线程/Task/协程；不并发访问世界 | 确定性；Unity 对象 API 非线程安全（View §9.4） |
| **ID-3** | **禁重入**：实现不得回调 Core（且不得引用 `VoxelRuntimeCore`，编译期保证） | 防状态机被重入破坏（Core §1.4 帧末闭合） |
| **ID-4** | **幂等**：`TryBuild` 同输入同输出；`Apply` 可重复；`Release` 幂等 | 重试安全、崩溃恢复（Core §9.2-6、View V14/V9） |
| **ID-5** | **不保留负载**：`TryBuild` 之后不持有输入 Chunk，`Apply` 之后不持有负载 | 负载是一次性对象（§5.6/§6.6） |
| **ID-6** | **不改世界**：Generation 只产出新实例；Mesher **只读**输入 Chunk；Presenter 不写任何数据 | 单一写入口（Mining §1.1、Mesh §1.2） |
| **ID-7** | **不读时间/随机/环境**：不得用 `Time` / `DateTime` / `Random` / `Guid` 影响输出 | 确定性（Core §10.2） |
| **ID-8** | **错误必须带原因**：`error` 非空且可读（含坐标、阶段） | 禁止静默失败（Runtime §9.1-3） |
| **ID-9** | **不依赖具体 Core**：实现只认识抽象层类型 | 依赖倒置（§1.1）与可替换性 |
| **ID-10** | **不隐式查找**：实现内部不得 `FindObjectOfType` / `Camera.main` / 单例；依赖必须由组装点注入 | 无隐藏全局状态（Generation §3、Runtime §3） |

---

## 8. 每个 Chunk 的调用时序（一条完整的调用链）

| 阶段（Runtime_Core §11） | 调用 | 允许的替代路径 |
|---|---|---|
| D 加载/生成 | `generator.Generate(coord)` → Core 搬运 → 叠加改动集 → `Dirty` | 失败 ⇒ `Failed`（世界未触碰） |
| F 构建 | `mesher.TryBuild(chunk, out mesh, out error)` | 失败 ⇒ 回 `Dirty`（不清脏） |
| G 呈递 | `presenter.Apply(coord, mesh, out error)` → `ClearDirty()` → `Ready` | 失败 ⇒ 回 `Dirty`（不清脏） |
| C 卸载 | `presenter.Release(coord)` → `RemoveChunk` → 注销 | `Release` 无条件调用（幂等） |

**时序硬性条款**

1. **数据 → 几何 → 表示**：`Apply` 之前必须已有 `TryBuild` 的成功输出；
   `Apply` 绝不早于该 Chunk 的数据就绪（Core §1.3）；
2. **同 Tick 配对**：`TryBuild` 与 `Apply` 对同一 Chunk 必须**同 Tick 连续**完成（Core §6.2）；
3. **每 Chunk 每 Tick 至多一次** `Generate` / `TryBuild` / `Apply`；
4. **`Release` 先于数据删除**（View §5.6 的释放顺序由实现内部保证，Core 保证调用次序）；
5. **任何阶段失败 ⇒ 不进入下一阶段**，且**不清脏**（Core §8.3）；
6. **接口之间不互相调用**：实现不得在自己的方法里调用另一个接口（例如 Mesher 不得调用 Presenter）——
   只有 Core 负责编排（单一编排者）。

---

## 9. Failure 统一分类与传播

| 分类 | 触发者 | 表现 | Core 的传播结果 |
|---|---|---|---|
| `NullInput` / `NullOutput` | Core 防御性校验 | 返回值为 null | `Failed(NullOutput)`，世界未触碰 |
| `ContractViolation` | Core 防御性校验 | 坐标不匹配等 | `Failed(ContractViolation)`，不搬运 |
| `InvalidPayload` | Mesher / Presenter | 返回 `false` / `Failed` + `error` | 状态回 `Dirty`，不清脏 |
| `Unavailable` | Presenter | 资产创建失败 | 回 `Dirty`，旧表示保持 |
| `OutcomeMismatch` | Core 交叉校验 | 空几何却报 `Presented` | `Failed(OutcomeMismatch)` + 计数 |
| `Exception` | 任一实现（违规） | 抛异常 | 防御性捕获 ⇒ `Failed`，原因 = 异常类型 + 消息 |
| `DataMissing` | Core 对账 | 注册表有记录、世界里无 Chunk | `Failed(DataMissing)`（Core §8.5） |

**传播硬性条款**

1. 失败**只**影响该 Chunk 的调度状态：不得回滚已生效的数据、不得清脏、不得跳过未处理项；
2. 每次失败必须：`FailureReason` 非空 + `FailureCount++` + 累计计数可见（禁止静默）；
3. 重试有界（`MaxRetriesPerChunk`，Core §9.2-4）；重试必须收敛到与成功路径**相同**的结果（同内容哈希）；
4. **禁止**用"整体重置 / 重新生成整个世界 / 隐藏对象 / 强制重建"掩盖失败（Runtime §9.1-4）。

---

## 10. 所有权总表

| 资产 | 所有者 | 创建者 | 释放者 | 跨层规则 |
|---|---|---|---|---|
| `VoxelWorld`（真值容器） | 组装点 | 组装点 | 组装点 | Core 只调用其 API，不 Dispose |
| `VoxelChunk`（世界数据） | `VoxelWorld` | `GetOrCreateChunk`（**不是** Core、**不是** 生成器） | `RemoveChunk`（Core 在卸载阶段调用） | 生成器返回的实例不是它（Core 搬运） |
| 生成器产物（`VoxelChunk` 临时） | Core（临时） | Generation 实现 | GC / Core 丢弃引用 | 搬运后不得再被引用（ID-5） |
| `VoxelMeshData`（负载） | Core（临时） | Mesher 实现 | GC | 一次性；实现不得留存（§6.5） |
| 表示资源（GameObject / Mesh / Collider） | View 实现 | View 实现 | View 实现（`Release`） | Core 只发调度指令，不持有句柄（§6.6） |
| 共享材质 | 组装点/游戏层 | 组装点 | 组装点 | View 只引用，禁止销毁（View §5.6） |
| 注册表 / 队列 / 改动集 | Runtime Core | Core | Core | 纯 C#；对外只读快照（Core §4） |
| 抽象层类型 | 无一（无状态） | — | — | 不持有资源（§1.2） |

---

## 11. 验收不变量（Interface）

> 全部可用**纯 C# + Fake 实现**自动化验证（不依赖 Unity 场景）。

| # | 不变量 | 验证方法 |
|---|---|---|
| **IF1** | **依赖倒置（结构性）**：Core 源码不出现任何实现类型名与 Unity 标识符 | §2.3 扫描表 + 扫描器自检 |
| **IF2** | **抽象唯一**：`IWorldGenerator` 只在 Generation 定义；`IChunkMesher` / `IChunkPresenter` 各只定义一次 | 源码扫描（同名接口声明计数 == 1） |
| **IF3** | **接口纯度**：抽象层签名不出现 Unity 类型标识符；抽象层无字段/静态状态/算法 | 扫描抽象层源码（仅允许 interface/enum/result 声明） |
| **IF4** | **异常被防御性转换**：注入"抛异常的实现" ⇒ Core 判 `Failed` + 原因，且状态不变量仍成立 | 注入 Faulty* 假实现（三个接口各一份）跑 Tick |
| **IF5** | **契约违反可检测**：生成器返回坐标不匹配的 Chunk ⇒ `Failed(ContractViolation)` 且世界未被污染 | 注入坏生成器 + 世界快照比较 |
| **IF6** | **输出自洽交叉校验**：空几何与 `PresenterOutcome` 不一致 ⇒ `Failed(OutcomeMismatch)` + 计数 | 注入"谎报 `Presented`"的假 Presenter |
| **IF7** | **幂等**：`TryBuild` 同输入同哈希；`Apply` 重复结果一致；`Release` 重复安全 | 重复调用 + 内容哈希/计数比较 |
| **IF8** | **只读**：Mesher 不得修改输入 Chunk（4096 体素 + `Coordinate` + `IsDirty` 不变） | 快照比较（真实 Mesher 与伪 Mesher 各一次） |
| **IF9** | **不保留负载**：`Apply` 返回后释放/复用 `meshData` 引用仍正确（实现已复制） | 在 `Apply` 后立即把负载变量置空并继续 Tick，断言表示内容仍正确 |
| **IF10** | **配对调用**：每个 `Apply` 最终都有 `Release`；`LiveCount` 与已加载集合一致；卸载后回基线 | 加载/卸载循环 + `LiveCount` 断言 |
| **IF11** | **组装点唯一**：Core 不创建实现（不出现 `new` 实现类型 / `FindObjectOfType` / `Camera.main`） | 抽样扫描 + 构造依赖全部来自构造参数 |
| **IF12** | **禁重入（结构性）**：实现不得引用 `VoxelRuntimeCore` ⇒ 编译期不可能回调 | 扫描实现源码（View / Adapters / Generation 目录不得出现 `VoxelRuntimeCore`） |

---

## 12. 明确不做（V1 out of scope）

* 接口的**热替换 / 运行时重绑定**（实现与 Core 同寿命；替换需重建 Core）；
* 依赖注入框架 / 服务定位器 / 反射装配（组装点必须是显式代码）；
* 多线程 / 异步 / 协程式接口（`Task` / `IEnumerator` 均不进入接口签名）；
* 目标平台的条件编译式接口分化（同一套接口）；
* 批处理/合批、GPU 上传优化、零拷贝共享缓冲（负载按值传递语义）；
* **编辑/挖掘接口**（`IWorldEditor` 等属 `VoxelWorld_Mining.md` 的扩展，V1 不定义；未来如需可按同一依赖倒置模式新增抽象）；
* 存档读写接口（属存档层，Runtime §7.1 只定义内容契约）；
* 网络/分帧上传/跨进程接口。

---

## 13. 变更规则

1. 优先级：上游契约冲突时按 `VoxelWorld_Core.md` > `VoxelWorld_Mining.md` > `VoxelWorld_Mesh.md`
   > `VoxelWorld_Runtime.md` > `VoxelWorld_Runtime_Core.md` > 本文档。
2. **只能新增，不能改签名**：扩展能力必须引入**新接口**或**新方法**；
   修改既有接口签名 ⇒ 升版本号 + 同步更新全部实现与验收测试 + 说明兼容性。
3. 抽象层**永远不允许**出现实现细节（算法、字段、静态状态、Unity 类型）。
4. 本规范不得要求修改 `IWorldGenerator` / `VoxelMeshGenerator` / `VoxelChunkView` /
   `UnityMeshBuilder` 的既有签名与语义（WorldGeneration_Core §10：扩展不得要求修改 VoxelCore）。
5. 实现替换（如未来的 Greedy Mesher、平滑 Mesher、池化 Presenter）**必须只通过实现侧完成**，
   不得触碰 Core 与抽象层（这正是本规范的目的）。

---

## 附录 A：接口草案（完整签名）

```csharp
// ============================================================
// 抽象层：Assets/Scripts/VoxelWorld/Runtime/Abstractions/
//            namespace VoxelWorld.Runtime.Abstractions
//            纯 C#：无实现、无状态、无 Unity 类型标识符
// ============================================================

using VoxelWorld.Mesh;     // VoxelMeshData（不透明负载）

namespace VoxelWorld.Runtime.Abstractions
{
    /// Mesher 抽象（屏蔽 Mesh 层的静态类 VoxelMeshGenerator）
    public interface IChunkMesher
    {
        /// true  = 构建完成（meshData 非 null，可能为空几何；用 HasGeometry 判定）
        /// false = 失败（meshData == null，error 非空）
        bool TryBuild(VoxelChunk chunk, out VoxelMeshData meshData, out string error);
    }

    /// 呈递结果的三种取值（View §8.1：必须能区分"已呈现/已确认空几何/失败"）
    public enum PresenterOutcome { Presented, Empty, Failed }

    /// 表示抽象（Core 定义，View 实现）
    public interface IChunkPresenter
    {
        PresenterOutcome Apply(VoxelCoordinate coordinate, VoxelMeshData meshData, out string error);
        void Release(VoxelCoordinate coordinate);
        int LiveCount { get; }
    }
}

// ============================================================
// Generation：抽象已存在（不得重复定义、不得修改）
//             namespace VoxelWorld.Generation
// ============================================================
// public interface IWorldGenerator
// {
//     WorldGenerationSettings Settings { get; }
//     VoxelChunk Generate(VoxelCoordinate chunkCoordinate);
// }

// ============================================================
// 实现侧（不属抽象层；此处仅示位置与命名，本轮不落地）
// ============================================================
// Assets/Scripts/VoxelWorld/Runtime/Adapters/VoxelMeshGeneratorMesher.cs   （纯 C#）
//     public sealed class VoxelMeshGeneratorMesher : IChunkMesher
//     {  // 内部唯一允许命名 VoxelMeshGenerator 的位置
//        public bool TryBuild(VoxelChunk chunk, out VoxelMeshData meshData, out string error)
//        { /* try { meshData = VoxelMeshGenerator.Generate(chunk); error = null; return true; }
//                catch (Exception ex) { meshData = null; error = ex.Message; return false; } */ }
//     }
//
// Assets/Scripts/VoxelWorld/View/ChunkViewPresenter.cs                     （Unity 侧）
//     public sealed class ChunkViewPresenter : IChunkPresenter
//     {  // 坐标 → VoxelChunkView 映射；LiveCount = 活跃 View 数
//        // Apply  : 内部使用 VoxelChunkView.TryApply（Presented/Empty/Failed → 三态）
//        // Release: 内部 VoxelChunkView.Release 并移除映射（幂等）
//     }
//
// 组装点（游戏层）：见 §2.4
```

## 附录 B：与已有规范的一致性映射

| 本文档条款 | 上游依据 | 关系 |
|---|---|---|
| §1.1 / §2 依赖倒置 | Runtime_Core §2.1（依赖方向）、§1.2（Core 不创建实现） | 细化：给出三类接口与唯一组装点 |
| §2.3 结构性禁令 | Runtime_Core §1.1（RC-P1/P2/P4）、View §10 V11/V12（View 侧禁令） | 一致 + 补 Core/抽象/适配器三段扫描范围 |
| §3.3 不透明负载例外 | Runtime_Core §1.1（诚实说明：元数据级间接引用） | 一致：同一判据（禁标识符，不禁携带 Unity 结构体的类型） |
| §4 Generation 输入/输出/失败 | WorldGeneration_Core §2/§3/§6/§9；`IWorldGenerator` 既有契约 | 一致：**不改名、不重复定义**；Core 侧补坐标自洽校验 |
| §5 `IChunkMesher` | Mesh §1.2（只读）/ §3/§5（输出契约）/ I1；Runtime_Core §6 | 新增抽象（因 Mesh 层是静态类）⇒ **Mesh 层零改动** |
| §6 `IChunkPresenter` 三态 | View §4.3（Empty 是成功）/ §8.1（三态）/ §9.1（失败带原因）；Runtime_Core §2.4 | 一致：签名不变；补五要素契约与交叉校验 |
| §6.5 负载一次性 | View §5.2（上传保真）/ §5.6（释放）；Mesh §5（输出可丢弃） | 细化：实现不得跨调用保留负载引用 |
| §7 接口纪律 ID-1…10 | Runtime §1.4/§9；Runtime_Core §9/§10；Mining §1.1 | 一致：把散落的纪律收敛为接口层条款 |
| §8 调用时序 | Runtime_Core §11（阶段 A–H）、§6.2（配对）、§7.2（顺序） | 一致：三维接口在阶段中的调用点与配对规则 |
| §9 Failure 分类 | Runtime_Core §9.1（失败矩阵）、§8.5（对账） | 一致 + 补跨接口的统一分类与传播 |
| §10 所有权 | Runtime_Core §4.3/§5.6/§6.6/§10；View §5.6/§6.6 | 一致：显式化"不转移所有权"与各资产归属 |
| §12 编辑接口留白 | Mining §2/§7；Runtime §5.3 | 一致：编辑规则属 Mining，V1 不定义编辑接口 |
| 附录 A 适配器 | Runtime_Core 附录 A（`IChunkPresenter` 草案、`VoxelRuntimeCore` 构造） | **细化**：Core 构造参数由 3 个抽象变为 4 个（新增 `IChunkMesher`），以满足"Runtime 不依赖具体实现" |
