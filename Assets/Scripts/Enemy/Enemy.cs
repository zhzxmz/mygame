using System.Collections;
using UnityEngine;

/// <summary>
/// 敌人基础身份与生命周期。
/// 负责监听 Health 的受伤与死亡事件，并处理最基础的受击闪烁表现。
/// </summary>
public class Enemy : MonoBehaviour
{
    [Header("受击反馈")]
    public Color hitFlashColor = new Color(1f, 0.3f, 0.3f, 1f);
    public float hitFlashDuration = 0.1f;

    private Health health;
    private bool isDead;
    private Renderer[] renderers;
    private MaterialPropertyBlock flashBlock;
    private Coroutine flashCoroutine;
    private float lastHealth;

    void Awake()
    {
        health = GetComponent<Health>();

        if (health == null)
        {
            Debug.LogWarning($"Enemy: {name} 缺少 Health 组件，无法监听死亡");
        }
        else
        {
            lastHealth = health.currentHP;
        }

        renderers = GetComponentsInChildren<Renderer>(true);
    }

    void OnEnable()
    {
        if (health != null)
        {
            health.OnHealthChanged += HandleHealthChanged;
            health.OnDeath += HandleDeath;
        }
    }

    void OnDisable()
    {
        if (flashCoroutine != null)
        {
            StopCoroutine(flashCoroutine);
            flashCoroutine = null;
        }

        SetFlashColor(false);

        if (health != null)
        {
            health.OnHealthChanged -= HandleHealthChanged;
            health.OnDeath -= HandleDeath;
        }
    }

    private void HandleHealthChanged(float current, float max)
    {
        if (current < lastHealth)
        {
            TriggerHitFlash();
        }

        lastHealth = current;
    }

    private void HandleDeath()
    {
        if (isDead) return;

        isDead = true;
        Debug.Log($"Enemy 死亡: {name}");

        // 掉落由 EnemyDrop 自行监听 Health.OnDeath 处理，这里不重复实现。
    }

    private void TriggerHitFlash()
    {
        if (renderers == null || renderers.Length == 0) return;

        if (flashCoroutine != null)
        {
            StopCoroutine(flashCoroutine);
        }

        flashCoroutine = StartCoroutine(FlashRoutine());
    }

    private IEnumerator FlashRoutine()
    {
        SetFlashColor(true);
        yield return new WaitForSeconds(hitFlashDuration);
        SetFlashColor(false);
        flashCoroutine = null;
    }

    private void SetFlashColor(bool flash)
    {
        if (renderers == null) return;

        if (flashBlock == null)
        {
            flashBlock = new MaterialPropertyBlock();
        }

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null) continue;

            if (flash)
            {
                renderer.GetPropertyBlock(flashBlock);
                flashBlock.SetColor("_Color", hitFlashColor);
                renderer.SetPropertyBlock(flashBlock);
            }
            else
            {
                renderer.SetPropertyBlock(null);
            }
        }
    }
}
