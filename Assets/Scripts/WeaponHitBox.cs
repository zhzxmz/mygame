using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HitBox : MonoBehaviour
{
    public Attack attack;
    void Awake()
    {
        attack=GetComponent<Attack>();
    }
    private void OnTriggerEnter(Collider other)
    {
        Health health=other.GetComponent<Health>();
        if (health != null)
        {
            attack.DoAttack(health);
        }
    }
}
