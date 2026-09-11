using UnityEngine;

/// <summary>
/// 通用掉落生成逻辑。
/// Enemy 和 ResourceNode 共用同一套 WorldItem 生成流程。
/// </summary>
public static class DropSpawner
{
    public static void SpawnDrops(GameObject worldItemPrefab, EnemyDropEntry[] drops, Transform origin)
    {
        if (worldItemPrefab == null)
        {
            Debug.LogWarning("DropSpawner: worldItemPrefab 没赋值！");
            return;
        }

        if (drops == null || drops.Length == 0)
        {
            Debug.LogWarning("DropSpawner: drops 为空");
            return;
        }

        if (origin == null)
        {
            Debug.LogWarning("DropSpawner: origin 为空");
            return;
        }

        foreach (EnemyDropEntry drop in drops)
        {
            if (drop == null || drop.item == null)
            {
                Debug.LogWarning("DropSpawner: 掉落项无效，跳过");
                continue;
            }

            Vector3 dropPosition = origin.position;

            // 向下检测地面，避免掉落物生成在地面以下。
            if (Physics.Raycast(dropPosition, Vector3.down, out RaycastHit hit, 50f))
            {
                dropPosition.y = hit.point.y + 0.1f;
            }

            GameObject obj = Object.Instantiate(worldItemPrefab, dropPosition, Quaternion.identity);

            WorldItem worldItem = obj.GetComponent<WorldItem>();
            if (worldItem == null)
            {
                Debug.LogWarning("DropSpawner: Prefab 上没有 WorldItem 组件！");
                continue;
            }

            worldItem.SetStack(new ItemStack(drop.item, drop.count));
        }
    }
}
