using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;


public class PlayerInputController : MonoBehaviour
{
    
        
    
    
    public CameraController cameraController;
    public WeaponController weaponController;
    float mouseSensitivity=800f;
    
    void Update()
    {
        // UI 打开时，只有鼠标指针悬停在 UI 上才停止游戏鼠标输入；
        // 点击非 UI 区域时仍可控制武器/视角。
        bool pointerOverUI = MouseLock.IsUIBlocking &&
                             EventSystem.current != null &&
                             EventSystem.current.IsPointerOverGameObject();

        if (pointerOverUI) return;

        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity * Time.deltaTime;
    float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity * Time.deltaTime;


        if (!Input.GetMouseButton(0))
        {
            if (cameraController != null)
            {
                cameraController.RotateCamera(mouseX, mouseY);
            }
        }

        else
        {
            if (weaponController != null)
            {
                weaponController.RotateWeapon(mouseX, mouseY);
            }
        }
            
    }
}
