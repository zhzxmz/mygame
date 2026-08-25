using UnityEngine;
using TMPro;

/// <summary>
/// 显示玩家特殊资源：Soul / Growth。
/// 只负责读取和显示，不包含资源逻辑。
/// </summary>
public class PlayerResourcesUI : MonoBehaviour
{
    public PlayerResources resources;

    public TextMeshProUGUI soulText;
    public TextMeshProUGUI growthText;

    private int lastSoul = -1;
    private int lastGrowth = -1;
    private bool warned;

    void Awake()
    {
        if (resources == null)
        {
            MovementController controller = FindObjectOfType<MovementController>();
            if (controller != null)
            {
                resources = controller.GetComponent<PlayerResources>();
            }
        }

        if (soulText == null)
        {
            soulText = GetComponent<TextMeshProUGUI>();
        }
    }

    void Update()
    {
        if (resources == null)
        {
            if (!warned)
            {
                Debug.LogWarning("PlayerResourcesUI: 未找到 PlayerResources");
                warned = true;
            }

            return;
        }

        if (soulText == null || growthText == null)
        {
            if (!warned)
            {
                Debug.LogWarning("PlayerResourcesUI: 缺少 soulText 或 growthText");
                warned = true;
            }

            return;
        }

        int soul = resources.GetSoulFragments();
        int growth = resources.GetGrowthResource();

        if (soul == lastSoul && growth == lastGrowth)
        {
            return;
        }

        lastSoul = soul;
        lastGrowth = growth;

        soulText.text = $"Soul: {soul}";
        growthText.text = $"Growth: {growth}";
    }
}
