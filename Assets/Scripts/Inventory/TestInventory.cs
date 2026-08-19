using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TestInventory : MonoBehaviour
{
    public InventoryManager inventory;

    public ItemData rubyData;

    void Start()
    {
        if (inventory == null)
        {
            Debug.LogWarning("TestInventory: inventory 未赋值");
            return;
        }

        if (rubyData == null)
        {
            Debug.LogWarning("TestInventory: rubyData 未赋值");
            return;
        }

        inventory.AddItem(rubyData);

        Debug.Log("添加红宝石");
    }
}
