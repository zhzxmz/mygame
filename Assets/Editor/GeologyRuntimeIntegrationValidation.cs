using System;
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
using VoxelWorld.Generation.Geology;
using VoxelWorld.Mesh;
using VoxelWorld.Runtime;
using VoxelWorld.Runtime.Abstractions;
using VoxelWorld.Runtime.Streaming;
using VoxelWorld.View;

// 命名空间 VoxelWorld 内存在同名类型 VoxelWorld.VoxelWorld ⇒ 用别名消除解析歧义
using VoxelWorldData = VoxelWorld.VoxelWorld;

/// <summary>
/// Geology Stage E 验证：GeologyWorldGenerator 接入正式世界生成入口。
///
/// 用法：
///   · Editor 菜单：Tools/VoxelWorld/Validate Geology Runtime Integration
///   · 批处理：-executeMethod GeologyRuntimeIntegrationValidation.RunBatch
///
/// 覆盖（对应任务 1–9）：
///   GE1  Bootstrap 创建的是 GeologyWorldGenerator（不再是 BasicTerrainGenerator）
///   GE2  Runtime Core 不知道具体类型（结构扫描 + 反射 + 行为等价）
///   GE3  Generate → Validate → Install → Register → Ready 全流程成功
///   GE4  Streaming 正常（请求集合、远移释放、滞回）
///   GE5  Mesh 正常（呈现缓冲 == 由体素数据派生）
///   GE6  View 正常（容器/摆放/材质槽/LiveCount/拆除无残留）
///   GE7  BasicTerrainGenerator 与 Runtime Core 的 SHA 不变
///   GE8  新世界结果确定性一致（两套独立 Bootstrap 逐体素 + 哈希一致）
///   GE9  既有文件 SHA 不变 + 本轮授权修改显式登记
/// </summary>
public static class GeologyRuntimeIntegrationValidation
{
    private static int checks;
    private static int failures;

    private static Material[] sharedMaterials;

    [MenuItem("Tools/VoxelWorld/Validate Geology Runtime Integration")]
    public static void ValidateFromMenu()
    {
        ValidateAll();

        Debug.Log("[GeologyRuntimeIntegrationValidation] Menu validation result = " +
                  (failures == 0 ? "PASS" : "FAIL"));
    }

    public static void RunBatch()
    {
        ValidateAll();

        Debug.Log(
            "[GeologyRuntimeIntegrationValidation] ===== SUMMARY =====\n" +
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
            Run("GE1 Bootstrap 创建 GeologyWorldGenerator", ValidateBootstrapUsesGeologyGenerator);
            Run("GE2 Runtime Core 不知道具体类型", ValidateRuntimeIsGeneratorAgnostic);
            Run("GE3 Generate → Ready 全流程", ValidateFullFlow);
            Run("GE4 Streaming 正常", ValidateStreaming);
            Run("GE5 Mesh 正常", ValidateMesh);
            Run("GE6 View 正常", ValidateView);
            Run("GE7 BasicTerrainGenerator / Runtime Core SHA 不变", ValidateCriticalFilesUnchanged);
            Run("GE8 新世界结果确定性一致", ValidateWorldDeterminism);
            Run("GE9 既有文件 SHA + 授权修改登记", ValidateAllExistingFiles);
        }
        finally
        {
            DestroySharedMaterials();
        }
    }

    // =====================================================================
    // GE1：Bootstrap 创建的是 GeologyWorldGenerator
    // =====================================================================

    private static void ValidateBootstrapUsesGeologyGenerator()
    {
        using (BootstrapRig rig = new BootstrapRig("GE1"))
        {
            VoxelWorldStreamingBootstrap bootstrap = rig.Bootstrap;

            Check(bootstrap.IsBuilt, "GE1：组装成功（" + (bootstrap.BuildError ?? "OK") + "）");
            if (!bootstrap.IsBuilt) return;

            Check(bootstrap.Generator != null && bootstrap.Generator is GeologyWorldGenerator,
                "GE1：组装点注入的是 GeologyWorldGenerator（实际 " +
                (bootstrap.Generator == null ? "<null>" : bootstrap.Generator.GetType().Name) + "）");
            Check(!(bootstrap.Generator is BasicTerrainGenerator),
                "GE1：组装点不再实例化 BasicTerrainGenerator");

            Check(bootstrap.Geology != null, "GE1：地质参数已显式配置（配置入口就位）");
            Check(bootstrap.Biomes != null && bootstrap.Deposits != null,
                "GE1：Biome / Deposit provider 已装配（只读查询接口）");

            // 组装点只依赖抽象：声明类型是 IWorldGenerator
            PropertyInfo generatorProperty = typeof(VoxelWorldStreamingBootstrap)
                .GetProperty("Generator");
            Check(generatorProperty != null &&
                  generatorProperty.PropertyType == typeof(IWorldGenerator),
                "GE1：Generator 属性类型为 IWorldGenerator（抽象，不是具体类型）");

            // R38：rig 参考设置与 Bootstrap 实际配置的一致性（仅当前默认配置；不宣称覆盖非默认 SeaLevel）
            Check(rig.GenerationSettings.SeaLevel == rig.Bootstrap.SeaLevel,
                "GE1：rig 参考设置 SeaLevel 与 Bootstrap 配置一致（" +
                rig.GenerationSettings.SeaLevel + " / " + rig.Bootstrap.SeaLevel + "）");
            Check(rig.Bootstrap.SeaLevel == WorldGenerationSettings.DefaultSeaLevel,
                "GE1：Bootstrap.SeaLevel 等于登记默认值 DefaultSeaLevel（" +
                rig.Bootstrap.SeaLevel + " / " + WorldGenerationSettings.DefaultSeaLevel + "）");

            // 默认配置：地质特性开启（来自显式字段）；全关时可退化为 Stage 1（见 GE3 对照与 Stage D GW2）
            Check(bootstrap.Geology.EnableCaves == BootstrapDefaults.EnableGeologyCaves,
                "GE1：地质开关来自显式字段（EnableCaves = " +
                bootstrap.Geology.EnableCaves + "）");

            Debug.Log("[GeologyRuntimeIntegrationValidation] GE1：Generator = " +
                      bootstrap.Generator + "，" + bootstrap.Geology);
        }
    }

