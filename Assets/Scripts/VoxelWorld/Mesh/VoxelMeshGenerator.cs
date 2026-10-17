using System.Collections.Generic;
using UnityEngine; // 仅使用 Vector2 / Vector3 数学结构体；不引用 Mesh / Renderer / Transform / MonoBehaviour

namespace VoxelWorld.Mesh
{
    /// <summary>
    /// Cube Surface Meshing（Specifications/World/VoxelWorld_Mesh.md §3/§4/§5 的第一阶段实现）。
    ///
    /// 契约（严格按 Mesh 规范）：
    ///   · 输入只能是 <see cref="VoxelChunk"/>（不读 VoxelWorld、不读相邻 Chunk 数据、
    ///     不读 Transform / Renderer / 时间 / 全局可变状态）；
    ///   · 输出只能是 <see cref="VoxelMeshData"/>（纯数据，不含 UnityEngine.Mesh）；
    ///   · **不修改** VoxelChunk（只调用 GetVoxel/IsLocalCoordinateValid 等只读 API，
    ///     不调用 SetVoxel/MarkDirty/ClearDirty）；
    ///   · 确定性：固定遍历顺序 + 固定方向顺序 + 固定顶点/三角形顺序 ⇒ 同输入必得同输出。
    ///
    /// 规则：
    ///   solid voxel 的 6 个方向：
    ///     邻居 Air（含 Chunk 外，规范 §4 B1）⇒ 生成 1 个 quad（2 三角形）
    ///     邻居 Solid                        ⇒ 不生成
    ///   Air voxel 永不生成几何。
    ///
    /// 几何构造（规范 §3.1）：
    ///   面平面 = 正方向取该轴 +1 平面 / 负方向取 +0 平面；
    ///   切向轴序 (t1,t2) 满足 cross(e_t1, e_t2) = d；
    ///   P0 = 面矩形上 t1/t2 皆取最小值的角点；
    ///   v0=P0, v1=P0+e_t1, v2=P0+e_t1+e_t2, v3=P0+e_t2（从外侧看 CCW）；
    ///   三角形固定为 (v0,v1,v2) 与 (v0,v2,v3)；
    ///   法线 = d；UV = (0,0),(1,0),(1,1),(0,1)；
    ///   submesh = (int)VoxelType - 1（Grass=0, Dirt=1, Stone=2）。
    /// </summary>
    public static class VoxelMeshGenerator
    {
        /// <summary>固定方向顺序（规范 §3.1 切向表的行序）：+X, −X, +Y, −Y, +Z, −Z。</summary>
        private static readonly int[] DirectionAxis = { 0, 0, 1, 1, 2, 2 };

        private static readonly int[] DirectionSign = { 1, -1, 1, -1, 1, -1 };

        /// <summary>切向轴 1：+X:(Y) −X:(Z) +Y:(Z) −Y:(X) +Z:(X) −Z:(Y)。</summary>
        private static readonly int[] Tangent1Axis = { 1, 2, 2, 0, 0, 1 };

        /// <summary>切向轴 2：+X:(Z) −X:(Y) +Y:(X) −Y:(Z) +Z:(Y) −Z:(X)。</summary>
        private static readonly int[] Tangent2Axis = { 2, 1, 0, 2, 1, 0 };

        private static readonly Vector3[] DirectionNormals =
        {
            Vector3.right,   // +X
            Vector3.left,    // −X
            Vector3.up,      // +Y
            Vector3.down,    // −Y
            Vector3.forward, // +Z
            Vector3.back     // −Z
        };

        private static readonly Vector2[] QuadUVs =
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(1f, 1f),
            new Vector2(0f, 1f)
        };

        /// <summary>方向数量（= 立方体面数）。</summary>
        public const int DirectionCount = 6;

