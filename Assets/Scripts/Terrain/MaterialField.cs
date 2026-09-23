using UnityEngine;

/// <summary>
/// 三维材料场数据。
/// 只保存每个采样位置对应的 MaterialType，不生成 Mesh，也不生成地形。
/// 空间信息（Origin / Size / Resolution / SampleCount / CellSize）与 DensityField 保持对齐。
/// </summary>
public class MaterialField
{
    private readonly MaterialType[,,] materials;

    public Vector3 Origin { get; private set; }
    public Vector3 Size { get; private set; }

    public int ResolutionX { get; private set; }
    public int ResolutionY { get; private set; }
    public int ResolutionZ { get; private set; }

    public int SampleCountX { get; private set; }
    public int SampleCountY { get; private set; }
    public int SampleCountZ { get; private set; }

    public float CellSizeX { get; private set; }
    public float CellSizeY { get; private set; }
    public float CellSizeZ { get; private set; }

    public MaterialField(
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

        materials = new MaterialType[SampleCountX, SampleCountY, SampleCountZ];
    }

    public MaterialType this[int x, int y, int z]
    {
        get { return materials[x, y, z]; }
        set { materials[x, y, z] = value; }
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
