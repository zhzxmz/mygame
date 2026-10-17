# World V2 — Legacy Status Declaration

> **文档性质**：**状态声明（Status Declaration）**。本文档声明 **旧 Terrain / Voxel 架构（Legacy）退出主线**，并冻结其状态、范围、退出理由、替代关系、迁移政策与删除时机。
> **仅创建 Markdown**；**不修改任何 `Assets` 文件**；**不修改任何已有规范**；**不实现任何代码**；**不删除任何代码**。
>
> **一句话声明**：
>
> > **Legacy（旧 Terrain / Voxel 架构）自本文档起退出主线：冻结、仅供历史参考、不得成为 V2 依赖；删除时机为 V2 稳定之后。**
>
> **本文件不裁决的事项（明确排除）**：
> * 不裁决审查报告 `Specifications/Audit/WorldArchitecture_CrossDocument_Audit_V1.md` 的 **A-1**（体素真值写入者归属）与 **A-3**（Phase 编号双轨）；
> * 不执行任何**场景切换**、**代码删除**、**文件移动**或**规范修改**；
> * 不定义任何 V2 规则、参数、因果边或接口；
> * 不评价 Legacy 代码质量（除本文档 §3 列出的**架构性**理由外）。
>
> **与既有审查的关系**：本文档对应审查报告的 **A-2 / CD-02**（Terrain 真值双轨）与 **B-8 / CD-12**（旧栈遗留文档与验证入口）——本文件只声明**方向**（Legacy 退出主线），**不解除**任何 Blocker，也不提供技术方案。

---

## §1 Status

| # | 条款 |
|---|---|
| **LS1** | **Legacy 定义**：Legacy = **旧 Terrain / Voxel 架构整体**（旧代码栈 + 旧规范 + 旧验证入口 + 场景中的旧世界挂载），即 `Assets/Scripts/Terrain/**` 与其配套规范/验证集合（清单见 §2 与附录 B） |
| **LS2** | **状态值（冻结）**：Legacy = **`LEGACY-FROZEN`**（冻结）+ **`OUT-OF-MAINLINE`**（退出主线）+ **`READ-ONLY-REFERENCE`**（只读参考）+ **`DELETION-CANDIDATE`**（删除候选，时机见 §7） |
| **LS3** | **冻结含义**：Legacy **不接受任何新功能**（不新增规则、不新增参数、不新增坐标语义、不新增验证入口、不扩展现有能力） |
| **LS4** | **退出主线含义**：所有**未来开发方向**只在 **World V2**（§4）；Legacy 不再参与任何新特性的设计与验收 |
| **LS5** | **只读参考含义**：Legacy 内容可作为**历史参考**被阅读（规则见 §6），但**不得**作为规范权威、不得作为 V2 依据 |
| **LS6** | **删除候选含义**：Legacy 是未来删除对象，但**删除时机为 V2 稳定之后**（判据见 §7）；**本文档不执行删除** |
| **LS7** | **代码保留**：本文档**不删除、不停用、不移动**任何 Legacy 代码；下发时 Legacy 文件数量与哈希不变（附录 B） |
| **LS8** | **规范保留**：4 份 Legacy 规范（`Specifications/Chunk.md` / `CoordinateMigration.md` / `Mining.md` / `VoxelAwareMesh.md`）**保留原位、不修改**；其"历史参考"身份由**本文档**声明，而非修改原文件 |
| **LS9** | **验证保留**：34 个旧栈验证入口**保持原状**（不删、不改）；V2 的验证入口为**唯一新功能门禁**（§5） |
| **LS10** | **场景现状记录（事实）**：`Assets/Scenes/SampleScene.unity` 仍挂载旧世界（`DensityTerrain`，guid `3af3bcfc875f4cbfae75f1f95dee772a` 出现 1 次），而 V2 组装点（`VoxelWorldStreamingBootstrap`，guid `2c531141a81459d499f3f2959fe28988`）出现 0 次 —— **本文档不改场景**，接入/切换属未来决策 |
| **LS11** | **安全修复例外**：若 Legacy 缺陷**阻塞 V2 接入**或导致崩溃，允许做**最小安全修复**，但必须：① 显式授权；② 逐次记录（改了什么、为什么）；③ 不改变 Legacy 的规则语义与坐标语义 |
| **LS12** | **本文档的效果边界**：本声明**不产生运行时效果**、不改变任何体素输出、不改变既有验收结论；它只改变**开发主线归属** |

---

## §2 Scope

### 2.1 范围内（Legacy）

