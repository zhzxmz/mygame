# Voxel-aware Mesh Specification

**Version:** V2 (Revised)
**Status:** Target Architecture
**Scope:** Voxel-aware Terrain Mesh Topology

---

## 1. Purpose

本规范定义连续自然 Terrain Mesh 与离散 Voxel 空间之间的拓扑关系。

核心目标不是将 Terrain 变成 Minecraft 式 Block Mesh。Terrain 仍然保持连续、自然的基础几何。

Voxel 提供的是：

> 离散、稳定、可修改的空间划分。

最终 Mesh 是 Voxel State 与 Base Terrain Geometry 的派生结果。

---

## 2. Core Architecture

### 2.1 Final Target Flow

```text
Base Terrain
    ↓
Voxel-aware Pre-cut
    ↓
Voxel-local Geometry
    +
Boundary Geometry
    ↓
Voxel State Change
    ↓
Local Geometry Re-evaluation
    ↓
SpatialFace Evaluation
    ↓
Hidden Face Region
    ↓
Final Mesh
```

### 2.2 Mining Data Boundary

```text
Mining
    ↓
Voxel State Change
    ↓
Chunk / Voxel-aware Mesh re-evaluation
    ↓
Mesh Update
```

Mining 不直接修改 Density、Base Mesh、Bridge、Hidden Face 或 Collider。

### 2.3 Transition / Legacy

当前 Runtime 的 Model B 流程：

```text
Mining → VoxelField → Effective Density → Marching Cubes
```

属于 Transition / Legacy，只用于迁移期间保持现有场景可运行。它不是 Voxel-aware Mesh 的最终 Mining 主流程。

---

## 3. Voxel Definition

### 3.1 Coordinate

Voxel 使用整数坐标：

```text
c = Vector3Int(x, y, z)
```

Voxel Coordinate 表示该 Voxel 的 **最小角（min-corner）**，不是 Voxel 中心。

### 3.2 Space Interval

```text
VoxelSize = 1m

Voxel(c) =
    [c.x, c.x + 1)
  × [c.y, c.y + 1)
  × [c.z, c.z + 1)
```

示例：

```text
Voxel (0,0,0) = [0,1) × [0,1) × [0,1)
Voxel (1,0,0) = [1,2) × [0,1) × [0,1)
Voxel (-1,0,0) = [-1,0) × [0,1) × [0,1)
```

区间为 half-open：min 闭，max 开。

### 3.3 Position to Voxel

```text
VoxelCoordinate = floor(TerrainLocalPosition / VoxelSize)
```

示例：

```text
position  0.0  → Voxel 0
position  0.9  → Voxel 0
position  1.0  → Voxel 1
position -0.1  → Voxel -1
position -1.0  → Voxel -1
```

禁止使用 Round / Ceil / Nearest / epsilon 修正来决定 Voxel 坐标。

### 3.4 Coordinate Space

Voxel 坐标与 Voxel 空间范围都定义在 **Terrain Local Space**。World Space 通过 Terrain Root Transform 到达。

---

## 4. Chunk Definition

一个 Chunk 包含：

```text
16 × 16 × 16 Voxels
```

使用 min-corner 契约：

```text
Chunk (0,0,0)：
    Voxel X = 0..15
    Terrain Local X = [0, 16)

Chunk (1,0,0)：
    Voxel X = 16..31
    Terrain Local X = [16, 32)

Chunk (-1,0,0)：
    Voxel X = -16..-1
    Terrain Local X = [-16, 0)
```

Chunk 是空间划分和 Mesh 更新的组织单位。Chunk 不拥有 VoxelField；VoxelField 属于整个 DensityTerrain。

---

## 5. SpatialFace

### 5.1 Definition

SpatialFace 是两个相邻 Voxel 之间实际共享的 **一个有限单位空间面**。

它不是两个 Voxel Face 的组合，也不是两个独立面。

```text
A.RightFace
    ↕
SpatialFace
    ↕
B.LeftFace
```

三者表示同一个空间面。

### 5.2 Identity

SpatialFace identity 必须同时包含：

```text
Axis
PlaneCoordinate
FaceCellCoordinateU
FaceCellCoordinateV
```

正式定义：

```text
FaceIdentity = (Axis, PlaneCoordinate, U, V)
```

其中：

- `Axis ∈ {X, Y, Z}`
- `PlaneCoordinate` 是该面的整数平面坐标
- `U, V` 是该单位面在另外两个轴上的 min-corner 整数坐标

各轴公式：

```text
Axis = X：
    Face 位于 x = PlaneCoordinate
    U = min(VoxelA.y, VoxelB.y)
    V = min(VoxelA.z, VoxelB.z)
    空间范围：
        x = PlaneCoordinate
        y ∈ [U, U+1)
        z ∈ [V, V+1)

Axis = Y：
    Face 位于 y = PlaneCoordinate
    U = min(VoxelA.x, VoxelB.x)
    V = min(VoxelA.z, VoxelB.z)
    空间范围：
        y = PlaneCoordinate
        x ∈ [U, U+1)
        z ∈ [V, V+1)

Axis = Z：
    Face 位于 z = PlaneCoordinate
    U = min(VoxelA.x, VoxelB.x)
    V = min(VoxelA.y, VoxelB.y)
    空间范围：
        z = PlaneCoordinate
        x ∈ [U, U+1)
        y ∈ [V, V+1)
```

### 5.3 Canonical Identity

```text
Voxel A = (0,0,0)
Voxel B = (1,0,0)

A.RightFace 的 canonical identity：
    Axis = X
    PlaneCoordinate = 1
    U = 0
    V = 0

B.LeftFace 的 canonical identity：
    同一个 (X, 1, 0, 0)
```

因此：

```text
A.RightFace == B.LeftFace
```

不同平面坐标、不同 U/V 的单位面是不同 SpatialFace：

```text
(X, 1, 0, 0) ≠ (X, 1, 1, 0)
(X, 1, 0, 0) ≠ (X, 2, 0, 0)
```

