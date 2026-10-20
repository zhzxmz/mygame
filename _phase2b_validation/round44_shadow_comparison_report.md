# R44 — Legacy DensityTerrain × World V2 影子高度对比验证器（已实施并真跑）

- 项目：`D:\gamemaking\My project`（Unity **2022.3.62f1c1**）
- 分支 / HEAD：`main` / **`37246478ed354a1e9a0ce7a3a3d9457caf188062`**（未变）
- 本轮性质：**新增 1 个独立 Editor 验证器 + 真跑 5 次 + 差异基准测量**。未修改任何生产代码、场景、Prefab、Asset、既有验证器、契约或冻结表。
- **结论前提**：两套高度函数**尚未统一**，"高度不相等"是**预期事实**，本轮**不**将其判为生产故障。

---

## 1. 现场基线与保护结果

| 项 | 轮前实测 | 结论 |
|---|---|---|
| HEAD | `37246478ed354a1e9a0ce7a3a3d9457caf188062` | 与基线一致 |
| 分支 / 上游 | `main` / `origin/main` | 一致 |
| 暂存区 | 空 | 未暂存 |
| `git status --porcelain` | 31 条（`M` 20 + `??` 11） | 与 R43 收尾一致 |
| `git diff --stat` | `20 files changed, 390 insertions(+), 142 deletions(-)` | 与 R43 收尾一致 |

轮前内容哈希基线（全部 OK）：`progress.md D638D271767BFFF9`、R41 `52FDF4479271BBBA`、R42 `A645487A4ADD4E88`、R43 `E716B053A47D7D78`、`WorldSkeletonProvider.cs E28316E8C5749D7F`、`WorldSkeletonProviderValidation.cs 5084FCD39726586C`、`DensityTerrain.cs 0EB935DBA79CBD40`、`DensityFieldGenerator.cs 702BEFA889002006`、`SampleScene.unity 8BE17EDAF421908E`。
冻结表复算：**349 / 匹配 349 / 不符 0 / 缺失 0**。目标文件 `V2SkeletonShadowComparisonValidation.cs(+.meta)` 轮前不存在、在既有 Editor 文件中引用数 0（**不在冻结表保护范围**）。

---

## 2. 新增文件与真实接口

| 文件 | 行数 | 字节 | SHA256（前16） | 说明 |
|---|---|---|---|---|
| `Assets/Editor/V2SkeletonShadowComparisonValidation.cs` | **655** | 32164 | `D12164861625E43C` | 新增验证器 |
| `Assets/Editor/V2SkeletonShadowComparisonValidation.cs.meta` | 11 | 243 | `7C4CB6415936554A` | Unity 自动生成，GUID `bcabbe3b111e2e3478daf2ae526abd12` |
| `_phase2b_validation/round44_shadow_comparison_report.md` | 本报告 | — | — | — |
| `_phase2b_validation/round44_*_run.log` (+`_stdout/_stderr`) | — | — | — | 5 次真实运行证据 |

**真实接口**（无命名空间，与既有验证器一致）：

```csharp
public static class V2SkeletonShadowComparisonValidation
{
    [MenuItem("Tools/World V2/Validate Skeleton Shadow Comparison")]
    public static void Validate();   // 33 条断言（自检开关开启时 34 条）

    public static void RunBatch();   // Batchmode 入口：Exit(ExitCodeFor(failures))
}
```

**实现前的源码复核（不依赖 R43 行号，本轮重新确认）**：

| 复核项 | 结论 |
|---|---|
| `DensityFieldGenerator.Generate(...)` | `public static`，21 参数（含 `out MaterialField`）；`HeightField` 模式 ⇒ `FillHeightField` ⇒ `ComputeSurfaceHeight`（当前 L483，`private static`） |
| `ComputeSurfaceHeight` 可访问性 | **`private static`** ⇒ 不调用、不改访问级（按 R44 §4 选择只读采样路径） |
| `DensityField` 只读 API | `Origin` / `Size` / `SampleCountX,Y,Z` / `CellSizeX,Y,Z` / 索引器 / `GetLocalPosition` |
| `DensityTerrain.Regenerate()` | `public`，会建 Chunk GameObject + Mesh + Collider（**本验证器不使用**，见下） |
| `WorldSkeletonProvider.TryGetFacts` | `(WorldCoordinate, WorldGenerationSettings, out WorldSkeletonFacts)` 与 `(int,int,int, WorldGenerationSettings, out WorldSkeletonFacts)` |
| `WorldGenerationSettings` | `struct`；六参数构造 ⇒ `SeaLevel = DefaultSeaLevel(64)`；七参数可显式 |
| `HeightFunction.GetTerrainHeight(int worldX, int worldZ, WorldGenerationSettings)` | 用于"错列检测"的独立真值 |
| `WorldSkeletonFacts` | `IsValid` / `Elevation` / `Ocean` / `Continent` |

