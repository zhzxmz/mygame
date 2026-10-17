# World V2 Implementation Boundary Plan

> **文档性质**：**实现边界规划（Implementation Boundary Plan）**。本文档在 **Legacy 已冻结**（`Specifications/Architecture/Legacy_Status.md`）的前提下，定义 **World V2 第一阶段**的实现边界。
> **仅创建 Markdown**；**不修改任何 `Assets`**；**不新增任何 C#**；**不实现任何 Runtime 逻辑**；**不删除 Legacy 文件**；**不修改任何已有 Specification**。
>
> **本文档定义"哪里可以有新实现、哪里不可以"，不定义"怎么实现"**：
> * 不定义接口/方法/类型/数据结构；
> * 不定义算法、阈值、噪声、存储布局；
> * 不定义新的参数、新的因果边；
> * 不裁决审查报告中的未决 Blocker（见 §2.4）。
>
> **上游依据**：
>
> | 依据 | 用途 |
> |---|---|
> | `Specifications/Architecture/Legacy_Status.md`（`LS1`–`LS92`） | Legacy 冻结、退出主线、不得成为 V2 依赖、删除时机（V2 稳定后） |
> | `Specifications/Audit/WorldArchitecture_CrossDocument_Audit_V1.md`（`A-1`–`A-4` / `B-1`–`B-9` / `CD-01`–`CD-12`） | 跨文档冲突与缺口（本计划**只登记**，不裁决） |
> | `Specifications/World/VoxelWorld_Core.md`、`WorldGeneration_Core.md` | 体素真值与坐标/生成契约（V2 权威） |
> | `Specifications/World/WorldParameter_Contract.md`、`WorldParameter_Runtime_Model.md` | 参数身份/量纲/权限、三态模型与只读访问（V2 权威） |
> | `Specifications/World/WorldState_Runtime_Contract.md` | 参数最终求值契约（`Base + Modifier`、Generation/Runtime 分离、玩家隔离） |
> | `Specifications/World/Region_Design.md`、`Region_Architecture_Contract.md` | Region 结构、采纳流程与权限（V2 权威） |
> | `Specifications/World/WorldQuery_Design.md`、`WorldQuery_Architecture_Contract.md` | 点查询与解释契约（V2 权威） |
> | `Specifications/World/WorldGeneration_Pipeline_Contract.md` | 阶段顺序与依赖方向（`PC*` / `PCV*`） |
>
> **编号命名空间说明**：本文档使用 **`IBP1`–`IBP76`**、**`IBPV1`–`IBPV12`**、**`IBPX-1`–`IBPX-12`**，均为 **本计划专属**（不与既有文档冲突）。
> 本文档 §2/§6 中的"**Plan Phase 1…8**"是**实现阶段标签**，**不是** `WorldGeneration_Pipeline_Contract.md` 的 Pipeline 编号（`Phase 0`–`Phase 8`）——两者对应关系见**附录 A**（防新增命名冲突）。

---

## 1. V2 Implementation Principle

| # | 原则 |
|---|---|
| **IBP1** | **World V2 是唯一未来实现方向**：所有新实现、新契约、新验证入口只允许进入 V2（继承 `LS41`） |
| **IBP2** | **Legacy 不作为依赖**：V2 不得引用 Legacy 命名空间/类型/资产/规范（继承 `LS54`） |
| **IBP3** | **Legacy 不作为参考实现**：允许阅读（历史参考），但不得把其规则、阈值、坐标约定、数据结构作为实现依据（继承 `LS47`/`LS71`） |
| **IBP4** | **不迁移 Legacy 代码**：不做"改名复制""逐段搬运""逻辑等价重写以保留旧行为"（继承 `LSX-7`/`LSX-8`） |
| **IBP5** | **Legacy 只作为历史记录**：其存在只为考古与对照，不承担任何技术职责（继承 `LS5`/`LS69`） |
| **IBP6** | **不删除 Legacy**：本计划不删除、不停用、不移动任何 Legacy 文件（继承 `LS7`） |
| **IBP7** | **不修改既有契约**：本阶段的实现只能**遵守**已冻结契约；若契约不足，**停下来报告**，不得就地扩写（继承 `LS67` 与本文档 §7） |
| **IBP8** | **零新增**：本阶段不新增参数、不新增 `VoxelType`、不新增因果边（继承 `LS3`/`LS57`（冻结纪律）） |
| **IBP9** | **边界可核验**：本计划的每条允许/禁止都必须能被**机械检查**（文件扫描/清单比对/哈希比对），不依赖人工判断 |
| **IBP10** | **与既存 V2 模块的关系**：V2 已存在的地质/Mesh/View/Runtime/Streaming/Bootstrap 实现**不删除、不修改**；本阶段只是**不接入、不扩展、不依赖**它们作为阶段产物（详见 §2.3） |

