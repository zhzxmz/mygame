using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Terrain / World prototype controller。
/// 负责 Inspector 参数、Chunk 网格创建、坐标查询、调用 DensityFieldGenerator，
/// 并把每个 Chunk 的 Chunk-local Mesh 显示到独立 GameObject。
/// 地形数据统一使用 Terrain Local Space；Root Transform 负责世界空间放置。
/// 不把 Unity Mesh 当作源数据；DensityField + MaterialField 才是权威地形状态。
/// </summary>
[ExecuteAlways]
public class DensityTerrain : MonoBehaviour
{
    [Header("Terrain Size (Legacy Fallback)")]
    [Tooltip("Legacy：当 chunkVoxelSize / chunkSize 都未设置时，作为单个 Chunk 的 Terrain Local Space 尺寸")]
    public Vector3 terrainSize = new Vector3(64f, 32f, 64f);

    [Header("Chunk Grid")]
    [Tooltip("Legacy：Chunk 的 Terrain Local Space 尺寸；优先使用 chunkVoxelSize；任一轴 <= 0 时回退到 terrainSize")]
    public Vector3 chunkSize = Vector3.zero;

    [Tooltip("Chunk 数量，必须 >= 1")]
    public Vector3Int chunkCount = new Vector3Int(1, 1, 1);

    [Tooltip("Chunk 起始绝对整数坐标，可为负；只决定生成范围，不改变绝对坐标计算")]
    public Vector3Int chunkStartCoordinate = Vector3Int.zero;

    [Header("Voxel / Chunk Coordinate (V2)")]
    [Tooltip("临时实现值：一个逻辑 Voxel 在 Terrain Local Space 中的尺寸。Voxel != Density Sample。")]
    public float voxelSize = 0.1f;

    [Tooltip("临时实现值：每个 Chunk 每个轴的逻辑 Voxel 数量。任一轴 <= 0 时从 chunkSize / terrainSize 临时推导。ChunkWorldSize = VoxelSize × ChunkVoxelSize。")]
    public Vector3Int chunkVoxelSize = Vector3Int.zero;

    [Header("Density Field Sampling")]
    [Tooltip("每个 Chunk 内每个轴上的采样单元格数量；SampleCount = resolution + 1；与 VoxelSize 无关")]
    [Min(1)]
    public int resolution = 24;

    [Header("Density Mode")]
    public DensityGenerationMode densityMode = DensityGenerationMode.HeightField;

    [Header("Density")]
    public bool useNoise = true;

    [Tooltip("HeightField 基础地表高度（Terrain Local Y）")]
    public float height = 16f;

    [Tooltip("噪声频率")]
    public float noiseScale = 0.08f;

    [Tooltip("噪声强度")]
    public float noiseStrength = 4f;

    [Header("Sphere Cavity Debug")]
    [Tooltip("球形空洞在 Terrain Local Space 中的绝对球心；多 Chunk 时不会按 Chunk Origin 重复")]
    public Vector3 cavityCenterOffset = new Vector3(0f, 6f, 0f);

    [Tooltip("球形空洞半径")]
    public float cavityRadius = 3f;

    [Header("Material Generation")]
    [Tooltip("使用旧的 depth-based 简单规则")]
    public bool useLegacyMaterialRules = false;

    [Tooltip("Legacy：地表以下 Grass 判定厚度；v2：Grass 层最大厚度上限")]
    public float grassDepth = 1f;

    [Tooltip("Legacy：Grass 层以下 Dirt 判定厚度；v2 中保留兼容")]
    public float dirtDepth = 3f;

    [Header("Terrain Generation v2")]
    [Tooltip("风化强度：增加整体 Dirt 量")]
    public float weatheringStrength = 0.8f;

    [Tooltip("风化影响深度")]
    public float weatheringDepth = 8f;

    [Tooltip("缓坡角度阈值：小于此角度土壤完全稳定")]
    public float gentleSlopeAngle = 15f;

    [Tooltip("陡坡角度阈值：大于此角度土壤无法稳定")]
    public float steepSlopeAngle = 45f;

    [Tooltip("地貌重力强度：越大 Dirt 越容易向低处迁移")]
    public float gravityStrength = 1f;

    [Tooltip("世界生成时土壤重力沉积迭代次数")]
    public int soilSimulationIterations = 4;

    [Tooltip("草生长强度")]
    public float grassStrength = 1f;

    [Header("Debug Cutaway")]
    [Tooltip("仅用于调试：在第一个 HeightField Chunk 中切出一个垂直空气切面")]
    public bool debugCutaway = false;

    [Tooltip("切面中心在 Terrain Local Space 中的 X 坐标")]
    public float debugCutX = 0f;

    [Tooltip("切面宽度")]
    public float debugCutWidth = 4f;

    [Header("Rendering")]
    public Material grassMaterial;
    public Material dirtMaterial;
    public Material stoneMaterial;