**为什么不创建 `DensityTerrain` 测试实例**（R44 §4 的备选路径，已采用并如实记录）：
1. `DensityTerrain.OnEnable` → `RequestRegenerate()` → `Regenerate()` 会创建 4 个 Chunk GameObject + Mesh + MeshCollider（`TerrainChunkView`/`MeshFilter`/`MeshRenderer`/`MeshCollider`），属重型副作用；
2. 这些对象受 `debugCutaway=1`、`useVoxelAwareMeshPrecut=1` 影响，会把"调试剖切"与"Pre-cut 几何"混入对比，超出本验证所需的"列地表高度"；
3. 列高的真实来源是 `DensityFieldGenerator.Generate`（`BuildChunk` 内的唯一生成调用），它是**纯静态函数**，不接触 GameObject / 场景 / Transform。

因此本验证器**不创建任何对象**，并对此做了前后一致性断言（见 §4 隔离性三条）。

---

## 3. 临时坐标映射公式与配置

### 3.1 映射（**仅测试用，不是正式契约**）

```
worldX = RoundToInt(legacyLocalX)        // 距 0.5 舍入（Mathf.RoundToInt）
worldZ = RoundToInt(legacyLocalZ)
worldY = 0                               // 仅需落在 [0, WorldHeight-1]，不参与 Elevation（列级量）
```

- 有效**前提**：地形根 Transform 为单位变换（位置为整数 0、旋转 identity、缩放 1）时，Terrain Local Space 与 World Space 数值重合（R43 已从 `SampleScene.unity` 核验：`m_LocalPosition=(0,0,0)`、`m_LocalRotation=identity`、`m_LocalScale=(1,1,1)`、无父节点）。
- **本轮该前提未在同一进程内复验**：batchmode 未加载含 `DensityTerrain` 的场景（`Resources.FindObjectsOfTypeAll<DensityTerrain>()` = 0），因此验证器走"无实例"分支并**如实记录为未复验**（不是静默假设）。
- 负坐标：本轮采样全部落在 `[0, 75]×[0, 50]`，**未涉及负坐标**；`ChunkSpace` 的负坐标规则本身在源码中自注"尚未最终确定"，本轮不做判断。
- 舍入误差（实测）：**X 最大 0.0000**（`CellSizeX = 75/75 = 1` ⇒ 网格 X 恰为整数）、**Z 最大 0.3333**（`CellSizeZ = 50/75 = 0.6667` ⇒ 网格 Z 为 0.6667 的倍数）。

### 3.2 两侧配置（全部显式记录）

**Legacy 侧（复现场景序列化参数，仅作为输入，不改任何生产数据）**：
`densityMode=HeightField`、`resolution=75`、`origin=(0,0,0)`、`size=(75,37,50)`、`useNoise=true`、`height=15.7`、`noiseScale=0.15`、`noiseStrength=2.37`、`cavityCenter=(15.5,5.23,19.1)`、`cavityRadius=6.13`、`grassDepth=1.9`、`dirtDepth=0.65`、`useLegacyMaterialRules=false`、`weatheringStrength=0.8`、`weatheringDepth=8`、`gentleSlopeAngle=15`、`steepSlopeAngle=45`、`gravityStrength=1`、`soilSimulationIterations=4`、`grassStrength=1`。
（`debugCutaway` **未施加**：它是调试剖切，只作用于首 Chunk 的 `x∈[−2,2]`；计入会把调试行为混入基准。）

