using System.Collections.Generic;
using TerrainModificationV2;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Coordinate Migration Stage B1 Validation — Density Sampling Dependency。
///
/// 用真实生产代码验证（不复制 floor/映射逻辑，不硬编码期望值）：
///   T1. 单 Chunk 内部 Voxel 的 Density 样本依赖（改动集合 == PositionToVoxel 预测集合，
///       无越界改动，改动值 == AirDensity）
///   T2. X=75 共享边界（两侧局部索引 / Terrain Local 位置 / Density 值；编辑跨边界 Voxel 的样本变化）
///   T3. Z=50 共享边界（同上）
///   T4. Y 轴非整数间距（CellSize.y == 37/75；sample index != VoxelCoordinate；
///       单 Chunk 全轴 Voxel 采样覆盖）
///   T5. 最终几何响应（内部 / X 边界 / Z 边界 / 地表 Voxel；Mesh 实际变化 + 变化局部性 +
///       未涉及 Chunk 不变）
///
/// 不修改任何生产代码；若发现缺陷，只记录证据。
/// </summary>
public static class VoxelCoordinateMigrationStageB1Validation
{
    private static readonly Vector3Int ChunkVoxelSize = new Vector3Int(75, 37, 50);
    private static readonly Vector3 ChunkWorldSize = new Vector3(75f, 37f, 50f);
    private const float Epsilon = 1e-4f;
    private const float AirDensity = -1f;

    private static int checks;
    private static int failures;

    [MenuItem("Tools/Terrain/Validate Coordinate Migration Stage B1")]
    public static void ValidateFromMenu()
    {
        RunAll();
        LogSummary();
    }

    public static void RunBatch()
    {
        try
        {
            RunAll();
        }
        catch (System.Exception exception)
        {
            failures++;
            Debug.LogError(
                "[VoxelCoordinateMigrationStageB1Validation] EXCEPTION: " +
                exception
            );
        }
        finally
        {
            LogSummary();
            EditorApplication.Exit(failures == 0 ? 0 : 1);
        }
    }

    private static void RunAll()
    {
        checks = 0;
        failures = 0;

        CheckInternalVoxelSampleDependency();
        CheckXSharedBoundary();
        CheckZSharedBoundary();
        CheckNonIntegerYSpacingAndCoverage();
        CheckGeometryResponse();
    }

    // =====================================================================
    // T1 — 单 Chunk 内部 Voxel 的样本依赖
    // =====================================================================

