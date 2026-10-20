# R42 — WorldSkeleton 事实适配层与独立验证器（获批实施）

- 项目：`D:\gamemaking\My project`（Unity **2022.3.62f1c1**）
- 分支 / HEAD：`main` / **`37246478ed354a1e9a0ce7a3a3d9457caf188062`**（未变）
- 本轮性质：**新增适配层 + 新增独立验证器**；**不接入正式生成流程**，**不改变玩家可见世界**
- **状态：✅ 已完成并验证**（许可证恢复后补跑：三个验证器 exit=0；新验证器 44 条断言全 PASS；退出码传播实证 exit=1）→ 最终结果见 **§12**；许可证阻塞期间的记录保留在 §4.2

---

## 1. 新增文件与实际接口签名

| 文件 | 行数 | 字节 | SHA-256 |
|---|---|---|---|
| `Assets/Scripts/World/V2/WorldSkeletonProvider.cs` | **75** | 4576 | `E28316E8C5749D7F3CAAF1A340D18A129CAFF65EFC6A2679F7CFD8FA0FE01A06` |
| `Assets/Editor/WorldSkeletonProviderValidation.cs` | **463**（初版 458，修正测试期望后 +5） | 23704 | `5084FCD39726586C...`（SHA16；初版 `82D5BF0EFC7CF5BD`） |
| `_phase2b_validation/round42_worldskeleton_provider_report.md` | 本报告 | — | — |
| `Assets/Scripts/World/V2/WorldSkeletonProvider.cs.meta` | 243 B | — | GUID `08d198bb1d6e232438b7971be39c23a8`（Unity 首次成功导入生成） |
| `Assets/Editor/WorldSkeletonProviderValidation.cs.meta` | 243 B | — | GUID `3bcbebd883f0d184c8e0a0fe3718ba69` |
| `_phase2b_validation/round42_offline_compile.log` | 离线编译证据 | — | — |
| `_phase2b_validation/round42c_*_run.log`（+ stdout/stderr） | **许可证恢复后的最终真跑证据** | — | — |
| `_phase2b_validation/round42_*_run.log` / `_stdout.txt` / `_stderr.txt` | 5 次 Unity 尝试的日志（**均为许可证失败证据**） | — | — |

**实际接口签名**（均为新增，未改动任何既有签名）：

```csharp
namespace World.V2
{
    public static class WorldSkeletonProvider
    {
        // 唯一适配入口（纯转发）
        public static bool TryGetFacts(
            WorldCoordinate position,
            WorldGenerationSettings settings,
            out WorldSkeletonFacts facts);

        // 整数三元组重载（仅类型适配，与上面完全等价）
        public static bool TryGetFacts(
            int worldX, int worldY, int worldZ,
            WorldGenerationSettings settings,
            out WorldSkeletonFacts facts);
    }
}

// Editor（无命名空间，与既有验证器一致）
public static class WorldSkeletonProviderValidation
{
    [MenuItem("Tools/World V2/Validate WorldSkeleton Provider")]
    public static void Validate();        // 44 条断言（自检开关开启时 45 条）

    public static void RunBatch();        // Batchmode 入口：Exit(ExitCodeFor(failures))
}
```

设计约束落实情况（逐条）：

| 约束 | 落实 | 证据 |
|---|---|---|
| SeaLevel 只来自 `settings.SeaLevel` | ✅ | Provider 内 **0** 处直接读取 `settings.SeaLevel`（代码级）；仅注释说明来源。海平面判定仍由 `WorldSkeletonFacts.TryEvaluate` 内的 `elevation < settings.SeaLevel` 承担 |
| 不得重新引入独立 `seaLevel` 参数 | ✅ | `int seaLevel` 在 Provider 中出现 **0** 次 |
| 不得重复实现 Ocean / Continent 判定 | ✅ | Provider 无 `?` 运算、无 `0 : 1` 映射、无比较式；仅两处 `WorldSkeletonFacts.TryEvaluate(...)` 转发（共 3 处提及，含注释） |
| 不得更改 Elevation 计算来源 | ✅ | Provider 不调用 `HeightFunction`（生产侧调用数 0）；`HeightFunction` 文件哈希未变 |
| 不得为测试把内部类型改 public | ✅ | 未修改任何既有文件（§7） |
| 不得新增抽象层/注册/DI | ✅ | 单一静态类、2 个方法、无接口/无注册表/无缓存、无静态可变字段 |

---

## 2. Provider 职责与 `WorldSkeletonFacts` 的关系

