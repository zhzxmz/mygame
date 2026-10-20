# SeaLevel 迁移 — 第七轮进度记录

## 当前阶段
阶段 1 已完成（配置结构）。阶段 2-7 未开始。

## 已完成的修改
- Assets/Scripts/VoxelWorld/Generation/WorldGenerationSettings.cs（全文重写）
  - 新增 public int SeaLevel（默认 64；单位世界体素 y）
  - 新增 public const int DefaultSeaLevel = 64
  - 旧六参数构造改为委托七参数构造（SeaLevel = DefaultSeaLevel）
  - 新增七参数构造（显式 seaLevel）
  - IsValid 新增 SeaLevel 范围校验 0 <= SeaLevel <= WorldHeight
  - ToString 输出 SeaLevel

## 已执行命令与真实结果
- csc 独立编译（V2 17 文件 + HeightFunction + WorldGenerationSettings）：exit 0，0 error / 0 warning

## 测试状态
- 独立编译：通过
- Unity 编译：未执行
- WorldV2ParameterPipelineValidation：未执行
- GeologyStageDValidation / VoxelWorldRuntimeCoreValidation / ChunkStreamingValidation / GeologyRuntimeIntegrationValidation：未执行
- Ocean 边界测试（SeaLevel=0 / =WorldHeight / Elevation=SeaLevel-1 / =SeaLevel / =SeaLevel+1）：未实施（阶段 3）

## 基线证据位置
- 尚未触发哈希基线变更（本轮只改 WorldGenerationSettings.cs；OceanRule 未改，故 WorldParameterRules.cs 未再变化）
- 预期受影响基线：GeologyStageDValidation GE7、ChunkStreamingValidation CS12、GeologyRuntimeIntegrationValidation GE7（均钉住 Generation 源文件哈希）

## 尚未完成的步骤与下一条具体操作
1. 阶段 2：WorldParameterRules.cs 的 OceanRule 改为 settings.SeaLevel，删除 ProvisionalSeaLevel 常量（L121）与过时注释（L113-114），比较符号保持严格小于。
2. 阶段 2：VoxelWorldStreamingBootstrap.cs 增加 public int SeaLevel = 64 字段，并在构造处传第 7 实参。
3. 阶段 3：WorldV2ParameterPipelineValidation.cs 追加配置范围、Ocean 严格边界、兼容性（旧构造=64、逐点一致、HeightFunction 不受影响、确定性）四组断言。
4. 阶段 4：跑 WorldV2ParameterPipelineValidation（Unity -executeMethod）。
5. 阶段 5：跑三个哈希验证器，区分本轮预期失败与历史基线，仅同步受信登记并保留失败日志。
6. 阶段 6：全量专项回归 + 同步 WPS56 与 A1 Ocean 配置来源。
7. 阶段 7：最终 git diff 审计（确认无无关改动、三个保留目录仍在）。

## 注意
- 默认 WorldHeight=64 且 SeaLevel=64 时 Ocean 在全部合法高度为 true（迁移前既有行为，本轮保持）。
- 未提交、未推送。
## 第九轮续跑记录（局部验证 + 序列化调查）

### 本阶段实际修改
- Assets/Editor/WorldV2ParameterPipelineValidation.cs：新增 SeaLevel 验证组 A/B/C/D 共 16 项断言（A 4 / B1 4 / B2-B3 2 / C 5 / D 静态 1），并移除编辑残留的未使用标签。

### Unity 编译与验证器实际结果
- 命令：Unity.exe -batchmode -nographics -quit -projectPath <proj> -logFile <TEMP>\unity_r9_v2.log -executeMethod WorldV2ParameterPipelineValidation.ValidateOrThrow
- 退出码：0
- Unity 编译错误：0（编译警告 4，未逐条分类，待后续核查）
- 验证器输出：30 行，PASS 29 / FAIL 0，汇总 failures=0（全部通过）
- 关键断言实测值：内部 Elevation 样本 = 23；B2/B3 各覆盖 35 列；旧配置 Ocean 与 Elevation<64 逐点 mismatch=0；改变 SeaLevel 的 HeightFunction 高度 mismatch=0
- 日志路径：C:\Users\xmz\AppData\Local\Temp\unity_r9_v2.log

### 序列化兼容调查证据
- 组件脚本 GUID：2c531141a81459d499f3f2959fe28988
- 扫描 Assets 下全部 *.unity / *.prefab（数量见上一条命令输出）后按 GUID 匹配
- 结论：见本轮命令输出（若无匹配则说明项目中没有既有 VoxelWorldStreamingBootstrap 序列化实例，新增字段的旧数据反序列化风险不适用于当前仓库；若将来创建场景实例，需确认新字段默认值写入）

### 仍未执行的回归与同步（交接给独立回归阶段）
1. GeologyStageDValidation.RunBatch（预期 GE7 完整性检查因 Generation/WorldParameterRules 变更而失败，需保留原始日志后按受信集合同步）
2. VoxelWorldRuntimeCoreValidation.RunBatch
3. ChunkStreamingValidation.RunBatch（预期 CS12）
4. GeologyRuntimeIntegrationValidation.RunBatch（预期 GE7）
5. Elevation 等价验证复跑
6. WorldParameter_Source_Contract.md 的 WPS56 与 A1 Ocean 配置来源同步

### 下一条具体操作
运行 GeologyStageDValidation.RunBatch 并保存原始失败日志，逐条区分本轮 SeaLevel 变更导致的预期哈希失败与既有历史基线差异，仅同步受信登记。

### 当前 Git 状态
- 未 commit / 未 push / 未 reset；三个保留目录仍在。
---

## 第十轮记录：GE7/GW10 哈希回归与受信基线审查（仅 GeologyStageDValidation）

### 原始运行 1（同步前）
- 命令：D:\gamemaking\2022.3.62f1c1\Editor\Unity.exe -batchmode -nographics -quit -projectPath "D:\gamemaking\My project" -logFile <TEMP>\unity_r10_stageD_raw.log -executeMethod GeologyStageDValidation.RunBatch
- 退出码：1（编辑器 2022.3.62f1c1，Unity.exe 版本 2022.3.62.11538587）
- 日志：_phase2b_validation/round10_GeologyStageDValidation_raw.log（114083 bytes，原始证据，未覆盖）
- 旁证：round10_GeologyStageDValidation_stdout.txt / _stderr.txt（均为 0 字节）
- 验证器确实真实执行：GW1–GW10 全部分节运行，标记行 5 条，编译错误 error CS = 0
- 结果：Checks = 46，Failures = 3，Result = FAIL（PASS 行 43 / FAIL 行 3）
- 说明：RunBatch 统计口径为「每个 Check 计 1 次」，失败的文件级 Check 仅在哈希不符时才会被发出，故失败态下 Checks 比通过态多 2（两条文件级失败）。

### 差异审查（逐项）
- 检查位置：GeologyStageDValidation.cs 的 FrozenFiles 字典（同步前 L890–L962，共 63 条）+ ValidateFrozenFiles()（L964–L1034）+ ComputeFileHash16()（L1050，SHA256 前 8 字节 = 16 位大写十六进制）
- 失败 1：Scripts/VoxelWorld/Generation/WorldGenerationSettings.cs
  - 实际 7F11DEE8061E4FA1 / 预期 140B7E289F05C6FA
  - 分类 = A（已批准的 SeaLevel 修改）。逐行审查 diff：+46/-0，含 SeaLevel 文档头、DefaultSeaLevel=64 常量、public int SeaLevel 字段、六参数构造委托（: this(..., DefaultSeaLevel)）、新增七参数构造、SeaLevel = seaLevel 赋值、IsValid 的 [0, WorldHeight] 范围校验、ToString 输出；原有构造体字段赋值序列完整保留一份，无重复赋值、无无关改动。
- 失败 2：Scripts/Game/VoxelWorldStreamingBootstrap.cs
  - 实际 8A59289F0194D20F / 预期 4738634952568520
  - 分类 = A（已批准的 SeaLevel 修改）。逐行审查 diff：+5/-1，含 public int SeaLevel = 64 配置字段与其文档、WorldGenerationSettings 构造改为七参数并显式传入 SeaLevel；无无关改动。
- 分类 B / C / D：本轮 0 项。63 条登记中其余 61 条在同步前后均逐条复算匹配。
- 独立复核方式：不依赖 Unity，用 PowerShell 按同一路径解析规则与同一 SHA256 前 16 位算法对 63 条登记全部复算，结果与 Unity 实跑完全一致（同步前 61 匹配 / 2 不匹配 / 0 缺失），排除「日志解析误差」导致误判。

### 受控基线同步（仅 2 条）
- 更新 1：Scripts/VoxelWorld/Generation/WorldGenerationSettings.cs 140B7E289F05C6FA -> 7F11DEE8061E4FA1，理由：diff 已逐行确认为 SeaLevel 迁移且无无关改动
- 更新 2：Scripts/Game/VoxelWorldStreamingBootstrap.cs 4738634952568520 -> 8A59289F0194D20F，理由：同上
- 同步方式：仅替换上述 2 条哈希值并各加 2 行说明注释；文件行数 1248 -> 1252（+4 = 仅注释），无删除行，无条目增删（仍 63 条），未扩展/缩小文件集合，未跳过任何失败文件，未无条件重登记
- 注意：哈希同步本身不是正确性证明，只是把「已审查过的当前内容」登记为新的比对基准；语义正确性仍由各专项断言与端到端验证负责。

### 复跑（同步后）
- 命令同原始运行 1，日志重定向为 <TEMP>\unity_r10_stageD_rerun.log
- 退出码：0
- 日志：_phase2b_validation/round10_GeologyStageDValidation_rerun.log（117333 bytes，独立文件）
- 结果：Checks = 44，Failures = 0，Result = PASS（PASS 行 44 / FAIL 行 0），编译错误 error CS = 0
- Checks 由 46 降为 44 的原因：两条文件级失败 Check 在哈希匹配时不再发出（46 - 2 = 44），并非删除断言

### 尚未解释 / 尚未处理
- 本轮只处理 GeologyStageDValidation；以下仍未执行：VoxelWorldRuntimeCoreValidation、ChunkStreamingValidation、GeologyRuntimeIntegrationValidation、Elevation 等价验证复跑
- WorldParameter_Source_Contract.md 的 WPS56 与 A1 Ocean 配置来源仍未同步
- 编译警告未逐条分类（本轮 error CS = 0，警告数见上）
- 观察（既有状态，非本轮引入）：Editor/ChunkStreamingValidation.cs 在 GW10 中登记的哈希 F2C2C9BFB4509DC4 已等于其当前（已被早前轮次修改过的）内容哈希，属早前轮次的登记状态，本轮未改动
### 补记（编译警告取证）
- 首次运行 warning CS = 0（命中上次编译缓存，未重新编译）；复跑因本轮修改了 GeologyStageDValidation.cs 触发重新编译，warning CS = 4。
- 4 条警告原文如下（均位于既有文件、与 SeaLevel 迁移无关；无 error CS）：  - Assets\Editor\VoxelCoordinateMigrationStageB3CValidation.cs(2130,50): warning CS0162: Unreachable code detected
  - Assets\Editor\MiningSurfaceIntegrityValidation.cs(432,24): warning CS0414: The field 'MiningSurfaceIntegrityValidation.LastScannedFaceCount' is assigned but its value is never used
  - Assets\Editor\VoxelCoordinateMigrationStageB3CValidation.cs(2130,50): warning CS0162: Unreachable code detected
  - Assets\Editor\MiningSurfaceIntegrityValidation.cs(432,24): warning CS0414: The field 'MiningSurfaceIntegrityValidation.LastScannedFaceCount' is assigned but its value is never used
- 去重说明：上列 4 行实为 **2 条唯一警告各出现 2 次**（程序集/编译轮次重复输出），且均位于既有文件 VoxelCoordinateMigrationStageB3CValidation.cs 与 MiningSurfaceIntegrityValidation.cs，与 SeaLevel 迁移无关。此项同时关闭第九轮遗留的「4 条编译警告未逐条分类」。
---

## 第十一轮记录：CS12 哈希回归与受信基线审查（仅 ChunkStreamingValidation）

### 原始运行（同步前）
- 命令：D:\gamemaking\2022.3.62f1c1\Editor\Unity.exe -batchmode -nographics -quit -projectPath "D:\gamemaking\My project" -logFile <TEMP>\unity_r11_cs12_raw.log -executeMethod ChunkStreamingValidation.RunBatch
- 退出码：1
- 日志：_phase2b_validation/round11_ChunkStreamingValidation_raw.log（166367 bytes）；stdout/stderr 各 0 字节
- 验证器确认真实执行：CS1–CS12 全部分节运行，编译错误 error CS = 0；CS1–CS11 全部 PASS（流式行为无回归）
- 结果：Checks = 108，Failures = 2，Result = FAIL（PASS 行 106 / FAIL 行 2）
- 受信基线规模：FrozenFiles 共 40 条 = 31 个 Assets 工程文件 + 9 份 Specifications 规范（规范 9 条本轮全部实际比对，跳过 0）

