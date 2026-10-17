using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using VoxelWorld;
using VoxelWorld.Generation;
using VoxelWorld.Mesh;
using VoxelWorld.View;

/// <summary>
/// Voxel View 第一阶段闭环验证（单 Chunk 显示）。
///
/// 用法：
///   · Editor 菜单：Tools/VoxelWorld/Validate View Core
///   · 批处理：-executeMethod VoxelWorldViewValidation.RunBatch
///
/// 契约来源：Specifications/World/VoxelWorld_View.md §10（V1–V16）+ 用户本轮 5 项要求：
///   1) 不修改已有 VoxelCore / Generation / Mesh —— 由 SHA256 与既有验证入口另行核验（不属本文件）；
///   2) View 层可以使用 UnityEngine —— 本验证在 EditMode 下真实创建 GameObject / Mesh / MeshCollider；
///   3) View 层禁止调用 SetVoxel / Generation / Mining —— V11/V12 结构扫描（剔除注释后逐 token 检查）；
///   4) Mesh 上传必须符合 View §5 —— V2/V3/V4/V5/V7/V14（保真、写入顺序、索引格式、包围盒、材质槽、幂等）；
///   5) 只实现单 Chunk 显示，不含 Runtime / Loader / Save —— 目录范围检查 + 无 Runtime 对象参与。
///
/// 注意：本文件是 **Editor 批处理验证**（不属于 View 层），因此允许使用
/// VoxelCore / Generation / Mesh 来"制造测试输入"；被扫描的"禁止项"只针对
/// `Assets/Scripts/VoxelWorld/View/` 下的 View 层源码。
/// </summary>
public static class VoxelWorldViewValidation
{
    private static int checks;
    private static int failures;

    private static Material[] sharedMaterials;
    private static bool materialsAreReal;
    private static string shaderName = "<none>";

    private const string ViewSourceFolder = "Scripts/VoxelWorld/View";

    [MenuItem("Tools/VoxelWorld/Validate View Core")]
    public static void ValidateFromMenu()
    {
        ValidateAll();

        Debug.Log(
            "[VoxelWorldViewValidation] Menu validation result = " +
            (failures == 0 ? "PASS" : "FAIL")
        );
    }

    public static void RunBatch()
    {
        ValidateAll();

        Debug.Log(
            "[VoxelWorldViewValidation] ===== SUMMARY =====\n" +
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
            Run("单 Voxel 呈现", ValidateSingleVoxelPresent);
            Run("空几何语义", ValidateEmptyGeometrySemantics);
            Run("变换与命名契约", ValidateTransformContract);
            Run("包围盒契约", ValidateBoundsContract);
            Run("材质槽契约", ValidateMaterialSlots);
            Run("索引格式选择", ValidateIndexFormatSelection);
            Run("幂等提交", ValidateIdempotentApply);
            Run("原子替换与失败保旧", ValidateAtomicReplaceAndFailure);
            Run("数据层只读性", ValidateChunkDataIsReadOnly);
            Run("释放即净", ValidateReleaseIsClean);
            Run("呈现与数据一致", ValidatePresentedMatchesData);
            Run("多 View 互不干扰", ValidateTwoViewsAreIndependent);
            Run("View 层结构性约束", ValidateViewLayerHasNoWorldRules);
        }
        finally
        {
            DestroySharedMaterials();
        }
    }

    // =====================================================================
    // 1) 单 Voxel 呈现（V2 / V3 / V4 起点 / V13）
    // =====================================================================

    private static void ValidateSingleVoxelPresent()
    {
        using (TestScope scope = new TestScope("ViewTest_SingleVoxel"))
        {
            VoxelChunk chunk = new VoxelChunk(VoxelCoordinate.Zero);
            chunk.SetVoxel(5, 5, 5, VoxelType.Stone); // 唯一测试体素

            VoxelMeshData data = VoxelMeshGenerator.Generate(chunk);
            Check(data.QuadCount == 6 && data.TriangleCount == 12,
                "前置：单 Stone Voxel ⇒ 6 面 / 12 三角形（实际 " + data.QuadCount +
                " / " + data.TriangleCount + "）");

            VoxelChunkView view = new VoxelChunkView(VoxelCoordinate.Zero, scope.Settings());
            string error;
            ChunkViewApplyResult result = view.TryApply(data, out error);

            Check(result == ChunkViewApplyResult.Presented,
                "单 Voxel：结果为 Presented（实际 " + result + "）");
            Check(error == null,
                "单 Voxel：成功时 error 为 null（实际 " + (error ?? "<null>") + "）");
            Check(view.State == ChunkViewState.Presented,
                "单 Voxel：View 状态 = Presented（实际 " + view.State + "）");
            Check(view.HasGeometry, "单 Voxel：HasGeometry = true");
            Check(view.AppliedCount == 1 && view.FailedCount == 0,
                "单 Voxel：计数 applied=1 / failed=0（实际 " + view.AppliedCount +
                " / " + view.FailedCount + "）");

            Mesh mesh = view.SharedMesh;
            Check(mesh != null, "单 Voxel：持有自有 Mesh 资产");
            Check(mesh != null && mesh.vertexCount == 24,
                "单 Voxel：资产顶点数 = 24（实际 " +
                (mesh != null ? mesh.vertexCount : -1) + "）");
            Check(mesh != null && mesh.subMeshCount == VoxelMeshData.SubmeshCount,
                "单 Voxel：资产 submesh 数 = " + VoxelMeshData.SubmeshCount);
            Check(mesh != null && mesh.triangles.Length == 36,
                "单 Voxel：资产索引数 = 36（12 三角形，实际 " +
                (mesh != null ? mesh.triangles.Length : -1) + "）");

            // V2 资产独占：MeshFilter / MeshCollider 与自有资产同源
            Check(view.Filter != null && view.Filter.sharedMesh == mesh,
                "V2：MeshFilter.sharedMesh 指向本 View 自有资产");
            Check(view.Renderer != null, "V2：存在 MeshRenderer");
            Check(view.Collider != null && view.Collider.sharedMesh == mesh,
                "V13：MeshCollider 与呈现同源（同一资产）");

            // V3 上传保真
            string detail;
            bool fidelity = CompareMeshToData(mesh, data, out detail);
            Check(fidelity, "V3：资产内容与 VoxelMeshData 逐元素一致（" +
                (fidelity ? "顶点/法线/UV/3 个 submesh 索引" : detail) + "）");
        }
    }

    // =====================================================================
    // 2) 空几何语义（V6）+ 顶点存在但无三角形
    // =====================================================================

