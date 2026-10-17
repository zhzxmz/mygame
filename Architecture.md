# Architecture

## 1. Document Purpose

This document defines the high-level architecture and core boundaries of the terrain system.

The terrain system is responsible for representing a large, continuous, procedurally generated world surface that can be modified by gameplay systems such as mining.

The system uses:

* Procedural terrain generation
* Density-based terrain representation
* Marching Cubes surface generation
* Chunk-based spatial partitioning
* VoxelField-based terrain modification

The terrain Mesh is a derived representation.

Terrain modification data must not depend on Mesh topology, Mesh vertices, Density sample indices, or Chunk ownership.

---

# 2. Core Design Principles

## 2.1 Terrain Is Continuous

The terrain is represented visually as a continuous Mesh.

It is NOT a Minecraft-style block Mesh.

The terrain surface may contain:

* slopes
* cliffs
* caves
* irregular natural surfaces
* continuous curved or angled boundaries

Marching Cubes is responsible for converting the terrain representation into the visible surface Mesh.

---

## 2.2 Terrain Modification Is Discrete

Although the terrain surface is continuous, terrain modification uses a discrete Edit Grid.

The fundamental terrain modification unit is an Edit Cell.

Default:

```text
EditCellSize = 1.0 meter
```

An Edit Cell is a logical spatial region:

```text
1m × 1m × 1m
```

The Edit Cell is a data-space concept.

It is NOT:

* a Mesh cube
* a Mesh vertex
* a Density sample
* a Marching Cubes cell
* a Chunk
* a GameObject

---

## 2.3 Edit Grid Is Independent From Density Grid

The Edit Grid and Density Grid are separate concepts.

### Edit Grid

Defines gameplay modification coordinates.

Example:

```text
CellCoordinate = (3, 5, -2)
```

This means:

```text
x ∈ [3, 4)
y ∈ [5, 6)
z ∈ [-2, -1)
```

when:

```text
EditCellSize = 1m
```

### Density Grid

Defines where terrain surface information is sampled for surface generation.

Density sample spacing may differ from EditCellSize.

The terrain architecture must not assume:

```text
EditCellSize == DensityCellSize
```

unless explicitly chosen by a future implementation.

---

# 3. Coordinate Spaces

The terrain system uses the following coordinate concepts.

## 3.1 World Space

Unity world coordinates.

Used by:

* Player
* Camera
* Physics
* Raycasts
* GameObjects

---

## 3.2 Terrain Local Space

Terrain Local Space is the authoritative spatial coordinate system for terrain operations.

Terrain modification, Edit Grid queries, terrain generation queries, and terrain-local terrain calculations must operate in Terrain Local Space.

World positions must be converted to Terrain Local Space before entering terrain-domain logic.

Example:

```text
World Position
      ↓
Terrain WorldToLocal
      ↓
Terrain Local Position
```

---

## 3.3 Edit Grid Coordinates

Edit Grid coordinates are integer coordinates derived from Terrain Local Space.

The mapping is:

```text
CellCoordinate = floor(LocalPosition / EditCellSize)
```

The Edit Grid uses half-open intervals:

```text
[min, max)
```

Negative coordinates must behave correctly.

Example with CellSize = 1:

```text
Position  0.0  → Cell 0
Position  0.9  → Cell 0
Position  1.0  → Cell 1

Position -0.1  → Cell -1
Position -1.0  → Cell -1
Position -1.1  → Cell -2
```

No epsilon correction or nearest-cell rounding is part of the coordinate contract.

---

## 3.4 Voxel, Chunk and Mesh Coordinate Conventions

The following conventions are global and apply to every terrain subsystem.

Voxel Coordinate:

```text
VoxelCoordinate = floor(TerrainLocalPosition / VoxelSize)
VoxelSize       = 1.0 meter
Voxel c covers  [c.x, c.x+1) × [c.y, c.y+1) × [c.z, c.z+1)
```

