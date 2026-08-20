using UnityEngine;

/// <summary>
/// 敌人攻击逻辑。
/// 只负责在玩家进入攻击范围且冷却结束时调用玩家 Health.TakeDamage()，
/// 不处理移动、AI、掉落等。
/// </summary>
public class EnemyAttack : MonoBehaviour
{
    [Header("攻击参数")]
    public float attackRange = 1.5f;
    public float attackCooldown = 1f;
    public int damage = 10;

    private Transform player;
    private Health enemyHealth;
    private Health playerHealth;
    private float nextAttackTime;
    private bool warnedNoPlayerHealth;

    void Awake()
    {
        enemyHealth = GetComponent<Health>();
    }

    void Start()
    {
        MovementController controller = FindObjectOfType<MovementController>();
        if (controller != null)
        {
            player = controller.transform;
            playerHealth = player.GetComponent<Health>();
        }
    }

    void Update()
    {
        if (player == null) return;

        // 敌人死亡后停止攻击
        if (enemyHealth != null && enemyHealth.IsDead) return;

        // 玩家没有 Health 时明确警告，不报 NRE
        if (playerHealth == null)
        {
            if (!warnedNoPlayerHealth)
            {
                Debug.LogWarning("EnemyAttack: 玩家没有 Health 组件，无法造成伤害");
                warnedNoPlayerHealth = true;
            }

            return;
        }

        float distance = Vector3.Distance(transform.position, player.position);
        if (distance > attackRange) return;

        if (Time.time < nextAttackTime) return;

        // 实际造成伤害
        playerHealth.TakeDamage(damage);

        nextAttackTime = Time.time + attackCooldown;
    }
}