        /// <summary>
        /// 生成一个 Chunk 的 Cube Surface Mesh 数据。
        /// 只读取 <paramref name="chunk"/>，不修改它，也不读取任何其它状态。
        /// </summary>
        public static VoxelMeshData Generate(VoxelChunk chunk)
        {
            if (chunk == null)
            {
                return VoxelMeshData.CreateEmpty();
            }

            List<Vector3> vertices = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();

            List<int>[] submeshes = new List<int>[VoxelMeshData.SubmeshCount];
            for (int i = 0; i < submeshes.Length; i++)
            {
                submeshes[i] = new List<int>();
            }

            int size = VoxelChunk.ChunkSize;

            // 固定遍历顺序：localX → localY → localZ（与 VoxelCore/Generation 的循环顺序一致）
            for (int localX = 0; localX < size; localX++)
            {
                for (int localY = 0; localY < size; localY++)
                {
                    for (int localZ = 0; localZ < size; localZ++)
                    {
                        Voxel voxel = chunk.GetVoxel(localX, localY, localZ);
                        if (!voxel.IsSolid()) continue;

                        int submesh = (int)voxel.Type - 1;
                        if (submesh < 0 || submesh >= VoxelMeshData.SubmeshCount)
                        {
                            // 数据层只允许 Grass/Dirt/Stone 产生几何（Air 已在上面排除）
                            continue;
                        }

                        for (int d = 0; d < DirectionCount; d++)
                        {
                            if (HasSolidNeighbour(
                                chunk, localX, localY, localZ, d))
                            {
                                continue;
                            }

                            AddQuad(
                                vertices, normals, uvs, submeshes[submesh],
                                localX, localY, localZ, d);
                        }
                    }
                }
            }

            int[][] triangles = new int[VoxelMeshData.SubmeshCount][];
            for (int i = 0; i < submeshes.Length; i++)
            {
                triangles[i] = submeshes[i].ToArray();
            }

            return new VoxelMeshData(
                vertices.ToArray(),
                normals.ToArray(),
                uvs.ToArray(),
                triangles);
        }

        /// <summary>
        /// 该方向邻居是否为实体。
        /// Chunk 外（越界）按规范 §4 B1 视为 Air ⇒ 返回 false（⇒ 生成边界面）。
        /// </summary>
        private static bool HasSolidNeighbour(
            VoxelChunk chunk,
            int localX,
            int localY,
            int localZ,
            int directionIndex)
        {
            int axis = DirectionAxis[directionIndex];
            int sign = DirectionSign[directionIndex];

            int nx = localX;
            int ny = localY;
            int nz = localZ;

            if (axis == 0) nx += sign;
            else if (axis == 1) ny += sign;
            else nz += sign;

            // 越界 = Chunk 外 = Air（不做跨 Chunk 查询，保持 Chunk 独立 Meshing）
            if (!VoxelChunk.IsLocalCoordinateValid(nx, ny, nz)) return false;

            return chunk.GetVoxel(nx, ny, nz).IsSolid();
        }

        /// <summary>按规范 §3.1 追加一个 quad（4 顶点 / 2 三角形）。</summary>
        private static void AddQuad(
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Vector2> uvs,
            List<int> indices,
            int localX,
            int localY,
            int localZ,
            int directionIndex)
        {
            int axis = DirectionAxis[directionIndex];
            int sign = DirectionSign[directionIndex];
            int t1 = Tangent1Axis[directionIndex];
            int t2 = Tangent2Axis[directionIndex];

            Vector3 normal = DirectionNormals[directionIndex];

            // P0：面平面上、两个切向轴都取最小值的角点。
            // 面平面：正方向取该轴的 +1 平面，负方向取 +0 平面（规范 §3.1-1）。
            int px = localX;
            int py = localY;
            int pz = localZ;

            if (axis == 0) px = sign > 0 ? localX + 1 : localX;
            else if (axis == 1) py = sign > 0 ? localY + 1 : localY;
            else pz = sign > 0 ? localZ + 1 : localZ;

            Vector3 p0 = new Vector3(px, py, pz);

            Vector3 e1 = AxisUnit(t1);
            Vector3 e2 = AxisUnit(t2);

            Vector3 v0 = p0;
            Vector3 v1 = p0 + e1;
            Vector3 v2 = p0 + e1 + e2;
            Vector3 v3 = p0 + e2;

            int baseIndex = vertices.Count;

            vertices.Add(v0);
            vertices.Add(v1);
            vertices.Add(v2);
            vertices.Add(v3);

            for (int i = 0; i < 4; i++)
            {
                normals.Add(normal);
                uvs.Add(QuadUVs[i]);
            }

            // 固定三角形顺序：(v0,v1,v2) + (v0,v2,v3)
            indices.Add(baseIndex + 0);
            indices.Add(baseIndex + 1);
            indices.Add(baseIndex + 2);

            indices.Add(baseIndex + 0);
            indices.Add(baseIndex + 2);
            indices.Add(baseIndex + 3);
        }

        private static Vector3 AxisUnit(int axis)
        {
            if (axis == 0) return Vector3.right;
            if (axis == 1) return Vector3.up;
            return Vector3.forward;
        }

        /// <summary>供测试/诊断：方向索引 → 法线（固定方向顺序）。</summary>
        public static Vector3 GetDirectionNormal(int directionIndex)
        {
            if (directionIndex < 0 || directionIndex >= DirectionCount)
            {
                return Vector3.zero;
            }

            return DirectionNormals[directionIndex];
        }
    }
}
