using System.Collections.Generic;
using System.Text;
using TerrainModificationV2;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Mining Surface 只读诊断工具（Editor Only，不修改任何 Terrain 生产逻辑）。
///
/// 目的：给定一个 Voxel 坐标，一次性导出“真实表面状态”，用于调查
/// 「玩家挖掘后偶发缺面」的三类怀疑：
///   怀疑 1：VoxelField=Solid 但 DensityField 实际为空气（选择/挖掘目标与材料不符）。
///   怀疑 2：Marching Cubes partial cell（Voxel 内只有少量 density&gt;0 区域）
///           在 Runtime Mutation 后没有正确生成暴露面。
///   怀疑 3：Runtime Mutation 删除了旧 Surface Fragment，但没有重新生成对应 Surface。
///
/// 本文件是**纯读取**诊断：
///   · 只读取 VoxelField / DensityField / ChunkGeometry / Mesh；
///   · 不写 VoxelField、不写 Mesh、不写 Visibility、不补面、不改任何生产数据；
///   · 唯一“构造”行为发生在自检（SelfTestDetector）里：用一个临时 TerrainChunk +
///     合成 Mesh 验证检测器本身能区分“有面/无面”，用完即销毁。
///
/// 用法：
///   · Editor 菜单：Tools/Terrain/Diagnose Mining Surface (Scene Voxel)
///   · 批处理：-executeMethod MiningSurfaceDiagnosticValidation.RunBatch
///   · 代码：MiningSurfaceDiagnosticValidation.Diagnose(terrain, voxel)
/// </summary>
public static class MiningSurfaceDiagnosticValidation
{
    // ---------------------------------------------------------------------
    // 输入配置
    // ---------------------------------------------------------------------

    /// <summary>诊断目标 Voxel（Scene 菜单 / 批处理默认值）。</summary>
    public static Vector3Int TargetVoxel = new Vector3Int(30, 14, 30);

    /// <summary>partial cell 子采样分辨率（每轴采样数，默认 4 ⇒ 4³=64 个样本）。</summary>
    public static int PartialSampleResolution = 4;

    private const float ProbeEpsilon = 0.001f;
    private const float PlaneTolerance = 0.02f;
    private const float RectTolerance = 0.05f;
    private const float NormalAlignment = 0.99f;
    private const float TolerantAxialWindow = 0.45f;

    private static readonly Vector3Int[] Directions =
    {
        new Vector3Int(1, 0, 0),
        new Vector3Int(-1, 0, 0),
        new Vector3Int(0, 1, 0),
        new Vector3Int(0, -1, 0),
        new Vector3Int(0, 0, 1),
        new Vector3Int(0, 0, -1)
    };

    private static readonly string[] DirectionNames =
    {
        "+X", "-X", "+Y", "-Y", "+Z", "-Z"
    };

    private static int checks;
    private static int failures;

    // =====================================================================
    // 入口
    // =====================================================================

    /// <summary>诊断当前场景中所有 DensityTerrain 的 TargetVoxel。</summary>
    [MenuItem("Tools/Terrain/Diagnose Mining Surface (Scene Voxel)")]
    public static void RunSceneDiagnostic()
    {
        DensityTerrain[] terrains =
            Object.FindObjectsOfType<DensityTerrain>(true);

        if (terrains == null || terrains.Length == 0)
        {
            Debug.LogWarning("[MiningSurfaceDiagnostic] 场景中没有 DensityTerrain。");
            return;
        }

        for (int i = 0; i < terrains.Length; i++)
        {
            MiningSurfaceDiagnosticReport report =
                Diagnose(terrains[i], TargetVoxel);

            if (report != null)
            {
                Debug.Log(report.ToText());
            }
        }
    }

    /// <summary>
    /// 批处理入口：先自检诊断器，再在夹具上导出样例报告。
    /// 注意：断言只针对“诊断器自身正确性”，生产数据异常只记录进报告，不判 FAIL。
    /// </summary>
    public static void RunBatch()
    {
        checks = 0;
        failures = 0;

        SelfTestDetector();
        RunFixtureDiagnostics();
        TempReproMine26_16_49();

        Debug.Log(
            "[MiningSurfaceDiagnosticValidation] ===== SUMMARY =====\n" +
            "Checks = " + checks + "\n" +
            "Failures = " + failures + "\n" +
            "Result = " + (failures == 0 ? "PASS" : "FAIL")
        );

        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    // =====================================================================
    // TEMP DIAGNOSTIC（缺面调查复现：Terrain "Terraria"）
    //   · 目标 Voxel=(14,14,12)（真实案例 C）
    //   · 夹具高度按案例 A 的真实日志校准：真实地表比 SampleScene 的 15.7 低 ≈0.2077 m
    //     （A 日志：y=16.28 ⇒ 0.4952、y=16.7733 ⇒ 0.0018 ⇒ 真实地表 ≈16.774；
    //      夹具同列地表 ≈16.982）⇒ 取 height = 15.4923
    // 只做“建夹具 → 挖该 Voxel → 打印诊断”；不改任何生产逻辑。
    // =====================================================================
    private static void TempReproMine26_16_49()
    {
        GameObject terrainObject = CreateFixture(
            "MiningSurfaceDiagnostic_Repro14_14_12", true, 15.4923f);

        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();
            Vector3Int voxel = new Vector3Int(14, 14, 12);

            MiningSurfaceDiagnosticReport before = Diagnose(terrain, voxel);

            Debug.Log(
                "[FaceTrace REPRO] before: VoxelField=" + before.VoxelFieldState +
                " material=" + before.HasMaterialInVoxel +
                " centre=" + before.DensityAtCentre.ToString("F4") +
                " partialRatio=" + before.PartialSolidRatio.ToString("F3") +
                "（实体样本 " + before.PartialSamplesSolid + "/" +
                before.PartialSamplesTotal + "）"
            );

            terrain.VoxelField.Set(voxel, VoxelState.Air);
            int rebuilt = terrain.RebuildChunksAffectedByVoxel(voxel);

            Debug.Log(
                "[FaceTrace REPRO] mined: rebuilt=" + rebuilt +
                " committed=" + VoxelAwareMeshRuntimeMutation.LastMutationCommitted +
                " dirty=" + JoinCoordinates(
                    VoxelAwareMeshRuntimeMutation.LastDirtyChunks) +
                " reassembled=" + JoinCoordinates(
                    VoxelAwareMeshRuntimeMutation.LastReassembledChunks)
            );

            MiningSurfaceDiagnosticReport after = Diagnose(terrain, voxel);

            for (int i = 0; i < after.Faces.Count; i++)
            {
                FaceDiagnostic face = after.Faces[i];

                Debug.Log(
                    "[FaceTrace REPRO] face " + face.Direction +
                    " key=" + face.FaceKey +
                    " boundary=" + face.IsBoundary +
                    " expectsSurface=" + face.ExpectsSurface +
                    " meshStrict=" + face.SurfaceFoundStrict +
                    " meshTolerant=" + face.SurfaceFoundTolerant +
                    " coplanarFragments=" + face.CoplanarFragmentCount +
                    " 覆写true/false=" + face.OverrideTrueCount + "/" +
                    face.OverrideFalseCount +
                    " hiddenContribution=" + face.ContributionFragmentCount
                );
            }

            Debug.Log(after.ToText());
        }
        finally
        {
            Object.DestroyImmediate(terrainObject);
        }
    }

