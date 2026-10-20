# R43 — Legacy DensityTerrain 的 World V2 接入点审计（只读，不实施）

- 项目：`D:\gamemaking\My project`（Unity **2022.3.62f1c1**）
- 分支 / HEAD：`main` / **`37246478ed354a1e9a0ce7a3a3d9457caf188062`**（未变）
- 本轮性质：**只读源码 + 场景序列化文件调查**。未修改任何生产代码 / 验证器 / 场景 / Prefab / 契约 / 冻结表；未执行任何 git 写操作；**未启动 Unity**（无新增运行时证据，见 §10）。
- 唯一新增文件：本报告。

---

## 1. 现场基线与保护检查

| 项 | 实测 | 结论 |
|---|---|---|
| HEAD | `37246478ed354a1e9a0ce7a3a3d9457caf188062` | 与基线**一致** |
| 分支 / 上游 | `main` / `origin/main` | 一致 |
| 暂存区 | 空 | 未暂存 |
| `git status --porcelain` | **31 条**（tracked `M` 20 + `??` 11） | 与 R42 收尾**完全相同** |
| `git diff --stat` | `20 files changed, 390 insertions(+), 142 deletions(-)` | 与 R42 收尾**完全相同** |

历史证据与 R42 产物内容哈希（轮前记录，收尾复核见 §9）：

| 文件 | SHA256（前 16） | 对照期望 | 结果 |
|---|---|---|---|
| `_phase2b_validation/sealevel_round7_progress.md` | `D638D271767BFFF9` | `D638D271767BFFF9` | ✅ |
| `_phase2b_validation/round41_worldv2_production_wiring_audit.md` | `52FDF4479271BBBA` | 同 | ✅ |
| `_phase2b_validation/round42_worldskeleton_provider_report.md` | `A645487A4ADD4E88` | 同 | ✅ |
| `Assets/Scripts/World/V2/WorldSkeletonProvider.cs` | `E28316E8C5749D7F` | 同 | ✅ |
| `Assets/Editor/WorldSkeletonProviderValidation.cs` | `5084FCD39726586C` | 同 | ✅ |
| `Assets/Scripts/World/V2/WorldSkeletonFacts.cs` | `98B59916CA9CDBCC` | 同 | ✅ |

冻结哈希表复算（7 个验证器）：**合计 349 / 匹配 349 / 不符 0 / 缺失 0** ⇒ 无漂移，无需重钉。

Legacy Terrain 调查对象（`Assets/Scripts/Terrain/**`，29 个 `.cs`）关键文件哈希：

| 文件 | 行数 | SHA16 |
|---|---|---|
| `DensityTerrain.cs` | 1855 | `0EB935DBA79CBD40` |
| `DensityFieldGenerator.cs` | 556 | `702BEFA889002006` |
| `ChunkSpace.cs` | 160 | `219FCBB569A413FB` |
| `DensityField.cs` | 87 | `497A5884A21A0822` |
| `TerrainChunk.cs` | 303 | `52AE93A43B97E79B` |
| `TerrainMeshGenerator.cs` | 413 | `00DC3191AB8BA5FA` |
| `VoxelField.cs` | 175 | `253DCBDE4202B2BB` |
| `VoxelDensityAdapter.cs` | 122 | `CF35963093786C15` |

---

## 2. 当前场景实际运行的 DensityTerrain 证据

`Assets/Scenes/SampleScene.unity`：
- 组件脚本 guid `3af3bcfc875f4cbfae75f1f95dee772a` 命中 **1** 次；所属 GameObject 名 **`Terraria`**，`m_Enabled: 1`。
- 根 `Transform`（`&482992370`）：`m_LocalPosition = (0,0,0)`、`m_LocalRotation = identity (0,0,0,1)`、`m_LocalScale = (1,1,1)`、无父节点
  ⇒ **Terrain Local Space 与 World Space 数值一致**（本场景特定事实，不是契约）。

**面板实际序列化参数（决定当前世界外观）**：

