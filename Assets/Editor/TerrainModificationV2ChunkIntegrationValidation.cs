using System;
using System.Reflection;
using TerrainModificationV2;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Terrain Modification &amp; Mining Specification V2.0 Step 1 Chunk Integration Validation。
/// Step 1 只验证：
/// - DensityTerrain 仍持有 TerrainEditLayer
/// - Base Density 不被 EditLayer 查询污染
/// - 无 Edit 时 Rebuild 保持稳定
/// Effective Cell 修改与 Surface Sampling 属于后续 Step。
/// </summary>
public static class TerrainModificationV2ChunkIntegrationValidation
{
    private static int failures;
    private static int checks;

    private sealed class MeshSnapshot
    {
        public Vector3[] Vertices;
        public int[] Triangles;
    }

    [MenuItem("Tools/Terrain/Validate Terrain Modification V2 Chunk Integration")]
    public static void ValidateFromMenu()
    {
        ValidateAll();
        LogSummary();

        Debug.Log(
            "[TerrainModificationV2ChunkIntegrationValidation] Menu validation result = " +
            (failures == 0 ? "PASS" : "FAIL")
        );
    }

    /// <summary>Batchmode 入口：-executeMethod TerrainModificationV2ChunkIntegrationValidation.RunBatch。</summary>
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

        ValidateTerrainOwnsEditLayer();
        ValidateNoEditRebuild();
        ValidateBaseDensityNotPolluted();
    }

    private static void ValidateTerrainOwnsEditLayer()
    {
        DensityTerrain terrain = CreateTerrain(
            "Validation_V2_Step1_EditLayerOwnership"
        );

        try
        {
            Check(terrain != null, "EditLayer ownership: terrain created");
            if (terrain == null) return;

            Check(terrain.EditLayer != null, "EditLayer ownership: terrain owns EditLayer");
            Check(terrain.EditLayer.Count == 0, "EditLayer ownership: starts empty");
        }
        finally
        {
            DestroyTerrain(terrain);
        }
    }

    private static void ValidateNoEditRebuild()
    {
        DensityTerrain terrain = CreateTerrain(
            "Validation_V2_Step1_NoEditRebuild"
        );

        try
        {
            if (terrain == null ||
                !terrain.TryGetChunk(new Vector3Int(0, 0, 0), out TerrainChunk chunk) ||
                chunk == null)
            {
                Check(false, "No edit rebuild: terrain/chunk exists");
                return;
            }

            MeshSnapshot before = CaptureMesh(chunk.Mesh);

            chunk.MarkDirty();
            terrain.RebuildDirtyChunks();

            MeshSnapshot after = CaptureMesh(chunk.Mesh);

            Check(
                MeshSnapshotsEqual(before, after),
                "No edit rebuild: rebuild without edits produces identical Mesh"
            );
        }
        finally
        {
            DestroyTerrain(terrain);
        }
    }

    private static void ValidateBaseDensityNotPolluted()
    {
        DensityTerrain terrain = CreateTerrain(
            "Validation_V2_Step1_BasePure"
        );

        try
        {
            if (terrain == null ||
                !terrain.TryGetChunk(new Vector3Int(0, 0, 0), out TerrainChunk chunk) ||
                chunk == null)
            {
                Check(false, "Base purity: terrain/chunk exists");
                return;
            }

            float[,,] baseBefore = CopyDensity(chunk.Density);

            terrain.EditLayer.Add(new TerrainEditRecord(
                1L,
                terrain.EditLayer.NextSequence(),
                TerrainEditOperation.Excavate,
                new Vector3Int(10, 5, 3)
            ));

            chunk.MarkDirty();
            terrain.RebuildDirtyChunks();
            terrain.RebuildDirtyChunks();

            Check(
                DensityArraysEqual(baseBefore, CopyDensity(chunk.Density)),
                "Base purity: Base Density unchanged after Step 1 rebuilds"
            );
        }
        finally
        {
            DestroyTerrain(terrain);
        }
    }

    private static MeshSnapshot CaptureMesh(Mesh mesh)
    {
        MeshSnapshot snapshot = new MeshSnapshot();
        if (mesh == null)
        {
            snapshot.Vertices = new Vector3[0];
            snapshot.Triangles = new int[0];
            return snapshot;
        }

        snapshot.Vertices = mesh.vertices;
        snapshot.Triangles = mesh.triangles;
        return snapshot;
    }

    private static bool MeshSnapshotsEqual(MeshSnapshot a, MeshSnapshot b)
    {
        if (a.Vertices.Length != b.Vertices.Length ||
            a.Triangles.Length != b.Triangles.Length)
        {
            return false;
        }

        for (int i = 0; i < a.Vertices.Length; i++)
        {
            if (Vector3.Distance(a.Vertices[i], b.Vertices[i]) > 0.000001f)
            {
                return false;
            }
        }

        for (int i = 0; i < a.Triangles.Length; i++)
        {
            if (a.Triangles[i] != b.Triangles[i])
            {
                return false;
            }
        }

        return true;
    }

    private static float[,,] CopyDensity(DensityField field)
    {
        float[,,] copy = new float[
            field.SampleCountX,
            field.SampleCountY,
            field.SampleCountZ
        ];

        for (int x = 0; x < field.SampleCountX; x++)
        {
            for (int y = 0; y < field.SampleCountY; y++)
            {
                for (int z = 0; z < field.SampleCountZ; z++)
                {
                    copy[x, y, z] = field[x, y, z];
                }
            }
        }

        return copy;
    }

    private static bool DensityArraysEqual(float[,,] a, float[,,] b)
    {
        if (a.GetLength(0) != b.GetLength(0) ||
            a.GetLength(1) != b.GetLength(1) ||
            a.GetLength(2) != b.GetLength(2))
        {
            return false;
        }

        for (int x = 0; x < a.GetLength(0); x++)
        {
            for (int y = 0; y < a.GetLength(1); y++)
            {
                for (int z = 0; z < a.GetLength(2); z++)
                {
                    if (Mathf.Abs(a[x, y, z] - b[x, y, z]) > 0.0001f)
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private static DensityTerrain CreateTerrain(string name)
    {
        Type harness = typeof(TerrainMultiChunkValidation);
        MethodInfo method = harness.GetMethod(
            "CreateTerrain",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        if (method == null) return null;

        return method.Invoke(
            null,
            new object[]
            {
                name,
                DensityGenerationMode.HeightField,
                Vector3Int.zero
            }
        ) as DensityTerrain;
    }

    private static void DestroyTerrain(DensityTerrain terrain)
    {
        if (terrain == null) return;
        UnityEngine.Object.DestroyImmediate(terrain.gameObject);
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
            "[TerrainModificationV2ChunkIntegrationValidation] ===== SUMMARY =====\n" +
            "Checks = " + checks + "\n" +
            "Failures = " + failures + "\n" +
            "Result = " + (failures == 0 ? "PASS" : "FAIL")
        );
    }
}