    private static void ValidateEmptyGeometrySemantics()
    {
        using (TestScope scope = new TestScope("ViewTest_EmptyGeometry"))
        {
            VoxelChunk airChunk = new VoxelChunk(VoxelCoordinate.Zero); // 全 Air
            VoxelMeshData emptyData = VoxelMeshGenerator.Generate(airChunk);

            Check(emptyData.TriangleCount == 0 && emptyData.VertexCount == 0,
                "前置：全 Air Chunk ⇒ 空缓冲（0/0）");

            VoxelChunkView view = new VoxelChunkView(VoxelCoordinate.Zero, scope.Settings());
            string error;
            ChunkViewApplyResult result = view.TryApply(emptyData, out error);

            Check(result == ChunkViewApplyResult.Empty,
                "V6：空几何结果是 Empty（成功路径，实际 " + result + "）");
            Check(error == null, "V6：空几何不报错（error 必须为 null）");
            Check(view.State == ChunkViewState.Empty,
                "V6：状态 = Empty（实际 " + view.State + "）");
            Check(view.Container != null, "V6：容器仍存在（Empty ≠ Absent）");
            Check(view.SharedMesh == null, "V6：不创建 Mesh 资产");
            Check(view.Filter != null && view.Filter.sharedMesh == null,
                "V6：MeshFilter 无资产");
            Check(view.Collider != null && view.Collider.sharedMesh == null,
                "V6：MeshCollider 为空");
            Check(!view.HasGeometry, "V6：HasGeometry = false");
            Check(view.FailedCount == 0, "V6：不记为失败");

            // 空 → 实：正常转为 Presented
            VoxelChunk solid = new VoxelChunk(VoxelCoordinate.Zero);
            solid.SetVoxel(1, 2, 3, VoxelType.Dirt);
            VoxelMeshData solidData = VoxelMeshGenerator.Generate(solid);

            result = view.TryApply(solidData, out error);
            Check(result == ChunkViewApplyResult.Presented && view.HasGeometry,
                "V6：Empty → Presented 正常转换");
            Mesh firstMesh = view.SharedMesh;

            // 实 → 空：撤下几何并销毁旧资产
            result = view.TryApply(emptyData, out error);
            Check(result == ChunkViewApplyResult.Empty && view.State == ChunkViewState.Empty,
                "V6：Presented → Empty 正常转换（撤下几何）");
            Check(firstMesh == null, "V6：转为 Empty 时旧资产已被销毁（无泄漏）");
            Check(view.SharedMesh == null && view.Filter.sharedMesh == null,
                "V6：转为 Empty 后无残留资产");
            Check(view.AppliedCount == 3, "V6：提交计数 = 3（实际 " + view.AppliedCount + "）");

            // 边界情形：顶点存在但三角形为 0 ⇒ 仍按"空几何"处理（§5.4：不创建资产）
            VoxelMeshData verticesOnly = new VoxelMeshData(
                new Vector3[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.one },
                new Vector3[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up },
                new Vector2[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up },
                new int[VoxelMeshData.SubmeshCount][]);

            Check(!verticesOnly.HasGeometry,
                "V6：顶点存在但 0 三角形 ⇒ HasGeometry = false");
            result = view.TryApply(verticesOnly, out error);
            Check(result == ChunkViewApplyResult.Empty && view.SharedMesh == null,
                "V6：顶点存在但无三角形 ⇒ 走 Empty 路径，不创建资产");
        }
    }

    // =====================================================================
    // 3) 变换与命名契约（V1 / §3.2 / §3.3）
    // =====================================================================

    private static void ValidateTransformContract()
    {
        Check(VoxelChunkView.ChunkSize == VoxelChunk.ChunkSize,
            "§3.3：View 的 ChunkSize(" + VoxelChunkView.ChunkSize +
            ") 必须与 VoxelCore ChunkSize(" + VoxelChunk.ChunkSize + ") 一致");

        using (TestScope scope = new TestScope("ViewTest_Transform"))
        {
            VoxelCoordinate coordinate = new VoxelCoordinate(-2, 1, -3);
            VoxelChunkView view = new VoxelChunkView(
                coordinate, scope.Settings(buildCollider: true, layer: 5));

            VoxelChunk chunk = new VoxelChunk(coordinate);
            chunk.SetVoxel(0, 0, 0, VoxelType.Grass);
            string error;
            view.TryApply(VoxelMeshGenerator.Generate(chunk), out error);

            Transform t = view.Container.transform;

            Check(t.parent == scope.Root, "V1：父级为显式 Root");
            Check(t.localPosition == new Vector3(-32f, 16f, -48f),
                "V1：localPosition = coordinate*16 = (-32, 16, -48)（实际 " +
                t.localPosition + "）");
            Check(t.localRotation == Quaternion.identity, "V1：localRotation = identity");
            Check(t.localScale == Vector3.one, "V1：localScale = one");
            Check(t.lossyScale == Vector3.one, "V1：lossyScale = one（父链无缩放）");
            Check(Mathf.Approximately(t.position.x, -32f) &&
                  Mathf.Approximately(t.position.y, 16f) &&
                  Mathf.Approximately(t.position.z, -48f),
                "V1：世界位置 = coordinate*16（实际 " + t.position + "）");
            Check(view.Container.layer == 5,
                "§3.1：容器 Layer 显式应用（实际 " + view.Container.layer + "）");

            // §3.2 命名可反解坐标
            string expectedName = ChunkViewView_ExpectedName(coordinate);
            Check(view.Container.name == expectedName,
                "§3.2：容器命名 = " + expectedName + "（实际 " + view.Container.name + "）");

            VoxelCoordinate parsed;
            bool parsedOk = VoxelChunkView.TryParseContainerName(view.Container.name, out parsed);
            Check(parsedOk && parsed == coordinate,
                "§3.2：命名可反解出 Chunk 坐标（解析 " +
                (parsedOk ? parsed.ToString() : "<失败>") + "）");

            Check(view.SharedMesh != null &&
                  view.SharedMesh.name == VoxelChunkView.BuildMeshAssetName(coordinate),
                "§3.2：Mesh 资产命名含 Chunk 坐标（实际 " +
                (view.SharedMesh != null ? view.SharedMesh.name : "<null>") + "）");

            // 容器必须直接挂在 Root 下（V15）
            Check(scope.Root.childCount == 1 &&
                  scope.Root.GetChild(0) == t,
                "V15：容器是 Root 的直接子级（Root 子级数 = " +
                scope.Root.childCount + "）");

            view.Release();
        }
    }

    private static string ChunkViewView_ExpectedName(VoxelCoordinate coordinate)
    {
        return "ChunkView (" + coordinate.x + ", " + coordinate.y + ", " +
               coordinate.z + ")";
    }

    // =====================================================================
    // 4) 包围盒契约（V5）
    // =====================================================================

