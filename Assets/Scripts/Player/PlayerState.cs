using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerState : CharacterState
{
    
    public double PlayerHealth=10;
    public PlayerSoul soul;

    void Awake()
    {
        if (soul == null)
        {
            soul = GetComponent<PlayerSoul>();
        }
    }

    public void TakeDamage(int Damage)
    {
        PlayerHealth-=Damage;
        if (soul != null)
        {
            soul.PlayerIsSoul();
        }
    }
}
