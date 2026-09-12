using UnityEngine;

/// <summary>
/// 第一版 Buff 数据：只保存基础属性加成。
/// </summary>
[CreateAssetMenu(menuName = "Buff/BuffData")]
public class BuffData : ScriptableObject
{
    public string buffName;

    public int attackBonus;
    public int defenseBonus;
    public int maxHPBonus;
}