    private static string JoinCoordinates(List<Vector3Int> list)
    {
        if (list == null || list.Count == 0) return "<empty>";

        StringBuilder text = new StringBuilder();

        for (int i = 0; i < list.Count; i++)
        {
            if (i > 0) text.Append(",");
            text.Append(list[i]);
        }

        return text.ToString();
    }

    // =====================================================================
    // 核心：给定 voxel 的真实表面状态
    // =====================================================================

    public static MiningSurfaceDiagnosticReport Diagnose(
        DensityTerrain terrain,
        Vector3Int voxel)
    {
        MiningSurfaceDiagnosticReport report =
            new MiningSurfaceDiagnosticReport();

        report.Voxel = voxel;

        if (terrain == null)
        {
            report.Findings.Add("Terrain 为 null，无法诊断。");
            return report;
        }

        VoxelField field = terrain.VoxelField;

        if (field == null)
        {
            report.Findings.Add("VoxelField 为 null，无法诊断。");
            return report;
        }

        float voxelSize = field.VoxelSize;
        Vector3Int chunkVoxelSize = terrain.EffectiveChunkVoxelSize;

        report.VoxelSize = voxelSize;
        report.VoxelFieldState = field.Get(voxel);
        report.VoxelFieldDefaultState = field.DefaultState;
        report.VoxelFieldExplicitlyStored = IsExplicitlyStored(field, voxel);
        report.EditedAir = report.VoxelFieldState == VoxelState.Air;

        TerrainChunk chunk = ResolveChunk(terrain, voxel, chunkVoxelSize);

        report.ChunkCoordinate = chunk != null
            ? chunk.Coordinate
            : ChunkSpace.VoxelToChunk(voxel, chunkVoxelSize);
        report.ChunkExists = chunk != null;

        // ---- 该 Voxel *自己*的基础片段状态（VoxelFragmentIndex）----
        CollectOwnFragments(chunk, voxel, report);

        // ---- 基础密度：中心 + 6 个面内侧探针 + partial cell 子采样 ----
        if (chunk != null && chunk.Density != null)
        {
            report.CentreSampleOk = TrySample(
                chunk, VoxelCentre(voxel, voxelSize), out report.DensityAtCentre);

            report.FaceProbeDensities = new float[6];
            report.FaceProbeSampleOk = new bool[6];

            for (int d = 0; d < Directions.Length; d++)
            {
                Vector3 probe = FaceProbePoint(
                    voxel, Directions[d], voxelSize);

                report.FaceProbeSampleOk[d] = TrySample(
                    chunk, probe, out report.FaceProbeDensities[d]);
            }

            SamplePartialCell(
                chunk,
                voxel,
                voxelSize,
                report);
        }

        report.HasMaterialInVoxel =
            (report.CentreSampleOk && report.DensityAtCentre > 0f) ||
            HasPositiveFaceProbe(report);

        report.Suspicion1VoxelSolidButAir =
            !report.EditedAir &&
            report.VoxelFieldState == VoxelState.Solid &&
            !report.HasMaterialInVoxel &&
            (report.CentreSampleOk || AnyFaceProbeSampled(report));

        report.Suspicion2PartialCell =
            report.PartialSamplesTotal > 0 &&
            report.PartialSamplesSolid > 0 &&
            report.PartialSamplesSolid < report.PartialSamplesTotal;

        // ---- 6 个面：边界面 / Fragment / Visibility / Contribution / Mesh ----
        for (int d = 0; d < Directions.Length; d++)
        {
            report.Faces.Add(DiagnoseFace(
                terrain,
                field,
                Directions[d],
                DirectionNames[d],
                voxel,
                voxelSize,
                chunkVoxelSize));
        }

        // ---- Chunk 级：View 同步 / 隐藏覆写 / 隐藏面贡献 ----
        CollectChunkDiagnostics(terrain, report);

        BuildFindings(report);

        return report;
    }

    private static bool HasCoplanarFragmentsOnAnyFace(
        MiningSurfaceDiagnosticReport report)
    {
        for (int i = 0; i < report.Faces.Count; i++)
        {
            if (report.Faces[i].CoplanarFragmentCount > 0) return true;
        }

        return false;
    }

    private static void CollectOwnFragments(
        TerrainChunk chunk,
        Vector3Int voxel,
        MiningSurfaceDiagnosticReport report)
    {
        if (chunk == null || chunk.Geometry == null) return;

        VoxelAwareMeshPrecut.ChunkGeometry geometry = chunk.Geometry;

        List<int> fragmentIds;
        if (!geometry.VoxelFragmentIndex.TryGetValue(voxel, out fragmentIds) ||
            fragmentIds == null)
        {
            return;
        }

        report.OwnFragmentIndexFound = true;

        for (int i = 0; i < fragmentIds.Count; i++)
        {
            int id = fragmentIds[i];
            if (id < 0 || id >= geometry.FaceFragments.Count) continue;

            VoxelAwareMeshPrecut.FaceFragment fragment = geometry.FaceFragments[id];

            report.OwnFragmentCount++;
            if (fragment.Active) report.OwnActiveFragmentCount++;

            bool visible;
            if (geometry.FaceFragmentVisibility.TryGetValue(id, out visible))
            {
                if (visible) report.OwnVisibleOverrideCount++;
                else report.OwnHiddenOverrideCount++;
            }
        }
    }