| # | 条款 |
|---|---|
| **LS13** | **Legacy 代码**：`Assets/Scripts/Terrain/**` —— **29 个 `.cs` / 10,615 行**（完整清单与 SHA16 见附录 B.1） |
| **LS14** | **Legacy 规范**：`Specifications/Chunk.md`（935 行，`E401E4F52AFF6C27`）、`Specifications/CoordinateMigration.md`（366 行，`45AB2D33FAE1C558`）、`Specifications/Mining.md`（720 行，`E600CFBB4A1F15AE`）、`Specifications/VoxelAwareMesh.md`（2089 行，`675053608E47161E`）—— 共 **4 份 / 4,110 行**（名单见附录 B.2） |
| **LS15** | **Legacy 验证入口**：`Assets/Editor/` 下 **34 个**旧栈 `*Validation*.cs`（完整名单见附录 B.3） |
| **LS16** | **Legacy 运行角色**：场景中由 `DensityTerrain` 承载的旧世界运行角色（`LS10` 的事实） |
| **LS17** | **Legacy 关联产物**：与上述代码/规范直接配套的旧世界资产与调试入口（以场景引用与验证入口引用为界）——**[待确认]**：完整资产清单未在本文档核定（本文档不枚举未核验项） |

### 2.2 范围外（不属于 Legacy）

| # | 条款 |
|---|---|
| **LS18** | **V2 代码**：`Assets/Scripts/VoxelWorld/**`（VoxelCore / Generation / Geology / Runtime / Streaming / Mesh / View）+ `Assets/Scripts/Game/VoxelWorldStreamingBootstrap.cs` —— 属 **V2**（关键文件与 SHA16 见附录 C.1） |
| **LS19** | **V2 规范**：`Specifications/World/**` 全部文档（含 `VoxelWorld_Core.md`、`WorldGeneration_Core.md`、`VoxelWorld_Mining.md`、V2 契约族）—— **不是 Legacy**；特别注意：`Specifications/World/VoxelWorld_Mining.md`（V2）与 `Specifications/Mining.md`（Legacy）是**两份不同文档** |
| **LS20** | **V2 验证入口**：11 个（名单见附录 C.2）—— 属 V2，是新功能门禁 |
| **LS21** | **ADR-001**（`Specifications/ArchitectureDecisionWorldSpaceEntity.txt`）：概念边界**仍然有效**，**不属 Legacy**，不得随 Legacy 一起弱化 |
| **LS22** | **VoxelCore 名称澄清**：`VoxelType` / `Voxel` / `VoxelCoordinate` / `VoxelChunk` / `VoxelWorld` 虽含 "Voxel"，但属 **V2**（`Specifications/World/VoxelWorld_Core.md`），**不得**被误判为 Legacy |

### 2.3 边界判定与稳定性

| # | 条款 |
|---|---|
| **LS23** | **判定判据（只读）**：一个文件属 Legacy，当且仅当满足任一条：① 位于 `Assets/Scripts/Terrain/**`；② 属于 `LS14` 的 4 份规范；③ 属于 `LS15` 的 34 个验证入口名单 |
| **LS24** | **判定不改文件**：判据只用于分类与扫描；**不得**为了"标记 Legacy"而修改文件名、命名空间、注释或元数据 |
| **LS25** | **不得自动扩张**：新增文件**不会**自动成为 Legacy；Legacy 集合的增删必须修改本文档（升版本） |
| **LS26** | **物理共存允许**：Legacy 与 V2 可同时在仓库与场景中存在；本声明**不要求**立即移除任何内容 |
| **LS27** | **范围以基线为准**：Legacy 范围与**附录 B** 的基线（29 + 4 + 34）一致；基线漂移（文件被改动）必须记录 |
| **LS28** | **声明性**：本文档的 Scope 只用于治理与扫描，不产生任何运行时或构建效果 |

---

## §3 Reason

> 说明：以下理由均为**架构性与治理性**理由，且每条都有既有审查/文档证据；**不评价代码质量**。

