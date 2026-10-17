# Voxel World Mining Specification V1

> 适用范围：`Assets/Scripts/VoxelWorld/`（VoxelCore + Generation + Mining）体系。
> 本规范只定义 **世界数据修改（Mining）**，不涉及既有 Terrain 系统
> （`TerrainModificationV2` / `DensityField` / Marching Cubes 地形）——那套挖掘系统与本文档无关。
>
> 上游规范：
> * `Specifications/World/VoxelWorld_Core.md` —— 世界真值（Voxel Data）与 Chunk 数据契约；
> * `Specifications/World/WorldGeneration_Core.md` —— 初始状态生成（Seed + ChunkCoordinate → VoxelChunk）；
> * `Specifications/World/VoxelWorld_Mesh.md` —— 派生表示（Mesh 生成 / 生命周期 / Dirty·Rebuild / 验收不变量）。
>
> 冲突时以 `VoxelWorld_Core.md` 为准；**修改规则**以本规范为准，**表示层规则**以 Mesh 规范为准。

---

## 1. Core Principles（不可协商条款）

1. **Mining 只修改 Voxel 数据。**
   * 唯一写入手段是 VoxelCore 的数据写入 API（`VoxelWorld.SetVoxel` → `VoxelChunk.SetVoxel`）；
   * 不得直接改 `Voxel[,,]`、不得绕过坐标换算、不得另建一套方块表；
   * 不得修改生成数据（Seed/生成参数）、不得修改存档结构、不得修改 Mesh/Collider/材质。
2. **Mesh 不是挖掘目标。**
   * Mining 的输入**只有** `VoxelCoordinate`（+ 目标类型）；**不接受** Mesh、三角形、`RaycastHit`、
     碰撞体、Chunk 对象或任何表示层数据作为"改哪里"的依据；
   * "命中点 → 目标 Voxel" 属于**查询层**（后续独立规范）：查询层负责把射线/交互解析成
     `VoxelCoordinate`，Mining 只消费坐标；
   * 禁止从 Mesh 反推目标、禁止用 Mesh 的缺失/多余反推世界状态（VoxelWorld_Core §1）。
3. **原子性**：一次挖掘要么完整生效（数据 + 脏标记），要么完全不生效；不得出现"半改变"。
4. **幂等/确定性**：同一坐标重复挖掘的结果与次数无关（第二次起为"无变化"）；
   结果不依赖帧序、Mesh 状态、加载顺序。
5. **不改上游**：VoxelCore（坐标 / 存储 / 脏语义）与生成规则均冻结；
   生成不得覆盖已挖掘数据（WorldGeneration_Core §9：运行时改动与生成分离）。
6. **不做表示层补救**：挖掘失败/缺面等问题必须在数据与规则层定位，
   禁止用"补面 / 隐藏 / 重建整个 Mesh"掩盖（VoxelWorld_Mesh §10.3）。

---

## 2. 修改流程（Pipeline）

```
MiningRequest(VoxelCoordinate target, targetType = Air)
        │
        ├─ 1. 合法性校验（坐标类型合法；目标类型合法）
        ├─ 2. 读取当前 Voxel（未生成区域 ⇒ Air，不创建 Chunk）
        ├─ 3. 同值判定（current.Type == target ⇒ Unchanged，直接返回）
        ├─ 4. 写入 Voxel 数据（VoxelWorld.SetVoxel ⇒ 真正变化时置脏）
        └─ 5. 返回结构化结果（Mined / Unchanged / NotGenerated / Rejected）
                    │
                    ▼
        （表示层在后续调度点按 Mesh 规范 §7 重建 Dirty Chunk）
```

### 2.1 逐步骤契约

| 步骤 | 契约 | 使用的既有 API |
|---|---|---|
| 1 校验 | 坐标必须是整数体素坐标（`VoxelCoordinate`）；V1 目标类型固定 `VoxelType.Air`；非法输入 ⇒ `Rejected(reason)`，不写数据 | `VoxelCoordinate`、`VoxelType` |
| 2 读取 | 未生成 Chunk ⇒ 视为 **Air**（空、无碰撞），**不得**为读取而创建 Chunk | `VoxelWorld.TryGetVoxel(...)`（返回 `false` ⇒ 未生成）/ `GetVoxel(...)`（未生成 ⇒ Air） |
| 3 同值判定 | 只依据 `Voxel.Type`；相同 ⇒ `Unchanged`：不写、不置脏、不触发重建 | `Voxel`、`Voxel.IsAir()` |
| 4 写入 | 走唯一入口 `VoxelWorld.SetVoxel(world, Air)`：内部 `WorldToChunkCoordinate` → `WorldToLocalCoordinate` → 懒创建 Chunk → `VoxelChunk.SetVoxel`；**只有类型真正变化**时 `IsDirty = true` | `VoxelWorld.SetVoxel`（返回 `bool`：是否变化） |
| 5 返回 | 结构化结果，供上层做高亮/音效/统计；失败必须带原因，禁止静默 | — |

