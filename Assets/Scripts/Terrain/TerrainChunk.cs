using UnityEngine;

/// <summary>
/// 一个地形 Chunk 的数据与 Mesh。
/// 当前阶段只创建一个 Chunk；未来可以扩展为多 Chunk 管理。
/// Density 是源数据，Mesh 只是生成结果。
/// </summary>
public class TerrainChunk
{
    public DensityField Density { get; private set; }
    public Mesh Mesh { get; private set; }

    public bool IsBuilt
    {
        get { return Density != null && Mesh != null; }
    }

    public void Build(DensityField density, float isoLevel)
    {
        Dispose();

        Density = density;
        Mesh = TerrainMeshGenerator.Generate(density, isoLevel);
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
    }
}
