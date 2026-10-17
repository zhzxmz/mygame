# World Generation Geology Implementation Specification V1

> **文档性质**：本文档是 `WorldGeneration_Geology.md`（地质**规则**层）的**接入层实现规范** ——
> 把"世界规则"落到一个可插拔的 `IWorldGenerator` 实现上，且**不替换既有实现、不修改任何既有核心代码**。
>
> **本轮只写规范，不写代码**（明确不做：`TerrainGenerator` 本体、采样器实现、验证入口实现）。
> 下一阶段按 §13 的清单与顺序落地。
>
> 上游规范：
> * `Specifications/World/WorldGeneration_Geology.md` —— 地质规则（**规则来源**；本文档只做接入细化）；
> * `Specifications/World/WorldGeneration_Core.md` —— 生成契约（Seed + ChunkCoordinate → VoxelChunk、确定性、Chunk 可独立计算、stage 划分）；
> * `Specifications/World/VoxelWorld_Core.md` —— 世界真值、`VoxelType` 集合（冻结）、Chunk 数据契约与坐标换算；
> * `Specifications/World/VoxelWorld_Runtime_Interface.md` —— `IWorldGenerator` 抽象（本文档的实现必须只实现它、不改它）；
> * `Specifications/World/VoxelWorld_Runtime_Core.md` / `_Implementation.md` —— 调用方契约（生成 → 校验 → 安装，§5.1/§5.2）；
> * `Specifications/ArchitectureDecisionWorldSpaceEntity.txt`（ADR-001）—— Voxel / Entity 边界。
>
> 冲突时的优先级：真值 > 生成契约 > 地质规则 > 调度 > 抽象 > **本文档（接入细化）**。
> 本文档**只新增**：新增类型、新增文件、新增验证；不得要求修改任何既有文件。

---

## 1. 目标与范围

### 1.1 目标

**新增一个地质生成实现，不替换旧实现。**

```
既有：BasicTerrainGenerator : IWorldGenerator      （Stage 1，保持不变、继续可用、仍是默认）
新增：GeologyWorldGenerator : IWorldGenerator      （Stage 1 规则 + Cave + Deposit/Biome 接口）
```

两者**同时存在**、可互换（同一个 `IWorldGenerator` 抽象），由组装点在构造处显式选择：

```csharp
// 组装点（游戏层）—— 唯一允许 new 具体实现的位置（Runtime_Interface §2.4）
IWorldGenerator generator =
    useGeology
        ? new GeologyWorldGenerator(stage1Settings, geologySettings, biomeProvider)  // 新增
        : new BasicTerrainGenerator(stage1Settings);                                  // 既有，零改动
```

* 关闭地质特性（Cave/Deposit/Biome 全关）时，`GeologyWorldGenerator` 的输出必须与
  `BasicTerrainGenerator` **逐体素一致**（GIV9）—— 证明它是"叠加层"而不是"另一套地形"；
* Runtime / Streaming / Mesh / View / VoxelCore / BasicTerrainGenerator / IWorldGenerator
  **一律零修改**（§1.3）。

### 1.2 本轮范围（明确不做）

| 本轮做 | 本轮不做 |
|---|---|
| 定义接入层的结构、类型、文件、不变量、验证设计（GIV1–GIV9） | **实现** `GeologyWorldGenerator` / `GeologySampler` / 噪声 / 验证入口 |
| 定义参数结构与校验规则 | 实现 `TerrainGenerator` 本体（用户明确要求先不做） |
| 定义接口（Deposit / Biome）的形态与边界 | 实现任何 Biome、任何植被、任何 Entity |
| 定义结构扫描与零修改纪律 | 修改 Bootstrap 以接入新生成器（属后续独立步骤，§13.3） |

### 1.3 禁止修改清单（硬性）

| 禁止修改 | 理由 |
|---|---|
| `VoxelCore`（`VoxelType` / `Voxel` / `VoxelCoordinate` / `VoxelChunk` / `VoxelWorld` / `VoxelWorldDebug`） | 世界真值与坐标契约冻结；`VoxelType` 集合不得新增（地质规则 §4.2） |
| `BasicTerrainGenerator` | Stage 1 行为是既有世界的基准；本实现必须与它可比对（GIV9） |
| `IWorldGenerator` | `Runtime_Interface.md` §4：**不改名、不重复定义、不改签名**（本文档的实现只能实现它） |
| `WorldGenerationSettings` | 其字段集是 Stage 1 的契约；地质参数走**新增**参数结构（§9） |
| `HeightFunction` | 既有确定性整数哈希噪声；地质噪声**新增**独立实现，不改它 |
| Runtime Core（`Runtime/*.cs`） | 调度与安装流程与地质无关；Core 只认识 `IWorldGenerator` |
| Streaming（`Runtime/Streaming/*.cs`） | 加载/卸载判定与地质无关 |
| Mesh（`VoxelMeshData` / `VoxelMeshGenerator`） | 几何由体素派生；生成阶段不得触碰几何 |
| View（`VoxelChunkView` / `UnityMeshBuilder` / `ChunkViewPresenter`） | 表现层与地质无关 |
| Bootstrap（`VoxelWorldStreamingBootstrap`） | 接入属后续步骤；本轮不碰 |

### 1.4 交付物（下一阶段创建的文件清单）

