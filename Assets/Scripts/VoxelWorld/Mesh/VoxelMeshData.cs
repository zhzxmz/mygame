using System;
using System.Text;
using UnityEngine; // 仅使用 Vector2 / Vector3 数学结构体；不引用 Mesh / Renderer / Transform / MonoBehaviour

namespace VoxelWorld.Mesh
{
    /// <summary>
    /// Voxel Mesh 数据容器（**纯数据**，对应 Specifications/World/VoxelWorld_Mesh.md §5 的输出契约）。
    ///
    /// 契约：
    ///   · 只保存派生数据：vertices / triangles（按 submesh 分组）/ normals / uv；
    ///   · **不引用 UnityEngine.Mesh**、不使用 Renderer / MeshFilter / MeshCollider / Transform；
    ///   · 坐标空间 = Chunk Local（1 单位 = 1 体素），顶点范围只能是 [0, ChunkSize]；
    ///   · 顶点顺序 / 三角形顺序 / submesh 顺序由 VoxelMeshGenerator 固定 ⇒ 同输入必得同输出；
    ///   · 本类型**不修改** VoxelChunk，也不反向影响世界真值（Mesh 不是世界状态）。
    ///
    /// 注意：公开的数组是只读语义（调用方不得修改）；需要可变副本时自行拷贝。
    /// </summary>
    public sealed class VoxelMeshData
    {
        /// <summary>子网格数量：0 = Grass、1 = Dirt、2 = Stone（submesh = (int)VoxelType - 1）。</summary>
        public const int SubmeshCount = 3;

        /// <summary>每 1 个 quad 固定 2 个三角形（第一阶段不做 greedy meshing）。</summary>
        public const int TrianglesPerQuad = 2;

        /// <summary>Chunk Local 顶点（1 单位 = 1 体素）。</summary>
        public readonly Vector3[] Vertices;

        /// <summary>顶点法线（立方体面 ⇒ 恰好为 6 个轴向单位向量之一）。</summary>
        public readonly Vector3[] Normals;

        /// <summary>顶点 UV（每个面 0..1）。</summary>
        public readonly Vector2[] UVs;

        /// <summary>按 submesh 分组的三角形索引：[submesh][index]，长度固定为 SubmeshCount。</summary>
        public readonly int[][] Triangles;

        public VoxelMeshData(
            Vector3[] vertices,
            Vector3[] normals,
            Vector2[] uvs,
            int[][] triangles)
        {
            if (vertices == null) vertices = new Vector3[0];
            if (normals == null) normals = new Vector3[0];
            if (uvs == null) uvs = new Vector2[0];

            if (normals.Length != vertices.Length)
            {
                throw new ArgumentException(
                    "normals 数量必须与 vertices 一致（" + normals.Length +
                    " != " + vertices.Length + "）。", "normals");
            }

            if (uvs.Length != vertices.Length)
            {
                throw new ArgumentException(
                    "uvs 数量必须与 vertices 一致（" + uvs.Length +
                    " != " + vertices.Length + "）。", "uvs");
            }

            int[][] safeTriangles = new int[SubmeshCount][];

            for (int submesh = 0; submesh < SubmeshCount; submesh++)
            {
                int[] indices = triangles != null && submesh < triangles.Length
                    ? triangles[submesh]
                    : null;

                if (indices == null) indices = new int[0];

                if (indices.Length % 3 != 0)
                {
                    throw new ArgumentException(
                        "submesh " + submesh + " 的索引数量必须是 3 的倍数（" +
                        indices.Length + "）。", "triangles");
                }

                for (int i = 0; i < indices.Length; i++)
                {
                    if (indices[i] < 0 || indices[i] >= vertices.Length)
                    {
                        throw new ArgumentException(
                            "submesh " + submesh + " 的索引越界：" + indices[i] +
                            "（顶点数 " + vertices.Length + "）。", "triangles");
                    }
                }

                safeTriangles[submesh] = indices;
            }

            Vertices = vertices;
            Normals = normals;
            UVs = uvs;
            Triangles = safeTriangles;
        }

