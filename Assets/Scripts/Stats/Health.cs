using System;
using UnityEngine;

public class Health : MonoBehaviour
{
    [SerializeField] private HealthPool healthPool = new HealthPool(100f, 100f);

    public event Action<float, float> OnHealthChanged;
    public event Action OnDeath;

    [Tooltip("死亡时是否销毁 GameObject。敌人默认 true；玩家会通过玩家专用脚本设为 false")]
    public bool destroyOnDeath = true;

    public float maxHP => healthPool != null ? healthPool.Max : 0f;
    public float currentHP => healthPool != null ? healthPool.Current : 0f;
    public bool IsDead { get; private set; }

    /// <summary>当前生命池。未来如需多生命池，可在此扩展为列表/字典。</summary>
    public HealthPool Pool => healthPool;

    void Awake()
    {
        if (healthPool == null)
        {
            healthPool = new HealthPool(100f, 100f);
        }

        healthPool.OnChanged += HandleHealthPoolChanged;
    }

    void OnDestroy()
    {
        if (healthPool != null)
        {
            healthPool.OnChanged -= HandleHealthPoolChanged;
        }
    }

    public void TakeDamage(DamageInfo damage)
    {
        if (damage == null || IsDead) return;

        healthPool.Damage(damage.Amount);
    }

    // 兼容入口：内部转换为 DamageInfo，不复制伤害逻辑。
    public void TakeDamage(int amount)
    {
        TakeDamage(new DamageInfo(amount));
    }

    public void Heal(float amount)
    {
        if (IsDead) return;

        healthPool.Heal(amount);
    }

    private void HandleHealthPoolChanged(float current, float max)
    {
        OnHealthChanged?.Invoke(current, max);
        CheckDeath();
    }

    private void CheckDeath()
    {
        if (IsDead) return;

        if (healthPool != null && healthPool.IsEmpty)
        {
            Die();
        }
    }

    private void Die()
    {
        if (IsDead) return;

        IsDead = true;
        Debug.Log("DIE");
        OnDeath?.Invoke();

        if (destroyOnDeath)
        {
            Destroy(gameObject);
        }
    }
}
