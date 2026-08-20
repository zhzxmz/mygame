using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 通用 HealthBar：使用 Unity Slider 显示 Health 的当前值。
/// 通过 Inspector 引用 Health 和 Slider；未赋值时尝试从父物体/自身查找。
/// </summary>
public class HealthBar : MonoBehaviour
{
    public Health health;
    public Slider slider;

    void Awake()
    {
        if (health == null)
        {
            health = GetComponentInParent<Health>();
        }

        if (slider == null)
        {
            slider = GetComponent<Slider>();
        }
    }

    void Start()
    {
        if (health == null || slider == null)
        {
            Debug.LogWarning("HealthBar: 缺少 Health 或 Slider 引用");
            return;
        }

        slider.maxValue = health.maxHP;
        slider.value = health.currentHP;

        health.OnHealthChanged += OnHealthChanged;
        health.OnDeath += OnDeath;
    }

    void OnDestroy()
    {
        if (health != null)
        {
            health.OnHealthChanged -= OnHealthChanged;
            health.OnDeath -= OnDeath;
        }
    }

    private void OnHealthChanged(float current, float max)
    {
        if (slider == null) return;

        slider.maxValue = max;
        slider.value = current;
    }

    private void OnDeath()
    {
        if (slider != null)
        {
            slider.gameObject.SetActive(false);
        }
    }
}
