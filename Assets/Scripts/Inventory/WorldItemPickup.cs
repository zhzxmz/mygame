using UnityEngine;

[RequireComponent(typeof(WorldItem))]
public class WorldItemPickup : MonoBehaviour
{
    public InventoryManager inventory;
    public KeyCode pickupKey = KeyCode.E;
    public bool destroyOnPickup = true;

    [Tooltip("玩家与掉落物的拾取距离")]
    public float pickupRange = 2f;

    private WorldItem worldItem;
    private Transform player;

    void Awake()
    {
        worldItem = GetComponent<WorldItem>();

        if (inventory == null)
        {
            inventory = FindObjectOfType<InventoryManager>();
        }

        MovementController controller = FindObjectOfType<MovementController>();
        if (controller != null)
        {
            player = controller.transform;
        }
    }

    void Update()
    {
        if (player == null) return;
        if (!Input.GetKeyDown(pickupKey)) return;

        float distance = Vector3.Distance(transform.position, player.position);
        if (distance <= pickupRange)
        {
            TryPickup();
        }
    }

    private void TryPickup()
    {
        if (inventory == null)
        {
            Debug.LogWarning("WorldItemPickup: 没有找到 InventoryManager，无法拾取物品");
            return;
        }

        if (worldItem == null || worldItem.Item == null || worldItem.Count <= 0)
        {
            Debug.LogWarning("WorldItemPickup: WorldItem 或 Item 为空，无法拾取物品");
            return;
        }

        int added = inventory.AddItem(worldItem.Item, worldItem.Count);
        if (added <= 0)
        {
            return;
        }

        int remaining = worldItem.Count - added;
        if (remaining <= 0)
        {
            Destroy(gameObject);
        }
        else
        {
            worldItem.SetStack(new ItemStack(worldItem.Item, remaining));
        }
    }
}
