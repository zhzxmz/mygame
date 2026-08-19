using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// 合成区域的材料槽。
/// 只负责接收从 InventorySlot 拖入的 ItemStack 并显示，不扣除背包物品，不参与实际合成逻辑。
/// </summary>
public class CraftingInputSlot : MonoBehaviour, IDropHandler
{
    public Image icon;
    public TextMeshProUGUI countText;

    private ItemStack stack;

    /// <summary>槽位内容变化时触发，用于让 CraftingArea 刷新配方匹配。</summary>
    public event Action Changed;

    public ItemStack Stack => stack;

    public bool IsEmpty()
    {
        return stack == null || stack.Item == null || stack.Count <= 0;
    }

    void Awake()
    {
        AssignMissingReferences();
        EnsureRaycastTarget();
    }

    void OnValidate()
    {
        AssignMissingReferences();
    }

    public void SetStack(ItemStack stack)
    {
        this.stack = stack;
        Refresh();
        Changed?.Invoke();
    }

    public void Clear()
    {
        stack = null;
        Refresh();
        Changed?.Invoke();
    }

    public void OnDrop(PointerEventData eventData)
    {
        Debug.Log($"[CraftingInputSlot] OnDrop: {gameObject.name}, pointerDrag={eventData.pointerDrag?.name}");

        if (eventData.pointerDrag == null) return;

        InventorySlot source = eventData.pointerDrag.GetComponent<InventorySlot>();
        if (source == null || source.IsEmpty())
        {
            Debug.Log("[CraftingInputSlot] OnDrop: source 无效或为空");
            return;
        }

        Debug.Log($"[CraftingInputSlot] source.Stack: {source.Stack?.Item?.itemName} x{source.Stack?.Count}");

        // 复制一份到合成槽；当前阶段不从背包扣除物品。
        SetStack(new ItemStack(source.Stack.Item, source.Stack.Count));
    }

    private void AssignMissingReferences()
    {
        if (icon == null)
        {
            Transform iconTransform = transform.Find("Icon");
            if (iconTransform != null)
            {
                icon = iconTransform.GetComponent<Image>();
            }
        }

        if (countText == null)
        {
            Transform countTextTransform = transform.Find("CountText");
            if (countTextTransform != null)
            {
                countText = countTextTransform.GetComponent<TextMeshProUGUI>();
            }
        }
    }

    private void EnsureRaycastTarget()
    {
        Image image = GetComponent<Image>();
        if (image == null)
        {
            image = gameObject.AddComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0f);
        }

        image.raycastTarget = true;
    }

    private void Refresh()
    {
        AssignMissingReferences();

        if (IsEmpty())
        {
            if (icon != null)
                icon.gameObject.SetActive(false);

            if (countText != null)
                countText.text = "";

            return;
        }

        if (icon != null)
        {
            icon.gameObject.SetActive(true);
            icon.sprite = stack.Item.icon;
        }

        if (countText != null)
        {
            countText.text = stack.Count.ToString();
        }
    }
}
