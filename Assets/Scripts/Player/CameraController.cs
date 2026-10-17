using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraController : MonoBehaviour
{
    public Transform player;
    public float xRotation;

    public float mouseSensitivity=200; // 灵敏度（历史字段：保留序列化兼容，当前不参与计算）

    [Tooltip(
        "单次 RotateCamera 调用允许应用的最大旋转角度（度）。\n" +
        "卡顿帧兜底：挖掘造成主线程卡顿时，鼠标增量会在恢复帧一次性到达，" +
        "这里把它截断，避免下一帧视角大幅跳转（超出部分直接丢弃，不回放）。\n" +
        "<= 0 表示不限制。"
    )]
    public float maxLookDegreesPerCall = 40f;

    // 旋转函数，只处理旋转逻辑
    public void RotateCamera(float mouseX, float mouseY)
    {
        if (player == null) return;

        // B 阶段：单次调用的旋转预算（卡顿帧保护）。
        float yawDelta = ClampLookDelta(mouseX);
        float pitchDelta = ClampLookDelta(mouseY);

        xRotation -= pitchDelta;
        xRotation = Mathf.Clamp(xRotation, -90f, 90f);

        player.Rotate(Vector3.up * yawDelta);
        transform.localRotation = Quaternion.Euler(xRotation, 0, 0);
    }

    /// <summary>
    /// 单次调用的旋转预算：超出 maxLookDegreesPerCall 的部分直接丢弃。
    /// 目的：卡顿期间累计的鼠标位移在恢复帧被一次性应用时，不会变成大幅跳转。
    /// </summary>
    private float ClampLookDelta(float deltaDegrees)
    {
        if (maxLookDegreesPerCall <= 0f)
        {
            return deltaDegrees;
        }

        return Mathf.Clamp(
            deltaDegrees,
            -maxLookDegreesPerCall,
            maxLookDegreesPerCall
        );
    }
}