using System;
using TerrainModificationV2;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Terrain Modification &amp; Mining Specification V2.0 Step 2 Validation。
/// 验证 TerrainEditGrid 的 Position ↔ Cell / Cell ↔ Bounds 数学契约。
/// 纯逻辑测试，不依赖 Scene / GameObject / Mesh / Raycast。
/// </summary>
public static class TerrainModificationV2EditGridValidation
{
    private const float PositionEpsilon = 0.00001f;

    private static int failures;
    private static int checks;

    [MenuItem("Tools/Terrain/Validate Terrain Modification V2 Edit Grid")]
    public static void ValidateFromMenu()
    {
        ValidateAll();
        LogSummary();

        Debug.Log(
            "[TerrainModificationV2EditGridValidation] Menu validation result = " +
            (failures == 0 ? "PASS" : "FAIL")
        );
    }

    /// <summary>Batchmode 入口：-executeMethod TerrainModificationV2EditGridValidation.RunBatch。</summary>
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

        ValidateDefaultPositiveCoordinates();
        ValidateNegativeCoordinates();
        ValidateNonUnitCellSize();
        ValidateCellBounds();
        ValidateBoundsCellConsistency();
        ValidateInvalidCellSize();
    }

    private static void ValidateDefaultPositiveCoordinates()
    {
        TerrainEditGrid grid = new TerrainEditGrid(1f);

        CheckCell(grid, new Vector3(0f, 0f, 0f), new Vector3Int(0, 0, 0));
        CheckCell(grid, new Vector3(0.1f, 0.2f, 0.3f), new Vector3Int(0, 0, 0));
        CheckCell(grid, new Vector3(0.999f, 0.999f, 0.999f), new Vector3Int(0, 0, 0));
        CheckCell(grid, new Vector3(1f, 0f, 0f), new Vector3Int(1, 0, 0));
        CheckCell(grid, new Vector3(2.3f, 4.2f, 6.9f), new Vector3Int(2, 4, 6));
    }

    private static void ValidateNegativeCoordinates()
    {
        TerrainEditGrid grid = new TerrainEditGrid(1f);

        CheckCell(grid, new Vector3(-0.1f, 0f, 0f), new Vector3Int(-1, 0, 0));
        CheckCell(grid, new Vector3(-1.0f, 0f, 0f), new Vector3Int(-1, 0, 0));
        CheckCell(grid, new Vector3(-1.1f, 0f, 0f), new Vector3Int(-2, 0, 0));

        CheckCell(grid, new Vector3(0f, -0.1f, 0f), new Vector3Int(0, -1, 0));
        CheckCell(grid, new Vector3(0f, -1.0f, 0f), new Vector3Int(0, -1, 0));
        CheckCell(grid, new Vector3(0f, -1.1f, 0f), new Vector3Int(0, -2, 0));

        CheckCell(grid, new Vector3(0f, 0f, -0.1f), new Vector3Int(0, 0, -1));
        CheckCell(grid, new Vector3(0f, 0f, -1.0f), new Vector3Int(0, 0, -1));
        CheckCell(grid, new Vector3(0f, 0f, -1.1f), new Vector3Int(0, 0, -2));
    }

    private static void ValidateNonUnitCellSize()
    {
        TerrainEditGrid grid = new TerrainEditGrid(2f);

        CheckCell(grid, new Vector3(0f, 0f, 0f), new Vector3Int(0, 0, 0));
        CheckCell(grid, new Vector3(1.99f, 0f, 0f), new Vector3Int(0, 0, 0));
        CheckCell(grid, new Vector3(2f, 0f, 0f), new Vector3Int(1, 0, 0));
        CheckCell(grid, new Vector3(-0.1f, 0f, 0f), new Vector3Int(-1, 0, 0));
        CheckCell(grid, new Vector3(-2f, 0f, 0f), new Vector3Int(-1, 0, 0));
        CheckCell(grid, new Vector3(-2.1f, 0f, 0f), new Vector3Int(-2, 0, 0));
    }

    private static void ValidateCellBounds()
    {
        TerrainEditGrid grid = new TerrainEditGrid(1f);
        Vector3Int cell = new Vector3Int(2, 3, -1);

        Bounds bounds = grid.GetCellBounds(cell);

        Check(
            Vector3Distance(bounds.min, new Vector3(2f, 3f, -1f)) < PositionEpsilon,
            "Cell (2,3,-1) bounds min == (2,3,-1)"
        );
        Check(
            Vector3Distance(bounds.max, new Vector3(3f, 4f, 0f)) < PositionEpsilon,
            "Cell (2,3,-1) bounds max == (3,4,0)"
        );
        Check(
            Vector3Distance(bounds.size, new Vector3(1f, 1f, 1f)) < PositionEpsilon,
            "Cell (2,3,-1) bounds size == (1,1,1)"
        );
    }

    private static void ValidateBoundsCellConsistency()
    {
        TerrainEditGrid grid = new TerrainEditGrid(1f);

        Vector3Int[] cells =
        {
            new Vector3Int(0, 0, 0),
            new Vector3Int(1, 2, 3),
            new Vector3Int(-1, 0, 2),
            new Vector3Int(-3, -4, -5)
        };

        for (int i = 0; i < cells.Length; i++)
        {
            Vector3Int cell = cells[i];
            Vector3 min = grid.GetCellMin(cell);
            Vector3 max = grid.GetCellMax(cell);

            Vector3 inside = min + Vector3.one * 0.001f;
            Check(
                grid.PositionToCell(inside) == cell,
                "min + epsilon maps back to cell " + cell
            );

            Check(
                grid.PositionToCell(max) != cell,
                "max is not inside cell " + cell
            );

            Vector3Int expectedNext = cell + Vector3Int.one;
            Check(
                grid.PositionToCell(max) == expectedNext,
                "max maps to adjacent cell " + expectedNext
            );
        }

        TerrainEditGrid grid2 = new TerrainEditGrid(2f);
        Vector3Int cellA = new Vector3Int(-2, 1, 3);
        Vector3 minA = grid2.GetCellMin(cellA);
        Vector3 maxA = grid2.GetCellMax(cellA);

        Check(
            grid2.PositionToCell(minA + Vector3.one * 0.001f) == cellA,
            "CellSize 2: min + epsilon maps back to " + cellA
        );
        Check(
            grid2.PositionToCell(maxA) == cellA + Vector3Int.one,
            "CellSize 2: max maps to adjacent cell"
        );
    }

    private static void ValidateInvalidCellSize()
    {
        Check(
            ThrowsArgumentOutOfRange(() => new TerrainEditGrid(0f)),
            "CellSize 0 throws ArgumentOutOfRangeException"
        );
        Check(
            ThrowsArgumentOutOfRange(() => new TerrainEditGrid(-1f)),
            "CellSize negative throws ArgumentOutOfRangeException"
        );
        Check(
            ThrowsArgumentOutOfRange(() => new TerrainEditGrid(float.NaN)),
            "CellSize NaN throws ArgumentOutOfRangeException"
        );
    }

    private static void CheckCell(
        TerrainEditGrid grid,
        Vector3 position,
        Vector3Int expected)
    {
        Check(
            grid.PositionToCell(position) == expected,
            "PositionToCell(" + position + ") == " + expected
        );
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

    private static float Vector3Distance(Vector3 a, Vector3 b)
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
            "[TerrainModificationV2EditGridValidation] ===== SUMMARY =====\n" +
            "Checks = " + checks + "\n" +
            "Failures = " + failures + "\n" +
            "Result = " + (failures == 0 ? "PASS" : "FAIL")
        );
    }
}
