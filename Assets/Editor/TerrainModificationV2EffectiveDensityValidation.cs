using TerrainModificationV2;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Terrain Modification &amp; Mining Specification V2.0 Step 3D Validation。
/// 验证 Excavated Cell State → Effective Density 的最小实验模型。
/// 纯 C#，不创建 Scene / DensityField / Mesh / Raycast。
/// </summary>
public static class TerrainModificationV2EffectiveDensityValidation
{
    private const float Epsilon = 0.00001f;

    private static int failures;
    private static int checks;

    [MenuItem("Tools/Terrain/Validate Terrain Modification V2 Effective Density")]
    public static void ValidateFromMenu()
    {
        ValidateAll();
        LogSummary();

        Debug.Log(
            "[TerrainModificationV2EffectiveDensityValidation] Menu validation result = " +
            (failures == 0 ? "PASS" : "FAIL")
        );
    }

    /// <summary>Batchmode 入口：-executeMethod TerrainModificationV2EffectiveDensityValidation.RunBatch。</summary>
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

        ValidateNoExcavation();
        ValidateExcavatedCellAir();
        ValidateSolidInsideCellBecomesAir();
        ValidateCellBoundary();
        ValidateNegativeCell();
        ValidateSamplingRegionDoesNotAffectDensity();
        ValidateAdjacentCells();
        ValidateDuplicateRecordsIdempotent();
    }

    private static TerrainEffectiveDensityEvaluator CreateEvaluator(
        TerrainEditLayer layer)
    {
        TerrainEditGrid grid = new TerrainEditGrid(1f);
        TerrainEffectiveTerrainQuery query =
            new TerrainEffectiveTerrainQuery(grid, layer);

        return new TerrainEffectiveDensityEvaluator(query);
    }

    private static void ValidateNoExcavation()
    {
        TerrainEffectiveDensityEvaluator evaluator =
            CreateEvaluator(new TerrainEditLayer());

        CheckDensity(evaluator, new Vector3(0f, -1f, 0f), 1.5f,
            "No excavation: y=-1 → BaseDensity 1.5");
        CheckDensity(evaluator, new Vector3(0f, 0f, 0f), 0.5f,
            "No excavation: y=0 → BaseDensity 0.5");
        CheckDensity(evaluator, new Vector3(0f, 0.5f, 0f), 0f,
            "No excavation: y=0.5 → BaseDensity 0");
        CheckDensity(evaluator, new Vector3(0f, 1f, 0f), -0.5f,
            "No excavation: y=1 → BaseDensity -0.5");
    }

    private static void ValidateExcavatedCellAir()
    {
        TerrainEditLayer layer = new TerrainEditLayer();
        layer.Add(CreateExcavate(1L, 1L, new Vector3Int(0, 0, 0)));

        TerrainEffectiveDensityEvaluator evaluator = CreateEvaluator(layer);

        Vector3 position = new Vector3(0.5f, 0.5f, 0.5f);
        Check(
            evaluator.EvaluateBaseDensity(position) == 0f,
            "Excavated cell: base density at (0.5,0.5,0.5) is 0"
        );
        Check(
            evaluator.Evaluate(position) <= 0f,
            "Excavated cell: effective density <= 0"
        );
        CheckDensity(
            evaluator,
            position,
            TerrainEffectiveDensityEvaluator.ExcavatedDensity,
            "Excavated cell: effective density == experimental Air density"
        );
    }

    private static void ValidateSolidInsideCellBecomesAir()
    {
        TerrainEditLayer layer = new TerrainEditLayer();
        layer.Add(CreateExcavate(1L, 1L, new Vector3Int(0, 0, 0)));

        TerrainEffectiveDensityEvaluator evaluator = CreateEvaluator(layer);

        Vector3 solidPosition = new Vector3(0.5f, 0.25f, 0.5f);

        Check(
            evaluator.EvaluateBaseDensity(solidPosition) > 0f,
            "Solid point: base density > 0 before excavation"
        );
        CheckDensity(
            evaluator,
            solidPosition,
            TerrainEffectiveDensityEvaluator.ExcavatedDensity,
            "Solid point: solid Base becomes Air inside excavated cell"
        );
    }

    private static void ValidateCellBoundary()
    {
        TerrainEditLayer layer = new TerrainEditLayer();
        layer.Add(CreateExcavate(1L, 1L, new Vector3Int(0, 0, 0)));

        TerrainEffectiveDensityEvaluator evaluator = CreateEvaluator(layer);

        float y = 0.25f;
        float z = 0.5f;

        // 只有 Cell(0,0,0) 被挖：[0,1) 属于 Excavated。
        CheckDensity(
            evaluator,
            new Vector3(-1f, y, z),
            0.5f - y,
            "Boundary x=-1: Cell(-1,0,0) is Base");
        CheckDensity(
            evaluator,
            new Vector3(-0.5f, y, z),
            0.5f - y,
            "Boundary x=-0.5: Cell(-1,0,0) is Base");
        CheckDensity(
            evaluator,
            new Vector3(0f, y, z),
            TerrainEffectiveDensityEvaluator.ExcavatedDensity,
            "Boundary x=0: Cell(0,0,0) is Excavated");
        CheckDensity(
            evaluator,
            new Vector3(0.5f, y, z),
            TerrainEffectiveDensityEvaluator.ExcavatedDensity,
            "Boundary x=0.5: Cell(0,0,0) is Excavated");
        CheckDensity(
            evaluator,
            new Vector3(1f, y, z),
            0.5f - y,
            "Boundary x=1: Cell(1,0,0) is Base");
        CheckDensity(
            evaluator,
            new Vector3(1.5f, y, z),
            0.5f - y,
            "Boundary x=1.5: Cell(1,0,0) is Base");
    }

    private static void ValidateNegativeCell()
    {
        TerrainEditLayer layer = new TerrainEditLayer();
        layer.Add(CreateExcavate(1L, 1L, new Vector3Int(-1, 0, 0)));

        TerrainEffectiveDensityEvaluator evaluator = CreateEvaluator(layer);

        float y = 0.25f;
        float z = 0.5f;

        CheckDensity(
            evaluator,
            new Vector3(-0.1f, y, z),
            TerrainEffectiveDensityEvaluator.ExcavatedDensity,
            "Negative: -0.1 belongs to Cell(-1,0,0) and is Excavated");
        CheckDensity(
            evaluator,
            new Vector3(-1.0f, y, z),
            TerrainEffectiveDensityEvaluator.ExcavatedDensity,
            "Negative: -1.0 belongs to Cell(-1,0,0) and is Excavated");
        CheckDensity(
            evaluator,
            new Vector3(-1.1f, y, z),
            0.5f - y,
            "Negative: -1.1 belongs to Cell(-2,0,0) and is Base");
    }

    private static void ValidateSamplingRegionDoesNotAffectDensity()
    {
        TerrainEditLayer layer = new TerrainEditLayer();
        layer.Add(CreateExcavate(1L, 1L, new Vector3Int(0, 0, 0)));

        TerrainEffectiveDensityEvaluator evaluator = CreateEvaluator(layer);

        float y = 0.25f;
        float z = 0.5f;

        CheckDensity(
            evaluator,
            new Vector3(-0.5f, y, z),
            0.5f - y,
            "Sampling region: x=-0.5 remains Base even though it is inside sampling region");
        CheckDensity(
            evaluator,
            new Vector3(1.0f, y, z),
            0.5f - y,
            "Sampling region: x=1 remains Base even though it is inside sampling region");
        CheckDensity(
            evaluator,
            new Vector3(1.5f, y, z),
            0.5f - y,
            "Sampling region: x=1.5 remains Base even though it is inside sampling region");
    }

    private static void ValidateAdjacentCells()
    {
        TerrainEditLayer layer = new TerrainEditLayer();
        layer.Add(CreateExcavate(1L, 1L, new Vector3Int(0, 0, 0)));
        layer.Add(CreateExcavate(2L, 2L, new Vector3Int(1, 0, 0)));

        TerrainEffectiveDensityEvaluator evaluator = CreateEvaluator(layer);

        float y = 0.25f;
        float z = 0.5f;

        CheckDensity(
            evaluator,
            new Vector3(0.5f, y, z),
            TerrainEffectiveDensityEvaluator.ExcavatedDensity,
            "Adjacent cells: x=0.5 is Excavated");
        CheckDensity(
            evaluator,
            new Vector3(1.0f, y, z),
            TerrainEffectiveDensityEvaluator.ExcavatedDensity,
            "Adjacent cells: x=1.0 belongs to Cell(1,0,0) and is Excavated");
        CheckDensity(
            evaluator,
            new Vector3(1.5f, y, z),
            TerrainEffectiveDensityEvaluator.ExcavatedDensity,
            "Adjacent cells: x=1.5 is Excavated");
        CheckDensity(
            evaluator,
            new Vector3(2.0f, y, z),
            0.5f - y,
            "Adjacent cells: x=2.0 belongs to Cell(2,0,0) and is Base");
    }

    private static void ValidateDuplicateRecordsIdempotent()
    {
        TerrainEditLayer layer = new TerrainEditLayer();
        layer.Add(CreateExcavate(1L, 1L, new Vector3Int(0, 0, 0)));
        layer.Add(CreateExcavate(2L, 2L, new Vector3Int(0, 0, 0)));
        layer.Add(CreateExcavate(3L, 3L, new Vector3Int(0, 0, 0)));

        TerrainEffectiveDensityEvaluator evaluator = CreateEvaluator(layer);

        CheckDensity(
            evaluator,
            new Vector3(0.5f, 0.5f, 0.25f),
            TerrainEffectiveDensityEvaluator.ExcavatedDensity,
            "Duplicate records: repeated excavation remains idempotent");
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

    private static void CheckDensity(
        TerrainEffectiveDensityEvaluator evaluator,
        Vector3 position,
        float expected,
        string message)
    {
        Check(
            Mathf.Abs(evaluator.Evaluate(position) - expected) < Epsilon,
            message
        );
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
            "[TerrainModificationV2EffectiveDensityValidation] ===== SUMMARY =====\n" +
            "Checks = " + checks + "\n" +
            "Failures = " + failures + "\n" +
            "Result = " + (failures == 0 ? "PASS" : "FAIL")
        );
    }
}