* A Voxel Coordinate is an integer min-corner coordinate in Terrain Local Space.
* Coordinates use mathematical floor semantics and half-open intervals. Negative
  coordinates must use floor division, never truncation toward zero; Chunk and
  Chunk-local decomposition must use positive modulo.
* A Voxel Coordinate identifies an Edit Cell. It is not a Density sample index
  and not a Marching Cubes cell.

Chunk Coordinate:

```text
ChunkCoordinate      = floor(VoxelCoordinate / ChunkVoxelSize)   (per axis)
LocalVoxelCoordinate = VoxelCoordinate mod ChunkVoxelSize         (positive modulo)
```

* A Chunk Coordinate is an integer, absolute coordinate. It does not depend on
  which Chunk range is currently generated.
* `chunkStartCoordinate` selects the generated range only; it never shifts the
  meaning of an absolute Chunk Coordinate.
* `ChunkVoxelSize` is expressed in Voxel units and
  `ChunkWorldSize = VoxelSize × ChunkVoxelSize`, independently on each axis.
* The concrete Chunk and Density configuration belongs to
  `Specifications/Chunk.md` and `Specifications/CoordinateMigration.md`, not to
  this document.

Coordinate conversion responsibility:

```text
World Space             ↔ Terrain Local Space : Terrain root Transform
Terrain Local Position  → Voxel Coordinate    : floor(position / VoxelSize)
Voxel Coordinate        → Chunk Coordinate    : floor division by ChunkVoxelSize
Chunk Coordinate        → Chunk Local Origin  : ChunkCoordinate × ChunkWorldSize
Chunk-local Mesh vertex → Terrain Local Space : Chunk GameObject placement
Terrain Local Space     → World Space         : Terrain root Transform
```

* World positions must be converted to Terrain Local Space before any
  terrain-domain logic consumes them.
* Mesh vertices are Chunk-local; the Chunk placement and the Terrain root
  Transform must never be baked into Mesh vertex data.
* Any legacy coordinate API that uses different units must be identified
  explicitly. New Voxel-aware code must not silently mix legacy units with the
  1-meter Voxel contract.

---

# 4. Terrain Data Layers

The terrain architecture is conceptually divided into several layers.

```text
Base Terrain
     ↓
VoxelField Edit Layer
     ↓
Effective Terrain
     ↓
Surface Representation
     ↓
Mesh
```

---

### 3.4.1 Voxel-Aware 几何的派生关系与 Region 粒度（当前实现）

```text
VoxelField            = 唯一 Mutable Edit State
Raw Base Mesh         = Base Terrain 的派生结果
ChunkGeometry         = RawBaseMesh + Voxel Boundary 预切（不含 Voxel 状态）
HiddenFace 几何        = 由 Region 材料侧覆盖判定派生（仅 Solid 一侧确有材料的 Region），
                         按 ChunkGeometry 细分粒度输出；绕序自材料侧指向空腔
Final Mesh            = ChunkGeometry + VoxelField 编辑结果的派生表现
```

- 修改入口（`RebuildChunksAffectedByVoxel`）与完整重建入口
  （`RebuildVoxelAwareGeometryFromVoxelField` / `Regenerate`）都必须走 Voxel-Aware
  Mutation 路径，并同步 MeshFilter / MeshCollider。
- Region 分类保留“代表性单点结论”，但**几何输出按覆盖边界细分**（详见
  `Specifications/VoxelAwareMesh.md` §13.7.6）。
- 覆盖判据（覆盖率 / 多覆盖 / 漏覆盖）是近似判据，其误差界与适用范围见
  `Specifications/VoxelAwareMesh.md` §26 与 `Specifications/CoordinateMigration.md` §6.3。
## 4.1 Base Terrain

Base Terrain is generated from the world seed and generation parameters.

It represents the terrain before gameplay modifications.

Base Terrain is conceptually immutable during ordinary gameplay.

Mining must not directly rewrite the procedural generation result.

The Base Terrain should remain reproducible from:

```text
World Seed
+
Generation Parameters
```

---

## 4.2 VoxelField Edit Layer

The Edit Layer stores persistent gameplay modifications to the terrain.