```
调用方（未来：地质/运行时层，以 int 三元组工作）
        │
        ▼
WorldSkeletonProvider.TryGetFacts(...)      ← 本层：类型边界适配（WorldCoordinate ⇄ int 三元组）
        │  纯转发，零判定逻辑
        ▼
WorldSkeletonFacts.TryEvaluate(...)          ← R40 唯一权威：唯一构造点 + Ocean 严格小于 settings.SeaLevel
        │
        ▼
HeightFunction.GetTerrainHeight(x, z, settings)   ← Elevation 唯一来源（与本层无关）
```

- **Provider 不含任何世界规则**：不判定海陆、不计算高度、不派生大陆、不校验/钳制/修正参数（`IF-8`）。
- **成功/失败语义原样透传**：`true + IsValid==true` / `false + default(IsValid==false)`；唯一失败条件仍是垂直越界（`0 <= Y < WorldHeight`）。
- **它有独立存在价值**：R41 §9.3 认定"未来接入方以整数三元组工作"，而 V2 的空间输入类型是 `WorldCoordinate`；本层是该类型边界上的唯一适配点，使生成侧无需自行拼装坐标，也使 V2 事实查询在生产侧只暴露一个入口。
- **它不是"为了多一层调用"**：两处转发之外无任何逻辑分支；若仅有一层转发的价值不足，则不应存在——本轮判定其价值在**类型适配 + 单一入口 + 边界文档化**，并已由验证器覆盖"两个重载完全等价"（防止出现第二套路径）。

---

## 3. 逐项验证结果（R42 第五节 10 项要求）

> 判定层级说明：**静态** = 读源码/符号核对；**编译** = 离线 Roslyn 编译（§6）；**运行时** = Unity Batchmode 真跑（**本轮被阻塞，全部未验证**）。

| # | 要求 | 断言组 | 静态 | 编译 | 运行时 |
|---|---|---|---|---|---|
| 1 | 合法坐标可成功获取事实 | `1a–1e`（成功 / IsValid / 两重载等价 / 确定性） | ✅ 设计就绪 | ✅ 通过 | ✅ 通过 |
| 2 | 成功结果 `IsValid` 与契约一致 | `2a–2c`（成功⇒true；`default`⇒false；三字段默认） | ✅ | ✅ | ✅ 通过 |
| 3 | `Elevation` 与同坐标同设置的 `HeightFunction` 一致 | `3a–3b`（逐列 + 同列不同 Y 恒定） | ✅ | ✅ | ✅ 通过 |
| 4 | `Ocean` 与 `settings.SeaLevel` 严格小于一致 | `4a–4j`（公式逐列 + 相等边界 + ±1 + SeaLevel=0） | ✅ | ✅ | ✅ 通过 |
| 5 | `Continent` 与既有 Ocean 映射一致 | `5a–5c`（逐列 + 海洋⇒0 + 陆地⇒1） | ✅ | ✅ | ✅ 通过 |
| 6 | `Y=-1` / `Y=WorldHeight` 失败且事实无效 | `6a–6e` | ✅ | ✅ | ✅ 通过 |
| 7 | 改 SeaLevel 变 Ocean、不变 Elevation | `7a–7c` | ✅ | ✅ | ✅ 通过 |
| 8 | 不同坐标不因错误缓存返回同一事实 | `8a–8b`（逐列独立真值 + 两两组合 + 非平凡前提） | ✅ | ✅ | ✅ 通过 |
| 9 | 失败路径不返回"看似有效"的事实 | `9a–9d`（含 `default(WorldGenerationSettings)` 与两重载） | ✅ | ✅ | ✅ 通过 |
| 10 | 断言失败 ⇒ 非零 Batchmode 退出码 | `10a–10c`（映射自检）+ 命令行注入自检 | ✅ | ✅ | ✅ 通过 |

**测试纪律落实**：
- **不硬编码**任何 `Elevation`：采样列由 `CollectSampleColumns` 在运行时用 `HeightFunction.GetTerrainHeight` 发现（网格 32×32 内找 `0 < h < WorldHeight-1` 且互不相同，最多 8 列）。
- **不迎合测试改默认值**：`SeaLevel` 一律用 `WorldGenerationSettings.DefaultSeaLevel`（六参数构造）或显式七参数构造；每个构造出的测试配置都单独断言 `IsValid`（`4b`–`4e`）。
- **`WorldHeight=64 / SeaLevel=64` 保持原样**：验证器只断言"`Ocean == (Elevation < settings.SeaLevel)`"这一规则式，**不**断言"Ocean 必须为 true"；并在 `4j` 显式标注该结论由既有规则给出。
- **断言非平凡**：`sampleCount >= 2` 且 `distinctElevations >= 2`，否则单独 FAIL（避免"只测到一列"的伪通过）。
- **退出码可证**：`ExitCodeFor(0)=0 / ExitCodeFor(1)=1 / ExitCodeFor(3)=1` 三条自检 + `-wsProviderSelfFailure` 命令行开关（正常运行永不触发；用于真跑时证明"断言失败 ⇒ exit 1"）。

