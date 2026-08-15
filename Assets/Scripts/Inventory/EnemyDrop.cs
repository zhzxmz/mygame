using UnityEngine;

public class EnemyDrop : MonoBehaviour
{
    public ItemData dropItem;

    public GameObject worldItemPrefab;

    private Health health;

    void Awake()
    {
        health = GetComponent<Health>();
        if (health != null)
        {
            health.OnDeath += Drop;
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

        GameObject obj = Instantiate(worldItemPrefab, transform.position, Quaternion.identity);

        WorldItem worldItem = obj.GetComponent<WorldItem>();
        if (worldItem == null)
        {
            Debug.LogWarning("EnemyDrop: Prefab 上没有 WorldItem 组件！");
            return;
        }

        worldItem.SetItem(dropItem);
    }
}
