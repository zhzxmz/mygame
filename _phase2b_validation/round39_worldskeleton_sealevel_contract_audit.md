# R39：`WorldSkeletonFacts.TryEvaluate` 的 SeaLevel 参数来源与接口契约审查（只读）

- 性质：**只读调查**。本轮**未修改任何文件**（除本报告）；未执行 `git add` / commit / push / reset / clean / stash / checkout；未运行 Unity。
- **结论边界**：本报告是**契约与来源审查 + 设计推荐**；不实施任何代码或文档修改；不代表真实世界 Ocean 分布已验证。

---

## 1. 现场保护核验

| 项 | 值 |
|---|---|
| 分支 / HEAD | `main` / **`37246478ed354a1e9a0ce7a3a3d9457caf188062`**（未变） |
| staged | **0** |
| `git status --short` | **25 条**（tracked `M` 18 / untracked 源文件 4 / 证据目录 3）——与本轮开始时一致 |
| 进度记录 | `sealevel_round7_progress.md` = **1032 行**（未改） |
| R36 改动 | 在（`GeologyRuntimeIntegrationValidation.cs`：`Check(` = **56**，含 R38 两条一致性断言） |
| R38 改动 | 在（`WorldV2ParameterPipelineValidation.cs`：`Check(` = **45**） |
| `WorldSkeletonFacts.cs` | 未被触碰：哈希 `EE8627035B72FD16`（与实施前一致） |
| 本轮新增文件 | **仅**本报告 |

---

## 2. 当前方法的真实签名与逐步执行逻辑

### 2.1 签名（`Assets/Scripts/World/V2/WorldSkeletonFacts.cs` L94–L98）

```csharp
public static bool TryEvaluate(
    WorldCoordinate position,
    WorldGenerationSettings settings,
    int seaLevel,                       // ← 独立参数
    out WorldSkeletonFacts facts)
```

### 2.2 逐步执行（L99–L119，逐行）

| 行 | 代码 | `settings` 是否被使用 | `seaLevel` 是否被使用 |
|---:|---|---|---|
| 101 | `if (position.Y < 0 \|\| position.Y >= settings.WorldHeight)` | ✅ **是**（`WorldHeight` 上界） | — |
| 103–104 | `facts = default; return false;` | — | — |
| 108 | `int elevation = HeightFunction.GetTerrainHeight(position.X, position.Z, settings);` | ✅ **是**（作为高度场入参） | — |
| 111 | `bool ocean = elevation < seaLevel;` | ❌ **否** | ✅ **是**（唯一使用者） |
| 114 | `int continent = ocean ? 0 : 1;` | — | — |
| 117–118 | `facts = new WorldSkeletonFacts(elevation, ocean, continent); return true;` | — | — |

**关键事实**：`settings.SeaLevel` **在整个方法中从未被读取**；Ocean 判定**只**依赖独立形参 `seaLevel`。`settings` 仅承担两件事：`WorldHeight`（垂直范围）与高度场入参。

### 2.3 失败 / 成功语义

- 唯一失败：`Y` 越出 `[0, settings.WorldHeight)` ⇒ `false` + `default`（`IsValid == false`）；**不钳制、不修正**、不新增失败通道（`IF-8`）。
- 成功：`true` + 完整有效事实（`IsValid == true`）；`Continent = 0`（海洋）是**合法结果**，与失败无关（`Q-8`/`D2`）。

---

## 3. 独立参数的历史来源及证据

### 3.1 引入时点（Git 只读查询）

| 查询 | 结果 |
|---|---|
| `git log --oneline -- Assets/Scripts/World/V2/WorldSkeletonFacts.cs` | **仅 1 次提交**：`7c0d474 World V2: WorldSkeleton V1 查询接口 + Region/参数契约体系`（2026-10-17，zhzxmz） |
| `git log -S 'int seaLevel' -- …/WorldSkeletonFacts.cs` | 同一提交 `7c0d474` ⇒ **独立参数与文件同时引入，此后从未修改** |
| 文件是否被跟踪 | 是（`git ls-files` 命中 1） |

### 3.2 决定性证据：设计当时 `settings.SeaLevel` 并不存在

| 检查 | 结果 |
|---|---|
| `git show HEAD:Assets/Scripts/VoxelWorld/Generation/WorldGenerationSettings.cs` 中 `SeaLevel` 命中 | **0** |
| 当前工作树同文件 `SeaLevel` 命中 | **19**（含注释） |
| `git status` 该文件 | **`M`（未提交）** |

