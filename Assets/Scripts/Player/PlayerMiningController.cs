using System.Collections.Generic;
using System.Text;
using TerrainModificationV2;
using UnityEngine;

/// <summary>
/// Player Mining / Targeting。
/// - 使用 Player Camera 做 Physics.Raycast
/// - 目标必须是现有 Terrain MeshCollider（TerrainChunkView）
/// - RaycastHit.point 通过 DensityTerrain.WorldToLocalPosition 转为 Terrain Local Space
/// - V2 F 键：TerrainVoxelSelector → SelectedVoxel → DensityTerrain.VoxelField.Set(Air)
/// - Step 3D：VoxelField 写入后由 DensityTerrain 重建受影响 Chunk 的 Mesh / MeshCollider
/// - 旧 TryMine()（V1 Density Brush）保留供兼容/测试调用，F 键不再走该路径
/// 新 Voxel Mining 路径不直接修改 DensityField / MaterialField / Mesh / MeshCollider。
/// </summary>
[DisallowMultipleComponent]
public class PlayerMiningController : MonoBehaviour
{
    [Header("Targeting")]
    [Tooltip("Raycast 使用的 Camera；留空时自动使用 Camera.main 或子物体 Camera")]
    public Camera targetCamera;

    [Tooltip("Raycast 最大距离")]
    public float maxDistance = 100f;

    [Tooltip("Raycast 层级；默认全部层，命中后仍需是 TerrainChunkView")]
    public LayerMask raycastMask = ~0;

    [Header("Terrain")]
    [Tooltip("目标 DensityTerrain；留空时自动查找场景中的 DensityTerrain")]
    public DensityTerrain terrain;

    [Header("Mining")]
    [Tooltip("Debug Mining 按键")]
    public KeyCode miningKey = KeyCode.F;

    [Tooltip("V1 Cube Brush 完整尺寸（Terrain Local Space）")]
    public Vector3 brushSize = new Vector3(2f, 2f, 2f);

    [Header("Debug")]
    public bool logMining;

    [Tooltip("每次按 F 输出一次 [TerrainVoxelMiningDiagnostic] 完整诊断日志")]
    public bool logVoxelMiningDiagnostic = true;

    [Header("Debug Targeting")]
    [Tooltip("在 Scene / Game 视图中绘制 RaycastHit 标记")]
    public bool debugDrawTargeting = true;

    [Tooltip("在屏幕上显示 Hit 的 Chunk / Triangle 信息")]
    public bool debugShowHitInfo = true;

    [Tooltip("Hit 信息变化时输出到 Console")]
    public bool debugLogHitInfo = false;

    [Header("Voxel Highlight (V2)")]
    [Tooltip("是否显示当前 Raycast 命中 Voxel 的 1m³ 线框")]
    public bool showVoxelHighlight = true;

    [Tooltip("留空时自动在 Player 下创建复用的 Highlight 对象")]
    public TerrainVoxelHighlight voxelHighlight;

    [Tooltip("Voxel 高亮线框颜色")]
    public Color voxelHighlightColor = new Color(1f, 1f, 0.2f, 0.95f);

    [Tooltip("Voxel 高亮线框宽度")]
    public float voxelHighlightLineWidth = 0.03f;

    /// <summary>当前帧 Raycast 选择出的唯一 Voxel Mining 目标。</summary>
    private bool hasSelectedVoxel;
    private Vector3Int selectedVoxel;
    private DensityTerrain selectedTerrain;

    // 选择时捕获的命中点/法线（World Space），用于挖掘前重新验证目标。
    private Vector3 selectedRaycastHitWorld;
    private Vector3 selectedRaycastHitNormal;
    private Vector3 selectedTerrainLocalPoint;

    public bool HasSelectedVoxel
    {
        get { return hasSelectedVoxel; }
    }

    public Vector3Int SelectedVoxel
    {
        get { return selectedVoxel; }
    }

    public DensityTerrain SelectedTerrain
    {
        get { return selectedTerrain; }
    }

    private bool hasDebugHit;
    private RaycastHit debugHit;
    private string debugHitInfo = string.Empty;
    private string lastLoggedHitInfo = string.Empty;

