using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Marching Cubes mesh generator.
/// 根据 DensityField 生成等值面 Mesh；Mesh 只是 DensityField 的可视化结果。
/// 这里使用运行时生成的 Marching Cubes 三角化表，避免在源码中手抄巨大的 256 项 triTable。
/// </summary>
public static class TerrainMeshGenerator
{
    private static readonly int[,] EdgeCorners = new int[12, 2]
    {
        {0, 1}, {1, 2}, {2, 3}, {3, 0},
        {4, 5}, {5, 6}, {6, 7}, {7, 4},
        {0, 4}, {1, 5}, {2, 6}, {3, 7}
    };

    // 每个面的 4 条边，按绕面顺序排列。
    private static readonly int[][] FaceEdges = new int[][]
    {
        new[] {0, 1, 2, 3},    // bottom
        new[] {4, 5, 6, 7},    // top
        new[] {0, 9, 4, 8},    // front
        new[] {2, 10, 6, 11},  // back
        new[] {3, 11, 7, 8},   // left
        new[] {1, 10, 5, 9}    // right
    };

    // 8 个角的编号顺序必须与 EdgeCorners / FaceEdges 保持一致。
    private static readonly Vector3Int[] CornerOffsets = new Vector3Int[8]
    {
        new Vector3Int(0, 0, 0),
        new Vector3Int(1, 0, 0),
        new Vector3Int(1, 0, 1),
        new Vector3Int(0, 0, 1),
        new Vector3Int(0, 1, 0),
        new Vector3Int(1, 1, 0),
        new Vector3Int(1, 1, 1),
        new Vector3Int(0, 1, 1)
    };

    private static readonly int[][] TriangleTable = BuildTriangleTable();

    public static Mesh Generate(DensityField field, float isoLevel = 0f)
    {
        return Generate(field, null, isoLevel);
    }

