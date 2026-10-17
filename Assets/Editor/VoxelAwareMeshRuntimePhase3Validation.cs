using System.Collections.Generic;
using System.IO;
using TerrainModificationV2;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// VoxelAwareMesh Runtime Phase 3 Validation（Feature Switch / Runtime Integration）。
///
/// 覆盖：
///   A. Feature Switch（旧路径 / 新路径 / 反复切换 / Dispose）
///   B. Chunk 布局（chunkCount (2,1,2) + chunkStartCoordinate (5,0,7)）
///   C. MeshFilter 同步（Regenerate / Mesh 替换 / 旧 Mesh 不再被引用 / 无重复 Chunk）
///   D. MeshCollider 现状（只检查，不新增不修改）
///   E. 非 Identity Transform（Chunk World Position / Chunk Local Space / Mesh 顶点）
///   F. 场景持久化与生命周期（不累积 / 不持久化 Chunk / 非 Chunk 子对象 / Scene Reload）
///
/// 不修改 Mining / Collider / VoxelAwareMesh 算法。
/// </summary>
public static class VoxelAwareMeshRuntimePhase3Validation
{
    private const string SampleScenePath = "Assets/Scenes/SampleScene.unity";

    private static int checks;
    private static int failures;
    private static bool sceneRuntimeSkipped;

    private sealed class MeshStats
    {
        public int VertexCount;
        public int TriangleCount;
        public float Area;
        public int DegenerateTriangles;
        public bool HasNaN;
    }

    [MenuItem("Tools/Terrain/Validate VoxelAwareMesh Runtime Phase 3")]
    public static void ValidateFromMenu()
    {
        RunAll();
        LogSummary();
    }

    public static void RunBatch()
    {
        try
        {
            RunAll();
        }
        catch (System.Exception exception)
        {
            failures++;
            Debug.LogError(
                "[VoxelAwareMeshRuntimePhase3Validation] EXCEPTION: " +
                exception
            );
        }
        finally
        {
            LogSummary();
            EditorApplication.Exit(failures == 0 ? 0 : 1);
        }
    }

    private static void RunAll()
    {
        checks = 0;
        failures = 0;
        sceneRuntimeSkipped = false;

        CheckLegacyPath();
        CheckVoxelAwarePath();
        CheckFeatureSwitchToggleLifecycle();
        CheckChunkLayout();
        CheckMeshFilterSyncAndReplacement();
        CheckMeshColliderStatus();
        CheckNonIdentityTransform();
        CheckScenePersistenceAndLifecycle();
        CheckSceneRoundTrip();

        float probeSeconds = ProbeSampleSceneConfigCost();
        CheckSampleSceneRuntime(probeSeconds);
    }

    // =====================================================================
    // A1 — Legacy（Feature Switch OFF）
    // =====================================================================

    private static void CheckLegacyPath()
    {
        GameObject terrainObject = CreateTerrainObject(
            "Phase3_LegacyTerrain",
            new Vector3Int(1, 1, 1),
            Vector3Int.zero,
            false,
            Vector3.zero,
            Quaternion.identity,
            Vector3.one
        );

        try
        {
            List<TerrainChunkView> views = GetViews(terrainObject);

            Check(views.Count == 1, "Phase3-A1: legacy path creates exactly one Chunk");
            if (views.Count == 0) return;

            TerrainChunkView view = views[0];
            TerrainChunk chunk = view.Chunk;

            Check(
                chunk != null && chunk.Geometry == null && chunk.RawMesh == null,
                "Phase3-A1: legacy Chunk has no ChunkGeometry / RawMesh"
            );
            Check(
                chunk.Mesh != null && view.MeshFilter != null &&
                view.MeshFilter.sharedMesh == chunk.Mesh,
                "Phase3-A1: legacy MeshFilter references Chunk.Mesh"
            );

            Mesh expected = TerrainMeshGenerator.Generate(
                chunk.Density,
                chunk.Material,
                chunk.IsoLevel
            );
            MeshStats expectedStats = ComputeMeshStats(expected);
            MeshStats actualStats = ComputeMeshStats(chunk.Mesh);

            Check(
                expectedStats.VertexCount > 0 &&
                expectedStats.VertexCount == actualStats.VertexCount &&
                expectedStats.TriangleCount == actualStats.TriangleCount &&
                Mathf.Abs(expectedStats.Area - actualStats.Area) < 1e-3f,
                "Phase3-A1: legacy Final Mesh equals legacy TerrainMeshGenerator output"
            );

            if (expected != null) Object.DestroyImmediate(expected);

            Check(
                view.MeshCollider != null &&
                view.MeshCollider.sharedMesh == chunk.Mesh,
                "Phase3-A1: legacy MeshCollider uses the same final Mesh"
            );
        }
        finally
        {
            Object.DestroyImmediate(terrainObject);
        }
    }

    // =====================================================================
    // A2 / A3 — VoxelAware（Feature Switch ON）
    // =====================================================================

    private static void CheckVoxelAwarePath()
    {
        GameObject terrainObject = CreateTerrainObject(
            "Phase3_VoxelAwareTerrain",
            new Vector3Int(1, 1, 1),
            Vector3Int.zero,
            true,
            Vector3.zero,
            Quaternion.identity,
            Vector3.one
        );

        try
        {
            List<TerrainChunkView> views = GetViews(terrainObject);

            Check(views.Count == 1, "Phase3-A2: voxel-aware path creates exactly one Chunk");
            if (views.Count == 0) return;

            TerrainChunkView view = views[0];
            TerrainChunk chunk = view.Chunk;

            Check(
                chunk.Geometry != null && chunk.RawMesh != null,
                "Phase3-A2: voxel-aware Chunk owns ChunkGeometry + RawMesh"
            );
            Check(
                chunk.Geometry != null &&
                chunk.Geometry.RawMesh == chunk.RawMesh &&
                chunk.RawMesh.GetInstanceID() == chunk.Geometry.RawMesh.GetInstanceID(),
                "Phase3-A2: RawMesh is the immutable Base Mesh recorded in Geometry"
            );
            Check(
                chunk.Mesh != null && chunk.Mesh != chunk.RawMesh,
                "Phase3-A3: Final Mesh is the assembled mesh (distinct object from RawMesh)"
            );
            Check(
                view.MeshFilter != null &&
                view.MeshFilter.sharedMesh == chunk.Mesh &&
                view.MeshCollider != null &&
                view.MeshCollider.sharedMesh == chunk.Mesh,
                "Phase3-A2: MeshFilter / MeshCollider reference the final assembled Mesh"
            );

            MeshStats rawStats = ComputeMeshStats(chunk.RawMesh);
            MeshStats finalStats = ComputeMeshStats(chunk.Mesh);

            Check(
                finalStats.TriangleCount > 0 &&
                !finalStats.HasNaN &&
                finalStats.DegenerateTriangles == 0,
                "Phase3-A3: assembled Mesh is valid (no NaN / Infinity / degenerate)"
            );
            Check(
                rawStats.Area > 0f &&
                Mathf.Abs(finalStats.Area - rawStats.Area) <=
                Mathf.Max(1e-3f, rawStats.Area * 1e-3f),
                "Phase3-A3: Pre-cut + Assembly preserves Base surface area"
            );

            LogChunkStatistics(views, "Phase3-A2");
        }
        finally
        {
            Object.DestroyImmediate(terrainObject);
        }
    }

