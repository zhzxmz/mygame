using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 使用 Slider 显示当前 XP / 升级所需 XP。
/// 自动查找玩家 PlayerProgression，不修改其他系统。
/// </summary>
public class XPBar : MonoBehaviour
{
    public PlayerProgression progression;
    public Slider slider;

    private float lastXP = -1f;
    private float lastXPToNext = -1f;

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

        if (slider == null)
        {
            slider = GetComponent<Slider>();
        }
    }

    void Update()
    {
        if (progression == null || slider == null) return;

        if (progression.currentXP == lastXP && progression.xpToNextLevel == lastXPToNext)
        {
            return;
        }

        lastXP = progression.currentXP;
        lastXPToNext = progression.xpToNextLevel;

        slider.maxValue = progression.xpToNextLevel > 0 ? progression.xpToNextLevel : 1f;
        slider.value = progression.currentXP;
    }
}
