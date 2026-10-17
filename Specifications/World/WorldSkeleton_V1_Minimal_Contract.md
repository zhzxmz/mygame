# WorldSkeleton V1 Minimal Contract（WorldSkeleton V1 产物接口与查询契约）

> **文档性质**：**WorldSkeleton V1 的产物接口与查询契约**（权威分工见 §1.5）。定义 V1 的最小职责、最小数据概念与查询行为契约、与 Region 及其它系统的依赖关系、首个可运行里程碑与验收判据。
>
> **生效状态**：§1.1 为**已批准**（架构方向 + 本轮批准决定）；§1.2 为**本文提案，尚待批准**；§1.3 为**代码事实**；§1.4 为**未实现项**。
>
> **当前实现状态**：`WorldSkeletonFacts.TryEvaluate` 最小查询接口已实现并通过 Unity 项目编译；它只提供既有高度场、海平面派生海洋与单大陆退化编号，不代表完整 WorldSkeleton 生成层已实现。大陆/掩码/气候带/`Slope` 生成及 Region 对骨架的实际消费仍属缺口。

---

## §1 Status and Authority

### 1.1 已批准（**已生效**）

| # | 已批准内容 |
|---|---|
| **A-1** | **WorldSkeleton V1 负责世界的大尺度地理格局与必要的基础地质事实** |
| **A-2** | **Region 根据 WorldSkeleton 的基础事实**确定区域身份、生成区域影响来源，并提供区域环境影响参数 |
| **A-3** | **Terrain** 负责具体地形几何及其表现 |
| **A-4** | **Magic、WorldHistory / WorldEvent、Resource、Ecology** 保持各自职责，通过明确的数据契约协作 |
| **A-5** | WorldSkeleton **不承担**所有下游系统的业务逻辑，**不得**读取 Region 的输出形成循环依赖 |
| **A-6** | `WorldSkeletonFacts` **最小查询接口已实现**；完整 WorldSkeleton 生成层（大陆/掩码/基础气候带/`Slope`）仍未实现；**不得将查询接口误称为完整生成层已实现** |
| **A-7** | 本文档（`WorldSkeleton_V1_Minimal_Contract.md`）作为 **WorldSkeleton V1 的产物接口与查询契约** |
| **A-8** | `WorldGeneration_Pipeline_Contract.md` 继续作为**阶段调度、生命周期、依赖方向与阶段输入面的权威契约**；`WorldState_Design.md` §7.4 继续作为**大陆、掩码及相关退化规则的权威来源**（本轮未修改该文件） |
| **A-9** | **`Skeleton → Region` 是已批准的上游只读依赖**（不是待决架构问题）；登记缺陷已在 `PC52` 补正 |
| **A-10** | Phase 1 **可以只读复用既有 `HeightFunction`**，**不得**改变其行为 |
| **A-11** | `Elevation` 的**终态 = 既有高度场只读转发，且不使用任何参数域**（不得以扩充域 1 覆盖清单作为终态） |
| **A-12** | **山脉、河流、河谷、土壤母质的权威归属待用户裁决**；裁决前**不得**判定为已排除或已纳入，**不得**实现或伪造这些事实 |
| **A-13** | 已批准登记 `ContinentCenters` / `ContinentRadius` / `ContinentShape` / `OceanGap` 的**语义与取值来源**；**未批准**新增默认值、生成算法、具体编码或未经现有契约支持的取值约束 |
| **A-14** | **未批准** Region 候选生成规则、`Kind` 数字码表及 `Epoch` / `Ordinal` 保留值编码；**不得**推进这些实现 |
| **A-15** | **垂直越界（`D1`，已批准）**：**有效垂直范围 = `0 ≤ y < WorldHeight`**；`y < 0` 或 `y >= WorldHeight` 时查询**必须明确失败**；**不得**夹取、钳制或静默修正越界坐标；**水平世界仍无限**，不新增水平越界限制 |
| **A-16** | **查询失败与成功结果分离（`D2`，已批准）**：查询成功必须产生**完整、有效**的结果；查询失败必须能被调用方**明确识别**；**不得**使用 `Continent = 0`、`Ocean = false` 或其它合法默认值冒充查询成功；**不得**把部分结果作为完整有效结果交付。本轮**只冻结行为语义**，**不规定**具体 C# 异常类型、结果包装类型、方法签名或类结构 |
| **A-17** | **大陆编号与单大陆退化模式（`D3`，已批准）**：`Continent = 0` 表示海洋/无大陆，是**合法查询结果**；**单大陆退化模式下所有陆地查询结果 `Continent = 1`**；`ContinentCount`（大陆**数量**配置）与 `Continent`（查询位置所属大陆**编号**）**不得混淆**；**不**提前规定多大陆的编号分配、稳定化或持久化规则，且**不**据此批准其它大陆生成参数、默认值、范围或算法 |

> 附带已批准项（前置轮次）：`G-1`（Region 层承担候选生成职责，产物为 `RegionSourceCandidate`）、`G-2`（`Kind` 用整数码且映射须登记在 `Region_Design.md` 指定位置）、`G-3`（首次实现只承诺确定性生成）。具体字段、接口、生成顺序**仍未确定**（本文不假定其已确定；`A-14` 明确本轮不推进）。

### 1.2 本文提出、尚待批准的设计（**未生效**）

| 编号 | 提案 | 所在章节 |
|---|---|---|
| **P-1** | V1 **基础事实集合** = `Elevation`（既有高度场 + 掩码叠加后的结果）/ `Ocean` / `Continent` / `Slope` + （建议）基础气候带；**不含**任何未裁决事实 | §3A |
| **P-2** | **生成算法可配置参数**的登记范围与未批准项清单 | §3B |
| **P-3** | 查询契约（输入、返回、**结果 vs 失败**、无隐式生成、确定性、退化） | §4 |
| **P-4** | 依赖边登记与不变的读边纪律 | §5 |
| **P-5** | 首个可运行里程碑与验证判据（**当前不可运行**） | §6 |
| **P-6** | 仅剩的阻塞项（`OD-4′`、`OD-5`；`OD-3` 已由 `D1`/`D2` 关闭） | §7 |

### 1.3 当前代码已经实现的内容（只读核查，**代码事实**）