### 2.2 单次挖掘的范围

* 一次 `Mine(target)` **只允许修改一个 Voxel**（V1）。
* 需要修改多个 Voxel 的玩法（爆炸、建筑、地形工具）必须走 §6 的**批量事务化入口**，
  不得在单 Voxel 入口里循环偷改。

### 2.3 建议的结果契约（实现阶段按此落地）

```csharp
public enum MiningOutcome
{
    Mined,        // 数据发生变化（已置脏）
    Unchanged,    // 同值写入：无操作（含"未生成区域的 Air → Air"）
    NotGenerated, // 目标落在未生成区域（如需可区分于 Unchanged）
    Rejected      // 输入非法 / 数据层异常（必须带 reason）
}
```

> `NotGenerated` 与 `Unchanged` 的取舍由实现决定，但**必须**满足 §5 的可观测性要求：
> 调用方永远能区分"改了数据"、"没改数据"、"被拒绝"。

---

## 3. Dirty 规则

1. **唯一置脏来源 = 数据实际变化**（VoxelCore 保证；同值写入返回 `false` 且不置脏）。
2. Mining **不得**手工调用 `MarkDirty()` 来"强制重建"；若确需强制刷新（诊断/工具），
   必须走独立的显式入口，且不得掩盖数据层问题。
3. **脏粒度 = Chunk**：不引入 per-voxel dirty，不引入额外的 dirty 注册表。
4. **单次挖掘的影响范围（V1）**：最多 **1 个** Chunk 变脏（owning Chunk）。
   依据 Mesh 规范 §4 B1（越界邻居按 Air），一个 Voxel 的变化不会影响其它 Chunk 的 Mesh。
5. **未生成区域**：对未生成坐标挖掘**不得创建 Chunk、不得置脏**（因为当前值即 Air ⇒ 无变化）；
   只有"真正写入数据"时才允许懒创建 Chunk（其初始状态本就是 `Dirty`，
   来自 VoxelCore 的"新 Chunk 未构建"语义，而非挖掘额外置的脏）。
6. **禁止顺带重建**：Mining 不构建 Mesh、不触碰 MeshFilter/MeshCollider
   （见 §4）。

---

## 4. Rebuild 触发

1. **职责分离**：Mining 只负责"数据 + 脏标记"；Mesh 重建由表示层在**后续调度点**执行
   （Mesh 规范 §6/§7）。Mining 调用**不得**同步构建 Mesh（避免挖掘帧阻塞/卡顿）。
2. **触发条件**：`SetVoxel` 返回 `true` ⇒ 该 Chunk 需要重建；返回 `false` ⇒ 无需重建。
3. **调度顺序与确定性**：表示层使用 `VoxelWorld.CollectDirtyChunks`（VoxelCore 已按
   Chunk 坐标排序）；重建顺序与字典遍历顺序无关。
4. **清理时机**：仅当该 Chunk 的 Mesh 构建**成功**后才 `ClearDirty()`
   （`ClearDirtyFlags()` 只能用于"全部已成功重建"之后的批量清理）。
5. **重建范围（V1）**：仅 owning Chunk。若未来启用 Mesh 规范 §4 B3（跨 Chunk 剔除模式），
   必须在此同步定义"边界 Voxel 的编辑需同时标脏相邻 Chunk"，否则会留下过期 Mesh。
6. **预算与原子性**：允许分帧重建，但每个 Chunk 仍原子完成，
   且最终几何必须与一次性重建逐字节一致（Mesh 规范 §6/§7、验收 I8）。

---

## 5. 失败语义

| 情形 | 判定 | 必须的行为 |
|---|---|---|
| 输入非法（坐标/类型不合法） | `Rejected` | 不写数据、不置脏；返回原因 + 日志（禁止静默） |
| 目标在未生成区域 | `NotGenerated` / `Unchanged` | **不创建 Chunk**、不置脏；结果必须可与 `Mined` 区分 |
| 同值写入（Air → Air） | `Unchanged` | 不写、不置脏、不触发重建；不记日志也可，但计数需可审计 |
| 数据层异常（越界访问等） | `Rejected`（异常向上传播或包装） | 世界数据保持"未写"或"完整写入"二态，不得半状态 |

