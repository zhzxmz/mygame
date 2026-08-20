using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 最简单的敌人生成器：每隔固定时间在指定位置生成敌人。
/// 只负责生成和数量控制，不实现波次、随机区域、Boss、难度曲线等。
/// </summary>
public class EnemySpawner : MonoBehaviour
{
    [Header("生成配置")]
    public GameObject enemyPrefab;
    public Transform spawnPoint;

    [Tooltip("每隔多少秒尝试生成一个敌人")]
    public float spawnInterval = 3f;

    [Tooltip("最大同时存活敌人数")]
    public int maxAliveEnemies = 5;

    private readonly List<Enemy> aliveEnemies = new List<Enemy>();
    private float timer;

    void Update()
    {
        // 清理已被销毁的敌人（Health 死亡时会 Destroy GameObject）
        aliveEnemies.RemoveAll(enemy => enemy == null);

        timer += Time.deltaTime;
        if (timer < spawnInterval) return;

        timer = 0f;
        TrySpawn();
    }

    private void TrySpawn()
    {
        if (enemyPrefab == null)
        {
            Debug.LogWarning("EnemySpawner: enemyPrefab 未赋值");
            return;
        }

        if (spawnPoint == null)
        {
            Debug.LogWarning("EnemySpawner: spawnPoint 未赋值");
            return;
        }

        if (aliveEnemies.Count >= maxAliveEnemies)
        {
            return;
        }

        GameObject go = Instantiate(enemyPrefab, spawnPoint.position, spawnPoint.rotation);

        Enemy enemy = go.GetComponent<Enemy>();
        if (enemy == null)
        {
            Debug.LogWarning("EnemySpawner: enemyPrefab 上没有 Enemy 组件，生成失败");
            Destroy(go);
            return;
        }

        aliveEnemies.Add(enemy);
    }
}
