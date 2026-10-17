# Voxel World View Specification V1

> 适用范围：`Assets/Scripts/VoxelWorld/`（VoxelCore + Generation + Mesh + Mining + Runtime + View）体系。
> 本规范只定义 **表示层（View）**：把 Runtime 交付的 `(VoxelCoordinate, VoxelMeshData)` 变成
> 场景里可被渲染 / 可被碰撞的 Unity 对象，并在卸载时干净地释放。
> **不涉及**既有 Terrain 系统（`TerrainModificationV2` / `DensityField` / Marching Cubes 地形）。
>
> 上游规范：
> * `Specifications/World/VoxelWorld_Core.md` —— 世界真值（Voxel Data）与 Chunk 数据契约；
> * `Specifications/World/WorldGeneration_Core.md` —— 初始状态生成（Seed + ChunkCoordinate → VoxelChunk）；
> * `Specifications/World/VoxelWorld_Mesh.md` —— 派生表示的数据契约（Mesh 生成 / 子网格 / 生命周期 / 不变量）；
> * `Specifications/World/VoxelWorld_Mining.md` —— 世界数据修改（Mining 流程 / 脏规则 / 失败语义）；
> * `Specifications/World/VoxelWorld_Runtime.md` —— 调度与生命周期（加载 / 生成 / 脏 / 重建 / 上传 / 卸载）。
>
> 冲突时的优先级：**真值与数据契约** 以 `VoxelWorld_Core.md` 为准；**修改规则** 以 `VoxelWorld_Mining.md` 为准；
> **几何数据与 Meshing** 以 `VoxelWorld_Mesh.md` 为准；**调度、生命周期、流式加载** 以 `VoxelWorld_Runtime.md` 为准；
> **呈现细节（对象模型 / 资产 / 材质槽 / Collider / 释放）** 以本规范为准。
>
> 本规范**只做呈现**：生成的几何、世界的真假、改动的合法性都不由 View 决定。
> View 是**唯一允许出现 Unity 运行时类型**的层（Runtime §2）；反过来，View **不得**包含任何世界规则。

---

## 1. Core Principles（不可协商条款）

1. **View 是纯表示层，永不成为真值。**
   * 世界真值只有 Voxel 数据（VoxelWorld_Core §1）；Mesh / Collider / GameObject 全部是派生表示；
   * 删除**全部** View 对象后，世界数据必须一字不变，重新呈现后与删除前**逐元素一致**
     （Runtime R18、Mesh 规范 I12）；
   * 禁止用"场景里有什么"回答"世界里有什么"（渲染剔除、Collider 存在性、`RaycastHit` 都不是世界状态）。
2. **输入只有 `(VoxelCoordinate, VoxelMeshData)`。**
   * View **不读** `VoxelChunk`、**不读** `VoxelWorld`、**不读**生成参数与种子；
   * View 不需要知道"这个 Chunk 是怎么来的"（生成 / 改动集叠加 / 编辑后重建，一概与 View 无关）；
   * 这条同时给出了可自动化验证的接缝：View 可以在**没有任何数据层对象**的情况下被完整测试。
3. **View 不写世界数据。**
   * View **禁止**调用 `VoxelWorld.SetVoxel` / `VoxelChunk.SetVoxel` / `MarkDirty` / `ClearDirty`；
   * 脏标记的清除由 Runtime Core 在 View **报告成功之后**执行（Runtime §4.5、§8.1）；
   * 任何"顺手修一下数据"的写法都视为越界（Mining §1.6、Runtime §9.1）。
4. **呈现必须原子（Atomic Present）。**
   * 一次提交要么产出**完整**的呈现，要么保持旧呈现 / 保持空；
   * **禁止**把半成品（顶点已换、索引未换 / 索引越界 / 旧包围盒配新顶点）暴露给渲染或物理。
5. **可释放、可池化、可重建，但结果恒定。**
   * 释放不改变世界数据；同一输入缓冲重复提交 ⇒ 资产内容**逐元素一致**（幂等）；
   * 允许对象池化作为显式优化，但池化**不得**改变上述任何一条不变量。
