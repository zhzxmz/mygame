using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using TMPro;

/// <summary>
/// Editor Tool：在 Inventory Panel 下创建/修复“装备”按钮，并绑定 InventoryEquipUI。
/// </summary>
public static class InventoryEquipUIBuilder
{
    [MenuItem("Tools/Equipment/Create Inventory Equip Button")]
    public static void Create()
    {
        Canvas canvas = Object.FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            Debug.LogWarning("InventoryEquipUIBuilder: 场景中没有 Canvas");
            return;
        }

        Transform panel = canvas.transform.Find("Inventory Panel");
        if (panel == null)
        {
            Debug.LogWarning("InventoryEquipUIBuilder: 没有找到 Inventory Panel");
            return;
        }

        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Create Inventory Equip Button");

        Button equipButton = FindOrCreateButton(panel, "EquipButton", "装备", new Vector2(0f, -220f), new Vector2(160f, 40f));

        InventoryEquipUI ui = panel.GetComponent<InventoryEquipUI>();
        if (ui == null)
        {
            ui = Undo.AddComponent<InventoryEquipUI>(panel.gameObject);
        }

        ui.equipButton = equipButton;

        Undo.CollapseUndoOperations(undoGroup);

        Debug.Log("InventoryEquipUIBuilder: 创建/修复完成");
    }

    private static Button FindOrCreateButton(Transform parent, string name, string label, Vector2 anchoredPosition, Vector2 size)
    {
        Transform existing = parent.Find(name);
        if (existing != null)
        {
            Button button = existing.GetComponent<Button>();
            if (button == null)
            {
                button = Undo.AddComponent<Button>(existing.gameObject);
            }

            if (existing.GetComponent<Image>() == null)
            {
                Undo.AddComponent<Image>(existing.gameObject);
            }

            Transform textTransform = existing.Find("Text");
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
                tmp = CreateText(existing, "Text", label, Vector2.zero, size);
            }

            Undo.RecordObject(tmp, "Configure Text");
            tmp.text = label;
            tmp.fontSize = 20;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;

            return button;
        }

        return CreateButton(parent, name, label, anchoredPosition, size);
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

        Undo.RegisterFullObjectHierarchyUndo(go, "Create " + name);

        return tmp;
    }
}
