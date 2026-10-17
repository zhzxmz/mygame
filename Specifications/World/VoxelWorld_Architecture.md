\# Voxel World Architecture Specification V1





\## 1. Core Principle



The voxel data is the single source of truth.



Voxel data represents the actual world state.



Mesh is only a rendering representation generated from voxel data.



No gameplay system may use mesh as world state.





\## 2. World Structure



World consists of:



World

&#x20;|

&#x20;+-- Chunk

&#x20;      |

&#x20;      +-- Voxel Data





Each Chunk contains a fixed voxel grid.





\## 3. Chunk Rules



Chunk size:



16 x 16 x 16 voxels





Each voxel occupies:



1 x 1 x 1 world unit.





Chunk coordinates are integer coordinates.





Chunk coordinate:



worldPosition = chunkCoordinate \* ChunkSize





\## 4. Voxel Rules



Voxel is the smallest world unit.



Initial voxel types:



\- Air

\- Grass

\- Dirt

\- Stone





Air represents empty space.



Any non-Air voxel is solid.





\## 5. Coordinate Rules



World voxel coordinates use integer coordinates.



Example:



Voxel(10,5,20)



represents the voxel occupying:



x:10\~11

y:5\~6

z:20\~21





Chunk conversion:



chunkX = floor(worldX / ChunkSize)





local voxel coordinate range:



0\~15





\## 6. Modification Rules



All world changes modify voxel data first.



Example:



Mining:



VoxelType.Stone

&#x20;       |

&#x20;       v

VoxelType.Air





After modification:



Affected chunk becomes dirty.



Dirty chunk regenerates mesh.





\## 7. Mesh Rules



Mesh is generated from voxel data.



Mesh does not store gameplay information.



Mesh generation must not modify voxel data.





\## 8. Future Extension



The system should support:



\- terrain generation

\- caves

\- biomes

\- minerals

\- structures

\- player building

\- voxel destruction

