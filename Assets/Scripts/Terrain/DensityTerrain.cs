using UnityEngine;

/// <summary>
/// Density Terrain Prototype。
/// 负责地形参数、DensityField 生成和把 Mesh 显示到当前 GameObject。
/// 当前只创建一个 TerrainChunk；未来可以在此基础上扩展多 Chunk。
/// 不把 Unity Mesh 当作源数据，Mesh 只是 DensityField 的可视化结果。
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class DensityTerrain : MonoBehaviour
{
    [Header("Terrain Size")]
    public Vector3 terrainSize = new Vector3(64f, 32f, 64f);

    [Tooltip("每个轴上的采样单元格数量；不是游戏规则里的方块大小")]
    [Min(1)]
    public int resolution = 24;

    [Header("Density")]
    public bool useNoise = true;

    [Tooltip("基础地表高度（世界 Y）")]
    public float height = 16f;

    [Tooltip("噪声频率")]
    public float noiseScale = 0.08f;

    [Tooltip("噪声高度强度")]
    public float noiseStrength = 4f;

    [Header("Rendering")]
    public Material terrainMaterial;

    [Tooltip("在 Editor 中修改参数时自动重新生成 Mesh")]
    public bool regenerateInEditor = true;

    private const float IsoLevel = 0f;

    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private TerrainChunk chunk;
    private Material runtimeMaterial;

    private void OnEnable()
    {
        if (!gameObject.scene.IsValid()) return;
        Regenerate();
    }

    private void OnValidate()
    {
        if (!regenerateInEditor) return;
        if (!isActiveAndEnabled) return;
        if (!gameObject.scene.IsValid()) return;

        Regenerate();
    }

    private void OnDestroy()
    {
        if (chunk != null)
        {
            chunk.Dispose();
            chunk = null;
        }

        if (runtimeMaterial != null)
        {
            if (Application.isPlaying)
            {
                Object.Destroy(runtimeMaterial);
            }
            else
            {
                Object.DestroyImmediate(runtimeMaterial);
            }

            runtimeMaterial = null;
        }
    }

    [ContextMenu("Regenerate Terrain")]
    public void Regenerate()
    {
        EnsureComponents();

        if (chunk == null)
        {
            chunk = new TerrainChunk();
        }
        else
        {
            chunk.Dispose();
        }

        DensityField densityField = BuildDensityField();
        chunk.Build(densityField, IsoLevel);

        meshFilter.sharedMesh = chunk.Mesh;
        meshRenderer.sharedMaterial = GetMaterial();
    }

    private void EnsureComponents()
    {
        if (meshFilter == null)
        {
            meshFilter = GetComponent<MeshFilter>();
        }

        if (meshRenderer == null)
        {
            meshRenderer = GetComponent<MeshRenderer>();
        }

        if (meshFilter == null)
        {
            meshFilter = gameObject.AddComponent<MeshFilter>();
        }

        if (meshRenderer == null)
        {
            meshRenderer = gameObject.AddComponent<MeshRenderer>();
        }
    }

    private DensityField BuildDensityField()
    {
        int cells = Mathf.Max(1, resolution);

        Vector3 size = new Vector3(
            Mathf.Max(0.01f, terrainSize.x),
            Mathf.Max(0.01f, terrainSize.y),
            Mathf.Max(0.01f, terrainSize.z)
        );

        Vector3 origin = transform.position;
        DensityField field = new DensityField(cells, cells, cells, origin, size);

        float strength = Mathf.Max(0f, noiseStrength);

        for (int x = 0; x < field.SampleCountX; x++)
        {
            for (int y = 0; y < field.SampleCountY; y++)
            {
                for (int z = 0; z < field.SampleCountZ; z++)
                {
                    Vector3 world = field.GetWorldPosition(x, y, z);

                    float surfaceHeight = height;

                    if (useNoise && strength > 0f)
                    {
                        float noise = Mathf.PerlinNoise(
                            world.x * noiseScale,
                            world.z * noiseScale
                        ) * 2f - 1f;

                        surfaceHeight += noise * strength;
                    }

                    // 地表以下 Density > 0，地表以上 Density < 0。
                    field[x, y, z] = surfaceHeight - world.y;
                }
            }
        }

        return field;
    }

    private Material GetMaterial()
    {
        if (terrainMaterial != null)
        {
            return terrainMaterial;
        }

        if (runtimeMaterial == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Sprites/Default");

            if (shader == null)
            {
                Debug.LogWarning("DensityTerrain: 找不到可用的 Shader，无法创建默认地形材质");
                return null;
            }

            runtimeMaterial = new Material(shader);
            runtimeMaterial.name = "Runtime Terrain Material";
            runtimeMaterial.hideFlags = HideFlags.DontSave;

            Color color = new Color(0.45f, 0.55f, 0.35f);

            if (runtimeMaterial.HasProperty("_BaseColor"))
            {
                runtimeMaterial.SetColor("_BaseColor", color);
            }
            else if (runtimeMaterial.HasProperty("_Color"))
            {
                runtimeMaterial.SetColor("_Color", color);
            }
        }

        return runtimeMaterial;
    }
}