    private static FaceDiagnostic DiagnoseFace(
        DensityTerrain terrain,
        VoxelField field,
        Vector3Int direction,
        string directionName,
        Vector3Int voxel,
        float voxelSize,
        Vector3Int chunkVoxelSize)
    {
        FaceDiagnostic face = new FaceDiagnostic();
        face.Direction = directionName;

        Vector3Int negativeVoxel;
        Vector3Int positiveVoxel;
        VoxelAwareMeshPrecut.FaceId faceId = BuildFaceId(
            voxel, direction, out negativeVoxel, out positiveVoxel);

        face.FaceKey = faceId.Key;
        face.NegativeVoxel = negativeVoxel;
        face.PositiveVoxel = positiveVoxel;

        bool negativeSolid = field.Get(negativeVoxel) == VoxelState.Solid;
        bool positiveSolid = field.Get(positiveVoxel) == VoxelState.Solid;

        face.NegativeSolid = negativeSolid;
        face.PositiveSolid = positiveSolid;
        face.IsBoundary = negativeSolid != positiveSolid;

        int axis = faceId.Axis;
        int sign = direction[axis];

        // 共享面平面 + 面矩形中心（面积以实体侧 Voxel 的面为准）
        float plane = Mathf.Max(
            negativeVoxel[axis], positiveVoxel[axis]) * voxelSize;

        Vector3Int solidVoxel = negativeSolid ? negativeVoxel : positiveVoxel;
        Vector3Int airVoxel = negativeSolid ? positiveVoxel : negativeVoxel;

        face.SolidVoxel = solidVoxel;
        face.AirVoxel = airVoxel;

        Vector3 faceCentre = FaceCentre(solidVoxel, axis, plane, voxelSize);
        face.Plane = plane;
        face.FaceCentre = faceCentre;

        TerrainChunk negativeChunk = ResolveChunk(
            terrain, negativeVoxel, chunkVoxelSize);
        TerrainChunk positiveChunk = ResolveChunk(
            terrain, positiveVoxel, chunkVoxelSize);
        TerrainChunk solidChunk = negativeSolid ? negativeChunk : positiveChunk;
        TerrainChunk airChunk = negativeSolid ? positiveChunk : negativeChunk;

        face.NegativeChunkExists = negativeChunk != null;
        face.PositiveChunkExists = positiveChunk != null;

        // 实体侧材料证据（面内侧 1e-3）
        if (face.IsBoundary && solidChunk != null && solidChunk.Density != null)
        {
            int inward = negativeSolid ? -1 : 1;
            Vector3 probe = faceCentre;
            probe[axis] += inward * sign * ProbeEpsilon;

            float density;
            face.SolidSideProbeSampled = TrySample(solidChunk, probe, out density);
            face.SolidSideProbeDensity = density;
            face.ExpectsSurface =
                face.SolidSideProbeSampled && density > 0f;
        }

        // Mesh 覆盖判定（严格 + 容差）
        float halfExtent = voxelSize * 0.5f + RectTolerance;

        if (face.IsBoundary || face.ExpectsSurface)
        {
            face.SurfaceFoundStrict =
                ChunkMeshHasFace(solidChunk, axis, plane, faceCentre, halfExtent, false) ||
                ChunkMeshHasFace(airChunk, axis, plane, faceCentre, halfExtent, false);

            face.SurfaceFoundTolerant =
                ChunkMeshHasFace(solidChunk, axis, plane, faceCentre, halfExtent, true) ||
                ChunkMeshHasFace(airChunk, axis, plane, faceCentre, halfExtent, true);
        }
        else
        {
            // 非边界面：也记录“该面上是否有几何”（Hovert 残留信号）
            face.SurfaceFoundStrict =
                ChunkMeshHasFace(solidChunk, axis, plane, faceCentre, halfExtent, false) ||
                ChunkMeshHasFace(airChunk, axis, plane, faceCentre, halfExtent, false);

            face.SurfaceFoundTolerant = face.SurfaceFoundStrict;
        }

        // 共面 FaceFragment + Visibility 覆写（两个相关 Chunk 都查，同一 Chunk 只查一次）
        CollectFaceFragments(face, faceId, solidChunk);

        if (airChunk != solidChunk)
        {
            CollectFaceFragments(face, faceId, airChunk);
        }

        // HiddenFaceContributions（同 face.Key） + 历史 SpatialFaceRecord
        CollectFaceContribution(face, faceId, solidChunk);
        CollectFaceContribution(face, faceId, airChunk);

        return face;
    }

    private static void CollectFaceFragments(
        FaceDiagnostic face,
        VoxelAwareMeshPrecut.FaceId faceId,
        TerrainChunk chunk)
    {
        if (chunk == null || chunk.Geometry == null) return;

        VoxelAwareMeshPrecut.ChunkGeometry geometry = chunk.Geometry;
        face.ScannedChunks.Add(chunk.Coordinate);

        for (int i = 0; i < geometry.FaceFragments.Count; i++)
        {
            VoxelAwareMeshPrecut.FaceFragment fragment = geometry.FaceFragments[i];

            if (fragment == null || fragment.Face.Key != faceId.Key) continue;

            face.CoplanarFragmentCount++;
            if (fragment.Active) face.ActiveFragmentCount++;

            // FaceFragmentVisibility 是覆写层：true = 显式可见，false = 显式隐藏。
            bool visibility;
            if (geometry.FaceFragmentVisibility.TryGetValue(i, out visibility))
            {
                if (visibility) face.OverrideTrueCount++;
                else face.OverrideFalseCount++;
            }
        }
    }

    private static void CollectFaceContribution(
        FaceDiagnostic face,
        VoxelAwareMeshPrecut.FaceId faceId,
        TerrainChunk chunk)
    {
        if (chunk == null || chunk.Geometry == null) return;

        VoxelAwareMeshPrecut.ChunkGeometry geometry = chunk.Geometry;

        List<VoxelAwareMeshSpatialFace.HiddenFaceFragment> contribution;
        if (geometry.HiddenFaceContributions.TryGetValue(
            faceId.Key, out contribution) && contribution != null)
        {
            face.ContributionFragmentCount += contribution.Count;

            for (int i = 0; i < contribution.Count; i++)
            {
                if (contribution[i] == null) continue;
                face.ContributionPolygonPointCount +=
                    contribution[i].Polygon != null
                        ? contribution[i].Polygon.Count
                        : 0;
                if (contribution[i].Active) face.ContributionActiveCount++;
            }
        }

        for (int i = 0; i < geometry.SpatialFaces.Count; i++)
        {
            VoxelAwareMeshSpatialFace.SpatialFaceRecord record =
                geometry.SpatialFaces[i];

            if (record == null || record.Face.Key != faceId.Key) continue;

            face.SpatialFaceRecordCount++;
            face.LastDecision = record.Decision.ToString();
            face.LastEvaluationFailed = record.EvaluationFailed;

            if (record.EvaluationFailed &&
                face.EvaluationFailureMessage.Length == 0)
            {
                face.EvaluationFailureMessage = record.EvaluationFailureMessage;
            }
        }
    }

    private static void CollectChunkDiagnostics(
        DensityTerrain terrain,
        MiningSurfaceDiagnosticReport report)
    {
        TerrainChunk[] chunks = CollectChunks(terrain);

        for (int i = 0; i < chunks.Length; i++)
        {
            TerrainChunk chunk = chunks[i];
            if (chunk == null) continue;

            ChunkDiagnostic diagnostic = new ChunkDiagnostic();
            diagnostic.Coordinate = chunk.Coordinate;
            diagnostic.Exists = true;
            diagnostic.GeometryExists = chunk.Geometry != null;

            Mesh mesh = chunk.Mesh;
            if (mesh != null)
            {
                diagnostic.VertexCount = mesh.vertexCount;
                diagnostic.TriangleCount = mesh.triangles.Length / 3;
            }

            if (chunk.Geometry != null)
            {
                diagnostic.FaceFragmentCount = chunk.Geometry.FaceFragments.Count;
                diagnostic.FragmentCount = chunk.Geometry.Fragments.Count;
                diagnostic.HiddenContributionKeyCount =
                    chunk.Geometry.HiddenFaceContributions.Count;

                foreach (KeyValuePair<int, bool> pair in
                    chunk.Geometry.FaceFragmentVisibility)
                {
                    if (pair.Value) diagnostic.VisibleOverrideCount++;
                    else diagnostic.HiddenOverrideCount++;
                }
            }

            TerrainChunkView view = FindView(terrain, chunk.Coordinate);
            if (view != null)
            {
                diagnostic.ViewFound = true;
                diagnostic.MeshFilterMatches =
                    view.MeshFilter != null &&
                    view.MeshFilter.sharedMesh == mesh;
                diagnostic.MeshColliderMatches =
                    view.MeshCollider != null &&
                    view.MeshCollider.sharedMesh == mesh;
            }

            report.Chunks.Add(diagnostic);
        }

        report.LastMutationCommitted =
            VoxelAwareMeshRuntimeMutation.LastMutationCommitted;
        report.LastEvaluationFailed =
            VoxelAwareMeshRuntimeMutation.LastEvaluationFailed;
        report.LastEvaluationFailureMessage =
            VoxelAwareMeshRuntimeMutation.LastEvaluationFailureMessage;
        report.LastDirtyChunks.AddRange(
            VoxelAwareMeshRuntimeMutation.LastDirtyChunks);
        report.LastReassembledChunks.AddRange(
            VoxelAwareMeshRuntimeMutation.LastReassembledChunks);
        report.LastReassembledChunkCount =
            VoxelAwareMeshRuntimeMutation.LastReassembledChunkCount;
    }

