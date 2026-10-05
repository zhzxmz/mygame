using UnityEngine;

/// <summary>
/// 一个地形 Chunk 的空间边界、Terrain Local Space 数据与 Mesh。
/// Chunk 只表示连续 Density 世界中的一个空间分块，不是 Minecraft 方块容器。
/// Mesh 使用 Chunk Local Space；Chunk GameObject 的 Transform 负责放置到世界空间。
/// Dirty / Rebuild 只消费当前 DensityField / MaterialField，不重新进行 World Generation。
/// </summary>
public class TerrainChunk
{
    /// <summary>Chunk 的绝对整数空间坐标。</summary>
    public Vector3Int Coordinate { get; private set; }

    /// <summary>逻辑 Voxel 在 Terrain Local Space 中的尺寸。Voxel 与 Density Sample 是不同概念。</summary>
    public float VoxelSize { get; private set; }

    /// <summary>Chunk 每个轴包含的逻辑 Voxel 数量。</summary>
    public Vector3Int ChunkVoxelSize { get; private set; }

    /// <summary>Chunk 的 Terrain Local Space 尺寸 = VoxelSize × ChunkVoxelSize。</summary>
    public Vector3 ChunkWorldSize { get; private set; }

    /// <summary>Chunk 在 Terrain Local Space 中的原点。</summary>
    public Vector3 ChunkOrigin { get; private set; }

    /// <summary>Chunk 在 Terrain Local Space 中的空间边界。</summary>
    public Bounds LocalBounds { get; private set; }

    /// <summary>本次 Mesh 构建使用的等值面。</summary>
    public float IsoLevel { get; private set; }

    public DensityField Density { get; private set; }
    public MaterialField Material { get; private set; }
    public Mesh Mesh { get; private set; }

    public bool IsDirty { get; private set; }

    public bool IsBuilt
    {
        get { return Density != null && Material != null && Mesh != null; }
    }

    public TerrainChunk(
        Vector3Int coordinate,
        float voxelSize,
        Vector3Int chunkVoxelSize)
    {
        Coordinate = coordinate;
        VoxelSize = Mathf.Max(0.0001f, voxelSize);
        ChunkVoxelSize = new Vector3Int(
            Mathf.Max(1, chunkVoxelSize.x),
            Mathf.Max(1, chunkVoxelSize.y),
            Mathf.Max(1, chunkVoxelSize.z)
        );

        ChunkWorldSize = new Vector3(
            VoxelSize * ChunkVoxelSize.x,
            VoxelSize * ChunkVoxelSize.y,
            VoxelSize * ChunkVoxelSize.z
        );

        ChunkOrigin = ChunkSpace.ChunkToLocalOrigin(
            coordinate,
            VoxelSize,
            ChunkVoxelSize
        );

        LocalBounds = new Bounds(
            ChunkOrigin + ChunkWorldSize * 0.5f,
            ChunkWorldSize
        );

        IsDirty = false;
    }

    /// <summary>
    /// 初始构建：接收 World Generation 产生的数据，并生成第一次 Mesh。
    /// </summary>
    public void Build(
        DensityField density,
        MaterialField material,
        float isoLevel)
    {
        if (density == null || material == null)
        {
            Dispose();
            return;
        }

        Density = density;
        Material = material;
        IsoLevel = isoLevel;
        IsDirty = false;

        RebuildMesh();
    }

    /// <summary>标记当前 Chunk 的 Mesh 需要在 Spatial Modification 后重建。</summary>
    public void MarkDirty()
    {
        if (Density == null || Material == null) return;

        IsDirty = true;
    }

    /// <summary>
    /// 只使用当前 DensityField / MaterialField 重建 Mesh。
    /// 不调用 World Generation，因此不会覆盖已经修改过的 Terrain State。
    /// </summary>
    public bool RebuildMesh()
    {
        if (Density == null || Material == null) return false;

        Mesh previousMesh = Mesh;
        Mesh = TerrainMeshGenerator.Generate(Density, Material, IsoLevel);
        DestroyMesh(previousMesh);

        IsDirty = false;
        return true;
    }

    /// <summary>Terrain Local Space 位置是否位于当前 Chunk 边界内（半开区间）。</summary>
    public bool ContainsLocalPosition(Vector3 terrainLocalPosition)
    {
        Vector3 min = LocalBounds.min;
        Vector3 max = LocalBounds.max;

        return terrainLocalPosition.x >= min.x && terrainLocalPosition.x < max.x
            && terrainLocalPosition.y >= min.y && terrainLocalPosition.y < max.y
            && terrainLocalPosition.z >= min.z && terrainLocalPosition.z < max.z;
    }

    /// <summary>World Space 位置是否位于当前 Chunk 边界内。</summary>
    public bool ContainsWorldPosition(Vector3 worldPosition, Transform terrainRoot)
    {
        Vector3 local = terrainRoot != null
            ? terrainRoot.InverseTransformPoint(worldPosition)
            : worldPosition;

        return ContainsLocalPosition(local);
    }

    /// <summary>Chunk 原点的世界空间位置。</summary>
    public Vector3 GetWorldOrigin(Transform terrainRoot)
    {
        return terrainRoot != null
            ? terrainRoot.TransformPoint(ChunkOrigin)
            : ChunkOrigin;
    }

    /// <summary>Chunk 边界在世界空间中的轴对齐包围盒。</summary>
    public Bounds GetWorldBounds(Transform terrainRoot)
    {
        if (terrainRoot == null) return LocalBounds;

        Vector3 localMin = LocalBounds.min;
        Vector3 localMax = LocalBounds.max;

        Vector3[] corners =
        {
            new Vector3(localMin.x, localMin.y, localMin.z),
            new Vector3(localMax.x, localMin.y, localMin.z),
            new Vector3(localMin.x, localMax.y, localMin.z),
            new Vector3(localMax.x, localMax.y, localMin.z),
            new Vector3(localMin.x, localMin.y, localMax.z),
            new Vector3(localMax.x, localMin.y, localMax.z),
            new Vector3(localMin.x, localMax.y, localMax.z),
            new Vector3(localMax.x, localMax.y, localMax.z)
        };

        Vector3 worldMin = terrainRoot.TransformPoint(corners[0]);
        Vector3 worldMax = worldMin;

        for (int i = 1; i < corners.Length; i++)
        {
            Vector3 world = terrainRoot.TransformPoint(corners[i]);
            worldMin = Vector3.Min(worldMin, world);
            worldMax = Vector3.Max(worldMax, world);
        }

        return new Bounds(
            (worldMin + worldMax) * 0.5f,
            worldMax - worldMin
        );
    }

    public void Dispose()
    {
        DestroyMesh(Mesh);
        Mesh = null;
        Density = null;
        Material = null;
        IsDirty = false;
    }

    private static void DestroyMesh(Mesh mesh)
    {
        if (mesh == null) return;

        if (Application.isPlaying)
        {
            Object.Destroy(mesh);
        }
        else
        {
            Object.DestroyImmediate(mesh);
        }
    }
}
