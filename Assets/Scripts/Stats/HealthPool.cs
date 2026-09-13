using System;
using UnityEngine;

/// <summary>
/// 生命池视图。
/// 这个类不再自己保存 current / max，只绑定 CharacterState，并把它包装成旧的 HealthPool 接口。
/// Health.Pool 返回的就是这个视图，所以旧的 health.Pool.SetMax / SetCurrent 调用仍然可用，
/// 但所有数据最终都写进 CharacterState.currentHP / maxHP，实际血量只有这一份。
/// </summary>
public class HealthPool
{
    private CharacterState state;

    public event Action<float, float> OnChanged;

    public float Current => state != null ? state.currentHP : 0f;
    public float Max => state != null ? state.maxHP : 0f;

    public bool IsEmpty => Current <= 0f;
    public bool IsFull => Current >= Max;

    /// <summary>当前生命值比例，0 ~ 1。Max <= 0 时返回 0。</summary>
    public float Ratio => Max <= 0f ? 0f : Current / Max;

    /// <summary>
    /// 绑定到 CharacterState。绑定后所有读写都会落到 CharacterState。
    /// </summary>
    public HealthPool(CharacterState state)
    {
        this.state = state;
    }

    /// <summary>造成伤害，返回实际扣除的生命值。</summary>
    public virtual float Damage(float amount)
    {
        if (state == null || amount <= 0f || IsEmpty) return 0f;

        float before = state.currentHP;
        float after = Mathf.Max(0f, before - amount);
        state.currentHP = after;

        NotifyChanged();
        return before - after;
    }

    /// <summary>治疗，返回实际恢复的生命值。</summary>
    public virtual float Heal(float amount)
    {
        if (state == null || amount <= 0f || IsFull) return 0f;

        float before = state.currentHP;
        float after = Mathf.Min(Mathf.Max(0f, state.maxHP), before + amount);

        if (after <= before) return 0f;

        state.currentHP = after;
        NotifyChanged();
        return after - before;
    }

    public virtual void SetMax(float newMax)
    {
        if (state == null) return;

        float clampedMax = Mathf.Max(0f, newMax);
        state.maxHP = clampedMax;

        if (state.currentHP > clampedMax) state.currentHP = clampedMax;
        if (state.currentHP < 0f) state.currentHP = 0f;

        NotifyChanged();
    }

    public virtual void SetCurrent(float newCurrent)
    {
        if (state == null) return;

        float clampedMax = Mathf.Max(0f, state.maxHP);
        state.currentHP = Mathf.Clamp(newCurrent, 0f, clampedMax);

        NotifyChanged();
    }

    public virtual void Fill()
    {
        if (state == null || IsFull) return;

        SetCurrent(state.maxHP);
    }

    public virtual void Reset()
    {
        Fill();
    }

    /// <summary>供 Health 在直接写入 CharacterState 后触发 OnChanged，保持旧 HealthPool 事件行为。</summary>
    internal void NotifyExternalChange()
    {
        NotifyChanged();
    }

    protected virtual void NotifyChanged()
    {
        OnChanged?.Invoke(Current, Max);
    }
}
