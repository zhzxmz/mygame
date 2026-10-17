using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
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
using VoxelWorld.View;

// 命名空间 VoxelWorld 内存在同名类型 VoxelWorld.VoxelWorld ⇒ 用别名消除解析歧义
using VoxelWorldData = VoxelWorld.VoxelWorld;

/// <summary>
/// Runtime Core V1（Phase 1）验证：手动请求 → Generation → Mesh → View → Ready 闭环。
///
/// 用法：
///   · Editor 菜单：Tools/VoxelWorld/Validate Runtime Core
///   · 批处理：-executeMethod VoxelWorldRuntimeCoreValidation.RunBatch
///
/// 契约来源：
///   · Specifications/World/VoxelWorld_Runtime_Core_Implementation.md §8（RCI1–RCI15）；
///   · 本文件是 **Editor 批处理验证**（不属于 Runtime Core 层），因此允许使用
///     Generation / Mesh / View 的**具体实现**来制造测试输入与参照结果；
///     被扫描的"禁止项"只针对 `Assets/Scripts/VoxelWorld/Runtime/` 下的 Core 与抽象层源码。
///
/// 分组：
///   A 组（纯 C#，无 GameObject）：用 Fake/真实混合实现驱动 Core，覆盖 RCI1–RCI14；
///   B 组（Unity 侧）：真实 VoxelMeshGeneratorMesher + ChunkViewPresenter + VoxelChunkView 端到端闭环。
/// </summary>
public static class VoxelWorldRuntimeCoreValidation
{
    private static int checks;
    private static int failures;

    private static Material[] sharedMaterials;
    private static bool materialsAreReal;

    private const string RuntimeFolder = "Scripts/VoxelWorld/Runtime";
    private const string AdapterFolder = "Scripts/VoxelWorld/Runtime/Adapters";
    private const string ViewFolder = "Scripts/VoxelWorld/View";

    [MenuItem("Tools/VoxelWorld/Validate Runtime Core")]
    public static void ValidateFromMenu()
    {
        ValidateAll();

        Debug.Log(
            "[VoxelWorldRuntimeCoreValidation] Menu validation result = " +
            (failures == 0 ? "PASS" : "FAIL")
        );
    }