6. **确定性。**
   * 同一 `(coordinate, VoxelMeshData)` ⇒ 完全相同的呈现结果（顶点 / 法线 / UV / 子网格分组 / 变换 / 材质槽）；
   * **禁止** `Time`、帧序、随机数、"上一次残留状态"影响上传内容（Runtime §1.4）。
7. **层边界冻结。**
   * Unity 运行时类型（`GameObject` / `Transform` / `Mesh` / `MeshFilter` / `MeshRenderer` / `MeshCollider` /
     `Camera` / `Physics`）**只允许**出现在本层（Runtime §2、§10 R16）；
   * View **禁止**引用世界规则类型（`HeightFunction` / `BasicTerrainGenerator` /
     `WorldGenerationSettings` / `TerrainModificationV2`），程序集/源码可扫描验证（§10 V11）。

---

## 2. 职责边界

| View **必须**做 | View **禁止**做 |
|---|---|
| 按 Chunk 坐标建立 / 释放容器对象，摆放为整数世界坐标（§3） | 决定"哪些 Chunk 要加载"（Runtime §6 唯一负责） |
| 把 `VoxelMeshData` 原样上传到自建 `Mesh` 资产（§5） | 生成 / 修改几何（剔除、合并、焊接、平滑、缩放） |
| 按固定顺序装配 3 个材质槽（Grass / Dirt / Stone，§6） | 触碰 Voxel 数据、脏标记、生成参数、存档 |
| 维护与之同源的 `MeshCollider`（§7） | 用 Collider / Mesh / `RaycastHit` 决定"改哪里"（§8.3） |
| 报告成功 / 失败（含原因），交给调度方决定清脏与状态切换 | 自行 `ClearDirty` / 自行切换 Chunk 生命周期状态 |
| 释放时销毁自己创建的资产与容器，保证无泄漏（§5.6 / §9） | 释放不属于自己的资产（共享材质、场景公共对象） |

> 一句话：**View 是"搬运 + 摆放 + 释放"，不是"计算"。**
> 任何需要计算的决定（几何、加载、清脏、世界规则）都必须由上游完成后再交给 View。

---

## 3. 对象模型与场景契约

### 3.1 对象构成

一个 Chunk 的呈现 = **一个容器** + **一组组件**：

```
ChunkView (coordinate)                      ← 容器（GameObject）
├── Transform        position = coordinate * 16（整数、一次性计算）
│                    rotation = identity, lossyScale = 1
├── MeshFilter       sharedMesh = <View 自建的 Mesh 资产>（Presented 状态必须非空）
├── MeshRenderer     sharedMaterials = [Grass, Dirt, Stone]（长度固定 3，顺序固定）
└── MeshCollider     sharedMesh = 同一个 Mesh 资产（§7；可显式关闭构建）
```

* 容器**恰好**是显式 Root 的**直接子级**：`transform.SetParent(root, false)` 后写 `localPosition`；
* 容器**禁止**携带任何世界语义组件（`Rigidbody`、自定义 gameplay `MonoBehaviour`、`Terrain`、
  触发器回调等）——世界规则属于数据层与 Runtime；
* 层级 / 层（Layer）/ 命名全部**显式配置**，禁止隐式改全局设置（`Physics` 设置、`Tag` 注册等）。

### 3.2 命名与诊断可读性

容器名称必须**可反解出 Chunk 坐标**（例如 `ChunkView (2, 0, -1)`），目的是让批处理验证与
运行期诊断都能"从场景对象名读出坐标"，从而与 Runtime 的已加载集合直接比对（§10 V15）。

### 3.3 Transform 契约（硬性）

| 项 | 契约 |
|---|---|
| 位置 | `localPosition == (coordinate.x, coordinate.y, coordinate.z) * ChunkSize`，**整数、一次性计算** |
| 旋转 | `identity`（禁止旋转，否则面缝 / 重叠） |
| 缩放 | `localScale == Vector3.one`，且 **父节点链的 `lossyScale == 1`**（父节点带缩放会破坏面接缝） |
| 父级 | 显式 Root；`SetParent(root, worldPositionStays: false)` |
| 变更频率 | **只在创建 / 重挂时写一次**；禁止每帧写 Transform（禁止依赖 `hasChanged` 驱动逻辑） |

