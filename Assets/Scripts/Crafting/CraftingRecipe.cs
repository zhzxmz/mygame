using System;
using UnityEngine;

/// <summary>
/// 合成配方数据。
/// 只负责保存“结果”和“材料”数据，不负责检查背包、扣除物品、UI 或合成逻辑。
/// </summary>
[CreateAssetMenu(menuName = "Crafting/Crafting Recipe")]
public class CraftingRecipe : ScriptableObject
{
    [Header("结果")]
    public ItemData result;

    [Tooltip("合成产出的数量，最小为 1")]
    public int resultCount = 1;

    [Header("材料")]
    public CraftingIngredient[] materials;

    void OnValidate()
    {
        if (resultCount < 1)
        {
            resultCount = 1;
        }

        if (materials == null) return;

        for (int i = 0; i < materials.Length; i++)
        {
            if (materials[i] != null && materials[i].count < 1)
            {
                materials[i].count = 1;
            }
        }
    }
}

/// <summary>
/// 一种合成材料：物品 + 数量。
/// </summary>
[Serializable]
public class CraftingIngredient
{
    public ItemData item;
    public int count = 1;

    public CraftingIngredient()
    {
    }

    public CraftingIngredient(ItemData item, int count)
    {
        this.item = item;
        this.count = count < 1 ? 1 : count;
    }
}