    private static void ValidateBoundsContract()
    {
        using (TestScope scope = new TestScope("ViewTest_Bounds"))
        {
            VoxelChunk chunk = new VoxelChunk(VoxelCoordinate.Zero);
            chunk.SetVoxel(5, 5, 5, VoxelType.Stone);
            VoxelMeshData data = VoxelMeshGenerator.Generate(chunk);

            VoxelChunkView view = new VoxelChunkView(VoxelCoordinate.Zero, scope.Settings());
            string error;
            view.TryApply(data, out error);

            Bounds bounds = view.SharedMesh.bounds;

            Check(bounds.size != Vector3.zero,
                "V5：bounds 非零尺寸（默认零尺寸包围盒会导致整个 Chunk 被剔除）");
            Check(Mathf.Abs(bounds.min.x - 5f) < 0.0001f &&
                  Mathf.Abs(bounds.min.y - 5f) < 0.0001f &&
                  Mathf.Abs(bounds.min.z - 5f) < 0.0001f,
                "V5：bounds.min = (5, 5, 5)（实际 " + bounds.min + "）");
            Check(Mathf.Abs(bounds.max.x - 6f) < 0.0001f &&
                  Mathf.Abs(bounds.max.y - 6f) < 0.0001f &&
                  Mathf.Abs(bounds.max.z - 6f) < 0.0001f,
                "V5：bounds.max = (6, 6, 6)（实际 " + bounds.max + "）");

            // 与独立重算的顶点 AABB 比较（Bounds 内部用 center+extents 表示）
            Vector3 min;
            Vector3 max;
            UnityMeshBuilder.ComputeVertexBounds(data, out min, out max);
            Check((bounds.min - min).magnitude < 0.0001f &&
                  (bounds.max - max).magnitude < 0.0001f,
                "V5：bounds == 顶点 AABB（min " + min + " / max " + max + "）");

            // 必须包含于 [0,16]³
            Check(bounds.min.x >= -0.0001f && bounds.min.y >= -0.0001f &&
                  bounds.min.z >= -0.0001f &&
                  bounds.max.x <= VoxelChunkView.ChunkSize + 0.0001f &&
                  bounds.max.y <= VoxelChunkView.ChunkSize + 0.0001f &&
                  bounds.max.z <= VoxelChunkView.ChunkSize + 0.0001f,
                "V5：bounds ⊂ [0,16]³");

            // 真实生成地形的包围盒（覆盖 [0,16]² 的水平范围）
            BasicTerrainGenerator generator =
                new BasicTerrainGenerator(WorldGenerationSettings.Default);
            VoxelChunk terrain = generator.Generate(VoxelCoordinate.Zero);
            VoxelMeshData terrainData = VoxelMeshGenerator.Generate(terrain);

            VoxelChunkView terrainView =
                new VoxelChunkView(VoxelCoordinate.Zero, scope.Settings());
            terrainView.TryApply(terrainData, out error);

            Vector3 tMin;
            Vector3 tMax;
            UnityMeshBuilder.ComputeVertexBounds(terrainData, out tMin, out tMax);
            Bounds tBounds = terrainView.SharedMesh.bounds;
            Check((tBounds.min - tMin).magnitude < 0.0001f &&
                  (tBounds.max - tMax).magnitude < 0.0001f,
                "V5：真实地形 bounds == 顶点 AABB（min " + tMin + " / max " + tMax + "）");

            view.Release();
            terrainView.Release();
        }
    }

    // =====================================================================
    // 5) 材质槽契约（V7 / §6）
    // =====================================================================

    private static void ValidateMaterialSlots()
    {
        using (TestScope scope = new TestScope("ViewTest_Materials"))
        {
            VoxelChunk chunk = new VoxelChunk(VoxelCoordinate.Zero);
            chunk.SetVoxel(2, 2, 2, VoxelType.Grass);
            chunk.SetVoxel(6, 2, 2, VoxelType.Dirt);
            chunk.SetVoxel(10, 2, 2, VoxelType.Stone);
            VoxelMeshData data = VoxelMeshGenerator.Generate(chunk);

            VoxelChunkView view = new VoxelChunkView(VoxelCoordinate.Zero, scope.Settings());
            string error;
            view.TryApply(data, out error);

            Material[] applied = view.Renderer.sharedMaterials;
            Check(applied.Length == VoxelMeshData.SubmeshCount,
                "V7：sharedMaterials 长度 = 3（实际 " + applied.Length + "）");

            bool sameRefs = true;
            for (int i = 0; i < applied.Length; i++)
            {
                if (applied[i] != sharedMaterials[i]) sameRefs = false;
            }

            Check(sameRefs,
                "V7：3 个材质槽顺序与配置一致（Grass/Dirt/Stone = 0/1/2，" +
                "材质来源 " + (materialsAreReal ? "真实共享材质 " + shaderName : "null 占位") + "）");

            // 三个 Chunk（同 Root）共享同一批材质对象 ⇒ 不随 Chunk 数增长
            VoxelChunkView viewB = new VoxelChunkView(
                new VoxelCoordinate(1, 0, 0), scope.Settings());
            VoxelChunkView viewC = new VoxelChunkView(
                new VoxelCoordinate(0, 0, 1), scope.Settings());
            viewB.TryApply(data, out error);
            viewC.TryApply(data, out error);

            Check(viewB.Renderer.sharedMaterials[0] == applied[0] &&
                  viewC.Renderer.sharedMaterials[2] == applied[2],
                "V7：材质实例数不随 Chunk 数增长（3 个 View 共用同一批材质引用）");

            // §6.4 材质缺失（null 占位）不得丢几何
            ChunkViewSettings nullMaterialSettings = new ChunkViewSettings
            {
                Root = scope.Root,
                SubmeshMaterials = new Material[VoxelMeshData.SubmeshCount],
                BuildCollider = true,
                Layer = 0
            };

            VoxelChunkView nullView = new VoxelChunkView(
                new VoxelCoordinate(2, 0, 0), nullMaterialSettings);
            ChunkViewApplyResult nullResult = nullView.TryApply(data, out error);

            Check(nullResult == ChunkViewApplyResult.Presented && nullView.HasGeometry,
                "§6.4：材质全为 null 时仍正常呈现（不得因外观配置丢弃几何）");
            string detail;
            Check(CompareMeshToData(nullView.SharedMesh, data, out detail),
                "§6.4：材质缺失时上传保真不受影响（" + (detail ?? "OK") + "）");
            Check(nullView.Renderer.sharedMaterials.Length == VoxelMeshData.SubmeshCount,
                "§6.4：材质槽仍为 3 个");

            view.Release();
            viewB.Release();
            viewC.Release();
            nullView.Release();
        }
    }

    // =====================================================================
    // 6) 索引格式选择（V4 / §5.3）
    // =====================================================================

