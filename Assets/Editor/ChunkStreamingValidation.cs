using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VoxelWorld;
using VoxelWorld.Adapters;
using VoxelWorld.Generation;
using VoxelWorld.Mesh;
using VoxelWorld.Runtime;
using VoxelWorld.Runtime.Abstractions;
using VoxelWorld.Runtime.Streaming;
using VoxelWorld.View;

// 命名空间 VoxelWorld 内存在同名类型 VoxelWorld.VoxelWorld ⇒ 用别名消除解析歧义
using VoxelWorldData = VoxelWorld.VoxelWorld;

/// <summary>
/// Chunk Streaming Phase 1 验证：Player Position → ChunkCoordinate → Request/Release → Runtime Tick。
///
/// 用法：
///   · Editor 菜单：Tools/VoxelWorld/Validate Chunk Streaming
///   · 批处理：-executeMethod ChunkStreamingValidation.RunBatch
///
/// 分组：
///   A 组（纯 C#，无 GameObject）：焦点换算 / 请求集合 / 滞回 / 释放 / 抖动 / 垂直层 / 确定性 / 结构扫描；
///   B 组（Unity 侧）：真实 Bootstrap 组装点 + 真实 Core/Mesher/View 端到端；
///   C 组：既有文件 SHA 不变（含上一轮 Runtime Core 的全部文件与规范）。
///
/// 本文件是 **Editor 批处理验证**，允许使用具体实现来制造输入与参照（被扫描的禁令只针对
/// Runtime/Streaming 与组装点源码）。
/// </summary>
public static class ChunkStreamingValidation
{
    private static int checks;
    private static int failures;

    private static Material[] sharedMaterials;

    private const string StreamingFolder = "Scripts/VoxelWorld/Runtime/Streaming";
    private const string BootstrapPath = "Scripts/Game/VoxelWorldStreamingBootstrap.cs";

    [MenuItem("Tools/VoxelWorld/Validate Chunk Streaming")]
    public static void ValidateFromMenu()
    {
        ValidateAll();

        Debug.Log("[ChunkStreamingValidation] Menu validation result = " +
                  (failures == 0 ? "PASS" : "FAIL"));
    }

    public static void RunBatch()
    {
        ValidateAll();

        Debug.Log(
            "[ChunkStreamingValidation] ===== SUMMARY =====\n" +
            "Checks = " + checks + "\n" +
            "Failures = " + failures + "\n" +
            "Result = " + (failures == 0 ? "PASS" : "FAIL"));

        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    private static void ValidateAll()
    {
        checks = 0;
        failures = 0;

        EnsureEditableScene();
        CreateSharedMaterials();

        try
        {
            Run("CS1 位置 → Chunk 坐标换算（含负坐标）", ValidatePositionToChunkCoordinate);
            Run("CS2 请求集合 == 期望加载集合", ValidateDesiredSet);
            Run("CS3 滞回：R_unload 内不释放", ValidateHysteresis);
            Run("CS4 远移立即释放（最远先释放）", ValidateTeleportRelease);
            Run("CS5 抖动无反复加载（同 Chunk 内移动）", ValidateJitterWithoutChurn);
            Run("CS6 垂直层加载范围", ValidateVerticalRange);
            Run("CS7 与 Core 集成：预算分帧至全部 Ready", ValidateCoreIntegration);
            Run("CS8 配置不一致 ⇒ 拒绝可见", ValidateMismatchVisible);
            Run("CS9 确定性（同路径两次运行一致）", ValidateDeterminism);
            Run("CS10 结构扫描（流式层纯 C# + 组装点边界）", ValidateStructuralScan);
            Run("CS11 Unity 侧 Bootstrap 端到端", ValidateBootstrapEndToEnd);
            Run("CS12 既有文件 SHA 不变", ValidateExistingFilesUnchanged);
        }
        finally
        {
            DestroySharedMaterials();
        }
    }

    // =====================================================================
    // CS1：位置 → Chunk 坐标（必须与 VoxelCore 换算完全一致）
    // =====================================================================

    private static void ValidatePositionToChunkCoordinate()
    {
        Vector3[] positions =
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(0.2f, 20.5f, 0.9f),
            new Vector3(15.99f, 5f, 15.99f),
            new Vector3(16f, 5f, 16f),
            new Vector3(-0.1f, 5f, -0.1f),
            new Vector3(-1.5f, 20f, -16.5f),
            new Vector3(-16f, 0f, -17f),
            new Vector3(31.5f, 33.2f, -32.5f)
        };

        for (int i = 0; i < positions.Length; i++)
        {
            Vector3 position = positions[i];

            VoxelCoordinate voxel =
                VoxelWorldStreamingBootstrap.WorldPositionToVoxelCoordinate(position);

            // 体素 = [x, x+1)：floor 语义 ⇒ 与手算 floor 一致
            bool floorOk = voxel.x == Mathf.FloorToInt(position.x) &&
                           voxel.y == Mathf.FloorToInt(position.y) &&
                           voxel.z == Mathf.FloorToInt(position.z);

            // Chunk 坐标必须由 VoxelCore 的换算给出（不得自实现坐标规则）
            VoxelCoordinate expectedChunk = VoxelWorldData.WorldToChunkCoordinate(voxel);

            FakeSink sink = new FakeSink();
            ChunkStreamingDriver driver = new ChunkStreamingDriver(
                sink, CreateStreamingSettings(radius: 0, hysteresis: 0, vertical: 1));

            ChunkStreamingReport report = driver.Update(voxel);

            Check(floorOk && report.FocusChunk == expectedChunk,
                "CS1：位置 " + position + " ⇒ 体素 " + voxel + " ⇒ focusChunk " +
                report.FocusChunk + "（VoxelCore 换算一致）");
        }

        // 负坐标必须走 floor 除法（-0.1 ⇒ 体素 -1 ⇒ Chunk -1）
        VoxelCoordinate negativeVoxel =
            VoxelWorldStreamingBootstrap.WorldPositionToVoxelCoordinate(
                new Vector3(-0.1f, 0f, 0f));
        Check(negativeVoxel.x == -1, "CS1：-0.1 ⇒ 体素 x = -1（floor，不是 0）");
        Check(VoxelWorldData.WorldToChunkCoordinate(negativeVoxel).x == -1,
            "CS1：体素 -1 ⇒ Chunk x = -1（VoxelCore floor 除法）");
    }

    // =====================================================================
    // CS2：请求集合 == 期望加载集合（R=1 ⇒ 加号形 5 个）
    // =====================================================================