    private int cachedTriangleMeshId;
    private int cachedTriangleIndex = -1;
    private string cachedTriangleIndices = "n/a";
    private string cachedSubMeshIndex = "n/a";

    private void Update()
    {
        if (debugDrawTargeting || debugShowHitInfo || debugLogHitInfo)
        {
            UpdateDebugTarget();
        }

        UpdateVoxelSelectionAndHighlight();

        if (MouseLock.IsUIBlocking) return;
        if (!Input.GetKeyDown(miningKey)) return;

        TryMineVoxel();
    }

    /// <summary>
    /// Legacy V1 Targeting + Mining 闭环（保留，不再由 F 键调用）。
    /// 供兼容调用与自动验证使用。
    /// </summary>
    public bool TryMine()
    {
        if (!TryGetTerrainHit(out RaycastHit hit, out TerrainChunkView _))
        {
            if (logMining)
            {
                Debug.Log("[PlayerMining] No terrain hit.");
            }

            return false;
        }

        DensityTerrain resolvedTerrain = ResolveTerrain();
        if (resolvedTerrain == null)
        {
            if (logMining)
            {
                Debug.LogWarning("[PlayerMining] No DensityTerrain.");
            }

            return false;
        }

        Vector3 localPoint = resolvedTerrain.WorldToLocalPosition(hit.point);
        int modifiedSamples = resolvedTerrain.MineCubeLocal(localPoint, brushSize);

        if (logMining)
        {
            Debug.Log(
                "[PlayerMining] hit=" + hit.point +
                " local=" + localPoint +
                " modifiedSamples=" + modifiedSamples
            );
        }

        return modifiedSamples > 0;
    }