---

## 2. First Implementation Boundary

### 2.1 只允许包含的五项（Plan Phase 1）

| # | 允许项 | 边界（能做什么 / 不能做什么） |
|---|---|---|
| **IBP11** | **World Seed** | ✅ 允许：接收种子并形成**不可变**的种子上下文；作为确定性输入与域分离输入（`WorldGeneration_Pipeline_Contract.md` Phase 0 / `PC11`–`PC14`）；**❌ 不允许**：由种子直接产生任何世界内容（骨架/区域/群系/地质/体素/网格） |
| **IBP12** | **Coordinate Contract** | ✅ 允许：**复用** V2 已冻结的坐标契约（整数世界坐标；`world = chunk × 16 + local`；负数使用 floor 语义；Chunk = 采样窗口）（`VoxelWorld_Core.md`、`WorldGeneration_Core.md`、`VoxelWorld.cs` 的换算面）；**❌ 不允许**：新建第二套坐标、引入 Terrain Local / `voxelSize` / 可配置 `chunkVoxelSize`、以 Chunk 编号作为规则输入 |
| **IBP13** | **World Parameter Evaluation** | ✅ 允许：对**已登记参数集合**（`WorldParameter_Contract.md` 附录 B 的环境参数组）求 **Base**，并在 Region 基础结构就位后合成 **Final**（两段式发布，`PC19`）；遵守量纲/范围/钳制/Owner 权限（`PW*`/`PM*`）；**❌ 不允许**：新增参数、改变量纲、引入第二套求值路径、读取 Biome/Geology/Ecology/Resource |
| **IBP14** | **Region 基础结构** | ✅ 允许：以既有 Region 契约的**字段与权限**为准，建立最小可用结构（区域身份、来源记录、修正侧写、只读发布的最小子集）；产物只作为参数修正的唯一写入来源（`RA75`）；**❌ 不允许**：完整采纳流程的边界（准入/上限/资格）、`RegionType` 规则化、区域级资格声明的完整推导；这些属 **Plan Phase 2**（§6） |
| **IBP15** | **单点查询** | ✅ 允许：**只读**点查询——给定世界坐标返回该点的 `WorldParameterState`（`Base` / `Modifier` / `Final`）与最小解释链（依据 `WorldQuery_Architecture_Contract.md` §3 的字段语义）；**❌ 不允许**：批量/范围/流式查询、查询结果回写、把查询结果作为生成输入（`WQ4`）、要求场景/Chunk/玩家存在 |

### 2.2 明确禁止（Plan Phase 1 范围内不得涉及）

| # | 禁止项 | 说明 |
|---|---|---|
| **IBP16** | **Mesh** | 不产出几何、不接入网格层（V2 既有 Mesh 模块保持现状，见 §2.3） |
| **IBP17** | **Terrain Rendering** | 不做任何渲染/表现/相机相关接入 |
| **IBP18** | **Mining** | 不定义挖掘/编辑语义，不接入 Mining 路径 |
| **IBP19** | **Resource Generation** | 不产出资源判定、不touch `DepositPotential` / `ResourcePresence` |
| **IBP20** | **Biome Rule** | 不定义/不接入群系分类规则（含 `Desert` 之类的分支问题，属既有规范的变更，不在本阶段） |
| **IBP21** | **Ecology** | 不产出生态候选、不接入生态层 |
| **IBP22** | **Runtime Editing** | 不做任何运行时修改（无编辑层、无改动集、无回写） |

### 2.3 与既存 V2 实现的关系（重要澄清，避免误读）

