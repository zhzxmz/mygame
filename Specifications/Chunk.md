# Chunk Specification

**Version:** V2
**Status:** Active
**Scope:** Terrain Local Space / World Space Partition / Terrain Data / Terrain Mesh

---

## 1. Purpose

Chunk is a finite spatial partition and management unit of the World Space.

A Chunk is responsible for managing the terrain-related spatial data and mesh
within its spatial boundary.

Chunk is not responsible for:

* World generation rules
* Mining gameplay logic
* Item or resource yield calculation
* Entity behavior
* Combat
* Inventory
* Crafting
* World-level rules

The purpose of Chunk is to provide a stable spatial boundary that other
systems can use to locate, modify, generate, and rebuild terrain data.

---

## 2. World, Space, Chunk and Voxel

The World represents the complete game world and its state.

Space represents the spatial domain within the World.

Chunk divides Space into finite regions so that the computer can manage,
generate, modify, and render parts of the World independently.

Voxel is a logical spatial unit used to discretize Space for computation.

The World itself is not composed of voxels or blocks.

Voxel is a representation used for:

* Spatial indexing
* Spatial queries
* Local spatial operations
* Structured terrain modification
* Mapping world positions to discrete spatial regions

A voxel does not imply that the terrain must visually appear block-based.

The terrain may remain continuous and natural while Voxel is used only as a
logical spatial representation.

The current architecture uses Model B:

```text
Base Terrain
    +
VoxelField Edit Layer
    ->
Effective Terrain
    ->
Density Sampling
    ->
Mesh / Collider
```

`DensityTerrain.VoxelField` is the single authoritative Mutable Terrain Edit
State. A Chunk never owns VoxelField and never creates a Chunk-local copy of a
Voxel edit.

The current implementation uses **Terrain Local Space** as the authoritative
coordinate space for terrain data and Chunk coordinates. World Space positions
are mapped into Terrain Local Space through the Terrain root Transform.

Conceptual relationship:

```text
World
└── Space
    ├── Chunk
    │   └── Terrain spatial data
    │
    └── Voxel
        └── Logical spatial division
```

---

## 3. Voxel and Density Sample

Voxel and Density Sample are different concepts.

### Voxel

A Voxel represents a logical spatial region used for spatial management and
operations.

### Density Sample

A Density Sample is a sample point/value used by DensityField to represent
terrain geometry.

DensityField may use a sampling resolution that is different from the
logical Voxel resolution.

Therefore:

```text
Voxel != Density Sample
Voxel != Terrain Block
Voxel != Mesh Vertex
```

The existence of Voxel must not require every voxel to be instantiated or
permanently stored in memory.

A voxel may be represented mathematically from its coordinates.

---

## 4. Voxel Size

`VoxelSize` defines the size represented by one logical Voxel in Terrain Local
Space.

The new Voxel Terrain / Voxel Mining architecture formally uses:

```text
VoxelSize = 1.0 meter
```

This value is fixed for the current VoxelField Edit Layer contract and is not
derived from `DensityTerrain.voxelSize`.

The legacy field `DensityTerrain.voxelSize = 0.1` belongs to the legacy
Density/Mining pipeline and is frozen. New code must use
`DensityTerrain.VoxelField.VoxelSize` (1m) for Edit Cell coordinates.

`VoxelSize` is not a fundamental property of the World. It may be changed in a
future specification revision, but any change must update the VoxelField
contract, the ChunkVoxelSize contract, and all related validations together.

`VoxelSize` is independent from the DensityField sampling resolution and from
the visual resolution of the Terrain Mesh.

The approved Scheme A migration keeps `VoxelSize = 1.0m` and adopts
`ChunkVoxelSize = (75, 37, 50)`; see §6 and `CoordinateMigration.md`.

---

## 5. Voxel Coordinate

A World Space position is first mapped into Terrain Local Space through the
Terrain root Transform. The resulting local position can be mapped to a logical
Voxel coordinate:

```text
VoxelCoordinate = floor(TerrainLocalPosition / VoxelSize)
```

The voxel represented by coordinate `(X, Y, Z)` corresponds to:

