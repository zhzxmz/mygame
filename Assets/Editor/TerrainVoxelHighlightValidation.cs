using TerrainModificationV2;
using UnityEditor;
using UnityEngine;

/// <summary>
/// TerrainVoxelHighlight Validation（Step 3B）。
/// 验证：
///   - Voxel → Terrain Local AABB 的唯一几何定义
///   - Terrain Local → World 转换（非零 Origin / rotation / scale）
///   - Selector 选择的 Voxel 与 Highlight 区域一致
///   - Show/Hide 状态
///   - 不修改 VoxelField / DensityField / MaterialField
///   - 无 Collider（不会被 Physics.Raycast 命中）
/// 不挖掘、不修改正式 Terrain Transform。
/// </summary>
public static class TerrainVoxelHighlightValidation
{
    private const float Epsilon = 0.00001f;

    private static int failures;
    private static int checks;

    [MenuItem("Tools/Terrain/Validate Terrain Voxel Highlight")]
    public static void ValidateFromMenu()
    {
        ValidateAll();
        LogSummary();

        Debug.Log(
            "[TerrainVoxelHighlightValidation] Menu validation result = " +
            (failures == 0 ? "PASS" : "FAIL")
        );
    }

    /// <summary>Batchmode 入口：-executeMethod TerrainVoxelHighlightValidation.RunBatch。</summary>
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

