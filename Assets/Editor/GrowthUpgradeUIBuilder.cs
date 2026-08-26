using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using TMPro;

/// <summary>
/// Unity Editor Builder：在当前场景 Canvas 下永久创建成长强化 UI。
/// 通过菜单 Tools > GrowthUpgrade > Create UI 执行。
/// </summary>
public static class GrowthUpgradeUIBuilder
{
    [MenuItem("Tools/GrowthUpgrade/Create UI")]
    public static void CreateUI()
    {
        Canvas canvas = Object.FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            Debug.LogWarning("GrowthUpgradeUIBuilder: 场景中没有 Canvas，无法创建 UI");
            return;
        }

        if (canvas.transform.Find("GrowthUpgradePanel") != null)
        {
            Debug.LogWarning("GrowthUpgradeUIBuilder: GrowthUpgradePanel 已存在，取消创建");
            return;
        }

        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Create Growth Upgrade UI");

        // 外部打开按钮
        Button growthButton = CreateButton(canvas.transform, "GrowthButton", "成长强化", new Vector2(0f, -160f), new Vector2(160f, 40f));

        // 强化面板
        GameObject panel = CreatePanel(canvas.transform, "GrowthUpgradePanel");

        GrowthUpgradeUI ui = panel.AddComponent<GrowthUpgradeUI>();
        Undo.RegisterCreatedObjectUndo(ui, "Add GrowthUpgradeUI");

        ui.panel = panel;
        ui.growthText = CreateText(panel.transform, "GrowthText", "Growth: 0", new Vector2(0f, 120f), new Vector2(260f, 40f));
        ui.attackButton = CreateButton(panel.transform, "AttackButton", "攻击 +5", new Vector2(0f, 60f), new Vector2(200f, 40f));
        ui.defenseButton = CreateButton(panel.transform, "DefenseButton", "防御 +3", new Vector2(0f, 10f), new Vector2(200f, 40f));
        ui.maxHPButton = CreateButton(panel.transform, "MaxHPButton", "最大生命 +20", new Vector2(0f, -40f), new Vector2(200f, 40f));
        ui.closeButton = CreateButton(panel.transform, "CloseButton", "关闭", new Vector2(0f, -100f), new Vector2(200f, 40f));
        ui.toggleButton = growthButton;

        panel.SetActive(false);

        Selection.activeGameObject = panel;
        Undo.CollapseUndoOperations(undoGroup);

        Debug.Log("GrowthUpgradeUIBuilder: 创建完成");
    }

    private static GameObject CreatePanel(Transform parent, string name)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);

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

    private static Button CreateButton(Transform parent, string name, string label, Vector2 anchoredPosition, Vector2 size)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);

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

    private static TextMeshProUGUI CreateText(Transform parent, string name, string content, Vector2 anchoredPosition, Vector2 size)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));

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

        // 注册整个层级，确保 TMP 自动生成的 SubMeshUI 子物体也能被 Undo 正确处理。
        Undo.RegisterFullObjectHierarchyUndo(go, "Create " + name);

        return tmp;
    }
}