### 5.4 Direction

SpatialFace 具有 canonical direction：

```text
negative side = 沿 Face 法线负方向一侧的 Voxel
positive side = 沿 Face 法线正方向一侧的 Voxel
```

例如：

```text
F = (X, 1, 0, 0)
negative side = Voxel (0,0,0)
positive side = Voxel (1,0,0)
```

该方向用于：

- Hidden Face winding；
- canonical owner；
- Solid / Air 关系描述。

### 5.5 No Registry Requirement

SpatialFace 是稳定的 **空间 identity 规则**。当前阶段不要求实现 Global Registry、SpatialFace Service、Factory 或 ECS 系统。

---

## 6. World Boundary

正式定义：

```text
World Outside = Air
```

- 世界边界不需要实际的无限 Voxel 对象。
- `Outside Air` 是边界语义。
- 一个位于世界边界的 Solid Voxel，其朝向世界外部的单位面按 `Solid ↔ Outside Air` 的 SpatialFace 处理。
- 该边界面可以参与 Hidden Face evaluation。

世界边界面的 identity 使用同样的 FaceIdentity 规则，其中外侧没有 Voxel，但 canonical identity 仍由边界平面坐标和单位面 U/V 唯一确定。

对于 Voxel `c` 的世界边界单位面：

```text
-X 边界：
    Axis = X
    PlaneCoordinate = c.x
    U = c.y
    V = c.z

+X 边界：
    Axis = X
    PlaneCoordinate = c.x + 1
    U = c.y
    V = c.z
```

Y / Z 轴边界同理，使用该 Voxel 的另外两个 min-corner 坐标作为 U/V。

世界边界面的 canonical owner = 该边界 Voxel 所属 Chunk。

---

## 7. Cross-Chunk SpatialFace

### 7.1 Single Logical Face

SpatialFace 是世界空间概念，不属于某一个 Chunk 的独立副本。

如果两个相邻 Voxel 分别属于 Chunk A 与 Chunk B，它们之间仍然只有一个 SpatialFace：

```text
Chunk A / Chunk B 共享同一个 SpatialFace
```

而不是：

```text
Chunk A Face + Chunk B Face
```

### 7.2 Canonical Evaluation Owner

SpatialFace 需要一个确定性的 **Evaluation Owner**，只负责：

```text
该 SpatialFace 的一次逻辑 Evaluation
```

规则：

```text
内部 SpatialFace：
    EvaluationOwnerChunk =
        negative side（Face 法线负方向一侧）Voxel 所属 Chunk

世界边界 SpatialFace：
    EvaluationOwnerChunk =
        该边界 Voxel 所属 Chunk
```

示例：

```text
F_AB = plane x = 16，A = Voxel(15,0,0)，B = Voxel(16,0,0)
negative side = A
EvaluationOwner = A 所属 Chunk
```

如果 A/B 属于同一 Chunk，Evaluation Owner 就是该 Chunk。

Evaluation Owner 规则不能改变：

```text
Voxel State
Base Mesh
邻居 Chunk 的自然 Mesh
```

### 7.3 Evaluation Owner ≠ Hidden Face Mesh Owner

必须明确区分：

```text
Canonical Evaluation Owner
    = 谁负责执行一次 SpatialFace Evaluation

HiddenFaceMeshOwner
    = 最终 Hidden Face Geometry 写入哪个 Chunk 的 Mesh
```

两者可以是不同 Chunk。

Evaluation Owner 只负责逻辑 Evaluation，不拥有 Hidden Face 的最终 Mesh。

### 7.4 Hidden Face Mesh Owner

规则：

```text
Negative Solid / Positive Air：
    Evaluation Owner = Negative-side Chunk
    Mesh Owner       = Negative-side Chunk

Negative Air / Positive Solid：
    Evaluation Owner = Negative-side Chunk
    Mesh Owner       = Positive-side Chunk

Solid / Solid：
    No Hidden Face

Air / Air：
    No Hidden Face
```

Hidden Face Mesh Owner：

```text
HiddenFaceMeshOwnerChunk =
    Solid-side Voxel 所属 Chunk
```

原因：

```text
Hidden Face 是 Solid Voxel 与 Air Voxel 之间的内部边界几何。
最终 Mesh 必须保持 Chunk-local。
因此 Hidden Face Vertex Position 必须位于 Solid-side Chunk Local Space。
```

### 7.5 Cross-Chunk Boundary Graph

对于跨越 Chunk 的 SpatialFace：

```text
Chunk A = Evaluation Owner
Chunk B = Mesh Owner（当 Solid-side 在 B 时）
```

Evaluation Owner 必须能够读取当前 FaceIdentity 所需的 Boundary Geometry：

```text
Natural Base Edge
Bridge
Coplanar Base Geometry Boundary
Face Boundary
```

来自：

```text
Chunk A Geometry 中属于 FaceIdentity 的几何
+
Chunk B Geometry 中属于 FaceIdentity 的几何
    ↓
Complete Planar Boundary Graph
    ↓
Regions
```

不能只读取 Evaluation Owner Chunk 自己的几何，否则 Boundary Graph 可能不完整。

读取必须按 FaceIdentity 过滤，而不是把整个 ChunkGeometry 放入当前 Face 的 Planar Graph。

Cross-Chunk 两侧提供同一逻辑 Segment 时，必须按照 13.9 的 Segment Identity 与 Dedup 规则去重，Dedup 必须发生在 Planar Graph 构建之前。

### 7.6 Exactly One Evaluation and One Hidden Face Result

同一个 FaceIdentity：

```text
Exactly One Evaluation
    ↓
Zero or One Hidden Face Result
    ↓
Zero or One HiddenFaceMeshOwner
```

禁止：

```text
Chunk A Evaluation + Chunk B Evaluation
HiddenFace_A + HiddenFace_B
Duplicate Hidden Face
```

### 7.7 Hidden Face Result 的逻辑归属

