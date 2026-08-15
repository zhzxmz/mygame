using UnityEngine;

public class InventoryManager : MonoBehaviour
{
    public InventorySlot[] slots;

    public bool AddItem(ItemData item)
    {
        if (item == null)
        {
            Debug.LogWarning("InventoryManager: item 为空，无法添加到背包");
            return false;
        }

        Debug.Log("AddItem 执行");
        foreach (var slot in slots)
        {
            if (slot == null) continue;

            Debug.Log("slot: " + slot);
            Debug.Log(slot.name + " 空吗？ " + slot.IsEmpty());
            if (slot.IsEmpty())
            {
                Debug.Log("准备SetItem: " + slot.name);
                slot.SetItem(item);
                Debug.Log("SetItem已执行");
                return true;
            }
        }

        Debug.Log("背包满了");
        return false;
    }

    public bool RemoveItem(ItemData item)
    {
        InventorySlot slot = FindSlot(item);
        if (slot == null)
        {
            return false;
        }

        slot.Clear();
        return true;
    }

    public bool ContainsItem(ItemData item)
    {
        return FindSlot(item) != null;
    }

    public InventorySlot FindSlot(ItemData item)
    {
        if (item == null)
        {
            return null;
        }

        foreach (var slot in slots)
        {
            if (slot != null && slot.ItemData == item)
            {
                return slot;
            }
        }

        return null;
    }
}
