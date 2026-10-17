# Voxel World Mesh Specification V1

> 适用范围：`Assets/Scripts/VoxelWorld/`（VoxelCore + Generation）体系。
> 本规范只定义 **派生表示层（Mesh）**，不涉及既有 Terrain 系统
> （`TerrainModificationV2` / `DensityField` / Marching Cubes 地形）——那套体系与本文档无关。
>
> 上游规范：
> * `Specifications/World/VoxelWorld_Core.md` —— 世界真值（Voxel Data）与 Chunk 数据契约；
> * `Specifications/World/WorldGeneration_Core.md` —— 初始世界状态生成（Seed + ChunkCoordinate → VoxelChunk）。
>
> 关系：**生成 = 初始状态，编辑 = 运行时改动，Mesh = 从 VoxelChunk 派生的表示**。
> 冲突时以 `VoxelWorld_Core.md` 为准。

---

## 1. Core Principles（不可协商条款）

1. **Mesh 不是世界真值。**
   * 任何 gameplay / 查询 / 存档逻辑都不得读取 Mesh 作为世界状态；
   * Mesh 可以随时被删除并重新生成，删除 Mesh 不改变世界状态；
   * 存档只保存 `Seed + 改动过的 Voxel 数据`（VoxelWorld_Core §6），**不保存 Mesh**。
2. **`VoxelChunk` 是 Meshing 的唯一输入。**
   * Mesher 只读取一个 `VoxelChunk`（以及本规范定义的常量/映射表）；
   * 不读取 `VoxelWorld`、不读取相邻 Chunk 的数据、不读取 Unity `Transform`、
     不读取任何全局/静态可变状态、不读时间/帧序；
   * Mesher **只读**：绝不调用 `SetVoxel/MarkDirty`，绝不写回 Voxel 数据。
3. **确定性（与 WorldGeneration_Core §2 同级约束）。**
   * 同一个 Chunk 数据 ⇒ 完全相同的 Mesh 输出（顶点/索引/子网格的顺序也一致）；
   * 结果不依赖构建顺序、帧时间、已加载的 Chunk 集合、平台无关的随机性。
4. **第一阶段采用 Cube Surface Meshing（体素面暴露法）。**
   * 每个实体 Voxel 的每个“暴露面”生成 1 个正方形（quad，2 三角形）；
   * **本阶段不实现 Marching Cubes**，不做任何平滑/等值面/顶点插值；
   * 后续若引入平滑方案，必须作为**独立的新版本**，且不得破坏第 8 章的不变量。

---

## 2. 数据与坐标契约（引用 VoxelCore）

| 项 | 契约 | 来源 |
|---|---|---|
| Chunk 尺寸 | `VoxelChunk.ChunkSize = 16`（每轴 16 体素）；`VoxelCount = 4096` | VoxelCore |
| 体素尺寸 | 1×1×1 world unit | VoxelWorld_Core §2 |
| 局部坐标 | `localX/localY/localZ ∈ [0, 15]`（越界由 VoxelCore 抛异常） | VoxelCore |
| 世界坐标 | `world = chunkCoordinate * 16 + local`；半开区间体素 `[x,x+1)×[y,y+1)×[z,z+1)` | VoxelCore §5 |
| 实体判定 | `Voxel.IsSolid() == (Type != Air)` | VoxelCore §2 |
| Chunk 世界原点 | `chunk.Coordinate * ChunkSize`（体素单位，无 Unity Transform 参与） | VoxelCore |
| 脏标记 | `VoxelChunk.IsDirty / MarkDirty / ClearDirty`；`VoxelWorld.CollectDirtyChunks / ClearDirtyFlags` | VoxelCore §5 |

**Mesh 坐标空间**：Mesh 顶点一律位于 **Chunk Local Space**，
单位 = 1 体素，范围只能是 `[0, 16]`（含边界，闭区间）内的面片；
世界摆放由表现层 Transform（`worldOrigin = chunkCoordinate * 16`）负责，
Mesher 不参与、也不知道世界位置。

---

## 3. Face 生成规则

对 Chunk 内每个 Voxel `v` 与 6 个方向 `d ∈ {+X,−X,+Y,−Y,+Z,−Z}`：

```
if (!v.IsSolid())            → 不生成任何面（Air 永不产生几何）
neighbour = v + d（世界坐标下的相邻 Voxel）
if (neighbour 是实体)         → 不生成该面
else                          → 生成 1 个 quad（该面暴露）
```

### 3.1 面的几何构造（可直接实现、可校验）