    // =====================================================================
    // GE2：Runtime Core 不知道具体类型
    // =====================================================================

    private static void ValidateRuntimeIsGeneratorAgnostic()
    {
        // ① 结构：Runtime 层（含抽象与适配器）不出现任何具体生成器类型
        string runtimeFolder = Path.Combine(
            Application.dataPath, "Scripts/VoxelWorld/Runtime");

        string[] runtimeFiles = Directory.GetFiles(runtimeFolder, "*.cs", SearchOption.AllDirectories);
        Array.Sort(runtimeFiles, StringComparer.Ordinal);

        string[] concreteGenerators =
        {
            "GeologyWorldGenerator", "BasicTerrainGenerator", "GeologySampler",
            "GeologySettings", "IDepositProvider", "IBiomeProvider"
        };

        int hits = 0;
        int abstractionReferences = 0;

        for (int i = 0; i < runtimeFiles.Length; i++)
        {
            string code = StripComments(File.ReadAllText(runtimeFiles[i], Encoding.UTF8));

            List<string> found = ScanTokens(code, concreteGenerators);
            if (found.Count > 0)
            {
                hits += found.Count;
                Check(false, "GE2：" + Path.GetFileName(runtimeFiles[i]) +
                             " 引用了具体生成器类型：" + string.Join(", ", found.ToArray()));
            }

            if (code.Contains("IWorldGenerator")) abstractionReferences++;
        }

        Check(hits == 0,
            "GE2：Runtime 层 " + runtimeFiles.Length +
            " 个文件不出现任何具体生成器类型（命中 " + hits + "）");
        Check(abstractionReferences > 0,
            "GE2：Runtime 层只依赖抽象 IWorldGenerator（正向对照：" +
            abstractionReferences + " 个文件引用）");

        // ② 反射：构造参数类型必须是抽象
        ConstructorInfo[] constructors = typeof(VoxelRuntimeCore).GetConstructors();
        bool usesAbstraction = false;
        bool usesConcrete = false;

        for (int i = 0; i < constructors.Length; i++)
        {
            ParameterInfo[] parameters = constructors[i].GetParameters();
            for (int p = 0; p < parameters.Length; p++)
            {
                if (parameters[p].ParameterType == typeof(IWorldGenerator)) usesAbstraction = true;
                if (parameters[p].ParameterType == typeof(GeologyWorldGenerator) ||
                    parameters[p].ParameterType == typeof(BasicTerrainGenerator)) usesConcrete = true;
            }
        }

        Check(usesAbstraction && !usesConcrete,
            "GE2：VoxelRuntimeCore 构造参数只接受 IWorldGenerator（不出现具体类型）");

        // ③ 行为：同一段 Core 代码驱动两种生成器都能走到 Ready（无感替换）
        VoxelWorldData worldA = new VoxelWorldData();
        VoxelWorldData worldB = new VoxelWorldData();

        BasicTerrainGenerator baseline = new BasicTerrainGenerator(WorldGenerationSettings.Default);
        GeologySettings geology = GeologySettings.Default;
        GeologySampler sampler = new GeologySampler(WorldGenerationSettings.Default, geology);
        GeologyWorldGenerator geologyGenerator = new GeologyWorldGenerator(
            WorldGenerationSettings.Default, geology,
            new GeologyBiomeProvider(sampler), new GeologyDepositProvider(sampler));

        string reasonA;
        string reasonB;

        bool okA = RunClosedLoop(worldA, baseline, out reasonA);
        bool okB = RunClosedLoop(worldB, geologyGenerator, out reasonB);

        Check(okA, "GE2：Core + BasicTerrainGenerator 闭环成功（" + reasonA + "）");
        Check(okB, "GE2：Core + GeologyWorldGenerator 闭环成功（" + reasonB + "）");
        Check(worldA.ChunkCount == 1 && worldB.ChunkCount == 1,
            "GE2：两种生成器产出的世界结构一致（ChunkCount 1 / 1）");
    }

    private static bool RunClosedLoop(
        VoxelWorldData world, IWorldGenerator generator, out string reason)
    {
        RuntimeCoreSettings settings = new RuntimeCoreSettings
        {
            VerticalChunkCount = 4,
            MaxLoadsPerFrame = 4,
            MaxChunkPairsPerFrame = 4,
            MaxUnloadsPerFrame = 4,
            MaxRetriesPerChunk = 3
        };

        CountingPresenter presenter = new CountingPresenter();
        VoxelRuntimeCore core = new VoxelRuntimeCore(
            world, generator, new VoxelMeshGeneratorMesher(), presenter, settings);

        VoxelCoordinate target = new VoxelCoordinate(0, 1, 0);
        core.RequestChunk(target);
        core.Tick();

        ChunkRecordSnapshot snapshot;
        core.TryGetSnapshot(target, out snapshot);

        reason = snapshot == null ? "无记录" : snapshot.ToString();
        return snapshot != null && snapshot.State == ChunkLifecycleState.Ready;
    }

    // =====================================================================
    // GE3：Generate → Ready 全流程
    // =====================================================================

