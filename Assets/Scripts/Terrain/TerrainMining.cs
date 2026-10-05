using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Mining V1：Axis-Aligned Cube Brush 的 Terrain Modification。
/// 只修改现有 DensityField / MaterialField，然后复用 Chunk V2 的 Dirty / Rebuild。
/// 不构造 Mesh，不调用 World Generation，不处理 Yield / Item / Collider。
/// </summary>
public static class TerrainMining
{
    /// <summary>
    /// 当前 Density 约定下用于表示 Air 的固定值。
    /// Density &lt; IsoLevel(0) 即 Air；现有 Debug Cutaway 也使用 -1f。
    /// </summary>
    public const float AirDensity = -1f;

    /// <summary>
    /// 在 Terrain Local Space 中执行一次 Cube Mining。
    /// size 是完整尺寸：Bounds = center ± size/2。
    /// 返回被修改的 Density sample 数量。
    /// 如果 rebuild = true，会复用 DensityTerrain.RebuildDirtyChunks() 更新 Mesh。
    /// </summary>
    public static int MineCube(
        DensityTerrain terrain,
        Vector3 localCenter,
        Vector3 size,
        bool rebuild = true)
    {
        if (terrain == null) return 0;

        Vector3 safeSize = new Vector3(
            Mathf.Abs(size.x),
            Mathf.Abs(size.y),
            Mathf.Abs(size.z)
        );

        if (safeSize.x <= 0f || safeSize.y <= 0f || safeSize.z <= 0f)
        {
            return 0;
        }

        Bounds brushBounds = new Bounds(localCenter, safeSize);

        List<TerrainChunk> affectedChunks = new List<TerrainChunk>();
        terrain.CollectChunksIntersectingLocalBounds(
            brushBounds,
            affectedChunks
        );

        // 没有命中任何现有 Chunk 时直接 no-op，避免触发 World Generation。
        if (affectedChunks.Count == 0)
        {
            return 0;
        }

        int modifiedDensitySamples = 0;

        for (int i = 0; i < affectedChunks.Count; i++)
        {
            modifiedDensitySamples += ModifyChunk(
                affectedChunks[i],
                brushBounds
            );
        }

        if (rebuild)
        {
            terrain.RebuildDirtyChunks();
        }

        return modifiedDensitySamples;
    }

    /// <summary>
    /// 只修改当前 Chunk 自己区域内的 Density / Material，然后 MarkDirty。
    /// 一个 Chunk 不会修改另一个 Chunk 的数据。
    /// </summary>
    private static int ModifyChunk(TerrainChunk chunk, Bounds brushBounds)
    {
        if (chunk == null || chunk.Density == null || chunk.Material == null)
        {
            return 0;
        }

        DensityField densityField = chunk.Density;
        MaterialField materialField = chunk.Material;
        int modifiedSamples = 0;

        for (int x = 0; x < densityField.SampleCountX; x++)
        {
            for (int y = 0; y < densityField.SampleCountY; y++)
            {
                for (int z = 0; z < densityField.SampleCountZ; z++)
                {
                    Vector3 sampleLocalPosition = densityField.GetLocalPosition(
                        x,
                        y,
                        z
                    );

                    if (!ContainsInclusive(brushBounds, sampleLocalPosition))
                    {
                        continue;
                    }

                    densityField[x, y, z] = AirDensity;
                    materialField[x, y, z] = MaterialType.Air;
                    modifiedSamples++;
                }
            }
        }

        // 保守规则：只要 Brush Bounds 与 Chunk 相交或接触，该 Chunk 就是 affected。
        // 即使没有 sample 落在 Brush 内，也 MarkDirty，防止未来 Collider / 边界更新遗漏。
        chunk.MarkDirty();

        return modifiedSamples;
    }

    private static bool ContainsInclusive(Bounds bounds, Vector3 point)
    {
        return point.x >= bounds.min.x && point.x <= bounds.max.x
            && point.y >= bounds.min.y && point.y <= bounds.max.y
            && point.z >= bounds.min.z && point.z <= bounds.max.z;
    }
}
