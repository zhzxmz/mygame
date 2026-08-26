using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 在运行时自动创建 GrowthUpgradePanel 和 GrowthButton。
/// 挂到当前 Canvas 下的任意 GameObject 上即可。
/// </summary>
public class GrowthUpgradeUIBuilder : MonoBehaviour
{
    public Canvas targetCanvas;

    void Start()
    {
        if (targetCanvas == null)
        {
            targetCanvas = GetComponentInParent<Canvas>();
            if (targetCanvas == null)
            {
                targetCanvas = FindScreenCanvas();
            }
        }

        if (targetCanvas == null)
        {
            Debug.LogWarning("GrowthUpgradeUIBuilder: 未找到 Canvas，无法创建强化 UI");
            return;
        }

        Build();
    }

    private void Build()
    {
        // 外部打开按钮
        Button growthButton = CreateButton(targetCanvas.transform, "GrowthButton", "成长强化", new Vector2(0f, -160f), new Vector2(160f, 40f));

        // 强化面板
        GameObject panel = CreatePanel(targetCanvas.transform, "GrowthUpgradePanel");
        panel.SetActive(false);

        GrowthUpgradeUI ui = panel.AddComponent<GrowthUpgradeUI>();
        ui.panel = panel;
        ui.growthText = CreateText(panel.transform, "GrowthText", "Growth: 0", new Vector2(0f, 120f), new Vector2(260f, 40f));
        ui.attackButton = CreateButton(panel.transform, "AttackButton", "攻击 +5", new Vector2(0f, 60f), new Vector2(200f, 40f));
        ui.defenseButton = CreateButton(panel.transform, "DefenseButton", "防御 +3", new Vector2(0f, 10f), new Vector2(200f, 40f));
        ui.maxHPButton = CreateButton(panel.transform, "MaxHPButton", "最大生命 +20", new Vector2(0f, -40f), new Vector2(200f, 40f));
        ui.closeButton = CreateButton(panel.transform, "CloseButton", "关闭", new Vector2(0f, -100f), new Vector2(200f, 40f));
        ui.toggleButton = growthButton;
    }

    private GameObject CreatePanel(Transform parent, string name)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(300f, 300f);

        Image image = go.GetComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0.8f);

        return go;
    }

    private Button CreateButton(Transform parent, string name, string label, Vector2 anchoredPosition, Vector2 size)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        Image image = go.GetComponent<Image>();
        image.color = new Color(0.2f, 0.2f, 0.2f, 1f);

        TextMeshProUGUI text = CreateText(go.transform, "Text", label, Vector2.zero, size);
        text.alignment = TextAlignmentOptions.Center;

        return go.GetComponent<Button>();
    }

    private TextMeshProUGUI CreateText(Transform parent, string name, string content, Vector2 anchoredPosition, Vector2 size)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = content;
        tmp.fontSize = 20;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = false;

        return tmp;
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