| 文件（新增） | 类型 | 命名空间 |
|---|---|---|
| `Assets/Scripts/VoxelWorld/Generation/Geology/GeologySettings.cs` | `GeologySettings`（显式参数 + `IsValid`） | `VoxelWorld.Generation.Geology` |
| `Assets/Scripts/VoxelWorld/Generation/Geology/GeologyNoise.cs` | 整数哈希值噪声（确定性；不改 `HeightFunction`） | `VoxelWorld.Generation.Geology` |
| `Assets/Scripts/VoxelWorld/Generation/Geology/GeologySample.cs` | 采样结果 + 命中规则枚举（可解释性） | `VoxelWorld.Generation.Geology` |
| `Assets/Scripts/VoxelWorld/Generation/Geology/GeologySampler.cs` | 世界坐标纯函数采样（Density/Material/Cave/Deposit/Biome） | `VoxelWorld.Generation.Geology` |
| `Assets/Scripts/VoxelWorld/Generation/Geology/IDepositProvider.cs` | 矿藏查询接口（只读） | `VoxelWorld.Generation.Geology` |
| `Assets/Scripts/VoxelWorld/Generation/Geology/IBiomeProvider.cs` | 群系接口（只读、只接口） | `VoxelWorld.Generation.Geology` |
| `Assets/Scripts/VoxelWorld/Generation/Geology/GeologyWorldGenerator.cs` | `IWorldGenerator` 实现（接入层） | `VoxelWorld.Generation.Geology` |
| `Assets/Editor/GeologyWorldGenerationValidation.cs` | 批处理验证（GIV1–GIV9 + 既有回归 + SHA） | — |

---

## 2. 结构

### 2.1 分层数据流（本文档的核心）

```
WorldGeneration_Geology.md          ← 规则层（层 / 洞 / 矿藏 / 群系接口；不变量 GE1–GE12）
        ↓
GeologySampler                      ← 世界坐标纯函数采样（无状态、无副作用、无 Unity）
        ↓
Density 判定                        ← P0 WorldBounds > P1 Cave > P2 Terrain > P3 Material
        ↓
Material 判定                       ← Surface/Depth/Deep → Grass / Dirt / Stone
        ↓
VoxelChunk                          ← 按 x → y → z 写入 VoxelCore 的体素数据
```

### 2.2 每层契约

| 层 | 输入 | 输出 | 纯函数性 | 禁止 |
|---|---|---|---|---|
| **GeologySampler** | **世界体素坐标** + `(Seed, GeologySettings, WorldGenerationSettings)` | `CaveDensity` / `DepositZone` / `BiomeId` / `SurfaceHeight` 等标量 | ✅ 同输入恒同输出（GIV1） | 读 Chunk 坐标、读时间/随机、访问世界、写数据 |
| **Density 判定** | 采样标量 | `Empty / Solid` + **命中的规则**（可解释） | ✅ | 引入新的优先级、让属性层（矿藏）改变实体性 |
| **Material 判定** | 采样标量 + `Solid` 结论 | `VoxelType`（仅 `Grass/Dirt/Stone`） | ✅ | 产出 `Air`（地表之上由 Density 回答）、产出未定义枚举值 |
| **VoxelChunk 写入** | `Material` 结论 + 体素局部坐标 | 写入世界拥有的 `VoxelChunk` | 结果与写入顺序无关 | 直接改内部数组、绕过 `SetVoxel`、写 Mesh/Entity/Unity 对象 |

**关键条款**：Density 判定与 Material 判定必须是**单一实现**
（不得存在"生成用一套、调试/可解释用另一套"的两套规则，§11-GIV 的等价性检查会失败）。

### 2.3 与 Stage 1 的关系：叠加，不是替换

```
GeologyWorldGenerator(c) =
      Stage 1 规则（BasicTerrainGenerator.ClassifyVoxel 的等价规则）
    + P1 Cave 覆盖（可关）
    + 属性层（DepositZone / Biome 修正，可关）
```

* Stage 1 的**规则**在本实现中**重新表达**（不得复制粘贴代码，见 §12.3 的"等价而非复用"条款）；
* 特性全关时输出必须与既有实现**逐体素相等**（GIV9）；
* 既有实现不被调用、不被修改、不被删除。

---

## 3. 世界坐标采样（要求 1）

### 3.1 唯一入口

采样只接受**世界体素坐标**：

```csharp
// 唯一允许的采样入口形态（示意）
int  CaveDensityAt(int worldX, int worldY, int worldZ);
int  SurfaceHeightAt(int worldX, int worldZ);
byte DepositZoneAt(int worldX, int worldY, int worldZ);
byte BiomeAt(int worldX, int worldZ);
```

生成器内部**必须**用 VoxelCore 的换算取得世界坐标：

```csharp
VoxelCoordinate world = chunk.LocalToWorldCoordinate(localX, localY, localZ);
```

**禁止**自行实现坐标规则（`Mining.md` §1.1 的同级要求）。

### 3.2 禁令：不得按 Chunk 局部坐标独立生成

* 采样函数的**签名中不得出现 Chunk 坐标**（`VoxelCoordinate chunkCoordinate` / `int chunkX/Y/Z`）；
* 采样函数**不得**接受"局部坐标 + Chunk 坐标"的组合再自行还原世界坐标；
* 采样结果**不得**依赖"当前正在生成哪个 Chunk"。