| # | 退出理由 | 证据 |
|---|---|---|
| **LS29** | **Terrain 真值双轨**：项目层同时存在两套地形真值（旧 `DensityTerrain` 运行中、V2 `VoxelWorld` 未接入场景），与 V2 契约"Voxel Data 是世界唯一真值"的主张不兼容 | 审查 **A-2 / CD-02**；`VoxelWorld_Core.md`:11；场景 GUID 核验（`LS10`） |
| **LS30** | **坐标契约双轨**：旧栈以 **Terrain Local + `voxelSize` + 可配置 `chunkVoxelSize`** 为坐标基准；V2 以**整数世界坐标 + 固定 16³ Chunk**为基准 —— 两套契约无法同时作为规则输入 | `ChunkSpace.cs:13/25/38/54`；`DensityTerrain.cs:16-37/177`；`VoxelChunk.cs:18/21`；`VoxelWorld.cs:47/60/71` |
| **LS31** | **地形规则双权威**：旧栈自带密度/材料规则体系（`HeightField` / `ThreeDimensional` / `MaterialV2` 等模式），与 V2 的地质判定链（P0→P1→P2→P3）互不相容；并存会产生"同一坐标两种结论" | `DensityFieldGenerator.cs:17/52/56/78/93`；`Geology_Architecture_Contract.md` `GC63`/`GC71` |
| **LS32** | **验证门禁分裂**：45 个 `*Validation*.cs` = V2 11 + Legacy 34；若两套并存为门禁，则"通过"无法代表 V2 可用 | 附录 B.3 / C.2 |
| **LS33** | **确定性纪律成本**：V2 要求"世界坐标纯函数 + 域分离 + 无 Transform 依赖"；旧栈以 Transform/局部空间为入口，长期维持两套确定性纪律的成本不可控 | `WorldGeneration_Geology.md` §2.1/§2.3（`F1`–`F4`）；`D1`–`D6` |
| **LS34** | **概念收敛**：V2 + ADR-001 已确定"Chunk = 空间分区/采样窗口、Terrain = Space 的空间数据、Entity 不属 Chunk 所有权"；旧栈把 Chunk 当作数据容器与网格单位的用法与之一致性不足 | `ADR-001`；`VoxelWorld_Architecture.md`；`GC55` |
| **LS35** | **主线复杂度**：不退出主线则任何 V2 接入都必须同时维护两套坐标、两套规则、两套验证 —— 这正是审查报告判定"实现必然推翻架构"的场景 | 审查 **A-1 / A-2** |
| **LS36** | **替代物已具备最小实现**：V2 已存在 VoxelCore、地质（Stage A–E）、Runtime Core、Streaming、Mesh、View、Bootstrap，并在批处理中通过 12 项验证 —— 退出主线具备可行性 | 附录 C；审查 **§7.2**（已验证事实） |
| **LS37** | **判定方向明确**：V2 契约族已冻结"单一权威、单一真值、只读查询、无反向边"等边界，可作为唯一未来开发依据 | `Specifications/World/**`（V2 契约族） |
| **LS38** | **代价可控**：Legacy 共 10,615 行代码 + 4,110 行规范；其功能位在 V2 中已有对应物（§4 映射表），缺口已被登记为待做项而非"必须保留 Legacy"的理由 | 附录 A / `LS50`–`LS52` |
| **LS39** | **不构成理由的事项（明确排除）**：本声明**不因**代码质量、性能、风格或个人偏好而要求退出主线 | — |
| **LS40** | **性质澄清**：这是**主线归属**决策，**不是删除决策**；删除的时机与前提见 §7 | `LS6` |

---

## §4 Replacement

| # | 条款 |
|---|---|
| **LS41** | **唯一方向（冻结）**：**World V2 = 唯一未来开发方向**。所有新功能、新规则、新参数、新契约、新验证入口**只允许进入 V2** |
| **LS42** | **V2 的构成**：VoxelCore（`VoxelType`/`Voxel`/`VoxelCoordinate`/`VoxelChunk`/`VoxelWorld`）+ Generation（`HeightFunction`/`GeologySampler`/`GeologyWorldGenerator`）+ Runtime Core + Streaming + Mesh + View + Bootstrap + `Specifications/World/**` 契约族 |
| **LS43** | **Legacy → V2 生态位映射**（逐位对照见附录 A）：旧密度/地形生成 → V2 地质判定 + 体素化；旧坐标工具 → V2 整数世界坐标工具；旧 Chunk/网格/视图 → V2 Chunk/Mesh/View；旧运行时修改与查询 → V2 对应契约（**部分尚未定义**） |
| **LS44** | **V2 现有能力（已具备）**：VoxelCore（4 种体素状态冻结）、地质（Stage A–E：地层/洞穴/矿藏属性层/群系接口）、Runtime（生命周期/安装/脏/重建）、Streaming、Mesh、View、组装点 |
| **LS45** | **V2 缺口（登记，不设计）**：WorldState / Region / Biome / Ecology / Resource / WorldQuery 的**实现**；**Runtime Modification 边界**；**场景接入**；**Phase 编号统一**（审查 A-3）；**体素写入者裁决**（审查 A-1） |
| **LS46** | **缺口处理纪律**：V2 缺口必须由 **V2 未来文档/实现**补齐；**禁止**用 Legacy 填补缺口（例如"暂时调用旧查询""暂时复用旧坐标换算"） |
| **LS47** | **Legacy 不是参考实现**：Legacy **可以阅读**（§6），但**不得**把其规则、阈值、坐标约定、数据结构复制进 V2 |
| **LS48** | **门禁归属**：V2 的验证入口（附录 C.2，11 个）为**唯一新功能门禁**；Legacy 的 34 个入口不承担新功能验收职责 |
| **LS49** | **命名与发布**：新功能一律以 **World V2**（及其契约族名）命名与发布；**不得**以 Legacy 名称发布新功能、不得让 Legacy 名称出现在 V2 的权威链中 |
| **LS50** | **权威链不变**：V2 的权威链保持为 `VoxelWorld_Core.md` → `WorldGeneration_Core.md` → V2 契约族 → 实现；Legacy 规范**不在任何权威链上** |
| **LS51** | **替换完成判据**：附录 A 的每一行迁移状态变为"已替代"（或明确"不迁移"），且 §7 的"V2 稳定"判据全部满足 |
| **LS52** | **本声明不等于 V2 完成**：V2 仍是"唯一方向 + 进行中"；本文件不暗示 V2 已可替代 Legacy 的运行角色（场景仍挂载 Legacy，`LS10`） |

---

## §5 Migration Policy