| # | 澄清 |
|---|---|
| **IBP23** | V2 **已存在**以下实现且**已通过验证**：VoxelCore、Geology（Stage A–E：地层/洞穴/矿藏倾向/群系接口）、Runtime Core、Streaming、Mesh、View、组装点 Bootstrap（11 个验证入口，附录 C） |
| **IBP24** | 本计划的"禁止"**不是**要求删除或停用上述实现，而是：**Plan Phase 1 的新增能力不得落在这些域内**，且 **Phase 1 的里程碑不得依赖它们**（唯一允许的 V2 依赖 = **VoxelCore 的坐标/体素契约** + 已冻结契约文档） |
| **IBP25** | 上述实现**不得**在本阶段被修改：其文件哈希在 Phase 1 期间必须保持不变（§7 `IBPV3`） |
| **IBP26** | 若 Phase 1 实现过程中发现"必须先改地质/Mesh/View/Runtime 才能完成" ⇒ **停止并报告**（属契约/边界缺口），不得就地改动 |

### 2.4 与未决 Blocker 的关系（只登记，不裁决）

| # | 审计项 | 与 Plan Phase 1 的关系 |
|---|---|---|
| **IBP27** | `A-1`（体素真值写入者归属：Runtime 安装 vs Phase 6 抽样写入） | **不在 Phase 1 范围**：Phase 1 不写体素、不涉及安装 ⇒ **不依赖该裁决**；实现阶段需要时再裁决 |
| **IBP28** | `A-2`（Terrain 真值双轨；场景仍挂载 Legacy） | Phase 1 **不切换场景、不接入运行角色** ⇒ 与 `A-2` 不冲突；场景接入属未来阶段（`M7` 类决策） |
| **IBP29** | `A-3`（Pipeline 编号双轨） | Phase 1 **不新增阶段编号**；本文档用独立的 "Plan Phase" 标签并给出映射表（附录 A）以防新增冲突；`A-3` 仍待裁决 |
| **IBP30** | `A-4`（ID 前缀多义）/ `B-2`（配置参数无 Owner）/ `B-3`（参数物理存储位置未定义） | Phase 1 必须回避：**不新增编号前缀**、**不使用未登记配置值**、**不定义存储布局**；若实现被这三项阻塞 ⇒ 按 `IBP26` 停止并报告 |

---

## 3. Dependency Direction

### 3.1 唯一允许方向

```
Seed
 ↓
Coordinate
 ↓
Parameter
 ↓
Region
 ↓
Query
```

| # | 规则 |
|---|---|
| **IBP31** | 上述链条是 **Plan Phase 1 唯一的依赖方向**：每一环只能读取其**上游**（或同环输入），不得跳环（继承 `PC48`/`PC62`） |
| **IBP32** | **Coordinate 是唯一空间语义来源**：Parameter 与 Region 的一切位置判断都经 Coordinate 契约，不得自带坐标换算 |
| **IBP33** | **Parameter 是唯一参数产出环**：其 `Modifier` 分量只能来自 Region 基础结构（`RA75`），其 `Final` 只能由合成得到（`RA76`） |
| **IBP34** | **Query 是终端环**：只读、无副作用、不产生参数、不改任何上游（`WQ3`/`WQ5`） |

### 3.2 禁止的四条边（硬性）

| # | 禁止 | 含义 | 依据 |
|---|---|---|---|
| **IBP35** | **Query → Generation** | 查询结果不得成为任何生成/求值环的输入；查询不得触发重算 | `WQ4`；`PC104` |
| **IBP36** | **Runtime → Parameter** | 运行时不改写、不影响参数；玩家/运行时状态只进 Runtime State | `PX2`；`PC53`；`LS` 同族 |
| **IBP37** | **Mesh → World Data** | 网格（几何/呈现）不得作为世界数据来源，也不得回写世界数据 | `PC40`/`PC57`；`VoxelWorld_Mesh.md`（"Mesh 不是世界真值"） |
| **IBP38** | **Player → World Generation** | 玩家状态（位置/进度/背包/已探索）不得参与任何生成/求值判断 | `PC54`；`WSC58` |

### 3.3 依赖方向检查

