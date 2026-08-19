using System;
using UnityEngine;

/// <summary>
/// 可扩展的生命池基础类。
/// 只负责表示一条生命池的当前值与最大值，并提供 Damage / Heal / IsEmpty 等基础能力。
/// 不依赖 MonoBehaviour，可被角色、敌人、玩家等脚本继承或组合使用。
/// </summary>
[Serializable]
public class HealthPool
{
    [SerializeField] private float current;
    [SerializeField] private float max;

    public event Action<float, float> OnChanged;

    public float Current => current;
    public float Max => max;

    public bool IsEmpty => current <= 0f;
    public bool IsFull => current >= max;

    /// <summary>当前生命值比例，0 ~ 1。Max <= 0 时返回 0。</summary>
    public float Ratio => max <= 0f ? 0f : current / max;

    public HealthPool() : this(100f)
    {
    }

    public HealthPool(float max) : this(max, max)
    {
    }

    public HealthPool(float max, float current)
    {
        this.max = Mathf.Max(0f, max);
        this.current = Mathf.Clamp(current, 0f, this.max);
    }

    /// <summary>造成伤害，返回实际扣除的生命值。</summary>
    public virtual float Damage(float amount)
    {
        if (amount <= 0f || IsEmpty) return 0f;

        float before = current;
        current = Mathf.Max(0f, current - amount);
        NotifyChanged();
        return before - current;
    }

    /// <summary>治疗，返回实际恢复的生命值。</summary>
    public virtual float Heal(float amount)
    {
        if (amount <= 0f || IsFull) return 0f;

        float before = current;
        current = Mathf.Min(max, current + amount);
        NotifyChanged();
        return current - before;
    }

    public virtual void SetMax(float newMax)
    {
        max = Mathf.Max(0f, newMax);
        if (current > max)
        {
            current = max;
        }
        NotifyChanged();
    }

    public virtual void SetCurrent(float newCurrent)
    {
        current = Mathf.Clamp(newCurrent, 0f, max);
        NotifyChanged();
    }

    public virtual void Fill()
    {
        if (current >= max) return;

        current = max;
        NotifyChanged();
    }

    public virtual void Reset()
    {
        Fill();
    }

    protected virtual void NotifyChanged()
    {
        OnChanged?.Invoke(current, max);
    }
}
