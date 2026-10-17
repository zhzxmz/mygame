using System;
using UnityEngine;
using UnityEngine.Rendering;
using VoxelWorld.Mesh;
// 别名：命名空间 VoxelWorld.Mesh 会遮蔽 UnityEngine.Mesh（简单名 Mesh 在 VoxelWorld 下解析为子命名空间），
// 因此本文件统一用 UnityMesh 指代 Unity 的 Mesh 资产类型。
using UnityMesh = UnityEngine.Mesh;

namespace VoxelWorld.View
{
    /// <summary>
    /// UnityMeshBuilder：<see cref="VoxelMeshData"/>（纯数据）→ <see cref="UnityMesh"/> 资产的**唯一上传路径**。
    ///
    /// 契约来源：Specifications/World/VoxelWorld_View.md §5。
    ///   · §5.1 资产独占     ：调用方为每个 View 创建 1 个自建资产，本类只负责写入，不共享、不改共享资产；
    ///   · §5.2 上传保真     ：逐项照搬输入缓冲（顺序不变），不做任何几何加工（禁止重算法线/剔除/合并）；
    ///                        固定写入顺序：indexFormat → subMeshCount → 顶点/法线/UV → 逐 submesh 索引 → bounds；
    ///   · §5.3 索引格式     ：必须按顶点数**显式**选择（> 65535 ⇒ UInt32），不得依赖默认值；
    ///   · §5.4 包围盒       ：必须显式设置（默认零尺寸包围盒会让整个 Chunk 被整体剔除）；
    ///   · §5.5 原子替换     ：写入必须先撤下旧几何，再由调用方整体替换资产（路径 A / B）。
    ///
    /// 层边界（View §1）：本类**不读** Voxel 数据、**不调用**生成/挖掘、**不碰**脏标记、
    /// 不出现任何世界规则；它只认识纯数据缓冲与 Unity Mesh 资产。
    /// </summary>
    public static class UnityMeshBuilder
    {
        /// <summary>UInt16 索引的顶点数上限（View §5.3）。</summary>
        public const int UInt16VertexLimit = 65535;

        /// <summary>固定 submesh 数量（Grass=0 / Dirt=1 / Stone=2，Mesh 规范 §3.1-8）。</summary>
        public const int ExpectedSubmeshCount = VoxelMeshData.SubmeshCount;

        /// <summary>按顶点数显式选择索引格式（View §5.3）。</summary>
        public static IndexFormat SelectIndexFormat(int vertexCount)
        {
            return vertexCount > UInt16VertexLimit
                ? IndexFormat.UInt32
                : IndexFormat.UInt16;
        }

        /// <summary>
        /// 结构合法性校验（不含"是否有几何"的判定）。
        /// 返回 false 时 <paramref name="error"/> 必须给出原因（View §9.1：禁止静默）。
        /// </summary>
        public static bool TryValidate(VoxelMeshData data, out string error)
        {
            if (data == null)
            {
                error = "meshData 为 null";
                return false;
            }

            if (data.Vertices == null || data.Normals == null || data.UVs == null)
            {
                error = "顶点/法线/UV 数组为 null";
                return false;
            }

            int vertexCount = data.Vertices.Length;

            if (data.Normals.Length != vertexCount || data.UVs.Length != vertexCount)
            {
                error = "法线/UV 数量与顶点数不一致（" + data.Normals.Length +
                        " / " + data.UVs.Length + " != " + vertexCount + "）";
                return false;
            }

            if (data.Triangles == null || data.Triangles.Length != ExpectedSubmeshCount)
            {
                error = "submesh 数量必须为 " + ExpectedSubmeshCount + "（实际 " +
                        (data.Triangles == null ? -1 : data.Triangles.Length) + "）";
                return false;
            }

            for (int submesh = 0; submesh < ExpectedSubmeshCount; submesh++)
            {
                int[] indices = data.Triangles[submesh];
                if (indices == null)
                {
                    error = "submesh " + submesh + " 的索引数组为 null";
                    return false;
                }

                if (indices.Length % 3 != 0)
                {
                    error = "submesh " + submesh + " 的索引数量不是 3 的倍数（" +
                            indices.Length + "）";
                    return false;
                }

                for (int i = 0; i < indices.Length; i++)
                {
                    if (indices[i] < 0 || indices[i] >= vertexCount)
                    {
                        error = "submesh " + submesh + " 的索引越界：" + indices[i] +
                                "（顶点数 " + vertexCount + "）";
                        return false;
                    }
                }
            }

            for (int i = 0; i < vertexCount; i++)
            {
                if (!IsFinite(data.Vertices[i]) ||
                    !IsFinite(data.Normals[i]) ||
                    !IsFinite(data.UVs[i]))
                {
                    // 非有限值会污染 bounds ⇒ 整个 Chunk 消失，属于结构性错误
                    error = "第 " + i + " 个顶点/法线/UV 含非有限值（NaN/Inf）";
                    return false;
                }
            }

            error = null;
            return true;
        }

