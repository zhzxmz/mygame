using UnityEngine;
using TMPro;

public class StatsPanelUI : MonoBehaviour
{
    public PlayerState stats;

    public TextMeshProUGUI hpText;
    public TextMeshProUGUI attackText;
    public TextMeshProUGUI defenseText;

    void Update()
    {
        if (stats == null || hpText == null || attackText == null || defenseText == null) return;

        hpText.text = $"HP: {stats.currentHP}/{stats.maxHP}";
        attackText.text = $"ATK: {stats.attack}";
        defenseText.text = $"DEF: {stats.defense}";
    }
}