**硬性条款**

1. **原子性**：任何失败都不得留下"数据改了但脏标记丢了"或"脏标记置了但数据没改"。
2. **不得回滚已生效的数据**：若数据写入成功，则世界状态以"数据已改"为准；
   上层的后续步骤失败不得让数据与脏标记不一致。
3. **脏标记必须可达**：若重建尚未执行，Chunk 必须保持 `Dirty`；
   **禁止**在未重建的情况下 `ClearDirty`（否则 Mesh 永久过期 = "看着是实体，实际已挖空"类缺陷）。
4. **失败可观测**：返回结构化原因 + 日志；**禁止静默空操作**
   （"看起来挖了，但既没改数据也没报错"）；禁止用 Mesh 侧补丁掩盖（§1.6）。
5. **重试安全**：写入是幂等的，失败后重试同一请求不会产生副作用（第二次多为 `Unchanged`）。
6. **禁止隐式重生成**：失败/异常后不得通过"重新生成整个世界/整个 Chunk"来掩盖状态不一致
   （生成不得覆盖玩家改动，WorldGeneration_Core §9）。

---

## 6. 同值修改规则

1. **相同类型写入 = 无操作**：不写数据、不置脏、不触发重建，返回 `Unchanged`。
2. **判定依据只有 Voxel 数据**：不得用 Mesh 状态、不得用"是否高亮/已选中/已渲染"、
   不得用玩家输入缓存作为"是否需要写"的依据。
3. **幂等**：`Mine(target)` 执行 N 次 ⇒ 数据一致；仅第一次可能返回 `Mined`，
   其余全部 `Unchanged`；脏标记只在第一次被置位（之后由重建流程清理）。
4. **反向操作（未来的放置）同样遵守**：`Air → 非 Air`、`非 Air → 另一非 Air`
   都按同一规则判定"是否变化"；不引入"强制覆盖"的默认行为
   （强制覆盖必须是显式参数，见 §7 Building 模式）。
5. **可审计**：建议对 `Mined / Unchanged / Rejected` 计数，
   用于验证"输入没有被静默吞掉"（与 §5.4 对应）。

---

## 7. 未来扩展原则（Explosion / Building）

### 7.1 统一原则

所有玩法（挖掘、爆炸、建筑、工具）都走**同一条数据修改路径**：
**目标集合（VoxelCoordinate[]）+ 写入策略（目标类型/覆盖模式）→ 批量事务 → 脏集合**。
不得新增第二套真值（不得自建方块表/网格/覆盖层），不得直接改 Mesh
（WorldGeneration_Core §10：扩展不得要求修改 VoxelCore）。

### 7.2 Explosion

* **请求形态**：`MineBatch(targets[], targetType = Air)`；目标集合由爆炸规则（半径/形状）生成，
  但**规则本身不属于本规范**（属于玩法层）。
* **提交顺序**：
  1. 解析目标集合 → 2. 校验（边界 / 未生成区域策略 / 数量上限）→ 3. 统一提交写入 → 4. 汇总脏集合。
* **同值处理**：批内每个 Voxel 独立遵循 §6（同值跳过，不计入 `Mined` 统计）。
* **原子性层级**：V1 建议 **逐 Chunk 原子、整批不要求原子**，但必须返回"部分成功"报告
  （`Mined/Unchanged/Rejected` 计数 + 失败原因），禁止"整批静默失败"。
* **脏集合**：= 所有被改 Chunk 的并集（跨 Chunk 时可能多个；边界剔除模式开启时按 §4.5 追加邻居）。
* **预算**：必须有每帧上限 / 分帧策略（避免一次改上万 Voxel 造成帧阻塞）；
  未完成部分保持 `Dirty`，不得丢弃。

### 7.3 Building / 结构放置

* **模板放置 = 更大的批量请求**：模板（局部 Voxel 图）→ 世界坐标集合 → 批量写入。
* **两种显式模式**（默认保守）：
  * `AirOnly`（默认）：只填 Air 位置，遇到非 Air 视为冲突；
  * `Override`：允许替换非 Air（必须显式开启，且记录被替换的类型以便撤销/审计）。
* **放置校验先行**：空间占用冲突、边界越界、数量上限等在**提交前**完成；
  校验失败 ⇒ 整次放置不写数据（与 §1.3 原子性一致）。
* **不得写入生成规则**：建筑/结构不进入生成参数；持久化只保存
  `Seed + 改动数据`（VoxelWorld_Core §6），流式加载时由"生成 + 改动叠加"重建
  （WorldGeneration_Core §9）。