    private static void BuildFindings(MiningSurfaceDiagnosticReport report)
    {
        string voxel = report.Voxel.ToString();

        if (report.Suspicion1VoxelSolidButAir)
        {
            report.Findings.Add(
                "怀疑1命中：VoxelField=" + report.VoxelFieldState +
                "（显式存储=" + report.VoxelFieldExplicitlyStored +
                "）但 DensityField 在中心/6 面探针/子采样中都没有 material（density>0）" +
                " ⇒ 该 Voxel 会被当成实体目标，而实际是空气。Voxel=" + voxel);
        }

        if (report.Suspicion2PartialCell)
        {
            report.Findings.Add(
                "怀疑2关注点：等值面穿过 Voxel —— 子采样实体占比 " +
                report.PartialSamplesSolid + "/" + report.PartialSamplesTotal +
                " = " + report.PartialSolidRatio.ToString("F3") +
                "（partial cell）；需核对该 Voxel 的暴露面是否生成。" +
                " Voxel=" + voxel);
        }

        if (report.PartialSamplesTotal > 0 &&
            report.PartialSamplesSolid > 0 &&
            report.PartialSamplesSolid < report.PartialSamplesTotal &&
            report.PartialSolidRatio < 0.25f)
        {
            report.Findings.Add(
                "  少量实体提示：实体样本占比 < 25%（" +
                report.PartialSolidRatio.ToString("P1") +
                "），若该 Voxel 的暴露面缺失，优先怀疑 MC partial cell 路径。");
        }

        if (report.OwnFragmentIndexFound)
        {
            if (report.EditedAir && report.OwnActiveFragmentCount > 0)
            {
                report.Findings.Add(
                    "怀疑3信号：Voxel 已编辑为 Air，但它自己的基础片段仍有 " +
                    report.OwnActiveFragmentCount + "/" + report.OwnFragmentCount +
                    " 个处于 Active（旧表面可能仍参与 Assemble）。");
            }

            if (!report.EditedAir && report.OwnActiveFragmentCount == 0 &&
                report.OwnFragmentCount > 0)
            {
                report.Findings.Add(
                    "反向信号：Voxel 未编辑（应为实体），但它自己的基础片段 " +
                    report.OwnFragmentCount + " 个全部被停用" +
                    "（旧表面被删除但 Voxel 仍是实体）。");
            }

            if (report.EditedAir && report.OwnHiddenOverrideCount == 0 &&
                report.OwnFragmentCount > 0)
            {
                report.Findings.Add(
                    "提示：Voxel 已挖空，但自身基础片段没有隐藏覆写（" +
                    report.OwnFragmentCount + " 个片段，active=" +
                    report.OwnActiveFragmentCount +
                    "）——若它本来有基岩表面，需核对 Visibility 生命周期。");
            }
        }
        else if (report.EditedAir && HasCoplanarFragmentsOnAnyFace(report))
        {
            report.Findings.Add(
                "说明：该 Voxel 自身没有基础片段条目（等值面恰好落在 Voxel 平面上时，" +
                "片段归属相邻 Voxel）；旧表面是否消失取决于共面片段的 " +
                "FaceFragmentVisibility 覆写，而不是“停用自身片段”。");
        }

        for (int i = 0; i < report.Faces.Count; i++)
        {
            FaceDiagnostic face = report.Faces[i];
            if (face.ExpectsSurface && !face.SurfaceFoundStrict)
            {
                report.Findings.Add(
                    "怀疑3信号：面 " + face.Direction +
                    "（FaceKey=" + face.FaceKey +
                    "）为 Solid|Air 边界且实体侧有材料，但 Mesh 未找到覆盖该面的三角形" +
                    "（严格判定）。共面 Fragment=" + face.CoplanarFragmentCount +
                    "（Active=" + face.ActiveFragmentCount +
                    "，显式隐藏覆写=" + face.OverrideFalseCount +
                    "），HiddenFaceContribution=" + face.ContributionFragmentCount +
                    "（多边形点=" + face.ContributionPolygonPointCount +
                    "），SpatialFaceRecord=" + face.SpatialFaceRecordCount +
                    "，Decision=" + face.LastDecision +
                    "，容差判定=" + face.SurfaceFoundTolerant);
            }
            else if (face.ExpectsSurface && !face.SurfaceFoundTolerant)
            {
                report.Findings.Add(
                    "面 " + face.Direction +
                    "：严格判定有面、容差判定无面（斜穿/部分覆盖，需人工确认）。");
            }

            if (face.ExpectsSurface &&
                face.CoplanarFragmentCount > 0 &&
                face.ActiveFragmentCount == 0 &&
                face.ContributionFragmentCount == 0)
            {
                report.Findings.Add(
                    "怀疑3信号：面 " + face.Direction +
                    " 的旧共面 Fragment 全部被停用（" +
                    face.CoplanarFragmentCount +
                    " 个，Active=0），且没有新的 HiddenFaceContribution" +
                    " ⇒ 旧 Surface 已删除但没有替代面。");
            }

            if (face.LastEvaluationFailed)
            {
                report.Findings.Add(
                    "面 " + face.Direction + " 的历史 SpatialFaceRecord 为 EvaluationFailed：" +
                    face.EvaluationFailureMessage);
            }
        }

        if (report.LastEvaluationFailed)
        {
            report.Findings.Add(
                "最近一次 Mutation 未提交（Abort）：" +
                report.LastEvaluationFailureMessage +
                " ⇒ VoxelField 与 Mesh 可能不一致（代码有意不回滚 VoxelField）。");
        }

        for (int i = 0; i < report.Chunks.Count; i++)
        {
            ChunkDiagnostic chunk = report.Chunks[i];

            if (chunk.ViewFound &&
                (!chunk.MeshFilterMatches || !chunk.MeshColliderMatches))
            {
                report.Findings.Add(
                    "Chunk " + chunk.Coordinate +
                    " 的 MeshFilter/MeshCollider 与 Chunk.Mesh 不同步（filter=" +
                    chunk.MeshFilterMatches + " collider=" +
                    chunk.MeshColliderMatches + "）。");
            }
        }

        if (report.Findings.Count == 0)
        {
            report.Findings.Add(
                "未发现异常：Voxel " + voxel + " 的密度/材料/边界面/Mesh 覆盖一致。");
        }
    }

