# B 阶段报告：Mining 卡顿后 Camera 大幅跳转修复

- 工程：`D:\gamemaking\My project`（Unity 2022.3.62f1c1）
- 范围：**只处理 Player 输入与 Camera 旋转稳定性**（不触碰 Terrain Mining 核心、VoxelField、DensityField、MaterialField、FaceId、Mesh 生成、Visibility、Weld、Collider 优化、Chunk 生命周期）
- **Editor Play Mode 人机验证：未执行**（本报告结论全部来自批处理断言 + 源码静态守卫）
- 证据：`_phase2b_validation/b_phase_git_diff.txt`、`b_phase_look1.log`、`regression_summary.txt`、本文件

---

## 1. 结论

调用链（现状）：`PlayerInputController.Update → Input.GetAxis("Mouse X/Y") → CameraController.RotateCamera() → 修改旋转`。

两个成因：

1. **单位错误 + 卡顿放大**：`Input.GetAxis("Mouse X/Y")` 返回的已经是“本帧累计位移增量”，旧代码又乘 `Time.deltaTime`：
   ```csharp
   float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity * Time.deltaTime; // 旧实现
   ```
   - 卡顿帧的 `dt` 很大，而该帧的鼠标增量又累计了整段卡顿期间的位移 ⇒ **位移 × 大 dt**，一次性转出极大角度；
   - 同一份鼠标位移在低帧率下转出更大角度（每像素转出角度 ∝ 1/帧率：30fps 是 60fps 的 2 倍），灵敏度随帧率漂移。
2. **没有任何单帧预算**：`CameraController.RotateCamera` 把传入角度直接累加/应用，没有上限 ⇒ 恢复帧的累计输入会一次性全部落地。

修复：

1. `PlayerInputController`：改用 **`Input.GetAxisRaw("Mouse X/Y")`**（与剑的鼠标路径同一来源）→ `÷ MouseAxisSensitivity(0.1)` 得近似像素位移 → `× lookDegreesPerPixel`（默认 `1.3333`）得角度，**不再乘 `Time.deltaTime`**；换算逻辑抽成可测试的 `ApplyLookDelta(Vector2)`。
2. `CameraController`：新增 `maxLookDegreesPerCall`（默认 **40°**，`≤ 0` 关闭）——单次调用的 yaw / pitch 各自截断，**超出部分直接丢弃（不回放）**；俯仰 ±90° 限制保留。

效果（同一次卡顿累计输入，250 像素）：**旧实现 3200° → 新实现 ≤ 40°**；正常小位移（1 像素 ⇒ 1.3333°）不受上限影响。

---

## 2. 范围约束核对（本轮零改动清单）

| 约束 | 状态 | 证据 |
|------|------|------|
| Terrain Mining 核心逻辑 | 未改 | 本轮 `git diff` 只新增 `Assets/Scripts/Player/*` 两个文件的改动 |
| VoxelField / DensityField / MaterialField | 未改 | 同上 |
| FaceId / Mesh 生成 / Visibility | 未改 | 同上 |
| Weld / Collider 性能优化 / Chunk 生命周期 | 未改 | 同上 |
| InputManager 资产（Mouse X/Y Sensitivity = 0.1） | 未改 | 仍按 0.1 换算，未改 ProjectSettings |
| `CameraController.mouseSensitivity`（公有、场景序列化 = 200） | 保留不动 | 历史字段，避免改动场景序列化；已在注释标注“当前不参与计算” |
| `WeaponController`（剑的鼠标控制） | 未改 | 它的鼠标路径本来就是 `GetAxisRaw ÷ 0.1`（像素增量、不乘 dt），本轮以它为准对齐相机路径 |
| `Time.deltaTime` 的其它合法用法（如剑的回位速率） | 未改 | 只去掉“乘进鼠标增量”的那一处 |

---

## 3. 修改文件（3 个）

| # | 文件 | 跟踪状态 | 改动 | SHA256 |
|---|------|----------|------|--------|
| 1 | `Assets/Scripts/Player/PlayerInputController.cs` | tracked-modified | 删除 `mouseSensitivity(800) × Time.deltaTime`；改用 `GetAxisRaw` 增量 ÷ 0.1 × `lookDegreesPerPixel`（默认 1.3333）；新增 `ApplyLookDelta(Vector2)` | `C866CAAF562FE0A5C6FA6D8E5E33222BDA27CF3F1DBF9162C595F33F7D7311A3` |
| 2 | `Assets/Scripts/Player/CameraController.cs` | tracked-modified | 新增 `maxLookDegreesPerCall`（默认 40°，≤0 关闭）与单次预算截断；俯仰 ±90 保留 | `277701427F691D852F98D10E86E1BA012A2D6536F989F13A250CA293E00DFABF` |
| 3 | `Assets/Editor/PlayerLookStabilityValidation.cs` | untracked（新增验证套件） | 20 条断言（见第 5 节） | `A91F60ABDE33FE17CEAB3E8E2383851DBAEC2F28FF5BE57664649FD5F861479C` |

