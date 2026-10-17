# Coordinate Migration — Scheme A



## 1. Goal



Migrate the current Terrain configuration to a consistent 1-meter Voxel coordinate contract while preserving existing Terrain generation and mining behavior.



This migration changes coordinate configuration and coordinate API consistency. It does not redesign the DensityField, MaterialField, Marching Cubes, or VoxelAwareMesh algorithms.



## 2. Coordinate Contract



### 2.1 Voxel



* `VoxelField.VoxelSize = 1.0` meter.

* Voxel coordinates are integer coordinates in Terrain Local Space.

* Voxel-to-position conversion uses the existing floor-based coordinate convention.

* Negative coordinates must use mathematical floor division and positive modulo, not truncation toward zero.



### 2.2 Chunk



* `chunkVoxelSize = (75, 37, 50)` specifies the number of Voxel units along each Chunk axis.

* The corresponding Chunk world dimensions are `(75, 37, 50)` meters.

* Chunk coordinates are integer coordinates.

* `chunkStartCoordinate` retains its existing absolute ChunkCoordinate semantics.

* Chunk-local mesh geometry and Terrain Local Space conventions must not change.



### 2.3 Density Sampling



The current DensityField generator uses a scalar `resolution`, shared by all three axes.



For this migration:



* `resolution = 75`.

* Each Chunk has 75 Density intervals and 76 Density samples per axis.

* DensityField dimensions are therefore `76 × 76 × 76` samples.

* For a Chunk size of `(75, 37, 50)` meters, the Density sample spacing is:



  * X: `1.0` meter

  * Y: `37 / 75` meters

  * Z: `50 / 75` meters



The Density sample spacing is not the same as the Voxel size on every axis. Per-axis Density resolution is explicitly out of scope.



## 3. SampleScene Target Configuration



* `voxelSize = 1.0`

* `chunkVoxelSize = (75, 37, 50)`

* `resolution = 75`

* `chunkCount = (2, 1, 2)`

* `chunkStartCoordinate = (0, 0, 0)`

* `useVoxelAwareMeshPrecut = enabled`



The expected total world dimensions are `(150, 37, 100)` meters.



## 4. Coordinate Mapping Requirements



Chunk coordinate conversion must use floor division and positive modulo independently on each axis.



For Chunk size `(75, 37, 50)`:



* Voxel `(75, 0, 0)` maps to Chunk `(1, 0, 0)`.

* Voxel `(-1, 0, 0)` maps to Chunk `(-1, 0, 0)`, local X = 74.

* Voxel `(0, 37, 0)` maps to Chunk `(0, 1, 0)`.

* Voxel `(0, 0, 50)` maps to Chunk `(0, 0, 1)`.



With `chunkStartCoordinate = (5, 0, 7)` and `chunkCount = (2, 1, 2)`, the generated Chunk coordinates must be:



* `(5, 0, 7)`

* `(6, 0, 7)`

* `(5, 0, 8)`

* `(6, 0, 8)`



## 5. Compatibility Requirements



* Preserve DensityField and MaterialField semantics.

* Preserve Terrain Local Space and Chunk-local mesh coordinates.

* Preserve absolute ChunkCoordinate semantics.

* Preserve existing Mining V1 core behavior.

* Do not change Mesh generation algorithms as part of this migration.

* Do not introduce a global registry, service layer, factory architecture, or ECS.

* Do not implement new mining features.

* Do not change runtime mutation transaction semantics in this migration.

* Do not introduce per-axis Density resolution in this migration.



Any legacy coordinate API that uses different units must be identified explicitly. New Voxel-aware code must not silently mix the legacy unit with the 1-meter Voxel contract.



## 6. Validation Strategy



### Stage A — Coordinate Contract



Verify:



1. Voxel size is 1 meter.

2. Chunk dimensions are `(75, 37, 50)` Voxel units.

3. Positive and negative coordinate conversion follows floor division and positive modulo.

4. Absolute `chunkStartCoordinate` semantics remain unchanged.

5. Non-identity Terrain transforms preserve existing local/world coordinate behavior.

6. SampleScene uses the target configuration.



### Stage B — Density Sampling and Boundary Dependencies



Verify independently:



1. Density sample positions are correct on all three axes.

2. Shared Density sample planes agree between adjacent Chunks.

3. Voxel edits update the expected Density samples.

4. Dirty Chunk propagation follows actual sampling and mesh dependencies.

5. Cross-Chunk SpatialFace ownership and hidden-face behavior remain correct.



Do not assume that a Voxel belonging to one Chunk automatically proves that only that Chunk, or necessarily both neighboring Chunks, require rebuilding. Verify the actual sample and geometry dependencies.



### Stage C — Regression



Run the existing Terrain V2 and Mining V1 validation suites.



Keep the existing legacy MultiChunk validation contract intact unless a separate, justified migration is approved. Add separate tests for the new 1-meter contract.



Report compile errors, validation failures, Unity batchmode exit code, and `git diff --check` results.



Do not declare the entire migration complete until Stages A, B, and C have passed.



### 6.3 Stage B3-C 覆盖判据与容差依据（当前实现）

Hidden Face 覆盖正确性用**整面区域量化判据**验证（slab 分解 + 梯形积分，非离散点采样）：

