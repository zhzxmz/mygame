using UnityEngine;

public class WeaponController : MonoBehaviour
{
    public Transform weaponPivot;

    [Header("挥剑参数")]
    [Tooltip("鼠标移动量到剑旋转量的灵敏度。1 表示 1 像素约 1 度")]
    [SerializeField] private float rotationSensitivity = 1f;

    [Tooltip("松开左键后剑返回默认姿态的速度（度/秒）")]
    [SerializeField] private float returnSpeed = 360f;

    [Tooltip("剑的 Trigger Collider，运行时自动查找同物体上的 Collider")]
    [SerializeField] private Collider weaponCollider;

    [Tooltip("剑的 Renderer，留空会自动查找自身及子物体的 Renderer")]
    [SerializeField] private Renderer[] swordRenderers;

    private const float ReturnCompleteAngle = 0.1f;

    private bool isControlling;
    private Quaternion defaultLocalRotation;
    private bool hasDefaultRotation;

    // 鼠标方向参考系的固定 Transform，优先使用主相机。
    private Transform referenceFrame;

    void Awake()
    {
        if (weaponCollider == null)
        {
            weaponCollider = GetComponent<Collider>();
        }

        // 自动查找剑自身的 Renderer（包括子物体），用于非攻击状态隐藏。
        if (swordRenderers == null || swordRenderers.Length == 0)
        {
            swordRenderers = GetComponentsInChildren<Renderer>(true);
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

        // 默认状态：剑隐藏，命中判定关闭。
        SetSwordVisible(false);
        SetHitboxActive(false);
    }

    void Update()
    {
        if (weaponPivot == null || !hasDefaultRotation) return;

        // 控制期间不保存目标姿态，也不主动追任何方向。
        // 旋转只由 ApplyMouseRotation 在当前帧发生；鼠标不动就完全不旋转。
        if (isControlling) return;

        // 松开左键后平滑回到默认姿态。
        float returnSpd = Mathf.Max(0f, returnSpeed);
        if (returnSpd > 0f)
        {
            weaponPivot.localRotation = Quaternion.RotateTowards(
                weaponPivot.localRotation,
                defaultLocalRotation,
                returnSpd * Time.deltaTime
            );
        }

        // 确认已经接近默认姿态后，吸附并隐藏剑。
        if (Quaternion.Angle(weaponPivot.localRotation, defaultLocalRotation) < ReturnCompleteAngle)
        {
            weaponPivot.localRotation = defaultLocalRotation;
            SetSwordVisible(false);
        }
    }

    /// <summary>左键按下：开始持续控制剑。</summary>
    public void BeginControl()
    {
        if (isControlling) return;

        isControlling = true;
        SetSwordVisible(true);
        SetHitboxActive(true);
    }

    /// <summary>
    /// 根据本帧鼠标位移量立即产生旋转。
    /// mouseDelta 为 0 时不旋转；mouseDelta 很小则产生很小的旋转。
    /// 旋转轴来自固定参考系（相机），而不是剑自身旋转后的 local axes。
    /// </summary>
    public void ApplyMouseRotation(Vector2 mouseDelta)
    {
        if (!isControlling || weaponPivot == null) return;
        if (mouseDelta.sqrMagnitude <= 0f) return;

        // 鼠标位移量 × rotationSensitivity = 本帧旋转角度。
        Quaternion step = Quaternion.Euler(
            -mouseDelta.y * rotationSensitivity,
            mouseDelta.x * rotationSensitivity,
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

    private void SetSwordVisible(bool visible)
    {
        if (swordRenderers == null) return;

        for (int i = 0; i < swordRenderers.Length; i++)
        {
            if (swordRenderers[i] != null)
            {
                swordRenderers[i].enabled = visible;
            }
        }
    }
}