> 结构性判据（§12.1 扫描）：`GeologySampler.cs` 的公开签名中不出现 Chunk 坐标参数；
> `GeologyWorldGenerator.cs` 是**唯一**同时持有 Chunk 坐标与局部坐标循环的文件。

### 3.3 可验证判据（"世界坐标纯函数"的定义）

| 判据 | 方法 |
|---|---|
| 同一世界坐标在不同 Chunk 窗口中采样结果相同 | 取边界列：`chunk(c).localX=15` 与 `chunk(c+1).localX=0` 的世界坐标相同 ⇒ 采样值必须相同（GIV2） |
| 同一坐标重复采样恒等 | 同一坐标采样 N 次比对（GIV1） |
| 与生成顺序无关 | 交错生成多个 Chunk、乱序生成 ⇒ 结果不变（GIV1） |

---

## 4. Chunk 独立性（要求 2）

1. **相邻 Chunk 共享同一世界函数**：`GeologySampler` 只有**一份**实例参数（Seed + 参数），
   对所有 Chunk 生效；不得存在"每个 Chunk 一套参数/种子"的路径；
2. **生成 `chunk(c)` 不需要邻居**：不查询任何其它 Chunk 的体素数据（`WorldGeneration_Core.md` §6）；
3. **不写跨 Chunk 状态**：采样器不得缓存"上一次生成的 Chunk"并据此影响结果；
   若为性能缓存（例如按列缓存 `SurfaceHeight`），缓存**必须**由世界坐标索引、
   且不得改变任何输出（可关缓存后逐一比对）；
4. **边界连续是结论而非补丁**：由"世界坐标纯函数"自动成立 ⇒ 禁止任何"边界修补"代码
   （例如"若 localX=15 则特殊处理"）。

---

## 5. 生成顺序（要求 3）

1. 写入遍历顺序**固定为 `localX → localY → localZ`**（与 `BasicTerrainGenerator`、
   既有 `WorldGenerationCoreValidation`、`VoxelMeshGenerator` 的循环顺序一致）；
2. 每个体素的写入必须用 `VoxelChunk.SetVoxel(localX, localY, localZ, type)`（VoxelCore 唯一写入口）；
3. **不得并行、不得乱序、不得分帧**（生成是同步、原子的；调度与分帧属 Runtime Core）；
4. **理由**：结果虽与写入顺序无关（§2.2），但固定顺序保证
   ① 生成过程可复现（调试/断点可比较）、② 与既有阶段的一致性、③ 与 `SetVoxel` 参数顺序一致；
5. **可验证性**：写入顺序是**实现纪律**（源码扫描嵌套顺序，§12.1）；
   其"结果正确性"由 GIV1/GIV2 独立保证（不依赖顺序）。

---

## 6. 生成阶段禁令（要求 4）

生成阶段（`GeologyWorldGenerator.Generate` 调用栈内）**禁止**：

| # | 禁止 | 说明 |
|---|---|---|
| **P1** | Mesh / 几何 | 不调用 `VoxelMeshGenerator`、不构造 `VoxelMeshData`、不访问顶点/索引 |
| **P2** | Unity 对象 | 不出现 `UnityEngine` 标识符：GameObject / Transform / Mesh / Material / Collider / Time / Random |
| **P3** | Entity | 不生成晶簇/矿石节点/植被/生物；不产生任何 Entity 记录（ADR-001：Entity 不属于 Chunk 所有权） |
| **P4** | Runtime / Streaming | 不引用 `VoxelRuntimeCore` / `IChunkPresenter` / `ChunkStreamingDriver` / `ChunkRecord`；不请求加载、不触碰脏标记 |
| **P5** | 数据层写入之外的动作 | 不写存档、不写文件、不发事件、不调用玩法代码 |

**可观察结果**：生成阶段只做两件事 —— **读参数、写体素**。

---

## 7. 矿物：本轮只生成 Deposit 数据接口（要求 5）

### 7.1 允许

```csharp
public interface IDepositProvider
{
    // 世界坐标 → 区域 ID（None 表示无资源）；只读、可重复求值、无副作用
    GeologyDepositZone GetDepositZone(int worldX, int worldY, int worldZ);
}
```

* V1 的实现可以是 `GeologySampler` 自身（或内部持有的一个 provider）；
* 查询规则：**只在实体内**返回非 `None`（`Density == Solid`）；对 `Empty` 坐标必须返回 `None`；
* 矿藏**不改变** `Density` 与 `Material`（GIV5）；
* 矿藏信息**不写入 `VoxelChunk`**（体素数据里没有任何"资源标记"）。

### 7.2 禁止（本轮明确不做）

| 禁止 | 说明 |
|---|---|
| 生成 `CrystalCluster`（晶簇） | 它是 **Entity**：有自身状态、通过空间位置与 Space 建立关系、不属于 Chunk 所有权（ADR-001） |
| 生成矿石节点 / 掉落物 | 同上 |
| 新增矿物 `VoxelType`（如 `IronOre`） | V1 的 `VoxelType` 集合冻结为 4 值（地质规则 §4.2） |
| 在体素数据里写"资源标记" | 违反"体素状态只有 4 种"与 GIV5 |
| 让 `DepositZone` 影响几何 | 矿藏不产生面/材质/子网格差异 |