### CS12 失败项与差异审查
- 失败 1（文件级）：Scripts/VoxelWorld/Generation/WorldGenerationSettings.cs 实际 7F11DEE8061E4FA1 / 预期 140B7E289F05C6FA
- 失败 2（聚合）：CS12：既有文件 SHA256(前16) 全部不变（比对 40 个；跳过规范 0 个）
- 独立复算（不依赖 Unity，按同一路径规则与 SHA256 前 16 位算法复算全部 40 条）：39 匹配 / 1 不匹配 / 0 缺失，与 Unity 实跑逐条一致
- 分类 = A（已批准的 SeaLevel 修改）。本轮独立审查手段（不引用第十轮 GE7 结论）：
  - git diff 统计：新增 46 行 / 删除 0 行
  - 新增行自动分类器逐条核对：SeaLevel 语义行 + 文档行 + 结构性行；仅 7 行未命中分类器，逐一核对后确认为「新七参数构造函数签名」（git 将旧六参签名对齐为上下文、把新签名记为新增），属结构性必需
  - 构造体赋值唯一性：Seed/TerrainHeight/SoilDepth/WorldHeight/SeaLevel/TerrainFrequency/TerrainAmplitude 每个字段赋值各出现 1 次（无重复、无丢失）
  - 关键语义行核验：DefaultSeaLevel = 64（L29）、六参委托 : this(..., DefaultSeaLevel)（L69）、SeaLevel = seaLevel（L89）、IsValid 范围 [0, WorldHeight]（L132）、ToString 输出（L161）
  - 「删除 0 行」⇒ 原文件既有校验逻辑一行未减少，不存在断言弱化
- 分类 B / C / D：0 项。CS12 其余 39 条（含 9 份规范）同步前后均逐条复算匹配

### 与 GE7 文件集合的差异（说明为何必须独立审查）
- GE7 登记 63 条，CS12 登记 40 条；CS12 不含 Geology/ 目录、不含 Scripts/Game/VoxelWorldStreamingBootstrap.cs、也不自钉 Editor/ChunkStreamingValidation.cs
- 因此 GE7 已同步的 Bootstrap 条目在 CS12 中并不存在，CS12 只需同步 1 条；GE7 的审查结论未被用作本轮的判断依据

### 受控基线同步（仅 1 条）
- Scripts/VoxelWorld/Generation/WorldGenerationSettings.cs：140B7E289F05C6FA -> 7F11DEE8061E4FA1，理由：diff 已本轮独立逐行审查确认为 SeaLevel 迁移且无无关改动
- 同步方式：一次字面替换（1 行 -> 3 行 = 新增 2 行说明注释），文件行数 1184 -> 1186（净 +2，与替换量一致，无其他行增删）；条目仍 40 条，未扩展/缩小文件集合，未跳过失败文件，未无条件重登记，未覆盖原始日志
- 声明：哈希通过只证明与当前基线一致，不等于功能正确性证明

### 复跑（同步后）
- 命令同上，日志重定向为 <TEMP>\unity_r11_cs12_rerun.log
- 退出码：0
- 日志：_phase2b_validation/round11_ChunkStreamingValidation_rerun.log（170356 bytes，独立文件）
- 结果：Checks = 107，Failures = 0，Result = PASS（PASS 行 107 / FAIL 行 0），error CS = 0
- Checks 108 -> 107 的原因：文件级失败 Check 在哈希匹配时不再发出（108 - 1 = 107），非删除断言
- CS12 复跑断言：既有文件全部存在（缺失 0）PASS；既有文件 SHA256 全部不变（比对 40 个；跳过规范 0）PASS
- 编译警告：复跑 warning CS 行 = 4（因本轮修改验证器触发重新编译；同步前运行 0，命中缓存），原文如下：  - Assets\Editor\VoxelCoordinateMigrationStageB3CValidation.cs(2130,50): warning CS0162: Unreachable code detected
  - Assets\Editor\MiningSurfaceIntegrityValidation.cs(432,24): warning CS0414: The field 'MiningSurfaceIntegrityValidation.LastScannedFaceCount' is assigned but its value is never used
  - Assets\Editor\VoxelCoordinateMigrationStageB3CValidation.cs(2130,50): warning CS0162: Unreachable code detected
  - Assets\Editor\MiningSurfaceIntegrityValidation.cs(432,24): warning CS0414: The field 'MiningSurfaceIntegrityValidation.LastScannedFaceCount' is assigned but its value is never used
- 去重：上列为 2 条唯一警告各出现 2 次，均位于既有文件 VoxelCoordinateMigrationStageB3CValidation.cs 与 MiningSurfaceIntegrityValidation.cs，与本轮无关

### 尚未执行的验证项目
- GeologyRuntimeIntegrationValidation.RunBatch（下一步，独立处理）
- VoxelWorldRuntimeCoreValidation.RunBatch
- Elevation 等价验证复跑
- WorldParameter_Source_Contract.md 的 WPS56 状态与 A1 Ocean 配置来源同步
- 最终全量 git diff 审计
---

## 第十二轮记录：GeologyRuntimeIntegrationValidation（GE7 + GE9）回归与受信基线审查

### 验证机制（实测确认）
- 入口：GeologyRuntimeIntegrationValidation.RunBatch()（L58）→ ValidateAll() → GE1–GE9 → EditorApplication.Exit(failures==0?0:1)
- 三个独立哈希登记表，全部为 SHA256 前 8 字节 = 16 位大写十六进制：
  - CriticalFiles（L545–L576，25 条，供 GE7 校验；路径 = Application.dataPath + key）
  - FrozenFiles（L707–L748，34 条 = 14 规范 + 6 VoxelCore + 9 Geology + 5 既有 Editor 入口；供 GE9）
  - AuthorizedChangedFiles（L763–L772，6 条，早前经授权修改并以新哈希钉住；供 GE9）
- 失败格式：GE7 为「GE7：<key> 已改变（actual != expected）」；GE9 为「GE9：[已授权修改] <key> 哈希不符（actual != expected）」；各自另有存在性聚合断言
- 路径规则：../Specifications/ → 工程根规范；其余 → Application.dataPath

### 原始运行（同步前）
- 命令：D:\gamemaking\2022.3.62f1c1\Editor\Unity.exe -batchmode -nographics -quit -projectPath "D:\gamemaking\My project" -logFile <TEMP>\unity_r12_gri_raw.log -executeMethod GeologyRuntimeIntegrationValidation.RunBatch
- 退出码：1
- 日志：_phase2b_validation/round12_GeologyRuntimeIntegrationValidation_raw.log（123838 bytes）；stdout/stderr 各 0 字节
- 验证器确认真实执行：GE1–GE9 全部分节运行，error CS = 0；GE1–GE6、GE8 全部 PASS（Runtime/Streaming/Mesh/View 行为无回归）
- 结果：Checks = 49，Failures = 6，Result = FAIL（PASS 43 / FAIL 6）
- 独立复算（不依赖 Unity，65 条全算）：CriticalFiles 24/25 匹配（1 不匹配）、FrozenFiles 34/34 匹配（0 不匹配）、AuthorizedChangedFiles 3/6 匹配（3 不匹配）；与 Unity 实跑逐条一致

### 失败项与逐文件审查
1. GE7：Scripts/VoxelWorld/Generation/WorldGenerationSettings.cs（actual 7F11DEE8061E4FA1 / pin 140B7E289F05C6FA）
   - 分类 A（SeaLevel）。本轮独立复核：新增 46 行 / 删除 0 行；仅 SeaLevel 语义行（DefaultSeaLevel=64、public int SeaLevel、七参数构造、SeaLevel=seaLevel、IsValid [0,WorldHeight]、ToString）+ 文档 + 结构行；删除 0 行 ⇒ 既有逻辑一行未少
2. GE9：[已授权修改] Scripts/Game/VoxelWorldStreamingBootstrap.cs（actual 8A59289F0194D20F / pin 4738634952568520）
   - 分类 A（SeaLevel）。新增 4 行 / 删除 1 行；唯一删除行是旧六参数构造调用，被七参数（含 SeaLevel）替换
