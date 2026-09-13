using UnityEngine;

/// <summary>
/// 角色数据容器：只保存数值，不处理战斗逻辑。
/// currentHP / maxHP 是实际战斗血量的唯一存放位置，Health 只负责读写这里的数据。
/// </summary>
public class CharacterState : MonoBehaviour
{
    public float currentHP = 100;
    public float maxHP = 100;

    public float attack = 10;
    public float defense = 5;
}
