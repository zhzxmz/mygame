using UnityEngine;

public class WeaponController : MonoBehaviour
{
    public Transform weaponPivot;

    [Header("挥剑参数")]
    [Tooltip("每帧鼠标移动超过该距离才更新剑的目标方向（屏幕像素）")]
    [SerializeField] private float minMouseDelta = 2f;

    [Tooltip("剑的旋转速度（度/秒）")]
    [SerializeField, Range(180f, 1440f)] private float rotationSpeed = 720f;

    [Tooltip("剑的 Trigger Collider，运行时自动查找同物体上的 Collider")]
    [SerializeField] private Collider weaponCollider;

    private bool isControlling;
    private bool hasTarget;
    private Quaternion targetRotation;

    public float MinMouseDelta => minMouseDelta;

    void Awake()
    {
        if (weaponCollider == null)
        {
            weaponCollider = GetComponent<Collider>();
        }

        // 非控制阶段关闭命中判定，左键按住期间开启。
        SetHitboxActive(false);
    }

    void Update()
    {
        if (!isControlling || !hasTarget || weaponPivot == null) return;

        float speed = Mathf.Clamp(rotationSpeed, 180f, 1440f);
        weaponPivot.localRotation = Quaternion.RotateTowards(
            weaponPivot.localRotation,
            targetRotation,
            speed * Time.deltaTime
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

        // 连续方向映射：不量化成 8 方向，直接用方向矢量的 x/y 分量决定目标俯仰/偏航。
        float targetX = -direction.y * 90f;
        float targetY = direction.x * 90f;
        targetRotation = Quaternion.Euler(targetX, targetY, 0f);
        hasTarget = true;
    }

    /// <summary>左键松开：停止控制剑。</summary>
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
