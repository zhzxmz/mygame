using System.Collections.Generic;
using UnityEngine;

/// <summary>Terrain Modification Operation。</summary>
public enum TerrainModificationOperation
{
    /// <summary>从当前 Terrain 中移除指定空间区域。</summary>
    Subtract
}

/// <summary>
/// 一次 Terrain Modification 的完整请求。
/// Shape 定义连续空间形状；Center 是连续 Terrain Local Space 位置；
/// Operation 定义如何修改 Terrain；Rebuild 决定是否在修改后统一重建 Dirty Chunks。
/// </summary>
public sealed class TerrainModificationRequest
{
    public ITerrainModificationShape Shape { get; private set; }
    public Vector3 Center { get; private set; }
    public TerrainModificationOperation Operation { get; private set; }
    public bool Rebuild { get; private set; }

    public TerrainModificationRequest(
        ITerrainModificationShape shape,
        Vector3 center,
        TerrainModificationOperation operation,
        bool rebuild)
    {
        Shape = shape;
        Center = center;
        Operation = operation;
        Rebuild = rebuild;
    }
}

/// <summary>
/// Terrain Modification 执行结果。
/// ModifiedSampleCount：authoritative state 实际发生变化的 Density Sample 数量。
/// AffectedChunkCount：实际发生数据变化并 MarkDirty 的 Chunk 数量。
/// </summary>
public readonly struct TerrainModificationResult
{
    public int ModifiedSampleCount { get; }
    public int AffectedChunkCount { get; }

    public TerrainModificationResult(
        int modifiedSampleCount,
        int affectedChunkCount)
    {
        ModifiedSampleCount = modifiedSampleCount;
        AffectedChunkCount = affectedChunkCount;
    }
}

/// <summary>
/// Terrain Modification（Phase 2 Density Modification）。
/// 负责把一次通用 Modification Request 应用到权威 Terrain State：
/// DensityField / MaterialField → Affected Chunks → Dirty → Rebuild。
///
/// Phase 2 采用 Scheme 3：Shape-Signed Stamp + Nearest Sample Fallback。
/// - 只对 Shape 内部或边界（Shape Signed Value &lt;= 0）的 Sample 应用：
///     d'(p) = min(d(p), s(p))
/// - Shape 外 Sample 的 Density / Material 完全不变。
/// - 如果所有 affected Chunks 中没有任何 Sample 位于 Shape 内部或边界，
///   才使用 Nearest Sample Fallback，将离 Center 最近的 Sample 写为 AirDensity。
/// - 只有 authoritative state 实际发生变化的 Chunk 才会 MarkDirty。
///
/// Terrain Modification 不直接修改 Mesh、MeshCollider 或 SubMesh。
/// </summary>
public static class TerrainModification
{
    /// <summary>
    /// 当前 Density 约定下用于表示 Air 的固定值。
    /// Density &lt; IsoLevel(0) 即 Air；现有 Debug Cutaway 也使用 -1f。
    /// 正常 Shape Signed Stamp 不写这个常量；它只作为 Nearest Sample Fallback 的保底值。
    /// </summary>
    public const float AirDensity = -1f;

    /// <summary>Sample Index 边界判断使用的极小浮点容差。</summary>
    private const float IndexEpsilon = 0.00001f;

    /// <summary>Density 实际变化判断使用的极小浮点容差。</summary>
    private const float DensityEpsilon = 0.000001f;