```text
[X * VoxelSize, (X + 1) * VoxelSize)
[Y * VoxelSize, (Y + 1) * VoxelSize)
[Z * VoxelSize, (Z + 1) * VoxelSize)
```

These intervals are expressed in Terrain Local Space.

Negative coordinates use the same floor / half-open contract:

```text
VoxelSize = 1

Position -0.1  -> Voxel -1
Position -1.0  -> Voxel -1
Position -1.1  -> Voxel -2
```

No epsilon correction or nearest-cell rounding is part of this contract.

---

## 6. Chunk Size

Chunk is a finite spatial partition of Space.

Chunk size is an implementation parameter and is not a fundamental architectural
requirement.

The approved migration target (Scheme A) is:

```text
ChunkVoxelSize = (75, 37, 50)
```

`ChunkVoxelSize` is expressed in Voxel units: each value is the number of Voxel
units along that Chunk axis. It is not a distance in meters and not a Density
resolution. With `VoxelSize = 1m`:

```text
ChunkWorldSize = (75m, 37m, 50m)
```

The earlier `16 × 16 × 16` value was the pre-migration architectural reference;
for the current implementation it is superseded by the approved Scheme A target
above (`CoordinateMigration.md`).

The existing prototype may still use legacy derived ChunkVoxelSize values; those
values belong to the frozen legacy Density/Mining pipeline and must not be used
for new Voxel Edit Cell / Chunk coordinate calculations.

The final Chunk size should be selected according to:

* Terrain generation cost
* DensityField memory usage
* Mesh generation cost
* Mesh complexity
* Frequency of terrain modification
* Number of neighboring Chunks affected by modifications
* Future streaming requirements

The following parameters should remain conceptually separate:

```text
VoxelSize
ChunkVoxelSize
DensityField Resolution
```

They must not be assumed to represent the same thing.

If:

```text
ChunkVoxelSize = N
```

then:

```text
ChunkWorldSize = VoxelSize × N
```

This relationship applies independently on each axis; a Chunk does not need to
be cubic.

The actual runtime Chunk configuration is being migrated from the legacy
prototype to the approved Scheme A target (`VoxelSize = 1m`,
`ChunkVoxelSize = (75, 37, 50)`) defined in `CoordinateMigration.md`. The new
Voxel Mining mesh-rebuild path must not run with legacy units.

---

## 7. Chunk Coordinate

A Chunk coordinate identifies the spatial Chunk containing a Voxel.

Conceptually:

```text
ChunkCoordinate =
    floor(VoxelCoordinate / ChunkVoxelSize)
```

The local voxel coordinate inside a Chunk is:

```text
LocalVoxelCoordinate =
    VoxelCoordinate mod ChunkVoxelSize
```

ChunkCoordinate is absolute in the terrain coordinate system. Selecting a
generated range (for example `chunkStartCoordinate`) does not shift absolute
ChunkCoordinate semantics.

With `VoxelSize = 1m` and the approved Scheme A `ChunkVoxelSize = (75, 37, 50)`,
floor semantics give (shown on the X axis; Y and Z use the same rule with 37 and
50):

```text
Voxel X  74 -> Chunk  0        Voxel X  -1 -> Chunk -1
Voxel X  75 -> Chunk  1        Voxel X -75 -> Chunk -1
Voxel X 149 -> Chunk  1        Voxel X -76 -> Chunk -2
Voxel X 150 -> Chunk  2
```

The same rule applies independently on each axis; Chunk axes do not share a
size. `(-1, 0, 0)` therefore has local X = 74, `(0, 37, 0)` belongs to Chunk
`(0, 1, 0)` and `(0, 0, 50)` belongs to Chunk `(0, 0, 1)`.

The project must reuse the existing `ChunkSpace` floor / positive-modulo
implementation. No second Chunk coordinate system may be introduced.

Chunk coordinate must be derived from the same terrain coordinate system used
by the terrain data and generation.

---

## 8. Chunk Spatial Boundary

A Chunk represents a finite region of Terrain Local Space. Through the Terrain
root Transform, this region is placed into World Space.

The Chunk boundary is a spatial management boundary, not necessarily a visible
boundary in the Terrain Mesh.