```text
SpatialFaceRecord
    = Face Evaluation Result
    = 逻辑上由 Evaluation Owner 持有

HiddenFaceFragment
    = Final Mesh contribution
    = 发射到 Solid-side Chunk（Mesh Owner）
```

Mesh Owner 不重新 Evaluation 该 SpatialFace，也不复制 SpatialFaceRecord。

### 7.8 Cross-Chunk Coordinate Rule

Hidden Face 写入 Solid-side Chunk 时：

```text
Face Local
    ↓
Terrain Local / World
    ↓
Solid-side Chunk Local
```

禁止把 Evaluation Owner Chunk Local 直接当作 Mesh Owner Chunk Local，除非两者相同。

### 7.9 Cross-Chunk Material

Hidden Face Material 继续使用：

```text
VoxelMainMaterial(globalVoxelCoordinate)
```

Material 来源为 Solid-side Voxel 所属 Chunk 的 Base MaterialField。

必须使用 global Voxel Coordinate 查询，而不是 Chunk-local Voxel Coordinate。

同一个 global Voxel 的结果必须确定且唯一。

### 7.10 No Global Face Registry

本节不要求：

```text
Global Face Registry
Global SpatialFace Service
Persistent Face Database
ECS
Factory
```

Cross-Chunk Chunk Lookup 可以使用 DensityTerrain 已有的 `chunkLookup`。

### 7.11 Current Runtime Implementation Limitation

Phase 2A 当前：

```text
VoxelAwareMeshSpatialFace.BuildLocalRecords()
```

把每个 Chunk 的本地 FaceRecord Owner 设为自己，尚未实现 canonical Evaluation Owner 与 Cross-Chunk Boundary Graph。

这是 Phase 2A implementation limitation，不是 Spec。

Phase 2B 实现时必须改为：

```text
EvaluationOwner = canonical negative-side Chunk
Cross-Chunk Boundary Geometry = 从两侧 Chunk 按 FaceIdentity 读取
HiddenFaceFragment 发射到 Solid-side Mesh Owner
```

---

## 8. Dirty Propagation

一次编辑：

```text
Voxel A: Solid → Air
```

至少重新评估：

```text
A-local Geometry
A 的 6 个 SpatialFace
```

对每个 Face：

```text
Evaluation Owner Chunk
和/或
HiddenFaceMeshOwnerChunk
```

都可能需要进入 Dirty Set。

正式定义：

```text
DirtySet =
    Changed Voxel Chunk
  + Affected Face Evaluation Owners
  + Affected Hidden Face Mesh Owners
```

DirtySet 必须先去重，再执行 Assembly。

Dirty 的含义：

```text
重新组合当前 Chunk 的最终 Mesh
```

而不是：

```text
重新切邻居 Chunk 的 Base Mesh
```

重新组合使用的输入：

```text
Base Geometry
Voxel-local Geometry
Bridge
Current Hidden Face Result
```

Cross-Chunk 情况：

```text
Evaluation Owner = Chunk A
Mesh Owner       = Chunk B
    →
Chunk A 执行一次 Face Evaluation
Chunk B 接收 Hidden Face contribution
Chunk A 不生成 Duplicate Hidden Face
Chunk B 不重新 Evaluation
```

### 8.1 Cross-Chunk Mutation Example

```text
Voxel A = (15,0,0)  属于 Chunk (0,0,0)
Voxel B = (16,0,0)  属于 Chunk (1,0,0)

Face(A,B)：
    Evaluation Owner = Chunk (0,0,0)
```

如果：

```text
A = Air
B = Solid
```

则：

```text
Evaluation Owner    = Chunk (0,0,0)
HiddenFaceMeshOwner = Chunk (1,0,0)

Chunk (0,0,0)：
    执行一次 Face Evaluation
    从 Chunk (0,0,0) + Chunk (1,0,0) 按 FaceIdentity 读取 Boundary Geometry
    产生 Hidden Face Result
    不把 Hidden Face 写入自己的 Mesh

Chunk (1,0,0)：
    接收 Hidden Face contribution
    将 Face Local 转换到 Chunk (1,0,0) Local Space
    加入自己的 Final Assembly
    不重新 Evaluation 该 Face
```

DirtySet：

```text
Chunk (0,0,0)  // Evaluation Owner
Chunk (1,0,0)  // HiddenFaceMeshOwner / Changed Voxel Chunk
```

---

## 9. Base Mesh and Voxel Boundary Cutting

### 9.1 Base Mesh

Base Mesh 是由自然 Terrain Generator 生成的原始连续表面。

当前实现可继续使用：

```text
DensityField → Marching Cubes → Base Mesh
```

Marching Cubes 只负责生成自然连续表面。它不负责 Voxel State、Mining、Bridge、Hidden Face。

### 9.2 Pre-cut

Base Mesh 在初始生成阶段就按 Voxel Boundary 预先切割，得到：

```text
Voxel-local Geometry
    +
Boundary Geometry（Bridge）
```

Pre-cut 不改变自然表面几何形状，只增加拓扑边界。

### 9.3 Pre-cut is One-time

运行时 Excavation 不重新切割邻居 Voxel 的 Base Mesh。已切好的 Voxel-local Geometry 直接参与删除 / 恢复。

---

## 10. Triangle / Voxel Boundary Intersection

Triangle 与 Voxel Boundary 的相交分类：

### 10.1 No Intersection

```text
Triangle ∩ Boundary = ∅
```

不生成 Bridge。

### 10.2 Point Contact

```text
Triangle ∩ Boundary = Point
```

- 不生成 BridgePoint；
- 不生成 BridgeEdge；
- 孤立 Point 不构成可渲染 Surface Geometry；
- 孤立 Point 不能单独作为 Hidden Face 边界。

### 10.3 Zero-length Segment

```text
Segment.length <= GeometryTolerance
```

视为退化交点，不生成 BridgeEdge。

### 10.4 Valid Segment

```text
Segment.length > GeometryTolerance
```

并且来自有效的 Mesh / Voxel Boundary 相交时，生成 BridgeEdge。

