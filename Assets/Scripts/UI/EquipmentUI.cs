using UnityEngine;

/// <summary>
/// 管理整个装备栏 UI。
/// 监听 EquipmentManager.OnEquipmentChanged 并刷新对应装备槽。
/// </summary>
public class EquipmentUI : MonoBehaviour
{
    [SerializeField] private EquipmentManager equipmentManager;

    [SerializeField] private EquipmentSlotUI weaponSlot;
    [SerializeField] private EquipmentSlotUI helmetSlot;
    [SerializeField] private EquipmentSlotUI armorSlot;
    [SerializeField] private EquipmentSlotUI accessorySlot;

    void Awake()
    {
        if (equipmentManager == null)
        {
            MovementController controller = FindObjectOfType<MovementController>();
            if (controller != null)
            {
                equipmentManager = controller.GetComponent<EquipmentManager>();
            }
        }
    }

    void Start()
    {
        if (equipmentManager != null)
        {
            equipmentManager.OnEquipmentChanged += HandleEquipmentChanged;
        }

        RefreshAll();
    }

    void OnDestroy()
    {
        if (equipmentManager != null)
        {
            equipmentManager.OnEquipmentChanged -= HandleEquipmentChanged;
        }
    }

    private void HandleEquipmentChanged(EquipmentSlot slot, ItemData item)
    {
        RefreshAll();
    }

    private void RefreshAll()
    {
        if (equipmentManager == null) return;

        RefreshSlot(weaponSlot, EquipmentSlot.Weapon);
        RefreshSlot(helmetSlot, EquipmentSlot.Helmet);
        RefreshSlot(armorSlot, EquipmentSlot.Armor);
        RefreshSlot(accessorySlot, EquipmentSlot.Accessory);
    }

    private void RefreshSlot(EquipmentSlotUI slotUI, EquipmentSlot slot)
    {
        if (slotUI == null) return;

        slotUI.Refresh(equipmentManager.GetEquippedItem(slot));
    }
}