**World V2 侧**：`new WorldGenerationSettings(12345, 24, 3, 64, 0.06f, 6f)`
⇒ `Seed=12345, TerrainHeight=24, SoilDepth=3, WorldHeight=64, SeaLevel=64(默认), TerrainFrequency=0.06, TerrainAmplitude=6`。
**未修改任何默认值**（`SeaLevel == DefaultSeaLevel` 已断言）。

---

## 4. 采样点明细与差异统计

### 4.1 采样设计

| 项 | 值 |
|---|---|
| 采样列数 | **36**（`X ∈ {0,1,19,38,57,75}` × `Z ∈ {0,1,13,26,38,75}` 网格索引） |
| 覆盖 | X 覆盖 Chunk 首尾（0 与 75）与内部分布；Z 覆盖 0 与 50（首尾）及中部 |
| 每个采样点记录 | Legacy 局部 X/Z、映射后 WorldCoordinate、舍入误差、Legacy 浮点高度、V2 Elevation、有符号差、绝对差、两侧成功与否 |

### 4.2 逐采样点明细（真实运行输出，原文）

```
 i | LegacyLocalX,Z    | WorldX,Z | 舍入误差X,Z   | LegacySurface | V2Elev | signedDiff | absDiff
00 | 0.0000, 0.0000    | 0, 0     | 0.0000, 0.0000 | 15.5354 | 23 | -7.4646 | 7.4646
01 | 0.0000, 0.6667    | 0, 1     | 0.0000, 0.3333 | 15.5108 | 24 | -8.4892 | 8.4892
02 | 0.0000, 8.6667    | 0, 9     | 0.0000, 0.3333 | 16.7028 | 26 | -9.2972 | 9.2972
03 | 0.0000, 17.3333   | 0, 17    | 0.0000, 0.3333 | 14.9266 | 27 | -12.0734 | 12.0734
04 | 0.0000, 25.3333   | 0, 25    | 0.0000, 0.3333 | 16.1376 | 24 | -7.8624 | 7.8624
05 | 0.0000, 50.0000   | 0, 50    | 0.0000, 0.0000 | 14.7363 | 25 | -10.2637 | 10.2637
06 | 1.0000, 0.0000    | 1, 0     | 0.0000, 0.0000 | 15.9298 | 24 | -8.0702 | 8.0702
07 | 1.0000, 0.6667    | 1, 1     | 0.0000, 0.3333 | 15.9052 | 24 | -8.0949 | 8.0949
08 | 1.0000, 8.6667    | 1, 9     | 0.0000, 0.3333 | 17.0113 | 26 | -8.9887 | 8.9887
09 | 1.0000, 17.3333   | 1, 17    | 0.0000, 0.3333 | 14.6246 | 27 | -12.3754 | 12.3754
10 | 1.0000, 25.3333   | 1, 25    | 0.0000, 0.3333 | 16.1587 | 24 | -7.8413 | 7.8413
11 | 1.0000, 50.0000   | 1, 50    | 0.0000, 0.0000 | 14.3058 | 25 | -10.6942 | 10.6942
12 | 19.0000, 0.0000   | 19, 0    | 0.0000, 0.0000 | 15.9298 | 27 | -11.0702 | 11.0702
13 | 19.0000, 0.6667   | 19, 1    | 0.0000, 0.3333 | 16.2031 | 27 | -10.7969 | 10.7969
14 | 19.0000, 8.6667   | 19, 9    | 0.0000, 0.3333 | 15.8618 | 28 | -12.1382 | 12.1382
15 | 19.0000, 17.3333  | 19, 17   | 0.0000, 0.3333 | 15.6715 | 29 | -13.3285 | 13.3285
16 | 19.0000, 25.3333  | 19, 25   | 0.0000, 0.3333 | 14.8583 | 25 | -10.1418 | 10.1418
17 | 19.0000, 50.0000  | 19, 50   | 0.0000, 0.0000 | 14.0223 | 20 | -5.9777 | 5.9777
18 | 38.0000, 0.0000   | 38, 0    | 0.0000, 0.0000 | 16.7028 | 23 | -6.2972 | 6.2972
19 | 38.0000, 0.6667   | 38, 1    | 0.0000, 0.3333 | 16.9826 | 23 | -6.0174 | 6.0174
20 | 38.0000, 8.6667   | 38, 9    | 0.0000, 0.3333 | 14.6020 | 22 | -7.3980 | 7.3980
21 | 38.0000, 17.3333  | 38, 17   | 0.0000, 0.3333 | 16.3684 | 21 | -4.6316 | 4.6316
22 | 38.0000, 25.3333  | 38, 25   | 0.0000, 0.3333 | 14.4710 | 21 | -6.5290 | 6.5290
23 | 38.0000, 50.0000  | 38, 50   | 0.0000, 0.0000 | 15.4936 | 25 | -9.5064 | 9.5064
24 | 57.0000, 0.0000   | 57, 0    | 0.0000, 0.0000 | 14.8201 | 24 | -9.1799 | 9.1799
25 | 57.0000, 0.6667   | 57, 1    | 0.0000, 0.3333 | 15.1605 | 24 | -8.8395 | 8.8395
26 | 57.0000, 8.6667   | 57, 9    | 0.0000, 0.3333 | 15.8324 | 23 | -7.1676 | 7.1676
27 | 57.0000, 17.3333  | 57, 17   | 0.0000, 0.3333 | 15.7676 | 22 | -6.2324 | 6.2324
28 | 57.0000, 25.3333  | 57, 25   | 0.0000, 0.3333 | 15.3956 | 23 | -7.6044 | 7.6044
29 | 57.0000, 50.0000  | 57, 50   | 0.0000, 0.0000 | 14.6223 | 28 | -13.3777 | 13.3777
30 | 75.0000, 0.0000   | 75, 0    | 0.0000, 0.0000 | 16.2517 | 20 | -3.7483 | 3.7483
31 | 75.0000, 0.6667   | 75, 1    | 0.0000, 0.3333 | 16.4734 | 20 | -3.5266 | 3.5266
32 | 75.0000, 8.6667   | 75, 9    | 0.0000, 0.3333 | 16.1043 | 19 | -2.8957 | 2.8957
33 | 75.0000, 17.3333  | 75, 17   | 0.0000, 0.3333 | 14.4642 | 19 | -4.5358 | 4.5358
34 | 75.0000, 25.3333  | 75, 25   | 0.0000, 0.3333 | 15.3462 | 19 | -3.6538 | 3.6538
35 | 75.0000, 50.0000  | 75, 50   | 0.0000, 0.0000 | 15.8249 | 26 | -10.1751 | 10.1751
```

