using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace TerrainModificationV2
{
    /// <summary>
    /// Terrain Voxel Highlight（Step 3B）。
    ///
    /// 职责非常单一：
    ///   给定 Voxel Coordinate + VoxelSize，在 World Space 显示该 Voxel 的
    ///   1m³（或指定尺寸）线框 AABB。
    ///
    /// 几何定义（唯一来源）：
    ///   Terrain Local Min = coordinate * voxelSize
    ///   Terrain Local Max = (coordinate + 1) * voxelSize
    ///   Center            = (Min + Max) / 2
    ///   World Corner      = TerrainRoot.TransformPoint(Local Corner)
    ///
    /// 不负责：
    ///   ❌ Raycast / Input / Camera
    ///   ❌ 查询或修改 VoxelField
    ///   ❌ DensityField / MaterialField / Mesh / MeshCollider
    ///   ❌ Mining / Terrain 生成
    ///
    /// 显示形式：
    ///   LineRenderer 12 边线框（24 个世界空间点）。
    ///   不添加任何 Collider，因此不会被 Physics.Raycast 命中。
    ///   同一实例复用；Voxel 未变化时跳过 SetPositions。
    /// </summary>
    [DisallowMultipleComponent]
    public class TerrainVoxelHighlight : MonoBehaviour
    {
        private static readonly int[] EdgeCornerPairs = new int[]
        {
            0, 1, 1, 2, 2, 3, 3, 0,
            4, 5, 5, 6, 6, 7, 7, 4,
            0, 4, 1, 5, 2, 6, 3, 7
        };

        [SerializeField]
        [Tooltip("Voxel 坐标所在的 Terrain Root；留空时使用父 Transform")]
        private Transform terrainRoot;

        [SerializeField]
        [Tooltip("高亮线框颜色")]
        private Color highlightColor = new Color(1f, 1f, 0.2f, 0.95f);

        [SerializeField]
        [Tooltip("高亮线框宽度")]
        private float lineWidth = 0.03f;

        private LineRenderer lineRenderer;
        private Material createdMaterial;

        private bool hasVoxel;
        private Vector3Int currentVoxel;
        private float currentVoxelSize;
        private Transform currentTerrainRoot;

        public bool IsVisible { get; private set; }

        public Vector3Int CurrentVoxel
        {
            get { return currentVoxel; }
        }

        public float CurrentVoxelSize
        {
            get { return currentVoxelSize; }
        }

        public Color HighlightColor
        {
            get { return highlightColor; }
            set
            {
                highlightColor = value;
                ApplyMaterialColor();
            }
        }

        public float LineWidth
        {
            get { return lineWidth; }
            set
            {
                lineWidth = Mathf.Max(0.001f, value);
                ApplyLineWidth();
            }
        }

        /// <summary>设置 Voxel 坐标所在的 Terrain Root。不创建 / 销毁任何对象。</summary>
        public void Initialize(Transform root)
        {
            terrainRoot = root;
        }

        /// <summary>显示指定 Voxel 的 AABB 线框。Voxel 未变化时不会重复设置几何。</summary>
        public void Show(Vector3Int voxelCoordinate, float voxelSize)
        {
            if (!IsFinitePositive(voxelSize))
            {
                Hide();
                return;
            }

            Transform root = ResolveTerrainRoot();
            if (root == null)
            {
                Hide();
                return;
            }

            bool sameGeometry =
                IsVisible &&
                hasVoxel &&
                currentVoxel == voxelCoordinate &&
                Mathf.Abs(currentVoxelSize - voxelSize) < 0.00001f &&
                currentTerrainRoot == root;

            if (sameGeometry) return;

            Vector3 localMin = GetTerrainLocalMin(voxelCoordinate, voxelSize);
            Vector3 localMax = GetTerrainLocalMax(voxelCoordinate, voxelSize);
            Vector3[] localCorners = GetTerrainLocalCorners(localMin, localMax);

            Vector3[] worldLinePoints = new Vector3[EdgeCornerPairs.Length];
            for (int i = 0; i < EdgeCornerPairs.Length; i++)
            {
                worldLinePoints[i] = root.TransformPoint(
                    localCorners[EdgeCornerPairs[i]]
                );
            }

            EnsureLineRenderer();
            lineRenderer.positionCount = worldLinePoints.Length;
            lineRenderer.SetPositions(worldLinePoints);
            lineRenderer.enabled = true;

            currentVoxel = voxelCoordinate;
            currentVoxelSize = voxelSize;
            currentTerrainRoot = root;
            hasVoxel = true;
            IsVisible = true;
        }

        public void Hide()
        {
            IsVisible = false;

            if (lineRenderer != null)
            {
                lineRenderer.enabled = false;
            }
        }

        /// <summary>最近一次 Show 的 Terrain Local Min（几何定义值）。</summary>
        public Vector3 TerrainLocalMin
        {
            get
            {
                return hasVoxel
                    ? GetTerrainLocalMin(currentVoxel, currentVoxelSize)
                    : default(Vector3);
            }
        }

        /// <summary>最近一次 Show 的 Terrain Local Max（几何定义值）。</summary>
        public Vector3 TerrainLocalMax
        {
            get
            {
                return hasVoxel
                    ? GetTerrainLocalMax(currentVoxel, currentVoxelSize)
                    : default(Vector3);
            }
        }

        /// <summary>当前高亮 Voxel 的 8 个 World Space 角点；未显示时返回 null。</summary>
        public Vector3[] GetWorldCorners()
        {
            if (!IsVisible || !hasVoxel || currentTerrainRoot == null)
            {
                return null;
            }

            Vector3[] localCorners = GetTerrainLocalCorners(
                TerrainLocalMin,
                TerrainLocalMax
            );

            Vector3[] worldCorners = new Vector3[localCorners.Length];
            for (int i = 0; i < localCorners.Length; i++)
            {
                worldCorners[i] = currentTerrainRoot.TransformPoint(localCorners[i]);
            }

            return worldCorners;
        }

        /// <summary>当前高亮 Voxel 的 World Space AABB（按 8 个角点取 min/max）。</summary>
        public bool TryGetWorldBounds(out Vector3 worldMin, out Vector3 worldMax)
        {
            worldMin = default(Vector3);
            worldMax = default(Vector3);

            Vector3[] worldCorners = GetWorldCorners();
            if (worldCorners == null || worldCorners.Length == 0)
            {
                return false;
            }

            worldMin = worldCorners[0];
            worldMax = worldCorners[0];

            for (int i = 1; i < worldCorners.Length; i++)
            {
                worldMin = Vector3.Min(worldMin, worldCorners[i]);
                worldMax = Vector3.Max(worldMax, worldCorners[i]);
            }

            return true;
        }

        // ------------------------------------------------------------------
        // 唯一几何定义：Min = coordinate * size；Max = (coordinate + 1) * size
        // ------------------------------------------------------------------

        public static Vector3 GetTerrainLocalMin(Vector3Int voxelCoordinate, float voxelSize)
        {
            return new Vector3(
                voxelCoordinate.x * voxelSize,
                voxelCoordinate.y * voxelSize,
                voxelCoordinate.z * voxelSize
            );
        }

        public static Vector3 GetTerrainLocalMax(Vector3Int voxelCoordinate, float voxelSize)
        {
            return new Vector3(
                (voxelCoordinate.x + 1) * voxelSize,
                (voxelCoordinate.y + 1) * voxelSize,
                (voxelCoordinate.z + 1) * voxelSize
            );
        }

        public static Vector3 GetTerrainLocalCenter(Vector3Int voxelCoordinate, float voxelSize)
        {
            return (GetTerrainLocalMin(voxelCoordinate, voxelSize)
                    + GetTerrainLocalMax(voxelCoordinate, voxelSize)) * 0.5f;
        }

        private static Vector3[] GetTerrainLocalCorners(Vector3 localMin, Vector3 localMax)
        {
            return new Vector3[]
            {
                new Vector3(localMin.x, localMin.y, localMin.z),
                new Vector3(localMax.x, localMin.y, localMin.z),
                new Vector3(localMax.x, localMin.y, localMax.z),
                new Vector3(localMin.x, localMin.y, localMax.z),
                new Vector3(localMin.x, localMax.y, localMin.z),
                new Vector3(localMax.x, localMax.y, localMin.z),
                new Vector3(localMax.x, localMax.y, localMax.z),
                new Vector3(localMin.x, localMax.y, localMax.z)
            };
        }

        private Transform ResolveTerrainRoot()
        {
            return terrainRoot != null ? terrainRoot : transform.parent;
        }

        private void Awake()
        {
            EnsureLineRenderer();
            Hide();
        }

        private void OnDestroy()
        {
            if (createdMaterial != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(createdMaterial);
                }
                else
                {
                    DestroyImmediate(createdMaterial);
                }

                createdMaterial = null;
            }
        }

        private void EnsureLineRenderer()
        {
            if (lineRenderer == null)
            {
                lineRenderer = GetComponent<LineRenderer>();
            }

            if (lineRenderer == null)
            {
                lineRenderer = gameObject.AddComponent<LineRenderer>();
            }

            lineRenderer.useWorldSpace = true;
            lineRenderer.loop = false;
            lineRenderer.shadowCastingMode = ShadowCastingMode.Off;
            lineRenderer.receiveShadows = false;
            lineRenderer.numCapVertices = 0;
            lineRenderer.numCornerVertices = 0;
            lineRenderer.sharedMaterial = GetOrCreateMaterial();

            ApplyLineWidth();
            ApplyMaterialColor();
        }

        private Material GetOrCreateMaterial()
        {
            if (createdMaterial != null) return createdMaterial;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Color");

            if (shader == null)
            {
                Debug.LogWarning(
                    "[TerrainVoxelHighlight] 找不到可用的线框 Shader。"
                );
                return null;
            }

            createdMaterial = new Material(shader);
            createdMaterial.name = "TerrainVoxelHighlightLine";
            createdMaterial.hideFlags = HideFlags.HideAndDontSave;
            createdMaterial.color = highlightColor;

            return createdMaterial;
        }

        private void ApplyLineWidth()
        {
            if (lineRenderer == null) return;

            float width = Mathf.Max(0.001f, lineWidth);
            lineRenderer.startWidth = width;
            lineRenderer.endWidth = width;
        }

        private void ApplyMaterialColor()
        {
            if (lineRenderer == null || createdMaterial == null) return;

            createdMaterial.color = highlightColor;
        }

        private static bool IsFinitePositive(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value) &&
                   value > 0f;
        }
    }
}
