using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerState : CharacterState
{
    
    public double PlayerHealth=10;
    public PlayerSoul soul;

    private Health health;

    void Awake()
    {
        if (soul == null)
        {
            soul = GetComponent<PlayerSoul>();
        }

        health = GetComponent<Health>();
        if (health != null)
        {
            // 玩家死亡时不销毁 GameObject，由玩家专用逻辑处理死亡表现。
            health.destroyOnDeath = false;
            health.OnDeath += OnPlayerDeath;
        }
        else
        {
            Debug.LogWarning("PlayerState: 玩家缺少 Health 组件，无法处理玩家死亡");
        }
    }

    void OnDestroy()
    {
        if (health != null)
        {
            health.OnDeath -= OnPlayerDeath;
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

    private void OnPlayerDeath()
    {
        Debug.Log("玩家死亡");

        MovementController movement = GetComponent<MovementController>();
        if (movement != null)
        {
            movement.enabled = false;
        }

        PlayerInputController input = GetComponent<PlayerInputController>();
        if (input != null)
        {
            input.enabled = false;
        }

        MouseLock mouseLock = GetComponent<MouseLock>();
        if (mouseLock != null)
        {
            mouseLock.enabled = false;
        }
    }
}
