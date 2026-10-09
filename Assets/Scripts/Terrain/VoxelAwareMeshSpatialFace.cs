using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Voxel-aware Mesh Runtime Phase 2A：
///   SpatialFace identity / Planar Boundary Graph / Region Partition /
///   Hidden Face evaluation + material rule。
///
/// 纯 C# runtime data，不创建 GameObject，不接入 Mining。
/// </summary>
public static class VoxelAwareMeshSpatialFace
{
    public const float Tolerance = 1e-5f;

    public sealed class FaceSegment
    {
        public Vector2 A;
        public Vector2 B;
        public bool IsBridge;
        public bool IsNatural;
        public bool IsCoplanar;

        public FaceSegment(Vector2 a, Vector2 b)
        {
            A = a;
            B = b;
        }
    }

    /// <summary>
    /// Spec 13.10 Region Evaluation Result。
    /// Domain Gap / Evaluation Failure 不能被折叠成 NoHiddenFace，
    /// 必须作为独立状态传播到 Mutation，阻止错误 Mesh 继续 Assemble。
    /// </summary>
    public enum RegionDecision
    {
        NoHiddenFace,
        HiddenFace,
        EvaluationFailed
    }

    public sealed class FaceRegion
    {
        public List<Vector2> Polygon = new List<Vector2>();
        public float Area;
        public Vector2 Midpoint;
        public bool GeneratesHiddenFace;
        public RegionDecision Decision = RegionDecision.NoHiddenFace;
    }

    public sealed class HiddenFaceFragment
    {
        public VoxelAwareMeshPrecut.FaceId Face;
        public List<Vector3> Polygon = new List<Vector3>();
        public List<int> VertexIds = new List<int>();
        public int Submesh;
        public float Area;
        public bool Active = true;
    }

    public sealed class SpatialFaceRecord
    {
        public VoxelAwareMeshPrecut.FaceId Face;
        public Vector3Int NegativeVoxel;
        public Vector3Int PositiveVoxel;
        public bool HasPositiveVoxel;
        public Vector3Int OwnerChunkCoordinate;
        public bool NegativeAir;
        public bool PositiveAir;
        public bool Evaluated;

        /// <summary>该 SpatialFace 的整体 Evaluation 结果。</summary>
        public RegionDecision Decision = RegionDecision.NoHiddenFace;

        /// <summary>Evaluation Failure（例如 Domain Gap）时为 true。</summary>
        public bool EvaluationFailed;

        /// <summary>Evaluation Failure 的具体原因（Diagnostics）。</summary>
        public string EvaluationFailureMessage = string.Empty;

        public List<FaceSegment> Segments = new List<FaceSegment>();
        public List<FaceRegion> Regions = new List<FaceRegion>();
        public List<HiddenFaceFragment> HiddenFaces =
            new List<HiddenFaceFragment>();
    }

    // =====================================================================
    // Face Identity
    // =====================================================================

    public static bool TryGetFaceBetweenVoxels(
        Vector3Int a,
        Vector3Int b,
        out VoxelAwareMeshPrecut.FaceId face)
    {
        face = default(VoxelAwareMeshPrecut.FaceId);

        Vector3Int delta = b - a;
        int axis = -1;
        if (delta.x != 0 && delta.y == 0 && delta.z == 0) axis = 0;
        else if (delta.x == 0 && delta.y != 0 && delta.z == 0) axis = 1;
        else if (delta.x == 0 && delta.y == 0 && delta.z != 0) axis = 2;

        if (axis < 0) return false;
        if (Mathf.Abs(axis == 0 ? delta.x : axis == 1 ? delta.y : delta.z) != 1)
        {
            return false;
        }

        Vector3Int lower = a;
        Vector3Int upper = b;
        if (Component(upper, axis) < Component(lower, axis))
        {
            lower = b;
            upper = a;
        }

        int plane = Component(upper, axis);
        int u = Component(lower, (axis + 1) % 3);
        int v = Component(lower, (axis + 2) % 3);
        face = new VoxelAwareMeshPrecut.FaceId(axis, plane, u, v);
        return true;
    }

    public static Vector3Int GetNegativeVoxel(VoxelAwareMeshPrecut.FaceId face)
    {
        Vector3Int voxel = Vector3Int.zero;
        SetComponent(ref voxel, face.Axis, face.Plane - 1);
        SetComponent(ref voxel, (face.Axis + 1) % 3, face.U);
        SetComponent(ref voxel, (face.Axis + 2) % 3, face.V);
        return voxel;
    }

    public static Vector3Int GetPositiveVoxel(VoxelAwareMeshPrecut.FaceId face)
    {
        Vector3Int voxel = Vector3Int.zero;
        SetComponent(ref voxel, face.Axis, face.Plane);
        SetComponent(ref voxel, (face.Axis + 1) % 3, face.U);
        SetComponent(ref voxel, (face.Axis + 2) % 3, face.V);
        return voxel;
    }

    public static Vector3Int GetCanonicalOwnerChunk(
        VoxelAwareMeshPrecut.FaceId face,
        Vector3Int chunkVoxelSize)
    {
        Vector3Int negative = GetNegativeVoxel(face);
        return ChunkSpace.VoxelToChunk(negative, chunkVoxelSize);
    }

    // =====================================================================
    // Material
    // =====================================================================

    public static MaterialType GetVoxelMainMaterial(
        MaterialField materialField,
        Vector3Int voxel,
        float voxelSize)
    {
        if (materialField == null) return MaterialType.Stone;

        Vector3 center = new Vector3(
            (voxel.x + 0.5f) * voxelSize,
            (voxel.y + 0.5f) * voxelSize,
            (voxel.z + 0.5f) * voxelSize
        );

        int x = Mathf.Clamp(
            Mathf.RoundToInt((center.x - materialField.Origin.x) /
                             materialField.CellSizeX),
            0,
            materialField.SampleCountX - 1
        );
        int y = Mathf.Clamp(
            Mathf.RoundToInt((center.y - materialField.Origin.y) /
                             materialField.CellSizeY),
            0,
            materialField.SampleCountY - 1
        );
        int z = Mathf.Clamp(
            Mathf.RoundToInt((center.z - materialField.Origin.z) /
                             materialField.CellSizeZ),
            0,
            materialField.SampleCountZ - 1
        );

        return materialField[x, y, z];
    }

    public static int MaterialTypeToSubmesh(MaterialType material)
    {
        switch (material)
        {
            case MaterialType.Grass: return 0;
            case MaterialType.Dirt: return 1;
            default: return 2;
        }
    }

