using System.Collections.Generic;
using UnityEngine;

public class WeaponHitBox : MonoBehaviour
{
    public Attack attack;

    [Tooltip("同一个敌人在该时间内只受到一次挥剑命中")]
    [SerializeField] private float hitCooldown = 0.15f;

    private readonly Dictionary<Health, float> lastHitTimes = new Dictionary<Health, float>();

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
        if (health == null) return;

        if (!CanHit(health)) return;

        lastHitTimes[health] = Time.time;
        attack.DoAttack(health);
    }

    private bool CanHit(Health health)
    {
        if (hitCooldown <= 0f) return true;

        if (lastHitTimes.TryGetValue(health, out float lastTime))
        {
            return Time.time - lastTime >= hitCooldown;
        }

        return true;
    }
}
