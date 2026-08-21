using UnityEngine;

/// <summary>
/// 玩家成长系统：XP / Level。
/// 升级时增加 CharacterState 的攻击/防御，以及 Health 的最大/当前生命值。
/// </summary>
public class PlayerProgression : MonoBehaviour
{
    [Header("等级")]
    public int level = 1;

    [Header("经验")]
    public int currentXP = 0;
    public int xpToNextLevel = 100;

    [Header("每级成长")]
    public float attackPerLevel = 2f;
    public float defensePerLevel = 1f;
    public float maxHpPerLevel = 10f;

    /// <summary>增加经验，支持一次获得大量 XP 时连续升级。</summary>
    public void AddXP(int amount)
    {
        if (amount <= 0) return;

        currentXP += amount;

        while (currentXP >= xpToNextLevel)
        {
            currentXP -= xpToNextLevel;
            level++;
            Debug.Log($"Level Up! Level: {level}");

            ApplyLevelUpStats();

            xpToNextLevel += 50;
        }
    }

    private void ApplyLevelUpStats()
    {
        CharacterState stats = GetComponent<CharacterState>();
        if (stats != null)
        {
            stats.attack += attackPerLevel;
            stats.defense += defensePerLevel;
        }

        Health health = GetComponent<Health>();
        if (health != null && health.Pool != null)
        {
            float newMax = health.maxHP + maxHpPerLevel;
            health.Pool.SetMax(newMax);
            health.Pool.SetCurrent(health.currentHP + maxHpPerLevel);
        }
    }
}
