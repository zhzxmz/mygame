using UnityEngine;

/// <summary>
/// 可采集/破坏的世界资源节点，例如树、石头、铁矿。
/// 只负责监听 Health 死亡并调用通用掉落逻辑。
/// </summary>
public class ResourceNode : MonoBehaviour
{
    public GameObject worldItemPrefab;

    [Tooltip("资源被破坏时的掉落列表")]
    public EnemyDropEntry[] drops;

    private Health health;

    void Awake()
    {
        health = GetComponent<Health>();

        if (health != null)
        {
            health.OnDeath += HandleDestroyed;
        }
        else
        {
            Debug.LogWarning($"ResourceNode: {name} 缺少 Health 组件，无法监听破坏事件");
        }
    }

    void OnDestroy()
    {
        if (health != null)
        {
            health.OnDeath -= HandleDestroyed;
        }
    }

    void OnValidate()
    {
        if (drops == null) return;

        foreach (EnemyDropEntry drop in drops)
        {
            if (drop != null && drop.count < 1)
            {
                drop.count = 1;
            }
        }
    }

    private void HandleDestroyed()
    {
        DropSpawner.SpawnDrops(worldItemPrefab, drops, transform);
    }
}
