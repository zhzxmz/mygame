using UnityEngine;
using TMPro;

/// <summary>
/// 显示玩家当前生命值和最大生命值，例如 80 / 100。
/// 只监听 Health.OnHealthChanged，不修改 Health / HealthBar。
/// </summary>
public class HealthText : MonoBehaviour
{
    public Health health;
    public TextMeshProUGUI text;

    void Awake()
    {
        if (health == null)
        {
            MovementController controller = FindObjectOfType<MovementController>();
            if (controller != null)
            {
                health = controller.GetComponent<Health>();
            }
        }

        if (text == null)
        {
            text = GetComponent<TextMeshProUGUI>();
        }
    }

    void Start()
    {
        if (health == null || text == null)
        {
            Debug.LogWarning("HealthText: 缺少 Health 或 TextMeshProUGUI 引用");
            return;
        }

        health.OnHealthChanged += OnHealthChanged;
        UpdateText(health.currentHP, health.maxHP);
    }

    void OnDestroy()
    {
        if (health != null)
        {
            health.OnHealthChanged -= OnHealthChanged;
        }
    }

    private void OnHealthChanged(float current, float max)
    {
        UpdateText(current, max);
    }

    private void UpdateText(float current, float max)
    {
        if (text == null) return;

        text.text = $"{Mathf.RoundToInt(current)} / {Mathf.RoundToInt(max)}";
    }
}
