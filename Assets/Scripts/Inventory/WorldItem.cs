using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WorldItem : MonoBehaviour
{
    public ItemData itemData;

    public SpriteRenderer iconRenderer;

    public void SetItem(ItemData data)
    {
        itemData = data;

        iconRenderer.sprite = data.icon;
    }
}
