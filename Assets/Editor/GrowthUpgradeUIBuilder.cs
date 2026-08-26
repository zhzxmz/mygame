using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using TMPro;

/// <summary>
/// Unity Editor Builder：在当前场景 Canvas 下创建或修复成长强化 UI。
/// 通过菜单 Tools > GrowthUpgrade > Create UI 执行。
/// 已存在的 UI 不会被重复创建，只会补齐缺失部分。
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

        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Create/Repair Growth Upgrade UI");

        // 外部打开按钮
        Button growthButton = FindOrCreateButton(canvas.transform, "GrowthButton", "成长强化", new Vector2(0f, -160f), new Vector2(160f, 40f));

        // 强化面板
        GameObject panel = FindOrCreatePanel(canvas.transform, "GrowthUpgradePanel");

        GrowthUpgradeUI ui = panel.GetComponent<GrowthUpgradeUI>();
        if (ui == null)
        {
            ui = Undo.AddComponent<GrowthUpgradeUI>(panel);
        }

        ui.panel = panel;
        ui.growthText = FindOrCreateText(panel.transform, "GrowthText", "Growth: 0", new Vector2(0f, 120f), new Vector2(260f, 40f));
        ui.attackButton = FindOrCreateButton(panel.transform, "AttackButton", "攻击 +5", new Vector2(0f, 60f), new Vector2(200f, 40f));
        ui.defenseButton = FindOrCreateButton(panel.transform, "DefenseButton", "防御 +3", new Vector2(0f, 10f), new Vector2(200f, 40f));
        ui.maxHPButton = FindOrCreateButton(panel.transform, "MaxHPButton", "最大生命 +20", new Vector2(0f, -40f), new Vector2(200f, 40f));
        ui.closeButton = FindOrCreateButton(panel.transform, "CloseButton", "关闭", new Vector2(0f, -100f), new Vector2(200f, 40f));
        ui.toggleButton = growthButton;

        panel.SetActive(false);

        Selection.activeGameObject = panel;
        Undo.CollapseUndoOperations(undoGroup);

        Debug.Log("GrowthUpgradeUIBuilder: 创建/修复完成");
    }

    private static GameObject FindOrCreatePanel(Transform parent, string name)
    {
        Transform existing = parent.Find(name);
        if (existing != null)
        {
            GameObject go = existing.gameObject;
            if (go.GetComponent<Image>() == null)
            {
                Undo.AddComponent<Image>(go);
            }

            return go;
        }

        return CreatePanel(parent, name);
    }

    private static Button FindOrCreateButton(Transform parent, string name, string label, Vector2 anchoredPosition, Vector2 size)
    {
        Transform existing = parent.Find(name);
        if (existing == null)
        {
            return CreateButton(parent, name, label, anchoredPosition, size);
        }

        GameObject go = existing.gameObject;

        if (go.GetComponent<Image>() == null)
        {
            Undo.AddComponent<Image>(go);
        }

        Button button = go.GetComponent<Button>();
        if (button == null)
        {
            button = Undo.AddComponent<Button>(go);
        }

        Transform textTransform = go.transform.Find("Text");
        TextMeshProUGUI tmp;
        if (textTransform != null)
        {
            tmp = textTransform.GetComponent<TextMeshProUGUI>();
            if (tmp == null)
            {
                tmp = Undo.AddComponent<TextMeshProUGUI>(textTransform.gameObject);
            }
        }
        else
        {
            tmp = CreateText(go.transform, "Text", label, Vector2.zero, size);
        }

        Undo.RecordObject(tmp, "Configure Text");
        tmp.text = label;
        tmp.fontSize = 20;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = false;

        return button;
    }

    private static TextMeshProUGUI FindOrCreateText(Transform parent, string name, string content, Vector2 anchoredPosition, Vector2 size)
    {
        Transform existing = parent.Find(name);
        if (existing != null)
        {
            TextMeshProUGUI tmp = existing.GetComponent<TextMeshProUGUI>();
            if (tmp == null)
            {
                tmp = Undo.AddComponent<TextMeshProUGUI>(existing.gameObject);
            }

            Undo.RecordObject(tmp, "Configure Text");
            tmp.text = content;
            tmp.fontSize = 20;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;

            return tmp;
        }

        return CreateText(parent, name, content, anchoredPosition, size);
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