| 项 | 证据 |
|---|---|
| 既有高度场 | `Assets/Scripts/VoxelWorld/Generation/HeightFunction.cs` **L21**：`public static class HeightFunction` |
| 地形/地质生成层 | `Generation/IWorldGenerator.cs` **L17**、`BasicTerrainGenerator.cs` **L25**、`WorldGenerationSettings.cs` **L20**、`Geology/GeologySampler.cs` **L41**、`Geology/GeologySample.cs` **L22**、`Geology/GeologyWorldGenerator.cs` **L36** 等 |
| Voxel / Mesh / Runtime / Streaming / View | `VoxelChunk`、`VoxelWorld`、`VoxelMeshGenerator`、`VoxelRuntimeCore`、`ChunkStreamingDriver`、`VoxelChunkView` 等 |
| V2 参数层（Phase 2 等价实现） | `Assets/Scripts/World/V2/`：8 参数规则表 + `WorldParameterEvaluator`（唯一 `Compose`，**L115–L124**）+ `WorldParameterState` |
| V2 Region 层（**占位链**） | `RegionDetermination`（哈希直接构造身份，**L84**）、`RegionPipeline.ResolveContext`（**L60**）、`RegionModifierDetermination.Determine`（**L69**）、`RegionSourceCandidate`（9 成员，纯数据载体） |
| 参数域登记 | `WorldParameterHash.SkeletonDomainId = 1`（**L32**）；`WorldParameter_Contract.md` §6.4 域表：`1` = `Skeleton`（覆盖 `Continent` / `Slope` / 基础气候带） |
| **WorldSkeleton V1 查询接口（已实现 · 阶段 A/B）** | `Assets/Scripts/World/V2/WorldSkeletonFacts.cs`（**121 行**；SHA-256 `EE8627035B72FD16074662BB2B13F963130D45B28F0A05945C3678FE0E3B0AC0`）+ 配套 `WorldSkeletonFacts.cs.meta`（GUID `0e1392c2839f0b94395dfd95eddb681a`）；**Unity 2022.3.62f1c1 项目编译通过**（`Assembly-CSharp.dll` 含 `WorldSkeletonFacts`/`TryEvaluate` 符号） |

### 1.4 当前尚未实现的内容（**缺口**）

| 缺口 | 证据 |
|---|---|
| **骨架生成层**（大陆/掩码/基础气候带/`Slope`） | **仍无实现**：不存在大陆函数、掩码叠加、气候带与 `Slope` 生成代码。**注意**：`WorldSkeleton V1 查询接口` **已实现**（§1.3 末行），但其事实来源仅为"既有高度场 + 海平面"，**不含**大陆/掩码/气候带。（本行更正：修订前本文写作"`WorldSkeleton` 命中 0 个 `.cs`"，该事实已被阶段 A/B 的实现变更） |
| 骨架事实的**参数侧**来源 | `WorldParameter_Source_Contract.md` 表 A1：`Temperature`/`Moisture`/`Elevation`（**参数系统内**）当前均为**占位**（域 1 标签哈希）；`Continent`（序 9）/`Slope`（序 14）**未接入**。**区分**：`Elevation` 的**查询侧**已由 `HeightFunction` 直接提供（`IF-3`），但**参数系统侧**仍为占位，二者互不影响 |
| Region → 骨架的消费 | 无：`RegionDetermination` 是占位哈希，从不查询任何来源 |
| History / Magic / Resource / Ecology 层 | **无**任何 V2 类型；`Assets/Scripts/World/ResourceNode.cs` **L7** 是既有 `MonoBehaviour` 玩法脚本，**不是** Resource 层契约实现 |
| Region 候选生成规则 / `Kind` 码表 / `Epoch`·`Ordinal` 保留值 | **未批准**（`A-14`）；不得推进 |

### 1.5 权威分工（**已批准**，`A-7`/`A-8`）

| 事项 | 权威文档 |
|---|---|
| 阶段顺序、生命周期、依赖方向、阶段输入面、（跨阶段访问）禁止边 | `Specifications/World/WorldGeneration_Pipeline_Contract.md` |
| 大陆、掩码、退化规则（`WC1`–`WC6`） | `Specifications/World/WorldState_Design.md` §7.4 |
| **WorldSkeleton V1 的产物接口与查询契约**（事实集合、返回语义、结果/失败区分、确定性要求） | **本文档** |
| 参数身份/量纲/权限与骨架生成配置登记 | `Specifications/World/WorldParameter_Contract.md` §3 |
| 参数来源与占位替换计划 | `Specifications/World/WorldParameter_Source_Contract.md` §5 |
| Region 的输入白名单（含骨架事实读入） | `Specifications/World/Region_Architecture_Boundary_Contract.md` §3.1（`RABW5`） |

---

## §2 WorldSkeleton V1 Responsibilities

### 2.1 职责（`R-1`–`R-6`）

| # | 职责 | 依据 |
|---|---|---|
| **R-1** | 提供**全流程的舞台框架**：大陆/海洋、基础气候带、基础高度框架 | `WorldGeneration_Pipeline_Contract.md` Phase 1 `Purpose` |
| **R-2** | 以**纯函数**(`Seed` + 显式参数) 确定大陆的数量/中心/半径/形状 | `WorldState_Design.md` `WC1`（**L638**） |
| **R-3** | 提供**大陆掩码**，并**只以叠加方式**影响 `Elevation`（不替换既有高度场定义） | `WC2`（**L639**）；`PC16`；`Geology_Architecture_Contract.md` `GC13`（**L90**）/`GC80`（**L228**） |
| **R-4** | 海洋由 `Elevation < SeaLevel` **派生**（不得用独立噪声） | `WC3`（**L640**）；`WorldParameter_Contract.md` `Ocean` 行；`WorldParameter_Source_Contract.md` 表 A1 `Ocean` 行 |
| **R-5** | 保证大陆间隔 ≥ `OceanGap`（隔离语义） | `WC4`（**L641**）；`Region_Design.md` `RW3`（**L580**） |
| **R-6** | 输出必须是**世界坐标的纯函数**（同 `Seed` + 坐标 ⇒ 同骨架，与加载顺序无关） | `PC18`；`WC1`（**L638**） |

### 2.2 非职责（`N-1a`/`N-1b`/`N-2`–`N-7`）

