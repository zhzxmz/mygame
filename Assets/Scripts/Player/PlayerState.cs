using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerState : CharacterState
{
    
    public double PlayerHealth=10;
    public PlayerSoul soul;
    public void TakeDamage(int Damage)
    {
        PlayerHealth-=Damage;
        soul.PlayerIsSoul();
    }
}
