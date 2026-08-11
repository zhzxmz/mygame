using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerSoul : MonoBehaviour
{public bool PlayerSouls;
PlayerState state;
    void Start()
    {
        state=GetComponent<PlayerState>();
    }
    public void PlayerIsSoul()
    {
        if (state.PlayerHealth <= 0)
        {
            PlayerSouls=true;
        }
        else
        {
        PlayerSouls=false;
        }
    }
    
}