    private static void ValidateDesiredSet()
    {
        FakeSink sink = new FakeSink();
        ChunkStreamingSettings settings = CreateStreamingSettings(radius: 1, hysteresis: 1, vertical: 1);
        ChunkStreamingDriver driver = new ChunkStreamingDriver(sink, settings);

        ChunkStreamingReport report = driver.Update(new VoxelCoordinate(0, 0, 0));

        Check(report.DesiredCount == 5,
            "CS2：R=1 ⇒ 期望集合 5 个（加号形；实际 " + report.DesiredCount + "）");
        Check(report.RequestedThisUpdate == 5 && report.RejectedThisUpdate == 0,
            "CS2：全部提交成功（" + report + "）");
        Check(driver.StreamedCount == 5, "CS2：已请求集合 = 5");
        Check(sink.Requested.Count == 5, "CS2：上游收到 5 个请求");

        List<VoxelCoordinate> expected = new List<VoxelCoordinate>
        {
            new VoxelCoordinate(0, 0, 0),
            new VoxelCoordinate(1, 0, 0),
            new VoxelCoordinate(-1, 0, 0),
            new VoxelCoordinate(0, 0, 1),
            new VoxelCoordinate(0, 0, -1)
        };

        Check(SetEquals(sink.Requested, expected),
            "CS2：请求集合 == 独立重算的期望集合（" + Describe(sink.Requested) + "）");

        // 独立重算：所有请求必须满足 d² ≤ R² 且 y ∈ [0, V)
        int violations = 0;
        foreach (VoxelCoordinate chunk in sink.Requested)
        {
            if (!settings.IsWithinLoadRange(chunk, new VoxelCoordinate(0, 0, 0))) violations++;
        }

        Check(violations == 0, "CS2：无越界请求（越界 " + violations + "）");

        // 幂等：第二次 update 不产生任何请求
        ChunkStreamingReport second = driver.Update(new VoxelCoordinate(0, 0, 0));
        Check(second.RequestedThisUpdate == 0 && second.ReleasedThisUpdate == 0,
            "CS2：同一焦点重复 update 不产生请求/释放（" + second + "）");
        Check(driver.RequestedTotal == 5 && driver.ReleasedTotal == 0,
            "CS2：累计计数正确（requested=" + driver.RequestedTotal +
            ", released=" + driver.ReleasedTotal + "）");
    }

    // =====================================================================
    // CS3：滞回 —— R_unload 内的坐标即使离开 R_load 也不释放
    // =====================================================================

    private static void ValidateHysteresis()
    {
        FakeSink sink = new FakeSink();
        ChunkStreamingSettings settings = CreateStreamingSettings(radius: 1, hysteresis: 1, vertical: 1);
        ChunkStreamingDriver driver = new ChunkStreamingDriver(sink, settings);

        driver.Update(new VoxelCoordinate(0, 0, 0));       // 焦点 (0,0,0)：5 个
        Check(driver.StreamedCount == 5, "CS3：初始 5 个");

        VoxelCoordinate farSide = new VoxelCoordinate(-1, 0, 0);   // 离开 R_load、仍在 R_unload 内
        Check(!settings.IsWithinLoadRange(farSide, new VoxelCoordinate(1, 0, 0)),
            "CS3：(-1,0,0) 在新焦点 (1,0,0) 的 R_load 之外");
        Check(settings.IsWithinUnloadRange(farSide, new VoxelCoordinate(1, 0, 0)),
            "CS3：(-1,0,0) 仍在 R_unload（=2）之内 ⇒ 属于滞回带");

        ChunkStreamingReport report = driver.Update(new VoxelCoordinate(16, 0, 0));   // 移动 1 个 Chunk

        Check(report.FocusChunk == new VoxelCoordinate(1, 0, 0),
            "CS3：焦点换算正确（" + report.FocusChunk + "）");
        Check(report.ReleasedThisUpdate == 0,
            "CS3：移动 1 格不触发任何释放（滞回生效；实际释放 " +
            report.ReleasedThisUpdate + "）");
        Check(report.RequestedThisUpdate == 3,
            "CS3：新增 3 个（x=2 列与 (1,±1)；实际 " + report.RequestedThisUpdate + "）");
        Check(report.RetainedInHysteresisBand == 3,
            "CS3：滞回带保留 3 个（(-1,0,0) 与 (0,±1)；实际 " +
            report.RetainedInHysteresisBand + "）");
        Check(sink.Requested.Contains(new VoxelCoordinate(-1, 0, 0)),
            "CS3：(-1,0,0) 仍在上游请求集合中");
        Check(driver.StreamedCount == 8,
            "CS3：已请求集合 = 8（5 + 3；实际 " + driver.StreamedCount + "）");
    }

    // =====================================================================
    // CS4：远移立即释放（最远先释放）
    // =====================================================================

    private static void ValidateTeleportRelease()
    {
        FakeSink sink = new FakeSink();
        ChunkStreamingSettings settings = CreateStreamingSettings(radius: 1, hysteresis: 1, vertical: 1);
        ChunkStreamingDriver driver = new ChunkStreamingDriver(sink, settings);

        driver.Update(new VoxelCoordinate(0, 0, 0));
        Check(driver.StreamedCount == 5, "CS4：初始 5 个");

        ChunkStreamingReport report = driver.Update(new VoxelCoordinate(5 * 16, 0, 0));   // 远移 5 个 Chunk

        Check(report.FocusChunk == new VoxelCoordinate(5, 0, 0), "CS4：焦点 = (5,0,0)");
        Check(report.ReleasedThisUpdate == 5,
            "CS4：5 个旧坐标全部释放（实际 " + report.ReleasedThisUpdate + "）");
        Check(report.RequestedThisUpdate == 5,
            "CS4：新位置请求 5 个（实际 " + report.RequestedThisUpdate + "）");
        Check(sink.Requested.Count == 5 && driver.StreamedCount == 5,
            "CS4：上游请求集合与本地记录都只剩 5 个");

        // 释放顺序：最远先释放（同距离按 (x,y,z) 降序）
        List<string> releaseTrace = sink.Trace.FindAll(line => line.StartsWith("- ", StringComparison.Ordinal));
        Check(releaseTrace.Count == 5, "CS4：释放调用 5 次（" + releaseTrace.Count + "）");
        Check(releaseTrace.Count > 0 && releaseTrace[0] == "- (-1, 0, 0)",
            "CS4：最远者 (-1,0,0) 先释放（实际 " + (releaseTrace.Count > 0 ? releaseTrace[0] : "-") + "）");
    }