### 10.5 Coplanar

Triangle 完全位于 Voxel Boundary Plane 上：

```text
不生成 Bridge
```

“位于 SpatialFace 上”不是 Bridge 的充分条件。

---

## 11. Coplanar Geometry

### 11.1 Triangle Completely on SpatialFace

如果 Base Triangle 完全位于 SpatialFace：

```text
它不是 Bridge。
它不应简单归属为某一侧 Voxel 的内部 Geometry。
它应作为 SpatialFace Base Geometry 处理。
```

保留 / 删除由该 SpatialFace 的最终 Surface Evaluation 决定：

```text
Solid / Solid：
    保持自然 Geometry，不生成 Hidden Face。

Air / Solid 或 Solid / Air：
    保留 Solid 侧的有效自然表面；
    根据该 Face 上已有 Base Geometry / Bridge 的 Surface Coverage
    决定还需要补多少 Hidden Face region。

Air / Air：
    不保留纯内部 Coplanar Surface。
```

### 11.2 Edge Completely on SpatialFace

如果 Base Edge 原本就完全位于 SpatialFace：

```text
它是 Base Mesh Edge，不是 BridgeEdge。
```

### 11.3 Point Only on SpatialFace

如果 Geometry 仅在一个 Point 接触 SpatialFace：

```text
不产生 Bridge。
该 Point 不能单独作为 Hidden Face 边界。
```

---

## 12. Bridge

### 12.1 Definition

Bridge 是：

> Base Mesh Geometry 被 Voxel Boundary Cutting 后新产生的、位于 SpatialFace 上的有效边界几何。

Bridge 不是“所有位于 SpatialFace 上的 Mesh Vertex / Edge”。

### 12.2 Bridge vs Final Mesh Topology

必须区分两个层次：

```text
Bridge provenance（逻辑切割记录）
    ≠
Final Unity Mesh Vertex / Edge / Triangle
```

- 不同 Triangle 可以拥有独立 Bridge provenance。
- 但最终写入 Unity Mesh 时，同一空间切割边界上的 Vertex / Edge 必须 shared / welded。

因此：

```text
A-local Polygon
        │
        P0 ===== P1
        │
B-local Polygon
```

最终是同一组：

```text
P0
P1
Edge(P0, P1)
```

而不是重复的：

```text
P0A / P1A
P0B / P1B
```

### 12.3 Bridge Support

Bridge 必须由当前有效 Mesh Geometry 支撑。

```text
Triangle → BridgeEdge
```

如果支持 Bridge 的 Triangle 被删除，BridgeEdge 删除。

如果 BridgePoint 不再被任何 BridgeEdge 引用，BridgePoint 删除。

### 12.4 Bridge Lifetime Invariant

```text
BridgeEdge exists
    => its supporting Mesh Geometry exists

BridgePoint exists
    => at least one valid BridgeEdge references it
```

不允许孤立 BridgeEdge / BridgePoint。

### 12.5 Bridge Semantics Boundary

Bridge 只来自 Boundary Cutting。

以下情况不是 Bridge：

```text
Base Edge 原本就在 SpatialFace 上
Base Vertex 原本就在 SpatialFace 上
Point Touch
完全 Coplanar Triangle
Zero-length Segment
```

---

## 13. Hidden Face

### 13.1 Definition

Hidden Face 是：

> 由于 Voxel Solid / Air 状态变化，在 SpatialFace 上新生成的补充表面。

Hidden Face 与 Bridge 不同：

```text
Bridge：
    来源 = Existing Mesh + Voxel Boundary Cutting

Hidden Face：
    来源 = Voxel Solid / Air State
```

### 13.2 Generation Condition

```text
SpatialFace 两侧：
    Air ↔ Solid
    →
    具备生成 Hidden Face 的条件

Solid ↔ Solid：
    不生成 Hidden Face

Air ↔ Air：
    不生成 Hidden Face
```

世界边界按 `Outside = Air` 处理。

### 13.3 One Evaluation Result

同一个 SpatialFace 只允许一个 Hidden Face evaluation result。

不允许 A/B 两侧分别生成两个重复 Hidden Face。

### 13.4 Bridge / Natural Boundary as Region Boundary

```text
SpatialFace
    │
    ├── Bridge / Natural Boundary
    │
    └── Remaining Region
             │
             ▼
        Hidden Face
```

Voxel State 决定该 SpatialFace 是否需要补面。

Bridge 与已有 Natural Boundary 决定补面区域如何被划分。

Hidden Face 只填补需要填补的 SpatialFace region。

### 13.5 Hidden Face is not Bridge

Hidden Face 不是 Bridge，Bridge 也不决定 Voxel Solid/Air 状态。

两者职责不同，不得互相替代。

### 13.6 Hidden Face Material

本节定义 Hidden Face 的正式 Material 语义。

#### 13.6.1 VoxelField does not own Material

```text
VoxelField 只保存 Air / Solid。
VoxelField 不保存 MaterialType。
```

Material 的来源是：

```text
DensityField
    +
MaterialField
```

`MaterialField` 是与 `DensityField` 空间对齐的离散 `MaterialType` 采样场。

#### 13.6.2 VoxelMainMaterial

对 Voxel：

```text
Voxel(c) = [c, c+1)
```

定义：

```text
VoxelMainMaterial(c) =
    Base MaterialField 在 Voxel 几何中心
    c + (0.5, 0.5, 0.5)
    处的 nearest sample 的 MaterialType
```

规则：

- 只做 nearest sample 查询。
- `MaterialType` 是离散枚举，禁止 trilinear interpolation。
- 禁止创建新的 MaterialType。
- 同一个 global Voxel 必须得到同一个 VoxelMainMaterial，不受 Chunk / Region / Triangle 影响。

#### 13.6.3 Hidden Face Region Material

对于一个 SpatialFace Region：

```text
Air / Solid
```

或：

```text
Solid / Air
```

如果该 Region 需要生成 Hidden Face：

