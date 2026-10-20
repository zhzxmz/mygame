# R35：`GeologyRuntimeIntegrationValidation` 的 SeaLevel 测试配置一致性审查（只读）

- 性质：**只读审查**（本阶段未修改任何文件，仅新增本报告）。未修改 `.cs` / 场景 / Prefab / 配置资产 / 冻结验证器 / 进度记录。
- 未执行 commit / push / reset / clean。
- 现场：`main`，HEAD `37246478ed354a1e9a0ce7a3a3d9457caf188062`，staged = **0**，`git status --short` = 25 条。
- 349 条冻结哈希复核：**349 / 失配 0 / 缺失 0**。
- **声明**：本报告为**测试配置一致性审查**，**不构成** World V2 生产端到端验证。

---

## 1. `BootstrapRig` 完整定义与初始化（`GeologyRuntimeIntegrationValidation.cs` L909–L980）

```csharp
909:     /// <summary>一套独立的 Bootstrap 装置：独立 Root / Player / 世界 / 组装。</summary>
910:     private sealed class BootstrapRig : IDisposable
911:     {
912:         public readonly GameObject HostObject;
...
916:         public readonly VoxelWorldStreamingBootstrap Bootstrap;
917:
918:         public readonly WorldGenerationSettings GenerationSettings;      // ← 参考设置（只读字段）
919:
920:         public BootstrapRig(string label)
921:         {
922:             HostObject      = new GameObject("StageE_" + label + "_Host");
923:             ChunkRootObject = new GameObject("StageE_" + label + "_ChunkRoot");
924:             PlayerObject    = new GameObject("StageE_" + label + "_Player");
926:             ChunkRoot = ChunkRootObject.transform;
928:             Bootstrap = HostObject.AddComponent<VoxelWorldStreamingBootstrap>();
929-933:      // Player / ChunkRoot / SubmeshMaterials / BuildColliders / TickEveryFrame
935-937:      // ViewDistanceChunks=1 / UnloadHysteresisChunks=1 / VerticalChunkCount=1
939:             Bootstrap.Seed            = 12345;
940:             Bootstrap.TerrainHeight   = 24;
941:             Bootstrap.SoilDepth       = 3;
942:             Bootstrap.WorldHeight     = 64;
943:             Bootstrap.TerrainFrequency= 0.06f;
944:             Bootstrap.TerrainAmplitude= 6f;
946-948:      // MaxLoadsPerFrame / MaxChunkPairsPerFrame / MaxUnloadsPerFrame = 8
949:
950:             GenerationSettings = new WorldGenerationSettings(              // ← 六参数构造
951:                 Bootstrap.Seed, Bootstrap.TerrainHeight, Bootstrap.SoilDepth,
952:                 Bootstrap.WorldHeight, Bootstrap.TerrainFrequency, Bootstrap.TerrainAmplitude);
953:
954:             Bootstrap.Build();                                            // ← Bootstrap 内部用七参（自带 SeaLevel）
955:         }
```

### 1.1 关键事实

| 项 | 实测 |
|---|---|
| rig 是否设置 `Bootstrap.SeaLevel` | **否 —— 全文件 `Bootstrap.SeaLevel =` 赋值次数 = 0**（L67 的字段初始化器 `= 64` 生效） |
| rig 的 `GenerationSettings.SeaLevel` | 六参构造 ⇒ **`DefaultSeaLevel` = 64** |
| Bootstrap 内部实际设置 | `Bootstrap.Build()` 内 L194–L195 **七参**，`SeaLevel` = `Bootstrap.SeaLevel` = **64** |
| **当前两侧是否一致** | **一致（都是 64）** |

---

## 2. `BootstrapRig.GenerationSettings` 的全部读取点

全文件 `GenerationSettings` 命中 **7 处**，逐条分类：

| 行 | 内容 | 性质 |
|---:|---|---|
| L204 | `new BasicTerrainGenerator(WorldGenerationSettings.Default)` | **不是 rig 字段**（用的是 `Default` 属性） |
| L206 | `new GeologySampler(WorldGenerationSettings.Default, geology)` | 同上 |
| L208 | `WorldGenerationSettings.Default, geology,` | 同上 |
| L550 | 哈希登记行（注释含 "SeaLevel" 字样） | 非代码逻辑 |
| L918 | `public readonly WorldGenerationSettings GenerationSettings;` | **声明** |
| L950 | `GenerationSettings = new WorldGenerationSettings(` | **唯一赋值**（六参） |
| **L302** | **`BasicTerrainGenerator baseline = new BasicTerrainGenerator(rig.GenerationSettings);`** | **唯一读取点（1 处）** |

⇒ `BootstrapRig.GenerationSettings` 在全验证器中**只被读取 1 次**，用于 GE3 的 **Stage-1 基线生成器**。

### 2.1 唯一读取点的上下文（GE3，L301–L330）

