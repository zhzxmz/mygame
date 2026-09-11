using UnityEngine;

/// <summary>一条掉落配置：物品 + 数量。</summary>
[System.Serializable]
public class EnemyDropEntry
{
    public ItemData item;
    public int count = 1;
}

public class EnemyDrop : MonoBehaviour
{
    public GameObject worldItemPrefab;

    [Tooltip("掉落列表，敌人死亡时会生成所有物品")]
    public EnemyDropEntry[] drops;

    [Tooltip("敌人死亡时给予玩家的经验值")]
    public int experienceReward = 10;

    private Health health;

    void Awake()
    {
        health = GetComponent<Health>();
        if (health != null)
        {
            health.OnDeath += Drop;
            health.OnDeath += GrantExperience;
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

    void OnDestroy()
    {
        if (health != null)
        {
            health.OnDeath -= Drop;
            health.OnDeath -= GrantExperience;
        }
    }

    private void GrantExperience()
    {
        if (experienceReward <= 0) return;

        MovementController controller = FindObjectOfType<MovementController>();
        if (controller == null) return;

        PlayerProgression progression = controller.GetComponent<PlayerProgression>();
        if (progression != null)
        {
            progression.AddXP(experienceReward);
        }
        else
        {
            Debug.LogWarning("EnemyDrop: 玩家缺少 PlayerProgression 组件，无法给予 XP");
        }
    }

    public void Drop()
    {
        DropSpawner.SpawnDrops(worldItemPrefab, drops, transform);
    }
}
