using System;
using UnityEngine;

namespace TerrainModificationV2
{
    /// <summary>
    /// Terrain Voxel Selector（Step 3A）。
    ///
    /// 职责只包含一次空间坐标转换：
    ///   RaycastHit.point (World Space)
    ///       ↓ DensityTerrain.WorldToLocalPosition
    ///   Terrain Local Space
    ///       ↓ VoxelField.PositionToVoxel
    ///   Vector3Int Voxel Coordinate
    ///
    /// 坐标契约：
    ///   - 地形数据统一使用 Terrain Local Space；DensityTerrain Root Transform
    ///     负责 World Space 放置（position / rotation / scale 都由
    ///     WorldToLocalPosition 处理）。
    ///   - VoxelField 坐标同样是 Terrain Local Space 的整数坐标。
    ///   - Voxel (0,0,0) 对应 Terrain Local [0, VoxelSize)³。
    ///
    /// 边界语义：
    ///   严格使用 VoxelField.PositionToVoxel 的
    ///       floor(position / VoxelSize)
    ///   规则。不使用 Round / Ceil / Nearest / epsilon 修正。
    ///   例如 VoxelSize = 1 时：
    ///       x = 1.0   → Voxel x = 1
    ///       x = -1.0  → Voxel x = -1
    ///       x = -0.1  → Voxel x = -1
    ///
    /// 本类不负责：
    ///   ❌ Raycast / Input / Camera
    ///   ❌ 修改 VoxelField
    ///   ❌ Mining / DensityField / Mesh / MeshCollider
    ///   ❌ Chunk 坐标（Selector 只输出 Voxel 坐标）
    /// </summary>
    public static class TerrainVoxelSelector
    {
        /// <summary>
        /// Terrain Local Space 位置 → Voxel 坐标。
        /// 唯一坐标映射来源是 VoxelField.PositionToVoxel；本类不复制 floor 逻辑。
        /// </summary>
        public static Vector3Int SelectVoxel(
            Vector3 terrainLocalPosition,
            VoxelField voxelField)
        {
            if (voxelField == null)
            {
                throw new ArgumentNullException(nameof(voxelField));
            }

            return voxelField.PositionToVoxel(terrainLocalPosition);
        }

        /// <summary>
        /// Raycast Hit Point → Voxel 坐标（只输出 Voxel）。
        /// </summary>
        public static bool TrySelectVoxel(
            RaycastHit hit,
            DensityTerrain terrain,
            VoxelField voxelField,
            out Vector3Int voxelCoordinate)
        {
            return TrySelectVoxel(
                hit,
                terrain,
                voxelField,
                out voxelCoordinate,
                out _
            );
        }

        /// <summary>
        /// Raycast Hit Point → Voxel 坐标，并同时输出转换后的 Terrain Local Point。
        /// </summary>
        public static bool TrySelectVoxel(
            RaycastHit hit,
            DensityTerrain terrain,
            VoxelField voxelField,
            out Vector3Int voxelCoordinate,
            out Vector3 terrainLocalPoint)
        {
            voxelCoordinate = default(Vector3Int);
            terrainLocalPoint = default(Vector3);

            if (terrain == null)
            {
                return false;
            }

            if (voxelField == null)
            {
                return false;
            }

            // 复用 DensityTerrain 已经定义好的 World → Terrain Local 转换。
            // 不自行创建第二套坐标系统。
            terrainLocalPoint = terrain.WorldToLocalPosition(hit.point);
            voxelCoordinate = voxelField.PositionToVoxel(terrainLocalPoint);
            return true;
        }