    private static void ValidateFullFlow()
    {
        using (BootstrapRig rig = new BootstrapRig("GE3"))
        {
            VoxelWorldStreamingBootstrap bootstrap = rig.Bootstrap;
            if (!bootstrap.IsBuilt)
            {
                Check(false, "GE3：组装失败（" + bootstrap.BuildError + "）");
                return;
            }

            rig.PlacePlayerAt(new Vector3(0f, 20f, 0f));

            for (int i = 0; i < 6; i++) bootstrap.Step();

            Check(bootstrap.Core.LoadedChunkCount > 0,
                "GE3：已加载 Chunk = " + bootstrap.Core.LoadedChunkCount);
            Check(bootstrap.World.ChunkCount == bootstrap.Core.LoadedChunkCount,
                "GE3：world.ChunkCount == 已加载记录数（" +
                bootstrap.World.ChunkCount + "）");

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

            Check(ready == loaded.Count && ready > 0,
                "GE3：全部 Chunk 到达 Ready（" + ready + " / " + loaded.Count + "）");
            Check(bootstrap.LastTickReport != null &&
                  bootstrap.LastTickReport.InvariantViolationCount == 0,
                "GE3：无帧末不变量违规（" +
                (bootstrap.LastTickReport != null
                    ? bootstrap.LastTickReport.FirstInvariantViolation ?? "OK"
                    : "无报告") + "）");

            RuntimeCoreDiagnostics diagnostics = bootstrap.Core.Diagnostics;
            Check(diagnostics.GeneratedTotal > 0 && diagnostics.BuiltTotal > 0 &&
                  diagnostics.PresentedTotal > 0 && diagnostics.FailedTotal == 0,
                "GE3：流程计数正常（" + diagnostics + "）");

            // 地质确实生效：洞穴开启时世界必须与 Stage 1 基线不同（否则说明生成器没被真正接入）
            BasicTerrainGenerator baseline = new BasicTerrainGenerator(rig.GenerationSettings);
            int differences = 0;

            for (int i = 0; i < loaded.Count && differences == 0; i++)
            {
                VoxelChunk chunk;
                if (!bootstrap.World.TryGetChunk(loaded[i], out chunk) || chunk == null) continue;

                VoxelChunk reference = baseline.Generate(loaded[i]);

                for (int y = 0; y < VoxelChunk.ChunkSize && differences == 0; y++)
                {
                    for (int z = 0; z < VoxelChunk.ChunkSize && differences == 0; z++)
                    {
                        for (int x = 0; x < VoxelChunk.ChunkSize; x++)
                        {
                            if (chunk.GetVoxel(x, y, z).Type != reference.GetVoxel(x, y, z).Type)
                            {
                                differences++;
                                break;
                            }
                        }
                    }
                }
            }

            Check(differences > 0,
                "GE3：新世界确实使用地质规则（与 Stage 1 基线存在差异；差异检出 " +
                differences + " ⇒ 断言非平凡）");

            Debug.Log("[GeologyRuntimeIntegrationValidation] GE3：" +
                      bootstrap.LastTickReport + "；" + bootstrap.LastStreamingReport);
        }
    }

    // =====================================================================
    // GE4：Streaming 正常
    // =====================================================================

    private static void ValidateStreaming()
    {
        using (BootstrapRig rig = new BootstrapRig("GE4"))
        {
            VoxelWorldStreamingBootstrap bootstrap = rig.Bootstrap;
            if (!bootstrap.IsBuilt)
            {
                Check(false, "GE4：组装失败（" + bootstrap.BuildError + "）");
                return;
            }

            rig.PlacePlayerAt(new Vector3(0f, 20f, 0f));
            for (int i = 0; i < 6; i++) bootstrap.Step();

            ChunkStreamingDriver streaming = bootstrap.Streaming;
            Check(streaming != null, "GE4：流式驱动已装配");

            Check(streaming.StreamedCount == bootstrap.Core.GetRequestedCoordinates().Count,
                "GE4：流式层已请求集合 == Core 请求集合（" +
                streaming.StreamedCount + " vs " +
                bootstrap.Core.GetRequestedCoordinates().Count + "）");
            Check(streaming.RejectedTotal == 0,
                "GE4：无被拒请求（垂直范围一致；拒绝 " + streaming.RejectedTotal + "）");

            int loadedBefore = bootstrap.Core.LoadedChunkCount;

            // 同一 Chunk 内移动 ⇒ 零请求零释放（滞回 / 焦点粒度）
            rig.PlacePlayerAt(new Vector3(4f, 20f, 9f));
            ChunkStreamingReport idle = bootstrap.Step();
            Check(idle != null && idle.RequestedThisUpdate == 0 && idle.ReleasedThisUpdate == 0,
                "GE4：Chunk 内移动零请求零释放（" + idle + "）");

            // 远移 ⇒ 释放旧区、加载新区
            rig.PlacePlayerAt(new Vector3(10 * 16f + 3f, 20f, 0f));
            ChunkStreamingReport move = bootstrap.Step();
            Check(move != null && move.ReleasedThisUpdate > 0,
                "GE4：远移释放旧区（释放 " + (move != null ? move.ReleasedThisUpdate : -1) + "）");

            for (int i = 0; i < 6; i++) bootstrap.Step();

            Check(bootstrap.Core.LoadedChunkCount == streaming.StreamedCount,
                "GE4：远移后已加载 == 已请求（" + bootstrap.Core.LoadedChunkCount + " vs " +
                streaming.StreamedCount + "）");
            Check(bootstrap.Core.LoadedChunkCount > 0 &&
                  bootstrap.Core.LoadedChunkCount == loadedBefore,
                "GE4：加载规模稳定（远移前后同为 " + bootstrap.Core.LoadedChunkCount + "）");
            Check(bootstrap.Core.Diagnostics.UnloadedTotal > 0,
                "GE4：卸载计数增长（" + bootstrap.Core.Diagnostics.UnloadedTotal + "）");

            Debug.Log("[GeologyRuntimeIntegrationValidation] GE4：" +
                      bootstrap.Streaming + "；" + bootstrap.Core.Diagnostics);
        }
    }

