using UnityEngine;

/// <summary>
/// 三维密度场数据（Terrain Local Space）。
/// Density > isoLevel 表示实体，Density < isoLevel 表示空气，边界在等值面上。
/// 这个类是地形源数据；Unity Mesh 只是它的可视化结果。
/// Density Sample 是采样点，不等于逻辑 Voxel；Resolution / CellSize 只描述采样精度。
/// </summary>
public class DensityField
{
    private readonly float[,,] densities;

    /// <summary>采样区域原点，位于 Terrain Local Space。</summary>
    public Vector3 Origin { get; private set; }

    /// <summary>采样区域尺寸，位于 Terrain Local Space。</summary>
    public Vector3 Size { get; private set; }

    /// <summary>每个轴上的单元格数量。</summary>
    public int ResolutionX { get; private set; }
    public int ResolutionY { get; private set; }
    public int ResolutionZ { get; private set; }

    /// <summary>每个轴上的采样点数量 = 单元格数量 + 1。</summary>
    public int SampleCountX { get; private set; }
    public int SampleCountY { get; private set; }
    public int SampleCountZ { get; private set; }

    public float CellSizeX { get; private set; }
    public float CellSizeY { get; private set; }
    public float CellSizeZ { get; private set; }

    public DensityField(
        int resolutionX,
        int resolutionY,
        int resolutionZ,
        Vector3 origin,
        Vector3 size)
    {
        ResolutionX = Mathf.Max(1, resolutionX);
        ResolutionY = Mathf.Max(1, resolutionY);
        ResolutionZ = Mathf.Max(1, resolutionZ);

        SampleCountX = ResolutionX + 1;
        SampleCountY = ResolutionY + 1;
        SampleCountZ = ResolutionZ + 1;

        Origin = origin;
        Size = new Vector3(
            Mathf.Max(0.0001f, size.x),
            Mathf.Max(0.0001f, size.y),
            Mathf.Max(0.0001f, size.z)
        );

        CellSizeX = Size.x / ResolutionX;
        CellSizeY = Size.y / ResolutionY;
        CellSizeZ = Size.z / ResolutionZ;

        densities = new float[SampleCountX, SampleCountY, SampleCountZ];
    }

    public float this[int x, int y, int z]
    {
        get { return densities[x, y, z]; }
        set { densities[x, y, z] = value; }
    }

    /// <summary>采样点索引 → 该采样点的 Terrain Local Space 位置。</summary>
    public Vector3 GetLocalPosition(int x, int y, int z)
    {
        return Origin + new Vector3(
            x * CellSizeX,
            y * CellSizeY,
            z * CellSizeZ
        );
    }

    /// <summary>采样点索引 → 相对于 Chunk 原点的 Chunk Local Space 位置。</summary>
    public Vector3 GetChunkLocalPosition(int x, int y, int z)
    {
        return new Vector3(
            x * CellSizeX,
            y * CellSizeY,
            z * CellSizeZ
        );
    }
}
