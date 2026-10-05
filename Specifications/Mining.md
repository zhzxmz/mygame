\# Mining Specification V1



\## 1. Purpose



Mining is a system that allows gameplay actions to modify the existing Terrain.



Mining does not directly define the Terrain geometry. It requests a spatial modification of Terrain data.



The Terrain data remains authoritative in `DensityField` and `MaterialField`.



The first version focuses only on:



\* selecting a mining region;

\* removing Terrain inside that region;

\* updating `DensityField`;

\* updating `MaterialField`;

\* marking affected Chunks dirty;

\* rebuilding affected Chunk Meshes.



Mining does \*\*not\*\* handle Item Yield, Inventory, Item Drops, or Pickup in V1.



\---



\## 2. Core Principle



Mining is a form of Terrain Modification.



The basic flow is:



```text

Mining

&#x20;   ↓

Mining Brush

&#x20;   ↓

Terrain Modification

&#x20;   ↓

DensityField / MaterialField

&#x20;   ↓

Affected Chunks

&#x20;   ↓

Dirty

&#x20;   ↓

Mesh Rebuild

```



Mining does not directly modify Mesh data.



Mesh is a derived representation of Terrain data.



\---



\## 3. Terrain Data Authority



`DensityField` represents the current geometric state of the Terrain.



`MaterialField` represents the current material state of the Terrain.



After World Generation:



```text

World Generation

&#x20;   ↓

DensityField + MaterialField

```



During gameplay:



```text

Mining / Terrain Modification

&#x20;   ↓

DensityField + MaterialField

```



Therefore, Mining must modify Terrain data rather than treating the generated Mesh as the source of truth.



The resulting Mesh is regenerated from the modified Terrain data.



\---



\## 4. Density Modification



For Mining V1, Terrain inside the selected Mining Brush is removed.



The basic operation is:



```text

Density → Air

```



A Density sample affected by the Mining Brush is assigned a value representing Air according to the Terrain's existing Density / IsoLevel convention.



Mining does not introduce a new Density representation.



Mining also does not modify Density outside the selected spatial region.



\---



\## 5. Material Modification



When Terrain is removed by Mining:



```text

Material → Air

```



The corresponding `MaterialField` region is changed to `MaterialType.Air`.



This keeps Density and Material semantically consistent:



```text

Density = Air

Material = Air

```



Mining V1 does not calculate which material was removed.



It does not calculate Stone, Dirt, Ore, or other Item Yield.



\---



\## 6. Mining Brush



Mining V1 uses a spatial Mining Brush.



The Brush describes the region that will be removed from Terrain.



The first Brush shape is:



```text

Cube

```



The Brush is conceptually equivalent to placing a cube-shaped volume into the Terrain and removing Terrain inside that volume.



The Brush has at minimum:



```text

Position

Size

```



The Brush is defined in Terrain Local Space.



The Brush does not represent a Voxel.



A Voxel is a spatial coordinate / partition concept.



A Mining Brush is a gameplay operation.



Therefore:



```text

Voxel ≠ Mining Brush

```



A Brush may contain one Voxel, multiple Voxels, or a fraction of the logical Voxel space.



\---



\## 7. Brush and Voxel Relationship



Mining V1 does not require the Brush to have the same size as a Voxel.



For example:



```text

VoxelSize = 0.1

Mining Brush = 1 × 1 × 1

```



is valid.



The Brush operates on the continuous Terrain representation by determining which Density samples fall inside its volume.



Voxel coordinates may be used to locate the relevant spatial region, but the final Terrain modification is performed on the DensityField.



\---



\## 8. Terrain Local Space



Mining uses Terrain Local Space as its authoritative operation space.



The existing coordinate architecture remains authoritative:



```text

World Space

&#x20;   ↓

Terrain Root Transform

&#x20;   ↓

Terrain Local Space

&#x20;   ↓

Mining Brush

```



A future player interaction system may convert a World-space hit position into Terrain Local Space before invoking Mining.



Mining itself does not depend on the Terrain Root being at identity Transform.



\---



\## 9. Selecting the Mining Position



Mining V1 separates \*\*where the Brush is placed\*\* from \*\*how the player selects that position\*\*.



The Terrain modification system only requires a valid Brush position.



V1 may use a directly specified/debug Brush position.



Player-facing selection is deferred.



A future selection flow may use:



```text

Camera

&#x20;   ↓

Ray

&#x20;   ↓

Terrain Surface

&#x20;   ↓

Hit Position / Surface Normal

&#x20;   ↓

Mining Brush Position

```



The Surface Normal is considered a possible placement aid rather than a fundamental part of the Mining data model.



Mining V1 does not require a final decision about the best player-facing targeting method.



\---



\## 10. Mining Direction



Mining direction is not itself the Terrain modification.



Direction is only one possible mechanism for determining Brush placement or orientation.



V1 may use a fixed axis-aligned Cube Brush.



Future versions may support:



\* surface-normal placement;

\* camera-directed mining;

\* directional excavation;

\* rotated Brushes;

\* other spatial selection methods.



These are interaction and Brush-placement concerns and should not redefine the underlying Terrain Modification system.



\---



\## 11. Chunk Interaction



A Mining Brush may intersect multiple Chunks.



Mining must therefore determine all affected Chunks.



Conceptually:



```text

Mining Brush Bounds

&#x20;       ↓

Find intersecting Chunks

&#x20;       ↓

Modify each Chunk's local Terrain data

```



Each Chunk only modifies the portion of the Brush that intersects its own spatial region.



For example:



```text

&#x20;       Mining Brush

&#x20;   ┌─────────────────┐

&#x20;   │                 │

────┼─────────────────┼──── Chunk boundary

&#x20;   │                 │

&#x20;   └─────────────────┘

&#x20;     Chunk A | Chunk B

```



