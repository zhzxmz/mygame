using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Voxel-aware Mesh Phase 1：Raw Base Mesh → Voxel Boundary Pre-cut →
/// ChunkGeometry → Runtime Mesh Assembly。
///
/// 数学规则来自已通过的 Prototype A：
///   - Voxel(c) = [c, c+1)（min-corner）
///   - Triangle 与 Voxel box clipping
///   - Bridge provenance（非最终重复 Edge）
///   - Chunk-local VertexPool + WeldingKey
///
/// 本文件不接入 Mining / Hidden Face / Collider 行为。
/// </summary>
public static class VoxelAwareMeshPrecut
{
    public const float GeometryTolerance = 1e-5f;
    public const float WeldSnapScale = 1e-5f;

    /// <summary>
    /// B3-E 测量/验证开关：true 时热循环回退到 FaceId.Key 字符串比较，
    /// 仅用于“优化前后”对比测量与等价性验证（默认 false = 逐字段优化路径）。
    /// 不参与几何语义，不改变 FaceId.Key 的对外行为。
    /// </summary>
    public static bool UseLegacyFaceKeyComparison;

    // ---- B3-D：Assemble 阶段计时（性能诊断，不参与几何判定） ----
    /// <summary>最近一次 Assemble 的顶点焊接耗时（毫秒，累加所有多边形）。</summary>
    public static double LastAssembleWeldMilliseconds { get; private set; }

    /// <summary>最近一次 Assemble 的多边形三角化耗时（毫秒）。</summary>
    public static double LastAssembleTriangulateMilliseconds { get; private set; }

    /// <summary>最近一次 Assemble 的 Mesh 顶点/索引上传耗时（毫秒）。</summary>
    public static double LastAssembleUploadMilliseconds { get; private set; }

    /// <summary>最近一次 Assemble 的法线/包围盒计算耗时（毫秒）。</summary>
    public static double LastAssembleNormalsMilliseconds { get; private set; }

    /// <summary>最近一次 Assemble 的顶点数 / 三角形数。</summary>
    public static int LastAssembleVertexCount { get; private set; }
    public static int LastAssembleTriangleCount { get; private set; }

    private static long assembleWeldTicks;
    private static long assembleTriangulateTicks;

    private static double TicksToMs(long ticks)
    {
        return ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
    }

    public sealed class ChunkGeometry
    {
        public Mesh RawMesh;
        public Vector3 ChunkOrigin;
        public float VoxelSize;
        public VertexPool Pool = new VertexPool();
        public List<Fragment> Fragments = new List<Fragment>();
        public List<FaceFragment> FaceFragments = new List<FaceFragment>();
        public List<BridgeRecord> Bridges = new List<BridgeRecord>();
        public List<VoxelAwareMeshSpatialFace.SpatialFaceRecord> SpatialFaces =
            new List<VoxelAwareMeshSpatialFace.SpatialFaceRecord>();
        public Dictionary<string, List<VoxelAwareMeshSpatialFace.HiddenFaceFragment>>
            HiddenFaceContributions =
                new Dictionary<string, List<VoxelAwareMeshSpatialFace.HiddenFaceFragment>>();
        public Dictionary<int, bool> FaceFragmentVisibility =
            new Dictionary<int, bool>();
        public Vector3Int ChunkCoordinate;
        public Vector3Int ChunkVoxelSize;
        public MaterialField BaseMaterialField;
        public Dictionary<Vector3Int, List<int>> VoxelFragmentIndex =
            new Dictionary<Vector3Int, List<int>>();
        public float FragmentAreaTotal;
        public float FaceAreaTotal;

        public bool HasGeometry
        {
            get
            {
                return Fragments.Count > 0 || FaceFragments.Count > 0;
            }
        }
    }

    public sealed class VertexPool
    {
        public List<Vector3> Positions = new List<Vector3>();
        private readonly Dictionary<string, int> ids =
            new Dictionary<string, int>();

        public int GetOrAdd(Vector3 position)
        {
            string key = MakeKey(position);
            if (ids.TryGetValue(key, out int id)) return id;

            id = Positions.Count;
            Positions.Add(position);
            ids.Add(key, id);
            return id;
        }

        public static string MakeKey(Vector3 position)
        {
            int qx = Mathf.RoundToInt(position.x / WeldSnapScale);
            int qy = Mathf.RoundToInt(position.y / WeldSnapScale);
            int qz = Mathf.RoundToInt(position.z / WeldSnapScale);
            return qx + ":" + qy + ":" + qz;
        }
    }