```csharp
301:  // 地质确实生效：洞穴开启时世界必须与 Stage 1 基线不同（否则说明生成器没被真正接入）
302:  BasicTerrainGenerator baseline = new BasicTerrainGenerator(rig.GenerationSettings);
303:  int differences = 0;
305-326:  // 逐 Chunk × 逐体素比较 bootstrap.World 的 chunk 与 baseline.Generate(...) 的差异
328:  Check(differences > 0,
329:      "GE3：新世界确实使用地质规则（与 Stage 1 基线存在差异；差异检出 " + differences + " ⇒ 断言非平凡）");
```

---

## 3. 关键断言与调用链

| 断言 | 位置 | 是否涉及 `SeaLevel` / `Ocean` |
|---|---|---|
| GE1 组装/生成器类型/地质开关/抽象类型 | L107–L130 | **否** |
| GE3 流程计数、帧末不变量 | L288–L299 | **否** |
| GE3 与 Stage-1 基线存在差异 | L328 | **间接**（经 L302 的 `rig.GenerationSettings`），但见 §4 |
| GRI 中 `Check(` 总数 | **54** | 其中 `Check(...SeaLevel...)` = **0** |
| GRI 中 `Ocean` 出现次数 | **0** | 无任何海洋语义断言 |

### 3.1 是否有「Bootstrap 实际配置 vs rig 参考设置」的比对断言

**没有。** 证据：
- 全文件 `.Settings` 匹配 = **0**（既无 `rig.Bootstrap.Generator.Settings`，也无 `IWorldGenerator.Settings` 读取）；
- `generationSettings.IsValid` 之外的配置比对不存在；
- GE1 只断言"生成器是 `GeologyWorldGenerator`、地质开关来自显式字段"，**不比对 `WorldGenerationSettings` 各字段值**。

---

## 4. 不一致会造成哪种结果（影响分类）

**结论：`当前断言不受影响`（主判定） + `测试无法覆盖非默认 SeaLevel`（结构性覆盖缺口）。**

### 4.1 为什么"当前断言不受影响"（三重证据）

1. **两侧数值当前相同**：rig 未设置 `Bootstrap.SeaLevel` ⇒ Bootstrap 内部与 rig 参考设置**都是 64**。
2. **唯一消费点不读 SeaLevel**：`rig.GenerationSettings` 只被用于构造 `BasicTerrainGenerator`（L302），而 `BasicTerrainGenerator.cs`（107 行）中 **`SeaLevel` 出现 = 0**，只读取 `settings.IsValid`(L32)、`settings.WorldHeight`(L90)、`settings.SoilDepth`(L93) ⇒ **SeaLevel 对该基线产物无任何影响**。
3. **无比对断言**：不存在"rig 设置 vs Bootstrap 实际设置"的字段级比较（§3.1），因此不存在"因不一致而误报/错通过"的机制。

### 4.2 为什么"无法覆盖非默认 SeaLevel"

- rig 从不给 `Bootstrap.SeaLevel` 赋值（0 次），且 GRI 全文件 `Ocean` = 0、`Check(...SeaLevel...)` = 0 ⇒ **GRI 目前对 SeaLevel / Ocean 语义零覆盖**。
- 即便有人把 `Bootstrap.SeaLevel` 改成非 64，rig 的 `GenerationSettings` 仍会是 64（六参 ⇒ `DefaultSeaLevel`），且**没有任何断言会发现这一分歧** ⇒ 潜在"参考设置失真但测试仍绿"的情况。

### 4.3 误报失败 / 错误通过 / 证据不足？

| 分类 | 判定 | 理由 |
|---|---|---|
| 测试无法覆盖非默认 SeaLevel | **是** | §4.2 |
| 误报失败 | **否（当前）** | 两侧一致且无比对断言；无失败产生机制 |
| 错误通过 | **否（当前）/ 潜在** | 当前无不一致；但若将来 rig 设置 `Bootstrap.SeaLevel ≠ 64`，参考设置会静默保持 64 ⇒ **潜在**错误通过 |
| 当前断言不受影响 | **是（主判定）** | §4.1 三重证据 |
| 证据不足 | **否** | 上述结论均有源码逐行证据 |

---

## 5. 该验证器其他地方是否已覆盖 SeaLevel

| 检查 | 结果 |
|---|---|
| `SeaLevel` 字面出现 | 2 处，**均为哈希登记行的注释文字**（L550 / L766），非逻辑 |
| 含 `SeaLevel` 的 `Check(...)` 断言 | **0** |
| `Ocean` 相关断言 | **0** |
| 结论 | **GRI 完全不覆盖 SeaLevel / Ocean 语义**；非默认 SeaLevel 的覆盖只存在于另一验证器 `WorldV2ParameterPipelineValidation`（A/B/C 组，R9/R20 已实跑） |

---

## 6. 是否建议修正，以及最小修正方案（**本轮未实施，待批准**）

### 6.1 建议

