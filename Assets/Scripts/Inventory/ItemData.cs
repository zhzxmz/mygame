using UnityEngine;

[CreateAssetMenu(menuName = "Item/ItemData")]
public class ItemData : ScriptableObject
{
    public string itemName;
    public Sprite icon;

    [Tooltip("该物品是否可以堆叠")]
    public bool stackable = true;

    [Tooltip("该物品单格最大堆叠数量")]
    public int maxStack = 99;
}