    private static void ValidateIndexFormatSelection()
    {
        Check(UnityMeshBuilder.SelectIndexFormat(65535) == IndexFormat.UInt16,
            "§5.3：顶点数 65535 ⇒ UInt16");
        Check(UnityMeshBuilder.SelectIndexFormat(65536) == IndexFormat.UInt32,
            "§5.3：顶点数 65536 ⇒ UInt32");

        using (TestScope scope = new TestScope("ViewTest_IndexFormat"))
        {
            string error;

            // 小缓冲 ⇒ UInt16
            VoxelChunk chunk = new VoxelChunk(VoxelCoordinate.Zero);
            chunk.SetVoxel(1, 1, 1, VoxelType.Stone);
            VoxelChunkView smallView = new VoxelChunkView(
                VoxelCoordinate.Zero, scope.Settings());
            smallView.TryApply(VoxelMeshGenerator.Generate(chunk), out error);

            Check(smallView.SharedMesh.indexFormat == IndexFormat.UInt16,
                "V4：小缓冲（24 顶点）⇒ indexFormat = UInt16");

            // 边界：65532 顶点（16383 quad）⇒ 仍 UInt16
            VoxelMeshData boundary = CreateSyntheticQuadData(16383);
            Check(boundary.VertexCount == 65532,
                "前置：合成缓冲顶点数 = 65532（实际 " + boundary.VertexCount + "）");

            VoxelChunkView boundaryView = new VoxelChunkView(
                new VoxelCoordinate(0, 1, 0), scope.Settings());
            ChunkViewApplyResult boundaryResult = boundaryView.TryApply(boundary, out error);
            Check(boundaryResult == ChunkViewApplyResult.Presented,
                "V4：65532 顶点缓冲提交成功（实际 " + boundaryResult + "）");
            Check(boundaryView.SharedMesh.indexFormat == IndexFormat.UInt16,
                "V4：顶点数 ≤ 65535 ⇒ UInt16");

            // 超界：65536 顶点（16384 quad）⇒ 必须 UInt32
            VoxelMeshData large = CreateSyntheticQuadData(16384);
            Check(large.VertexCount == 65536,
                "前置：合成缓冲顶点数 = 65536（实际 " + large.VertexCount + "）");

            VoxelChunkView largeView = new VoxelChunkView(
                new VoxelCoordinate(1, 0, 0), scope.Settings());
            ChunkViewApplyResult largeResult = largeView.TryApply(large, out error);
            Check(largeResult == ChunkViewApplyResult.Presented,
                "V4：65536 顶点缓冲提交成功（实际 " + largeResult + "）");
            Check(largeView.SharedMesh.indexFormat == IndexFormat.UInt32,
                "V4：顶点数 > 65535 ⇒ 必须 UInt32（实际 " +
                largeView.SharedMesh.indexFormat + "）");
            Check(largeView.SharedMesh.vertexCount == 65536,
                "V4：超大缓冲顶点数保真（实际 " + largeView.SharedMesh.vertexCount + "）");

            string detail;
            Check(CompareMeshToData(largeView.SharedMesh, large, out detail),
                "V4/V3：超大缓冲内容逐元素保真（" + (detail ?? "OK") + "）");

            smallView.Release();
            boundaryView.Release();
            largeView.Release();
        }
    }

    // =====================================================================
    // 7) 幂等提交（V14）
    // =====================================================================

    private static void ValidateIdempotentApply()
    {
        using (TestScope scope = new TestScope("ViewTest_Idempotent"))
        {
            VoxelChunk chunk = new VoxelChunk(VoxelCoordinate.Zero);
            for (int x = 2; x <= 4; x++)
            {
                for (int y = 2; y <= 3; y++)
                {
                    for (int z = 2; z <= 4; z++)
                    {
                        chunk.SetVoxel(x, y, z, VoxelType.Stone);
                    }
                }
            }

            chunk.SetVoxel(8, 8, 8, VoxelType.Dirt);
            VoxelMeshData data = VoxelMeshGenerator.Generate(chunk);

            VoxelChunkView view = new VoxelChunkView(VoxelCoordinate.Zero, scope.Settings());
            string error;
            view.TryApply(data, out error);
            Mesh first = view.SharedMesh;

            Check(first != null, "幂等：首次提交后持有资产");
            Check(scope.Root.childCount == 1,
                "幂等：提交不额外创建容器（Root 子级数 = " + scope.Root.childCount + "）");

            view.TryApply(data, out error);
            Mesh second = view.SharedMesh;

            Check(second != null && second != first,
                "V14：第二次提交为路径 A 的整体替换（新资产对象）");
            Check(first == null,
                "V14：被替换的旧资产已在替换成功之后销毁（无泄漏）");
            Check(scope.Root.childCount == 1,
                "V14：重复提交不增加容器数（Root 子级数 = " + scope.Root.childCount + "）");

            string detail;
            Check(CompareMeshToData(second, data, out detail),
                "V14：重复提交后内容与输入逐元素一致（" + (detail ?? "OK") + "）");
            Check(view.AppliedCount == 2 && view.FailedCount == 0,
                "V14：计数 = applied 2 / failed 0（实际 " + view.AppliedCount +
                " / " + view.FailedCount + "）");

            Debug.Log("[VoxelWorldViewValidation] 幂等提交内容哈希 = " +
                      ComputeMeshHash(second).ToString("X16"));

            view.Release();
        }
    }

    // =====================================================================
    // 8) 原子替换与失败保旧（V8 / §9.1）
    // =====================================================================

    private static void ValidateAtomicReplaceAndFailure()
    {
        using (TestScope scope = new TestScope("ViewTest_Atomic"))
        {
            VoxelChunk chunk = new VoxelChunk(VoxelCoordinate.Zero);
            chunk.SetVoxel(3, 3, 3, VoxelType.Grass);
            VoxelMeshData good = VoxelMeshGenerator.Generate(chunk);

            VoxelCoordinate coordinate = new VoxelCoordinate(0, 0, 0);
            VoxelChunkView view = new VoxelChunkView(coordinate, scope.Settings());
            string error;
            view.TryApply(good, out error);

            Mesh before = view.SharedMesh;
            Check(before != null, "前置：成功呈现，持有资产");

            // 失败 1：null 缓冲
            ChunkViewApplyResult r1 = view.TryApply((VoxelMeshData)null, out error);
            Check(r1 == ChunkViewApplyResult.Failed,
                "V8：null 缓冲 ⇒ Failed（实际 " + r1 + "）");
            Check(!string.IsNullOrEmpty(error),
                "V8：失败必须携带原因（实际 " + (error ?? "<null>") + "）");

            // 失败 2：坐标不匹配
            string coordError;
            ChunkViewApplyResult r2 = view.TryApply(
                new VoxelCoordinate(99, 99, 99), good, out coordError);
            Check(r2 == ChunkViewApplyResult.Failed,
                "V8：坐标不匹配 ⇒ Failed（防「喂错 Chunk」）");
            Check(!string.IsNullOrEmpty(coordError),
                "V8：坐标不匹配必须携带原因（实际 " + (coordError ?? "<null>") + "）");

            // 失败 3：结构性非法缓冲（NaN 顶点 ⇒ 会污染 bounds）
            VoxelMeshData invalid = CreateNaNVertexData();
            ChunkViewApplyResult r3 = view.TryApply(invalid, out error);
            Check(r3 == ChunkViewApplyResult.Failed,
                "V8：非法缓冲（NaN 顶点）⇒ Failed（实际 " + r3 + "）");
            Check(!string.IsNullOrEmpty(error),
                "V8：非法缓冲必须携带原因（实际 " + (error ?? "<null>") + "）");

            // 旧呈现必须原样保留
            Check(view.SharedMesh == before,
                "V8：失败后自有资产引用未被替换（原子性）");
            Check(view.Filter.sharedMesh == before,
                "V8：失败后 MeshFilter 仍指向旧资产");
            Check(view.State == ChunkViewState.Presented,
                "V8：失败后仍为 Presented（旧呈现完整）");
            Check(view.AppliedCount == 1 && view.FailedCount == 3,
                "V8：计数 applied=1 / failed=3（实际 " + view.AppliedCount +
                " / " + view.FailedCount + "）");

            string detail;
            Check(CompareMeshToData(view.SharedMesh, good, out detail),
                "V8：失败后旧资产内容未变（" + (detail ?? "OK") + "）");

            view.Release();

            // 释放后再提交 ⇒ 必须失败（释放是终态）
            ChunkViewApplyResult afterRelease = view.TryApply(good, out error);
            Check(afterRelease == ChunkViewApplyResult.Failed && !string.IsNullOrEmpty(error),
                "V8：释放后提交 ⇒ Failed(" + (error ?? "<null>") + ")");
        }
    }