### 4.3 差异统计（真实运行输出）

```
[V2Shadow] 差异汇总：n=36 | Legacy 范围=[14.0223, 17.0113] | V2 范围=[19, 29]
           | signedDiff min=-13.3777 max=-2.8957 | maxAbs=13.3777 | meanAbs=8.2301 | 正/负/零=0/36/0
```

| 指标 | 值 |
|---|---|
| 有效对比样本数 | **36 / 36** |
| Legacy 高度范围 | **[14.0223, 17.0113]**（幅度 2.9890） |
| V2 Elevation 范围 | **[19, 29]**（幅度 10） |
| 有符号差（Legacy − V2） | **min = −13.3777，max = −2.8957，全部 36 项为负** |
| 最大绝对差 | **13.3777** |
| 平均绝对差 | **8.2301** |
| 平均有符号差 | ≈ **−8.2301**（因 36/36 同号） |

---

## 5. 每类差异的证据、解释与不确定性

> 归因原则：只有源码或实测能支持的写成确定结论，其余标"待查"。**"两套高度不相等"本身不判为生产 Bug**——两套系统尚未约定同一高度函数。

### 类别 1 —— 噪声算法与参数不同（**确定，源码 + 实测**）

| 证据 | 内容 |
|---|---|
| 源码 | Legacy：`Mathf.PerlinNoise(x·noiseScale, z·noiseScale)·2−1`（`DensityFieldGenerator.cs:495`，**无种子**）；V2：整数 hash + smoothstep 双线性（`HeightFunction.cs:114–136`，**带 Seed**） |
| 参数 | Legacy `height=15.7` / `amplitude=2.37`（float）；V2 `TerrainHeight=24` / `TerrainAmplitude=6`（int/float，且经 `round`+`clamp`） |
| 实测（幅度） | Legacy 实测幅度 **2.989**（≈2×2.37 的样本内实际摆动）；V2 实测幅度 **10**（≈2×6 的部分摆动）⇒ 幅度比 ≈1:3.3，与参数比 `6/2.37=2.53` 同向 |
| 实测（同号性） | 36/36 全部为负 ⇒ **存在系统性偏移**，而非随机正负抵消 |

