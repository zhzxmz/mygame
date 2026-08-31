using UnityEngine;

/// <summary>
/// 装备管理器（第一阶段只支持武器装备）。
/// 负责把武器 attackBonus 应用到 CharacterState.attack，并在换装/卸下时正确移除。
/// </summary>
public class EquipmentManager : MonoBehaviour
{
    public ItemData equippedWeapon;

    [SerializeField] private ItemData testWeapon;

    private CharacterState stats;
    private float appliedAttackBonus;

    void Awake()
    {
        stats = GetComponent<CharacterState>();

        if (stats == null)
        {
            Debug.LogWarning("EquipmentManager: 玩家缺少 CharacterState 组件，无法应用装备属性");
        }
    }

    void Start()
    {
        // 如果 Inspector 中已经指定了 equippedWeapon，则在启动时应用它的攻击加成。
        // 注意：不会自动装备 testWeapon。
        if (equippedWeapon != null &&
            stats != null &&
            appliedAttackBonus == 0f &&
            equippedWeapon.itemType == ItemType.Equipment &&
            equippedWeapon.equipmentSlot == EquipmentSlot.Weapon)
        {
            appliedAttackBonus = equippedWeapon.attackBonus;
            stats.attack += appliedAttackBonus;
        }
    }

    public bool Equip(ItemData item)
    {
        Debug.Log($"[EquipmentDebug] EquipmentManager.Equip: item={item?.name}, currentATK={stats?.attack}");

        if (item == null) return false;

        if (item.itemType != ItemType.Equipment)
        {
            Debug.LogWarning($"EquipmentManager: {item.name} 不是装备，无法装备");
            return false;
        }

        if (item.equipmentSlot != EquipmentSlot.Weapon)
        {
            Debug.LogWarning($"EquipmentManager: {item.name} 不是武器，当前只支持 Weapon 槽位");
            return false;
        }

        if (stats == null)
        {
            Debug.LogWarning("EquipmentManager: 缺少 CharacterState 组件，无法装备");
            return false;
        }

        // 先移除旧武器提供的攻击加成
        if (equippedWeapon != null)
        {
            stats.attack -= appliedAttackBonus;
        }

        equippedWeapon = item;
        appliedAttackBonus = item.attackBonus;
        stats.attack += item.attackBonus;

        Debug.Log($"[EquipmentDebug] EquipmentManager.Equip success: weapon={item.name}, attackBonus={item.attackBonus}, afterATK={stats.attack}");
        return true;
    }

    public void UnequipWeapon()
    {
        if (equippedWeapon == null) return;

        if (stats == null)
        {
            Debug.LogWarning("EquipmentManager: 缺少 CharacterState 组件，无法卸下装备");
        }
        else
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

    /// <summary>测试辅助：装备 Inspector 中指定的 testWeapon。</summary>
    public void TestEquip()
    {
        if (testWeapon == null)
        {
            Debug.LogWarning("EquipmentManager: testWeapon 未指定");
            return;
        }

        if (stats == null)
        {
            Debug.LogWarning("EquipmentManager: 缺少 CharacterState 组件，无法测试装备");
            return;
        }

        float beforeAttack = stats.attack;
        float beforeBonus = appliedAttackBonus;

        bool result = Equip(testWeapon);

        Debug.Log(
            $"EquipmentManager TestEquip: result={result}, " +
            $"weapon={testWeapon.itemName}, " +
            $"attackBonus={testWeapon.attackBonus}, " +
            $"beforeAttack={beforeAttack}, " +
            $"beforeAppliedBonus={beforeBonus}, " +
            $"afterAttack={stats.attack}, " +
            $"appliedAttackBonus={appliedAttackBonus}"
        );
    }
}