```text
HiddenFaceRegion
    ↓
Solid-side Voxel
    ↓
VoxelMainMaterial(solidVoxel)
    ↓
HiddenFaceFragment.Submesh / MaterialType
```

因此：

- 不使用 nearest Natural Triangle Material。
- 一个 SpatialFace 可以拥有多个 Region。
- 同一个 SpatialFace 的不同 Region 可以拥有不同 MaterialType。
- 禁止把整个 SpatialFace 强制为同一 Material。

#### 13.6.4 Cross-Chunk Consistency

Voxel 是 Terrain-global Coordinate。

```text
VoxelMainMaterial(globalVoxel)
```

必须跨越 Chunk 保持一致。跨 Chunk SpatialFace 的 Hidden Face Material 也必须相同。

MaterialField 的正式归属：

```text
Base MaterialField 由生成该 Voxel 所属 Chunk 提供。
```

Voxel 的所属 Chunk 由 min-corner Voxel Coordinate 通过既有 Chunk 映射确定，因此同一个 global Voxel 只有一个 Base MaterialField 查询来源。

规则：

```text
global Voxel Coordinate
    ↓
Voxel 所属 Chunk
    ↓
该 Chunk 的 Base MaterialField
    ↓
nearest sample
    ↓
VoxelMainMaterial
```

禁止：

```text
使用不同 Chunk 的 MaterialField 对同一个 global Voxel 得到不同结果
```

Solid-side Voxel 位于 Cross-Chunk Face 另一侧时，Hidden Face Material 仍由该 Solid-side Voxel 的所属 Chunk 决定，并与 Evaluation Owner 无关。

#### 13.6.5 World Outside

```text
World Outside = Air
```

世界边界 `Solid / Outside-Air` 的 Hidden Face 使用 Solid-side Voxel 的 `VoxelMainMaterial`。

#### 13.6.6 Derived Result

Hidden Face Material 是 Derived Result：

```text
不修改 Base MaterialField
不写回 VoxelField
不改变 Base Fragment Material
```

Base Geometry / Face Base Geometry / Bridge provenance 保持不变。

### 13.7 Region Natural Coverage Classification

本节定义 SpatialFace Region 是否需要 Hidden Face 的正式判定。

#### 13.7.1 No Geometry/Count Heuristics

Natural Coverage 不能由以下方式推断：

```text
Triangle 数量
Fragment 数量
Bridge 数量
Material
```

必须使用 Base Density / Base Solid State。

#### 13.7.2 Region Interior Sample

每个 Region 必须获得一个稳定、确定性的 interior sample：

```text
1. 对 Region Polygon 做确定性 Triangulation。
2. 选择面积大于 GeometryTolerance 的三角形。
3. 使用该三角形的 centroid 作为候选 sample。
4. 候选点必须不在任何 Boundary / Bridge / Natural Edge /
   Coplanar Boundary / Face Boundary 上。
5. 若候选点不合法，按确定顺序尝试下一个三角形。
6. 若所有候选都不合法：
      Topology Classification Failure
```

禁止：

```text
随机采样
midpoint 未验证就使用
```

#### 13.7.3 Base Density Side Sample

对 Region interior sample `P`：

```text
FaceNormal = SpatialFace canonical normal
ClassificationOffset = GeometryTolerance

P- = P - ClassificationOffset * FaceNormal
P+ = P + ClassificationOffset * FaceNormal
```

`P-` 与 `P+` 使用 Terrain Local Space。

#### 13.7.4 Base Density Sampling

BaseDensity 的正式来源：

```text
Voxel 所属 Chunk 的 Base DensityField
```

查询规则：

```text
P- / P+
    ↓
对应 Voxel 所属 Chunk 的 Base DensityField
    ↓
Base Terrain canonical density evaluation
```

Base Density 是连续表面的采样表示。该分类必须使用与 Base Terrain 表面一致的密度插值：

```text
BaseDensity(P) =
    对应 Chunk Base DensityField 在 Terrain Local P 处的
    deterministic trilinear interpolation
```

采样点超出 Base DensityField 范围时，按该 Chunk 的 Base Density 边界语义处理，不允许回退到 VoxelField。

禁止使用：

```text
VoxelField
Effective Density
MaterialField
```

来确定 Natural Coverage。

#### 13.7.5 Natural Solid Coverage

Current State：

```text
Negative = Solid
Positive = Air
    →
Solid-side = Negative
```

Region classification：

```text
BaseDensity(P-) > IsoLevel
    →
Natural Solid Coverage = true
    →
No Hidden Face

BaseDensity(P-) <= IsoLevel
    →
Natural Solid Coverage = false
    →
Uncovered Region
```

反向情况完全对称：

```text
Negative = Air
Positive = Solid
    →
Solid-side = Positive

BaseDensity(P+) > IsoLevel
    →
Natural Solid Coverage = true
    →
No Hidden Face

BaseDensity(P+) <= IsoLevel
    →
Natural Solid Coverage = false
    →
Uncovered Region
```

#### 13.7.6 Region 覆盖语义细分（当前实现，B3-C）

Region 由 Bridge / Coplanar / Natural Boundary 划分，但基础表面（Solid/Air）覆盖边界
不一定与这些 Segment 重合：同一个 Region 内部可能同时存在“需要封闭”和“不需要封闭”
的位置。**单点 Region Interior Sample 只作为该 Region 的代表性结论（用于材质与诊断），
不足以决定整片 Region 的几何输出。**

当前实现（`VoxelAwareMeshSpatialFace.Evaluate`）：

```text
Region 内部采样一致  → 保持既有行为：整片 Polygon 作为 Hidden Face（几何不变）
Region 内部采样不一致 → 按覆盖判定符号边界细分为子区域，只输出需要封闭的子区域
    · 细分单元 ≤ 4cm；跨边界单元用二分求交定位边界，再按“开口”半平面裁剪
    · 完整落在 Region 内且全开口的单元贪心合并为矩形（限制三角形数量）
    · 丢弃 ≤ 1e-6 m² 的数值退化薄片
```