### 7.3 边界（重复自地质规则 §6.3，便于实现者对照）

```
Generation / Voxel ：回答"这里存在什么资源"（DepositZone，只读查询）
Entity / 未来系统   ：决定"是否真的放一个晶簇对象"、拥有它的状态与表现
```

---

## 8. 生物群系：只保留接口（要求 6）

```csharp
public interface IBiomeProvider
{
    GeologyBiome GetBiome(int worldX, int worldZ);                        // 纯函数（Seed + 世界坐标）
    GeologyBiomeProfile GetProfile(GeologyBiome biome);                    // 只读参数表
}

public struct GeologyBiomeProfile
{
    public int   SurfaceMaterial;   // V1：只能是 Grass/Dirt/Stone 之一（对应 VoxelType 的整数取值）
    public int   SoilDepthDelta;    // 对 SoilDepth 的显式修正
    public int   SurfaceHeightDelta;// 对地表高度的显式修正
    public int   Temperature;       // 供未来系统消费（V1 不参与几何/实体生成）
    public int   Humidity;          // 同上
    public int   VegetationRule;    // 只声明"条件"，不生成植被
}
```

要求：

1. **只接口**：V1 不实现任何具体群系；不生成植被、不生成树、不生成任何 Entity；
2. **可选注入**：`GeologyWorldGenerator` 允许 `biomeProvider == null` ⇒ 使用**恒等修正**
   （等价于 Stage 1 行为，GIV9）；
3. **不得改变 Runtime**：群系不得影响加载/卸载、生命周期、脏标记、并发与预算；
4. **纯函数**：`GetBiome(worldX, worldZ)` 只依赖 Seed 与世界坐标；
5. **连续性**：群系过渡必须连续（允许过渡带由显式参数控制），不得在 Chunk 边界跳变；
6. **只通过 P2/P3 生效**：群系只修正地表高度/土壤深度/地表材料，不得改变 P0/P1 的优先级语义。

---

## 9. 参数与校验（显式，无隐藏全局状态）

### 9.1 `GeologySettings`（新增结构）

| 字段 | 含义 | 校验 |
|---|---|---|
| `EnableCaves` | 是否启用 P1 洞穴覆盖 | 布尔（显式开关，便于 GIV4/GIV9 对照） |
| `CaveThreshold` | 洞穴阈值 | 与噪声取值范围一致（显式定义域） |
| `CaveFrequency` | 洞穴噪声频率 | `> 0` |
| `EnableDeposits` | 是否启用矿藏属性 | 布尔（显式开关，便于 GIV5 对照） |
| `DepositFrequency` | 矿藏噪声频率 | `> 0` |
| `DepositRichness` | 矿藏区域大小/稀有度（显式标量） | 显式定义域 |
| `EnableBiomeModifiers` | 是否应用 Biome 修正 | 布尔（`biomeProvider == null` 时必须为 false 或等价恒等） |
| `IsValid(out string reason)` | 启动前校验 | 非法参数 ⇒ **拒绝启动**（`Runtime_Core.md` §3.5） |

### 9.2 参数注入（不改既有契约）

```csharp
public sealed class GeologyWorldGenerator : IWorldGenerator
{
    public GeologyWorldGenerator(
        WorldGenerationSettings stage1Settings,   // 既有类型（不改），显式传入
        GeologySettings geologySettings,          // 新增类型，显式传入
        IBiomeProvider biomeProvider)             // 可为 null（恒等修正）

    public WorldGenerationSettings Settings { get; }   // 契约要求：返回既有类型
    public GeologySettings Geology { get; }            // 新增只读访问（诊断/验证）
}
```

* **`IWorldGenerator.Settings` 的签名不变** ⇒ 既有类型与调用方零改动；
* 地质参数**不得**塞进 `WorldGenerationSettings`（那是 Stage 1 的契约）；
* 构造期不得产生副作用（不生成 Chunk、不访问世界）。

---

## 10. 确定性

1. 继承 `WorldGeneration_Core.md` §2 与地质规则 §8：同 `Seed + 参数 + 世界坐标` ⇒ 逐位一致；
2. 噪声必须**新增自实现整数哈希**（`GeologyNoise`），禁止：
   `UnityEngine.Random` / `System.Random` / `Time` / `DateTime` / `Guid` /
   `Mathf.PerlinNoise` / 依赖 `Dictionary` 遍历序；
3. 标量与判定使用**整数运算**（或显式定点），避免平台相关浮点差异；
   `SurfaceHeight` 等结论必须是整数体素坐标；
4. 采样器**无状态**（除由世界坐标索引的纯缓存，可关且等价）；
5. 生成结果与①生成顺序②已生成的其它 Chunk③调用次数无关（GIV1）。

---

## 11. 验证设计（GIV1–GIV9）

> 落地时由**新增**的 `Assets/Editor/GeologyWorldGenerationValidation.cs` 实现（菜单项
> `Tools/VoxelWorld/Validate Geology Generation` + `-executeMethod`）；
> 纯 C# 部分不依赖场景与 Unity 对象。

### GIV1 — 同 Seed 同坐标结果一致

