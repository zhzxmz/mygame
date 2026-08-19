using System;
using UnityEngine;

/// <summary>
/// 表示一组物品数据：物品定义 + 数量。
/// 只负责数据表示，不包含背包逻辑、UI、掉落、拾取或合成逻辑。
/// </summary>
[Serializable]
public class ItemStack
{
    [SerializeField] private ItemData item;
    [SerializeField] private int count;

    public ItemData Item => item;
    public int Count => count;

    public ItemStack()
    {
        item = null;
        count = 0;
    }

    public ItemStack(ItemData item, int count)
    {
        this.item = item;
        this.count = count < 0 ? 0 : count;
    }
}
