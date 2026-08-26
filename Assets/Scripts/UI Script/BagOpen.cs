using UnityEngine;

public class BagOpen : MonoBehaviour
{
    public GameObject inventoryPanel;

    private UIWindow window;

    void Start()
    {
        if (inventoryPanel != null)
        {
            window = inventoryPanel.GetComponent<UIWindow>();
            if (window == null)
            {
                window = inventoryPanel.AddComponent<UIWindow>();
            }

            window.target = inventoryPanel;
            window.Close();
        }
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.B) && window != null)
        {
            window.Toggle();
        }
    }
}
