using UnityEngine;
using UnityEngine.EventSystems;

public class PlayerInputController : MonoBehaviour
{
    public CameraController cameraController;
    public WeaponController weaponController;

    float mouseSensitivity = 800f;

    // 项目默认在游玩时锁定光标，部分平台下 Input.mousePosition 会固定在屏幕中心。
    // InputManager 的 Mouse X / Mouse Y Sensitivity 为 0.1，这里用鼠标轴累计并按比例换算成近似屏幕像素位移。
    private const float MouseAxisSensitivity = 0.1f;

    private Vector2 mouseStartPosition;
    private Vector2 mouseCurrentPosition;
    private Vector2 lockedSwipePosition;
    private bool isTrackingSwipe;
    private bool swipeTriggered;

    void Update()
    {
        bool uiOpen = MouseLock.IsUIBlocking;

        if (uiOpen)
        {
            // UI 打开时：鼠标悬停在 UI 上则完全交给 UI。
            bool pointerOverUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (pointerOverUI)
            {
                ResetSwipeTracking();
                return;
            }

            // 鼠标不在 UI 上：允许滑动攻击，但不旋转视角。
            UpdateSwipeAttack();
            return;
        }

        // 无 UI：如果鼠标处于自由状态（Alt 切换），不处理游戏鼠标输入。
        if (Cursor.lockState != CursorLockMode.Locked)
        {
            ResetSwipeTracking();
            return;
        }

        // 左键按住时进入滑动攻击检测，不再旋转视角。
        if (Input.GetMouseButton(0))
        {
            UpdateSwipeAttack();
            return;
        }

        ResetSwipeTracking();

        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity * Time.deltaTime;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity * Time.deltaTime;

        if (cameraController != null)
        {
            cameraController.RotateCamera(mouseX, mouseY);
        }
    }

    private void UpdateSwipeAttack()
    {
        if (weaponController == null)
        {
            ResetSwipeTracking();
            return;
        }

        if (Input.GetMouseButtonDown(0))
        {
            BeginSwipe();
        }

        if (!Input.GetMouseButton(0))
        {
            ResetSwipeTracking();
            return;
        }

        if (!isTrackingSwipe)
        {
            // 处理 UI 关闭 / 脚本启用时鼠标已经按住的情况，补一个起点。
            BeginSwipe();
        }

        if (swipeTriggered) return;

        mouseCurrentPosition = GetCurrentSwipePosition();
        Vector2 delta = mouseCurrentPosition - mouseStartPosition;

        if (delta.magnitude < weaponController.MinSwipeDistance) return;

        weaponController.StartSwipeAttack(delta.normalized);
        swipeTriggered = true;
    }

    private void BeginSwipe()
    {
        lockedSwipePosition = Vector2.zero;
        mouseStartPosition = Cursor.lockState == CursorLockMode.Locked ? Vector2.zero : (Vector2)Input.mousePosition;
        mouseCurrentPosition = mouseStartPosition;
        isTrackingSwipe = true;
        swipeTriggered = false;
    }

    private Vector2 GetCurrentSwipePosition()
    {
        if (Cursor.lockState == CursorLockMode.Locked)
        {
            Vector2 mouseAxisDelta = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
            lockedSwipePosition += mouseAxisDelta / MouseAxisSensitivity;
            return lockedSwipePosition;
        }

        return Input.mousePosition;
    }

    private void ResetSwipeTracking()
    {
        isTrackingSwipe = false;
        swipeTriggered = false;
        lockedSwipePosition = Vector2.zero;
    }
}