| 字段 | 场景值 | 含义 / 影响 |
|---|---|---|
| `terrainSize` | `(83, 20.3, 55.2)` | 仅在 `chunkSize` 为 0 时用于推导 Chunk 尺寸；本场景被 `chunkVoxelSize` 覆盖 |
| `chunkSize` | `(0,0,0)` | 未配置 ⇒ 走推导路径（本次未走到，因为 `chunkVoxelSize` 显式） |
| `chunkCount` | `(2,1,2)` | **共 4 个 Chunk** |
| `chunkStartCoordinate` | `(0,0,0)` | Chunk 坐标从 0 起 |
| `voxelSize` | `1` | 逻辑体素 1 m（Scheme A 已迁移值） |
| `chunkVoxelSize` | `(75,37,50)` | 每 Chunk 体素数 ⇒ 世界尺寸 75×37×50 |
| `resolution` | `75` | 每轴 **75 个 cell ⇒ 76 个采样点**；`CellSize = Size/75 = (1, 0.4933, 0.6667)` |
| `densityMode` | `0` = `HeightField` | 生成模式（唯一影响地表公式分支） |
| `useNoise` | `1` | 启用噪声 |
| `height` | `15.7` | 基准地表高度（**float**） |
| `noiseScale` | `0.15` | Perlin 采样频率 |
| `noiseStrength` | `2.37` | 噪声幅度 ⇒ 地表 ∈ **[13.33, 18.07]** |
| `cavityCenterOffset` / `cavityRadius` | `(15.5,5.23,19.1)` / `6.13` | **仅 `SphereCavity` 模式使用**；当前模式不使用 |
| `useLegacyMaterialRules` | `0` | **走材料 V2 流水线**（非简单分层） |
| `grassDepth` | `1.9` | Grass 厚度上限 |
| `dirtDepth` | `0.65` | 仅旧规则使用（新流水线用风化/沉积推导） |
| `weatheringStrength` / `weatheringDepth` | `0.8` / `8` | 风化产生初始 Dirt |
| `gentleSlopeAngle` / `steepSlopeAngle` | `15` / `45` | 坡度因子 `InverseLerp(15,45,angle)` |
| `gravityStrength` / `soilSimulationIterations` | `1` / `4` | 土壤重力沉积迭代次数 |
| `grassStrength` | `1` | Grass 生成强度 |
| **`debugCutaway`** | **`1`（开启）** | 首 Chunk 内 `x∈[debugCutX−2, debugCutX+2] = [−2,2]` 的样本被置 `density = −1`（**调试剖切**） |
| `debugCutX` / `debugCutWidth` | `0` / `4` | 见上 |
| `useVoxelAwareMeshPrecut` | **`1`（开启）** | **运行走 Voxel-Aware Pre-cut 路径**（非有效密度路径） |
| `grassMaterial` / `dirtMaterial` / `stoneMaterial` | 均 `{fileID: 0}`（未赋值） | 运行时由 `GetMaterials()` 创建材质实例 |
| `regenerateInEditor` | `1` | 编辑器内参数变更即重建 |

**推论**：当前世界 = 4 个 Chunk × (75×37×50 m) = 150 × 37 × 100 m；地表仅由 `height + Perlin 噪声` 决定（**无种子、无海平面、无大陆、无生物群系**）；且首 Chunk 被调试剖切。

---

## 3. 从初始化到生成、挖掘与重建的真实调用链

### 3.1 初始化与生成（每次完整重建）

| 序 | 位置 | 行为 | 调用者 | 被调用者 |
|---|---|---|---|---|
| 1 | `DensityTerrain.cs:213` `OnEnable` | 创建权威 VoxelField，然后请求重建 | Unity 生命周期 | `EnsureVoxelField()`(L226) / `RequestRegenerate()`(L250) |
| 2 | `DensityTerrain.cs:226` `EnsureVoxelField` | `new VoxelField(1f, VoxelState.Solid)` —— **sparse，未记录 = Solid** | L215 / `VoxelField` getter(L163) | `VoxelField` ctor |
| 3 | `DensityTerrain.cs:250` `RequestRegenerate` | 编辑器 `delayCall` 延迟；运行时直接 | L218 / L242(`OnValidate`) | `Regenerate()`(L301) |
| 4 | `DensityTerrain.cs:301` `Regenerate` | `ClearChunks()` → 三重循环遍历 `chunkCount` → `BuildChunk(...)` → 若 precut 开启则重放 Voxel 状态 | 菜单/`OnValidate`/`OnEnable` | `BuildChunk`(L752)、`ReplayStoredVoxelStatesForVoxelAwareGeometry`(L403) |
| 5 | `DensityTerrain.cs:752` `BuildChunk` | 组装参数 → **调用唯一生成入口** → 建 Chunk 对象/网格/碰撞体 | `Regenerate` | `DensityFieldGenerator.Generate`(L783)、`chunk.Build`(L813)、`Precut.Build/Assemble`(L819/835)、`BuildEffectiveDensityField`(L846) |
| 6 | `DensityFieldGenerator.cs:17` `Generate` | 按 `mode` 填 DensityField，再按 `useLegacyMaterialRules` 选材料流水线 | `BuildChunk`（**全库唯一调用点**） | `FillHeightField`(L107) / `FillHeightFieldMaterialV2`(L~220) |
| 7 | `DensityFieldGenerator.cs:107` `FillHeightField` | 逐采样点 `density = surface − localY` | `Generate` | `ComputeSurfaceHeight`(L483) |
| 8 | `DensityFieldGenerator.cs:483` `ComputeSurfaceHeight` | `height + (PerlinNoise(x·noiseScale, z·noiseScale)·2−1)·noiseStrength` | `FillHeightField` | `Mathf.PerlinNoise` |
| 9 | `DensityFieldGenerator.cs` `FillHeightFieldMaterialV2` | 列地表 → 坡度 → 风化 → 重力沉积 → Grass/Dirt/Stone 分层 | `Generate` | `FindSurfaceHeight`(L403) |
| 10 | `TerrainChunk.cs:93` `Build(density, material, isoLevel)` | 缓存三个输入，调 `RebuildMesh()` | `BuildChunk` | `RebuildMesh()`(L124) |
| 11 | `TerrainChunk.cs:141` `RebuildMesh(effectiveDensity)` | `Mesh = TerrainMeshGenerator.Generate(effectiveDensity, Material, IsoLevel)` | `Build` / `RebuildDirtyChunks` / `RebuildChunksAffectedByVoxel` | `TerrainMeshGenerator.Generate`(L54) |
| 12 | `TerrainMeshGenerator.cs:54` `Generate` | Marching Cubes：`density > isoLevel` ⇒ 实体；按三角形中心最近实体角点的 `MaterialField` 值分 3 个子网格 | `RebuildMesh` | `ResolveTriangleMaterial`(L213) / `GetSubMeshIndex`(L243) |
| 13 | `DensityTerrain.cs:853–898` | 创建 `Chunk_x_y_z` GameObject（`HideFlags.DontSave`）→ `TerrainChunkView` + `MeshFilter` + `MeshRenderer` + `MeshCollider` → 注册进 `chunks` / `chunkLookup` | `BuildChunk` | Unity 组件 |

