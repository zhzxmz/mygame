using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TestInventory : MonoBehaviour
{
    public InventoryManager inventory;

    public ItemData rubyData;

    void Start()
    {
        inventory.AddItem(rubyData);

        Debug.Log("添加红宝石");
    }
}
