# R33：`WorldGenerationSettings.SeaLevel` 默认值与反序列化行为验证

- 性质：**只读诊断 + 隔离测试**。未修改任何生产 `.cs` / 场景 / Prefab / 配置资产 / 冻结验证器 / 哈希登记；未改既有报告、历史日志、进度记录。
- 现场：`main`，HEAD `37246478ed354a1e9a0ce7a3a3d9457caf188062`，staged = **0**。
- 未执行：commit / push / reset / clean。
- **声明**：本报告为**静态源码核对 + 隔离测试**结果，不构成 World V2 端到端验证。

---

## 1. 关键源码位置与真实值（执行前核对）

| 位置 | 内容 | 实测 |
|---|---|---|
| `WorldGenerationSettings.cs` L22 | `[Serializable]` 特性 | **存在**（该结构**可**被 Unity 序列化） |
| 同 L23 | `public struct WorldGenerationSettings` | 值类型 |
| 同 L29 | `public const int DefaultSeaLevel = 64;` | **64** |
| 同 L49 | `public int SeaLevel;` | **无内联初始化器** ⇒ `default(struct).SeaLevel == 0` |
| 同 L62–L69 | 六参构造：`: this(…, DefaultSeaLevel)` | 委托默认 **64** |
| 同 L76–L89 | 七参构造：`SeaLevel = seaLevel;` | 显式赋值 |
| 同 L132 | `if (SeaLevel < 0 \|\| SeaLevel > WorldHeight)` | 合法范围 **`[0, WorldHeight]`** |
| `VoxelWorldStreamingBootstrap.cs` L64/L67 | `public int WorldHeight = 64;` / `public int SeaLevel = 64;` | **有**内联初始化器 |
| 同 L194–L195 | `new WorldGenerationSettings(Seed, TerrainHeight, SoilDepth, WorldHeight, TerrainFrequency, TerrainAmplitude, SeaLevel)` | 以**七参**显式传入 |
| 同（结构持有情况） | Bootstrap **不持有** `WorldGenerationSettings` 字段（L194 为**局部变量**） | ⇒ 该结构当前**不作为任何组件的序列化字段** |

---

## 2. 测试设计与副作用说明（先设计，后执行）

| 路径 | 测试方法 | 是否触碰项目 | 副作用评估 |
|---|---|---|---|
| A / B / C# 默认值路径 | **项目外隔离编译**：将 `WorldGenerationSettings.cs` **只读复制**到 `%TEMP%\r33_sealevel_probe`，与探针 `Probe.cs` 一起用编辑器自带 `mcs.bat` 编译为独立 exe 运行 | **否**（项目内零写入） | 无 |
| C（Unity 序列化） | **临时 Editor 探针** `Assets/Editor/__R33SeaLevelProbe.cs`，用 `-executeMethod R33SeaLevelProbe.Run` 在 batchmode 内运行 `JsonUtility` 往返与缺字段反序列化 | **是（新增 1 个临时文件）** | 仅新增临时脚本；**不写资产/场景**；测试后**删除 `.cs` 与自动生成的 `.meta`**，已复核 `git status` 恢复为 25 条 |
| D（Bootstrap） | 同一探针：`new GameObject` + `AddComponent<VoxelWorldStreamingBootstrap>()` 读取字段；随后 `DestroyImmediate` | **是（同上临时文件）** | 临时 GameObject 仅存在于 batchmode 进程内存，进程退出即丢弃；**未保存场景** |
| E（既有实例） | **递归搜索** `Assets` 下全部 `.unity` / `.prefab`（35 个）及 `.asset` / `.mat` / `.json`，按组件 GUID `2c531141a81459d499f3f2959fe28988` 匹配 | 只读 | 无 |
| F（临时资产） | 见 §4.3：探针从不调用 `SaveScene` / `SaveAssets` / `AssetDatabase.*`；对象全部 `DestroyImmediate` | — | 已清理并复核 |