| # | 条款 |
|---|---|
| **LS53** | **单向原则**：迁移只允许 **Legacy → V2**；**禁止反向**（V2 → Legacy） |
| **LS54** | **禁止 V2 依赖 Legacy（硬性）**：V2 代码**不得**引用 Legacy 命名空间/类型/资产；V2 文档**不得**把 Legacy 作为权威依据；二者**不得**共享可变状态 |
| **LS55** | **禁止兜底（failover）**：不得在 V2 缺口处"回退到 Legacy 行为"（例如缺省调用旧生成器、旧查询、旧坐标换算） |
| **LS56** | **跨栈只读边界（例外，需登记）**：若工具/调试**必须**读取 Legacy（例如对比历史数据），只允许**只读**、且必须显式登记为"工具用途"，**不得**成为 V2 的结构依赖 |
| **LS57** | **禁止在 Legacy 上开发新功能**：Legacy 冻结（`LS3`）；仅 `LS11` 的安全修复例外，且不得改变规则/坐标语义 |
| **LS58** | **迁移清单**：附录 A 必须逐项记录 `已替代 / 未替代 / 不迁移`；**存在"未替代"项时不得删除 Legacy** |
| **LS59** | **验证政策**：Legacy 的 34 个验证入口**保持原状**（不删不改）；V2 门禁为附录 C.2；跨栈对比数据（如体素快照）**不得**作为 V2 的权威真值 |
| **LS60** | **场景政策**：场景挂载切换属未来接入决策（审查 `AD-1` / `M7`）；本文件只声明**目标方向是 V2**，不规定切换方式与时间 |
| **LS61** | **文档政策**：Legacy 规范保留原位不改（`LS8`）；V2 新文档**不得**引用 Legacy 规范作为权威（如需提及，必须标注"历史参考、非权威"） |
| **LS62** | **数据政策**：旧世界数据/存档与 V2 世界数据的关系**未定义** —— 登记为缺口（**[待确认]**），**本文件不裁决** |
| **LS63** | **审计联动**：本文件对应审查 `A-2 / CD-02`（双真值）与 `B-8 / CD-12`（旧栈遗留）；**本文件不解除** `A-1`、`A-3` 及其它 Warning |
| **LS64** | **违规判定**：任何 **V2 → Legacy 依赖**、任何 **Legacy 新功能**、任何**跨栈写交互**，一律判为违规（可机械扫描） |
| **LS65** | **扫描口径（可核验）**：① `Assets/Scripts/VoxelWorld/**` 与 `Assets/Scripts/Game/VoxelWorldStreamingBootstrap.cs` 中不得出现 Legacy 命名空间/类型/路径引用；② Legacy 文件集合的哈希在"未授权"情况下必须不变；③ Legacy 验证入口数量与名单不变 |
| **LS66** | **例外登记格式**：任何例外（`LS11`/`LS56`）必须记录：原因、范围、期限、影响面、是否可回退 |
| **LS67** | **职责边界**：本政策**不规定实现方式**（不改代码、不加适配层、不写迁移脚本）；实现层迁移方案属未来"实现边界规划" |
| **LS68** | **政策稳定性**：本政策的变更必须修改本文档并升版本；不得以"临时需要"为由默认放宽 `LS54`/`LS55` |

**禁止清单（LSX）**

| # | 禁止项 | 反例（必须判为违规） | 依据 |
|---|---|---|---|
| **LSX-1** | V2 引用 Legacy 类型/命名空间 | V2 生成器调用 `DensityTerrain`/`ChunkSpace` | `LS54` |
| **LSX-2** | V2 文档把 Legacy 当权威 | V2 契约引用 `Specifications/Mining.md` 作为规则依据 | `LS54`/`LS61` |
| **LSX-3** | Legacy 兜底 | V2 缺失时回退旧生成器 | `LS55` |
| **LSX-4** | 共享可变状态 | Legacy 与 V2 共用同一体素数组/编辑层 | `LS54` |
| **LSX-5** | 跨栈写交互 | V2 写 Legacy 的字段/资产 | `LS56` |
| **LSX-6** | Legacy 新功能 | 在旧栈新增矿物/群系/参数 | `LS57` |
| **LSX-7** | 复制 Legacy 代码进 V2 | 改名复制 `VoxelAwareMesh*` 逻辑 | `LS47` |
| **LSX-8** | 复制 Legacy 规则/阈值 | 把旧密度公式搬进 V2 契约 | `LS47` |
| **LSX-9** | 反向迁移 | 用 V2 语义改造 Legacy 使其成为依赖 | `LS53` |
| **LSX-10** | 未裁决即删除 Legacy | 在无显式授权下删除旧文件/规范 | `LS6`/`LS86` |
| **LSX-11** | 把 Legacy 命名引入 V2 | V2 契约采用 Terrain Local/`voxelSize` 等旧术语 | `LS49`/`LS65` |
| **LSX-12** | 以本文件为技术方案 | 用本声明替代接入/迁移设计 | 文首"不裁决"声明 |

---

## §6 Legacy as Historical Reference