设体素 `v` 占据 `[x,x+1]×[y,y+1]×[z,z+1]`（Chunk Local），方向 `d` 为主轴方向：

1. **面平面**：`d` 为正方向时取该轴的 `+1` 平面，反之取 `+0` 平面
   （例：`+Y` 面平面 `y = y+1`；`−Y` 面平面 `y = y`）。
2. **切向轴序** `(t1, t2)` 取满足 **`cross(e_t1, e_t2) = d`**（右手系）的一对：

   | d | t1 | t2 |
   |---|---|---|
   | +X | +Y | +Z |
   | −X | +Z | +Y |
   | +Y | +Z | +X |
   | −Y | +X | +Z |
   | +Z | +X | +Y |
   | −Z | +Y | +X |

3. **P0** = 面矩形上 `t1`、`t2` 两轴都取最小值的角点。
4. **顶点顺序（从外侧看逆时针，CCW）**：
   `v0 = P0`、`v1 = P0 + e_t1`、`v2 = P0 + e_t1 + e_t2`、`v3 = P0 + e_t2`。
5. **三角形化**：`(v0, v1, v2)` 与 `(v0, v2, v3)`（固定顺序，不允许实现随机翻转）。
6. **法线不变量**：`cross(v1−v0, v2−v0)` 与 `d` 同向（即 `dot(normal, d) > 0`），
   面法线 = `d`（立方体面法线只允许 6 个轴向值）。
7. **UV**：按 `(t1, t2)` 顺序取 `(0,0) → (1,0) → (1,1) → (0,1)`（每面 0..1，
   本阶段不做图集/分面贴图差异）。
8. **材质与子网格**：`submesh = (int)VoxelType − 1`
   （`Grass → 0`、`Dirt → 1`、`Stone → 2`；`Air` 不产生几何）。
   材质外观（贴图/着色）不属于本规范。

### 3.2 禁止项（Face 级）

* 同一个 `(voxel, direction)` 在一次 Meshing 中**最多生成一个 quad**（禁止重复面）；
* 禁止 Air 面、禁止实体-实体内部面（含跨 Chunk 的实体-实体面，见 §4）；
* 本阶段不做 greedy meshing / 合并共面 quad / 顶点焊接优化；
  若未来加入，必须保持“quad 计数不变式”（§8 I2）与确定性。

---

## 4. Chunk 边界规则

**B1（默认，Phase 1 唯一允许的实现方式）：越界邻居按 Air 处理。**
超出本 Chunk 的邻居 Voxel（例如 `localX=0` 的 `−X` 邻居）不查询任何数据，
一律视为 **Air** ⇒ 在 Chunk 边界面上生成面片。

* 目的：保证 **Chunk 可独立 Meshing**（WorldGeneration_Core §6 的同级要求）：
  任意顺序、任意已加载集合下，同一个 Chunk 的 Mesh 100% 可复现；
* 代价：相邻 Chunk 在共享面上可能各生成一份共面 quad（“边界重复面”是**预期行为**，
  Phase 1 不要求去重，也不做跨 Chunk 合并）；
* 该规则使“一次编辑只影响所属 Chunk 的 Mesh”成立（§6 重建范围）。

**B2：边界坐标必须严格对齐。**
`localX=0` 的面平面 = `chunkOriginX`；其外侧邻居是世界体素 `(chunkOriginX−1)`，
属于 Chunk `(chunkX−1)` 的 `localX=15`。因此：

```
chunk(c).localX=15 与 chunk(c+1).localX=0 在共享平面上完全重合
（同理适用于 ±Y / ±Z，以及负坐标 Chunk）
```

两侧数据一致时（生成器保证列连续），两个 Chunk 各自生成的面片在同一世界平面上，
不产生缝隙也不产生重叠判定歧义。

**B3（可选、后续阶段）：跨 Chunk 剔除模式。**
允许在“邻居 Chunk 数据可用”时跳过共享面，但必须：
① 作为**独立显式开关**（默认关闭 = B1）；
② 保持确定性（同一输入集合 ⇒ 同一输出；不得依赖加载顺序）；
③ 绝不写入/修改任何 `VoxelChunk`；
④ 若开启，必须同步扩展 §6 的“重建范围”规则（跨边界编辑需同时标脏邻居）。

**B4：边界面的顶点范围**仍必须在 `[0, 16]` 闭区间内（不含任何外扩）。

---

## 5. Meshing 输入/输出契约（唯一输入 = VoxelChunk）

