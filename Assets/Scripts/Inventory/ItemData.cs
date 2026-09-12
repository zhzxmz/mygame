using UnityEngine;

public enum ItemType
{
    Material,
    Equipment,
    Consumable
}

public enum EquipmentSlot
{
    None,
    Weapon,
    Helmet,
    Armor,
    Accessory
}

[CreateAssetMenu(menuName = "Item/ItemData")]
public class ItemData : ScriptableObject
{
    public string itemName;
    public Sprite icon;

    [Header("物品类型")]
    public ItemType itemType;

    [Header("装备槽位")]
    public EquipmentSlot equipmentSlot;

    [Tooltip("该物品是否可以堆叠")]
    public bool stackable = true;

    [Tooltip("该物品单格最大堆叠数量")]
    public int maxStack = 99;

    [Header("装备属性")]
    [Tooltip("装备后提供的攻击力加成")]
    public int attackBonus;

    [Tooltip("装备后提供的防御力加成（暂未使用）")]
    public int defenseBonus;

    [Tooltip("装备后提供的最大生命加成（暂未使用）")]
    public int maxHPBonus;

    [Header("装备 Buff")]
    [Tooltip("装备后自动获得的 Buff；卸下时移除")]
    public BuffData equippedBuff;
}
