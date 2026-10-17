using UnityEngine;
using UnityEngine.EventSystems;

public class PlayerInputController : MonoBehaviour
{
    public CameraController cameraController;
    public WeaponController weaponController;

    [Tooltip(
        "每 1 像素鼠标位移对应的视角旋转角度（度/像素）。\n" +
        "默认 1.3333 = 原实现 800(灵敏度) × 0.1(MouseAxisSensitivity) ÷ 60fps，\n" +
        "即保持原 60fps 下的手感；同时不再乘 Time.deltaTime，故帧率无关、卡顿不放大。"
    )]
    public float lookDegreesPerPixel = 1.3333f;

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
            // B 阶段：鼠标轴返回的就是“本帧累计位移增量”（不含时间因子），因此：
            //   1) 不再乘 Time.deltaTime —— 那是卡顿后视角大幅跳转的根因
            //      （卡顿帧 dt 很大，累计位移 × 大 dt 会一次性转出很大角度）；
            //   2) 用 GetAxisRaw，与剑的鼠标路径一致，避免 GetAxis 平滑造成的残留漂移。
            Vector2 mouseAxisDelta = new Vector2(
                Input.GetAxisRaw("Mouse X"),
                Input.GetAxisRaw("Mouse Y")
            );

            ApplyLookDelta(mouseAxisDelta);
        }
    }

    /// <summary>
    /// 把“一帧的鼠标轴增量”应用到视角（纯输入换算，不读 Input、不乘 Time.deltaTime）：
    ///   轴值 ÷ MouseAxisSensitivity → 近似像素位移；× lookDegreesPerPixel → 角度。
    /// 单帧上限由 CameraController.maxLookDegreesPerCall 兜底（卡顿累计输入截断）。
    /// 拆成方法是为了让换算/截断逻辑可在批处理测试中被直接验证。
    /// </summary>
    public void ApplyLookDelta(Vector2 mouseAxisDelta)
    {
        if (cameraController == null) return;

        Vector2 mousePixels = mouseAxisDelta / MouseAxisSensitivity;

        cameraController.RotateCamera(
            mousePixels.x * lookDegreesPerPixel,
            mousePixels.y * lookDegreesPerPixel
        );
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

        // 左键按住：持续读取鼠标每帧移动量。
        if (Input.GetMouseButton(0))
        {
            Vector2 mouseDelta = ReadMouseDelta();

            if (mouseDelta.sqrMagnitude > 0f)
            {
                weaponController.ApplyMouseRotation(mouseDelta);
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
            // 使用 GetAxisRaw，避免 GetAxis 的平滑导致鼠标停止后剑还继续漂移。
            Vector2 mouseAxisDelta = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
            return mouseAxisDelta / MouseAxisSensitivity;
        }

        Vector2 currentMousePosition = Input.mousePosition;
        Vector2 delta = currentMousePosition - previousMousePosition;
        previousMousePosition = currentMousePosition;
        return delta;
    }
}
