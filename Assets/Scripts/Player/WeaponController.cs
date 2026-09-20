using UnityEngine;

public class WeaponController : MonoBehaviour
{
    public Transform weaponPivot;

    [Header("挥砍参数")]
    [Tooltip("最小鼠标滑动距离（屏幕像素）")]
    [SerializeField] private float minSwipeDistance = 20f;

    [Tooltip("剑的旋转速度（度/秒）")]
    [SerializeField, Range(180f, 1440f)] private float rotationSpeed = 720f;

    [Tooltip("一次挥砍的持续时间（秒）")]
    [SerializeField] private float attackDuration = 0.25f;

    [Tooltip("剑的 Trigger Collider，运行时自动查找同物体上的 Collider")]
    [SerializeField] private Collider weaponCollider;

    private bool isAttacking;
    private float attackTimer;
    private Quaternion targetRotation;

    public float MinSwipeDistance => minSwipeDistance;
    public bool IsAttacking => isAttacking;

    void Awake()
    {
        if (weaponCollider == null)
        {
            weaponCollider = GetComponent<Collider>();
        }

        // 非攻击阶段关闭命中判定，攻击开始时再启用。
        SetHitboxActive(false);
    }

    void Update()
    {
        if (!isAttacking) return;
        if (weaponPivot == null) return;

        attackTimer += Time.deltaTime;

        float speed = Mathf.Clamp(rotationSpeed, 180f, 1440f);
        weaponPivot.localRotation = Quaternion.RotateTowards(
            weaponPivot.localRotation,
            targetRotation,
            speed * Time.deltaTime
        );

        if (attackTimer >= attackDuration)
        {
            isAttacking = false;
            SetHitboxActive(false);
        }
    }

    /// <summary>
    /// 根据鼠标滑动方向开始一次挥砍。
    /// </summary>
    public void StartSwipeAttack(Vector2 swipeDirection)
    {
        if (isAttacking) return;
        if (weaponPivot == null) return;
        if (swipeDirection.sqrMagnitude < 0.0001f) return;

        Vector2 direction = swipeDirection.normalized;

        // 连续方向映射：不量化成 8 方向，直接用滑动方向的 x/y 分量决定剑的目标俯仰/偏航。
        float targetX = -direction.y * 90f;
        float targetY = direction.x * 90f;
        targetRotation = Quaternion.Euler(targetX, targetY, 0f);

        attackTimer = 0f;
        isAttacking = true;
        SetHitboxActive(true);
    }

    private void SetHitboxActive(bool active)
    {
        if (weaponCollider != null)
        {
            weaponCollider.enabled = active;
        }
    }
}