> **远原点精度（V1 明确范围）**：位置是 `chunkCoordinate * 16` 的**一次性整数计算**，
> 不得用"逐 Chunk 累加"推算（会累积浮点误差）。
> 当 `|coordinate * 16|` 超过 float 精确整数范围（2²⁴）时，需要"原点重定中心（floating origin）"
> 方案——**属于后续独立课题，V1 不处理**，但 V1 必须保证"一次性计算 + 不在 View 内做世界坐标运算"，
> 免得未来无法平滑升级。

---

## 4. View 生命周期

View 自身的状态（与 Runtime 的 Chunk 状态机是**投影关系**，Runtime §4 仍是唯一权威）：

| 状态 | 含义 | 进入条件 | 离开条件 |
|---|---|---|---|
| `Absent` | 无容器、无资产 | 初始；释放完成 | 调度方请求创建 → `Empty` |
| `Empty` | 容器已建、已确认**无几何**（`sharedMesh == null`） | 创建后首次提交空缓冲（`HasGeometry == false`） | 有几何提交 → `Presented`；释放 → `Absent` |
| `Presented` | 容器 + 资产，内容与**最近一次输入缓冲**一致 | 非空缓冲提交成功 | 新缓冲提交（留在 `Presented`）/ 空缓冲提交 → `Empty` / 释放 → `Absent` |
| `Released` | 容器与资产都已销毁（等价 `Absent` + 计数回基线） | `Release()` | — |

规则：

1. **只接受数据就绪的输入**：View 不得接收 `Generating` 中的 Chunk 的输入
   （那会产出空 Mesh 并让调度方误清脏 ⇒ 永久缺面；Runtime §5.3）；
2. **状态与 Runtime 的映射**：Runtime 的 `Building` ↔ View 的提交中；
   Runtime 的 `Ready` ⇒ View 必须是 `Presented` 或 `Empty`，且内容 == 当前数据（§10 V10 / V6）；
3. **`Empty` 不是失败**：数据确认无几何（例如全 Air Chunk）是**正常成功**，
   必须报告成功（调度方据此清脏）；
4. **释放是幂等的**：对已释放的 View 再调 `Release()` 必须安全（不得重复销毁 / 不得抛错）；
5. **禁止跨坐标复用内容**：容器可以池化复用，但**内容必须整体覆盖**，
   不允许"新 Chunk 沿用上一个 Chunk 的部分三角形"。

---

## 5. Mesh 资产契约

### 5.1 创建（独占）

* 每个 `Presented` 的 View **恰好**拥有 1 个**自建** `Mesh` 资产（`new Mesh()`）；
* **禁止**共享 / 复用别的 Chunk 的 Mesh，**禁止**修改 `Resources` / 项目里的共享资产；
* 资产的写权限只属于其拥有者（View），其它任何系统不得写它。

### 5.2 上传（保真、无加工）

上传必须**逐项照搬**输入缓冲，不许做任何"优化"：

| `VoxelMeshData` | Unity 资产写入 | 约束 |
|---|---|---|
| `Vertices` | `mesh.SetVertices(Vector3[])` | 顺序不变（Chunk Local，1 单位 = 1 体素） |
| `Normals` | `mesh.SetNormals(Vector3[])` | 顺序不变；**禁止** `RecalculateNormals`（法线由 Mesh 规范 §3.1-6 决定） |
| `UVs` | `mesh.SetUVs(0, Vector2[])` | 顺序不变（每面 `(0,0)→(1,0)→(1,1)→(0,1)`） |
| `Triangles[i]` | `mesh.subMeshCount = 3` 后 `SetTriangles(int[], i)` | **必须按 submesh 分组写入**，不得合并成单一 submesh |
| `SubmeshCount = 3` | 固定（Grass=0 / Dirt=1 / Stone=2） | 不得重排、不得省略空 submesh（空 submesh 允许写入 0 个索引） |

硬性顺序（否则必然短暂出现非法资产）：