| # | 条款 |
|---|---|
| **LS69** | **允许**：阅读、考古、对照、理解历史设计取舍、撰写历史说明 |
| **LS70** | **禁止**：把 Legacy 内容当作规范权威（`LS54`/`LS61`） |
| **LS71** | **禁止复制**：不得把 Legacy 代码（含重命名/重构后的等价物）或规则参数复制进 V2 |
| **LS72** | **引用标注**：V2 文档中若必须提及 Legacy，必须显式标注"**历史参考、非权威**" |
| **LS73** | **术语隔离**：Legacy 术语（Terrain Local、`voxelSize`、`DensityField`、Edit Layer 等）**不得**成为 V2 契约术语 |
| **LS74** | **无维护承诺**：Legacy 作为历史参考**不承诺**与未来变更同步（不修、不补、不更新） |
| **LS75** | **保持原样**：不得为"看起来更清晰"而重命名/重构/重排 Legacy 文件（避免无谓改动与哈希漂移） |
| **LS76** | **参考不等于正当性**：历史参考不构成"必须保留 Legacy 行为"的技术理由（缺口一律按 `LS46` 处理） |

---

## §7 Deletion Timing

| # | 条款 |
|---|---|
| **LS77** | **删除时机原则（冻结）**：**只有 V2 稳定后**才删除 Legacy；在此之前 Legacy 只冻结、不删除（`LS6`/`LS7`） |
| **LS78** | **"V2 稳定"判据（全部满足才算稳定）**：① V2 已在场景中**接入并可运行**，替代旧世界的运行角色；② V2 验证入口（含既有 12 项与未来新增）**持续 PASS**；③ 附录 A 迁移清单**无"未替代"项**（或已明确"不迁移"）；④ **V2 → Legacy 依赖 = 0**（机械扫描，`LS65`）；⑤ V2 缺口（`LS45`）全部关闭或明确不迁移 |
| **LS79** | **删除前前置条件**：① 迁移清单清零；② 审查 `A-1`/`A-2`/`A-3` 已裁决且有对应文档；③ 备份/分支就绪；④ 删除范围清单已核定（含资产与场景引用） |
| **LS80** | **删除范围（届时）**：Legacy 代码 29 文件、Legacy 规范 4 文件、Legacy 验证入口 34 个、场景中的旧世界挂载与关联资产 |
| **LS81** | **删除顺序（届时）**：① 标记 deprecated（本文件即声明）→ ② 停止在场景中运行（切换完成后自然达成）→ ③ 冻结/停用 Legacy 验证入口 → ④ 删除代码与资产 → ⑤ **归档优先**处理规范 |
| **LS82** | **归档优先**：Legacy 规范建议**归档**（移至归档位置）而非直接删除；归档位置与流程由届时决策确定（**本文件不指定路径**） |
| **LS83** | **不可逆性提示**：删除后不再作为参考；因此 `LS82` 的归档优先是推荐顺序 |
| **LS84** | **删除不得破坏 V2**：删除前必须确认无 V2 引用（`LS65` 扫描 = 0） |
| **LS85** | **删除不得影响既有基线**：若某 Legacy 验证入口仍被共享工具引用，必须先解耦并登记（`LS58`） |
| **LS86** | **删除需显式授权**：删除动作必须作为**独立任务**执行，并经用户明确指令；**本文件不授权、不执行**任何删除 |
| **LS87** | **不承诺时间**：本文件**不给出版本号/日期**（未定义即不裁决） |
| **LS88** | **部分删除**：允许按"生态位"分批删除（例如某一位已被 V2 完全替代且无引用）；每一批都必须满足 `LS78`–`LS86` |
| **LS89** | **回退预案**：删除批次必须可回退（分支/标签/备份），直到 V2 运行稳定被确认 |
| **LS90** | **状态终结**：全部删除完成后，本文件升级为 **`LEGACY-REMOVED`** 状态（或由后续文档取代）；升级方式由届时任务决定 |
| **LS91** | **中途失败处理**：若某批删除后发现 V2 回归，**立即停止后续删除**并回退该批次；把失败登记为 V2 缺口（`LS46`） |
| **LS92** | **本文件不改变审查结论**：删除完成不自动解除审查中的其它 Blocker/Warning（`LS63`） |

---

## §8 Verification

| # | 不变量 | 验证方法 |
|---|---|---|
| **LSV1** | **唯一新增**：本任务只新增本文档，未修改任何已有文件 | `git status` + 被审文件 SHA16 复核 |
| **LSV2** | **Assets 未修改**：本次任务期间 `Assets/**` 写入 = 0 | 文件 mtime 扫描 |
| **LSV3** | **无代码删除**：Legacy 29 文件仍存在且 SHA16 与附录 B.1 一致 | 文件哈希比对 |
| **LSV4** | **Legacy 清单完整**：29 代码 + 4 规范 + 34 验证入口 | 清单核对 |
| **LSV5** | **V2 基线未变**：附录 C.1 的 V2 关键文件 SHA16 不变 | 文件哈希比对 |
| **LSV6** | **无 V2 → Legacy 依赖新增**：V2 目录内 Legacy 引用 = 0 | 结构扫描（命名空间/类型/路径） |
| **LSV7** | **本文件不定义规则**：不含阈值/公式/参数/因果边/接口符号 | 结构检查 |
| **LSV8** | **声明性**：开关本文档不影响任何运行时行为与体素输出 | 结构性论证 + 无代码参与 |
| **LSV9** | **迁移清单可核对**：附录 A 每行都有迁移状态 | 表格完整性检查 |
| **LSV10** | **删除判据可核验**：`LS78` 五条判据均可机械检查 | 检查项清单（`LS65`） |
| **LSV11** | **术语隔离**：本文档未把 Legacy 术语用作 V2 术语 | 术语扫描（`LS73`） |
| **LSV12** | **不解除 Blocker**：本文档明确不裁决 `A-1`/`A-3`、不解除任何 Warning | 文首声明与 `LS63`/`LS92` 复核 |

