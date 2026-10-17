using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using VoxelWorld;
using VoxelWorld.Generation;

// 命名空间 VoxelWorld 与其内部的类 VoxelWorld 同名：
// 在该 using 下 "VoxelWorld.X" 会被解析为命名空间查找，因此这里显式取类型别名。
using VoxelWorldCore = VoxelWorld.VoxelWorld;

/// <summary>
/// World Generation Core（Phase 1）验证：
///   1. 同 Seed + 同 Chunk 两次生成完全一致（同实例 / 不同实例 / 不同生成顺序）；
///   2. 不同 Seed 结果不同；
///   3. Chunk 坐标转换正确（Chunk 坐标 ↔ 世界坐标 ↔ 局部坐标，含负坐标与跨 Chunk 连续性）；
///   4. Grass / Dirt / Stone / Air 分布严格符合 V1 §4 规则。
///
/// 用法：
///   · Editor 菜单：Tools/VoxelWorld/Validate World Generation
///   · 批处理：-executeMethod WorldGenerationCoreValidation.RunBatch
///
/// 本文件只是测试入口，不修改任何生产数据逻辑。
/// </summary>
public static class WorldGenerationCoreValidation
{
    private static int checks;
    private static int failures;

    [MenuItem("Tools/VoxelWorld/Validate World Generation")]
    public static void ValidateFromMenu()
    {
        ValidateAll();

        Debug.Log(
            "[WorldGenerationCoreValidation] Menu validation result = " +
            (failures == 0 ? "PASS" : "FAIL")
        );
    }

