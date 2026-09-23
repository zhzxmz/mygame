using UnityEngine;

/// <summary>
/// 一个地形 Chunk 的数据与 Mesh。
/// Density 表示形状，Material 表示实体内容，Mesh 只是生成结果。
/// 当前阶段只创建一个 Chunk；未来可以扩展为多 Chunk 管理。
/// </summary>
public class TerrainChunk
{
    public DensityField Density { get; private set; }
    public MaterialField Material { get; private set; }
    public Mesh Mesh { get; private set; }

    public bool IsBuilt
    {
        get { return Density != null && Material != null && Mesh != null; }
    }

    public void Build(DensityField density, MaterialField material, float isoLevel)
    {
        Dispose();

        Density = density;
        Material = material;
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
        Material = null;
    }
}