附：`_phase2b_validation/run_regression.ps1`（未跟踪的测试脚手架）注册了新套件 `PlayerLook`，使全量回归覆盖本轮修复。

---

## 4. 手感等价性与行为变化

- **60fps 等价**：旧 `axis × 800 × (1/60) = axis × 13.333`；新 `axis × 10 × 1.3333 = axis × 13.333` ⇒ 默认值下 60fps 的行为与旧实现一致（`1.3333 = 800 × 0.1 ÷ 60` 已写进字段 Tooltip）。
- **帧率无关**：新实现不再含任何时间因子 ⇒ 同一份鼠标位移在任何帧率下转出相同角度；旧实现每像素角度 ∝ 1/帧率。
- **卡顿有界**：单次调用 ≤ `maxLookDegreesPerCall`（默认 40°），超出部分丢弃。
- **行为变化（需 Play Mode 复核）**：相机由 `GetAxis`（平滑）改为 `GetAxisRaw`（原始增量），与剑的鼠标路径一致，可消除“鼠标停止后仍平滑漂移”，但手感会更“直接”。

---

## 5. 新增验证套件（20 条断言，全 PASS）

`PlayerLookStabilityValidation`（`Assets/Editor/PlayerLookStabilityValidation.cs`，`-executeMethod …RunBatch`，日志 `_phase2b_validation/b_phase_look1.log`）：

| 用例 | 断言要点 |
|------|----------|
| 默认值契约 | `lookDegreesPerPixel = 1.3333`、`maxLookDegreesPerCall = 40`、`ApplyLookDelta(Vector2)` 存在且**只接受 1 个参数**（结构性帧时间无关） |
| 正常位移换算 | 1 像素 ⇒ 1.3333°；5 像素 ⇒ 6.6665°；`lookDegreesPerPixel = 2` 时 1 像素 ⇒ 2.0000°（灵敏度是唯一缩放来源） |
| 静态守卫 | 两个 Player 脚本都不含 `× Time.deltaTime/unscaledDeltaTime/smoothDeltaTime/fixedDeltaTime`；输入用 `GetAxisRaw("Mouse X")`（防止旧 bug 回归） |
| **卡顿累计输入（核心验收）** | 模拟 160ms 卡顿期间累计 250 像素：单帧 yaw 仅转 **40.0000°**（≤ 上限 40°）；旧公式会一次转出 **3200°**；pitch 同样 40.0000°；反向输入 −40.0000° |
| 俯仰限制 | 连续上推 ⇒ `xRotation = −90`；连续下推 ⇒ `xRotation = +90`；相机 `localRotation` 与 `xRotation` 同步 |
| 上限可配置 | `= 0`（关闭）时 3 像素完整转 3.9999°；`= 1°` 时同样位移被截断为 1.0000°；默认上限不干扰正常小位移 |
| 空引用安全 | `cameraController == null` 时 `ApplyLookDelta` 不抛异常；`player == null` 时 `RotateCamera` 不修改任何旋转状态 |

关键输出（原文）：

```
PASS - 卡顿累计输入被截断：本帧 yaw 仅转 40.0000°（≤ 上限 40°，未经修复时会一次转出 3200°）
PASS - 正常位移：1 像素 ⇒ 1.3333°（期望 1.3333°）；若仍乘 Time.deltaTime 结果会随帧耗时变化
PASS - CameraController 的旋转没有乘任何 Time 因子（源码静态守卫）
Checks = 20   Failures = 0   Result = PASS
```

---

## 6. 全量回归（33 套件）

见文末「回归判定」一节（执行中 → 完成后回填）。

---

## 7. 具体失败用例

- 本轮新增套件**首次运行即 20/20 通过**，修复过程中没有需要处理的失败断言。
- 供追溯（上一阶段遗留、本轮保持通过）：A 阶段首轮全量回归曾出现 `MeshIntegration` 1 条、`VoxelMining` 14 条失败（反射注入选择缺少命中点/法线 + 空壳地形无材料证据），已在上一轮修正，本轮回归全通过。
- 若本轮全量回归出现任何失败，将在此列出套件名与逐条断言（见文末「回归判定」）。

---

## 8. 尚未验证 / 明确未做

- **Editor Play Mode 未验证**（本轮最重要的未验证项）：
  * 真实鼠标手感（`GetAxisRaw` 去掉平滑后是否偏“硬”）；
  * 真实挖掘卡顿（诊断轮实测约 160 ms/次）下的实际跳转是否消失；
  * `maxLookDegreesPerCall = 40°` 在快速转身时是否会体感“顶到上限”（若需要，Play Mode 里直接调该字段或 `lookDegreesPerPixel` 即可，两者都已暴露在 Inspector）。
