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
        hpText.text = $"HP: {stats.currentHP}/{stats.maxHP}";
        attackText.text = $"ATK: {stats.attack}";
        defenseText.text = $"DEF: {stats.defense}";
    }
}