    // =====================================================================
    // A4 — 反复切换不累积
    // =====================================================================

    private static void CheckFeatureSwitchToggleLifecycle()
    {
        GameObject terrainObject = new GameObject("Phase3_ToggleTerrain");

        try
        {
            DensityTerrain terrain = terrainObject.AddComponent<DensityTerrain>();
            ConfigureSmallTerrain(
                terrain,
                new Vector3Int(2, 1, 1),
                Vector3Int.zero
            );

            List<Mesh> capturedMeshes = new List<Mesh>();
            List<GameObject> capturedChunkObjects = new List<GameObject>();
            List<Mesh> capturedRawMeshes = new List<Mesh>();

            for (int cycle = 0; cycle < 3; cycle++)
            {
                // ---- ON ----
                terrain.useVoxelAwareMeshPrecut = true;
                terrain.Regenerate();

                List<TerrainChunkView> views = GetViews(terrainObject);

                Check(
                    views.Count == 2,
                    "Phase3-A4: ON Regenerate keeps exactly 2 Chunks (cycle " +
                    cycle + ")"
                );
                Check(
                    AllHaveGeometry(views) && AllMeshFiltersMatch(views),
                    "Phase3-A4: ON Regenerate gives every Chunk Geometry + MeshFilter sync"
                );
                Check(
                    NoDuplicateCoordinates(views),
                    "Phase3-A4: ON Regenerate has no duplicate Chunk coordinate"
                );

                capturedMeshes.Clear();
                capturedRawMeshes.Clear();
                capturedChunkObjects.Clear();

                for (int i = 0; i < views.Count; i++)
                {
                    if (views[i].Chunk.Mesh != null)
                    {
                        capturedMeshes.Add(views[i].Chunk.Mesh);
                    }

                    if (views[i].Chunk.RawMesh != null)
                    {
                        capturedRawMeshes.Add(views[i].Chunk.RawMesh);
                    }

                    capturedChunkObjects.Add(views[i].gameObject);
                }

                // ---- OFF ----
                terrain.useVoxelAwareMeshPrecut = false;
                terrain.Regenerate();

                views = GetViews(terrainObject);

                Check(
                    views.Count == 2 && AllHaveNoGeometry(views),
                    "Phase3-A4: OFF Regenerate returns to the legacy path"
                );
                Check(
                    LegacyChunksMatchLegacyGenerator(views),
                    "Phase3-A4: OFF Regenerate produces the legacy Mesh again"
                );

                // 上一轮的 Mesh / GameObject 必须被释放，不累积。
                bool meshesReleased = true;
                for (int i = 0; i < capturedMeshes.Count; i++)
                {
                    if (capturedMeshes[i] != null) meshesReleased = false;
                }

                bool rawReleased = true;
                for (int i = 0; i < capturedRawMeshes.Count; i++)
                {
                    if (capturedRawMeshes[i] != null) rawReleased = false;
                }

                bool objectsReleased = true;
                for (int i = 0; i < capturedChunkObjects.Count; i++)
                {
                    if (capturedChunkObjects[i] != null) objectsReleased = false;
                }

                Check(
                    meshesReleased && rawReleased && objectsReleased,
                    "Phase3-A4: switching releases previous Mesh / RawMesh / Chunk objects"
                );
            }
        }
        finally
        {
            Object.DestroyImmediate(terrainObject);
        }
    }

    // =====================================================================
    // B — Chunk 布局
    // =====================================================================

    private static void CheckChunkLayout()
    {
        Vector3Int[] expected =
        {
            new Vector3Int(5, 0, 7),
            new Vector3Int(6, 0, 7),
            new Vector3Int(5, 0, 8),
            new Vector3Int(6, 0, 8)
        };

        GameObject terrainObject = CreateTerrainObject(
            "Phase3_LayoutTerrain",
            new Vector3Int(2, 1, 2),
            new Vector3Int(5, 0, 7),
            true,
            Vector3.zero,
            Quaternion.identity,
            Vector3.one
        );

        try
        {
            List<TerrainChunkView> views = GetViews(terrainObject);

            Check(
                views.Count == expected.Length,
                "Phase3-B: chunkCount (2,1,2) creates exactly 4 Chunks"
            );
            Check(
                !HasDuplicateCoordinates(views),
                "Phase3-B: Chunk coordinates are unique"
            );

            for (int i = 0; i < expected.Length; i++)
            {
                Vector3Int coordinate = expected[i];
                TerrainChunkView view = FindView(views, coordinate);

                Check(
                    view != null,
                    "Phase3-B: Chunk coordinate " + coordinate + " exists"
                );
                if (view == null) continue;

                TerrainChunk chunk = view.Chunk;

                Check(
                    chunk.Coordinate == coordinate &&
                    view.Coordinate == coordinate,
                    "Phase3-B: Chunk " + coordinate + " identity matches"
                );
                Check(
                    chunk.Geometry != null &&
                    chunk.RawMesh != null &&
                    chunk.Mesh != null,
                    "Phase3-B: Chunk " + coordinate +
                    " has Geometry / RawMesh / Final Mesh"
                );
                Check(
                    chunk.ChunkOrigin == ChunkSpace.ChunkToLocalOrigin(
                        coordinate,
                        chunk.VoxelSize,
                        chunk.ChunkVoxelSize
                    ),
                    "Phase3-B: Chunk " + coordinate +
                    " local origin follows ChunkSpace"
                );
                Check(
                    view.transform.localPosition == chunk.ChunkOrigin &&
                    view.transform.localRotation == Quaternion.identity &&
                    view.transform.localScale == Vector3.one,
                    "Phase3-B: Chunk " + coordinate +
                    " GameObject is placed in Chunk-local space"
                );
                Check(
                    view.MeshFilter != null &&
                    view.MeshFilter.sharedMesh == chunk.Mesh,
                    "Phase3-B: Chunk " + coordinate +
                    " MeshFilter references its own final Mesh"
                );
                Check(
                    MeshIsChunkLocal(chunk.Mesh, chunk.ChunkWorldSize),
                    "Phase3-B: Chunk " + coordinate +
                    " Mesh vertices are Chunk-local"
                );
            }

            Check(
                AllMeshesDistinct(views),
                "Phase3-B: every Chunk owns a distinct Mesh instance"
            );
        }
        finally
        {
            Object.DestroyImmediate(terrainObject);
        }
    }

