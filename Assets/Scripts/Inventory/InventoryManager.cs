using System;
using UnityEngine;

public class InventoryManager : MonoBehaviour
{
    public InventorySlot[] slots;

    private ItemStack[] items;

    /// <summary>背包数据发生变化时触发，用于通知 UI 刷新。</summary>
    public event Action InventoryChanged;

    void Awake()
    {
        EnsureItems();
        RefreshAllSlots();
    }

    /// <summary>
    /// 添加指定数量的物品。
    /// 可堆叠物品会先填充已有堆，再放入空槽；不可堆叠物品每格放 1 个。
    /// 返回实际成功加入的数量。
    /// </summary>
    public int AddItem(ItemData item, int count)
    {
        if (item == null || count <= 0) return 0;

        EnsureItems();

        int remaining = count;

        if (item.stackable)
        {
            int maxStack = Mathf.Max(1, item.maxStack);

            // 优先填充已有堆
            for (int i = 0; i < items.Length && remaining > 0; i++)
            {
                ItemStack stack = items[i];
                if (stack != null && stack.Item == item && stack.Count < maxStack)
                {
                    int canAdd = maxStack - stack.Count;
                    int add = Mathf.Min(canAdd, remaining);

                    items[i] = new ItemStack(item, stack.Count + add);
                    remaining -= add;
                    RefreshSlot(i);
                }
            }

            // 剩余数量放入空槽
            for (int i = 0; i < items.Length && remaining > 0; i++)
            {
                if (items[i] == null)
                {
                    int add = Mathf.Min(maxStack, remaining);

                    items[i] = new ItemStack(item, add);
                    remaining -= add;
                    RefreshSlot(i);
                }
            }
        }
        else
        {
            // 不可堆叠物品：每个占一个槽
            for (int i = 0; i < items.Length && remaining > 0; i++)
            {
                if (items[i] == null)
                {
                    items[i] = new ItemStack(item, 1);
                    remaining--;
                    RefreshSlot(i);
                }
            }
        }

        int added = count - remaining;
        if (added > 0)
        {
            InventoryChanged?.Invoke();
        }

        return added;
    }

    /// <summary>
    /// 移除指定数量的物品。
    /// 从已有堆中扣除，数量变为 0 时清空槽位。
    /// 返回实际移除的数量。
    /// </summary>
    public int RemoveItem(ItemData item, int count)
    {
        if (item == null || count <= 0) return 0;

        EnsureItems();

        int remaining = count;

        for (int i = 0; i < items.Length && remaining > 0; i++)
        {
            ItemStack stack = items[i];
            if (stack != null && stack.Item == item)
            {
                int remove = Mathf.Min(stack.Count, remaining);
                int newCount = stack.Count - remove;

                if (newCount <= 0)
                {
                    items[i] = null;
                    RefreshSlot(i);
                }
                else
                {
                    items[i] = new ItemStack(item, newCount);
                    RefreshSlot(i);
                }

                remaining -= remove;
            }
        }

        int removed = count - remaining;
        if (removed > 0)
        {
            InventoryChanged?.Invoke();
        }

        return removed;
    }

    /// <summary>统计背包中某物品的总数量。</summary>
    public int GetItemCount(ItemData item)
    {
        if (item == null) return 0;

        EnsureItems();

        int total = 0;
        for (int i = 0; i < items.Length; i++)
        {
            ItemStack stack = items[i];
            if (stack != null && stack.Item == item)
            {
                total += stack.Count;
            }
        }

        return total;
    }

    /// <summary>判断背包中某物品数量是否足够。</summary>
    public bool HasItem(ItemData item, int count)
    {
        return GetItemCount(item) >= count;
    }

    /// <summary>
    /// 预检查背包能否容纳指定数量的物品。
    /// 只检查，不修改 items、InventorySlot，也不会触发 InventoryChanged。
    /// </summary>
    public bool CanAddItem(ItemData item, int count)
    {
        if (item == null || count <= 0) return false;

        EnsureItems();

        if (item.stackable)
        {
            int maxStack = Mathf.Max(1, item.maxStack);
            int capacity = 0;

            // 已有同类堆的剩余容量
            for (int i = 0; i < items.Length && capacity < count; i++)
            {
                ItemStack stack = items[i];
                if (stack != null && stack.Item == item && stack.Count < maxStack)
                {
                    capacity += maxStack - stack.Count;
                }
            }

            // 空槽容量
            for (int i = 0; i < items.Length && capacity < count; i++)
            {
                if (items[i] == null)
                {
                    capacity += maxStack;
                }
            }

            return capacity >= count;
        }
        else
        {
            // 不可堆叠物品：每个物品占一个空槽
            int emptySlots = 0;
            for (int i = 0; i < items.Length && emptySlots < count; i++)
            {
                if (items[i] == null)
                {
                    emptySlots++;
                }
            }

            return emptySlots >= count;
        }
    }

    // ---- 旧 API 兼容入口（最小迁移） ----

    public bool AddItem(ItemData item)
    {
        return AddItem(item, 1) > 0;
    }

    public bool RemoveItem(ItemData item)
    {
        return RemoveItem(item, 1) > 0;
    }

    public bool ContainsItem(ItemData item)
    {
        return HasItem(item, 1);
    }

    public InventorySlot FindSlot(ItemData item)
    {
        if (item == null || slots == null) return null;

        EnsureItems();

        for (int i = 0; i < items.Length; i++)
        {
            if (items[i] != null && items[i].Item == item)
            {
                return slots[i];
            }
        }

        return null;
    }

    // ---- 内部同步 ----

    private void EnsureItems()
    {
        int length = slots != null ? slots.Length : 0;

        if (items == null || items.Length != length)
        {
            // items 是唯一真实背包数据源；InventorySlot 只负责显示/同步，不能反向提供数据。
            items = new ItemStack[length];
        }
    }

    private void RefreshSlot(int index)
    {
        if (slots == null || index < 0 || index >= slots.Length) return;
        if (slots[index] == null) return;

        ItemStack stack = items != null && index < items.Length ? items[index] : null;

        if (stack == null)
        {
            slots[index].Clear();
        }
        else
        {
            slots[index].SetStack(stack);
        }
    }

    private void RefreshAllSlots()
    {
        if (slots == null) return;

        for (int i = 0; i < slots.Length; i++)
        {
            RefreshSlot(i);
        }
    }
}