    private static void CheckInternalVoxelSampleDependency()
    {
        GameObject terrainObject = CreateSchemeATerrain(
            "StageB1_Internal",
            new Vector3Int(2, 1, 2),
            false
        );

        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();
            List<TerrainChunkView> views = GetViews(terrainObject);
            TerrainChunkView view = FindView(views, Vector3Int.zero);

            Check(
                view != null && view.Chunk != null,
                "StageB1-T1: Chunk (0,0,0) exists"
            );
            if (view == null) return;

            TerrainChunk chunk = view.Chunk;
            VoxelField field = terrain.VoxelField;
            Vector3Int voxel = new Vector3Int(30, 10, 30);

            Check(
                field.Get(voxel) == VoxelState.Solid,
                "StageB1-T1: fixture voxel (30,10,30) starts Solid"
            );

            DensityField before = terrain.BuildEffectiveDensityField(chunk);
            field.Set(voxel, VoxelState.Air);
            DensityField after = terrain.BuildEffectiveDensityField(chunk);

            List<Vector3Int> changed = CollectChangedSamples(before, after);
            List<Vector3Int> predicted = PredictSamplesMappedToVoxel(
                before,
                field,
                voxel
            );

            Check(
                predicted.Count > 0,
                "StageB1-T1: voxel (30,10,30) contains at least one Density sample " +
                "(predicted = " + predicted.Count + ")"
            );
            Check(
                changed.Count == predicted.Count &&
                SameSampleSet(changed, predicted),
                "StageB1-T1: changed samples == PositionToVoxel prediction (" +
                changed.Count + " vs " + predicted.Count + ")"
            );

            bool allAir = true;
            for (int i = 0; i < changed.Count; i++)
            {
                Vector3Int s = changed[i];
                if (Mathf.Abs(after[s.x, s.y, s.z] - AirDensity) > 1e-5f)
                {
                    allAir = false;
                }
            }

            Check(
                allAir,
                "StageB1-T1: every changed sample equals AirDensity (-1)"
            );

            LogSampleWindow("StageB1-T1", before, voxel);

            // 未修改区域（Chunk 内）不得发生变化：changed 即预测集合已覆盖。
            int outsideChanges = CountUnchangedViolations(before, after, predicted);
            Check(
                outsideChanges == 0,
                "StageB1-T1: no unintended sample changes outside the edited voxel " +
                "(violations = " + outsideChanges + ")"
            );

            // 还原，避免影响后续断言
            field.Set(voxel, VoxelState.Solid);
        }
        finally
        {
            Object.DestroyImmediate(terrainObject);
        }
    }

    // =====================================================================
    // T2 — X = 75 共享边界
    // =====================================================================

    private static void CheckXSharedBoundary()
    {
        GameObject terrainObject = CreateSchemeATerrain(
            "StageB1_XBoundary",
            new Vector3Int(2, 1, 2),
            false
        );

        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();
            List<TerrainChunkView> views = GetViews(terrainObject);
            TerrainChunkView view0 = FindView(views, Vector3Int.zero);
            TerrainChunkView view1 = FindView(views, new Vector3Int(1, 0, 0));

            Check(
                view0 != null && view1 != null,
                "StageB1-T2: Chunk (0,0,0) and (1,0,0) exist"
            );
            if (view0 == null || view1 == null) return;

            DensityField field0 = view0.Chunk.Density;
            DensityField field1 = view1.Chunk.Density;
            int lastX0 = field0.SampleCountX - 1;

            Vector3 pos0 = field0.GetLocalPosition(lastX0, 10, 10);
            Vector3 pos1 = field1.GetLocalPosition(0, 10, 10);

            Check(
                field0.SampleCountX == 76 && field1.SampleCountX == 76,
                "StageB1-T2: both Chunk DensityFields have 76 X samples"
            );
            Check(
                Mathf.Abs(pos0.x - 75f) < Epsilon &&
                Mathf.Abs(pos1.x - 75f) < Epsilon &&
                Mathf.Abs(pos0.x - pos1.x) < Epsilon,
                "StageB1-T2: shared plane is Terrain Local x = 75 on both sides " +
                "(local idx " + lastX0 + " vs 0; " + pos0 + " vs " + pos1 + ")"
            );

            float baseDelta = ComparePlane(
                field0, lastX0, field1, 0, "StageB1-T2 base"
            );
            Check(
                baseDelta < 1e-3f,
                "StageB1-T2: shared plane Base Density agrees (max delta = " +
                baseDelta.ToString("E2") + ")"
            );

            // 共享平面样本的 Voxel 归属：x 必须是 75，且两侧必须映射到同一 Voxel
            // （y/z 的 Voxel 由 floor(samplePos) 决定，不由 sample index 决定）
            Vector3Int voxelOfShared = terrain.VoxelField.PositionToVoxel(pos0);
            Vector3Int voxelOfShared1 = terrain.VoxelField.PositionToVoxel(pos1);
            Check(
                voxelOfShared.x == 75 &&
                voxelOfShared == voxelOfShared1,
                "StageB1-T2: shared X sample maps to global Voxel x=75 and both " +
                "sides agree (actual " + voxelOfShared + " / " + voxelOfShared1 + ")"
            );

            // 编辑跨边界 Voxel (75,5,10)：两侧字段的共享平面样本都应改变
            Vector3Int voxel = new Vector3Int(75, 5, 10);
            DensityField before0 = terrain.BuildEffectiveDensityField(view0.Chunk);
            DensityField before1 = terrain.BuildEffectiveDensityField(view1.Chunk);

            terrain.VoxelField.Set(voxel, VoxelState.Air);
            int rebuilt = terrain.RebuildChunksAffectedByVoxel(voxel);

            DensityField after0 = terrain.BuildEffectiveDensityField(view0.Chunk);
            DensityField after1 = terrain.BuildEffectiveDensityField(view1.Chunk);

            List<Vector3Int> changed0 = CollectChangedSamples(before0, after0);
            List<Vector3Int> changed1 = CollectChangedSamples(before1, after1);

            Check(
                changed0.Count > 0 && changed1.Count > 0,
                "StageB1-T2: editing boundary Voxel (75,5,10) changes samples on " +
                "BOTH sides (" + changed0.Count + " / " + changed1.Count + ")"
            );
            Check(
                changed0.Count == 4 && changed1.Count == 4,
                "StageB1-T2: expected 1×2×2 samples per side (actual " +
                changed0.Count + " / " + changed1.Count + ")"
            );
            Check(
                rebuilt == 2,
                "StageB1-T2: both neighbouring Chunks may contain the edit " +
                "(rebuilt = " + rebuilt + ")"
            );

            bool sideChanged = false;
            for (int i = 0; i < changed0.Count; i++)
            {
                if (changed0[i].x == lastX0) sideChanged = true;
            }

            bool otherSideChanged = false;
            for (int i = 0; i < changed1.Count; i++)
            {
                if (changed1[i].x == 0) otherSideChanged = true;
            }

            Check(
                sideChanged && otherSideChanged,
                "StageB1-T2: shared-plane samples (local idx 75 / 0) changed on both sides"
            );

            float postDelta = ComparePlane(
                after0, lastX0, after1, 0, "StageB1-T2 effective"
            );
            Check(
                postDelta < 1e-3f,
                "StageB1-T2: shared plane Effective Density agrees after edit " +
                "(max delta = " + postDelta.ToString("E2") + ")"
            );
        }
        finally
        {
            Object.DestroyImmediate(terrainObject);
        }
    }

    // =====================================================================
    // T3 — Z = 50 共享边界
    // =====================================================================

    private static void CheckZSharedBoundary()
    {
        GameObject terrainObject = CreateSchemeATerrain(
            "StageB1_ZBoundary",
            new Vector3Int(2, 1, 2),
            false
        );

        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();
            List<TerrainChunkView> views = GetViews(terrainObject);
            TerrainChunkView view0 = FindView(views, Vector3Int.zero);
            TerrainChunkView viewZ = FindView(views, new Vector3Int(0, 0, 1));

            Check(
                view0 != null && viewZ != null,
                "StageB1-T3: Chunk (0,0,0) and (0,0,1) exist"
            );
            if (view0 == null || viewZ == null) return;

            DensityField field0 = view0.Chunk.Density;
            DensityField fieldZ = viewZ.Chunk.Density;
            int lastZ0 = field0.SampleCountZ - 1;

            Vector3 pos0 = field0.GetLocalPosition(10, 10, lastZ0);
            Vector3 posZ = fieldZ.GetLocalPosition(10, 10, 0);

            Check(
                Mathf.Abs(pos0.z - 50f) < 1e-3f &&
                Mathf.Abs(posZ.z - 50f) < 1e-3f &&
                Mathf.Abs(pos0.z - posZ.z) < 1e-3f,
                "StageB1-T3: shared plane is Terrain Local z = 50 on both sides " +
                "(local idx " + lastZ0 + " vs 0; " + pos0 + " vs " + posZ + ")"
            );

            float baseDelta = ComparePlaneZ(
                field0, lastZ0, fieldZ, 0, "StageB1-T3 base"
            );
            Check(
                baseDelta < 1e-3f,
                "StageB1-T3: shared plane Base Density agrees (max delta = " +
                baseDelta.ToString("E2") + ")"
            );

            // 共享平面样本的 Voxel 归属：x=10、z=50 必须是 50 号 Voxel（y 由样本位置决定）
            Vector3Int voxelOfShared = terrain.VoxelField.PositionToVoxel(pos0);
            Vector3Int voxelZ = terrain.VoxelField.PositionToVoxel(posZ);
            Check(
                voxelOfShared.x == 10 && voxelOfShared.z == 50 &&
                voxelZ == voxelOfShared,
                "StageB1-T3: shared Z sample maps to Voxel (10,y,50) and both sides " +
                "agree (actual " + voxelOfShared + " / " + voxelZ + ")"
            );

            Vector3Int voxel = new Vector3Int(10, 5, 50);
            DensityField before0 = terrain.BuildEffectiveDensityField(view0.Chunk);
            DensityField beforeZ = terrain.BuildEffectiveDensityField(viewZ.Chunk);

            terrain.VoxelField.Set(voxel, VoxelState.Air);
            int rebuilt = terrain.RebuildChunksAffectedByVoxel(voxel);

            DensityField after0 = terrain.BuildEffectiveDensityField(view0.Chunk);
            DensityField afterZ = terrain.BuildEffectiveDensityField(viewZ.Chunk);

            List<Vector3Int> changed0 = CollectChangedSamples(before0, after0);
            List<Vector3Int> changedZ = CollectChangedSamples(beforeZ, afterZ);

            Check(
                changed0.Count > 0 && changedZ.Count > 0,
                "StageB1-T3: editing boundary Voxel (10,5,50) changes samples on " +
                "BOTH sides (" + changed0.Count + " / " + changedZ.Count + ")"
            );
            Check(
                rebuilt == 2,
                "StageB1-T3: both neighbouring Chunks may contain the edit " +
                "(rebuilt = " + rebuilt + ")"
            );

            float postDelta = ComparePlaneZ(
                after0, lastZ0, afterZ, 0, "StageB1-T3 effective"
            );
            Check(
                postDelta < 1e-3f,
                "StageB1-T3: shared plane Effective Density agrees after edit " +
                "(max delta = " + postDelta.ToString("E2") + ")"
            );
        }
        finally
        {
            Object.DestroyImmediate(terrainObject);
        }
    }

    // =====================================================================
    // T4 — Y 轴非整数间距 + 单 Chunk 全轴采样覆盖
    // =====================================================================

    private static void CheckNonIntegerYSpacingAndCoverage()
    {
        GameObject terrainObject = CreateSchemeATerrain(
            "StageB1_YSpacing",
            new Vector3Int(1, 1, 1),
            false
        );

        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();
            List<TerrainChunkView> views = GetViews(terrainObject);
            TerrainChunk chunk = views.Count > 0 ? views[0].Chunk : null;

            Check(chunk != null, "StageB1-T4: fixture has 1 Chunk");
            if (chunk == null || chunk.Density == null) return;

            DensityField field = chunk.Density;

            Check(
                Mathf.Abs(field.CellSizeY - 37f / 75f) < 1e-6f &&
                Mathf.Abs(field.CellSizeX - 1f) < 1e-6f &&
                Mathf.Abs(field.CellSizeZ - 50f / 75f) < 1e-6f,
                "StageB1-T4: CellSize == (1, 37/75, 50/75) " +
                "(actual " + field.CellSizeX + ", " + field.CellSizeY + ", " +
                field.CellSizeZ + ")"
            );
            Check(
                field.SampleCountY == 76,
                "StageB1-T4: Y sample count == 76"
            );

            // sample index 不是 VoxelCoordinate：index 5 -> y=2.4667 -> Voxel 2
            Vector3 p5 = field.GetLocalPosition(0, 5, 0);
            Vector3Int voxelOfSample5 = terrain.VoxelField.PositionToVoxel(p5);
            Check(
                voxelOfSample5.y == 2 && voxelOfSample5.y != 5,
                "StageB1-T4: sample index 5 maps to Voxel y=2 (not 5) " +
                "(pos.y=" + p5.y + " -> " + voxelOfSample5 + ")"
            );

            // 每个 Voxel 的样本数：Voxel y=5 覆盖 [5,6)，应为 2 个样本（11、12）
            List<Vector3Int> y5 = PredictSamplesMappedToVoxel(
                field,
                terrain.VoxelField,
                new Vector3Int(0, 5, 0)
            );
            HashSet<int> yIndices = new HashSet<int>();
            HashSet<int> zIndices = new HashSet<int>();
            for (int i = 0; i < y5.Count; i++)
            {
                yIndices.Add(y5[i].y);
                zIndices.Add(y5[i].z);
            }

            Check(
                yIndices.Count == 2,
                "StageB1-T4: Voxel (0,5,0) contains 2 Density samples on Y " +
                "(actual " + yIndices.Count + ", indices " +
                string.Join(",", yIndices) + ")"
            );

            // 全轴覆盖：单 Chunk 每个 Voxel 轴区间至少含 1 个样本
            int uncoveredX = CountUncoveredVoxelSlabs(field, 0, 75, terrain.VoxelField);
            int uncoveredY = CountUncoveredVoxelSlabs(field, 1, 37, terrain.VoxelField);
            int uncoveredZ = CountUncoveredVoxelSlabs(field, 2, 50, terrain.VoxelField);

            Check(
                uncoveredX == 0 && uncoveredY == 0 && uncoveredZ == 0,
                "StageB1-T4: every Voxel slab has >= 1 Density sample on every axis " +
                "(uncovered X/Y/Z = " + uncoveredX + "/" + uncoveredY + "/" +
                uncoveredZ + ")"
            );

            // 3D 抽样：代表 Voxel（角落 / 中心 / 边界内侧 / Y 最薄处）
            Vector3Int[] probeVoxels =
            {
                new Vector3Int(0, 0, 0),
                new Vector3Int(30, 10, 30),
                new Vector3Int(74, 36, 49),
                new Vector3Int(74, 0, 0),
                new Vector3Int(0, 36, 0),
                new Vector3Int(0, 0, 49),
                new Vector3Int(12, 5, 12)
            };

            bool allCovered = true;
            for (int i = 0; i < probeVoxels.Length; i++)
            {
                List<Vector3Int> inside = PredictSamplesMappedToVoxel(
                    field,
                    terrain.VoxelField,
                    probeVoxels[i]
                );

                if (inside.Count == 0)
                {
                    allCovered = false;
                    Debug.LogWarning(
                        "[StageB1-T4] Voxel " + probeVoxels[i] +
                        " has no Density sample"
                    );
                }
            }

            Check(
                allCovered,
                "StageB1-T4: representative 3D Voxels all contain >= 1 Density sample"
            );
        }
        finally
        {
            Object.DestroyImmediate(terrainObject);
        }
    }

    // =====================================================================
    // T5 — 最终几何响应（Effective Density 路径）
    // =====================================================================

    private static void CheckGeometryResponse()
    {
        // 使用 Effective Density（MC）路径：switch OFF 时 RebuildChunksAffectedByVoxel
        // 会从 Base + VoxelField 重建 Effective Density 并重新生成 Mesh，
        // 这正是 Density 样本 → Marching Cubes → 最终 Mesh 的依赖链。
        GameObject terrainObject = CreateSchemeATerrain(
            "StageB1_Geometry",
            new Vector3Int(2, 1, 2),
            false
        );

        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();
            List<TerrainChunkView> views = GetViews(terrainObject);

            Check(
                views.Count == 4,
                "StageB1-T5: fixture has 4 Chunks (effective density path)"
            );
            if (views.Count != 4) return;

            CheckGeometryCase(terrain, views, new Vector3Int(30, 10, 30), "internal");
            CheckGeometryCase(terrain, views, new Vector3Int(75, 5, 10), "X boundary");
            CheckGeometryCase(terrain, views, new Vector3Int(10, 5, 50), "Z boundary");
            CheckGeometryCase(terrain, views, new Vector3Int(30, 15, 30), "surface");
        }
        finally
        {
            Object.DestroyImmediate(terrainObject);
        }
    }

    private static void CheckGeometryCase(
        DensityTerrain terrain,
        List<TerrainChunkView> views,
        Vector3Int voxel,
        string label)
    {
        Dictionary<TerrainChunk, Mesh> beforeMeshes =
            new Dictionary<TerrainChunk, Mesh>();
        Dictionary<TerrainChunk, MeshStats> beforeStats =
            new Dictionary<TerrainChunk, MeshStats>();
        Dictionary<TerrainChunk, DensityField> beforeFields =
            new Dictionary<TerrainChunk, DensityField>();
        Dictionary<TerrainChunk, HashSet<string>> beforeKeys =
            new Dictionary<TerrainChunk, HashSet<string>>();

        for (int i = 0; i < views.Count; i++)
        {
            TerrainChunk chunk = views[i].Chunk;
            beforeMeshes[chunk] = chunk.Mesh;
            beforeStats[chunk] = ComputeMeshStats(chunk.Mesh);
            beforeFields[chunk] = terrain.BuildEffectiveDensityField(chunk);
            beforeKeys[chunk] = BuildPositionKeys(chunk.Mesh);
        }

        terrain.VoxelField.Set(voxel, VoxelState.Air);
        int rebuilt = terrain.RebuildChunksAffectedByVoxel(voxel);

        int sampleChangeTotal = 0;
        int meshChangedCount = 0;
        int unintendedChunkChanges = 0;
        bool deltaLocalized = true;
        string deltaDiagnostics = string.Empty;
        MeshStats changedStats = null;

        for (int i = 0; i < views.Count; i++)
        {
            TerrainChunk chunk = views[i].Chunk;
            DensityField after = terrain.BuildEffectiveDensityField(chunk);
            int sampleChanges = CollectChangedSamples(
                beforeFields[chunk],
                after
            ).Count;
            sampleChangeTotal += sampleChanges;

            MeshStats statsAfter = ComputeMeshStats(chunk.Mesh);
            bool meshChanged =
                chunk.Mesh != beforeMeshes[chunk] ||
                statsAfter.VertexCount != beforeStats[chunk].VertexCount ||
                statsAfter.TriangleCount != beforeStats[chunk].TriangleCount;

            if (meshChanged)
            {
                meshChangedCount++;
                changedStats = statsAfter;

                if (IsMeshDeltaLocal(beforeKeys[chunk], chunk.Mesh,
                    beforeFields[chunk], voxel))
                {
                    deltaDiagnostics += " " + chunk.Coordinate + ":ok";
                }
                else
                {
                    deltaLocalized = false;
                    deltaDiagnostics += " " + chunk.Coordinate + ":FAIL";
                }
            }
            else if (sampleChanges > 0)
            {
                // 该 Chunk 的样本变了但 Mesh 未变 ⇒ 需要诊断
                unintendedChunkChanges++;
            }
        }

        Check(
            sampleChangeTotal > 0,
            "StageB1-T5[" + label + "]: Voxel " + voxel +
            " changes Density samples (total = " + sampleChangeTotal + ")"
        );
        Check(
            rebuilt > 0,
            "StageB1-T5[" + label + "]: RebuildChunksAffectedByVoxel rebuilt " +
            rebuilt + " Chunk(s)"
        );
        Check(
            meshChangedCount > 0,
            "StageB1-T5[" + label + "]: Final Mesh changed for " +
            meshChangedCount + " Chunk(s)"
        );
        Check(
            unintendedChunkChanges == 0,
            "StageB1-T5[" + label + "]: no Chunk with changed samples but unchanged Mesh " +
            "(violations = " + unintendedChunkChanges + ")"
        );
        Check(
            deltaLocalized,
            "StageB1-T5[" + label + "]: Mesh delta stays local to the edited Voxel" +
            deltaDiagnostics
        );

        Debug.Log(
            "[StageB1-T5] " + label + " voxel=" + voxel +
            " rebuilt=" + rebuilt +
            " sampleChanges=" + sampleChangeTotal +
            " changedChunks=" + meshChangedCount +
            (changedStats != null
                ? " mesh(v=" + changedStats.VertexCount +
                  ",t=" + changedStats.TriangleCount + ")"
                : string.Empty)
        );
    }

    // =====================================================================
    // Helpers — 采样依赖
    // =====================================================================

    private static List<Vector3Int> CollectChangedSamples(
        DensityField before,
        DensityField after)
    {
        List<Vector3Int> changed = new List<Vector3Int>();
        if (before == null || after == null) return changed;

        for (int x = 0; x < before.SampleCountX; x++)
        {
            for (int y = 0; y < before.SampleCountY; y++)
            {
                for (int z = 0; z < before.SampleCountZ; z++)
                {
                    if (Mathf.Abs(before[x, y, z] - after[x, y, z]) > 1e-6f)
                    {
                        changed.Add(new Vector3Int(x, y, z));
                    }
                }
            }
        }

        return changed;
    }

    private static List<Vector3Int> PredictSamplesMappedToVoxel(
        DensityField field,
        VoxelField voxelField,
        Vector3Int voxel)
    {
        List<Vector3Int> predicted = new List<Vector3Int>();

        for (int x = 0; x < field.SampleCountX; x++)
        {
            for (int y = 0; y < field.SampleCountY; y++)
            {
                for (int z = 0; z < field.SampleCountZ; z++)
                {
                    Vector3 position = field.GetLocalPosition(x, y, z);
                    if (voxelField.PositionToVoxel(position) == voxel)
                    {
                        predicted.Add(new Vector3Int(x, y, z));
                    }
                }
            }
        }

        return predicted;
    }

    private static bool SameSampleSet(
        List<Vector3Int> a,
        List<Vector3Int> b)
    {
        HashSet<string> set = new HashSet<string>();
        for (int i = 0; i < a.Count; i++) set.Add(Key(a[i]));
        for (int i = 0; i < b.Count; i++)
        {
            if (!set.Contains(Key(b[i]))) return false;
        }

        return true;
    }

    private static int CountUnchangedViolations(
        DensityField before,
        DensityField after,
        List<Vector3Int> predicted)
    {
        HashSet<string> allowed = new HashSet<string>();
        for (int i = 0; i < predicted.Count; i++)
        {
            allowed.Add(Key(predicted[i]));
        }

        int violations = 0;
        for (int x = 0; x < before.SampleCountX; x++)
        {
            for (int y = 0; y < before.SampleCountY; y++)
            {
                for (int z = 0; z < before.SampleCountZ; z++)
                {
                    if (Mathf.Abs(before[x, y, z] - after[x, y, z]) > 1e-6f &&
                        !allowed.Contains(Key(new Vector3Int(x, y, z))))
                    {
                        violations++;
                    }
                }
            }
        }

        return violations;
    }

    private static string Key(Vector3Int value)
    {
        return value.x + ":" + value.y + ":" + value.z;
    }

    private static float ComparePlane(
        DensityField a,
        int indexA,
        DensityField b,
        int indexB,
        string label)
    {
        float maxDelta = 0f;

        for (int y = 0; y < a.SampleCountY && y < b.SampleCountY; y++)
        {
            for (int z = 0; z < a.SampleCountZ && z < b.SampleCountZ; z++)
            {
                float delta = Mathf.Abs(a[indexA, y, z] - b[indexB, y, z]);
                if (delta > maxDelta) maxDelta = delta;
            }
        }

        Debug.Log(
            "[" + label + "] plane(" + indexA + " vs " + indexB +
            ") maxDelta=" + maxDelta.ToString("E3")
        );

        return maxDelta;
    }

    private static float ComparePlaneZ(
        DensityField a,
        int indexA,
        DensityField b,
        int indexB,
        string label)
    {
        float maxDelta = 0f;

        for (int x = 0; x < a.SampleCountX && x < b.SampleCountX; x++)
        {
            for (int y = 0; y < a.SampleCountY && y < b.SampleCountY; y++)
            {
                float delta = Mathf.Abs(a[x, y, indexA] - b[x, y, indexB]);
                if (delta > maxDelta) maxDelta = delta;
            }
        }

        Debug.Log(
            "[" + label + "] plane z(" + indexA + " vs " + indexB +
            ") maxDelta=" + maxDelta.ToString("E3")
        );

        return maxDelta;
    }

    private static int CountUncoveredVoxelSlabs(
        DensityField field,
        int axis,
        int voxelCount,
        VoxelField voxelField)
    {
        int uncovered = 0;

        for (int v = 0; v < voxelCount; v++)
        {
            bool covered = false;
            int samples = axis == 0
                ? field.SampleCountX
                : axis == 1 ? field.SampleCountY : field.SampleCountZ;

            for (int i = 0; i < samples && !covered; i++)
            {
                Vector3 position = axis == 0
                    ? field.GetLocalPosition(i, 0, 0)
                    : axis == 1
                        ? field.GetLocalPosition(0, i, 0)
                        : field.GetLocalPosition(0, 0, i);

                Vector3Int coordinate = voxelField.PositionToVoxel(position);
                int value = axis == 0
                    ? coordinate.x
                    : axis == 1 ? coordinate.y : coordinate.z;

                if (value == v) covered = true;
            }

            if (!covered) uncovered++;
        }

        return uncovered;
    }

    private static void LogSampleWindow(
        string label,
        DensityField field,
        Vector3Int voxel)
    {
        int minIndex = 0;

        for (int i = 0; i < field.SampleCountX; i++)
        {
            if (field.GetLocalPosition(i, 0, 0).x >= voxel.x - 1e-6f)
            {
                minIndex = i;
                break;
            }
        }

        Debug.Log(
            "[" + label + "] voxel=" + voxel +
            " firstSampleIndex>=voxelMin.x: " + minIndex +
            " (pos.x=" + field.GetLocalPosition(minIndex, 0, 0).x + ")"
        );
    }

    // =====================================================================
    // Helpers — Mesh
    // =====================================================================

    private sealed class MeshStats
    {
        public int VertexCount;
        public int TriangleCount;
        public float Area;
    }

    private static MeshStats ComputeMeshStats(Mesh mesh)
    {
        MeshStats stats = new MeshStats();
        if (mesh == null) return stats;

        Vector3[] vertices = mesh.vertices;
        stats.VertexCount = vertices.Length;

        for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
        {
            int[] triangles = mesh.GetTriangles(submesh);

            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                Vector3 a = vertices[triangles[i]];
                Vector3 b = vertices[triangles[i + 1]];
                Vector3 c = vertices[triangles[i + 2]];

                stats.TriangleCount++;
                stats.Area += Vector3.Cross(b - a, c - a).magnitude * 0.5f;
            }
        }

        return stats;
    }

    /// <summary>
    /// Mesh 变化是否局限在被编辑 Voxel 附近：
    /// 只允许“新增/消失”的顶点落在 voxel 盒 + 1.5×最大 CellSize 范围内。
    ///
    /// 注意：必须在重建之前快照顶点位置键——重建会 Destroy 旧 Mesh，
    /// 事后持有旧 Mesh 引用比较会得到 null（Unity 的 destroyed 语义）。
    /// 另外 Mesh 顶点是 Chunk 局部坐标，窗口需减去 DensityField.Origin 再比较。
    /// </summary>
    private static bool IsMeshDeltaLocal(
        HashSet<string> beforeKeys,
        Mesh after,
        DensityField beforeField,
        Vector3Int voxel)
    {
        if (after == null) return false;

        float margin = 1.5f * Mathf.Max(
            beforeField.CellSizeX,
            Mathf.Max(beforeField.CellSizeY, beforeField.CellSizeZ)
        );
        margin += 1e-3f;

        Vector3 origin = beforeField.Origin;
        Vector3 min = new Vector3(voxel.x, voxel.y, voxel.z) -
                      new Vector3(margin, margin, margin) - origin;
        Vector3 max = new Vector3(voxel.x + 1, voxel.y + 1, voxel.z + 1) +
                      new Vector3(margin, margin, margin) - origin;

        HashSet<string> afterKeys = BuildPositionKeys(after);

        foreach (string key in afterKeys)
        {
            if (beforeKeys.Contains(key)) continue;
            if (!IsInsideDeltaWindow(ParseKey(key), min, max, voxel,
                origin, "new"))
            {
                return false;
            }
        }

        foreach (string key in beforeKeys)
        {
            if (afterKeys.Contains(key)) continue;
            if (!IsInsideDeltaWindow(ParseKey(key), min, max, voxel,
                origin, "removed"))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsInsideDeltaWindow(
        Vector3 local,
        Vector3 min,
        Vector3 max,
        Vector3Int voxel,
        Vector3 origin,
        string kind)
    {
        if (local.x >= min.x && local.x <= max.x &&
            local.y >= min.y && local.y <= max.y &&
            local.z >= min.z && local.z <= max.z)
        {
            return true;
        }

        Debug.LogWarning(
            "[StageB1-T5] " + kind + " vertex outside local window: local=" +
            local + " global=" + (local + origin) + " voxel=" + voxel
        );
        return false;
    }

    private static HashSet<string> BuildPositionKeys(Mesh mesh)
    {
        HashSet<string> keys = new HashSet<string>();
        if (mesh == null) return keys;

        Vector3[] vertices = mesh.vertices;

        for (int i = 0; i < vertices.Length; i++)
        {
            keys.Add(PositionKey(vertices[i]));
        }

        return keys;
    }

    private static string PositionKey(Vector3 position)
    {
        return Mathf.RoundToInt(position.x * 1000f) + ":" +
               Mathf.RoundToInt(position.y * 1000f) + ":" +
               Mathf.RoundToInt(position.z * 1000f);
    }

    private static Vector3 ParseKey(string key)
    {
        string[] parts = key.Split(':');
        return new Vector3(
            int.Parse(parts[0]) / 1000f,
            int.Parse(parts[1]) / 1000f,
            int.Parse(parts[2]) / 1000f
        );
    }

    // =====================================================================
    // Helpers — Terrain
    // =====================================================================

    private static GameObject CreateSchemeATerrain(
        string name,
        Vector3Int chunkCount,
        bool voxelAware)
    {
        GameObject terrainObject = new GameObject(name);

        DensityTerrain terrain = terrainObject.AddComponent<DensityTerrain>();
        terrain.voxelSize = 1f;
        terrain.chunkVoxelSize = ChunkVoxelSize;
        terrain.chunkCount = chunkCount;
        terrain.chunkStartCoordinate = Vector3Int.zero;
        terrain.resolution = 75;
        terrain.densityMode = DensityGenerationMode.HeightField;
        terrain.useNoise = false;
        terrain.height = 15.7f;
        terrain.cavityRadius = 0f;
        terrain.debugCutaway = false;
        terrain.regenerateInEditor = false;
        terrain.useVoxelAwareMeshPrecut = voxelAware;
        terrain.Regenerate();

        return terrainObject;
    }

    private static List<TerrainChunkView> GetViews(GameObject terrainObject)
    {
        TerrainChunkView[] array = terrainObject
            .GetComponentsInChildren<TerrainChunkView>(true);

        return new List<TerrainChunkView>(array);
    }

    private static TerrainChunkView FindView(
        List<TerrainChunkView> views,
        Vector3Int coordinate)
    {
        for (int i = 0; i < views.Count; i++)
        {
            if (views[i] != null && views[i].Coordinate == coordinate)
            {
                return views[i];
            }
        }

        return null;
    }

    private static void Check(bool condition, string message)
    {
        checks++;

        if (condition)
        {
            Debug.Log("PASS - " + message);
        }
        else
        {
            failures++;
            Debug.LogError("FAIL - " + message);
        }
    }

    private static void LogSummary()
    {
        Debug.Log(
            "[VoxelCoordinateMigrationStageB1Validation] ===== SUMMARY =====\n" +
            "Checks = " + checks + "\n" +
            "Failures = " + failures + "\n" +
            "Result = " + (failures == 0 ? "PASS" : "FAIL")
        );
    }
}
