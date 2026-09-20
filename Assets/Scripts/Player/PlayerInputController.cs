using UnityEngine;
using UnityEngine.EventSystems;

public class PlayerInputController : MonoBehaviour
{
    public CameraController cameraController;
    public WeaponController weaponController;

    float mouseSensitivity = 800f;

    // 项目游玩时锁定光标，部分平台下 Input.mousePosition 会固定在屏幕中心。
    // InputManager 的 Mouse X / Mouse Y Sensitivity 为 0.1，这里用鼠标轴累计并换算成近似屏幕像素位移。
    private const float MouseAxisSensitivity = 0.1f;

    private Vector2 previousMousePosition;

    void Update()
    {
        bool uiOpen = MouseLock.IsUIBlocking;

        if (uiOpen)
        {
            // UI 打开时：鼠标悬停在 UI 上则完全交给 UI。
            bool pointerOverUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (pointerOverUI)
            {
                StopSwordControl();
                return;
            }

            // 鼠标不在 UI 上：允许持续控制剑，但不旋转视角。
            UpdateSwordControl();
            return;
        }

        // 无 UI：如果鼠标处于自由状态（Alt 切换），不处理游戏鼠标输入。
        if (Cursor.lockState != CursorLockMode.Locked)
        {
            StopSwordControl();
            return;
        }

        UpdateSwordControl();

        // 左键松开时才旋转视角；左键按住期间专注于控制剑。
        if (!Input.GetMouseButton(0))
        {
            float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity * Time.deltaTime;
            float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity * Time.deltaTime;

            if (cameraController != null)
            {
                cameraController.RotateCamera(mouseX, mouseY);
            }
        }
    }

    private void UpdateSwordControl()
    {
        if (weaponController == null) return;

        // 左键按下：开始进入持续挥剑控制状态。
        if (Input.GetMouseButtonDown(0))
        {
            previousMousePosition = Input.mousePosition;
            weaponController.BeginControl();
        }

        // 左键按住：持续读取鼠标每帧移动方向。
        if (Input.GetMouseButton(0))
        {
            Vector2 mouseDelta = ReadMouseDelta();

            if (mouseDelta.magnitude >= weaponController.MinMouseDelta)
            {
                weaponController.UpdateTargetDirection(mouseDelta.normalized);
            }
        }

        // 左键松开：停止控制剑。
        if (Input.GetMouseButtonUp(0))
        {
            weaponController.EndControl();
        }
    }

    private void StopSwordControl()
    {
        if (weaponController != null)
        {
            weaponController.EndControl();
        }
    }

    private Vector2 ReadMouseDelta()
    {
        if (Cursor.lockState == CursorLockMode.Locked)
        {
            Vector2 mouseAxisDelta = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
            return mouseAxisDelta / MouseAxisSensitivity;
        }

        Vector2 currentMousePosition = Input.mousePosition;
        Vector2 delta = currentMousePosition - previousMousePosition;
        previousMousePosition = currentMousePosition;
        return delta;
    }
}