Terrain generated inside adjacent Chunks must be able to form a continuous
surface across Chunk boundaries.

A Chunk boundary must not cause an artificial geometric step or block-like
appearance.

---

## 9. DensityField

DensityField represents the current geometric state of terrain within a
spatial region.

DensityField is a Surface Generation Representation and a runtime snapshot,
not the authoritative Mutable Terrain State.

DensityField is responsible for:

* Storing density values
* Providing density queries
* Providing the spatial information necessary to interpret its samples

DensityField is not responsible for:

* World generation rules
* Mining logic
* Item yield
* Combat
* Terrain Mesh generation
* Storing persistent player edits

World Generation creates the initial Base DensityField.

Under the new Voxel Mining architecture, player Mining must not directly modify
DensityField. Mining writes to `DensityTerrain.VoxelField`, and affected Chunks
derive a new Effective DensityField from:

```text
Base Terrain + VoxelField Edit Layer
```

Legacy V1 code that modified DensityField directly through
`TerrainModification` / `TerrainMining` is frozen legacy behavior.

Terrain Mesh generation consumes DensityField data.

Conceptual flow:

```text
World Generation
       ↓
Base DensityField (immutable runtime snapshot)
       ↓
       +
VoxelField Edit Layer
       ↓
Effective DensityField (derived)
       ↓
Terrain Mesh
```

and after an edit:

```text
VoxelField edit
       ↓
Identify affected Chunk(s)
       ↓
Rebuild Effective DensityField from Base + VoxelField
       ↓
Terrain Mesh Rebuild
```

### 9.1 Current Density sampling configuration (Scheme A migration target)

The current DensityField generator uses one scalar `resolution` that is applied
to all three axes. Per-axis Density resolution is explicitly out of scope for
this migration; `resolution` is not a per-axis vector.

For the approved Scheme A target (`ChunkVoxelSize = (75, 37, 50)`,
`VoxelSize = 1m`):

```text
resolution             = 75     (scalar, shared by X, Y, Z)
intervals per axis     = 75
samples per axis       = 76     (SampleCount = Resolution + 1)
DensityField size      = 76 × 76 × 76 samples per Chunk
Density sample spacing = X: 1.0m, Y: 37/75m, Z: 50/75m
```

Density sample spacing is therefore not equal to `VoxelSize` on every axis.
`VoxelSize`, `ChunkVoxelSize` and Density `resolution` remain three separate
concepts (§6), and a Density sample is never a Voxel. Sample spacing is a
surface-generation parameter; it does not change the Voxel Edit Cell contract,
and it does not by itself guarantee that a Voxel edit produces a visible Mesh
change.

---

## 10. MaterialField

MaterialField represents the material state associated with terrain spatial
data.

Examples include:

```text
Air
Grass
Dirt
Stone
```

The current material set is intentionally limited and may be expanded later.

MaterialField is independent from DensityField but must remain spatially
aligned with the terrain data it describes.

MaterialField is not responsible for:

* Item generation
* Mining yield
* Crafting
* World generation orchestration
* Terrain Mesh ownership

DensityField and MaterialField together provide the spatial information
required by Terrain Mesh generation.

---

## 11. Terrain Mesh

Terrain Mesh is a visual representation generated from terrain spatial data.

The Mesh is not the authoritative World state.

The authoritative terrain state is:

```text
Base Terrain (World Seed + Generation Parameters)
+
DensityTerrain.VoxelField (only Mutable Edit State)
```

The following are derived runtime data:

```text
Base DensityField snapshot
Effective DensityField snapshot
MaterialField snapshot
TerrainChunk.Mesh
MeshFilter.sharedMesh
MeshCollider.sharedMesh
```

The Mesh may be deleted and regenerated without changing the underlying World
state.

The preferred spatial convention is:

```text
Chunk-local Mesh
        ↓ Chunk GameObject Transform
Terrain Local Space
        ↓ Terrain root Transform
World Space
```

Terrain Mesh vertices must be expressed relative to the Chunk's local origin
rather than using Terrain Local or World coordinates directly. The Terrain root
Transform must not be baked into Mesh vertices.

This allows:

* Independent Chunk positioning
* Correct parent Transform behavior
* Independent Mesh rebuilding
* Clear Chunk spatial boundaries

---

## 12. TerrainMeshGenerator

TerrainMeshGenerator converts terrain spatial data into a visual Mesh.

Its conceptual input is:

```text
DensityField
MaterialField
```

Its conceptual output is:

```text
Terrain Mesh
```

TerrainMeshGenerator must not own:

* World generation
* Mining gameplay
* Item yield
* Entity behavior
* Inventory
* Crafting

The Mesh generator may use algorithms such as Marching Cubes to convert density
data into a continuous terrain surface.

The specific meshing algorithm is an implementation detail.

---

## 13. World Generation and Chunk

World Generation defines the rules used to create the initial World state.

Chunk must not define World Generation rules.

World Generation may generate data for a Chunk, but generation results should
be determined by World-level parameters and a stable terrain coordinate system
(Terrain Local Space in the current implementation) rather than by arbitrary
Chunk-local state.

The same World generated with different Chunk partitioning should produce the
same terrain state at corresponding positions, assuming identical generation
parameters. Known current exceptions are listed in Deferred Decisions / Known
Limitations.

Therefore:

```text
World Generation
        ↓
Terrain spatial data
(Terrain Local Space)
        ↓
Chunk partition
        ↓
Chunk data
        ↓
Mesh
```

Chunk partitioning must not fundamentally change the generated World.

Chunk may request or receive the portion of World Generation data belonging to
its spatial boundary.

---

## 14. Terrain Modification

Terrain Modification represents operations that change the spatial state of
the terrain.

Examples include:

* Structured mining
* Freeform mining
* Explosion
* Magic
* Future environmental processes

Terrain Modification must operate on authoritative Mutable Terrain State rather
than directly editing the generated Mesh or Density samples.

New Voxel Mining flow:

```text
Operation
    ↓
DensityTerrain.VoxelField edit
    ↓
Identify affected Chunk(s)
    ↓
Rebuild Effective Density (Base + VoxelField)
    ↓
Rebuild Chunk Mesh
    ↓
Sync MeshFilter + MeshCollider
```

Legacy V1 flow that modified DensityField / MaterialField directly is frozen.

Different modification methods may coexist in future, but they must all write
through the authoritative Edit State layer.

---

## 15. Structured Terrain Modification

Structured modification operates using logical spatial units.

For example, a future structured mining operation may select a Voxel or a
defined group of Voxels.

The exact mining gameplay rules are outside the responsibility of Chunk.

Chunk only provides the spatial data and modification boundary required by the
operation.

Structured modification does not require the final Terrain Mesh to look
block-based.

---

## 16. Freeform Terrain Modification

Freeform modification operates on a spatial region defined by parameters such
as:

* Center
* Direction
* Shape
* Size
* Depth

For example, a mining tool may determine its direction from the Terrain
surface normal:

```text
MiningDirection = -SurfaceNormal
```

Possible shapes include:

* Sphere
* Cylinder
* Capsule
* Cone
* Other future shapes

Freeform and structured modification must be able to operate on the same
underlying terrain spatial state.

Neither method should directly edit the Mesh.

---

## 17. Mining Yield

Mining Yield is separate from terrain modification.

The general conceptual flow is:

```text
Mining Operation
      ↓
Affected Spatial Region
      ↓
Terrain Material / Volume Sampling
      ↓
Yield Calculation
      ↓
Item / Resource
```

Chunk and DensityField must not determine gameplay item yield.

The Mining system may query Chunk spatial data to calculate the affected
region and available material.

---

## 18. Dirty and Mesh Rebuild

When the authoritative VoxelField changes, the affected Chunk(s) must be
marked dirty and rebuilt.

Conceptually:

```text
DensityTerrain.VoxelField edit
        ↓
Identify affected Chunk(s)
        ↓
MarkDirty()
        ↓
Rebuild Effective Density (Base + VoxelField)
        ↓
Rebuild Mesh
        ↓
Sync MeshFilter + MeshCollider
```

A Voxel edit near a Chunk boundary may affect neighboring Chunks.

Therefore multiple Chunks may become dirty from a single Voxel edit.