| 项 | 内容 |
|---|---|
| **定义** | 同 `(Seed, WorldGenerationSettings, GeologySettings, chunkCoordinate)` ⇒ **逐体素完全一致** |
| **方法** | ①两个独立实例生成同一坐标 ⇒ 比较 4096 体素；②同一实例重复生成 ⇒ 比较；③**交错**生成其它 Chunk 后再生成 ⇒ 比较；④逆序生成多个 Chunk ⇒ 每个 Chunk 与首次结果比较；⑤派生 `ComputeContentHash()` 比较 |
| **反例保护** | 断言"差异体素计数 == 0"，同时断言"该 Chunk 非全 Air"（避免空 Chunk 使断言恒真） |
| **对应上游** | `WorldGeneration_Core.md` §2；地质规则 GE1/GE2/GE12 |

### GIV2 — 跨 Chunk 边界一致

| 项 | 内容 |
|---|---|
| **定义** | 同一世界坐标在相邻 Chunk 中采样结果相同：`chunk(c).localX=15` 与 `chunk(c+1).localX=0`（±X/±Y/±Z 六方向，含负坐标）逐体素相等 |
| **方法** | 对给定 Chunk，取 16×16 边界列（每方向 256 列 × 16 体素）逐体素比较两侧 Chunk（既有验证入口已有同类做法） |
| **覆盖** | ①正坐标 ②负坐标 ③`chunkX=-1` 与 `chunkX=0` 的世界原点跨越 |
| **反例保护** | 断言比较的体素数量 > 0 且两侧 Chunk 均非全 Air（至少一侧有实体） |
| **对应上游** | 地质规则 §2.3-F2/F3、GE3；`WorldGeneration_Core.md` §5/§6 |

### GIV3 — 不同 Seed 结果不同

| 项 | 内容 |
|---|---|
| **定义** | 只改 `Seed` ⇒ 至少一个体素不同（证明 Seed 真的参与世界规则） |
| **方法** | 两套仅 `Seed` 不同的参数生成同一坐标 Chunk；统计差异体素计数；对多个 Chunk 统计并断言 `总差异 > 0` |
| **反例保护** | 断言差异计数**既不恒为 0**（否则 Seed 无效），也**不为全部体素**（否则可能是"整体错位/越界"这类缺陷的信号 —— 仅作诊断输出，不作硬断言） |
| **对应上游** | `WorldGeneration_Core.md` §2；地质规则 GE2 |

### GIV4 — 洞穴连续

| 项 | 内容 |
|---|---|
| **定义** | ①**采样连续**：相邻世界坐标的 `CaveDensity` 差值有显式上界（`|Δ| ≤ MaxCaveGradient`），不得逐点跳变；②**判定连续**：跨 Chunk 边界的洞穴判定一致；③**单调影响**：开洞/关洞两次生成的差异集合**只包含 `Solid → Empty`**（不得把 Air 变 Solid、不得改变材料种类） |
| **方法** | ①沿 x/y/z 扫描相邻采样求最大差分；②边界列比较洞穴判定；③两套设置（仅 `EnableCaves` 不同）逐体素比较差异集合 |
| **反例保护** | 断言"开洞后空腔体素数 > 0"（否则说明洞穴没生效）且"关洞时差异为 0" |
| **对应上游** | 地质规则 §5.2、GE4/GE8；`VoxelWorld_Mesh.md`（几何仍由 Meshing 派生） |

### GIV5 — 矿物规则不影响 VoxelType

| 项 | 内容 |
|---|---|
| **定义** | 矿藏属性**不改变任何体素**：仅改 `EnableDeposits`/`DepositFrequency` ⇒ 体素数据**逐体素完全一致**（差异 0） |
| **方法** | 两套设置（仅矿藏参数不同）生成同坐标 Chunk ⇒ 全量 4096 比较 + `ComputeContentHash()` 相等；同时断言"开启时 `GetDepositZone` 至少对若干实体坐标返回非 `None`"（证明矿藏信息确实存在） |
| **扩展检查** | 矿藏查询对 `Empty` 坐标必须返回 `None`；矿藏查询结果与生成顺序无关 |
| **对应上游** | 地质规则 §6.1、GE7/GE9；`VoxelWorld_Core.md` §2（体素状态集合冻结） |

### GIV6 — 结构边界（补充）

| 项 | 内容 |
|---|---|
| **定义** | 生成阶段不出现 Mesh / Unity / Entity / Runtime 标识符；采样器签名不含 Chunk 坐标 |
| **方法** | 剔除注释后逐 token 扫描 `Generation/Geology/*.cs`（禁词表见 §12.1），带扫描器自检与正向对照（`GeologyWorldGenerator.cs` 必须出现 `LocalToWorldCoordinate`） |
| **对应上游** | 地质规则 §1.2/§10.2、GE10；`Runtime_Interface.md` §2.3 |

### GIV7 — 生成顺序纪律（补充）

| 项 | 内容 |
|---|---|
| **定义** | 写入嵌套顺序必须是 `localX → localY → localZ`，且调用 `SetVoxel(localX, localY, localZ, type)` |
| **方法** | 源码扫描：`GeologyWorldGenerator.cs` 中三层 `for` 的变量顺序与 `SetVoxel` 参数顺序一致（正向对照 + 反例探针自检） |
| **对应上游** | 本文档 §5；`Runtime_Core_Implementation.md` §5.3（写入遍历顺序固定） |

