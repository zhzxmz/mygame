using UnityEngine;

public class MouseLock : MonoBehaviour
{
    /// <summary>是否处于 UI 输入状态。由 UIInputManager 统一计算。</summary>
    public static bool IsUIBlocking => UIInputManager.AnyUIOpen;

    private bool mouseFree;
    private bool wasUIBlocking;

    void Start()
    {
        mouseFree = false;
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
            // UI 打开时鼠标必须自由，Alt 不参与切换。
            wasUIBlocking = true;
            UnlockCursor();
            return;
        }

        // 所有 UI 关闭后，不自动重新锁定鼠标，保持自由直到玩家按 Alt。
        if (wasUIBlocking)
        {
            wasUIBlocking = false;
            mouseFree = true;
        }

        if (Input.GetKeyDown(KeyCode.LeftAlt) || Input.GetKeyDown(KeyCode.RightAlt))
        {
            mouseFree = !mouseFree;
        }

        if (mouseFree)
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
