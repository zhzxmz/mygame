using UnityEngine;

public class WorldItem : MonoBehaviour
{
    public ItemData itemData;

    public SpriteRenderer iconRenderer;

    public void SetItem(ItemData data)
    {
        if (data == null)
        {
            Debug.LogWarning("WorldItem: itemData 为空，无法设置世界物品");
            return;
        }

        itemData = data;

        if (iconRenderer != null)
        {
            iconRenderer.sprite = data.icon;
        }
    }
}
