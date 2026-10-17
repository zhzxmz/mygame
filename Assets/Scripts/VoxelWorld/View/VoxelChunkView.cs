using System;
using UnityEngine;
using VoxelWorld.Mesh;
// 别名：命名空间 VoxelWorld.Mesh 会遮蔽 UnityEngine.Mesh（简单名 Mesh 在 VoxelWorld 下解析为子命名空间），
// 因此本文件统一用 UnityMesh 指代 Unity 的 Mesh 资产类型。
using UnityMesh = UnityEngine.Mesh;

namespace VoxelWorld.View
{
    /// <summary>View 自身状态（View 规范 §4）。与 Runtime 的 Chunk 状态机是投影关系。</summary>
    public enum ChunkViewState
    {
        /// <summary>无容器、无资产（初始 / 已释放）。</summary>
        Absent,

        /// <summary>容器已建、已确认**无几何**（正常成功路径，不是失败）。</summary>
        Empty,

        /// <summary>容器 + 资产，内容与最近一次成功提交的缓冲一致。</summary>
        Presented
    }

    /// <summary>
    /// 一次提交的三种结果（View §8.1：必须能区分"已呈现 / 已确认空几何 / 失败"）。
    /// </summary>
    public enum ChunkViewApplyResult
    {
        /// <summary>已完整呈现几何。</summary>
        Presented,

        /// <summary>已确认无几何（数据一致，属成功）。</summary>
        Empty,

        /// <summary>失败（必须携带原因；旧呈现保持不变）。</summary>
        Failed
    }

    /// <summary>
    /// View 层的显式配置（View §3 / §8.1）：所有影响呈现的输入都必须显式传入，
    /// 禁止隐式查找（`Camera.main` / `FindObjectOfType` / 全局单例 / 全局 Layer 设置）。
    /// </summary>
    [Serializable]
    public sealed class ChunkViewSettings
    {
        /// <summary>容器父节点（必填；父链 lossyScale 必须为 1，View §3.3）。</summary>
        public Transform Root;

        /// <summary>固定 3 个材质槽，顺序 <b>Grass=0 / Dirt=1 / Stone=2</b>（View §6.1，禁止重排）。</summary>
        public Material[] SubmeshMaterials = new Material[VoxelMeshData.SubmeshCount];

        /// <summary>是否构建 MeshCollider（View §7.5；关闭时组件不得残留）。</summary>
        public bool BuildCollider = true;

        /// <summary>显式 Layer（View §3.1；禁止改全局设置）。</summary>
        public int Layer;

        /// <summary>配置校验（View §9.1：非法配置必须在启动前被拒绝，不得运行中静默修正）。</summary>
        public bool IsValid(out string reason)
        {
            if (Root == null)
            {
                reason = "Root 未设置或已被销毁";
                return false;
            }

            if (SubmeshMaterials == null ||
                SubmeshMaterials.Length != VoxelMeshData.SubmeshCount)
            {
                reason = "SubmeshMaterials 长度必须为 " + VoxelMeshData.SubmeshCount +
                         "（顺序 Grass/Dirt/Stone）";
                return false;
            }

            if (Layer < 0 || Layer > 31)
            {
                reason = "Layer 必须在 [0, 31] 内（实际 " + Layer + "）";
                return false;
            }

            Vector3 scale = Root.lossyScale;
            if (Mathf.Abs(scale.x - 1f) > 0.0001f ||
                Mathf.Abs(scale.y - 1f) > 0.0001f ||
                Mathf.Abs(scale.z - 1f) > 0.0001f)
            {
                // 父链带缩放会破坏 Chunk 之间的面接缝（View §3.3）
                reason = "Root 的 lossyScale 必须为 1（实际 " + scale + "）";
                return false;
            }

            reason = null;
            return true;
        }
    }

    /// <summary>
    /// VoxelChunkView：单个 Chunk 的呈现（View 第一阶段闭环）。
    ///
    /// 契约来源：Specifications/World/VoxelWorld_View.md。
    ///   · §1.2 输入只有 (VoxelCoordinate, VoxelMeshData)：不读数据层、不读生成参数、不建第二份真值；
    ///   · §1.3 不写世界：不调用数据写入、不碰脏标记（清脏由 Runtime Core 在收到成功之后执行）；
    ///   · §3   对象模型：显式 Root 下的直接子级、整数世界坐标、无旋转/无缩放、命名可反解坐标；
    ///   · §5   Mesh 资产：独占自建、保真上传、原子替换（路径 A：新建资产后整体替换）；
    ///   · §6   材质：3 个共享材质槽，顺序固定 Grass/Dirt/Stone，不为每 Chunk 实例化材质；
    ///   · §7   Collider：与呈现同源（同一资产）、静态非凸、容器不挂 Rigidbody；
    ///   · §9   失败语义：失败必须带原因、旧呈现不变、不得清脏。
    ///
    /// 第一阶段范围：**单 Chunk 显示**。不含 Runtime / Loader / Save / 流式加载。
    /// </summary>
    public sealed class VoxelChunkView : IDisposable
    {
        /// <summary>Chunk 边长（体素单位）。必须与 VoxelCore 的 ChunkSize 一致（由自动化验证断言）。</summary>
        public const int ChunkSize = 16;

