using UnityEngine;

public class PlayerSoul : MonoBehaviour
{
    public bool PlayerSouls;

    private PlayerState state;

    void Start()
    {
        state = GetComponent<PlayerState>();
    }

    public void PlayerIsSoul()
    {
        if (state == null)
        {
            state = GetComponent<PlayerState>();
        }

        if (state == null) return;

        // 战斗生命值的唯一来源是 CharacterState.currentHP。
        // 玩家死亡时 Health.OnDeath 会调用本方法，此时 currentHP 已为 0。
        PlayerSouls = state.currentHP <= 0f;
    }
}
