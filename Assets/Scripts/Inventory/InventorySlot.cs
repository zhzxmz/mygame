using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public class InventorySlot : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public Image icon;
    public TextMeshProUGUI countText;

    private ItemStack stack;
    private GameObject dragVisual;

    public ItemStack Stack => stack;

    public bool IsEmpty()
    {
        return stack == null || stack.Item == null || stack.Count <= 0;
    }

    void Awake()
    {
        AssignMissingReferences();
    }

    void OnValidate()
    {
        AssignMissingReferences();
    }

    public void SetStack(ItemStack stack)
    {
        this.stack = stack;

        Refresh();
    }

    public void Clear()
    {
        stack = null;

        Refresh();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (IsEmpty()) return;

        Debug.Log($"[InventorySlot] OnBeginDrag: {gameObject.name}, stack={stack.Item?.itemName} x{stack.Count}");
        eventData.pointerDrag = gameObject;
        CreateDragVisual(eventData.position);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (dragVisual != null)
        {
            dragVisual.transform.position = eventData.position;
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        Debug.Log($"[InventorySlot] OnEndDrag: {gameObject.name}");
        DestroyDragVisual();
    }

    private void CreateDragVisual(Vector2 position)
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;

        dragVisual = new GameObject("InventoryDragVisual");
        dragVisual.transform.SetParent(canvas.transform, false);
        dragVisual.transform.SetAsLastSibling();

        Image image = dragVisual.AddComponent<Image>();
        image.sprite = stack.Item.icon;
        image.raycastTarget = false;
        image.preserveAspect = true;

        CanvasGroup group = dragVisual.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;

        RectTransform rect = dragVisual.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(50f, 50f);

        dragVisual.transform.position = position;
    }

    private void DestroyDragVisual()
    {
        if (dragVisual != null)
        {
            Destroy(dragVisual);
            dragVisual = null;
        }
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

    void Refresh()
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

        // 显示物品图标
        if (icon != null)
        {
            icon.gameObject.SetActive(true);
            icon.sprite = stack.Item.icon;
        }

        // 显示数量；如果没有数量文本组件则跳过
        if (countText != null)
        {
            countText.text = stack.Count.ToString();
        }
    }
}
