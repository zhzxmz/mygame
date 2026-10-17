# Mining Specification

## 1. Document Purpose

This document defines the current Mining system.

The goal is to provide predictable, grid-aligned terrain excavation while preserving the continuous Mesh-based nature of the terrain.

The current Mining system intentionally does not support arbitrary freeform terrain carving.

---

# 2. Mining Design Goal

The desired behavior is:

> Mining should behave similarly to Minecraft in terms of the logical excavation unit, while the world itself remains a continuous Marching Cubes Mesh rather than a block Mesh.

Therefore:

```text
Mining Unit
    = discrete Edit Cell

World Surface
    = continuous Mesh
```

The system should provide:

* predictable excavation
* grid alignment
* stable data representation
* persistent terrain edits
* continuous Mesh output
* Chunk-independent edits

---

# 3. Edit Cell

The fundamental Mining unit is one Edit Cell.

Default:

```text
EditCellSize = 1.0m
```

Therefore each mining operation targets:

```text
1m × 1m × 1m
```

in Terrain Local Space.

The Edit Cell is identified by:

```text
Vector3Int VoxelCoordinate
```

Example:

```text
(3, 5, -2)
```

represents:

```text
x ∈ [3, 4)
y ∈ [5, 6)
z ∈ [-2, -1)
```

when:

```text
EditCellSize = 1
```

---

# 4. Cell Selection

Mining begins with a Physics Raycast from the player's camera.

Conceptual flow:

```text
Camera
  ↓
Raycast
  ↓
Terrain Collider Hit
  ↓
Hit Point in World Space
  ↓
World → Terrain Local
  ↓
VoxelField.PositionToVoxel() / TerrainVoxelSelector
  ↓
Selected Voxel
```

The selected Edit Cell is determined entirely by the Edit Grid coordinate contract.

The Mining system must not determine the cell using:

* nearest Mesh vertex
* nearest Mesh triangle center
* triangle normal
* Mesh bounds
* Density sample index
* Chunk coordinate
* arbitrary rounding
* surface normal offset

---

# 5. Hit Normal

The current Mining system does not use the hit normal to determine the Edit Cell.

The normal may be retained by the Player Mining Controller for future gameplay features, but it is not part of the current cell-selection contract.

Therefore:

```text
Selected Cell
    = function(Hit Point)
```

not:

```text
Selected Cell
    = function(Hit Point, Hit Normal)
```

---

# 6. Mining Preview

Before committing an excavation, the player should be able to determine which Edit Cell will be excavated.

Preview and Commit MUST use the same cell-selection logic.

Conceptually:

```text
Preview:
Raycast
  ↓
World → Local
  ↓
PositionToVoxel()
  ↓
Show selected Voxel

Commit:
Use the same SelectedVoxel state
  ↓
DensityTerrain.VoxelField.Set(voxel, Air)
```

There must not be separate cell-selection algorithms for Preview and Commit.

---

# 7. Excavation Commit

When mining is committed, the only Mutable Terrain State written is:

```text
DensityTerrain.VoxelField
```

```text
Selected Voxel
      ↓
Excavate Operation
      ↓
VoxelField.Set(selectedVoxel, VoxelState.Air)
```

The edit identifies exactly one Edit Cell.

Example:

```text
Operation:
    Excavate

VoxelCoordinate:
    (10, 4, -3)
```

The legacy `TerrainEditRecord` / `TerrainEditLayer.Add()` path is frozen and
must not be used by new Mining.

---

# 8. Repeated Mining

If the selected Edit Cell has already been excavated:

```text
Excavate Cell
```

must be idempotent.

Repeated excavation of the same cell must not create additional terrain removal.

The preferred logical state is:

```text
Cell = Excavated
```

rather than:

```text
Cell = Excavated 5 times
```

`VoxelField.Set(voxel, Air)` is idempotent under sparse storage: setting an
already-Air Voxel to Air produces the same state and does not add extra
records.

---

# 9. Adjacent Mining

Adjacent cells can be excavated independently.

Example:

```text
[X][X][ ]
[X][X][ ]
[ ][ ][ ]
```

The four `X` cells represent four logical excavation edits.

The Effective Terrain must treat them as a combined excavated region.

The surface generation system is responsible for producing the resulting continuous Mesh.

