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
    [Tooltip("攻击倍率，最终伤害 = CharacterState.attack * damageMultiplier")]
    public float damageMultiplier = 1f;

    private Transform player;
    private Health enemyHealth;
    private Health playerHealth;
    private CharacterState stats;
    private float nextAttackTime;
    private bool warnedNoPlayerHealth;
    private bool warnedNoCharacterState;

    void Awake()
    {
        enemyHealth = GetComponent<Health>();
    }

    void Start()
    {
        stats = GetComponent<CharacterState>();

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

        // 敌人没有 CharacterState 时只警告一次，并跳过本次攻击。
        if (stats == null)
        {
            if (!warnedNoCharacterState)
            {
                Debug.LogWarning("EnemyAttack: 敌人没有 CharacterState，无法计算攻击力");
                warnedNoCharacterState = true;
            }

            return;
        }

        // 最终伤害基于自身 CharacterState.attack，倍率保留攻击动作差异。
        int finalDamage = Mathf.Max(
            0,
            Mathf.RoundToInt(stats.attack * damageMultiplier)
        );

        // 实际造成伤害
        playerHealth.TakeDamage(finalDamage);

        nextAttackTime = Time.time + attackCooldown;
    }
}