    // =====================================================================
    // CS5：抖动无反复加载（同一 Chunk 内的位置移动不产生任何请求/释放）
    // =====================================================================

    private static void ValidateJitterWithoutChurn()
    {
        FakeSink sink = new FakeSink();
        ChunkStreamingDriver driver = new ChunkStreamingDriver(
            sink, CreateStreamingSettings(radius: 1, hysteresis: 1, vertical: 1));

        // 同一 Chunk 内的亚体素抖动（0..15 体素都落在 Chunk (0,0,0)）
        driver.Update(new VoxelCoordinate(0, 0, 0));
        int requestedAfterFirst = driver.RequestedTotal;

        for (int i = 1; i <= 8; i++)
        {
            driver.Update(new VoxelCoordinate(i, 0, i));
        }

        Check(driver.RequestedTotal == requestedAfterFirst,
            "CS5：Chunk 内抖动不产生新请求（" + requestedAfterFirst + " → " +
            driver.RequestedTotal + "）");
        Check(driver.ReleasedTotal == 0,
            "CS5：Chunk 内抖动不产生释放（实际 " + driver.ReleasedTotal + "）");

        // 相邻 Chunk 之间来回移动：因 R_unload = R + H（H=1）也不产生释放，只产生首次新增
        int requestedBeforeOscillation = driver.RequestedTotal;
        driver.Update(new VoxelCoordinate(16, 0, 0));
        int requestedAfterMove = driver.RequestedTotal;
        Check(driver.ReleasedTotal == 0, "CS5：移动 1 格无释放（滞回）");

        for (int i = 0; i < 6; i++)
        {
            driver.Update(new VoxelCoordinate(0, 0, 0));
            driver.Update(new VoxelCoordinate(16, 0, 0));
        }

        Check(driver.ReleasedTotal == 0,
            "CS5：相邻 Chunk 抖动全程 0 释放（实际 " + driver.ReleasedTotal + "）");
        Check(driver.RequestedTotal == requestedAfterMove,
            "CS5：抖动期间不再新增请求（" + requestedAfterMove + " → " +
            driver.RequestedTotal + "）");
        Check(requestedAfterMove > requestedBeforeOscillation,
            "CS5：首次移动确实新增了请求（" + requestedBeforeOscillation + " → " +
            requestedAfterMove + "）");
    }

    // =====================================================================
    // CS6：垂直层加载范围
    // =====================================================================

    private static void ValidateVerticalRange()
    {
        for (int vertical = 1; vertical <= 3; vertical++)
        {
            FakeSink sink = new FakeSink();
            ChunkStreamingSettings settings =
                CreateStreamingSettings(radius: 1, hysteresis: 0, vertical: vertical);
            ChunkStreamingDriver driver = new ChunkStreamingDriver(sink, settings);

            ChunkStreamingReport report = driver.Update(new VoxelCoordinate(0, 0, 0));

            Check(report.DesiredCount == 5 * vertical,
                "CS6：V=" + vertical + " ⇒ 期望 " + (5 * vertical) +
                " 个（实际 " + report.DesiredCount + "）");
            Check(sink.Requested.Count == 5 * vertical,
                "CS6：上游收到 " + (5 * vertical) + " 个请求");

            int outOfRange = 0;
            int minY = int.MaxValue;
            int maxY = int.MinValue;
            foreach (VoxelCoordinate chunk in sink.Requested)
            {
                if (!settings.IsWithinVerticalRange(chunk)) outOfRange++;
                if (chunk.y < minY) minY = chunk.y;
                if (chunk.y > maxY) maxY = chunk.y;
            }

            Check(outOfRange == 0, "CS6：V=" + vertical + " 无越界层（越界 " + outOfRange + "）");
            Check(minY == 0 && maxY == vertical - 1,
                "CS6：V=" + vertical + " 覆盖 y ∈ [0," + (vertical - 1) + "]（实际 [" +
                minY + "," + maxY + "]）");
        }
    }

    // =====================================================================
    // CS7：与 Core 集成（预算分帧 → 全部 Ready）
    // =====================================================================

    private static void ValidateCoreIntegration()
    {
        VoxelWorldData world = new VoxelWorldData();
        BasicTerrainGenerator generator = new BasicTerrainGenerator(WorldGenerationSettings.Default);
        FakePresenter presenter = new FakePresenter();

        RuntimeCoreSettings coreSettings = new RuntimeCoreSettings
        {
            VerticalChunkCount = 1,
            MaxLoadsPerFrame = 2,
            MaxChunkPairsPerFrame = 2,
            MaxUnloadsPerFrame = 4,
            MaxRetriesPerChunk = 3
        };

        VoxelRuntimeCore core =
            new VoxelRuntimeCore(world, generator, new VoxelMeshGeneratorMesher(), presenter, coreSettings);

        ChunkStreamingSettings streamingSettings =
            CreateStreamingSettings(radius: 2, hysteresis: 1, vertical: 1);
        ChunkStreamingDriver driver =
            new ChunkStreamingDriver(new RuntimeCoreRequestSink(core), streamingSettings);

        ChunkStreamingReport streamingReport = driver.Update(new VoxelCoordinate(0, 0, 0));
        Check(streamingReport.DesiredCount == 13,
            "CS7：R=2 ⇒ 13 个期望 Chunk（实际 " + streamingReport.DesiredCount + "）");
        Check(streamingReport.RequestedThisUpdate == 13 && streamingReport.RejectedThisUpdate == 0,
            "CS7：13 个请求全部被 Core 接受（" + streamingReport + "）");

        // 请求集合与 Core 的权威请求集合一致（排序后逐一比对）
        List<VoxelCoordinate> driverSet = new List<VoxelCoordinate>(driver.GetStreamedCoordinates());
        List<VoxelCoordinate> coreSet = new List<VoxelCoordinate>(core.GetRequestedCoordinates());
        Check(SetEquals(new HashSet<VoxelCoordinate>(driverSet), coreSet),
            "CS7：流式层已请求集合 == Core 请求集合（" + driverSet.Count + " vs " +
            coreSet.Count + "）");

        int frames = 0;
        int overflowFrames = 0;
        RuntimeCoreTickReport tickReport = null;

        while (core.LoadedChunkCount < 13 && frames < 40)
        {
            tickReport = core.Tick();
            frames++;

            if (tickReport.LoadedThisTick > coreSettings.MaxLoadsPerFrame ||
                tickReport.BuiltThisTick > coreSettings.MaxChunkPairsPerFrame)
            {
                overflowFrames++;
            }
        }

        Check(core.LoadedChunkCount == 13,
            "CS7：最终全部加载（" + core.LoadedChunkCount + "/13，用了 " + frames + " 帧）");
        Check(core.GetLoadedCoordinates().Count == 13, "CS7：已加载坐标 13 个");
        Check(world.ChunkCount == 13, "CS7：世界里 13 个 Chunk");
        Check(presenter.LiveCount == 13, "CS7：13 个表示槽");
        Check(overflowFrames == 0,
            "CS7：每帧不超预算（越界帧 " + overflowFrames + "）");

        // 全部 Ready（无 Dirty 积压）
        int notReady = 0;
        IList<VoxelCoordinate> loaded = core.GetLoadedCoordinates();
        for (int i = 0; i < loaded.Count; i++)
        {
            ChunkRecordSnapshot snapshot;
            if (!core.TryGetSnapshot(loaded[i], out snapshot) ||
                snapshot.State != ChunkLifecycleState.Ready)
            {
                notReady++;
            }
        }

        Check(notReady == 0, "CS7：全部 Ready（非 Ready " + notReady + "）");
        Check(tickReport != null && tickReport.InvariantViolationCount == 0,
            "CS7：无帧末不变量违规");

        // 远移：全部旧 Chunk 卸载，新位置加载
        ChunkStreamingReport move = driver.Update(new VoxelCoordinate(20 * 16, 0, 0));
        Check(move.ReleasedThisUpdate == 13,
            "CS7：远移释放全部 13 个（实际 " + move.ReleasedThisUpdate + "）");

        for (int i = 0; i < 20 && (core.LoadedChunkCount > 0 || core.Diagnostics.UnloadedTotal < 13); i++)
        {
            core.Tick();
        }

        Check(core.LoadedChunkCount == 0 || core.GetLoadedCoordinates().Count <= 13,
            "CS7：卸载推进（当前已加载 " + core.LoadedChunkCount + "）");

        for (int i = 0; i < 30 && core.LoadedChunkCount < 13; i++) core.Tick();

        Check(core.LoadedChunkCount == 13,
            "CS7：新位置 13 个全部加载（实际 " + core.LoadedChunkCount + "）");
        Check(world.ChunkCount == 13, "CS7：世界最终 13 个 Chunk（旧坐标已卸载）");
    }

