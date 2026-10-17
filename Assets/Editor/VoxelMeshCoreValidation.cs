using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using VoxelWorld;
using VoxelWorld.Mesh;

/// <summary>
/// Voxel Mesh Core（Phase 1：Cube Surface Meshing）验证。
///
/// 用法：
///   · Editor 菜单：Tools/VoxelWorld/Validate Mesh Core
///   · 批处理：-executeMethod VoxelMeshCoreValidation.RunBatch
///
/// 覆盖用户要求 + Mesh 规范 §8 不变量：
///   1) 单个 Stone voxel ⇒ 6 faces / 12 triangles；
///   2) 两个相邻 Stone   ⇒ 10 faces / 20 triangles（共享面被剔除）；
///   3) Stone + Air      ⇒ 边界面位置/法线正确，固体-固体面不存在；
///   4) 空 Chunk         ⇒ 0 triangles / 0 vertices；
///   5) 两次生成结果完全一致（内容哈希 + 逐元素比较）；
///   + 只读性（不修改 VoxelChunk）、Chunk 边界规则 B1、覆盖完整性、无重复面、
///     法线/绕序、顶点范围与整数性、UV、材质 submesh 映射。
/// </summary>
public static class VoxelMeshCoreValidation
{
    private static int checks;
    private static int failures;

    [MenuItem("Tools/VoxelWorld/Validate Mesh Core")]
    public static void ValidateFromMenu()
    {
        ValidateAll();

        Debug.Log(
            "[VoxelMeshCoreValidation] Menu validation result = " +
            (failures == 0 ? "PASS" : "FAIL")
        );
    }

