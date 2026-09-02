using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// 单个装备槽 UI。
/// 只负责显示当前装备，并在点击时通过 EquipmentController 卸下装备。
/// </summary>
public class EquipmentSlotUI : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private EquipmentSlot slotType;

    [SerializeField] private Image iconImage;

    public EquipmentController equipmentController;

    public EquipmentSlot SlotType => slotType;

    void Awake()
    {
        if (iconImage == null)
        {
            iconImage = GetComponent<Image>();
        }

        if (equipmentController == null)
        {
            MovementController controller = FindObjectOfType<MovementController>();
            if (controller != null)
            {
                equipmentController = controller.GetComponent<EquipmentController>();
            }
        }
    }

    public void Refresh(ItemData item)
    {
        if (iconImage == null) return;

        if (item != null && item.icon != null)
        {
            iconImage.sprite = item.icon;
            iconImage.enabled = true;
        }
        else
        {
            iconImage.sprite = null;
            iconImage.enabled = false;
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (equipmentController == null) return;

        equipmentController.TryUnequip(slotType);
    }
}