    // =====================================================================
    // GE5：Mesh 正常
    // =====================================================================

    private static void ValidateMesh()
    {
        using (BootstrapRig rig = new BootstrapRig("GE5"))
        {
            VoxelWorldStreamingBootstrap bootstrap = rig.Bootstrap;
            if (!bootstrap.IsBuilt) { Check(false, "GE5：组装失败"); return; }

            rig.PlacePlayerAt(new Vector3(0f, 20f, 0f));
            for (int i = 0; i < 6; i++) bootstrap.Step();

            IList<VoxelCoordinate> loaded = bootstrap.Core.GetLoadedCoordinates();
            int compared = 0;
            int mismatches = 0;
            int withGeometry = 0;
            string firstMismatch = null;

            for (int i = 0; i < loaded.Count; i++)
            {
                VoxelChunk chunk;
                if (!bootstrap.World.TryGetChunk(loaded[i], out chunk) || chunk == null) continue;

                VoxelMeshData expected = VoxelMeshGenerator.Generate(chunk);
                if (expected.HasGeometry) withGeometry++;

                VoxelChunkView view;
                if (!bootstrap.Presenter.TryGetView(loaded[i], out view) || view == null)
                {
                    mismatches++;
                    if (firstMismatch == null) firstMismatch = loaded[i] + "：无 View";
                    continue;
                }

                compared++;
                string difference = CompareMeshWithData(view.SharedMesh, expected);

                if (difference != null)
                {
                    mismatches++;
                    if (firstMismatch == null) firstMismatch = loaded[i] + "：" + difference;
                }
            }

            Check(compared > 0, "GE5：比较了 " + compared + " 个 Chunk 的呈现缓冲");
            Check(withGeometry > 0,
                "GE5：存在非空几何（" + withGeometry + " / " + compared + " ⇒ 断言非平凡）");
            Check(mismatches == 0,
                "GE5：呈现缓冲 == 由体素数据派生的 Meshing 结果（不一致 " + mismatches + "）" +
                (firstMismatch == null ? "" : " 首次：" + firstMismatch));
        }
    }

    private static string CompareMeshWithData(Mesh mesh, VoxelMeshData data)
    {
        if (mesh == null) return "Mesh 为 null";
        if (data == null) return "VoxelMeshData 为 null";
        if (mesh.vertexCount != data.VertexCount)
        {
            return "顶点数 " + mesh.vertexCount + " != " + data.VertexCount;
        }

        for (int submesh = 0; submesh < VoxelMeshData.SubmeshCount; submesh++)
        {
            int[] actual = mesh.GetTriangles(submesh);
            int[] expected = data.GetSubmeshTriangles(submesh);

            if (actual.Length != expected.Length)
            {
                return "submesh " + submesh + " 索引数 " + actual.Length + " != " + expected.Length;
            }

            for (int i = 0; i < actual.Length; i++)
            {
                if (actual[i] != expected[i]) return "submesh " + submesh + " 索引 " + i + " 不一致";
            }
        }

        return null;
    }

    // =====================================================================
    // GE6：View 正常
    // =====================================================================

    private static void ValidateView()
    {
        using (BootstrapRig rig = new BootstrapRig("GE6"))
        {
            VoxelWorldStreamingBootstrap bootstrap = rig.Bootstrap;
            if (!bootstrap.IsBuilt) { Check(false, "GE6：组装失败"); return; }

            rig.PlacePlayerAt(new Vector3(0f, 20f, 0f));
            for (int i = 0; i < 6; i++) bootstrap.Step();

            int loaded = bootstrap.Core.LoadedChunkCount;

            Check(bootstrap.Presenter.LiveCount == loaded,
                "GE6：表示槽数量 == 已加载 Chunk（" + bootstrap.Presenter.LiveCount +
                " vs " + loaded + "）");
            Check(rig.ChunkRoot.childCount == loaded,
                "GE6：Root 下容器数 == 已加载 Chunk（" + rig.ChunkRoot.childCount + "）");

            int positionViolations = 0;
            int materialViolations = 0;

            IList<VoxelCoordinate> coordinates = bootstrap.Core.GetLoadedCoordinates();
            for (int i = 0; i < coordinates.Count; i++)
            {
                VoxelChunkView view;
                if (!bootstrap.Presenter.TryGetView(coordinates[i], out view) || view == null) continue;

                Vector3 expected = new Vector3(
                    coordinates[i].x * VoxelChunk.ChunkSize,
                    coordinates[i].y * VoxelChunk.ChunkSize,
                    coordinates[i].z * VoxelChunk.ChunkSize);

                if ((view.Container.transform.localPosition - expected).sqrMagnitude > 0.0001f)
                {
                    positionViolations++;
                }

                if (view.Renderer == null ||
                    view.Renderer.sharedMaterials.Length != VoxelMeshData.SubmeshCount)
                {
                    materialViolations++;
                }
            }

            Check(positionViolations == 0,
                "GE6：容器摆放 == coordinate * ChunkSize（违反 " + positionViolations + "）");
            Check(materialViolations == 0,
                "GE6：材质槽 = " + VoxelMeshData.SubmeshCount + " 个（违反 " + materialViolations + "）");

            // 拆除：表示与容器全部释放
            bootstrap.Teardown();

            Check(bootstrap.Presenter == null && !bootstrap.IsBuilt && bootstrap.Generator == null,
                "GE6：Teardown 清空组装产物（含 Generator / Geology / providers）");
            Check(rig.ChunkRoot.childCount == 0,
                "GE6：Teardown 后 Root 无残留容器（" + rig.ChunkRoot.childCount + "）");
        }
    }