        ValidateTerrainLocalGeometryCases();
        ValidateShowHideWorldAndNoCollider();
        ValidateSelectorToHighlightBehavior();
        ValidateNonIdentityTerrainTransform();
        ValidateHighlightDoesNotModifyData();
    }

    private static void ValidateTerrainLocalGeometryCases()
    {
        CheckMinMaxCenter(
            new Vector3Int(0, 0, 0),
            1f,
            new Vector3(0f, 0f, 0f),
            new Vector3(1f, 1f, 1f),
            new Vector3(0.5f, 0.5f, 0.5f),
            "Case 1"
        );

        CheckMinMaxCenter(
            new Vector3Int(1, 2, 3),
            1f,
            new Vector3(1f, 2f, 3f),
            new Vector3(2f, 3f, 4f),
            new Vector3(1.5f, 2.5f, 3.5f),
            "Case 2"
        );

        CheckMinMaxCenter(
            new Vector3Int(-1, 0, 0),
            1f,
            new Vector3(-1f, 0f, 0f),
            new Vector3(0f, 1f, 1f),
            new Vector3(-0.5f, 0.5f, 0.5f),
            "Case 3"
        );

        CheckMinMaxCenter(
            new Vector3Int(2, -3, 4),
            0.5f,
            new Vector3(1f, -1.5f, 2f),
            new Vector3(1.5f, -1f, 2.5f),
            new Vector3(1.25f, -1.25f, 2.25f),
            "Case 4"
        );
    }

    private static void ValidateShowHideWorldAndNoCollider()
    {
        GameObject terrainRoot = new GameObject("TerrainVoxelHighlightValidationRoot");
        GameObject highlightObject = new GameObject("TerrainVoxelHighlightValidationHighlight");

        try
        {
            terrainRoot.transform.position = new Vector3(100f, 50f, -30f);
            terrainRoot.transform.rotation = Quaternion.identity;
            terrainRoot.transform.localScale = Vector3.one;

            TerrainVoxelHighlight highlight =
                highlightObject.AddComponent<TerrainVoxelHighlight>();
            highlight.Initialize(terrainRoot.transform);

            highlight.Show(new Vector3Int(0, 0, 0), 1f);

            Check(highlight.IsVisible, "Case 5a: Show makes highlight visible");
            Check(
                highlight.CurrentVoxel == new Vector3Int(0, 0, 0),
                "Case 5b: CurrentVoxel == (0,0,0)"
            );

            Check(
                highlight.TryGetWorldBounds(out Vector3 worldMin, out Vector3 worldMax),
                "Case 5c: World bounds are available while visible"
            );

            Check(
                Approximately(worldMin, new Vector3(100f, 50f, -30f)) &&
                Approximately(worldMax, new Vector3(101f, 51f, -29f)),
                "Case 5d: non-zero Terrain origin produces World AABB at (100,50,-30)..(101,51,-29)"
            );

            Vector3[] worldCorners = highlight.GetWorldCorners();
            Check(
                worldCorners != null && worldCorners.Length == 8,
                "Highlight exposes 8 World corners"
            );

            LineRenderer lineRenderer = highlightObject.GetComponent<LineRenderer>();
            Check(lineRenderer != null, "Highlight uses a LineRenderer");
            Check(
                lineRenderer != null &&
                lineRenderer.enabled &&
                lineRenderer.positionCount == 24,
                "LineRenderer draws a 12-edge wireframe box (24 points)"
            );

            Check(
                highlightObject.GetComponent<Collider>() == null &&
                highlightObject.GetComponentsInChildren<Collider>().Length == 0,
                "Case 8: Highlight has no Collider, so Physics.Raycast cannot hit it"
            );

            highlight.Hide();
            Check(!highlight.IsVisible, "Case 7a: Hide clears visible state");

            if (lineRenderer != null)
            {
                Check(!lineRenderer.enabled, "Case 7b: Hide disables LineRenderer");
            }
            else
            {
                Check(false, "Case 7b: Hide disables LineRenderer");
            }

            Check(
                !highlight.TryGetWorldBounds(out _, out _),
                "Case 7c: hidden highlight reports no World bounds"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(highlightObject);
            UnityEngine.Object.DestroyImmediate(terrainRoot);
        }
    }

    private static void ValidateSelectorToHighlightBehavior()
    {
        GameObject terrainRoot = new GameObject("TerrainVoxelHighlightBehaviorRoot");
        GameObject highlightObject = new GameObject("TerrainVoxelHighlightBehaviorHighlight");

        try
        {
            terrainRoot.transform.position = Vector3.zero;
            terrainRoot.transform.rotation = Quaternion.identity;
            terrainRoot.transform.localScale = Vector3.one;

            TerrainVoxelHighlight highlight =
                highlightObject.AddComponent<TerrainVoxelHighlight>();
            highlight.Initialize(terrainRoot.transform);

            VoxelField voxelField = new VoxelField(1f);

            CheckSelectorHighlight(
                highlight,
                voxelField,
                new Vector3(0.2f, 0.2f, 0.2f),
                new Vector3Int(0, 0, 0),
                "Hit local (0.2,0.2,0.2) highlights Voxel (0,0,0)"
            );

            CheckSelectorHighlight(
                highlight,
                voxelField,
                new Vector3(0.999f, 0.999f, 0.999f),
                new Vector3Int(0, 0, 0),
                "Hit local (0.999,0.999,0.999) highlights Voxel (0,0,0)"
            );

            CheckSelectorHighlight(
                highlight,
                voxelField,
                new Vector3(1f, 0.5f, 0.5f),
                new Vector3Int(1, 0, 0),
                "Hit local (1,0.5,0.5) highlights Voxel (1,0,0)"
            );

            CheckSelectorHighlight(
                highlight,
                voxelField,
                new Vector3(-0.1f, 0.5f, 0.5f),
                new Vector3Int(-1, 0, 0),
                "Hit local (-0.1,0.5,0.5) highlights Voxel (-1,0,0)"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(highlightObject);
            UnityEngine.Object.DestroyImmediate(terrainRoot);
        }
    }

    private static void ValidateNonIdentityTerrainTransform()
    {
        GameObject terrainRoot = new GameObject("TerrainVoxelHighlightTransformRoot");
        GameObject highlightObject = new GameObject("TerrainVoxelHighlightTransformHighlight");

        try
        {
            terrainRoot.transform.position = new Vector3(5f, -2f, 3f);
            terrainRoot.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            terrainRoot.transform.localScale = new Vector3(2f, 1f, 1.5f);

            TerrainVoxelHighlight highlight =
                highlightObject.AddComponent<TerrainVoxelHighlight>();
            highlight.Initialize(terrainRoot.transform);

            Vector3Int voxel = new Vector3Int(1, -2, 3);
            float voxelSize = 0.5f;
            highlight.Show(voxel, voxelSize);

            Vector3 localMin = TerrainVoxelHighlight.GetTerrainLocalMin(voxel, voxelSize);
            Vector3 localMax = TerrainVoxelHighlight.GetTerrainLocalMax(voxel, voxelSize);

            Vector3[] expectedCorners =
            {
                new Vector3(localMin.x, localMin.y, localMin.z),
                new Vector3(localMax.x, localMin.y, localMin.z),
                new Vector3(localMax.x, localMin.y, localMax.z),
                new Vector3(localMin.x, localMin.y, localMax.z),
                new Vector3(localMin.x, localMax.y, localMin.z),
                new Vector3(localMax.x, localMax.y, localMin.z),
                new Vector3(localMax.x, localMax.y, localMax.z),
                new Vector3(localMin.x, localMax.y, localMax.z)
            };

            Vector3[] actualCorners = highlight.GetWorldCorners();
            Check(actualCorners != null && actualCorners.Length == 8, "Non-identity transform: world corners exist");

            bool cornersMatch = actualCorners != null && actualCorners.Length == 8;
            if (actualCorners != null)
            {
                for (int i = 0; i < actualCorners.Length; i++)
                {
                    Vector3 expected = terrainRoot.transform.TransformPoint(expectedCorners[i]);
                    if (Vector3.Distance(actualCorners[i], expected) > Epsilon)
                    {
                        cornersMatch = false;
                    }
                }
            }

            Check(
                cornersMatch,
                "Highlight respects Terrain Root position / rotation / scale (no identity assumption)"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(highlightObject);
            UnityEngine.Object.DestroyImmediate(terrainRoot);
        }
    }

    private static void ValidateHighlightDoesNotModifyData()
    {
        GameObject terrainRoot = new GameObject("TerrainVoxelHighlightDataRoot");
        GameObject highlightObject = new GameObject("TerrainVoxelHighlightDataHighlight");

        try
        {
            TerrainVoxelHighlight highlight =
                highlightObject.AddComponent<TerrainVoxelHighlight>();
            highlight.Initialize(terrainRoot.transform);

            VoxelField voxelField = new VoxelField(1f);
            voxelField.Set(new Vector3Int(5, 6, 7), VoxelState.Air);

            DensityField densityField = new DensityField(
                2, 2, 2,
                Vector3.zero,
                new Vector3(2f, 2f, 2f)
            );
            densityField[0, 0, 0] = 0.25f;

            MaterialField materialField = new MaterialField(
                2, 2, 2,
                Vector3.zero,
                new Vector3(2f, 2f, 2f)
            );
            materialField[0, 0, 0] = MaterialType.Grass;

            int storedBefore = voxelField.StoredVoxelCount;
            float densityBefore = densityField[0, 0, 0];
            MaterialType materialBefore = materialField[0, 0, 0];

            highlight.Show(new Vector3Int(0, 0, 0), 1f);
            highlight.Show(new Vector3Int(-1, 2, 3), 1f);
            highlight.Hide();

            Check(
                voxelField.StoredVoxelCount == storedBefore &&
                voxelField.Get(new Vector3Int(5, 6, 7)) == VoxelState.Air,
                "Case 6: Highlight does not modify VoxelField"
            );

            Check(
                Mathf.Abs(densityField[0, 0, 0] - densityBefore) < Epsilon,
                "Case 6: Highlight does not modify DensityField"
            );

            Check(
                materialField[0, 0, 0] == materialBefore,
                "Case 6: Highlight does not modify MaterialField"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(highlightObject);
            UnityEngine.Object.DestroyImmediate(terrainRoot);
        }
    }

    private static void CheckMinMaxCenter(
        Vector3Int voxel,
        float voxelSize,
        Vector3 expectedMin,
        Vector3 expectedMax,
        Vector3 expectedCenter,
        string label)
    {
        Check(
            Approximately(
                TerrainVoxelHighlight.GetTerrainLocalMin(voxel, voxelSize),
                expectedMin
            ),
            label + ": Terrain Local Min correct"
        );

        Check(
            Approximately(
                TerrainVoxelHighlight.GetTerrainLocalMax(voxel, voxelSize),
                expectedMax
            ),
            label + ": Terrain Local Max correct"
        );

        Check(
            Approximately(
                TerrainVoxelHighlight.GetTerrainLocalCenter(voxel, voxelSize),
                expectedCenter
            ),
            label + ": Center = (Min + Max) / 2"
        );
    }

    private static void CheckSelectorHighlight(
        TerrainVoxelHighlight highlight,
        VoxelField voxelField,
        Vector3 hitLocalPoint,
        Vector3Int expectedVoxel,
        string label)
    {
        Vector3Int selected = TerrainVoxelSelector.SelectVoxel(
            hitLocalPoint,
            voxelField
        );

        highlight.Show(selected, voxelField.VoxelSize);

        Check(
            selected == expectedVoxel &&
            highlight.CurrentVoxel == expectedVoxel,
            label
        );

        Check(
            Approximately(
                highlight.TerrainLocalMin,
                TerrainVoxelHighlight.GetTerrainLocalMin(expectedVoxel, voxelField.VoxelSize)
            ) &&
            Approximately(
                highlight.TerrainLocalMax,
                TerrainVoxelHighlight.GetTerrainLocalMax(expectedVoxel, voxelField.VoxelSize)
            ),
            label + ": highlight AABB equals selected Voxel AABB"
        );
    }

    private static bool Approximately(Vector3 a, Vector3 b)
    {
        return Vector3.Distance(a, b) < Epsilon;
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
            "[TerrainVoxelHighlightValidation] ===== SUMMARY =====\n" +
            "Checks = " + checks + "\n" +
            "Failures = " + failures + "\n" +
            "Result = " + (failures == 0 ? "PASS" : "FAIL")
        );
    }
}
