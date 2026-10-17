using TerrainModificationV2;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Voxel → Density → Marching Cubes Geometry Validation (Step 2B)。
/// 用真实 TerrainMeshGenerator.Generate() 验证 VoxelField 经
/// VoxelDensityAdapter 转成 DensityField 后生成的 Mesh 顶点空间位置。
///
/// 坐标系说明：
///   - Mesh.vertices 是 Chunk Local（相对 DensityField.Origin）。
///   - Terrain Local = DensityField.Origin + Mesh.vertices。
///   - Adapter 把 sample i 放在 Voxel i 中心：
///       Terrain Local = regionOrigin + (i + 0.5) × VoxelSize。
///   - MC 沿相邻 sample 边做线性插值，+1 / -1 的零等值面交点
///     正好落在相邻 Voxel 的共享面上。
/// </summary>
public static class VoxelMarchingCubesGeometryValidation
{
    private const float Epsilon = 0.0001f;

    private static int failures;
    private static int checks;

    [MenuItem("Tools/Terrain/Validate Voxel Marching Cubes Geometry")]
    public static void ValidateFromMenu()
    {
        ValidateAll();
        LogSummary();

        Debug.Log(
            "[VoxelMarchingCubesGeometryValidation] Menu validation result = " +
            (failures == 0 ? "PASS" : "FAIL")
        );
    }