The current and authoritative Edit Layer implementation is:

```text
DensityTerrain.VoxelField
```

`VoxelField` is the only Mutable Edit State of the terrain system.

Current Voxel contract:

```text
VoxelSize = 1.0 meter
VoxelState = Solid | Air
```

Current supported edit operation:

```text
Excavate:
Solid -> Air
```

An explicit Air Voxel means:

> The corresponding logical Edit Cell has been excavated.

Sparse storage semantics:

```text
missing voxel = no excavation edit
explicit Air = excavated
```

The missing-voxel default is an internal sparse-storage detail and must not be interpreted as "the entire world is Solid"; Base Terrain defines the world, VoxelField only records edits.

The Edit Layer does not store:

* Mesh vertices
* Mesh triangles
* Density sample arrays
* Chunk references
* Mesh collider data
* Player references

A Voxel is a logical Edit Cell. It is NOT a Minecraft block, NOT a Mesh cube, and NOT a Marching Cubes cell.

---

## 4.3 Effective Terrain

Effective Terrain represents the terrain state after applying the VoxelField Edit Layer to the Base Terrain.

Conceptually:

```text
Effective Terrain = Base Terrain + VoxelField Edits
```

For the current Excavation-only version:

```text
Excavate Cell
```

means that the corresponding Edit Cell is explicitly Air in VoxelField.

The Effective Terrain layer is the conceptual source used by surface generation.

### 4.3.1 Current Effective Density Policy (Excavation-only V1)

For each Density sample at Terrain Local position `p`:

```text
if VoxelField.IsAir(Voxel(p)):
    EffectiveDensity(p) = AirDensity
else:
    EffectiveDensity(p) = BaseDensity(p)
```

This policy is the current Excavation-only policy, not an immutable final rule. Future operations such as Fill, Build, Replace Material, Ore placement, or player construction may extend the Edit State / Edit Operation set without changing the overall architecture:

```text
Base Terrain
+
Edit State
->
Effective Terrain
```

---

## 4.4 Terrain Data Authority

The following authority rules are architectural invariants.

### Authoritative Mutable State

```text
DensityTerrain.VoxelField
```

is the single authoritative Mutable Terrain State.

### Derived Data

The following are derived data only:

```text
Effective DensityField
TerrainChunk.Density
TerrainChunk.Mesh
MeshFilter.sharedMesh
MeshCollider.sharedMesh
```

Derived data may be discarded and rebuilt from Base Terrain + VoxelField at any time.

### Base Terrain

Base Terrain is generated from:

```text
World Seed
+
Generation Parameters
```

Base Terrain is immutable during ordinary gameplay. Player Mining must not directly rewrite the procedural generation result.

Base Terrain is not stored in VoxelField, Mesh, or MeshCollider.

In the current implementation, `TerrainChunk.Density` may temporarily serve as the Base Density runtime snapshot. Long term it may evolve into a procedural Base Terrain Sampler that is regenerated on demand from Seed + Generation Parameters.

### Legacy Edit Representations

`TerrainEditLayer` and `TerrainEditRecord` are legacy / experimental edit representations. They are frozen for the new Voxel Mining architecture:

> New Terrain Mining must not write to TerrainEditLayer / TerrainEditRecord.

A dedicated Legacy Cleanup step may later remove or migrate these types; they must not be deleted or silently broken before that step.

### Legacy V1 Density Mining

Legacy V1 Mining directly modified DensityField through `TerrainModification` / `TerrainMining`. This behavior is frozen legacy behavior and is not part of the new Terrain Editing Architecture. New Mining must write only to `VoxelField`.

---

# 5. Terrain Edit Model

## 5.1 Current Supported Operation

The current terrain modification operation is:

```text
Excavate
```

An Excavation targets exactly one Edit Cell, implemented as one explicit Air Voxel in `DensityTerrain.VoxelField`.

Example:

```text
Operation = Excavate
VoxelCoordinate = (10, 4, -3)
```

This means:

> The terrain inside this Edit Cell has been excavated.

---