    // =====================================================================
    // 9) 数据层只读性（V12 / §1.3）
    // =====================================================================

    private static void ValidateChunkDataIsReadOnly()
    {
        using (TestScope scope = new TestScope("ViewTest_ReadOnly"))
        {
            BasicTerrainGenerator generator =
                new BasicTerrainGenerator(WorldGenerationSettings.Default);
            VoxelChunk chunk = generator.Generate(VoxelCoordinate.Zero);

            chunk.MarkDirty(); // 模拟"数据已改、等待重建"

            VoxelType[] before = SnapshotVoxels(chunk);
            VoxelCoordinate coordinateBefore = chunk.Coordinate;
            bool dirtyBefore = chunk.IsDirty;

            VoxelMeshData data = VoxelMeshGenerator.Generate(chunk);

            VoxelChunkView view = new VoxelChunkView(VoxelCoordinate.Zero, scope.Settings());
            string error;
            view.TryApply(data, out error);

            Check(dirtyBefore && chunk.IsDirty,
                "V12：成功提交后 View 未清脏（IsDirty 仍为 true —— 清脏属 Runtime Core）");
            Check(chunk.Coordinate == coordinateBefore, "V12：Chunk 坐标未被修改");
            Check(VoxelsEqual(chunk, before), "V12：4096 个体素未被修改（成功提交路径）");

            // 干净状态下失败 / 空几何提交都不得置脏
            chunk.ClearDirty();
            Check(!chunk.IsDirty, "前置：ClearDirty 后 IsDirty = false");

            view.TryApply(CreateNaNVertexData(), out error);
            Check(!chunk.IsDirty,
                "V12：失败提交不会置脏（View 不写数据层）");

            VoxelChunk emptyChunk = new VoxelChunk(VoxelCoordinate.Zero);
            view.TryApply(VoxelMeshGenerator.Generate(emptyChunk), out error);
            Check(!chunk.IsDirty, "V12：空几何提交不会置脏");

            view.Release();
            Check(!chunk.IsDirty, "V12：释放不会置脏");
            Check(VoxelsEqual(chunk, before), "V12：全流程结束后 4096 个体素仍未被修改");

            // 独立生成器的同参数结果必须与快照一致（真值未被 View 影响）
            VoxelChunk regenerated = generator.Generate(VoxelCoordinate.Zero);
            Check(VoxelsEqual(regenerated, before),
                "V12：同参数重新生成的数据与快照一致（View 未影响世界真值）");
        }
    }

    // =====================================================================
    // 10) 释放即净（V9 / §5.6）
    // =====================================================================

    private static void ValidateReleaseIsClean()
    {
        using (TestScope scope = new TestScope("ViewTest_Release"))
        {
            VoxelChunk chunk = new VoxelChunk(VoxelCoordinate.Zero);
            chunk.SetVoxel(4, 4, 4, VoxelType.Stone);
            VoxelMeshData data = VoxelMeshGenerator.Generate(chunk);

            VoxelChunkView view = new VoxelChunkView(VoxelCoordinate.Zero, scope.Settings());
            string error;
            view.TryApply(data, out error);

            GameObject container = view.Container;
            Mesh mesh = view.SharedMesh;
            Check(container != null && mesh != null, "前置：容器与资产都已创建");

            view.Release();

            Check(container == null, "V9：Release 后容器已被销毁");
            Check(mesh == null, "V9：Release 后自有 Mesh 资产已被销毁");
            Check(view.Container == null && view.SharedMesh == null,
                "V9：View 引用已清空（Container / SharedMesh）");
            Check(view.State == ChunkViewState.Absent, "V9：状态回到 Absent");
            Check(view.IsReleased, "V9：IsReleased = true（释放为终态）");
            Check(scope.Root.childCount == 0,
                "V9：Root 下无残留对象（实际 " + scope.Root.childCount + "）");

            // 幂等释放
            bool threw = false;
            try { view.Release(); view.Release(); }
            catch (Exception ex) { threw = true; Debug.LogError(ex); }
            Check(!threw, "V9：重复 Release 幂等且不抛错");

            view.Dispose();
            Check(scope.Root.childCount == 0, "V9：Dispose 后仍无残留");

            // 25 次创建 / 提交 / 释放循环：容器与资产都不得累积
            const int cycles = 25;
            int leakedContainers = 0;
            int leakedMeshes = 0;

            for (int i = 0; i < cycles; i++)
            {
                VoxelChunkView cycleView = new VoxelChunkView(
                    new VoxelCoordinate(i, 0, -i), scope.Settings());
                GameObject cycleContainer = null;
                Mesh cycleMesh = null;

                try
                {
                    cycleView.TryApply(data, out error);
                    cycleContainer = cycleView.Container;
                    cycleMesh = cycleView.SharedMesh;
                    cycleView.Release();

                    if (cycleContainer != null) leakedContainers++;
                    if (cycleMesh != null) leakedMeshes++;
                }
                finally
                {
                    // 失败时兜底，避免污染后续计数
                    if (cycleView.Container != null) cycleView.Release();
                }
            }

            Check(leakedContainers == 0,
                "V9：" + cycles + " 次循环后无容器泄漏（泄漏 " + leakedContainers + "）");
            Check(leakedMeshes == 0,
                "V9：" + cycles + " 次循环后无 Mesh 资产泄漏（泄漏 " + leakedMeshes + "）");
            Check(scope.Root.childCount == 0,
                "V9：循环结束后 Root 无残留（实际 " + scope.Root.childCount + "）");
        }
    }

