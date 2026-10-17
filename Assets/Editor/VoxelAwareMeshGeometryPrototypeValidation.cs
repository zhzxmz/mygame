using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Voxel-aware Mesh Geometry Prototype Validation。
/// 纯 Editor Prototype，不接入任何 Runtime Terrain / Mining。
/// 验证两个核心数学问题：
///   Prototype A: Triangle × Voxel Boundary Pre-cut
///   Prototype B: SpatialFace Region Partition + Hidden Face
/// </summary>
public static class VoxelAwareMeshGeometryPrototypeValidation
{
    private const float DefaultEpsilon = 1e-5f;
    private const float SnapScale = 1e-5f;

    private static int failures;
    private static int checks;

    [MenuItem("Tools/Terrain/Validate VoxelAwareMesh Geometry Prototype")]
    public static void ValidateFromMenu()
    {
        RunAll();
        LogSummary();

        Debug.Log(
            "[VoxelAwareMeshGeometryPrototypeValidation] Menu result = " +
            (failures == 0 ? "PASS" : "FAIL")
        );
    }

    public static void RunBatch()
    {
        RunAll();
        LogSummary();
        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    private static void RunAll()
    {
        failures = 0;
        checks = 0;

        RunPrototypeA();
        RunPrototypeB();
    }

    private static void Check(bool condition, string message)
    {
        checks++;
        if (condition)
        {
            Debug.Log("PASS - " + message);
        }
        else
        {
            failures++;
            Debug.LogError("FAIL - " + message);
        }
    }

    // =====================================================================
    // Prototype A
    // =====================================================================

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

    private sealed class VertexPool
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
            int qx = Mathf.RoundToInt(position.x / SnapScale);
            int qy = Mathf.RoundToInt(position.y / SnapScale);
            int qz = Mathf.RoundToInt(position.z / SnapScale);
            return qx + ":" + qy + ":" + qz;
        }
    }

    private sealed class Fragment
    {
        public Vector3Int Voxel;
        public List<Vector3> Polygon = new List<Vector3>();
        public List<int> VertexIds = new List<int>();
        public List<bool> Original = new List<bool>();
        public float Area;
    }

    private sealed class FaceFragment
    {
        public FaceId Face;
        public List<Vector3> Polygon = new List<Vector3>();
        public List<int> VertexIds = new List<int>();
        public float Area;
    }

    private struct FaceId
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
    }

    private sealed class BridgeRecord
    {
        public FaceId Face;
        public int VertexA;
        public int VertexB;
        public int FragmentA;
        public int FragmentB;
        public bool HasCutVertex;
    }

    private sealed class CutResult
    {
        public List<Fragment> Fragments = new List<Fragment>();
        public List<FaceFragment> FaceFragments = new List<FaceFragment>();
        public List<BridgeRecord> Bridges = new List<BridgeRecord>();
        public VertexPool Pool = new VertexPool();
        public float FragmentAreaSum;
        public float FaceAreaSum;
        public int DegeneratePolygonCount;
    }

    private struct Triangle3
    {
        public Vector3 A;
        public Vector3 B;
        public Vector3 C;

        public Triangle3(Vector3 a, Vector3 b, Vector3 c)
        {
            A = a;
            B = b;
            C = c;
        }
    }

    private static CutResult CutTriangles(
        IReadOnlyList<Triangle3> triangles,
        float epsilon)
    {
        CutResult result = new CutResult();

        for (int i = 0; i < triangles.Count; i++)
        {
            Triangle3 triangle = triangles[i];
            float area = TriangleArea3(triangle.A, triangle.B, triangle.C);

            if (area <= epsilon)
            {
                result.DegeneratePolygonCount++;
                continue;
            }

            int coplanarAxis = -1;
            int coplanarPlane = 0;

            for (int axis = 0; axis < 3; axis++)
            {
                if (IsCoplanarWithIntegerPlane(
                    triangle,
                    axis,
                    epsilon,
                    out int plane))
                {
                    coplanarAxis = axis;
                    coplanarPlane = plane;
                    break;
                }
            }

            if (coplanarAxis >= 0)
            {
                CutCoplanarTriangle(
                    triangle,
                    coplanarAxis,
                    coplanarPlane,
                    epsilon,
                    result
                );
                continue;
            }

            CutOrdinaryTriangle(triangle, epsilon, result);
        }

        BuildBridgeRecords(result, epsilon);
        return result;
    }

    private static void CutOrdinaryTriangle(
        Triangle3 triangle,
        float epsilon,
        CutResult result)
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

        Vector3Int minVoxel = new Vector3Int(
            Mathf.FloorToInt(min.x + epsilon),
            Mathf.FloorToInt(min.y + epsilon),
            Mathf.FloorToInt(min.z + epsilon)
        );

        Vector3Int maxVoxel = new Vector3Int(
            Mathf.FloorToInt(max.x - epsilon),
            Mathf.FloorToInt(max.y - epsilon),
            Mathf.FloorToInt(max.z - epsilon)
        );

        if (maxVoxel.x < minVoxel.x ||
            maxVoxel.y < minVoxel.y ||
            maxVoxel.z < minVoxel.z)
        {
            result.DegeneratePolygonCount++;
            return;
        }

        for (int x = minVoxel.x; x <= maxVoxel.x; x++)
        {
            for (int y = minVoxel.y; y <= maxVoxel.y; y++)
            {
                for (int z = minVoxel.z; z <= maxVoxel.z; z++)
                {
                    Vector3 boxMin = new Vector3(x, y, z);
                    Vector3 boxMax = boxMin + Vector3.one;

                    Poly3 clipped = ClipPolyToBox(
                        source,
                        boxMin,
                        boxMax,
                        epsilon
                    );

                    if (clipped == null) continue;

                    float area = PolygonArea3(clipped.Points);
                    if (area <= epsilon)
                    {
                        result.DegeneratePolygonCount++;
                        continue;
                    }

                    Fragment fragment = new Fragment();
                    fragment.Voxel = new Vector3Int(x, y, z);
                    fragment.Polygon.AddRange(clipped.Points);
                    fragment.Original.AddRange(clipped.Original);
                    fragment.Area = area;

                    for (int i = 0; i < clipped.Points.Count; i++)
                    {
                        fragment.VertexIds.Add(
                            result.Pool.GetOrAdd(clipped.Points[i])
                        );
                    }

                    result.Fragments.Add(fragment);
                    result.FragmentAreaSum += area;
                }
            }
        }
    }

    private static void CutCoplanarTriangle(
        Triangle3 triangle,
        int axis,
        int plane,
        float epsilon,
        CutResult result)
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

        int minU = Mathf.FloorToInt(min2.x + epsilon);
        int minV = Mathf.FloorToInt(min2.y + epsilon);
        int maxU = Mathf.FloorToInt(max2.x - epsilon);
        int maxV = Mathf.FloorToInt(max2.y - epsilon);

        if (maxU < minU || maxV < minV)
        {
            result.DegeneratePolygonCount++;
            return;
        }

        List<Vector2> source2 = new List<Vector2> { p0, p1, p2 };
        float area2 = PolygonArea2(source2);
        if (area2 <= epsilon)
        {
            result.DegeneratePolygonCount++;
            return;
        }

        for (int u = minU; u <= maxU; u++)
        {
            for (int v = minV; v <= maxV; v++)
            {
                List<Vector2> clipped2 = ClipPoly2ToBox(
                    source2,
                    new Vector2(u, v),
                    new Vector2(u + 1f, v + 1f),
                    epsilon
                );

                if (clipped2 == null) continue;

                float clippedArea = PolygonArea2(clipped2);
                if (clippedArea <= epsilon)
                {
                    result.DegeneratePolygonCount++;
                    continue;
                }

                FaceFragment fragment = new FaceFragment();
                fragment.Face = new FaceId(axis, plane, u, v);
                fragment.Area = clippedArea;

                for (int i = 0; i < clipped2.Count; i++)
                {
                    Vector3 point = Vector3.zero;
                    SetComponent(ref point, axis, plane);
                    SetComponent(ref point, uAxis, clipped2[i].x);
                    SetComponent(ref point, vAxis, clipped2[i].y);

                    fragment.Polygon.Add(point);
                    fragment.VertexIds.Add(result.Pool.GetOrAdd(point));
                }

                result.FaceFragments.Add(fragment);
                result.FaceAreaSum += clippedArea;
            }
        }
    }

    private static void BuildBridgeRecords(CutResult result, float epsilon)
    {
        Dictionary<string, List<BridgeCandidate>> candidates =
            new Dictionary<string, List<BridgeCandidate>>();

        for (int f = 0; f < result.Fragments.Count; f++)
        {
            Fragment fragment = result.Fragments[f];
            int count = fragment.Polygon.Count;

            for (int i = 0; i < count; i++)
            {
                int j = (i + 1) % count;
                Vector3 p0 = fragment.Polygon[i];
                Vector3 p1 = fragment.Polygon[j];

                if (!TryGetIntegerPlane(p0, p1, epsilon,
                    out int axis, out int plane))
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
            int axis = ParseBridgeAxis(pair.Key);
            int plane = ParseBridgePlane(pair.Key);

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

                    int expectedPlane = upper[axis];
                    if (plane != expectedPlane) continue;

                    int u = lower[(axis + 1) % 3];
                    int v = lower[(axis + 2) % 3];

                    result.Bridges.Add(new BridgeRecord
                    {
                        Face = new FaceId(axis, plane, u, v),
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

    private static int ParseBridgeAxis(string key)
    {
        int first = key.IndexOf(':');
        return int.Parse(key.Substring(0, first));
    }

    private static int ParseBridgePlane(string key)
    {
        int first = key.IndexOf(':');
        int second = key.IndexOf(':', first + 1);
        return int.Parse(key.Substring(first + 1, second - first - 1));
    }

    private static bool TryGetIntegerPlane(
        Vector3 p0,
        Vector3 p1,
        float epsilon,
        out int axis,
        out int plane)
    {
        axis = -1;
        plane = 0;

        for (int a = 0; a < 3; a++)
        {
            float value0 = p0[a];
            float value1 = p1[a];

            if (Mathf.Abs(value0 - value1) > epsilon) continue;

            int rounded = Mathf.RoundToInt(value0);
            if (Mathf.Abs(value0 - rounded) > epsilon) continue;

            axis = a;
            plane = rounded;
            return true;
        }

        return false;
    }

    private static bool IsCoplanarWithIntegerPlane(
        Triangle3 triangle,
        int axis,
        float epsilon,
        out int plane)
    {
        float valueA = triangle.A[axis];
        float valueB = triangle.B[axis];
        float valueC = triangle.C[axis];

        plane = Mathf.RoundToInt(valueA);
        return Mathf.Abs(valueA - plane) <= epsilon &&
               Mathf.Abs(valueB - plane) <= epsilon &&
               Mathf.Abs(valueC - plane) <= epsilon;
    }

    private static Poly3 ClipPolyToBox(
        Poly3 source,
        Vector3 min,
        Vector3 max,
        float epsilon)
    {
        Poly3 poly = source.Clone();

        poly = ClipHalfSpace(poly, 0, min.x, true, epsilon);
        if (poly == null) return null;
        poly = ClipHalfSpace(poly, 0, max.x, false, epsilon);
        if (poly == null) return null;
        poly = ClipHalfSpace(poly, 1, min.y, true, epsilon);
        if (poly == null) return null;
        poly = ClipHalfSpace(poly, 1, max.y, false, epsilon);
        if (poly == null) return null;
        poly = ClipHalfSpace(poly, 2, min.z, true, epsilon);
        if (poly == null) return null;
        poly = ClipHalfSpace(poly, 2, max.z, false, epsilon);
        if (poly == null) return null;

        if (poly.Points.Count < 3) return null;
        if (PolygonArea3(poly.Points) <= epsilon) return null;

        return poly;
    }

    private static Poly3 ClipHalfSpace(
        Poly3 source,
        int axis,
        float plane,
        bool keepGreater,
        float epsilon)
    {
        Poly3 output = new Poly3();

        for (int i = 0; i < source.Points.Count; i++)
        {
            Vector3 a = source.Points[i];
            Vector3 b = source.Points[(i + 1) % source.Points.Count];
            bool inA = IsInsideHalfSpace(a, axis, plane, keepGreater, epsilon);
            bool inB = IsInsideHalfSpace(b, axis, plane, keepGreater, epsilon);

            if (inA)
            {
                output.Points.Add(a);
                output.Original.Add(source.Original[i]);
            }

            if (inA != inB)
            {
                float denom = b[axis] - a[axis];
                float t = Mathf.Abs(denom) > epsilon
                    ? (plane - a[axis]) / denom
                    : 0.5f;

                t = Mathf.Clamp01(t);
                Vector3 point = Vector3.Lerp(a, b, t);
                output.Points.Add(point);
                output.Original.Add(false);
            }
        }

        if (output.Points.Count < 3) return null;
        return output;
    }

    private static bool IsInsideHalfSpace(
        Vector3 point,
        int axis,
        float plane,
        bool keepGreater,
        float epsilon)
    {
        float value = point[axis];
        return keepGreater ? value >= plane - epsilon : value <= plane + epsilon;
    }

    private static List<Vector2> ClipPoly2ToBox(
        List<Vector2> source,
        Vector2 min,
        Vector2 max,
        float epsilon)
    {
        List<Vector2> poly = new List<Vector2>(source);

        poly = ClipPoly2HalfSpace(poly, 0, min.x, true, epsilon);
        if (poly == null) return null;
        poly = ClipPoly2HalfSpace(poly, 0, max.x, false, epsilon);
        if (poly == null) return null;
        poly = ClipPoly2HalfSpace(poly, 1, min.y, true, epsilon);
        if (poly == null) return null;
        poly = ClipPoly2HalfSpace(poly, 1, max.y, false, epsilon);
        if (poly == null) return null;

        if (poly.Count < 3) return null;
        if (PolygonArea2(poly) <= epsilon) return null;

        return poly;
    }

    private static List<Vector2> ClipPoly2HalfSpace(
        List<Vector2> source,
        int axis,
        float plane,
        bool keepGreater,
        float epsilon)
    {
        List<Vector2> output = new List<Vector2>();

        for (int i = 0; i < source.Count; i++)
        {
            Vector2 a = source[i];
            Vector2 b = source[(i + 1) % source.Count];

            bool inA = keepGreater
                ? a[axis] >= plane - epsilon
                : a[axis] <= plane + epsilon;
            bool inB = keepGreater
                ? b[axis] >= plane - epsilon
                : b[axis] <= plane + epsilon;

            if (inA) output.Add(a);

            if (inA != inB)
            {
                float denom = b[axis] - a[axis];
                float t = Mathf.Abs(denom) > epsilon
                    ? (plane - a[axis]) / denom
                    : 0.5f;

                t = Mathf.Clamp01(t);
                output.Add(Vector2.Lerp(a, b, t));
            }
        }

        if (output.Count < 3) return null;
        return output;
    }

    private static float TriangleArea3(Vector3 a, Vector3 b, Vector3 c)
    {
        return Vector3.Cross(b - a, c - a).magnitude * 0.5f;
    }

    private static float PolygonArea3(List<Vector3> points)
    {
        if (points.Count < 3) return 0f;

        Vector3 normal = Vector3.zero;
        Vector3 anchor = points[0];

        for (int i = 1; i < points.Count - 1; i++)
        {
            normal += Vector3.Cross(points[i] - anchor, points[i + 1] - anchor);
        }

        return normal.magnitude * 0.5f;
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

    private static void RunPrototypeA()
    {
        CheckSingleVoxel();
        CheckTwoVoxels();
        CheckThreeVoxels();
        CheckTwoDimensionalCut();
        CheckPointTouch();
        CheckCoplanar();
        CheckBaseEdgeOnBoundary();
        CheckNegativeCoordinates();
        CheckWelding();
        RunToleranceMatrix();
    }

    private static void CheckSingleVoxel()
    {
        CutResult result = CutTriangles(
            new[]
            {
                new Triangle3(
                    new Vector3(0.1f, 0.1f, 0.1f),
                    new Vector3(0.9f, 0.1f, 0.1f),
                    new Vector3(0.1f, 0.9f, 0.1f))
            },
            DefaultEpsilon
        );

        Check(result.Fragments.Count == 1, "A1: single-voxel triangle yields 1 fragment");
        Check(result.Bridges.Count == 0, "A1: single-voxel triangle yields 0 bridges");
        Check(
            Mathf.Abs(result.FragmentAreaSum - TriangleArea3(
                new Vector3(0.1f, 0.1f, 0.1f),
                new Vector3(0.9f, 0.1f, 0.1f),
                new Vector3(0.1f, 0.9f, 0.1f))) < 1e-4f,
            "A1: fragment area equals source triangle area"
        );
    }

    private static void CheckTwoVoxels()
    {
        Triangle3 triangle = new Triangle3(
            new Vector3(0.2f, 0.1f, 0.1f),
            new Vector3(1.8f, 0.1f, 0.1f),
            new Vector3(0.5f, 0.9f, 0.1f)
        );

        CutResult result = CutTriangles(new[] { triangle }, DefaultEpsilon);

        Check(result.Fragments.Count == 2, "A2: two-voxel triangle yields 2 fragments");
        Check(result.Bridges.Count == 1, "A2: two-voxel triangle yields 1 bridge");

        bool areaMatches = Mathf.Abs(
            result.FragmentAreaSum - TriangleArea3(triangle.A, triangle.B, triangle.C)
        ) < 1e-4f;
        Check(areaMatches, "A2: fragment area sum equals source area");

        if (result.Bridges.Count == 1)
        {
            BridgeRecord bridge = result.Bridges[0];
            Check(bridge.HasCutVertex, "A2: bridge is generated by cutting");
            Check(bridge.VertexA != bridge.VertexB, "A2: bridge endpoints are distinct");
        }
    }

    private static void CheckThreeVoxels()
    {
        Triangle3 triangle = new Triangle3(
            new Vector3(0.2f, 0.1f, 0.1f),
            new Vector3(2.8f, 0.1f, 0.1f),
            new Vector3(1.5f, 0.9f, 0.1f)
        );

        CutResult result = CutTriangles(new[] { triangle }, DefaultEpsilon);

        Check(result.Fragments.Count == 3, "A3: three-voxel triangle yields 3 fragments");
        Check(result.Bridges.Count == 2, "A3: three-voxel triangle yields 2 bridges");
        Check(
            Mathf.Abs(result.FragmentAreaSum - TriangleArea3(triangle.A, triangle.B, triangle.C)) < 1e-4f,
            "A3: fragment area sum equals source area"
        );
    }

    private static void CheckTwoDimensionalCut()
    {
        Triangle3 triangle = new Triangle3(
            new Vector3(0.4f, 0.4f, 0.1f),
            new Vector3(2.8f, 0.4f, 0.1f),
            new Vector3(0.4f, 2.8f, 0.1f)
        );

        CutResult result = CutTriangles(new[] { triangle }, DefaultEpsilon);

        Check(result.Fragments.Count >= 3, "A4: X+Y multi-cell triangle yields multiple fragments");
        Check(
            Mathf.Abs(result.FragmentAreaSum - TriangleArea3(triangle.A, triangle.B, triangle.C)) < 1e-4f,
            "A4: multi-axis fragment area sum equals source area"
        );

        List<Vector3Int> voxels = new List<Vector3Int>();
        for (int i = 0; i < result.Fragments.Count; i++)
        {
            if (!voxels.Contains(result.Fragments[i].Voxel))
            {
                voxels.Add(result.Fragments[i].Voxel);
            }
        }

        Check(voxels.Count >= 3, "A4: multi-axis cut produces distinct voxel fragments");
    }

    private static void CheckPointTouch()
    {
        Triangle3 triangle = new Triangle3(
            new Vector3(0.1f, 0.1f, 0.1f),
            new Vector3(0.9f, 0.1f, 0.1f),
            new Vector3(1.0f, 0.9f, 0.1f)
        );

        CutResult result = CutTriangles(new[] { triangle }, DefaultEpsilon);

        Check(result.Fragments.Count == 1, "A5: point-touch triangle remains one fragment");
        Check(result.Bridges.Count == 0, "A5: point touch produces no bridge");
    }

    private static void CheckCoplanar()
    {
        Triangle3 triangle = new Triangle3(
            new Vector3(1f, 0.1f, 0.1f),
            new Vector3(1f, 0.9f, 0.1f),
            new Vector3(1f, 0.1f, 0.9f)
        );

        CutResult result = CutTriangles(new[] { triangle }, DefaultEpsilon);

        Check(result.Fragments.Count == 0, "A6: coplanar triangle produces no voxel fragment");
        Check(result.Bridges.Count == 0, "A6: coplanar triangle produces no bridge");
        Check(result.FaceFragments.Count == 1, "A6: coplanar triangle is stored as SpatialFace Base Geometry");
        Check(result.FaceFragments[0].Face.Key == new FaceId(0, 1, 0, 0).Key, "A6: coplanar face identity is (X,1,0,0)");
    }

    private static void CheckBaseEdgeOnBoundary()
    {
        Triangle3 triangle = new Triangle3(
            new Vector3(0.2f, 0.2f, 0.1f),
            new Vector3(1f, 0.2f, 0.1f),
            new Vector3(1f, 0.8f, 0.1f)
        );

        CutResult result = CutTriangles(new[] { triangle }, DefaultEpsilon);

        Check(result.Fragments.Count == 1, "A7: base-edge-on-boundary triangle stays in one voxel");
        Check(result.Bridges.Count == 0, "A7: base edge on boundary is not a bridge");
    }

    private static void CheckNegativeCoordinates()
    {
        Triangle3 triangle = new Triangle3(
            new Vector3(-1.2f, 0.1f, 0.1f),
            new Vector3(1.2f, 0.1f, 0.1f),
            new Vector3(-1.2f, 2.2f, 0.1f)
        );

        CutResult result = CutTriangles(new[] { triangle }, DefaultEpsilon);

        Check(
            result.Fragments.Count >= 3,
            "A8: negative-coordinate triangle yields multiple fragments"
        );

        bool hasNegativeVoxel = false;
        bool hasZeroVoxel = false;
        for (int i = 0; i < result.Fragments.Count; i++)
        {
            if (result.Fragments[i].Voxel.x < 0) hasNegativeVoxel = true;
            if (result.Fragments[i].Voxel.x == 0) hasZeroVoxel = true;
        }

        Check(hasNegativeVoxel && hasZeroVoxel, "A8: [c,c+1) contract maps negative and zero cells correctly");
        Check(
            Mathf.Abs(result.FragmentAreaSum - TriangleArea3(triangle.A, triangle.B, triangle.C)) < 1e-4f,
            "A8: negative-coordinate area sum matches source"
        );
    }

    private static void CheckWelding()
    {
        Triangle3 triangle = new Triangle3(
            new Vector3(0.2f, 0.1f, 0.1f),
            new Vector3(1.8f, 0.1f, 0.1f),
            new Vector3(0.5f, 0.9f, 0.1f)
        );

        // 两条 provenance 不同的 Triangle 共享同一个 crossing segment。
        CutResult result = CutTriangles(
            new[] { triangle, triangle },
            DefaultEpsilon
        );

        Check(result.Bridges.Count >= 2, "A3-weld: identical triangles produce independent bridge provenances");
        Check(result.Fragments.Count == 4, "A3-weld: each triangle contributes two fragments");

        bool allShared = result.Bridges.Count >= 2;
        if (allShared)
        {
            BridgeRecord bridgeA = result.Bridges[0];

            for (int i = 1; i < result.Bridges.Count; i++)
            {
                BridgeRecord bridge = result.Bridges[i];
                if (bridge.VertexA != bridgeA.VertexA ||
                    bridge.VertexB != bridgeA.VertexB)
                {
                    allShared = false;
                }
            }
        }

        Check(
            allShared,
            "A3-weld: identical spatial bridge points share the same VertexIds"
        );
    }

    private static void RunToleranceMatrix()
    {
        Triangle3 triangle = new Triangle3(
            new Vector3(0.2f, 0.1f, 0.1f),
            new Vector3(1.8f, 0.1f, 0.1f),
            new Vector3(0.5f, 0.9f, 0.1f)
        );

        float[] epsilons = { 1e-4f, 1e-5f, 1e-6f };

        for (int i = 0; i < epsilons.Length; i++)
        {
            CutResult result = CutTriangles(new[] { triangle }, epsilons[i]);
            float areaError = Mathf.Abs(
                result.FragmentAreaSum -
                TriangleArea3(triangle.A, triangle.B, triangle.C)
            );

            Debug.Log(
                "[VoxelAwareMeshGeometryPrototype] tolerance=" + epsilons[i] +
                " fragments=" + result.Fragments.Count +
                " bridges=" + result.Bridges.Count +
                " degenerate=" + result.DegeneratePolygonCount +
                " areaError=" + areaError.ToString("E4")
            );

            Check(
                result.Fragments.Count == 2 &&
                result.Bridges.Count == 1 &&
                areaError < 1e-4f,
                "A-tolerance " + epsilons[i] + ": stable two-voxel cut"
            );
        }
    }

    // =====================================================================
    // Prototype B
    // =====================================================================

    private sealed class Segment2
    {
        public Vector2 A;
        public Vector2 B;

        public Segment2(Vector2 a, Vector2 b)
        {
            A = a;
            B = b;
        }
    }

    private sealed class PlanarPartition
    {
        public List<Vector2> Nodes = new List<Vector2>();
        public List<Vector3Int> LoopTriangles = new List<Vector3Int>();
        public List<List<int>> Loops = new List<List<int>>();
        public int NodeCount
        {
            get { return Nodes.Count; }
        }
    }

    private static PlanarPartition PartitionFace(
        IReadOnlyList<Segment2> segments,
        float epsilon)
    {
        List<Segment2> allSegments = new List<Segment2>();
        allSegments.AddRange(segments);

        // square boundary
        allSegments.Add(new Segment2(new Vector2(0f, 0f), new Vector2(1f, 0f)));
        allSegments.Add(new Segment2(new Vector2(1f, 0f), new Vector2(1f, 1f)));
        allSegments.Add(new Segment2(new Vector2(1f, 1f), new Vector2(0f, 1f)));
        allSegments.Add(new Segment2(new Vector2(0f, 1f), new Vector2(0f, 0f)));

        Dictionary<int, List<float>> splits =
            new Dictionary<int, List<float>>();

        for (int i = 0; i < allSegments.Count; i++)
        {
            List<float> list = new List<float> { 0f, 1f };
            splits.Add(i, list);
        }

        for (int i = 0; i < allSegments.Count; i++)
        {
            for (int j = i + 1; j < allSegments.Count; j++)
            {
                if (TryGetSegmentIntersection(
                    allSegments[i],
                    allSegments[j],
                    epsilon,
                    out float t,
                    out float s))
                {
                    if (t > epsilon && t < 1f - epsilon)
                    {
                        splits[i].Add(t);
                    }

                    if (s > epsilon && s < 1f - epsilon)
                    {
                        splits[j].Add(s);
                    }
                }
            }
        }

        Dictionary<string, int> nodeIds = new Dictionary<string, int>();
        PlanarPartition partition = new PlanarPartition();
        List<Vector2Int> edges = new List<Vector2Int>();

        for (int i = 0; i < allSegments.Count; i++)
        {
            Segment2 segment = allSegments[i];
            splits[i].Sort();

            for (int k = 0; k + 1 < splits[i].Count; k++)
            {
                float t0 = splits[i][k];
                float t1 = splits[i][k + 1];
                if (t1 - t0 <= epsilon) continue;

                Vector2 a = Vector2.Lerp(segment.A, segment.B, t0);
                Vector2 b = Vector2.Lerp(segment.A, segment.B, t1);

                int idA = AddNode(partition, nodeIds, a);
                int idB = AddNode(partition, nodeIds, b);

                if (idA != idB)
                {
                    edges.Add(new Vector2Int(idA, idB));
                }
            }
        }

        // Build undirected adjacency.
        List<List<int>> adjacency = new List<List<int>>();
        for (int i = 0; i < partition.Nodes.Count; i++)
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

        // Traverse faces.
        List<List<int>> loops = new List<List<int>>();
        Dictionary<string, bool> usedDirectedEdges = new Dictionary<string, bool>();

        for (int a = 0; a < partition.Nodes.Count; a++)
        {
            List<int> neighbours = adjacency[a];
            for (int n = 0; n < neighbours.Count; n++)
            {
                int b = neighbours[n];
                string directedKey = a + "->" + b;
                if (usedDirectedEdges.ContainsKey(directedKey)) continue;

                List<int> loop = new List<int>();
                Dictionary<string, bool> localUsed =
                    new Dictionary<string, bool>();

                int currentFrom = a;
                int currentTo = b;

                while (true)
                {
                    string key = currentFrom + "->" + currentTo;
                    if (localUsed.ContainsKey(key)) break;

                    localUsed.Add(key, true);
                    usedDirectedEdges[key] = true;
                    loop.Add(currentFrom);

                    Vector2 pointFrom = partition.Nodes[currentFrom];
                    Vector2 pointTo = partition.Nodes[currentTo];
                    Vector2 reverseIncoming = pointFrom - pointTo;

                    int nextTo = -1;
                    float bestClockwiseAngle = -1f;

                    List<int> outgoing = adjacency[currentTo];
                    for (int i = 0; i < outgoing.Count; i++)
                    {
                        int candidate = outgoing[i];
                        if (candidate == currentFrom) continue;

                        Vector2 direction =
                            partition.Nodes[candidate] - pointTo;

                        float cross = reverseIncoming.x * direction.y -
                                       reverseIncoming.y * direction.x;
                        float dot = reverseIncoming.x * direction.x +
                                    reverseIncoming.y * direction.y;

                        // CCW angle from reverseIncoming。
                        float angle = Mathf.Atan2(cross, dot);
                        if (angle < 0f)
                        {
                            angle += Mathf.PI * 2f;
                        }

                        // 取 clockwise-immediate（CCW 前驱）：
                        // 即从 reverseIncoming 逆时针旋转角度最大的 outgoing edge。
                        if (angle > bestClockwiseAngle)
                        {
                            bestClockwiseAngle = angle;
                            nextTo = candidate;
                        }
                    }

                    if (nextTo < 0)
                    {
                        loop.Add(currentTo);
                        break;
                    }

                    currentFrom = currentTo;
                    currentTo = nextTo;
                }

                if (loop.Count >= 3)
                {
                    loops.Add(loop);
                }
            }
        }

        // Deduplicate the two opposite traversals of the same face.
        Dictionary<string, List<int>> canonicalLoops =
            new Dictionary<string, List<int>>();

        for (int i = 0; i < loops.Count; i++)
        {
            string key = CanonicalLoopKey(loops[i]);
            if (!canonicalLoops.ContainsKey(key))
            {
                canonicalLoops.Add(key, loops[i]);
            }
        }

        loops.Clear();
        foreach (KeyValuePair<string, List<int>> pair in canonicalLoops)
        {
            loops.Add(pair.Value);
        }

        // 去掉 graph 的外侧 face（square boundary 的另一侧）。
        // 它不表示单位 Face 内部 region。
        // Prototype 中 square outer loop 的 |area| ≈ 1。
        if (loops.Count > 1)
        {
            for (int i = loops.Count - 1; i >= 0; i--)
            {
                if (Mathf.Abs(LoopArea(loops[i], partition.Nodes)) > 0.99f)
                {
                    loops.RemoveAt(i);
                }
            }
        }

        for (int i = loops.Count - 1; i >= 0; i--)
        {
            if (Mathf.Abs(LoopArea(loops[i], partition.Nodes)) <= epsilon)
            {
                loops.RemoveAt(i);
            }
        }

        partition.Loops = loops;
        partition.LoopTriangles = TriangulateLoops(loops, partition.Nodes, epsilon);
        return partition;
    }

    private static int AddNode(
        PlanarPartition partition,
        Dictionary<string, int> nodeIds,
        Vector2 point)
    {
        string key =
            Mathf.RoundToInt(point.x / SnapScale) + ":" +
            Mathf.RoundToInt(point.y / SnapScale);

        if (nodeIds.TryGetValue(key, out int id)) return id;

        id = partition.Nodes.Count;
        partition.Nodes.Add(point);
        nodeIds.Add(key, id);
        return id;
    }

    private static int FindNode(PlanarPartition partition, Vector2 point)
    {
        string key =
            Mathf.RoundToInt(point.x / SnapScale) + ":" +
            Mathf.RoundToInt(point.y / SnapScale);

        for (int i = 0; i < partition.Nodes.Count; i++)
        {
            string nodeKey =
                Mathf.RoundToInt(partition.Nodes[i].x / SnapScale) + ":" +
                Mathf.RoundToInt(partition.Nodes[i].y / SnapScale);
            if (nodeKey == key) return i;
        }

        return -1;
    }

    private static bool IsSquareBoundaryLoop(
        List<int> loop,
        List<int> squareCornerIds)
    {
        if (loop.Count != squareCornerIds.Count) return false;

        bool[] matched = new bool[squareCornerIds.Count];
        for (int i = 0; i < loop.Count; i++)
        {
            for (int j = 0; j < squareCornerIds.Count; j++)
            {
                if (loop[i] == squareCornerIds[j])
                {
                    matched[j] = true;
                    break;
                }
            }
        }

        for (int i = 0; i < matched.Length; i++)
        {
            if (!matched[i]) return false;
        }

        return true;
    }

    private static string CanonicalLoopKey(List<int> loop)
    {
        int minIndex = 0;
        for (int i = 1; i < loop.Count; i++)
        {
            if (loop[i] < loop[minIndex])
            {
                minIndex = i;
            }
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

        StringBuilder builder = new StringBuilder();
        List<int> chosen = forwardSmaller ? forward : backward;
        for (int i = 0; i < chosen.Count; i++)
        {
            if (i > 0) builder.Append(',');
            builder.Append(chosen[i]);
        }

        return builder.ToString();
    }

    private static bool TryGetSegmentIntersection(
        Segment2 a,
        Segment2 b,
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

    private static List<Vector3Int> TriangulateLoops(
        List<List<int>> loops,
        List<Vector2> nodes,
        float epsilon)
    {
        List<Vector3Int> triangles = new List<Vector3Int>();

        for (int i = 0; i < loops.Count; i++)
        {
            List<int> loop = new List<int>(loops[i]);

            if (Mathf.Abs(LoopArea(loop, nodes)) <= epsilon) continue;

            bool ccw = LoopArea(loop, nodes) > 0f;
            if (!ccw) loop.Reverse();

            while (loop.Count > 3)
            {
                bool earFound = false;

                for (int v = 0; v < loop.Count; v++)
                {
                    int prev = loop[(v - 1 + loop.Count) % loop.Count];
                    int current = loop[v];
                    int next = loop[(v + 1) % loop.Count];

                    Vector2 p0 = nodes[prev];
                    Vector2 p1 = nodes[current];
                    Vector2 p2 = nodes[next];

                    if (Cross2(p0, p1, p2) <= epsilon)
                    {
                        continue;
                    }

                    bool contains = false;
                    for (int k = 0; k < loop.Count; k++)
                    {
                        int point = loop[k];
                        if (point == prev || point == current || point == next)
                        {
                            continue;
                        }

                        if (PointInTriangle(
                            nodes[point],
                            p0,
                            p1,
                            p2,
                            epsilon))
                        {
                            contains = true;
                            break;
                        }
                    }

                    if (contains) continue;

                    triangles.Add(new Vector3Int(prev, current, next));
                    loop.RemoveAt(v);
                    earFound = true;
                    break;
                }

                if (!earFound) break;
            }

            if (loop.Count == 3)
            {
                triangles.Add(new Vector3Int(loop[0], loop[1], loop[2]));
            }
        }

        return triangles;
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
        float cross0 = Cross2(a, b, p);
        float cross1 = Cross2(b, c, p);
        float cross2 = Cross2(c, a, p);

        bool hasNegative = cross0 < -epsilon || cross1 < -epsilon || cross2 < -epsilon;
        bool hasPositive = cross0 > epsilon || cross1 > epsilon || cross2 > epsilon;

        return !(hasNegative && hasPositive);
    }

    private static bool SegmentIntersectsTriangleInterior(
        Segment2 segment,
        Vector2 a,
        Vector2 b,
        Vector2 c,
        float epsilon)
    {
        for (int i = 0; i < 3; i++)
        {
            Vector2 p0 = i == 0 ? a : i == 1 ? b : c;
            Vector2 p1 = i == 0 ? b : i == 1 ? c : a;

            Segment2 edge = new Segment2(p0, p1);
            if (TryGetSegmentIntersection(segment, edge, epsilon,
                out float t, out float s))
            {
                if (t > epsilon && t < 1f - epsilon &&
                    s > -epsilon && s < 1f + epsilon)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static Vector2 LoopCentroid(List<int> loop, List<Vector2> nodes)
    {
        Vector2 sum = Vector2.zero;
        for (int i = 0; i < loop.Count; i++)
        {
            sum += nodes[loop[i]];
        }

        return sum / Mathf.Max(1, loop.Count);
    }

    private static void RunPrototypeB()
    {
        CheckEmptyFace();
        CheckSingleBridge();
        CheckCrossBridge();
        CheckDiagonalBridge();
        CheckMixedBridgeAndBaseEdge();
        CheckCoplanarCoverageRegion();
        CheckStateMatrix();
        CheckMidpointSampling();
        CheckHiddenFaceTopology();
    }

    private static void LogPartition(string label, PlanarPartition partition)
    {
        StringBuilder builder = new StringBuilder();
        builder.Append("[PartitionDebug] ");
        builder.Append(label);
        builder.Append(" nodes=").Append(partition.Nodes.Count);
        builder.Append(" loops=").Append(partition.Loops.Count);
        builder.Append(" triangles=").Append(partition.LoopTriangles.Count);
        builder.Append(" area=").Append(PartitionArea(partition));

        for (int i = 0; i < partition.Loops.Count; i++)
        {
            builder.Append(" | loop").Append(i).Append("=[");
            List<int> loop = partition.Loops[i];
            for (int j = 0; j < loop.Count; j++)
            {
                if (j > 0) builder.Append(',');
                builder.Append(loop[j]);
            }
            builder.Append("] area=").Append(LoopArea(loop, partition.Nodes));
        }

        Debug.Log(builder.ToString());
    }

    private static void CheckEmptyFace()
    {
        PlanarPartition partition = PartitionFace(
            new Segment2[0],
            DefaultEpsilon
        );

        LogPartition("empty", partition);
        Check(partition.Loops.Count == 1, "B1: empty SpatialFace has 1 region");
        Check(partition.LoopTriangles.Count >= 1, "B1: empty SpatialFace triangulates");
        Check(PartitionArea(partition) > 0.99f && PartitionArea(partition) < 1.01f, "B1: partition area covers the unit face");
    }

    private static void CheckSingleBridge()
    {
        PlanarPartition partition = PartitionFace(
            new[]
            {
                new Segment2(new Vector2(0f, 0.5f), new Vector2(1f, 0.5f))
            },
            DefaultEpsilon
        );

        LogPartition("one-bridge", partition);
        Check(partition.Loops.Count == 2, "B2: one bridge creates 2 regions");
        Check(PartitionArea(partition) > 0.99f && PartitionArea(partition) < 1.01f, "B2: partition area has no gap/overlap");
    }

    private static void CheckCrossBridge()
    {
        PlanarPartition partition = PartitionFace(
            new[]
            {
                new Segment2(new Vector2(0f, 0.5f), new Vector2(1f, 0.5f)),
                new Segment2(new Vector2(0.5f, 0f), new Vector2(0.5f, 1f))
            },
            DefaultEpsilon
        );

        LogPartition("cross", partition);
        Check(partition.Loops.Count == 4, "B3: cross bridges create 4 regions");
        Check(PartitionArea(partition) > 0.99f && PartitionArea(partition) < 1.01f, "B3: cross partition has no gap/overlap");
    }

    private static void CheckDiagonalBridge()
    {
        PlanarPartition partition = PartitionFace(
            new[]
            {
                new Segment2(new Vector2(0f, 0f), new Vector2(1f, 1f))
            },
            DefaultEpsilon
        );

        LogPartition("diagonal", partition);
        Check(partition.Loops.Count == 2, "B4: diagonal bridge creates 2 regions");
        Check(PartitionArea(partition) > 0.99f && PartitionArea(partition) < 1.01f, "B4: diagonal partition has no gap/overlap");
    }

    private static void CheckMixedBridgeAndBaseEdge()
    {
        PlanarPartition partition = PartitionFace(
            new[]
            {
                new Segment2(new Vector2(0f, 0.4f), new Vector2(1f, 0.4f)),
                new Segment2(new Vector2(0.5f, 0f), new Vector2(0.5f, 0.4f))
            },
            DefaultEpsilon
        );

        LogPartition("mixed", partition);
        Check(partition.Loops.Count == 3, "B5: bridge + natural base edge partition into 3 regions");
        Check(PartitionArea(partition) > 0.99f && PartitionArea(partition) < 1.01f, "B5: mixed partition has no gap/overlap");
    }

    private static void CheckCoplanarCoverageRegion()
    {
        // 对角线把单位 Face 分成两个区域；其中一个区域代表被
        // coplanar Base Triangle（对角线 + 两条 square boundary）覆盖。
        PlanarPartition partition = PartitionFace(
            new[]
            {
                new Segment2(new Vector2(0f, 0f), new Vector2(1f, 1f))
            },
            DefaultEpsilon
        );

        LogPartition("coplanar-triangle", partition);
        Check(partition.Loops.Count == 2, "B6: coplanar base triangle partitions face into covered + uncovered");
        Check(PartitionArea(partition) > 0.99f && PartitionArea(partition) < 1.01f, "B6: covered/uncovered regions cover the face exactly once");
    }

    private static void CheckStateMatrix()
    {
        bool[] negativeAir = { false, true, true, false };
        bool[] positiveAir = { false, true, false, true };
        bool[] expectHidden = { false, false, true, true };

        for (int i = 0; i < 4; i++)
        {
            int hiddenRegions = EvaluateHiddenFaceForState(
                negativeAir[i],
                positiveAir[i]
            );

            bool shouldEvaluate = negativeAir[i] != positiveAir[i];
            Check(
                shouldEvaluate ? hiddenRegions > 0 : hiddenRegions == 0,
                "B3-state matrix " +
                (negativeAir[i] ? "Air" : "Solid") + "/" +
                (positiveAir[i] ? "Air" : "Solid") +
                " -> " + (shouldEvaluate ? "evaluate" : "no hidden face")
            );
        }
    }

    private static int EvaluateHiddenFaceForState(bool negativeAir, bool positiveAir)
    {
        if (negativeAir == positiveAir) return 0;

        PlanarPartition partition = PartitionFace(
            new Segment2[0],
            DefaultEpsilon
        );

        return partition.Loops.Count;
    }

    private static void CheckMidpointSampling()
    {
        // Natural boundary = 水平 stripe v ∈ [0.4, 0.6]。
        // 先不插入任何 segment：square centroid 在 stripe 内，
        // 但真实 majority 在 stripe 外，构造 midpoint sampling 反例。
        PlanarPartition badPartition = PartitionFace(
            new Segment2[0],
            DefaultEpsilon
        );

        bool badMidpointReliable = true;
        for (int i = 0; i < badPartition.Loops.Count; i++)
        {
            Vector2 centroid = LoopCentroid(
                badPartition.Loops[i],
                badPartition.Nodes
            );

            bool midpointSolid = IsSolidMask(centroid);
            bool exactSolid = ExactLoopMajority(
                badPartition.Loops[i],
                badPartition.Nodes
            );

            if (midpointSolid != exactSolid)
            {
                badMidpointReliable = false;
            }
        }

        Check(
            !badMidpointReliable,
            "B4-counterexample: midpoint sampling disagrees when natural boundary is not inserted"
        );

        // 插入 stripe 的上下边界后，每个 region 不再跨越 natural boundary。
        PlanarPartition goodPartition = PartitionFace(
            new[]
            {
                new Segment2(new Vector2(0f, 0.4f), new Vector2(1f, 0.4f)),
                new Segment2(new Vector2(0f, 0.6f), new Vector2(1f, 0.6f))
            },
            DefaultEpsilon
        );

        bool goodMidpointReliable = true;
        for (int i = 0; i < goodPartition.Loops.Count; i++)
        {
            Vector2 centroid = LoopCentroid(
                goodPartition.Loops[i],
                goodPartition.Nodes
            );

            bool midpointSolid = IsSolidMask(centroid);
            bool exactSolid = ExactLoopMajority(
                goodPartition.Loops[i],
                goodPartition.Nodes
            );

            if (midpointSolid != exactSolid)
            {
                goodMidpointReliable = false;
            }
        }

        Check(
            goodMidpointReliable,
            "B4: midpoint sampling is reliable after inserting every natural boundary segment"
        );
    }

    private static bool IsSolidMask(Vector2 p)
    {
        return p.y > 0.4f && p.y < 0.6f;
    }

    private static bool ExactLoopMajority(List<int> loop, List<Vector2> nodes)
    {
        const int samples = 200;
        int solidCount = 0;
        int totalCount = 0;

        Vector2 min = new Vector2(0f, 0f);
        Vector2 max = new Vector2(1f, 1f);

        for (int i = 0; i < samples; i++)
        {
            for (int j = 0; j < samples; j++)
            {
                Vector2 point = new Vector2(
                    min.x + (i + 0.5f) / samples,
                    min.y + (j + 0.5f) / samples
                );

                if (!PointInLoop(point, loop, nodes)) continue;

                totalCount++;
                if (IsSolidMask(point)) solidCount++;
            }
        }

        if (totalCount == 0) return false;
        return solidCount * 2 > totalCount;
    }

    private static bool PointInLoop(Vector2 point, List<int> loop, List<Vector2> nodes)
    {
        bool inside = false;

        for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
        {
            Vector2 a = nodes[loop[i]];
            Vector2 b = nodes[loop[j]];

            bool intersects = (a.y > point.y) != (b.y > point.y);
            if (intersects)
            {
                float x = (b.x - a.x) * (point.y - a.y) /
                          (b.y - a.y) + a.x;
                if (point.x < x) inside = !inside;
            }
        }

        return inside;
    }

    private static void CheckHiddenFaceTopology()
    {
        List<Segment2> segments = new List<Segment2>
        {
            new Segment2(new Vector2(0f, 0.5f), new Vector2(1f, 0.5f))
        };

        PlanarPartition partition = PartitionFace(
            segments,
            DefaultEpsilon
        );

        LogPartition("hidden-topology", partition);
        int generatedRegions = 0;
        List<Vector3Int> hiddenTriangles = new List<Vector3Int>();

        for (int i = 0; i < partition.Loops.Count; i++)
        {
            Vector2 centroid = LoopCentroid(
                partition.Loops[i],
                partition.Nodes
            );

            if (centroid.y > 0.5f)
            {
                generatedRegions++;
                TriangulateLoop(
                    partition.Loops[i],
                    partition.Nodes,
                    hiddenTriangles
                );
            }
        }

        Check(generatedRegions == 1, "B5: one bridge generates exactly one hidden-face region");
        Check(hiddenTriangles.Count >= 1, "B5: hidden face region is triangulated");

        int zeroAreaCount = 0;
        int crossingCount = 0;
        Dictionary<string, bool> triangleKeys = new Dictionary<string, bool>();

        for (int i = 0; i < hiddenTriangles.Count; i++)
        {
            Vector3Int triangle = hiddenTriangles[i];
            Vector2 a = partition.Nodes[triangle.x];
            Vector2 b = partition.Nodes[triangle.y];
            Vector2 c = partition.Nodes[triangle.z];

            if (Mathf.Abs(Cross2(a, b, c)) <= DefaultEpsilon)
            {
                zeroAreaCount++;
            }

            Segment2 bridge = segments[0];
            if (SegmentIntersectsTriangleInterior(
                bridge,
                a,
                b,
                c,
                DefaultEpsilon))
            {
                crossingCount++;
            }

            string key = MakeTriangleKey(triangle.x, triangle.y, triangle.z);
            if (!triangleKeys.ContainsKey(key))
            {
                triangleKeys.Add(key, true);
            }
        }

        Check(zeroAreaCount == 0, "B5: hidden face has no zero-area triangles");
        Check(crossingCount == 0, "B5: hidden face triangles do not cross the bridge");
        Check(triangleKeys.Count == hiddenTriangles.Count, "B5: hidden face contains no duplicate triangles");
    }

    private static void TriangulateLoop(
        List<int> loop,
        List<Vector2> nodes,
        List<Vector3Int> output)
    {
        List<int> poly = new List<int>(loop);
        if (LoopArea(poly, nodes) < 0f) poly.Reverse();

        while (poly.Count > 3)
        {
            bool removed = false;

            for (int v = 0; v < poly.Count; v++)
            {
                int prev = poly[(v - 1 + poly.Count) % poly.Count];
                int current = poly[v];
                int next = poly[(v + 1) % poly.Count];

                if (Cross2(nodes[prev], nodes[current], nodes[next]) <= DefaultEpsilon)
                {
                    continue;
                }

                bool contains = false;
                for (int k = 0; k < poly.Count; k++)
                {
                    int p = poly[k];
                    if (p == prev || p == current || p == next) continue;
                    if (PointInTriangle(nodes[p], nodes[prev], nodes[current], nodes[next], DefaultEpsilon))
                    {
                        contains = true;
                        break;
                    }
                }

                if (contains) continue;

                output.Add(new Vector3Int(prev, current, next));
                poly.RemoveAt(v);
                removed = true;
                break;
            }

            if (!removed) break;
        }

        if (poly.Count == 3)
        {
            output.Add(new Vector3Int(poly[0], poly[1], poly[2]));
        }
    }

    private static string MakeTriangleKey(int a, int b, int c)
    {
        int min = Mathf.Min(Mathf.Min(a, b), c);
        int max = Mathf.Max(Mathf.Max(a, b), c);
        int mid = a + b + c - min - max;
        return min + ":" + mid + ":" + max;
    }

    private static float PartitionArea(PlanarPartition partition)
    {
        float area = 0f;
        for (int i = 0; i < partition.LoopTriangles.Count; i++)
        {
            Vector3Int triangle = partition.LoopTriangles[i];
            Vector2 a = partition.Nodes[triangle.x];
            Vector2 b = partition.Nodes[triangle.y];
            Vector2 c = partition.Nodes[triangle.z];
            area += Mathf.Abs(Cross2(a, b, c)) * 0.5f;
        }

        return area;
    }

    private static void LogSummary()
    {
        Debug.Log(
            "[VoxelAwareMeshGeometryPrototypeValidation] ===== SUMMARY =====\n" +
            "Checks = " + checks + "\n" +
            "Failures = " + failures + "\n" +
            "Prototype A = " + (failures == 0 ? "PASS" : "FAIL") + "\n" +
            "Prototype B = " + (failures == 0 ? "PASS" : "FAIL") + "\n" +
            "Final = " + (failures == 0 ? "PASS" : "FAIL")
        );
    }
}