    // =====================================================================
    // CS8：配置不一致 ⇒ 拒绝可见（不静默）
    // =====================================================================

    private static void ValidateMismatchVisible()
    {
        VoxelWorldData world = new VoxelWorldData();
        BasicTerrainGenerator generator = new BasicTerrainGenerator(WorldGenerationSettings.Default);

        RuntimeCoreSettings coreSettings = new RuntimeCoreSettings
        {
            VerticalChunkCount = 1,     // Core 只接受 y = 0
            MaxLoadsPerFrame = 4,
            MaxChunkPairsPerFrame = 4,
            MaxUnloadsPerFrame = 4,
            MaxRetriesPerChunk = 3
        };

        VoxelRuntimeCore core = new VoxelRuntimeCore(
            world, generator, new VoxelMeshGeneratorMesher(), new FakePresenter(), coreSettings);

        // 故意不一致：流式层请求 2 层，Core 只接受 1 层
        ChunkStreamingDriver driver = new ChunkStreamingDriver(
            new RuntimeCoreRequestSink(core),
            CreateStreamingSettings(radius: 0, hysteresis: 0, vertical: 2));

        ChunkStreamingReport report = driver.Update(new VoxelCoordinate(0, 0, 0));

        Check(report.DesiredCount == 2, "CS8：期望 2 个（两层）");
        Check(report.RejectedThisUpdate == 1,
            "CS8：越界层被 Core 拒绝 1 个（实际 " + report.RejectedThisUpdate + "）");
        Check(report.RequestedThisUpdate == 1, "CS8：只有合法层被接受");
        Check(core.Diagnostics.RejectedRequests == 1,
            "CS8：Core 侧拒绝计数可见（" + core.Diagnostics.RejectedRequests + "）");
        Check(driver.RejectedTotal == 1,
            "CS8：流式层累计拒绝计数可见（" + driver.RejectedTotal + "）");
        Check(core.GetRequestedCoordinates().Count == 1,
            "CS8：Core 请求集合只有 1 个");
    }

    // =====================================================================
    // CS9：确定性（同一路径两次运行的报告序列完全一致）
    // =====================================================================

    private static void ValidateDeterminism()
    {
        VoxelCoordinate[] path =
        {
            new VoxelCoordinate(0, 0, 0),
            new VoxelCoordinate(5, 0, 0),
            new VoxelCoordinate(16, 0, 3),
            new VoxelCoordinate(40, 0, 40),
            new VoxelCoordinate(-33, 0, 17),
            new VoxelCoordinate(-33, 0, 17),
            new VoxelCoordinate(0, 0, 0)
        };

        string[] runA = RunDeterminismRig(path);
        string[] runB = RunDeterminismRig(path);

        Check(runA.Length == path.Length + 1 && runB.Length == path.Length + 1,
            "CS9：两次运行产生相同数量的更新报告（" + runA.Length + " / " +
            runB.Length + "，路径 " + path.Length + " 步 + 1 行汇总）");

        int differences = 0;
        for (int i = 0; i < Mathf.Min(runA.Length, runB.Length); i++)
        {
            if (runA[i] != runB[i]) differences++;
        }

        Check(differences == 0,
            "CS9：报告序列逐项一致（差异 " + differences + " 条）");

        // 期望集合始终 ⊆ 已请求集合（"焦点周围一定已加载"）
        FakeSink sink = new FakeSink();
        ChunkStreamingDriver driver = new ChunkStreamingDriver(
            sink, CreateStreamingSettings(radius: 2, hysteresis: 1, vertical: 1));

        int missing = 0;
        for (int i = 0; i < path.Length; i++)
        {
            driver.Update(path[i]);
            IList<VoxelCoordinate> desired = driver.GetDesiredCoordinates();

            for (int j = 0; j < desired.Count; j++)
            {
                if (!sink.Requested.Contains(desired[j])) missing++;
            }
        }

        Check(missing == 0,
            "CS9：任一时刻 期望加载集合 ⊆ 已请求集合（缺失 " + missing + "）");

        Debug.Log("[ChunkStreamingValidation] 报告序列（同一路径第一次运行）：\n" +
                  string.Join("\n", runA));
    }