    [Tooltip("在 Editor 中修改参数时自动重新生成 Mesh")]
    public bool regenerateInEditor = true;

    private const float IsoLevel = 0f;

    private class ChunkEntry
    {
        public TerrainChunk Chunk;
        public TerrainChunkView View;
        public GameObject GameObject;
        public MeshFilter MeshFilter;
        public MeshRenderer MeshRenderer;
    }

    private readonly List<ChunkEntry> chunks = new List<ChunkEntry>();
    private readonly Dictionary<Vector3Int, ChunkEntry> chunkLookup =
        new Dictionary<Vector3Int, ChunkEntry>();

    private Material[] runtimeMaterials = new Material[3];
    private Material[] materialArray;
    private bool loggedTemporaryVoxelConfig;

    public float EffectiveVoxelSize
    {
        get { return Mathf.Max(0.0001f, voxelSize); }
    }

    public Vector3Int EffectiveChunkVoxelSize
    {
        get { return GetEffectiveChunkVoxelSize(); }
    }

    public Vector3 EffectiveChunkWorldSize
    {
        get { return GetEffectiveChunkWorldSize(GetEffectiveChunkVoxelSize()); }
    }

#if UNITY_EDITOR
    private bool editorRegenerateQueued;
#endif

    private void OnEnable()
    {
        if (!gameObject.scene.IsValid()) return;
        RequestRegenerate();
    }

    private void OnValidate()
    {
        if (!regenerateInEditor) return;
        if (!isActiveAndEnabled) return;
        if (!gameObject.scene.IsValid()) return;

        RequestRegenerate();
    }

    /// <summary>
    /// Editor 下把重新生成延迟到 EditorApplication.delayCall，
    /// 避免在 OnValidate 生命周期里调用 DestroyImmediate。
    /// 运行时仍然直接 Regenerate。
    /// </summary>
    private void RequestRegenerate()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            if (editorRegenerateQueued) return;

            editorRegenerateQueued = true;
            UnityEditor.EditorApplication.delayCall += DelayedRegenerate;
            return;
        }
#endif

        Regenerate();
    }

#if UNITY_EDITOR
    private void DelayedRegenerate()
    {
        editorRegenerateQueued = false;

        if (this == null) return;
        if (!isActiveAndEnabled) return;
        if (!gameObject.scene.IsValid()) return;

        Regenerate();
    }
