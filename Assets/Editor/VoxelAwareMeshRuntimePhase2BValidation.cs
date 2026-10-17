using System.Collections.Generic;
using TerrainModificationV2;
using UnityEditor;
using UnityEngine;

/// <summary>
/// VoxelAwareMesh Runtime Phase 2B Validation。
/// 验证 VoxelField Solid ↔ Air Mutation 驱动的局部 Fragment / SpatialFace /
/// Hidden Face / Final Mesh Reassembly。
/// </summary>
public static class VoxelAwareMeshRuntimePhase2BValidation
{
    private static int failures;
    private static int checks;

    [MenuItem("Tools/Terrain/Validate VoxelAwareMesh Runtime Phase 2B")]
    public static void ValidateFromMenu()
    {
        RunAll();
        LogSummary();
        Debug.Log(
            "[VoxelAwareMeshRuntimePhase2BValidation] Menu result = " +
            (failures == 0 ? "PASS" : "FAIL")
        );
    }

    public static void RunBatch()
    {
        RunAll();
        LogSummary();
        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    private static void RunAll()
    {
        failures = 0;
        checks = 0;

        CheckSingleVoxelExcavationAndRestore();
        CheckNeighborPreservation();
        CheckHiddenFaceLifecycle();
        CheckCrossChunkMutation();
        CheckNonIdentityTransform();
        CheckRepeatedMutation();
        CheckMultiVoxelMutation();
        CheckBaseGeometryImmutability();
        CheckMultipleRegionsAndMaterial();
        CheckBaseDensityDomainGuard();
        CheckFaceFragmentVisibilityFilter();
        CheckFaceFragmentVisibilityRule();
        CheckBoundaryGraphDedup();
        CheckDirtyPropagationSets();
        CheckDomainGapAudit();
        CheckDisposeAudit();
    }

    private static void CheckSingleVoxelExcavationAndRestore()
    {
        GameObject terrainObject = CreateTerrain(
            new Vector3Int(1, 1, 1),
            out DensityTerrain terrain,
            out List<TerrainChunk> chunks,
            out List<TerrainChunkView> views
        );

        try
        {
            VoxelField field = terrain.VoxelField;
            Vector3Int voxel;
            if (!TryGetFragmentVoxel(chunks[0].Geometry, out voxel))
            {
                Check(false, "R2B-1: found a voxel with fragments");
                return;
            }

            MeshStats before = MeshStatsOf(chunks[0].Mesh);
            int fragmentCountBefore = CountActiveFragments(
                chunks[0].Geometry,
                voxel
            );

            Apply(field, voxel, VoxelState.Air, chunks, views);

            Check(
                CountActiveFragments(chunks[0].Geometry, voxel) == 0,
                "R2B-1: excavated voxel fragments inactive"
            );
            Check(
                fragmentCountBefore > 0,
                "R2B-1: voxel had active fragments before mutation"
            );
            Check(
                chunks[0].Mesh != null,
                "R2B-1: Final Mesh exists after excavation"
            );
            Check(
                VoxelAwareMeshRuntimeMutation.LastEvaluatedFaceCount == 6,
                "R2B-1: six SpatialFaces re-evaluated"
            );

            Apply(field, voxel, VoxelState.Solid, chunks, views);

            Check(
                CountActiveFragments(chunks[0].Geometry, voxel) ==
                fragmentCountBefore,
                "R2B-2: restore re-activates voxel fragments"
            );

            MeshStats after = MeshStatsOf(chunks[0].Mesh);
            Check(
                before.VertexCount == after.VertexCount &&
                before.TriangleCount == after.TriangleCount &&
                before.SubmeshCount == after.SubmeshCount &&
                Mathf.Abs(before.Area - after.Area) < 1e-3f,
                "R2B-2: Final Mesh restored to initial stats"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    private static void CheckNeighborPreservation()
    {
        GameObject terrainObject = CreateTerrain(
            new Vector3Int(1, 1, 1),
            out DensityTerrain terrain,
            out List<TerrainChunk> chunks,
            out List<TerrainChunkView> views
        );

        try
        {
            VoxelField field = terrain.VoxelField;
            Vector3Int voxelA;
            Vector3Int voxelB;
            if (!TryGetTwoFragmentVoxels(
                chunks[0].Geometry,
                out voxelA,
                out voxelB))
            {
                Check(false, "R2B-3: found two voxels with fragments");
                return;
            }

            bool[] activeBefore = ActiveFragments(
                chunks[0].Geometry,
                voxelB
            );

            Apply(field, voxelA, VoxelState.Air, chunks, views);

            bool[] activeAfter = ActiveFragments(
                chunks[0].Geometry,
                voxelB
            );

            bool unchanged = activeBefore.Length == activeAfter.Length;
            if (unchanged)
            {
                for (int i = 0; i < activeBefore.Length; i++)
                {
                    if (activeBefore[i] != activeAfter[i]) unchanged = false;
                }
            }

            Check(unchanged, "R2B-3: neighbor Base Fragments unchanged");
            Check(
                chunks[0].RawMesh != null,
                "R2B-3: RawMesh preserved"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    private static void CheckHiddenFaceLifecycle()
    {
        GameObject terrainObject = CreateTerrain(
            new Vector3Int(2, 1, 1),
            out DensityTerrain terrain,
            out List<TerrainChunk> chunks,
            out List<TerrainChunkView> views
        );

        try
        {
            VoxelField field = terrain.VoxelField;
            Vector3Int voxelA;
            Vector3Int voxelB;
            if (!TryGetTwoFragmentVoxels(
                chunks[0].Geometry,
                out voxelA,
                out voxelB))
            {
                Check(false, "R2B-4: found two fragment voxels");
                return;
            }

            Apply(field, voxelA, VoxelState.Air, chunks, views);
            int contributionFacesAfterAir = CountContributionFaces(chunks);

            Apply(field, voxelB, VoxelState.Air, chunks, views);
            int contributionFacesAfterSecondAir =
                CountContributionFaces(chunks);

            Check(
                NoDuplicateFaceContributions(chunks),
                "R2B-5: same FaceIdentity result replaced, not accumulated"
            );

            Apply(field, voxelA, VoxelState.Solid, chunks, views);
            Apply(field, voxelB, VoxelState.Solid, chunks, views);

            Check(
                CountContributions(chunks) == 0,
                "R2B-5: Hidden Face removed after Solid/Solid restore"
            );

            Check(
                NoDuplicateFaceContributions(chunks),
                "R2B-4/R2B-9: exactly one contribution per FaceIdentity"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    private static void CheckCrossChunkMutation()
    {
        GameObject terrainObject = CreateTerrain(
            new Vector3Int(2, 1, 1),
            out DensityTerrain terrain,
            out List<TerrainChunk> chunks,
            out List<TerrainChunkView> views
        );

        try
        {
            VoxelField field = terrain.VoxelField;
            Vector3Int voxel = new Vector3Int(15, 0, 8);

            Apply(field, voxel, VoxelState.Air, chunks, views);

            Check(
                VoxelAwareMeshRuntimeMutation.LastEvaluatedFaceCount == 6,
                "R2B-6/R2B-8: cross-chunk mutation evaluates six faces once"
            );

            Check(
                NoDuplicateFaceContributions(chunks),
                "R2B-9: no duplicate Hidden Face across chunks"
            );

            Apply(field, voxel, VoxelState.Solid, chunks, views);

            Check(
                CountContributions(chunks) == 0,
                "R2B-6: cross-chunk restore removes hidden contribution"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    private static void CheckNonIdentityTransform()
    {
        GameObject terrainObject = CreateTerrain(
            new Vector3Int(1, 1, 1),
            out DensityTerrain terrain,
            out List<TerrainChunk> chunks,
            out List<TerrainChunkView> views
        );

        try
        {
            terrainObject.transform.position = new Vector3(100f, 50f, -30f);
            terrainObject.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            terrainObject.transform.localScale = new Vector3(1.5f, 1f, 2f);
            terrain.Regenerate();
            views = GetViews(terrainObject);
            chunks = GetChunks(views);

            VoxelField field = terrain.VoxelField;
            Vector3Int voxel;
            if (!TryGetFragmentVoxel(chunks[0].Geometry, out voxel))
            {
                Check(false, "R2B-14: found fragment voxel");
                return;
            }

            Bounds localBefore = chunks[0].Mesh.bounds;
            Apply(field, voxel, VoxelState.Air, chunks, views);

            Check(
                Vector3.Distance(chunks[0].Mesh.bounds.center,
                    localBefore.center) < 1e-4f ||
                chunks[0].Mesh != null,
                "R2B-14: mutation keeps Chunk-local Mesh"
            );

            Vector3 expectedWorld = terrainObject.transform.TransformPoint(
                chunks[0].ChunkOrigin
            );
            Check(
                Vector3.Distance(views[0].transform.position, expectedWorld) <
                1e-3f,
                "R2B-14: world placement follows Terrain Transform"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    private static void CheckRepeatedMutation()
    {
        GameObject terrainObject = CreateTerrain(
            new Vector3Int(1, 1, 1),
            out DensityTerrain terrain,
            out List<TerrainChunk> chunks,
            out List<TerrainChunkView> views
        );

        try
        {
            VoxelField field = terrain.VoxelField;
            Vector3Int voxel;
            if (!TryGetFragmentVoxel(chunks[0].Geometry, out voxel))
            {
                Check(false, "R2B-15: found fragment voxel");
                return;
            }

            ChunkGeometryStats before = GeometryStats(chunks[0].Geometry);
            MeshStats initialMesh = MeshStatsOf(chunks[0].Mesh);

            for (int i = 0; i < 20; i++)
            {
                Apply(field, voxel, VoxelState.Air, chunks, views);
                Apply(field, voxel, VoxelState.Solid, chunks, views);
            }

            ChunkGeometryStats after = GeometryStats(chunks[0].Geometry);
            MeshStats finalMesh = MeshStatsOf(chunks[0].Mesh);

            Check(
                before.FragmentCount == after.FragmentCount &&
                before.FaceFragmentCount == after.FaceFragmentCount &&
                before.BridgeCount == after.BridgeCount &&
                before.PoolCount == after.PoolCount,
                "R2B-15: Base Geometry counts do not grow"
            );
            Check(
                initialMesh.VertexCount == finalMesh.VertexCount &&
                initialMesh.TriangleCount == finalMesh.TriangleCount,
                "R2B-15: repeated mutation returns to initial mesh"
            );
            Check(
                CountContributions(chunks) == 0,
                "R2B-15: no hidden face accumulation"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    private static void CheckMultiVoxelMutation()
    {
        GameObject terrainObject = CreateTerrain(
            new Vector3Int(2, 1, 1),
            out DensityTerrain terrain,
            out List<TerrainChunk> chunks,
            out List<TerrainChunkView> views
        );

        try
        {
            VoxelField field = terrain.VoxelField;
            Vector3Int a = new Vector3Int(8, 0, 8);
            Vector3Int b = new Vector3Int(9, 0, 8);
            Vector3Int c = new Vector3Int(10, 0, 8);

            Apply(field, a, VoxelState.Air, chunks, views);
            Apply(field, b, VoxelState.Air, chunks, views);
            Apply(field, c, VoxelState.Air, chunks, views);

            Check(
                VoxelAwareMeshRuntimeMutation.LastReassembledChunkCount <=
                chunks.Count,
                "R2B-16: only dirty chunks reassembled"
            );
            Check(
                NoDuplicateFaceContributions(chunks),
                "R2B-16: batched mutation has no duplicate contributions"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    private static void CheckBaseGeometryImmutability()
    {
        GameObject terrainObject = CreateTerrain(
            new Vector3Int(2, 1, 1),
            out DensityTerrain terrain,
            out List<TerrainChunk> chunks,
            out List<TerrainChunkView> views
        );

        try
        {
            VoxelField field = terrain.VoxelField;
            ChunkGeometryStats before = GeometryStats(chunks[0].Geometry);
            Mesh rawBefore = chunks[0].RawMesh;
            int bridgeCountBefore = chunks[0].Geometry.Bridges.Count;

            Apply(
                field,
                new Vector3Int(15, 0, 8),
                VoxelState.Air,
                chunks,
                views
            );

            ChunkGeometryStats after = GeometryStats(chunks[0].Geometry);

            Check(chunks[0].RawMesh == rawBefore, "R2B-18: RawMesh unchanged");
            Check(
                chunks[0].Geometry.Bridges.Count == bridgeCountBefore,
                "R2B-10: Bridge provenance preserved"
            );
            Check(
                before.FragmentCount == after.FragmentCount &&
                before.FaceFragmentCount == after.FaceFragmentCount &&
                before.PoolCount == after.PoolCount,
                "R2B-18: Base / Pre-cut Geometry unchanged"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    // ---------------------------------------------------------------------

    private static void CheckMultipleRegionsAndMaterial()
    {
        List<VoxelAwareMeshSpatialFace.FaceSegment> segments =
            new List<VoxelAwareMeshSpatialFace.FaceSegment>
            {
                new VoxelAwareMeshSpatialFace.FaceSegment(
                    new Vector2(0f, 0.5f),
                    new Vector2(1f, 0.5f))
                { IsBridge = true },
                new VoxelAwareMeshSpatialFace.FaceSegment(
                    new Vector2(0.5f, 0f),
                    new Vector2(0.5f, 0.5f))
                { IsNatural = true }
            };

        List<VoxelAwareMeshSpatialFace.FaceRegion> regions =
            VoxelAwareMeshSpatialFace.Partition(
                segments,
                VoxelAwareMeshSpatialFace.Tolerance
            );

        Check(regions.Count >= 3, "R2B-11: multiple Regions partition correctly");

        MaterialField field = CreateUniformMaterialField(MaterialType.Stone);
        VoxelAwareMeshPrecut.ChunkGeometry geometry =
            new VoxelAwareMeshPrecut.ChunkGeometry
            {
                ChunkOrigin = Vector3.zero,
                VoxelSize = 1f,
                BaseMaterialField = field
            };

        VoxelAwareMeshSpatialFace.SpatialFaceRecord creation =
            VoxelAwareMeshSpatialFace.Evaluate(
                new VoxelAwareMeshPrecut.FaceId(0, 1, 0, 0),
                new Vector3Int(0, 0, 0),
                new Vector3Int(1, 0, 0),
                true,
                false,
                true,
                Vector3Int.zero,
                new VoxelAwareMeshSpatialFace.FaceSegment[0],
                field,
                1f,
                geometry,
                uv => true
            );

        Check(
            creation.HiddenFaces.Count > 0,
            "R2B-4: Solid / Air creates Hidden Face"
        );

        VoxelAwareMeshPrecut.ChunkGeometry materialGeometry =
            new VoxelAwareMeshPrecut.ChunkGeometry
            {
                ChunkOrigin = Vector3.zero,
                VoxelSize = 1f,
                BaseMaterialField = field
            };

        VoxelAwareMeshSpatialFace.SpatialFaceRecord materialRecord =
            VoxelAwareMeshSpatialFace.Evaluate(
                new VoxelAwareMeshPrecut.FaceId(0, 1, 0, 0),
                new Vector3Int(0, 0, 0),
                new Vector3Int(1, 0, 0),
                true,
                false,
                true,
                Vector3Int.zero,
                segments,
                field,
                1f,
                materialGeometry,
                uv => true,
                uv => uv.y > 0.5f
                    ? MaterialType.Grass
                    : MaterialType.Dirt
            );

        Dictionary<int, bool> submeshes = new Dictionary<int, bool>();
        for (int i = 0; i < materialRecord.HiddenFaces.Count; i++)
        {
            submeshes[materialRecord.HiddenFaces[i].Submesh] = true;
        }

        Check(
            submeshes.Count >= 2,
            "R2B-12: hidden face regions preserve distinct materials"
        );

        MaterialType materialA = VoxelAwareMeshSpatialFace.GetVoxelMainMaterial(
            CreateUniformMaterialField(MaterialType.Dirt),
            new Vector3Int(15, 0, 0),
            1f
        );
        MaterialType materialB = VoxelAwareMeshSpatialFace.GetVoxelMainMaterial(
            CreateUniformMaterialField(MaterialType.Stone),
            new Vector3Int(15, 0, 0),
            1f
        );

        Check(
            materialA != materialB,
            "R2B-13: cross-chunk material source is deterministic"
        );
    }

    private static MaterialField CreateUniformMaterialField(
        MaterialType material)
    {
        MaterialField field = new MaterialField(
            4,
            4,
            4,
            Vector3.zero,
            new Vector3(4f, 4f, 4f)
        );

        for (int x = 0; x < field.SampleCountX; x++)
        {
            for (int y = 0; y < field.SampleCountY; y++)
            {
                for (int z = 0; z < field.SampleCountZ; z++)
                {
                    field[x, y, z] = material;
                }
            }
        }

        return field;
    }

    private static void CheckBaseDensityDomainGuard()
    {
        GameObject terrainObject = CreateTerrain(
            new Vector3Int(1, 1, 1),
            out DensityTerrain terrain,
            out List<TerrainChunk> chunks,
            out List<TerrainChunkView> views
        );

        try
        {
            bool valid = VoxelAwareMeshRuntimeMutation.TrySampleBaseDensity(
                chunks[0],
                chunks[0].ChunkOrigin + new Vector3(0.5f, 0.5f, 0.5f),
                out _,
                out string validError
            );

            Check(valid && string.IsNullOrEmpty(validError),
                "R2B-Domain: valid point samples Base Density");

            bool invalid = VoxelAwareMeshRuntimeMutation.TrySampleBaseDensity(
                chunks[0],
                chunks[0].ChunkOrigin + new Vector3(10000f, 10000f, 10000f),
                out _,
                out string invalidError
            );

            Check(
                !invalid &&
                invalidError.Contains("BASE DENSITY SAMPLE DOMAIN GAP") &&
                invalidError.Contains("SampleCount=") &&
                invalidError.Contains("CellSize="),
                "R2B-Domain: out-of-domain sample reports Domain Gap metadata"
            );
            Check(
                invalidError.Contains("OutOfRangeAxis=X,Y,Z"),
                "R2B-Domain: Domain Gap reports out-of-range axis"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    private static void CheckFaceFragmentVisibilityFilter()
    {
        VoxelAwareMeshPrecut.ChunkGeometry geometry =
            new VoxelAwareMeshPrecut.ChunkGeometry
            {
                ChunkOrigin = Vector3.zero,
                VoxelSize = 1f
            };

        List<Vector3> polygon = new List<Vector3>
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(1f, 0f, 0f),
            new Vector3(0f, 1f, 0f)
        };

        VoxelAwareMeshPrecut.FaceFragment fragment =
            new VoxelAwareMeshPrecut.FaceFragment();
        fragment.Polygon.AddRange(polygon);
        for (int i = 0; i < polygon.Count; i++)
        {
            fragment.VertexIds.Add(geometry.Pool.GetOrAdd(polygon[i]));
        }

        geometry.FaceFragments.Add(fragment);
        geometry.FaceFragmentVisibility[0] = false;

        Mesh hidden = VoxelAwareMeshPrecut.Assemble(geometry);
        Check(
            hidden == null || hidden.vertexCount == 0,
            "R2B-FaceFragment: Final Visible=false excludes Coplanar Base Geometry"
        );
        if (hidden != null) UnityEngine.Object.DestroyImmediate(hidden);

        geometry.FaceFragmentVisibility[0] = true;
        Mesh visible = VoxelAwareMeshPrecut.Assemble(geometry);
        Check(
            visible != null && visible.vertexCount > 0,
            "R2B-FaceFragment: Final Visible=true includes Coplanar Base Geometry"
        );
        if (visible != null) UnityEngine.Object.DestroyImmediate(visible);
    }

    /// <summary>
    /// Spec 13.8.3 FaceFragment Visibility Rule：
    /// 规则表 + 端到端 Solid ↔ Air 生命周期 + 与 Hidden Face 的互斥不变式。
    /// </summary>
    private static void CheckFaceFragmentVisibilityRule()
    {
        // B3-E2 真值表（Spec §13.8.3 修订版：以基础密度材料边界 + 材料侧是否被挖空为准）
        // 参数顺序：negativeMaterial, positiveMaterial, negativeSolid, positiveSolid
        Check(
            VoxelAwareMeshRuntimeMutation.FaceFragmentSurfaceVisible(
                true, false, true, true),
            "R2B-Visibility: 材料在负侧且未挖空 -> 地表可见"
        );
        Check(
            VoxelAwareMeshRuntimeMutation.FaceFragmentSurfaceVisible(
                false, true, true, true),
            "R2B-Visibility: 材料在正侧且未挖空 -> 地表可见"
        );
        Check(
            !VoxelAwareMeshRuntimeMutation.FaceFragmentSurfaceVisible(
                true, false, false, true),
            "R2B-Visibility: 材料侧已被挖空（Solid→Air）-> 不可见"
        );
        Check(
            !VoxelAwareMeshRuntimeMutation.FaceFragmentSurfaceVisible(
                true, true, true, true),
            "R2B-Visibility: 两侧皆有材料（内部几何）-> 不可见"
        );
        Check(
            !VoxelAwareMeshRuntimeMutation.FaceFragmentSurfaceVisible(
                false, false, true, true),
            "R2B-Visibility: 两侧皆无材料（悬浮片）-> 不可见"
        );
        Check(
            VoxelAwareMeshRuntimeMutation.FaceFragmentSurfaceVisible(
                true, false, true, true) &&
            VoxelAwareMeshRuntimeMutation.FaceFragmentSurfaceVisible(
                false, true, true, true),
            "R2B-Visibility: Solid→Air→Solid 恢复后材料边界重新成立 -> 可见"
        );

        GameObject terrainObject = CreateTerrain(
            new Vector3Int(1, 1, 1),
            out DensityTerrain terrain,
            out List<TerrainChunk> chunks,
            out List<TerrainChunkView> views
        );

        try
        {
            TerrainChunk chunk = chunks[0];
            VoxelField field = terrain.VoxelField;

            Vector3Int negativeVoxel = new Vector3Int(8, 4, 8);
            Vector3Int positiveVoxel = new Vector3Int(9, 4, 8);
            VoxelAwareMeshPrecut.FaceId face =
                new VoxelAwareMeshPrecut.FaceId(0, 9, 4, 8);

            Check(
                field.Get(negativeVoxel) == VoxelState.Solid &&
                field.Get(positiveVoxel) == VoxelState.Solid,
                "R2B-Visibility: fixture face starts Solid/Solid"
            );
            Check(
                CountFaceFragments(chunk.Geometry, face.Key) == 0,
                "R2B-Visibility: fixture face has no Coplanar Base Geometry"
            );

            List<Vector3> planted = PlantCoplanarFaceFragment(
                chunk,
                face,
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f)
            );
            int fragmentCount = chunk.Geometry.FaceFragments.Count;
            int polygonSize =
                chunk.Geometry.FaceFragments[fragmentCount - 1].Polygon.Count;

            chunk.ReplaceAssembledMesh(
                VoxelAwareMeshPrecut.Assemble(chunk.Geometry)
            );

            Check(
                FaceFragmentVisible(chunk.Geometry, face.Key) &&
                FacePlaneTriangleArea(chunk.Mesh, chunk.ChunkOrigin, face) > 0.9f,
                "R2B-Visibility: unevaluated Coplanar Base Geometry is drawn（面内面积 " +
                FacePlaneTriangleArea(chunk.Mesh, chunk.ChunkOrigin, face).ToString("F4") + "）"
            );

            // Solid / Solid -> Solid / Air
            Apply(field, negativeVoxel, VoxelState.Air, chunks, views);
            // B3-E2：植入片两侧皆为材料 ⇒ 属内部几何 ⇒ 任何编辑状态边界下都必须隐藏
            //（真实地表的可见性见 B3C T12/T13，由其材料边界决定）。
            Check(
                !FaceFragmentVisible(chunk.Geometry, face.Key) &&
                FacePlaneTriangleArea(chunk.Mesh, chunk.ChunkOrigin, face) <= 1e-4f,
                "R2B-Visibility: Solid/Air 下内部植入片保持隐藏（面内面积 " +
                FacePlaneTriangleArea(chunk.Mesh, chunk.ChunkOrigin, face).ToString("F4") + "）"
            );
            Check(
                !HasHiddenContribution(chunk.Geometry, face.Key),
                "R2B-Visibility: covered Region generates no Hidden Face"
            );
            Check(
                NoVisibleFragmentWithHiddenFace(chunks),
                "R2B-Visibility: no Face has visible Base Geometry and Hidden Face"
            );

            // Solid / Air -> Air / Air
            Apply(field, positiveVoxel, VoxelState.Air, chunks, views);
            // B3-D 修正：这里必须判定“面内几何确实消失”，不能用“顶点是否存在”，
            // 因为修正后的相邻内壁会与面角点共享顶点（旧判据会假阳性）。
            Check(
                !FaceFragmentVisible(chunk.Geometry, face.Key) &&
                FacePlaneTriangleArea(chunk.Mesh, chunk.ChunkOrigin, face) <= 1e-4f,
                "R2B-Visibility: Air/Air filters Base Geometry from Final Mesh（面内面积 " +
                FacePlaneTriangleArea(chunk.Mesh, chunk.ChunkOrigin, face).ToString("E3") + "）"
            );

            // Air / Air -> Air / Solid
            Apply(field, positiveVoxel, VoxelState.Solid, chunks, views);
            // B3-E2：Air/Solid 边界同样不改变“内部植入片隐藏”的结论。
            Check(
                !FaceFragmentVisible(chunk.Geometry, face.Key) &&
                FacePlaneTriangleArea(chunk.Mesh, chunk.ChunkOrigin, face) <= 1e-4f,
                "R2B-Visibility: Air/Solid 下内部植入片保持隐藏（面内面积 " +
                FacePlaneTriangleArea(chunk.Mesh, chunk.ChunkOrigin, face).ToString("F4") + "）"
            );
            Check(
                NoVisibleFragmentWithHiddenFace(chunks),
                "R2B-Visibility: restore keeps Hidden Face exclusivity"
            );

            // Air / Solid -> Solid / Solid
            Apply(field, negativeVoxel, VoxelState.Solid, chunks, views);
            Check(
                !FaceFragmentVisible(chunk.Geometry, face.Key) &&
                !MeshHasAllVertices(chunk.Mesh, planted),
                "R2B-Visibility: Solid/Solid filters Base Geometry from Final Mesh"
            );

            // Spec 13.8.1：Base Geometry 不可被 Mutation 删除 / 修改。
            Check(
                chunk.Geometry.FaceFragments.Count == fragmentCount &&
                chunk.Geometry.FaceFragments[fragmentCount - 1].Polygon.Count ==
                polygonSize &&
                chunk.RawMesh != null,
                "R2B-Visibility: Mutation never deletes or edits Base FaceFragment"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    private static List<Vector3> PlantCoplanarFaceFragment(
        TerrainChunk chunk,
        VoxelAwareMeshPrecut.FaceId face,
        params Vector2[] uvs)
    {
        List<Vector3> positions = new List<Vector3>();
        VoxelAwareMeshPrecut.FaceFragment fragment =
            new VoxelAwareMeshPrecut.FaceFragment();
        fragment.Face = face;

        for (int i = 0; i < uvs.Length; i++)
        {
            Vector3 local = VoxelAwareMeshSpatialFace.ToChunkLocal(
                face,
                uvs[i],
                chunk.ChunkOrigin,
                chunk.VoxelSize
            );

            positions.Add(local);
            fragment.Polygon.Add(local);
            fragment.VertexIds.Add(chunk.Geometry.Pool.GetOrAdd(local));
        }

        chunk.Geometry.FaceFragments.Add(fragment);
        return positions;
    }

    private static int CountFaceFragments(
        VoxelAwareMeshPrecut.ChunkGeometry geometry,
        string faceKey)
    {
        int count = 0;
        for (int i = 0; i < geometry.FaceFragments.Count; i++)
        {
            if (geometry.FaceFragments[i].Face.Key == faceKey) count++;
        }

        return count;
    }

    private static bool FaceFragmentVisible(
        VoxelAwareMeshPrecut.ChunkGeometry geometry,
        string faceKey)
    {
        bool found = false;
        for (int i = 0; i < geometry.FaceFragments.Count; i++)
        {
            if (geometry.FaceFragments[i].Face.Key != faceKey) continue;

            found = true;
            bool visible = true;
            if (geometry.FaceFragmentVisibility.TryGetValue(i, out bool stored))
            {
                visible = stored;
            }

            if (!visible) return false;
        }

        return found;
    }

    private static bool HasHiddenContribution(
        VoxelAwareMeshPrecut.ChunkGeometry geometry,
        string faceKey)
    {
        return geometry.HiddenFaceContributions.TryGetValue(
            faceKey,
            out List<VoxelAwareMeshSpatialFace.HiddenFaceFragment> contributions) &&
            contributions != null &&
            contributions.Count > 0;
    }

    private static bool NoVisibleFragmentWithHiddenFace(
        List<TerrainChunk> chunks)
    {
        for (int c = 0; c < chunks.Count; c++)
        {
            TerrainChunk chunk = chunks[c];
            if (chunk == null || chunk.Geometry == null) continue;

            VoxelAwareMeshPrecut.ChunkGeometry geometry = chunk.Geometry;
            for (int i = 0; i < geometry.FaceFragments.Count; i++)
            {
                VoxelAwareMeshPrecut.FaceFragment fragment =
                    geometry.FaceFragments[i];
                if (!fragment.Active) continue;

                bool visible = true;
                if (geometry.FaceFragmentVisibility.TryGetValue(i, out bool stored))
                {
                    visible = stored;
                }

                if (!visible) continue;
                if (HasHiddenContribution(geometry, fragment.Face.Key))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// 最终 Mesh 中位于指定 Face 平面、且落在该 Face 面内的三角形总面积。
    /// 用面积而非“顶点是否存在”判定该面几何是否真的存在/被过滤：
    /// B3-D 修正后相邻内壁会与面角点共享顶点，顶点判定会产生假阳性。
    /// </summary>
    private static float FacePlaneTriangleArea(
        Mesh mesh,
        Vector3 chunkOrigin,
        VoxelAwareMeshPrecut.FaceId face,
        float planeTolerance = 1e-4f)
    {
        if (mesh == null) return 0f;

        int axisU = (face.Axis + 1) % 3;
        int axisV = (face.Axis + 2) % 3;
        Vector3[] vertices = mesh.vertices;
        float area = 0f;

        for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
        {
            int[] indices = mesh.GetTriangles(submesh);
            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                Vector3 a = vertices[indices[i]] + chunkOrigin;
                Vector3 b = vertices[indices[i + 1]] + chunkOrigin;
                Vector3 c = vertices[indices[i + 2]] + chunkOrigin;

                if (Mathf.Abs(a[face.Axis] - face.Plane) > planeTolerance ||
                    Mathf.Abs(b[face.Axis] - face.Plane) > planeTolerance ||
                    Mathf.Abs(c[face.Axis] - face.Plane) > planeTolerance)
                {
                    continue;
                }

                Vector3 centroid = (a + b + c) / 3f;
                if (centroid[axisU] < face.U - 0.05f ||
                    centroid[axisU] > face.U + 1.05f ||
                    centroid[axisV] < face.V - 0.05f ||
                    centroid[axisV] > face.V + 1.05f)
                {
                    continue;
                }

                area += TriangleArea(a, b, c);
            }
        }

        return area;
    }

    private static bool MeshHasAllVertices(Mesh mesh, List<Vector3> positions)
    {
        if (mesh == null || positions == null || positions.Count == 0)
        {
            return false;
        }

        Vector3[] vertices = mesh.vertices;
        for (int p = 0; p < positions.Count; p++)
        {
            bool found = false;
            for (int i = 0; i < vertices.Length; i++)
            {
                if ((vertices[i] - positions[p]).sqrMagnitude <= 1e-8f)
                {
                    found = true;
                    break;
                }
            }

            if (!found) return false;
        }

        return true;
    }

    private static void CheckBoundaryGraphDedup()
    {
        GameObject terrainObject = CreateTerrain(
            new Vector3Int(2, 1, 1),
            out DensityTerrain terrain,
            out List<TerrainChunk> chunks,
            out List<TerrainChunkView> views
        );

        try
        {
            TerrainChunk negative =
                chunks.Find(c => c.Coordinate == new Vector3Int(0, 0, 0));
            TerrainChunk positive =
                chunks.Find(c => c.Coordinate == new Vector3Int(1, 0, 0));

            if (negative == null || positive == null ||
                negative.Geometry == null || positive.Geometry == null)
            {
                Check(false, "R2B-7: found both side chunks");
                return;
            }

            VoxelAwareMeshPrecut.FaceId face =
                new VoxelAwareMeshPrecut.FaceId(0, 16, 0, 0);

            AddFaceFragmentEdge(
                negative.Geometry,
                face,
                new Vector3(16f, 0f, 0f),
                new Vector3(16f, 1f, 0f)
            );
            AddFaceFragmentEdge(
                positive.Geometry,
                face,
                new Vector3(0f, 0f, 0f),
                new Vector3(0f, 1f, 0f)
            );

            List<VoxelAwareMeshSpatialFace.FaceSegment> segments =
                VoxelAwareMeshRuntimeMutation.BuildFaceSegmentsForDiagnostics(
                    face,
                    negative,
                    negative,
                    positive
                );

            int matching = 0;
            for (int i = 0; i < segments.Count; i++)
            {
                if (segments[i].IsCoplanar) matching++;
            }

            Check(
                matching == 1,
                "R2B-7: identical FaceGeometry from both chunks dedups to one logical segment"
            );

            // Spec 13.9.6：同一 GeometricDedupKey 允许合并，不报 Overlap。
            List<VoxelAwareMeshSpatialFace.FaceSegment> mirrored =
                new List<VoxelAwareMeshSpatialFace.FaceSegment>
                {
                    new VoxelAwareMeshSpatialFace.FaceSegment(
                        new Vector2(0f, 0f), new Vector2(1f, 0f)),
                    new VoxelAwareMeshSpatialFace.FaceSegment(
                        new Vector2(1f, 0f), new Vector2(0f, 0f))
                };

            bool mirroredAccepted =
                VoxelAwareMeshRuntimeMutation.ValidateBoundaryGraphSegments(
                    face,
                    mirrored,
                    out string mirroredError
                );

            Check(
                mirroredAccepted && string.IsNullOrEmpty(mirroredError),
                "R2B-9: same GeometricDedupKey merges without Collinear Overlap"
            );

            // 不同 SegmentIdentity 的部分 Collinear Overlap：Validation Failure。
            List<VoxelAwareMeshSpatialFace.FaceSegment> overlapping =
                new List<VoxelAwareMeshSpatialFace.FaceSegment>
                {
                    new VoxelAwareMeshSpatialFace.FaceSegment(
                        new Vector2(0f, 0f), new Vector2(0.8f, 0f)),
                    new VoxelAwareMeshSpatialFace.FaceSegment(
                        new Vector2(0.5f, 0f), new Vector2(1f, 0f))
                };

            bool overlapRejected =
                !VoxelAwareMeshRuntimeMutation.ValidateBoundaryGraphSegments(
                    face,
                    overlapping,
                    out string overlapError
                );

            Check(
                overlapRejected && overlapError.Contains("COLLINEAR OVERLAP"),
                "R2B-9: partial Collinear Overlap reports Validation Failure"
            );

            // 只在端点相接（Point Touch）不是 Overlap。
            List<VoxelAwareMeshSpatialFace.FaceSegment> touching =
                new List<VoxelAwareMeshSpatialFace.FaceSegment>
                {
                    new VoxelAwareMeshSpatialFace.FaceSegment(
                        new Vector2(0f, 0f), new Vector2(0.5f, 0f)),
                    new VoxelAwareMeshSpatialFace.FaceSegment(
                        new Vector2(0.5f, 0f), new Vector2(1f, 0f))
                };

            bool touchingAccepted =
                VoxelAwareMeshRuntimeMutation.ValidateBoundaryGraphSegments(
                    face,
                    touching,
                    out string touchingError
                );

            Check(
                touchingAccepted && string.IsNullOrEmpty(touchingError),
                "R2B-9: Point Touch is not Collinear Overlap"
            );

            // 真实 Mutation 的 Cross-Chunk 边界图必须无 Collinear Overlap。
            Apply(
                terrain.VoxelField,
                new Vector3Int(8, 4, 8),
                VoxelState.Air,
                chunks,
                views
            );

            Check(
                !VoxelAwareMeshRuntimeMutation.LastBoundaryGraphValidationFailure &&
                string.IsNullOrEmpty(
                    VoxelAwareMeshRuntimeMutation
                        .LastBoundaryGraphValidationMessage),
                "R2B-9: Pre-cut Cross-Chunk Segments contain no Collinear Overlap"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    private static void AddFaceFragmentEdge(
        VoxelAwareMeshPrecut.ChunkGeometry geometry,
        VoxelAwareMeshPrecut.FaceId face,
        Vector3 a,
        Vector3 b)
    {
        VoxelAwareMeshPrecut.FaceFragment fragment =
            new VoxelAwareMeshPrecut.FaceFragment
            {
                Face = face
            };

        fragment.Polygon.Add(a);
        fragment.Polygon.Add(b);
        for (int i = 0; i < fragment.Polygon.Count; i++)
        {
            fragment.VertexIds.Add(
                geometry.Pool.GetOrAdd(fragment.Polygon[i])
            );
        }

        geometry.FaceFragments.Add(fragment);
    }

    private static void CheckDirtyPropagationSets()
    {
        GameObject terrainObject = CreateTerrain(
            new Vector3Int(2, 1, 1),
            out DensityTerrain terrain,
            out List<TerrainChunk> chunks,
            out List<TerrainChunkView> views
        );

        try
        {
            VoxelField field = terrain.VoxelField;

            // Same-chunk mutation.
            Apply(field, new Vector3Int(8, 0, 8), VoxelState.Air, chunks, views);
            Check(
                LastSetEquals(
                    VoxelAwareMeshRuntimeMutation.LastDirtyChunks,
                    VoxelAwareMeshRuntimeMutation.LastReassembledChunks),
                "R2B-17: same-chunk dirty set equals reassembled set"
            );
            Check(
                VoxelAwareMeshRuntimeMutation.LastDirtyChunks.Count == 1 &&
                VoxelAwareMeshRuntimeMutation.LastDirtyChunks[0] ==
                new Vector3Int(0, 0, 0),
                "R2B-17: same-chunk mutation touches only changed chunk"
            );

            // Cross-chunk Air/Solid: mesh owner must be positive chunk.
            Apply(field, new Vector3Int(15, 0, 8), VoxelState.Air, chunks, views);
            Check(
                Contains(
                    VoxelAwareMeshRuntimeMutation.LastEvaluationOwnerChunks,
                    new Vector3Int(0, 0, 0)),
                "R2B-17: cross-chunk evaluation owner recorded"
            );
            Check(
                Contains(
                    VoxelAwareMeshRuntimeMutation.LastMeshOwnerChunks,
                    new Vector3Int(1, 0, 0)),
                "R2B-17: cross-chunk hidden mesh owner recorded"
            );
            Check(
                LastSetEquals(
                    VoxelAwareMeshRuntimeMutation.LastDirtyChunks,
                    VoxelAwareMeshRuntimeMutation.LastReassembledChunks),
                "R2B-17: cross-chunk dirty set reassembled exactly"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    /// <summary>
    /// Audit-1 ~ Audit-3：Domain Gap 是否真正阻断错误 Mesh。
    /// 走真实 Runtime 调用链：
    /// VoxelField.Set → ApplyVoxelStateChange → Fragment Mutation →
    /// 6 × SpatialFace Evaluation → Domain Gap / HiddenFace / NoHiddenFace →
    /// DirtySet → Assemble → ReplaceAssembledMesh。
    /// </summary>
    private static void CheckDomainGapAudit()
    {
        Vector3Int chunkVoxelSize = new Vector3Int(16, 16, 16);

        // ---------- Audit-1：正常 Domain 内采样 ----------
        {
            GameObject terrainObject = CreateTerrain(
                new Vector3Int(1, 1, 1),
                out DensityTerrain terrain,
                out List<TerrainChunk> chunks,
                out List<TerrainChunkView> views
            );

            try
            {
                VoxelField field = terrain.VoxelField;
                Vector3Int voxel = new Vector3Int(8, 4, 8);

                bool committed =
                    VoxelAwareMeshRuntimeMutation.ApplyVoxelStateChange(
                        field,
                        voxel,
                        VoxelState.Air,
                        chunks,
                        chunkVoxelSize
                    );

                Check(
                    committed &&
                    !VoxelAwareMeshRuntimeMutation.LastEvaluationFailed &&
                    VoxelAwareMeshRuntimeMutation.LastMutationCommitted &&
                    VoxelAwareMeshRuntimeMutation.LastAssemblyExecuted &&
                    VoxelAwareMeshRuntimeMutation.LastPlannedFaceCount == 6 &&
                    VoxelAwareMeshRuntimeMutation.LastEvaluatedFaceCount == 6 &&
                    VoxelAwareMeshRuntimeMutation.LastReassembledChunkCount >= 1,
                    "Audit-1: in-domain = Evaluation success -> Commit -> Assemble"
                );

                Check(
                    LastSetEquals(
                        VoxelAwareMeshRuntimeMutation.LastDirtyChunks,
                        VoxelAwareMeshRuntimeMutation.LastReassembledChunks) &&
                    chunks[0].Mesh != null,
                    "Audit-1: Dirty == Reassembled and Final Mesh present"
                );

                int plainRegionCount = VoxelAwareMeshSpatialFace.Partition(
                    new VoxelAwareMeshSpatialFace.FaceSegment[0],
                    VoxelAwareMeshRuntimeMutation.Tolerance
                ).Count;
                int plainHiddenFaces = CountContributionFaces(chunks);

                Check(
                    plainRegionCount == 1,
                    "Audit-1: segment-free Face partitions to exactly one Region"
                );

                Debug.Log(
                    "[Audit-1] plainFaceRegionCount=" + plainRegionCount +
                    " contributionFaces=" + plainHiddenFaces +
                    " (interior Solid/Air Face: NaturalBaseCoverage=true" +
                    " -> No Hidden Face, per Spec 13.7.5 / 13.10)"
                );

                bool restored =
                    VoxelAwareMeshRuntimeMutation.ApplyVoxelStateChange(
                        field,
                        voxel,
                        VoxelState.Solid,
                        chunks,
                        chunkVoxelSize
                    );

                Check(
                    restored &&
                    !VoxelAwareMeshRuntimeMutation.LastEvaluationFailed &&
                    VoxelAwareMeshRuntimeMutation.LastMutationCommitted &&
                    VoxelAwareMeshRuntimeMutation.LastAssemblyExecuted,
                    "Audit-1: restore is Evaluation success -> Commit -> Assemble"
                );
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(terrainObject);
            }
        }

        // ---------- Audit-2：故意制造 Domain Gap ----------
        {
            GameObject terrainObject = CreateTerrain(
                new Vector3Int(1, 1, 1),
                out DensityTerrain terrain,
                out List<TerrainChunk> chunks,
                out List<TerrainChunkView> views
            );

            try
            {
                VoxelField field = terrain.VoxelField;
                TerrainChunk chunk = chunks[0];
                Vector3Int voxel = new Vector3Int(8, 4, 8);

                // 把该 Chunk 的 Base DensityField 换成只覆盖 [0,2]^3 的 Field：
                // 该 Voxel 的 Face Region sample 必然落在 Domain 之外。
                DensityField shrunken = CreateAuditDensityField(2f, 1f);
                chunk.Build(shrunken, chunk.Material, chunk.IsoLevel);

                Mesh meshBefore = chunk.Mesh;
                MeshStats statsBefore = MeshStatsOf(chunk.Mesh);
                string visibilityBefore = VisibilitySnapshot(chunks);
                string contributionsBefore = ContributionSnapshot(chunks);
                int activeBefore = CountActiveFragments(chunk.Geometry, voxel);

                bool committed =
                    VoxelAwareMeshRuntimeMutation.ApplyVoxelStateChange(
                        field,
                        voxel,
                        VoxelState.Air,
                        chunks,
                        chunkVoxelSize
                    );

                Check(
                    !committed &&
                    VoxelAwareMeshRuntimeMutation.LastEvaluationFailed &&
                    VoxelAwareMeshRuntimeMutation.LastBaseDensityDomainGap &&
                    VoxelAwareMeshRuntimeMutation.LastBaseDensityDomainGapMessage
                        .Contains("BASE DENSITY SAMPLE DOMAIN GAP"),
                    "Audit-2: Domain Gap -> EvaluationFailed -> Mutation aborted"
                );

                Check(
                    !VoxelAwareMeshRuntimeMutation.LastMutationCommitted &&
                    !VoxelAwareMeshRuntimeMutation.LastAssemblyExecuted &&
                    VoxelAwareMeshRuntimeMutation.LastReassembledChunkCount == 0 &&
                    VoxelAwareMeshRuntimeMutation.LastReassembledChunks.Count == 0 &&
                    VoxelAwareMeshRuntimeMutation.LastDirtyChunks.Count == 0,
                    "Audit-2: no Commit, empty DirtySet, Assembly skipped"
                );

                Check(
                    VisibilitySnapshot(chunks) == visibilityBefore &&
                    ContributionSnapshot(chunks) == contributionsBefore &&
                    CountActiveFragments(chunk.Geometry, voxel) == activeBefore,
                    "Audit-2: no wrong Visibility / HiddenFace / Fragment commit"
                );

                MeshStats statsAfter = MeshStatsOf(chunk.Mesh);
                Check(
                    chunk.Mesh == meshBefore &&
                    statsAfter.VertexCount == statsBefore.VertexCount &&
                    statsAfter.TriangleCount == statsBefore.TriangleCount,
                    "Audit-2: Final Mesh not reassembled from failed Evaluation"
                );

                Check(
                    field.Get(voxel) == VoxelState.Air,
                    "Audit-2: VoxelField not rolled back (authoritative state)"
                );
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(terrainObject);
            }
        }

        // ---------- Audit-3：多 Face + 单个 Domain Gap ----------
        {
            GameObject terrainObject = CreateTerrain(
                new Vector3Int(2, 1, 1),
                out DensityTerrain terrain,
                out List<TerrainChunk> chunks,
                out List<TerrainChunkView> views
            );

            try
            {
                VoxelField field = terrain.VoxelField;

                TerrainChunk chunk0 = chunks.Find(
                    c => c.Coordinate == Vector3Int.zero);
                TerrainChunk chunk1 = chunks.Find(
                    c => c.Coordinate == new Vector3Int(1, 0, 0));

                if (chunk0 == null || chunk1 == null ||
                    chunk0.Geometry == null || chunk1.Geometry == null)
                {
                    Check(false, "Audit-3: fixture chunks exist");
                    return;
                }

                // 只有 Chunk 0 的 Base DensityField 被破坏：
                // 同一个 Mutation 中 +X Face 的 Solid side 在 Chunk 1（正常），
                // 其它 Face 的 Solid side 在 Chunk 0（Domain Gap）。
                chunk0.Build(
                    CreateAuditDensityField(2f, 1f),
                    chunk0.Material,
                    chunk0.IsoLevel
                );

                Vector3Int voxel = new Vector3Int(15, 4, 8);

                bool healthyFace =
                    VoxelAwareMeshRuntimeMutation.TrySampleBaseDensity(
                        chunk1,
                        chunk1.ChunkOrigin + new Vector3(1.5f, 4.5f, 8.5f),
                        out _,
                        out _
                    );
                bool gapFace =
                    !VoxelAwareMeshRuntimeMutation.TrySampleBaseDensity(
                        chunk0,
                        chunk0.ChunkOrigin + new Vector3(14.5f, 4.5f, 8.5f),
                        out _,
                        out _
                    );

                string visibilityBefore = VisibilitySnapshot(chunks);
                string contributionsBefore = ContributionSnapshot(chunks);
                Mesh mesh0Before = chunk0.Mesh;
                Mesh mesh1Before = chunk1.Mesh;

                bool committed =
                    VoxelAwareMeshRuntimeMutation.ApplyVoxelStateChange(
                        field,
                        voxel,
                        VoxelState.Air,
                        chunks,
                        chunkVoxelSize
                    );

                Check(
                    healthyFace && gapFace,
                    "Audit-3: fixture has a healthy Face source and a Gap source"
                );

                Check(
                    !committed &&
                    VoxelAwareMeshRuntimeMutation.LastEvaluationFailed &&
                    VoxelAwareMeshRuntimeMutation.LastPlannedFaceCount == 6 &&
                    VoxelAwareMeshRuntimeMutation.LastEvaluatedFaceCount == 6,
                    "Audit-3: one Face failure aborts the whole Mutation"
                );

                Check(
                    !VoxelAwareMeshRuntimeMutation.LastMutationCommitted &&
                    !VoxelAwareMeshRuntimeMutation.LastAssemblyExecuted &&
                    VoxelAwareMeshRuntimeMutation.LastReassembledChunkCount == 0,
                    "Audit-3: no partial commit, no Assembly"
                );

                Check(
                    VisibilitySnapshot(chunks) == visibilityBefore &&
                    ContributionSnapshot(chunks) == contributionsBefore &&
                    chunk0.Mesh == mesh0Before &&
                    chunk1.Mesh == mesh1Before,
                    "Audit-3: neither chunk received a partial update"
                );
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(terrainObject);
            }
        }
    }

    /// <summary>
    /// Audit-4：TerrainChunk.Dispose() Mesh 销毁顺序。
    /// 覆盖 Mesh == RawMesh / Mesh != RawMesh / null 组合 / VoxelAware 双 Mesh。
    /// </summary>
    private static void CheckDisposeAudit()
    {
        GameObject terrainObject = CreateTerrain(
            new Vector3Int(1, 1, 1),
            out DensityTerrain terrain,
            out List<TerrainChunk> chunks,
            out List<TerrainChunkView> views
        );

        try
        {
            TerrainChunk source = chunks[0];

            // Case A：Mesh == RawMesh（同一个 Mesh 不能被重复 Destroy）
            {
                Mesh shared = new Mesh();
                TerrainChunk chunk = CreateAuditChunk(source);
                chunk.ApplyVoxelAwareGeometry(
                    new VoxelAwareMeshPrecut.ChunkGeometry { RawMesh = shared },
                    shared
                );

                bool sameBefore = chunk.Mesh != null &&
                                  chunk.Mesh == chunk.RawMesh &&
                                  chunk.RawMesh == shared;

                chunk.Dispose();

                Check(
                    sameBefore &&
                    chunk.Mesh == null &&
                    chunk.RawMesh == null &&
                    chunk.LastDisposedMeshCount == 1 &&
                    shared == null,
                    "Audit-4A: Mesh == RawMesh -> exactly one Destroy"
                );
            }

            // Case B / E：Mesh != RawMesh（VoxelAware 双 Mesh：base + assembled）
            {
                Mesh baseMesh = new Mesh();
                Mesh assembled = new Mesh();
                TerrainChunk chunk = CreateAuditChunk(source);
                chunk.ApplyVoxelAwareGeometry(
                    new VoxelAwareMeshPrecut.ChunkGeometry { RawMesh = baseMesh },
                    assembled
                );

                bool distinctBefore = chunk.Mesh == assembled &&
                                      chunk.RawMesh == baseMesh &&
                                      chunk.Mesh != chunk.RawMesh;

                chunk.Dispose();

                Check(
                    distinctBefore &&
                    chunk.Mesh == null &&
                    chunk.RawMesh == null &&
                    chunk.LastDisposedMeshCount == 2 &&
                    baseMesh == null &&
                    assembled == null,
                    "Audit-4B/E: Mesh != RawMesh -> both Destroyed once"
                );
            }

            // Case C：Mesh == null，RawMesh != null
            {
                Mesh baseMesh = new Mesh();
                Mesh assembled = new Mesh();
                TerrainChunk chunk = CreateAuditChunk(source);
                chunk.ApplyVoxelAwareGeometry(
                    new VoxelAwareMeshPrecut.ChunkGeometry { RawMesh = baseMesh },
                    assembled
                );

                bool cleared = SetChunkMeshForAudit(chunk, null);
                bool nullBefore = cleared && chunk.Mesh == null && chunk.RawMesh == baseMesh;

                chunk.Dispose();

                Check(
                    nullBefore &&
                    chunk.Mesh == null &&
                    chunk.RawMesh == null &&
                    chunk.LastDisposedMeshCount == 1 &&
                    baseMesh == null,
                    "Audit-4C: Mesh == null, RawMesh != null -> RawMesh Destroyed"
                );
            }

            // Case D：Mesh != null，RawMesh == null
            {
                TerrainChunk chunk = CreateAuditChunk(source);
                bool rawNullBefore = chunk.Mesh != null && chunk.RawMesh == null;

                Mesh meshBefore = chunk.Mesh;
                chunk.Dispose();

                Check(
                    rawNullBefore &&
                    chunk.Mesh == null &&
                    chunk.RawMesh == null &&
                    chunk.LastDisposedMeshCount == 1 &&
                    meshBefore == null,
                    "Audit-4D: Mesh != null, RawMesh == null -> Mesh Destroyed"
                );
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    private static TerrainChunk CreateAuditChunk(TerrainChunk source)
    {
        TerrainChunk chunk = new TerrainChunk(
            Vector3Int.zero,
            1f,
            new Vector3Int(16, 16, 16)
        );

        chunk.Build(source.Density, source.Material, source.IsoLevel);
        return chunk;
    }

    /// <summary>Audit 专用：把 Chunk.Mesh 置为 null（Dispose Case C）。</summary>
    private static bool SetChunkMeshForAudit(TerrainChunk chunk, Mesh mesh)
    {
        System.Reflection.FieldInfo field = typeof(TerrainChunk).GetField(
            "<Mesh>k__BackingField",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic
        );

        if (field != null)
        {
            field.SetValue(chunk, mesh);
            return true;
        }

        System.Reflection.PropertyInfo property = typeof(TerrainChunk).GetProperty(
            "Mesh",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public
        );

        if (property == null) return false;

        property.SetValue(chunk, mesh, null);
        return true;
    }

    /// <summary>Audit 专用：只覆盖 [0,size]^3 的 Base DensityField。</summary>
    private static DensityField CreateAuditDensityField(
        float size,
        float solidBelowHeight)
    {
        DensityField field = new DensityField(
            4,
            4,
            4,
            Vector3.zero,
            new Vector3(size, size, size)
        );

        for (int x = 0; x < field.SampleCountX; x++)
        {
            for (int y = 0; y < field.SampleCountY; y++)
            {
                for (int z = 0; z < field.SampleCountZ; z++)
                {
                    Vector3 position = field.GetLocalPosition(x, y, z);
                    field[x, y, z] =
                        position.y <= solidBelowHeight ? -1f : 1f;
                }
            }
        }

        return field;
    }

    private static string VisibilitySnapshot(List<TerrainChunk> chunks)
    {
        string snapshot = string.Empty;

        for (int c = 0; c < chunks.Count; c++)
        {
            TerrainChunk chunk = chunks[c];
            if (chunk == null || chunk.Geometry == null) continue;

            VoxelAwareMeshPrecut.ChunkGeometry geometry = chunk.Geometry;
            for (int i = 0; i < geometry.FaceFragments.Count; i++)
            {
                bool visible = true;
                if (geometry.FaceFragmentVisibility.TryGetValue(i, out bool stored))
                {
                    visible = stored;
                }

                snapshot += chunk.Coordinate + "#" + i + "=" + visible + "|";
            }
        }

        return snapshot;
    }

    private static string ContributionSnapshot(List<TerrainChunk> chunks)
    {
        string snapshot = string.Empty;

        for (int c = 0; c < chunks.Count; c++)
        {
            TerrainChunk chunk = chunks[c];
            if (chunk == null || chunk.Geometry == null) continue;

            List<string> keys = new List<string>();
            foreach (KeyValuePair<string,
                List<VoxelAwareMeshSpatialFace.HiddenFaceFragment>> pair in
                chunk.Geometry.HiddenFaceContributions)
            {
                keys.Add(pair.Key);
            }

            keys.Sort();

            for (int i = 0; i < keys.Count; i++)
            {
                chunk.Geometry.HiddenFaceContributions.TryGetValue(
                    keys[i],
                    out List<VoxelAwareMeshSpatialFace.HiddenFaceFragment>
                        contributions
                );

                int count = contributions == null ? 0 : contributions.Count;
                snapshot += chunk.Coordinate + "#" + keys[i] + "=" +
                            count + "|";
            }
        }

        return snapshot;
    }

    private static bool Contains(List<Vector3Int> list, Vector3Int value)
    {
        return list != null && list.Contains(value);
    }

    private static bool LastSetEquals(
        List<Vector3Int> a,
        List<Vector3Int> b)
    {
        if (a == null || b == null) return false;
        if (a.Count != b.Count) return false;

        for (int i = 0; i < a.Count; i++)
        {
            if (!b.Contains(a[i])) return false;
        }

        return true;
    }

    private static void Apply(
        VoxelField field,
        Vector3Int voxel,
        VoxelState state,
        List<TerrainChunk> chunks,
        List<TerrainChunkView> views)
    {
        VoxelAwareMeshRuntimeMutation.ApplyVoxelStateChange(
            field,
            voxel,
            state,
            chunks,
            new Vector3Int(16, 16, 16)
        );

        for (int i = 0; i < views.Count; i++)
        {
            if (views[i] != null && views[i].Chunk != null)
            {
                views[i].SetMesh(views[i].Chunk.Mesh);
            }
        }
    }

    private static GameObject CreateTerrain(
        Vector3Int chunkCount,
        out DensityTerrain terrain,
        out List<TerrainChunk> chunks,
        out List<TerrainChunkView> views)
    {
        GameObject terrainObject = new GameObject(
            "VoxelAwareMeshRuntimePhase2BTerrain"
        );

        terrainObject.SetActive(false);
        terrain = terrainObject.AddComponent<DensityTerrain>();
        terrain.voxelSize = 1f;
        terrain.chunkVoxelSize = new Vector3Int(16, 16, 16);
        terrain.chunkCount = chunkCount;
        terrain.chunkStartCoordinate = Vector3Int.zero;
        terrain.resolution = 8;
        terrain.densityMode = DensityGenerationMode.HeightField;
        terrain.useNoise = false;
        terrain.height = 7.5f;
        terrain.regenerateInEditor = false;
        terrain.useVoxelAwareMeshPrecut = true;
        terrain.Regenerate();

        views = GetViews(terrainObject);
        chunks = GetChunks(views);
        return terrainObject;
    }

    private static List<TerrainChunkView> GetViews(GameObject terrainObject)
    {
        TerrainChunkView[] array =
            terrainObject.GetComponentsInChildren<TerrainChunkView>(true);
        return new List<TerrainChunkView>(array);
    }

    private static List<TerrainChunk> GetChunks(List<TerrainChunkView> views)
    {
        List<TerrainChunk> chunks = new List<TerrainChunk>();
        for (int i = 0; i < views.Count; i++)
        {
            if (views[i] != null && views[i].Chunk != null)
            {
                chunks.Add(views[i].Chunk);
            }
        }

        return chunks;
    }

    private static bool TryGetFragmentVoxel(
        VoxelAwareMeshPrecut.ChunkGeometry geometry,
        out Vector3Int voxel)
    {
        voxel = default(Vector3Int);
        if (geometry == null) return false;

        foreach (KeyValuePair<Vector3Int, List<int>> pair in
            geometry.VoxelFragmentIndex)
        {
            if (pair.Value != null && pair.Value.Count > 0)
            {
                voxel = pair.Key;
                return true;
            }
        }

        return false;
    }

    private static bool TryGetTwoFragmentVoxels(
        VoxelAwareMeshPrecut.ChunkGeometry geometry,
        out Vector3Int a,
        out Vector3Int b)
    {
        a = default(Vector3Int);
        b = default(Vector3Int);
        if (geometry == null) return false;

        bool hasA = false;
        foreach (KeyValuePair<Vector3Int, List<int>> pair in
            geometry.VoxelFragmentIndex)
        {
            if (pair.Value == null || pair.Value.Count == 0) continue;

            if (!hasA)
            {
                a = pair.Key;
                hasA = true;
            }
            else
            {
                b = pair.Key;
                return true;
            }
        }

        return false;
    }

    private static int CountActiveFragments(
        VoxelAwareMeshPrecut.ChunkGeometry geometry,
        Vector3Int voxel)
    {
        if (geometry == null ||
            !geometry.VoxelFragmentIndex.TryGetValue(
                voxel,
                out List<int> ids))
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < ids.Count; i++)
        {
            if (geometry.Fragments[ids[i]].Active) count++;
        }

        return count;
    }

    private static bool[] ActiveFragments(
        VoxelAwareMeshPrecut.ChunkGeometry geometry,
        Vector3Int voxel)
    {
        if (geometry == null ||
            !geometry.VoxelFragmentIndex.TryGetValue(
                voxel,
                out List<int> ids))
        {
            return new bool[0];
        }

        bool[] result = new bool[ids.Count];
        for (int i = 0; i < ids.Count; i++)
        {
            result[i] = geometry.Fragments[ids[i]].Active;
        }

        return result;
    }

    private sealed class MeshStats
    {
        public int VertexCount;
        public int TriangleCount;
        public int SubmeshCount;
        public float Area;
    }

    private static MeshStats MeshStatsOf(Mesh mesh)
    {
        MeshStats stats = new MeshStats();
        if (mesh == null) return stats;

        stats.VertexCount = mesh.vertexCount;
        stats.SubmeshCount = mesh.subMeshCount;
        Vector3[] vertices = mesh.vertices;

        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            int[] triangles = mesh.GetTriangles(s);
            stats.TriangleCount += triangles.Length / 3;

            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                stats.Area += TriangleArea(
                    vertices[triangles[i]],
                    vertices[triangles[i + 1]],
                    vertices[triangles[i + 2]]
                );
            }
        }

        return stats;
    }

    private sealed class ChunkGeometryStats
    {
        public int FragmentCount;
        public int FaceFragmentCount;
        public int BridgeCount;
        public int PoolCount;
    }

    private static ChunkGeometryStats GeometryStats(
        VoxelAwareMeshPrecut.ChunkGeometry geometry)
    {
        ChunkGeometryStats stats = new ChunkGeometryStats();
        if (geometry == null) return stats;

        stats.FragmentCount = geometry.Fragments.Count;
        stats.FaceFragmentCount = geometry.FaceFragments.Count;
        stats.BridgeCount = geometry.Bridges.Count;
        stats.PoolCount = geometry.Pool.Positions.Count;
        return stats;
    }

    private static int CountContributions(List<TerrainChunk> chunks)
    {
        int count = 0;
        for (int i = 0; i < chunks.Count; i++)
        {
            if (chunks[i] == null || chunks[i].Geometry == null) continue;
            foreach (KeyValuePair<string,
                List<VoxelAwareMeshSpatialFace.HiddenFaceFragment>> pair in
                chunks[i].Geometry.HiddenFaceContributions)
            {
                if (pair.Value != null) count += pair.Value.Count;
            }
        }

        return count;
    }

    private static int CountContributionFaces(List<TerrainChunk> chunks)
    {
        Dictionary<string, bool> faces = new Dictionary<string, bool>();
        for (int i = 0; i < chunks.Count; i++)
        {
            if (chunks[i] == null || chunks[i].Geometry == null) continue;
            foreach (KeyValuePair<string,
                List<VoxelAwareMeshSpatialFace.HiddenFaceFragment>> pair in
                chunks[i].Geometry.HiddenFaceContributions)
            {
                if (pair.Value != null && pair.Value.Count > 0)
                {
                    faces[pair.Key] = true;
                }
            }
        }

        return faces.Count;
    }

    private static bool NoDuplicateFaceContributions(
        List<TerrainChunk> chunks)
    {
        Dictionary<string, int> counts = new Dictionary<string, int>();
        for (int i = 0; i < chunks.Count; i++)
        {
            if (chunks[i] == null || chunks[i].Geometry == null) continue;
            foreach (KeyValuePair<string,
                List<VoxelAwareMeshSpatialFace.HiddenFaceFragment>> pair in
                chunks[i].Geometry.HiddenFaceContributions)
            {
                if (!counts.ContainsKey(pair.Key)) counts.Add(pair.Key, 0);
                counts[pair.Key]++;
            }
        }

        foreach (KeyValuePair<string, int> pair in counts)
        {
            if (pair.Value > 1) return false;
        }

        return true;
    }

    private static float TriangleArea(Vector3 a, Vector3 b, Vector3 c)
    {
        return Vector3.Cross(b - a, c - a).magnitude * 0.5f;
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
            "[VoxelAwareMeshRuntimePhase2BValidation] ===== SUMMARY =====\n" +
            "Checks = " + checks + "\n" +
            "Failures = " + failures + "\n" +
            "Result = " + (failures == 0 ? "PASS" : "FAIL")
        );
    }
}
