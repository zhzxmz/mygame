using UnityEngine;
using UnityEngine.EventSystems;

public class PlayerInputController : MonoBehaviour
{
    public CameraController cameraController;
    public WeaponController weaponController;

    float mouseSensitivity = 800f;

    void Update()
    {
        bool uiOpen = MouseLock.IsUIBlocking;

        if (uiOpen)
        {
            // UI 打开时：鼠标悬停在 UI 上则完全交给 UI。
            bool pointerOverUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (pointerOverUI) return;

            // 鼠标不在 UI 上：允许武器攻击，但不旋转视角。
            UpdateWeaponOnly();
            return;
        }

        // 无 UI：如果鼠标处于自由状态（Alt 切换），不处理游戏鼠标输入。
        if (Cursor.lockState != CursorLockMode.Locked) return;

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

    private void UpdateWeaponOnly()
    {
        if (!Input.GetMouseButton(0)) return;

        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity * Time.deltaTime;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity * Time.deltaTime;

        if (weaponController != null)
        {
            weaponController.RotateWeapon(mouseX, mouseY);
        }
    }
}