- 未做（明确不在本轮范围）：卡顿本身的性能优化（Weld 占 Assemble 大头、Collider 重建分帧）、Mining 逻辑、相机 FOV/位置、剑（WeaponController）控制策略。
- 未改 `ProjectSettings/InputManager.asset`（Mouse X/Y Sensitivity 仍为 0.1）。
- 未执行 `git add` / `commit` / `push`。

---

## 9. 完整 diff 与未跟踪清单

证据文件：`_phase2b_validation/b_phase_git_diff.txt`（约 550 KB）：

1. `git status --porcelain`（跟踪修改 **17** 个 = 既有 15 + 本轮 `CameraController.cs` / `PlayerInputController.cs`）；
2. `git diff --stat`；
3. 空白校验：`git diff --check` **exit=0**（空白类发现 0）、`git diff --cached --check` **exit=0**（暂存 0）；
4. 本轮 4 个改动/脚手架文件的跟踪状态 + SHA256；
5. **完整 `git diff`（跟踪文件，含两个 Player 脚本的全部改动）**；
6. 主工程 ↔ 验证工程 `.cs` 同步校验：**161 对，0 不匹配，0 缺失**；
7. 未跟踪聚合：**1103** 个（`Assets/` 85 = 40 `.cs` + 45 `.meta`；`_b3b_diff/` 2、`_finalization_backup/` 12、`_phase2b_validation/` 1004）；
8. **附录 A**：新增的未跟踪验证套件 `PlayerLookStabilityValidation.cs` 全文。

---

## 回归判定

**结论：全量回归 33/33 PASS —— 1661 条检查、0 失败、0 编译错误、全部 exit=0**（`_phase2b_validation/regression_summary.txt`；上一轮 32 套件 1641 条，本轮新增 `PlayerLook` 套件 20 条 ⇒ 1661 条）。

| 套件 | exit | checks | failedChecks | csErrors | 结果 |
|---|---|---|---|---|---|
| ProtoAB | 0 | 55 | 0 | 0 | PASS |
| Phase1 | 0 | 25 | 0 | 0 | PASS |
| Phase2A | 0 | 32 | 0 | 0 | PASS |
| Phase2B | 0 | 77 | 0 | 0 | PASS |
| Phase3 | 0 | 108 | 0 | 0 | PASS |
| SchemeA | 0 | 85 | 0 | 0 | PASS |
| StageB1 | 0 | 51 | 0 | 0 | PASS |
| StageB2 | 0 | 317 | 0 | 0 | PASS |
| StageB3A | 0 | 52 | 0 | 0 | PASS |
| StageB3B | 0 | 106 | 0 | 0 | PASS |
| StageB3C | 0 | 264 | 0 | 0 | PASS |
| MeshIntegration | 0 | 53 | 0 | 0 | PASS |
| VoxelMining | 0 | 41 | 0 | 0 | PASS |
| VoxelHighlight | 0 | 36 | 0 | 0 | PASS |
| VoxelSelector | 0 | 43 | 0 | 0 | PASS |
| VoxelDensityAdapter | 0 | 22 | 0 | 0 | PASS |
| VoxelMarchingCubes | 0 | 29 | 0 | 0 | PASS |
| VoxelFieldOwnership | 0 | 14 | 0 | 0 | PASS |
| MultiChunk | 0 | 0 | 0 | 0 | PASS |
| MiningV1 | 0 | 0 | 0 | 0 | PASS |
| ChunkLifecycle | 0 | 0 | 0 | 0 | PASS |
| V2_ChunkIntegration | 0 | 5 | 0 | 0 | PASS |
| V2_EditGrid | 0 | 40 | 0 | 0 | PASS |
| V2_EditLayer | 0 | 24 | 0 | 0 | PASS |
| V2_EffectiveDensity | 0 | 26 | 0 | 0 | PASS |
| V2_EffectiveField | 0 | 2 | 0 | 0 | PASS |
| V2_SurfaceSampleEnumerator | 0 | 22 | 0 | 0 | PASS |
| V2_SurfaceSampling | 0 | 23 | 0 | 0 | PASS |
| V2_TerrainQuery | 0 | 21 | 0 | 0 | PASS |
| V2_Mining | 0 | 10 | 0 | 0 | PASS |
| V2_Main | 0 | 16 | 0 | 0 | PASS |
| V2_VoxelField | 0 | 42 | 0 | 0 | PASS |
| PlayerLook | 0 | 20 | 0 | 0 | PASS |
| **合计（33 套件）** | **0** | **1661** | **0** | **0** | **33/33 PASS** |

本轮唯一新增套件：`PlayerLook`（20 条，见第 5 节）；其余 32 套件的检查数与上一轮完全一致，全部 PASS ⇒ 本轮 Player 输入/相机改动没有影响任何既有功能。

**具体失败用例：0 条**（本轮修复过程中新套件首次运行即通过，全量回归无任何失败断言）。
