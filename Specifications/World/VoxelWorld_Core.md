\# Voxel World Core Specification V1

> **系统适用范围**：本文档只约束 `Assets/Scripts/VoxelWorld/` 下的离散 VoxelWorld 体系，包括 `VoxelChunk`、`VoxelType` 及其配套生成、运行时和网格流程。它不是整个游戏所有地形系统的全局定义。
>
> 仓库另有独立的 `DensityTerrain` 体系，使用 `DensityField` / `MaterialField` 与 Marching Cubes；其架构入口是 `Specifications/World/Terrain_Editing_Architecture.md`。两套系统目前是并存的不同实现，不得仅凭本文档推断 DensityTerrain 必须改为固定尺寸方块体素，也不得反向用 DensityTerrain 规则改写本体系的 `VoxelType` / `VoxelChunk` 契约。若任务要统一两套系统，必须先提出并批准专门的迁移方案。






\## 1. 世界真值



Voxel Data 是世界唯一真值。



Mesh:

\- 不是世界状态

\- 不参与逻辑判断

\- 可以随时删除并重新生成





\## 2. Voxel



Voxel 是世界最小空间单位。



Voxel:



Position:

(integer x,y,z)



Size:

1 x 1 x 1 world unit





Voxel State:



Air

Grass

Dirt

Stone





Rule:



Air:

\- empty

\- no collision





Non-Air:

\- solid

\- collision





\## 3. Chunk





Chunk contains voxel data.



Default:



ChunkSize = 16x16x16





Chunk coordinate:



chunkX = floor(x / 16)



localX = x - chunkX\*16





\## 4. Mesh generation





Mesh is generated from voxel data.



For every solid voxel:



Check six neighbors:



Air:

&#x20;   create face



Solid:

&#x20;   no face





The generated mesh must never become world truth.





\## 5. Modification





Voxel modification:



Change voxel data



↓



Mark chunk dirty



↓



Regenerate affected chunk mesh





No geometry mutation.





\## 6. Save





Save:



seed

\+

changed voxel data





Do not save mesh.