There must be no requirement that each cell produces an isolated cube-shaped hole.

---

# 10. Cross-Chunk Mining

An Edit Cell may cross a Chunk boundary.

The Edit Cell remains one logical edit.

Example:

```text
          Chunk A       Chunk B
        ┌──────────┬──────────┐
        │          │          │
        │     ┌────┼────┐     │
        │     │ Edit Cell   │ │
        │     └────┼────┘     │
        │          │          │
        └──────────┴──────────┘
```

The Voxel edit is stored exactly once in the Terrain-global
`DensityTerrain.VoxelField`.

All surface Chunks affected by the edit must be rebuilt as necessary.

Chunk boundaries must never alter the logical size or coordinate of the Edit Cell.

---

# 11. Terrain State Result

An excavated Edit Cell represents:

```text
The terrain inside this logical cell has been excavated.
```

It does NOT mean:

```text
Delete a cube GameObject
```

and does NOT mean:

```text
Delete Mesh triangles
```

and does NOT mean:

```text
Set arbitrary Density samples to Air
```

`VoxelField` records the gameplay-level fact as an explicit Air Voxel.

The Effective Terrain and Surface Generation systems convert that fact into the resulting surface.

---

# 12. Mesh Result

The terrain remains a continuous Mesh after excavation.

The expected conceptual pipeline is:

```text
Excavate Cell
      ↓
Effective Terrain changes
      ↓
Surface Representation is recalculated
      ↓
Marching Cubes
      ↓
Continuous Mesh
```

The Mesh is allowed to have:

* vertical surfaces
* sloped surfaces
* irregular boundaries
* continuous surfaces between adjacent excavated cells

The visible result does not need to look like a collection of cube Meshes.

---

# 13. Density Interaction

Mining must not directly manipulate Density samples as its authoritative data model.

The following approach is prohibited as the primary Mining representation:

```text
Raycast
  ↓
Find Density samples
  ↓
Set Density = -1
  ↓
Save modified Density
```

Instead:

```text
Raycast
  ↓
Select Voxel
  ↓
VoxelField.Set(voxel, Air)
  ↓
Effective Terrain (Base Terrain + VoxelField)
  ↓
Surface Sampling
  ↓
Effective Density
  ↓
Marching Cubes
```

The exact Surface Sampling implementation is defined separately from Mining.

---

# 14. Material / Resource Drop

Mining has two conceptual results:

```text
Terrain Modification
+
Resource Yield
```

Terrain Modification:

```text
Selected Cell → Excavated
```

Resource Yield:

```text
Excavated Cell
    ↓
Determine material/resource
    ↓
Generate corresponding drop
```

The current version should keep resource calculation simple.

The Mining system must not yet attempt detailed continuous material-volume simulation inside a cell.

For the current implementation, the terrain material associated with the excavated cell may determine the corresponding resource drop.

Example:

```text
Stone
    ↓
Stone Resource

Iron Ore
    ↓
Iron Ore Resource

Dirt
    ↓
Dirt / Soil Resource
```

Exact drop quantities and material classification are separate gameplay specifications.

---

# 15. Material Reduction

Mining should reduce the available terrain material/resource in the world.

It should not require the entire terrain material system to become a voxel simulation.

The current goal is:

> Excavating one Edit Cell removes approximately one Edit Cell's worth of terrain material and produces the corresponding resource yield.

Detailed fractional material distribution is not part of the current Mining version.

---

# 16. Mining State

The logical state of a cell can be considered:

```text
Not Excavated
```

or:

```text
Excavated
```

The Effective Terrain Query should answer whether a given Terrain Local position belongs to an excavated Edit Cell.

Conceptually:

```text
IsExcavated(localPosition)
```

performs:

```text
localPosition
    ↓
VoxelField.PositionToVoxel()
    ↓
VoxelCoordinate
    ↓
VoxelField.IsAir(VoxelCoordinate)
    ↓
Excavated?
```

---

# 17. Preview Contract

The Preview must represent the exact Edit Cell that Commit would modify.

The preview should therefore be based on:

```text
Voxel Min
Voxel Max
```

from:

```text
VoxelField / TerrainVoxelHighlight coordinate contract
```

The preview must not be generated from:

* Mesh triangle bounds
* Mesh normals
* Density sample spacing
* nearest Mesh vertex
* arbitrary brush geometry

---

# 18. Mining and Chunk Rebuild

After a successful new excavation:

```text
DensityTerrain.VoxelField
      ↓
Determine affected terrain surface region
      ↓
Mark affected Chunks dirty
      ↓
Rebuild Effective Density (Base + VoxelField)
      ↓
Rebuild Chunk Mesh
      ↓
Update MeshFilter
      ↓
Update MeshCollider
```

The exact affected-region calculation belongs to the terrain surface/rebuild system.

Mining itself should only communicate:

```text
A Voxel was excavated.
```

It should not contain Marching Cubes-specific logic.

---

# 19. Mining Idempotency

The following operation:

```text
Excavate(10, 4, -3)
```

performed multiple times must produce the same Effective Terrain state as performing it once.

This property is required for:

* repeated clicks
* network synchronization
* save/load
* replay
* debugging

---

# 20. Save / Load

Mining state should be reconstructable from sparse excavation state.

Conceptually:

```text
World Seed
+
Generation Parameters / Version
+
Explicit Air Voxel Coordinates
```

is sufficient to reconstruct the modified terrain.

The Mesh itself is not authoritative save data.

Density arrays are not authoritative save data.

MeshCollider data is not authoritative save data.

Future Edit Operations may upgrade the saved edit set to Operation Records, but
the persistent model remains Base World + Edit State -> Effective World.

---

# 21. Current Scope

### Supported

* Raycast-based Voxel selection
* Terrain Local Space
* VoxelSize = 1m Edit Cells
* Integer Voxel coordinates
* Excavation (`VoxelField.Set(voxel, Air)`)
* Mining Preview / Highlight using the same SelectedVoxel
* Adjacent excavation
* Cross-Chunk excavation
* Continuous Mesh result
* Idempotent excavation
* `DensityTerrain.VoxelField` as the only Mining write target

### Not Supported

* Arbitrary freeform carving
* Spherical brushes
* Capsule brushes
* Cone brushes
* Plane cutting
* Surface-normal carving
* Continuous sculpting
* Arbitrary Mesh deformation
* Direct Mesh editing
* Density-sample-based persistent mining
* Fractional material-volume simulation
* Fill / Build / Replace Material
* Material painting
* Mining Tool / Mining Speed / long-press mining
* Item drop / Inventory / Ore placement

---

# 22. Architectural Invariants

### Rule 1

Mining selects an Edit Cell, not a Mesh triangle.

### Rule 2

Edit Cell selection occurs in Terrain Local Space.

### Rule 3

Edit Cell coordinates use the VoxelField coordinate contract
(`floor(TerrainLocalPosition / VoxelSize)`, VoxelSize = 1m).

### Rule 4

The Edit Cell is the current fundamental Mining unit.

### Rule 5

The Edit Cell is not a Mesh cube.

### Rule 6

Mining does not directly modify Mesh data.

### Rule 7

Mining does not directly store modified Density samples.

### Rule 8

Preview and Commit use identical cell-selection logic.

### Rule 9

Repeated excavation of the same cell is idempotent.

### Rule 10

Chunk boundaries do not define Mining boundaries.

### Rule 11

Mining writes logical Voxel edits to `DensityTerrain.VoxelField`; surface
generation produces the Mesh result.

### Rule 12

`VoxelField` is the only Mutable Terrain Edit State; Base Density, Effective
Density, Mesh, and MeshCollider are derived data.

### Rule 13

The current Mining system intentionally prioritizes predictable representation and system stability over arbitrary freeform excavation.

---

# 23. Current Implementation Target

The immediate target is a Mining system that feels approximately like:

```text
Minecraft-style excavation
+
Continuous Marching Cubes terrain
```

More precisely:

```text
Player points at terrain
        ↓
One logical 1m³ Edit Cell is selected
        ↓
Player mines
        ↓
That cell becomes Excavated
        ↓
The terrain surface is regenerated
        ↓
The visible world remains a continuous Mesh
        ↓
The corresponding terrain/resource amount is reduced
```

The system should first make this stable, predictable, persistent, and performant.

Freeform mining is explicitly deferred until the core terrain architecture is mature enough to support it without compromising the rest of the game.