| # | 检查 |
|---|---|
| **IBP39** | Phase 1 的依赖图必须是 **DAG**（无环）：Seed → Coordinate → Parameter → Region → Query 单向（继承 `PCV1`） |
| **IBP40** | 禁止"横向绕行"：Query 不得绕过 Parameter 直接读 Region 内部（只能读已发布的修正）；Region 不得绕过 Parameter 直接读 Coordinate 之外的来源 |
| **IBP41** | 禁止"隐式边"：任何"因为某环存在所以改变另一环行为"的隐式耦合，一律视为违规（`LS65` 扫描口径） |
| **IBP42** | **边集合封闭**：Phase 1 只允许 §3.1 的链条 + 只读输入；**禁止新增因果边**（本计划不授权任何新边） |

---

## 4. First Milestone Definition

### 4.1 里程碑目标（冻结）

> **给定同一个 `Seed` + 同一个 World Position，能够稳定返回 `World Parameter State`。**

| # | 规则 |
|---|---|
| **IBP43** | 返回内容 = **参数三态**：`Base` / `Modifier` / `Final`（`WorldParameter_Runtime_Model.md` §3），且覆盖**已登记参数集合**（`WorldParameter_Contract.md` 附录 B 的环境参数组） |
| **IBP44** | 返回必须是**只读**结果：不产生任何副作用（不写体素、不建 Chunk、不生成对象；`T4`/`PM120`） |
| **IBP45** | 返回必须带**最小解释**：能说明 `Base` 与 `Modifier` 的分解、以及"无修正/无影响"的显式说明（`PM100`/`PM107`/`WE8` 同族） |

### 4.2 四项要求（缺一不可）

| # | 要求 | 判据（可核验，不设计实现） |
|---|---|---|
| **IBP46** | **可重复** | 同一 `(Seed, Position)` 重复求值 ⇒ `Base`/`Modifier`/`Final` **逐字段相同**（多次求值 + 两个独立求值实例对照；继承 `WSV1`/`PMV6`） |
| **IBP47** | **与 Scene 无关** | 求值不依赖任何场景对象/Transform/相机/渲染状态；在不同场景或**无场景**的批处理上下文中结果一致（继承 `VoxelWorld_Core.md` §5"不得依赖 Unity Transform"） |
| **IBP48** | **与 Chunk 无关** | 求值不要求任何 Chunk 已存在/已加载；未生成区域同样可答；求值前后 Chunk 数量不变（继承 `WSC73`/`PM54`/`PC87`） |
| **IBP49** | **与 Player 无关** | 求值不读取玩家位置/进度/背包/已探索/已采集；玩家操作前后逐字段不变（继承 `PC54`/`WSC78`/`WSV14`） |

### 4.3 里程碑的附加约束

| # | 约束 |
|---|---|
| **IBP50** | **退化可核验**：Region 基础结构不存在或修正为空时，`Final` 必须等于 `Base`（逐字段；继承 `Region_Design.md` `RM3`） |
| **IBP51** | **域分离**：Seed/Coordinate/Parameter/Region 各自使用**已登记域**；不得复用保留域、不得新增域（`PZ12`；`IBP8`） |
| **IBP52** | **不引入新参数**：若里程碑需要的某个量未被登记 ⇒ **停止并报告**（属配置/参数缺口，见 `IBP30`），不得就地新增 |
| **IBP53** | **不引入新契约**：里程碑只使用已冻结契约中的语义；若发现语义不足 ⇒ 停止并报告（`IBP7`/`IBP26`） |
| **IBP54** | **可验证环境**：里程碑的验收必须能在**与场景无关的批处理验证入口**中执行（沿用 V2 既有验证风格；**不新增 Legacy 依赖**） |
| **IBP55** | **不改既有 V2 模块**：里程碑不得要求修改地质/Mesh/View/Runtime/Streaming/Bootstrap（`IBP25`/`IBP26`） |
| **IBP56** | **里程碑不覆盖**：不做 Biome/Geology/Resource/Ecology/Voxel 化/Mesh/运行时编辑的任何部分（§2.2） |

---

## 5. Legacy Boundary

### 5.1 禁止进入 V2 的 Legacy 内容（五类，冻结）