**明确区分测试的序列化机制**：本轮 C 路线验证的是 **`JsonUtility`（Unity 的 JSON 序列化设施）**，**不是** Inspector/ScriptableObject 的 YAML 序列化，也不是 `System.Runtime.Serialization`。三者不可混称。

---

## 3. 路径 A / B / C# 默认值路径（隔离编译，零项目影响）

工具：`D:\gamemaking\2022.3.62f1c1\Editor\Data\MonoBleedingEdge\bin\mcs.bat`（编辑器自带）；源文件为**只读拷贝**。编译退出码 **0**，运行退出码 **0**，`Failures = 0`。

| 用例 | 实际输出 | 判定 |
|---|---|---|
| **A** 六参构造 `new WorldGenerationSettings(12345, 24, 3, 64, 0.06f, 6f)` | `SeaLevel = 64`（= `DefaultSeaLevel`） | **PASS** |
| **B1** 七参 `seaLevel = 0` | `SeaLevel = 0`；`IsValid = True` | **PASS** |
| **B2** 七参 `seaLevel = 64` | `SeaLevel = 64`；`IsValid = True` | **PASS** |
| **B3** 七参 `seaLevel = WorldHeight(64)` | `SeaLevel = 64`；`IsValid = True` | **PASS** |
| **B4** 越界 `SeaLevel = 65`（WorldHeight=64） | `IsValid = False`，reason = `SeaLevel 必须在 [0, WorldHeight] 内（实际 65 / WorldHeight=64）。` | **PASS**（反例保护有效） |
| **B5** 越界 `SeaLevel = -1` | `IsValid = False`，reason 同上（实际 -1） | **PASS** |
| **A2** `default(WorldGenerationSettings)` | `SeaLevel = 0`，`WorldHeight = 0`，`IsValid = False` | **PASS**（但见 §5 风险 R-2） |
| **A3** `new WorldGenerationSettings()` + object-initializer 设置其余 6 个字段（**跳过构造器赋值**） | **`SeaLevel = 0`**，`IsValid = True` | **PASS（按代码语义）**，但**语义风险见 R-1** |

---

## 4. 路径 C / D（Unity Batchmode 隔离探针）

### 4.1 运行信息

| 项 | 值 |
|---|---|
| Unity 版本 | `2022.3.62f1c1`（`Unity.exe` 文件版本 `2022.3.62.11538587`） |
| 命令 | `"D:\gamemaking\2022.3.62f1c1\Editor\Unity.exe" -batchmode -nographics -quit -projectPath "D:\gamemaking\My project" -logFile <TEMP>\unity_r33_probe.log -executeMethod R33SeaLevelProbe.Run` |
| 进程退出码 | **0** |
| 耗时 | 30 s |
| 日志 | `_phase2b_validation/round33_sealevel_probe_run.log`（83535 bytes）；stdout/stderr 各 0 字节 |
| 编译错误 | `error CS = 0` |
| 编译警告 | 4 行 = 既有 2 条唯一警告 ×2（`VoxelCoordinateMigrationStageB3CValidation.cs CS0162`、`MiningSurfaceIntegrityValidation.cs CS0414`），**无新增** |
| 断言结果 | 全部 `PASS`，`Failures = 0 / Result = PASS` |

### 4.2 结果