    public static void RunBatch()
    {
        ValidateAll();

        Debug.Log(
            "[VoxelMeshCoreValidation] ===== SUMMARY =====\n" +
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

        ValidateSingleVoxel();
        ValidateTwoAdjacentVoxels();
        ValidateStoneAirBoundary();
        ValidateEmptyChunk();
        ValidateDeterminism();
        ValidateGeneratorIsReadOnly();
        ValidateChunkBoundaryPolicyB1();
        ValidateCoverageAndWindingOnMixedChunk();
        ValidateMaterialSubmeshes();
    }

    // =====================================================================
    // 1) 单个 Stone voxel ⇒ 6 faces / 12 triangles
    // =====================================================================

    private static void ValidateSingleVoxel()
    {
        VoxelChunk chunk = CreateChunk();
        chunk.SetVoxel(5, 5, 5, VoxelType.Stone);

        VoxelMeshData mesh = VoxelMeshGenerator.Generate(chunk);

        Check(mesh.QuadCount == 6,
            "单个 Stone Voxel：6 个面（quad=" + mesh.QuadCount + "）");
        Check(mesh.TriangleCount == 12,
            "单个 Stone Voxel：12 个三角形（triangle=" + mesh.TriangleCount + "）");
        Check(mesh.VertexCount == 24,
            "单个 Stone Voxel：24 个顶点（vertex=" + mesh.VertexCount + "）");
        Check(mesh.GetSubmeshTriangleCount(2) == 12,
            "单个 Stone Voxel：三角形全部落在 submesh 2（Stone，实际 " +
            mesh.GetSubmeshTriangleCount(2) + "）");

        // 6 个方向法线各出现一次
        Check(CountDistinctFaceNormals(mesh) == 6,
            "单个 Stone Voxel：6 个面的法线互不相同（实际 " +
            CountDistinctFaceNormals(mesh) + "）");

        Check(VerticesInRange(mesh) && VerticesAreIntegers(mesh),
            "单个 Stone Voxel：顶点均为整数且位于 [0, 16] 内");
    }

    // =====================================================================
    // 2) 两个相邻 Stone ⇒ 10 faces / 20 triangles
    // =====================================================================

    private static void ValidateTwoAdjacentVoxels()
    {
        VoxelChunk chunk = CreateChunk();
        chunk.SetVoxel(5, 5, 5, VoxelType.Stone);
        chunk.SetVoxel(6, 5, 5, VoxelType.Stone);

        VoxelMeshData mesh = VoxelMeshGenerator.Generate(chunk);

        Check(mesh.QuadCount == 10,
            "两个相邻 Stone：10 个面（quad=" + mesh.QuadCount + "）");
        Check(mesh.TriangleCount == 20,
            "两个相邻 Stone：20 个三角形（triangle=" + mesh.TriangleCount + "）");
        Check(!HasQuadOnPlane(mesh, 0, 6f),
            "两个相邻 Stone：共享面 x=6 已剔除（不生成内部面）");
        Check(HasQuadOnPlane(mesh, 0, 5f) && HasQuadOnPlane(mesh, 0, 7f),
            "两个相邻 Stone：外侧 x=5 / x=7 面存在");
    }

    // =====================================================================
    // 3) Stone + Air ⇒ 边界面正确
    // =====================================================================

    private static void ValidateStoneAirBoundary()
    {
        VoxelChunk chunk = CreateChunk();
        chunk.SetVoxel(8, 8, 8, VoxelType.Stone); // 下方实体
        chunk.SetVoxel(8, 9, 8, VoxelType.Stone); // 上方实体（竖直相邻）
        // 其余（含 (9,8,8) / (9,9,8) / (8,10,8)）保持 Air

        VoxelMeshData mesh = VoxelMeshGenerator.Generate(chunk);

        Check(!HasQuadOnPlane(mesh, 1, 9f),
            "Stone + Air：固体-固体共享面 y=9 不生成");
        Check(CountQuadsOnPlaneWithNormal(mesh, 0, 9f, Vector3.right) == 2,
            "Stone + Air：Air 侧（x=9）暴露 2 个 +X 面（上下体素各 1，实际 " +
            CountQuadsOnPlaneWithNormal(mesh, 0, 9f, Vector3.right) + "）");
        Check(
            QuadCoversRectOnPlane(mesh, 0, 9f, Vector3.right, 8f, 9f, 8f, 9f) &&
            QuadCoversRectOnPlane(mesh, 0, 9f, Vector3.right, 9f, 10f, 8f, 9f),
            "Stone + Air：两个 +X 面的矩形分别覆盖 y∈[8,9] 与 y∈[9,10]、z∈[8,9]");
        Check(CountQuadsOnPlaneWithNormal(mesh, 0, 9f, Vector3.left) == 0 &&
              !HasQuadOnPlane(mesh, 0, 10f),
            "Stone + Air：Air 一侧不产生任何几何（无反向面、无 x=10 面）");
        Check(mesh.QuadCount == 10,
            "Stone + Air：总面数 = 2×6 − 共享 2 = 10（实际 " + mesh.QuadCount + "）");
    }

    // =====================================================================
    // 4) 空 Chunk ⇒ 0 triangles
    // =====================================================================

    private static void ValidateEmptyChunk()
    {
        VoxelChunk chunk = CreateChunk();

        VoxelMeshData mesh = VoxelMeshGenerator.Generate(chunk);

        Check(mesh.TriangleCount == 0,
            "空 Chunk：0 个三角形（实际 " + mesh.TriangleCount + "）");
        Check(mesh.VertexCount == 0,
            "空 Chunk：0 个顶点（实际 " + mesh.VertexCount + "）");
        Check(!mesh.HasGeometry, "空 Chunk：HasGeometry = false");
        Check(mesh.GetSubmeshTriangleCount(0) == 0 &&
              mesh.GetSubmeshTriangleCount(1) == 0 &&
              mesh.GetSubmeshTriangleCount(2) == 0,
            "空 Chunk：3 个 submesh 全为空");
    }

    // =====================================================================
    // 5) 两次生成结果完全一致
    // =====================================================================

    private static void ValidateDeterminism()
    {
        VoxelChunk chunk = CreateMixedChunk();

        VoxelMeshData first = VoxelMeshGenerator.Generate(chunk);
        VoxelMeshData second = VoxelMeshGenerator.Generate(chunk);

        Check(first.ComputeContentHash() == second.ComputeContentHash(),
            "确定性：同一 Chunk 两次生成的内容哈希一致（" +
            first.ComputeContentHash() + "）");
        Check(MeshesEqual(first, second),
            "确定性：同一 Chunk 两次生成的顶点/法线/UV/索引逐元素一致");

        // 两份独立构造（内容相同）的 Chunk 必须生成相同 Mesh
        VoxelMeshData fromTwin = VoxelMeshGenerator.Generate(CreateMixedChunk());
        Check(MeshesEqual(first, fromTwin),
            "确定性：内容相同的两个独立 Chunk 生成结果一致");

        // 生成顺序不影响（另一 Chunk 先/后生成）
        VoxelChunk other = CreateChunk();
        other.SetVoxel(1, 2, 3, VoxelType.Dirt);

        VoxelMeshData a1 = VoxelMeshGenerator.Generate(chunk);
        VoxelMeshGenerator.Generate(other);
        VoxelMeshData a2 = VoxelMeshGenerator.Generate(chunk);

        Check(MeshesEqual(a1, a2),
            "确定性：生成结果与调用顺序无关（先/后生成其它 Chunk 结果一致）");
    }

    // =====================================================================
    // 附加：只读性 / 边界规则 / 覆盖完整性（Mesh 规范 §8 I1/I2/I3/B1）
    // =====================================================================

    private static void ValidateGeneratorIsReadOnly()
    {
        VoxelChunk chunk = CreateMixedChunk();

        VoxelType[] before = SnapshotVoxels(chunk);
        bool dirtyBefore = chunk.IsDirty;
        VoxelCoordinate coordinateBefore = chunk.Coordinate;

        VoxelMeshGenerator.Generate(chunk);

        bool same = chunk.IsDirty == dirtyBefore &&
                    chunk.Coordinate == coordinateBefore;

        if (same)
        {
            for (int x = 0; x < VoxelChunk.ChunkSize && same; x++)
            {
                for (int y = 0; y < VoxelChunk.ChunkSize && same; y++)
                {
                    for (int z = 0; z < VoxelChunk.ChunkSize; z++)
                    {
                        if (chunk.GetVoxel(x, y, z).Type !=
                            before[Index(x, y, z)])
                        {
                            same = false;
                            break;
                        }
                    }
                }
            }
        }

        Check(same,
            "只读性：Generate 不修改 VoxelChunk（4096 个体素 + 坐标 + 脏标记均不变）");
    }

    private static void ValidateChunkBoundaryPolicyB1()
    {
        VoxelChunk chunk = CreateChunk();
        chunk.SetVoxel(0, 0, 0, VoxelType.Stone); // 三个负方向都在 Chunk 外

        VoxelMeshData mesh = VoxelMeshGenerator.Generate(chunk);

        Check(mesh.QuadCount == 6,
            "边界规则 B1：角落 Voxel 的 6 个面全部生成（Chunk 外按 Air，实际 " +
            mesh.QuadCount + "）");
        Check(HasQuadOnPlane(mesh, 0, 0f) &&
              HasQuadOnPlane(mesh, 1, 0f) &&
              HasQuadOnPlane(mesh, 2, 0f),
            "边界规则 B1：x=0 / y=0 / z=0 三个边界面均存在");
        Check(mesh.VertexCount == 24 && VerticesInRange(mesh),
            "边界规则 B1：顶点仍位于 [0, 16] 内（不外扩）");
    }

    private static void ValidateCoverageAndWindingOnMixedChunk()
    {
        VoxelChunk chunk = CreateMixedChunk();
        VoxelMeshData mesh = VoxelMeshGenerator.Generate(chunk);

        // 独立重算期望面集合（solid×6 − Chunk 内 solid-solid 相邻）
        HashSet<string> expected = BuildExpectedFaceKeys(chunk);
        HashSet<string> actual = BuildActualFaceKeys(mesh);

        Check(expected.Count == mesh.QuadCount,
            "覆盖完整性：期望面数 = 实际 quad 数（期望 " + expected.Count +
            "，实际 " + mesh.QuadCount + "）");
        Check(SetEquals(expected, actual),
            "覆盖完整性 + 无重复面：面键集合完全一致（期望 " + expected.Count +
            "，实际 " + actual.Count + "）");
        int duplicates = CountDuplicateFaceKeys(mesh);
        Check(duplicates == 0,
            "无重复面：不存在同一 (voxel, 方向) 被生成两次（实际重复 " +
            duplicates + "）");
        Check(mesh.VertexCount == mesh.QuadCount * 4,
            "第一阶段约定：每个 quad 独立 4 个顶点、不做焊接（顶点 " +
            mesh.VertexCount + " = 4 × " + mesh.QuadCount + "）");
        Check(WindingConsistent(mesh),
            "法线/绕序：每个 quad 的 cross(v1-v0, v2-v0) 与法线同向");
        Check(UVsAreUnitQuad(mesh),
            "UV：每个 quad 的 4 个 UV = (0,0)/(1,0)/(1,1)/(0,1)");
    }

    private static void ValidateMaterialSubmeshes()
    {
        VoxelChunk chunk = CreateChunk();
        chunk.SetVoxel(2, 2, 2, VoxelType.Grass);
        chunk.SetVoxel(6, 2, 2, VoxelType.Dirt);
        chunk.SetVoxel(10, 2, 2, VoxelType.Stone);

        VoxelMeshData mesh = VoxelMeshGenerator.Generate(chunk);

        Check(mesh.GetSubmeshTriangleCount(0) == 12,
            "材质映射：Grass ⇒ submesh 0（12 三角形，实际 " +
            mesh.GetSubmeshTriangleCount(0) + "）");
        Check(mesh.GetSubmeshTriangleCount(1) == 12,
            "材质映射：Dirt ⇒ submesh 1（12 三角形，实际 " +
            mesh.GetSubmeshTriangleCount(1) + "）");
        Check(mesh.GetSubmeshTriangleCount(2) == 12,
            "材质映射：Stone ⇒ submesh 2（12 三角形，实际 " +
            mesh.GetSubmeshTriangleCount(2) + "）");
        Check(mesh.QuadCount == 18,
            "三块互不相邻的实体 ⇒ 18 个面（实际 " + mesh.QuadCount + "）");
    }

    // =====================================================================
    // 工具
    // =====================================================================

    private static VoxelChunk CreateChunk()
    {
        return new VoxelChunk(VoxelCoordinate.Zero);
    }

    /// <summary>构造一个内容固定的混合 Chunk（用于确定性与覆盖检查）。</summary>
    private static VoxelChunk CreateMixedChunk()
    {
        VoxelChunk chunk = CreateChunk();

        // 3×3×2 的实体块 + 一根柱子 + 一块孤立石头 + 一层草/土/石
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

        for (int y = 5; y <= 9; y++)
        {
            chunk.SetVoxel(8, y, 8, VoxelType.Dirt);
        }

        chunk.SetVoxel(12, 12, 12, VoxelType.Stone);
        chunk.SetVoxel(1, 14, 1, VoxelType.Grass);
        chunk.SetVoxel(14, 1, 14, VoxelType.Dirt);

        return chunk;
    }

    private static VoxelType[] SnapshotVoxels(VoxelChunk chunk)
    {
        VoxelType[] snapshot = new VoxelType[VoxelChunk.VoxelCount];

        for (int x = 0; x < VoxelChunk.ChunkSize; x++)
        {
            for (int y = 0; y < VoxelChunk.ChunkSize; y++)
            {
                for (int z = 0; z < VoxelChunk.ChunkSize; z++)
                {
                    snapshot[Index(x, y, z)] = chunk.GetVoxel(x, y, z).Type;
                }
            }
        }

        return snapshot;
    }

    private static int Index(int x, int y, int z)
    {
        return (x * VoxelChunk.ChunkSize + y) * VoxelChunk.ChunkSize + z;
    }

    private static bool MeshesEqual(VoxelMeshData a, VoxelMeshData b)
    {
        if (a == null || b == null) return false;
        if (a.VertexCount != b.VertexCount) return false;
        if (a.TriangleCount != b.TriangleCount) return false;

        for (int i = 0; i < a.VertexCount; i++)
        {
            if (a.Vertices[i] != b.Vertices[i]) return false;
            if (a.Normals[i] != b.Normals[i]) return false;
            if (a.UVs[i] != b.UVs[i]) return false;
        }

        for (int submesh = 0; submesh < VoxelMeshData.SubmeshCount; submesh++)
        {
            int[] ia = a.GetSubmeshTriangles(submesh);
            int[] ib = b.GetSubmeshTriangles(submesh);
            if (ia.Length != ib.Length) return false;

            for (int i = 0; i < ia.Length; i++)
            {
                if (ia[i] != ib[i]) return false;
            }
        }

        return true;
    }

    private static bool VerticesInRange(VoxelMeshData mesh)
    {
        for (int i = 0; i < mesh.VertexCount; i++)
        {
            Vector3 v = mesh.Vertices[i];

            if (v.x < 0f || v.x > VoxelChunk.ChunkSize ||
                v.y < 0f || v.y > VoxelChunk.ChunkSize ||
                v.z < 0f || v.z > VoxelChunk.ChunkSize)
            {
                return false;
            }
        }

        return true;
    }

    private static bool VerticesAreIntegers(VoxelMeshData mesh)
    {
        for (int i = 0; i < mesh.VertexCount; i++)
        {
            Vector3 v = mesh.Vertices[i];

            if (Mathf.Abs(v.x - Mathf.Round(v.x)) > 0.0001f ||
                Mathf.Abs(v.y - Mathf.Round(v.y)) > 0.0001f ||
                Mathf.Abs(v.z - Mathf.Round(v.z)) > 0.0001f)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>统计不同法线数量（按 4 顶点一组取每组首顶点法线）。</summary>
    private static int CountDistinctFaceNormals(VoxelMeshData mesh)
    {
        HashSet<string> normals = new HashSet<string>();

        for (int quad = 0; quad * 4 < mesh.VertexCount; quad++)
        {
            Vector3 n = mesh.Normals[quad * 4];
            normals.Add(n.x + "," + n.y + "," + n.z);
        }

        return normals.Count;
    }

    /// <summary>是否存在“落在给定轴平面上的 quad”（轴 = 0/1/2）。</summary>
    private static bool HasQuadOnPlane(VoxelMeshData mesh, int axis, float plane)
    {
        for (int quad = 0; quad * 4 + 3 < mesh.VertexCount; quad++)
        {
            int b = quad * 4;

            bool onPlane = true;
            for (int i = 0; i < 4; i++)
            {
                if (Mathf.Abs(mesh.Vertices[b + i][axis] - plane) > 0.0001f)
                {
                    onPlane = false;
                    break;
                }
            }

            if (onPlane) return true;
        }

        return false;
    }

    /// <summary>统计平面上法线等于 normal 的 quad 数量。</summary>
    private static int CountQuadsOnPlaneWithNormal(
        VoxelMeshData mesh, int axis, float plane, Vector3 normal)
    {
        int count = 0;

        for (int quad = 0; quad * 4 + 3 < mesh.VertexCount; quad++)
        {
            int b = quad * 4;

            if ((mesh.Normals[b] - normal).sqrMagnitude > 0.0001f) continue;

            bool onPlane = true;
            for (int i = 0; i < 4; i++)
            {
                if (Mathf.Abs(mesh.Vertices[b + i][axis] - plane) > 0.0001f)
                {
                    onPlane = false;
                    break;
                }
            }

            if (onPlane) count++;
        }

        return count;
    }

    /// <summary>校验平面上的 quad 精确覆盖 [minA,maxA]×[minB,maxB]（切向轴 = 其余两轴）。</summary>
    private static bool QuadCoversRectOnPlane(
        VoxelMeshData mesh, int axis, float plane, Vector3 normal,
        float minA, float maxA, float minB, float maxB)
    {
        int t1 = axis == 0 ? 1 : 0;
        int t2 = axis == 2 ? 1 : 2;

        for (int quad = 0; quad * 4 + 3 < mesh.VertexCount; quad++)
        {
            int b = quad * 4;

            if ((mesh.Normals[b] - normal).sqrMagnitude > 0.0001f) continue;

            float min1 = float.MaxValue, max1 = float.MinValue;
            float min2 = float.MaxValue, max2 = float.MinValue;
            bool onPlane = true;

            for (int i = 0; i < 4; i++)
            {
                Vector3 v = mesh.Vertices[b + i];

                if (Mathf.Abs(v[axis] - plane) > 0.0001f)
                {
                    onPlane = false;
                    break;
                }

                min1 = Mathf.Min(min1, v[t1]);
                max1 = Mathf.Max(max1, v[t1]);
                min2 = Mathf.Min(min2, v[t2]);
                max2 = Mathf.Max(max2, v[t2]);
            }

            if (!onPlane) continue;

            if (Mathf.Abs(min1 - minA) < 0.0001f &&
                Mathf.Abs(max1 - maxA) < 0.0001f &&
                Mathf.Abs(min2 - minB) < 0.0001f &&
                Mathf.Abs(max2 - maxB) < 0.0001f)
            {
                return true;
            }
        }

        return false;
    }

    private static bool WindingConsistent(VoxelMeshData mesh)
    {
        int triangleCount = mesh.TriangleCount;

        for (int submesh = 0; submesh < VoxelMeshData.SubmeshCount; submesh++)
        {
            int[] indices = mesh.GetSubmeshTriangles(submesh);

            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                Vector3 a = mesh.Vertices[indices[i]];
                Vector3 b = mesh.Vertices[indices[i + 1]];
                Vector3 c = mesh.Vertices[indices[i + 2]];

                Vector3 cross = Vector3.Cross(b - a, c - a);
                if (cross.sqrMagnitude < 0.0001f) return false;

                if (Vector3.Dot(cross.normalized, mesh.Normals[indices[i]]) < 0.99f)
                {
                    return false;
                }
            }
        }

        return triangleCount > 0;
    }

    private static bool UVsAreUnitQuad(VoxelMeshData mesh)
    {
        for (int quad = 0; quad * 4 + 3 < mesh.VertexCount; quad++)
        {
            int b = quad * 4;

            if ((mesh.UVs[b + 0] - new Vector2(0f, 0f)).sqrMagnitude > 0.0001f)
            {
                return false;
            }

            if ((mesh.UVs[b + 1] - new Vector2(1f, 0f)).sqrMagnitude > 0.0001f)
            {
                return false;
            }

            if ((mesh.UVs[b + 2] - new Vector2(1f, 1f)).sqrMagnitude > 0.0001f)
            {
                return false;
            }

            if ((mesh.UVs[b + 3] - new Vector2(0f, 1f)).sqrMagnitude > 0.0001f)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>独立重算期望面键：solid voxel × 6 方向，邻居非实体（含 Chunk 外）⇒ 期望存在。</summary>
    private static HashSet<string> BuildExpectedFaceKeys(VoxelChunk chunk)
    {
        HashSet<string> keys = new HashSet<string>();

        int[][] offsets =
        {
            new int[] { 1, 0, 0 }, new int[] { -1, 0, 0 },
            new int[] { 0, 1, 0 }, new int[] { 0, -1, 0 },
            new int[] { 0, 0, 1 }, new int[] { 0, 0, -1 }
        };

        for (int x = 0; x < VoxelChunk.ChunkSize; x++)
        {
            for (int y = 0; y < VoxelChunk.ChunkSize; y++)
            {
                for (int z = 0; z < VoxelChunk.ChunkSize; z++)
                {
                    if (!chunk.GetVoxel(x, y, z).IsSolid()) continue;

                    for (int d = 0; d < offsets.Length; d++)
                    {
                        int nx = x + offsets[d][0];
                        int ny = y + offsets[d][1];
                        int nz = z + offsets[d][2];

                        bool neighbourSolid =
                            VoxelChunk.IsLocalCoordinateValid(nx, ny, nz) &&
                            chunk.GetVoxel(nx, ny, nz).IsSolid();

                        if (!neighbourSolid)
                        {
                            keys.Add(x + ":" + y + ":" + z + ":" + d);
                        }
                    }
                }
            }
        }

        return keys;
    }

    /// <summary>从 Mesh 反推实际面键（依据法线方向与面平面定位所属固体 Voxel）。</summary>
    private static HashSet<string> BuildActualFaceKeys(VoxelMeshData mesh)
    {
        HashSet<string> keys = new HashSet<string>();

        for (int quad = 0; quad * 4 + 3 < mesh.VertexCount; quad++)
        {
            int b = quad * 4;

            Vector3 n = mesh.Normals[b];
            int axis = DominantAxis(n);
            int sign = n[axis] > 0f ? 1 : -1;

            float plane = mesh.Vertices[b][axis];

            // 切向最小角 = 该 quad 的 P0
            Vector3 p0 = mesh.Vertices[b];

            int vx = Mathf.RoundToInt(p0.x);
            int vy = Mathf.RoundToInt(p0.y);
            int vz = Mathf.RoundToInt(p0.z);

            if (axis == 0) vx = Mathf.RoundToInt(sign > 0 ? plane - 1f : plane);
            else if (axis == 1) vy = Mathf.RoundToInt(sign > 0 ? plane - 1f : plane);
            else vz = Mathf.RoundToInt(sign > 0 ? plane - 1f : plane);

            int directionIndex = DirectionIndexFromAxisSign(axis, sign);
            keys.Add(vx + ":" + vy + ":" + vz + ":" + directionIndex);
        }

        return keys;
    }

    /// <summary>重复面（同一 voxel + 同一方向被生成多次）的数量。</summary>
    private static int CountDuplicateFaceKeys(VoxelMeshData mesh)
    {
        HashSet<string> seen = new HashSet<string>();
        int duplicates = 0;

        for (int quad = 0; quad * 4 + 3 < mesh.VertexCount; quad++)
        {
            int b = quad * 4;

            Vector3 n = mesh.Normals[b];
            int axis = DominantAxis(n);
            int sign = n[axis] > 0f ? 1 : -1;
            float plane = mesh.Vertices[b][axis];

            Vector3 p0 = mesh.Vertices[b];

            int vx = Mathf.RoundToInt(p0.x);
            int vy = Mathf.RoundToInt(p0.y);
            int vz = Mathf.RoundToInt(p0.z);

            if (axis == 0) vx = Mathf.RoundToInt(sign > 0 ? plane - 1f : plane);
            else if (axis == 1) vy = Mathf.RoundToInt(sign > 0 ? plane - 1f : plane);
            else vz = Mathf.RoundToInt(sign > 0 ? plane - 1f : plane);

            string key =
                vx + ":" + vy + ":" + vz + ":" + DirectionIndexFromAxisSign(axis, sign);

            if (!seen.Add(key)) duplicates++;
        }

        return duplicates;
    }

    private static int DominantAxis(Vector3 n)
    {
        float ax = Mathf.Abs(n.x);
        float ay = Mathf.Abs(n.y);
        float az = Mathf.Abs(n.z);

        if (ax >= ay && ax >= az) return 0;
        if (ay >= az) return 1;
        return 2;
    }

    /// <summary>与 VoxelMeshGenerator 的固定方向顺序一致：+X,−X,+Y,−Y,+Z,−Z。</summary>
    private static int DirectionIndexFromAxisSign(int axis, int sign)
    {
        if (axis == 0) return sign > 0 ? 0 : 1;
        if (axis == 1) return sign > 0 ? 2 : 3;
        return sign > 0 ? 4 : 5;
    }

    private static bool SetEquals(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count != b.Count) return false;

        foreach (string key in a)
        {
            if (!b.Contains(key)) return false;
        }

        return true;
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
