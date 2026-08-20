using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 玩家受伤时屏幕红色提示。
/// 复用玩家 Health.OnHealthChanged，只在 HP 实际下降时触发。
/// </summary>
public class PlayerHitFlash : MonoBehaviour
{
    [Header("引用")]
    public Health playerHealth;
    public Image flashImage;

    [Header("效果参数")]
    public float flashMaxAlpha = 0.4f;
    public float fadeDuration = 0.25f;

    private Coroutine flashCoroutine;
    private float lastHealth;

    void Awake()
    {
        if (playerHealth == null)
        {
            MovementController controller = FindObjectOfType<MovementController>();
            if (controller != null)
            {
                playerHealth = controller.GetComponent<Health>();
            }
        }

        if (flashImage == null)
        {
            flashImage = GetComponent<Image>();
        }

        if (flashImage == null)
        {
            CreateFlashImage();
        }

        if (flashImage != null)
        {
            Color color = flashImage.color;
            color.a = 0f;
            flashImage.color = color;
            flashImage.raycastTarget = false;
        }

        if (playerHealth != null)
        {
            lastHealth = playerHealth.currentHP;
            playerHealth.OnHealthChanged += HandleHealthChanged;
        }
        else
        {
            Debug.LogWarning("PlayerHitFlash: 未找到玩家 Health，无法显示受伤提示");
        }
    }

    void OnDestroy()
    {
        if (playerHealth != null)
        {
            playerHealth.OnHealthChanged -= HandleHealthChanged;
        }
    }

    private void HandleHealthChanged(float current, float max)
    {
        if (current < lastHealth)
        {
            TriggerFlash();
        }

        lastHealth = current;
    }

    private void TriggerFlash()
    {
        if (flashImage == null) return;

        if (flashCoroutine != null)
        {
            StopCoroutine(flashCoroutine);
        }

        flashCoroutine = StartCoroutine(FlashRoutine());
    }

    private IEnumerator FlashRoutine()
    {
        float timer = 0f;

        while (timer < fadeDuration)
        {
            timer += Time.deltaTime;
            float alpha = Mathf.Lerp(flashMaxAlpha, 0f, timer / fadeDuration);

            Color color = flashImage.color;
            color.a = alpha;
            flashImage.color = color;

            yield return null;
        }

        Color finalColor = flashImage.color;
        finalColor.a = 0f;
        flashImage.color = finalColor;

        flashCoroutine = null;
    }

    private void CreateFlashImage()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null)
        {
            canvas = FindScreenCanvas();
        }

        if (canvas == null)
        {
            Debug.LogWarning("PlayerHitFlash: 未找到 Canvas，无法创建受伤提示");
            return;
        }

        GameObject go = new GameObject("PlayerHitFlashImage", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(canvas.transform, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        flashImage = go.GetComponent<Image>();
        flashImage.color = new Color(1f, 0f, 0f, 0f);
        flashImage.raycastTarget = false;
    }

    private Canvas FindScreenCanvas()
    {
        Canvas[] canvases = FindObjectsOfType<Canvas>();
        foreach (Canvas canvas in canvases)
        {
            if (canvas != null && canvas.renderMode != RenderMode.WorldSpace)
            {
                return canvas;
            }
        }

        return canvases.Length > 0 ? canvases[0] : null;
    }
}