### GIV8 — 输出契约与参数校验（补充）

| 项 | 内容 |
|---|---|
| **定义** | ①`chunk.Coordinate == 请求坐标`（Runtime Core 的 Validate 步骤会检查，`Runtime_Core_Implementation.md` §5.1）；②所有体素类型 ∈ `{Air, Grass, Dirt, Stone}`（Core 的越界检查会拒绝非法值）；③`IsValid` 非法参数 ⇒ 构造拒绝；④`Generate` 不需要任何外部状态（不读世界、不读时间） |
| **方法** | 直接断言 + 逐体素类型范围检查（4096）+ 非法参数构造抛出 |
| **对应上游** | `Runtime_Core_Implementation.md` §5.1/§5.2；`WorldGeneration_Core.md` §8（V1 只支持 4 种体素状态） |

### GIV9 — 与 Stage 1 的等价性（补充，最关键）

| 项 | 内容 |
|---|---|
| **定义** | 关闭全部地质特性（`EnableCaves=false`, `EnableDeposits=false`, 无 Biome 修正）⇒ `GeologyWorldGenerator` 的输出与 `BasicTerrainGenerator` **逐体素完全一致** |
| **方法** | 同 `WorldGenerationSettings` + 同坐标，两个生成器各生成同一 Chunk ⇒ 全量 4096 比较 + `ComputeContentHash()` 相等；覆盖多个 Chunk（含负坐标） |
| **意义** | 证明地质层是**叠加**而不是"另一套地形"；保证既有世界的可复现性（同 Seed 同坐标在新实现下不变） |
| **对应上游** | 地质规则 §3.3（与 `ClassifyVoxel` 逐条对应）、GE5/GE6/GE12 |

### GIV 与上游不变量对照

| 本文档 | 地质规则（WorldGeneration_Geology.md） | 生成契约 / 调度 |
|---|---|---|
| GIV1 | GE1 / GE2 / GE12 | `WorldGeneration_Core.md` §2；`Runtime_Core.md` RC2 |
| GIV2 | GE3 | `WorldGeneration_Core.md` §5/§6 |
| GIV3 | GE2 | `WorldGeneration_Core.md` §2 |
| GIV4 | GE4 / GE8 | `VoxelWorld_Mesh.md` §1.2（几何派生） |
| GIV5 | GE7 / GE9 | `VoxelWorld_Core.md` §2 |
| GIV6 | GE10 | `Runtime_Interface.md` §2.3/§4 |
| GIV7 | — | `Runtime_Core_Implementation.md` §5.3 |
| GIV8 | — | `Runtime_Core_Implementation.md` §5.1；`Runtime_Core.md` RC1（类型范围） |
| GIV9 | GE5 / GE6 / GE12 | `WorldGeneration_Core.md` §4/§9 |

---

## 12. 结构扫描与零修改纪律

### 12.1 禁令清单（可机械扫描，剔除注释后逐 token）

扫描范围：`Assets/Scripts/VoxelWorld/Generation/Geology/*.cs`

| 类别 | 禁词 / 模式 |
|---|---|
| Unity | `UnityEngine`, `UnityEditor`, `MonoBehaviour`, `GameObject`, `Transform`, `Material`, `Time.`, `Random`, `Mathf.PerlinNoise` |
| Mesh | `VoxelMeshGenerator`, `VoxelMeshData`, `\.Vertices`, `\.Normals`, `\.UVs` |
| Runtime / Streaming | `VoxelRuntimeCore`, `ChunkRecord`, `ChunkStreamingDriver`, `IChunkPresenter`, `IChunkMesher`, `RequestChunk`, `ReleaseChunk`, `ClearDirty`, `MarkDirty` |
| Entity | `CrystalCluster`, `Entity`, `Spawn`, `Prefab`, `Instantiate` |
| 采样签名 | 采样器公开签名中出现 Chunk 坐标参数（`chunkCoordinate` / `chunkX` / `chunkY` / `chunkZ`） |
| 旧体系 | `TerrainModificationV2`, `DensityField` |

**正向对照（必须存在）**：
* `GeologyWorldGenerator.cs` 出现 `LocalToWorldCoordinate`（证明用 VoxelCore 换算）；
* `GeologyWorldGenerator.cs` 出现 `IWorldGenerator`（证明是实现抽象，不是新契约）；
* `GeologySampler.cs` **不**出现 `VoxelChunk`（采样器不接触数据容器）。

### 12.2 零修改纪律（可机械验证）

| 项 | 要求 |
|---|---|
| `VoxelCore` / `BasicTerrainGenerator` / `IWorldGenerator` / `WorldGenerationSettings` / `HeightFunction` | SHA256 不变 |
| Runtime Core / Streaming / Mesh / View / Bootstrap | SHA256 不变 |
| 既有 Editor 验证入口（5 个） | SHA256 不变 |
| 既有规范（11 份 World 规范 + ADR） | SHA256 不变 |

**验证方式**：与新入口同一批运行（既有做法：文件哈希比对，落地后逐一列出比对数量与结果）。

### 12.3 "等价而非复用"条款