    /// <summary>Batchmode 入口：-executeMethod VoxelMarchingCubesGeometryValidation.RunBatch。</summary>
    public static void RunBatch()
    {
        ValidateAll();
        LogSummary();
        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    private static void ValidateAll()
    {
        failures = 0;
        checks = 0;

        ValidateResolutionSampleCountRelation();
        ValidateAllSolidProducesNoSurface();
        ValidateSingleAirVoxelBounds();
        ValidateSolidAirSolidBoundaries();
        ValidateTwoAdjacentAirVoxels();
        ValidateNegativeAirVoxelBounds();
    }

    private static void ValidateResolutionSampleCountRelation()
    {
        VoxelField voxelField = new VoxelField(1f);
        VoxelDensityAdapter adapter = new VoxelDensityAdapter();

        Vector3 regionOrigin = new Vector3(-1f, -1f, -1f);
        DensityField density = adapter.Build(
            voxelField,
            regionOrigin,
            new Vector3(3f, 3f, 3f),
            3
        );

        Check(
            density.ResolutionX == 3 &&
            density.ResolutionY == 3 &&
            density.ResolutionZ == 3,
            "Resolution == 3 per axis"
        );

        Check(
            density.SampleCountX == 4 &&
            density.SampleCountY == 4 &&
            density.SampleCountZ == 4,
            "SampleCount == Resolution + 1 per axis"
        );

        Check(
            Approx(density.CellSizeX, voxelField.VoxelSize) &&
            Approx(density.CellSizeY, voxelField.VoxelSize) &&
            Approx(density.CellSizeZ, voxelField.VoxelSize),
            "Adapter CellSize == VoxelSize per axis"
        );

        Check(
            Approx(density.Origin, regionOrigin + Vector3.one * 0.5f),
            "DensityField Origin == region origin + 0.5 × VoxelSize"
        );

        Check(
            density.GetChunkLocalPosition(0, 0, 0) == Vector3.zero,
            "Chunk Local sample (0,0,0) is at Chunk origin"
        );

        Check(
            Approx(density.GetChunkLocalPosition(1, 0, 0), new Vector3(1f, 0f, 0f)),
            "Chunk Local sample (1,0,0) is at CellSize on X"
        );

        Check(
            Approx(density.GetLocalPosition(0, 0, 0), new Vector3(-0.5f, -0.5f, -0.5f)),
            "Terrain Local sample (0,0,0) is Voxel(-1) center"
        );

        Check(
            Approx(density.GetLocalPosition(1, 0, 0), new Vector3(0.5f, -0.5f, -0.5f)),
            "Terrain Local sample (1,0,0) is Voxel(0) center"
        );
    }

    private static void ValidateAllSolidProducesNoSurface()
    {
        VoxelField voxelField = new VoxelField(1f);
        Mesh mesh = BuildMesh(
            voxelField,
            new Vector3(-1f, -1f, -1f),
            3
        );

        Check(mesh != null, "All solid: Mesh was created");
        Check(mesh.vertexCount == 0, "All solid: no surface vertices");
        Check(TotalTriangleCount(mesh) == 0, "All solid: no triangles");

        UnityEngine.Object.DestroyImmediate(mesh);
    }

    private static void ValidateSingleAirVoxelBounds()
    {
        VoxelField voxelField = new VoxelField(1f);
        voxelField.Set(new Vector3Int(0, 0, 0), VoxelState.Air);

        Mesh mesh = BuildMesh(
            voxelField,
            new Vector3(-1f, -1f, -1f),
            3,
            out DensityField density
        );

        Vector3 min = Vector3.zero;
        Vector3 max = Vector3.one;

        Check(mesh.vertexCount > 0, "Single air voxel: mesh has vertices");
        Check(TotalTriangleCount(mesh) > 0, "Single air voxel: mesh has triangles");

        Check(
            AllVerticesWithinAndOnBoundary(mesh, density, min, max),
            "Single air voxel: every vertex is inside [0,1]³ and on one of the six voxel boundary planes"
        );

        Check(
            AllBoundaryPlanesPresent(mesh, density, min, max),
            "Single air voxel: all six boundary planes x/y/z = 0/1 are present"
        );

        Check(
            AllVerticesHalfCellAligned(mesh, density),
            "Single air voxel: every Chunk Local vertex sits on the 0.5×CellSize grid"
        );

        UnityEngine.Object.DestroyImmediate(mesh);
    }

    private static void ValidateSolidAirSolidBoundaries()
    {
        VoxelField voxelField = new VoxelField(1f);
        voxelField.Set(new Vector3Int(-1, 0, 0), VoxelState.Solid);
        voxelField.Set(new Vector3Int(0, 0, 0), VoxelState.Air);
        voxelField.Set(new Vector3Int(1, 0, 0), VoxelState.Solid);

        Mesh mesh = BuildMesh(
            voxelField,
            new Vector3(-1f, -1f, -1f),
            3,
            out DensityField density
        );

        Check(mesh.vertexCount > 0, "Solid|Air|Solid: mesh has vertices");

        Check(
            AllVerticesAxisWithin(mesh, density, 0, 0f, 1f),
            "Solid|Air|Solid: every vertex X coordinate is inside [0,1]"
        );

        Check(
            AxisBoundaryPresent(mesh, density, 0, 0f) &&
            AxisBoundaryPresent(mesh, density, 0, 1f),
            "Solid|Air|Solid: surfaces exist at x=0 and x=1"
        );

        Check(
            AllVerticesWithinAndOnBoundary(mesh, density, Vector3.zero, Vector3.one),
            "Solid|Air|Solid: every vertex is inside [0,1]³ and on a boundary plane"
        );

        UnityEngine.Object.DestroyImmediate(mesh);
    }

    private static void ValidateTwoAdjacentAirVoxels()
    {
        VoxelField voxelField = new VoxelField(1f);
        voxelField.Set(new Vector3Int(0, 0, 0), VoxelState.Air);
        voxelField.Set(new Vector3Int(1, 0, 0), VoxelState.Air);

        // 覆盖 Voxel(-1..2)，Air 区域理论范围是 x ∈ [0,2)，y/z ∈ [0,1)。
        Mesh mesh = BuildMesh(
            voxelField,
            new Vector3(-1f, -1f, -1f),
            4,
            out DensityField density
        );

        Vector3 min = new Vector3(0f, 0f, 0f);
        Vector3 max = new Vector3(2f, 1f, 1f);

        Check(mesh.vertexCount > 0, "Adjacent air voxels: mesh has vertices");

        Check(
            AllVerticesWithinAndOnBoundary(mesh, density, min, max),
            "Adjacent air voxels: every vertex is inside the air region and on a boundary plane"
        );

        Check(
            AllBoundaryPlanesPresent(mesh, density, min, max),
            "Adjacent air voxels: outer boundary planes x=0, x=2, y=0, y=1, z=0, z=1 are present"
        );

        Check(
            !AnyCoordinateNear(mesh, density, 0, 1f),
            "Adjacent air voxels: no surface at shared face x=1"
        );

        Check(
            AllVerticesHalfCellAligned(mesh, density),
            "Adjacent air voxels: every Chunk Local vertex sits on the 0.5×CellSize grid"
        );

        UnityEngine.Object.DestroyImmediate(mesh);
    }

    private static void ValidateNegativeAirVoxelBounds()
    {
        VoxelField voxelField = new VoxelField(1f);
        voxelField.Set(new Vector3Int(-1, 0, 0), VoxelState.Air);

        Mesh mesh = BuildMesh(
            voxelField,
            new Vector3(-2f, -1f, -1f),
            3,
            out DensityField density
        );

        Vector3 min = new Vector3(-1f, 0f, 0f);
        Vector3 max = new Vector3(0f, 1f, 1f);

        Check(mesh.vertexCount > 0, "Negative air voxel: mesh has vertices");

        Check(
            AllVerticesWithinAndOnBoundary(mesh, density, min, max),
            "Negative air voxel: every vertex is inside [-1,0]×[0,1]×[0,1] and on a boundary plane"
        );

        Check(
            AxisBoundaryPresent(mesh, density, 0, -1f) &&
            AxisBoundaryPresent(mesh, density, 0, 0f),
            "Negative air voxel: surfaces exist at x=-1 and x=0"
        );

        Check(
            AllVerticesHalfCellAligned(mesh, density),
            "Negative air voxel: every Chunk Local vertex sits on the 0.5×CellSize grid"
        );

        UnityEngine.Object.DestroyImmediate(mesh);
    }

    private static Mesh BuildMesh(
        VoxelField voxelField,
        Vector3 regionOrigin,
        int resolution)
    {
        return BuildMesh(voxelField, regionOrigin, resolution, out _);
    }

    private static Mesh BuildMesh(
        VoxelField voxelField,
        Vector3 regionOrigin,
        int resolution,
        out DensityField density)
    {
        VoxelDensityAdapter adapter = new VoxelDensityAdapter();

        float voxelSize = voxelField.VoxelSize;
        Vector3 size = new Vector3(
            resolution * voxelSize,
            resolution * voxelSize,
            resolution * voxelSize
        );

        density = adapter.Build(
            voxelField,
            regionOrigin,
            size,
            resolution
        );

        return TerrainMeshGenerator.Generate(density, null, 0f);
    }

    // ---------------------------------------------------------------------
    // Geometry predicates（Mesh.vertices 是 Chunk Local；
    // 所有空间断言都在 Terrain Local = DensityField.Origin + vertex 上完成）
    // ---------------------------------------------------------------------

    private static bool AllVerticesWithinAndOnBoundary(
        Mesh mesh,
        DensityField density,
        Vector3 min,
        Vector3 max)
    {
        Vector3[] vertices = mesh.vertices;

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 p = density.Origin + vertices[i];

            bool inside =
                p.x >= min.x - Epsilon && p.x <= max.x + Epsilon &&
                p.y >= min.y - Epsilon && p.y <= max.y + Epsilon &&
                p.z >= min.z - Epsilon && p.z <= max.z + Epsilon;

            bool onBoundary =
                Approx(p.x, min.x) || Approx(p.x, max.x) ||
                Approx(p.y, min.y) || Approx(p.y, max.y) ||
                Approx(p.z, min.z) || Approx(p.z, max.z);

            if (!inside || !onBoundary)
            {
                return false;
            }
        }

        return true;
    }