## 5.2 Edits Are Cell-Based

Terrain edits are represented by Voxel Coordinates in Terrain Local Space.

They are not represented by arbitrary continuous shapes.

The system must not require:

* CubeModificationShape
* arbitrary brush geometry
* brush radius
* surface normal
* arbitrary cutting plane
* arbitrary Boolean geometry

for the current mining implementation.

---

## 5.3 Adjacent Excavated Cells

Multiple Excavated Cells may be adjacent.

Example:

```text
[X][X][ ][ ]
[X][X][ ][ ]
[ ][ ][ ][ ]
```

Each `X` is an independent Air Voxel edit in VoxelField.

The Effective Terrain treats the union of these excavated cells as excavated terrain.

Adjacent edits must produce a continuous resulting terrain surface.

There must be no requirement that each edit remain visually separated from neighboring edits.

---

# 6. Chunk Architecture

Chunks are a spatial partitioning and surface rebuild mechanism.

A Chunk is NOT the owner of terrain modification data.

The following rule is mandatory:

> Chunk boundaries must never define the logical boundary of a terrain edit.

An Edit Cell may cross a Chunk boundary.

A single Voxel edit must remain a single logical edit even if its spatial effect requires multiple Chunks to rebuild.

Example:

```text
        Chunk A       Chunk B
      ┌─────────┬─────────┐
      │         │         │
      │    ┌────┼────┐    │
      │    │ Edit Cell │   │
      │    └────┼────┘    │
      │         │         │
      └─────────┴─────────┘
```

The Edit Cell does not split into two edits.

---

# 7. Mesh Is Derived Data

The Mesh is never authoritative terrain state.

The Mesh is generated from the current Effective Terrain representation.

Conceptually:

```text
Effective Terrain
       ↓
Surface Sampling
       ↓
Density / Surface Representation
       ↓
Marching Cubes
       ↓
Mesh
```

Mesh vertices are stored in Chunk-local space. Chunk placement maps Chunk-local
space to Terrain Local Space, and the Terrain root Transform maps Terrain Local
Space to World Space. Mesh vertex data must never bake in Terrain Local or World
offsets.

The exact implementation of the Surface Sampling stage is intentionally left to the terrain implementation specification.

Gameplay systems must not use Mesh topology as persistent terrain state.

---

# 8. Density Representation

The current terrain implementation uses a Density Field.

The Density Field provides samples used by Marching Cubes.

The fundamental contract is:

```text
density > isoLevel
    = solid

density <= isoLevel
    = air
```

The Density Field is a surface-generation representation.

It is not the same thing as the Edit Grid.

An Edit Cell must not be identified by Density sample indices.

---

# 9. Surface Generation

Surface generation converts the Effective Terrain state into a continuous Mesh.

The intended conceptual pipeline is:

```text
Base Terrain
      +
VoxelField Edit Layer
      ↓
Effective Terrain
      ↓
Surface Sampling
      ↓
Density Field
      ↓
Marching Cubes
      ↓
Chunk Mesh
```

The resulting Mesh may cross or approach Edit Cell boundaries smoothly.

An Edit Cell does not imply that the resulting Mesh must contain a visible cube-shaped Mesh boundary.

---

# 10. Terrain Modification and Surface Representation

Terrain modification and surface representation are intentionally separate.

An Edit Cell defines:

> What terrain state was changed.

The surface generation system defines:

> How that changed terrain state becomes a continuous Mesh.

Therefore:

```text
Edit Cell
    ≠
Cube Mesh
```

and:

```text
Edit Cell
    ≠
Marching Cubes Cell
```

These concepts must not be conflated.

---

# 11. Mining Architecture

Mining is a gameplay system that writes logical Voxel edits.

The conceptual flow is:

```text
Player Raycast
      ↓
World Hit Point
      ↓
Terrain Local Position
      ↓
Edit Grid Cell Selection
      ↓
Mining Preview
      ↓
Mining Commit
      ↓
DensityTerrain.VoxelField.Set(voxel, Air)
      ↓
Determine affected Chunk(s)
      ↓
Effective Terrain rebuild
      ↓
Surface Sampling
      ↓
Density
      ↓
Marching Cubes
      ↓
Chunk Mesh
      ↓
MeshFilter + MeshCollider
```