    // =====================================================================
    // 11) 呈现与数据一致（V10 / V3 真实数据）
    // =====================================================================

    private static void ValidatePresentedMatchesData()
    {
        using (TestScope scope = new TestScope("ViewTest_Consistency"))
        {
            BasicTerrainGenerator generator =
                new BasicTerrainGenerator(WorldGenerationSettings.Default);
            VoxelCoordinate coordinate = new VoxelCoordinate(1, 0, -1);
            VoxelChunk chunk = generator.Generate(coordinate);
            VoxelMeshData data = VoxelMeshGenerator.Generate(chunk);

            Check(data.TriangleCount > 0,
                "前置：真实地形 Chunk 有几何（三角形 " + data.TriangleCount + "）");

            VoxelChunkView view = new VoxelChunkView(coordinate, scope.Settings());
            string error;
            view.TryApply(data, out error);

            // V10：Ready（Presented）时的资产内容 == 用当前数据重新 Generate 的结果
            VoxelMeshData regenerated = VoxelMeshGenerator.Generate(chunk);
            Check(regenerated.ComputeContentHash() == data.ComputeContentHash(),
                "V10：同数据重复 Meshing 内容哈希一致（" +
                data.ComputeContentHash() + "）");

            string detail;
            Check(CompareMeshToData(view.SharedMesh, regenerated, out detail),
                "V10：呈现内容 == 当前数据重新 Meshing 的结果（" +
                (detail ?? "顶点/法线/UV/3 submesh 全部一致") + "）");

            ulong meshHash = ComputeMeshHash(view.SharedMesh);
            Debug.Log("[VoxelWorldViewValidation] 真实地形呈现内容哈希 = " +
                      meshHash.ToString("X16") + "，三角形 = " + data.TriangleCount);

            // 两个 View（同一份数据）内容必须一致
            VoxelChunkView twin = new VoxelChunkView(
                new VoxelCoordinate(2, 0, -1), scope.Settings());
            twin.TryApply(data, out error);
            Check(ComputeMeshHash(twin.SharedMesh) == meshHash,
                "V10：两个 View 呈现同一份数据 ⇒ 内容哈希一致");

            view.Release();
            twin.Release();
        }
    }

    // =====================================================================
    // 12) 多 View 互不干扰（V2 / V15）
    // =====================================================================

    private static void ValidateTwoViewsAreIndependent()
    {
        using (TestScope scope = new TestScope("ViewTest_MultipleViews"))
        {
            VoxelChunk chunk = new VoxelChunk(VoxelCoordinate.Zero);
            chunk.SetVoxel(1, 1, 1, VoxelType.Stone);
            VoxelMeshData data = VoxelMeshGenerator.Generate(chunk);

            VoxelCoordinate coordinateA = new VoxelCoordinate(0, 0, 0);
            VoxelCoordinate coordinateB = new VoxelCoordinate(1, 0, 0);

            VoxelChunkView viewA = new VoxelChunkView(coordinateA, scope.Settings());
            VoxelChunkView viewB = new VoxelChunkView(coordinateB, scope.Settings());
            string error;
            viewA.TryApply(data, out error);
            viewB.TryApply(data, out error);

            Check(scope.Root.childCount == 2,
                "V15：两个容器都是 Root 的直接子级（实际 " + scope.Root.childCount + "）");
            Check(viewA.SharedMesh != null && viewB.SharedMesh != null &&
                  viewA.SharedMesh != viewB.SharedMesh,
                "V2：两个 View 各自持有独立资产（禁止跨 Chunk 共享）");
            Check(viewA.Container.transform.localPosition == Vector3.zero &&
                  viewB.Container.transform.localPosition == new Vector3(16f, 0f, 0f),
                "V1：两个容器分别摆放在 coordinate*16");

            Mesh meshA = viewA.SharedMesh;

            viewA.Release();

            Check(scope.Root.childCount == 1,
                "V15：释放 A 后 Root 只剩 1 个子级（实际 " + scope.Root.childCount + "）");
            Check(meshA == null, "V2：释放 A 销毁其自有资产");
            Check(viewB.SharedMesh != null && viewB.State == ChunkViewState.Presented,
                "V2：释放 A 不影响 B 的呈现");
            Check(viewB.Container != null && viewB.Container.transform.parent == scope.Root,
                "V15：B 的父级不变");

            string detail;
            Check(CompareMeshToData(viewB.SharedMesh, data, out detail),
                "V2：B 的内容未受影响（" + (detail ?? "OK") + "）");

            viewB.Release();
            Check(scope.Root.childCount == 0, "V15：全部释放后 Root 无残留");
        }
    }

    // =====================================================================
    // 13) View 层结构性约束（V11 / V12 / §1.2 / §1.6 / §5.2 / §5.5）
    // =====================================================================

    private static void ValidateViewLayerHasNoWorldRules()
    {
        string folder = Path.Combine(Application.dataPath, ViewSourceFolder);

        Check(Directory.Exists(folder), "结构：View 源码目录存在（" + folder + "）");
        if (!Directory.Exists(folder)) return;

        string[] files = Directory.GetFiles(folder, "*.cs", SearchOption.TopDirectoryOnly);
        Array.Sort(files, StringComparer.Ordinal);

        Check(files.Length >= 2,
            "结构：View 层至少包含 2 个源文件（实际 " + files.Length + "）");

        foreach (string expected in new string[] { "VoxelChunkView.cs", "UnityMeshBuilder.cs" })
        {
            Check(File.Exists(Path.Combine(folder, expected)),
                "结构：存在 " + expected);
        }

        // 范围：第一阶段不含 Runtime / Loader / Save / Streaming
        int scopeViolations = 0;
        for (int i = 0; i < files.Length; i++)
        {
            string name = Path.GetFileName(files[i]);
            if (name.IndexOf("Runtime", StringComparison.Ordinal) >= 0 ||
                name.IndexOf("Loader", StringComparison.Ordinal) >= 0 ||
                name.IndexOf("Save", StringComparison.Ordinal) >= 0 ||
                name.IndexOf("Streaming", StringComparison.Ordinal) >= 0)
            {
                scopeViolations++;
            }
        }

        Check(scopeViolations == 0,
            "范围：View 目录不含 Runtime/Loader/Save/Streaming 文件（目录共 " +
            files.Length + " 个 .cs）");

        // 扫描器自检（保证"扫描能真的抓到东西"）
        string probe = "void P(){ c.SetVoxel(0,0,0,VoxelType.Air); } " +
                       "// 注释里出现 SetVoxel 不应计数\n";
        List<string> probeHits = ScanForbiddenTokens(StripComments(probe));
        Check(probeHits.Count == 1 && probeHits[0] == "SetVoxel",
            "结构扫描器自检：能检出代码区违规、忽略注释（命中 " +
            probeHits.Count + " 处）");

        // 逐个 View 源文件扫描（先剔除注释，避免"注释里解释禁令"被误判）
        int totalHits = 0;
        for (int i = 0; i < files.Length; i++)
        {
            string code = StripComments(File.ReadAllText(files[i], Encoding.UTF8));
            List<string> hits = ScanForbiddenTokens(code);

            if (hits.Count > 0)
            {
                totalHits += hits.Count;
                Check(false, "V11/V12：" + Path.GetFileName(files[i]) +
                    " 命中禁止项：" + string.Join(", ", hits.ToArray()));
            }
        }

        Check(totalHits == 0,
            "V11/V12：View 层不含数据写入(SetVoxel/MarkDirty/ClearDirty)、" +
            "生成(HeightFunction/BasicTerrainGenerator/WorldGenerationSettings)、" +
            "挖掘(Mining)、旧地形(TerrainModificationV2/DensityField)、" +
            "Runtime/Loader/Save 引用、VoxelChunk 数据对象、Physics.Raycast、" +
            "RecalculateNormals/Bounds、MeshFilter.mesh、Time（扫描 " +
            files.Length + " 个文件，命中 " + totalHits + "）");

        Debug.Log("[VoxelWorldViewValidation] 结构扫描文件：" +
                  string.Join(", ", files));
    }