```
1) mesh.indexFormat = <按顶点数显式选择：≤65535 → UInt16；>65535 → UInt32>
2) mesh.subMeshCount = 3
3) SetVertices → SetNormals → SetUVs(0, …)
4) 逐个 submesh SetTriangles(…)
5) 设置 bounds（§5.4）
6) 提交给 MeshFilter / MeshCollider（§5.5）
```

### 5.3 索引格式（必须显式，禁止依赖默认值）

* 顶点数 `≤ 65535` ⇒ 允许 `IndexFormat.UInt16`（默认值，但**仍须显式设置**）；
* 顶点数 `> 65535` ⇒ **必须** `IndexFormat.UInt32`，否则索引截断 ⇒ 三角形错乱（且不会报错）；
* `indexFormat` 必须在**写入任何几何之前**设定；
* V1 的 Cube Meshing（每 quad 4 顶点、不做焊接）在 16³ Chunk 上的顶点数上界为
  `6 × 2048 × 4 = 49152`（最大独立实体集 = 棋盘格），因此 UInt16 足够；
  **但规范不把该上界当作实现假设**——实现必须按顶点数选择并断言，
  以便未来引入 greedy meshing / 顶点焊接 / 跨 Chunk 剔除后仍然安全。

### 5.4 包围盒（渲染正确性的前提）

* 必须显式设置 `mesh.bounds` 或调用 `RecalculateBounds()`；**禁止**保留默认（零尺寸）包围盒——
  那会让 `MeshRenderer` / `MeshCollider` 因包围盒为空而被整体剔除（"看不见的世界"）；
* 契约：`bounds` == 输入顶点集合的 AABB（Chunk Local 空间，容差 0），且包含于 `[0, 16]³`；
* 空几何 ⇒ 不创建资产（§5.5），不产生"空 Mesh 资产"。

### 5.5 原子替换（两种允许路径）

**路径 A（V1 推荐）：新建资产 + 整体替换**

```
newMesh = 按 §5.2 完整写入
meshFilter.sharedMesh = newMesh
meshCollider.sharedMesh = newMesh            （若启用 Collider）
Object.Destroy(旧资产)                        （替换成功之后）
→ 返回"成功"；由 Runtime Core 执行 ClearDirty
```

**路径 B（复用同一资产）：限制条件下允许**

* 必须先把索引清空（先"撤下"旧几何）再改顶点，重新写回索引后统一提交；
* 提交期间该 Chunk 不得被判定为 `Ready`；
* 无论哪条路径，都必须满足：**观察者永远看不到越界索引或"顶点已新、索引还旧"的组合**。

硬性条款：

1. **只用 `sharedMesh`**：运行时**禁止**使用 `MeshFilter.mesh` 取值（它会复制资产 ⇒ 泄漏 + 内容错位）；
2. **提交后才清脏**：View 只报告成功；`ClearDirty` 与状态切换由 Runtime Core 执行（§1.3）；
3. **失败保持旧呈现**：任何步骤失败 ⇒ 释放本次临时资产 → 旧呈现保持原样 → 返回失败（§9）；
4. **不呈现即不脏清**：若本次提交没有真正完成呈现，绝不允许上游把该 Chunk 标成 `Ready`。

### 5.6 释放

* 释放顺序：`MeshCollider.sharedMesh = null` → `MeshFilter.sharedMesh = null` →
  销毁自建 `Mesh` 资产 → 销毁容器（或收入池，§9.3）；
* **共享材质不得销毁**（不属于 View 自有资产）；
* 释放必须成对，且**不得依赖** `Resources.UnloadUnusedAssets` 之类的兜底手段；
* 释放的幂等性与"无泄漏"都是验收不变量（§10 V9）。

---

## 6. 材质与外观契约

1. **材质槽顺序固定**：`MeshRenderer.sharedMaterials` 长度 == `VoxelMeshData.SubmeshCount`（3），
   槽序号与 submesh 一一对应：
   `0 → Grass`、`1 → Dirt`、`2 → Stone`（Mesh 规范 §3.1-8；**禁止重排**）；
