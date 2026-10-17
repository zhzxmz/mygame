using UnityEngine;

/// <summary>
/// Mining V1 compatibility path。
///
/// 依据最新 Terrain Modification &amp; Mining Specification V2.0：
/// 旧的 Shape-based V2 Mining API 已移除。
/// V2 ExcavateCell Mining API 将在后续 Step 实现。
///
/// 当前保留：
///   MineCube(...) → V1 TerrainModification，直接修改 DensityField。
/// 该 V1 路径继续用于现有 PlayerMiningController 与旧 Validation。
/// </summary>
public static class TerrainMining
{
    /// <summary>
    /// V1 compatibility 常量。
    /// 真正的定义与语义归属 V1 TerrainModification。
    /// </summary>
    public const float AirDensity = TerrainModification.AirDensity;

    /// <summary>
    /// V1 compatibility path。
    /// 在 Terrain Local Space 中执行一次 Cube Mining，直接修改 DensityField。
    /// 返回被修改的 Density sample 数量。
    /// </summary>
    public static int MineCube(
        DensityTerrain terrain,
        Vector3 localCenter,
        Vector3 size,
        bool rebuild = true)
    {
        TerrainModificationRequest request = new TerrainModificationRequest(
            new CubeModificationShape(size),
            localCenter,
            TerrainModificationOperation.Subtract,
            rebuild
        );

        TerrainModificationResult result = TerrainModification.Apply(
            terrain,
            request
        );

        return result.ModifiedSampleCount;
    }
}