⇒ **在 `7c0d474` 设计并提交 `WorldSkeletonFacts` 时，`WorldGenerationSettings` 中根本没有 `SeaLevel` 字段**。因此：

- 独立 `seaLevel` 形参**不是"为未来预留"**，而是当时**唯一**能传入海平面的通道；
- 该设计的**目的是必要且合理的**，与 `Q-1`（查询输入 = 世界坐标 + `settings` + `seaLevel`）完全一致；
- 二者的"重复"是**后来**把 `SeaLevel` 加进 `WorldGenerationSettings`（R7，**尚未提交**）才产生的，**契约尚未同步这一变化**。

### 3.3 契约是否规定二者必须一致

| 检索（`WorldSkeleton_V1_Minimal_Contract.md`，347 行） | 命中 |
|---|---|
| `settings.SeaLevel` | **0** |
| `必须等于` / `须一致` / `保持一致` / `同步` | **0** |

契约实际规定的是：
- `IF-1`（L160）：签名**已冻结**，含 `int seaLevel`；
- `Q-1`（L177）：查询输入含 `settings`（世界生成配置）**+** `seaLevel`（`C-1` 登记配置量）；
- `C-1`（L140）：`SeaLevel` 属**已登记配置**（`WorldParameter_Contract.md` 表 C，默认 64，范围 `[0, WorldHeight]`），"无（沿用既有登记值，**不另设默认值**）"；
- `IF-8`（L167）：前置条件由**调用方**保证（①`settings` 合法 ②`seaLevel ∈ [0, WorldHeight]`），入口**不校验/不钳制/不修正**，且"**不因此要求**查询入口增加校验"；
- L306：`seaLevel` 范围归属裁决**已关闭**——入口**均不**处理；
- `WorldParameter_Contract.md` L344：明确 `seaLevel ∈ [0, WorldHeight]` 与 `settings` 合法性**均由调用方保证**。

⇒ **契约从未要求 `seaLevel == settings.SeaLevel`**；它只要求调用方保证范围合法。

### 3.4 是否曾有其他调用者 / 是否曾为未来功能预留

| 问题 | 结论 | 依据 |
|---|---|---|
| 是否曾经存在其他调用者 | **UNKNOWN** | 本文件自身历史仅 1 次提交（无删除痕迹）；但**跨文件**历史（`git log -S 'WorldSkeletonFacts.TryEvaluate'` 全库）本轮**未执行**，故不能断言"从未有过" |
| 是否为未来功能预留 | **否（有证据）** | 引入时 `settings.SeaLevel` 不存在 ⇒ 当时是必要通道；无"预留"性质 |
| 是否有兼容性要求 | **是** | `IF-1` 明确"**已冻结的接口形状**"（L154/L346），`AC-15` 要求文档与实现逐条对齐 |

---

## 4. 全项目调用关系与生产调用点计数

（递归扫描：`Get-ChildItem -LiteralPath Assets -Recurse -Filter *.cs`，共 235 个 `.cs`；未使用可能不递归的 `Assets/**/*.cs` 通配符）

### 4.1 `WorldSkeletonFacts.TryEvaluate` 调用点

| # | 位置 | 区域 | 说明 |
|---:|---|---|---|
| — | `WorldSkeletonFacts.cs` L94 | PROD | **声明**（非调用） |
| 1 | `WorldV2ParameterPipelineValidation.cs` L276 | EDITOR | R38 E1 海洋样本 |
| 2 | 同 L291 | EDITOR | E5 相等边界 |
| 3 | 同 L304 | EDITOR | E8 seaLevel 低于 Elevation |
| 4 | 同 L315 | EDITOR | E10 Y=−1 越界 |
| 5 | 同 L320 | EDITOR | E11 Y=WorldHeight 越界 |
| 6 | 同 L336 | EDITOR | E14 `slZero` 配置对比 |
| 7 | 同 L349 | EDITOR | E15 逐列（seaLevel=32） |

⇒ **生产调用点 = 0；Editor 调用点 = 7（全部来自 R38 新增断言）**。

### 4.2 `WorldSkeletonFacts` 类型使用分布

| 区域 | 处数 | 内容 |
|---|---:|---|
| PROD（`Assets/Scripts`） | **8** | **全部在 `WorldSkeletonFacts.cs` 自身**（类型声明、注释引用、`out` 参数、私有构造、`TryEvaluate` 内构造） |
| EDITOR | **17** | 全部在 R38 新增的 E 组断言内 |

