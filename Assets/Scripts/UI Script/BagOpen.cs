using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BagOpen : MonoBehaviour
{
    // Start is called before the first frame update
    public GameObject inventoryPanel;
    void Start()
    {
        if (inventoryPanel != null)
        {
            inventoryPanel.SetActive(false);
            UIInputManager.Unregister(this);
        }
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.B) && inventoryPanel != null)
        {
            inventoryPanel.SetActive(!inventoryPanel.activeSelf);

            if (inventoryPanel.activeSelf)
            {
                UIInputManager.Register(this);
            }
            else
            {
                UIInputManager.Unregister(this);
            }
        }
    }

    void OnDestroy()
    {
        UIInputManager.Unregister(this);
    }
}