    // =====================================================================
    // GE7：BasicTerrainGenerator 与 Runtime Core 的 SHA 不变
    // =====================================================================

    private static readonly Dictionary<string, string> CriticalFiles =
        new Dictionary<string, string>
        {
            { "Scripts/VoxelWorld/Generation/BasicTerrainGenerator.cs", "7FFD8F4B1226EA4E" },
            { "Scripts/VoxelWorld/Generation/IWorldGenerator.cs", "B39614CA9BF37921" },
            { "Scripts/VoxelWorld/Generation/WorldGenerationSettings.cs", "7F11DEE8061E4FA1" }, // 第十二轮：SeaLevel 迁移后受信重钉（diff +46-0 已独立逐行审查）
            { "Scripts/VoxelWorld/Generation/HeightFunction.cs", "715341D5FC652408" },
            // Runtime Core（12 个）
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
            // Streaming（5 个）
            { "Scripts/VoxelWorld/Runtime/Streaming/IChunkRequestSink.cs", "9481A8BA397ACE60" },
            { "Scripts/VoxelWorld/Runtime/Streaming/ChunkStreamingSettings.cs", "6BA404AF57F553C3" },
            { "Scripts/VoxelWorld/Runtime/Streaming/ChunkStreamingReport.cs", "B57EA40C7FA6ECC9" },
            { "Scripts/VoxelWorld/Runtime/Streaming/ChunkStreamingDriver.cs", "26DF1B6B22E2DFF3" },
            { "Scripts/VoxelWorld/Runtime/Streaming/RuntimeCoreRequestSink.cs", "FD0E5CD9D1C9D77B" },
            // Mesh / View
            { "Scripts/VoxelWorld/Mesh/VoxelMeshData.cs", "8B78A03ED1C78E26" },
            { "Scripts/VoxelWorld/Mesh/VoxelMeshGenerator.cs", "653804C19C272C72" },
            { "Scripts/VoxelWorld/View/VoxelChunkView.cs", "9D92BA355535ADCA" },
            { "Scripts/VoxelWorld/View/UnityMeshBuilder.cs", "F9E4224996AC7CE2" },
            { "Scripts/VoxelWorld/View/ChunkViewPresenter.cs", "C6346E735591E2BF" }
        };

    private static void ValidateCriticalFilesUnchanged()
    {
        int compared = 0;
        int mismatch = 0;
        int missing = 0;

        foreach (KeyValuePair<string, string> pair in CriticalFiles)
        {
            string path = Path.Combine(Application.dataPath, pair.Key);
            if (!File.Exists(path)) { missing++; Check(false, "GE7：缺失 " + pair.Key); continue; }

            string hash = ComputeFileHash16(path);
            if (hash == null) continue;

            compared++;
            if (hash != pair.Value)
            {
                mismatch++;
                Check(false, "GE7：" + pair.Key + " 已改变（" + hash + " != " + pair.Value + "）");
            }
        }

        Check(missing == 0, "GE7：关键文件全部存在（缺失 " + missing + "）");
        Check(mismatch == 0,
            "GE7：BasicTerrainGenerator / IWorldGenerator / Generation / Runtime Core(12) / " +
            "Streaming(5) / Mesh(2) / View(3) 共 " + compared + " 个文件 SHA 不变");
    }

    // =====================================================================
    // GE8：新世界结果确定性一致
    // =====================================================================

