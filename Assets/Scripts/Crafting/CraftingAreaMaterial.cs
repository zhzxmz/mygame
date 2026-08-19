using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 合成区域内已放置材料的拖拽组件。
/// 拖出合成区域时通知 CraftingArea 返还 InventoryManager；拖到区域内部时只移动位置。
/// </summary>
public class CraftingAreaMaterial : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    private CraftingArea owner;
    private ItemStack stack;
    private GameObject dragVisual;

    public void Setup(CraftingArea owner, ItemStack stack)
    {
        this.owner = owner;
        this.stack = stack;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (stack == null || stack.Item == null) return;

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
        DestroyDragVisual();

        if (owner == null) return;

        if (owner.IsPointerInsideArea(eventData.position, eventData.pressEventCamera))
        {
            owner.MoveMaterial(this, eventData.position, eventData.pressEventCamera);
        }
        else
        {
            owner.RemoveMaterialAndReturn(this);
        }
    }

    private void CreateDragVisual(Vector2 position)
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;

        dragVisual = new GameObject("CraftingAreaDragVisual");
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
}
