using System;
using TerrainModificationV2;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Terrain Modification &amp; Mining Specification V2.0 Step 3B Validation。
/// 验证 TerrainEffectiveSurfaceSampling 契约。
/// 纯 C# 测试，不创建 Scene / DensityField / Mesh / Raycast。
/// </summary>
public static class TerrainModificationV2EffectiveSurfaceSamplingValidation
{
    private const float Epsilon = 0.00001f;

    private static int failures;
    private static int checks;

    [MenuItem("Tools/Terrain/Validate Terrain Modification V2 Surface Sampling Contract")]
    public static void ValidateFromMenu()
    {
        ValidateAll();
        LogSummary();

        Debug.Log(
            "[TerrainModificationV2EffectiveSurfaceSamplingValidation] Menu validation result = " +
            (failures == 0 ? "PASS" : "FAIL")
        );
    }

    /// <summary>Batchmode 入口：-executeMethod TerrainModificationV2EffectiveSurfaceSamplingValidation.RunBatch。</summary>
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

        ValidateSampleState();
        ValidateRequiredSamplingRegion();
        ValidateSurfaceCellSizeRelationship();
        ValidateAdjacentCells();
        ValidateDuplicateCells();
        ValidateInvalidConfiguration();
        ValidateNoDensityDependency();
    }

    private static void ValidateSampleState()
    {
        TerrainEditGrid grid = new TerrainEditGrid(1f);
        TerrainEditLayer layer = new TerrainEditLayer();
        TerrainEffectiveTerrainQuery query =
            new TerrainEffectiveTerrainQuery(grid, layer);
        TerrainEffectiveSurfaceSampling sampling =
            new TerrainEffectiveSurfaceSampling(grid, query);

        Check(
            sampling.Sample(new Vector3(2.5f, 3.5f, -0.5f)) ==
            TerrainEffectiveSampleState.Base,
            "Sample state: unexcavated position is Base"
        );

        layer.Add(CreateExcavate(1L, 1L, new Vector3Int(2, 3, -1)));

        Check(
            sampling.Sample(new Vector3(2.5f, 3.5f, -0.5f)) ==
            TerrainEffectiveSampleState.Excavated,
            "Sample state: excavated position is Excavated"
        );
        Check(
            sampling.Sample(new Vector3(3.5f, 3.5f, -0.5f)) ==
            TerrainEffectiveSampleState.Base,
            "Sample state: adjacent unexcavated position is Base"
        );
    }

    private static void ValidateRequiredSamplingRegion()
    {
        TerrainEditGrid grid = new TerrainEditGrid(1f);
        TerrainEditLayer layer = new TerrainEditLayer();
        TerrainEffectiveTerrainQuery query =
            new TerrainEffectiveTerrainQuery(grid, layer);
        TerrainEffectiveSurfaceSampling sampling =
            new TerrainEffectiveSurfaceSampling(grid, query, 2);

        TerrainEffectiveSampleRegion region =
            sampling.GetRequiredSamplingRegion(new Vector3Int(0, 0, 0));

        Check(
            region.EditCellCoordinate == new Vector3Int(0, 0, 0),
            "Region: EditCellCoordinate == (0,0,0)"
        );
        Check(
            VectorDistance(region.Min, new Vector3(-0.5f, -0.5f, -0.5f)) < Epsilon,
            "Region: min == (-0.5,-0.5,-0.5) for N=2"
        );
        Check(
            VectorDistance(region.Max, new Vector3(1.5f, 1.5f, 1.5f)) < Epsilon,
            "Region: max == (1.5,1.5,1.5) for N=2"
        );
        Check(
            Mathf.Abs(region.Spacing - 0.5f) < Epsilon,
            "Region: spacing == 0.5 for N=2"
        );

        TerrainEffectiveSurfaceSampling samplingN1 =
            new TerrainEffectiveSurfaceSampling(grid, query, 1);

        TerrainEffectiveSampleRegion regionN1 =
            samplingN1.GetRequiredSamplingRegion(new Vector3Int(0, 0, 0));

        Check(
            VectorDistance(regionN1.Min, new Vector3(-1f, -1f, -1f)) < Epsilon,
            "Region N=1: min == (-1,-1,-1)"
        );
        Check(
            VectorDistance(regionN1.Max, new Vector3(2f, 2f, 2f)) < Epsilon,
            "Region N=1: max == (2,2,2)"
        );

        TerrainEffectiveSampleRegion negativeRegion =
            sampling.GetRequiredSamplingRegion(new Vector3Int(-1, 0, 2));

        Check(
            VectorDistance(
                negativeRegion.Min,
                new Vector3(-1.5f, -0.5f, 1.5f)) < Epsilon,
            "Region negative cell: min is correctly expanded"
        );
        Check(
            VectorDistance(
                negativeRegion.Max,
                new Vector3(0.5f, 1.5f, 3.5f)) < Epsilon,
            "Region negative cell: max is correctly expanded"
        );
    }

    private static void ValidateSurfaceCellSizeRelationship()
    {
        TerrainEditGrid grid = new TerrainEditGrid(1f);
        TerrainEditLayer layer = new TerrainEditLayer();
        TerrainEffectiveTerrainQuery query =
            new TerrainEffectiveTerrainQuery(grid, layer);

        TerrainEffectiveSurfaceSampling n2 =
            new TerrainEffectiveSurfaceSampling(grid, query, 2);
        Check(
            Mathf.Abs(n2.SurfaceCellSize - 0.5f) < Epsilon,
            "SurfaceCellSize = EditCellSize / 2"
        );

        TerrainEffectiveSurfaceSampling n4 =
            new TerrainEffectiveSurfaceSampling(grid, query, 4);
        Check(
            Mathf.Abs(n4.SurfaceCellSize - 0.25f) < Epsilon,
            "SurfaceCellSize = EditCellSize / 4"
        );

        TerrainEditGrid grid2 = new TerrainEditGrid(2f);
        TerrainEffectiveTerrainQuery query2 =
            new TerrainEffectiveTerrainQuery(grid2, layer);
        TerrainEffectiveSurfaceSampling n2Grid2 =
            new TerrainEffectiveSurfaceSampling(grid2, query2, 2);
        Check(
            Mathf.Abs(n2Grid2.SurfaceCellSize - 1f) < Epsilon,
            "SurfaceCellSize adapts to EditCellSize"
        );
    }

    private static void ValidateAdjacentCells()
    {
        TerrainEditGrid grid = new TerrainEditGrid(1f);
        TerrainEditLayer layer = new TerrainEditLayer();
        TerrainEffectiveTerrainQuery query =
            new TerrainEffectiveTerrainQuery(grid, layer);
        TerrainEffectiveSurfaceSampling sampling =
            new TerrainEffectiveSurfaceSampling(grid, query, 2);

        layer.Add(CreateExcavate(1L, 1L, new Vector3Int(0, 0, 0)));
        layer.Add(CreateExcavate(2L, 2L, new Vector3Int(1, 0, 0)));

        Check(
            sampling.Sample(new Vector3(0.5f, 0.5f, 0.5f)) ==
            TerrainEffectiveSampleState.Excavated,
            "Adjacent cells: first cell is excavated"
        );
        Check(
            sampling.Sample(new Vector3(1.5f, 0.5f, 0.5f)) ==
            TerrainEffectiveSampleState.Excavated,
            "Adjacent cells: second cell is excavated"
        );
        Check(
            sampling.Sample(new Vector3(2.5f, 0.5f, 0.5f)) ==
            TerrainEffectiveSampleState.Base,
            "Adjacent cells: outside region is Base"
        );
    }

    private static void ValidateDuplicateCells()
    {
        TerrainEditGrid grid = new TerrainEditGrid(1f);
        TerrainEditLayer layer = new TerrainEditLayer();
        TerrainEffectiveTerrainQuery query =
            new TerrainEffectiveTerrainQuery(grid, layer);
        TerrainEffectiveSurfaceSampling sampling =
            new TerrainEffectiveSurfaceSampling(grid, query, 2);

        layer.Add(CreateExcavate(1L, 1L, new Vector3Int(2, 3, 4)));
        layer.Add(CreateExcavate(2L, 2L, new Vector3Int(2, 3, 4)));
        layer.Add(CreateExcavate(3L, 3L, new Vector3Int(2, 3, 4)));

        Check(
            sampling.Sample(new Vector3(2.5f, 3.5f, 4.5f)) ==
            TerrainEffectiveSampleState.Excavated,
            "Duplicate cells: repeated records remain idempotent"
        );
    }

    private static void ValidateInvalidConfiguration()
    {
        Check(
            ThrowsArgumentNull(
                () => new TerrainEffectiveSurfaceSampling(
                    null,
                    new TerrainEffectiveTerrainQuery(
                        new TerrainEditGrid(1f),
                        new TerrainEditLayer()))),
            "Null edit grid throws ArgumentNullException"
        );

        Check(
            ThrowsArgumentNull(
                () => new TerrainEffectiveSurfaceSampling(
                    new TerrainEditGrid(1f),
                    null)),
            "Null query throws ArgumentNullException"
        );

        Check(
            ThrowsArgumentOutOfRange(
                () => new TerrainEffectiveSurfaceSampling(
                    new TerrainEditGrid(1f),
                    new TerrainEffectiveTerrainQuery(
                        new TerrainEditGrid(1f),
                        new TerrainEditLayer()),
                    0)),
            "SamplesPerEditCell 0 throws ArgumentOutOfRangeException"
        );

        Check(
            ThrowsArgumentOutOfRange(
                () => new TerrainEffectiveSurfaceSampling(
                    new TerrainEditGrid(1f),
                    new TerrainEffectiveTerrainQuery(
                        new TerrainEditGrid(1f),
                        new TerrainEditLayer()),
                    -1)),
            "Negative SamplesPerEditCell throws ArgumentOutOfRangeException"
        );
    }

    private static void ValidateNoDensityDependency()
    {
        // 本测试明确不创建 DensityField / Mesh / Chunk / Scene。
        TerrainEditGrid grid = new TerrainEditGrid(1f);
        TerrainEditLayer layer = new TerrainEditLayer();
        TerrainEffectiveTerrainQuery query =
            new TerrainEffectiveTerrainQuery(grid, layer);
        TerrainEffectiveSurfaceSampling sampling =
            new TerrainEffectiveSurfaceSampling(grid, query, 2);

        layer.Add(CreateExcavate(1L, 1L, new Vector3Int(10, 5, 3)));

        Check(
            sampling.Sample(new Vector3(10.5f, 5.5f, 3.5f)) ==
            TerrainEffectiveSampleState.Excavated,
            "No density dependency: sampling contract works without DensityField"
        );
    }

    private static TerrainEditRecord CreateExcavate(
        long editId,
        long sequence,
        Vector3Int cellCoordinate)
    {
        return new TerrainEditRecord(
            editId,
            sequence,
            TerrainEditOperation.Excavate,
            cellCoordinate
        );
    }

    private static bool ThrowsArgumentNull(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (ArgumentNullException)
        {
            return true;
        }
    }

    private static bool ThrowsArgumentOutOfRange(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (ArgumentOutOfRangeException)
        {
            return true;
        }
    }

    private static float VectorDistance(Vector3 a, Vector3 b)
    {
        return Vector3.Distance(a, b);
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
            "[TerrainModificationV2EffectiveSurfaceSamplingValidation] ===== SUMMARY =====\n" +
            "Checks = " + checks + "\n" +
            "Failures = " + failures + "\n" +
            "Result = " + (failures == 0 ? "PASS" : "FAIL")
        );
    }
}
