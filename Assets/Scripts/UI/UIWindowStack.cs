using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// UI 窗口栈：记录当前打开的 UI 窗口顺序。
/// ESC 由 MouseLock 统一调用 CloseTopWindow()。
/// </summary>
public static class UIWindowStack
{
    private static readonly List<UIWindow> windows = new List<UIWindow>();

    public static bool AnyOpen => windows.Count > 0;
    public static UIWindow Top => windows.Count > 0 ? windows[windows.Count - 1] : null;

    public static void RegisterWindow(UIWindow window)
    {
        if (window == null) return;

        if (!windows.Contains(window))
        {
            windows.Add(window);
        }
    }

    public static void UnregisterWindow(UIWindow window)
    {
        if (window == null) return;

        windows.Remove(window);
    }

    public static void CloseTopWindow()
    {
        if (windows.Count == 0) return;

        UIWindow top = windows[windows.Count - 1];
        if (top == null)
        {
            windows.RemoveAt(windows.Count - 1);
            return;
        }

        if (top.closeOnEsc)
        {
            top.Close();
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        windows.Clear();
    }
}
