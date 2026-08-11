using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Health : MonoBehaviour{
public int maxHP;
public int currentHP;
public event Action<int,int> OnHealthChanged;
public event Action OnDeath;

    // Start is called before the first frame update
    void Start()
    {
        currentHP=maxHP;
    }

    // Update is called once per frame
    
        public void TakeDamage(int Damage)
    {
        currentHP-=Damage;
        if(currentHP<0)currentHP=0;
        OnHealthChanged?.Invoke(currentHP,maxHP);
        if(currentHP<=0)Die();
    }
    void Die()
    {
        Debug.Log("DIE");
        OnDeath?.Invoke();
        GetComponent<EnemyDrop>().Drop();
        Destroy(gameObject);
    }
    

    
    
}
