using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 通用多槽位装备管理器。
/// 使用 Dictionary 存储已装备物品，统一处理攻击/防御/最大生命加成。
/// </summary>
public class EquipmentManager : MonoBehaviour
{
    [SerializeField] private ItemData testWeapon;

    private readonly Dictionary<EquipmentSlot, ItemData> equippedItems = new Dictionary<EquipmentSlot, ItemData>();
    private CharacterState stats;
    private Health health;
    private BuffManager buffManager;

    public event Action<EquipmentSlot, ItemData> OnEquipmentChanged;

    void Awake()
    {
        stats = GetComponent<CharacterState>();
        health = GetComponent<Health>();
        buffManager = GetComponent<BuffManager>();

        if (stats == null)
        {
            Debug.LogWarning("EquipmentManager: 玩家缺少 CharacterState 组件，无法应用装备属性");
        }

        if (health == null)
        {
            Debug.LogWarning("EquipmentManager: 玩家缺少 Health 组件，无法应用生命加成");
        }
    }

    public bool Equip(ItemData item)
    {
        if (item == null) return false;

        if (item.itemType != ItemType.Equipment)
        {
            Debug.LogWarning($"EquipmentManager: {item.name} 不是装备，无法装备");
            return false;
        }

        if (item.equipmentSlot == EquipmentSlot.None)
        {
            Debug.LogWarning($"EquipmentManager: {item.name} 没有指定装备槽位");
            return false;
        }

        // 如果该槽已有旧装备，先移除旧装备属性和旧装备 Buff
        EquipmentSlot slot = item.equipmentSlot;
        if (equippedItems.TryGetValue(slot, out ItemData oldItem))
        {
            RemoveEquipmentStats(oldItem);

            if (oldItem.equippedBuff != null && buffManager != null)
            {
                buffManager.RemoveBuff(oldItem.equippedBuff);
            }
        }

        equippedItems[slot] = item;
        ApplyEquipmentStats(item);

        if (item.equippedBuff != null && buffManager != null)
        {
            buffManager.AddBuff(item.equippedBuff);
        }

        OnEquipmentChanged?.Invoke(slot, item);
        return true;
    }

    public bool Unequip(EquipmentSlot slot)
    {
        if (!equippedItems.TryGetValue(slot, out ItemData item)) return false;

        RemoveEquipmentStats(item);

        if (item.equippedBuff != null && buffManager != null)
        {
            buffManager.RemoveBuff(item.equippedBuff);
        }

        equippedItems.Remove(slot);

        OnEquipmentChanged?.Invoke(slot, null);
        return true;
    }

    public ItemData GetEquippedItem(EquipmentSlot slot)
    {
        equippedItems.TryGetValue(slot, out ItemData item);
        return item;
    }

    public bool IsEquipped(ItemData item)
    {
        if (item == null) return false;

        foreach (ItemData equipped in equippedItems.Values)
        {
            if (equipped == item) return true;
        }

        return false;
    }

    // ---- 兼容包装 ----

    public ItemData GetEquippedWeapon()
    {
        return GetEquippedItem(EquipmentSlot.Weapon);
    }

    public void UnequipWeapon()
    {
        Unequip(EquipmentSlot.Weapon);
    }

    // ---- 属性应用 ----

    private void ApplyEquipmentStats(ItemData item)
    {
        if (item == null) return;

        if (stats != null)
        {
            stats.attack += item.attackBonus;
            stats.defense += item.defenseBonus;
        }

        if (health != null && health.Pool != null)
        {
            float newMax = health.maxHP + item.maxHPBonus;
            health.Pool.SetMax(newMax);
            health.Pool.SetCurrent(health.currentHP + item.maxHPBonus);
        }
    }

    private void RemoveEquipmentStats(ItemData item)
    {
        if (item == null) return;

        if (stats != null)
        {
            stats.attack -= item.attackBonus;
            stats.defense -= item.defenseBonus;
        }

        if (health != null && health.Pool != null)
        {
            float newMax = health.maxHP - item.maxHPBonus;
            health.Pool.SetMax(newMax);

            // 卸下装备时只把当前 HP 限制到新上限，不额外扣除，避免玩家直接死亡。
            float newCurrent = Mathf.Min(health.currentHP, newMax);
            health.Pool.SetCurrent(newCurrent);
        }
    }

    // ---- 测试辅助 ----

    public void TestEquip()
    {
        if (testWeapon == null)
        {
            Debug.LogWarning("EquipmentManager: testWeapon 未指定");
            return;
        }

        bool result = Equip(testWeapon);
        Debug.Log(
            $"EquipmentManager TestEquip: result={result}, " +
            $"weapon={testWeapon.itemName}, " +
            $"slot={testWeapon.equipmentSlot}, " +
            $"equipped={(GetEquippedItem(testWeapon.equipmentSlot) != null ? GetEquippedItem(testWeapon.equipmentSlot).name : "NULL")}"
        );
    }
}