| # | 禁止进入 V2 的 Legacy 内容 | 基线证据（`Legacy_Status.md` 附录 B.1） | 说明 |
|---|---|---|---|
| **IBP57** | **`DensityTerrain`** | `DensityTerrain.cs` 1855 行，SHA16 `0EB935DBA79CBD40` | 旧世界运行主体；其规则/生命周期/坐标入口一律不得进入 V2 |
| **IBP58** | **`VoxelAwareMesh`**（增量面修改体系） | `VoxelAwareMeshPrecut.cs` 1090 行 `5DD248CA0E593FEF`；`VoxelAwareMeshRuntimeMutation.cs` 1890 行 `479E8655D5F0F7CE`；`VoxelAwareMeshSpatialFace.cs` 1829 行 `A5968958A5C71821` | 旧增量网格修改逻辑不得进入 V2 的 Mesh/编辑路径 |
| **IBP59** | **`Terrain Chunk`** | `TerrainChunk.cs` 303 行 `52AE93A43B97E79B`；配套 `TerrainChunkView.cs` 49 行 `76A9685FC1E3E6B4` | 旧 Chunk 数据/视图不得替代或包裹 V2 `VoxelChunk` |
| **IBP60** | **`Mesh Generation Pipeline`（Legacy 的）** | `TerrainMeshGenerator.cs` 413 行 `00DC3191AB8BA5FA` | 仅指 **Legacy 的网格生成管线**；**V2 自有的 Mesh 模块不在本禁令内**（但也**不在 Phase 1 范围**，§2.2） |
| **IBP61** | **`Mining Runtime`（Legacy 的）** | `TerrainMining.cs` 47 行 `E802CBA26C507620`；`TerrainModification.cs` 445 行 `47925DE263BF15F4`；`TerrainEditGrid/Layer/RecordV2`（88/85/56） | 旧挖掘/编辑运行时不得进入 V2；V2 的运行时修改边界**尚未定义**（审计 `B-1`），本计划**不裁决** |

### 5.2 Legacy 边界的三条补充规则

| # | 规则 |
|---|---|
| **IBP62** | **名称禁令**：V2 的实现、文档、验证入口**不得**采用上述 Legacy 名称或旧术语（Terrain Local、`voxelSize`、`DensityField`、Edit Layer 等）（继承 `LS73`） |
| **IBP63** | **数据禁令**：不得把 Legacy 的资产/场景数据/编辑记录作为 V2 的输入或参考真值（继承 `LS62` `[待确认]` 项：数据/存档关系仍未定义） |
| **IBP64** | **工具例外**：若工具/调试必须**只读**对比 Legacy（例如截图对比、历史数据考古），只允许只读、且必须显式登记为工具用途、不得成为结构依赖（继承 `LS56`） |

### 5.3 Legacy 在本阶段的状态（不变）

| # | 规则 |
|---|---|
| **IBP65** | Legacy 保持 `LEGACY-FROZEN` + `OUT-OF-MAINLINE`（`Legacy_Status.md` `LS2`）：本阶段**不新增、不修改、不删除** |
| **IBP66** | Legacy 的 34 个验证入口保持原状；Phase 1 的新增验收**不进入**该集合（`LS9`/`LS59`） |
| **IBP67** | 若 Phase 1 的验证意外发现"必须改 Legacy 才能继续" ⇒ **停止并报告**（`IBP26`），不得在 Legacy 上开发（`LSX-6`） |
| **IBP68** | 本阶段**不触发** Legacy 删除流程：删除判据（`LS78`：V2 稳定五条）与前置条件（`LS79`）在本阶段**均不满足**（V2 尚未接入场景） |

---

## 6. Future Expansion Points

> **只登记，不设计**。以下为**未来阶段标签**（Plan Phase 2–8），**不是** Pipeline 编号（见附录 A）。