#endif

    private void OnDestroy()
    {
        ClearChunks();

        for (int i = 0; i < runtimeMaterials.Length; i++)
        {
            if (runtimeMaterials[i] == null) continue;

            if (Application.isPlaying)
            {
                Object.Destroy(runtimeMaterials[i]);
            }
            else
            {
                Object.DestroyImmediate(runtimeMaterials[i]);
            }

            runtimeMaterials[i] = null;
        }
    }

    [ContextMenu("Regenerate Terrain")]
    public void Regenerate()
    {
#if UNITY_EDITOR
        // 如果已经排队了 Editor 延迟生成，先取消，避免手动点击后重复生成。
        if (!Application.isPlaying && editorRegenerateQueued)
        {
            UnityEditor.EditorApplication.delayCall -= DelayedRegenerate;
            editorRegenerateQueued = false;
        }
#endif

        ClearChunks();

        float effectiveVoxelSize = EffectiveVoxelSize;
        Vector3Int effectiveChunkVoxelSize = GetEffectiveChunkVoxelSize();
        Vector3 effectiveChunkWorldSize = GetEffectiveChunkWorldSize(
            effectiveChunkVoxelSize
        );
        Vector3Int count = GetPositiveChunkCount();

        for (int x = 0; x < count.x; x++)
        {
            for (int y = 0; y < count.y; y++)
            {
                for (int z = 0; z < count.z; z++)
                {
                    Vector3Int coordinate = new Vector3Int(
                        chunkStartCoordinate.x + x,
                        chunkStartCoordinate.y + y,
                        chunkStartCoordinate.z + z
                    );

                    bool isFirstChunk = x == 0 && y == 0 && z == 0;
                    BuildChunk(
                        coordinate,
                        effectiveVoxelSize,
                        effectiveChunkVoxelSize,
                        effectiveChunkWorldSize,
                        isFirstChunk
                    );
                }
            }
        }
    }

    [ContextMenu("Mark First Chunk Dirty")]
    public void MarkFirstChunkDirty()
    {
        EnsureChunksForDebug();

        if (chunks.Count == 0) return;

        chunks[0].Chunk.MarkDirty();
        Debug.Log(
            $"[TerrainChunk] Marked dirty: {chunks[0].Chunk.Coordinate}"
        );
    }

    [ContextMenu("Rebuild Dirty Chunks")]
    public void RebuildDirtyChunks()
    {
        EnsureChunksForDebug();

        int rebuilt = 0;

        for (int i = 0; i < chunks.Count; i++)
        {
            ChunkEntry entry = chunks[i];
            if (entry.Chunk == null || !entry.Chunk.IsDirty) continue;

            if (!entry.Chunk.RebuildMesh()) continue;

            SyncChunkMesh(entry);

            rebuilt++;
        }

        Debug.Log($"[TerrainChunk] Rebuilt dirty chunks = {rebuilt}");
    }

    private void BuildChunk(
        Vector3Int coordinate,
        float effectiveVoxelSize,
        Vector3Int effectiveChunkVoxelSize,
        Vector3 chunkWorldSize,
        bool isFirstChunk)
    {
        TerrainChunk chunk = new TerrainChunk(
            coordinate,
            effectiveVoxelSize,
            effectiveChunkVoxelSize
        );

        // 地形数据使用 Terrain Local Space；World Generation 也只接收 Local 坐标。
        Vector3 terrainLocalOrigin = chunk.ChunkOrigin;

        MaterialField materialField;
        DensityField densityField = DensityFieldGenerator.Generate(
            densityMode,
            Mathf.Max(1, resolution),
            terrainLocalOrigin,
            chunkWorldSize,
            useNoise,
            height,
            noiseScale,
            noiseStrength,
            cavityCenterOffset,
            cavityRadius,
            grassDepth,
            dirtDepth,
            useLegacyMaterialRules,
            weatheringStrength,
            weatheringDepth,
            gentleSlopeAngle,
            steepSlopeAngle,
            gravityStrength,
            soilSimulationIterations,
            grassStrength,
            out materialField
        );

        // Debug Cutaway 只作用于第一个 Chunk，避免把多 Chunk 逻辑复杂化。
        if (isFirstChunk)
        {
            ApplyDebugCutaway(densityField);
        }

        chunk.Build(densityField, materialField, IsoLevel);

        GameObject chunkObject = new GameObject(
            $"Chunk_{coordinate.x}_{coordinate.y}_{coordinate.z}"
        );

        // Chunk 使用 DensityTerrain 根对象的 Layer，
        // 保证 Player 的 groundLayer 查询能命中运行时生成的 Terrain MeshCollider。
        chunkObject.layer = gameObject.layer;

        // Chunk GameObject / Mesh 都是运行时派生对象，不写入 Scene。
        // Scene 只保存 DensityTerrain 配置和 generation parameters。
        chunkObject.hideFlags = HideFlags.DontSave;

        chunkObject.transform.SetParent(transform, false);

        // Mesh 已经是 Chunk Local Space；
        // Chunk GameObject 只负责在 Terrain Local Space 中放置 Chunk，Root Transform 再负责世界空间。
        chunkObject.transform.localPosition = chunk.ChunkOrigin;
        chunkObject.transform.localRotation = Quaternion.identity;
        chunkObject.transform.localScale = Vector3.one;

        TerrainChunkView chunkView = chunkObject.AddComponent<TerrainChunkView>();
        chunkView.Coordinate = coordinate;
        chunkView.Chunk = chunk;

        MeshFilter chunkFilter = chunkObject.AddComponent<MeshFilter>();
        MeshRenderer chunkRenderer = chunkObject.AddComponent<MeshRenderer>();
        MeshCollider chunkCollider = chunkObject.AddComponent<MeshCollider>();

        chunkView.MeshFilter = chunkFilter;
        chunkView.MeshRenderer = chunkRenderer;
        chunkView.MeshCollider = chunkCollider;
        chunkView.SetMesh(chunk.Mesh);

        chunkRenderer.sharedMaterials = GetMaterials();

        ChunkEntry entry = new ChunkEntry
        {
            Chunk = chunk,
            View = chunkView,
            GameObject = chunkObject,
            MeshFilter = chunkFilter,
            MeshRenderer = chunkRenderer
        };

        chunks.Add(entry);
        chunkLookup[coordinate] = entry;
    }

    private void ClearChunks()
    {
        for (int i = 0; i < chunks.Count; i++)
        {
            ChunkEntry entry = chunks[i];

            if (entry.Chunk != null)
            {
                entry.Chunk.Dispose();
            }

            if (entry.GameObject != null)
            {
                DestroyChunkObject(entry.GameObject);
            }
        }

        chunks.Clear();
        chunkLookup.Clear();

        // Domain Reload / Scene Reload 后 chunks 会变成空列表，
        // 但旧的 Chunk GameObject 可能仍留在场景中。
        // 只清理 Terrain 自己管理的 Chunk：有 TerrainChunkView 标记，
        // 或旧版本生成的 Chunk_* + MeshFilter + MeshRenderer 对象。
        DestroyUntrackedChunkObjects();
    }

    private void DestroyUntrackedChunkObjects()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (child == null) continue;

            GameObject childObject = child.gameObject;
            if (childObject == null) continue;

            bool hasChunkView =
                childObject.GetComponent<TerrainChunkView>() != null;
            bool isLegacyChunk = IsLegacyChunkObject(childObject);

            if (!hasChunkView && !isLegacyChunk) continue;

            DestroyChunkObject(childObject);
        }
    }

    private static bool IsLegacyChunkObject(GameObject gameObject)
    {
        if (gameObject == null) return false;
        if (!gameObject.name.StartsWith("Chunk_")) return false;
        if (gameObject.GetComponent<MeshFilter>() == null) return false;
        if (gameObject.GetComponent<MeshRenderer>() == null) return false;

        return true;
    }

    private static void DestroyChunkObject(GameObject gameObject)
    {
        if (gameObject == null) return;

        if (Application.isPlaying)
        {
            Object.Destroy(gameObject);
        }
        else
        {
            Object.DestroyImmediate(gameObject);
        }
    }

    private static void SyncChunkMesh(ChunkEntry entry)
    {
        if (entry == null || entry.Chunk == null) return;

        if (entry.View != null && entry.View.MeshFilter != null)
        {
            entry.View.Chunk = entry.Chunk;
            entry.View.SetMesh(entry.Chunk.Mesh);
        }
        else if (entry.MeshFilter != null)
        {
            entry.MeshFilter.sharedMesh = entry.Chunk.Mesh;
        }
    }

    private Vector3 GetLegacyChunkSize()
    {
        if (chunkSize.x > 0f && chunkSize.y > 0f && chunkSize.z > 0f)
        {
            return chunkSize;
        }

        return new Vector3(
            Mathf.Max(0.01f, terrainSize.x),
            Mathf.Max(0.01f, terrainSize.y),
            Mathf.Max(0.01f, terrainSize.z)
        );
    }

    private Vector3Int GetEffectiveChunkVoxelSize()
    {
        Vector3Int configured = chunkVoxelSize;

        if (configured.x > 0 && configured.y > 0 && configured.z > 0)
        {
            return configured;
        }

        // 临时兼容：还没有显式配置 ChunkVoxelSize 时，从旧 chunkSize / terrainSize 推导。
        // VoxelSize / ChunkVoxelSize / DensityField Resolution 仍然是三个独立概念。
        Vector3 legacySize = GetLegacyChunkSize();
        float effectiveVoxelSize = EffectiveVoxelSize;

        Vector3Int derived = new Vector3Int(
            Mathf.Max(1, Mathf.RoundToInt(legacySize.x / effectiveVoxelSize)),
            Mathf.Max(1, Mathf.RoundToInt(legacySize.y / effectiveVoxelSize)),
            Mathf.Max(1, Mathf.RoundToInt(legacySize.z / effectiveVoxelSize))
        );

        if (!loggedTemporaryVoxelConfig)
        {
            loggedTemporaryVoxelConfig = true;

            Debug.LogWarning(
                "[DensityTerrain] chunkVoxelSize 未显式配置，使用临时推导值 " +
                $"{derived}（VoxelSize={effectiveVoxelSize}, LegacySize={legacySize}）"
            );
        }

        return derived;
    }

    private Vector3 GetEffectiveChunkWorldSize(Vector3Int effectiveChunkVoxelSize)
    {
        float size = EffectiveVoxelSize;

        return new Vector3(
            size * Mathf.Max(1, effectiveChunkVoxelSize.x),
            size * Mathf.Max(1, effectiveChunkVoxelSize.y),
            size * Mathf.Max(1, effectiveChunkVoxelSize.z)
        );
    }

    private Vector3Int GetPositiveChunkCount()
    {
        return new Vector3Int(
            Mathf.Max(1, chunkCount.x),
            Mathf.Max(1, chunkCount.y),
            Mathf.Max(1, chunkCount.z)
        );
    }

    /// <summary>Terrain Local Space 位置 → 逻辑 Voxel 坐标。</summary>
    public Vector3Int LocalToVoxel(Vector3 terrainLocalPosition)
    {
        return ChunkSpace.LocalToVoxel(terrainLocalPosition, EffectiveVoxelSize);
    }

    /// <summary>Terrain Local Space 位置 → 绝对 Chunk 坐标。</summary>
    public Vector3Int LocalToChunkCoordinate(Vector3 terrainLocalPosition)
    {
        return ChunkSpace.LocalToChunk(
            terrainLocalPosition,
            EffectiveVoxelSize,
            GetEffectiveChunkVoxelSize()
        );
    }

    /// <summary>World Space 位置 → Terrain Local Space 位置。</summary>
    public Vector3 WorldToLocalPosition(Vector3 worldPosition)
    {
        return transform.InverseTransformPoint(worldPosition);
    }

    /// <summary>World Space 位置 → Terrain Local Space 逻辑 Voxel 坐标。</summary>
    public Vector3Int WorldToVoxel(Vector3 worldPosition)
    {
        return ChunkSpace.WorldToVoxel(
            worldPosition,
            transform,
            EffectiveVoxelSize
        );
    }

    /// <summary>World Space 位置 → 绝对 Chunk 坐标。</summary>
    public Vector3Int WorldToChunkCoordinate(Vector3 worldPosition)
    {
        return ChunkSpace.WorldToChunk(
            worldPosition,
            transform,
            EffectiveVoxelSize,
            GetEffectiveChunkVoxelSize()
        );
    }

    /// <summary>World Space 位置 → Chunk 内部 Local Voxel 坐标。</summary>
    public Vector3Int WorldToLocalVoxel(Vector3 worldPosition)
    {
        return ChunkSpace.VoxelToLocal(
            WorldToVoxel(worldPosition),
            GetEffectiveChunkVoxelSize()
        );
    }

    /// <summary>World Space 位置 → 包含它的已生成 Chunk。</summary>
    public bool TryGetChunk(Vector3 worldPosition, out TerrainChunk chunk)
    {
        return TryGetChunk(WorldToChunkCoordinate(worldPosition), out chunk);
    }

    /// <summary>绝对 Chunk 坐标 → 已生成 Chunk。</summary>
    public bool TryGetChunk(Vector3Int chunkCoordinate, out TerrainChunk chunk)
    {
        if (chunkLookup.TryGetValue(chunkCoordinate, out ChunkEntry entry)
            && entry.Chunk != null)
        {
            chunk = entry.Chunk;
            return true;
        }

        chunk = null;
        return false;
    }

    /// <summary>绝对 Chunk 坐标 → Chunk 原点在世界空间中的位置。</summary>
    public Vector3 GetChunkWorldOrigin(Vector3Int chunkCoordinate)
    {
        return ChunkSpace.ChunkToWorldOrigin(
            chunkCoordinate,
            transform,
            EffectiveVoxelSize,
            GetEffectiveChunkVoxelSize()
        );
    }

    /// <summary>
    /// 收集与 Terrain Local Space bounds 相交或接触的所有已生成 Chunk。
    /// 使用保守的 AABB 接触规则；结果追加到 results。
    /// </summary>
    public int CollectChunksIntersectingLocalBounds(
        Bounds localBounds,
        List<TerrainChunk> results)
    {
        int count = 0;

        for (int i = 0; i < chunks.Count; i++)
        {
            ChunkEntry entry = chunks[i];
            if (entry.Chunk == null) continue;

            if (!IntersectsOrTouches(localBounds, entry.Chunk.LocalBounds))
            {
                continue;
            }

            if (results != null)
            {
                results.Add(entry.Chunk);
            }

            count++;
        }

        return count;
    }

    /// <summary>
    /// Debug / Programmatic Mining V1 入口。
    /// localCenter / size 使用 Terrain Local Space；size 是完整尺寸。
    /// 实际修改由 TerrainMining 完成，Mesh 更新复用现有 Dirty / Rebuild。
    /// </summary>
    public int MineCubeLocal(
        Vector3 localCenter,
        Vector3 size,
        bool rebuild = true)
    {
        return TerrainMining.MineCube(this, localCenter, size, rebuild);
    }

    private static bool IntersectsOrTouches(Bounds a, Bounds b)
    {
        return a.min.x <= b.max.x && a.max.x >= b.min.x
            && a.min.y <= b.max.y && a.max.y >= b.min.y
            && a.min.z <= b.max.z && a.max.z >= b.min.z;
    }

    private void ApplyDebugCutaway(DensityField densityField)
    {
        // 只用于 HeightField 调试；其他测试模式保持原样。
        if (!debugCutaway) return;
        if (densityMode != DensityGenerationMode.HeightField) return;

        float width = Mathf.Max(0f, debugCutWidth);
        if (width <= 0f) return;

        float halfWidth = width * 0.5f;
        float cutMinX = debugCutX - halfWidth;
        float cutMaxX = debugCutX + halfWidth;

        int minIndex = Mathf.FloorToInt(
            (cutMinX - densityField.Origin.x) / densityField.CellSizeX
        );
        int maxIndex = Mathf.CeilToInt(
            (cutMaxX - densityField.Origin.x) / densityField.CellSizeX
        );

        minIndex = Mathf.Clamp(minIndex, 0, densityField.SampleCountX - 1);
        maxIndex = Mathf.Clamp(maxIndex, minIndex, densityField.SampleCountX - 1);

        for (int x = minIndex; x <= maxIndex; x++)
        {
            for (int y = 0; y < densityField.SampleCountY; y++)
            {
                for (int z = 0; z < densityField.SampleCountZ; z++)
                {
                    densityField[x, y, z] = -1f;
                }
            }
        }
    }

    [ContextMenu("Log Density Summary")]
    public void LogDensitySummary()
    {
        EnsureChunksForDebug();

        for (int i = 0; i < chunks.Count; i++)
        {
            ChunkEntry entry = chunks[i];
            DensityField densityField = entry.Chunk.Density;

            if (densityField == null) continue;

            float min = float.MaxValue;
            float max = float.MinValue;
            double sum = 0.0;
            long solid = 0;
            long air = 0;

            for (int x = 0; x < densityField.SampleCountX; x++)
            {
                for (int y = 0; y < densityField.SampleCountY; y++)
                {
                    for (int z = 0; z < densityField.SampleCountZ; z++)
                    {
                        float density = densityField[x, y, z];

                        if (density < min) min = density;
                        if (density > max) max = density;

                        sum += density;

                        if (density > IsoLevel) solid++;
                        else air++;
                    }
                }
            }

            long total = solid + air;
            float average = total > 0 ? (float)(sum / total) : 0f;
            float solidRatio = total > 0 ? (float)solid / total : 0f;

            Debug.Log(
                $"[DensityField Chunk {entry.Chunk.Coordinate}]\n" +
                $"Min = {min}\n" +
                $"Max = {max}\n" +
                $"Average = {average}\n" +
                $"Solid = {solid}\n" +
                $"Air = {air}\n" +
                $"Total = {total}\n" +
                $"Solid Ratio = {solidRatio}"
            );
        }
    }

    [ContextMenu("Log Mesh Summary")]
    public void LogMeshSummary()
    {
        EnsureChunksForDebug();

        for (int index = 0; index < chunks.Count; index++)
        {
            ChunkEntry entry = chunks[index];
            Mesh mesh = entry.Chunk.Mesh;
            if (mesh == null) continue;

            int subMeshCount = mesh.subMeshCount;
            int subMesh0IndexCount = subMeshCount > 0 ? mesh.GetTriangles(0).Length : 0;
            int subMesh1IndexCount = subMeshCount > 1 ? mesh.GetTriangles(1).Length : 0;
            int subMesh2IndexCount = subMeshCount > 2 ? mesh.GetTriangles(2).Length : 0;

            Bounds bounds = mesh.bounds;
            Vector3[] vertices = mesh.vertices;
            Vector3 vertexMin = Vector3.zero;
            Vector3 vertexMax = Vector3.zero;

            if (vertices != null && vertices.Length > 0)
            {
                vertexMin = vertices[0];
                vertexMax = vertices[0];

                for (int i = 1; i < vertices.Length; i++)
                {
                    Vector3 v = vertices[i];
                    if (v.x < vertexMin.x) vertexMin.x = v.x;
                    if (v.y < vertexMin.y) vertexMin.y = v.y;
                    if (v.z < vertexMin.z) vertexMin.z = v.z;
                    if (v.x > vertexMax.x) vertexMax.x = v.x;
                    if (v.y > vertexMax.y) vertexMax.y = v.y;
                    if (v.z > vertexMax.z) vertexMax.z = v.z;
                }
            }

            Material[] materials = entry.MeshRenderer != null
                ? entry.MeshRenderer.sharedMaterials
                : null;

            string material0 = materials != null && materials.Length > 0 && materials[0] != null
                ? materials[0].name
                : "null";
            string material1 = materials != null && materials.Length > 1 && materials[1] != null
                ? materials[1].name
                : "null";
            string material2 = materials != null && materials.Length > 2 && materials[2] != null
                ? materials[2].name
                : "null";

            bool filterMatches = entry.MeshFilter != null
                && entry.MeshFilter.sharedMesh == mesh;

            Debug.Log(
                $"[TerrainMesh Chunk {entry.Chunk.Coordinate}]\n" +
                $"Mesh exists = {mesh != null}\n\n" +
                $"Vertex Count = {mesh.vertexCount}\n" +
                $"SubMesh Count = {subMeshCount}\n\n" +
                "SubMesh 0:\n" +
                $"Triangle Index Count = {subMesh0IndexCount}\n\n" +
                "SubMesh 1:\n" +
                $"Triangle Index Count = {subMesh1IndexCount}\n\n" +
                "SubMesh 2:\n" +
                $"Triangle Index Count = {subMesh2IndexCount}\n\n" +
                "Chunk Local Bounds:\n" +
                $"center = {bounds.center}\n" +
                $"size = {bounds.size}\n" +
                $"min = {bounds.min}\n" +
                $"max = {bounds.max}\n\n" +
                $"Chunk Origin (Terrain Local) = {entry.Chunk.ChunkOrigin}\n" +
                $"Chunk World Origin = {entry.Chunk.GetWorldOrigin(transform)}\n\n" +
                $"Vertex Min (Chunk Local) = {vertexMin}\n" +
                $"Vertex Max (Chunk Local) = {vertexMax}\n\n" +
                $"MeshFilter.sharedMesh == chunk.Mesh = {filterMatches}\n\n" +
                $"MeshRenderer.enabled = {(entry.MeshRenderer != null ? entry.MeshRenderer.enabled.ToString() : "meshRenderer null")}\n\n" +
                $"Shared Materials Count = {(materials != null ? materials.Length : 0)}\n" +
                $"Material[0] = {material0}\n" +
                $"Material[1] = {material1}\n" +
                $"Material[2] = {material2}\n\n" +
                $"GameObject activeSelf = {entry.GameObject.activeSelf}\n" +
                $"GameObject activeInHierarchy = {entry.GameObject.activeInHierarchy}"
            );
        }
    }

    [ContextMenu("Validate Chunk Boundaries")]
    public void ValidateChunkBoundaries()
    {
        EnsureChunksForDebug();

        long pairsChecked = 0;
        long positionMismatches = 0;
        long densityMismatches = 0;
        long materialMismatches = 0;

        Vector3Int[] neighborOffsets = new Vector3Int[]
        {
            new Vector3Int(1, 0, 0),
            new Vector3Int(0, 1, 0),
            new Vector3Int(0, 0, 1)
        };

        for (int i = 0; i < chunks.Count; i++)
        {
            ChunkEntry entry = chunks[i];
            TerrainChunk chunkA = entry.Chunk;

            for (int n = 0; n < neighborOffsets.Length; n++)
            {
                Vector3Int neighborCoordinate = new Vector3Int(
                    chunkA.Coordinate.x + neighborOffsets[n].x,
                    chunkA.Coordinate.y + neighborOffsets[n].y,
                    chunkA.Coordinate.z + neighborOffsets[n].z
                );

                if (!chunkLookup.TryGetValue(neighborCoordinate, out ChunkEntry neighbor))
                {
                    continue;
                }

                TerrainChunk chunkB = neighbor.Chunk;

                if (neighborOffsets[n].x == 1)
                {
                    CheckBoundaryX(chunkA, chunkB, ref pairsChecked, ref positionMismatches, ref densityMismatches, ref materialMismatches);
                }
                else if (neighborOffsets[n].y == 1)
                {
                    CheckBoundaryY(chunkA, chunkB, ref pairsChecked, ref positionMismatches, ref densityMismatches, ref materialMismatches);
                }
                else
                {
                    CheckBoundaryZ(chunkA, chunkB, ref pairsChecked, ref positionMismatches, ref densityMismatches, ref materialMismatches);
                }
            }
        }

        bool pass = positionMismatches == 0 && densityMismatches == 0;

        Debug.Log(
            "[ChunkBoundary]\n" +
            $"Pairs Checked = {pairsChecked}\n" +
            $"Position Mismatches = {positionMismatches}\n" +
            $"Density Mismatches = {densityMismatches}\n" +
            $"Material Mismatches = {materialMismatches} (informational)\n" +
            $"Result = {(pass ? "PASS" : "FAIL")}"
        );
    }

    private void CheckBoundaryX(
        TerrainChunk a,
        TerrainChunk b,
        ref long pairs,
        ref long positionMismatches,
        ref long densityMismatches,
        ref long materialMismatches)
    {
        int ax = a.Density.SampleCountX - 1;

        for (int y = 0; y < a.Density.SampleCountY; y++)
        {
            for (int z = 0; z < a.Density.SampleCountZ; z++)
            {
                CompareBoundarySample(
                    a.Density.GetLocalPosition(ax, y, z),
                    b.Density.GetLocalPosition(0, y, z),
                    a.Density[ax, y, z],
                    b.Density[0, y, z],
                    a.Material[ax, y, z],
                    b.Material[0, y, z],
                    ref pairs,
                    ref positionMismatches,
                    ref densityMismatches,
                    ref materialMismatches
                );
            }
        }
    }

    private void CheckBoundaryY(
        TerrainChunk a,
        TerrainChunk b,
        ref long pairs,
        ref long positionMismatches,
        ref long densityMismatches,
        ref long materialMismatches)
    {
        int ay = a.Density.SampleCountY - 1;

        for (int x = 0; x < a.Density.SampleCountX; x++)
        {
            for (int z = 0; z < a.Density.SampleCountZ; z++)
            {
                CompareBoundarySample(
                    a.Density.GetLocalPosition(x, ay, z),
                    b.Density.GetLocalPosition(x, 0, z),
                    a.Density[x, ay, z],
                    b.Density[x, 0, z],
                    a.Material[x, ay, z],
                    b.Material[x, 0, z],
                    ref pairs,
                    ref positionMismatches,
                    ref densityMismatches,
                    ref materialMismatches
                );
            }
        }
    }

    private void CheckBoundaryZ(
        TerrainChunk a,
        TerrainChunk b,
        ref long pairs,
        ref long positionMismatches,
        ref long densityMismatches,
        ref long materialMismatches)
    {
        int az = a.Density.SampleCountZ - 1;

        for (int x = 0; x < a.Density.SampleCountX; x++)
        {
            for (int y = 0; y < a.Density.SampleCountY; y++)
            {
                CompareBoundarySample(
                    a.Density.GetLocalPosition(x, y, az),
                    b.Density.GetLocalPosition(x, y, 0),
                    a.Density[x, y, az],
                    b.Density[x, y, 0],
                    a.Material[x, y, az],
                    b.Material[x, y, 0],
                    ref pairs,
                    ref positionMismatches,
                    ref densityMismatches,
                    ref materialMismatches
                );
            }
        }
    }

    private void CompareBoundarySample(
        Vector3 localA,
        Vector3 localB,
        float densityA,
        float densityB,
        MaterialType materialA,
        MaterialType materialB,
        ref long pairs,
        ref long positionMismatches,
        ref long densityMismatches,
        ref long materialMismatches)
    {
        pairs++;

        if (Vector3.Distance(localA, localB) > 0.001f)
        {
            positionMismatches++;
        }

        if (Mathf.Abs(densityA - densityB) > 0.0001f)
        {
            densityMismatches++;
        }

        if (materialA != materialB)
        {
            materialMismatches++;
        }
    }

    private void EnsureChunksForDebug()
    {
        if (chunks.Count == 0)
        {
            Regenerate();
        }
    }

    [ContextMenu("Log Material At Center")]
    public void LogMaterialAtCenter()
    {
        EnsureChunksForDebug();

        for (int i = 0; i < chunks.Count; i++)
        {
            ChunkEntry entry = chunks[i];
            MaterialField materialField = entry.Chunk.Material;
            if (materialField == null) continue;

            Vector3 center = materialField.Origin + materialField.Size * 0.5f;
            LogMaterialAtLocalPoint(materialField, center, $"Chunk {entry.Chunk.Coordinate} Center");

            float step = Mathf.Max(1f, materialField.Size.y / 8f);
            for (float y = materialField.Origin.y + materialField.Size.y;
                 y >= materialField.Origin.y;
                 y -= step)
            {
                Vector3 point = new Vector3(center.x, y, center.z);
                LogMaterialAtLocalPoint(materialField, point, $"Chunk {entry.Chunk.Coordinate} Column");
            }
        }
    }

    [ContextMenu("Log Material Summary")]
    public void LogMaterialSummary()
    {
        EnsureChunksForDebug();

        int air = 0;
        int grass = 0;
        int dirt = 0;
        int stone = 0;

        for (int i = 0; i < chunks.Count; i++)
        {
            MaterialField materialField = chunks[i].Chunk.Material;
            if (materialField == null) continue;

            for (int x = 0; x < materialField.SampleCountX; x++)
            {
                for (int y = 0; y < materialField.SampleCountY; y++)
                {
                    for (int z = 0; z < materialField.SampleCountZ; z++)
                    {
                        switch (materialField[x, y, z])
                        {
                            case MaterialType.Air: air++; break;
                            case MaterialType.Grass: grass++; break;
                            case MaterialType.Dirt: dirt++; break;
                            case MaterialType.Stone: stone++; break;
                        }
                    }
                }
            }
        }

        Debug.Log($"[MaterialField AllChunks] Air={air}, Grass={grass}, Dirt={dirt}, Stone={stone}");
    }

    private void LogMaterialAtLocalPoint(
        MaterialField materialField,
        Vector3 terrainLocalPosition,
        string label)
    {
        int x = Mathf.Clamp(
            Mathf.RoundToInt((terrainLocalPosition.x - materialField.Origin.x) / materialField.CellSizeX),
            0,
            materialField.SampleCountX - 1
        );

        int y = Mathf.Clamp(
            Mathf.RoundToInt((terrainLocalPosition.y - materialField.Origin.y) / materialField.CellSizeY),
            0,
            materialField.SampleCountY - 1
        );

        int z = Mathf.Clamp(
            Mathf.RoundToInt((terrainLocalPosition.z - materialField.Origin.z) / materialField.CellSizeZ),
            0,
            materialField.SampleCountZ - 1
        );

        Debug.Log(
            $"[MaterialField] {label} local={terrainLocalPosition} index=({x},{y},{z}) material={materialField[x, y, z]}"
        );
    }

    private Material[] GetMaterials()
    {
        if (materialArray == null)
        {
            materialArray = new Material[3];
        }

        materialArray[0] = grassMaterial != null
            ? grassMaterial
            : GetRuntimeMaterial(0, new Color(0.35f, 0.65f, 0.25f), "Runtime Grass Material");

        materialArray[1] = dirtMaterial != null
            ? dirtMaterial
            : GetRuntimeMaterial(1, new Color(0.45f, 0.30f, 0.18f), "Runtime Dirt Material");

        materialArray[2] = stoneMaterial != null
            ? stoneMaterial
            : GetRuntimeMaterial(2, new Color(0.55f, 0.55f, 0.55f), "Runtime Stone Material");

        return materialArray;
    }

    private Material GetRuntimeMaterial(int index, Color color, string materialName)
    {
        if (runtimeMaterials[index] != null)
        {
            return runtimeMaterials[index];
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null) shader = Shader.Find("Sprites/Default");

        if (shader == null)
        {
            Debug.LogWarning("DensityTerrain: 找不到可用的 Shader，无法创建默认地形材质");
            return null;
        }

        Material material = new Material(shader);
        material.name = materialName;
        material.hideFlags = HideFlags.DontSave;

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }
        else if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }

        runtimeMaterials[index] = material;
        return material;
    }
}