---

## 附录 A：Legacy ↔ V2 生态位对照（迁移清单）

> 迁移状态取值：**已替代**（V2 已有对应实现且可用）/ **部分**（V2 有骨架但未接入/未完成）/ **未替代**（V2 尚无对应物）/ **不迁移**（明确不需要）。

| # | Legacy 生态位（文件） | V2 对应物 | 迁移状态 | 备注 |
|---|---|---|---|---|
| 1 | `DensityTerrain.cs`（1855 行，`0EB935DBA79CBD40`） | `VoxelRuntimeCore` + `GeologyWorldGenerator` + `ChunkStreamingDriver` + Bootstrap | **部分** | V2 已具备，但**未接入场景**（`LS10`） |
| 2 | `DensityFieldGenerator.cs`（556，`702BEFA889002006`） | `GeologySampler`（P0–P3）+ `HeightFunction` | **已替代** | 规则权威为 V2 地质契约 |
| 3 | `DensityField.cs` / `MaterialField.cs` / `MaterialType.cs`（87/84/7） | `VoxelChunk`（体素状态）+ `VoxelType`（4 类冻结） | **已替代** | 体素状态集合由 VoxelCore 冻结 |
| 4 | `ChunkSpace.cs`（160，`219FCBB569A413FB`） | `VoxelWorld.WorldToChunkCoordinate` / `FloorDiv` / `WorldToLocalCoordinate` | **已替代** | 坐标语义变更：Terrain Local → 整数世界坐标 |
| 5 | `TerrainChunk.cs`（303） | `VoxelChunk`（16³ / 4096） | **已替代** | — |
| 6 | `TerrainMeshGenerator.cs`（413） | `VoxelMeshGenerator` + `UnityMeshBuilder` | **已替代** | — |
| 7 | `TerrainChunkView.cs`（49） | `VoxelChunkView` + `ChunkViewPresenter` | **已替代** | — |
| 8 | `TerrainEffectiveTerrainQueryV2.cs`（76，`6D66D4CF9474BCA7`） | V2 WorldQuery 契约（`WorldQuery_Design.md` / `WorldQuery_Architecture_Contract.md`） | **部分** | 契约已冻结；**查询实现未建立** |
| 9 | `TerrainModification.cs` / `TerrainModificationShape*.cs` / `TerrainMining.cs`（445/86/96/47） | `VoxelWorld.SetVoxel` + V2 `VoxelWorld_Mining.md` | **部分** | 体素写入 API 已有；**Runtime Modification 边界未定义** |
| 10 | `VoxelAwareMesh*`（Precut 1090 / RuntimeMutation 1890 / SpatialFace 1829） | V2 Mesh（`VoxelMeshGenerator` + 派生表示） | **部分** | V2 用"从体素派生"替代增量面修改；**增量修改能力未迁移** |
| 11 | `TerrainEditGrid.cs` / `TerrainEditLayer.cs` / `TerrainEditRecordV2.cs`（88/85/56） | **未定义**（Runtime Modification Layer） | **未替代** | 审查 `B-1`（Missing Boundary） |
| 12 | `TerrainVoxelSelector.cs` / `TerrainVoxelHighlight.cs`（256/367） | **未定义**（交互层） | **未替代** | 属未来交互/工具设计 |
| 13 | `TerrainEffective*Evaluator` / `*Sampling` / `*Enumerator`（66/35/145/123） | `GeologySampler` 点采样 | **已替代** | 点查询能力已在 V2 地质层具备 |
| 14 | `VoxelDensityAdapter.cs` / `VoxelField.cs`（122/175） | `VoxelChunk` 数据面 + `GeologySample` | **已替代** | — |
| 15 | `SpatialFaceDebugSettings.cs`（24） | — | **不迁移** | 旧调试专用 |
| 16 | Legacy 规范 4 份（4,110 行） | V2 契约族（`Specifications/World/**`） | **已替代**（作为规范） | Legacy 规范转为历史参考（`LS8`/`LS61`） |
| 17 | Legacy 验证入口 34 个 | V2 验证入口 11 个（附录 C.2） | **部分** | V2 入口未覆盖旧栈全部功能位（如 Edit Layer、Selector） |

**清单汇总**

| 迁移状态 | 行数 |
|---|---|
| 已替代 | 9 |
| 部分 | 5 |
| 未替代 | 2 |
| 不迁移 | 1 |
| **合计** | **17** |

---

## 附录 B：Legacy 基线

### B.1 Legacy 代码（`Assets/Scripts/Terrain/**`，29 文件 / 10,615 行）