---

## 4. 三个验证器的运行结果与退出码

### 4.1 【历史记录 · 许可证阻塞期】实际执行情况（**全部失败于环境，未进入编译/执行阶段**）

| # | 命令（`-batchmode -nographics -quit -projectPath "D:\gamemaking\My project" -logFile <log> -executeMethod <M>`） | 退出码 | 耗时 | 日志 | 日志内容判定 |
|---|---|---|---|---|---|
| 1 | `WorldSkeletonProviderValidation.RunBatch` | **1** | 17.3 s | `round42_WorldSkeletonProviderValidation_run.log`（2308 B） | 许可证失败；**编译/执行证据 = 0** |
| 2 | `WorldSkeletonProviderValidation.RunBatch -wsProviderSelfFailure` | **1** | 26.1 s | `..._selffail_run.log`（2760 B） | 同上 |
| 3 | `WorldV2ParameterPipelineValidation.RunBatch` | **1** | 28.1 s | `round42_WorldV2ParameterPipelineValidation_run.log`（2760 B） | 同上 |
| 4 | `GeologyRuntimeIntegrationValidation.RunBatch` | **1** | 25.1 s | `round42_GeologyRuntimeIntegrationValidation_run.log`（2760 B） | 同上 |
| 5 | 重试 `WorldSkeletonProviderValidation.RunBatch` | **1** | — | `round42_retry1_run.log`（2306 B） | 同上 |

- **PASS / FAIL 数：不可得**（设计 44 条断言，但验证器**从未被执行**；不得填任何数字）。
- **编译错误/警告：不可得**（未进入编译阶段；日志中 `bee_backend` / `Assembly-CSharp` / `WorldSkeletonProvider` 命中 **0**）。
- **是否重新编译 / 是否使用缓存：不适用**（未到该阶段）。
- **崩溃或超时：无**（均为许可证快速失败，非崩溃、非超时）。
- `stdout` / `stderr`：全部 **0 B**（Unity 输出走 `-logFile`，与 R31/R38/R40 一致）。

### 4.2 阻断点诊断（严格区分环境 / 基线 / 本轮引入）

| 判据 | 实测 | 结论 |
|---|---|---|
| 日志原文 | `No valid Unity Editor license found. Please activate your license.` + `Exiting without the bug reporter. Application will terminate with return code 1` | **环境（许可证）** |
| 许可侧错误链 | `Code 10 while verifying Licensing Client signature` → `Access token is unavailable; failed to update` → `Code 404 ... Found 0 entitlement groups and 0 free entitlements` → `'com.unity.editor.headless' was not found` | 令牌/授权缺失，非项目问题 |
| 是否仅新验证器受影响 | **否**：两个**既有**验证器（`WorldV2ParameterPipelineValidation`、`GeologyRuntimeIntegrationValidation`）以**完全相同**的错误失败 | **与本轮新增代码无关** |
| 是否基线问题 | **否**：HEAD 未变、`diff --stat` 未变（`20 files, 390/142`）、既有文件哈希全部未变（§7） | 非基线回归 |
| 是否出现过编译 | **否**：日志中零编译证据；`Library/ScriptAssemblies` 时间戳仍为 **13:50:39 / 13:50:42**（R40 遗留），未被本次启动改动 | 未到编译阶段 |
| 许可证文件 | `C:\ProgramData\Unity\Unity_lic.ulf` 存在（2673 B，最后写入 2026/10/19 16:13:20 = 本次尝试期间被许可客户端重写） | 文件在，但**令牌无法获取** |
| Unity Hub 登录态 | `%APPDATA%\UnityHub\logs` 最后写入 2026/10/12；`%LOCALAPPDATA%\Unity\licenses` 仅 1 B（空壳） | 登录令牌过期/缺失 |
| 环境变量 | `UNITY_LICENSE` / `UNITY_SERIAL` / `UNITY_USERNAME` / `UNITY_PASSWORD` 均**未设置** | 无可用凭据（本轮**不**索取凭据） |
| 重试（判断瞬时） | 第 5 次仍失败（错误形态从 `Code 404` 变为 `Code 500 超时 + Token not found in cache`） | **非瞬时网络抖动，需人工恢复登录/激活** |

> **判定：环境阻塞（Unity 许可证/登录令牌），不是代码缺陷、不是基线漂移、不是本轮引入。** 按轮次纪律"不得修改旧验证器来掩盖问题"，本轮**未修改任何既有验证器**。

### 4.3 恢复许可证后的复跑指令（待执行，不得预先声称结果）