2. **共享优先**：默认使用配置里给定的**共享材质**（`sharedMaterials`），
   禁止为每个 Chunk 实例化材质（材质实例数必须与 Chunk 数无关，§10 V7）；
3. **外观不参与真值**：材质 / Shader / 贴图 / 颜色 / 阴影 / 光照配方全部是纯配置，
   替换外观**不得**改变几何、不得改变数据、不得触发重建（除显式重新呈现外）；
4. **材质缺失不改变几何**：配置缺失（null / 数量不足）时用占位材质并**如实上报**，
   但**禁止**因此丢弃三角形数据（几何保真优先，§10 V3）；
5. **不引入外观复杂度**：Shader / 贴图 / 光照 / LOD / 后处理**不在本规范范围**（§11）。

---

## 7. Collider 契约

1. **同源**：`MeshCollider.sharedMesh` 必须指向**同一个** Mesh 资产（
   禁止"呈现一份几何 + 碰撞另一份几何"——两者必然不同步）；
2. **静态非凸**：`convex == false`、`isTrigger == false`；Chunk 容器**禁止**挂 `Rigidbody`
   （体素地形不是刚体方块模拟，Runtime §11）；
3. **不是真值**：任何 gameplay 判定必须走数据层 `VoxelWorld.GetVoxel` / `TryGetVoxel`
   （Runtime §8.2）；Collider 只用于"被物理系统/查询层利用"；
4. **允许滞后**：Collider 的更新可以独立于呈现调度（例如按帧预算延迟），
   但**不得**参与"数据是否一致"的判定，也不得让它成为上游清脏的前置条件；
5. **可显式关闭**：配置可关闭 Collider 构建（例如纯观察视角）；关闭时 `MeshCollider` 组件不得残留空资产。

---

## 8. 接口（与 Runtime / 查询层的接缝）

### 8.1 Runtime Core → View（唯一入口）

```
Create(coordinate)                         → 建立容器（状态 Empty）
TryApply(coordinate, meshData, out error)  → 提交呈现（成功 / 失败 + 原因）
Release()                                  → 释放容器与自有资产
```

* `TryApply` 的输入**只有** `VoxelCoordinate` + `VoxelMeshData`（§1.2）——
  签名中**不得**出现 `VoxelChunk` / `VoxelWorld` / `Transform` 之外的场景对象；
* 返回值必须能区分"已呈现 / 已确认空几何 / 失败（带原因）"三种结果（§4.3、§9.1）；
* 调用方（Runtime Core）在收到成功后执行 `ClearDirty` 与状态切换（§1.3）。

### 8.2 View → 诊断 / 验证

只读暴露：`Coordinate`、`HasGeometry`、容器与资产引用（供批处理验证读取），
以及可选计数（§9.2）。**不得**暴露"写世界"的入口。

### 8.3 View → 查询层（独立规范的接缝）

* "射线 / 交互 → 目标 `VoxelCoordinate`" 属于**查询层**（Mining §1.2、§9、Runtime §8.3）：
  查询层可以把 View 的 Collider / Mesh 当作**输入辅助**（例如 `Physics.Raycast` 找面）；
* 查询层的**唯一输出**必须是 `VoxelCoordinate`（可附带面方向）；
* View **不得**把命中结果直接写进世界；修改只能经 Mining / Runtime 的数据路径
  （`VoxelWorld.SetVoxel`）；
* 查询层自身的规则不在本规范范围（§11）。

---

## 9. 失败语义、预算与诊断

### 9.1 失败语义

| 情形 | 判定 | 必须的行为 |
|---|---|---|
| 输入缓冲非法（长度为 0 却有索引 / null 数组 / 索引越界） | 失败 | 不触碰现有呈现；释放本次临时资产；返回原因 + 日志；**禁止静默** |
| 资产创建失败（`new Mesh()` 异常 / 组件缺失） | 失败 | 保持旧呈现或保持 `Empty`；向上报告；**不得**让上游清脏 |
| 提交中途失败 | 失败 | 回滚到"旧呈现完整"或"空呈现"二态（不出现半成品）；返回原因 |
| 空几何（`HasGeometry == false`） | **成功** | 撤下几何（转 `Empty`）；报告成功（§4.3） |
| 释放时资产已被外部销毁 | 成功（幂等） | 不抛错；继续完成剩余释放步骤；计一次异常计数 |
| 配置非法（Root 为空 / 材质数量不足 / 父节点带缩放） | 拒绝启动 | **启动前**校验（显式），禁止运行中静默修正 |