| 文件 | 行数 | SHA16 |
|---|---|---|
| `ChunkSpace.cs` | 160 | `219FCBB569A413FB` |
| `DensityField.cs` | 87 | `497A5884A21A0822` |
| `DensityFieldGenerator.cs` | 556 | `702BEFA889002006` |
| `DensityTerrain.cs` | 1855 | `0EB935DBA79CBD40` |
| `MaterialField.cs` | 84 | `4420F4D58E141524` |
| `MaterialType.cs` | 7 | `99208CB5ED596A98` |
| `SpatialFaceDebugSettings.cs` | 24 | `13D6CA42B3CC2B3C` |
| `TerrainChunk.cs` | 303 | `52AE93A43B97E79B` |
| `TerrainChunkView.cs` | 49 | `76A9685FC1E3E6B4` |
| `TerrainEditGrid.cs` | 88 | `C1F2221620492CAC` |
| `TerrainEditLayer.cs` | 85 | `C4C56D1508FBF62A` |
| `TerrainEditRecordV2.cs` | 56 | `655890B358FBD226` |
| `TerrainEffectiveDensityEvaluator.cs` | 66 | `E4CC0E008A95CA96` |
| `TerrainEffectiveFieldEvaluatorV2.cs` | 35 | `CB6A7BFEDB1CD9F9` |
| `TerrainEffectiveSurfaceSampleEnumerator.cs` | 123 | `2B971A6EA89A06F5` |
| `TerrainEffectiveSurfaceSampling.cs` | 145 | `D7F521052044CF9F` |
| `TerrainEffectiveTerrainQueryV2.cs` | 76 | `6D66D4CF9474BCA7` |
| `TerrainMeshGenerator.cs` | 413 | `00DC3191AB8BA5FA` |
| `TerrainMining.cs` | 47 | `E802CBA26C507620` |
| `TerrainModification.cs` | 445 | `47925DE263BF15F4` |
| `TerrainModificationShape.cs` | 86 | `753ED5B99527782F` |
| `TerrainModificationShapeV2.cs` | 96 | `841CABD55F5B01E8` |
| `TerrainVoxelHighlight.cs` | 367 | `B3DFFEB6C94BECBA` |
| `TerrainVoxelSelector.cs` | 256 | `35D62EEEA13D3CDD` |
| `VoxelAwareMeshPrecut.cs` | 1090 | `5DD248CA0E593FEF` |
| `VoxelAwareMeshRuntimeMutation.cs` | 1890 | `479E8655D5F0F7CE` |
| `VoxelAwareMeshSpatialFace.cs` | 1829 | `A5968958A5C71821` |
| `VoxelDensityAdapter.cs` | 122 | `CF35963093786C15` |
| `VoxelField.cs` | 175 | `253DCBDE4202B2BB` |

### B.2 Legacy 规范（4 份 / 4,110 行）

| 文件 | 行数 | SHA16 |
|---|---|---|
| `Specifications/Chunk.md` | 935 | `E401E4F52AFF6C27` |
| `Specifications/CoordinateMigration.md` | 366 | `45AB2D33FAE1C558` |
| `Specifications/Mining.md` | 720 | `E600CFBB4A1F15AE` |
| `Specifications/VoxelAwareMesh.md` | 2089 | `675053608E47161E` |

### B.3 Legacy 验证入口（34 个，名单即基线）

```
DensityTerrainVoxelFieldOwnershipValidation.cs        MiningSurfaceDiagnosticValidation.cs
MiningSurfaceIntegrityValidation.cs                   PlayerLookStabilityValidation.cs
SpatialFaceConservativeHiddenFaceValidation.cs        TerrainModificationV2ChunkIntegrationValidation.cs
TerrainModificationV2EditGridValidation.cs            TerrainModificationV2EditLayerValidation.cs
TerrainModificationV2EffectiveDensityValidation.cs    TerrainModificationV2EffectiveFieldValidation.cs
TerrainModificationV2EffectiveSurfaceSampleEnumeratorValidation.cs
TerrainModificationV2EffectiveSurfaceSamplingValidation.cs
TerrainModificationV2EffectiveTerrainQueryValidation.cs
TerrainModificationV2MiningValidation.cs              TerrainModificationV2Validation.cs
TerrainModificationV2VoxelFieldValidation.cs          TerrainMultiChunkValidation.cs
TerrainVoxelHighlightValidation.cs                    TerrainVoxelMeshIntegrationValidation.cs
TerrainVoxelMiningValidation.cs                       TerrainVoxelSelectorValidation.cs
VoxelAwareMeshGeometryPrototypeValidation.cs          VoxelAwareMeshRuntimePhase1Validation.cs
VoxelAwareMeshRuntimePhase2AValidation.cs             VoxelAwareMeshRuntimePhase2BValidation.cs
VoxelAwareMeshRuntimePhase3Validation.cs              VoxelCoordinateMigrationSchemeAValidation.cs
VoxelCoordinateMigrationStageB1Validation.cs          VoxelCoordinateMigrationStageB2Validation.cs
VoxelCoordinateMigrationStageB3AValidation.cs         VoxelCoordinateMigrationStageB3BValidation.cs
VoxelCoordinateMigrationStageB3CValidation.cs         VoxelDensityAdapterValidation.cs
VoxelMarchingCubesGeometryValidation.cs
```

