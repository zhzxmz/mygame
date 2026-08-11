using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class InventoryManager : MonoBehaviour
{
    public InventorySlot[] slots;

    

    
    public void AddItem(ItemData item)
    {
       
        Debug.Log("AddItem 执行");
        foreach (var slot in slots)
        { 
        Debug.Log("slot: " + slot);
        Debug.Log(slot.name + " 空吗？ " + slot.IsEmpty());
            if (slot.IsEmpty())
            {   
                Debug.Log("准备SetItem: " + slot.name);
                slot.SetItem(item);
                Debug.Log("SetItem已执行");
                return; 
            }
        }

        Debug.Log("背包满了");
    }


}