    private static void ValidateWorldDeterminism()
    {
        using (BootstrapRig rigA = new BootstrapRig("GE8-A"))
        using (BootstrapRig rigB = new BootstrapRig("GE8-B"))
        {
            if (!rigA.Bootstrap.IsBuilt || !rigB.Bootstrap.IsBuilt)
            {
                Check(false, "GE8：组装失败（" + rigA.Bootstrap.BuildError + " / " +
                             rigB.Bootstrap.BuildError + "）");
                return;
            }

            Vector3[] path =
            {
                new Vector3(0f, 20f, 0f),
                new Vector3(12f, 20f, 5f),
                new Vector3(40f, 20f, 40f),
                new Vector3(-25f, 20f, 17f),
                new Vector3(0f, 20f, 0f)
            };

            for (int i = 0; i < path.Length; i++)
            {
                rigA.PlacePlayerAt(path[i]);
                rigB.PlacePlayerAt(path[i]);
                for (int s = 0; s < 4; s++) { rigA.Bootstrap.Step(); rigB.Bootstrap.Step(); }
            }

            IList<VoxelCoordinate> loadedA = rigA.Bootstrap.Core.GetLoadedCoordinates();
            IList<VoxelCoordinate> loadedB = rigB.Bootstrap.Core.GetLoadedCoordinates();

            Check(loadedA.Count > 0 && loadedA.Count == loadedB.Count,
                "GE8：两套新世界的已加载集合规模一致（" + loadedA.Count + " vs " +
                loadedB.Count + "）");

            int mismatches = 0;
            int comparedVoxels = 0;
            int hashMismatches = 0;
            string firstMismatch = null;

            for (int i = 0; i < loadedA.Count; i++)
            {
                VoxelChunk a;
                VoxelChunk b;

                if (!rigA.Bootstrap.World.TryGetChunk(loadedA[i], out a) || a == null) continue;
                if (!rigB.Bootstrap.World.TryGetChunk(loadedA[i], out b) || b == null)
                {
                    mismatches++;
                    if (firstMismatch == null) firstMismatch = loadedA[i] + "：B 世界缺少该 Chunk";
                    continue;
                }

                comparedVoxels += VoxelChunk.VoxelCount;

                for (int y = 0; y < VoxelChunk.ChunkSize && mismatches == 0; y++)
                {
                    for (int z = 0; z < VoxelChunk.ChunkSize && mismatches == 0; z++)
                    {
                        for (int x = 0; x < VoxelChunk.ChunkSize; x++)
                        {
                            if (a.GetVoxel(x, y, z).Type != b.GetVoxel(x, y, z).Type)
                            {
                                mismatches++;
                                if (firstMismatch == null)
                                {
                                    firstMismatch = loadedA[i] + " (" + x + "," + y + "," + z + ")";
                                }
                                break;
                            }
                        }
                    }
                }

                if (VoxelMeshGenerator.Generate(a).ComputeContentHash() !=
                    VoxelMeshGenerator.Generate(b).ComputeContentHash())
                {
                    hashMismatches++;
                }
            }

            Check(mismatches == 0,
                "GE8：两套新世界逐体素一致（比较 " + comparedVoxels + " 体素，不一致 " +
                mismatches + "）" + (firstMismatch == null ? "" : " 首次：" + firstMismatch));
            Check(hashMismatches == 0,
                "GE8：派生内容哈希一致（不一致 " + hashMismatches + "）");
            Check(rigA.Bootstrap.Core.Diagnostics.GeneratedTotal ==
                  rigB.Bootstrap.Core.Diagnostics.GeneratedTotal,
                "GE8：生成次数一致（" + rigA.Bootstrap.Core.Diagnostics.GeneratedTotal +
                " vs " + rigB.Bootstrap.Core.Diagnostics.GeneratedTotal + "）");
        }
    }

    // =====================================================================
    // GE9：既有文件 SHA 不变 + 授权修改登记
    // =====================================================================

    private static readonly Dictionary<string, string> FrozenFiles =
        new Dictionary<string, string>
        {
            // 规范（14 份）+ ADR
            { "../Specifications/World/VoxelWorld_Architecture.md", "F09B17A1002D7CD2" },
            { "../Specifications/World/VoxelWorld_Core.md", "6D471FFA184B13EC" },
            { "../Specifications/World/WorldGeneration_Core.md", "91F0FCDA380C91F7" },
            { "../Specifications/World/VoxelWorld_Mesh.md", "81CEDBFA6E9756CE" },
            { "../Specifications/World/VoxelWorld_Mining.md", "8A2B020DDB97422B" },
            { "../Specifications/World/VoxelWorld_View.md", "D9BD5422026E21B6" },
            { "../Specifications/World/VoxelWorld_Runtime.md", "EDD02AC50FC03E5E" },
            { "../Specifications/World/VoxelWorld_Runtime_Core.md", "10F1EC7F10DAD8F3" },
            { "../Specifications/World/VoxelWorld_Runtime_Interface.md", "0AE21EB684412685" },
            { "../Specifications/World/VoxelWorld_Runtime_Core_Implementation.md", "D816F09C60A810C4" },
            { "../Specifications/World/WorldGeneration_Geology.md", "D1E70FE71BC400E1" },
            { "../Specifications/World/WorldGeneration_Geology_Implementation.md", "2700AE40B0CB09A0" },
            { "../Specifications/World/WorldGeneration_Geology_Runtime_Implementation.md", "373A8BF712D73C3B" },
            { "../Specifications/ArchitectureDecisionWorldSpaceEntity.txt", "218D1CAAA738C59D" },
            // VoxelCore
            { "Scripts/VoxelWorld/VoxelType.cs", "02C1F54580AEE541" },
            { "Scripts/VoxelWorld/Voxel.cs", "DC52145E6E82132D" },
            { "Scripts/VoxelWorld/VoxelCoordinate.cs", "F327B2761DFDCFC2" },
            { "Scripts/VoxelWorld/VoxelChunk.cs", "4DBDFBAC5BCE500C" },
            { "Scripts/VoxelWorld/VoxelWorld.cs", "D9E749A466635918" },
            { "Scripts/VoxelWorld/VoxelWorldDebug.cs", "6AE0C41274AF9154" },
            // Geology（Stage A–D 实现，全部只读）
            { "Scripts/VoxelWorld/Generation/Geology/GeologySettings.cs", "BEA8EB4F4967721C" },
            { "Scripts/VoxelWorld/Generation/Geology/GeologyNoise.cs", "2266E7A880E43315" },
            { "Scripts/VoxelWorld/Generation/Geology/GeologySample.cs", "4946B90F3E56262E" },
            { "Scripts/VoxelWorld/Generation/Geology/GeologySampler.cs", "62836145F8F3E212" },
            { "Scripts/VoxelWorld/Generation/Geology/IDepositProvider.cs", "6F25E54FE64F7935" },
            { "Scripts/VoxelWorld/Generation/Geology/IBiomeProvider.cs", "6EF3344B319436B7" },
            { "Scripts/VoxelWorld/Generation/Geology/GeologyDepositProvider.cs", "7B1B8CF998402D3A" },
            { "Scripts/VoxelWorld/Generation/Geology/GeologyBiomeProvider.cs", "CC537685742367D9" },
            { "Scripts/VoxelWorld/Generation/Geology/GeologyWorldGenerator.cs", "73EB59CFB939DC96" },
            // 既有 Editor 验证入口（未被本轮修改）
            { "Editor/VoxelWorldCoreValidation.cs", "6DE813AABCE95394" },
            { "Editor/WorldGenerationCoreValidation.cs", "4A517A0F902A1066" },
            { "Editor/VoxelMeshCoreValidation.cs", "5FFD8451D2CE05EB" },
            { "Editor/VoxelWorldViewValidation.cs", "BEFBAAF6B111A241" },
            { "Editor/VoxelWorldRuntimeCoreValidation.cs", "C5ABCECD2C338BED" }
        };