```
"D:\gamemaking\2022.3.62f1c1\Editor\Unity.exe" -batchmode -nographics -quit ^
  -projectPath "D:\gamemaking\My project" ^
  -logFile "_phase2b_validation\round42b_WorldSkeletonProviderValidation_run.log" ^
  -executeMethod WorldSkeletonProviderValidation.RunBatch
# 期望：exit 0 / PASS 44 / FAIL 0

# 同上，追加自检开关（证明断言失败 ⇒ 非零退出码）：
#   ... -executeMethod WorldSkeletonProviderValidation.RunBatch -wsProviderSelfFailure
# 期望：exit 1 / FAIL 1（仅自检项）

# 回归：
#   ... -executeMethod WorldV2ParameterPipelineValidation.RunBatch       期望 exit 0 / PASS 45 / FAIL 0
#   ... -executeMethod GeologyRuntimeIntegrationValidation.RunBatch      期望 exit 0 / PASS 47 / FAIL 0（含 GE7/GE9 冻结表）
```

---

## 5. 离线编译证据（不需要 Unity 许可证的替代验证）

| 目标 | 参照上下文 | 引用集 | 退出码 | 错误 | 警告 | 产物 |
|---|---|---|---|---|---|---|
| `WorldSkeletonProvider.cs` | **生产**（`Assembly-CSharp`，**不含 UnityEditor**） | `netstandard 2.1` + `Assembly-CSharp.dll` | **0** | **0** | **0** | `WorldSkeletonProvider.dll`（3584 B） |
| `WorldSkeletonProviderValidation.cs` | **Editor** | `netstandard 2.1` + `UnityEngine.CoreModule` + `UnityEditor` + `Assembly-CSharp(.Editor)` + 上一步产物 | **0** | **0** | **0** | `WorldSkeletonProviderValidation.dll`（12800 B） |

- 编译器：`C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe`；参数采用 Unity 编译范式 `/noconfig /nostdlib+ /r:netstandard.dll …`；`/langversion:9.0`。
- 完整命令与输出：`_phase2b_validation/round42_offline_compile.log`。
- **能力边界（必须如实标注）**：离线编译只证明**语法与 API 引用可编译**，**不能**证明断言在真实运行时通过，**不能**替代 Unity Batchmode 的 PASS/FAIL 与退出码验证。

---

## 6. 冻结哈希核验（要求：349 条全部匹配、0 不符、0 缺失、不重钉）

| 验证器 | 条目 |
|---|---|
| `ChunkStreamingValidation` | 40 |
| `GeologyRuntimeIntegrationValidation` | 65 |
| `GeologyStageAValidation` | 51 |
| `GeologyStageBValidation` | 55 |
| `GeologyStageCValidation` | 57 |
| `GeologyStageDValidation` | 63 |
| `VoxelWorldRuntimeCoreValidation` | 18 |
| **合计** | **349** |

- 结果：**匹配 349 / 不符 0 / 缺失 0**（与 R40/R41 一致）。
- **两个新文件的登记数 = 0**（`WorldSkeletonProvider.cs`、`WorldSkeletonProviderValidation.cs` 在 7 张表中命中 0 次）⇒ 本轮**不需要**也**没有**重钉任何哈希。
- 注：核验方式为独立静态复算（`SHA256(文件全字节)` 前 8 字节 → 16 位大写 HEX），因 Unity 阻塞**未能**再由 `GE9` 自身断言复核（R40 曾双向确认）。

---

## 7. 生产代码 / 场景 / 旧验证器未被修改的证据