```text
多覆盖  = 并集面积 − 逐点材料侧判据面积(面平面外 1mm, Base Density > 0)
漏覆盖  = 逐点材料侧判据面积 − 并集面积
重叠    = Σ三角形面积 − 并集面积

B3-D 材料边界语义：判据面积 = **未挖空（Solid）一侧确有材料**（Base Density > IsoLevel）
的面积。只有该侧存在材料时才需要暴露内壁；该侧本来就是空气（Base Density <= IsoLevel）
的位置不得生成任何面。因此整面判据同时断言“材料侧（坑底/内壁）完整覆盖”与
“材料侧为空的一侧零几何（不造盖板、不封堵空腔）”。
```

#### 6.3.1 条件性的推导上界（非无条件证明）

细分单元边长 h ≤ 0.04m（cellsPerAxis = clamp(ceil(size/0.04), 2, 32) ⇒ h = size/cells ≤ 0.04）。
记 `A_face` = 该 SpatialFace 的面面积（Scheme A 夹具中为 1 m²，但下列关系与 `A_face` 取值无关）、
`A_material` = 面内材料侧面积（= 逐点判据 Base Density > 0 的面积，即应封闭区域），
要求 **0 ≤ A_material ≤ A_face**；
`L` = 面内覆盖边界长度、`N` = 与该边界相交的单元数：

```text
(1) 单元量化：边界单元四角同号时无法切分，整格按角点结论输出 ⇒ 每单元误判 ≤ h²
(2) 计数（有条件）：仅当边界在每个单元内穿越次数有界（≤ 2 次）时才有 N ≤ 2L/h
(3) ⇒ 在该前提下 E ≤ N·h² ≤ 2·L·h = 0.08·L
(4) 二值覆盖面积差的平凡上界（无条件）：同一面上“材料侧面积”与“实际覆盖面积”之差满足
    E ≤ min(A_material, A_face − A_material)，其中 0 ≤ A_material ≤ A_face
```

**适用条件与限制**：

- (2)/(3) 依赖“边界单元穿越次数有界”这一前提，属**有条件推导**；对未经约束的
  任意高频抖动边界，`N` 可远超 `2L/h`，`2Lh` 不再成立（此时仅 (1) 与 (4) 仍成立）。
- `L·h = 0.04·L` 是条件上界 `2Lh` 的一半，属**经验系数**（实测为 `2Lh` 的 4%–34%），
  **不是无条件上界**。
- (4) 只约束同一面上的二值覆盖面积差，不约束拓扑（重叠/退化/绕序等需另行断言）。

#### 6.3.2 实测误差

| 面 | 边界 L (m) | 材料侧 A_material (m²) | 实测偏差 (m²) | 严格上界 2Lh (m²) | 占严格上界 |
|---|---|---|---|---|---|
| `1:14:37:0` | 0.94 | 0.4094 | 0.0028 | 0.0752 | 4% |
| `1:16:50:75` | 0.49 | 0.8776 | 0.0132 | 0.0394 | 34% |
| 6 个自然面 | — | 0.47–0.54 | ≤ 0.0025 | — | ≤ 3% |

判据侧误差已实测排除：200×200 → 800×800 网格的预期面积差 ≤ 1.44e-4 m²。

#### 6.3.3 采取的工程容差（现状，未因定稿复核而放宽）

```text
非整面： tol = clamp( max(0.04·L, 0.15·A_material), 0.005, 0.02 ) m²
整面：   tol = 1e-3 m²
```

各项性质：

| 项 | 性质 | 说明 |
|---|---|---|
| `0.04·L` | **经验系数**（取严格上界 `0.08·L` 的一半） | 实测为严格上界的 4–34%；未证明其为上界 |
| `0.15·A_material` | **经验项**（真实严格上界是 `A_material`） | 小尺度特征实测相对误差 10.8%（`1:16:50:75` 的 air 侧 0.1225 m² 上偏差 0.0132 m²；按材料侧 0.8776 m² 则为 1.5%） |
| 下限 `0.005` | 工程阈值 | 抑制极小开口的数值噪声；会使极小开口容差不再下降 |
| 上限 `0.02` | 工程阈值 | 限定 1 m² 级面的适用范围；不适用于更大面 |
| 整面 `1e-3` | 工程阈值 | 均匀 Region 走既有单多边形路径、判据侧无边界离散误差，仅剩裁剪/焊接浮点误差 |

#### 6.3.4 判据能证明 / 不能证明

```text
能证明：与夹具同类的面上，最终 Mesh 覆盖与逐点材料侧判据一致到上述工程容差内，
        且无重叠 / 退化 / 重复 / 越界 / 绕序错误；均匀 Region 几何与既有实现一致。
不能证明：任意噪声地形的解析全等；saddle（对角）单元整格判定的 ≤ h² 局部误差；
          高曲率/极小开口下的相对误差界；0.04·L 与 0.15·A_material 均非严格上界；
          N ≤ 2L/h 与 E ≤ 2Lh 仅在有界穿越前提下成立，未覆盖任意高频抖动边界。
```
## 7. Scope Boundary



This migration does not include:



* New Player Mining behavior

* New Raycast behavior

* New Collider functionality

* Inventory, Items, Yield, or Pickup changes

* DensityField or MaterialField redesign

* Marching Cubes redesign

* Per-axis Density resolution

* Runtime mutation rollback or transaction redesign



## 8. Completion Report



Report:



1. Files modified.

2. Coordinate APIs and units audited.

3. Final SampleScene configuration.

4. Coordinate contract test results.

5. Density sampling and shared-boundary test results.

6. Terrain V2 and Mining V1 regression results.

7. Unity batchmode exit code and compile errors.

8. `git diff --check` and `git diff` summary.

9. Any remaining known limitations.
