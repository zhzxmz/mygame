using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 第一版合成 UI：只支持一个配方，点击按钮合成。
/// UI 不直接操作 InventoryManager，也不扣除材料；实际合成由 CraftingManager 负责。
/// </summary>
public class CraftingUI : MonoBehaviour
{
    [Header("逻辑引用")]
    public CraftingManager craftingManager;
    public CraftingRecipe recipe;

    [Header("结果显示")]
    public TextMeshProUGUI resultNameText;
    public Image resultIconImage;
    public TextMeshProUGUI resultCountText;

    [Header("材料与按钮")]
    public TextMeshProUGUI materialsText;
    public Button craftButton;

    void Awake()
    {
        if (craftingManager == null)
        {
            craftingManager = FindObjectOfType<CraftingManager>();

            if (craftingManager == null)
            {
                Debug.LogWarning("CraftingUI: 未找到 CraftingManager，请手动赋值");
            }
        }
    }

    /// <summary>由 CraftingArea 调用，切换当前显示的配方并刷新 UI。</summary>
    public void SetRecipe(CraftingRecipe newRecipe)
    {
        recipe = newRecipe;
        RefreshUI();
    }

    void Start()
    {
        if (craftButton != null)
        {
            craftButton.onClick.AddListener(OnCraftButtonClicked);
        }

        if (craftingManager != null && craftingManager.inventory != null)
        {
            craftingManager.inventory.InventoryChanged += RefreshUI;
        }
        else if (craftingManager == null)
        {
            Debug.LogWarning("CraftingUI: CraftingManager 未赋值，无法订阅背包变化");
        }
        else
        {
            Debug.LogWarning("CraftingUI: CraftingManager.inventory 未赋值，无法订阅背包变化");
        }

        RefreshUI();
    }

    void OnDestroy()
    {
        if (craftButton != null)
        {
            craftButton.onClick.RemoveListener(OnCraftButtonClicked);
        }

        if (craftingManager != null && craftingManager.inventory != null)
        {
            craftingManager.inventory.InventoryChanged -= RefreshUI;
        }
    }

    private void OnCraftButtonClicked()
    {
        if (craftingManager == null)
        {
            Debug.LogWarning("CraftingUI: CraftingManager 未赋值，无法合成");
            return;
        }

        if (recipe == null)
        {
            Debug.LogWarning("CraftingUI: recipe 未赋值，无法合成");
            return;
        }

        craftingManager.Craft(recipe);
        RefreshUI();
    }

    private void RefreshUI()
    {
        if (recipe == null)
        {
            SetResultName("无匹配配方");
            SetResultIcon(null);
            SetResultCount("");
            SetMaterialsText("");
            SetButtonInteractable(false);
            return;
        }

        bool hasResult = recipe.result != null;

        SetResultName(hasResult ? recipe.result.itemName : "无结果");
        SetResultIcon(hasResult ? recipe.result.icon : null);
        SetResultCount(hasResult ? $"x{recipe.resultCount}" : "");

        SetMaterialsText(BuildMaterialsText(recipe));
        SetButtonInteractable(craftingManager != null && craftingManager.CanCraft(recipe));
    }

    private string BuildMaterialsText(CraftingRecipe recipe)
    {
        if (recipe.materials == null || recipe.materials.Length == 0)
        {
            return "无材料";
        }

        string text = "";
        for (int i = 0; i < recipe.materials.Length; i++)
        {
            CraftingIngredient ingredient = recipe.materials[i];
            if (ingredient == null || ingredient.item == null)
            {
                text += "无效材料";
            }
            else
            {
                text += $"{ingredient.item.itemName} x{ingredient.count}";
            }

            if (i < recipe.materials.Length - 1)
            {
                text += "\n";
            }
        }

        return text;
    }

    private void SetResultName(string text)
    {
        if (resultNameText != null)
        {
            resultNameText.text = text;
        }
    }

    private void SetResultIcon(Sprite sprite)
    {
        if (resultIconImage != null)
        {
            resultIconImage.sprite = sprite;
            resultIconImage.enabled = sprite != null;
        }
    }

    private void SetResultCount(string text)
    {
        if (resultCountText != null)
        {
            resultCountText.text = text;
        }
    }

    private void SetMaterialsText(string text)
    {
        if (materialsText != null)
        {
            materialsText.text = text;
        }
    }

    private void SetButtonInteractable(bool interactable)
    {
        if (craftButton != null)
        {
            craftButton.interactable = interactable;
        }
    }
}