**Pre-cut 路径（本场景开启）**：`BuildChunk` L815–841 → `VoxelAwareMeshPrecut.Build(rawBaseMesh, ...)` → `Assemble(geometry)` → `chunk.ApplyVoxelAwareGeometry(...)`；此时 **不调用** `BuildEffectiveDensityField`。

### 3.2 挖掘（玩家 F 键，运行时）

| 序 | 位置 | 行为 |
|---|---|---|
| 1 | `PlayerMiningController.cs:192+` | 选靶：`TerrainVoxelSelector` → `SelectedVoxel` / `selectedTerrain` |
| 2 | `PlayerMiningController.cs:310` | `targetVoxelField.Set(selectedVoxel, VoxelState.Air)` —— 写权威 sparse VoxelField |
| 3 | `PlayerMiningController.cs:316` | `selectedTerrain.RebuildChunksAffectedByVoxel(selectedVoxel)` |
| 4 | `DensityTerrain.cs:690` `RebuildChunksAffectedByVoxel` | precut **开**（本场景）⇒ L697 → `ApplyVoxelAwareStateChangeAndSync`(L527) → `VoxelAwareMeshRuntimeMutation`（生产路径，更新 Fragment/Visibility/Dirty 并重新 Assemble）<br>precut **关** ⇒ L718 `BuildEffectiveDensityField` + L724 `chunk.RebuildMesh(effectiveDensity)` |
| 5 | `DensityTerrain.cs:974` `SyncChunkMesh` | 把重建后的 Mesh 同步到 `MeshFilter` / `MeshCollider` |

- **旧 V1 挖掘路径 `MineCubeLocal`（L1328，直接改密度）已不在 F 键路径上**：`PlayerMiningController.cs:13` 原文「旧 `TryMine()`（V1 Density Brush）保留供兼容/测试调用，F 键不再走该路径」；生产调用点仅 `PlayerMiningController.cs:147`（旧方法内）与 `TerrainMultiChunkValidation`（Editor）。
- `TerrainModification.cs:190` → `terrain.RebuildDirtyChunks()`：**Legacy** 修改入口，本轮未见生产调用者（仅 Editor 验证器）。

### 3.3 重建（`Regenerate` / `RebuildDirtyChunks`）

- `RebuildDirtyChunks`（L574）：对 `IsDirty` 的 Chunk **重新从 Base Density + VoxelField 派生**（L607/L611）——**不重跑** `DensityFieldGenerator`。
- `Regenerate`（L301）：**丢弃全部 Chunk 并重新调用 `DensityFieldGenerator`**；随后若 precut 开启，按坐标升序重放 `VoxelField` 中的 Air 体素（L403）。
- `TerrainChunk.cs:7` 原文：「Dirty / Rebuild 只消费当前 DensityField / MaterialField，**不重新进行 World Generation**」。

### 3.4 缓存与持久化

| 数据 | 载体 | 生命周期 |
|---|---|---|
| `DensityField`（Base 密度） | `TerrainChunk.Density`（不可变） | Chunk 生命周期内缓存；`Regenerate` 时丢弃 |
| `MaterialField` | `TerrainChunk.Material` | 同上；**重建时从不重算** |
| `Mesh` / `RawMesh` / `Geometry` | `TerrainChunk` | 同上 |
| 挖掘编辑状态 | `DensityTerrain.VoxelField`（sparse `Dictionary`，未记录 = Solid） | 内存中；**无持久化** |
| Chunk GameObject / Mesh / Collider | 运行时派生对象（`HideFlags.DontSave`） | 不写入场景 |

**持久化结论**：`Assets/Scripts/Terrain/**` 全目录对 `PlayerPrefs` / `persistentDataPath` / `File.Write*` / `JsonUtility` / `BinaryWriter` / `ScriptableObject.CreateInstance` 命中 **0**（唯一 `[SerializeField]` 命中在 `TerrainVoxelHighlight`，属高亮显示参数，与地形数据无关）⇒ **当前世界无存档，挖掘结果在重进场景/重生成后丢失**。

---

## 4. 地形高度、密度、材料与坐标转换现状

### 4.1 地表高度：**唯一来源**

`DensityFieldGenerator.ComputeSurfaceHeight`（L483–504）：

```
surface(x_local, z_local) = height + (PerlinNoise(x_local · noiseScale, z_local · noiseScale) · 2 − 1) · noiseStrength
```

- 输入是 **Terrain Local Space 的浮点坐标**（`DensityField.GetLocalPosition`），不是整数世界坐标。
- 噪声是 **`Mathf.PerlinNoise`（无种子、全局）**；参数中**没有 Seed**。
- 结果**未取整、未钳制**；本场景数值范围 **[13.33, 18.07]**。
- 全库 `Mathf.PerlinNoise` 实际调用点**仅此一处**（`HeightFunction.cs:16` / `GeologyNoise.cs:12` / `GeologySampler.cs:34` 均为注释提及）⇒ **地表高度无第二来源**。

### 4.2 密度与 IsoLevel

