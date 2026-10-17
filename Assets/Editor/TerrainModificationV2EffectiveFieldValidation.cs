using System.Collections.Generic;
using TerrainModificationV2;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Terrain Modification &amp; Mining Specification V2.0 Step 1 Validation。
/// Step 1 尚未实现 Effective Terrain Cell Occupancy；
/// 当前只验证 Evaluator 的 Step 1 deferred 行为，避免旧 Shape/Influence 语义继续存在。
/// </summary>
public static class TerrainModificationV2EffectiveFieldValidation
{
    private static int failures;
    private static int checks;

    [MenuItem("Tools/Terrain/Validate Terrain Modification V2 Effective Field")]
    public static void ValidateFromMenu()
    {
        ValidateAll();
        LogSummary();

        Debug.Log(
            "[TerrainModificationV2EffectiveFieldValidation] Menu validation result = " +
            (failures == 0 ? "PASS" : "FAIL")
        );
    }

    /// <summary>Batchmode 入口：-executeMethod TerrainModificationV2EffectiveFieldValidation.RunBatch。</summary>
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

        ValidateDeferredPassthrough();
        ValidateAcceptsCellRecords();
    }

    private static void ValidateDeferredPassthrough()
    {
        TerrainEditRecord edit = new TerrainEditRecord(
            1L,
            1L,
            TerrainEditOperation.Excavate,
            new Vector3Int(10, 5, 3)
        );

        float effective = TerrainEffectiveFieldEvaluator.Evaluate(
            5f,
            Vector3.zero,
            new List<TerrainEditRecord> { edit }
        );

        Check(
            Mathf.Abs(effective - 5f) < 0.00001f,
            "Step 1 deferred evaluator returns baseDensity unchanged"
        );
    }

    private static void ValidateAcceptsCellRecords()
    {
        List<TerrainEditRecord> edits = new List<TerrainEditRecord>
        {
            new TerrainEditRecord(
                1L,
                1L,
                TerrainEditOperation.Excavate,
                new Vector3Int(0, 0, 0)),
            new TerrainEditRecord(
                2L,
                2L,
                TerrainEditOperation.Excavate,
                new Vector3Int(-1, 0, 2))
        };

        float effective = TerrainEffectiveFieldEvaluator.Evaluate(
            -2f,
            new Vector3(10f, 5f, 3f),
            edits
        );

        Check(
            Mathf.Abs(effective - (-2f)) < 0.00001f,
            "Step 1 evaluator accepts CellCoordinate-based records"
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
            "[TerrainModificationV2EffectiveFieldValidation] ===== SUMMARY =====\n" +
            "Checks = " + checks + "\n" +
            "Failures = " + failures + "\n" +
            "Result = " + (failures == 0 ? "PASS" : "FAIL")
        );
    }
}
