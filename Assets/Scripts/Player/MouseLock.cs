using UnityEngine;

public class MouseLock : MonoBehaviour
{
    public CameraController cameraController; // 拖拽赋值
    
    void Start()
    {
        LockCursor();
        EnableCameraControl(true);
    }

    void Update()
    {
        if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt))
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