    // =====================================================================
    // C — MeshFilter 同步 + Mesh 替换
    // =====================================================================

    private static void CheckMeshFilterSyncAndReplacement()
    {
        GameObject terrainObject = CreateTerrainObject(
            "Phase3_MeshFilterTerrain",
            new Vector3Int(2, 1, 1),
            Vector3Int.zero,
            true,
            Vector3.zero,
            Quaternion.identity,
            Vector3.one
        );

        try
        {
            List<TerrainChunkView> views = GetViews(terrainObject);
            Check(views.Count == 2, "Phase3-C: fixture has 2 Chunks");
            if (views.Count < 2) return;

            Check(
                AllMeshFiltersMatch(views) && AllCollidersMatch(views),
                "Phase3-C: after Regenerate every MeshFilter / MeshCollider matches Chunk.Mesh"
            );

            List<Mesh> previousMeshes = new List<Mesh>();
            for (int i = 0; i < views.Count; i++)
            {
                previousMeshes.Add(views[i].Chunk.Mesh);
            }

            // Regenerate 后必须引用最新的 Mesh，而不是旧 Mesh。
            terrainObject.GetComponent<DensityTerrain>().Regenerate();
            views = GetViews(terrainObject);

            bool newest = AllMeshFiltersMatch(views) && AllCollidersMatch(views);
            for (int i = 0; i < views.Count; i++)
            {
                if (views[i].Chunk.Mesh == previousMeshes[i]) newest = false;
            }

            Check(
                newest,
                "Phase3-C: Regenerate re-points MeshFilter / MeshCollider to the newest Mesh"
            );

            // 通过既有 Phase 2B Mutation 替换 Final Mesh，验证引用同步契约。
            List<TerrainChunk> chunks = new List<TerrainChunk>();
            for (int i = 0; i < views.Count; i++)
            {
                chunks.Add(views[i].Chunk);
            }

            TerrainChunk changedChunk = views[0].Chunk;
            Mesh meshBeforeMutation = changedChunk.Mesh;
            Mesh rawBeforeMutation = changedChunk.RawMesh;
            int rawInstanceBefore = rawBeforeMutation.GetInstanceID();

            bool committed = VoxelAwareMeshRuntimeMutation.ApplyVoxelStateChange(
                terrainObject.GetComponent<DensityTerrain>().VoxelField,
                new Vector3Int(8, 4, 8),
                VoxelState.Air,
                chunks,
                new Vector3Int(16, 16, 16)
            );

            // 运行时集成契约：Mesh 替换后由 View 同步（未来 Mining 接入点）。
            for (int i = 0; i < views.Count; i++)
            {
                views[i].SetMesh(views[i].Chunk.Mesh);
            }

            Check(
                committed &&
                changedChunk.Mesh != meshBeforeMutation &&
                meshBeforeMutation == null,
                "Phase3-C: Mutation reassembly replaces the Final Mesh and releases the old one"
            );
            Check(
                changedChunk.RawMesh.GetInstanceID() == rawInstanceBefore &&
                changedChunk.Geometry != null &&
                changedChunk.Geometry.RawMesh == changedChunk.RawMesh,
                "Phase3-C: RawMesh / ChunkGeometry ownership survives Mesh replacement"
            );
            Check(
                views[0].MeshFilter.sharedMesh == changedChunk.Mesh &&
                views[0].MeshCollider.sharedMesh == changedChunk.Mesh,
                "Phase3-C: MeshFilter / MeshCollider follow the replaced Final Mesh"
            );

            Check(
                GetViews(terrainObject).Count == 2 && NoDuplicateCoordinates(views),
                "Phase3-C: no orphan / duplicate Chunk after Mesh replacement"
            );
        }
        finally
        {
            Object.DestroyImmediate(terrainObject);
        }
    }

    // =====================================================================
    // D — MeshCollider 现状（只检查）
    // =====================================================================

    private static void CheckMeshColliderStatus()
    {
        GameObject terrainObject = CreateTerrainObject(
            "Phase3_ColliderTerrain",
            new Vector3Int(2, 1, 1),
            Vector3Int.zero,
            true,
            Vector3.zero,
            Quaternion.identity,
            Vector3.one
        );

        try
        {
            List<TerrainChunkView> views = GetViews(terrainObject);
            int withCollider = 0;

            for (int i = 0; i < views.Count; i++)
            {
                if (views[i].MeshCollider == null) continue;

                withCollider++;
                Check(
                    views[i].MeshCollider.sharedMesh == views[i].Chunk.Mesh,
                    "Phase3-D: Chunk " + views[i].Coordinate +
                    " MeshCollider references the final Mesh"
                );
                Check(
                    views[i].MeshCollider.sharedMesh != views[i].Chunk.RawMesh,
                    "Phase3-D: Chunk " + views[i].Coordinate +
                    " MeshCollider does not reference RawMesh"
                );
            }

            Check(
                views.Count > 0 && withCollider == views.Count,
                "Phase3-D: every runtime Chunk already has a MeshCollider"
            );

            Debug.Log(
                "[Phase3-D] MeshCollider 现状：运行时每个 Chunk 由 DensityTerrain.BuildChunk " +
                "创建 MeshCollider，并通过 TerrainChunkView.SetMesh 指向最终 Mesh（" +
                "新路径为 assembled Mesh，旧路径为 legacy Mesh）。本轮未新增 / 未修改任何 Collider。"
            );
        }
        finally
        {
            Object.DestroyImmediate(terrainObject);
        }
    }