| Plan Phase | 扩展点 | 登记内容（一句话） | 前置条件（登记） |
|---|---|---|---|
| **Phase 2** | **Region** | 在 Phase 1 的 Region 基础结构之上补齐**完整区域能力**（采纳流程边界、上限、资格声明等，以 `Region_Architecture_Contract.md` §4 的六步为准） | Phase 1 里程碑达成；Region 契约缺口（如区域级资格推导 `P1-2`）明确 |
| **Phase 3** | **Biome** | 接入群系分类（以 `WorldState_Design.md` §5.3 规则表为唯一权威；`Desert` 等分支问题须在该规则表内解决） | Phase 2 完成 |
| **Phase 4** | **Geology** | 地质判定接入参数链（P3 是唯一可读 Biome 的位置；不得影响 P0/P1/P2） | Phase 3 完成；既有 Stage A–E 实现哈希不变 |
| **Phase 5** | **Resource** | 资源判定层接入（`DepositPotential` 只读消费 + 环境/纪元条件；不得读 Biome/生态） | Phase 4 完成；`Resource_Architecture_Contract.md` 边界生效 |
| **Phase 6** | **Voxel** | 体素化（抽样 + 写入动作）——**需要先裁决体素写入者归属**（审计 `A-1`） | Phase 5 完成；`A-1` 已裁决 |
| **Phase 7** | **Mesh** | 从体素数据派生几何（只读派生；不得回写世界数据） | Phase 6 完成 |
| **Phase 8** | **Runtime Modification** | 运行时修改/编辑层的边界（**当前未定义**，审计 `B-1`；需独立文档） | 需要独立决策文档；不得用 Legacy `Modification` 体系填补 |

| # | 规则 |
|---|---|
| **IBP69** | 上述 7 个扩展点**仅登记**，本计划不设计其实现、不定义其接口、不承诺时序 |
| **IBP70** | 每个扩展点启动前必须确认其**前置条件**已满足（含审计项裁决），否则**停止并报告** |
| **IBP71** | 扩展点**不得**以 Legacy 内容作为过渡实现（`LS46`/`LS55`） |
| **IBP72** | 扩展点**不得**引入新的参数/`VoxelType`/因果边，除非由对应规范先行变更（`IBP8`） |
| **IBP73** | 扩展点的编号使用 **Plan Phase** 标签；若未来需要与 Pipeline 编号对齐，必须先解决 `A-3`（附录 A） |
| **IBP74** | Phase 2–8 的**顺序**遵循既有因果链；本计划不重排、不合并、不跳阶段 |
| **IBP75** | Phase 8（Runtime Modification）与 Phase 6（Voxel 写入）**存在耦合风险**（都涉及"谁写世界数据"）；两者都必须等待 `A-1` 类裁决（`IBP27`） |
| **IBP76** | 任何扩展点若需要修改 V2 已存在模块（地质/Mesh/View/Runtime/Streaming/Bootstrap）的哈希 ⇒ 属**显式授权变更**（需独立任务 + 记录），不得在扩展点内顺手修改 |

---

## 7. Acceptance Rules

| # | 不变量 | 验证方法 |
|---|---|---|
| **IBPV1** | **无代码修改**：本次仅新增本文档；`Assets/**` 写入 = 0；被引用的既有规范 SHA 不变 | `git status` + 文件 mtime 扫描 + 哈希比对 |
| **IBPV2** | **无 Legacy 依赖**：仓库中不存在"V2 → Legacy"的新增引用（Phase 1 实现尚未存在时，扫描基线 = 0） | 结构扫描（命名空间/类型/路径）与 Legacy 清单对照（`LS65`） |
| **IBPV3** | **无新增参数**：Phase 1 不使用未登记参数；不新增任何参数名 | 与 `WorldParameter_Contract.md` 附录 B 集合比对 |
| **IBPV4** | **无新增因果边**：Phase 1 只使用 `Seed → Coordinate → Parameter → Region → Query` 与只读输入 | 依赖图检查（DAG，`IBP39`） |
| **IBPV5** | **无修改已有契约**：所有被引用契约的 SHA16 与本计划登记值一致 | 哈希比对 |
| **IBPV6** | **Legacy 未被删除/修改**：`Assets/Scripts/Terrain/**` 29 文件与 4 份 Legacy 规范哈希不变 | 与 `Legacy_Status.md` 附录 B 基线比对 |
| **IBPV7** | **既有 V2 模块未被修改**：地质/Mesh/View/Runtime/Streaming/Bootstrap 关键文件哈希不变（附录 C） | 哈希比对 |
| **IBPV8** | **里程碑可判定**：`IBP46`–`IBP49` 四项要求均有明确判据且可机械核验 | 检查项清单（逐条对照） |
| **IBPV9** | **禁止项无遗漏**：§2.2 的 7 项禁止与 §5.1 的 5 项 Legacy 禁令均在本文档中显式列出 | 清单完整性检查 |
| **IBPV10** | **编号不冲突**：本文档前缀（`IBP*`/`IBPV*`/`IBPX-*`）不与既有文档冲突；Plan Phase 与 Pipeline Phase 的映射存在（附录 A） | 前缀扫描 + 映射表存在性 |
| **IBPV11** | **声明性**：本文档不产生运行时效果、不改变任何体素输出、不改变既有验收结论 | 结构性论证（无代码参与） |
| **IBPV12** | **不解除未决项**：`A-1`–`A-4`、`B-1`–`B-9` 的裁决状态在本文档中保持不变（只登记） | 与审计报告条目对照 |

