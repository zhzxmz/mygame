using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 自由放置型合成区域。
/// 玩家从 InventorySlot 拖入时，如果松手位置落在本 RectTransform 内，
/// 会从 InventoryManager 扣除对应 ItemStack，并在该位置生成材料显示对象。
/// 已放入的材料可以再次拖出，拖出后返还 InventoryManager。
/// </summary>
public class CraftingArea : MonoBehaviour, IDropHandler
{
    [Header("背包")]
    public InventoryManager inventory;

    [Header("可用配方")]
    public CraftingRecipe[] recipes;

    [Header("结果 UI")]
    public CraftingUI craftingUI;

    private RectTransform areaRect;
    private readonly List<PlacedMaterial> placedMaterials = new List<PlacedMaterial>();

    private class PlacedMaterial
    {
        public ItemStack Stack;
        public GameObject DisplayObject;
        public TextMeshProUGUI CountText;
    }

    public IReadOnlyList<ItemStack> PlacedStacks => GetPlacedMaterials();

    void Awake()
    {
        areaRect = GetComponent<RectTransform>();

        if (inventory == null)
        {
            inventory = FindObjectOfType<InventoryManager>();
            if (inventory == null)
            {
                Debug.LogWarning("CraftingArea: 未找到 InventoryManager，请手动赋值");
            }
        }
    }

    void Start()
    {
        Refresh();
    }

    public void OnDrop(PointerEventData eventData)
    {
        if (eventData.pointerDrag == null) return;

        InventorySlot source = eventData.pointerDrag.GetComponent<InventorySlot>();
        if (source == null || source.IsEmpty()) return;

        if (areaRect == null || inventory == null) return;

        if (!RectTransformUtility.RectangleContainsScreenPoint(areaRect, eventData.position, eventData.pressEventCamera))
        {
            return;
        }

        Vector2 localPoint;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(areaRect, eventData.position, eventData.pressEventCamera, out localPoint))
        {
            return;
        }

        // 在 RemoveItem 之前保存数据，因为 RemoveItem 会同步刷新 InventorySlot，可能清空 source.Stack
        ItemData item = source.Stack.Item;
        int count = source.Stack.Count;

        Debug.Log($"CraftingArea: 放入 {item.itemName} x{count}");

        // 从背包扣除该 ItemStack
        int removed = inventory.RemoveItem(item, count);
        if (removed < count)
        {
            Debug.LogWarning("CraftingArea: 扣除背包材料失败，无法放入合成区域");
            return;
        }

