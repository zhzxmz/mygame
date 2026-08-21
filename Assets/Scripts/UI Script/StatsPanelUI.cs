using UnityEngine;
using TMPro;

public class StatsPanelUI : MonoBehaviour
{
    [Header("数据源")]
    public CharacterState stats;
    public Health health;

    [Header("UI")]
    public TextMeshProUGUI hpText;
    public TextMeshProUGUI attackText;
    public TextMeshProUGUI defenseText;

    void Awake()
    {
        if (stats == null || health == null)
        {
            MovementController controller = FindObjectOfType<MovementController>();
            if (controller != null)
            {
                if (stats == null)
                {
                    stats = controller.GetComponent<CharacterState>();
                }

                if (health == null)
                {
                    health = controller.GetComponent<Health>();
                }
            }
        }
    }

    void Update()
    {
        if (stats == null || hpText == null || attackText == null || defenseText == null) return;

        float currentHP = health != null ? health.currentHP : stats.currentHP;
        float maxHP = health != null ? health.maxHP : stats.maxHP;

        hpText.text = $"HP: {Mathf.RoundToInt(currentHP)}/{Mathf.RoundToInt(maxHP)}";
        attackText.text = $"ATK: {Mathf.RoundToInt(stats.attack)}";
        defenseText.text = $"DEF: {Mathf.RoundToInt(stats.defense)}";
    }
}
