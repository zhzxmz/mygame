using UnityEngine;

/// <summary>
/// 敌人基础身份与生命周期。
/// 只负责监听 Health.OnDeath 并进入死亡状态，不实现 AI、攻击、动画或复杂行为。
/// </summary>
public class Enemy : MonoBehaviour
{
    private Health health;
    private bool isDead;

    void Awake()
    {
        health = GetComponent<Health>();

        if (health == null)
        {
            Debug.LogWarning($"Enemy: {name} 缺少 Health 组件，无法监听死亡");
        }
    }

    void OnEnable()
    {
        if (health != null)
        {
            health.OnDeath += HandleDeath;
        }
    }

    void OnDisable()
    {
        if (health != null)
        {
            health.OnDeath -= HandleDeath;
        }
    }

    private void HandleDeath()
    {
        if (isDead) return;

        isDead = true;
        Debug.Log($"Enemy 死亡: {name}");

        // 掉落由 EnemyDrop 自行监听 Health.OnDeath 处理，这里不重复实现。
    }
}
