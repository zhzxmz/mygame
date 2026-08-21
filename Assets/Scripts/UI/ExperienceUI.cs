using UnityEngine;
using TMPro;

/// <summary>
/// 显示玩家 Level 和 XP / XPToNextLevel。
/// 通过 Update 检测 PlayerProgression 数据变化，自动刷新 UI。
/// </summary>
public class ExperienceUI : MonoBehaviour
{
    public PlayerProgression progression;

    public TextMeshProUGUI levelText;
    public TextMeshProUGUI xpText;

    private int lastLevel = -1;
    private int lastXP = -1;
    private int lastXPToNext = -1;

    void Awake()
    {
        if (progression == null)
        {
            MovementController controller = FindObjectOfType<MovementController>();
            if (controller != null)
            {
                progression = controller.GetComponent<PlayerProgression>();
            }
        }

        if (levelText == null)
        {
            levelText = GetComponent<TextMeshProUGUI>();
        }
    }

    void Update()
    {
        if (progression == null || levelText == null || xpText == null) return;

        if (progression.level == lastLevel &&
            progression.currentXP == lastXP &&
            progression.xpToNextLevel == lastXPToNext)
        {
            return;
        }

        lastLevel = progression.level;
        lastXP = progression.currentXP;
        lastXPToNext = progression.xpToNextLevel;

        levelText.text = $"Level: {progression.level}";
        xpText.text = $"{progression.currentXP} / {progression.xpToNextLevel}";
    }
}