`GeologyWorldGenerator` **必须**独立表达 Stage 1 规则（不得调用 `BasicTerrainGenerator`，
也不得复制其源码文本）：

* **不得调用**：调用会引入"生成器依赖生成器"的耦合，并让 GIV9 变成同义反复（自己比自己）；
* **不得复制粘贴**：规则必须按本规范重新表达（§3.3 的逐条对应表就是重新表达的依据），
  复制粘贴会让两份实现同时漂移；
* **可比较**：GIV9 通过"同参数同坐标逐体素比较"证明等价 ✓。

---

## 13. 落地清单与顺序

### 13.1 阶段划分

| 阶段 | 内容 | 验收 |
|---|---|---|
| **Stage 0（本轮）** | 本规范 | 文件存在、零修改（§12.2） |
| **Stage A** | `GeologySettings` + `GeologyNoise` + `GeologySample` | `IsValid` 校验；噪声确定性自检（同坐标恒等、相邻差分有界） |
| **Stage B** | `GeologySampler`（世界坐标纯函数）+ 可解释入口 | GIV1 / GIV2 / GIV3 / GIV9（Stage 1 等价，特性全关） |
| **Stage C** | `IDepositProvider` / `IBiomeProvider` 接口 + 采样器实现（**接口层，不生成 Entity/植被**） | GIV5（矿藏不改体素）+ 接口纯函数性 |
| **Stage D** | `GeologyWorldGenerator : IWorldGenerator`（接入层：坐标换算 + 固定顺序写入） | GIV6 / GIV7 / GIV8 + GIV1–GIV5 全量 |
| **Stage E** | `Assets/Editor/GeologyWorldGenerationValidation.cs`（GIV1–GIV9 + 既有回归 + SHA） | 批处理 PASS；既有 6 个验证入口全部 PASS |
| **Stage F（独立后续）** | 组装点选择新生成器（Bootstrap 或玩法层显式选择） | Runtime / Streaming / Mesh / View **零改动** ⇒ 只换一个 `new` |

### 13.2 每阶段的硬性检查

1. 每个阶段结束时：既有 6 个验证入口（VoxelCore 61 / WorldGeneration 41 / Mesh 37 / View 127 /
   RuntimeCore 196 / Streaming 106）必须仍全部 PASS；
2. 每个阶段结束时：§12.2 的 SHA 比对必须为 0 改动；
3. Stage D 之后：`GeologyWorldGenerator` 必须能直接被 `VoxelRuntimeCore` 使用
   （因为只实现 `IWorldGenerator`，Core 无需任何改动）—— 可用"用新生成器跑一遍 Runtime Core 闭环"验证。

### 13.3 接入 Runtime 的前提与方式（后续步骤，本轮不做）

* **前提**：Stage D 完成且 GIV1–GIV9 全绿；
* **方式**：只在组装点替换 `new BasicTerrainGenerator(...)` → `new GeologyWorldGenerator(...)`；
  或在玩法层显式选择（两者都只改**新增/组装点**代码）；
* **禁止**：为了让新生成器接入而修改 Runtime Core / Streaming / Bootstrap 的**既有逻辑**
  （若发现必须修改，说明本规范设计出错，应先改本规范）；
* **V1 默认**：仍使用 `BasicTerrainGenerator`（不替换旧实现）。

---

## 14. 明确不做与变更规则

### 14.1 明确不做（本文档范围）

* `TerrainGenerator` 本体的实现（用户明确要求先不做）；
* 具体 Biome（森林/沙漠/雪原…）与任何植被生成；
* 晶簇 / 矿石节点 / 掉落物等 Entity；
* 树木 / 建筑 / 生物 / NPC / 文明；
* 新增 `VoxelType`、新增几何/材质语义、新增存档格式；
* 任何对既有文件（§1.3 清单）的修改；
* 加载/卸载策略、Chunk 生命周期、Mesh/View 的任何改动。

### 14.2 变更规则

1. 与上游冲突时优先级见文档头；本文档不得放宽上游硬性条款
   （真值隔离、确定性、不覆盖改动、不要求修改 VoxelCore）；
2. 任何触及 §11（GIV 不变量）、§12（结构纪律）、§1.3（禁止修改清单）的修改必须：
   ① 升版本号；② 同步更新验证入口；③ 说明与既有世界（同 Seed 同坐标结果）的兼容性；
3. **禁止**用"地质需要"为理由修改 VoxelCore / 既有生成实现 / Runtime / Streaming / Mesh / View；
4. 落地只能以**新增文件**的方式进行（§1.4 清单）。

---

## 附录 A：接口与类型草案（下一阶段落地依据）