---

## 附录 A：编号映射（防新增冲突）

> **Plan Phase = 实现阶段标签（本计划）；Pipeline Phase = `WorldGeneration_Pipeline_Contract.md` 的阶段编号；Old Phase = `WorldGeneration_Pipeline_Design.md`（旧）的编号。**

| Plan Phase | 内容 | 对应 Pipeline Phase（新） | 对应 Old Phase（旧） | 备注 |
|---|---|---|---|---|
| **Phase 1** | Seed + Coordinate + Parameter（Base/Final）+ Region 基础结构 + 单点查询 | 0（Seed）· 1（Skeleton，仅坐标框架语义）· 2（World Parameter，两段式）· 3 的一部分（Region 基础结构）· Query 旁路 | 0 · 1 · 3（部分） · 4 · Query 旁路 | 单点查询在两者的编号体系中都**不占阶段**（旁路只读层） |
| **Phase 2** | Region（完整） | 3 | 3 | — |
| **Phase 3** | Biome | 4 | 5 | **两套旧编号中数字不同**（`A-3`） |
| **Phase 4** | Geology | 5 | 6 | 同上 |
| **Phase 5** | Resource | 旁路（不占阶段） | 旁路（不占阶段） | — |
| **Phase 6** | Voxel | 6 | 7 | 同上 |
| **Phase 7** | Mesh | 7 | 不在管线内 | — |
| **Phase 8** | Runtime Modification | 8（Runtime Activation）+ 未定义的修改层 | 不在管线内 | 依赖审计 `B-1`/`A-1` |

| # | 规则 |
|---|---|
| **IBPX-1** | 引用本文档的阶段时，必须写成 **"Plan Phase N"**；引用管线时必须写成 **"Pipeline Phase N"** 或 **"Old Phase N"** |
| **IBPX-2** | 不得把 Plan Phase 当作 Pipeline Phase 使用（反之亦然） |

---

## 附录 B：Phase 1 前置/未决项（登记，不裁决）

| # | 项 | 来源 | 对 Phase 1 的影响 | 处置（本计划） |
|---|---|---|---|---|
| 1 | `A-1` 体素写入者归属 | 审计 CD-01 | **无**（Phase 1 不写体素） | 不裁决（`IBP27`） |
| 2 | `A-2` Terrain 双真值/场景挂载 | 审计 CD-02 | **无**（Phase 1 不接入场景） | 不裁决（`IBP28`） |
| 3 | `A-3` Phase 编号双轨 | 审计 CD-03 | 间接（编号引用歧义） | 用映射表规避（附录 A）；不裁决（`IBP29`） |
| 4 | `A-4` 编号前缀多义 | 审计 CD-04 | 间接（新文档不得新增前缀冲突） | 本计划使用独立前缀（`IBP*`） |
| 5 | `B-1` Runtime Modification 边界缺失 | 审计 CD-05 | **无**（Phase 1 不涉及运行时修改） | 不裁决（`IBP22`） |
| 6 | `B-2` 配置参数无 Owner 登记 | 审计 CD-06 | **有**：若 Phase 1 需要某配置值而它无 Owner 登记 ⇒ 缺口 | 停止并报告（`IBP30`/`IBP52`） |
| 7 | `B-3` 参数物理存储位置未定义 | 审计 CD-07 | **有**：实现时"谁持有三态"未被契约定义 | 不在本阶段设计（`IBP30`）；实现前需决策 |
| 8 | `B-4`/`B-5` 术语与图例多义 | 审计 CD-08/CD-09 | 间接（实现文档需避免歧义术语） | 引用时带文档名；不裁决 |
| 9 | `B-6` 横向禁令缺口（`U` 单元） | 审计 CD-10 | **无**（Phase 1 不涉及这些系统对） | 不裁决 |
| 10 | `B-7` `Elevation` 双角色 | 审计 §9 | **有**：Coordinate 契约需明确"提供者=高度场、转发者=WorldState" | 遵守既有 `GC71`/`WSC48` 口径（不改契约） |
| 11 | `B-8`/`B-9` 旧栈遗留与 Phase 集合扩展 | 审计 CD-11/CD-12 | 间接 | 由 `Legacy_Status.md` 与本计划 §5 承接 |

