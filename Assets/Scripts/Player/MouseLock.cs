using UnityEngine;

public class MouseLock : MonoBehaviour
{
    /// <summary>是否处于 UI 输入状态。由 UIInputManager 统一计算。</summary>
    public static bool IsUIBlocking => UIInputManager.AnyUIOpen;

    private bool uiMode;
    private bool wasUIBlocking;

    void Start()
    {
        uiMode = false;
        ApplyCursor();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape) && UIWindowStack.AnyOpen)
        {
            UIWindowStack.CloseTopWindow();
            return;
        }

        bool uiOpen = UIInputManager.AnyUIOpen;

        if (uiOpen)
        {
            // 任何 UI 打开时，强制进入 UI 操作状态。
            if (!wasUIBlocking)
            {
                wasUIBlocking = true;
                uiMode = true;
            }
        }
        else
        {
            if (wasUIBlocking)
            {
                // 最后一个 UI 刚关闭：自动恢复游戏控制状态。
                wasUIBlocking = false;
                uiMode = false;
            }
            else if (Input.GetKeyDown(KeyCode.LeftAlt) || Input.GetKeyDown(KeyCode.RightAlt))
            {
                // 没有 UI 时，Alt 在游戏控制 / UI 操作之间切换。
                uiMode = !uiMode;
            }
        }

        ApplyCursor();
    }

    private void ApplyCursor()
    {
        if (uiMode)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }
}