| 用例 | 实际输出（原文） | 判定 |
|---|---|---|
| **A**（Unity 程序集内复验六参构造） | `PASS - A six-arg ctor SeaLevel (in Unity) = 64` | **PASS** |
| **B**（Unity 内复验七参 0 / WorldHeight） | `= 0` / `= 64` | **PASS** |
| **C1** `JsonUtility.ToJson(settings)` | `{"Seed":12345,"TerrainHeight":24,"SoilDepth":3,"WorldHeight":64,"SeaLevel":64,"TerrainFrequency":0.05999999865889549,"TerrainAmplitude":6.0}` ⇒ JSON **含 `SeaLevel` 字段**；`FromJson` 往返 `SeaLevel = 64`、`WorldHeight = 64`、`TerrainHeight = 24` | **PASS** |
| **C2** 缺字段反序列化（模拟旧数据不含 `SeaLevel`） | `JsonUtility.FromJson<WorldGenerationSettings>({…无 SeaLevel…})` ⇒ **`SeaLevel = 0`**，`WorldHeight = 64`，**`IsValid = True`** | **PASS（行为已确认）**，但语义风险见 **R-1** |
| **C3** 显式 `SeaLevel = 0` 往返 | `ToJson` → `FromJson` ⇒ `SeaLevel = 0`（与"缺字段"不可区分） | **PASS** |
| **D1** `AddComponent<VoxelWorldStreamingBootstrap>()` 新建实例 | `bootstrap WorldHeight=64 SeaLevel=64` | **PASS** |
| **D2** `JsonUtility.FromJsonOverwrite(不含 SeaLevel 的 JSON, bootstrap)` | `bootstrap.SeaLevel=64`（**未被清零**），`Seed=12345`（被覆盖），`WorldHeight=64` | **PASS**（缺字段**不覆盖**已有值） |
| **D3** 临时对象清理 | `临时 GameObject 已 DestroyImmediate，未写入任何资产/场景` | **PASS** |

> **C2 与 D2 的关键差异**：对**结构体**做 `FromJson` 会**整体新建**（缺字段 ⇒ 0）；对**已存在的 MonoBehaviour 实例**做 `FromJsonOverwrite` 则**只覆盖出现过的字段**（缺字段 ⇒ 保留原值 64）。两者语义不同，不可互相推断。

### 4.3 临时资产创建与清理

| 项 | 结果 |
|---|---|
| 创建 | `Assets/Editor/__R33SeaLevelProbe.cs`（1 个临时 Editor 脚本；Unity 自动生成同名 `.meta`） |
| 是否写入资产/场景 | **否**：探针未调用 `SaveScene` / `SaveAssets` / `AssetDatabase.*`；创建的唯一 GameObject 在 `finally` 中 `DestroyImmediate` |
| 清理 | 已删除 `.cs` 与 `.meta`；`Assets` 递归搜索 `R33Probe|R33SeaLevelProbe` 残留 = **0** |
| 现场复核 | `git status --short` 恢复为 **25 条**（与探针创建前一致）；`tracked M = 18`、untracked 源文件 = 4；`git diff --stat` 仍为 `18 files changed, 361 insertions(+), 121 deletions(-)` |
| 项目外隔离目录 | `%TEMP%\r33_sealevel_probe` 已删除（证据日志已归档到项目内 `_phase2b_validation/`） |

---

## 5. 路径 E：既有场景 / Prefab 的序列化覆盖情况

| 检查 | 范围（递归） | 结果 |
|---|---|---|
| Bootstrap 组件 GUID | `2c531141a81459d499f3f2959fe28988`（取自 `VoxelWorldStreamingBootstrap.cs.meta`） | — |
| `.unity` / `.prefab` | `Assets` 递归全部 **35 个** 文件 | **0 处**引用该 GUID |
| `.asset` / `.mat` / `.controller` / `.json` | `Assets` 递归全部同类文件 | **0 处**引用 |
| `WorldGenerationSettings` 作为序列化字段的承载 | Bootstrap 内为**局部变量**，非字段 | **当前无任何组件/资产序列化该结构** |

**结论（严格措辞）**：**未发现任何既有 Bootstrap 实例**，因此"旧数据缺 `SeaLevel` 字段"的反序列化风险**当前不存在实际暴露面**。但**不能**据此推断"未来所有实例都安全"——一旦将来创建 Bootstrap 组件实例或把 `WorldGenerationSettings` 作为字段序列化，即在 §4 C2 所示的缺字段语义下会得到 `0`。

---

## 6. PASS / FAIL / UNKNOWN 分类与判定理由