    /// <summary>
    /// 应用一次 Terrain Modification。
    /// 返回实际被修改的 Density Sample 数量与 dirty Chunk 数量。
    /// </summary>
    public static TerrainModificationResult Apply(
        DensityTerrain terrain,
        TerrainModificationRequest request)
    {
        if (terrain == null ||
            request == null ||
            request.Shape == null ||
            !request.Shape.IsValid)
        {
            return new TerrainModificationResult(0, 0);
        }

        if (request.Operation != TerrainModificationOperation.Subtract)
        {
            return new TerrainModificationResult(0, 0);
        }

        Bounds modificationBounds = request.Shape.GetLocalBounds(
            request.Center
        );

        List<TerrainChunk> affectedChunks = new List<TerrainChunk>();
        terrain.CollectChunksIntersectingLocalBounds(
            modificationBounds,
            affectedChunks
        );

        // 没有命中任何现有 Chunk 时直接 no-op，避免触发 World Generation。
        if (affectedChunks.Count == 0)
        {
            return new TerrainModificationResult(0, 0);
        }

        int modifiedDensitySamples = 0;
        int dirtyChunkCount = 0;
        bool hasSampleInsideShape = false;

        for (int i = 0; i < affectedChunks.Count; i++)
        {
            int chunkModifiedSamples = ModifyChunk(
                affectedChunks[i],
                request,
                modificationBounds,
                out bool chunkHasSampleInsideShape
            );

            if (chunkHasSampleInsideShape)
            {
                hasSampleInsideShape = true;
            }

            if (chunkModifiedSamples > 0)
            {
                modifiedDensitySamples += chunkModifiedSamples;
                dirtyChunkCount++;
            }
        }

        // Nearest Sample Fallback：
        // 仅当整个 affected Chunk 集合中没有任何 Sample 位于 Shape 内部或边界时使用。
        // 它只负责表达“DensityField 分辨率无法表达极小连续 Brush”的情况。
        if (!hasSampleInsideShape &&
            modifiedDensitySamples == 0 &&
            TryFindNearestSample(
                affectedChunks,
                request.Center,
                out TerrainChunk fallbackChunk,
                out int fallbackX,
                out int fallbackY,
                out int fallbackZ))
        {
            float oldDensity =
                fallbackChunk.Density[fallbackX, fallbackY, fallbackZ];
            MaterialType oldMaterial =
                fallbackChunk.Material[fallbackX, fallbackY, fallbackZ];

            // Fallback 仍然属于 Subtract，必须保持单调：
            // newDensity = min(oldDensity, AirDensity)，不能把更低的 Density 拉回 -1。
            float newDensity = Mathf.Min(oldDensity, AirDensity);
            MaterialType newMaterial = newDensity <= 0f
                ? MaterialType.Air
                : oldMaterial;

            bool densityChanged =
                Mathf.Abs(newDensity - oldDensity) > DensityEpsilon;
            bool materialChanged = newMaterial != oldMaterial;

            if (densityChanged || materialChanged)
            {
                fallbackChunk.Density[fallbackX, fallbackY, fallbackZ] =
                    newDensity;
                fallbackChunk.Material[fallbackX, fallbackY, fallbackZ] =
                    newMaterial;
                modifiedDensitySamples = 1;
                dirtyChunkCount = 1;
                fallbackChunk.MarkDirty();
            }
        }

        if (request.Rebuild)
        {
            terrain.RebuildDirtyChunks();
        }

        return new TerrainModificationResult(
            modifiedDensitySamples,
            dirtyChunkCount
        );
    }

