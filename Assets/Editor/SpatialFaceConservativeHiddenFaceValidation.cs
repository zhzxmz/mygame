using System.Collections.Generic;
using TerrainModificationV2;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 验证补丁：SpatialFaceDebugSettings.ConservativeHiddenFace
///
/// 目标（仅验证，不改动 Partition / Fragment / MeshAssembler）：
///   1. 开关关闭（原逻辑）⇒ 已确认的缺面案例在夹具中可复现；
///   2. 开关打开（Mixed/Uncertain ⇒ 保守生成 HiddenFace）⇒ 同一案例的面恢复；
///   3. 没有 Mixed/Uncertain 的普通场景下，开关打开/关闭结果**完全一致**
///      （证明“保留原逻辑”）。
///
/// 案例：Terrain 同场景参数（HeightField + noise、height=15.7、75/37/50），
///       挖掘 Voxel=(26,16,49) 后的 +X 面（FaceKey=0:27:16:49）。
/// </summary>
public static class SpatialFaceConservativeHiddenFaceValidation
{
    private const int ChunkVoxelX = 75;
    private const int ChunkVoxelY = 37;
    private const int ChunkVoxelZ = 50;
    private const float NoiseHeight = 15.7f;
    private const float NoiseScale = 0.15f;
    private const float NoiseStrength = 2.37f;

    private static readonly Vector3Int ReproVoxel = new Vector3Int(26, 16, 49);
    private const string ReproFaceDirection = "+X";

    private static int checks;
    private static int failures;

    [MenuItem("Tools/Terrain/Debug/Toggle Conservative HiddenFace")]
    public static void ToggleConservativeHiddenFace()
    {
        SpatialFaceDebugSettings.ConservativeHiddenFace =
            !SpatialFaceDebugSettings.ConservativeHiddenFace;

        Debug.Log(
            "[SpatialFaceDebugSettings] ConservativeHiddenFace = " +
            SpatialFaceDebugSettings.ConservativeHiddenFace +
            "（true=Mixed/Uncertain 区域保守生成 HiddenFace）"
        );
    }

