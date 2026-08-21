using UnityEngine;

/// <summary>
/// 玩家成长系统：XP / Level。
/// 只负责玩家升级数据，不放在 CharacterState 中。
/// </summary>
public class PlayerProgression : MonoBehaviour
{
    [Header("等级")]
    public int level = 1;

    [Header("经验")]
    public int currentXP = 0;
    public int xpToNextLevel = 100;

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

            xpToNextLevel += 50;
        }
    }
}
