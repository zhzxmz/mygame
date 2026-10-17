using UnityEditor;
using UnityEngine;

/// <summary>
/// VoxelCore（Phase 1）验证入口：
///   · 运行 <see cref="VoxelWorld.VoxelWorldDebug.RunSelfCheck"/>（纯数据自检）；
///   · 输出逐条 PASS/FAIL 与汇总；
///   · 批处理退出码 = 失败数（0 = 全部通过）。
///
/// 用法：
///   · Editor 菜单：Tools/VoxelWorld/Validate VoxelCore (Phase 1)
///   · 批处理：-executeMethod VoxelWorldCoreValidation.RunBatch
///
/// 本文件只做测试入口，不修改任何 VoxelCore 数据逻辑。
/// </summary>
public static class VoxelWorldCoreValidation
{
    [MenuItem("Tools/VoxelWorld/Validate VoxelCore (Phase 1)")]
    public static void ValidateFromMenu()
    {
        string report;
        bool ok = VoxelWorld.VoxelWorldDebug.RunSelfCheck(out report);

        Debug.Log(report);
        Debug.Log(
            "[VoxelWorldCoreValidation] Menu validation result = " +
            (ok ? "PASS" : "FAIL")
        );
    }

    public static void RunBatch()
    {
        string report;
        bool ok = VoxelWorld.VoxelWorldDebug.RunSelfCheck(out report);

        Debug.Log(report);
        EditorApplication.Exit(ok ? 0 : 1);
    }
}