约束与不变量：

- SpatialFace identity、Canonical Evaluation Owner、Hidden Face Mesh Owner 规则不变。
- Region identity 与其代表性 Decision 保留；细分只改变几何输出的粒度。
- 覆盖边界是近似：单个边界单元整格误判 ≤ h²（h ≤ 0.04m）；
  仅当边界每单元穿越次数有界（≤2）时才有聚合上界 `2Lh`（L = 覆盖边界长度），
  对任意高频抖动边界不成立；无条件成立的只有二值覆盖面积差上界
  `E ≤ min(A_open, A_face − A_open)`（0 ≤ A_open ≤ A_face）。
  工程容差的取值、实测数据与适用范围见 `Specifications/CoordinateMigration.md` §6.3。

Dirty 传播要求（B3-C 修复的既有缺口）：

```text
任何因为 Face 重新评估而被移除 HiddenFaceContribution 的 Chunk
（EvaluationOwner / NegativeChunk / PositiveChunk / MeshOwner）
必须被标记为 Dirty 并重新 Assemble。
```

否则当 Face 从 `Solid/Air` 边界回到 `Solid/Solid`（例如恢复 Solid）时，旧的 Hidden Face
会残留在 MeshOwner（可能是相邻 Chunk）的 Mesh 中，造成“应开放区域未开放”。
### 13.8 FaceFragment Base Geometry and Final Visibility

#### 13.8.1 Immutable Base Geometry

`FaceFragment` 正式定义为：

```text
Coplanar Base Geometry
    =
Immutable Base / Pre-cut Geometry
```

因此：

```text
Mutation 不能删除 FaceFragment
Mutation 不能修改 FaceFragment Polygon
Mutation 不能修改 FaceFragment SourceTriangle / Submesh
```

`Solid ↔ Air` 只能改变 VoxelField State，不能改变 Base Face Geometry。

#### 13.8.2 Final Visibility is Derived

FaceFragment 的最终 Mesh 可见性由以下决定：

```text
Current Voxel State
+ Natural Coverage
+ Coplanar Base Coverage
```

必须区分：

```text
FaceFragment Base Exists
    =
immutable true
```

与：

```text
FaceFragment Final Visible
    =
derived per Final Assembly
```

禁止：

```text
Mutation
    ↓
删除 / 修改 Base FaceFragment
```

允许：

```text
Mutation
    ↓
Final Assembly Filtering
    ↓
决定是否输出 FaceFragment
```

#### 13.8.3 FaceFragment Visibility Rule

对 Face Region 与覆盖它的 Coplanar Base Geometry：

```text
Current State Solid / Solid
    →
No current Solid/Air boundary
    →
FaceFragment Final Visible = false

Current State Air / Air
    →
No current Solid/Air boundary
    →
FaceFragment Final Visible = false

Current State Solid / Air 或 Air / Solid
    →
存在 current Solid/Air boundary
    →
若 Region 被 Coplanar Base Geometry 覆盖：
        FaceFragment Final Visible = true
        Hidden Face = false
```

### 13.9 Cross-Chunk Boundary Segment Identity and Dedup

#### 13.9.1 Segment Type

至少区分：

```text
NaturalBaseEdge
Bridge
CoplanarBoundary
FaceBoundary
```

`Bridge` 与 `NaturalBaseEdge` 即使端点相同，也保留不同 provenance。

#### 13.9.2 Endpoint Identity

Segment 端点必须使用统一的：

```text
Face Local canonical coordinates
```

并按 `GeometryTolerance` 量化。

不能使用 Chunk-local VertexId 作为跨 Chunk identity。

Endpoint canonical ordering：

```text
EndpointA <= EndpointB
```

因此 A→B 与 B→A 得到同一个 identity。

#### 13.9.3 Segment Identity

```text
SegmentIdentity =
(
    FaceIdentity,
    EndpointA,
    EndpointB,
    SegmentType
)
```

#### 13.9.4 Geometric Dedup Key

为了构建唯一 Planar Graph：

```text
GeometricDedupKey =
(
    FaceIdentity,
    EndpointA,
    EndpointB
)
```

`GeometricDedupKey` 不包含 SegmentType。

规则：

```text
同一 GeometricDedupKey：
    Planar Graph 中只允许一条几何边

该几何边可以保留多个 provenance：
    NaturalBaseEdge / Bridge / CoplanarBoundary
```

#### 13.9.5 Canonical Source

同一 GeometricDedupKey：

```text
优先使用 Evaluation Owner Chunk 提供的端点
若 Evaluation Owner 没有，则使用 Positive-side Chunk
```

provenance 合并但不能丢失 SegmentType。

如果两个来源对同一 SegmentType 给出：

```text
端点不一致且超出 GeometryTolerance
```

则：

```text
Validation Failure
```

不能静默合并。

#### 13.9.6 Collinear Overlap

不同 SegmentIdentity 如果发生部分 collinear overlap：

```text
Validation Failure
```

Pre-cut 必须先保证 Segment 已正确处理，不允许 Planar Graph 吞掉 overlap。

#### 13.9.7 Face Boundary

Face Boundary 是：

```text
SpatialFace 本身的有限 1×1 边界
```

它由 FaceIdentity 唯一生成，不来自 Chunk Geometry，因此不需要 Cross-Chunk 合并。

### 13.10 Final Hidden Face Decision Table

```text
Current Negative State | Current Positive State
-----------------------------------------------
Solid                  | Solid
    → No Hidden Face
    → FaceFragment Final Visible = false

Air                    | Air
    → No Hidden Face
    → FaceFragment Final Visible = false

Solid                  | Air
    → Solid-side = Negative
    → Evaluate each Region

Air                    | Solid
    → Solid-side = Positive
    → Evaluate each Region
```

对每个 Region：

```text
NeedHiddenFace(Region)
=
    CurrentBoundaryState
    AND
    !NaturalBaseCoverage(Region)
    AND
    !CoplanarBaseCoverage(Region)
```