- `density[x,y,z] = surface − localY`（`FillHeightField` L129）。
- `IsoLevel = 0f`（`DensityTerrain.cs:117`，`private const`）。
- 符号约定：`density > 0` ⇒ 实体；`density ≤ 0` ⇒ 空气；等值面为三角形生成位置。
- **采样点 ≠ 逻辑体素**（`DensityField.cs:7` 原文）：每轴 76 采样点、`CellSize = (1, 0.4933, 0.6667)`，与 1 m 体素格仅在 X 轴对齐。
- 空气密度常量：`VoxelDensityAdapter.AirDensity = −1f`、`SolidDensity = 1f`（L30/L33）。

### 4.3 材料分配（本场景 `useLegacyMaterialRules = 0` ⇒ 材料 V2 流水线）

`FillHeightFieldMaterialV2`（`DensityFieldGenerator.cs` ~L220–401）：

| 步 | 规则 | 依赖 |
|---|---|---|
| 1 | 每列地表高度 = `FindSurfaceHeight`（L403）从密度符号变化线性插值 | DensityField |
| 2 | 坡度因子 `slopeFactor = Clamp01(InverseLerp(gentle, steep, slopeAngle))` | 相邻列地表高差 + `gentleSlopeAngle(15)`/`steepSlopeAngle(45)` |
| 3 | 风化土壤厚度 = Σ`weatheringStrength · exposure · CellSizeY`（仅 `depth ≤ weatheringDepth(8)`），再乘 `(1 − slopeFactor)` | `weatheringStrength/Depth`、坡度 |
| 4 | 重力沉积：`soilSimulationIterations(4)` 轮，按相邻地表高差把土壤向下坡分配 | 坡度、`gravityStrength` |
| 5 | 分层：`density ≤ 0` ⇒ **Air**；`depth ≤ grassThickness`（= min(soil, `grassDepth·grassStrength·(1−slope)`)) ⇒ **Grass**；`depth ≤ soil` ⇒ **Dirt**；否则 **Stone** | 上述结果 |

- 材料枚举：`MaterialType { Air, Grass, Dirt, Stone }`（`MaterialType.cs`，4 值）。
- Mesh 子网格：`Grass / Dirt / Stone` 三个；三角形材料取**最近的实体角点**的材料值（`ResolveTriangleMaterial` L213–240，取不到则 Stone）。
- **逐 Chunk 独立**：风化/重力全部在单个 Chunk 的采样网格内进行（L229–353），**没有跨 Chunk 土壤交换** ⇒ 相邻 Chunk 边界处土壤厚度可能不连续。

### 4.4 坐标转换（`ChunkSpace.cs`）

| 转换 | 规则 | 行号 |
|---|---|---|
| Terrain Local → Voxel | `floor(local / voxelSize)` | L13–22 |
| World → Voxel | `terrainRoot.InverseTransformPoint(world)` → `LocalToVoxel` | L25–35 |
| Voxel → Chunk | `floorDivide(voxel, chunkVoxelSize)` | L38–51 |
| Voxel → Chunk-Local | `positiveMod(voxel, chunkVoxelSize)` | L54–67 |
| Chunk → Local 原点 | `chunkCoord × (voxelSize × chunkVoxelSize)` | L95–110 |

- 负坐标：`FloorDivide` / `PositiveMod`（L142/L155）——**规格自注「负坐标规则尚未最终确定，当前为临时语义」**（L7–8）。
- 本场景根 Transform 为单位变换 ⇒ `World → Local` 为恒等映射（**场景特定**）。

### 4.5 是否存在 Region / Biome / Ocean / 世界尺度接口

在 `Assets/Scripts/Terrain/**` 全目录检索 `seed|Seed|WorldGenerationSettings|WorldHeight|SeaLevel|WorldCoordinate`：**命中 0**。
⇒ Legacy 系统内**不存在**区域、生物群系、海洋、海平面、世界高度、世界尺度等任何 World V2 概念或接口。

### 4.6 挖掘/重建时会重跑的规则

| 规则 | 挖掘后重建 | `RebuildDirtyChunks` | `Regenerate` |
|---|---|---|---|
| `ComputeSurfaceHeight`（Perlin 地表） | **不跑** | **不跑** | **跑** |
| 材料 V2 流水线（坡度/风化/沉积/Grass） | **不跑**（`Material` 复用） | **不跑** | **跑** |
| Density 派生（Base + VoxelField） | precut 关时跑；开启时走 Pre-cut Geometry 变异 | 跑 | **跑（全新 Base）** |
| Marching Cubes 网格 | 跑 | 跑 | 跑 |

⇒ **挖掘与增量重建不重新执行任何"世界生成规则"**，只做几何派生；`TerrainChunk.cs:131` 原文：「本方法不修改 Base Density / Material」。

---

## 5. R42 `WorldSkeletonProvider` 能否直接使用？缺失哪些前置条件

### 5.1 可以在 `DensityTerrain` 上下文中被调用（技术上无障碍）

- `WorldSkeletonProvider` 与 `WorldSkeletonFacts`、`HeightFunction`、`WorldGenerationSettings`、`WorldCoordinate` 均为**纯 C#**（无 `UnityEngine` 依赖）：R42 已用「仅 `netstandard 2.1` + `Assembly-CSharp`、**不带 UnityEditor**」的引用集离线编译通过。
- 因此 `DensityTerrain`（MonoBehaviour）内调用 `WorldSkeletonProvider.TryGetFacts(...)` 无编译/运行时障碍。