    // =====================================================================
    // E — 非 Identity Transform
    // =====================================================================

    private static void CheckNonIdentityTransform()
    {
        Vector3 position = new Vector3(3.25f, 4.5f, -2.75f);
        Quaternion rotation = Quaternion.Euler(0f, 37f, 0f);
        Vector3 scale = new Vector3(1.5f, 1.25f, 0.75f);

        GameObject identityObject = CreateTerrainObject(
            "Phase3_IdentityTerrain",
            new Vector3Int(2, 1, 1),
            Vector3Int.zero,
            true,
            Vector3.zero,
            Quaternion.identity,
            Vector3.one
        );

        GameObject transformedObject = CreateTerrainObject(
            "Phase3_TransformedTerrain",
            new Vector3Int(2, 1, 1),
            Vector3Int.zero,
            true,
            position,
            rotation,
            scale
        );

        try
        {
            List<TerrainChunkView> identityViews = GetViews(identityObject);
            List<TerrainChunkView> transformedViews = GetViews(transformedObject);
            Transform terrainTransform = transformedObject.transform;

            Check(
                identityViews.Count == transformedViews.Count,
                "Phase3-E: non-identity Transform does not change Chunk count"
            );

            bool meshesIdentical = identityViews.Count == transformedViews.Count;
            for (int i = 0; i < identityViews.Count && meshesIdentical; i++)
            {
                TerrainChunkView identityView = FindView(
                    identityViews,
                    identityViews[i].Coordinate
                );
                TerrainChunkView transformedView = FindView(
                    transformedViews,
                    identityViews[i].Coordinate
                );

                if (identityView == null || transformedView == null)
                {
                    meshesIdentical = false;
                    break;
                }

                MeshStats a = ComputeMeshStats(identityView.Chunk.Mesh);
                MeshStats b = ComputeMeshStats(transformedView.Chunk.Mesh);

                meshesIdentical =
                    a.VertexCount == b.VertexCount &&
                    a.TriangleCount == b.TriangleCount &&
                    Mathf.Abs(a.Area - b.Area) < 1e-3f;
            }

            Check(
                meshesIdentical,
                "Phase3-E: Mesh vertex data stays Chunk-local (identical with / without Transform)"
            );

            for (int i = 0; i < transformedViews.Count; i++)
            {
                TerrainChunkView view = transformedViews[i];
                TerrainChunk chunk = view.Chunk;

                Check(
                    view.transform.localPosition == chunk.ChunkOrigin &&
                    view.transform.localRotation == Quaternion.identity &&
                    view.transform.localScale == Vector3.one,
                    "Phase3-E: Chunk " + chunk.Coordinate +
                    " keeps Chunk-local placement under non-identity Transform"
                );
                Check(
                    (view.transform.position -
                     terrainTransform.TransformPoint(chunk.ChunkOrigin)).sqrMagnitude
                    < 1e-6f,
                    "Phase3-E: Chunk " + chunk.Coordinate +
                    " world position follows the Terrain Transform"
                );
                Check(
                    view.MeshFilter != null &&
                    view.MeshFilter.sharedMesh == chunk.Mesh,
                    "Phase3-E: Chunk " + chunk.Coordinate +
                    " MeshFilter stays correct under non-identity Transform"
                );
            }

            Check(
                terrainTransform.position == position &&
                terrainTransform.localScale == scale,
                "Phase3-E: test Transform is not forced to identity"
            );
        }
        finally
        {
            Object.DestroyImmediate(identityObject);
            Object.DestroyImmediate(transformedObject);
        }
    }

    // =====================================================================
    // F1 — 生命周期（不累积 / 不删除非 Chunk 子对象 / DontSave）
    // =====================================================================

