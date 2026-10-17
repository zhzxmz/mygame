using System.Collections.Generic;
using TerrainModificationV2;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Terrain Modification &amp; Mining Specification V2.0 Step 1 Edit Layer Validation。
/// Step 1 Layer 只保存 Cell Record；Cell Spatial Query 尚未实现。
/// </summary>
public static class TerrainModificationV2EditLayerValidation
{
    private static int failures;
    private static int checks;

    [MenuItem("Tools/Terrain/Validate Terrain Modification V2 Edit Layer")]
    public static void ValidateFromMenu()
    {
        ValidateAll();
        LogSummary();

        Debug.Log(
            "[TerrainModificationV2EditLayerValidation] Menu validation result = " +
            (failures == 0 ? "PASS" : "FAIL")
        );
    }

    /// <summary>Batchmode 入口：-executeMethod TerrainModificationV2EditLayerValidation.RunBatch。</summary>
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

        ValidateAddAndQuery();
        ValidateEditIdUniqueness();
        ValidateMultipleEdits();
        ValidateQueryDoesNotModifyEdit();
        ValidateQuerySequenceOrder();
        ValidateNextSequence();
        ValidateAddRejectsInvalidInput();
    }

    private static void ValidateAddAndQuery()
    {
        TerrainEditLayer layer = new TerrainEditLayer();
        TerrainEditRecord edit = CreateEdit(
            1L,
            10L,
            new Vector3Int(0, 0, 0)
        );

        Check(layer.Add(edit), "Add: valid Edit is accepted");
        Check(layer.Count == 1, "Add: layer contains one Edit");

        List<TerrainEditRecord> result = layer.QueryEdits(
            new Bounds(Vector3.zero, Vector3.one)
        );

        Check(result.Count == 1, "Query: returns stored Edit");
        Check(
            result.Count == 1 && ReferenceEquals(result[0], edit),
            "Query: returns the same authoritative Record reference"
        );
    }

    private static void ValidateEditIdUniqueness()
    {
        TerrainEditLayer layer = new TerrainEditLayer();

        TerrainEditRecord editA = CreateEdit(42L, 1L, new Vector3Int(0, 0, 0));
        TerrainEditRecord editB = CreateEdit(42L, 2L, new Vector3Int(1, 2, 3));

        Check(layer.Add(editA), "Duplicate EditId: first Edit accepted");
        Check(!layer.Add(editB), "Duplicate EditId: second same EditId rejected");
        Check(layer.Count == 1, "Duplicate EditId: layer still contains one Record");

        List<TerrainEditRecord> all = layer.QueryEdits(
            new Bounds(Vector3.zero, Vector3.one * 10000f)
        );

        Check(all.Count == 1, "Duplicate EditId: query returns one Record");
        Check(
            all.Count == 1 && ReferenceEquals(all[0], editA),
            "Duplicate EditId: first authoritative Record is preserved"
        );
    }

    private static void ValidateMultipleEdits()
    {
        TerrainEditLayer layer = new TerrainEditLayer();

        Check(layer.Add(CreateEdit(1L, 1L, new Vector3Int(0, 0, 0))),
            "Multiple: EditA accepted");
        Check(layer.Add(CreateEdit(2L, 2L, new Vector3Int(1, 0, 0))),
            "Multiple: EditB accepted");
        Check(layer.Add(CreateEdit(3L, 3L, new Vector3Int(-1, 0, 2))),
            "Multiple: EditC accepted");
        Check(layer.Count == 3, "Multiple: layer contains three Edits");

        List<TerrainEditRecord> all = layer.QueryEdits(
            new Bounds(Vector3.zero, Vector3.one * 10000f)
        );
        Check(all.Count == 3, "Multiple: query returns all three Edits");
    }

    private static void ValidateQueryDoesNotModifyEdit()
    {
        TerrainEditLayer layer = new TerrainEditLayer();
        TerrainEditRecord edit = new TerrainEditRecord(
            9L,
            1L,
            TerrainEditOperation.Excavate,
            new Vector3Int(10, 5, 3)
        );

        layer.Add(edit);

        List<TerrainEditRecord> result = layer.QueryEdits(
            new Bounds(Vector3.zero, Vector3.one)
        );

        Check(result.Count == 1, "Query immutability: query returns Edit");
        Check(
            result.Count == 1 && ReferenceEquals(result[0], edit),
            "Query immutability: same Record reference"
        );
        Check(
            edit.EditId == 9L &&
            edit.Sequence == 1L &&
            edit.Operation == TerrainEditOperation.Excavate &&
            edit.CellCoordinate == new Vector3Int(10, 5, 3),
            "Query immutability: Record fields unchanged after query"
        );
    }

    private static void ValidateQuerySequenceOrder()
    {
        TerrainEditLayer layer = new TerrainEditLayer();

        layer.Add(CreateEdit(30L, 30L, new Vector3Int(0, 0, 0)));
        layer.Add(CreateEdit(10L, 10L, new Vector3Int(1, 0, 0)));
        layer.Add(CreateEdit(20L, 20L, new Vector3Int(2, 0, 0)));

        List<TerrainEditRecord> result = layer.QueryEdits(
            new Bounds(Vector3.zero, Vector3.one * 10000f)
        );

        Check(result.Count == 3, "Sequence: query returns all Edits");
        Check(
            result.Count == 3 &&
            result[0].Sequence == 10L &&
            result[1].Sequence == 20L &&
            result[2].Sequence == 30L,
            "Sequence: result is sorted by Sequence ascending (10, 20, 30)"
        );
    }

    private static void ValidateNextSequence()
    {
        TerrainEditLayer layer = new TerrainEditLayer();

        Check(layer.NextSequence() == 1L, "NextSequence: first value is 1");
        Check(layer.NextSequence() == 2L, "NextSequence: second value is 2");
        Check(layer.NextSequence() == 3L, "NextSequence: third value is 3");
    }

    private static void ValidateAddRejectsInvalidInput()
    {
        TerrainEditLayer layer = new TerrainEditLayer();

        Check(!layer.Add(null), "Add rejects null Edit");
        Check(layer.Count == 0, "Add null: layer remains empty");
    }

    private static TerrainEditRecord CreateEdit(
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
            "[TerrainModificationV2EditLayerValidation] ===== SUMMARY =====\n" +
            "Checks = " + checks + "\n" +
            "Failures = " + failures + "\n" +
            "Result = " + (failures == 0 ? "PASS" : "FAIL")
        );
    }
}
