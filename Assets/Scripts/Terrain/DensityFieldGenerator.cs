using UnityEngine;

public enum DensityGenerationMode
{
    HeightField,
    ThreeDimensional,
    SphereCavity
}

/// <summary>
/// Density 生成逻辑。
/// 独立的生成层：输入地形参数，输出 DensityField。
/// TerrainMeshGenerator 只负责 Density -> Mesh，不包含 3D Noise 或洞穴逻辑。
/// </summary>
public static class DensityFieldGenerator
{
    public static DensityField Generate(
        DensityGenerationMode mode,
        int resolution,
        Vector3 origin,
        Vector3 size,
        bool useNoise,
        float height,
        float noiseScale,
        float noiseStrength,
        Vector3 cavityCenterOffset,
        float cavityRadius,
        float grassDepth,
        float dirtDepth,
        out MaterialField materialField)
    {
        DensityField densityField = new DensityField(resolution, resolution, resolution, origin, size);
        MaterialField generatedMaterials = new MaterialField(
            resolution,
            resolution,
            resolution,
            origin,
            size
        );

        switch (mode)
        {
            case DensityGenerationMode.HeightField:
                FillHeightField(densityField, useNoise, height, noiseScale, noiseStrength);
                break;

            case DensityGenerationMode.ThreeDimensional:
                FillThreeDimensional(densityField, noiseScale, noiseStrength);
                break;

            case DensityGenerationMode.SphereCavity:
                FillSphereCavity(
                    densityField,
                    useNoise,
                    height,
                    noiseScale,
                    noiseStrength,
                    cavityCenterOffset,
                    cavityRadius
                );
                break;

            default:
                FillHeightField(densityField, useNoise, height, noiseScale, noiseStrength);
                break;
        }

        FillMaterialField(
            mode,
            densityField,
            generatedMaterials,
            grassDepth,
            dirtDepth
        );

        materialField = generatedMaterials;
        return densityField;
    }

    /// <summary>普通高度场地形：density = surfaceHeight(x,z) - y。</summary>
    private static void FillHeightField(
        DensityField field,
        bool useNoise,
        float height,
        float noiseScale,
        float noiseStrength)
    {
        for (int x = 0; x < field.SampleCountX; x++)
        {
            for (int y = 0; y < field.SampleCountY; y++)
            {
                for (int z = 0; z < field.SampleCountZ; z++)
                {
                    Vector3 world = field.GetWorldPosition(x, y, z);
                    float surface = ComputeSurfaceHeight(
                        world,
                        useNoise,
                        height,
                        noiseScale,
                        noiseStrength
                    );

                    field[x, y, z] = surface - world.y;
                }
            }
        }
    }

    /// <summary>
    /// 真正的三维 Density：density 只依赖 (x, y, z) 的三维噪声。
    /// 当前使用简单的 3D value noise，用于验证 DensityField 可以表达三维实体/空气结构。
    /// </summary>
    private static void FillThreeDimensional(
        DensityField field,
        float noiseScale,
        float noiseStrength)
    {
        float frequency = Mathf.Max(0.0001f, noiseScale);
        float amplitude = Mathf.Max(0f, noiseStrength);

        for (int x = 0; x < field.SampleCountX; x++)
        {
            for (int y = 0; y < field.SampleCountY; y++)
            {
                for (int z = 0; z < field.SampleCountZ; z++)
                {
                    Vector3 world = field.GetWorldPosition(x, y, z);

                    float noise = Sample3DValueNoise(
                        world.x * frequency,
                        world.y * frequency,
                        world.z * frequency
                    );

                    // value noise 为 0..1，映射到 -amplitude..amplitude。
                    field[x, y, z] = (noise - 0.5f) * 2f * amplitude;
                }
            }
        }
    }

    /// <summary>
    /// Debug/Test 模式：在高度场地形内部挖一个球形空洞。
    /// density = min(地形密度, 到球心的距离 - 半径)。
    /// 球内 distance - radius &lt; 0，会把实体强制变成空气。
    /// </summary>
    private static void FillSphereCavity(
        DensityField field,
        bool useNoise,
        float height,
        float noiseScale,
        float noiseStrength,
        Vector3 cavityCenterOffset,
        float cavityRadius)
    {
        Vector3 cavityCenter = field.Origin + cavityCenterOffset;
        float radius = Mathf.Max(0f, cavityRadius);

        for (int x = 0; x < field.SampleCountX; x++)
        {
            for (int y = 0; y < field.SampleCountY; y++)
            {
                for (int z = 0; z < field.SampleCountZ; z++)
                {
                    Vector3 world = field.GetWorldPosition(x, y, z);

                    float surface = ComputeSurfaceHeight(
                        world,
                        useNoise,
                        height,
                        noiseScale,
                        noiseStrength
                    );

                    float terrainDensity = surface - world.y;
                    float cavityDensity = Vector3.Distance(world, cavityCenter) - radius;

                    field[x, y, z] = Mathf.Min(terrainDensity, cavityDensity);
                }
            }
        }
    }

