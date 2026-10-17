using System.Reflection;
using TerrainModificationV2;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Terrain Voxel Mining Validation（Step 3C）。
/// 验证 F 键新路径：
///   SelectedVoxel + SelectedTerrain → DensityTerrain.VoxelField.Set(Air)
/// 不调用旧 Mining，不修改 Density / Material / Mesh / Collider。
/// </summary>
public static class TerrainVoxelMiningValidation
{
    private const float Epsilon = 0.00001f;

    private static int failures;
    private static int checks;

    [MenuItem("Tools/Terrain/Validate Terrain Voxel Mining")]
    public static void ValidateFromMenu()
    {
        ValidateAll();
        LogSummary();

        Debug.Log(
            "[TerrainVoxelMiningValidation] Menu validation result = " +
            (failures == 0 ? "PASS" : "FAIL")
        );
    }

    /// <summary>Batchmode 入口：-executeMethod TerrainVoxelMiningValidation.RunBatch。</summary>
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

        ValidateSingleAndRepeatedMining();
        ValidateAdjacentVoxelIsolation();
        ValidateNegativeCoordinateMining();
        ValidateThreeAxisNegativeMining();
        ValidateSparseStorageSemantics();
        ValidateNoSelectionMinesNothing();
        ValidateCrossTerrainIsolation();
        ValidateNoDensityMeshColliderMutation();
        ValidatePartialSolidVoxelMining();
    }

    /// <summary>
    /// A 阶段修正：部分实体 Voxel（中心基础密度 &lt;= 0，但 Voxel 内含可见实体表面）
    /// 必须能被选中与挖掘，且挖掘后复验不得漂移到相邻 Voxel。
    /// 夹具：平地 height = 10.25 ⇒ Voxel(0,10,0)（跨 y ∈ [10,11]）中心密度 −0.25，
    /// 等值面 y = 10.25 落在该 Voxel 内部（其下方 0.25m 为实体）。
    /// </summary>
    private static void ValidatePartialSolidVoxelMining()
    {
        const float surfaceHeight = 10.25f;
        Vector3Int voxel = new Vector3Int(0, 10, 0);

        GameObject terrainObject = CreateTemporaryTerrain(
            out DensityTerrain terrain, surfaceHeight);
        GameObject controllerObject = CreateTemporaryController(
            out PlayerMiningController controller);

        try
        {
            Check(
                terrain.VoxelField.Get(voxel) == VoxelState.Solid,
                "Case 10: 部分实体 Voxel 初始为 Solid（未编辑）"
            );

            TerrainChunk chunk = FindChunkForVoxel(terrain, voxel);
            float centreDensity = SampleBaseDensity(
                chunk,
                new Vector3(voxel.x + 0.5f, voxel.y + 0.5f, voxel.z + 0.5f),
                out bool centreSampled
            );

            Check(
                chunk != null && centreSampled && centreDensity <= 0f,
                "Case 10: 用例成立 —— 目标 Voxel 中心基础密度 <= 0（" +
                (centreSampled ? centreDensity.ToString("F4") : "<out of domain>") +
                "），旧判据会拒绝"
            );

            Vector3 localHit = new Vector3(0.5f, surfaceHeight, 0.5f);

            Check(
                SetSelectedVoxel(controller, terrain, voxel, localHit, Vector3.up),
                "Case 10: 测试选择（含命中点/法线）已注入"
            );

            TerrainVoxelHighlight highlight = CreateHighlight(
                controller, terrain, voxel);

            Check(
                highlight != null && highlight.IsVisible &&
                highlight.CurrentVoxel == voxel,
                "Case 10: 选择与高亮指向同一个最终目标 " + voxel
            );

            Check(
                controller.TryMineVoxel(),
                "Case 10: 部分实体 Voxel 正常挖掘（F 返回成功）"
            );

            Check(
                terrain.VoxelField.Get(voxel) == VoxelState.Air,
                "Case 10: 目标 Voxel 变为 Air"
            );

            int neighboursMined = CountMinedNeighbours(terrain, voxel);

            Check(
                neighboursMined == 0 && terrain.VoxelField.StoredVoxelCount == 1,
                "Case 10: 只编辑目标 Voxel（相邻被挖 " + neighboursMined +
                "，StoredVoxelCount=" + terrain.VoxelField.StoredVoxelCount + "）"
            );

            Check(
                !controller.HasSelectedVoxel,
                "Case 10: 挖掘后复验未漂移到相邻 Voxel（失效选择已清除）"
            );

            Check(
                !highlight.IsVisible,
                "Case 10: 挖掘后高亮不再停留在已挖空的目标上"
            );

            Check(
                !controller.TryMineVoxel() &&
                terrain.VoxelField.StoredVoxelCount == 1 &&
                CountMinedNeighbours(terrain, voxel) == 0,
                "Case 10: 再次 F 明确失败且不写任何 Voxel（不静默空操作、不误挖相邻）"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(controllerObject);
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    private static void ValidateSingleAndRepeatedMining()
    {
        GameObject terrainObject = CreateTemporaryTerrain(out DensityTerrain terrain);
        GameObject controllerObject = CreateTemporaryController(out PlayerMiningController controller);

        try
        {
            Vector3Int voxel = new Vector3Int(0, 0, 0);
            Check(terrain.VoxelField.Get(voxel) == VoxelState.Solid, "Case 1: initial (0,0,0) is Solid");

            Check(SetSelectedVoxel(controller, terrain, voxel), "Case 1: test selection is set");
            Check(controller.TryMineVoxel(), "Case 1: first F returns success");
            Check(terrain.VoxelField.Get(voxel) == VoxelState.Air, "Case 1: after first F (0,0,0) is Air");
            Check(terrain.VoxelField.StoredVoxelCount == 1, "Case 6: one new Air entry stored");

            // A 阶段：被挖空的 Voxel 不再是有效目标 —— 过期选择不得静默空操作。
            // 生产路径每帧先重算选择（Update → UpdateVoxelSelectionAndHighlight）
            // 再判定 F 键，"同一帧连续两次 F" 并不是生产场景；这里断言的是：
            // 失效目标必须明确失败并清除选择，而不是保留一个已成 Air 的目标。
            Check(
                !controller.TryMineVoxel(),
                "Case 2: 挖掘后被挖空的 Voxel 不再是有效选择（第二次 F 明确失败，不静默空操作）"
            );
            Check(
                !controller.HasSelectedVoxel,
                "Case 2: 失效选择已被清除（HasSelectedVoxel=false）"
            );
            Check(terrain.VoxelField.Get(voxel) == VoxelState.Air, "Case 2: Air stays Air after second F");
            Check(terrain.VoxelField.StoredVoxelCount == 1, "Case 6: repeated F does not add a second entry");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(controllerObject);
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    private static void ValidateAdjacentVoxelIsolation()
    {
        GameObject terrainObject = CreateTemporaryTerrain(out DensityTerrain terrain);
        GameObject controllerObject = CreateTemporaryController(out PlayerMiningController controller);

        try
        {
            Check(
                terrain.VoxelField.Get(new Vector3Int(0, 0, 0)) == VoxelState.Solid &&
                terrain.VoxelField.Get(new Vector3Int(1, 0, 0)) == VoxelState.Solid,
                "Case 3: (0,0,0) and (1,0,0) both start Solid"
            );

            SetSelectedVoxel(controller, terrain, new Vector3Int(0, 0, 0));
            controller.TryMineVoxel();

            Check(
                terrain.VoxelField.Get(new Vector3Int(0, 0, 0)) == VoxelState.Air &&
                terrain.VoxelField.Get(new Vector3Int(1, 0, 0)) == VoxelState.Solid,
                "Case 3: mining (0,0,0) does not modify adjacent (1,0,0)"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(controllerObject);
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    private static void ValidateNegativeCoordinateMining()
    {
        GameObject terrainObject = CreateTemporaryTerrain(out DensityTerrain terrain);
        GameObject controllerObject = CreateTemporaryController(out PlayerMiningController controller);

        try
        {
            SetSelectedVoxel(controller, terrain, new Vector3Int(-1, 0, 0));
            Check(controller.TryMineVoxel(), "Case 4: mining (-1,0,0) succeeds");

            Check(
                terrain.VoxelField.Get(new Vector3Int(-1, 0, 0)) == VoxelState.Air,
                "Case 4: (-1,0,0) becomes Air"
            );

            Check(
                terrain.VoxelField.Get(new Vector3Int(0, 0, 0)) == VoxelState.Solid,
                "Case 4: (0,0,0) stays Solid"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(controllerObject);
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    private static void ValidateThreeAxisNegativeMining()
    {
        GameObject terrainObject = CreateTemporaryTerrain(out DensityTerrain terrain);
        GameObject controllerObject = CreateTemporaryController(out PlayerMiningController controller);

        try
        {
            Vector3Int target = new Vector3Int(-2, -3, -4);
            SetSelectedVoxel(controller, terrain, target);
            controller.TryMineVoxel();

            Check(
                terrain.VoxelField.Get(target) == VoxelState.Air,
                "Case 5: (-2,-3,-4) becomes Air"
            );

            Check(
                terrain.VoxelField.Get(new Vector3Int(-1, -3, -4)) == VoxelState.Solid &&
                terrain.VoxelField.Get(new Vector3Int(-2, -2, -4)) == VoxelState.Solid &&
                terrain.VoxelField.Get(new Vector3Int(-2, -3, -3)) == VoxelState.Solid,
                "Case 5: only the selected negative Voxel was modified"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(controllerObject);
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    private static void ValidateSparseStorageSemantics()
    {
        GameObject terrainObject = CreateTemporaryTerrain(out DensityTerrain terrain);
        GameObject controllerObject = CreateTemporaryController(out PlayerMiningController controller);

        try
        {
            Check(terrain.VoxelField.StoredVoxelCount == 0, "Case 6: sparse storage starts empty");

            SetSelectedVoxel(controller, terrain, new Vector3Int(7, 8, 9));
            controller.TryMineVoxel();
            Check(terrain.VoxelField.StoredVoxelCount == 1, "Case 6: first Air adds exactly one entry");

            controller.TryMineVoxel();
            controller.TryMineVoxel();
            Check(terrain.VoxelField.StoredVoxelCount == 1, "Case 6: repeated Air is idempotent and adds no entries");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(controllerObject);
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    private static void ValidateNoSelectionMinesNothing()
    {
        GameObject terrainObject = CreateTemporaryTerrain(out DensityTerrain terrain);
        GameObject controllerObject = CreateTemporaryController(out PlayerMiningController controller);

        try
        {
            Check(!controller.TryMineVoxel(), "Case 8: F with no SelectedVoxel returns false");
            Check(terrain.VoxelField.StoredVoxelCount == 0, "Case 8: no Voxel was modified");
            Check(
                terrain.VoxelField.Get(new Vector3Int(0, 0, 0)) == VoxelState.Solid,
                "Case 8: default Voxel(0,0,0) is untouched"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(controllerObject);
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    private static void ValidateCrossTerrainIsolation()
    {
        GameObject terrainObjectA = CreateTemporaryTerrain(out DensityTerrain terrainA);
        GameObject terrainObjectB = CreateTemporaryTerrain(out DensityTerrain terrainB);
        GameObject controllerObject = CreateTemporaryController(out PlayerMiningController controller);

        try
        {
            SetSelectedVoxel(controller, terrainA, new Vector3Int(0, 0, 0));
            controller.TryMineVoxel();

            Check(
                terrainA.VoxelField.Get(new Vector3Int(0, 0, 0)) == VoxelState.Air,
                "Case 7: selected Terrain A Voxel becomes Air"
            );

            Check(
                terrainB.VoxelField.Get(new Vector3Int(0, 0, 0)) == VoxelState.Solid &&
                terrainB.VoxelField.StoredVoxelCount == 0,
                "Case 7: unselected Terrain B VoxelField is untouched"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(controllerObject);
            UnityEngine.Object.DestroyImmediate(terrainObjectA);
            UnityEngine.Object.DestroyImmediate(terrainObjectB);
        }
    }

    private static void ValidateNoDensityMeshColliderMutation()
    {
        GameObject terrainObject = CreateTemporaryTerrain(out DensityTerrain terrain);
        GameObject controllerObject = CreateTemporaryController(out PlayerMiningController controller);

        try
        {
            terrain.chunkCount = new Vector3Int(1, 1, 1);
            terrain.chunkStartCoordinate = Vector3Int.zero;
            terrain.chunkVoxelSize = new Vector3Int(2, 2, 2);
            terrain.resolution = 1;
            terrain.Regenerate();

            TerrainChunkView[] views =
                terrainObject.GetComponentsInChildren<TerrainChunkView>(true);

            Check(
                views != null && views.Length == 1,
                "Case 9 setup: one temporary chunk exists"
            );

            if (views == null || views.Length != 1) return;

            TerrainChunkView view = views[0];
            float densityBefore = view.Chunk.Density[0, 0, 0];
            MaterialType materialBefore = view.Chunk.Material[0, 0, 0];
            int childCountBefore = terrainObject.transform.childCount;

            SetSelectedVoxel(controller, terrain, new Vector3Int(0, 0, 0));
            controller.TryMineVoxel();

            Check(
                Mathf.Abs(view.Chunk.Density[0, 0, 0] - densityBefore) < Epsilon,
                "Case 9: F keeps Base DensityField unchanged"
            );

            Check(
                view.Chunk.Material[0, 0, 0] == materialBefore,
                "Case 9: F keeps MaterialField unchanged"
            );

            Check(
                terrainObject.transform.childCount == childCountBefore,
                "Case 9: F does not create/destroy Chunk GameObjects"
            );

            Check(
                terrain.VoxelField.Get(new Vector3Int(0, 0, 0)) == VoxelState.Air,
                "Case 9: F writes only VoxelField as Mutable Edit State"
            );

            Check(
                view.MeshFilter != null &&
                view.MeshFilter.sharedMesh == view.Chunk.Mesh,
                "Case 9 (Step 3D): MeshFilter is synced to TerrainChunk.Mesh"
            );

            Check(
                view.MeshCollider != null &&
                view.MeshCollider.sharedMesh == view.Chunk.Mesh,
                "Case 9 (Step 3D): MeshCollider is synced to TerrainChunk.Mesh"
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(controllerObject);
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    private static bool SetSelectedVoxel(
        PlayerMiningController controller,
        DensityTerrain terrain,
        Vector3Int voxel)
    {
        // 兼容既有用例：命中点取该 Voxel 的 +Y 面平面上一点（外向法线 +Y）——
        // 沿法线反向偏移 VoxelSize×1e-3 后仍落在同一 Voxel 内。
        if (controller == null || terrain == null) return false;

        float voxelSize = terrain.VoxelField != null
            ? terrain.VoxelField.VoxelSize
            : 1f;

        Vector3 localPoint = new Vector3(
            (voxel.x + 0.5f) * voxelSize,
            (voxel.y + 1f) * voxelSize,
            (voxel.z + 0.5f) * voxelSize
        );

        return SetSelectedVoxel(
            controller, terrain, voxel, localPoint, Vector3.up);
    }

    /// <summary>
    /// 注入选择状态（含生产路径必然存在的命中点/法线，用于 TryMineVoxel 挖掘前复验）。
    /// localHitPoint / localNormal 为 Terrain Local Space，测试内部转换为 World Space，
    /// 与 PlayerMiningController 记录 selectedRaycastHitWorld/Normal 的方式一致。
    /// </summary>
    private static bool SetSelectedVoxel(
        PlayerMiningController controller,
        DensityTerrain terrain,
        Vector3Int voxel,
        Vector3 localHitPoint,
        Vector3 localNormal)
    {
        if (controller == null || terrain == null) return false;

        FieldInfo hasSelected = typeof(PlayerMiningController).GetField(
            "hasSelectedVoxel",
            BindingFlags.Instance | BindingFlags.NonPublic
        );

        FieldInfo selectedVoxelField = typeof(PlayerMiningController).GetField(
            "selectedVoxel",
            BindingFlags.Instance | BindingFlags.NonPublic
        );

        FieldInfo selectedTerrainField = typeof(PlayerMiningController).GetField(
            "selectedTerrain",
            BindingFlags.Instance | BindingFlags.NonPublic
        );

        FieldInfo hitWorldField = typeof(PlayerMiningController).GetField(
            "selectedRaycastHitWorld",
            BindingFlags.Instance | BindingFlags.NonPublic
        );

        FieldInfo hitNormalField = typeof(PlayerMiningController).GetField(
            "selectedRaycastHitNormal",
            BindingFlags.Instance | BindingFlags.NonPublic
        );

        if (hasSelected == null ||
            selectedVoxelField == null ||
            selectedTerrainField == null ||
            hitWorldField == null ||
            hitNormalField == null)
        {
            return false;
        }

        hasSelected.SetValue(controller, true);
        selectedVoxelField.SetValue(controller, voxel);
        selectedTerrainField.SetValue(controller, terrain);
        hitWorldField.SetValue(
            controller,
            terrain.transform.TransformPoint(localHitPoint)
        );
        hitNormalField.SetValue(
            controller,
            terrain.transform.TransformDirection(localNormal)
        );
        return true;
    }

    private static TerrainChunk FindChunkForVoxel(
        DensityTerrain terrain,
        Vector3Int voxel)
    {
        if (terrain == null) return null;

        TerrainChunk chunk;
        bool found = terrain.TryGetChunk(
            ChunkSpace.VoxelToChunk(voxel, terrain.EffectiveChunkVoxelSize),
            out chunk
        );

        return found ? chunk : null;
    }

    private static float SampleBaseDensity(
        TerrainChunk chunk,
        Vector3 terrainLocal,
        out bool sampled)
    {
        float density;
        string error;
        sampled = VoxelAwareMeshRuntimeMutation.TrySampleBaseDensity(
            chunk,
            terrainLocal,
            out density,
            out error
        );

        return density;
    }

    private static TerrainVoxelHighlight CreateHighlight(
        PlayerMiningController controller,
        DensityTerrain terrain,
        Vector3Int voxel)
    {
        GameObject highlightObject = new GameObject(
            "TerrainVoxelMiningValidationHighlight"
        );

        highlightObject.SetActive(false);
        highlightObject.transform.SetParent(controller.transform, false);

        TerrainVoxelHighlight highlight =
            highlightObject.AddComponent<TerrainVoxelHighlight>();

        controller.voxelHighlight = highlight;
        controller.showVoxelHighlight = true;
        highlight.Initialize(terrain.transform);
        highlight.Show(voxel, terrain.VoxelField.VoxelSize);
        return highlight;
    }

    private static int CountMinedNeighbours(
        DensityTerrain terrain,
        Vector3Int voxel)
    {
        if (terrain == null) return 0;

        Vector3Int[] offsets = new Vector3Int[]
        {
            Vector3Int.right,
            Vector3Int.left,
            Vector3Int.up,
            Vector3Int.down,
            Vector3Int.forward,
            Vector3Int.back
        };

        int mined = 0;
        for (int i = 0; i < offsets.Length; i++)
        {
            if (terrain.VoxelField.Get(voxel + offsets[i]) == VoxelState.Air)
            {
                mined++;
            }
        }

        return mined;
    }

    /// <summary>
    /// A 阶段：挖掘要求目标 Voxel 具备权威材料证据
    /// （基础 DensityField 在 Voxel 中心或命中侧探测点 > 0，且未被编辑为 Air），
    /// 因此测试地形必须是真实生成过的、覆盖被测 Voxel 的地形
    /// （而不是没有 Chunk / Density 的空壳 DensityTerrain）。
    /// 正式契约：VoxelSize = 1m、ChunkVoxelSize = 16³；
    /// Chunk 起点 (−1,−1,−1)、2×2×2 个 Chunk ⇒ 覆盖 Voxel 范围 [−16,15]³，
    /// 含本文件用到的负坐标 (−2,−3,−4)；平地 height ⇒ y &lt; height 为实体，
    /// 默认 height = 12 覆盖 (0,0,0) / (1,0,0) / (−1,0,0) / (7,8,9)。
    /// </summary>
    private static GameObject CreateTemporaryTerrain(
        out DensityTerrain terrain,
        float height = 12f)
    {
        GameObject terrainObject = new GameObject(
            "TerrainVoxelMiningValidationTerrain"
        );

        terrainObject.SetActive(false);
        terrain = terrainObject.AddComponent<DensityTerrain>();
        terrainObject.transform.position = Vector3.zero;
        terrainObject.transform.rotation = Quaternion.identity;
        terrainObject.transform.localScale = Vector3.one;

        terrain.voxelSize = 1f;
        terrain.chunkVoxelSize = new Vector3Int(16, 16, 16);
        terrain.chunkCount = new Vector3Int(2, 2, 2);
        terrain.chunkStartCoordinate = new Vector3Int(-1, -1, -1);
        terrain.resolution = 16;
        terrain.densityMode = DensityGenerationMode.HeightField;
        terrain.useNoise = false;
        terrain.height = height;
        terrain.regenerateInEditor = false;
        terrain.Regenerate();

        return terrainObject;
    }

    private static GameObject CreateTemporaryController(out PlayerMiningController controller)
    {
        GameObject controllerObject = new GameObject(
            "TerrainVoxelMiningValidationController"
        );

        controllerObject.SetActive(false);
        controller = controllerObject.AddComponent<PlayerMiningController>();
        return controllerObject;
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
            "[TerrainVoxelMiningValidation] ===== SUMMARY =====\n" +
            "Checks = " + checks + "\n" +
            "Failures = " + failures + "\n" +
            "Result = " + (failures == 0 ? "PASS" : "FAIL")
        );
    }
}