---

## 附录 C：基线（本计划的核验锚点）

### C.1 既有 V2 关键文件（Phase 1 期间不得修改）

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

### C.2 Legacy 基线（禁入范围，只读）

* Legacy 代码 29 文件 / 10,615 行；Legacy 规范 4 份 / 4,110 行；Legacy 验证入口 34 个 —— 完整清单与 SHA16 见 `Specifications/Architecture/Legacy_Status.md` 附录 B。

### C.3 本计划引用的契约（不得修改）

`VoxelWorld_Core.md` `F718B68E95100C57` · `WorldGeneration_Core.md` `684753D5E6B31565` · `WorldParameter_Contract.md` `532F9596F1348B57` · `WorldParameter_Runtime_Model.md` `B8ADCE96CA1AA773` · `WorldState_Runtime_Contract.md` `04B164617833A832` · `WorldState_Design.md` `D54CB1CC14859312` · `Region_Design.md` `77FA71B2620E7396` · `Region_Architecture_Contract.md` `90361CAB116697C1` · `WorldQuery_Design.md` `6D801C60F3C6D42F` · `WorldQuery_Architecture_Contract.md` `6635ED5334F9FD5C` · `WorldGeneration_Pipeline_Contract.md` `BEF7CFC1AF98AF3C` · `Specifications/Architecture/Legacy_Status.md` `9B8488B2D1335DC3` · `Specifications/Audit/WorldArchitecture_CrossDocument_Audit_V1.md` `068664697B376128`

---

## 附录 D：边界声明与变更规则

### D.1 本计划不做的事

| # | 不做 |
|---|---|
| 1 | **不实现**：本文档不含代码、接口、算法、数据结构、存储布局 |
| 2 | **不删除**：Legacy 文件、规范、验证入口全部保留（`IBP6`/`IBP65`） |
| 3 | **不修改**：任何既有 Specification、任何 `Assets` 文件、任何 V2 既有实现 |
| 4 | **不裁决**：审计 `A-1`–`A-4`、`B-1`–`B-9` 一律保持未决（附录 B） |
| 5 | **不新增**：参数、`VoxelType`、因果边、编号前缀、契约 |
| 6 | **不切换**：场景挂载、运行角色、数据/存档均不在本阶段变更（`IBP28`） |

### D.2 变更规则

1. 变更 Phase 1 的允许范围、禁止清单、依赖方向或里程碑判据，**必须**修改本文档并升版本。
2. **不得放宽**：`IBP11`–`IBP15`（允许边界）、`IBP16`–`IBP22`（禁止）、`IBP35`–`IBP38`（四条禁止边）、`IBP46`–`IBP49`（里程碑四项要求）、`IBP57`–`IBP61`（Legacy 五类禁令）、§7 验收。
3. 任何"需要改 V2 既有模块""需要新增参数""需要新增契约""需要动 Legacy"的诉求 ⇒ 先按 `IBP26`/`IBP52`/`IBP53`/`IBP67` **停止并报告**，不得在实现中就地处理。
4. Phase 2–8 的启动必须逐项确认前置条件（§6）与审计裁决状态（附录 B）。
5. 本文档的 Plan Phase 标签与 Pipeline 编号的对应关系（附录 A）在 `A-3` 裁决前**不得**被并入既有契约。
