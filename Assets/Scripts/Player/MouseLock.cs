using UnityEngine;

public class MouseLock : MonoBehaviour
{
    /// <summary>是否处于 UI 输入状态（例如背包打开）。为 true 时释放鼠标并停止游戏输入。</summary>
    public static bool IsUIBlocking { get; set; }

    public CameraController cameraController; // 拖拽赋值
    
    void Start()
    {
        LockCursor();
        EnableCameraControl(true);
    }

    void Update()
    {
        if (IsUIBlocking || Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt))
        {
            UnlockCursor();
            EnableCameraControl(false);
        }
        else
        {
            LockCursor();
            EnableCameraControl(true);
        }
    }

    private void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void UnlockCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void EnableCameraControl(bool enable)
    {
        if (cameraController != null)
        {
            cameraController.enabled = enable;
        }
    }
}