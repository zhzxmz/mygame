using UnityEngine;

/// <summary>
/// 合成逻辑层。
/// 材料来源是 CraftingArea 中已放置的 ItemStack，不再直接检查/扣除 InventoryManager 中的材料。
/// 合成结果仍添加到 InventoryManager。
/// </summary>
public class CraftingManager : MonoBehaviour
{
    [Tooltip("如果未赋值，会在 Awake 中尝试查找场景中的 InventoryManager")]
    public InventoryManager inventory;

    [Tooltip("合成材料来源区域；如果未赋值，会在 Awake 中尝试查找")]
    public CraftingArea craftingArea;

    void Awake()
    {
        if (inventory == null)
        {
            inventory = FindObjectOfType<InventoryManager>();

            if (inventory == null)
            {
                Debug.LogWarning("CraftingManager: 未找到 InventoryManager，请手动赋值");
            }
        }

        if (craftingArea == null)
        {
            craftingArea = FindObjectOfType<CraftingArea>();

            if (craftingArea == null)
            {
                Debug.LogWarning("CraftingManager: 未找到 CraftingArea，请手动赋值");
            }
        }
    }

    /// <summary>检查 CraftingArea 中当前放置的材料是否满足配方需求。</summary>
    public bool CanCraft(CraftingRecipe recipe)
    {
        if (!IsRecipeValid(recipe)) return false;
        if (craftingArea == null)
        {
            WarnNoCraftingArea();
            return false;
        }

        bool result = craftingArea.HasMaterials(recipe);
        Debug.Log($"CraftingManager: CanCraft = {result}");
        return result;
    }

    /// <summary>
    /// 执行合成：
    /// 1. 检查 CraftingArea 材料是否足够。
    /// 2. 预检查背包能否放入结果。
    /// 3. 先添加结果到背包。
    /// 4. 从 CraftingArea 消耗配方所需材料。
    /// 如果消耗失败，会回滚已添加的结果。
    /// </summary>
    public bool Craft(CraftingRecipe recipe)
    {
        if (!CanCraft(recipe)) return false;

        if (inventory == null)
        {
            WarnNoInventory();
            return false;
        }

        if (!inventory.CanAddItem(recipe.result, recipe.resultCount))
        {
            Debug.LogWarning("CraftingManager: 背包空间不足，无法加入合成结果");
            return false;
        }

        int added = inventory.AddItem(recipe.result, recipe.resultCount);
        if (added < recipe.resultCount)
        {
            if (added > 0)
            {
                inventory.RemoveItem(recipe.result, added);
            }

            return false;
        }

        if (!craftingArea.TryConsumeMaterials(recipe))
        {
            inventory.RemoveItem(recipe.result, recipe.resultCount);
            return false;
        }

        return true;
    }

    private bool IsRecipeValid(CraftingRecipe recipe)
    {
        if (recipe == null)
        {
            Debug.LogWarning("CraftingManager: recipe 为空");
            return false;
        }

        if (recipe.result == null)
        {
            Debug.LogWarning("CraftingManager: recipe.result 为空");
            return false;
        }

        if (recipe.resultCount < 1)
        {
            Debug.LogWarning("CraftingManager: recipe.resultCount 小于 1");
            return false;
        }

        if (recipe.materials == null || recipe.materials.Length == 0)
        {
            Debug.LogWarning("CraftingManager: recipe.materials 为空");
            return false;
        }

        foreach (CraftingIngredient ingredient in recipe.materials)
        {
            if (ingredient == null || ingredient.item == null || ingredient.count < 1)
            {
                Debug.LogWarning("CraftingManager: 配方材料配置无效");
                return false;
            }
        }

        return true;
    }

    private void WarnNoInventory()
    {
        Debug.LogWarning("CraftingManager: 未找到 InventoryManager，无法合成");
    }

    private void WarnNoCraftingArea()
    {
        Debug.LogWarning("CraftingManager: 未找到 CraftingArea，无法检查合成材料");
    }
}
