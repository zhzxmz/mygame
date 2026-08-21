using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 类似 Minecraft 聊天框的系统消息区域。
/// 消息按顺序显示，并在一定时间后消失。
/// 第一阶段只负责 UI 和消息显示。
/// </summary>
public class GameMessageUI : MonoBehaviour
{
    [Header("容器")]
    public RectTransform messageContainer;

    [Header("消息参数")]
    public float messageLifetime = 3f;
    public int maxMessages = 5;

    private readonly List<GameObject> activeMessages = new List<GameObject>();

    void Awake()
    {
        if (messageContainer == null)
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                canvas = FindScreenCanvas();
            }

            if (canvas != null)
            {
                CreateMessageContainer(canvas);
            }
            else
            {
                Debug.LogWarning("GameMessageUI: 未找到 Canvas，无法显示消息");
            }
        }
    }

    /// <summary>显示一条系统消息。</summary>
    public void ShowMessage(string message)
    {
        if (messageContainer == null || string.IsNullOrEmpty(message)) return;

        GameObject msg = new GameObject("Message", typeof(RectTransform), typeof(TextMeshProUGUI));
        msg.transform.SetParent(messageContainer, false);

        TextMeshProUGUI tmp = msg.GetComponent<TextMeshProUGUI>();
        tmp.text = message;
        tmp.fontSize = 20;
        tmp.color = Color.white;
        tmp.enableWordWrapping = true;

        activeMessages.Add(msg);

        if (activeMessages.Count > maxMessages)
        {
            GameObject oldest = activeMessages[0];
            activeMessages.RemoveAt(0);
            if (oldest != null)
            {
                Destroy(oldest);
            }
        }

        StartCoroutine(RemoveMessageAfterDelay(msg));
    }

    private IEnumerator RemoveMessageAfterDelay(GameObject msg)
    {
        yield return new WaitForSeconds(messageLifetime);

        activeMessages.Remove(msg);
        if (msg != null)
        {
            Destroy(msg);
        }
    }

    private void CreateMessageContainer(Canvas canvas)
    {
        GameObject go = new GameObject("GameMessageContainer", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        go.transform.SetParent(canvas.transform, false);

        messageContainer = go.GetComponent<RectTransform>();
        messageContainer.anchorMin = new Vector2(0.5f, 1f);
        messageContainer.anchorMax = new Vector2(0.5f, 1f);
        messageContainer.pivot = new Vector2(0.5f, 1f);
        messageContainer.anchoredPosition = new Vector2(0f, -20f);
        messageContainer.sizeDelta = new Vector2(400f, 0f);

        VerticalLayoutGroup layout = go.GetComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        layout.spacing = 4f;

        ContentSizeFitter fitter = go.GetComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
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