### 5.2 阻断项 1（**决定性**）：无法构造合法的 `WorldGenerationSettings`

逐字段核对「Legacy 现有可靠来源 → V2 必需字段」：

| V2 字段 | Legacy 是否有可靠来源 | 说明 |
|---|---|---|
| `Seed` | **完全没有** | Legacy 用 `Mathf.PerlinNoise`（**无种子**）；Terrain 目录 `seed` 命中 0 |
| `TerrainHeight`(int) | 仅 `height = 15.7`（float） | 映射到 int 会丢精度且改变语义；且 V2 该值用于 `round()` 后的整数高度 |
| `TerrainAmplitude`(float) | 仅 `noiseStrength = 2.37` | 名称相近但**噪声函数不同**（Perlin vs 整数 hash），不能当作等价参数 |
| `TerrainFrequency`(float) | 仅 `noiseScale = 0.15` | 同样不可等价 |
| `WorldHeight`(int) | **没有**（无世界高度概念；只有 `chunkVoxelSize.y=37`×`chunkCount.y=1`） | 若强行取 37 属"临时拼装" |
| `SeaLevel`(int) | **没有**（Legacy 无海平面） | — |
| `SoilDepth`(int) | 仅 `dirtDepth = 0.65`（且新流水线不用它） | — |

⇒ **只能用臆造值拼装**，而 R43 §3 明确禁止（"不得为了使用 Provider 而临时拼装配置、硬编码坐标"）。**这是当前接入的第一号缺失前置条件：Legacy 侧不存在可追溯到配置的 V2 参数来源，尤其缺少 Seed。**

### 5.3 阻断项 2：`WorldSkeletonFacts.Elevation` 与 Legacy 地表高度**不可比**

| 维度 | Legacy 地表高度 | `WorldSkeletonFacts.Elevation`（V2） |
|---|---|---|
| 噪声函数 | `Mathf.PerlinNoise`（Unity 全局、**无种子**） | 自实现整数 hash + smoothstep 双线性（**带 `Seed`**，`HeightFunction.cs:114–136`） |
| 输入坐标 | Terrain Local **float**（`x·0.15`） | 世界**整数** `(worldX, worldZ)`，内部 `worldX·frequency` |
| 数值形态 | **float，未取整、未钳制** | `clamp(round(...), 0, WorldHeight−1)` 的 **int**（`HeightFunction.cs:42–47`） |
| 本场景/默认值范围 | **[13.33, 18.07]** | 默认配置 `TerrainHeight=24, Amplitude=6` ⇒ **[18, 30]** |
| 坐标空间 | Terrain Local（本场景恰等于 World） | 世界整数体素坐标 |

⇒ 两者**只在"列级地表高度"这一概念上同类，数值上完全不可比**。任何"假设两者天然一致"的接线都会立即产生错误结论（R43 §3 明令禁止该假设）。

### 5.4 阻断项 3：`SeaLevel` / `Ocean` / `Continent` 在 Legacy 中**没有语义承载**

- Legacy 无海平面、无海洋/陆地判定、无大陆概念（§4.5）。
- 更严重：当前配置 `WorldHeight=64`、`SeaLevel=64` ⇒ `Ocean = Elevation < 64` 对 `Elevation ∈ [18,30]` **恒为 true**（R40 实跑断言 B3 已证）⇒ 若把 `Ocean` 直接接入材料/地形，整片世界会变成"海洋"。R43 §3 已明令禁止该用法。
- `Continent` 恒为 `0`（海洋）或 `1`（陆地）二值，Legacy 侧无对应消费者。

### 5.5 坐标一致性

- 本场景根 Transform 为单位变换 ⇒ `WorldCoordinate(x,y,z)` 与 `VoxelField.PositionToVoxel(local)` 在**数值上可对齐**（都是 `floor(pos)`，`VoxelSize = 1`）。
- 但该等价**依赖场景巧合**（根节点位置为整数 0、无旋转、缩放 1）。若根节点平移/旋转/缩放，`ChunkSpace` 的 `InverseTransformPoint` 结果不再等于整数世界坐标 ⇒ 任何接线必须先显式校验该前提，不能写死"local == world"。

### 5.6 结论（§5）

| 前置条件 | 状态 |
|---|---|
| Provider 可在 Legacy 上下文调用 | ✅ 满足（纯 C#） |
| 可构造**合法且有来源**的 `WorldGenerationSettings` | ❌ **不满足**（无 Seed、无 WorldHeight、无 SeaLevel） |
| `Elevation` 与 Legacy 地表高可比 | ❌ **不满足**（噪声函数/坐标/量化三项全异） |
| `Ocean`/`Continent` 在 Legacy 有可用语义 | ❌ **不满足**（且当前默认配置下 Ocean 恒 true） |
| 坐标可映射（带前提校验） | ⚠️ 本场景可，但需显式守卫 |

---

## 6. 三个接入方案与影响比较

> 共同红线：不得改变现有地形外观；不得修改 `SeaLevel`/`WorldHeight`；不得把 `Ocean` 直接用于现有材料/地形；不得拼装配置；不得复制地形规则。

### 方案 1（**推荐**）— 只读影子接入 + 差异量化验证器（外观零变化）

