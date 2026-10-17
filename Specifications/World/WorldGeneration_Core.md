\# World Generation Core Specification V1

> **系统适用范围**：本文档约束 `Assets/Scripts/VoxelWorld/Generation/` 的离散 VoxelWorld 生成契约（`Seed + ChunkCoordinate → VoxelChunk`），不是全局 World Generation Pipeline，也不定义 DensityTerrain 的生成方式。
>
> 仓库另有以 `DensityField` / `MaterialField` 和 Marching Cubes 为核心的 `DensityTerrain` 体系，其架构入口为 `Specifications/World/Terrain_Editing_Architecture.md`。两套体系目前并存；任何统一或迁移都必须明确指定目标系统、数据权威和兼容方案，不能将本文件的固定 `16³` Chunk 与四种 `VoxelType` 默认套用到 DensityTerrain。






\## 1. Purpose



World Generation is responsible for creating the initial voxel state.



Generation output:



World

&#x20;└── Chunk

&#x20;      └── Voxel





Generation creates the initial world state.



After generation:



\- VoxelWorld is the only world truth.

\- Mesh is generated separately.

\- Runtime modification uses voxel editing, not regeneration.





\---



\# 2. Generation Determinism





World generation must be deterministic.



Same:



\- Seed

\- World coordinate

\- Generation parameters





must always produce the same voxel result.





Example:



Seed = 12345



Chunk(0,0,0)



must always generate identical voxels.





Random generation must never depend on:



\- execution order

\- chunk loading order

\- frame timing





Generation is coordinate-based.





\---



\# 3. Generation Input





WorldGenerator input:





\## Seed



A world seed controls all generated terrain.





\## Chunk Coordinate



Generation receives:



ChunkCoordinate(x,y,z)





The generator does not assume generation order.





\## Generation Settings



Parameters must be explicit.



Examples:



\- world height

\- terrain frequency

\- terrain amplitude

\- soil depth





No hidden global state.





\---



\# 4. Basic Terrain Model V1





The first generation version uses a height based terrain model.





For each world column:



(x,z)





calculate:





TerrainHeight = HeightFunction(x,z,Seed)





Then assign voxel states.





Rules:





Above terrain:



Air





Surface layer:



Grass





Below surface:



Dirt





Deep underground:



Stone







Example:





Air



Air



Grass



Dirt



Dirt



Stone



Stone



Stone





\---



\# 5. Coordinate Rules





Generation uses world voxel coordinates.





Voxel coordinate:



(integer x,y,z)





Chunk generation:





Chunk coordinate



↓



World voxel coordinate



↓



Set voxel value







Generator must not depend on Unity Transform.





\---



\# 6. Chunk Independence





A Chunk can be generated independently.





GenerateChunk(chunkCoordinate)





must produce the same result regardless of:



\- neighboring chunks loaded or unloaded

\- generation order





However:



Future generation passes may perform world-scale operations.



Examples:



\- caves

\- rivers

\- structures





These are separate generation stages.





\---



\# 7. Generation Pipeline





Future world generation follows stages:





Stage 1:



Base Terrain



Creates:



\- Air

\- Grass

\- Dirt

\- Stone







Stage 2:



Caves



Modify voxel state.



Example:



Stone → Air







Stage 3:



Biomes



Modify terrain rules.







Stage 4:



Structures



Create:



\- buildings

\- ruins

\- special locations







Stage 5:



Resources



Create:



\- minerals

\- ores

\- rare materials







Each stage receives voxel data from previous stage.





\---



\# 8. Current Version Scope





V1 only implements:





Input:



Seed



Chunk Coordinate





Output:



VoxelChunk





Supported voxel types:



Air

Grass

Dirt

Stone





Only implement:



Height based terrain.





Do not implement:



\- caves

\- minerals

\- structures

\- biome system

\- vegetation

\- mesh generation





\---



\# 9. World Truth Rule





Generated voxel data becomes the initial world state.





After generation:



Generation does not overwrite player modifications.





Runtime changes are stored separately from generation logic.





\---



\# 10. Future Compatibility





The generation system must allow:



\- infinite world expansion

\- chunk streaming

\- different biome rules

\- cave generation

\- resource generation

\- structure generation





without changing VoxelCore.