| 判据 | 实测 | 结论 |
|---|---|---|
| `git diff --stat` | `20 files changed, 390 insertions(+), 142 deletions(-)` —— 与 R41 收尾**完全相同** | 20 个 tracked 文件内容**零改动** |
| `git status --porcelain` | **29 条**（`M` 20 + `??` 9）；R41 收尾为 27 条 ⇒ **新增的 2 条正是本轮两个新 `.cs`** | 无其它新增/删除 |
| 新增的 untracked 项 | `?? Assets/Scripts/World/V2/WorldSkeletonProvider.cs`、`?? Assets/Editor/WorldSkeletonProviderValidation.cs` | 仅此 2 个源文件 |
| 暂存区 | 空 | 未 `git add` |
| 关键文件哈希 | `WorldSkeletonFacts.cs 98B59916CA9CDBCC`、`WorldStatePipeline.cs C90D0F015CC37484`、`WorldParameterQuery.cs F72A67F57605B724`、`VoxelWorldStreamingBootstrap.cs 8A59289F0194D20F`（= 其冻结表登记值）、`GeologyWorldGenerator.cs 73EB59CFB939DC96`（= 其冻结表登记值）、`DensityTerrain.cs 0EB935DBA79CBD40`、`SampleScene.unity 8BE17EDAF421908E` | 全部**未变** |
| 旧验证器 | `WorldV2ParameterPipelineValidation.cs 8BB8275A655A9FEC`、`GeologyRuntimeIntegrationValidation.cs E3DE56D14D75F010` | **未修改**（未为掩盖许可证失败而改动任何断言） |
| 契约文档 | `WorldSkeleton_V1_Minimal_Contract.md 6C764EE847E27DDA`、`WorldParameter_Contract.md B7BF71C69180659A` | **未修改** |
| 场景 / Prefab / 材质 / 地形配置 / 生成参数 | 场景哈希未变；未创建/修改任何 `.prefab`、`.mat`、地形参数 | **未触碰** |
| Provider 注册到生产流程 | Bootstrap / GeologyWorldGenerator / DensityTerrain / IBiomeProvider 均**未引用** Provider（生产侧引用数 **0**） | **未接入** |
| Unity 侧副作用 | `.meta` **未生成**（Unity 未成功导入）；`Library/ScriptAssemblies` 时间戳未变 | 失败启动**未改动项目** |

---

## 8. HEAD、暂存区、工作区与历史证据保护

| 项 | 结果 |
|---|---|
| HEAD | `37246478ed354a1e9a0ce7a3a3d9457caf188062`（**未变**） |
| 分支 / 上游 | `main` / `origin/main` |
| 暂存区 | **空** |
| `commit` / `push` / `reset` / `clean` / `stash` / `checkout` / `add` | **全部未执行** |
| `_phase2b_validation/sealevel_round7_progress.md` | **未修改**：1032 行 / 87345 B / SHA16 `D638D271767BFFF9`（与 R40/R41 一致） |
| `round41_worldv2_production_wiring_audit.md` | **未修改**：SHA16 `52FDF4479271BBBA` |
| `round40_…_unification_report.md` / `round39_…contract_audit.md` | **未修改**（哈希与 R41 记录一致） |
| `_b3b_diff/` `_finalization_backup/` | **未触碰** |
| 本轮新增到 `_phase2b_validation/` | 仅：本报告 + `round42_offline_compile.log` + 5 次 Unity 尝试的 `run.log/stdout/stderr`（作为环境阻断证据保留） |
| 临时分析件 | 已清理（`_tmp_r42_*.txt` 全部删除；离线编译产物位于系统临时目录 `%TEMP%\r42_compile`，**不在仓库内**） |

---

## 9. 当前仍未解决的问题

### 9.1 本轮引入的未完成项（必须由下一轮补做）
| # | 未完成项 | 原因 | 补做方式 |
|---|---|---|---|
| U1 | 三个验证器的**真实 PASS/FAIL 与退出码** | Unity 许可证阻塞 | 恢复登录/激活后执行 §4.3 命令 |
| U2 | `-wsProviderSelfFailure` 的**退出码传播实证** | 同上 | 同上（期望 exit 1） |
| U3 | `GE9` 对冻结表的**运行时自断言**复核 | 同上 | 跑 `GeologyRuntimeIntegrationValidation` |
| U4 | 两个新文件的 `.meta`（GUID） | Unity 未成功导入 | 首次成功导入后自动生成，需登记其 GUID |
| U5 | Unity 侧真实**编译诊断**（本项目语言/引用设置下） | 同上 | 首次成功运行即覆盖（离线编译已先行通过） |

### 9.2 沿用自 R41 / R40 的既有未决项（本轮**未触碰、未裁决**）
| # | 未决项 | 状态 |
|---|---|---|
| E1 | `A-2` / `CD-02` / `AD-1`（旧 `DensityTerrain` 与新 `VoxelWorld` 关系：并存/迁移/替换/双轨） | **未裁决**；场景级接线仍被阻塞 |
| E2 | `WorldHeight == SeaLevel == 64`（默认配置 ⇒ `Ocean` 全 true）语义合法性 | **未裁决**；`Ocean` 驱动生成前必须先裁决 |
| E3 | 环境权威来源冲突：V2 `Temperature/Moisture`（配置基线 + Region 占位）vs `GeologyBiomeProvider`（噪声 + 海拔递减，0.1℃/0.1% 定点） | **未裁决**；不得替换 |
| E4 | `RegionModifier` 仍为占位 `[0, 0.25)` | 未变 |
| E5 | `Ocean` 的 `float` 表示缺口（`PM32`） | 未变 |
| U6 | **Provider 目前仍无生产调用者**（这是本轮**设计如此**：只建立适配层与验证能力） | 符合 R42 范围 |

---

## 10. 本轮工具使用缺陷与自我更正（如实登记）

