# Project Architecture

## 1. Core Rules

1. 系统保持明确职责和边界。
2. 优先设计可扩展的规则，而不是为具体内容编写特殊逻辑。
3. 数据、状态、规则、系统和表现应保持合理分离。
4. 新功能优先扩展已有系统，而不是复制已有逻辑。
5. Agent 修改代码前必须阅读相关架构、契约与 Specification，并确认当前实现状态。
6. Agent 不得为了完成任务擅自破坏已批准的架构或 Specification；发现冲突时先报告，不自行裁决。
7. 修改必须通过与改动范围相匹配的编译、测试或验证；未运行的验证不得声称通过。
8. 当前需求优先，不为尚未确定的未来功能过度实现。
9. 对架构级、跨系统或长期影响的未知问题，Agent 必须向用户询问。
10. Architecture 与 Specification 可以随项目实践修订；修改时必须同步维护文档状态，避免把提案、已实现接口和完整系统混为一谈。

## 2. System Map

System Map 描述游戏的主要系统领域、核心概念与边界，不规定所有系统立即实现，也不替代各系统的详细契约。

```text
Game World
├── World
│   ├── World Identity / Seed / Generation / Persistence
│   ├── World Skeleton → Region → Biome / Geology / Voxel Evaluation
│   └── World History / Events
├── Space
│   ├── Coordinates / Spatial Queries
│   ├── Chunk / Streaming
│   └── Terrain / Voxel Data / Mesh
├── Entity
│   ├── Player
│   ├── NPC / Creature / Enemy
│   └── State / Traits / Needs / Body / Behaviour
├── Item
├── Material
├── Production
│   ├── Crafting / Forging / Smelting
│   ├── Farming / Cooking / Construction
│   └── Other transformation processes
├── Knowledge
│   ├── Skills / Technology / Recipes
│   └── Magic Knowledge / Discoveries / Mastery
└── Life / Soul
    └── Future domain; implement only when current scope requires it
```

### 2.1 World

World represents the world as a whole: identity, seed, generation rules, world-level facts, history and persistence. It coordinates the world-generation pipeline but does not own every subsystem's detailed rules.

### 2.2 Space

Space describes where things are and what spatial information can be queried there. Chunk is a spatial partition and local sampling/cache unit, not an authority that decides world-generation rules. Terrain geometry and Mesh are representations derived from world/voxel data; Mesh is not persistent world truth.

### 2.3 Entity and Player

Entity represents an object with identity, state and spatial position. Player is a specialized entity with player-facing systems such as inventory, equipment, interaction and progression. Entity behaviour must not be embedded in Chunk ownership.

### 2.4 Item, Material and Production

Item represents something that can be held, used, equipped, consumed, crafted or supplied to another system. Material describes composition and properties. Production describes processes that transform inputs into outputs. These concepts are related but must not be collapsed into one type or system.

### 2.5 Knowledge and Life / Soul

Knowledge represents learned capabilities and discoveries, including skills, technology, recipes and magic knowledge. Life / Soul is a long-term architecture domain; its presence in this map does not mean a complete implementation is currently required.

## 3. Architecture and Specification Authority

- This file is the **project-wide overview**: core rules, system domains and high-level boundaries only.
- World-generation phase order, dependencies and lifecycle are defined by `Specifications/World/WorldGeneration_Pipeline_Contract.md`.
- WorldSkeleton V1 query semantics and implementation status are defined by `Specifications/World/WorldSkeleton_V1_Minimal_Contract.md`; continent/mask degeneration rules remain owned by `Specifications/World/WorldState_Design.md`.
- Terrain editing and voxel-aware mesh details belong in terrain-specific specifications, including `Specifications/World/Terrain_Editing_Architecture.md` and the existing voxel/mesh contracts.
- Chunk and coordinate details belong to their corresponding specifications, not this project-wide overview.
- If two documents disagree, do not silently choose one. Identify the conflicting authority and request a decision or update the authoritative document first.

## 4. Scope Rule

The System Map describes domains and intended boundaries, not a commitment to implement every listed feature now. Add or expand implementation and detailed specifications only when required by the current development milestone.
