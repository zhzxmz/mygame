using UnityEngine;

/// <summary>
/// Player Mining / Targeting V1。
/// 这是输入与 Targeting 层，不负责地形修改逻辑：
/// - 使用 Player Camera 做 Physics.Raycast
/// - 目标必须是现有 Terrain MeshCollider（TerrainChunkView）
/// - RaycastHit.point 通过 DensityTerrain.WorldToLocalPosition 转为 Terrain Local Space
/// - 调用现有 DensityTerrain.MineCubeLocal → TerrainMining Cube Brush API
/// 不直接修改 DensityField / MaterialField / Mesh / MeshCollider。
/// </summary>
[DisallowMultipleComponent]
public class PlayerMiningController : MonoBehaviour
{
    [Header("Targeting")]
    [Tooltip("Raycast 使用的 Camera；留空时自动使用 Camera.main 或子物体 Camera")]
    public Camera targetCamera;

    [Tooltip("Raycast 最大距离")]
    public float maxDistance = 100f;

    [Tooltip("Raycast 层级；默认全部层，命中后仍需是 TerrainChunkView")]
    public LayerMask raycastMask = ~0;

    [Header("Terrain")]
    [Tooltip("目标 DensityTerrain；留空时自动查找场景中的 DensityTerrain")]
    public DensityTerrain terrain;

    [Header("Mining")]
    [Tooltip("Debug Mining 按键")]
    public KeyCode miningKey = KeyCode.F;

    [Tooltip("V1 Cube Brush 完整尺寸（Terrain Local Space）")]
    public Vector3 brushSize = new Vector3(2f, 2f, 2f);

    [Header("Debug")]
    public bool logMining;

    [Header("Debug Targeting")]
    [Tooltip("在 Scene / Game 视图中绘制 RaycastHit 标记")]
    public bool debugDrawTargeting = true;

    [Tooltip("在屏幕上显示 Hit 的 Chunk / Triangle 信息")]
    public bool debugShowHitInfo = true;

    [Tooltip("Hit 信息变化时输出到 Console")]
    public bool debugLogHitInfo = false;

    private bool hasDebugHit;
    private RaycastHit debugHit;
    private string debugHitInfo = string.Empty;
    private string lastLoggedHitInfo = string.Empty;

    private int cachedTriangleMeshId;
    private int cachedTriangleIndex = -1;
    private string cachedTriangleIndices = "n/a";
    private string cachedSubMeshIndex = "n/a";

    private void Update()
    {
        if (debugDrawTargeting || debugShowHitInfo || debugLogHitInfo)
        {
            UpdateDebugTarget();
        }

        if (MouseLock.IsUIBlocking) return;
        if (!Input.GetKeyDown(miningKey)) return;

        TryMine();
    }

    /// <summary>
    /// 执行一次 Targeting + Mining 闭环。
    /// 供 Debug 输入和自动验证调用。
    /// </summary>
    public bool TryMine()
    {
        if (!TryGetTerrainHit(out RaycastHit hit, out TerrainChunkView _))
        {
            if (logMining)
            {
                Debug.Log("[PlayerMining] No terrain hit.");
            }

            return false;
        }

        DensityTerrain resolvedTerrain = ResolveTerrain();
        if (resolvedTerrain == null)
        {
            if (logMining)
            {
                Debug.LogWarning("[PlayerMining] No DensityTerrain.");
            }

            return false;
        }

        Vector3 localPoint = resolvedTerrain.WorldToLocalPosition(hit.point);
        int modifiedSamples = resolvedTerrain.MineCubeLocal(localPoint, brushSize);

        if (logMining)
        {
            Debug.Log(
                "[PlayerMining] hit=" + hit.point +
                " local=" + localPoint +
                " modifiedSamples=" + modifiedSamples
            );
        }

        return modifiedSamples > 0;
    }

