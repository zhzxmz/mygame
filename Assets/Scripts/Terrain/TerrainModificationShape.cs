using UnityEngine;

/// <summary>
/// Terrain Modification Shape 抽象。
/// Shape 定义在连续 Terrain Local Space 中；修改 Position 由
/// TerrainModificationRequest.Center 单独提供，Shape 不拥有 Request。
///
/// 每个 Shape 通过 EvaluateSignedValue 提供连续的 inside / boundary / outside 语义：
///   s(p) < 0   Shape 内部
///   s(p) = 0   Shape 边界
///   s(p) > 0   Shape 外部
/// 该 Shape Signed Value 不是 Terrain 的 SDF，也不改变 Terrain Density 的单位或语义。
/// V1 只实现 Cube；未来 Sphere / Capsule 等通过同一接口扩展。
/// </summary>
public interface ITerrainModificationShape
{
    /// <summary>该 Shape 的参数是否有效（例如 Size 必须全部为正）。</summary>
    bool IsValid { get; }

    /// <summary>给定修改 Position 后，Shape 在 Terrain Local Space 中的 AABB。</summary>
    Bounds GetLocalBounds(Vector3 position);

    /// <summary>
    /// 计算一个 Terrain Local Space 采样点的连续 Shape Signed Value。
    /// 内部为负、边界为 0、外部为正。
    /// </summary>
    float EvaluateSignedValue(Vector3 position, Vector3 samplePosition);

    /// <summary>
    /// 采样点是否位于 Shape 内部或边界（Signed Value &lt;= 0）。
    /// 仅作为 EvaluateSignedValue 的便捷封装。
    /// </summary>
    bool Contains(Vector3 position, Vector3 samplePosition);
}

/// <summary>
/// V1 Modification Shape：Axis-Aligned Cube。
/// Center 由 TerrainModificationRequest 提供；Size 是完整尺寸（Center ± Size/2）。
/// Cube 是 Modification Shape，不是 Terrain Block。
///
/// Signed Value 使用 Chebyshev 形式：
///   s(p) = max(|px-cx| - sizeX/2, |py-cy| - sizeY/2, |pz-cz| - sizeZ/2)
/// 该函数在 Cube 内部为负（值约等于到最近面的距离），
/// 在六个面上为 0，在外部为正。它不是 Terrain SDF。
/// </summary>
public sealed class CubeModificationShape : ITerrainModificationShape
{
    public Vector3 Size { get; }

    public bool IsValid
    {
        get { return Size.x > 0f && Size.y > 0f && Size.z > 0f; }
    }

    public CubeModificationShape(Vector3 size)
    {
        Size = new Vector3(
            Mathf.Abs(size.x),
            Mathf.Abs(size.y),
            Mathf.Abs(size.z)
        );
    }

    public Bounds GetLocalBounds(Vector3 position)
    {
        return new Bounds(position, Size);
    }

    public float EvaluateSignedValue(
        Vector3 position,
        Vector3 samplePosition)
    {
        Vector3 halfSize = Size * 0.5f;

        float dx = Mathf.Abs(samplePosition.x - position.x) - halfSize.x;
        float dy = Mathf.Abs(samplePosition.y - position.y) - halfSize.y;
        float dz = Mathf.Abs(samplePosition.z - position.z) - halfSize.z;

        return Mathf.Max(dx, Mathf.Max(dy, dz));
    }

    public bool Contains(Vector3 position, Vector3 samplePosition)
    {
        return EvaluateSignedValue(position, samplePosition) <= 0f;
    }
}