### 类别 2 —— Legacy 浮点局部坐标与 V2 整数世界坐标的映射差异（**确定，实测可量化**）

| 证据 | 内容 |
|---|---|
| 实测 | X 舍入误差 **0.0000**（`CellSizeX=1` ⇒ 网格 X 恰好整数）；Z 舍入误差 **最大 0.3333**（`CellSizeZ=0.6667`） |
| 对高度的影响（用相邻列梯度实测估算） | 以每个 X 的 `z=0` 与 `z=0.6667` 两列梯度外推 0.3333 的位移：\|Δh\| ≈ 0.0123 / 0.0123 / 0.1367 / 0.1399 / 0.1702 / 0.1109（X=0/1/19/38/57/75）⇒ **≤ 0.17** |
| 占比 | 0.17 / 8.2301 ≈ **2.1%** ⇒ 映射误差是**次要项**，不能解释 8.23 的平均差 |

### 类别 3 —— 高度量化、钳制与合法范围差异（**确定，源码 + 实测**）

| 证据 | 内容 |
|---|---|
| 量化 | V2 `Elevation` 为 **int**（`round` 后取整），Legacy 为 **float**；实测 V2 全部为整数、Legacy 全部带小数 ⇒ 至少 0.5 的亚体素差 |
| 钳制 | V2 `clamp(..., 0, WorldHeight−1)`；Legacy **无钳制**。本次 V2 实测范围 `[19,29]` 远离 `[0,63]` 边界 ⇒ **钳制未触发**，本样本内该类贡献 ≈0 |
| 合法范围 | Legacy 只需 `[0, ChunkSize.y=37]`（实测 `[14.02,17.01]` 满足）；V2 需 `[0, WorldHeight−1=63]` |

### 类别 4 —— 原点、单位或缩放转换造成的差异（**本样本内为 0，但前提未在本进程复验**）

| 证据 | 内容 |
|---|---|
| 单位 | Legacy 体素 1 m（`voxelSize=1`、`chunkVoxelSize=(75,37,50)`）；V2 世界整数体素 ⇒ 单位一致 |
| 原点 | Legacy `origin=(0,0,0)`；映射 `worldX=RoundToInt(localX)` ⇒ 原点一致 |
| 缩放/旋转 | 场景根 Transform 为恒等（R43 从场景文件核验）；**本轮 batchmode 未加载含 `DensityTerrain` 的场景**（实例数 0），故该前提**未在同一进程内复验** ⇒ 标为"证据来自序列化文件，非运行期复验" |
| 结论 | 若前提成立，本类别贡献为 **0**；若前提不成立（根节点被移动/旋转/缩放），映射会整体失效 ⇒ **这是本轮测量的最大外部风险** |

### 类别 5 —— 无法由现有证据解释的差异（**待查**）

| 项 | 状态 |
|---|---|
| 逐列差值的"相位相关"程度 | **待查**：本轮未计算 Legacy 与 V2 高度的相关系数；残差范围 `[−5.15, +5.33]`（signedDiff 减去均值 −8.2301）与"两套噪声振幅差 + 相位无关"的预期**一致**，但未做统计检验 |
| 系统性偏移的精确分解 | 预期基准偏移 `V2(24) − Legacy(15.7) = +8.3` ⇒ 预测 `signedDiff ≈ −8.3`；实测均值 **−8.2301**，偏差 **+0.0699（0.84%）** ⇒ 与"参数基准差主导、噪声项均值≈0"的解释**高度一致**，但不能断言为严格证明 |
| `debugCutaway` 的影响 | 未纳入对比（已剔除）；其对可见世界（首 Chunk `x∈[−2,2]`）的实际视觉影响**未渲染验证** |

### 5.1 归因小结（证据强度）

