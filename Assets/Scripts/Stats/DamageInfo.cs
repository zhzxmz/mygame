using System;
using UnityEngine;

/// <summary>伤害类型。第一版只做最小分类，后续可扩展元素/属性。</summary>
public enum DamageType
{
    Physical,
    Magical,
    True
}

/// <summary>
/// 伤害信息（仅描述一次伤害）。
/// 只负责携带伤害数值与类型，不负责计算伤害、不访问 Health、不执行扣血。
/// 未来扩展点：伤害来源（source）、属性标签（tags）、伤害修正（modifiers）。
/// </summary>
[Serializable]
public class DamageInfo
{
    [SerializeField] private float amount;
    [SerializeField] private DamageType type;

    public float Amount => amount;
    public DamageType Type => type;

    public DamageInfo(float amount) : this(amount, DamageType.Physical)
    {
    }

    public DamageInfo(float amount, DamageType type)
    {
        this.amount = amount;
        this.type = type;
    }
}