1. **PowerShell 函数输出被变量吸收**：首次环境诊断时把 `"..."` 写在函数内，输出被 `$r1=$fn()` 捕获而**未显示**，导致"看起来什么都没做"。已改为 `Write-Host` + 直线脚本。
2. **`-out:` 参数被 PowerShell 吞掉**：csc 反复报 `CS2005: "-out:" 选项缺少文件规范`。定位为参数传递形式问题，改用 `/out:` 斜杠形式后正常（**非代码问题**）。
3. **csc 路径拼写错误导致假"通过"**：一次把 `Microsoft Visual Studio` 写成 `Microsoft\Visual Studio`，命令未执行却因"无诊断输出"被判为 ✓。已改为**先断言 exe 存在 + 检查 `$LASTEXITCODE` + 检查产物存在与大小**三重判定，重新执行后才得出 exit=0 的结论。
4. **引用集试错**：新增 `netstandard` 2.1 与 `UnityEngine.dll` 门面时分别出现 `CS0012`/`CS0433`，均为**离线引用集**问题（不是源码缺陷），最终采用 Unity 编译范式（`/nostdlib+` + 仅模块程序集）。

---

## 11. 结论

**WorldSkeleton 事实适配层与独立验证能力完成；正式生产流程尚未接入，当前玩家可见世界不变。**

补充限定（必须同时成立，否则不得视为验收通过）：

1. "验证能力完成"指**代码与离线编译层面完成**（Provider 与 Validator 均以 Unity 编译范式离线编译 **0 错误 0 警告**，44 条断言设计就绪、覆盖 R42 全部 10 项要求）。
2. **Unity Batchmode 真跑已完成**（许可证恢复后补跑，见 §12）：新验证器 `checks=44 / failures=0 / exit 0`；自检开关 `failures=1 / exit 1`（退出码传播实证）；两个既有验证器 `exit 0`（`WorldV2` 45 条 PASS、`Geology` `Checks=47 / Failures=0`）；零编译错误、无崩溃。
3. **未接入正式流程**：`WorldSkeletonProvider` 在生产侧引用数 **0**；未修改 `VoxelWorldStreamingBootstrap` / `GeologyWorldGenerator` / `DensityTerrain` / `IBiomeProvider` / 任何场景 / Prefab / 材质 / 地形配置；未改 `SeaLevel`/`WorldHeight`/`Elevation`/`Ocean`/`Continent` 任何规则。
4. **玩家可见世界不变**：运行世界仍为 `SampleScene.unity` 中的 Legacy `DensityTerrain`（哈希未变），与当前未改动的 VoxelWorld 栈一致。
5. 冻结哈希表 **349/0/0**、两新文件登记 0、**未重钉**。
---

## 12. 许可证恢复后的补跑结果（最终验收；取代 §4/§5 的未验证状态）

### 12.1 四次真实运行（Unity Batchmode，本轮实测）

命令模板：
`"D:\gamemaking\2022.3.62f1c1\Editor\Unity.exe" -batchmode -nographics -quit -projectPath "D:\gamemaking\My project" -logFile <log> -executeMethod <M>`

| # | 入口 | 附加参数 | 退出码 | 耗时 | PASS 行 | FAIL 行 | error CS | warning CS | 重编译 | 崩溃/超时 | 日志 |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | `WorldSkeletonProviderValidation.RunBatch` | — | **0** | 16.1 s | **45**（44 断言 + 1 汇总） | **0** | **0** | 4 | **是**（首次全量） | 无 | `round42c_WorldSkeletonProviderValidation_run.log`（110066 B） |
| 2 | `WorldSkeletonProviderValidation.RunBatch` | `-wsProviderSelfFailure` | **1** | 13.1 s | 45 | **1**（仅注入项） | **0** | 0 | 否（缓存） | 无 | `..._selffail_run.log`（105977 B） |
| 3 | `WorldV2ParameterPipelineValidation.RunBatch` | — | **0** | 14.1 s | **46**（45 断言 + 1 汇总） | **0** | **0** | 0 | 否（缓存） | 无 | `round42c_WorldV2ParameterPipelineValidation_run.log`（110686 B） |
| 4 | `GeologyRuntimeIntegrationValidation.RunBatch` | — | **0** | 14.1 s | **48**（47 断言 + 1 `Result = PASS`） | **0** | **0** | 0 | 否（缓存） | 无 | `round42c_GeologyRuntimeIntegrationValidation_run.log`（121970 B） |

