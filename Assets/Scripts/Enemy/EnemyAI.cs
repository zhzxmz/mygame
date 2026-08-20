using UnityEngine;

/// <summary>
/// 最简单的敌人追踪：只负责向玩家移动，不处理 Health、伤害、死亡、掉落等。
/// 优先使用 Rigidbody 移动；如果敌人没有 Rigidbody，则退化为 Transform 移动。
/// </summary>
public class EnemyAI : MonoBehaviour
{
    [Header("移动参数")]
    public float moveSpeed = 2f;
    public float stoppingDistance = 1.5f;

    private Transform player;
    private Health health;
    private Rigidbody rb;
    private bool hasRigidbody;

    void Awake()
    {
        health = GetComponent<Health>();
        rb = GetComponent<Rigidbody>();
        hasRigidbody = rb != null;
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

        // 敌人死亡后停止移动
        if (health != null && health.IsDead) return;

        float distance = Vector3.Distance(transform.position, player.position);
        if (distance <= stoppingDistance) return;

        Vector3 direction = (player.position - transform.position).normalized;
        Vector3 targetPosition = transform.position + direction * moveSpeed * Time.deltaTime;

        if (hasRigidbody)
        {
            rb.MovePosition(targetPosition);
        }
        else
        {
            transform.position = targetPosition;
        }
    }
}
