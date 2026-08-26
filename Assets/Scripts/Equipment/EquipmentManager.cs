using UnityEngine;

/// <summary>
/// 装备管理器（第一阶段只支持武器装备）。
/// 负责把武器 attackBonus 应用到 CharacterState.attack，并在换装/卸下时正确移除。
/// </summary>
public class EquipmentManager : MonoBehaviour
{
    public ItemData equippedWeapon;

    private CharacterState stats;
    private float appliedAttackBonus;

    void Awake()
    {
        stats = GetComponent<CharacterState>();
    }

    public bool Equip(ItemData item)
    {
        if (item == null) return false;
        if (stats == null) return false;

        // 先移除旧武器提供的攻击加成
        if (equippedWeapon != null)
        {
            stats.attack -= appliedAttackBonus;
        }

        equippedWeapon = item;
        appliedAttackBonus = item.attackBonus;
        stats.attack += item.attackBonus;

        return true;
    }

    public void UnequipWeapon()
    {
        if (equippedWeapon == null) return;

        if (stats != null)
        {
            stats.attack -= appliedAttackBonus;
        }

        appliedAttackBonus = 0f;
        equippedWeapon = null;
    }

    public ItemData GetEquippedWeapon()
    {
        return equippedWeapon;
    }

    /// <summary>测试辅助：直接装备指定物品。</summary>
    public void TestEquip(ItemData item)
    {
        Equip(item);
    }
}