    /// <summary>
    /// 只修改当前 Chunk 自己区域内的 Density / Material。
    /// 正常路径使用 Shape Signed Stamp；返回该 Chunk 实际变化的 Sample 数量。
    /// hasSampleInsideShape 表示该 Chunk 是否存在位于 Shape 内部或边界的 Sample。
    /// 只有实际发生数据变化时才 MarkDirty。
    /// </summary>
    private static int ModifyChunk(
        TerrainChunk chunk,
        TerrainModificationRequest request,
        Bounds modificationBounds,
        out bool hasSampleInsideShape)
    {
        hasSampleInsideShape = false;

        if (chunk == null || chunk.Density == null || chunk.Material == null)
        {
            return 0;
        }

        DensityField densityField = chunk.Density;
        MaterialField materialField = chunk.Material;

        bool hasBoundsCoverageX = TryGetSampleIndexRange(
            modificationBounds.min.x,
            modificationBounds.max.x,
            densityField.Origin.x,
            densityField.CellSizeX,
            densityField.SampleCountX,
            out int minX,
            out int maxX
        );
        bool hasBoundsCoverageY = TryGetSampleIndexRange(
            modificationBounds.min.y,
            modificationBounds.max.y,
            densityField.Origin.y,
            densityField.CellSizeY,
            densityField.SampleCountY,
            out int minY,
            out int maxY
        );
        bool hasBoundsCoverageZ = TryGetSampleIndexRange(
            modificationBounds.min.z,
            modificationBounds.max.z,
            densityField.Origin.z,
            densityField.CellSizeZ,
            densityField.SampleCountZ,
            out int minZ,
            out int maxZ
        );

        if (!hasBoundsCoverageX ||
            !hasBoundsCoverageY ||
            !hasBoundsCoverageZ)
        {
            return 0;
        }

        int modifiedSamples = 0;

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    Vector3 sampleLocalPosition = densityField.GetLocalPosition(
                        x,
                        y,
                        z
                    );

                    float shapeSignedValue = request.Shape.EvaluateSignedValue(
                        request.Center,
                        sampleLocalPosition
                    );

                    if (shapeSignedValue <= 0f)
                    {
                        hasSampleInsideShape = true;
                    }

                    // Shape 外 Sample 保持 Density / Material 完全不变。
                    if (shapeSignedValue > 0f)
                    {
                        continue;
                    }

                    float oldDensity = densityField[x, y, z];
                    MaterialType oldMaterial = materialField[x, y, z];

                    // Phase 2 Subtract：
                    // Shape 内部或边界：d'(p) = min(d(p), s(p))。
                    float newDensity = Mathf.Min(
                        oldDensity,
                        shapeSignedValue
                    );

                    // s(p) <= 0，因此 d'(p) <= 0，Material 必须为 Air。
                    MaterialType newMaterial = newDensity <= 0f
                        ? MaterialType.Air
                        : oldMaterial;

                    bool densityChanged =
                        Mathf.Abs(newDensity - oldDensity) > DensityEpsilon;
                    bool materialChanged = newMaterial != oldMaterial;

                    // 已经是 Air 且 Density 未变的 Sample 不重复计数。
                    if (!densityChanged && !materialChanged)
                    {
                        continue;
                    }

                    densityField[x, y, z] = newDensity;
                    materialField[x, y, z] = newMaterial;
                    modifiedSamples++;
                }
            }
        }

        if (modifiedSamples > 0)
        {
            chunk.MarkDirty();
        }

        return modifiedSamples;
    }

    /// <summary>
    /// 在 affected Chunk 集合中找到距离 Center 最近的 Density Sample。
    /// 对每个 Chunk 使用其自己的 Origin / CellSize 映射，并比较实际 Terrain Local
    /// 距离；只用于 Nearest Sample Fallback。
    /// </summary>
    private static bool TryFindNearestSample(
        List<TerrainChunk> affectedChunks,
        Vector3 localCenter,
        out TerrainChunk nearestChunk,
        out int nearestX,
        out int nearestY,
        out int nearestZ)
    {
        nearestChunk = null;
        nearestX = 0;
        nearestY = 0;
        nearestZ = 0;

        float bestDistanceSquared = float.MaxValue;

        for (int i = 0; i < affectedChunks.Count; i++)
        {
            TerrainChunk chunk = affectedChunks[i];
            if (chunk == null || chunk.Density == null) continue;

            DensityField densityField = chunk.Density;

            int x = GetNearestSampleIndex(
                localCenter.x,
                densityField.Origin.x,
                densityField.CellSizeX,
                densityField.SampleCountX
            );
            int y = GetNearestSampleIndex(
                localCenter.y,
                densityField.Origin.y,
                densityField.CellSizeY,
                densityField.SampleCountY
            );
            int z = GetNearestSampleIndex(
                localCenter.z,
                densityField.Origin.z,
                densityField.CellSizeZ,
                densityField.SampleCountZ
            );

            Vector3 samplePosition = densityField.GetLocalPosition(x, y, z);
            float distanceSquared = (samplePosition - localCenter).sqrMagnitude;

            if (distanceSquared >= bestDistanceSquared) continue;

            bestDistanceSquared = distanceSquared;
            nearestChunk = chunk;
            nearestX = x;
            nearestY = y;
            nearestZ = z;
        }

        return nearestChunk != null;
    }

    /// <summary>
    /// Terrain Local Position → 该轴最近的 Sample Index。
    /// 使用 DensityField 自己的 Origin / CellSize，不使用 VoxelSize。
    /// </summary>
    private static int GetNearestSampleIndex(
        float localPosition,
        float origin,
        float cellSize,
        int sampleCount)
    {
        if (sampleCount <= 0) return 0;

        float safeCellSize = Mathf.Max(0.000001f, cellSize);
        int index = Mathf.RoundToInt(
            (localPosition - origin) / safeCellSize
        );

        return Mathf.Clamp(index, 0, sampleCount - 1);
    }

    /// <summary>
    /// 根据 Modification Bounds 和 Sample spacing 计算该轴实际覆盖的 Sample Index 范围。
    /// Sample 位置 = Origin + Index × CellSize；范围是闭区间。
    /// 返回 false 表示该轴 Bounds 内没有 Sample，或 Bounds 完全在该轴 field 之外。
    /// </summary>
    private static bool TryGetSampleIndexRange(
        float boundsMin,
        float boundsMax,
        float origin,
        float cellSize,
        int sampleCount,
        out int minIndex,
        out int maxIndex)
    {
        minIndex = 0;
        maxIndex = 0;

        if (sampleCount <= 0) return false;

        float safeCellSize = Mathf.Max(0.000001f, cellSize);

        int rawMin = Mathf.CeilToInt(
            (boundsMin - origin) / safeCellSize - IndexEpsilon
        );
        int rawMax = Mathf.FloorToInt(
            (boundsMax - origin) / safeCellSize + IndexEpsilon
        );

        // Bounds 在该轴上没有覆盖任何 Sample。
        if (rawMin > rawMax) return false;

        // Bounds 完全在该轴 field 的 Sample 范围之外。
        if (rawMax < 0 || rawMin > sampleCount - 1) return false;

        minIndex = Mathf.Clamp(rawMin, 0, sampleCount - 1);
        maxIndex = Mathf.Clamp(rawMax, 0, sampleCount - 1);
        return true;
    }
}