| # | 非职责 / 关系 | 依据 |
|---|---|---|
| **N-1a** | **不承担下游业务逻辑**：不产生"原因"、不判定区域身份、不生成候选、不提供生态/资源/历史/魔法内容，也不出现在因果链的**因果节点**位置上 | `PC15`；`Pipeline_Design` `PC1`（**L170**/**L177**） |
| **N-1b** | **同时是 Region 的已批准上游只读数据来源**：Region 通过白名单读取骨架事实并据此生成区域来源；该读取**是设计意图的一部分**，**不得**以"避免耦合"为由否认或省略（`A-9`） | `PC52`（已补登 `Skeleton→Region`）；Phase 3 `Allowed Read`；`Region_Architecture_Boundary_Contract.md` `RABW5`；`Region_Architecture_Contract.md` §3.1（**L152**，"自然来源（来自骨架事实）"） |
| **N-2** | **不**读取任何下游产物（`History`/`Region`/`WorldState`/`Biome`/`Geology`/`Voxel`/`Mesh`/`Runtime`） | `PC17`；`Pipeline_Design` `PC2`（**L171**）；`GC82`（**L230**） |
| **N-3** | **不**替换既有高度场：`Elevation` 必须复用既有 `HeightFunction`（只读，**不得改变其行为**，`A-10`） | `PC16`；`WC2`（**L639**）；`WorldParameter_Contract.md` `Elevation` 行 |
| **N-4** | **不**引入水平世界硬边界 | `PC5`（`Pipeline_Design`）；`Region_Design.md` `RW2`（**L579**） |
| **N-5** | **不**影响加载/卸载、Chunk 划分、脏标记、几何；不改变 `VoxelType`/Chunk 契约 | `WC6`（**L643**）；`WorldState_Design.md` **L653** |
| **N-6** | 大陆元数据**不是** Chunk 数据、**不是** Region 容器 | `WC5`（**L642**）；`Pipeline_Design` `PQ3` |
| **N-7** | **不**决定气候参数值本身（参数求值属 Phase 2）；参数层只求值环境参数、不产生世界结构 | `WorldParameter_Source_Contract.md` `WPS34`/`WPS35` |

### 2.3 候选事实类别逐项裁定

| 候选类别 | 裁定 | 依据等级 | 理由 | 最小需求（若纳入） | 未纳入/未裁决时的影响 |
|---|---|---|---|---|---|
| **大尺度陆地、海洋与地理分区** | **纳入 V1** | ① 现有正式契约 | `Ocean`/`Continent` 已登记；`WC3` 冻结派生关系；`RABW5` 承认其为 Region 输入 | 大陆掩码 + `Continent` 标识（`0` = 无大陆/海洋）+ `Ocean` 派生 | **不可推迟**：无舞台则 Region 无自然来源基础 |
| **山脉、河流、河谷、土壤母质** | **权威归属待用户裁决**（`A-12`）：**既未裁定纳入，也未裁定排除** | ④ 待用户决定 | `Region_Design.md` §3.2 **L214–L216** 已登记这些**来源类别**（说明 Region 将来需要相应事实），但其**事实承载层**未定；`GC82`（**L230**）只界定"大陆元数据不得参与材料/密度判定"，**不构成归属裁定** | 若纳入：需先裁决归属层，再登记事实与参数；若由他层承担：须立独立数据契约 | 裁决前：**不得**实现、**不得**伪造、**不得**以"缺少参数 ID"为由判定为已排除 |
| **必要的基础地质事实** | **纳入最小形式**：仅**基础高度框架（只读转发）+ 大陆掩码** | ① 现有正式契约 | `GC13`（**L90**）定义 V1 产物 = 基础高度框架（既有 `HeightFunction`）+ 大陆掩码；`GC82`（**L230**）禁止大陆元数据参与材料/密度判定 | 仅此两项（材料/矿藏/洞穴属 Geology Phase 5） | Region **不得**读 Geology ⇒ 任何"土壤母质/矿藏"类 Region 来源在 V1 无来源，须后续独立契约 |
| **地貌与地质信息之间的依赖关系** | **纳入为关系条款**（不是新字段） | ① 现有正式契约 | `GC2`（**L48**）、`GC80`–`GC82`（**L228–L230**）、`PC35` | 三条关系：掩码只叠加 / 退化兼容 / 大陆元数据不得进入材料·密度判定 | 若不明写，可能出现"骨架直接改高度场定义"或"大陆元数据参与地质判定" |

---

## §3 Minimal Data Contract

> **依据等级图例**：**①** 现有正式契约 · **②** 已批准架构方向 · **③** 本文提案 · **④** 待用户决定。
> 本节**只列已有依据的概念**；**不**为"将来可能有用"添加字段；**不**规定具体 C# 类型（编码待实现期确定）。

### 3A 基础事实（WorldSkeleton V1 必须表达的世界事实）

| # | 事实 | 表达含义 | 依据等级 | 登记状态 | 生成方 | 持久化 / 按需 | 查询方式与坐标语义 | 对 Region 的用途 | V1 必需 |
|---|---|---|---|---|---|---|---|---|---|
| **F-1** | **`Elevation`** | 该坐标的地形基础高度（既有高度场 + 掩码叠加后的**结果**） | ① | 参数已登记（`WorldParameter_Contract.md` 表 A `Elevation` 行）；**终态来源**：只读转发既有高度场、**不使用任何参数域**（`A-11`；`WPS57`） | 既有高度场（Skeleton 只读转发）；掩码由 Skeleton 叠加（`GC13`/`WC2`） | 阶段登记生命周期 = World Lifetime；规则要求纯函数（`PC18`）⇒ **可按需重算**；是否物化属实现细节 | 世界坐标；单位 = 体素（y 轴）；值域 `[0, WorldHeight-1]`；**整数语义、不得插值**（`WSC13`） | 自然来源的舞台；`Ocean`/`Slope` 的派生输入 | **是** |
| **F-2** | **`Ocean`** | 该坐标是否属海洋 | ① | 参数已登记（表 A `Ocean` 行）；**派生规则非临时** | Skeleton（派生 `Elevation < SeaLevel`，`WC3`） | 派生、可重算 | 世界坐标；布尔判定；**不得**用独立噪声 | 海洋类自然来源（`Region_Design.md` **L213**） | **是** |
| **F-3** | **`Continent`** | 该坐标所属大陆的标识 | ① | 参数已登记（表 A `Continent` 行：`[0, ContinentCount]`，`0` = 无大陆（海洋）；默认 `0`）；**编号语义（`D3`/`A-17`，已批准）**：`0` = 海洋/无大陆（**合法结果**）；**单大陆退化模式 ⇒ 所有陆地 = `1`**；`ContinentCount`（数量配置）**≠** `Continent`（编号）；**当前未接入** | Skeleton（大陆函数：`Seed` + 显式参数，`WC1`） | 纯函数派生、可重算 | 世界坐标；整数 ID；**`0` 表示"海洋/无大陆"，不是错误值**（见 `Q-8`/`Q-10`） | 区域身份/来源的分区依据；大陆间隔离（`RW3`） | **是** |
| **F-4** | **`Slope`** | 该坐标的地形坡度 | ① | 参数已登记（表 C `Slope` 行：`[0,1]`，`0` = 平地，默认 `0`）；**当前未接入** | Skeleton（由 `Elevation` 梯度派生） | 派生、可重算 | 世界坐标；归一化标量 | 与高程相关的自然来源**仅可参考**；**不得**直接解释为"山脉"（`A-12`） | 建议纳入（已登记） |
| **F-5** | **基础气候带**（`Temperature`/`Moisture` 的**骨架部分**） | 大尺度气候分区的框架值 | ①（域 1 = `Skeleton` 覆盖清单含"基础气候带"） | 域登记存在；**真实来源未接入**（当前为占位，`WPS` 表 A1） | Skeleton；参数层负责求值（`WPS34`/`WPS35`） | 派生、可重算 | 世界坐标；归一化基值 | 气候类自然来源（`Region_Design.md` **L212**） | 建议纳入，但**非**退化里程碑必需（见 §6 分层） |
| **F-6** | 大陆掩码（**机制**，非独立查询项） | 陆/海判定在大陆函数上的叠加量 | ③ 本文提案 | 不单独登记 | Skeleton | 不单独持久化 | **不单独查询**，只体现为 `F-1` 的结果 | 避免"掩码 vs 高度"两个真相 | 建议：不作独立事实（`【提案·待批准】`） |

### 3B 生成算法可配置参数

| # | 参数 | 含义 | 依据等级（概念） | 登记状态 | Owner / 取值来源 | 未批准项（不得自行取值） | 是否阻塞最小里程碑 |
|---|---|---|---|---|---|---|---|
| **C-1** | **`SeaLevel`** | 海洋判定阈值 | ① | **已登记**：`WorldParameter_Contract.md` 表 C，默认 **`64`**（显式配置），范围 `[0, WorldHeight]`、单位体素 | 配置（世界创建期） | 无（沿用既有登记值，**不另设默认值**） | 否 |
| **C-2** | **`ContinentCount`** | 大陆数量 | ① | **已登记**：表 C，默认 **`1`**（可退化为全部陆地），范围 `[1, 上限显式]` | 配置 | 无（沿用既有登记值，**不另设默认值**） | 否（退化模式即用 `1`） |
| **C-3** | **`ContinentCenters`** | 各大陆中心 | ①（**仅"确定性"要求**：`WC1` **L638**）；命名见 `WorldState_Design.md` §7.3 **L622**（该节自注"参数设计（显式，不实现）"，**不构成来源裁定**） | **待确认（未批准）**：**取值来源选择**（`Seed` 派生 / 世界创建期显式提供 / 其它）、**坐标类型与单位**均**未获批准**；数值范围/编码/算法亦未批准 | **待确认** | 来源选择、坐标类型、单位、范围、编码、算法 | 否（退化模式不依赖中心取值；多大陆形态待 `OD-4′`） |
| **C-4** | **`ContinentRadius`** | 大陆规模 | ①（`WC1`；§7.3 **L623**） | 同上：**语义与取值来源已登记**（配置，世界创建期）；**默认值/范围/编码未批准** | 配置（世界创建期） | 默认值、范围、编码 | 否（退化模式可退化为"无掩码"） |
| **C-5** | **`ContinentShape`** | 大陆形状 | ①（`WC1`；§7.3 **L623**） | 同上：语义与取值来源已登记；默认值/范围/编码未批准 | 配置（世界创建期） | 默认值、范围、编码、形状算法 | 否（退化模式不依赖形状） |
| **C-6** | **`OceanGap`** | 大陆之间最小海洋间隔 | ①（`WC4` **L641** 指名；§7.3 **L625**） | 同上：语义与取值来源已登记；默认值/范围/编码未批准 | 配置（世界创建期） | 默认值、范围、编码 | 否（单大陆退化模式无间隔语义） |

> **登记说明**：`C-3`–`C-6` 本轮**不批准**默认值、数值范围、编码与生成算法（`A-13`）；参数契约中已以"未批准（本轮不设定）"显式标注。**不得**把"未登记"当作"该事实不属于 WorldSkeleton"的理由（`A-12` 同族纪律）。
> **`C-3` 的待确认边界**：`WC1` 的**确定性**要求保留；但**不得**把"确定性"扩大解释为"中心只能由 `Seed` 派生"——`ContinentCenters` 的**取值来源、坐标类型与单位**继续标记为**待确认**。`C-4`–`C-6` 的取值来源 = 显式参数（配置，世界创建期），依据 `WorldState_Design.md` §7.3 **L623**/**L625**。

