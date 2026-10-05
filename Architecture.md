\## 1.Core Rules

1. 系统保持明确职责和边界。
2. 优先设计可扩展的规则，而不是为具体内容编写特殊逻辑。
3. 数据、状态、规则、系统和表现应保持合理分离。
4. 新功能优先扩展已有系统，而不是复制已有逻辑。
5. Agent 修改代码前必须理解相关架构和 Specification。
6. Agent 不得为了完成任务擅自破坏已有架构或 Specification。
7. 修改必须通过适当的编译和测试验证。
8. 当前需求优先，不为尚未确定的未来功能进行过度实现。
9. 对于架构级或长期影响的未知问题，Agent 必须询问用户。
10. Architecture 和 Specification 可以随着项目实践进行修订。

\## 2. System Map

1\. Purpose



System Map 描述游戏的主要系统领域、核心概念以及它们之间的关系。



System Map 只用于确定项目的整体结构和系统边界，不描述具体代码实现、类结构或详细规则。



具体系统的实现规则由对应的 Specification 定义。



2\. Overall Structure

Game World

│

├── World

│

├── Space

│   └── Chunk

│       ├── Terrain

│       ├── Material

│       └── Structures

│

├── Entity

│

├── Item

│

├── Production

│

├── Knowledge

│

├── Life / Soul

│

└── Player



这些领域共同构成游戏运行所需要的主要结构。



3\. World



World 表示游戏世界整体。



World 包含世界中的空间、实体、规则、状态以及其他世界级数据和关系。



World 负责描述：



世界身份

世界 Seed

世界规则

世界状态

世界生成

世界持久化

世界之间的关系



World 是游戏世界的整体概念，而不是单独负责所有具体系统的实现。



4\. Space



Space 描述世界中的空间结构。



Space 负责回答：



某个位置在哪里，以及该位置周围有什么空间数据。



Space 可以包含：



世界坐标

空间分区

Chunk

Terrain

Density

Material

空间查询



Space 是 World 的组成部分。



Entity 通过自身的空间位置与 Space 建立关系。



5\. Chunk



Chunk 是 Space 的空间分区和局部管理单位。



Chunk 用于将规模可能非常大的世界空间划分为有限大小的区域。



Chunk 可以管理局部空间数据，例如：



Terrain / Density

Material

Terrain Modification

Structures

其他需要按空间区域组织的数据



Chunk 不等于 Terrain。



Chunk 也不负责 Entity 自身的行为逻辑。



Chunk 的具体规则由 Chunk Specification 定义。



6\. Entity



Entity 表示存在于 World 中、具有自身状态并能够在空间中定位的对象。



例如：



Player

NPC

Creature

Enemy

Other interactive objects



Entity 可以具有：



Identity

State

Position

Traits

Needs

Body

Soul

Behaviour



Entity 与 Space 是独立概念。



Entity 通过 Position 与 Space 建立空间关系。



Entity 不属于 Chunk 的所有权体系，但可以根据 Position 确定其所在的 Chunk。



7\. Item



Item 表示游戏中可以被持有、使用、装备、消耗、加工或作为其他系统输入的物品。



可能包括：



Weapons

Armor

Tools

Food

Seeds

Medicine

Magic Items

Building Items

Knowledge-related Items

Other special items



Item 不应被限制为单一用途。



具体 Item 规则由 Item Specification 定义。



8\. Material



Material 描述构成物品、地形或其他世界对象的材料性质。



Material 可以具有不同性质，例如：



Physical Properties

Thermal Properties

Elemental Properties

Soul-related Properties

Other Properties



Material 与 Item 是不同概念。



Material 描述“由什么构成以及具有什么性质”。



Item 描述“作为游戏对象存在并可以被系统使用的东西”。



9\. Production



Production 描述通过过程改变、制造或产生物品及其他世界对象的系统。



包括但不限于：



Crafting

Forging

Smelting

Cooking

Farming

Construction

Alchemy

Other production processes



Production 应尽可能通过通用规则支持不同材料、配方、工具、条件和结果。



10\. Knowledge



Knowledge 描述角色通过探索、学习、实践和发现获得的知识与能力。



可能包括：



Skills

Technologies

Recipes

Magic Knowledge

Discoveries

Mastery

Other learned knowledge



Knowledge 是角色成长的重要组成部分。



角色成长不应仅依赖传统等级和属性增长。



11\. Life / Soul



Life / Soul 描述生命、肉身和灵魂相关系统。



未来可能包括：



Body

Life

Death

Soul

Soul State

Soul Attachment

Soul Fragments

Holy

Unholy

Rebirth

Reincarnation