        private readonly VoxelCoordinate coordinate;
        private readonly ChunkViewSettings settings;

        private GameObject container;
        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private MeshCollider meshCollider;

        private UnityMesh ownedMesh;
        private ChunkViewState state = ChunkViewState.Absent;
        private bool released;

        private int appliedCount;
        private int failedCount;

        public VoxelChunkView(VoxelCoordinate coordinate, ChunkViewSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException("settings");
            }

            string reason;
            if (!settings.IsValid(out reason))
            {
                throw new ArgumentException("ChunkViewSettings 非法：" + reason, "settings");
            }

            this.coordinate = coordinate;
            this.settings = settings;
        }

        // -----------------------------------------------------------------
        // 只读状态 / 诊断
        // -----------------------------------------------------------------

        /// <summary>本 View 对应的 Chunk 坐标（构造后不变）。</summary>
        public VoxelCoordinate Coordinate
        {
            get { return coordinate; }
        }

        /// <summary>View 自身状态（View §4）。</summary>
        public ChunkViewState State
        {
            get { return state; }
        }

        /// <summary>是否持有可呈现几何。</summary>
        public bool HasGeometry
        {
            get { return ownedMesh != null && ownedMesh.vertexCount > 0; }
        }

        /// <summary>是否已释放（释放后为终态，不可再提交）。</summary>
        public bool IsReleased
        {
            get { return released; }
        }

        public GameObject Container
        {
            get { return container; }
        }

        public MeshFilter Filter
        {
            get { return meshFilter; }
        }

        public MeshRenderer Renderer
        {
            get { return meshRenderer; }
        }

        public MeshCollider Collider
        {
            get { return meshCollider; }
        }

        /// <summary>本 View 自有的 Mesh 资产（只读暴露，供诊断 / 自动化验证）。</summary>
        public UnityMesh SharedMesh
        {
            get { return ownedMesh; }
        }

        public int AppliedCount
        {
            get { return appliedCount; }
        }

        public int FailedCount
        {
            get { return failedCount; }
        }

        /// <summary>容器命名（View §3.2：名称必须可反解出 Chunk 坐标）。</summary>
        public static string BuildContainerName(VoxelCoordinate coordinate)
        {
            return "ChunkView (" + coordinate.x + ", " + coordinate.y + ", " +
                   coordinate.z + ")";
        }

        /// <summary><see cref="BuildContainerName"/> 的逆运算（供批处理验证 / 诊断）。</summary>
        public static bool TryParseContainerName(string name, out VoxelCoordinate coordinate)
        {
            coordinate = VoxelCoordinate.Zero;

            if (string.IsNullOrEmpty(name)) return false;
            if (!name.StartsWith("ChunkView (", StringComparison.Ordinal)) return false;
            if (!name.EndsWith(")", StringComparison.Ordinal)) return false;

            string body = name.Substring("ChunkView (".Length);
            body = body.Substring(0, body.Length - 1);

            string[] parts = body.Split(',');
            if (parts.Length != 3) return false;

            int x;
            int y;
            int z;
            if (!int.TryParse(parts[0].Trim(), out x)) return false;
            if (!int.TryParse(parts[1].Trim(), out y)) return false;
            if (!int.TryParse(parts[2].Trim(), out z)) return false;

            coordinate = new VoxelCoordinate(x, y, z);
            return true;
        }

        // -----------------------------------------------------------------
        // 提交呈现（唯一入口）
        // -----------------------------------------------------------------

        /// <summary>
        /// 提交呈现。坐标必须与本 View 的坐标一致（防"喂错 Chunk"这一类缺陷）。
        /// </summary>
        public ChunkViewApplyResult TryApply(
            VoxelCoordinate coordinate, VoxelMeshData meshData, out string error)
        {
            if (coordinate != this.coordinate)
            {
                failedCount++;
                error = "坐标不匹配：本 View 为 " + this.coordinate + "，收到 " + coordinate;
                return ChunkViewApplyResult.Failed;
            }

            return TryApply(meshData, out error);
        }

