using UnityEngine;

/// <summary>游戏状态。当前先只实现 Playing 和 GameOver。</summary>
public enum GameState
{
    Playing,
    GameOver
}

/// <summary>
/// 最基础的游戏状态管理。
/// 监听玩家 Health.OnDeath，切换到 GameOver。
/// 不创建 UI、不重新开始、不修改玩家死亡行为。
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("玩家 Health")]
    public Health playerHealth;

    public GameState CurrentState { get; private set; } = GameState.Playing;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    void Start()
    {
        if (playerHealth == null)
        {
            MovementController controller = FindObjectOfType<MovementController>();
            if (controller != null)
            {
                playerHealth = controller.GetComponent<Health>();
            }
        }

        if (playerHealth != null)
        {
            playerHealth.OnDeath += HandlePlayerDeath;
        }
        else
        {
            Debug.LogWarning("GameManager: 未找到玩家 Health，无法监听玩家死亡");
        }
    }

    void OnDestroy()
    {
        if (playerHealth != null)
        {
            playerHealth.OnDeath -= HandlePlayerDeath;
        }

        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void HandlePlayerDeath()
    {
        SetState(GameState.GameOver);
        Debug.Log("Game Over");
    }

    public void SetState(GameState newState)
    {
        if (CurrentState == newState) return;

        CurrentState = newState;
        Debug.Log($"GameManager: 状态切换为 {CurrentState}");
    }
}