**硬性条款**

1. **禁止半成品**：任何失败都不得留下"越界索引 / 半更新资产 / 组件与资产不匹配"的状态（§1.4）；
2. **不得代替上游决策**：View 不 `ClearDirty`、不切换 Chunk 状态、不丢弃编辑请求（§1.3）；
3. **失败可观测**：返回结构化原因 + 日志 + 累计计数（§9.2）；
4. **禁止用表示层补丁掩盖数据层缺陷**：缺面 / 穿透 / 闪烁必须回到"Voxel 数据 + Meshing 规则"定位
   （Mesh 规范 §10.3、Mining §1.6、Runtime §9.1）。

### 9.2 诊断（只读）

必须可观测（供 `Tools/VoxelWorld/*` 类批处理验证入口读取，且不得修改世界状态）：
`LiveViewCount`、`PresentedCount`、`EmptyCount`、`PooledCount`、`LiveMeshAssetCount`、
累计 `Uploaded / Released / Failed / Rejected`、`LastApplyMs`。

### 9.3 对象池（允许，但受约束）

池化是**实现优化**，不是规范要求；启用时必须满足：

1. 池中对象 `activeSelf == false`，**不可见、不可碰撞**（`MeshCollider.sharedMesh == null`）；
2. 出池复用时**内容整体覆盖**（禁止沿用上一个 Chunk 的残留几何）；
3. 池化不得改变不变量 §10 V9（无泄漏）与 V14（幂等）的判定方式；
4. 池容量显式配置（有上限），不得无限增长。

### 9.4 并发（V1 约束）

* V1 的资产创建 / 上传 / 释放**只在主线程**执行（`UnityEngine.Object` 的 API 不是线程安全的）；
* 若未来引入后台构建：几何构建在私有副本上完成，**提交（写 Unity 资产）仍必须回主线程**
  （Runtime §9.3）。

---

## 10. 验收不变量（View）

> 每条必须可自动化验证（对象 / 资产 / 缓冲比较 + 结构扫描），不依赖人眼。
> 可在 **EditMode** 下用"合成 `VoxelMeshData` + 显式 Root"构造，无需数据层对象（§1.2）。