| 维度 | 内容 |
|---|---|
| 修改的现有文件 | **0 个**（不碰 `DensityTerrain`/`DensityFieldGenerator`/场景） |
| 新增 | `Assets/Editor/V2SkeletonShadowComparisonValidation.cs`（+`.meta`，`RunBatch` 入口）：在编辑器内**临时**创建 `DensityTerrain` + 临时 GameObject（既有验证器惯例，如 `MiningSurfaceDiagnosticValidation`），跑 `Regenerate()` 取得 Legacy 逐列地表高度；同时用**显式构造**的 `WorldGenerationSettings`（来源标注为"实验对照"）调用 `WorldSkeletonProvider`，逐列输出：`Elevation` 差值分布、`Ocean` 判定分布、坐标映射一致性检查；**不写盘、不改场景** |
| 新增配置/接口 | 无 |
| 如何避免重复计算高度 | 不重复计算：Legacy 一侧直接读 `TerrainChunk.Density`（已有缓存）+ `FindSurfaceHeight`；V2 一侧只调 Provider 一次 |
| 挖掘/重建一致性 | 不涉及（不改变任何生成或重建路径） |
| 性能影响 | 仅 Editor 验证期；运行时 0 |
| 存档兼容性 | 无关（Legacy 无存档） |
| 现有地形影响 | **结构上为零**（不进入 `BuildChunk`、不改任何参数） |
| 如何验证前后一致 | 验证器内对同一临时的 Legacy 世界做两次 `Regenerate`，逐 Chunk 逐采样点比较 `DensityField` 与 Mesh 顶点/索引哈希；要求完全一致 |
| 未来可叠加能力 | 为方案 2/3 提供**量化基线**（差多少、在哪些列差、Ocean 分布如何），并可用于长期"影子回归"守卫 |

### 方案 2 — 参数来源单一化（数值参数搬到单一配置载体，公式不变）

| 维度 | 内容 |
|---|---|
| 修改的现有文件 | `DensityTerrain.cs`（新增可选字段，如 `WorldGenerationSettings` 引用或 4 个显式引用字段）、场景（绑定该引用） |
| 新增配置/接口 | 需新增"世界级配置载体"（ScriptableObject 或显式字段） |
| 避免重复计算 | 仅搬运数值，不新增计算 |
| 挖掘/重建一致性 | 不受影响（生成规则不变） |
| 性能 | 可忽略 |
| 存档兼容性 | Legacy 无存档 ⇒ 无影响；但引入了新的资产依赖 |
| 现有地形影响 | **可保持为零，但需要证明**：`height 15.7 / noiseScale 0.15 / noiseStrength 2.37` 必须能无损表达；而 `WorldGenerationSettings` 的 `TerrainHeight/TerrainAmplitude` 是 **int/float 混用且语义由 `HeightFunction` 决定**，与 Perlin 参数语义不同 ⇒ 需要一个**新的中立配置结构**，而不是复用 V2 的 `WorldGenerationSettings`（否则会暗示两者公式等价） |
| 关键风险 | 若复用 `WorldGenerationSettings`，会再次制造"双载体/假等价"（正是 R40 修掉的那类问题）；且 `Seed` 在 Perlin 侧**无处可用** |
| 验证 | 搬迁前后 `Regenerate` 的 `DensityField` 逐样本 bitwise 相同 + Mesh 顶点/索引哈希相同 |
| 可叠加能力 | 为"世界级配置"提供唯一来源，是方案 3 的前置 |

### 方案 3 — V2 地表函数接管（`HeightFunction` 取代 Perlin）

| 维度 | 内容 |
|---|---|
| 修改的现有文件 | `DensityFieldGenerator.cs`（`ComputeSurfaceHeight`）、`DensityTerrain.cs`（参数注入）、场景 |
| 是否改变外观 | **必然彻底改变**（噪声函数、坐标空间、量化方式全变；地表会从 [13.33,18.07] 浮点曲面变成 [18,30] 整数台阶） |
| 前置 | 需 owner 明确批准"世界外观变更"，并确定 `WorldHeight`/`SeaLevel` 语义（含 64/64 全海洋问题）、负坐标规则、Chunk 边界土壤连续性策略 |
| 风险 | 高：影响所有依赖地形的系统（挖掘、Mesh、碰撞、玩家出生点、`TerrainVoxelSelector`）；无存档可回退，但有 `regenerateInEditor` 可手动回滚 |
| 验证 | 需新建世界基线 + 视觉验收 + 全套 Legacy 验证器回归 |
| 结论 | **本轮不推荐、近期不实施** |

### 6.x 影响对比

| 维度 | 方案 1 | 方案 2 | 方案 3 |
|---|---|---|---|
| 改动的现有文件 | 0 | 1–2 + 场景 | 2–3 + 场景 |
| 是否改变外观 | **否** | 否（需证明） | **是（彻底）** |
| 需 owner 新裁决 | 否 | 是（配置载体归属） | **是**（外观变更 + SeaLevel 语义） |
| 触碰 Legacy 生产代码 | 否 | 是（轻） | 是（重） |
| 可独立验证 | 是 | 是（bitwise 等价） | 否（需人工视觉验收） |
| 破坏现有世界风险 | **极低** | 低-中 | 高 |

---

## 7. 推荐方案、理由与预期修改文件清单

### 推荐：**方案 1（只读影子接入 + 差异量化验证器）**