---

## §4 Query Contract

> 定义**行为契约**（`Q-*`）与**已冻结的接口形状**（`IF-*`，与 `WorldSkeletonFacts` 实现对齐）；**不**定义枚举、缓存、多大陆、`Slope`、气候带、Region 归属等未批准能力。

### 4.1 已冻结的接口形状（`IF-*`，与实现对齐）

| # | 已冻结条款 | 内容 |
|---|---|---|
| **IF-1** | **查询入口** | `public static bool WorldSkeletonFacts.TryEvaluate(WorldCoordinate position, WorldGenerationSettings settings, int seaLevel, out WorldSkeletonFacts facts)`（命名空间 `World.V2`） |
| **IF-2** | **Seed 的承载** | Seed **只**由 `settings.Seed` 承载；查询入口**不接收**独立 `WorldSeed` 参数（`Q-1` ① 的 Seed 输入即由 `settings.Seed` 提供） |
| **IF-3** | **`Elevation` 的来源（约束对象 = `WorldSkeletonFacts`）** | **本类型**只读调用既有 `HeightFunction.GetTerrainHeight(position.X, position.Z, settings)` 取得 `Elevation`；**不复制、不重写**高度计算逻辑，**不改变其调用语义**，**不**经参数域哈希。**本条不冻结 `HeightFunction` 的内部实现**：允许其在保持**既有签名与行为契约**的前提下演进内部实现（见 `HeightFunction.cs` L14–L16 自注与 `A-10`） |
| **IF-4** | **垂直范围** | `0 <= position.Y < settings.WorldHeight`；越界 ⇒ **明确失败**，**不钳制**、不修正（`Q-4`/`D1`/`A-15`） |
| **IF-5** | **成功 / 失败 + 调用约定** | 成功 ⇒ `true` + **完整有效**结果（`IsValid == true`）；失败 ⇒ `false` + `default`（`IsValid == false`，三字段为默认值）；**不存在部分结果**（`Q-8`/`D2`/`A-16`）。**调用约定（强制）**：调用方**必须先**确认 `TryEvaluate` 返回 `true` **或** `facts.IsValid == true`，**之后**才能把 `Elevation` / `Ocean` / `Continent` 当作有效结果使用；**失败返回的 `default` 不代表合法海洋，也不代表任何其它世界事实**（不得据此推导"该处为海洋/低地"等结论） |
| **IF-6** | **`Ocean`** | `Ocean = Elevation < seaLevel`（**严格小于**；`WC3`） |
| **IF-7** | **`Continent`** | `Continent = Ocean ? 0 : 1`（**当前仅单大陆退化模式**；`Q-10`/`D3`/`A-17`） |
| **IF-8** | **调用方前置条件（含违反后果）** | **前置条件（由调用方保证）**：① `settings` 合法（`WorldGenerationSettings.IsValid`）；② `seaLevel ∈ [0, WorldHeight]`（`WorldParameter_Contract.md` §5.2 冻结范围）。查询入口**不校验、不钳制、不修正**，也**不**新增失败原因通道。**违反前置条件时**：调用方**不能依赖**本契约保证的任何结果（本契约对该情形**不作保证**）；**不因此要求**查询入口增加校验、钳制、修正或新的失败分支 |
| **IF-9** | **`PS5` 的边界** | `WorldParameter_Contract.md` `PS5`（参数越界钳制）**仅**适用于**参数值域**；**不**延伸为本查询入口的钳制逻辑。本入口对 `seaLevel` **无任何**范围处理（见 `IF-8`） |
| **IF-10** | **未冻结项（明确排除）** | 多大陆编号分配/稳定化/持久化算法、`ContinentCenters` 的来源与坐标类型、`Slope`、基础气候带、山脉/河流/河谷/土壤母质、Region 归属与接线 —— **均未冻结、未实现**（见 §3A `F-4`/`F-5`、§7） |