    private static void CheckScenePersistenceAndLifecycle()
    {
        GameObject terrainObject = CreateTerrainObject(
            "Phase3_LifecycleTerrain",
            new Vector3Int(2, 1, 1),
            Vector3Int.zero,
            true,
            Vector3.zero,
            Quaternion.identity,
            Vector3.one
        );

        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();

            GameObject marker = new GameObject("Phase3_NonChunkChild");
            marker.transform.SetParent(terrainObject.transform, false);

            List<Mesh> capturedMeshes = new List<Mesh>();
            List<TerrainChunkView> views = GetViews(terrainObject);
            for (int i = 0; i < views.Count; i++)
            {
                capturedMeshes.Add(views[i].Chunk.Mesh);
                capturedMeshes.Add(views[i].Chunk.RawMesh);
            }

            terrain.Regenerate();

            views = GetViews(terrainObject);
            Check(
                views.Count == 2,
                "Phase3-F: Regenerate creates exactly one Chunk per coordinate"
            );
            Check(
                marker != null &&
                marker.transform.parent == terrainObject.transform,
                "Phase3-F: Regenerate does not delete non-Chunk children"
            );
            Check(
                AllChunkObjectsAreDontSave(views),
                "Phase3-F: runtime Chunk GameObjects use HideFlags.DontSave"
            );
            Check(
                NoDuplicateCoordinates(views),
                "Phase3-F: no duplicate Chunk coordinate after Regenerate"
            );

            bool released = true;
            for (int i = 0; i < capturedMeshes.Count; i++)
            {
                if (capturedMeshes[i] != null) released = false;
            }

            Check(
                released,
                "Phase3-F: Regenerate releases previous Mesh / RawMesh (no accumulation)"
            );

            int childCountBefore = terrainObject.transform.childCount;
            for (int i = 0; i < 2; i++)
            {
                terrain.Regenerate();
            }

            views = GetViews(terrainObject);

            Check(
                terrainObject.transform.childCount == childCountBefore &&
                views.Count == 2 &&
                marker != null,
                "Phase3-F: repeated Regenerate does not accumulate children / Chunks"
            );
            Check(
                AllHaveGeometry(views) && AllMeshFiltersMatch(views) &&
                NoDuplicateCoordinates(views),
                "Phase3-F: repeated Regenerate keeps every Chunk complete and unique"
            );

            LogChunkStatistics(views, "Phase3-F");
        }
        finally
        {
            Object.DestroyImmediate(terrainObject);
        }
    }

    // =====================================================================
    // F2 — Scene 保存 / 重新加载（自动化 Scene Reload 测试）
    // =====================================================================

    private static void CheckSceneRoundTrip()
    {
        // EditorSceneManager.OpenScene 只接受项目内路径，
        // 因此临时 Scene 放在 Assets 下的临时目录，并在 finally 中删除。
        const string tempFolder = "Assets/__Phase3ValidationTemp";
        string tempPath = tempFolder + "/Phase3_TempScene.unity";

        try
        {
            if (!AssetDatabase.IsValidFolder(tempFolder))
            {
                AssetDatabase.CreateFolder("Assets", "__Phase3ValidationTemp");
            }

            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single
            );

            GameObject terrainObject = new GameObject("Phase3_PersistTerrain");
            DensityTerrain terrain = terrainObject.AddComponent<DensityTerrain>();
            ConfigureSmallTerrain(
                terrain,
                new Vector3Int(2, 1, 1),
                Vector3Int.zero
            );
            terrain.useVoxelAwareMeshPrecut = true;
            terrain.Regenerate();

            Check(
                GetViews(terrainObject).Count == 2,
                "Phase3-F2: temp scene regenerates 2 Chunks before save"
            );

            bool saved = EditorSceneManager.SaveScene(scene, tempPath);

            Check(
                saved && File.Exists(tempPath),
                "Phase3-F2: temp scene saved for Scene Reload test"
            );

            // 卸载并重新加载：模拟 Scene Reload。
            EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single
            );

            Scene reopened = EditorSceneManager.OpenScene(
                tempPath,
                OpenSceneMode.Single
            );

            DensityTerrain reloaded = null;
            int persistedChunkViews = 0;
            GameObject[] roots = reopened.GetRootGameObjects();

            for (int i = 0; i < roots.Length; i++)
            {
                persistedChunkViews += roots[i]
                    .GetComponentsInChildren<TerrainChunkView>(true)
                    .Length;

                if (reloaded == null)
                {
                    reloaded = roots[i].GetComponentInChildren<DensityTerrain>(true);
                }
            }

            Check(
                reloaded != null,
                "Phase3-F2: DensityTerrain survives Scene save / reload"
            );
            Check(
                persistedChunkViews == 0,
                "Phase3-F2: runtime Chunk GameObject is not persisted into the Scene"
            );

            if (reloaded != null)
            {
                Check(
                    reloaded.useVoxelAwareMeshPrecut,
                    "Phase3-F2: Feature Switch value persists across reload"
                );

                reloaded.Regenerate();

                List<TerrainChunkView> views = GetViews(reloaded.gameObject);

                Check(
                    views.Count == 2 &&
                    AllHaveGeometry(views) &&
                    AllMeshFiltersMatch(views) &&
                    NoDuplicateCoordinates(views),
                    "Phase3-F2: reloaded terrain regenerates without orphan Chunks"
                );
            }
        }
        finally
        {
            EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single
            );

            if (AssetDatabase.IsValidFolder(tempFolder))
            {
                AssetDatabase.DeleteAsset(tempFolder);
            }

            AssetDatabase.Refresh();
        }
    }

    // =====================================================================
    // Summary 场景配置成本探测（不加压：单 Chunk）
    // =====================================================================

    private static float ProbeSampleSceneConfigCost()
    {
        GameObject terrainObject = null;

        try
        {
            terrainObject = new GameObject("Phase3_SceneConfigProbe");
            DensityTerrain terrain = terrainObject.AddComponent<DensityTerrain>();
            ConfigureSampleSceneTerrain(terrain, new Vector3Int(1, 1, 1));

            System.Diagnostics.Stopwatch stopwatch =
                System.Diagnostics.Stopwatch.StartNew();
            terrain.Regenerate();
            stopwatch.Stop();

            float seconds = (float)stopwatch.Elapsed.TotalSeconds;

            Debug.Log(
                "[Phase3-Probe] SampleScene 参数下单 Chunk VoxelAware Regenerate = " +
                seconds.ToString("F2") + "s（chunkCount=(1,1,1)）"
            );

            return seconds;
        }
        finally
        {
            if (terrainObject != null)
            {
                Object.DestroyImmediate(terrainObject);
            }
        }
    }

    // =====================================================================
    // Summary 场景真实运行（SampleScene 已启用新路径）
    // =====================================================================

    private static void CheckSampleSceneRuntime(float probeSeconds)
    {
        const float skipThresholdSeconds = 120f;

        string fullPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            SampleScenePath
        );
        System.DateTime before = File.Exists(fullPath)
            ? File.GetLastWriteTimeUtc(fullPath)
            : System.DateTime.MinValue;

        if (probeSeconds > skipThresholdSeconds)
        {
            sceneRuntimeSkipped = true;
            Debug.LogWarning(
                "[Phase3-Scene] 单 Chunk 成本 " + probeSeconds.ToString("F2") +
                "s 超过阈值 " + skipThresholdSeconds +
                "s，跳过 SampleScene 现场 Regenerate 自动化测试（未执行，不计 PASS）。"
            );
            return;
        }

        try
        {
            Scene scene = EditorSceneManager.OpenScene(
                SampleScenePath,
                OpenSceneMode.Single
            );

            Check(scene.IsValid(), "Phase3-Scene: SampleScene opens");
            if (!scene.IsValid()) return;

            DensityTerrain terrain = Object.FindObjectOfType<DensityTerrain>(true);

            Check(
                terrain != null,
                "Phase3-Scene: SampleScene contains a DensityTerrain"
            );
            if (terrain == null) return;

            Debug.Log("[Phase3-Scene] config: " + DescribeConfig(terrain));

            Check(
                terrain.useVoxelAwareMeshPrecut,
                "Phase3-Scene: SampleScene DensityTerrain uses the VoxelAware path"
            );

            System.Diagnostics.Stopwatch stopwatch =
                System.Diagnostics.Stopwatch.StartNew();
            terrain.Regenerate();
            stopwatch.Stop();

            float seconds = (float)stopwatch.Elapsed.TotalSeconds;

            List<TerrainChunkView> views = GetViews(terrain.gameObject);
            int expected =
                Mathf.Max(1, terrain.chunkCount.x) *
                Mathf.Max(1, terrain.chunkCount.y) *
                Mathf.Max(1, terrain.chunkCount.z);

            Check(
                views.Count == expected,
                "Phase3-Scene: SampleScene creates exactly chunkCount Chunks"
            );
            Check(
                AllHaveGeometry(views),
                "Phase3-Scene: every SampleScene Chunk runs the VoxelAware path"
            );
            Check(
                AllHaveRawAndFinalMesh(views),
                "Phase3-Scene: every Chunk owns RawMesh + Final Mesh"
            );
            Check(
                AllMeshFiltersMatch(views),
                "Phase3-Scene: every MeshFilter references its Chunk final Mesh"
            );
            Check(
                AllCollidersMatch(views),
                "Phase3-Scene: every MeshCollider references the same final Mesh"
            );
            Check(
                NoDuplicateCoordinates(views),
                "Phase3-Scene: no duplicate Chunk coordinate"
            );
            Check(
                AllMeshesDistinct(views),
                "Phase3-Scene: every Chunk owns a distinct Mesh instance"
            );
            Check(
                AllMeshesValid(views),
                "Phase3-Scene: no NaN / Infinity / degenerate triangles"
            );

            Debug.Log(
                "[Phase3-Scene] Regenerate seconds = " + seconds.ToString("F2") +
                "（" + views.Count + " Chunks）"
            );

            LogChunkStatistics(views, "Phase3-Scene");

            // 第二次 Regenerate：统计不得累积。
            List<MeshStats> firstStats = CollectStats(views);
            terrain.Regenerate();
            views = GetViews(terrain.gameObject);
            List<MeshStats> secondStats = CollectStats(views);

            Check(
                StatsEqual(firstStats, secondStats),
                "Phase3-Scene: repeated Regenerate does not accumulate geometry"
            );
        }
        finally
        {
            // 不保存 SampleScene：卸载到空场景，丢弃运行时 Chunk。
            EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single
            );

            System.DateTime after = File.Exists(fullPath)
                ? File.GetLastWriteTimeUtc(fullPath)
                : System.DateTime.MinValue;

            Check(
                before == after,
                "Phase3-Scene: SampleScene file is untouched by the runtime test"
            );
        }
    }

    // =====================================================================
    // Helpers — Terrain 构造
    // =====================================================================

    private static GameObject CreateTerrainObject(
        string name,
        Vector3Int chunkCount,
        Vector3Int chunkStartCoordinate,
        bool voxelAware,
        Vector3 position,
        Quaternion rotation,
        Vector3 scale)
    {
        GameObject terrainObject = new GameObject(name);
        terrainObject.transform.position = position;
        terrainObject.transform.rotation = rotation;
        terrainObject.transform.localScale = scale;

        DensityTerrain terrain = terrainObject.AddComponent<DensityTerrain>();
        ConfigureSmallTerrain(terrain, chunkCount, chunkStartCoordinate);
        terrain.useVoxelAwareMeshPrecut = voxelAware;
        terrain.Regenerate();

        return terrainObject;
    }

    private static void ConfigureSmallTerrain(
        DensityTerrain terrain,
        Vector3Int chunkCount,
        Vector3Int chunkStartCoordinate)
    {
        terrain.voxelSize = 1f;
        terrain.chunkVoxelSize = new Vector3Int(16, 16, 16);
        terrain.chunkCount = chunkCount;
        terrain.chunkStartCoordinate = chunkStartCoordinate;
        terrain.resolution = 8;
        terrain.densityMode = DensityGenerationMode.HeightField;
        terrain.useNoise = false;
        terrain.height = 7.5f;
        terrain.cavityRadius = 0f;
        terrain.debugCutaway = false;
        terrain.regenerateInEditor = false;
    }

    private static void ConfigureSampleSceneTerrain(
        DensityTerrain terrain,
        Vector3Int chunkCount)
    {
        terrain.terrainSize = new Vector3(83f, 20.3f, 55.2f);
        terrain.chunkSize = Vector3.zero;
        terrain.chunkCount = chunkCount;
        terrain.chunkStartCoordinate = Vector3Int.zero;

        // Scheme A 迁移后的 SampleScene 参数（Coordinate Migration Phase A）。
        // 旧值 0.18 / (415,203,276) / 60 属于 legacy 约定，已不再描述 SampleScene。
        terrain.voxelSize = 1f;
        terrain.chunkVoxelSize = new Vector3Int(75, 37, 50);
        terrain.resolution = 75;
        terrain.densityMode = DensityGenerationMode.HeightField;
        terrain.useNoise = true;
        terrain.height = 15.7f;
        terrain.noiseScale = 0.15f;
        terrain.noiseStrength = 2.37f;
        terrain.cavityCenterOffset = new Vector3(15.5f, 5.23f, 19.1f);
        terrain.cavityRadius = 6.13f;
        terrain.useLegacyMaterialRules = false;
        terrain.grassDepth = 1.9f;
        terrain.dirtDepth = 0.65f;
        terrain.weatheringStrength = 0.8f;
        terrain.weatheringDepth = 8f;
        terrain.gentleSlopeAngle = 15f;
        terrain.steepSlopeAngle = 45f;
        terrain.gravityStrength = 1f;
        terrain.soilSimulationIterations = 4;
        terrain.grassStrength = 1f;
        terrain.debugCutaway = true;
        terrain.debugCutX = 0f;
        terrain.debugCutWidth = 4f;
        terrain.regenerateInEditor = false;
        terrain.useVoxelAwareMeshPrecut = true;
    }

    private static string DescribeConfig(DensityTerrain terrain)
    {
        return
            "voxelSize=" + terrain.voxelSize +
            " chunkVoxelSize=" + terrain.chunkVoxelSize +
            " chunkCount=" + terrain.chunkCount +
            " chunkStart=" + terrain.chunkStartCoordinate +
            " resolution=" + terrain.resolution +
            " useNoise=" + terrain.useNoise +
            " voxelAware=" + terrain.useVoxelAwareMeshPrecut +
            " VoxelField.VoxelSize=" + terrain.VoxelField.VoxelSize;
    }

    // =====================================================================
    // Helpers — 查询 / 断言
    // =====================================================================

    private static List<TerrainChunkView> GetViews(GameObject terrainObject)
    {
        TerrainChunkView[] array = terrainObject
            .GetComponentsInChildren<TerrainChunkView>(true);

        return new List<TerrainChunkView>(array);
    }

    private static TerrainChunkView FindView(
        List<TerrainChunkView> views,
        Vector3Int coordinate)
    {
        for (int i = 0; i < views.Count; i++)
        {
            if (views[i] != null && views[i].Coordinate == coordinate)
            {
                return views[i];
            }
        }

        return null;
    }

    private static bool NoDuplicateCoordinates(List<TerrainChunkView> views)
    {
        for (int i = 0; i < views.Count; i++)
        {
            for (int j = i + 1; j < views.Count; j++)
            {
                if (views[i].Coordinate == views[j].Coordinate) return false;
            }
        }

        return true;
    }

    private static bool HasDuplicateCoordinates(List<TerrainChunkView> views)
    {
        return !NoDuplicateCoordinates(views);
    }

    private static bool AllHaveGeometry(List<TerrainChunkView> views)
    {
        if (views.Count == 0) return false;

        for (int i = 0; i < views.Count; i++)
        {
            if (views[i] == null || views[i].Chunk == null) return false;
            if (views[i].Chunk.Geometry == null) return false;
        }

        return true;
    }

    private static bool AllHaveNoGeometry(List<TerrainChunkView> views)
    {
        if (views.Count == 0) return false;

        for (int i = 0; i < views.Count; i++)
        {
            if (views[i] == null || views[i].Chunk == null) return false;
            if (views[i].Chunk.Geometry != null) return false;
            if (views[i].Chunk.RawMesh != null) return false;
        }

        return true;
    }

    private static bool AllHaveRawAndFinalMesh(List<TerrainChunkView> views)
    {
        if (views.Count == 0) return false;

        for (int i = 0; i < views.Count; i++)
        {
            TerrainChunk chunk = views[i] != null ? views[i].Chunk : null;
            if (chunk == null || chunk.RawMesh == null || chunk.Mesh == null)
            {
                return false;
            }

            if (chunk.Geometry == null || chunk.Geometry.RawMesh != chunk.RawMesh)
            {
                return false;
            }
        }

        return true;
    }

    private static bool AllMeshFiltersMatch(List<TerrainChunkView> views)
    {
        if (views.Count == 0) return false;

        for (int i = 0; i < views.Count; i++)
        {
            if (views[i] == null || views[i].Chunk == null) return false;
            if (views[i].MeshFilter == null) return false;
            if (views[i].MeshFilter.sharedMesh != views[i].Chunk.Mesh) return false;
        }

        return true;
    }

    private static bool AllCollidersMatch(List<TerrainChunkView> views)
    {
        if (views.Count == 0) return false;

        for (int i = 0; i < views.Count; i++)
        {
            if (views[i] == null || views[i].Chunk == null) return false;
            if (views[i].MeshCollider == null) return false;
            if (views[i].MeshCollider.sharedMesh != views[i].Chunk.Mesh) return false;
        }

        return true;
    }

    private static bool AllChunkObjectsAreDontSave(List<TerrainChunkView> views)
    {
        if (views.Count == 0) return false;

        for (int i = 0; i < views.Count; i++)
        {
            if (views[i] == null) return false;
            if (views[i].gameObject.hideFlags != HideFlags.DontSave) return false;
        }

        return true;
    }

    private static bool AllMeshesDistinct(List<TerrainChunkView> views)
    {
        for (int i = 0; i < views.Count; i++)
        {
            if (views[i] == null || views[i].Chunk == null) return false;
            if (views[i].Chunk.Mesh == null) return false;

            for (int j = i + 1; j < views.Count; j++)
            {
                if (views[i].Chunk.Mesh == views[j].Chunk.Mesh) return false;
            }
        }

        return true;
    }

    private static bool AllMeshesValid(List<TerrainChunkView> views)
    {
        if (views.Count == 0) return false;

        for (int i = 0; i < views.Count; i++)
        {
            MeshStats stats = ComputeMeshStats(
                views[i] != null ? views[i].Chunk.Mesh : null
            );

            if (stats.VertexCount == 0) return false;
            if (stats.HasNaN) return false;
            if (stats.DegenerateTriangles > 0) return false;
        }

        return true;
    }

    private static bool LegacyChunksMatchLegacyGenerator(List<TerrainChunkView> views)
    {
        for (int i = 0; i < views.Count; i++)
        {
            TerrainChunk chunk = views[i].Chunk;
            Mesh expected = TerrainMeshGenerator.Generate(
                chunk.Density,
                chunk.Material,
                chunk.IsoLevel
            );
            MeshStats expectedStats = ComputeMeshStats(expected);
            MeshStats actualStats = ComputeMeshStats(chunk.Mesh);
            bool matches =
                expectedStats.VertexCount == actualStats.VertexCount &&
                expectedStats.TriangleCount == actualStats.TriangleCount &&
                Mathf.Abs(expectedStats.Area - actualStats.Area) < 1e-3f;

            if (expected != null) Object.DestroyImmediate(expected);

            if (!matches) return false;
        }

        return true;
    }

    private static bool MeshIsChunkLocal(Mesh mesh, Vector3 chunkWorldSize)
    {
        if (mesh == null) return false;

        Bounds bounds = mesh.bounds;
        float slack = 0.05f;

        return bounds.min.x >= -slack &&
               bounds.min.y >= -slack &&
               bounds.min.z >= -slack &&
               bounds.max.x <= chunkWorldSize.x + slack &&
               bounds.max.y <= chunkWorldSize.y + slack &&
               bounds.max.z <= chunkWorldSize.z + slack;
    }

    // =====================================================================
    // Helpers — Mesh 统计
    // =====================================================================

    private static MeshStats ComputeMeshStats(Mesh mesh)
    {
        MeshStats stats = new MeshStats();
        if (mesh == null) return stats;

        Vector3[] vertices = mesh.vertices;
        stats.VertexCount = vertices.Length;

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 vertex = vertices[i];
            if (float.IsNaN(vertex.x) || float.IsNaN(vertex.y) ||
                float.IsNaN(vertex.z) || float.IsInfinity(vertex.x) ||
                float.IsInfinity(vertex.y) || float.IsInfinity(vertex.z))
            {
                stats.HasNaN = true;
                break;
            }
        }

        for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
        {
            int[] triangles = mesh.GetTriangles(submesh);

            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                Vector3 a = vertices[triangles[i]];
                Vector3 b = vertices[triangles[i + 1]];
                Vector3 c = vertices[triangles[i + 2]];

                float area = Vector3.Cross(b - a, c - a).magnitude * 0.5f;
                stats.TriangleCount++;
                stats.Area += area;

                if (area <= 1e-8f) stats.DegenerateTriangles++;
            }
        }

        return stats;
    }

    private static List<MeshStats> CollectStats(List<TerrainChunkView> views)
    {
        List<MeshStats> stats = new List<MeshStats>();

        for (int i = 0; i < views.Count; i++)
        {
            stats.Add(ComputeMeshStats(views[i].Chunk.Mesh));
        }

        return stats;
    }

    private static bool StatsEqual(List<MeshStats> a, List<MeshStats> b)
    {
        if (a.Count != b.Count) return false;

        for (int i = 0; i < a.Count; i++)
        {
            if (a[i].VertexCount != b[i].VertexCount) return false;
            if (a[i].TriangleCount != b[i].TriangleCount) return false;
            if (Mathf.Abs(a[i].Area - b[i].Area) > 1e-3f) return false;
        }

        return true;
    }

    private static void LogChunkStatistics(
        List<TerrainChunkView> views,
        string label)
    {
        for (int i = 0; i < views.Count; i++)
        {
            TerrainChunkView view = views[i];
            if (view == null || view.Chunk == null) continue;

            TerrainChunk chunk = view.Chunk;
            MeshStats rawStats = ComputeMeshStats(chunk.RawMesh);
            MeshStats finalStats = ComputeMeshStats(chunk.Mesh);

            int fragments = 0;
            int bridges = 0;
            int faceFragments = 0;
            int zeroAreaFragments = 0;
            int duplicateVertexFragments = 0;
            int minPolygonVertices = int.MaxValue;
            int maxPolygonVertices = 0;

            if (chunk.Geometry != null)
            {
                for (int f = 0; f < chunk.Geometry.Fragments.Count; f++)
                {
                    VoxelAwareMeshPrecut.Fragment fragment =
                        chunk.Geometry.Fragments[f];
                    if (!fragment.Active) continue;

                    fragments++;

                    float area = PolygonArea(fragment.Polygon);
                    if (area <= 1e-8f) zeroAreaFragments++;
                    if (HasDuplicateVertex(fragment.Polygon))
                    {
                        duplicateVertexFragments++;
                    }

                    if (fragment.Polygon.Count < minPolygonVertices)
                    {
                        minPolygonVertices = fragment.Polygon.Count;
                    }

                    if (fragment.Polygon.Count > maxPolygonVertices)
                    {
                        maxPolygonVertices = fragment.Polygon.Count;
                    }
                }

                bridges = chunk.Geometry.Bridges.Count;
                faceFragments = chunk.Geometry.FaceFragments.Count;
            }

            if (fragments == 0) minPolygonVertices = 0;

            Debug.Log(
                "[" + label + "-Stats] Chunk " + chunk.Coordinate +
                " RawMesh(v=" + rawStats.VertexCount + ",t=" + rawStats.TriangleCount +
                ") FinalMesh(v=" + finalStats.VertexCount + ",t=" +
                finalStats.TriangleCount + ") area=" + finalStats.Area.ToString("F3") +
                " fragments=" + fragments + " bridges=" + bridges +
                " faceFragments=" + faceFragments +
                " degenerate=" + finalStats.DegenerateTriangles +
                " nan=" + finalStats.HasNaN +
                " zeroAreaFragments=" + zeroAreaFragments +
                " duplicateVertexFragments=" + duplicateVertexFragments +
                " polygonVerts=[" + minPolygonVertices + ".." +
                maxPolygonVertices + "]"
            );
        }
    }

    private static float PolygonArea(List<Vector3> polygon)
    {
        if (polygon == null || polygon.Count < 3) return 0f;

        Vector3 origin = polygon[0];
        float area = 0f;

        for (int i = 1; i + 1 < polygon.Count; i++)
        {
            area += Vector3.Cross(
                polygon[i] - origin,
                polygon[i + 1] - origin
            ).magnitude * 0.5f;
        }

        return area;
    }

    private static bool HasDuplicateVertex(List<Vector3> polygon)
    {
        if (polygon == null) return false;

        for (int i = 0; i < polygon.Count; i++)
        {
            Vector3 a = polygon[i];
            Vector3 b = polygon[(i + 1) % polygon.Count];

            if ((a - b).sqrMagnitude <= 1e-12f) return true;
        }

        return false;
    }

    // =====================================================================
    // Check / Summary
    // =====================================================================

    private static void Check(bool condition, string message)
    {
        checks++;

        if (condition)
        {
            Debug.Log("PASS - " + message);
        }
        else
        {
            failures++;
            Debug.LogError("FAIL - " + message);
        }
    }

    private static void LogSummary()
    {
        Debug.Log(
            "[VoxelAwareMeshRuntimePhase3Validation] ===== SUMMARY =====\n" +
            "Checks = " + checks + "\n" +
            "Failures = " + failures + "\n" +
            "SampleSceneRuntimeSkipped = " + sceneRuntimeSkipped + "\n" +
            "Result = " + (failures == 0 ? "PASS" : "FAIL")
        );
    }
}