    /// <summary>View 层禁止项（在剔除注释后的代码区匹配）。</summary>
    private static readonly string[] ForbiddenLiterals =
    {
        "SetVoxel",                  // 数据写入（View §1.3）
        "MarkDirty",                 // 脏标记
        "ClearDirty",                // 清脏属 Runtime Core
        "HeightFunction",            // 生成规则
        "BasicTerrainGenerator",
        "WorldGenerationSettings",
        "IWorldGenerator",
        "Mining",                    // 挖掘（Mining §1）
        "TerrainModificationV2",     // 旧 Terrain 体系
        "DensityField",
        "VoxelWorldRuntime",         // Runtime / Loader / Save（第一阶段不做）
        "ChunkLoader",
        "VoxelWorldLoader",
        "SaveWorld",
        "SaveChunk",
        "ISaveData",
        "Physics.Raycast",           // 查询层规则（View §8.3）
        "RecalculateNormals",        // 上传保真禁令（View §5.2）
        "RecalculateBounds",
        "Time."                      // 确定性（View §1.6）
    };

    private static readonly string[] ForbiddenPatterns =
    {
        @"VoxelChunk(?!View)",       // 不得依赖数据层对象（View §1.2）
        @"\.mesh\b"                  // 禁止 MeshFilter.mesh（View §5.5-1）
    };

