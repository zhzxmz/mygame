using UnityEngine;

[RequireComponent(typeof(WorldItem))]
public class WorldItemPickup : MonoBehaviour
{
    public InventoryManager inventory;
    public KeyCode pickupKey = KeyCode.E;
    public bool destroyOnPickup = true;

    private WorldItem worldItem;
    private bool playerInRange;

    void Awake()
    {
        worldItem = GetComponent<WorldItem>();

        if (inventory == null)
        {
            inventory = FindObjectOfType<InventoryManager>();
        }
    }

    void Update()
    {
        if (!playerInRange) return;
        if (!Input.GetKeyDown(pickupKey)) return;

        TryPickup();
    }

    void OnTriggerEnter(Collider other)
    {
        if (IsPlayer(other))
        {
            playerInRange = true;
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (IsPlayer(other))
        {
            playerInRange = false;
        }
    }

    private bool IsPlayer(Collider other)
    {
        return other.GetComponentInParent<MovementController>() != null;
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