The Raycast identifies which Edit Cell the player is interacting with.

Mining does not directly edit Mesh vertices, Mesh triangles, or Density samples.

Mining does not write to `TerrainEditLayer` / `TerrainEditRecord`.

---

# 12. Terrain Modification Persistence

Terrain modifications must be persistable independently of Mesh data.

A future save system should conceptually store:

```text
World Seed
+
World Generation Parameters / Version
+
Sparse Excavation State
```

For the current Excavation-only architecture, Sparse Excavation State is the set of explicit Air Voxel coordinates in `DensityTerrain.VoxelField`.

It should not store:

```text
DensityField arrays
MaterialField arrays
Mesh
MeshCollider
TerrainChunk.Mesh
```

These are Derived Data and can be reconstructed after loading.

If the Edit State later expands to Fill / Build / Replace Material, it may be upgraded to Operation Records; the persistent model must remain:

```text
Base World
+
Edit State
->
Effective World
```

### 12.1 Deterministic World Regeneration

Deterministic World Regeneration requires an explicit World Seed as part of the Generation Parameters. The current implementation may not yet expose a World Seed parameter; this is recorded as a pending implementation requirement, not as an architecture exception.

---

# 13. Current Scope

The current terrain modification system intentionally supports:

* Cell-based excavation
* Integer Edit Grid coordinates
* 1m default Edit Cells
* Multiple adjacent excavated cells
* Cross-Chunk edits
* Continuous Mesh output
* Marching Cubes surface generation
* Persistent sparse Voxel edit state

The current system intentionally does NOT support:

* Arbitrary freeform mining
* Spherical mining brushes
* Arbitrary cutting planes
* Continuous sculpting
* Surface-normal-based carving
* Brush radius
* Boolean mesh operations
* Direct Mesh deformation

These may be considered future extensions, but they are not part of the current architecture.

---

# 14. Future Extensibility

Future terrain operations may introduce additional Edit Operations.

For example:

```text
Excavate
Fill
ReplaceMaterial
PlaceTerrain
```

Future operations must preserve the separation between:

```text
Gameplay Edit Data
```

and:

```text
Surface Representation
```

A future freeform operation must not require rewriting the existing VoxelField Edit Layer or making Mesh data authoritative.

---

# 15. Architectural Invariants

The following rules are architectural invariants.

### Rule 1

Terrain modification operates in Terrain Local Space.

### Rule 2

The Edit Grid is independent from the Density Grid.

### Rule 3

An Edit Cell is the current fundamental terrain modification unit.

### Rule 4

An Edit Cell is not a Mesh cube.

### Rule 5

A Chunk is not the owner of an Edit.

### Rule 6

An Edit may affect multiple Chunks.

### Rule 7

Mesh data is derived data.

### Rule 8

Density samples are surface-generation data, not persistent gameplay edit data.

### Rule 9

Mining writes logical Voxel edits in `DensityTerrain.VoxelField` rather than directly modifying Meshes or Density samples.

### Rule 10

`DensityTerrain.VoxelField` is the single authoritative Mutable Terrain State; Base Density, Effective Density, Mesh, and MeshCollider are derived data.

### Rule 11

The current version does not attempt to support arbitrary freeform terrain carving.

---

# 16. Current Target

The adopted terrain state model is Model B:

```text
Base Terrain
    +
VoxelField Edit Layer
    ->
Effective Terrain
    ->
Surface Sampling
    ->
Density
    ->
Marching Cubes
    ->
Mesh / MeshCollider
```

The immediate target is:

> Build a terrain modification system where the world remains a continuous Marching Cubes Mesh, while mining behaves approximately like Minecraft-style discrete excavation at the gameplay/data level.

The terrain does NOT become a block Mesh.

Only the modification unit is discrete.

The visual result remains a generated continuous terrain surface.
