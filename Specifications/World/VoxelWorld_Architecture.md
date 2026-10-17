# Voxel World Architecture

> **状态：历史概念文档，已由当前地形架构与地形契约取代。**
>
> 本文档此前描述的是固定尺寸、离散 VoxelType 网格模型。当前地形实现采用 Density / Material 数据并派生 Mesh，因此本文中的固定 `16 × 16 × 16` Chunk、`Air/Grass/Dirt/Stone` 离散实心判定等内容**不得作为当前实现或 coding agent 的开发依据**。
>
> 当前权威入口：
> - 地形与挖掘的高层架构：`Specifications/World/Terrain_Editing_Architecture.md`
> - Voxel / Density / Material / Mesh 的具体实现契约：应以仓库中对应的当前地形契约及实现代码为准。
> - 世界生成阶段顺序：`Specifications/World/WorldGeneration_Pipeline_Contract.md`

## 1. Still-valid general principles

1. 世界数据是游戏状态的权威来源；Mesh 是从世界数据派生的表现，不是独立的世界状态真相。
2. 游戏规则不应仅通过渲染 Mesh 推断世界状态。
3. 修改世界数据后，应明确识别受影响的数据区域，并按既定更新流程重建相关表现。
4. 生成 Mesh 的过程不得反向修改其输入世界数据。

## 2. Superseded assumptions

以下内容是旧模型的假设，现已不再作为当前架构约束：

- 每个 Chunk 必须固定为 `16 × 16 × 16`。
- 每个 Voxel 必须是 `1 × 1 × 1` 的离散实体格子。
- 世界实体性仅由 `Air` 与非 `Air` 的离散类型决定。
- `Grass / Dirt / Stone` 必须是完整且固定的 Voxel 类型集合。

这些参数和表示方式只能由当前地形契约与实现明确决定，不能从本历史文档继承。

## 3. Maintenance rule

若本文与 `Terrain_Editing_Architecture.md`、当前地形契约或实际实现冲突，以当前地形契约和经确认的实现为准；发现权威文档之间存在冲突时，应先记录冲突并修正文档，不得自行混用两套地形模型。