    /// <summary>
    /// Targeting only：Camera Physics.Raycast，目标必须是 Terrain MeshCollider。
    /// 不修改 Terrain 数据。
    /// </summary>
    public bool TryGetTerrainHit(out RaycastHit hit, out TerrainChunkView view)
    {
        hit = default(RaycastHit);
        view = null;

        Camera camera = ResolveCamera();
        if (camera == null) return false;

        Ray ray = new Ray(camera.transform.position, camera.transform.forward);

        if (!Physics.Raycast(
            ray,
            out hit,
            maxDistance,
            raycastMask,
            QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        if (hit.collider == null) return false;

        view = hit.collider.GetComponentInParent<TerrainChunkView>();
        return view != null;
    }

    /// <summary>
    /// Step 3B/3C：更新 SelectedVoxel 的唯一来源，并同步 Highlight。
    /// F 键挖掘直接使用这里保存的 selectedVoxel / selectedTerrain，
    /// 不会在按下时重新 Raycast 计算另一套坐标。
    /// 没有有效选择时清空状态并 Hide。
    /// </summary>
    private void UpdateVoxelSelectionAndHighlight()
    {
        if (TryGetTerrainHit(out RaycastHit hit, out TerrainChunkView _))
        {
            DensityTerrain resolvedTerrain = ResolveTerrain();
            string resolveReason = string.Empty;

            // A 阶段：不再使用“命中点所在 Voxel”，而是解析到**实体侧** Voxel，
            // 并用基础密度（材料）+ 编辑状态（是否已挖空）做有效性校验。
            if (resolvedTerrain != null &&
                TerrainVoxelSelector.TryResolveMineTarget(
                    hit.point,
                    hit.normal,
                    resolvedTerrain,
                    out Vector3Int voxelCoordinate,
                    out resolveReason))
            {
                hasSelectedVoxel = true;
                selectedVoxel = voxelCoordinate;
                selectedTerrain = resolvedTerrain;
                selectedRaycastHitWorld = hit.point;
                selectedRaycastHitNormal = hit.normal;
                selectedTerrainLocalPoint =
                    resolvedTerrain.WorldToLocalPosition(hit.point);

                if (showVoxelHighlight)
                {
                    TerrainVoxelHighlight highlight = ResolveVoxelHighlight();
                    if (highlight != null)
                    {
                        highlight.Initialize(resolvedTerrain.transform);
                        highlight.Show(
                            voxelCoordinate,
                            resolvedTerrain.VoxelField.VoxelSize
                        );
                    }
                }
                else if (voxelHighlight != null)
                {
                    voxelHighlight.Hide();
                }

                return;
            }
        }

        ClearSelectedVoxel();

        if (voxelHighlight != null)
        {
            voxelHighlight.Hide();
        }
    }

    /// <summary>
    /// Step 3C/3D：F 键正式入口。
    /// 只把当前 SelectedVoxel 在 SelectedTerrain.VoxelField 中设置为 Air，
    /// 然后由 SelectedTerrain 重建受影响 Chunk 的 Effective Density / Mesh /
    /// MeshFilter / MeshCollider。
    /// 不调用旧 Mining，不直接修改 Density/Material/Mesh/Collider。
    /// 没有有效 SelectedVoxel 时什么都不做并返回 false。
    /// </summary>
    public bool TryMineVoxel()
    {
        if (!hasSelectedVoxel ||
            selectedTerrain == null ||
            selectedTerrain.VoxelField == null)
        {
            return false;
        }

        // A 阶段：挖掘前重新确认当前目标仍然有效（防止过期的 selectedVoxel 被使用）；
        // 重新解析出的坐标作为本次挖掘目标；无效 ⇒ 清除选择与高亮并返回（不写 Air、不空转重建）。
        string validateReason = string.Empty;
        Vector3Int validatedVoxel;
        if (!TerrainVoxelSelector.TryResolveMineTarget(
            selectedRaycastHitWorld,
            selectedRaycastHitNormal,
            selectedTerrain,
            out validatedVoxel,
            out validateReason))
        {
            if (logMining)
            {
                Debug.Log(
                    "[PlayerMining] Selection invalid, mine skipped: " +
                    validateReason + " selectedVoxel=" + selectedVoxel
                );
            }

            ClearSelectedVoxel();
            return false;
        }

        selectedVoxel = validatedVoxel;

        VoxelField targetVoxelField = selectedTerrain.VoxelField;
        VoxelState previousState = targetVoxelField.Get(selectedVoxel);

        List<TerrainChunkView> diagnosticChunks = null;
        int[] diagnosticVertexCountsBefore = null;
        int[] diagnosticIndexCountsBefore = null;

        if (logVoxelMiningDiagnostic)
        {
            diagnosticChunks = CollectDiagnosticAffectedChunks();
            CaptureDiagnosticMeshCounts(
                diagnosticChunks,
                out diagnosticVertexCountsBefore,
                out diagnosticIndexCountsBefore
            );
        }

        targetVoxelField.Set(selectedVoxel, VoxelState.Air);
        VoxelState afterState = targetVoxelField.Get(selectedVoxel);

        int rebuiltChunks = 0;
        if (previousState != VoxelState.Air)
        {
            rebuiltChunks = selectedTerrain.RebuildChunksAffectedByVoxel(
                selectedVoxel
            );
        }

        if (logVoxelMiningDiagnostic)
        {
            DiagnoseVoxelMining(
                previousState,
                afterState,
                rebuiltChunks,
                diagnosticChunks,
                diagnosticVertexCountsBefore,
                diagnosticIndexCountsBefore
            );
        }

        if (logMining)
        {
            Debug.Log(
                "[PlayerMining] VoxelMine " +
                "terrain=" + selectedTerrain.name +
                " voxel=" + selectedVoxel +
                " state=" + previousState + " -> Air" +
                " rebuiltChunks=" + rebuiltChunks
            );
        }

        // A 阶段：一次挖掘之后必须重新验证目标（不新增 Raycast，使用捕获的命中点/法线）。
        // 仍然有效 ⇒ 刷新为新的实体侧 Voxel 并同步高亮；否则清除选择（下一帧正常选择更新）。
        string refreshReason = string.Empty;
        Vector3Int refreshedVoxel;
        if (TerrainVoxelSelector.TryResolveMineTarget(
            selectedRaycastHitWorld,
            selectedRaycastHitNormal,
            selectedTerrain,
            out refreshedVoxel,
            out refreshReason))
        {
            selectedVoxel = refreshedVoxel;

            if (showVoxelHighlight && voxelHighlight != null)
            {
                voxelHighlight.Show(
                    refreshedVoxel,
                    selectedTerrain.VoxelField.VoxelSize
                );
            }
        }
        else
        {
            ClearSelectedVoxel();
        }

        return true;
    }

    private List<TerrainChunkView> CollectDiagnosticAffectedChunks()
    {
        List<TerrainChunkView> result = new List<TerrainChunkView>();
        if (selectedTerrain == null) return result;

        Vector3 voxelMin = selectedTerrain.VoxelField.GetVoxelMin(selectedVoxel);
        Vector3 voxelMax = selectedTerrain.VoxelField.GetVoxelMax(selectedVoxel);

        TerrainChunkView[] views =
            selectedTerrain.GetComponentsInChildren<TerrainChunkView>(true);

        for (int i = 0; i < views.Length; i++)
        {
            TerrainChunkView view = views[i];
            if (view == null || view.Chunk == null) continue;

            Bounds bounds = view.Chunk.LocalBounds;

            bool affected =
                bounds.max.x >= voxelMin.x && bounds.min.x < voxelMax.x &&
                bounds.max.y >= voxelMin.y && bounds.min.y < voxelMax.y &&
                bounds.max.z >= voxelMin.z && bounds.min.z < voxelMax.z;

            if (affected)
            {
                result.Add(view);
            }
        }

        return result;
    }

    private static void CaptureDiagnosticMeshCounts(
        List<TerrainChunkView> views,
        out int[] vertexCounts,
        out int[] indexCounts)
    {
        int count = views != null ? views.Count : 0;
        vertexCounts = new int[count];
        indexCounts = new int[count];

        for (int i = 0; i < count; i++)
        {
            TerrainChunkView view = views[i];
            Mesh mesh = view != null && view.Chunk != null
                ? view.Chunk.Mesh
                : null;

            vertexCounts[i] = mesh != null ? mesh.vertexCount : 0;
            indexCounts[i] = TotalIndexCount(mesh);
        }
    }

    /// <summary>
    /// 计算“可能映射到该 Voxel”的 Density 样本索引窗口（含 ±1 安全边距）。
    /// 与 VoxelField.PositionToVoxel / DensityField.GetLocalPosition 的既有契约一致：
    /// samplePosition ∈ [voxelMin, voxelMax) 的样本才可能映射到该 Voxel。
    /// </summary>
    private static void ComputeSampleIndexWindow(
        DensityField field,
        Vector3 voxelMin,
        Vector3 voxelMax,
        out int x0,
        out int x1,
        out int y0,
        out int y1,
        out int z0,
        out int z1)
    {
        x0 = ClampIndex(
            Mathf.FloorToInt((voxelMin.x - field.Origin.x) / field.CellSizeX) - 1,
            field.SampleCountX);
        x1 = ClampIndex(
            Mathf.CeilToInt((voxelMax.x - field.Origin.x) / field.CellSizeX) + 1,
            field.SampleCountX);
        y0 = ClampIndex(
            Mathf.FloorToInt((voxelMin.y - field.Origin.y) / field.CellSizeY) - 1,
            field.SampleCountY);
        y1 = ClampIndex(
            Mathf.CeilToInt((voxelMax.y - field.Origin.y) / field.CellSizeY) + 1,
            field.SampleCountY);
        z0 = ClampIndex(
            Mathf.FloorToInt((voxelMin.z - field.Origin.z) / field.CellSizeZ) - 1,
            field.SampleCountZ);
        z1 = ClampIndex(
            Mathf.CeilToInt((voxelMax.z - field.Origin.z) / field.CellSizeZ) + 1,
            field.SampleCountZ);
    }

    private static int ClampIndex(int index, int sampleCount)
    {
        if (index < 0) return 0;
        if (index > sampleCount - 1) return sampleCount - 1;
        return index;
    }
    private void DiagnoseVoxelMining(
        VoxelState previousState,
        VoxelState afterState,
        int rebuiltChunks,
        List<TerrainChunkView> affectedChunks,
        int[] vertexCountsBefore,
        int[] indexCountsBefore)
    {
        StringBuilder log = new StringBuilder(4096);

        log.AppendLine("[TerrainVoxelMiningDiagnostic]");
        log.AppendLine("Selection");
        log.AppendLine(
            "  Terrain=" + (selectedTerrain != null ? selectedTerrain.name : "null")
        );
        log.AppendLine("  SelectedVoxel=" + selectedVoxel);
        log.AppendLine("  RaycastHitWorld=" + FormatVector(selectedRaycastHitWorld));
        log.AppendLine("  TerrainLocal=" + FormatVector(selectedTerrainLocalPoint));

        Vector3 voxelMin = selectedTerrain != null
            ? selectedTerrain.VoxelField.GetVoxelMin(selectedVoxel)
            : Vector3.zero;
        Vector3 voxelMax = selectedTerrain != null
            ? selectedTerrain.VoxelField.GetVoxelMax(selectedVoxel)
            : Vector3.zero;

        log.AppendLine("  VoxelLocalMin=" + FormatVector(voxelMin));
        log.AppendLine("  VoxelLocalMax=" + FormatVector(voxelMax));

        log.AppendLine("Voxel");
        log.AppendLine("  Before=" + previousState);
        log.AppendLine("  After=" + afterState);
        log.AppendLine("  IsAirAfter=" + (afterState == VoxelState.Air));

        log.AppendLine("AffectedChunks");
        int affectedCount = affectedChunks != null ? affectedChunks.Count : 0;
        log.AppendLine("  Count=" + affectedCount);
        log.Append("  Chunks=[");
        for (int i = 0; i < affectedCount; i++)
        {
            if (i > 0) log.Append(", ");
            log.Append(affectedChunks[i].Coordinate);
        }
        log.AppendLine("]");
        log.AppendLine("  RebuildReturned=" + rebuiltChunks);

        if (affectedCount == 0)
        {
            log.AppendLine(
                "WARNING: Selected voxel changed, but no affected chunk was found."
            );
        }

        // -----------------------------------------------------------------
        // Effective Density diagnostic: read-only, does not write anything.
        // -----------------------------------------------------------------
        int totalBaseSamples = 0;
        int totalMappedSamples = 0;
        int totalChangedSamples = 0;
        int printedSamples = 0;

        for (int chunkIndex = 0; chunkIndex < affectedCount; chunkIndex++)
        {
            TerrainChunkView view = affectedChunks[chunkIndex];
            if (view == null || view.Chunk == null) continue;

            DensityField baseField = view.Chunk.Density;
            if (baseField == null) continue;

            int chunkBaseSamples = 0;
            int chunkMappedSamples = 0;
            int chunkChangedSamples = 0;

            // 性能（B3-D）：只有落在被挖 Voxel 的 Terrain Local 区间内的 Density 样本
            // 才可能映射到 selectedVoxel。把三重循环限制在该索引窗口内，
            // 结果与“扫描全部样本”完全等价，但把每次挖掘的工作量从
            // O(SampleCount³)（76³ ≈ 439k/Chunk）降到 O(窗口³)（≈ 8–27 个样本）。
            int x0, x1, y0, y1, z0, z1;
            ComputeSampleIndexWindow(
                baseField,
                selectedTerrain.VoxelField.GetVoxelMin(selectedVoxel),
                selectedTerrain.VoxelField.GetVoxelMax(selectedVoxel),
                out x0,
                out x1,
                out y0,
                out y1,
                out z0,
                out z1
            );

            for (int x = x0; x <= x1; x++)
            {
                for (int y = y0; y <= y1; y++)
                {
                    for (int z = z0; z <= z1; z++)
                    {
                        chunkBaseSamples++;
                        totalBaseSamples++;

                        Vector3 samplePosition = baseField.GetLocalPosition(x, y, z);
                        Vector3Int mappedVoxel =
                            selectedTerrain.VoxelField.PositionToVoxel(
                                samplePosition
                            );

                        if (mappedVoxel != selectedVoxel) continue;

                        chunkMappedSamples++;
                        totalMappedSamples++;

                        float baseDensity = baseField[x, y, z];
                        float effectiveBefore = previousState == VoxelState.Air
                            ? VoxelDensityAdapter.AirDensity
                            : baseDensity;
                        float effectiveAfter = afterState == VoxelState.Air
                            ? VoxelDensityAdapter.AirDensity
                            : baseDensity;

                        bool changed = Mathf.Abs(effectiveAfter - effectiveBefore) >
                                       0.00001f;

                        if (changed)
                        {
                            chunkChangedSamples++;
                            totalChangedSamples++;
                        }

                        if (printedSamples < 16)
                        {
                            printedSamples++;
                            log.AppendLine("Sample=" + new Vector3Int(x, y, z));
                            log.AppendLine(
                                "SamplePosition=" + FormatVector(samplePosition)
                            );
                            log.AppendLine("MappedVoxel=" + mappedVoxel);
                            log.AppendLine("BaseDensity=" + baseDensity);
                            log.AppendLine("EffectiveBefore=" + effectiveBefore);
                            log.AppendLine("EffectiveAfter=" + effectiveAfter);
                            log.AppendLine("Changed=" + changed);
                        }
                    }
                }
            }

            log.AppendLine(
                "Chunk=" + view.Coordinate +
                " BaseSamples=" + chunkBaseSamples +
                " MappedSamples=" + chunkMappedSamples +
                " ChangedSamples=" + chunkChangedSamples
            );
        }

        log.AppendLine("EffectiveDensity");
        log.AppendLine("  BaseSampleCount=" + totalBaseSamples);
        log.AppendLine("  MappedSampleCount=" + totalMappedSamples);
        log.AppendLine("  ChangedSampleCount=" + totalChangedSamples);

        if (totalMappedSamples == 0)
        {
            log.AppendLine(
                "WARNING: Selected voxel does not affect any Base Density sample."
            );
        }
        else if (totalChangedSamples == 0)
        {
            log.AppendLine(
                "WARNING: Selected voxel maps to Density samples, but Effective Density does not change."
            );
        }

        // -----------------------------------------------------------------
        // Mesh rebuild diagnostic.
        // -----------------------------------------------------------------
        bool anyReferenceError = false;
        bool anyMeshTopologyUnchanged = false;
        bool meshRebuiltSuccessfully = false;

        for (int i = 0; i < affectedCount; i++)
        {
            TerrainChunkView view = affectedChunks[i];
            if (view == null || view.Chunk == null)
            {
                anyReferenceError = true;
                continue;
            }

            Mesh meshAfter = view.Chunk.Mesh;
            int vertexCountAfter = meshAfter != null ? meshAfter.vertexCount : 0;
            int indexCountAfter = TotalIndexCount(meshAfter);

            int vertexCountBefore = vertexCountsBefore != null &&
                                    i < vertexCountsBefore.Length
                ? vertexCountsBefore[i]
                : 0;
            int indexCountBefore = indexCountsBefore != null &&
                                   i < indexCountsBefore.Length
                ? indexCountsBefore[i]
                : 0;

            bool meshNull = meshAfter == null;
            bool meshFilterMatches =
                view.MeshFilter != null &&
                view.MeshFilter.sharedMesh == meshAfter;
            bool meshColliderMatches =
                view.MeshCollider != null &&
                view.MeshCollider.sharedMesh == meshAfter;

            bool topologyUnchanged =
                vertexCountBefore == vertexCountAfter &&
                indexCountBefore == indexCountAfter;

            log.AppendLine("Mesh");
            log.AppendLine("  Chunk=" + view.Coordinate);
            log.AppendLine("  VerticesBefore=" + vertexCountBefore);
            log.AppendLine("  VerticesAfter=" + vertexCountAfter);
            log.AppendLine("  IndicesBefore=" + indexCountBefore);
            log.AppendLine("  IndicesAfter=" + indexCountAfter);
            log.AppendLine("  MeshNull=" + meshNull);
            log.AppendLine("  MeshFilterMatches=" + meshFilterMatches);
            log.AppendLine("  MeshColliderMatches=" + meshColliderMatches);

            if (topologyUnchanged)
            {
                anyMeshTopologyUnchanged = true;
                log.AppendLine("WARNING: Mesh topology statistics unchanged.");
            }

            if (meshNull || !meshFilterMatches || !meshColliderMatches)
            {
                anyReferenceError = true;
            }
            else if (!topologyUnchanged || totalChangedSamples > 0)
            {
                meshRebuiltSuccessfully = true;
            }
        }

        log.AppendLine("Conclusion");

        if (afterState != VoxelState.Air)
        {
            log.AppendLine("  Case A: Voxel write failed");
        }
        else if (affectedCount == 0)
        {
            log.AppendLine("  Case B: No affected chunk");
        }
        else if (totalMappedSamples == 0)
        {
            log.AppendLine("  Case C: Voxel does not affect any Density sample");
        }
        else if (totalChangedSamples == 0)
        {
            log.AppendLine("  Case D: Density mapping exists but Effective Density unchanged");
        }
        else if (anyReferenceError)
        {
            log.AppendLine(
                "  ReferenceError: Effective Density changed but MeshFilter/MeshCollider reference is invalid"
            );
        }
        else if (anyMeshTopologyUnchanged)
        {
            log.AppendLine(
                "  Case E: Effective Density changed but Mesh statistics unchanged"
            );
        }
        else if (meshRebuiltSuccessfully && rebuiltChunks > 0)
        {
            log.AppendLine("  Case F: Mesh rebuilt successfully");
        }
        else
        {
            log.AppendLine("  Case E: Effective Density changed but Mesh statistics unchanged");
        }

        if (totalChangedSamples > 0 && rebuiltChunks == 0)
        {
            log.AppendLine(
                "WARNING: Effective Density changed but RebuildChunksAffectedByVoxel returned 0."
            );
        }

        Debug.Log(log.ToString());
    }

    private static int TotalIndexCount(Mesh mesh)
    {
        if (mesh == null) return 0;

        int total = 0;
        for (int i = 0; i < mesh.subMeshCount; i++)
        {
            total += mesh.GetTriangles(i).Length;
        }

        return total;
    }

    private static string FormatVector(Vector3 value)
    {
        return "(" +
               value.x.ToString("F4") + "," +
               value.y.ToString("F4") + "," +
               value.z.ToString("F4") + ")";
    }

    private void ClearSelectedVoxel()
    {
        hasSelectedVoxel = false;
        selectedVoxel = default(Vector3Int);
        selectedTerrain = null;
        selectedRaycastHitWorld = default(Vector3);
        selectedRaycastHitNormal = default(Vector3);
        selectedTerrainLocalPoint = default(Vector3);

        // A 阶段：清除无效选择时必须同时清除高亮（避免高亮残留在空气侧）。
        if (voxelHighlight != null)
        {
            voxelHighlight.Hide();
        }
    }

    private TerrainVoxelHighlight ResolveVoxelHighlight()
    {
        if (voxelHighlight != null) return voxelHighlight;

        DensityTerrain resolvedTerrain = ResolveTerrain();
        if (resolvedTerrain == null) return null;

        GameObject highlightObject = new GameObject("VoxelHighlight");
        highlightObject.hideFlags = HideFlags.DontSave;
        highlightObject.transform.SetParent(transform, false);

        voxelHighlight = highlightObject.AddComponent<TerrainVoxelHighlight>();
        voxelHighlight.HighlightColor = voxelHighlightColor;
        voxelHighlight.LineWidth = voxelHighlightLineWidth;
        voxelHighlight.Initialize(resolvedTerrain.transform);
        voxelHighlight.Hide();

        return voxelHighlight;
    }

    private void UpdateDebugTarget()
    {
        if (!TryGetTerrainHit(out RaycastHit hit, out TerrainChunkView view))
        {
            hasDebugHit = false;
            debugHitInfo = string.Empty;
            return;
        }

        hasDebugHit = true;
        debugHit = hit;
        debugHitInfo = BuildHitInfo(hit, view);

        if (debugLogHitInfo && debugHitInfo != lastLoggedHitInfo)
        {
            lastLoggedHitInfo = debugHitInfo;
            Debug.Log("[PlayerMiningDebug] " + debugHitInfo);
        }

        if (debugDrawTargeting)
        {
            DrawRuntimeHitMarker(hit);
        }
    }

    private string BuildHitInfo(RaycastHit hit, TerrainChunkView view)
    {
        string chunkCoordinate = view != null && view.Chunk != null
            ? view.Chunk.Coordinate.ToString()
            : "unknown";

        Mesh mesh = view != null && view.MeshFilter != null
            ? view.MeshFilter.sharedMesh
            : null;

        BuildTriangleInfo(mesh, hit.triangleIndex);

        return "Chunk=" + chunkCoordinate +
               " Triangle=" + hit.triangleIndex +
               " SubMesh=" + cachedSubMeshIndex +
               " Indices=(" + cachedTriangleIndices + ")" +
               " Point=" + hit.point +
               " Normal=" + hit.normal;
    }

    private void BuildTriangleInfo(Mesh mesh, int triangleIndex)
    {
        if (mesh == null || !mesh.isReadable || triangleIndex < 0)
        {
            cachedTriangleMeshId = 0;
            cachedTriangleIndex = -1;
            cachedTriangleIndices = "n/a";
            cachedSubMeshIndex = "n/a";
            return;
        }

        int meshId = mesh.GetInstanceID();
        if (meshId == cachedTriangleMeshId && triangleIndex == cachedTriangleIndex)
        {
            return;
        }

        cachedTriangleMeshId = meshId;
        cachedTriangleIndex = triangleIndex;
        cachedTriangleIndices = "n/a";
        cachedSubMeshIndex = "n/a";

        int[] triangles = mesh.triangles;
        int baseIndex = triangleIndex * 3;

        if (baseIndex >= 0 && baseIndex + 2 < triangles.Length)
        {
            cachedTriangleIndices =
                triangles[baseIndex] + "," +
                triangles[baseIndex + 1] + "," +
                triangles[baseIndex + 2];
        }

        int offset = 0;
        for (int i = 0; i < mesh.subMeshCount; i++)
        {
            int count = mesh.GetTriangles(i).Length;
            if (baseIndex >= offset && baseIndex < offset + count)
            {
                cachedSubMeshIndex = i.ToString();
                break;
            }

            offset += count;
        }
    }

    private void DrawRuntimeHitMarker(RaycastHit hit)
    {
        Camera camera = ResolveCamera();
        if (camera != null)
        {
            Debug.DrawLine(
                camera.transform.position,
                hit.point,
                Color.yellow,
                0f,
                false
            );
        }

        Debug.DrawRay(hit.point, hit.normal * 2f, Color.cyan, 0f, false);

        const float size = 0.25f;
        Debug.DrawLine(
            hit.point - Vector3.right * size,
            hit.point + Vector3.right * size,
            Color.red,
            0f,
            false
        );
        Debug.DrawLine(
            hit.point - Vector3.up * size,
            hit.point + Vector3.up * size,
            Color.red,
            0f,
            false
        );
        Debug.DrawLine(
            hit.point - Vector3.forward * size,
            hit.point + Vector3.forward * size,
            Color.red,
            0f,
            false
        );
    }

    private void OnDrawGizmos()
    {
        if (!debugDrawTargeting || !hasDebugHit) return;

        Gizmos.color = new Color(1f, 0.2f, 0.1f, 0.35f);
        Gizmos.DrawSphere(debugHit.point, 0.25f);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(debugHit.point, 0.35f);

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(debugHit.point, debugHit.point + debugHit.normal * 2f);
    }

    private void OnGUI()
    {
        if (!debugShowHitInfo || !hasDebugHit ||
            string.IsNullOrEmpty(debugHitInfo))
        {
            return;
        }

        Camera camera = ResolveCamera();
        if (camera == null) return;

        Vector3 screenPoint = camera.WorldToScreenPoint(debugHit.point);
        if (screenPoint.z < 0f) return;

        Rect labelRect = new Rect(
            screenPoint.x + 14f,
            Screen.height - screenPoint.y + 14f,
            900f,
            60f
        );

        GUI.color = Color.yellow;
        GUI.Label(labelRect, debugHitInfo);
        GUI.color = Color.white;
    }

    private Camera ResolveCamera()
    {
        if (targetCamera != null) return targetCamera;

        targetCamera = Camera.main;
        if (targetCamera == null)
        {
            targetCamera = GetComponentInChildren<Camera>();
        }

        return targetCamera;
    }

    private DensityTerrain ResolveTerrain()
    {
        if (terrain != null) return terrain;

        terrain = FindObjectOfType<DensityTerrain>();
        return terrain;
    }
}