> **条款分层与效力（`R-1`）**：`IF-*` 是 `A-15`–`A-17`（已批准决定）与 `Q-*`（行为条款）相关规则的**接口级重述**；**不得**用接口级重述改变上位已批准语义。若发现条款之间出现**实质不一致**，应**报告并停止**，**不得**自行选择一条覆盖另一条。

### 4.2 行为条款（`Q-*`）

| # | 条款 | 内容 | 依据 |
|---|---|---|---|
| **Q-1** | **查询输入（概念）** | ① **Seed**（**由 `settings.Seed` 承载**，见 `IF-2`）；② **世界坐标**（唯一空间输入）；③ 世界创建期配置：`settings`（世界生成配置）+ `seaLevel`（`C-1` 登记配置量）。**`ContinentCount`（`C-2`）不是本查询的输入**——V1 只实现单大陆退化模式（`IF-7`）。**不**接受 Chunk 标识、邻域状态或"部分世界" | `PC18`；`WC6`（**L643**）；`PZ11`；`IF-2`/`IF-8` |
| **Q-2** | **返回值** | **V1 已冻结（与实现对齐）**：`IsValid` + `Elevation`（整数体素）+ `Ocean`（布尔判定）+ `Continent`（整数 ID）（`IF-5`–`IF-7`）。**未冻结 / 未实现**：`Slope`（`F-4`）、基础气候带基值（`F-5`）、"该值是否已含大陆掩码叠加"（掩码未实现，`F-6`）——因此**不得**声称 V1 返回值已含大陆掩码叠加，也不得声称返回 `Slope`/气候带 | `IF-5`–`IF-7`；§3A `F-1`–`F-6`；`GC13`（**L90**） |
| **Q-3** | **无数据** | 在 V1 纯函数模型下**不应存在**（每个世界坐标都有骨架事实）。若未来引入"事实不覆盖区"，必须**显式表达**，且**不得**与"海洋/无大陆"混同 | `PC18`；`WC1`（**L638**） |
| **Q-4** | **无效位置（`D1`/`A-15`，已批准）** | **有效垂直范围 = `0 ≤ y < WorldHeight`**；`y < 0` 或 `y >= WorldHeight` ⇒ 查询**必须明确失败**（**不得**夹取、钳制或静默修正，也**不得**返回合法默认值）；水平方向无限 ⇒ **无**无效水平位置，**不新增**水平越界限制（`RW2` **L579**） | `A-15`；`WorldParameter_Contract.md` `Elevation` 行（`[0, WorldHeight-1]`）；`RW2`（**L579**） |
| **Q-5** | **生成未完成** | 纯函数模型下**不存在**中间态；但**若**将来物化世界级元数据（如大陆列表），必须显式状态，**禁止**部分填充（`PM32`/`PM33` 同族纪律） | `PC18`；`WorldParameter_Runtime_Model.md` **L91–L92** |
| **Q-6** | **是否允许隐式生成或修改世界状态** | **禁止**：查询**只读**；不得写世界状态、不得触发下游阶段、不得改变加载/Chunk/几何；骨架**只被下游读取** | `PC17`；`WPS28`；`PC101`；`WC6`（**L643**） |
| **Q-7** | **确定性要求** | 同 `(Seed, 显式参数, 世界坐标)` ⇒ **同结果**；与加载顺序、调用顺序、帧序、平台无关；**禁止**运行时随机与系统时钟；跨域统计独立 | `PC18`；`WC1`（**L638**）；`PZ13` |
| **Q-8** | **查询失败与成功结果分离（`D2`/`A-16`，已批准）** | ① **成功** ⇒ 产生**完整、有效**的结果；② **失败** ⇒ 必须能被调用方**明确识别**；③ **不得**使用 `Continent = 0`、`Ocean = false` 或其它合法默认值冒充查询成功；④ **不得**把部分结果作为完整有效结果交付；⑤ 本轮**只冻结行为语义**——具体 C# 异常类型、结果包装类型、方法签名与类结构**留待实现轮**决定（且不得违反 ①–④） | `A-16`；`WorldParameter_Contract.md` `Continent`/`Ocean` 行；`PM33`（**L92**，禁止部分填充） |
| **Q-9** | **退化要求** | `ContinentCount = 1` 且掩码退化为"全部陆地" ⇒ 骨架事实必须回到**既有无限地形**（与既有基线逐值一致）。**语境澄清（`R-3`）**：此处的 `ContinentCount` 属**世界级配置语境**，**不是** `TryEvaluate` 的输入参数，也**不**表示本查询入口必须读取该值（见 `Q-1` 与 `IF-7`） | `WC2`（**L639**）；`PC3`（`Pipeline_Design`）；`PQ1`；`GC81`（**L229**） |
| **Q-10** | **大陆编号语义（`D3`/`A-17`，已批准）** | ① **`Continent = 0` = 海洋/无大陆**，是**合法查询结果**（与查询失败无关，见 `Q-8`）；② **单大陆退化模式**（`ContinentCount = 1` 且掩码 = 全陆地）：**所有陆地查询结果 `Continent = 1`**；③ **`ContinentCount`（大陆数量配置）与 `Continent`（查询位置所属大陆编号）不得混淆**；④ **不**提前规定多大陆的编号分配算法、稳定化算法或持久化规则 | `A-17`；`WorldParameter_Contract.md` `Continent`（**L213**）/`ContinentCount`（**L240**）行 |

---

## §5 Dependency and Generation Order

