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

    [Header("Density Mode")]
    public DensityGenerationMode densityMode = DensityGenerationMode.HeightField;

    [Header("Density")]
    public bool useNoise = true;

    [Tooltip("HeightField 基础地表高度（世界 Y）")]
    public float height = 16f;

    [Tooltip("噪声频率")]
    public float noiseScale = 0.08f;

    [Tooltip("噪声强度")]
    public float noiseStrength = 4f;

    [Header("Sphere Cavity Debug")]
    [Tooltip("球形空洞中心相对地形 Origin 的偏移")]
    public Vector3 cavityCenterOffset = new Vector3(0f, 6f, 0f);

    [Tooltip("球形空洞半径")]
    public float cavityRadius = 3f;

    [Header("Material Layers")]
    [Tooltip("地表以下 Grass 层厚度")]
    public float grassDepth = 1f;

    [Tooltip("Grass 层以下 Dirt 层厚度")]
    public float dirtDepth = 3f;

    [Header("Debug Cutaway")]
    [Tooltip("仅用于调试：在 HeightField 地形中切出一个垂直空气切面")]
    public bool debugCutaway = false;

    [Tooltip("切面中心的世界 X 坐标")]
    public float debugCutX = 0f;

    [Tooltip("切面宽度")]
    public float debugCutWidth = 4f;

    [Header("Rendering")]
    public Material grassMaterial;
    public Material dirtMaterial;
    public Material stoneMaterial;

    [Tooltip("在 Editor 中修改参数时自动重新生成 Mesh")]
    public bool regenerateInEditor = true;

    private const float IsoLevel = 0f;

    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private TerrainChunk chunk;
    private Material[] runtimeMaterials = new Material[3];
    private Material[] materialArray;

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

        for (int i = 0; i < runtimeMaterials.Length; i++)
        {
            if (runtimeMaterials[i] == null) continue;

            if (Application.isPlaying)
            {
                Object.Destroy(runtimeMaterials[i]);
            }
            else
            {
                Object.DestroyImmediate(runtimeMaterials[i]);
            }

            runtimeMaterials[i] = null;
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

        DensityField densityField = BuildDensityField(out MaterialField materialField);
        ApplyDebugCutaway(densityField);
        chunk.Build(densityField, materialField, IsoLevel);

        meshFilter.sharedMesh = chunk.Mesh;
        meshRenderer.sharedMaterials = GetMaterials();
    }

    private void ApplyDebugCutaway(DensityField densityField)
    {
        // 只用于 HeightField 调试；其他测试模式保持原样。
        if (!debugCutaway) return;
        if (densityMode != DensityGenerationMode.HeightField) return;

        float width = Mathf.Max(0f, debugCutWidth);
        if (width <= 0f) return;

        float halfWidth = width * 0.5f;
        float cutMinX = debugCutX - halfWidth;
        float cutMaxX = debugCutX + halfWidth;

        int minIndex = Mathf.FloorToInt(
            (cutMinX - densityField.Origin.x) / densityField.CellSizeX
        );
        int maxIndex = Mathf.CeilToInt(
            (cutMaxX - densityField.Origin.x) / densityField.CellSizeX
        );

        minIndex = Mathf.Clamp(minIndex, 0, densityField.SampleCountX - 1);
        maxIndex = Mathf.Clamp(maxIndex, minIndex, densityField.SampleCountX - 1);

        for (int x = minIndex; x <= maxIndex; x++)
        {
            for (int y = 0; y < densityField.SampleCountY; y++)
            {
                for (int z = 0; z < densityField.SampleCountZ; z++)
                {
                    densityField[x, y, z] = -1f;
                }
            }
        }
    }

    private MaterialField GetMaterialFieldForDebug()
    {
        if (chunk == null || chunk.Material == null)
        {
            Regenerate();
        }

        return chunk != null ? chunk.Material : null;
    }

    [ContextMenu("Log Material At Center")]
    public void LogMaterialAtCenter()
    {
        MaterialField materialField = GetMaterialFieldForDebug();
        if (materialField == null)
        {
            Debug.LogWarning("DensityTerrain: MaterialField 尚未生成");
            return;
        }

        Vector3 center = materialField.Origin + materialField.Size * 0.5f;
        LogMaterialAtWorldPoint(materialField, center, "Center");

        // 额外打印一条竖直采样线，便于看到 Air / Grass / Dirt / Stone。
        float step = Mathf.Max(1f, materialField.Size.y / 8f);
        for (float y = materialField.Origin.y + materialField.Size.y;
             y >= materialField.Origin.y;
             y -= step)
        {
            Vector3 point = new Vector3(center.x, y, center.z);
            LogMaterialAtWorldPoint(materialField, point, "Column");
        }
    }

    [ContextMenu("Log Material Summary")]
    public void LogMaterialSummary()
    {
        MaterialField materialField = GetMaterialFieldForDebug();
        if (materialField == null) return;

        int air = 0;
        int grass = 0;
        int dirt = 0;
        int stone = 0;

        for (int x = 0; x < materialField.SampleCountX; x++)
        {
            for (int y = 0; y < materialField.SampleCountY; y++)
            {
                for (int z = 0; z < materialField.SampleCountZ; z++)
                {
                    switch (materialField[x, y, z])
                    {
                        case MaterialType.Air: air++; break;
                        case MaterialType.Grass: grass++; break;
                        case MaterialType.Dirt: dirt++; break;
                        case MaterialType.Stone: stone++; break;
                    }
                }
            }
        }

        Debug.Log($"[MaterialField] Air={air}, Grass={grass}, Dirt={dirt}, Stone={stone}");
    }

    private void LogMaterialAtWorldPoint(
        MaterialField materialField,
        Vector3 world,
        string label)
    {
        int x = Mathf.Clamp(
            Mathf.RoundToInt((world.x - materialField.Origin.x) / materialField.CellSizeX),
            0,
            materialField.SampleCountX - 1
        );

        int y = Mathf.Clamp(
            Mathf.RoundToInt((world.y - materialField.Origin.y) / materialField.CellSizeY),
            0,
            materialField.SampleCountY - 1
        );

        int z = Mathf.Clamp(
            Mathf.RoundToInt((world.z - materialField.Origin.z) / materialField.CellSizeZ),
            0,
            materialField.SampleCountZ - 1
        );

        Debug.Log(
            $"[MaterialField] {label} world={world} index=({x},{y},{z}) material={materialField[x, y, z]}"
        );
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

    private DensityField BuildDensityField(out MaterialField materialField)
    {
        int cells = Mathf.Max(1, resolution);

        Vector3 size = new Vector3(
            Mathf.Max(0.01f, terrainSize.x),
            Mathf.Max(0.01f, terrainSize.y),
            Mathf.Max(0.01f, terrainSize.z)
        );

        Vector3 origin = transform.position;

        return DensityFieldGenerator.Generate(
            densityMode,
            cells,
            origin,
            size,
            useNoise,
            height,
            noiseScale,
            noiseStrength,
            cavityCenterOffset,
            cavityRadius,
            grassDepth,
            dirtDepth,
            out materialField
        );
    }

    private Material[] GetMaterials()
    {
        if (materialArray == null)
        {
            materialArray = new Material[3];
        }

        materialArray[0] = grassMaterial != null
            ? grassMaterial
            : GetRuntimeMaterial(0, new Color(0.35f, 0.65f, 0.25f), "Runtime Grass Material");

        materialArray[1] = dirtMaterial != null
            ? dirtMaterial
            : GetRuntimeMaterial(1, new Color(0.45f, 0.30f, 0.18f), "Runtime Dirt Material");

        materialArray[2] = stoneMaterial != null
            ? stoneMaterial
            : GetRuntimeMaterial(2, new Color(0.55f, 0.55f, 0.55f), "Runtime Stone Material");

        return materialArray;
    }

    private Material GetRuntimeMaterial(int index, Color color, string materialName)
    {
        if (runtimeMaterials[index] != null)
        {
            return runtimeMaterials[index];
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null) shader = Shader.Find("Sprites/Default");

        if (shader == null)
        {
            Debug.LogWarning("DensityTerrain: 找不到可用的 Shader，无法创建默认地形材质");
            return null;
        }

        Material material = new Material(shader);
        material.name = materialName;
        material.hideFlags = HideFlags.DontSave;

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }
        else if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }

        runtimeMaterials[index] = material;
        return material;
    }
}