| 差异来源 | 证据强度 | 量级（实测/推算） |
|---|---|---|
| 参数基准差（`height` 15.7 vs `TerrainHeight` 24） | **强**（预测 −8.3 vs 实测均值 −8.2301） | ≈ −8.3 |
| 噪声振幅差（2.37 vs 6） | **中-强**（幅度比同向） | 残差范围 ±5.3 |
| 噪声算法/相位不同 | **强（源码）** | 不可分离，体现为逐列无规律差异 |
| 量化（int vs float） | **强（源码+实测）** | ≤ 0.5 |
| 映射舍入（Z ≤0.3333） | **强（实测量化）** | ≤ 0.17 |
| 钳制 | 确定但**未触发** | 0 |
| 原点/单位/缩放 | 单位与原点一致；缩放前提**未运行期复验** | 0（前提成立时） |

---

## 6. 四个（实跑五次）验证器的真实运行结果

命令模板：`"D:\gamemaking\2022.3.62f1c1\Editor\Unity.exe" -batchmode -nographics -quit -projectPath "D:\gamemaking\My project" -logFile <log> -executeMethod <M> [附加开关]`

| # | 入口 | 附加 | 退出码 | 耗时 | PASS 行 | FAIL 行 | error CS | warning CS | 重编译 | 崩溃/超时 | 日志 |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | `V2SkeletonShadowComparisonValidation.RunBatch` | — | **0** | 18.2 s | **33** | **0** | 0 | 4（既有无关） | **是（首次全量）** | 无 | `round44_V2SkeletonShadowComparisonValidation_run.log`（135173 B） |
| 2 | 同上 | `-wsShadowSelfFailure` | **1** | 15.1 s | 34 | **1（仅注入项）** | 0 | 0 | 否（缓存） | 无 | `..._selffail_run.log`（129612 B） |
| 3 | `WorldSkeletonProviderValidation.RunBatch` | — | **0** | 14.1 s | 45 | **0** | 0 | 0 | 否 | 无 | `round44_WorldSkeletonProviderValidation_run.log`（105188 B） |
| 4 | `WorldV2ParameterPipelineValidation.RunBatch` | — | **0** | 14.1 s | 46 | **0** | 0 | 0 | 否 | 无 | `round44_WorldV2ParameterPipelineValidation_run.log`（110681 B） |
| 5 | `GeologyRuntimeIntegrationValidation.RunBatch` | — | **0** | 15.2 s | 48 | **0** | 0 | 0 | 否 | 无 | `round44_GeologyRuntimeIntegrationValidation_run.log`（121953 B） |

- 验证器自报汇总原文：
  - 运行 1：`[V2Shadow] Legacy × World V2 影子高度对比 ⇒ checks=33 failures=0（全部通过；exit 0）`
  - 运行 2：`⇒ checks=34 failures=1（存在失败项；exit 1）`，唯一 FAIL 为 `[自检] 命令行开关注入的故意失败`
  - 运行 3：`checks=44 failures=0（全部通过；exit 0）`
  - 运行 4：`failures=0（全部通过）`
  - 运行 5：`Checks = 47 / Failures = 0 / Result = PASS`
- `stdout` / `stderr`：全部 **0 B**（Unity 输出走 `-logFile`）。
- 运行 1 的 4 条警告**均为既有、与本次无关**：`VoxelCoordinateMigrationStageB3CValidation.cs(2130,50) CS0162`、`MiningSurfaceIntegrityValidation.cs(432,24) CS0414`（缓存运行 0 警告）。
- 崩溃特征正则（`Crash!!!`/`Aborting batchmode`/`Unhandled exception`/`StackOverflowException`/`Fatal error`/`Native Crash`/`Received signal`）命中 **0**；无超时。
- Unity 环境：本轮**正常启动**（无许可证错误，`licErr=0`），全部运行均含真实编译/执行证据。

### 6.1 新增验证器 33 条断言（全部 PASS）与 10 项要求对应

