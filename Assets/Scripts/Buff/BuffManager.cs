using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 第一版 Buff 管理器。
/// 使用 List 保存当前 Buff，并在 Buff 变化时重新计算属性加成。
/// </summary>
public class BuffManager : MonoBehaviour
{
    private readonly List<BuffData> activeBuffs = new List<BuffData>();

    private CharacterState stats;
    private Health health;

    private float appliedAttackBonus;
    private float appliedDefenseBonus;
    private float appliedMaxHPBonus;

    void Awake()
    {
        stats = GetComponent<CharacterState>();
        health = GetComponent<Health>();
    }

    public bool HasBuff(BuffData buff)
    {
        return buff != null && activeBuffs.Contains(buff);
    }

    public void AddBuff(BuffData buff)
    {
        if (buff == null) return;
        if (activeBuffs.Contains(buff)) return;

        activeBuffs.Add(buff);
        Debug.Log($"[BuffManager] Add Buff: {buff.buffName}");
        RecalculateBuffs();
    }

    public void RemoveBuff(BuffData buff)
    {
        if (buff == null) return;
        if (!activeBuffs.Remove(buff)) return;

        Debug.Log($"[BuffManager] Remove Buff: {buff.buffName}");
        RecalculateBuffs();
    }

    private void RecalculateBuffs()
    {
        float targetAttack = 0f;
        float targetDefense = 0f;
        float targetMaxHP = 0f;

        foreach (BuffData buff in activeBuffs)
        {
            if (buff == null) continue;

            targetAttack += buff.attackBonus;
            targetDefense += buff.defenseBonus;
            targetMaxHP += buff.maxHPBonus;
        }

        if (stats != null)
        {
            stats.attack += targetAttack - appliedAttackBonus;
            stats.defense += targetDefense - appliedDefenseBonus;
        }

        if (health != null && health.Pool != null)
        {
            float deltaMax = targetMaxHP - appliedMaxHPBonus;

            if (deltaMax != 0f)
            {
                float newMax = health.maxHP + deltaMax;
                health.Pool.SetMax(newMax);

                if (deltaMax > 0f)
                {
                    health.Pool.SetCurrent(health.currentHP + deltaMax);
                }
                else
                {
                    health.Pool.SetCurrent(Mathf.Min(health.currentHP, newMax));
                }
            }
        }

        appliedAttackBonus = targetAttack;
        appliedDefenseBonus = targetDefense;
        appliedMaxHPBonus = targetMaxHP;
    }
}