    /// <summary>
    /// 根据 DensityField 生成 MaterialField。
    /// HeightField 使用 Grass / Dirt / Stone 分层；
    /// ThreeDimensional 和 SphereCavity 暂时只区分 Air / Stone。
    /// Air 也会被明确写入 MaterialField。
    /// </summary>
    private static void FillMaterialField(
        DensityGenerationMode mode,
        DensityField densityField,
        MaterialField materialField,
        float grassDepth,
        float dirtDepth)
    {
        float grass = Mathf.Max(0f, grassDepth);
        float dirt = Mathf.Max(0f, dirtDepth);
        float dirtEnd = grass + dirt;

        for (int x = 0; x < densityField.SampleCountX; x++)
        {
            for (int y = 0; y < densityField.SampleCountY; y++)
            {
                for (int z = 0; z < densityField.SampleCountZ; z++)
                {
                    float density = densityField[x, y, z];

                    if (density < 0f)
                    {
                        materialField[x, y, z] = MaterialType.Air;
                        continue;
                    }

                    if (mode == DensityGenerationMode.HeightField)
                    {
                        // HeightField 下 density = surfaceHeight - worldY，可直接作为地表以下深度。
                        float depth = density;

                        if (depth <= grass)
                        {
                            materialField[x, y, z] = MaterialType.Grass;
                        }
                        else if (depth <= dirtEnd)
                        {
                            materialField[x, y, z] = MaterialType.Dirt;
                        }
                        else
                        {
                            materialField[x, y, z] = MaterialType.Stone;
                        }
                    }
                    else
                    {
                        // 3D 和球形空洞测试模式只做最简单的实体/空气区分。
                        materialField[x, y, z] = MaterialType.Stone;
                    }
                }
            }
        }
    }

    private static float ComputeSurfaceHeight(
        Vector3 world,
        bool useNoise,
        float height,
        float noiseScale,
        float noiseStrength)
    {
        float surface = height;

        float strength = Mathf.Max(0f, noiseStrength);
        if (useNoise && strength > 0f)
        {
            float noise = Mathf.PerlinNoise(
                world.x * noiseScale,
                world.z * noiseScale
            ) * 2f - 1f;

            surface += noise * strength;
        }

        return surface;
    }

    /// <summary>简单、确定性的 3D value noise，返回 0..1。</summary>
    private static float Sample3DValueNoise(float x, float y, float z)
    {
        int x0 = Mathf.FloorToInt(x);
        int y0 = Mathf.FloorToInt(y);
        int z0 = Mathf.FloorToInt(z);

        int x1 = x0 + 1;
        int y1 = y0 + 1;
        int z1 = z0 + 1;

        float fx = x - x0;
        float fy = y - y0;
        float fz = z - z0;

        // Smoothstep，避免明显的网格状线性插值。
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);
        fz = fz * fz * (3f - 2f * fz);

        float c000 = HashTo01(x0, y0, z0);
        float c100 = HashTo01(x1, y0, z0);
        float c010 = HashTo01(x0, y1, z0);
        float c110 = HashTo01(x1, y1, z0);
        float c001 = HashTo01(x0, y0, z1);
        float c101 = HashTo01(x1, y0, z1);
        float c011 = HashTo01(x0, y1, z1);
        float c111 = HashTo01(x1, y1, z1);

        float c00 = Mathf.Lerp(c000, c100, fx);
        float c10 = Mathf.Lerp(c010, c110, fx);
        float c01 = Mathf.Lerp(c001, c101, fx);
        float c11 = Mathf.Lerp(c011, c111, fx);

        float c0 = Mathf.Lerp(c00, c10, fy);
        float c1 = Mathf.Lerp(c01, c11, fy);

        return Mathf.Lerp(c0, c1, fz);
    }

    private static float HashTo01(int x, int y, int z)
    {
        unchecked
        {
            int n = x * 374761393 + y * 668265263 + z * 1442695040;
            n = (n ^ (n >> 13)) * 1274126177;
            n = n ^ (n >> 16);
            return (n & 0x7fffffff) / (float)0x7fffffff;
        }
    }
}
