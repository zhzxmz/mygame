using UnityEngine;

/// <summary>
/// 一个 UI 窗口。
/// 打开/关闭时自动注册/解除 UIInputManager 和 UIWindowStack。
/// </summary>
public class UIWindow : MonoBehaviour
{
    public GameObject target;
    public bool closeOnEsc = true;

    void Awake()
    {
        if (target == null)
        {
            target = gameObject;
        }
    }

    void OnDestroy()
    {
        UIWindowStack.UnregisterWindow(this);
        UIInputManager.Unregister(this);
    }

    public void Open()
    {
        if (target != null)
        {
            target.SetActive(true);
        }

        UIWindowStack.RegisterWindow(this);
        UIInputManager.Register(this);
    }

    public void Close()
    {
        UIWindowStack.UnregisterWindow(this);
        UIInputManager.Unregister(this);

        if (target != null)
        {
            target.SetActive(false);
        }
    }

    public void Toggle()
    {
        if (target != null && target.activeSelf)
        {
            Close();
        }
        else
        {
            Open();
        }
    }

    public bool IsOpen => target != null && target.activeSelf;
}
