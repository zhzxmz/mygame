using UnityEngine;

public class Attack : MonoBehaviour
{
    // 兼容旧 Inspector 字段，当前攻击力统一从 CharacterState.attack 读取。
    public int attackPower;

    // 最近一次攻击的最终伤害，调试 / Inspector 显示用。
    public float finalDamage;

    private bool warnedNoAttackerState;
    private bool warnedNoTargetState;

    public void DoAttack(Health target)
    {
        if (target == null) return;

        // Attack 可能挂在剑/子物体上，因此用 GetComponentInParent 获取玩家 CharacterState。
        CharacterState attackerState = GetComponentInParent<CharacterState>();
        if (attackerState == null)
        {
            if (!warnedNoAttackerState)
            {
                Debug.LogWarning("Attack: 攻击者缺少 CharacterState，无法计算攻击力，本次攻击跳过");
                warnedNoAttackerState = true;
            }

            return;
        }

        // 目标必须和它的 Health 在同一个 GameObject 上拥有 CharacterState。
        CharacterState targetState = target.GetComponent<CharacterState>();
        if (targetState == null)
        {
            if (!warnedNoTargetState)
            {
                Debug.LogWarning("Attack: 目标缺少 CharacterState，无法计算防御，本次攻击跳过");
                warnedNoTargetState = true;
            }

            return;
        }

        // 统一最小原型公式：最低造成 1 点伤害。
        finalDamage = Mathf.Max(1f, attackerState.attack - targetState.defense);

        float beforeHP = target.currentHP;
        Debug.Log(
            $"Attack Debug: AttackerATK={attackerState.attack}, " +
            $"TargetDEF={targetState.defense}, " +
            $"FinalDamage={finalDamage}, " +
            $"TargetHPBefore={beforeHP}"
        );

        // 通过现有 DamageInfo 传递最终伤害；DamageInfo 仍只保存 amount + type。
        target.TakeDamage(new DamageInfo(finalDamage));

        Debug.Log($"Attack Debug: TargetHPAfter={target.currentHP}");
    }
}
