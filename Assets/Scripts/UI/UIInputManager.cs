using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 统一 UI 输入状态管理。
/// UI 打开时注册自己，关闭时解除自己；任意 UI 打开时鼠标保持自由。
/// </summary>
public static class UIInputManager
{
    private static readonly HashSet<object> registrations = new HashSet<object>();

    public static bool AnyUIOpen => registrations.Count > 0;

    public static void Register(object requester)
    {
        if (requester == null) return;
        registrations.Add(requester);
    }

    public static void Unregister(object requester)
    {
        if (requester == null) return;
        registrations.Remove(requester);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        registrations.Clear();
    }
}
