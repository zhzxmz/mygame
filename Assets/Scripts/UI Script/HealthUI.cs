using UnityEngine;
using UnityEngine.UI;

public class HealthUI : MonoBehaviour
{
    public Health targetHP;
    public Slider slider;

    void Start()
    {
        if (slider == null || targetHP == null)
        {
            Debug.LogWarning("HealthUI: slider 或 targetHP 未赋值，血条无法初始化");
            return;
        }

        slider.maxValue = targetHP.maxHP;
        slider.value = targetHP.currentHP;

        targetHP.OnHealthChanged += OnTargetHealthChanged;
    }

    void OnDestroy()
    {
        if (targetHP != null)
        {
            targetHP.OnHealthChanged -= OnTargetHealthChanged;
        }
    }

    private void OnTargetHealthChanged(float current, float max)
    {
        UpdateHealth();
    }

    public void UpdateHealth()
    {
        if (slider == null || targetHP == null) return;

        slider.value = targetHP.currentHP;
    }
}