| 路径 | 分类 | 判定理由 |
|---|---|---|
| **A** C# 构造器默认路径（六参） | **PASS** | 两套独立环境（隔离 mcs 编译 + Unity 程序集内）均得 `SeaLevel = 64 = DefaultSeaLevel` |
| **B** 显式传入（0 / 64 / WorldHeight） | **PASS** | 三个合法值均精确保留；越界 `65`/`-1` 均 `IsValid = False` 且给出正确 reason |
| **C-1** Unity 序列化往返（**JsonUtility**，全字段） | **PASS** | JSON 含 `SeaLevel`，往返 3 个代表字段逐一相同 |
| **C-2** 缺字段反序列化（**JsonUtility**） | **PASS（行为已确认）** | 实测 `SeaLevel = 0` 且 `IsValid = True` ⇒ 行为确认为"缺字段 ⇒ 0"，**不是** `DefaultSeaLevel` |
| **C-3** Inspector / ScriptableObject **YAML** 序列化路径 | **UNKNOWN** | 当前**无任何生产类型**把该结构作为序列化字段，无法在不修改生产代码的前提下构造 YAML 场景/资产来验证；需见 §7 的隔离方案 |
| **D-1** Bootstrap 新建实例字段初始值 | **PASS** | 实测 `SeaLevel = 64`（内联初始化器生效） |
| **D-2** 覆盖式反序列化语义 | **PASS** | 缺字段时保留 64，未归零 |
| **D-3** Bootstrap → `WorldGenerationSettings` 的**传递** | **PASS（源码级 + 构造器级双重佐证）** | L194–L195 以**七参**显式传 `SeaLevel`；七参构造语义已由 B 系列实测确认；`IsValid` 失败会写入 `buildError`（L197–L200） |
| **D-4** 运行时"世界海洋分布是否正确" | **UNKNOWN / 未覆盖** | 需真实世界输出逐体素比对，超出本轮只读范围；且 World V2 参数层无生产消费者 |
| **E** 既有场景/Prefab 覆盖 | **PASS（"未发现实例"）** | 35 个 `.unity/.prefab` + 全部 `.asset/.mat/.json` 中 GUID 命中 **0** |

---

## 7. 尚未覆盖的行为 / 需另立轮的最小隔离方案

| # | 未覆盖项 | 建议的最小隔离测试（**本轮未实施**） |
|---|---|---|
| 1 | **Unity YAML 序列化**（Inspector / Scene / Prefab）中"缺字段 ⇒ ？" | 新建**临时** ScriptableObject 类型（脚本置于 `Assets/Editor/`，事后删除）+ 一个**临时** `.asset`，先写入含 `SeaLevel` 的数据，再手工删除 YAML 中该字段后用 `AssetDatabase.LoadAssetAtPath` 读回，观察是 `0` 还是其它；需批准"临时资产"操作 |
| 2 | 真实场景中 Bootstrap 缺字段的加载行为 | 创建临时场景 + Bootstrap 实例，保存为**临时** `.unity`（非项目既有场景），删除 `SeaLevel` 行后重新打开读取 |
| 3 | 对象初始化式构造（A3）在实际代码中是否出现 | 递归扫描全库 `new WorldGenerationSettings` 调用点，逐一确认均为构造器调用（本轮未做全量扫描；Bootstrap 已确认是七参） |
| 4 | `SeaLevel` 改动对世界输出的实际影响 | 需真实生成世界并比对 `Ocean` 分布；属端到端验证，须单独规划 |

---

## 8. 风险清单（基于实测行为，非推测）

| # | 风险 | 证据 | 现时暴露面 |
|---|---|---|---|
| **R-1** | `SeaLevel` 字段**无内联初始化器**：任何"缺字段"的结构化反序列化（缺省 payload、对象初始化式跳过构造器）都会得到 **0**，而 `IsValid` 仍可能返回 **True**（如 A3、C2 实测）⇒ **静默地把"无海洋"当成合法配置** | §3 A3（`SeaLevel=0` 且 `IsValid=True`）；§4 C2（`SeaLevel=0`、`IsValid=True`） | **当前为 0**：无组件/资产序列化该结构（§5）；但**任何未来**的序列化承载都会立即继承该行为 |
| **R-2** | `default(WorldGenerationSettings)` 的 `IsValid = False`（因 `WorldHeight=0` 等） | §3 A2 | 低（默认值即非法，属"显式失败"） |
| **R-3** | `SeaLevel = 0` 与"字段缺失"在序列化上**不可区分**（C3） | §4 C3 | 低（当前无持久化） |
| **R-4** | `WorldHeight = 64` 且 `SeaLevel = 64` ⇒ **全部合法高度皆为海洋**（`elevation < 64` 恒真） | R26/R33 既有结论；本轮未改变 | 语义是否为期望值**仍待所有者裁决**（既有未决项） |

