using UnityEngine;

public class WorldItem : MonoBehaviour
{
    [SerializeField] private ItemStack stack;

    public SpriteRenderer iconRenderer;

    public ItemStack Stack => stack;
    public ItemData Item => stack != null ? stack.Item : null;
    public int Count => stack != null ? stack.Count : 0;

    // 兼容旧代码访问：worldItem.itemData
    public ItemData itemData => Item;

    public void SetStack(ItemStack stack)
    {
        if (stack != null && stack.Count < 0)
        {
            stack = new ItemStack(stack.Item, 0);
        }

        this.stack = stack;
        RefreshIcon();
    }

    // 兼容旧入口：EnemyDrop 仍调用 SetItem(ItemData)
    public void SetItem(ItemData data)
    {
        if (data == null)
        {
            Debug.LogWarning("WorldItem: itemData 为空，无法设置世界物品");
            return;
        }

        SetStack(new ItemStack(data, 1));
    }

    void OnValidate()
    {
        if (stack != null && stack.Count < 0)
        {
            stack = new ItemStack(stack.Item, 0);
            RefreshIcon();
        }
    }

    private void RefreshIcon()
    {
        if (iconRenderer != null)
        {
            iconRenderer.sprite = Item != null ? Item.icon : null;
        }
    }
}
