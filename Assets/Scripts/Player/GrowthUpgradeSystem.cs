using UnityEngine;

/// <summary>
/// 成长资源强化系统。
/// 只负责强化逻辑，不负责 UI。
/// </summary>
public class GrowthUpgradeSystem : MonoBehaviour
{
    private PlayerResources resources;
    private CharacterState stats;
    private Health health;

    void Awake()
    {
        resources = GetComponent<PlayerResources>();
        stats = GetComponent<CharacterState>();
        health = GetComponent<Health>();
    }

    public bool UpgradeAttack()
    {
        if (resources == null || stats == null) return false;
        if (resources.GetGrowthResource() < 1) return false;

        resources.RemoveGrowthResource(1);
        stats.attack += 5f;
        return true;
    }

    public bool UpgradeDefense()
    {
        if (resources == null || stats == null) return false;
        if (resources.GetGrowthResource() < 1) return false;

        resources.RemoveGrowthResource(1);
        stats.defense += 3f;
        return true;
    }

    public bool UpgradeMaxHP()
    {
        if (resources == null || health == null || health.Pool == null) return false;
        if (resources.GetGrowthResource() < 1) return false;

        resources.RemoveGrowthResource(1);

        float newMax = health.maxHP + 20f;
        health.Pool.SetMax(newMax);
        health.Pool.SetCurrent(health.currentHP + 20f);
        return true;
    }
}