> 说明：**R-1 不等于"当前有缺陷"**。按本轮约束，未改动默认值或字段初始化器；是否给 `public int SeaLevel = 64;` 加内联初始化器（使缺字段路径也落到 64）属**设计决策**，需明确批准。

---

## 9. 最终现场

| 项 | 值 |
|---|---|
| 分支 / HEAD | `main` / `37246478ed354a1e9a0ce7a3a3d9457caf188062`（未变） |
| staged | **0** |
| `git status --short` | **25 条**（18 tracked `M` + 4 untracked 源文件 + 3 证据目录，与 R32 结束时一致） |
| `git diff --stat` | `18 files changed, 361 insertions(+), 121 deletions(-)`（未变） |
| `WorldGenerationSettings.cs` | `7F11DEE8061E4FA1`（**未改动**） |
| `VoxelWorldStreamingBootstrap.cs` | `8A59289F0194D20F`（**未改动**） |
| 349 条冻结哈希 | **349 / 失配 0 / 缺失 0** |
| 进度记录 | 仍 **1032 行**（未改） |
| 临时探针 / 临时资产 | 已全部删除并复核（`Assets` 内 `R33Probe` 残留 = 0；TEMP 隔离目录已删） |

### 9.1 本轮新增文件

| 文件 | 大小 |
|---|---:|
| `_phase2b_validation/round33_sealevel_probe_run.log` | 83535 B |
| `_phase2b_validation/round33_sealevel_probe_stdout.txt` | 0 B（如实空捕获） |
| `_phase2b_validation/round33_sealevel_probe_stderr.txt` | 0 B（如实空捕获） |
| `_phase2b_validation/round33_sealevel_default_serialization_report.md` | 本文件 |

### 9.2 变更清单

| 变更 | 内容 |
|---|---|
| 修改 | **无**（生产代码、场景、Prefab、配置资产、验证器、哈希登记**全部未动**） |
| 临时创建后删除 | `Assets/Editor/__R33SeaLevelProbe.cs` + `.meta`（已删，残留 0） |
| 删除 / 移动 / 覆盖既有证据 | **无** |

---

## 10. 结论

1. **A（六参构造）**、**B（显式 0/64/WorldHeight 与越界保护）**、**D1/D2（Bootstrap 初始值与覆盖语义）**、**D3（传递，源码+构造器级）** 均为 **PASS**，且在**两套独立环境**（隔离 msc 编译 / Unity 程序集）中得到一致结果。
2. **C-1（JsonUtility 全字段往返）PASS**；**C-2 行为已确认为"缺字段 ⇒ 0 且 IsValid 仍 True"**——这是行为事实，不是本轮判定的缺陷。
3. **C-3（YAML 序列化路径）为 UNKNOWN**：当前无生产类型把该结构作为序列化字段，只读条件下无法构造；已给出最小隔离方案，需批准。
4. **E：未发现任何既有 Bootstrap 实例**（35 个 `.unity/.prefab` + 全部 `.asset/.mat/.json` 中 GUID 命中 0）⇒ 现时无缺字段反序列化风险暴露；**不推断未来实例安全**。
5. 未修改任何默认值、字段初始化器或生产逻辑；349 条冻结哈希闭合未受影响；现场已恢复到与 R32 结束完全一致的状态。
6. **UNKNOWN 项（C-3、D-4）与 R-1/R-4 设计决策均待你决定，本轮不改。**
