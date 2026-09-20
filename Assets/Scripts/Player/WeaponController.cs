using UnityEngine;

public class WeaponController : MonoBehaviour
{
    public Transform weaponPivot;

    [Header("挥剑参数")]
    [Tooltip("每帧鼠标移动超过该距离才更新剑的目标方向（屏幕像素）")]
    [SerializeField] private float minMouseDelta = 3f;

    [Tooltip("剑的旋转速度（度/秒）")]
    [SerializeField, Range(180f, 1440f)] private float rotationSpeed = 720f;

    [Tooltip("松开左键后剑返回默认姿态的速度（度/秒）")]
    [SerializeField] private float returnSpeed = 360f;

    [Tooltip("相对于默认姿态的最大水平活动角度")]
    [SerializeField] private float horizontalAngleLimit = 80f;

    [Tooltip("相对于默认姿态的最大垂直活动角度")]
    [SerializeField] private float verticalAngleLimit = 60f;

    [Tooltip("剑的 Trigger Collider，运行时自动查找同物体上的 Collider")]
    [SerializeField] private Collider weaponCollider;

    private bool isControlling;
    private bool hasTarget;
    private Quaternion targetRotation;
    private Quaternion defaultLocalRotation;
    private bool hasDefaultRotation;

    public float MinMouseDelta => minMouseDelta;

    void Awake()
    {
        if (weaponCollider == null)
        {
            weaponCollider = GetComponent<Collider>();
        }

        // 保存默认剑姿态，作为活动角度限制和松开回位的基准。
        if (weaponPivot != null)
        {
            defaultLocalRotation = weaponPivot.localRotation;
            hasDefaultRotation = true;
        }

        // 非控制阶段关闭命中判定，左键按住期间开启。
        SetHitboxActive(false);
    }

    void Update()
    {
        if (weaponPivot == null || !hasDefaultRotation) return;

        if (isControlling)
        {
            if (!hasTarget) return;

            float speed = Mathf.Clamp(rotationSpeed, 180f, 1440f);
            weaponPivot.localRotation = Quaternion.RotateTowards(
                weaponPivot.localRotation,
                targetRotation,
                speed * Time.deltaTime
            );
            return;
        }

        // 松开左键后平滑回到默认姿态。
        float returnSpd = Mathf.Max(0f, returnSpeed);
        weaponPivot.localRotation = Quaternion.RotateTowards(
            weaponPivot.localRotation,
            defaultLocalRotation,
            returnSpd * Time.deltaTime
        );
    }

    /// <summary>左键按下：开始持续控制剑。</summary>
    public void BeginControl()
    {
        if (isControlling) return;

        isControlling = true;
        hasTarget = false;
        SetHitboxActive(true);
    }

    /// <summary>持续更新鼠标方向对应的剑目标方向。</summary>
    public void UpdateTargetDirection(Vector2 direction)
    {
        if (!isControlling) return;
        if (direction.sqrMagnitude < 0.0001f) return;

        direction = direction.normalized;

        float hLimit = Mathf.Max(0f, horizontalAngleLimit);
        float vLimit = Mathf.Max(0f, verticalAngleLimit);

        // 连续方向映射：不量化成 8 方向，直接用方向矢量的 x/y 分量决定目标俯仰/偏航。
        // 限制相对默认姿态，而不是限制世界欧拉角。
        float targetX = Mathf.Clamp(-direction.y * 90f, -vLimit, vLimit);
        float targetY = Mathf.Clamp(direction.x * 90f, -hLimit, hLimit);

        Quaternion offset = Quaternion.Euler(targetX, targetY, 0f);
        targetRotation = defaultLocalRotation * offset;
        hasTarget = true;
    }

    /// <summary>左键松开：停止控制剑，随后由 Update 平滑回默认姿态。</summary>
    public void EndControl()
    {
        if (!isControlling) return;

        isControlling = false;
        hasTarget = false;
        SetHitboxActive(false);
    }

    private void SetHitboxActive(bool active)
    {
        if (weaponCollider != null)
        {
            weaponCollider.enabled = active;
        }
    }
}