    private static string[] RunDeterminismRig(VoxelCoordinate[] path)
    {
        FakeSink sink = new FakeSink();
        ChunkStreamingDriver driver = new ChunkStreamingDriver(
            sink, CreateStreamingSettings(radius: 2, hysteresis: 1, vertical: 2));

        List<string> lines = new List<string>();

        for (int i = 0; i < path.Length; i++)
        {
            ChunkStreamingReport report = driver.Update(path[i]);
            lines.Add(report.ToString());
        }

        lines.Add("streamed=" + driver.StreamedCount +
                  " requested=" + driver.RequestedTotal +
                  " released=" + driver.ReleasedTotal);

        return lines.ToArray();
    }

    // =====================================================================
    // CS10：结构扫描（流式层纯 C#；组装点边界）
    // =====================================================================

    private static readonly string[] ForbiddenInStreaming =
    {
        "UnityEngine", "UnityEditor", "MonoBehaviour", "GameObject", "Transform",
        @"\bVector2\b", @"\bVector3\b", @"\bBounds\b", @"(?<!\.)\bMesh\b", "Material",
        @"Time\.", @"\bRandom\b", "FindObjectOfType", @"Camera\.main",
        "HeightFunction", "BasicTerrainGenerator", "VoxelMeshGenerator",
        "VoxelChunkView", "UnityMeshBuilder", "new VoxelChunk",
        "SetVoxel", "MarkDirty", "ClearDirty", "ClearDirtyFlags",
        @"\.Vertices", @"\.Normals", @"\.UVs", "Physics\\.Raycast"
    };

    private static readonly string[] ForbiddenInBootstrap =
    {
        "HeightFunction", "SetVoxel", "MarkDirty", "ClearDirty", "ClearDirtyFlags",
        "FindObjectOfType", @"Camera\.main", "Resources\\.Load", "Physics\\.Raycast",
        @"Time\.deltaTime"
    };

    private static void ValidateStructuralScan()
    {
        string folder = Path.Combine(Application.dataPath, StreamingFolder);
        Check(Directory.Exists(folder), "CS10：流式层目录存在（" + folder + "）");
        if (!Directory.Exists(folder)) return;

        string[] files = Directory.GetFiles(folder, "*.cs", SearchOption.TopDirectoryOnly);
        Array.Sort(files, StringComparer.Ordinal);

        Check(files.Length >= 5, "CS10：流式层至少 5 个源文件（实际 " + files.Length + "）");

        foreach (string expected in new string[]
        {
            "IChunkRequestSink.cs", "ChunkStreamingSettings.cs", "ChunkStreamingReport.cs",
            "ChunkStreamingDriver.cs", "RuntimeCoreRequestSink.cs"
        })
        {
            Check(File.Exists(Path.Combine(folder, expected)), "CS10：存在 Streaming/" + expected);
        }

        // 扫描器自检
        string probe = "using UnityEngine;\n// UnityEngine 在注释里不计\nvoid P(){ }\n";
        Check(ScanTokens(StripComments(probe), ForbiddenInStreaming).Count == 1,
            "CS10：扫描器自检（命中 1）");

        int hits = 0;
        for (int i = 0; i < files.Length; i++)
        {
            string code = StripComments(File.ReadAllText(files[i], Encoding.UTF8));
            bool isSinkAdapter = Path.GetFileName(files[i]) == "RuntimeCoreRequestSink.cs";

            // 唯一豁免：RuntimeCoreRequestSink.cs 允许命名 Runtime Core 具体类型（它就是适配器）；
            // 其余流式层文件（求解器 / 参数 / 报告 / 抽象）一律禁止出现 Core 具体类型名。
            string[] patterns = isSinkAdapter
                ? ForbiddenInStreaming
                : Append(ForbiddenInStreaming, "VoxelRuntimeCore");

            List<string> found = ScanTokens(code, patterns);
            if (found.Count > 0)
            {
                hits += found.Count;
                Check(false, "CS10：" + Path.GetFileName(files[i]) + " 命中禁止项：" +
                             string.Join(", ", found.ToArray()));
            }
        }

        Check(hits == 0,
            "CS10：流式层 " + files.Length +
            " 个文件不含 Unity 类型、世界规则、数据写入、具体实现名（命中 " + hits + "）");

        // 正向对照 1：驱动必须调用 VoxelCore 的坐标换算（不得自实现坐标规则）
        string driverPath = Path.Combine(folder, "ChunkStreamingDriver.cs");
        string driverCode = StripComments(File.ReadAllText(driverPath, Encoding.UTF8));
        Check(driverCode.Contains("WorldToChunkCoordinate"),
            "CS10：驱动使用 VoxelCore 的 WorldToChunkCoordinate 做焦点换算（正向对照）");

        // 正向对照 2：只有 RuntimeCoreRequestSink 命名 VoxelRuntimeCore
        string sinkPath = Path.Combine(folder, "RuntimeCoreRequestSink.cs");
        string sinkCode = StripComments(File.ReadAllText(sinkPath, Encoding.UTF8));
        Check(sinkCode.Contains("VoxelRuntimeCore"),
            "CS10：RuntimeCoreRequestSink 是唯一命名 Core 的流式层文件（正向对照）");

        // 组装点：允许 new 具体实现，但不得含世界规则 / 隐式查找 / 数据写入
        string bootstrapPath = Path.Combine(Application.dataPath, BootstrapPath);
        Check(File.Exists(bootstrapPath), "CS10：组装点文件存在（" + BootstrapPath + "）");
        if (!File.Exists(bootstrapPath)) return;

        string bootstrapCode = StripComments(File.ReadAllText(bootstrapPath, Encoding.UTF8));
        List<string> bootstrapHits = ScanTokens(bootstrapCode, ForbiddenInBootstrap);

        if (bootstrapHits.Count > 0)
        {
            Check(false, "CS10：组装点命中禁止项：" + string.Join(", ", bootstrapHits.ToArray()));
        }

        Check(bootstrapHits.Count == 0,
            "CS10：组装点不含世界规则 / 隐式查找 / 数据写入（命中 " + bootstrapHits.Count + "）");
        // 组装点是唯一 new 具体实现的位置（Stage E：世界生成器已换为 GeologyWorldGenerator）
        Check(bootstrapCode.Contains("new GeologyWorldGenerator"),
            "CS10：组装点装配世界生成器（正向对照：GeologyWorldGenerator）");
        Check(!bootstrapCode.Contains("new BasicTerrainGenerator"),
            "CS10：组装点不再装配 BasicTerrainGenerator（Stage E 组装点替换）");
        Check(bootstrapCode.Contains("ChunkStreamingDriver") &&
              bootstrapCode.Contains("VoxelRuntimeCore"),
            "CS10：组装点显式装配 流式驱动 + Runtime Core");

        Debug.Log("[ChunkStreamingValidation] 扫描范围（" + files.Length + " 流式文件 + 组装点）：\n" +
                  string.Join("\n", files) + "\n" + bootstrapPath);
    }

