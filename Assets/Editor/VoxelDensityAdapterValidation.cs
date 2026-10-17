using System;
using TerrainModificationV2;
using UnityEditor;
using UnityEngine;

/// <summary>
/// VoxelDensityAdapter Validation。
/// 验证 VoxelField → DensityField 的 cell-centered mapping。
/// 纯 C#，不创建 Scene / Mesh / Raycast / TerrainChunk。
/// </summary>
public static class VoxelDensityAdapterValidation
{
    private const float Epsilon = 0.00001f;

    private static int failures;
    private static int checks;

    [MenuItem("Tools/Terrain/Validate Voxel Density Adapter")]
    public static void ValidateFromMenu()
    {
        ValidateAll();
        LogSummary();

        Debug.Log(
            "[VoxelDensityAdapterValidation] Menu validation result = " +
            (failures == 0 ? "PASS" : "FAIL")
        );
    }

    /// <summary>Batchmode 入口：-executeMethod VoxelDensityAdapterValidation.RunBatch。</summary>
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

        ValidateAllSolid();
        ValidateSingleAirVoxel();
        ValidateAdjacentAirVoxels();
        ValidateSolidAirSolidBoundary();
        ValidateNegativeCoordinates();
        ValidateNonUnitVoxelSize();
        ValidateSampleCentersMatchVoxels();
        ValidateInvalidArguments();
    }

    private static void ValidateAllSolid()
    {
        VoxelField voxelField = new VoxelField(1f);
        VoxelDensityAdapter adapter = new VoxelDensityAdapter();

        DensityField density = adapter.Build(
            voxelField,
            new Vector3(-1f, -1f, -1f),
            new Vector3(3f, 3f, 3f),
            3
        );

        Check(
            density.SampleCountX == 4 &&
            density.SampleCountY == 4 &&
            density.SampleCountZ == 4,
            "All solid: 4×4×4 samples for 3×3×3 voxel centers"
        );

        bool allSolid = true;
        for (int x = 0; x < density.SampleCountX; x++)
        {
            for (int y = 0; y < density.SampleCountY; y++)
            {
                for (int z = 0; z < density.SampleCountZ; z++)
                {
                    if (Mathf.Abs(
                        density[x, y, z] - VoxelDensityAdapter.SolidDensity
                    ) > Epsilon)
                    {
                        allSolid = false;
                    }
                }
            }
        }

        Check(allSolid, "All solid: every sample is SolidDensity");
    }

    private static void ValidateSingleAirVoxel()
    {
        VoxelField voxelField = new VoxelField(1f);
        voxelField.Set(new Vector3Int(0, 0, 0), VoxelState.Air);

        VoxelDensityAdapter adapter = new VoxelDensityAdapter();

        DensityField density = adapter.Build(
            voxelField,
            new Vector3(-1f, -1f, -1f),
            new Vector3(3f, 3f, 3f),
            3
        );

        // Voxel 中心位于 (0.5,0.5,0.5)，对应 sample index (1,1,1)。
        Check(
            Mathf.Abs(
                density[1, 1, 1] - VoxelDensityAdapter.AirDensity
            ) < Epsilon,
            "Single air voxel: its center sample is AirDensity"
        );

        int airCount = 0;
        int solidCount = 0;
        for (int x = 0; x < density.SampleCountX; x++)
        {
            for (int y = 0; y < density.SampleCountY; y++)
            {
                for (int z = 0; z < density.SampleCountZ; z++)
                {
                    if (density[x, y, z] <= 0f)
                    {
                        airCount++;
                    }
                    else
                    {
                        solidCount++;
                    }
                }
            }
        }

        Check(airCount == 1, "Single air voxel: exactly one Air sample");
        Check(solidCount == 64 - 1, "Single air voxel: all other samples Solid");
    }

    private static void ValidateAdjacentAirVoxels()
    {
        VoxelField voxelField = new VoxelField(1f);
        voxelField.Set(new Vector3Int(0, 0, 0), VoxelState.Air);
        voxelField.Set(new Vector3Int(1, 0, 0), VoxelState.Air);

        VoxelDensityAdapter adapter = new VoxelDensityAdapter();

        DensityField density = adapter.Build(
            voxelField,
            new Vector3(0f, 0f, 0f),
            new Vector3(2f, 2f, 2f),
            2
        );

        // Sample 网格：
        // (0.5,0.5,0.5) → Voxel(0,0,0)
        // (1.5,0.5,0.5) → Voxel(1,0,0)
        Check(
            Mathf.Abs(
                density[0, 0, 0] - VoxelDensityAdapter.AirDensity
            ) < Epsilon,
            "Adjacent air: Voxel(0,0,0) center is Air"
        );
        Check(
            Mathf.Abs(
                density[1, 0, 0] - VoxelDensityAdapter.AirDensity
            ) < Epsilon,
            "Adjacent air: Voxel(1,0,0) center is Air"
        );
        Check(
            density[0, 0, 0] <= 0f && density[1, 0, 0] <= 0f,
            "Adjacent air: no false Solid sample between adjacent Air voxels"
        );
    }

    private static void ValidateSolidAirSolidBoundary()
    {
        VoxelField voxelField = new VoxelField(1f);
        voxelField.Set(new Vector3Int(-1, 0, 0), VoxelState.Solid);
        voxelField.Set(new Vector3Int(0, 0, 0), VoxelState.Air);
        voxelField.Set(new Vector3Int(1, 0, 0), VoxelState.Solid);

        VoxelDensityAdapter adapter = new VoxelDensityAdapter();

        DensityField density = adapter.Build(
            voxelField,
            new Vector3(-1f, 0f, 0f),
            new Vector3(3f, 3f, 3f),
            3
        );

        // Centers: -0.5 Solid, 0.5 Air, 1.5 Solid。
        Check(
            density[0, 0, 0] > 0f,
            "Solid|Air|Solid: left voxel center is Solid"
        );
        Check(
            density[1, 0, 0] < 0f,
            "Solid|Air|Solid: middle voxel center is Air"
        );
        Check(
            density[2, 0, 0] > 0f,
            "Solid|Air|Solid: right voxel center is Solid"
        );

        // 等幅 +1 / -1 的线性插值在 0 处穿过，正好是共享 Voxel 面。
        float leftBoundary = ComputeZeroCrossing(
            density.GetLocalPosition(0, 0, 0),
            density[0, 0, 0],
            density.GetLocalPosition(1, 0, 0),
            density[1, 0, 0]
        );
        float rightBoundary = ComputeZeroCrossing(
            density.GetLocalPosition(1, 0, 0),
            density[1, 0, 0],
            density.GetLocalPosition(2, 0, 0),
            density[2, 0, 0]
        );

        Check(
            Mathf.Abs(leftBoundary - 0f) < Epsilon,
            "Solid|Air|Solid: zero crossing at x=0"
        );
        Check(
            Mathf.Abs(rightBoundary - 1f) < Epsilon,
            "Solid|Air|Solid: zero crossing at x=1"
        );
    }

    private static void ValidateNegativeCoordinates()
    {
        VoxelField voxelField = new VoxelField(1f);
        voxelField.Set(new Vector3Int(-2, 0, 0), VoxelState.Air);
        voxelField.Set(new Vector3Int(-1, 0, 0), VoxelState.Solid);

        VoxelDensityAdapter adapter = new VoxelDensityAdapter();

        DensityField density = adapter.Build(
            voxelField,
            new Vector3(-2f, 0f, 0f),
            new Vector3(2f, 2f, 2f),
            2
        );

        // Centers: -1.5 → Voxel(-2), -0.5 → Voxel(-1)。
        Check(
            density[0, 0, 0] < 0f,
            "Negative: Voxel(-2,0,0) center is Air"
        );
        Check(
            density[1, 0, 0] > 0f,
            "Negative: Voxel(-1,0,0) center is Solid"
        );
    }

    private static void ValidateNonUnitVoxelSize()
    {
        VoxelField voxelField = new VoxelField(2f);
        voxelField.Set(new Vector3Int(0, 0, 0), VoxelState.Air);
        voxelField.Set(new Vector3Int(1, 0, 0), VoxelState.Solid);

        VoxelDensityAdapter adapter = new VoxelDensityAdapter();

        DensityField density = adapter.Build(
            voxelField,
            new Vector3(0f, 0f, 0f),
            new Vector3(4f, 4f, 4f),
            2
        );

        // Voxel(0) center = (1,1,1); Voxel(1) center = (3,1,1)。
        Check(
            Mathf.Abs(density[0, 0, 0] - VoxelDensityAdapter.AirDensity) < Epsilon,
            "VoxelSize 2: first voxel center is Air"
        );
        Check(
            Mathf.Abs(density[1, 0, 0] - VoxelDensityAdapter.SolidDensity) < Epsilon,
            "VoxelSize 2: second voxel center is Solid"
        );
    }

    private static void ValidateSampleCentersMatchVoxels()
    {
        VoxelField voxelField = new VoxelField(1f);
        voxelField.Set(new Vector3Int(0, 0, 0), VoxelState.Air);

        VoxelDensityAdapter adapter = new VoxelDensityAdapter();

        DensityField density = adapter.Build(
            voxelField,
            new Vector3(-2f, -2f, -2f),
            new Vector3(4f, 4f, 4f),
            4
        );

        bool allCentersExact = true;
        bool statesMatch = true;

        for (int x = 0; x < density.SampleCountX; x++)
        {
            for (int y = 0; y < density.SampleCountY; y++)
            {
                for (int z = 0; z < density.SampleCountZ; z++)
                {
                    Vector3 samplePosition = density.GetLocalPosition(x, y, z);
                    Vector3Int voxel = voxelField.PositionToVoxel(samplePosition);
                    Vector3 min = voxelField.GetVoxelMin(voxel);
                    Vector3 center = min + Vector3.one * 0.5f;

                    if (Vector3.Distance(samplePosition, center) > Epsilon)
                    {
                        allCentersExact = false;
                    }

                    float expected = voxelField.IsAir(voxel)
                        ? VoxelDensityAdapter.AirDensity
                        : VoxelDensityAdapter.SolidDensity;

                    if (Mathf.Abs(density[x, y, z] - expected) > Epsilon)
                    {
                        statesMatch = false;
                    }
                }
            }
        }

        Check(allCentersExact, "Sample positions are exact Voxel centers");
        Check(statesMatch, "Every sample state matches its containing Voxel");
    }

    private static void ValidateInvalidArguments()
    {
        VoxelDensityAdapter adapter = new VoxelDensityAdapter();

        Check(
            ThrowsArgumentNull(() => adapter.Build(
                null,
                Vector3.zero,
                Vector3.one,
                1)),
            "Null VoxelField throws ArgumentNullException"
        );

        Check(
            ThrowsArgumentOutOfRange(() => adapter.Build(
                new VoxelField(1f),
                Vector3.zero,
                Vector3.one,
                0)),
            "Resolution 0 throws ArgumentOutOfRangeException"
        );

        Check(
            ThrowsArgumentException(() => adapter.Build(
                new VoxelField(1f),
                Vector3.zero,
                new Vector3(2f, 1f, 1f),
                1)),
            "Mismatched size/resolution throws ArgumentException"
        );
    }

    private static float ComputeZeroCrossing(
        Vector3 a,
        float densityA,
        Vector3 b,
        float densityB)
    {
        float denominator = densityB - densityA;
        if (Mathf.Abs(denominator) < Epsilon)
        {
            return float.NaN;
        }

        float t = -densityA / denominator;
        return Mathf.Lerp(a.x, b.x, t);
    }

    private static bool ThrowsArgumentNull(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (ArgumentNullException)
        {
            return true;
        }
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

    private static bool ThrowsArgumentException(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (ArgumentException)
        {
            return true;
        }
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
            "[VoxelDensityAdapterValidation] ===== SUMMARY =====\n" +
            "Checks = " + checks + "\n" +
            "Failures = " + failures + "\n" +
            "Result = " + (failures == 0 ? "PASS" : "FAIL")
        );
    }
}
