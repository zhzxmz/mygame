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
        Vector3 cavityCenter,
        float cavityRadius,
        float grassDepth,
        float dirtDepth,
        bool useLegacyMaterialRules,
        float weatheringStrength,
        float weatheringDepth,
        float gentleSlopeAngle,
        float steepSlopeAngle,
        float gravityStrength,
        int soilSimulationIterations,
        float grassStrength,
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
                    cavityCenter,
                    cavityRadius
                );
                break;

            default:
                FillHeightField(densityField, useNoise, height, noiseScale, noiseStrength);
                break;
        }

        if (mode == DensityGenerationMode.HeightField && !useLegacyMaterialRules)
        {
            FillHeightFieldMaterialV2(
                densityField,
                generatedMaterials,
                grassDepth,
                weatheringStrength,
                weatheringDepth,
                gentleSlopeAngle,
                steepSlopeAngle,
                gravityStrength,
                soilSimulationIterations,
                grassStrength
            );
        }
        else
        {
            FillMaterialFieldSimple(
                mode,
                densityField,
                generatedMaterials,
                grassDepth,
                dirtDepth
            );
        }

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
                    Vector3 local = field.GetLocalPosition(x, y, z);
                    float surface = ComputeSurfaceHeight(
                        local,
                        useNoise,
                        height,
                        noiseScale,
                        noiseStrength
                    );

                    field[x, y, z] = surface - local.y;
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
                    Vector3 local = field.GetLocalPosition(x, y, z);

                    float noise = Sample3DValueNoise(
                        local.x * frequency,
                        local.y * frequency,
                        local.z * frequency
                    );

                    // value noise 为 0..1，映射到 -amplitude..amplitude。
                    field[x, y, z] = (noise - 0.5f) * 2f * amplitude;
                }
            }
        }
    }

    /// <summary>
    /// Debug/Test 模式：在高度场地形内部挖一个球形空洞。
    /// cavityCenter 是 Terrain Local Space 中的绝对球心，不随 Chunk 划分重复。
    /// density = min(地形密度, 到球心的距离 - 半径)。
    /// 球内 distance - radius &lt; 0，会把实体强制变成空气。
    /// </summary>
    private static void FillSphereCavity(
        DensityField field,
        bool useNoise,
        float height,
        float noiseScale,
        float noiseStrength,
        Vector3 cavityCenter,
        float cavityRadius)
    {
        float radius = Mathf.Max(0f, cavityRadius);

        for (int x = 0; x < field.SampleCountX; x++)
        {
            for (int y = 0; y < field.SampleCountY; y++)
            {
                for (int z = 0; z < field.SampleCountZ; z++)
                {
                    Vector3 local = field.GetLocalPosition(x, y, z);

                    float surface = ComputeSurfaceHeight(
                        local,
                        useNoise,
                        height,
                        noiseScale,
                        noiseStrength
                    );

                    float terrainDensity = surface - local.y;
                    float cavityDensity = Vector3.Distance(local, cavityCenter) - radius;

                    field[x, y, z] = Mathf.Min(terrainDensity, cavityDensity);
                }
            }
        }
    }

    /// <summary>
    /// HeightField v2 材料生成：
    /// Stone -> Weathering -> Initial Dirt -> Slope Retention -> Gravity Deposition -> Final Dirt -> Grass。
    /// 只修改 MaterialField，不修改 DensityField。
    /// 已知 V2 限制：本 pass 目前只读取当前 Chunk 的 DensityField，坡度/重力沉积不跨 Chunk；
    /// 因此多 Chunk 时 Material 结果可能仍依赖 Chunk 划分，本轮暂不重写此算法。
    /// </summary>
    private static void FillHeightFieldMaterialV2(
        DensityField densityField,
        MaterialField materialField,
        float grassDepth,
        float weatheringStrength,
        float weatheringDepth,
        float gentleSlopeAngle,
        float steepSlopeAngle,
        float gravityStrength,
        int soilSimulationIterations,
        float grassStrength)
    {
        int sizeX = densityField.SampleCountX;
        int sizeZ = densityField.SampleCountZ;

        float[,] surfaceHeight = new float[sizeX, sizeZ];
        float[,] slopeFactor = new float[sizeX, sizeZ];
        float[,] soilThickness = new float[sizeX, sizeZ];

        // 1. 从 DensityField 找出每个 column 的地表高度。
        for (int x = 0; x < sizeX; x++)
        {
            for (int z = 0; z < sizeZ; z++)
            {
                surfaceHeight[x, z] = FindSurfaceHeight(densityField, x, z);
            }
        }

        // 2. 局部坡度：0 = 平地，1 = 陡坡。
        float gentle = Mathf.Max(0f, gentleSlopeAngle);
        float steep = Mathf.Max(gentle + 0.001f, steepSlopeAngle);

        for (int x = 0; x < sizeX; x++)
        {
            for (int z = 0; z < sizeZ; z++)
            {
                int xm = Mathf.Max(0, x - 1);
                int xp = Mathf.Min(sizeX - 1, x + 1);
                int zm = Mathf.Max(0, z - 1);
                int zp = Mathf.Min(sizeZ - 1, z + 1);

                float dx = (surfaceHeight[xp, z] - surfaceHeight[xm, z])
                           / ((xp - xm) * densityField.CellSizeX);
                float dz = (surfaceHeight[x, zp] - surfaceHeight[x, zm])
                           / ((zp - zm) * densityField.CellSizeZ);

                float slopeAngle = Mathf.Atan(Mathf.Sqrt(dx * dx + dz * dz))
                                   * Mathf.Rad2Deg;

                slopeFactor[x, z] = Mathf.Clamp01(
                    Mathf.InverseLerp(gentle, steep, slopeAngle)
                );
            }
        }

        // 3. 风化：越接近地表的 Stone 越容易产生初始 Dirt。
        float strength = Mathf.Max(0f, weatheringStrength);
        float exposureDepth = Mathf.Max(0.0001f, weatheringDepth);

        for (int x = 0; x < sizeX; x++)
        {
            for (int z = 0; z < sizeZ; z++)
            {
                float surface = surfaceHeight[x, z];
                float columnSoil = 0f;

                for (int y = 0; y < densityField.SampleCountY; y++)
                {
                    float localY = densityField.Origin.y + y * densityField.CellSizeY;
                    float depth = surface - localY;

                    if (depth <= 0f) continue;
                    if (depth > exposureDepth) continue;

                    float exposure = Mathf.Clamp01(1f - depth / exposureDepth);
                    columnSoil += strength * exposure * densityField.CellSizeY;
                }

                // 4. 坡度稳定性：缓坡保留，陡坡流失。
                columnSoil *= (1f - slopeFactor[x, z]);
                soilThickness[x, z] = Mathf.Max(0f, columnSoil);
            }
        }

        // 5. 重力沉积：Dirt 向较低的地表迁移。
        int iterations = Mathf.Max(0, soilSimulationIterations);
        float gravity = Mathf.Max(0f, gravityStrength);

        for (int iteration = 0; iteration < iterations; iteration++)
        {
            float[,] nextSoil = (float[,])soilThickness.Clone();

            for (int x = 0; x < sizeX; x++)
            {
                for (int z = 0; z < sizeZ; z++)
                {
                    float here = soilThickness[x, z];
                    if (here <= 0f) continue;

                    float slope = slopeFactor[x, z];
                    if (slope <= 0f) continue;

                    float moveFactor = Mathf.Clamp01(gravity * slope * 0.25f);
                    if (moveFactor <= 0f) continue;

                    float surface = surfaceHeight[x, z];

                    float diffRight = 0f;
                    float diffLeft = 0f;
                    float diffUp = 0f;
                    float diffDown = 0f;

                    if (x + 1 < sizeX) diffRight = surface - surfaceHeight[x + 1, z];
                    if (x - 1 >= 0) diffLeft = surface - surfaceHeight[x - 1, z];
                    if (z + 1 < sizeZ) diffUp = surface - surfaceHeight[x, z + 1];
                    if (z - 1 >= 0) diffDown = surface - surfaceHeight[x, z - 1];

                    diffRight = Mathf.Max(0f, diffRight);
                    diffLeft = Mathf.Max(0f, diffLeft);
                    diffUp = Mathf.Max(0f, diffUp);
                    diffDown = Mathf.Max(0f, diffDown);

                    float totalDiff = diffRight + diffLeft + diffUp + diffDown;
                    if (totalDiff <= 0.0001f) continue;

                    float move = Mathf.Min(here, here * moveFactor);
                    nextSoil[x, z] -= move;

                    if (diffRight > 0f) nextSoil[x + 1, z] += move * diffRight / totalDiff;
                    if (diffLeft > 0f) nextSoil[x - 1, z] += move * diffLeft / totalDiff;
                    if (diffUp > 0f) nextSoil[x, z + 1] += move * diffUp / totalDiff;
                    if (diffDown > 0f) nextSoil[x, z - 1] += move * diffDown / totalDiff;
                }
            }

            soilThickness = nextSoil;
        }

        // 6. Grass：最终稳定 Dirt 表面才可能长草。
        float maxGrassThickness = Mathf.Max(0f, grassDepth);
        float grassAmountFactor = Mathf.Clamp01(grassStrength);

        for (int x = 0; x < sizeX; x++)
        {
            for (int z = 0; z < sizeZ; z++)
            {
                float surface = surfaceHeight[x, z];
                float soil = Mathf.Max(0f, soilThickness[x, z]);
                float grassSlopeFactor = 1f - slopeFactor[x, z];

                float grassThickness = Mathf.Min(
                    soil,
                    maxGrassThickness * grassAmountFactor * grassSlopeFactor
                );

                for (int y = 0; y < densityField.SampleCountY; y++)
                {
                    float density = densityField[x, y, z];

                    if (density <= 0f)
                    {
                        materialField[x, y, z] = MaterialType.Air;
                        continue;
                    }

                    float localY = densityField.Origin.y + y * densityField.CellSizeY;
                    float depth = surface - localY;
                    if (depth < 0f) depth = 0f;

                    if (depth <= grassThickness)
                    {
                        materialField[x, y, z] = MaterialType.Grass;
                    }
                    else if (depth <= soil)
                    {
                        materialField[x, y, z] = MaterialType.Dirt;
                    }
                    else
                    {
                        materialField[x, y, z] = MaterialType.Stone;
                    }
                }
            }
        }
    }

    private static float FindSurfaceHeight(DensityField densityField, int x, int z)
    {
        for (int y = 0; y < densityField.SampleCountY - 1; y++)
        {
            float density0 = densityField[x, y, z];
            float density1 = densityField[x, y + 1, z];

            if (density0 > 0f && density1 <= 0f)
            {
                float t = density0 / (density0 - density1);
                return densityField.Origin.y + (y + t) * densityField.CellSizeY;
            }
        }

        // 没有找到地表交点：底部实体则视为地形顶部，否则视为底部。
        if (densityField[x, 0, z] > 0f)
        {
            return densityField.Origin.y + densityField.Size.y;
        }

        return densityField.Origin.y;
    }

    /// <summary>
    /// 简单的旧规则：HeightField 按 depth 直接分层；
    /// ThreeDimensional / SphereCavity 只区分 Air / Stone。
    /// </summary>
    private static void FillMaterialFieldSimple(
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
                        // HeightField 下 density = surfaceHeight - localY，可直接作为地表以下深度。
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
        Vector3 terrainLocalPosition,
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
                terrainLocalPosition.x * noiseScale,
                terrainLocalPosition.z * noiseScale
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