    private static string[] Append(string[] source, string extra)
    {
        string[] result = new string[source.Length + 1];
        Array.Copy(source, result, source.Length);
        result[source.Length] = extra;
        return result;
    }

    private static List<string> ScanTokens(string code, string[] patterns)
    {
        List<string> hits = new List<string>();

        for (int i = 0; i < patterns.Length; i++)
        {
            if (Regex.IsMatch(code, patterns[i])) hits.Add(patterns[i]);
        }

        return hits;
    }

    private static string StripComments(string text)
    {
        StringBuilder output = new StringBuilder(text.Length);
        string[] lines = text.Replace("\r\n", "\n").Split('\n');
        bool inBlockComment = false;

        for (int l = 0; l < lines.Length; l++)
        {
            string line = lines[l];
            StringBuilder kept = new StringBuilder(line.Length);
            int i = 0;

            while (i < line.Length)
            {
                if (inBlockComment)
                {
                    int end = line.IndexOf("*/", i, StringComparison.Ordinal);
                    if (end < 0) { i = line.Length; break; }
                    inBlockComment = false;
                    i = end + 2;
                    continue;
                }

                if (i + 1 < line.Length && line[i] == '/' && line[i + 1] == '/') break;

                if (i + 1 < line.Length && line[i] == '/' && line[i + 1] == '*')
                {
                    inBlockComment = true;
                    i += 2;
                    continue;
                }

                kept.Append(line[i]);
                i++;
            }

            output.Append(kept).Append('\n');
        }

        return output.ToString();
    }

    // =====================================================================
    // CS11：Unity 侧 Bootstrap 端到端
    // =====================================================================

    private static void ValidateBootstrapEndToEnd()
    {
        GameObject host = null;
        GameObject player = null;
        GameObject chunkRoot = null;
        VoxelWorldStreamingBootstrap bootstrap = null;

        try
        {
            chunkRoot = new GameObject("StreamingChunkRoot");
            player = new GameObject("StreamingPlayer");
            host = new GameObject("StreamingBootstrap");

            bootstrap = host.AddComponent<VoxelWorldStreamingBootstrap>();
            bootstrap.Player = player.transform;
            bootstrap.ChunkRoot = chunkRoot.transform;
            bootstrap.SubmeshMaterials = sharedMaterials;
            bootstrap.BuildColliders = true;
            bootstrap.TickEveryFrame = false;

            bootstrap.ViewDistanceChunks = 1;
            bootstrap.UnloadHysteresisChunks = 1;
            bootstrap.VerticalChunkCount = 1;
            bootstrap.WorldHeight = 64;
            bootstrap.TerrainHeight = 24;
            bootstrap.MaxLoadsPerFrame = 8;
            bootstrap.MaxChunkPairsPerFrame = 8;
            bootstrap.MaxUnloadsPerFrame = 8;

            bootstrap.Build();

            Check(bootstrap.IsBuilt, "CS11：组装成功（" + (bootstrap.BuildError ?? "OK") + "）");
            if (!bootstrap.IsBuilt) return;

            Check(bootstrap.Core != null && bootstrap.Streaming != null &&
                  bootstrap.Presenter != null && bootstrap.World != null,
                "CS11：组装产物齐全（Core / Streaming / Presenter / World）");

            // 玩家在原点：1 次 Step ⇒ 请求 5 个并全部加载 + 呈递
            player.transform.position = new Vector3(0f, 20f, 0f);
            ChunkStreamingReport streamingReport = bootstrap.Step();

            Check(streamingReport != null && streamingReport.DesiredCount == 5,
                "CS11：期望 5 个（R=1, V=1；实际 " +
                (streamingReport != null ? streamingReport.DesiredCount : -1) + "）");
            Check(streamingReport != null && streamingReport.RequestedThisUpdate == 5,
                "CS11：5 个请求被提交（" + (streamingReport != null ? streamingReport + "" : "-") + "）");
            Check(bootstrap.Core.LoadedChunkCount == 5,
                "CS11：5 个 Chunk 已加载（实际 " + bootstrap.Core.LoadedChunkCount + "）");
            Check(bootstrap.Presenter.LiveCount == 5,
                "CS11：5 个表示槽（实际 " + bootstrap.Presenter.LiveCount + "）");
            Check(chunkRoot.transform.childCount == 5,
                "CS11：Root 下 5 个容器（实际 " + chunkRoot.transform.childCount + "）");

            int ready = 0;
            IList<VoxelCoordinate> loaded = bootstrap.Core.GetLoadedCoordinates();
            for (int i = 0; i < loaded.Count; i++)
            {
                ChunkRecordSnapshot snapshot;
                if (bootstrap.Core.TryGetSnapshot(loaded[i], out snapshot) &&
                    snapshot.State == ChunkLifecycleState.Ready)
                {
                    ready++;
                }
            }

            Check(ready == 5, "CS11：全部 Ready（" + ready + "/5）");

            // 玩家在 Chunk 内移动（亚体素）⇒ 不产生新的请求 / 释放
            player.transform.position = new Vector3(4f, 20f, 9f);
            ChunkStreamingReport idle = bootstrap.Step();
            Check(idle != null && idle.RequestedThisUpdate == 0 && idle.ReleasedThisUpdate == 0,
                "CS11：Chunk 内移动无请求/释放（" + (idle != null ? idle + "" : "-") + "）");

            // 玩家远移 ⇒ 旧的被释放、新的被加载
            player.transform.position = new Vector3(10 * 16f + 3f, 20f, 0f);
            ChunkStreamingReport move = bootstrap.Step();
            Check(move != null && move.ReleasedThisUpdate == 5,
                "CS11：远移释放 5 个（实际 " +
                (move != null ? move.ReleasedThisUpdate : -1) + "）");

            for (int i = 0; i < 6; i++) bootstrap.Step();

            Check(bootstrap.Core.LoadedChunkCount == 5,
                "CS11：新位置 5 个已加载（实际 " + bootstrap.Core.LoadedChunkCount + "）");
            Check(chunkRoot.transform.childCount == 5,
                "CS11：Root 下仍 5 个容器（实际 " + chunkRoot.transform.childCount + "）");

            // 拆除：表示与容器全部释放
            bootstrap.Teardown();
            Check(bootstrap.Presenter == null && !bootstrap.IsBuilt, "CS11：Teardown 清空引用");
            Check(chunkRoot.transform.childCount == 0,
                "CS11：Teardown 后无残留容器（实际 " + chunkRoot.transform.childCount + "）");
        }
        finally
        {
            if (bootstrap != null) bootstrap.Teardown();

            if (host != null) UnityEngine.Object.DestroyImmediate(host);
            if (player != null) UnityEngine.Object.DestroyImmediate(player);
            if (chunkRoot != null) UnityEngine.Object.DestroyImmediate(chunkRoot);
        }
    }