        /// <summary>
        /// 输入顶点集合的精确 AABB 分量（供 §5.4 设置 bounds 与自动化验证使用；
        /// 不走 <see cref="Bounds"/> 的 center+extents 表示，故无额外浮点舍入）。
        /// </summary>
        public static void ComputeVertexBounds(
            VoxelMeshData data, out Vector3 min, out Vector3 max)
        {
            min = Vector3.zero;
            max = Vector3.zero;

            if (data == null || data.Vertices == null || data.Vertices.Length == 0) return;

            min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            max = new Vector3(float.MinValue, float.MinValue, float.MinValue);

            Vector3[] vertices = data.Vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 v = vertices[i];
                if (v.x < min.x) min.x = v.x;
                if (v.y < min.y) min.y = v.y;
                if (v.z < min.z) min.z = v.z;
                if (v.x > max.x) max.x = v.x;
                if (v.y > max.y) max.y = v.y;
                if (v.z > max.z) max.z = v.z;
            }
        }

        /// <summary>输入顶点集合的 AABB（Chunk Local 空间，View §5.4）。</summary>
        public static Bounds ComputeBounds(VoxelMeshData data)
        {
            Vector3 min;
            Vector3 max;
            ComputeVertexBounds(data, out min, out max);

            if (data == null || data.Vertices == null || data.Vertices.Length == 0)
            {
                return new Bounds(Vector3.zero, Vector3.zero);
            }

            Bounds bounds = new Bounds();
            bounds.SetMinMax(min, max);
            return bounds;
        }

        /// <summary>
        /// 写入一个已存在的资产（View §5.5 路径 B：复用同一资产）。
        /// 顺序：撤下旧几何（Clear）→ indexFormat → subMeshCount → 顶点/法线/UV → 逐 submesh 索引 → bounds。
        ///
        /// 注意：返回 false 时该资产可能处于"已撤下几何"的状态，
        /// 调用方**必须**保持旧呈现不变或撤下呈现，且**不得**清脏/置 Ready（View §9.1）。
        /// </summary>
        public static bool TryWrite(UnityMesh target, VoxelMeshData data, out string error)
        {
            if (target == null)
            {
                error = "目标 Mesh 资产为 null";
                return false;
            }

            if (!TryValidate(data, out error)) return false;

            if (!data.HasGeometry)
            {
                error = "空几何不写入资产（View §5.4：应由调用方走 Empty 路径，不创建资产）";
                return false;
            }

            try
            {
                // 路径 B 第一步：先撤下旧几何，避免"顶点已新、索引还旧"的可见组合
                target.Clear(false);

                // §5.2-1 索引格式（必须在写入任何几何之前显式设定）
                target.indexFormat = SelectIndexFormat(data.VertexCount);

                // §5.2-2 submesh 数量固定为 3（空 submesh 允许写入 0 个索引）
                target.subMeshCount = ExpectedSubmeshCount;

                // §5.2-3 顶点 / 法线 / UV：顺序照搬，禁止 RecalculateNormals
                target.SetVertices(data.Vertices);
                target.SetNormals(data.Normals);
                target.SetUVs(0, data.UVs);

                // §5.2-4 逐 submesh 写入索引（不得合并成单一 submesh）
                for (int submesh = 0; submesh < ExpectedSubmeshCount; submesh++)
                {
                    target.SetTriangles(data.GetSubmeshTriangles(submesh), submesh);
                }

                // §5.4 显式包围盒（禁止保留默认零尺寸 bounds）
                target.bounds = ComputeBounds(data);

                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = "写入 Mesh 资产失败：" + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 构建一个**新的**自建资产（View §5.5 路径 A：完整写入后整体替换 —— V1 推荐路径）。
        /// 失败时必须释放临时资产并返回 false（不留半成品、不泄漏）。
        /// </summary>
        public static bool TryBuild(
            VoxelMeshData data, string assetName, out UnityMesh mesh, out string error)
        {
            mesh = null;

            if (!TryValidate(data, out error)) return false;

            if (!data.HasGeometry)
            {
                error = "空几何不创建资产（View §5.4）";
                return false;
            }

            UnityMesh created = null;

            try
            {
                created = new UnityMesh();
                if (!string.IsNullOrEmpty(assetName)) created.name = assetName;

                if (!TryWrite(created, data, out error))
                {
                    DestroySafely(created);
                    return false;
                }

                mesh = created;
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                DestroySafely(created);
                error = "创建 Mesh 资产失败：" + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 释放 Unity 对象（资产 / 容器）：播放模式用 <see cref="UnityEngine.Object.Destroy"/>，
        /// 编辑模式（含批处理验证）用 <see cref="UnityEngine.Object.DestroyImmediate"/>。
        /// 空引用与"已被销毁"（fake-null）都安全返回（幂等）。
        /// </summary>
        public static void DestroySafely(UnityEngine.Object obj)
        {
            if (obj == null) return;

            if (Application.isPlaying) UnityEngine.Object.Destroy(obj);
            else UnityEngine.Object.DestroyImmediate(obj);
        }

        private static bool IsFinite(Vector3 v)
        {
            return IsFinite(v.x) && IsFinite(v.y) && IsFinite(v.z);
        }

        private static bool IsFinite(Vector2 v)
        {
            return IsFinite(v.x) && IsFinite(v.y);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
