using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// B 阶段验证：挖掘卡顿后的 Camera 稳定性。
///
/// 目标：当主线程因挖掘短暂卡顿（鼠标增量在恢复帧一次性到达）时，
///       不应该因为“鼠标累计输入”导致下一帧 Camera 大幅旋转。
///
/// 修复要点（本套件逐条验证）：
///   1) 鼠标轴返回的已是“本帧累计位移增量”，不得再乘 Time.deltaTime；
///   2) 轴值 ÷ MouseAxisSensitivity(0.1) → 近似像素位移，× lookDegreesPerPixel → 角度；
///   3) CameraController 单次调用有旋转预算 maxLookDegreesPerCall（默认 40°），
///      超出部分直接丢弃（不回放），卡顿累计输入不会变成大幅跳转；
///   4) 俯仰仍限制在 ±90°；
///   5) 无 cameraController / 无 player 时安全返回。
/// </summary>
public static class PlayerLookStabilityValidation
{
    private const float DefaultDegreesPerPixel = 1.3333f;
    private const float DefaultMaxDegreesPerCall = 40f;
    private const float Tolerance = 0.02f;

    private static int checks;
    private static int failures;

    [MenuItem("Tools/Player/Validate Player Look Stability")]
    public static void ValidateFromMenu()
    {
        ValidateAll();
        LogSummary();

        Debug.Log(
            "[PlayerLookStabilityValidation] Menu validation result = " +
            (failures == 0 ? "PASS" : "FAIL")
        );
    }