The Voxel edit itself remains one Terrain-global record in VoxelField. Chunks
must not copy Voxel state into Chunk-local data.

Mesh rebuilding must use the current authoritative state (Base + VoxelField)
rather than invoking World Generation again or mutating the Base Density.

This distinction is important:

```text
Initial Generation
    → Generate Base Terrain

Mesh Rebuild
    → Rebuild visual representation from Base + VoxelField
```

A Mesh rebuild must not accidentally regenerate and overwrite previously
modified terrain.

---

## 19. Entity and Chunk

Entity belongs to the World rather than being owned by a Chunk.

An Entity has a World Position. World Position is converted into Terrain Local
Space through the Terrain root Transform before Chunk lookup.

The spatial system may determine which Chunk contains the Entity for purposes
such as:

* Spatial lookup
* Streaming
* Collision-related organization
* Terrain queries
* Future optimization

Conceptually:

```text
Entity
  ↓
World Position
  ↓
Spatial Query
  ↓
Containing Chunk
```

Chunk is therefore a spatial index/management relationship, not an ownership
relationship.

Destroying or unloading a Chunk must not imply destroying the Entity merely
because the Entity is spatially located inside that Chunk.

---

## 19.4 HiddenFaceContribution 移除与 Dirty（当前实现）

Hidden Face 的最终几何以 `ChunkGeometry.HiddenFaceContributions` 形式由 **MeshOwner**
（可能是相邻 Chunk，≠ EvaluationOwner）持有。因此：

```text
每当一个 SpatialFace 被重新评估并移除某个 Chunk 的旧 Contribution，
该 Chunk 必须被标记为 Dirty 并重新 Assemble（MeshFilter / MeshCollider 同步）。
```

B3-D 材料边界语义补充：Hidden Face 只在**未挖空（Solid）一侧确有材料**的 Region 生成
（见 `Specifications/VoxelAwareMesh.md` §13.2 / §13.10）。因此“挖到地表以上”的 Voxel
不生成盖板，也不产生需要移除的 Contribution；但恢复 Solid 时仍必须移除同一 Face 上
其他 Region 生成的 Contribution，并 Dirty 对应 Chunk。

B3-C 验证覆盖：mine → restore 后所有 Chunk 的顶点/三角形数量必须精确回到基线，
且 `HiddenFaceContributions` 计数归零（StageB2-T3 / StageB3C-T6 断言）。
## 20. Chunk Lifecycle

A Chunk may conceptually move through the following lifecycle:

```text
Created
   ↓
Spatial Data Available
   ↓
Mesh Built
   ↓
Active
   ↓
Dirty
   ↓
Mesh Rebuilt
   ↓
Active
```

Future systems may introduce:

* Loading
* Unloading
* Streaming
* Persistence
* Pooling

These are not required in the current implementation.

---

## 21. Current V2 Implementation Scope

The current implementation establishes the foundations required for future
terrain modification and multi-Chunk management.

The current implementation focuses on:

* Clear Chunk spatial boundaries
* Clear coordinate semantics
* Separation between Voxel and Density Sample
* `DensityTerrain.VoxelField` as terrain-global authoritative Mutable Edit State
* Base Density as immutable runtime snapshot
* Chunk-local Mesh semantics
* Terrain Local Space as the authoritative terrain data space
* World-generation independence from Chunk partitioning
* A minimal Dirty / Rebuild path

The current implementation does not require:

* Mining Yield
* Streaming
* Persistence
* Multithreaded generation
* GPU meshing
* LOD
* ECS
* Dynamic multi-resolution Voxel systems
* Advanced material blending
* Ore systems
* Biome systems
* Water systems
* Full cave gameplay systems

---

## 22. Deferred Decisions / Known Limitations

The following decisions remain intentionally open:

* Final runtime ChunkWorldSize configuration beyond the approved Scheme A
  migration target (VoxelSize=1m, ChunkVoxelSize=(75, 37, 50))
* Final DensityField sampling resolution beyond the approved Scheme A target
  (`resolution = 75`)
* Density representation
* Material representation
* Chunk streaming strategy
* Persistence strategy
* LOD strategy
* Multi-resolution terrain
* Mesh optimization
* Material blending
* World-generation implementation architecture