Chunk A modifies its own affected region.



Chunk B modifies its own affected region.



The Mining operation is therefore not limited to a single Chunk.



\---



\## 12. Chunk Dirty State



After modifying Terrain data, every affected Chunk must be marked Dirty.



```text

Terrain Modification

&#x20;       ↓

Affected Chunk

&#x20;       ↓

MarkDirty()

```



The Mesh must not be treated as the modified data.



After modification:



```text

DensityField / MaterialField

&#x20;       ↓

Chunk Dirty

&#x20;       ↓

RebuildDirtyChunks()

&#x20;       ↓

New Mesh

```



The existing Chunk V2 Dirty / Rebuild mechanism should be reused.



Mining does not introduce a second Mesh rebuild mechanism.



\---



\## 13. Mesh and Collider Relationship



The Terrain Mesh is a derived representation of the Terrain data.



A future Terrain Collider should also be derived from the same Terrain data.



Conceptually:



```text

DensityField

&#x20;    │

&#x20;    ├──→ Terrain Mesh

&#x20;    │

&#x20;    └──→ Terrain Collider

```



Therefore, when Mining changes Terrain:



```text

Density → Air

&#x20;   ↓

Mesh → Air

&#x20;   ↓

Collider → Air

```



The player should eventually be able to move through the removed region.



Collider generation is not required for the first Mining implementation if the Terrain currently has no Collider.



The Collider system should be added later without changing the Mining data model.



\---



\## 14. Mining V1 Operation



The minimum valid Mining operation is:



```text

1\. Receive a Cube Brush.

2\. Determine its Terrain Local Space bounds.

3\. Find all affected Chunks.

4\. Determine the Density samples affected by the Brush.

5\. Set affected Density samples to Air.

6\. Set corresponding Material samples to Air.

7\. Mark affected Chunks Dirty.

8\. Rebuild affected Chunk Meshes.

```



No Item or Inventory operation is required.



\---



\## 15. Density Sample Selection



Mining operates on the existing DensityField sampling structure.



The Brush is continuous, while DensityField contains discrete samples.



Therefore the implementation must define whether a Density sample is affected by testing its Terrain Local Space position against the Brush volume.



For a Cube Brush, the basic V1 rule is:



```text

Density Sample Position ∈ Brush Bounds

&#x20;   → Density becomes Air

```



Samples outside the Brush remain unchanged.



The exact boundary inclusion rule may be finalized during implementation if required.



\---



\## 16. Mesh Result



After a successful Mining operation:



```text

Before:



████████████████

████████████████

████████████████



Mining Cube:



&#x20;     ┌──────┐

&#x20;     │      │

&#x20;     │      │

&#x20;     └──────┘



After:



████████████████

██████    ██████

██████    ██████

```



The exact surface shape is determined by the existing Density-to-Mesh algorithm.



Mining does not directly construct the resulting Mesh.



\---



\## 17. V1 Scope



\### Included



\* Cube Mining Brush.

\* Terrain Local Space Brush position.

\* Brush bounds.

\* Density modification.

\* Material modification.

\* Multi-Chunk modification.

\* Chunk Dirty state.

\* Chunk Mesh rebuild.

\* Debug / direct Brush placement.

\* Existing DensityField / MaterialField architecture.



\### Not Included



\* Item Yield.

\* Item Drops.

\* Inventory.

\* Pickup.

\* Ore calculation.

\* Material volume calculation.

\* Mining tools.

\* Mining speed.

\* Mining damage.

\* Player animation.

\* Camera interaction.

\* Physics Raycast targeting.

\* Terrain Collider implementation.

\* Rotated Brushes.

\* Sphere / Capsule / arbitrary Brushes.

\* Streaming.

\* Persistence.

\* Multiplayer synchronization.



\---



\## 18. Future Extension



The intended future structure is:



```text

&#x20;               Mining

&#x20;                 │

&#x20;                 ↓

&#x20;           Mining Brush

&#x20;                 │

&#x20;       ┌─────────┴─────────┐

&#x20;       ↓                   ↓

&#x20;  Cube Brush          Future Brushes

&#x20;                           │

&#x20;                Sphere / Capsule / etc.

&#x20;       └─────────┬─────────┘

&#x20;                 ↓

&#x20;         Terrain Modification

&#x20;                 ↓

&#x20;      DensityField + MaterialField

&#x20;                 ↓

&#x20;         Affected Chunks

&#x20;                 ↓

&#x20;            Dirty / Rebuild

```



Item-related processing is intentionally separated:



```text

Mining

&#x20; ↓

Terrain Modification



Mining Result / Removed Material

&#x20; ↓

Future Yield System

&#x20; ↓

Item / Inventory

```

Player Mining V1

1. Player Mining is an input/targeting layer.
2. It does not own terrain modification logic.
3. Camera performs a Physics Raycast.
4. The Raycast targets Terrain MeshCollider.
5. RaycastHit.point is converted from World Space to Terrain Local Space.
6. The resulting local position is passed to TerrainMining.
7. V1 uses the existing axis-aligned Cube Brush.
8. Brush size is fixed/configurable for debugging.
9. Player Mining does not implement:
   - mining speed
   - tools
   - animation
   - item drops
   - inventory
   - yield calculation
   - new brush shapes
10. Raycast targeting and terrain modification remain separate responsibilities.

Player Input
    ↓
Player Mining
    ↓
Camera Raycast
    ↓
Terrain MeshCollider
    ↓
World Hit Point
    ↓
Terrain Local Point
    ↓
TerrainMining.MineCube(...)
    ↓
Density / Material
    ↓
Dirty
    ↓
Rebuild
    ↓
Mesh + Collider

Mining V1 should therefore remain independent from Inventory and Item systems.