    public static Mesh Generate(
        DensityField field,
        MaterialField materialField,
        float isoLevel = 0f)
    {
        Mesh mesh = new Mesh();
        mesh.name = "Density Terrain Mesh";
        mesh.hideFlags = HideFlags.DontSave;
        mesh.indexFormat = IndexFormat.UInt32;

        List<Vector3> vertices = new List<Vector3>();
        List<int>[] subMeshTriangles = new List<int>[3];
        for (int i = 0; i < subMeshTriangles.Length; i++)
        {
            subMeshTriangles[i] = new List<int>();
        }

        int processedCubes = 0;
        int grassTriangles = 0;
        int dirtTriangles = 0;
        int stoneTriangles = 0;

        float[] cornerDensities = new float[8];
        Vector3[] cornerPositions = new Vector3[8];
        MaterialType[] cornerMaterials = new MaterialType[8];
        Vector3[] edgeVertices = new Vector3[12];

        for (int x = 0; x < field.ResolutionX; x++)
        {
            for (int y = 0; y < field.ResolutionY; y++)
            {
                for (int z = 0; z < field.ResolutionZ; z++)
                {
                    int cubeIndex = 0;

                    for (int c = 0; c < 8; c++)
                    {
                        Vector3Int offset = CornerOffsets[c];
                        int sx = x + offset.x;
                        int sy = y + offset.y;
                        int sz = z + offset.z;

                        float density = field[sx, sy, sz];
                        cornerDensities[c] = density;
                        cornerPositions[c] = field.GetWorldPosition(sx, sy, sz);

                        if (materialField != null)
                        {
                            cornerMaterials[c] = materialField[sx, sy, sz];
                        }
                        else
                        {
                            cornerMaterials[c] = density > isoLevel
                                ? MaterialType.Stone
                                : MaterialType.Air;
                        }

                        if (density > isoLevel)
                        {
                            cubeIndex |= (1 << c);
                        }
                    }

                    if (cubeIndex == 0 || cubeIndex == 255) continue;

                    processedCubes++;

                    for (int e = 0; e < 12; e++)
                    {
                        int a = EdgeCorners[e, 0];
                        int b = EdgeCorners[e, 1];
                        edgeVertices[e] = InterpolateEdge(
                            cornerPositions[a],
                            cornerPositions[b],
                            cornerDensities[a],
                            cornerDensities[b],
                            isoLevel
                        );
                    }

                    Vector3 gradient = EstimateGradient(cornerDensities, field);
                    Vector3 desiredNormal = gradient.sqrMagnitude > 1e-10f
                        ? -gradient.normalized
                        : Vector3.zero;

                    int[] cellTriangles = TriangleTable[cubeIndex];
                    for (int i = 0; i < cellTriangles.Length; i += 3)
                    {
                        Vector3 v0 = edgeVertices[cellTriangles[i]];
                        Vector3 v1 = edgeVertices[cellTriangles[i + 1]];
                        Vector3 v2 = edgeVertices[cellTriangles[i + 2]];

                        if (desiredNormal != Vector3.zero)
                        {
                            Vector3 faceNormal = Vector3.Cross(v1 - v0, v2 - v0);
                            if (Vector3.Dot(faceNormal, desiredNormal) < 0f)
                            {
                                Vector3 temp = v1;
                                v1 = v2;
                                v2 = temp;
                            }
                        }

                        Vector3 triangleCenter = (v0 + v1 + v2) / 3f;
                        MaterialType triangleMaterial = ResolveTriangleMaterial(
                            triangleCenter,
                            cornerPositions,
                            cornerDensities,
                            cornerMaterials,
                            isoLevel
                        );

                        int subMeshIndex = GetSubMeshIndex(triangleMaterial);
                        if (subMeshIndex == 0) grassTriangles++;
                        else if (subMeshIndex == 1) dirtTriangles++;
                        else stoneTriangles++;

                        int baseIndex = vertices.Count;
                        vertices.Add(v0);
                        vertices.Add(v1);
                        vertices.Add(v2);

                        List<int> subMesh = subMeshTriangles[subMeshIndex];
                        subMesh.Add(baseIndex);
                        subMesh.Add(baseIndex + 1);
                        subMesh.Add(baseIndex + 2);
                    }
                }
            }
        }

        mesh.SetVertices(vertices);
        mesh.subMeshCount = 3;
        mesh.SetTriangles(subMeshTriangles[0], 0);
        mesh.SetTriangles(subMeshTriangles[1], 1);
        mesh.SetTriangles(subMeshTriangles[2], 2);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        Debug.Log(
            "[TerrainMeshGenerator]\n" +
            $"Processed Cubes = {processedCubes}\n" +
            $"Generated Triangles = {grassTriangles + dirtTriangles + stoneTriangles}\n" +
            $"Grass Triangles = {grassTriangles}\n" +
            $"Dirt Triangles = {dirtTriangles}\n" +
            $"Stone Triangles = {stoneTriangles}"
        );

        return mesh;
    }

    /// <summary>
    /// 为三角形选择一个主要 Material。
    /// 第一版规则：找离三角形中心最近的实体采样点，使用该点的 MaterialField 值。
    /// 不做复杂材料插值，仅做近邻采样。
    /// </summary>
    private static MaterialType ResolveTriangleMaterial(
        Vector3 triangleCenter,
        Vector3[] cornerPositions,
        float[] cornerDensities,
        MaterialType[] cornerMaterials,
        float isoLevel)
    {
        int bestIndex = -1;
        float bestDistance = float.MaxValue;

        for (int c = 0; c < 8; c++)
        {
            if (cornerDensities[c] <= isoLevel) continue;

            float distance = (cornerPositions[c] - triangleCenter).sqrMagnitude;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = c;
            }
        }

        if (bestIndex >= 0)
        {
            return cornerMaterials[bestIndex];
        }