### 5.1 有向边登记（**已批准**）

```
WorldSeed ──► WorldSkeleton ──┬──► Region           （只读：骨架事实 → 自然来源）
                              ├──► WorldState        （Base 规则输入）
                              └──► （经 WorldState/Biome）Geology 的地形基础

WorldSeed ──► History / WorldEvent ──► Region Source 候选 + HistoryTags（只读）──► Region
（History 尚未实现；接入形式已冻结于 `PC26`）
```

| 边 | 状态 | 登记位置 |
|---|---|---|
| `Seed → Skeleton` | 已批准 | `WorldGeneration_Pipeline_Contract.md` `PC52` |
| **`Skeleton → Region`** | **已批准（只读）**；**本轮补登记** | `PC52` 允许边枚举（本轮修订）；阶段输入面见同文档 Phase 3 `Allowed Read`；Region 侧见 `Region_Architecture_Boundary_Contract.md` `RABW5`（本轮修订） |
| `Skeleton → WorldState` | 已批准 | `PC52` |
| `Seed → Region` / `Seed → WorldState` | 已批准 | `PC52` |
| `Region → WorldState`（仅 `RegionModifier`） | 已批准 | `PC52`；`RA75`/`RA76` |
| `WorldState → Biome` → `Biome → Geology`（仅 P3） → `Geology → Voxel` → `Voxel → Mesh` | 已批准 | `PC52`；`GC2`（**L48**） |

> **登记缺陷（已修复）**：修订前 `PC52` 的"允许边枚举（共 8 条）"**漏列** `Skeleton→Region`，而该读边在 Phase 3 `Allowed Read` 中**已生效**——该不一致属**文档登记缺陷**，**不是**架构未决问题（`A-9`）。本轮已补登并更新计数。

### 5.2 依赖条款

| # | 条款 | 依据 |
|---|---|---|
| **DEP-1** | WorldSkeleton **不读取**尚未生成/未定稿的下游产物（Region/History/Biome/Geology/WorldState 的结论） | `PC17`；`Pipeline_Design` `PC2`（**L171**）；`GC82`（**L230**） |
| **DEP-2** | **不允许循环依赖**：骨架位于因果方向最前；同时 **`Skeleton → Region` 读边已登记**（"只被下游读取"**不等于**"没有下游数据依赖"，`N-1b`/`A-9`） | `PC52`（本轮修订）；`Pipeline_Design` `PC1`/`L177` |
| **DEP-3** | **Terrain 几何生成 ≠ WorldSkeleton 基础事实生成**：骨架提供高度**框架**与掩码，体素几何由 Phase 6/7 依 Geology 派生；Geology 不拥有体素 | `GC2`（**L48**）；`PC35`；`WC6`（**L643**）；`WorldState_Design.md` **L651** |
| **DEP-4** | 其它系统向 Region 提供影响来源**必须**走独立、明确的数据契约：History 形式已冻结（`PC26`）；Magic / Resource / Ecology **尚无登记提供方** | `PC26`；`Region_Architecture_Boundary_Contract.md` `RABW5`/`RABW6` |
| **DEP-5** | 历史事件、魔法源等在**无可用数据提供方**时，必须标记为**未实现**，不得伪造输入 | §1.4；`RA23`–`RA25`（候选不得携带下游产物/目标结果/玩家状态） |
| **DEP-6** | Region 的输出方向**唯一**：`RegionModifier` 只能写入 WorldState；Region 不得回写骨架 | `PC52`；`RA75`/`RA76` |
| **DEP-7** | 未裁决事实（`A-12`）**不得**成为任何已登记依赖边的输入 | `A-12`；`RABW5`（白名单封闭纪律） |

### 5.3 阶段顺序（沿用既有登记，**未改**）

`Seed(0) → WorldSkeleton(1) → WorldParameter(2) → Region(3) → Biome(4) → Geology(5) → Voxel(6) → Mesh(7) → Runtime(8)`；History 不以 Phase 编号出现，其产物以 Region 输入形式接入（`PC26`）。参数侧因果方向登记为 `Seed → Skeleton → History → Region → WorldState → Biome → Geology → Ecology / Resource → Query`，且 `Phase 1 Skeleton → Elevation · Ocean · Continent · Slope`。

---

## §6 First Runnable Milestone

> **当前状态：尚不可运行。** 本节只定义**验收目标、前置条件与验证方法**，**不**包含实现代码，也**不**声称已经可运行（`A-6`）。

### 6.1 目标（最小数据流）

```
WorldSeed
  └─► WorldSkeleton 查询（输入：Seed + 世界坐标 + 世界创建期参数）
        ├─ Elevation  ← 既有高度场（只读转发，A-10/A-11）+ 大陆掩码（只叠加）
        ├─ Ocean      ← Elevation < SeaLevel（WC3）
        ├─ Continent  ← 大陆函数（Seed + 显式参数）
        └─ Slope      ← Elevation 梯度
  └─► Region 消费骨架事实（自然来源）→ 候选 → RegionContext / RegionModifier
        └─► WorldState：FinalState = clamp01(Base + RegionModifier)
              （WorldParameterEvaluator.Compose **不得修改**）
```

| # | 步骤 |
|---|---|
| **M-1** | 给定 `WorldSeed` 与世界创建期显式参数 |
| **M-2** | **查询**最小 WorldSkeleton 基础事实（§3A `F-1`–`F-4`） |
| **M-3** | Region **消费**这些事实（作为自然来源的事实基础），产出 `RegionSourceCandidate` |
| **M-4** | 在不修改 `WorldParameterEvaluator` 的前提下，为指定坐标产出**可复现**的 Region 结果（`RegionContext` / `RegionModifier`） |

### 6.2 决策分层（严格区分）

| 层 | 内容 |
|---|---|
| **A. 编码前必须决定（阻塞）** | `OD-4′`（`C-3` 的**来源选择/坐标类型/单位**，以及 `C-3`–`C-6` 的默认值/范围/编码 —— **仅**在需要非退化（多大陆）形态时阻塞）；`OD-5`（Region 侧生成规则授权、`Kind` 码表、`Epoch`/`Ordinal` 保留值 —— `A-14` 明确本轮**未批准且不得推进**）。**查询侧语义已冻结**（`Q-4`/`Q-8`/`Q-10` = `D1`/`D2`/`D3`）：越界必须明确失败、失败不得冒充成功、单大陆退化模式陆地 = `Continent = 1` |
| **B. 可保守起步（需用户批准的保守方案）** | ① **退化模式**：`ContinentCount = 1` + 掩码 = 全陆地（直接引用已登记默认值，§3B `C-2`；退化验收见 `Q-9`）；② `SeaLevel = 64`（§3B `C-1` 登记值）；③ 里程碑**不要求** `F-5` 基础气候带落地（保持参数层既有行为）；④ 未裁决事实（`A-12`）**不进入**里程碑 |
| **C. 可推迟（非阻塞扩展）** | 非退化多大陆形态与 `C-3`–`C-6` 的完整取值；真实气候带；山脉/河流/河谷/土壤事实（`A-12` 裁决后另立）；骨架物化/缓存；跨大陆交通；世界硬边界；历史/事件/文明、魔法源、资源、生态内容规则 |

