using UnityEngine;

/// <summary>
/// Voxel / Chunk 坐标转换入口。
/// 地形数据统一使用 Terrain Local Space；World Space 查询通过 terrainRoot 转换。
/// Voxel 是逻辑空间单位，不等于 DensityField 的采样点。
/// 负坐标的精确规则在 Chunk Specification V2 中仍未最终确定；
/// 当前实现使用 floor / 非负模数作为临时语义。
/// </summary>
public static class ChunkSpace
{
    /// <summary>Terrain Local Space 位置 → 逻辑 Voxel 坐标。</summary>
    public static Vector3Int LocalToVoxel(Vector3 terrainLocalPosition, float voxelSize)
    {
        float size = Mathf.Max(0.0001f, voxelSize);

        return new Vector3Int(
            Mathf.FloorToInt(terrainLocalPosition.x / size),
            Mathf.FloorToInt(terrainLocalPosition.y / size),
            Mathf.FloorToInt(terrainLocalPosition.z / size)
        );
    }

    /// <summary>World Space 位置 → Terrain Local 位置 → 逻辑 Voxel 坐标。</summary>
    public static Vector3Int WorldToVoxel(
        Vector3 worldPosition,
        Transform terrainRoot,
        float voxelSize)
    {
        Vector3 local = terrainRoot != null
            ? terrainRoot.InverseTransformPoint(worldPosition)
            : worldPosition;

        return LocalToVoxel(local, voxelSize);
    }

    /// <summary>Voxel 坐标 → 所属 Chunk 坐标。</summary>
    public static Vector3Int VoxelToChunk(
        Vector3Int voxelCoordinate,
        Vector3Int chunkVoxelSize)
    {
        int nx = Mathf.Max(1, chunkVoxelSize.x);
        int ny = Mathf.Max(1, chunkVoxelSize.y);
        int nz = Mathf.Max(1, chunkVoxelSize.z);

        return new Vector3Int(
            FloorDivide(voxelCoordinate.x, nx),
            FloorDivide(voxelCoordinate.y, ny),
            FloorDivide(voxelCoordinate.z, nz)
        );
    }

    /// <summary>Voxel 坐标 → Chunk 内部 Local Voxel 坐标。</summary>
    public static Vector3Int VoxelToLocal(
        Vector3Int voxelCoordinate,
        Vector3Int chunkVoxelSize)
    {
        int nx = Mathf.Max(1, chunkVoxelSize.x);
        int ny = Mathf.Max(1, chunkVoxelSize.y);
        int nz = Mathf.Max(1, chunkVoxelSize.z);

        return new Vector3Int(
            PositiveMod(voxelCoordinate.x, nx),
            PositiveMod(voxelCoordinate.y, ny),
            PositiveMod(voxelCoordinate.z, nz)
        );
    }

    /// <summary>Terrain Local Space 位置 → Chunk 坐标。</summary>
    public static Vector3Int LocalToChunk(
        Vector3 terrainLocalPosition,
        float voxelSize,
        Vector3Int chunkVoxelSize)
    {
        return VoxelToChunk(
            LocalToVoxel(terrainLocalPosition, voxelSize),
            chunkVoxelSize
        );
    }

    /// <summary>World Space 位置 → Chunk 坐标。</summary>
    public static Vector3Int WorldToChunk(
        Vector3 worldPosition,
        Transform terrainRoot,
        float voxelSize,
        Vector3Int chunkVoxelSize)
    {
        return VoxelToChunk(
            WorldToVoxel(worldPosition, terrainRoot, voxelSize),
            chunkVoxelSize
        );
    }

    /// <summary>Chunk 坐标 → Chunk 在 Terrain Local Space 中的原点。</summary>
    public static Vector3 ChunkToLocalOrigin(
        Vector3Int chunkCoordinate,
        float voxelSize,
        Vector3Int chunkVoxelSize)
    {
        float size = Mathf.Max(0.0001f, voxelSize);
        float worldSizeX = size * Mathf.Max(1, chunkVoxelSize.x);
        float worldSizeY = size * Mathf.Max(1, chunkVoxelSize.y);
        float worldSizeZ = size * Mathf.Max(1, chunkVoxelSize.z);

        return new Vector3(
            chunkCoordinate.x * worldSizeX,
            chunkCoordinate.y * worldSizeY,
            chunkCoordinate.z * worldSizeZ
        );
    }

    /// <summary>Chunk 坐标 → Chunk 原点在世界空间中的位置。</summary>
    public static Vector3 ChunkToWorldOrigin(
        Vector3Int chunkCoordinate,
        Transform terrainRoot,
        float voxelSize,
        Vector3Int chunkVoxelSize)
    {
        Vector3 localOrigin = ChunkToLocalOrigin(
            chunkCoordinate,
            voxelSize,
            chunkVoxelSize
        );

        return terrainRoot != null
            ? terrainRoot.TransformPoint(localOrigin)
            : localOrigin;
    }

    /// <summary>Voxel 坐标 → Voxel 在 Terrain Local Space 中的最小角位置。</summary>
    public static Vector3 VoxelToLocalMin(Vector3Int voxelCoordinate, float voxelSize)
    {
        float size = Mathf.Max(0.0001f, voxelSize);

        return new Vector3(
            voxelCoordinate.x * size,
            voxelCoordinate.y * size,
            voxelCoordinate.z * size
        );
    }

    private static int FloorDivide(int value, int divisor)
    {
        int quotient = value / divisor;
        int remainder = value % divisor;

        if (remainder != 0 && ((remainder < 0) != (divisor < 0)))
        {
            quotient--;
        }

        return quotient;
    }

    private static int PositiveMod(int value, int divisor)
    {
        int remainder = value % divisor;
        return remainder < 0 ? remainder + divisor : remainder;
    }
}