        return MaterialType.Stone;
    }

    private static int GetSubMeshIndex(MaterialType material)
    {
        switch (material)
        {
            case MaterialType.Grass:
                return 0;

            case MaterialType.Dirt:
                return 1;

            default:
                return 2;
        }
    }

    private static Vector3 InterpolateEdge(
        Vector3 a,
        Vector3 b,
        float densityA,
        float densityB,
        float isoLevel)
    {
        float denominator = densityB - densityA;
        float t = Mathf.Abs(denominator) < 1e-6f
            ? 0.5f
            : Mathf.Clamp01((isoLevel - densityA) / denominator);

        return Vector3.Lerp(a, b, t);
    }

    private static Vector3 EstimateGradient(float[] density, DensityField field)
    {
        // 8 个角编号：
        // 0(0,0,0) 1(1,0,0) 2(1,0,1) 3(0,0,1)
        // 4(0,1,0) 5(1,1,0) 6(1,1,1) 7(0,1,1)
        float dx = ((density[1] + density[2] + density[5] + density[6])
                    - (density[0] + density[3] + density[4] + density[7]))
                   * 0.25f / field.CellSizeX;

        float dy = ((density[4] + density[5] + density[6] + density[7])
                    - (density[0] + density[1] + density[2] + density[3]))
                   * 0.25f / field.CellSizeY;

        float dz = ((density[2] + density[3] + density[6] + density[7])
                    - (density[0] + density[1] + density[4] + density[5]))
                   * 0.25f / field.CellSizeZ;

        return new Vector3(dx, dy, dz);
    }

    private static int[][] BuildTriangleTable()
    {
        int[][] table = new int[256][];

        for (int mask = 0; mask < 256; mask++)
        {
            table[mask] = BuildCaseTriangles(mask);
        }

        return table;
    }

    private static int[] BuildCaseTriangles(int mask)
    {
        bool[] inside = new bool[8];
        for (int i = 0; i < 8; i++)
        {
            inside[i] = ((mask >> i) & 1) != 0;
        }

        bool[] edgeCross = new bool[12];
        for (int e = 0; e < 12; e++)
        {
            edgeCross[e] = inside[EdgeCorners[e, 0]] != inside[EdgeCorners[e, 1]];
        }

        List<int> segmentA = new List<int>();
        List<int> segmentB = new List<int>();

        for (int f = 0; f < FaceEdges.Length; f++)
        {
            int[] face = FaceEdges[f];
            List<int> crossedEdges = new List<int>(4);

            for (int i = 0; i < face.Length; i++)
            {
                if (edgeCross[face[i]])
                {
                    crossedEdges.Add(face[i]);
                }
            }

            if (crossedEdges.Count == 0) continue;

            if (crossedEdges.Count == 2)
            {
                segmentA.Add(crossedEdges[0]);
                segmentB.Add(crossedEdges[1]);
            }
            else if (crossedEdges.Count == 4)
            {
                // 歧义面：统一固定配对，保证相邻 cube 使用相同连接规则。
                segmentA.Add(crossedEdges[0]);
                segmentB.Add(crossedEdges[1]);
                segmentA.Add(crossedEdges[2]);
                segmentB.Add(crossedEdges[3]);
            }
        }

        List<int>[] nodeSegments = new List<int>[12];
        for (int i = 0; i < 12; i++)
        {
            nodeSegments[i] = new List<int>();
        }

        for (int s = 0; s < segmentA.Count; s++)
        {
            nodeSegments[segmentA[s]].Add(s);
            nodeSegments[segmentB[s]].Add(s);
        }

        bool[] used = new bool[segmentA.Count];
        List<int> triangles = new List<int>();

        for (int s0 = 0; s0 < segmentA.Count; s0++)
        {
            if (used[s0]) continue;

            int start = segmentA[s0];
            int current = start;
            int previous = -1;
            List<int> loop = new List<int>();

            while (true)
            {
                loop.Add(current);

                int next = -1;
                List<int> candidates = nodeSegments[current];
                for (int i = 0; i < candidates.Count; i++)
                {
                    int s = candidates[i];
                    if (s != previous && !used[s])
                    {
                        next = s;
                        break;
                    }
                }

                if (next < 0) break;

                used[next] = true;
                current = segmentA[next] == current ? segmentB[next] : segmentA[next];
                previous = next;

                if (current == start) break;
            }

            if (loop.Count < 3) continue;

            for (int i = 1; i < loop.Count - 1; i++)
            {
                triangles.Add(loop[0]);
                triangles.Add(loop[i]);
                triangles.Add(loop[i + 1]);
            }
        }

        return triangles.ToArray();
    }
}