理由：
1. **唯一不需要任何未裁决决策就能立即实施的方案**（无 owner 裁决、无场景改动、无参数变更）。
2. **外观零风险**：不进入 `BuildChunk`、不触碰 `DensityFieldGenerator`、不改变任何场景字段。
3. **解决"不可比"这个真问题**：它把 §5.3 的三项差异（噪声函数/坐标/量化）量化成数据，而不是让后续轮次在"以为两者等价"的假设上直接接线——这正是 R43 §3 反复强调的红线。
4. **建立长期护栏**：未来任何让 V2 参与地形的改动，都能被这条影子对比覆盖（差异突变即报警）。
5. **成本最低**：只新增 1 个 Editor 验证器，`DensityTerrain` 零修改，冻结表 0 重钉。

预期修改文件清单（方案 1）：

| 动作 | 文件 | 是否在冻结表 | 需重钉 |
|---|---|---|---|
| 新增 | `Assets/Editor/V2SkeletonShadowComparisonValidation.cs` | 未登记 | 否 |
| 新增 | 上述文件的 `.meta`（Unity 自动生成） | 未登记 | 否 |
| 修改 | **无** | — | — |

### 明确不做（本轮与推荐方案均不含）
- 不新增 `WorldBiomeProvider`、不创建 Region 系统、不让 `Ocean` 影响材料。
- 不修改 `SeaLevel`/`WorldHeight`/地表公式/材料规则/坐标语义。
- 不把 Provider 注册进任何生成路径。
- 不声称"玩家当前世界已使用 World V2"。

---

## 8. 建议的分阶段实施顺序与回归验证标准

| 阶段 | 内容 | 出口标准（全部满足才进入下一阶段） |
|---|---|---|
| **A（下一步，推荐）** | 方案 1：影子对比验证器 | ① 验证器 `RunBatch` exit 0、断言全 PASS；② 同一临时 Legacy 世界两次 `Regenerate` 的 `DensityField` 与 Mesh 哈希**完全一致**（证明验证器自身非侵入）；③ 产出逐列差异统计（Elevation 差值分布 / Ocean 分布 / 坐标映射一致性）；④ 冻结表 349/0/0；⑤ 旧验证器全绿 |
| B（需批准） | 方案 2：世界级配置载体（中立结构，不复用 V2 `WorldGenerationSettings`） | ① 搬迁前后 `DensityField` 逐样本 bitwise 相同；② Mesh 顶点/索引哈希相同；③ 场景 diff 仅限新增字段；④ 全套 Legacy 验证器回归 |
| C（需 owner 批准） | 方案 3：V2 地表函数接管（外观变更） | ① 明确的新世界基线；② `WorldHeight`/`SeaLevel` 语义裁决落地；③ 负坐标规则定稿；④ 视觉验收 + 挖掘/碰撞回归；⑤ 明确回退路径 |
| D（远期） | Region / Biome / Ocean / Magic / Resource / Ecology 接入 | 每项单独 owner 裁决 + 独立接线轮 + 独立验证 |

**每阶段通用门禁**：HEAD 不变或经批准的提交；`sealevel_round7_progress.md` 1032 行未改；冻结表 349/0/0；不得修改 `SeaLevel`/`WorldHeight` 默认值；不得让 V2 依赖 Legacy 类型（`LS65`/`RAB-L2`）；不得让 Legacy 依赖 V2 类型（本轮红线）。

---

## 9. 本轮未修改任何既有文件的证据

| 判据 | 实测 |
|---|---|
| `git status --porcelain` | 轮前 **31 条**（20 `M` + 11 `??`）→ 轮后相同（仅 `_phase2b_validation/` 内新增本报告，该目录整体为 `??`） |
| `git diff --stat` | 轮前轮后均为 `20 files changed, 390 insertions(+), 142 deletions(-)` |
| HEAD / 暂存区 | 未变 / 空 |
| 关键文件内容哈希 | `progress.md D638D271767BFFF9`、R41 报告 `52FDF4479271BBBA`、R42 报告 `A645487A4ADD4E88`、`WorldSkeletonProvider.cs E28316E8C5749D7F`、`WorldSkeletonProviderValidation.cs 5084FCD39726586C`、`WorldSkeletonFacts.cs 98B59916CA9CDBCC` —— **全部未变** |
| Legacy 文件 | `DensityTerrain.cs 0EB935DBA79CBD40`、`DensityFieldGenerator.cs 702BEFA889002006`、`ChunkSpace.cs 219FCBB569A413FB` 等 —— **未触碰** |
| 场景 / Prefab / Asset | `SampleScene.unity` 未修改（哈希见收尾核验） |
| 冻结表 | 349/349/0/0，**未重钉** |
| git 写操作 | 未执行 `commit` / `push` / `reset` / `clean` / `stash` / `checkout` / `add` |
| 临时分析脚本 | 本轮**未编写任何**外部脚本文件；所有分析通过只读 PowerShell 管道（`ReadAllLines`/正则/`Get-FileHash`）完成；中间输出文件均以 `_tmp_r43_*.txt` 命名并已在收尾清理（清理证据见 §11） |

---

## 10. 当前证据的局限与尚未确定的问题