    public static void RunBatch()
    {
        ValidateAll();

        Debug.Log(
            "[WorldGenerationCoreValidation] ===== SUMMARY =====\n" +
            "Checks = " + checks + "\n" +
            "Failures = " + failures + "\n" +
            "Result = " + (failures == 0 ? "PASS" : "FAIL")
        );

        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    private static void ValidateAll()
    {
        checks = 0;
        failures = 0;

        WorldGenerationSettings settings = WorldGenerationSettings.Default;

        ValidateSettings(settings);
        ValidateDeterminism(settings);
        ValidateDifferentSeed(settings);
        ValidateCoordinateConversion(settings);
        ValidateVoxelDistributionRules(settings);
    }

    // =====================================================================
    // 参数
    // =====================================================================

    private static void ValidateSettings(WorldGenerationSettings settings)
    {
        string reason;

        Check(settings.IsValid(out reason),
            "默认参数合法（" + settings + "）");

        WorldGenerationSettings invalid = settings;
        invalid.TerrainHeight = settings.WorldHeight; // 越界
        string invalidReason;
        Check(!invalid.IsValid(out invalidReason),
            "TerrainHeight >= WorldHeight 被判定为非法（" + invalidReason + "）");

        bool threw = false;
        try
        {
            new BasicTerrainGenerator(invalid);
        }
        catch (ArgumentException)
        {
            threw = true;
        }

        Check(threw, "非法参数构造生成器时抛 ArgumentException（显式失败，不静默用默认值）");

        WorldGenerationSettings zeroSoil = settings;
        zeroSoil.SoilDepth = 0;
        Check(zeroSoil.IsValid(out reason),
            "SoilDepth = 0 合法（无 Dirt 层）");
    }

    // =====================================================================
    // 1) 确定性
    // =====================================================================

    private static void ValidateDeterminism(WorldGenerationSettings settings)
    {
        BasicTerrainGenerator generator = new BasicTerrainGenerator(settings);

        VoxelCoordinate[] chunks = new VoxelCoordinate[]
        {
            new VoxelCoordinate(0, 0, 0),
            new VoxelCoordinate(0, 1, 0),
            new VoxelCoordinate(10, 0, -7),
            new VoxelCoordinate(-3, 1, 5)
        };

        for (int i = 0; i < chunks.Length; i++)
        {
            VoxelChunk first = generator.Generate(chunks[i]);
            VoxelChunk second = generator.Generate(chunks[i]);

            Check(ChunksEqual(first, second),
                "同 Seed 同 Chunk 两次生成（同实例）完全一致：Chunk" + chunks[i]);
        }

        // 不同生成器实例（同参数）——验证没有隐藏可变状态
        BasicTerrainGenerator other = new BasicTerrainGenerator(settings);

        for (int i = 0; i < chunks.Length; i++)
        {
            VoxelChunk a = generator.Generate(chunks[i]);
            VoxelChunk b = other.Generate(chunks[i]);

            Check(ChunksEqual(a, b),
                "同 Seed 同 Chunk 两次生成（不同实例）完全一致：Chunk" + chunks[i]);
        }

        // 不同生成顺序（V1 §6：不依赖加载顺序）
        VoxelCoordinate x = new VoxelCoordinate(2, 1, 3);
        VoxelCoordinate y = new VoxelCoordinate(-5, 0, 8);

        BasicTerrainGenerator orderA = new BasicTerrainGenerator(settings);
        VoxelChunk aFirst = orderA.Generate(x);
        VoxelChunk aSecond = orderA.Generate(y);

        BasicTerrainGenerator orderB = new BasicTerrainGenerator(settings);
        VoxelChunk bSecond = orderB.Generate(y);
        VoxelChunk bFirst = orderB.Generate(x);

        Check(
            ChunksEqual(aFirst, bFirst) && ChunksEqual(aSecond, bSecond),
            "生成结果与调用顺序无关（先 x 后 y ≡ 先 y 后 x）"
        );

        // 相邻 Chunk 是否已生成，不影响结果（V1 §6）
        BasicTerrainGenerator isolated = new BasicTerrainGenerator(settings);
        VoxelChunk alone = isolated.Generate(new VoxelCoordinate(4, 1, 4));

        BasicTerrainGenerator surrounded = new BasicTerrainGenerator(settings);
        surrounded.Generate(new VoxelCoordinate(3, 1, 4));
        surrounded.Generate(new VoxelCoordinate(5, 1, 4));
        surrounded.Generate(new VoxelCoordinate(4, 1, 3));
        surrounded.Generate(new VoxelCoordinate(4, 1, 5));
        VoxelChunk withNeighbours = surrounded.Generate(new VoxelCoordinate(4, 1, 4));

        Check(ChunksEqual(alone, withNeighbours),
            "目标 Chunk 的结果与相邻 Chunk 是否已生成无关");
    }

    // =====================================================================
    // 2) 不同 Seed
    // =====================================================================

    private static void ValidateDifferentSeed(WorldGenerationSettings settings)
    {
        WorldGenerationSettings otherSeed = settings;
        otherSeed.Seed = settings.Seed + 1;

        BasicTerrainGenerator a = new BasicTerrainGenerator(settings);
        BasicTerrainGenerator b = new BasicTerrainGenerator(otherSeed);

        VoxelCoordinate chunkCoordinate = new VoxelCoordinate(0, 1, 0);
        VoxelChunk chunkA = a.Generate(chunkCoordinate);
        VoxelChunk chunkB = b.Generate(chunkCoordinate);

        int diff = CountDifferences(chunkA, chunkB);

        Check(diff > 0,
            "不同 Seed 生成结果不同（Chunk" + chunkCoordinate +
            " 差异 Voxel = " + diff + "）");

        int heightDiff = 0;
        for (int x = 0; x < 16; x++)
        {
            for (int z = 0; z < 16; z++)
            {
                if (a.GetTerrainHeight(x, z) != b.GetTerrainHeight(x, z))
                {
                    heightDiff++;
                }
            }
        }

        Check(heightDiff > 0,
            "不同 Seed 至少一列地表高度不同（" + heightDiff + "/256 列）");

        // 同 Seed 不同实例高度必须完全一致（HeightFunction 确定性）
        BasicTerrainGenerator a2 = new BasicTerrainGenerator(settings);
        bool heightsEqual = true;
        for (int x = 0; x < 16 && heightsEqual; x++)
        {
            for (int z = 0; z < 16; z++)
            {
                if (a.GetTerrainHeight(x, z) != a2.GetTerrainHeight(x, z))
                {
                    heightsEqual = false;
                    break;
                }
            }
        }

        Check(heightsEqual, "同 Seed 同坐标的地表高度可重复（HeightFunction 确定性）");

        // 频率/幅度也是显式参数：改幅度必须改变结果
        WorldGenerationSettings flat = settings;
        flat.TerrainAmplitude = 0f;
        BasicTerrainGenerator flatGenerator = new BasicTerrainGenerator(flat);
        VoxelChunk flatChunk = flatGenerator.Generate(chunkCoordinate);
        Check(CountDifferences(chunkA, flatChunk) > 0,
            "TerrainAmplitude = 0 与默认幅度结果不同（参数显式生效）");
    }

    // =====================================================================
    // 3) 坐标转换
    // =====================================================================

    private static void ValidateCoordinateConversion(WorldGenerationSettings settings)
    {
        BasicTerrainGenerator generator = new BasicTerrainGenerator(settings);

        VoxelCoordinate[] chunks = new VoxelCoordinate[]
        {
            new VoxelCoordinate(0, 0, 0),
            new VoxelCoordinate(1, 0, 0),
            new VoxelCoordinate(-1, 0, 0),
            new VoxelCoordinate(0, 1, 0),
            new VoxelCoordinate(-2, 1, -3)
        };

        for (int c = 0; c < chunks.Length; c++)
        {
            VoxelChunk chunk = generator.Generate(chunks[c]);

            Check(chunk.Coordinate == chunks[c],
                "生成结果携带请求的 Chunk 坐标：" + chunks[c]);

            bool allMatch = true;
            string firstMismatch = string.Empty;

            for (int localX = 0; localX < VoxelChunk.ChunkSize && allMatch; localX++)
            {
                for (int localY = 0; localY < VoxelChunk.ChunkSize && allMatch; localY++)
                {
                    for (int localZ = 0; localZ < VoxelChunk.ChunkSize; localZ++)
                    {
                        VoxelCoordinate world = chunk.LocalToWorldCoordinate(
                            localX, localY, localZ);

                        VoxelCoordinate mappedChunk =
                            VoxelWorldCore.WorldToChunkCoordinate(world);
                        VoxelCoordinate mappedLocal =
                            VoxelWorldCore.WorldToLocalCoordinate(world);

                        if (mappedChunk != chunks[c] ||
                            mappedLocal.x != localX ||
                            mappedLocal.y != localY ||
                            mappedLocal.z != localZ)
                        {
                            allMatch = false;
                            firstMismatch =
                                " local(" + localX + "," + localY + "," + localZ +
                                ") → world" + world + " → chunk" + mappedChunk +
                                " local" + mappedLocal;
                            break;
                        }
                    }
                }
            }

            Check(allMatch,
                "Chunk" + chunks[c] + " 内每个 Voxel 的世界坐标都映射回同一 Chunk/局部坐标" +
                firstMismatch);
        }

        // 跨 Chunk 连续性：相邻 Chunk 在共享边界上的列必须一致（+X / -X / +Z / -Z）
        VoxelChunk c000 = generator.Generate(new VoxelCoordinate(0, 0, 0));
        VoxelChunk c100 = generator.Generate(new VoxelCoordinate(1, 0, 0));
        VoxelChunk cm100 = generator.Generate(new VoxelCoordinate(-1, 0, 0));
        VoxelChunk c001 = generator.Generate(new VoxelCoordinate(0, 0, 1));
        VoxelChunk c0m10 = generator.Generate(new VoxelCoordinate(0, 0, -1));

        Check(BoundaryColumnsEqual(c000, VoxelChunk.ChunkSize - 1, c100, 0, 0),
            "跨 Chunk +X 边界列一致（chunk(0,0,0).localX=15 ≡ chunk(1,0,0).localX=0）");

        Check(BoundaryColumnsEqual(cm100, VoxelChunk.ChunkSize - 1, c000, 0, 0),
            "跨 Chunk -X 边界列一致（chunk(-1,0,0).localX=15 ≡ chunk(0,0,0).localX=0）");

        Check(BoundaryColumnsEqual(c000, VoxelChunk.ChunkSize - 1, c001, 0, 2),
            "跨 Chunk +Z 边界列一致（chunk(0,0,0).localZ=15 ≡ chunk(0,0,1).localZ=0）");

        Check(BoundaryColumnsEqual(c0m10, VoxelChunk.ChunkSize - 1, c000, 0, 2),
            "跨 Chunk -Z 边界列一致（chunk(0,0,-1).localZ=15 ≡ chunk(0,0,0).localZ=0）");
    }

    // =====================================================================
    // 4) 分布规则（V1 §4）
    // =====================================================================

    private static void ValidateVoxelDistributionRules(WorldGenerationSettings settings)
    {
        BasicTerrainGenerator generator = new BasicTerrainGenerator(settings);

        VoxelCoordinate surfaceChunkCoordinate = new VoxelCoordinate(0, 1, 0);
        VoxelCoordinate undergroundChunkCoordinate = new VoxelCoordinate(0, 0, 0);
        VoxelCoordinate aboveWorldChunkCoordinate = new VoxelCoordinate(0, 5, 0);

        ValidateChunkAgainstRules(generator, settings, surfaceChunkCoordinate);
        ValidateChunkAgainstRules(generator, settings, undergroundChunkCoordinate);
        ValidateChunkAgainstRules(generator, settings, aboveWorldChunkCoordinate);

        // 计数验证（独立于规则函数重新统计）
        VoxelChunk surfaceChunk = generator.Generate(surfaceChunkCoordinate);
        int grassCount = CountType(surfaceChunk, VoxelType.Grass);
        int airCount = CountType(surfaceChunk, VoxelType.Air);

        Check(grassCount == 16 * 16,
            "地表 Chunk(0,1,0) 每个世界列恰好 1 个 Grass（实际 " + grassCount + "）");

        Check(airCount > 0,
            "地表 Chunk(0,1,0) 存在 Air（地表之上，实际 " + airCount + "）");

        VoxelChunk underground = generator.Generate(undergroundChunkCoordinate);
        Check(CountType(underground, VoxelType.Air) == 0 &&
              CountType(underground, VoxelType.Grass) == 0 &&
              CountType(underground, VoxelType.Stone) > 0,
            "地下 Chunk(0,0,0) 无 Air / 无 Grass 且含 Stone（实际 Stone=" +
            CountType(underground, VoxelType.Stone) + "）");

        VoxelChunk aboveWorld = generator.Generate(aboveWorldChunkCoordinate);
        Check(CountType(aboveWorld, VoxelType.Air) == VoxelChunk.VoxelCount,
            "世界之上 Chunk(0,5,0) 全为 Air（y >= WorldHeight=" +
            settings.WorldHeight + "）");

        // SoilDepth 生效性：SoilDepth=0 ⇒ 无 Dirt
        WorldGenerationSettings noSoil = settings;
        noSoil.SoilDepth = 0;
        BasicTerrainGenerator noSoilGenerator = new BasicTerrainGenerator(noSoil);
        VoxelChunk noSoilChunk = noSoilGenerator.Generate(surfaceChunkCoordinate);

        Check(CountType(noSoilChunk, VoxelType.Dirt) == 0,
            "SoilDepth=0 时不生成 Dirt（实际 " +
            CountType(noSoilChunk, VoxelType.Dirt) + "）");
        Check(CountType(noSoilChunk, VoxelType.Grass) == 16 * 16,
            "SoilDepth=0 仍生成 Grass（实际 " +
            CountType(noSoilChunk, VoxelType.Grass) + "）");
    }

    /// <summary>逐 Voxel 校验：生成结果必须等于规则函数 ClassifyVoxel(worldY, height)。</summary>
    private static void ValidateChunkAgainstRules(
        BasicTerrainGenerator generator,
        WorldGenerationSettings settings,
        VoxelCoordinate chunkCoordinate)
    {
        VoxelChunk chunk = generator.Generate(chunkCoordinate);

        bool allMatch = true;
        string firstMismatch = string.Empty;
        int checkedVoxels = 0;

        for (int localX = 0; localX < VoxelChunk.ChunkSize && allMatch; localX++)
        {
            for (int localZ = 0; localZ < VoxelChunk.ChunkSize; localZ++)
            {
                for (int localY = 0; localY < VoxelChunk.ChunkSize; localY++)
                {
                    VoxelCoordinate world = chunk.LocalToWorldCoordinate(
                        localX, localY, localZ);

                    int height = HeightFunction.GetTerrainHeight(
                        world.x, world.z, settings);

                    VoxelType expected = BasicTerrainGenerator.ClassifyVoxel(
                        world.y, height, settings);

                    VoxelType actual = chunk.GetVoxel(
                        localX, localY, localZ).Type;

                    checkedVoxels++;

                    if (actual != expected)
                    {
                        allMatch = false;
                        firstMismatch =
                            " 首个不匹配：world" + world + " height=" + height +
                            " expected=" + expected + " actual=" + actual;
                        break;
                    }
                }
            }
        }

        Check(allMatch,
            "Chunk" + chunkCoordinate + " 全部 " + checkedVoxels +
            " 个体素符合 V1 §4 规则（Air/Grass/Dirt/Stone）" + firstMismatch);
    }

    // =====================================================================
    // 工具
    // =====================================================================

    private static bool ChunksEqual(VoxelChunk a, VoxelChunk b)
    {
        if (a == null || b == null) return false;
        if (a.Coordinate != b.Coordinate) return false;

        for (int x = 0; x < VoxelChunk.ChunkSize; x++)
        {
            for (int y = 0; y < VoxelChunk.ChunkSize; y++)
            {
                for (int z = 0; z < VoxelChunk.ChunkSize; z++)
                {
                    if (a.GetVoxel(x, y, z) != b.GetVoxel(x, y, z)) return false;
                }
            }
        }

        return true;
    }

    private static int CountDifferences(VoxelChunk a, VoxelChunk b)
    {
        int diff = 0;

        for (int x = 0; x < VoxelChunk.ChunkSize; x++)
        {
            for (int y = 0; y < VoxelChunk.ChunkSize; y++)
            {
                for (int z = 0; z < VoxelChunk.ChunkSize; z++)
                {
                    if (a.GetVoxel(x, y, z) != b.GetVoxel(x, y, z)) diff++;
                }
            }
        }

        return diff;
    }

    private static int CountType(VoxelChunk chunk, VoxelType type)
    {
        int count = 0;

        for (int x = 0; x < VoxelChunk.ChunkSize; x++)
        {
            for (int y = 0; y < VoxelChunk.ChunkSize; y++)
            {
                for (int z = 0; z < VoxelChunk.ChunkSize; z++)
                {
                    if (chunk.GetVoxel(x, y, z).Type == type) count++;
                }
            }
        }

        return count;
    }

    /// <summary>
    /// 校验两个 Chunk 在共享边界列上逐 Voxel 一致（覆盖全部 16×16 条列）。
    /// axis = 0：比较 localX 边界列（a.localX = localA ↔ b.localX = localB）；
    /// axis = 2：比较 localZ 边界列（a.localZ = localA ↔ b.localZ = localB）。
    /// </summary>
    private static bool BoundaryColumnsEqual(
        VoxelChunk a,
        int localA,
        VoxelChunk b,
        int localB,
        int axis)
    {
        for (int y = 0; y < VoxelChunk.ChunkSize; y++)
        {
            for (int t = 0; t < VoxelChunk.ChunkSize; t++)
            {
                Voxel va = axis == 0
                    ? a.GetVoxel(localA, y, t)
                    : a.GetVoxel(t, y, localA);

                Voxel vb = axis == 0
                    ? b.GetVoxel(localB, y, t)
                    : b.GetVoxel(t, y, localB);

                if (va != vb) return false;
            }
        }

        return true;
    }

    private static void Check(bool condition, string message)
    {
        checks++;

        if (condition)
        {
            Debug.Log("PASS - " + message);
            return;
        }

        failures++;
        Debug.LogError("FAIL - " + message);
    }
}
