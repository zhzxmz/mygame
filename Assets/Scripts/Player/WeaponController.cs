using UnityEngine;

public class WeaponController : MonoBehaviour
{
    public Transform weaponPivot;

    [Header("挥剑参数")]
    [Tooltip("每帧鼠标移动超过该距离才产生旋转（屏幕像素）")]
    [SerializeField] private float minMouseDelta = 3f;

    [Tooltip("鼠标控制时的最大旋转速度（度/秒）")]
    [SerializeField, Range(180f, 1440f)] private float rotationSpeed = 720f;

    [Tooltip("松开左键后剑返回默认姿态的速度（度/秒）")]
    [SerializeField] private float returnSpeed = 360f;

    [Tooltip("剑的 Trigger Collider，运行时自动查找同物体上的 Collider")]
    [SerializeField] private Collider weaponCollider;

    private bool isControlling;
    private Quaternion defaultLocalRotation;
    private bool hasDefaultRotation;

    public float MinMouseDelta => minMouseDelta;

    void Awake()
    {
        if (weaponCollider == null)
        {
            weaponCollider = GetComponent<Collider>();
        }

        // 保存默认剑姿态，只用于松开左键后的回位。
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

        // 左键按住期间不主动追任何目标方向；旋转只在 ApplyMouseRotation 中按本帧鼠标位移发生。
        if (isControlling) return;

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
        SetHitboxActive(true);
    }

    /// <summary>
    /// 根据本帧鼠标移动方向立即产生旋转。
    /// 鼠标没有移动时 PlayerInputController 不会调用本方法，所以剑会立即停止。
    /// </summary>
    public void ApplyMouseRotation(Vector2 direction)
    {
        if (!isControlling || weaponPivot == null) return;
        if (direction.sqrMagnitude < 0.0001f) return;

        direction = direction.normalized;

        float speed = Mathf.Clamp(rotationSpeed, 180f, 1440f);
        float maxStep = speed * Time.deltaTime;

        // 连续方向映射，不做 8 方向量化。
        Quaternion step = Quaternion.Euler(
            -direction.y * maxStep,
            direction.x * maxStep,
            0f
        );

        weaponPivot.localRotation = weaponPivot.localRotation * step;
    }

    /// <summary>左键松开：停止鼠标控制，随后由 Update 平滑回默认姿态。</summary>
    public void EndControl()
    {
        if (!isControlling) return;

        isControlling = false;
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