### 10.1 本轮证据的局限
1. **未启动 Unity**：本轮无新增运行时证据；所有结论来自源码与场景序列化文件（R42 的运行证据只覆盖 V2 侧，不覆盖 Legacy 地形）。
2. **未实测挖掘后的材质表现**：`RebuildMesh(effectiveDensity)`（`TerrainChunk.cs:131`）复用旧 `MaterialField` 而不重算；据推断被挖空处 `density ≤ isoLevel` 不会生成三角形（`TerrainMeshGenerator.cs:109/114`），故不可见，但**未实测验证**。
3. **未深入 `VoxelAwareMeshRuntimeMutation`（1890 行）**：仅确认它是 precut 路径的生产入口（`DensityTerrain.cs:697`）与职责；其内部几何算法未逐行审计。
4. **`debugCutaway = 1` 的影响范围未实测**：仅从代码（L1343–1376）确认它对首个 Chunk 的 `x∈[−2,2]` 样本置 `density = −1`；实际可见效果未渲染确认。
5. **`terrainSize (83, 20.3, 55.2)` 的实际作用未验证**：因 `chunkVoxelSize` 显式配置，`GetEffectiveChunkVoxelSize`（L1005）直接返回配置值，`terrainSize` 不参与。若未来清空 `chunkVoxelSize`，会走推导路径（L1010–1031）。

### 10.2 尚未确定的问题（需 owner 或后续轮次裁决）
| # | 问题 | 影响 |
|---|---|---|
| Q1 | **Legacy 侧的随机种子从何而来**（当前 Perlin 无种子；引入 Seed 需定义"同 Seed 同世界"语义） | 决定能否构造合法 `WorldGenerationSettings`（§5.2） |
| Q2 | `WorldHeight`/`SeaLevel` 在 Legacy 世界的等价语义（尤其 64/64 全海洋） | 决定 Ocean 何时可用（R40 UNKNOWN 仍未决） |
| Q3 | 是否允许"世界外观变更"（方案 3）以及迁移窗口/回退策略 | 决定 V2 能否真正影响可见世界 |
| Q4 | 负坐标规则（`ChunkSpace.cs:7-8` 自注"未最终确定"） | 决定 V2 世界整数坐标与 Legacy 体素的长期映射 |
| Q5 | Chunk 边界土壤连续性（当前逐 Chunk 独立，无跨边界交换） | 若引入 V2 驱动材料，需先决定边界策略 |
| Q6 | `A-2`/`AD-1`（Legacy 与新栈关系）仍未裁决（R41 §9.4） | 决定长期是"改造 Legacy"还是"迁移到 VoxelWorld" |

---

## 11. 结论（严格区分三种状态）

### 11.1 当前**真正运行**的 Legacy 地形能力
1. **单一生成入口**：`DensityTerrain.BuildChunk`（L752）→ `DensityFieldGenerator.Generate`（L783，**全库唯一调用点**）。
2. **单一地表高度来源**：`ComputeSurfaceHeight`（L483）= `height(15.7) + Perlin(无种子)·noiseStrength(2.37)`，Terrain Local 浮点坐标，范围 [13.33, 18.07]。
3. **密度/等值面**：`density = surface − localY`，`IsoLevel = 0`，`density > 0` 为实体；采样点 ≠ 逻辑体素（CellSize 各轴不同）。
4. **材料**：材料 V2 流水线（地表 → 坡度 → 风化 → 重力沉积 → Grass/Dirt/Stone），**逐 Chunk 独立**；Mesh 分 3 子网格。
5. **挖掘**：F 键 → `VoxelField.Set(Air)` → `RebuildChunksAffectedByVoxel` →（本场景 precut 开）Pre-cut Geometry 变异 / （precut 关）Effective Density 重建；**不重跑任何生成规则**。
6. **无持久化**：VoxelField 仅内存；无存档；重进场景即丢失挖掘。
7. **当前场景另有调试行为**：`debugCutaway = 1`（首 Chunk 剖切 4 m）。

### 11.2 World V2 **已具备但尚未进入**当前生成流程的能力
1. `WorldSkeletonProvider` / `WorldSkeletonFacts`（`Elevation` / `Ocean` / `Continent`）—— R42 已完成并真跑验证（44/44 PASS），**生产调用者 0**。
2. `WorldParameterQuery` / `WorldStatePipeline` / `WorldParameterEvaluator`（8 参数 + 两段式发布）—— 生产调用者 0。
3. Region 全族（身份/修正量）—— 仅 V2 内部使用，修正量为占位 `[0, 0.25)`。
4. `SeaLevel` 配置与严格小于的 Ocean 规则 —— 与 Legacy 世界**零接触**。
5. **关键断点**：Legacy 无 Seed / 无 WorldHeight / 无 SeaLevel / 无海陆语义；且 `Elevation` 与 Legacy 地表高度在**噪声函数、坐标空间、量化方式**三方面全异 ⇒ 两者不可直接互换、不可假设一致。

### 11.3 下一阶段建议的**最小改动**
- **只做方案 1**：新增 `Assets/Editor/V2SkeletonShadowComparisonValidation.cs`（+meta），以**只读影子方式**在编辑器内量化 Legacy 地表高度与 V2 `Elevation` 的差异、`Ocean` 分布、坐标映射一致性；**零生产改动、零场景改动、零外观变化、零哈希重钉**。
- 该验证器同时作为长期护栏：任何未来让 V2 参与地形的改动都必须让它保持"差异受控"。
- **不**在本阶段碰配置载体（方案 2 需批准）、**不**碰地表公式（方案 3 需 owner 批准"外观变更"）、**不**用 `Ocean` 影响材料。

**本轮不声称**：玩家当前世界使用了 World V2；V2 与 Legacy 高度等价；Ocean/Continent 可安全用于现有地形。