        ItemStack copy = new ItemStack(item, removed);
        AddPlacedMaterial(copy, localPoint);
    }

    public List<ItemStack> GetPlacedMaterials()
    {
        List<ItemStack> result = new List<ItemStack>();
        for (int i = 0; i < placedMaterials.Count; i++)
        {
            if (placedMaterials[i] != null && placedMaterials[i].Stack != null)
            {
                result.Add(placedMaterials[i].Stack);
            }
        }

        return result;
    }

    public bool IsPointerInsideArea(Vector2 screenPos, Camera eventCamera)
    {
        return areaRect != null &&
               RectTransformUtility.RectangleContainsScreenPoint(areaRect, screenPos, eventCamera);
    }

    public void MoveMaterial(CraftingAreaMaterial material, Vector2 screenPos, Camera eventCamera)
    {
        if (material == null || areaRect == null) return;

        Vector2 localPoint;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(areaRect, screenPos, eventCamera, out localPoint))
        {
            return;
        }

        material.transform.localPosition = localPoint;
    }

    public void RemoveMaterialAndReturn(CraftingAreaMaterial material)
    {
        if (material == null) return;

        PlacedMaterial entry = null;
        for (int i = 0; i < placedMaterials.Count; i++)
        {
            if (placedMaterials[i] != null && placedMaterials[i].DisplayObject == material.gameObject)
            {
                entry = placedMaterials[i];
                placedMaterials.RemoveAt(i);
                break;
            }
        }

        if (entry == null) return;

        ItemStack stack = entry.Stack;
        if (stack != null && stack.Item != null && stack.Count > 0)
        {
            if (inventory != null)
            {
                int added = inventory.AddItem(stack.Item, stack.Count);
                if (added < stack.Count)
                {
                    Debug.LogWarning($"CraftingArea: 返还背包不完整，物品 {stack.Item.name} 应返还 {stack.Count}，实际返还 {added}");
                }
            }
        }

        if (entry.DisplayObject != null)
        {
            Destroy(entry.DisplayObject);
        }

        Refresh();
    }

    /// <summary>检查 CraftingArea 当前放置的材料是否满足配方需求。</summary>
    public bool HasMaterials(CraftingRecipe recipe)
    {
        if (recipe == null || recipe.materials == null) return false;

        Dictionary<ItemData, int> available = GetAggregatedMaterials();

        foreach (CraftingIngredient ingredient in recipe.materials)
        {
            if (ingredient == null || ingredient.item == null || ingredient.count < 1) return false;

            int have;
            if (!available.TryGetValue(ingredient.item, out have) || have < ingredient.count)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 从 CraftingArea 中消耗指定材料。
    /// 只消耗配方需要的数量，超过需求的数量会继续留在 CraftingArea。
    /// 如果材料不足，不会做任何修改。
    /// </summary>
    public bool TryConsumeMaterials(CraftingRecipe recipe)
    {
        if (recipe == null || recipe.materials == null) return false;
        if (!HasMaterials(recipe)) return false;

        Dictionary<ItemData, int> remaining = new Dictionary<ItemData, int>();
        foreach (CraftingIngredient ingredient in recipe.materials)
        {
            if (ingredient == null || ingredient.item == null || ingredient.count < 1) return false;

            if (remaining.ContainsKey(ingredient.item))
            {
                remaining[ingredient.item] += ingredient.count;
            }
            else
            {
                remaining[ingredient.item] = ingredient.count;
            }
        }

        List<PlacedMaterial> toRemove = new List<PlacedMaterial>();

        for (int i = 0; i < placedMaterials.Count; i++)
        {
            PlacedMaterial entry = placedMaterials[i];
            if (entry == null || entry.Stack == null || entry.Stack.Item == null) continue;

            ItemData item = entry.Stack.Item;
            if (!remaining.ContainsKey(item) || remaining[item] <= 0) continue;

            int consume = Mathf.Min(entry.Stack.Count, remaining[item]);
            int newCount = entry.Stack.Count - consume;
            remaining[item] -= consume;

            if (newCount <= 0)
            {
                toRemove.Add(entry);
            }
            else
            {
                entry.Stack = new ItemStack(item, newCount);
                if (entry.CountText != null)
                {
                    entry.CountText.text = newCount.ToString();
                }
            }
        }

        for (int i = 0; i < toRemove.Count; i++)
        {
            PlacedMaterial entry = toRemove[i];
            if (entry == null) continue;

            placedMaterials.Remove(entry);
            if (entry.DisplayObject != null)
            {
                Destroy(entry.DisplayObject);
            }
        }

        Refresh();
        return true;
    }

    private Dictionary<ItemData, int> GetAggregatedMaterials()
    {
        Dictionary<ItemData, int> available = new Dictionary<ItemData, int>();

        for (int i = 0; i < placedMaterials.Count; i++)
        {
            ItemStack stack = placedMaterials[i] != null ? placedMaterials[i].Stack : null;
            if (stack == null || stack.Item == null || stack.Count <= 0) continue;

            ItemData item = stack.Item;
            int count = stack.Count;

            if (available.ContainsKey(item))
            {
                available[item] += count;
            }
            else
            {
                available[item] = count;
            }
        }

        return available;
    }

    private void AddPlacedMaterial(ItemStack stack, Vector2 localPoint)
    {
        GameObject display = CreateDisplayObject(stack, localPoint);

        TextMeshProUGUI countText = null;
        if (display != null)
        {
            Transform countTransform = display.transform.Find("Count");
            if (countTransform != null)
            {
                countText = countTransform.GetComponent<TextMeshProUGUI>();
            }
        }

        placedMaterials.Add(new PlacedMaterial
        {
            Stack = stack,
            DisplayObject = display,
            CountText = countText
        });

        Debug.Log($"CraftingArea: 当前材料数量 = {placedMaterials.Count}");
        Refresh();
    }

    private GameObject CreateDisplayObject(ItemStack stack, Vector2 localPoint)
    {
        GameObject root = new GameObject("PlacedMaterial", typeof(RectTransform));
        root.transform.SetParent(areaRect, false);

        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.anchoredPosition = localPoint;
        rootRect.sizeDelta = new Vector2(60f, 60f);

        // 让整个材料块可以接收拖拽事件
        Image rootImage = root.AddComponent<Image>();
        rootImage.color = new Color(1f, 1f, 1f, 0f);
        rootImage.raycastTarget = true;

        // 图标
        GameObject iconGO = new GameObject("Icon", typeof(RectTransform), typeof(Image));
        iconGO.transform.SetParent(rootRect, false);

        Image icon = iconGO.GetComponent<Image>();
        icon.sprite = stack.Item != null ? stack.Item.icon : null;
        icon.raycastTarget = false;

        RectTransform iconRect = iconGO.GetComponent<RectTransform>();
        iconRect.anchorMin = Vector2.zero;
        iconRect.anchorMax = Vector2.one;
        iconRect.offsetMin = Vector2.zero;
        iconRect.offsetMax = Vector2.zero;

        // 数量
        GameObject countGO = new GameObject("Count", typeof(RectTransform), typeof(TextMeshProUGUI));
        countGO.transform.SetParent(rootRect, false);

        TextMeshProUGUI countText = countGO.GetComponent<TextMeshProUGUI>();
        countText.text = stack.Count.ToString();
        countText.fontSize = 18;
        countText.alignment = TextAlignmentOptions.BottomRight;
        countText.color = Color.white;
        countText.raycastTarget = false;

        RectTransform countRect = countGO.GetComponent<RectTransform>();
        countRect.anchorMin = new Vector2(0.5f, 0f);
        countRect.anchorMax = new Vector2(1f, 0f);
        countRect.pivot = new Vector2(1f, 0f);
        countRect.offsetMin = new Vector2(0f, 0f);
        countRect.offsetMax = new Vector2(0f, 20f);

        // 拖出合成区域的逻辑
        CraftingAreaMaterial material = root.AddComponent<CraftingAreaMaterial>();
        material.Setup(this, stack);

        return root;
    }

    private void Refresh()
    {
        if (craftingUI == null) return;

        CraftingRecipe match = FindMatchingRecipe();
        Debug.Log($"CraftingArea: 匹配配方 = {(match != null ? match.name : "NULL")}");
        craftingUI.SetRecipe(match);
    }

    private CraftingRecipe FindMatchingRecipe()
    {
        if (recipes == null || placedMaterials.Count == 0) return null;

        Dictionary<ItemData, int> available = new Dictionary<ItemData, int>();

        for (int i = 0; i < placedMaterials.Count; i++)
        {
            ItemStack stack = placedMaterials[i] != null ? placedMaterials[i].Stack : null;
            if (stack == null || stack.Item == null || stack.Count <= 0) continue;

            ItemData item = stack.Item;
            int count = stack.Count;

            if (available.ContainsKey(item))
            {
                available[item] += count;
            }
            else
            {
                available[item] = count;
            }
        }

        if (available.Count == 0) return null;

        foreach (CraftingRecipe recipe in recipes)
        {
            if (recipe == null || !IsRecipeValid(recipe)) continue;

            // 不允许出现配方外多余材料
            bool hasExtra = false;
            foreach (KeyValuePair<ItemData, int> pair in available)
            {
                if (!HasIngredient(recipe, pair.Key))
                {
                    hasExtra = true;
                    break;
                }
            }

            if (hasExtra) continue;

            // 检查配方所需材料数量是否足够
            bool enough = true;
            foreach (CraftingIngredient ingredient in recipe.materials)
            {
                int have;
                if (!available.TryGetValue(ingredient.item, out have) || have < ingredient.count)
                {
                    enough = false;
                    break;
                }
            }

            if (enough)
            {
                return recipe;
            }
        }

        return null;
    }

    private bool IsRecipeValid(CraftingRecipe recipe)
    {
        if (recipe.result == null) return false;
        if (recipe.resultCount < 1) return false;
        if (recipe.materials == null || recipe.materials.Length == 0) return false;

        foreach (CraftingIngredient ingredient in recipe.materials)
        {
            if (ingredient == null || ingredient.item == null || ingredient.count < 1)
            {
                return false;
            }
        }

        return true;
    }

    private bool HasIngredient(CraftingRecipe recipe, ItemData item)
    {
        foreach (CraftingIngredient ingredient in recipe.materials)
        {
            if (ingredient != null && ingredient.item == item)
            {
                return true;
            }
        }

        return false;
    }
}