### 4.3 `WorldGenerationSettings.SeaLevel` 的生产读取点

| 位置 | 内容 | 是否与 WSF 相关 |
|---|---|---|
| `WorldParameterRules.cs` L143 | `return elevation < settings.SeaLevel ? 1f : 0f;`（**OceanRule**） | 是**另一条** Ocean 派生路径 |
| `WorldParameterRules.cs` L112 | 注释（说明取自 `settings.SeaLevel`） | — |

⇒ **`WorldSkeletonFacts` 不读 `settings.SeaLevel`**（§2.2）；生产代码中读取它的只有 `OceanRule`。

### 4.4 独立 `seaLevel` 实参的来源

| 来源 | 值 | 性质 |
|---|---|---|
| R38 E1 | `skElevation + 1` | Editor 测试表达式 |
| R38 E5 | `skElevation` | 同上（相等边界） |
| R38 E8 | `skElevation − 1` | 同上 |
| R38 E10/E11 | `skElevation`（越界用例中与判定无关） | 同上 |
| R38 E14 | `skElevation` | 同上 |
| R38 E15 | `nonDefaultSeaLevel = 32` | 同上 |

⇒ **无任何生产来源**。当前没有任何代码把 `settings.SeaLevel` 传给它。

### 4.5 文档 / 验证器之间是否存在矛盾约定

| 文档 | 表述 | 与本接口的关系 |
|---|---|---|
| `WorldSkeleton_V1_Minimal_Contract.md` `IF-1`/`Q-1`/`C-1`/`IF-8` | 独立 `seaLevel` 为查询输入，范围由调用方保证 | **与本接口一致** |
| `WorldParameter_Contract.md` L344 | `seaLevel` 与 `settings` 合法性由调用方保证 | 一致 |
| `WorldParameter_Contract.md` L374（消费登记） | 接口"**不引入新参数**"、只转发 Elevation、按 `IF-6`/`IF-7` 派生 | 一致 |
| `WorldParameterRules.cs` L143 + `WorldParameter_Source_Contract.md` L267/L281 | `OceanRule` 读 `settings.SeaLevel` | **另一组件**，不构成本接口的明文矛盾 |

**判定**：**无明文矛盾**，但存在**表述张力**——同一概念"海平面"在参数层被表述为 `settings.SeaLevel`，在骨架查询层被表述为独立入参，**且没有任何文档说明两者应相等**。

---

## 5. 三组参数案例的静态推导

### 5.1 真实样本（沿用 R38 实测，非硬编码）

- R38 运行日志（`_phase2b_validation/round38_WorldV2ParameterPipelineValidation_rerun.log`）实测：
  - `E0：取得 Skeleton 查询用的内部 Elevation 样本（23 @ 0,0）` ⇒ **列 `(0,0)` 的 `Elevation = 23`**
  - `E15：seaLevel = 32（非默认）逐列 …（列数 35，mismatch=0）`
- `HeightFunction` 与 `SeaLevel` 无关（`OceanRule` 之外无读取；R38 E14 亦验证），故该列高度在下列案例中恒为 **23**。

### 5.2 三案例（静态推导，**未在生产流程中运行**）

设定：`settings.SeaLevel = 32`，坐标 `(0,0,0)`，`Elevation = 23`；仅改变独立形参 `seaLevel`：

| 案例 | `settings.SeaLevel` | 独立 `seaLevel` | 代码实际比较（L111） | **实际 Ocean** | 若按 `settings.SeaLevel` 应为 | 是否一致 |
|---|---:|---:|---|---|---|---|
| ① | 32 | **24** | `23 < 24` ⇒ true | **true（海洋）** | `23 < 32` ⇒ true | **一致（巧合）** |
| ② | 32 | **23** | `23 < 23` ⇒ false | **false（陆地）** | true | **❌ 分歧** |
| ③ | 32 | **22** | `23 < 22` ⇒ false | **false（陆地）** | true | **❌ 分歧** |

**为什么**：L111 只读形参 `seaLevel`；`settings` 在方法内仅用于 L101（`WorldHeight` 上界）与 L108（高度场入参），**`settings.SeaLevel` 从未被读取**；且全库**无任何交叉校验**（同行同时出现两者的命中 4 处，经核实全部为测试断言/注释/文档注释，**无一处校验**）。

⇒ 结论：**当调用方传入的 `seaLevel` 与 `settings.SeaLevel` 不一致时，接口静默采用形参值，不报错、不提示**；3 个案例中有 2 个与配置语义相反。

