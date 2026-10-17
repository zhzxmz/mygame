using System.Reflection;
using TerrainModificationV2;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Terrain Modification &amp; Mining Specification V2.0 Step 1 Validation。
/// 验证 TerrainEditRecord 的 Excavate + CellCoordinate 数据语义。
/// </summary>
public static class TerrainModificationV2Validation
{
    private static int failures;
    private static int checks;

    [MenuItem("Tools/Terrain/Validate Terrain Modification V2 Concepts")]
    public static void ValidateFromMenu()
    {
        ValidateAll();
        LogSummary();

        Debug.Log(
            "[TerrainModificationV2Validation] Menu validation result = " +
            (failures == 0 ? "PASS" : "FAIL")
        );
    }

    /// <summary>Batchmode 入口：-executeMethod TerrainModificationV2Validation.RunBatch。</summary>
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

        ValidateExcavateOperation();
        ValidateCellCoordinateStorage();
        ValidateRecordImmutability();
        ValidateNoShapeDependency();
    }

    private static void ValidateExcavateOperation()
    {
        Check(
            System.Enum.IsDefined(
                typeof(TerrainEditOperation),
                TerrainEditOperation.Excavate),
            "TerrainEditOperation.Excavate exists"
        );

        TerrainEditRecord record = new TerrainEditRecord(
            1L,
            1L,
            TerrainEditOperation.Excavate,
            new Vector3Int(0, 0, 0)
        );

        Check(
            record.Operation == TerrainEditOperation.Excavate,
            "Record can be created with Excavate"
        );
    }

    private static void ValidateCellCoordinateStorage()
    {
        Vector3Int[] coordinates =
        {
            new Vector3Int(0, 0, 0),
            new Vector3Int(1, 2, 3),
            new Vector3Int(-1, 0, 2)
        };

        for (int i = 0; i < coordinates.Length; i++)
        {
            TerrainEditRecord record = new TerrainEditRecord(
                10L + i,
                20L + i,
                TerrainEditOperation.Excavate,
                coordinates[i]
            );

            Check(
                record.CellCoordinate == coordinates[i],
                "CellCoordinate " + coordinates[i] + " is stored exactly"
            );
        }
    }

    private static void ValidateRecordImmutability()
    {
        TerrainEditRecord record = new TerrainEditRecord(
            42L,
            7L,
            TerrainEditOperation.Excavate,
            new Vector3Int(10, 5, 3)
        );

        Check(record.EditId == 42L, "EditId is immutable value");
        Check(record.Sequence == 7L, "Sequence is immutable value");
        Check(
            record.Operation == TerrainEditOperation.Excavate,
            "Operation is immutable value"
        );
        Check(
            record.CellCoordinate == new Vector3Int(10, 5, 3),
            "CellCoordinate is immutable value"
        );

        PropertyInfo[] properties = typeof(TerrainEditRecord).GetProperties(
            BindingFlags.Public | BindingFlags.Instance
        );

        for (int i = 0; i < properties.Length; i++)
        {
            PropertyInfo property = properties[i];
            Check(
                !property.CanWrite,
                "Property " + property.Name + " has no public setter"
            );
        }
    }

    private static void ValidateNoShapeDependency()
    {
        TerrainEditRecord record = new TerrainEditRecord(
            1L,
            1L,
            TerrainEditOperation.Excavate,
            new Vector3Int(10, 5, 3)
        );

        Check(record != null, "Record can be created without Shape argument");

        PropertyInfo shapeProperty = typeof(TerrainEditRecord).GetProperty(
            "Shape",
            BindingFlags.Public | BindingFlags.Instance
        );
        Check(
            shapeProperty == null,
            "TerrainEditRecord no longer exposes Shape"
        );

        PropertyInfo materialPolicyProperty = typeof(TerrainEditRecord).GetProperty(
            "MaterialPolicy",
            BindingFlags.Public | BindingFlags.Instance
        );
        Check(
            materialPolicyProperty == null,
            "TerrainEditRecord no longer exposes MaterialPolicy"
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
            "[TerrainModificationV2Validation] ===== SUMMARY =====\n" +
            "Checks = " + checks + "\n" +
            "Failures = " + failures + "\n" +
            "Result = " + (failures == 0 ? "PASS" : "FAIL")
        );
    }
}
