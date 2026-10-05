using UnityEngine;

/// <summary>
/// 单个 Terrain Chunk 的运行时视图标记。
/// Chunk GameObject 由 DensityTerrain 管理，属于运行时派生对象，不应保存进 Scene。
/// MeshFilter / MeshRenderer / MeshCollider 都挂在这个视图上，
/// 并通过 SetMesh 从 TerrainChunk.Mesh 统一同步。
/// </summary>
[DisallowMultipleComponent]
public class TerrainChunkView : MonoBehaviour
{
    public Vector3Int Coordinate;
    [System.NonSerialized] public TerrainChunk Chunk;
    public MeshFilter MeshFilter;
    public MeshRenderer MeshRenderer;
    public MeshCollider MeshCollider;

    /// <summary>
    /// 将当前 TerrainChunk.Mesh 同步到 Chunk 的派生组件。
    /// MeshFilter.sharedMesh 与 MeshCollider.sharedMesh 使用同一个 Mesh 引用。
    /// </summary>
    public void SetMesh(Mesh mesh)
    {
        if (MeshFilter != null)
        {
            MeshFilter.sharedMesh = mesh;
        }

        if (MeshCollider == null)
        {
            return;
        }

        if (mesh == null)
        {
            MeshCollider.sharedMesh = null;
            return;
        }

        if (MeshCollider.sharedMesh == mesh)
        {
            return;
        }

        // 先清空再赋值，确保 Unity 在 Mesh 被替换后重新识别并重新 cooking collider。
        MeshCollider.sharedMesh = null;
        MeshCollider.sharedMesh = mesh;
    }
}
