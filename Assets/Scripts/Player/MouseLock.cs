using UnityEngine;

public class MouseLock : MonoBehaviour
{
    /// <summary>是否处于 UI 输入状态。由 UIInputManager 统一计算。</summary>
    public static bool IsUIBlocking => UIInputManager.AnyUIOpen;

    void Start()
    {
        LockCursor();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape) && UIWindowStack.AnyOpen)
        {
            UIWindowStack.CloseTopWindow();
            return;
        }

        if (UIInputManager.AnyUIOpen)
        {
            UnlockCursor();
        }
        else
        {
            LockCursor();
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
}