    private static List<string> ScanForbiddenTokens(string code)
    {
        List<string> hits = new List<string>();

        for (int i = 0; i < ForbiddenLiterals.Length; i++)
        {
            if (code.IndexOf(ForbiddenLiterals[i], StringComparison.Ordinal) >= 0)
            {
                hits.Add(ForbiddenLiterals[i]);
            }
        }

        for (int i = 0; i < ForbiddenPatterns.Length; i++)
        {
            if (Regex.IsMatch(code, ForbiddenPatterns[i]))
            {
                hits.Add(ForbiddenPatterns[i]);
            }
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
    // 测试工具
    // =====================================================================

    private sealed class TestScope : IDisposable
    {
        public readonly GameObject RootObject;
        public readonly Transform Root;

        public TestScope(string name)
        {
            RootObject = new GameObject(name);
            Root = RootObject.transform;
        }

        public ChunkViewSettings Settings(bool buildCollider = true, int layer = 0)
        {
            return new ChunkViewSettings
            {
                Root = Root,
                SubmeshMaterials = sharedMaterials,
                BuildCollider = buildCollider,
                Layer = layer
            };
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

    /// <summary>合成一个"仅含数据"的缓冲（不经过生成器 / Meshing），用于边界与索引格式测试。</summary>
    private static VoxelMeshData CreateSyntheticQuadData(int quadCount)
    {
        int vertexCount = quadCount * 4;

        Vector3[] vertices = new Vector3[vertexCount];
        Vector3[] normals = new Vector3[vertexCount];
        Vector2[] uvs = new Vector2[vertexCount];

        int[] perSubmesh = new int[VoxelMeshData.SubmeshCount];
        for (int q = 0; q < quadCount; q++) perSubmesh[q % VoxelMeshData.SubmeshCount]++;

        int[][] triangles = new int[VoxelMeshData.SubmeshCount][];
        for (int s = 0; s < VoxelMeshData.SubmeshCount; s++)
        {
            triangles[s] = new int[perSubmesh[s] * 6];
        }

        int[] cursor = new int[VoxelMeshData.SubmeshCount];

        for (int q = 0; q < quadCount; q++)
        {
            int b = q * 4;

            float x = q % VoxelChunkView.ChunkSize;
            float z = (q / VoxelChunkView.ChunkSize) % VoxelChunkView.ChunkSize;

            // 位于 [0,16]³ 内的水平单位面
            vertices[b + 0] = new Vector3(x, 0.5f, z);
            vertices[b + 1] = new Vector3(x + 1f, 0.5f, z);
            vertices[b + 2] = new Vector3(x + 1f, 0.5f, z + 1f);
            vertices[b + 3] = new Vector3(x, 0.5f, z + 1f);

            normals[b + 0] = Vector3.up;
            normals[b + 1] = Vector3.up;
            normals[b + 2] = Vector3.up;
            normals[b + 3] = Vector3.up;

            uvs[b + 0] = new Vector2(0f, 0f);
            uvs[b + 1] = new Vector2(1f, 0f);
            uvs[b + 2] = new Vector2(1f, 1f);
            uvs[b + 3] = new Vector2(0f, 1f);

            int submesh = q % VoxelMeshData.SubmeshCount;
            int c = cursor[submesh];
            int[] t = triangles[submesh];

            t[c + 0] = b + 0;
            t[c + 1] = b + 1;
            t[c + 2] = b + 2;
            t[c + 3] = b + 0;
            t[c + 4] = b + 2;
            t[c + 5] = b + 3;

            cursor[submesh] += 6;
        }

        return new VoxelMeshData(vertices, normals, uvs, triangles);
    }

    /// <summary>结构性非法缓冲：含 NaN 顶点（会污染 bounds），用于失败注入。</summary>
    private static VoxelMeshData CreateNaNVertexData()
    {
        Vector3[] vertices =
        {
            new Vector3(float.NaN, 0f, 0f),
            new Vector3(1f, 0f, 0f),
            new Vector3(1f, 0f, 1f),
            new Vector3(0f, 0f, 1f)
        };

        Vector3[] normals = { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
        Vector2[] uvs =
        {
            new Vector2(0f, 0f), new Vector2(1f, 0f),
            new Vector2(1f, 1f), new Vector2(0f, 1f)
        };

        int[][] triangles = new int[VoxelMeshData.SubmeshCount][];
        triangles[0] = new int[] { 0, 1, 2, 0, 2, 3 };
        triangles[1] = new int[0];
        triangles[2] = new int[0];

        return new VoxelMeshData(vertices, normals, uvs, triangles);
    }

    /// <summary>V3：资产内容与纯数据缓冲逐元素比较。</summary>
    private static bool CompareMeshToData(Mesh mesh, VoxelMeshData data, out string detail)
    {
        detail = null;

        if (mesh == null) { detail = "Mesh 资产为 null"; return false; }
        if (data == null) { detail = "VoxelMeshData 为 null"; return false; }

        if (mesh.vertexCount != data.VertexCount)
        {
            detail = "顶点数不一致：" + mesh.vertexCount + " != " + data.VertexCount;
            return false;
        }

        Vector3[] meshVertices = mesh.vertices;
        Vector3[] meshNormals = mesh.normals;
        Vector2[] meshUVs = mesh.uv;

        if (meshNormals.Length != data.VertexCount)
        {
            detail = "法线数量不一致：" + meshNormals.Length;
            return false;
        }

        if (meshUVs.Length != data.VertexCount)
        {
            detail = "UV 数量不一致：" + meshUVs.Length;
            return false;
        }

        for (int i = 0; i < meshVertices.Length; i++)
        {
            if (meshVertices[i] != data.Vertices[i])
            {
                detail = "顶点 " + i + " 不一致：" + meshVertices[i] + " != " +
                         data.Vertices[i];
                return false;
            }

            if (meshNormals[i] != data.Normals[i])
            {
                detail = "法线 " + i + " 不一致：" + meshNormals[i] + " != " +
                         data.Normals[i];
                return false;
            }

            if (meshUVs[i] != data.UVs[i])
            {
                detail = "UV " + i + " 不一致：" + meshUVs[i] + " != " + data.UVs[i];
                return false;
            }
        }

        int totalTriangles = 0;

        for (int submesh = 0; submesh < VoxelMeshData.SubmeshCount; submesh++)
        {
            int[] actual = mesh.GetTriangles(submesh);
            int[] expected = data.GetSubmeshTriangles(submesh);

            if (actual.Length != expected.Length)
            {
                detail = "submesh " + submesh + " 索引数不一致：" + actual.Length +
                         " != " + expected.Length;
                return false;
            }

            for (int i = 0; i < actual.Length; i++)
            {
                if (actual[i] != expected[i])
                {
                    detail = "submesh " + submesh + " 索引 " + i + " 不一致：" +
                             actual[i] + " != " + expected[i];
                    return false;
                }
            }

            totalTriangles += actual.Length / 3;
        }

        if (totalTriangles != data.TriangleCount)
        {
            detail = "三角形总数不一致：" + totalTriangles + " != " + data.TriangleCount;
            return false;
        }

        return true;
    }

    /// <summary>独立计算资产内容哈希（FNV-1a 64），用于"两次呈现完全一致"类断言。</summary>
    private static ulong ComputeMeshHash(Mesh mesh)
    {
        if (mesh == null) return 0UL;

        const ulong offset = 1469598103934665603UL;
        const ulong prime = 1099511628211UL;

        ulong hash = offset;

        Vector3[] vertices = mesh.vertices;
        Vector3[] normals = mesh.normals;
        Vector2[] uvs = mesh.uv;

        hash = HashInt(hash, vertices.Length, prime);

        for (int i = 0; i < vertices.Length; i++)
        {
            hash = HashFloat(hash, vertices[i].x, prime);
            hash = HashFloat(hash, vertices[i].y, prime);
            hash = HashFloat(hash, vertices[i].z, prime);
            hash = HashFloat(hash, normals[i].x, prime);
            hash = HashFloat(hash, normals[i].y, prime);
            hash = HashFloat(hash, normals[i].z, prime);
            hash = HashFloat(hash, uvs[i].x, prime);
            hash = HashFloat(hash, uvs[i].y, prime);
        }

        for (int submesh = 0; submesh < VoxelMeshData.SubmeshCount; submesh++)
        {
            int[] indices = mesh.GetTriangles(submesh);
            hash = HashInt(hash, indices.Length, prime);

            for (int i = 0; i < indices.Length; i++)
            {
                hash = HashInt(hash, indices[i], prime);
            }
        }

        return hash;
    }

    private static ulong HashInt(ulong hash, int value, ulong prime)
    {
        unchecked
        {
            hash ^= (uint)value;
            hash *= prime;
            return hash;
        }
    }

    private static ulong HashFloat(ulong hash, float value, ulong prime)
    {
        return HashInt(hash, BitConverter.SingleToInt32Bits(value), prime);
    }

    private static VoxelType[] SnapshotVoxels(VoxelChunk chunk)
    {
        VoxelType[] snapshot = new VoxelType[VoxelChunk.VoxelCount];
        int index = 0;

        for (int x = 0; x < VoxelChunk.ChunkSize; x++)
        {
            for (int y = 0; y < VoxelChunk.ChunkSize; y++)
            {
                for (int z = 0; z < VoxelChunk.ChunkSize; z++)
                {
                    snapshot[index++] = chunk.GetVoxel(x, y, z).Type;
                }
            }
        }

        return snapshot;
    }

    private static bool VoxelsEqual(VoxelChunk chunk, VoxelType[] expected)
    {
        int index = 0;

        for (int x = 0; x < VoxelChunk.ChunkSize; x++)
        {
            for (int y = 0; y < VoxelChunk.ChunkSize; y++)
            {
                for (int z = 0; z < VoxelChunk.ChunkSize; z++)
                {
                    if (chunk.GetVoxel(x, y, z).Type != expected[index++])
                    {
                        return false;
                    }
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
                Debug.Log("[VoxelWorldViewValidation] 无有效场景 ⇒ 已创建空场景用于验证");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[VoxelWorldViewValidation] 场景准备异常（继续执行）：" +
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
            if (shader != null)
            {
                shaderName = candidates[i];
                break;
            }
        }

        sharedMaterials = new Material[VoxelMeshData.SubmeshCount];

        if (shader == null)
        {
            materialsAreReal = false;
            Debug.LogWarning("[VoxelWorldViewValidation] 未找到可用 Shader ⇒ " +
                             "使用 null 占位材质（View §6.4 允许；材质槽长度/顺序仍被断言）");
            return;
        }

        for (int i = 0; i < sharedMaterials.Length; i++)
        {
            sharedMaterials[i] = new Material(shader);
            sharedMaterials[i].name = "ViewValidation_Shared_" + i;
        }

        materialsAreReal = true;
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
            Check(false, "测试「" + name + "」抛异常：" + ex.GetType().Name + ": " +
                         ex.Message);
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
