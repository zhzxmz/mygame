using System;
using UnityEngine;

/// <summary>
/// 角色生命组件：只负责伤害、治疗、死亡和事件，不保存第二份 HP。
/// currentHP / maxHP 实际读写同 GameObject 上的 CharacterState。
/// Health.Pool 仍然保留，但它只是绑定到 CharacterState 的 HealthPool 视图，方便旧代码继续调用 SetMax / SetCurrent。
/// </summary>
public class Health : MonoBehaviour
{
    [Tooltip("HP 数据来源。留空会自动查找同 GameObject 上的 CharacterState；玩家的 PlayerState 继承自 CharacterState，也能找到。")]
    [SerializeField] private CharacterState characterState;

    public event Action<float, float> OnHealthChanged;
    public event Action OnDeath;

    [Tooltip("死亡时是否销毁 GameObject。敌人默认 true；玩家会通过玩家专用脚本设为 false")]
    public bool destroyOnDeath = true;

    private HealthPool pool;
    private static bool warnedMissingCharacterState;

    public float maxHP => ResolveState() != null ? characterState.maxHP : 0f;
    public float currentHP => ResolveState() != null ? characterState.currentHP : 0f;
    public bool IsDead { get; private set; }

    /// <summary>兼容旧接口：绑定到 CharacterState 的 HealthPool 视图，不再持有独立的 current / max。</summary>
    public HealthPool Pool => EnsurePool();

    void Awake()
    {
        ResolveState();

        if (characterState == null)
        {
            Debug.LogError("Health: 找不到 CharacterState，HP 无法初始化。", this);
            return;
        }

        ClampCurrentHP();
        EnsurePool();
    }

    void OnDestroy()
    {
        if (pool != null)
        {
            pool.OnChanged -= HandlePoolChanged;
        }
    }

    public void TakeDamage(DamageInfo damage)
    {
        if (damage == null || IsDead || ResolveState() == null) return;

        float amount = Mathf.Max(0f, damage.Amount);
        if (amount <= 0f) return;

        // 与旧 HealthPool.Damage 保持一致：空血时不重复触发事件/死亡检查。
        if (characterState.currentHP <= 0f) return;

        // CharacterState 是唯一血库，这里直接写回。
        characterState.currentHP = Mathf.Max(0f, characterState.currentHP - amount);
        RaiseHealthChanged();
    }

    // 兼容入口：内部转换为 DamageInfo，不复制伤害逻辑。
    public void TakeDamage(int amount)
    {
        TakeDamage(new DamageInfo(amount));
    }

    public void Heal(float amount)
    {
        if (IsDead || ResolveState() == null) return;
        if (amount <= 0f) return;

        // 与旧 HealthPool.Heal 保持一致：满血时不触发事件。
        if (characterState.currentHP >= characterState.maxHP) return;

        float before = characterState.currentHP;
        float healed = Mathf.Min(characterState.maxHP, before + amount);
        if (healed <= before) return;

        // CharacterState 是唯一血库，这里直接写回。
        characterState.currentHP = healed;
        RaiseHealthChanged();
    }

    /// <summary>
    /// 解析并缓存同 GameObject 上的 CharacterState。
    /// 旧敌人 Prefab 可能还没有 CharacterState，这里自动补一个默认组件，保证不会空引用。
    /// </summary>
    private CharacterState ResolveState()
    {
        if (characterState != null && characterState.gameObject == gameObject)
        {
            return characterState;
        }

        if (characterState != null && characterState.gameObject != gameObject)
        {
            Debug.LogWarning("Health: CharacterState 必须挂在同一个 GameObject 上，已忽略 Inspector 中的外部引用。", this);
        }

        characterState = GetComponent<CharacterState>();

        if (characterState == null)
        {
            characterState = gameObject.AddComponent<CharacterState>();

            if (!warnedMissingCharacterState)
            {
                warnedMissingCharacterState = true;
                Debug.LogWarning(
                    "Health: 有物体缺少 CharacterState，已在运行时自动添加。建议在 Prefab / 场景中手动挂载 CharacterState，" +
                    "这样才能在 Inspector 中配置 currentHP / maxHP / attack / defense。",
                    this
                );
            }
        }

        return characterState;
    }

    /// <summary>创建绑定到 CharacterState 的 HealthPool 视图，并监听它的变化以转发 OnHealthChanged。</summary>
    private HealthPool EnsurePool()
    {
        if (pool == null && ResolveState() != null)
        {
            pool = new HealthPool(characterState);
            pool.OnChanged += HandlePoolChanged;
        }

        return pool;
    }

    private void ClampCurrentHP()
    {
        if (characterState == null) return;

        if (characterState.maxHP < 0f)
        {
            characterState.maxHP = 0f;
        }

        characterState.currentHP = Mathf.Clamp(characterState.currentHP, 0f, characterState.maxHP);
    }

    private void HandlePoolChanged(float current, float max)
    {
        OnHealthChanged?.Invoke(current, max);
        CheckDeath();
    }

    private void RaiseHealthChanged()
    {
        // 通过绑定的 Pool 视图触发，这样订阅了 Health.Pool.OnChanged 的旧代码也能收到伤害/治疗事件。
        HealthPool view = EnsurePool();
        if (view != null)
        {
            view.NotifyExternalChange();
            return;
        }

        // 理论上不会到这里（ResolveState 会自动补 CharacterState），仅作空引用兜底。
        OnHealthChanged?.Invoke(currentHP, maxHP);
        CheckDeath();
    }

    private void CheckDeath()
    {
        if (IsDead) return;

        if (characterState != null && characterState.currentHP <= 0f)
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