    // =====================================================================
    // 自检：证明检测器能区分“有面 / 无面”（正控 + 负控）
    // =====================================================================

    private static void SelfTestDetector()
    {
        Mesh quad = new Mesh();
        quad.name = "MiningSurfaceDiagnosticSelfTestQuad";
        quad.vertices = new Vector3[]
        {
            new Vector3(0f, 5f, 0f),
            new Vector3(1f, 5f, 0f),
            new Vector3(1f, 5f, 1f),
            new Vector3(0f, 5f, 1f)
        };
        quad.triangles = new int[] { 0, 1, 2, 0, 2, 3 };
        quad.RecalculateNormals();
        quad.RecalculateBounds();

        // 注意：TerrainChunk.ChunkOrigin 由坐标推导（ChunkSpace.ChunkToLocalOrigin），
        // 因此自检用 (0,0,0) 号 Chunk，保证合成 Mesh 的 chunk-local 顶点
        // 与检测使用的 Terrain Local 坐标系原点一致。
        TerrainChunk chunk = new TerrainChunk(
            Vector3Int.zero, 1f, new Vector3Int(16, 16, 16));
        chunk.ReplaceAssembledMesh(quad);

        try
        {
            float half = 0.5f + RectTolerance;

            bool foundAtPlane = ChunkMeshHasFace(
                chunk, 1, 5f, new Vector3(0.5f, 5f, 0.5f), half, false);
            bool foundOtherPlane = ChunkMeshHasFace(
                chunk, 1, 6f, new Vector3(0.5f, 6f, 0.5f), half, false);
            bool foundOutsideRect = ChunkMeshHasFace(
                chunk, 1, 5f, new Vector3(3.5f, 5f, 0.5f), half, false);
            bool foundWrongAxis = ChunkMeshHasFace(
                chunk, 0, 0f, new Vector3(0f, 5.5f, 0.5f), half, false);

            Check(foundAtPlane, "自检：检测器能在合成四边形所在平面找到面（正控）");
            Check(!foundOtherPlane, "自检：检测器不会在 1.0 外的平面误报（负控）");
            Check(!foundOutsideRect, "自检：检测器不会在面矩形外误报（负控）");
            Check(!foundWrongAxis, "自检：检测器不会在错误轴向误报（负控）");
        }
        finally
        {
            chunk.Dispose();
            Object.DestroyImmediate(quad);
        }
    }

    // =====================================================================
    // 批处理样例：夹具 + 挖掘后诊断输出（生产异常只记录，不判 FAIL）
    // =====================================================================

    private static void RunFixtureDiagnostics()
    {
        RunFlatFixtureDiagnostics();
        RunPartialCellDiagnostics();
    }