3. GE9：[已授权修改] Editor/ChunkStreamingValidation.cs（actual 55AC4FFF4BFC13C8 / pin 13E244B6EE978FC8）
   - 分类 A + B。新增 5 行 / 删除 3 行；删除的 3 行全部是登记哈希行（Settings + VoxelWorld_Core.md + WorldGeneration_Core.md），新增为 2 行注释 + 3 个新登记值；无任何 Check( 断言行被增删
   - 交叉一致证据：新钉的两份规范哈希 6D471FFA184B13EC / 91F0FCDA380C91F7 与本文件 GE9 FrozenFiles 中已校验通过的值完全相同
4. GE9：[已授权修改] Editor/GeologyStageDValidation.cs（actual ABA08564B8C26871 / pin CB0C506ECD786D59）
   - 分类 B（早前已批准登记重钉 + SeaLevel 条目重钉）。新增 13 行 / 删除 9 行；删除的 9 行全部是登记哈希行（6 份规范 + Settings + Bootstrap + ChunkStreaming），新增为 6 个规范新值 + 4 行注释 + 3 个新登记值；无任何 Check( 断言行被增删
- 分类 C / D：0 项

### 受控基线同步（4 条，全部为登记哈希值）
- CriticalFiles：WorldGenerationSettings.cs 140B7E289F05C6FA -> 7F11DEE8061E4FA1（理由：分类 A，diff 本轮独立审查）
- AuthorizedChangedFiles：
  - VoxelWorldStreamingBootstrap.cs 4738634952568520 -> 8A59289F0194D20F（分类 A）
  - ChunkStreamingValidation.cs 13E244B6EE978FC8 -> 55AC4FFF4BFC13C8（分类 A+B，仅登记行/注释变化）
  - GeologyStageDValidation.cs CB0C506ECD786D59 -> ABA08564B8C26871（分类 B，仅登记行/注释变化）
- 同步方式：4 次精确匹配替换（每次断言匹配数 = 1），文件行数 1145 -> 1145 不变，无断言行增删；三表条目数 25/34/6 不变，未扩展/缩小集合，未跳过失败文件，未无条件重登记，未覆盖原始日志
- 首次同步尝试因「GeologyStageDValidation.cs 是字典最后一条、无尾逗号」导致匹配数 0，脚本在写入前抛错，**文件未被修改**（字节数 48759 前后一致）；修正模式后重做成功
- 声明：哈希通过只证明与当前基线一致，不等于功能正确性证明

### 复跑（同步后）
- 命令同上，日志重定向为 <TEMP>\unity_r12_gri_rerun.log
- 退出码：0
- 日志：_phase2b_validation/round12_GeologyRuntimeIntegrationValidation_rerun.log（124100 bytes，独立文件）
- 结果：Checks = 45，Failures = 0，Result = PASS（PASS 45 / FAIL 0），error CS = 0
- Checks 49 -> 45 的原因：4 条文件级失败 Check 在哈希匹配时不再发出（49 - 4 = 45），非删除断言
- 关键断言复跑值：GE7「关键文件全部存在（缺失 0）」PASS；GE7「…共 25 个文件 SHA 不变」PASS；GE9「既有文件全部存在（缺失 0）」PASS；GE9「既有文件 SHA256(前16) 与登记一致（比对 40 个；6 个已授权修改；跳过 0）」PASS
- 编译警告：warning CS 行 = 4（本轮修改验证器触发重编译），原文如下：  - Assets\Editor\VoxelCoordinateMigrationStageB3CValidation.cs(2130,50): warning CS0162: Unreachable code detected
  - Assets\Editor\MiningSurfaceIntegrityValidation.cs(432,24): warning CS0414: The field 'MiningSurfaceIntegrityValidation.LastScannedFaceCount' is assigned but its value is never used
  - Assets\Editor\VoxelCoordinateMigrationStageB3CValidation.cs(2130,50): warning CS0162: Unreachable code detected
  - Assets\Editor\MiningSurfaceIntegrityValidation.cs(432,24): warning CS0414: The field 'MiningSurfaceIntegrityValidation.LastScannedFaceCount' is assigned but its value is never used
- 去重：上列为 2 条唯一警告各出现 2 次（VoxelCoordinateMigrationStageB3CValidation.cs CS0162 / MiningSurfaceIntegrityValidation.cs CS0414），均在海量既有文件，与本轮无关

### 重要发现：跨验证器级联（本轮只取证、不修复）
- 第十一轮对 Editor/ChunkStreamingValidation.cs 的注释与登记同步，使 GeologyStageDValidation 的 GW10 条目 F2C2C9BFB4509DC4 过期。
- 本轮只读复算实测：StageD 登记 63 条，不匹配 1 条 = Editor/ChunkStreamingValidation.cs（pin F2C2C9BFB4509DC4 / actual 55AC4FFF4BFC13C8）。
- 结论：**第十轮记录的 StageD PASS 自第十一轮起不再可复现**，需单独一轮修复（同流程：先跑原件日志 -> 审查 -> 同步 -> 复跑）。本轮依"只处理 GeologyRuntimeIntegrationValidation"限定，未改动 StageD。
- 附带观察（既有、非本轮引入，未修改）：GE7 聚合断言文本硬编码为「Runtime Core(12)」，而 CriticalFiles 实际只列 11 个 Runtime Core 条目（总数 25 = 4+11+5+2+3 正确）；属描述性文本与条目数的既有不一致，改动它会触及断言文本，留待专项。

### 尚未执行的验证项目
- GeologyStageDValidation.RunBatch 复跑（需先修复上述级联过期条目）
- VoxelWorldRuntimeCoreValidation.RunBatch
- Elevation 等价验证复跑
- WorldParameter_Source_Contract.md 的 WPS56 状态与 A1 Ocean 配置来源同步
- 最终全量 git diff 审计
---

## 第十三轮记录：修复 StageD GW10 跨验证器级联失效

### 阶段0 现场与既有基线核对（全部在位）
- 分支 main / HEAD 37246478ed354a1e9a0ce7a3a3d9457caf188062；未 commit / push / reset
- StageD GW10：WorldGenerationSettings.cs=7F11DEE8061E4FA1（L917）、VoxelWorldStreamingBootstrap.cs=8A59289F0194D20F（L945）、Editor/ChunkStreamingValidation.cs=F2C2C9BFB4509DC4（L952，**本轮目标**）
- CS12：WorldGenerationSettings.cs=7F11DEE8061E4FA1（L869）
- GRI：GE7 CriticalFiles Settings=7F11DEE8061E4FA1（L550）；GE9 AuthorizedChangedFiles Bootstrap=8A59289F0194D20F（L766）、ChunkStreamingValidation.cs=55AC4FFF4BFC13C8（L767）、GeologyStageDValidation.cs=ABA08564B8C26871（L771）
- StageD 同步前结构快照：文件 1252 行 / GW10 条目 63 / Check( 调用 52

### 阶段1 同步前真实运行（未预设失败数量）
- 命令：D:\gamemaking\2022.3.62f1c1\Editor\Unity.exe -batchmode -nographics -quit -projectPath "D:\gamemaking\My project" -logFile <TEMP>\unity_r13_stageD_raw.log -executeMethod GeologyStageDValidation.RunBatch
- 退出码：1
- 日志：_phase2b_validation/round13_GeologyStageDValidation_raw.log（113165 bytes）；stdout/stderr 各 0 字节
- GW1–GW10 实际执行取证（各分节断言行数）：GW1=3 GW2=5 GW3=2 GW4=4 GW5=3 GW6=6 GW7=9 GW8=6 GW9=5 GW10=4
- 结果：Checks = 45，Failures = 2，Result = FAIL（PASS 43 / FAIL 2）
- 失败项（共 2 条，与独立复算一致）：
  1. FAIL - GW10：Editor/ChunkStreamingValidation.cs 已改变（55AC4FFF4BFC13C8 != F2C2C9BFB4509DC4）
  2. FAIL - GW10：既有文件 SHA256(前16) 全部不变（比对 63 个…；跳过 0）—— 聚合断言
- 缺失文件：0（GW10「既有文件全部存在」PASS）

### 阶段2 差异审查
- 独立复算 63 条：缺失 0 / 不匹配 1（仅 Editor/ChunkStreamingValidation.cs），与 Unity 实跑逐条一致；**无第二个差异**
- 被审文件 diff 复枚举（与第十二轮完全一致，未出现新改动）：新增 5 行 / 删除 3 行
  - 删除的 3 行全部是登记哈希行：WorldGenerationSettings.cs 旧钉、VoxelWorld_Core.md 旧钉 F718B68E95100C57、WorldGeneration_Core.md 旧钉 684753D5E6B31565
  - 新增：2 行说明注释 + WorldGenerationSettings.cs 新钉 7F11DEE8061E4FA1（第十一轮 CS12 同步）+ 两份规范新钉 6D471FFA184B13EC / 91F0FCDA380C91F7（更早一轮规范基线重钉）
  - 含 Check( 的变更行数 = 0 ⇒ 无断言增删/弱化
- 与第十一/十二轮证据比对：当前实际哈希 55AC4FFF4BFC13C8 与第十二轮 GE9 登记值 **完全一致**，说明该文件自第十二轮同步后未再变动
- 分类：该差异属**已批准的验证器登记值修改**（A：SeaLevel 相关登记 + 更早一轮规范基线重钉），且已在第十二轮 GE9 中经独立审查并登记，本轮再次独立复核通过

### 阶段3 受控同步（单条）
- 仅更新 Editor/ChunkStreamingValidation.cs 一条：F2C2C9BFB4509DC4 -> 55AC4FFF4BFC13C8，并附 2 行说明注释
- 同步前后结构核对：文件 1252 -> 1254 行（+2 仅注释）；**GW10 条目数 63 不变**；**Check( 调用数 52 不变**；旧哈希残留 0
- 同步后复算 63 条：缺失 0 / 不匹配 0
- 未做：删除或弱化断言、跳过失败文件、改变 GW10 文件集合、全量重登记、覆盖原始日志、修改生产代码规避哈希检查
- 一次执行中断如实记录：首次尝试因 git 的 CRLF 提示在 ErrorActionPreference=Stop 下成为终止错误，脚本在写入前中止，文件未被修改；改以 Continue 重做成功

### 阶段4 复跑
- 命令同上，日志重定向为 <TEMP>\unity_r13_stageD_rerun.log
- 退出码：0
- 日志：_phase2b_validation/round13_GeologyStageDValidation_rerun.log（117154 bytes，独立文件）
- 结果：Checks = 44，Failures = 0，Result = PASS（PASS 44 / FAIL 0），error CS = 0
- GW10 逐项：缺失 0 PASS；不匹配 0（比对 63 个，跳过 0）PASS；10 个验证入口存在 PASS
- Checks 45 -> 44 的原因：文件级失败 Check 在哈希匹配时不再发出（45 - 1 = 44），非删除断言
- 编译警告：warning CS 行 = 4（= 2 条唯一警告各重复 2 次：VoxelCoordinateMigrationStageB3CValidation.cs CS0162 / MiningSurfaceIntegrityValidation.cs CS0414），均在既有文件、与本轮无关
- 声明：哈希通过只证明与当前基线一致，不等于功能正确性证明

### 本轮观察（仅记录，未修改）
- GE7 说明文本与条目数不一致：GeologyRuntimeIntegrationValidation 的 CriticalFiles 代码注释写「Runtime Core（12 个）」，GE7 聚合断言文本也写「Runtime Core(12)」，但实测条目构成为 Generation=4 / Runtime Core=11 / Streaming=5 / Mesh+View=5 / 合计=25。
- 该不一致属描述性文本，聚合断言的比对与统计逻辑本身正确（compared=25）。修改它会触及断言文本，按本轮限定未改动，留待专项处理。

### 尚未执行的验证项目
- VoxelWorldRuntimeCoreValidation.RunBatch
- Elevation 等价验证复跑
- WorldParameter_Source_Contract.md 的 WPS56 状态与 A1 Ocean 配置来源同步
- GE7 说明文本（12 vs 11）专项修正
- 最终全量 git diff 审计
---

## 第十四轮记录：VoxelWorldRuntimeCoreValidation 回归验证（只读，未修改任何文件）

### 阶段0 现场
- 分支 main / HEAD 37246478ed354a1e9a0ce7a3a3d9457caf188062；未 commit / push / reset
- git status --short：12 个 tracked M + 4 个 untracked 源文件 + 3 个证据目录（与第十三轮结束时一致，本轮未新增任何改动）
- 三个证据目录保留：_b3b_diff/、_finalization_backup/、_phase2b_validation/

### 阶段1 验证器入口与机制（实测）
- 文件：Assets/Editor/VoxelWorldRuntimeCoreValidation.cs，1724 行
- 入口：VoxelWorldRuntimeCoreValidation.RunBatch()（L63）→ ValidateAll()（L77）→ 17 项子验证 → EditorApplication.Exit(failures==0?0:1)（L74）
- 子验证：闭环、预算 1/帧、RCI1–RCI14、RCI15 既有文件 SHA 不变、Unity 侧真实 View 闭环
- 冻结表：FrozenFiles（L1159–L1180，**18 条**），哈希算法 ComputeFileHash16（L1209，SHA256 前 8 字节 = 16 位大写十六进制），路径 = Application.dataPath + key
- 本验证器自身未被修改（git diff 为空）⇒ 断言与冻结表集合本轮未变

### 阶段2 真实运行
- 命令：D:\gamemaking\2022.3.62f1c1\Editor\Unity.exe -batchmode -nographics -quit -projectPath "D:\gamemaking\My project" -logFile <TEMP>\unity_r14_rtcore_raw.log -executeMethod VoxelWorldRuntimeCoreValidation.RunBatch
- 退出码：1
- 日志：_phase2b_validation/round14_VoxelWorldRuntimeCoreValidation_raw.log（271151 bytes）；stdout/stderr 各 0 字节
- 入口实际执行确认：目标类标记行 3 条，PASS 195 + FAIL 2 = 197 条断言真实产出，非仅凭退出码判断
- 结果：Checks = 197，Failures = 2，Result = FAIL
- error CS = 0；warning CS 行 = 0（本轮未触发重新编译，命中缓存；不代表既有 2 条警告被消除）

### 阶段3 独立复核
- 失败项共 2 条，全属 RCI15 哈希类，无功能断言失败、无缺失文件（缺失 0）、无编译错误：
  1. FAIL - RCI15：Scripts/VoxelWorld/Generation/WorldGenerationSettings.cs 已改变（7F11DEE8061E4FA1 != 140B7E289F05C6FA）
  2. FAIL - RCI15：既有文件 SHA256(前16) 全部不变（比对 18 个，不一致 1）—— 聚合断言
- 独立复算 18 条（按该验证器同一路径规则与同一算法）：匹配 17 / 缺失 0 / 不匹配 1，与实跑逐条一致
- 18 条明细复算结果：除 WorldGenerationSettings.cs 一行 DIFF，其余 17 条全部 OK（VoxelCore 6 / Generation 3 / Mesh 2 / View 3 / 既有 Editor 入口 4）
- 该冻结表集合**不包含**第十至十三轮修改过的任何验证器文件（ChunkStreamingValidation / StageD / GRI / Bootstrap 均不在表内）⇒ 无新增级联风险，唯一失效点就是 SeaLevel 文件
- WorldGenerationSettings.cs 本轮独立 diff 审查（第 5 次独立复核）：新增 46 行 / 删除 0 行；非 SeaLevel/非文档/非结构行 = 0；删除行清单为空 ⇒ 无断言弱化、无未授权改动
- 与既往证据核对：本轮实测 7F11DEE8061E4FA1 与第十、十一、十二、十三轮记录完全一致；该值已在 StageD（L917）、CS12（L869）、GRI（L550）三处登记，仅本验证器 RCI15 仍为旧钉 140B7E289F05C6FA
- 结论：失败属「合法修改（已批准 SeaLevel 迁移）导致的冻结哈希失效」，非功能回归、非既有问题

### 阶段4 最小同步方案（本轮未执行，留待下一轮）
- 文件：Assets/Editor/VoxelWorldRuntimeCoreValidation.cs
- 位置：FrozenFiles 内 Scripts/VoxelWorld/Generation/WorldGenerationSettings.cs 条目
- 旧值：140B7E289F05C6FA  ->  新值：7F11DEE8061E4FA1
- 预期：条目数 18 不变、Check( 调用数不变、复跑 exit 0 / Checks 196 / Failures 0（197 - 1 条文件级失败 Check 不再发出）
- 本轮按"默认只读"与"只提出最小方案"的限定，未修改该文件；也未触碰 GE7 的 Runtime Core(12) 说明文本

### 阶段5 结论
- 需要下一轮最小修复：是，且仅需上述 1 条登记值（+ 复跑验证）
- 未执行：该同步与复跑、Elevation 等价验证、WPS56/A1 Ocean 文档同步、GE7 说明文本修正、最终全量 git diff 审计
---

## 第十五轮记录：修复 RCI15 SeaLevel 冻结哈希并复跑

### 阶段0 现场
- 分支 main / HEAD 37246478ed354a1e9a0ce7a3a3d9457caf188062（与第十四轮一致）；未 commit / push / reset
- 第十四轮结束时 git status 与本轮开始时逐条一致（12 tracked M + 4 untracked + 3 证据目录）
- 三个证据目录与全部历史日志保留，未覆盖

### 阶段1 修改前复核
- 目标文件 Scripts/VoxelWorld/Generation/WorldGenerationSettings.cs 独立复算 = 7F11DEE8061E4FA1（与目标值一致）
- 目标文件 diff 复核：新增 46 / 删除 0 / 非 SeaLevel·非文档·非结构行 = 0 ⇒ 无新增未审查改动
- 目标登记项位置：Assets/Editor/VoxelWorldRuntimeCoreValidation.cs 的 FrozenFiles 内 L1168

### 阶段2 单行同步
- 仅改动 1 条登记值：140B7E289F05C6FA -> 7F11DEE8061E4FA1
- git numstat = 1 新增 / 1 删除 / 1 文件；git diff hunk = @@ -1167,3 +1167,3 @@，只有该行 -/+ 一对
- 逐行内存比对：变化行数 = 1（L1168），其余全同；行数 1724 -> 1724 不变
- FrozenFiles 条目数 = 18（不变）；Check( 调用数 = 162（不变）；旧哈希残留 0 / 新哈希计数 1
- 18 条登记项复算：匹配 18 / 不匹配 0
- 未修改其他哈希项、未改生产代码、未触碰 GE7 的 Runtime Core(12) 文本

### 阶段3 真实 Unity 复跑
- 命令：D:\gamemaking\2022.3.62f1c1\Editor\Unity.exe -batchmode -nographics -quit -projectPath "D:\gamemaking\My project" -logFile <TEMP>\unity_r15_rtcore_rerun.log -executeMethod VoxelWorldRuntimeCoreValidation.RunBatch
- 退出码：0
- 日志：_phase2b_validation/round15_VoxelWorldRuntimeCoreValidation_rerun.log（275038 bytes）；stdout/stderr 各 0 字节
- 入口执行证据：目标类标记行 3 条 + PASS 196 条断言真实产出
- 结果：Checks = 196，Failures = 0，Result = PASS（与第十四轮提出的预期 0 / 0 / 196 完全一致；预期仅用于验证，未据此过滤任何输出）
- RCI15 复跑断言：18 个既有文件全部存在（缺失 0）PASS；SHA256(前16) 全部不变（比对 18 个，不一致 0）PASS
- error CS = 0；warning CS 行 = 4（2 条唯一警告各重复 2 次：VoxelCoordinateMigrationStageB3CValidation.cs CS0162 / MiningSurfaceIntegrityValidation.cs CS0414），与第十至十四轮同源，**非新增**

### 阶段4 审计：本轮编辑造成的跨验证器登记过期（重要，未扩大修复）
- Editor/VoxelWorldRuntimeCoreValidation.cs 哈希由 0E0B5C160FEAB236 变为 C5ABCECD2C338BED
- 全工程扫描：同一 pin 值 0E0B5C160FEAB236 出现在 **6 个验证器**共 6 处（ChunkStreamingValidation L896、GeologyRuntimeIntegrationValidation L747、GeologyStageAValidation L868、GeologyStageBValidation L975、GeologyStageCValidation L975、GeologyStageDValidation L951）
- 逐表只读复算（未修改任何一张表）：
  - ChunkStreamingValidation.cs   登记 40 / 不匹配 1（Editor/VoxelWorldRuntimeCoreValidation.cs）
  - GeologyRuntimeIntegrationValidation.cs 登记 65 / 不匹配 2（Editor/VoxelWorldRuntimeCoreValidation.cs、Editor/GeologyStageDValidation.cs）
  - GeologyStageDValidation.cs    登记 63 / 不匹配 1（Editor/VoxelWorldRuntimeCoreValidation.cs）
  - GeologyStageA/B/CValidation.cs 登记 51/55/57 / 各不匹配 9（其中 1 条为本轮引入的 RuntimeCoreValidation，其余 8 条为既有：5 份规范重钉 + SeaLevel 两个文件 + ChunkStreamingValidation）
- **另外发现（此前漏报，据实披露）**：第十三轮编辑 GeologyStageDValidation.cs 后，GRI 的 GE9 登记值 ABA08564B8C26871 即已过期，第十三/十四轮报告未指出该条；本轮复算确认其为 GRI 第 2 条不匹配
- 结论：该仓库采取「验证器交叉自钉」结构 ⇒ 每次编辑任一验证器都会使约 6 处登记过期，属结构性代价而非功能缺陷；按本轮"停止扩大修改范围"的停止条件，未同步任何一张表，仅提交证据

### 后续建议（需所有者决策，不自行实施）
1. 最小修复序：按既有节奏逐个验证器一轮，先补 CS12 与 StageD 各 1 条（RuntimeCoreValidation 新哈希 C5ABCECD2C338BED），再处理 GRI 2 条
2. Stage A/B/C 各有 9 条过期（含既有 8 条），需单独立轮审查，不可批量重钉
3. 结构性议题（供所有者决定，未擅自改动）：交叉自钉使每次验证器编辑都触发 N 处级联；是否改为「只钉生产代码、不互钉验证器」需架构级批准

### 尚未执行
- 上述 6 张表的登记同步
- Elevation 等价验证复跑
- WorldParameter_Source_Contract.md 的 WPS56 状态与 A1 Ocean 来源同步
- GE7 Runtime Core(12) 文本专项
- 最终全量 git diff 审计
---

## 第十六轮记录：验证器交叉哈希依赖全量只读审计

### 保护现场
- 分支 main / HEAD 37246478ed354a1e9a0ce7a3a3d9457caf188062；未 commit / push / reset / 清理
- **本轮未修改任何验证器、生产代码或冻结哈希值**；仅新增 2 个证据文件并追加本记录
- 三个证据目录与全部历史日志保留

### 盘点结果（含清单外验证器）
- 全 Assets 范围扫描含 16 位冻结哈希的 .cs：**恰好 7 个文件**（无遗漏的其他验证器）
- 共 **9 张登记表 / 349 条登记**：
  - ChunkStreamingValidation.FrozenFiles=40
  - GeologyRuntimeIntegrationValidation.CriticalFiles=25 / FrozenFiles=34 / AuthorizedChangedFiles=6
  - GeologyStageAValidation.FrozenFiles=51
  - GeologyStageBValidation.FrozenFiles=55
  - GeologyStageCValidation.FrozenFiles=55 / AuthorizedChangedFiles=2
  - GeologyStageDValidation.FrozenFiles=63
  - VoxelWorldRuntimeCoreValidation.FrozenFiles=18
- 算法**逐表实测核验**（不使用统一假设）：7 个验证器各自实现 ComputeFileHash16，均为 SHA256 → 取前 8 字节 → ToString("X2") 拼接 16 位大写十六进制；失败标签分别为 CS12 / GE9 / GSA6 / GB7 / GC8 / GW10 / RCI15
- 路径规则逐表核验：规范键 `../Specifications/` → 工程根（FindSpecificationsRoot 向上找 Specifications/World，找不到则显式 skipped 计数，不伪装 PASS）；其余键 → Application.dataPath 拼接；RCI15 无规范键
- 缺失处理：各表均 Check(false, "…缺失…") 并计入 missing，另有聚合断言；本轮 349 条**缺失 0**

### 独立复算结果
- 349 条：**匹配 318 / 不匹配 31 / 缺失 0 / 结构异常 0**
- 差异分类：**A=6 / B=19 / C=6 / D=0 / E=0**
  - A（第十五轮合法修改所致，1 个文件 × 6 张表）：Editor/VoxelWorldRuntimeCoreValidation.cs 0E0B5C160FEAB236 -> C5ABCECD2C338BED（CS12、GRI.FrozenFiles、StageA、StageB、StageC、StageD）
  - B（此前已发生但未发现/未报告）：
    - Editor/GeologyStageDValidation.cs ABA08564B8C26871 -> A26D957C4B698097（GRI.AuthorizedChangedFiles，第十三轮编辑所致，第十三/十四轮未报告）1 处
    - Editor/ChunkStreamingValidation.cs 13E244B6EE978FC8 -> 55AC4FFF4BFC13C8（Stage A/B/C，第十一/十二轮所致）3 处
    - 5 份规范（VoxelWorld_Architecture / VoxelWorld_Core / WorldGeneration_Core / WorldGeneration_Geology / WorldGeneration_Geology_Implementation）在 Stage A/B/C 的旧登记（早前提交的规范更新；实际值等于 StageD/GRI 中已校验通过的值）15 处
  - C（已批准但尚未同步）：WorldGenerationSettings.cs 140B7E289F05C6FA -> 7F11DEE8061E4FA1、VoxelWorldStreamingBootstrap.cs 4738634952568520 -> 8A59289F0194D20F，各在 Stage A/B/C 共 6 处
  - D＝0，E＝0
- 特别复核第十五轮三项断言：RuntimeCoreValidation 现哈希确为 C5ABCECD2C338BED（6 处失效确认）；GRI 对 StageD 的过期确认（1 处）；Stage A/B/C 各 9 条拆分为「本轮归因 1 条（RuntimeCoreValidation）+ 其余 8 条（5 规范 + 2 SeaLevel 生产文件 + ChunkStreamingValidation）」

### 交叉依赖图（以实际登记表为证据）
- 反向计数：4 个 legacy core 验证器各被 7 张表登记（全部匹配）；VoxelWorldRuntimeCoreValidation.cs 被 6 张（全部失效）；ChunkStreamingValidation.cs 被 5 张（3 失效）；GeologyStageAValidation.cs 被 4 张（0 失效）；StageB 被 3 张、StageC 被 2 张（0 失效）；GeologyStageDValidation.cs 被 1 张（GRI，失效）
- 环路检查：依赖方向严格为旧阶段→新阶段，**无循环依赖**；无验证器自钉
- 影响面：编辑 ChunkStreamingValidation.cs 最多波及 5 张表；编辑 VoxelWorldRuntimeCoreValidation.cs 波及 6 张；编辑 4 个 legacy core 验证器波及 7 张

### 当前真实执行结果 vs 历史结果（严格区分）
- 本会话真实运行过的 4 个验证器：仅 VoxelWorldRuntimeCoreValidation 当前仍 PASS（R15：exit 0 / 196 / 0）；StageD(R13:44/0)、GRI(R12:45/0)、CS12(R11:107/0) 的历史 PASS 在当前工作树下**已不可复现**
- Stage A/B/C 与 4 个 legacy core 验证器**本会话从未运行**，状态未知，不得标记为 PASS

### 交付物
- _phase2b_validation/round16_hash_dependency_audit.md（审计报告：登记表清单、349 条复算、依赖边表、反向计数、分类、修复计划、设计代价）
- _phase2b_validation/round16_hash_audit_rows.csv（349 条逐项明细）

### 建议的最小修复计划（本轮未实施）
1. Stage A/B/C 各 9 条（叶子层，需逐条审查、每个验证器一轮、禁止批量重钉）
2. CS12 1 条、StageD 1 条（RuntimeCoreValidation 新值 C5ABCECD2C338BED）
3. GRI 最后修 2 条（含 StageD 新哈希 A26D957C4B698097，因 StageD 若再被编辑该值会继续变化）
4. RTC 当前 0 失效，无需改动
- 结构性提示：修复 Stage A/B/C 会再次改动这 3 个验证器文件，从而使其被钉处再次失效，故执行时必须把「最后一次编辑」安排在依赖链末端

### 尚未执行
- 上述全部登记同步与复跑
- Stage A/B/C 与 legacy core 验证器的真实运行
- Elevation 等价验证复跑
- WPS56 / A1 Ocean 文档同步
- GE7 Runtime Core(12) 文本专项
- 最终全量 git diff 审计
---

## 第十七轮记录：GeologyStageAValidation 冻结哈希最小修复

### 阶段0 现场
- 分支 main / HEAD 37246478ed354a1e9a0ce7a3a3d9457caf188062；事务 20 项状态与第十六轮末一致；未 commit / push / reset / 清理
- 以第十六轮 round16_hash_audit_rows.csv 逐项记录为起点：Stage A 不匹配 = 9 条，与第十六轮完全一致（数量、文件集合、分类均无出入，未触发停止条件）

### 阶段1 逐条独立确认（9 条全部为 C 类：证据充分、已批准、尚未同步）
| 键 | 旧登记值 | 当前实际值 | 证据 |
|---|---|---|---|
| ../Specifications/World/VoxelWorld_Architecture.md | 779DF1EFC4082579 | F09B17A1002D7CD2 | 工作区无改动；最后提交 9bb687b；实际值已被 StageD(R13)、GRI(R12) 实跑校验通过 |
| ../Specifications/World/VoxelWorld_Core.md | F718B68E95100C57 | 6D471FFA184B13EC | 最后提交 5d5baec；StageD/GRI/CS12 三处已按该值登记并通过 |
| ../Specifications/World/WorldGeneration_Core.md | 684753D5E6B31565 | 91F0FCDA380C91F7 | 最后提交 f606865；StageD/GRI/CS12 三处已登记并通过 |
| ../Specifications/World/WorldGeneration_Geology.md | 1A7DD7E7AE1EBAC9 | D1E70FE71BC400E1 | 最后提交 fe6c991；StageD/GRI 已登记并通过 |
| ../Specifications/World/WorldGeneration_Geology_Implementation.md | 9BF0E11A4E5CA87D | 2700AE40B0CB09A0 | 最后提交 4b8eaae；StageD/GRI 已登记并通过 |
| Scripts/VoxelWorld/Generation/WorldGenerationSettings.cs | 140B7E289F05C6FA | 7F11DEE8061E4FA1 | 已批准 SeaLevel 迁移（第 10–15 轮五次独立 diff 审查，+46-0）；StageD/CS12/GRI/RTC 四处已登记并通过 |
| Scripts/Game/VoxelWorldStreamingBootstrap.cs | 4738634952568520 | 8A59289F0194D20F | 已批准 SeaLevel 配置字段（+4-1）；StageD/GRI 已登记并通过 |
| Editor/VoxelWorldRuntimeCoreValidation.cs | 0E0B5C160FEAB236 | C5ABCECD2C338BED | 第十五轮已审查的单行修复（git numstat 1/1）；RTC(R15) exit 0 / 196 PASS |
| Editor/ChunkStreamingValidation.cs | 13E244B6EE978FC8 | 55AC4FFF4BFC13C8 | 第 11–12 轮已批准编辑（+5-3，无断言行增删）；StageD(R13)、GRI(R12) 已登记并通过 |

### 阶段2 最小同步
- 仅替换 GeologyStageAValidation.cs 中 9 条已确认的目标登记值；每条替换前断言精确匹配数 = 1（9/9 成功）
- 结构核对：文件行数 1088（不变）/ FrozenFiles 条目数 51（不变）/ Check( 调用数 83（不变）；git numstat = +9 −9
- 同步后 51 条逐项复算：不匹配 = 0
- 未修改生产代码、未改断言逻辑/数量、未改文件集合与路径规则、未改哈希算法、未机械复制全部实际值（9 条逐条有审查依据）

### 阶段3 真实复跑
- 入口：GeologyStageAValidation.RunBatch()（L47，Menu L38，EditorApplication.Exit L57）
- 命令：D:\gamemaking\2022.3.62f1c1\Editor\Unity.exe -batchmode -nographics -quit -projectPath "D:\gamemaking\My project" -logFile <TEMP>\unity_r17_stageA_rerun.log -executeMethod GeologyStageAValidation.RunBatch
- 退出码：0
- 日志：_phase2b_validation/round17_GeologyStageAValidation_rerun.log（170315 bytes）；stdout/stderr 各 0 字节
- 入口实际执行证据：目标类标记行 6 条 + PASS 105 条断言真实产出
- 结果：Checks = 105，Failures = 0，Result = PASS（PASS 105 / FAIL 0），缺失 0，哈希不匹配 0
- GSA6：既有文件全部存在（缺失 0）PASS；SHA256(前16) 全部不变（比对 51 个；跳过 0）PASS
- error CS = 0；warning CS 行 = 4（2 条唯一警告各重复 2 次：VoxelCoordinateMigrationStageB3CValidation.cs CS0162 / MiningSurfaceIntegrityValidation.cs CS0414），非新增

### 阶段4 交叉影响重算（未顺手更新其它验证器）
- 本轮唯一改动的源文件：Assets/Editor/GeologyStageAValidation.cs，哈希 DF42F8605BA60CAF -> 83731D5FB896BA38
- 该文件被 4 张表登记，全部因此失效（与第十六轮预测的 4 处完全一致）：
  - GeologyStageDValidation.FrozenFiles
  - GeologyRuntimeIntegrationValidation.AuthorizedChangedFiles
  - GeologyStageBValidation.FrozenFiles
  - GeologyStageCValidation.AuthorizedChangedFiles
- 全量复算：总登记 349 / 不匹配 **26**（第十六轮 31）
  - 已修复 9 条（全部为 Stage A 的 C 类）
  - 本轮新引入 4 条（上列，pin DF42F8605BA60CAF -> act 83731D5FB896BA38）
  - 仍然存在 22 条（历史遗留：StageB/C 各 8～9 条、CS12 1 条、StageD 1 条、GRI 2 条）
- 净变化 31 - 9 + 4 = 26，与实测一致；无无法解释的差异
- 第十六轮 round16_hash_dependency_audit.md **未被改动**（20350 bytes 保持原样）；当前状态另建追加式增量记录 round17_hash_dependency_audit_delta.md

### 阶段5 结论
- Stage A 已从「9 条失效」恢复为「51 条全匹配 + 真实运行 PASS」；这是本会话内第 2 个当前可复现 PASS 的验证器（另一个是 RTC）
- 仍需后续轮次：StageB(9)、StageC(9)、CS12(1)、StageD(1)、GRI(2) 的登记同步；其中 StageB/C/StageD/GRI 各含 1 条本轮新引入的 StageA 登记（83731D5FB896BA38），应在各自轮次一并审查同步
- StageB/C、4 个 legacy core 验证器仍未在本会话真实运行，状态未知，不得标记 PASS

### 尚未执行
- StageB / StageC / CS12 / StageD / GRI 的登记同步与复跑
- 4 个 legacy core 验证器真实运行
- Elevation 等价验证复跑
- WPS56 / A1 Ocean 文档同步
- GE7 Runtime Core(12) 文本专项
- 最终全量 git diff 审计
---

## 第十八轮记录：GeologyStageBValidation 冻结哈希最小修复

### 保护现场
- 分支 main / HEAD 37246478ed354a1e9a0ce7a3a3d9457caf188062；事务状态 21 项；未 commit / push / reset / 清理
- 三份审计记录均存在并已回读：round16_hash_audit_rows.csv / round16_hash_dependency_audit.md / round17_hash_dependency_audit_delta.md

### 独立审查：实测 10 条 = 第十六轮 9 条 + 第十七轮新增 1 条（与审计链完全吻合，非不一致）
| 分组 | 键 | 旧值 | 实际值 | 证据 |
|---|---|---|---|---|
| 历史9 | ../Specifications/World/VoxelWorld_Architecture.md | 779DF1EFC4082579 | F09B17A1002D7CD2 | 已提交规范 9bb687b；StageD/GRI/StageA 三表已按该值登记并通过 |
| 历史9 | ../Specifications/World/VoxelWorld_Core.md | F718B68E95100C57 | 6D471FFA184B13EC | 已提交 5d5baec；4 表已登记并通过 |
| 历史9 | ../Specifications/World/WorldGeneration_Core.md | 684753D5E6B31565 | 91F0FCDA380C91F7 | 已提交 f606865；4 表已登记并通过 |
| 历史9 | ../Specifications/World/WorldGeneration_Geology.md | 1A7DD7E7AE1EBAC9 | D1E70FE71BC400E1 | 已提交 fe6c991；3 表已登记并通过 |
| 历史9 | ../Specifications/World/WorldGeneration_Geology_Implementation.md | 9BF0E11A4E5CA87D | 2700AE40B0CB09A0 | 已提交 4b8eaae；3 表已登记并通过 |
| 历史9 | Scripts/VoxelWorld/Generation/WorldGenerationSettings.cs | 140B7E289F05C6FA | 7F11DEE8061E4FA1 | 已批准 SeaLevel 迁移（+46-0，5 次独立审查）；5 表已登记并通过 |
| 历史9 | Scripts/Game/VoxelWorldStreamingBootstrap.cs | 4738634952568520 | 8A59289F0194D20F | 已批准 SeaLevel 配置字段（+4-1）；3 表已登记并通过 |
| 历史9 | Editor/VoxelWorldRuntimeCoreValidation.cs | 0E0B5C160FEAB236 | C5ABCECD2C338BED | 第十五轮单行修复（numstat 1/1）；RTC R15 exit0/196 PASS |
| 历史9 | Editor/ChunkStreamingValidation.cs | 13E244B6EE978FC8 | 55AC4FFF4BFC13C8 | 第 11-12 轮已批准编辑（+5-3 无断言改动）；3 表已登记并通过 |
| R17新增 | Editor/GeologyStageAValidation.cs | DF42F8605BA60CAF | 83731D5FB896BA38 | 第十七轮 StageA 9 条登记同步（+9-9）；StageA R17 exit0/105 PASS；GSA6 51/51 通过；该值在第十七轮增量记录中明确登记 |
- 分类：全部为「已批准且证据充分，尚未同步」（C 类）；**无 D 类（无法解释）差异**
- 规范 5 条工作区均无改动（变更来自更早提交），其余 5 条工作区有改动且均有前序轮次审查记录

### 最小同步
- 仅替换 GeologyStageBValidation.cs 中 10 条已确认的登记值；每条替换前断言精确匹配数 = 1（10/10 成功）
- 结构核对：1167 行（不变）/ FrozenFiles 55 条（不变）/ Check( 59（不变）；git numstat = +10 −10
- 同步后 55 条复算：不匹配 = 0；未改生产代码、断言逻辑/数量、冻结集合、路径规则、哈希算法；未机械复制全部实际值

### 真实复跑
- 入口：GeologyStageBValidation.RunBatch()（L53；EditorApplication.Exit L63）
- 命令：D:\gamemaking\2022.3.62f1c1\Editor\Unity.exe -batchmode -nographics -quit -projectPath "D:\gamemaking\My project" -logFile <TEMP>\unity_r18_stageB_rerun.log -executeMethod GeologyStageBValidation.RunBatch
- 退出码：0；日志：_phase2b_validation/round18_GeologyStageBValidation_rerun.log（182513 bytes）；stdout/stderr 各 0 字节
- 入口执行证据：目标类标记行 4 条 + PASS 122 条断言真实产出
- 结果：Checks = 122，Failures = 0，Result = PASS（PASS 122 / FAIL 0），缺失 0，哈希不匹配 0
- GB7：既有文件全部存在（缺失 0）PASS；SHA256(前16) 全部不变（比对 55 个；跳过 0）PASS
- error CS = 0；warning CS = 4（2 条唯一警告各重复 2 次，既有非新增）

### 交叉影响重算（未顺手修复 StageC / StageD / CS12 / GRI）
- 本轮 GeologyStageBValidation.cs 哈希 CFA08E4EE8D49C99 -> B5EE9A249FCF5AE3
- 新增 3 处交叉失效（与第十六轮依赖图预测的 3 张表一致）：
  - GeologyStageDValidation.FrozenFiles
  - GeologyRuntimeIntegrationValidation.AuthorizedChangedFiles
  - GeologyStageCValidation.AuthorizedChangedFiles
- 全量复算：**19 条不匹配**（R17 基线 26）＝ 基线 26 − 本轮修复 10 + 本轮新增 3，实测与算式一致
- 当前 19 条按验证器分布：ChunkStreamingValidation 1 / GeologyRuntimeIntegrationValidation 4 / GeologyStageCValidation 11 / GeologyStageDValidation 3
- 第十六、十七轮审计文件均未被改动；本轮另建 round18_hash_dependency_audit_delta.md

### 结论与后续
- Stage B 已从 10 条失效恢复为「55 条全匹配 + 真实运行 PASS」；本会话内第 3 个当前可复现 PASS 的验证器（RTC、StageA、StageB）
- 仍需后续轮次：StageC(11)、StageD(3)、GRI(4)、CS12(1)；StageC/D/GRI 各含本轮新引入的 StageB 登记（B5EE9A249FCF5AE3）
- StageC 与 4 个 legacy core 验证器仍未在本会话真实运行，状态未知，不得标记 PASS

### 尚未执行
- StageC / StageD / CS12 / GRI 的登记同步与复跑
- 4 个 legacy core 验证器真实运行
- Elevation 等价验证复跑
- WPS56 / A1 Ocean 文档同步
- GE7 Runtime Core(12) 文本专项
- 最终全量 git diff 审计
---

## 第十九轮记录：GeologyStageCValidation 冻结哈希最小修复与首次真实验证

### 保护现场
- branch=main / HEAD=37246478ed354a1e9a0ce7a3a3d9457caf188062（与基线一致）；未 commit / push / reset / clean
- 四份审计记录与进度记录全部存在且已读取：round16_hash_audit_rows.csv(55067B) / round16_hash_dependency_audit.md(20350B) / round17_hash_dependency_audit_delta.md(6100B) / round18_hash_dependency_audit_delta.md(5041B) / sealevel_round7_progress.md(53260B)
- 未发现任何无法解释的现场变化

### 逐项审查（实测 11 条，与第十八轮记录一致）
| 表 | 键 | 旧值 | 实测值 | 变更来源与前序证据 |
|---|---|---|---|---|
| FrozenFiles | ../Specifications/World/VoxelWorld_Architecture.md | 779DF1EFC4082579 | F09B17A1002D7CD2 | 已提交规范 9bb687b；StageD/GRI/StageA/StageB 四表已按该值登记并通过 |
| FrozenFiles | ../Specifications/World/VoxelWorld_Core.md | F718B68E95100C57 | 6D471FFA184B13EC | 已提交 5d5baec；五表已登记并通过 |
| FrozenFiles | ../Specifications/World/WorldGeneration_Core.md | 684753D5E6B31565 | 91F0FCDA380C91F7 | 已提交 f606865；五表已登记并通过 |
| FrozenFiles | ../Specifications/World/WorldGeneration_Geology.md | 1A7DD7E7AE1EBAC9 | D1E70FE71BC400E1 | 已提交 fe6c991；四表已登记并通过 |
| FrozenFiles | ../Specifications/World/WorldGeneration_Geology_Implementation.md | 9BF0E11A4E5CA87D | 2700AE40B0CB09A0 | 已提交 4b8eaae；四表已登记并通过 |
| FrozenFiles | Scripts/VoxelWorld/Generation/WorldGenerationSettings.cs | 140B7E289F05C6FA | 7F11DEE8061E4FA1 | 已批准 SeaLevel 迁移（+46-0，5 次独立审查）；六表已登记并通过 |
| FrozenFiles | Scripts/Game/VoxelWorldStreamingBootstrap.cs | 4738634952568520 | 8A59289F0194D20F | 已批准 SeaLevel 配置字段（+4-1）；四表已登记并通过 |
| FrozenFiles | Editor/VoxelWorldRuntimeCoreValidation.cs | 0E0B5C160FEAB236 | C5ABCECD2C338BED | 第十五轮单行修复（numstat 1/1）；RTC(R15) exit0/196 PASS；StageA(R17)/StageB(R18) 已登记并通过 |
| FrozenFiles | Editor/ChunkStreamingValidation.cs | 13E244B6EE978FC8 | 55AC4FFF4BFC13C8 | 第 11-12 轮已批准编辑（+5-3 无断言改动）；四表已登记并通过 |
| AuthorizedChangedFiles | Editor/GeologyStageAValidation.cs | DF42F8605BA60CAF | 83731D5FB896BA38 | 第十七轮登记同步（+9-9）；StageA(R17) exit0/105 PASS；StageB(R18) 已登记并通过 |
| AuthorizedChangedFiles | Editor/GeologyStageBValidation.cs | CFA08E4EE8D49C99 | B5EE9A249FCF5AE3 | 第十八轮登记同步（+10-10）；StageB(R18) exit0/122 PASS；GB7 55/55 通过 |
- 全部 11 条路径实测存在（缺失 0，无路径解析异常）；哈希算法核验为 1 处标准实现（SHA256 前 8 字节 → 16 位大写十六进制），未变化
- 分类：11 条全部「已批准且证据充分、尚未同步」；**无未知或无法解释（D 类）差异**；差异仅涉及冻结哈希登记，不涉及未审查的语义或架构变更

### 最小同步
- 仅替换 GeologyStageCValidation.cs 中 11 条已确认登记值；每条替换前断言精确匹配数 = 1（11/11）
- 结构核对：1209 行（不变）/ FrozenFiles 55 + AuthorizedChangedFiles 2 = 57（不变）/ Check( 55（不变）；git numstat = +11 −11
- 同步后 57 条复算：不匹配 0；未改生产代码、断言逻辑/数量、表项增删、路径规则、哈希算法；未机械复制全部实测值

### 首次真实运行 Stage C
- 入口：GeologyStageCValidation.RunBatch()（L50；EditorApplication.Exit L60）
- 命令：D:\gamemaking\2022.3.62f1c1\Editor\Unity.exe -batchmode -nographics -quit -projectPath "D:\gamemaking\My project" -logFile <TEMP>\unity_r19_stageC_rerun.log -executeMethod GeologyStageCValidation.RunBatch
- 退出码：0；耗时 29s；日志：_phase2b_validation/round19_GeologyStageCValidation_rerun.log（121376 bytes）；stdout/stderr 各 0 字节
- 入口真实执行证据：目标类标记行 5 条 + PASS 49 条断言真实产出（非仅凭退出码）
- 结果：Checks = 49，Failures = 0，Result = PASS；缺失文件 0；哈希不匹配 0
- GC8：既有文件全部存在（缺失 0）PASS；既有文件 SHA256(前16) 与登记一致（比对 57 个；其中 2 个为本轮已授权修改并用新哈希钉住；跳过 0）PASS
- error CS = 0；warning CS = 4（2 条唯一警告各重复 2 次：VoxelCoordinateMigrationStageB3CValidation.cs CS0162 / MiningSurfaceIntegrityValidation.cs CS0414），既有非新增
- 判定：**Stage C 达到可复现 PASS**（入口真实执行 + 断言真实产出 + Failures 0 + 缺失 0 + 不匹配 0 + 退出码 0）

### 交叉哈希级联复算
- Stage C 自身新哈希：4379D1DD622EB737 -> F2FED5F4A6D68117
- 引用 Stage C 哈希的登记表共 2 处（与第十六轮依赖图一致）：GeologyRuntimeIntegrationValidation.AuthorizedChangedFiles、GeologyStageDValidation.FrozenFiles
- 全量复算（349 条登记）：**10 条不匹配**（R18 基线 19）
  - 本轮修复 11 条（StageC.FrozenFiles 9 + StageC.AuthorizedChangedFiles 2）
  - 本轮新增级联 2 条（上述两处，均为 StageC.cs 旧钉）
  - 仍然存在 8 条（历史遗留）
- 算式校验：19 − 11 + 2 = 10，与实测一致；**未发现依赖图之外的额外级联**
- 当前 10 条按表分布：ChunkStreamingValidation.FrozenFiles 1 / GRI.AuthorizedChangedFiles 4 / GRI.FrozenFiles 1 / StageD.FrozenFiles 4
- 未修复 StageD / GRI / CS12 / RTC 中任何登记；第十六至十八轮审计文件均未被改动，本轮另建 round19_hash_dependency_audit_delta.md

### 结论与后续
- 本会话内当前可复现 PASS 的验证器已达 4 个：RTC(R15)、StageA(R17)、StageB(R18)、StageC(R19)
- 仍待处理：StageD.FrozenFiles(4)、GRI(5)、CS12(1)；其中 StageD 与 GRI 各含本轮新引入的 StageC 登记（F2FED5F4A6D68117）
- 4 个 legacy core 验证器仍未在本会话真实运行，状态未知，不得标记 PASS
- 本轮仅为 Stage C 验证器及其冻结登记，**不构成整个地质系统的端到端验证**

### 尚未执行
- StageD / GRI / CS12 的登记同步与复跑
- 4 个 legacy core 验证器真实运行
- Elevation 等价验证复跑
- WPS56 / A1 Ocean 文档同步
- GE7 Runtime Core(12) 文本专项
- 最终全量 git diff 审计
---

## 第二十轮记录：ChunkStreamingValidation（CS12）单项哈希修复与真实验证

### 保护现场
- branch=main / HEAD=37246478ed354a1e9a0ce7a3a3d9457caf188062；未 commit / push / reset / clean
- 第十六至十九轮审计文件与进度记录全部存在并读取（round16_hash_dependency_audit.md 20350B / round17_delta 6100B / round18_delta 5041B / round19_delta 4597B / progress 59619B）
- 未发现无法解释的现场变化

### 全量重测（不沿用上一轮结论）
- CS12 FrozenFiles 40 条**全部重新实测**：不匹配 **1** 条、缺失 0 条
- 算法核验：SHA256 前 8 字节→16 位大写十六进制，1 处标准实现；路径规则（`isSpecification = pair.Key.StartsWith("../")` → 工程根，其余 → dataPath）未变
- 唯一失配：`Editor/VoxelWorldRuntimeCoreValidation.cs` pin `0E0B5C160FEAB236` act `C5ABCECD2C338BED`
- 证据：该文件为第十五轮已批准的单行修复（git numstat 1/1，RTC R15 exit0/196 PASS）；新值已被 StageA(R17)/StageB(R18)/StageC(R19) 三表登记并实跑通过；与第十六轮依赖图一致；属已批准历史变更，非未知差异

### 最小修复
- 仅替换 1 条登记值：`Editor/VoxelWorldRuntimeCoreValidation.cs` `0E0B5C160FEAB236` → `C5ABCECD2C338BED`
- 结构核对：1186 行（不变）/ FrozenFiles 40 条（不变）/ Check( 97（不变）；旧值残留 0 / 新值计数 1
- 修复后 40 条复算：不匹配 0；未改生产代码、断言逻辑/Check 调用、冻结集合、路径规则、哈希算法；未改动 StageD/GRI/StageA/B/C/RTC

### 真实验证
- 入口：ChunkStreamingValidation.RunBatch()
- 命令：D:\gamemaking\2022.3.62f1c1\Editor\Unity.exe -batchmode -nographics -quit -projectPath "D:\gamemaking\My project" -logFile <TEMP>\unity_r20_cs12_rerun.log -executeMethod ChunkStreamingValidation.RunBatch
- 退出码：0；耗时 21s；日志：_phase2b_validation/round20_ChunkStreamingValidation_rerun.log（170354 bytes）；stdout/stderr 各 0 字节
- 入口真实执行证据：目标类标记行 3 条 + PASS 107 条断言真实产出
- 结果：Checks = 107，Failures = 0，Result = PASS；缺失 0；哈希不匹配 0
- CS12：既有文件全部存在（缺失 0）PASS；既有文件 SHA256(前16) 全部不变（比对 40 个；本工程跳过规范 0 个）PASS
- error CS = 0；warning CS = 4（2 条唯一警告×2，既有非新增）
- 判定：**CS12 达到可复现 PASS**

### 级联审计
- ChunkStreamingValidation.cs 自身哈希：55AC4FFF4BFC13C8 → **BEB05E3DD16D48F3**
- 引用它的登记表共 5 处（与第十六轮依赖图「CS 被 5 张表登记」完全一致），全部失效：
  - GeologyStageDValidation.FrozenFiles
  - GeologyRuntimeIntegrationValidation.AuthorizedChangedFiles
  - GeologyStageAValidation.FrozenFiles
  - GeologyStageBValidation.FrozenFiles
  - GeologyStageCValidation.FrozenFiles
- 全量复算（349 条登记）：**14 条不匹配**（R19 基线 10）
  - 本轮修复 1 条
  - 本轮新增级联 5 条（全部为 CS12.cs 旧钉）
  - 仍然存在 9 条（历史遗留：GRI 5 条、StageD 4 条，均为 StageA/B/C/D/RTC 的验证器登记）
- 算式校验：10 − 1 + 5 = 14（实测 14），逆推 14 − 5 + 1 = 10 亦成立
- 依赖图一致性：新增级联 100% 为 `Editor/ChunkStreamingValidation.cs`；无缺失文件；**未发现依赖图之外的额外变化**
- 未修复 StageD / GRI / StageA/B/C / RTC 中任何登记；第十六至十九轮审计文件均未被改动，本轮另建 round20_hash_dependency_audit_delta.md

### 结论与后续
- 本会话内当前可复现 PASS 的验证器已达 5 个：RTC(R15)、StageA(R17)、StageB(R18)、StageC(R19)、CS12(R20)
- 仍待处理：GRI（5 条）、StageD（5 条）、StageA/B/C 各 1 条本轮新增的 CS12 钉
- 注意：下一轮若修复 GRI 或 StageD，会再次使其所钉验证器（StageA/B/C/D、CS12、RTC）登记失效；按第十六轮修复顺序建议，应把「GRI 最后修」
- 4 个 legacy core 验证器仍未在本会话真实运行，状态未知，不得标记 PASS
- 本轮仅为 CS12 验证器及其冻结登记，**不构成整个地质系统的端到端验证**

### 尚未执行
- GRI / StageD / StageA/B/C 的登记同步与复跑
- 4 个 legacy core 验证器真实运行
- Elevation 等价验证复跑
- WPS56 / A1 Ocean 文档同步
- GE7 Runtime Core(12) 文本专项
- 最终全量 git diff 审计
---

## 第二十一轮记录：Stage A 的 CS12 级联修复

### 保护现场
- branch=main / HEAD=37246478ed354a1e9a0ce7a3a3d9457caf188062；未 commit / push / reset / clean
- 第十六至二十轮审计文件与进度记录全部存在并读取（round16 20350B / round17 6100B / round18 5041B / round19 4597B / round20 4872B / progress 64050B）
- 未发现无法解释的现场变化

### 全量重测（未沿用旧依赖图推测）
- StageA FrozenFiles **51 条全部实测**：不匹配 **1** 条、缺失 0 条；算法标准实现 1 处；路径规则（规范键→工程根）未变
- 唯一失配：`Editor/ChunkStreamingValidation.cs` pin `55AC4FFF4BFC13C8` act `BEB05E3DD16D48F3`
- **与实际值第二十轮记录 BEB05E3DD16D48F3 完全一致**（脚本实测比对 = True），无第二处失配
- 级联影响面以实际登记表查询确认（非依赖图推测）：4 张表登记 StageA —— StageB.FrozenFiles、StageC.AuthorizedChangedFiles、StageD.FrozenFiles、GRI.AuthorizedChangedFiles

### 最小修复
- 仅替换 1 条：`Editor/ChunkStreamingValidation.cs` `55AC4FFF4BFC13C8` → `BEB05E3DD16D48F3`
- 结构核对：1088 行（不变）/ FrozenFiles 51 条（不变）/ Check( 83（不变）；旧值残留 0 / 新值计数 1；修复后 51 条复算不匹配 0
- 未改生产代码、断言逻辑/Check 调用、冻结集合、路径规则、哈希算法；未动 StageB/C/D/GRI/CS12

### 真实运行
- 入口：GeologyStageAValidation.RunBatch()
- 命令：D:\gamemaking\2022.3.62f1c1\Editor\Unity.exe -batchmode -nographics -quit -projectPath "D:\gamemaking\My project" -logFile <TEMP>\unity_r21_stageA_rerun.log -executeMethod GeologyStageAValidation.RunBatch
- 退出码：0；耗时 21s；日志：_phase2b_validation/round21_GeologyStageAValidation_rerun.log（170360 bytes）；stdout/stderr 各 0 字节
- 入口真实执行证据：目标类标记行 6 条 + PASS 105 条断言真实产出
- 结果：Checks = 105，Failures = 0，Result = PASS；缺失 0；哈希不匹配 0
- GSA6：既有文件全部存在（缺失 0）PASS；SHA256(前16) 全部不变（比对 51 个；跳过 0）PASS
- error CS = 0；warning CS = 4（2 条唯一警告×2，既有非新增）
- 判定：**Stage A 恢复可复现 PASS**

### 级联审计
- GeologyStageAValidation.cs 哈希：83731D5FB896BA38 → **67DB0CD6161EE4F4**
- 引用 StageA 的 4 张表全部失配，分类如下：
  - 本轮新建失配 2 条（此前匹配）：GeologyStageBValidation.FrozenFiles、GeologyStageCValidation.AuthorizedChangedFiles
  - 值变更但仍失配 2 条（历史失配延续）：GeologyStageDValidation.FrozenFiles、GRI.AuthorizedChangedFiles（pin 仍为 DF42F8605BA60CAF，act 由 83731D5FB896BA38 更新为 67DB0CD6161EE4F4）
- 本轮修复 1 条：ChunkStreamingValidation.FrozenFiles ← `Editor/ChunkStreamingValidation.cs`
- 全量复算（349 条）：**15 条不匹配**（R20 基线 14）
  - 算式：14 − 1（修复） + 2（新建失配） = 15（实测 15）；逆推 15 − 2 + 1 = 14（与 R20 记录一致）
  - 值变更类 2 条不改变计数
- 当前 15 条分布：GRI 6 条（Auth 5 + Frozen 1）/ StageB 2 条 / StageC 2 条 / StageD 5 条
- 无缺失文件；**未发现依赖图之外的额外变化**；未修复 StageB/C/D/GRI 任何登记
- 第十六至二十轮审计文件均未被改动，本轮另建 round21_hash_dependency_audit_delta.md

### 结论与后续
- 本会话内当前可复现 PASS 的验证器仍为 5 个（RTC、StageA、StageB、StageC、CS12），但 StageB/C 的登记表本轮新增失配，**其 PASS 需在各自轮次重钉后重新验证**
- 待处理：StageB 2 条、StageC 2 条、StageD 5 条、GRI 6 条
- 结构提示：StageB/C/D/GRI 互相钉定，任何一次验证器编辑都会再次产生级联；按第十六轮建议 **GRI 应最后修**
- 4 个 legacy core 验证器仍未在本会话真实运行，状态未知，不得标记 PASS
- 本轮仅恢复 Stage A 一致性，**不代表整个地质系统完成端到端验证**

### 尚未执行
- StageB / StageC / StageD / GRI 的登记同步与复跑
- 4 个 legacy core 验证器真实运行
- Elevation 等价验证复跑
- WPS56 / A1 Ocean 文档同步
- GE7 Runtime Core(12) 文本专项
- 最终全量 git diff 审计
---

## 第二十二轮记录：GeologyStageBValidation 两条冻结哈希最小修复

### 保护现场
- branch=main / HEAD=37246478ed354a1e9a0ce7a3a3d9457caf188062；未 commit / push / reset / clean
- 第十六至二十一轮审计文件与进度记录全部存在并读取（round16 20350B / round17 6100B / round18 5041B / round19 4597B / round20 4872B / round21 4585B / progress 68331B）
- 未发现无法解释的现场变化

### 两条差异的复核（本轮重新实测，未照抄基线）
| 键 | 登记值 | 实测值 | 独立证据 |
|---|---|---|---|
| Editor/ChunkStreamingValidation.cs | 55AC4FFF4BFC13C8 | BEB05E3DD16D48F3 | 第二十轮 CS12 真实运行通过（exit0/107 PASS，比对 40 个全匹配）；本轮实测该文件哈希与之一致（True） |
| Editor/GeologyStageAValidation.cs | 83731D5FB896BA38 | 67DB0CD6161EE4F4 | 第二十一轮 StageA 真实运行通过（exit0/105 PASS，比对 51 个全匹配）；本轮实测该文件哈希与之一致（True） |
- StageB 全量重测 **55 条**：不匹配恰 **2** 条、缺失 0，与第二十一轮基线**逐字段一致**（脚本比对 True）
- 算法标准实现 1 处；路径规则（规范键→工程根）未变

### 最小修复
- 仅替换 2 条登记值（值替换，行数不变）：ChunkStreamingValidation.cs 钉、GeologyStageAValidation.cs 钉
- 结构核对：1167 行（不变）/ FrozenFiles 55 条（不变）/ 该验证器**无 AuthorizedChangedFiles 表** / Check( 59（不变）
- 修复后 55 条复算不匹配 0；旧值残留 55AC4FFF4BFC13C8=0、83731D5FB896BA38=0；新值计数各 1
- 说明：因是值替换，该文件相对 HEAD 的累计 numstat 仍为 +10/−10（含第十八轮的 10 条），本轮增量为上述 2 行
- 未改生产代码、断言逻辑/Check 调用、冻结集合、路径规则、哈希算法；未动 StageC/D/GRI 或其它验证器

### 真实运行 Stage B
- 入口：GeologyStageBValidation.RunBatch()
- 命令：D:\gamemaking\2022.3.62f1c1\Editor\Unity.exe -batchmode -nographics -quit -projectPath "D:\gamemaking\My project" -logFile <TEMP>\unity_r22_stageB_rerun.log -executeMethod GeologyStageBValidation.RunBatch
- 退出码：0；耗时 24s；日志：_phase2b_validation/round22_GeologyStageBValidation_rerun.log（182517 bytes）；stdout/stderr 各 0 字节
- 入口真实执行证据：目标类标记行 4 条 + PASS 122 条断言真实产出
- 结果：Checks = 122，Failures = 0，Result = PASS；缺失 0；哈希不匹配 0
- GB7：既有文件全部存在（缺失 0）PASS；SHA256(前16) 全部不变（比对 55 个；跳过 0）PASS
- error CS = 0；warning CS = 4（2 条唯一警告×2，既有非新增）
- 判定：**Stage B 本轮可复现 PASS**

### 级联审计
- GeologyStageBValidation.cs 哈希：B5EE9A249FCF5AE3 → **58DA2160E30ABA72**
- 引用 StageB 的 3 张表（实测查询）：GRI.AuthorizedChangedFiles、StageC.AuthorizedChangedFiles、StageD.FrozenFiles
  - 本轮新建失配 1 条：GeologyStageCValidation.AuthorizedChangedFiles（此前登记 B5EE9A249FCF5AE3 匹配）
  - 值变更仍失配 2 条：GRI.AuthorizedChangedFiles、StageD.FrozenFiles（pin 仍为历史旧钉 CFA08E4EE8D49C99，act 更新为 58DA2160E30ABA72）
- 本轮修复 2 条（StageB.FrozenFiles 内 CS12 与 StageA 登记）
- 全量复算（349 条）：**14 条不匹配**（R21 基线 15）
  - 算式：15 − 2（修复） + 1（新建） = 14（实测 14）；逆推 14 − 1 + 2 = 15（与 R21 记录一致）
  - 值变更类 2 条不改变计数
- 当前 14 条分布：GRI 6 / StageC 3 / StageD 5；StageA、StageB、CS12、RTC 当前自身登记表均为 0 失配
- 无缺失文件；**未发现依赖图之外的额外变化**；未修复 StageC / StageD / GRI 任何登记
- 第十六至二十一轮审计文件均未被改动，本轮另建 round22_hash_dependency_audit_delta.md

### 结论与后续
- Stage B 恢复可复现 PASS；本会话内当前 PASS 的验证器：RTC、StageA、StageB、CS12（StageC 因其登记表新增 1 条失配，其此前 PASS 需在下一轮重钉后重新验证）
- 待处理：StageC 3 条、StageD 5 条、GRI 6 条
- 结构提示：StageC/D/GRI 互钉，修复会再产生级联；按第十六轮建议 **GRI 应最后修**
- 4 个 legacy core 验证器仍未在本会话真实运行，状态未知，不得标记 PASS
- 本轮仅为 Stage B 验证器及其冻结登记，**不构成端到端验证**

### 尚未执行
- StageC / StageD / GRI 的登记同步与复跑
- 4 个 legacy core 验证器真实运行
- Elevation 等价验证复跑
- WPS56 / A1 Ocean 文档同步
- GE7 Runtime Core(12) 文本专项
- 最终全量 git diff 审计
---

## 第二十三轮记录：GeologyStageCValidation 三条冻结哈希最小修复

### 保护现场
- branch=main / HEAD=37246478ed354a1e9a0ce7a3a3d9457caf188062；未 commit / push / reset / clean
- 第十六至二十二轮审计文件与进度记录全部存在并读取（round16 20350B / round17 6100B / round18 5041B / round19 4597B / round20 4872B / round21 4585B / round22 5277B / progress 73009B）
- 未发现无法解释的现场变化

### 三条差异的重新实测与审查
| 表 | 键 | 登记值 | 实测值 | 依据 |
|---|---|---|---|---|
| FrozenFiles | Editor/ChunkStreamingValidation.cs | 55AC4FFF4BFC13C8 | BEB05E3DD16D48F3 | CS12 第二十轮真实运行通过（exit0/107 PASS，比对 40 全匹配）；本轮实测文件哈希 = 同值 |
| AuthorizedChangedFiles | Editor/GeologyStageAValidation.cs | 83731D5FB896BA38 | 67DB0CD6161EE4F4 | StageA 第二十一轮真实运行通过（exit0/105 PASS，比对 51 全匹配）；本轮实测 = 同值 |
| AuthorizedChangedFiles | Editor/GeologyStageBValidation.cs | B5EE9A249FCF5AE3 | 58DA2160E30ABA72 | StageB 第二十二轮真实运行通过（exit0/122 PASS，比对 55 全匹配）；本轮实测 = 同值 |
- StageC 全量重测 **57 条**（FrozenFiles 55 + AuthorizedChangedFiles 2）：不匹配恰 **3** 条、缺失 0，与第二十二轮基线**逐字段一致**（脚本比对 True）
- 算法标准实现 1 处；路径规则（VerifyOne 规范键分支）未变

### 最小修复
- 仅替换 3 条登记值（值替换，行数不变）：CS12 钉、StageA 钉、StageB 钉
- 结构核对：1209 行（不变）/ FrozenFiles 55 条（不变）/ AuthorizedChangedFiles 2 条（不变）/ Check( 55（不变）
- 修复后 57 条复算不匹配 0；三个旧值残留均为 0；三个新值计数各 1
- 说明：值替换，故相对 HEAD 累计 numstat 仍为 +11/−11（含第十九轮 11 条），本轮增量为上述 3 行
- 未改生产代码、断言逻辑/Check 调用、冻结集合、路径规则、哈希算法；未动 StageD/GRI/其它验证器

### 真实运行 Stage C
- 入口：GeologyStageCValidation.RunBatch()
- 命令：D:\gamemaking\2022.3.62f1c1\Editor\Unity.exe -batchmode -nographics -quit -projectPath "D:\gamemaking\My project" -logFile <TEMP>\unity_r23_stageC_rerun.log -executeMethod GeologyStageCValidation.RunBatch
- 退出码：0；耗时 22s；日志：_phase2b_validation/round23_GeologyStageCValidation_rerun.log（121255 bytes）；stdout/stderr 各 0 字节
- 入口真实执行证据：目标类标记行 5 条 + PASS 49 条断言真实产出
- 结果：Checks = 49，Failures = 0，Result = PASS；缺失 0；哈希不匹配 0
- GC8：既有文件全部存在（缺失 0）PASS；既有文件 SHA256(前16) 与登记一致（比对 57 个；其中 2 个已授权修改；跳过 0）PASS
- error CS = 0；warning CS = 4（2 条唯一警告×2，既有非新增）
- 判定：**Stage C 本轮可复现 PASS**

### 级联审计
- GeologyStageCValidation.cs 哈希：F2FED5F4A6D68117 → **F52E2D1A4B515581**
- 引用 StageC 的登记表实测 2 处（StageD.FrozenFiles、GRI.AuthorizedChangedFiles），**此前即已失配**（pin 仍为历史旧钉 4379D1DD622EB737）
  - 本轮**新建失配 = 0 条**（无表曾登记 StageC 的中间哈希）
  - 值变更但仍失配 = 2 条（act 由 F2FED5F4A6D68117 更新为 F52E2D1A4B515581）
- 本轮修复 3 条（StageC 自身两表）
- 全量复算（349 条）：**11 条不匹配**（R22 基线 14）
  - 算式：14 − 3（修复） + 0（新建） = 11（实测 11）；逆推 11 − 0 + 3 = 14（与 R22 记录一致）
- 当前 11 条分布：GRI 6 / StageD 5；StageA、StageB、StageC、CS12、RTC 当前自身登记表均 0 失配
- 缺失文件 0；**未发现依赖图之外的额外变化**；未修复 StageD / GRI 任何登记
- 第十六至二十二轮审计文件均未被改动，本轮另建 round23_hash_dependency_audit_delta.md

### 结论与后续
- Stage C 恢复可复现 PASS；本会话内当前 PASS 的验证器：RTC、StageA、StageB、StageC、CS12（五个）
- 剩余仅 **StageD 5 条 + GRI 6 条**；按依赖顺序应为 **先 StageD、最后 GRI**（修 StageD 会再改变 GRI 中 StageD 的实测值）
- 4 个 legacy core 验证器仍未在本会话真实运行，状态未知，不得标记 PASS
- 本轮仅修复 Stage C 的哈希登记并验证该验证器，**不代表整个地质系统已完成端到端验证**

### 尚未执行
- StageD / GRI 的登记同步与复跑
- 4 个 legacy core 验证器真实运行
- Elevation 等价验证复跑
- WPS56 / A1 Ocean 文档同步
- GE7 Runtime Core(12) 文本专项
- 最终全量 git diff 审计
---

## 第二十四轮记录：GeologyStageDValidation 冻结哈希最小修复

### 保护现场
- branch=main / HEAD=37246478ed354a1e9a0ce7a3a3d9457caf188062；未 commit / push / reset / clean
- 第十六至二十三轮的 9 份审计文件与进度记录全部存在并读取
- 未发现与报告不符的现场状态；**未修改 GRI**

### 五个外部核对值（重新计算，未假定有效）
- ChunkStreamingValidation = BEB05E3DD16D48F3（预期同）True
- GeologyStageAValidation = 67DB0CD6161EE4F4（预期同）True
- GeologyStageBValidation = 58DA2160E30ABA72（预期同）True
- GeologyStageCValidation = F52E2D1A4B515581（预期同）True
- VoxelWorldRuntimeCoreValidation = C5ABCECD2C338BED（预期同）True

### 修改前 5 条失配（StageD.FrozenFiles，全部经实测确认）
| 键 | 旧值 | 实测值 | 依据 |
|---|---|---|---|
| Editor/ChunkStreamingValidation.cs | 55AC4FFF4BFC13C8 | BEB05E3DD16D48F3 | CS12 第二十轮 107/0 PASS |
| Editor/GeologyStageAValidation.cs | DF42F8605BA60CAF | 67DB0CD6161EE4F4 | StageA 第二十一轮 105/0 PASS |
| Editor/GeologyStageBValidation.cs | CFA08E4EE8D49C99 | 58DA2160E30ABA72 | StageB 第二十二轮 122/0 PASS |
| Editor/GeologyStageCValidation.cs | 4379D1DD622EB737 | F52E2D1A4B515581 | StageC 第二十三轮 49/0 PASS |
| Editor/VoxelWorldRuntimeCoreValidation.cs | 0E0B5C160FEAB236 | C5ABCECD2C338BED | RTC 第十五轮 196/0 PASS |
- StageD 全量重测 63 条：不匹配恰 5、缺失 0、「未匹配 R23 基线的失配数 = 0」
- 算法标准实现 1 处；路径规则（规范键分支）未变

### 最小修复
- 仅替换 5 条登记值（值替换，行数不变）
- 结构核对：1254 行（不变）/ FrozenFiles 63 条（不变）/ 该验证器无 AuthorizedChangedFiles 表 / Check( 52（不变）
- 修复后 63 条复算不匹配 0；5 个旧值残留均为 0；5 个新值计数各 1
- 相对 HEAD 累计 numstat = +19/−13（含第 13 轮注释与登记变更），本轮增量为上述 5 行
- 未改生产代码、断言/Check 调用、条目数量与顺序、路径规则、哈希算法、异常与跳过策略；未动 GRI

### Stage D 真实 Unity 运行
- 入口：GeologyStageDValidation.RunBatch()
- 命令：D:\gamemaking\2022.3.62f1c1\Editor\Unity.exe -batchmode -nographics -quit -projectPath "D:\gamemaking\My project" -logFile <TEMP>\unity_r24_stageD_rerun.log -executeMethod GeologyStageDValidation.RunBatch
- 退出码：0；执行时长 30s；日志：_phase2b_validation/round24_GeologyStageDValidation_rerun.log（117265 bytes）；stdout/stderr 各 0 字节
- 入口真实执行证据：目标类标记行 5 条 + PASS 44 条断言真实产出
- 结果：Checks = 44，Failures = 0，Result = PASS；缺失 0；哈希不匹配 0
- GW10：既有文件全部存在（缺失 0）PASS；SHA256 全部不变（比对 63 个；跳过 0）PASS；10 个验证入口存在 PASS
- error CS = 0；warning CS = 4（2 条唯一警告×2，既有非新增）
- 判定：**Stage D 本轮 PASS**

### 级联与全局复算
- GeologyStageDValidation.cs 哈希：A26D957C4B698097 → **C80477D2430B85AA**
- 引用 StageD 的登记位置实测 1 处：GRI.AuthorizedChangedFiles（pin ABA08564B8C26871，未变）
  - 本轮**新建失配 = 0**（该行此前即失配，仅实测值更新）
- 全局复算（349 条）：**6 条不匹配**（R23 基线 11）
  - 算式：11 − 5（修复） + 0（新建） = 6（实测 6）；逆推 6 − 0 + 5 = 11 通过校验
- 七个验证器失配数：ChunkStreamingValidation 0 / **GRI 6** / StageA 0 / StageB 0 / StageC 0 / StageD 0 / RuntimeCore 0
- 缺失文件 0；未发现依赖图之外的额外变化
- 剩余 6 条**全部位于 GRI**：AuthorizedChangedFiles 5 条（CS12/StageA/StageB/StageC/StageD）+ FrozenFiles 1 条（RTC），留待下一轮最后处理

### 结论
- **Stage D 验证器本轮 PASS**
- **全局冻结哈希尚未全部匹配**：仍有 6 条位于 GRI，需下一轮修复
- 6 个验证器（CS12/StageA/StageB/StageC/StageD/RTC）当前自身登记表均 0 失配
- 4 个 legacy core 验证器仍未在本会话真实运行，状态未知，不得标记 PASS
- **本条不构成地质系统端到端验证完成**

### 尚未执行
- GRI 的 6 条登记同步与复跑（按计划最后一轮）
- 4 个 legacy core 验证器真实运行
- Elevation 等价验证复跑
- WPS56 / A1 Ocean 文档同步
- GE7 Runtime Core(12) 文本专项
- 最终全量 git diff 审计
---

## 第二十五轮记录：GRI 最终冻结哈希修复（全局闭合）

### 保护现场
- branch=main / HEAD=37246478ed354a1e9a0ce7a3a3d9457caf188062；未 commit / push / reset / clean
- R23/R24 审计增量与进度记录全部存在并读取；未发现现场不一致

### 修改前六条失配（逐条重新实测，R24 基线逐字段吻合）
| 表 | 键 | 旧值 | 实测值 | 依据 |
|---|---|---|---|---|
| FrozenFiles | Editor/VoxelWorldRuntimeCoreValidation.cs | 0E0B5C160FEAB236 | C5ABCECD2C338BED | RTC R15 196/0 PASS |
| AuthorizedChangedFiles | Editor/ChunkStreamingValidation.cs | 55AC4FFF4BFC13C8 | BEB05E3DD16D48F3 | CS12 R20 107/0 PASS |
| AuthorizedChangedFiles | Editor/GeologyStageAValidation.cs | DF42F8605BA60CAF | 67DB0CD6161EE4F4 | StageA R21 105/0 PASS |
| AuthorizedChangedFiles | Editor/GeologyStageBValidation.cs | CFA08E4EE8D49C99 | 58DA2160E30ABA72 | StageB R22 122/0 PASS |
| AuthorizedChangedFiles | Editor/GeologyStageCValidation.cs | 4379D1DD622EB737 | F52E2D1A4B515581 | StageC R23 49/0 PASS |
| AuthorizedChangedFiles | Editor/GeologyStageDValidation.cs | ABA08564B8C26871 | C80477D2430B85AA | StageD R24 44/0 PASS |
- 六个外部哈希重新计算后与 R24 报告 6/6 一致；失配**键集合与预期完全一致**（脚本比对 True）；缺失 0

### 最小修复
- 仅替换 6 条登记值（值替换）
- 结构核对：1144 行（不变）/ CriticalFiles 25 / FrozenFiles 34 / AuthorizedChangedFiles 6 = 65（均不变）/ Check( 58（不变）
- 修复后 65 条复算不匹配 0；6 个旧值残留全部为 0
- 相对 HEAD 累计 numstat = +14/−14（含第十二轮登记与注释）；本轮增量 6 行；无非哈希字符串改动
- 未改生产代码、断言/Check 调用、集合结构与条目数量、算法、路径规则、跳过策略

### GRI 真实 Unity 运行
- 入口：GeologyRuntimeIntegrationValidation.RunBatch()（L58）
- 命令：D:\gamemaking\2022.3.62f1c1\Editor\Unity.exe -batchmode -nographics -quit -projectPath "D:\gamemaking\My project" -logFile <TEMP>\unity_r25_gri_rerun.log -executeMethod GeologyRuntimeIntegrationValidation.RunBatch
- 退出码：0；耗时 23s；日志：_phase2b_validation/round25_GeologyRuntimeIntegrationValidation_rerun.log（124094 bytes）；stdout/stderr 各 0 字节
- 入口真实执行证据：目标类标记行 4 条 + PASS 45 条断言真实产出
- 结果：Checks = 45，Failures = 0，Result = PASS
- GE7：关键文件全部存在（缺失 0）PASS；共 25 个文件 SHA 不变 PASS
- GE9：既有文件全部存在（缺失 0）PASS；SHA256 与登记一致（比对 40 个；6 个已授权修改；跳过 0）PASS
- error CS = 0；warning CS = 4，**仅**两条既有唯一警告×2（VoxelCoordinateMigrationStageB3CValidation.cs CS0162 / MiningSurfaceIntegrityValidation.cs CS0414），无新增
- 判定：**GRI 本轮 PASS**

### GRI 最终哈希与引用检查
- GeologyRuntimeIntegrationValidation.cs：0538AA426932DF3E → **0D12877FEACCCD60**
- 全局搜索 Assets/**/*.cs 中 `{ "Editor/GeologyRuntimeIntegrationValidation.cs", "<16hex>" }`：**命中 0 处**
- 结论（有搜索证据，非依赖图推断）：GRI 自身哈希不被任何登记表引用 ⇒ 本轮修复**无新增级联**

### 全局七验证器最终审计
| 验证器 | 登记数 | 失配 | 缺失 |
|---|---:|---:|---:|
| ChunkStreamingValidation | 40 | 0 | 0 |
| GeologyRuntimeIntegrationValidation | 65 | 0 | 0 |
| GeologyStageAValidation | 51 | 0 | 0 |
| GeologyStageBValidation | 55 | 0 | 0 |
| GeologyStageCValidation | 57 | 0 | 0 |
| GeologyStageDValidation | 63 | 0 | 0 |
| VoxelWorldRuntimeCoreValidation | 18 | 0 | 0 |
| **合计** | **349** | **0** | **0** |
- 对照 R24 基线（349/6）：本轮修复 6、新建级联 0 ⇒ 全局失配 6 → **0**
- **冻结哈希登记已全部闭合**

### 结论（分层，不得混淆）
1. **GRI 验证器本轮 PASS**：是
2. **七验证器 349 条哈希登记全部匹配**：是（失配 0、缺失 0）
3. **地质系统端到端验证**：**未完成**。冻结哈希闭合 ≠ 端到端验证。

### 本会话内真实验证状态汇总（仅记录实际运行过的）
| 验证器 | 最近真实运行 | 结果 |
|---|---|---|
| VoxelWorldRuntimeCoreValidation | R15 | exit0 / 196 / 0 PASS |
| ChunkStreamingValidation | R20 | exit0 / 107 / 0 PASS |
| GeologyStageAValidation | R21 | exit0 / 105 / 0 PASS |
| GeologyStageBValidation | R22 | exit0 / 122 / 0 PASS |
| GeologyStageCValidation | R23 | exit0 / 49 / 0 PASS |
| GeologyStageDValidation | R24 | exit0 / 44 / 0 PASS |
| GeologyRuntimeIntegrationValidation | R25 | exit0 / 45 / 0 PASS |
| VoxelWorldCoreValidation / WorldGenerationCoreValidation / VoxelMeshCoreValidation / VoxelWorldViewValidation | **从未运行** | 状态未知，不得标记 PASS |

### 仍未完成（如实记录）
- 4 个 legacy core 验证器的真实运行
- Elevation 等价验证复跑
- WorldParameter_Source_Contract.md 的 WPS56 状态与 A1 Ocean 配置来源同步
- GE7 说明文本「Runtime Core(12)」与实际 11 条目不一致的专项修正
- 最终全量 git diff 审计（当前 16 tracked M + 4 untracked + 3 证据目录）
- 全部改动均未 commit / push