- `stdout` / `stderr`：全部 **0 B**（Unity 输出走 `-logFile`，与 R31/R38/R40 一致）。
- 验证器自报汇总原文：
  - 运行 1：`[WorldSkeletonProvider] WorldSkeleton 事实适配层验证 ⇒ checks=44 failures=0（全部通过；exit 0）`
  - 运行 2：`[WorldSkeletonProvider] WorldSkeleton 事实适配层验证 ⇒ checks=45 failures=1（存在失败项；exit 1）`
  - 运行 3：`[WorldV2-TwoPhase] World V2 参数两段式发布 ⇒ failures=0（全部通过）`
  - 运行 4：`Checks = 47` / `Failures = 0` / `Result = PASS`
- **4 条编译警告全部为既有、与本次无关**：`VoxelCoordinateMigrationStageB3CValidation.cs(2130,50) CS0162`、`MiningSurfaceIntegrityValidation.cs(432,41) CS0414`（仅在运行 1 的全量编译中出现；后续缓存运行 0 警告）。

### 12.2 新验证器 44 条断言实测结论（全部 PASS）

| 组 | 覆盖要求 | 实测 |
|---|---|---|
| `1a–1e` | 合法坐标成功 / `IsValid` / 两个重载等价 / 确定性 | **5/5 PASS** |
| `2a–2c` | `IsValid` 与契约一致（成功⇒true、`default`⇒false、三字段默认） | **3/3 PASS** |
| `3a–3b` | `Elevation` == `HeightFunction`（逐列）+ 同列不同 Y 恒定 | **2/2 PASS** |
| `4a`–`4j` + 前置 `4b–4e` | `Ocean` 严格小于（公式逐列 / 相等边界 / ±1 / `SeaLevel=0`）+ 测试配置合法性 | **10/10 PASS** |
| `5a–5c` | `Continent` == `Ocean ? 0 : 1`（逐列 + 海洋⇒0 + 陆地⇒1） | **3/3 PASS** |
| `6a–6e` | `Y=-1` / `Y=WorldHeight` 失败、事实无效、三字段默认 | **5/5 PASS** |
| `7a–7c` | 改合法 `SeaLevel` ⇒ `Ocean` 变、`Elevation` 不变且等于独立高度 | **3/3 PASS** |
| `8a–8b` | 逐列独立真值一致 + 不同高度列不返回同一 `Elevation`（含非平凡前提） | **2/2 PASS** |
| `9a–9d` | 失败路径（含 `default(WorldGenerationSettings)`、两个重载）不返回"看似有效"的事实 | **4/4 PASS** |
| `10a–10c` | 退出码映射自检（0⇒0；1⇒1；3⇒1） | **3/3 PASS** |
| 前置 | `SeaLevel == DefaultSeaLevel`、基准配置合法、`sampleCount≥2`、`distinctElevations≥2` | **4/4 PASS** |

⇒ **44 / 44 PASS，0 FAIL**（与设计断言数一致，无"漏跑"）。

### 12.3 退出码传播实证（要求 10 的端到端证明）

- 运行 2 追加 `-wsProviderSelfFailure`：`checks=45 / failures=1`，**唯一 FAIL 为注入项**：`[自检] 命令行开关注入的故意失败（用于验证 Batchmode 退出码传播）`；进程退出码 **1**。
- 对照运行 1（不带开关）：同 44 条断言全 PASS、退出码 **0**。
- ⇒ **"断言失败 ⇒ 非零 Batchmode 退出码"** 已由真实进程退出码证明，而非仅靠映射函数自检。

### 12.4 既有验证器回归（未修改它们，仅回归验证）

- `WorldV2ParameterPipelineValidation`：`failures=0`、45 条 PASS、exit 0（与 R40 基线 45 条一致）。
- `GeologyRuntimeIntegrationValidation`：`Checks = 47 / Failures = 0 / Result = PASS`，exit 0；其中冻结表断言实测：
  - `PASS - GE7：BasicTerrainGenerator / … / View(3) 共 25 个文件 SHA 不变`
  - `PASS - GE9：既有文件 SHA256(前16) 与登记一致（比对 40 个；其中 6 个为本轮已授权修改并以新哈希钉住；跳过 0）`
  - `PASS - GE9：11 个验证入口文件都存在（缺失 0）`

### 12.5 冻结哈希核验（静态 + 运行时双向）

- 静态独立复算：**349 条 / 匹配 349 / 不符 0 / 缺失 0**；两个新文件登记数 **0** ⇒ **未重钉任何哈希**。
- 运行时：`GE7`（25 个 Critical 文件）+ `GE9`（比对 40、授权 6、跳过 0、缺失 0）**全部 PASS**。

### 12.6 Unity 导入产物（`.meta` 与 GUID）