        /// <summary>
        /// A 阶段：World Space 命中点 → **实体侧** Voxel（只做坐标解析，不做材料判定）。
        /// 语义与依据：
        ///   1) `RaycastHit.point/normal` 均为 World Space（Unity 约定）；
        ///   2) 法线经 `transform.InverseTransformDirection` 转入 Terrain Local 后取
        ///      **主轴与符号**（Voxel 契约要求 Terrain Local 中轴对齐的面），
        ///      得到指向表面外侧的轴对齐法线；
        ///   3) 沿其反方向（实体侧）偏移 `VoxelSize × 1e-3`（下限 1e-4），
        ///      使探测点严格离开整数平面，避免 `floor`（min-corner 半开区间）
        ///      把“恰好落在平面上的命中点”映射到空气侧；
        ///   4) 偏移量与 VoxelSize 成比例且 ≪ 1 个 Voxel，不会跨过相邻表面。
        /// 不满足时返回 false 并给出 reason（调用方不得静默使用）。
        /// </summary>
        public static bool TryResolveSolidVoxel(
            Vector3 hitPointWorld,
            Vector3 hitNormalWorld,
            DensityTerrain terrain,
            out Vector3Int voxelCoordinate,
            out Vector3 terrainLocalProbe,
            out Vector3 terrainLocalNormal,
            out string reason)
        {
            voxelCoordinate = default(Vector3Int);
            terrainLocalProbe = default(Vector3);
            terrainLocalNormal = default(Vector3);
            reason = string.Empty;

            if (terrain == null)
            {
                reason = "Terrain is null.";
                return false;
            }

            VoxelField voxelField = terrain.VoxelField;
            if (voxelField == null)
            {
                reason = "VoxelField is null.";
                return false;
            }

            Vector3 localPoint = terrain.WorldToLocalPosition(hitPointWorld);
            Vector3 localNormal = terrain.transform.InverseTransformDirection(
                hitNormalWorld);

            if (localNormal.sqrMagnitude <= 1e-12f)
            {
                reason = "Hit normal is degenerate.";
                return false;
            }

            localNormal.Normalize();

            int axis = 0;
            float best = Mathf.Abs(localNormal.x);
            if (Mathf.Abs(localNormal.y) > best)
            {
                axis = 1;
                best = Mathf.Abs(localNormal.y);
            }

            if (Mathf.Abs(localNormal.z) > best)
            {
                axis = 2;
            }

            float sign = localNormal[axis] >= 0f ? 1f : -1f;
            Vector3 axisNormal = Vector3.zero;
            axisNormal[axis] = sign;

            float epsilon = Mathf.Max(1e-4f, voxelField.VoxelSize * 1e-3f);
            Vector3 probe = localPoint - axisNormal * epsilon;

            terrainLocalNormal = axisNormal;
            terrainLocalProbe = probe;
            voxelCoordinate = voxelField.PositionToVoxel(probe);
            return true;
        }

        /// <summary>
        /// A 阶段修正：World Space 命中点 → **有效挖掘目标**。
        /// 有效性 = 实体侧解析成功 ∧ 基础密度场在目标 Voxel 内确有材料
        ///            ∧ 该 Voxel 未被编辑为 Air（VoxelField 编辑状态）。
        ///
        /// “确有材料”由基础密度场在**两个都位于目标 Voxel 内部**的采样点判定（取或）：
        ///   1) Voxel 中心 &gt; 0（既有判据，保留）；
        ///   2) 命中侧探测点 &gt; 0 —— 探测点由命中点沿法线进入实体侧 Δ 后得到，
        ///      而解析出的目标 Voxel 就是包含该探测点的 Voxel。
        /// Marching Cubes 的等值面可能穿过 Voxel 内部（中心采样 ≤ 0，但 Voxel 内
        /// 仍含可见实体表面），此时命中位置内侧属于实体材料，因此 2) 覆盖
        /// “部分实体 Voxel”；又因为两个采样点都在目标 Voxel 内部，不会把相邻
        /// Voxel 的实体误当成当前目标的实体。
        ///
        /// 任一条件不满足 ⇒ false（调用方必须清除选择与高亮，不得静默空操作）。
        /// 数据来源均为既有权威接口（VoxelField 编辑状态 + 基础 DensityField），
        /// 不新建密度/材料真值系统。
        /// </summary>
        public static bool TryResolveMineTarget(
            Vector3 hitPointWorld,
            Vector3 hitNormalWorld,
            DensityTerrain terrain,
            out Vector3Int voxelCoordinate,
            out string reason)
        {
            voxelCoordinate = default(Vector3Int);

            Vector3 terrainLocalProbe;
            if (!TryResolveSolidVoxel(
                hitPointWorld,
                hitNormalWorld,
                terrain,
                out voxelCoordinate,
                out terrainLocalProbe,
                out _,
                out reason))
            {
                return false;
            }

            bool hasBaseMaterial;
            bool isEditedAir;
            if (!terrain.TryGetVoxelMaterialState(
                voxelCoordinate,
                terrainLocalProbe,
                out hasBaseMaterial,
                out isEditedAir,
                out reason))
            {
                return false;
            }

            if (isEditedAir)
            {
                reason = "Voxel " + voxelCoordinate + " is edited as Air.";
                return false;
            }

            if (!hasBaseMaterial)
            {
                // 保留 TryGetVoxelMaterialState 给出的详细采样诊断（中心 / 命中侧探测点），
                // 仅在缺失时补一个兜底原因。
                if (string.IsNullOrEmpty(reason))
                {
                    reason = "Voxel " + voxelCoordinate +
                             " has no base material.";
                }

                return false;
            }

            return true;
        }
    }
}
