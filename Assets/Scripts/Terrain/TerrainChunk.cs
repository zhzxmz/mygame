using UnityEngine;

/// <summary>
/// 一个地形 Chunk 的数据与 Mesh。
/// Chunk 只表示连续 Density 世界中的一个空间分块，不是 Minecraft 方块容器。
/// </summary>
public class TerrainChunk
{
    public Vector3Int Coordinate { get; private set; }
    public Vector3 ChunkSize { get; private set; }
    public Vector3 ChunkOrigin { get; private set; }

    public DensityField Density { get; private set; }
    public MaterialField Material { get; private set; }
    public Mesh Mesh { get; private set; }

    public bool IsBuilt
    {
        get { return Density != null && Material != null && Mesh != null; }
    }

    public TerrainChunk(Vector3Int coordinate, Vector3 chunkSize)
    {
        Coordinate = coordinate;
        ChunkSize = chunkSize;
        ChunkOrigin = new Vector3(
            coordinate.x * chunkSize.x,
            coordinate.y * chunkSize.y,
            coordinate.z * chunkSize.z
        );
    }

    public void Build(DensityField density, MaterialField material, float isoLevel)
    {
        Dispose();

        Density = density;
        Material = material;
        Mesh = TerrainMeshGenerator.Generate(density, material, isoLevel);
    }

    public void Dispose()
    {
        if (Mesh != null)
        {
            if (Application.isPlaying)
            {
                Object.Destroy(Mesh);
            }
            else
            {
                Object.DestroyImmediate(Mesh);
            }

            Mesh = null;
        }

        Density = null;
        Material = null;
    }
}
