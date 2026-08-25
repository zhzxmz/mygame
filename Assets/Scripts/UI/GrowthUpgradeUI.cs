using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 成长资源强化界面。
/// 负责显示 Growth、打开/关闭面板、绑定三个强化按钮。
/// </summary>
public class GrowthUpgradeUI : MonoBehaviour
{
    [Header("逻辑引用")]
    public GrowthUpgradeSystem upgradeSystem;
    public PlayerResources resources;

    [Header("面板")]
    public GameObject panel;

    [Header("显示")]
    public TextMeshProUGUI growthText;

    [Header("按钮")]
    public Button attackButton;
    public Button defenseButton;
    public Button maxHPButton;
    public Button closeButton;

    [Header("外部开关按钮")]
    public Button toggleButton;

    private int lastGrowth = -1;

    void Awake()
    {
        if (upgradeSystem == null || resources == null)
        {
            MovementController controller = FindObjectOfType<MovementController>();
            if (controller != null)
            {
                if (upgradeSystem == null)
                {
                    upgradeSystem = controller.GetComponent<GrowthUpgradeSystem>();
                }

                if (resources == null)
                {
                    resources = controller.GetComponent<PlayerResources>();
                }
            }
        }
    }

    void Start()
    {
        if (panel != null)
        {
            panel.SetActive(false);
        }

        BindButton(attackButton, UpgradeAttack);
        BindButton(defenseButton, UpgradeDefense);
        BindButton(maxHPButton, UpgradeMaxHP);
        BindButton(closeButton, ClosePanel);
        BindButton(toggleButton, TogglePanel);

        RefreshGrowthText();
    }

    void Update()
    {
        if (resources == null || growthText == null) return;

        int growth = resources.GetGrowthResource();
        if (growth == lastGrowth) return;

        lastGrowth = growth;
        RefreshGrowthText();
    }

    public void TogglePanel()
    {
        if (panel == null) return;

        panel.SetActive(!panel.activeSelf);

        if (panel.activeSelf)
        {
            MouseLock.IsUIBlocking = true;
            RefreshGrowthText();
        }
        else
        {
            MouseLock.IsUIBlocking = false;
        }
    }

    public void ClosePanel()
    {
        if (panel == null) return;

        panel.SetActive(false);
        MouseLock.IsUIBlocking = false;
    }

    private void UpgradeAttack()
    {
        if (upgradeSystem != null && upgradeSystem.UpgradeAttack())
        {
            RefreshGrowthText();
        }
    }

    private void UpgradeDefense()
    {
        if (upgradeSystem != null && upgradeSystem.UpgradeDefense())
        {
            RefreshGrowthText();
        }
    }

    private void UpgradeMaxHP()
    {
        if (upgradeSystem != null && upgradeSystem.UpgradeMaxHP())
        {
            RefreshGrowthText();
        }
    }

    private void RefreshGrowthText()
    {
        if (growthText == null) return;

        int growth = resources != null ? resources.GetGrowthResource() : 0;
        growthText.text = $"Growth: {growth}";
        lastGrowth = growth;
    }

    private void BindButton(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null) return;

        if (button.onClick.GetPersistentEventCount() == 0)
        {
            button.onClick.AddListener(action);
        }
    }
}
