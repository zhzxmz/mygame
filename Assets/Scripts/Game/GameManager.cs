using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>游戏状态。当前先只实现 Playing 和 GameOver。</summary>
public enum GameState
{
    Playing,
    GameOver
}

/// <summary>
/// 最基础的游戏状态管理。
/// 监听玩家 Health.OnDeath，切换到 GameOver，显示 Game Over UI 并支持重新开始。
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("玩家 Health")]
    public Health playerHealth;

    [Header("Game Over UI")]
    public GameObject gameOverPanel;
    public Button restartButton;

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

        if (gameOverPanel != null)
        {
            gameOverPanel.SetActive(false);
        }

        if (restartButton != null && restartButton.onClick.GetPersistentEventCount() == 0)
        {
            restartButton.onClick.AddListener(RestartGame);
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

        // 解锁鼠标，确保 Game Over UI 可以点击
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (gameOverPanel != null)
        {
            gameOverPanel.SetActive(true);
        }

        Debug.Log("Game Over");
    }

    public void RestartGame()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void SetState(GameState newState)
    {
        if (CurrentState == newState) return;

        CurrentState = newState;
        Debug.Log($"GameManager: 状态切换为 {CurrentState}");
    }
}