        /// <summary>
        /// 提交呈现（原子替换）：
        ///   · 几何为空 ⇒ 撤下几何 → <see cref="ChunkViewState.Empty"/>（成功）；
        ///   · 几何非空 ⇒ 新建资产完整写入 → 整体替换 → <see cref="ChunkViewState.Presented"/>（成功）；
        ///   · 任何失败 ⇒ 旧呈现保持不变 + 返回原因，且**不触碰脏标记**。
        /// </summary>
        public ChunkViewApplyResult TryApply(VoxelMeshData meshData, out string error)
        {
            if (released)
            {
                failedCount++;
                error = "View 已释放（释放是终态，View §4）";
                return ChunkViewApplyResult.Failed;
            }

            if (meshData == null)
            {
                failedCount++;
                error = "meshData 为 null";
                return ChunkViewApplyResult.Failed;
            }

            if (container == null && !TryCreateContainer(out error))
            {
                failedCount++;
                return ChunkViewApplyResult.Failed;
            }

            // §5.4 / §4.3：空几何是**成功**路径 —— 撤下几何，不创建资产
            if (!meshData.HasGeometry)
            {
                ReleaseGeometry();
                state = ChunkViewState.Empty;
                appliedCount++;
                error = null;
                return ChunkViewApplyResult.Empty;
            }

            // §5.5 路径 A：先在私有资产上完整写完，再整体替换（禁止半成品可见）
            UnityMesh newMesh;
            if (!UnityMeshBuilder.TryBuild(
                meshData, BuildMeshAssetName(coordinate), out newMesh, out error))
            {
                failedCount++;
                return ChunkViewApplyResult.Failed; // 旧呈现不变
            }

            UnityMesh previous = ownedMesh;

            ownedMesh = newMesh;
            meshFilter.sharedMesh = newMesh;
            if (meshCollider != null) meshCollider.sharedMesh = newMesh;

            // 替换成功之后才释放旧资产（顺序见 View §5.5 路径 A）
            if (previous != null && previous != newMesh)
            {
                UnityMeshBuilder.DestroySafely(previous);
            }

            state = ChunkViewState.Presented;
            appliedCount++;
            error = null;
            return ChunkViewApplyResult.Presented;
        }

        /// <summary>
        /// Mesh 资产命名（诊断可读，含 Chunk 坐标）。
        /// 注意：命名中不得出现数据层对象的类型名，否则会被 View §1.2 的结构性扫描判为越界引用。
        /// </summary>
        public static string BuildMeshAssetName(VoxelCoordinate coordinate)
        {
            return "VoxelMesh (" + coordinate.x + ", " + coordinate.y + ", " +
                   coordinate.z + ")";
        }

        // -----------------------------------------------------------------
        // 释放（幂等）
        // -----------------------------------------------------------------

        /// <summary>
        /// 释放容器与**自有**资产（View §5.6）。幂等；不触碰数据层、不销毁共享材质。
        /// 释放后本 View 为终态（<see cref="IsReleased"/> == true）。
        /// </summary>
        public void Release()
        {
            ReleaseGeometry();

            if (container != null)
            {
                UnityMeshBuilder.DestroySafely(container);
                container = null;
            }

            meshFilter = null;
            meshRenderer = null;
            meshCollider = null;

            state = ChunkViewState.Absent;
            released = true;
        }

        public void Dispose()
        {
            Release();
        }

        public override string ToString()
        {
            return "VoxelChunkView" + coordinate + "[" + state +
                   ", applied=" + appliedCount + ", failed=" + failedCount + "]";
        }

        // -----------------------------------------------------------------
        // 内部
        // -----------------------------------------------------------------

        private void ReleaseGeometry()
        {
            // 顺序（View §5.6）：Collider → MeshFilter → 自有资产
            if (meshCollider != null) meshCollider.sharedMesh = null;
            if (meshFilter != null) meshFilter.sharedMesh = null;

            if (ownedMesh != null)
            {
                UnityMeshBuilder.DestroySafely(ownedMesh);
                ownedMesh = null;
            }
        }

        private bool TryCreateContainer(out string error)
        {
            string reason;
            if (!settings.IsValid(out reason))
            {
                error = reason;
                return false;
            }

            try
            {
                container = new GameObject(BuildContainerName(coordinate));
                container.layer = settings.Layer;

                Transform t = container.transform;
                t.SetParent(settings.Root, false);

                // §3.3：整数世界坐标（一次性计算，禁止逐 Chunk 累加）、无旋转、无缩放
                t.localRotation = Quaternion.identity;
                t.localScale = Vector3.one;
                t.localPosition = new Vector3(
                    coordinate.x * ChunkSize,
                    coordinate.y * ChunkSize,
                    coordinate.z * ChunkSize);

                meshFilter = container.AddComponent<MeshFilter>();
                meshRenderer = container.AddComponent<MeshRenderer>();

                // §6：3 个共享材质槽（不为每 Chunk 实例化材质）
                meshRenderer.sharedMaterials = settings.SubmeshMaterials;
                meshRenderer.enabled = true;

                if (settings.BuildCollider)
                {
                    meshCollider = container.AddComponent<MeshCollider>();
                    meshCollider.convex = false;   // §7.2 静态非凸
                    meshCollider.isTrigger = false;
                    meshCollider.sharedMesh = null;
                }

                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = "创建容器失败：" + ex.GetType().Name + ": " + ex.Message;
                Release();
                return false;
            }
        }
    }
}
