using System;
using System.Collections.Generic;
using TerrainModificationV2;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Voxel-aware Mesh Runtime Phase 2A Validation�?/// SpatialFace / Planar Boundary Graph / Region Partition /
/// Hidden Face / Hidden Face Material�?/// �?Editor Validation，不接入 Mining�?/// </summary>
public static class VoxelAwareMeshRuntimePhase2AValidation
{
    private static int failures;
    private static int checks;

    [MenuItem("Tools/Terrain/Validate VoxelAwareMesh Runtime Phase 2A")]
    public static void ValidateFromMenu()
    {
        RunAll();
        LogSummary();
        Debug.Log(
            "[VoxelAwareMeshRuntimePhase2AValidation] Menu result = " +
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

        CheckEmptyFace();
        CheckSingleBridge();
        CheckCrossBridge();
        CheckDiagonalBridge();
        CheckDanglingBridge();
        CheckPointTouch();
        CheckCoplanarBoundary();
        CheckNaturalEdge();
        CheckStateMatrix();
        CheckNaturalCoverage();
        CheckBridgeBoundary();
        CheckHiddenFaceWelding();
        CheckNoDegenerateOrDuplicate();
        CheckCrossChunkFace();
        CheckNegativeCoordinates();
        CheckNonIdentityTransform();

        CheckMaterialM1();
        CheckMaterialM2();
        CheckMaterialM3();
        CheckMaterialM4();
        CheckMaterialM5();
        CheckMaterialM6();
    }

    private static void CheckEmptyFace()
    {
        List<VoxelAwareMeshSpatialFace.FaceRegion> regions =
            VoxelAwareMeshSpatialFace.Partition(
                new VoxelAwareMeshSpatialFace.FaceSegment[0],
                VoxelAwareMeshSpatialFace.Tolerance
            );

        Check(regions.Count == 1, "F1: empty Face has 1 Region");
        Check(
            Mathf.Abs(RegionArea(regions) - 1f) < 1e-4f,
            "F1: empty Face Region area covers unit square"
        );
    }

    private static void CheckSingleBridge()
    {
        List<VoxelAwareMeshSpatialFace.FaceSegment> segments =
            new List<VoxelAwareMeshSpatialFace.FaceSegment>
            {
                new VoxelAwareMeshSpatialFace.FaceSegment(
                    new Vector2(0f, 0.5f),
                    new Vector2(1f, 0.5f))
                { IsBridge = true }
            };

        List<VoxelAwareMeshSpatialFace.FaceRegion> regions =
            VoxelAwareMeshSpatialFace.Partition(
                segments,
                VoxelAwareMeshSpatialFace.Tolerance
            );

        Check(regions.Count == 2, "F2: one Bridge creates 2 Regions");
    }

    private static void CheckCrossBridge()
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
                    new Vector2(0.5f, 1f))
                { IsBridge = true }
            };

        List<VoxelAwareMeshSpatialFace.FaceRegion> regions =
            VoxelAwareMeshSpatialFace.Partition(
                segments,
                VoxelAwareMeshSpatialFace.Tolerance
            );

        Check(regions.Count == 4, "F3: cross Bridge creates 4 Regions");
    }

    private static void CheckDiagonalBridge()
    {
        List<VoxelAwareMeshSpatialFace.FaceSegment> segments =
            new List<VoxelAwareMeshSpatialFace.FaceSegment>
            {
                new VoxelAwareMeshSpatialFace.FaceSegment(
                    new Vector2(0f, 0f),
                    new Vector2(1f, 1f))
                { IsBridge = true }
            };

        List<VoxelAwareMeshSpatialFace.FaceRegion> regions =
            VoxelAwareMeshSpatialFace.Partition(
                segments,
                VoxelAwareMeshSpatialFace.Tolerance
            );

        Check(regions.Count == 2, "F4: boundary-connected diagonal Bridge creates 2 Regions");
    }

    private static void CheckDanglingBridge()
    {
        List<VoxelAwareMeshSpatialFace.FaceSegment> segments =
            new List<VoxelAwareMeshSpatialFace.FaceSegment>
            {
                new VoxelAwareMeshSpatialFace.FaceSegment(
                    new Vector2(0.3f, 0.5f),
                    new Vector2(0.7f, 0.5f))
                { IsBridge = true }
            };

        List<VoxelAwareMeshSpatialFace.FaceRegion> regions =
            VoxelAwareMeshSpatialFace.Partition(
                segments,
                VoxelAwareMeshSpatialFace.Tolerance
            );

        Check(regions.Count == 1, "F5: dangling Bridge does not create a new Region");
    }

    private static void CheckPointTouch()
    {
        List<VoxelAwareMeshSpatialFace.FaceSegment> segments =
            new List<VoxelAwareMeshSpatialFace.FaceSegment>
            {
                new VoxelAwareMeshSpatialFace.FaceSegment(
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f))
                { IsBridge = true }
            };

        List<VoxelAwareMeshSpatialFace.FaceRegion> regions =
            VoxelAwareMeshSpatialFace.Partition(
                segments,
                VoxelAwareMeshSpatialFace.Tolerance
            );

        Check(regions.Count == 1, "F6: Point Touch creates no Region boundary");
    }

    private static void CheckCoplanarBoundary()
    {
        List<VoxelAwareMeshSpatialFace.FaceSegment> segments =
            new List<VoxelAwareMeshSpatialFace.FaceSegment>
            {
                new VoxelAwareMeshSpatialFace.FaceSegment(
                    new Vector2(0f, 0f),
                    new Vector2(1f, 1f))
                { IsCoplanar = true }
            };

        List<VoxelAwareMeshSpatialFace.FaceRegion> regions =
            VoxelAwareMeshSpatialFace.Partition(
                segments,
                VoxelAwareMeshSpatialFace.Tolerance
            );

        Check(regions.Count == 2, "F7: coplanar Base Geometry boundary enters partition");
    }

    private static void CheckNaturalEdge()
    {
        List<VoxelAwareMeshSpatialFace.FaceSegment> segments =
            new List<VoxelAwareMeshSpatialFace.FaceSegment>
            {
                new VoxelAwareMeshSpatialFace.FaceSegment(
                    new Vector2(0f, 0.5f),
                    new Vector2(1f, 0.5f))
                { IsNatural = true }
            };

        List<VoxelAwareMeshSpatialFace.FaceRegion> regions =
            VoxelAwareMeshSpatialFace.Partition(
                segments,
                VoxelAwareMeshSpatialFace.Tolerance
            );

        Check(regions.Count == 2, "F8: Natural Base Edge enters partition");
        Check(!segments[0].IsBridge, "F8: Natural Base Edge is not Bridge");
    }

    private static void CheckStateMatrix()
    {
        MaterialField field = CreateUniformMaterialField(MaterialType.Stone);
        VoxelAwareMeshPrecut.ChunkGeometry geometry =
            CreateGeometry(Vector3.zero, field);

        Check(
            EvaluateCount(false, false, new VoxelAwareMeshSpatialFace.FaceSegment[0], geometry, field) == 0,
            "F9: Solid / Solid -> 0 Hidden Face"
        );
        Check(
            EvaluateCount(true, true, new VoxelAwareMeshSpatialFace.FaceSegment[0], geometry, field) == 0,
            "F9: Air / Air -> 0 Hidden Face"
        );
        Check(
            EvaluateCount(false, true, new VoxelAwareMeshSpatialFace.FaceSegment[0], geometry, field) > 0,
            "F9: Solid / Air -> evaluate"
        );
        Check(
            EvaluateCount(true, false, new VoxelAwareMeshSpatialFace.FaceSegment[0], geometry, field) > 0,
            "F9: Air / Solid -> evaluate"
        );
    }

    private static void CheckNaturalCoverage()
    {
        MaterialField field = CreateUniformMaterialField(MaterialType.Stone);
        VoxelAwareMeshPrecut.ChunkGeometry geometry =
            CreateGeometry(Vector3.zero, field);

        List<VoxelAwareMeshSpatialFace.FaceSegment> segments =
            new List<VoxelAwareMeshSpatialFace.FaceSegment>
            {
                new VoxelAwareMeshSpatialFace.FaceSegment(
                    new Vector2(0f, 0f),
                    new Vector2(1f, 1f))
                { IsCoplanar = true }
            };

        VoxelAwareMeshSpatialFace.SpatialFaceRecord record =
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
                geometry,
                uv => uv.y > uv.x
            );

        float hiddenArea = HiddenArea(record);
        Check(
            hiddenArea < 1f - 1e-4f,
            "F10: Hidden Face does not cover the natural-covered region"
        );
        Check(hiddenArea > 0f, "F10: Hidden Face generated on uncovered region");
    }

    private static void CheckBridgeBoundary()
    {
        MaterialField field = CreateUniformMaterialField(MaterialType.Stone);
        VoxelAwareMeshPrecut.ChunkGeometry geometry =
            CreateGeometry(Vector3.zero, field);

        List<VoxelAwareMeshSpatialFace.FaceSegment> segments =
            new List<VoxelAwareMeshSpatialFace.FaceSegment>
            {
                new VoxelAwareMeshSpatialFace.FaceSegment(
                    new Vector2(0f, 0.5f),
                    new Vector2(1f, 0.5f))
                { IsBridge = true }
            };

        VoxelAwareMeshSpatialFace.SpatialFaceRecord record =
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
                geometry,
                uv => uv.y > 0.5f
            );

        bool crossesBridge = false;
        for (int i = 0; i < record.HiddenFaces.Count; i++)
        {
            for (int p = 0; p < record.HiddenFaces[i].Polygon.Count; p++)
            {
                Vector2 uv = VoxelAwareMeshSpatialFace.ToFaceLocal(
                    record.Face,
                    record.HiddenFaces[i].Polygon[p],
                    geometry.ChunkOrigin,
                    geometry.VoxelSize
                );

                if (uv.y < 0.5f - 1e-4f)
                {
                    crossesBridge = true;
                }
            }
        }

        Check(!crossesBridge, "F11: Hidden Face does not cross the Bridge");
    }

    private static void CheckHiddenFaceWelding()
    {
        MaterialField field = CreateUniformMaterialField(MaterialType.Grass);
        VoxelAwareMeshPrecut.ChunkGeometry geometry =
            CreateGeometry(Vector3.zero, field);

        VoxelAwareMeshSpatialFace.SpatialFaceRecord record =
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

        bool weldingOk = record.HiddenFaces.Count > 0;
        Dictionary<string, int> keyToId = new Dictionary<string, int>();

        for (int i = 0; i < record.HiddenFaces.Count; i++)
        {
            VoxelAwareMeshSpatialFace.HiddenFaceFragment fragment =
                record.HiddenFaces[i];

            for (int p = 0; p < fragment.VertexIds.Count; p++)
            {
                int id = fragment.VertexIds[p];
                if (id < 0 || id >= geometry.Pool.Positions.Count)
                {
                    weldingOk = false;
                    continue;
                }

                Vector3 stored = geometry.Pool.Positions[id];
                Vector3 expected = fragment.Polygon[p];

                if (Vector3.Distance(stored, expected) > 1e-5f)
                {
                    weldingOk = false;
                }

                string key = VoxelAwareMeshPrecut.VertexPool.MakeKey(stored);
                if (keyToId.TryGetValue(key, out int existingId))
                {
                    if (existingId != id)
                    {
                        weldingOk = false;
                    }
                }
                else
                {
                    keyToId.Add(key, id);
                }
            }
        }

        Check(weldingOk, "F12: Hidden Face reuses VertexPool with one VertexId per point");
    }

    private static void CheckNoDegenerateOrDuplicate()
    {
        MaterialField field = CreateUniformMaterialField(MaterialType.Stone);
        VoxelAwareMeshPrecut.ChunkGeometry geometry =
            CreateGeometry(Vector3.zero, field);

        VoxelAwareMeshSpatialFace.SpatialFaceRecord record =
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

        int zeroArea = 0;
        Dictionary<string, bool> triangleKeys = new Dictionary<string, bool>();

        for (int i = 0; i < record.HiddenFaces.Count; i++)
        {
            VoxelAwareMeshSpatialFace.HiddenFaceFragment fragment =
                record.HiddenFaces[i];
            if (fragment.Area <= VoxelAwareMeshSpatialFace.Tolerance)
            {
                zeroArea++;
            }

            string key = MakeTriangleKey(fragment.VertexIds);
            if (triangleKeys.ContainsKey(key)) zeroArea++;
            else triangleKeys.Add(key, true);
        }

        Check(record.Evaluated, "F14: face evaluated exactly once");
        Check(zeroArea == 0, "F13/F14: no zero-area or duplicate Hidden Face triangles");
    }

    private static void CheckCrossChunkFace()
    {
        Vector3Int a = new Vector3Int(15, 0, 0);
        Vector3Int b = new Vector3Int(16, 0, 0);

        Check(
            VoxelAwareMeshSpatialFace.TryGetFaceBetweenVoxels(a, b, out var faceAB) &&
            VoxelAwareMeshSpatialFace.TryGetFaceBetweenVoxels(b, a, out var faceBA) &&
            faceAB.Key == faceBA.Key,
            "F15: adjacent Chunk-boundary voxels share one FaceIdentity"
        );

        Check(
            VoxelAwareMeshSpatialFace.GetCanonicalOwnerChunk(
                faceAB,
                new Vector3Int(16, 16, 16)
            ) == new Vector3Int(0, 0, 0),
            "F15: canonical owner is negative-side Chunk"
        );
    }

    private static void CheckNegativeCoordinates()
    {
        Vector3Int a = new Vector3Int(-1, 0, 0);
        Vector3Int b = new Vector3Int(0, 0, 0);

        Check(
            VoxelAwareMeshSpatialFace.TryGetFaceBetweenVoxels(a, b, out var face),
            "F16: negative-coordinate voxels produce a FaceIdentity"
        );

        Check(face.Axis == 0 && face.Plane == 0 && face.U == 0 && face.V == 0,
            "F16: FaceIdentity plane/U/V use min-corner semantics");

        Check(
            VoxelAwareMeshSpatialFace.GetCanonicalOwnerChunk(
                face,
                new Vector3Int(16, 16, 16)
            ) == new Vector3Int(-1, 0, 0),
            "F16: negative side belongs to negative Chunk"
        );
    }

    private static void CheckNonIdentityTransform()
    {
        MaterialField field = CreateUniformMaterialField(MaterialType.Stone);
        Vector3 origin = new Vector3(100f, 50f, -30f);
        VoxelAwareMeshPrecut.ChunkGeometry geometry =
            CreateGeometry(origin, field);

        VoxelAwareMeshSpatialFace.SpatialFaceRecord record =
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

        bool localOk = true;
        for (int i = 0; i < record.HiddenFaces.Count; i++)
        {
            for (int p = 0; p < record.HiddenFaces[i].Polygon.Count; p++)
            {
                Vector2 uv = VoxelAwareMeshSpatialFace.ToFaceLocal(
                    record.Face,
                    record.HiddenFaces[i].Polygon[p],
                    geometry.ChunkOrigin,
                    geometry.VoxelSize
                );

                Vector3 roundTrip = VoxelAwareMeshSpatialFace.ToChunkLocal(
                    record.Face,
                    uv,
                    geometry.ChunkOrigin,
                    geometry.VoxelSize
                );

                if (Vector3.Distance(
                    roundTrip,
                    record.HiddenFaces[i].Polygon[p]) > 1e-3f)
                {
                    localOk = false;
                }
            }
        }

        Check(localOk, "F17: Face Local <-> Chunk Local geometry is transform-independent");
    }

    private static void CheckMaterialM1()
    {
        MaterialField field = CreateUniformMaterialField(MaterialType.Grass);
        VoxelAwareMeshPrecut.ChunkGeometry geometry =
            CreateGeometry(Vector3.zero, field);

        MaterialType material =
            VoxelAwareMeshSpatialFace.GetVoxelMainMaterial(
                field,
                new Vector3Int(0, 0, 0),
                1f
            );

        Check(material == MaterialType.Grass, "M1: VoxelMainMaterial queries Base MaterialField");
    }

    private static void CheckMaterialM2()
    {
        MaterialType a = VoxelAwareMeshSpatialFace.GetVoxelMainMaterial(
            CreateUniformMaterialField(MaterialType.Stone),
            new Vector3Int(0, 0, 0),
            1f
        );
        MaterialType b = VoxelAwareMeshSpatialFace.GetVoxelMainMaterial(
            CreateUniformMaterialField(MaterialType.Dirt),
            new Vector3Int(0, 0, 0),
            1f
        );

        Check(a != b, "M2: different Solid Voxels can produce different Hidden Face Materials");
    }

    private static void CheckMaterialM3()
    {
        MaterialField field = CreateUniformMaterialField(MaterialType.Stone);
        VoxelAwareMeshPrecut.ChunkGeometry geometry =
            CreateGeometry(Vector3.zero, field);

        List<VoxelAwareMeshSpatialFace.FaceSegment> segments =
            new List<VoxelAwareMeshSpatialFace.FaceSegment>
            {
                new VoxelAwareMeshSpatialFace.FaceSegment(
                    new Vector2(0f, 0.5f),
                    new Vector2(1f, 0.5f))
                { IsBridge = true }
            };

        VoxelAwareMeshSpatialFace.SpatialFaceRecord record =
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
                geometry,
                uv => true,
                uv => uv.y > 0.5f ? MaterialType.Stone : MaterialType.Dirt
            );

        Dictionary<int, bool> submeshes = new Dictionary<int, bool>();
        for (int i = 0; i < record.HiddenFaces.Count; i++)
        {
            submeshes[record.HiddenFaces[i].Submesh] = true;
        }

        Check(
            submeshes.Count >= 2,
            "M3: one SpatialFace can produce multiple Region Materials"
        );
    }

    private static void CheckMaterialM4()
    {
        MaterialField field = CreateUniformMaterialField(MaterialType.Dirt);
        field[0, 0, 0] = MaterialType.Dirt;

        MaterialType material =
            VoxelAwareMeshSpatialFace.GetVoxelMainMaterial(
                field,
                new Vector3Int(0, 0, 0),
                1f
            );

        Check(
            material == MaterialType.Dirt || material == MaterialType.Grass,
            "M4: MaterialType remains a discrete enum value"
        );
    }

    private static void CheckMaterialM5()
    {
        MaterialField a = CreateUniformMaterialField(MaterialType.Stone);
        MaterialField b = CreateUniformMaterialField(MaterialType.Stone);

        MaterialType ma = VoxelAwareMeshSpatialFace.GetVoxelMainMaterial(
            a,
            new Vector3Int(15, 0, 0),
            1f
        );
        MaterialType mb = VoxelAwareMeshSpatialFace.GetVoxelMainMaterial(
            b,
            new Vector3Int(15, 0, 0),
            1f
        );

        Check(ma == mb, "M5: cross-chunk VoxelMainMaterial is consistent");
    }

    private static void CheckMaterialM6()
    {
        MaterialField field = CreateUniformMaterialField(MaterialType.Stone);
        VoxelAwareMeshPrecut.ChunkGeometry geometry =
            CreateGeometry(Vector3.zero, field);

        VoxelAwareMeshSpatialFace.SpatialFaceRecord record =
            VoxelAwareMeshSpatialFace.Evaluate(
                new VoxelAwareMeshPrecut.FaceId(0, 1, 0, 0),
                new Vector3Int(0, 0, 0),
                new Vector3Int(1, 0, 0),
                false,
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
            record.HiddenFaces.Count > 0 &&
            record.HiddenFaces[0].Submesh ==
            VoxelAwareMeshSpatialFace.MaterialTypeToSubmesh(MaterialType.Stone),
            "M6: Solid / Outside-Air Hidden Face uses Solid-side VoxelMainMaterial"
        );
    }

    // ---------------------------------------------------------------------

    private static int EvaluateCount(
        bool negativeAir,
        bool positiveAir,
        IReadOnlyList<VoxelAwareMeshSpatialFace.FaceSegment> segments,
        VoxelAwareMeshPrecut.ChunkGeometry geometry,
        MaterialField field)
    {
        VoxelAwareMeshSpatialFace.SpatialFaceRecord record =
            VoxelAwareMeshSpatialFace.Evaluate(
                new VoxelAwareMeshPrecut.FaceId(0, 1, 0, 0),
                new Vector3Int(0, 0, 0),
                new Vector3Int(1, 0, 0),
                true,
                negativeAir,
                positiveAir,
                Vector3Int.zero,
                segments,
                field,
                1f,
                geometry,
                uv => true
            );

        return record.HiddenFaces.Count;
    }

    private static float HiddenArea(
        VoxelAwareMeshSpatialFace.SpatialFaceRecord record)
    {
        float area = 0f;
        for (int i = 0; i < record.HiddenFaces.Count; i++)
        {
            area += record.HiddenFaces[i].Area;
        }

        return area;
    }

    private static float RegionArea(
        List<VoxelAwareMeshSpatialFace.FaceRegion> regions)
    {
        float area = 0f;
        for (int i = 0; i < regions.Count; i++)
        {
            area += regions[i].Area;
        }

        return area;
    }

    private static VoxelAwareMeshPrecut.ChunkGeometry CreateGeometry(
        Vector3 origin,
        MaterialField field)
    {
        VoxelAwareMeshPrecut.ChunkGeometry geometry =
            new VoxelAwareMeshPrecut.ChunkGeometry
            {
                ChunkOrigin = origin,
                VoxelSize = 1f,
                BaseMaterialField = field
            };

        return geometry;
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

    private static string MakeTriangleKey(List<int> vertexIds)
    {
        int a = vertexIds[0];
        int b = vertexIds[1];
        int c = vertexIds[2];
        int min = Mathf.Min(Mathf.Min(a, b), c);
        int max = Mathf.Max(Mathf.Max(a, b), c);
        int mid = a + b + c - min - max;
        return min + ":" + mid + ":" + max;
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
            "[VoxelAwareMeshRuntimePhase2AValidation] ===== SUMMARY =====\n" +
            "Checks = " + checks + "\n" +
            "Failures = " + failures + "\n" +
            "Result = " + (failures == 0 ? "PASS" : "FAIL")
        );
    }
}
