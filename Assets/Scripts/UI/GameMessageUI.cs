using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 动态消息 UI。
/// 挂在现有 UI Panel 上，Panel 作为消息容器和背景。
/// 每次 ShowMessage 动态创建一个 TextMeshProUGUI 消息对象，并用 VerticalLayoutGroup 自动排列。
/// </summary>
public class GameMessageUI : MonoBehaviour
{
    [Header("容器")]
    public RectTransform messageContainer;

    [Header("消息参数")]
    public float messageLifetime = 3f;
    public int maxMessages = 5;

    [Header("可选引用")]
    public GameObject messagePrefab;
    public TMP_FontAsset messageFont;

    private readonly List<GameObject> activeMessages = new List<GameObject>();

    void Awake()
    {
        if (messageContainer == null)
        {
            messageContainer = GetComponent<RectTransform>();
        }

        if (messageContainer == null)
        {
            Debug.LogWarning("GameMessageUI: 没有找到消息容器 RectTransform");
            return;
        }

        VerticalLayoutGroup layout = messageContainer.GetComponent<VerticalLayoutGroup>();
        if (layout == null)
        {
            layout = messageContainer.gameObject.AddComponent<VerticalLayoutGroup>();
        }

        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        layout.spacing = 4f;
    }

    /// <summary>显示一条系统消息。</summary>
    public void ShowMessage(string message)
    {
        if (messageContainer == null || string.IsNullOrEmpty(message)) return;

        GameObject msg = CreateMessageObject(message);
        if (msg == null) return;

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

    private GameObject CreateMessageObject(string message)
    {
        TextMeshProUGUI tmp;

        GameObject msg;
        if (messagePrefab != null)
        {
            msg = Instantiate(messagePrefab, messageContainer, false);
            tmp = msg.GetComponentInChildren<TextMeshProUGUI>();
            if (tmp == null)
            {
                Debug.LogWarning("GameMessageUI: messagePrefab 上没有 TextMeshProUGUI，无法显示消息");
                Destroy(msg);
                return null;
            }
        }
        else
        {
            msg = new GameObject("Message", typeof(RectTransform), typeof(TextMeshProUGUI));
            msg.transform.SetParent(messageContainer, false);
            tmp = msg.GetComponent<TextMeshProUGUI>();

            if (messageFont != null)
            {
                tmp.font = messageFont;
            }
        }

        tmp.text = message;
        tmp.fontSize = 20;
        tmp.color = Color.white;
        tmp.enableWordWrapping = true;

        return msg;
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
}
