using UnityEngine;

public class EnemyDrop : MonoBehaviour
{
    public ItemData dropItem;

    public GameObject worldItemPrefab;

    [Tooltip("掉落数量，最小为 1")]
    public int dropCount = 1;

    private Health health;

    void Awake()
    {
        health = GetComponent<Health>();
        if (health != null)
        {
            health.OnDeath += Drop;
        }
    }

    void OnValidate()
    {
        if (dropCount < 1)
        {
            dropCount = 1;
        }
    }

    void OnDestroy()
    {
        if (health != null)
        {
            health.OnDeath -= Drop;
        }
    }

    public void Drop()
    {
        if (worldItemPrefab == null)
        {
            Debug.LogWarning("EnemyDrop: worldItemPrefab 没赋值！");
            return;
        }

        if (dropItem == null)
        {
            Debug.LogWarning("EnemyDrop: dropItem 没赋值！");
            return;
        }

        Vector3 dropPosition = transform.position;

        // 向下检测地面，避免掉落物生成在地面以下。
        if (Physics.Raycast(dropPosition, Vector3.down, out RaycastHit hit, 50f))
        {
            dropPosition.y = hit.point.y + 0.1f;
        }

        GameObject obj = Instantiate(worldItemPrefab, dropPosition, Quaternion.identity);

        WorldItem worldItem = obj.GetComponent<WorldItem>();
        if (worldItem == null)
        {
            Debug.LogWarning("EnemyDrop: Prefab 上没有 WorldItem 组件！");
            return;
        }

        worldItem.SetStack(new ItemStack(dropItem, dropCount));
    }
}