    private static bool AllBoundaryPlanesPresent(
        Mesh mesh,
        DensityField density,
        Vector3 min,
        Vector3 max)
    {
        bool foundMinX = false;
        bool foundMaxX = false;
        bool foundMinY = false;
        bool foundMaxY = false;
        bool foundMinZ = false;
        bool foundMaxZ = false;

        Vector3[] vertices = mesh.vertices;

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 p = density.Origin + vertices[i];

            if (Approx(p.x, min.x)) foundMinX = true;
            if (Approx(p.x, max.x)) foundMaxX = true;
            if (Approx(p.y, min.y)) foundMinY = true;
            if (Approx(p.y, max.y)) foundMaxY = true;
            if (Approx(p.z, min.z)) foundMinZ = true;
            if (Approx(p.z, max.z)) foundMaxZ = true;
        }

        return foundMinX && foundMaxX && foundMinY && foundMaxY && foundMinZ && foundMaxZ;
    }

    private static bool AllVerticesAxisWithin(
        Mesh mesh,
        DensityField density,
        int axis,
        float min,
        float max)
    {
        Vector3[] vertices = mesh.vertices;

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 p = density.Origin + vertices[i];
            float value = axis == 0 ? p.x : axis == 1 ? p.y : p.z;

            if (value < min - Epsilon || value > max + Epsilon)
            {
                return false;
            }
        }

        return true;
    }

    private static bool AxisBoundaryPresent(
        Mesh mesh,
        DensityField density,
        int axis,
        float boundary)
    {
        Vector3[] vertices = mesh.vertices;

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 p = density.Origin + vertices[i];
            float value = axis == 0 ? p.x : axis == 1 ? p.y : p.z;

            if (Approx(value, boundary))
            {
                return true;
            }
        }

        return false;
    }

    private static bool AnyCoordinateNear(
        Mesh mesh,
        DensityField density,
        int axis,
        float value)
    {
        Vector3[] vertices = mesh.vertices;

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 p = density.Origin + vertices[i];
            float coordinate = axis == 0 ? p.x : axis == 1 ? p.y : p.z;

            if (Approx(coordinate, value))
            {
                return true;
            }
        }

        return false;
    }

    private static bool AllVerticesHalfCellAligned(Mesh mesh, DensityField density)
    {
        float halfX = density.CellSizeX * 0.5f;
        float halfY = density.CellSizeY * 0.5f;
        float halfZ = density.CellSizeZ * 0.5f;

        Vector3[] vertices = mesh.vertices;

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 p = vertices[i];

            if (!IsMultipleOf(p.x, halfX) ||
                !IsMultipleOf(p.y, halfY) ||
                !IsMultipleOf(p.z, halfZ))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsMultipleOf(float value, float step)
    {
        if (step <= 0f)
        {
            return false;
        }

        float nearest = Mathf.Round(value / step) * step;
        return Mathf.Abs(value - nearest) < Epsilon;
    }

    private static int TotalTriangleCount(Mesh mesh)
    {
        int total = 0;
        for (int i = 0; i < mesh.subMeshCount; i++)
        {
            total += mesh.GetTriangles(i).Length / 3;
        }

        return total;
    }

    private static bool Approx(float a, float b)
    {
        return Mathf.Abs(a - b) < Epsilon;
    }

    private static bool Approx(Vector3 a, Vector3 b)
    {
        return Approx(a.x, b.x) && Approx(a.y, b.y) && Approx(a.z, b.z);
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

    private static void LogSummary()
    {
        Debug.Log(
            "[VoxelMarchingCubesGeometryValidation] ===== SUMMARY =====\n" +
            "Checks = " + checks + "\n" +
            "Failures = " + failures + "\n" +
            "Result = " + (failures == 0 ? "PASS" : "FAIL")
        );
    }
}