    private static void RunFlatFixtureDiagnostics()
    {
        GameObject terrainObject = CreateFixture(
            "MiningSurfaceDiagnostic_Flat", false, 15f);

        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();

            // (a) 完全空气 Voxel（平地表面之上）⇒ 怀疑1 检出能力
            Vector3Int airVoxel = new Vector3Int(30, 15, 30);
            MiningSurfaceDiagnosticReport airReport =
                Diagnose(terrain, airVoxel);

            Check(
                airReport.Suspicion1VoxelSolidButAir,
                "样例(a)：表面之上的未编辑 Voxel 被诊断为“VoxelField=Solid 但无材料”" +
                "（怀疑1 检出）"
            );

            bool anyExpected = false;
            for (int i = 0; i < airReport.Faces.Count; i++)
            {
                if (airReport.Faces[i].ExpectsSurface) anyExpected = true;
            }

            Check(!anyExpected, "样例(a)：空气 Voxel 的 6 面都不应期待表面");

            // (b) 实体内 Voxel（表面之下）⇒ 材料证据
            Vector3Int solidVoxel = new Vector3Int(30, 13, 30);
            MiningSurfaceDiagnosticReport solidReport =
                Diagnose(terrain, solidVoxel);

            Check(
                solidReport.HasMaterialInVoxel && !solidReport.Suspicion1VoxelSolidButAir,
                "样例(b)：表面之下的 Voxel 诊断为有材料（中心密度 " +
                solidReport.DensityAtCentre.ToString("F4") + "）"
            );

            // (c) 挖掉表层 Voxel 后诊断：边界面必须都有 Mesh 覆盖
            Vector3Int minedVoxel = new Vector3Int(30, 14, 30);
            terrain.VoxelField.Set(minedVoxel, VoxelState.Air);
            terrain.RebuildChunksAffectedByVoxel(minedVoxel);

            MiningSurfaceDiagnosticReport minedReport =
                Diagnose(terrain, minedVoxel);

            int expectedFaces = 0;
            int missingFaces = 0;

            for (int i = 0; i < minedReport.Faces.Count; i++)
            {
                FaceDiagnostic face = minedReport.Faces[i];
                if (!face.ExpectsSurface) continue;

                expectedFaces++;

                if (!face.SurfaceFoundStrict) missingFaces++;
            }

            Check(
                expectedFaces > 0,
                "样例(c)：挖掘后诊断到应有表面（" + expectedFaces + " 个面）" +
                " ⇒ 诊断器确实在工作"
            );

            Check(
                missingFaces == 0,
                "样例(c)：挖掘后被挖 Voxel 邻域应有表面全部存在（缺失 " +
                missingFaces + "）"
            );

            Check(
                minedReport.Faces[2].OverrideFalseCount > 0 ||
                minedReport.Faces[2].ContributionFragmentCount > 0,
                "样例(c)：被挖 Voxel 的 +Y（旧地表所在面）已被处理" +
                "（显式隐藏覆写=" + minedReport.Faces[2].OverrideFalseCount +
                "，HiddenFaceContribution=" +
                minedReport.Faces[2].ContributionFragmentCount +
                "） ⇒ 旧表面不会留在 Assemble 输出里"
            );

            Check(
                minedReport.Faces[3].ContributionFragmentCount > 0,
                "样例(c)：被挖 Voxel 的 -Y（新暴露地板）由 HiddenFaceContribution 生成" +
                "（" + minedReport.Faces[3].ContributionFragmentCount +
                " 个片段 / " + minedReport.Faces[3].ContributionPolygonPointCount +
                " 个多边形点）"
            );

            Debug.Log(minedReport.ToText());
        }
        finally
        {
            Object.DestroyImmediate(terrainObject);
        }
    }

    private static void RunPartialCellDiagnostics()
    {
        GameObject terrainObject = CreateFixture(
            "MiningSurfaceDiagnostic_SceneLike", true, 15.7f);

        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();

            Vector3Int partialVoxel;
            MiningSurfaceDiagnosticReport partialReport;

            bool found = TryFindPartialCellVoxel(
                terrain, 24, 24, 44, 44, out partialVoxel, out partialReport);

            Check(
                found,
                "样例(d)：噪声地形中找到 partial cell Voxel" +
                (found ? " " + partialVoxel + "（实体占比 " +
                    partialReport.PartialSolidRatio.ToString("F3") + "）" : string.Empty)
            );

            if (found)
            {
                Debug.Log(partialReport.ToText());

                Check(
                    partialReport.Suspicion2PartialCell,
                    "样例(d)：partial cell 被正确标注（0 < 实体样本数 < 总数）"
                );
            }
        }
        finally
        {
            Object.DestroyImmediate(terrainObject);
        }
    }

    private static bool TryFindPartialCellVoxel(
        DensityTerrain terrain,
        int fromX,
        int fromZ,
        int toX,
        int toZ,
        out Vector3Int voxel,
        out MiningSurfaceDiagnosticReport report)
    {
        voxel = default(Vector3Int);
        report = null;

        for (int x = fromX; x <= toX; x++)
        {
            for (int z = fromZ; z <= toZ; z++)
            {
                for (int y = 20; y >= 8; y--)
                {
                    Vector3Int candidate = new Vector3Int(x, y, z);
                    MiningSurfaceDiagnosticReport candidateReport =
                        Diagnose(terrain, candidate);

                    if (!candidateReport.Suspicion2PartialCell) continue;

                    voxel = candidate;
                    report = candidateReport;
                    return true;
                }
            }
        }

        return false;
    }

    // =====================================================================
    // Mesh 覆盖判定（与 MiningSurfaceIntegrityValidation 相同判据）
    // =====================================================================

    private static bool ChunkMeshHasFace(
        TerrainChunk chunk,
        int axis,
        float plane,
        Vector3 faceCentre,
        float halfExtent,
        bool tolerant)
    {
        if (chunk == null) return false;

        Mesh mesh = chunk.Mesh;
        if (mesh == null) return false;

        Vector3[] vertices = mesh.vertices;
        int[] indices = mesh.triangles;
        Vector3 origin = chunk.ChunkOrigin;

        int t1 = axis == 0 ? 1 : 0;
        int t2 = axis == 2 ? 1 : 2;

        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            Vector3 a = vertices[indices[i]] + origin;
            Vector3 b = vertices[indices[i + 1]] + origin;
            Vector3 c = vertices[indices[i + 2]] + origin;

            if (tolerant)
            {
                Vector3 min = Vector3.Min(a, Vector3.Min(b, c));
                Vector3 max = Vector3.Max(a, Vector3.Max(b, c));

                if (faceCentre[t1] < min[t1] - halfExtent ||
                    faceCentre[t1] > max[t1] + halfExtent)
                {
                    continue;
                }

                if (faceCentre[t2] < min[t2] - halfExtent ||
                    faceCentre[t2] > max[t2] + halfExtent)
                {
                    continue;
                }

                if (plane < min[axis] - TolerantAxialWindow ||
                    plane > max[axis] + TolerantAxialWindow)
                {
                    continue;
                }

                return true;
            }

            Vector3 centroid = (a + b + c) / 3f;
            Vector3 normal = Vector3.Cross(b - a, c - a);

            if (normal.sqrMagnitude < 1e-12f) continue;

            Vector3 unitNormal = normal.normalized;

            if (Mathf.Abs(unitNormal[axis]) < NormalAlignment) continue;
            if (Mathf.Abs(centroid[axis] - plane) > PlaneTolerance) continue;
            if (Mathf.Abs(centroid[t1] - faceCentre[t1]) > halfExtent) continue;
            if (Mathf.Abs(centroid[t2] - faceCentre[t2]) > halfExtent) continue;

            return true;
        }

        return false;
    }

    // =====================================================================
    // 采样辅助
    // =====================================================================

    private static bool TrySample(
        TerrainChunk chunk,
        Vector3 terrainLocal,
        out float density)
    {
        string error;
        return VoxelAwareMeshRuntimeMutation.TrySampleBaseDensity(
            chunk, terrainLocal, out density, out error);
    }

    private static void SamplePartialCell(
        TerrainChunk chunk,
        Vector3Int voxel,
        float voxelSize,
        MiningSurfaceDiagnosticReport report)
    {
        int resolution = Mathf.Clamp(PartialSampleResolution, 2, 16);
        float step = 1f / resolution;

        report.PartialSamplesSolid = 0;
        report.PartialSamplesTotal = 0;
        report.PartialSampleFailures = 0;
        report.PartialMinDensity = float.MaxValue;
        report.PartialMaxDensity = float.MinValue;

        for (int ix = 0; ix < resolution; ix++)
        {
            for (int iy = 0; iy < resolution; iy++)
            {
                for (int iz = 0; iz < resolution; iz++)
                {
                    Vector3 local = new Vector3(
                        (voxel.x + (ix + 0.5f) * step) * voxelSize,
                        (voxel.y + (iy + 0.5f) * step) * voxelSize,
                        (voxel.z + (iz + 0.5f) * step) * voxelSize
                    );

                    float density;
                    if (!TrySample(chunk, local, out density))
                    {
                        report.PartialSampleFailures++;
                        continue;
                    }

                    report.PartialSamplesTotal++;

                    if (density > 0f) report.PartialSamplesSolid++;

                    if (density < report.PartialMinDensity)
                    {
                        report.PartialMinDensity = density;
                    }

                    if (density > report.PartialMaxDensity)
                    {
                        report.PartialMaxDensity = density;
                    }
                }
            }
        }

        report.PartialSolidRatio = report.PartialSamplesTotal > 0
            ? (float)report.PartialSamplesSolid / report.PartialSamplesTotal
            : 0f;
    }

    private static bool HasPositiveFaceProbe(
        MiningSurfaceDiagnosticReport report)
    {
        if (report.FaceProbeDensities == null) return false;

        for (int i = 0; i < report.FaceProbeDensities.Length; i++)
        {
            if (report.FaceProbeSampleOk != null &&
                i < report.FaceProbeSampleOk.Length &&
                report.FaceProbeSampleOk[i] &&
                report.FaceProbeDensities[i] > 0f)
            {
                return true;
            }
        }

        return false;
    }

    private static bool AnyFaceProbeSampled(
        MiningSurfaceDiagnosticReport report)
    {
        if (report.FaceProbeSampleOk == null) return false;

        for (int i = 0; i < report.FaceProbeSampleOk.Length; i++)
        {
            if (report.FaceProbeSampleOk[i]) return true;
        }

        return false;
    }

    // =====================================================================
    // 坐标 / 面标识辅助（镜像生产约定，不修改生产逻辑）
    // =====================================================================

    private static Vector3 VoxelCentre(Vector3Int voxel, float voxelSize)
    {
        return new Vector3(
            (voxel.x + 0.5f) * voxelSize,
            (voxel.y + 0.5f) * voxelSize,
            (voxel.z + 0.5f) * voxelSize
        );
    }

    /// <summary>面内侧探针：从该 Voxel 的对应面平面向 Voxel 内部偏移 1e-3。</summary>
    private static Vector3 FaceProbePoint(
        Vector3Int voxel,
        Vector3Int direction,
        float voxelSize)
    {
        int axis = direction.x != 0 ? 0 : (direction.y != 0 ? 1 : 2);
        int sign = direction[axis];

        float plane = (sign > 0 ? voxel[axis] + 1 : voxel[axis]) * voxelSize;

        Vector3 centre = VoxelCentre(voxel, voxelSize);
        centre[axis] = plane - sign * ProbeEpsilon;

        return centre;
    }

    private static Vector3 FaceCentre(
        Vector3Int voxel,
        int axis,
        float plane,
        float voxelSize)
    {
        Vector3 centre = VoxelCentre(voxel, voxelSize);
        centre[axis] = plane;
        return centre;
    }

    /// <summary>
    /// 镜像 VoxelAwareMeshRuntimeMutation.TryGetFaceForDirection 的映射（只读复刻）：
    ///   axis = 方向主轴；sign &gt; 0 ⇒ negative = voxel / positive = voxel + d
    ///                       否则 ⇒ negative = voxel + d / positive = voxel
    ///   plane = positive[axis]；u = negative[(axis+1)%3]；v = negative[(axis+2)%3]
    /// </summary>
    private static VoxelAwareMeshPrecut.FaceId BuildFaceId(
        Vector3Int voxel,
        Vector3Int direction,
        out Vector3Int negativeVoxel,
        out Vector3Int positiveVoxel)
    {
        int axis = direction.x != 0 ? 0 : (direction.y != 0 ? 1 : 2);
        int sign = direction[axis];

        negativeVoxel = sign > 0 ? voxel : voxel + direction;
        positiveVoxel = sign > 0 ? voxel + direction : voxel;

        int plane = positiveVoxel[axis];
        int u = negativeVoxel[(axis + 1) % 3];
        int v = negativeVoxel[(axis + 2) % 3];

        return new VoxelAwareMeshPrecut.FaceId(axis, plane, u, v);
    }

    private static bool IsExplicitlyStored(VoxelField field, Vector3Int voxel)
    {
        List<Vector3Int> stored = field.GetStoredCoordinates(VoxelState.Air);

        for (int i = 0; i < stored.Count; i++)
        {
            if (stored[i] == voxel) return true;
        }

        stored = field.GetStoredCoordinates(VoxelState.Solid);

        for (int i = 0; i < stored.Count; i++)
        {
            if (stored[i] == voxel) return true;
        }

        return false;
    }

    private static TerrainChunk ResolveChunk(
        DensityTerrain terrain,
        Vector3Int voxel,
        Vector3Int chunkVoxelSize)
    {
        TerrainChunk chunk;
        bool found = terrain.TryGetChunk(
            ChunkSpace.VoxelToChunk(voxel, chunkVoxelSize), out chunk);

        return found ? chunk : null;
    }

    private static TerrainChunk[] CollectChunks(DensityTerrain terrain)
    {
        TerrainChunkView[] views =
            terrain.GetComponentsInChildren<TerrainChunkView>(true);

        List<TerrainChunk> result = new List<TerrainChunk>(views.Length);

        for (int i = 0; i < views.Length; i++)
        {
            if (views[i] != null && views[i].Chunk != null)
            {
                result.Add(views[i].Chunk);
            }
        }

        return result.ToArray();
    }

    private static TerrainChunkView FindView(
        DensityTerrain terrain,
        Vector3Int coordinate)
    {
        TerrainChunkView[] views =
            terrain.GetComponentsInChildren<TerrainChunkView>(true);

        for (int i = 0; i < views.Length; i++)
        {
            if (views[i] != null && views[i].Coordinate == coordinate)
            {
                return views[i];
            }
        }

        return null;
    }

    private static GameObject CreateFixture(
        string name,
        bool useNoise,
        float height)
    {
        GameObject terrainObject = new GameObject(name);
        DensityTerrain terrain = terrainObject.AddComponent<DensityTerrain>();

        terrain.voxelSize = 1f;
        terrain.chunkVoxelSize = new Vector3Int(75, 37, 50);
        terrain.chunkCount = new Vector3Int(2, 1, 2);
        terrain.chunkStartCoordinate = Vector3Int.zero;
        terrain.resolution = 75;
        terrain.densityMode = DensityGenerationMode.HeightField;
        terrain.useNoise = useNoise;
        terrain.height = height;
        terrain.noiseScale = 0.15f;
        terrain.noiseStrength = 2.37f;
        terrain.regenerateInEditor = false;
        terrain.useVoxelAwareMeshPrecut = true;

        terrain.Regenerate();

        return terrainObject;
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

    // =====================================================================
    // 报告数据结构
    // =====================================================================

    public sealed class MiningSurfaceDiagnosticReport
    {
        public Vector3Int Voxel;
        public float VoxelSize;

        public VoxelState VoxelFieldState;
        public VoxelState VoxelFieldDefaultState;
        public bool VoxelFieldExplicitlyStored;
        public bool EditedAir;

        public Vector3Int ChunkCoordinate;
        public bool ChunkExists;

        public bool CentreSampleOk;
        public float DensityAtCentre;
        public bool[] FaceProbeSampleOk;
        public float[] FaceProbeDensities;

        public int PartialSamplesSolid;
        public int PartialSamplesTotal;
        public int PartialSampleFailures;
        public float PartialSolidRatio;
        public float PartialMinDensity;
        public float PartialMaxDensity;

        public bool HasMaterialInVoxel;
        public bool Suspicion1VoxelSolidButAir;
        public bool Suspicion2PartialCell;

        /// <summary>该 Voxel 自身基础片段（VoxelFragmentIndex）统计。</summary>
        public bool OwnFragmentIndexFound;
        public int OwnFragmentCount;
        public int OwnActiveFragmentCount;
        public int OwnHiddenOverrideCount;
        public int OwnVisibleOverrideCount;

        public bool LastMutationCommitted;
        public bool LastEvaluationFailed;
        public string LastEvaluationFailureMessage = string.Empty;
        public int LastReassembledChunkCount;
        public readonly List<Vector3Int> LastDirtyChunks = new List<Vector3Int>();
        public readonly List<Vector3Int> LastReassembledChunks =
            new List<Vector3Int>();

        public readonly List<FaceDiagnostic> Faces = new List<FaceDiagnostic>();
        public readonly List<ChunkDiagnostic> Chunks = new List<ChunkDiagnostic>();
        public readonly List<string> Findings = new List<string>();

        public string ToText()
        {
            StringBuilder text = new StringBuilder(2048);

            text.AppendLine("================ Mining Surface Diagnostic ================");
            text.AppendLine("Voxel=" + Voxel + "  VoxelSize=" + VoxelSize +
                "  Chunk=" + ChunkCoordinate +
                "（ChunkExists=" + ChunkExists + "）");
            text.AppendLine("VoxelField: State=" + VoxelFieldState +
                "（DefaultState=" + VoxelFieldDefaultState +
                "，显式存储=" + VoxelFieldExplicitlyStored +
                "，EditedAir=" + EditedAir + "）");

            text.AppendLine("DensityField（基础密度，Voxel 中心 / 6 面内侧探针）:");
            text.AppendLine("  中心        = " + Format(CentreSampleOk, DensityAtCentre));

            if (FaceProbeDensities != null)
            {
                for (int i = 0; i < FaceProbeDensities.Length &&
                    i < DirectionNames.Length; i++)
                {
                    bool ok = FaceProbeSampleOk != null &&
                              i < FaceProbeSampleOk.Length &&
                              FaceProbeSampleOk[i];

                    text.AppendLine("  " + DirectionNames[i].PadRight(3) +
                        " 面内侧 = " + Format(ok, FaceProbeDensities[i]));
                }
            }

            text.AppendLine("Partial cell: 实体样本 " + PartialSamplesSolid + "/" +
                PartialSamplesTotal +
                "（占比 " + PartialSolidRatio.ToString("F3") +
                "，失败 " + PartialSampleFailures +
                "，min=" + PartialMinDensity.ToString("F4") +
                "，max=" + PartialMaxDensity.ToString("F4") + "）");

            text.AppendLine("材料证据: HasMaterialInVoxel=" + HasMaterialInVoxel +
                "  怀疑1=" + Suspicion1VoxelSolidButAir +
                "  怀疑2=" + Suspicion2PartialCell);

            text.AppendLine("自身基础片段（VoxelFragmentIndex）: " +
                (OwnFragmentIndexFound
                    ? OwnFragmentCount + " 个（active=" + OwnActiveFragmentCount +
                      "，隐藏覆写=" + OwnHiddenOverrideCount +
                      "，可见覆写=" + OwnVisibleOverrideCount + "）"
                    : "该 Voxel 没有基础片段（无基岩表面经过它）"));

            text.AppendLine("最近一次 Mutation: committed=" + LastMutationCommitted +
                " evaluationFailed=" + LastEvaluationFailed +
                " reassembledChunks=" + LastReassembledChunkCount +
                " dirty=[" + Join(LastDirtyChunks) +
                "] reassembled=[" + Join(LastReassembledChunks) + "]");

            if (LastEvaluationFailureMessage.Length > 0)
            {
                text.AppendLine("  失败信息=" + LastEvaluationFailureMessage);
            }

            text.AppendLine("面（6 主轴方向）:");
            for (int i = 0; i < Faces.Count; i++)
            {
                FaceDiagnostic face = Faces[i];

                text.AppendLine(
                    "  " + face.Direction.PadRight(3) +
                    " FaceKey=" + face.FaceKey +
                    " boundary=" + face.IsBoundary +
                    "（-Solid=" + face.NegativeSolid +
                    " +Solid=" + face.PositiveSolid + "）" +
                    " expectsSurface=" + face.ExpectsSurface +
                    " solidProbe=" + (face.IsBoundary
                        ? Format(face.SolidSideProbeSampled, face.SolidSideProbeDensity)
                        : "<非边界（未采样）>") +
                    " meshStrict=" + face.SurfaceFoundStrict +
                    " meshTolerant=" + face.SurfaceFoundTolerant);

                text.AppendLine(
                    "      coplanarFragments=" + face.CoplanarFragmentCount +
                    "（active=" + face.ActiveFragmentCount +
                    "，覆写 true可见/false隐藏=" + face.OverrideTrueCount + "/" +
                    face.OverrideFalseCount + "）" +
                    " hiddenContribution=" + face.ContributionFragmentCount +
                    "（active=" + face.ContributionActiveCount +
                    "，polyPoints=" + face.ContributionPolygonPointCount + "）" +
                    " spatialFaceRecords=" + face.SpatialFaceRecordCount +
                    " decision=" + face.LastDecision +
                    " evalFailed=" + face.LastEvaluationFailed);
            }

            text.AppendLine("Chunk 概览:");
            for (int i = 0; i < Chunks.Count; i++)
            {
                ChunkDiagnostic chunk = Chunks[i];

                text.AppendLine(
                    "  " + chunk.Coordinate +
                    " mesh=" + chunk.VertexCount + "v/" + chunk.TriangleCount + "t" +
                    " fragments=" + chunk.FragmentCount +
                    " faceFragments=" + chunk.FaceFragmentCount +
                    " 覆写(隐藏/可见)=" + chunk.HiddenOverrideCount + "/" +
                    chunk.VisibleOverrideCount +
                    " hiddenContributionKeys=" + chunk.HiddenContributionKeyCount +
                    " viewSync(filter/collider)=" + chunk.MeshFilterMatches + "/" +
                    chunk.MeshColliderMatches);
            }

            text.AppendLine("结论:");
            for (int i = 0; i < Findings.Count; i++)
            {
                text.AppendLine("  · " + Findings[i]);
            }

            text.AppendLine("==========================================================");

            return text.ToString();
        }

        private static string Format(bool sampled, float density)
        {
            return sampled ? density.ToString("F4") : "<采样失败/出域>";
        }

        private static string Join(List<Vector3Int> list)
        {
            if (list == null || list.Count == 0) return string.Empty;

            StringBuilder text = new StringBuilder();

            for (int i = 0; i < list.Count && i < 12; i++)
            {
                if (i > 0) text.Append(",");
                text.Append(list[i]);
            }

            if (list.Count > 12) text.Append("...");

            return text.ToString();
        }
    }

    public sealed class FaceDiagnostic
    {
        public string Direction = string.Empty;
        public string FaceKey = string.Empty;
        public float Plane;
        public Vector3 FaceCentre;

        public Vector3Int NegativeVoxel;
        public Vector3Int PositiveVoxel;
        public Vector3Int SolidVoxel;
        public Vector3Int AirVoxel;

        public bool NegativeSolid;
        public bool PositiveSolid;
        public bool IsBoundary;
        public bool ExpectsSurface;

        public bool NegativeChunkExists;
        public bool PositiveChunkExists;

        public bool SolidSideProbeSampled;
        public float SolidSideProbeDensity;

        public bool SurfaceFoundStrict;
        public bool SurfaceFoundTolerant;

        public int CoplanarFragmentCount;
        public int ActiveFragmentCount;

        /// <summary>FaceFragmentVisibility 覆写：true = 显式可见。</summary>
        public int OverrideTrueCount;

        /// <summary>FaceFragmentVisibility 覆写：false = 显式隐藏（Assemble 会过滤掉）。</summary>
        public int OverrideFalseCount;

        public int ContributionFragmentCount;
        public int ContributionActiveCount;
        public int ContributionPolygonPointCount;

        public int SpatialFaceRecordCount;
        public string LastDecision = string.Empty;
        public bool LastEvaluationFailed;
        public string EvaluationFailureMessage = string.Empty;

        public readonly List<Vector3Int> ScannedChunks = new List<Vector3Int>();
    }

    public sealed class ChunkDiagnostic
    {
        public Vector3Int Coordinate;
        public bool Exists;
        public bool GeometryExists;

        public int VertexCount;
        public int TriangleCount;
        public int FragmentCount;
        public int FaceFragmentCount;
        public int HiddenOverrideCount;
        public int VisibleOverrideCount;
        public int HiddenContributionKeyCount;

        public bool ViewFound;
        public bool MeshFilterMatches;
        public bool MeshColliderMatches;
    }
}