```csharp
namespace VoxelWorld.Generation.Geology
{
    // ---- 参数（新增，显式；不改 WorldGenerationSettings）----
    public sealed class GeologySettings
    {
        public bool EnableCaves = true;
        public int  CaveThreshold = 0;
        public int  CaveFrequency = 1;
        public bool EnableDeposits = true;
        public int  DepositFrequency = 1;
        public int  DepositRichness = 0;
        public bool EnableBiomeModifiers = false;

        public bool IsValid(out string reason);
    }

    // ---- 可解释性：命中规则 ----
    public enum GeologyRule
    {
        WorldBounds,   // P0
        Cave,          // P1
        AboveSurface,  // P2
        Surface,       // P3
        Depth,         // P3
        Deep           // P3
    }

    public struct GeologySample
    {
        public GeologyRule Rule;
        public int  SurfaceHeight;
        public int  CaveDensity;
        public byte DepositZone;   // 0 = None
        public byte Biome;         // 0 = 无群系
        public bool IsSolid;
    }

    // ---- 采样器（世界坐标纯函数；签名中不出现 Chunk 坐标）----
    public sealed class GeologySampler
    {
        public GeologySampler(
            WorldGenerationSettings stage1Settings, GeologySettings geology,
            IBiomeProvider biomeProvider /* 可为 null */);

        public int  SurfaceHeightAt(int worldX, int worldZ);
        public int  CaveDensityAt(int worldX, int worldY, int worldZ);
        public byte DepositZoneAt(int worldX, int worldY, int worldZ);   // 只在实体内非 0
        public byte BiomeAt(int worldX, int worldZ);

        public bool IsSolidAt(int worldX, int worldY, int worldZ);
        public VoxelType MaterialAt(int worldX, int worldY, int worldZ);

        // 可解释入口（与快速路径共用同一判定逻辑）
        public GeologySample ExplainAt(int worldX, int worldY, int worldZ);
    }

    // ---- 矿物：只读查询接口（本轮只定义，不生成 Entity）----
    public interface IDepositProvider
    {
        byte GetDepositZone(int worldX, int worldY, int worldZ);
    }

    // ---- 群系：只接口，不实现、不生成植被 ----
    public interface IBiomeProvider
    {
        byte GetBiome(int worldX, int worldZ);
        GeologyBiomeProfile GetProfile(byte biome);
    }

    public struct GeologyBiomeProfile
    {
        public VoxelType SurfaceMaterial;   // V1 只能是 Grass/Dirt/Stone（不得为 Air）
        public int SoilDepthDelta;
        public int SurfaceHeightDelta;
        public int Temperature;
        public int Humidity;
        public int VegetationRule;
    }

    // ---- 接入层：实现既有抽象，不修改它 ----
    public sealed class GeologyWorldGenerator : IWorldGenerator
    {
        public GeologyWorldGenerator(
            WorldGenerationSettings stage1Settings, GeologySettings geologySettings,
            IBiomeProvider biomeProvider);

        public WorldGenerationSettings Settings { get; }   // 契约不变
        public GeologySettings Geology { get; }            // 新增只读

        // 固定顺序 localX → localY → localZ；世界坐标经 LocalToWorldCoordinate 换算
        public VoxelChunk Generate(VoxelCoordinate chunkCoordinate);
    }
}
```

**附录 A 禁令**：以上类型与实现中不得出现
`UnityEngine` / `UnityEditor` / `VoxelMeshGenerator` / `VoxelMeshData` /
`VoxelRuntimeCore` / `ChunkStreamingDriver` / `IChunkPresenter` / `CrystalCluster` /
`Instantiate` / `MarkDirty` / `ClearDirty` / `RequestChunk` / `ReleaseChunk`。

## 附录 B：与现有规范一致性映射

| # | 规范 | 本文档的处理 | 冲突 |
|---|---|---|---|
| 1 | `WorldGeneration_Geology.md` | 本文档是其接入细化：结构 / 参数 / 接口 / GIV1–GIV9 全部映射到 GE1–GE12 | 否 |
| 2 | `WorldGeneration_Core.md` | 实现 `IWorldGenerator` 契约（§3）；确定性（§2）；Chunk 可独立计算（§6）；stage 叠加（§7）；不覆盖改动（§9）；不要求改 VoxelCore（§10） | 否 |
| 3 | `VoxelWorld_Core.md` | 不新增 `VoxelType`；只用 `SetVoxel` 写数据；坐标经 `LocalToWorldCoordinate` 换算 | 否 |
| 4 | `VoxelWorld_Runtime_Interface.md` | `IWorldGenerator` **不改名/不重复定义/不改签名**；新实现属于 Generation 实现侧；组装点不变 | 否 |
| 5 | `VoxelWorld_Runtime_Core.md` | 地质实现可被 Core 直接调用（Core 只认识抽象）；输出满足 Core 的 Validate（坐标自洽 + 类型范围） | 否 |
| 6 | `VoxelWorld_Runtime_Core_Implementation.md` | 安装流程（Generate→Validate→Install→Register）与 §5.3 写入纪律（x→y→z）一致 | 否 |
| 7 | `VoxelWorld_Runtime.md` | 加载/卸载与地质无关；`y ≥ WorldHeight` 恒空与垂直范围一致 | 否 |
| 8 | `VoxelWorld_Mesh.md` | 生成阶段不产几何；洞穴/地层的面由 Meshing 派生 | 否 |
| 9 | `VoxelWorld_Mining.md` | 生成与挖掘来源不同、结果同型（Air）；生成不覆盖改动 | 否 |
| 10 | `VoxelWorld_View.md` | 表现层与地质无关 | 否 |
| 11 | `ADR-001` | 晶簇等对象归 Entity、不属于 Chunk 所有权；本文档只定义 Deposit 只读接口 | 否 |
| 12 | `BasicTerrainGenerator`（既有实现） | 不替换、不修改、不被新实现调用；以 GIV9 证明"特性全关时逐体素等价" | 否 |
