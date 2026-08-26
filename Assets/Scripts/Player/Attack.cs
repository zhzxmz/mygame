using UnityEngine;

public class Attack : MonoBehaviour
{
    public int attackPower;
    public int finalDamage;

    public void DoAttack(Health target)
    {
        if (target == null) return;

        // Attack 可能挂在剑/子物体上，因此用 GetComponentInParent 获取玩家 CharacterState
        CharacterState attackerStats = GetComponentInParent<CharacterState>();
        CharacterState targetStats = target.GetComponent<CharacterState>();

        // 优先使用攻击者身上的 CharacterState.attack，没有时回退到 attackPower
        float rawAttack = attackerStats != null ? attackerStats.attack : attackPower;
        float baseDamage = rawAttack;

        float targetDefense = targetStats != null ? targetStats.defense : 0f;

        // 保持现有目标防御计算逻辑
        if (targetStats != null)
        {
            baseDamage -= targetStats.defense;
        }

        finalDamage = Mathf.Max(0, Mathf.RoundToInt(baseDamage));

        float beforeHP = target.currentHP;
        Debug.Log(
            $"Attack Debug: AttackerATK={rawAttack}, " +
            $"TargetDEF={targetDefense}, " +
            $"BaseDamage={baseDamage}, " +
            $"FinalDamage={finalDamage}, " +
            $"TargetHPBefore={beforeHP}"
        );

        target.TakeDamage(finalDamage);

        Debug.Log($"Attack Debug: TargetHPAfter={target.currentHP}");
    }
}
