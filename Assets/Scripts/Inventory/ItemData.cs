using System.Collections.Generic;
using UnityEngine;
[CreateAssetMenu(menuName = "Item/ItemData")]
public class ItemData:ScriptableObject
{
    public string itemName;
    public int count;
    public Sprite icon;

    public bool isEmpty => count <= 0;

    public void SetItem(string name, int amount, Sprite sprite = null)
    {
        itemName = name;
        count = amount;
        icon = sprite;
        
    }
    
    

}


    