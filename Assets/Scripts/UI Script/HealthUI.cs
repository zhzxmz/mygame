using UnityEngine;
using UnityEngine.UI;

public class HealthUI : MonoBehaviour
{
    public Health targetHP;
    public Slider slider;

    void Start()
    {
        slider.maxValue = targetHP.maxHP;
        slider.value = targetHP.currentHP;
    }

    public void UpdateHealth()
    {
        slider.value = targetHP.currentHP;
    }
}