    /// <summary>Batchmode 入口：-executeMethod SpatialFaceConservativeHiddenFaceValidation.RunBatch。</summary>
    public static void RunBatch()
    {
        ValidateAll();

        Debug.Log(
            "[SpatialFaceConservativeHiddenFaceValidation] ===== SUMMARY =====\n" +
            "Checks = " + checks + "\n" +
            "Failures = " + failures + "\n" +
            "Result = " + (failures == 0 ? "PASS" : "FAIL")
        );

        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    private static void ValidateAll()
    {
        checks = 0;
        failures = 0;

        bool restore = SpatialFaceDebugSettings.ConservativeHiddenFace;

        try
        {
            ValidateCase(false, true, "开关关闭（原逻辑）");
            ValidateCase(true, false, "开关打开（保守生成）");
            ValidateNoMixedRegionNeutrality();
        }
        finally
        {
            SpatialFaceDebugSettings.ConservativeHiddenFace = restore;
        }
    }

    // =====================================================================
    // 案例 1/2：真实缺面案例在 关闭/打开 两种模式下的对比
    // =====================================================================

    private static void ValidateCase(
        bool conservative,
        bool expectMissing,
        string label)
    {
        SpatialFaceDebugSettings.ConservativeHiddenFace = conservative;

        GameObject terrainObject = CreateSceneLikeFixture(
            "SpatialFaceConservative_" + conservative);

        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();
            VoxelField field = terrain.VoxelField;

            MiningSurfaceDiagnosticValidation.MiningSurfaceDiagnosticReport before =
                MiningSurfaceDiagnosticValidation.Diagnose(terrain, ReproVoxel);

            Check(
                field.Get(ReproVoxel) == VoxelState.Solid &&
                before.HasMaterialInVoxel,
                label + "：用例成立（Voxel " + ReproVoxel + " 初始为 Solid 且含材料，" +
                "实体样本 " + before.PartialSamplesSolid + "/" +
                before.PartialSamplesTotal + "）"
            );

            field.Set(ReproVoxel, VoxelState.Air);
            int rebuilt = terrain.RebuildChunksAffectedByVoxel(ReproVoxel);

            Check(
                rebuilt >= 1 &&
                VoxelAwareMeshRuntimeMutation.LastMutationCommitted,
                label + "：挖掘已提交（rebuilt=" + rebuilt + "）"
            );

            MiningSurfaceDiagnosticValidation.MiningSurfaceDiagnosticReport after =
                MiningSurfaceDiagnosticValidation.Diagnose(terrain, ReproVoxel);

            MiningSurfaceDiagnosticValidation.FaceDiagnostic face = FindFace(after, ReproFaceDirection);

            if (face == null)
            {
                Check(false, label + "：未找到 " + ReproFaceDirection + " 面诊断");
                return;
            }

            bool missing = face.ExpectsSurface && !face.SurfaceFoundStrict;

            Check(
                face.ExpectsSurface,
                label + "：" + ReproFaceDirection + " 面应该有表面（面内侧密度 " +
                face.SolidSideProbeDensity.ToString("F4") + "）"
            );

            Check(
                missing == expectMissing,
                label + "：" + ReproFaceDirection + " 面缺失=" + missing +
                "（期望 " + expectMissing + "）；FaceKey=" + face.FaceKey +
                " meshStrict=" + face.SurfaceFoundStrict +
                " meshTolerant=" + face.SurfaceFoundTolerant +
                " hiddenContribution=" + face.ContributionFragmentCount +
                " coplanarFragments=" + face.CoplanarFragmentCount +
                " 覆写true/false=" + face.OverrideTrueCount + "/" +
                face.OverrideFalseCount
            );

            Debug.Log(
                "[ConservativeHiddenFace 验证] " + label +
                "：FaceKey=" + face.FaceKey +
                " expectsSurface=" + face.ExpectsSurface +
                " meshStrict=" + face.SurfaceFoundStrict +
                " hiddenContribution=" + face.ContributionFragmentCount +
                " 三角形总数=" + TotalTriangles(terrainObject)
            );
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    // =====================================================================
    // 案例 3：无 Mixed/Uncertain 区域时，开关不影响结果（保留原逻辑）
    // =====================================================================

    private static void ValidateNoMixedRegionNeutrality()
    {
        // 平地夹具（height 15、noNoise、表面恰好落在 Voxel 平面）⇒ 无 Mixed 区域。
        string offCanonical;
        string onCanonical;

        SpatialFaceDebugSettings.ConservativeHiddenFace = false;
        offCanonical = DigAndCanonical(false, out int offTriangles);

        SpatialFaceDebugSettings.ConservativeHiddenFace = true;
        onCanonical = DigAndCanonical(true, out int onTriangles);

        Check(
            offCanonical == onCanonical,
            "无 Mixed 区域场景：开关 关闭/打开 结果一致（off=" + offTriangles +
            " 三角形, on=" + onTriangles + " 三角形）⇒ 原逻辑保留"
        );
    }

    private static string DigAndCanonical(bool conservative, out int triangles)
    {
        GameObject terrainObject = CreateFlatFixture(
            "SpatialFaceConservative_Flat_" + conservative);

        try
        {
            DensityTerrain terrain = terrainObject.GetComponent<DensityTerrain>();
            Vector3Int voxel = new Vector3Int(30, 14, 30);

            terrain.VoxelField.Set(voxel, VoxelState.Air);
            terrain.RebuildChunksAffectedByVoxel(voxel);

            triangles = TotalTriangles(terrainObject);

            List<TerrainChunkView> views = new List<TerrainChunkView>(
                terrainObject.GetComponentsInChildren<TerrainChunkView>(true));

            List<string> parts = new List<string>();

            for (int i = 0; i < views.Count; i++)
            {
                TerrainChunk chunk = views[i].Chunk;
                Mesh mesh = chunk != null ? chunk.Mesh : null;

                if (mesh == null)
                {
                    parts.Add(chunk != null ? chunk.Coordinate + ":null" : "null");
                    continue;
                }

                Vector3[] vertices = mesh.vertices;
                float sumX = 0f, sumY = 0f, sumZ = 0f;

                for (int v = 0; v < vertices.Length; v++)
                {
                    sumX += vertices[v].x;
                    sumY += vertices[v].y;
                    sumZ += vertices[v].z;
                }

                parts.Add(
                    (chunk != null ? chunk.Coordinate.ToString() : "?") +
                    ":" + mesh.vertexCount + ":" + mesh.triangles.Length +
                    ":" + sumX.ToString("F3") + ":" + sumY.ToString("F3") +
                    ":" + sumZ.ToString("F3")
                );
            }

            parts.Sort();

            return string.Join("|", parts);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(terrainObject);
        }
    }

    // =====================================================================
    // 辅助
    // =====================================================================

    private static MiningSurfaceDiagnosticValidation.FaceDiagnostic FindFace(
        MiningSurfaceDiagnosticValidation.MiningSurfaceDiagnosticReport report,
        string direction)
    {
        if (report == null || report.Faces == null) return null;

        for (int i = 0; i < report.Faces.Count; i++)
        {
            if (report.Faces[i].Direction == direction) return report.Faces[i];
        }

        return null;
    }

    private static int TotalTriangles(GameObject terrainObject)
    {
        TerrainChunkView[] views =
            terrainObject.GetComponentsInChildren<TerrainChunkView>(true);

        int total = 0;

        for (int i = 0; i < views.Length; i++)
        {
            Mesh mesh = views[i] != null && views[i].Chunk != null
                ? views[i].Chunk.Mesh
                : null;

            if (mesh != null) total += mesh.triangles.Length / 3;
        }

        return total;
    }

    private static GameObject CreateSceneLikeFixture(string name)
    {
        GameObject terrainObject = new GameObject(name);
        DensityTerrain terrain = terrainObject.AddComponent<DensityTerrain>();

        terrain.voxelSize = 1f;
        terrain.chunkVoxelSize = new Vector3Int(
            ChunkVoxelX, ChunkVoxelY, ChunkVoxelZ);
        terrain.chunkCount = new Vector3Int(2, 1, 2);
        terrain.chunkStartCoordinate = Vector3Int.zero;
        terrain.resolution = 75;
        terrain.densityMode = DensityGenerationMode.HeightField;
        terrain.useNoise = true;
        terrain.height = NoiseHeight;
        terrain.noiseScale = NoiseScale;
        terrain.noiseStrength = NoiseStrength;
        terrain.regenerateInEditor = false;
        terrain.useVoxelAwareMeshPrecut = true;

        terrain.Regenerate();

        return terrainObject;
    }

    private static GameObject CreateFlatFixture(string name)
    {
        GameObject terrainObject = new GameObject(name);
        DensityTerrain terrain = terrainObject.AddComponent<DensityTerrain>();

        terrain.voxelSize = 1f;
        terrain.chunkVoxelSize = new Vector3Int(
            ChunkVoxelX, ChunkVoxelY, ChunkVoxelZ);
        terrain.chunkCount = new Vector3Int(2, 1, 2);
        terrain.chunkStartCoordinate = Vector3Int.zero;
        terrain.resolution = 75;
        terrain.densityMode = DensityGenerationMode.HeightField;
        terrain.useNoise = false;
        terrain.height = 15f;
        terrain.regenerateInEditor = false;
        terrain.useVoxelAwareMeshPrecut = true;

        terrain.Regenerate();

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
}