---

## 6. 方案对比

评价维度：配置一致性 / 调用错误风险 / 未来扩展性 / 改动范围 / 现有调用者兼容性 / 测试复杂度。

| 维度 | **方案 A：统一来源**（移除独立参数，改读 `settings.SeaLevel`） | **方案 B：保留签名 + 明确调用约定** | **方案 C：独立参数作为正式"覆盖值"** |
|---|---|---|---|
| 配置一致性 | **最好**（单一权威来源，`settings` 已必传） | 中（依赖调用方纪律；契约 `IF-8` 明确**禁止**入口校验，故无法在代码层强制） | 中（"覆盖"是显式语义，但需明确谁权威） |
| 调用错误风险 | **消除**（结构上不可能传错） | 保留（可静默传错；只能在文档/审查层约束） | 保留但**语义化**（错传=`有意覆盖`，需命名与记录） |
| 未来扩展性 | 中（若将来引入分区/局部海平面需再改签名） | 好（保留灵活性） | 最好（可表达"世界配置 vs 局部覆盖"） |
| 改动范围 | 中：`WorldSkeletonFacts.cs` 1 处签名 + R38 的 7 处调用 + 契约 `IF-1`/`IF-8`/`IF-9`/`L306`/`Q-1` + `WorldParameter_Contract.md` L344 + 相关测试断言 | **最小**（仅文档：契约 + 调用约定说明；若加运行时校验则**违反** `IF-8`，不可取） | 大：契约需新增"覆盖"语义/命名/验收，测试需覆盖两条路径 |
| 兼容性 | **生产 0 调用者 ⇒ 迁移成本近零**；但改动**已冻结的接口形状** ⇒ 需契约修订授权 | 完全兼容 | 完全兼容 |
| 测试复杂度 | **降低**（不再需要"一致性"测试；R38 E13/E14 可合并简化） | 中（只能靠测试"文档约定"，被测代码不强制） | 高（覆盖/非覆盖、优先级、越界语义都要测） |
| 需求证据 | — | — | **无**：项目内**没有任何**需要"与配置不同的海平面"的业务证据（本轮检索未发现） |

---

## 7. 对当前实现的风险判断（严格区分四类问题）

| 问题 | 判定 | 证据 |
|---|---|---|
| **方法没有生产调用者** | **是** | §4.1：生产调用点 = **0** |
| **接口契约尚未明确** | **部分明确**：`IF-1`（签名）、`IF-5`（调用约定）、`IF-6`（Ocean 公式）、`IF-8`（前置条件）都已冻结；**未明确的是"形参 seaLevel 与 `settings.SeaLevel` 的关系"** | §3.3：契约中 `settings.SeaLevel` 命中 **0**、"必须等于/须一致" 命中 **0** |
| **当前实现是否正确** | **按现行契约正确**（`Ocean = Elevation < seaLevel`，`IF-6`；失败语义 `D1/D2`；不校验不钳制 `IF-8`）⇒ **不是 bug** | §2.2、§2.3 |
| **未来生产调用是否可能误用** | **可能**（真实风险）：①静默传入与配置不一致的值（§5）；②**同时存在两条 Ocean 派生路径**（`OceanRule` 用 `settings.SeaLevel`，`WSF` 用形参），同一世界可能出现两种海洋结论 | §4.3、§5.2、§4.5 |

> **不混淆**：R38 直接接口测试通过，只证明"被测试的调用方式符合当前实现"，**不能**证明独立参数就是最佳设计；反过来，独立参数的存在**也不等于**实现有缺陷。

**风险分级**：
- **实际缺陷**：**无**（无生产调用者 + 实现符合冻结契约）。
- **契约缺口（真实、可修）**：`settings.SeaLevel` 于 R7 引入后，**契约未同步**说明两个 SeaLevel 载体的关系。
- **未来误用风险（真实、当前为零暴露）**：一旦接入生产，形参与配置可能不一致且无任何告警；与 `OceanRule` 形成双派生。

---

## 8. 唯一首选推荐：**方案 A（统一来源）**

### 8.1 为什么它最适合当前项目状态