        /// <summary>顶点数。</summary>
        public int VertexCount
        {
            get { return Vertices.Length; }
        }

        /// <summary>三角形总数（所有 submesh 合计）。</summary>
        public int TriangleCount
        {
            get
            {
                int total = 0;
                for (int i = 0; i < Triangles.Length; i++)
                {
                    total += Triangles[i].Length / 3;
                }

                return total;
            }
        }

        /// <summary>quad 总数（第一阶段 1 quad = 2 三角形 ⇒ 三角形数 / 2）。</summary>
        public int QuadCount
        {
            get { return TriangleCount / TrianglesPerQuad; }
        }

        /// <summary>是否存在任何几何（空 Chunk ⇒ false）。</summary>
        public bool HasGeometry
        {
            get { return TriangleCount > 0; }
        }

        /// <summary>指定 submesh 的三角形索引（只读语义）。</summary>
        public int[] GetSubmeshTriangles(int submesh)
        {
            if (submesh < 0 || submesh >= Triangles.Length) return new int[0];
            return Triangles[submesh];
        }

        /// <summary>指定 submesh 的三角形数量。</summary>
        public int GetSubmeshTriangleCount(int submesh)
        {
            if (submesh < 0 || submesh >= Triangles.Length) return 0;
            return Triangles[submesh].Length / 3;
        }

        /// <summary>空 Mesh（无几何）。</summary>
        public static VoxelMeshData CreateEmpty()
        {
            return new VoxelMeshData(
                new Vector3[0], new Vector3[0], new Vector2[0],
                new int[SubmeshCount][]);
        }

        /// <summary>
        /// 确定性内容哈希（FNV-1a 64bit，覆盖顶点/法线/UV/索引）。
        /// 只依赖数据本身（不使用 string.GetHashCode 等按进程随机化的哈希），
        /// 可用于"两次生成结果完全一致"的自动化断言与未来的脏检测。
        /// </summary>
        public ulong ComputeContentHash()
        {
            const ulong offset = 1469598103934665603UL;
            const ulong prime = 1099511628211UL;

            ulong hash = offset;

            hash = HashInt(hash, Vertices.Length, prime);
            hash = HashInt(hash, Normals.Length, prime);
            hash = HashInt(hash, UVs.Length, prime);

            for (int i = 0; i < Vertices.Length; i++)
            {
                hash = HashFloat(hash, Vertices[i].x, prime);
                hash = HashFloat(hash, Vertices[i].y, prime);
                hash = HashFloat(hash, Vertices[i].z, prime);
            }

            for (int i = 0; i < Normals.Length; i++)
            {
                hash = HashFloat(hash, Normals[i].x, prime);
                hash = HashFloat(hash, Normals[i].y, prime);
                hash = HashFloat(hash, Normals[i].z, prime);
            }

            for (int i = 0; i < UVs.Length; i++)
            {
                hash = HashFloat(hash, UVs[i].x, prime);
                hash = HashFloat(hash, UVs[i].y, prime);
            }

            for (int submesh = 0; submesh < Triangles.Length; submesh++)
            {
                hash = HashInt(hash, Triangles[submesh].Length, prime);

                for (int i = 0; i < Triangles[submesh].Length; i++)
                {
                    hash = HashInt(hash, Triangles[submesh][i], prime);
                }
            }

            return hash;
        }

        public override string ToString()
        {
            StringBuilder text = new StringBuilder();

            text.Append("VoxelMeshData(vertices=").Append(VertexCount);
            text.Append(", quads=").Append(QuadCount);
            text.Append(", triangles=").Append(TriangleCount);
            text.Append(", submeshTriangles=[");

            for (int i = 0; i < Triangles.Length; i++)
            {
                if (i > 0) text.Append(", ");
                text.Append(GetSubmeshTriangleCount(i));
            }

            text.Append("])");
            return text.ToString();
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
    }
}