Resolved decisions for the new Voxel Terrain architecture:

* `VoxelSize = 1.0m`
* `ChunkVoxelSize = (75, 37, 50)` for the approved Scheme A migration
  (`CoordinateMigration.md`); each value is a Voxel count per Chunk axis
* Density `resolution` is a scalar shared by all three axes; the Scheme A target
  is `resolution = 75` (76 samples per axis)
* Negative-coordinate rules use floor / positive-modulo semantics
* Terrain Local Space and Chunk-local Mesh conventions are unchanged by the
  migration

Current prototype configuration (legacy / frozen, not the new Voxel Terrain
contract):

* Legacy `DensityTerrain.voxelSize` (default `0.1`; the current SampleScene
  serializes `0.18`)
* Legacy `ChunkVoxelSize` derived from legacy chunk/terrain size (the current
  SampleScene serializes `(415, 203, 276)`)
* A total terrain extent of `83 × 20.3 × 55.2` was split into a `2 × 1 × 2`
  Chunk grid for testing
* Legacy Chunk sizes and legacy Voxel units must not be used for new Voxel Edit
  Cell or Chunk coordinate calculations

The runtime must be migrated from the legacy configuration to the approved
Scheme A contract (`VoxelSize=1m / ChunkVoxelSize=(75, 37, 50)`, see
`CoordinateMigration.md`) before the Step 3D Voxel → Mesh rebuild path is
enabled with the new Voxel unit.

Known limitations of the current implementation:

* Independent per-Chunk Marching Cubes does not guarantee identical boundary
  vertices. The current multi-Chunk validation therefore uses a small
  continuity tolerance; exact seam stitching / Transvoxel is deferred.
* Material V2 currently computes slope and gravity deposition only from the
  current Chunk's DensityField. Material output may therefore differ across
  Chunk boundaries. Cross-Chunk Material V2 consistency is deferred.
* Marking and rebuilding multiple Chunks is supported, but automatic neighbor
  Dirty propagation is not implemented. The caller identifies affected Chunks.
* Negative-coordinate floor / positive-modulo semantics are now part of the
  Voxel coordinate contract; the legacy V1 Density path may still carry its own
  frozen behavior until cleanup.

These decisions and limitations should be addressed when the corresponding
implementation requirements become concrete.

---

## 23. Core Principles

The following principles should be preserved when implementing or modifying
Chunk-related systems:

1. **World is not made of blocks.**
2. **Voxel is a logical spatial division, not the physical nature of the World.**
3. **Voxel and Density Sample are different concepts.**
4. **Chunk is a spatial management unit, not the World itself.**
5. **Chunk size is an implementation parameter, not an architectural law.**
6. **DensityField is a Surface Generation Representation / derived snapshot, not the authoritative Mutable Terrain State.**
7. **MaterialField represents terrain material state.**
8. **Mesh is a representation of spatial state, not the authoritative state.**
9. **New Terrain Modification writes to `DensityTerrain.VoxelField`, not to DensityField or Mesh directly.**
10. **Mesh rebuilding must not regenerate and overwrite modified World state.**
11. **World Generation should not fundamentally depend on Chunk partitioning.**
12. **Entity and Chunk have a spatial relationship, not an ownership relationship.**
13. **Future systems should extend these boundaries rather than bypass them.**
14. **Terrain Local Space is the authoritative terrain data and Chunk coordinate space; World Space is reached through the Terrain root Transform.**
15. **Terrain Mesh uses Chunk-local Space; the Chunk GameObject Transform places it into Terrain Local Space and World Space.**
16. **The new Voxel Terrain contract uses VoxelSize=1m and ChunkVoxelSize=(75, 37, 50) (approved Scheme A migration, `CoordinateMigration.md`); legacy prototype values are frozen.**
17. **ChunkCoordinate is absolute; generated-range selection does not change absolute coordinate semantics.**
18. **VoxelField is Terrain-global; Chunks never own or copy Voxel state.**
19. **Base Terrain + VoxelField Edit Layer -> Effective Terrain -> Density -> Mesh -> Collider.**