    /// <summary>
    /// **本轮经用户明确授权**修改的文件（Stage E：世界生成组装点接入 + SHA 级联重钉）：
    ///   · `Scripts/Game/VoxelWorldStreamingBootstrap.cs`：组装点把 `BasicTerrainGenerator` 换成
    ///     `GeologyWorldGenerator`，新增地质配置入口与只读暴露（Generator/Geology/Biomes/Deposits）；
    ///   · `Editor/ChunkStreamingValidation.cs`：CS10 正向对照从 `BasicTerrainGenerator` 更新为
    ///     `GeologyWorldGenerator`（组装点已替换，原断言不再反映现实），并新增"组装点不再装配
    ///     BasicTerrainGenerator"断言；该文件原属 FrozenFiles，本轮经授权修改，故移入本表；
    ///   · `Editor/GeologyStageAValidation.cs`：重钉 Bootstrap + ChunkStreamingValidation 哈希；
    ///   · `Editor/GeologyStageBValidation.cs`：重钉 Bootstrap + Chunk + Stage A；
    ///   · `Editor/GeologyStageCValidation.cs`：重钉 Bootstrap + Chunk + Stage A + Stage B；
    ///   · `Editor/GeologyStageDValidation.cs`：重钉 Bootstrap + Chunk + Stage A + Stage B + Stage C。
    /// 未修改 Runtime Core / Streaming / Mesh / View / IWorldGenerator / BasicTerrainGenerator。
    /// </summary>
    private static readonly Dictionary<string, string> AuthorizedChangedFiles =
        new Dictionary<string, string>
        {
            { "Scripts/Game/VoxelWorldStreamingBootstrap.cs", "8A59289F0194D20F" }, // 第十二轮：SeaLevel 迁移后受信重钉（diff +4-1 已独立逐行审查）
            { "Editor/ChunkStreamingValidation.cs", "BEB05E3DD16D48F3" }, // 第十二轮：CS12 登记重钉 + 规范哈希重钉后同步（仅登记行与注释变化，无断言改动）
            { "Editor/GeologyStageAValidation.cs", "67DB0CD6161EE4F4" },
            { "Editor/GeologyStageBValidation.cs", "58DA2160E30ABA72" },
            { "Editor/GeologyStageCValidation.cs", "F52E2D1A4B515581" },
            { "Editor/GeologyStageDValidation.cs", "C80477D2430B85AA" } // 第十二轮：早前已批准登记重钉后同步（仅登记行与注释变化，无断言改动）
        };

    private static void ValidateAllExistingFiles()
    {
        int compared = 0;
        int missing = 0;
        int mismatch = 0;
        int skipped = 0;
        string specificationRoot = FindSpecificationsRoot();

        foreach (KeyValuePair<string, string> pair in FrozenFiles)
        {
            if (!VerifyOne(pair.Key, pair.Value, specificationRoot, false,
                    ref compared, ref missing, ref mismatch, ref skipped)) continue;
        }

        foreach (KeyValuePair<string, string> pair in AuthorizedChangedFiles)
        {
            if (!VerifyOne(pair.Key, pair.Value, specificationRoot, true,
                    ref compared, ref missing, ref mismatch, ref skipped)) continue;
        }

        Check(missing == 0, "GE9：既有文件全部存在（缺失 " + missing + "）");
        Check(mismatch == 0,
            "GE9：既有文件 SHA256(前16) 与登记一致（比对 " + compared + " 个；其中 " +
            AuthorizedChangedFiles.Count + " 个为本轮已授权修改并以新哈希钉住；跳过 " + skipped + "）");

        // 入口存在性（6 个基础 + 5 个地质阶段）
        string[] entries =
        {
            "Editor/VoxelWorldCoreValidation.cs",
            "Editor/WorldGenerationCoreValidation.cs",
            "Editor/VoxelMeshCoreValidation.cs",
            "Editor/VoxelWorldViewValidation.cs",
            "Editor/VoxelWorldRuntimeCoreValidation.cs",
            "Editor/ChunkStreamingValidation.cs",
            "Editor/GeologyStageAValidation.cs",
            "Editor/GeologyStageBValidation.cs",
            "Editor/GeologyStageCValidation.cs",
            "Editor/GeologyStageDValidation.cs",
            "Editor/GeologyRuntimeIntegrationValidation.cs"
        };

        int missingEntries = 0;
        for (int i = 0; i < entries.Length; i++)
        {
            if (!File.Exists(Path.Combine(Application.dataPath, entries[i]))) missingEntries++;
        }

        Check(missingEntries == 0,
            "GE9：" + entries.Length + " 个验证入口文件都存在（缺失 " + missingEntries + "）");
    }

