using UnityEngine;

/// <summary>
/// 装备与背包的数据连接层。
/// 只负责 Inventory ↔ EquipmentManager 之间的物品转移，不负责 UI。
/// </summary>
public class EquipmentController : MonoBehaviour
{
    public InventoryManager inventory;
    public EquipmentManager equipmentManager;

    [SerializeField] private ItemData testWeapon;

    void Awake()
    {
        if (inventory == null)
        {
            inventory = GetComponent<InventoryManager>();
            if (inventory == null)
            {
                InventoryManager[] managers = FindObjectsOfType<InventoryManager>(true);
                inventory = managers.Length > 0 ? managers[0] : null;
            }

            if (inventory == null)
            {
                Debug.LogWarning("EquipmentController: 未找到 InventoryManager");
            }
        }

        if (equipmentManager == null)
        {
            equipmentManager = GetComponent<EquipmentManager>();
            if (equipmentManager == null)
            {
                Debug.LogWarning("EquipmentController: 未找到 EquipmentManager");
            }
        }
    }

    public bool TryEquipFromInventory(ItemData item)
    {
        Debug.Log($"[EquipmentDebug] TryEquipFromInventory: {item?.name}");

        if (item == null)
        {
            Debug.Log("[EquipmentDebug] TryEquip failed: item is null");
            return false;
        }

        if (item.itemType != ItemType.Equipment)
        {
            Debug.Log($"[EquipmentDebug] TryEquip failed: itemType={item.itemType} != Equipment");
            return false;
        }

        if (item.equipmentSlot != EquipmentSlot.Weapon)
        {
            Debug.Log($"[EquipmentDebug] TryEquip failed: equipmentSlot={item.equipmentSlot} != Weapon");
            return false;
        }

        if (inventory == null || equipmentManager == null)
        {
            Debug.Log($"[EquipmentDebug] TryEquip failed: inventory={inventory != null}, equipmentManager={equipmentManager != null}");
            return false;
        }

        // 防止重复装备当前武器
        if (equipmentManager.GetEquippedWeapon() == item)
        {
            Debug.Log("[EquipmentDebug] TryEquip failed: already equipped this weapon");
            return false;
        }

        if (!inventory.HasItem(item, 1))
        {
            Debug.Log($"[EquipmentDebug] TryEquip failed: inventory does not have {item.name}");
            return false;
        }

        CharacterState stats = GetComponent<CharacterState>();
        float attackBefore = stats != null ? stats.attack : float.NaN;

        // 1. 从背包移除新武器
        int removeResult = inventory.RemoveItem(item, 1);
        Debug.Log($"[EquipmentDebug] RemoveItem result: {removeResult}");
        if (removeResult != 1) return false;

        // 2. 获取旧武器
        ItemData oldWeapon = equipmentManager.GetEquippedWeapon();
        Debug.Log($"[EquipmentDebug] OldWeapon: {oldWeapon?.name ?? "NULL"}");

        // 3. 先把旧武器放回背包，再卸下旧武器，避免武器丢失
        if (oldWeapon != null)
        {
            int addedOld = inventory.AddItem(oldWeapon, 1);
            Debug.Log($"[EquipmentDebug] AddItem oldWeapon result: {addedOld}");
            if (addedOld != 1)
            {
                // 背包放不下旧武器：回滚新武器
                inventory.AddItem(item, 1);
                return false;
            }

            equipmentManager.UnequipWeapon();
        }

        // 4. 装备新武器
        bool equipResult = equipmentManager.Equip(item);
        Debug.Log($"[EquipmentDebug] Equip result: {equipResult}");

        if (!equipResult)
        {
            // 回滚：新武器放回背包
            inventory.AddItem(item, 1);

            // 如果存在旧武器，重新装备旧武器
            if (oldWeapon != null)
            {
                if (equipmentManager.Equip(oldWeapon))
                {
                    inventory.RemoveItem(oldWeapon, 1);
                }
            }

            return false;
        }

        float attackAfter = stats != null ? stats.attack : float.NaN;
        Debug.Log($"[EquipmentDebug] Attack before: {attackBefore}, Attack after: {attackAfter}");

        return true;
    }

    public bool TryUnequip(EquipmentSlot slot)
    {
        if (inventory == null || equipmentManager == null) return false;

        ItemData oldItem = equipmentManager.GetEquippedItem(slot);
        if (oldItem == null) return false;

        // 必须先成功放回背包，再卸下，避免装备丢失
        int added = inventory.AddItem(oldItem, 1);
        if (added != 1) return false;

        equipmentManager.Unequip(slot);
        return true;
    }

    public bool TryUnequipWeapon()
    {
        return TryUnequip(EquipmentSlot.Weapon);
    }

    public void TestEquipFromInventory()
    {
        if (testWeapon == null)
        {
            Debug.LogWarning("EquipmentController: testWeapon 未指定");
            return;
        }

        bool result = TryEquipFromInventory(testWeapon);
        Debug.Log(
            $"EquipmentController TestEquipFromInventory: result={result}, " +
            $"weapon={testWeapon.itemName}, " +
            $"equippedWeapon={(equipmentManager != null && equipmentManager.GetEquippedWeapon() != null ? equipmentManager.GetEquippedWeapon().itemName : "NULL")}"
        );
    }
}
