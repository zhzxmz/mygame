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

    // 鼠标方向参考系的固定 Transform，优先使用主相机。
    private Transform referenceFrame;

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

        // 鼠标输入必须基于固定参考系，不能基于剑自身旋转后的局部轴。
        // 优先使用玩家 Camera；找不到主相机时退化为 WeaponPivot 的父级（玩家根节点）。
        if (Camera.main != null)
        {
            referenceFrame = Camera.main.transform;
        }
        else if (weaponPivot != null)
        {
            referenceFrame = weaponPivot.parent;
        }

        // 非控制阶段关闭命中判定，左键按住期间开启。
        SetHitboxActive(false);
    }

    void Update()
    {
        if (weaponPivot == null || !hasDefaultRotation) return;

        // 控制期间不保存 targetRotation，也不主动追目标方向。
        // 旋转只由 ApplyMouseRotation 在当前帧发生；鼠标不动就完全不旋转。
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
    /// 旋转轴来自固定参考系（相机），而不是剑自身旋转后的 local axes。
    /// 鼠标没有移动时 PlayerInputController 不会调用本方法，所以剑会立即停止。
    /// </summary>
    public void ApplyMouseRotation(Vector2 direction)
    {
        if (!isControlling || weaponPivot == null) return;
        if (direction.sqrMagnitude < 0.0001f) return;

        direction = direction.normalized;

        float speed = Mathf.Clamp(rotationSpeed, 180f, 1440f);
        float maxStep = speed * Time.deltaTime;

        // step 定义在固定参考系的局部坐标中：X 对应上下，Y 对应左右。
        Quaternion step = Quaternion.Euler(
            -direction.y * maxStep,
            direction.x * maxStep,
            0f
        );

        if (referenceFrame != null)
        {
            // 把参考系局部旋转换算到世界空间，再作用到剑当前世界旋转上。
            Quaternion referenceWorld = referenceFrame.rotation;
            Quaternion stepWorld = referenceWorld * step * Quaternion.Inverse(referenceWorld);

            weaponPivot.rotation = stepWorld * weaponPivot.rotation;
        }
        else
        {
            // 极端兜底：没有相机参考系时使用父级固定坐标系，绝不使用剑自身局部轴。
            weaponPivot.localRotation = step * weaponPivot.localRotation;
        }
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