1. **根因是"同一概念两个载体"**，且 `settings.SeaLevel` 已是**契约层登记的权威配置**（`WorldParameter_Contract.md` 表 C；`C-1` 明确"已登记、不另设默认值"）。方案 A 让唯一权威只剩一个。
2. **迁移成本正处于历史最低点**：生产调用者 = **0**，被改动的只有 R38 的 7 处 Editor 调用。**越晚实施成本越高**（一旦生产接入，签名变更将波及运行时）。
3. **不损失信息**：`settings` 已是必传参数，SeaLevel 就在其中；A 不新增参数（呼应 `WorldParameter_Contract.md` L374 "不引入新参数"）。
4. **消除双派生**：与 `OceanRule`（L143，读 `settings.SeaLevel`）**同源**，避免"同一世界两套海洋判定"。
5. **测试更简单**：不再需要"形参 vs 配置一致性"测试；R38 的 E13/E14 由两条可合并为一条更本质的断言（改 `settings.SeaLevel` 不影响 Elevation）。
6. **唯一代价可接受**：改动"已冻结接口形状"——但这正是契约允许的演化方式（该契约已存在多处"实现裁决/更正"记录，L306/L308/L334），只需一次性修订并保留审计痕迹。

### 8.2 预计修改范围（若获批准，**另立轮实施**）

| 对象 | 改动 |
|---|---|
| `Assets/Scripts/World/V2/WorldSkeletonFacts.cs` | `TryEvaluate` 去掉 `int seaLevel` 形参；L111 改为 `elevation < settings.SeaLevel`；文件内注释（L8/L26–L28/L110）同步 |
| `Specifications/World/WorldSkeleton_V1_Minimal_Contract.md` | `IF-1`（签名）、`Q-1`（输入项合并）、`C-1`（承载方式改为 `settings.SeaLevel`）、`IF-8`（前置条件②删除）、`IF-9`/L306（范围归属条目改写）、`L58`（WSF 文件 SHA-256 记录需更新）、`AC-15`（对齐说明） |
| `Specifications/World/WorldParameter_Contract.md` L344 / L374 | `seaLevel` 表述改为"取自 `settings.SeaLevel`" |
| `Assets/Editor/WorldV2ParameterPipelineValidation.cs` | R38 的 **7 处** `TryEvaluate(...)` 调用去末参（L276/L291/L304/L315/L320/L336/L349）；E13/E14 合并为"改 `settings.SeaLevel` 不影响 `facts.Elevation`"；E15 改用 `SeaLevel=32` 的 settings 实例；建议**新增 1 条**"`facts.Ocean` 与 `settings.SeaLevel` 判定一致"断言 |

### 8.3 兼容性与测试影响

| 项 | 影响 |
|---|---|
| 生产调用者 | **0** ⇒ 无生产迁移 |
| Editor 调用者 | 7 处（R38），需同步改；`Check(` 计数将从 45 变为 **≈45**（−1 合并 +1 新增，需按实际实现核对） |
| 冻结哈希 | **WSF 不在任何冻结表登记中**（本轮实测：`WorldSkeletonFacts` 登记 = **0**；冻结表含 `Scripts/World/V2` 路径键 = **0**）⇒ 实施后**无哈希级联**；但契约 `L58` 以文字记录了 WSF 的 SHA-256，**必须同步更新**（属文档一致性） |
| 回归验证 | 必跑：`WorldV2ParameterPipelineValidation.RunBatch`（预期 PASS / Failures=0，断言数按实际核对）与 `GeologyRuntimeIntegrationValidation.RunBatch`（不调用 WSF，预期仍 47/0）；并复算 349 条冻结哈希 |

### 8.4 为什么不是 B 或 C（简要）

- **B** 无法在代码层降低风险：`IF-8` 明确**禁止**入口增加校验，因此"一致"只能靠人为纪律；而纪律正是当前缺环。B 可作**过渡**（若暂不改签名，至少应在契约中写明"应传 `settings.SeaLevel`"），但不解决问题根源。
- **C** 缺乏需求证据：项目内没有任何"世界海平面之外的局部/覆盖海平面"业务需求；仅为抽象层次引入会违反"不得为追求抽象增加架构"的约束。

---

## 9. 本轮未实施任何代码修改的声明

- 本轮**没有修改** `WorldSkeletonFacts.cs`（哈希仍为 `EE8627035B72FD16`）、任何生产文件、任何验证器、任何 `Specifications` 文档、`.gitignore` 或进度记录。
- 未执行 `git add` / commit / push / reset / clean / stash / checkout。
- 未删除、移动、覆盖或重命名任何既有证据。
- **唯一新增文件**：`_phase2b_validation/round39_worldskeleton_sealevel_contract_audit.md`（本报告）。
- 不开始 R40，不实施 §8 的任何改动，等待审查决定。