### 6.3 前置条件与验证方法

| 前置 | 状态 |
|---|---|
| `OD-5` 裁决（及非退化多大陆形态所需的 `OD-4′` 余项）；查询侧语义（`D1`/`D2`/`D3`）**已冻结** | ⬜ 未满足（`OD-5` 部分） |
| 骨架事实**不得**复用占位链（域 1 占位哈希、`ProvisionalSeaLevel`） | ⬜ 未满足（当前 `Elevation`/`Temperature`/`Moisture` 仍为占位，见 `WPS54a`） |
| `WorldParameterEvaluator` 逐字节不变；Region 现运行时代码不变 | ✅ 约束已明确 |

| # | 判据 |
|---|---|
| **V-1** | **确定性**：同 `(Seed, 参数, 坐标)` ⇒ 逐字段相同；**跨进程**指纹一致 |
| **V-2** | **退化兼容**：`ContinentCount = 1` + 掩码 = 全陆地 ⇒ 与既有基线**逐值一致**（`WC2`/`GC81`/`PQ1`） |
| **V-3** | **无下游读取**：结构扫描证明骨架侧不引用 History/Region/Biome/Geology/Voxel 或 Phase 2–8 产物（`PC17`） |
| **V-4** | **越界必须明确失败（`D1`）**：`y < 0` 或 `y >= WorldHeight` ⇒ 查询明确失败；且**不存在**夹取/钳制/静默修正（`Q-4`） |
| **V-4b** | **结果与失败分离（`D2`）**：`Continent = 0`（海洋）等合法结果与查询失败在返回上**不混同**；**无**部分结果被当作完整结果交付（`Q-8`） |
| **V-4c** | **大陆编号语义（`D3`）**：`Continent = 0` 为海洋（合法结果）；单大陆退化模式陆地 = `Continent = 1`（`Q-10`） |
| **V-5** | **Region 结果可复现**：同坐标跨进程一致；且不依赖 `WorldParameter FinalState` |
| **V-6** | **零修改证明**：`WorldParameterEvaluator` 与既有 V2 文件哈希不变；Legacy 地形哈希不变 |
| **V-7** | **不声称完整性**：里程碑只验证"事实可用 + Region 可消费 + 可复现"，**不**验证完整性/有界性（`G-3`） |

---

## §7 Open Decisions

### 7.1 阻塞下一步实现的决策（只列阻塞项）

| # | 问题 | 可选方案 | 推荐（及理由） | 对代码实现的影响 | 必须编码前决定？ |
|---|---|---|---|---|---|
| **OD-4′** | **`C-3` 的来源选择/坐标类型/单位**，以及 `C-3`–`C-6` 的**默认值/数值范围/编码** | ① 在参数契约登记默认值与范围；② 明确"无默认值，必须由世界创建期显式提供"；③ 由实现期确定编码、契约只固定语义；④（`C-3`）确认取值来源 = `Seed` 派生 / 世界创建期显式提供 / 其它 | **② + ③ + ④**：最小承诺、避免发明未经批准的数值（`A-13`）；`C-3` 的来源**保持待确认**，**不得**把 `WC1` 的"确定性"扩大解释为"只能由 `Seed` 派生" | 阻塞**非退化（多大陆）**实现；**不**阻塞单大陆退化里程碑 | **是**（仅当进入非退化形态） |
| **OD-5** | **Region 侧三项**：候选生成规则授权、`Kind` 数字码表、`Epoch`/`Ordinal` 保留值编码 | 见 `Region_Source_Candidate_Generation_Minimal_Design.md` §8（`MS-1`–`MS-3`） | 该文推荐方案；**本轮明确未批准**（`A-14`），**不得推进实现** | 阻塞 §6 `M-3`/`M-4` | **是** |

### 7.2 已关闭（登记完成，仅记录）

| # | 事项 | 结论 |
|---|---|---|
| **OD-1** | 骨架契约的权威落点 | **已关闭**（`A-7`/`A-8`）：产物接口与查询契约 = 本文档；调度/依赖/输入面 = `WorldGeneration_Pipeline_Contract.md`；规则 = `WorldState_Design.md` §7.4 |
| **OD-2** | `Skeleton→Region` 读边 | **已关闭**（`A-9`）：确认为**已批准只读依赖**，`PC52` 漏列属登记缺陷，本轮已补登记 |
| **OD-4 之语义部分** | 四个骨架生成参数的概念与取值来源 | **已登记**（`A-13`）：语义 + Owner/取值来源入 `WorldParameter_Contract.md` §3.4 表 C；余项见 `OD-4′` |
| **OD-3** | 查询的越界与失败表示 | **已关闭**（`D1`/`D2` → `A-15`/`A-16`）：有效垂直范围 `0 ≤ y < WorldHeight`，越界**必须明确失败**、**不得**夹取/钳制；成功/失败**必须可区分**、**不得**用 `Continent = 0`/`Ocean = false` 等合法默认值冒充、**不得**交付部分结果。**具体机制（异常类型/包装/签名）留给实现轮** |
| **OD-4′ 之编号部分** | 单大陆退化模式下的陆地 `Continent` 取值 | **已关闭**（`D3` → `A-17`）：`Continent = 0` = 海洋/无大陆（合法结果）；**单大陆退化模式：所有陆地 = `1`**；多大陆编号规则仍待 `OD-4′` |
| **实现裁决：`settings` 合法性归属** | 查询入口是否自检 `settings.IsValid` | **已关闭**：**由调用方保证**，入口不校验（`IF-8`）。依据：`IsValid` 的既有语境是"生成器构造时强制校验"（`WorldGenerationSettings.cs` **L72**）；`WorldHeight ≤ 0` 时 `D1` 空范围已使一切查询失败，无需额外校验即可保证安全 |
| **实现裁决：`seaLevel` 范围归属** | 查询入口是否钳制/拒绝越界 `seaLevel` | **已关闭**：**均不**（`IF-8`/`IF-9`）。`PS5` 的钳制仅适用参数值域，**不**延伸至本入口 |
| **实现裁决：Seed 承载方式** | 是否接收独立 `WorldSeed` 参数 | **已关闭**：**不接收**；Seed 只由 `settings.Seed` 承载（`IF-2`）。依据：`HeightFunction` 只使用 `settings.Seed`；两载体当前 0 处互通、亦无契约要求二者相等 |
| **实现裁决：结果构造权限** | 如何限制有效实例的构造 | **已关闭**：`private` 构造函数 + 查询入口内置于 `WorldSkeletonFacts`；**代码内构造调用点显式集中在 1 处**（`TryEvaluate` 内，文件内已注释标记）。`private` 仅提供语言层可见性（本类型及其嵌套类型），"唯一调用点"是**实现书面约定**，非编译器保证 |