| R44 §6 要求 | 覆盖断言 | 实测 |
|---|---|---|
| 1 测试实例创建且不修改正式 `DensityTerrain` | 隔离性四条（不创建对象 / `DensityTerrain` 0→0 / GameObject 1→1 / 场景未脏） | PASS |
| 2 两侧都能产生可记录结果 | Legacy 非空 + 网格 76³ + CellSize 一致；V2 36/36 成功 | PASS |
| 3 成功结果满足 `IsValid` 契约 | 「全部成功结果 IsValid == true（无效 0）」 | PASS |
| 4 逐采样点一一对应、不串列 | 「facts.Elevation == HeightFunction(X,Z)，不一致 0」 | PASS |
| 5 重复查询稳定性 | 「Legacy 两次生成逐值相同」「V2 重复查询逐值相同」 | PASS |
| 6 统计有符号/绝对/最大/平均绝对 | 「差值指标有限」「平均 ≤ 最大」+ 汇总行 | PASS |
| 7 两侧边界与失败独立记录 | V2 三条失败路径（Y=−1 / Y=WorldHeight / `default(settings)`）+ Legacy 退化列独立计数 | PASS |
| 8 高度不等**不**误报生产故障 | 「已实测到差异（maxAbs=13.3777）但未产生任何失败 ⇒ 高度不等不被当作生产故障」 | PASS |
| 9 无效/缺失样本不计入对比 | 「失败路径样本未被误计入有效对比（有效对比仍为 36 列）」 | PASS |
| 10 断言失败 ⇒ 非零退出码 | 退出码映射自检 3 条 + `-wsShadowSelfFailure` 实测 exit 1 | PASS |

---

## 7. 冻结表复核

- 7 张冻结表独立静态复算：**合计 349 / 匹配 349 / 不符 0 / 缺失 0**。
- 新增文件 `V2SkeletonShadowComparisonValidation.cs` 在 7 张表中的登记数 = **0** ⇒ **未重钉任何哈希**。
- 运行时侧：`GeologyRuntimeIntegrationValidation` 的 `GE7`（25 个 Critical 文件）与 `GE9`（比对 40 / 授权 6 / 跳过 0 / 缺失 0）**均 PASS**（运行 5）。

---

## 8. 正式场景与生产代码未修改的证据

| 判据 | 实测 |
|---|---|
| `git diff --stat` | 轮前轮后均为 `20 files changed, 390 insertions(+), 142 deletions(-)` |
| `git status --porcelain` | 轮前 31 条 → 轮后 **33 条**（`M` 20 + `??` 13）；新增的 2 条正是本轮 `V2SkeletonShadowComparisonValidation.cs` 与其 `.meta` |
| HEAD / 暂存区 | 未变 / 空；未执行 `commit`/`push`/`reset`/`clean`/`stash`/`checkout`/`add` |
| 生产与 Legacy 文件哈希 | `DensityTerrain.cs 0EB935DBA79CBD40`、`DensityFieldGenerator.cs 702BEFA889002006`、`TerrainChunk.cs 52AE93A43B97E79B`、`WorldSkeletonFacts.cs 98B59916CA9CDBCC`、`WorldSkeletonProvider.cs E28316E8C5749D7F` —— **全部未变** |
| 既有验证器 | `WorldSkeletonProviderValidation.cs 5084FCD39726586C`（其运行 PASS 45）—— **未修改** |
| 场景 / Prefab / Asset / 材质 | `SampleScene.unity 8BE17EDAF421908E` **未变**；未创建或修改任何 Prefab / 材质 |
| 契约文档 | `WorldSkeleton_V1_Minimal_Contract.md 6C764EE847E27DDA` 等 —— **未修改** |
| 历史报告 | `progress.md D638D271767BFFF9`、R41 `52FDF4479271BBBA`、R42 `A645487A4ADD4E88`、R43 `E716B053A47D7D78` —— **全部未变** |
| 运行时副作用 | 验证器内断言：GameObject 数 1→1、`DensityTerrain` 0→0、场景 `isDirty` false→false ⇒ 未创建/未污染场景 |
| 临时文件 | 本轮分析中间件 `_tmp_r44_*` 已在收尾清理（残留 0） |

---

## 9. 当前仍缺少哪些统一契约，与后续最小实施建议

### 9.1 仍缺少的统一契约