    // =====================================================================
    // Face local <-> 3D
    // =====================================================================

    public static Vector2 ToFaceLocal(
        VoxelAwareMeshPrecut.FaceId face,
        Vector3 chunkLocal,
        Vector3 chunkOrigin,
        float voxelSize)
    {
        Vector3 global = chunkLocal + chunkOrigin;
        int uAxis = (face.Axis + 1) % 3;
        int vAxis = (face.Axis + 2) % 3;
        return new Vector2(
            Component(global, uAxis) / voxelSize - face.U,
            Component(global, vAxis) / voxelSize - face.V
        );
    }

    public static Vector3 ToChunkLocal(
        VoxelAwareMeshPrecut.FaceId face,
        Vector2 uv,
        Vector3 chunkOrigin,
        float voxelSize)
    {
        Vector3 global = Vector3.zero;
        SetComponent(ref global, face.Axis, face.Plane * voxelSize);
        SetComponent(
            ref global,
            (face.Axis + 1) % 3,
            (face.U + uv.x) * voxelSize
        );
        SetComponent(
            ref global,
            (face.Axis + 2) % 3,
            (face.V + uv.y) * voxelSize
        );

        return global - chunkOrigin;
    }

    // =====================================================================
    // Planar Boundary Graph
    // =====================================================================

    public static List<FaceRegion> Partition(
        IReadOnlyList<FaceSegment> segments,
        float epsilon)
    {
        List<FaceSegment> allSegments = new List<FaceSegment>();
        if (segments != null) allSegments.AddRange(segments);

        allSegments.Add(new FaceSegment(
            new Vector2(0f, 0f), new Vector2(1f, 0f)));
        allSegments.Add(new FaceSegment(
            new Vector2(1f, 0f), new Vector2(1f, 1f)));
        allSegments.Add(new FaceSegment(
            new Vector2(1f, 1f), new Vector2(0f, 1f)));
        allSegments.Add(new FaceSegment(
            new Vector2(0f, 1f), new Vector2(0f, 0f)));

        Dictionary<int, List<float>> splits =
            new Dictionary<int, List<float>>();
        for (int i = 0; i < allSegments.Count; i++)
        {
            splits.Add(i, new List<float> { 0f, 1f });
        }

        for (int i = 0; i < allSegments.Count; i++)
        {
            for (int j = i + 1; j < allSegments.Count; j++)
            {
                if (!TryIntersect(
                    allSegments[i],
                    allSegments[j],
                    epsilon,
                    out float t,
                    out float s))
                {
                    continue;
                }

                if (t > epsilon && t < 1f - epsilon) splits[i].Add(t);
                if (s > epsilon && s < 1f - epsilon) splits[j].Add(s);
            }
        }

        List<Vector2> nodes = new List<Vector2>();
        Dictionary<string, int> nodeIds = new Dictionary<string, int>();
        List<Vector2Int> edges = new List<Vector2Int>();

        for (int i = 0; i < allSegments.Count; i++)
        {
            FaceSegment segment = allSegments[i];
            splits[i].Sort();

            for (int k = 0; k + 1 < splits[i].Count; k++)
            {
                float t0 = splits[i][k];
                float t1 = splits[i][k + 1];
                if (t1 - t0 <= epsilon) continue;

                Vector2 a = Vector2.Lerp(segment.A, segment.B, t0);
                Vector2 b = Vector2.Lerp(segment.A, segment.B, t1);
                int idA = AddNode(nodes, nodeIds, a);
                int idB = AddNode(nodes, nodeIds, b);
                if (idA != idB) edges.Add(new Vector2Int(idA, idB));
            }
        }

        List<List<int>> adjacency = new List<List<int>>();
        for (int i = 0; i < nodes.Count; i++)
        {
            adjacency.Add(new List<int>());
        }

        for (int i = 0; i < edges.Count; i++)
        {
            int a = edges[i].x;
            int b = edges[i].y;
            adjacency[a].Add(b);
            adjacency[b].Add(a);
        }

        List<List<int>> loops = new List<List<int>>();
        Dictionary<string, bool> used = new Dictionary<string, bool>();

        for (int a = 0; a < nodes.Count; a++)
        {
            for (int n = 0; n < adjacency[a].Count; n++)
            {
                int b = adjacency[a][n];
                string startKey = a + "->" + b;
                if (used.ContainsKey(startKey)) continue;

                List<int> loop = new List<int>();
                Dictionary<string, bool> local =
                    new Dictionary<string, bool>();
                int from = a;
                int to = b;

                while (true)
                {
                    string key = from + "->" + to;
                    if (local.ContainsKey(key)) break;

                    local.Add(key, true);
                    used[key] = true;
                    loop.Add(from);

                    Vector2 reverse = nodes[from] - nodes[to];
                    int next = -1;
                    float bestAngle = -1f;

                    List<int> outgoing = adjacency[to];
                    for (int i = 0; i < outgoing.Count; i++)
                    {
                        int candidate = outgoing[i];
                        if (candidate == from) continue;

                        Vector2 direction = nodes[candidate] - nodes[to];
                        float cross = reverse.x * direction.y -
                                      reverse.y * direction.x;
                        float dot = reverse.x * direction.x +
                                    reverse.y * direction.y;
                        float angle = Mathf.Atan2(cross, dot);
                        if (angle < 0f) angle += Mathf.PI * 2f;

                        if (angle > bestAngle)
                        {
                            bestAngle = angle;
                            next = candidate;
                        }
                    }

                    if (next < 0) break;
                    from = to;
                    to = next;

                    if (from == a && to == b)
                    {
                        break;
                    }
                }

                if (loop.Count >= 3) loops.Add(loop);
            }
        }

        Dictionary<string, List<int>> canonical =
            new Dictionary<string, List<int>>();
        for (int i = 0; i < loops.Count; i++)
        {
            string key = CanonicalLoopKey(loops[i]);
            if (!canonical.ContainsKey(key)) canonical.Add(key, loops[i]);
        }

        loops.Clear();
        foreach (KeyValuePair<string, List<int>> pair in canonical)
        {
            loops.Add(pair.Value);
        }

        if (loops.Count > 1)
        {
            for (int i = loops.Count - 1; i >= 0; i--)
            {
                if (Mathf.Abs(LoopArea(loops[i], nodes)) > 0.99f)
                {
                    loops.RemoveAt(i);
                }
            }
        }

        List<FaceRegion> regions = new List<FaceRegion>();
        for (int i = 0; i < loops.Count; i++)
        {
            float area = Mathf.Abs(LoopArea(loops[i], nodes));
            if (area <= epsilon) continue;

            FaceRegion region = new FaceRegion();
            region.Area = area;

            for (int p = 0; p < loops[i].Count; p++)
            {
                region.Polygon.Add(nodes[loops[i][p]]);
            }

            // Spec 13.7.2 Region Interior Sample。
            region.Midpoint = RegionInteriorSample(region.Polygon, epsilon);
            regions.Add(region);
        }

        return regions;
    }