    /// <summary>
    /// Targeting only：Camera Physics.Raycast，目标必须是 Terrain MeshCollider。
    /// 不修改 Terrain 数据。
    /// </summary>
    public bool TryGetTerrainHit(out RaycastHit hit, out TerrainChunkView view)
    {
        hit = default(RaycastHit);
        view = null;

        Camera camera = ResolveCamera();
        if (camera == null) return false;

        Ray ray = new Ray(camera.transform.position, camera.transform.forward);

        if (!Physics.Raycast(
            ray,
            out hit,
            maxDistance,
            raycastMask,
            QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        if (hit.collider == null) return false;

        view = hit.collider.GetComponentInParent<TerrainChunkView>();
        return view != null;
    }

    private void UpdateDebugTarget()
    {
        if (!TryGetTerrainHit(out RaycastHit hit, out TerrainChunkView view))
        {
            hasDebugHit = false;
            debugHitInfo = string.Empty;
            return;
        }

        hasDebugHit = true;
        debugHit = hit;
        debugHitInfo = BuildHitInfo(hit, view);

        if (debugLogHitInfo && debugHitInfo != lastLoggedHitInfo)
        {
            lastLoggedHitInfo = debugHitInfo;
            Debug.Log("[PlayerMiningDebug] " + debugHitInfo);
        }

        if (debugDrawTargeting)
        {
            DrawRuntimeHitMarker(hit);
        }
    }

    private string BuildHitInfo(RaycastHit hit, TerrainChunkView view)
    {
        string chunkCoordinate = view != null && view.Chunk != null
            ? view.Chunk.Coordinate.ToString()
            : "unknown";

        Mesh mesh = view != null && view.MeshFilter != null
            ? view.MeshFilter.sharedMesh
            : null;

        BuildTriangleInfo(mesh, hit.triangleIndex);

        return "Chunk=" + chunkCoordinate +
               " Triangle=" + hit.triangleIndex +
               " SubMesh=" + cachedSubMeshIndex +
               " Indices=(" + cachedTriangleIndices + ")" +
               " Point=" + hit.point +
               " Normal=" + hit.normal;
    }

    private void BuildTriangleInfo(Mesh mesh, int triangleIndex)
    {
        if (mesh == null || !mesh.isReadable || triangleIndex < 0)
        {
            cachedTriangleMeshId = 0;
            cachedTriangleIndex = -1;
            cachedTriangleIndices = "n/a";
            cachedSubMeshIndex = "n/a";
            return;
        }

        int meshId = mesh.GetInstanceID();
        if (meshId == cachedTriangleMeshId && triangleIndex == cachedTriangleIndex)
        {
            return;
        }

        cachedTriangleMeshId = meshId;
        cachedTriangleIndex = triangleIndex;
        cachedTriangleIndices = "n/a";
        cachedSubMeshIndex = "n/a";

        int[] triangles = mesh.triangles;
        int baseIndex = triangleIndex * 3;

        if (baseIndex >= 0 && baseIndex + 2 < triangles.Length)
        {
            cachedTriangleIndices =
                triangles[baseIndex] + "," +
                triangles[baseIndex + 1] + "," +
                triangles[baseIndex + 2];
        }

        int offset = 0;
        for (int i = 0; i < mesh.subMeshCount; i++)
        {
            int count = mesh.GetTriangles(i).Length;
            if (baseIndex >= offset && baseIndex < offset + count)
            {
                cachedSubMeshIndex = i.ToString();
                break;
            }

            offset += count;
        }
    }

    private void DrawRuntimeHitMarker(RaycastHit hit)
    {
        Camera camera = ResolveCamera();
        if (camera != null)
        {
            Debug.DrawLine(
                camera.transform.position,
                hit.point,
                Color.yellow,
                0f,
                false
            );
        }

        Debug.DrawRay(hit.point, hit.normal * 2f, Color.cyan, 0f, false);

        const float size = 0.25f;
        Debug.DrawLine(
            hit.point - Vector3.right * size,
            hit.point + Vector3.right * size,
            Color.red,
            0f,
            false
        );
        Debug.DrawLine(
            hit.point - Vector3.up * size,
            hit.point + Vector3.up * size,
            Color.red,
            0f,
            false
        );
        Debug.DrawLine(
            hit.point - Vector3.forward * size,
            hit.point + Vector3.forward * size,
            Color.red,
            0f,
            false
        );
    }

    private void OnDrawGizmos()
    {
        if (!debugDrawTargeting || !hasDebugHit) return;

        Gizmos.color = new Color(1f, 0.2f, 0.1f, 0.35f);
        Gizmos.DrawSphere(debugHit.point, 0.25f);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(debugHit.point, 0.35f);

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(debugHit.point, debugHit.point + debugHit.normal * 2f);
    }

    private void OnGUI()
    {
        if (!debugShowHitInfo || !hasDebugHit ||
            string.IsNullOrEmpty(debugHitInfo))
        {
            return;
        }

        Camera camera = ResolveCamera();
        if (camera == null) return;

        Vector3 screenPoint = camera.WorldToScreenPoint(debugHit.point);
        if (screenPoint.z < 0f) return;

        Rect labelRect = new Rect(
            screenPoint.x + 14f,
            Screen.height - screenPoint.y + 14f,
            900f,
            60f
        );

        GUI.color = Color.yellow;
        GUI.Label(labelRect, debugHitInfo);
        GUI.color = Color.white;
    }

    private Camera ResolveCamera()
    {
        if (targetCamera != null) return targetCamera;

        targetCamera = Camera.main;
        if (targetCamera == null)
        {
            targetCamera = GetComponentInChildren<Camera>();
        }

        return targetCamera;
    }

    private DensityTerrain ResolveTerrain()
    {
        if (terrain != null) return terrain;

        terrain = FindObjectOfType<DensityTerrain>();
        return terrain;
    }
}