### 7.4 禁止项（扩展通用）

* 不得让新玩法绕过 `VoxelWorld.SetVoxel` 直接改内部数组；
* 不得为玩法单独引入"另一份世界状态"（缓存方块、影子 Mesh、私有脏表）；
* 不得让 Mining/Explosion/Building 依赖已经加载的 Mesh 或 MeshCollider 来决定改哪里；
* 不得把"重建 Mesh"作为数据修改的一部分（重建永远是表示层的后续调度）。

---

## 8. 验收不变量（Mining）

> 每条必须可自动化验证（纯数据断言，必要时辅以 Mesh 快照）。

| # | 不变量 | 验证方法 |
|---|---|---|
| **M1** | 只改目标 Voxel：挖掘后 Chunk 内其余 4095 个体素不变 | 挖掘前后全 Chunk 快照比较（仅目标坐标变化） |
| **M2** | 目标坐标正确：写入坐标 == 请求坐标（world→chunk→local 换算一致，含负坐标/边界） | 对 6 个方向邻居与跨 Chunk 坐标各挖一次并读回 |
| **M3** | 单次挖掘最多 1 个 Chunk 变脏（V1） | `CollectDirtyChunks` 计数 == 1 |
| **M4** | 同值无操作：对 Air 挖掘 ⇒ 数据不变、`IsDirty` 不变、返回 `Unchanged`、无重建请求 | 断言返回码 + `IsDirty` + 数据快照 |
| **M5** | 幂等：同坐标重复挖掘 N 次 ⇒ 数据一致，仅第一次 `Mined` | 循环调用并统计 `Mined/Unchanged` |
| **M6** | 脏生命周期：`Mined` ⇒ owning Chunk `Dirty`；重建成功 + `ClearDirty` ⇒ `Clean`；未重建 ⇒ 仍 `Dirty` | 直接断言 `IsDirty` 与重建顺序 |
| **M7** | 未生成区域：挖掘未生成坐标 ⇒ 不创建 Chunk、不置脏、结果可区分 | 断言 `ChunkCount` 不变 + `IsDirty` 不变 + 返回码 |
| **M8** | 失败可观测且无半状态：非法输入 ⇒ `Rejected` + 原因，数据/脏标记无变化 | 注入非法输入，断言数据与 `IsDirty` 不变 |
| **M9** | 真值分离：删除/重建 Mesh 不影响挖掘结果；挖掘不读取 Mesh | 挖掘前后 Mesh 引用不变；重建 Mesh 后重复挖掘结果一致 |
| **M10** | 可复现：`Seed + 改动数据` 重新构建世界后，挖掘后的世界与首次一致 | 生成→挖掘→重建世界（同 seed + 改动）→ 比较数据与 Mesh（Mesh 规范 I14） |
| **M11** | 无 Mesh 依赖（结构性）：Mining API 签名不出现 Mesh/三角形/`RaycastHit`/碰撞体类型 | 代码审查 + 反射检查参数与返回类型 |
| **M12** | 生成不覆盖挖掘：重新生成/流式加载同一 Chunk 后，已挖掘 Voxel 仍为 Air | 生成→挖掘→再次生成（或卸载重载）后读回目标坐标 |

---

## 9. 明确不做（V1 out of scope）

* 命中/射线/目标解析（查询层独立规范：查询只输出 `VoxelCoordinate`）；
* 挖掘耗时、工具、耐久、动画、粒子、音效（表现/玩法层）；
* 掉落物、物品、背包、合成；
* 爆炸与建筑的具体规则（§7 只定义扩展原则与契约）；
* Mesh 构建与 Collider 细节（VoxelWorld_Mesh 规范）；
* 跨 Chunk 剔除模式（Mesh 规范 §4 B3）、并发挖掘、撤销/重做、网络同步。

---

## 10. 变更规则

1. 与上游冲突时以 `VoxelWorld_Core.md` 为准；表示层问题以 `VoxelWorld_Mesh.md` 为准。
2. 任何修改若触及 §1（原则）、§5（失败语义）或 §8（不变量），必须：
   ① 升版本号；② 同步更新验收测试；③ 说明与既有存档（`Seed + 改动数据`）的兼容性。
3. **禁止**通过修改 Mesh 或增加表示层补丁来掩盖数据层缺陷；
   问题必须先回到"Voxel 数据 + 本规范规则"定位。
4. V1 实现范围：**单 Voxel Mining = `VoxelWorld.SetVoxel(target, Air)` + 脏标记**，
   其余扩展（批量/爆炸/建筑）按 §7 的契约分阶段引入，不得改变 VoxelCore。