该领域目前主要作为未来架构领域存在。



当前阶段不实现完整 Life / Soul 系统。



12\. Player



Player 是一种特殊的 Entity。



Player 除了具有 Entity 的基本能力之外，还具有玩家专属的系统，例如：



Character Creation

Traits

Inventory

Equipment

Knowledge

Progression

Player-specific interaction



Player 不应与 Entity 完全割裂。



Player 应建立在 Entity 的基础结构之上，并通过玩家专属系统扩展。



13\. Major Relationships

World / Space / Entity

World

├── Space

│   └── Chunk

│

└── Entity

&#x20;     └── Position → Space



World 是整体。



Space 描述空间。



Entity 描述存在于世界中的对象。



Entity 通过 Position 与 Space 建立关系。



Space / Chunk / Terrain

Space

└── Chunk

&#x20;    └── Spatial Data

&#x20;         ├── Terrain / Density

&#x20;         ├── Material

&#x20;         └── Structures



Chunk 是空间分区，而不是 Terrain 本身。



Entity / Item

Entity

&#x20;  │

&#x20;  ├── Inventory

&#x20;  │      └── Item

&#x20;  │

&#x20;  └── Equipment

&#x20;         └── Item



Item 可以被 Entity 持有、装备、使用或消耗。



Material / Item / Production

Material

&#x20;  │

&#x20;  ↓

Production

&#x20;  │

&#x20;  ↓

Item



Production 可以使用不同 Material，通过不同过程产生 Item。



Entity / Knowledge

Entity

&#x20;  │

&#x20;  ↓

Knowledge

&#x20;  │

&#x20;  ├── Skills

&#x20;  ├── Technology

&#x20;  ├── Recipes

&#x20;  └── Magic Knowledge



Knowledge 可以改变 Entity 可以执行的行为、使用的系统以及成长方式。



14\. Current Development Scope



当前项目处于早期开发阶段。



当前主要实现：



Enemy

&#x20; ↓

Death

&#x20; ↓

Drop

&#x20; ↓

Pickup

&#x20; ↓

Inventory

&#x20; ↓

Crafting

&#x20; ↓

Iron Sword



同时建立：



World

Space

Chunk

Terrain

Entity

Item

Material



等基础架构。



以下系统目前只确定其长期位置，不进行完整实现：



Civilization

Advanced Magic

Soul

Reincarnation

Multiple Worlds

Advanced Farming

Advanced Building

Advanced Technology

Complex World Simulation

15\. Scope Rule



System Map 描述系统领域，而不是要求所有系统立即实现。



系统可以提前出现在 System Map 中，但只有在当前开发阶段需要时才建立对应 Specification 和实现。



随着项目开发，System Map 可以被修改和扩展。

\## 3. Architecture Decisions



\### 3.1 World / Space / Entity



World 是游戏世界的整体。



Space 是 World 中用于描述空间的领域。



Entity 是存在于 World 中并具有自身状态的对象。



Entity 通过 Position 与 Space 建立空间关系。



Entity 不属于 Chunk 的所有权体系。



Chunk 与 Entity 主要通过空间索引产生关系。

\### 3.2 World / Voxel / Terrain



世界本身不是由方块构成的。



世界生成可以产生自然、连续的 Terrain 与 Geological Structure。



Voxel 是用于离散化空间、定位空间区域以及执行局部空间操作的逻辑空间单位。



Voxel 不代表 Terrain Mesh 必须呈现为方块结构。



Chunk 是 Space 的空间分区单位。



因此：



World

→ Natural World State



Space

→ Spatial Representation



Voxel

→ Discrete Spatial Unit



Chunk

→ Spatial Partition / Management Unit

\### 3.3 DensityField



DensityField 是 Terrain 空间状态的一种表示。



DensityField 不属于 World Generation Logic，也不属于 Mining Logic。



World Generation 可以产生 Density 数据。



Terrain Modification 可以修改 Density 数据。



Terrain Mesh 根据 Density 数据生成。



因此：



World Generation

&#x20;       ↓

DensityField

&#x20;       ↓

Terrain Mesh



Mining / Other Modification

&#x20;       ↓

DensityField

&#x20;       ↓

Terrain Mesh

\### 3.4 Terrain Modification



Terrain 的修改不直接操作 Mesh。



不同类型的 Terrain Modification，包括：



\- Structured Mining

\- Freeform Mining

\- Explosion

\- Magic

\- Other Operations



最终统一表现为空间状态修改。



```text

Operation

&#x20;   ↓

Spatial Modification

&#x20;   ↓

Terrain Spatial Data

&#x20;   ↓

Chunk

&#x20;   ↓

Mesh Rebuild

