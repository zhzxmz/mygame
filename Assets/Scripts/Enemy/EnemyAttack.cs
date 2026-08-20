using UnityEngine;

/// <summary>
/// 敌人攻击距离检测。
/// 只负责判断玩家是否进入攻击范围并按冷却输出攻击日志，不处理移动、AI、伤害、掉落等。
/// </summary>
public class EnemyAttack : MonoBehaviour
{
    [Header("攻击参数")]
    public float attackRange = 1.5f;
    public float attackCooldown = 1f;

    private Transform player;
    private Health health;
    private float nextAttackTime;

    void Awake()
    {
        health = GetComponent<Health>();
    }

    void Start()
    {
        MovementController controller = FindObjectOfType<MovementController>();
        if (controller != null)
        {
            player = controller.transform;
        }
    }

    void Update()
    {
        if (player == null) return;

        // 敌人死亡后停止攻击
        if (health != null && health.IsDead) return;

        float distance = Vector3.Distance(transform.position, player.position);
        if (distance > attackRange) return;

        if (Time.time < nextAttackTime) return;

        Debug.Log("Enemy 可以攻击玩家");

        nextAttackTime = Time.time + attackCooldown;
    }
}