    public static void RunBatch()
    {
        ValidateAll();

        Debug.Log(
            "[VoxelWorldRuntimeCoreValidation] ===== SUMMARY =====\n" +
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

        EnsureEditableScene();
        CreateSharedMaterials();

        try
        {
            Run("闭环：请求 → 生成 → Mesh → 呈递 → Ready", ValidateClosedLoop);
            Run("预算 1/帧（分帧与顺序）", ValidatePerFrameBudget);
            Run("RCI1 生成失败不污染世界", ValidateGenerationFailureDoesNotPollute);
            Run("RCI2 同 Seed 同 Chunk 结果一致", ValidateSameSeedSameResult);
            Run("RCI3 Mesh 失败不清 Dirty", ValidateMeshFailureKeepsDirty);
            Run("RCI4 View 失败不清 Dirty", ValidateViewFailureKeepsDirty);
            Run("RCI5 删除 View 不影响 Voxel 数据", ValidateRemovingViewKeepsVoxelData);
            Run("RCI6 Tick 顺序确定", ValidateTickOrderDeterminism);
            Run("RCI7 ChunkRecord 不是真值", ValidateRecordIsNotWorldTruth);
            Run("RCI8 请求-加载一致", ValidateRequestLoadConsistency);
            Run("RCI9 帧末闭合", ValidateTickExitClosure);
            Run("RCI10 清脏纪律 + 调用序列", ValidateClearDirtyDiscipline);
            Run("RCI11 依赖倒置 + 范围扫描", ValidateDependencyInversionScan);
            Run("RCI12 Meshing 只读", ValidateMeshingIsReadOnly);
            Run("RCI13 所有权与计数一致", ValidateOwnershipAndCounts);
            Run("RCI14 安装全量重写（残留覆盖）", ValidateFullRewriteOnRetry);
            Run("RCI15 既有文件 SHA 不变", ValidateExistingFilesUnchanged);
            Run("Unity 侧真实 View 闭环（含空几何）", ValidateRealViewClosedLoop);
        }
        finally
        {
            DestroySharedMaterials();
        }
    }

    // =====================================================================
    // 闭环：请求 → 生成 → Mesh → 呈递 → Ready（RCI 主干）
    // =====================================================================

    private static void ValidateClosedLoop()
    {
        VoxelWorldData world = new VoxelWorldData();
        BasicTerrainGenerator generator = new BasicTerrainGenerator(WorldGenerationSettings.Default);
        VoxelMeshGeneratorMesher mesher = new VoxelMeshGeneratorMesher();
        FakePresenter presenter = new FakePresenter();
        RuntimeCoreSettings settings = CreateSettings();

        VoxelRuntimeCore core = new VoxelRuntimeCore(world, generator, mesher, presenter, settings);
        VoxelCoordinate coordinate = new VoxelCoordinate(0, 0, 0);

        Check(core.RequestChunk(coordinate), "请求一个 Chunk 成功");
        Check(core.LoadedChunkCount == 0 && world.ChunkCount == 0,
            "请求本身不加载（判定发生在 Tick 的 B/D 阶段）");
        Check(core.TickIndex == 0, "Tick 前序号为 0");

        RuntimeCoreTickReport report = core.Tick();

        Check(report.LoadedThisTick == 1, "D 阶段加载 1 个（实际 " + report.LoadedThisTick + "）");
        Check(report.BuiltThisTick == 1, "F 阶段构建 1 个（实际 " + report.BuiltThisTick + "）");
        Check(report.PresentedThisTick == 1, "G 阶段呈递 1 个（实际 " + report.PresentedThisTick + "）");
        Check(report.InvariantViolationCount == 0,
            "帧末无不变量违规（实际 " + report.InvariantViolationCount + "：" +
            (report.FirstInvariantViolation ?? "-") + "）");
        Check(report.LoadedCount == 1 && report.ReadyCount == 1 && report.DirtyCount == 0,
            "报告计数：loaded=1 / ready=1 / dirty=0（实际 " + report + "）");

        ChunkRecordSnapshot snapshot;
        Check(core.TryGetSnapshot(coordinate, out snapshot), "可取记录快照");
        Check(snapshot.State == ChunkLifecycleState.Ready,
            "状态 = Ready（实际 " + snapshot.State + "）");
        Check(snapshot.HasChunk && !snapshot.IsChunkDirty,
            "Ready 投影：有数据且不脏（" + snapshot + "）");
        Check(!snapshot.NeedsReinstall, "成功后 NeedsReinstall = false");
        Check(snapshot.FailureCount == 0 && snapshot.FailureReason == null, "成功后无失败残留");

        Check(world.ChunkCount == 1, "世界里有 1 个 Chunk");
        Check(presenter.LiveCount == 1, "呈现槽 = 1（实际 " + presenter.LiveCount + "）");

        VoxelChunk chunk;
        Check(world.TryGetChunk(coordinate, out chunk) && chunk != null, "可从世界取回该 Chunk");
        Check(chunk != null && !chunk.IsDirty, "数据已清脏（唯一清脏位置：Apply 成功之后）");

        VoxelChunk reference = generator.Generate(coordinate);
        string detail;
        Check(CompareChunk(chunk, reference, out detail),
            "安装结果 == 生成结果（逐体素；V1 改动集恒为空）：" + (detail ?? "OK"));

        Check(core.Diagnostics.GeneratedTotal == 1 &&
              core.Diagnostics.BuiltTotal == 1 &&
              core.Diagnostics.PresentedTotal == 1,
            "诊断计数：generated/built/presented = 1/1/1（" + core.Diagnostics + "）");
        Check(core.ChangeSet.Count == 0, "V1 改动集恒为空（无 Mining 入口）");
        Check(presenter.CallTrace.Count == 2 &&
              presenter.CallTrace[0].StartsWith("Apply ", StringComparison.Ordinal) &&
              presenter.CallTrace[1].StartsWith("Presented ", StringComparison.Ordinal),
            "呈递调用序列：Apply → Presented（" +
            string.Join(", ", presenter.CallTrace.ToArray()) + "）");

        // 幂等：重复 Tick 不产生额外工作
        RuntimeCoreTickReport report2 = core.Tick();
        Check(report2.LoadedThisTick == 0 && report2.BuiltThisTick == 0 &&
              report2.PresentedThisTick == 0 && report2.UnloadedThisTick == 0,
            "重复 Tick 无额外工作（" + report2 + "）");
        Check(report2.ReadyCount == 1 && report2.InvariantViolationCount == 0,
            "重复 Tick 后仍 Ready 且无违规");
        Check(core.Diagnostics.GeneratedTotal == 1 && core.Diagnostics.BuiltTotal == 1,
            "无重复生成 / 无重复构建");

        // 同一 Tick 内完成"加载 → 构建 → 呈递"（本设置预算充足：loads=8 / pairs=8）
        VoxelCoordinate second = new VoxelCoordinate(1, 0, 0);
        core.RequestChunk(second);
        RuntimeCoreTickReport report3 = core.Tick();
        Check(report3.LoadedThisTick == 1 && report3.BuiltThisTick == 1 &&
              report3.PresentedThisTick == 1,
            "同帧完成 加载+构建+呈递（" + report3 + "）");
        Check(report3.ReadyCount == 2 && report3.DirtyCount == 0,
            "两个 Chunk 都 Ready 且无积压脏（" + report3 + "）");
        Check(report3.InvariantViolationCount == 0, "第 3 帧无不变量违规");
    }

    /// <summary>预算 1/帧：每帧只处理 1 个，顺序 = 坐标升序（用于分帧与顺序断言）。</summary>
    private static void ValidatePerFrameBudget()
    {
        VoxelWorldData world = new VoxelWorldData();
        BasicTerrainGenerator generator = new BasicTerrainGenerator(WorldGenerationSettings.Default);
        RuntimeCoreSettings settings = CreateSettings(loads: 1, pairs: 1);
        VoxelRuntimeCore core = new VoxelRuntimeCore(
            world, generator, new VoxelMeshGeneratorMesher(), new FakePresenter(), settings);

        core.RequestChunk(new VoxelCoordinate(1, 0, 0));
        core.RequestChunk(new VoxelCoordinate(0, 0, 0));

        RuntimeCoreTickReport report1 = core.Tick();
        Check(report1.LoadedThisTick == 1 && report1.BuiltThisTick == 1 &&
              report1.PresentedThisTick == 1,
            "预算 1/帧：首帧只处理 1 个（" + report1 + "）");

        IList<VoxelCoordinate> loaded = core.GetLoadedCoordinates();
        Check(loaded.Count == 1 && loaded[0] == new VoxelCoordinate(0, 0, 0),
            "预算 1/帧：先处理坐标较小者（确定性顺序；实际 " +
            (loaded.Count > 0 ? loaded[0].ToString() : "<none>") + "）");

        core.Tick();
        Check(core.LoadedChunkCount == 2, "预算 1/帧：第二帧补齐");
        RuntimeCoreTickReport report3 = core.Tick();
        Check(report3.LoadedThisTick == 0 && report3.InvariantViolationCount == 0,
            "预算 1/帧：无积压后不再工作（" + report3 + "）");
    }

    // =====================================================================
    // RCI1：生成失败不污染世界
    // =====================================================================

    private static void ValidateGenerationFailureDoesNotPollute()
    {
        CheckGenerationFailure("生成抛异常", 1, 0, 0, 0);
        CheckGenerationFailure("生成返回 null", 0, 1, 0, 0);
        CheckGenerationFailure("生成坐标不自洽", 0, 0, 1, 0);
        CheckGenerationFailure("生成类型非法", 0, 0, 0, 1);
    }

    private static void CheckGenerationFailure(
        string label, int throwTimes, int nullTimes, int wrongCoordinateTimes, int invalidTypeTimes)
    {
        VoxelWorldData world = new VoxelWorldData();
        FaultyGenerator generator = new FaultyGenerator(WorldGenerationSettings.Default);
        generator.ThrowTimes = throwTimes;
        generator.NullTimes = nullTimes;
        generator.WrongCoordinateTimes = wrongCoordinateTimes;
        generator.InvalidTypeTimes = invalidTypeTimes;

        FakePresenter presenter = new FakePresenter();
        VoxelRuntimeCore core = new VoxelRuntimeCore(
            world, generator, new VoxelMeshGeneratorMesher(), presenter, CreateSettings());

        VoxelCoordinate coordinate = new VoxelCoordinate(0, 0, 0);
        core.RequestChunk(coordinate);
        core.Tick();

        Check(world.ChunkCount == 0,
            "RCI1/" + label + "：世界未被污染（ChunkCount = " + world.ChunkCount + "）");
        Check(presenter.ApplyCalls == 0,
            "RCI1/" + label + "：未触碰表示层（ApplyCalls = " + presenter.ApplyCalls + "）");
        Check(core.Diagnostics.GeneratedTotal == 0,
            "RCI1/" + label + "：未计数为生成成功");

        ChunkRecordSnapshot snapshot;
        Check(core.TryGetSnapshot(coordinate, out snapshot) &&
              snapshot.State == ChunkLifecycleState.Failed,
            "RCI1/" + label + "：记录为 Failed（" + snapshot + "）");
        Check(snapshot != null && !string.IsNullOrEmpty(snapshot.FailureReason),
            "RCI1/" + label + "：失败原因非空：" + (snapshot != null ? snapshot.FailureReason : "<null>"));
        Check(snapshot != null && !snapshot.HasChunk && snapshot.NeedsReinstall,
            "RCI1/" + label + "：无数据 + NeedsReinstall = true（重试走完整生成）");

        // 故障注入用完 ⇒ 下一次 Tick 自动重试并成功
        RuntimeCoreTickReport report = core.Tick();

        ChunkRecordSnapshot retried;
        core.TryGetSnapshot(coordinate, out retried);
        Check(retried != null && retried.State == ChunkLifecycleState.Ready,
            "RCI1/" + label + "：重试成功 → Ready（" + retried + "）");
        Check(world.ChunkCount == 1 && report.InvariantViolationCount == 0,
            "RCI1/" + label + "：重试后世界与记录一致（" + report + "）");
        Check(report.LoadedThisTick == 1 && report.PresentedThisTick == 1,
            "RCI1/" + label + "：重试帧计入 load+1 / present+1（" + report + "）");
    }

    // =====================================================================
    // RCI2：同 Seed 同 Chunk 结果一致
    // =====================================================================

    private static void ValidateSameSeedSameResult()
    {
        VoxelCoordinate coordinate = new VoxelCoordinate(-1, 1, 2);

        VoxelWorldData worldA = new VoxelWorldData();
        VoxelWorldData worldB = new VoxelWorldData();
        BasicTerrainGenerator generatorA = new BasicTerrainGenerator(WorldGenerationSettings.Default);
        BasicTerrainGenerator generatorB = new BasicTerrainGenerator(WorldGenerationSettings.Default);

        RunSingleChunk(worldA, generatorA, coordinate);
        RunSingleChunk(worldB, generatorB, coordinate);

        VoxelChunk chunkA;
        VoxelChunk chunkB;
        worldA.TryGetChunk(coordinate, out chunkA);
        worldB.TryGetChunk(coordinate, out chunkB);

        string detail;
        Check(CompareChunk(chunkA, chunkB, out detail),
            "RCI2：两个独立世界（同 Seed）结果逐体素一致（" + (detail ?? "OK") + "）");

        ulong hashA = ComputeMeshHash(chunkA);
        ulong hashB = ComputeMeshHash(chunkB);
        Check(hashA == hashB, "RCI2：派生 Mesh 内容哈希一致（" + hashA.ToString("X16") + "）");

        VoxelChunk reference = generatorA.Generate(coordinate);
        Check(CompareChunk(chunkA, reference, out detail),
            "RCI2：安装结果 == 独立生成结果（" + (detail ?? "OK") + "）");

        // 反例保护：不同 Seed 必须产生不同结果（避免断言恒真）
        WorldGenerationSettings other = WorldGenerationSettings.Default;
        other.Seed = WorldGenerationSettings.Default.Seed + 12345;
        VoxelChunk different = new BasicTerrainGenerator(other).Generate(coordinate);
        Check(!CompareChunk(chunkA, different, out detail),
            "RCI2：不同 Seed 的结果确实不同（反例保护，差异：" + (detail ?? "-") + "）");
    }

    private static void RunSingleChunk(
        VoxelWorldData world, IWorldGenerator generator, VoxelCoordinate coordinate)
    {
        VoxelRuntimeCore core = new VoxelRuntimeCore(
            world, generator, new VoxelMeshGeneratorMesher(), new FakePresenter(), CreateSettings());
        core.RequestChunk(coordinate);
        core.Tick();
    }

    // =====================================================================
    // RCI3：Mesh 失败不清 Dirty
    // =====================================================================

    private static void ValidateMeshFailureKeepsDirty()
    {
        // F-1：TryBuild 返回 false
        VoxelWorldData world = new VoxelWorldData();
        BasicTerrainGenerator generator = new BasicTerrainGenerator(WorldGenerationSettings.Default);
        FakeMesher mesher = new FakeMesher();
        mesher.FailCount = 1;
        FakePresenter presenter = new FakePresenter();
        VoxelRuntimeCore core = new VoxelRuntimeCore(world, generator, mesher, presenter, CreateSettings());

        VoxelCoordinate coordinate = new VoxelCoordinate(0, 1, 0);
        core.RequestChunk(coordinate);
        core.Tick();

        ChunkRecordSnapshot snapshot;
        core.TryGetSnapshot(coordinate, out snapshot);
        Check(snapshot != null && snapshot.State == ChunkLifecycleState.Dirty,
            "RCI3：Mesher 返回 false ⇒ 状态回 Dirty（" + snapshot + "）");

        VoxelChunk chunk;
        world.TryGetChunk(coordinate, out chunk);
        Check(chunk != null && chunk.IsDirty, "RCI3：不清 Dirty（IsDirty 仍为 true）");
        Check(presenter.ApplyCalls == 0, "RCI3：未调用 Apply（构建失败不呈递）");
        Check(core.Diagnostics.BuildFailures == 1, "RCI3：BuildFailures 计数 = 1");
        Check(snapshot != null && !string.IsNullOrEmpty(snapshot.FailureReason),
            "RCI3：失败原因非空：" + (snapshot != null ? snapshot.FailureReason : "<null>"));

        core.Tick();
        core.TryGetSnapshot(coordinate, out snapshot);
        Check(snapshot != null && snapshot.State == ChunkLifecycleState.Ready,
            "RCI3：下一 Tick 重试成功 → Ready（" + snapshot + "）");
        Check(chunk != null && !chunk.IsDirty, "RCI3：成功后才清脏");

        // F-2：TryBuild 抛异常（实现违规，Core 必须防御性捕获）
        VoxelWorldData world2 = new VoxelWorldData();
        FakeMesher throwing = new FakeMesher();
        throwing.ThrowAlways = true;
        FakePresenter presenter2 = new FakePresenter();
        VoxelRuntimeCore core2 = new VoxelRuntimeCore(
            world2, new BasicTerrainGenerator(WorldGenerationSettings.Default), throwing,
            presenter2, CreateSettings());

        core2.RequestChunk(coordinate);
        core2.Tick();

        ChunkRecordSnapshot snapshot2;
        core2.TryGetSnapshot(coordinate, out snapshot2);
        Check(snapshot2 != null && snapshot2.State == ChunkLifecycleState.Dirty,
            "RCI3：Mesher 抛异常 ⇒ 防御性捕获并回 Dirty（" + snapshot2 + "）");
        VoxelChunk chunk2;
        world2.TryGetChunk(coordinate, out chunk2);
        Check(chunk2 != null && chunk2.IsDirty, "RCI3：异常路径也不清 Dirty");
        Check(presenter2.ApplyCalls == 0 && core2.Diagnostics.BuildFailures >= 1,
            "RCI3：异常路径不呈递且计数可见");

        // 超过重试上限 ⇒ Failed，但必须保持 Dirty（禁止"永久过期表示"）
        for (int i = 0; i < 8; i++) core2.Tick();
        core2.TryGetSnapshot(coordinate, out snapshot2);
        Check(snapshot2 != null && snapshot2.State == ChunkLifecycleState.Failed,
            "RCI3：超过 MaxRetriesPerChunk ⇒ Failed（" + snapshot2 + "）");
        Check(chunk2 != null && chunk2.IsDirty,
            "RCI3：Failed 状态下仍保持 IsDirty == true（Runtime_Core §9.2-4）");
        int failedCalls = throwing.Calls;
        core2.Tick();
        Check(throwing.Calls == failedCalls, "RCI3：Failed 后停止自动重试（不再调用 Mesher）");

        core2.RetryFailedChunks();
        core2.Tick();
        Check(throwing.Calls > failedCalls, "RCI3：RetryFailedChunks 之后恢复重试");
    }

    // =====================================================================
    // RCI4：View 失败不清 Dirty
    // =====================================================================

    private static void ValidateViewFailureKeepsDirty()
    {
        VoxelWorldData world = new VoxelWorldData();
        BasicTerrainGenerator generator = new BasicTerrainGenerator(WorldGenerationSettings.Default);
        FakePresenter presenter = new FakePresenter();
        presenter.FailApplyCount = 1;
        VoxelRuntimeCore core = new VoxelRuntimeCore(
            world, generator, new VoxelMeshGeneratorMesher(), presenter, CreateSettings());

        VoxelCoordinate coordinate = new VoxelCoordinate(0, 1, 0);
        core.RequestChunk(coordinate);
        core.Tick();

        ChunkRecordSnapshot snapshot;
        core.TryGetSnapshot(coordinate, out snapshot);
        Check(snapshot != null && snapshot.State == ChunkLifecycleState.Dirty,
            "RCI4：Apply 失败 ⇒ 状态回 Dirty（" + snapshot + "）");
        VoxelChunk chunk;
        world.TryGetChunk(coordinate, out chunk);
        Check(chunk != null && chunk.IsDirty, "RCI4：不清 Dirty");
        Check(presenter.ApplyCalls == 1 && core.Diagnostics.PresentFailures == 1,
            "RCI4：失败可观测（Apply 1 次 / PresentFailures = 1）");

        core.Tick();
        core.TryGetSnapshot(coordinate, out snapshot);
        Check(snapshot != null && snapshot.State == ChunkLifecycleState.Ready && !chunk.IsDirty,
            "RCI4：下一 Tick 重试成功 → Ready 且清脏");

        // View 抛异常
        VoxelWorldData world2 = new VoxelWorldData();
        FakePresenter throwing = new FakePresenter();
        throwing.ThrowOnApply = true;
        VoxelRuntimeCore core2 = new VoxelRuntimeCore(
            world2, generator, new VoxelMeshGeneratorMesher(), throwing, CreateSettings());
        core2.RequestChunk(coordinate);
        core2.Tick();

        ChunkRecordSnapshot snapshot2;
        core2.TryGetSnapshot(coordinate, out snapshot2);
        VoxelChunk chunk2;
        world2.TryGetChunk(coordinate, out chunk2);
        Check(snapshot2 != null && snapshot2.State == ChunkLifecycleState.Dirty,
            "RCI4：Apply 抛异常 ⇒ 防御性捕获并回 Dirty");
        Check(chunk2 != null && chunk2.IsDirty, "RCI4：异常路径不清 Dirty");

        // OutcomeMismatch：空几何却声称 Presented ⇒ 失败 + 计数 + 不清脏
        VoxelWorldData world3 = new VoxelWorldData();
        FakePresenter liar = new FakePresenter();
        liar.ClaimsGeometryAlways = true;
        VoxelRuntimeCore core3 = new VoxelRuntimeCore(
            world3, generator, new VoxelMeshGeneratorMesher(), liar, CreateSettings());

        VoxelCoordinate emptyCoordinate = new VoxelCoordinate(0, 3, 0);   // 全 Air（地形高度 24，本层 48..63）
        core3.RequestChunk(emptyCoordinate);
        core3.Tick();

        ChunkRecordSnapshot snapshot3;
        core3.TryGetSnapshot(emptyCoordinate, out snapshot3);
        VoxelChunk chunk3;
        world3.TryGetChunk(emptyCoordinate, out chunk3);
        Check(snapshot3 != null && snapshot3.State == ChunkLifecycleState.Dirty,
            "RCI4：OutcomeMismatch ⇒ 视为失败并回 Dirty（" + snapshot3 + "）");
        Check(core3.Diagnostics.OutcomeMismatches == 1, "RCI4：OutcomeMismatches 计数 = 1");
        Check(chunk3 != null && chunk3.IsDirty, "RCI4：OutcomeMismatch 不清 Dirty");

        // 正确实现：空几何 → Empty 也是成功
        VoxelWorldData world4 = new VoxelWorldData();
        FakePresenter correct = new FakePresenter();
        VoxelRuntimeCore core4 = new VoxelRuntimeCore(
            world4, generator, new VoxelMeshGeneratorMesher(), correct, CreateSettings());
        core4.RequestChunk(emptyCoordinate);
        RuntimeCoreTickReport report = core4.Tick();

        ChunkRecordSnapshot snapshot4;
        core4.TryGetSnapshot(emptyCoordinate, out snapshot4);
        Check(snapshot4 != null && snapshot4.State == ChunkLifecycleState.Ready,
            "RCI4：空几何 → Empty 是成功（Ready）");
        Check(report.EmptyPresentedThisTick == 1, "RCI4：EmptyPresentedThisTick = 1");
        Check(core4.Diagnostics.EmptyPresentedTotal == 1, "RCI4：EmptyPresentedTotal = 1");
    }

    // =====================================================================
    // RCI5：删除 View 不影响 Voxel 数据
    // =====================================================================

    private static void ValidateRemovingViewKeepsVoxelData()
    {
        VoxelWorldData world = new VoxelWorldData();
        BasicTerrainGenerator generator = new BasicTerrainGenerator(WorldGenerationSettings.Default);
        FakePresenter presenter = new FakePresenter();
        VoxelRuntimeCore core = new VoxelRuntimeCore(
            world, generator, new VoxelMeshGeneratorMesher(), presenter, CreateSettings());

        VoxelCoordinate coordinate = new VoxelCoordinate(0, 1, 0);
        core.RequestChunk(coordinate);
        core.Tick();

        VoxelChunk chunk;
        world.TryGetChunk(coordinate, out chunk);
        VoxelType[] before = SnapshotVoxels(chunk);
        ulong hashBefore = ComputeMeshHash(chunk);
        int countBefore = world.ChunkCount;

        // 直接删除表示（模拟表示层被清空/销毁，不经 Core）
        presenter.Release(coordinate);
        presenter.ReleaseAll();
        Check(presenter.ReleaseCalls >= 1, "RCI5：Release 被调用");

        Check(world.ChunkCount == countBefore, "RCI5：世界 Chunk 数不变");
        string detail;
        Check(CompareVoxels(chunk, before, out detail),
            "RCI5：删除 View 不改变 Voxel 数据（4096 体素）：" + (detail ?? "OK"));
        Check(ComputeMeshHash(chunk) == hashBefore, "RCI5：由数据派生的 Mesh 内容哈希不变");

        // 正常卸载 ⇒ 数据按设计移除；重新加载后与首次完全一致（卸载-重载一致）
        core.ReleaseChunk(coordinate);
        core.Tick();
        Check(world.ChunkCount == 0 && core.LoadedChunkCount == 0, "RCI5：卸载即净（数据 + 记录）");
        Check(presenter.LiveCount == 0, "RCI5：卸载后呈现槽回基线（LiveCount = 0）");

        core.RequestChunk(coordinate);
        core.Tick();
        VoxelChunk reloaded;
        world.TryGetChunk(coordinate, out reloaded);
        Check(CompareVoxels(reloaded, before, out detail),
            "RCI5：卸载-重载后数据与首次一致（" + (detail ?? "OK") + "）");
        Check(ComputeMeshHash(reloaded) == hashBefore, "RCI5：卸载-重载后内容哈希一致");
    }

    // =====================================================================
    // RCI6：Tick 顺序确定
    // =====================================================================

    private static void ValidateTickOrderDeterminism()
    {
        VoxelCoordinate[] forward =
        {
            new VoxelCoordinate(0, 1, 0),
            new VoxelCoordinate(2, 1, 1),
            new VoxelCoordinate(1, 1, 0)
        };

        VoxelCoordinate[] reversed = { forward[2], forward[1], forward[0] };

        string[] sequenceA = RunDeterminismRig(forward);
        string[] sequenceB = RunDeterminismRig(reversed);

        Check(sequenceA.Length == sequenceB.Length,
            "RCI6：两条运行的 Tick 数相同（" + sequenceA.Length + "）");

        int differences = 0;
        for (int i = 0; i < Mathf.Min(sequenceA.Length, sequenceB.Length); i++)
        {
            if (sequenceA[i] != sequenceB[i]) differences++;
        }

        Check(differences == 0,
            "RCI6：请求顺序不影响 Tick 序列（差异 " + differences + " 条）");
        Check(sequenceA.Length > 0 && sequenceA[0].Contains("load+1"),
            "RCI6：预算 1/帧 ⇒ 首帧只加载 1 个（" + sequenceA[0] + "）");
        Check(sequenceA[sequenceA.Length - 1].Contains("ready=3"),
            "RCI6：最终 3 个 Chunk 全部 Ready（" + sequenceA[sequenceA.Length - 1] + "）");

        Debug.Log("[VoxelWorldRuntimeCoreValidation] Tick 序列（请求顺序 A）：\n" +
                  string.Join("\n", sequenceA));
    }

    private static string[] RunDeterminismRig(VoxelCoordinate[] requestOrder)
    {
        VoxelWorldData world = new VoxelWorldData();
        BasicTerrainGenerator generator = new BasicTerrainGenerator(WorldGenerationSettings.Default);
        RuntimeCoreSettings settings = CreateSettings(loads: 1, pairs: 1);
        VoxelRuntimeCore core = new VoxelRuntimeCore(
            world, generator, new VoxelMeshGeneratorMesher(), new FakePresenter(), settings);

        for (int i = 0; i < requestOrder.Length; i++) core.RequestChunk(requestOrder[i]);

        List<string> lines = new List<string>();

        for (int tick = 0; tick < 8; tick++)
        {
            RuntimeCoreTickReport report = core.Tick();
            lines.Add(report.ToString() + " || " + SnapshotAll(core));
        }

        return lines.ToArray();
    }

    private static string SnapshotAll(VoxelRuntimeCore core)
    {
        StringBuilder text = new StringBuilder();
        IList<VoxelCoordinate> loaded = core.GetLoadedCoordinates();

        for (int i = 0; i < loaded.Count; i++)
        {
            ChunkRecordSnapshot snapshot;
            if (core.TryGetSnapshot(loaded[i], out snapshot))
            {
                text.Append('[').Append(snapshot).Append(']');
            }
        }

        return text.ToString();
    }

    // =====================================================================
    // RCI7：ChunkRecord 不是真值
    // =====================================================================

    private static void ValidateRecordIsNotWorldTruth()
    {
        // 结构 1：快照类型不得含数组 / 集合（只允许值类型、枚举、字符串）
        PropertyInfo[] properties =
            typeof(ChunkRecordSnapshot).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        int badProperties = 0;
        for (int i = 0; i < properties.Length; i++)
        {
            Type type = properties[i].PropertyType;
            if (type == typeof(string)) continue;
            if (type.IsArray) { badProperties++; continue; }
            if (typeof(IEnumerable).IsAssignableFrom(type)) { badProperties++; continue; }
        }

        Check(properties.Length > 0 && badProperties == 0,
            "RCI7：ChunkRecordSnapshot 的 " + properties.Length +
            " 个属性都不含数组/集合（越界 " + badProperties + "）");

        // 结构 2：ChunkRecord 为 internal ⇒ 不可通过公开 API 暴露可变记录
        Type recordType = typeof(VoxelRuntimeCore).Assembly.GetType("VoxelWorld.Runtime.ChunkRecord");
        Check(recordType != null && !recordType.IsPublic,
            "RCI7：ChunkRecord 是 internal（外部拿不到可变记录）");

        MethodInfo[] methods = typeof(VoxelRuntimeCore).GetMethods(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);

        int exposed = 0;
        for (int i = 0; i < methods.Length; i++)
        {
            if (recordType != null && methods[i].ReturnType == recordType) exposed++;
        }

        Check(exposed == 0, "RCI7：公开 API 不返回 ChunkRecord（越界 " + exposed + "）");

        // 行为：清空记录（卸载全部）后世界数据仍由"生成 + 数据层"决定
        VoxelWorldData world = new VoxelWorldData();
        BasicTerrainGenerator generator = new BasicTerrainGenerator(WorldGenerationSettings.Default);
        FakePresenter presenter = new FakePresenter();
        VoxelRuntimeCore core = new VoxelRuntimeCore(
            world, generator, new VoxelMeshGeneratorMesher(), presenter, CreateSettings());

        VoxelCoordinate coordinate = new VoxelCoordinate(0, 1, 0);
        core.RequestChunk(coordinate);
        core.Tick();

        VoxelChunk chunk;
        world.TryGetChunk(coordinate, out chunk);
        VoxelType[] expected = SnapshotVoxels(chunk);

        core.ClearRequests();
        core.Tick();
        Check(core.GetLoadedCoordinates().Count == 0 && presenter.LiveCount == 0,
            "RCI7：清空请求后记录与表示都清空");

        core.RequestChunk(coordinate);
        core.Tick();
        VoxelChunk again;
        world.TryGetChunk(coordinate, out again);
        string detail;
        Check(CompareVoxels(again, expected, out detail),
            "RCI7：真值来自生成 + 数据层（记录清空后重建结果一致）：" + (detail ?? "OK"));
    }

    // =====================================================================
    // RCI8：请求-加载一致
    // =====================================================================

    private static void ValidateRequestLoadConsistency()
    {
        VoxelWorldData world = new VoxelWorldData();
        BasicTerrainGenerator generator = new BasicTerrainGenerator(WorldGenerationSettings.Default);
        FakePresenter presenter = new FakePresenter();
        RuntimeCoreSettings settings = CreateSettings();
        VoxelRuntimeCore core = new VoxelRuntimeCore(
            world, generator, new VoxelMeshGeneratorMesher(), presenter, settings);

        VoxelCoordinate outside = new VoxelCoordinate(0, 99, 0);
        Check(!core.RequestChunk(outside), "RCI8：垂直范围外的请求被拒绝");
        Check(core.Diagnostics.RejectedRequests == 1,
            "RCI8：拒绝计数可见（" + core.Diagnostics.RejectedRequests + "）");

        VoxelCoordinate coordinate = new VoxelCoordinate(0, 1, 0);
        Check(core.RequestChunk(coordinate), "RCI8：首次请求成功");
        Check(!core.RequestChunk(coordinate), "RCI8：重复请求幂等（返回 false）");
        Check(core.GetRequestedCoordinates().Count == 1, "RCI8：请求集合大小 = 1");

        core.Tick();
        Check(core.Diagnostics.GeneratedTotal == 1, "RCI8：重复请求不导致重复生成");
        Check(core.LoadedChunkCount == 1 && world.ChunkCount == 1, "RCI8：加载集合 = 请求集合");

        IList<VoxelCoordinate> loaded = core.GetLoadedCoordinates();
        Check(loaded.Count == 1 && loaded[0] == coordinate, "RCI8：已加载坐标 == 请求坐标");

        core.ReleaseChunk(coordinate);
        core.Tick();
        Check(world.ChunkCount == 0 && core.GetLoadedCoordinates().Count == 0,
            "RCI8：撤销请求后卸载（数据 + 记录）");
        Check(!core.ReleaseChunk(coordinate), "RCI8：重复释放幂等（返回 false）");

        // 释放后世界里没有无主 Chunk
        Check(world.ChunkCount == core.LoadedChunkCount,
            "RCI8：world.ChunkCount == 已加载记录数（" + world.ChunkCount + "）");
    }

    // =====================================================================
    // RCI9：帧末闭合
    // =====================================================================

    private static void ValidateTickExitClosure()
    {
        VoxelWorldData world = new VoxelWorldData();
        BasicTerrainGenerator generator = new BasicTerrainGenerator(WorldGenerationSettings.Default);
        FakeMesher mesher = new FakeMesher();
        mesher.FailCount = 2;
        FakePresenter presenter = new FakePresenter();
        RuntimeCoreSettings settings = CreateSettings(loads: 2, pairs: 1, retries: 1);
        VoxelRuntimeCore core = new VoxelRuntimeCore(world, generator, mesher, presenter, settings);

        core.RequestChunk(new VoxelCoordinate(0, 1, 0));
        core.RequestChunk(new VoxelCoordinate(1, 1, 0));
        core.RequestChunk(new VoxelCoordinate(0, 3, 0));

        int transientSeen = 0;
        int violations = 0;

        for (int tick = 0; tick < 6; tick++)
        {
            RuntimeCoreTickReport report = core.Tick();
            violations += report.InvariantViolationCount;

            IList<VoxelCoordinate> loaded = core.GetLoadedCoordinates();
            for (int i = 0; i < loaded.Count; i++)
            {
                ChunkRecordSnapshot snapshot;
                if (!core.TryGetSnapshot(loaded[i], out snapshot)) continue;

                if (snapshot.State == ChunkLifecycleState.Generating ||
                    snapshot.State == ChunkLifecycleState.Building ||
                    snapshot.State == ChunkLifecycleState.Unloading)
                {
                    transientSeen++;
                }
            }
        }

        Check(transientSeen == 0,
            "RCI9：任一 Tick 返回后不存在 Generating/Building/Unloading（实际 " + transientSeen + "）");
        Check(violations == 0,
            "RCI9：6 次 Tick 累计不变量违规 = 0（实际 " + violations + "）");
        Check(core.LoadedChunkCount == 3 && world.ChunkCount == 3,
            "RCI9：最终 3 个 Chunk 都在世界与注册表中");
    }

    // =====================================================================
    // RCI10：清脏纪律 + 调用序列
    // =====================================================================

    private static void ValidateClearDirtyDiscipline()
    {
        List<string> trace = new List<string>();

        VoxelWorldData world = new VoxelWorldData();
        BasicTerrainGenerator generator = new BasicTerrainGenerator(WorldGenerationSettings.Default);
        FakeMesher mesher = new FakeMesher();
        mesher.Trace = trace;
        FakePresenter presenter = new FakePresenter();
        presenter.Trace = trace;

        VoxelRuntimeCore core = new VoxelRuntimeCore(world, generator, mesher, presenter, CreateSettings());
        VoxelCoordinate coordinate = new VoxelCoordinate(0, 1, 0);
        core.RequestChunk(coordinate);
        core.Tick();

        Check(mesher.LastSeenIsDirty,
            "RCI10：构建时数据仍是脏的（清脏发生在呈递之后，而不是之前）");
        Check(trace.Count == 2 && trace[0].StartsWith("TryBuild") && trace[1].StartsWith("Apply"),
            "RCI10：调用序列 = TryBuild → Apply（" + string.Join(" → ", trace.ToArray()) + "）");

        VoxelChunk chunk;
        world.TryGetChunk(coordinate, out chunk);
        Check(chunk != null && !chunk.IsDirty, "RCI10：呈递成功后才清脏（Ready 且 IsDirty = false）");

        // 失败路径不得清脏：注入失败后 IsDirty 必须仍为 true
        VoxelWorldData world2 = new VoxelWorldData();
        FakePresenter failing = new FakePresenter();
        failing.FailApplyCount = 1;
        VoxelRuntimeCore core2 = new VoxelRuntimeCore(
            world2, generator, new VoxelMeshGeneratorMesher(), failing, CreateSettings());
        core2.RequestChunk(coordinate);
        core2.Tick();
        VoxelChunk chunk2;
        world2.TryGetChunk(coordinate, out chunk2);
        Check(chunk2 != null && chunk2.IsDirty,
            "RCI10：呈递失败路径不清脏（IsDirty = true）");
    }

    // =====================================================================
    // RCI11：依赖倒置 + 范围扫描
    // =====================================================================

    private static readonly string[] CoreForbiddenPatterns =
    {
        "UnityEngine", "UnityEditor", "MonoBehaviour", "GameObject", "Transform",
        @"\bVector2\b", @"\bVector3\b", @"\bBounds\b",
        @"(?<!\.)\bMesh\b",   // Unity 的 Mesh 类型；排除 "using VoxelWorld.Mesh;"（命名空间导入）
        "Material",
        @"Time\.", @"\bRandom\b", "FindObjectOfType", @"Camera\.main",
        "ClearDirtyFlags", "BasicTerrainGenerator", "VoxelMeshGenerator",
        "VoxelChunkView", "UnityMeshBuilder", "new VoxelChunk",
        @"\.Vertices", @"\.Normals", @"\.UVs", "ChunkLoader", "VoxelWorldRuntime"
    };

    private static void ValidateDependencyInversionScan()
    {
        string coreFolder = Path.Combine(Application.dataPath, RuntimeFolder);
        string abstractionFolder = Path.Combine(coreFolder, "Abstractions");

        Check(Directory.Exists(coreFolder), "RCI11：Runtime 目录存在（" + coreFolder + "）");
        Check(Directory.Exists(abstractionFolder), "RCI11：Abstractions 目录存在");

        if (!Directory.Exists(coreFolder)) return;

        string[] coreFiles = Directory.GetFiles(coreFolder, "*.cs", SearchOption.TopDirectoryOnly);
        string[] abstractionFiles = Directory.Exists(abstractionFolder)
            ? Directory.GetFiles(abstractionFolder, "*.cs", SearchOption.TopDirectoryOnly)
            : new string[0];

        Array.Sort(coreFiles, StringComparer.Ordinal);
        Array.Sort(abstractionFiles, StringComparer.Ordinal);

        Check(coreFiles.Length >= 8, "RCI11：Runtime 层至少 8 个源文件（实际 " + coreFiles.Length + "）");
        Check(abstractionFiles.Length >= 2, "RCI11：抽象层至少 2 个源文件（实际 " + abstractionFiles.Length + "）");

        foreach (string expected in new string[]
        {
            "VoxelRuntimeCore.cs", "ChunkRecord.cs", "ChunkRecordSnapshot.cs",
            "ChunkLifecycleState.cs", "RuntimeCoreSettings.cs", "RuntimeCoreTickReport.cs",
            "RuntimeCoreDiagnostics.cs", "RuntimeChangeSet.cs"
        })
        {
            Check(File.Exists(Path.Combine(coreFolder, expected)), "RCI11：存在 Runtime/" + expected);
        }

        foreach (string expected in new string[] { "IChunkMesher.cs", "IChunkPresenter.cs" })
        {
            Check(File.Exists(Path.Combine(abstractionFolder, expected)),
                "RCI11：存在 Abstractions/" + expected);
        }

        // 扫描器自检（保证"扫描能真的抓到东西"）
        string probe = "using UnityEngine;\n" +
                       "// 注释里的 UnityEngine 不应计数\n" +
                       "void P(){ var x = new GameObject(); }\n";
        List<string> probeHits = ScanTokens(StripComments(probe), CoreForbiddenPatterns);
        Check(probeHits.Count == 2 && probeHits.Contains("UnityEngine") &&
              probeHits.Contains("GameObject"),
            "RCI11：扫描器自检（代码区命中 " + probeHits.Count + "：" +
            string.Join(", ", probeHits.ToArray()) + "）");

        string commentOnlyProbe = "// UnityEngine GameObject Transform\n" +
                                  "/* UnityEngine Mesh */\n" +
                                  "void Q(){ }\n";
        Check(ScanTokens(StripComments(commentOnlyProbe), CoreForbiddenPatterns).Count == 0,
            "RCI11：扫描器忽略注释（仅注释中的禁词不得计数）");

        int hits = 0;
        List<string> files = new List<string>();
        files.AddRange(coreFiles);
        files.AddRange(abstractionFiles);

        for (int i = 0; i < files.Count; i++)
        {
            string code = StripComments(File.ReadAllText(files[i], Encoding.UTF8));
            List<string> found = ScanTokens(code, CoreForbiddenPatterns);

            if (found.Count > 0)
            {
                hits += found.Count;
                Check(false, "RCI11：" + Path.GetFileName(files[i]) + " 命中禁止项：" +
                             string.Join(", ", found.ToArray()));
            }
        }

        Check(hits == 0,
            "RCI11：Runtime/ 与 Abstractions/ 共 " + files.Count +
            " 个文件不含 Unity 类型、具体实现名、ClearDirtyFlags、" +
            "new VoxelChunk、.Vertices/.Normals/.UVs（命中 " + hits + "）");

        // 正向对照：适配器**必须**命名 VoxelMeshGenerator（否则说明适配器没起作用）
        string adapterPath = Path.Combine(Application.dataPath, "Scripts/VoxelWorld/Runtime/Adapters/VoxelMeshGeneratorMesher.cs");
        Check(File.Exists(adapterPath), "RCI11：适配器文件存在");
        if (File.Exists(adapterPath))
        {
            string adapterCode = StripComments(File.ReadAllText(adapterPath, Encoding.UTF8));
            Check(adapterCode.Contains("VoxelMeshGenerator"),
                "RCI11：适配器是唯一命名 VoxelMeshGenerator 的位置（正向对照）");
            Check(!adapterCode.Contains("UnityEngine"),
                "RCI11：适配器仍为纯 C#（无 UnityEngine）");
        }

        // Core 不得暴露"另一个 Core"或实现细节类型
        Check(!File.ReadAllText(coreFiles[0], Encoding.UTF8).Contains("MonoBehaviour"),
            "RCI11：抽样检查第 1 个 Core 文件（" + Path.GetFileName(coreFiles[0]) + "）不含 MonoBehaviour");

        Debug.Log("[VoxelWorldRuntimeCoreValidation] 扫描范围（" + files.Count + " 文件）：\n" +
                  string.Join("\n", files));
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
    // RCI12：Meshing 只读
    // =====================================================================

    private static void ValidateMeshingIsReadOnly()
    {
        VoxelWorldData world = new VoxelWorldData();
        BasicTerrainGenerator generator = new BasicTerrainGenerator(WorldGenerationSettings.Default);
        VoxelMeshGeneratorMesher mesher = new VoxelMeshGeneratorMesher();
        VoxelRuntimeCore core = new VoxelRuntimeCore(
            world, generator, mesher, new FakePresenter(), CreateSettings());

        VoxelCoordinate coordinate = new VoxelCoordinate(0, 1, 0);
        core.RequestChunk(coordinate);
        core.Tick();

        VoxelChunk chunk;
        world.TryGetChunk(coordinate, out chunk);
        Check(chunk != null, "RCI12：取回 Chunk");

        VoxelType[] before = SnapshotVoxels(chunk);
        VoxelCoordinate coordinateBefore = chunk.Coordinate;
        bool dirtyBefore = chunk.IsDirty;
        ulong hashBefore = ComputeMeshHash(chunk);

        VoxelMeshData meshData;
        string error;
        bool built = mesher.TryBuild(chunk, out meshData, out error);

        Check(built && meshData != null && string.IsNullOrEmpty(error),
            "RCI12：直接调用适配器成功");
        string detail;
        Check(CompareVoxels(chunk, before, out detail),
            "RCI12：Meshing 前后 4096 体素不变（" + (detail ?? "OK") + "）");
        Check(chunk.Coordinate == coordinateBefore, "RCI12：Meshing 不修改 Chunk 坐标");
        Check(chunk.IsDirty == dirtyBefore, "RCI12：Meshing 不修改脏标记");
        Check(ComputeMeshHash(chunk) == hashBefore, "RCI12：重复 Meshing 内容哈希一致（幂等）");
    }

    // =====================================================================
    // RCI13：所有权与计数一致
    // =====================================================================

    private static void ValidateOwnershipAndCounts()
    {
        VoxelWorldData world = new VoxelWorldData();
        BasicTerrainGenerator generator = new BasicTerrainGenerator(WorldGenerationSettings.Default);
        FakePresenter presenter = new FakePresenter();
        VoxelRuntimeCore core = new VoxelRuntimeCore(
            world, generator, new VoxelMeshGeneratorMesher(), presenter, CreateSettings());

        VoxelCoordinate[] coordinates =
        {
            new VoxelCoordinate(0, 1, 0),
            new VoxelCoordinate(1, 1, 0),
            new VoxelCoordinate(0, 3, 0)
        };

        for (int i = 0; i < coordinates.Length; i++) core.RequestChunk(coordinates[i]);

        for (int tick = 0; tick < 3; tick++)
        {
            RuntimeCoreTickReport report = core.Tick();
            Check(world.ChunkCount == core.LoadedChunkCount,
                "RCI13：world.ChunkCount(" + world.ChunkCount + ") == 记录数(" +
                core.LoadedChunkCount + ")");
            Check(report.InvariantViolationCount == 0,
                "RCI13：第 " + report.TickIndex + " 帧无违规（" +
                (report.FirstInvariantViolation ?? "-") + "）");
        }

        Check(presenter.LiveCount == 3, "RCI13：3 个表示槽");

        // 卸载全部后 Core 仍可继续使用（未 Dispose world / 实现）
        core.ClearRequests();
        core.Tick();
        core.Tick();
        Check(world.ChunkCount == 0 && core.LoadedChunkCount == 0 && presenter.LiveCount == 0,
            "RCI13：卸载全部后世界、记录、表示都清空（Core 未 Dispose 依赖）");

        core.RequestChunk(coordinates[0]);
        core.Tick();
        Check(core.LoadedChunkCount == 1 && world.ChunkCount == 1,
            "RCI13：卸载全部后仍可继续加载（world / generator / mesher / presenter 仍可用）");
        Check(core.Diagnostics.UnloadedTotal == 3,
            "RCI13：卸载计数 = 3（实际 " + core.Diagnostics.UnloadedTotal + "）");
    }

    // =====================================================================
    // RCI14：安装全量重写（残留覆盖）
    // =====================================================================

    private static void ValidateFullRewriteOnRetry()
    {
        VoxelWorldData world = new VoxelWorldData();
        FaultyGenerator generator = new FaultyGenerator(WorldGenerationSettings.Default);
        generator.NullTimes = 1;   // 第一次生成失败 ⇒ 记录 Failed(NeedsReinstall = true)
        FakePresenter presenter = new FakePresenter();
        VoxelRuntimeCore core = new VoxelRuntimeCore(
            world, generator, new VoxelMeshGeneratorMesher(), presenter, CreateSettings());

        VoxelCoordinate coordinate = new VoxelCoordinate(0, 1, 0);

        core.RequestChunk(coordinate);
        core.Tick();

        Check(world.ChunkCount == 0, "RCI14：生成失败时世界为空（未被触碰）");
        ChunkRecordSnapshot snapshot;
        core.TryGetSnapshot(coordinate, out snapshot);
        Check(snapshot != null && snapshot.NeedsReinstall && snapshot.State == ChunkLifecycleState.Failed,
            "RCI14：记录为 Failed + NeedsReinstall（" + snapshot + "）");

        // 模拟"上次安装残留"或外部写入：世界里该坐标已有一个全 Stone 的 Chunk
        VoxelChunk residue = world.GetOrCreateChunk(coordinate);
        residue.Fill(VoxelType.Stone);
        Check(world.ChunkCount == 1 && residue.IsDirty, "RCI14：注入残留 Chunk（全 Stone）");

        VoxelChunk reference = generator.Generate(coordinate);

        // 显式重试：NeedsReinstall = true ⇒ 目标已存在 ⇒ 必须**全量写入（含 Air）**
        core.RetryFailedChunks();
        RuntimeCoreTickReport report = core.Tick();

        core.TryGetSnapshot(coordinate, out snapshot);
        Check(snapshot != null && snapshot.State == ChunkLifecycleState.Ready,
            "RCI14：重装成功 → Ready（" + snapshot + "）");
        Check(report.InvariantViolationCount == 0, "RCI14：重装帧无违规");

        VoxelChunk chunk;
        world.TryGetChunk(coordinate, out chunk);
        string detail;
        Check(CompareChunk(chunk, reference, out detail),
            "RCI14：全量写入覆盖残留 ⇒ 数据 == 生成结果（" + (detail ?? "OK") + "）");

        int stoneAboveTerrain = CountTypeAboveTerrain(chunk, VoxelType.Stone);
        int airAboveTerrain = CountTypeAboveTerrain(chunk, VoxelType.Air);
        Check(stoneAboveTerrain == 0 && airAboveTerrain > 0,
            "RCI14：地表之上无残留 Stone（Stone = " + stoneAboveTerrain +
            "，Air = " + airAboveTerrain +
            "）—— 若误用「只写非 Air」优化则此处必然失败");
        Check(chunk != null && !chunk.IsDirty, "RCI14：重装 + 呈递成功后清脏");
    }

    private static int CountTypeAboveTerrain(VoxelChunk chunk, VoxelType type)
    {
        WorldGenerationSettings settings = WorldGenerationSettings.Default;
        int originX = chunk.Coordinate.x * VoxelChunk.ChunkSize;
        int originY = chunk.Coordinate.y * VoxelChunk.ChunkSize;
        int originZ = chunk.Coordinate.z * VoxelChunk.ChunkSize;
        int count = 0;

        for (int x = 0; x < VoxelChunk.ChunkSize; x++)
        {
            for (int z = 0; z < VoxelChunk.ChunkSize; z++)
            {
                // 逐列真实地表高度（不是平均值 TerrainHeight）
                int height = HeightFunction.GetTerrainHeight(originX + x, originZ + z, settings);

                for (int y = 0; y < VoxelChunk.ChunkSize; y++)
                {
                    if (originY + y <= height) continue;
                    if (chunk.GetVoxel(x, y, z).Type == type) count++;
                }
            }
        }

        return count;
    }

    // =====================================================================
    // RCI15：既有文件 SHA 不变
    // =====================================================================

    private static readonly Dictionary<string, string> FrozenFiles =
        new Dictionary<string, string>
        {
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
            { "Scripts/VoxelWorld/View/VoxelChunkView.cs", "9D92BA355535ADCA" },
            { "Scripts/VoxelWorld/View/UnityMeshBuilder.cs", "F9E4224996AC7CE2" },
            { "Editor/VoxelWorldCoreValidation.cs", "6DE813AABCE95394" },
            { "Editor/WorldGenerationCoreValidation.cs", "4A517A0F902A1066" },
            { "Editor/VoxelMeshCoreValidation.cs", "5FFD8451D2CE05EB" },
            { "Editor/VoxelWorldViewValidation.cs", "BEFBAAF6B111A241" }
        };

    private static void ValidateExistingFilesUnchanged()
    {
        int mismatch = 0;
        int missing = 0;
        int compared = 0;

        foreach (KeyValuePair<string, string> pair in FrozenFiles)
        {
            string path = Path.Combine(Application.dataPath, pair.Key);
            if (!File.Exists(path)) { missing++; continue; }

            string hash = ComputeFileHash16(path);
            if (hash == null) continue;

            compared++;
            if (hash != pair.Value)
            {
                mismatch++;
                Check(false, "RCI15：" + pair.Key + " 已改变（" + hash + " != " + pair.Value + "）");
            }
        }

        Check(missing == 0, "RCI15：18 个既有文件全部存在（缺失 " + missing + "）");
        Check(mismatch == 0,
            "RCI15：既有文件 SHA256(前16) 全部不变（比对 " + compared + " 个，不一致 " + mismatch + "）");
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
            Check(false, "RCI15：无法计算哈希（" + path + "）：" + ex.Message);
            return null;
        }
    }

    // =====================================================================
    // Unity 侧真实 View 闭环（Generation → Mesh → View → Ready）
    // =====================================================================

    private static void ValidateRealViewClosedLoop()
    {
        using (TestScope scope = new TestScope("RuntimeCoreViewTest"))
        {
            ChunkViewSettings viewSettings = new ChunkViewSettings
            {
                Root = scope.Root,
                SubmeshMaterials = sharedMaterials,
                BuildCollider = true,
                Layer = 0
            };

            ChunkViewPresenter presenter = new ChunkViewPresenter(viewSettings);
            VoxelWorldData world = new VoxelWorldData();
            BasicTerrainGenerator generator = new BasicTerrainGenerator(WorldGenerationSettings.Default);
            VoxelMeshGeneratorMesher mesher = new VoxelMeshGeneratorMesher();
            VoxelRuntimeCore core = new VoxelRuntimeCore(
                world, generator, mesher, presenter, CreateSettings());

            VoxelCoordinate solidCoordinate = new VoxelCoordinate(0, 1, 0);
            VoxelCoordinate emptyCoordinate = new VoxelCoordinate(0, 3, 0);

            core.RequestChunk(solidCoordinate);
            core.RequestChunk(emptyCoordinate);

            RuntimeCoreTickReport report = core.Tick();

            Check(report.InvariantViolationCount == 0,
                "真实 View：闭环帧无不变量违规（" + (report.FirstInvariantViolation ?? "-") + "）");
            Check(report.PresentedThisTick == 2 && report.EmptyPresentedThisTick == 1,
                "真实 View：2 次呈递、其中 1 次空几何（" + report + "）");
            Check(presenter.LiveCount == 2, "真实 View：LiveCount = 2");
            Check(scope.Root.childCount == 2,
                "真实 View：Root 下有 2 个容器（实际 " + scope.Root.childCount + "）");

            ChunkRecordSnapshot snapshot;
            core.TryGetSnapshot(solidCoordinate, out snapshot);
            Check(snapshot != null && snapshot.State == ChunkLifecycleState.Ready,
                "真实 View：有几何的 Chunk = Ready（" + snapshot + "）");

            VoxelChunkView solidView;
            Check(presenter.TryGetView(solidCoordinate, out solidView) && solidView != null,
                "真实 View：取回表示");
            Check(solidView.State == ChunkViewState.Presented, "真实 View：状态 = Presented");
            Check(solidView.SharedMesh != null, "真实 View：持有自有 Mesh 资产");
            Check(solidView.Container.transform.localPosition == new Vector3(0f, 16f, 0f),
                "真实 View：容器摆放 = coordinate * 16（实际 " +
                solidView.Container.transform.localPosition + "）");

            VoxelChunk chunk;
            world.TryGetChunk(solidCoordinate, out chunk);
            VoxelMeshData expected = VoxelMeshGenerator.Generate(chunk);

            string detail;
            Check(CompareMeshWithData(solidView.SharedMesh, expected, out detail),
                "真实 View：呈现内容 == 数据重新 Meshing 的结果（" + (detail ?? "OK") + "）");
            Check(chunk != null && !chunk.IsDirty, "真实 View：Ready 后数据已清脏");

            VoxelChunkView emptyView;
            Check(presenter.TryGetView(emptyCoordinate, out emptyView) && emptyView != null,
                "真实 View：空几何也有表示槽");
            Check(emptyView.State == ChunkViewState.Empty && emptyView.SharedMesh == null,
                "真实 View：空几何 ⇒ Empty 状态且无资产");

            core.TryGetSnapshot(emptyCoordinate, out snapshot);
            Check(snapshot != null && snapshot.State == ChunkLifecycleState.Ready,
                "真实 View：空几何的 Chunk 也是 Ready（成功路径）");

            // 卸载：表示与数据都要释放
            core.ClearRequests();
            core.Tick();
            core.Tick();

            Check(presenter.LiveCount == 0, "真实 View：LiveCount 回 0（" + presenter.LiveCount + "）");
            Check(scope.Root.childCount == 0,
                "真实 View：Root 下无残留容器（实际 " + scope.Root.childCount + "）");
            Check(world.ChunkCount == 0, "真实 View：卸载后世界为空");

            presenter.ReleaseAll();
        }
    }

    // =====================================================================
    // 测试辅助
    // =====================================================================

    private static RuntimeCoreSettings CreateSettings(
        int vertical = 4, int loads = 8, int pairs = 8, int unloads = 8, int retries = 3)
    {
        return new RuntimeCoreSettings
        {
            VerticalChunkCount = vertical,
            MaxLoadsPerFrame = loads,
            MaxChunkPairsPerFrame = pairs,
            MaxUnloadsPerFrame = unloads,
            MaxRetriesPerChunk = retries
        };
    }

    /// <summary>可控故障注入的生成器（包装真实生成器，调用次数用完后恢复正常）。</summary>
    private sealed class FaultyGenerator : IWorldGenerator
    {
        private readonly BasicTerrainGenerator inner;

        public int ThrowTimes;
        public int NullTimes;
        public int WrongCoordinateTimes;
        public int InvalidTypeTimes;
        public int Calls;

        public FaultyGenerator(WorldGenerationSettings settings)
        {
            inner = new BasicTerrainGenerator(settings);
        }

        public WorldGenerationSettings Settings { get { return inner.Settings; } }

        public VoxelChunk Generate(VoxelCoordinate chunkCoordinate)
        {
            Calls++;

            if (ThrowTimes > 0)
            {
                ThrowTimes--;
                throw new InvalidOperationException("注入：生成抛异常");
            }

            if (NullTimes > 0)
            {
                NullTimes--;
                return null;
            }

            if (WrongCoordinateTimes > 0)
            {
                WrongCoordinateTimes--;
                return inner.Generate(chunkCoordinate.Offset(1, 0, 0));
            }

            if (InvalidTypeTimes > 0)
            {
                InvalidTypeTimes--;
                VoxelChunk invalid = inner.Generate(chunkCoordinate);
                invalid.SetVoxel(3, 3, 3, (VoxelType)99);
                return invalid;
            }

            return inner.Generate(chunkCoordinate);
        }
    }

    /// <summary>可控故障注入的 Mesher（默认委托真实 Mesher 适配器）。</summary>
    private sealed class FakeMesher : IChunkMesher
    {
        private readonly VoxelMeshGeneratorMesher inner = new VoxelMeshGeneratorMesher();

        public int FailCount;
        public bool ThrowAlways;
        public int Calls;
        public bool LastSeenIsDirty;
        public List<string> Trace;

        public bool TryBuild(VoxelChunk chunk, out VoxelMeshData meshData, out string error)
        {
            Calls++;
            LastSeenIsDirty = chunk.IsDirty;
            if (Trace != null) Trace.Add("TryBuild " + chunk.Coordinate);

            if (ThrowAlways)
            {
                meshData = null;
                error = "注入：Mesher 抛异常";
                throw new InvalidOperationException(error);
            }

            if (FailCount > 0)
            {
                FailCount--;
                meshData = null;
                error = "注入：Mesher 返回失败";
                return false;
            }

            return inner.TryBuild(chunk, out meshData, out error);
        }
    }

    /// <summary>记录调用序列、可控失败的 Presenter（不创建任何 Unity 对象）。</summary>
    private sealed class FakePresenter : IChunkPresenter
    {
        private readonly HashSet<VoxelCoordinate> live = new HashSet<VoxelCoordinate>();

        public int FailApplyCount;
        public bool ThrowOnApply;
        public bool ClaimsGeometryAlways;
        public bool ClaimsEmptyAlways;
        public int ApplyCalls;
        public int ReleaseCalls;
        public List<string> Trace;

        public readonly List<string> CallTrace = new List<string>();

        public int LiveCount { get { return live.Count; } }

        public PresenterOutcome Apply(
            VoxelCoordinate coordinate, VoxelMeshData meshData, out string error)
        {
            ApplyCalls++;
            CallTrace.Add("Apply " + coordinate);
            if (Trace != null) Trace.Add("Apply " + coordinate);

            if (ThrowOnApply)
            {
                error = "注入：Presenter 抛异常";
                throw new InvalidOperationException(error);
            }

            if (FailApplyCount > 0)
            {
                FailApplyCount--;
                error = "注入：Presenter 返回失败";
                return PresenterOutcome.Failed;
            }

            bool hasGeometry = meshData != null && meshData.HasGeometry;
            if (ClaimsGeometryAlways) hasGeometry = true;
            if (ClaimsEmptyAlways) hasGeometry = false;

            live.Add(coordinate);
            error = null;

            if (!hasGeometry)
            {
                CallTrace.Add("Empty " + coordinate);
                return PresenterOutcome.Empty;
            }

            CallTrace.Add("Presented " + coordinate);
            return PresenterOutcome.Presented;
        }

        public void Release(VoxelCoordinate coordinate)
        {
            ReleaseCalls++;
            CallTrace.Add("Release " + coordinate);
            live.Remove(coordinate);
        }

        /// <summary>释放全部表示槽（按坐标升序，保证调用序列确定）。</summary>
        public void ReleaseAll()
        {
            List<VoxelCoordinate> coordinates = new List<VoxelCoordinate>(live);
            coordinates.Sort(CompareCoordinate);

            for (int i = 0; i < coordinates.Count; i++)
            {
                Release(coordinates[i]);
            }
        }

        private static int CompareCoordinate(VoxelCoordinate a, VoxelCoordinate b)
        {
            if (a.x != b.x) return a.x.CompareTo(b.x);
            if (a.y != b.y) return a.y.CompareTo(b.y);
            return a.z.CompareTo(b.z);
        }
    }

    private sealed class TestScope : IDisposable
    {
        public readonly GameObject RootObject;
        public readonly Transform Root;

        public TestScope(string name)
        {
            RootObject = new GameObject(name);
            Root = RootObject.transform;
        }

        public void Dispose()
        {
            if (RootObject == null) return;

            for (int i = RootObject.transform.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.DestroyImmediate(
                    RootObject.transform.GetChild(i).gameObject);
            }

            UnityEngine.Object.DestroyImmediate(RootObject);
        }
    }

    private static VoxelType[] SnapshotVoxels(VoxelChunk chunk)
    {
        VoxelType[] snapshot = new VoxelType[VoxelChunk.VoxelCount];
        int index = 0;

        for (int x = 0; x < VoxelChunk.ChunkSize; x++)
            for (int y = 0; y < VoxelChunk.ChunkSize; y++)
                for (int z = 0; z < VoxelChunk.ChunkSize; z++)
                    snapshot[index++] = chunk.GetVoxel(x, y, z).Type;

        return snapshot;
    }

    private static bool CompareVoxels(VoxelChunk chunk, VoxelType[] expected, out string detail)
    {
        detail = null;
        if (chunk == null) { detail = "chunk 为 null"; return false; }

        int index = 0;
        for (int x = 0; x < VoxelChunk.ChunkSize; x++)
        {
            for (int y = 0; y < VoxelChunk.ChunkSize; y++)
            {
                for (int z = 0; z < VoxelChunk.ChunkSize; z++)
                {
                    VoxelType actual = chunk.GetVoxel(x, y, z).Type;
                    if (actual != expected[index])
                    {
                        detail = "(" + x + "," + y + "," + z + ") " + actual + " != " +
                                 expected[index];
                        return false;
                    }

                    index++;
                }
            }
        }

        return true;
    }

    private static bool CompareChunk(VoxelChunk a, VoxelChunk b, out string detail)
    {
        detail = null;
        if (a == null || b == null) { detail = "chunk 为 null"; return false; }

        for (int x = 0; x < VoxelChunk.ChunkSize; x++)
        {
            for (int y = 0; y < VoxelChunk.ChunkSize; y++)
            {
                for (int z = 0; z < VoxelChunk.ChunkSize; z++)
                {
                    VoxelType ta = a.GetVoxel(x, y, z).Type;
                    VoxelType tb = b.GetVoxel(x, y, z).Type;
                    if (ta != tb)
                    {
                        detail = "(" + x + "," + y + "," + z + ") " + ta + " != " + tb;
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private static ulong ComputeMeshHash(VoxelChunk chunk)
    {
        if (chunk == null) return 0UL;
        return VoxelMeshGenerator.Generate(chunk).ComputeContentHash();
    }

    private static bool CompareMeshWithData(Mesh mesh, VoxelMeshData data, out string detail)
    {
        detail = null;
        if (mesh == null) { detail = "Mesh 为 null"; return false; }
        if (data == null) { detail = "VoxelMeshData 为 null"; return false; }

        if (mesh.vertexCount != data.VertexCount)
        {
            detail = "顶点数 " + mesh.vertexCount + " != " + data.VertexCount;
            return false;
        }

        for (int submesh = 0; submesh < VoxelMeshData.SubmeshCount; submesh++)
        {
            int[] actual = mesh.GetTriangles(submesh);
            int[] expected = data.GetSubmeshTriangles(submesh);

            if (actual.Length != expected.Length)
            {
                detail = "submesh " + submesh + " 索引数 " + actual.Length + " != " +
                         expected.Length;
                return false;
            }

            for (int i = 0; i < actual.Length; i++)
            {
                if (actual[i] != expected[i])
                {
                    detail = "submesh " + submesh + " 索引 " + i + " 不一致";
                    return false;
                }
            }
        }

        return true;
    }

    private static void EnsureEditableScene()
    {
        try
        {
            UnityEngine.SceneManagement.Scene scene = EditorSceneManager.GetActiveScene();

            if (!scene.IsValid() || !scene.isLoaded)
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                Debug.Log("[VoxelWorldRuntimeCoreValidation] 无有效场景 ⇒ 已创建空场景");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[VoxelWorldRuntimeCoreValidation] 场景准备异常（继续）：" +
                             ex.Message);
        }
    }

    private static void CreateSharedMaterials()
    {
        string[] candidates =
        {
            "Standard",
            "Universal Render Pipeline/Lit",
            "Unlit/Color",
            "Sprites/Default",
            "Legacy Shaders/Diffuse"
        };

        Shader shader = null;
        for (int i = 0; i < candidates.Length; i++)
        {
            shader = Shader.Find(candidates[i]);
            if (shader != null) break;
        }

        sharedMaterials = new Material[VoxelMeshData.SubmeshCount];
        materialsAreReal = shader != null;

        if (!materialsAreReal) return;

        for (int i = 0; i < sharedMaterials.Length; i++)
        {
            sharedMaterials[i] = new Material(shader);
            sharedMaterials[i].name = "RuntimeCoreValidation_Shared_" + i;
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
