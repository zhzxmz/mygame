using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 背包装备操作入口。
/// 监听 InventorySlot 点击，保存选中物品，点击“装备”按钮后调用 EquipmentController。
/// </summary>
public class InventoryEquipUI : MonoBehaviour
{
    public Button equipButton;
    public EquipmentController equipmentController;
    public InventoryManager inventoryManager;

    private bool warnedNotInteractable;

    void Awake()
    {
        if (equipmentController == null)
        {
            MovementController controller = FindObjectOfType<MovementController>();
            if (controller != null)
            {
                equipmentController = controller.GetComponent<EquipmentController>();
            }
        }

        if (inventoryManager == null)
        {
            inventoryManager = FindObjectOfType<InventoryManager>();
        }
    }

    void Start()
    {
        if (equipButton != null)
        {
            equipButton.onClick.RemoveListener(OnEquipClicked);
            equipButton.onClick.AddListener(OnEquipClicked);
        }
        else
        {
            Debug.LogWarning("[EquipmentDebug] equipButton is NULL");
        }

        if (inventoryManager != null)
        {
            foreach (InventorySlot slot in inventoryManager.slots)
            {
                if (slot != null)
                {
                    slot.OnSlotClicked += OnSlotClicked;
                }
            }
        }
    }

    void OnDestroy()
    {
        if (equipButton != null)
        {
            equipButton.onClick.RemoveListener(OnEquipClicked);
        }

        if (inventoryManager != null)
        {
            foreach (InventorySlot slot in inventoryManager.slots)
            {
                if (slot != null)
                {
                    slot.OnSlotClicked -= OnSlotClicked;
                }
            }
        }
    }

    void Update()
    {
        if (equipButton == null) return;

        if (!equipButton.interactable && !warnedNotInteractable)
        {
            Debug.Log("[EquipmentDebug] Equip button is not interactable");
            warnedNotInteractable = true;
        }
        else if (equipButton.interactable)
        {
            warnedNotInteractable = false;
        }

        ItemStack selected = InventorySelection.SelectedStack;
        bool canEquip = selected != null &&
                        selected.Item != null &&
                        selected.Item.itemType == ItemType.Equipment &&
                        selected.Item.equipmentSlot == EquipmentSlot.Weapon;

        equipButton.interactable = canEquip;
    }

    private void OnSlotClicked(InventorySlot slot)
    {
        if (slot == null) return;

        Debug.Log($"[EquipmentDebug] Selected: {slot.Stack?.Item?.itemName}");
        InventorySelection.Select(slot.Stack);
    }

    private void OnEquipClicked()
    {
        Debug.Log("[EquipmentDebug] Equip button clicked");

        ItemStack selected = InventorySelection.SelectedStack;
        Debug.Log($"[EquipmentDebug] Equip button clicked, selected={(selected != null && selected.Item != null ? selected.Item.itemName : "NULL")}");

        if (selected == null || selected.Item == null) return;

        if (equipmentController == null)
        {
            Debug.LogWarning("InventoryEquipUI: 未找到 EquipmentController，无法装备");
            return;
        }

        bool result = equipmentController.TryEquipFromInventory(selected.Item);
        Debug.Log($"[EquipmentDebug] TryEquipFromInventory result: {result}");
        if (result)
        {
            InventorySelection.Clear();
        }
    }
}
