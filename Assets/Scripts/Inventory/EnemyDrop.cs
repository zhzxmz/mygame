using UnityEngine;

public class EnemyDrop : MonoBehaviour
{
    public ItemData dropItem;

    public GameObject worldItemPrefab;

    [Tooltip("掉落数量，最小为 1")]
    public int dropCount = 1;

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