    public sealed class Fragment
    {
        public Vector3Int Voxel;
        public List<Vector3> Polygon = new List<Vector3>();
        public List<int> VertexIds = new List<int>();
        public List<bool> Original = new List<bool>();
        public int Submesh;
        public int SourceTriangle;
        public float Area;
        public bool Active = true;
    }

    public sealed class FaceFragment
    {
        public FaceId Face;
        public List<Vector3> Polygon = new List<Vector3>();
        public List<int> VertexIds = new List<int>();
        public int Submesh;
        public int SourceTriangle;
        public float Area;
        public bool Active = true;
    }

    public struct FaceId
    {
        public int Axis;
        public int Plane;
        public int U;
        public int V;

        public FaceId(int axis, int plane, int u, int v)
        {
            Axis = axis;
            Plane = plane;
            U = u;
            V = v;
        }

        public string Key
        {
            get { return Axis + ":" + Plane + ":" + U + ":" + V; }
        }

        /// <summary>
        /// 无分配的面标识相等判定（逐字段比较）。
        /// Key 是计算属性：每次访问都会新建字符串；在遍历数万 Bridge /
        /// FaceFragment 的热循环中必须使用本方法。
        /// 语义与 Key 字符串比较完全等价（Key 由 4 个 int 段以 ':' 拼接）。
        /// </summary>
        public bool SameAs(FaceId other)
        {
            return Axis == other.Axis && Plane == other.Plane &&
                   U == other.U && V == other.V;
        }
    }

    public sealed class BridgeRecord
    {
        public FaceId Face;
        public int VertexA;
        public int VertexB;
        public int FragmentA;
        public int FragmentB;
        public bool HasCutVertex;
    }

    private sealed class Poly3
    {
        public List<Vector3> Points = new List<Vector3>();
        public List<bool> Original = new List<bool>();

        public Poly3 Clone()
        {
            Poly3 clone = new Poly3();
            clone.Points.AddRange(Points);
            clone.Original.AddRange(Original);
            return clone;
        }
    }

    private struct SourceTriangle
    {
        public Vector3 A;
        public Vector3 B;
        public Vector3 C;
        public int Submesh;
        public int SourceIndex;
    }

    /// <summary>
    /// 对 Chunk-local Raw Base Mesh 执行一次 Voxel Boundary Pre-cut。
    /// rawMesh 不被修改。
    /// </summary>
    public static ChunkGeometry Build(
        Mesh rawMesh,
        Vector3 chunkOrigin,
        float voxelSize,
        Vector3Int chunkCoordinate,
        Vector3Int chunkVoxelSize)
    {
        if (rawMesh == null || rawMesh.vertexCount == 0) return null;
        if (voxelSize <= 0f) return null;

        ChunkGeometry geometry = new ChunkGeometry
        {
            RawMesh = rawMesh,
            ChunkOrigin = chunkOrigin,
            VoxelSize = voxelSize,
            ChunkCoordinate = chunkCoordinate,
            ChunkVoxelSize = chunkVoxelSize
        };

        Vector3[] rawVertices = rawMesh.vertices;
        int submeshCount = Mathf.Max(1, rawMesh.subMeshCount);

        for (int submesh = 0; submesh < submeshCount; submesh++)
        {
            int[] triangles = rawMesh.GetTriangles(submesh);
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                SourceTriangle source = new SourceTriangle
                {
                    A = rawVertices[triangles[i]],
                    B = rawVertices[triangles[i + 1]],
                    C = rawVertices[triangles[i + 2]],
                    Submesh = submesh,
                    SourceIndex = i / 3
                };

                CutTriangle(source, geometry);
            }
        }

