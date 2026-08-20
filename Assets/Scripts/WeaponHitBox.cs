using System.Collections.Generic;
using UnityEngine;

public class WeaponHitBox : MonoBehaviour
{
    public Attack attack;

    private readonly HashSet<Health> alreadyHit = new HashSet<Health>();

    void Awake()
    {
        if (attack == null)
        {
            attack = GetComponent<Attack>();
        }
    }

    void Update()
    {
        if (MouseLock.IsUIBlocking) return;

        // 每次按下鼠标左键视为一次新的攻击/挥动开始，清空本次命中记录。
        if (Input.GetMouseButtonDown(0))
        {
            alreadyHit.Clear();
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
        if (health != null && alreadyHit.Add(health))
        {
            attack.DoAttack(health);
        }
    }
}