    /// <summary>Batchmode 入口：-executeMethod PlayerLookStabilityValidation.RunBatch。</summary>
    public static void RunBatch()
    {
        ValidateAll();
        LogSummary();
        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    private static void ValidateAll()
    {
        checks = 0;
        failures = 0;

        ValidateDefaults();
        ValidateNormalDeltaScaling();
        ValidateFrameTimeIndependenceGuard();
        ValidateHitchAccumulatedDeltaIsCapped();
        ValidatePitchClamp();
        ValidateCapConfiguration();
        ValidateNullSafety();
    }

    private static void ValidateDefaults()
    {
        GameObject playerObject;
        GameObject cameraObject;
        GameObject inputObject;
        CameraController camera;
        PlayerInputController input;

        CreateRig(out playerObject, out cameraObject, out inputObject, out camera, out input);

        try
        {
            Check(
                Mathf.Abs(input.lookDegreesPerPixel - DefaultDegreesPerPixel) < 0.001f,
                "默认 lookDegreesPerPixel = 1.3333（等价原 800×0.1÷60fps 手感），实际 " +
                input.lookDegreesPerPixel
            );

            Check(
                Mathf.Abs(camera.maxLookDegreesPerCall - DefaultMaxDegreesPerCall) < 0.001f,
                "默认 maxLookDegreesPerCall = 40°，实际 " + camera.maxLookDegreesPerCall
            );

            MethodInfo method = typeof(PlayerInputController).GetMethod(
                "ApplyLookDelta",
                new System.Type[] { typeof(Vector2) }
            );

            Check(
                method != null && method.GetParameters().Length == 1,
                "ApplyLookDelta(Vector2) 存在且不含时间参数（结构性帧时间无关）"
            );
        }
        finally
        {
            DestroyRig(playerObject, cameraObject, inputObject);
        }
    }

    private static void ValidateNormalDeltaScaling()
    {
        GameObject playerObject;
        GameObject cameraObject;
        GameObject inputObject;
        CameraController camera;
        PlayerInputController input;

        CreateRig(out playerObject, out cameraObject, out inputObject, out camera, out input);

        try
        {
            // 轴值 0.1 = 1 像素（MouseAxisSensitivity = 0.1）⇒ 1 × 1.3333 = 1.3333°
            float startYaw = playerObject.transform.eulerAngles.y;
            input.ApplyLookDelta(new Vector2(0.1f, 0f));
            float applied = Mathf.DeltaAngle(
                startYaw, playerObject.transform.eulerAngles.y);

            Check(
                Mathf.Abs(applied - DefaultDegreesPerPixel) < Tolerance,
                "正常位移：1 像素 ⇒ " + applied.ToString("F4") +
                "°（期望 " + DefaultDegreesPerPixel + "°）；若仍乘 Time.deltaTime 结果会随帧耗时变化"
            );

            // 轴值 0.5 = 5 像素 ⇒ 6.6665°
            startYaw = playerObject.transform.eulerAngles.y;
            input.ApplyLookDelta(new Vector2(0.5f, 0f));
            applied = Mathf.DeltaAngle(
                startYaw, playerObject.transform.eulerAngles.y);

            Check(
                Mathf.Abs(applied - 5f * DefaultDegreesPerPixel) < Tolerance,
                "正常位移：5 像素 ⇒ " + applied.ToString("F4") +
                "°（期望 " + (5f * DefaultDegreesPerPixel).ToString("F4") + "°）"
            );

            // 灵敏度是唯一缩放来源：改成 2.0 后 1 像素应变成 2°
            input.lookDegreesPerPixel = 2f;
            startYaw = playerObject.transform.eulerAngles.y;
            input.ApplyLookDelta(new Vector2(0.1f, 0f));
            applied = Mathf.DeltaAngle(
                startYaw, playerObject.transform.eulerAngles.y);

            Check(
                Mathf.Abs(applied - 2f) < Tolerance,
                "lookDegreesPerPixel 是唯一缩放来源：1 像素 ⇒ " + applied.ToString("F4") +
                "°（期望 2.0000°）"
            );
        }
        finally
        {
            DestroyRig(playerObject, cameraObject, inputObject);
        }
    }

    private static void ValidateFrameTimeIndependenceGuard()
    {
        // 静态守卫：防止再次把 Time 因子乘进鼠标增量（旧实现 `axis × 800 × Time.deltaTime`）。
        // 只匹配“乘法因子”形态，避免命中注释里的说明文字。
        string pattern = @"\*\s*Time\.(deltaTime|unscaledDeltaTime|smoothDeltaTime|fixedDeltaTime)";

        Check(
            !SourceMatches("Assets/Scripts/Player/PlayerInputController.cs", pattern),
            "PlayerInputController 的鼠标增量没有乘任何 Time 因子（源码静态守卫）"
        );

        Check(
            !SourceMatches("Assets/Scripts/Player/CameraController.cs", pattern),
            "CameraController 的旋转没有乘任何 Time 因子（源码静态守卫）"
        );

        Check(
            SourceMatches(
                "Assets/Scripts/Player/PlayerInputController.cs",
                "GetAxisRaw\\(\"Mouse X\"\\)"
            ),
            "PlayerInputController 使用 GetAxisRaw 读取鼠标增量（与剑的鼠标路径一致）"
        );
    }

    private static void ValidateHitchAccumulatedDeltaIsCapped()
    {
        GameObject playerObject;
        GameObject cameraObject;
        GameObject inputObject;
        CameraController camera;
        PlayerInputController input;

        CreateRig(out playerObject, out cameraObject, out inputObject, out camera, out input);

        try
        {
            // 模拟一次 160ms 卡顿：玩家在这段时间里移动了 250 像素（轴值 25）。
            // 旧实现：25 × 800 × 0.16 = 3200°（一次性大幅跳转）；
            // 新实现：25 / 0.1 × 1.3333 = 333.33°，且被 maxLookDegreesPerCall 截断到 40°。
            const float hitchAxisDelta = 25f;
            float oldFormulaDegrees = hitchAxisDelta * 800f * 0.16f;

            float startYaw = playerObject.transform.eulerAngles.y;
            input.ApplyLookDelta(new Vector2(hitchAxisDelta, 0f));
            float appliedYaw = Mathf.DeltaAngle(
                startYaw, playerObject.transform.eulerAngles.y);

            Check(
                appliedYaw > 0f &&
                appliedYaw <= camera.maxLookDegreesPerCall + Tolerance,
                "卡顿累计输入被截断：本帧 yaw 仅转 " + appliedYaw.ToString("F4") +
                "°（≤ 上限 " + camera.maxLookDegreesPerCall +
                "°，未经修复时会一次转出 " + oldFormulaDegrees.ToString("F0") + "°）"
            );

            float startPitch = camera.xRotation;
            input.ApplyLookDelta(new Vector2(0f, hitchAxisDelta));
            float appliedPitch = Mathf.Abs(camera.xRotation - startPitch);

            Check(
                appliedPitch > 0f &&
                appliedPitch <= camera.maxLookDegreesPerCall + Tolerance,
                "卡顿累计输入被截断：本帧 pitch 仅转 " + appliedPitch.ToString("F4") +
                "°（≤ 上限 " + camera.maxLookDegreesPerCall + "°）"
            );

            // 反向同理（鼠标向另一侧累计）
            startYaw = playerObject.transform.eulerAngles.y;
            input.ApplyLookDelta(new Vector2(-hitchAxisDelta, 0f));
            appliedYaw = Mathf.DeltaAngle(
                startYaw, playerObject.transform.eulerAngles.y);

            Check(
                appliedYaw < 0f &&
                Mathf.Abs(appliedYaw) <= camera.maxLookDegreesPerCall + Tolerance,
                "反向卡顿累计输入同样被截断：" + appliedYaw.ToString("F4") + "°"
            );
        }
        finally
        {
            DestroyRig(playerObject, cameraObject, inputObject);
        }
    }

    private static void ValidatePitchClamp()
    {
        GameObject playerObject;
        GameObject cameraObject;
        GameObject inputObject;
        CameraController camera;
        PlayerInputController input;

        CreateRig(out playerObject, out cameraObject, out inputObject, out camera, out input);

        try
        {
            for (int i = 0; i < 10; i++)
            {
                input.ApplyLookDelta(new Vector2(0f, 25f));
            }

            Check(
                Mathf.Abs(camera.xRotation + 90f) < Tolerance,
                "俯仰上限仍为 -90°（xRotation=" + camera.xRotation + "）"
            );

            for (int i = 0; i < 20; i++)
            {
                input.ApplyLookDelta(new Vector2(0f, -25f));
            }

            Check(
                Mathf.Abs(camera.xRotation - 90f) < Tolerance,
                "俯仰下限仍为 +90°（xRotation=" + camera.xRotation + "）"
            );

            Check(
                Mathf.Abs(camera.transform.localRotation.eulerAngles.x - 90f) < 0.5f ||
                Mathf.Abs(camera.transform.localRotation.eulerAngles.x - 270f) < 0.5f,
                "相机 localRotation 与 xRotation 同步（eulerX=" +
                camera.transform.localRotation.eulerAngles.x.ToString("F2") + "）"
            );
        }
        finally
        {
            DestroyRig(playerObject, cameraObject, inputObject);
        }
    }

    private static void ValidateCapConfiguration()
    {
        GameObject playerObject;
        GameObject cameraObject;
        GameObject inputObject;
        CameraController camera;
        PlayerInputController input;

        CreateRig(out playerObject, out cameraObject, out inputObject, out camera, out input);

        try
        {
            // cap = 0 ⇒ 关闭上限（不做截断）：3 像素 ⇒ 4.0°
            camera.maxLookDegreesPerCall = 0f;
            float startYaw = playerObject.transform.eulerAngles.y;
            input.ApplyLookDelta(new Vector2(0.3f, 0f));
            float applied = Mathf.DeltaAngle(
                startYaw, playerObject.transform.eulerAngles.y);

            Check(
                Mathf.Abs(applied - 4f) < Tolerance,
                "maxLookDegreesPerCall = 0（关闭）时不截断：3 像素 ⇒ " +
                applied.ToString("F4") + "°（期望 4.0000°）"
            );

            // cap = 1 ⇒ 同样的 3 像素位移被截断到 1°
            camera.maxLookDegreesPerCall = 1f;
            startYaw = playerObject.transform.eulerAngles.y;
            input.ApplyLookDelta(new Vector2(0.3f, 0f));
            applied = Mathf.DeltaAngle(
                startYaw, playerObject.transform.eulerAngles.y);

            Check(
                Mathf.Abs(applied - 1f) < Tolerance,
                "maxLookDegreesPerCall = 1° 时 3 像素位移被截断为 " +
                applied.ToString("F4") + "°（期望 1.0000°）"
            );

            // 正常小位移不应被默认上限波及
            camera.maxLookDegreesPerCall = DefaultMaxDegreesPerCall;
            startYaw = playerObject.transform.eulerAngles.y;
            input.ApplyLookDelta(new Vector2(0.1f, 0f));
            applied = Mathf.DeltaAngle(
                startYaw, playerObject.transform.eulerAngles.y);

            Check(
                Mathf.Abs(applied - DefaultDegreesPerPixel) < Tolerance,
                "默认上限不干扰正常小位移：1 像素 ⇒ " + applied.ToString("F4") + "°"
            );
        }
        finally
        {
            DestroyRig(playerObject, cameraObject, inputObject);
        }
    }

    private static void ValidateNullSafety()
    {
        GameObject playerObject;
        GameObject cameraObject;
        GameObject inputObject;
        CameraController camera;
        PlayerInputController input;

        CreateRig(out playerObject, out cameraObject, out inputObject, out camera, out input);

        try
        {
            bool threw = false;
            input.cameraController = null;

            try
            {
                input.ApplyLookDelta(new Vector2(5f, 5f));
            }
            catch (System.Exception)
            {
                threw = true;
            }

            Check(!threw, "cameraController 为空时 ApplyLookDelta 安全返回（不抛异常）");

            input.cameraController = camera;

            Quaternion beforeRotation = camera.transform.localRotation;
            float beforePitch = camera.xRotation;
            camera.player = null;
            camera.RotateCamera(10f, 10f);

            Check(
                camera.xRotation == beforePitch &&
                camera.transform.localRotation == beforeRotation,
                "player 为空时 RotateCamera 不修改任何旋转状态"
            );
        }
        finally
        {
            DestroyRig(playerObject, cameraObject, inputObject);
        }
    }

    private static void CreateRig(
        out GameObject playerObject,
        out GameObject cameraObject,
        out GameObject inputObject,
        out CameraController camera,
        out PlayerInputController input)
    {
        playerObject = new GameObject("PlayerLookStabilityValidationPlayer");
        playerObject.SetActive(false);
        playerObject.transform.position = Vector3.zero;
        playerObject.transform.rotation = Quaternion.identity;
        playerObject.transform.localScale = Vector3.one;

        cameraObject = new GameObject("PlayerLookStabilityValidationCamera");
        cameraObject.SetActive(false);
        camera = cameraObject.AddComponent<CameraController>();
        camera.player = playerObject.transform;
        camera.xRotation = 0f;
        camera.transform.localRotation = Quaternion.identity;

        inputObject = new GameObject("PlayerLookStabilityValidationInput");
        inputObject.SetActive(false);
        input = inputObject.AddComponent<PlayerInputController>();
        input.cameraController = camera;
        input.weaponController = null;
    }

    private static void DestroyRig(
        GameObject playerObject,
        GameObject cameraObject,
        GameObject inputObject)
    {
        if (inputObject != null) UnityEngine.Object.DestroyImmediate(inputObject);
        if (cameraObject != null) UnityEngine.Object.DestroyImmediate(cameraObject);
        if (playerObject != null) UnityEngine.Object.DestroyImmediate(playerObject);
    }

    private static bool SourceMatches(string assetRelativePath, string pattern)
    {
        string relative = assetRelativePath.StartsWith("Assets/")
            ? assetRelativePath.Substring("Assets/".Length)
            : assetRelativePath;

        string fullPath = Path.Combine(Application.dataPath, relative);
        if (!File.Exists(fullPath)) return false;

        string text = File.ReadAllText(fullPath);
        return Regex.IsMatch(text, pattern);
    }

    private static void Check(bool condition, string message)
    {
        checks++;

        if (condition)
        {
            Debug.Log("PASS - " + message);
            return;
        }

        failures++;
        Debug.LogError("FAIL - " + message);
    }

    private static void LogSummary()
    {
        Debug.Log(
            "[PlayerLookStabilityValidation] ===== SUMMARY =====\n" +
            "Checks = " + checks + "\n" +
            "Failures = " + failures + "\n" +
            "Result = " + (failures == 0 ? "PASS" : "FAIL")
        );
    }
}
