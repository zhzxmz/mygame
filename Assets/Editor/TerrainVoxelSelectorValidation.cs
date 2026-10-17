using System;
using TerrainModificationV2;
using UnityEditor;
using UnityEngine;

/// <summary>
/// TerrainVoxelSelector Validation（Step 3A）。
/// 只验证 Raycast Hit Point → Terrain Local → Voxel Coordinate 的选择逻辑，
/// 不修改 VoxelField，不挖掘，不重建 Mesh，不修改正式 Terrain Transform。
/// </summary>
public static class TerrainVoxelSelectorValidation
{
    private const float Epsilon = 0.00001f;

    private static int failures;
    private static int checks;

    [MenuItem("Tools/Terrain/Validate Terrain Voxel Selector")]
    public static void ValidateFromMenu()
    {
        ValidateAll();
        LogSummary();

        Debug.Log(
            "[TerrainVoxelSelectorValidation] Menu validation result = " +
            (failures == 0 ? "PASS" : "FAIL")
        );
    }

    /// <summary>Batchmode 入口：-executeMethod TerrainVoxelSelectorValidation.RunBatch。</summary>
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

        ValidateTerrainLocalBoundaryCases();
        ValidateThreeAxisBoundaries();
        ValidateSelectionIsReadOnly();
        ValidateNullArguments();
        ValidateWorldToTerrainLocalWithNonZeroOrigin();
    }

    // ------------------------------------------------------------------
    // Case 1-6：VoxelField 的 floor / half-open 边界语义
    // ------------------------------------------------------------------
    private static void ValidateTerrainLocalBoundaryCases()
    {
        VoxelField voxelField = new VoxelField(1f);

        CheckVoxel(voxelField, new Vector3(0.5f, 0.5f, 0.5f), new Vector3Int(0, 0, 0), "Case 1: Voxel center (0.5,0.5,0.5) -> (0,0,0)");
        CheckVoxel(voxelField, new Vector3(0.9999f, 0.5f, 0.5f), new Vector3Int(0, 0, 0), "Case 2: Positive boundary minus epsilon (0.9999,...) -> (0,0,0)");
        CheckVoxel(voxelField, new Vector3(1.0f, 0.5f, 0.5f), new Vector3Int(1, 0, 0), "Case 3: Positive boundary x=1.0 -> Voxel x=1");
        CheckVoxel(voxelField, new Vector3(-0.1f, 0.5f, 0.5f), new Vector3Int(-1, 0, 0), "Case 4: Negative coordinate x=-0.1 -> Voxel x=-1");
        CheckVoxel(voxelField, new Vector3(-1.0f, 0.5f, 0.5f), new Vector3Int(-1, 0, 0), "Case 5: Negative boundary x=-1.0 -> Voxel x=-1");
        CheckVoxel(voxelField, new Vector3(-1.0001f, 0.5f, 0.5f), new Vector3Int(-2, 0, 0), "Case 6: Negative boundary outside x=-1.0001 -> Voxel x=-2");
        CheckVoxel(voxelField, new Vector3(0.0f, 0.0f, 0.0f), new Vector3Int(0, 0, 0), "Origin (0,0,0) -> Voxel (0,0,0)");
        CheckVoxel(voxelField, new Vector3(0.9999f, 1.0f, 0.5f), new Vector3Int(0, 1, 0), "Y axis follows same rule: y=1.0 -> Voxel y=1");
        CheckVoxel(voxelField, new Vector3(0.5f, -1.0001f, -1.0f), new Vector3Int(0, -2, -1), "Y/Z negative axes follow same rule");
    }

    // ------------------------------------------------------------------
    // Case 7：三轴边界
    // ------------------------------------------------------------------
    private static void ValidateThreeAxisBoundaries()
    {
        VoxelField voxelField = new VoxelField(1f);

        CheckVoxel(voxelField, new Vector3(1f, 1f, 1f), new Vector3Int(1, 1, 1), "Case 7a: (1,1,1) -> (1,1,1) on all three axes");
        CheckVoxel(voxelField, new Vector3(-1f, -1f, -1f), new Vector3Int(-1, -1, -1), "Case 7b: (-1,-1,-1) -> (-1,-1,-1) on all three axes");
        CheckVoxel(voxelField, new Vector3(0.9999f, 0.9999f, 0.9999f), new Vector3Int(0, 0, 0), "Three axes just before +1 all stay in Voxel 0");
        CheckVoxel(voxelField, new Vector3(-0.0001f, -0.0001f, -0.0001f), new Vector3Int(-1, -1, -1), "Three axes just below 0 all floor to Voxel -1");
    }

    private static void ValidateSelectionIsReadOnly()
    {
        VoxelField voxelField = new VoxelField(1f);
        voxelField.Set(new Vector3Int(3, 4, 5), VoxelState.Air);
        int storedBefore = voxelField.StoredVoxelCount;

        TerrainVoxelSelector.SelectVoxel(new Vector3(0.5f, 0.5f, 0.5f), voxelField);
        TerrainVoxelSelector.SelectVoxel(new Vector3(1.0f, -1.0f, 7.25f), voxelField);

        Check(voxelField.StoredVoxelCount == storedBefore, "Selector never modifies VoxelField storage");
        Check(voxelField.Get(new Vector3Int(3, 4, 5)) == VoxelState.Air, "Selector never changes existing Voxel state");
    }

    private static void ValidateNullArguments()
    {
        VoxelField voxelField = new VoxelField(1f);

        Check(
            !TerrainVoxelSelector.TrySelectVoxel(
                default(RaycastHit),
                null,
                voxelField,
                out _),
            "Null DensityTerrain -> TrySelectVoxel returns false"
        );

        Check(
            !TerrainVoxelSelector.TrySelectVoxel(
                default(RaycastHit),
                null,
                null,
                out _),
            "Null terrain + null VoxelField -> TrySelectVoxel returns false"
        );

        bool throwsForNullField = false;
        try
        {
            TerrainVoxelSelector.SelectVoxel(Vector3.zero, null);
        }
        catch (ArgumentNullException)
        {
            throwsForNullField = true;
        }

        Check(throwsForNullField, "SelectVoxel(null VoxelField) throws ArgumentNullException");
    }

    // ------------------------------------------------------------------
    // Terrain 非零 World Origin 测试。
    // 只创建临时 GameObject / 临时 DensityTerrain，不触碰正式 Terrain Transform。
    // ------------------------------------------------------------------
    private static void ValidateWorldToTerrainLocalWithNonZeroOrigin()
    {
        GameObject terrainObject = new GameObject("TerrainVoxelSelectorValidationTerrain");
        terrainObject.SetActive(false);

        try
        {
            DensityTerrain terrain = terrainObject.AddComponent<DensityTerrain>();
            terrainObject.transform.position = new Vector3(100f, 50f, -30f);
            terrainObject.transform.rotation = Quaternion.identity;
            terrainObject.transform.localScale = Vector3.one;

            // 测试 VoxelField 使用 VoxelSize = 1；临时 Terrain 的 EffectiveVoxelSize 也设为 1，
            // 以便与现有 DensityTerrain.WorldToVoxel 做一致性交叉验证。
            terrain.voxelSize = 1f;
            VoxelField voxelField = new VoxelField(1f);

            CheckWorldCase(
                terrain,
                voxelField,
                new Vector3(100.5f, 50.5f, -29.5f),
                new Vector3(0.5f, 0.5f, 0.5f),
                new Vector3Int(0, 0, 0),
                "World origin (100,50,-30): world (100.5,50.5,-29.5) -> Voxel (0,0,0)"
            );

            CheckWorldCase(
                terrain,
                voxelField,
                new Vector3(100.9999f, 50.5f, -29.5f),
                new Vector3(0.9999f, 0.5f, 0.5f),
                new Vector3Int(0, 0, 0),
                "Non-zero origin: just before local x=1 still Voxel (0,0,0)"
            );

            CheckWorldCase(
                terrain,
                voxelField,
                new Vector3(101f, 50.5f, -29.5f),
                new Vector3(1f, 0.5f, 0.5f),
                new Vector3Int(1, 0, 0),
                "Non-zero origin: local x=1 -> Voxel (1,0,0)"
            );

            CheckWorldCase(
                terrain,
                voxelField,
                new Vector3(99.9f, 50.5f, -29.5f),
                new Vector3(-0.1f, 0.5f, 0.5f),
                new Vector3Int(-1, 0, 0),
                "Non-zero origin: local x=-0.1 -> Voxel (-1,0,0)"
            );

            CheckWorldCase(
                terrain,
                voxelField,
                new Vector3(99f, 50.5f, -29.5f),
                new Vector3(-1f, 0.5f, 0.5f),
                new Vector3Int(-1, 0, 0),
                "Non-zero origin: local x=-1.0 -> Voxel (-1,0,0)"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    private static void CheckWorldCase(
        DensityTerrain terrain,
        VoxelField voxelField,
        Vector3 worldPoint,
        Vector3 expectedTerrainLocal,
        Vector3Int expectedVoxel,
        string label)
    {
        RaycastHit hit = default(RaycastHit);
        hit.point = worldPoint;

        bool selected = TerrainVoxelSelector.TrySelectVoxel(
            hit,
            terrain,
            voxelField,
            out Vector3Int selectedVoxel,
            out Vector3 selectedTerrainLocal
        );

        Check(selected, label + ": selection succeeded");
        Check(
            selectedTerrainLocal == expectedTerrainLocal ||
            Vector3.Distance(selectedTerrainLocal, expectedTerrainLocal) < Epsilon,
            label + ": Terrain Local point matches expected"
        );
        Check(selectedVoxel == expectedVoxel, label + ": Voxel coordinate matches expected");

        // 与现有 DensityTerrain 坐标 API 交叉验证：Selector 没有创造第二套坐标系统。
        Check(
            terrain.WorldToVoxel(worldPoint) == expectedVoxel,
            label + ": DensityTerrain.WorldToVoxel agrees with Selector"
        );

        Check(
            Vector3.Distance(terrain.WorldToLocalPosition(worldPoint), expectedTerrainLocal) < Epsilon,
            label + ": DensityTerrain.WorldToLocalPosition agrees with Selector"
        );
    }

    private static void CheckVoxel(
        VoxelField voxelField,
        Vector3 terrainLocalPoint,
        Vector3Int expectedVoxel,
        string label)
    {
        Vector3Int selected = TerrainVoxelSelector.SelectVoxel(
            terrainLocalPoint,
            voxelField
        );

        Check(selected == expectedVoxel, label);
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
            "[TerrainVoxelSelectorValidation] ===== SUMMARY =====\n" +
            "Checks = " + checks + "\n" +
            "Failures = " + failures + "\n" +
            "Result = " + (failures == 0 ? "PASS" : "FAIL")
        );
    }
}
