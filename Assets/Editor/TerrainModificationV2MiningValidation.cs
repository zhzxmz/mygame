using TerrainModificationV2;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Terrain Modification &amp; Mining Specification V2.0 Step 1 Mining Data Validation。
/// Step 1 尚未实现 ExcavateCell Mining API；本 Validation 只验证未来 Mining
/// Producer 将要创建的 TerrainEditRecord 数据语义。
/// </summary>
public static class TerrainModificationV2MiningValidation
{
    private static int failures;
    private static int checks;

    [MenuItem("Tools/Terrain/Validate Terrain Modification V2 Mining")]
    public static void ValidateFromMenu()
    {
        ValidateAll();
        LogSummary();

        Debug.Log(
            "[TerrainModificationV2MiningValidation] Menu validation result = " +
            (failures == 0 ? "PASS" : "FAIL")
        );
    }

    /// <summary>Batchmode 入口：-executeMethod TerrainModificationV2MiningValidation.RunBatch。</summary>
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

        ValidateMiningRecordShape();
        ValidateCellCoordinateSemantics();
        ValidateRepeatedRecords();
    }

    private static void ValidateMiningRecordShape()
    {
        TerrainEditRecord record = CreateExcavateEdit(
            1L,
            1L,
            new Vector3Int(10, 5, 3)
        );

        Check(record != null, "Mining record: record created");
        Check(
            record.Operation == TerrainEditOperation.Excavate,
            "Mining record: Operation == Excavate"
        );
        Check(
            record.CellCoordinate == new Vector3Int(10, 5, 3),
            "Mining record: CellCoordinate is authoritative geometry"
        );
        Check(
            record.EditId == 1L && record.Sequence == 1L,
            "Mining record: EditId / Sequence preserved"
        );
    }

    private static void ValidateCellCoordinateSemantics()
    {
        Vector3Int[] cells =
        {
            new Vector3Int(0, 0, 0),
            new Vector3Int(1, 0, 0),
            new Vector3Int(-1, 0, 2),
            new Vector3Int(10, 5, 3)
        };

        for (int i = 0; i < cells.Length; i++)
        {
            TerrainEditRecord record = CreateExcavateEdit(
                10L + i,
                20L + i,
                cells[i]
            );

            Check(
                record.CellCoordinate == cells[i],
                "CellCoordinate " + cells[i] + " is stored exactly"
            );
        }
    }

    private static void ValidateRepeatedRecords()
    {
        TerrainEditRecord editA = CreateExcavateEdit(
            1L,
            1L,
            new Vector3Int(10, 5, 3)
        );
        TerrainEditRecord editB = CreateExcavateEdit(
            2L,
            2L,
            new Vector3Int(10, 5, 3)
        );

        Check(
            editA.EditId != editB.EditId,
            "Repeated mining: two operations have unique EditIds"
        );
        Check(
            editA.CellCoordinate == editB.CellCoordinate,
            "Repeated mining: same CellCoordinate for same mined cell"
        );
    }

    private static TerrainEditRecord CreateExcavateEdit(
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
            "[TerrainModificationV2MiningValidation] ===== SUMMARY =====\n" +
            "Checks = " + checks + "\n" +
            "Failures = " + failures + "\n" +
            "Result = " + (failures == 0 ? "PASS" : "FAIL")
        );
    }
}
