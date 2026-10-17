using UnityEngine;

namespace TerrainModificationV2
{
    /// <summary>
    /// Terrain Modification V2 的连续 Shape 抽象。
    ///
    /// Shape 完全定义在连续 Terrain Local Space 中：
    /// - 不依赖 Voxel Coordinate
    /// - 不依赖 Density Sample Index
    /// - 不依赖 Chunk Coordinate
    /// - 不包含 World Space / Chunk Space 转换
    ///
    /// EvaluateInfluence 提供连续的 inside / boundary / outside 语义：
    ///   inside    &lt; 0
    ///   boundary == 0
    ///   outside   &gt; 0
    /// 该 influence 不是严格 SDF。
    /// </summary>
    public interface TerrainModificationShape
    {
        /// <summary>Shape 参数是否有效。</summary>
        bool IsValid { get; }

        /// <summary>Shape 在 Terrain Local Space 中的 AABB。</summary>
        Bounds GetBounds();

        /// <summary>计算连续 Terrain Local Space 位置的 Shape influence。</summary>
        float EvaluateInfluence(Vector3 terrainLocalPosition);

        /// <summary>位置是否位于 Shape 内部或边界。</summary>
        bool Contains(Vector3 terrainLocalPosition);
    }

    /// <summary>
    /// V2 Axis-Aligned Cube / Box Shape。
    ///
    /// Center 与 Size 都是连续 Terrain Local Space 值。
    /// Size 是完整尺寸；构造时按绝对值归一化。
    ///
    /// Influence 公式：
    ///   dx = abs(px - cx) - sizeX / 2
    ///   dy = abs(py - cy) - sizeY / 2
    ///   dz = abs(pz - cz) - sizeZ / 2
    ///   influence = max(dx, dy, dz)
    ///
    /// 该值满足 inside &lt; 0 / boundary == 0 / outside &gt; 0，
    /// 但不是严格 SDF。
    /// </summary>
    public sealed class CubeModificationShape : TerrainModificationShape
    {
        /// <summary>连续 Terrain Local Space 中心。</summary>
        public Vector3 Center { get; }

        /// <summary>连续 Terrain Local Space 完整尺寸。</summary>
        public Vector3 Size { get; }

        public bool IsValid
        {
            get { return Size.x > 0f && Size.y > 0f && Size.z > 0f; }
        }

        public CubeModificationShape(Vector3 center, Vector3 size)
        {
            Center = center;
            Size = new Vector3(
                Mathf.Abs(size.x),
                Mathf.Abs(size.y),
                Mathf.Abs(size.z)
            );
        }

        public Bounds GetBounds()
        {
            return new Bounds(Center, Size);
        }

        public float EvaluateInfluence(Vector3 terrainLocalPosition)
        {
            float halfSizeX = Size.x * 0.5f;
            float halfSizeY = Size.y * 0.5f;
            float halfSizeZ = Size.z * 0.5f;

            float dx = Mathf.Abs(terrainLocalPosition.x - Center.x) - halfSizeX;
            float dy = Mathf.Abs(terrainLocalPosition.y - Center.y) - halfSizeY;
            float dz = Mathf.Abs(terrainLocalPosition.z - Center.z) - halfSizeZ;

            return Mathf.Max(dx, Mathf.Max(dy, dz));
        }

        public bool Contains(Vector3 terrainLocalPosition)
        {
            return EvaluateInfluence(terrainLocalPosition) <= 0f;
        }
    }
}