| # | 缺失契约 | 为何必须 |
|---|---|---|
| C1 | **Legacy 的随机种子语义**（`Mathf.PerlinNoise` 无 Seed） | 无 Seed ⇒ 无法构造可追溯的 `WorldGenerationSettings`（R43 §5.2）；决定"同 Seed 同世界"能否成立 |
| C2 | **世界坐标契约**（Terrain Local ↔ 整数世界坐标的正式映射） | 本轮只能用"根恒等 ⇒ Local==World"的**临时**映射；根节点一变即失效。正式契约需定义原点/单位/负坐标/Chunk 对齐 |
| C3 | **高度参数的单一载体**（`height` / `noiseScale` / `noiseStrength` 的归属） | 当前 Legacy 与 V2 各有一套参数，且语义不同（Perlin 参数 vs 整数 hash 参数）⇒ 需要中立结构，**不能复用** `WorldGenerationSettings` 假装等价 |
| C4 | **高度函数统一（或明确的"双函数共存"政策）** | 决定 V2 能否影响可见世界；统一即外观变更，需 owner 批准 |
| C5 | `WorldHeight` / `SeaLevel` 在 Legacy 世界的等价语义 | 决定 `Ocean`/`Continent` 何时可用（当前默认 64/64 ⇒ 全 Ocean） |
| C6 | 负坐标规则（`ChunkSpace.cs:7-8` 自注"未最终确定"） | 决定长期映射与跨 Chunk 一致性 |
| C7 | Chunk 边界土壤/材料连续性策略 | 若未来 V2 参与材料，需先定边界口径 |

### 9.2 后续最小实施建议（按顺序，每步独立可回退）

| 阶段 | 最小改动 | 出口标准 |
|---|---|---|
| **A（已完成，本轮）** | 影子对比验证器（零生产改动） | exit 0 / 33 PASS；差异基准已建立（meanAbs=8.2301，maxAbs=13.3777） |
| **B（建议下一步，需批准）** | **映射契约的只读固化**：把"Terrain Local ↔ 世界整数坐标"的临时映射抽成**只读、可单测的纯函数**（新文件，不改 Legacy），并加入"根恒等"前提的显式校验 | 新映射函数单测通过；影子对比在"根恒等"前提下逐列一致；Legacy 文件哈希不变 |
| **C（需 owner 决定）** | Seed / 高度参数载体（C1/C3）：为 Legacy 引入可追溯的配置载体（**保持 Perlin 公式不变**，只把数值搬到单一来源），并证明 `Regenerate` 前后 `DensityField` 逐样本 bitwise 相同 | 搬迁前后 `DensityField` 与 Mesh 哈希完全一致 |
| **D（需 owner 批准外观变更）** | 高度函数统一（C4）+ `WorldHeight`/`SeaLevel` 语义（C5） | 新世界基线 + 视觉验收 + 挖掘/碰撞回归 + 回退路径 |
| **E（远期）** | Region / Biome / Ocean / Magic / Resource / Ecology 各自独立接线轮 | 每项 owner 裁决 + 独立验证器 |

**门禁（每阶段适用）**：冻结表 349/0/0；`sealevel_round7_progress.md` 1032 行未改；不得修改 `SeaLevel`/`WorldHeight` 默认值；不得让 V2 依赖 Legacy 类型（`LS65`/`RAB-L2`）；不得让 Legacy 依赖 V2 类型（本轮红线）；影子对比必须持续可跑（作为差异突变护栏）。

---

## 10. 结论

**Legacy 与 World V2 的高度差异已被独立测量**（36 列，Legacy ∈[14.0223,17.0113] / V2 ∈[19,29] / 平均绝对差 **8.2301** / 最大绝对差 **13.3777** / 36 列全部为负，存在系统性偏移）；
**本轮未统一生成规则**（两套噪声函数、参数、量化与坐标空间均保持原状）、**未接入正式生产流程**（`WorldSkeletonProvider` 生产调用者仍为 **0**；未修改 `DensityTerrain`/`DensityFieldGenerator`/`TerrainChunk`/场景/参数）、**当前玩家可见世界不变**（`SampleScene.unity` 与全部 Legacy 文件哈希未变；运行世界仍是 Perlin 地表 + 材料 V2 流水线）。

不得据此声称：两套高度等价、V2 已影响可见世界、或高度差异是生产 Bug（两套系统尚未约定同一高度函数）。