其中：

```text
CurrentBoundaryState =
    (Negative Solid AND Positive Air)
    OR
    (Negative Air AND Positive Solid)

NaturalBaseCoverage(Region) =
    在 Solid-side infinitesimal offset 处
    BaseDensity > IsoLevel

CoplanarBaseCoverage(Region) =
    Region 被当前 SpatialFace 的 Coplanar Base Geometry 覆盖
```

如果：

```text
CoplanarBaseCoverage(Region) == true
```

则：

```text
FaceFragment Final Visible = true
Hidden Face = false
```

如果：

```text
NaturalBaseCoverage(Region) == true
```

则：

```text
Natural solid exists elsewhere
    →
No Hidden Face
FaceFragment Final Visible 取决于 13.8.3
```

如果：

```text
NeedHiddenFace(Region) == true
```

则：

```text
Hidden Face Fragment
    →
Solid-side Chunk
    →
VoxelMainMaterial(Solid-side Voxel)
```

该表达式与 Phase 2A 的 Region Evaluation 一致：

- Region Partition 仍由完整 Planar Boundary Graph 决定。
- `isSolidSide` 的外部输入由本节 Base Density classification 正式替代。
- 不允许因为 Bridge 存在就直接生成 Hidden Face。

---

## 14. Excavation

### 14.1 Voxel Internal Geometry

```text
Voxel A: Solid → Air
```

删除：

```text
完全属于 A 内部区域的 Point / Edge / Triangle
```

保留：

```text
A/B SpatialFace 上的 Bridge
B-local Geometry
```

### 14.2 Neighbor Natural Mesh

Excavation A 后：

```text
B 原有自然 Geometry 完全不变
B 不重新切割
```

需要更新的只是：

```text
A-local Geometry 删除
相关 SpatialFace 的 Hidden Face evaluation
```

---

## 15. Hidden Face and Bridge on Same SpatialFace

Bridge 与 Hidden Face 可以同时涉及同一个 SpatialFace，但二者不是竞争关系：

```text
SpatialFace 被 Bridge / Natural Boundary 划分成 regions；
Hidden Face 只生成在需要补面的 region。
```

因此不应产生：

```text
两个重复 Hidden Face
Bridge 与 Hidden Face 在完全同一 region 上重叠
不必要的 z-fighting
```

实现必须保证 Hidden Face 的 region 边界复用 Bridge / Natural Boundary 的 shared Vertex / Edge。

---

## 16. Degenerate Geometry After Excavation

Excavation 后如果只剩：

```text
零面积 Triangle → 删除
零长度 Edge → 删除
孤立 Point → 删除
```

删除规则必须与 Bridge Lifetime Invariant 一致：

- 如果零面积 Triangle 是某个 Bridge 的唯一支撑，则该 Bridge 也应删除。
- 如果零长度 Edge 是某个 BridgeEdge，则该 BridgeEdge 及其孤立 BridgePoint 删除。

---

## 17. Fill / Restore

架构必须保留 `Air → Solid` 的恢复能力：

```text
Solid → Air：
    删除当前 Voxel-local Geometry
    更新相关 SpatialFace / Hidden Face

Air → Solid：
    恢复该 Voxel 的 Base / Pre-cut Geometry
    删除不再需要的 Hidden Face
```

要求：

```text
Base Geometry 与 Voxel-local Geometry
不能因为 Mining 而被永久破坏。
```

“不可变缓存”是架构语义要求；不锁定为某一个具体 C# 数据结构，也不要求现在设计大型 Geometry Object System。

---

## 18. Geometry Lifecycle

初始生成：

```text
Base Terrain
    ↓
Base Mesh
    ↓
Voxel Boundary Cutting
    ↓
Voxel-local Geometry
    ↓
Bridge Geometry
```

挖掘：

```text
Voxel Solid → Air
    ↓
Remove Voxel-internal Geometry
    ↓
Remove unsupported Bridge
    ↓
Evaluate adjacent SpatialFaces
    ↓
Generate required Hidden Face regions
```

恢复：

```text
Voxel Air → Solid
    ↓
Restore Voxel-local Geometry
    ↓
Remove now-invalid Hidden Face regions
    ↓
Re-evaluate adjacent SpatialFaces
```

---

## 19. Two-Voxel Counterexample

```text
Voxel A = [0,1) × [0,1) × [0,1)
Voxel B = [1,2) × [0,1) × [0,1)
F_AB = (X, 1, 0, 0)
```

### 阶段 1：Pre-cut

一个自然 Triangle 横跨 A/B：

```text
Base Triangle
    ↓
Voxel Boundary Cutting
    ↓
A-local Polygon
Bridge segment s_AB
B-local Polygon
```

最终 Mesh topology：

```text
s_AB 的 P0 / P1 被 A-local 与 B-local Polygon 共享。
```

### 阶段 2：Excavate A

```text
删除：A-local Polygon 及 A 内部 Geometry
保留：Bridge s_AB
保留：B-local Polygon
B：不重新切割
```

### 阶段 3：Hidden Face Evaluation

```text
F_AB：A = Air，B = Solid
    ↓
s_AB 将 F_AB 划分为 regions
    ↓
只对 Solid B 暴露且未被自然边界覆盖的 region 生成 Hidden Face
    ↓
Hidden Face 边界复用 s_AB 的 shared Vertex / Edge
```

---

## 20. Three-Voxel Counterexample

```text
Voxel A = [0,1)
Voxel B = [1,2)
Voxel C = [2,3)

F_AB = (X, 1, 0, 0)
F_BC = (X, 2, 0, 0)
```

一个自然 Triangle 横跨 A/B/C。

### Pre-cut

```text
A-local Polygon
Bridge s_AB
B-local Polygon
Bridge s_BC
C-local Polygon
```

最终 topology：

```text
s_AB 被 A/B 两侧共享
s_BC 被 B/C 两侧共享
B-local Polygon 同时引用 s_AB 与 s_BC
```

