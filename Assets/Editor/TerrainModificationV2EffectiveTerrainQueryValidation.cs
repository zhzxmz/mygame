using System;
using TerrainModificationV2;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Terrain Modification &amp; Mining Specification V2.0 Step 3A Validation。
/// 验证 TerrainEffectiveTerrainQuery 的 Cell Membership 语义。
/// 纯 C# 测试，不创建 Scene / GameObject / DensityField / Mesh / Raycast。
/// </summary>
public static class TerrainModificationV2EffectiveTerrainQueryValidation
{
    private static int failures;
    private static int checks;

    [MenuItem("Tools/Terrain/Validate Terrain Modification V2 Effective Terrain Query")]
    public static void ValidateFromMenu()
    {
        ValidateAll();
        LogSummary();

        Debug.Log(
            "[TerrainModificationV2EffectiveTerrainQueryValidation] Menu validation result = " +
            (failures == 0 ? "PASS" : "FAIL")
        );
    }

    /// <summary>Batchmode 入口：-executeMethod TerrainModificationV2EffectiveTerrainQueryValidation.RunBatch。</summary>
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

        ValidateNoEdits();
        ValidateSingleExcavatedCell();
        ValidateMultipleExcavatedCells();
        ValidateDuplicateRecords();
        ValidateHalfOpenBoundary();
        ValidateNegativeCoordinates();
        ValidateNonUnitCellSize();
        ValidateDensityIndependence();
        ValidateNullDependencies();
    }

    private static void ValidateNoEdits()
    {
        TerrainEditGrid grid = new TerrainEditGrid(1f);
        TerrainEditLayer layer = new TerrainEditLayer();
        TerrainEffectiveTerrainQuery query =
            new TerrainEffectiveTerrainQuery(grid, layer);

        Check(
            !query.IsExcavated(Vector3.zero),
            "No edits: any position is not excavated"
        );
        Check(
            !query.IsExcavated(new Vector3(100f, -20f, 30f)),
            "No edits: arbitrary position is not excavated"
        );
    }

    private static void ValidateSingleExcavatedCell()
    {
        TerrainEditGrid grid = new TerrainEditGrid(1f);
        TerrainEditLayer layer = new TerrainEditLayer();
        TerrainEffectiveTerrainQuery query =
            new TerrainEffectiveTerrainQuery(grid, layer);

        layer.Add(CreateExcavate(1L, 1L, new Vector3Int(2, 3, -1)));

        Check(
            query.IsExcavated(new Vector3(2.5f, 3.5f, -0.5f)),
            "Single cell: inside position is excavated"
        );
        Check(
            !query.IsExcavated(new Vector3(3.5f, 3.5f, -0.5f)),
            "Single cell: adjacent cell is not excavated"
        );
    }

    private static void ValidateMultipleExcavatedCells()
    {
        TerrainEditGrid grid = new TerrainEditGrid(1f);
        TerrainEditLayer layer = new TerrainEditLayer();
        TerrainEffectiveTerrainQuery query =
            new TerrainEffectiveTerrainQuery(grid, layer);

        layer.Add(CreateExcavate(1L, 1L, new Vector3Int(0, 0, 0)));
        layer.Add(CreateExcavate(2L, 2L, new Vector3Int(1, 0, 0)));
        layer.Add(CreateExcavate(3L, 3L, new Vector3Int(5, 2, -3)));

        Check(
            query.IsExcavated(new Vector3(0.5f, 0.5f, 0.5f)),
            "Multiple cells: Cell(0,0,0) is excavated"
        );
        Check(
            query.IsExcavated(new Vector3(1.5f, 0.5f, 0.5f)),
            "Multiple cells: Cell(1,0,0) is excavated"
        );
        Check(
            query.IsExcavated(new Vector3(5.5f, 2.5f, -2.5f)),
            "Multiple cells: Cell(5,2,-3) is excavated"
        );
        Check(
            !query.IsExcavated(new Vector3(3.5f, 0.5f, 0.5f)),
            "Multiple cells: Cell(3,0,0) is not excavated"
        );
    }

    private static void ValidateDuplicateRecords()
    {
        TerrainEditGrid grid = new TerrainEditGrid(1f);
        TerrainEditLayer layer = new TerrainEditLayer();
        TerrainEffectiveTerrainQuery query =
            new TerrainEffectiveTerrainQuery(grid, layer);

        layer.Add(CreateExcavate(1L, 1L, new Vector3Int(2, 3, 4)));
        layer.Add(CreateExcavate(2L, 2L, new Vector3Int(2, 3, 4)));
        layer.Add(CreateExcavate(3L, 3L, new Vector3Int(2, 3, 4)));

        Check(
            query.IsExcavated(new Vector3(2.5f, 3.5f, 4.5f)),
            "Duplicate records: cell remains excavated"
        );
        Check(
            query.IsExcavated(new Vector3(2.5f, 3.5f, 4.5f)),
            "Duplicate records: query is idempotent"
        );
    }

    private static void ValidateHalfOpenBoundary()
    {
        TerrainEditGrid grid = new TerrainEditGrid(1f);
        TerrainEditLayer layer = new TerrainEditLayer();
        TerrainEffectiveTerrainQuery query =
            new TerrainEffectiveTerrainQuery(grid, layer);

        layer.Add(CreateExcavate(1L, 1L, new Vector3Int(0, 0, 0)));

        Check(
            query.IsExcavated(new Vector3(0f, 0f, 0f)),
            "Half-open: min boundary is inside Cell(0,0,0)"
        );
        Check(
            query.IsExcavated(new Vector3(0.999f, 0.999f, 0.999f)),
            "Half-open: 0.999 is inside Cell(0,0,0)"
        );
        Check(
            !query.IsExcavated(new Vector3(1f, 0.5f, 0.5f)),
            "Half-open: max boundary belongs to adjacent Cell"
        );
    }

    private static void ValidateNegativeCoordinates()
    {
        TerrainEditGrid grid = new TerrainEditGrid(1f);
        TerrainEditLayer layer = new TerrainEditLayer();
        TerrainEffectiveTerrainQuery query =
            new TerrainEffectiveTerrainQuery(grid, layer);

        layer.Add(CreateExcavate(1L, 1L, new Vector3Int(-1, 0, 0)));

        Check(
            query.IsExcavated(new Vector3(-0.001f, 0.5f, 0.5f)),
            "Negative: (-0.001) belongs to Cell(-1,0,0)"
        );
        Check(
            query.IsExcavated(new Vector3(-0.999f, 0.5f, 0.5f)),
            "Negative: (-0.999) belongs to Cell(-1,0,0)"
        );
        Check(
            !query.IsExcavated(new Vector3(0f, 0.5f, 0.5f)),
            "Negative: 0.0 belongs to Cell(0,0,0)"
        );
    }

    private static void ValidateNonUnitCellSize()
    {
        TerrainEditGrid grid = new TerrainEditGrid(2f);
        TerrainEditLayer layer = new TerrainEditLayer();
        TerrainEffectiveTerrainQuery query =
            new TerrainEffectiveTerrainQuery(grid, layer);

        layer.Add(CreateExcavate(1L, 1L, new Vector3Int(0, 0, 0)));

        Check(
            query.IsExcavated(new Vector3(1.99f, 0.5f, 0.5f)),
            "CellSize 2: 1.99 belongs to Cell(0,0,0)"
        );
        Check(
            !query.IsExcavated(new Vector3(2f, 0.5f, 0.5f)),
            "CellSize 2: 2.0 belongs to Cell(1,0,0)"
        );
    }

    private static void ValidateDensityIndependence()
    {
        // 本测试明确不创建 DensityField。
        // Effective Terrain Query 只依赖 TerrainEditGrid + TerrainEditLayer。
        TerrainEditGrid grid = new TerrainEditGrid(1f);
        TerrainEditLayer layer = new TerrainEditLayer();
        TerrainEffectiveTerrainQuery query =
            new TerrainEffectiveTerrainQuery(grid, layer);

        layer.Add(CreateExcavate(1L, 1L, new Vector3Int(10, 5, 3)));

        Check(
            query.IsExcavated(new Vector3(10.5f, 5.5f, 3.5f)),
            "Density independence: query works without DensityField"
        );
    }

    private static void ValidateNullDependencies()
    {
        Check(
            ThrowsArgumentNull(
                () => new TerrainEffectiveTerrainQuery(null, new TerrainEditLayer())),
            "Null grid throws ArgumentNullException"
        );
        Check(
            ThrowsArgumentNull(
                () => new TerrainEffectiveTerrainQuery(new TerrainEditGrid(1f), null)),
            "Null layer throws ArgumentNullException"
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
            "[TerrainModificationV2EffectiveTerrainQueryValidation] ===== SUMMARY =====\n" +
            "Checks = " + checks + "\n" +
            "Failures = " + failures + "\n" +
            "Result = " + (failures == 0 ? "PASS" : "FAIL")
        );
    }
}