    private static bool VerifyOne(
        string key, string expected, string specificationRoot, bool authorized,
        ref int compared, ref int missing, ref int mismatch, ref int skipped)
    {
        string path;

        if (key.StartsWith("../Specifications/", StringComparison.Ordinal))
        {
            if (specificationRoot == null) { skipped++; return true; }

            path = Path.Combine(
                specificationRoot, key.Substring(3).Replace('/', Path.DirectorySeparatorChar));
        }
        else
        {
            path = Path.Combine(Application.dataPath, key);
        }

        if (!File.Exists(path))
        {
            missing++;
            Check(false, "GE9：缺失 " + key);
            return false;
        }

        string hash = ComputeFileHash16(path);
        if (hash == null) return true;

        compared++;
        if (hash != expected)
        {
            mismatch++;
            Check(false, "GE9：" + (authorized ? "[已授权修改] " : "") + key +
                         " 哈希不符（" + hash + " != " + expected + "）");
            return false;
        }

        return true;
    }

    private static string FindSpecificationsRoot()
    {
        DirectoryInfo directory = new DirectoryInfo(Application.dataPath);

        for (int i = 0; i < 4 && directory != null; i++)
        {
            string candidate = Path.Combine(directory.FullName, "Specifications", "World");
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
            Check(false, "GE9：无法计算哈希（" + path + "）：" + ex.Message);
            return null;
        }
    }

    // =====================================================================
    // 测试装置
    // =====================================================================

    /// <summary>Bootstrap 默认值镜像（用于断言"配置来自显式字段"）。</summary>
    private static class BootstrapDefaults
    {
        public const bool EnableGeologyCaves = true;
    }

    /// <summary>一套独立的 Bootstrap 装置：独立 Root / Player / 世界 / 组装。</summary>
    private sealed class BootstrapRig : IDisposable
    {
        public readonly GameObject HostObject;
        public readonly GameObject ChunkRootObject;
        public readonly GameObject PlayerObject;
        public readonly Transform ChunkRoot;
        public readonly VoxelWorldStreamingBootstrap Bootstrap;

        public readonly WorldGenerationSettings GenerationSettings;

        public BootstrapRig(string label)
        {
            HostObject = new GameObject("StageE_" + label + "_Host");
            ChunkRootObject = new GameObject("StageE_" + label + "_ChunkRoot");
            PlayerObject = new GameObject("StageE_" + label + "_Player");

            ChunkRoot = ChunkRootObject.transform;

            Bootstrap = HostObject.AddComponent<VoxelWorldStreamingBootstrap>();
            Bootstrap.Player = PlayerObject.transform;
            Bootstrap.ChunkRoot = ChunkRoot;
            Bootstrap.SubmeshMaterials = sharedMaterials;
            Bootstrap.BuildColliders = true;
            Bootstrap.TickEveryFrame = false;

            Bootstrap.ViewDistanceChunks = 1;
            Bootstrap.UnloadHysteresisChunks = 1;
            Bootstrap.VerticalChunkCount = 1;

            Bootstrap.Seed = 12345;
            Bootstrap.TerrainHeight = 24;
            Bootstrap.SoilDepth = 3;
            Bootstrap.WorldHeight = 64;
            Bootstrap.SeaLevel = 64;
            Bootstrap.TerrainFrequency = 0.06f;
            Bootstrap.TerrainAmplitude = 6f;

            Bootstrap.MaxLoadsPerFrame = 8;
            Bootstrap.MaxChunkPairsPerFrame = 8;
            Bootstrap.MaxUnloadsPerFrame = 8;

            GenerationSettings = new WorldGenerationSettings(
                Bootstrap.Seed, Bootstrap.TerrainHeight, Bootstrap.SoilDepth,
                Bootstrap.WorldHeight, Bootstrap.TerrainFrequency, Bootstrap.TerrainAmplitude, Bootstrap.SeaLevel);

            Bootstrap.Build();
        }

        public void PlacePlayerAt(Vector3 position)
        {
            PlayerObject.transform.position = position;
        }

        public void Dispose()
        {
            if (Bootstrap != null) Bootstrap.Teardown();

            if (HostObject != null) UnityEngine.Object.DestroyImmediate(HostObject);
            if (PlayerObject != null) UnityEngine.Object.DestroyImmediate(PlayerObject);

            if (ChunkRootObject != null)
            {
                for (int i = ChunkRootObject.transform.childCount - 1; i >= 0; i--)
                {
                    UnityEngine.Object.DestroyImmediate(
                        ChunkRootObject.transform.GetChild(i).gameObject);
                }

                UnityEngine.Object.DestroyImmediate(ChunkRootObject);
            }
        }
    }

    private sealed class CountingPresenter : IChunkPresenter
    {
        private readonly HashSet<VoxelCoordinate> live = new HashSet<VoxelCoordinate>();

        public int LiveCount { get { return live.Count; } }

        public PresenterOutcome Apply(
            VoxelCoordinate coordinate, VoxelMeshData meshData, out string error)
        {
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

    // =====================================================================
    // 工具
    // =====================================================================

    private static List<string> ScanTokens(string text, string[] patterns)
    {
        List<string> hits = new List<string>();

        for (int i = 0; i < patterns.Length; i++)
        {
            if (text.IndexOf(patterns[i], StringComparison.Ordinal) >= 0) hits.Add(patterns[i]);
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

    private static void EnsureEditableScene()
    {
        try
        {
            UnityEngine.SceneManagement.Scene scene = EditorSceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                Debug.Log("[GeologyRuntimeIntegrationValidation] 无有效场景 ⇒ 已创建空场景");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[GeologyRuntimeIntegrationValidation] 场景准备异常（继续）：" +
                             ex.Message);
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
                sharedMaterials[i].name = "StageE_Shared_" + i;
            }
        }
    }

    private static void DestroySharedMaterials()
    {
        if (sharedMaterials == null) return;

        for (int i = 0; i < sharedMaterials.Length; i++)
        {
            if (sharedMaterials[i] != null) UnityEngine.Object.DestroyImmediate(sharedMaterials[i]);
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
