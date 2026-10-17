using System.Reflection;
using TerrainModificationV2;
using UnityEditor;
using UnityEngine;

/// <summary>
/// DensityTerrain → VoxelField Ownership Validation（Step 3A.5）。
/// 验证 DensityTerrain 是 VoxelField 的唯一权威 Runtime 归属：
///   - 实例存在且 VoxelSize = 1m
///   - 多次访问返回同一实例
///   - Sparse，不预填充
///   - TerrainChunk 不持有 VoxelField
///   - 每个 DensityTerrain 各持有一个实例（非全局 static 单例）
/// </summary>
public static class DensityTerrainVoxelFieldOwnershipValidation
{
    private static int failures;
    private static int checks;

    [MenuItem("Tools/Terrain/Validate DensityTerrain VoxelField Ownership")]
    public static void ValidateFromMenu()
    {
        ValidateAll();
        LogSummary();

        Debug.Log(
            "[DensityTerrainVoxelFieldOwnershipValidation] Menu validation result = " +
            (failures == 0 ? "PASS" : "FAIL")
        );
    }

    /// <summary>Batchmode 入口：-executeMethod DensityTerrainVoxelFieldOwnershipValidation.RunBatch。</summary>
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

        ValidateOwnershipBasics();
        ValidateChunkRelationshipAndSingleInstance();
        ValidatePerTerrainInstance();
    }

    private static void ValidateOwnershipBasics()
    {
        GameObject terrainObject = CreateTemporaryTerrain(out DensityTerrain terrain);

        try
        {
            VoxelField first = terrain.VoxelField;

            Check(first != null, "Case 1: terrain.VoxelField != null");
            Check(first.VoxelSize == 1f, "Case 2: VoxelField.VoxelSize == 1.0f");

            VoxelField second = terrain.VoxelField;
            Check(
                ReferenceEquals(first, second),
                "Case 3: repeated terrain.VoxelField returns the same instance"
            );

            PropertyInfo property = typeof(DensityTerrain).GetProperty(
                "VoxelField",
                BindingFlags.Public | BindingFlags.Instance
            );

            Check(property != null, "DensityTerrain exposes public VoxelField property");
            Check(
                property != null && property.CanRead && !property.CanWrite,
                "VoxelField property is read-only from outside"
            );

            Check(
                terrain.VoxelField.Get(new Vector3Int(0, 0, 0)) == VoxelState.Solid,
                "Case 4: unedited Voxel(0,0,0) returns default Solid"
            );

            Check(
                terrain.VoxelField.StoredVoxelCount == 0,
                "Case 5: initialization does not prefill any Voxel (sparse, empty storage)"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    private static void ValidateChunkRelationshipAndSingleInstance()
    {
        GameObject terrainObject = CreateTemporaryTerrain(out DensityTerrain terrain);

        try
        {
            VoxelField beforeRegenerate = terrain.VoxelField;

            // 临时生成 2×1×1 Chunk 仅用于验证归属关系；
            // 不修改正式 Terrain，不接入 VoxelField 到生成流程。
            terrain.chunkCount = new Vector3Int(2, 1, 1);
            terrain.chunkStartCoordinate = Vector3Int.zero;
            terrain.chunkVoxelSize = new Vector3Int(2, 2, 2);
            terrain.resolution = 1;
            terrain.Regenerate();

            TerrainChunkView[] chunkViews =
                terrainObject.GetComponentsInChildren<TerrainChunkView>(true);

            Check(
                chunkViews != null && chunkViews.Length == 2,
                "Case 6a: temporary terrain generated 2 TerrainChunkView objects"
            );

            Check(
                ReferenceEquals(beforeRegenerate, terrain.VoxelField),
                "Case 6b: Regenerate / Chunk creation does not replace VoxelField"
            );

            bool hasChunk0 = false;
            bool hasChunk1 = false;

            if (chunkViews != null)
            {
                for (int i = 0; i < chunkViews.Length; i++)
                {
                    if (chunkViews[i].Coordinate == new Vector3Int(0, 0, 0)) hasChunk0 = true;
                    if (chunkViews[i].Coordinate == new Vector3Int(1, 0, 0)) hasChunk1 = true;
                }
            }

            Check(
                hasChunk0 && hasChunk1,
                "Case 6c: generated chunks are Chunk(0,0,0) and Chunk(1,0,0)"
            );

            PropertyInfo chunkVoxelProperty = typeof(TerrainChunk).GetProperty(
                "VoxelField",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            );

            FieldInfo chunkVoxelField = typeof(TerrainChunk).GetField(
                "VoxelField",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            );

            Check(
                chunkVoxelProperty == null && chunkVoxelField == null,
                "Case 6d: TerrainChunk does not own a VoxelField member"
            );

            Check(
                terrain.VoxelField.StoredVoxelCount == 0,
                "Regenerate does not prefill or mutate authoritative VoxelField"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    private static void ValidatePerTerrainInstance()
    {
        GameObject terrainObjectA = CreateTemporaryTerrain(out DensityTerrain terrainA);
        GameObject terrainObjectB = CreateTemporaryTerrain(out DensityTerrain terrainB);

        try
        {
            Check(
                terrainA.VoxelField != null && terrainB.VoxelField != null,
                "Two DensityTerrain instances each expose their own VoxelField"
            );

            Check(
                !ReferenceEquals(terrainA.VoxelField, terrainB.VoxelField),
                "Ownership is per-DensityTerrain, not a global static singleton"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObjectA);
            UnityEngine.Object.DestroyImmediate(terrainObjectB);
        }
    }

    private static GameObject CreateTemporaryTerrain(out DensityTerrain terrain)
    {
        GameObject terrainObject = new GameObject(
            "DensityTerrainVoxelFieldOwnershipValidationTerrain"
        );

        // 保持 inactive，避免 OnEnable 的 Editor 延迟 Regenerate 干扰纯 Ownership 测试。
        terrainObject.SetActive(false);
        terrain = terrainObject.AddComponent<DensityTerrain>();
        terrainObject.transform.position = Vector3.zero;
        terrainObject.transform.rotation = Quaternion.identity;
        terrainObject.transform.localScale = Vector3.one;

        return terrainObject;
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
            "[DensityTerrainVoxelFieldOwnershipValidation] ===== SUMMARY =====\n" +
            "Checks = " + checks + "\n" +
            "Failures = " + failures + "\n" +
            "Result = " + (failures == 0 ? "PASS" : "FAIL")
        );
    }
}