```
VoxelChunk (只读)
      │
      ▼
CubeSurfaceMesher.Build(chunk)        ← 纯函数式：无状态、无副作用
      │
      ▼
MeshBuffers / Mesh 数据
  · vertices : Vector3[]   （Chunk Local，1 单位 = 1 体素）
  · normals  : Vector3[]   （每个面 = 轴向单位向量）
  · uvs      : Vector2[]   （每面 0..1，见 §3.1-7）
  · submeshes: int[][]     （索引按 submesh 分组，顺序固定 Stone→? 见 §3.1-8）
```

约束：

* 输入只允许 `VoxelChunk`（+ 本规范的常量）；不得传入 `VoxelWorld`、邻居 Chunk、
  `Transform`、相机、时间等任何外部状态；
* 输出可以构建为临时缓冲或引擎 Mesh（实现自由），但**必须可丢弃**：
  删除输出后重新 `Build` 必须得到**完全相同**的缓冲；
* 输出不得反向影响输入（`chunk.IsDirty`、体素数据、Chunk 坐标都不变）。

---

## 6. Mesh 生命周期

| 状态 | 含义 | 进入条件 | 离开条件 |
|---|---|---|---|
| `Absent` | 尚未构建 / 已释放 | 初始；释放 Mesh 后 | 触发构建 |
| `Building` | 正在构建（可选状态，用于分帧） | 调度重建 | 成功 → `Ready`；失败 → `Dirty` |
| `Ready`（= chunk 非脏） | Mesh 与当前 Voxel 数据一致 | 构建成功 | Voxel 数据变化 → `Dirty` |
| `Dirty`（= `chunk.IsDirty`） | 数据已变、Mesh 过期 | `SetVoxel` 实际改变数据；新 Chunk | 构建成功并 `ClearDirty` |

规则：

1. **Mesh 归表现层所有**（`TerrainChunkView` 式包装层）：Mesh / MeshFilter / Collider
   都是派生表示；`VoxelWorld` 与 `VoxelChunk` 不持有、不知道 Mesh 的存在。
2. **原子替换**：一次构建要么产出完整 Mesh，要么保持旧 Mesh / 保持 `Dirty`；
   禁止把“半成品 Mesh”呈现为 `Ready`。
3. **失败可重试**：构建失败（异常/预算中断）时 **不得** `ClearDirty`；
   下一次调度必须能重新构建并得到与成功路径相同的结果。
4. **可随时释放**：释放 Mesh 只释放表示，不改变 Voxel 数据、不改变 `IsDirty`
   的语义（释放后可按需重建，结果与释放前一致）。
5. **不落盘**：Mesh 永不写入存档；重开世界由 `Seed + 改动数据` 重建
   （WorldGeneration_Core §9 / VoxelWorld_Core §6）。

---

## 7. Dirty / Rebuild 规则

1. **置脏（数据侧，已由 VoxelCore 保证）**
   * `VoxelChunk.SetVoxel(local, type)`：类型**实际变化** ⇒ `IsDirty = true`；
     写入相同类型 ⇒ 不置脏、返回 `false`；
   * 新建 Chunk 初始为 `Dirty`（生成器输出即未构建状态）。
2. **重建范围（Phase 1）**：**只重建 owning Chunk**。
   依据 B1（越界邻居视为 Air），Voxel 数据变化不会影响任何其它 Chunk 的 Mesh，
   因此**禁止**“改 1 个体素 ⇒ 重建全世界”，也禁止隐式传播脏标记。
   （若未来启用 B3，则必须显式定义跨边界传播规则并更新本节与 §8。）
3. **调度顺序**：使用 `VoxelWorld.CollectDirtyChunks`（VoxelCore 已按 Chunk 坐标排序）
   ⇒ 重建顺序确定、与字典遍历顺序无关。
4. **清理时机**：只有在 Mesh 构建成功后才 `ClearDirty()`；
   `ClearDirtyFlags()` 只能用于“全部已成功重建”之后的批量清理。
5. **预算（可选）**：允许按帧预算分摊多个 Chunk 的重建，但：
   * 每个 Chunk 仍然原子完成；
   * 不得因为分帧而改变最终几何（与一次性重建逐字节一致）；
   * 未完成的 Chunk 保持 `Dirty`。
6. **并发**：同一 Chunk 不允许并发重建；同帧内重复调度同一 Chunk 只构建一次。

---

## 8. 验收不变量（Acceptance Invariants）

> 每条都必须可自动化验证（纯数据 + Mesh 缓冲比较，不依赖人眼/渲染）。

