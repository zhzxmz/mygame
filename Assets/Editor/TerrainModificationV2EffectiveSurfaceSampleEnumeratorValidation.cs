using System.Collections.Generic;
using TerrainModificationV2;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Terrain Modification &amp; Mining Specification V2.0 Step 3C Validation。
/// 验证 Surface Sample Enumerator 的规则 Grid 坐标 / 边界 / State 语义。
/// 纯 C#，不创建 Scene / DensityField / Mesh / Raycast。
/// </summary>
public static class TerrainModificationV2EffectiveSurfaceSampleEnumeratorValidation
{
    private const float Epsilon = 0.00001f;

    private static int failures;
    private static int checks;

    [MenuItem("Tools/Terrain/Validate Terrain Modification V2 Surface Sample Enumerator")]
    public static void ValidateFromMenu()
    {
        ValidateAll();
        LogSummary();

        Debug.Log(
            "[TerrainModificationV2EffectiveSurfaceSampleEnumeratorValidation] Menu validation result = " +
            (failures == 0 ? "PASS" : "FAIL")
        );
    }

    /// <summary>Batchmode 入口：-executeMethod TerrainModificationV2EffectiveSurfaceSampleEnumeratorValidation.RunBatch。</summary>
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

        ValidateNoExcavation();
        ValidateSingleCellEnumeration();
        ValidateAdjacentCells();
        ValidateNegativeCell();
        ValidateSamplingRegionIsNotEditBounds();
        ValidateSharedBoundaryConsistency();
        ValidateRegionEnumerationMatchesCellEnumeration();
        ValidateIntegerGridCoordinates();
    }

    private static TerrainEffectiveSurfaceSampleEnumerator CreateEnumerator(
        TerrainEditLayer layer,
        int samplesPerEditCell = 2)
    {
        TerrainEditGrid grid = new TerrainEditGrid(1f);
        TerrainEffectiveTerrainQuery query =
            new TerrainEffectiveTerrainQuery(grid, layer);
        TerrainEffectiveSurfaceSampling sampling =
            new TerrainEffectiveSurfaceSampling(grid, query, samplesPerEditCell);

        return new TerrainEffectiveSurfaceSampleEnumerator(sampling);
    }

    private static void ValidateNoExcavation()
    {
        TerrainEditLayer layer = new TerrainEditLayer();
        TerrainEffectiveSurfaceSampleEnumerator enumerator =
            CreateEnumerator(layer);

        List<TerrainEffectiveSample> samples =
            enumerator.Enumerate(new Vector3Int(0, 0, 0));

        Check(samples.Count == 5 * 5 * 5, "No excavation: 5×5×5 samples for N=2");

        for (int i = 0; i < samples.Count; i++)
        {
            if (samples[i].State != TerrainEffectiveSampleState.Base)
            {
                Check(false, "No excavation: all samples should be Base");
                return;
            }
        }

        Check(true, "No excavation: all samples are Base");
    }

    private static void ValidateSingleCellEnumeration()
    {
        TerrainEditLayer layer = new TerrainEditLayer();
        layer.Add(CreateExcavate(1L, 1L, new Vector3Int(0, 0, 0)));

        TerrainEffectiveSurfaceSampleEnumerator enumerator =
            CreateEnumerator(layer);

        List<TerrainEffectiveSample> samples =
            enumerator.Enumerate(new Vector3Int(0, 0, 0));

        Check(samples.Count == 5 * 5 * 5, "Single cell: 5×5×5 samples for N=2");

        int excavated = CountByState(samples, TerrainEffectiveSampleState.Excavated);
        int baseSamples = CountByState(samples, TerrainEffectiveSampleState.Base);

        // 每个轴有 5 个 grid index：-1,-0.5? 实际 index -1..3。
        // Cell(0,0,0) 属于 [0,1)，每个轴包含 index 0,1。
        Check(excavated == 2 * 2 * 2, "Single cell: 8 samples inside [0,1) are Excavated");
        Check(baseSamples == 125 - 8, "Single cell: all other samples are Base");

        CheckSampleState(
            samples,
            new Vector3(0.5f, 0.5f, 0.5f),
            TerrainEffectiveSampleState.Excavated,
            "Single cell: (0.5,0.5,0.5) is Excavated");

        CheckSampleState(
            samples,
            new Vector3(1.5f, 0.5f, 0.5f),
            TerrainEffectiveSampleState.Base,
            "Single cell: (1.5,0.5,0.5) is Base");
    }

    private static void ValidateAdjacentCells()
    {
        TerrainEditLayer layer = new TerrainEditLayer();
        layer.Add(CreateExcavate(1L, 1L, new Vector3Int(0, 0, 0)));
        layer.Add(CreateExcavate(2L, 2L, new Vector3Int(1, 0, 0)));

        TerrainEffectiveSurfaceSampleEnumerator enumerator =
            CreateEnumerator(layer);

        List<TerrainEffectiveSample> samples =
            enumerator.Enumerate(new Vector3Int(0, 0, 0));

        CheckSampleState(
            samples,
            new Vector3(0.5f, 0.5f, 0.5f),
            TerrainEffectiveSampleState.Excavated,
            "Adjacent cells: first cell sample is Excavated");

        CheckSampleState(
            samples,
            new Vector3(1f, 0.5f, 0.5f),
            TerrainEffectiveSampleState.Excavated,
            "Adjacent cells: shared boundary belongs to Cell(1,0,0) and is Excavated");

        CheckSampleState(
            samples,
            new Vector3(1.5f, 0.5f, 0.5f),
            TerrainEffectiveSampleState.Excavated,
            "Adjacent cells: second cell interior is Excavated");
    }

    private static void ValidateNegativeCell()
    {
        TerrainEditLayer layer = new TerrainEditLayer();
        layer.Add(CreateExcavate(1L, 1L, new Vector3Int(-1, 0, 0)));

        TerrainEffectiveSurfaceSampleEnumerator enumerator =
            CreateEnumerator(layer);

        List<TerrainEffectiveSample> samples =
            enumerator.Enumerate(new Vector3Int(-1, 0, 0));

        CheckSampleState(
            samples,
            new Vector3(-1f, 0.5f, 0.5f),
            TerrainEffectiveSampleState.Excavated,
            "Negative cell: (-1,0.5,0.5) is Excavated");

        CheckSampleState(
            samples,
            new Vector3(-0.5f, 0.5f, 0.5f),
            TerrainEffectiveSampleState.Excavated,
            "Negative cell: (-0.5,0.5,0.5) is Excavated");

        CheckSampleState(
            samples,
            new Vector3(0f, 0.5f, 0.5f),
            TerrainEffectiveSampleState.Base,
            "Negative cell: 0.0 belongs to Cell(0,0,0), Base");

        CheckSampleState(
            samples,
            new Vector3(-1.5f, 0.5f, 0.5f),
            TerrainEffectiveSampleState.Base,
            "Negative cell: -1.5 outside cell, Base");
    }

    private static void ValidateSamplingRegionIsNotEditBounds()
    {
        TerrainEditLayer layer = new TerrainEditLayer();
        layer.Add(CreateExcavate(1L, 1L, new Vector3Int(0, 0, 0)));

        TerrainEffectiveSurfaceSampleEnumerator enumerator =
            CreateEnumerator(layer);

        List<TerrainEffectiveSample> samples =
            enumerator.Enumerate(new Vector3Int(0, 0, 0));

        // Sampling Region 是 [-0.5,1.5]^3，但 Edit Bounds 是 [0,1)^3。
        CheckSampleState(
            samples,
            new Vector3(-0.5f, 0.5f, 0.5f),
            TerrainEffectiveSampleState.Base,
            "Sampling region != edit bounds: x=-0.5 remains Base");

        CheckSampleState(
            samples,
            new Vector3(1f, 0.5f, 0.5f),
            TerrainEffectiveSampleState.Base,
            "Sampling region != edit bounds: x=1 belongs to adjacent cell and remains Base");

        CheckSampleState(
            samples,
            new Vector3(1.5f, 0.5f, 0.5f),
            TerrainEffectiveSampleState.Base,
            "Sampling region != edit bounds: x=1.5 remains Base");
    }

    private static void ValidateSharedBoundaryConsistency()
    {
        TerrainEditLayer layerOnlyA = new TerrainEditLayer();
        layerOnlyA.Add(CreateExcavate(1L, 1L, new Vector3Int(0, 0, 0)));

        TerrainEffectiveSurfaceSampleEnumerator enumeratorOnlyA =
            CreateEnumerator(layerOnlyA);

        List<TerrainEffectiveSample> samplesA =
            enumeratorOnlyA.Enumerate(new Vector3Int(0, 0, 0));
        List<TerrainEffectiveSample> samplesB =
            enumeratorOnlyA.Enumerate(new Vector3Int(1, 0, 0));

        TerrainEffectiveSampleState stateAt1FromA = FindState(
            samplesA,
            new Vector3(1f, 0.5f, 0.5f));
        TerrainEffectiveSampleState stateAt1FromB = FindState(
            samplesB,
            new Vector3(1f, 0.5f, 0.5f));

        Check(
            stateAt1FromA == TerrainEffectiveSampleState.Base &&
            stateAt1FromB == TerrainEffectiveSampleState.Base,
            "Shared boundary: x=1 is Base from both A and B when only A is excavated");

        TerrainEditLayer layerBoth = new TerrainEditLayer();
        layerBoth.Add(CreateExcavate(1L, 1L, new Vector3Int(0, 0, 0)));
        layerBoth.Add(CreateExcavate(2L, 2L, new Vector3Int(1, 0, 0)));

        TerrainEffectiveSurfaceSampleEnumerator enumeratorBoth =
            CreateEnumerator(layerBoth);

        List<TerrainEffectiveSample> bothA =
            enumeratorBoth.Enumerate(new Vector3Int(0, 0, 0));
        List<TerrainEffectiveSample> bothB =
            enumeratorBoth.Enumerate(new Vector3Int(1, 0, 0));

        Check(
            FindState(bothA, new Vector3(1f, 0.5f, 0.5f)) ==
                TerrainEffectiveSampleState.Excavated &&
            FindState(bothB, new Vector3(1f, 0.5f, 0.5f)) ==
                TerrainEffectiveSampleState.Excavated,
            "Shared boundary: x=1 is Excavated from both sides when both cells are excavated");
    }

    private static void ValidateRegionEnumerationMatchesCellEnumeration()
    {
        TerrainEditLayer layer = new TerrainEditLayer();
        layer.Add(CreateExcavate(1L, 1L, new Vector3Int(0, 0, 0)));

        TerrainEditGrid grid = new TerrainEditGrid(1f);
        TerrainEffectiveTerrainQuery query =
            new TerrainEffectiveTerrainQuery(grid, layer);
        TerrainEffectiveSurfaceSampling sampling =
            new TerrainEffectiveSurfaceSampling(grid, query, 2);
        TerrainEffectiveSurfaceSampleEnumerator enumerator =
            new TerrainEffectiveSurfaceSampleEnumerator(sampling);

        Vector3Int cell = new Vector3Int(0, 0, 0);
        TerrainEffectiveSampleRegion region =
            sampling.GetRequiredSamplingRegion(cell);

        List<TerrainEffectiveSample> byCell = enumerator.Enumerate(cell);
        List<TerrainEffectiveSample> byRegion = enumerator.Enumerate(region);

        Check(
            byCell.Count == byRegion.Count,
            "Region enumeration count equals cell enumeration count"
        );

        if (byCell.Count == byRegion.Count)
        {
            bool allMatch = true;
            for (int i = 0; i < byCell.Count; i++)
            {
                if (byCell[i].GridCoordinate != byRegion[i].GridCoordinate ||
                    Vector3.Distance(
                        byCell[i].TerrainLocalPosition,
                        byRegion[i].TerrainLocalPosition) > Epsilon ||
                    byCell[i].State != byRegion[i].State)
                {
                    allMatch = false;
                    break;
                }
            }

            Check(allMatch, "Region enumeration equals cell enumeration");
        }
    }

    private static void ValidateIntegerGridCoordinates()
    {
        TerrainEditLayer layer = new TerrainEditLayer();
        TerrainEffectiveSurfaceSampleEnumerator enumerator =
            CreateEnumerator(layer);

        List<TerrainEffectiveSample> samples =
            enumerator.Enumerate(new Vector3Int(0, 0, 0));

        TerrainEffectiveSample sample = FindSample(
            samples,
            new Vector3Int(-1, 0, 2));

        Check(
            Vector3.Distance(
                sample.TerrainLocalPosition,
                new Vector3(-0.5f, 0f, 1f)) < Epsilon,
            "Integer grid coordinate converts to exact position without drift"
        );
    }

    private static int CountByState(
        List<TerrainEffectiveSample> samples,
        TerrainEffectiveSampleState state)
    {
        int count = 0;
        for (int i = 0; i < samples.Count; i++)
        {
            if (samples[i].State == state)
            {
                count++;
            }
        }

        return count;
    }

    private static TerrainEffectiveSampleState FindState(
        List<TerrainEffectiveSample> samples,
        Vector3 position)
    {
        for (int i = 0; i < samples.Count; i++)
        {
            if (Vector3.Distance(
                samples[i].TerrainLocalPosition,
                position) < Epsilon)
            {
                return samples[i].State;
            }
        }

        return TerrainEffectiveSampleState.Base;
    }

    private static TerrainEffectiveSample FindSample(
        List<TerrainEffectiveSample> samples,
        Vector3Int gridCoordinate)
    {
        for (int i = 0; i < samples.Count; i++)
        {
            if (samples[i].GridCoordinate == gridCoordinate)
            {
                return samples[i];
            }
        }

        return samples[0];
    }

    private static void CheckSampleState(
        List<TerrainEffectiveSample> samples,
        Vector3 position,
        TerrainEffectiveSampleState expected,
        string message)
    {
        Check(FindState(samples, position) == expected, message);
    }

    private static TerrainEditRecord CreateExcavate(
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
            "[TerrainModificationV2EffectiveSurfaceSampleEnumeratorValidation] ===== SUMMARY =====\n" +
            "Checks = " + checks + "\n" +
            "Failures = " + failures + "\n" +
            "Result = " + (failures == 0 ? "PASS" : "FAIL")
        );
    }
}