> 说明：本表以**名单**为基线（34 个）；逐个 SHA16 未在本轮登记（如需，删除前按 `LS79` 核定）。

---

## 附录 C：V2 基线（范围外参照）

### C.1 V2 关键实现文件（SHA16）

| 文件 | 行数 | SHA16 |
|---|---|---|
| `Assets/Scripts/VoxelWorld/VoxelWorld.cs` | 258 | `D9E749A466635918` |
| `Assets/Scripts/VoxelWorld/VoxelChunk.cs` | 173 | `4DBDFBAC5BCE500C` |
| `Assets/Scripts/VoxelWorld/Generation/Geology/GeologySampler.cs` | 350 | `62836145F8F3E212` |
| `Assets/Scripts/VoxelWorld/Generation/Geology/GeologyWorldGenerator.cs` | 276 | `73EB59CFB939DC96` |
| `Assets/Scripts/VoxelWorld/Mesh/VoxelMeshGenerator.cs` | 242 | `653804C19C272C72` |
| `Assets/Scripts/VoxelWorld/View/ChunkViewPresenter.cs` | 135 | `C6346E735591E2BF` |
| `Assets/Scripts/VoxelWorld/Runtime/VoxelRuntimeCore.cs` | 980 | `9119E8D3292A4C71` |
| `Assets/Scripts/VoxelWorld/Runtime/Streaming/ChunkStreamingDriver.cs` | 312 | `26DF1B6B22E2DFF3` |
| `Assets/Scripts/Game/VoxelWorldStreamingBootstrap.cs` | 392 | `4738634952568520` |

### C.2 V2 验证入口（11 个，名单即门禁基线）

```
VoxelWorldCoreValidation.cs              WorldGenerationCoreValidation.cs
VoxelMeshCoreValidation.cs               VoxelWorldViewValidation.cs
VoxelWorldRuntimeCoreValidation.cs       ChunkStreamingValidation.cs
GeologyStageAValidation.cs               GeologyStageBValidation.cs
GeologyStageCValidation.cs               GeologyStageDValidation.cs
GeologyRuntimeIntegrationValidation.cs
```

### C.3 V2 契约族（规范权威，非 Legacy）

`Specifications/World/**`：`VoxelWorld_Architecture.md` · `VoxelWorld_Core.md` · `VoxelWorld_Mesh.md` · `VoxelWorld_Mining.md` · `VoxelWorld_View.md` · `VoxelWorld_Runtime*.md` · `WorldGeneration_Core.md` · `WorldGeneration_Geology*.md` · `WorldState_Design.md` · `WorldParameter_Contract.md` · `WorldParameter_Runtime_Model.md` · `WorldGeneration_Pipeline_Design.md` · `WorldGeneration_Pipeline_Contract.md` · `Region_Design.md` · `Region_Architecture_Contract.md` · `Biome_Architecture_Contract.md` · `Geology_Architecture_Contract.md` · `Resource_Design.md` · `Resource_Architecture_Contract.md` · `Ecology_Design.md` · `WorldQuery_Design.md` · `WorldQuery_Architecture_Contract.md` · `WorldHistory_Design.md` · `WorldState_Runtime_Contract.md`
以及 `Specifications/Audit/WorldArchitecture_CrossDocument_Audit_V1.md`（审查报告，非规则权威）。

---

## 附录 D：术语与变更规则

### D.1 术语

| 术语 | 含义 |
|---|---|
| **Legacy** | 旧 Terrain / Voxel 架构整体（§2.1 范围） |
| **World V2 / V2** | 当前主线架构（VoxelCore + Generation + Runtime + Streaming + Mesh + View + 契约族） |
| **主线（Mainline）** | 唯一接收新功能的方向（当前 = V2） |
| **冻结（Frozen）** | 不接受新功能，仅 `LS11` 例外的安全修复 |
| **删除候选（Deletion Candidate）** | 未来删除对象，时机见 §7 |
| **历史参考（Historical Reference）** | 可读、不可依赖、不可复制（§6） |

### D.2 变更规则

1. 变更 Legacy 状态、范围、政策或删除判据，**必须**修改本文档并升版本。
2. **不得放宽**：`LS3`（冻结）、`LS6`（不删除直至 V2 稳定）、`LS54`（禁止 V2 依赖 Legacy）、`LS55`（禁止兜底）、`LS57`（禁止 Legacy 新功能）、`LS78`（V2 稳定判据）、`LS86`（删除需显式授权）。
3. 例外只允许通过 `LS11`/`LS56` 提出，并必须按 `LS66` 登记。
4. 本文档**不裁决**审查报告的 `A-1`/`A-3`，**不解除**任何 Blocker/Warning（`LS63`/`LS92`）。
5. 任何"删除 Legacy""切换场景""迁移实现"的提案，必须作为**独立任务**提出，并证明满足 §7 的前置条件；本文档不提供技术方案。