        BuildBridgeRecords(geometry);
        BuildVoxelIndex(geometry);
        return geometry;
    }

    private static void CutTriangle(
        SourceTriangle triangle,
        ChunkGeometry geometry)
    {
        float area = TriangleArea3(triangle.A, triangle.B, triangle.C);
        if (area <= GeometryTolerance) return;

        for (int axis = 0; axis < 3; axis++)
        {
            if (IsCoplanarWithVoxelPlane(
                triangle,
                geometry,
                axis,
                out int plane))
            {
                CutCoplanarTriangle(
                    triangle,
                    geometry,
                    axis,
                    plane
                );
                return;
            }
        }

        CutOrdinaryTriangle(triangle, geometry);
    }

    private static void CutOrdinaryTriangle(
        SourceTriangle triangle,
        ChunkGeometry geometry)
    {
        Poly3 source = new Poly3();
        source.Points.Add(triangle.A);
        source.Points.Add(triangle.B);
        source.Points.Add(triangle.C);
        source.Original.Add(true);
        source.Original.Add(true);
        source.Original.Add(true);

        Vector3 min = Vector3.Min(
            Vector3.Min(triangle.A, triangle.B),
            triangle.C
        );
        Vector3 max = Vector3.Max(
            Vector3.Max(triangle.A, triangle.B),
            triangle.C
        );

        Vector3 globalMin = min + geometry.ChunkOrigin;
        Vector3 globalMax = max + geometry.ChunkOrigin;

        Vector3Int minVoxel = new Vector3Int(
            Mathf.FloorToInt(
                globalMin.x / geometry.VoxelSize + GeometryTolerance),
            Mathf.FloorToInt(
                globalMin.y / geometry.VoxelSize + GeometryTolerance),
            Mathf.FloorToInt(
                globalMin.z / geometry.VoxelSize + GeometryTolerance)
        );

        Vector3Int maxVoxel = new Vector3Int(
            Mathf.FloorToInt(
                globalMax.x / geometry.VoxelSize - GeometryTolerance),
            Mathf.FloorToInt(
                globalMax.y / geometry.VoxelSize - GeometryTolerance),
            Mathf.FloorToInt(
                globalMax.z / geometry.VoxelSize - GeometryTolerance)
        );

        if (maxVoxel.x < minVoxel.x ||
            maxVoxel.y < minVoxel.y ||
            maxVoxel.z < minVoxel.z)
        {
            return;
        }

        for (int x = minVoxel.x; x <= maxVoxel.x; x++)
        {
            for (int y = minVoxel.y; y <= maxVoxel.y; y++)
            {
                for (int z = minVoxel.z; z <= maxVoxel.z; z++)
                {
                    Vector3Int voxel = new Vector3Int(x, y, z);

                    Vector3 boxMin = new Vector3(
                        x * geometry.VoxelSize - geometry.ChunkOrigin.x,
                        y * geometry.VoxelSize - geometry.ChunkOrigin.y,
                        z * geometry.VoxelSize - geometry.ChunkOrigin.z
                    );

                    Vector3 boxMax = boxMin +
                                     Vector3.one * geometry.VoxelSize;

                    Poly3 clipped = ClipPolyToBox(source, boxMin, boxMax);
                    if (clipped == null) continue;

                    float clippedArea = PolygonArea3(clipped.Points);
                    if (clippedArea <= GeometryTolerance) continue;

                    Fragment fragment = new Fragment
                    {
                        Voxel = voxel,
                        Submesh = triangle.Submesh,
                        SourceTriangle = triangle.SourceIndex,
                        Area = clippedArea
                    };

                    fragment.Polygon.AddRange(clipped.Points);
                    fragment.Original.AddRange(clipped.Original);

                    for (int i = 0; i < clipped.Points.Count; i++)
                    {
                        fragment.VertexIds.Add(
                            geometry.Pool.GetOrAdd(clipped.Points[i])
                        );
                    }

                    geometry.Fragments.Add(fragment);
                    geometry.FragmentAreaTotal += clippedArea;
                }
            }
        }
    }

    private static void CutCoplanarTriangle(
        SourceTriangle triangle,
        ChunkGeometry geometry,
        int axis,
        int plane)
    {
        int uAxis = (axis + 1) % 3;
        int vAxis = (axis + 2) % 3;

        Vector2 p0 = new Vector2(
            Component(triangle.A, uAxis),
            Component(triangle.A, vAxis)
        );
        Vector2 p1 = new Vector2(
            Component(triangle.B, uAxis),
            Component(triangle.B, vAxis)
        );
        Vector2 p2 = new Vector2(
            Component(triangle.C, uAxis),
            Component(triangle.C, vAxis)
        );

        Vector2 min2 = Vector2.Min(Vector2.Min(p0, p1), p2);
        Vector2 max2 = Vector2.Max(Vector2.Max(p0, p1), p2);

        int minU = Mathf.FloorToInt(min2.x + GeometryTolerance);
        int minV = Mathf.FloorToInt(min2.y + GeometryTolerance);
        int maxU = Mathf.FloorToInt(max2.x - GeometryTolerance);
        int maxV = Mathf.FloorToInt(max2.y - GeometryTolerance);

        if (maxU < minU || maxV < minV) return;

        List<Vector2> source2 = new List<Vector2> { p0, p1, p2 };
        if (PolygonArea2(source2) <= GeometryTolerance) return;

        for (int u = minU; u <= maxU; u++)
        {
            for (int v = minV; v <= maxV; v++)
            {
                List<Vector2> clipped2 = ClipPoly2ToBox(
                    source2,
                    new Vector2(u, v),
                    new Vector2(u + 1f, v + 1f)
                );

                if (clipped2 == null) continue;

                float clippedArea = PolygonArea2(clipped2);
                if (clippedArea <= GeometryTolerance) continue;

                // B3-E3：FaceId 统一契约为**绝对 Voxel 坐标**
                //   Plane = 绝对平面索引；U/V = 绝对 Voxel 单元索引。
                // 本函数的循环 u/v 是 Chunk Local 单元索引（p0/p1/p2 来自 Chunk Local
                // 三角形坐标），必须在此处（唯一的创建边界）补上 Chunk Origin，
                // 供 ToFaceLocal / ToChunkLocal / TryGetFaceBetweenVoxels /
                // GetNegativeVoxel / 运行时 Face 匹配统一使用；不得在消费者侧补偿。
                int uAbsolute = u + Mathf.RoundToInt(
                    Component(geometry.ChunkOrigin, uAxis) / geometry.VoxelSize);
                int vAbsolute = v + Mathf.RoundToInt(
                    Component(geometry.ChunkOrigin, vAxis) / geometry.VoxelSize);

                FaceFragment fragment = new FaceFragment
                {
                    Face = new FaceId(axis, plane, uAbsolute, vAbsolute),
                    Submesh = triangle.Submesh,
                    SourceTriangle = triangle.SourceIndex,
                    Area = clippedArea
                };

                for (int i = 0; i < clipped2.Count; i++)
                {
                    Vector3 point = Vector3.zero;

                    // Face plane position in Chunk Local Space:
                    // global plane = plane * voxelSize
                    // local = global - chunkOrigin
                    float planeLocal =
                        plane * geometry.VoxelSize -
                        Component(geometry.ChunkOrigin, axis);

                    SetComponent(ref point, axis, planeLocal);
                    SetComponent(ref point, uAxis, clipped2[i].x);
                    SetComponent(ref point, vAxis, clipped2[i].y);

                    fragment.Polygon.Add(point);
                    fragment.VertexIds.Add(
                        geometry.Pool.GetOrAdd(point)
                    );
                }

                geometry.FaceFragments.Add(fragment);
                geometry.FaceAreaTotal += clippedArea;
            }
        }
    }

    private static bool IsCoplanarWithVoxelPlane(
        SourceTriangle triangle,
        ChunkGeometry geometry,
        int axis,
        out int plane)
    {
        float a = Component(triangle.A, axis) + Component(geometry.ChunkOrigin, axis);
        float b = Component(triangle.B, axis) + Component(geometry.ChunkOrigin, axis);
        float c = Component(triangle.C, axis) + Component(geometry.ChunkOrigin, axis);

        float global = a / geometry.VoxelSize;
        plane = Mathf.RoundToInt(global);

        return Mathf.Abs(global - plane) <= GeometryTolerance &&
               Mathf.Abs(b / geometry.VoxelSize - plane) <= GeometryTolerance &&
               Mathf.Abs(c / geometry.VoxelSize - plane) <= GeometryTolerance;
    }

    private static void BuildBridgeRecords(ChunkGeometry geometry)
    {
        Dictionary<string, List<BridgeCandidate>> candidates =
            new Dictionary<string, List<BridgeCandidate>>();

        for (int f = 0; f < geometry.Fragments.Count; f++)
        {
            Fragment fragment = geometry.Fragments[f];
            int count = fragment.Polygon.Count;

            for (int i = 0; i < count; i++)
            {
                int j = (i + 1) % count;
                Vector3 p0 = fragment.Polygon[i];
                Vector3 p1 = fragment.Polygon[j];

                if (!TryGetVoxelPlane(
                    p0,
                    p1,
                    geometry,
                    out int axis,
                    out int plane))
                {
                    continue;
                }

                int id0 = fragment.VertexIds[i];
                int id1 = fragment.VertexIds[j];
                int minId = Mathf.Min(id0, id1);
                int maxId = Mathf.Max(id0, id1);
                string key = axis + ":" + plane + ":" + minId + ":" + maxId;

                BridgeCandidate candidate = new BridgeCandidate
                {
                    FragmentIndex = f,
                    Voxel = fragment.Voxel,
                    VertexA = id0,
                    VertexB = id1,
                    HasCutVertex =
                        !fragment.Original[i] || !fragment.Original[j]
                };

                if (!candidates.TryGetValue(key, out List<BridgeCandidate> list))
                {
                    list = new List<BridgeCandidate>();
                    candidates.Add(key, list);
                }

                list.Add(candidate);
            }
        }

        foreach (KeyValuePair<string, List<BridgeCandidate>> pair in candidates)
        {
            List<BridgeCandidate> list = pair.Value;
            int axis = ParseKeyPart(pair.Key, 0);
            int plane = ParseKeyPart(pair.Key, 1);

            for (int i = 0; i < list.Count; i++)
            {
                for (int j = i + 1; j < list.Count; j++)
                {
                    BridgeCandidate a = list[i];
                    BridgeCandidate b = list[j];

                    Vector3Int delta = a.Voxel - b.Voxel;
                    if (Mathf.Abs(delta[axis]) != 1) continue;
                    if (delta[(axis + 1) % 3] != 0 ||
                        delta[(axis + 2) % 3] != 0) continue;

                    Vector3Int lower = a.Voxel;
                    Vector3Int upper = b.Voxel;
                    if (upper[axis] < lower[axis])
                    {
                        lower = b.Voxel;
                        upper = a.Voxel;
                    }

                    if (plane != upper[axis]) continue;

                    geometry.Bridges.Add(new BridgeRecord
                    {
                        Face = new FaceId(
                            axis,
                            plane,
                            lower[(axis + 1) % 3],
                            lower[(axis + 2) % 3]
                        ),
                        VertexA = Mathf.Min(a.VertexA, a.VertexB),
                        VertexB = Mathf.Max(a.VertexA, a.VertexB),
                        FragmentA = a.FragmentIndex,
                        FragmentB = b.FragmentIndex,
                        HasCutVertex = a.HasCutVertex || b.HasCutVertex
                    });
                }
            }
        }
    }

    private sealed class BridgeCandidate
    {
        public int FragmentIndex;
        public Vector3Int Voxel;
        public int VertexA;
        public int VertexB;
        public bool HasCutVertex;
    }

    private static bool TryGetVoxelPlane(
        Vector3 p0,
        Vector3 p1,
        ChunkGeometry geometry,
        out int axis,
        out int plane)
    {
        axis = -1;
        plane = 0;

        for (int a = 0; a < 3; a++)
        {
            float global0 =
                (Component(p0, a) + Component(geometry.ChunkOrigin, a)) /
                geometry.VoxelSize;
            float global1 =
                (Component(p1, a) + Component(geometry.ChunkOrigin, a)) /
                geometry.VoxelSize;

            if (Mathf.Abs(global0 - global1) > GeometryTolerance) continue;

            int rounded = Mathf.RoundToInt(global0);
            if (Mathf.Abs(global0 - rounded) > GeometryTolerance) continue;

            axis = a;
            plane = rounded;
            return true;
        }

        return false;
    }

    private static void BuildVoxelIndex(ChunkGeometry geometry)
    {
        for (int i = 0; i < geometry.Fragments.Count; i++)
        {
            Fragment fragment = geometry.Fragments[i];
            if (!geometry.VoxelFragmentIndex.TryGetValue(
                fragment.Voxel,
                out List<int> list))
            {
                list = new List<int>();
                geometry.VoxelFragmentIndex.Add(fragment.Voxel, list);
            }

            list.Add(i);
        }
    }

    /// <summary>将 ChunkGeometry 的 Active Fragments 组装为 Chunk-local Unity Mesh。</summary>
    public static Mesh Assemble(ChunkGeometry geometry)
    {
        if (geometry == null) return null;

        assembleWeldTicks = 0;
        assembleTriangulateTicks = 0;

        List<Vector3> meshVertices = new List<Vector3>();
        Dictionary<string, int> weldingMap = new Dictionary<string, int>();
        List<int>[] submeshes = new List<int>[3];
        for (int i = 0; i < submeshes.Length; i++)
        {
            submeshes[i] = new List<int>();
        }

        for (int f = 0; f < geometry.Fragments.Count; f++)
        {
            Fragment fragment = geometry.Fragments[f];
            if (!fragment.Active) continue;
            AddPolygonToMesh(
                fragment.Polygon,
                fragment.Submesh,
                meshVertices,
                weldingMap,
                submeshes
            );
        }

        for (int f = 0; f < geometry.FaceFragments.Count; f++)
        {
            FaceFragment fragment = geometry.FaceFragments[f];
            if (!fragment.Active) continue;

            if (geometry.FaceFragmentVisibility.TryGetValue(f, out bool visible) &&
                !visible)
            {
                continue;
            }

            AddPolygonToMesh(
                fragment.Polygon,
                fragment.Submesh,
                meshVertices,
                weldingMap,
                submeshes
            );
        }

        for (int f = 0; f < geometry.SpatialFaces.Count; f++)
        {
            VoxelAwareMeshSpatialFace.SpatialFaceRecord record =
                geometry.SpatialFaces[f];
            if (record == null) continue;

            for (int h = 0; h < record.HiddenFaces.Count; h++)
            {
                VoxelAwareMeshSpatialFace.HiddenFaceFragment hidden =
                    record.HiddenFaces[h];
                if (hidden == null || !hidden.Active) continue;

                AddPolygonToMesh(
                    hidden.Polygon,
                    hidden.Submesh,
                    meshVertices,
                    weldingMap,
                    submeshes
                );
            }
        }

        foreach (KeyValuePair<string, List<VoxelAwareMeshSpatialFace.HiddenFaceFragment>>
            pair in geometry.HiddenFaceContributions)
        {
            List<VoxelAwareMeshSpatialFace.HiddenFaceFragment> contributions =
                pair.Value;
            if (contributions == null) continue;

            for (int h = 0; h < contributions.Count; h++)
            {
                VoxelAwareMeshSpatialFace.HiddenFaceFragment hidden =
                    contributions[h];
                if (hidden == null || !hidden.Active) continue;

                AddPolygonToMesh(
                    hidden.Polygon,
                    hidden.Submesh,
                    meshVertices,
                    weldingMap,
                    submeshes
                );
            }
        }

        // =================================================================
        // TEMP DIAGNOSTIC（缺面调查：只统计 FaceKey=2:50:26:16 是否进入最终 Mesh）
        // 只读重扫，不改变任何装配逻辑；调查结束后删除本块。
        // =================================================================
        {
            const string tempKey = "2:50:26:16";

            int faceFragmentTotal = 0;
            int faceFragmentInactive = 0;
            int faceFragmentHiddenByOverride = 0;
            int faceFragmentEmitted = 0;

            for (int i = 0; i < geometry.FaceFragments.Count; i++)
            {
                FaceFragment fragment = geometry.FaceFragments[i];
                if (fragment == null || fragment.Face.Key != tempKey) continue;

                faceFragmentTotal++;

                if (!fragment.Active)
                {
                    faceFragmentInactive++;
                    continue;
                }

                bool visible;
                if (geometry.FaceFragmentVisibility.TryGetValue(i, out visible) &&
                    !visible)
                {
                    faceFragmentHiddenByOverride++;
                    continue;
                }

                faceFragmentEmitted++;
            }

            List<VoxelAwareMeshSpatialFace.HiddenFaceFragment> contributions = null;
            bool hasContribution = geometry.HiddenFaceContributions
                .TryGetValue(tempKey, out contributions);

            int contributionTotal = 0;
            int contributionActive = 0;

            if (hasContribution && contributions != null)
            {
                contributionTotal = contributions.Count;

                for (int i = 0; i < contributions.Count; i++)
                {
                    if (contributions[i] != null && contributions[i].Active)
                    {
                        contributionActive++;
                    }
                }
            }

            if (faceFragmentTotal > 0 || contributionTotal > 0)
            {
                Debug.Log(
                    "[FaceTrace ASSEMBLE] chunk=" + geometry.ChunkCoordinate +
                    " key=" + tempKey +
                    " faceFragments(total/inactive/hiddenByOverride/emitted)=" +
                    faceFragmentTotal + "/" + faceFragmentInactive + "/" +
                    faceFragmentHiddenByOverride + "/" + faceFragmentEmitted +
                    " contribution(stored/total/active)=" + hasContribution +
                    "/" + contributionTotal + "/" + contributionActive +
                    " meshTriangles(submesh0/1/2)=" +
                    (submeshes[0].Count / 3) + "/" +
                    (submeshes[1].Count / 3) + "/" +
                    (submeshes[2].Count / 3)
                );
            }
        }
        // =================== TEMP DIAGNOSTIC END ===================

        long uploadStart = System.Diagnostics.Stopwatch.GetTimestamp();
        Mesh mesh = new Mesh();
        mesh.name = "VoxelAwareMesh";
        mesh.hideFlags = HideFlags.DontSave;
        mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(meshVertices);
        mesh.subMeshCount = submeshes.Length;
        for (int i = 0; i < submeshes.Length; i++)
        {
            mesh.SetTriangles(submeshes[i], i);
        }

        long uploadEnd = System.Diagnostics.Stopwatch.GetTimestamp();

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        long normalsEnd = System.Diagnostics.Stopwatch.GetTimestamp();

        LastAssembleWeldMilliseconds = TicksToMs(assembleWeldTicks);
        LastAssembleTriangulateMilliseconds = TicksToMs(assembleTriangulateTicks);
        LastAssembleUploadMilliseconds = TicksToMs(uploadEnd - uploadStart);
        LastAssembleNormalsMilliseconds = TicksToMs(normalsEnd - uploadEnd);
        LastAssembleVertexCount = meshVertices.Count;

        int triangleCount = 0;
        for (int i = 0; i < submeshes.Length; i++)
        {
            triangleCount += submeshes[i].Count / 3;
        }

        LastAssembleTriangleCount = triangleCount;

        return mesh;
    }

    private static void AddPolygonToMesh(
        List<Vector3> polygon,
        int submesh,
        List<Vector3> meshVertices,
        Dictionary<string, int> weldingMap,
        List<int>[] submeshes)
    {
        if (polygon == null || polygon.Count < 3) return;

        int submeshIndex = Mathf.Clamp(submesh, 0, submeshes.Length - 1);

        long weldStart = System.Diagnostics.Stopwatch.GetTimestamp();
        List<int> meshIds = new List<int>(polygon.Count);
        for (int i = 0; i < polygon.Count; i++)
        {
            Vector3 point = polygon[i];
            string key = VertexPool.MakeKey(point);

            if (!weldingMap.TryGetValue(key, out int meshId))
            {
                meshId = meshVertices.Count;
                meshVertices.Add(point);
                weldingMap.Add(key, meshId);
            }

            meshIds.Add(meshId);
        }

        assembleWeldTicks +=
            System.Diagnostics.Stopwatch.GetTimestamp() - weldStart;
        long triangulateStart = System.Diagnostics.Stopwatch.GetTimestamp();

        for (int i = 1; i < meshIds.Count - 1; i++)
        {
            int idA = meshIds[0];
            int idB = meshIds[i];
            int idC = meshIds[i + 1];

            // Pre-cut 的裁剪结果偶尔包含重复顶点或极细长（sliver）面片，
            // fan triangulation 会因此产生零面积 / 退化三角形。
            // 只为 Final Mesh 过滤退化三角形，不修改 Base / Fragment 数据。
            // 阈值 1e-8 m^2（0.01 mm^2）远小于任何有效地形面片。
            if (idB == idA || idC == idA || idC == idB) continue;

            if (TriangleArea3(
                meshVertices[idA],
                meshVertices[idB],
                meshVertices[idC]) <= 1e-8f) continue;

            submeshes[submeshIndex].Add(idA);
            submeshes[submeshIndex].Add(idB);
            submeshes[submeshIndex].Add(idC);
        }

        assembleTriangulateTicks +=
            System.Diagnostics.Stopwatch.GetTimestamp() - triangulateStart;
    }

    private static Poly3 ClipPolyToBox(
        Poly3 source,
        Vector3 min,
        Vector3 max)
    {
        Poly3 poly = source.Clone();

        poly = ClipHalfSpace(poly, 0, min.x, true);
        if (poly == null) return null;
        poly = ClipHalfSpace(poly, 0, max.x, false);
        if (poly == null) return null;
        poly = ClipHalfSpace(poly, 1, min.y, true);
        if (poly == null) return null;
        poly = ClipHalfSpace(poly, 1, max.y, false);
        if (poly == null) return null;
        poly = ClipHalfSpace(poly, 2, min.z, true);
        if (poly == null) return null;
        poly = ClipHalfSpace(poly, 2, max.z, false);
        if (poly == null) return null;

        if (poly.Points.Count < 3) return null;
        if (PolygonArea3(poly.Points) <= GeometryTolerance) return null;

        return poly;
    }

    private static Poly3 ClipHalfSpace(
        Poly3 source,
        int axis,
        float plane,
        bool keepGreater)
    {
        Poly3 output = new Poly3();

        for (int i = 0; i < source.Points.Count; i++)
        {
            Vector3 a = source.Points[i];
            Vector3 b = source.Points[(i + 1) % source.Points.Count];

            bool inA = keepGreater
                ? Component(a, axis) >= plane - GeometryTolerance
                : Component(a, axis) <= plane + GeometryTolerance;
            bool inB = keepGreater
                ? Component(b, axis) >= plane - GeometryTolerance
                : Component(b, axis) <= plane + GeometryTolerance;

            if (inA)
            {
                output.Points.Add(a);
                output.Original.Add(source.Original[i]);
            }

            if (inA != inB)
            {
                float denom = Component(b, axis) - Component(a, axis);
                float t = Mathf.Abs(denom) > GeometryTolerance
                    ? (plane - Component(a, axis)) / denom
                    : 0.5f;

                output.Points.Add(Vector3.Lerp(a, b, Mathf.Clamp01(t)));
                output.Original.Add(false);
            }
        }

        if (output.Points.Count < 3) return null;
        return output;
    }

    private static List<Vector2> ClipPoly2ToBox(
        List<Vector2> source,
        Vector2 min,
        Vector2 max)
    {
        List<Vector2> poly = new List<Vector2>(source);

        poly = ClipPoly2HalfSpace(poly, 0, min.x, true);
        if (poly == null) return null;
        poly = ClipPoly2HalfSpace(poly, 0, max.x, false);
        if (poly == null) return null;
        poly = ClipPoly2HalfSpace(poly, 1, min.y, true);
        if (poly == null) return null;
        poly = ClipPoly2HalfSpace(poly, 1, max.y, false);
        if (poly == null) return null;

        if (poly.Count < 3) return null;
        if (PolygonArea2(poly) <= GeometryTolerance) return null;

        return poly;
    }

    private static List<Vector2> ClipPoly2HalfSpace(
        List<Vector2> source,
        int axis,
        float plane,
        bool keepGreater)
    {
        List<Vector2> output = new List<Vector2>();

        for (int i = 0; i < source.Count; i++)
        {
            Vector2 a = source[i];
            Vector2 b = source[(i + 1) % source.Count];

            bool inA = keepGreater
                ? a[axis] >= plane - GeometryTolerance
                : a[axis] <= plane + GeometryTolerance;
            bool inB = keepGreater
                ? b[axis] >= plane - GeometryTolerance
                : b[axis] <= plane + GeometryTolerance;

            if (inA) output.Add(a);

            if (inA != inB)
            {
                float denom = b[axis] - a[axis];
                float t = Mathf.Abs(denom) > GeometryTolerance
                    ? (plane - a[axis]) / denom
                    : 0.5f;

                output.Add(Vector2.Lerp(a, b, Mathf.Clamp01(t)));
            }
        }

        if (output.Count < 3) return null;
        return output;
    }

    private static int ParseKeyPart(string key, int part)
    {
        string[] pieces = key.Split(':');
        return int.Parse(pieces[part]);
    }

    private static float TriangleArea3(Vector3 a, Vector3 b, Vector3 c)
    {
        return Vector3.Cross(b - a, c - a).magnitude * 0.5f;
    }

    private static float PolygonArea3(List<Vector3> points)
    {
        if (points.Count < 3) return 0f;

        Vector3 sum = Vector3.zero;
        for (int i = 1; i < points.Count - 1; i++)
        {
            sum += Vector3.Cross(
                points[i] - points[0],
                points[i + 1] - points[0]
            );
        }

        return sum.magnitude * 0.5f;
    }

    private static float PolygonArea2(List<Vector2> points)
    {
        if (points.Count < 3) return 0f;

        float area = 0f;
        for (int i = 0; i < points.Count; i++)
        {
            Vector2 a = points[i];
            Vector2 b = points[(i + 1) % points.Count];
            area += a.x * b.y - b.x * a.y;
        }

        return Mathf.Abs(area) * 0.5f;
    }

    private static float Component(Vector3 value, int axis)
    {
        return axis == 0 ? value.x : axis == 1 ? value.y : value.z;
    }

    private static void SetComponent(ref Vector3 value, int axis, float component)
    {
        if (axis == 0) value.x = component;
        else if (axis == 1) value.y = component;
        else value.z = component;
    }
}
