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

The value is an implementation parameter rather than a fundamental property
of the World.

`VoxelSize` is not fixed to `1`. Any positive implementation value may be used.
The value may be changed without changing the DensityField sampling resolution
or the visual resolution of the Terrain Mesh.

Final numerical values remain deferred until the corresponding implementation
requirements become concrete. The temporary values used by the current
prototype are listed in Deferred Decisions / Known Limitations.

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

These intervals are expressed in Terrain Local Space. The exact
negative-coordinate behavior is not finalized yet.

---

## 6. Chunk Size

Chunk is a finite spatial partition of Space.

Chunk size is an implementation parameter and is not a fundamental architectural
requirement.

The commonly used `16 × 16 × 16` configuration is only a reference value.
It is not required for this project.

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

For a uniform cubic Chunk, this relationship applies independently to X, Y,
and Z.

The actual numerical Chunk size remains an implementation decision. Current
prototype values are temporary and are not architectural constants.

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

The exact behavior for negative coordinates is deferred until the coordinate
system is expanded to formally support negative Terrain Local Space.

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

DensityField is responsible for:

* Storing density values
* Providing density queries
* Allowing density values to be modified
* Providing the spatial information necessary to interpret its samples

DensityField is not responsible for:

* World generation rules
* Mining logic
* Item yield
* Combat
* Terrain Mesh generation

World Generation may create the initial DensityField.

Terrain Modification may modify an existing DensityField.

Terrain Mesh generation consumes DensityField data.

Conceptual flow:

```text
World Generation
       ↓
DensityField
       ↓
Terrain Mesh
```

and later:

```text
Spatial Modification
       ↓
DensityField
       ↓
Terrain Mesh Rebuild
```

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

The authoritative terrain state is represented by spatial data such as:

```text
DensityField
MaterialField
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

Terrain Modification must operate on spatial data rather than directly
editing the generated Mesh.

General flow:

```text
Operation
    ↓
Spatial Modification
    ↓
DensityField / MaterialField
    ↓
Affected Chunk(s)
    ↓
Dirty
    ↓
Mesh Rebuild
```

Different modification methods may coexist.

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

When terrain spatial data changes, the affected Chunk must be marked dirty.

Conceptually:

```text
Modify Spatial Data
        ↓
Identify Affected Chunk(s)
        ↓
MarkDirty()
        ↓
Rebuild Mesh
```

A terrain modification near a Chunk boundary may affect neighboring Chunks.

Therefore multiple Chunks may become dirty from a single operation.

Mesh rebuilding must use the current Chunk spatial data rather than invoking
World Generation again.

This distinction is important:

```text
Initial Generation
    → Generate spatial state

Mesh Rebuild
    → Rebuild visual representation from current spatial state
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
* DensityField / MaterialField data ownership
* Chunk-local Mesh semantics
* Terrain Local Space as the authoritative terrain data space
* World-generation independence from Chunk partitioning
* A minimal Dirty / Rebuild path

The current implementation does not require:

* Mining
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

* Final VoxelSize
* Final ChunkVoxelSize
* Final ChunkWorldSize
* DensityField sampling resolution
* Density representation
* Material representation
* Negative-coordinate rules
* Chunk streaming strategy
* Persistence strategy
* LOD strategy
* Multi-resolution terrain
* Mesh optimization
* Material blending
* World-generation implementation architecture

Current prototype configuration (temporary, not a final design):

* `VoxelSize = 0.1`
* `ChunkVoxelSize = (415, 203, 276)`
* A total terrain extent of `83 × 20.3 × 55.2` is split into a `2 × 1 × 2`
  Chunk grid for testing.

These values may be replaced when final Chunk sizing is decided.

Known limitations of the current implementation:

* Independent per-Chunk Marching Cubes does not guarantee identical boundary
  vertices. The current multi-Chunk validation therefore uses a small
  continuity tolerance; exact seam stitching / Transvoxel is deferred.
* Material V2 currently computes slope and gravity deposition only from the
  current Chunk's DensityField. Material output may therefore differ across
  Chunk boundaries. Cross-Chunk Material V2 consistency is deferred.
* Marking and rebuilding multiple Chunks is supported, but automatic neighbor
  Dirty propagation is not implemented. The caller identifies affected Chunks.
* Negative-coordinate behavior is a temporary floor / positive-modulo
  implementation, not a finalized rule.

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
6. **DensityField represents terrain geometry state.**
7. **MaterialField represents terrain material state.**
8. **Mesh is a representation of spatial state, not the authoritative state.**
9. **Terrain modification changes spatial data, not Mesh directly.**
10. **Mesh rebuilding must not regenerate and overwrite modified World state.**
11. **World Generation should not fundamentally depend on Chunk partitioning.**
12. **Entity and Chunk have a spatial relationship, not an ownership relationship.**
13. **Future systems should extend these boundaries rather than bypass them.**
14. **Terrain Local Space is the authoritative terrain data and Chunk coordinate space; World Space is reached through the Terrain root Transform.**
15. **Terrain Mesh uses Chunk-local Space; the Chunk GameObject Transform places it into Terrain Local Space and World Space.**
16. **VoxelSize and ChunkVoxelSize are configurable implementation parameters; current prototype values are temporary, not architectural constants.**
17. **ChunkCoordinate is absolute; generated-range selection does not change absolute coordinate semantics.**