| # | 不变量 | 验证方法 |
|---|---|---|
| **V1** | **变换契约**：`localPosition == coordinate*16`（整数）、`rotation == identity`、`lossyScale == 1`、父级 == 显式 Root | 逐 View 断言 Transform 各分量（含负坐标 Chunk） |
| **V2** | **资产独占**：每个 `Presented` View 恰好 1 个自建 `Mesh`，且不被其它 View 引用 | 遍历所有 View 的 `sharedMesh`，断言唯一性与计数（无跨 Chunk 共享） |
| **V3** | **上传保真**：资产内容与输入缓冲逐元素一致（`vertices` / `normals` / `uv` 顺序 + 每个 submesh 的索引） | 读回 `mesh.vertices` / `normals` / `uv` / `GetTriangles(i)` 与 `VoxelMeshData` 比较 |
| **V4** | **索引格式安全**：写入前显式设定；顶点数 `> 65535` ⇒ `UInt32` | 喂入合成的超大缓冲（顶点 > 65535）⇒ 断言 `indexFormat` 与索引内容正确 |
| **V5** | **包围盒正确**：`mesh.bounds` == 输入顶点 AABB（容差 0）且 ⊂ `[0,16]³` | 与独立重算的 AABB 比较（防止零尺寸 bounds 导致整体被剔除） |
| **V6** | **空几何语义**：`HasGeometry == false` ⇒ 无呈现几何、无 Collider 资产，且**报告成功** | 全 Air Chunk 输入 ⇒ 断言 `Empty` 状态 + 成功返回 |
| **V7** | **材质槽映射**：`sharedMaterials.Length == 3`，槽 0/1/2 == Grass/Dirt/Stone，材质为共享引用 | 断言数组长度、逐槽引用；材质实例计数与 Chunk 数无关 |
| **V8** | **原子替换**：失败注入下资产恒为"某次完整上传的结果"或空，绝不出现半成品；且不触发清脏 | 注入非法缓冲 / 异常 / 中途失败 ⇒ 读回旧资产 + 断言 `IsDirty` 仍为 true |
| **V9** | **释放即净（无泄漏）**：`Release()` 后容器与自有资产被销毁；N 次创建/释放循环计数回基线 | 基线计数比较（含池化实现：池中对象不可见、Collider 为空） |
| **V10** | **呈现-数据一致**：`Ready` 的 View 资产内容 == 用当前 Chunk 数据重新 `Generate` 的结果 | View 侧独立重算并比较（与 Runtime R2 对齐，双保险） |
| **V11** | **无世界规则（结构性）**：View 不引用生成 / 挖掘类型；输入契约不含 `VoxelChunk` | 源码 / 程序集扫描（禁止 `HeightFunction` / `BasicTerrainGenerator` / `WorldGenerationSettings` / `TerrainModificationV2`）+ API 签名检查 |
| **V12** | **只读数据层**：View 任何操作不改变 Voxel 数据（4096 体素 + `Coordinate` + `IsDirty`）；`ClearDirty` 不由 View 调用 | 数据快照比较 + 调用图检查（View 程序集不出现 `SetVoxel` / `MarkDirty` / `ClearDirty`） |
| **V13** | **物理所有权**：容器无 `Rigidbody`；`MeshCollider` 为静态非凸、`sharedMesh` 与呈现同源 | 组件扫描 + 引用相等断言 |
| **V14** | **幂等上传**：同一 `(coordinate, buffer)` 连续提交两次 ⇒ 内容逐元素一致且资产计数不变 | 两次提交后比较 + 计数断言 |
| **V15** | **层级与命名稳定**：所有容器是 Root 的直接子级；名称可反解坐标；释放后 Root 下无残留 | 遍历 Root 子级与期望集合比较（加载/卸载循环后仍成立） |
| **V16** | **View 不持久化**：Mesh / Collider / 容器 / 材质实例均不进入存档 | 结构检查：存档路径只包含 `Seed + RuntimeSettings + 改动集`（Runtime §7.1） |

任何一条不变量被破坏，都视为 **View 缺陷**（而非数据缺陷）；
修复必须落回本规范与上游规则，禁止"在渲染侧打补丁"。

---

## 11. 明确不做（V1 out of scope）

* 查询层的规则：射线解析、命中判定、交互高亮、选中轮廓、放置预览（§8.3）；
* 光照 / Shader / 贴图 / 材质资产 / 后处理 / 阴影配方（§6.5）；
* LOD、遮挡剔除、视锥外裁剪策略、批处理 / GPU Instancing / `GraphicsBuffer` / Compute Shader；
* greedy meshing、顶点焊接、跨 Chunk 剔除（Mesh 规范 §4 B3）——它们属于 Mesh 层，不属于 View；
* 后台线程上传、异步 GPU 上传、`Mesh.MarkDynamic` 之外的动态更新策略；
* 原点重定中心（floating origin）与超大世界坐标精度方案（§3.3）；
* 玩法层：掉落物、粒子、音效、动画、方块交互反馈；
* 场景 / 相机 / 焦点管理（Runtime §3）；存档的字节布局（存档层）；
* 对象池的具体实现策略（允许优化，但必须满足 §9.3 与 §10 的不变量）。

---

## 12. 变更规则

1. 与上游冲突时的优先级：`VoxelWorld_Core.md`（真值）> `VoxelWorld_Mining.md`（修改）
   > `VoxelWorld_Mesh.md`（几何数据）> `VoxelWorld_Runtime.md`（调度）> 本规范（呈现细节）。
