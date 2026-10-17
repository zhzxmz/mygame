/// <summary>
/// SpatialFace 调试开关（**仅用于验证**，不参与正常玩法逻辑）。
///
/// ConservativeHiddenFace = true 时：
///   当 coverage evaluation 对某个 Region 的判定是 Mixed / Uncertain
///   （即 <see cref="VoxelAwareMeshSpatialFace"/> 的 BuildCoveragePieces 走到
///   “非 uniform 且未 failed”分支）时，按**保守策略**处理 ——
///   直接为该 Region 生成 HiddenFace，而不是交给单元细分（细分可能把
///   有材料的薄片丢弃，导致缺面）。
///
/// 关闭时（默认 false）逻辑与打补丁前**完全一致**（原分支原样执行）。
/// 打开/关闭方式：
///   · 代码：SpatialFaceDebugSettings.ConservativeHiddenFace = true/false;
///   · Editor 菜单：Tools/Terrain/Debug/Toggle Conservative HiddenFace
///     （由 SpatialFaceConservativeHiddenFaceValidation 提供，便于 Play Mode 验证）
/// </summary>
public static class SpatialFaceDebugSettings
{
    /// <summary>
    /// 保守生成 HiddenFace（Mixed/Uncertain ⇒ HiddenFace）。
    /// 默认 false = 保持既有行为；验证真实地形缺面时置 true。
    /// </summary>
    public static bool ConservativeHiddenFace = false;
}
