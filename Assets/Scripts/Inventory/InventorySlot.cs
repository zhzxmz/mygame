using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class InventorySlot : MonoBehaviour
{
    public Image icon;
    public TextMeshProUGUI countText;

    private ItemData itemData;

    public ItemData ItemData => itemData;

    public bool IsEmpty()
    {
        return itemData == null;
    }

    void Awake()
    {
        AssignMissingReferences();
    }

    void OnValidate()
    {
        AssignMissingReferences();
    }

    public void SetItem(ItemData data)
    {
        itemData = data;

        Refresh();
    }

    public void Clear()
    {
        itemData = null;

        Refresh();
    }

    private void AssignMissingReferences()
    {
        if (icon == null)
        {
            Transform iconTransform = transform.Find("Icon");
            if (iconTransform != null)
            {
                icon = iconTransform.GetComponent<Image>();
            }
        }

        if (countText == null)
        {
            Transform countTextTransform = transform.Find("CountText");
            if (countTextTransform != null)
            {
                countText = countTextTransform.GetComponent<TextMeshProUGUI>();
            }
        }
    }

    void Refresh()
    {
        AssignMissingReferences();

        if (itemData == null)
        {
            if (icon != null)
                icon.gameObject.SetActive(false);

            if (countText != null)
                countText.text = "";

            return;
        }

        // 显示图片
        if (icon != null)
        {
            icon.gameObject.SetActive(true);

            icon.sprite = itemData.icon;
            Debug.Log("替换图片了");
        }

        // 显示名字（以后这里可以改成数量）
        if (countText != null)
        {
            countText.text = itemData.itemName;
        }
    }
}