    // =====================================================================
    // CS12：既有文件 SHA 不变
    // =====================================================================

    private static readonly Dictionary<string, string> FrozenFiles =
        new Dictionary<string, string>
        {
            // VoxelCore / Generation / Mesh
            { "Scripts/VoxelWorld/VoxelType.cs", "02C1F54580AEE541" },
            { "Scripts/VoxelWorld/Voxel.cs", "DC52145E6E82132D" },
            { "Scripts/VoxelWorld/VoxelCoordinate.cs", "F327B2761DFDCFC2" },
            { "Scripts/VoxelWorld/VoxelChunk.cs", "4DBDFBAC5BCE500C" },
            { "Scripts/VoxelWorld/VoxelWorld.cs", "D9E749A466635918" },
            { "Scripts/VoxelWorld/VoxelWorldDebug.cs", "6AE0C41274AF9154" },
            { "Scripts/VoxelWorld/Generation/WorldGenerationSettings.cs", "140B7E289F05C6FA" },
            { "Scripts/VoxelWorld/Generation/IWorldGenerator.cs", "B39614CA9BF37921" },
            { "Scripts/VoxelWorld/Generation/HeightFunction.cs", "715341D5FC652408" },
            { "Scripts/VoxelWorld/Generation/BasicTerrainGenerator.cs", "7FFD8F4B1226EA4E" },
            { "Scripts/VoxelWorld/Mesh/VoxelMeshData.cs", "8B78A03ED1C78E26" },
            { "Scripts/VoxelWorld/Mesh/VoxelMeshGenerator.cs", "653804C19C272C72" },
            // View
            { "Scripts/VoxelWorld/View/VoxelChunkView.cs", "9D92BA355535ADCA" },
            { "Scripts/VoxelWorld/View/UnityMeshBuilder.cs", "F9E4224996AC7CE2" },
            { "Scripts/VoxelWorld/View/ChunkViewPresenter.cs", "C6346E735591E2BF" },
            // Runtime Core（上一轮）
            { "Scripts/VoxelWorld/Runtime/Abstractions/IChunkMesher.cs", "8389FA2E4A4DCF7D" },
            { "Scripts/VoxelWorld/Runtime/Abstractions/IChunkPresenter.cs", "B743B37CD921AC8E" },
            { "Scripts/VoxelWorld/Runtime/ChunkLifecycleState.cs", "D05B26A6E84BF314" },
            { "Scripts/VoxelWorld/Runtime/RuntimeCoreSettings.cs", "C44D9D517F9974CB" },
            { "Scripts/VoxelWorld/Runtime/RuntimeCoreTickReport.cs", "25D00EEA0C43B246" },
            { "Scripts/VoxelWorld/Runtime/RuntimeCoreDiagnostics.cs", "8006333FE230FD65" },
            { "Scripts/VoxelWorld/Runtime/ChunkRecord.cs", "95BA7DB5D34198E1" },
            { "Scripts/VoxelWorld/Runtime/ChunkRecordSnapshot.cs", "ACCC7B1BC8677AD5" },
            { "Scripts/VoxelWorld/Runtime/RuntimeChangeSet.cs", "1425E571E78401FF" },
            { "Scripts/VoxelWorld/Runtime/VoxelRuntimeCore.cs", "9119E8D3292A4C71" },
            { "Scripts/VoxelWorld/Runtime/Adapters/VoxelMeshGeneratorMesher.cs", "8A21FE91027AA135" },
            // Editor 验证入口
            { "Editor/VoxelWorldCoreValidation.cs", "6DE813AABCE95394" },
            { "Editor/WorldGenerationCoreValidation.cs", "4A517A0F902A1066" },
            { "Editor/VoxelMeshCoreValidation.cs", "5FFD8451D2CE05EB" },
            { "Editor/VoxelWorldViewValidation.cs", "BEFBAAF6B111A241" },
            { "Editor/VoxelWorldRuntimeCoreValidation.cs", "0E0B5C160FEAB236" },
            // 规范
            { "../Specifications/World/VoxelWorld_Core.md", "F718B68E95100C57" },
            { "../Specifications/World/WorldGeneration_Core.md", "684753D5E6B31565" },
            { "../Specifications/World/VoxelWorld_Mesh.md", "81CEDBFA6E9756CE" },
            { "../Specifications/World/VoxelWorld_Mining.md", "8A2B020DDB97422B" },
            { "../Specifications/World/VoxelWorld_View.md", "D9BD5422026E21B6" },
            { "../Specifications/World/VoxelWorld_Runtime.md", "EDD02AC50FC03E5E" },
            { "../Specifications/World/VoxelWorld_Runtime_Core.md", "10F1EC7F10DAD8F3" },
            { "../Specifications/World/VoxelWorld_Runtime_Interface.md", "0AE21EB684412685" },
            { "../Specifications/World/VoxelWorld_Runtime_Core_Implementation.md", "D816F09C60A810C4" }
        };

