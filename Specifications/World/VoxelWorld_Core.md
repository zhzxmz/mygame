\# Voxel World Core Specification V1





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



