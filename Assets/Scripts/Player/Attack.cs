using UnityEngine;

public class Attack : MonoBehaviour
{
    public int attackPower;
    public int finalDamage;

    public void DoAttack(Health target)
    {
        if (target == null) return;

        CharacterState attackerStats = GetComponent<CharacterState>();
        CharacterState targetStats = target.GetComponent<CharacterState>();

        // 优先使用攻击者身上的 CharacterState.attack，没有时回退到 attackPower
        float baseDamage = attackerStats != null ? attackerStats.attack : attackPower;

        // 保持现有目标防御计算逻辑
        if (targetStats != null)
        {
            baseDamage -= targetStats.defense;
        }

        finalDamage = Mathf.Max(0, Mathf.RoundToInt(baseDamage));

        target.TakeDamage(finalDamage);
    }
}
