using System;
using UnityEngine;

public class Health : MonoBehaviour
{
    public int maxHP;
    public int currentHP;
    public event Action<int, int> OnHealthChanged;
    public event Action OnDeath;

    private bool isDead;

    void Start()
    {
        currentHP = maxHP;
    }

    public void TakeDamage(int Damage)
    {
        if (isDead) return;

        currentHP -= Damage;
        if (currentHP < 0) currentHP = 0;
        OnHealthChanged?.Invoke(currentHP, maxHP);
        if (currentHP <= 0) Die();
    }

    void Die()
    {
        if (isDead) return;

        isDead = true;
        Debug.Log("DIE");
        OnDeath?.Invoke();
        Destroy(gameObject);
    }
}