2. 任何修改若触及 §1（原则）、§5（资产契约）或 §10（不变量），必须：
   ① 升版本号；② 同步更新验收测试；③ 说明与既有存档（`Seed + 改动集`）的兼容性。
3. **禁止**用 View 侧的补丁（强制重建、隐藏对象、重置资产、绕过 bounds）掩盖数据层或 Meshing 缺陷。
4. V1 实现范围：**`(VoxelCoordinate, VoxelMeshData)` → 容器 + Mesh 资产 + 材质槽 + Collider，
   以及可释放 / 可幂等重传**；§11 的内容一律不得进入 V1。
5. 本规范不得要求为了 View 而修改 VoxelCore / Generation / Mesh / Mining 的既有契约
   （WorldGeneration_Core §10：扩展不得要求修改 VoxelCore）。

---

## 附录 A：建议接口草案（实现阶段按此落地）

```csharp
namespace VoxelWorld.View
{
    /// 显式配置：所有影响呈现的输入都必须显式传入（无隐式查找）。
    public sealed class ChunkViewSettings
    {
        public UnityEngine.Transform Root;              // 必填：容器父节点（父链 lossyScale 必须 == 1）
        public UnityEngine.Material[] SubmeshMaterials; // 长度 3，顺序固定 Grass/Dirt/Stone
        public bool BuildCollider = true;               // 是否构建 MeshCollider（§7.5）
        public int Layer;                               // 显式 Layer（禁止改全局设置）
    }

    /// 单个 Chunk 的呈现。状态：Absent / Empty / Presented（§4）。
    public interface IChunkView
    {
        VoxelCoordinate Coordinate { get; }
        bool HasGeometry { get; }

        /// 提交呈现：输入只有坐标 + 纯数据缓冲（§1.2）；
        /// 返回 true = 已完整呈现（或已确认空几何）；false = 失败，error 必须给出原因。
        bool TryApply(VoxelCoordinate coordinate, VoxelMeshData meshData, out string error);

        /// 释放容器与自有资产（幂等；不触碰数据层，§5.6）。
        void Release();
    }

    /// View 的创建入口：由 Runtime Core 调用（Runtime §8.1）。
    public interface IChunkViewFactory
    {
        IChunkView Create(VoxelCoordinate coordinate);
    }
}
```

**接口层禁令（可扫描验证）**：以上签名与实现中不得出现
`VoxelChunk` / `VoxelWorld` / `SetVoxel` / `MarkDirty` / `ClearDirty` /
`HeightFunction` / `BasicTerrainGenerator` / `WorldGenerationSettings` / `TerrainModificationV2`。

## 附录 B：与 Runtime 的对应关系

| Runtime 概念（`VoxelWorld_Runtime.md`） | View 侧对应 | 关系 |
|---|---|---|
| Chunk 状态 `Building`（§4） | 一次 `TryApply` 正在进行 | 提交期间不得判定为 `Ready` |
| Chunk 状态 `Ready`（§4） | `Presented` 或 `Empty` | `Ready ⇒ 内容 == 当前数据`（View V10 / Runtime R2） |
| `ClearDirty`（§4.5 / §5 G 阶段） | **View 不做** | 由 Runtime Core 在 View 报告成功之后执行 |
| `MaxUploadsPerFrame`（§3） | 每帧 `TryApply` 次数上限 | 预算由 Runtime 控制，View 不做调度 |
| 空几何（Runtime §8.1.4） | `Empty` 状态 + 报告成功 | 数据一致即事实，不因"没有几何"而失败 |
| 卸载释放（Runtime §6.3） | `Release()`（资产 → 容器） | 顺序：`MeshCollider` → `MeshFilter` → 资产 → 容器 |
| `MeshCollider` 契约（Runtime §8.2） | §7 同源 / 静态非凸 / 不是真值 | View 负责装配，语义由数据层决定 |
| 输入解析接缝（Runtime §8.3） | §8.3：只输出 `VoxelCoordinate` | View 不写世界，修改只能经 Mining / Runtime |
| 层边界（Runtime §2 / §10 R16） | 本层是**唯一**允许 Unity 类型的层 | 反向要求：View 不含世界规则（V11） |
