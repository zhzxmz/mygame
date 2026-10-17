using System;
using TerrainModificationV2;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Voxel Terrain 重构 Step 1 Validation。
/// 验证 VoxelField 的可修改体素数据契约。
/// 纯 C#，不创建 Scene / DensityField / Mesh / Raycast。
/// </summary>
public static class TerrainModificationV2VoxelFieldValidation
{
    private const float Epsilon = 0.00001f;

    private static int failures;
    private static int checks;

    [MenuItem("Tools/Terrain/Validate Voxel Field")]
    public static void ValidateFromMenu()
    {
        ValidateAll();
        LogSummary();

        Debug.Log(
            "[TerrainModificationV2VoxelFieldValidation] Menu validation result = " +
            (failures == 0 ? "PASS" : "FAIL")
        );
    }

    /// <summary>Batchmode 入口：-executeMethod TerrainModificationV2VoxelFieldValidation.RunBatch。</summary>
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

        ValidateInitialization();
        ValidateSetGet();
        ValidateSingleVoxelLogicalUnit();
        ValidatePositionToVoxel();
        ValidateBounds();
        ValidateMultipleVoxels();
        ValidateBoundary();
        ValidateSparseStorage();
        ValidateInvalidVoxelSize();
    }

    private static void ValidateInitialization()
    {
        VoxelField field = new VoxelField(1f);

        Check(
            field.Get(new Vector3Int(0, 0, 0)) == VoxelState.Solid,
            "Initialization: default state is Solid"
        );
        Check(
            field.IsSolid(new Vector3Int(5, 2, -3)),
            "Initialization: arbitrary unrecorded voxel is Solid"
        );
        Check(
            field.StoredVoxelCount == 0,
            "Initialization: sparse storage starts empty"
        );
    }

    private static void ValidateSetGet()
    {
        VoxelField field = new VoxelField(1f);

        field.Set(new Vector3Int(0, 0, 0), VoxelState.Air);

        Check(
            field.Get(new Vector3Int(0, 0, 0)) == VoxelState.Air,
            "Set/Get: voxel becomes Air"
        );
        Check(
            field.IsAir(new Vector3Int(0, 0, 0)),
            "Set/Get: IsAir returns true"
        );
        Check(
            !field.IsSolid(new Vector3Int(0, 0, 0)),
            "Set/Get: IsSolid returns false"
        );

        field.Set(new Vector3Int(0, 0, 0), VoxelState.Solid);

        Check(
            field.Get(new Vector3Int(0, 0, 0)) == VoxelState.Solid,
            "Set/Get: voxel can return to Solid"
        );
    }

    private static void ValidateSingleVoxelLogicalUnit()
    {
        VoxelField field = new VoxelField(1f);
        field.Set(new Vector3Int(0, 0, 0), VoxelState.Air);

        Vector3 insideA = new Vector3(0.25f, 0.25f, 0.25f);
        Vector3 insideB = new Vector3(0.5f, 0.5f, 0.5f);
        Vector3 insideC = new Vector3(0.999f, 0.999f, 0.999f);

        Check(
            field.Get(field.PositionToVoxel(insideA)) == VoxelState.Air,
            "Logical unit: position 0.25 belongs to excavated voxel"
        );
        Check(
            field.Get(field.PositionToVoxel(insideB)) == VoxelState.Air,
            "Logical unit: position 0.5 belongs to excavated voxel"
        );
        Check(
            field.Get(field.PositionToVoxel(insideC)) == VoxelState.Air,
            "Logical unit: position 0.999 belongs to excavated voxel"
        );
    }

    private static void ValidatePositionToVoxel()
    {
        VoxelField field = new VoxelField(1f);

        CheckVoxel(field, new Vector3(0f, 0f, 0f), new Vector3Int(0, 0, 0));
        CheckVoxel(field, new Vector3(0.5f, 0.5f, 0.5f), new Vector3Int(0, 0, 0));
        CheckVoxel(field, new Vector3(0.999f, 0f, 0f), new Vector3Int(0, 0, 0));
        CheckVoxel(field, new Vector3(1f, 0f, 0f), new Vector3Int(1, 0, 0));
        CheckVoxel(field, new Vector3(1.5f, 0f, 0f), new Vector3Int(1, 0, 0));
        CheckVoxel(field, new Vector3(-0.1f, 0f, 0f), new Vector3Int(-1, 0, 0));
        CheckVoxel(field, new Vector3(-1f, 0f, 0f), new Vector3Int(-1, 0, 0));
        CheckVoxel(field, new Vector3(-1.1f, 0f, 0f), new Vector3Int(-2, 0, 0));
    }

    private static void ValidateBounds()
    {
        VoxelField field = new VoxelField(1f);

        Bounds boundsA = field.GetVoxelBounds(new Vector3Int(0, 0, 0));
        Check(
            VectorDistance(boundsA.min, new Vector3(0f, 0f, 0f)) < Epsilon,
            "Bounds: Voxel(0,0,0) min == (0,0,0)"
        );
        Check(
            VectorDistance(boundsA.max, new Vector3(1f, 1f, 1f)) < Epsilon,
            "Bounds: Voxel(0,0,0) max == (1,1,1)"
        );
        Check(
            VectorDistance(boundsA.size, new Vector3(1f, 1f, 1f)) < Epsilon,
            "Bounds: Voxel(0,0,0) size == (1,1,1)"
        );

        Bounds boundsB = field.GetVoxelBounds(new Vector3Int(-1, 2, 3));
        Check(
            VectorDistance(boundsB.min, new Vector3(-1f, 2f, 3f)) < Epsilon,
            "Bounds: Voxel(-1,2,3) min == (-1,2,3)"
        );
        Check(
            VectorDistance(boundsB.max, new Vector3(0f, 3f, 4f)) < Epsilon,
            "Bounds: Voxel(-1,2,3) max == (0,3,4)"
        );
    }

    private static void ValidateMultipleVoxels()
    {
        VoxelField field = new VoxelField(1f);

        field.Set(new Vector3Int(0, 0, 0), VoxelState.Air);
        field.Set(new Vector3Int(1, 0, 0), VoxelState.Air);
        field.Set(new Vector3Int(2, 0, 0), VoxelState.Air);

        Check(field.IsAir(new Vector3Int(0, 0, 0)), "Multiple: voxel 0 is Air");
        Check(field.IsAir(new Vector3Int(1, 0, 0)), "Multiple: voxel 1 is Air");
        Check(field.IsAir(new Vector3Int(2, 0, 0)), "Multiple: voxel 2 is Air");
        Check(field.IsSolid(new Vector3Int(3, 0, 0)), "Multiple: voxel 3 remains Solid");

        field.Set(new Vector3Int(1, 0, 0), VoxelState.Solid);

        Check(field.IsAir(new Vector3Int(0, 0, 0)), "Multiple: voxel 0 unaffected");
        Check(field.IsSolid(new Vector3Int(1, 0, 0)), "Multiple: voxel 1 returns to Solid");
        Check(field.IsAir(new Vector3Int(2, 0, 0)), "Multiple: voxel 2 unaffected");
    }

    private static void ValidateBoundary()
    {
        VoxelField field = new VoxelField(1f);

        Check(
            field.PositionToVoxel(new Vector3(1f, 0f, 0f)) ==
            new Vector3Int(1, 0, 0),
            "Boundary: x=1 belongs to Voxel(1,0,0)"
        );
        Check(
            field.PositionToVoxel(new Vector3(0.999f, 0f, 0f)) ==
            new Vector3Int(0, 0, 0),
            "Boundary: x=0.999 belongs to Voxel(0,0,0)"
        );

        VoxelField field2 = new VoxelField(2f);

        Check(
            field2.PositionToVoxel(new Vector3(2f, 0f, 0f)) ==
            new Vector3Int(1, 0, 0),
            "Boundary: VoxelSize=2, x=2 belongs to Voxel(1,0,0)"
        );
        Check(
            field2.PositionToVoxel(new Vector3(-0.1f, 0f, 0f)) ==
            new Vector3Int(-1, 0, 0),
            "Boundary: VoxelSize=2, x=-0.1 belongs to Voxel(-1,0,0)"
        );
    }

    private static void ValidateSparseStorage()
    {
        VoxelField field = new VoxelField(1f, VoxelState.Solid);

        field.Set(new Vector3Int(0, 0, 0), VoxelState.Air);
        field.Set(new Vector3Int(1, 0, 0), VoxelState.Air);

        Check(
            field.StoredVoxelCount == 2,
            "Sparse storage: two non-default voxels are stored"
        );

        field.Set(new Vector3Int(1, 0, 0), VoxelState.Solid);

        Check(
            field.StoredVoxelCount == 1,
            "Sparse storage: returning to default removes entry"
        );

        VoxelField airDefault = new VoxelField(1f, VoxelState.Air);
        airDefault.Set(new Vector3Int(0, 0, 0), VoxelState.Solid);

        Check(
            airDefault.Get(new Vector3Int(0, 0, 0)) == VoxelState.Solid,
            "Sparse storage: Air default supports explicit Solid voxel"
        );
        Check(
            airDefault.Get(new Vector3Int(5, 5, 5)) == VoxelState.Air,
            "Sparse storage: unrecorded voxel returns Air default"
        );
    }

    private static void ValidateInvalidVoxelSize()
    {
        Check(
            ThrowsArgumentOutOfRange(() => new VoxelField(0f)),
            "VoxelSize 0 throws ArgumentOutOfRangeException"
        );
        Check(
            ThrowsArgumentOutOfRange(() => new VoxelField(-1f)),
            "VoxelSize negative throws ArgumentOutOfRangeException"
        );
        Check(
            ThrowsArgumentOutOfRange(() => new VoxelField(float.NaN)),
            "VoxelSize NaN throws ArgumentOutOfRangeException"
        );
        Check(
            ThrowsArgumentOutOfRange(() => new VoxelField(float.PositiveInfinity)),
            "VoxelSize Infinity throws ArgumentOutOfRangeException"
        );
    }

    private static void CheckVoxel(
        VoxelField field,
        Vector3 position,
        Vector3Int expected)
    {
        Check(
            field.PositionToVoxel(position) == expected,
            "PositionToVoxel(" + position + ") == " + expected
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
            "[TerrainModificationV2VoxelFieldValidation] ===== SUMMARY =====\n" +
            "Checks = " + checks + "\n" +
            "Failures = " + failures + "\n" +
            "Result = " + (failures == 0 ? "PASS" : "FAIL")
        );
    }
}