| # | 不变量 | 验证方法 |
|---|---|---|
| **I1** | **真值隔离**：Meshing 不改变世界数据 | 构建前后对 `VoxelChunk` 全部 4096 个体素做快照比较（含 `Coordinate`/`IsDirty` 不变） |
| **I2** | **覆盖完整性**：每个实体 Voxel × 6 方向，邻居为 Air（含越界，B1）⇒ 恰好 1 个 quad；邻居实体 ⇒ 0 个 quad | 用 `GetVoxel` 独立重算期望集合，与 Mesh 中实际面集合逐一比对（计数 + 位置） |
| **I3** | **无重复面**：同一 `(voxel, direction)` 不出现 2 次 | 面键集合（voxel,direction）去重后计数不变 |
| **I4** | **Air 无几何**：Air Voxel 不产生任何面 | 全 Air Chunk ⇒ 顶点/三角形/子网格全空（0/0） |
| **I5** | **法线与绕序**：每个 quad 的 `cross(v1−v0, v2−v0)` 与面的方向 `d` 同向 | 逐面计算叉积符号（`dot > 0`） |
| **I6** | **几何范围**：所有顶点在 `[0,16]³` 闭区间内，且每个顶点坐标分量为整数 | 逐顶点范围与整数性检查 |
| **I7** | **块内一致**：每个 quad 的 4 个顶点共面且面积 = 1（无缩放/无变形） | 平面方程 + 面积检查 |
| **I8** | **确定性**：同一 Chunk 数据重复构建、跨实例构建、不同构建顺序 ⇒ 缓冲**完全一致**（顶点/索引/子网格顺序） | 逐元素比较两次构建的输出（含 submesh 分组） |
| **I9** | **跨 Chunk 连续性**：相邻 Chunk 在共享平面上生成的边界面，顶点/平面重合（B2） | 取两侧边界 Voxel 的面，比较世界平面（`chunkOrigin + local`）与顶点集合 |
| **I10** | **脏生命周期**：实际变化 ⇒ `Dirty`；同值写入 ⇒ 不变脏；构建成功 ⇒ `ClearDirty`；构建失败 ⇒ 仍 `Dirty` | 直接断言 `IsDirty` 与写入返回值 |
| **I11** | **重建范围**：Chunk A 的编辑不改变 Chunk B 的 Mesh（B1 前提下） | 编辑 A 后比较 B 的 Mesh 快照（未重建即未变化） |
| **I12** | **释放可重建**：释放 Mesh 后重建，结果与释放前完全一致 | 两次缓冲逐元素比较 |
| **I13** | **材质映射**：`Grass/Dirt/Stone` 分别落在 submesh `0/1/2`；无未知 submesh | 逐三角形所属 submesh 与对应 Voxel 类型比对 |
| **I14** | **可复现性（跨存档）**：`Seed + 改动数据` 重建后的 Chunk ⇒ 与首次构建的 Mesh 一致 | 生成→编辑→重建（同 seed）后比较缓冲 |

任何一条不变量被破坏，都视为 **Meshing 缺陷**（而非数据缺陷）；
修复必须落回本规范的规则，禁止“只在 Mesh 上打补丁”。

---

## 9. 明确不做（Phase 1 out of scope）

* **Marching Cubes / 等值面 / 平滑地形**（本阶段只做 Cube Surface Meshing）；
* 洞穴 / 矿物 / 生物群系 / 结构 —— 它们属于生成阶段（WorldGeneration_Core §7），
  只会改变 Voxel 数据，不改变本规范；
* Greedy Meshing、跨 Chunk 合并、顶点焊接优化（未来可作为**保持 I2/I8** 的优化引入）；
* AO、光照、混合材质、每面不同贴图、LOD；
* Collider 的构建细节（仅确认：Collider 与 Mesh 同为派生表示，绝不成为真值）；
* 渲染管线、Shader、材质资产。

---

## 10. 变更规则

1. 本规范的所有条款都以 `VoxelWorld_Core.md` 为上游；数据契约冲突时以上游为准。
2. 任何实现变更若触及第 1 章或第 8 章不变量，必须：
   ① 更新本规范版本号；② 同步更新验收不变量清单与自动化测试；③ 说明与既有存档的兼容性。
3. **禁止**通过修改 Mesh 生成逻辑来“掩盖”数据层问题（缺面/穿透类问题必须先回到
   Voxel 数据与 Meshing 规则本身定位）。
4. 第一阶段（V1）实现范围：**Seed + ChunkCoordinate → VoxelChunk → Cube Surface Mesh**，
   不包含任何其它系统。