    private static void ValidateExistingFilesUnchanged()
    {
        int compared = 0;
        int missing = 0;
        int mismatch = 0;
        int skippedSpecs = 0;

        // 规范位于工程根（主工程）；验证工程没有规范副本 ⇒ 显式跳过并说明（不伪装成 PASS）
        string specificationRoot = FindSpecificationsRoot();

        foreach (KeyValuePair<string, string> pair in FrozenFiles)
        {
            bool isSpecification = pair.Key.StartsWith("../", StringComparison.Ordinal);

            if (isSpecification)
            {
                if (specificationRoot == null) { skippedSpecs++; continue; }

                string specificationPath = Path.Combine(
                    specificationRoot, pair.Key.Substring(3).Replace('/', Path.DirectorySeparatorChar));

                if (!File.Exists(specificationPath))
                {
                    missing++;
                    Check(false, "CS12：缺失规范 " + pair.Key);
                    continue;
                }

                string specificationHash = ComputeFileHash16(specificationPath);
                if (specificationHash == null) continue;

                compared++;
                if (specificationHash != pair.Value)
                {
                    mismatch++;
                    Check(false, "CS12：" + pair.Key + " 已改变（" + specificationHash +
                                 " != " + pair.Value + "）");
                }

                continue;
            }

            string path = Path.Combine(Application.dataPath, pair.Key);
            if (!File.Exists(path))
            {
                missing++;
                Check(false, "CS12：缺失 " + pair.Key);
                continue;
            }

            string hash = ComputeFileHash16(path);
            if (hash == null) continue;

            compared++;
            if (hash != pair.Value)
            {
                mismatch++;
                Check(false, "CS12：" + pair.Key + " 已改变（" + hash + " != " + pair.Value + "）");
            }
        }

        Check(missing == 0, "CS12：既有文件全部存在（缺失 " + missing + "）");
        Check(mismatch == 0,
            "CS12：既有文件 SHA256(前16) 全部不变（比对 " + compared + " 个；" +
            "本工程跳过规范 " + skippedSpecs + " 个）");
    }

    /// <summary>向上查找包含 Specifications/World 的工程根（主工程才有规范）。</summary>
    private static string FindSpecificationsRoot()
    {
        DirectoryInfo directory = new DirectoryInfo(Application.dataPath);

        for (int i = 0; i < 4 && directory != null; i++)
        {
            string candidate = Path.Combine(
                directory.FullName, "Specifications", "World");

            if (Directory.Exists(candidate)) return directory.FullName;

            directory = directory.Parent;
        }

        return null;
    }

    private static string ComputeFileHash16(string path)
    {
        try
        {
            using (FileStream stream = File.OpenRead(path))
            {
                SHA256 sha = SHA256.Create();
                byte[] digest = sha.ComputeHash(stream);
                StringBuilder text = new StringBuilder(16);
                for (int i = 0; i < 8; i++) text.Append(digest[i].ToString("X2"));
                return text.ToString();
            }
        }
        catch (Exception ex)
        {
            Check(false, "CS12：无法计算哈希（" + path + "）：" + ex.Message);
            return null;
        }
    }

    // =====================================================================
    // 测试辅助
    // =====================================================================

    private static ChunkStreamingSettings CreateStreamingSettings(
        int radius, int hysteresis, int vertical)
    {
        return new ChunkStreamingSettings
        {
            ViewDistanceChunks = radius,
            UnloadHysteresisChunks = hysteresis,
            VerticalChunkCount = vertical
        };
    }

    /// <summary>记录调用序列、可注入拒绝的请求接收端（不创建任何 Unity 对象）。</summary>
    private sealed class FakeSink : IChunkRequestSink
    {
        public readonly HashSet<VoxelCoordinate> Requested = new HashSet<VoxelCoordinate>();
        public readonly List<string> Trace = new List<string>();

        public int RequestCalls;
        public int ReleaseCalls;
        public Func<VoxelCoordinate, bool> RequestFilter;

        public bool RequestChunk(VoxelCoordinate chunkCoordinate)
        {
            RequestCalls++;
            Trace.Add("+ " + chunkCoordinate);

            if (RequestFilter != null && !RequestFilter(chunkCoordinate)) return false;

            return Requested.Add(chunkCoordinate);
        }

        public bool ReleaseChunk(VoxelCoordinate chunkCoordinate)
        {
            ReleaseCalls++;
            Trace.Add("- " + chunkCoordinate);
            return Requested.Remove(chunkCoordinate);
        }
    }

    /// <summary>只记录调用的表示层假实现（Core 集成测试用）。</summary>
    private sealed class FakePresenter : IChunkPresenter
    {
        private readonly HashSet<VoxelCoordinate> live = new HashSet<VoxelCoordinate>();

        public int ApplyCalls;

        public int LiveCount { get { return live.Count; } }

        public PresenterOutcome Apply(
            VoxelCoordinate coordinate, VoxelMeshData meshData, out string error)
        {
            ApplyCalls++;
            live.Add(coordinate);
            error = null;

            if (meshData == null || !meshData.HasGeometry) return PresenterOutcome.Empty;
            return PresenterOutcome.Presented;
        }

        public void Release(VoxelCoordinate coordinate)
        {
            live.Remove(coordinate);
        }
    }

    private static bool SetEquals(HashSet<VoxelCoordinate> a, ICollection<VoxelCoordinate> b)
    {
        if (a.Count != b.Count) return false;
        foreach (VoxelCoordinate chunk in b)
        {
            if (!a.Contains(chunk)) return false;
        }

        return true;
    }

    private static string Describe(ICollection<VoxelCoordinate> coordinates)
    {
        List<string> text = new List<string>();
        foreach (VoxelCoordinate chunk in coordinates) text.Add(chunk.ToString());
        text.Sort(StringComparer.Ordinal);
        return string.Join(" ", text.ToArray());
    }

    private static void EnsureEditableScene()
    {
        try
        {
            UnityEngine.SceneManagement.Scene scene = EditorSceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                Debug.Log("[ChunkStreamingValidation] 无有效场景 ⇒ 已创建空场景");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[ChunkStreamingValidation] 场景准备异常（继续）：" + ex.Message);
        }
    }

    private static void CreateSharedMaterials()
    {
        string[] candidates =
        {
            "Standard", "Universal Render Pipeline/Lit", "Unlit/Color",
            "Sprites/Default", "Legacy Shaders/Diffuse"
        };

        Shader shader = null;
        for (int i = 0; i < candidates.Length; i++)
        {
            shader = Shader.Find(candidates[i]);
            if (shader != null) break;
        }

        sharedMaterials = new Material[VoxelMeshData.SubmeshCount];

        for (int i = 0; i < sharedMaterials.Length; i++)
        {
            if (shader != null)
            {
                sharedMaterials[i] = new Material(shader);
                sharedMaterials[i].name = "ChunkStreamingValidation_Shared_" + i;
            }
        }
    }

    private static void DestroySharedMaterials()
    {
        if (sharedMaterials == null) return;

        for (int i = 0; i < sharedMaterials.Length; i++)
        {
            if (sharedMaterials[i] != null)
            {
                UnityEngine.Object.DestroyImmediate(sharedMaterials[i]);
            }
        }

        sharedMaterials = null;
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
        }
        catch (Exception ex)
        {
            Check(false, "测试「" + name + "」抛异常：" + ex.GetType().Name + ": " + ex.Message);
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
}