### Excavate A

```text
删除：A-local Polygon
保留：s_AB
保留：B-local Polygon
保留：s_BC
保留：C-local Polygon
```

```text
B：自然 Geometry 不变，不重新切割
C：自然 Geometry 不变，不重新切割
```

### SpatialFace Evaluation

```text
F_AB：A = Air，B = Solid
    → 由 s_AB 划分 F_AB
    → 只生成需要补面的 Hidden Face region

F_BC：B = Solid，C = Solid
    → 不生成 Hidden Face
```

---

## 21. Coordinate Precision

### 21.1 Chunk Local Space

Boundary Cutting 必须在 Chunk Local Space 执行。

```text
World Coordinate
    ↓
Chunk Coordinate（integer）
    ↓
Chunk Local Position（float）
    ↓
Triangle / Plane Intersection
```

不得在巨大 World Coordinate 中直接做全部几何计算。

### 21.2 GeometryTolerance

几何判断必须使用统一的 `GeometryTolerance`，处理：

```text
Point on Plane
Zero-length Segment
Intersection Point equality
Zero-area Triangle
Coplanar Triangle
Point on Voxel Boundary
```

禁止各函数自行选择不同阈值。

Tolerance 具体数值属于实现参数，但必须全系统统一，并在 Chunk Local Space 内解释。

---

## 22. Source of Truth and Persistence

```text
VoxelField = 唯一 Mutable Edit State
Base Terrain = Seed + Generation Parameters 的派生结果
Mesh = Voxel State + Base Terrain 的派生表现
```

不得把当前 Mesh 反向作为唯一世界状态。

Save / Load 应保存：

```text
World Seed
Generation Parameters
Generation Version
Voxel Modifications
```

不保存运行时 Mesh。

---

## 23. Topology Rules

1. Voxel 是空间划分，不是 Mesh。
2. Voxel 使用 min-corner `[c, c+1)` 契约。
3. SpatialFace 是两个相邻 Voxel 的唯一有限单位空间面。
4. SpatialFace identity = `(Axis, PlaneCoordinate, U, V)`。
5. World Outside = Air。
6. Cross-Chunk SpatialFace 只有一个逻辑 Face。
7. Bridge 是 Mesh Geometry 与 Voxel Boundary 的切割关系。
8. Bridge provenance 独立，最终 Mesh Vertex / Edge 必须 shared / welded。
9. Bridge 必须由当前有效 Mesh Geometry 支撑。
10. 没有支撑 Geometry 的 Bridge 必须删除。
11. 位于 SpatialFace 上的普通 Base Edge / Vertex 不自动成为 Bridge。
12. Point Touch 不产生 Bridge。
13. Zero-length Segment 不产生 Bridge。
14. Coplanar 不产生 Bridge。
15. Hidden Face 由 Solid / Air 邻接关系决定。
16. 同一个 SpatialFace 只允许一个 Hidden Face evaluation result。
17. Hidden Face 只填补需要填补的 SpatialFace region。
18. Excavation 删除 Voxel 内部 Geometry，不重新切割邻居自然 Mesh。
19. Base Geometry 不得因 Mining 被永久破坏，以支持 Fill / Restore。
20. Mining 不直接修改 Density、Base Mesh、Bridge、Hidden Face。

---

## 24. Required Invariants

```text
VoxelState ∈ {Solid, Air}

SpatialFace
    ↔ 相邻 Voxel 或世界边界之间的一个有限单位空间面

BridgeEdge
    → valid supporting Mesh Geometry

BridgePoint
    → at least one valid BridgeEdge

HiddenFace
    → one SpatialFace
    + opposite Solid/Air state
```

以及：

```text
No unsupported Bridge
No orphan BridgePoint
No duplicate SpatialFace for the same adjacent pair
No duplicate Hidden Face for the same SpatialFace
No duplicate final Mesh Vertex / Edge on the same cutting boundary
```

---

## 25. Current Scope

V1 定义：

- Voxel（min-corner）
- Chunk
- SpatialFace
- Base Mesh
- Voxel Boundary Cutting
- BridgePoint
- BridgeEdge
- Voxel-local Geometry
- Hidden Face
- Solid / Air
- Excavation
- Geometry Lifetime
- Coordinate Precision

V1 不实现：

- Player Mining UI
- Mining Speed
- Item / Inventory / Pickup
- Material Drop
- Save / Load Runtime
- Network Synchronization
- Terrain Collider Architecture
- Freeform Mining
- Non-cubic Voxel
- Dynamic VoxelSize
- ECS
- Service / Factory / Registry Architecture
- Global Bridge Registry / Global Edge Registry

---

## 26. Validation Requirements

实现前至少验证：

```text
Triangle completely inside Voxel       → 0 Bridge
Triangle completely outside            → 0 Geometry, 0 Bridge
Triangle crosses one boundary          → valid segment + 1 BridgeEdge
Triangle crosses multiple boundaries   → all intersections, correct polygons
Point contact                          → 0 Bridge
Zero-length intersection               → 0 Bridge
Coplanar triangle                      → 0 Bridge
Base Edge already on SpatialFace       → Base Edge, not Bridge
Excavation                             → internal delete, supported Bridge keep
Two adjacent Voxels                    → 1 SpatialFace
Overlapping independent Bridge         → provenance independent, final topology shared
Chunk boundary                         → 1 logical SpatialFace, canonical owner unique
Negative coordinates                   → floor / min-corner semantics
Repeated excavation                    → idempotent evaluation
Excavation leaving line / point        → degenerate geometry deleted consistently
```

---

## 27. Design Principle

```text
Voxel       = Editable Spatial State
Chunk       = Spatial Partition + Mesh Rebuild Unit
SpatialFace = Shared Finite Spatial Boundary
Base Mesh   = Natural Terrain Geometry
Bridge      = Mesh / Voxel Boundary Cutting Topology
Hidden Face = Geometry Derived from Solid/Air State
```

任何一个概念都不应该承担另一个概念的职责。
