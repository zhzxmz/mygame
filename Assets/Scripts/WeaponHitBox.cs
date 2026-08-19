using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WeaponHitBox : MonoBehaviour
{
    public Attack attack;
    void Awake()
    {
        if (attack == null)
        {
            attack = GetComponent<Attack>();
        }
    }
    private void OnTriggerEnter(Collider other)
    {
        if (attack == null)
        {
            Debug.LogWarning("HitBox: 缺少 Attack 组件，无法造成伤害");
            return;
        }

        Health health = other.GetComponent<Health>();
        if (health != null)
        {
            attack.DoAttack(health);
        }
    }
}