**建议修正**，理由：当前虽无实际影响，但存在**语义陷阱**——rig 自称"镜像 Bootstrap 显式字段"（L903 注释 `Bootstrap 默认值镜像`），却**漏掉 `SeaLevel`**；一旦将来有人给 rig 设置非默认 SeaLevel，会出现"参考设置与实测配置不一致且无断言发现"的静默失真。

### 6.2 最小修正（2 行，仅改测试装置，不触碰断言、不触碰生产代码）

```diff
@@ -939,6 +939,7 @@
             Bootstrap.SoilDepth = 3;
             Bootstrap.WorldHeight = 64;
+            Bootstrap.SeaLevel = 64;                                  // 显式声明 rig 依赖的默认值（可改以覆盖非默认场景）
             Bootstrap.TerrainFrequency = 0.06f;
             Bootstrap.TerrainAmplitude = 6f;
@@ -950,3 +951,3 @@
             GenerationSettings = new WorldGenerationSettings(
                 Bootstrap.Seed, Bootstrap.TerrainHeight, Bootstrap.SoilDepth,
-                Bootstrap.WorldHeight, Bootstrap.TerrainFrequency, Bootstrap.TerrainAmplitude);
+                Bootstrap.WorldHeight, Bootstrap.TerrainFrequency, Bootstrap.TerrainAmplitude, Bootstrap.SeaLevel);
```

### 6.3 预期影响（供批准时评估）

| 维度 | 影响 |
|---|---|
| 断言数量 / 内容 | **不变**（`Check(` 仍 54 处；不改任何断言） |
| 行为差异 | **当前无**：改后 rig 的 `GenerationSettings.SeaLevel` 仍为 64（因 `Bootstrap.SeaLevel = 64`），且唯一消费点 `BasicTerrainGenerator` 不读 SeaLevel |
| 测试能力增益 | ①参考设置与 Bootstrap 配置**语义对齐**；②此后只需把 `Bootstrap.SeaLevel` 改为非默认值，rig 参考设置即自动跟随 ⇒ **为非默认 SeaLevel 覆盖打开通道**（仍需另加断言才算真覆盖） |
| 哈希级联 | **无**：GRI 自身哈希在**全部 7 张冻结表中被登记 0 次**（本轮实测）⇒ 修改 GRI 不会触发任何重钉 |
| 需同步修改 | **无**（不涉及生产代码、其他验证器、登记表、脚本） |
| 需复跑验证 | **需要**：修改后应真实运行 `GeologyRuntimeIntegrationValidation.RunBatch` 确认仍 PASS（预计 45 checks / 0 failures，与 R25 一致） |

### 6.4 备选（零代码）

若不修改代码，则应至少在 GRI 的 `BootstrapRig` 类注释中登记约束：「**本 rig 不得将 `Bootstrap.SeaLevel` 设为非 64，否则参考设置将失真**」。缺点：约束只存在于文档，仍可能被后续修改破坏。

---

## 7. 结论

1. `BootstrapRig.GenerationSettings` **只被读取 1 次**（L302，GE3 的 `BasicTerrainGenerator` 基线），而该消费者**完全不读 `SeaLevel`**。
2. rig **从未设置** `Bootstrap.SeaLevel`（赋值 0 次）⇒ 两侧当前**都是 64**，且**没有配置比对断言** ⇒ **当前断言不受影响**（无误差、无误报）。
3. **GRI 对 SeaLevel / Ocean 零覆盖**（`Check(...SeaLevel...)` = 0、`Ocean` = 0）⇒ **无法测试非默认 SeaLevel**。
4. 存在**潜在**静默失真（若将来 rig 设置非默认 SeaLevel 且消费点变为 SeaLevel 敏感）。
5. 建议按 §6.2 做 **2 行最小修正**（仅测试装置、零断言改动、**零哈希级联**），**但需你批准后才实施**；实施后须复跑 GRI 确认 PASS。
6. 本轮**未修改任何文件**；349 条冻结哈希仍 **0 失配 / 0 缺失**。

---

### 8. 附：本轮执行证据

| 检查 | 命令要点 | 结果 |
|---|---|---|
| `GenerationSettings` 出现 | `Select-String 'GenerationSettings'` | 7 处（1 赋值 + 1 声明 + 1 读取 + 3 `Default` + 1 登记注释） |
| `Bootstrap.SeaLevel =` 赋值 | `Select-String 'Bootstrap\.SeaLevel\s*='` | **0** |
| `.Settings` 读取 | `Select-String '\.Settings'` | **0** |
| `BasicTerrainGenerator` 中 `SeaLevel` | `Select-String 'SeaLevel'` | **0** |
| GRI 被冻结表登记次数 | 遍历 7 张表的字典条目 | **0** |
| `Check(` 总数 / 含 SeaLevel 的 | `Select-String '^\s*Check\('` / `Check\(.*SeaLevel` | 54 / **0** |
| 349 条冻结哈希 | 逐表逐项重算 | **349 / 0 / 0** |
