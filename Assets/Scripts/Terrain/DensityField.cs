using UnityEngine;

/// <summary>
/// 三维密度场数据。
/// Density > isoLevel 表示实体，Density < isoLevel 表示空气，边界在等值面上。
/// 这个类是地形源数据；Unity Mesh 只是它的可视化结果。
/// </summary>
public class DensityField
{
    private readonly float[,,] densities;

    public Vector3 Origin { get; private set; }
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

    public Vector3 GetWorldPosition(int x, int y, int z)
    {
        return Origin + new Vector3(
            x * CellSizeX,
            y * CellSizeY,
            z * CellSizeZ
        );
    }
}