**明确不列为阻塞项**（可推迟，不得升级为当前阻塞）：山脉/河流/河谷/土壤母质的裁决结果本身（裁决前只需"不实现、不伪造"）；气候带真实来源；`ContinentShape` 的具体形状算法；骨架物化/缓存；跨大陆交通；世界硬边界；历史/事件/文明、魔法、资源、生态内容规则。

---

## §8 Acceptance Criteria

> **声明：文档完成 ≠ 代码实现完成。** 本清单只验收**本文档**；骨架与 Region 的实现必须另立实现轮，并满足 §6.3 判据。

| # | 验收项 | 判据 |
|---|---|---|
| **AC-1** | 四类状态已区分且逐条标注 | §1.1 已批准 / §1.2 提案 / §1.3 代码事实 / §1.4 未实现 |
| **AC-2** | `N-1` 已拆分且**正向依赖被明确声明** | §2.2 `N-1a`（不承担下游业务）+ `N-1b`（是 Region 已批准上游只读来源） |
| **AC-3** | `Skeleton → Region` 已在依赖章节登记，`PC52` 缺项定性为登记缺陷 | §5.1 边表 + "登记缺陷（已修复）"注 |
| **AC-4** | 基础事实与生成算法参数已**分节** | §3A（`F-1`–`F-6`）与 §3B（`C-1`–`C-6`） |
| **AC-5** | 每项均有**依据等级**（①/②/③/④）与**登记状态** | §3A/§3B 表列 |
| **AC-6** | `ContinentCount`/`SeaLevel` 只引用既有登记值，未另设默认值 | §3B `C-1`/`C-2` |
| **AC-7** | 四个未登记参数标注为"概念已有依据，但参数登记或取值来源仍需完成"（本轮：语义与来源已登记，默认值/范围/编码未批准） | §3B `C-3`–`C-6` + 登记说明 |
| **AC-8** | 山脉/河流/河谷/土壤母质统一标为**权威归属待用户裁决**，且**未**以缺少参数 ID 为排除理由 | §2.3 第 2 行；§1.1 `A-12` |
| **AC-9** | `Elevation` 终态 = **移除参数域**；未把扩充域 1 覆盖清单写成终态 | §3A `F-1`；`WorldParameter_Source_Contract.md` `WPS57`（本轮修订） |
| **AC-10** | `D1`/`D2`/`D3` 已写入查询契约，且**未引入具体 C# 实现决定**：越界必须明确失败、成功/失败必须分离、`Continent = 0` 为合法海洋结果、单大陆退化模式陆地 = `1` | §4 `Q-4`/`Q-8`/`Q-10`；§1.1 `A-15`–`A-17` |
| **AC-11** | 查询/依赖/里程碑/权威分工表述已更新，且**未声称未实现功能已完成** | §1.5、§4、§5、§6（"当前不可运行"） |
| **AC-12** | 阻塞项只列 `OD-4′`/`OD-5`，并含四要素；`OD-3` 与"单大陆陆地编号"已关闭 | §7.1 + §7.2 |
| **AC-13** | 未超出白名单修改任何文件 | 交付报告（白名单核验） |
| **AC-14** | `ContinentCenters` 的来源选择/坐标类型/单位保持**待确认**，且**未**把"确定性"扩大解释为"只能由 `Seed` 派生" | §3B `C-3` + 登记说明 |
| **AC-15** | **文档与实现一致**：`IF-1`–`IF-10` 可逐条对照 `WorldSkeletonFacts.cs` 实现（入口签名、`Elevation` 来源、垂直范围、成功/失败、`Ocean`、`Continent`、前置条件、`PS5` 边界、未冻结项） | §4.1；`Assets/Scripts/World/V2/WorldSkeletonFacts.cs` |
| **AC-16** | **未冻结项未被写成已批准**：多大陆编号算法、`ContinentCenters` 来源、`Slope`、气候带、山脉/河流/河谷/土壤母质、Region 归属 —— 全部保持"未冻结/未实现"表述 | §4.1 `IF-10`；§7.1；§3A `F-4`/`F-5` |

---

## 附：一句话交付

> **权威分工**：调度/依赖/输入面 = `WorldGeneration_Pipeline_Contract.md`；规则 = `WorldState_Design.md` §7.4；**产物接口与查询契约 = 本文档**。
> **依赖**：`Skeleton → Region` 是**已批准的上游只读依赖**（`PC52` 已补登记）；"不是因果链的一环"只表达**不承担下游业务逻辑**，不否认数据依赖。
> **事实与参数**：事实 `F-1`–`F-6` 与配置参数 `C-1`–`C-6` 分节；`C-1`/`C-2` 引用既有登记值；`C-3`–`C-6` 语义与来源已登记、默认值/范围/编码未批准。
> **查询语义（已冻结，`D1`/`D2`/`D3`）**：有效垂直范围 `0 ≤ y < WorldHeight`、越界**明确失败**（不得钳制）；成功/失败必须分离（不得用 `Continent = 0`/`Ocean = false` 冒充成功）；`Continent = 0` = 海洋/无大陆（**合法结果**）；单大陆退化模式陆地 = `Continent = 1`。
> **待裁决**：山脉/河流/河谷/土壤母质（`A-12`）；`OD-4′`（含 `C-3` 来源/坐标类型/单位待确认）；`OD-5`。
> **已冻结的接口形状**（`IF-1`–`IF-10`，已实现）：`WorldSkeletonFacts.TryEvaluate(position, settings, seaLevel, out facts)`；`Elevation` 直接复用 `HeightFunction.GetTerrainHeight(X, Z, settings)`；垂直范围越界明确失败；成功=完整有效、失败=`default`；`Ocean = Elevation < seaLevel`；`Continent = Ocean ? 0 : 1`；Seed 由 `settings.Seed` 承载；`settings` 与 `seaLevel` 由调用方保证；`PS5` 不延伸至本入口。
> **实现现状**：V1 **查询接口**已实现并通过 Unity 2022.3.62f1c1 项目编译（阶段 A/B）；**骨架生成层**（大陆/掩码/气候带/`Slope`）、Region 接线、多大陆仍**未实现**。
