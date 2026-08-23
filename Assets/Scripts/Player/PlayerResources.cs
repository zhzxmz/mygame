using UnityEngine;

/// <summary>
/// 玩家特殊资源系统。
/// 只负责保存和修改资源数量，不负责技能、配方、合成、属性强化、Inventory 或 UI。
/// </summary>
public class PlayerResources : MonoBehaviour
{
    [Header("特殊资源")]
    [SerializeField] private int soulFragments;
    [SerializeField] private int growthResource;

    public void AddSoulFragments(int amount)
    {
        if (amount <= 0) return;
        soulFragments += amount;
    }

    public void AddGrowthResource(int amount)
    {
        if (amount <= 0) return;
        growthResource += amount;
    }

    public void RemoveSoulFragments(int amount)
    {
        if (amount <= 0) return;
        soulFragments = Mathf.Max(0, soulFragments - amount);
    }

    public void RemoveGrowthResource(int amount)
    {
        if (amount <= 0) return;
        growthResource = Mathf.Max(0, growthResource - amount);
    }

    public int GetSoulFragments()
    {
        return soulFragments;
    }

    public int GetGrowthResource()
    {
        return growthResource;
    }
}