    /// <summary>
    /// Spec 13.7.2：Region 的稳定确定性 interior sample。
    /// 对 Region Polygon 做确定性 Triangulation，按确定顺序选择面积大于
    /// GeometryTolerance 且 centroid 不在任何 Region Boundary 上的三角形。
    /// 所有候选都不合法时回退到 Polygon vertex centroid（退化 Region）。
    /// </summary>
    private static Vector2 RegionInteriorSample(
        List<Vector2> polygon,
        float epsilon)
    {
        if (polygon == null || polygon.Count < 3)
        {
            return Vector2.zero;
        }

        Vector2 fallback = Vector2.zero;
        for (int i = 0; i < polygon.Count; i++) fallback += polygon[i];
        fallback /= polygon.Count;

        List<Vector3Int> triangles = TriangulateLoop(polygon, epsilon);
        for (int t = 0; t < triangles.Count; t++)
        {
            Vector3Int tri = triangles[t];
            Vector2 a = polygon[tri.x];
            Vector2 b = polygon[tri.y];
            Vector2 c = polygon[tri.z];

            float area = Mathf.Abs(
                (b.x - a.x) * (c.y - a.y) -
                (c.x - a.x) * (b.y - a.y)) * 0.5f;
            if (area <= epsilon) continue;

            Vector2 candidate = (a + b + c) / 3f;
            if (IsOnPolygonBoundary(candidate, polygon, epsilon)) continue;

            return candidate;
        }

        return fallback;
    }

    private static bool IsOnPolygonBoundary(
        Vector2 point,
        List<Vector2> polygon,
        float epsilon)
    {
        for (int i = 0; i < polygon.Count; i++)
        {
            Vector2 a = polygon[i];
            Vector2 b = polygon[(i + 1) % polygon.Count];
            if (DistanceToSegment(point, a, b) <= epsilon) return true;
        }

        return false;
    }

    private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float lengthSquared = ab.sqrMagnitude;
        if (lengthSquared <= 1e-12f) return (point - a).magnitude;

