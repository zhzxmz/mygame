using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Diagnostics;

public class InventorySlot : MonoBehaviour
{
    public Image icon;
    public TextMeshProUGUI countText;

    private ItemData itemData;

    public bool IsEmpty()
    {
        return itemData == null;
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

    void Refresh()
    {
       

        if(itemData == null)
        {
            

            if(icon != null)
                icon.gameObject.SetActive(false);

            if(countText != null)
                countText.text = "";

            return;
        }

      

        // 显示图片
        if(icon != null)
        {
            icon.gameObject.SetActive(true);

            icon.sprite = itemData.icon;
            UnityEngine.Debug.Log("替换图片了");
        }

        // 显示名字（以后这里可以改成数量）
        if(countText != null)
        {
            countText.text = itemData.itemName;
        }

      
    }
}