| 文件 | 大小 | GUID |
|---|---|---|
| `Assets/Scripts/World/V2/WorldSkeletonProvider.cs.meta` | 243 B | `08d198bb1d6e232438b7971be39c23a8` |
| `Assets/Editor/WorldSkeletonProviderValidation.cs.meta` | 243 B | `3bcbebd883f0d184c8e0a0fe3718ba69` |

（由 Unity 首次成功导入自动生成；本轮**未**手工创建或修改 `.meta`。）

### 12.7 最终现场

| 项 | 结果 |
|---|---|
| HEAD | `37246478ed354a1e9a0ce7a3a3d9457caf188062`（未变） |
| 暂存区 | **空** |
| `git status` | **31 条**（`M` 20 + `??` 11）＝R41 的 27 + 本轮 2 个新源文件 + 其 2 个 `.meta` |
| `git diff --stat` | `20 files changed, 390 insertions(+), 142 deletions(-)`（与 R41 收尾完全相同） |
| 受保护文件 | `progress.md D638D271767BFFF9`、R41 报告 `52FDF4479271BBBA`、`WorldSkeletonFacts 98B59916CA9CDBCC`、`Bootstrap 8A59289F0194D20F`、`GeologyWorldGenerator 73EB59CFB939DC96`、`DensityTerrain 0EB935DBA79CBD40`、`SampleScene 8BE17EDAF421908E`、两份旧验证器与两份契约 —— **11/11 OK，异常数 0** |
| 本轮产物哈希 | `WorldSkeletonProvider.cs E28316E8C5749D7F`（4576 B / 75 行）；`WorldSkeletonProviderValidation.cs 5084FCD39726586C`（23704 B / 463 行）；两份 `.meta` 243 B |

### 12.8 本轮暴露并已修正的**测试缺陷**（必须登记）

**首次真跑（`round42b_*`）出现 4 条 FAIL**，经诊断为**我的测试期望写反**，非生产代码缺陷：

| FAIL 项 | 原（错误）期望 | 实际（正确，符合 `IF-6`/`WC3`） | 根因 |
|---|---|---|---|
| `4g` | `SeaLevel = Elevation-1` ⇒ `Ocean == true` | `Elevation(23) > SeaLevel(22)` ⇒ `Ocean == **false**`（陆地） | 把"海平面低于地表"误当海洋 |
| `4h` | `SeaLevel = Elevation+1` ⇒ `Ocean == false` | `Elevation(23) < SeaLevel(24)` ⇒ `Ocean == **true**`（海洋） | 同上，方向整体颠倒 |
| `5b` | 期望"海洋 ⇒ Continent 0"却取了 `SeaLevel=elev-1` 的样本 | 该样本实为陆地（Continent 1）；海洋样本是 `SeaLevel=elev+1` | 依赖上述错误样本 |
| `7a` | 比较 `SeaLevel=elev-1` 与 `elev` 期望 `Ocean` 变化 | 两者均为陆地（false）⇒ 无变化 | 同上 |

**处理方式**：
1. **只改测试**：把配置改名为 `seaBelowSurface / seaAtSurface / seaAboveSurface`（语义明确），期望改为契约正确值，`7a` 改为比较 `elev-1` 与 `elev+1`；并在代码中新增**契约锚点注释**（`Ocean = Elevation < SeaLevel` 的三种情形）以防再次写反。
2. **生产代码零改动**：`WorldSkeletonProvider.cs` 哈希 `E28316E8C5749D7F` 自创建起未变（`WorldSkeletonFacts.cs`、`WorldGenerationSettings.cs`、`HeightFunction.cs` 亦未变）。
3. **证据保留**：失败日志 `round42b_*` **原样保留**，与修正后的 `round42c_*` 并列，可复核修正前后差异。

> 该次失败恰好验证了"验证器能把真实问题反映为退出码 1"——只是这次被抓住的是测试自身。

### 12.9 最终结论（与 §11 一致，叠加本轮实测）

**WorldSkeleton 事实适配层与独立验证能力完成；正式生产流程尚未接入，当前玩家可见世界不变。**

- 适配层与验证器**已通过真实 Unity Batchmode 验证**：`44/44` 断言 PASS、退出码 0；注入失败时退出码 1（传播实证）。
- 既有两个验证器**回归通过**（`45` 条 / `Checks=47 Failures=0`），冻结表 **349/0/0**，未重钉任何哈希。
- `WorldSkeletonProvider` 在生产侧引用数仍为 **0**；未修改 Bootstrap / GeologyWorldGenerator / DensityTerrain / IBiomeProvider / 场景 / Prefab / 材质 / 地形配置；未改 `SeaLevel`/`WorldHeight`/`Elevation`/`Ocean`/`Continent` 任何规则。
- 玩家可见世界仍是 `SampleScene.unity` 中的 Legacy `DensityTerrain`（哈希未变）。