        float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / lengthSquared);
        return (point - (a + ab * t)).magnitude;
    }

    // =====================================================================
    // Hidden Face Evaluation
    // =====================================================================

    /// <summary>
    /// bool 兼容重载：true = HiddenFace，false = NoHiddenFace。
    /// Evaluation Failure 必须使用 RegionDecision 重载，不能被折叠成 false。
    /// </summary>
    public static SpatialFaceRecord Evaluate(
        VoxelAwareMeshPrecut.FaceId face,
        Vector3Int negativeVoxel,
        Vector3Int positiveVoxel,
        bool hasPositiveVoxel,
        bool negativeAir,
        bool positiveAir,
        Vector3Int ownerChunkCoordinate,
        IReadOnlyList<FaceSegment> segments,
        MaterialField materialField,
        float voxelSize,
        VoxelAwareMeshPrecut.ChunkGeometry ownerGeometry,
        Func<Vector2, bool> isSolidSide,
        Func<Vector2, MaterialType> regionMaterialResolver = null)
    {
        Func<Vector2, RegionDecision> resolver = null;
        if (isSolidSide != null)
        {
            resolver = uv => isSolidSide(uv)
                ? RegionDecision.HiddenFace
                : RegionDecision.NoHiddenFace;
        }

        return Evaluate(
            face,
            negativeVoxel,
            positiveVoxel,
            hasPositiveVoxel,
            negativeAir,
            positiveAir,
            ownerChunkCoordinate,
            segments,
            materialField,
            voxelSize,
            ownerGeometry,
            resolver,
            regionMaterialResolver
        );
    }

    public static SpatialFaceRecord Evaluate(
        VoxelAwareMeshPrecut.FaceId face,
        Vector3Int negativeVoxel,
        Vector3Int positiveVoxel,
        bool hasPositiveVoxel,
        bool negativeAir,
        bool positiveAir,
        Vector3Int ownerChunkCoordinate,
        IReadOnlyList<FaceSegment> segments,
        MaterialField materialField,
        float voxelSize,
        VoxelAwareMeshPrecut.ChunkGeometry ownerGeometry,
        Func<Vector2, RegionDecision> regionDecisionResolver,
        Func<Vector2, MaterialType> regionMaterialResolver = null)
    {
        SpatialFaceRecord record = new SpatialFaceRecord
        {
            Face = face,
            NegativeVoxel = negativeVoxel,
            PositiveVoxel = positiveVoxel,
            HasPositiveVoxel = hasPositiveVoxel,
            NegativeAir = negativeAir,
            PositiveAir = positiveAir,
            OwnerChunkCoordinate = ownerChunkCoordinate,
            Evaluated = true
        };

        if (segments != null) record.Segments.AddRange(segments);

        if (negativeAir == positiveAir)
        {
            record.Regions = Partition(record.Segments, Tolerance);
            return record;
        }

        // Midpoint evaluation 的前提：所有 Natural Boundary / Bridge /
        // Coplanar Boundary 已经作为 segments 进入 Partition。
        record.Regions = Partition(record.Segments, Tolerance);

        Vector3Int solidVoxel = negativeAir ? positiveVoxel : negativeVoxel;

        for (int i = 0; i < record.Regions.Count; i++)
        {
            FaceRegion region = record.Regions[i];
            RegionDecision decision = regionDecisionResolver != null
                ? regionDecisionResolver(region.Midpoint)
                : RegionDecision.HiddenFace;

            region.Decision = decision;
            region.GeneratesHiddenFace = decision == RegionDecision.HiddenFace;

            if (decision == RegionDecision.EvaluationFailed)
            {
                // Evaluation Failure 保持独立状态：
                // 不生成 Hidden Face，也不能被当成 NoHiddenFace 静默继续。
                record.Decision = RegionDecision.EvaluationFailed;
                record.EvaluationFailed = true;
                if (string.IsNullOrEmpty(record.EvaluationFailureMessage))
                {
                    record.EvaluationFailureMessage =
                        "EVALUATION FAILURE: Region classification failed" +
                        " Face=" + face.Key +
                        " Region=" + region.Midpoint;
                }
                continue;
            }

            // B3-C：Region 覆盖语义细分。
            // 覆盖状态均匀的 Region 与既有行为完全一致（输出原多边形）；
            // 只有同一 Region 内部同时存在“需要封闭/不需要封闭”时才按覆盖边界
            // 拆成子区域，只输出需要封闭的部分。
            bool coverageFailed;
            List<List<Vector2>> coveragePieces = BuildCoveragePieces(
                region.Polygon,
                regionDecisionResolver,
                out coverageFailed
            );

            if (coverageFailed)
            {
                record.Decision = RegionDecision.EvaluationFailed;
                record.EvaluationFailed = true;
                if (string.IsNullOrEmpty(record.EvaluationFailureMessage))
                {
                    record.EvaluationFailureMessage =
                        "EVALUATION FAILURE: Region coverage subdivision failed" +
                        " Face=" + face.Key +
                        " Region=" + region.Midpoint;
                }
                continue;
            }

            if (coveragePieces.Count == 0) continue;

            MaterialType material = regionMaterialResolver != null
                ? regionMaterialResolver(region.Midpoint)
                : GetVoxelMainMaterial(
                    materialField,
                    solidVoxel,
                    voxelSize
                );
            int submesh = MaterialTypeToSubmesh(material);

            for (int pieceIndex = 0;
                 pieceIndex < coveragePieces.Count;
                 pieceIndex++)
            {
                List<Vector2> piece = coveragePieces[pieceIndex];
                if (piece == null || piece.Count < 3) continue;

                List<Vector3Int> triangles = TriangulateLoop(
                    piece,
                    Tolerance
                );

                for (int t = 0; t < triangles.Count; t++)
                {
                    Vector3Int tri = triangles[t];
                    Vector2[] uv =
                    {
                        piece[tri.x],
                        piece[tri.y],
                        piece[tri.z]
                    };

                    // 丢弃数值退化碎片（面积 <= 1e-6 m² = 0.001 mm²）：
                    // 近共线顶点产生的薄片方向不可靠，会污染法线。
                    float pieceArea = Mathf.Abs(
                        (uv[1].x - uv[0].x) * (uv[2].y - uv[0].y) -
                        (uv[2].x - uv[0].x) * (uv[1].y - uv[0].y)
                    ) * 0.5f;

                    if (pieceArea <= 1e-6f) continue;

                    HiddenFaceFragment fragment = new HiddenFaceFragment
                    {
                        Face = face,
                        Submesh = submesh,
                        Active = true
                    };

                    for (int p = 0; p < uv.Length; p++)
                    {
                        Vector3 local = ToChunkLocal(
                            face,
                            uv[p],
                            ownerGeometry != null
                                ? ownerGeometry.ChunkOrigin
                                : Vector3.zero,
                            voxelSize
                        );

                        fragment.Polygon.Add(local);
                        fragment.VertexIds.Add(
                            ownerGeometry != null
                                ? ownerGeometry.Pool.GetOrAdd(local)
                                : p
                        );
                    }

                    if (fragment.VertexIds.Count == 3)
                    {
                        fragment.Area = TriangleArea3(
                            fragment.Polygon[0],
                            fragment.Polygon[1],
                            fragment.Polygon[2]
                        );
                        record.HiddenFaces.Add(fragment);
                    }
                }
            }
        }

        return record;
    }

    // =====================================================================
    // B3-C：Region 覆盖语义细分
    //
    // 问题：Region 由 Bridge / Coplanar / Natural Boundary 划分，但基础表面
    // （Solid/Air）覆盖边界不一定与这些 Segment 重合 ⇒ 同一个 Region 内部可能
    // 同时存在“需要封闭”和“不需要封闭”的位置。既有实现只用 Region Interior
    // Sample 一点决定整片 Region，当该点贴近覆盖边界时会把整片 Region 判成
    // Hidden Face（多覆盖）或漏掉应当封闭的部分（漏覆盖）。
    //
    // 方案：Region 覆盖状态不均匀时，按覆盖判定的符号边界把 Region 拆成子区域，
    // 只输出需要封闭的子区域；均匀 Region 完全保持既有行为（几何不变）。
    // =====================================================================

    /// <summary>
    /// 细分单元目标尺寸（米）。实际单元边长 h ≤ CoverageCellSize = 0.04m
    /// （cellsPerAxis = clamp(ceil(size/h), 2, 32) ⇒ h = size/cells ≤ 0.04m），
    /// 因此“单个边界单元整格误判面积 ≤ h² ≤ 1.6e-3 m²”成立。
    /// 注意：覆盖判据的容差属于验证侧工程判据（见 B3-C 验证与
    /// Specifications/CoordinateMigration.md §6.3），生产代码只负责几何粒度。
    /// </summary>
    private const float CoverageCellSize = 0.04f;
    private const int CoverageMaxCellsPerAxis = 32;
    private const int CoverageMinCellsPerAxis = 2;
    private const int CoverageUniformityGrid = 5;
    private const int CoverageCrossingIterations = 24;

    /// <summary>
    /// 计算一个 Region 最终应当输出的 Hidden Face 多边形集合：
    ///   - 覆盖状态均匀：返回原 Region 多边形（与既有行为一致）或空集；
    ///   - 不均匀：按覆盖符号边界细分，返回需要封闭的子多边形。
    /// </summary>
    private static List<List<Vector2>> BuildCoveragePieces(
        List<Vector2> regionPolygon,
        Func<Vector2, RegionDecision> resolver,
        out bool failed)
    {
        failed = false;
        List<List<Vector2>> pieces = new List<List<Vector2>>();

        if (regionPolygon == null || regionPolygon.Count < 3) return pieces;

        if (resolver == null)
        {
            pieces.Add(regionPolygon);
            return pieces;
        }

        RegionDecision uniform;
        if (IsRegionCoverageUniform(
            regionPolygon,
            resolver,
            out uniform,
            out failed))
        {
            if (!failed && uniform == RegionDecision.HiddenFace)
            {
                pieces.Add(regionPolygon);
            }

            return pieces;
        }

        if (failed) return pieces;

        float minU = float.MaxValue;
        float maxU = float.MinValue;
        float minV = float.MaxValue;
        float maxV = float.MinValue;
        for (int i = 0; i < regionPolygon.Count; i++)
        {
            minU = Mathf.Min(minU, regionPolygon[i].x);
            maxU = Mathf.Max(maxU, regionPolygon[i].x);
            minV = Mathf.Min(minV, regionPolygon[i].y);
            maxV = Mathf.Max(maxV, regionPolygon[i].y);
        }

        float sizeU = maxU - minU;
        float sizeV = maxV - minV;
        if (sizeU <= 1e-6f || sizeV <= 1e-6f) return pieces;

        int cellsU = Mathf.Clamp(
            Mathf.CeilToInt(sizeU / CoverageCellSize),
            CoverageMinCellsPerAxis,
            CoverageMaxCellsPerAxis
        );
        int cellsV = Mathf.Clamp(
            Mathf.CeilToInt(sizeV / CoverageCellSize),
            CoverageMinCellsPerAxis,
            CoverageMaxCellsPerAxis
        );

        float cellU = sizeU / cellsU;
        float cellV = sizeV / cellsV;

        // 0 = 实体（不封闭）；1 = 开口且完整落在 Region 内（可合并）；
        // 2 = 开口但被 Region 边界裁剪或跨覆盖边界（单独输出）。
        int[,] cellClass = new int[cellsU, cellsV];
        List<Vector2>[,] cellPolygon = new List<Vector2>[cellsU, cellsV];

        for (int iu = 0; iu < cellsU; iu++)
        {
            for (int iv = 0; iv < cellsV; iv++)
            {
                float u0 = minU + iu * cellU;
                float u1 = u0 + cellU;
                float v0 = minV + iv * cellV;
                float v1 = v0 + cellV;

                List<Vector2> cell = ClipPolygonToBox(
                    regionPolygon,
                    u0,
                    v0,
                    u1,
                    v1
                );

                if (cell == null || cell.Count < 3) continue;

                bool air00 = ClassifyPoint(
                    resolver,
                    new Vector2(u0, v0),
                    ref failed
                );
                bool air10 = ClassifyPoint(
                    resolver,
                    new Vector2(u1, v0),
                    ref failed
                );
                bool air01 = ClassifyPoint(
                    resolver,
                    new Vector2(u0, v1),
                    ref failed
                );
                bool air11 = ClassifyPoint(
                    resolver,
                    new Vector2(u1, v1),
                    ref failed
                );

                if (failed) return pieces;

                int airCount = (air00 ? 1 : 0) + (air10 ? 1 : 0) +
                               (air01 ? 1 : 0) + (air11 ? 1 : 0);

                if (airCount == 0) continue;

                bool fullCell = Mathf.Abs(
                    (float)Mathf.Abs((float)PolygonArea2(cell)) - cellU * cellV
                ) <= 1e-6f;

                if (airCount == 4)
                {
                    cellClass[iu, iv] = fullCell ? 1 : 2;
                    cellPolygon[iu, iv] = cell;
                    continue;
                }

                // 混合单元：用符号变化边构造分段直线，再按“开口半平面”裁剪。
                List<Vector2> airPiece = null;

                if (airCount == 2 && air00 == air11 && air10 == air01)
                {
                    // 对角（saddle）：符号变化边不唯一，用单元质心判定，
                    // 误差 ≤ cellU*cellV（~1.6e-3 m²）。
                    bool air = ClassifyPoint(
                        resolver,
                        new Vector2((u0 + u1) * 0.5f, (v0 + v1) * 0.5f),
                        ref failed
                    );
                    if (failed) return pieces;
                    if (air) airPiece = cell;
                }
                else
                {
                    airPiece = ClipCellToCoverageBoundary(
                        cell,
                        resolver,
                        new Vector2(u0, v0),
                        new Vector2(u1, v0),
                        new Vector2(u1, v1),
                        new Vector2(u0, v1),
                        air00,
                        air10,
                        air11,
                        air01,
                        ref failed
                    );

                    if (failed) return pieces;
                }

                if (airPiece != null && airPiece.Count >= 3)
                {
                    // 丢弃数值退化碎片（面积 <= 1e-6 m² = 0.001 mm²）：
                    // 这类薄片的方向由浮点噪声决定，会污染 Final Mesh 的法线。
                    if (Mathf.Abs((float)PolygonArea2(airPiece)) <= 1e-6f)
                    {
                        continue;
                    }

                    cellClass[iu, iv] = 2;
                    cellPolygon[iu, iv] = airPiece;
                }
            }
        }

        AppendMergedAirRectangles(
            cellClass,
            cellPolygon,
            cellsU,
            cellsV,
            minU,
            minV,
            cellU,
            cellV,
            pieces
        );

        return pieces;
    }

    private static bool ClassifyPoint(
        Func<Vector2, RegionDecision> resolver,
        Vector2 point,
        ref bool failed)
    {
        RegionDecision decision = resolver(point);
        if (decision == RegionDecision.EvaluationFailed)
        {
            failed = true;
            return false;
        }

        return decision == RegionDecision.HiddenFace;
    }

    /// <summary>
    /// Region 覆盖状态是否均匀：在多边形内部栅格点 + 三角化三角形重心上
    /// 采样覆盖判定，只要出现不同结果（或评估失败）就认为不均匀。
    /// </summary>
    private static bool IsRegionCoverageUniform(
        List<Vector2> polygon,
        Func<Vector2, RegionDecision> resolver,
        out RegionDecision uniform,
        out bool failed)
    {
        uniform = RegionDecision.NoHiddenFace;
        failed = false;
        bool hasSample = false;

        // 三角化重心（TriangulateLoop 已返回原始下标）。
        List<Vector3Int> triangles = TriangulateLoop(polygon, Tolerance);
        for (int t = 0; t < triangles.Count; t++)
        {
            Vector3Int tri = triangles[t];
            Vector2 centroid = (
                polygon[tri.x] + polygon[tri.y] + polygon[tri.z]
            ) / 3f;

            if (!SampleCoverage(
                resolver,
                centroid,
                ref hasSample,
                ref uniform,
                ref failed))
            {
                return false;
            }
        }

        // 内部栅格点。
        float minU = float.MaxValue;
        float maxU = float.MinValue;
        float minV = float.MaxValue;
        float maxV = float.MinValue;
        for (int i = 0; i < polygon.Count; i++)
        {
            minU = Mathf.Min(minU, polygon[i].x);
            maxU = Mathf.Max(maxU, polygon[i].x);
            minV = Mathf.Min(minV, polygon[i].y);
            maxV = Mathf.Max(maxV, polygon[i].y);
        }

        for (int iu = 1; iu < CoverageUniformityGrid; iu++)
        {
            for (int iv = 1; iv < CoverageUniformityGrid; iv++)
            {
                Vector2 point = new Vector2(
                    minU + (maxU - minU) * iu / CoverageUniformityGrid,
                    minV + (maxV - minV) * iv / CoverageUniformityGrid
                );

                if (!PointInPolygon(point, polygon)) continue;

                if (!SampleCoverage(
                    resolver,
                    point,
                    ref hasSample,
                    ref uniform,
                    ref failed))
                {
                    return false;
                }
            }
        }

        return failed || hasSample;
    }

    private static bool SampleCoverage(
        Func<Vector2, RegionDecision> resolver,
        Vector2 point,
        ref bool hasSample,
        ref RegionDecision uniform,
        ref bool failed)
    {
        RegionDecision decision = resolver(point);
        if (decision == RegionDecision.EvaluationFailed)
        {
            failed = true;
            return false;
        }

        if (!hasSample)
        {
            uniform = decision;
            hasSample = true;
            return true;
        }

        if (decision != uniform) return false;

        return true;
    }

    /// <summary>
    /// 用单元四角的覆盖判定构造覆盖边界（符号变化点用二分法求交），
    /// 然后把单元裁剪到“开口”一侧。
    /// </summary>
    private static List<Vector2> ClipCellToCoverageBoundary(
        List<Vector2> cell,
        Func<Vector2, RegionDecision> resolver,
        Vector2 p00,
        Vector2 p10,
        Vector2 p11,
        Vector2 p01,
        bool air00,
        bool air10,
        bool air11,
        bool air01,
        ref bool failed)
    {
        List<Vector2> crossings = new List<Vector2>();

        AddCrossing(resolver, p00, p10, air00, air10, crossings, ref failed);
        AddCrossing(resolver, p10, p11, air10, air11, crossings, ref failed);
        AddCrossing(resolver, p11, p01, air11, air01, crossings, ref failed);
        AddCrossing(resolver, p01, p00, air01, air00, crossings, ref failed);

        if (failed || crossings.Count < 2) return null;

        Vector2 airCorner = air00 ? p00 : air10 ? p10 : air11 ? p11 : p01;

        return ClipPolygonToHalfPlane(
            cell,
            crossings[0],
            crossings[1],
            airCorner
        );
    }

    private static void AddCrossing(
        Func<Vector2, RegionDecision> resolver,
        Vector2 a,
        Vector2 b,
        bool airA,
        bool airB,
        List<Vector2> crossings,
        ref bool failed)
    {
        if (airA == airB) return;

        Vector2 lo = a;
        Vector2 hi = b;
        for (int i = 0; i < CoverageCrossingIterations; i++)
        {
            Vector2 mid = (lo + hi) * 0.5f;
            bool airMid = ClassifyPoint(resolver, mid, ref failed);
            if (failed) return;

            if (airMid == airA) lo = mid;
            else hi = mid;
        }

        crossings.Add((lo + hi) * 0.5f);
    }

    private static List<Vector2> ClipPolygonToHalfPlane(
        List<Vector2> polygon,
        Vector2 lineA,
        Vector2 lineB,
        Vector2 insidePoint)
    {
        List<Vector2> result = new List<Vector2>();
        if (polygon == null || polygon.Count < 3) return result;

        Vector2 direction = lineB - lineA;
        float sideInside = direction.x * (insidePoint.y - lineA.y) -
                           direction.y * (insidePoint.x - lineA.x);

        for (int i = 0; i < polygon.Count; i++)
        {
            Vector2 current = polygon[i];
            Vector2 previous = polygon[(i - 1 + polygon.Count) % polygon.Count];

            float dCurrent = direction.x * (current.y - lineA.y) -
                             direction.y * (current.x - lineA.x);
            float dPrevious = direction.x * (previous.y - lineA.y) -
                              direction.y * (previous.x - lineA.x);

            bool insideCurrent = sideInside >= 0f
                ? dCurrent >= -1e-9f
                : dCurrent <= 1e-9f;
            bool insidePrevious = sideInside >= 0f
                ? dPrevious >= -1e-9f
                : dPrevious <= 1e-9f;

            if (insideCurrent != insidePrevious)
            {
                float denominator = dCurrent - dPrevious;
                if (Mathf.Abs(denominator) > 1e-12f)
                {
                    float s = dCurrent / denominator;
                    result.Add(new Vector2(
                        current.x + s * (previous.x - current.x),
                        current.y + s * (previous.y - current.y)
                    ));
                }
            }

            if (insideCurrent) result.Add(current);
        }

        return result;
    }

    /// <summary>
    /// 把“完整落在 Region 内且判定为开口”的单元贪心合并成矩形，避免细分后
    /// 三角形数量爆炸；被 Region 边界裁剪或跨覆盖边界的单元保持原样输出。
    /// </summary>
    private static void AppendMergedAirRectangles(
        int[,] cellClass,
        List<Vector2>[,] cellPolygon,
        int cellsU,
        int cellsV,
        float minU,
        float minV,
        float cellU,
        float cellV,
        List<List<Vector2>> pieces)
    {
        bool[,] used = new bool[cellsU, cellsV];

        for (int iv = 0; iv < cellsV; iv++)
        {
            for (int iu = 0; iu < cellsU; iu++)
            {
                if (cellClass[iu, iv] == 2 && cellPolygon[iu, iv] != null)
                {
                    pieces.Add(cellPolygon[iu, iv]);
                    continue;
                }

                if (cellClass[iu, iv] != 1 || used[iu, iv]) continue;

                int runEnd = iu;
                while (runEnd + 1 < cellsU &&
                       cellClass[runEnd + 1, iv] == 1 &&
                       !used[runEnd + 1, iv])
                {
                    runEnd++;
                }

                int runBottom = iv;
                bool canExtend = true;
                while (canExtend && runBottom + 1 < cellsV)
                {
                    for (int k = iu; k <= runEnd; k++)
                    {
                        if (cellClass[k, runBottom + 1] != 1 ||
                            used[k, runBottom + 1])
                        {
                            canExtend = false;
                            break;
                        }
                    }

                    if (canExtend) runBottom++;
                }

                for (int k = iu; k <= runEnd; k++)
                {
                    for (int m = iv; m <= runBottom; m++)
                    {
                        used[k, m] = true;
                    }
                }

                float u0 = minU + iu * cellU;
                float u1 = minU + (runEnd + 1) * cellU;
                float v0 = minV + iv * cellV;
                float v1 = minV + (runBottom + 1) * cellV;

                pieces.Add(new List<Vector2>
                {
                    new Vector2(u0, v0),
                    new Vector2(u1, v0),
                    new Vector2(u1, v1),
                    new Vector2(u0, v1)
                });
            }
        }
    }

    private static List<Vector2> ClipPolygonToBox(
        List<Vector2> polygon,
        float minU,
        float minV,
        float maxU,
        float maxV)
    {
        List<Vector2> result = polygon;

        result = ClipHalfPlane(result, 0, minU);
        result = ClipHalfPlane(result, 1, -maxU);
        result = ClipHalfPlane(result, 2, minV);
        result = ClipHalfPlane(result, 3, -maxV);

        return result;
    }

    private static List<Vector2> ClipHalfPlane(
        List<Vector2> polygon,
        int mode,
        float bound)
    {
        List<Vector2> result = new List<Vector2>();
        if (polygon == null || polygon.Count == 0) return result;

        for (int i = 0; i < polygon.Count; i++)
        {
            Vector2 current = polygon[i];
            Vector2 previous = polygon[(i - 1 + polygon.Count) % polygon.Count];
            float dCurrent = AxisValue(current, mode) - bound;
            float dPrevious = AxisValue(previous, mode) - bound;
            bool insideCurrent = dCurrent >= 0;
            bool insidePrevious = dPrevious >= 0;

            if (insideCurrent != insidePrevious)
            {
                float denominator = dCurrent - dPrevious;
                if (Mathf.Abs(denominator) > 1e-12f)
                {
                    float s = dCurrent / denominator;
                    result.Add(new Vector2(
                        current.x + s * (previous.x - current.x),
                        current.y + s * (previous.y - current.y)
                    ));
                }
            }

            if (insideCurrent) result.Add(current);
        }

        return result;
    }

    private static float AxisValue(Vector2 p, int mode)
    {
        switch (mode)
        {
            case 0: return p.x;
            case 1: return -p.x;
            case 2: return p.y;
            default: return -p.y;
        }
    }

    private static double PolygonArea2(List<Vector2> polygon)
    {
        double area = 0;
        for (int i = 0; i < polygon.Count; i++)
        {
            Vector2 a = polygon[i];
            Vector2 b = polygon[(i + 1) % polygon.Count];
            area += a.x * b.y - b.x * a.y;
        }

        return area * 0.5;
    }

    /// <summary>点是否在多边形内部（射线法，供 Region 覆盖细分使用）。</summary>
    private static bool PointInPolygon(Vector2 point, List<Vector2> polygon)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            Vector2 a = polygon[i];
            Vector2 b = polygon[j];
            if (((a.y > point.y) != (b.y > point.y)) &&
                (point.x <
                 (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x))
            {
                inside = !inside;
            }
        }

        return inside;
    }

    public static List<Vector3Int> TriangulateLoop(
        List<Vector2> loop,
        float epsilon)
    {
        List<Vector3Int> triangles = new List<Vector3Int>();
        List<Vector2> poly = new List<Vector2>(loop);

        // Ear clipping 会 RemoveAt 掉已切出的耳朵，因此必须并行维护
        // “当前 poly 下标 → 原始 loop 下标”的映射；调用方（RegionInteriorSample /
        // Hidden Face 生成）都用返回的下标索引**原始** loop。
        // 若直接返回收缩后列表的下标，第二个及之后的耳朵会指向错误顶点，
        // 造成同面内三角形重叠 + 未覆盖缺口（B3-B 修复的根因）。
        List<int> ids = new List<int>(loop.Count);
        for (int i = 0; i < loop.Count; i++)
        {
            ids.Add(i);
        }

        if (LoopArea2(poly) < 0f)
        {
            poly.Reverse();
            ids.Reverse();
        }

        while (poly.Count > 3)
        {
            bool removed = false;

            for (int v = 0; v < poly.Count; v++)
            {
                int prev = (v - 1 + poly.Count) % poly.Count;
                int current = v;
                int next = (v + 1) % poly.Count;

                if (Cross2(poly[prev], poly[current], poly[next]) <= epsilon)
                {
                    continue;
                }

                bool contains = false;
                for (int k = 0; k < poly.Count; k++)
                {
                    if (k == prev || k == current || k == next) continue;
                    if (PointInTriangle(
                        poly[k],
                        poly[prev],
                        poly[current],
                        poly[next],
                        epsilon))
                    {
                        contains = true;
                        break;
                    }
                }

                if (contains) continue;

                triangles.Add(new Vector3Int(
                    ids[prev],
                    ids[current],
                    ids[next]
                ));
                poly.RemoveAt(current);
                ids.RemoveAt(current);
                removed = true;
                break;
            }

            if (!removed) break;
        }

        if (poly.Count == 3)
        {
            triangles.Add(new Vector3Int(ids[0], ids[1], ids[2]));
        }

        return triangles;
    }

    // =====================================================================
    // Local Face Records from Phase 1 ChunkGeometry
    // =====================================================================

    public static void BuildLocalRecords(
        VoxelAwareMeshPrecut.ChunkGeometry geometry,
        Vector3Int chunkCoordinate,
        Vector3Int chunkVoxelSize,
        MaterialField materialField)
    {
        if (geometry == null) return;

        Dictionary<string, SpatialFaceRecord> records =
            new Dictionary<string, SpatialFaceRecord>();

        for (int i = 0; i < geometry.FaceFragments.Count; i++)
        {
            VoxelAwareMeshPrecut.FaceFragment faceFragment =
                geometry.FaceFragments[i];
            SpatialFaceRecord record = GetOrCreateRecord(
                records,
                geometry,
                faceFragment.Face,
                chunkCoordinate,
                chunkVoxelSize,
                materialField
            );

            for (int p = 0; p < faceFragment.Polygon.Count; p++)
            {
                Vector2 a = ToFaceLocal(
                    faceFragment.Face,
                    faceFragment.Polygon[p],
                    geometry.ChunkOrigin,
                    geometry.VoxelSize
                );
                Vector2 b = ToFaceLocal(
                    faceFragment.Face,
                    faceFragment.Polygon[
                        (p + 1) % faceFragment.Polygon.Count
                    ],
                    geometry.ChunkOrigin,
                    geometry.VoxelSize
                );

                record.Segments.Add(new FaceSegment(a, b)
                {
                    IsCoplanar = true
                });
            }
        }

        for (int i = 0; i < geometry.Bridges.Count; i++)
        {
            VoxelAwareMeshPrecut.BridgeRecord bridge = geometry.Bridges[i];
            SpatialFaceRecord record = GetOrCreateRecord(
                records,
                geometry,
                bridge.Face,
                chunkCoordinate,
                chunkVoxelSize,
                materialField
            );

            Vector3 a3 = geometry.Pool.Positions[bridge.VertexA];
            Vector3 b3 = geometry.Pool.Positions[bridge.VertexB];
            Vector2 a = ToFaceLocal(
                bridge.Face,
                a3,
                geometry.ChunkOrigin,
                geometry.VoxelSize
            );
            Vector2 b = ToFaceLocal(
                bridge.Face,
                b3,
                geometry.ChunkOrigin,
                geometry.VoxelSize
            );

            record.Segments.Add(new FaceSegment(a, b)
            {
                IsBridge = true
            });
        }

        geometry.SpatialFaces.Clear();
        foreach (KeyValuePair<string, SpatialFaceRecord> pair in records)
        {
            geometry.SpatialFaces.Add(pair.Value);
        }
    }

    private static SpatialFaceRecord GetOrCreateRecord(
        Dictionary<string, SpatialFaceRecord> records,
        VoxelAwareMeshPrecut.ChunkGeometry geometry,
        VoxelAwareMeshPrecut.FaceId face,
        Vector3Int chunkCoordinate,
        Vector3Int chunkVoxelSize,
        MaterialField materialField)
    {
        if (records.TryGetValue(face.Key, out SpatialFaceRecord record))
        {
            return record;
        }

        record = new SpatialFaceRecord
        {
            Face = face,
            NegativeVoxel = GetNegativeVoxel(face),
            PositiveVoxel = GetPositiveVoxel(face),
            HasPositiveVoxel = true,
            OwnerChunkCoordinate = GetCanonicalOwnerChunk(
                face,
                chunkVoxelSize
            ),
            NegativeAir = false,
            PositiveAir = false
        };

        record.OwnerChunkCoordinate = chunkCoordinate;
        records.Add(face.Key, record);
        return record;
    }

    // =====================================================================
    // 2D helpers
    // =====================================================================

    private static int AddNode(
        List<Vector2> nodes,
        Dictionary<string, int> nodeIds,
        Vector2 point)
    {
        string key =
            Mathf.RoundToInt(point.x / Tolerance) + ":" +
            Mathf.RoundToInt(point.y / Tolerance);

        if (nodeIds.TryGetValue(key, out int id)) return id;

        id = nodes.Count;
        nodes.Add(point);
        nodeIds.Add(key, id);
        return id;
    }

    private static bool TryIntersect(
        FaceSegment a,
        FaceSegment b,
        float epsilon,
        out float t,
        out float s)
    {
        t = 0f;
        s = 0f;

        Vector2 r = a.B - a.A;
        Vector2 u = b.B - b.A;
        float denom = r.x * u.y - r.y * u.x;
        if (Mathf.Abs(denom) < epsilon) return false;

        Vector2 w = b.A - a.A;
        t = (w.x * u.y - w.y * u.x) / denom;
        s = (w.x * r.y - w.y * r.x) / denom;

        return t >= -epsilon && t <= 1f + epsilon &&
               s >= -epsilon && s <= 1f + epsilon;
    }

    private static string CanonicalLoopKey(List<int> loop)
    {
        int minIndex = 0;
        for (int i = 1; i < loop.Count; i++)
        {
            if (loop[i] < loop[minIndex]) minIndex = i;
        }

        List<int> forward = new List<int>();
        List<int> backward = new List<int>();
        for (int i = 0; i < loop.Count; i++)
        {
            forward.Add(loop[(minIndex + i) % loop.Count]);
            backward.Add(loop[(minIndex - i + loop.Count) % loop.Count]);
        }

        bool forwardSmaller = false;
        for (int i = 0; i < loop.Count; i++)
        {
            if (forward[i] != backward[i])
            {
                forwardSmaller = forward[i] < backward[i];
                break;
            }
        }

        List<int> chosen = forwardSmaller ? forward : backward;
        string key = string.Empty;
        for (int i = 0; i < chosen.Count; i++)
        {
            if (i > 0) key += ",";
            key += chosen[i];
        }

        return key;
    }

    private static float LoopArea(List<int> loop, List<Vector2> nodes)
    {
        float area = 0f;
        for (int i = 0; i < loop.Count; i++)
        {
            Vector2 a = nodes[loop[i]];
            Vector2 b = nodes[loop[(i + 1) % loop.Count]];
            area += a.x * b.y - b.x * a.y;
        }

        return area * 0.5f;
    }

    private static float LoopArea2(List<Vector2> loop)
    {
        float area = 0f;
        for (int i = 0; i < loop.Count; i++)
        {
            Vector2 a = loop[i];
            Vector2 b = loop[(i + 1) % loop.Count];
            area += a.x * b.y - b.x * a.y;
        }

        return area * 0.5f;
    }

    private static float Cross2(Vector2 a, Vector2 b, Vector2 c)
    {
        return (b.x - a.x) * (c.y - a.y) -
               (b.y - a.y) * (c.x - a.x);
    }

    private static bool PointInTriangle(
        Vector2 p,
        Vector2 a,
        Vector2 b,
        Vector2 c,
        float epsilon)
    {
        float c0 = Cross2(a, b, p);
        float c1 = Cross2(b, c, p);
        float c2 = Cross2(c, a, p);

        bool negative = c0 < -epsilon || c1 < -epsilon || c2 < -epsilon;
        bool positive = c0 > epsilon || c1 > epsilon || c2 > epsilon;
        return !(negative && positive);
    }

    private static float TriangleArea3(Vector3 a, Vector3 b, Vector3 c)
    {
        return Vector3.Cross(b - a, c - a).magnitude * 0.5f;
    }

    private static float Component(Vector3 value, int axis)
    {
        return axis == 0 ? value.x : axis == 1 ? value.y : value.z;
    }

    private static int Component(Vector3Int value, int axis)
    {
        return axis == 0 ? value.x : axis == 1 ? value.y : value.z;
    }

    private static void SetComponent(ref Vector3 value, int axis, float v)
    {
        if (axis == 0) value.x = v;
        else if (axis == 1) value.y = v;
        else value.z = v;
    }

    private static void SetComponent(ref Vector3Int value, int axis, int v)
    {
        if (axis == 0) value.x = v;
        else if (axis == 1) value.y = v;
        else value.z = v;
    }
}
